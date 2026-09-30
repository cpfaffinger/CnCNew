# CnCNew – Arbeitsanweisungen für Coding-Agenten

Lokale C#/.NET-10-Neuimplementierung der für *Command & Conquer: Generals – Zero Hour* benötigten
SAGE-Laufzeit (Windows-Client mit Stride 4.3 / Direct3D 11). Scope des ersten Produkts:
Singleplayer, Skirmish und Kampagne. Nicht im Scope: Multiplayer, binäre Kompatibilität zu alten
Mods und Savegames. Das Repo ist rein lokal (Branch `main`, **kein Remote**).

Die Gesamtplanung steht in `docs/IMPLEMENTATION-PLAN.md`. Die aktuell zu bearbeitende Phase steht
als vollständiger Auftrag in `docs/HANDOFF-PHASE8.md` (Handoff-Datei der jeweils nächsten Phase).
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
dotnet run --project tools/CnCNew.PresentationTool -- object-audit .local/phase2-import-a
dotnet run --project tools/CnCNew.PresentationTool -- fx-audit .local/phase2-import-a
dotnet run --project tools/CnCNew.PresentationTool -- terrain-audit .local/phase2-import-a
dotnet run --project src/CnCNew.Client.Stride -c Release -- --smoke-test --content-dir .local/phase2-import-a --model-path Art/W3D/ABArFrcCmd.W3D
dotnet run --project src/CnCNew.Client.Stride -c Release -- --match "Maps/Alpine Assault/Alpine Assault.map" --content-dir .local/phase2-import-a
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
