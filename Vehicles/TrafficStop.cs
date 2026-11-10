using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using Chassis.Net;
using UnityEngine;

namespace Chassis.Vehicles
{
    /// <summary>
    /// A police body search of a player who sits in a vehicle becomes a traffic stop. Vanilla starts the random body
    /// search on players in cars too (<c>PoliceOfficer.CheckNewInvestigation</c> has no vehicle test, and a car in sight
    /// counts as full exposure), and then escalates at once because the target sits in a vehicle
    /// (<c>BodySearchBehaviour.UpdateEscalation</c>), or because the officer cannot reach a point inside the car
    /// (<c>UpdateMovement</c>). The player got "Failure to comply" and a chase without a chance to react.
    /// Now the officer calls "Pull over!" and the driver has <see cref="PullOverTime"/> seconds to stop. Once the car
    /// stands, the officer walks to the window of the player's seat and the vanilla search screen opens from there.
    /// Not stopping in time, or driving off once stopped, escalates as vanilla does.
    /// The prefix replaces the behaviour's update while the target is seated: movement and escalation on the host,
    /// the countdown bubble and the vanilla search screen on every peer. A target who gets out goes back to vanilla.
    /// </summary>
    [HarmonyPatch]
    internal static class TrafficStop
    {
        internal const float PullOverTime = 10f;     // seconds to bring the car to a stop
        private const float StoppedSpeed = 3f;       // km/h below which the car counts as standing
        private const float StoppedHold = 1f;        // seconds the car has to stand before the officer walks up
        private const float DriveOffDistance = 5f;   // metres the car may roll once it stood
        private const float FleeDistance = 60f;      // metres between officer and car that end the stop at once
        private const float WalkTimeout = 25f;       // seconds the officer has to reach the window
        private const float ArriveDistance = 0.4f;   // flat metres from the window point that count as there; the game opens the search screen only within 2 m of the player

        internal enum Phase { PullOver, Approach, AtWindow }

        internal sealed class Stop
        {
            internal Phase Phase;
            internal float Started;
            internal float PhaseStarted;
            internal Vector3 LastCarPos;
            internal float LastSample = -1f;
            internal float SpeedKmh;
            internal float StillSince = -1f;
            internal Vector3 StoppedAt;
            internal float LastOrder = -10f;
            internal int ShownSecond = -1;
            internal string Car;
            internal bool HasWindow;
            internal Vector3 RawWindow;
            internal Vector3 Window;
        }

        // Per officer net id, on every peer that updates the behaviour.
        internal static readonly Dictionary<int, Stop> Stops = new Dictionary<int, Stop>();

        // Debug console only: false lets the vanilla body search run in cars, to compare against the old behaviour.
        internal static bool Enabled = true;

        // Set while BodySearchScreen.Open runs: its GameInput.ExitAll pops the in-game state down to the default,
        // which leaves the vehicle, so the search from the seat would put the player on the road first.
        private static bool _screenOpening;

        [HarmonyPatch(typeof(GameBodySearchScreen), nameof(GameBodySearchScreen.Open))]
        [HarmonyPrefix]
        private static void ScreenOpening() => _screenOpening = true;

        [HarmonyPatch(typeof(GameBodySearchScreen), nameof(GameBodySearchScreen.Open))]
        [HarmonyPostfix]
        private static void ScreenOpened() => _screenOpening = false;

        [HarmonyPatch(typeof(GameInput), nameof(GameInput.ExitAll))]
        [HarmonyPrefix]
        private static bool ExitAll()
        {
            if (!_screenOpening) return true;
            _screenOpening = false;
            try
            {
                var lp = Player.Local;
                if (lp == null || lp.CurrentVehicle == null || !Enabled) return true;
                Core.Log.Msg("[Stop] the search screen opens while " + lp.PlayerName + " sits in " + lp.CurrentVehicle.name + " - the player stays seated.");
                return false;
            }
            catch (Exception e)
            {
                Core.Log.Error("[Stop] keeping the player seated for the search screen failed - the game's exit runs: " + e);
                return true;
            }
        }

        [HarmonyPatch(typeof(GameBodySearch), nameof(GameBodySearch.Activate))]
        [HarmonyPostfix]
        private static void Activated(GameBodySearch __instance)
        {
            try
            {
                int id = Ids.Of(__instance.Npc);
                if (Stops.Remove(id)) Core.Log.Msg("[Stop] officer " + __instance.Npc.name + " starts a new body search - the old stop state is dropped.");
            }
            catch (Exception e)
            {
                Core.Log.Error("[Stop] clearing the stop state on a new body search failed: " + e);
            }
        }

        [HarmonyPatch(typeof(GameBodySearch), nameof(GameBodySearch.Deactivate))]
        [HarmonyPostfix]
        private static void Deactivated(GameBodySearch __instance)
        {
            try
            {
                if (Stops.Remove(Ids.Of(__instance.Npc))) Core.Log.Msg("[Stop] the body search of officer " + __instance.Npc.name + " ended - the traffic stop is over.");
            }
            catch (Exception e)
            {
                Core.Log.Error("[Stop] clearing the stop state at the end of a body search failed: " + e);
            }
        }

        [HarmonyPatch(typeof(GameBodySearch), nameof(GameBodySearch.BehaviourUpdate))]
        [HarmonyPrefix]
        private static bool Update(GameBodySearch __instance)
        {
            try
            {
                var target = __instance.TargetPlayer;
                var npc = __instance.Npc;
                if (target == null || npc == null || !Enabled) return true;
                int id = Ids.Of(npc);
                var lv = target.CurrentVehicle != null ? target.CurrentVehicle.GetComponent<LandVehicle>() : null;
                if (lv == null)
                {
                    if (Stops.Remove(id))
                    {
                        Core.Log.Msg("[Stop] " + target.PlayerName + " got out during the stop by " + npc.name + " - the vanilla body search goes on.");
                        ResetSearchTime(__instance);
                    }
                    return true;
                }
                if (!Stops.TryGetValue(id, out var stop))
                {
                    stop = new Stop { Phase = Phase.PullOver, Started = Time.time, PhaseStarted = Time.time, Car = Registry.Name(lv) };
                    Stops[id] = stop;
                    Core.Log.Msg("[Stop] officer " + npc.name + " pulls over " + target.PlayerName + " in " + stop.Car + " - " + PullOverTime.ToString("F0", CultureInfo.InvariantCulture) + " s to stop.");
                    if (target.IsOwner && PlayerSingleton<PlayerCamera>.InstanceExists) PlayerSingleton<PlayerCamera>.Instance.StopFocus();
                }
                Sample(stop, lv);
                Run(__instance, npc, target, lv, stop);
            }
            catch (Exception e)
            {
                Core.Log.Error("[Stop] the traffic stop update failed - vanilla runs this frame: " + e);
                return true;
            }
            return false;
        }

        private static void Run(GameBodySearch beh, NPC npc, Player target, LandVehicle lv, Stop stop)
        {
            bool host = InstanceFinder.IsServer;
            float now = Time.time;
            float apart = Vector3.Distance(npc.transform.position, lv.transform.position);

            if (host && apart > FleeDistance) { End(beh, stop, "the car got " + apart.ToString("F0", CultureInfo.InvariantCulture) + " m away from the officer"); return; }

            switch (stop.Phase)
            {
                case Phase.PullOver:
                {
                    int left = Mathf.CeilToInt(PullOverTime - (now - stop.Started));
                    if (left != stop.ShownSecond && left > 0)
                    {
                        stop.ShownSecond = left;
                        npc.DialogueHandler.ShowWorldspaceDialogue("Pull over! " + left, 1.2f);
                    }
                    if (host) Walk(npc, lv, target, stop, now);
                    if (stop.StillSince >= 0f && now - stop.StillSince >= StoppedHold)
                    {
                        stop.Phase = Phase.Approach;
                        stop.PhaseStarted = now;
                        stop.StoppedAt = lv.transform.position;
                        npc.DialogueHandler.ShowWorldspaceDialogue("Stay in the car.", 3f);
                        Core.Log.Msg("[Stop] " + stop.Car + " stopped after " + (now - stop.Started).ToString("F1", CultureInfo.InvariantCulture) + " s - officer " + npc.name + " walks to the window.");
                    }
                    else if (host && now - stop.Started > PullOverTime)
                        End(beh, stop, "the car did not stop within " + PullOverTime.ToString("F0", CultureInfo.InvariantCulture) + " s (" + stop.SpeedKmh.ToString("F0", CultureInfo.InvariantCulture) + " km/h)");
                    break;
                }
                case Phase.Approach:
                case Phase.AtWindow:
                {
                    if (host && Vector3.Distance(lv.transform.position, stop.StoppedAt) > DriveOffDistance) { End(beh, stop, "the car drove off during the stop"); return; }
                    if (host) Walk(npc, lv, target, stop, now);
                    float reach = Vector3.Distance(npc.transform.position, target.Avatar.CenterPoint);
                    if (stop.Phase == Phase.Approach && reach < 2f)
                    {
                        stop.Phase = Phase.AtWindow;
                        Core.Log.Msg("[Stop] officer " + npc.name + " is at the window of " + stop.Car + " (" + reach.ToString("F1", CultureInfo.InvariantCulture) + " m, walked " + (now - stop.PhaseStarted).ToString("F1", CultureInfo.InvariantCulture) + " s) - the search starts.");
                    }
                    // Opens the vanilla search screen on the target's own peer once the officer is within 2 m.
                    SearchStep(beh);
                    if (host && stop.Phase == Phase.Approach && now - stop.PhaseStarted > WalkTimeout)
                    {
                        Core.Log.Warning("[Stop] officer " + npc.name + " did not reach the window of " + stop.Car + " in " + WalkTimeout.ToString("F0", CultureInfo.InvariantCulture) + " s (" + reach.ToString("F1", CultureInfo.InvariantCulture) + " m off) - the stop ends without a search.");
                        Stops.Remove(Ids.Of(npc));
                        beh.Disable_Networked(null);
                        beh.Deactivate_Networked(null);
                    }
                    break;
                }
            }
        }

        /// <summary>Host: the officer heads for the window of the target's seat, re-ordered twice a second.</summary>
        private static void Walk(NPC npc, LandVehicle lv, Player target, Stop stop, float now)
        {
            if (now - stop.LastOrder < 0.5f) return;
            stop.LastOrder = now;
            var mv = npc.Movement;
            if (mv == null || !mv.CanMove) return;
            var seat = target.CurrentVehicleSeat != null ? target.CurrentVehicleSeat.transform : null;
            var raw = CheckpointHorn.Window(lv, seat, out Vector3 face);
            // The NavMesh lookup runs again only once the window point moved, which keeps its log line to one per spot.
            if (!stop.HasWindow || (raw - stop.RawWindow).sqrMagnitude > 1f)
            {
                stop.RawWindow = raw;
                stop.Window = CheckpointHorn.Reachable(npc, raw, "[Stop]");
                stop.HasWindow = true;
            }
            var window = stop.Window;
            var d = npc.transform.position - window;
            d.y = 0f;
            if (d.magnitude <= ArriveDistance)
            {
                if (mv.IsMoving) mv.Stop();
                mv.FacePoint(face, 0.5f);
                return;
            }
            mv.SetDestination(window);
        }

        /// <summary>The car's speed from its own movement, so it reads the same on the host for a client's car.</summary>
        private static void Sample(Stop stop, LandVehicle lv)
        {
            float now = Time.time;
            var pos = lv.transform.position;
            if (stop.LastSample >= 0f && now - stop.LastSample >= 0.2f)
            {
                stop.SpeedKmh = Vector3.Distance(pos, stop.LastCarPos) / (now - stop.LastSample) * 3.6f;
                stop.LastSample = now;
                stop.LastCarPos = pos;
                if (stop.SpeedKmh < StoppedSpeed) { if (stop.StillSince < 0f) stop.StillSince = now; }
                else stop.StillSince = -1f;
            }
            else if (stop.LastSample < 0f)
            {
                stop.LastSample = now;
                stop.LastCarPos = pos;
            }
        }

        /// <summary>Host: the vanilla consequence of not complying - Failure to comply and a chase.</summary>
        private static void End(GameBodySearch beh, Stop stop, string why)
        {
            Core.Log.Msg("[Stop] traffic stop of " + stop.Car + " escalates: " + why + ".");
            Stops.Remove(Ids.Of(beh.Npc));
            beh.Escalate();
        }

#if IL2CPP
        private static void SearchStep(GameBodySearch beh) => beh.UpdateSearch();
        private static void ResetSearchTime(GameBodySearch beh) => beh.searchTime = 0f;
#else
        private static readonly System.Reflection.MethodInfo _updateSearch = AccessTools.Method(typeof(GameBodySearch), "UpdateSearch");
        private static readonly AccessTools.FieldRef<GameBodySearch, float> _searchTime = AccessTools.FieldRefAccess<GameBodySearch, float>("searchTime");
        private static void SearchStep(GameBodySearch beh) => _updateSearch.Invoke(beh, null);
        private static void ResetSearchTime(GameBodySearch beh) => _searchTime(beh) = 0f;
#endif

        internal static string Describe()
        {
            if (Stops.Count == 0) return "stop: none";
            var sb = new System.Text.StringBuilder();
            foreach (var kv in Stops)
                sb.Append("stop: officer net " + kv.Key + " " + kv.Value.Phase + " car " + kv.Value.Car + " since " + (Time.time - kv.Value.Started).ToString("F1", CultureInfo.InvariantCulture) + " s speed " + kv.Value.SpeedKmh.ToString("F0", CultureInfo.InvariantCulture) + " km/h; ");
            return sb.ToString();
        }
    }
}
