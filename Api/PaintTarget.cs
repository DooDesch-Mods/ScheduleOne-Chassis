// Copyright (c) 2026 DooDesch. DooDesch API License (see LICENSE.md): you may copy this file unchanged.
namespace Chassis.Api
{
    /// <summary>
    /// One material slot that takes the body paint: a renderer and the index into its materials. Chassis paints
    /// <c>VehicleColor.BodyMeshes</c> unless a mod names its own targets with <see cref="ChassisApi.SetPaintTargets(LandVehicle, System.Collections.Generic.IEnumerable{PaintTarget})"/>.
    /// A <see cref="Secondary"/> slot takes the second paint ("paint2") instead and keeps its own material without one.
    /// A <see cref="Flat"/> slot drops its colour texture while painted (the normal map stays), for a part whose texture
    /// is dark or two-toned: the paint multiplies the texture, so it would show darker than chosen.
    /// </summary>
    public struct PaintTarget
    {
        public Renderer Renderer;
        public int MaterialIndex;
        public bool Secondary;
        public bool Flat;

        public PaintTarget(Renderer renderer, int materialIndex)
        {
            Renderer = renderer;
            MaterialIndex = materialIndex;
            Secondary = false;
            Flat = false;
        }

        public PaintTarget(Renderer renderer, int materialIndex, bool secondary)
        {
            Renderer = renderer;
            MaterialIndex = materialIndex;
            Secondary = secondary;
            Flat = false;
        }
    }
}
