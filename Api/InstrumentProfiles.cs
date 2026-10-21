// Copyright (c) 2026 DooDesch. DooDesch API License (see LICENSE.md): you may copy this file unchanged.
namespace Chassis.Api
{
    /// <summary>
    /// One display in the cockpit: a needle that turns with a value, a lamp that glows above a threshold, or digits.
    /// It reads a source by name (<see cref="ChassisApi.GaugeSources"/>) and moves a transform of the vehicle's model or
    /// interior. Chassis draws gauges on every peer, for vehicles near the camera.
    /// </summary>
    /// <example>
    /// A speedometer needle that sweeps from 0 to 160 km/h across 270 degrees:
    /// <code>
    /// profile.Gauges.Add(new GaugeProfile { Name = "speedo", Kind = "needle", Path = "Dash/SpeedoNeedle", Source = "speed_kmh", Min = 0f, Max = 160f });
    /// </code>
    /// </example>
    public sealed class GaugeProfile
    {
        /// <summary>Name the debug output and warnings use, e.g. "speedo".</summary>
        public string Name = string.Empty;

        /// <summary>"needle", "lamp" or "digits".</summary>
        /// <value>
        /// "needle" turns the transform at <see cref="Path"/> from <see cref="From"/> to <see cref="To"/> degrees about
        /// <see cref="Axis"/>. "lamp" makes every renderer below it glow in <see cref="Color"/> while the value is above
        /// <see cref="Threshold"/>. "digits" puts a text below it that shows the value in <see cref="Format"/>.
        /// </value>
        public string Kind = "needle";

        /// <summary>The transform to move, light or write on, below the vehicle's frame.</summary>
        /// <value>
        /// A path like "Chassis_Interior/Dash/SpeedoNeedle", or one name like "SpeedoNeedle" that is searched in the whole
        /// frame. A needle's transform should sit at the pivot of the needle. Chassis logs one warning per vehicle code
        /// when the path is not found.
        /// </value>
        public string Path = string.Empty;

        /// <summary>The value the gauge shows, by source name.</summary>
        /// <value>
        /// A built-in source such as "speed", "rpm", "lights" or "clock_hours", or a name a mod registered with <see cref="ChassisApi.RegisterGaugeSource"/>. The full list with units is on
        /// <see cref="ChassisApi.GaugeSources"/>.
        /// </value>
        public string Source = "speed";

        /// <summary>Needle: the value at <see cref="From"/>. Values outside Min and Max stop at the end of the dial.</summary>
        /// <value>In the unit of the source, e.g. 0 for a speedometer.</value>
        public float Min;

        /// <summary>Needle: the value at <see cref="To"/>.</summary>
        /// <value>In the unit of the source, e.g. 160 for a speedometer that ends at 160 km/h.</value>
        public float Max = 1f;

        /// <summary>Needle: angle at <see cref="Min"/>, in degrees about <see cref="Axis"/> from the needle's rest pose.</summary>
        /// <value>With the default axis (0, 0, -1) on a needle that points up at rest, -135 points to the lower left as the driver sees it.</value>
        public float From = -135f;

        /// <summary>Needle: angle at <see cref="Max"/>, in degrees.</summary>
        /// <value>135 points to the lower right with the default axis; From -135 to To 135 sweeps 270 degrees clockwise.</value>
        public float To = 135f;

        /// <summary>Needle: the turning axis in the needle transform's own space.</summary>
        /// <value>
        /// A direction. (0, 0, -1) turns clockwise as seen by a driver who looks along the vehicle's forward axis at a
        /// dial that faces back towards them.
        /// </value>
        public Vector3 Axis = new Vector3(0f, 0f, -1f);

        /// <summary>Needle and digits: seconds the shown value takes to follow the source; 0 follows at once.</summary>
        public float Smooth = 0.12f;

        /// <summary>Lamp: glows while the value is above this.</summary>
        /// <value>0.5 suits the on/off sources, which read 0 or 1.</value>
        public float Threshold = 0.5f;

        /// <summary>Lamp: glow colour. Digits: text colour.</summary>
        /// <value>Channels from 0 to 1. In JSON: [1, 0.42, 0, 1] for amber.</value>
        public Color Color = new Color(1f, 0.42f, 0f);

        /// <summary>Digits: number format of the value.</summary>
        /// <value>
        /// A .NET numeric format in invariant culture: "0" for whole numbers, "0.0" for one decimal, "000000" for an
        /// odometer with leading zeros. The source "clock" formats as "00:00" by itself.
        /// </value>
        public string Format = "0";

        /// <summary>Digits: height of one line of text in metres.</summary>
        /// <value>0.02 to 0.03 reads well on a dashboard at arm's length.</value>
        public float Size = 0.025f;
    }

    /// <summary>
    /// One switch in the cockpit a seated player can use in first person: look at it and click, or turn a knob with
    /// the mouse wheel. Everyone within reach (about 0.9 m from the eyes) can use it; the click goes through the host to
    /// every peer, and Chassis keeps and saves the state of toggles and knobs.
    /// </summary>
    /// <example>
    /// A toggle for the headlights and a button for a mod's own action:
    /// <code>
    /// profile.Controls.Add(new ControlProfile { Name = "lights", Kind = "toggle", Path = "Dash/LightSwitch", Action = "lights", Source = "lights" });
    /// profile.Controls.Add(new ControlProfile { Name = "siren", Kind = "button", Path = "Dash/SirenButton", Action = "mymod.siren" });
    /// </code>
    /// </example>
    public sealed class ControlProfile
    {
        /// <summary>Name of the switch, unique in the profile; the saved state and <see cref="ChassisApi.ControlValue"/> use it.</summary>
        public string Name = string.Empty;

        /// <summary>The name the player reads under the centre dot while looking at the switch.</summary>
        /// <value>For example "Hazard lights". Empty shows <see cref="Name"/> with a capital first letter and spaces for "_".</value>
        public string Label = string.Empty;

        /// <summary>"button", "toggle" or "knob".</summary>
        /// <value>
        /// "button" fires its action on a click and springs back. "toggle" flips between 0 and 1 on a click. "knob" steps
        /// between <see cref="Min"/> and <see cref="Max"/> with the mouse wheel; a click puts it back to <see cref="Default"/>.
        /// </value>
        public string Kind = "button";

        /// <summary>The transform that moves and that the player aims at, below the vehicle's frame.</summary>
        /// <value>A path or one name, searched as <see cref="GaugeProfile.Path"/>. Its renderers give the area to aim at unless <see cref="BoxSize"/> is set.</value>
        public string Path = string.Empty;

        /// <summary>What the switch does.</summary>
        /// <value>
        /// A built-in action: "lights" (headlights on/off), "hazard" (hazard lights on/off), "indicator" (a knob from -1
        /// left to 1 right), "indicator_left", "indicator_right", "horn" or "trip_reset". Any other name is a mod's own
        /// action from <see cref="ChassisApi.RegisterControlAction"/>; empty does nothing but keep the state.
        /// </value>
        public string Action = string.Empty;

        /// <summary>A source whose value the switch shows instead of its own state, or empty.</summary>
        /// <value>
        /// For switches whose state can change without them: "lights" for a light switch (the light key also switches),
        /// "hazard", "indicator". Any source from <see cref="ChassisApi.GaugeSources"/> works.
        /// </value>
        public string Source = string.Empty;

        /// <summary>The axis the switch turns about (toggle, knob) or moves along (button), in its own space.</summary>
        /// <value>A direction, e.g. (1, 0, 0) for a rocker switch that tips forward and back, (0, 0, 1) for a button pushed forward.</value>
        public Vector3 Axis = Vector3.right;

        /// <summary>Toggle: degrees between off and on. Knob: degrees from <see cref="Min"/> to <see cref="Max"/>.</summary>
        /// <value>The rest pose is the middle: a toggle stands at -Angle/2 off and +Angle/2 on.</value>
        public float Angle = 30f;

        /// <summary>Button: how far it moves along <see cref="Axis"/> while pressed, in metres.</summary>
        public float Press = 0.004f;

        /// <summary>Knob: lowest value.</summary>
        public float Min;

        /// <summary>Knob: highest value.</summary>
        public float Max = 1f;

        /// <summary>Knob: change per notch of the mouse wheel.</summary>
        public float Step = 0.1f;

        /// <summary>Toggle and knob: the value before anyone touched it, and a knob's value after a click.</summary>
        public float Default;

        /// <summary>Centre of the area to aim at, in the switch transform's own space; used when <see cref="BoxSize"/> is set.</summary>
        public Vector3 BoxCenter;

        /// <summary>Size of the area to aim at in metres, in the switch transform's own space; zero uses its renderers.</summary>
        /// <value>Set it for a switch with no renderer of its own, or to make a small one easier to hit.</value>
        public Vector3 BoxSize;
    }
}
