using System;
using System.Collections.Generic;
using Chassis.Api;
using Chassis.Vehicles;
using HarmonyLib;

namespace Chassis.Seating
{
    /// <summary>
    /// Gunshots reach NPCs seated in a vehicle. Vanilla hides an NPC in a car together with every collider a bullet could
    /// find, and the shot passes through the car body (it is damageable itself, so the cast does not stop there). A
    /// seated body Chassis shows keeps its colliders off for the driving AI's sake (<see cref="SeatRig"/>); they come on
    /// for the instant of <c>Equippable_RangedWeapon.Fire</c> only, so melee, the AI's rays and the physics never see
    /// them. The hit then takes vanilla's way: <c>NPC.SendImpact</c> to the host, damage, the police response.
    ///
    /// A driver that dies or is knocked out falls out as vanilla has it, unless a handler registered through
    /// <see cref="ChassisApi.RegisterOccupantDown"/> keeps the body in its seat. Vanilla takes a driver out when the
    /// paused driving behaviour calls <c>ExitVehicle</c>; an NPC with no such behaviour is taken out by the host half a
    /// second later and ragdolled. A kept body: the host holds back that exit, no peer ragdolls a body that is still in
    /// a vehicle, the dead stay out of the medical centre while seated, and <see cref="SeatRig"/> slumps the body.
    /// </summary>
    internal static class SeatedTargets
    {
        /// <summary>How long after going down the host holds back the NPC's exit; later exits (hospital, revive) pass.</summary>
        private const float HoldSeconds = 3f;
        /// <summary>How long after going down an NPC nobody keeps may stay in its seat before the host takes it out.</summary>
        private const float EjectSeconds = 0.5f;

        private static readonly Dictionary<string, Func<OccupantDown, bool>> _handlers = new Dictionary<string, Func<OccupantDown, bool>>();
        private static readonly Dictionary<int, float> _kept = new Dictionary<int, float>();
        private static readonly HashSet<int> _ragdollSkipLogged = new HashSet<int>();
        private static readonly List<SeatRig> _armed = new List<SeatRig>();
        private static readonly Dictionary<int, (NPC npc, float at)> _eject = new Dictionary<int, (NPC, float)>();
        private static readonly List<int> _due = new List<int>();
        private static readonly HashSet<int> _deadTickLogged = new HashSet<int>();

        internal static void Register(string name, Func<OccupantDown, bool> keepSeated)
        {
            if (string.IsNullOrEmpty(name) || keepSeated == null)
            {
                Core.Log.Error("[Targets] RegisterOccupantDown needs a name and a handler; ignored (name '" + name + "').");
                return;
            }
            _handlers[name] = keepSeated;
            Core.Log.Msg("[Targets] occupant-down handler '" + name + "' registered (" + _handlers.Count + " in total).");
        }

        internal static bool Remove(string name)
        {
            bool had = name != null && _handlers.Remove(name);
            Core.Log.Msg("[Targets] occupant-down handler '" + name + "' " + (had ? "removed." : "was not registered."));
            return had;
        }

        internal static void Reset()
        {
            Disarm("world reset");
            _kept.Clear();
            _eject.Clear();
            _ragdollSkipLogged.Clear();
            _deadTickLogged.Clear();
        }

        // ---- the shot ----------------------------------------------------------------------------------------------

        private static void Arm()
        {
            Disarm("a shot before ended without its postfix");
            int colliders = 0;
            foreach (var rig in NpcRiders.All)
            {
                if (!rig.Alive || rig.Npc == null) continue;
                int n = rig.ArmHitbox(true);
                if (n == 0) continue;
                colliders += n;
                _armed.Add(rig);
            }
            if (_armed.Count == 0) return;
            // Bones moved with the animation since the last physics step; the cast has to see where they are now.
            Physics.SyncTransforms();
            Core.Log.Msg("[Targets] shot fired: " + _armed.Count + " seated NPC(s) hittable for it, " + colliders + " collider(s).");
        }

        /// <summary>Switches the colliders off again; with a reason, it also says a previous shot left them on.</summary>
        internal static void Disarm(string why = null)
        {
            if (_armed.Count == 0) return;
            if (why != null) Core.Log.Warning("[Targets] " + _armed.Count + " seated body(ies) still hittable (" + why + "), switched off.");
            foreach (var rig in _armed)
            {
                try { if (rig.Alive) rig.ArmHitbox(false); }
                catch (Exception e) { Core.Log.Error("[Targets] switching " + rig.Name + "'s colliders off failed: " + e.Message); }
            }
            _armed.Clear();
        }

        [HarmonyPatch(typeof(GameRangedWeapon), nameof(GameRangedWeapon.Fire))]
        private static class FirePatch
        {
            private static void Prefix()
            {
                try { Arm(); }
                catch (Exception e) { Core.Log.Error("[Targets] making seated NPCs hittable failed, this shot passes through them: " + e); }
            }

            private static void Postfix()
            {
                try { Disarm(); }
                catch (Exception e) { Core.Log.Error("[Targets] switching seated bodies off after the shot failed: " + e); }
            }
        }

        // ---- host: damage and going down ---------------------------------------------------------------------------

        private static NPC NpcOf(Component c) => c != null ? c.GetComponentInParent<NPC>() : null;

        private static int SeatOf(NPC npc, LandVehicle lv)
        {
            var occupants = lv.OccupantNPCs;
            if (occupants == null) return 0;
            for (int i = 0; i < occupants.Length; i++)
                if (occupants[i] != null && occupants[i].GetInstanceID() == npc.GetInstanceID()) return i;
            return 0;
        }

        [HarmonyPatch(typeof(GameNpcHealth), nameof(GameNpcHealth.TakeDamage))]
        private static class DamagePatch
        {
            private static void Postfix(GameNpcHealth __instance, float damage)
            {
                try
                {
                    var npc = NpcOf(__instance);
                    if (npc == null || !npc.IsInVehicle) return;
                    Core.Log.Msg("[Targets] " + SeatRig.Who(npc) + " took " + damage.ToString("F0") + " damage in " + Registry.Name(npc.CurrentVehicle)
                        + ", health now " + __instance.Health.ToString("F0") + ".");
                }
                catch (Exception e) { Core.Log.Error("[Targets] logging damage to a seated NPC failed: " + e.Message); }
            }
        }

        /// <summary>
        /// Runs before <c>Die</c> / <c>KnockOut</c>: vanilla switches the NPC's behaviour inside that call, and the paused
        /// driving behaviour sends it out of the car right there, so whether the body stays has to be known before.
        /// </summary>
        private static void Decide(GameNpcHealth health, bool dead)
        {
            string what = dead ? "died" : "was knocked out";
            var npc = NpcOf(health);
            if (npc == null) { Core.Log.Warning("[Targets] an NPC health with no NPC above it " + what + "; nothing to decide."); return; }
            string who = SeatRig.Who(npc);
            if (!npc.IsInVehicle) { Core.Log.Msg("[Targets] " + who + " " + what + " on foot; vanilla handles it."); return; }
            if (!InstanceFinder.IsServer) { Core.Log.Msg("[Targets] " + who + " " + what + " in a vehicle; the host decides whether it stays seated."); return; }
            if ((dead && health.IsDead) || (!dead && health.IsKnockedOut)) { Core.Log.Msg("[Targets] " + who + " " + what + " again in a vehicle; decided the first time."); return; }
            var lv = npc.CurrentVehicle;
            var info = new OccupantDown { Vehicle = lv, Npc = npc, Seat = SeatOf(npc, lv), Dead = dead };
            string keptBy = null;
            foreach (var kv in _handlers)
            {
                bool keep;
                try { keep = kv.Value(info); }
                catch (Exception e) { Core.Log.Error("[Targets] occupant-down handler '" + kv.Key + "' threw, counted as vanilla: " + e); continue; }
                if (keep && keptBy == null) keptBy = kv.Key;
            }
            if (keptBy == null)
            {
                _eject[npc.GetInstanceID()] = (npc, Time.time + EjectSeconds);
                Core.Log.Msg("[Targets] " + who + " " + what + " in seat " + info.Seat + " of " + Registry.Name(lv)
                    + ": falls out as in vanilla (" + _handlers.Count + " handler(s) asked).");
                return;
            }
            _kept[npc.GetInstanceID()] = Time.time;
            Core.Log.Msg("[Targets] " + who + " " + what + " in seat " + info.Seat + " of " + Registry.Name(lv) + ": stays seated, kept by '" + keptBy + "'.");
        }

        /// <summary>An invincible NPC does not go down after all; the decision made for it is dropped.</summary>
        private static void Confirm(GameNpcHealth health, bool dead)
        {
            if (!InstanceFinder.IsServer) return;
            var npc = NpcOf(health);
            if (npc == null || (dead ? health.IsDead : health.IsKnockedOut)) return;
            int id = npc.GetInstanceID();
            if (_kept.Remove(id) | _eject.Remove(id))
                Core.Log.Msg("[Targets] " + SeatRig.Who(npc) + " is invincible and stays up; decision dropped.");
        }

        /// <summary>Host: an NPC that went down unkept and is still seated after <see cref="EjectSeconds"/> gets out and ragdolls.</summary>
        internal static void Tick()
        {
            if (_eject.Count == 0) return;
            _due.Clear();
            foreach (var kv in _eject)
                if (kv.Value.npc == null || !kv.Value.npc.IsInVehicle || Time.time >= kv.Value.at) _due.Add(kv.Key);
            foreach (int id in _due)
            {
                var npc = _eject[id].npc;
                _eject.Remove(id);
                if (npc == null) continue;
                if (!npc.IsInVehicle) { Core.Log.Msg("[Targets] " + SeatRig.Who(npc) + " got out by its own behaviour, as in vanilla."); continue; }
                Core.Log.Msg("[Targets] " + SeatRig.Who(npc) + " is still in " + Registry.Name(npc.CurrentVehicle) + " with no behaviour to take it out; it gets out and falls.");
                npc.ExitVehicle();
                if (npc.Movement != null && npc.Avatar != null && !npc.Avatar.Ragdolled) npc.Movement.ActivateRagdoll(Vector3.zero, Vector3.zero, 0f);
            }
        }

        [HarmonyPatch(typeof(GameNpcHealth), nameof(GameNpcHealth.Die))]
        private static class DiePatch
        {
            private static void Prefix(GameNpcHealth __instance)
            {
                try { Decide(__instance, true); }
                catch (Exception e) { Core.Log.Error("[Targets] deciding about a seated NPC that dies failed, it falls out as in vanilla: " + e); }
            }

            private static void Postfix(GameNpcHealth __instance)
            {
                try { Confirm(__instance, true); }
                catch (Exception e) { Core.Log.Error("[Targets] checking a seated NPC after Die failed: " + e.Message); }
            }
        }

        [HarmonyPatch(typeof(GameNpcHealth), nameof(GameNpcHealth.KnockOut))]
        private static class KnockOutPatch
        {
            private static void Prefix(GameNpcHealth __instance)
            {
                try { Decide(__instance, false); }
                catch (Exception e) { Core.Log.Error("[Targets] deciding about a seated NPC that is knocked out failed, it falls out as in vanilla: " + e); }
            }

            private static void Postfix(GameNpcHealth __instance)
            {
                try { Confirm(__instance, false); }
                catch (Exception e) { Core.Log.Error("[Targets] checking a seated NPC after KnockOut failed: " + e.Message); }
            }
        }

        /// <summary>The paused driving behaviour sends the NPC out right after it goes down; a kept body stays.</summary>
        [HarmonyPatch(typeof(NPC), nameof(NPC.ExitVehicle))]
        private static class ExitPatch
        {
            private static bool Prefix(NPC __instance)
            {
                try
                {
                    int id = __instance.GetInstanceID();
                    if (!_kept.TryGetValue(id, out float since)) return true;
                    if (__instance.IsConscious) { _kept.Remove(id); return true; }
                    if (Time.time - since > HoldSeconds) { _kept.Remove(id); return true; }
                    Core.Log.Msg("[Targets] " + SeatRig.Who(__instance) + " stays in " + Registry.Name(__instance.CurrentVehicle) + ": exit held back.");
                    return false;
                }
                catch (Exception e) { Core.Log.Error("[Targets] checking a seated NPC's exit failed, it gets out: " + e.Message); return true; }
            }

            /// <summary>A body that got out leaves its seat rig at once, before vanilla ragdolls it.</summary>
            private static void Postfix(NPC __instance)
            {
                try
                {
                    if (__instance == null || __instance.IsInVehicle) return;
                    _eject.Remove(__instance.GetInstanceID());
                    NpcRiders.Drop(__instance, "got out");
                }
                catch (Exception e) { Core.Log.Error("[Targets] releasing the seat of an NPC that got out failed, the next scan does it: " + e.Message); }
            }
        }

        /// <summary>
        /// Vanilla sends a dead NPC that is not ragdolled straight to the medical centre. A body kept in its seat is not
        /// ragdolled, so it waits there until the end of the day, as a ragdoll would.
        /// </summary>
        [HarmonyPatch(typeof(GameDeadBehaviour), nameof(GameDeadBehaviour.OnActiveTick))]
        private static class DeadTickPatch
        {
            private static bool Prefix(GameDeadBehaviour __instance)
            {
                try
                {
                    var npc = NpcOf(__instance);
                    if (npc == null || !npc.IsInVehicle) return true;
                    if (_deadTickLogged.Add(npc.GetInstanceID()))
                        Core.Log.Msg("[Targets] " + SeatRig.Who(npc) + " is dead in " + Registry.Name(npc.CurrentVehicle) + ": stays seated until the day ends, not sent to the medical centre.");
                    return false;
                }
                catch (Exception e) { Core.Log.Error("[Targets] checking a dead NPC in a vehicle failed, vanilla goes on: " + e.Message); return true; }
            }
        }

        /// <summary>At the end of the day the dead go to the medical centre; one still seated gets out first.</summary>
        [HarmonyPatch(typeof(GameDeadBehaviour), "EnterMedicalCentre")]
        private static class MedicalPatch
        {
            private static void Prefix(GameDeadBehaviour __instance)
            {
                try
                {
                    var npc = NpcOf(__instance);
                    if (npc == null || !npc.IsInVehicle || !InstanceFinder.IsServer) return;
                    _kept.Remove(npc.GetInstanceID());
                    _deadTickLogged.Remove(npc.GetInstanceID());
                    Core.Log.Msg("[Targets] " + SeatRig.Who(npc) + " leaves " + Registry.Name(npc.CurrentVehicle) + " for the medical centre.");
                    npc.ExitVehicle();
                }
                catch (Exception e) { Core.Log.Error("[Targets] taking a dead NPC out of its vehicle for the medical centre failed: " + e.Message); }
            }
        }

        /// <summary>A body still in a vehicle when it goes down was kept there; flinging it through the car would launch it.</summary>
        [HarmonyPatch(typeof(GameNpcMovement), nameof(GameNpcMovement.ActivateRagdoll))]
        private static class RagdollPatch
        {
            private static bool Prefix(GameNpcMovement __instance)
            {
                try
                {
                    var npc = NpcOf(__instance);
                    if (npc == null || !npc.IsInVehicle) return true;
                    if (_ragdollSkipLogged.Add(npc.GetInstanceID()))
                        Core.Log.Msg("[Targets] " + SeatRig.Who(npc) + " is still in " + Registry.Name(npc.CurrentVehicle) + ": no ragdoll, slumped in the seat.");
                    return false;
                }
                catch (Exception e) { Core.Log.Error("[Targets] checking a ragdoll for a seated NPC failed, it ragdolls: " + e.Message); return true; }
            }
        }
    }
}
