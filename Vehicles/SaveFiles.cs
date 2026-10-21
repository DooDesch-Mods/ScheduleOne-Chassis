using System;
using Chassis.Instruments;
using Chassis.Net;
using HarmonyLib;

namespace Chassis.Vehicles
{
    /// <summary>
    /// Keeps Chassis' files in the save folder. At the end of a save, vanilla deletes every file in the folder that no
    /// saveable claimed (<c>ClearBaseLevelOutdatedSaves</c>), which took <c>Chassis.vehicles</c> and
    /// <c>Chassis.instruments</c> with it (and now <c>Chassis.tuning</c>). The prefix reads both files before that can happen, the postfix claims
    /// their names and writes them again.
    /// </summary>
    [HarmonyPatch(typeof(GameSaveManager), nameof(GameSaveManager.Save), new Type[] { typeof(string) })]
    internal static class SaveFiles
    {
        private static void Prefix()
        {
            if (!ChassisNet.IsHost) return;
            try { Fleet.EnsureLoaded(); Store.EnsureLoaded(); Tune.State.EnsureLoaded(); }
            catch (Exception e) { Core.Log.Error("[Save] reading Chassis' files before the save failed: " + e); }
        }

        private static void Postfix(GameSaveManager __instance)
        {
            if (!ChassisNet.IsHost) return;
            try
            {
                var approved = __instance.ApprovedBaseLevelPaths;
                if (approved == null) Core.Log.Warning("[Save] the game has no list of kept files - Chassis' files may be deleted at the end of the save.");
                else
                {
                    if (!approved.Contains(Fleet.FileName)) approved.Add(Fleet.FileName);
                    if (!approved.Contains(Store.FileName)) approved.Add(Store.FileName);
                    if (!approved.Contains(Tune.State.FileName)) approved.Add(Tune.State.FileName);
                }
            }
            catch (Exception e) { Core.Log.Error("[Save] could not mark Chassis' files as kept: " + e); }
            try { Fleet.SaveWithGame(); }
            catch (Exception e) { Core.Log.Error("[Save] saving the owner records failed: " + e); }
            try { Store.Save(); }
            catch (Exception e) { Core.Log.Error("[Save] saving the cockpits failed: " + e); }
            try { Tune.State.Save(); }
            catch (Exception e) { Core.Log.Error("[Save] saving the tuning failed: " + e); }
        }
    }
}
