using System;
using System.Collections.Generic;
using Chassis.Interior;
using Chassis.Seating;
using Chassis.Vehicles;
using UnityEngine.UI;

namespace Chassis.Instruments
{
    /// <summary>
    /// Using the switches in first person: the view picks the nearest switch within <see cref="Controls.Reach"/> of the
    /// eyes, the switch gets an outline and a dot appears in the middle of the screen; the left mouse button clicks it
    /// and the mouse wheel turns a knob. Nothing happens in the chase view, and E stays the vanilla exit key.
    /// </summary>
    internal static class Cockpit
    {
        private static readonly Color OutlineColor = new Color(1f, 0.93f, 0.55f);
        private const float OutlineWidth = 0.003f;

        private static Controls.Bound _hover;
        private static GameObject _canvas, _dot;
        private static Material _outline, _zoneMat, _reachMat;
        private static bool _gateLogged;

        /// <summary>Hit zones and the reach are drawn (console <c>chassis zones</c>).</summary>
        internal static bool ShowZones;
        private static GameObject _reach;

        internal static Controls.Bound Hovered => _hover;

        internal static void Tick()
        {
            var rig = FirstPersonView.Active ? Occupants.Of(Player.Local) : null;
            var lv = rig != null && rig.Vehicle != null ? rig.Vehicle.GetComponent<LandVehicle>() : null;
            if (lv == null) { Hover(null); Reach(null); return; }

            Camera cam = PlayerSingleton<PlayerCamera>.InstanceExists ? PlayerSingleton<PlayerCamera>.Instance.Camera : null;
            if (cam == null) { Hover(null); return; }
            Vector3 origin = cam.transform.position, dir = cam.transform.forward;

            Controls.Bound best = null;
            float bestT = Controls.Reach;
            foreach (var b in Controls.Of(lv))
            {
                float t = Controls.Hit(b, origin, dir);
                if (t < 0f || t > bestT) continue;
                bestT = t;
                best = b;
            }
            Hover(best);
            if (ShowZones) { Zones(lv); Reach(rig); }

            if (best == null) return;
            if (GameInput.IsTyping || Cursor.lockState != CursorLockMode.Locked)
            {
                if (!_gateLogged) Core.Log.Msg("[Cockpit] a switch is in view but the cursor is free or a text field has focus - clicks wait.");
                _gateLogged = true;
                return;
            }
            _gateLogged = false;
            if (GameInput.GetButtonDown(GameInput.ButtonCode.PrimaryClick)) Controls.Click(best);
            float scroll = GameInput.MouseScrollDelta;
            if (best.Kind == "knob" && Mathf.Abs(scroll) > 0.01f) Controls.Scroll(best, scroll > 0f ? 1 : -1);
        }

        /// <summary>Clicks the switch in view as the mouse would (debug console).</summary>
        internal static string ClickHovered()
        {
            if (_hover == null) return "no switch in view";
            Controls.Click(_hover);
            return "clicked '" + _hover.P.Name + "'";
        }

        internal static string ScrollHovered(int notches)
        {
            if (_hover == null) return "no switch in view";
            if (_hover.Kind != "knob") return "'" + _hover.P.Name + "' is a " + _hover.Kind + ", not a knob";
            Controls.Scroll(_hover, notches);
            return "turned '" + _hover.P.Name + "' by " + notches;
        }

        // ---- outline and dot ---------------------------------------------------------------------------------------

        private static void Hover(Controls.Bound b)
        {
            if (b == _hover) return;
            if (_hover != null) foreach (var go in _hover.Outline) if (go != null) go.SetActive(false);
            _hover = b;
            if (b != null)
            {
                if (!b.OutlineMade) MakeOutline(b);
                foreach (var go in b.Outline) if (go != null) go.SetActive(true);
            }
            Dot(b != null);
        }

        /// <summary>
        /// A copy of each mesh of the switch, a few millimetres larger around its centre and drawn from the inside in a
        /// flat light colour: the switch shows a rim where the copy sticks out.
        /// </summary>
        private static void MakeOutline(Controls.Bound b)
        {
            b.OutlineMade = true;
            try
            {
                var mat = OutlineMaterial();
                foreach (var mf in b.T.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf == null || mf.sharedMesh == null || mf.name.StartsWith("Chassis_Outline", StringComparison.Ordinal) || mf.name.StartsWith("Chassis_Zone", StringComparison.Ordinal)) continue;
                    Bounds mb = mf.sharedMesh.bounds;
                    Vector3 lossy = mf.transform.lossyScale;
                    Vector3 s = new Vector3(Grow(mb.size.x, lossy.x), Grow(mb.size.y, lossy.y), Grow(mb.size.z, lossy.z));
                    var go = new GameObject("Chassis_Outline");
                    go.transform.SetParent(mf.transform, false);
                    go.transform.localScale = s;
                    go.transform.localPosition = mb.center - Vector3.Scale(mb.center, s);
                    go.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                    var r = go.AddComponent<MeshRenderer>();
                    var mats = new Material[Mathf.Max(1, mf.sharedMesh.subMeshCount)];
                    for (int i = 0; i < mats.Length; i++) mats[i] = mat;
                    r.sharedMaterials = mats;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    go.SetActive(false);
                    b.Outline.Add(go);
                }
                if (b.Outline.Count == 0) Core.Log.Msg("[Cockpit] switch '" + b.P.Name + "' has no mesh to outline - only the dot shows it.");
            }
            catch (Exception e) { Core.Log.Error("[Cockpit] the outline of switch '" + b.P.Name + "' could not be made, only the dot shows it: " + e); }
        }

        private static float Grow(float size, float scale)
        {
            float world = Mathf.Abs(size * scale);
            return world > 1e-4f ? (world + 2f * OutlineWidth) / world : 1f;
        }

        private static Material OutlineMaterial()
        {
            if (_outline != null) return _outline;
            _outline = Unlit("Chassis_Outline", OutlineColor);
            _outline.SetFloat("_Cull", 1f); // front: only the rim around the switch shows
            return _outline;
        }

        private static Material Unlit(string name, Color c)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) { Core.Log.Error("[Cockpit] URP/Unlit shader not found - " + name + " uses URP/Lit."); shader = Shader.Find("Universal Render Pipeline/Lit"); }
            var mat = new Material(shader) { name = name };
            mat.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            mat.SetColor("_BaseColor", c);
            return mat;
        }

        private static void Dot(bool on)
        {
            if (on && _dot == null) MakeDot();
            if (_dot != null) _dot.SetActive(on);
        }

        private static void MakeDot()
        {
            try
            {
                _canvas = new GameObject("Chassis_CockpitDot");
                UnityEngine.Object.DontDestroyOnLoad(_canvas);
                var canvas = _canvas.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 500;
                _dot = new GameObject("Dot");
                _dot.transform.SetParent(_canvas.transform, false);
                var rt = _dot.AddComponent<RectTransform>();
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(7f, 7f);
                var img = _dot.AddComponent<Image>();
                img.sprite = Circle();
                img.color = new Color(1f, 1f, 1f, 0.92f);
                img.raycastTarget = false;
                Core.Log.Msg("[Cockpit] centre dot made.");
            }
            catch (Exception e)
            {
                Core.Log.Error("[Cockpit] the centre dot could not be made, switches show only their outline: " + e);
                if (_canvas != null) UnityEngine.Object.Destroy(_canvas);
                _canvas = null; _dot = null;
            }
        }

        /// <summary>A white disc with a soft edge and a dark rim, so it reads on bright and dark backgrounds.</summary>
        private static Sprite Circle()
        {
            const int n = 32;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "Chassis_Dot" };
            tex.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            float c = (n - 1) * 0.5f, r = n * 0.5f - 1f;
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c));
                    float a = Mathf.Clamp01(r - d);
                    float rim = Mathf.Clamp01((d - (r - 5f)) / 2f);
                    float v = Mathf.Lerp(1f, 0.15f, rim);
                    tex.SetPixel(x, y, new Color(v, v, v, a));
                }
            tex.Apply();
            var sprite = Sprite.Create(tex, new Rect(0f, 0f, n, n), new Vector2(0.5f, 0.5f), 100f);
            sprite.hideFlags |= HideFlags.DontUnloadUnusedAsset;
            return sprite;
        }

        // ---- hit zones ---------------------------------------------------------------------------------------------

        internal static string SetZones(bool on)
        {
            ShowZones = on;
            int n = 0;
            foreach (var b in Controls.AllBound())
            {
                if (b.Zone != null) { b.Zone.SetActive(on); n++; }
            }
            if (!on && _reach != null) _reach.SetActive(false);
            Core.Log.Msg("[Cockpit] hit zones " + (on ? "shown" : "hidden") + ".");
            return on ? "hit zones on: green boxes are what the view must hit, the yellow rings are the reach (" + Controls.Reach.ToString("0.0") + " m from the eyes). Sit in a car and press V." : "hit zones off";
        }

        private static void Zones(LandVehicle lv)
        {
            foreach (var b in Controls.Of(lv))
            {
                if (b.Zone != null) { if (!b.Zone.activeSelf) b.Zone.SetActive(true); continue; }
                if (b.T == null) continue;
                try
                {
                    var m = new KitMesh();
                    Vector3 h = b.BoxSize * 0.5f, c = b.BoxCenter;
                    float scale = Mathf.Max(Mathf.Abs(b.T.lossyScale.x), 1e-4f);
                    float w = 0.0015f / scale;
                    for (int axis = 0; axis < 3; axis++)
                        for (int i = 0; i < 4; i++)
                        {
                            Vector3 p = c, size = new Vector3(w, w, w);
                            int a1 = (axis + 1) % 3, a2 = (axis + 2) % 3;
                            p[a1] += (i & 1) == 0 ? -h[a1] : h[a1];
                            p[a2] += (i & 2) == 0 ? -h[a2] : h[a2];
                            size[axis] = b.BoxSize[axis] + w;
                            m.Box(p, size);
                        }
                    var go = new GameObject("Chassis_Zone");
                    go.transform.SetParent(b.T, false);
                    go.AddComponent<MeshFilter>().sharedMesh = m.Build("Chassis_Zone");
                    var r = go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = _zoneMat != null ? _zoneMat : (_zoneMat = Unlit("Chassis_Zone", new Color(0.2f, 1f, 0.35f)));
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    b.Zone = go;
                }
                catch (Exception e) { Core.Log.Error("[Cockpit] the hit zone of '" + b.P.Name + "' could not be drawn: " + e.Message); b.Zone = new GameObject("Chassis_Zone_Failed"); b.Zone.transform.SetParent(b.T, false); }
            }
        }

        /// <summary>Three rings of the reach radius around the local player's eyes.</summary>
        private static void Reach(SeatRig rig)
        {
            if (rig == null || !ShowZones || rig.Frame == null || rig.Profile == null)
            {
                if (_reach != null && _reach.activeSelf) _reach.SetActive(false);
                return;
            }
            try
            {
                if (_reach == null)
                {
                    _reach = new GameObject("Chassis_Reach");
                    var mesh = new KitMesh().Ring(Controls.Reach, 0.002f, 48, 4).Build("Chassis_Reach");
                    foreach (var rot in new[] { Quaternion.identity, Quaternion.Euler(90f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f) })
                    {
                        var ring = new GameObject("Ring");
                        ring.transform.SetParent(_reach.transform, false);
                        ring.transform.localRotation = rot;
                        ring.AddComponent<MeshFilter>().sharedMesh = mesh;
                        var r = ring.AddComponent<MeshRenderer>();
                        r.sharedMaterial = _reachMat != null ? _reachMat : (_reachMat = Unlit("Chassis_Reach", new Color(1f, 0.85f, 0.2f)));
                        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    }
                }
                if (_reach.transform.parent != rig.Frame) _reach.transform.SetParent(rig.Frame, false);
                _reach.transform.localPosition = rig.Profile.Eyes;
                _reach.transform.localRotation = Quaternion.identity;
                _reach.transform.localScale = Vector3.one;
                if (!_reach.activeSelf) _reach.SetActive(true);
            }
            catch (Exception e) { Core.Log.Error("[Cockpit] the reach rings could not be drawn: " + e.Message); ShowZones = false; }
        }

        internal static void Reset()
        {
            _hover = null;
            if (_dot != null) _dot.SetActive(false);
            if (_reach != null) UnityEngine.Object.Destroy(_reach);
            _reach = null;
            _gateLogged = false;
        }
    }
}
