using System;
using System.Collections.Generic;
using Chassis.Vehicles;

namespace Chassis.Api
{
    /// <summary>Who owns a vehicle and whether it is locked, as the host keeps it.</summary>
    public sealed class OwnerRecord
    {
        /// <summary><c>Player.PlayerCode</c> of the owner.</summary>
        public string Owner = string.Empty;
        /// <summary>Only the owner can get in while this is set.</summary>
        public bool Locked;
    }

    public static partial class ChassisApi
    {
        // ---- profile per vehicle -----------------------------------------------------------------------------------

        private static readonly Dictionary<int, string> _assigned = new Dictionary<int, string>();
        private static readonly Dictionary<int, LandVehicle> _assignedVehicles = new Dictionary<int, LandVehicle>();

        /// <summary>
        /// Gives one vehicle the profile of another code. For mods that build their vehicle on a vanilla one (a "veeper"
        /// under a camper van): without this, the profile would follow the vanilla code to every vehicle of that kind.
        /// Call it on every peer; it holds until <see cref="Unassign"/> or until the vehicle is gone.
        /// </summary>
        /// <param name="code">The profile code to use, e.g. "mymod_camper"; it may be registered before or after this call.</param>
        public static void Assign(LandVehicle vehicle, string code)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            if (string.IsNullOrEmpty(code)) throw new ArgumentException("a profile code is needed");
            _assigned[vehicle.GetInstanceID()] = code;
            _assignedVehicles[vehicle.GetInstanceID()] = vehicle;
            Core.Log.Msg("[Profiles] " + vehicle.name + " net " + Net.Ids.Of(vehicle) + " uses profile '" + code + "'" + (Get(code) == null ? ", which has no profile yet" : "") + ".");
            Registry.Touch();
        }

        /// <summary>Ends an <see cref="Assign"/>: the vehicle uses the profile of its own vehicle code again.</summary>
        public static void Unassign(LandVehicle vehicle)
        {
            if (vehicle == null) return;
            _assignedVehicles.Remove(vehicle.GetInstanceID());
            if (_assigned.Remove(vehicle.GetInstanceID())) Core.Log.Msg("[Profiles] " + vehicle.name + " uses its own vehicle code again.");
            Registry.Touch();
        }

        /// <summary>The profile code a vehicle uses: the assigned one, else its vehicle code.</summary>
        public static string CodeOf(LandVehicle vehicle)
        {
            if (vehicle == null) return null;
            return _assigned.TryGetValue(vehicle.GetInstanceID(), out string code) ? code : vehicle.vehicleCode;
        }

        /// <summary>The profile a vehicle uses, or null when Chassis leaves it to vanilla.</summary>
        public static ChassisProfile For(LandVehicle vehicle) => Get(CodeOf(vehicle));

        private static readonly List<int> _stale = new List<int>();

        /// <summary>
        /// Drops assignments of destroyed vehicles and hands back the assigned ones the vehicle manager does not list
        /// (a mod may take its vehicle off the manager's lists).
        /// </summary>
        internal static void PruneAssignments(HashSet<int> listed, List<LandVehicle> unlisted)
        {
            if (_assigned.Count == 0) return;
            _stale.Clear();
            foreach (var kv in _assignedVehicles)
            {
                if (kv.Value == null) { _stale.Add(kv.Key); continue; }
                if (!listed.Contains(kv.Key)) unlisted.Add(kv.Value);
            }
            foreach (int id in _stale)
            {
                Core.Log.Msg("[Profiles] dropped the profile assignment '" + _assigned[id] + "' of a vehicle that is gone.");
                _assigned.Remove(id);
                _assignedVehicles.Remove(id);
            }
        }

        // ---- sounds ------------------------------------------------------------------------------------------------

        /// <summary>Makes a clip available to profiles by name (<see cref="HornProfile.Clip"/>, <see cref="HornProfile.Chirp"/>).</summary>
        /// <param name="name">
        /// The name profiles use, e.g. "mymod_horn". The same name again replaces the clip. "chassis.click" (a cabin
        /// switch moves), "chassis.tick" and "chassis.tock" (the indicator relay) replace the built-in cabin sounds.
        /// </param>
        public static void RegisterSound(string name, AudioClip clip) => Sounds.Register(name, clip);

        // ---- horn --------------------------------------------------------------------------------------------------

        /// <summary>
        /// Sounds the horn of a vehicle on every peer, and with <paramref name="flash"/> blinks its headlights twice.
        /// The host allows it when the local player sits in the vehicle or owns it. Every vehicle has a horn; the key is G.
        /// </summary>
        /// <param name="flash">true also blinks the headlights twice.</param>
        public static void Honk(LandVehicle vehicle, bool flash = false) => Horn.Request(vehicle, flash);

        // ---- indicators --------------------------------------------------------------------------------------------

        /// <summary>Raised on every peer with the vehicle and the new side when an indicator changes: -1 left, 1 right, 0 off, 2 hazard lights.</summary>
        public static event Action<LandVehicle, int> IndicatorChanged;

        /// <summary>Switches the indicator of a vehicle for every peer: -1 left, 1 right, 0 off, 2 hazard lights. The keys are the left and right arrows.</summary>
        /// <param name="side">-1 left, 1 right, 0 off, 2 both sides (hazard lights).</param>
        public static void SetIndicator(LandVehicle vehicle, int side) => Indicators.Request(vehicle, side, false);

        /// <summary>The side a vehicle signals: -1 left, 1 right, 0 off, 2 hazard lights.</summary>
        public static int Indicator(LandVehicle vehicle) => Indicators.SideOf(vehicle);

        /// <summary>Whether the indicator of a vehicle is in the lit half of its flash right now; for mods that draw their own lamps.</summary>
        public static bool IndicatorLit(LandVehicle vehicle) => Indicators.SideOf(vehicle) != 0 && Indicators.Phase;

        internal static void RaiseIndicator(LandVehicle vehicle, int side)
        {
            if (IndicatorChanged == null) return;
            try { IndicatorChanged(vehicle, side); }
            catch (Exception e) { Core.Log.Error("[Indicators] an IndicatorChanged handler failed: " + e); }
        }

        // ---- moving parts ------------------------------------------------------------------------------------------

        /// <summary>Opens or closes a lid from the profile for every peer.</summary>
        /// <param name="part">A <see cref="PartProfile.Name"/> from the vehicle's profile, e.g. "boot"; case is ignored.</param>
        /// <param name="open">true opens the lid, false closes it.</param>
        public static void SetPart(LandVehicle vehicle, string part, bool open) => Parts.Request(vehicle, part, open);

        /// <summary>Whether a lid is held open by <see cref="SetPart"/>.</summary>
        public static bool PartOpen(LandVehicle vehicle, string part) => Parts.IsOpen(vehicle, part);

        /// <summary>
        /// Tells every peer which end the vehicle's storage is opened from, for lids with <see cref="PartProfile.Storage"/>.
        /// Vanilla storage counts as "rear" until a mod says otherwise.
        /// </summary>
        /// <param name="front">true for storage opened at the front, false for the rear.</param>
        public static void SetStorageEnd(LandVehicle vehicle, bool front) => Parts.RequestStorageEnd(vehicle, front);

        // ---- owner and lock ----------------------------------------------------------------------------------------

        /// <summary>Raised on every peer after the host's owner and lock records arrived and something changed.</summary>
        public static event Action OwnersChanged;

        /// <summary>
        /// Host: asked once for a player-owned vehicle that has no record yet, for example one bought before Chassis kept
        /// owners. Return the owner and lock state to import, or null to let the vehicle belong to the host.
        /// </summary>
        public static Func<LandVehicle, OwnerRecord> ResolveUnknownOwner;

        /// <summary>The local player's code, empty before the player exists.</summary>
        public static string LocalPlayerCode => Fleet.MyCode();

        /// <summary>Player code of the owner, or empty when the vehicle has no record (not player owned, or not synced yet).</summary>
        public static string OwnerOf(LandVehicle vehicle) => Fleet.For(vehicle)?.Owner ?? string.Empty;

        /// <summary>True when the local player owns the vehicle.</summary>
        public static bool IsMine(LandVehicle vehicle) => Fleet.IsMine(vehicle);

        /// <summary>True when the vehicle is locked; false when it has no owner record.</summary>
        public static bool IsLocked(LandVehicle vehicle) => Fleet.For(vehicle)?.Locked ?? false;

        /// <summary>
        /// Locks or unlocks a vehicle the local player owns. A locked vehicle lets in its owner, and others only while the
        /// owner sits inside. There is no key for it: locking is up to mods (Yesla's app) and this call.
        /// </summary>
        public static void SetLocked(LandVehicle vehicle, bool locked) => Fleet.RequestLock(vehicle, locked);

        /// <summary>Host only: gives a vehicle a new owner. Returns false on a client or for a vehicle that is not player owned.</summary>
        /// <param name="playerCode">The new owner's <c>Player.PlayerCode</c>, the same kind of value as <see cref="LocalPlayerCode"/>.</param>
        public static bool SetOwner(LandVehicle vehicle, string playerCode) => Fleet.HostSetOwner(vehicle, playerCode, "set by a mod");

        internal static void RaiseOwners()
        {
            if (OwnersChanged == null) return;
            try { OwnersChanged(); }
            catch (Exception e) { Core.Log.Error("[Fleet] an OwnersChanged handler failed: " + e); }
        }
    }
}
