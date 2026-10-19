using System.Collections.Generic;
using Chassis.Api;
using Chassis.Net;

namespace Chassis.Vehicles
{
    /// <summary>
    /// Automatic headlights for profiles with <see cref="ChassisProfile.AutoLights"/>: a vehicle that is driven has its
    /// headlights on from 17:30 to 06:00, the hours vanilla's NPC drivers switch theirs on (<c>NPC.cs:687</c>).
    ///
    /// It switches only when the wish changes: night falls, the day breaks, a drive starts or ends. A player who presses
    /// the light key in between keeps that choice until the next change. After a drive at night the headlights stay on
    /// for a minute, or until the vehicle is locked. Runs on the host, which writes the lights' SyncVar for everyone.
    /// </summary>
    internal static class AutoLights
    {
        private const int OnFrom = 1730, OffFrom = 600;
        private const float CheckEvery = 1f;
        /// <summary>An empty vehicle moving faster than this is driven by something (a summon, an autopilot).</summary>
        private const float MovingKmh = 3f;
        private const float AfterglowSeconds = 60f;

        private static readonly Dictionary<int, bool> Wanted = new Dictionary<int, bool>();
        private static readonly Dictionary<int, bool> WasDriven = new Dictionary<int, bool>();
        private static readonly Dictionary<int, float> GlowUntil = new Dictionary<int, float>();
        private static float _next;
        private static bool _warned;

        internal static void Tick()
        {
            if (!ChassisNet.IsHost) return;
            float now = Time.time;
            if (now < _next) return;
            _next = now + CheckEvery;
            if (!NetworkSingleton<GameTimeManager>.InstanceExists)
            {
                if (!_warned) Core.Log.Warning("[Lights] no clock yet - automatic headlights wait.");
                _warned = true;
                return;
            }
            _warned = false;
            bool night = NetworkSingleton<GameTimeManager>.Instance.IsCurrentTimeWithinRange(OnFrom, OffFrom);
            foreach (var lv in Registry.Profiled)
            {
                if (lv == null || !(ChassisApi.For(lv)?.AutoLights ?? false)) continue;
                int key = lv.GetInstanceID();
                bool driven = lv.IsOccupied || (lv.Agent != null && lv.Agent.AutoDriving) || Mathf.Abs(lv.Speed_Kmh) > MovingKmh;
                bool glow = Afterglow(lv, key, now, night, driven);
                bool want = night && (driven || glow);
                if (Wanted.TryGetValue(key, out bool was) && was == want) continue;
                Wanted[key] = want;
                Apply(lv, want, night, driven, glow);
            }
        }

        private static bool Afterglow(LandVehicle lv, int key, float now, bool night, bool driven)
        {
            WasDriven.TryGetValue(key, out bool wasDriven);
            WasDriven[key] = driven;
            if (driven) { GlowUntil.Remove(key); return false; }
            if (wasDriven && night)
            {
                GlowUntil[key] = now + AfterglowSeconds;
                Core.Log.Msg("[Lights] " + Registry.Name(lv) + " parked at night - headlights stay on for " + AfterglowSeconds.ToString("0") + " s or until it is locked.");
            }
            if (!GlowUntil.TryGetValue(key, out float until)) return false;
            string end = !night ? "the day broke" : Fleet.For(lv)?.Locked == true ? "it was locked" : now >= until ? "the minute is up" : null;
            if (end == null) return true;
            GlowUntil.Remove(key);
            Core.Log.Msg("[Lights] " + Registry.Name(lv) + " afterglow ends: " + end + ".");
            return false;
        }

        private static void Apply(LandVehicle lv, bool on, bool night, bool driven, bool glow)
        {
            var lights = lv.lights;
            if (lights == null) { Core.Log.Warning("[Lights] " + Registry.Name(lv) + " has no lights to switch."); return; }
            if (lights.HeadlightsOn == on) return;
            try
            {
                lights.HeadlightsOn = on;
                Core.Log.Msg("[Lights] " + Registry.Name(lv) + " headlights " + (on ? "on" : "off") + " (" + (night ? "night" : "day") + ", " + (driven ? "driven" : glow ? "afterglow" : "parked") + ").");
            }
            catch (System.Exception e) { Core.Log.Error("[Lights] switching the headlights of " + Registry.Name(lv) + " failed: " + e.Message); }
        }

        internal static void Reset()
        {
            Wanted.Clear();
            WasDriven.Clear();
            GlowUntil.Clear();
            _next = 0f;
            _warned = false;
        }
    }
}
