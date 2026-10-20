using System;
using Chassis.Seating;

namespace Chassis.Api
{
    /// <summary>A seated NPC that just died or was knocked out, as <see cref="ChassisApi.RegisterOccupantDown"/> hands it over.</summary>
    public sealed class OccupantDown
    {
        /// <summary>The vehicle the NPC sits in.</summary>
        public LandVehicle Vehicle;
        /// <summary>The NPC, about to die or be knocked out; its health still reads the moment before.</summary>
        public NPC Npc;
        /// <summary>Index into <c>Vehicle.Seats</c>; 0 is the driver.</summary>
        public int Seat;
        /// <summary>True when the NPC died, false when it was knocked out.</summary>
        public bool Dead;
    }

    public static partial class ChassisApi
    {
        // ---- seated NPCs that go down ------------------------------------------------------------------------------

        /// <summary>
        /// Decides what happens to an NPC that dies or is knocked out in its seat, for example from a gunshot through the
        /// window. Return true to keep the body slumped in the seat; the car then stops where it is. Return false for
        /// vanilla: the NPC falls out next to the car as a ragdoll. The NPC stays seated when any handler returns true.
        /// Runs on the host only; clients follow the host. A knocked-out NPC that wakes up in its seat sits up again.
        /// </summary>
        /// <param name="name">Your handler's name, e.g. "mymod.gta-style"; registering the same name again replaces it.</param>
        public static void RegisterOccupantDown(string name, Func<OccupantDown, bool> keepSeated) => SeatedTargets.Register(name, keepSeated);

        /// <summary>Removes a handler added with <see cref="RegisterOccupantDown"/>; false when there was none by that name.</summary>
        public static bool RemoveOccupantDown(string name) => SeatedTargets.Remove(name);
    }
}
