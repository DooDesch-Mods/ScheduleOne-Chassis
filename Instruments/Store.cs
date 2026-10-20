using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chassis.Api;
using Chassis.Net;
using Chassis.Vehicles;

namespace Chassis.Instruments
{
    /// <summary>
    /// What a vehicle's cockpit remembers: odometer, trip and the state of its toggles and knobs. Every peer keeps it
    /// by network id; the host is the authority, counts the distance and writes the player-owned vehicles next to the
    /// save (<c>Chassis.instruments</c>, keyed by the vehicle's save GUID) whenever the game saves.
    /// </summary>
    internal static class Store
    {
        internal sealed class Cab
        {
            internal double Odo, Trip;
            internal bool Known;
            internal readonly Dictionary<string, float> Controls = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            internal double SentOdo = -1, SentTrip = -1;
        }

        internal const string FileName = "Chassis.instruments";

        private static readonly Dictionary<int, Cab> _byId = new Dictionary<int, Cab>();
        // Host: what the save file holds for vehicles not seen yet in this session.
        private static readonly Dictionary<string, Cab> _fromFile = new Dictionary<string, Cab>();
        private static readonly HashSet<int> _seeded = new HashSet<int>();
        private static string _loadedFrom;

        internal static Cab Of(LandVehicle v)
        {
            int id = Ids.Of(v);
            if (id < 0) return null;
            return Of(id);
        }

        internal static Cab Of(int id)
        {
            if (!_byId.TryGetValue(id, out var c)) _byId[id] = c = new Cab { Known = ChassisNet.IsHost };
            return c;
        }

        /// <summary>Host: the first time a vehicle is seen, it takes over what the save file holds for it.</summary>
        internal static void Seed(LandVehicle v, int id)
        {
            if (_seeded.Contains(id) || SavePath() == null) return;
            _seeded.Add(id);
            EnsureLoaded();
            if (!v.IsPlayerOwned) return;
            string guid = v.GUID.ToString();
            if (!_fromFile.TryGetValue(guid, out var saved)) return;
            var c = Of(id);
            c.Odo = saved.Odo; c.Trip = saved.Trip; c.Known = true;
            foreach (var kv in saved.Controls) c.Controls[kv.Key] = kv.Value;
            _fromFile.Remove(guid);
            Core.Log.Msg("[Instruments] " + Registry.Name(v) + ": " + c.Odo.ToString("F1", CultureInfo.InvariantCulture) + " km, trip " + c.Trip.ToString("F1", CultureInfo.InvariantCulture) + " km, " + c.Controls.Count + " switch state(s) from the save.");
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
            if (!File.Exists(path)) { Core.Log.Msg("[Instruments] no " + FileName + " in this save yet."); return; }
            try
            {
                int n = 0;
                foreach (string line in File.ReadAllLines(path))
                {
                    string[] f = line.Split('|');
                    if (f.Length < 3 || f[0].Length == 0) continue;
                    var c = new Cab { Known = true };
                    double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out c.Odo);
                    double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out c.Trip);
                    if (f.Length > 3)
                        foreach (string pair in f[3].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            int eq = pair.IndexOf('=');
                            if (eq <= 0 || !float.TryParse(pair.Substring(eq + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float val)) continue;
                            c.Controls[pair.Substring(0, eq)] = val;
                        }
                    _fromFile[f[0]] = c;
                    n++;
                }
                Core.Log.Msg("[Instruments] loaded " + n + " cockpit record(s) from " + path + ".");
            }
            catch (Exception e) { Core.Log.Error("[Instruments] could not read " + path + ", odometers start at 0: " + e.Message); }
        }

        /// <summary>Host: writes every player-owned vehicle's cockpit. Runs when the game saves.</summary>
        internal static void Save()
        {
            if (!ChassisNet.IsHost) return;
            string path = SavePath();
            if (path == null) { Core.Log.Warning("[Instruments] no save folder - odometers and switches stay in memory."); return; }
            EnsureLoaded();
            try
            {
                var sb = new StringBuilder();
                int n = 0;
                foreach (var v in Registry.All)
                {
                    if (v == null || !v.IsPlayerOwned) continue;
                    int id = Ids.Of(v);
                    if (id < 0 || !_byId.TryGetValue(id, out var c)) continue;
                    Line(sb, v.GUID.ToString(), c);
                    n++;
                }
                // Records of vehicles that did not show up this session stay in the file.
                foreach (var kv in _fromFile) Line(sb, kv.Key, kv.Value);
                File.WriteAllText(path, sb.ToString());
                Core.Log.Msg("[Instruments] saved " + n + " cockpit(s) to " + path + ".");
            }
            catch (Exception e) { Core.Log.Error("[Instruments] could not write " + path + ": " + e.Message); }
        }

        private static void Line(StringBuilder sb, string guid, Cab c)
        {
            sb.Append(guid).Append('|').Append(c.Odo.ToString("R", CultureInfo.InvariantCulture)).Append('|').Append(c.Trip.ToString("R", CultureInfo.InvariantCulture)).Append('|');
            bool first = true;
            foreach (var kv in c.Controls)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(kv.Key).Append('=').Append(kv.Value.ToString("R", CultureInfo.InvariantCulture));
            }
            sb.Append('\n');
        }

        internal static IEnumerable<KeyValuePair<int, Cab>> All => _byId;

        internal static void Reset()
        {
            _byId.Clear();
            _fromFile.Clear();
            _seeded.Clear();
            _loadedFrom = null;
        }
    }

    /// <summary>
    /// Distance per vehicle. The host adds up how far each vehicle moved, times the display factor the speedometer
    /// uses, and sends the changed values to everyone once a second:
    ///   odo id,odo,trip;id,odo,trip...   (km, invariant culture)
    /// A jump of more than 30 m in one frame is a teleport and does not count.
    /// </summary>
    internal static class Odometer
    {
        private const float SendEvery = 1f;
        private const float TeleportMetres = 30f;

        private static readonly Dictionary<int, Vector3> _last = new Dictionary<int, Vector3>();
        private static float _nextSend;

        internal static GaugeReading Read(LandVehicle v, bool trip, bool miles)
        {
            var c = Store.Of(v);
            if (c == null || !c.Known) return GaugeReading.Invalid;
            double km = trip ? c.Trip : c.Odo;
            return new GaugeReading((float)(miles ? km / Sources.KmPerMile : km), true);
        }

        internal static void Tick()
        {
            if (!ChassisNet.IsHost) return;
            foreach (var v in Registry.All)
            {
                if (v == null) continue;
                int id = Ids.Of(v);
                if (id < 0) continue;
                Store.Seed(v, id);
                Vector3 p = v.transform.position;
                if (_last.TryGetValue(id, out Vector3 was))
                {
                    float m = Vector3.Distance(was, p);
                    if (m > 0.0005f && m < TeleportMetres)
                    {
                        var c = Store.Of(id);
                        double km = m * Sources.DisplayFactor / 1000.0;
                        c.Odo += km;
                        c.Trip += km;
                    }
                }
                _last[id] = p;
            }
            if (Time.unscaledTime < _nextSend) return;
            _nextSend = Time.unscaledTime + SendEvery;
            Broadcast(false);
        }

        /// <summary>Host: the odometers that changed by 1 m or more (or all of them) to every peer.</summary>
        internal static void Broadcast(bool all)
        {
            var sb = new StringBuilder();
            foreach (var kv in Store.All)
            {
                var c = kv.Value;
                if (!all && Math.Abs(c.Odo - c.SentOdo) < 0.001 && Math.Abs(c.Trip - c.SentTrip) < 0.001) continue;
                c.SentOdo = c.Odo; c.SentTrip = c.Trip;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(kv.Key.ToString(CultureInfo.InvariantCulture)).Append(',')
                  .Append(c.Odo.ToString("F3", CultureInfo.InvariantCulture)).Append(',')
                  .Append(c.Trip.ToString("F3", CultureInfo.InvariantCulture));
            }
            if (sb.Length == 0) return;
            ChassisNet.ToAll("odo", sb.ToString());
        }

        /// <summary>Everyone: odometers from the host. The host's own values are the source and stay as they are.</summary>
        internal static void OnOdo(string payload)
        {
            if (ChassisNet.IsHost) return;
            foreach (string entry in (payload ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = entry.Split(',');
                if (f.Length < 3 || !int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)
                    || !double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double odo)
                    || !double.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double trip))
                {
                    Core.Log.Warning("[Instruments] unreadable odometer '" + entry + "' skipped.");
                    continue;
                }
                var c = Store.Of(id);
                c.Odo = odo; c.Trip = trip; c.Known = true;
            }
        }

        /// <summary>Host: sets the trip of a vehicle back to 0 for everyone.</summary>
        internal static void HostResetTrip(LandVehicle v, string why)
        {
            var c = Store.Of(v);
            if (c == null) { Core.Log.Warning("[Instruments] trip reset for a vehicle without a network id - ignored."); return; }
            c.Trip = 0;
            c.SentTrip = -1;
            Core.Log.Msg("[Instruments] trip of " + Registry.Name(v) + " reset (" + why + ").");
            Broadcast(false);
        }

        internal static void Reset()
        {
            _last.Clear();
            _nextSend = 0f;
        }
    }
}
