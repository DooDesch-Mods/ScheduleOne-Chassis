using Chassis.Api;
using HarmonyLib;

namespace Chassis.Seating
{
    /// <summary>
    /// The local driver's own throttle for the hands on push rims (<see cref="Occupants.LocalDrive"/>), recorded before
    /// any other mod's UpdateThrottle postfix adds to it. In DEBUG builds the console's <c>chassis drive</c> stands in
    /// for the driver's keys here as well.
    /// </summary>
    internal static class DriveInput
    {
#if DEBUG
        /// <summary>Debug: drive input (forward, steer) standing in for the driver's keys until <see cref="DebugUntil"/>.</summary>
        internal static Vector2 DebugDrive;
        internal static float DebugUntil;
        private static float _debugSteer;
#endif

        /// <summary>A vehicle whose driver's hands ride the rims; only those need the stroke direction.</summary>
        private static bool RimDriven(LandVehicle lv) => ChassisApi.For(lv)?.Seat(0)?.Hands?.Mode == "rim";

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
                    + " pitch " + Mathf.DeltaAngle(0f, e.x).ToString("F0") + " roll " + Mathf.DeltaAngle(0f, e.z).ToString("F0") + " throttle " + lv.currentThrottle.ToString("F2") + " steer " + lv.CurrentSteerAngle.ToString("F1") + wheels);
            }
#endif

            [HarmonyPriority(Priority.First)]
            private static void Postfix(LandVehicle __instance)
            {
                if (!__instance.LocalPlayerIsDriver) return;
#if DEBUG
                if (Time.time < DebugUntil) __instance.currentThrottle = DebugDrive.x;
                if (Time.time < DebugUntil + 1f && Time.time >= _nextTrace) TraceDrive(__instance);
#endif
                if (RimDriven(__instance)) Occupants.LocalDrive = __instance.currentThrottle;
            }
        }

#if DEBUG
        [HarmonyPatch(typeof(LandVehicle), "UpdateSteerAngle")]
        private static class SteerPatch
        {
            [HarmonyPriority(Priority.First)]
            private static void Postfix(LandVehicle __instance)
            {
                if (!__instance.LocalPlayerIsDriver) return;
                if (Time.time < DebugUntil && DebugDrive.y != 0f)
                {
                    // Vanilla eases its own angle back towards the keys each frame, so the debug angle runs on its own.
                    _debugSteer = Mathf.MoveTowards(_debugSteer, DebugDrive.y * __instance.ActualMaxSteeringAngle, __instance.steerRate * Time.deltaTime);
                    __instance.CurrentSteerAngle = _debugSteer;
                }
                else _debugSteer = __instance.CurrentSteerAngle;
            }
        }
#endif
    }
}
