using System;
using System.Collections.Generic;
using System.Globalization;
using Chassis.Instruments;
using Chassis.Interior;
using Chassis.Vehicles;

namespace Chassis.Api
{
    /// <summary>One value of a gauge source, and whether this peer knows it.</summary>
    /// <remarks>
    /// A value is not valid when this peer cannot know it: the throttle of a car an NPC drives on a client, the odometer
    /// before the host's first update, a mod source that threw. Gauges hold their last position while a value is not valid.
    /// </remarks>
    public readonly struct GaugeReading
    {
        /// <summary>The value, in the unit of its source; 0 when not valid.</summary>
        public readonly float Value;

        /// <summary>True when this peer knows the value.</summary>
        public readonly bool Valid;

        /// <summary>A reading with a value.</summary>
        public GaugeReading(float value, bool valid) { Value = valid ? value : 0f; Valid = valid; }

        /// <summary>A reading this peer does not know.</summary>
        public static GaugeReading Invalid => new GaugeReading(0f, false);

        /// <summary>The value with two decimals, or "-" when not valid.</summary>
        public override string ToString() => Valid ? Value.ToString("0.##", CultureInfo.InvariantCulture) : "-";
    }

    /// <summary>
    /// Everything Chassis knows about a vehicle's driving state at one moment, from <see cref="ChassisApi.Telemetry"/>.
    /// Each value carries its own validity; see <see cref="GaugeReading"/>.
    /// </summary>
    /// <example>
    /// <code>
    /// var t = ChassisApi.Telemetry(vehicle);
    /// if (t.SpeedKmh.Valid &amp;&amp; t.SpeedKmh.Value > 120f) MelonLogger.Msg("Too fast: " + t.SpeedKmh);
    /// </code>
    /// </example>
    public sealed class VehicleTelemetry
    {
        private readonly LandVehicle _v;

        internal VehicleTelemetry(LandVehicle v)
        {
            _v = v;
            Vehicle = v;
            At = Time.time;
            Speed = Sources.Read(v, "speed");
            SpeedKmh = Sources.Read(v, "speed_kmh");
            Rpm = Sources.Read(v, "rpm");
            Throttle = Sources.Read(v, "throttle");
            Handbrake = Sources.Read(v, "handbrake");
            Brake = Sources.Read(v, "brake");
            Reverse = Sources.Read(v, "reverse");
            Headlights = Sources.Read(v, "lights");
            Indicator = Sources.Read(v, "indicator");
            Hazard = Sources.Read(v, "hazard");
            Steer = Sources.Read(v, "steer");
            Clock = Sources.Read(v, "clock");
            OdometerKm = Sources.Read(v, "odometer_km");
            TripKm = Sources.Read(v, "trip_km");
            Occupied = Sources.Read(v, "occupied");
            Driver = Sources.Read(v, "driver");
        }

        /// <summary>The vehicle the snapshot is of.</summary>
        public LandVehicle Vehicle { get; }

        /// <summary><c>Time.time</c> when the snapshot was taken.</summary>
        public float At { get; }

        /// <summary>Speed in the player's unit (km/h or mph, <see cref="ChassisApi.UsesMph"/>), as the speedometer shows it.</summary>
        public GaugeReading Speed { get; }

        /// <summary>Speed in km/h as the speedometer shows it: vanilla's <c>Speed_Kmh</c> times 1.4, never negative.</summary>
        /// <value>On a passenger's peer it trails the driver's by about 0.4 s.</value>
        public GaugeReading SpeedKmh { get; }

        /// <summary>Estimated engine speed in revolutions per minute: 800 idle up to 6000 at top speed, 0 with nobody aboard.</summary>
        /// <value>Vanilla has no engine model; this follows speed and throttle as vanilla's engine sound does.</value>
        public GaugeReading Rpm { get; }

        /// <summary>Throttle from -1 (reverse) to 1. Synced by Chassis from the driver's peer, up to 10 times a second.</summary>
        public GaugeReading Throttle { get; }

        /// <summary>1 while the handbrake is on, else 0. Synced like <see cref="Throttle"/>.</summary>
        public GaugeReading Handbrake { get; }

        /// <summary>1 while the brakes are on, else 0.</summary>
        public GaugeReading Brake { get; }

        /// <summary>1 while the vehicle drives backwards, else 0.</summary>
        public GaugeReading Reverse { get; }

        /// <summary>1 while the headlights are on, else 0.</summary>
        public GaugeReading Headlights { get; }

        /// <summary>-1 left, 0 off, 1 right; 0 while the hazard lights flash.</summary>
        public GaugeReading Indicator { get; }

        /// <summary>1 while the hazard lights flash, else 0.</summary>
        public GaugeReading Hazard { get; }

        /// <summary>Front wheel angle from -1 (full left) to 1 (full right).</summary>
        public GaugeReading Steer { get; }

        /// <summary>Game time as HHMM, e.g. 1730 for half past five in the afternoon.</summary>
        public GaugeReading Clock { get; }

        /// <summary>Total distance in km, counted by the host with the speedometer's factor and saved with player-owned vehicles.</summary>
        public GaugeReading OdometerKm { get; }

        /// <summary>Distance in km since the last trip reset.</summary>
        public GaugeReading TripKm { get; }

        /// <summary>1 when anyone is aboard (players or NPCs), else 0.</summary>
        public GaugeReading Occupied { get; }

        /// <summary>1 when someone is at the wheel (a player or an NPC), else 0.</summary>
        public GaugeReading Driver { get; }

        /// <summary>Any source by name, read now; also the ones mods registered.</summary>
        /// <param name="source">A name from <see cref="ChassisApi.GaugeSources"/>.</param>
        public GaugeReading Get(string source) => Sources.Read(_v, source);
    }

    /// <summary>A switch in a cockpit was used, from <see cref="ChassisApi.ControlUsed"/> and the handlers of <see cref="ChassisApi.RegisterControlAction"/>.</summary>
    public sealed class ControlUse
    {
        /// <summary>The vehicle the switch is in.</summary>
        public LandVehicle Vehicle { get; internal set; }

        /// <summary>The switch, from the vehicle's profile.</summary>
        public ControlProfile Control { get; internal set; }

        /// <summary>The switch's action, e.g. "lights" or "mymod.siren".</summary>
        public string Action { get; internal set; }

        /// <summary>The new value: 1 for a button, 0 or 1 for a toggle, the knob's new position.</summary>
        public float Value { get; internal set; }

        /// <summary>The player who used it, or null when a mod called <see cref="ChassisApi.UseControl"/>.</summary>
        public Player By { get; internal set; }

        /// <summary>True on the host. Do work that changes the world only where this is set; the handler runs on every peer.</summary>
        public bool IsHost { get; internal set; }
    }

    public static partial class ChassisApi
    {
        // ---- interior ----------------------------------------------------------------------------------------------

        /// <summary>
        /// Builds a mod's own interior into every vehicle that uses the profile <paramref name="code"/>, in place of the
        /// base interior's seats, dashboard and wheel. The lining and the tinted glass of <see cref="InteriorProfile"/>
        /// stay; set <see cref="InteriorProfile.Lining"/> to false or <see cref="InteriorProfile.Glass"/> to null in
        /// <paramref name="configure"/> to drop them. Call it on every peer; the same code again replaces the factory and
        /// rebuilds the vehicles that have one.
        /// </summary>
        /// <param name="code">The profile code, e.g. "shitbox" or a mod's own code from <see cref="Assign"/>.</param>
        /// <param name="build">
        /// Makes the interior for one vehicle. Chassis parents the object to the vehicle's frame and keeps its local
        /// position, rotation and scale, so place it in the frame's space (x right, y up, z forward, metres).
        /// </param>
        /// <param name="configure">
        /// Changes the profile to match the new interior: <see cref="ChassisProfile.Steering"/>,
        /// <see cref="ChassisProfile.Gauges"/>, <see cref="ChassisProfile.Controls"/>, the driver's hands. It runs once on
        /// every profile object of this code: the one there now, the one Chassis fits for a vanilla car the first time
        /// one is seen, and one from a JSON file. Null leaves the profile as it is.
        /// </param>
        /// <example>
        /// An interior from an AssetBundle prefab for the Shitbox, with its own wheel and speedometer:
        /// <code>
        /// ChassisApi.SetInterior("shitbox",
        ///     vehicle => UnityEngine.Object.Instantiate(myCockpitPrefab),
        ///     profile =>
        ///     {
        ///         profile.Steering = new SteeringProfile { Path = "MyCockpit/Wheel", Axis = Vector3.forward, Ratio = 8f, Max = 300f };
        ///         profile.Gauges.Clear();
        ///         profile.Gauges.Add(new GaugeProfile { Name = "speedo", Path = "MyCockpit/SpeedoNeedle", Source = "speed_kmh", Max = 160f });
        ///     });
        /// </code>
        /// </example>
        public static void SetInterior(string code, Func<LandVehicle, GameObject> build, Action<ChassisProfile> configure = null)
        {
            if (string.IsNullOrEmpty(code)) throw new ArgumentException("an interior needs a profile code");
            if (build == null) throw new ArgumentNullException(nameof(build));
            BaseInterior.SetFactory(code, build);
            lock (_configure)
            {
                // Without a callback the profile still loses the base kit's wheel, needles and switches.
                _configure[code] = configure ?? (_ => { });
                _configuredProfiles.RemoveWhere(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
            }
            var p = Get(code);
            Core.Log.Msg("[Interior] '" + code + "' gets an interior from a mod" + (configure != null ? " and a profile change" : "") + (p == null ? "; no profile for it yet" : "") + ".");
            BaseInterior.Rebuild(code);
            NotifyChanged(code);
        }

        /// <summary>Ends <see cref="SetInterior"/>: the vehicles of this code get the base interior again. False when there was none.</summary>
        /// <remarks>Changes a configure callback made to the profile stay until the profile is reloaded (<see cref="Reload"/>).</remarks>
        public static bool RemoveInterior(string code)
        {
            bool had = BaseInterior.RemoveFactory(code);
            lock (_configure) _configure.Remove(code ?? string.Empty);
            if (had) BaseInterior.Rebuild(code);
            return had;
        }

        private static readonly Dictionary<string, Action<ChassisProfile>> _configure = new Dictionary<string, Action<ChassisProfile>>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<ChassisProfile> _configuredProfiles = new HashSet<ChassisProfile>();

        /// <summary>Runs a mod's configure callback once on each profile object of its code.</summary>
        private static void Configure(ChassisProfile p)
        {
            Action<ChassisProfile> configure;
            lock (_configure)
            {
                if (_configure.Count == 0 || _configuredProfiles.Contains(p) || !_configure.TryGetValue(p.Code, out configure)) return;
                _configuredProfiles.Add(p);
            }
            try
            {
                // A profile Chassis fitted to a vanilla car names the base kit's wheel, needles and switches; the mod's
                // interior has none of them.
                if (BaseInterior.IsFitted(p)) { p.Gauges.Clear(); p.Controls.Clear(); p.Steering = null; }
                configure(p);
                Core.Log.Msg("[Interior] '" + p.Code + "' profile changed by the mod that set its interior: " + p.Gauges.Count + " gauge(s), " + p.Controls.Count + " switch(es).");
            }
            catch (Exception e) { Core.Log.Error("[Interior] the configure callback for '" + p.Code + "' failed, the profile may be half changed: " + e); }
        }

        // ---- gauge sources -----------------------------------------------------------------------------------------

        /// <summary>
        /// Every gauge source by name: the built-in ones and those mods registered. A gauge, a switch's
        /// <see cref="ControlProfile.Source"/> and <see cref="VehicleTelemetry.Get"/> read them.
        /// </summary>
        /// <value>
        /// Built in, with their units:
        /// "speed" (km/h or mph by the player's setting), "speed_kmh", "speed_mph", "speed_raw" (vanilla's signed km/h
        /// without the display factor), "rpm" (800 idle to 6000), "throttle" (-1 to 1), "handbrake", "brake",
        /// "reverse", "lights", "hazard" (each 0 or 1), "indicator" (-1, 0, 1), "indicator_left" and
        /// "indicator_right" (1 in the lit half of the flash), "steer" (-1 to 1), "steer_deg" (front wheel degrees),
        /// "clock" (HHMM), "clock_hours" (0 to 12), "clock_minutes" (0 to 60), "odometer" and "trip" (km or miles
        /// by the player's setting), "odometer_km", "trip_km", "occupied" and "driver" (0 or 1).
        /// </value>
        public static IEnumerable<string> GaugeSources
        {
            get
            {
                var names = new List<string>();
                foreach (var d in Sources.All) names.Add(d.Name);
                return names;
            }
        }

        /// <summary>
        /// Adds a gauge source of a mod's own: a fuel level, a battery, a siren state. <paramref name="read"/> runs for
        /// every gauge that shows it, every frame, on every peer; keep it cheap and return the same value on every peer.
        /// NaN or an exception reads as not valid. The same name again replaces the source; a built-in name throws.
        /// </summary>
        /// <param name="name">Your own unique name, e.g. "mymod.fuel".</param>
        /// <param name="read">The value for one vehicle, in a unit of your choice; gauges map it with Min and Max.</param>
        /// <example>
        /// <code>
        /// ChassisApi.RegisterGaugeSource("mymod.fuel", vehicle => MyFuel.LitresIn(vehicle));
        /// profile.Gauges.Add(new GaugeProfile { Name = "fuel", Path = "FuelNeedle", Source = "mymod.fuel", Min = 0f, Max = 40f, From = -60f, To = 60f });
        /// </code>
        /// </example>
        public static void RegisterGaugeSource(string name, Func<LandVehicle, float> read) => Sources.Register(name, read);

        /// <summary>Removes a source a mod registered; false for a built-in or unknown name.</summary>
        public static bool RemoveGaugeSource(string name) => Sources.Remove(name);

        /// <summary>One source's current value for a vehicle.</summary>
        /// <param name="source">A name from <see cref="GaugeSources"/>.</param>
        public static GaugeReading ReadGauge(LandVehicle vehicle, string source) => Sources.Read(vehicle, source);

        /// <summary>A snapshot of everything Chassis knows about the vehicle's driving state right now.</summary>
        public static VehicleTelemetry Telemetry(LandVehicle vehicle)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            return new VehicleTelemetry(vehicle);
        }

        /// <summary>True when the player set speeds and distances to miles (preference <c>SpeedUnit</c> = mph).</summary>
        public static bool UsesMph => Sources.Mph;

        /// <summary>Sets a vehicle's trip distance back to 0 for every peer; the host decides.</summary>
        public static void ResetTrip(LandVehicle vehicle) => Controls.RequestTripReset(vehicle);

        // ---- switches ----------------------------------------------------------------------------------------------

        /// <summary>
        /// Gives a switch action of a mod's own a handler. Switches whose <see cref="ControlProfile.Action"/> has this
        /// name call it on every peer once the host passed the use on; do world changes only where
        /// <see cref="ControlUse.IsHost"/> is set. Chassis keeps and saves the state of toggles and knobs itself; read it
        /// with <see cref="ControlValue"/>. The same name again replaces the handler.
        /// </summary>
        /// <param name="action">Your own unique name, e.g. "mymod.siren"; built-in names throw.</param>
        /// <example>
        /// <code>
        /// ChassisApi.RegisterControlAction("mymod.siren", use =>
        /// {
        ///     if (use.Value > 0.5f) MySiren.Start(use.Vehicle); else MySiren.Stop(use.Vehicle);
        /// });
        /// </code>
        /// </example>
        public static void RegisterControlAction(string action, Action<ControlUse> handler) => Controls.RegisterAction(action, handler);

        /// <summary>Removes a mod's switch action handler; false when there was none.</summary>
        public static bool RemoveControlAction(string action) => Controls.RemoveAction(action);

        /// <summary>
        /// Uses a switch as if the local player clicked it, without the reach check: a button fires, a toggle flips, a
        /// knob goes to <paramref name="value"/> (or back to its default when that is null).
        /// </summary>
        /// <param name="control">A <see cref="ControlProfile.Name"/> from the vehicle's profile.</param>
        /// <param name="value">Knob: the new value. Toggle: 0 or 1, or null to flip. Ignored for a button.</param>
        public static void UseControl(LandVehicle vehicle, string control, float? value = null) => Controls.UseFromApi(vehicle, control, value);

        /// <summary>A switch's state: 0 or 1 for a toggle, the knob's value, 0 for a button.</summary>
        /// <remarks>A switch with a <see cref="ControlProfile.Source"/> reports that source's value.</remarks>
        public static float ControlValue(LandVehicle vehicle, string control) => Controls.ValueOf(vehicle, control);

        /// <summary>Raised on every peer after any switch was used, built-in actions included.</summary>
        public static event Action<ControlUse> ControlUsed;

        internal static void RaiseControlUsed(ControlUse use)
        {
            if (ControlUsed == null) return;
            try { ControlUsed(use); }
            catch (Exception e) { Core.Log.Error("[Controls] a ControlUsed handler failed: " + e); }
        }

        // ---- driving events ----------------------------------------------------------------------------------------

        /// <summary>Raised on every peer when a vehicle's headlights go on (true) or off (false).</summary>
        public static event Action<LandVehicle, bool> HeadlightsChanged;

        /// <summary>Raised on every peer when a vehicle's brakes go on (true) or off (false).</summary>
        public static event Action<LandVehicle, bool> BrakeChanged;

        /// <summary>Raised on every peer when a vehicle starts (true) or stops (false) driving backwards.</summary>
        public static event Action<LandVehicle, bool> ReverseChanged;

        /// <summary>Raised on every peer when someone takes the wheel (true) or the driver's seat empties (false); players and NPCs.</summary>
        public static event Action<LandVehicle, bool> DriverChanged;

        internal static bool HasDrivingWatchers => HeadlightsChanged != null || BrakeChanged != null || ReverseChanged != null || DriverChanged != null;

        internal static void RaiseWatch(int which, LandVehicle v, bool on)
        {
            Action<LandVehicle, bool> e = which == 0 ? HeadlightsChanged : which == 1 ? BrakeChanged : which == 2 ? ReverseChanged : DriverChanged;
            if (e == null) return;
            try { e(v, on); }
            catch (Exception ex) { Core.Log.Error("[Instruments] a " + (which == 0 ? "HeadlightsChanged" : which == 1 ? "BrakeChanged" : which == 2 ? "ReverseChanged" : "DriverChanged") + " handler failed: " + ex); }
        }
    }
}
