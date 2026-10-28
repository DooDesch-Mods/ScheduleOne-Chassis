# Chassis - Vehicle Framework for Schedule I

> 🛟 **Need help or found a bug?** Get support at [support.doodesch.de/chassis](https://support.doodesch.de/chassis).

📖 **API documentation:** [docs.doodesch.de/mods/chassis/api/](https://docs.doodesch.de/mods/chassis/api/)

> Drivers and passengers sit in the car where you can see them, with their hands on a steering
> wheel that turns. Chassis adds a first person view, a horn, indicators, locks and owners, and a
> base interior for the vanilla cars. Other mods build their vehicles on it.

![Version](https://img.shields.io/badge/version-1.6.1-blue)
![Game](https://img.shields.io/badge/game-Schedule%20I-purple)
![MelonLoader](https://img.shields.io/badge/MelonLoader-0.7.3+-green)
![Status](https://img.shields.io/badge/status-working-brightgreen)
[![Polyfill](https://polyfill.doomods.com/badge/chassis.svg)](https://polyfill.doomods.com/mod/chassis)

[![MLVScan Chassis.dll](https://mlvscan.com/attestations/att_wRWdBQmujNdtRaX9AMScUxGQ/badge.svg)](https://mlvscan.com/attestations/att_wRWdBQmujNdtRaX9AMScUxGQ)

![Chassis](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-Chassis/main/docs/img/header.jpg)

## Features

- **Visible occupants:** Players and NPCs in a vehicle sit visibly in their seats, police
  included. The driver holds the steering wheel with both hands, and the wheel turns when you
  steer.
- **First person view:** Press `V` in a vehicle to look from your seat. Your body stays visible
  below you, and only your own copy of your head is hidden.
- **Horn:** Press `G`. `H` stays the headlights.
- **Indicators:** The left and right arrow keys switch them on vehicles that have indicator lamps.
  Press the same key again to switch off. After the turn they switch off on their own.
- **Owner and lock per player:** Each player-owned vehicle has an owner, and the owner can lock
  it. A locked vehicle lets in only its owner. Others can ride along while the owner sits
  inside. There is no key for the lock: other mods set it through the API. Vehicles
  from older saves belong to the host.
- **Moving parts:** The steering wheel follows the front wheels.
- **Base interior for the vanilla cars:** Seats, a dashboard, a steering wheel and tinted glass,
  so you can see who sits inside.
- **Working dashboard:** The speedometer and the rev counter move, and lamps show the indicators
  and the headlights. In first person, look at a switch and left-click it: light switch, hazard
  lights, indicator stalk (also with the mouse wheel) and the horn in the wheel centre.
- **Indicators outside:** The vanilla cars flash their own amber indicator lamps. A rear without
  one flashes part of the brake light in red.
- **Shots into cars:** Guns hit NPCs that sit in a car. A driver who goes down falls out next to
  the car, and the car stops.
- **Wheelchairs:** Riding and pushing the wheelchairs moved to its own mod,
  [Rollin](https://github.com/DooDesch-Mods/ScheduleOne-Rollin). Without Rollin the wheelchairs stay vanilla props.
- **Showroom cars:** The display cars at Hyland Auto can be entered before you buy one. The engine
  starts, but they stay on their display. They are not saved and do not belong to anyone.
- **Key hints:** The keys of the vehicle show at the bottom left, next to the game's own: First
  Person, Horn, and Indicators on vehicles with indicator lamps. While you push a vehicle (Rollin's
  wheelchair) they show Push and Let Go with your own key bindings.
- **Co-op:** Seats, horn, indicators, locks and owners sync through the host.

## Controls

| Key | Action |
|---|---|
| `V` | First person view in a vehicle |
| `G` | Horn |
| `H` | Headlights (vanilla) |
| `Left arrow` / `Right arrow` | Indicator left / right. Press again to switch off. |
| `Left click` | In first person: use the switch you look at. A dot in the screen centre shows that you can. |
| `Mouse wheel` | In first person: turn the knob or stalk you look at. |

## Screenshots

| | |
|---|---|
| ![Driver in the seat](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-Chassis/main/docs/img/01-seated-driver.jpg) | ![First person view](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-Chassis/main/docs/img/02-first-person.jpg) |
| ![Base interior](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-Chassis/main/docs/img/03-interior.jpg) | ![NPC driver](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-Chassis/main/docs/img/04-npc-driver.jpg) |
| ![Working dashboard](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-Chassis/main/docs/img/09-dash.jpg) | ![Indicators outside](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-Chassis/main/docs/img/10-indicators.jpg) |
| ![Key hints](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-Chassis/main/docs/img/08-key-hints.jpg) | |

## Requirements

| Component | Version / Source |
|-----------|------------------|
| Schedule I | `0.4.7f12` (IL2CPP, current Steam public build) |
| MelonLoader | [`0.7.3+`](https://github.com/LavaGang/MelonLoader) |

Every player in a co-op session needs Chassis.

## Installation

### Recommended: a Thunderstore mod manager

Install with a mod manager (r2modman / Gale) from the Schedule I community. MelonLoader comes in
as a dependency.

### Manual

1. Install **MelonLoader 0.7.3** for Schedule I ([melonwiki.xyz](https://melonwiki.xyz/)).
2. Drop **`Chassis.dll`** into your Schedule I `Mods/` folder.

## Configuration

Settings live in `UserData/MelonPreferences.cfg` under `Chassis`.

| Setting | Default | What it does |
|---|---|---|
| `BaseInterior` | `true` | Seats, dashboard, steering wheel and tinted glass for the vanilla cars, and visible NPC drivers. |
| `SpeedUnit` | `kmh` | `kmh` or `mph`: the speedometer and the odometer. |

## For mod authors

The public API is the static class `ChassisApi` in the namespace `Chassis.Api`
([`Api/`](Api/)). Reference `Chassis.dll` and add Chassis as a dependency of your package.
Every type and member: [API reference](https://docs.doodesch.de/mods/chassis/api/).
Developers and AI coding agents: read [AGENTS.md](AGENTS.md) - depend on the API, do not copy the source.

- **Profiles:** A `ChassisProfile` per vehicle code holds seats, steering wheel, lids, lamps,
  horn and interior. `ChassisApi.Register` adds one from code, `ChassisApi.Assign` gives one
  vehicle the profile of another code, and `ChassisApi.FitSeat` makes a first seat profile from a
  vanilla seat. `Changed` reports every change.
- **JSON files:** Profiles also load from `UserData/Chassis/<vehicle code>.json`. A file wins
  over a profile from code, so a player can always correct the values. `ChassisApi.Save` writes
  the current profile to its file.
- **Horn, indicators, parts:** `Honk`, `SetIndicator`, `Indicator`, `IndicatorLit`,
  `IndicatorChanged`, `SetPart`, `PartOpen`, `SetStorageEnd`, `RegisterSound`.
- **Owner and lock:** `OwnerOf`, `IsMine`, `IsLocked`, `SetLocked`, `SetOwner` (host),
  `OwnersChanged`, `ResolveUnknownOwner` (host, for vehicles without a record).
- **First person:** `FirstPersonActive`, `FirstPersonKey`, `FirstPersonChanged`.
- **Key hints:** `AddKeyHint` puts your own key into the key hints, `RemoveKeyHint` takes it
  out. The `show` callback gets a `KeyHintContext` (`Vehicle`, `Profile`, `Seat`, `Driver`,
  `Pushing`).
- **NPCs that go down in a seat:** `RegisterOccupantDown` decides whether a shot NPC stays slumped
  in its seat instead of falling out (host only). The handler gets an `OccupantDown` (`Vehicle`,
  `Npc`, `Seat`, `Dead`); `RemoveOccupantDown` takes it out.

### Interiors, gauges and switches

- **Your own interior:** `SetInterior(code, vehicle => yourPrefabInstance, profile => ...)`
  replaces the base interior's seats, dashboard and wheel for one vehicle code. The lining and the
  tinted glass stay unless the callback turns them off. The callback names your wheel, gauges and
  switches in the profile.
- **Gauges:** `ChassisProfile.Gauges` binds a transform by name or path to a source. Kinds:
  `needle` (turns between `From` and `To`), `lamp` (glows above `Threshold`) and `digits` (text).
- **Sources:** `GaugeSources` lists them: speed, rpm, throttle, handbrake, lamps, steering, clock,
  odometer, trip, occupied, driver. `RegisterGaugeSource` adds your own. `Telemetry(vehicle)`
  reads all of them at once, each with a `Valid` flag. Throttle and handbrake reach passengers
  about 0.4 s late.
- **Switches:** `ChassisProfile.Controls` makes a transform clickable in first person: `button`,
  `toggle` or `knob`. Built-in actions are `lights`, `hazard`, `indicator`, `horn` and
  `trip_reset`; `RegisterControlAction` adds your own. Clicks go through the host, and toggle and
  knob states are saved with the vehicle.
- **Events:** `ControlUsed`, `HeadlightsChanged`, `BrakeChanged`, `ReverseChanged`,
  `DriverChanged`.
- **Console:** `chassis telemetry` prints every source with its validity and every bound gauge
  and switch of your car. `chassis zones` shows the hit zones of the switches. Both work in the
  release build. A gauge or switch whose path is not found logs one warning per vehicle code.
- **Debug build:** every GitHub release also carries `Chassis_<version>_debug.zip`. It adds the
  developer console commands, among them `chassis profile` (edit and save seat profiles live),
  `chassis interior`, `aim`, `click` and `ctl`; `chassis help` lists them all. Players use the
  normal build.
- **Example:** [`Examples/ChassisCockpitExample`](Examples/ChassisCockpitExample) gives the
  Shitbox a cockpit of its own with a speedometer, odometer digits and a beacon switch.

### Tuning

A vehicle's tuning is a list of keys that your mod writes through `ChassisApi`, for example a tuning
shop or a garage app. The host checks each change against the vehicle's owner, syncs it to every
player and saves it with the vehicle.

- **Parts:** `RegisterTuningPart` adds a part with its own model, fit and driving factors (torque,
  gearing, brake, grip, steer, mass, spring, damper, downforce, ride height). `InstallPart` and
  `RemovePart` fit it to a vehicle. Built-in parts: engine, brakes and transmission stages 1-3,
  suspension 1-4, turbo and drift tyres.
- **Looks:** `SetPaint` (primary and secondary, with a finish), `SetRimColor`, `SetXenon`,
  `SetNeon`, `SetWindowTint`, `SetTyreSmoke` and `SetHorn`.
- **Paint on your own model:** `SetPaintTargets` names the material slots that take the body paint,
  per vehicle or per vehicle code. Without targets the paint goes on the vanilla body. A target
  marked `Secondary` takes the second paint instead (stripes, trim); `HasSecondPaint` tells a shop
  to offer it. A target marked `Flat` drops its colour texture while painted, for a dark or two-toned
  texture that would darken the paint. `PaintTargetsOf` reads them. `PaintRenderers` paints slots that belong to no vehicle, for a model
  that stays when its vehicle is gone.
- **Your own keys:** `SetTuning`, `GetTuning` and `ClearTuning`.
- **Preview:** `PreviewTuning`, `PreviewPart` and `EndPreview` show a change on this machine only,
  without the host and without saving, for a shop that shows a part before the player buys it.
  `IsPreviewing` and `ShownTuning` read it.
- **Read:** `Tuning`, `InstalledPart`, `TuningStatsOf` (the driving factors in effect) and the
  event `TuningChanged`.
- **Example:** [Rollin](https://github.com/DooDesch-Mods/ScheduleOne-Rollin) builds the wheelchair
  on Chassis and tunes it with rockets, spoilers, neon and hats.

## Building from source

The project compiles two targets from one source tree: `net6.0` for IL2CPP and `netstandard2.1`
for Mono. It reads the game and MelonLoader reference DLLs from `../Workspace/lib/<il2cpp|mono>/`
(`game/` and `melonloader/`). Point `WorkspaceLibPath` at another folder with the same layout if
needed.

```sh
dotnet build Chassis.csproj -c Release                         # both targets
dotnet build Chassis.csproj -c Release -f netstandard2.1       # Mono only
dotnet build Chassis.csproj -c Release -p:WorkspaceLibPath=/path/to/lib
```

The IL2CPP build is at `bin/Release/net6.0/Chassis.dll`. Debug builds add console commands
(`chassis help`).

## Credits

- **DooDesch** - mod author.

## License

DooDesch API License - build on the API, do not copy the code. See [LICENSE.md](LICENSE.md).

This mod is written with help from generative AI tools (Anthropic Claude, OpenAI Codex).
