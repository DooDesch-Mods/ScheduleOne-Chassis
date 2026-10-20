# Changelog

All notable changes to Chassis are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/), and this
project adheres to Semantic Versioning.

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
