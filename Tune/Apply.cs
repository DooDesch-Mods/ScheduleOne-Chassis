using System;
using System.Collections.Generic;
using System.Globalization;
using Chassis.Api;
using Chassis.Compat;
using Chassis.Net;
using Chassis.Vehicles;

namespace Chassis.Tune
{
    /// <summary>
    /// The one path from a car's tuning state to the car. Each aspect (driving, paint, parts, wheels, lights, smoke,
    /// windows) remembers what it last applied and only touches the car when the wanted value differs, so applying twice
    /// changes nothing. Runs on every peer: the physics moves between the driver's peer and the host, and every
    /// player sees the parts.
    /// </summary>
    internal static class Apply
    {
        /// <summary>What Chassis did to one car, so it can be undone.</summary>
        internal sealed class Car
        {
            internal LandVehicle V;
            internal int Id;
            internal int Seen = -1;
            internal string Code;

            internal Handling.Stock Stock;
            internal string HandlingKey = "";
            internal TuningStats Stats;

            internal readonly List<Paint.Slot> PaintSlots = new List<Paint.Slot>();
            internal string PaintKey = "";

            internal string PartsKey = "";
            internal readonly List<GameObject> Built = new List<GameObject>();
            internal readonly List<GameObject> Hidden = new List<GameObject>();
            internal readonly List<Body.CutMesh> Cuts = new List<Body.CutMesh>();
            internal List<Bounds> Boxes = new List<Bounds>();
            internal string BoxesKey = "";

            internal string WheelsKey = "";
            internal readonly List<Body.WheelSwap> Wheels = new List<Body.WheelSwap>();

            internal string XenonKey = "";
            internal Material XenonStock;
            internal readonly List<KeyValuePair<Light, Color>> XenonLights = new List<KeyValuePair<Light, Color>>();

            internal string NeonKey = "";
            internal GameObject Neon;

            internal string SmokeKey = "";
            internal readonly List<KeyValuePair<ParticleSystem, Color>> Smoke = new List<KeyValuePair<ParticleSystem, Color>>();

            internal string TintKey = "";
            internal bool TintWarned;
        }

        private static readonly Dictionary<int, Car> _cars = new Dictionary<int, Car>();
        private static readonly List<int> _gone = new List<int>();
        private static float _nextVerify;
        // Goes up when a part is registered or removed, so cars that carry it build it again.
        private static int _epoch;
        private static bool _hooked;

        internal static IEnumerable<Car> Cars => _cars.Values;

        internal static Car For(LandVehicle v)
        {
            if (v == null) return null;
            int key = v.GetInstanceID();
            if (_cars.TryGetValue(key, out var c) && c.V != null) return c;
            c = new Car { V = v, Id = Ids.Of(v), Code = ChassisApi.CodeOf(v) };
            _cars[key] = c;
            return c;
        }

        internal static Car Find(LandVehicle v) => v != null && _cars.TryGetValue(v.GetInstanceID(), out var c) && c.V != null ? c : null;

        internal static void Tick()
        {
            if (!_hooked)
            {
                _hooked = true;
                Catalog.Changed += id => { _epoch++; foreach (var c in _cars.Values) c.Seen = -1; };
            }
            foreach (var v in Registry.All)
            {
                if (v == null) continue;
                int id = Ids.Of(v);
                if (id < 0) continue;
                int version = State.Version(id);
                var known = Find(v);
                // An untuned car Chassis never touched needs no record.
                if (known == null && State.Of(id).Count == 0) continue;
                var c = known ?? For(v);
                if (c.Id != id) { c.Id = id; c.Seen = -1; }
                if (c.Seen == version) continue;
                c.Seen = version;
                All(c);
            }
            if (Time.unscaledTime >= _nextVerify)
            {
                _nextVerify = Time.unscaledTime + 1f;
                Verify();
            }
        }

        /// <summary>Applies every aspect of one car. A failing aspect logs and the others still run.</summary>
        internal static void All(Car c)
        {
            var s = State.Of(c.Id);
            Step(c, "driving", () => Handling.Apply(c, StatsOf(c, s)));
            Step(c, "paint", () => Paint.Apply(c, Get(s, "paint")));
            Step(c, "parts", () => Body.ApplyParts(c, s, _epoch));
            Step(c, "wheels", () => Body.ApplyWheels(c, s, _epoch));
            Step(c, "xenon", () => Glow.Xenon(c, Get(s, "xenon")));
            Step(c, "neon", () => Glow.Neon(c, Get(s, "neon")));
            Step(c, "tyre smoke", () => Glow.Smoke(c, Get(s, "smoke")));
            Step(c, "window tint", () => Tint(c, Get(s, "tint")));
        }

        private static void Step(Car c, string what, Action a)
        {
            try { a(); }
            catch (Exception e) { Core.Log.Error("[Tuning] applying the " + what + " of " + Registry.Name(c.V) + " failed: " + e); }
        }

        private static string Get(Dictionary<string, string> s, string key) => s.TryGetValue(key, out var v) && v.Length > 0 ? v : null;

        /// <summary>The parts installed on the car, by slot, that are registered and fit its code.</summary>
        internal static List<TuningPart> Parts(Car c, Dictionary<string, string> s)
        {
            var list = new List<TuningPart>();
            foreach (var kv in s)
            {
                if (!kv.Key.StartsWith("part.", StringComparison.OrdinalIgnoreCase) || kv.Value.Length == 0) continue;
                var p = Catalog.Get(kv.Value);
                if (p == null) { Warn(c, kv.Value, "is not registered on this peer - the slot stays stock"); continue; }
                if (!p.FitsCode(c.Code)) { Warn(c, kv.Value, "does not fit a " + c.Code + " - the slot stays stock"); continue; }
                list.Add(p);
            }
            return list;
        }

        private static readonly HashSet<string> _warned = new HashSet<string>();

        private static void Warn(Car c, string part, string why)
        {
            if (_warned.Add(c.Id + "|" + part + "|" + why)) Core.Log.Warning("[Tuning] " + Registry.Name(c.V) + ": part '" + part + "' " + why + ".");
        }

        internal static TuningStats StatsOf(Car c, Dictionary<string, string> s)
        {
            TuningStats all = null;
            foreach (var p in Parts(c, s)) if (p.Stats != null) all = TuningStats.Combine(all, p.Stats);
            return all ?? new TuningStats();
        }

        // ---- windows -----------------------------------------------------------------------------------------------

        /// <summary>The tinted glass colour the tuning asks for on this car, or null for the profile's own tint.</summary>
        internal static Color? TintOf(LandVehicle v)
        {
            string t = State.Get(v, "tint");
            return t != null && Values.Rgba(t, out Color c) ? c : (Color?)null;
        }

        private static void Tint(Car c, string value)
        {
            string key = value ?? "";
            if (key == c.TintKey) return;
            c.TintKey = key;
            if (Interior.BaseInterior.RebuildVehicle(c.V))
            {
                Core.Log.Msg("[Tuning] " + Registry.Name(c.V) + ": windows " + (value != null ? "tinted " + value : "back to the profile's tint") + ", interior rebuilt.");
                return;
            }
            if (value != null && !c.TintWarned)
            {
                c.TintWarned = true;
                Core.Log.Warning("[Tuning] " + Registry.Name(c.V) + " has no base interior with glass - the window tint " + value + " shows once one is built.");
            }
        }

        // ---- checks once a second ----------------------------------------------------------------------------------

        private static void Verify()
        {
            _gone.Clear();
            foreach (var kv in _cars)
            {
                var c = kv.Value;
                if (c.V == null) { _gone.Add(kv.Key); continue; }
                try { Paint.Verify(c); }
                catch (Exception e) { Core.Log.Error("[Tuning] checking the paint of " + Registry.Name(c.V) + " failed: " + e.Message); }
                try { Body.VerifyCuts(c); }
                catch (Exception e) { Core.Log.Error("[Tuning] checking the body cuts of " + Registry.Name(c.V) + " failed: " + e.Message); }
            }
            foreach (int k in _gone) { Handling.Forget(k); _cars.Remove(k); }
        }

        // ---- console -----------------------------------------------------------------------------------------------

        internal static string Describe(LandVehicle v)
        {
            int id = Ids.Of(v);
            var s = State.Of(id);
            var lines = new List<string> { Registry.Name(v) + ": " + (s.Count == 0 ? "stock" : s.Count + " tuning key(s)") };
            foreach (var kv in s) lines.Add("  " + kv.Key + " = " + kv.Value);
            var c = Find(v);
            lines.Add("  driving: " + (c?.Stats != null ? c.Stats.ToString() : "stock"));
            return string.Join("\n", lines);
        }

        internal static void Reset()
        {
            _cars.Clear();
            _warned.Clear();
            _nextVerify = 0f;
            Handling.Reset();
            Body.Reset();
            Glow.Reset();
        }

        internal static string F(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
