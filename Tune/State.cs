using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chassis.Api;
using Chassis.Net;
using Chassis.Vehicles;

namespace Chassis.Tune
{
    /// <summary>
    /// The tuning of every car: a list of key = value per car, the only thing that is synced and saved. Every peer keeps
    /// it by network id; the host is the authority and writes the player-owned cars next to the save
    /// (<c>Chassis.tuning</c>, one line per car keyed by its save GUID) whenever the game saves.
    ///   req.tune vehicle player key value -> tune vehicle key value   (host: the requester owns the car; key "*" clears all)
    ///                                        tunes vehicle k=v;k=v    (host: a car's whole state, on load and for late joiners)
    /// </summary>
    internal static class State
    {
        internal const string FileName = "Chassis.tuning";
        internal const string ClearAll = "*";

        private static readonly Dictionary<int, Dictionary<string, string>> _byId = new Dictionary<int, Dictionary<string, string>>();
        private static readonly Dictionary<int, int> _versions = new Dictionary<int, int>();
        // Host: what the save file holds for cars not seen yet in this session.
        private static readonly Dictionary<string, Dictionary<string, string>> _fromFile = new Dictionary<string, Dictionary<string, string>>();
        private static readonly HashSet<int> _seeded = new HashSet<int>();
        private static readonly HashSet<int> _seenAlive = new HashSet<int>();
        private static readonly HashSet<int> _alive = new HashSet<int>();
        private static readonly List<int> _gone = new List<int>();
        private static string _loadedFrom;
        private static float _nextTick;

        private static readonly Dictionary<string, string> Empty = new Dictionary<string, string>();

        // Values shown on this machine only, over the real state: a shop shows a part or a colour before it is bought.
        // Never sent, never saved. An empty value shows the key as removed.
        private static readonly Dictionary<int, Dictionary<string, string>> _preview = new Dictionary<int, Dictionary<string, string>>();

        internal static Dictionary<string, string> Of(int id) => id >= 0 && _byId.TryGetValue(id, out var s) ? s : Empty;

        internal static Dictionary<string, string> Of(LandVehicle v) => Of(Ids.Of(v));

        internal static string Get(LandVehicle v, string key) => Of(v).TryGetValue(key, out var s) ? s : null;

        /// <summary>
        /// What the car shows on this machine: the real state with the local preview on top. The apply path reads this;
        /// saving, the network and the API getters read <see cref="Of(int)"/>.
        /// </summary>
        internal static Dictionary<string, string> Shown(int id)
        {
            var real = Of(id);
            if (id < 0 || !_preview.TryGetValue(id, out var over) || over.Count == 0) return real;
            var merged = over.ContainsKey(ClearAll)
                ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(real, StringComparer.OrdinalIgnoreCase);
            foreach (var kv in over)
            {
                if (kv.Key == ClearAll) continue;
                if (kv.Value.Length == 0) merged.Remove(kv.Key);
                else merged[kv.Key] = kv.Value;
            }
            return merged;
        }

        internal static string GetShown(LandVehicle v, string key) => Shown(Ids.Of(v)).TryGetValue(key, out var s) && s.Length > 0 ? s : null;

        internal static bool HasPreview(LandVehicle v) => _preview.TryGetValue(Ids.Of(v), out var over) && over.Count > 0;

        /// <summary>Shows a value on this machine only. "*" with no value shows the whole car stock.</summary>
        internal static bool Preview(LandVehicle v, string key, string value, string why)
        {
            if (v == null) { Core.Log.Warning("[Tuning] " + why + ": no vehicle given - ignored."); return false; }
            value = value ?? string.Empty;
            string bad = Check(v, key, value);
            if (bad != null) { Core.Log.Warning("[Tuning] " + why + " on " + Registry.Name(v) + ": " + key + " = '" + value + "' refused, " + bad + "."); return false; }
            int id = Ids.Of(v);
            if (id < 0) { Core.Log.Warning("[Tuning] " + why + ": " + v.vehicleCode + " has no network id yet - ignored."); return false; }
            if (!_preview.TryGetValue(id, out var over)) _preview[id] = over = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (key == ClearAll) over.Clear();
            over[key] = value;
            Bump(id);
            Core.Log.Msg("[Tuning] " + Registry.Name(v) + ": preview " + (key == ClearAll ? "of the stock car" : key + " = '" + value + "'") + " (" + why + ").");
            return true;
        }

        /// <summary>Drops the local preview of one key, or of every key when <paramref name="key"/> is null.</summary>
        internal static bool EndPreview(LandVehicle v, string key, string why)
        {
            if (v == null) { Core.Log.Warning("[Tuning] " + why + ": no vehicle given - ignored."); return false; }
            int id = Ids.Of(v);
            if (!_preview.TryGetValue(id, out var over) || over.Count == 0) return false;
            bool changed = key == null ? over.Count > 0 : over.Remove(key);
            if (key == null) _preview.Remove(id);
            if (!changed) return false;
            Bump(id);
            Core.Log.Msg("[Tuning] " + Registry.Name(v) + ": preview " + (key == null ? "ended" : "of " + key + " ended") + " (" + why + ").");
            return true;
        }

        /// <summary>Goes up with every change of the car's state; the apply path compares it.</summary>
        internal static int Version(int id) => _versions.TryGetValue(id, out int n) ? n : 0;

        private static Dictionary<string, string> Writable(int id)
        {
            if (!_byId.TryGetValue(id, out var s)) _byId[id] = s = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            return s;
        }

        private static void Bump(int id) => _versions[id] = Version(id) + 1;

        // ---- requests ----------------------------------------------------------------------------------------------

        /// <summary>
        /// A change from the API or the console. On the host it is applied at once; a client asks the host, which checks
        /// that the local player owns the car. Returns false with the reason logged when it cannot even be sent.
        /// </summary>
        internal static bool Request(LandVehicle v, string key, string value, string why)
        {
            if (v == null) { Core.Log.Warning("[Tuning] " + why + ": no vehicle given - ignored."); return false; }
            value = value ?? string.Empty;
            string bad = Check(v, key, value);
            if (bad != null) { Core.Log.Warning("[Tuning] " + why + " on " + Registry.Name(v) + ": " + key + " = '" + value + "' refused, " + bad + "."); return false; }
            int id = Ids.Of(v);
            if (id < 0) { Core.Log.Warning("[Tuning] " + why + ": " + v.vehicleCode + " has no network id yet - ignored."); return false; }
            if (ChassisNet.IsHost) return HostSet(v, key, value, why);
            string me = Fleet.MyCode();
            if (me.Length == 0) { Core.Log.Warning("[Tuning] " + why + ": the local player does not exist yet - not sent."); return false; }
            ChassisNet.ToHost("req.tune", id.ToString(CultureInfo.InvariantCulture), me, key, value);
            Core.Log.Msg("[Tuning] asked the host to set " + key + " = '" + value + "' on " + Registry.Name(v) + " (" + why + ").");
            return true;
        }

        /// <summary>Host: a client's request. Only the owner may tune a car.</summary>
        internal static void HostRequest(int vehicle, string player, string key, string value)
        {
            var v = Registry.ById(vehicle);
            if (v == null) { Core.Log.Warning("[Tuning] " + player + " asked to tune net " + vehicle + ", which is not here - dropped."); return; }
            if (!Fleet.IsOwner(v, player)) { Core.Log.Msg("[Tuning] " + player + " does not own " + Registry.Name(v) + " - " + key + " refused."); return; }
            string bad = Check(v, key, value);
            if (bad != null) { Core.Log.Warning("[Tuning] " + player + ": " + key + " = '" + value + "' on " + Registry.Name(v) + " refused, " + bad + "."); return; }
            HostSet(v, key, value, "asked by " + player);
        }

        /// <summary>Host: sets one key (or clears all with "*") and tells every peer.</summary>
        internal static bool HostSet(LandVehicle v, string key, string value, string why)
        {
            int id = Ids.Of(v);
            if (id < 0) { Core.Log.Warning("[Tuning] " + Registry.Name(v) + " has no network id - " + key + " not set."); return false; }
            if (key != ClearAll && Get(v, key) == (value.Length == 0 ? null : value))
            {
                Core.Log.Msg("[Tuning] " + Registry.Name(v) + ": " + key + " is already '" + value + "'.");
                return true;
            }
            Core.Log.Msg("[Tuning] " + Registry.Name(v) + ": " + (key == ClearAll ? "everything back to stock" : key + " = '" + value + "'") + " (" + why + ").");
            ChassisNet.ToAll("tune", id.ToString(CultureInfo.InvariantCulture), key, value);
            return true;
        }

        /// <summary>Everyone: one key changed.</summary>
        internal static void OnTune(int id, string key, string value)
        {
            var s = Writable(id);
            if (key == ClearAll) s.Clear();
            else if (string.IsNullOrEmpty(value)) s.Remove(key);
            else s[key] = value;
            Bump(id);
            if (!ChassisNet.IsHost) Core.Log.Msg("[Tuning] net " + id + ": " + (key == ClearAll ? "back to stock" : key + " = '" + value + "'") + ".");
            ChassisApi.RaiseTuning(Registry.ById(id), key);
        }

        /// <summary>Everyone: a car's whole state from the host.</summary>
        internal static void OnTunes(int id, string payload)
        {
            if (ChassisNet.IsHost) return;
            var s = Writable(id);
            s.Clear();
            Parse(payload, s);
            Bump(id);
            Core.Log.Msg("[Tuning] net " + id + ": " + s.Count + " tuning key(s) from the host.");
            ChassisApi.RaiseTuning(Registry.ById(id), ClearAll);
        }

        /// <summary>Host: every tuned car's state to a player that joined late.</summary>
        internal static void HostReplay()
        {
            int n = 0;
            foreach (var kv in _byId)
            {
                if (kv.Value.Count == 0) continue;
                ChassisNet.ToAll("tunes", kv.Key.ToString(CultureInfo.InvariantCulture), Encode(kv.Value));
                n++;
            }
            Core.Log.Msg("[Tuning] replayed the tuning of " + n + " car(s).");
        }

        // ---- checks ------------------------------------------------------------------------------------------------

        /// <summary>Why a key or value cannot be set on this car, or null.</summary>
        internal static string Check(LandVehicle v, string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return "no key";
            if (key == ClearAll) return value.Length == 0 ? null : "the clear key takes no value";
            foreach (char ch in key)
                if (!(char.IsLetterOrDigit(ch) || ch == '_' || ch == '.' || ch == '-')) return "keys are letters, digits, '_', '.' and '-'";
            if (value.IndexOfAny(new[] { '|', ';', '\n', '\r' }) >= 0) return "a value may not contain '|', ';' or a line break";
            if (value.Length == 0) return null;
            if (key.StartsWith("part.", StringComparison.OrdinalIgnoreCase))
            {
                string slot = key.Substring(5);
                var part = Catalog.Get(value);
                if (part == null) return "no part '" + value + "' is registered";
                if (!string.Equals(part.Slot, slot, StringComparison.OrdinalIgnoreCase)) return "'" + value + "' goes into slot " + part.Slot + ", not " + slot;
                if (!part.FitsCode(ChassisApi.CodeOf(v))) return "'" + value + "' does not fit a " + ChassisApi.CodeOf(v);
                return null;
            }
            switch (key.ToLowerInvariant())
            {
                case "paint": case "paint2": case "rims": return Values.Paint(value, out _, out _) ? null : "expected RRGGBB,finish (finish: " + string.Join(", ", TuningFinish.All) + ")";
                case "xenon": case "smoke": return Values.Rgb(value, out _) ? null : "expected RRGGBB";
                case "tint": return Values.Rgba(value, out _) ? null : "expected RRGGBBAA";
                case "neon": return Values.Neon(value, out _, out _) ? null : "expected RRGGBB,sides with sides from f, b, l, r";
                case "horn": return Sounds.Has(value) ? null : "no sound '" + value + "' is registered";
                default: return null;
            }
        }

        // ---- per second --------------------------------------------------------------------------------------------

        internal static void Tick()
        {
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + 1f;
            var all = Registry.All;
            if (all.Count == 0) return;
            _alive.Clear();
            foreach (var v in all)
            {
                if (v == null) continue;
                int id = Ids.Of(v);
                if (id < 0) continue;
                _alive.Add(id);
                _seenAlive.Add(id);
                if (ChassisNet.IsHost) Seed(v, id);
            }
            // A car that was here and is gone (sold, destroyed) takes its state with it; its id may come back for another.
            _gone.Clear();
            foreach (int id in _byId.Keys) if (_seenAlive.Contains(id) && !_alive.Contains(id)) _gone.Add(id);
            foreach (int id in _gone)
            {
                _byId.Remove(id);
                _preview.Remove(id);
                _seenAlive.Remove(id);
                _seeded.Remove(id);
                Bump(id);
                Core.Log.Msg("[Tuning] net " + id + " is gone - its tuning is dropped.");
            }
        }

        /// <summary>Host: the first time a car is seen, it takes over what the save file holds for it.</summary>
        private static void Seed(LandVehicle v, int id)
        {
            if (_seeded.Contains(id) || SavePath() == null) return;
            _seeded.Add(id);
            EnsureLoaded();
            if (!v.IsPlayerOwned) return;
            string guid = v.GUID.ToString();
            if (!_fromFile.TryGetValue(guid, out var saved)) return;
            _fromFile.Remove(guid);
            var s = Writable(id);
            s.Clear();
            foreach (var kv in saved) s[kv.Key] = kv.Value;
            Bump(id);
            Core.Log.Msg("[Tuning] " + Registry.Name(v) + ": " + s.Count + " tuning key(s) from the save.");
            ChassisNet.ToAll("tunes", id.ToString(CultureInfo.InvariantCulture), Encode(s));
            ChassisApi.RaiseTuning(v, ClearAll);
        }

        // ---- save file ---------------------------------------------------------------------------------------------

        private static string SavePath()
        {
            if (!Singleton<GameLoadManager>.InstanceExists) return null;
            string folder = Singleton<GameLoadManager>.Instance.LoadedGameFolderPath;
            return string.IsNullOrEmpty(folder) || !Directory.Exists(folder) ? null : Path.Combine(folder, FileName);
        }

        internal static void EnsureLoaded()
        {
            string path = SavePath();
            if (path == null || path == _loadedFrom) return;
            _loadedFrom = path;
            _fromFile.Clear();
            if (!File.Exists(path)) { Core.Log.Msg("[Tuning] no " + FileName + " in this save yet."); return; }
            try
            {
                int n = 0;
                foreach (string line in File.ReadAllLines(path))
                {
                    int bar = line.IndexOf('|');
                    if (bar <= 0) continue;
                    var s = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    Parse(line.Substring(bar + 1), s);
                    _fromFile[line.Substring(0, bar)] = s;
                    n++;
                }
                Core.Log.Msg("[Tuning] loaded the tuning of " + n + " car(s) from " + path + ".");
            }
            catch (Exception e) { Core.Log.Error("[Tuning] could not read " + path + ", the cars stay stock: " + e.Message); }
        }

        /// <summary>Host: writes every player-owned car's tuning. Runs when the game saves.</summary>
        internal static void Save()
        {
            if (!ChassisNet.IsHost) return;
            string path = SavePath();
            if (path == null) { Core.Log.Warning("[Tuning] no save folder - the tuning stays in memory."); return; }
            EnsureLoaded();
            try
            {
                var sb = new StringBuilder();
                int n = 0;
                foreach (var v in Registry.All)
                {
                    if (v == null || !v.IsPlayerOwned) continue;
                    int id = Ids.Of(v);
                    if (id < 0 || !_byId.TryGetValue(id, out var s) || s.Count == 0) continue;
                    sb.Append(v.GUID.ToString()).Append('|').Append(Encode(s)).Append('\n');
                    n++;
                }
                // Cars that did not show up this session keep their line.
                foreach (var kv in _fromFile) sb.Append(kv.Key).Append('|').Append(Encode(kv.Value)).Append('\n');
                File.WriteAllText(path, sb.ToString());
                Core.Log.Msg("[Tuning] saved the tuning of " + n + " car(s) to " + path + ".");
            }
            catch (Exception e) { Core.Log.Error("[Tuning] could not write " + path + ": " + e.Message); }
        }

        internal static string Encode(Dictionary<string, string> s)
        {
            var sb = new StringBuilder();
            foreach (var kv in s)
            {
                if (sb.Length > 0) sb.Append(';');
                sb.Append(kv.Key).Append('=').Append(kv.Value);
            }
            return sb.ToString();
        }

        private static void Parse(string payload, Dictionary<string, string> into)
        {
            foreach (string pair in (payload ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = pair.IndexOf('=');
                if (eq <= 0) { Core.Log.Warning("[Tuning] unreadable entry '" + pair + "' skipped."); continue; }
                into[pair.Substring(0, eq)] = pair.Substring(eq + 1);
            }
        }

        internal static void Reset()
        {
            _byId.Clear();
            _preview.Clear();
            _versions.Clear();
            _fromFile.Clear();
            _seeded.Clear();
            _seenAlive.Clear();
            _loadedFrom = null;
            _nextTick = 0f;
        }
    }

    /// <summary>The value formats of the tuning keys.</summary>
    internal static class Values
    {
        internal static bool Rgb(string s, out Color c) => Hex(s, 6, out c);

        internal static bool Rgba(string s, out Color c) => Hex(s, 8, out c);

        private static bool Hex(string s, int digits, out Color c)
        {
            c = Color.white;
            if (s == null) return false;
            s = s.Trim().TrimStart('#');
            if (s.Length != digits || !uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint n)) return false;
            if (digits == 6) n = (n << 8) | 0xFF;
            c = new Color(((n >> 24) & 0xFF) / 255f, ((n >> 16) & 0xFF) / 255f, ((n >> 8) & 0xFF) / 255f, (n & 0xFF) / 255f);
            return true;
        }

        internal static bool Paint(string s, out Color c, out string finish)
        {
            finish = TuningFinish.Classic;
            c = Color.white;
            if (string.IsNullOrEmpty(s)) return false;
            string[] f = s.Split(',');
            if (!Rgb(f[0], out c)) return false;
            if (f.Length > 1)
            {
                finish = f[1].Trim().ToLowerInvariant();
                if (Array.IndexOf(TuningFinish.All, finish) < 0) return false;
            }
            return f.Length <= 2;
        }

        internal static bool Neon(string s, out Color c, out string sides)
        {
            sides = "fblr";
            c = Color.white;
            if (string.IsNullOrEmpty(s)) return false;
            string[] f = s.Split(',');
            if (!Rgb(f[0], out c)) return false;
            if (f.Length > 1)
            {
                sides = f[1].Trim().ToLowerInvariant();
                if (sides.Length == 0) return false;
                foreach (char ch in sides) if ("fblr".IndexOf(ch) < 0) return false;
            }
            return f.Length <= 2;
        }

        internal static string Hex(Color c) => B(c.r) + B(c.g) + B(c.b);

        internal static string HexA(Color c) => Hex(c) + B(c.a);

        private static string B(float v) => Mathf.RoundToInt(Mathf.Clamp01(v) * 255f).ToString("X2", CultureInfo.InvariantCulture);

        /// <summary>Metallic and smoothness of a finish on the URP/Lit body material.</summary>
        internal static void Finish(string finish, out float metallic, out float smoothness)
        {
            switch (finish)
            {
                case TuningFinish.Metallic: metallic = 0.6f; smoothness = 0.75f; break;
                case TuningFinish.Matte: metallic = 0f; smoothness = 0.15f; break;
                case TuningFinish.Metal: metallic = 0.9f; smoothness = 0.45f; break;
                case TuningFinish.Chrome: metallic = 1f; smoothness = 0.95f; break;
                default: metallic = 0.1f; smoothness = 0.6f; break;
            }
        }
    }
}
