using System;
using HarmonyLib;

namespace Chassis.Rideables
{
    /// <summary>
    /// Host side: puts a wheelchair vehicle into the world and takes it out again. Spawned through FishNet directly,
    /// not through VehicleManager, so it never enters the save.
    /// </summary>
    internal static class WheelchairSpawn
    {
        internal static LandVehicle Spawn(Vector3 position, Quaternion rotation, string why)
        {
            if (!InstanceFinder.IsServer) { Core.Log.Warning("[Wheelchair] spawn asked on a client (" + why + ") - only the host spawns."); return null; }
            var prefab = WheelchairType.Prefab;
            if (prefab == null) { Core.Log.Error("[Wheelchair] spawn failed (" + why + "): the hull was never built."); return null; }

            var go = UnityEngine.Object.Instantiate(prefab.gameObject, position, rotation);
            go.name = "ChassisWheelchair";
            go.SetActive(true);
            InstanceFinder.ServerManager.Spawn(go, null);
            var lv = go.GetComponent<LandVehicle>();
            Core.Log.Msg("[Wheelchair] spawned at " + position.ToString("F2") + " yaw " + rotation.eulerAngles.y.ToString("F0") + " as net " + Net.Ids.Of(lv) + " (" + why + ").");
            return lv;
        }

        internal static void Despawn(LandVehicle lv, string why)
        {
            if (lv == null) return;
            if (!InstanceFinder.IsServer) { Core.Log.Warning("[Wheelchair] despawn asked on a client (" + why + ")."); return; }
            int id = Net.Ids.Of(lv);
            Compat.Interop.Despawn(lv.gameObject);
            Core.Log.Msg("[Wheelchair] despawned net " + id + " (" + why + ").");
        }

        /// <summary>Every peer builds the look of its own copy when the vehicle starts.</summary>
        [HarmonyPatch(typeof(LandVehicle), "Start")]
        private static class BuildLookOnStart
        {
            private static void Postfix(LandVehicle __instance)
            {
                if (!WheelchairType.Is(__instance)) return;
                try { WheelchairModel.Build(__instance); }
                catch (Exception e) { Core.Log.Error("[Wheelchair] building the look failed: " + e); }
                try { Push.AddHandles(__instance); }
                catch (Exception e) { Core.Log.Error("[Wheelchair] adding the push handles failed: " + e); }
            }
        }
    }
}
