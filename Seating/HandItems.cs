using System;
using System.Collections.Generic;
using System.Reflection;
using Chassis.Compat;
using HarmonyLib;

namespace Chassis.Seating
{
    /// <summary>
    /// Hotbar items in a vehicle. Vanilla gives every vehicle a scene state with the <c>Vehicle</c> preset, which turns
    /// equipping and the hotbar off while the local player sits inside. Chassis lets the hotbar and equipping through,
    /// as the skateboard preset does, so the driver and every passenger can hold and use an item at any time.
    /// The state's other properties (locked mouse, vehicle camera, no movement) stay vanilla.
    ///
    /// Rules while seated:
    /// - Placeables (furniture, pots, skateboards) are not taken into the hand.
    /// - Weapons only aim and fire in the first-person view; in the chase view the right mouse button keeps turning the
    ///   camera and the weapon is only held.
    /// - A hovered cockpit switch or knob takes the click and the scroll, the item and the hotbar do not.
    /// - The chase view shows the item in the seated body's hand, not as a first-person viewmodel.
    /// - Trash from reloading and consuming lands outside the window next to the seat.
    /// </summary>
    internal static class HandItems
    {
        /// <summary>The vehicle state as Chassis sets it: vanilla's Vehicle preset with equipping and the hotbar on.</summary>
        internal static GameStateProperties Seated()
        {
            var p = GameStateProperties.Vehicle;
            p.Equipping = GameStateProperties.EEquippingState.Enabled;
            p.Inventory = GameStateProperties.EInventoryState.Interactable;
            return p;
        }

        /// <summary>The local player sits in (or pushes) a vehicle.</summary>
        internal static bool LocalSeated
        {
            get
            {
                var lp = Player.Local;
                return lp != null && lp.CurrentVehicle != null;
            }
        }

        private static bool _logged;

        /// <summary>Before the vehicle pushes its state: the state carries the seated properties from then on.</summary>
        [HarmonyPatch(typeof(LandVehicle), "EnterVehicle", new Type[0])]
        private static class EnterPatch
        {
            private static void Prefix(LandVehicle __instance)
            {
                try
                {
                    var state = __instance.State;
                    if (state == null) { Core.Log.Warning("[Items] " + __instance.name + " has no scene state yet - the hotbar stays off in it."); return; }
                    state.Properties = Seated();
                    if (!_logged) { _logged = true; Core.Log.Msg("[Items] hotbar and equipping stay on in vehicles (" + __instance.name + ": " + state.Properties + ")."); }
                }
                catch (Exception e) { Core.Log.Error("[Items] could not open the hotbar in " + __instance.name + ", it stays off: " + e); }
            }
        }

        // ---- placeables ----------------------------------------------------------------------------------------------

        /// <summary>Items that are put down in the world: buildables (furniture, pots, stations) and the skateboard.</summary>
        internal static bool IsPlaceable(GameItemInstance item)
        {
            if (item == null || item.Definition == null) return false;
            if (item.Definition.As<GameBuildableDefinition>() != null) return true;
            var eq = item.Equippable;
            if (eq == null) return false;
            return eq.GetComponent<GameBuildableEquippable>() != null || eq.GetComponent<GameSurfaceEquippable>() != null
                || eq.GetComponent<GameSkateboardEquippable>() != null;
        }

        private static string _refusedLogged;

        /// <summary>A placeable stays out of the hand while seated; the slot stays selected and equips it after getting out.</summary>
        [HarmonyPatch(typeof(HotbarSlot), "Equip", new Type[0])]
        private static class SlotEquipPatch
        {
            private static bool Prefix(HotbarSlot __instance)
            {
                try
                {
                    if (!LocalSeated) return true;
                    var item = __instance.ItemInstance;
                    if (!IsPlaceable(item)) return true;
                    string name = item.Definition != null ? item.Definition.Name : "?";
                    if (_refusedLogged != name) { _refusedLogged = name; Core.Log.Msg("[Items] " + name + " is a placeable, it stays out of the hand in the vehicle."); }
                    return false;
                }
                catch (Exception e) { Core.Log.Error("[Items] placeable check failed, the item is equipped as vanilla does: " + e); return true; }
            }
        }

        // ---- input ---------------------------------------------------------------------------------------------------

        private static int _depth;
        private static readonly List<GameInput.ButtonCode> _heldOff = new List<GameInput.ButtonCode>();
        private static readonly List<GameInput.ButtonCode> _downOff = new List<GameInput.ButtonCode>();
        private static int _patchedUpdates;

        /// <summary>
        /// Every equippable's Update gets a prefix and a postfix. While a rule blocks the item, the mouse buttons are
        /// taken out of the game's input lists for the length of that Update only, so the item sees them released (an
        /// aimed gun lowers as vanilla does) and the vehicle camera and the cockpit still read them.
        /// </summary>
        internal static void ApplyInputPatches(HarmonyLib.Harmony h)
        {
            try
            {
                var pre = new HarmonyMethod(typeof(HandItems).GetMethod(nameof(UpdatePrefix), BindingFlags.NonPublic | BindingFlags.Static));
                var post = new HarmonyMethod(typeof(HandItems).GetMethod(nameof(UpdatePostfix), BindingFlags.NonPublic | BindingFlags.Static));
                Type[] types;
                try { types = typeof(GameEquippable).Assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e)
                {
                    Core.Log.Warning("[Items] " + e.LoaderExceptions.Length + " type(s) of the game assembly did not load; their items are not checked.");
                    types = e.Types;
                }
                foreach (var t in types)
                {
                    if (t == null || !t.IsSubclassOf(typeof(GameEquippable))) continue;
                    var m = t.GetMethod("Update", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                    if (m == null) continue;
                    try { h.Patch(m, prefix: pre, postfix: post); _patchedUpdates++; }
                    catch (Exception e) { Core.Log.Error("[Items] " + t.Name + ".Update could not be patched, that item ignores the seat rules: " + e.Message); }
                }
                var hotbar = typeof(GamePlayerInventory).GetMethod("UpdateHotbarSelection", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (hotbar == null) Core.Log.Error("[Items] PlayerInventory.UpdateHotbarSelection not found - scrolling a cockpit knob also scrolls the hotbar.");
                else h.Patch(hotbar, prefix: new HarmonyMethod(typeof(HandItems).GetMethod(nameof(HotbarPrefix), BindingFlags.NonPublic | BindingFlags.Static)));
                Core.Log.Msg("[Items] seat rules on " + _patchedUpdates + " equippable Update method(s).");
            }
            catch (Exception e) { Core.Log.Error("[Items] patching the item input failed - weapons fire in every view while seated: " + e); }
        }

        /// <summary>Weapons are blocked in the chase view; every item is blocked while a cockpit control is hovered.</summary>
        private static bool Blocks(GameEquippable eq, out bool weapon)
        {
            weapon = eq.As<GameRangedWeapon>() != null || eq.As<GameMeleeWeapon>() != null;
            if (!LocalSeated) return false;
            if (Instruments.Cockpit.Hovered != null) return true;
            return weapon && !FirstPersonView.Active;
        }

        private static string _blockLogged;

#if DEBUG
        internal static int UpdateCalls;
        internal static string LastUpdated = "-";
#endif

        private static void UpdatePrefix(GameEquippable __instance)
        {
#if DEBUG
            UpdateCalls++;
            if (__instance != null) LastUpdated = Interop.TypeName(__instance);
#endif
            if (_depth++ > 0) return;
            try
            {
                if (__instance == null || !Blocks(__instance, out bool weapon)) return;
                var gi = Singleton<GameInput>.Instance;
                if (gi == null) { Core.Log.Warning("[Items] no GameInput, the item's input is not blocked this frame."); return; }
                Take(Held(gi), GameInput.ButtonCode.PrimaryClick, _heldOff);
                Take(Down(gi), GameInput.ButtonCode.PrimaryClick, _downOff);
                if (weapon)
                {
                    Take(Held(gi), GameInput.ButtonCode.SecondaryClick, _heldOff);
                    Take(Down(gi), GameInput.ButtonCode.SecondaryClick, _downOff);
                }
                if ((_heldOff.Count > 0 || _downOff.Count > 0))
                {
                    string why = Interop.TypeName(__instance) + (Instruments.Cockpit.Hovered != null ? ": cockpit control hovered" : ": weapon in the chase view");
                    if (_blockLogged != why) { _blockLogged = why; Core.Log.Msg("[Items] mouse input kept from " + why + "."); }
                }
            }
            catch (Exception e) { Core.Log.Error("[Items] blocking the item's input failed this frame: " + e.Message); }
        }

        private static void UpdatePostfix()
        {
            if (--_depth > 0) return;
            _depth = 0;
            Restore();
        }

        /// <summary>Gives the taken buttons back; also runs late in the frame in case an Update threw before its postfix.</summary>
        internal static void Restore()
        {
            if (_heldOff.Count == 0 && _downOff.Count == 0) return;
            try
            {
                var gi = Singleton<GameInput>.Instance;
                if (gi == null) { Core.Log.Warning("[Items] GameInput gone, " + (_heldOff.Count + _downOff.Count) + " taken button(s) dropped."); return; }
                var held = Held(gi); var down = Down(gi);
                foreach (var b in _heldOff) if (!held.Contains(b)) held.Add(b);
                foreach (var b in _downOff) if (!down.Contains(b)) down.Add(b);
            }
            catch (Exception e) { Core.Log.Error("[Items] giving the mouse buttons back failed: " + e.Message); }
            finally { _heldOff.Clear(); _downOff.Clear(); }
        }

        internal static void LateTick()
        {
            if (_ignoredForShot.Count > 0)
            {
                Core.Log.Warning("[Items] a shot ended without its postfix, " + _ignoredForShot.Count + " body tag(s) given back late.");
                RestoreShotTags();
            }
            if (_depth != 0 || _heldOff.Count > 0 || _downOff.Count > 0)
            {
                Core.Log.Warning("[Items] an item Update ended without its postfix (depth " + _depth + "), buttons given back late.");
                _depth = 0;
                Restore();
            }
        }

#if IL2CPP
        internal static Il2CppSystem.Collections.Generic.List<GameInput.ButtonCode> Held(GameInput gi) => gi.buttonsDown;
        internal static Il2CppSystem.Collections.Generic.List<GameInput.ButtonCode> Down(GameInput gi) => gi.buttonsDownThisFrame;
        internal static Il2CppSystem.Collections.Generic.List<GameInput.ButtonCode> Up(GameInput gi) => gi.buttonsUpThisFrame;
        private static void Take(Il2CppSystem.Collections.Generic.List<GameInput.ButtonCode> list, GameInput.ButtonCode b, List<GameInput.ButtonCode> taken)
#else
        private static readonly AccessTools.FieldRef<GameInput, List<GameInput.ButtonCode>> _held = AccessTools.FieldRefAccess<GameInput, List<GameInput.ButtonCode>>("buttonsDown");
        private static readonly AccessTools.FieldRef<GameInput, List<GameInput.ButtonCode>> _down = AccessTools.FieldRefAccess<GameInput, List<GameInput.ButtonCode>>("buttonsDownThisFrame");
        private static readonly AccessTools.FieldRef<GameInput, List<GameInput.ButtonCode>> _up = AccessTools.FieldRefAccess<GameInput, List<GameInput.ButtonCode>>("buttonsUpThisFrame");
        internal static List<GameInput.ButtonCode> Held(GameInput gi) => _held(gi);
        internal static List<GameInput.ButtonCode> Down(GameInput gi) => _down(gi);
        internal static List<GameInput.ButtonCode> Up(GameInput gi) => _up(gi);
        private static void Take(List<GameInput.ButtonCode> list, GameInput.ButtonCode b, List<GameInput.ButtonCode> taken)
#endif
        {
            if (list != null && list.Remove(b)) taken.Add(b);
        }

        /// <summary>Scrolling over a hovered cockpit knob turns the knob, not the hotbar.</summary>
        private static bool HotbarPrefix()
        {
            try
            {
                if (!LocalSeated || Instruments.Cockpit.Hovered == null) return true;
                return Mathf.Abs(GameInput.MouseScrollDelta) < 0.001f;
            }
            catch (Exception e) { Core.Log.Error("[Items] hotbar scroll check failed, the hotbar scrolls as vanilla: " + e.Message); return true; }
        }

        // ---- view and seat state -------------------------------------------------------------------------------------

        private static bool _wasSeated;
        private static bool _vmHidden;
        private static bool _avatarItemHidden;

        /// <summary>
        /// Per frame: puts a placeable away on getting in and back into the hand on getting out, hides the first-person
        /// viewmodel in the chase view, and hides the seated body's own copy of the item in the first-person view.
        /// </summary>
        internal static void Tick()
        {
            bool seated = LocalSeated;
            var inv = PlayerSingleton<GamePlayerInventory>.Instance;
            if (inv == null) return;
            if (seated != _wasSeated)
            {
                _wasSeated = seated;
                if (seated) PutPlaceableAway(inv);
                else TakePlaceableBack(inv);
            }
            bool hide = seated && !FirstPersonView.Active;
            if (hide != _vmHidden)
            {
                _vmHidden = hide;
                inv.SetViewmodelVisible(!hide);
                Core.Log.Msg("[Items] first-person viewmodel " + (hide ? "hidden in the chase view, the item shows in the seated hand." : "shown again."));
            }
            AvatarItem(seated && FirstPersonView.Active);
        }

        private static void PutPlaceableAway(GamePlayerInventory inv)
        {
            var slot = inv.equippedSlot;
            if (slot == null || !IsPlaceable(slot.ItemInstance) || inv.Equippable == null) return;
            inv.Equippable.Unequip();
            Core.Log.Msg("[Items] " + slot.ItemInstance.Definition.Name + " put away on getting in, it is a placeable.");
        }

        private static void TakePlaceableBack(GamePlayerInventory inv)
        {
            var slot = inv.equippedSlot;
            if (slot == null || !IsPlaceable(slot.ItemInstance) || inv.Equippable != null) return;
            slot.Select();
            Core.Log.Msg("[Items] " + slot.ItemInstance.Definition.Name + " back in the hand after getting out.");
        }

        /// <summary>In the first-person view the viewmodel shows the item; the seated body's copy would float beside it.</summary>
        private static void AvatarItem(bool hide)
        {
            var lp = Player.Local;
            var item = lp != null && lp.Avatar != null ? lp.Avatar.CurrentEquippable : null;
            if (item == null) { _avatarItemHidden = false; return; }
            int want = hide ? LayerMask.NameToLayer("Invisible") : lp.Avatar.gameObject.layer;
            if (item.gameObject.layer == want) { _avatarItemHidden = hide; return; }
            SetLayer(item.transform, want);
            if (hide != _avatarItemHidden) Core.Log.Msg("[Items] seated body's " + item.name + (hide ? " hidden in the first-person view." : " shown again."));
            _avatarItemHidden = hide;
        }

        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            for (int i = 0; i < t.childCount; i++) SetLayer(t.GetChild(i), layer);
        }

        internal static void Reset()
        {
            _depth = 0;
            _heldOff.Clear(); _downOff.Clear();
            _wasSeated = false; _vmHidden = false; _avatarItemHidden = false;
            _refusedLogged = null; _blockLogged = null;
            _ignoredForShot.Clear(); _shotLogged = false;
        }

        // ---- shots out of the own car --------------------------------------------------------------------------------

        private const string CombatIgnore = "CombatIgnore";
        private static readonly List<KeyValuePair<Collider, string>> _ignoredForShot = new List<KeyValuePair<Collider, string>>();
        private static bool _shotLogged;

        /// <summary>
        /// A shot starts at the camera inside the cabin. Vanilla stops a bullet at the first collider that cannot take
        /// damage, which is the own car's body or glass. For the length of the local player's shot the own vehicle's
        /// colliders carry the tag vanilla's bullet skips; occupants keep theirs, so a passenger can still be hit.
        /// </summary>
        [HarmonyPatch(typeof(GameRangedWeapon), "Fire", new Type[0])]
        private static class FirePatch
        {
            private static void Prefix() => TagOwnBody();

            private static void Postfix() => RestoreShotTags();
        }

        /// <summary>Gives the local player's vehicle colliders, except seated bodies, the tag vanilla's bullet skips.</summary>
        internal static void TagOwnBody()
        {
            if (_ignoredForShot.Count > 0) return;
            try
            {
                var lp = Player.Local;
                if (lp == null || lp.CurrentVehicle == null) return;
                var lv = lp.CurrentVehicle.GetComponent<LandVehicle>();
                if (lv == null) { Core.Log.Warning("[Items] vehicle of the local player has no LandVehicle, the shot can hit its body."); return; }
                foreach (var c in lv.GetComponentsInChildren<Collider>(true))
                {
                    // Triggers count too: vanilla's cast hits them, and the car's interaction volumes enclose the seats.
                    if (c == null || c.CompareTag(CombatIgnore)) continue;
                    // Seated bodies (players, NPCs) are parented into the car and stay hittable.
                    if (c.GetComponentInParent<GameAvatar>() != null) continue;
                    _ignoredForShot.Add(new KeyValuePair<Collider, string>(c, c.tag));
                    c.tag = CombatIgnore;
                }
                if (!_shotLogged) { _shotLogged = true; Core.Log.Msg("[Items] shot from inside " + lv.name + ": " + _ignoredForShot.Count + " body collider(s) let the bullet through."); }
            }
            catch (Exception e) { Core.Log.Error("[Items] letting the shot out of the car failed, it can hit the own body: " + e.Message); }
        }

        internal static void RestoreShotTags()
        {
            if (_ignoredForShot.Count == 0) return;
            foreach (var kv in _ignoredForShot)
            {
                try { if (kv.Key != null) kv.Key.tag = kv.Value; }
                catch (Exception e) { Core.Log.Error("[Items] giving " + kv.Key.name + " its tag '" + kv.Value + "' back failed: " + e.Message); }
            }
            _ignoredForShot.Clear();
        }

        // ---- trash out of the window ---------------------------------------------------------------------------------

        /// <summary>Spawn points this close to the local camera (first person or chase view) come from the player's own hands.</summary>
        private const float HandReach = 1.5f;
        /// <summary>How far out from the seat the body's outer skin is searched.</summary>
        private const float SkinSearch = 4f;

        /// <summary>
        /// Trash the local player makes in a vehicle (an empty magazine, a drunk can) spawns at the camera in vanilla,
        /// which is inside the cabin. It is moved out of the window beside the seat and thrown away from the car.
        /// </summary>
        [HarmonyPatch(typeof(GameTrashManager), "CreateTrashItem", new[] { typeof(string), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(string) })]
        private static class TrashPatch
        {
            private static void Prefix(string __0, ref Vector3 __1, ref Vector3 __3)
            {
                try
                {
                    var lp = Player.Local;
                    if (lp == null || lp.CurrentVehicle == null) return;
                    var cam = PlayerSingleton<PlayerCamera>.Instance;
                    if (cam == null) { Core.Log.Warning("[Items] no player camera, trash " + __0 + " stays where vanilla puts it."); return; }
                    if (Vector3.Distance(__1, cam.transform.position) > HandReach) return;
                    var lv = lp.CurrentVehicle.GetComponent<LandVehicle>();
                    if (lv == null) { Core.Log.Warning("[Items] vehicle of the local player has no LandVehicle, trash " + __0 + " stays inside."); return; }
                    Transform v = lv.transform;
                    // The chase camera sits outside the car, often on the other side; the seat's eye point says which
                    // window is beside the player.
                    var rig = Occupants.Of(lp);
                    Vector3 eye = rig != null && rig.Profile != null ? rig.Frame.TransformPoint(rig.Profile.Eyes) : cam.transform.position;
                    if (rig == null || rig.Profile == null) Core.Log.Warning("[Items] no seat of the local player, trash " + __0 + " goes out on the camera's side.");
                    float side = v.InverseTransformPoint(eye).x >= 0f ? 1f : -1f;
                    Vector3 dir = v.right * side;
                    Vector3 at = OutsideSkin(lv, eye - v.up * 0.35f, dir) + dir * 0.35f;
                    var rb = lv.GetComponent<Rigidbody>();
                    Vector3 carVel = rb != null ? rb.velocity : Vector3.zero;
                    Vector3 vel = carVel + v.right * side * 2.5f + Vector3.up * 0.8f;
                    Core.Log.Msg("[Items] trash " + __0 + " thrown out of the " + (side > 0f ? "right" : "left") + " window of " + lv.name + " (" + Vector3.Distance(__1, at).ToString("F2") + " m from the cabin spot).");
                    __1 = at; __3 = vel;
                }
                catch (Exception e) { Core.Log.Error("[Items] moving trash " + __0 + " out of the window failed, it spawns inside the cabin: " + e); }
            }
        }

        /// <summary>
        /// The outermost point of the vehicle's solid colliders on a line from <paramref name="from"/> out along
        /// <paramref name="dir"/>, found by casting back in from outside; the window glass and doors count as body.
        /// </summary>
        private static Vector3 OutsideSkin(LandVehicle lv, Vector3 from, Vector3 dir)
        {
            var ray = new Ray(from + dir * SkinSearch, -dir);
            float best = float.MaxValue;
            foreach (var c in lv.GetComponentsInChildren<Collider>())
            {
                if (c == null || c.isTrigger || !c.enabled) continue;
                if (c.Raycast(ray, out RaycastHit hit, SkinSearch) && hit.distance < best) best = hit.distance;
            }
            if (best == float.MaxValue)
            {
                Core.Log.Warning("[Items] no body collider of " + lv.name + " beside the seat, trash goes 1 m out to the side.");
                return from + dir * 1f;
            }
            return ray.GetPoint(best);
        }
    }
}
