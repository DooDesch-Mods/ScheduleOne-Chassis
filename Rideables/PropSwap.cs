using System;
using System.Collections.Generic;
using System.Globalization;
using Chassis.Net;
using UnityEngine.Events;

namespace Chassis.Rideables
{
    /// <summary>
    /// The placed wheelchair props become rideable. Each prop gets a "Sit" trigger over its seat; looking at the seat
    /// offers "Sit", looking at the frame keeps the vanilla "Pick up". Sitting asks the host, which spawns a wheelchair
    /// vehicle at the prop's pose and hides the prop on every peer; the asking player then enters it. When the
    /// vehicle is empty again, the host despawns it and every peer puts the prop back where the vehicle stood.
    ///
    /// Props are named across peers by their scene path and sibling index; the scene is the same on every peer.
    /// </summary>
    internal static class PropSwap
    {
        private const string TriggerName = "ChassisSitTrigger";
        private static readonly Vector3 TriggerCentre = new Vector3(0f, 0.66f, 0.06f);
        private static readonly Vector3 TriggerSize = new Vector3(0.44f, 0.32f, 0.46f);
        /// <summary>Seconds an empty swapped vehicle waits before it turns back into the prop.</summary>
        private const float EmptyGrace = 1.0f;
        /// <summary>Seconds after the swap during which the vehicle may still be empty (the player is entering).</summary>
        private const float EnterGrace = 4.0f;

        private sealed class Prop
        {
            internal string Key;
            internal Transform Root;
            internal Rigidbody Body;
            internal GameObject Trigger;
            internal int Vehicle = -1;
            internal float SwappedAt;
            internal float EmptySince = -1f;
            internal readonly List<KeyValuePair<Renderer, bool>> Renderers = new List<KeyValuePair<Renderer, bool>>();
            internal readonly List<KeyValuePair<Collider, bool>> Colliders = new List<KeyValuePair<Collider, bool>>();
            internal bool WasKinematic;
            internal bool Hidden;
        }

        private static readonly Dictionary<string, Prop> _props = new Dictionary<string, Prop>();
        private static float _nextScan;
        private static int _pendingEnter = -1;
        private static float _pendingUntil;

        internal static void Reset()
        {
            _props.Clear();
            _nextScan = 0f;
            _pendingEnter = -1;
        }

        internal static string KeyOf(Transform t)
        {
            string p = t.name + "#" + t.GetSiblingIndex();
            for (var x = t.parent; x != null; x = x.parent) p = x.name + "#" + x.GetSiblingIndex() + "/" + p;
            return p;
        }

        // ---- finding props -----------------------------------------------------------------------------------------

        private static void Scan()
        {
            int added = 0;
            foreach (var rb in UnityEngine.Object.FindObjectsOfType<Rigidbody>())
            {
                if (rb == null || rb.name.IndexOf("wheelchair", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (rb.GetComponentInParent<LandVehicle>() != null) continue;
                string key = KeyOf(rb.transform);
                if (_props.ContainsKey(key)) continue;
                var prop = new Prop { Key = key, Root = rb.transform, Body = rb };
                prop.Trigger = AddTrigger(prop);
                _props[key] = prop;
                added++;
            }
            if (added > 0) Core.Log.Msg("[Swap] " + added + " wheelchair prop(s) got a Sit trigger, " + _props.Count + " known.");
        }

        private static GameObject AddTrigger(Prop prop)
        {
            var go = new GameObject(TriggerName);
            go.transform.SetParent(prop.Root, false);
            go.transform.localPosition = TriggerCentre;
            go.layer = LayerOf(prop.Root);
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = TriggerSize;
            var io = go.AddComponent<InteractableObject>();
            io.MaxInteractionRange = 2.5f;
            io.SetMessage("Sit");
            io.SetInteractableState(InteractableObject.EInteractableState.Default);
            string key = prop.Key;
            io.onHovered.AddListener((UnityAction)(() => Hovered(key, io)));
            io.onInteractStart.AddListener((UnityAction)(() => Interacted(key)));
            return go;
        }

        /// <summary>The layer the prop's own colliders are on, which the interaction raycast already hits.</summary>
        private static int LayerOf(Transform root)
        {
            foreach (var c in root.GetComponentsInChildren<Collider>(true)) if (c != null && !c.isTrigger) return c.gameObject.layer;
            return root.gameObject.layer;
        }

        private static void Hovered(string key, InteractableObject io)
        {
            bool free = _props.TryGetValue(key, out var p) && !p.Hidden;
            io.SetMessage("Sit");
            io.SetInteractableState(free ? InteractableObject.EInteractableState.Default : InteractableObject.EInteractableState.Disabled);
        }

        private static void Interacted(string key)
        {
            var local = Player.Local;
            if (local == null) { Core.Log.Warning("[Swap] Sit pressed without a local player."); return; }
            if (local.CurrentVehicle != null) { Core.Log.Msg("[Swap] Sit pressed while already in a vehicle, ignored."); return; }
            Core.Log.Msg("[Swap] Sit on " + key + " - asking the host.");
            ChassisNet.ToHost("req.sit", key, Ids.Of(local).ToString());
        }

        // ---- host --------------------------------------------------------------------------------------------------

        internal static void HostSit(string key, int player)
        {
            if (!_props.TryGetValue(key, out var p) || p.Root == null) { Core.Log.Warning("[Swap] host: no prop " + key + ", Sit from player " + player + " dropped."); return; }
            if (p.Hidden || p.Vehicle >= 0) { Core.Log.Msg("[Swap] host: prop " + key + " is already a vehicle (net " + p.Vehicle + "), Sit from player " + player + " dropped."); return; }

            // Upright with the prop's heading: a knocked-over prop rolls on as a standing chair.
            Vector3 fwd = Vector3.ProjectOnPlane(p.Root.forward, Vector3.up);
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.ProjectOnPlane(p.Root.up, Vector3.up);
            if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
            var rot = Quaternion.LookRotation(fwd.normalized, Vector3.up);
            var pos = Ground(p.Root.position) + Vector3.up * 0.05f;

            var lv = WheelchairSpawn.Spawn(pos, rot, "Sit on prop " + key);
            if (lv == null) return;
            ChassisNet.ToAll("swap", key, Ids.Of(lv).ToString(), player.ToString());
        }

        /// <summary>Ground height under a point, so a prop resting on its side does not spawn the vehicle in the air.</summary>
        private static Vector3 Ground(Vector3 at)
        {
            var hits = Physics.RaycastAll(at + Vector3.up * 0.8f, Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            foreach (var h in hits)
            {
                if (h.collider == null || h.collider.attachedRigidbody != null) continue;
                if (h.point.y > best) best = h.point.y;
            }
            return float.IsNegativeInfinity(best) ? at : new Vector3(at.x, best, at.z);
        }

        /// <summary>Host, every frame: an empty swapped vehicle turns back into its prop.</summary>
        private static void HostTick()
        {
            foreach (var p in _props.Values)
            {
                if (p.Vehicle < 0) continue;
                var lv = Ids.Find<LandVehicle>(p.Vehicle);
                if (lv == null)
                {
                    Core.Log.Warning("[Swap] host: vehicle net " + p.Vehicle + " of prop " + p.Key + " is gone, the prop comes back where it was.");
                    ChassisNet.ToAll("unswap", p.Key, F(p.Root.position.x), F(p.Root.position.y), F(p.Root.position.z), F(p.Root.eulerAngles.y));
                    continue;
                }
                bool busy = lv.IsOccupied || Push.HasPusher(p.Vehicle) || Time.time - p.SwappedAt < EnterGrace;
                if (busy) { p.EmptySince = -1f; continue; }
                if (p.EmptySince < 0f) { p.EmptySince = Time.time; continue; }
                if (Time.time - p.EmptySince < EmptyGrace) continue;

                var t = lv.transform;
                Vector3 pos = t.position;
                float yaw = t.eulerAngles.y;
                Core.Log.Msg("[Swap] host: vehicle net " + p.Vehicle + " is empty, back to prop " + p.Key + " at " + pos.ToString("F2") + ".");
                ChassisNet.ToAll("unswap", p.Key, F(pos.x), F(pos.y), F(pos.z), F(yaw));
                WheelchairSpawn.Despawn(lv, "empty, prop restored");
            }
        }

        private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
        private static float P(string s) => float.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);

        // ---- every peer --------------------------------------------------------------------------------------------

        internal static void OnSwap(string key, int vehicle, int player)
        {
            if (!_props.ContainsKey(key)) Scan();
            if (!_props.TryGetValue(key, out var p)) { Core.Log.Error("[Swap] swap for unknown prop " + key + " - it stays visible beside the vehicle."); }
            else
            {
                p.Vehicle = vehicle;
                p.SwappedAt = Time.time;
                p.EmptySince = -1f;
                Hide(p);
            }
            var local = Player.Local;
            if (local != null && Ids.Of(local) == player)
            {
                _pendingEnter = vehicle;
                _pendingUntil = Time.time + 5f;
                Core.Log.Msg("[Swap] prop " + key + " is vehicle net " + vehicle + " now, entering.");
            }
            else Core.Log.Msg("[Swap] prop " + key + " is vehicle net " + vehicle + " now (player " + player + ").");
        }

        internal static void OnUnswap(string key, Vector3 pos, float yaw)
        {
            if (!_props.TryGetValue(key, out var p)) { Core.Log.Warning("[Swap] unswap for unknown prop " + key + "."); return; }
            p.Vehicle = -1;
            p.EmptySince = -1f;
            Show(p, pos, Quaternion.Euler(0f, yaw, 0f));
            Core.Log.Msg("[Swap] prop " + key + " is back at " + pos.ToString("F2") + " yaw " + yaw.ToString("F0") + ".");
        }

        private static void Hide(Prop p)
        {
            if (p.Hidden || p.Root == null) return;
            p.Renderers.Clear(); p.Colliders.Clear();
            foreach (var r in p.Root.GetComponentsInChildren<Renderer>(true)) { p.Renderers.Add(new KeyValuePair<Renderer, bool>(r, r.enabled)); r.enabled = false; }
            foreach (var c in p.Root.GetComponentsInChildren<Collider>(true)) { p.Colliders.Add(new KeyValuePair<Collider, bool>(c, c.enabled)); c.enabled = false; }
            if (p.Body != null) { p.WasKinematic = p.Body.isKinematic; p.Body.isKinematic = true; }
            p.Hidden = true;
        }

        private static void Show(Prop p, Vector3 pos, Quaternion rot)
        {
            if (p.Root == null) return;
            p.Root.SetPositionAndRotation(pos, rot);
            if (p.Body != null)
            {
                p.Body.position = pos;
                p.Body.rotation = rot;
            }
            if (p.Hidden)
            {
                foreach (var kv in p.Renderers) if (kv.Key != null) kv.Key.enabled = kv.Value;
                foreach (var kv in p.Colliders) if (kv.Key != null) kv.Key.enabled = kv.Value;
                if (p.Body != null)
                {
                    p.Body.isKinematic = p.WasKinematic;
                    if (!p.Body.isKinematic) { p.Body.velocity = Vector3.zero; p.Body.angularVelocity = Vector3.zero; }
                }
            }
            p.Hidden = false;
        }

        internal static void Tick()
        {
            if (Time.time >= _nextScan) { _nextScan = Time.time + 5f; Scan(); }
            if (ChassisNet.IsHost) HostTick();
            if (_pendingEnter >= 0) PendingEnter();
        }

        /// <summary>The spawn and the swap arrive before or after each other; enter once the vehicle exists here.</summary>
        private static void PendingEnter()
        {
            var lv = Ids.Find<LandVehicle>(_pendingEnter);
            if (lv != null && lv.IsSpawned && lv.IsPlayerOwned && lv.transform.Find(WheelchairType.FrameName) != null)
            {
                Core.Log.Msg("[Swap] entering vehicle net " + _pendingEnter + ".");
                _pendingEnter = -1;
                lv.EnterVehicle();
                return;
            }
            if (Time.time > _pendingUntil)
            {
                Core.Log.Error("[Swap] vehicle net " + _pendingEnter + " never became enterable here (found " + (lv != null) + ", spawned " + (lv != null && lv.IsSpawned)
                    + ", player-owned " + (lv != null && lv.IsPlayerOwned) + "); not entering.");
                _pendingEnter = -1;
            }
        }

        internal static void Parse(string[] a, out Vector3 pos, out float yaw)
        {
            pos = new Vector3(P(a[1]), P(a[2]), P(a[3]));
            yaw = P(a[4]);
        }

        internal static string Describe()
        {
            int swapped = 0;
            foreach (var p in _props.Values) if (p.Vehicle >= 0) swapped++;
            return "props: " + _props.Count + " known, " + swapped + " swapped" + (_pendingEnter >= 0 ? ", entering net " + _pendingEnter : "");
        }
    }
}
