# Changelog

All notable changes to Chassis are documented in this file.
The format is based on [Keep a Changelog](https://keepachangelog.com/), and this
project adheres to Semantic Versioning.

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
