using System;
using System.Collections.Generic;
using HarmonyLib;

namespace Chassis.Seating
{
    /// <summary>
    /// First-person view from a Chassis seat, toggled with V. The camera sits at the seat profile's eye point and
    /// the mouse looks around; the seated body stays visible below it. Only this peer's copy of the head is hidden
    /// (vanilla DisableHead scales the neck away, face parts shadow-only), so the camera does not look out of the inside of the head.
    ///
    /// VehicleCamera owns the camera while the player sits in a vehicle, so the view is written in a postfix to
    /// VehicleCamera.LateUpdate: same frame, after the chase camera, nothing later moves it again.
    /// </summary>
    [HarmonyPatch(typeof(VehicleCamera), "LateUpdate")]
    internal static class FirstPersonView
    {
        internal const KeyCode Key = KeyCode.V;
        private const float MaxYaw = 135f, MinPitch = -70f, MaxPitch = 60f, NearClip = 0.03f;

        internal static bool Active { get; private set; }

        private static float _yaw, _pitch;
        private static float _savedNearClip = -1f;
        private static Transform _head;
        private static AvatarEffects _effects;
        private static bool _disableHeadBefore;
        private static readonly List<KeyValuePair<Renderer, UnityEngine.Rendering.ShadowCastingMode>> _hidden = new List<KeyValuePair<Renderer, UnityEngine.Rendering.ShadowCastingMode>>();
        private static bool _announced;

        internal static void Tick()
        {
            var rig = Occupants.Of(Player.Local);
            if (Active && (rig == null || rig.IsPusher)) { Set(false, "not seated"); return; }
            if (rig == null || rig.IsPusher || GameInput.IsTyping) return;
            if (Input.GetKeyDown(Key)) Set(!Active, "V pressed");
        }

        internal static void Reset()
        {
            if (Active) Set(false, "world unloaded");
        }

        internal static void Set(bool on, string why)
        {
            if (on == Active) return;
            var rig = Occupants.Of(Player.Local);
            if (on && rig == null) { Core.Log.Warning("[View] first person asked without a seat (" + why + ")."); return; }
            Active = on;
            _yaw = 0f; _pitch = 0f; _announced = false;
            var cam = PlayerSingleton<PlayerCamera>.InstanceExists ? PlayerSingleton<PlayerCamera>.Instance.Camera : null;
            if (cam != null)
            {
                if (on) { _savedNearClip = cam.nearClipPlane; cam.nearClipPlane = Mathf.Min(cam.nearClipPlane, NearClip); }
                else if (_savedNearClip > 0f) cam.nearClipPlane = _savedNearClip;
            }
            else Core.Log.Warning("[View] no player camera - the near clip plane stays as it is.");

            if (on) HideHead(rig);
            else ShowHead();
            Core.Log.Msg("[View] first person " + (on ? "on" : "off") + " (" + why + ").");
            Api.ChassisApi.RaiseFirstPerson(on);
        }

        private static void HideHead(SeatRig rig)
        {
            // AvatarEffects.Update writes the head bone's scale every frame; its DisableHead flag scales the neck to
            // zero instead and is this peer's own field, so other players still see the head.
            _effects = rig.Avatar.Effects;
            if (_effects != null) { _disableHeadBefore = _effects.DisableHead; _effects.DisableHead = true; }
            else Core.Log.Warning("[View] avatar has no AvatarEffects - the camera may look out of the head.");
            _head = rig.Head;
            // Glasses, eyes and other face parts are not children of the head bone; they cast only their shadow.
            Vector3 eyes = _head != null ? _head.position : rig.Frame.TransformPoint(rig.Profile.Eyes);
            foreach (var r in rig.Avatar.GetComponentsInChildren<Renderer>())
            {
                if (r == null || !r.enabled || r.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly) continue;
                Bounds b = r.bounds;
                if (Vector3.Distance(b.center, eyes) > 0.25f || b.extents.magnitude > 0.35f) continue;
                _hidden.Add(new KeyValuePair<Renderer, UnityEngine.Rendering.ShadowCastingMode>(r, r.shadowCastingMode));
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
            }
            Core.Log.Msg("[View] head hidden locally: neck " + (_effects != null ? "scaled away" : "kept") + ", " + _hidden.Count + " face part(s) shadow-only.");
        }

        private static void ShowHead()
        {
            if (_effects != null) _effects.DisableHead = _disableHeadBefore;
            _effects = null;
            _head = null;
            foreach (var kv in _hidden) if (kv.Key != null) kv.Key.shadowCastingMode = kv.Value;
            _hidden.Clear();
        }

        private static void Postfix(VehicleCamera __instance)
        {
            if (!Active) return;
            try
            {
                var rig = Occupants.Of(Player.Local);
                if (rig == null || __instance.vehicle == null || __instance.vehicle.transform != rig.Vehicle) return;
                var lv = __instance.vehicle;
                if (lv.State != null && lv.State.IsAcceptingInput)
                {
                    float sens = Singleton<Settings>.InstanceExists ? Singleton<Settings>.Instance.LookSensitivity : 1f;
                    Vector2 mouse = GameInput.MouseDelta;
                    _yaw = Mathf.Clamp(_yaw + mouse.x * 0.6f * sens, -MaxYaw, MaxYaw);
                    _pitch = Mathf.Clamp(_pitch - mouse.y * 0.6f * sens, MinPitch, MaxPitch);
                }
                var pc = PlayerSingleton<PlayerCamera>.Instance;
                Transform cam = pc.transform;
                var frame = rig.Frame;
                Vector3 eyePos = frame.TransformPoint(rig.Profile.Eyes);
                Quaternion eyeRot = frame.rotation * Quaternion.Euler(0f, rig.Profile.Yaw, 0f) * Quaternion.Euler(_pitch, _yaw, 0f);
                cam.position = eyePos;
                cam.rotation = eyeRot;
                // The rendering camera can sit below the PlayerCamera transform with an offset of its own; move the
                // parent so the camera that draws the picture ends up on the eye point.
                Transform view = pc.Camera != null ? pc.Camera.transform : cam;
                if (view != cam)
                {
                    cam.rotation = eyeRot * Quaternion.Inverse(view.rotation) * cam.rotation;
                    cam.position += eyePos - view.position;
                }
                if (!_announced)
                {
                    _announced = true;
                    var head = rig.Head;
                    Core.Log.Msg("[View] camera at eyes " + Api.ProfileJson.F(rig.Profile.Eyes) + " = " + view.position.ToString("F2")
                        + ", looking " + view.rotation.eulerAngles.ToString("F0") + " (frame " + frame.rotation.eulerAngles.ToString("F0")
                        + ", render camera " + (view == cam ? "is" : "under") + " PlayerCamera, parent " + (cam.parent != null ? cam.parent.name : "none") + ")"
                        + (head != null ? ", head bone at " + Api.ProfileJson.F(frame.InverseTransformPoint(head.position)) + " in frame space, scale " + head.lossyScale.x.ToString("F3") : "") + ".");
                }
            }
            catch (Exception e)
            {
                Core.Log.Error("[View] could not place the camera, back to the chase camera: " + e);
                Set(false, "camera error");
            }
        }

        /// <summary>Sets the look-down angle of the active first-person view; the mouse moves on from there.</summary>
        internal static void Look(float pitch)
        {
            if (!Active) { Core.Log.Warning("[View] pitch " + pitch + " ignored, first person is off."); return; }
            _pitch = Mathf.Clamp(pitch, MinPitch, MaxPitch);
        }

        internal static string Describe()
        {
            string s = "view=" + (Active ? "first person" : "chase") + " yaw " + _yaw.ToString("F0") + " pitch " + _pitch.ToString("F0");
            if (!PlayerSingleton<PlayerCamera>.InstanceExists) return s;
            var pc = PlayerSingleton<PlayerCamera>.Instance;
            Transform view = pc.Camera != null ? pc.Camera.transform : pc.transform;
            return s + ", render camera at " + view.position.ToString("F2") + " looking " + view.rotation.eulerAngles.ToString("F0");
        }
    }
}
