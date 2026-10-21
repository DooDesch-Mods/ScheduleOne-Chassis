// Copyright (c) 2026 DooDesch. DooDesch API License (see LICENSE.md): you may copy this file unchanged.
using System;
using System.Collections.Generic;

namespace Chassis.Api
{
    /// <summary>
    /// One tuning part: a visual piece (spoiler, bumper, rims), a performance upgrade (engine, brakes), or both. Register
    /// it on every peer with <see cref="ChassisApi.RegisterTuningPart"/>; a car carries at most one part per
    /// <see cref="Slot"/>.
    /// </summary>
    public sealed class TuningPart
    {
        /// <summary>Unique id, saved with the car. Lower case with a mod prefix, e.g. "mymod_gtwing".</summary>
        public string Id;

        /// <summary>
        /// The slot the part goes into: wheels, spoiler, bumper_front, bumper_rear, skirts, hood, grille, exhaust, roof,
        /// fenders, cage, engine, brakes, transmission, suspension, turbo, tyres, or a slot of your own.
        /// </summary>
        public string Slot;

        /// <summary>Name for menus.</summary>
        public string Label;

        /// <summary>Price for a shop. Chassis does not charge it.</summary>
        public int Price;

        /// <summary>
        /// Makes the part's model; Chassis parents it to the car and places it with the car's <see cref="PartFit"/>.
        /// Null for a part that only changes how the car drives. Renderer materials whose name starts with "paint",
        /// "paint2" or "rim" take the car's paint, second paint or rim colour. A "wheels" part gives one rim facing +X;
        /// Chassis puts a copy on every wheel, scaled to the stock rim.
        /// </summary>
        public Func<LandVehicle, GameObject> Build;

        /// <summary>How the part changes driving. Null for a part that only looks different.</summary>
        public TuningStats Stats;

        /// <summary>
        /// Placement per vehicle code. A part with a <see cref="Build"/> fits only the codes listed here, except a
        /// "wheels" part, which fits every car. A part without a model and with no entries fits every car.
        /// </summary>
        public Dictionary<string, PartFit> Fits = new Dictionary<string, PartFit>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Whether this part goes on cars of this code.</summary>
        public bool FitsCode(string code)
        {
            if (Build == null || string.Equals(Slot, "wheels", StringComparison.OrdinalIgnoreCase))
                return Fits == null || Fits.Count == 0 || (code != null && Fits.ContainsKey(code));
            return code != null && Fits != null && Fits.ContainsKey(code);
        }

        /// <summary>The placement on cars of this code, or null.</summary>
        public PartFit FitFor(string code) => code != null && Fits != null && Fits.TryGetValue(code, out var f) ? f : null;
    }

    /// <summary>Where a part sits on one vehicle code, in the car's own space (its root transform).</summary>
    public sealed class PartFit
    {
        public Vector3 Position;
        /// <summary>Euler angles in degrees.</summary>
        public Vector3 Rotation;
        public Vector3 Scale = Vector3.one;

        /// <summary>
        /// Boxes in the car's space. While the part is on, Chassis removes the body triangles whose centre lies inside,
        /// on every level of detail, so the stock bumper or wing does not show through the part.
        /// </summary>
        public List<Bounds> Cut = new List<Bounds>();

        /// <summary>Names of objects under the car that Chassis hides while the part is on (stock parts of a modded car).</summary>
        public List<string> Hide = new List<string>();
    }

    /// <summary>
    /// What a part changes in driving. Factors multiply the car's stock value (1 = stock); the factors of all parts on a
    /// car multiply. <see cref="RideHeight"/> adds up.
    /// </summary>
    public sealed class TuningStats
    {
        /// <summary>Engine torque at every speed.</summary>
        public float Torque = 1f;
        /// <summary>Stretches the torque curve over speed: more pull at high speed, a higher top speed.</summary>
        public float Gearing = 1f;
        /// <summary>Brake and handbrake force.</summary>
        public float Brake = 1f;
        /// <summary>Tyre grip, forwards and sideways. Below 1 the car drifts.</summary>
        public float Grip = 1f;
        /// <summary>Largest steering angle.</summary>
        public float Steer = 1f;
        /// <summary>Weight of the car.</summary>
        public float Mass = 1f;
        /// <summary>Suspension spring stiffness.</summary>
        public float Spring = 1f;
        /// <summary>Suspension damping.</summary>
        public float Damper = 1f;
        /// <summary>Downforce at speed.</summary>
        public float Downforce = 1f;
        /// <summary>Engine sound pitch.</summary>
        public float EnginePitch = 1f;
        /// <summary>Metres the body sits higher (negative: lower).</summary>
        public float RideHeight;

        /// <summary>The product of two sets of stats.</summary>
        public static TuningStats Combine(TuningStats a, TuningStats b)
        {
            if (a == null) return b;
            if (b == null) return a;
            return new TuningStats
            {
                Torque = a.Torque * b.Torque, Gearing = a.Gearing * b.Gearing, Brake = a.Brake * b.Brake, Grip = a.Grip * b.Grip,
                Steer = a.Steer * b.Steer, Mass = a.Mass * b.Mass, Spring = a.Spring * b.Spring, Damper = a.Damper * b.Damper,
                Downforce = a.Downforce * b.Downforce, EnginePitch = a.EnginePitch * b.EnginePitch, RideHeight = a.RideHeight + b.RideHeight,
            };
        }

        public override string ToString()
        {
            var parts = new List<string>();
            void F(string n, float v) { if (Mathf.Abs(v - 1f) > 0.0001f) parts.Add(n + " x" + v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)); }
            F("torque", Torque); F("gearing", Gearing); F("brake", Brake); F("grip", Grip); F("steer", Steer); F("mass", Mass);
            F("spring", Spring); F("damper", Damper); F("downforce", Downforce); F("pitch", EnginePitch);
            if (Mathf.Abs(RideHeight) > 0.0001f) parts.Add("ride " + RideHeight.ToString("+0.###;-0.###", System.Globalization.CultureInfo.InvariantCulture) + " m");
            return parts.Count == 0 ? "stock" : string.Join(", ", parts);
        }
    }

    /// <summary>The paint finishes <see cref="ChassisApi.SetPaint"/> knows.</summary>
    public static class TuningFinish
    {
        public const string Classic = "classic", Metallic = "metallic", Matte = "matte", Metal = "metal", Chrome = "chrome";
        public static readonly string[] All = { Classic, Metallic, Matte, Metal, Chrome };
    }
}
