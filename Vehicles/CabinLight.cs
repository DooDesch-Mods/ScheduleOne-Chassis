using System;
using System.Collections.Generic;
using Chassis.Api;
using Chassis.Instruments;
using Chassis.Net;

namespace Chassis.Vehicles
{
    /// <summary>
    /// The cabin light of a vehicle whose profile has a <see cref="DomeName"/> kit part, as the base interior builds it.
    ///
    /// The switch <see cref="ControlName"/> has three positions, as the ON/DOOR/OFF switch of a real car; a click moves it
    /// Auto, On, Off, Auto. In Auto the light comes on for <see cref="DoorSeconds"/> when someone gets in or out, and
    /// while a car with a player aboard stands for <see cref="StandSeconds"/>; it goes out as soon as the car drives
    /// (vanilla has no ignition, so throttle and speed stand in for it). Every peer sees the same seats and the same
    /// speed and works that out on its own. The host puts the switch back to Auto when the last occupant gets out, and it
    /// is never saved. Lens and point light fade in over <see cref="FadeInSeconds"/> and out over
    /// <see cref="FadeSeconds"/>, eased at both ends, as the theatre dimming of a real car.
    /// </summary>
    internal static class CabinLight
    {
        internal const string DomeName = "Chassis_DomeLight";
        internal const string ControlName = "dome";
        /// <summary>Switch positions, stored as the value of the switch <see cref="ControlName"/>.</summary>
        internal const int Auto = 0, On = 1, Off = 2;
        /// <summary>After someone gets in or out the light stays on this long, as the door delay of a real car.</summary>
        internal const float DoorSeconds = 15f;
        internal const float FadeSeconds = 1.5f, FadeInSeconds = 0.6f;
        /// <summary>A car with a player aboard that stands this long lights up.</summary>
        internal const float StandSeconds = 3f;
        /// <summary>Above this speed, held for <see cref="MovingSeconds"/>, the car drives (raw km/h, either way).</summary>
        private const float MovingKmh = 3f, MovingSeconds = 0.3f;
        /// <summary>Lights farther than this from the camera are not lit.</summary>
        private const float Range = 40f;
        /// <summary>Dark cars beyond this are not looked at (a little past <see cref="Range"/>, so a car at the edge does not flip).</summary>
        private const float SkipRange = 45f;
        private const float Intensity = 1.4f;

        internal static readonly Color LightColor = new Color(1f, 0.86f, 0.62f);

        private sealed class State
        {
            internal LandVehicle V;
            internal int Riders = -1;
            internal float DoorUntil;
            internal float FastSince = -1f;
            internal float StillSince = -1f;
            internal bool Parked;
            internal float Level;
            internal bool Want;
            internal float FadeStart = -1f;
            internal Light Light;
            internal Transform Dome;
            // The lens keeps a material of its own, so its colour can run between dark and lit.
            internal Renderer Lens;
            internal Material LensMat;
            internal Color LensOff;
            internal float Shown = -1f;
            internal ChassisProfile Profile;
            internal bool LoggedMissing;
        }

        private static readonly Dictionary<int, State> _states = new Dictionary<int, State>();
        private static readonly List<int> _gone = new List<int>();
        private static float _nextPrune;
        private static bool _lightFailed;

        /// <summary>How bright the light is, 0 (dark) to 1; between the two while it fades out.</summary>
        internal static float Level(LandVehicle lv) =>
            lv != null && _states.TryGetValue(lv.GetInstanceID(), out var s) ? s.Level : 0f;

        internal static bool IsOn(LandVehicle lv) => Level(lv) > 0f;

        /// <summary>Cars the cabin light keeps a state for.</summary>
        internal static int Tracked => _states.Count;

        /// <summary>Cabin lights that glow now, and of those the point lights that are switched on.</summary>
        internal static void Count(out int glowing, out int pointLights)
        {
            glowing = 0; pointLights = 0;
            foreach (var s in _states.Values)
            {
                if (s.Level > 0f) glowing++;
                if (s.Light != null && s.Light.enabled) pointLights++;
            }
        }

        /// <summary>The switch position: <see cref="Auto"/>, <see cref="On"/> or <see cref="Off"/>.</summary>
        internal static int Mode(LandVehicle lv)
        {
            var cab = Store.Of(lv);
            if (cab == null || !cab.Controls.TryGetValue(ControlName, out float v)) return Auto;
            int m = Mathf.RoundToInt(v);
            return m == On || m == Off ? m : Auto;
        }

        internal static string ModeName(int mode) => mode == On ? "On" : mode == Off ? "Off" : "Auto";

        /// <summary>The next position a click on the lamp moves the switch to: Auto, On, Off, Auto again.</summary>
        internal static int Next(int mode) => (mode + 1) % 3;

        private static bool HasPlayer(LandVehicle lv)
        {
            var seats = lv.Seats;
            if (seats != null) foreach (var seat in seats) if (seat != null && seat.Occupant != null) return true;
            return false;
        }

        internal static void Tick()
        {
            Camera cam = Camera.main;
            Vector3 eye = cam != null ? cam.transform.position : Vector3.zero;
            float now = Time.time, dt = Time.deltaTime;
            // A position other than Auto is rare; only then does a far car need its own switch looked up.
            bool anyHeld = Store.AnyAway(ControlName, Auto);
            foreach (var lv in Registry.Profiled)
            {
                if (lv == null) continue;
                int key = lv.GetInstanceID();
                _states.TryGetValue(key, out var s);
                // A dark car far from the camera has nothing to show: skip its seats and speed, the bulk of this loop
                // across every car in the world. It starts over when it comes near, so a stale rider count does not
                // read as someone getting in. A switch left on On or Off still runs, so the host can put it back.
                bool far = cam != null && (lv.transform.position - eye).sqrMagnitude > SkipRange * SkipRange;
                if (far && (s == null || (s.Level <= 0f && s.Shown <= 0f)) && (!anyHeld || Mode(lv) == Auto))
                {
                    if (s != null) Forget(s);
                    continue;
                }
                var profile = ChassisApi.For(lv);
                if (profile == null || profile.Control(ControlName) == null) continue;
                if (s == null) _states[key] = s = new State { V = lv };
                int mode = Mode(lv);

                int riders = Riders(lv);
                if (s.Riders >= 0 && riders != s.Riders)
                {
                    s.DoorUntil = now + DoorSeconds;
                    Core.Log.Msg("[CabinLight] " + Registry.Name(lv) + ": " + (riders > s.Riders ? "someone got in" : "someone got out") + " (" + s.Riders + " -> " + riders + ") - door light for " + DoorSeconds.ToString("0") + " s.");
                    if (riders == 0 && ChassisNet.IsHost && mode != Auto)
                    {
                        Core.Log.Msg("[CabinLight] " + Registry.Name(lv) + " is empty - the switch goes back to " + ModeName(Auto) + ".");
                        try { Controls.UseFromApi(lv, ControlName, Auto); }
                        catch (Exception e) { Core.Log.Error("[CabinLight] switching the cabin light of " + Registry.Name(lv) + " back to automatic failed: " + e.Message); }
                    }
                }
                s.Riders = riders;

                // Driving plays the part of a real car's ignition: the local driver's throttle counts at once, the speed
                // (all a passenger or a watcher has) after a moment, so a car settling on its springs is not driving.
                bool gas = lv.LocalPlayerIsDriver && Mathf.Abs(lv.currentThrottle) > 0.05f;
                if (Mathf.Abs(lv.Speed_Kmh) <= MovingKmh) s.FastSince = -1f;
                else if (s.FastSince < 0f) s.FastSince = now;
                bool moving = gas || (s.FastSince >= 0f && now - s.FastSince >= MovingSeconds);
                if (moving)
                {
                    s.StillSince = -1f;
                    if (s.DoorUntil > now)
                    {
                        s.DoorUntil = now;
                        Core.Log.Msg("[CabinLight] " + Registry.Name(lv) + " drives off" + (gas ? " (throttle)" : "") + " - the door light goes out.");
                    }
                }
                else if (s.StillSince < 0f) s.StillSince = now;

                // Parked with a player aboard: the light comes on after a few seconds of standing. NPC cars that wait
                // at a junction stay dark.
                bool parked = !moving && s.StillSince >= 0f && now - s.StillSince >= StandSeconds && HasPlayer(lv);
                if (parked != s.Parked)
                {
                    s.Parked = parked;
                    if (parked) Core.Log.Msg("[CabinLight] " + Registry.Name(lv) + " stands with a player aboard - the light comes on.");
                }

                bool door = now < s.DoorUntil;
                bool want = mode == On || (mode == Auto && (door || parked));
                if (want != s.Want)
                {
                    s.Want = want;
                    s.FadeStart = now;
                    string why = mode == On ? "switch On" : mode == Off ? "switch Off" : door ? "door light" : parked ? "standing with a player aboard"
                        : moving ? "driving" : s.DoorUntil > 0f && now - s.DoorUntil < 0.5f ? "door time over" : "nothing to light";
                    Core.Log.Msg("[CabinLight] " + Registry.Name(lv) + ": light fades " + (want ? "in" : "out") + " (" + why + ", level " + s.Level.ToString("0.00") + ").");
                }
                bool near = cam == null || (lv.transform.position - eye).sqrMagnitude < Range * Range;
                float target = want && near ? 1f : 0f;
                s.Level = Mathf.MoveTowards(s.Level, target, dt / (target > s.Level ? FadeInSeconds : FadeSeconds));
                if (s.FadeStart >= 0f && s.Level == target)
                {
                    // A car out of range was dark already when its wish changed: nothing faded, nothing to tell.
                    if (now > s.FadeStart) Core.Log.Msg("[CabinLight] " + Registry.Name(lv) + ": light fully " + (target > 0f ? "in" : "out") + " after " + (now - s.FadeStart).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " s.");
                    s.FadeStart = -1f;
                }
                Show(lv, profile, s, s.Level);
            }

            if (Time.unscaledTime < _nextPrune) return;
            _nextPrune = Time.unscaledTime + 2f;
            _gone.Clear();
            foreach (var kv in _states) if (kv.Value.V == null) _gone.Add(kv.Key);
            foreach (int k in _gone)
            {
                if (_states[k].LensMat != null) UnityEngine.Object.Destroy(_states[k].LensMat);
                _states.Remove(k);
            }
        }

        /// <summary>Drops what a far, dark car was doing, so it is judged afresh once it comes near.</summary>
        private static void Forget(State s)
        {
            if (s.Riders < 0) return;
            s.Riders = -1;
            s.DoorUntil = 0f;
            s.FastSince = -1f;
            s.StillSince = -1f;
            s.Parked = false;
            s.Want = false;
            s.FadeStart = -1f;
        }

        /// <summary>Players and NPCs aboard.</summary>
        private static int Riders(LandVehicle lv)
        {
            int n = 0;
            var seats = lv.Seats;
            if (seats != null) foreach (var seat in seats) if (seat != null && seat.Occupant != null) n++;
            var npcs = lv.OccupantNPCs;
            if (npcs != null) foreach (var npc in npcs) if (npc != null) n++;
            return n;
        }

        private static void Show(LandVehicle lv, ChassisProfile profile, State s, float level)
        {
            bool on = level > 0f;
            if (s.Profile != profile || (s.Dome == null && on) || (s.Dome != null && s.Lens == null && s.LensMat != null))
            {
                s.Profile = profile;
                s.Dome = Frames.Resolve(Frames.Resolve(lv.transform, profile.Frame), DomeName);
                s.Light = null;
                s.Lens = null;
                if (s.LensMat != null) { UnityEngine.Object.Destroy(s.LensMat); s.LensMat = null; }
                s.Shown = -1f;
                if (s.Dome == null)
                {
                    if (!s.LoggedMissing) Core.Log.Msg("[CabinLight] " + Registry.Name(lv) + " has no " + DomeName + " yet (interior not built or off) - no cabin light.");
                    s.LoggedMissing = true;
                    return;
                }
                s.LoggedMissing = false;
                FindLens(lv, s);
            }
            if (s.Dome == null) return;

            // Eased at both ends: the light swells in and dies away instead of starting or stopping with a step.
            float glow = Mathf.SmoothStep(0f, 1f, level);
            if (Mathf.Abs(glow - s.Shown) < 1e-4f) return;
            s.Shown = glow;
            if (s.LensMat != null)
            {
                if (s.LensMat.HasProperty("_BaseColor")) s.LensMat.SetColor("_BaseColor", Color.Lerp(s.LensOff, LightColor, glow));
                s.LensMat.SetColor("_EmissionColor", LightColor * (3f * glow));
            }
            if (s.Light == null)
            {
                if (!on || _lightFailed) return;
                try
                {
                    var go = new GameObject("Chassis_DomeLamp");
                    go.transform.SetParent(s.Dome, false);
                    go.transform.localPosition = new Vector3(0f, -0.04f, 0f);
                    var light = go.AddComponent<Light>();
                    light.type = LightType.Point;
                    light.color = LightColor;
                    light.range = 1.8f;
                    light.intensity = Intensity;
                    light.shadows = LightShadows.None;
                    s.Light = light;
                    Core.Log.Msg("[CabinLight] " + Registry.Name(lv) + ": cabin light made.");
                }
                catch (Exception e)
                {
                    _lightFailed = true;
                    Core.Log.Error("[CabinLight] the point light could not be made, only the lamp glows from now on: " + e);
                    return;
                }
            }
            if (s.Light.enabled != on) s.Light.enabled = on;
            s.Light.intensity = Intensity * glow;
        }

        /// <summary>Gives the lens a material of its own, with emission on, so <see cref="Show"/> can fade its colour.</summary>
        private static void FindLens(LandVehicle lv, State s)
        {
            Renderer r = s.Dome.GetComponent<Renderer>();
            if (r == null)
            {
                foreach (var c in s.Dome.GetComponentsInChildren<Renderer>(true))
                {
                    if (c == null || c.name.StartsWith("Chassis_Outline", StringComparison.Ordinal) || c.name.StartsWith("Chassis_DomeLamp", StringComparison.Ordinal)) continue;
                    r = c;
                    break;
                }
            }
            if (r == null || r.sharedMaterial == null)
            {
                Core.Log.Warning("[CabinLight] " + Registry.Name(lv) + ": the lens " + DomeName + " has no renderer - only the point light fades.");
                return;
            }
            try
            {
                var mat = new Material(r.sharedMaterial) { name = r.sharedMaterial.name + "_ChassisDome" };
                s.LensOff = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : Color.white;
                if (mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", null);
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", Color.black);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                r.sharedMaterial = mat;
                s.Lens = r;
                s.LensMat = mat;
                Core.Log.Msg("[CabinLight] " + Registry.Name(lv) + ": lens '" + r.name + "' fades on its own material.");
            }
            catch (Exception e) { Core.Log.Error("[CabinLight] the lens material of " + Registry.Name(lv) + " could not be made, only the point light fades: " + e.Message); }
        }

        /// <summary>One line for the console and the tests: "cabin light on|off (switch Auto|On|Off, why), level ...".</summary>
        internal static string Describe(LandVehicle lv)
        {
            if (lv == null) return "cabin light: no vehicle";
            if (ChassisApi.For(lv)?.Control(ControlName) == null) return "cabin light: none in this profile";
            _states.TryGetValue(lv.GetInstanceID(), out var s);
            int mode = Mode(lv);
            float left = s != null ? s.DoorUntil - Time.time : 0f;
            string why = mode != Auto ? "held" : left > 0f ? "door, " + left.ToString("0.0") + " s left" : s != null && s.Parked ? "standing" : "waiting";
            return "cabin light " + (IsOn(lv) ? "on" : "off") + " (switch " + ModeName(mode) + ", " + why + "), level " + (s != null ? s.Level : 0f).ToString("0.00")
                + ", riders " + (s != null ? s.Riders : -1) + ", lamp " + (s?.Dome != null ? "found" : "not found") + ", lens " + (s?.LensMat != null ? "fading" : "plain") + ", point light " + (s?.Light != null ? (s.Light.enabled ? "lit" : "dark") : "not made");
        }

        internal static void Reset()
        {
            foreach (var st in _states.Values) if (st.LensMat != null) UnityEngine.Object.Destroy(st.LensMat);
            _states.Clear();
            _nextPrune = 0f;
            _lightFailed = false;
        }
    }
}
