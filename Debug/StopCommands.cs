#if DEBUG
using System.Globalization;
using UnityEngine;
using Chassis.Vehicles;

namespace Chassis.Debugging
{
    /// <summary>
    /// <c>chassis stop ...</c>: starts and reports a police traffic stop on the local player, so the test does not
    /// wait for the random body search of an officer nearby.
    /// </summary>
    internal static class StopCommands
    {
        internal static void Help()
        {
            Say("stop start      - host: the nearest officer on foot starts a body search on you (a traffic stop when seated)");
            Say("stop state      - stop phase, body search, search screen and pursuit level");
            Say("stop vanilla on|off - on: the game's own body search in cars, for the before/after comparison");
        }

        internal static void Run(string[] p)
        {
            string op = p.Length > 2 ? p[2].ToLower() : "state";
            switch (op)
            {
                case "help":
                    Help();
                    break;
                case "vanilla":
                    TrafficStop.Enabled = !(p.Length > 3 && p[3] == "on");
                    Say("stop vanilla: traffic stop " + (TrafficStop.Enabled ? "on" : "off - the game's own body search runs in cars"));
                    break;
                case "start":
                {
                    if (!InstanceFinder.IsServer) { Say("stop start: host only"); return; }
                    var lp = Player.Local;
                    if (lp == null) { Say("stop start: no player"); return; }
                    GamePoliceOfficer best = null; float bd = float.MaxValue;
                    foreach (var o in UnityEngine.Object.FindObjectsOfType<GamePoliceOfficer>())
                    {
                        if (o == null || o.IsInVehicle || !o.IsConscious || !o.gameObject.activeInHierarchy) continue;
                        float d = Vector3.Distance(o.transform.position, lp.transform.position);
                        if (d < bd) { bd = d; best = o; }
                    }
                    if (best == null) { Say("stop start: no officer on foot"); return; }
                    best.ConductBodySearch(lp);
                    Say("stop start: officer " + best.name + " at " + bd.ToString("F1", CultureInfo.InvariantCulture) + " m searches " + lp.PlayerName + " (seated " + (lp.CurrentVehicle != null) + ")");
                    break;
                }
                default:
                    Say("stop state: " + State());
                    break;
            }
        }

        private static string State()
        {
            var lp = Player.Local;
            string pursuit = lp != null && lp.CrimeData != null ? lp.CrimeData.CurrentPursuitLevel.ToString() : "-";
            bool screen = Singleton<GameBodySearchScreen>.InstanceExists && Singleton<GameBodySearchScreen>.Instance.IsOpen;
            int searching = 0;
            foreach (var o in UnityEngine.Object.FindObjectsOfType<GamePoliceOfficer>())
                if (o != null && o.BodySearchBehaviour != null && o.BodySearchBehaviour.Active) searching++;
            return TrafficStop.Describe() + " searches=" + searching + " screen=" + screen + " pursuit=" + pursuit
                + " seated=" + (lp != null && lp.CurrentVehicle != null);
        }

        private static void Say(string line) => DebugConsolePatch.Say(line);
    }
}
#endif
