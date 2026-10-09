# CnCNew

> **English abstract.** CnCNew is an independent C#/.NET 10 reimplementation of the SAGE engine runtime needed for
> *Command & Conquer™ Generals – Zero Hour*, plus a patcher that builds a new local game directory (plain files, no
> `.big` archives, our engine) from a copy of the game **you already own** — your installation is only read, never
> modified. The repository contains **no game data**. Status: the simulation is deterministic at 60 Hz and checked
> against a reference manifest; skirmish and campaign run headless; a Windows client renders and plays sound — but
> **no human has accepted its look, sound or playability, and nothing here is a finished game**. Multiplayer is
> planned, not built. Licence: GPL-3.0-or-later; not affiliated with or endorsed by Electronic Arts (see `NOTICE.md`).
> The documentation is in German.

## Was das ist – und was nicht

CnCNew ist eine eigenständige Neuimplementierung der für *Command & Conquer Generals – Zero Hour* benötigten
SAGE-Laufzeit in C#/.NET 10: Datenmodell, deterministische Simulation, Präsentation und ein Windows-Client auf
Stride 4.3 / Direct3D 11. Dazu gehört ein Patcher, der aus einer vorhandenen Installation des Spiels ein neues lokales
Spielverzeichnis erzeugt, in dem jede Datei offen liegt (keine `.big`-Archive) und das mit dieser Engine läuft. Die
Installation des Anwenders wird dabei nur gelesen.

Es ist **kein fertiges Spiel**, kein Mod des Originalprogramms, kein Ersatz für den Kauf und kein Download von
Spieldaten. Scope des ersten Produkts: Singleplayer (Gefecht und Kampagne) und – geplant, nicht gebaut –
Mehrspieler-Gefechte mit bis zu acht Spielern über einen Dedicated Server (`docs/MULTIPLAYER-PLAN.md`). Nicht im Scope:
vernetzte Kampagne, Matchmaking/Rangliste, NAT-Durchdringung, GameSpy-Kompatibilität, Anti-Cheat, binäre
Kompatibilität zu alten Mods und Savegames.

## Stand (9. Oktober 2026)

Gemessen ist, was Werkzeuge und Tests ohne Menschen belegen können; Einzelheiten und Zahlen stehen in den verlinkten
Dokumenten, die Messlatte für Ergebnisse ist `baselines/reference-manifest.json`.

- **Daten:** die Spieldaten werden streng typisiert geladen; unbekannte Schlüssel, Module oder Referenzen fallen nie
  still auf Standardwerte zurück, jeder Kompatibilitätsfall ist gezählt und gegen `baselines/compatibility.json`
  abgesichert (`docs/TYPED-DEFINITIONS.md`, `docs/PHASE3-COMPATIBILITY.md`).
- **Simulation:** Festkomma (Q39.24), deterministisch, standardmäßig 60 Hz (30 und 120 Hz werden über eine
  Raten-Äquivalenz-Suite gegen 30 Hz als Definition geprüft). Alle 183 Simulationsmodule des Katalogs sind
  implementiert (offen: die 22 Präsentationsmodule), alle 2.115 Objekte der Retail-Daten instanziierbar. Ein
  Referenzmanifest mit 50 Läufen (Referenzmatch, Stresstest, KI-Gefechte, Missionen) wird bei jedem Phasenabschluss
  unter Windows geprüft (`docs/SIMULATION-CORE.md`, `docs/DETERMINISM.md`, `docs/TICKRATE-MIGRATION.md` §15).
- **Gefecht und Kampagne, headless:** KI-gegen-KI-Gefechte laufen deterministisch (14 von 16 Referenzläufen enden mit
  einem Sieger). Die Skript-VM führt die Kartenskripte aus; ein Prüfwerkzeug erreicht auf allen 24 eindeutigen
  Kampagnenkarten Sieg und Niederlage über die eigenen Skripte der Karte – es ersetzt dabei Spielerhandlungen durch
  Welteingriffe und belegt die Skriptkette, **nicht die Spielbarkeit**. Ein Agent, der nur Befehle gibt, gewinnt drei
  dieser Karten (`docs/SCRIPT-ENGINE.md`, `docs/CAMPAIGN-AND-SAVEGAMES.md`).
- **Client:** zeichnet Gelände, Objekte, Effekte, HUD und die Menüs aus den Originallayouts, gibt Ton aus und nimmt
  Eingaben an; Gefecht, Mission, Shell und ein Zuschauermodus starten. **Kein Mensch hat Aussehen, Ton oder
  Spielbarkeit abgenommen**, und bei 60 Hz hat noch niemand das Spiel gesehen oder gehört; die bisherigen Sichtungen
  fanden Fehler (`docs/DEMO-FINDINGS.md`). Rauchtests und Prüfsummen belegen Reproduzierbarkeit, nicht Richtigkeit.
- **Mehrspieler:** nur geplant. Gebaut ist die Vorarbeit (Messbasis, Leistung, Tickrate als Wert der Welt, Festkomma,
  60 Hz); Sitzungskern, Protokoll und Server folgen ab Phase 24.
- **Offen und benannt:** Determinismus ist auf einem Rechner belegt, ein zweiter physischer Rechner steht aus; das
  Tickmaximum verfehlt sein Soll; kein Videodecoder; die Balance der KI ist nicht bewertet; elf Entscheidungen liegen
  beim Projektinhaber (`docs/MULTIPLAYER-PLAN.md` §7a). Roadmap: `docs/ROADMAP.md`.

## Rechtliches in Kürze

- **Eigene Kopie nötig.** Das Repository enthält keine Spieldaten und nichts daraus Exportiertes – nur Prüfsummen,
  Größen, Namen und Anzahlen zur Prüfung (`baselines/`). Die Rechte an den Spieldaten liegen bei Electronic Arts Inc.
- **Lizenz:** `GPL-3.0-or-later` ([LICENSE.md](LICENSE.md)). Der Code ist mit Blick in den von Electronic Arts unter
  GPL v3 (mit Zusatzbedingungen) veröffentlichten Quelltext entstanden; er ist eine neu geschriebene, als solche
  gekennzeichnete Implementierung und nicht das Originalprogramm. Einzelheiten, die Zusatzbedingungen und die
  Lizenzen der Komponenten Dritter stehen in [NOTICE.md](NOTICE.md).
- **Marken:** „Command & Conquer“, „Generals“ und „Zero Hour“ sind Marken von Electronic Arts Inc. Dieses Projekt steht
  in keiner Verbindung zu Electronic Arts und wird von Electronic Arts weder unterstützt noch gebilligt.

## Voraussetzungen

- Windows (einzige Zielplattform; Linux kommt später nur für den Dedicated Server in Betracht)
- .NET SDK 10.0.401 oder ein neueres Feature-Band derselben Hauptversion (`global.json`), PowerShell 7 (`pwsh`)
- eine frische **englische Steam-Installation** von *Command & Conquer Generals – Zero Hour* – die eine unterstützte
  Ausgangsversion; sie ist in `baselines/steam-master.json` über Pfade, Größen und SHA-256 beschrieben

## Schnellstart für Anwender

```powershell
pwsh scripts/install.ps1                 # optional: -GameDir <Steam-Installation> -Target <Zielverzeichnis>
pwsh scripts/uninstall.ps1               # entfernt das erzeugte Spielverzeichnis wieder
```

`install.ps1` liest die Installation, ohne sie zu verändern, und legt das neue Spielverzeichnis an (Vorgabe
`%LOCALAPPDATA%\CnCNew\Game`). Was dabei geprüft wird, wie das Spiel gestartet wird, Optionen und Fehlerbilder
beschreibt [docs/INSTALLATION.md](docs/INSTALLATION.md).

## Für Entwickler

```powershell
pwsh scripts/dev-content.ps1             # Entwicklerinstallation nach <repo>\game (git-ignoriert): game\content
dotnet build CnCNew.slnx                 # sauberer Build: 0 Warnungen (TreatWarningsAsErrors)
dotnet run --project tests/CnCNew.Simulation.Tests
dotnet run --project tests/CnCNew.Simulation.Tests -- --content-dir game/content
dotnet run --project tests/CnCNew.Data.Tests -- --content-dir game/content
dotnet run --project tools/CnCNew.IniTool -- compat-check game/content baselines/compatibility.json
```

Tests sind eigenständige Konsolenprogramme (Exitcode ungleich 0 bei Fehler), kein xUnit/NUnit. Die vollständige Liste
der Testprogramme, Audits, Manifestprüfungen und Werkzeugbefehle steht in [CLAUDE.md](CLAUDE.md), Abschnitt „Build,
Tests, Audits“. Tech-Demos im Fenster (bauen den Client und brauchen `game/content`):

```powershell
.\scripts\demo-skirmish.ps1              # Gefecht gegen die KI
.\scripts\demo-mission.ps1               # Kampagnenmission
.\scripts\demo-shell.ps1                 # Hauptmenü
.\scripts\demo-spectator.ps1             # KI gegen KI zum Zuschauen
```

## Aufbau des Repositorys

| Verzeichnis | Inhalt |
|---|---|
| `src/` | die Bibliotheken und der Client: `CnCNew.Archives`, `.IO`, `.Import` (Importseite), `.Content` (Laufzeit-Dateisystem), `.Formats`, `.Images`, `.W3D`, `.Audio` (Formate), `.Data` (typisierte Definitionen), `.Simulation` (deterministischer Kern), `.Presentation`, `.Ui`, `.Game` (Spielschicht, Shell, HUD), `.Client.Stride` (Windows-Client) |
| `tools/` | Konsolenwerkzeuge: `AssetTool` (Import, Installation), `IniTool`, `MapTool`, `ImageTool`, `W3dTool`, `SimTool` (Referenzläufe, Manifeste, Leistung), `CampaignTool`, `PresentationTool`, `UiRenderTool` |
| `tests/` | ein Testprogramm je Projekt, synthetische Fixtures |
| `scripts/` | PowerShell-Skripte: Installation, Entwicklerbestand, Demos, Captures, Audits, Leistungsprofil |
| `baselines/` | eingecheckte Messlatten: Kompatibilitätsbasislinie, Eingangs- und Referenzmanifest, Beschreibung der Ausgangsversion (nur Pfade, Größen, Prüfsummen) |
| `build/` | MSBuild-Bausteine (Stride-Werkzeugpakete, Asset-Compiler-Target) |
| `docs/` | Dokumentation; Einstieg über [docs/README.md](docs/README.md) |

Schichten und Abhängigkeitsregeln (Import → Runtime-Content → Datenmodelle → Simulation → Präsentation) stehen in
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md). Nicht im Repository und von Git ignoriert: `game/` (Entwicklerinstallation),
`.local/` (lokale Arbeitsdaten), `references/` (fremde Quelltexte zum Nachlesen), `bin/`, `obj/`.

## Dokumentation

Die Übersicht aller Dokumente, nach Themen geordnet, steht in [docs/README.md](docs/README.md). Erste Anlaufstellen:
[docs/ARCHITECTURE.md](docs/ARCHITECTURE.md), [docs/ROADMAP.md](docs/ROADMAP.md),
[docs/IMPLEMENTATION-PLAN.md](docs/IMPLEMENTATION-PLAN.md), [docs/SIMULATION-CORE.md](docs/SIMULATION-CORE.md),
[docs/DEMO-FINDINGS.md](docs/DEMO-FINDINGS.md).

## Wie das Projekt entsteht

Die Arbeit läuft in Phasen: jede Phase hat einen in sich vollständigen Auftrag (`docs/handoffs/`), beginnt mit dem
reproduzierten Ausgangszustand und endet mit Messungen, aktualisierter Dokumentation und dem Auftrag der nächsten
Phase. Geschrieben wird der Code überwiegend von Coding-Agenten unter Anleitung des Projektinhabers;
[CLAUDE.md](CLAUDE.md) (mit dem Verweis [AGENTS.md](AGENTS.md)) ist deren verbindliche Arbeitsanweisung und zugleich
die genaueste Beschreibung der Regeln und des gemessenen Stands. Was nur ein Mensch beurteilen kann, gilt als offen,
bis es stattgefunden hat.

## Mitarbeiten

Es gibt noch keine CI: wer etwas beiträgt, lässt die Testprogramme der berührten Projekte selbst laufen
([CONTRIBUTING.md](CONTRIBUTING.md)). Spieldaten oder daraus Exportiertes werden nie committet.
