using System;
using System.Collections.Generic;
using Chassis.Tune;

namespace Chassis.Api
{
    public static partial class ChassisApi
    {
        // ---- tuning ------------------------------------------------------------------------------------------------
        //
        // A car's tuning is a list of keys: part.<slot> (part id), paint and paint2 (RRGGBB,finish), rims (RRGGBB,finish),
        // xenon (RRGGBB), neon (RRGGBB,sides from f b l r), tint (RRGGBBAA), smoke (RRGGBB), horn (sound name), and keys
        // of your own. Every setter asks the host: it accepts a request from the car's owner, and everything a mod or the
        // console does on the host itself. Refusals are logged with the reason. The host saves the tuning of player
        // cars with the game.

        /// <summary>Raised on every peer with the car and the key after its tuning changed ("*": everything at once).</summary>
        public static event Action<LandVehicle, string> TuningChanged;

        /// <summary>Adds or replaces a tuning part. Register the same parts on every peer; cars that carry it rebuild.</summary>
        public static void RegisterTuningPart(TuningPart part) => Catalog.Register(part);

        /// <summary>Removes a tuning part; cars that carry it show the stock part until it is registered again.</summary>
        public static bool RemoveTuningPart(string id) => Catalog.Remove(id);

        /// <summary>The registered part with this id, or null.</summary>
        public static TuningPart TuningPartById(string id) => Catalog.Get(id);

        /// <summary>The registered parts that fit this car, optionally only one slot.</summary>
        public static List<TuningPart> TuningParts(LandVehicle vehicle, string slot = null)
        {
            var list = new List<TuningPart>();
            string code = CodeOf(vehicle);
            foreach (var p in Catalog.All)
                if (p.FitsCode(code) && (slot == null || string.Equals(p.Slot, slot, StringComparison.OrdinalIgnoreCase))) list.Add(p);
            return list;
        }

        /// <summary>Installs a part into its slot, replacing what was there.</summary>
        public static bool InstallPart(LandVehicle vehicle, string partId)
        {
            var p = Catalog.Get(partId);
            if (p == null) { Core.Log.Warning("[Tuning] InstallPart: no part '" + partId + "' is registered."); return false; }
            return State.Request(vehicle, "part." + p.Slot, p.Id, "InstallPart");
        }

        /// <summary>Takes the part out of a slot; the car shows the stock part again.</summary>
        public static bool RemovePart(LandVehicle vehicle, string slot) => State.Request(vehicle, "part." + slot, "", "RemovePart");

        /// <summary>The part id in a slot, or null for stock.</summary>
        public static string InstalledPart(LandVehicle vehicle, string slot) => State.Get(vehicle, "part." + slot);

        /// <summary>Paints the body (or the second paint of parts) in any colour with a finish from <see cref="TuningFinish"/>.</summary>
        public static bool SetPaint(LandVehicle vehicle, Color colour, string finish = TuningFinish.Classic, bool secondary = false)
            => State.Request(vehicle, secondary ? "paint2" : "paint", Values.Hex(colour) + "," + (finish ?? TuningFinish.Classic), "SetPaint");

        /// <summary>Back to the vanilla paint.</summary>
        public static bool ClearPaint(LandVehicle vehicle, bool secondary = false) => State.Request(vehicle, secondary ? "paint2" : "paint", "", "ClearPaint");

        /// <summary>Rim colour; null for the stock rim colour.</summary>
        public static bool SetRimColor(LandVehicle vehicle, Color? colour, string finish = TuningFinish.Metal)
            => State.Request(vehicle, "rims", colour.HasValue ? Values.Hex(colour.Value) + "," + (finish ?? TuningFinish.Metal) : "", "SetRimColor");

        /// <summary>Headlight colour; null for the stock headlights.</summary>
        public static bool SetXenon(LandVehicle vehicle, Color? colour) => State.Request(vehicle, "xenon", colour.HasValue ? Values.Hex(colour.Value) : "", "SetXenon");

        /// <summary>Underglow in a colour on the given sides ("fblr": front, back, left, right); null switches it off.</summary>
        public static bool SetNeon(LandVehicle vehicle, Color? colour, string sides = "fblr")
            => State.Request(vehicle, "neon", colour.HasValue ? Values.Hex(colour.Value) + "," + (string.IsNullOrEmpty(sides) ? "fblr" : sides) : "", "SetNeon");

        /// <summary>Window glass colour with its alpha as the darkness; null for the profile's tint. Needs the base interior.</summary>
        public static bool SetWindowTint(LandVehicle vehicle, Color? tint) => State.Request(vehicle, "tint", tint.HasValue ? Values.HexA(tint.Value) : "", "SetWindowTint");

        /// <summary>Drift smoke colour; null for the stock white. The smoke shows on the driver's screen, as in vanilla.</summary>
        public static bool SetTyreSmoke(LandVehicle vehicle, Color? colour) => State.Request(vehicle, "smoke", colour.HasValue ? Values.Hex(colour.Value) : "", "SetTyreSmoke");

        /// <summary>Horn sound from <see cref="RegisterSound"/>; null or empty for the profile's horn.</summary>
        public static bool SetHorn(LandVehicle vehicle, string sound) => State.Request(vehicle, "horn", sound ?? "", "SetHorn");

        /// <summary>Sets any tuning key; an empty value removes it.</summary>
        public static bool SetTuning(LandVehicle vehicle, string key, string value) => State.Request(vehicle, key, value, "SetTuning");

        /// <summary>The value of one tuning key, or null.</summary>
        public static string GetTuning(LandVehicle vehicle, string key) => State.Get(vehicle, key);

        /// <summary>A copy of every tuning key of a car.</summary>
        public static Dictionary<string, string> Tuning(LandVehicle vehicle) => new Dictionary<string, string>(State.Of(vehicle), StringComparer.OrdinalIgnoreCase);

        /// <summary>The driving factors of all parts on the car together.</summary>
        public static TuningStats TuningStatsOf(LandVehicle vehicle)
        {
            var c = Tune.Apply.Find(vehicle);
            return c?.Stats ?? new TuningStats();
        }

        /// <summary>Puts the whole car back to stock.</summary>
        public static bool ClearTuning(LandVehicle vehicle) => State.Request(vehicle, State.ClearAll, "", "ClearTuning");

        internal static void RaiseTuning(LandVehicle vehicle, string key)
        {
            if (TuningChanged == null || vehicle == null) return;
            try { TuningChanged(vehicle, key); }
            catch (Exception e) { Core.Log.Error("[Tuning] a TuningChanged handler failed: " + e); }
        }
    }
}
