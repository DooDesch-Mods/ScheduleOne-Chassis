using System;
using System.Collections.Generic;
using Chassis.Api;
using Chassis.Net;
using HarmonyLib;

namespace Chassis.Seating
{
    /// <summary>
    /// Who sits where, on this peer. Every seated player of a vehicle with a Chassis profile gets a <see cref="SeatRig"/>:
    /// the local player through <c>Player.EnterVehicle</c>, everyone else through <c>Player.CurrentVehicleChanged</c>,
    /// which vanilla runs for remote players only. Pushers get a rig too, from <see cref="Rideables.Push"/>.
    ///
    /// The rig poses the body every frame in a postfix to <c>Player.Update</c>, after vanilla and before the animator
    /// and Final IK run.
    /// </summary>
    internal static class Occupants
    {
        private static readonly Dictionary<int, SeatRig> _rigs = new Dictionary<int, SeatRig>();
        private static readonly List<int> _dead = new List<int>();
        private static bool _subscribed;

        /// <summary>Drive input of seated players on other peers, by vehicle net id: -1, 0 or 1 (stroke direction).</summary>
        private static readonly Dictionary<int, float> _remoteDrive = new Dictionary<int, float>();
        private static int _sentDriveVehicle = -1;
        private static float _sentDrive;

        internal static IEnumerable<SeatRig> All => _rigs.Values;

        internal static SeatRig Of(Player p)
        {
            if (p == null) return null;
            return _rigs.TryGetValue(p.GetInstanceID(), out var rig) ? rig : null;
        }

        internal static bool HasProfile(LandVehicle lv) => lv != null && ChassisApi.For(lv) != null;

        /// <summary>Seats a player's body on a seat of a profiled vehicle (or behind it with <see cref="SeatRig.PushSeat"/>).</summary>
        internal static SeatRig Seat(Player p, LandVehicle lv, int seatIndex, bool glide, string why)
        {
            if (p == null || lv == null) { Core.Log.Warning("[Seat] seat asked without a player or vehicle (" + why + ")."); return null; }
            Subscribe();
            var old = Of(p);
            if (old != null)
            {
                if (old.Vehicle == lv.transform && old.SeatIndex == seatIndex) return old;
                Leave(p, "moved to another seat");
            }
            var rig = SeatRig.Create(p, lv, ChassisApi.CodeOf(lv), seatIndex, glide);
            if (rig == null) return null;
            _rigs[p.GetInstanceID()] = rig;
            if (p.IsLocalPlayer && !rig.IsPusher)
            {
                p.SetThirdPersonMeshesVisibility(true);
                if (rig.Profile.Pose == "Sitting") p.SendAnimationBool("Sitting", true);
            }
            Core.Log.Msg("[Seat] " + SeatRig.Who(p) + " -> " + ChassisApi.CodeOf(lv) + " net " + Ids.Of(lv) + " (" + why + ").");
            return rig;
        }

        internal static void Leave(Player p, string why)
        {
            var rig = Of(p);
            if (rig == null) return;
            _rigs.Remove(p.GetInstanceID());
            rig.Detach(why);
            if (p != null && p.IsLocalPlayer && !rig.IsPusher)
            {
                if (rig.Profile != null && rig.Profile.Pose == "Sitting") p.SendAnimationBool("Sitting", false);
                p.SetThirdPersonMeshesVisibility(false);
                FirstPersonView.Set(false, "left the seat");
            }
        }

        internal static void Reset()
        {
            foreach (var rig in new List<SeatRig>(_rigs.Values))
            {
                try { rig.Detach("world reset"); }
                catch (Exception e) { Core.Log.Error("[Seat] reset of a rig failed: " + e.Message); }
            }
            _rigs.Clear();
            _remoteDrive.Clear();
            _sentDriveVehicle = -1;
            _sentDrive = 0f;
        }

        private static void Subscribe()
        {
            if (_subscribed) return;
            _subscribed = true;
            ChassisApi.Changed += code =>
            {
                foreach (var rig in _rigs.Values) if (rig.Code == code) rig.Refresh();
            };
        }

        /// <summary>Seat index of a player in a vehicle, from the seat occupants vanilla syncs; 0 when not known yet.</summary>
        private static int SeatOf(Player p, LandVehicle lv)
        {
            var seats = lv.Seats;
            if (seats == null) return 0;
            for (int i = 0; i < seats.Length; i++) if (seats[i] != null && seats[i].Occupant == p) return i;
            return 0;
        }

        // ---- drive state for the hands -----------------------------------------------------------------------------

        /// <summary>The local driver's own throttle, before a pusher's input is added. Set by <see cref="Rideables.Push"/>.</summary>
        internal static float LocalDrive;

        private static void UpdateDrive(SeatRig rig)
        {
            if (rig.IsPusher) { rig.DriveInput = 0f; return; }
            var lv = rig.Vehicle.GetComponent<LandVehicle>();
            int id = Ids.Of(lv);
            if (rig.Player.IsLocalPlayer)
            {
                rig.DriveInput = lv != null && lv.LocalPlayerIsDriver ? LocalDrive : 0f;
                float sign = Mathf.Abs(rig.DriveInput) > 0.05f ? Mathf.Sign(rig.DriveInput) : 0f;
                if (id != _sentDriveVehicle || sign != _sentDrive)
                {
                    _sentDriveVehicle = id; _sentDrive = sign;
                    ChassisNet.ToHost("req.drive", id.ToString(), ((int)sign).ToString());
                }
                return;
            }
            rig.DriveInput = _remoteDrive.TryGetValue(id, out float d) ? d : 0f;
        }

        /// <summary>Host relays a driver's stroke state; peers keep it for the remote seated body.</summary>
        internal static void OnDrive(int vehicle, float sign)
        {
            _remoteDrive.TryGetValue(vehicle, out float old);
            _remoteDrive[vehicle] = sign;
            if (old == sign) return;
            // The driver's own peer gets its broadcast back; its hands already follow the local input.
            if (vehicle == _sentDriveVehicle) return;
            Core.Log.Msg("[Seat] remote stroke on net " + vehicle + ": " + (sign > 0 ? "forward" : sign < 0 ? "back" : "coasting") + ".");
        }

        // ---- per frame ---------------------------------------------------------------------------------------------

        private static void Tick(Player p)
        {
            var rig = Of(p);
            if (rig == null) return;
            if (!rig.Alive) { _rigs.Remove(p.GetInstanceID()); rig.Detach("vehicle or body gone"); return; }
            if (!rig.IsPusher)
            {
                var current = p.CurrentVehicle;
                if (current == null || current.transform != rig.Vehicle) { Leave(p, "no longer in that vehicle"); return; }
            }
            UpdateDrive(rig);
            rig.Pose(Time.deltaTime);
        }

        /// <summary>Drops rigs whose player left the game.</summary>
        internal static void Sweep()
        {
            _dead.Clear();
            foreach (var kv in _rigs) if (kv.Value.Player == null || kv.Value.Vehicle == null) _dead.Add(kv.Key);
            foreach (int k in _dead)
            {
                var rig = _rigs[k];
                _rigs.Remove(k);
                rig.Detach("player or vehicle gone");
            }
        }

        internal static string Describe()
        {
            if (_rigs.Count == 0) return "occupants: none";
            var parts = new List<string>();
            foreach (var rig in _rigs.Values) parts.Add(rig.Describe());
            return "occupants: " + string.Join(" | ", parts);
        }

        // ---- hooks -------------------------------------------------------------------------------------------------

        [HarmonyPatch(typeof(Player), nameof(Player.EnterVehicle))]
        private static class EnterPatch
        {
            private static void Postfix(Player __instance, LandVehicle vehicle, VehicleSeat seat)
            {
                try
                {
                    if (!HasProfile(vehicle)) return;
                    int index = seat != null && vehicle.Seats != null ? Array.IndexOf(vehicle.Seats, seat) : 0;
                    if (index < 0) index = SeatOf(__instance, vehicle);
                    Seat(__instance, vehicle, index, true, "entered");
                }
                catch (Exception e) { Core.Log.Error("[Seat] seating " + SeatRig.Who(__instance) + " after EnterVehicle failed: " + e); }
            }
        }

        /// <summary>Before vanilla moves the body back to the player, so the rig hands it over from the anchor.</summary>
        [HarmonyPatch(typeof(Player), nameof(Player.ExitVehicle))]
        private static class ExitPatch
        {
            private static void Prefix(Player __instance)
            {
                try { Leave(__instance, "exited"); }
                catch (Exception e) { Core.Log.Error("[Seat] releasing " + SeatRig.Who(__instance) + " before ExitVehicle failed: " + e); }
            }
        }

        /// <summary>Remote players: vanilla re-parents and hides their body here (it skips the owner).</summary>
        [HarmonyPatch(typeof(Player), "CurrentVehicleChanged")]
        private static class RemotePatch
        {
            private static void Prefix(Player __instance, FishNetObject oldVeh, FishNetObject newVeh)
            {
                try
                {
                    if (__instance.IsOwner || oldVeh == newVeh) return;
                    var rig = Of(__instance);
                    if (rig != null && !rig.IsPusher) Leave(__instance, "remote vehicle changed");
                }
                catch (Exception e) { Core.Log.Error("[Seat] releasing remote " + SeatRig.Who(__instance) + " failed: " + e); }
            }

            private static void Postfix(Player __instance, FishNetObject oldVeh, FishNetObject newVeh)
            {
                try
                {
                    if (__instance.IsOwner || oldVeh == newVeh || newVeh == null) return;
                    var lv = newVeh.GetComponent<LandVehicle>();
                    if (!HasProfile(lv)) return;
                    Seat(__instance, lv, SeatOf(__instance, lv), true, "remote entered");
                }
                catch (Exception e) { Core.Log.Error("[Seat] seating remote " + SeatRig.Who(__instance) + " failed: " + e); }
            }
        }

        [HarmonyPatch(typeof(Player), "Update")]
        private static class PosePatch
        {
            private static void Postfix(Player __instance)
            {
                try { Tick(__instance); }
                catch (Exception e)
                {
                    Core.Log.Error("[Seat] posing " + SeatRig.Who(__instance) + " failed, the body goes back to vanilla: " + e);
                    try { Leave(__instance, "pose error"); }
                    catch (Exception e2) { Core.Log.Error("[Seat] releasing after the pose error failed too: " + e2.Message); }
                }
            }
        }
    }
}
