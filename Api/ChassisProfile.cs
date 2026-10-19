using System.Collections.Generic;
using Newtonsoft.Json;

namespace Chassis.Api
{
    /// <summary>
    /// Where occupants sit and hold on in one vehicle type, keyed by its vehicle code. Every point is in the space of
    /// the vehicle's model frame: the transform at <see cref="Frame"/> under the vehicle, or the vehicle root when
    /// that is empty. Left-side points are the mirror of the right-side values stored here, about the seat's own
    /// centre plane (through the seat root, turned by its yaw); in wheel mode the hands mirror about the steering column.
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

        /// <summary>The steering wheel that turns with the road wheels, or null when the vehicle has none.</summary>
        public SteeringProfile Steering;

        /// <summary>Lids and wipers that move by themselves.</summary>
        public List<PartProfile> Parts = new List<PartProfile>();

        /// <summary>Indicator lamps, or null when the model has none.</summary>
        public LampProfile Lamps;

        /// <summary>Horn and lock chirp sounds; null uses the built-in ones.</summary>
        public HornProfile Horn;

        /// <summary>Headlights switch on by themselves while the vehicle is driven between 17:30 and 06:00.</summary>
        public bool AutoLights;

        public SeatProfile Seat(int index)
        {
            for (int i = 0; i < Seats.Count; i++)
                if (Seats[i] != null && Seats[i].Index == index) return Seats[i];
            return null;
        }

        public PartProfile Part(string name)
        {
            for (int i = 0; i < Parts.Count; i++)
                if (Parts[i] != null && string.Equals(Parts[i].Name, name, System.StringComparison.OrdinalIgnoreCase)) return Parts[i];
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
        /// <summary>
        /// "rim": hands ride the push rims of two wheels. "fixed": hands stay at <see cref="GripStart"/>. "wheel": hands
        /// hold the steering wheel at <see cref="GripStart"/> and turn with it up to <see cref="FollowMax"/> degrees.
        /// </summary>
        public string Mode = "fixed";

        /// <summary>Wheel mode: the hands follow the steering wheel this far either way, then stay.</summary>
        public float FollowMax = 90f;

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

    public sealed class SteeringProfile
    {
        /// <summary>Pivot of the steering wheel below the frame; a single name is searched in the whole frame.</summary>
        public string Path = string.Empty;

        /// <summary>Column axis in the pivot's own space at rest.</summary>
        public Vector3 Axis = Vector3.forward;

        /// <summary>Steering wheel degrees per degree of road wheel.</summary>
        public float Ratio = 5f;

        /// <summary>The wheel stops at this many degrees either way.</summary>
        public float Max = 170f;

        /// <summary>+1 or -1 for the turning direction; 0 works it out so a right turn moves the top of the wheel right.</summary>
        public float Sign;
    }

    public sealed class PartProfile
    {
        /// <summary>Name the API and the debug console use, e.g. "bonnet", "boot", "wiper_l".</summary>
        public string Name = string.Empty;

        /// <summary>"lid": opens on request or with the storage. "wiper": sweeps while it rains and the vehicle runs.</summary>
        public string Kind = "lid";

        /// <summary>Pivot below the frame at the hinge or axle; a single name is searched in the whole frame.</summary>
        public string Path = string.Empty;

        /// <summary>Hinge or axle in the pivot's own space at rest.</summary>
        public Vector3 Axis = Vector3.right;

        /// <summary>Lid: open angle. Wiper: sweep. In degrees.</summary>
        public float Angle = 60f;

        /// <summary>Lid: seconds to open or close. Wiper: seconds per stroke in light rain.</summary>
        public float Seconds = 1.2f;

        /// <summary>Wiper: seconds per stroke in heavy rain.</summary>
        public float FastSeconds = 0.8f;

        /// <summary>+1 or -1 for the turning direction; 0 works it out so the part lifts.</summary>
        public float Sign;

        /// <summary>Lid: opens while the vehicle's storage is open from this end, "front" or "rear"; empty never.</summary>
        public string Storage = string.Empty;
    }

    public sealed class LampProfile
    {
        /// <summary>Names of the left and right indicator lamp objects. A name also matches "name.001" and every renderer below it.</summary>
        public List<string> Left = new List<string>();
        public List<string> Right = new List<string>();

        /// <summary>Glow colour while a lamp flashes.</summary>
        public Color Color = new Color(1f, 0.42f, 0f);

        /// <summary>The glow takes the lamp's own texture as its pattern; off, the whole lamp glows in <see cref="Color"/>.</summary>
        public bool UseTexture;
    }

    public sealed class HornProfile
    {
        /// <summary>Sound name from <see cref="ChassisApi.RegisterSound"/>; empty is the built-in two-tone horn.</summary>
        public string Clip = string.Empty;

        /// <summary>Sound name of the lock chirp; empty is the built-in chirp.</summary>
        public string Chirp = string.Empty;

        /// <summary>Where the sound comes from, in the vehicle root's space.</summary>
        public Vector3 Position = new Vector3(0f, 0.6f, 1.8f);

        public float Volume = 0.8f;
    }
}
