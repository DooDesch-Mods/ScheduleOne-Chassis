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
            private static void Prefix(LandVehicle __instance, out float __state) => __state = __instance.lastFrameSteerAngle;

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
            }
        }
    }
}
