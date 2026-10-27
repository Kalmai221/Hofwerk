# Tests

Everything is run from one place, from the repository root (PowerShell 5.1 or 7):

```powershell
./test.ps1                        # all unit tests, about 25 seconds
./test.ps1 -Area Installer        # one area (several: -Area Installer,Updates)
./test.ps1 -Filter MapImporter    # tests whose name contains this
./test.ps1 -Slowest 10            # also list the slowest tests
./test.ps1 -Ui                    # also the UI tests of the built installers and app (Windows, needs dist/)
```

It ends with a summary (failed tests with the first line of their message) and a non-zero exit code when something failed.
On GitHub Actions the same summary is written to the job page.

## Areas

Every test class has `[Trait("Area", "...")]`. Put a new class in the area it belongs to:

| Area | What it covers |
| --- | --- |
| `HofFiles` | Reading and writing `.hof` files, the editor view model, string templates and presets |
| `Omsi` | Finding OMSI, its vehicles and maps, importing a map, folder name capitals, the home screen |
| `App` | The main view model, settings, themes, the New file dialog |
| `Updates` | Finding and applying updates, version order, the compatibility version, "What's new" |
| `Installer` | The install engine, the setup window's view model, update in place, packing and the online payload |
| `Errors` | The error report and the bug-report link |

## UI tests

`installer/*.ps1` start the finished installers and the published app and click through them with UI Automation. They need
`dist/` and `publish/` from `./installer/build-installer.ps1`, and take a few minutes. `./test.ps1 -Ui` runs them in order. They
save and restore a real installation on the machine (`installer/protect-install.ps1`) and the crash log, so they are safe to run
on a computer that has Hofwerk installed.

## Writing a test

- Use a temporary folder under `Path.GetTempPath()` and delete it in `Dispose()`; never touch the real `%APPDATA%\Hofwerk`.
- Use a fake `HttpMessageHandler` for anything online; no test may reach the network.
- Wait for something to happen by polling, not by a fixed sleep.
