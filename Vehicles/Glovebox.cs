using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Chassis.Net;
using MelonLoader;
#if IL2CPP
using Il2CppInterop.Runtime;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.UI;
using GameItemDeserializer = Il2CppScheduleOne.Persistence.ItemDeserializer;
using GameRegistry = Il2CppScheduleOne.Registry;
using SlotList = Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.ItemFramework.ItemSlot>;
#else
using ScheduleOne.ItemFramework;
using ScheduleOne.Storage;
using ScheduleOne.UI;
using GameItemDeserializer = ScheduleOne.Persistence.ItemDeserializer;
using GameRegistry = ScheduleOne.Registry;
using SlotList = System.Collections.Generic.List<ScheduleOne.ItemFramework.ItemSlot>;
#endif

namespace Chassis.Vehicles
{
    /// <summary>
    /// The glovebox behind the lid on the passenger side of the dash. Two modes, set by the preference
    /// <c>[Chassis] GloveboxOwnStorage</c>:
    ///   own (default) - a compartment of its own with 2 to 4 slots by car size, kept per vehicle next to the save
    ///                   (<c>Chassis.glovebox</c>, keyed by the vehicle's save GUID);
    ///   trunk         - the lid opens the vehicle's vanilla trunk, so a save stays readable without Chassis.
    /// Switching modes moves nothing: items in the own compartment stay there until it is turned on again.
    ///
    /// The slots shown in the menu are ownerless <c>ItemSlot</c>s on a <c>StorageEntity</c> that lives on an inactive
    /// object, so it never wakes, never networks and never saves itself. The contents travel over
    /// <see cref="ChassisNet"/> instead, one slot at a time as the item's JSON (base64):
    ///   req.glove.open vehicle player    -> glove.open vehicle player 1|0   (host: one player at a time)
    ///   req.glove vehicle player slot b64 -> glove vehicle slot b64          (host: only the player holding it)
    ///   req.glove.close vehicle player    -> glove.close vehicle
    ///                                       gloves vehicle b64,b64,...      (host: a whole glovebox, late join and seed)
    /// </summary>
    internal static class Glovebox
    {
        internal const string ControlName = "glovebox";
        internal const string FileName = "Chassis.glovebox";
        private const float PollSeconds = 0.25f;

        private sealed class Box
        {
            internal string[] Json;
            internal int Holder = -1;
        }

        private static readonly Dictionary<int, Box> _byId = new Dictionary<int, Box>();
        private static readonly Dictionary<string, string[]> _fromFile = new Dictionary<string, string[]>();
        private static readonly HashSet<int> _seeded = new HashSet<int>();
        private static string _loadedFrom;

        private static MelonPreferences_Entry<bool> _ownEntry;

        // The menu open on this peer.
        private static GameObject _carrier;
        private static StorageEntity _entity;
        private static readonly List<ItemSlot> _slots = new List<ItemSlot>();
        private static int _openId = -1;
        // Gloveboxes some player holds open (the host's word, on every peer), and the car whose trunk this peer opened
        // from the lid. Both swing the lid open.
        private static readonly HashSet<int> _lidOpen = new HashSet<int>();
        private static int _trunkId = -1;
        private static string[] _pushed;
        private static float _nextPoll;
        private static float _nextHostCheck;

        internal static bool OwnStorage => _ownEntry?.Value ?? true;

        internal static void LoadSetting()
        {
            try
            {
                var cat = MelonPreferences.CreateCategory("Chassis");
                _ownEntry = cat.CreateEntry("GloveboxOwnStorage", true, "Glovebox own storage",
                    "true: the glovebox has its own 2-4 slots. false: the glovebox opens the vehicle's trunk (vanilla-compatible); items in the own glovebox stay there until this is true again.");
                Core.Log.Msg("[Glovebox] mode " + (OwnStorage ? "own storage" : "trunk") + " (preference GloveboxOwnStorage = " + _ownEntry.Value + ").");
            }
            catch (Exception e) { Core.Log.Warning("[Glovebox] reading the GloveboxOwnStorage preference failed - own storage is used: " + e.Message); }
        }

        internal static string SetMode(bool own)
        {
            if (_ownEntry == null) return "no preference entry - the glovebox mode is not kept.";
            // Set, do not save: MelonPreferences.Save() fires every mod's preference listeners from this call; MelonLoader
            // writes the value when the game closes.
            _ownEntry.Value = own;
            string s = "glovebox mode set to " + (own ? "own storage" : "trunk") + ".";
            Core.Log.Msg("[Glovebox] " + s);
            return s;
        }

        /// <summary>Slots by car size; cars Chassis does not know take the middle size.</summary>
        internal static int SlotsFor(LandVehicle v)
        {
            string code = ((v != null ? v.vehicleCode : null) ?? string.Empty).ToLowerInvariant();
            switch (code)
            {
                case "shitbox":
                case "shitbox_police":
                case "coupe": return 2;
                case "van": return 4;
                default: return 3;
            }
        }

        private static Box Of(int id, LandVehicle v)
        {
            if (!_byId.TryGetValue(id, out var b))
            {
                _byId[id] = b = new Box { Json = new string[SlotsFor(v)] };
                for (int i = 0; i < b.Json.Length; i++) b.Json[i] = string.Empty;
            }
            return b;
        }

        // ---- opening -------------------------------------------------------------------------------------------------

        /// <summary>The lid was clicked on this peer (or the console asked). Returns what happened, for the console.</summary>
        internal static string Open(LandVehicle lv)
        {
            if (lv == null) { Core.Log.Warning("[Glovebox] open without a vehicle - ignored."); return "no vehicle."; }
            if (!OwnStorage) return OpenTrunk(lv);
            int id = Ids.Of(lv);
            if (id < 0) { Core.Log.Warning("[Glovebox] " + Registry.Name(lv) + " has no network id - glovebox not opened."); return "refused: no network id."; }
            var me = Player.Local;
            if (me == null) { Core.Log.Warning("[Glovebox] no local player - glovebox not opened."); return "refused: no local player."; }
            if (_openId >= 0) { Core.Log.Msg("[Glovebox] a glovebox is open here already - the click is ignored."); return "refused: a glovebox is open already."; }
            string pid = Ids.Of(me).ToString(CultureInfo.InvariantCulture);
            if (ChassisNet.IsHost)
            {
                HostOpen(id, Ids.Of(me));
                return _openId == id ? "own glovebox opened (" + _slots.Count + " slots)." : "refused: someone else has it open.";
            }
            ChassisNet.ToHost("req.glove.open", id.ToString(CultureInfo.InvariantCulture), pid);
            return "asked the host to open the glovebox.";
        }

        private static string OpenTrunk(LandVehicle lv)
        {
            var trunk = lv.Storage;
            if (trunk == null) { Core.Log.Warning("[Glovebox] " + Registry.Name(lv) + " has no trunk - the lid in trunk mode does nothing."); return "refused: no trunk."; }
            if (!trunk.CanBeOpened()) { Core.Log.Msg("[Glovebox] the trunk of " + Registry.Name(lv) + " cannot be opened now (not owned or in use)."); return "refused: the trunk cannot be opened now."; }
            trunk.Open();
            _trunkId = Ids.Of(lv);
            Core.Log.Msg("[Glovebox] trunk opened from the seat of " + Registry.Name(lv) + " (" + trunk.SlotCount + " slots).");
            return "trunk opened (" + trunk.SlotCount + " slots).";
        }

        /// <summary>Host: one player at a time may hold a glovebox, so two players cannot take the same item.</summary>
        internal static void HostOpen(int vehicle, int player)
        {
            var lv = Registry.ById(vehicle);
            if (lv == null) { Core.Log.Warning("[Glovebox] open for net " + vehicle + ", which is not here - dropped."); return; }
            Seed(lv, vehicle);
            var b = Of(vehicle, lv);
            if (b.Holder >= 0 && b.Holder != player && Ids.PlayerById(b.Holder) != null)
            {
                Core.Log.Msg("[Glovebox] " + Registry.Name(lv) + ": player net " + b.Holder + " has the glovebox open - player net " + player + " has to wait.");
                ChassisNet.ToAll("glove.open", vehicle.ToString(CultureInfo.InvariantCulture), player.ToString(CultureInfo.InvariantCulture), "0");
                return;
            }
            b.Holder = player;
            // The contents first, so the opening peer shows what the host holds.
            ChassisNet.ToAll("gloves", vehicle.ToString(CultureInfo.InvariantCulture), Encode(b.Json));
            ChassisNet.ToAll("glove.open", vehicle.ToString(CultureInfo.InvariantCulture), player.ToString(CultureInfo.InvariantCulture), "1");
        }

        internal const string BusyHint = "Someone else has the glovebox open.";

        internal static void OnOpen(int vehicle, int player, bool granted)
        {
            if (granted && _lidOpen.Add(vehicle)) Core.Log.Msg("[Glovebox] the lid of net " + vehicle + " opens (player net " + player + " holds it).");
            var me = Player.Local;
            if (me == null || Ids.Of(me) != player) return;
            var lv = Registry.ById(vehicle);
            if (lv == null) { Core.Log.Warning("[Glovebox] the host granted the glovebox of net " + vehicle + ", which is not here."); ReleaseRemote(vehicle); return; }
            if (!granted)
            {
                Core.Log.Msg("[Glovebox] the glovebox of " + Registry.Name(lv) + " is open for another player - not opened.");
                var hints = Singleton<HintDisplay>.Instance;
                if (hints == null) { Core.Log.Warning("[Glovebox] no hint display - the refusal shows only in this log."); return; }
                hints.ShowHint(BusyHint, 3f, null);
                Core.Log.Msg("[Glovebox] hint shown: " + BusyHint);
                return;
            }
            ShowMenu(lv, vehicle);
        }

        private static void ShowMenu(LandVehicle lv, int id)
        {
            var menu = Singleton<StorageMenu>.Instance;
            if (menu == null) { Core.Log.Error("[Glovebox] no storage menu - glovebox not opened."); ReleaseRemote(id); return; }
            if (menu.IsOpen) { Core.Log.Msg("[Glovebox] another storage menu is open - glovebox not opened."); ReleaseRemote(id); return; }
            var b = Of(id, lv);
            try
            {
                BuildCarrier(b.Json.Length);
                for (int i = 0; i < _slots.Count; i++)
                {
                    var slot = _slots[i];
                    if (slot.ItemInstance != null) slot.ClearStoredInstance(false);
                    string json = b.Json[i];
                    if (string.IsNullOrEmpty(json)) continue;
                    var item = GameItemDeserializer.LoadItem(json);
                    if (item == null) { Core.Log.Warning("[Glovebox] slot " + (i + 1) + " of " + Registry.Name(lv) + " holds an item the game cannot read - shown empty, kept in the save."); continue; }
                    slot.SetStoredItem(item, false);
                }
                _entity.StorageEntityName = "Glovebox";
                _entity.StorageEntitySubtitle = string.Empty;
                _openId = id;
                _pushed = (string[])b.Json.Clone();
                _nextPoll = Time.unscaledTime + PollSeconds;
#if IL2CPP
                menu.Open(_entity, DelegateSupport.ConvertDelegate<Il2CppSystem.Action>((Action)OnMenuClosed));
#else
                menu.Open(_entity, OnMenuClosed);
#endif
                Core.Log.Msg("[Glovebox] own glovebox opened in " + Registry.Name(lv) + " (" + _slots.Count + " slots, " + Count(b.Json) + " item(s)).");
            }
            catch (Exception e)
            {
                _openId = -1;
                Core.Log.Error("[Glovebox] opening the glovebox of " + Registry.Name(lv) + " failed: " + e);
                ReleaseRemote(id);
            }
        }

        private static void BuildCarrier(int count)
        {
            if (_carrier == null)
            {
                _carrier = new GameObject("Chassis_GloveboxSlots");
                // Inactive before the component is added: the storage entity never wakes, so it registers no GUID,
                // no save file and no network object.
                _carrier.SetActive(false);
                UnityEngine.Object.DontDestroyOnLoad(_carrier);
                _entity = _carrier.AddComponent<StorageEntity>();
                _slots.Clear();
            }
            if (_slots.Count == count) return;
            _slots.Clear();
            var list = new SlotList();
            for (int i = 0; i < count; i++)
            {
                var slot = new ItemSlot(false);
                list.Add(slot);
                _slots.Add(slot);
            }
            new ItemSlotSiblingSet(list);
            _entity.SlotCount = count;
            _entity.DisplayRowCount = 1;
            _entity.ItemSlots = list;
        }

        internal static string Close()
        {
            var menu = Singleton<StorageMenu>.InstanceExists ? Singleton<StorageMenu>.Instance : null;
            if (menu == null || !menu.IsOpen) return "no storage menu open.";
            menu.Close();
            return "storage menu closed.";
        }

        private static void OnMenuClosed()
        {
            try
            {
                if (_openId < 0) return;
                int id = _openId;
                Push(id);
                _openId = -1;
                foreach (var slot in _slots) if (slot.ItemInstance != null) slot.ClearStoredInstance(false);
                ReleaseRemote(id);
                Core.Log.Msg("[Glovebox] glovebox of net " + id + " closed.");
            }
            catch (Exception e) { Core.Log.Error("[Glovebox] closing the glovebox failed: " + e); }
        }

        private static void ReleaseRemote(int vehicle)
        {
            var me = Player.Local;
            ChassisNet.ToHost("req.glove.close", vehicle.ToString(CultureInfo.InvariantCulture), (me != null ? Ids.Of(me) : -1).ToString(CultureInfo.InvariantCulture));
        }

        internal static void HostClose(int vehicle, int player)
        {
            if (!_byId.TryGetValue(vehicle, out var b) || b.Holder != player) return;
            b.Holder = -1;
            Core.Log.Msg("[Glovebox] player net " + player + " let go of the glovebox of net " + vehicle + ".");
            ChassisNet.ToAll("glove.close", vehicle.ToString(CultureInfo.InvariantCulture));
        }

        internal static void OnClose(int vehicle)
        {
            if (_lidOpen.Remove(vehicle)) Core.Log.Msg("[Glovebox] the lid of net " + vehicle + " closes.");
        }

        /// <summary>True while the lid of this car stands open: someone holds its glovebox, or this peer opened the trunk
        /// from it.</summary>
        internal static bool LidOpen(LandVehicle lv)
        {
            if (lv == null) return false;
            int id = Ids.Of(lv);
            return id >= 0 && (_lidOpen.Contains(id) || _openId == id || _trunkId == id);
        }

        // ---- contents ------------------------------------------------------------------------------------------------

        private static string JsonOf(ItemSlot slot)
        {
            var item = slot.ItemInstance;
            return item == null || item.Quantity <= 0 ? string.Empty : item.GetItemData().GetJson(false);
        }

        /// <summary>Sends the slots that changed in the open menu since the last push.</summary>
        private static void Push(int id)
        {
            if (_pushed == null) return;
            var me = Player.Local;
            string pid = (me != null ? Ids.Of(me) : -1).ToString(CultureInfo.InvariantCulture);
            for (int i = 0; i < _slots.Count && i < _pushed.Length; i++)
            {
                string now = JsonOf(_slots[i]);
                if (now == _pushed[i]) continue;
                _pushed[i] = now;
                ChassisNet.ToHost("req.glove", id.ToString(CultureInfo.InvariantCulture), pid, i.ToString(CultureInfo.InvariantCulture), B64(now));
            }
        }

        internal static void HostSet(int vehicle, int player, int slot, string b64)
        {
            var lv = Registry.ById(vehicle);
            if (lv == null) { Core.Log.Warning("[Glovebox] slot change for net " + vehicle + ", which is not here - dropped."); return; }
            var b = Of(vehicle, lv);
            if (b.Holder != player) { Core.Log.Warning("[Glovebox] slot change on " + Registry.Name(lv) + " from player net " + player + ", who does not hold it - dropped."); return; }
            if (slot < 0 || slot >= b.Json.Length) { Core.Log.Warning("[Glovebox] slot " + slot + " on " + Registry.Name(lv) + " does not exist - dropped."); return; }
            ChassisNet.ToAll("glove", vehicle.ToString(CultureInfo.InvariantCulture), slot.ToString(CultureInfo.InvariantCulture), b64);
        }

        internal static void OnSet(int vehicle, int slot, string b64)
        {
            var b = Of(vehicle, Registry.ById(vehicle));
            if (slot < 0 || slot >= b.Json.Length) { Core.Log.Warning("[Glovebox] slot " + slot + " of net " + vehicle + " does not exist here - ignored."); return; }
            b.Json[slot] = Unb64(b64);
            Core.Log.Msg("[Glovebox] net " + vehicle + " slot " + (slot + 1) + " " + (b.Json[slot].Length == 0 ? "emptied" : "filled") + ".");
        }

        internal static void OnAll(int vehicle, string payload)
        {
            var b = Of(vehicle, Registry.ById(vehicle));
            string[] f = (payload ?? string.Empty).Split(',');
            for (int i = 0; i < b.Json.Length; i++) b.Json[i] = i < f.Length ? Unb64(f[i]) : string.Empty;
        }

        /// <summary>Host: every glovebox with something in it, for a peer that joined late.</summary>
        internal static void HostReplay()
        {
            foreach (var kv in _byId)
                if (Count(kv.Value.Json) > 0) ChassisNet.ToAll("gloves", kv.Key.ToString(CultureInfo.InvariantCulture), Encode(kv.Value.Json));
        }

        private static string Encode(string[] json)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < json.Length; i++) { if (i > 0) sb.Append(','); sb.Append(B64(json[i])); }
            return sb.ToString();
        }

        private static string B64(string s) => string.IsNullOrEmpty(s) ? string.Empty : Convert.ToBase64String(Encoding.UTF8.GetBytes(s));

        private static string Unb64(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(s)); }
            catch (FormatException) { Core.Log.Warning("[Glovebox] an unreadable slot came over the network - kept empty."); return string.Empty; }
        }

        private static int Count(string[] json)
        {
            int n = 0;
            foreach (string s in json) if (!string.IsNullOrEmpty(s)) n++;
            return n;
        }

        // ---- per frame -----------------------------------------------------------------------------------------------

        private static readonly List<int> _released = new List<int>();

        internal static void Tick()
        {
            if (_trunkId >= 0)
            {
                var menu = Singleton<StorageMenu>.InstanceExists ? Singleton<StorageMenu>.Instance : null;
                if (menu == null || !menu.IsOpen) { Core.Log.Msg("[Glovebox] the trunk menu opened from the lid of net " + _trunkId + " closed - the lid closes."); _trunkId = -1; }
            }
            if (_openId >= 0)
            {
                var lv = Registry.ById(_openId);
                if (lv == null) { Core.Log.Warning("[Glovebox] the vehicle of the open glovebox is gone - menu closed."); Close(); }
                else if (Time.unscaledTime >= _nextPoll) { _nextPoll = Time.unscaledTime + PollSeconds; Push(_openId); }
            }
            if (!ChassisNet.IsHost) return;
            foreach (var v in Registry.All)
            {
                if (v == null) continue;
                int id = Ids.Of(v);
                if (id >= 0) Seed(v, id);
            }
            if (Time.unscaledTime < _nextHostCheck) return;
            _nextHostCheck = Time.unscaledTime + 2f;
            foreach (var kv in _byId)
            {
                if (kv.Value.Holder < 0 || Ids.PlayerById(kv.Value.Holder) != null) continue;
                Core.Log.Msg("[Glovebox] player net " + kv.Value.Holder + " left while holding the glovebox of net " + kv.Key + " - released.");
                kv.Value.Holder = -1;
                _released.Add(kv.Key);
            }
            foreach (int id in _released) ChassisNet.ToAll("glove.close", id.ToString(CultureInfo.InvariantCulture));
            _released.Clear();
        }

        // ---- save file -----------------------------------------------------------------------------------------------

        private static string SavePath()
        {
            if (!Singleton<GameLoadManager>.InstanceExists) return null;
            string folder = Singleton<GameLoadManager>.Instance.LoadedGameFolderPath;
            return string.IsNullOrEmpty(folder) || !Directory.Exists(folder) ? null : Path.Combine(folder, FileName);
        }

        /// <summary>Host: the first time a vehicle is seen, it takes over what the save file holds for it.</summary>
        private static void Seed(LandVehicle v, int id)
        {
            if (_seeded.Contains(id) || SavePath() == null) return;
            _seeded.Add(id);
            EnsureLoaded();
            if (!v.IsPlayerOwned) return;
            string guid = v.GUID.ToString();
            if (!_fromFile.TryGetValue(guid, out var saved)) return;
            _fromFile.Remove(guid);
            var b = Of(id, v);
            for (int i = 0; i < b.Json.Length; i++) b.Json[i] = i < saved.Length ? saved[i] : string.Empty;
            if (saved.Length > b.Json.Length && Count(saved) > Count(b.Json))
                Core.Log.Warning("[Glovebox] " + Registry.Name(v) + ": the save holds more slots than this car has - the extra items are dropped.");
            Core.Log.Msg("[Glovebox] " + Registry.Name(v) + ": " + Count(b.Json) + " item(s) from the save.");
            ChassisNet.ToAll("gloves", id.ToString(CultureInfo.InvariantCulture), Encode(b.Json));
        }

        internal static void EnsureLoaded()
        {
            string path = SavePath();
            if (path == null || path == _loadedFrom) return;
            _loadedFrom = path;
            _fromFile.Clear();
            if (!File.Exists(path)) { Core.Log.Msg("[Glovebox] no " + FileName + " in this save yet."); return; }
            try
            {
                int n = 0;
                foreach (string line in File.ReadAllLines(path))
                {
                    int bar = line.IndexOf('|');
                    if (bar <= 0) continue;
                    string[] f = line.Substring(bar + 1).Split(',');
                    var json = new string[f.Length];
                    for (int i = 0; i < f.Length; i++) json[i] = Unb64(f[i]);
                    _fromFile[line.Substring(0, bar)] = json;
                    n++;
                }
                Core.Log.Msg("[Glovebox] loaded " + n + " glovebox record(s) from " + path + ".");
            }
            catch (Exception e) { Core.Log.Error("[Glovebox] could not read " + path + ", gloveboxes start empty: " + e.Message); }
        }

        /// <summary>Host: writes every player-owned vehicle's glovebox. Runs when the game saves.</summary>
        internal static void Save()
        {
            if (!ChassisNet.IsHost) return;
            string path = SavePath();
            if (path == null) { Core.Log.Warning("[Glovebox] no save folder - glovebox contents stay in memory."); return; }
            EnsureLoaded();
            // A menu open on the host: what it shows now is what gets saved.
            if (_openId >= 0) Push(_openId);
            try
            {
                var sb = new StringBuilder();
                int n = 0;
                foreach (var v in Registry.All)
                {
                    if (v == null || !v.IsPlayerOwned) continue;
                    int id = Ids.Of(v);
                    if (id < 0 || !_byId.TryGetValue(id, out var b) || Count(b.Json) == 0) continue;
                    sb.Append(v.GUID.ToString()).Append('|').Append(Encode(b.Json)).Append('\n');
                    n++;
                }
                // Records of vehicles that did not show up this session stay in the file.
                foreach (var kv in _fromFile) sb.Append(kv.Key).Append('|').Append(Encode(kv.Value)).Append('\n');
                File.WriteAllText(path, sb.ToString());
                Core.Log.Msg("[Glovebox] saved " + n + " glovebox(es) to " + path + ".");
            }
            catch (Exception e) { Core.Log.Error("[Glovebox] could not write " + path + ": " + e.Message); }
        }

        // ---- console -------------------------------------------------------------------------------------------------

        internal static string Describe(LandVehicle lv)
        {
            if (lv == null) return "glovebox: no vehicle.";
            if (!OwnStorage)
                return "glovebox in trunk mode (trunk " + (lv.Storage != null ? lv.Storage.SlotCount + " slots" : "missing") + ")";
            int id = Ids.Of(lv);
            var b = id >= 0 ? Of(id, lv) : null;
            if (b == null) return "own glovebox: no network id.";
            int items = Count(b.Json);
            return "own glovebox, " + b.Json.Length + " slots, " + (_openId == id ? "open" : "closed") + ", " + items + " item" + (items == 1 ? "" : "s")
                + (b.Holder >= 0 ? ", held by player net " + b.Holder : "");
        }

        /// <summary>Console: puts one of a known item into the first empty slot, as the host.</summary>
        internal static string DebugPut(LandVehicle lv)
        {
            if (lv == null) return "put failed: no vehicle.";
            if (!ChassisNet.IsHost) return "put failed: only the host can put a test item.";
            int id = Ids.Of(lv);
            if (id < 0) return "put failed: no network id.";
            var b = Of(id, lv);
            int free = Array.IndexOf(b.Json, string.Empty);
            if (free < 0) return "put failed: the glovebox is full.";
            foreach (string itemId in new[] { "cuke", "banana", "flashlight" })
            {
                var def = GameRegistry.GetItem(itemId);
                if (def == null) continue;
                var inst = def.GetDefaultInstance(1);
                if (inst == null) continue;
                b.Json[free] = inst.GetItemData().GetJson(false);
                ChassisNet.ToAll("gloves", id.ToString(CultureInfo.InvariantCulture), Encode(b.Json));
                Core.Log.Msg("[Glovebox] test item '" + itemId + "' put into slot " + (free + 1) + " of " + Registry.Name(lv) + ".");
                return "put: " + itemId + " in slot " + (free + 1) + ".";
            }
            return "put failed: none of the test items exist.";
        }

        /// <summary>Console: empties the first filled slot of the menu open on this peer, as a drag out would.</summary>
        internal static string DebugTake()
        {
            if (_openId < 0) return "take failed: no glovebox open here.";
            foreach (var slot in _slots)
            {
                if (slot.ItemInstance == null) continue;
                slot.ClearStoredInstance(false);
                Core.Log.Msg("[Glovebox] test take: one slot of net " + _openId + " emptied in the open menu.");
                return "take: one slot emptied.";
            }
            return "take failed: the open glovebox is empty.";
        }

        internal static void Reset()
        {
            _byId.Clear();
            _fromFile.Clear();
            _seeded.Clear();
            _loadedFrom = null;
            _openId = -1;
            _trunkId = -1;
            _lidOpen.Clear();
            _pushed = null;
            foreach (var slot in _slots) if (slot != null && slot.ItemInstance != null) slot.ClearStoredInstance(false);
        }
    }
}
