#if DEBUG
using System;
using System.Collections.Generic;
using System.Globalization;
using Chassis.Compat;
using Chassis.Seating;
using HarmonyLib;
#if IL2CPP
using GameRegistry = Il2CppScheduleOne.Registry;
using GameCharacterCreator = Il2CppScheduleOne.CharacterCreator.CharacterCreator;
#else
using GameRegistry = ScheduleOne.Registry;
using GameCharacterCreator = ScheduleOne.CharacterCreator.CharacterCreator;
#endif

namespace Chassis.Debugging
{
    /// <summary>
    /// <c>chassis items ...</c>: state of hotbar items while seated, presses of the game's mouse and reload buttons
    /// without the real mouse, hotbar selection and aiming the first-person view at an NPC.
    /// </summary>
    internal static class ItemCommands
    {
        internal static void Run(string[] p)
        {
            string op = p.Length > 2 ? p[2].ToLowerInvariant() : "";
            switch (op)
            {
                case "status": Say("items: " + Status()); break;
                case "press":
                    if (p.Length < 4 || !TryButton(p[3], out var b)) { Say("items press primary|secondary|reload [frames]"); break; }
                    int frames = p.Length > 4 ? int.Parse(p[4], CultureInfo.InvariantCulture) : 1;
                    Press(b, frames);
                    Say("items press: " + b + " for " + frames + " frame(s)");
                    break;
                case "slot":
                    if (p.Length < 4) { Say("items slot <1-9>"); break; }
                    Slot(int.Parse(p[3], CultureInfo.InvariantCulture));
                    break;
                case "pick":
                    if (p.Length < 4) { Say("items pick <name or id>"); break; }
                    Pick(p[3].ToLowerInvariant());
                    break;
                case "creator":
                    // A test client joining without saved looks starts in the character creator, where the viewmodel
                    // and the held item stay inactive; this finishes it with the default looks.
                    {
                        var cc = Singleton<GameCharacterCreator>.InstanceExists ? Singleton<GameCharacterCreator>.Instance : null;
                        if (cc == null || !cc.IsOpen) Say("items creator: not open");
                        else { cc.Done(); Say("items creator: done with the default looks"); }
                    }
                    break;
                case "give":
                    if (p.Length < 4) { Say("items give <id> [qty]"); break; }
                    Say("items give: " + Give(p[3].ToLowerInvariant(), p.Length > 4 ? int.Parse(p[4], CultureInfo.InvariantCulture) : 1));
                    break;
                case "aim": Say("items aim: " + AimAtNpc()); break;
                case "ray": Say("items ray: " + Ray()); break;
                case "cop": Say("items cop: " + Cop()); break;
                case "seats": Say("items seats: " + Seats()); break;
                case "target": Say("items target: " + Target(p.Length > 3 ? float.Parse(p[3], CultureInfo.InvariantCulture) : 8f, p.Length > 4 && p[4].ToLowerInvariant() == "cop")); break;
                case "hp": Say("items hp: " + (_aimed != null ? SeatRig.Who(_aimed) + " health " + Hp(_aimed) : "no NPC aimed at, use 'chassis items aim'")); break;
                case "aimfix":
                    Say("items aimfix: " + (_aimed != null ? SeatRig.Who(_aimed) + ", " + FirstPersonView.Nudge(_aimed.transform.position + Vector3.up * 1.2f) : "no NPC aimed at, use 'chassis items target'"));
                    break;
                case "track":
                    _track = p.Length < 4 || p[3].ToLowerInvariant() != "off";
                    Say("items track: " + (_track ? "on" : "off") + (_aimed != null ? ", target " + SeatRig.Who(_aimed) : ", no NPC aimed at"));
                    break;
                case "exit":
                {
                    var lv = Player.Local != null && Player.Local.CurrentVehicle != null ? Player.Local.CurrentVehicle.GetComponent<LandVehicle>() : null;
                    if (lv == null) { Say("items exit: not in a vehicle"); break; }
                    lv.ExitVehicle();
                    Say("items exit: left " + lv.name);
                    break;
                }
                default:
                    Say("items status | press primary|secondary|reload [frames] | slot <1-9> | pick <name> | give <id> [qty] | creator | aim | target [m] [cop] | aimfix | track [off] | ray | hp | cop | seats | exit");
                    break;
            }
        }

        private static void Say(string s) => DebugConsolePatch.Say(s);

        private static bool TryButton(string s, out GameInput.ButtonCode b)
        {
            switch (s.ToLowerInvariant())
            {
                case "primary": b = GameInput.ButtonCode.PrimaryClick; return true;
                case "secondary": b = GameInput.ButtonCode.SecondaryClick; return true;
                case "reload": b = GameInput.ButtonCode.Reload; return true;
                default: b = GameInput.ButtonCode.PrimaryClick; return false;
            }
        }

        internal static string Status()
        {
            var lp = Player.Local;
            var inv = PlayerSingleton<GamePlayerInventory>.Instance;
            var eq = inv != null ? inv.Equippable : null;
            var slot = inv != null ? inv.equippedSlot : null;
            var item = slot != null ? slot.ItemInstance : null;
            var avItem = lp != null && lp.Avatar != null ? lp.Avatar.CurrentEquippable : null;
            var cam = PlayerSingleton<PlayerCamera>.Instance;
            bool vm = cam != null && cam.Camera != null && (cam.Camera.cullingMask & (1 << LayerMask.NameToLayer("Viewmodel"))) != 0;
            var rig = Occupants.Of(lp);
            string hand = "-";
            if (rig != null && rig.Avatar != null)
            {
                var ik = rig.Avatar.GetComponentInChildren<AvatarIKController>(true);
                if (ik != null && ik.BodyIK != null) hand = ik.BodyIK.solvers.rightHand.IKPositionWeight.ToString("F2", CultureInfo.InvariantCulture);
            }
            var state = lp != null && lp.CurrentVehicle != null ? lp.CurrentVehicle.GetComponent<LandVehicle>()?.State : null;
            string ammo = "-";
            var gun = eq != null ? eq.As<GameRangedWeapon>() : null;
            if (gun != null)
            {
                // Vanilla caps the accuracy by the player controller's speed, so a non-zero speed while seated keeps the spread wide.
                var pm = PlayerSingleton<PlayerMovement>.InstanceExists ? PlayerSingleton<PlayerMovement>.Instance : null;
                string speed = pm != null && pm.Controller != null ? pm.Controller.velocity.magnitude.ToString("F2", CultureInfo.InvariantCulture) : "-";
                ammo = gun.Ammo + " aim " + gun.Aim.ToString("F2", CultureInfo.InvariantCulture)
                    + " acc " + gun.Accuracy.ToString("F2", CultureInfo.InvariantCulture) + " ctrlSpeed " + speed;
            }
            return "seated=" + HandItems.LocalSeated + " fp=" + FirstPersonView.Active + " hovered=" + (Instruments.Cockpit.Hovered != null)
                + " slot=" + (item != null && item.Definition != null ? item.Definition.Name : "-") + " placeable=" + HandItems.IsPlaceable(item)
                + " equippable=" + (eq != null ? Interop.TypeName(eq) : "-") + " avatarItem=" + (avItem != null ? avItem.name + "@" + LayerMask.LayerToName(avItem.gameObject.layer) : "-")
                + " viewmodel=" + vm + " rightHandIK=" + hand + " gun=" + ammo
                + " stateEquipping=" + (state != null ? state.Properties.Equipping.ToString() : "-")
                + " pursuit=" + (lp != null && lp.CrimeData != null ? lp.CrimeData.CurrentPursuitLevel.ToString() + "/" + lp.CrimeData.Crimes.Count : "-")
                + " input=" + Input()
                + " eqUpdates " + HandItems.UpdateCalls + " last " + HandItems.LastUpdated
                + (eq != null ? " eqEnabled " + eq.enabled + " eqActive " + eq.gameObject.activeInHierarchy + " eqPos " + eq.transform.position : "");
        }

        /// <summary>What the game's input sees: time scale, GameInput instances, the singleton's held buttons and pending presses.</summary>
        private static string Input()
        {
            var gi = Singleton<GameInput>.InstanceExists ? Singleton<GameInput>.Instance : null;
            var all = UnityEngine.Object.FindObjectsOfType<GameInput>();
            var held = new List<string>();
            if (gi != null) foreach (var b in HandItems.Held(gi)) held.Add(b.ToString());
            var pcam = PlayerSingleton<PlayerCamera>.InstanceExists ? PlayerSingleton<PlayerCamera>.Instance : null;
            return "ts " + Time.timeScale.ToString("F2", CultureInfo.InvariantCulture) + " ui " + (pcam != null ? "[" + UiNames(pcam) + "]" : "-") + " instances " + (all != null ? all.Length : 0)
                + " held [" + string.Join(",", held) + "] pending " + _held.Count + " lateUpdates " + _lateUpdates
                + " secondary " + GameInput.GetButton(GameInput.ButtonCode.SecondaryClick);
        }

        /// <summary>Names of the UI elements the player camera counts as open; vanilla consumables need none.</summary>
        private static string UiNames(PlayerCamera pcam)
        {
            var list = pcam.ActiveUIElements;
            if (list == null) return "";
            var names = new List<string>();
            for (int i = 0; i < list.Count; i++) names.Add(list[i]);
            return string.Join(",", names);
        }

        private static int _lateUpdates;

        private static void Slot(int n)
        {
            var inv = PlayerSingleton<GamePlayerInventory>.Instance;
            if (inv == null) { Say("items slot: no PlayerInventory"); return; }
            Equip(inv, n - 1);
        }

        /// <summary>Selects the first hotbar slot whose item name or ID contains <paramref name="what"/>.</summary>
        private static void Pick(string what)
        {
            var inv = PlayerSingleton<GamePlayerInventory>.Instance;
            if (inv == null) { Say("items pick: no PlayerInventory"); return; }
            for (int i = 0; i < inv.hotbarSlots.Count; i++)
            {
                var it = inv.hotbarSlots[i].ItemInstance;
                var def = it != null ? it.Definition : null;
                if (def == null) continue;
                if (def.Name.ToLowerInvariant().Contains(what) || def.ID.ToLowerInvariant().Contains(what)) { Equip(inv, i); return; }
            }
            Say("items pick: no hotbar item matches '" + what + "', hotbar " + Hotbar(inv));
        }

        private static string Hotbar(GamePlayerInventory inv)
        {
            var parts = new List<string>();
            for (int i = 0; i < inv.hotbarSlots.Count; i++)
            {
                var it = inv.hotbarSlots[i].ItemInstance;
                parts.Add((i + 1) + "=" + (it != null && it.Definition != null ? it.Definition.ID + "x" + it.Quantity : "-"));
            }
            return string.Join(" ", parts);
        }

        /// <summary>Adds items through the local PlayerInventory and reports the hotbar after it, or the error.</summary>
        private static string Give(string id, int qty)
        {
            var inv = PlayerSingleton<GamePlayerInventory>.Instance;
            if (inv == null) return "no PlayerInventory";
            var def = GameRegistry.GetItem(id);
            if (def == null) return "unknown item '" + id + "'";
            int added = 0;
            try
            {
                for (; added < qty; added++)
                {
                    var inst = def.GetDefaultInstance(1);
                    if (!inv.CanItemFitInInventory(inst, 1)) { Core.Log.Warning("[Items] give " + id + ": no space after " + added + "."); break; }
                    inv.AddItemToInventory(inst);
                }
            }
            catch (Exception e)
            {
                Core.Log.Error("[Items] give " + id + " failed after " + added + ": " + e);
                return "failed after " + added + ": " + e.GetType().Name + " " + e.Message;
            }
            return "added " + added + " " + id + ", hotbar " + Hotbar(inv);
        }

        private static void Equip(GamePlayerInventory inv, int i)
        {
            int n = i + 1;
            if (i < 0 || i >= inv.hotbarSlots.Count) { Say("items slot: " + n + " out of range 1-" + inv.hotbarSlots.Count); return; }
            var slot = inv.hotbarSlots[i];
            inv.Equip(slot);
            Say("items slot " + n + ": " + (slot.ItemInstance != null && slot.ItemInstance.Definition != null ? slot.ItemInstance.Definition.Name : "empty") + " -> " + Status());
        }

        private static NPC _aimed;
        private static bool _track;
        private static Vector3? _holdSpot;
        private static Quaternion _holdRot;

        /// <summary>While tracking is on, turns the first-person view onto the aimed NPC's chest once per frame, so a
        /// walking target stays under the crosshair.</summary>
        private static void Track()
        {
            if (!_track) return;
            if (_aimed == null || !FirstPersonView.Active)
            {
                _track = false;
                Core.Log.Warning("[Items] aim tracking stopped: " + (_aimed == null ? "no NPC aimed at" : "first person is off") + ".");
                return;
            }
            try
            {
                // The NPC's schedule walks it on after the warp; it is put back whenever it drifts off its spot.
                if (_holdSpot.HasValue && _aimed.Movement != null && Vector3.Distance(_aimed.transform.position, _holdSpot.Value) > 0.3f)
                {
                    _aimed.Movement.Stop();
                    _aimed.Movement.Warp(_holdSpot.Value, _holdRot);
                }
                FirstPersonView.Nudge(_aimed.transform.position + Vector3.up * 1.2f);
            }
            catch (Exception e) { _track = false; Core.Log.Error("[Items] aim tracking failed and stopped: " + e.Message); }
        }

        /// <summary>
        /// Turns the first-person view to the chest of the nearest conscious NPC within 40 m, preferring one ahead of
        /// the car (within 60 degrees of its nose), so the shot leaves through the windscreen.
        /// </summary>
        private static string AimAtNpc()
        {
            var lp = Player.Local;
            if (lp == null) return "no player";
            Transform car = lp.CurrentVehicle != null ? lp.CurrentVehicle.transform : lp.transform;
            NPC best = null; float bd = float.MaxValue;
            foreach (var n in UnityEngine.Object.FindObjectsOfType<NPC>())
            {
                if (n == null || n.IsInVehicle || !n.IsConscious || !n.gameObject.activeInHierarchy) continue;
                Vector3 to = n.transform.position - lp.transform.position;
                float d = to.magnitude;
                if (d > 40f) continue;
                float score = d + (Vector3.Angle(Vector3.ProjectOnPlane(to, Vector3.up), Vector3.ProjectOnPlane(car.forward, Vector3.up)) > 60f ? 1000f : 0f);
                if (score < bd) { bd = score; best = n; }
            }
            if (best == null) return "no NPC on foot within 40 m";
            _aimed = best;
            _holdSpot = null;
            float dist =Vector3.Distance(best.transform.position, lp.transform.position);
            return SeatRig.Who(best) + " at " + dist.ToString("F1") + " m" + (bd >= 1000f ? " (behind or beside)" : " (ahead)") + ", health " + Hp(best) + ", "
                + FirstPersonView.LookAt(best.transform.position + Vector3.up * 1.2f);
        }

        /// <summary>
        /// Every seated player as this peer sees them: the item in the body's hand with its layer and renderer state,
        /// and the right hand's IK weight on the wheel.
        /// </summary>
        private static string Seats()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var rig in Occupants.All)
            {
                if (rig == null || rig.Player == null || rig.Avatar == null) continue;
                var it = rig.Avatar.CurrentEquippable;
                string item = "-";
                if (it != null)
                {
                    int shown = 0, all = 0;
                    foreach (var r in it.GetComponentsInChildren<Renderer>(true)) { all++; if (r.enabled && r.gameObject.activeInHierarchy) shown++; }
                    item = it.name + "@" + LayerMask.LayerToName(it.gameObject.layer) + " renderers " + shown + "/" + all;
                }
                string hand = "-";
                var ik = rig.Avatar.GetComponentInChildren<AvatarIKController>(true);
                if (ik != null && ik.BodyIK != null) hand = ik.BodyIK.solvers.rightHand.IKPositionWeight.ToString("F2", CultureInfo.InvariantCulture);
                sb.Append("; " + rig.Name + (rig.Player.IsLocalPlayer ? " (local)" : " (remote)") + " seat " + rig.SeatIndex + " item=" + item + " rightHandIK=" + hand);
            }
            return sb.Length == 0 ? "no seated player" : sb.ToString(2, sb.Length - 2);
        }

        /// <summary>The nearest conscious police officer on foot anywhere on the map, with its position.</summary>
        private static string Cop()
        {
            var lp = Player.Local;
            if (lp == null) return "no player";
            GamePoliceOfficer best = null; float bd = float.MaxValue;
            foreach (var o in UnityEngine.Object.FindObjectsOfType<GamePoliceOfficer>())
            {
                if (o == null || o.IsInVehicle || !o.IsConscious || !o.gameObject.activeInHierarchy) continue;
                float d = Vector3.Distance(o.transform.position, lp.transform.position);
                if (d < bd) { bd = d; best = o; }
            }
            if (best == null) return "no officer on foot";
            var p = best.transform.position;
            return SeatRig.Who(best) + " at " + bd.ToString("F1", CultureInfo.InvariantCulture) + " m pos="
                + p.x.ToString("F1", CultureInfo.InvariantCulture) + "," + p.y.ToString("F1", CultureInfo.InvariantCulture) + "," + p.z.ToString("F1", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Puts the nearest civilian on foot on the ground <paramref name="dist"/> m in front of the local player's car,
        /// stops them there and turns the first-person view to their chest.
        /// </summary>
        /// <summary>Puts the nearest civilian (or, with <paramref name="police"/>, officer) on foot <paramref name="dist"/> m
        /// ahead of the camera, facing the car, and turns the view onto its chest.</summary>
        private static string Target(float dist, bool police)
        {
            var lp = Player.Local;
            if (lp == null || lp.CurrentVehicle == null) return "not in a vehicle";
            Transform car = lp.CurrentVehicle.transform;
            NPC best = null; float bd = float.MaxValue;
            foreach (var n in UnityEngine.Object.FindObjectsOfType<NPC>())
            {
                if (n == null || n.IsInVehicle || !n.IsConscious || !n.gameObject.activeInHierarchy || n.Movement == null) continue;
                if ((n.As<GamePoliceOfficer>() != null) != police) continue;
                float d = Vector3.Distance(n.transform.position, car.position);
                if (d < bd) { bd = d; best = n; }
            }
            if (best == null) return police ? "no officer on foot" : "no civilian on foot";
            var pc = PlayerSingleton<PlayerCamera>.Instance;
            Transform view = pc != null ? pc.transform : car;
            Vector3 fwd = Vector3.ProjectOnPlane(view.forward, Vector3.up).normalized;
            Vector3 spot = view.position + fwd * dist;
            if (Physics.Raycast(spot + Vector3.up * 5f, Vector3.down, out var hit, 15f, ~0, QueryTriggerInteraction.Ignore)) spot = hit.point;
            best.Movement.Stop();
            best.Movement.Warp(spot, Quaternion.LookRotation(-fwd));
            _aimed = best;
            _holdSpot = spot;
            _holdRot = Quaternion.LookRotation(-fwd);
            return SeatRig.Who(best) + " put " + dist.ToString("F1", CultureInfo.InvariantCulture) + " m ahead at " + spot.ToString("F2") + ", health " + Hp(best) + ", "
                + FirstPersonView.LookAt(spot + Vector3.up * 1.2f);
        }

        private static string Hp(NPC n) => n != null && n.Health != null ? n.Health.Health.ToString("F0", CultureInfo.InvariantCulture) : "-";

        /// <summary>
        /// Vanilla's bullet cast from the camera along the view: the hits in order and the one a bullet stops at, which
        /// is the first collider without CombatIgnore that takes no damage.
        /// </summary>
        private static string Ray()
        {
            var pc = PlayerSingleton<PlayerCamera>.Instance;
            var cm = NetworkSingleton<GameCombatManager>.Instance;
            if (pc == null || cm == null) return "no camera or CombatManager";
            Transform cam = pc.transform;
            Vector3 from = cam.position + cam.forward * 0.4f + cam.right * 0.1f - cam.up * 0.03f;
            var hits = Physics.SphereCastAll(from, 0.05f, cam.forward, 60f, cm.RangedWeaponLayerMask);
            var list = new List<RaycastHit>(hits);
            list.Sort((x, y) => x.distance.CompareTo(y.distance));
            var lp = Player.Local;
            Transform car = lp != null && lp.CurrentVehicle != null ? lp.CurrentVehicle.transform : null;
            var sb = new System.Text.StringBuilder();
            string stop = "nothing";
            int shown = 0;
            foreach (var h in list)
            {
                var c = h.collider;
                bool own = car != null && c.transform.IsChildOf(car);
                bool ignore = c.CompareTag("CombatIgnore") || (own && c.GetComponentInParent<GameAvatar>() == null);
                var npc = c.GetComponentInParent<NPC>();
                bool dmg = npc != null || c.GetComponentInParent<Player>() != null;
                if (npc != null && npc == _aimed) { stop = "aimed " + SeatRig.Who(npc) + " (" + c.name + ") at " + h.distance.ToString("F2") + " m"; break; }
                if (shown++ < 8) sb.Append("; " + c.name + " " + h.distance.ToString("F2") + " m" + (own ? " own" : "") + (ignore ? " ignored" : "") + (dmg ? " body" : ""));
                if (!ignore && !dmg) { stop = c.name + (own ? " (own car)" : "") + " at " + h.distance.ToString("F2") + " m"; break; }
            }
            string target = _aimed != null ? ", aimed NPC at " + _aimed.transform.position.ToString("F2") + " " + Vector3.Distance(_aimed.transform.position, from).ToString("F1", CultureInfo.InvariantCulture) + " m" : "";
            return "bullet stops at " + stop + ", " + list.Count + " hit(s)" + target + sb;
        }

        /// <summary>Logs every crime the game records on the local player, so a test sees how police read a scene.</summary>
        [HarmonyPatch(typeof(GameCrimeData), "AddCrime")]
        private static class CrimeLogPatch
        {
            private static void Postfix(GameCrimeData __instance, GameCrime __0)
            {
                try
                {
                    if (Player.Local == null || __instance != Player.Local.CrimeData) return;
                    Core.Log.Msg("[Items] crime recorded on the local player: " + (__0 != null ? __0.CrimeName : "null")
                        + " (seated=" + HandItems.LocalSeated + ", pursuit " + __instance.CurrentPursuitLevel + ").");
                }
                catch (Exception e) { Core.Log.Warning("[Items] crime log failed: " + e.Message); }
            }
        }

        // ---- presses ------------------------------------------------------------------------------------------------

        private static readonly Dictionary<GameInput.ButtonCode, int> _held = new Dictionary<GameInput.ButtonCode, int>();
        private static readonly List<GameInput.ButtonCode> _fresh = new List<GameInput.ButtonCode>();

        private static void Press(GameInput.ButtonCode b, int frames)
        {
            _held[b] = Math.Max(1, frames);
            _fresh.Add(b);
        }

        /// <summary>
        /// After the game clears its per-frame button list: a pressed button counts as down in the next frame and as
        /// held for its frames, as a real press would.
        /// </summary>
        [HarmonyPatch(typeof(GameInput), "LateUpdate", new Type[0])]
        private static class InjectPatch
        {
            private static void Postfix(GameInput __instance)
            {
                _lateUpdates++;
                Track();
                if (_held.Count == 0) return;
                try
                {
                    var held = HandItems.Held(__instance);
                    var down = HandItems.Down(__instance);
                    foreach (var b in _fresh) if (!down.Contains(b)) down.Add(b);
                    _fresh.Clear();
                    foreach (var b in new List<GameInput.ButtonCode>(_held.Keys))
                    {
                        if (_held[b] <= 0)
                        {
                            held.Remove(b);
                            HandItems.Up(__instance).Add(b);
                            _held.Remove(b);
                            continue;
                        }
                        if (!held.Contains(b)) held.Add(b);
                        _held[b]--;
                    }
                }
                catch (Exception e) { Core.Log.Error("[Items] debug press failed: " + e.Message); _held.Clear(); _fresh.Clear(); }
            }
        }
    }
}
#endif
