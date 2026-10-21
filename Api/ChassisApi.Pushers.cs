// Copyright (c) 2026 DooDesch. DooDesch API License (see LICENSE.md): you may copy this file unchanged.
using Chassis.Seating;

namespace Chassis.Api
{
    public static partial class ChassisApi
    {
        // ---- pushers -----------------------------------------------------------------------------------------------
        //
        // A pusher stands behind a vehicle with both hands on its handles, as the vehicle's PushProfile says. Chassis
        // only poses the body; who pushes, where the player stands and what the input does is up to the mod that
        // offers pushing (Rollin for its wheelchair). Call these on every peer for the same player.

        /// <summary>
        /// Poses a player's body behind a vehicle with the hands on the handles from <see cref="ChassisProfile.Push"/>.
        /// For the local player the key hints switch to the pusher's keys. Returns false with the reason logged when the
        /// vehicle has no push profile or the body cannot be posed.
        /// </summary>
        /// <param name="glide">true eases the body into the pose over a moment, false puts it there at once.</param>
        public static bool PoseAsPusher(Player player, LandVehicle vehicle, bool glide = true)
        {
            if (player == null || vehicle == null) { Core.Log.Warning("[Push] PoseAsPusher without a player or vehicle - ignored."); return false; }
            if (For(vehicle)?.Push == null) { Core.Log.Warning("[Push] " + CodeOf(vehicle) + " has no push profile - " + SeatRig.Who(player) + " is not posed."); return false; }
            return Occupants.Seat(player, vehicle, SeatRig.PushSeat, glide, "pushing") != null;
        }

        /// <summary>Lets go: the body of a player posed with <see cref="PoseAsPusher"/> moves on its own again.</summary>
        public static void ReleasePusher(Player player)
        {
            var rig = Occupants.Of(player);
            if (rig == null || !rig.IsPusher) return;
            Occupants.Leave(player, "stopped pushing");
        }

        /// <summary>The vehicle a player is posed behind, or null.</summary>
        public static LandVehicle PushedBy(Player player)
        {
            var rig = Occupants.Of(player);
            return rig != null && rig.IsPusher && rig.Vehicle != null ? rig.Vehicle.GetComponent<LandVehicle>() : null;
        }

        /// <summary>
        /// The local driver's own throttle this physics step, before mods add to it in their own UpdateThrottle
        /// postfix. Chassis records it first; a pusher mod adds the pusher's input after that, so a seated body's hands
        /// follow only the driver's keys.
        /// </summary>
        public static float LocalDriverThrottle => Occupants.LocalDrive;
    }
}
