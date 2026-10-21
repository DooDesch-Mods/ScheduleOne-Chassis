using System;
using System.Collections.Generic;
using System.Text;
using Chassis.Net;
using Chassis.Vehicles;
using HarmonyLib;

namespace Chassis.Rideables
{
    /// <summary>
    /// The display cars of the Hyland Auto showroom can be sat in. The displays are props (meshes, a VehicleColor and
    /// a sale sign), so the host puts a real car of the sign's code under each one: same pose, same colour, never
    /// player owned. Every peer hides the prop over that car, so the cabin, the seats and the doors are the ones of a
    /// real car while it still looks like the display. The car stays kinematic: the engine starts for a driver, but
    /// throttle and steering do nothing.
    ///
    /// Nothing is saved: vanilla saves only <c>VehicleManager.PlayerOwnedVehicles</c>, a car that is not player owned
    /// adds nothing to the net worth, shows no map marker and keeps its trunk closed. After a load the host puts the
    /// cars in again. Peers find the car of a display by its code and position, so a late joiner needs no message.
    /// </summary>
    internal static class Showroom
    {
        /// <summary>A car further than this from its display is not the display's car.</summary>
        private const float MatchDistance = 1.5f;
        private const float ScanSeconds = 2f;
        private const float RetrySeconds = 30f;
        private const int MaxAttempts = 3;

        private sealed class Display
        {
            internal string Key;
            internal string Code;
            internal Transform Root;
            internal Transform Sign;
            internal LandVehicle Car;
            internal int Attempts;
            internal float NextAttempt;
            internal bool Hidden;
            internal readonly List<KeyValuePair<Renderer, bool>> Renderers = new List<KeyValuePair<Renderer, bool>>();
            internal readonly List<KeyValuePair<Collider, bool>> Colliders = new List<KeyValuePair<Collider, bool>>();
        }

        private static readonly List<Display> _displays = new List<Display>();
        private static readonly HashSet<int> _demo = new HashSet<int>();
        private static readonly HashSet<int> _held = new HashSet<int>();
        private static float _nextScan;
        private static bool _scanned;
        private static bool _waitLogged;

        internal static void Reset()
        {
            _displays.Clear();
            _demo.Clear();
            _held.Clear();
            _nextScan = 0f;
            _scanned = false;
            _waitLogged = false;
        }

        /// <summary>True for a showroom car on this peer. Cheap: the vehicle patches ask on every hover and physics step.</summary>
        internal static bool IsDemo(LandVehicle lv) => lv != null && _demo.Count > 0 && _demo.Contains(lv.GetInstanceID());

        // ---- finding the displays ----------------------------------------------------------------------------------

        /// <summary>The scene path with sibling indices: tells two displays of the same name apart.</summary>
        private static string KeyOf(Transform t)
        {
            string p = t.name + "#" + t.GetSiblingIndex();
            for (var x = t.parent; x != null; x = x.parent) p = x.name + "#" + x.GetSiblingIndex() + "/" + p;
            return p;
        }

        private static void Scan()
        {
            var signs = UnityEngine.Object.FindObjectsOfType<VehicleSaleSign>();
            int added = 0;
            foreach (var s in signs)
            {
                if (s == null) continue;
                var root = s.transform.parent;
                if (root == null) { Core.Log.Warning("[Showroom] sale sign without a display above it - skipped."); continue; }
                if (s.VehiclePrefab == null) { Core.Log.Warning("[Showroom] the sale sign of " + root.name + " names no vehicle - skipped."); continue; }
                string key = KeyOf(root);
                if (_displays.Exists(d => d.Key == key)) continue;
                _displays.Add(new Display { Key = key, Code = s.VehiclePrefab.vehicleCode, Root = root, Sign = s.transform });
                added++;
            }
            if (added > 0)
            {
                var names = new List<string>();
                foreach (var d in _displays) names.Add(d.Root.name + "=" + d.Code);
                Core.Log.Msg("[Showroom] " + _displays.Count + " display car(s): " + string.Join(", ", names) + ".");
            }
            else if (_displays.Count == 0 && !_scanned) Core.Log.Msg("[Showroom] no display cars in this scene yet (" + signs.Length + " sale sign(s)).");
            _scanned = true;
        }

        internal static void Tick()
        {
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + ScanSeconds;
            if (_displays.Count == 0) Scan();
            if (_displays.Count == 0) return;
            if (!NetworkSingleton<VehicleManager>.InstanceExists || Player.Local == null)
            {
                if (!_waitLogged) Core.Log.Msg("[Showroom] waiting for the " + (Player.Local == null ? "local player" : "vehicle manager") + " before the display cars are set up.");
                _waitLogged = true;
                return;
            }

            foreach (var d in _displays)
            {
                if (d.Root == null) continue;
                if (d.Car == null) d.Car = Match(d);
                if (d.Car != null && !_demo.Contains(d.Car.GetInstanceID()))
                {
                    _demo.Add(d.Car.GetInstanceID());
                    Core.Log.Msg("[Showroom] " + d.Root.name + " is " + Registry.Name(d.Car) + " here.");
                    Unstick(d.Car);
                }
                if (d.Car != null) Hide(d);
                else
                {
                    Show(d);
                    if (ChassisNet.IsHost) HostPlace(d);
                }
            }
        }

        /// <summary>The car of this display: same code, standing on the display.</summary>
        private static LandVehicle Match(Display d)
        {
            Vector3 at = d.Root.position;
            foreach (var v in Registry.All)
            {
                if (v == null || v.IsPlayerOwned || !string.Equals(v.vehicleCode, d.Code, StringComparison.OrdinalIgnoreCase)) continue;
                if ((v.transform.position - at).sqrMagnitude < MatchDistance * MatchDistance) return v;
            }
            return null;
        }

        // ---- host --------------------------------------------------------------------------------------------------

        private static void HostPlace(Display d)
        {
            if (d.Attempts >= MaxAttempts || Time.unscaledTime < d.NextAttempt) return;
            d.Attempts++;
            d.NextAttempt = Time.unscaledTime + RetrySeconds;
            try
            {
                var vm = NetworkSingleton<VehicleManager>.Instance;
                var prefab = vm.GetVehiclePrefab(d.Code);
                if (prefab == null) { Core.Log.Error("[Showroom] no vehicle prefab '" + d.Code + "' for " + d.Root.name + " - it stays a prop."); d.Attempts = MaxAttempts; return; }
                Pose(d, prefab, out Vector3 pos, out Quaternion rot, out string how);

                var lv = vm.SpawnAndReturnVehicle(d.Code, pos, rot, false);
                if (lv == null) { Core.Log.Error("[Showroom] spawning '" + d.Code + "' for " + d.Root.name + " failed (attempt " + d.Attempts + " of " + MaxAttempts + ")."); return; }
                // Kinematic from the first physics step on: it must not settle on its springs or roll off the display.
                _demo.Add(lv.GetInstanceID());
                d.Car = lv;

                var paint = d.Root.GetComponent<VehicleColor>();
                if (paint != null) lv.SetOwnedColor(null, paint.displayedColor);
                else Core.Log.Warning("[Showroom] " + d.Root.name + " has no VehicleColor - the car keeps its default paint.");

                Core.Log.Msg("[Showroom] host: " + Registry.Name(lv) + " placed on " + d.Root.name + " at " + pos.ToString("F3") + " yaw " + rot.eulerAngles.y.ToString("F1")
                    + " (" + how + "), paint " + (paint != null ? paint.displayedColor.ToString() : "default") + ".");
                Hide(d);
                Unstick(lv);
            }
            catch (Exception e) { Core.Log.Error("[Showroom] placing the car on " + d.Root.name + " failed (attempt " + d.Attempts + " of " + MaxAttempts + "): " + e); }
        }

        /// <summary>
        /// The root pose that puts the car's body mesh where the display's body mesh is. The display is built from the
        /// same meshes, but its root need not sit where a car's root does. Falls back to the display's own pose.
        /// </summary>
        private static void Pose(Display d, LandVehicle prefab, out Vector3 pos, out Quaternion rot, out string how)
        {
            pos = d.Root.position;
            rot = d.Root.rotation;
            how = "display root, no shared mesh";
            var inPrefab = new Dictionary<string, Transform>();
            foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh == null) continue;
                string n = mf.sharedMesh.name;
                if (!inPrefab.ContainsKey(n)) inPrefab[n] = mf.transform;
            }
            MeshFilter best = null;
            Transform twin = null;
            float bestSize = 0f;
            foreach (var mf in d.Root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf == null || mf.sharedMesh == null || mf.transform.IsChildOf(d.Sign)) continue;
                if (!inPrefab.TryGetValue(mf.sharedMesh.name, out var t)) continue;
                float size = mf.sharedMesh.bounds.size.sqrMagnitude;
                if (size > bestSize) { bestSize = size; best = mf; twin = t; }
            }
            if (best == null) return;
            // The twin's pose in the car's root space, then the car root that puts the twin on the display mesh.
            Matrix4x4 inCar = prefab.transform.worldToLocalMatrix * twin.localToWorldMatrix;
            Matrix4x4 root = best.transform.localToWorldMatrix * inCar.inverse;
            pos = root.GetColumn(3);
            rot = Quaternion.LookRotation(root.GetColumn(2), root.GetColumn(1));
            how = "matched on mesh '" + best.sharedMesh.name + "', " + (pos - d.Root.position).magnitude.ToString("F3") + " m from the display root";
        }

        // ---- every peer --------------------------------------------------------------------------------------------

        /// <summary>
        /// A save made while sitting in a showroom car stores the seat position, and the car comes back a few seconds
        /// after the player. A local player inside the new car or on its roof steps out at its exit point.
        /// </summary>
        private static void Unstick(LandVehicle car)
        {
            var local = Player.Local;
            if (local == null || car == null || local.CurrentVehicle != null) return;
            Vector3 feet = local.transform.position;
            bool any = false;
            var body = new Bounds();
            foreach (var c in car.GetComponentsInChildren<Collider>())
            {
                if (c == null || !c.enabled || c.isTrigger) continue;
                if (!any) { body = c.bounds; any = true; }
                else body.Encapsulate(c.bounds);
            }
            if (!any) return;
            // Inside the body or standing on its roof: the saved seat position lands on one of the two.
            bool over = feet.x > body.min.x && feet.x < body.max.x && feet.z > body.min.z && feet.z < body.max.z;
            if (!over || feet.y < body.min.y - 0.2f || feet.y > body.max.y + 0.6f) return;
            var exit = car.GetExitPoint(0);
            if (exit == null) { Core.Log.Error("[Showroom] the local player stands inside " + Registry.Name(car) + ", which has no exit point - not moved."); return; }
            PlayerSingleton<PlayerMovement>.Instance.Teleport(exit.position + Vector3.up * 0.1f, true);
            Core.Log.Msg("[Showroom] the local player stood in or on " + Registry.Name(car) + " at " + feet.ToString("F2") + ", moved to its exit point " + exit.position.ToString("F2") + ".");
        }

        private static void Hide(Display d)
        {
            if (d.Hidden || d.Root == null) return;
            d.Renderers.Clear(); d.Colliders.Clear();
            foreach (var r in d.Root.GetComponentsInChildren<Renderer>(true))
            {
                if (r == null || r.transform.IsChildOf(d.Sign)) continue;
                d.Renderers.Add(new KeyValuePair<Renderer, bool>(r, r.enabled));
                r.enabled = false;
            }
            foreach (var c in d.Root.GetComponentsInChildren<Collider>(true))
            {
                if (c == null || c.transform.IsChildOf(d.Sign)) continue;
                d.Colliders.Add(new KeyValuePair<Collider, bool>(c, c.enabled));
                c.enabled = false;
            }
            d.Hidden = true;
            Core.Log.Msg("[Showroom] " + d.Root.name + " prop hidden (" + d.Renderers.Count + " renderer(s), " + d.Colliders.Count + " collider(s)) - its car stands there.");
        }

        private static void Show(Display d)
        {
            if (!d.Hidden || d.Root == null) return;
            foreach (var kv in d.Renderers) if (kv.Key != null) kv.Key.enabled = kv.Value;
            foreach (var kv in d.Colliders) if (kv.Key != null) kv.Key.enabled = kv.Value;
            d.Hidden = false;
            Core.Log.Warning("[Showroom] the car of " + d.Root.name + " is gone - the prop is back.");
        }

        // ---- vanilla hooks -----------------------------------------------------------------------------------------

        /// <summary>Vanilla offers "Enter vehicle" only on player-owned cars; a showroom car offers it too.</summary>
        [HarmonyPatch(typeof(LandVehicle), "Hovered")]
        private static class HoveredPatch
        {
            private static void Postfix(LandVehicle __instance)
            {
                try
                {
                    if (!IsDemo(__instance) || __instance.IsPlayerOwned) return;
                    if (__instance.CurrentPlayerOccupancy < __instance.Capacity)
                    {
                        __instance.intObj.SetMessage("Enter vehicle");
                        __instance.intObj.SetInteractableState(InteractableObject.EInteractableState.Default);
                    }
                    else
                    {
                        __instance.intObj.SetMessage("Vehicle full");
                        __instance.intObj.SetInteractableState(InteractableObject.EInteractableState.Invalid);
                    }
                }
                catch (Exception e) { Core.Log.Error("[Showroom] the Enter prompt failed: " + e.Message); }
            }
        }

        [HarmonyPatch(typeof(LandVehicle), "Interacted")]
        private static class InteractedPatch
        {
            private static void Postfix(LandVehicle __instance)
            {
                try
                {
                    if (!IsDemo(__instance) || __instance.IsPlayerOwned) return;
                    if (__instance.justExitedVehicle) { Core.Log.Msg("[Showroom] " + Registry.Name(__instance) + ": just got out, not entering again."); return; }
                    if (__instance.CurrentPlayerOccupancy >= __instance.Capacity) { Core.Log.Msg("[Showroom] " + Registry.Name(__instance) + " is full."); return; }
                    Core.Log.Msg("[Showroom] entering " + Registry.Name(__instance) + ".");
                    __instance.EnterVehicle();
                }
                catch (Exception e) { Core.Log.Error("[Showroom] entering the showroom car failed: " + e); }
            }
        }

        /// <summary>
        /// A driver makes vanilla simulate the car. A showroom car stays kinematic on every peer, so it keeps its
        /// place and the driver's Update skips throttle and steering; the engine still starts on entering.
        /// </summary>
        [HarmonyPatch(typeof(LandVehicle), "UpdatePhysicallySimulated")]
        private static class StayParkedPatch
        {
            private static bool Prefix(LandVehicle __instance)
            {
                if (!IsDemo(__instance)) return true;
                try
                {
                    var rb = __instance.Rb;
                    if (rb != null && !rb.isKinematic)
                    {
                        rb.velocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                        rb.isKinematic = true;
                    }
                    if (rb != null) rb.interpolation = RigidbodyInterpolation.None;
                    if (_held.Add(__instance.GetInstanceID()))
                    {
                        foreach (var w in __instance.wheels) if (w != null) w.SetPhysicsEnabled(false);
                        Core.Log.Msg("[Showroom] " + Registry.Name(__instance) + " held in place (kinematic, wheels off).");
                    }
                    __instance.IsPhysicallySimulated = false;
                }
                catch (Exception e) { Core.Log.Error("[Showroom] holding " + Registry.Name(__instance) + " in place failed: " + e.Message); }
                return false;
            }
        }

        // ---- debug -------------------------------------------------------------------------------------------------

        /// <summary>The display at index i, for the console.</summary>
        internal static LandVehicle CarAt(int i, out string name)
        {
            name = null;
            if (i < 0 || i >= _displays.Count) return null;
            name = _displays[i].Root != null ? _displays[i].Root.name : "?";
            return _displays[i].Car;
        }

        internal static int Count => _displays.Count;

        /// <summary>Debug: every prop drawn again over its car (on) or hidden as usual (off), for an alignment check.</summary>
        internal static string Ghost(bool on)
        {
            int n = 0;
            foreach (var d in _displays)
            {
                if (!d.Hidden) continue;
                foreach (var kv in d.Renderers) if (kv.Key != null) kv.Key.enabled = on && kv.Value;
                n++;
            }
            return "showroom ghost " + (on ? "on" : "off") + ": " + n + " prop(s)";
        }

        /// <summary>Debug: the hover and interact events of car i, as the interaction system fires them.</summary>
        internal static string Press(int i, bool interact)
        {
            var car = CarAt(i, out string name);
            if (car == null || car.intObj == null) return "showroom press: no car at index " + i;
            car.intObj.onHovered.Invoke();
            string seen = "prompt '" + car.intObj.message + "' state " + car.intObj.interactionState;
            if (interact) car.intObj.onInteractStart.Invoke();
            return "showroom press " + name + ": " + seen + (interact ? ", interacted" : "");
        }

        private static string Engine(LandVehicle car)
        {
            var snd = car.GetComponentInChildren<GameVehicleSound>();
            if (snd == null || snd.EngineIdleSource == null) return " engine: no sound";
            return " engine idle " + (snd.EngineIdleSource.IsPlaying ? "playing" : "silent") + " vol " + snd.EngineIdleSource.VolumeMultiplier.ToString("F2");
        }

        internal static string Describe()
        {
            var sb = new StringBuilder("showroom: " + _displays.Count + " display(s), " + _demo.Count + " car(s) known");
            for (int i = 0; i < _displays.Count; i++)
            {
                var d = _displays[i];
                sb.Append("\n " + i + " " + (d.Root != null ? d.Root.name : "?") + " " + d.Code + " -> "
                    + (d.Car != null ? Registry.Name(d.Car) + " owned " + d.Car.IsPlayerOwned + " kinematic " + (d.Car.Rb != null && d.Car.Rb.isKinematic)
                        + " paint " + d.Car.OwnedColor + " seats " + d.Car.CurrentPlayerOccupancy + "/" + d.Car.Capacity
                        + " off " + (d.Car.transform.position - d.Root.position).magnitude.ToString("F3") + " m" + Engine(d.Car)
                      : "no car (attempts " + d.Attempts + ")")
                    + (d.Hidden ? ", prop hidden" : ", prop shown"));
            }
            return sb.ToString();
        }
    }
}
