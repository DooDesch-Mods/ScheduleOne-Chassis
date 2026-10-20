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
            Core.Log.Msg("[Profiles] fitted seat " + seatIndex + " of " + vehicle.name + ": root " + ProfileJson.F(seat.Root) + " yaw " + seat.Yaw.ToString("F0")
                + " eyes " + ProfileJson.F(seat.Eyes) + ", hands " + seat.Hands.Mode + " at " + ProfileJson.F(seat.Hands.GripStart) + ".");
            return seat;
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
