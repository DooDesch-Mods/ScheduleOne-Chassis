# Changelog

All notable changes to Chassis are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/), and this
project adheres to Semantic Versioning.

## [1.3.0] - 2026-10-21

### Changed
- The drivable wheelchair moved into its own mod, [Rollin](https://github.com/DooDesch-Mods/ScheduleOne-Rollin). Install
  Rollin to keep riding and pushing wheelchairs.
- A repaint at Marco's replaces paint that a tuning mod gave the car.

### For mod authors
- Tuning API: `RegisterTuningPart`, `InstallPart` and `RemovePart` for parts with their own model and driving factors.
- Built-in parts: engine, brakes and transmission stages 1-3, suspension 1-4, turbo, drift tyres.
- `SetPaint` (primary, secondary, finish), `SetRimColor`, `SetXenon`, `SetNeon`, `SetWindowTint`, `SetTyreSmoke` and
  `SetHorn`. `SetTuning` and `GetTuning` store keys of your own mod.
- The host checks each change against the owner, syncs it and saves it with the car. `TuningStatsOf` and
  `TuningChanged` read it.
- `PoseAsPusher`, `ReleasePusher` and `PushedBy` let a player push a vehicle from behind, as Rollin's wheelchair does.

## [1.2.0] - 2026-10-21

### Added
- The speedometer and rev counter in the vanilla cars move, dash lamps show indicators and headlights. `SpeedUnit` sets
  `kmh` or `mph`.
- In first person, look at a dash switch and left-click it: lights, hazard lights, indicator stalk (also mouse wheel),
  horn. Switches click, indicators tick.
- A dot in the screen centre shows where you look while your view is below the windscreen, with the switch's name.
- The vanilla cars flash their own amber indicator lamps outside, at the rear part of the brake light in red where a car
  has none.
- Guns hit NPCs in cars. A driver who goes down falls out and the car stops.

### Fixed
- Vehicle owners and locks are no longer lost when the game saves.
- Seats and the driver's legs no longer stick out under the cars.
- The steering wheel no longer spins faster than the hands could turn it.
- Getting in crouched no longer leaves you crouched in the seat.

### For mod authors
- `ChassisApi.SetInterior` builds your own interior; `ChassisProfile.Gauges` and `ChassisProfile.Controls` bind
  needles, lamps, digits and clickable switches.
- `ChassisApi.Telemetry`, `ReadGauge` and `RegisterGaugeSource`: speed, rpm, pedals, lamps, steering, clock, odometer,
  trip, each with a validity flag.
- `RegisterControlAction`, `UseControl`, `ControlUsed` and the events `HeadlightsChanged`, `BrakeChanged`,
  `ReverseChanged`, `DriverChanged`.
- `RegisterOccupantDown` keeps a shot NPC slumped in its seat instead of falling out; `RemoveOccupantDown` removes it.
- `chassis telemetry` and `chassis zones` work in the release build. The GitHub release also carries
  `Chassis_1.2.0_debug.zip` with the developer console commands.

## [1.1.0] - 2026-10-20

### Added
- The key hints at the bottom left show the vehicle's keys: V First Person, G Horn, and the arrow keys for
  Indicators on vehicles with indicator lamps.
- Behind a wheelchair the key hints show Push and Let Go, with your own key bindings.

### Fixed
- NPCs in low cars like Sam's and Michael's Coupe drive off again instead of sitting still at the curb.

### For mod authors
- `ChassisApi.AddKeyHint` puts your own key into the key hints, a fixed key or one from your preferences.
  `RemoveKeyHint` takes it out.
- The `show` callback gets a `KeyHintContext` with `Vehicle`, `Profile`, `Seat`, `Driver` and `Pushing`.

## [1.0.1] - 2026-10-20

### Changed
- Chassis has its own icon in Thunderstore and r2modman.

### For mod authors
- Every type and member is listed in the [API reference](https://docs.doodesch.de/mods/chassis/api/), linked from
  the README.

## [1.0.0] - 2026-10-20

### Added
- You sit visibly in the driver's seat with both hands on the wheel, and the wheel turns as you steer. Co-op
  players see it too.
- Press V in a vehicle to switch to first person.
- G sounds the horn. The left and right arrow keys set the indicators on vehicles that have indicator lamps.
- Vanilla cars get seats, a dashboard, a steering wheel and tinted glass. NPC drivers sit visibly, police included.
  `BaseInterior` turns both off.

### For mod authors
- `ChassisApi.Register` adds a vehicle profile from code; `FitSeat` builds a seat profile from the vanilla seat.
- `Honk`, `SetIndicator`, `SetLocked`, `SetOwner`, `SetPart` and `SetStorageEnd` act on a `LandVehicle` and sync in
  co-op.
- `FirstPersonActive`, `FirstPersonChanged`, `IndicatorChanged` and `OwnersChanged` report state to your mod.
