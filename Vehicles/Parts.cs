using System;
using System.Collections.Generic;
using System.Globalization;
using Chassis.Api;
using Chassis.Net;

namespace Chassis.Vehicles
{
    /// <summary>
    /// The parts of a profiled vehicle that move by themselves: the steering wheel follows the road wheels, lids open
    /// on request or with the storage, wipers sweep while it rains and the vehicle runs. Each part is a pivot at its
    /// hinge or axle that turns about one axis from its rest pose; this class only turns the pivots.
    ///
    /// The steering angle is vanilla's <c>CurrentSteerAngle</c>, a SyncVar, so the wheel turns on every peer.
    /// Lid requests go through the host to every peer.
    /// </summary>
    internal static class Parts
    {
        /// <summary>Steering wheels and wipers are not posed beyond this distance from the camera.</summary>
        private const float PoseRange = 80f;
        private const float RainPollSeconds = 1f;
        private const float RunningKmh = 2f;
        /// <summary>Degrees per second the steering wheel turns at most; the road wheels snap faster than hands could follow.</summary>
        private const float WheelSpeed = 200f;

        /// <summary>Debug: forces rain on (true) or off (false); null reads the weather.</summary>
        internal static bool? RainOverride;

        private sealed class Pivot
        {
            internal PartProfile P;
            internal Transform T;
            internal Quaternion Rest;
            internal Vector3 Axis;
            internal float Dir = 1f;
            internal float Open;      // lid: 0 closed .. 1 open
            internal float Phase;     // wiper: 0 .. 1 per stroke
            internal float Turned;    // steering wheel: degrees shown, before Dir
            internal bool TurnedSet;
            internal bool Logged;
        }

        private sealed class Rig
        {
            internal LandVehicle V;
            internal ChassisProfile Profile;
            internal Transform Frame;
            internal Pivot Steering;
            internal readonly List<Pivot> Lids = new List<Pivot>(), Wipers = new List<Pivot>();
            internal bool Wiping;
            internal float Rain, NextRain;
        }

        private static readonly Dictionary<int, Rig> _rigs = new Dictionary<int, Rig>();
        private static readonly Dictionary<int, HashSet<string>> _open = new Dictionary<int, HashSet<string>>();
        private static readonly Dictionary<int, bool> _storageFront = new Dictionary<int, bool>();
        private static bool _rainBroken;

        // ---- steering wheel, also for the hands --------------------------------------------------------------------

        /// <summary>
        /// The steering wheel of a vehicle: its pivot, its column axis in world space and how far it is turned right now,
        /// in degrees about that axis. False when the profile has no steering wheel or it is not in the model.
        /// </summary>
        internal static bool Wheel(LandVehicle lv, out Transform pivot, out Vector3 axis, out float angle)
        {
            pivot = null; axis = Vector3.forward; angle = 0f;
            var rig = RigOf(lv);
            if (rig?.Steering == null || rig.Steering.T == null) return false;
            var s = rig.Steering;
            pivot = s.T;
            axis = s.T.parent != null ? s.T.parent.rotation * (s.Rest * s.Axis) : s.Rest * s.Axis;
            angle = (s.TurnedSet ? s.Turned : SteerAngle(lv, rig.Profile.Steering)) * s.Dir;
            return true;
        }

        /// <summary>Moves the shown steering wheel angle toward the road wheels' at <see cref="WheelSpeed"/>.</summary>
        private static float Turn(Rig rig, LandVehicle lv, float dt)
        {
            var s = rig.Steering;
            float target = SteerAngle(lv, rig.Profile.Steering);
            s.Turned = s.TurnedSet ? Mathf.MoveTowards(s.Turned, target, WheelSpeed * dt) : target;
            s.TurnedSet = true;
            return s.Turned;
        }

        private static float SteerAngle(LandVehicle lv, SteeringProfile s) => Mathf.Clamp(lv.CurrentSteerAngle * s.Ratio, -s.Max, s.Max);

        // ---- requests ----------------------------------------------------------------------------------------------

        internal static void Request(LandVehicle lv, string part, bool open)
        {
            if (lv == null) { Core.Log.Warning("[Parts] '" + part + "' asked for a vehicle that is gone."); return; }
            if (ChassisApi.For(lv)?.Part(part) == null) { Core.Log.Warning("[Parts] " + Registry.Name(lv) + " has no part '" + part + "' in its profile."); return; }
            ChassisNet.ToHost("req.part", Ids.Of(lv).ToString(CultureInfo.InvariantCulture), part, open ? "1" : "0");
        }

        internal static void RequestStorageEnd(LandVehicle lv, bool front)
        {
            if (lv == null) { Core.Log.Warning("[Parts] storage end asked for a vehicle that is gone."); return; }
            ChassisNet.ToHost("req.side", Ids.Of(lv).ToString(CultureInfo.InvariantCulture), front ? "1" : "0");
        }

        internal static bool IsOpen(LandVehicle lv, string part) =>
            lv != null && _open.TryGetValue(Ids.Of(lv), out var set) && set.Contains(part.ToLowerInvariant());

        /// <summary>Everyone: a lid opened or closed by request.</summary>
        internal static void OnPart(int vehicle, string part, bool open)
        {
            if (!_open.TryGetValue(vehicle, out var set)) _open[vehicle] = set = new HashSet<string>();
            bool changed = open ? set.Add(part.ToLowerInvariant()) : set.Remove(part.ToLowerInvariant());
            if (changed) Core.Log.Msg("[Parts] net " + vehicle + ": " + part + (open ? " opened" : " closed") + " on request.");
        }

        /// <summary>Everyone: which end the vehicle's storage is opened from.</summary>
        internal static void OnStorageEnd(int vehicle, bool front)
        {
            _storageFront.TryGetValue(vehicle, out bool old);
            _storageFront[vehicle] = front;
            if (old != front) Core.Log.Msg("[Parts] net " + vehicle + ": storage opened at the " + (front ? "front" : "rear") + ".");
        }

        /// <summary>Host: every open lid and storage end, for a peer that joined late.</summary>
        internal static void HostReplay()
        {
            foreach (var kv in _open)
                foreach (string part in kv.Value)
                    ChassisNet.ToAll("part", kv.Key.ToString(CultureInfo.InvariantCulture), part, "1");
            foreach (var kv in _storageFront)
                if (kv.Value) ChassisNet.ToAll("side", kv.Key.ToString(CultureInfo.InvariantCulture), "1");
        }

        // ---- per frame ---------------------------------------------------------------------------------------------

        /// <summary>Every peer, from OnLateUpdate: after the wheels took their pose for the frame.</summary>
        internal static void LateTick()
        {
            if (Registry.Profiled.Count == 0) return;
            float now = Time.time, dt = Time.deltaTime;
            Camera cam = Camera.main;
            Vector3 eye = cam != null ? cam.transform.position : Vector3.zero;
            foreach (var lv in Registry.Profiled)
            {
                if (lv == null) continue;
                var rig = RigOf(lv);
                if (rig == null) continue;
                MoveLids(rig, dt);
                if (cam != null && (lv.transform.position - eye).sqrMagnitude > PoseRange * PoseRange) continue;
                if (rig.Steering != null && rig.Steering.T != null)
                    Pose(rig.Steering, Turn(rig, lv, dt));
                Wipe(rig, now, dt);
            }
        }

        /// <summary>Drops the rig of <paramref name="lv"/> so the next frame looks its parts up again, for parts added after it was built.</summary>
        internal static void Forget(LandVehicle lv)
        {
            if (lv == null || !_rigs.TryGetValue(lv.GetInstanceID(), out var rig)) return;
            if (rig != null) Restore(rig);
            _rigs.Remove(lv.GetInstanceID());
        }

        private static Rig RigOf(LandVehicle lv)
        {
            if (lv == null) return null;
            var profile = ChassisApi.For(lv);
            int key = lv.GetInstanceID();
            if (_rigs.TryGetValue(key, out var rig) && rig != null && rig.Profile == profile && rig.Frame != null) return rig;
            if (rig != null) Restore(rig);
            if (profile == null) { _rigs.Remove(key); return null; }
            rig = Build(lv, profile);
            _rigs[key] = rig;
            return rig;
        }

        private static Rig Build(LandVehicle lv, ChassisProfile profile)
        {
            var rig = new Rig { V = lv, Profile = profile, Frame = Frames.Resolve(lv.transform, profile.Frame) };
            if (rig.Frame == null)
            {
                Core.Log.Warning("[Parts] " + Registry.Name(lv) + ": frame '" + profile.Frame + "' not found - nothing moves on it yet.");
                return rig;
            }
            var missing = new List<string>();
            if (profile.Steering != null && !string.IsNullOrEmpty(profile.Steering.Path))
            {
                var p = MakePivot(rig, profile.Steering.Path, profile.Steering.Axis);
                if (p == null) missing.Add("steering wheel '" + profile.Steering.Path + "'");
                else
                {
                    p.Dir = profile.Steering.Sign != 0f ? Mathf.Sign(profile.Steering.Sign) : TopRight(rig, p);
                    rig.Steering = p;
                }
            }
            foreach (var part in profile.Parts)
            {
                if (part == null || string.IsNullOrEmpty(part.Path)) continue;
                var p = MakePivot(rig, part.Path, part.Axis);
                if (p == null) { missing.Add(part.Kind + " '" + part.Path + "'"); continue; }
                p.P = part;
                p.Dir = part.Sign != 0f ? Mathf.Sign(part.Sign) : Lifts(rig, p);
                if (part.Kind == "wiper") rig.Wipers.Add(p);
                else rig.Lids.Add(p);
            }
            Core.Log.Msg("[Parts] " + Registry.Name(lv) + " (" + profile.Code + "): " + (rig.Steering != null ? "steering wheel turning " + (rig.Steering.Dir > 0 ? "+" : "-") : "no steering wheel")
                + ", " + rig.Lids.Count + " lid(s), " + rig.Wipers.Count + " wiper(s)" + (missing.Count > 0 ? "; not in the model: " + string.Join(", ", missing) : "") + ".");
            return rig;
        }

        private static Pivot MakePivot(Rig rig, string path, Vector3 axis)
        {
            var t = Frames.Resolve(rig.Frame, path);
            if (t == null || t == rig.Frame) return null;
            return new Pivot { T = t, Rest = t.localRotation, Axis = axis.sqrMagnitude > 0f ? axis.normalized : Vector3.forward };
        }

        private static Vector3 AxisWorld(Pivot p) => p.T.parent != null ? p.T.parent.rotation * (p.Rest * p.Axis) : p.Rest * p.Axis;

        /// <summary>+1 when a positive turn about the column moves the top of the wheel to the right of the frame.</summary>
        private static float TopRight(Rig rig, Pivot p)
        {
            Vector3 axis = AxisWorld(p);
            Vector3 top = Vector3.ProjectOnPlane(rig.Frame.up, axis).normalized;
            Vector3 moved = Quaternion.AngleAxis(10f, axis) * top;
            return Vector3.Dot(moved - top, rig.Frame.right) >= 0f ? 1f : -1f;
        }

        /// <summary>+1 when a positive turn about the hinge lifts the middle of the part's meshes.</summary>
        private static float Lifts(Rig rig, Pivot p)
        {
            Vector3 centre = Vector3.zero;
            int n = 0;
            foreach (var r in p.T.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null) continue;
                centre += r.bounds.center;
                n++;
            }
            if (n == 0) return 1f;
            Vector3 arm = centre / n - p.T.position;
            Vector3 moved = Quaternion.AngleAxis(10f, AxisWorld(p)) * arm;
            return Vector3.Dot(moved - arm, rig.Frame.up) >= 0f ? 1f : -1f;
        }

        private static void Pose(Pivot p, float degrees) => p.T.localRotation = p.Rest * Quaternion.AngleAxis(degrees * p.Dir, p.Axis);

        private static void MoveLids(Rig rig, float dt)
        {
            if (rig.Lids.Count == 0) return;
            int id = Ids.Of(rig.V);
            _open.TryGetValue(id, out var open);
            bool storage = false;
            try { storage = rig.V.Storage != null && rig.V.Storage.IsOpened; }
            catch (Exception e) { Core.Log.Warning("[Parts] could not read the storage of " + Registry.Name(rig.V) + ": " + e.Message); }
            _storageFront.TryGetValue(id, out bool front);
            foreach (var p in rig.Lids)
            {
                bool want = (open != null && open.Contains(p.P.Name.ToLowerInvariant()))
                            || (storage && ((p.P.Storage == "front" && front) || (p.P.Storage == "rear" && !front)));
                if (want != p.Logged) { p.Logged = want; Core.Log.Msg("[Parts] " + Registry.Name(rig.V) + " " + p.P.Name + (want ? " opens" : " closes") + "."); }
                p.Open = Mathf.MoveTowards(p.Open, want ? 1f : 0f, dt / Mathf.Max(0.05f, p.P.Seconds));
                float k = p.Open * p.Open * (3f - 2f * p.Open);
                Pose(p, p.P.Angle * k);
            }
        }

        private static void Wipe(Rig rig, float now, float dt)
        {
            if (rig.Wipers.Count == 0) return;
            if (now >= rig.NextRain)
            {
                rig.NextRain = now + RainPollSeconds;
                float rain = RainAt(rig.V);
                bool running = rig.V.IsOccupied || Mathf.Abs(rig.V.Speed_Kmh) > RunningKmh;
                bool raining = rain > 0f && !rig.V.IsUnderCover;
                bool wiping = raining && running;
                if (wiping != rig.Wiping)
                    Core.Log.Msg("[Parts] " + Registry.Name(rig.V) + " wipers " + (wiping ? "on (rain " + rain.ToString("F2") + ")" : "off (" + (!raining ? (rig.V.IsUnderCover ? "under cover" : "no rain") : "not running") + ")") + ".");
                rig.Wiping = wiping;
                rig.Rain = rain;
            }
            foreach (var p in rig.Wipers)
            {
                // A stroke in progress finishes, so the wipers always come to rest at the bottom.
                if (!rig.Wiping && p.Phase == 0f) continue;
                float period = Mathf.Lerp(p.P.Seconds, p.P.FastSeconds, Mathf.Clamp01(rig.Rain));
                p.Phase += dt / Mathf.Max(0.1f, period);
                if (p.Phase >= 1f) p.Phase = rig.Wiping ? p.Phase - 1f : 0f;
                Pose(p, p.P.Angle * (0.5f - 0.5f * Mathf.Cos(p.Phase * 2f * Mathf.PI)));
            }
        }

        private static float RainAt(LandVehicle lv)
        {
            if (RainOverride.HasValue) return RainOverride.Value ? 1f : 0f;
            if (_rainBroken) return 0f;
            try
            {
                if (!NetworkSingleton<GameEnvironment>.InstanceExists) return 0f;
                var env = NetworkSingleton<GameEnvironment>.Instance;
                if (!env.HasActiveWeatherVolumes()) return 0f;
                var weather = env.GetWeatherProfileFromPosition(lv.transform.position);
                return weather != null && weather.Conditions != null ? weather.Conditions.Rainy : 0f;
            }
            catch (Exception e)
            {
                _rainBroken = true;
                Core.Log.Error("[Parts] reading the weather failed, the wipers stay still from now on: " + e.Message);
                return 0f;
            }
        }

        private static void Restore(Rig rig)
        {
            if (rig.Steering?.T != null) rig.Steering.T.localRotation = rig.Steering.Rest;
            foreach (var p in rig.Lids) if (p.T != null) p.T.localRotation = p.Rest;
            foreach (var p in rig.Wipers) if (p.T != null) p.T.localRotation = p.Rest;
        }

        internal static string Describe(LandVehicle lv)
        {
            var rig = RigOf(lv);
            if (rig == null) return "parts: " + Registry.Name(lv) + " has no profile";
            string Rot(Transform t) => t == null ? "-" : t.localEulerAngles.ToString("F0");
            var sb = new System.Text.StringBuilder("parts of " + Registry.Name(lv) + ": steer " + lv.CurrentSteerAngle.ToString("F1"));
            if (rig.Steering != null) sb.Append(" wheel ").Append(Rot(rig.Steering.T)).Append(" dir ").Append(rig.Steering.Dir);
            foreach (var p in rig.Lids) sb.Append(" | ").Append(p.P.Name).Append(' ').Append(p.Open.ToString("F2")).Append(' ').Append(Rot(p.T));
            sb.Append(" | wipers ").Append(rig.Wiping ? "on" : "off");
            foreach (var p in rig.Wipers) sb.Append(' ').Append(p.P.Name).Append(' ').Append(Rot(p.T));
            return sb.ToString();
        }

        internal static void Reset()
        {
            _rigs.Clear();
            _open.Clear();
            _storageFront.Clear();
            _rainBroken = false;
            RainOverride = null;
        }
    }
}
