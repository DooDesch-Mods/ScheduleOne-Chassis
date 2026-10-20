# Chassis - Vehicle Framework for Schedule I

> 🛟 **Need help or found a bug?** Get support at [support.doodesch.de/chassis](https://support.doodesch.de/chassis).

> Drivers and passengers sit in the car where you can see them, with their hands on a steering
> wheel that turns. Chassis adds a first person view, a horn, indicators, locks and owners, and a
> base interior for the vanilla cars. Other mods build their vehicles on it.

![Version](https://img.shields.io/badge/version-1.0.0-blue)
![Game](https://img.shields.io/badge/game-Schedule%20I-purple)
![MelonLoader](https://img.shields.io/badge/MelonLoader-0.7.3+-green)
![Status](https://img.shields.io/badge/status-working-brightgreen)

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
  inside. There is no key for the lock: mods do it, for example the Yesla phone app. Vehicles
  from older saves belong to the host.
- **Moving parts:** The steering wheel follows the front wheels.
- **Base interior for the vanilla cars:** Seats, a dashboard, a steering wheel and tinted glass,
  so you can see who sits inside.
- **Wheelchairs:** The wheelchairs placed around town can be ridden. Look at the seat and press
  Sit, then roll with the driving keys. A second player can take the handles and push. When it is
  empty again, it stays as a wheelchair where it stopped.
- **Co-op:** Seats, horn, indicators, locks and owners sync through the host.

Yesla and RVRepairVan build their vehicles on Chassis and install it as a dependency.

## Controls

| Key | Action |
|---|---|
| `V` | First person view in a vehicle |
| `G` | Horn |
| `H` | Headlights (vanilla) |
| `Left arrow` / `Right arrow` | Indicator left / right. Press again to switch off. |

## Screenshots

| | |
|---|---|
| ![Driver in the seat](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-Chassis/main/docs/img/01-seated-driver.jpg) | ![First person view](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-Chassis/main/docs/img/02-first-person.jpg) |
| ![Base interior](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-Chassis/main/docs/img/03-interior.jpg) | ![NPC driver](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-Chassis/main/docs/img/04-npc-driver.jpg) |
| ![Yesla](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-Chassis/main/docs/img/05-yesla.jpg) | ![RV Repair Van](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-Chassis/main/docs/img/06-rv.jpg) |
| ![Wheelchair](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-Chassis/main/docs/img/07-wheelchair.jpg) | |

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

## For mod authors

The public API is the static class `ChassisApi` in the namespace `Chassis.Api`
([`Api/`](Api/)). Reference `Chassis.dll` and add Chassis as a dependency of your package.

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

Provided as-is under the [MIT License](LICENSE.md).

This mod is written with help from generative AI tools (Anthropic Claude, OpenAI Codex).
