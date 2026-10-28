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
