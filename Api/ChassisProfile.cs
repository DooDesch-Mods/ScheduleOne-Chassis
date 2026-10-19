using System.Collections.Generic;
using Newtonsoft.Json;

namespace Chassis.Api
{
    /// <summary>
    /// Where occupants sit and hold on in one vehicle type, keyed by its vehicle code. Every point is in the space of
    /// the vehicle's model frame: the transform at <see cref="Frame"/> under the vehicle, or the vehicle root when
    /// that is empty. Left-side points are the mirror (x negated) of the right-side values stored here.
    ///
    /// The same object backs the JSON file (UserData/Chassis/&lt;code&gt;.json) and the C# API, so a value set from code,
    /// from the file or from the debug console is the same value.
    /// </summary>
    public sealed class ChassisProfile
    {
        /// <summary>Vehicle code (<c>LandVehicle.VehicleCode</c>) this profile belongs to.</summary>
        public string Code = string.Empty;

        /// <summary>Path of the model frame below the vehicle root, or empty for the root itself.</summary>
        public string Frame = string.Empty;

        /// <summary>One entry per <c>LandVehicle.Seats</c> index that should show a seated body.</summary>
        public List<SeatProfile> Seats = new List<SeatProfile>();

        /// <summary>Where a second player stands and holds on to push, or null when the vehicle cannot be pushed.</summary>
        public PushProfile Push;

        public SeatProfile Seat(int index)
        {
            for (int i = 0; i < Seats.Count; i++)
                if (Seats[i] != null && Seats[i].Index == index) return Seats[i];
            return null;
        }

        public ChassisProfile Clone() => JsonConvert.DeserializeObject<ChassisProfile>(JsonConvert.SerializeObject(this, ProfileJson.Settings), ProfileJson.Settings);
    }

    public sealed class SeatProfile
    {
        /// <summary>Index into <c>LandVehicle.Seats</c>.</summary>
        public int Index;

        /// <summary>Avatar root. The vanilla sit clip puts the hips 0.825 m above it.</summary>
        public Vector3 Root;

        /// <summary>Avatar yaw against the frame, in degrees.</summary>
        public float Yaw;

        /// <summary>Camera position for the first-person view.</summary>
        public Vector3 Eyes;

        /// <summary>Animator bool that holds the pose; vanilla has "Sitting".</summary>
        public string Pose = "Sitting";

        /// <summary>Hands, or null to leave the arms to the animation.</summary>
        public HandProfile Hands;

        /// <summary>Feet, or null to leave the legs to the animation.</summary>
        public FootProfile Feet;
    }

    public sealed class HandProfile
    {
        /// <summary>"rim": hands ride the push rims of two wheels. "fixed": hands stay at <see cref="GripStart"/>.</summary>
        public string Mode = "fixed";

        /// <summary>Rim mode: names of the left and right wheel transforms in the model.</summary>
        public string LeftWheel = string.Empty;
        public string RightWheel = string.Empty;

        /// <summary>Right hand where a push stroke starts. Rim mode: from the wheel centre; fixed mode: in the frame.</summary>
        public Vector3 GripStart;

        /// <summary>Rim mode: right hand where the stroke ends and the hand lets go.</summary>
        public Vector3 GripEnd;

        /// <summary>Seconds the hand takes from the end of a stroke back to its start.</summary>
        public float Recovery = 0.35f;

        public float RotationWeight;

        /// <summary>Elbow bend goal (right side).</summary>
        public Vector3 Elbow = new Vector3(0.55f, 0.85f, -0.4f);

        /// <summary>Point the spine reaches for at the start and the end of a stroke, and how strongly.</summary>
        public Vector3 LeanStart;
        public float LeanStartWeight;
        public Vector3 LeanEnd;
        public float LeanEndWeight;
    }

    public sealed class FootProfile
    {
        /// <summary>Right foot target (left mirrored).</summary>
        public Vector3 Position;

        /// <summary>Right foot bone rotation against the frame in degrees (yaw and roll mirrored for the left).</summary>
        public Vector3 Rotation;

        public float RotationWeight = 1f;

        /// <summary>Knee bend goal (right side).</summary>
        public Vector3 Knee = new Vector3(0.14f, 0.75f, 0.9f);
    }

    public sealed class PushProfile
    {
        /// <summary>Where the pusher's feet stand.</summary>
        public Vector3 Stand;

        /// <summary>Right handle grip (left mirrored).</summary>
        public Vector3 Handle;

        /// <summary>Elbow bend goal of the pusher (right side).</summary>
        public Vector3 Elbow = new Vector3(0.45f, 0.9f, -1.0f);

        /// <summary>Range in metres from the stand point within which a player can take the handles.</summary>
        public float Reach = 1.2f;
    }
}
