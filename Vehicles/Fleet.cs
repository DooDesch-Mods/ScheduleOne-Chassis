using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chassis.Api;
using Chassis.Net;
using HarmonyLib;

namespace Chassis.Vehicles
{
    /// <summary>
    /// Who owns which player vehicle, and whether it is locked.
    ///
    /// Vanilla only knows "player owned" for the whole save, not which player bought a vehicle. The host is the
    /// authority: it keys the records by the vehicle's save GUID, writes them next to the save (<c>Chassis.vehicles</c>)
    /// and broadcasts them by network id, because a client's GUID for the same vehicle is made locally and never
    /// matches. A vehicle with no record (bought before Chassis kept owners) belongs to the host, unless a mod imports
    /// an older record through <see cref="ChassisApi.ResolveUnknownOwner"/>.
    ///
    /// Unlocked, everyone may drive. Locked, only the owner gets in, and others only while the owner sits inside.
    ///
    /// Vanilla gives the local player the code <see cref="NoSteamCode"/> when Steam was not ready at start. Such a
    /// game has no other players, so that player owns every vehicle; and a host with a Steam id takes over the records
    /// written under that code, because a save played without Steam is the host's own save.
    /// </summary>
    internal static class Fleet
    {
        /// <summary>The player code vanilla uses when Steam was not initialised (<c>Player.OnStartClient</c>).</summary>
        internal const string NoSteamCode = "0";

        internal sealed class Rec
        {
            internal string Owner = string.Empty;
            internal bool Locked;
            internal bool Same(Rec o) => o != null && o.Owner == Owner && o.Locked == Locked;
        }

        internal const string FileName = "Chassis.vehicles";
        private const float ScanSeconds = 1f;
        /// <summary>How long a purchase announcement and a new, unclaimed vehicle wait for each other.</summary>
        private const float ClaimSeconds = 10f;

        // Host: by save GUID. Everyone: by network object id, as the host last broadcast it.
        private static readonly Dictionary<string, Rec> ByGuid = new Dictionary<string, Rec>();
        private static readonly Dictionary<int, Rec> ById = new Dictionary<int, Rec>();
        private static string _loadedFrom;
        private static float _nextScan;
        private static string _lastBroadcast = string.Empty;
        private static bool _askedForSync;
        private static bool _scanned;

        private struct Announcement { internal string Buyer; internal string Code; internal float At; }
        private static readonly List<Announcement> Pending = new List<Announcement>();
        private static readonly Dictionary<string, float> FirstSeen = new Dictionary<string, float>();

        internal static Rec For(LandVehicle lv)
        {
            if (lv == null) return null;
            int id = Ids.Of(lv);
            return id >= 0 && ById.TryGetValue(id, out Rec r) ? r : null;
        }

        internal static string MyCode()
        {
            var local = Player.Local;
            return local != null ? local.PlayerCode ?? string.Empty : string.Empty;
        }

        internal static bool IsOwner(LandVehicle lv, string playerCode)
        {
            var r = For(lv);
            if (r == null || string.IsNullOrEmpty(playerCode)) return false;
            return r.Owner == playerCode || playerCode == NoSteamCode;
        }

        internal static bool IsMine(LandVehicle lv) => IsOwner(lv, MyCode());

        /// <summary>Whether the local player may get in: unlocked, their own, or the owner sits inside.</summary>
        internal static bool CanEnter(LandVehicle lv, out string why)
        {
            why = null;
            var r = For(lv);
            if (r == null || !r.Locked) return true;
            if (IsOwner(lv, MyCode())) return true;
            var seats = lv.Seats;
            if (seats != null)
                foreach (var s in seats)
                    if (s != null && s.Occupant != null && s.Occupant.PlayerCode == r.Owner) return true;
            why = "locked by its owner " + r.Owner;
            return false;
        }

        // ---- requests ----------------------------------------------------------------------------------------------

        internal static void RequestLock(LandVehicle lv, bool locked)
        {
            if (lv == null) { Core.Log.Warning("[Fleet] lock asked for a vehicle that is gone."); return; }
            string me = MyCode();
            if (me.Length == 0) { Core.Log.Warning("[Fleet] lock asked before the local player exists."); return; }
            ChassisNet.ToHost("req.lock", Ids.Of(lv).ToString(CultureInfo.InvariantCulture), me, locked ? "1" : "0");
        }

        /// <summary>Host: lock or unlock for the owner. Logs why it was refused.</summary>
        internal static void HostLock(int vehicle, string requester, bool locked)
        {
            var lv = Registry.ById(vehicle);
            if (lv == null) { Core.Log.Warning("[Fleet] lock for net " + vehicle + ", which is not here - dropped."); return; }
            EnsureLoaded();
            if (!ByGuid.TryGetValue(lv.GUID.ToString(), out Rec r)) { Core.Log.Msg("[Fleet] " + Registry.Name(lv) + " has no owner record - lock refused."); return; }
            if (r.Owner != requester) { Core.Log.Msg("[Fleet] " + requester + " does not own " + Registry.Name(lv) + " - lock refused."); return; }
            if (r.Locked == locked) { Core.Log.Msg("[Fleet] " + Registry.Name(lv) + " is already " + (locked ? "locked" : "unlocked") + "."); return; }
            r.Locked = locked;
            Core.Log.Msg("[Fleet] " + Registry.Name(lv) + " " + (locked ? "locked" : "unlocked") + " by " + requester + ".");
            ChassisNet.ToAll("chirp", vehicle.ToString(CultureInfo.InvariantCulture), locked ? "1" : "0");
            Save();
            Broadcast(true);
        }

        /// <summary>Host: a new owner from a mod. False on a client or for a vehicle that is not player owned.</summary>
        internal static bool HostSetOwner(LandVehicle lv, string owner, string why)
        {
            if (!ChassisNet.IsHost) { Core.Log.Warning("[Fleet] owner change on a client ignored - the host records owners."); return false; }
            if (lv == null || !lv.IsPlayerOwned || string.IsNullOrEmpty(owner)) { Core.Log.Warning("[Fleet] owner change without a player vehicle or owner (" + why + ") ignored."); return false; }
            EnsureLoaded();
            string guid = lv.GUID.ToString();
            if (!ByGuid.TryGetValue(guid, out Rec r)) ByGuid[guid] = r = new Rec();
            r.Owner = owner;
            Core.Log.Msg("[Fleet] " + Registry.Name(lv) + " " + Short(guid) + " now belongs to " + owner + " (" + why + ").");
            Save();
            Broadcast(true);
            return true;
        }

        /// <summary>Host: a player announced a purchase; the next new vehicle of that code is theirs.</summary>
        internal static void Announce(string buyer, string code)
        {
            if (string.IsNullOrEmpty(buyer)) { Core.Log.Warning("[Fleet] purchase announcement without a buyer ignored."); return; }
            Pending.Add(new Announcement { Buyer = buyer, Code = code ?? string.Empty, At = Time.unscaledTime });
            Core.Log.Msg("[Fleet] " + buyer + " is buying a '" + code + "' - the next new one is theirs.");
            _nextScan = 0f;
        }

        // ---- per frame ---------------------------------------------------------------------------------------------

        internal static void Tick()
        {
            if (!ChassisNet.IsHost)
            {
                // A client that joined late has not seen a broadcast yet; it asks once.
                if (!_askedForSync && Player.Local != null && GameQuestManager.Instance != null)
                {
                    _askedForSync = true;
                    ChassisNet.ToHost("req.sync", MyCode());
                }
                return;
            }

            float now = Time.unscaledTime;
            if (now < _nextScan) return;
            _nextScan = now + ScanSeconds;
            EnsureLoaded();
            string host = MyCode();
            if (host.Length == 0) return;
            bool changed = AdoptNoSteamRecords(host);
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                if (now - Pending[i].At <= ClaimSeconds) continue;
                Core.Log.Warning("[Fleet] the '" + Pending[i].Code + "' purchase announced by " + Pending[i].Buyer + " never showed up as a vehicle - announcement dropped.");
                Pending.RemoveAt(i);
            }
            foreach (var v in Registry.All)
            {
                if (v == null || !v.IsPlayerOwned) continue;
                string guid = v.GUID.ToString();
                if (ByGuid.ContainsKey(guid)) continue;

                int claim = Pending.FindIndex(a => string.Equals(a.Code, v.vehicleCode, StringComparison.OrdinalIgnoreCase));
                if (claim >= 0)
                {
                    ByGuid[guid] = new Rec { Owner = Pending[claim].Buyer };
                    Core.Log.Msg("[Fleet] " + Registry.Name(v) + " " + Short(guid) + " bought at the dealership by " + Pending[claim].Buyer + ".");
                    Pending.RemoveAt(claim);
                    changed = true;
                    continue;
                }
                // A vehicle that appears after the first scan was just bought; its buyer's announcement may still be on
                // the way. Vehicles already there at the first scan came from the save.
                if (_scanned)
                {
                    if (!FirstSeen.TryGetValue(guid, out float seen)) { FirstSeen[guid] = now; continue; }
                    if (now - seen < ClaimSeconds) continue;
                }
                ByGuid[guid] = Unknown(v, host);
                changed = true;
            }
            _scanned = true;
            if (changed) Save();
            Broadcast(false);
        }

        private static Rec Unknown(LandVehicle v, string host)
        {
            var resolve = ChassisApi.ResolveUnknownOwner;
            if (resolve != null)
            {
                try
                {
                    var imported = resolve(v);
                    if (imported != null && !string.IsNullOrEmpty(imported.Owner))
                    {
                        Core.Log.Msg("[Fleet] " + Registry.Name(v) + " " + Short(v.GUID.ToString()) + " imported from a mod: owner " + imported.Owner + (imported.Locked ? ", locked" : "") + ".");
                        return new Rec { Owner = imported.Owner, Locked = imported.Locked };
                    }
                }
                catch (Exception e) { Core.Log.Error("[Fleet] a ResolveUnknownOwner handler failed for " + Registry.Name(v) + ", it belongs to the host: " + e); }
            }
            Core.Log.Msg("[Fleet] " + Registry.Name(v) + " " + Short(v.GUID.ToString()) + " had no owner record - it belongs to the host (" + host + ").");
            return new Rec { Owner = host };
        }

        /// <summary>
        /// Host with a Steam id: records written while the game ran without Steam become the host's. Returns whether
        /// any record changed.
        /// </summary>
        private static bool AdoptNoSteamRecords(string host)
        {
            if (host == NoSteamCode) return false;
            int adopted = 0;
            foreach (var r in ByGuid.Values)
            {
                if (r.Owner != NoSteamCode) continue;
                r.Owner = host;
                adopted++;
            }
            if (adopted > 0) Core.Log.Msg("[Fleet] " + adopted + " vehicle(s) saved without Steam (owner " + NoSteamCode + ") now belong to the host " + host + ".");
            return adopted > 0;
        }

        /// <summary>Host: send the current records to everyone when they changed (or when forced).</summary>
        internal static void Broadcast(bool force)
        {
            var sb = new StringBuilder();
            foreach (var v in Registry.All)
            {
                if (v == null || !v.IsPlayerOwned) continue;
                if (!ByGuid.TryGetValue(v.GUID.ToString(), out Rec r)) continue;
                if (sb.Length > 0) sb.Append(';');
                sb.Append(Ids.Of(v).ToString(CultureInfo.InvariantCulture)).Append(',').Append(r.Owner).Append(',').Append(r.Locked ? 1 : 0);
            }
            string payload = sb.ToString();
            if (!force && payload == _lastBroadcast) return;
            _lastBroadcast = payload;
            ChassisNet.ToAll("own", payload);
        }

        /// <summary>Everyone: apply the host's records.</summary>
        internal static void ApplyState(string payload)
        {
            var next = new Dictionary<int, Rec>();
            foreach (string entry in (payload ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = entry.Split(',');
                if (f.Length < 3 || !int.TryParse(f[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int id)) { Core.Log.Warning("[Fleet] unreadable record '" + entry + "' skipped."); continue; }
                next[id] = new Rec { Owner = f[1], Locked = f[2] == "1" };
            }
            bool changed = next.Count != ById.Count;
            if (!changed)
                foreach (var kv in next)
                    if (!ById.TryGetValue(kv.Key, out Rec old) || !old.Same(kv.Value)) { changed = true; break; }
            if (!changed) return;
            ById.Clear();
            foreach (var kv in next) ById[kv.Key] = kv.Value;
            Core.Log.Msg("[Fleet] owner records: " + (payload.Length > 0 ? payload : "none") + " (I am " + MyCode() + ").");
            ChassisApi.RaiseOwners();
        }

        // ---- save file ---------------------------------------------------------------------------------------------

        internal static void EnsureLoaded()
        {
            string path = SavePath();
            if (path == null || path == _loadedFrom) return;
            _loadedFrom = path;
            ByGuid.Clear();
            if (!File.Exists(path)) { Core.Log.Msg("[Fleet] no " + FileName + " in this save yet."); return; }
            try
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    string[] f = line.Split('|');
                    if (f.Length < 3 || f[0].Length == 0) continue;
                    ByGuid[f[0]] = new Rec { Owner = f[1], Locked = f[2] == "1" };
                }
                Core.Log.Msg("[Fleet] loaded " + ByGuid.Count + " owner record(s) from " + path + ".");
            }
            catch (Exception e) { Core.Log.Error("[Fleet] could not read " + path + ", owners start empty: " + e.Message); }
        }

        /// <summary>Host: writes the owner records again after the game saved.</summary>
        internal static void SaveWithGame()
        {
            if (!ChassisNet.IsHost) return;
            EnsureLoaded();
            Save();
            Core.Log.Msg("[Fleet] saved " + ByGuid.Count + " owner record(s) with the game.");
        }

        private static void Save()
        {
            string path = SavePath();
            if (path == null) { Core.Log.Warning("[Fleet] no save folder - the owner records stay in memory."); return; }
            try
            {
                var sb = new StringBuilder();
                foreach (var kv in ByGuid) sb.Append(kv.Key).Append('|').Append(kv.Value.Owner).Append('|').Append(kv.Value.Locked ? 1 : 0).Append('\n');
                File.WriteAllText(path, sb.ToString());
                _loadedFrom = path;
            }
            catch (Exception e) { Core.Log.Error("[Fleet] could not write " + path + ": " + e.Message); }
        }

        private static string SavePath()
        {
            if (!Singleton<GameLoadManager>.InstanceExists) return null;
            string folder = Singleton<GameLoadManager>.Instance.LoadedGameFolderPath;
            return string.IsNullOrEmpty(folder) || !Directory.Exists(folder) ? null : Path.Combine(folder, FileName);
        }

        private static string Short(string guid) => guid.Length > 6 ? guid.Substring(0, 6) : guid;

        internal static string Describe()
        {
            var sb = new StringBuilder("owners me=" + MyCode() + " host=" + ChassisNet.IsHost);
            foreach (var kv in ById) sb.Append(" [net ").Append(kv.Key).Append(" owner=").Append(kv.Value.Owner).Append(" locked=").Append(kv.Value.Locked).Append(']');
            return sb.ToString();
        }

        internal static void Reset()
        {
            ByGuid.Clear();
            ById.Clear();
            _loadedFrom = null;
            _lastBroadcast = string.Empty;
            _askedForSync = false;
            _scanned = false;
            Pending.Clear();
            FirstSeen.Clear();
            _nextScan = 0f;
        }

        // ---- vanilla hooks -----------------------------------------------------------------------------------------

        /// <summary>
        /// Vanilla decides entry in <c>LandVehicle.Hovered</c> (the prompt) and <c>LandVehicle.Interacted</c> (the
        /// action) with no owner check at all. Leaving a locked vehicle is always possible.
        /// </summary>
        [HarmonyPatch(typeof(LandVehicle), "Hovered")]
        private static class HoveredPatch
        {
            private static void Postfix(LandVehicle __instance)
            {
                try
                {
                    if (!__instance.IsPlayerOwned || CanEnter(__instance, out _)) return;
                    __instance.intObj.SetMessage("Locked");
                    __instance.intObj.SetInteractableState(InteractableObject.EInteractableState.Invalid);
                }
                catch (Exception e) { Core.Log.Error("[Fleet] could not show the locked prompt: " + e.Message); }
            }
        }

        private static float _lastRefusalLog = -100f;

        [HarmonyPatch(typeof(LandVehicle), "Interacted")]
        private static class InteractedPatch
        {
            private static bool Prefix(LandVehicle __instance)
            {
                try
                {
                    if (CanEnter(__instance, out string why)) return true;
                    if (Time.time - _lastRefusalLog > 2f)
                    {
                        _lastRefusalLog = Time.time;
                        Core.Log.Msg("[Fleet] " + Registry.Name(__instance) + " is " + why + " - not entering.");
                    }
                    return false;
                }
                catch (Exception e)
                {
                    Core.Log.Error("[Fleet] lock check failed, letting the player in: " + e.Message);
                    return true;
                }
            }
        }

        /// <summary>
        /// The purchase runs on the buyer's machine and ends in <c>VehicleManager.SpawnVehicle</c>, a ServerRpc that
        /// carries no connection - the host sees a new vehicle and nothing else. So the buyer announces itself right
        /// before the purchase, and the host matches the announcement to the next new vehicle of that code.
        /// </summary>
        [HarmonyPatch(typeof(VehicleSalesman), nameof(VehicleSalesman.ChoiceCallback))]
        private static class PurchasePatch
        {
            private static void Prefix(VehicleSalesman __instance, string choiceLabel)
            {
                try
                {
                    if (choiceLabel != "BUY_CASH" && choiceLabel != "BUY_ONLINE") return;
                    var listing = __instance.selectedVehicle;
                    if (listing == null) { Core.Log.Warning("[Fleet] purchase without a selected vehicle - not announced."); return; }
                    // The same balance check vanilla makes next; a purchase that will not happen is not announced.
                    var money = NetworkSingleton<GameMoneyManager>.Instance;
                    float balance = choiceLabel == "BUY_CASH" ? money.cashBalance : money.onlineBalance;
                    if (balance < listing.price)
                    {
                        Core.Log.Msg("[Fleet] '" + listing.vehicleCode + "' purchase with " + choiceLabel + " will fail (balance " + balance + " < " + listing.price + ") - not announced.");
                        return;
                    }
                    string me = MyCode();
                    if (me.Length == 0) { Core.Log.Warning("[Fleet] no local player code - the new vehicle will belong to the host."); return; }
                    ChassisNet.ToHost("req.bought", me, listing.vehicleCode);
                    Core.Log.Msg("[Fleet] announced a '" + listing.vehicleCode + "' purchase by " + me + " (" + choiceLabel + ").");
                }
                catch (Exception e) { Core.Log.Error("[Fleet] could not announce the purchase - the vehicle will belong to the host: " + e); }
            }
        }
    }
}
