# CnCNew

Lokale C#/.NET-Neuimplementierung der für *Generals: Zero Hour* benötigten SAGE-Laufzeit. Der aktuelle Scope umfasst Singleplayer, Skirmish und Kampagne; Multiplayer folgt später.

Dieses Arbeitsverzeichnis bleibt lokal. Originale Spieldateien, entpackte Assets und heruntergeladene Referenz-Repositories liegen in ignorierten Verzeichnissen und sind nicht Bestandteil des neuen Quellcodes.

## Aktueller Stand

- lokale, unveränderte Kopie der vorhandenen Steam-Installation
- .NET-10-Solution
- validierender Reader für `BIGF`- und `BIG4`-Archive
- sicherer Einzel- und Batch-Extractor
- SHA-256-Inventarisierung der lokalen Installation
- materialisierte Laufzeitdaten: 25.682 normale Dateien, keine BIG-Container
- manifestbasiertes `CnCNew.Content`-Dateisystem ohne Archiv-Abhängigkeit
- SAGE-INI-Parser erfolgreich über 256/256 INI-Dateien geprüft
- W3D-Chunk- und Meshdecoder erfolgreich über 8.904/8.904 Dateien und 70.587 Meshes geprüft
- W3D-Materialpässe, 77.672 Shaderzustände, 80.041 Vertexmaterialien, Material-/Shader-IDs, DCG/DIG/SCG-Vertexfarben, Texturtabellen und UV-Daten werden validiert und rendererunabhängig decodiert
- alle vier Prelit-Materialwrapper werden als eigenständige Varianten erhalten; der ausgelieferte Bestand verwendet sie derzeit nicht
- fünf bereits ausgeliefert beschädigte `UISabotr_*`-Binärdateien werden verlustarm im Speicher rekonstruiert und erneut validiert
- eigener validierender TGA-Decoder erfolgreich über 826/826 Dateien und 100.666.368 Pixel geprüft
- Phase 1 „W3D-Material- und Renderingtreue“ ist abgeschlossen: 64.962 Einpass- und 5.625 Zweipass-Meshes sowie 317 zweistufige Pässe sind im Vollbestand nachgewiesen
- Phase 2 „Assetformate und Runtime-Contentvertrag“ ist abgeschlossen: 25.682/25.682 Dateien sind klassifiziert und validiert, Schema 2 enthält 41.527 auditierbare Referenzen
- Phase 3 „Typisierte Spieldaten und Modulregistrierung“ ist abgeschlossen und wurde durch Block 0 der Phase 4 gehärtet: Retail-Ladeplan der Original-Engine, `DefaultThingTemplate`-Vererbung mit Interfacemasken, wiederholbare Schlüssel mit Vielfachheit, schemagetypte Object-Köpfe und Modulfelder, strikte Farb-/Vektor-/Enum-/Flags-Grammatik über Referenz-Namenstabellen, Modulkatalog mit 205 Modultypen ohne Factories und eine eingecheckte Kompatibilitätsbasislinie (`baselines/compatibility.json`, `IniTool compat-check`)
- Vollinventar (alle Kartenoverlays, Stand Abschluss Phase 6): 228 geladene INI-Dateien (28 als nicht Retail ausgeschlossen), 65.875 Blöcke, 12.574 Definitionen, 16.380 Module, 333.061 Schlüssel, 18.138/19.688 Referenzen aufgelöst (1.550 sichtbare Fallbacks); zwei unabhängige 144.832.736-Byte-Snapshots sind byteidentisch (`AEB5108DDA27ABAC7445452AA2743B794AD0BF1102F2687F8D7D8F7B87D2ADB7`); Details in `docs/TYPED-DEFINITIONS.md`
- Phase 4 „Deterministischer Simulationskern“ ist abgeschlossen: `CnCNew.Simulation` (nur von `CnCNew.Data` abhängig) mit Festkomma-Numerik, 30-Hz-Tick, Original-RNG mit benannten Streams, monotonen IDs, Sleepy-Update-Scheduler, Objekt-/Modul-Lebenszyklus, tick-adressierten Befehlen, Timern, Events, kanonischen Snapshots mit inkrementellem Zustands-Hash und einem versionierten Replay-Format (`docs/SIMULATION-CORE.md`)
- Phase 5 „Spielregeln, Kampf, Wirtschaft und Produktion“ ist abgeschlossen (mit dokumentierten Grenzen, `docs/GAME-RULES.md` §10): Wirtschaft (Guthaben, Energie, Versorgungswirtschaft mit Andockmaschine und Spawnern), Upgrades (128-Bit-Maske, portierte `UpgradeMux`-Zustandsmaschine), Sciences, direkter Kampf mit Flächenschaden (Waffen, Rüstungskoeffizienten, Magazin/Nachladen), Erfahrung und Veteranenstufen mit Gesundheits- und Waffenboni, Teams und Diplomatie, Garnison/Transport-Containment, Reparaturdock und `AutoFindHealingUpdate`, Erzeugungslisten und Kisten, das Spezialkraft-Subsystem, einfache geradlinige Bewegung, eine Produktionswarteschlange und „letzter Überlebender“ als Sieg; **74 von 205 Modultypen** sind gebunden, **1.384 der 2.115 Retail-Objekte (65,4 %)** sind vollständig instanziierbar (gegenüber 26/2.113 am Ende von Phase 4), 78,6 % der Modulverwendungen abgedeckt. Das deterministische Referenzmatch (Replay-Format v6, 25 Befehlstypen, Snapshot v6) läuft auf Retail-Daten mit allen 13 Fraktionen, echten Kommandozentralen, **0 übersprungenen Startobjekten** und echter Versorgungswirtschaft (707 Lieferungen, 93.075 Guthaben; Sieger bei Tick 26.880, Snapshotfortsetzung identisch); Golden-Master-Tests (aus dem Quelltext abgeleitet) decken Schadenskette, Produktionsuhr, Supply und Erfahrung. **Offen bzw. bewusst vereinfacht:** RNG-Abgleich mit einem kompilierten Original (Block 0.4, kein Compiler), Kollision, Projektil-Flugzeit, Wirkung von `CommandSet` und Radar, Waffen-/Rüstungssatzwahl nach Veteranenstufe, `ChinookAIUpdate` (Amerika ohne Versorgungswirtschaft), 722 nicht instanziierbare Objekte (Stand nach Phase 6); Folgeauftrag stand in `docs/HANDOFF-PHASE7.md`
- Phase 6 „Welt, Terrain, Bewegung, Sicht, Bau und Skirmish-KI“ ist abgeschlossen (mit dokumentierten Grenzen, `docs/SIMULATION-CORE.md` §13–§20, `docs/IMPLEMENTATION-PLAN.md` §6): `SageMapDecoder` liest alle **150 Retail-Karten** (0 Fehler; `MapTool audit`), `TerrainMap` und `NavigationGrid` (8-fach verbundener A*, Fluchtzellen, Fußabdruckprüfung) führen Einheiten über das Gelände (456 von 462 Startpositionspaaren vollständig begehbar, 6 teilweise), `VisionMap` liefert Kriegsnebel je Spieler (Schüsse verlangen ein sichtbares Ziel), Dozer setzen, reparieren und verkaufen Gebäude, und die `SkirmishAi` spielt mit nur sichtbarer Information: in 16 KI-gegen-KI-Läufen (4 Karten × 4 Paarungen) enden 15 mit einem Sieger, einer bleibt ein Patt, und alle 16 sind mit Zweitlauf und Snapshotfortsetzung identisch; **77 von 205 Modultypen**, **1.393 der 2.115 Retail-Objekte (65,9 %)** instanziierbar, 78,7 % der Modulverwendungen abgedeckt; Folgeauftrag in `docs/HANDOFF-PHASE8.md`
- 216.000 Ticks (zwei Stunden Spielzeit) mit bis zu 15.775 gleichzeitigen Entities, 401.237 Spawn-/Destroy-Vorgängen und 914.394 Befehlen liefern in drei frischen Läufen und nach Snapshot-Fortsetzung identische Tickhashes (Endhash `D1521974C861A4B9`, Kettenhash `C298F5F74993D6CD`, Stand Phase 6); alle Tabellen kehren auf null zurück
- atomarer, inkrementeller Runtimeimport mit kanonischen IDs/Pfaden, SHA-256, Overlayherkunft, Importerversion, Artefakten, Abhängigkeiten und typisierten Fallbacks
- zwei unabhängige Frischimporte sowie ein 25.682/25.682-Cachelauf erzeugten dasselbe kanonische Manifest; Details stehen in `docs/CONTENT-PIPELINE.md`
- typisierte UV-, Environment-, Classic-Environment-, Linear-Offset- und Grid-Mapper; positive und negative Geschwindigkeiten sowie beide Stage-Slots werden deterministisch ausgewertet
- echter Stride-/Direct3D-11-Pfad mit geordneten Mehrfachpässen, bis zu zwei Texture-Stages je Pass, DCG/SCG-Streams, animierten UV-Puffern, Alpha-Test, allen Blendfaktoren des Bestands, Depth-Compare/-Write und Two-Sided-Culling
- case-insensitive DDS-/TGA-Auflösung, sichtbarer Magenta-Fallback mit Diagnose, Texturcache und vollständige GPU-Ressourcenfreigabe
- reproduzierbare lokale PNG-Regressionen für Mehrpass/Alpha/Additiv, zweistufige Detailmaterialien, Linear-/Grid-Animation und Stage-1-Animation; Baseline-Hashes stehen in `docs/W3D-MATERIALS.md`
- Strides Standard-Shader werden reproduzierbar durch den offiziellen Assetcompiler gebaut; Game Studio ist nicht erforderlich
- Phase 7 „Vollständige audiovisuelle Weltpräsentation“ ist abgeschlossen (mit dokumentierten Grenzen): `CnCNew.Client.Stride` bekam eine Welt-Layer-Nahtstelle (`RenderServices`, `IWorldLayer`, `LiveMatchHost`) mit Terrain-/Wasser-/Fog-/Minimap-Layer, W3D-Objekt-/Skelett-Rendering mit Teamfarben, einem Partikel-/Tracer-/Decal-Layer (`Fx`) sowie einem 2D-UI-Renderer mit FreeType-Textausgabe; `CnCNew.Game/Audio` (`AudioDirector`, `EvaCatalog`, `MusicPlaylist`) und `AudioOutput` (`Stride.Audio`) sind als Bibliothek gebaut und getestet, **aber im Client nicht verdrahtet – die Demos sind stumm** (`docs/DEMO-FINDINGS.md` #7, Phase 16). Ein komplettes Skirmish-Match ist headless visualisierbar und per `--match`/`--smoke-test --capture` reproduzierbar deterministisch. **Einschränkung:** „abgeschlossen“ gilt für die Bibliotheken und Rauchtests, nicht für die Darstellungstreue – die erste Sichtprüfung durch einen Menschen fand grobe Fehler (`docs/DEMO-FINDINGS.md`), die Phase 15 korrigiert hat (`docs/CLIENT-OBJECTS.md`). Offen: kein Bink-Videodecoder (keine SDK), kein Blob-/Kontaktschatten, `WaterAppearance.TransparentWaterDepth` wird nicht ausgewertet; Details in `docs/CLIENT-TERRAIN.md`, `docs/CLIENT-OBJECTS.md`, `docs/CLIENT-FX.md`, `docs/CLIENT-UI-RENDERING.md`, `docs/GAME-AUDIO.md`.
- Phase 8 „Eingabe, UI und Lokalisierung“ ist abgeschlossen (mit dokumentierten Grenzen): `CnCNew.Presentation.Input` (`GameInputController`, `SelectionModel`, `CommandModeController`, `CommandBarModel`, `HotkeyMap`) übersetzt Maus/Tastatur/Hotkeys nach dem Vorbild von `GameClient.cpp`; `CnCNew.Game/Shell` treibt Hauptmenü, Optionen, Gefechtssetup, Ladebildschirm und Ergebnisbildschirm aus den echten WND-Layouts, `CnCNew.Game/Hud` bindet Command Bar, Minimap und Auswahl an echte `ControlBar.wnd`-Daten. Offen: kein Mehrspieler-, Lade-/Wiedergabe- oder Kampagnen-Einstieg im Hauptmenü (Buttons sichtbar, aber deaktiviert), keine Formations-Gruppenversatzlogik, kein Befehl „Feuer auf Position“, FreeType-Glyphen sind pixelgenau zu FreeType statt zur originalen GDI-Rasterung; Details in `docs/GAME-SHELL.md`, `docs/GAME-HUD.md`, `docs/PRESENTATION-INPUT.md`.
- Phase 9–12 „Skirmish, Missionen, Kampagnen und Savegames“ ist **weit fortgeschritten; der Zielfahrer belegt die Skriptketten, die Spielbarkeit ist nicht belegt**: die deterministische World-Builder-Skript-VM (`CnCNew.Simulation/Scripting`, 80 von 109 Bedingungs- und 160 von 344 Aktionstypen ausgeführt, 129 weitergereicht, 10 aufgezeichnet; mit den `SKIRMISH_*`-Bedingungen, Sequenz- und Teamereignis-Skripten, Kartenaufdeckung, Garnison, Team-Bau/-Rekrutierung, Überschreibungs-Beziehungen und der Team-Befehlsknopf-Familie), die Missionssitzung mit speicherbarem Zustand (`MissionSession`/`MissionSnapshot`, Version 2), `CampaignCatalog`/`CampaignProgress`/`CampaignController`, das Savegameformat `SaveGame` (`CnCNew.Game`), die Shell-Anbindung (Kampagnenauswahl, Briefing, Laden/Speichern, Missionsabschluss, Challenge-Seitenwahl samt Persona-Fraktion aus `ChallengeMode.ini`), eine Missions-HUD (`LiveGame`/`LiveMission`/`MissionController`), die Verdrahtung von HUD, Eingabe und Shell im Stride-Client (`ClientRuntime`) und die Prüfwerkzeuge `CnCNew.CampaignTool` (`audit`, `playthrough`, `sides`, `reachability`, `script`, `drive`, `template`, `objects`, `transports`) und `SimTool skirmish-sweep` sind fertig. 24 eindeutige Kampagnenkarten laufen deterministisch und setzen nach Speichern/Laden identisch fort; die Endskripte aller 24 Karten (Sieg und Niederlage) sind statisch mit den implementierten Typen erreichbar. Der zielgerichtete Fahrer (`drive`, 60.000 Ticks) erreicht seit Phase 13 Sieg und Niederlage auf dem eigenen Skriptweg bei **allen 24 Karten** (`MD_GLA03` über `ParticleUplinkCannonUpdate`, `MD_CHI04` über `UndeadBody`/`BattleBusSlowDeathBehavior`); Phase 13 hat zudem Verstärkung mit Transport (`DeliverPayloadAIUpdate`, `ParachuteContain`, `JetSlowDeathBehavior`, `CountermeasuresBehavior`), die KI-Team-Warteschlangen (`BUILD_TEAM`/`RECRUIT_TEAM`, Startaktionen, inaktive Singleton-Teams) und die Bereitschaft von Upgrade-Knöpfen ergänzt (107 von 205 Modultypen, 1.735 von 2.115 Objekten instanziierbar, 82,0 %); ein Nur-Befehle-Modus des Fahrers (`drive --commands-only`, `SkirmishAi` als Agent) misst den Abstand zur Spielbarkeit: ohne Weltschreibung wird der Sieg auf 1 von 24 Karten erreicht; **Phase 14** hat danach die Modulabdeckung geschlossen – alle 183 Simulationsmodule implementiert (Luft, Kisten/Spezial/Kollision, Struktur/Welt, Sklaven/Spawn/Container, die letzten neun Blocker), 2.115 von 2.115 Retail-Objekten instanziierbar –, ein Waffenslot-Modell (Primär/Sekundär/Tertiär, Clips, Sperren, Satzwahl; `WorldSnapshot` 8, Replay 8, `CommandTypes.Version` 11), Formationen, den Pfadflug des Partikelstrahls, Luftlieferungen, Todeswaffen und weitere Skriptaktionen (109 von 109 Bedingungen, 187 Aktionstypen ausgeführt, 129 weitergereicht, 19 aufgezeichnet, 9 fehlend und nirgends benutzt); der Zielfahrer erreicht Sieg und Niederlage auf dem Skriptweg bei 24/24 Karten. Der Nur-Befehle-Agent (`drive --commands-only`, 15.000 Ticks, ohne jede Weltschreibung) erreicht den Sieg über die eigenen Skripte weiterhin nur auf **1 von 24 Karten** (`MD_GLA04`) – die neuen Module haben die Spielbarkeit nicht verbessert. Offen bleiben die Spielbarkeit der Missionen durch einen Menschen, neun Skirmish-KI-Aktionen ohne Kampagnennutzung und die benannten Näherungen der neuen Module; Details in `docs/SIMULATION-CORE.md` §29/§30, `docs/SCRIPT-ENGINE.md`, `docs/CAMPAIGN-AND-SAVEGAMES.md` und `docs/HANDOFF-PHASE15.md`. 
- Phase 15 „Tech-Demo-Reife I – Welt-Darstellung und Sicherheit der Abhängigkeiten“ (4. Oktober 2026, frisch gemessen; `docs/DEMO-FINDINGS.md`, `docs/CLIENT-OBJECTS.md`): **Sicherheit:** die vier verwundbaren transitiven Werkzeugpakete von Stride sind über `build/StrideToolingPins.props` auf Patch-Versionen angehoben, `scripts/audit-vulnerable.ps1` meldet keine bekannte Anfälligkeit, die Unterdrückung von `NU1901`/`NU1903` ist entfernt, der saubere Solution-Build hat **11** Warnungen (10 `YamlAssemblyRegistry`- und 1 `AssetCompiler`-Hinweis von Strides Werkzeug, vorher 21). **Welt:** die „halben Gebäude“ hatten drei Darstellungsursachen (Freigabe des geteilten Index-Puffers, invertierter Windungssinn der Objekt-Meshes, fehlende Beleuchtung) plus ignorierte Bone-Sichtbarkeit – behoben; `PresentationTool model-audit` zeichnet 1.585.401 von 1.585.417 Dreiecken (0 im Renderplan verloren); Türme folgen den Schüssen, Reifen drehen mit der Strecke, Mündungsfeuer erscheint nur nach einem Schuss, unerkundete Szenerie bleibt verborgen, das bisher nie gezeichnete HUD-Overlay (Auswahl, Lebensbalken, Befehlslinien, Sammelpunkte, Heilsymbole) wird gezeichnet, der Versorgungs-Chinook zeigt die Abladeanimation erst am Depot. Der gemeldete Befund falsch gedrehter Kartenobjekte wurde **nicht reproduziert** (`angle-audit`: gespeicherter Winkel richtet 64,6 % der Zaunreihen aus). Nebenbei: Wegfindung um stehende Einheiten (488 → 0 Überlappungs-Ticks), KI-Sammelpunkt frei von Gebäuden, Kampfplan als typisierte Option (`CommandTypes.Version` 12, Replay 9). **Nicht belegt:** die Sichtabnahme durch einen Menschen (Checkliste in `docs/CLIENT-OBJECTS.md` ist nicht abgehakt, die Prüfsummen von `scripts/capture-world.ps1` sind vorläufig), Ton, Cursor, Wegpunkte, Text und Mission im Client (Phase 16), die Spielbarkeit (Phase 17); 54 Modellzustände (Türen, Vorangriff, Packen, Nachbrenner …) werden nie gesetzt, Lenkung/Federung, Straßen und die Abdunklung von Objekten im Nebel fehlen.
- der früher dokumentierte Live-/Replay-Determinismus-Unterschied bei geteilten Spezialkräften (`SpecialPowerBase.SharedReadyFrame()`) ist seit Phase 9 behoben (`docs/SIMULATION-CORE.md` §12/§12a; Test `SkirmishLiveSessionMatchesItsOwnReplay`).

## Tech-Demos starten (erste Rückmeldung: `docs/DEMO-FINDINGS.md`)

Die Skripte in `scripts/` bauen den Stride-Client aus dem Quelltext (Release) und starten eine Demo; sie brauchen den lokalen Runtimebestand `.local/phase2-import-a` (oder `-ContentDir`):

- `.\scripts\demo-skirmish.ps1` – Live-Gefecht (Standard: `Alpine Assault`, `FactionAmerica` gegen `FactionChina`; `-Map`, `-Faction`, `-Opponent A,B`)
- `.\scripts\demo-mission.ps1` – Kampagnenmission hinter der Befehlsleiste (Standard: `GC_ChemGeneral`; `-Map "Maps/…/….map"`, `-HumanSide`)
- `.\scripts\demo-shell.ps1` – Hauptmenü-Shell (Kampagne wählen, Briefing, Mission, Speichern/Laden)

Gestellte Einzeldemos zum Auditieren (Gebäude einnehmen, Panzer gegen Panzer, Jet gegen Raketentruppen): `scripts\demo-scenario.ps1`, headless `scriptsudit-scenarios.ps1`, Beschreibung und aktueller Befund in `docs/DEMO-SCENARIOS.md`.

Gemeinsame Schalter (`scripts\demo.ps1 skirmish|mission|shell`): `-Size 1920x1080`, `-Seed N`, `-Configuration Debug`, `-NoBuild`, `-SmokeTest` (headless statt Fenster). Die Demos sind headless rauchgetestet, aber noch nicht von einem Menschen durchgespielt (`docs/HANDOFF-PHASE15.md`).
## Lokale Verzeichnisse

- `.local/game-original`: unveränderte Installationskopie
- `.local/assets/by-archive`: getrennt nach Ursprungsarchiv entpackte Daten
- `.local/content`: zusammengeführter, offen lesbarer Laufzeit-Content ohne BIG-Container
- `.local/manifests`: reproduzierbare Datei- und Archivlisten
- `references`: nur lesbare Downloads von EA und den Community-Projekten

## Befehle

```powershell
dotnet build CnCNew.slnx
dotnet run --project tests/CnCNew.Archives.Tests
dotnet run --project tests/CnCNew.Content.Tests
dotnet run --project tests/CnCNew.Data.Tests
dotnet run --project tests/CnCNew.IO.Tests
dotnet run --project tests/CnCNew.Images.Tests
dotnet run --project tests/CnCNew.Formats.Tests
dotnet run --project tests/CnCNew.Import.Tests
dotnet run --project tests/CnCNew.Simulation.Tests
dotnet run --project tools/CnCNew.AssetTool -- inventory .local/game-original .local/manifests/game-original.json
dotnet run --project tools/CnCNew.AssetTool -- extract-all .local/game-original .local/assets/by-archive
dotnet run --project tools/CnCNew.AssetTool -- materialize .local/game-original .local/content
dotnet run --project tools/CnCNew.AssetTool -- audit-formats .local/content
dotnet run --project tools/CnCNew.AssetTool -- audit-dependencies .local/content
dotnet run --project tools/CnCNew.AssetTool -- runtime-import .local/game-original .local/phase2-import-a
dotnet run --project tools/CnCNew.AssetTool -- verify .local/phase2-import-a
dotnet run --project tools/CnCNew.IniTool -- audit .local/content
dotnet run --project tools/CnCNew.IniTool -- compile .local/phase2-import-a .local/phase3-definitions.json
dotnet run --project tools/CnCNew.IniTool -- compat-check .local/phase2-import-a baselines/compatibility.json
dotnet run --project tools/CnCNew.IniTool -- load-order-report .local/phase2-import-a
dotnet run -c Release --project tools/CnCNew.SimTool -- stress
dotnet run -c Release --project tools/CnCNew.SimTool -- audit-coverage .local/phase2-import-a
dotnet run -c Release --project tools/CnCNew.SimTool -- reference-match .local/phase2-import-a
dotnet run -c Release --project tools/CnCNew.SimTool -- ai-match .local/phase2-import-a --map "Maps/Alpine Assault/Alpine Assault.map" --factions China,GLA --max-ticks 45000
dotnet run --project tools/CnCNew.MapTool -- audit .local/phase2-import-a
dotnet run --project tools/CnCNew.ImageTool -- audit-tga .local/content
dotnet run --project tools/CnCNew.W3dTool -- audit .local/content
dotnet run --project tools/CnCNew.W3dTool -- inspect .local/content Art/W3D/ABArFrcCmd.W3D
dotnet run --project tests/CnCNew.W3D.Tests -- --content-dir .local/content
dotnet run --project src/CnCNew.Client.Stride -c Release -- --smoke-test --content-dir .local/content --model-path Art/W3D/ABArFrcCmd.W3D
dotnet run --project src/CnCNew.Client.Stride -c Release -- --smoke-test --content-dir .local/content --model-path Art/W3D/AVSoloDrn.W3D --capture .local/visual-verification/animated-stage1.png
```

Der Stride-Assetcompiler 4.3.0.2507 bringt derzeit einige von NuGet als verwundbar gemeldete transitive **Buildwerkzeuge** mit. `NU1901` und `NU1903` bleiben beim Client-Build sichtbar, werden dort aber nicht als Fehler behandelt. Sie sind kein stillschweigend ignorierter Befund und müssen bei einem Stride-Update oder einer isolierten Content-Pipeline entfernt werden.

## Rechtliche Trennung

Der EA-Quellcode und die Community-Projekte stehen unter GPLv3 mit jeweils zu prüfenden Zusatzbedingungen. Markenrechte und Rechte an den ausgelieferten Medien/Assets werden dadurch nicht übertragen. Da dieses Projekt derzeit ausschließlich lokal verwendet wird, werden keine Original-Assets verteilt.

BIG-Dateien sind ausschließlich Eingabe für den einmaligen lokalen Import. Das neue Spiel liest zur Laufzeit normale Dateien aus `.local/content`; `content-manifest.json` hält die gewählte Overlay-Quelle und alle verdrängten Varianten nachvollziehbar fest.

Der typisierte Definitionsvertrag, Include-/Overlayregeln und das Snapshotformat stehen in
`docs/TYPED-DEFINITIONS.md`; die vollständige, nicht stille Kompatibilitätsstufe steht in
`docs/PHASE3-COMPATIBILITY.md`. Tickphasen, Numerik, RNG, Snapshot- und Replayformat, Modulbindung,
Budgets und Modulabdeckung des Simulationskerns stehen in `docs/SIMULATION-CORE.md`; die darauf aufsetzenden
Spielregeln (Wirtschaft, Upgrades, Kampf, Bewegung, Produktion, Sieg/Niederlage) stehen in `docs/GAME-RULES.md`; Gelände, Navigation, Sicht, Bau und Skirmish-KI stehen in `docs/SIMULATION-CORE.md` §13–§20.

