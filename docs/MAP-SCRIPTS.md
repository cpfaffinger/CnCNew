# World Builder scripts (Phase 9, part A item 2 – decoding)

This document describes how `CnCNew.Formats` converts the World Builder scripts of a `.map` file (`PlayerScriptsList`)
losslessly into a typed, immutable model. It covers only the **decoding**; the
**execution** (the deterministic script VM in `CnCNew.Simulation`, mission session, coverage tables and measurements) is in
`docs/SCRIPT-ENGINE.md`.

## 1. Location in the container format

Contrary to an obvious assumption, `PlayerScriptsList` is **not** a top-level chunk of the `.map` file, but lies
**nested inside `SidesList`**, behind the team list (`SidesList::ParseSidesDataChunk`,
`ScriptList::WriteScriptsDataChunk`, `GeneralsMD/Code/GameEngine/Source/GameLogic/ScriptEngine/Scripts.cpp`). The
decoder therefore reads it in `SageMapDecoder.ReadSides`, not in the outer chunk switch.

The nesting follows the same chunky format as the rest of the file (named, versioned child chunks) and
uses the same `ChunkReader` idiom as `ReadSides`/`ReadObjects`/`ReadTriggers`:

```
PlayerScriptsList (v1)
  ScriptList (v1)            – one per player/side, in file order
    Script (v1/v2)           – top-level script
    ScriptGroup (v1/v2)      – named, collapsible group
      Script (v1/v2)
  ScriptList (v1)
  ...
```

A `Script` in turn contains 0+ `OrCondition` child chunks (one OR branch each), 0+ `ScriptAction` child chunks
(true branch) and 0+ `ScriptActionFalse` child chunks (false branch); an `OrCondition` contains 0+ `Condition` child chunks
(the AND chain of this branch). Every `Condition`/`ScriptAction` carries a fixed number of `Parameter`s (see below).

## 2. The type identity: NameKeyType cannot be reconstructed

`Condition`/`ScriptAction` first write the raw `ConditionType`/`ScriptActionType` enum index (`Scripts.h`), and from
chunk version 4 (Condition) or 2 (ScriptAction) on additionally a `NameKeyType`. The original uses the NameKeyType as
the primary identity and the enum index only as a fallback ("rematch by name", `Scripts.cpp` ~1701/~2463).

**This decoder does it the other way round, on purpose:** `NameKeyType` is assigned by `NameKeyGenerator::nameToKey`
(`Common/NameKeyGenerator.cpp`) **process-wide, in first-registration order across the whole running engine**
(`m_nextID++` for every new, never before seen string) – no hash, no number that can be derived deterministically from
the string. A single `.map` container knows only its **own** small symbol table dictionary (the same
table that `ReadChunkHeader`/`ReadDict` use for chunk names and dict keys) – it contains NameKeyType values from
scripts only by chance, when the same string also occurs as a dict key or chunk name in the same file. An
attempt to resolve `NameKeyType` through this table regularly fails on real retail maps with "unknown symbol
id" (verified: 75 of 150 maps). Without the exact WorldBuilder binary compiled at the time (not just the
source code), the numeric NameKeyType value cannot be translated back into a string.

**The raw enum index, by contrast, is an ordinary, gapless C++ `enum` value** (`Condition::ConditionType`,
`ScriptAction::ScriptActionType`, `Scripts.h`) – determined purely lexically from the declaration order, independent
of runtime state. `MapScriptCatalog` (`src/CnCNew.Data/World/MapScriptCatalog.cs`) contains both tables
(`ConditionTypeNames`, 109 entries; `ScriptActionTypeNames`, 344 entries) **extracted programmatically from the header
text** (script in the scratchpad of this session, not in the repo – on a future update of `Scripts.h`
repeat the same approach: isolate the enum body, remove `//` comments, split at commas, up to `NUM_ITEMS`).
The decoder still reads the `NameKeyType` (to consume the byte stream correctly), but discards it and resolves the
type identity exclusively through the enum index. `MapCondition.TypeId`/`MapScriptAction.TypeId` keep the raw
value for diagnostic purposes.

An index outside the known table (e.g. through a patch that appends new actions) produces a
synthetic, counted placeholder name (`UNKNOWN_CONDITION_<id>`/`UNKNOWN_ACTION_<id>`) instead of a silent
failure – so far not observed on any of the 150 retail maps.

## 3. Parameters

`Parameter::ReadParameter` (`Scripts.cpp`) is fully self-describing and independent of condition/action:
every parameter first writes its `ParameterType` (`Scripts.h`, 50 values, `Int=0` … `Percent=49`), then
**always** `Int` + `Real` + `AsciiString` – except for `Coord3D`, which writes three `Real` values (x, y, z) instead.
Which field is meant in terms of content is determined exclusively by `Type`; the other fields are present for a given
parameter, but meaningless. `MapScriptParameter` maps this 1:1.

## 4. Data model (`src/CnCNew.Data/World/MapModel.cs`)

`MapScriptParameterType` (wire order = enum order), `MapScriptParameter`, `MapCondition`, `MapOrCondition`,
`MapScriptAction`, `MapScript`, `MapScriptGroup`, `MapScriptList`. `MapData.PlayerScripts` is an
`ImmutableArray<MapScriptList>`, one entry per player/side in file order (corresponds to `MapData.Sides`).
Everything immutable, renderer- and filesystem-independent, like the rest of `CnCNew.Data`.

## 5. Real numbers (`game/content`, `MapTool audit`, 30 September 2026 – re-measure)

150 of 150 maps decode without errors (before: 75 failed – 25 campaign maps with condition version 2/3 without
NameKeyType, 50 more on the wrong NameKeyType assumption; both causes are fixed by the enum index approach).

- **1,631** script lists (one per player/side across all maps), **23,906** scripts, **2,294** groups (7
  subroutines), **26,634** OR branches, **33,890** conditions, **81,991** true actions, **572**
  false actions.
- **82 of 109** condition types actually used, **291 of 344** action types. Most frequent conditions:
  `CONDITION_TRUE` (7,342), `TIMER_EXPIRED` (5,797), `FLAG` (4,455), `COUNTER` (4,021),
  `PLAYER_HAS_OBJECT_COMPARISON` (1,594). Most frequent actions: `ENABLE_SCRIPT` (8,205),
  `OBJECTLIST_ADDOBJECTTYPE` (6,338), `SET_MILLISECOND_TIMER` (5,182), `SET_FLAG` (4,497), `SPEECH_PLAY` (3,273),
  `CALL_SUBROUTINE` (2,858). Complete lists sorted by frequency: `MapTool audit <content-dir>`.
- Most frequent parameter types: `Team` (25,473), `Counter` (19,421), `Boolean` (17,553), `Int` (16,549),
  `ObjectType` (16,491), `Unit` (14,697), `Real` (14,587). All 50 parameter types occur in retail data, no
  outliers or unexpected values.
- `MapTool info <content-dir> <pfad>` additionally shows the script count of a single map.

This frequency table was the basis for prioritising the script VM (`docs/SCRIPT-ENGINE.md`): the four most frequent
conditions and the six most frequent actions were implemented first; the runtime frequency of the *still missing* types has since been measured by
`CnCNew.CampaignTool audit` (`docs/SCRIPT-ENGINE.md` §9) and steers the further order.

## 5a. Team keys that the VM reads (Phase 11)

`ScriptProgram` reads from every entry of the `TeamsList` (`ScriptTeamPrototype`): `teamName`, `teamOwner`, `teamIsSingleton`, `teamMaxInstances`, the unit composition
(`teamUnitType1..7`, `teamUnitMinCount1..7`, `teamUnitMaxCount1..7`), `teamTransport`, `teamReinforcementOrigin` and since Phase 11 `teamHome`, `teamProductionPriority`,
`teamIsAIRecruitable` (recruiting, `RECRUIT_TEAM`) as well as the six event scripts `teamOnCreateScript`, `teamOnIdleScript`, `teamEnemySightedScript`, `teamAllClearScript`,
`teamOnDestroyedScript` (with `teamDestroyedThreshold`) and `teamOnUnitDestroyedScript` (`docs/SCRIPT-ENGINE.md` §5b). Further team keys of the original (`teamTransportsReturn`,
`teamProductionCondition`, `teamExecutesActionsOnCreate`, `teamProductionPrioritySuccessIncrease/FailureDecrease`, `teamAggressiveness`, `teamAutoReinforce` …) are **not** read.

## 6. Known limits

- The "heal old files" compatibility cases of the original (e.g. `TEAM_FOLLOW_WAYPOINTS` with a missing third
  parameter in old files, `Scripts.cpp` ~2500–2606) are not reproduced, because they depend exclusively on the
  `NameKeyType` identity, which this decoder does not use, and were not observed on any of the 150 retail maps
  (all parameter counts match the respective action). Should a future map nevertheless contain one of these legacy
  cases, the decoder does not abort (it reads what is on disk), but the parameter list then does not match
  what `ScriptActions`/`ScriptConditions` expect for execution – the VM handles a parameter list that is too short
  explicitly (`Has(...)`: diagnostic `<Typ>: n of m parameters`, the action/condition does nothing), the decoder stays unchanged.
- `GlobalLighting` remains, independently of this, a skipped chunk (see `docs/PRESENTATION-WORLD.md`).
