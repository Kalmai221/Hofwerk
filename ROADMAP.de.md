# Roadmap

[English](ROADMAP.md) · **Deutsch**

## Versionen

Hofwerk erscheint als **Vorabversionen, bis es alles kann, was die originale HOF Suite kann**. Die Vorabversionen heißen
`V1-dev1`, `V1-dev2`, `V1-dev3` und so weiter (davor `V0.1-dev1` und `V0.1-dev2`). **`V1`** ist die erste endgültige Version: die
mit denselben Funktionen wie die HOF Suite. Spätere Versionen setzen danach fort (1.1, 1.2, ...).

Jede Vorabversion ist neuer als die vorige, und `V1` ist neuer als alle ihre `V1-dev`-Vorabversionen. Hofwerks eigene Update-Prüfung
folgt dieser Reihenfolge und bietet Vorabversionen nur Leuten an, die sie eingeschaltet haben (oder eine benutzen).

## Was V1 bedeutet

Die [HOF Suite](https://gitlab.com/omsi2/hof-suite) (Lukas G., 2018–2019) ist das Vorbild. Diese Liste entstand aus ihrem öffentlichen
Änderungsprotokoll und dem Aufbau ihres Quellcodes (Fenster und `Core`-Klassen), es können also Kleinigkeiten fehlen. Wenn du
die HOF Suite kennst und hier etwas fehlt, eröffne bitte ein Issue.

### Schon in Hofwerk (was die HOF Suite kann)

- [x] `.hof`-Dateien lesen und schreiben, mit ihrer Kodierung
- [x] Ziele, Haltestellen und Routen bearbeiten
- [x] Die Einstellungen des Betriebshofs: Name, Dienstfahrt, Ordner für Ansagen / Rollbänder / Seitenschilder, IBIS-Codes
- [x] Eigene String-Namen über Vorlagen (HOF Suite 1.4); Hofwerk nennt sie **String-Vorlagen** und teilt sie über den Ordner
  `Presets/`. Eine eigene Strings-Datei lässt sich importieren (Hofwerk fragt, was darin steht, daher braucht sie keine `preset.json`)
- [x] Eine Liste der Probleme, die in einer Datei gefunden wurden
- [x] **Export in mehrere Ordner auf einmal** (HOF Suite 1.1): Die Export-Orte auf der Seite „Einstellungen" werden in der Datei gespeichert
  (`[HOF_SUITE_DIRECTORIES]`, wie in der HOF Suite), und Exportieren legt die Datei in jedem davon ab, mit Sicherung dessen, was es ersetzt
- [x] **Eine Karte in eine vorhandene Datei importieren** und auswählen, was hinzukommt. Was schon in der Datei steht, bleibt unberührt (die Vorschau sagt, was hinzukäme), und „Rückgängig" nimmt den ganzen Import zurück
- [x] **Eine Fahrt importieren**: eine Fahrplandatei (`.ttp`) als Route zu einer Datei hinzufügen (*Import trip* der HOF Suite)
- [x] **Haltestellen-Verknüpfungen** (HOF Suite 1.3): Fahrplandateien, die einen Halt nur als Zahl schreiben (`[station_typ2]`), bekommen seinen Namen aus der `Busstops.cfg` der Karte. Karten wie Lincolnshire, Yorkshire, London und Pimlico schreiben die meisten ihrer Fahrten so. (Es ist nicht die Datei `StnLinks.cfg`, wie diese Roadmap zuerst vermutete)
- [x] Eine neue Datei aus einer OMSI-**Karte** beginnen: ihre Haltestellen, Ziele und Routen, mit Bild und Namen der Karte (eine erste Version
  von *Import map* der HOF Suite)
- [x] **Übersetzungen**: Englisch und Deutsch, in der App und im Installer, mit der Systemsprache als Vorgabe

### Noch zu tun für V1

- [ ] **Eine visuelle Routen-Zeitleiste** (von Anfang an geplant)
- [ ] Ein letzter vollständiger Vergleich mit der HOF Suite anhand echter `.hof`-Dateien, um Fehlendes oder Abweichendes zu finden

### Schon in Hofwerk, über die HOF Suite hinaus

- [x] **Rückgängig und Wiederholen** für alles in der Datei, **automatisches Speichern** und eine Nachfrage vor dem Schließen mit ungespeicherten Änderungen
- [x] Updates in der App, „Neuigkeiten" zu jeder Version, eine Designauswahl (hell, dunkel, System), App-Einstellungen und eine Info-Seite
- [x] Eine **Linux**-Version (noch nicht auf einem echten Linux-Desktop ausprobiert)
- [x] Ein Fehlerfenster mit Neustart, eine zweite Windows-Version (Avalonia) für Rechner, auf denen WinUI scheitert, und ein fertiger Fehlerbericht
- [x] Ein-Datei-Installer, online und offline, die ihre Schritte zeigen; Releases mit Notizen aus dem Änderungsprotokoll
- [x] Eine Website mit den Downloads, dem Änderungsprotokoll, der Roadmap und dem Vergleich
- [x] Neue Dateien enthalten eine Anleitung in einfachen Worten zu jedem Abschnitt und einen „Made with Hofwerk"-Vermerk
- [x] Fortschrittsbalken für alles, was einen Moment dauert, und Einstellungen, die sich der Fensterbreite anpassen

## Bekannte Lücken

Nicht Teil von V1, aber gut zu wissen:

- Linux wurde bisher nur unter Windows gebaut und gestartet. Jemand mit einem echten Linux-Desktop sollte den Installer und die App ausprobieren.
- Der Installer ist nicht signiert, deshalb verlangt Windows SmartScreen eine Bestätigung.
- Die Frage nach ungespeicherten Änderungen kommt beim Schließen des Fensters, nicht wenn eine andere Datei darüber geöffnet wird.
- Der Export kopiert, wenn du auf Exportieren drückst; er läuft nicht bei jedem Speichern.
- Die deutsche Übersetzung wurde nicht von einer Muttersprachlerin oder einem Muttersprachler geprüft. Verbesserungen sind willkommen.

## Nach V1

Ideen, die die HOF Suite nie hatte. Nichts davon ist versprochen.

- Zwei `.hof`-Dateien vergleichen
- Eine Datei mit den Haltestellen einer Karte abgleichen und vor Haltestellen warnen, die es dort nicht gibt
- Mehrere `.hof`-Dateien gleichzeitig geöffnet
- Beim Öffnen oder Anlegen einer Datei über ungespeicherten Änderungen nachfragen, und eine Option, bei jedem Speichern zu exportieren
- Weitere Sprachen (eine Anleitung steht in `src/Hofwerk.Installer.Engine/Languages/README.md`)
