# String-Vorlagen

[English](README.md) · **Deutsch**

Eine Vorlage gibt den String-Feldern eines Ziels oder einer Haltestelle aussagekräftige Namen. Ohne sie heißt ein Feld nur
„String 7"; mit einer Vorlage für deinen Bus wird daraus etwa „Stacked (1st) Primary Text". Vorlagen wählst du in
der App unter **Vorlagen**, und jede Vorlage in diesem Ordner ist in die App eingebaut.

## Eine Vorlage hinzufügen (Pull Request)

1. Lege hier einen Ordner an, klein geschrieben und mit Bindestrichen: `Presets/mein-bus-pack/`.
2. Lege zwei Dateien hinein:

   **`preset.json`**

   ```json
   {
     "name": "Mein Bus-Pack",
     "author": "Dein Name oder Studio",
     "description": "Ein oder zwei Sätze dazu, für welche Busse oder Anzeigen das gedacht ist.",
     "url": "https://example.com/woher-es-kommt",
     "strings": "strings.txt"
   }
   ```

   `url` ist optional (nimm `""`, wenn es keine gibt). `strings` ist standardmäßig `strings.txt`.

   **`strings.txt`**: die Feldnamen, im selben Format wie die `defaultStrings.txt` der originalen HOF Suite
   und die Strings-Dateien, die manche OMSI-Erweiterungen mitbringen:

   ```text
   string 0:
   [TerminusString]
   Deutscher Name
   English name|Optionaler Hinweis, der als Tooltip erscheint
   (leere Zeile)
   (leere Zeile)

   string 0:
   [BusstopString]
   Deutscher Name
   English name
   ```

   Nur die englische Zeile (die 4. Zeile jedes Blocks) wird in der App angezeigt. Felder, die du auslässt, erscheinen als
   „String N". Speichere die Datei als UTF-8.
3. **Reiche nur ein, was du teilen darfst.** Schreibe die Namen selbst, oder nimm Dateien, deren Urheber die Weitergabe erlaubt.
   Kopiere keine Strings-Dateien aus fremden kostenpflichtigen oder urheberrechtlich geschützten Erweiterungen ohne Erlaubnis.
   Stammen die Namen von jemand anderem, trage dessen Namen in `author` ein, sag es in `description` und nimm ihn in
   `THIRD_PARTY_NOTICES.md` auf.
4. Eröffne einen Pull Request. Sonst muss nichts geändert werden: Die App übernimmt jeden Ordner in `Presets/`. Eine Prüfung läuft bei deinem
   Pull Request und meldet, wenn die Vorlage sich nicht laden lässt, keinen Autor oder keine Felder hat (`RepositoryPresetTests`).

## Eine eigene Strings-Datei importieren

Die meisten Strings-Dateien sind kein Vorlagen-Ordner mit `preset.json`. Öffne in der App **App-Einstellungen → String-Vorlagen → Eigene hinzufügen →
Strings-Datei importieren…**, wähle die Datei, und Hofwerk fragt, was darin steht:

- den **Namen** der Vorlage (der Dateiname wird vorgeschlagen) und optional, wer sie gemacht hat und wofür sie ist;
- **wie die Namen geschrieben sind**: eine OMSI-Strings-Datei (das Format oben), ein Name pro Zeile mit der Feldnummer davor
  (`0 = IBIS 1`, `7: ANNAX (Seite)`) oder ein Name pro Zeile in Feldreihenfolge (die erste Zeile ist Feld 0 oder das Feld, das du wählst);
- bei den beiden einfachen Formaten, ob die Namen für **Ziele**, **Haltestellen** oder beides gelten.

Aus der Datei wird eine Vermutung abgeleitet, und der Dialog zeigt, was er verstanden hat („24 Namen gefunden, zum Beispiel 0 = IBIS 1"), bevor du
importierst. In den einfachen Formaten ist eine Zeile mit `#` eine Notiz, eine leere Zeile lässt das Feld ohne Namen, und
`Name|Hinweis` ergibt einen Tooltip. Hofwerk schreibt die `preset.json` und die Strings-Datei für dich in deinen Vorlagen-Ordner und wechselt zur neuen Vorlage.

## Eine Vorlage ohne Pull Request nutzen

Lege denselben Ordner in deinen Benutzer-Vorlagen-Ordner (die Seite „Vorlagen" hat einen Knopf **Ordner öffnen**).
Vorlagen dort erscheinen nach **Aktualisieren** neben den eingebauten.

## Enthaltene Vorlagen

| Ordner | Name | Urheber |
| --- | --- | --- |
| `hof-suite-default` | HOF Suite Default | Lukas G., originale HOF Suite |
