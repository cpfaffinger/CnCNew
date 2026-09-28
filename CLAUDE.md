# CnCNew – Arbeitsanweisungen für Coding-Agenten

Lokale C#/.NET-10-Neuimplementierung der für *Command & Conquer: Generals – Zero Hour* benötigten
SAGE-Laufzeit (Windows-Client mit Stride 4.3 / Direct3D 11). Scope des ersten Produkts:
Singleplayer, Skirmish und Kampagne. Nicht im Scope: Multiplayer, binäre Kompatibilität zu alten
Mods und Savegames. Das Repo ist rein lokal (Branch `main`, **kein Remote**).

Die Gesamtplanung steht in `docs/IMPLEMENTATION-PLAN.md`. Die aktuell zu bearbeitende Phase steht
als vollständiger Auftrag in `docs/HANDOFF-PHASE4.md` (Handoff-Datei der jeweils nächsten Phase).
Lies vor jeder Phase `README.md`, `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`,
`docs/IMPLEMENTATION-PLAN.md` sowie die Fachdokumente der berührten Domäne
(`CONTENT-PIPELINE.md`, `TYPED-DEFINITIONS.md`, `PHASE3-COMPATIBILITY.md`, `W3D-MATERIALS.md`).

## Schichten und harte Abhängigkeitsregeln

Import → Runtime-Content → Datenmodelle → Simulation → Präsentation. Schichten bleiben getrennt.

- BIG-Archive und Import-VFS existieren nur in `CnCNew.Archives`, `CnCNew.IO`, `CnCNew.Import`.
- Laufzeitcode öffnet nur materialisierte Dateien über `CnCNew.Content` (Schema 2).
  `CnCNew.Content` referenziert weder `CnCNew.Archives` noch das Import-VFS.
- `CnCNew.Import` ist die einzige Verbindung zwischen Import-VFS, Formaten und Runtimevertrag.
- `CnCNew.Data` ist renderer-, engine-, archiv- und filesystemunabhängig. `IniNode` verlässt den
  Compiler nie; Simulation und Präsentation sehen nur `CompiledDefinitionGraph`.
- `CnCNew.Simulation` (ab Phase 4) hängt nur von `CnCNew.Data` ab: kein Stride, kein DirectX, keine
  UI, kein Content-/Archiv-/Filesystemzugriff, keine Wandzeit, keine Threads im Kern,
  kein `Random.Shared`, keine Reflection-Reihenfolgeabhängigkeit.
- Stride-Typen bleiben in `CnCNew.Client.Stride`.
- Die Präsentation liest immutable Simulationssnapshots und schreibt nur typisierte Inputbefehle.

## Nicht verhandelbare Qualitätsregeln

- Unbekannte Schlüssel, Module, Enums, Einheiten oder Referenzen fallen **nie** still auf
  Standardwerte zurück. Jeder Kompatibilitätsfall ist explizit, gezählt, dokumentiert und
  (ab Phase 4) durch einen Test gegen Drift abgesichert.
- Determinismus: kanonische Sortierung, keine absoluten Pfade, Zeitstempel oder
  Dictionary-/HashSet-Reihenfolgen in Artefakten. Zwei unabhängige Läufe müssen byteidentisch sein.
- `TreatWarningsAsErrors=true`. Bekannt und erklärt sind ausschließlich `NU1901`/`NU1903` aus den
  transitiven Stride-4.3-Buildwerkzeugen (10 Warnungen beim Solution-Build). Jede neue Warnung
  ist ein Fehler, bis sie erklärt ist.
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
dotnet run --project tests/CnCNew.W3D.Tests -- --content-dir .local/phase2-import-a

dotnet run --project tools/CnCNew.AssetTool -- verify .local/phase2-import-a
dotnet run --project tools/CnCNew.IniTool -- audit .local/phase2-import-a
dotnet run --project tools/CnCNew.IniTool -- compile .local/phase2-import-a <ausgabe.json>
dotnet run --project tools/CnCNew.ImageTool -- audit-tga .local/phase2-import-a
dotnet run --project tools/CnCNew.W3dTool -- audit .local/phase2-import-a
dotnet run --project src/CnCNew.Client.Stride -c Release -- --smoke-test --content-dir .local/phase2-import-a --model-path Art/W3D/ABArFrcCmd.W3D
```

Tests sind eigenständige Konsolenprogramme (`tests/<Projekt>.Tests/Program.cs`, `OutputType=Exe`,
Exitcode ungleich 0 bei Fehler) mit kleinen lokalen `Assert`/`Throws`-Helfern – kein xUnit/NUnit.
Diesen Stil beibehalten. Für schnelle Wiederholung nach einem Build: `dotnet run --no-build`.

Referenzwerte (Stand Phase 3, neu messen): 256 INI-Dateien, 66.623 Blöcke, 12.677 Definitionen,
16.360 Module, 335.592 Schlüssel, 12.441/12.468 Referenzen aufgelöst, Definitionssnapshot
112.664.082 Byte, SHA-256 `14154C512DED47C4AD72B651F8FCB0BA702EACBF8678C51DDAB7D125BEC675D4`.
Asset-Runtimebestand: 25.682 Dateien.

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
