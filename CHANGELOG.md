# Changelog

All notable changes to Hofwerk are written down here. The format follows [Keep a Changelog](https://keepachangelog.com/),
and versions follow the release tags (`V0.1-dev1` is a pre-release of 0.1).

## [Unreleased]

Nothing yet.

## [0.1-dev2] - pre-release

### Added
- **New file.** Make a new, empty .hof file from the New button in the toolbar (Ctrl+N on Windows) or from Home. It opens ready to
  edit, with the string slots of the active preset.
- **Made with Hofwerk.** Files Hofwerk creates start with a short credit and a link to the GitHub page, and have plain-words notes
  between their sections. OMSI ignores these lines. Files made by other tools are left as they are.
- **Updates inside the app.** Hofwerk checks GitHub for a newer version when it starts, and from App settings. "Update now"
  downloads only the packed app (about 19 MB, checked against its SHA-256), replaces its own files while it runs, and restarts
  with your file open. If anything goes wrong the old version is put back. Pre-releases are offered when you ask for them.
- **Online installer.** Each release now has a small `Hofwerk-WebSetup` (about 19 MB) that downloads the app while installing,
  next to the full offline `Hofwerk-Setup` (about 38 MB). Both check the app's size and SHA-256 before installing.
- **Installer update check.** The installer looks for a newer release when it opens and offers to fetch it.
- **App settings.** A new page at the bottom of the sidebar with Appearance, Updates, the OMSI 2 folder, String presets and About.
- **Themes.** Choose System default, Light or Dark in App settings.
- **Motion.** Pages ease in when you switch to them, and a theme change fades instead of flipping. The Windows app follows the
  system setting for animations.
- **What's new.** A link in App settings opens the notes of the version you have in a pop-up, and the update bar shows the notes of
  the new version before you install it. Release descriptions on GitHub are written from this changelog.
- **File dialogs open in OMSI.** Open and New start in the `Vehicles` folder of your OMSI 2 install, and Save as starts next to the
  file you have open. Choosing the OMSI folder starts where OMSI is.
- **Searching and copying.** Search boxes and a Duplicate button for destinations, bus stops and routes on both Windows and Linux.

### Changed
- **String presets** are now a drop-down in App settings, with credits and an "Add your own" section, instead of a page of their own.
- The file's **Settings** page has its own icon, and a line in the sidebar separates the start pages from the editor pages.
- **Messages in the status bar** disappear after 3 seconds.
- The **installer** is smaller (about 38 MB, from about 45 MB): it packs the app with a larger Brotli window, leaves out parts
  of Windows App SDK that Hofwerk does not use, and draws its window without the ANGLE graphics layer.
- The **"Where is OMSI 2?"** installer page now fits its window.
- The release workflow checks the changelog before building, uses current GitHub Actions versions, and pins the Linux runner.

### Fixed
- Removing one copy of Hofwerk no longer removes the Installed apps, Start menu and "Open with" entries of another copy.
- Folder names that Windows would not accept are also refused on Linux.
- The build script no longer fails on PowerShell 7, and the release workflow accepts tags that start with a capital `V`.
- The installer tests no longer leave a non-zero exit code behind that made the CI step fail even when every check passed.

## [0.1-dev1] - pre-release

First pre-release.

### Added
- **Editing .hof files** for OMSI 2: destinations, bus stops and routes, with the file's settings (name, service trip, folders and
  IBIS codes). Windows-1252 and UTF-8 files are saved the way they were read.
- **Routes** are built by picking stops from the bus stops defined in the file, with move up, move down and remove. Stops that are
  not defined are flagged, and renaming a bus stop renames it in every route.
- **Windows app** built with WinUI, and a **Linux app** built with Avalonia, with a home screen, recent files and an OMSI library
  that works like a file explorer.
- **String presets** that name the string slots, with a library for community presets (see `Presets/README.md`).
- **OMSI 2 detection** through Steam and the Windows registry, and a "Where is OMSI 2?" step in the installer.
- **Installers**: a single `.exe` for Windows and a `tar.gz` for Linux, with an uninstaller. Releases are built by the GitHub workflow.

[Unreleased]: https://github.com/Kalmai221/Hofwerk/compare/V0.1-dev2...HEAD
[0.1-dev2]: https://github.com/Kalmai221/Hofwerk/compare/V0.1-dev1...V0.1-dev2
[0.1-dev1]: https://github.com/Kalmai221/Hofwerk/releases/tag/V0.1-dev1
