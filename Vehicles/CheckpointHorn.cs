using System;
using System.Collections.Generic;
using System.Globalization;
using Chassis.Net;
using UnityEngine;

namespace Chassis.Vehicles
{
    /// <summary>
    /// Honking at a police checkpoint calls the officer to the driver window. Vanilla only starts the checkpoint talk
    /// on foot: the player walks up and presses the interact key, the officer asks, and with the last driven car in a
    /// search area he searches the car and opens the gate. In a car the interact key leaves the seat, so the driver
    /// could not ask without getting out. Now the horn sends the nearest free officer to the driver window; once he
    /// stands there, the vanilla talk starts and the rest runs as in vanilla.
    /// NPC movement belongs to the host, so the walk goes there as <c>req.cpwalk</c>; the driver's own peer watches
    /// the officer arrive and opens the talk, because the dialogue is local to the player who honked.
    /// </summary>
    internal static class CheckpointHorn
    {
        private const float WalkTimeout = 25f;      // seconds before the talk starts wherever the officer stands
        private const float ArriveDistance = 1.6f;  // flat metres from the window point that count as there
        private const float WindowGap = 0.7f;       // metres outside the body side where the officer stands
        private const float HostHold = 5f;          // seconds the host keeps him at the window after he arrived

        // The local driver's wait for the officer.
        private static GamePoliceOfficer _officer;
        private static LandVehicle _vehicle;
        private static GameRoadCheckpoint _checkpoint;
        private static Vector3 _window;
        private static float _since;

        private sealed class Walk
        {
            internal GamePoliceOfficer Officer;
            internal Vector3 Window;
            internal Vector3 Face;
            internal float Started;
            internal float Arrived = -1f;
            internal float LastOrder = -10f;
        }

        // Host: officers on their way to a driver window, by officer net id.
        private static readonly Dictionary<int, Walk> Walks = new Dictionary<int, Walk>();

        /// <summary>The local driver honked in <paramref name="lv"/>.</summary>
        internal static void Honked(LandVehicle lv)
        {
            try
            {
                if (lv == null) { Core.Log.Msg("[Checkpoint] honk without a vehicle - no checkpoint talk."); return; }
                var lp = Player.Local;
                if (lp == null) { Core.Log.Msg("[Checkpoint] honk before the local player exists - no checkpoint talk."); return; }

                GameRoadCheckpoint checkpoint = null;
                foreach (var cp in UnityEngine.Object.FindObjectsOfType<GameRoadCheckpoint>())
                {
                    if (cp == null || cp.ActivationState != GameRoadCheckpoint.ECheckpointState.Enabled) continue;
                    if (Holds(cp.SearchArea1, lv) || Holds(cp.SearchArea2, lv)) { checkpoint = cp; break; }
                }
                if (checkpoint == null) return; // an ordinary honk away from any checkpoint

                if (_officer != null) { Core.Log.Msg("[Checkpoint] honk while officer " + _officer.name + " is already on his way - no second call."); return; }
                var officer = Officer(checkpoint, lv.transform.position, out string why);
                if (officer == null) { Core.Log.Msg("[Checkpoint] " + Registry.Name(lv) + " honked at checkpoint " + checkpoint.name + "#" + checkpoint.GetInstanceID() + ", but " + why + " - no talk."); return; }
                if (officer.DialogueHandler == null) { Core.Log.Warning("[Checkpoint] officer " + officer.name + " has no dialogue handler - no talk."); return; }
                if (lp.LastDrivenVehicle == null || lp.LastDrivenVehicle != lv)
                    Core.Log.Warning("[Checkpoint] the game's last driven vehicle of the local player is not " + Registry.Name(lv) + " - the officer will not find the car to search.");

                int officerId = Ids.Of(officer);
                if (officerId < 0) { Core.Log.Warning("[Checkpoint] officer " + officer.name + " has no network id - no talk."); return; }
                var window = Reachable(officer, Window(lv, out Vector3 face));
                _officer = officer;
                _vehicle = lv;
                _checkpoint = checkpoint;
                _window = window;
                _since = Time.time;
                ChassisNet.ToHost("req.cpwalk", officerId.ToString(CultureInfo.InvariantCulture),
                    F(window.x), F(window.y), F(window.z), F(face.x), F(face.y), F(face.z));
                Core.Log.Msg("[Checkpoint] " + Registry.Name(lv) + " honked in a search area - officer " + officer.name + " walks to the driver window.");
            }
            catch (Exception e)
            {
                Core.Log.Error("[Checkpoint] calling the officer from the horn failed: " + e);
            }
        }

        /// <summary>Every frame: the driver's wait for the officer, and on the host the officers' walks.</summary>
        internal static void Tick()
        {
            if (_officer != null) TickWait();
            if (Walks.Count > 0) TickWalks();
        }

        private static void TickWait()
        {
            var lp = Player.Local;
            var seated = lp != null && lp.CurrentVehicle != null ? lp.CurrentVehicle.GetComponent<LandVehicle>() : null;
            if (_vehicle == null || seated != _vehicle) { Cancel("the driver left the car"); return; }
            if (_checkpoint == null || !(Holds(_checkpoint.SearchArea1, _vehicle) || Holds(_checkpoint.SearchArea2, _vehicle))) { Cancel("the car left the search area"); return; }
            if (!_officer.IsConscious) { Cancel("the officer is not conscious"); return; }

            var d = _officer.transform.position - _window;
            d.y = 0f;
            bool there = d.magnitude <= ArriveDistance;
            bool late = Time.time - _since > WalkTimeout;
            if (!there && !late) return;

            var officer = _officer;
            _officer = null;
            if (there)
                Core.Log.Msg("[Checkpoint] officer " + officer.name + " is at the driver window of " + Registry.Name(_vehicle) + " - he asks.");
            else
                Core.Log.Warning("[Checkpoint] officer " + officer.name + " did not reach the driver window in " + WalkTimeout + " s (" + d.magnitude.ToString("F1", CultureInfo.InvariantCulture) + " m off) - he asks from where he stands.");
            try { officer.DialogueHandler.Interacted(); }
            catch (Exception e) { Core.Log.Error("[Checkpoint] starting the checkpoint talk failed: " + e); }
        }

        private static void Cancel(string why)
        {
            Core.Log.Msg("[Checkpoint] officer " + (_officer != null ? _officer.name : "?") + " is no longer awaited: " + why + ".");
            _officer = null;
        }

        /// <summary>Host: send the officer with this net id to a driver window.</summary>
        internal static void HostWalk(int officerId, Vector3 window, Vector3 face)
        {
            try
            {
                var officer = Ids.Find<GamePoliceOfficer>(officerId);
                if (officer == null) { Core.Log.Warning("[Checkpoint] host: no officer with net " + officerId + " - walk dropped."); return; }
                window = Reachable(officer, window);
                Walks[officerId] = new Walk { Officer = officer, Window = window, Face = face, Started = Time.time };
                Core.Log.Msg("[Checkpoint] host: officer " + officer.name + " walks to " + window.ToString("F1") + ".");
            }
            catch (Exception e)
            {
                Core.Log.Error("[Checkpoint] host: sending officer net " + officerId + " to the window failed: " + e);
            }
        }

        // The checkpoint behaviour sends an idle officer back to his post; while a walk runs the host orders him to
        // the window again, twice a second at most, until he has stood there for HostHold seconds.
        private static void TickWalks()
        {
            List<int> done = null;
            foreach (var kv in Walks)
            {
                var w = kv.Value;
                string end = null;
                if (w.Officer == null || !w.Officer.IsConscious) end = "the officer is gone or down";
                else if (w.Officer.CheckpointBehaviour != null && w.Officer.CheckpointBehaviour.IsSearching) end = "the search started";
                else if (w.Arrived >= 0f && Time.time - w.Arrived > HostHold) end = "he stood at the window";
                else if (w.Arrived < 0f && Time.time - w.Started > WalkTimeout) end = "he did not get there in time";
                if (end != null)
                {
                    Core.Log.Msg("[Checkpoint] host: walk of officer net " + kv.Key + " ends - " + end + ".");
                    (done ??= new List<int>()).Add(kv.Key);
                    continue;
                }

                var mv = w.Officer.Movement;
                var d = w.Officer.transform.position - w.Window;
                d.y = 0f;
                if (d.magnitude <= ArriveDistance && !mv.IsMoving)
                {
                    if (w.Arrived < 0f)
                    {
                        w.Arrived = Time.time;
                        mv.FacePoint(w.Face, 0.5f);
                        Core.Log.Msg("[Checkpoint] host: officer " + w.Officer.name + " reached the window.");
                    }
                    continue;
                }
                if (Time.time - w.LastOrder < 0.5f) continue;
                if (d.magnitude > ArriveDistance && mv.CanMove)
                {
                    mv.SetDestination(w.Window);
                    w.LastOrder = Time.time;
                }
            }
            if (done != null) foreach (var id in done) Walks.Remove(id);
        }

        /// <summary>The point beside the driver door where the officer stands, and the point he faces.</summary>
        private static Vector3 Window(LandVehicle lv, out Vector3 face) => Window(lv, null, out face);

        /// <summary>The point beside the window of <paramref name="seat"/> (the driver seat when null) where an officer
        /// stands, and the point he faces.</summary>
        internal static Vector3 Window(LandVehicle lv, Transform seat, out Vector3 face)
        {
            var t = lv.transform;
            if (seat == null && lv.Seats != null)
                foreach (var s in lv.Seats)
                    if (s != null && s.isDriverSeat) { seat = s.transform; break; }
            var local = seat != null ? t.InverseTransformPoint(seat.position) : new Vector3(-0.4f, 0f, 0f);
            if (seat == null) Core.Log.Warning("[Checkpoint] " + Registry.Name(lv) + " has no driver seat - the officer goes to the left side.");
            float side = local.x > 0.05f ? 1f : -1f;
            float half = lv.BoundingBoxDimensions.x * 0.5f;
            face = seat != null ? seat.position : t.position;
            var p = t.TransformPoint(new Vector3(side * (half + WindowGap), 0f, local.z));
            p.y = t.position.y;
            return p;
        }

        /// <summary>
        /// The point of the officer's NavMesh closest to <paramref name="window"/>. Beside a car on a kerb, a barrier
        /// or a fence the window point itself can be off the NavMesh; a destination there fails, the checkpoint sends
        /// the officer back to his post and the next order sends him out again, so he walked back and forth until the
        /// walk timed out.
        /// </summary>
        private static Vector3 Reachable(GamePoliceOfficer officer, Vector3 window) => Reachable(officer, window, "[Checkpoint]");

        internal static Vector3 Reachable(NPC officer, Vector3 window, string tag)
        {
            try
            {
                var mv = officer.Movement;
                if (mv != null && mv.GetClosestReachablePoint(window, out Vector3 point))
                {
                    var d = point - window;
                    d.y = 0f;
                    if (d.magnitude > 0.3f)
                        Core.Log.Msg(tag + " the window point is off the officer's NavMesh - he walks to the closest reachable point " + d.magnitude.ToString("F1", CultureInfo.InvariantCulture) + " m away.");
                    return point;
                }
                Core.Log.Msg(tag + " no reachable point near the window for officer " + officer.name + " on this peer - he walks to the window point itself.");
            }
            catch (Exception e)
            {
                Core.Log.Warning(tag + " finding the reachable point near the window failed, he walks to the window point itself: " + e.Message);
            }
            return window;
        }

        private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

        private static bool Holds(GameVehicleDetector area, LandVehicle lv)
        {
            if (area == null || area.vehicles == null) return false;
            foreach (var v in area.vehicles)
                if (v != null && v == lv) return true;
            return false;
        }

        /// <summary>
        /// The nearest officer of the checkpoint who stands at his post and is free to talk. Officers are found by
        /// their checkpoint behaviour: on a client the checkpoint's own list of assigned NPCs stays empty.
        /// </summary>
        private static GamePoliceOfficer Officer(GameRoadCheckpoint cp, Vector3 at, out string why)
        {
            why = "no officer is assigned";
            GamePoliceOfficer best = null;
            float bestDistance = float.MaxValue;
            foreach (var officer in UnityEngine.Object.FindObjectsOfType<GamePoliceOfficer>())
            {
                var cb = officer != null ? officer.CheckpointBehaviour : null;
                if (cb == null || !cb.Enabled || cb.Checkpoint == null || cb.Checkpoint != cp) continue;
                if (!officer.IsConscious) { why = "the officer is not conscious"; continue; }
                if (!cb.Active) { why = "the officer is not at his post"; continue; }
                if (cb.IsSearching) { why = "the officer is already searching a car"; continue; }
                float d = Vector3.Distance(officer.transform.position, at);
                if (d < bestDistance) { best = officer; bestDistance = d; }
            }
            return best;
        }
    }
}
