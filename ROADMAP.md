# Roadmap

## Versions

Hofwerk is released as **pre-releases until it can do everything the original HOF Suite does**. The pre-releases are called
`V1-dev1`, `V1-dev2`, `V1-dev3` and so on (and `V0.1-dev1` and `V0.1-dev2` before them). **`V1`** is the first final release: the one
with the same features as HOF Suite. Releases after that continue from there (1.1, 1.2, ...).

Each pre-release is newer than the last one, and `V1` is newer than all of its `V1-dev` pre-releases. Hofwerk's own update check
follows that order, and only offers pre-releases to people who have them switched on (or who are running one).

## What V1 means

[HOF Suite](https://gitlab.com/omsi2/hof-suite) (Lukas G., 2018-2019) is the model. This list was put together from its public
changelog and the layout of its source code (its screens and its `Core` classes), so it may be missing small things. If you know
HOF Suite and something is missing here, please open an issue.

### Already in Hofwerk (what HOF Suite does)

- [x] Read and write `.hof` files, keeping their encoding
- [x] Edit destinations, bus stops and routes
- [x] The depot's settings: name, service trip, announcement / roll sign / side plate folders, IBIS codes
- [x] Custom string names through templates (HOF Suite 1.4); Hofwerk calls them **string presets** and shares them through the
  `Presets/` folder. A strings file of your own can be imported (Hofwerk asks what is in it, so it needs no `preset.json`)
- [x] A list of the problems found in a file
- [x] **Export to several folders at once** (HOF Suite 1.1): the Export locations on the Settings page are kept in the file
  (`[HOF_SUITE_DIRECTORIES]`, as HOF Suite does) and Export puts the file into every one of them, with a backup of what it replaces
- [x] Start a new file from an OMSI **map**: its bus stops, destinations and routes, with the map's picture and name (a first version
  of HOF Suite's *Import map*)

### Still to do for V1

- [ ] **Import a map into a file you already have**, choosing what to add (today a map can only start a new file)
- [ ] **Import a trip**: add one timetable trip (a `.ttp` file) to a file as a route (HOF Suite's *Import trip*)
- [ ] **Station links** (HOF Suite 1.3): read and keep the `StnLinks.cfg` information of a map
- [ ] **Translations**: HOF Suite has a German version; Hofwerk is English only so far
- [ ] **A visual route timeline** (planned from the start)
- [ ] A last full comparison against HOF Suite with real `.hof` files, to catch anything that is missing or different

### Already in Hofwerk, beyond HOF Suite

- [x] **Undo and redo** for everything in the file, **autosave**, and a question before closing with unsaved changes
- [x] Updates inside the app, "What's new" for every version, a theme choice (light, dark, system), App settings and an About page
- [x] A **Linux** version (not yet tried on a real Linux desktop)
- [x] An error window with Restart, a second Windows version (Avalonia) for computers where WinUI fails, and a ready-made bug report
- [x] One-file installers, online and offline, that show their steps; releases with notes taken from the changelog
- [x] A website with the downloads, the changelog, the roadmap and the comparison
- [x] New files carry a plain-words guide to each section, and a "Made with Hofwerk" credit
- [x] Progress bars for everything that takes a moment, and settings that rearrange to the width of the window

## Known gaps

Not part of V1, but worth knowing:

- Linux has only been built and run on Windows so far. Someone with a real Linux desktop should try the installer and the app.
- The installer is not code-signed, so Windows SmartScreen asks for confirmation.
- The question about unsaved changes comes when the window is closed, not when another file is opened over it.
- Export copies when you press Export; it does not run on every save.

## After V1

Ideas that HOF Suite never had. Nothing here is promised.

- Compare two `.hof` files
- Check a file against the bus stops of a map and warn about stops that do not exist there
- Several `.hof` files open at once
- Ask about unsaved changes when opening or creating a file over them, and an option to export on every save
