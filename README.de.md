<p align="center"><img src="branding/hofwerk-logo.svg" alt="Hofwerk" width="112"></p>

# Hofwerk

[English](README.md) · **Deutsch**

Ein moderner Editor für die **`.hof`-Dateien von OMSI 2**: die Zielanzeigen, Haltestellen und Routen, mit denen ein
Bus-Modell auf vielen Linien fahren kann. Der Name bedeutet „Betriebshof-Werk" (*Hof* ist OMSIs Wort für einen Betriebshof).
Hofwerk ist eine Neuentwicklung der HOF Suite von 2018 (siehe [Danksagung](#danksagung)).

- **Windows:** native WinUI-3-App (Mica, heller und dunkler Modus). Läuft sie auf einem Rechner nicht, kann Hofwerk zu einer
  zweiten, mit Avalonia gezeichneten Version wechseln, die bei Bedarf heruntergeladen wird.
- **Linux:** Avalonia-App, die ihre gesamte Logik mit der Windows-App teilt. *Sie lässt sich bauen, wurde aber noch nicht auf Linux getestet.*

![Hofwerks Startseite unter Windows und unter Linux](docs/windows-and-linux.png)

## Warum ich Hofwerk gemacht habe

Mit der HOF Suite haben die meisten in der OMSI-Community gelernt, `.hof`-Dateien zu erstellen, ohne Text von Hand zu
bearbeiten – und sie wurde seit 2019 nicht mehr aktualisiert. Sie läuft nur unter Windows, und immer mehr Leute spielen auf Linux
oder dem Steam Deck. Ich wollte einen `.hof`-Editor, der sich wie heutige Software anfühlt, mit Dateien aus der HOF Suite
weiterarbeitet und auf beiden Systemen läuft, damit niemand Windows starten oder eine virtuelle Maschine betreiben muss, nur um ein Ziel zu ändern.

Gaming unter Linux wächst. In Valves Steam-Hardware-Umfrage (nur von Leuten, die teilnehmen) lag Linux bei
[2,64 % im August 2025 und 3,90 % im August 2026](https://gamingonlinux.com/2026/08/linux-back-over-4-percent-in-the-july-2026-steam-survey),
und es
[lag in der Umfrage vom März 2026 zum ersten Mal über 5 %](https://linuxiac.com/linux-make-history-hitting-five-percent-usage-on-steam/).
Die Zahl schwankt von Monat zu Monat und stammt nur von Steam-Nutzern, die geantwortet haben; sie zeigt also eine Richtung, keine
genaue Zählung. OMSI 2 selbst ist ein Windows-Spiel, deshalb spielen Linux-Nutzer es über eine Kompatibilitätsschicht wie Steams Proton.
Ihre `.hof`-Dateien sind dieselben Dateien, und sie verdienen einen nativen Editor.

## Was er kann

- Öffnet und speichert `.hof`-Dateien und behält dabei die ursprüngliche Kodierung (Windows-1252 oder UTF-8).
- Bearbeitet Ziele, Haltestellen und Routen sowie die **Einstellungen** des Betriebshofs: allgemeine Einstellungen (Anzeigename und
  Ziel der Dienstfahrt), die **Ordner** für Ansagen, Rollbänder und Seitenschilder (mit einem Haken, wenn der Ordner in deiner
  OMSI-Installation existiert) und die **IBIS-Codes**.
- **Export in mehrere Ordner:** Die Seite „Einstellungen" listet die Fahrzeugordner auf, in die eine Datei gehört (in der Datei
  gespeichert, wie es die HOF Suite macht), und **Exportieren** legt die Datei in alle auf einmal ab, mit Fortschrittsbalken und einem Ergebnis je Ordner.
- **Rückgängig und Wiederholen** für alles in der Datei, optionales **automatisches Speichern** und eine Nachfrage vor dem Schließen mit ungespeicherten Änderungen.
- Startseite mit deinen zuletzt geöffneten Dateien und jeder `.hof` deiner Steam-OMSI-Installation (Windows, Linux, Flatpak und zusätzliche
  Bibliotheks-Laufwerke).
- **Import aus einer OMSI-Karte:** Wähle eine Karte (mit Bild und Namen wie in OMSI), und Hofwerk füllt ihre Haltestellen,
  Ziele und Routen ein – für eine neue Datei oder in die geöffnete Datei hinein. Eine einzelne Fahrplandatei (`.ttp`) lässt sich als eine Route hinzufügen.
- **String-Vorlagen:** Namen für die String-Felder („IBIS 1", „ANNAX (Seite)" …), damit du dir nicht merken musst, was
  „String 7" bedeutet. Importiere eine eigene Strings-Datei (Hofwerk fragt, was darin steht), oder sieh dir [`Presets/`](Presets/README.de.md)
  an, um eine per Pull Request beizutragen.
- **Updates in der App**, helle und dunkle Designs, „Neuigkeiten" zu jeder Version und ein Fehlerfenster mit
  fertigem Fehlerbericht.
- **Sprachen:** Englisch und Deutsch, in der App und im Installer. Hofwerk nimmt die Sprache deines Systems, solange du keine andere wählst.

Hofwerk ist noch eine Vorabversion (`V1-dev1`, `V1-dev2`, `V1-dev3` …). **V1** ist die Version, die alles kann, was die HOF Suite
kann. Was fertig ist und was noch fehlt, steht in der [Roadmap](ROADMAP.de.md), was sich in jeder Version geändert hat, im
[Änderungsprotokoll](CHANGELOG.md) (nur auf Englisch).

## Hofwerk und HOF Suite

Die [HOF Suite](https://gitlab.com/omsi2/hof-suite) (Lukas G., 2018–2019) ist der Editor, nach dessen Vorbild Hofwerk gebaut ist. Hofwerk liest und
schreibt dieselben `.hof`-Dateien, du kannst also zwischen beiden wechseln. Die Liste beruht auf dem öffentlichen Änderungsprotokoll und dem
Quellcode-Aufbau der HOF Suite (siehe [Roadmap](ROADMAP.de.md)), es kann also ein Detail fehlen.

| | HOF Suite | Hofwerk |
| --- | --- | --- |
| Systeme | Windows | **Windows und Linux** (Linux noch nicht auf einem echten Desktop getestet) |
| Ziele, Haltestellen, Routen und Betriebshof-Einstellungen bearbeiten | Ja | Ja |
| Behält die Kodierung der Datei, listet Probleme in einer Datei auf | Ja | Ja |
| Namen für die String-Felder | Vorlagen | String-Vorlagen, eingebaut und geteilt in `Presets/`, und Import einer eigenen Strings-Datei |
| Neue Datei aus einer OMSI-Karte | Ja (*Import map*) | Ja, mit Bild und Namen der Karte |
| Karte in eine bestehende Datei importieren, einzelne Fahrt importieren | Ja | Ja, nichts wird doppelt hinzugefügt, und „Rückgängig" nimmt es zurück |
| Eine Datei in mehrere Bus-Ordner exportieren (die Liste bleibt in der Datei) | Ja | Ja, mit Sicherung ersetzter Dateien |
| Haltestellen-Verknüpfungen (Fahrplandateien, die ihre Halte als Zahlen schreiben) | Ja | Ja |
| Übersetzungen | Englisch und Deutsch | Englisch und Deutsch |
| Updates in der App, „Neuigkeiten" | Nicht in seinem Änderungsprotokoll | Ja |
| Helles, dunkles und Systemdesign | Nicht in seinem Änderungsprotokoll | Ja |
| Rückgängig und Wiederholen, automatisches Speichern, Nachfrage vor dem Schließen mit ungespeicherten Änderungen | Nicht in seinem Änderungsprotokoll | Ja |
| Neue Dateien enthalten eine Anleitung in einfachen Worten zu jedem Abschnitt | Nicht in seinem Änderungsprotokoll | Ja |
| Fehlerfenster mit fertigem Fehlerbericht | Nicht in seinem Änderungsprotokoll | Ja |
| Zweite Windows-Version für Rechner, auf denen WinUI scheitert | Entfällt | Ja (die Avalonia-Version, bei Bedarf heruntergeladen) |
| Installer | Setup-Programm | Ein-Datei-Installer, online oder offline, ohne Administratorrechte |

Hofwerk wird **V1**, wenn die „Noch nicht"-Zeilen erledigt sind. Danach geht es über die HOF Suite hinaus (siehe Roadmap).

## Installation

Lade den Installer für dein System von der Hofwerk-Website herunter (Seite „Download"; dieselben Dateien liegen auf der Seite [Releases](../../releases)). Jeder ist eine einzelne Datei. Unter Windows gibt es
zwei: `Hofwerk-Setup-<Version>-win-x64.exe` enthält die App (funktioniert offline), und `Hofwerk-WebSetup-<Version>-win-x64.exe` ist
klein und lädt die App während der Installation herunter. Beide zeigen ihre Schritte: Herunterladen, Entpacken, Dateien verschieben, Aufräumen, Schließen.

- **Windows:** `Hofwerk-Setup-<Version>-win-x64.exe`. Doppelklick genügt. Es installiert nur für dich (ohne Administratorrechte),
  legt einen Startmenü-Eintrag an und kann eine Desktop-Verknüpfung anlegen und Hofwerk unter „Öffnen mit" für `.hof`-Dateien anbieten. Entfernen kannst du es unter Windows
  *Installierte Apps* oder mit dem Deinstallationsprogramm im Installationsordner. Der Installer ist noch nicht
  signiert, deshalb fragt Windows SmartScreen eventuell nach; wähle *Weitere Informationen → Trotzdem ausführen*.
- **Linux:** `Hofwerk-Setup-<Version>-linux-x64.tar.gz`. Entpacken und die Datei darin ausführen:

  ```bash
  tar -xzf Hofwerk-Setup-*-linux-x64.tar.gz
  ./Hofwerk-Setup-*-linux-x64
  ```

  Es installiert nach `~/.local/share/hofwerk`, fügt Hofwerk deinem Anwendungsmenü hinzu und legt einen Befehl `hofwerk` an. Der Menüeintrag
  hat die Aktion *Hofwerk deinstallieren*. *Noch nicht auf einem echten Linux-Desktop getestet.*

Für Skripte laufen beide Installer auch ohne Fenster:
`Setup --silent [--dir PFAD] [--language de] [--desktop-shortcut] [--file-association]` und `Uninstall --silent --uninstall`.

## Selbst bauen

Du brauchst das [.NET 10 SDK](https://dotnet.microsoft.com/download).

```bash
./test.ps1                                             # alle Tests; siehe tests/README.md
dotnet run --project src/Hofwerk.WinUI -r win-x64      # Windows
dotnet run --project src/Hofwerk.Linux                 # Linux (läuft auch unter Windows)
```

Den Installer selbst bauen (PowerShell 5.1 oder 7; das Ergebnis landet in `dist/`):

```powershell
pwsh installer/build-installer.ps1 -Rid win-x64 -Version 0.1.0
```

## Eine Sprache hinzufügen

Jede Sprache ist eine JSON-Datei in `src/Hofwerk.Installer.Engine/Languages/`. Wie man eine neue anlegt und prüft, steht in der
[Anleitung dort](src/Hofwerk.Installer.Engine/Languages/README.md) (auf Englisch).

## Projektaufbau

| Ordner | Inhalt |
| --- | --- |
| `src/Hofwerk.Core` | `.hof`-Parser und -Schreiber, OMSI/Steam-Finder, zuletzt geöffnete Dateien, Vorlagen-Bibliothek. Keine Oberfläche. |
| `src/Hofwerk.ViewModels` | Alles, was die Bildschirme zeigen und tun, geteilt von beiden Oberflächen. |
| `src/Hofwerk.WinUI` | Windows-Oberfläche (WinUI 3). |
| `src/Hofwerk.Linux` | Linux-Oberfläche (Avalonia). |
| `src/Hofwerk.Installer.Engine` | Installieren und Deinstallieren: Dateien kopieren, Verknüpfungen, Registry- oder Menüeinträge. Außerdem die Sprachdateien. Keine Oberfläche. |
| `src/Hofwerk.Installer` | Das Setup-Fenster (Avalonia). Dasselbe Programm ohne angehängte App ist das Deinstallationsprogramm. |
| `installer/` | Skripte, die die Ein-Datei-Installer bauen und so testen, wie Nutzer ihnen begegnen (UI Automation). |
| `Presets/` | In die App eingebaute String-Vorlagen. Ein Ordner pro Vorlage. |
| `tests/` | Unit-Tests, darunter ein Round-Trip-Test mit echten OMSI-Dateien, wenn OMSI installiert ist. Starte sie mit `./test.ps1` ([so geht's](tests/README.md)). |
| `docs/` | Bilder für diese README und die Website. |
| `website/` | Die Website: Downloads, Neuigkeiten, Vergleich, Roadmap. Siehe [website/README.md](website/README.md). |

## Releases

Ein Tag wie `V1-dev3` führt die Tests aus, baut die Windows- und Linux-Installer, testet sie und veröffentlicht sie als
GitHub-Release (`.github/workflows/release.yml`). Die Website (`.github/workflows/website.yml`) kopiert dann die neuesten Installer auf ihre Download-Seite. Das Release enthält die Installer, die gepackte App, die In-App-Updates herunterladen
(`Hofwerk-App-*.hwpack`), die gepackte zweite Windows-Version (`Hofwerk-Compat-*.hwpack`) und Notizen aus dem passenden
Abschnitt von [CHANGELOG.md](CHANGELOG.md). Verschiebe die Notizen vor dem Taggen von `[Unreleased]` unter eine neue Versionsüberschrift; eine Prüfung bei jedem Pull
Request stellt sicher, dass jede Version Notizen hat.

## Danksagung

Ein großes Dankeschön an **Lukas G.**, den Schöpfer der originalen **HOF Suite**, für die Inspiration, Hofwerk zu bauen.
Das ursprüngliche Werkzeug hat `.hof`-Dateien für die ganze OMSI-Community zugänglich gemacht, und es wurde seit 2019 nicht mehr aktualisiert.
Ich wollte diese Idee mit einer modernen Oberfläche für heutige Windows- und Linux-Spieler weitertragen.

Danke auch an alle in der OMSI-Community, die Busse, Karten und Erweiterungen machen und teilen, und an alle,
die String-Vorlagen beisteuern.

## Quellen und Lizenzen

- Die originale **HOF Suite** von **Lukas G.** (© 2018): https://gitlab.com/omsi2/hof-suite. Die Vorlage „HOF Suite Default"
  stammt daraus. Hofwerk ist eine neue Codebasis, kein Fork. Die Lizenz verlangt Namensnennung, Links zum
  Original und öffentlichen Quelltext; Einzelheiten in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
- Alle, die in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) für String-Vorlagen genannt werden.
- OMSI 2 ist ein Produkt seiner jeweiligen Inhaber. Dieses Projekt steht in keiner Verbindung zu ihnen.

*Dies ist die deutsche Fassung der [englischen README](README.md). Bei Abweichungen gilt die englische.*
