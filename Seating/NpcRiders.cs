using System;
using System.Collections.Generic;
using System.Reflection;
using Chassis.Api;
using Chassis.Vehicles;

namespace Chassis.Seating
{
    /// <summary>
    /// NPCs that drive or ride in a profiled vehicle sit visibly in their seat, police included. Vanilla parks an NPC
    /// at its seat transform and switches its avatar off (<c>NPC.EnterVehicle</c>, on every peer); each peer seats the
    /// NPCs it sees on its own, so nothing here is networked.
    ///
    /// Only NPCs near the camera get a rig: a seated body costs an IK solve each frame, and beyond that distance the
    /// tinted glass hides it anyway.
    /// </summary>
    internal static class NpcRiders
    {
        private const float SeatRange = 60f;
        private const float LeaveRange = 70f;
        private const float ScanSeconds = 0.5f;

        private static readonly Dictionary<int, SeatRig> _rigs = new Dictionary<int, SeatRig>();
        private static readonly HashSet<long> _failed = new HashSet<long>();
        private static readonly List<int> _drop = new List<int>();
        private static float _next;

        /// <summary>NPC drivers are shown; off until the base interior is approved, together with it.</summary>
        internal static bool Enabled => Interior.BaseInterior.Enabled;

        internal static IEnumerable<SeatRig> All => _rigs.Values;

        internal static void Tick()
        {
            if (_rigs.Count > 0) PoseAll();
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + ScanSeconds;
            Camera cam = Camera.main;
            Vector3 eye = cam != null ? cam.transform.position : Vector3.zero;
            Release(eye, cam != null);
            if (!Enabled || cam == null) return;

            foreach (var lv in Registry.Profiled)
            {
                if (lv == null || (lv.transform.position - eye).sqrMagnitude > SeatRange * SeatRange) continue;
                var occupants = lv.OccupantNPCs;
                if (occupants == null) continue;
                for (int i = 0; i < occupants.Length; i++)
                {
                    NPC npc = occupants[i];
                    if (npc == null || _rigs.ContainsKey(npc.GetInstanceID())) continue;
                    long key = ((long)npc.GetInstanceID() << 32) ^ (uint)lv.GetInstanceID();
                    if (_failed.Contains(key)) continue;
                    int seat = Mathf.Clamp(i, 0, Math.Max(0, (lv.Seats?.Length ?? 1) - 1));
                    SeatRig rig = null;
                    try { rig = SeatRig.CreateNpc(npc, lv, ChassisApi.CodeOf(lv), seat); }
                    catch (Exception e) { Core.Log.Error("[Seat] seating " + SeatRig.Who(npc) + " in " + Registry.Name(lv) + " failed: " + e); }
                    if (rig == null) { _failed.Add(key); continue; }
                    if (rig.Avatar.Animation != null) rig.Avatar.Animation.SetBool("Sitting", true);
                    _rigs[npc.GetInstanceID()] = rig;
                    RefreshUmbrella(npc, "seated");
                }
            }
        }

        /// <summary>Drops rigs of NPCs that got out, of vehicles that left the range and of bodies that are gone.</summary>
        private static void Release(Vector3 eye, bool haveEye)
        {
            if (_rigs.Count == 0) return;
            _drop.Clear();
            foreach (var kv in _rigs)
            {
                var rig = kv.Value;
                string why = null;
                if (!rig.Alive) why = "vehicle or body gone";
                else if (!Enabled) why = "npc riders off";
                else if (rig.Npc.CurrentVehicle == null || rig.Npc.CurrentVehicle.transform != rig.Vehicle) why = "left the vehicle";
                else if (ChassisApi.For(rig.Npc.CurrentVehicle) == null) why = "the vehicle has no profile";
                else if (haveEye && (rig.Vehicle.position - eye).sqrMagnitude > LeaveRange * LeaveRange) why = "out of range";
                if (why == null) continue;
                _drop.Add(kv.Key);
                try { rig.Detach(why); }
                catch (Exception e) { Core.Log.Error("[Seat] releasing " + rig.Name + " failed: " + e); }
            }
            foreach (int k in _drop) _rigs.Remove(k);
        }

        private static void PoseAll()
        {
            float dt = Time.deltaTime;
            _drop.Clear();
            foreach (var kv in _rigs)
            {
                try { kv.Value.Pose(dt); }
                catch (Exception e)
                {
                    Core.Log.Error("[Seat] posing " + kv.Value.Name + " failed, the body goes back to vanilla: " + e);
                    _drop.Add(kv.Key);
                }
            }
            foreach (int k in _drop)
            {
                var rig = _rigs[k];
                _rigs.Remove(k);
                var lv = rig.Vehicle != null ? rig.Vehicle.GetComponent<LandVehicle>() : null;
                _failed.Add(((long)k << 32) ^ (uint)(lv != null ? lv.GetInstanceID() : 0));
                try { rig.Detach("pose error"); }
                catch (Exception e) { Core.Log.Error("[Seat] releasing " + rig.Name + " after a pose error failed: " + e.Message); }
            }
        }

        internal static void Reset()
        {
            foreach (var rig in _rigs.Values)
            {
                try { rig.Detach("world reset"); }
                catch (Exception e) { Core.Log.Error("[Seat] reset of " + rig.Name + " failed: " + e.Message); }
            }
            _rigs.Clear();
            _failed.Clear();
            _next = 0f;
        }

        /// <summary>Releases one NPC's rig at once, so a body that got out is not held in the seat until the next scan.</summary>
        internal static void Drop(NPC npc, string why)
        {
            if (npc == null || !_rigs.TryGetValue(npc.GetInstanceID(), out var rig)) return;
            _rigs.Remove(npc.GetInstanceID());
            try { rig.Detach(why); }
            catch (Exception e) { Core.Log.Error("[Seat] releasing " + rig.Name + " failed: " + e); }
        }

        // ---- umbrella ----------------------------------------------------------------------------------------------

        /// <summary>True while Chassis shows this NPC in a seat.</summary>
        internal static bool IsSeated(NPC npc) => npc != null && _rigs.TryGetValue(npc.GetInstanceID(), out var rig) && rig.Alive;

        /// <summary>
        /// Vanilla <c>NPCActions.UpdateUmbrellaUse</c> runs every game minute and has no vehicle check, so it hands an
        /// umbrella to a seated NPC that is shown. A seated NPC holds none; vanilla's own rule gives it back on exit.
        /// </summary>
        internal static void ApplyUmbrellaPatch(HarmonyLib.Harmony h)
        {
            try
            {
                MethodInfo target = typeof(GameNpcActions).GetMethod("UpdateUmbrellaUse", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (target == null) { Core.Log.Error("[Seat] NPCActions.UpdateUmbrellaUse not found - seated NPCs keep their umbrella."); return; }
                h.Patch(target, prefix: new HarmonyLib.HarmonyMethod(typeof(NpcRiders).GetMethod(nameof(UmbrellaPrefix), BindingFlags.NonPublic | BindingFlags.Static)));
                Core.Log.Msg("[Seat] umbrella hook on " + target.DeclaringType.Name + "." + target.Name + ".");
            }
            catch (Exception e) { Core.Log.Error("[Seat] patching the umbrella rule failed - seated NPCs keep their umbrella: " + e.Message); }
        }

        private static bool UmbrellaPrefix(GameNpcActions __instance)
        {
            try
            {
                var npc = __instance.GetComponentInParent<NPC>();
                if (!IsSeated(npc)) return true;
                var umbrella = __instance._umbrellaAction;
                if (umbrella != null && umbrella.IsActive)
                {
                    umbrella.End();
                    Core.Log.Msg("[Seat] " + SeatRig.Who(npc) + " puts the umbrella away while seated.");
                }
                return false;
            }
            catch (Exception e) { Core.Log.Error("[Seat] the umbrella check failed, vanilla decides: " + e.Message); return true; }
        }

        /// <summary>Runs vanilla's umbrella rule once now: it ends the umbrella on seating and gives it back on leaving.</summary>
        internal static void RefreshUmbrella(NPC npc, string why)
        {
            try
            {
                var actions = npc != null ? npc.Actions : null;
                if (actions == null) { Core.Log.Warning("[Seat] " + SeatRig.Who(npc) + " has no NPCActions - the umbrella is not updated (" + why + ")."); return; }
                actions.UpdateUmbrellaUse();
                if (why != "seated") Core.Log.Msg("[Seat] " + SeatRig.Who(npc) + " umbrella " + (actions._umbrellaAction != null && actions._umbrellaAction.IsActive ? "held" : "not held") + " (" + why + ").");
            }
            catch (Exception e) { Core.Log.Error("[Seat] updating the umbrella of " + SeatRig.Who(npc) + " failed (" + why + "): " + e.Message); }
        }

        internal static string Describe() => "npc riders " + (Enabled ? "on" : "off") + ", " + _rigs.Count + " seated";
    }
}
