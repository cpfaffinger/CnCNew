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
- **Phase 7 „Vollständige audiovisuelle Weltpräsentation“ abgeschlossen am 30. September 2026, mit den unten benannten Einschränkungen:** eine Welt-Layer-Nahtstelle (`RenderServices`, `IWorldLayer`, `LiveMatchHost`) treibt Terrain-/Wasser-/Fog-/Minimap-, W3D-Objekt-/Skelett- (inkl. Teamfarben) und Partikel-/Tracer-/Decal-Layer aus Live-Simulations-Snapshots; `CnCNew.Game/Audio` (`AudioDirector`, `EvaCatalog`, `MusicPlaylist`) ist als Bibliothek gebaut und getestet (Musik, EVA-Ansagen und Sound über `Stride.Audio`), im Client aber **nicht verdrahtet – die Demos sind stumm** (`docs/DEMO-FINDINGS.md` #7, Phase 16). Ein komplettes headless Match ist per `--match <Karte>`/`--smoke-test --capture` visualisierbar und steuerbar; mehrere lange Läufe liefern byteidentische Screenshot- und Drawlist-Hashes. **Bewusst offen:** kein Bink-Videodecoder (proprietäres Binärformat, keine SDK), „abgeschlossen“ gilt für Bibliotheken und Rauchtests, nicht für die Darstellungstreue (grobe Darstellungsfehler zeigte erst die Sichtprüfung durch einen Menschen; Korrekturen in Phase 15, `docs/CLIENT-OBJECTS.md`), kein Blob-/Kontaktschatten, `WaterAppearance.TransparentWaterDepth` bleibt unausgewertet, seltene `FxFrame`-Inhalte (Drawable-/Smudge-Partikel, Lichtpulse, Strahleffekte – 0 Retail-Verwendungen bei Strahleffekten) werden nicht gezeichnet; Details in `docs/CLIENT-TERRAIN.md`, `docs/CLIENT-OBJECTS.md`, `docs/CLIENT-FX.md`, `docs/GAME-AUDIO.md`.
- **Phase 8 „Eingabe, UI und Lokalisierung“ abgeschlossen am 30. September 2026, mit den unten benannten Einschränkungen:** `CnCNew.Presentation.Input` übersetzt Maus, Tastatur und Hotkeys nach der Reihenfolge von `GameClient.cpp` in typisierte Simulationsbefehle; `CnCNew.Game/Shell` und `/Hud` bilden Hauptmenü, Optionen, Gefechtssetup, Ladebildschirm, HUD/Command-Bar/Minimap und Ergebnisbildschirm aus den echten WND-Layouts nach, und `CnCNew.Client.Stride/Ui` zeichnet sie auflösungsunabhängig mit einem 2D-Drawlist-Renderer und FreeType-Textausgabe. **Bewusst offen:** kein Mehrspieler-, Lade-/Wiedergabe- oder Kampagnen-Einstieg im Hauptmenü (Buttons sichtbar, aber deaktiviert), keine Formations-/Gruppenversatzlogik, kein Befehl „Feuer auf Position“, mehrere fehlende Simulationsbefehle für Kontextaktionen (Entführen, Sabotage, Gebäude einnehmen, Hacken u. a.), FreeType-Glyphen sind pixelgenau zur FreeType-Rasterung statt zur originalen GDI-Rasterung; Details in `docs/GAME-SHELL.md`, `docs/GAME-HUD.md`, `docs/PRESENTATION-INPUT.md`.
- **Phase 9–12 „Skirmish, Missionen, Kampagnen und Savegames“ – Zwischenstand 2. Oktober 2026 (Skriptketten belegt, Spielbarkeit nicht):** Skript-VM, Missionssitzung, Kampagnenlogik, Savegameformat, Shell-Anbindung, Kollision, Bau-/Regelgrenzen und Skirmish-Abschluss sind fertig und gemessen. Phase 11 hat die `SKIRMISH_*`-Bedingungen (Entscheidung: Portierung), Kartenaufdeckung, Garnison, Bauschalter, `BUILD_TEAM`/`RECRUIT_TEAM`, Sequenz- und Teamereignis-Skripte ergänzt und mit `CampaignTool reachability` ein statisches Maß eingeführt. **Phase 12** hat einen zielgerichteten Zielfahrer (rückwärts vom `VICTORY`/`DEFEAT`-Skript geplant) gebaut, der Sieg auf dem eigenen Skriptweg bei **22/24** und Niederlage bei **24/24** Karten erreicht (vorher 10/24 und 16/24; die Ausschalt-Endproben 13/24 und 15/24), 29 weitere Aktionstypen ausgeführt und 8 aufgezeichnet (160 ausgeführt/10 aufgezeichnet von 344), sieben Missionsobjekt-Module ergänzt (98 von 205 Modultypen, 1.680 von 2.115 Objekten instanziierbar, 79,4 %), die Persona-Fraktion der Challenge-Wahl und die Verdrahtung von HUD, Eingabe und Shell im Stride-Client geliefert; die Endskripte aller 24 Karten sind statisch erreichbar (Sieg und Niederlage). **Phase 13 (3. Oktober 2026)** hat die beiden letzten Sieg-Missionen geschlossen (`MD_GLA03`: `ParticleUplinkCannonUpdate`; `MD_CHI04`: `UndeadBody`, `BattleBusSlowDeathBehavior`, `SubObjectsUpgrade`, `AnimationSteeringUpdate`) – der Fahrer erreicht jetzt Sieg **24/24** und Niederlage **24/24** –, Verstärkung mit Transport (Flugzeuge, Fallschirm, Täuschkörper) sowie die KI-Team-Warteschlangen und die Upgrade-Knopf-Bereitschaft umgesetzt (107 von 205 Modultypen, 1.735 von 2.115 Objekten instanziierbar, 82,0 %). Offen: Formationen/Angriffsmarsch, Pfadflug, `WeaponSlot`, „untätig“ ohne AI-Zustand, und der Nachweis, dass ein Mensch die Missionen tatsächlich spielen kann. Siehe `docs/IMPLEMENTATION-PLAN.md` §9 und `docs/HANDOFF-PHASE14.md`.
- **Phase 14 „Modulabdeckung, Waffenslots, Skriptlücken“ – abgeschlossen am 3. Oktober 2026 (Spielbarkeit weiter nicht belegt):** alle 183 Simulationsmodule implementiert, 2.115 von 2.115 Retail-Objekten instanziierbar (84,7 % der Modulverwendungen, der Rest sind die 22 Präsentationsmodule); Waffenslot-Modell (`WorldSnapshot` 8, Replay 8, `CommandTypes.Version` 11); Formationen, Pfadflug des Partikelstrahls, Luftlieferungen, Todeswaffen, Halte-/Deaktivierungszustände, Sprengfallen, Brückenzustand; Skript-VM 109/109 Bedingungen, 187 Aktionstypen ausgeführt (9 fehlend, nirgends benutzt); der Zielfahrer erreicht Sieg und Niederlage bei 24/24 Karten auf dem Skriptweg, Ausschalt-Endproben 13/24 und 15/24. Nur-Befehle-Agent (`drive --commands-only`, 15.000 Ticks, ohne jede Weltschreibung): Sieg über die eigenen Skripte weiterhin nur auf **1 von 24 Karten** (`MD_GLA04`) – die neuen Module haben die Spielbarkeit nicht verbessert. Nächste Schritte: `docs/HANDOFF-PHASE15.md` (Welt-Darstellung und Sicherheit), `docs/HANDOFF-PHASE16.md` (Bedienung, Ton, Mission im Client), `docs/HANDOFF-PHASE17.md` (Spielbarkeit); Befunde der ersten Tech-Demo: `docs/DEMO-FINDINGS.md`.
- **Phase 15 „Tech-Demo-Reife I – Welt-Darstellung und Sicherheit der Abhängigkeiten“ – Korrekturen gemessen am 4. Oktober 2026, Sichtabnahme durch einen Menschen offen** ( `docs/DEMO-FINDINGS.md`, `docs/CLIENT-OBJECTS.md`): **Sicherheit:** die vier verwundbaren transitiven Werkzeugpakete von Stride sind über `build/StrideToolingPins.props` auf Patch-Versionen angehoben, `scripts/audit-vulnerable.ps1` meldet keine bekannte Anfälligkeit, die Unterdrückung von `NU1901`/`NU1903` ist entfernt, der saubere Solution-Build hat **11** Warnungen (10 `YamlAssemblyRegistry`- und 1 `AssetCompiler`-Hinweis von Strides Werkzeug, vorher 21). **Welt:** die „halben Gebäude“ hatten drei Darstellungsursachen (Freigabe des geteilten Index-Puffers, invertierter Windungssinn der Objekt-Meshes, fehlende Beleuchtung) plus ignorierte Bone-Sichtbarkeit – behoben; `PresentationTool model-audit` zeichnet 1.585.401 von 1.585.417 Dreiecken (0 im Renderplan verloren); Türme folgen den Schüssen, Reifen drehen mit der Strecke, Mündungsfeuer erscheint nur nach einem Schuss, unerkundete Szenerie bleibt verborgen, das bisher nie gezeichnete HUD-Overlay (Auswahl, Lebensbalken, Befehlslinien, Sammelpunkte, Heilsymbole) wird gezeichnet, der Versorgungs-Chinook zeigt die Abladeanimation erst am Depot. Der gemeldete Befund falsch gedrehter Kartenobjekte wurde **nicht reproduziert** (`angle-audit`: gespeicherter Winkel richtet 64,6 % der Zaunreihen aus). Nebenbei: Wegfindung um stehende Einheiten (488 → 0 Überlappungs-Ticks), KI-Sammelpunkt frei von Gebäuden, Kampfplan als typisierte Option (`CommandTypes.Version` 12, Replay 9). Nachtrag 4. Oktober 2026: Straßen, Nebel-Abdunklung, Lenkung/Federung und 95 von 106 Modellzuständen sind gebaut; drei gestellte Auditdemos (`docs/DEMO-SCENARIOS.md`) deckten Lücken der Simulation auf, die geschlossen sind – Turmzustand mit Feuerfreigabe (`WorldSnapshot` 9), gestufte Gebäudeübernahme, Flügelflug mit Startbahn aus Modellknochen; neue Referenzwerte: Referenzmatch Sieger FactionGLA Tick 15.000, Endhash `01C01E3B91EAE18C` (nach dauerhaftem Turm-Angriffszustand und Idle-Scan; `CommandTypes.Version` 13, Replay 10), Stresstest unverändert `56882F763EABEF67`. **Nicht belegt:** die Sichtabnahme durch einen Menschen (Checkliste in `docs/CLIENT-OBJECTS.md` ist nicht abgehakt, die Prüfsummen von `scripts/capture-world.ps1` sind vorläufig), Ton, Cursor, Wegpunkte, Text und Mission im Client (Phase 16), die Spielbarkeit (Phase 17); 54 Modellzustände (Türen, Vorangriff, Packen, Nachbrenner …) werden nie gesetzt, Lenkung/Federung, Straßen und die Abdunklung von Objekten im Nebel fehlen.
- **Gelöst:** der Live-/Replay-Determinismus-Unterschied bei geteilten Spezialkräften (`SpecialPowerBase.SharedReadyFrame()`) ist seit Phase 9 behoben (`docs/SIMULATION-CORE.md` §12/§12a).

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

