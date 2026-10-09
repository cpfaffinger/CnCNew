# Installation: aus der eigenen Steam-Installation ein spielbares Verzeichnis

Stand: 9. Oktober 2026 (alle Zahlen an diesem Tag auf einem Rechner gemessen; neu messen)

Das Repository enthält nur selbst geschriebenen Code. Spieldateien und alles, was aus ihnen exportiert
wird, liegen nie im Repository. Ein spielbares Verzeichnis entsteht auf dem Rechner des Nutzers: der
**Patcher** liest die eigene Steam-Installation von *Command & Conquer Generals Zero Hour* und legt an
anderer Stelle ein neues Spielverzeichnis an.

Rechtlicher Rahmen: Der Nutzer stellt sein eigenes, bei Steam erworbenes Spiel bereit; dieses Projekt
verteilt keine Spieldateien und keine daraus erzeugten Dateien. Näheres steht in `LICENSE` und `NOTICE`.

## Was der Patcher tut – und was nie

Er tut:

1. die Steam-Installation finden (oder `-GameDir` nehmen) und **Datei für Datei** gegen die eingecheckte
   Masterbeschreibung `baselines/steam-master.json` prüfen (Pfad, Größe, SHA-256);
2. nur bei vollständiger Übereinstimmung den Runtime-Bestand nach `<Ziel>\content` exportieren
   (`RuntimeContentImporter`, Schema 2): **kein `.big`-Archiv wird übernommen, jede Datei liegt einzeln im
   Verzeichnis**;
3. den Export prüfen (jede Datei gegen Größe und SHA-256 des Manifests) und das erzeugte
   `content-manifest.json` mit dem in der Masterbeschreibung festgehaltenen vergleichen (Bytezahl,
   SHA-256, Dateizahl) – „der Export ist genau der bekannte“ ist ein einziger Vergleich;
4. `<Ziel>\install.json` schreiben, den Client nach `<Ziel>\bin` veröffentlichen und
   `<Ziel>\Start-CnCNew.cmd` anlegen.

Er tut nie:

- in das Steam-Verzeichnis schreiben, dort etwas umbenennen oder löschen (alle Quelldateien werden nur
  lesend mit `FileShare.Read` geöffnet; ein Test vergleicht Dateiliste, Größen und Zeitstempel der Quelle
  vor und nach Installation, Reparatur und Deinstallation);
- eine andere Version, Sprache oder eine gemoddete Installation stillschweigend annehmen – es gibt
  **keinen** Schalter, der die Prüfung abschaltet;
- in ein Ziel schreiben, das im Spielverzeichnis liegt, es enthält oder nicht leer und nicht unsere eigene
  frühere Installation ist.

## Voraussetzungen

- Windows (Client: Direct3D 11) und PowerShell 7 (`pwsh`).
- Das Spiel bei Steam besitzen und installiert haben: Steam-App **2732960**, **englische** Sprachfassung,
  frische Installation ohne Mods. Das ist die eine bekannte Masterversion: 350 Dateien, 2.939.721.100 Byte.
- Das .NET SDK aus `global.json`: **10.0.401** (`rollForward: latestFeature`). Das SDK wird gebraucht, weil
  Werkzeug und Client aus dem Quelltext gebaut werden.
- Rund 2,9 GB freier Platz am Ziel (gemessen: `content` 2.763.982.961 Byte in 25.609 Dateien, `bin`
  67.793.031 Byte in 172 Dateien); während einer Reparatur kurzzeitig das Doppelte von `content`, weil der
  neue Bestand neben dem alten entsteht und erst nach erfolgreicher Prüfung an seine Stelle tritt.

## Schritt für Schritt

```powershell
git clone <Repository>
cd CnCNew
pwsh scripts\install.ps1
```

Ohne Parameter sucht das Skript das Spiel unter
`C:\Program Files (x86)\Steam\steamapps\common\Command & Conquer Generals - Zero Hour`, sonst über den
Steam-Pfad der Registry (`HKCU:\Software\Valve\Steam`, `HKLM:\SOFTWARE\WOW6432Node\Valve\Steam`), die
Bibliotheken aus `steamapps\libraryfolders.vdf` und `appmanifest_2732960.acf` (`installdir`), und sagt, was
es gefunden hat. Das Ziel ist ohne Angabe `%LOCALAPPDATA%\CnCNew\Game` – außerhalb des Repositorys und
außerhalb von Steam.

| Parameter | Bedeutung |
| --- | --- |
| `-GameDir <Pfad>` | die Steam-Installation, falls sie nicht gefunden wird |
| `-Target <Pfad>` | das neue Spielverzeichnis |
| `-SkipClient` | den Client in diesem Lauf nicht veröffentlichen (ein vorhandener bleibt) |
| `-ContentOnly` | nur `content` und `install.json`, kein Client, kein Startskript |

Gestartet wird mit `<Ziel>\Start-CnCNew.cmd`. Das Skript nennt seine Pfade relativ zu sich selbst; das
Verzeichnis kann als Ganzes verschoben werden. Weitere Optionen werden an den Client durchgereicht
(z. B. `Start-CnCNew.cmd --size 1920x1080`); ohne Optionen öffnet der Client das Hauptmenü.

Ein zweiter Aufruf auf ein vorhandenes eigenes Verzeichnis ist gefahrlos: er prüft jede Runtime-Datei,
stellt fehlende und veränderte aus dem Spielverzeichnis wieder her, entfernt fremde Dateien **in
`content`** und berichtet, was er getan hat (`AlreadyComplete`, `Repaired`). Eine abgebrochene
Installation trägt `"verified": false` in `install.json` und wird beim nächsten Lauf vollendet.

### Wohin geschrieben wird

| Pfad | Inhalt |
| --- | --- |
| `<Ziel>\content` | exportierter Runtime-Bestand mit `content-manifest.json` |
| `<Ziel>\install.json` | Produkt, Version und Commit des Builds, Master-ID, Steam-App, Importerversion, Hash/Größe des Content-Manifests, Dateizahl, `verified` – ohne Zeitstempel und ohne absolute Pfade |
| `<Ziel>\bin` | veröffentlichter Client (`dotnet publish -c Release`) |
| `<Ziel>\Start-CnCNew.cmd` | Start mit `--content-dir` auf `content` |

Sonst nirgends – ausgenommen die üblichen `bin`/`obj`-Verzeichnisse des Builds im Repository.

## Was die Masterprüfung meldet und was zu tun ist

`check-install` (und jeder `install`) nennt jede Abweichung einzeln:

| Meldung | Bedeutung |
| --- | --- |
| `missing` | eine Datei des Masters fehlt |
| `renamed` | eine Datei des Masters fehlt unter ihrem Namen, liegt aber Byte für Byte unter einem anderen (etwa ein stillgelegtes Archiv `PatchWindow.big.bak`) |
| `changed` | andere Größe oder, bei gleicher Größe, anderer SHA-256 |
| `extra` | eine Datei, die der Master nicht kennt (Mods, zusätzliche DLLs, Sicherungskopien) |
| `harmless` | eine zusätzliche Datei, die **ausdrücklich** in `harmlessExtraFiles` der Masterbeschreibung steht; sie wird gezählt, ist aber keine Abweichung |

`harmlessExtraFiles` ist derzeit **leer**: es ist keine Datei bekannt, die Steam von sich aus hinzufügt.
Ein Eintrag dort ist eine belegte Einzelfallentscheidung und Review-Gegenstand; es gibt keine Regel nach
Muster oder Endung.

Bei Abweichungen:

1. Mods und alle hinzugefügten Dateien aus dem Spielverzeichnis entfernen (die `extra`-Einträge, die neuen
   Namen der `renamed`-Einträge).
2. In Steam: Bibliothek → Rechtsklick auf das Spiel → Eigenschaften → Installierte Dateien →
   „Dateien auf Fehler überprüfen“ (englisch: *Verify integrity of game files*). Steam stellt fehlende und
   veränderte Dateien wieder her, löscht aber keine Dateien, die es nicht kennt.
3. Sprache prüfen: Eigenschaften → Allgemein → Englisch.
4. Erneut prüfen.

Gemessenes Beispiel an einer gemoddeten Kopie derselben Installation: 23 Abweichungen – 1 `missing`
(`Data/INI/INIZH.big`), 1 `renamed` (`PatchWindow.big` → `PatchWindow.big.bak`), 1 `changed` (`Game.dat`),
20 `extra` (darunter `340_ControlBarProZH.big`, `400_ControlBarHDEnglishZH.big`, `990_DecalsZH.big`,
`CustomContentMaps.big`, `310_ExpandedLANLobbyMenu.big`, `d3d8.dll`). Verglichen wird ohne Beachtung der
Groß-/Kleinschreibung von Pfaden; leere Verzeichnisse zählen nicht.

## Der Entwicklerweg: `game\`

Skripte, Werkzeuge und Tests erwarten den Inhalt unter **`game/content`** im Repository. `game/` ist eine
Installation mit genau dem Aufbau einer Nutzerinstallation (`game\content`, `game\install.json`, optional
`game\bin`) und steht in `.gitignore`:

```powershell
pwsh scripts\dev-content.ps1                 # = install.ps1 -Target <repo>\game -ContentOnly
pwsh scripts\dev-content.ps1 -WithClient     # zusätzlich game\bin und game\Start-CnCNew.cmd
dotnet run --project tools/CnCNew.AssetTool -- verify game/content
dotnet run --project tools/CnCNew.AssetTool -- check-export game/content
```

`.local/` bleibt ignoriert, ist aber nicht mehr der Ort für Inhalte. Wer einen Bestand an anderer Stelle
hat, gibt ihn wie bisher mit `--content-dir` bzw. `-ContentDir` an.

## Werkzeugbefehle

Die Logik liegt in `CnCNew.Import` (`InstallationMaster`, `InstallationInspector`, `GameInstaller`,
`InstallRecord`), die Skripte sind dünne Treiber. Ein eigenes Projekt war nicht nötig: `CnCNew.Import` ist
nach den Schichtenregeln ohnehin die einzige Verbindung zwischen der Originalinstallation (Import-VFS, BIG)
und dem Runtimevertrag, und die Installation braucht genau diese Verbindung; hinzugekommen ist nur BCL-Code.
Die Masterbeschreibung ist zusätzlich in die Assembly eingebettet (`InstallationMaster.LoadBuiltIn`), damit
ein späteres GUI ohne Repository-Pfad auskommt.

```powershell
dotnet run -c Release --project tools/CnCNew.AssetTool -- check-install <Spielverzeichnis> [--master <json>]
dotnet run -c Release --project tools/CnCNew.AssetTool -- describe-install <Spielverzeichnis> <aus.json> [--name <Name>] [--id <ID>]
dotnet run -c Release --project tools/CnCNew.AssetTool -- install <Spielverzeichnis> <Ziel> [--master <json>]
dotnet run -c Release --project tools/CnCNew.AssetTool -- check-export <Content-Verzeichnis> [--master <json>]
dotnet run -c Release --project tools/CnCNew.AssetTool -- uninstall <Ziel> --yes
```

Exitcodes: 0 identisch bzw. erledigt, 1 das Spielverzeichnis (bzw. der Export) ist nicht der Master,
2 Aufruf-, Ziel- oder E/A-Fehler. Ohne `--master` gilt die eingebettete Beschreibung.

`describe-install` erzeugt `baselines/steam-master.json` (kanonisches JSON: UTF-8 ohne BOM, LF, Pfade mit
`/` in ordinaler Reihenfolge, keine absoluten Pfade, keine Zeitstempel; zwei Läufe sind byteidentisch). Die
Datei enthält Namen, Steam-App-ID (aus `steam_appid.txt` der Installation gelesen), Dateizahl, Bytesumme,
je Datei Pfad, Größe und SHA-256 sowie den erwarteten Export – **keinen Dateiinhalt**. Den erwarteten
Export berechnet der Befehl ohne zu schreiben (`RuntimeContentImporter.DescribeAsync`). Das frühere
`inventory`-Format (Liste aus `Path`/`Bytes`/`Sha256`) lieferte die Dateieinträge als Vorbild, reicht aber
nicht: es kennt weder Identifikation noch erwarteten Export und schreibt plattformabhängige Zeilenenden.
Ändert sich `RuntimeContentImporter.ImporterVersion`, muss die Beschreibung neu geschrieben werden; `install`
lehnt eine Beschreibung für eine andere Importerversion ab.

Erwarteter Export des Masters: 25.608 Runtime-Dateien, `content-manifest.json` 27.087.727 Byte, SHA-256
`b1f8b14cd5cb005afd16237759c9020a8c3f2402874d24d2ff204e61c620fbab` (Importer 2.0.0).

Tests: `dotnet run --project tests/CnCNew.Import.Tests` (synthetische Installation mit handgebautem
BIG-Archiv) und mit `-- --game-dir <Steam-Pfad>` zusätzlich der echte Master gegen die eingebettete
Beschreibung samt Export-Hash (liest nur).

## Deinstallation

```powershell
pwsh scripts\uninstall.ps1 -Target "$env:LOCALAPPDATA\CnCNew\Game" -Yes
```

Entfernt wird nur ein Verzeichnis, das unser `install.json` trägt, und davon nur `content`, `bin`,
`Start-CnCNew.cmd` und `install.json`; alles andere bleibt und wird aufgelistet, das Verzeichnis selbst
verschwindet nur, wenn es danach leer ist. Ein Verzeichnis mit `steam_appid.txt` in der Wurzel wird nie
angefasst. Es wird nichts gefragt; ohne `-Yes` geschieht nichts.

## Gemessene Zahlen (9. Oktober 2026)

| Vorgang | Dauer |
| --- | ---: |
| `check-install` auf dem Master (350 Dateien, 2,9 GB) | 2,3 s (Dateien im Cache des Betriebssystems) |
| `check-install` auf der gemoddeten Kopie | 2,5 s |
| `describe-install` (Hash aller Dateien und Export-Probelauf) | 18,5 s |
| `install` in ein neues Verzeichnis (prüfen, exportieren, prüfen) | 99,9 s beim ersten, 53,0 s bei einem weiteren Lauf in ein anderes Ziel |
| `install.ps1` gesamt mit Werkzeug-Build und Client-Veröffentlichung | 130 s |
| erneuter `install` auf eine vollständige Installation (`AlreadyComplete`) | 11,5 s |
| `AssetTool verify` auf `game/content` | 8 s |
| Realdatentest `Import.Tests -- --game-dir …` | 37 s |
| Client aus dem installierten Verzeichnis, Rauchtest eines Gefechts mit 300 Bildern | 23 s, Exitcode 0 |

## Grenzen

- Ein Skript, kein GUI. Die Logik ist als Bibliothek testbar und für ein GUI vorbereitet.
- Genau eine Version und Sprache (Steam, Englisch). Andere Fassungen brauchen eine eigene, gemessene
  Masterbeschreibung; es gibt keinen Entwicklerschalter für ungeprüfte Installationen. Wer bewusst einen
  anderen Bestand importieren will, benutzt wie bisher `AssetTool runtime-import` – das schreibt kein
  `install.json`, das Ergebnis kann sich also nicht als geprüfte Installation ausgeben.
- Der Client wird aus dem Quelltext veröffentlicht: das braucht das SDK und, weil die Veröffentlichung
  nicht eigenständig (self-contained) ist, zur Laufzeit .NET 10 auf dem Rechner.
- Der Export übernimmt den ganzen Bestand der Installation, auch Dateien außerhalb des Produktumfangs
  (Programmdateien, Handbücher, Installationsmetadaten des Originals – im Manifest als
  `out-of-product-scope` markiert). Der Client braucht sie nicht.
- `content-manifest.json` wird mit den Zeilenenden der Plattform geschrieben; der festgehaltene Hash gilt
  für Windows.
- Die Suche über Registry und `libraryfolders.vdf` ist nicht an einer Installation außerhalb des
  Standardpfads erprobt.
- Dass der installierte Client startet, ist durch einen Rauchtest belegt, nicht durch einen Menschen am
  Bildschirm.
