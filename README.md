# Hofwerk

A modern editor for the **`.hof` files of OMSI 2**: the destination displays, bus stops and routes that let one bus
model run on many lines. The name is German for "depot works" (*Hof* is OMSI's word for a depot). Hofwerk is a new
implementation of the 2018 HOF Suite (see [Credits](#credits)).

- **Windows:** native WinUI 3 app (Mica, light and dark mode).
- **Linux:** Avalonia app that shares all logic with the Windows one. *Builds, but has not been tested on Linux yet.*

## What it does

- Opens and saves `.hof` files while keeping their original encoding (Windows-1252 or UTF-8).
- Edits the depot settings, destinations, bus stops and routes.
- Home screen with your recent files and every `.hof` of your Steam OMSI install (Windows, Linux, Flatpak and extra
  library drives).
- **String presets:** names for the string slots ("IBIS 1", "ANNAX (side)" …) so you do not have to remember what
  "String 7" means. See [`Presets/`](Presets/README.md) to add your own through a pull request.

Not built yet: map and trip import, export, and a visual route timeline.

## Build

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
dotnet test tests/Hofwerk.Core.Tests
dotnet run --project src/Hofwerk.WinUI -r win-x64      # Windows
dotnet run --project src/Hofwerk.Linux                 # Linux (also runs on Windows)
```

## Project layout

| Folder | What is in it |
| --- | --- |
| `src/Hofwerk.Core` | `.hof` parser and writer, OMSI/Steam locator, recent files, preset library. No UI. |
| `src/Hofwerk.ViewModels` | Everything the screens show and do, shared by both front ends. |
| `src/Hofwerk.WinUI` | Windows front end (WinUI 3). |
| `src/Hofwerk.Linux` | Linux front end (Avalonia). |
| `Presets/` | String presets built into the app. One folder per preset. |
| `tests/` | Unit tests, including a round-trip check against real OMSI files when OMSI is installed. |

## Releases

Pushing a tag such as `v0.1.0` builds the Windows app and publishes a GitHub release
(`.github/workflows/release.yml`).

## Thank you

A big thank you to **Lukas G.**, the creator of the original **HOF Suite**, for the inspiration to build Hofwerk.
The original tool made `.hof` files approachable for the whole OMSI community, and it has not been updated since 2019.
I wanted to carry that idea forward with a modern interface for today's Windows and Linux players.

Thanks also to everyone in the OMSI community who makes and shares buses, maps and add-ons, and to everyone who
contributes string presets.

## Credits

- The original **HOF Suite** by **Lukas G.** (© 2018): https://gitlab.com/omsi2/hof-suite. The "HOF Suite Default"
  preset is taken from it. Hofwerk is a new codebase, not a fork. Its license requires attribution, links to the
  original and public source; details in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
- Everyone credited in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for string presets.
- OMSI 2 is a product of its respective owners. This project is not affiliated with them.
