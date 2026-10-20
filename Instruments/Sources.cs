using System;
using System.Collections.Generic;
using Chassis.Api;
using Chassis.Vehicles;

namespace Chassis.Instruments
{
    /// <summary>
    /// The values gauges and the telemetry snapshot read, by name. Built-in sources come from the vehicle's synced
    /// state, from Chassis' own sync (pedals, odometer) and from the game clock; mods add their own.
    ///
    /// Speeds are shown as vanilla's dormant <c>SPEED_DISPLAY_MULTIPLIER</c> intends: <c>Speed_Kmh</c> times 1.4. The
    /// odometer counts with the same factor, so speedometer and odometer agree.
    /// </summary>
    internal static class Sources
    {
        /// <summary>vanilla LandVehicle.SPEED_DISPLAY_MULTIPLIER, defined there but never used.</summary>
        internal const float DisplayFactor = 1.4f;
        internal const float KmPerMile = 1.609344f;
        private const float IdleRpm = 800f, MaxRpm = 6000f;

        internal sealed class Def
        {
            internal string Name;
            internal string Unit;
            internal Func<LandVehicle, GaugeReading> Read;
            internal bool Custom;
        }

        private static readonly Dictionary<string, Def> _defs = new Dictionary<string, Def>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _failed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Speeds and distances in miles. From the <c>SpeedUnit</c> preference.</summary>
        internal static bool Mph;

        static Sources()
        {
            Add("speed", "km/h or mph (the SpeedUnit setting)", v => Ok(Kmh(v) / (Mph ? KmPerMile : 1f)));
            Add("speed_kmh", "km/h", v => Ok(Kmh(v)));
            Add("speed_mph", "mph", v => Ok(Kmh(v) / KmPerMile));
            Add("speed_raw", "km/h, signed, without the display factor", v => Ok(v.Speed_Kmh));
            Add("rpm", "revolutions per minute, 0 with nobody aboard", Rpm);
            Add("throttle", "-1 (reverse) to 1", v => Pedals.Throttle(v));
            Add("handbrake", "0 or 1", v => Pedals.Handbrake(v));
            Add("brake", "0 or 1", v => Ok(v.BrakesApplied ? 1f : 0f));
            Add("reverse", "0 or 1", v => Ok(v.IsReversing ? 1f : 0f));
            Add("lights", "0 or 1, the headlights", v => v.lights != null ? Ok(v.lights.HeadlightsOn ? 1f : 0f) : GaugeReading.Invalid);
            Add("indicator", "-1 left, 0 off, 1 right; 0 while the hazard lights flash", v => Ok(Mathf.Abs(Indicators.SideOf(v)) == 1 ? Indicators.SideOf(v) : 0f));
            Add("hazard", "0 or 1", v => Ok(Indicators.SideOf(v) == Indicators.Hazard ? 1f : 0f));
            Add("indicator_left", "0 or 1, lit in the flash", v => Ok(Lit(v, -1) ? 1f : 0f));
            Add("indicator_right", "0 or 1, lit in the flash", v => Ok(Lit(v, 1) ? 1f : 0f));
            Add("steer", "-1 full left to 1 full right", v => Ok(v.ActualMaxSteeringAngle > 0.01f ? Mathf.Clamp(v.CurrentSteerAngle / v.ActualMaxSteeringAngle, -1f, 1f) : 0f));
            Add("steer_deg", "degrees of the front wheels, right positive", v => Ok(v.CurrentSteerAngle));
            Add("clock", "game time as HHMM, e.g. 1730", v => Clock(out int t) ? Ok(t) : GaugeReading.Invalid);
            Add("clock_hours", "0 to 12, for an hour hand", v => Clock(out int t) ? Ok(t / 100 % 12 + t % 100 / 60f) : GaugeReading.Invalid);
            Add("clock_minutes", "0 to 60, for a minute hand", v => Clock(out int t) ? Ok(t % 100) : GaugeReading.Invalid);
            Add("odometer", "km or miles (the SpeedUnit setting)", v => Odometer.Read(v, false, Mph));
            Add("odometer_km", "km", v => Odometer.Read(v, false, false));
            Add("trip", "km or miles (the SpeedUnit setting)", v => Odometer.Read(v, true, Mph));
            Add("trip_km", "km", v => Odometer.Read(v, true, false));
            Add("occupied", "0 or 1, anyone aboard (players or NPCs)", v => Ok(v.IsOccupied ? 1f : 0f));
            Add("driver", "0 or 1, someone at the wheel (a player or an NPC)", v => Ok(HasDriver(v) ? 1f : 0f));
        }

        private static void Add(string name, string unit, Func<LandVehicle, GaugeReading> read) =>
            _defs[name] = new Def { Name = name, Unit = unit, Read = read };

        private static GaugeReading Ok(float v) => new GaugeReading(v, true);

        internal static float Kmh(LandVehicle v) => Mathf.Abs(v.Speed_Kmh) * DisplayFactor;

        private static bool Lit(LandVehicle v, int side)
        {
            int s = Indicators.SideOf(v);
            return Indicators.Phase && (s == side || s == Indicators.Hazard);
        }

        internal static bool HasDriver(LandVehicle v)
        {
            if (v.DriverPlayer != null) return true;
            var npcs = v.OccupantNPCs;
            return npcs != null && npcs.Length > 0 && npcs[0] != null;
        }

        private static GaugeReading Rpm(LandVehicle v)
        {
            if (!v.IsOccupied) return Ok(0f);
            float top = v.TopSpeed > 1f ? v.TopSpeed : 60f;
            float f = Mathf.Clamp01(Mathf.Abs(v.Speed_Kmh) / top);
            // Standing with the throttle down revs the engine; vanilla has no engine model, VehicleSound pitches by speed alone.
            var t = Pedals.Throttle(v);
            float rev = t.Valid ? Mathf.Abs(t.Value) * (1f - f) * 0.35f : 0f;
            return Ok(IdleRpm + (MaxRpm - IdleRpm) * Mathf.Clamp01(f + rev));
        }

        private static bool Clock(out int hhmm)
        {
            hhmm = 0;
            if (!NetworkSingleton<GameTimeManager>.InstanceExists) return false;
            hhmm = NetworkSingleton<GameTimeManager>.Instance.CurrentTime;
            return true;
        }

        internal static void Register(string name, Func<LandVehicle, float> read)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("a gauge source needs a name");
            if (read == null) throw new ArgumentNullException(nameof(read));
            if (_defs.TryGetValue(name, out var old) && !old.Custom) throw new ArgumentException("'" + name + "' is a built-in gauge source");
            _defs[name] = new Def
            {
                Name = name,
                Unit = "set by a mod",
                Custom = true,
                Read = v =>
                {
                    float x = read(v);
                    return float.IsNaN(x) || float.IsInfinity(x) ? GaugeReading.Invalid : new GaugeReading(x, true);
                },
            };
            _failed.Remove(name);
            Core.Log.Msg("[Gauges] source '" + name + "' registered by a mod.");
        }

        internal static bool Remove(string name)
        {
            if (!_defs.TryGetValue(name ?? string.Empty, out var d) || !d.Custom) return false;
            _defs.Remove(name);
            Core.Log.Msg("[Gauges] source '" + name + "' removed.");
            return true;
        }

        internal static IEnumerable<Def> All => _defs.Values;

        internal static bool Exists(string name) => !string.IsNullOrEmpty(name) && _defs.ContainsKey(name);

        /// <summary>A source's current value. An unknown name or a failing mod source reads as not valid.</summary>
        internal static GaugeReading Read(LandVehicle v, string name)
        {
            if (v == null || string.IsNullOrEmpty(name) || !_defs.TryGetValue(name, out var d)) return GaugeReading.Invalid;
            try { return d.Read(v); }
            catch (Exception e)
            {
                // One error per source; a gauge reads it every frame.
                if (_failed.Add(name)) Core.Log.Error("[Gauges] source '" + name + "' failed on " + Registry.Name(v) + ", it reads as not valid from now on: " + e);
                return GaugeReading.Invalid;
            }
        }

        /// <summary>Reads <c>SpeedUnit</c> from the Chassis preferences: "kmh" (default) or "mph".</summary>
        internal static void LoadSetting()
        {
            try
            {
                var cat = MelonLoader.MelonPreferences.CreateCategory("Chassis");
                var entry = cat.CreateEntry("SpeedUnit", "kmh", "Speed unit", "kmh or mph: the speedometer and the odometer in the cars.");
                string unit = (entry.Value ?? string.Empty).Trim().ToLowerInvariant();
                Mph = unit == "mph";
                if (unit != "mph" && unit != "kmh") Core.Log.Warning("[Gauges] SpeedUnit '" + entry.Value + "' is neither kmh nor mph - km/h it is.");
                Core.Log.Msg("[Gauges] speeds in " + (Mph ? "mph" : "km/h") + " (preference SpeedUnit).");
            }
            catch (Exception e)
            {
                Mph = false;
                Core.Log.Warning("[Gauges] reading the SpeedUnit preference failed - speeds stay in km/h: " + e.Message);
            }
        }

        internal static void Reset() => _failed.Clear();
    }
}
