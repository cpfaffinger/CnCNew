# Compatibility list (Phase 3, secured as a baseline in Phase 4, carried forward in Phase 5)

As of: 28 September 2026

The compiler never ignores unknown semantics. Definitions that are not needed, simulation/presentation
modules not yet implemented, deferred object/module keys, repeated
scalar keys, diagnostic codes and excluded files are carried as a `CompatibilityEntry` with category,
name, frequency, owner kind and planned target in every canonical snapshot. Since Phase 4 the
list has been checked in as a **verified baseline** (`baselines/compatibility.json`); Phase 5 deliberately regenerated it several times
as soon as typed fields shifted (most recently **746 entries**, no asset contents), and
checks it against drift:

```powershell
dotnet run --project tools/CnCNew.IniTool -- compat-check game/content baselines/compatibility.json
```

The command ends with exit code 1 on every new (`NEW`), missing (`MISSING`) or, in frequency, owner kind or target, changed
(`CHANGED`) entry; the real-data test of `CnCNew.Data.Tests --content-dir` checks the same. The baseline
is produced by `IniTool compat-baseline`; every change is subject to review (full-inventory mode: all map overlays, strict
module catalogue, retail load plan).

Categories (freshly measured, full inventory): `definition` (38 types, 2,899 uses), `module` (178 module types,
10,229 uses – 27 types have been implemented in the meantime, see below), `deferred-field` (184 keys, of which 37
object keys and 147 new Phase 5 module/domain fields — Weapon/Armor/Upgrade/Science/Locomotor/**Faction**,
12,921 uses), `duplicate-key` (24 keys, 120), `diagnostic` (9 codes, 10,567 uses) and
`excluded-file` (28 files).

## Definition domains outside the type contract

38 types (2,899 uses) are fully preserved syntactically and value-typed; their domain-specific contracts follow in
the later phases stated:

- Simulation/world/AI (Phase 4–6): AIData, Bridge, Campaign, ChallengeGenerals, CrateData, MapCache, ObjectCreationList, Rank,
  Road, Terrain, WaterSet, WaterTransparency and Weather (ScriptAction/ScriptCondition belong to `Scripts.ini`, which only the
  WorldBuilder tool loads and which is therefore excluded).
- Presentation/UI (Phase 7–8): Animation, AudioSettings, CommandMap, ControlBarResizer, ControlBarScheme, Credits,
  DrawGroupInfo, DynamicGameLOD, EvaEvent, HeaderTemplate, InGameUI, Language, MappedImage, MiscAudio, Mouse, MouseCursor,
  MultiplayerColor, MultiplayerSettings, MultiplayerStartingMoneyChoice, OnlineChatColors, ShellMenuScheme, StaticGameLOD,
  Video, WebpageURL and WindowTransition.

Compat definitions have IDs of the form `compatibility://<typ>/<name>`; six definitions of different
types with the same name therefore no longer merge.

## Module/behavior inventory

The catalogue `ModuleCatalog` carries all **205** module types of the shipped content (an earlier count of 206 included
the phantom module `ArmorSet`). **74** are implemented (as of the end of Phase 5): the 12 from Phase 4 plus 62 from Phase 5 – the
complete list is printed by `SimTool audit-modules <content-dir>`, details and simplifications are in `docs/GAME-RULES.md`. The remaining
131 are in the baseline (22 of them pure presentation modules without relation to the simulation) with frequency, owner kind and target; a list of the uses per module is provided by
`SimTool audit-modules <content-dir>`. `StrictUnknownModules` fails on every name outside the catalogue, so that a
typo can be distinguished from a known module that is not yet implemented.

## Visible special cases

- `GeometryMajorRadius = 18.5.0` and `GeometryHeight = 40v.0` are explicit compat values (`shipped-double-decimal-radius`,
  `shipped-real-trailing-text`, both read like `sscanf("%f")`) and counted in `compatibility-value` (2).
- Negative durations (`MinLifetime = -1`) wrap modulo 2^32 like `sscanf("%u")` (`compatibility-unsigned-wrap`, 10).
- Surplus tokens after single values (`compatibility-ignored-tokens`, 3).
- Repeated scalar keys in the same block: the last one wins (`compatibility-duplicate-key`, 120 uses in 24
  keys; repeatable keys such as `Armor` or `Locomotor` keep their multiplicity and are not listed here).
- References: 1,550 uses are visible fallbacks in the full inventory (1,523 in the base data; re-measured; the additional real references from the
  Phase 5 domain typing, see `TYPED-DEFINITIONS.md`, all resolve and do not change this number) – very
  predominantly `compatibility-engine-veterancy-upgrade` (`Upgrade_Veterancy_ELITE`/`_HEROIC`, engine-internal names without
  an INI definition, via the `AutoHealBehavior` inherited from the `DefaultThingTemplate`) and a small number of
  `compatibility-unresolved-reference` (`DieByFireUSA`, `Boss_VehicleSentryDrone`); no silent replacement by another
  target.
- **Phase 5 parser correction:** `SageIniParser` recognised the `End` keyword only if the entire line read exactly `End`;
  the original (`INI::initFromINIMulti`) compares only the first `strtok` token and ignores the rest of the
  line. Two objects in the content carry the typo `End Upgrade_AmericaHallfireDrone`
  (`AmericaVehicle.ini`, `BossGeneral.ini`); without the correction the block stayed open and swallowed all following
  sibling blocks up to the next `End`, which went unnoticed as long as no implemented module validated the swallowed
  fields. The correction changes the object count (2,113 → 2,115) and the definition fingerprint.
- The full inventory applies all map overlays together. As a result, targets of `ReplaceModule` (52,
  `compatibility-missing-replace-target`) and `RemoveModule` (51, `compatibility-missing-remove-target`, re-measured) are missing,
  and repeated tags from several overlays occur (`compatibility-duplicate-module-tag`, 95, re-measured; 18 of them
  in the base data). A single overlay keeps correct semantics; `--maps exclude` provides the base data without these
  aggregate artefacts.
- Four `AddModule` blocks contain no module (`compatibility-add-module-without-module`); their fields act on the
  object, as in the original.
- Excluded files (28) are in the list with a reason: debug files (`GameDataDebug.ini`, `CommandMapDebug.ini`,
  `CommandMapDemo.ini`), the WorldBuilder file `Scripts.ini`, unreferenced files (`MemoryPools.ini`,
  `Default/ButtonSets.ini`, `Default/Webpages.ini`, installer `Options.ini`) and 20 language variants.

## State after Phase 6

Phase 6 strictly typed `Locomotor/Surfaces` and `GameData/SellPercentage` and added catalogue schemas for `PowerPlantUpdate`, `PowerPlantUpgrade` and `OverchargeBehavior`
(`docs/TYPED-DEFINITIONS.md`, section "Phase 6"). The compatibility baseline `baselines/compatibility.json` has **741 entries** (516 `deferred-field`, 36 `definition`, 9 `diagnostic`,
24 `duplicate-key`, 28 `excluded-file`, 128 `module`; `IniTool compat-check` confirms it, 30 September 2026). The number `746` and all earlier values are superseded.
