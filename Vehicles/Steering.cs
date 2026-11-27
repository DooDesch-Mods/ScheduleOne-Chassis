using System;
using HarmonyLib;

namespace Chassis.Vehicles
{
    /// <summary>
    /// Speed-sensitive steering for every vehicle the local player drives: the wheels turn less the faster it goes and
    /// ease back to straight on their own, so a tap at speed changes lane instead of spinning the car. The preference
    /// <c>SpeedSteering</c> turns it off for the game's own steering.
    /// </summary>
    internal static class Steering
    {
        /// <summary>The share of the full steering angle left at top speed.</summary>
        private const float TopSpeedLock = 0.35f;

        /// <summary>Turning in at top speed runs at this share of the vehicle's steer rate.</summary>
        private const float TopSpeedRate = 0.55f;

        /// <summary>Back to straight runs a little faster than turning in.</summary>
        private const float ReturnRate = 1.25f;

        internal static bool Enabled = true;

        internal static void LoadSetting()
        {
            try
            {
                var cat = MelonLoader.MelonPreferences.CreateCategory("Chassis");
                var entry = cat.CreateEntry("SpeedSteering", true, "Speed steering",
                    "The steering angle shrinks with speed and the wheels ease back to straight, as in GTA V. Off: the game's own steering.");
                Enabled = entry.Value;
                Core.Log.Msg("[Steering] speed steering " + (Enabled ? "on" : "off") + " (preference SpeedSteering).");
            }
            catch (Exception e)
            {
                Enabled = true;
                Core.Log.Warning("[Steering] reading the SpeedSteering preference failed - speed steering stays on: " + e.Message);
            }
        }

        /// <summary>
        /// Runs after the game moved the angle and replaces its step with one from the same start: the game starts every
        /// frame from <c>lastFrameSteerAngle</c>, so that field takes the new angle too.
        /// </summary>
        [HarmonyPatch(typeof(LandVehicle), "UpdateSteerAngle")]
        private static class SteerPatch
        {
            private static void Prefix(LandVehicle __instance, out float __state)
            {
                __state = __instance.lastFrameSteerAngle;
                try { WatchForeign(__instance, __state); }
                catch (Exception e) { _foreignBroken = true; Core.Log.Warning("[Steering] the check for other steering writers failed and stays off: " + e.Message); }
            }

            [HarmonyPriority(Priority.High)]
            private static void Postfix(LandVehicle __instance, float __state)
            {
                if (!Enabled || !__instance.LocalPlayerIsDriver) return;
                var me = Player.Local;
                // Taser, seizure and disorientation keep the game's own wild steering.
                if (me == null || me.Seizure || me.Disoriented || (__instance.DriverPlayer != null && __instance.DriverPlayer.IsTased)) return;

                float axis = GameInput.MotionAxis.x;
                bool input = Mathf.Abs(axis) > 0.001f && (__instance.State == null || __instance.State.IsAcceptingInput);
#if DEBUG
                // A console drive (chassis drive) steers through the same curve as the keys.
                if (Time.time < Seating.DriveInput.DebugUntil && Seating.DriveInput.DebugDrive.y != 0f) { axis = Seating.DriveInput.DebugDrive.y; input = true; }
#endif
                float t = Mathf.Clamp01(Mathf.Abs(__instance.Speed_Kmh) / Mathf.Max(1f, __instance.TopSpeed));
                float fast = Mathf.SmoothStep(0f, 1f, t);
                float target = input ? axis * __instance.ActualMaxSteeringAngle * Mathf.Lerp(1f, TopSpeedLock, fast) : 0f;
                bool back = Mathf.Abs(target) < Mathf.Abs(__state) || Mathf.Sign(target) != Mathf.Sign(__state);
                float rate = __instance.steerRate * (back ? ReturnRate : Mathf.Lerp(1f, TopSpeedRate, fast));
                float angle = Mathf.MoveTowards(__state, target, rate * Time.deltaTime);
                __instance.CurrentSteerAngle = angle;
                __instance.lastFrameSteerAngle = angle;
                _wroteId = __instance.GetInstanceID();
                _wrote = angle;
                _wroteFrame = Time.frameCount;
            }
        }

        // ---- other writers -------------------------------------------------------------------------------------------

        private static int _wroteId, _wroteFrame = -10;
        private static float _wrote;
        private static bool _foreignBroken;
        private static readonly System.Collections.Generic.HashSet<int> _foreignWarned = new System.Collections.Generic.HashSet<int>();

        /// <summary>
        /// The game starts each frame's step from the angle Chassis wrote last frame. When that angle has changed in
        /// between, another mod or the game steered the same car too, and the wheels jump between both values: the
        /// front wheels twitch while nothing is pressed. Warned once per car, with the other patches on the method.
        /// </summary>
        private static void WatchForeign(LandVehicle lv, float start)
        {
            if (_foreignBroken || !Enabled || Time.frameCount - _wroteFrame != 1 || lv.GetInstanceID() != _wroteId) return;
            if (Mathf.Abs(start - _wrote) < 0.05f) return;
            if (!_foreignWarned.Add(_wroteId)) return;
            Core.Log.Warning("[Steering] " + lv.VehicleCode + ": the steering angle changed from " + _wrote.ToString("0.00") + " to "
                + start.ToString("0.00") + " deg between two frames outside Chassis - something else steers this car too, which can make "
                + "the front wheels twitch. Other patches on LandVehicle.UpdateSteerAngle: " + OtherPatches() + ".");
        }

        private static string OtherPatches()
        {
            var m = AccessTools.Method(typeof(LandVehicle), "UpdateSteerAngle");
            var info = m != null ? HarmonyLib.Harmony.GetPatchInfo(m) : null;
            if (info == null) return "none";
            var owners = new System.Collections.Generic.List<string>();
            void Add(System.Collections.Generic.IEnumerable<HarmonyLib.Patch> list, string kind)
            {
                foreach (var p in list)
                {
                    if (p.PatchMethod != null && p.PatchMethod.DeclaringType != null && p.PatchMethod.DeclaringType.Assembly == typeof(Steering).Assembly) continue;
                    string name = kind + " " + (p.PatchMethod != null && p.PatchMethod.DeclaringType != null ? p.PatchMethod.DeclaringType.FullName : "?") + " (" + p.owner + ")";
                    if (!owners.Contains(name)) owners.Add(name);
                }
            }
            Add(info.Prefixes, "prefix");
            Add(info.Postfixes, "postfix");
            Add(info.Transpilers, "transpiler");
            return owners.Count == 0 ? "none" : string.Join(", ", owners);
        }
    }
}
