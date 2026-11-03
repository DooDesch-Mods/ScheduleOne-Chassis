using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Chassis.Interior
{
    /// <summary>
    /// The live car mirrors: the rear-view mirror and the two side mirrors. Per mirror a second camera stands at the
    /// viewer's eye mirrored in the glass and renders through the glass as a window (an off-axis frustum after Kooima), so
    /// the picture moves with the head like a real mirror. The frustum is widened to a minimum angle, as a convex car
    /// mirror shows more than its flat size would. The glass is a quad on the back of the housing <see cref="BaseInterior"/>
    /// builds, showing that picture flipped left to right.
    /// <see cref="Tune.MirrorMode"/> decides which mirrors render: none, the rear-view mirror of the car the player sits in,
    /// its side mirrors too, or also the mirrors of up to three cars near the camera, on foot as well. Each car has its own
    /// rig of cameras and pictures. A camera renders only while its mirror is on screen and faces the camera. Resolution
    /// and rate follow the <see cref="Tune.Reflections"/> level; the side mirrors and the nearby cars take turns. Each rig
    /// hides its own car from its mirrors unless <c>[Chassis] MirrorShowsOwnCar</c> is on; the rear mirror keeps the kit
    /// inside it, the side mirrors hide it with the body.
    /// </summary>
    internal static class Mirrors
    {
        /// <summary>Vehicle codes that get live mirrors: every vanilla car the interior fits.</summary>
        internal static HashSet<string> Codes => BaseInterior.VanillaCodes;
        internal const string RearName = "Chassis_RearMirror", SideLName = "Chassis_SideMirrorL", SideRName = "Chassis_SideMirrorR";
        private const string CameraName = "Chassis_MirrorCamera";
        /// <summary>The rear-view housing: width, height, depth in metres.</summary>
        internal static readonly Vector3 RearSize = new Vector3(0.24f, 0.07f, 0.025f);
        /// <summary>The glass inside the housing's back face, and how far it stands off it.</summary>
        private const float GlassInset = 0.008f, GlassGap = 0.001f;
        /// <summary>Least horizontal angle a mirror shows, in degrees, like convex car mirrors.</summary>
        private const float RearMinFov = 32f, SideMinFov = 34f;
        /// <summary>Where a side mirror's centre looks, in degrees outwards and downwards from straight back; how near the
        /// eye must be to the glass (metres) to count as the driver's.</summary>
        private const float SideOutward = 15f, SideDown = 2f, AimMaxEye = 2.5f;
        /// <summary>Nearby cars: how many, how far from the camera (metres), how often they are picked again (seconds).</summary>
        private const int NearbyCars = 3;
        private const float NearbyRange = 15f, NearbyEvery = 0.5f;
        /// <summary>Car rigs kept with their cameras and pictures; beyond this the least recently used one is dropped.</summary>
        private const int MaxRigs = 6;

        /// <summary>Picture width per <see cref="Tune.Reflections"/> level; the height follows the housing.</summary>
        private static readonly int[] RearWidths = { 256, 384, 512, 768 };
        private static readonly int[] SideWidths = { 128, 192, 256, 320 };
        /// <summary>Mirror cameras of nearby cars that render in one frame, per <see cref="Tune.Reflections"/> level.</summary>
        private static readonly int[] NearbyPerFrame = { 1, 2, 2, 3 };

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
            internal float Widen = 1f;
            /// <summary>Side mirrors: the glass plane as a driver sets it, in housing space; set once from the seat.</summary>
            internal bool Aimed;
            internal Vector3 AimRight, AimUp, AimNormal;

            internal View(string name, string label, bool side, bool left) { Name = name; Label = label; Side = side; Left = left; }
            internal float GlassZ => -Size.z * 0.5f - GlassGap;
            internal string GlassName => Name + "Glass";
        }

        /// <summary>The mirrors of one car: three views and the car's renderers to hide from them.</summary>
        private sealed class Rig
        {
            internal readonly LandVehicle Car;
            internal readonly int Id;
            internal readonly string CarName;
            internal readonly List<View> Views = new List<View>
            {
                new View(RearName, "rear", false, false),
                new View(SideLName, "left", true, true),
                new View(SideRName, "right", true, false),
            };
            /// <summary>The car's body renderers and the Chassis kit inside it, each with its own forceRenderingOff.</summary>
            internal readonly List<KeyValuePair<Renderer, bool>> Body = new List<KeyValuePair<Renderer, bool>>();
            internal readonly List<KeyValuePair<Renderer, bool>> Kit = new List<KeyValuePair<Renderer, bool>>();
            internal bool Collected, Active;
            internal float Used;

            internal Rig(LandVehicle car) { Car = car; Id = car.GetInstanceID(); CarName = Vehicles.Registry.Name(car); }
            internal View Rear => Views[0];
        }

        private static readonly Dictionary<int, Rig> _rigs = new Dictionary<int, Rig>();
        private static readonly List<Rig> _nearby = new List<Rig>();
        private static readonly List<Rig> _scratch = new List<Rig>();
        private static readonly List<KeyValuePair<float, LandVehicle>> _near = new List<KeyValuePair<float, LandVehicle>>();
        private static readonly List<KeyValuePair<Rig, View>> _turns = new List<KeyValuePair<Rig, View>>();
        private static Rig _seated, _lastSeated, _hiddenRig;
        private static string _seatWhy = "not seated", _nearbyNames = "";
        private static float _nextPick;
        private static int _turn;

        private static MelonPreferences_Entry<bool> _showOwnCar;
        private static bool _enabled = true;
        private static bool _hooked, _carHidden, _kitHidden, _hookFailed, _noMirrorLogged, _setupFailed;
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
            foreach (var rig in _rigs.Values) foreach (var v in rig.Views) v.Level = -1;
            Core.Log.Msg("[Mirror] restyled for Reflections " + Tune.Reflections.Names[Tune.Reflections.Level] + ".");
        }

        /// <summary>The <see cref="Tune.MirrorMode"/> changed: cameras the mode no longer allows stop now, the glass follows next frame.</summary>
        internal static void ModeChanged()
        {
            int mode = Tune.MirrorMode.Mode;
            if (mode == Tune.MirrorMode.Off) StopAll("mirrors off by mode");
            if (mode != Tune.MirrorMode.Nearby && _nearby.Count > 0)
            {
                foreach (var rig in _nearby) StopRig(rig, "not nearby");
                _nearby.Clear();
                _nearbyNames = "";
            }
            _nextPick = 0f;
            Core.Log.Msg("[Mirror] applying mode " + Tune.MirrorMode.Names[mode] + " to " + _rigs.Count + " car rig(s).");
        }

        internal static void Reset()
        {
            StopAll("world reset");
            _scratch.Clear();
            _scratch.AddRange(_rigs.Values);
            foreach (var rig in _scratch) Drop(rig, null);
            _rigs.Clear();
            _nearby.Clear();
            _nearbyNames = "";
            _seated = null; _lastSeated = null; _hiddenRig = null;
            _carHidden = false; _kitHidden = false;
            _noMirrorLogged = false; _setupFailed = false;
            _nextPick = 0f;
        }

        // ---- per frame -------------------------------------------------------------------------------------------

        /// <summary>Every frame after the cars moved: decides which mirror cameras render this frame.</summary>
        internal static void LateTick()
        {
            if (_setupFailed) return;
            int mode = Tune.MirrorMode.Mode;
            Prune();
            foreach (var rig in _rigs.Values) rig.Active = false;

            var seated = SeatedRig();
            if (seated != _seated)
            {
                if (seated != null) Seat(seated);
                _seated = seated;
            }
            if (seated != null) { seated.Active = true; seated.Used = Time.unscaledTime; }
            if (mode == Tune.MirrorMode.Nearby) PickNearby(seated);
            foreach (var rig in _nearby) { rig.Active = true; rig.Used = Time.unscaledTime; }

            foreach (var rig in _rigs.Values) ShowGlass(rig, mode);
            foreach (var rig in _rigs.Values)
                if (!rig.Active) StopRig(rig, rig == _lastSeated ? _seatWhy : "not nearby");

            if (!_enabled) return;
            if (mode == Tune.MirrorMode.Off) { StopAll("mirrors off by mode"); return; }
            if (seated == null && _nearby.Count == 0) return;
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
            if (seated != null)
            {
                foreach (var v in seated.Views)
                {
                    if (v.Side && mode < Tune.MirrorMode.RearSide) { Stop(v, "side mirrors off by mode"); continue; }
                    if (!Ready(seated, v, main, level)) continue;
                    if (!Due(v, level, frame)) { Stop(v, "rendering, skips this frame"); continue; }
                    Start(v, main);
                }
            }
            if (_nearby.Count > 0) RunNearby(main, level);
        }

        /// <summary>The rig of the car the local player sits in, or null with the reason in <see cref="_seatWhy"/>.</summary>
        private static Rig SeatedRig()
        {
            var player = Player.Local;
            if (player == null) { _seatWhy = "no local player"; return null; }
            var occ = Seating.Occupants.Of(player);
            if (occ == null || occ.IsPusher || occ.Vehicle == null) { _seatWhy = "not seated"; return null; }
            var lv = occ.Vehicle.GetComponent<LandVehicle>();
            if (lv == null) { _seatWhy = "seat without a car"; return null; }
            if (!HasMirror(lv))
            {
                if (!_noMirrorLogged) { _noMirrorLogged = true; Core.Log.Msg("[Mirror] " + Vehicles.Registry.Name(lv) + " has no mirror built - nothing to render."); }
                _seatWhy = "car without a mirror";
                return null;
            }
            _noMirrorLogged = false;
            var rig = RigOf(lv);
            if (!Find(rig)) { _seatWhy = "car without a mirror"; return null; }
            return rig;
        }

        /// <summary>The player sat down in this car: its mirrors are aimed, sized and logged again from the seat.</summary>
        private static void Seat(Rig rig)
        {
            _lastSeated = rig;
            _nearby.Remove(rig);
            int sides = 0;
            foreach (var v in rig.Views)
            {
                v.Aimed = false;
                v.Level = -1;
                v.First = false;
                if (v.Side && v.Found) sides++;
            }
            rig.Collected = false;
            Core.Log.Msg("[Mirror] seated in " + rig.CarName + ": rear-view mirror " + (rig.Rear.Found ? "found" : "missing") + ", " + sides + " side mirror(s).");
        }

        /// <summary>Every <see cref="NearbyEvery"/> seconds: the cars with mirrors nearest the camera, without the seated one.</summary>
        private static void PickNearby(Rig seated)
        {
            float now = Time.unscaledTime;
            if (now < _nextPick) return;
            _nextPick = now + NearbyEvery;
            _nearby.Clear();
            var main = Camera.main;
            if (main == null) return;
            Vector3 eye = main.transform.position;
            _near.Clear();
            foreach (var lv in Vehicles.Registry.All)
            {
                if (lv == null || (seated != null && lv == seated.Car)) continue;
                float d = (lv.transform.position - eye).sqrMagnitude;
                if (d > NearbyRange * NearbyRange) continue;
                _near.Add(new KeyValuePair<float, LandVehicle>(d, lv));
            }
            _near.Sort((a, b) => a.Key.CompareTo(b.Key));
            string names = "";
            foreach (var kv in _near)
            {
                if (_nearby.Count >= NearbyCars) break;
                if (!HasMirror(kv.Value)) continue;
                var rig = RigOf(kv.Value);
                if (!Find(rig)) continue;
                _nearby.Add(rig);
                names += (names.Length > 0 ? ", " : "") + rig.CarName + " " + Mathf.Sqrt(kv.Key).ToString("F1") + " m";
            }
            string set = "";
            foreach (var rig in _nearby) set += rig.Id + ";";
            if (set != _nearbyNames)
            {
                _nearbyNames = set;
                Core.Log.Msg("[Mirror] nearby cars with mirrors: " + (names.Length > 0 ? names : "none") + ".");
            }
        }

        /// <summary>The views of nearby cars that are on screen take turns, a few per frame by the Reflections level.</summary>
        private static void RunNearby(Camera main, int level)
        {
            _turns.Clear();
            foreach (var rig in _nearby)
                foreach (var v in rig.Views)
                    if (Ready(rig, v, main, level)) _turns.Add(new KeyValuePair<Rig, View>(rig, v));
            if (_turns.Count == 0) return;
            int budget = NearbyPerFrame[level];
            _turn %= _turns.Count;
            for (int i = 0; i < _turns.Count; i++)
            {
                var v = _turns[(_turn + i) % _turns.Count].Value;
                if (i < budget) Start(v, main);
                else Stop(v, "rendering, waits its turn");
            }
            _turn = (_turn + budget) % _turns.Count;
        }

        /// <summary>The view has a housing on screen and its camera, picture and glass; stopped with the reason otherwise.</summary>
        private static bool Ready(Rig rig, View v, Camera main, int level)
        {
            if (v.Housing == null) { Stop(v, "no housing"); return false; }
            if (!v.Housing.gameObject.activeInHierarchy) { Stop(v, "housing hidden"); return false; }
            if (!EnsureView(rig, v, main, level)) return false;
            if (!Visible(v, main)) { Stop(v, "off screen"); return false; }
            return true;
        }

        private static void Start(View v, Camera main)
        {
            v.Cam.depth = main.depth - 1f;
            v.Cam.cullingMask = main.cullingMask & ~InvisibleMask();
            v.Cam.enabled = true;
            v.State = "rendering";
        }

        /// <summary>
        /// The glass shows a picture only where the mode lets the mirror render and once it has rendered: the seated car's
        /// rear mirror from Rear Only on, its side mirrors from Rear + Side on, nearby cars' mirrors with + Nearby Cars.
        /// </summary>
        private static void ShowGlass(Rig rig, int mode)
        {
            foreach (var v in rig.Views)
            {
                if (v.Glass == null) continue;
                bool allowed = rig == _seated
                    ? mode >= (v.Side ? Tune.MirrorMode.RearSide : Tune.MirrorMode.RearOnly)
                    : mode == Tune.MirrorMode.Nearby && rig.Active;
                bool on = allowed && v.Renders > 0;
                if (v.Glass.enabled != on) v.Glass.enabled = on;
            }
        }

        // ---- rigs ------------------------------------------------------------------------------------------------

        private static bool HasMirror(LandVehicle lv) =>
            BaseInterior.Mirror(lv, RearName) != null || BaseInterior.Mirror(lv, SideLName) != null || BaseInterior.Mirror(lv, SideRName) != null;

        private static Rig RigOf(LandVehicle lv)
        {
            int id = lv.GetInstanceID();
            if (_rigs.TryGetValue(id, out var rig)) return rig;
            if (_rigs.Count >= MaxRigs)
            {
                Rig oldest = null;
                foreach (var r in _rigs.Values)
                    if (r != _seated && !_nearby.Contains(r) && (oldest == null || r.Used < oldest.Used)) oldest = r;
                if (oldest != null) Drop(oldest, "least recently used");
            }
            rig = new Rig(lv) { Used = Time.unscaledTime };
            _rigs[id] = rig;
            Core.Log.Msg("[Mirror] mirror rig made for " + rig.CarName + " (" + _rigs.Count + " car rig(s)).");
            return rig;
        }

        /// <summary>Rigs of cars that are gone are dropped with their cameras and pictures.</summary>
        private static void Prune()
        {
            _scratch.Clear();
            foreach (var rig in _rigs.Values) if (rig.Car == null) _scratch.Add(rig);
            foreach (var rig in _scratch) Drop(rig, "the car is gone");
        }

        /// <summary>Destroys a rig's cameras, pictures and glass; <paramref name="why"/> null drops it without a log line.</summary>
        private static void Drop(Rig rig, string why)
        {
            StopRig(rig, "dropped");
            if (_hiddenRig == rig) { AfterCamera(default, null); }
            foreach (var v in rig.Views)
            {
                if (v.Cam != null) UnityEngine.Object.Destroy(v.Cam.gameObject);
                if (v.Rt != null) { v.Rt.Release(); UnityEngine.Object.Destroy(v.Rt); }
                if (v.Glass != null) UnityEngine.Object.Destroy(v.Glass.gameObject);
                if (v.Mat != null) UnityEngine.Object.Destroy(v.Mat);
                v.Cam = null; v.Data = null; v.Rt = null; v.Glass = null; v.Mat = null;
            }
            _rigs.Remove(rig.Id);
            _nearby.Remove(rig);
            if (_seated == rig) _seated = null;
            if (_lastSeated == rig) _lastSeated = null;
            if (why != null) Core.Log.Msg("[Mirror] dropped the mirror rig of " + rig.CarName + " (" + why + ").");
        }

        /// <summary>The housings of the rig's car; false when it has none.</summary>
        private static bool Find(Rig rig)
        {
            bool lost = false, any = false;
            foreach (var v in rig.Views)
            {
                if (v.Found && v.Housing == null) lost = true;
                if (v.Found) any = true;
            }
            if (any && !lost) return true;

            rig.Body.Clear(); rig.Kit.Clear(); rig.Collected = false;
            int sides = 0;
            any = false;
            foreach (var v in rig.Views)
            {
                v.Housing = BaseInterior.Mirror(rig.Car, v.Name);
                v.Found = v.Housing != null;
                v.Glass = null;
                v.Aimed = false;
                v.Size = SizeOf(v);
                // Other glass: the picture is sized again and its first render is logged again.
                v.Level = -1;
                v.First = false;
                if (v.Found) { any = true; if (v.Side) sides++; }
            }
            if (!any)
            {
                StopRig(rig, "car without a mirror");
                return false;
            }
            Core.Log.Msg("[Mirror] housings of " + rig.CarName + ": rear-view mirror " + (rig.Rear.Found ? "found" : "missing") + ", " + sides + " side mirror(s).");
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
        /// Whether a seated car's mirror renders this frame. The rear mirror renders every frame, on Low every second one.
        /// The side mirrors take turns: on Low each every fourth frame, on Medium and High each every second, on Ultra both
        /// every frame.
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

        private static void StopRig(Rig rig, string why)
        {
            foreach (var v in rig.Views) Stop(v, why);
        }

        private static void StopAll(string why)
        {
            foreach (var rig in _rigs.Values) StopRig(rig, why);
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

        /// <summary>Camera, texture and glass of one mirror for its car and the level; false when one cannot be made.</summary>
        private static bool EnsureView(Rig rig, View v, Camera main, int level)
        {
            try
            {
                if (v.Cam == null)
                {
                    var go = new GameObject(CameraName + "_" + v.Label + "_" + rig.Id);
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
                    Core.Log.Msg("[Mirror] mirror camera made (" + v.Label + ", " + rig.CarName + ").");
                }
                if (level != v.Level || v.Rt == null)
                {
                    int w = (v.Side ? SideWidths : RearWidths)[level];
                    int h = Mathf.Max(16, Mathf.RoundToInt(w * v.Size.y / v.Size.x));
                    if (v.Rt == null || v.Width != w || v.Rt.height != h)
                    {
                        if (v.Rt != null) { v.Rt.Release(); UnityEngine.Object.Destroy(v.Rt); }
                        var format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGB111110Float) ? RenderTextureFormat.RGB111110Float : RenderTextureFormat.ARGB32;
                        v.Rt = new RenderTexture(w, h, 24, format) { name = "Chassis_MirrorRT_" + v.Label + "_" + rig.Id, antiAliasing = 1 };
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
                if (v.Glass == null) v.Glass = Glass(rig, v);
                if (!rig.Collected) CollectCar(rig);
                return true;
            }
            catch (Exception e)
            {
                _setupFailed = true;
                StopAll("setup failed");
                Core.Log.Error("[Mirror] setting up the " + v.Label + " mirror of " + rig.CarName + " failed - the mirrors stay off until the next world: " + e);
                return false;
            }
        }

        private static void SetPicture(View v)
        {
            v.Mat.SetTexture("_BaseMap", v.Rt);
            v.Mat.mainTexture = v.Rt;
        }

        /// <summary>The glass quad on the back of the housing, facing the driver (local -z), flipped left to right.</summary>
        private static MeshRenderer Glass(Rig rig, View v)
        {
            var old = v.Housing.Find(v.GlassName);
            if (old != null)
            {
                var kept = old.GetComponent<MeshRenderer>();
                if (kept != null && v.Mat != null) kept.sharedMaterial = v.Mat;
                if (kept != null) return kept;
                UnityEngine.Object.Destroy(old.gameObject);
            }
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
            // Hidden until the camera has drawn a picture into it; ShowGlass turns it on.
            r.enabled = false;
            if (v.Mat == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) throw new InvalidOperationException("URP/Unlit shader not found");
                v.Mat = new Material(shader) { name = "Chassis_MirrorGlass_" + v.Label + "_" + rig.Id };
                v.Mat.hideFlags |= HideFlags.DontUnloadUnusedAsset;
                v.Mat.SetColor("_BaseColor", new Color(0.92f, 0.92f, 0.92f));
                SetPicture(v);
            }
            r.sharedMaterial = v.Mat;
            Core.Log.Msg("[Mirror] glass added to the " + v.Label + " mirror of " + rig.CarName + ", " + (hw * 2f).ToString("F2") + " x " + (hh * 2f).ToString("F2") + " m.");
            return r;
        }

        /// <summary>The car's own renderers and the Chassis kit inside it, to hide from its mirrors.</summary>
        private static void CollectCar(Rig rig)
        {
            rig.Body.Clear(); rig.Kit.Clear();
            rig.Collected = true;
            Transform root = rig.Car.transform;
            foreach (var r in rig.Car.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                bool kit = false;
                for (Transform t = r.transform; t != null && t != root; t = t.parent)
                    if (t.name.StartsWith("Chassis_")) { kit = true; break; }
                (kit ? rig.Kit : rig.Body).Add(new KeyValuePair<Renderer, bool>(r, r.forceRenderingOff));
            }
            Core.Log.Msg("[Mirror] " + rig.Body.Count + " body renderer(s) of " + rig.CarName + " hide from its mirrors unless MirrorShowsOwnCar is on; "
                + rig.Kit.Count + " kit renderer(s) hide from the side mirrors with them.");
        }

        // ---- rendering -------------------------------------------------------------------------------------------

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

        private static View ViewOf(Camera cam, out Rig owner)
        {
            owner = null;
            if (cam == null) return null;
            foreach (var rig in _rigs.Values)
                foreach (var v in rig.Views)
                    if (v.Cam != null && v.Cam == cam) { owner = rig; return v; }
            return null;
        }

        /// <summary>Before a mirror camera renders: place it at the mirrored eye, frame the glass, hide its own car.</summary>
        private static void BeforeCamera(ScriptableRenderContext context, Camera cam)
        {
            var v = ViewOf(cam, out var rig);
            if (v == null) return;
            try
            {
                var main = Camera.main;
                if (main == null || v.Housing == null) return;
                Frame(rig, v, main.transform.position);
                if (!ShowsOwnCar)
                {
                    Hide(rig.Body);
                    _carHidden = true;
                    _hiddenRig = rig;
                    // Without the body the seats and the dashboard would float in the side mirrors.
                    if (v.Side) { Hide(rig.Kit); _kitHidden = true; }
                }
                v.Renders++;
                if (!v.First) { v.First = true; Core.Log.Msg("[Mirror] first mirror render (" + v.Label + ") from " + cam.transform.position.ToString("F2") + ", picture " + v.Rt.width + "x" + v.Rt.height + ", widened x" + v.Widen.ToString("F2") + " to at least " + (v.Side ? SideMinFov : RearMinFov) + " deg" + (rig == _seated ? "" : ", " + rig.CarName) + "."); }
            }
            catch (Exception e)
            {
                if (!_hookFailed) { _hookFailed = true; Core.Log.Error("[Mirror] placing the " + v.Label + " mirror camera of " + rig.CarName + " failed - logged once: " + e.Message); }
            }
        }

        private static void AfterCamera(ScriptableRenderContext context, Camera cam)
        {
            if (!_carHidden && !_kitHidden) return;
            var rig = _hiddenRig;
            if (rig == null) { _carHidden = false; _kitHidden = false; return; }
            // A null camera comes from Drop: the rig goes away while its car is hidden.
            if (cam != null && ViewOf(cam, out _) == null) return;
            if (_carHidden) { Restore(rig.Body); _carHidden = false; }
            if (_kitHidden) { Restore(rig.Kit); _kitHidden = false; }
            _hiddenRig = null;
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
        /// Projection"). The camera looks out of the glass away from the viewer; its near plane lies in the glass, so the
        /// housing and whatever stands behind it never show.
        /// </summary>
        private static void Frame(Rig rig, View v, Vector3 eye)
        {
            Transform housing = v.Housing;
            Vector3 vr = -housing.right, vu = housing.up;
            Vector3 centre = housing.TransformPoint(0f, 0f, v.GlassZ);
            Vector3 normal = -housing.forward;
            if (v.Side)
            {
                // A nearby car's glass is set from its own driver's seat, not from where the player stands.
                if (!v.Aimed) Aim(rig, v, rig == _seated ? eye : BaseInterior.DriverEye(rig.Car) ?? eye, centre);
                vr = housing.TransformDirection(v.AimRight);
                vu = housing.TransformDirection(v.AimUp);
                normal = housing.TransformDirection(v.AimNormal);
            }
            Vector3 vn = -Vector3.Cross(vr, vu);
            Vector3 pe = eye - 2f * Vector3.Dot(eye - centre, normal) * normal;

            float hw = v.Size.x * 0.5f - GlassInset, hh = v.Size.y * 0.5f - GlassInset;
            Vector3 pa = centre - vr * hw - vu * hh, pb = centre + vr * hw - vu * hh, pc = centre - vr * hw + vu * hh;
            Vector3 va = pa - pe, vb = pb - pe, vc = pc - pe;
            float d = -Vector3.Dot(va, vn);
            if (d < 0.01f) return;
            float n = Mathf.Max(0.01f, d - 0.002f), k = n / d;
            float l = Vector3.Dot(vr, va) * k, r = Vector3.Dot(vr, vb) * k;
            float b = Vector3.Dot(vu, va) * k, t = Vector3.Dot(vu, vc) * k;

            // A flat glass seen from the seat spans only a few degrees (the right side mirror about 6). Real car mirrors
            // are convex: widen the frustum around its centre until it spans the mirror's minimum horizontal angle.
            float minHalf = Mathf.Tan((v.Side ? SideMinFov : RearMinFov) * 0.5f * Mathf.Deg2Rad) * n;
            float widen = Mathf.Max(1f, minHalf / ((r - l) * 0.5f));
            float cx = (l + r) * 0.5f, cy = (b + t) * 0.5f;
            l = cx + (l - cx) * widen; r = cx + (r - cx) * widen;
            b = cy + (b - cy) * widen; t = cy + (t - cy) * widen;
            v.Widen = widen;

            v.Cam.transform.SetPositionAndRotation(pe, Quaternion.LookRotation(-vn, vu));
            v.Cam.nearClipPlane = n;
            v.Cam.projectionMatrix = Matrix4x4.Frustum(l, r, b, t, n, v.Cam.farClipPlane);
        }

        /// <summary>
        /// Turns the side glass the way a driver sets it: the ray from the eye through the glass centre leaves level
        /// backwards, a little outwards past the own flank. The vanilla face itself points the view at the road. The plane
        /// is kept once the eye is near the seat (first person); from farther away (third person) it is only used for this frame.
        /// </summary>
        private static void Aim(Rig rig, View v, Vector3 eye, Vector3 centre)
        {
            Transform car = rig.Car.transform, housing = v.Housing;
            Vector3 toEye = (eye - centre).normalized;
            Vector3 outward = v.Left ? -car.right : car.right;
            Vector3 want = (-car.forward + outward * Mathf.Tan(SideOutward * Mathf.Deg2Rad) - car.up * Mathf.Tan(SideDown * Mathf.Deg2Rad)).normalized;
            Vector3 n = (toEye + want).normalized;
            Vector3 up = Vector3.ProjectOnPlane(car.up, n).normalized;
            Vector3 right = Vector3.Cross(up, n);
            v.AimNormal = housing.InverseTransformDirection(n);
            v.AimUp = housing.InverseTransformDirection(up);
            v.AimRight = housing.InverseTransformDirection(right);

            float far = (eye - centre).magnitude;
            if (far > AimMaxEye) return;
            v.Aimed = true;
            Vector3 before = Vector3.Reflect(-toEye, -housing.forward);
            float beforeDown = -Mathf.Asin(Mathf.Clamp(Vector3.Dot(before, car.up), -1f, 1f)) * Mathf.Rad2Deg;
            Core.Log.Msg("[Mirror] " + v.Label + " glass set like a driver would: " + SideOutward + " deg out, " + SideDown
                + " deg down, turned " + Vector3.Angle(n, -housing.forward).ToString("F1") + " deg off the body face (that face looked "
                + beforeDown.ToString("F1") + " deg down), eye " + far.ToString("F2") + " m away, " + rig.CarName + ".");
        }

        // ---- queries ---------------------------------------------------------------------------------------------

        /// <summary>The rig the queries describe: the seated car, else the last one sat in, else the nearest nearby car.</summary>
        private static Rig Focus => _seated ?? _lastSeated ?? (_nearby.Count > 0 ? _nearby[0] : null);

        private static string GlassState(View v) => v.Glass == null ? "no" : v.Glass.enabled ? "yes" : "hidden";

        internal static string Describe()
        {
            var rig = Focus;
            var rear = rig?.Rear;
            string s = "mirror " + (_enabled ? "on" : "off") + ", " + (rear != null ? rear.State : _seatWhy)
                + ", level " + Tune.Reflections.Names[Tune.Reflections.Level]
                + ", own car " + (ShowsOwnCar ? "shown" : "hidden")
                + ", car " + (rig != null && rig.Car != null ? rig.CarName : "-")
                + ", housing " + (rear?.Housing != null ? rear.Housing.position.ToString("F2") : "-")
                + ", glass " + (rear != null ? GlassState(rear) : "no")
                + ", picture " + (rear?.Rt != null ? rear.Rt.width + "x" + rear.Rt.height : "-");
#if DEBUG
            s += ", renders " + (rear != null ? rear.Renders : 0);
#endif
            if (rear?.Cam != null) s += ", camera at " + rear.Cam.transform.position.ToString("F2");
            s += ", mode " + Tune.MirrorMode.Names[Tune.MirrorMode.Mode] + ", nearby " + _nearby.Count;
            if (rig != null)
            {
                for (int i = 1; i < rig.Views.Count; i++)
                {
                    var v = rig.Views[i];
                    s += "; " + v.Label + " " + v.State
                        + ", glass " + GlassState(v)
                        + ", picture " + (v.Rt != null ? v.Rt.width + "x" + v.Rt.height : "-");
#if DEBUG
                    s += ", renders " + v.Renders;
#endif
                }
            }
            foreach (var near in _nearby)
            {
                s += "; nearby " + near.CarName;
                foreach (var v in near.Views)
                    s += (v == near.Rear ? " " : ", ") + v.Label + " " + v.State + " glass " + GlassState(v) + " renders " + v.Renders;
            }
            return s;
        }

        /// <summary>The middle of a mirror's glass in world space (rear, left or right) of the focus car, or null without that mirror.</summary>
        internal static Vector3? GlassCentre(string which)
        {
            var rig = Focus;
            if (rig == null) return null;
            foreach (var v in rig.Views)
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
