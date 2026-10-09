# Typed definition contract

As of: 28 September 2026 (Phase 3 hardened by block 0 of Phase 4; Weapon/Armor/Upgrade/Science/Locomotor
domain-typed in block 0.1 of Phase 5, see the last section)

## Layer and public contract

`CnCNew.Data` compiles SAGE INI into an immutable `CompiledDefinitionGraph`. The
public graph contains no `IniNode` instances. Parser nodes exist exclusively
during compilation; simulation and later systems receive derived definitions,
`DefinitionId`, typed values, modules and resolved reference edges. The library has no
dependency on archives, import VFS, Stride or presentation.

Dedicated definition types exist for Object, Weapon, WeaponSet, Armor, Damage, Upgrade, Science,
Player, Faction, CommandButton, CommandSet, FXList/FX, AudioEvent, DialogEvent, MusicTrack,
AmbientStream, Locomotor, ExperienceLevel, SpecialPower as well as GameData/GlobalData. Name,
original spelling and virtual source position are preserved. `DefinitionValue` separates
boolean, integer, finite decimal number, time, distance, speed, angle, percent, money,
vectors, colour, colour keyframe, integer list, enum, flags, list, reference and sentinel.
Numbers are read invariantly; NaN, Infinity and overflows are errors.

All other nested blocks are preserved recursively as immutable `DefinitionSection`
(WeaponSet, ArmorSet, Prerequisites, ConditionState, …).

## Value grammar (block 0.3)

Unknown tokens are errors, never `Text`.

- **Colours** `R:n G:n B:n [A:n]` and **vectors** `X:n Y:n [Z:n]` are parsed strictly, independent of the key
  (labels case-insensitive, `:` acts like a separator as in `INI::getNextSubToken`, therefore
  `X: 60.0  Y:  35.0   Z:0.0`, `X:-30 Y:15: Z:10` and `X:.071` in the content are valid too). Colour components are
  0–255; a `//` comment after a colour is allowed; `R:n G:n B:n <frame>` (particle colour keyframes) is the
  separate value `ColorKeyframe`. Malformed forms fail.
- **Enums and flags** hang on **strict name tables**, taken over mechanically from the reference sources
  (`NameTables`, order = bit index): `KindOf` (120, `KindOf.cpp`), `ObjectStatus` (45), `DamageType` (38), `DeathType`
  (21), `Veterancy` (4), `Geometry` (3), `RadarPriority`, `BuildableStatus`, `BuildCompletion`, `EditorSorting`, `Shadow`,
  `LocomotorSet`. Flags have the semantics of `BitFlags::parse`: simple lists, `NONE`, `+`/`−` deltas (not
  mixable); relative deltas are resolved against the inherited value. `DeathTypes`/`VeterancyLevels` follow
  `INI::parseDeathTypeFlags` (start always `ALL`, `ALL`/`NONE`/`+X`/`−X`).
- **Single-token types** read, like the original, only the first token; surplus tokens become visible as
  `compatibility-ignored-tokens` (3 cases in the content, for instance `ExperienceRequired` with five values,
  `EditorSorting = MISC_MAN_MADE CLEARED_BY_BUILD`). A stray `=` in the value vanishes as with `INI::m_seps`.
- **Real numbers** follow `sscanf("%f")`: exact numbers are typed, numbers with trailing text (the two shipped typos
  `GeometryMajorRadius = 18.5.0` and `GeometryHeight = 40v.0`) are explicit `Compatibility` values
  (`shipped-double-decimal-radius`, `shipped-real-trailing-text`), no number at all is an error.
- **Durations** (`FieldType.DurationMs`) follow `sscanf("%u")`: negative numbers wrap modulo 2^32 and are reported as
  `compatibility-unsigned-wrap` (10 cases in map overlays).

## Key schemas (block 0.3)

- **Object head:** `ObjectSchema` contains all keys of `ThingTemplate::s_objectFieldParseTable` (96 keys, of which 43 `Opaque`). Typed:
  `KindOf`, `Geometry*`, `Shadow`, `RadarPriority`, `Buildable`, `BuildCompletion`, `EditorSorting` (enums/flags),
  vision ranges/radii (distance), `BuildCost`/`RefundValue` (money, 0–65535), `BuildTime` (seconds – previously mistyped as
  milliseconds), `PlacementViewAngle` (angle), `DisplayColor` (colour), `Experience*`/`SkillPointValue` (4 integers),
  booleans (`Yes`/`No` case-insensitive), `CommandSet`, `BuildVariations` (references), `Locomotor`
  (`TaggedReferenceList`: set name + locomotor references, exactly one list per set name). An **unknown object key is
  an error**; audio/portrait/cameo keys are known, but `Opaque`, and are listed with their frequency in the
  compatibility list (`deferred-field`, 37 keys, 6,021 uses). `ObjectReskin` may set only the keys of the
  reskin table.
- **Modules:** every implemented module type has a strict field schema (key, value kind, required, origin); the compiler
  checks the block kind (`Behavior`/`Body`/`Draw`/`ClientUpdate`) against the catalog, unknown and duplicate keys, wrong
  value kinds and required fields.
- **Remaining definition domains** stay flat (Phase 5), see "Known limits".

## Load order (block 0.4)

`OriginalLoadPlan` maps the retail load order of `GameEngine.cpp` (`initSubsystem`: default file, main file,
directory), `GameAudio.cpp`, `GameLOD.cpp`, `Image.cpp` and `GameLogic.cpp`. Directories first load the files
directly in the directory, then the subdirectories, each sorted (`INI::loadDirectory`). Every INI file is classified
(`Retail`, `MapOverlay`, `DebugOnly`, `ToolOnly`, `Unreferenced`, `LanguageVariant`, `Unclassified`); excluded files are listed
with a reason in the compatibility list (28 files: `GameDataDebug.ini`, `CommandMapDebug.ini`, `CommandMapDemo.ini`, `Scripts.ini`
(WorldBuilder only), `MemoryPools.ini`, two unreferenced default files, `Options.ini` of the installer and 20 language variants
(16 of other languages, 4 Windows 9x)). The language is selectable (`English`). A strict run excludes unclassified files
and reports them.

`IniTool load-order-report` lists all definitions with more than one occurrence (478, of which 431 across several files) with
file order and winner. **Result:** 18 winners change against the Phase 3 order – 16 `MappedImage` names
(`HandCreated` loads after `TextureSize_512`, `Image.cpp`; Phase 3 sorted the other way round) and 2 upgrades between map overlays
(`MD_USA03_END`/`MD_USA03_END1`, in the aggregate without a counterpart in the original). For no pair does the relative file order depend
on an assumption that is not verified (report: 0 "ORDER-NOT-SOURCED"); the GameClient files not ordered by source code
define no common names. Without map overlays there is no duplicate object name: duplicate `Object` names occur only in
map `map.ini` files (`INI_LOAD_CREATE_OVERRIDES`); in the base data multiple definitions concern the pairs default/main file
(GameData, AIData, Multiplayer), the `MappedImage` pairs and repetitions within one file (Terrain, FXList, Upgrade,
ObjectCreationList).

Map overlays: `MapOverlayMode.Include` applies all 27 maps after the base data in path order (full inventory,
aggregate); `Exclude` yields the retail base data. At runtime exactly one overlay per map applies
(`GameLogic::startNewGame`); the simulation uses the base content.

**Compat IDs** now carry the declaration type (`compatibility://<type>/<name>`), because definitions of the same name
of different types (six in the content, for instance `MappedImage`/`EvaEvent`) previously merged into one.

## Object inheritance (block 0.4)

Like `ThingFactory::newTemplate`, every new `Object` copies the `DefaultThingTemplate` (fields, modules, WeaponSet/ArmorSet).
After that `ThingTemplate::parseModuleName`/`ModuleInfo::clearCopiedFromDefaultEntries` applies with the **interface masks** from the
module catalog: an explicit module removes all inherited default modules that share an interface with it (e.g.
`ActiveBody` replaces the `InactiveBody`, a die module the default `DestroyDie`); modules marked as `InheritableModule` stay (the
default `AutoHealBehavior` only if the object is trainable), those marked as `OverrideableByLikeKind`
(`StealthUpdate`) only for vehicles/infantry/score/portable structures and not for aircraft, buildings etc.
The evaluation is, as in the original, **order-dependent** (it reads `KindOf`/`IsTrainable` at the time of the
module declaration). The first explicit `WeaponSet`/`ArmorSet` replaces the inherited ones once; `UnitSpecificSounds/-FX` replace
existing ones. `ObjectReskin Child Parent` copies the parent, allows only appearance keys and replaces inherited draw modules.
`AddModule`/`RemoveModule`/`ReplaceModule`/`InheritableModule`/`OverrideableByLikeKind` apply their children with the fields of the
object – **unclosed `AddModule` blocks** (`MD_CHI03`, `GLAStatue`) too set fields and sections of the object instead of
creating a phantom module `ArmorSet`. `ReplaceModule` removes and appends the replacement **at the end**, requires the same
module type and a new tag; `RemoveModule` on a missing tag is counted as a diagnostic in the aggregate. Repeated module tags
in the same object are both kept (retail; only the debug build aborts) and are counted as
`compatibility-duplicate-module-tag` (18 cases in the base data). The syntax `Object Child : Parent` does not exist in the
original; it is kept as an extension for tests and is counted (0 in the content).

## Repeated keys (block 0.4)

In the content keys occur several times within one head (`Armor = …` 860 repetitions, `Locomotor` 343, `ScatterTarget` 51,
`MapCache supplyPosition` 1,472 among others). Phase 3 overwrote them silently (`RemoveAll`), whereby for instance all `Armor` coefficients
except the last were lost. Now a justified table applies (`RepeatableKeys`, per entry with the parser function
of the original): **Accumulate** (every occurrence stays, order preserved) for `ScatterTarget`, `StandardPublicBone`,
`supplyPosition`, `techPosition`, `Animation Image`, `Credits Text/Style`, `CrateObject`, `TransitionToFX/OCL`;
**indexed** (an occurrence replaces the one with the same index in place, otherwise appended) for `Armor` (damage type),
`MajorFX`/`MinorFX` (damage type), `VeterancyMajorFX/MinorFX`, `WeaponBonus` (condition + field) and `Object Locomotor`
(set name). All other repeated keys are scalars: the last wins, every repetition in the same block is
counted (`duplicate-key`, 39 keys, 171 uses in the full inventory).

## Module registration (block 0.2)

`ModuleDescriptor` contains name, block kind, interface mask (from the reference headers, `getInterfaceMask()`), owner kinds,
field schema, `CompatibilityOnly` and target phase and **no factory**. The catalog `ModuleCatalog` lists all 205 module types of the
content: 12 implemented (see `SIMULATION-CORE.md`), 137 simulation modules for Phase 5, 34 for Phase 6, 22
presentation modules (Draw/ClientUpdate) for Phase 7. The binding to implementations lies in `CnCNew.Simulation`.
`StrictUnknownModules` fails on every name that is not in the catalog; a typo in the name is thereby distinguishable from a
known, not yet implemented module.

## Compatibility baseline (block 0.1)

`baselines/compatibility.json` (344 entries: category, name, owner kinds, frequency, target phase; no asset contents) records
the checked list: definition types without a contract, unimplemented modules, deferred object keys,
duplicate keys, diagnostic codes and excluded files. `IniTool compat-check <dir> <baseline>` ends with exit code 1 on every
new, missing entry or one changed in frequency or target; the real-data test of the data tests checks
the same. Regenerate: `IniTool compat-baseline`. Changes to the file are subject to review.

## References and snapshots

IDs have the form `<kind>://<ordinal-lowercase-name>`. The graph separates source kind and target kind; a target of the same name
of the wrong type is not resolved. `NONE` is a typed sentinel. CommandSet slots, TriggeredBy/ConflictsWith lists,
`Locomotor` sets and the known Object, Weapon, Armor, Science, Upgrade, FX, Audio, Locomotor and
SpecialPower fields produce edges – also recursively from definition and module sections. The engine-internal names
`Upgrade_Veterancy_ELITE`/`_HEROIC` have no INI definition and carry the separate fallback
`compatibility-engine-veterancy-upgrade`.

`DefinitionSnapshotWriter` writes schema 2 as canonical UTF-8 JSON (schema 1 had no module block kind and no
new value kinds); `ComputeFingerprint` streams the same snapshot into SHA-256 (replay fingerprint). Definitions, fields,
edges and compatibility are sorted ordinally; modules and repeated keys keep their order. Absolute paths,
timestamps and non-deterministic dictionary orders are absent.

## Reproducibility and numbers

```powershell
dotnet run --project tools/CnCNew.IniTool -- compile game/content .local/phase4-a.json
dotnet run --project tools/CnCNew.IniTool -- compile game/content .local/phase4-b.json
Get-FileHash -Algorithm SHA256 .local/phase4-a.json,.local/phase4-b.json
```

Reference values (28 September 2026, full inventory with all map overlays, two independent runs byte-identical):

| | Phase 3 (end) | after block 0 |
|---|---:|---:|
| loaded INI files | 256 | 228 (28 excluded) |
| syntax blocks | 66,623 | 65,873 |
| definitions | 12,677 | 12,573 (typed unchanged 9,876; compat 2,801 → 2,697) |
| module entries / keys | 16,360 / 335,592 | 16,360 / 333,061 |
| references resolved | 12,441 of 12,468 | 13,134 of 14,679 |
| visible fallbacks | 27 | 1,545 (1,541 veterancy pseudo-upgrades, of which 1,518 from the inherited default `AutoHealBehavior`; 4 real gaps) |
| module catalog | 206 observed names (with phantom) | 205 (12 implemented) |
| snapshot | 112,664,082 bytes | 142,302,102 bytes |
| SHA-256 | `14154C51…C675D4` | `131ED730F2AD4DB85D6F50CE91A7059358CA32D6728F8EB6E91E6CF8775E58A4` |

Retail base data without map overlays (`--maps exclude`): 146 files, 63,745 blocks, 12,548 definitions (2,113 objects),
16,045 modules, 327,293 keys, 12,902 of 14,420 references resolved, snapshot 141,720,050 bytes, SHA-256
`9B49E9DD1EE403C6797B32AD66F63CF3C0E538CEB510A84CEFF4AD5257FCCD38`.

Explanation of the deviations of the hash: (1) 28 files excluded (debug, tool, other languages); (2) compat IDs with a
type prefix; (3) new value kinds (colour, colour keyframe, vector, enum, flags, integer lists, bound locomotor lists);
(4) repeated keys keep their multiplicity (the 53 `Armor` definitions keep 884 coefficient lines instead of one value per damage-type override); (5) `DefaultThingTemplate` is copied into every
object (more fields and modules, +1,518 inherited pseudo-upgrade references); (6) module block kind in the snapshot; (7) compatibility
categories extended. Value distribution without the inherited default copies: of 333,053 typed values 128,006 are `Text` (before
135,215 of 335,592) and 74,128 `List` (before 89,511); newly typed are 388 colours, 8,696 colour keyframes, 6,316 vectors,
5,134 flags, 5,044 enums and 908 integer lists. All values including inheritance: 412,756 (159,030 `Text`, 75,138 `List`).

## Known limits (after block 0 of Phase 4)

- The definition types other than Object (`WeaponDefinition`, …) still have no domain-specific properties, required fields
  or key schemas; their 30,000 head values are to a large part still typed heuristically. Debt for Phase 5, with
  numbers from `IniTool debt-report`: Armor 884 head fields (all `List`), Weapon 6,248 (2,449 `Text`/`List`), Locomotor 3,599
  (552), AudioEvent 8,442 (5,006), Fx 58,459 (27,623), CommandButton 5,240 (4,413), Upgrade 427 (263), Science 456 (254),
  SpecialPower 542 (193), Faction 409 (333), GameData 323 (85).
- The unit typing of these domains still happens heuristically via substrings of the key name; the conversion into
  ticks and distance per tick is done by the simulation only for the fields of the implemented modules.
- Two compat groups are deliberately coarse: definitions without a contract (compatibility list, 38 types) and `Opaque` object keys.
- The order of the GameClient subsystems among each other is not verified from the source code; the report proves that
  no change of winner follows from it.
- An incremental or cached compilation does not exist.

*(The debt listed above for Armor, Weapon, Upgrade, Science and Locomotor has since been paid off – see below.)*

## Block 0.1 of Phase 5: domain schemas for Weapon, Armor, Upgrade, Science, Locomotor

`DomainSchemas.For(DefinitionKind)` (`src/CnCNew.Data/Compilation/DomainSchemas.cs`) hangs on `DefinitionCompiler.ApplyProperty`
between the Object schema check and the generic fallback: per definition head one `SchemaSet` with the mechanically
relevant fields; keys that are not mechanically needed but real stay `Opaque` (counted as `deferred-field`,
category `phase-5-domain-field-typing`, never silently discarded).

- **`WeaponSchema`** (`Weapon.cpp TheWeaponTemplateFieldParseTable`): 15 mechanically relevant fields typed
  (`PrimaryDamage`, `PrimaryDamageRadius`, `SecondaryDamage`, `SecondaryDamageRadius`, `AttackRange`,
  `MinimumAttackRange`, `DamageType`, `DeathType`, `WeaponSpeed`, `MinWeaponSpeed`, `ClipSize`, `ClipReloadTime`,
  `DelayBetweenShots`, `AutoReloadsClip`); ~60 remaining keys stay deferred. New value kinds:
  **`ShotDelayValue`** (`FieldType.ShotDelayMs`) for `DelayBetweenShots` – accepts both the simple form (also with
  decimal places, `WeaponTemplate::parseShotDelay` reads real, not only integer) and the four `Min:x Max:y` cases
  occurring in the content (e.g. `ScudStormMissile`).
- **`ArmorSchema`**: one field, `Armor = <Default|damage type> <percent>`, repeatable and indexed by the first token
  (`RepeatPolicy.IndexedByFirstToken` – without this specification every repetition would have been wrongly counted as
  `duplicate-key`). New value kind **`CoefficientValue`** (`FieldType.CoefficientByEnum`, parameterised over `InnerType`, so that
  the same mechanism can later also carry `DamageFX` amounts).
- **`UpgradeSchema`/`ScienceSchema`/`LocomotorSchema`**: analogous, fields and sources commented in the catalog.
- **Important side correction:** the generic reference recogniser (`DefinitionValueParser.TryReferenceKind`) knew the
  field `ArmorSet` (→ `DefinitionKind.Armor`), but **not** the field `Armor` of the same name inside an
  `ArmorSet` block (`ArmorSet { Armor = <Name> End }`) – an error unnoticed until then, because Phase 4 never evaluated `ArmorSet`
  contents. Without the addition `"armor" => DefinitionKind.Armor` the value stayed unresolved text, and
  `ObjectTemplate.ResolveArmor` fell back for every object to the all-coefficients-1.0 default (discovered by the
  new Phase 5 combat integration test, not by the real-data test – the 100 % coefficients of `NoArmor` hid
  the error by chance). After the correction 2,260 additional references resolve (all successfully,
  13,207/14,753 → 15,467/17,013 in the full inventory).
- **Numbers** (`IniTool debt-report`, full inventory): Armor 884 head fields, **0 unnamed**; Weapon 6,248 head fields,
  1,738 unnamed (before 2,449); Upgrade 427/228 (before 263); Science 456/168 (before 254); Locomotor 3,599/552
  (unchanged – the 552 are deliberately deferred, see the `LocomotorSchema` comment).

## Block 0.1 continued: `FactionSchema` (players/factions)

`PlayerTemplate.cpp TheFieldParseTable` (~35 keys) supplies twelve mechanically relevant fields for the headless
reference run: `Side`, `PlayableSide`, `IsObserver`, `StartMoney` (`PlayerTemplate::parseStartMoney`: a simple
money amount, `Money::setStartingCash`), `IntrinsicSciences` (reference list to `Science`),
`IntrinsicSciencePurchasePoints`, `StartingBuilding` as well as `StartingUnit0`–`StartingUnit9` (one `Object` reference each).
The remaining ~23 keys (command bar assignment, screen/audio resources, tooltips, medallions) stay
deferred – pure presentation. Numbers: 409 head fields over 15 factions, 294 unnamed (before 333; 41 additional
references all resolve).

`CnCNew.Simulation.Templates.FactionCatalog` reads `StartingBuilding`/`StartingUnit0-9` deliberately as **names**, not as
resolved `ObjectTemplate` indices: spawning runs via `SpawnCommand` anyway, which takes a name and validates it on
enqueueing, so that the faction catalog does not have to be built in the same order as the object template list.
`SimulationWorld` writes `StartMoney`/`IntrinsicSciences` once at construction into the
player state (`Player::Player()`), provided `PlayerConfiguration.Faction` is set.

## Block 0.1 continued: `ObjectCreationList`, `CrateData` and `GameData/Gravity`

- **`ObjectCreationList`** is a definition kind of its own (`ObjectCreationListDefinition.Sections`, 295 definitions in the
  full inventory, 294 in the retail content). `ObjectCreationListSchema` types the six nugget kinds strictly after
  `ObjectCreationList.cpp` (`CreateObject`, `CreateDebris`, `ApplyRandomForce`, `FireWeapon`, `Attack`) as well as
  `DeliverPayload` (`DeliverPayloadAIUpdate.cpp`, `RadiusDecal.cpp`); unknown keys and nugget kinds are errors.
  Module fields with list references (`OCL`, `DamagedOCL`, `RubbleOCL`, …) are now references of the kind `ObjectCreationList` and are
  resolved in the full inventory. New value kinds: `RetailAlternatingNames` (keeps the 1st, 3rd, … names, see below) and
  `CountedReference` (`<Name> [count]`, `DeliverPayloadNugget::parsePayload`).
- **Retail peculiarity as a counted compatibility case:** `ObjectNames` (`CreateObject`, references to `Object`) and
  `ModelNames` (`CreateDebris`) follow `parseDebrisObjectNames`: the retail loop stores one token and reads the
  next without storing it; only the 1st, 3rd, … token count (value kind `RetailAlternatingNames`). The discarded
  tokens go via `FieldSchema.IgnoredTokenCount` into the compatibility content instead of vanishing silently.- **`CrateData`** (`CrateSystem.cpp`): 9 definitions as definition kind `Crate`. `CreationChance`, `VeterancyLevel`,
  `KilledByType`, `KillerScience` and `OwnedByMaker` are typed; `CrateObject <Name> <weight>` stays as
  `deferred-field` (11 occurrences, target `phase-5-domain-field-typing`), because the name-weight pair is read only when building the
  simulation catalog (`CrateCatalog`), with an error message on malformed input. `CreateCrateDie.CrateData` is a
  reference list field to `Crate`.
- **`GameData/Gravity`** is typed and removed from the compatibility baseline; the simulation needs it for
  `Thing::isSignificantlyAboveTerrain` (`docs/GAME-RULES.md` §6b).
- The compatibility baseline then has **784 entries** (before 787).

## Block 0.1 completed: `SpecialPower`

`SpecialPowerSchema` (`SpecialPower.cpp SpecialPowerTemplate::m_specialPowerFieldParse`) types the definition kind `SpecialPower`
strictly: `ReloadTime` (duration), `RequiredScience` (reference to `Science`, resolved in the full inventory), `PublicTimer`, `Enum`
(name table `SpecialPowerType`, 67 entries including `EARLY_*`, `AIRF_*` variants), `DetectionTime`, `SharedSyncedTimer`,
`ViewObjectDuration`, `ViewObjectRange`, `RadiusCursorRadius`, `ShortcutPower`. `InitiateSound`, `InitiateAtLocationSound` and
`AcademyClassify` stay as `deferred-field` (presentation/statistics, 79 uses). The ten special power modules
(`SpecialAbility`, `OCLSpecialPower`, `CashBountyPower`, `CashHackSpecialPower`, `SpecialPowerCreate`, `GrantUpgradeCreate`,
`GrantScienceUpgrade`, `UnpauseSpecialPowerUpgrade`, `SpecialPowerCompletionDie`, `SpectreGunshipDeploymentUpdate`) have catalog schemas
and bindings (`docs/GAME-RULES.md` §6d); `SpecialAbility/InitiateSound` (85 uses) is likewise a counted compatibility case.
The reference resolution thereby rises in the full inventory to 18,050/19,600, the compatibility baseline drops to **761 entries**, the
definition snapshot (schema 2) has 144,745,050 bytes (SHA-256 `D0C06A2B06CD7065474A3EB729E36B87E1815DAE2D461E564D023EAA019295CF`).
## Completion of Phase 5: further schemas and numbers

After the special power subsystem the following fields and modules have been typed strictly (each with a regeneration of the
compatibility baseline; details of the semantics in `docs/GAME-RULES.md`):

- **Supply** (§6e): `SupplyCenterDockUpdate`, `SupplyWarehouseDockUpdate`, `SupplyTruckAIUpdate`, `WorkerAIUpdate`,
  `DozerAIUpdate`, `AutoDepositUpdate` as catalog schemas as well as `GameData/ValuePerSupplyBox` (`Player::getSupplyBoxValue`).
- **`WeaponBonus`** (Weapon and GameData) is a strict entry `<condition> <field> <percent>` on the name tables of the
  reference (`WeaponBonusConditionType`, `WeaponBonusField`); the `HealthBonus_*` fields of `GameData` are typed percent values (§6h).
- **Repair** (§6f): `RepairDockUpdate` and `AutoFindHealingUpdate`.
- **Combat** (§6g): `FireWeaponUpdate` and `ReplaceObjectUpgrade` as catalog schemas; `CrushDie` as a die module with the common
  death filter (`DieMuxData`) without fields of its own.

Re-measured (`IniTool compile game/content`, full inventory with all map overlays): 228 loaded INI files (28 excluded by the
load plan), 65,875 blocks, 12,574 definitions, 16,380 modules, 333,061 keys, 18,138/19,688 references resolved
(1,550 visible fallbacks); compatibility: 518 `deferred-field` entries (20,606 uses), 36 `definition` (2,590), 9
`diagnostic` (4,617), 24 `duplicate-key` (120), 28 `excluded-file` (28), 131 `module` (4,151); the baseline has **746 entries**.
Canonical definition snapshot (schema 2): 144,813,963 bytes, SHA-256 `D98CE0154ADF721DC58E9FF1FE769ADA646A058CE7B9AA9A8F0AC99254E5AB67`;
two independent runs are byte-identical. Retail base data (`--maps exclude`): 146 loaded INI files (110 excluded), 63,747
blocks, 12,550 definitions (2,115 objects), 16,065 modules, 327,293 keys, 17,748/19,271 references resolved (1,523 fallbacks),
144,203,312 bytes, SHA-256 `9F5307957A55CD4683E8A79CBEB079320BE69400802DA38D2FFDDB200713E264` (fingerprint `9F5307957A55CD46`). The earlier
numbers above (`761 entries`, `144,745,050 bytes`) are superseded.

## Phase 6: typing of locomotor `Surfaces`, `SellPercentage` and map model

- **`Locomotor/Surfaces`** is a flags field on the name table `LocomotorSurface` (`NameTables.Phase5.cs`: `GROUND`, `WATER`, `CLIFF`, `AIR`, `RUBBLE`) instead of a compatibility value;
  the navigation (`SIMULATION-CORE.md` §14) reads it as `LocomotorSurfaces`.
- **`GameData/SellPercentage`** is a typed percent value (`GameDataSchema`), the basis of the sale proceeds (`SIMULATION-CORE.md` §16).
- **Power plant modules:** catalog schemas for `PowerPlantUpdate` (`RodsExtendTime`), `PowerPlantUpgrade` (upgrade mux) and `OverchargeBehavior`
  (`HealthPercentToDrainPerSecond`, `NotAllowedWhenHealthBelowPercent`).
- **Map model:** `CnCNew.Data.World.MapData` (`MapModel.cs`) is an immutable contract type without reference to renderer, archive or file system; it is not a definition domain and
  does not appear in the definition snapshot (`SIMULATION-CORE.md` §13).

Re-measured on 30 September 2026 (`IniTool compile game/content`, full inventory): 228 loaded INI files (28 excluded), 65,875 blocks, 12,574 definitions, 16,380 modules,
333,061 keys, 18,138/19,688 references resolved (1,550 visible fallbacks); compatibility: 516 `deferred-field` entries (20,401 uses), 36 `definition` (2,590),
9 `diagnostic` (4,599), 24 `duplicate-key` (120), 28 `excluded-file` (28), 128 `module` (4,133); the baseline has **741 entries** (`compat-check`: all match).
Definition snapshot (schema 2): 144,832,736 bytes, SHA-256 `AEB5108DDA27ABAC7445452AA2743B794AD0BF1102F2687F8D7D8F7B87D2ADB7`, two runs byte-identical.
Retail base data (`--maps exclude`): 12,550 definitions (2,115 objects), snapshot 144,221,861 bytes, SHA-256 `1248585D0BAAB79F5202FDB81F26A2B00962A9D393B0CE1FF93C5FFDF6A5C6E7`.
`IniTool audit`: 256 of 256 INI files, 66,625 blocks. Earlier numbers (`746 entries`, `761 entries`, `144,813,963 bytes`) are superseded.
