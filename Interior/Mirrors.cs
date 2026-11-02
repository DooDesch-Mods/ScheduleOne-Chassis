using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Chassis.Interior
{
    /// <summary>
    /// The live mirrors of the car the local player sits in: the rear-view mirror and the two side mirrors. Per mirror a
    /// second camera stands at the driver's eye mirrored in the glass and renders through the glass as a window (an
    /// off-axis frustum after Kooima), so the picture moves with the head like a real mirror. The glass is a quad on the
    /// back of the housing <see cref="BaseInterior"/> builds, showing that picture flipped left to right.
    /// A camera renders only while its mirror is on screen and faces the camera; when it stops, the glass keeps its last
    /// picture. Resolution and rate follow the <see cref="Tune.Reflections"/> level; the side mirrors take turns. The own
    /// car is hidden from the mirrors unless <c>[Chassis] MirrorShowsOwnCar</c> is on; the rear mirror keeps the kit inside
    /// it, the side mirrors hide it with the body.
    /// </summary>
    internal static class Mirrors
    {
        /// <summary>Vehicle codes that get live mirrors.</summary>
        internal static readonly HashSet<string> Codes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "shitbox" };
        internal const string RearName = "Chassis_RearMirror", SideLName = "Chassis_SideMirrorL", SideRName = "Chassis_SideMirrorR";
        private const string CameraName = "Chassis_MirrorCamera";
        /// <summary>The rear-view housing: width, height, depth in metres.</summary>
        internal static readonly Vector3 RearSize = new Vector3(0.24f, 0.07f, 0.025f);
        /// <summary>The glass inside the housing's back face, and how far it stands off it.</summary>
        private const float GlassInset = 0.008f, GlassGap = 0.001f;

        /// <summary>Picture width per <see cref="Tune.Reflections"/> level; the height follows the housing.</summary>
        private static readonly int[] RearWidths = { 256, 384, 512, 768 };
        private static readonly int[] SideWidths = { 128, 192, 256, 320 };

        /// <summary>One mirror: its housing in the kit, its camera and picture.</summary>
        private sealed class View
        {
            internal readonly string Name, Label;
            internal readonly bool Side, Left;
            internal Transform Housing;
            internal bool Found;
            internal Vector3 Size = RearSize;
            internal Camera Cam;
            internal UniversalAdditionalCameraData Data;
            internal RenderTexture Rt;
            internal Material Mat;
            internal MeshRenderer Glass;
            internal int Width, Level = -1, Renders;
            internal string State = "idle";
            internal bool First;

            internal View(string name, string label, bool side, bool left) { Name = name; Label = label; Side = side; Left = left; }
            internal float GlassZ => -Size.z * 0.5f - GlassGap;
            internal string GlassName => Name + "Glass";
        }

        private static readonly List<View> _views = new List<View>
        {
            new View(RearName, "rear", false, false),
            new View(SideLName, "left", true, true),
            new View(SideRName, "right", true, false),
        };

        private static MelonPreferences_Entry<bool> _showOwnCar;
        private static bool _enabled = true;

        private static LandVehicle _car;
        /// <summary>The car's body renderers and the Chassis kit inside it, each with its own forceRenderingOff.</summary>
        private static readonly List<KeyValuePair<Renderer, bool>> _carRenderers = new List<KeyValuePair<Renderer, bool>>();
        private static readonly List<KeyValuePair<Renderer, bool>> _kitRenderers = new List<KeyValuePair<Renderer, bool>>();
        private static bool _collected, _hooked, _carHidden, _kitHidden, _hookFailed, _noMirrorLogged, _setupFailed;
#if IL2CPP
        /// <summary>Held so the garbage collector keeps the delegates the game calls.</summary>
        private static Il2CppSystem.Action<ScriptableRenderContext, Camera> _onBegin, _onEnd;
#endif

        /// <summary>The mirrors may render; off freezes them with their last picture.</summary>
        internal static bool Enabled
        {
            get => _enabled;
            set { _enabled = value; if (!value) StopAll("switched off"); Core.Log.Msg("[Mirror] " + (value ? "on" : "off") + "."); }
        }

        internal static bool ShowsOwnCar => _showOwnCar != null && _showOwnCar.Value;

        internal static void LoadSetting()
        {
            try
            {
                var cat = MelonPreferences.CreateCategory("Chassis");
                _showOwnCar = cat.CreateEntry("MirrorShowsOwnCar", false, "Own car in the mirror",
                    "true shows the own car's body in the mirrors; false (default) shows the street behind it.");
                Core.Log.Msg("[Mirror] own car in the mirror: " + (ShowsOwnCar ? "shown" : "hidden") + " (preference MirrorShowsOwnCar).");
            }
            catch (Exception e) { Core.Log.Warning("[Mirror] reading the MirrorShowsOwnCar preference failed - the own car stays hidden: " + e.Message); }
        }

        /// <summary>The <see cref="Tune.Reflections"/> level changed: the next frame builds the pictures at the new size.</summary>
        internal static void Restyle()
        {
            foreach (var v in _views) v.Level = -1;
            Core.Log.Msg("[Mirror] restyled for Reflections " + Tune.Reflections.Names[Tune.Reflections.Level] + ".");
        }

        internal static void Reset()
        {
            StopAll("world reset");
            foreach (var v in _views)
            {
                if (v.Cam != null) UnityEngine.Object.Destroy(v.Cam.gameObject);
                if (v.Rt != null) { v.Rt.Release(); UnityEngine.Object.Destroy(v.Rt); }
                v.Cam = null; v.Data = null; v.Rt = null;
                v.Housing = null; v.Found = false; v.Glass = null;
                v.Level = -1; v.Width = 0;
            }
            _car = null;
            _carRenderers.Clear(); _kitRenderers.Clear(); _collected = false;
            _noMirrorLogged = false; _setupFailed = false;
        }

        /// <summary>Every frame after the cars moved: decides which mirror cameras render this frame.</summary>
        internal static void LateTick()
        {
            if (!_enabled || _setupFailed) return;
            var player = Player.Local;
            if (player == null) { StopAll("no local player"); return; }
            var rig = Seating.Occupants.Of(player);
            if (rig == null || rig.IsPusher || rig.Vehicle == null) { StopAll("not seated"); return; }
            var lv = rig.Vehicle.GetComponent<LandVehicle>();
            if (lv == null) { StopAll("seat without a car"); return; }
            if (!FindHousings(lv)) return;

            var main = Camera.main;
            if (main == null) { StopAll("no main camera"); return; }
            try { Hook(); }
            catch (Exception e)
            {
                _setupFailed = true;
                StopAll("setup failed");
                Core.Log.Error("[Mirror] listening to camera renders failed - the mirrors stay off until the next world: " + e);
                return;
            }

            int level = Tune.Reflections.Level, frame = Time.frameCount;
            foreach (var v in _views)
            {
                if (v.Housing == null) { Stop(v, "no housing"); continue; }
                if (!EnsureView(v, main, level)) return;
                if (!Visible(v, main)) { Stop(v, "off screen"); continue; }
                if (!Due(v, level, frame)) { Stop(v, "rendering, skips this frame"); continue; }
                v.Cam.depth = main.depth - 1f;
                v.Cam.cullingMask = main.cullingMask & ~InvisibleMask();
                v.Cam.enabled = true;
                v.State = "rendering";
            }
        }

        /// <summary>The housings of the seated car; false when it has none.</summary>
        private static bool FindHousings(LandVehicle lv)
        {
            bool lost = false, any = false;
            foreach (var v in _views)
            {
                if (v.Found && v.Housing == null) lost = true;
                if (v.Found) any = true;
            }
            if (lv == _car && any && !lost) return true;

            _car = lv;
            _carRenderers.Clear(); _kitRenderers.Clear(); _collected = false;
            int sides = 0;
            any = false;
            foreach (var v in _views)
            {
                v.Housing = BaseInterior.Mirror(lv, v.Name);
                v.Found = v.Housing != null;
                v.Glass = null;
                v.Size = SizeOf(v);
                if (v.Found) { any = true; if (v.Side) sides++; }
            }
            if (!any)
            {
                if (!_noMirrorLogged) { _noMirrorLogged = true; Core.Log.Msg("[Mirror] " + Vehicles.Registry.Name(lv) + " has no mirror built - nothing to render."); }
                StopAll("car without a mirror");
                return false;
            }
            _noMirrorLogged = false;
            Core.Log.Msg("[Mirror] seated in " + Vehicles.Registry.Name(lv) + ": rear-view mirror " + (_views[0].Found ? "found" : "missing") + ", " + sides + " side mirror(s).");
            return true;
        }

        /// <summary>The housing box from its mesh; the rear size when the mesh cannot be read.</summary>
        private static Vector3 SizeOf(View v)
        {
            if (v.Housing == null) return RearSize;
            var mf = v.Housing.GetComponent<MeshFilter>();
            var mesh = mf != null ? mf.sharedMesh : null;
            if (mesh == null)
            {
                Core.Log.Warning("[Mirror] the " + v.Label + " mirror housing has no mesh - using the rear-view size.");
                return RearSize;
            }
            return mesh.bounds.size;
        }

        /// <summary>
        /// Whether a mirror renders this frame. The rear mirror renders every frame, on Low every second one. The side
        /// mirrors take turns: on Low each every fourth frame, on Medium and High each every second, on Ultra both every frame.
        /// </summary>
        private static bool Due(View v, int level, int frame)
        {
            if (!v.Side) return level != 0 || (frame & 1) == 0;
            switch (level)
            {
                case 0: return (frame & 3) == (v.Left ? 1 : 3);
                case 1:
                case 2: return (frame & 1) == (v.Left ? 0 : 1);
                default: return true;
            }
        }

        private static void Stop(View v, string why)
        {
            if (v.Cam != null && v.Cam.enabled) v.Cam.enabled = false;
            v.State = why;
        }

        private static void StopAll(string why)
        {
            foreach (var v in _views) Stop(v, why);
        }

        private static int InvisibleMask()
        {
            int layer = LayerMask.NameToLayer("Invisible");
            return layer >= 0 ? 1 << layer : 0;
        }

        /// <summary>The glass is inside the main camera's view and turned towards it.</summary>
        private static bool Visible(View v, Camera main)
        {
            Vector3 eye = main.transform.position;
            float z = v.GlassZ;
            Vector3 centre = v.Housing.TransformPoint(0f, 0f, z);
            if (Vector3.Dot(-v.Housing.forward, eye - centre) <= 0f) return false;
            float hw = v.Size.x * 0.5f, hh = v.Size.y * 0.5f;
            for (int i = 0; i < 4; i++)
            {
                var p = main.WorldToViewportPoint(v.Housing.TransformPoint((i & 1) == 0 ? -hw : hw, (i & 2) == 0 ? -hh : hh, z));
                if (p.z > 0f && p.x > -0.05f && p.x < 1.05f && p.y > -0.05f && p.y < 1.05f) return true;
            }
            return false;
        }

        /// <summary>Camera, texture and glass of one mirror for the current car and level; false when one cannot be made.</summary>
        private static bool EnsureView(View v, Camera main, int level)
        {
            try
            {
                if (v.Cam == null)
                {
                    var go = new GameObject(CameraName + "_" + v.Label);
                    UnityEngine.Object.DontDestroyOnLoad(go);
                    v.Cam = go.AddComponent<Camera>();
                    v.Cam.enabled = false;
                    v.Cam.allowMSAA = false;
                    v.Cam.allowHDR = true;
                    v.Cam.useOcclusionCulling = false;
                    v.Cam.clearFlags = main.clearFlags;
                    v.Cam.backgroundColor = main.backgroundColor;
                    v.Data = go.AddComponent<UniversalAdditionalCameraData>();
                    v.Data.renderPostProcessing = false;
                    v.Data.requiresDepthTexture = false;
                    v.Data.requiresColorTexture = false;
                    v.Data.antialiasing = AntialiasingMode.None;
                    v.Data.volumeLayerMask = 0;
                    v.Level = -1;
                    Core.Log.Msg("[Mirror] mirror camera made (" + v.Label + ").");
                }
                if (level != v.Level || v.Rt == null)
                {
                    int w = (v.Side ? SideWidths : RearWidths)[level];
                    if (v.Rt == null || v.Width != w)
                    {
                        if (v.Rt != null) { v.Rt.Release(); UnityEngine.Object.Destroy(v.Rt); }
                        var format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB111110Float) ? RenderTextureFormat.RGB111110Float : RenderTextureFormat.ARGB32;
                        int h = Mathf.Max(16, Mathf.RoundToInt(w * v.Size.y / v.Size.x));
                        v.Rt = new RenderTexture(w, h, 24, format) { name = "Chassis_MirrorRT_" + v.Label, antiAliasing = 1 };
                        v.Rt.Create();
                        v.Width = w;
                        Core.Log.Msg("[Mirror] picture " + v.Rt.width + "x" + v.Rt.height + " " + format + " (" + v.Label + ").");
                    }
                    v.Cam.targetTexture = v.Rt;
                    v.Data.renderShadows = level == 3;
                    // The mirror sees as far as the player's own view: same far plane, same per-layer cull distances.
                    v.Cam.farClipPlane = main.farClipPlane;
                    v.Cam.layerCullDistances = main.layerCullDistances;
                    v.Cam.layerCullSpherical = main.layerCullSpherical;
                    Core.Log.Msg("[Mirror] range " + v.Cam.farClipPlane.ToString("0") + " m, layer culling of the main camera (" + v.Label + ").");
                    if (v.Mat != null) SetPicture(v);
                    v.Level = level;
                    Core.Log.Msg("[Mirror] level " + Tune.Reflections.Names[level] + " (" + v.Label + "): " + v.Rt.width + "x" + v.Rt.height + (level == 3 ? ", with shadows" : "") + ".");
                }
                if (v.Glass == null) v.Glass = Glass(v);
                if (!_collected) CollectCar(_car);
                return true;
            }
            catch (Exception e)
            {
                _setupFailed = true;
                StopAll("setup failed");
                Core.Log.Error("[Mirror] setting up the " + v.Label + " mirror failed - the mirrors stay off until the next world: " + e);
                return false;
            }
        }

        private static void SetPicture(View v)
        {
            v.Mat.SetTexture("_BaseMap", v.Rt);
            v.Mat.mainTexture = v.Rt;
        }

        /// <summary>The glass quad on the back of the housing, facing the driver (local -z), flipped left to right.</summary>
        private static MeshRenderer Glass(View v)
        {
            var old = v.Housing.Find(v.GlassName);
            if (old != null) return old.GetComponent<MeshRenderer>();
            float hw = v.Size.x * 0.5f - GlassInset, hh = v.Size.y * 0.5f - GlassInset, z = v.GlassZ;
            var mesh = new Mesh { name = v.GlassName };
            mesh.vertices = new[] { new Vector3(-hw, -hh, z), new Vector3(hw, -hh, z), new Vector3(-hw, hh, z), new Vector3(hw, hh, z) };
            // The vertex on the housing's +x side shows the left edge of the camera picture: the mirror flip.
            mesh.uv = new[] { new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            mesh.triangles = new[] { 0, 2, 3, 0, 3, 1 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            var go = new GameObject(v.GlassName);
            go.layer = v.Housing.gameObject.layer;
            go.transform.SetParent(v.Housing, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            if (v.Mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) throw new InvalidOperationException("URP/Unlit shader not found");
                v.Mat = new Material(shader) { name = "Chassis_MirrorGlass_" + v.Label };
                v.Mat.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                v.Mat.SetColor("_BaseColor", new Color(0.92f, 0.92f, 0.92f));
                SetPicture(v);
            }
            r.sharedMaterial = v.Mat;
            Core.Log.Msg("[Mirror] glass added to the " + v.Label + " mirror of " + Vehicles.Registry.Name(_car) + ", " + (hw * 2f).ToString("F2") + " x " + (hh * 2f).ToString("F2") + " m.");
            return r;
        }

        /// <summary>The car's own renderers and the Chassis kit inside it, to hide from the mirrors.</summary>
        private static void CollectCar(LandVehicle lv)
        {
            _carRenderers.Clear(); _kitRenderers.Clear();
            _collected = true;
            Transform root = lv.transform;
            foreach (var r in lv.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                bool kit = false;
                for (Transform t = r.transform; t != null && t != root; t = t.parent)
                    if (t.name.StartsWith("Chassis_")) { kit = true; break; }
                (kit ? _kitRenderers : _carRenderers).Add(new KeyValuePair<Renderer, bool>(r, r.forceRenderingOff));
            }
            Core.Log.Msg("[Mirror] " + _carRenderers.Count + " body renderer(s) of " + Vehicles.Registry.Name(lv) + " hide from the mirrors unless MirrorShowsOwnCar is on; "
                + _kitRenderers.Count + " kit renderer(s) hide from the side mirrors with them.");
        }

        private static void Hook()
        {
            if (_hooked) return;
            _hooked = true;
#if IL2CPP
            _onBegin = new System.Action<ScriptableRenderContext, Camera>(BeforeCamera);
            _onEnd = new System.Action<ScriptableRenderContext, Camera>(AfterCamera);
            RenderPipelineManager.add_beginCameraRendering(_onBegin);
            RenderPipelineManager.add_endCameraRendering(_onEnd);
#else
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
            RenderPipelineManager.endCameraRendering += AfterCamera;
#endif
            Core.Log.Msg("[Mirror] listening to camera renders.");
        }

        private static View ViewOf(Camera cam)
        {
            if (cam == null) return null;
            foreach (var v in _views) if (v.Cam != null && v.Cam == cam) return v;
            return null;
        }

        /// <summary>Before a mirror camera renders: place it at the mirrored eye, frame the glass, hide the own car.</summary>
        private static void BeforeCamera(ScriptableRenderContext context, Camera cam)
        {
            var v = ViewOf(cam);
            if (v == null) return;
            try
            {
                var main = Camera.main;
                if (main == null || v.Housing == null) return;
                Frame(v, main.transform.position);
                if (!ShowsOwnCar)
                {
                    Hide(_carRenderers);
                    _carHidden = true;
                    // Without the body the seats and the dashboard would float in the side mirrors.
                    if (v.Side) { Hide(_kitRenderers); _kitHidden = true; }
                }
                v.Renders++;
                if (!v.First) { v.First = true; Core.Log.Msg("[Mirror] first mirror render (" + v.Label + ") from " + cam.transform.position.ToString("F2") + ", picture " + v.Rt.width + "x" + v.Rt.height + "."); }
            }
            catch (Exception e)
            {
                if (!_hookFailed) { _hookFailed = true; Core.Log.Error("[Mirror] placing the " + v.Label + " mirror camera failed - logged once: " + e.Message); }
            }
        }

        private static void AfterCamera(ScriptableRenderContext context, Camera cam)
        {
            if (!_carHidden && !_kitHidden) return;
            if (ViewOf(cam) == null) return;
            if (_carHidden) { Restore(_carRenderers); _carHidden = false; }
            if (_kitHidden) { Restore(_kitRenderers); _kitHidden = false; }
        }

        private static void Hide(List<KeyValuePair<Renderer, bool>> list)
        {
            foreach (var kv in list) if (kv.Key != null) kv.Key.forceRenderingOff = true;
        }

        private static void Restore(List<KeyValuePair<Renderer, bool>> list)
        {
            foreach (var kv in list) if (kv.Key != null) kv.Key.forceRenderingOff = kv.Value;
        }

        /// <summary>
        /// Off-axis projection through the glass as seen from the eye mirrored in it (Kooima, "Generalized Perspective
        /// Projection"). The camera looks out of the glass away from the driver; its near plane lies in the glass, so the
        /// housing and whatever stands behind it never show.
        /// </summary>
        private static void Frame(View v, Vector3 eye)
        {
            Transform housing = v.Housing;
            Vector3 vr = -housing.right, vu = housing.up;
            Vector3 vn = -Vector3.Cross(vr, vu);
            Vector3 centre = housing.TransformPoint(0f, 0f, v.GlassZ);
            Vector3 normal = -housing.forward;
            Vector3 pe = eye - 2f * Vector3.Dot(eye - centre, normal) * normal;

            float hw = v.Size.x * 0.5f - GlassInset, hh = v.Size.y * 0.5f - GlassInset;
            Vector3 pa = centre - vr * hw - vu * hh, pb = centre + vr * hw - vu * hh, pc = centre - vr * hw + vu * hh;
            Vector3 va = pa - pe, vb = pb - pe, vc = pc - pe;
            float d = -Vector3.Dot(va, vn);
            if (d < 0.01f) return;
            float n = Mathf.Max(0.01f, d - 0.002f), k = n / d;
            float l = Vector3.Dot(vr, va) * k, r = Vector3.Dot(vr, vb) * k;
            float b = Vector3.Dot(vu, va) * k, t = Vector3.Dot(vu, vc) * k;

            v.Cam.transform.SetPositionAndRotation(pe, Quaternion.LookRotation(-vn, vu));
            v.Cam.nearClipPlane = n;
            v.Cam.projectionMatrix = Matrix4x4.Frustum(l, r, b, t, n, v.Cam.farClipPlane);
        }

        internal static string Describe()
        {
            var rear = _views[0];
            string s = "mirror " + (_enabled ? "on" : "off") + ", " + rear.State
                + ", level " + Tune.Reflections.Names[Tune.Reflections.Level]
                + ", own car " + (ShowsOwnCar ? "shown" : "hidden")
                + ", car " + (_car != null ? Vehicles.Registry.Name(_car) : "-")
                + ", housing " + (rear.Housing != null ? rear.Housing.position.ToString("F2") : "-")
                + ", glass " + (rear.Glass != null ? "yes" : "no")
                + ", picture " + (rear.Rt != null ? rear.Rt.width + "x" + rear.Rt.height : "-");
#if DEBUG
            s += ", renders " + rear.Renders;
#endif
            if (rear.Cam != null) s += ", camera at " + rear.Cam.transform.position.ToString("F2");
            for (int i = 1; i < _views.Count; i++)
            {
                var v = _views[i];
                s += "; " + v.Label + " " + v.State
                    + ", glass " + (v.Glass != null ? "yes" : "no")
                    + ", picture " + (v.Rt != null ? v.Rt.width + "x" + v.Rt.height : "-");
#if DEBUG
                s += ", renders " + v.Renders;
#endif
            }
            return s;
        }

        /// <summary>The middle of a mirror's glass in world space (rear, left or right), or null without that mirror.</summary>
        internal static Vector3? GlassCentre(string which)
        {
            foreach (var v in _views)
                if (string.Equals(v.Label, which, StringComparison.OrdinalIgnoreCase))
                    return v.Housing != null ? v.Housing.TransformPoint(0f, 0f, v.GlassZ) : (Vector3?)null;
            return null;
        }

        /// <summary>Shows or hides the own car in the mirrors and keeps the choice.</summary>
        internal static void SetShowOwnCar(bool on)
        {
            if (_showOwnCar == null) { Core.Log.Warning("[Mirror] no preference entry - the own car setting is not kept."); return; }
            _showOwnCar.Value = on;
            Core.Log.Msg("[Mirror] own car in the mirror: " + (on ? "shown" : "hidden") + " (kept on the next preferences save).");
        }
    }
}
