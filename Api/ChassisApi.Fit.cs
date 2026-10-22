// Copyright (c) 2026 DooDesch. DooDesch API License (see LICENSE.md): you may copy this file unchanged.
using System;
using Chassis.Seating;

namespace Chassis.Api
{
    public static partial class ChassisApi
    {
        /// <summary>The vanilla sit clip puts the hips this far above the avatar root.</summary>
        private const float SitHipLift = 0.825f;

        /// <summary>
        /// A first seat profile for one of a vehicle's vanilla seats: the body sits on the seat, faces the vehicle's
        /// front, holds the steering wheel (or rests the hands on the lap without one) and puts the feet into the
        /// footwell. A starting point for a new vehicle; fine-tune it with <c>chassis profile set</c> and save it.
        /// </summary>
        /// <param name="vehicle">Vehicle whose <c>Seats</c> entry the profile is for.</param>
        /// <param name="seatIndex">Index into <c>LandVehicle.Seats</c>.</param>
        /// <param name="frame">Model frame the profile's points are in (the transform at <see cref="ChassisProfile.Frame"/>).</param>
        /// <param name="steeringPivot">Steering wheel pivot for "wheel" hands, or null for hands on the lap.</param>
        /// <param name="hipHeight">Hips above the vanilla seat transform, in metres.</param>
        public static SeatProfile FitSeat(LandVehicle vehicle, int seatIndex, Transform frame, Transform steeringPivot = null, float hipHeight = 0.15f)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            var seats = vehicle.Seats;
            if (seats == null || seatIndex < 0 || seatIndex >= seats.Length || seats[seatIndex] == null)
                throw new ArgumentOutOfRangeException(nameof(seatIndex), vehicle.name + " has no seat " + seatIndex);
            return FitSeat(vehicle, seatIndex, seats[seatIndex].transform.position, frame, steeringPivot, hipHeight);
        }

        /// <summary>
        /// As <see cref="FitSeat(LandVehicle, int, Transform, Transform, float)"/>, for a body that sits somewhere else
        /// than the vanilla seat: a mod's own chair on a vehicle it built over a vanilla one.
        /// </summary>
        /// <param name="seatPoint">World point the hips rest on, minus <paramref name="hipHeight"/>.</param>
        public static SeatProfile FitSeat(LandVehicle vehicle, int seatIndex, Vector3 seatPoint, Transform frame, Transform steeringPivot = null, float hipHeight = 0.15f)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            Transform root = vehicle.transform;
            Vector3 s = seatPoint;
            Vector3 up = root.up, fwd = root.forward, right = root.right;
            Vector3 F(Vector3 world) => frame.InverseTransformPoint(world);

            Vector3 forwardInFrame = frame.InverseTransformDirection(fwd);
            float yaw = Mathf.Atan2(forwardInFrame.x, forwardInFrame.z) * Mathf.Rad2Deg;

            var seat = new SeatProfile
            {
                Index = seatIndex,
                Root = F(s + up * (hipHeight - SitHipLift) - fwd * 0.05f),
                Yaw = Mathf.Round(yaw * 10f) / 10f,
                Eyes = F(s + up * (hipHeight + 0.62f)),
                Pose = "Sitting",
                Hands = new HandProfile
                {
                    Mode = steeringPivot != null ? "wheel" : "fixed",
                    GripStart = steeringPivot != null
                        ? F(steeringPivot.position + right * 0.17f)
                        : F(s + right * 0.15f + up * (hipHeight + 0.05f) + fwd * 0.3f),
                    Elbow = F(s + right * 0.35f + up * (hipHeight + 0.1f) + fwd * 0.1f),
                    RotationWeight = 0f,
                },
                Feet = new FootProfile
                {
                    Position = F(s + fwd * 0.75f + up * (hipHeight - 0.45f) + right * 0.15f),
                    Rotation = new Vector3(-75f, 180f, 0f),
                    RotationWeight = 0.8f,
                    Knee = F(s + fwd * 0.5f + up * (hipHeight + 0.1f) + right * 0.15f),
                },
            };
            if (steeringPivot != null) seat.Hands.RimRadius = RimRadiusOf(steeringPivot, seat.Hands.RimThickness);
            Core.Log.Msg("[Profiles] fitted seat " + seatIndex + " of " + vehicle.name + ": root " + ProfileJson.F(seat.Root) + " yaw " + seat.Yaw.ToString("F0")
                + " eyes " + ProfileJson.F(seat.Eyes) + ", hands " + seat.Hands.Mode + " at " + ProfileJson.F(seat.Hands.GripStart) + ", rim radius " + seat.Hands.RimRadius.ToString("F3") + ".");
            return seat;
        }

        /// <summary>
        /// The rim's radius from the wheel's meshes under the pivot, measured in the pivot's space: the thinnest extent is
        /// the column, the widest one across it is the rim's outer diameter. 0 (the hands keep the plain grip) when no
        /// readable mesh hangs under the pivot, as with a static-batched wheel.
        /// </summary>
        private static float RimRadiusOf(Transform pivot, float thickness)
        {
            bool any = false;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            foreach (var mf in pivot.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh = mf.sharedMesh;
                // A static-batched wheel shares one combined mesh with the whole scene; its bounds say nothing about the wheel.
                if (mesh == null || mesh.name.StartsWith("Combined Mesh")) continue;
                var b = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    var p = pivot.InverseTransformPoint(mf.transform.TransformPoint(c));
                    min = Vector3.Min(min, p); max = Vector3.Max(max, p);
                    any = true;
                }
            }
            if (!any) { Core.Log.Warning("[Profiles] no mesh under the steering pivot " + pivot.name + ", the rim radius stays 0 and the hands keep the plain grip; set it with 'profile set rimradius'."); return 0f; }
            // Scale to world metres along each pivot axis.
            Vector3 size = Vector3.Scale(max - min, pivot.lossyScale);
            float a = Mathf.Abs(size.x), b2 = Mathf.Abs(size.y), c2 = Mathf.Abs(size.z);
            float across = a <= b2 && a <= c2 ? Mathf.Max(b2, c2) : b2 <= c2 ? Mathf.Max(a, c2) : Mathf.Max(a, b2);
            if (across < 0.2f || across > 0.8f)
            {
                Core.Log.Warning("[Profiles] steering wheel " + pivot.name + " measures " + ProfileJson.F(size) + " m, no wheel size; the rim radius stays 0 and the hands keep the plain grip.");
                return 0f;
            }
            float r = Mathf.Max(0f, across * 0.5f - thickness * 0.5f);
            Core.Log.Msg("[Profiles] steering wheel " + pivot.name + " measures " + ProfileJson.F(size) + " m, rim radius " + r.ToString("F3") + " m.");
            return r;
        }

        // ---- first person ------------------------------------------------------------------------------------------

        /// <summary>The local player looks through the eyes of their seated body (key V).</summary>
        public static bool FirstPersonActive => FirstPersonView.Active;

        /// <summary>The key that switches the first-person view, for a mod's own key hints.</summary>
        public static KeyCode FirstPersonKey => FirstPersonView.Key;

        /// <summary>Raised on this peer with the new state whenever the local first-person view turns on or off.</summary>
        public static event Action<bool> FirstPersonChanged;

        internal static void RaiseFirstPerson(bool on)
        {
            if (FirstPersonChanged == null) return;
            try { FirstPersonChanged(on); }
            catch (Exception e) { Core.Log.Error("[View] a FirstPersonChanged handler failed: " + e); }
        }
    }
}
