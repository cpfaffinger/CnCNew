# World-Builder-Skripte (Phase 9, Teil A Punkt 2 – Dekodierung)

Dieses Dokument beschreibt, wie `CnCNew.Formats` die World-Builder-Skripte einer `.map`-Datei (`PlayerScriptsList`)
verlustfrei in ein typisiertes, unveränderliches Modell überführt. Es deckt nur die **Dekodierung** ab; die
**Ausführung** (die deterministische Skript-VM in `CnCNew.Simulation`, Missionssitzung, Abdeckungstabellen und Messwerte) steht in
`docs/SCRIPT-ENGINE.md`.

## 1. Fundstelle im Containerformat

Entgegen einer naheliegenden Annahme ist `PlayerScriptsList` **kein** Top-Level-Chunk der `.map`-Datei, sondern liegt
**verschachtelt in `SidesList`**, hinter der Team-Liste (`SidesList::ParseSidesDataChunk`,
`ScriptList::WriteScriptsDataChunk`, `GeneralsMD/Code/GameEngine/Source/GameLogic/ScriptEngine/Scripts.cpp`). Der
Decoder liest ihn deshalb in `SageMapDecoder.ReadSides`, nicht im äußeren Chunk-Switch.

Die Verschachtelung folgt dem gleichen Chunky-Format wie der Rest der Datei (benannte, versionierte Kindchunks) und
nutzt dieselbe `ChunkReader`-Idiomatik wie `ReadSides`/`ReadObjects`/`ReadTriggers`:

```
PlayerScriptsList (v1)
  ScriptList (v1)            – einer pro Spieler/Seite, in Dateireihenfolge
    Script (v1/v2)           – Top-Level-Skript
    ScriptGroup (v1/v2)      – benannte, einklappbare Gruppe
      Script (v1/v2)
  ScriptList (v1)
  ...
```

Ein `Script` enthält wiederum 0+ `OrCondition`-Kindchunks (je eine ODER-Verzweigung), 0+ `ScriptAction`-Kindchunks
(Wahr-Zweig) und 0+ `ScriptActionFalse`-Kindchunks (Falsch-Zweig); eine `OrCondition` enthält 0+ `Condition`-Kindchunks
(die UND-Kette dieser Verzweigung). Jede `Condition`/`ScriptAction` trägt eine feste Anzahl `Parameter` (siehe unten).

## 2. Die Typidentität: NameKeyType ist nicht rekonstruierbar

`Condition`/`ScriptAction` schreiben zuerst den rohen `ConditionType`/`ScriptActionType`-Enum-Index (`Scripts.h`), ab
Chunk-Version 4 (Condition) bzw. 2 (ScriptAction) zusätzlich einen `NameKeyType`. Das Original nutzt den NameKeyType als
primäre Identität und den Enum-Index nur als Rückfallebene ("rematch by name", `Scripts.cpp` ~1701/~2463).

**Dieser Decoder macht es umgekehrt, mit Absicht:** `NameKeyType` wird von `NameKeyGenerator::nameToKey`
(`Common/NameKeyGenerator.cpp`) **prozessweit, in Erstregistrierungsreihenfolge über die gesamte laufende Engine**
vergeben (`m_nextID++` bei jedem neuen, noch nie gesehenen String) – kein Hash, keine deterministisch aus dem String
ableitbare Zahl. Ein einzelner `.map`-Container kennt nur sein **eigenes** kleines Symboltabellen-Wörterbuch (dieselbe
Tabelle, die `ReadChunkHeader`/`ReadDict` für Chunknamen und Dict-Schlüssel nutzen) – das enthält NameKeyType-Werte aus
Skripten nur zufällig, wenn derselbe String auch als Dict-Schlüssel oder Chunkname in derselben Datei vorkommt. Ein
Versuch, `NameKeyType` über diese Tabelle aufzulösen, schlägt bei echten Retail-Karten regelmäßig mit "unknown symbol
id" fehl (verifiziert: 75 von 150 Karten). Ohne das exakte, damals kompilierte WorldBuilder-Binary (nicht nur den
Quelltext) ist der numerische NameKeyType-Wert nicht in einen String zurückzuübersetzen.

**Der rohe Enum-Index dagegen ist ein gewöhnlicher, lückenloser C++-`enum`-Wert** (`Condition::ConditionType`,
`ScriptAction::ScriptActionType`, `Scripts.h`) – rein lexikalisch aus der Deklarationsreihenfolge bestimmt, unabhängig
von Laufzeitzustand. `MapScriptCatalog` (`src/CnCNew.Data/World/MapScriptCatalog.cs`) enthält beide Tabellen
(`ConditionTypeNames`, 109 Einträge; `ScriptActionTypeNames`, 344 Einträge) **programmatisch aus dem Header-Text
extrahiert** (Skript im Scratchpad dieser Sitzung, nicht im Repo – bei einer künftigen Aktualisierung von `Scripts.h`
denselben Ansatz wiederholen: Enum-Rumpf isolieren, `//`-Kommentare entfernen, an Kommas splitten, bis `NUM_ITEMS`).
Der Decoder liest den `NameKeyType` weiterhin (um den Bytestrom korrekt zu konsumieren), verwirft ihn aber und löst die
Typidentität ausschließlich über den Enum-Index auf. `MapCondition.TypeId`/`MapScriptAction.TypeId` behalten den rohen
Wert zu Diagnosezwecken.

Ein Index außerhalb der bekannten Tabelle (z. B. durch einen Patch, der neue Aktionen anhängt) erzeugt einen
synthetischen, gezählten Platzhalternamen (`UNKNOWN_CONDITION_<id>`/`UNKNOWN_ACTION_<id>`) statt eines stillen
Fehlschlags – bislang bei keiner der 150 Retail-Karten beobachtet.

## 3. Parameter

`Parameter::ReadParameter` (`Scripts.cpp`) ist vollständig selbstbeschreibend und unabhängig von Condition/Action:
jeder Parameter schreibt zuerst seinen `ParameterType` (`Scripts.h`, 50 Werte, `Int=0` … `Percent=49`), danach
**immer** `Int` + `Real` + `AsciiString` – außer bei `Coord3D`, das stattdessen drei `Real`-Werte (x, y, z) schreibt.
Welches Feld inhaltlich gemeint ist, bestimmt ausschließlich `Type`; die anderen Felder sind bei einem gegebenen
Parameter zwar vorhanden, aber bedeutungslos. `MapScriptParameter` bildet das 1:1 ab.

## 4. Datenmodell (`src/CnCNew.Data/World/MapModel.cs`)

`MapScriptParameterType` (Wire-Reihenfolge = Enum-Reihenfolge), `MapScriptParameter`, `MapCondition`, `MapOrCondition`,
`MapScriptAction`, `MapScript`, `MapScriptGroup`, `MapScriptList`. `MapData.PlayerScripts` ist ein
`ImmutableArray<MapScriptList>`, ein Eintrag pro Spieler/Seite in Dateireihenfolge (korrespondiert mit `MapData.Sides`).
Alles unveränderlich, renderer- und filesystemunabhängig, wie der Rest von `CnCNew.Data`.

## 5. Reale Zahlen (`game/content`, `MapTool audit`, 30. September 2026 – neu messen)

150 von 150 Karten dekodieren fehlerfrei (vorher: 75 schlugen fehl – 25 Kampagnenkarten mit Condition-Version 2/3 ohne
NameKeyType, 50 weitere an der NameKeyType-Fehlannahme; beide Ursachen sind mit dem Enum-Index-Ansatz behoben).

- **1.631** Skript-Listen (eine pro Spieler/Seite über alle Karten), **23.906** Skripte, **2.294** Gruppen (7
  Unterprogramme), **26.634** ODER-Verzweigungen, **33.890** Bedingungen, **81.991** Wahr-Aktionen, **572**
  Falsch-Aktionen.
- **82 von 109** Bedingungstypen tatsächlich verwendet, **291 von 344** Aktionstypen. Häufigste Bedingungen:
  `CONDITION_TRUE` (7.342), `TIMER_EXPIRED` (5.797), `FLAG` (4.455), `COUNTER` (4.021),
  `PLAYER_HAS_OBJECT_COMPARISON` (1.594). Häufigste Aktionen: `ENABLE_SCRIPT` (8.205),
  `OBJECTLIST_ADDOBJECTTYPE` (6.338), `SET_MILLISECOND_TIMER` (5.182), `SET_FLAG` (4.497), `SPEECH_PLAY` (3.273),
  `CALL_SUBROUTINE` (2.858). Vollständige, nach Häufigkeit sortierte Listen: `MapTool audit <content-dir>`.
- Häufigste Parametertypen: `Team` (25.473), `Counter` (19.421), `Boolean` (17.553), `Int` (16.549),
  `ObjectType` (16.491), `Unit` (14.697), `Real` (14.587). Alle 50 Parametertypen kommen in Retail-Daten vor, keine
  Ausreißer oder unerwarteten Werte.
- `MapTool info <content-dir> <pfad>` zeigt zusätzlich die Skriptanzahl einer einzelnen Karte.

Diese Häufigkeitstabelle war die Priorisierungsgrundlage für die Skript-VM (`docs/SCRIPT-ENGINE.md`): die vier häufigsten
Bedingungen und die sechs häufigsten Aktionen sind zuerst implementiert worden; die Laufzeit-Häufigkeit der *noch fehlenden* Typen misst
seither `CnCNew.CampaignTool audit` (`docs/SCRIPT-ENGINE.md` §9) und steuert die weitere Reihenfolge.

## 5a. Teamschlüssel, die die VM liest (Phase 11)

`ScriptProgram` liest aus jedem Eintrag der `TeamsList` (`ScriptTeamPrototype`): `teamName`, `teamOwner`, `teamIsSingleton`, `teamMaxInstances`, die Einheitszusammensetzung
(`teamUnitType1..7`, `teamUnitMinCount1..7`, `teamUnitMaxCount1..7`), `teamTransport`, `teamReinforcementOrigin` und seit Phase 11 `teamHome`, `teamProductionPriority`,
`teamIsAIRecruitable` (Rekrutierung, `RECRUIT_TEAM`) sowie die sechs Ereignisskripte `teamOnCreateScript`, `teamOnIdleScript`, `teamEnemySightedScript`, `teamAllClearScript`,
`teamOnDestroyedScript` (mit `teamDestroyedThreshold`) und `teamOnUnitDestroyedScript` (`docs/SCRIPT-ENGINE.md` §5b). Weitere Teamschlüssel des Originals (`teamTransportsReturn`,
`teamProductionCondition`, `teamExecutesActionsOnCreate`, `teamProductionPrioritySuccessIncrease/FailureDecrease`, `teamAggressiveness`, `teamAutoReinforce` …) werden **nicht** gelesen.

## 6. Bekannte Grenzen

- Die "heal old files"-Kompatibilitätsfälle des Originals (z. B. `TEAM_FOLLOW_WAYPOINTS` mit fehlendem dritten
  Parameter in alten Dateien, `Scripts.cpp` ~2500–2606) sind nicht nachgebildet, da sie ausschließlich an der
  `NameKeyType`-Identität hängen, die dieser Decoder nicht verwendet, und bei keiner der 150 Retail-Karten beobachtet
  wurden (alle Parameteranzahlen passen zur jeweiligen Aktion). Sollte eine künftige Karte doch eine dieser Altlasten
  enthalten, bricht der Decoder nicht ab (er liest, was auf der Platte steht), aber die Parameterliste entspricht dann
  nicht dem, was `ScriptActions`/`ScriptConditions` zur Ausführung erwarten – die VM behandelt eine zu kurze
  Parameterliste explizit (`Has(...)`: Diagnose `<Typ>: n of m parameters`, die Aktion/Bedingung tut nichts), der Decoder bleibt unverändert.
- `GlobalLighting` bleibt unabhängig davon ein übersprungener Chunk (siehe `docs/PRESENTATION-WORLD.md`).
