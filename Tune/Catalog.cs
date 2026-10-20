using System;
using System.Collections.Generic;
using Chassis.Api;

namespace Chassis.Tune
{
    /// <summary>
    /// The registered tuning parts by id. Chassis brings performance stages without a model; mods add visual parts.
    /// Every peer registers the same parts, the state only carries their ids.
    /// </summary>
    internal static class Catalog
    {
        private static readonly Dictionary<string, TuningPart> _parts = new Dictionary<string, TuningPart>(StringComparer.OrdinalIgnoreCase);
        private static bool _builtIn;

        /// <summary>Raised with the part id after a part was registered or removed; installed copies are rebuilt.</summary>
        internal static event Action<string> Changed;

        internal static TuningPart Get(string id)
        {
            EnsureBuiltIn();
            return !string.IsNullOrEmpty(id) && _parts.TryGetValue(id, out var p) ? p : null;
        }

        internal static IEnumerable<TuningPart> All
        {
            get { EnsureBuiltIn(); return new List<TuningPart>(_parts.Values); }
        }

        internal static void Register(TuningPart part)
        {
            if (part == null) throw new ArgumentNullException(nameof(part));
            if (string.IsNullOrEmpty(part.Id)) throw new ArgumentException("a tuning part needs an id");
            if (string.IsNullOrEmpty(part.Slot)) throw new ArgumentException("tuning part '" + part.Id + "' needs a slot");
            foreach (char ch in part.Id + part.Slot)
                if (!(char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.')) throw new ArgumentException("tuning part ids and slots are letters, digits, '_', '-' and '.': " + part.Id + " / " + part.Slot);
            EnsureBuiltIn();
            bool replaced = _parts.ContainsKey(part.Id);
            _parts[part.Id] = part;
            Core.Log.Msg("[Tuning] part '" + part.Id + "' " + (replaced ? "replaced" : "registered") + ": slot " + part.Slot + ", " + (part.Build != null ? "model" : "no model") + ", " + (part.Stats != null ? part.Stats.ToString() : "no stats") + ", " + (part.Fits == null || part.Fits.Count == 0 ? "any car" : string.Join("/", part.Fits.Keys)) + ".");
            Raise(part.Id);
        }

        internal static bool Remove(string id)
        {
            EnsureBuiltIn();
            if (string.IsNullOrEmpty(id) || !_parts.Remove(id)) { Core.Log.Warning("[Tuning] remove: no part '" + id + "' is registered."); return false; }
            Core.Log.Msg("[Tuning] part '" + id + "' removed; cars that carry it show their stock part until it is registered again.");
            Raise(id);
            return true;
        }

        private static void Raise(string id)
        {
            if (Changed == null) return;
            try { Changed(id); }
            catch (Exception e) { Core.Log.Error("[Tuning] a part change handler failed for '" + id + "': " + e); }
        }

        private static void EnsureBuiltIn()
        {
            if (_builtIn) return;
            _builtIn = true;
            Stage("engine_1", "engine", "Engine Tune 1", 1500, new TuningStats { Torque = 1.10f, EnginePitch = 1.03f });
            Stage("engine_2", "engine", "Engine Tune 2", 3500, new TuningStats { Torque = 1.20f, EnginePitch = 1.06f });
            Stage("engine_3", "engine", "Engine Tune 3", 7000, new TuningStats { Torque = 1.35f, EnginePitch = 1.10f });
            Stage("brakes_1", "brakes", "Street Brakes", 1000, new TuningStats { Brake = 1.20f });
            Stage("brakes_2", "brakes", "Sport Brakes", 2000, new TuningStats { Brake = 1.40f });
            Stage("brakes_3", "brakes", "Race Brakes", 4000, new TuningStats { Brake = 1.60f });
            Stage("transmission_1", "transmission", "Street Transmission", 1500, new TuningStats { Gearing = 1.10f });
            Stage("transmission_2", "transmission", "Sport Transmission", 3000, new TuningStats { Gearing = 1.20f });
            Stage("transmission_3", "transmission", "Race Transmission", 5000, new TuningStats { Gearing = 1.30f });
            Stage("suspension_1", "suspension", "Lowered Suspension", 1000, new TuningStats { RideHeight = -0.02f, Spring = 1.10f, Damper = 1.10f });
            Stage("suspension_2", "suspension", "Street Suspension", 2000, new TuningStats { RideHeight = -0.04f, Spring = 1.20f, Damper = 1.20f });
            Stage("suspension_3", "suspension", "Sport Suspension", 3500, new TuningStats { RideHeight = -0.06f, Spring = 1.30f, Damper = 1.30f });
            Stage("suspension_4", "suspension", "Competition Suspension", 5000, new TuningStats { RideHeight = -0.08f, Spring = 1.40f, Damper = 1.40f });
            Stage("turbo", "turbo", "Turbo Tuning", 5000, new TuningStats { Torque = 1.15f, EnginePitch = 1.05f });
            Stage("tyres_drift", "tyres", "Drift Tyres", 1500, new TuningStats { Grip = 0.7f });
        }

        private static void Stage(string id, string slot, string label, int price, TuningStats stats)
            => _parts[id] = new TuningPart { Id = id, Slot = slot, Label = label, Price = price, Stats = stats };

    }
}
