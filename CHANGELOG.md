# Changelog

All notable changes to Chassis are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/), and this
project adheres to Semantic Versioning.

## [1.12.1] - 2026-11-10

### Fixed

- An officer who wants to search you in a car no longer starts a chase at once. He pulls you over, walks to your
  window and searches you in your seat. Driving off still counts as failure to comply.

## [1.12.0] - 2026-11-10

### Added

- Getting into a car starts in the view you had when you last got out. `StartInFirstPerson` starts every seat in
  first person.

### Fixed

- After honking at a police checkpoint, the view no longer jumps to where you got into the car once the officer has
  talked.
- The checkpoint officer no longer walks back and forth when the spot beside the driver door is blocked.

## [1.11.0] - 2026-11-09

### Added

- Use hotbar items in any vehicle seat: eat, drink, smoke and hold weapons. Weapons fire in first person, and trash
  goes out of the window.
- Optional ignition: turn the key beside the wheel (`I`) to switch the engine off and on, or press the auto ignition
  button to drive as before.
- The tag on the ignition key has the colour of the car and changes with it when the car is repainted.
- Honk at a police checkpoint to call the officer to your window. He searches the car while you stay inside.
- Indicator, first person and ignition keys can be changed, and each can get a controller button (`[Chassis]` in
  `MelonPreferences.cfg`).

### Fixed

- Underglow lights the ground as one even strip instead of separate spots.
- The driver no longer shows standing for a moment when getting into a car.

## [1.10.4] - 2026-11-07

### Fixed

- Suspension upgrades lower the car again. Every stage raised the body instead, on every car, most visible on the
  Cheetah.

## [1.10.3] - 2026-11-05

### Fixed

- Cabin light and rear-view mirror no longer poke through the windscreen. Most visible on the Cheetah, the mirror
  corner also showed on every car except the Hotbox.

## [1.10.2] - 2026-11-05

### Fixed

- Side mirror glass follows the rounded shape of the mirror on the Cheetah, Hounddog, Veeper and Hotbox, instead of
  a rectangle with its corners sticking out.

## [1.10.1] - 2026-11-05

### Fixed

- Side mirrors of the Cheetah and the Hounddog show the whole mirror instead of a grey face with a thin strip.
  Reported by MR.Monkey.
- The Veeper's side mirror glass fills its whole mirror.

## [1.10.0] - 2026-11-05

### Added

- Cabin light in the vanilla cars: it fades on when someone gets in or out and while the car stands, and off when you drive.
  - Click the dome lamp to switch between Auto, On and Off.
- Glovebox right of the centre console, reachable from both front seats, also while driving. 2 to 4 slots by car size, saved with the car.
  - `GloveboxOwnStorage = false` makes the lid open the trunk instead, so saves stay vanilla.

## [1.9.2] - 2026-11-03

### Fixed

- With `MirrorShowsOwnCar` on, the side mirrors no longer show the edge of their own housing in the middle of the picture.

## [1.9.1] - 2026-11-03

### Fixed

- The left and right arrow keys switch the indicators in vanilla cars again, and the key hints bottom left list them.

## [1.9.0] - 2026-11-03

### Added

- Every vanilla car has a live rear-view mirror and two side mirrors.
- A Mirrors row in the graphics settings, below Reflections: Off, Rear Only, Rear + Side (default) or + Nearby Cars.
  - + Nearby Cars also shows the mirrors of up to three cars within 15 m, on foot too. Setting: `Mirrors`.

### Changed

- Reflections also sets how sharp the mirrors are.

### Fixed

- Chrome paint now reflects the sky and looks brighter.

## [1.8.3] - 2026-11-02

### Fixed

- Cars keep their windows and interior at a distance, and a hidden car no longer leaves its interior floating.
- NPCs put their umbrella away while driving.

### Changed

- Thunderstore and Nexus Mods may now review the mod files: the license names their package review as an exception.

## [1.8.2] - 2026-11-01

### Fixed
- Less stutter with several chrome cars on Reflections Ultra: they share one refresh rate, and near cars refresh more often than far ones.
- The speed display no longer stays on screen for co-op clients after they leave a vehicle.
- Loading a save several times without restarting the game piles up far less memory for car interiors.

## [1.8.1] - 2026-10-28

### Fixed
- The horn no longer carries across the map: it fades with distance, sounds duller far away and is silent past 80 m.

## [1.8.0] - 2026-10-27

### Added
- New "Reflections" row in the graphics settings, Low to Ultra. High and Ultra give sharper chrome that updates faster.

### Fixed
- Tuned cars no longer reverse almost as fast as they drive forward.
- Lowered suspension now shows on big vehicles like the RV.
- Underglow no longer lights the inside of the car.

### For mod authors
- `ChassisApi.HornClip` returns the sound a vehicle's horn plays.

## [1.7.1] - 2026-10-27

### Fixed
- Window tints are clearly darker; the game's fog washed them out to grey.
- Changing the window tint no longer flashes the stock body colour for a moment.
- Chrome paint looks like chrome and reflects the street around the car, also while it drives; metallic paints look
  metallic.
- Neon lights the ground in an even band along the sills and bumpers, with no tube in sight.

## [1.7.0] - 2026-10-26

### Fixed
- Key hints on Left Ctrl show up at the bottom left: the game has no picture for that key and dropped them.

### For mod authors
- `ChassisApi.PreviewTuning`, `PreviewPart` and `EndPreview` show a part or a paint on this machine only, without
  saving or syncing it.
- `ChassisApi.SetPaintTargets` puts the body paint on your own model instead of the stock body; `Secondary` targets
  take the second paint.
- `ChassisApi.PaintRenderers` paints a model with no car under it, on this machine only.
- `ChassisApi.AddKeyHint` takes any key: one the hint strip has no picture for is drawn on a wide key with its name.

## [1.6.1] - 2026-10-22

### Fixed
- The cheetah's body beside the rear window is no longer see-through.

## [1.6.0] - 2026-10-22

### Fixed
- Cars bought while Steam was not ready at the start are yours again in a lobby: locked cars let you in and the
  Yesla app shows.

### For mod authors
- `SeatProfile.Recline` leans the seated body back about the hips with a straight back; hands, feet and eyes keep
  their points. Console: `chassis profile set recline <degrees>`.

## [1.5.0] - 2026-10-22

### Added
- Drivers hold the steering wheel: both hands close around the rim and turn with it, for you and for NPC drivers.

### For mod authors
- `HandProfile.RimRadius`, `RimThickness`, `RimOffset` and `GripAngle` close the hands around the rim. `FitSeat`
  measures `RimRadius` from the wheel's mesh.

## [1.4.0] - 2026-10-21

### Added
- Hold G to keep the horn sounding; it stops when you let go.
- Steering is calmer at speed: the wheels turn less the faster you go and ease back to straight. `SpeedSteering`
  turns it off.

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
