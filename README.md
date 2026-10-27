# Hofwerk

A modern editor for the **`.hof` files of OMSI 2**: the destination displays, bus stops and routes that let one bus
model run on many lines. The name is German for "depot works" (*Hof* is OMSI's word for a depot). Hofwerk is a new
implementation of the 2018 HOF Suite (see [Credits](#credits)).

- **Windows:** native WinUI 3 app (Mica, light and dark mode).
- **Linux:** Avalonia app that shares all logic with the Windows one. *Builds, but has not been tested on Linux yet.*

![Hofwerk's home screen on Windows and on Linux](docs/windows-and-linux.png)

## What it does

- Opens and saves `.hof` files while keeping their original encoding (Windows-1252 or UTF-8).
- Edits destinations, bus stops and routes, and the depot's **Settings**: general settings (display name and service
  trip destination), the announcement / roll sign / side plate **folders** (with a tick when the folder exists in your
  OMSI install) and the **IBIS codes**.
- Home screen with your recent files and every `.hof` of your Steam OMSI install (Windows, Linux, Flatpak and extra
  library drives).
- **String presets:** names for the string slots ("IBIS 1", "ANNAX (side)" …) so you do not have to remember what
  "String 7" means. See [`Presets/`](Presets/README.md) to add your own through a pull request.

- **Undo and redo**, optional **autosave**, and a question before closing with unsaved changes.
- **New file from an OMSI map:** pick a map and Hofwerk fills in its bus stops, destinations and routes.
- **Updates inside the app**, light and dark themes, and a "What's new" for every version.

Hofwerk is still in pre-release (`V1-dev1`, `V1-dev2` ...). **V1** is the release that does everything HOF Suite does. See
the [roadmap](ROADMAP.md) for what is done and what is left, and the [changelog](CHANGELOG.md) for what changed in each version.

## Hofwerk and HOF Suite

[HOF Suite](https://gitlab.com/omsi2/hof-suite) (Lukas G., 2018-2019) is the editor Hofwerk is modelled on. Hofwerk reads and
writes the same `.hof` files, so you can switch between them. The list is based on HOF Suite's public changelog and source
layout (see the [roadmap](ROADMAP.md)), so a detail may be missing.

| | HOF Suite | Hofwerk |
| --- | --- | --- |
| Systems | Windows | **Windows and Linux** (Linux not tested on a real desktop yet) |
| Edit destinations, bus stops, routes, depot settings | Yes | Yes |
| Keeps the file's encoding, lists problems in a file | Yes | Yes |
| Names for the string slots | Templates | String presets, built in and shared in `Presets/` |
| New file from an OMSI map | Yes (*Import map*) | Yes, with the map's picture and name. Only for a new file so far |
| Import a map into an existing file, import a single trip | Yes | Not yet |
| Export one file to several bus folders (the list is kept in the file) | Yes | Yes, with a backup of replaced files |
| Station links (`StnLinks.cfg`) | Yes | Not yet |
| Translations | English and German | English only so far |
| Updates inside the app, "What's new" | No | Yes |
| Light, dark and system theme | No | Yes |
| New files carry a plain-words guide to each section | No | Yes |
| Error window with a ready-made bug report | No | Yes |
| Second Windows version for computers where WinUI fails | No | Yes (the Avalonia one, downloaded when needed) |
| Installer | Setup program | One-file installers, online or offline, no administrator rights |

Hofwerk becomes **V1** when the "Not yet" rows are done. After that it goes beyond HOF Suite (see the roadmap).

## Install

Download the installer for your system from the [Releases](../../releases) page. Each one is a single file.

- **Windows:** `Hofwerk-Setup-<version>-win-x64.exe`. Double-click it. It installs just for you (no administrator
  rights), adds a Start menu entry, and can add a desktop shortcut and offer Hofwerk in "Open with" for `.hof`
  files. Remove it from Windows' *Installed apps* or with the uninstaller in the install folder. The installer is not
  code-signed yet, so Windows SmartScreen may ask you to choose *More info → Run anyway*.
- **Linux:** `Hofwerk-Setup-<version>-linux-x64.tar.gz`. Unpack it and run the file inside:

  ```bash
  tar -xzf Hofwerk-Setup-*-linux-x64.tar.gz
  ./Hofwerk-Setup-*-linux-x64
  ```

  It installs to `~/.local/share/hofwerk`, adds Hofwerk to your applications menu and a `hofwerk` command. The menu
  entry has an *Uninstall Hofwerk* action. *Not tested on a real Linux desktop yet.*

For scripts, both installers also run without a window:
`Setup --silent [--dir PATH] [--desktop-shortcut] [--file-association]`, and `Uninstall --silent --uninstall`.

## Build

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
./test.ps1                                             # all tests; see tests/README.md
dotnet run --project src/Hofwerk.WinUI -r win-x64      # Windows
dotnet run --project src/Hofwerk.Linux                 # Linux (also runs on Windows)
```

To build the installer yourself (PowerShell 5.1 or 7; the result lands in `dist/`):

```powershell
pwsh installer/build-installer.ps1 -Rid win-x64 -Version 0.1.0
```

## Project layout

| Folder | What is in it |
| --- | --- |
| `src/Hofwerk.Core` | `.hof` parser and writer, OMSI/Steam locator, recent files, preset library. No UI. |
| `src/Hofwerk.ViewModels` | Everything the screens show and do, shared by both front ends. |
| `src/Hofwerk.WinUI` | Windows front end (WinUI 3). |
| `src/Hofwerk.Linux` | Linux front end (Avalonia). |
| `src/Hofwerk.Installer.Engine` | Installing and uninstalling: copying files, shortcuts, registry or menu entries. No UI. |
| `src/Hofwerk.Installer` | The setup window (Avalonia). The same program, without the app attached, is the uninstaller. |
| `installer/` | Script that builds the one-file installers. |
| `Presets/` | String presets built into the app. One folder per preset. |
| `tests/` | Unit tests, including a round-trip check against real OMSI files when OMSI is installed. |

## Releases

Pushing a tag such as `v0.1.0` runs the tests, builds the Windows and Linux installers, and publishes them as a
GitHub release (`.github/workflows/release.yml`).

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
