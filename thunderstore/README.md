# Chassis - Vehicle Framework for Schedule I

> 🛟 **Need help or found a bug?** Get support at [support.doodesch.de/chassis](https://support.doodesch.de/chassis).

> Drivers and passengers sit in the car where you can see them, with their hands on a steering
> wheel that turns. Chassis adds a first person view, a horn, indicators, locks and owners, and a
> base interior for the vanilla cars. Other mods build their vehicles on it.

![Version](https://img.shields.io/badge/version-1.0.0-blue)
![Game](https://img.shields.io/badge/game-Schedule%20I-purple)
![MelonLoader](https://img.shields.io/badge/MelonLoader-0.7.3+-green)

[![MLVScan Chassis.dll](https://mlvscan.com/attestations/att_Oaw2PH3Iatm2oFer1SosKO3k/badge.svg)](https://mlvscan.com/attestations/att_Oaw2PH3Iatm2oFer1SosKO3k)

![Chassis](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-Chassis/main/docs/img/header.jpg)

## Features

- Players and NPCs in a vehicle sit visibly in their seats, police included. The driver holds
  the steering wheel with both hands, and the wheel turns when you steer.
- Press V in a vehicle for a first person view from your seat. Your body stays visible below you.
- Press G for the horn. H stays the headlights.
- The left and right arrow keys switch the indicators on vehicles that have indicator lamps.
  After the turn they switch off on their own.
- Each player-owned vehicle has an owner, and the owner can lock it. A locked vehicle lets in
  only its owner. Others can ride along while the owner sits inside. Other mods set the lock
  through the API. Vehicles from older saves belong to the host.
- The vanilla cars get a base interior: seats, a dashboard, a steering wheel and tinted glass,
  so you can see who sits inside.
- The wheelchairs placed around town can be ridden: look at the seat, press Sit and roll with the
  driving keys. A second player can take the handles and push.
- In co-op, seats, horn, indicators, locks and owners sync through the host.

## Controls

| Key | Action |
|---|---|
| `V` | First person view in a vehicle |
| `G` | Horn |
| `H` | Headlights (vanilla) |
| `Left arrow` / `Right arrow` | Indicator left / right. Press again to switch off. |

## Screenshots

![Driver in the seat](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-Chassis/main/docs/img/01-seated-driver.jpg)

![First person view](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-Chassis/main/docs/img/02-first-person.jpg)

![Wheelchair](https://raw.githubusercontent.com/DooDesch-Mods/ScheduleOne-Chassis/main/docs/img/07-wheelchair.jpg)

## Requirements

- **Schedule I** `0.4.7f12` (IL2CPP) with **MelonLoader 0.7.3+**.
- Every player in a co-op session needs Chassis.

## Settings

`BaseInterior` (default `true`) in category `Chassis` of `UserData/MelonPreferences.cfg`. It
turns the base interior and the visible NPC drivers of the vanilla cars on or off.

## For mod authors

Chassis has a public C# API in `Chassis.Api` (`ChassisApi`). With it you give a vehicle seat
profiles, a steering wheel, lids, lamps and a horn sound, sound the horn, set indicators, and
read or set owner and lock. Profiles are JSON files in `UserData/Chassis/<vehicle code>.json`,
and a file there wins over a profile from code. Source and API:
[GitHub](https://github.com/DooDesch-Mods/ScheduleOne-Chassis).

## License

MIT. See the included LICENSE.md.

This mod is written with help from generative AI tools (Anthropic Claude, OpenAI Codex).

If you have fun with it, a like on the [Thunderstore page](https://thunderstore.io/c/schedule-i/p/DooDesch/Chassis/) really helps other people find it (thumbs up next to Download, you need to be logged in) - ty either way, and lemme know if anything breaks or you've got ideas
