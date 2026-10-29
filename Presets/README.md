# String presets

<!-- languages -->
<p align="center"><b>English</b> · <a href="../translations/de/Presets.md">Deutsch</a></p>
<!-- /languages -->

A preset gives the string slots of a destination or bus stop meaningful names. Without one, a slot is just
"String 7"; with a preset for your bus it becomes something like "Stacked (1st) Primary Text". Presets are chosen in
the app under **Presets**, and every preset in this folder is built into the app.

## Add a preset (pull request)

1. Create a folder here, named in lowercase with dashes: `Presets/my-bus-pack/`.
2. Put two files in it:

   **`preset.json`**

   ```json
   {
     "name": "My Bus Pack",
     "author": "Your name or studio",
     "description": "One or two sentences about which buses or displays this is for.",
     "url": "https://example.com/where-it-comes-from",
     "strings": "strings.txt"
   }
   ```

   `url` is optional (use `""` if there is none). `strings` defaults to `strings.txt`.

   **`strings.txt`**: the slot names, in the same format as the `defaultStrings.txt` of the original HOF Suite
   and the strings files some OMSI add-ons ship with:

   ```text
   string 0:
   [TerminusString]
   Deutscher Name
   English name|Optional hint shown as a tooltip
   (empty line)
   (empty line)

   string 0:
   [BusstopString]
   Deutscher Name
   English name
   ```

   Only the English line (the 4th line of each block) is shown in the app. Slots you leave out are shown as
   "String N". Save the file as UTF-8.
3. **Only submit what you may share.** Write the names yourself, or use files whose author allows redistribution.
   Do not copy strings files out of someone else's paid or copyrighted add-on without their permission. If the names
   come from someone else, put their name in `author`, say so in `description`, and add them to
   `THIRD_PARTY_NOTICES.md`.
4. Open a pull request. Nothing else needs to change: the app picks up every folder in `Presets/`. A check runs on your
   pull request and tells you if the preset does not load, has no author, or has no slots (`RepositoryPresetTests`).

## Import a strings file of your own

Most strings files are not a preset folder with a `preset.json`. In the app, open **App settings → String presets → Add your
own → Import a strings file…**, pick the file, and Hofwerk asks what is in it:

- the **name** of the preset (the file's name is suggested), and optionally who made it and what it is for;
- **how the names are written**: an OMSI strings file (the format above), one name per line with the slot number in front
  (`0 = IBIS 1`, `7: ANNAX (side)`), or one name per line in slot order (the first line is slot 0, or the slot you choose);
- for the two plain formats, whether the names are for **destinations**, **bus stops** or both.

A guess is made from the file and the dialog shows what it understood ("Found 24 names, for example 0 = IBIS 1") before you
import. In the plain formats a line starting with `#` is a note, an empty line leaves that slot without a name, and
`Name|Hint` gives a tooltip. Hofwerk writes the `preset.json` and the strings file into your presets folder for you and switches to the new preset.

## Use a preset without a pull request

Drop the same folder into your user presets folder (the Presets page has an **Open folder** button).
Presets there show up next to the built-in ones after **Refresh**.

## Included presets

| Folder | Name | Credit |
| --- | --- | --- |
| `hof-suite-default` | HOF Suite Default | Lukas G., original HOF Suite |
