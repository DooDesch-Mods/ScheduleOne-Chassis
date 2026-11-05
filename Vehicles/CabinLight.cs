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
    /// It comes on by itself when someone gets in or out and goes off after <see cref="AutoSeconds"/> or once the car
    /// drives off, like the courtesy light of a real car; vanilla has no ignition, so the doors are the only cue. Every
    /// peer sees the same seats change and works that out on its own. A click on the lamp keeps it on (the switch
    /// <see cref="ControlName"/>); the host turns that off again when the last occupant gets out, and it is never saved.
    /// The lamp glows through the gauge <see cref="ControlName"/>; a point light below it lights the cabin.
    /// </summary>
    internal static class CabinLight
    {
        internal const string DomeName = "Chassis_DomeLight";
        internal const string ControlName = "dome";
        internal const float AutoSeconds = 8f;
        /// <summary>Above this shown speed the automatic light goes off: the car drives off.</summary>
        private const float DriveOffKmh = 8f;
        /// <summary>Lights farther than this from the camera are not lit.</summary>
        private const float Range = 40f;

        internal static readonly Color LightColor = new Color(1f, 0.86f, 0.62f);

        private sealed class State
        {
            internal LandVehicle V;
            internal int Riders = -1;
            internal float AutoUntil;
            internal Light Light;
            internal Transform Dome;
            internal ChassisProfile Profile;
            internal bool LoggedMissing;
        }

        private static readonly Dictionary<int, State> _states = new Dictionary<int, State>();
        private static readonly List<int> _gone = new List<int>();
        private static float _nextPrune;
        private static bool _lightFailed;

        /// <summary>The light is on: the automatic window runs or the switch holds it on.</summary>
        internal static bool IsOn(LandVehicle lv)
        {
            if (lv == null) return false;
            if (Held(lv)) return true;
            return _states.TryGetValue(lv.GetInstanceID(), out var s) && Time.time < s.AutoUntil;
        }

        private static bool Held(LandVehicle lv)
        {
            var cab = Store.Of(lv);
            return cab != null && cab.Controls.TryGetValue(ControlName, out float v) && v > 0.5f;
        }

        internal static void Tick()
        {
            Camera cam = Camera.main;
            Vector3 eye = cam != null ? cam.transform.position : Vector3.zero;
            float now = Time.time;
            foreach (var lv in Registry.Profiled)
            {
                if (lv == null) continue;
                var profile = ChassisApi.For(lv);
                if (profile == null || profile.Control(ControlName) == null) continue;
                int key = lv.GetInstanceID();
                if (!_states.TryGetValue(key, out var s)) _states[key] = s = new State { V = lv };

                int riders = Riders(lv);
                if (s.Riders >= 0 && riders != s.Riders)
                {
                    s.AutoUntil = now + AutoSeconds;
                    Core.Log.Msg("[CabinLight] " + Registry.Name(lv) + ": " + (riders > s.Riders ? "someone got in" : "someone got out") + " (" + s.Riders + " -> " + riders + ") - light on for " + AutoSeconds.ToString("0") + " s.");
                    if (riders == 0 && ChassisNet.IsHost && Held(lv))
                    {
                        Core.Log.Msg("[CabinLight] " + Registry.Name(lv) + " is empty - the switch goes back to automatic.");
                        try { Controls.UseFromApi(lv, ControlName, 0f); }
                        catch (Exception e) { Core.Log.Error("[CabinLight] switching the cabin light of " + Registry.Name(lv) + " back to automatic failed: " + e.Message); }
                    }
                }
                s.Riders = riders;
                if (s.AutoUntil > now && Sources.Kmh(lv) > DriveOffKmh)
                {
                    s.AutoUntil = 0f;
                    Core.Log.Msg("[CabinLight] " + Registry.Name(lv) + " drives off - the automatic light goes off.");
                }

                bool near = cam == null || (lv.transform.position - eye).sqrMagnitude < Range * Range;
                Show(lv, profile, s, near && IsOn(lv));
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

        private static void Show(LandVehicle lv, ChassisProfile profile, State s, bool on)
        {
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
                    light.intensity = 1.4f;
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
        }

        internal static string Describe(LandVehicle lv)
        {
            if (lv == null) return "cabin light: no vehicle";
            if (ChassisApi.For(lv)?.Control(ControlName) == null) return "cabin light: none in this profile";
            _states.TryGetValue(lv.GetInstanceID(), out var s);
            float left = s != null ? s.AutoUntil - Time.time : 0f;
            return "cabin light " + (IsOn(lv) ? "on" : "off") + (Held(lv) ? " (switch on)" : left > 0f ? " (automatic, " + left.ToString("0.0") + " s left)" : " (automatic)")
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
