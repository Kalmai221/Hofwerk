# Languages

Each language is one JSON file in this folder, named by its two-letter code (`de.json`, `fr.json` ...). The English text of Hofwerk is the key:

```json
{
  "name": "Deutsch",
  "strings": {
    "Save as": "Speichern unter",
    "{0} bus stops": "{0} Haltestellen"
  }
}
```

`name` is how the language is listed in the language box, written in that language. A text with no translation is shown in English, so a half-finished file never leaves a blank.

## Adding a language

1. Copy `de.json` to `<code>.json` and change `name`.
2. Translate the values. Keep every `{0}`, `{1}` ... exactly as in the English text; they are replaced with numbers and names.
3. Run the check:

   ```
   python src/Hofwerk.Installer.Engine/Languages/extract-texts.py <code>      # what is still missing
   python src/Hofwerk.Installer.Engine/Languages/extract-texts.py --check     # every language; CI runs this
   ```

4. The file is picked up by itself (it is embedded in the build). Start Hofwerk or the installer and choose the language, or leave the choice on *System default* if your Windows or desktop is set to it.

Plurals use two texts: `"{0} bus stop"` and `"{0} bus stops"` are translated as separate entries (one and many).

If the code gets a new text, `--check` fails until every language has it.

## Languages with a region, a different way of counting, or written from right to left

- **A regional language** is a file named with the region: `pt-BR.json`, `zh-Hans.json`. It is used for people whose system is set to
  that region. A file without a region (`de.json`) is used for every region of that language (de-AT, de-CH). When a language has only
  regional files (`pt-BR.json` and nothing else), people in the other regions of it (pt-PT) get the first of them, which is closer than English.
- **Counting.** English and German have two forms ("1 bus stop", "12 bus stops"), and those are two entries, as in `de.json`. A language
  that counts in more ways puts all its forms in a list under the first English text, in the order of the rule, and names the rule in the file:

  ```json
  {
    "name": "Русский",
    "plural": "slavic",
    "strings": {
      "{0} bus stop": ["{0} остановка", "{0} остановки", "{0} остановок"]
    }
  }
  ```

  The rules are `one-other` (the default: 1, then the rest), `french` (0 and 1, then the rest), `none` (one form for every number),
  `slavic` (1, 21 and 31 / 2 to 4 / the rest, as in Russian) and `polish` (1 / 2 to 4 / the rest). Need another? Add it to
  `Localizer.PluralIndex` and say so in the pull request.
- **Right to left.** `"rightToLeft": true` turns the windows around for Arabic, Hebrew and the like.

## Trying a language without building Hofwerk

Put the file in a folder named `Languages` next to the program (`Hofwerk.exe`), or in any folder and set the environment variable
`HOFWERK_LANGUAGES_DIR` to it. Hofwerk and the installer find it when they start, and it replaces a built-in language with the same code.

## The documents and the website

- **The documents on GitHub** go in a folder `translations/<code>/` (see `translations/de/`): `README.md`, `ROADMAP.md` and `Presets.md`, each
  translated from the English ones at the top of the repository. Then run `python translations/update-language-links.py`: it puts the
  language selector at the top of every document (the line with "English · Deutsch · ..."), including the English ones, and a check on every pull request
  fails when a selector is out of date. The changelog stays in English.
- **The website** follows the languages that have a folder `website/src/<code>/`: copy `website/src/de/` and translate `strings.json` (the menu,
  the page texts, the names of the downloads), `home.html`, `download-help.html` and `layout.html`. The pages are made from the documents in
  `translations/<code>/`. What is not there yet is shown in English, so a half-finished translation never breaks the site.
