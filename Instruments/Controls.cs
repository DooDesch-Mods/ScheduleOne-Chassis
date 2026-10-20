using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Chassis.Api;
using Chassis.Interior;
using Chassis.Net;
using Chassis.Vehicles;

namespace Chassis.Instruments
{
    /// <summary>
    /// The switches of a cockpit. A use goes to the host, which checks that the player sits in the vehicle, runs the
    /// built-in action and passes the use on to every peer, where the switch moves and mod handlers run:
    ///   req.ctl vehicle name player value -> ctl vehicle name value player
    ///   req.trip vehicle                  -> odo ...                 (trip back to 0)
    ///   (late join)                       -> ctls vehicle:name=value,...;...
    /// A player id of -1 is a use from a mod's code. Toggles and knobs keep their value per vehicle in <see cref="Store"/>,
    /// which the host saves with the game.
    /// </summary>
    internal static class Controls
    {
        /// <summary>A switch is in reach this far along the view from the eyes, in metres.</summary>
        internal const float Reach = 0.9f;
        /// <summary>Switches are not animated beyond this distance from the camera.</summary>
        private const float Range = 30f;
        private const float PressSeconds = 0.18f, MoveSeconds = 0.06f;

        internal static readonly HashSet<string> BuiltIn = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "lights", "hazard", "indicator", "indicator_left", "indicator_right", "horn", "trip_reset",
        };

        private static readonly Dictionary<string, Action<ControlUse>> _actions = new Dictionary<string, Action<ControlUse>>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal sealed class Bound
        {
            internal LandVehicle V;
            internal ControlProfile P;
            internal string Kind;
            internal Transform T;
            internal Quaternion Rest;
            internal Vector3 RestPos;
            internal Vector3 BoxCenter, BoxSize;
            internal float Angle, Pressed;
            internal float PressUntil;
            internal readonly List<GameObject> Outline = new List<GameObject>();
            internal bool OutlineMade;
            internal GameObject Zone;
        }

        private sealed class Cab
        {
            internal LandVehicle V;
            internal ChassisProfile Profile;
            internal readonly List<Bound> Items = new List<Bound>();
        }

        private static readonly Dictionary<int, Cab> _cabs = new Dictionary<int, Cab>();
        private static readonly List<Bound> _none = new List<Bound>();
        private static readonly List<int> _gone = new List<int>();
        private static float _nextPrune;

        // ---- binding -----------------------------------------------------------------------------------------------

        /// <summary>The bound switches of a vehicle; binds them the first time and after the interior or profile changed.</summary>
        internal static List<Bound> Of(LandVehicle lv)
        {
            if (lv == null) return _none;
            var profile = ChassisApi.For(lv);
            if (profile == null || profile.Controls.Count == 0 || !BaseInterior.Ready(lv, profile)) return _none;
            int key = lv.GetInstanceID();
            if (_cabs.TryGetValue(key, out var cab) && cab.Profile == profile) return cab.Items;
            if (cab != null) Release(cab);
            cab = new Cab { V = lv, Profile = profile };
            _cabs[key] = cab;
            Transform frame = Frames.Resolve(lv.transform, profile.Frame);
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in profile.Controls)
            {
                if (c == null) continue;
                string kind = (c.Kind ?? "").ToLowerInvariant();
                if (kind != "button" && kind != "toggle" && kind != "knob")
                {
                    Warn(profile.Code, "kind|" + c.Name, "switch '" + c.Name + "' has the kind '" + c.Kind + "' - button, toggle or knob; it is skipped.");
                    continue;
                }
                if (string.IsNullOrEmpty(c.Name) || !names.Add(c.Name))
                {
                    Warn(profile.Code, "name|" + c.Name, "switch '" + c.Name + "' needs a name of its own in the profile - it is skipped.");
                    continue;
                }
                Transform t = string.IsNullOrEmpty(c.Path) ? null : Frames.Resolve(frame, c.Path);
                if (t == null)
                {
                    Warn(profile.Code, "path|" + c.Path, "switch '" + c.Name + "' path '" + c.Path + "' not found below the frame - it cannot be used.");
                    continue;
                }
                var b = new Bound { V = lv, P = c, Kind = kind, T = t, Rest = t.localRotation, RestPos = t.localPosition };
                if (!Box(b))
                {
                    Warn(profile.Code, "box|" + c.Name, "switch '" + c.Name + "' has no renderer and no BoxSize - nothing to aim at, it cannot be used.");
                    continue;
                }
                b.Angle = Target(b);
                cab.Items.Add(b);
            }
            Core.Log.Msg("[Controls] " + Registry.Name(lv) + ": " + cab.Items.Count + " of " + profile.Controls.Count + " switch(es) bound.");
            return cab.Items;
        }

        /// <summary>The area to aim at, in the switch transform's own space: the profile's box, or the meshes below it.</summary>
        private static bool Box(Bound b)
        {
            Vector3 lossy = b.T.lossyScale;
            float scale = Mathf.Max(Mathf.Abs(lossy.x), Mathf.Abs(lossy.y), Mathf.Abs(lossy.z), 1e-4f);
            if (b.P.BoxSize.sqrMagnitude > 1e-8f)
            {
                b.BoxCenter = b.P.BoxCenter;
                b.BoxSize = b.P.BoxSize;
            }
            else
            {
                bool any = false;
                var box = new Bounds();
                foreach (var mf in b.T.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf == null || mf.sharedMesh == null || IsOurs(mf.transform, b.T)) continue;
                    Bounds mb = mf.sharedMesh.bounds;
                    Vector3 mn = mb.min, mx = mb.max;
                    for (int i = 0; i < 8; i++)
                    {
                        var c = new Vector3((i & 1) == 0 ? mn.x : mx.x, (i & 2) == 0 ? mn.y : mx.y, (i & 4) == 0 ? mn.z : mx.z);
                        Vector3 p = b.T.InverseTransformPoint(mf.transform.TransformPoint(c));
                        if (!any) { box = new Bounds(p, Vector3.zero); any = true; }
                        else box.Encapsulate(p);
                    }
                }
                if (!any) return false;
                b.BoxCenter = box.center;
                b.BoxSize = box.size;
            }
            // Small switches get at least 2 cm to aim at.
            float min = 0.02f / scale;
            b.BoxSize = new Vector3(Mathf.Max(Mathf.Abs(b.BoxSize.x), min), Mathf.Max(Mathf.Abs(b.BoxSize.y), min), Mathf.Max(Mathf.Abs(b.BoxSize.z), min));
            return true;
        }

        /// <summary>Outlines and hit zones Chassis hangs below a switch; they are not part of its shape.</summary>
        private static bool IsOurs(Transform t, Transform stop)
        {
            for (; t != null && t != stop; t = t.parent)
                if (t.name.StartsWith("Chassis_Outline", StringComparison.Ordinal) || t.name.StartsWith("Chassis_Zone", StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>Where along the view a switch is hit, as a distance from <paramref name="origin"/>, or -1.</summary>
        internal static float Hit(Bound b, Vector3 origin, Vector3 dir)
        {
            if (b.T == null) return -1f;
            Matrix4x4 m = b.T.worldToLocalMatrix;
            Vector3 o = m.MultiplyPoint3x4(origin) - b.BoxCenter;
            Vector3 d = m.MultiplyVector(dir);
            Vector3 h = b.BoxSize * 0.5f;
            float tMin = 0f, tMax = float.MaxValue;
            for (int i = 0; i < 3; i++)
            {
                if (Mathf.Abs(d[i]) < 1e-8f)
                {
                    if (o[i] < -h[i] || o[i] > h[i]) return -1f;
                    continue;
                }
                float t1 = (-h[i] - o[i]) / d[i], t2 = (h[i] - o[i]) / d[i];
                if (t1 > t2) { float s = t1; t1 = t2; t2 = s; }
                tMin = Mathf.Max(tMin, t1);
                tMax = Mathf.Min(tMax, t2);
                if (tMin > tMax) return -1f;
            }
            return tMin;
        }

        // ---- state -------------------------------------------------------------------------------------------------

        /// <summary>The value a switch shows: its source when it has one, else its stored state.</summary>
        internal static float Shown(LandVehicle lv, ControlProfile c)
        {
            if (!string.IsNullOrEmpty(c.Source))
            {
                var r = Sources.Read(lv, c.Source);
                if (r.Valid) return r.Value;
            }
            return Stored(lv, c);
        }

        private static float Stored(LandVehicle lv, ControlProfile c)
        {
            if (string.Equals(c.Kind, "button", StringComparison.OrdinalIgnoreCase)) return 0f;
            var cab = Store.Of(lv);
            return cab != null && cab.Controls.TryGetValue(c.Name, out float v) ? v : c.Default;
        }

        internal static float ValueOf(LandVehicle lv, string name)
        {
            var c = ChassisApi.For(lv)?.Control(name);
            return lv == null || c == null ? 0f : Shown(lv, c);
        }

        // ---- using -------------------------------------------------------------------------------------------------

        /// <summary>The local player clicked a switch: a button fires, a toggle flips, a knob goes back to its default.</summary>
        internal static void Click(Bound b)
        {
            float now = Shown(b.V, b.P);
            float value = b.Kind == "button" ? 1f : b.Kind == "toggle" ? (now > 0.5f ? 0f : 1f) : b.P.Default;
            if (b.Kind == "button") b.PressUntil = Time.time + PressSeconds;
            Send(b.V, b.P, value, Player.Local);
        }

        /// <summary>The local player turned the mouse wheel on a knob.</summary>
        internal static void Scroll(Bound b, int notches)
        {
            if (b.Kind != "knob" || notches == 0) return;
            float step = Mathf.Abs(b.P.Step) > 1e-6f ? Mathf.Abs(b.P.Step) : 0.1f;
            float lo = Mathf.Min(b.P.Min, b.P.Max), hi = Mathf.Max(b.P.Min, b.P.Max);
            float now = Shown(b.V, b.P);
            float value = Mathf.Clamp(now + notches * step, lo, hi);
            if (Mathf.Abs(value - now) < 1e-5f) return;
            Send(b.V, b.P, value, Player.Local);
        }

        internal static void UseFromApi(LandVehicle lv, string name, float? value)
        {
            if (lv == null) throw new ArgumentNullException("vehicle");
            var c = ChassisApi.For(lv)?.Control(name);
            if (c == null) throw new ArgumentException("the profile of " + Registry.Name(lv) + " has no switch '" + name + "'");
            string kind = (c.Kind ?? "").ToLowerInvariant();
            float v = kind == "button" ? 1f
                : kind == "toggle" ? (value.HasValue ? (value.Value > 0.5f ? 1f : 0f) : (Shown(lv, c) > 0.5f ? 0f : 1f))
                : value ?? c.Default;
            Send(lv, c, v, null);
        }

        private static void Send(LandVehicle lv, ControlProfile c, float value, Player by)
        {
            int id = Ids.Of(lv);
            if (id < 0) { Core.Log.Warning("[Controls] " + Registry.Name(lv) + " has no network id - switch '" + c.Name + "' not used."); return; }
            if (c.Name.IndexOf('|') >= 0 || c.Name.IndexOf(' ') >= 0) { Core.Log.Warning("[Controls] switch name '" + c.Name + "' has a space or '|' - it cannot travel over the network."); return; }
            ChassisNet.ToHost("req.ctl", id.ToString(CultureInfo.InvariantCulture), c.Name, (by != null ? Ids.Of(by) : -1).ToString(CultureInfo.InvariantCulture), value.ToString("R", CultureInfo.InvariantCulture));
        }

        internal static void RequestTripReset(LandVehicle lv)
        {
            if (lv == null) throw new ArgumentNullException("vehicle");
            int id = Ids.Of(lv);
            if (id < 0) { Core.Log.Warning("[Controls] trip reset for " + Registry.Name(lv) + ", which has no network id - ignored."); return; }
            ChassisNet.ToHost("req.trip", id.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Host: a trip reset from a mod.</summary>
        internal static void HostTrip(int vehicle)
        {
            var lv = Registry.ById(vehicle);
            if (lv == null) { Core.Log.Warning("[Controls] trip reset for net " + vehicle + ", which is not here - dropped."); return; }
            Odometer.HostResetTrip(lv, "a mod");
        }

        /// <summary>Host: checks a use, runs the built-in action and passes the use on to everyone.</summary>
        internal static void HostUse(int vehicle, string name, int player, float value)
        {
            var lv = Registry.ById(vehicle);
            if (lv == null) { Core.Log.Warning("[Controls] switch '" + name + "' on net " + vehicle + ", which is not here - dropped."); return; }
            var c = ChassisApi.For(lv)?.Control(name);
            if (c == null) { Core.Log.Warning("[Controls] " + Registry.Name(lv) + " has no switch '" + name + "' on the host - dropped."); return; }
            Player p = null;
            if (player >= 0)
            {
                p = Ids.Find<Player>(player);
                if (p == null) { Core.Log.Warning("[Controls] switch '" + name + "' from player net " + player + ", who is not here - dropped."); return; }
                if (!SitsIn(lv, p)) { Core.Log.Msg("[Controls] " + p.PlayerName + " does not sit in " + Registry.Name(lv) + " - switch '" + name + "' not used."); return; }
            }
            string kind = (c.Kind ?? "").ToLowerInvariant();
            if (float.IsNaN(value) || float.IsInfinity(value)) value = c.Default;
            if (kind == "button") value = 1f;
            else if (kind == "toggle") value = value > 0.5f ? 1f : 0f;
            else value = Mathf.Clamp(value, Mathf.Min(c.Min, c.Max), Mathf.Max(c.Min, c.Max));

            if (BuiltIn.Contains(c.Action ?? "")) HostBuiltIn(lv, vehicle, c, kind, value);
            ChassisNet.ToAll("ctl", vehicle.ToString(CultureInfo.InvariantCulture), c.Name, value.ToString("R", CultureInfo.InvariantCulture), player.ToString(CultureInfo.InvariantCulture));
        }

        private static bool SitsIn(LandVehicle lv, Player p)
        {
            var seats = lv.Seats;
            if (seats == null) return false;
            foreach (var s in seats) if (s != null && s.Occupant == p) return true;
            return false;
        }

        private static void HostBuiltIn(LandVehicle lv, int vehicle, ControlProfile c, string kind, float value)
        {
            string id = vehicle.ToString(CultureInfo.InvariantCulture);
            int side = Indicators.SideOf(lv);
            bool flip = kind == "button";
            switch (c.Action.ToLowerInvariant())
            {
                case "lights":
                    if (lv.lights == null) { Core.Log.Warning("[Controls] " + Registry.Name(lv) + " has no headlights to switch."); return; }
                    bool on = flip ? !lv.lights.HeadlightsOn : value > 0.5f;
                    lv.lights.HeadlightsOn = on;
                    Core.Log.Msg("[Controls] headlights of " + Registry.Name(lv) + " " + (on ? "on" : "off") + " (switch '" + c.Name + "').");
                    return;
                case "hazard":
                {
                    bool want = flip ? side != Indicators.Hazard : value > 0.5f;
                    if (!want && side != Indicators.Hazard) return;
                    Blink(id, want ? Indicators.Hazard : 0);
                    return;
                }
                case "indicator":
                    // A knob from -1 to 1; a button turns the signal off.
                    Blink(id, flip ? 0 : Mathf.Clamp(Mathf.RoundToInt(value), -1, 1));
                    return;
                case "indicator_left":
                case "indicator_right":
                {
                    int s = c.Action.EndsWith("left", StringComparison.OrdinalIgnoreCase) ? -1 : 1;
                    bool want = flip ? side != s : value > 0.5f;
                    if (!want && side != s) return;
                    Blink(id, want ? s : 0);
                    return;
                }
                case "horn":
                    ChassisNet.ToAll("honk", id);
                    return;
                case "trip_reset":
                    Odometer.HostResetTrip(lv, "switch '" + c.Name + "'");
                    return;
            }
        }

        private static void Blink(string id, int side) => ChassisNet.ToAll("blink", id, side.ToString(CultureInfo.InvariantCulture));

        /// <summary>Everyone: a switch was used. Stores the state, moves the switch and runs the handlers.</summary>
        internal static void OnUse(int vehicle, string name, float value, int player)
        {
            var lv = Registry.ById(vehicle);
            if (lv == null) { Core.Log.Warning("[Controls] switch '" + name + "' used on net " + vehicle + ", which is not here."); return; }
            var c = ChassisApi.For(lv)?.Control(name);
            if (c == null) { Core.Log.Warning("[Controls] " + Registry.Name(lv) + " has no switch '" + name + "' on this peer - the use is ignored."); return; }
            string kind = (c.Kind ?? "").ToLowerInvariant();
            if (kind != "button")
            {
                var cab = Store.Of(vehicle);
                cab.Controls[c.Name] = value;
            }
            else
            {
                foreach (var b in Of(lv)) if (b.P == c) b.PressUntil = Time.time + PressSeconds;
            }
            var by = player >= 0 ? Ids.Find<Player>(player) : null;
            Core.Log.Msg("[Controls] " + Registry.Name(lv) + ": switch '" + c.Name + "' (" + kind + (string.IsNullOrEmpty(c.Action) ? "" : ", " + c.Action) + ") = "
                + value.ToString("0.##", CultureInfo.InvariantCulture) + " by " + (by != null ? by.PlayerName : player >= 0 ? "player net " + player : "a mod") + ".");
            var use = new ControlUse { Vehicle = lv, Control = c, Action = c.Action ?? string.Empty, Value = value, By = by, IsHost = ChassisNet.IsHost };
            if (!string.IsNullOrEmpty(c.Action) && !BuiltIn.Contains(c.Action))
            {
                if (_actions.TryGetValue(c.Action, out var handler))
                {
                    try { handler(use); }
                    catch (Exception e) { Core.Log.Error("[Controls] the handler of action '" + c.Action + "' failed: " + e); }
                }
                else if (_warned.Add("action|" + c.Action)) Core.Log.Warning("[Controls] no mod registered the action '" + c.Action + "' - switch '" + c.Name + "' only keeps its state.");
            }
            ChassisApi.RaiseControlUsed(use);
        }

        /// <summary>Host: the state of every toggle and knob, for a peer that joined late. No handler runs for it.</summary>
        internal static void HostReplay()
        {
            var sb = new StringBuilder();
            foreach (var kv in Store.All)
            {
                if (kv.Value.Controls.Count == 0) continue;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(kv.Key.ToString(CultureInfo.InvariantCulture)).Append(':');
                bool first = true;
                foreach (var c in kv.Value.Controls)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    sb.Append(c.Key).Append('=').Append(c.Value.ToString("R", CultureInfo.InvariantCulture));
                }
            }
            if (sb.Length > 0) ChassisNet.ToAll("ctls", sb.ToString());
        }

        internal static void OnStates(string payload)
        {
            if (ChassisNet.IsHost) return;
            int n = 0;
            foreach (string entry in (payload ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = entry.IndexOf(':');
                if (colon <= 0 || !int.TryParse(entry.Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id))
                {
                    Core.Log.Warning("[Controls] unreadable switch states '" + entry + "' skipped.");
                    continue;
                }
                var cab = Store.Of(id);
                foreach (string pair in entry.Substring(colon + 1).Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = pair.IndexOf('=');
                    if (eq <= 0 || !float.TryParse(pair.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float v)) continue;
                    cab.Controls[pair.Substring(0, eq)] = v;
                    n++;
                }
            }
            Core.Log.Msg("[Controls] " + n + " switch state(s) from the host.");
        }

        // ---- mod actions -------------------------------------------------------------------------------------------

        internal static void RegisterAction(string action, Action<ControlUse> handler)
        {
            if (string.IsNullOrEmpty(action)) throw new ArgumentException("a switch action needs a name");
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (BuiltIn.Contains(action)) throw new ArgumentException("'" + action + "' is a built-in switch action");
            _actions[action] = handler;
            _warned.Remove("action|" + action);
            Core.Log.Msg("[Controls] action '" + action + "' registered by a mod.");
        }

        internal static bool RemoveAction(string action)
        {
            if (string.IsNullOrEmpty(action) || !_actions.Remove(action)) return false;
            Core.Log.Msg("[Controls] action '" + action + "' removed.");
            return true;
        }

        // ---- moving the switches -----------------------------------------------------------------------------------

        internal static void Tick()
        {
            Camera cam = Camera.main;
            Vector3 eye = cam != null ? cam.transform.position : Vector3.zero;
            float k = 1f - Mathf.Exp(-Time.deltaTime / MoveSeconds);
            foreach (var lv in Registry.Profiled)
            {
                if (lv == null) continue;
                if (cam != null && (lv.transform.position - eye).sqrMagnitude > Range * Range) continue;
                foreach (var b in Of(lv))
                {
                    if (b.T == null) continue;
                    Vector3 axis = b.P.Axis.sqrMagnitude > 1e-6f ? b.P.Axis.normalized : Vector3.right;
                    if (b.Kind == "button")
                    {
                        float press = Time.time < b.PressUntil ? b.P.Press : 0f;
                        if (Mathf.Abs(press - b.Pressed) < 1e-5f) continue;
                        b.Pressed = Mathf.Lerp(b.Pressed, press, k);
                        if (Mathf.Abs(press - b.Pressed) < 1e-4f) b.Pressed = press;
                        b.T.localPosition = b.RestPos + b.Rest * axis * b.Pressed;
                        continue;
                    }
                    float target = Target(b);
                    if (Mathf.Abs(target - b.Angle) < 0.01f) continue;
                    b.Angle = Mathf.Lerp(b.Angle, target, k);
                    if (Mathf.Abs(target - b.Angle) < 0.05f) b.Angle = target;
                    b.T.localRotation = b.Rest * Quaternion.AngleAxis(b.Angle, axis);
                }
            }
            if (Time.unscaledTime < _nextPrune) return;
            _nextPrune = Time.unscaledTime + 2f;
            _gone.Clear();
            foreach (var kv in _cabs) if (kv.Value.V == null) _gone.Add(kv.Key);
            foreach (int key in _gone) _cabs.Remove(key);
        }

        private static float Target(Bound b)
        {
            float half = b.P.Angle * 0.5f;
            float v = Shown(b.V, b.P);
            if (b.Kind == "toggle") return v > 0.5f ? half : -half;
            if (b.Kind == "knob") return Mathf.Lerp(-half, half, Mathf.InverseLerp(b.P.Min, b.P.Max, v));
            return 0f;
        }

        // ---- bookkeeping -------------------------------------------------------------------------------------------

        private static void Warn(string code, string what, string text)
        {
            if (!_warned.Add(code + "|" + what)) return;
            Core.Log.Warning("[Controls] " + code + ": " + text);
        }

        private static void Release(Cab cab)
        {
            foreach (var b in cab.Items)
            {
                try
                {
                    if (b.T != null) { b.T.localRotation = b.Rest; b.T.localPosition = b.RestPos; }
                    foreach (var go in b.Outline) if (go != null) UnityEngine.Object.Destroy(go);
                    if (b.Zone != null) UnityEngine.Object.Destroy(b.Zone);
                }
                catch (Exception e) { Core.Log.Warning("[Controls] putting switch '" + b.P.Name + "' back failed: " + e.Message); }
            }
            cab.Items.Clear();
        }

        /// <summary>The vehicle's interior was rebuilt or removed; its switches bind again.</summary>
        internal static void Forget(LandVehicle lv)
        {
            if (lv == null || !_cabs.TryGetValue(lv.GetInstanceID(), out var cab)) return;
            Release(cab);
            _cabs.Remove(lv.GetInstanceID());
        }

        internal static void ForgetAll()
        {
            foreach (var cab in _cabs.Values) Release(cab);
            _cabs.Clear();
            _warned.Clear();
        }

        internal static IEnumerable<Bound> AllBound()
        {
            foreach (var cab in _cabs.Values)
                foreach (var b in cab.Items) yield return b;
        }

        internal static IEnumerable<string> Describe(LandVehicle lv)
        {
            var list = Of(lv);
            if (list.Count == 0) { yield return "switches: none bound (no profile switches, or the interior is not built yet)"; yield break; }
            foreach (var b in list)
                yield return "switch " + b.P.Name + " (" + b.Kind + (string.IsNullOrEmpty(b.P.Action) ? "" : ", " + b.P.Action) + (string.IsNullOrEmpty(b.P.Source) ? "" : ", shows " + b.P.Source) + ") = "
                    + Shown(lv, b.P).ToString("0.##", CultureInfo.InvariantCulture) + ", box " + ProfileJson.F(b.BoxSize) + " at " + ProfileJson.F(b.BoxCenter);
        }

        internal static void Reset()
        {
            _cabs.Clear();
            _warned.Clear();
            _nextPrune = 0f;
        }
    }
}
