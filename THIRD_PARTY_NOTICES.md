# Third-party notices

String presets live in [`Presets/`](Presets/README.md). The slot names in them were created by the people credited
below. Each preset's `preset.json` carries the same credit, and the app shows it in the preset library.

## HOF Suite Default (original HOF Suite)

`Presets/hof-suite-default/strings.txt` is the `defaultStrings.txt` of the original **HOF Suite**,
© 2018 Lukas G.

- Source: https://gitlab.com/omsi2/hof-suite
- Forum thread: https://forum.omnibussimulator.de/forum/index.php?thread/40888-hof-suite/
- Download: https://reboot.omsi-webdisk.de/file/773-hof-suite-en-de/

The original license (copy in `Presets/hof-suite-default/LICENSE.txt`) allows using, uploading and editing the
software on these conditions: attribute the author, link to the pages above, describe the changes made, and publish
the changed source code on a platform like GitLab or GitHub. HOF files written with the application are free to use.

### Changes made in this version

Hofwerk is a new implementation, not a fork of the original code. It is written in C# for .NET 10 with a WinUI 3
front end for Windows and an Avalonia front end for Linux. The only material taken from the original is the slot-name
list above, saved as UTF-8 and read through the preset library.
