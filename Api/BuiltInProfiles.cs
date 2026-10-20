using System.Collections.Generic;

namespace Chassis.Api
{
    /// <summary>Profiles Chassis ships with. JSON files and other mods can replace them per vehicle code.</summary>
    internal static class BuiltInProfiles
    {
        internal static IEnumerable<ChassisProfile> All()
        {
            yield return WheelchairProfile();
        }

        /// <summary>
        /// The wheelchair, in the space of the placed wheelchair prop (scaled as in the scene). The values come from the
        /// approved look: hands at the start of a push stroke, feet on the footrests.
        /// </summary>
        internal static ChassisProfile WheelchairProfile() => new ChassisProfile
        {
            Code = Rideables.WheelchairType.Code,
            Frame = Rideables.WheelchairType.FrameName,
            Seats =
            {
                new SeatProfile
                {
                    Index = 0,
                    Root = new Vector3(0f, -0.099f, -0.12f),
                    Eyes = new Vector3(0f, 1.34f, 0.1f),
                    Pose = "Sitting",
                    Hands = new HandProfile
                    {
                        Mode = "rim",
                        LeftWheel = Rideables.WheelchairType.RimLeft,
                        RightWheel = Rideables.WheelchairType.RimRight,
                        GripStart = new Vector3(0.04f, 0.26f, 0.15f),
                        GripEnd = new Vector3(0.04f, 0.15f, 0.27f),
                        Recovery = 0.35f,
                        Elbow = new Vector3(0.55f, 0.85f, -0.4f),
                        LeanStart = new Vector3(0f, 1.15f, 0.18f), LeanStartWeight = 0.35f,
                        LeanEnd = new Vector3(0f, 1.12f, 0.24f), LeanEndWeight = 0.45f,
                    },
                    Feet = new FootProfile
                    {
                        Position = new Vector3(0.165f, 0.17f, 0.46f),
                        Rotation = new Vector3(-92f, 180f, 0f),
                        RotationWeight = 1f,
                        Knee = new Vector3(0.14f, 0.75f, 0.9f),
                    },
                },
            },
            Push = new PushProfile
            {
                Stand = new Vector3(0f, 0f, -0.95f),
                Handle = new Vector3(0.2f, 0.95f, -0.45f),
                Elbow = new Vector3(0.45f, 0.9f, -1.2f),
                Reach = 1.2f,
            },
        };
    }
}
