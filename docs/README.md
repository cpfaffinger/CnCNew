# Dokumentation – Übersicht

Alle Dokumente sind deutsch; Bezeichner und Befehle englisch. Fachdokumente liegen flach in `docs/`, die Phasenaufträge
in `docs/handoffs/`, die Paketberichte der Phase 23 in `docs/rate-invariance/`. Zahlen in den Dokumenten sind Messungen
ihres Stands (Datum und Commit stehen dabei) – die Messlatte für Ergebnisse ist `baselines/reference-manifest.json`,
nicht ein Hash in einem Text. Die Arbeitsregeln stehen in [`../CLAUDE.md`](../CLAUDE.md), die Lizenzlage in
[`../NOTICE.md`](../NOTICE.md).

Zu Pfaden: der Entwicklerbestand heißt heute `game/content` (`scripts/dev-content.ps1`). Die Aufträge der abgeschlossenen
Phasen (`handoffs/HANDOFF-PHASE4.md` bis `-23.md`), der Abnahmeblock in `CONTENT-PIPELINE.md` und die Phasenabsätze in
`CLAUDE.md` nennen als Zeitdokumente noch den früheren Ort `.local/phase2-import-a`.

## Überblick und Plan

| Dokument | Inhalt |
|---|---|
| [ARCHITECTURE.md](ARCHITECTURE.md) | Zielarchitektur: Schichten, Abhängigkeitsregeln, Projekte |
| [ROADMAP.md](ROADMAP.md) | Roadmap mit dem Stand jeder Phase |
| [IMPLEMENTATION-PLAN.md](IMPLEMENTATION-PLAN.md) | Implementierungsplan bis zum abgeschlossenen ersten Scope, Abnahmeaussagen und bekannte Grenzen |
| [ENGINE-EVALUATION.md](ENGINE-EVALUATION.md) | Engine-Evaluierung (Entscheidung für Stride) |

## Content-Pipeline und Installation

| Dokument | Inhalt |
|---|---|
| `INSTALLATION.md` | Patcher: Installation aus der eigenen Steam-Kopie, Deinstallation, Entwicklerbestand (kommt mit dem Patcher-Paket) |
| [CONTENT-PIPELINE.md](CONTENT-PIPELINE.md) | Runtime-Contentvertrag (Schema 2) und Formatmatrix |
| [W3D-MATERIALS.md](W3D-MATERIALS.md) | W3D-Materialvertrag: Pässe, Shaderzustände, Mapper |
| [W3D-SKELETONS.md](W3D-SKELETONS.md) | W3D-Hierarchien, Animationen und Modelle |
| [AUDIO.md](AUDIO.md) | Audio: Decoder, Katalog, Auswahl und Software-Mixer |

## Daten und Definitionen

| Dokument | Inhalt |
|---|---|
| [TYPED-DEFINITIONS.md](TYPED-DEFINITIONS.md) | Typisierter Definitionsvertrag, Include-/Overlayregeln, Snapshotformat |
| [PHASE3-COMPATIBILITY.md](PHASE3-COMPATIBILITY.md) | Kompatibilitätsliste zu `baselines/compatibility.json` |
| [MAP-SCRIPTS.md](MAP-SCRIPTS.md) | World-Builder-Skripte: Dekodierung |
| [UI-WND.md](UI-WND.md) | Fensterlayouts (WND), Skalierung, Laufzeit, Zeichenliste |
| [UI-TEXT-AND-SETTINGS.md](UI-TEXT-AND-SETTINGS.md) | UI-Texte, Lokalisierung, Mapped Images, Einstellungen |

## Simulation

| Dokument | Inhalt |
|---|---|
| [SIMULATION-CORE.md](SIMULATION-CORE.md) | Der deterministische Simulationskern: Tickphasen, Numerik, RNG, Snapshot, Replay, Module, Gelände, Navigation, Sicht, Bau, Skirmish-KI, Raten-Invarianz |
| [GAME-RULES.md](GAME-RULES.md) | Spielregeln: Wirtschaft, Upgrades, Kampf, Bewegung, Produktion, Sieg |
| [SIM-FIRING-TRACKER.md](SIM-FIRING-TRACKER.md) | Feuerstufen, Vorangriffsverzögerung, beschädigte Locomotoren |
| [SIM-JET-FLIGHT.md](SIM-JET-FLIGHT.md) | Flächenflugzeuge und Flugplatzgeometrie |
| [SIM-STAGED-CAPTURE.md](SIM-STAGED-CAPTURE.md) | Gestufte Gebäudeübernahme |
| [SIM-TURRET-AIM.md](SIM-TURRET-AIM.md) | Türme: Zielen, Angriffszustand, Leerlaufabtasten |
| [SKIRMISH-AI-ORIGINAL.md](SKIRMISH-AI-ORIGINAL.md) | Die Skirmish-KI des Originals als wählbare Sitzung |
| [AI-OBSERVED-2026-10-08.md](AI-OBSERVED-2026-10-08.md) | Erste Beobachtung der eingebauten KI durch einen Menschen: Befunde, Korrekturen, Messung |
| [DEFECT-LOST-WAKE.md](DEFECT-LOST-WAKE.md) | 30-Hz-Fehler: die verlorene Weckbitte des langsamen Todes |

## Skripte und Kampagne

| Dokument | Inhalt |
|---|---|
| [SCRIPT-ENGINE.md](SCRIPT-ENGINE.md) | Skript-VM und Missionssitzung, Abdeckung, Zielfahrer |
| [CAMPAIGN-AND-SAVEGAMES.md](CAMPAIGN-AND-SAVEGAMES.md) | Kampagnen, Missionsablauf, Savegames |

## Präsentation, Client, UI und Ton

| Dokument | Inhalt |
|---|---|
| [PRESENTATION-WORLD.md](PRESENTATION-WORLD.md) | Weltpräsentation: Terrain, Kamera, Kriegsnebel, Minimap |
| [PRESENTATION-OBJECTS.md](PRESENTATION-OBJECTS.md) | Objektdarstellung: Modellzustände, Zeichenspezifikationen, Frame-Projektor |
| [PRESENTATION-FX.md](PRESENTATION-FX.md) | Effekte: FXList, Partikel, Tracer, Licht, Decals |
| [PRESENTATION-INPUT.md](PRESENTATION-INPUT.md) | Eingabe, Auswahl, Befehlsmodi, Hotkeys |
| [MODEL-CONDITIONS-UNITS.md](MODEL-CONDITIONS-UNITS.md) | Modellzustände von Einheiten, Waffen, Bewegung, Tod |
| [MODEL-CONDITIONS-BUILDINGS.md](MODEL-CONDITIONS-BUILDINGS.md) | Modellbedingungen der Gebäude, Docks, Türen |
| [CLIENT-TERRAIN.md](CLIENT-TERRAIN.md) | Stride-Client: Terrain-Layer |
| [CLIENT-ROADS.md](CLIENT-ROADS.md) | Stride-Client: Straßen |
| [CLIENT-OBJECTS.md](CLIENT-OBJECTS.md) | Stride-Client: Objektlayer, Sichtprüfliste |
| [CLIENT-FX.md](CLIENT-FX.md) | Stride-Client: Effektschicht |
| [CLIENT-UI-RENDERING.md](CLIENT-UI-RENDERING.md) | Stride-Client: 2D-Renderer der UI-Zeichenliste |
| [GAME-SHELL.md](GAME-SHELL.md) | Spielhülle: Boot, Menüs, Skirmish-Setup, Ergebnis |
| [GAME-HUD.md](GAME-HUD.md) | HUD und Steueroberfläche im Spiel |
| [GAME-AUDIO.md](GAME-AUDIO.md) | AudioDirector, EVA, Musik, Geräteausgabe |
| [TEXT-AUDIT.md](TEXT-AUDIT.md) | Textdarstellung: Schriftpfad, Satzumbruch, Befunde |
| [SPECTATOR-MODE.md](SPECTATOR-MODE.md) | Zuschauermodus: KI gegen KI im Fenster |

## Leistung und Determinismus

| Dokument | Inhalt |
|---|---|
| [DETERMINISM.md](DETERMINISM.md) | Determinismus über Rechnergrenzen: Matrix, Anleitung zweiter Rechner, Regeln |
| [DETERMINISM-FINDINGS.md](DETERMINISM-FINDINGS.md) | Determinismus-Befunde D1–D9 |
| [PERFORMANCE.md](PERFORMANCE.md) | Leistungsprofil der Simulation, Sollwerte, Messreihen |
| [PERF-MOVEMENT.md](PERF-MOVEMENT.md) | Leistung: Bewegung und Wegsuche |
| [PERF-VISION.md](PERF-VISION.md) | Leistung: Sicht und Sicht-Hash |
| [PERF-HASH.md](PERF-HASH.md) | Leistung: Zustands-Hash |
| [PERF-MISSIONS.md](PERF-MISSIONS.md) | Leistung der Kampagnenmissionen |
| [PERF-GAME-LAYER.md](PERF-GAME-LAYER.md) | Leistung der Spielschicht |

## Tickrate und Festkomma

| Dokument | Inhalt |
|---|---|
| [TICKRATE-AUDIT.md](TICKRATE-AUDIT.md) | Geprüftes Inventar der Tickraten-Kopplung |
| [TICKRATE-MIGRATION.md](TICKRATE-MIGRATION.md) | Tickrate als Wert der Welt: Umstellung (Phasen 20, 21, 23), Stand und Restlisten in §15.7 |
| [FIXED-POINT-Q39-24.md](FIXED-POINT-Q39-24.md) | Festkomma von Q47.16 auf Q39.24 |
| [rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md](rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md) | Phase 23: Zusammenführung der sieben Pakete, die Bildkonvention (§1.3) |
| [rate-invariance/RATE-INVARIANCE-GROUND.md](rate-invariance/RATE-INVARIANCE-GROUND.md) | Paket Bodenbewegung |
| [rate-invariance/RATE-INVARIANCE-FLIGHT.md](rate-invariance/RATE-INVARIANCE-FLIGHT.md) | Paket Flug |
| [rate-invariance/RATE-INVARIANCE-PROJECTILES.md](rate-invariance/RATE-INVARIANCE-PROJECTILES.md) | Paket Geschosse |
| [rate-invariance/RATE-INVARIANCE-PHYSICS.md](rate-invariance/RATE-INVARIANCE-PHYSICS.md) | Paket Physik: Faktoren, Rekursionen, Heilscheiben |
| [rate-invariance/RATE-INVARIANCE-TIMING.md](rate-invariance/RATE-INVARIANCE-TIMING.md) | Paket Zeitmaß: „ein Tick, wo das Original ein Bild meint“ |
| [rate-invariance/RATE-INVARIANCE-TURRETS.md](rate-invariance/RATE-INVARIANCE-TURRETS.md) | Paket Türme, Zielerfassung, Feuerrhythmus |
| [rate-invariance/RATE-INVARIANCE-ROUND3-FLIGHT.md](rate-invariance/RATE-INVARIANCE-ROUND3-FLIGHT.md) | Runde 3: Flug |
| [rate-invariance/RATE-INVARIANCE-ROUND3-OBSERVERS.md](rate-invariance/RATE-INVARIANCE-ROUND3-OBSERVERS.md) | Runde 3: Beobachter |
| [rate-invariance/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md](rate-invariance/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md) | Runde 4: das erste Bild und ganze Gefechte |
| [rate-invariance/RATE-SWITCH-OUTER-TESTS.md](rate-invariance/RATE-SWITCH-OUTER-TESTS.md) | Tickraten-Schalter der äußeren Testprogramme |

## Mehrspieler-Programm

| Dokument | Inhalt |
|---|---|
| [MULTIPLAYER-PLAN.md](MULTIPLAYER-PLAN.md) | Programm Tickrate, Festkomma, Mehrspieler (Phasen 18–29): Entscheidungen, Zielarchitektur, Engstellen, offene Entscheidungen des Projektinhabers |

Gebaut ist davon die Vorarbeit (Phasen 18 bis 23); der Mehrspieler-Teil selbst (Phasen 24 bis 29) ist geplant.

## Demos und Befunde

| Dokument | Inhalt |
|---|---|
| [DEMO-FINDINGS.md](DEMO-FINDINGS.md) | Befunde der Tech-Demos durch einen Menschen, Sichtprüflisten |
| [DEMO-SCENARIOS.md](DEMO-SCENARIOS.md) | Gestellte Demos zum Auditieren |

## Phasenaufträge (`handoffs/`)

Jeder Auftrag ist in sich vollständig geschrieben. Die Aufträge der Phasen 4 bis 23 sind abgearbeitet und bleiben als
Zeitdokumente stehen; aktuell ist Phase 24, die Aufträge 25 bis 30 sind vorbereitet und werden am Ende der jeweils
vorigen Phase in ihrem Abschnitt „Ausgangslage“ fortgeschrieben.

| Auftrag | Thema | Stand |
|---|---|---|
| [HANDOFF-PHASE4.md](handoffs/HANDOFF-PHASE4.md) | Deterministischer Simulationskern | abgearbeitet |
| [HANDOFF-PHASE5.md](handoffs/HANDOFF-PHASE5.md) | Spielregeln, Kampf, Wirtschaft, Produktion | abgearbeitet |
| [HANDOFF-PHASE6.md](handoffs/HANDOFF-PHASE6.md) | Rest Phase 5, dann Welt, Terrain, Bewegung, Sicht, Skirmish-KI | abgearbeitet |
| [HANDOFF-PHASE7.md](handoffs/HANDOFF-PHASE7.md) | Phase 6: Welt, Terrain, Bewegung, Sicht, Skirmish-KI | abgearbeitet |
| [HANDOFF-PHASE8.md](handoffs/HANDOFF-PHASE8.md) | Weltpräsentation und offene Simulationsschulden | abgearbeitet |
| [HANDOFF-PHASE9.md](handoffs/HANDOFF-PHASE9.md) | Skirmish, Missionen, Kampagnen, Savegames | abgearbeitet |
| [HANDOFF-PHASE10.md](handoffs/HANDOFF-PHASE10.md) | Phase 9 abschließen: Skirmish, Shell, Durchspielbarkeit | abgearbeitet |
| [HANDOFF-PHASE11.md](handoffs/HANDOFF-PHASE11.md) | Skript-Restfamilien, Kampagnen-Shell, Missions-HUD | abgearbeitet |
| [HANDOFF-PHASE12.md](handoffs/HANDOFF-PHASE12.md) | Zielfahrer, Skriptfamilien, Client-Verdrahtung | abgearbeitet |
| [HANDOFF-PHASE13.md](handoffs/HANDOFF-PHASE13.md) | Verstärkung mit Transport, offene Sieg-Missionen, VM-Semantik | abgearbeitet |
| [HANDOFF-PHASE14.md](handoffs/HANDOFF-PHASE14.md) | Formationen, Waffenslots, Pfadflug, Blocker-Module | abgearbeitet |
| [HANDOFF-PHASE15.md](handoffs/HANDOFF-PHASE15.md) | Tech-Demo-Reife I: Welt-Darstellung, Sicherheit der Abhängigkeiten | abgearbeitet, Sichtabnahme offen |
| [HANDOFF-PHASE16.md](handoffs/HANDOFF-PHASE16.md) | Tech-Demo-Reife II: Ton, Cursor, Wegpunkte, Text, Mission im Client | begonnen, nicht abgenommen |
| [HANDOFF-PHASE17.md](handoffs/HANDOFF-PHASE17.md) | Spielbarkeit durch Menschen | ohne Abnahmeziel beendet; Rest am Ende des Dokuments |
| [HANDOFF-PHASE18.md](handoffs/HANDOFF-PHASE18.md) | Messbasis: Referenzmanifest, Determinismus, Leistungsprofil | abgearbeitet |
| [HANDOFF-PHASE19.md](handoffs/HANDOFF-PHASE19.md) | Leistung: Tickkosten senken ohne Ergebnisänderung | abgearbeitet |
| [HANDOFF-PHASE20.md](handoffs/HANDOFF-PHASE20.md) | Tickrate als Parameter: Simulation | abgearbeitet |
| [HANDOFF-PHASE21.md](handoffs/HANDOFF-PHASE21.md) | Tickrate als Parameter: Präsentation, UI, Ton, Werkzeuge, Tests | abgearbeitet |
| [HANDOFF-PHASE22.md](handoffs/HANDOFF-PHASE22.md) | Festkomma Q39.24 | abgearbeitet |
| [HANDOFF-PHASE23.md](handoffs/HANDOFF-PHASE23.md) | Zeitschritt-Invarianz, 60 Hz als Standard | abgearbeitet, mit benannten Grenzen |
| [HANDOFF-PHASE24.md](handoffs/HANDOFF-PHASE24.md) | Sitzungskern: KI im Zustand, Skirmish-Speicherstand, Befehlsweg, Tickquelle | **aktuell** |
| [HANDOFF-PHASE25.md](handoffs/HANDOFF-PHASE25.md) | Protokoll, Raum, Relay im Prozess | geplant |
| [HANDOFF-PHASE26.md](handoffs/HANDOFF-PHASE26.md) | Dedicated Server, UDP, Zeitabgleich | geplant |
| [HANDOFF-PHASE27.md](handoffs/HANDOFF-PHASE27.md) | Desync-Diagnose, Wiedereinstieg, Zuschauer, Server-Replay | geplant |
| [HANDOFF-PHASE28.md](handoffs/HANDOFF-PHASE28.md) | Serverbetrieb und Sicherheit | geplant |
| [HANDOFF-PHASE29.md](handoffs/HANDOFF-PHASE29.md) | Mehrspieler im Client, Abnahme mit Menschen | geplant |
| [HANDOFF-PHASE30.md](handoffs/HANDOFF-PHASE30.md) | Patch-Integration, Qualität, auslieferbarer Abschluss | geplant (verschoben) |
