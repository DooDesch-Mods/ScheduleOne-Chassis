using System;
using System.Collections.Generic;
using Chassis.Api;
using Chassis.Net;
using Chassis.Seating;
using HarmonyLib;
using UnityEngine.Events;

namespace Chassis.Rideables
{
    /// <summary>
    /// A second player pushes an occupied wheelchair from behind. The pusher stands at the profile's push point with
    /// both hands on the handles and cannot walk; W/S and A/D go to the host, the host relays them, and the driver's
    /// peer (which simulates the vehicle) adds them to the driver's own throttle and steering. Interact again to let go.
    ///
    /// The host keeps one pusher per vehicle. An empty vehicle cannot be pushed: the vanilla drag of the prop covers that.
    /// </summary>
    internal static class Push
    {
        private const string TriggerName = "ChassisPushTrigger";
        private static readonly Vector3 TriggerCentre = new Vector3(0f, 0.98f, -0.55f);
        private static readonly Vector3 TriggerSize = new Vector3(0.7f, 0.4f, 0.32f);

        /// <summary>Pusher by vehicle net id, on every peer (player net id).</summary>
        private static readonly Dictionary<int, int> _pushers = new Dictionary<int, int>();
        /// <summary>Pusher input by vehicle net id: forward/back and steer, each -1, 0 or 1.</summary>
        private static readonly Dictionary<int, Vector2> _input = new Dictionary<int, Vector2>();

        private static int _localVehicle = -1;
        private static int _localSince;
        private static Vector2 _sentInput;
        private static float _lastChairYaw = float.NaN;

        /// <summary>Debug: drive input standing in for the driver's keys until <see cref="DebugUntil"/>.</summary>
        internal static Vector2 DebugDrive;
        internal static float DebugUntil;
        private static float _debugSteer;
        /// <summary>Debug: pusher input standing in for the pusher's keys until <see cref="DebugPushUntil"/>.</summary>
        internal static Vector2 DebugPush;
        internal static float DebugPushUntil;

        internal static bool HasPusher(int vehicle) => _pushers.ContainsKey(vehicle);
        internal static bool LocalPushing => _localVehicle >= 0;

        internal static void Reset()
        {
            if (_localVehicle >= 0) StopLocal("world reset");
            _pushers.Clear();
            _input.Clear();
        }

        /// <summary>Every peer, when a wheelchair starts: the handles behind the seat offer "Push".</summary>
        internal static void AddHandles(LandVehicle lv)
        {
            var frame = lv.transform.Find(WheelchairType.FrameName);
            if (frame == null) { Core.Log.Warning("[Push] " + lv.name + " has no frame, no handles to push."); return; }
            if (frame.Find(TriggerName) != null) return;
            var intObj = lv.transform.Find("IntObj");
            var go = new GameObject(TriggerName);
            go.transform.SetParent(frame, false);
            go.transform.localPosition = TriggerCentre;
            go.layer = intObj != null ? intObj.gameObject.layer : lv.gameObject.layer;
            var box = go.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = TriggerSize;
            var io = go.AddComponent<InteractableObject>();
            io.MaxInteractionRange = 2.5f;
            io.SetMessage("Push");
            io.onHovered.AddListener((UnityAction)(() => Hovered(lv, io)));
            io.onInteractStart.AddListener((UnityAction)(() => Interacted(lv)));
        }

        private static void Hovered(LandVehicle lv, InteractableObject io)
        {
            bool can = lv != null && lv.IsOccupied && !_pushers.ContainsKey(Ids.Of(lv)) && !LocalPushing;
            io.SetMessage("Push");
            io.SetInteractableState(can ? InteractableObject.EInteractableState.Default : InteractableObject.EInteractableState.Disabled);
        }

        private static void Interacted(LandVehicle lv)
        {
            var local = Player.Local;
            if (lv == null || local == null) { Core.Log.Warning("[Push] Push pressed without a vehicle or local player."); return; }
            if (!lv.IsOccupied) { Core.Log.Msg("[Push] Push pressed on an empty wheelchair, ignored."); return; }
            Core.Log.Msg("[Push] Push on net " + Ids.Of(lv) + " - asking the host.");
            ChassisNet.ToHost("req.push", Ids.Of(lv).ToString(), Ids.Of(local).ToString(), "1");
        }

        // ---- host --------------------------------------------------------------------------------------------------

        internal static void HostRequest(int vehicle, int player, bool on)
        {
            _pushers.TryGetValue(vehicle, out int current);
            bool has = _pushers.ContainsKey(vehicle);
            if (on)
            {
                var lv = Ids.Find<LandVehicle>(vehicle);
                if (lv == null || !lv.IsOccupied) { Core.Log.Msg("[Push] host: net " + vehicle + " is " + (lv == null ? "gone" : "empty") + ", push from player " + player + " dropped."); return; }
                if (has) { Core.Log.Msg("[Push] host: net " + vehicle + " already has pusher " + current + ", push from player " + player + " dropped."); return; }
                ChassisNet.ToAll("push", vehicle.ToString(), player.ToString());
                return;
            }
            if (!has || current != player) { Core.Log.Msg("[Push] host: player " + player + " is not pushing net " + vehicle + ", release ignored."); return; }
            ChassisNet.ToAll("push", vehicle.ToString(), "-1");
        }

        internal static void HostInput(int vehicle, Vector2 input) => ChassisNet.ToAll("pushin", vehicle.ToString(), ((int)input.x).ToString(), ((int)input.y).ToString());

        /// <summary>Host, every frame: a pusher lets go when the chair empties or disappears.</summary>
        private static void HostTick()
        {
            if (_pushers.Count == 0) return;
            foreach (var kv in new List<KeyValuePair<int, int>>(_pushers))
            {
                var lv = Ids.Find<LandVehicle>(kv.Key);
                var pusher = PlayerById(kv.Value);
                string why = lv == null ? "vehicle gone" : !lv.IsOccupied ? "nobody sits" : pusher == null ? "pusher left" : null;
                if (why == null) continue;
                Core.Log.Msg("[Push] host: pusher " + kv.Value + " lets go of net " + kv.Key + " (" + why + ").");
                ChassisNet.ToAll("push", kv.Key.ToString(), "-1");
            }
        }

        // ---- every peer --------------------------------------------------------------------------------------------

        internal static void OnPush(int vehicle, int player)
        {
            if (_pushers.TryGetValue(vehicle, out int old))
            {
                _pushers.Remove(vehicle);
                _input.Remove(vehicle);
                var oldPlayer = PlayerById(old);
                if (oldPlayer != null)
                {
                    if (oldPlayer.IsLocalPlayer) StopLocal("released");
                    else Occupants.Leave(oldPlayer, "stopped pushing");
                }
            }
            if (player < 0) { Core.Log.Msg("[Push] net " + vehicle + " has no pusher now."); return; }

            var lv = Ids.Find<LandVehicle>(vehicle);
            var p = PlayerById(player);
            if (lv == null || p == null) { Core.Log.Warning("[Push] pusher " + player + " on net " + vehicle + ": " + (lv == null ? "vehicle" : "player") + " not found here."); return; }
            _pushers[vehicle] = player;
            var rig = Occupants.Seat(p, lv, SeatRig.PushSeat, true, "pushing");
            if (rig == null) { Core.Log.Error("[Push] no push pose for " + ChassisApi.CodeOf(lv) + " - the pusher stands where they are."); }
            if (p.IsLocalPlayer) StartLocal(vehicle);
        }

        internal static void OnInput(int vehicle, Vector2 input)
        {
            _input.TryGetValue(vehicle, out var old);
            _input[vehicle] = input;
            if (old != input) Core.Log.Msg("[Push] pusher input on net " + vehicle + ": forward " + input.x + ", steer " + input.y + ".");
        }

        private static void StartLocal(int vehicle)
        {
            _localVehicle = vehicle;
            _localSince = Time.frameCount;
            _sentInput = Vector2.zero;
            _lastChairYaw = float.NaN;
            if (PlayerSingleton<PlayerMovement>.InstanceExists) PlayerSingleton<PlayerMovement>.Instance.CanMove = false;
            else Core.Log.Warning("[Push] no PlayerMovement - the pusher can still walk away.");
            Core.Log.Msg("[Push] local player pushes net " + vehicle + ".");
        }

        private static void StopLocal(string why)
        {
            var local = Player.Local;
            if (local != null) Occupants.Leave(local, "stopped pushing");
            if (PlayerSingleton<PlayerMovement>.InstanceExists) PlayerSingleton<PlayerMovement>.Instance.CanMove = true;
            Core.Log.Msg("[Push] local player stops pushing net " + _localVehicle + " (" + why + ").");
            _localVehicle = -1;
        }

        internal static void Tick()
        {
            if (ChassisNet.IsHost) HostTick();
            if (_localVehicle < 0) return;
            var lv = Ids.Find<LandVehicle>(_localVehicle);
            if (lv == null) { StopLocal("vehicle gone"); return; }

            bool typing = GameInput.IsTyping;
            if (!typing && Time.frameCount > _localSince + 1 && GameInput.GetButtonDown(GameInput.ButtonCode.Interact))
            {
                ChassisNet.ToHost("req.push", _localVehicle.ToString(), Ids.Of(Player.Local).ToString(), "0");
                return;
            }
            Vector2 axis = typing ? Vector2.zero : GameInput.MotionAxis;
            if (Time.time < DebugPushUntil) axis = new Vector2(DebugPush.y, DebugPush.x);
            var input = new Vector2(Quant(axis.y), Quant(axis.x));
            if (input != _sentInput)
            {
                _sentInput = input;
                ChassisNet.ToHost("req.pushin", _localVehicle.ToString(), ((int)input.x).ToString(), ((int)input.y).ToString());
            }
        }

        private static float Quant(float v) => v > 0.3f ? 1f : v < -0.3f ? -1f : 0f;

        /// <summary>
        /// Late in the frame: the pusher's capsule follows the push point behind the moving chair. The view yaw (the
        /// player transform's, as in PlayerCamera.RotateCamera) faces the chair on the first frame and then turns with it.
        /// </summary>
        internal static void LateTick()
        {
            if (_localVehicle < 0) return;
            var lv = Ids.Find<LandVehicle>(_localVehicle);
            var local = Player.Local;
            if (lv == null || local == null || !PlayerSingleton<PlayerMovement>.InstanceExists) return;
            var frame = lv.transform.Find(WheelchairType.FrameName) ?? lv.transform;
            var push = ChassisApi.For(lv)?.Push;
            if (push == null) return;
            var move = PlayerSingleton<PlayerMovement>.Instance;
            var cc = move.Controller;
            Vector3 feet = frame.TransformPoint(new Vector3(push.Stand.x, 0f, push.Stand.z));
            feet.y = frame.position.y;
            Vector3 centre = feet + Vector3.up * (cc != null ? cc.height * 0.5f : 0.9f);
            if (cc != null) cc.enabled = false;
            local.transform.position = centre;
            if (cc != null) cc.enabled = true;

            float chairYaw = frame.eulerAngles.y;
            if (float.IsNaN(_lastChairYaw))
            {
                local.transform.rotation = Quaternion.Euler(0f, chairYaw, 0f);
                Core.Log.Msg("[Push] view turned to the chair's heading " + chairYaw.ToString("F0") + ".");
            }
            else local.transform.rotation = Quaternion.Euler(0f, local.transform.eulerAngles.y + Mathf.DeltaAngle(_lastChairYaw, chairYaw), 0f);
            _lastChairYaw = chairYaw;
        }

        // ---- the driver's peer adds the pusher's input ------------------------------------------------------------

        [HarmonyPatch(typeof(LandVehicle), "UpdateThrottle")]
        private static class ThrottlePatch
        {
#if DEBUG
            private static float _nextTrace;

            /// <summary>While a console drive runs: speed, tilt and drive wheel state four times a second.</summary>
            private static void TraceDrive(LandVehicle lv)
            {
                _nextTrace = Time.time + 0.25f;
                var rb = lv.GetComponent<Rigidbody>();
                var e = lv.transform.eulerAngles;
                string wheels = "";
                foreach (var wc in lv.driveWheels) wheels += " rpm " + wc.rpm.ToString("F0") + " motor " + wc.motorTorque.ToString("F0") + " brake " + wc.brakeTorque.ToString("F0") + (wc.isGrounded ? "" : " AIR");
                Core.Log.Msg("[Drive] t " + (Time.time % 1000f).ToString("F2") + " speed " + lv.Speed_Kmh.ToString("F1") + " km/h vel " + rb.velocity.magnitude.ToString("F2")
                    + " pitch " + Mathf.DeltaAngle(0f, e.x).ToString("F0") + " roll " + Mathf.DeltaAngle(0f, e.z).ToString("F0") + " throttle " + lv.currentThrottle.ToString("F2") + wheels);
            }
#endif

            private static void Postfix(LandVehicle __instance)
            {
                if (!__instance.LocalPlayerIsDriver || !WheelchairType.Is(__instance)) return;
                float own = Time.time < DebugUntil ? DebugDrive.x : __instance.currentThrottle;
                __instance.currentThrottle = own;
                Occupants.LocalDrive = own;
#if DEBUG
                if (Time.time < DebugUntil + 1f && Time.time >= _nextTrace) TraceDrive(__instance);
#endif
                if (_input.Count == 0 || !_input.TryGetValue(Ids.Of(__instance), out var input) || input.x == 0f) return;
                // Both add up: a pusher alone moves the chair, both together pull harder (the motor curve keeps the top speed).
                __instance.currentThrottle = Mathf.Clamp(own + input.x, -2f, 2f);
            }
        }

        [HarmonyPatch(typeof(LandVehicle), "UpdateSteerAngle")]
        private static class SteerPatch
        {
            private static void Postfix(LandVehicle __instance)
            {
                if (Time.time < DebugUntil && DebugDrive.y != 0f && __instance.LocalPlayerIsDriver)
                {
                    // Vanilla eases its own angle back towards the keys each frame, so the debug angle runs on its own.
                    _debugSteer = Mathf.MoveTowards(_debugSteer, DebugDrive.y * __instance.ActualMaxSteeringAngle, __instance.steerRate * Time.deltaTime);
                    __instance.CurrentSteerAngle = _debugSteer;
                }
                else if (__instance.LocalPlayerIsDriver) _debugSteer = __instance.CurrentSteerAngle;
                if (_input.Count == 0 || !__instance.LocalPlayerIsDriver || !WheelchairType.Is(__instance)) return;
                if (!_input.TryGetValue(Ids.Of(__instance), out var input) || input.y == 0f) return;
                if (Mathf.Abs(GameInput.MotionAxis.x) > 0.001f) return; // the driver's own steering wins
                __instance.CurrentSteerAngle = Mathf.MoveTowards(__instance.CurrentSteerAngle, input.y * __instance.ActualMaxSteeringAngle, __instance.steerRate * Time.deltaTime);
            }
        }

        /// <summary>The pusher looks at the chair the whole time; vanilla's "Vehicle full" prompt stays hidden meanwhile.</summary>
        [HarmonyPatch(typeof(LandVehicle), "Hovered")]
        private static class HoverPatch
        {
            private static void Postfix(LandVehicle __instance)
            {
                if (_localVehicle < 0 || Ids.Of(__instance) != _localVehicle) return;
                var io = __instance.intObj;
                if (io != null) io.SetInteractableState(InteractableObject.EInteractableState.Disabled);
            }
        }

        internal static Player PlayerById(int id)
        {
            if (id < 0) return null;
            var list = Player.PlayerList;
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++) if (list[i] != null && Ids.Of(list[i]) == id) return list[i];
            return null;
        }

        internal static string Describe()
        {
            var parts = new List<string>();
            foreach (var kv in _pushers) parts.Add("net " + kv.Key + " by " + kv.Value + (_input.TryGetValue(kv.Key, out var i) ? " in " + i : ""));
            return "push: " + (parts.Count == 0 ? "none" : string.Join(", ", parts)) + (LocalPushing ? " (local pushes " + _localVehicle + ")" : "");
        }
    }
}
