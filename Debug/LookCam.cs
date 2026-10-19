#if DEBUG
using HarmonyLib;

namespace Chassis.Debugging
{
    /// <summary>
    /// Look-check camera for screenshots: orbits the local player's vehicle, else the nearest vehicle with a seated body, else the nearest wheelchair vehicle at a distance, a yaw against the
    /// chair's facing and a height, following it while it rolls. In a vehicle it writes after VehicleCamera; on foot
    /// through PlayerCamera.OverrideTransform every frame.
    /// </summary>
    [HarmonyPatch(typeof(VehicleCamera), "LateUpdate")]
    internal static class LookCam
    {
        internal static bool Active;
        private static float _dist = 2.6f, _yaw = 35f, _height = 1.2f;
        private static bool _overriding;

        internal static string Set(string[] p)
        {
            if (p.Length > 2 && p[2] == "off")
            {
                Active = false;
                if (_overriding && PlayerSingleton<PlayerCamera>.InstanceExists) PlayerSingleton<PlayerCamera>.Instance.StopTransformOverride(0f, true, true);
                _overriding = false;
                var lp = Player.Local;
                var rig = Seating.Occupants.Of(lp);
                if (lp != null && (rig == null || rig.IsPusher)) lp.SetThirdPersonMeshesVisibility(false);
                return "cam: off";
            }
            if (p.Length > 2) float.TryParse(p[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _dist);
            if (p.Length > 3) float.TryParse(p[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _yaw);
            if (p.Length > 4) float.TryParse(p[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _height);
            Active = true;
            // From outside the own body must show, also on foot or while pushing.
            Player.Local?.SetThirdPersonMeshesVisibility(true);
            return "cam: " + _dist + " m at " + _yaw + " deg, " + _height + " m up" + (Target() == null ? " (no wheelchair vehicle yet)" : "");
        }

        private static Transform Target()
        {
            var local = Player.Local;
            if (local == null) return null;
            if (local.CurrentVehicle != null) return local.CurrentVehicle.transform;
            Transform best = null; float bd = float.MaxValue;
            foreach (var rig in Seating.Occupants.All)
            {
                if (rig.Vehicle == null || rig.IsPusher) continue;
                float d = Vector3.Distance(rig.Vehicle.position, local.transform.position);
                if (d < bd) { bd = d; best = rig.Vehicle; }
            }
            if (best != null) return best;
            foreach (var v in UnityEngine.Object.FindObjectsOfType<LandVehicle>())
            {
                if (!Rideables.WheelchairType.Is(v)) continue;
                float d = Vector3.Distance(v.transform.position, local.transform.position);
                if (d < bd) { bd = d; best = v.transform; }
            }
            return best;
        }

        private static bool Place(out Vector3 at, out Quaternion rot)
        {
            at = Vector3.zero; rot = Quaternion.identity;
            var t = Target();
            if (t == null) return false;
            Vector3 fwd = Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
            Vector3 centre = t.position + Vector3.up * 0.7f;
            // Frame a seated body's head where there is one, so a car shot shows the driver and not the bonnet.
            foreach (var rig in Seating.Occupants.All)
                if (rig.Vehicle == t && !rig.IsPusher && rig.Head != null) { centre = rig.Head.position; break; }
            Vector3 dir = Quaternion.Euler(0f, _yaw, 0f) * fwd;
            at = t.position + dir * _dist + Vector3.up * _height;
            rot = Quaternion.LookRotation(centre - at, Vector3.up);
            return true;
        }

        private static void Postfix()
        {
            if (!Active || !Place(out var at, out var rot)) return;
            var cam = PlayerSingleton<PlayerCamera>.Instance.transform;
            cam.position = at; cam.rotation = rot;
        }

        /// <summary>On foot (no VehicleCamera running): override the player camera each frame.</summary>
        internal static void LateTick()
        {
            if (!Active) return;
            var local = Player.Local;
            if (local == null || local.CurrentVehicle != null || !PlayerSingleton<PlayerCamera>.InstanceExists) return;
            if (!Place(out var at, out var rot)) return;
            PlayerSingleton<PlayerCamera>.Instance.OverrideTransform(at, rot, 0f, false);
            _overriding = true;
        }
    }
}
#endif
