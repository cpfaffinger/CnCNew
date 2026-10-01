# Roadmap

## 0. Reproduzierbare Ausgangsbasis

- Installation unverändert kopieren und per SHA-256 inventarisieren.
- EA-Quellcode, GeneralsGameCode und GeneralsGamePatch als lokale Referenzen festhalten.
- Original, installierte Modifikationen und künftige Engine-Ausgaben strikt trennen.

## 1. Asset- und Dateisystem

**Status: abgeschlossen am 27. September 2026.**

- BIG-Archive lesen, validieren und getrennt extrahieren.
- Virtuelles Overlay-Dateisystem mit originalgetreuer Suchpriorität bauen.
- Overlay einmalig als normale Dateien mit Herkunfts- und SHA-256-Manifest materialisieren.
- Laufzeitzugriff ausschließlich über das archivfreie `CnCNew.Content` bereitstellen.
- INI-, STR-, MAP-, WND-, W3D-, DDS/TGA-, Audio- und Videoformate katalogisieren.
- Asset-Konverter nur dort einsetzen, wo Laufzeit-Decoding nicht sinnvoll ist.
- Schema-2-Manifest, deterministischer Abhängigkeitsgraph, atomarer inkrementeller Import und vollständiger Format-/Referenzaudit sind umgesetzt; Bestandsmatrix und Fallbacks stehen in `CONTENT-PIPELINE.md`.

## 2. Headless-Simulation

- **Datengrundlage abgeschlossen am 27. September 2026:** 256 INI-Dateien werden deterministisch
  zu unveränderlichen Definitionen, expliziten Moduldeskriptoren und einem typisierten
  Referenzgraphen kompiliert; lose Parserknoten überschreiten die Daten-/Simulationsgrenze nicht.
- **Simulationskern abgeschlossen am 28. September 2026:** deterministischer 30-Hz-Tick, Festkomma-Numerik, Original-RNG mit
  benannten Streams, monotone IDs, Befehlsqueue, Objekt-/Modul-/Timer-/Eventlebenszyklus, kanonische Snapshots, inkrementeller
  Zustands-Hash und Replay-Harness; über 216.000 Ticks bitidentisch, auch nach Snapshot-Fortsetzung (`docs/SIMULATION-CORE.md`).
- **Phase-3-Härtung (Block 0) abgeschlossen:** Retail-Ladeplan, Default-Vererbung, strikte Wertgrammatik, Modulkatalog ohne
  Factories, Kompatibilitätsbasislinie.
- den registrierten Template-/Module-/Behavior-Vertrag im Simulationskern instanziieren: zwölf Lebenszyklusmodule sind
  implementiert (26 von 2.113 Retail-Objekten vollständig instanziierbar); die übrigen Module folgen mit Phase 5/6.
- **Phase 5 „Spielregeln“ abgeschlossen:** Wirtschaft mit Versorgungswirtschaft (Andockmaschine, Spawner), Upgrades (portierte
  `UpgradeMux`-Zustandsmaschine), Sciences, direkter Kampf mit Flächenschaden, Erfahrung und Veteranenstufen, Teams und Diplomatie,
  Garnison/Transport-Containment, Reparaturdock, Erzeugungslisten und Kisten, das Spezialkraft-Subsystem, einfache geradlinige
  Bewegung, Produktionswarteschlange und „letzter Überlebender“; 74 Module gebunden, 1.384 von 2.115 Retail-Objekten vollständig
  instanziierbar (65,4 %, `docs/GAME-RULES.md`). Der Referenzmatch läuft auf Retail-Daten mit allen 13 Fraktionen, echten
  Kommandozentralen, 0 übersprungenen Startobjekten und echter Wirtschaft; Golden Masters decken Schadenskette, Produktionsuhr, Supply
  und Erfahrung. Bewusst offen: RNG-Abgleich mit kompiliertem Original (Block 0.4), Kollision,
  Projektil-Flugzeit, Wirkung von `CommandSet` und Radar, Waffen-/Rüstungssatzwahl nach Veteranenstufe (`docs/GAME-RULES.md`
  §10; Bauvorgang, Verkauf, Terrain und Pfadsuche hat Phase 6 nachgeholt).
- **Phase 6 „Welt, Terrain, Bewegung, Sicht, Bau und Skirmish-KI“ abgeschlossen (30. September 2026):** 150 von 150 Retail-Karten
  werden dekodiert und zu Gelände aufgebaut, A*-Navigation, Kriegsnebel je Spieler, Bau/Verkauf/Reparatur/Sammelpunkte und eine Skirmish-KI, die nur
  sichtbare Information nutzt; 16 KI-gegen-KI-Läufe (4 Karten × 4 Paarungen) sind alle deterministisch (Zweitlauf und Snapshotfortsetzung
  identisch), 15 enden mit einem Sieger, einer bleibt ein Patt; 77 Module gebunden, 1.393 von 2.115 Objekten (65,9 %) instanziierbar.
  Bewusst offen: Kollision, Brücken, Formationen, Detektoren, Chinook-/Transport-KI (Amerika ohne Wirtschaft), Projektil-Flugzeit,
  Block 0.4 (`docs/IMPLEMENTATION-PLAN.md` §6, `docs/SIMULATION-CORE.md` §20).
- Zielwahl mit Sichtlinie über Höhen, Kartenskripte und Formationen (offen).
- Golden-Master- und Replay-basierte Vergleichstests gegen das Original.

## 3. Windows-Client

- Fenster, Eingabe und Direct3D-Renderer auf aktuellem Windows-Unterbau.
- **Erster Vertical Slice erreicht:** archivfreies W3D-Modell wird samt Materialgruppen, UVs und Original-DDS-Texturen über Stride/Direct3D 11 gerendert.
- **W3D-Materialzustands-Slice erreicht:** Shader-/Vertexmaterialtabellen, IDs und DCG werden über den Vollbestand validiert; Alpha-Test, Blend, Depth-Zustände, Vertexfarben und Two-Sided-Culling sind im Direct3D-11-Client aktiv.
- **W3D-Materialdomäne abgeschlossen:** geordnete Mehrfachpässe, beide Texture-Stages, DCG/DIG/SCG, alle Shaderfunktionen des ausgelieferten Bestands, Environment-Mapping sowie typisierte und zeitabhängige Linear-/Grid-Mapper laufen rendererunabhängig getestet und im Direct3D-11-Client.
- Lokale deterministische PNG-Baselines decken transparente, additive, zweistufige und animierte Materialpfade ab; Originalassets und abgeleitete Bilder bleiben außerhalb von Git.
- **Phase 7 „Vollständige audiovisuelle Weltpräsentation“ abgeschlossen am 30. September 2026, mit den unten benannten Einschränkungen:** eine Welt-Layer-Nahtstelle (`RenderServices`, `IWorldLayer`, `LiveMatchHost`) treibt Terrain-/Wasser-/Fog-/Minimap-, W3D-Objekt-/Skelett- (inkl. Teamfarben) und Partikel-/Tracer-/Decal-Layer aus Live-Simulations-Snapshots; `CnCNew.Game/Audio` (`AudioDirector`, `EvaCatalog`, `MusicPlaylist`) spielt Musik, EVA-Ansagen und Sound über `Stride.Audio` deterministisch aus. Ein komplettes headless Match ist per `--match <Karte>`/`--smoke-test --capture` visualisierbar und steuerbar; mehrere lange Läufe liefern byteidentische Screenshot- und Drawlist-Hashes. **Bewusst offen:** kein Bink-Videodecoder (proprietäres Binärformat, keine SDK), Türme rendern in Ruhepose (keine live gelieferten Turmwinkel), einige dünne zweiseitige W3D-Meshes schattieren rückseitig schwarz (geteilte Stride-Materialpipeline `MaterialDiffuseLambertModelFeature`), kein Blob-/Kontaktschatten, `WaterAppearance.TransparentWaterDepth` bleibt unausgewertet, seltene `FxFrame`-Inhalte (Drawable-/Smudge-Partikel, Lichtpulse, Strahleffekte – 0 Retail-Verwendungen bei Strahleffekten) werden nicht gezeichnet; Details in `docs/CLIENT-TERRAIN.md`, `docs/CLIENT-OBJECTS.md`, `docs/CLIENT-FX.md`, `docs/GAME-AUDIO.md`.
- **Phase 8 „Eingabe, UI und Lokalisierung“ abgeschlossen am 30. September 2026, mit den unten benannten Einschränkungen:** `CnCNew.Presentation.Input` übersetzt Maus, Tastatur und Hotkeys nach der Reihenfolge von `GameClient.cpp` in typisierte Simulationsbefehle; `CnCNew.Game/Shell` und `/Hud` bilden Hauptmenü, Optionen, Gefechtssetup, Ladebildschirm, HUD/Command-Bar/Minimap und Ergebnisbildschirm aus den echten WND-Layouts nach, und `CnCNew.Client.Stride/Ui` zeichnet sie auflösungsunabhängig mit einem 2D-Drawlist-Renderer und FreeType-Textausgabe. **Bewusst offen:** kein Mehrspieler-, Lade-/Wiedergabe- oder Kampagnen-Einstieg im Hauptmenü (Buttons sichtbar, aber deaktiviert), keine Formations-/Gruppenversatzlogik, kein Befehl „Feuer auf Position“, mehrere fehlende Simulationsbefehle für Kontextaktionen (Entführen, Sabotage, Gebäude einnehmen, Hacken u. a.), FreeType-Glyphen sind pixelgenau zur FreeType-Rasterung statt zur originalen GDI-Rasterung; Details in `docs/GAME-SHELL.md`, `docs/GAME-HUD.md`, `docs/PRESENTATION-INPUT.md`.
- **Phase 9/10 „Skirmish, Missionen, Kampagnen und Savegames“ – Zwischenstand 1. Oktober 2026 (nicht abgeschlossen):** Skript-VM, Missionssitzung, Kampagnenlogik, Savegameformat und Headless-Prüfung (`CnCNew.CampaignTool`) sind fertig und gemessen (24/24 Kampagnenkarten deterministisch und nach Speichern/Laden identisch; Endproben Sieg 10/24, Niederlage 15/24). Phase 10 hat die vier häufigsten Befehlsknopf-Fähigkeits-Aktionen (`*_USE_COMMANDBUTTON_ABILITY*`) und `SpecialAbilityUpdate` ergänzt (1.530 von 2.115 Objekten instanziierbar, 72,3 %) sowie eine host-wählbare lokale Seite (`MissionOptions.HumanSideName`) für Karten ohne eigene `playerIsHuman`-Seite; offen bleiben der Skirmish-Abschluss, Kollision (`ToppleUpdate`, `HeightDieUpdate`), Bau-/Regelgrenzen, Garnison/Kartenaufdeckung/KI-Teambau/Sichtbedingungen, die `SKIRMISH_*`-Skripte und die Shell-Anbindung (Kampagnenauswahl, Briefing, Ladeliste, General-Wahl). Siehe `docs/IMPLEMENTATION-PLAN.md` §9 und `docs/HANDOFF-PHASE11.md`.
- **An eine künftige Phase weitergereicht:** ein bestätigter, dokumentierter (nicht behobener) Live-/Replay-Determinismus-Unterschied – `SpecialPowerBase.SharedReadyFrame()` mutiert Weltzustand lazy innerhalb einer eigentlich lesenden `IsReady()`-Abfrage; die Skirmish-KI fragt das live jeden Tick ab, aber der `ReplayRunner` führt nie KI-Code aus, sodass der `StateHash` eines Live-Matches vom Replay derselben Aufzeichnung abweichen kann, sobald geteilte Spezialkräfte nie explizit gewährt wurden. Eine Behebung ändert KI-Timing und würde alle bestehenden Referenzmatch-/Stress-/KI-Lauf-Hashes ungültig machen; sie braucht eine eigene, vermessene Phase. Analyse in `docs/SIMULATION-CORE.md` §12/§12a.

## 4. Spielmodi

- zuerst eine kleine Skirmish-Karte als vertikaler Slice.
- anschließend vollständiger Skirmish samt KI.
- danach Missionsskripte, Briefings, Zwischensequenzen und Kampagnenfortschritt.
- Savegames erst nach Stabilisierung des Simulations-Datenmodells.

## 5. Patch-Integration und Qualität

- GeneralsGameCode-Fixes nach Subsystem katalogisieren und mit Regressionstests übernehmen.
- GeneralsGamePatch als eigene Overlay-Schicht importieren; kontroverse Balanceänderungen konfigurierbar halten.
- Crash-, Speicher-, Pfadfindungs-, KI-, Grafik- und UI-Fehler priorisieren.
- Profiler, strukturierte Logs, deterministische Testläufe und Diagnose-Dumps.

## Nicht im ersten Scope

- Netzwerkcode, Matchmaking, NAT, GameSpy-Kompatibilität und Anti-Cheat.
- binäre Kompatibilität zu alten Mods oder Savegames, solange der Kern noch im Aufbau ist.

