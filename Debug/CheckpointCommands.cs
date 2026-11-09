#if DEBUG
using System;
using UnityEngine;

namespace Chassis.Debugging
{
    /// <summary>
    /// <c>chassis checkpoint ...</c>: a police checkpoint for the horn test. Turns one on, puts its officers at their
    /// posts, parks the car in its search area and reports gates, search areas and officers.
    /// </summary>
    internal static class CheckpointCommands
    {
        private static int _loc;

        internal static void Help()
        {
            Say("checkpoint on [location]     - enable a checkpoint for a game day (none = one that has officers)");
            Say("checkpoint warp              - its own officers to their stand points (they are sent from the station first)");
            Say("checkpoint park              - the car you sit in into search area 1, facing the gate");
            Say("checkpoint state             - gates, search areas, officers");
            Say("checkpoint choose <n>        - pick answer n (1 = first) in the open officer talk");
        }

        private static GameRoadCheckpoint Cp() =>
            NetworkSingleton<GameCheckpointManager>.Instance.GetCheckpoint((GameCheckpointManager.ECheckpointLocation)_loc);

        internal static void Run(string[] p)
        {
            string op = p.Length > 2 ? p[2].ToLower() : "state";
            if (!NetworkSingleton<GameCheckpointManager>.InstanceExists) { Say("checkpoint: no checkpoint manager in this scene"); return; }
            switch (op)
            {
                case "help":
                    Help();
                    break;
                case "on":
                {
                    if (p.Length > 3) { if (!int.TryParse(p[3], out _loc)) { Say("checkpoint on: location must be a number"); return; } }
                    else _loc = Staffed();
                    var cp = Cp();
                    if (cp == null) { Say("checkpoint on: no checkpoint at location " + _loc); return; }
                    if (cp.ActivationState != GameRoadCheckpoint.ECheckpointState.Enabled)
                        NetworkSingleton<GameCheckpointManager>.Instance.SetCheckpointEnabled((GameCheckpointManager.ECheckpointLocation)_loc, true, 2);
                    Say("checkpoint on: " + (GameCheckpointManager.ECheckpointLocation)_loc + " at " + cp.transform.position.ToString("F1"));
                    break;
                }
                case "warp":
                {
                    int n = 0;
                    var here = Cp();
                    foreach (var o in UnityEngine.Object.FindObjectsOfType<GamePoliceOfficer>())
                    {
                        var cb = o.CheckpointBehaviour;
                        if (cb == null || !cb.Enabled || cb.Checkpoint == null || cb.Checkpoint != here || cb.standPoint == null) continue;
                        o.Movement.Warp(cb.standPoint.position);
                        n++;
                    }
                    Say("checkpoint warp: " + n + " officer(s) at their posts");
                    break;
                }
                case "park":
                {
                    var lp = Player.Local;
                    var lv = lp != null && lp.CurrentVehicle != null ? lp.CurrentVehicle.GetComponent<LandVehicle>() : null;
                    if (lv == null) { Say("checkpoint park: sit in a car first"); return; }
                    var cp = Cp();
                    var a = cp.SearchArea1.transform.position;
                    var dir = cp.transform.position - a;
                    dir.y = 0f;
                    lv.SetTransform(a + Vector3.up * 0.5f, Quaternion.LookRotation(dir.normalized, Vector3.up));
                    Say("checkpoint park: " + Chassis.Vehicles.Registry.Name(lv) + " in search area 1");
                    break;
                }
                case "choose":
                {
                    if (p.Length < 4 || !int.TryParse(p[3], out int n) || n < 1) { Say("checkpoint choose: give the answer number, 1 = first"); return; }
                    foreach (var o in UnityEngine.Object.FindObjectsOfType<GamePoliceOfficer>())
                    {
                        var dh = o != null ? o.DialogueHandler : null;
                        if (dh == null || !dh.IsDialogueInProgress) continue;
                        dh.ChoiceSelected(n - 1);
                        Say("checkpoint choose: answer " + n + " in the talk of " + o.name);
                        return;
                    }
                    Say("checkpoint choose: no officer talk is open");
                    break;
                }
                default:
                    State();
                    break;
            }
        }

        /// <summary>
        /// The first location whose checkpoint the game runs right now with an officer of its own. Enabling a
        /// checkpoint pulls its officers from the police station pool, and an empty pool sends none, so a staffed one
        /// is the reliable pick.
        /// </summary>
        private static int Staffed()
        {
            var mgr = NetworkSingleton<GameCheckpointManager>.Instance;
            var officers = UnityEngine.Object.FindObjectsOfType<GamePoliceOfficer>();
            for (int loc = 0; loc < 4; loc++)
            {
                var cp = mgr.GetCheckpoint((GameCheckpointManager.ECheckpointLocation)loc);
                // The game turns its checkpoints on and off by the time of day; only one it runs right now keeps its officers.
                if (cp == null || cp.ActivationState != GameRoadCheckpoint.ECheckpointState.Enabled) continue;
                foreach (var o in officers)
                {
                    var cb = o != null ? o.CheckpointBehaviour : null;
                    if (cb != null && cb.Enabled && cb.Checkpoint == cp)
                    {
                        Say("checkpoint on: location " + loc + " already has officer " + o.name);
                        return loc;
                    }
                }
            }
            Say("checkpoint on: no checkpoint has officers yet - location 0, staffed from the station pool");
            return 0;
        }

        private static string List(GameVehicleDetector d)
        {
            if (d == null) return "none";
            string s = "";
            foreach (var v in d.vehicles) s += (s.Length > 0 ? "," : "") + (v != null ? Chassis.Vehicles.Registry.Name(v) : "null");
            return "[" + s + "]";
        }

        private static void State()
        {
            var cp = Cp();
            if (cp == null) { Say("checkpoint: no checkpoint at location " + _loc); return; }
            Say("checkpoint: " + cp.ActivationState + " gate1 " + cp.Gate1Open + " gate2 " + cp.Gate2Open + " area1 " + List(cp.SearchArea1) + " area2 " + List(cp.SearchArea2));
            foreach (var o in UnityEngine.Object.FindObjectsOfType<GamePoliceOfficer>())
            {
                var cb = o.CheckpointBehaviour;
                if (cb == null || !cb.Enabled) continue;
                Say("checkpoint officer " + o.name + " of " + (cb.Checkpoint != null ? cb.Checkpoint.name + "#" + cb.Checkpoint.GetInstanceID() : "none") + " (this " + cp.name + "#" + cp.GetInstanceID() + "): at post " + cb.Active + ", searching " + cb.IsSearching
                    + ", car " + (cb.CurrentSearchedVehicle != null ? Chassis.Vehicles.Registry.Name(cb.CurrentSearchedVehicle) : "none"));
            }
        }

        private static void Say(string line) => DebugConsolePatch.Say(line);
    }
}
#endif
