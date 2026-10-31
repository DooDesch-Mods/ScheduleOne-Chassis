using System;
using HarmonyLib;

namespace Chassis.Vehicles
{
    /// <summary>
    /// The vanilla speed HUD on a co-op client. <c>VehicleCanvas.Start</c> waits for <c>Player.onLocalPlayerSpawned</c>
    /// to subscribe to the local player's enter and exit events, and a joining client has spawned its player before that
    /// Start runs. The event never comes, so the speed stays at the prefab's "0km/h" and nothing turns the canvas off
    /// after the player gets out: <c>Update</c> only ever enables it. <c>VehicleCamera.Start</c> checks for this case;
    /// this postfix adds the same check for the canvas. On the host the player spawns after Start, so the vanilla event
    /// does the subscribing and the postfix leaves it alone.
    /// </summary>
    [HarmonyPatch(typeof(GameVehicleCanvas), "Start")]
    internal static class SpeedHud
    {
        private static void Postfix(GameVehicleCanvas __instance)
        {
            if (Player.Local == null)
            {
                Core.Log.Msg("[SpeedHud] local player not spawned yet - the game subscribes the speed HUD itself.");
                return;
            }
            try
            {
                var subscribe = AccessTools.Method(typeof(GameVehicleCanvas), "Subscribe");
                if (subscribe == null)
                {
                    Core.Log.Error("[SpeedHud] VehicleCanvas.Subscribe not found - the speed HUD stays on after leaving a vehicle.");
                    return;
                }
                subscribe.Invoke(__instance, null);
                Core.Log.Msg("[SpeedHud] local player spawned before VehicleCanvas.Start - speed HUD subscribed.");
            }
            catch (Exception e)
            {
                Core.Log.Error("[SpeedHud] subscribing the speed HUD failed - it stays on after leaving a vehicle: " + e);
            }
        }
    }
}
