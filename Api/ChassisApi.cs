using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Chassis.Api
{
    /// <summary>
    /// Public entry point for other mods: read and change the seat profiles Chassis uses per vehicle code.
    ///
    /// Precedence, last one wins: built-in defaults, a profile a mod registers with <see cref="Register"/>, the JSON
    /// file in UserData/Chassis/&lt;code&gt;.json. The file wins so a player or the debug console can always correct a mod's
    /// values. Changes take effect on the next seat change; <see cref="Changed"/> tells seated rigs to re-read now.
    /// </summary>
    public static class ChassisApi
    {
        private static readonly Dictionary<string, ChassisProfile> _profiles = new Dictionary<string, ChassisProfile>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> _fromFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static bool _loaded;

        /// <summary>Raised with the vehicle code after a profile was registered, loaded or edited.</summary>
        public static event Action<string> Changed;

        /// <summary>Folder of the JSON profiles.</summary>
        public static string Folder => Path.Combine(MelonLoader.Utils.MelonEnvironment.UserDataDirectory, "Chassis");

        /// <summary>The profile for a vehicle code, or null when Chassis leaves that vehicle to vanilla.</summary>
        public static ChassisProfile Get(string vehicleCode)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(vehicleCode)) return null;
            return _profiles.TryGetValue(vehicleCode, out var p) ? p : null;
        }

        /// <summary>Every vehicle code that has a profile.</summary>
        public static IEnumerable<string> Codes
        {
            get { EnsureLoaded(); return new List<string>(_profiles.Keys); }
        }

        /// <summary>
        /// Adds or replaces a profile from code. A JSON file for the same code still wins; returns false when one does.
        /// </summary>
        public static bool Register(ChassisProfile profile)
        {
            if (profile == null || string.IsNullOrEmpty(profile.Code)) throw new ArgumentException("a profile needs a vehicle code");
            EnsureLoaded();
            if (_fromFile.Contains(profile.Code))
            {
                Core.Log.Msg("[Profiles] '" + profile.Code + "' registered by a mod, but UserData/Chassis/" + profile.Code + ".json overrides it.");
                return false;
            }
            _profiles[profile.Code] = profile;
            Core.Log.Msg("[Profiles] '" + profile.Code + "' registered from code, " + profile.Seats.Count + " seat(s).");
            Raise(profile.Code);
            return true;
        }

        /// <summary>Writes the current profile of a vehicle code to its JSON file; from then on the file wins.</summary>
        public static string Save(string vehicleCode)
        {
            var p = Get(vehicleCode);
            if (p == null) throw new ArgumentException("no profile for '" + vehicleCode + "'");
            Directory.CreateDirectory(Folder);
            string path = Path.Combine(Folder, p.Code + ".json");
            File.WriteAllText(path, JsonConvert.SerializeObject(p, ProfileJson.Settings));
            _fromFile.Add(p.Code);
            Core.Log.Msg("[Profiles] wrote " + path + ".");
            return path;
        }

        /// <summary>Tells seated rigs that a profile changed in place, so they re-read it this frame.</summary>
        public static void NotifyChanged(string vehicleCode) => Raise(vehicleCode);

        /// <summary>Drops every loaded profile and reads defaults and files again.</summary>
        public static int Reload()
        {
            _loaded = false;
            _profiles.Clear();
            _fromFile.Clear();
            EnsureLoaded();
            foreach (var code in _profiles.Keys) Raise(code);
            return _profiles.Count;
        }

        private static void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            foreach (var p in BuiltInProfiles.All()) _profiles[p.Code] = p;

            if (!Directory.Exists(Folder)) return;
            foreach (string file in Directory.GetFiles(Folder, "*.json"))
            {
                try
                {
                    var p = JsonConvert.DeserializeObject<ChassisProfile>(File.ReadAllText(file), ProfileJson.Settings);
                    if (p == null || string.IsNullOrEmpty(p.Code))
                    {
                        Core.Log.Warning("[Profiles] " + Path.GetFileName(file) + " has no vehicle code, skipped.");
                        continue;
                    }
                    _profiles[p.Code] = p;
                    _fromFile.Add(p.Code);
                    Core.Log.Msg("[Profiles] loaded '" + p.Code + "' from " + Path.GetFileName(file) + ", " + p.Seats.Count + " seat(s).");
                }
                catch (Exception e) { Core.Log.Error("[Profiles] " + Path.GetFileName(file) + " could not be read, the built-in values stay: " + e.Message); }
            }
        }

        private static void Raise(string code)
        {
            if (Changed == null) return;
            try { Changed(code); }
            catch (Exception e) { Core.Log.Error("[Profiles] a Changed handler failed for '" + code + "': " + e); }
        }
    }
}
