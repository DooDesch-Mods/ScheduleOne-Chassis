// Copyright (c) 2026 DooDesch. DooDesch API License (see LICENSE.md): you may copy this file unchanged.
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
    /// <remarks>
    /// Points are <c>Vector3</c> values in metres in the frame's own axes: x to the vehicle's right, y up, z forward.
    /// Angles are in degrees. Weights run from 0 (the animation decides) to 1 (the profile decides). In the JSON
    /// file a vector is <c>[x, y, z]</c> and a colour is <c>[r, g, b, a]</c> with each channel from 0 to 1.
    /// </remarks>
    /// <example>
    /// A profile for a vehicle a mod builds on a vanilla one, with the driver fitted to the vanilla seat:
    /// <code>
    /// var frame = vehicle.transform.Find("MyModel");
    /// var profile = new ChassisProfile { Code = "mymod_cart", Frame = "MyModel" };
    /// profile.Seats.Add(ChassisApi.FitSeat(vehicle, 0, frame, frame.Find("SteeringWheel")));
    /// ChassisApi.Register(profile);
    /// ChassisApi.Assign(vehicle, "mymod_cart");
    /// </code>
    /// </example>
    public sealed class ChassisProfile
    {
        /// <summary>Vehicle code (<c>LandVehicle.VehicleCode</c>) this profile belongs to.</summary>
        /// <value>
        /// Lower case, matched without case. The vanilla cars are "shitbox", "shitbox_police", "veeper", "bruiser",
        /// "bruiser_police", "dinkler", "hounddog", "cheetah" and "hotbox"; the wheelchair from the mod Rollin is "rollin_wheelchair".
        /// A mod vehicle can use a code of its own and get it with <see cref="ChassisApi.Assign"/>.
        /// <see cref="ChassisApi.Codes"/> lists every code that has a profile.
        /// </value>
        public string Code = string.Empty;

        /// <summary>Path of the model frame below the vehicle root, or empty for the root itself.</summary>
        /// <value>
        /// Transform names joined with "/", as <c>Transform.Find</c> takes them, e.g. "Body/Model". When that path
        /// does not exist, the first transform named like its last part is used. Rollin's wheelchair uses "ChassisModel";
        /// the vanilla cars leave it empty.
        /// </value>
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

        /// <summary>The base interior Chassis builds into a vehicle without one, or null for none.</summary>
        public InteriorProfile Interior;

        /// <summary>Needles, lamps and digit displays in the cockpit.</summary>
        public List<GaugeProfile> Gauges = new List<GaugeProfile>();

        /// <summary>Switches a seated player can use in first person.</summary>
        public List<ControlProfile> Controls = new List<ControlProfile>();

        /// <summary>The switch with this name (case ignored), or null.</summary>
        public ControlProfile Control(string name)
        {
            for (int i = 0; i < Controls.Count; i++)
                if (Controls[i] != null && string.Equals(Controls[i].Name, name, System.StringComparison.OrdinalIgnoreCase)) return Controls[i];
            return null;
        }

        /// <summary>The seat entry for this <c>LandVehicle.Seats</c> index, or null when the seat shows no body.</summary>
        public SeatProfile Seat(int index)
        {
            for (int i = 0; i < Seats.Count; i++)
                if (Seats[i] != null && Seats[i].Index == index) return Seats[i];
            return null;
        }

        /// <summary>The part with this name (case ignored), or null.</summary>
        public PartProfile Part(string name)
        {
            for (int i = 0; i < Parts.Count; i++)
                if (Parts[i] != null && string.Equals(Parts[i].Name, name, System.StringComparison.OrdinalIgnoreCase)) return Parts[i];
            return null;
        }

        /// <summary>A deep copy, made through the same JSON the profile file uses.</summary>
        public ChassisProfile Clone() => JsonConvert.DeserializeObject<ChassisProfile>(JsonConvert.SerializeObject(this, ProfileJson.Settings), ProfileJson.Settings);
    }

    /// <summary>One seated body: where the avatar sits, where the first-person camera looks from, and how the hands and feet hold on.</summary>
    public sealed class SeatProfile
    {
        /// <summary>Index into <c>LandVehicle.Seats</c>.</summary>
        /// <value>0 up to the number of vanilla seats minus 1. The driver's seat is the one with <c>isDriverSeat</c> set.</value>
        public int Index;

        /// <summary>Avatar root. The vanilla sit clip puts the hips 0.825 m above it.</summary>
        /// <value>A point in the frame, in metres. With the hips 0.5 m above the frame origin, y is -0.325.</value>
        public Vector3 Root;

        /// <summary>Avatar yaw against the frame, in degrees.</summary>
        /// <value>0 faces the frame's forward axis, 90 its right, 180 backwards.</value>
        public float Yaw;

        /// <summary>How far the whole body leans back, in degrees, turned about the hips; the back stays straight.</summary>
        /// <value>0 sits upright as the sit clip does. 15 suits a low car roof. Hands and feet keep their own points.</value>
        public float Recline;

        /// <summary>Camera position for the first-person view.</summary>
        /// <value>A point in the frame, in metres; about 0.6 m above the hips looks from the avatar's eyes.</value>
        public Vector3 Eyes;

        /// <summary>Animator bool that holds the pose; vanilla has "Sitting".</summary>
        /// <value>"Sitting" is the only seated pose the vanilla avatar has.</value>
        public string Pose = "Sitting";

        /// <summary>Hands, or null to leave the arms to the animation.</summary>
        public HandProfile Hands;

        /// <summary>Feet, or null to leave the legs to the animation.</summary>
        public FootProfile Feet;
    }

    /// <summary>Hand targets of a seated body: on push rims, on the steering wheel or at a fixed grip. Right side; the left is mirrored.</summary>
    public sealed class HandProfile
    {
        /// <summary>
        /// "rim": hands ride the push rims of two wheels. "fixed": hands stay at <see cref="GripStart"/>. "wheel": hands
        /// hold the steering wheel at <see cref="GripStart"/> and turn with it up to <see cref="FollowMax"/> degrees.
        /// </summary>
        public string Mode = "fixed";

        /// <summary>Wheel mode: the hands follow the steering wheel this far either way, then stay.</summary>
        public float FollowMax = 90f;

        /// <summary>Rim mode: name of the left wheel transform in the model.</summary>
        /// <value>A transform name below the frame, e.g. "RimPivot_L" on the wheelchair. Empty outside rim mode.</value>
        public string LeftWheel = string.Empty;

        /// <summary>Rim mode: name of the right wheel transform in the model.</summary>
        /// <value>A transform name below the frame, e.g. "RimPivot_R" on the wheelchair. Empty outside rim mode.</value>
        public string RightWheel = string.Empty;

        /// <summary>Right hand where a push stroke starts. Rim mode: from the wheel centre; fixed mode: in the frame.</summary>
        /// <value>
        /// In metres. Rim mode: offset from the right wheel's centre, e.g. (0.04, 0.26, 0.15) on the wheelchair.
        /// Fixed and wheel mode: a point in the frame; for the wheel, about 0.17 m right of its centre.
        /// </value>
        public Vector3 GripStart;

        /// <summary>Rim mode: right hand where the stroke ends and the hand lets go.</summary>
        /// <value>Offset from the right wheel's centre in metres, e.g. (0.04, 0.15, 0.27) on the wheelchair.</value>
        public Vector3 GripEnd;

        /// <summary>Seconds the hand takes from the end of a stroke back to its start.</summary>
        public float Recovery = 0.35f;

        /// <summary>How strongly the hands turn to the grip, 0 to 1; 0 leaves the wrists to the animation.</summary>
        public float RotationWeight;

        /// <summary>
        /// Wheel mode: radius of the steering wheel's rim, from the steering pivot to the middle of the rim. Above 0 the
        /// hands close around the rim at <see cref="GripAngle"/>: palm on the rim, fingers around it, thumb on the inside,
        /// and <see cref="GripStart"/> and <see cref="RotationWeight"/> are not used. 0 keeps the wrists at
        /// <see cref="GripStart"/> with the fingers as the animation leaves them.
        /// </summary>
        /// <value>In metres, e.g. 0.18 on a car wheel 0.39 m across. <see cref="ChassisApi.FitSeat(LandVehicle, int, Transform, Transform, float)"/> measures it from the wheel's mesh.</value>
        public float RimRadius;

        /// <summary>Wheel mode: thickness of the rim the fingers close around.</summary>
        /// <value>In metres, e.g. 0.03.</value>
        public float RimThickness = 0.03f;

        /// <summary>
        /// Wheel mode: how far the rim's middle sits from the steering pivot along the column, towards the driver, in
        /// metres. 0 when the pivot is the rim's centre; set it when the pivot sits at the hub behind the rim.
        /// </summary>
        public float RimOffset;

        /// <summary>Wheel mode with a <see cref="RimRadius"/>: where the right hand holds the rim, the left one mirrored.</summary>
        /// <value>Degrees from level towards the top of the wheel: 0 holds it at quarter to three, 30 at ten to two, negative below.</value>
        public float GripAngle;

        /// <summary>Elbow bend goal (right side).</summary>
        /// <value>A point in the frame the elbow bends towards, in metres: right of and behind the hand points it outwards.</value>
        public Vector3 Elbow = new Vector3(0.55f, 0.85f, -0.4f);

        /// <summary>Point the spine reaches for at the start of a stroke.</summary>
        public Vector3 LeanStart;

        /// <summary>How strongly the spine reaches for <see cref="LeanStart"/>, 0 to 1.</summary>
        public float LeanStartWeight;

        /// <summary>Point the spine reaches for at the end of a stroke.</summary>
        public Vector3 LeanEnd;

        /// <summary>How strongly the spine reaches for <see cref="LeanEnd"/>, 0 to 1.</summary>
        public float LeanEndWeight;
    }

    /// <summary>Foot targets of a seated body. Right side; the left is mirrored.</summary>
    public sealed class FootProfile
    {
        /// <summary>Right foot target (left mirrored).</summary>
        /// <value>A point in the frame in metres, e.g. (0.165, 0.17, 0.46) puts the foot on the wheelchair's footrest.</value>
        public Vector3 Position;

        /// <summary>
        /// Right foot bone rotation against the frame in degrees (yaw and roll mirrored for the left). The bone's Y axis
        /// runs to the toes and its X axis points to the body's left, so (-90, 180, 0) is a flat foot with the toes
        /// forward and the sole down. (90, 0, 0) points the toes forward too, but with the sole up.
        /// </summary>
        public Vector3 Rotation;

        /// <summary>How strongly the feet turn to <see cref="Rotation"/>, 0 to 1; 0 leaves the ankles to the animation.</summary>
        public float RotationWeight = 1f;

        /// <summary>Knee bend goal (right side).</summary>
        public Vector3 Knee = new Vector3(0.14f, 0.75f, 0.9f);
    }

    /// <summary>Where a second player stands and holds on to push the vehicle. Right side; the left is mirrored.</summary>
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

    /// <summary>A steering wheel in the model that turns with the road wheels.</summary>
    public sealed class SteeringProfile
    {
        /// <summary>Pivot of the steering wheel below the frame; a single name is searched in the whole frame.</summary>
        /// <value>A path like "Dashboard/SteeringWheel" or one name like "SteeringWheel". The base interior's wheel is "Chassis_Wheel".</value>
        public string Path = string.Empty;

        /// <summary>Column axis in the pivot's own space at rest.</summary>
        /// <value>A direction: (0, 0, 1) when the pivot's forward axis runs along the column, (0, 1, 0) when its up axis does.</value>
        public Vector3 Axis = Vector3.forward;

        /// <summary>Steering wheel degrees per degree of road wheel.</summary>
        public float Ratio = 5f;

        /// <summary>The wheel stops at this many degrees either way.</summary>
        public float Max = 170f;

        /// <summary>+1 or -1 for the turning direction; 0 works it out so a right turn moves the top of the wheel right.</summary>
        public float Sign;
    }

    /// <summary>A lid or a wiper in the model that Chassis moves.</summary>
    public sealed class PartProfile
    {
        /// <summary>Name the API and the debug console use, e.g. "bonnet", "boot", "wiper_l".</summary>
        public string Name = string.Empty;

        /// <summary>"lid": opens on request or with the storage. "wiper": sweeps while it rains and the vehicle runs.</summary>
        public string Kind = "lid";

        /// <summary>Pivot below the frame at the hinge or axle; a single name is searched in the whole frame.</summary>
        /// <value>A path like "Body/BootHinge" or one name like "BootHinge".</value>
        public string Path = string.Empty;

        /// <summary>Hinge or axle in the pivot's own space at rest.</summary>
        /// <value>A direction: on a pivot turned like the car, (1, 0, 0) for a bonnet or boot hinged across it and (0, 0, 1) for a wiper axle facing forward.</value>
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

    /// <summary>Indicator lamps in the model.</summary>
    public sealed class LampProfile
    {
        /// <summary>Names of the left indicator lamp objects. A name also matches "name.001" and every renderer below it.</summary>
        /// <value>Transform names in the model, for example ["Indicator_FL", "Indicator_RL"] for a front and a rear lamp.</value>
        public List<string> Left = new List<string>();

        /// <summary>Names of the right indicator lamp objects, matched as <see cref="Left"/>.</summary>
        public List<string> Right = new List<string>();

        /// <summary>Glow colour while a lamp flashes.</summary>
        /// <value>Channels from 0 to 1; the default (1, 0.42, 0) is amber. In JSON: [1, 0.42, 0, 1].</value>
        public Color Color = new Color(1f, 0.42f, 0f);

        /// <summary>The glow takes the lamp's own texture as its pattern; off, the whole lamp glows in <see cref="Color"/>.</summary>
        public bool UseTexture;
    }

    /// <summary>Horn and lock chirp sounds of a vehicle.</summary>
    public sealed class HornProfile
    {
        /// <summary>Sound name from <see cref="ChassisApi.RegisterSound"/>; empty is the built-in two-tone horn.</summary>
        /// <value>The name a mod passed to <see cref="ChassisApi.RegisterSound"/>, e.g. "mymod_horn".</value>
        public string Clip = string.Empty;

        /// <summary>Sound name of the lock chirp; empty is the built-in chirp.</summary>
        /// <value>The name a mod passed to <see cref="ChassisApi.RegisterSound"/>, e.g. "mymod_chirp".</value>
        public string Chirp = string.Empty;

        /// <summary>Where the sound comes from, in the vehicle root's space.</summary>
        /// <value>A point in metres; the default (0, 0.6, 1.8) is at the front of a car.</value>
        public Vector3 Position = new Vector3(0f, 0.6f, 1.8f);

        /// <summary>Volume of horn and chirp, 0 to 1.</summary>
        public float Volume = 0.8f;
    }

    /// <summary>
    /// The base interior: seats, a dashboard and a steering wheel built from simple shapes, the inside of the body
    /// shell drawn as a dark lining, and see-through tinted windows. Points are in the profile's frame.
    /// </summary>
    public sealed class InteriorProfile
    {
        /// <summary>Kit pieces: "seat", "bench", "dash", "gauges", "wheel".</summary>
        public List<KitPart> Parts = new List<KitPart>();

        /// <summary>Draws the inside of the body shell, so the cabin is closed when seen through the glass.</summary>
        public bool Lining = true;

        /// <summary>Lining colour.</summary>
        public Color LiningColor = new Color(0.16f, 0.16f, 0.17f);

        /// <summary>Cuts the flat lid some vanilla bodies close the cabin with at the belt line out of the body mesh.</summary>
        public bool OpenLid;

        /// <summary>The windows, or null to keep them as they are.</summary>
        public GlassProfile Glass = new GlassProfile();
    }

    /// <summary>One simple shape of the base interior.</summary>
    public sealed class KitPart
    {
        /// <summary>
        /// "seat" (cushion and backrest), "bench" (a seat across several places), "dash", "gauges" (the hood over the
        /// dials), "wheel", and the instrument pieces "needle", "rocker" (a switch that tips), "button" (a round push
        /// button facing back), "stalk" (a lever pointing left) and "lamp" (a small block a lamp gauge lights).
        /// </summary>
        public string Kind = "seat";

        /// <summary>Name of the part's object, so gauges, switches and other parts can find it; empty names it by kind.</summary>
        /// <value>A transform name such as "Chassis_SpeedNeedle". The wheel is "Chassis_Wheel" unless named otherwise.</value>
        public string Name = string.Empty;

        /// <summary>Name of another kit part this one hangs below and moves with, or empty for none.</summary>
        /// <value>"Chassis_Wheel" puts a horn button on the steering wheel. <see cref="Position"/> stays in the frame either way.</value>
        public string Parent = string.Empty;

        /// <summary>
        /// Seat and bench: middle of the cushion top. Dash: middle of the top edge at the windscreen. Gauges: middle of the
        /// back bottom edge. Wheel: centre of the rim. Needle: its pivot. Rocker, lamp: its centre. Button: the middle of
        /// its back. Stalk: where it meets the column.
        /// </summary>
        public Vector3 Position;

        /// <summary>Euler angles in the frame. A wheel's column runs along its local forward axis.</summary>
        public Vector3 Rotation;

        /// <summary>
        /// Seat and bench: width, backrest height, cushion depth. Dash and gauges: width, height, depth. Wheel: rim
        /// radius, -, column length. Needle: width, length, thickness. Rocker and lamp: width, height, depth. Button:
        /// radius, -, depth. Stalk: length, thickness, -.
        /// </summary>
        public Vector3 Size = new Vector3(0.5f, 0.6f, 0.5f);

        /// <summary>Colour of the shape.</summary>
        public Color Color = new Color(0.2f, 0.2f, 0.22f);
    }

    /// <summary>Which materials are windows and how they are tinted.</summary>
    public sealed class GlassProfile
    {
        /// <summary>Window materials are the ones whose name starts with one of these.</summary>
        /// <value>Material name prefixes; the vanilla cars use "window_opaque".</value>
        public List<string> Materials = new List<string> { "window_opaque" };

        /// <summary>Glass colour; alpha is how much of the cabin it hides.</summary>
        /// <value>Channels from 0 to 1; alpha 0 is clear glass, 1 hides the cabin. In JSON: [0.2, 0.24, 0.28, 0.55].</value>
        public Color Tint = new Color(0.2f, 0.24f, 0.28f, 0.55f);
    }
}
