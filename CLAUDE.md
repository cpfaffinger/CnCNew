# CnCNew – Arbeitsanweisungen für Coding-Agenten

Lokale C#/.NET-10-Neuimplementierung der für *Command & Conquer: Generals – Zero Hour* benötigten
SAGE-Laufzeit (Windows-Client mit Stride 4.3 / Direct3D 11). Scope des ersten Produkts:
Singleplayer, Skirmish und Kampagne. Nicht im Scope: Multiplayer, binäre Kompatibilität zu alten
Mods und Savegames. Das Repo ist rein lokal (Branch `main`, **kein Remote**).

Die Gesamtplanung steht in `docs/IMPLEMENTATION-PLAN.md`. Die aktuell zu bearbeitende Phase steht
als vollständiger Auftrag in `docs/HANDOFF-PHASE12.md` (Handoff-Datei der jeweils nächsten Phase).
Lies vor jeder Phase `README.md`, `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`,
`docs/IMPLEMENTATION-PLAN.md` sowie die Fachdokumente der berührten Domäne
(`CONTENT-PIPELINE.md`, `TYPED-DEFINITIONS.md`, `PHASE3-COMPATIBILITY.md`, `SIMULATION-CORE.md`, `W3D-MATERIALS.md`).

## Schichten und harte Abhängigkeitsregeln

Import → Runtime-Content → Datenmodelle → Simulation → Präsentation. Schichten bleiben getrennt.

- BIG-Archive und Import-VFS existieren nur in `CnCNew.Archives`, `CnCNew.IO`, `CnCNew.Import`.
- Laufzeitcode öffnet nur materialisierte Dateien über `CnCNew.Content` (Schema 2).
  `CnCNew.Content` referenziert weder `CnCNew.Archives` noch das Import-VFS.
- `CnCNew.Import` ist die einzige Verbindung zwischen Import-VFS, Formaten und Runtimevertrag.
- `CnCNew.Data` ist renderer-, engine-, archiv- und filesystemunabhängig. `IniNode` verlässt den
  Compiler nie; Simulation und Präsentation sehen nur `CompiledDefinitionGraph`.
- `CnCNew.Simulation` hängt nur von `CnCNew.Data` ab (der Test `SourceRules` erzwingt das und verbietet Wandzeit, ambiente Zufallszahlen, `float`/`double`, Threads, Datei-/Prozesszugriff und Reflection im Quelltext): kein Stride, kein DirectX, keine
  UI, kein Content-/Archiv-/Filesystemzugriff, keine Wandzeit, keine Threads im Kern,
  kein `Random.Shared`, keine Reflection-Reihenfolgeabhängigkeit.
- Stride-Typen bleiben in `CnCNew.Client.Stride`.
- Die Präsentation liest immutable Simulationssnapshots und schreibt nur typisierte Inputbefehle.

## Nicht verhandelbare Qualitätsregeln

- Unbekannte Schlüssel, Module, Enums, Einheiten oder Referenzen fallen **nie** still auf
  Standardwerte zurück. Jeder Kompatibilitätsfall ist explizit, gezählt, dokumentiert und durch
  `baselines/compatibility.json` (`IniTool compat-check`, Realdatentest der Datentests) gegen Drift abgesichert;
  Änderungen an dieser Datei sind Review-Gegenstand.
- Die Simulation liest nur, was `CnCNew.Data` streng typisiert liefert; nicht implementierte Module machen ein Objekt
  nicht instanziierbar und stehen in `MissingModules`, sie werden nie stillschweigend ignoriert. Neue Simulationsmodule
  brauchen Katalogschema, Bindung, Tests und einen Eintrag in `SIMULATION-CORE.md`.
- Determinismus: kanonische Sortierung, keine absoluten Pfade, Zeitstempel oder
  Dictionary-/HashSet-Reihenfolgen in Artefakten. Zwei unabhängige Läufe müssen byteidentisch sein.
- `TreatWarningsAsErrors=true`. Bekannt und erklärt sind ausschließlich `NU1901`/`NU1903` aus den
  transitiven Stride-4.3-Buildwerkzeugen sowie, seit `CnCNew.Client.Stride` in Phase 7/8 auf Data, Simulation,
  Presentation, Ui, Audio, Game und Formats verweist, die dadurch ausgelösten `EXEC`-Konsolenzeilen von Strides eigenem
  `Stride.Core.Assets.CompilerApp` (`[YamlAssemblyRegistry] Assembly … has not been processed by assembly processor
  with --serialization flags`, je eine pro referenziertem Projekt ohne Stride-Assets, plus einmal
  `[AssetCompiler] Could not find game settings asset …`) – informative Werkzeugausgaben, kein `Fehler`, sie tauchen nur
  bei einem sauberen Build auf (inkrementell cacht der Asset-Compiler-Schritt sie weg) und wachsen 1:1 mit jedem neu
  referenzierten Projekt. Referenzzahl bei sauberem `dotnet build CnCNew.slnx` (`bin`/`obj` von `CnCNew.Client.Stride`
  gelöscht, sonst inkrementell und die Zahl irreführend niedrig): **21 Warnungen** (10 `NU1901`/`NU1903` + 11 Stride-
  Konsolennotizen; vor Phase 7/8 waren es bei drei referenzierten Projekten 14). Jede andere neue Warnung ist ein Fehler,
  bis sie erklärt ist.
- Kein Vertrauen auf Zahlen aus Docs oder früheren Berichten: Bestandszahlen immer neu messen.
- Fremder Code (EA-Quellen, GeneralsGameCode, GeneralsGamePatch unter `references/`) darf gelesen,
  aber nicht unbesehen kopiert werden. Algorithmen werden verstanden, neu geschrieben und im
  Doc mit Quellverweis (Datei, Funktion) begründet.
- Neue NuGet-Abhängigkeiten nur mit Begründung; bevorzugt BCL.

## Lokale Daten (niemals committen)

`.local/` (`game-original`, `assets/by-archive`, `content` = historisches Schema 1,
`phase2-import-a` = **geprüfter Schema-2-Runtimebestand**, `phase2-import-b`, `manifests`,
Snapshots, Captures), `references/`, `bin/`, `obj/`, `.idea/`, `.vs/`, `artifacts/`.
Originalassets, generierte Artefakte und eigener Quellcode bleiben rechtlich und technisch getrennt.
Testfixtures sind klein, synthetisch und rechtlich unbedenklich (keine Auszüge aus Originaldateien).
Temporäre Ausgaben gehören in das Scratchpad der Session, nicht in das Repo.

## Build, Tests, Audits

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
dotnet run --project tests/CnCNew.Simulation.Tests -- --content-dir .local/phase2-import-a
dotnet run -c Release --project tests/CnCNew.Simulation.Tests -- --long
dotnet run --project tests/CnCNew.Data.Tests -- --content-dir .local/phase2-import-a
dotnet run --project tests/CnCNew.W3D.Tests -- --content-dir .local/phase2-import-a
dotnet run --project tests/CnCNew.Ui.Tests
dotnet run --project tests/CnCNew.Presentation.Tests
dotnet run --project tests/CnCNew.Game.Tests -- --content-dir .local/phase2-import-a
dotnet run --project tests/CnCNew.Audio.Tests -- --content-dir .local/phase2-import-a

dotnet run --project tools/CnCNew.AssetTool -- verify .local/phase2-import-a
dotnet run --project tools/CnCNew.IniTool -- audit .local/phase2-import-a
dotnet run --project tools/CnCNew.IniTool -- compile .local/phase2-import-a <ausgabe.json> [--maps include|exclude]
dotnet run --project tools/CnCNew.IniTool -- compat-check .local/phase2-import-a baselines/compatibility.json
dotnet run --project tools/CnCNew.IniTool -- load-order-report .local/phase2-import-a
dotnet run --project tools/CnCNew.IniTool -- debt-report .local/phase2-import-a
dotnet run -c Release --project tools/CnCNew.SimTool -- stress
dotnet run -c Release --project tools/CnCNew.SimTool -- audit-modules .local/phase2-import-a
dotnet run -c Release --project tools/CnCNew.SimTool -- audit-coverage .local/phase2-import-a
dotnet run -c Release --project tools/CnCNew.SimTool -- reference-match .local/phase2-import-a
dotnet run -c Release --project tools/CnCNew.SimTool -- ai-match .local/phase2-import-a --map "Maps/Alpine Assault/Alpine Assault.map" --factions China,GLA --max-ticks 45000
dotnet run --project tools/CnCNew.MapTool -- audit .local/phase2-import-a
dotnet run --project tools/CnCNew.ImageTool -- audit-tga .local/phase2-import-a
dotnet run --project tools/CnCNew.W3dTool -- audit .local/phase2-import-a
dotnet run --project tools/CnCNew.W3dTool -- audit-skeletons .local/phase2-import-a
dotnet run -c Release --project tools/CnCNew.SimTool -- mission-run .local/phase2-import-a --map "Maps/USA01/USA01.map" --ticks 3000
dotnet run --project tools/CnCNew.CampaignTool -- list .local/phase2-import-a
dotnet run -c Release --project tools/CnCNew.CampaignTool -- audit .local/phase2-import-a --ticks 900
dotnet run -c Release --project tools/CnCNew.CampaignTool -- playthrough .local/phase2-import-a --campaign CHALLENGE_0
dotnet run --project tools/CnCNew.CampaignTool -- reachability .local/phase2-import-a [--campaign NAME] [--difficulty easy|normal|hard]
dotnet run --project tools/CnCNew.CampaignTool -- sides .local/phase2-import-a --campaign CHALLENGE_0
dotnet run --project tools/CnCNew.CampaignTool -- script .local/phase2-import-a --map MD_CHI04 --name END_Final_
dotnet run --project tools/CnCNew.PresentationTool -- object-audit .local/phase2-import-a
dotnet run --project tools/CnCNew.PresentationTool -- fx-audit .local/phase2-import-a
dotnet run --project tools/CnCNew.PresentationTool -- terrain-audit .local/phase2-import-a
dotnet run --project src/CnCNew.Client.Stride -c Release -- --smoke-test --content-dir .local/phase2-import-a --model-path Art/W3D/ABArFrcCmd.W3D
dotnet run --project src/CnCNew.Client.Stride -c Release -- --match "Maps/Alpine Assault/Alpine Assault.map" --content-dir .local/phase2-import-a
dotnet run --project src/CnCNew.Client.Stride -c Release -- --mission "Maps/GC_ChemGeneral/GC_ChemGeneral.map" --content-dir .local/phase2-import-a
```

`tools/CnCNew.UiRenderTool` ist bewusst **nicht** in `CnCNew.slnx` (zwei Stride-Game-.exe-Projekte in einer Solution
erzeugen erneut Assetcompiler-Warnrauschen, siehe `docs/CLIENT-UI-RENDERING.md`); es wird eigenständig gebaut und
ausgeführt: `dotnet run --project tools/CnCNew.UiRenderTool -- --content-dir .local/phase2-import-a --out-dir <ausgabe>`.

Tests sind eigenständige Konsolenprogramme (`tests/<Projekt>.Tests/Program.cs`, `OutputType=Exe`,
Exitcode ungleich 0 bei Fehler) mit kleinen lokalen `Assert`/`Throws`-Helfern – kein xUnit/NUnit.
Diesen Stil beibehalten. Für schnelle Wiederholung nach einem Build: `dotnet run --no-build`.

Referenzwerte (Stand Abschluss Phase 6, 30. September 2026; neu messen): Vollinventar mit allen Kartenoverlays 228 geladene
INI-Dateien (28 nach Ladeplan ausgeschlossen), 65.875 Blöcke, 12.574 Definitionen, 16.380 Module, 333.061 Schlüssel, 18.138/19.688
Referenzen aufgelöst (1.550 sichtbare Fallbacks), Definitionssnapshot (Schema 2) 144.832.736 Byte, SHA-256
`AEB5108DDA27ABAC7445452AA2743B794AD0BF1102F2687F8D7D8F7B87D2ADB7`; Retail-Basisdaten (`--maps exclude`) 12.550
Definitionen, 2.115 Objekte, 144.221.861 Byte, SHA-256 `1248585D0BAAB79F5202FDB81F26A2B00962A9D393B0CE1FF93C5FFDF6A5C6E7` (die Hashes
ändern sich, sobald ein Feld strikt typisiert statt als Kompatibilitätswert geführt wird – zuletzt `Locomotor/Surfaces` und `GameData/SellPercentage`).
Modulkatalog 205 Typen (77 implementiert: 12 aus Phase 4, 62 aus Phase 5, 3 aus Phase 6 – `docs/GAME-RULES.md`), Kompatibilitätsbasislinie
741 Einträge, Instanziierbarkeit 1.393 von 2.115 Objekten (65,9 %), Modulverwendungen abgedeckt 15.382 von 19.553 (78,7 %).
Retail-Referenzmatch (`SimTool reference-match`): 13 Fraktionen, Sieger Spieler 12 bei Tick 26.880, Endhash `5C370ED383EC9F6B`.
Phase 6: `MapTool audit` 150/150 Karten (462 Startpaare, 456 vollständig begehbar, 6 teilweise); 16 KI-gegen-KI-Läufe (`SimTool ai-match`, Tabelle `docs/SIMULATION-CORE.md` §19) alle deterministisch, 15 Sieger, 1 Patt.
Jedes `IUpdateModule` muss `nextWake` speichern (Test `SourceRules`).
Asset-Runtimebestand: 25.682 Dateien. Stresstest (216.000 Ticks, `SimTool stress`, synthetische Fixture, mit den Modulen der Phase 5 und Phase 6):
Endhash `D1521974C861A4B9`, Kettenhash `C298F5F74993D6CD`. Snapshot Version 6, Replay-Format v6, `CommandTypes.Version` 8.

Phase 7/8 und Simulationsschulden-Paket E1 (Stand 30. September 2026; neu messen): Modulkatalog jetzt 85 von 205 Typen
implementiert, 1.470 von 2.115 Retail-Objekten instanziierbar (69,5 %), Kompatibilitätsbasislinie 749 Einträge, Snapshot
Version 7, Replay-Format v7, `CommandTypes.Version` 9. Retail-Referenzmatch (`SimTool reference-match`): Sieger
FactionAmerica bei Tick 13.800, Endhash `4DCB46FE3D2E8214`. Weltpräsentation (`docs/CLIENT-TERRAIN.md`,
`docs/CLIENT-OBJECTS.md`, `docs/CLIENT-FX.md`): Terrain 0,266 ms/Frame CPU-Mittel; Objekte 0,19–0,60 ms/Frame über zwei
geprüfte Gefechte; FX-Bench mit 10.000 aktiven Partikeln 1,735 ms je Build; alle drei Layer liefern über wiederholte
Läufe byteidentische SHA-256. Eingabe/UI (`docs/PRESENTATION-INPUT.md`, `docs/GAME-SHELL.md`, `docs/GAME-HUD.md`,
`docs/CLIENT-UI-RENDERING.md`): `CommandMap.ini` 96 Einträge/0 Probleme, 820 Befehlsschaltflächen/472 Befehlssätze/0
Probleme, 8 Shell-Screens mit 54 Golden-Hashes (6 Auflösungen), UI-Renderer-Verifikation über 32 PNGs byteidentisch.
Bekannter, dokumentierter (nicht behobener) Live-/Replay-Determinismus-Unterschied bei geteilten Spezialkräften
(`SpecialPowerBase.SharedReadyFrame()`, `docs/SIMULATION-CORE.md` §12/§12a) – Fix bleibt einer eigenen, vermessenen
Phase vorbehalten. Build unverändert 21 Warnungen beim Solution-Build (10 bekannte `NU1901`/`NU1903` plus 11 bekannte
Stride-Assetcompiler-/`YamlAssemblyRegistry`-Hinweise), 0 davon neu.

Phase 9, Teilpunkt 12 (Stand 30. September 2026; neu messen): Der `SharedReadyFrame`-Determinismus-Fehler oben ist behoben
(`SpecialPowerModuleBase.OnSpawn` legt den Bereitschafts-Tick einer `SharedSynced`-Kraft jetzt deterministisch an statt lesend in
`IsReady()`; volle Diagnose und Messung in `docs/SIMULATION-CORE.md` §12/§12a). Ein neuer Test (`Tests.Skirmish.cs` →
`SkirmishLiveSessionMatchesItsOwnReplay`) vergleicht erstmals den `World.StateHash` einer live gelaufenen `SkirmishMatchSession` gegen
`ReplayRunner.Run` ihres eigenen `ToReplay()`-Mitschnitts und schlägt auf dem unbehobenen Code nachweislich fehl. Unverändert durch den Fix:
Retail-Referenzmatch (`SimTool reference-match`) weiterhin Sieger FactionAmerica bei Tick 13.800, Endhash `4DCB46FE3D2E8214` (das Szenario
nutzt `SkirmishAi` nicht); 216.000-Tick-Stresstest (`SimTool stress`) weiterhin Endhash `ED6F9329F59662E3` (veraltet – seit Phase 11 gemessen `56882F763EABEF67`, auch am Stand vor Phase 11; die synthetische Fixture trägt
keine geteilte Spezialkraft; dieser Hash weicht bereits unabhängig vom Fix von der Phase-6-Referenzzahl oben ab – seit Phase 7/8 nicht neu
gemessen, siehe `docs/HANDOFF-PHASE9.md`). Geändert durch den Fix (KI-Timing verschiebt sich): ein Skirmish-KI-Lauf, der die Kraft
tatsächlich benutzt (`SimTool ai-match .local/phase2-import-a --map "Maps/Alpine Assault/Alpine Assault.map" --factions
FactionAmericaAirForceGeneral,FactionChina --max-ticks 45000`) endet mit Sieger FactionChina bei Tick 16.155, Endhash `0FDF97B63EFECC6A`. Die
16-Karten-KI-Übersicht in `docs/SIMULATION-CORE.md` §19 wurde für diesen Fix nicht neu vermessen (eigene Balance-Erhebung, siehe dort).

Phase 9, Teilpunkt 2 – Skriptdekodierung (Stand 30. September 2026; neu messen; `docs/MAP-SCRIPTS.md`): `PlayerScriptsList` liegt
verschachtelt in `SidesList`, nicht im Top-Level-Chunk-Switch; `MapTool audit` deckt jetzt 150/150 Karten fehlerfrei ab (vorher 75
Fehlschläge). 1.631 Skript-Listen, 23.906 Skripte, 2.294 Gruppen, 33.890 Bedingungen (82 von 109 Typen verwendet), 82.563 Aktionen
(291 von 344 Typen verwendet). Die Typidentität kommt aus dem rohen `ConditionType`/`ScriptActionType`-Enum-Index
(`MapScriptCatalog`, mechanisch aus `Scripts.h` extrahiert), nicht aus dem mitgeschriebenen `NameKeyType` – der ist ein
prozessweiter, laufzeitabhängiger Zähler (`NameKeyGenerator`) und aus einer einzelnen Datei nicht rekonstruierbar. Nur die
Dekodierung ist fertig; die Ausführung (Skript-VM) ist Gegenstand des laufenden Phase-9-Hauptauftrags.

Phase 9, Skriptausführung, Kampagne und Savegames (Stand 1. Oktober 2026; neu messen; `docs/SCRIPT-ENGINE.md`, `docs/CAMPAIGN-AND-SAVEGAMES.md`):
`CnCNew.Simulation/Scripting` führt die dekodierten Kartenskripte aus (Bedingungen 63 ausgeführt/46 fehlend von 109, Aktionen 69 ausgeführt/129
weitergereicht/146 fehlend von 344; fehlende Typen werden zur Laufzeit gezählt, nie still übergangen); `MissionSession`/`MissionSnapshot` (Version 1, bettet
`WorldSnapshot` Version 7 ein) tragen den Missionszustand; `CnCNew.Game/Campaign` (`CampaignCatalog`: 17 Kampagnen/85 Missionen, `CampaignProgress`,
`CampaignController`) und `CnCNew.Game/Save` (`SaveGame` Version 1, `SaveGameStore`) bilden Ablauf und Speicherstände; `CnCNew.CampaignTool` prüft jede
Kampagnenkarte headless. `CnCNew.CampaignTool audit` (24 eindeutige Karten, 900 Ticks): 24/24 deterministisch, 24/24 nach Speichern/Wiederherstellen identisch,
Endproben Sieg 10/24, Niederlage 15/24, 5,7 % der Aktionsausführungen treffen fehlende Typen. Zwei ehrliche Hüllenmodule (`CommandButtonHuntUpdate`,
`LockWeaponCreate`) heben die Instanziierbarkeit auf 1.494 von 2.115 Objekten (70,6 %), 87 von 205 Modultypen, Modulverwendungen abgedeckt 15.639 von 19.553 (80,0 %),
Kompatibilitätsbasislinie 747 Einträge. Retail-Referenzmatch jetzt: Sieger FactionAmerica bei Tick 13.665, `final hash` (Run 1) `D4779BE413835B6D`
(vorher Tick 13.800, `95022B80366E841E`; der früher dokumentierte „Endhash“ `4DCB46FE3D2E8214` ist mit dem Stand vor Phase 9 nicht reproduzierbar);
Stresstest (damalige Angabe; aktuell `56882F763EABEF67`) `ED6F9329F59662E3`. Definitionssnapshot Retail-Basisdaten (`--maps exclude`): 144.284.673 Byte, SHA-256
`2B9D902AEC46A6DE66A21521EFB8FDBE669E27C50F50CC36D3AFEFBEA7ED8D80`; Vollinventar mit Überlagerungen: 144.895.548 Byte, SHA-256
`0A9D10F4FE878F01D14877C0FB6D13DCF7DF7BA3B52FABB0D15B0D8EF13526C3`, 18.321/19.871 Referenzen aufgelöst (1.550 sichtbare Fallbacks). Missionen sind **keine**
Replays (Skriptaktionen sind direkte Weltänderungen); Wahrheit ist der Snapshot. Zeilenenden: das Repo ist überwiegend LF, einzelne Dateien (`README.md`, `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`,
`docs/IMPLEMENTATION-PLAN.md` u. a.) sind CRLF, es gibt kein `.gitattributes`; beim Bearbeiten die vorhandenen Zeilenenden erhalten (die Phase-9-Arbeit hat sie einmal
versehentlich umgestellt und in `9130f44` korrigiert).

Phase 10, vier zusammengeführte Pakete (Stand 1. Oktober 2026; neu messen; `docs/SIMULATION-CORE.md` §23–§25,
`docs/CAMPAIGN-AND-SAVEGAMES.md` §7, `docs/SCRIPT-ENGINE.md` §5/§7/§9): Kollision (`ToppleUpdate`, `StructureToppleUpdate`,
`HeightDieUpdate`, echtes `CrushDie`/`SquishCollide`, lokale Ausweichbewegung), Skript-Befehlsknopf-Fähigkeiten
(`NAMED_/TEAM_USE_COMMANDBUTTON_ABILITY*`, `SpecialAbilityUpdate`, wählbare lokale Seite), Bau-/Regelgrenzen
(Voraussetzungs-/Fähigkeitsprüfung, `MaxSimultaneousOfType`, `OverchargeCommand`, reduzierter `CaptureCommand`, Veteranen-`ArmorSet`/
`WeaponSet`, Höhen-Sichtlinie, `CommandTypes.Version` 9 → 10) und die Kampagnen-Shell (Hauptmenü → Mission → Briefing → Speichern/
Laden → Abschluss, echte `ScriptOutput`-Wiedergabe mit VM-Rückmeldung) sind gemergt. Nach dem Merge frisch gemessen: Modulkatalog
91 von 205 implementiert, Instanziierbarkeit 1.624 von 2.115 (76,8 %), Modulverwendungen abgedeckt 15.843 von 19.553 (81,0 %),
Kompatibilitätsbasislinie 760 Einträge. Retail-Referenzmatch: Sieger FactionGLA bei Tick 19.200, Endhash `28BE35C1174E369F`
(Sieger/Hash ändern sich gegenüber Phase 9, weil die Zufallsproduktion des Szenarios jetzt Voraussetzungen respektiert). Alle 64
Skirmish-Karten mit ≥2 Startpositionen starten und enden fehlerfrei (`SimTool skirmish-sweep`); Skirmish-Speicherstände sind
bewusst nicht unterstützt (`SkirmishMatchSession.CreateSave()` wirft `NotSupportedException`). `MissionSnapshot.Version` 1 → 2
(`humanSideName`). Offen (siehe `docs/HANDOFF-PHASE11.md`): die `SKIRMISH_*`-Skriptfamilie, Garnison-/Aufdeckungs-/Bauteam-Aktionen,
Verstärkungstransport/Formationen, eine Missions-HUD und die Generals'-Challenge-General-Zuordnung in der Shell.

## Code- und Dokumentationsstil

- Bezeichner, Commitmessages und Code-Kommentare englisch; Dokumentation (`docs/`, README) deutsch.
- Commitmessages nach Conventional Commits (`feat(data): …`, `docs: …`, `test: …`, `fix: …`).
- Kommentare nur, wenn das Warum nicht offensichtlich ist. Kein Abstraktionsvorrat auf Verdacht.
- Immutable Vertragstypen (`record`, `ImmutableArray`); Parserknoten und veränderliche
  Zwischenstrukturen bleiben compilerintern.
- Dokumentation und Implementierung müssen am Ende jeder Phase übereinstimmen. Überschätzte
  Abnahmeaussagen in `IMPLEMENTATION-PLAN.md` sind Fehler; bekannte Grenzen werden dort benannt.

## Git-Regeln

- Nur lokal arbeiten. **Keinen Remote anlegen, nichts pushen.**
- Mehrere fachlich geschlossene Commits pro Phase (Implementierung, Tests, Doku getrennt, wenn
  sinnvoll). Vor jedem Commit `git status` prüfen und gezielt stagen (`git add <dateien>`).
- Keine destruktiven Befehle (`reset --hard`, `clean`, `checkout .`, Force-Operationen). Fremde oder
  unerwartete Änderungen bewahren.
- Kein `--no-verify`. Working Tree am Phasenende sauber.

## Ablauf einer Phase

1. Ausgangszustand reproduzieren: `git status`, Build, alle acht Testprogramme, relevante Audits.
2. Fachliche Annahmen gegen reale Daten (`.local/phase2-import-a`) und die Referenzquellen prüfen.
3. Implementieren, jeweils mit Tests; Vollbestandsaudit; zwei unabhängige Läufe vergleichen.
4. Doku aktualisieren (README, ARCHITECTURE, ROADMAP, IMPLEMENTATION-PLAN + Fachdokument).
5. Commits, sauberer Working Tree, Abschlussbericht mit Zahlen und Commit-Hashes.
6. Eigenständigen N+1-Handoff nach `docs/HANDOFF-PHASE<N+1>.md` schreiben und committen.

Phase 11 (Stand 1. Oktober 2026; neu messen; `docs/SCRIPT-ENGINE.md` §4–§9, `docs/GAME-HUD.md` §14, `docs/SIMULATION-CORE.md` §26): die Skript-VM führt jetzt 80 von 109 Bedingungs- und 131 von
344 Aktionstypen aus (129 weitergereicht, 2 aufgezeichnet – `ScriptSupport.Recorded`: Wert gespeichert, aber ohne Verbraucher in der Simulation –, 82 fehlend), darunter die `SKIRMISH_*`-Bedingungen
(Entscheidung Portierung statt `SkirmishAi`-Ersatz), Sicht-/Spezialkraft-Bedingungen, Kartenaufdeckung, Garnison, Bauschalter, `BUILD_TEAM`/`RECRUIT_TEAM`, **Sequenzskripte** und **Teamereignis-Skripte**.
`ScriptEngine` ist auf Teildateien verteilt (`ScriptEngine.Extended*.cs`, `.Sequential.cs`, `.Powers.cs`, `.TeamEvents.cs`). Neuer Zustand wird nur geschrieben, wenn er vom Standard abweicht, damit Hashes
bestehender Läufe unverändert bleiben (Retail-Referenzmatch weiter Sieger FactionGLA Tick 19.200, Endhash `28BE35C1174E369F`; Stresstest `56882F763EABEF67`; keine Versionssprünge: `WorldSnapshot` 7, Replay 7,
`MissionSnapshot` 2, `CommandTypes.Version` 10). Modulkatalog (91/205), Instanziierbarkeit (1.624 von 2.115) und Kompatibilitätsbasislinie (760) unverändert. `CnCNew.CampaignTool reachability` prüft statisch, ob
die `VICTORY`/`DEFEAT`-Skripte jeder Kampagnenkarte mit den implementierten Typen feuern können: Sieg sauber 21 / eingeschränkt 3 / blockiert 0, Niederlage sauber 24 von 24 (Stand Phase 10, gleiche Analyse:
Sieg 4/18/2, Niederlage 0/24/0); „sauber“ heißt nicht „gewinnbar“ – die Ausschalt-Endproben von `audit` erreichen unverändert Sieg 10/24, Niederlage 15/24. Der Zielfahrer `CampaignTool drive` (`MissionObjectiveDriver`) erreicht Sieg 10/24, Niederlage 16/24 auf dem Skriptweg. Offen (`docs/HANDOFF-PHASE12.md`): ein vollständigerer Zielfahrer, der
die Missionsziele auf dem Skriptweg erfüllt, restliche Skriptaktionen, Verstärkung mit Transport, die Persona-Fraktion der Challenge-Wahl und die Client-Verdrahtung der Shell. Die Challenge-Seite wählt
`ChallengeSides` (`CampaignController`), eine Kampagnenmission spielt hinter der Skirmish-Befehlsleiste (`LiveGame`/`LiveMission`/`MissionController`, Stride `--mission`).
**Zeilenenden** (Korrektur): maßgeblich ist `git ls-files --eol <datei>`; CRLF im Index haben `README.md`, `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, `docs/IMPLEMENTATION-PLAN.md`, alles andere ist LF. Zeilenenden pro Datei erhalten
(Skripte: `newline=''`, `\n` im Suchtext nach `\r\n` umwandeln, danach `git ls-files --eol -m` prüfen); mehrzeilige Python-Editskripte als Datei im Scratchpad ausführen statt als Heredoc.
