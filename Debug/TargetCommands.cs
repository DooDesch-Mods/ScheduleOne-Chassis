#if DEBUG
using System;
using System.Globalization;
using Chassis.Api;
using Chassis.Seating;
using Chassis.Vehicles;

namespace Chassis.Debugging
{
    /// <summary>
    /// <c>chassis target ...</c>: seats an NPC, sends out a police car, arms the player and fires at seated NPCs, so the
    /// shots into cars (<see cref="SeatedTargets"/>) can be checked through the bridge.
    /// </summary>
    internal static class TargetCommands
    {
        private const string KeepHandler = "chassis.debug.keep";

        internal static void Run(string[] p)
        {
            string op = p.Length > 2 ? p[2].ToLowerInvariant() : "";
            switch (op)
            {
                case "seat": Seat(); break;
                case "patrol": Patrol(); break;
                case "near": Near(p.Length > 3 ? F(p[3]) : 6f); break;
                case "gun": Gun(); break;
                case "shoot": Shoot(p.Length > 3 ? p[3].ToLowerInvariant() : "body", p.Length > 4 ? int.Parse(p[4], CultureInfo.InvariantCulture) : 1); break;
                case "keep":
                    if (p.Length > 3 && p[3] == "on") ChassisApi.RegisterOccupantDown(KeepHandler, d => true);
                    else ChassisApi.RemoveOccupantDown(KeepHandler);
                    break;
                case "list": List(); break;
                case "slump":
                    if (p.Length > 5) { SeatRig.SlumpHeight = F(p[3]); SeatRig.SlumpForward = F(p[4]); SeatRig.SlumpWeight = F(p[5]); }
                    Say("target slump: height " + SeatRig.SlumpHeight + ", forward " + SeatRig.SlumpForward + ", weight " + SeatRig.SlumpWeight);
                    break;
                default:
                    Say("target seat | patrol | near [m] | gun | shoot [head|body] [n] | keep on|off | list | slump [h f w]");
                    break;
            }
        }

        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        private static void Say(string s) => DebugConsolePatch.Say(s);

        /// <summary>The nearest NPC on foot within 30 m gets into the driver's seat of the nearest car (host).</summary>
        private static void Seat()
        {
            if (!InstanceFinder.IsServer) { Say("target seat: host only"); return; }
            var lp = Player.Local;
            var car = DebugConsolePatch.Target(null);
            if (lp == null || car == null) { Say("target seat: no player or no car within 30 m"); return; }
            NPC best = null; float bd = 30f;
            foreach (var n in UnityEngine.Object.FindObjectsOfType<NPC>())
            {
                if (n == null || n.IsInVehicle || !n.IsConscious || !n.gameObject.activeInHierarchy) continue;
                float d = Vector3.Distance(n.transform.position, lp.transform.position);
                if (d < bd) { bd = d; best = n; }
            }
            if (best == null) { Say("target seat: no NPC on foot within 30 m"); return; }
            best.EnterVehicle(null, car);
            Say("target seat: " + SeatRig.Who(best) + " (" + bd.ToString("F1") + " m) into " + Registry.Name(car));
        }

        /// <summary>The nearest officer on foot gets into the nearest car and drives the closest patrol route (host).</summary>
        private static void Patrol()
        {
            if (!InstanceFinder.IsServer) { Say("target patrol: host only"); return; }
            var lp = Player.Local;
            var car = DebugConsolePatch.Target(null);
            if (lp == null || car == null) { Say("target patrol: no player or no car within 30 m"); return; }
            GamePoliceOfficer cop = null; float cd = 60f;
            foreach (var o in UnityEngine.Object.FindObjectsOfType<GamePoliceOfficer>())
            {
                if (o == null || o.IsInVehicle || !o.IsConscious || !o.gameObject.activeInHierarchy) continue;
                float d = Vector3.Distance(o.transform.position, lp.transform.position);
                if (d < cd) { cd = d; cop = o; }
            }
            if (cop == null) { Say("target patrol: no officer on foot within 60 m"); return; }
            GamePatrolRoute route = null; float rd = float.MaxValue;
            foreach (var r in UnityEngine.Object.FindObjectsOfType<GamePatrolRoute>())
            {
                if (r == null || r.Waypoints == null || r.Waypoints.Length == 0) continue;
                float d = Vector3.Distance(r.Waypoints[r.StartWaypointIndex].position, lp.transform.position);
                if (d < rd) { rd = d; route = r; }
            }
            if (route == null) { Say("target patrol: no vehicle patrol route"); return; }
            cop.EnterVehicle(null, car);
            cop.StartVehiclePatrol(route, car);
            Say("target patrol: " + SeatRig.Who(cop) + " (" + cd.ToString("F1") + " m) drives '" + route.RouteName + "' in " + Registry.Name(car));
        }

        /// <summary>The nearest seated NPC that is still conscious; a body already down only when there is no other.</summary>
        private static SeatRig Nearest()
        {
            var lp = Player.Local;
            SeatRig best = null; float bd = float.MaxValue;
            foreach (var rig in NpcRiders.All)
            {
                if (!rig.Alive || rig.Npc == null || rig.Head == null) continue;
                float d = (lp != null ? Vector3.Distance(rig.Head.position, lp.transform.position) : 0f) + (rig.Npc.IsConscious ? 0f : 1000f);
                if (d < bd) { bd = d; best = rig; }
            }
            return best;
        }

        /// <summary>Puts the player m metres off the left side of the nearest seated NPC's car, facing it.</summary>
        private static void Near(float m)
        {
            var rig = Nearest();
            if (rig == null) { Say("target near: no seated NPC"); return; }
            Vector3 side = -rig.Vehicle.right;
            Vector3 at = rig.Vehicle.position + side * m + Vector3.up * 0.3f;
            PlayerSingleton<PlayerMovement>.Instance.Teleport(at, true);
            Player.Local.transform.rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(-side, Vector3.up), Vector3.up);
            Say("target near: " + m + " m beside " + rig.Name + " in " + rig.Code);
        }

        /// <summary>Equips the first ranged weapon in the hotbar ("give revolver" first).</summary>
        private static void Gun()
        {
            var inv = PlayerSingleton<GamePlayerInventory>.Instance;
            if (inv == null) { Say("target gun: no PlayerInventory"); return; }
            foreach (var slot in inv.hotbarSlots)
            {
                var item = slot != null ? slot.ItemInstance : null;
                var eq = item != null && item.Definition != null ? item.Definition.Equippable : null;
                if (eq == null || eq.GetComponent<GameRangedWeapon>() == null) continue;
                inv.Equip(slot);
                Say("target gun: equipped " + item.Definition.Name);
                return;
            }
            Say("target gun: no ranged weapon in the hotbar, try 'give revolver'");
        }

        /// <summary>Fires the equipped gun n times at the nearest seated NPC, without spread.</summary>
        private static void Shoot(string where, int n)
        {
            var gun = UnityEngine.Object.FindObjectOfType<GameRangedWeapon>();
            if (gun == null) { Say("target shoot: no gun equipped, use 'chassis target gun'"); return; }
            var rig = Nearest();
            if (rig == null) { Say("target shoot: no seated NPC"); return; }
            Vector3 aim = rig.Head.position - (where == "head" ? Vector3.zero : rig.Vehicle.up * 0.35f);
            var cam = PlayerSingleton<PlayerCamera>.Instance.transform;
            float min = gun.MinSpread, max = gun.MaxSpread;
            gun.MinSpread = 0f; gun.MaxSpread = 0f;
            try
            {
                for (int i = 0; i < n; i++)
                {
                    cam.rotation = Quaternion.LookRotation(aim - cam.position, Vector3.up);
                    gun.Fire();
                }
            }
            finally { gun.MinSpread = min; gun.MaxSpread = max; }
            Say("target shoot: " + n + " shot(s) at " + rig.Name + "'s " + where + " in " + rig.Code + " from " + Vector3.Distance(cam.position, aim).ToString("F1") + " m");
        }

        private static void List()
        {
            var line = new System.Text.StringBuilder();
            int c = 0;
            foreach (var rig in NpcRiders.All)
            {
                if (rig.Npc == null) continue;
                c++;
                line.Append("; " + rig.Name + " in " + rig.Code + " seat " + rig.SeatIndex + ", conscious " + rig.Npc.IsConscious + ", in vehicle " + rig.Npc.IsInVehicle
                    + ", body active " + (rig.Avatar != null && rig.Avatar.gameObject.activeInHierarchy) + ", ragdolled " + (rig.Avatar != null && rig.Avatar.Ragdolled)
                    + ", head in seat frame " + (rig.Head != null && rig.Frame != null ? rig.Frame.InverseTransformPoint(rig.Head.position).ToString("F2") : "-")
                    + ", root in seat frame " + (rig.Avatar != null && rig.Frame != null ? rig.Frame.InverseTransformPoint(rig.Avatar.transform.position).ToString("F2") : "-"));
            }
            Say("target list: " + c + " seated NPC(s)" + line);
        }
    }
}
#endif
