# Changelog

All notable changes to Hofwerk are written down here. The format follows [Keep a Changelog](https://keepachangelog.com/),
and versions follow the release tags. Pre-releases are called `V1-dev1`, `V1-dev2` and so on until V1, the release with the
same features as HOF Suite (see the [roadmap](ROADMAP.md)). `V0.1-dev1` and `V0.1-dev2` came before them.

## [Unreleased]

Nothing yet.

## [1-dev3] - pre-release

### Added
- Updating inside the app is safer. Any error during the update is shown in the Update area instead of closing the app. If the new version cannot be started again, Hofwerk stays open and says so. The old files are removed only by the new copy once it is running (it waits a few seconds for the old one to close).
- **Undo and redo.** Undo (Ctrl+Z) and Redo (Ctrl+Y or Ctrl+Shift+Z) are in the toolbar and cover everything in the file: destinations, bus stops, routes, the settings page and the export locations. Typing a word is one step, the last 100 steps are kept, and undoing back to what is on disk makes the file count as saved again.
- **Autosave.** App settings → Saving can save the open file by itself 5 seconds, 30 seconds or 2 minutes after you stop editing. An autosave can be undone like any change.
- **Unsaved changes are asked about when you close.** Closing Hofwerk with changes that are not saved asks Save, Don't save or Cancel (with autosave on, the changes are just saved).
- **Export to several folders.** Settings has a new Export locations card: the vehicle folders this file should be copied into. They are saved in the file itself between `[HOF_SUITE_DIRECTORIES]` and `[HOF_SUITE_DIRECTORIES_END]`, the same list HOF Suite writes, so files from HOF Suite show their folders at once. The Export button (or Export now…) saves the file and puts it into every folder, with a progress bar, a result for each folder and a `.bak` copy of any file it replaces.
- **Import your own strings file.** App settings → String presets → Import a strings file… takes a strings file that has no preset.json, asks what is in it (name, how the names are written, whether they are for destinations or bus stops) and makes a preset of it.
- More progress bars (also settings that fit on one line when the window is wide). Looking for an update, putting an update in place and reading a map in the New file dialog now show a moving bar next to the text, and downloads keep their percentage bar.
- The installer shows its steps while it works: Download, Extract, Move files, Cleanup, Close. It unpacks into a work folder first and only then moves the files in, so a damaged package no longer touches an existing install.
- The offline installer no longer looks for a newer version when it opens. Only the online installer does.

## [1-dev2] - pre-release

### Added
- **Error handling.** When Hofwerk runs into a problem it did not expect, it no longer just closes. A window explains what went wrong
  and offers three things: **Restart Hofwerk**, **Try a different version** (Windows only, see below), and **Report a bug**. Report a bug
  opens a new GitHub issue in your browser with the error, your Hofwerk version and your system already filled in. Your name is taken
  out of any file path first, you can read exactly what would be sent, and nothing is sent until you press Submit on GitHub.
  Every error is also written to `crash.log`.
- **A second version for Windows.** Hofwerk can also run as a version drawn with Avalonia (the toolkit of the Linux version), for
  computers where the standard WinUI one does not work. "Try a different version" downloads it the first time (about 15 MB), and from
  then on Hofwerk starts that one. App settings of the compatibility version has a button to go back to the standard version, and
  starting Hofwerk with `--standard` always gives the standard one.

- **Maps with pictures.** The New file dialog shows the maps as a grid of pictures with their names, like OMSI's own map list and HOF
  Suite: each map's `picture.jpg` and the name from its `global.cfg` ("Cotterell v1.1"). A map picked from another folder shows up the
  same way.

### Fixed
- Updating inside the app could crash at the very end: after the new files were in place, refreshing the Start menu entry used a Windows
  component that the trimmed app had switched off. The update was complete, but Hofwerk closed with an error instead of restarting.
  That step can no longer undo or interrupt an update, the component is switched on again, and the installer test now runs this step
  in the real app.

## [1-dev1] - pre-release

The first pre-release on the way to V1, the release with the same features as HOF Suite (see the [roadmap](ROADMAP.md)).

### Added
- **Import from an OMSI map.** New .hof file can start from a map: pick a map and Hofwerk reads its timetable files, then fills in
  the bus stops, the destinations and the routes (with their stops, in order). You choose which of the three to take. Route codes are
  suggestions (line 28 gives 2801, 2802 and so on) and can be changed in the editor.
- **Roadmap.** `ROADMAP.md` lists what HOF Suite does, what Hofwerk already does, and what is left before V1.

### Changed
- **Pre-releases:** the "Include pre-releases" switch now does what it says. It is on by itself only while you run a pre-release (and
  says so); if you turn it off you are offered final versions only. Before, a pre-release always got newer pre-releases.
- **Folder names** such as the OMSI 2 folder are shown the way they are on disk (`C:\Program Files (x86)\Steam`), not in the lowercase
  that Steam and the registry store.

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

[Unreleased]: https://github.com/Kalmai221/Hofwerk/compare/V1-dev3...HEAD
[1-dev3]: https://github.com/Kalmai221/Hofwerk/compare/V1-dev2...V1-dev3
[1-dev2]: https://github.com/Kalmai221/Hofwerk/compare/V1-dev1...V1-dev2
[1-dev1]: https://github.com/Kalmai221/Hofwerk/compare/V0.1-dev2...V1-dev1
[0.1-dev2]: https://github.com/Kalmai221/Hofwerk/compare/V0.1-dev1...V0.1-dev2
[0.1-dev1]: https://github.com/Kalmai221/Hofwerk/releases/tag/V0.1-dev1
