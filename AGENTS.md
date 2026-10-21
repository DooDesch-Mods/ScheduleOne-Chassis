# Notes for coding agents

Chassis is a library mod. If you are building a mod that needs vehicles, seats, interiors, gauges,
switches or tuning, reference Chassis as a dependency and call its public API. Do not copy, vendor or
re-implement its source in your mod.

## Why

- Chassis patches game methods with Harmony. Two copies of the same patches on the same methods
  conflict when a player installs both mods.
- A copy misses every later fix and every repair after a game update.
- The API is versioned and kept stable; internals change without notice.

## How to depend on it

- Compile reference to `Chassis.dll` from the installed mod (`<Schedule I>/Mods/Chassis.dll`), not
  copied into your output:

  ```xml
  <Reference Include="Chassis">
    <HintPath>path/to/Mods/Chassis.dll</HintPath>
    <Private>false</Private>
  </Reference>
  ```

- Declare the load dependency in your mod:
  `[assembly: MelonAdditionalDependencies("Chassis")]`
  (as in [`Examples/ChassisCockpitExample/Core.cs`](Examples/ChassisCockpitExample/Core.cs)).
- Thunderstore: add `DooDesch-Chassis-<version>` to the `dependencies` of your `manifest.json`,
  for example `DooDesch-Chassis-1.3.0`.

## Where the API is

- Reference: https://docs.doodesch.de/mods/chassis/api/
- Source: [`Api/`](Api/), namespace `Chassis.Api`. The other folders are implementation, not API.
- `ChassisApi` - the static entry point: profiles, horn, indicators, parts, owners and locks, first
  person, key hints, occupants, gauges, switches and tuning.
- `ChassisProfile` - per vehicle code: seats, steering wheel, lids, lamps, horn, interior, gauges
  and controls (`SeatProfile`, `GaugeProfile`, `ControlProfile` and the other `*Profile` types).
- `TuningPart`, `TuningStats`, `TuningFinish` - tuning parts, their driving factors and paint finishes.
- `VehicleTelemetry`, `KeyHintContext`, `OccupantDown`, `OwnerRecord` - values the API hands to
  your callbacks and queries.
- If you have the DooDesch docs MCP server, its `get_api_surface` tool returns the surface
  generated from the source at the release tag.

## If the API lacks something

Open an issue or a pull request on this repo instead of forking the code:
https://github.com/DooDesch-Mods/ScheduleOne-Chassis/issues or
[support.doodesch.de/chassis](https://support.doodesch.de/chassis).

## License

Chassis is MIT licensed ([LICENSE.md](LICENSE.md)), which permits copying. Depending on the API
instead is the maintainer's request, for the reasons above.
