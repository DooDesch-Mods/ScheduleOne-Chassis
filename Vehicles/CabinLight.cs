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
    /// (vanilla has no ignition, so throttle and speed stand in for it) and fades over a second. Every peer sees the same
    /// seats and the same speed and works that out on its own. The host puts the switch back to Auto when the last
    /// occupant gets out, and it is never saved. The lamp glows through the gauge <see cref="ControlName"/>; a point
    /// light below it lights the cabin.
    /// </summary>
    internal static class CabinLight
    {
        internal const string DomeName = "Chassis_DomeLight";
        internal const string ControlName = "dome";
        /// <summary>Switch positions, stored as the value of the switch <see cref="ControlName"/>.</summary>
        internal const int Auto = 0, On = 1, Off = 2;
        /// <summary>After someone gets in or out the light stays on this long, as the door delay of a real car.</summary>
        internal const float DoorSeconds = 15f;
        private const float FadeSeconds = 1f, FadeInSeconds = 0.12f;
        /// <summary>A car with a player aboard that stands this long lights up.</summary>
        internal const float StandSeconds = 3f;
        /// <summary>Above this speed, held for <see cref="MovingSeconds"/>, the car drives (raw km/h, either way).</summary>
        private const float MovingKmh = 3f, MovingSeconds = 0.3f;
        /// <summary>Lights farther than this from the camera are not lit.</summary>
        private const float Range = 40f;
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
            internal Light Light;
            internal Transform Dome;
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
            foreach (var lv in Registry.Profiled)
            {
                if (lv == null) continue;
                var profile = ChassisApi.For(lv);
                if (profile == null || profile.Control(ControlName) == null) continue;
                int key = lv.GetInstanceID();
                if (!_states.TryGetValue(key, out var s)) _states[key] = s = new State { V = lv };
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

                bool want = mode == On || (mode == Auto && (now < s.DoorUntil || parked));
                bool near = cam == null || (lv.transform.position - eye).sqrMagnitude < Range * Range;
                float target = want && near ? 1f : 0f;
                // On within a blink, off over a second, as the theatre dimming of a real car.
                s.Level = target > s.Level ? Mathf.MoveTowards(s.Level, 1f, dt / FadeInSeconds) : Mathf.MoveTowards(s.Level, 0f, dt / FadeSeconds);
                Show(lv, profile, s, s.Level);
            }

            if (Time.unscaledTime < _nextPrune) return;
            _nextPrune = Time.unscaledTime + 2f;
            _gone.Clear();
            foreach (var kv in _states) if (kv.Value.V == null) _gone.Add(kv.Key);
            foreach (int k in _gone) _states.Remove(k);
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
            if (s.Profile != profile || (s.Dome == null && on))
            {
                s.Profile = profile;
                s.Dome = Frames.Resolve(Frames.Resolve(lv.transform, profile.Frame), DomeName);
                s.Light = null;
                if (s.Dome == null)
                {
                    if (!s.LoggedMissing) Core.Log.Msg("[CabinLight] " + Registry.Name(lv) + " has no " + DomeName + " yet (interior not built or off) - no cabin light.");
                    s.LoggedMissing = true;
                    return;
                }
                s.LoggedMissing = false;
            }
            if (s.Dome == null) return;
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
            s.Light.intensity = Intensity * level;
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
                + ", riders " + (s != null ? s.Riders : -1) + ", lamp " + (s?.Dome != null ? "found" : "not found") + ", point light " + (s?.Light != null ? (s.Light.enabled ? "lit" : "dark") : "not made");
        }

        internal static void Reset()
        {
            _states.Clear();
            _nextPrune = 0f;
            _lightFailed = false;
        }
    }
}
