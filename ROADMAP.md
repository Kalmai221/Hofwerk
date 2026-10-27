# Roadmap

## Versions

Hofwerk is released as **pre-releases until it can do everything the original HOF Suite does**. The pre-releases are called
`V1-dev1`, `V1-dev2` and so on (and `V0.1-dev1` and `V0.1-dev2` before them). **`V1`** is the first final release: the one with the
same features as HOF Suite. Releases after that continue from there (1.1, 1.2, ...).

Each pre-release is newer than the last one, and `V1` is newer than all of its `V1-dev` pre-releases. Hofwerk's own update check
follows that order, and only offers pre-releases to people who have them switched on (or who are running one).

## What V1 means

[HOF Suite](https://gitlab.com/omsi2/hof-suite) (Lukas G., 2018-2019) is the model. This list was put together from its public
changelog and the layout of its source code (its screens and its `Core` classes), so it may be missing small things. If you know
HOF Suite and something is missing here, please open an issue.

### Already in Hofwerk

- [x] Read and write `.hof` files, keeping their encoding
- [x] Edit destinations, bus stops and routes
- [x] The depot's settings: name, service trip, announcement / roll sign / side plate folders, IBIS codes
- [x] Custom string names through templates (HOF Suite 1.4); Hofwerk calls them **string presets** and shares them through the
  `Presets/` folder
- [x] A list of the problems found in a file
- [x] Start a new file from an OMSI **map**: its bus stops, destinations and routes (a first version of HOF Suite's *Import map*)
- [x] App settings and an About page
- [x] Updates inside the app, a theme choice (light, dark, system), a Linux version

### Still to do for V1

- [ ] **Import a map into a file you already have**, choosing what to add (today a map can only start a new file)
- [ ] **Import a trip**: add one timetable trip (a `.ttp` file) to a file as a route (HOF Suite's *Import trip*)
- [ ] **Export to several folders at once** (HOF Suite 1.1): save the same `.hof` into every bus pack folder that needs it
- [ ] **Station links** (HOF Suite 1.3): read and keep the `StnLinks.cfg` information of a map
- [ ] **Translations**: HOF Suite has a German version; Hofwerk is English only so far
- [ ] **A visual route timeline** (planned from the start)
- [ ] A last full comparison against HOF Suite with real `.hof` files, to catch anything that is missing or different

### After V1

Ideas that HOF Suite never had. Nothing here is promised.

- Compare two `.hof` files
- Check a file against the bus stops of a map and warn about stops that do not exist there
- Several `.hof` files open at once
