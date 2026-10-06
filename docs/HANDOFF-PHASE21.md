N+1-Handoff (Phase 21): Tickrate als Parameter – Präsentation, Spielschicht, UI, Ton, Client, Werkzeuge und Tests (bildgleich bei 30 Hz)

Du arbeitest autonom am lokalen Projekt `C:\Users\cp\RiderProjects\CnCNew`. Lies zuerst `CLAUDE.md`, **`docs/MULTIPLAYER-PLAN.md`**, **`docs/TICKRATE-AUDIT.md`** (§2, §3, §4, §6, §7), `docs/PRESENTATION-OBJECTS.md`, `docs/PRESENTATION-FX.md`, `docs/CLIENT-FX.md`, `docs/GAME-AUDIO.md`, `docs/GAME-HUD.md`, `docs/GAME-SHELL.md`, `docs/SCRIPT-ENGINE.md` §7a und den Abschlussbericht der Phase 20.
Voraussetzung: Phase 20 ist abgeschlossen – die Simulation trägt ihre Rate als Wert der Welt und läuft bei 60 und 120 Hz durch.
Diese Phase darf **parallel zu Phase 22** (Festkomma) in einem eigenen Worktree laufen; gemergt wird 21 vor 22.

## Ziel in einem Satz

Nichts außerhalb der Simulation nimmt an, dass ein Tick 1/30 Sekunde dauert. **Bei 30 Hz bleibt jedes Bild, jeder Ton und jede Prüfsumme gleich;** bei 60 Hz dauert alles Sichtbare und Hörbare in Sekunden gleich lang wie bei 30 Hz.

## Ehrlicher Status (Ausgangslage; Stellen aus `docs/TICKRATE-AUDIT.md`)

1. **Partikel, Lichter, Leuchtspuren und Decals laufen am Simulationstick.** `LiveGame.SyncAfterTick` ruft `Fx.Update()` einmal je Tick; die FX-Daten sind in 30er-Bildern des Originals verfasst (`FxValueReader.FramesPerSecond`). Bei 60 Hz liefe jeder Effekt doppelt so schnell. `FxExecutor` mischt außerdem FX-Bilder mit Simulationsticks (Drosselung der Schadenseffekte).
2. **Kopien der 30** in `Suspension.cs`, `FrameProjector.cs` (`3 * 30`), `UnitConditionTracker.cs`, `SoundCues.cs`, `HudMessageFeed.cs`, `InGameUiSettings.cs` (eigene Konstante, von `MissionText` benutzt), `MissionPresentation.cs` (`LetterboxFadeFrames`), `LiveGame.cs`, `Program.cs` (Rauchtest-Schritt), mehrere Werkzeuge.
3. **Hüllkurven in Ticks:** Mündungsfeuer 4 Ticks, Feuerzustand genau ein Tick (`ConditionDeriver`), Auswahlblinken, Heilsymbol, Jubel, Tonhinweis-Fenster, EVA-Abstände, HUD-Meldungen, Superwaffen-Uhren.
4. **Die Missionspräsentation benutzt den Tick als Uhr** (Kamera, Blenden, Untertitel, Zähler, Bildfilter je Schritt).
5. **Integration je Tick in der Darstellung:** Federung (`Suspension`), Alpha-Zerfall der Missionstexte, Bildfilter.
6. **Bereits richtig** (an Wandzeit bzw. Bildzeit): Modell- und Skelettanimation, Texturanimation, Wasser, Kamera, Befehlsmarken, Shell-Übergänge. Nicht anfassen, aber im Zeitmaß-Test mitprüfen.
7. **Tests und Werkzeuge:** rund 105 feste Schritte `1.0 / 30` und rund 845 Zeilen mit von Hand geschriebenen Tickerwartungen in 84 Testdateien (nicht alle betreffen diese Phase).

**Nachtrag aus Phase 20 (6. Oktober 2026, Stand `9299a2d`):** die Simulation ist umgestellt und bei 30 Hz ergebnisgleich (`docs/TICKRATE-MIGRATION.md`). Maßgeblich für diese Phase sind dort §6 (16 Punkte, was Verbraucher außerhalb der Simulation ändern müssen), §8 (Einheiten der öffentlichen Werte: was Bilder des Originals und was Ticks der Rate sind) und §9 (Werkzeuge). Die wichtigsten Punkte: `SimulationWorld.VisionIntervalTicks` ist nur noch ein Alias für Aufrufer außerhalb der Simulation (ersetzen durch `world.VisionInterval`, dann entfernen); `GameData.SessionRate` ist die eine benannte Rate der Spielschicht und baut den Katalog; `SaveGame` (Version 1) schreibt und prüft keine Rate; 14 + 1 + 3 Verwendungen von `SimulationRate.OriginalFramesPerSecond` in `CnCNew.Game`, `CnCNew.Presentation` und den Werkzeugen sind nur übersetzbar gemacht, nicht entschieden; Sabotagedauern verlassen die Simulation als Tickzahl im Ereigniswert; `TensileFormationUpdate.Life` und der Timer-`CounterValue` der Skript-VM zählen Ticks der Rate; `ScriptOutput.Arguments` sind unskalierte Kartenparameter (Bilder des Originals), `ScriptOutput.Tick` ist ein Tick der Welt; `StandingOrders`/`LiveGame.BeforeTick` erzeugen Befehle im Ticktakt (ändert bei 60 Hz den Replay-Inhalt). Die Simulation läuft bei 60 und 120 Hz deterministisch durch (Referenzmatch, zwei KI-Gefechte, drei Missionen); ihr Verhalten dort ist unbewertet. Werkzeuge: `--tick-rate N` an `SimTool` und `CampaignTool`. Leistungsvergleiche nur als direkter Wechsel zweier Binärverzeichnisse im selben Zeitraum (der Rechner lieferte am 6. Oktober abends 24–44 % höhere Absolutwerte als `docs/PERFORMANCE.md` §6a bei gleicher Geschwindigkeit beider Stände).

## Entscheidungen, die feststehen

- **Zwei Begriffe, zwei Namen.** Die *Simulationsrate* kommt aus der Welt und ist veränderlich. Das *Originalbild* (1/30 s) ist die feste Einheit, in der das Original Client-Inhalte verfasst hat (Partikel, Shell-Übergänge, `InGameUI`-Zeiten, Kamerawerte). Es gibt genau eine benannte Konstante dafür außerhalb der Simulation, und sie wird nie mit der Simulationsrate verwechselt.
- **Die Uhr der Präsentation ist Spielzeit in Sekunden**, abgeleitet aus Tick, Rate und Interpolationsanteil. Sie steht bei Pause, läuft mit der Spielgeschwindigkeit und springt nicht. Wandzeit bleibt für alles, was vom Spiel unabhängig ist (Shell, Mauszeiger, Kamera).
- Die Präsentation ändert die Simulation nicht: Hashes des Referenzmanifests bleiben unverändert.

## Auftrag (in dieser Reihenfolge)

1. **Ausgangszustand reproduzieren.** Build, alle Testprogramme, Manifest, und alle Darstellungsprüfsummen bei 30 Hz als Messlatte sichern: die 54 Screen-Hashes und der HUD-Drawlist-Hash (`tests/CnCNew.Game.Tests/baselines`), die UI-Renderer-PNGs, die Capture-Szenen (`scripts/capture-*.ps1`, `audit-scenarios.ps1`), FX-Bench- und FX-Audit-Hashes, die Audio-Capture-Prüfsummen.
2. **Spielschicht.** `LiveGame`: Tickdauer aus der Rate der Welt; die Obergrenze der Ticks je Bild als Zeitbudget (heute 8 Ticks ≙ 0,27 s Spielzeit) statt als Tickzahl; `SimulationWorld.VisionIntervalTicks`-Kopplung der Nebeldarstellung über die Welt; die Spielzeit-Uhr bereitstellen.
3. **Effekte.** `FxExecutor`/`ParticleWorld` laufen auf dem Originalbild-Raster, angetrieben von der Spielzeit (Akkumulator in `LiveGame` oder im Executor), nicht vom Simulationstick. Die Drosselung der Schadenseffekte bekommt eine einzige Uhr. Bei 30 Hz fällt jedes Originalbild auf einen Tick – die FX-Prüfsummen bleiben gleich.
4. **Objektdarstellung.** Mündungsfeuer, Feuerzustand, Heilsymbol, Jubel, Auswahlblinken, Abklingkurven: Dauern in Sekunden bzw. Originalbildern. `ConditionDeriver`: „hat im letzten Tick gefeuert“ wird zu „hat innerhalb der letzten 1/30 s gefeuert“ (sonst wäre der Zustand bei 60 Hz nur halb so lang sichtbar). `Suspension`: Feder und Dämpfer mit der Schrittweite rechnen, Daten je Originalbild.
5. **Ton und HUD.** `EvaCatalog`/`AudioDirector` (Abstände, Sperrzeiten), `SoundCues` (Fenster, Höchstzahl je Zeit statt je Tick), `HudMessageFeed`, `SuperweaponTimers`, Befehlsrückmeldungen (`CommandFeedback`-Dauern).
6. **Mission.** `MissionPresentation` (Uhr, Letterbox-Blende als Wandzeit wie im Original, Bildfilter je Zeit), `MissionText` (`InGameUiSettings` ohne eigene Ratenkonstante), `LiveMission.ClientPresentationLatencyTicks` als Zeit; `CAMERA_BW_MODE_*` in Originalbildern.
7. **Client und Werkzeuge.** `Program.cs` (fester Schritt im Rauchtest aus der Rate), `--tick-rate N` am Client (Standard 30 bis Phase 23), `PresentationTool`, `UiRenderTool`, `CampaignTool`, die Demo- und Capture-Skripte: Tickbudgets aus Spielzeit berechnen.
8. **Tests.** Feste Schritte `1.0 / 30` über einen Helfer aus der Rate der jeweiligen Welt; Tickerwartungen, die eigentlich Zeiten meinen, in Spielzeit ausdrücken (ein Helfer, der Sekunden in Ticks der Testwelt wandelt). Erwartungen, die wirklich Ticks meinen (Scheduler, Sequenzen), bleiben. Jede Testdatei aus dem Inventar wird bewertet; das Ergebnis ist eine Liste „Datei × umgestellt / bewusst in Ticks / nicht betroffen“.
9. **Zeitmaß-Test bei 60 Hz.** Für jedes Element aus den Punkten 3 bis 6 eine Prüfung, dass seine Dauer in Sekunden bei 30 und 60 Hz gleich ist (Toleranz: ein Originalbild): Lebensdauer eines Partikelsystems, Mündungsfeuer, Blinkfolge der Übernahme, Heilsymbol, EVA-Sperrzeit, Untertiteldauer, Kamerafahrt, Federweg-Ausschwingzeit. Die Simulation bei 60 Hz ist dafür gut genug (Phase 20), auch wenn ihre Regeln dort noch nicht stimmen.
10. **Suchlauf.** Die Muster aus `docs/TICKRATE-AUDIT.md` §8 liefern außerhalb der Simulation keinen unbewerteten Treffer mehr; ein Quelltexttest verbietet in `CnCNew.Game`, `CnCNew.Presentation`, `CnCNew.Ui`, `CnCNew.Client.Stride` jede Ratenkonstante außer der einen für das Originalbild.
11. **Doku:** `docs/PRESENTATION-OBJECTS.md`, `docs/PRESENTATION-FX.md`, `docs/CLIENT-FX.md`, `docs/GAME-AUDIO.md`, `docs/GAME-HUD.md`, `docs/SCRIPT-ENGINE.md` §7a, `docs/TICKRATE-AUDIT.md` (Präsentationsteil erledigt), `docs/MULTIPLAYER-PLAN.md`, `CLAUDE.md`, README, ARCHITECTURE, ROADMAP, IMPLEMENTATION-PLAN.

## Engstellen und Showstopper dieser Phase

- **Prüfsummengleichheit bei 30 Hz** ist der Beweis; jede Abweichung ist ein Fehler im Umbau. Wo ein Akkumulator in Gleitkomma einen Schritt um ein Bild verschiebt, die Zeit ganzzahlig führen (Ticks und Rate), nicht in `double` summieren.
- **Rauchtests und Golden-Hashes beweisen Reproduzierbarkeit, nicht Richtigkeit** (`CLAUDE.md`). Der Zeitmaß-Test belegt Dauern; ob es bei 60 Hz *richtig aussieht und klingt*, kann nur ein Mensch sagen. Eine Sichtprüfliste für Phase 23 anlegen (Effekte, Mündungsfeuer, Federung, Missionsintro), nichts davon als abgenommen melden.
- **Was bei 60 Hz bewusst anders wird:** die Interpolation hat halb so große Schritte; Positionen sind feiner. Das ist gewollt und kein Befund.

## Nicht Gegenstand

Mehr Interpolation (Turm, Baufortschritt) und Extrapolation (Phase 29); die Umstellung des Standards auf 60 Hz (Phase 23).

## Abnahme

Alle unter Punkt 1 gesicherten Prüfsummen sind bei 30 Hz unverändert; das Referenzmanifest besteht; der Zeitmaß-Test besteht bei 60 Hz; der Suchlauf ist leer; die Testliste ist vollständig. Der Abschlussbericht nennt Zahlen mit Commit-Hashes und ausdrücklich, was kein Mensch gesehen hat. Am Ende die „Ausgangslage“ in `docs/HANDOFF-PHASE22.md` aktualisieren.
