# Implementation plan up to the completed first scope

> **Superseded as the plan on 10 October 2026** by `docs/plan/PROGRAMME.md` (four stages: port, engine, changes,
> multiplayer). The steps 1 to 11 below map onto the milestones of `docs/plan/STAGE-1-PORT.md` (steps 1 to 9),
> `docs/plan/STAGE-2-ENGINE.md` and `docs/plan/STAGE-4-MULTIPLAYER.md` (step 10) and the release gates (step 11). This
> document is kept as the record of the scope, acceptance statements and known limits of every step at the time it was
> measured; the fidelity debt register in `docs/plan/STAGE-1-PORT.md` §3 carries the known limits forward.

As of: 30 September 2026 (Phase 6 completed)

## Goal and delimitation

This plan takes the current prototype to a locally playable reimplementation of
*Generals: Zero Hour* for single-player skirmish and campaign and – since the scope change of
5 October 2026 – for multiplayer skirmishes through dedicated servers. Networked campaign, matchmaking,
NAT traversal, GameSpy compatibility, anti-cheat as well as binary compatibility with old mods and
savegames stay outside this first completion.

**Scope change of 5 October 2026** (justified and versioned according to "Order and
change rule"): the project owner takes multiplayer skirmishes with up to eight players through
dedicated servers into the scope, raises the simulation to 60 Hz and demands that the tick rate be a
parameter instead of a constant; the multiplayer module also carries the local skirmish against the AI.
Justification: the goal is a modern game with direct command intake and network play that does not depend on the
slowest fellow player. Consequence for this plan: a new step 10 (below) lies before the
previous step 10, which becomes step 11 – tick rate, fixed-point resolution and formats change every
reference value and every savegame and must be settled before a shippable completion. Frame
and decisions: `docs/MULTIPLAYER-PLAN.md`.

Every step forms a self-contained domain. A step counts as completed only when
its public data contracts are settled, the real installation is fully audited,
automated regression tests pass and the associated documentation describes the actual
state. New domains may build on completed contracts, but may not take over known
defects from the previous step.

## Reconstructed starting state

Already present and functional are:

- reproducible local import of the original installation, BIG reader and materialised,
  archive-free content with an origin manifest;
- validating SAGE INI syntax parser;
- W3D chunk, mesh and material decoder with full-content audit;
- TGA decoder with full-content audit;
- a Stride/Direct3D 11 viewer that renders a static W3D model with original textures and a
  part of the material states;
- a currently successful solution build and five successful test programs.

The domain worked on last is clearly W3D material fidelity and its mapping onto Stride.
`CnCNew.W3D.Tests` currently stops in a newly added test for per-face UVs of the second
texture stage. The selection and the V flip are implemented; the assertion, however, compares
binary `float` results exactly. Behind it remain the already documented functional gaps:
simultaneous material passes and texture stages, animated mappers as well as rare gradient modes.

## 1. Completing the W3D material and rendering domain

**Status: completed on 27 September 2026.**

**Result:** static W3D meshes are described correctly, independently of the renderer, with all
material variants occurring in the shipped content and are displayed in the Stride client.

Scope:

- replace the faulty exact float comparison by a tolerance check appropriate to the subject
  and make all W3D tests green again;
- model semantics and order of several material passes and texture stages completely;
- map primary, secondary, detail and post-detail functions onto reproducible shader paths,
  including the rare gradient modes;
- type mapper arguments and execute UV animations time-dependently;
- secure texture resolution, fallbacks, alpha test, blend, depth, culling, vertex colours and opacity
  visually and automatically over representative models;
- break the monolithic viewer path down into testable W3D-to-Stride components.

Completed when:

- build and all six existing test programs are successful;
- the W3D full-content audit runs without unknown material states;
- a real-asset test demonstrates several passes, two stages, per-face UVs and animated mappers;
- golden images for transparent, additive, two-stage and animated example materials
  are released;
- the smoke test ends without `NotSupportedException` for data from the shipped content.

Acceptance:

- Solution build and all six test programs are successful; the W3D test additionally runs against `.local/content`.
- The audit processes 8,904/8,904 files and 70,587 meshes without an unknown shader or mapper state.
- 5,625 two-pass meshes, 317 two-stage passes, both mapper slots and per-face UV indices are demonstrated on real data.
- The Stride path uses ordered material-pass sequences and dynamic vertex buffers for time-dependent mappers.
- Deterministic local PNG captures and SHA-256 baselines cover multi-pass/alpha/additive, two stages as well as linear/grid animation.
- The implemented semantics and mapper/bump cases deliberately lying outside the shipped content are documented in `docs/W3D-MATERIALS.md`.

## 2. Completing asset formats and the runtime content contract

**Status: completed on 27 September 2026.**

**Result:** all file types needed for maps, UI, localisation, models, animation, effects, audio and video
have a validating reader or a documented, reproducible
conversion. Runtime code accesses exclusively through a stable content contract.

Scope:

- inventory STR, MAP, WND, W3D animation/hierarchy, DDS/TGA, particle, audio and video formats
  and classify them by runtime decoder or import converter respectively;
- implement missing readers with size, range and reference validation;
- merge logical asset IDs, dependencies, overlay origin and hashes in a versioned
  runtime manifest;
- provide incremental, reproducible import as well as understandable diagnostics for defective or
  unknown assets;
- keep original data, generated artefacts and source code legally and technically separate.

Completed when the entire local content is inventoried without unknown needed formats
and can be built reproducibly from a fresh import and every runtime reference
is either resolvable or identified as a deliberately supported fallback.

Acceptance:

- `CnCNew.Formats` validates STR, MAP/SCB/WAK, WND, DDS, ANI, WAV, MP3 and BIK; existing
  INI, TGA and W3D readers are integrated into the same full-content audit.
- 25,682/25,682 files are classified; no extension stays unknown. The productive
  content numbers and 119 out-of-scope installation files are in `docs/CONTENT-PIPELINE.md`.
- Schema 2 contains canonical IDs and paths, type, hash, size, overlay candidates/priority,
  importer version/options, runtime artefacts, direct dependencies and diagnostics.
- 41,308 of 41,527 reference uses are resolved; 219 uses are visible as one of
  three checked fallback types. Real cycles and untyped missing targets are errors.
- Two independent fresh imports have the identical manifest SHA-256
  `ACDF0B6CF98538469AA05F62E23D2A83BD3E17A911433B4EA73373FFFB1245FF`; the follow-up run reaches
  25,682/25,682 cache hits.
- Staging and directory swap prevent partial publications. All artefacts were afterwards
  checked against size and SHA-256 without BIG access.

## 3. Typed game data and module registration

**Status: completed on 27 September 2026.**

**Result:** the INI syntax is compiled into immutable, validated game definitions;
strings and loose nodes no longer reach the simulation.

Scope:

- reproduce includes, inheritance, overrides, conditions and name resolution true to the original;
- introduce types for Object, Weapon, Armor, Damage, Upgrade, Science, Player, Faction, Command,
  FX and Audio definitions;
- bind module and behavior names to implementations through explicit registries;
- validate units, measures, enums, references and mandatory fields when loading;
- generate understandable source diagnostics as well as snapshots of the compiled definition data.

Completed when the complete shipped INI content compiles into a deterministic
definition graph, all references are resolved and unknown keys or modules are recorded in
a checked compatibility list.

Acceptance:

- 256/256 files and unchanged 66,623 syntax blocks yield 12,677 definitions, 16,360
  module instances and 335,592 typed keys including recursive configuration sections.
- Own immutable contract types cover Objects, Weapons/WeaponSets, Armor/Damage,
  Upgrades/Sciences, Player/Factions, Commands, FX, Audio, Locomotors, Experience,
  SpecialPowers and GlobalData; parser nodes stay internal to the compiler.
- Includes are resolved exclusively through virtual runtime paths. Cycles, missing bases,
  wrong conditions, invalid values, registry duplicates and unknown modules in strict mode
  are errors. Overlay and Add/Remove/ReplaceModule are deterministic.
- 12,441/12,468 typed reference uses are resolved. The 27 remaining uses
  (25 veterancy pseudo-upgrades, `DieByFireUSA` and `Boss_VehicleSentryDrone`) are explicit,
  not silent compatibility cases.
- 41 later definition domains (2,801 instances) and 206 not yet implemented module types
  (16,360 uses) are inventoried with frequency, owner kind and implementation target.
- Two independent 112,664,082-byte snapshots were byte-identical; SHA-256 in each case
  `14154C512DED47C4AD72B651F8FCB0BA702EACBF8678C51DDAB7D125BEC675D4`. Contract, rules,
  compatibility list and commands are in `TYPED-DEFINITIONS.md` and
  `PHASE3-COMPATIBILITY.md`.

Verification (28 September 2026): build, all eight test programs, the audit tools and a
fresh full compilation (identical snapshot SHA-256) confirm the numbers above. The
acceptance applies to structure, completeness and determinism. Shallow in subject matter remain: no
domain-specific definition properties, mandatory fields or key schemas for
definitions; heuristic unit typing without tick conversion; `Enum`/`Flags`/`Color`/
`Vector` are never generated in the content (9,150 colours, 6,161 coordinates and 1,964 `KindOf` lists
are `List`); the productive `ModuleRegistry` is empty and the compatibility list not secured as a
baseline; load order not compared against `GameEngine.cpp`; no
incremental compilation. Details in `TYPED-DEFINITIONS.md` ("Known limits"). The
necessary part was made up in Phase 4 as block 0 (see below and `TYPED-DEFINITIONS.md`), the rest is advance work
for Phase 5.

## 4. Deterministic simulation core

**Status: completed on 28 September 2026 (including block 0, hardening of Phase 3).**

**Result:** a world running headless has a fixed tick, deterministic IDs and
random numbers, command queue, object life cycle and reproducible snapshots.

Scope:

- create `CnCNew.Simulation` without dependency on Stride, DirectX or UI;
- firmly define time, RNG streams, entity IDs, update order and numeric rules;
- implement object, module, behavior, event, timer and command life cycles;
- decouple input commands from presentation and real time;
- create a snapshot, hash and replay harness for deterministic regressions.

Completed when identical definitions, map, seed and command sequence deliver
bit-identical state hashes over long runs and memory validation as well as headless stress test pass.

Acceptance block 0 (hardening of Phase 3):

- Retail load plan of the original engine (order, exclusion of debug/tool/foreign-language files); `IniTool load-order-report`
  shows 478 multiply defined names with winners; 18 winners changed (16 `MappedImage`, 2 map overlays), for
  none does the order depend on an unverified assumption.
- `DefaultThingTemplate` inheritance, `Add/Remove/ReplaceModule`, `ObjectReskin` and unclosed `AddModule` blocks as in the
  original; repeatable keys keep multiplicity (table with parser source).
- Object headers and module fields strictly typed (96 keys, 43 of them deliberately `Opaque` and counted); colours, vectors, enums and
  flags through name tables from the reference; no token falls back to `Text`.
- Module catalogue with 205 types without factories (the earlier number 206 contained a phantom module); binding table in the simulation,
  checked against the descriptors at start; `StrictUnknownModules` passes on the content; `compat-check` passes against the
  checked-in baseline (344 entries); new reference hash `131ED730F2AD4DB85D6F50CE91A7059358CA32D6728F8EB6E91E6CF8775E58A4`
  (two runs byte-identical, explanation of the deviation in `TYPED-DEFINITIONS.md`).

Acceptance Phase 4:

- Solution build and all nine test programs are green (warnings: only the ten known NU1901/NU1903); the simulation tests
  additionally run with `--content-dir` and `--long`.
- 30 Hz tick, exact time conversion, Q47.16 fixed point with documented rounding and limit values; the binary32 deviation of the
  original is measured (6,310 of 5,000,001 durations, at the earliest from 66 minutes on) and documented.
- Original RNG with `logic`/`cosmetic`/`audio` streams, golden sequences from an independent transcription (no compiler
  available), state after one million draws, carry chains.
- Monotonic IDs (0 invalid), canonical orders, sleepy-update model with documented deviations, eight tick phases,
  mutation queues, defined event delivery before the removal.
- Twelve life-cycle modules with strict schemas; selection supported by numbers (28.1 % of the 19,529 module uses of the
  base objects); module coverage audit: 26 of 2,113 objects fully instantiable, blocker list per module.
- Versioned command types, rejection reasons (`Late`, `Duplicate`, `OutOfOrder`, `UnknownPlayer`, `NotPermitted`,
  `InvalidArguments`, `UnknownTemplate`, `UnknownEntity`, `UnknownTimer`) counted and logged as events.
- Canonical snapshots (SHA-256), incremental 64-bit state hash (check against the full recomputation), replay format with
  fingerprint, checkpoints and integrity hash; damaged, truncated files and files of a foreign version or foreign definitions are
  rejected.
- Stress test over 216,000 ticks: up to 15,774 simultaneous entities, 401,229 spawn/destroy operations, 914,771 commands,
  7,099 timers; three fresh runs and the snapshot continuation are identical over all ticks; all tables return to zero;
  22 s per run, 532 MiB peak; budgets and hardware in `SIMULATION-CORE.md`.

Known limits of the phase: no movement/weapon/armour/production rule set (Phase 5/6); update phases of the original not
modelled; random consumptions of the original outside the lifetime modules are missing; RNG comparison with the compiled original is
outstanding; `Restore` is internal; definition domains other than Object stay shallowly typed (numbers in `TYPED-DEFINITIONS.md`); 26 of 2,113
objects are instantiable because modules inherited from the default (`StealthUpdate`, `AutoHealBehavior`) are still missing.

## 5. Game rules, combat, economy and production

**Status: completed (as of 29 September 2026) with documented limits (see "Known limits"); follow-up brief in `docs/handoffs/HANDOFF-PHASE7.md`.**

**Result (goal):** the complete core loop of an RTS match works headless:
collecting resources, building, producing, moving, attacking, applying damage and winning.

Scope:

- players, teams, diplomacy, factions, command sets and tech prerequisites;
- resources, supply, building sites, construction, production, repair and selling;
- weapons, projectiles, target selection, armor, damage, status, experience, upgrades and sciences;
- energy, special abilities, transport, garrison, stealth and relevant module interactions;
- victory/defeat conditions and rule events.

Completed when a headless reference match of all factions runs from start to victory without
special paths and central combat and economy processes are secured by golden-master scenarios
against known original values.

**Implemented** (details, numerics and deliberate simplifications in `docs/GAME-RULES.md`; numbers freshly measured on 29 September 2026):

- Block 0.1: domain schemas for Weapon, Armor, Upgrade, Science, Locomotor and Faction (`docs/TYPED-DEFINITIONS.md`); as a side finding an
  INI parser defect fixed (`End` detection only on the first token, like the original).
- Block 0.2: `StealthUpdate` and `AutoHealBehavior` with real fields and upgrade activation. Block 0.3 (fear signal `GameLogicRandomValue`)
  completed; **0.4 (RNG comparison with the compiled original) stays open** (no compiler available, the value is not guessed).
- Economy (credits, energy balance) and **supply economy** (`DockMachine`, `DockCommand`, depots, centres, supply carriers, workers,
  `SpawnBehavior`, `GAME-RULES.md` §6e), upgrade system (128-bit mask, `UpgradeMux`, `*Upgrade` modules), sciences (`ScienceCatalog`,
  `PurchaseScienceCommand`), direct combat with **area damage** (weapons, armour coefficients, magazine/reloading, §6g), **experience and
  veterancy levels** with health and weapon bonuses (`WeaponBonusTable`, §6h), **teams and diplomacy** (directed relationships,
  `SetRelationshipCommand`, bounty and victory rule on teams, §7), **repair dock and `AutoFindHealingUpdate`** (§6f), simple straight-line
  movement (no terrain, no pathfinding), a production queue, "last survivor" as victory, garrison/transport containment,
  creation lists, crates and the special-power subsystem (§6d).
- Command table with 19 types (`CommandTypes.Version = 7`), snapshot version 3, replay format v4.
- Reference match (`ReferenceMatchScenario`, `SimTool reference-match`) demonstrated deterministically on synthetic **and** retail data (winner,
  replay round, snapshot continuation): 13 factions with real command centres, **0 skipped start objects**, 707 deliveries with 93,075
  credits paid out, winner player 12 at tick 26,880, end hash `B6E0CDECA3168EFC` (`GAME-RULES.md` §7a). Golden masters with an independent
  model in the test cover damage chain, production clock, supply and experience (derived from the source code, not measured against original runs).
- 74 of 205 module types implemented (12 from Phase 4, 62 from Phase 5); **1,384 of 2,115 retail objects fully instantiable (65.4 %,
  before 26 of 2,113), 15,364 of 19,553 module uses covered (78.6 %)**; the 216,000-tick stress test runs deterministically with all modules
  (end hash `D80B367CEEF6D72D`), without a leak.

**Acceptance:** the acceptance criterion "headless reference match of all factions from start to victory" is fulfilled; "without special paths" and "against
known original values" only with the following restrictions.

**Known limits** (state at the end of Phase 5; construction process, selling, building-site check, rally point, terrain and pathfinding were made up for by Phase 6, see §6; complete list: `docs/GAME-RULES.md` §10):

- Block 0.4 open; golden masters are not original measurements, fixed point instead of `Real` is a documented deviation.
- No construction process: dozers and workers move and deliver, but do not build; bases in the reference match are start objects plus logged
  substitute infrastructure (`SpawnSupplyInfrastructure`). No selling, no building-site check, no rally point.
- No terrain, no collision, no pathfinding, no projectile flight time; `ToppleUpdate`, real `PhysicsBehavior` forces and `CrushDie` (only inert) follow
  with Phase 6.
- Effect of `CommandSet`/`CommandButton` and radar not implemented; `ChinookAIUpdate` is missing, which is why America has no
  supply economy in the reference match; no `UseSpecialPowerCommand` generator.
- Veteran-based `ArmorSet`/`WeaponSet` selection and science purchase points from experience are not modelled; `FireWeaponWhenDeadBehavior` without
  area damage.
- 731 of 2,115 objects are still not instantiable; 80 modules of the catalogue category "Phase 5" cannot sensibly be implemented without terrain/collision/AI
  (blocker list in `docs/SIMULATION-CORE.md` §11).


## 6. World, terrain, movement, vision and skirmish AI

**Result:** MAP data become a playable world with terrain, obstacles, navigation,
fog/shroud and a competitive skirmish AI.

Scope:

- load heightfield, texture layers, water, bridges, waypoints, trigger areas and props;
- model footprints, collision, ground types, formations and separate movement kinds;
- implement deterministic pathfinding, local avoidance and recomputation;
- simulate sight range, shroud, fog, stealth and reveal;
- port build plans, economy, attack, defence and ability use of the skirmish AI.

Completed when the selected vertical-slice map as well as a representative set of maps
are loaded completely, units navigate reliably and several long AI-versus-AI runs
end deterministically without deadlock or unauthorised information.

**Status: completed on 30 September 2026, with the restrictions named below.** All numbers freshly measured; details, numerics and limits in `docs/SIMULATION-CORE.md` §13–§20.

**Implemented:**

- **Maps:** `SageMapDecoder` (`CnCNew.Formats`, container formats CkMp/EAR/ZL5, blend-tile versions 6–8) and the contract model `MapData` (`CnCNew.Data.World`); `MapTool audit` decodes **150 of 150** retail maps
  without error (30,391,581 height points, 281,480 objects, 25,463 waypoints, 2,488 polygon triggers), builds the terrain for all of them (16,475,736 path cells, 258 start positions, largest extent 589 cells).
- **Terrain and navigation:** `TerrainMap` (cell classes cliff/water, height, start positions, fingerprint in snapshot v4 and replay v5), `NavigationGrid` (deterministic A*, footprint check,
  escape cells, `ObstacleSum` for replanning), turn-rate-limited movement with terrain height in `AIUpdateInterface`. Ground paths between start positions: 462 pairs, 456 complete, 6 partial, 0 blocked.
- **Vision:** `VisionMap` (visible/explored per player, vision hash in snapshot v5), sharing with allies, `REVEAL_TO_ALL`, construction sites, stealth; `FireWeaponCommand` requires vision (`TargetNotVisible`). Side finding: stealth defect
  fixed (`STEALTHED` only with `CAN_STEALTH`).
- **Construction:** construction sites as entities, six new commands (codes 20–25, `CommandTypes.Version = 8`, replay v6, snapshot v6), building-site check, selling (179 ticks, `SellPercentage`), free repair, rally points;
  `PowerPlantUpdate`/`PowerPlantUpgrade`/`OverchargeBehavior`; `CommandCatalog` (`CommandSet` → `CommandButton`).
- **Skirmish AI:** `SkirmishAi` (roles from modules, build planning, production, attack/defence, suppliers, repair, science), works only with visible information, memory and public start positions;
  `SkirmishMatchScenario`/`SimTool ai-match`.
- **Numbers:** 77 of 205 module types implemented (3 new), **1,393 of 2,115 retail objects instantiable (65.9 %)**, 15,382 of 19,553 module uses covered (78.7 %); compatibility baseline 741 entries;
  definition snapshot 144,832,736 bytes (`AEB5108D…D2ADB7`); stress test 216,000 ticks in 13.0 s, end hash `D1521974C861A4B9`; retail reference match winner at tick 26,880, end hash `5C370ED383EC9F6B`.

**Acceptance:**

- *Loading maps:* fulfilled for all 150 maps (decoding and terrain build). A single, designated vertical-slice map exists only with the presentation (step 7); the AI runs use four maps
  (Alpine Assault, Armored Fury, Bitter Winter, Death Valley).
- *Units navigate reliably:* fulfilled with a restriction – navigation is deterministic and without blockage on the checked start pairs, but simplified (point-shaped, no collision, 6 of 462 start pairs only partly walkable).
- *AI against AI, deterministic, without deadlock, without unauthorised information:* fulfilled. 16 runs (4 maps × 4 pairings, at most 45,000 ticks): **15 end with a winner, 1 in a stalemate** (Alpine Assault, America against China);
  all 16 are identical with second run and snapshot continuation. The test `SkirmishAiOnlyUsesWhatItSees` secures the information rule; a stalemate is not a deadlock (the simulation continues to run), but a named result.
  America never wins, because without `ChinookAIUpdate` no supply economy arises.

**Known limits:**

- *Not implemented from the scope:* bridges, formations, local avoidance and collision, texture layers (purely presentation), scripts of the map (`SidesList`/`PlayerScriptsList` stay unread), trigger areas only as water surfaces.
  The skirmish AI is a new development of its own, not a port of the original scripts.
- *Navigation:* flat A* with a budget of 200,000 expansions, approximated footprint, escape cells with a surcharge instead of real layers; a partial or failed path ends the command.
- *Vision:* circular, without height line of sight, every 3 ticks, no detectors (stealthed survivors can prolong a match).
- *Construction:* `QueueProduction` checks no prerequisite and no building capability, `MaxSimultaneousOfType` unchecked, repair free; no capture of power plants, no toggle command for `Overcharge`.
- *AI:* special powers unclassified, enemy start positions publicly known, only warehouses placed by the map, starting money (10,000) and science points from the system, no aircraft and airfields, army limited by
  instantiability, equally strong pairings can reach a stalemate.
- *Still open:* block 0.4 (RNG comparison, not guessed), projectile flight time, `ToppleUpdate`/`CrushDie` with real collision, `ChinookAIUpdate`/`TransportAIUpdate`, effect of `CommandSet`/radar,
  veteran `ArmorSet`/`WeaponSet`, `CommandButtonHuntUpdate`, `SpecialAbilityUpdate`; 722 objects stay non-instantiable (blockers in `docs/SIMULATION-CORE.md` §11).

## 7. Complete audiovisual world presentation

**Result:** the Windows client projects simulation snapshots into a readable 3D battlefield view
that functionally corresponds to the original.

Scope:

- display W3D hierarchies, skeletons, animations, condition states and turrets;
- render terrain, water, props, decals, particles, tracers, projectiles and FX;
- integrate camera, selection, command feedback, vision/fog, lighting and shadows;
- output audio events, music and speech playback through a free runtime path;
- play videos without a proprietary Miles/Bink SDK dependency;
- profile and stabilise rendering, streaming and resource lifetime.

Completed when a complete headless match can be visualised and controlled live,
all visible object states change correctly and defined performance, memory and
golden-image budgets are kept on target hardware.

**Status: completed on 30 September 2026, with the restrictions named below.** All numbers taken over from the
subject documents of the packages; details in `docs/CLIENT-TERRAIN.md`, `docs/CLIENT-OBJECTS.md`,
`docs/CLIENT-FX.md`, `docs/GAME-AUDIO.md`.

**Restriction (subsequently, 4 October 2026):** "completed" applies to the libraries and smoke tests. The first visual check by a human found gross rendering defects (missing walls, black surfaces, buildings in the fog without terrain), and `AudioDirector`/`AudioOutput` are never created in the client – the demos are silent. Corrections of the world: Phase 15 (`docs/DEMO-FINDINGS.md`, `docs/CLIENT-OBJECTS.md`); sound, cursor, waypoints, text: Phase 16.

**Implemented:**

- **World-layer seam:** `RenderServices`/`IWorldLayer`/`LiveMatchHost`/`StrideConvert` separate the Stride scene into
  independent layers that read exclusively simulation snapshots; `--match <map>` starts a controllable
  live skirmish, `--smoke-test --capture` generates a PNG headless.
- **Terrain:** `Layers/Terrain` renders heightfield, blend/3-way textures, water and fog of war (fog texture
  0/127/255) as well as the minimap (`MinimapImage.Compose`) directly from `TerrainRenderModel`/`FogOfWarGrid`; 11 of 150
  maps spot-checked, 6 of them verified with a screenshot (e.g. Alpine Assault: 239×259 cells,
  156 chunks, 26,630 blend cells, 370 diagonal deviations(4)); 0.266 ms/frame CPU mean (Fortress Avalanche,
  900 frames); screenshot SHA-256 of two independent runs identical (`CFF6C66C4708683A…`).
- **Objects:** `Layers/Objects` shares one Stride model+skeleton per rigid-part model and builds skinned parts
  (169 in the full content) per instance; team colour through ZHC hue-shift textures and HOUSECOLOR vertex colouring. Example
  Alpine Assault (America against China): 590 drawables, 10 drawn in the view frustum, 78 loaded models,
  2,080 triangles, 0.19–0.27 ms/frame, 0 additional allocations, SHA-256 identical over 300 and 1,500 frames.
  Example GLA against America (6,000 frames): 612 drawables, 16 drawn, 9 skinned infantrymen, 124 models,
  6,133 triangles, 0.38–0.60 ms/frame, 168 bytes/frame, identical hash in both runs.
- **FX:** `Layers/Fx` renders particles (camera/ground-aligned sprites, streaks), tracers and ground decals
  (`DefaultMaxQuads = 32768`, four materials); demo-ring test (400 frames): last frame 2,457 active particles,
  1,516 drawn, 9 streaks, 4 decals, 7 batches, 0.23 ms CPU (mean 0.50 ms), SHA-256 identical over two runs;
  real-data skirmish (12,000 frames) 0 discarded quads, 0 unknown batches, SHA-256 identical; bench with 10,000
  active particles: 1.735 ms per build (mean over 400 repetitions).
- **Audio (since Phase 16 wired in the client and measured, not heard – `docs/DEMO-FINDINGS.md` #7, `docs/GAME-AUDIO.md`):** `CnCNew.Game/Audio` (`AudioDirector`, `EvaCatalog`, `MusicPlaylist`) selects music, EVA announcements (among others
  `BuildingLost`, `UnitLost`, `BaseUnderAttack`, `UpgradeComplete`, `LowPower`, `InsufficientFunds`) and sound over
  five buses (Music/Sfx/Voice/Ambient/Ui) deterministically from snapshot events; `Audio/AudioOutput` feeds
  `Stride.Audio` `DynamicSoundSource` instances from the renderer-independent mixer (block size 882 frames at
  44,100 Hz, four buffers, ~80 ms latency); real-data test over 600 rendered frames (20 s) delivers byte-identical SHA-256 over two
  runs.
- **Simulation foundation (package E1):** Chinook/transport/tunnel AI and projectile flight time are added; module catalogue
  now 85 of 205 types implemented, 1,470 of 2,115 objects instantiable (69.5 %), compatibility baseline
  749 entries, snapshot v7/replay v7/`CommandTypes.Version` 9; retail reference match: winner FactionAmerica at
  tick 13,800, end hash `4DCB46FE3D2E8214`.

**Acceptance:**

- *Complete headless match can be visualised and controlled live:* fulfilled – `--match`/`--smoke-test --capture` show
  and control a running skirmish match, reproduced deterministically several times (identical hashes over
  repeated runs in terrain, object and FX layer).
- *All visible object states change correctly:* fulfilled with a restriction – `ModelConditionState` changes
  (movement, attack, damage, build/sell progress) are displayed, (state Phase 7: turrets stayed in rest pose; since Phase 15 they follow the shots, doors/pre-attack/packing
  are still not displayed, the presentation never sets 54 of 106 state names).
- *Defined performance/memory/golden-image budgets kept:* fulfilled for the measured layers (terrain
  0.266 ms, objects 0.19–0.60 ms, FX ≤ 1.735 ms for 10,000 particles – clearly below the originally stated
  guide value of 4 ms for ~600 drawables); golden-image/draw-list hashes are byte-identical over repeated runs.
  Measured on the development machine, not on separately defined target hardware.
- *Play videos without a proprietary Miles/Bink SDK dependency:* **not fulfilled.** There is no
  Bink decoder; every call is counted (`Stats.SkippedMovies`) and otherwise does nothing. According to `docs/GAME-AUDIO.md`
  this is a known, permanent limit of the project (no legal decoding without the official Bink SDK),
  not an implementation defect of this phase.

**Known limits:**

- No Bink video decoder (see above) – permanent limit, no SDK available.
- Turrets render in rest pose – superseded: since Phase 15 they follow the shots (`docs/CLIENT-OBJECTS.md`).
- *(Superseded: the black surfaces were the inverted winding order of the object meshes and missing lighting, fixed in Phase 15.)* Some thin, two-sided W3D meshes (`mesh.IsTwoSided` → `CullMode.None`) show from the back an
  unlit (black) surface, because `MaterialDiffuseLambertModelFeature` does not mirror the normals for back faces
  – a behaviour of the shared Stride material pipeline, not of the object layer itself.
- No blob/contact shadow: not implemented in this phase for reasons of time.
- `WaterAppearance.AdditiveBlending` unchecked (none of the eleven probed maps uses it); `WaterAppearance.TransparentWaterDepth`
  is not evaluated (water has constant opacity independent of the depth).
- `FxFrame` contents are not drawn completely: `ParticleKind.Drawable` (1 of 1,087 particle systems in the
  retail content affected, no W3D particle-model render path), `ParticleKind.Smudge` (no post-processing access),
  `LightPulseInstance` (counted, not drawn), `RayEffectRequest` (0 retail uses).
- Camera-aligned billboards/HLOD aggregates and proxies are not resolved; `AnimationMode.Manual` always renders
  frame 0; `StartFrameFirst/Last`/`MaintainFrameAcrossStates*` are not evaluated; environment mapping
  uses a fixed view direction instead of real per-pixel reflection.
- Still open from Phase 6/package E1: real collision (`ToppleUpdate`/`CrushDie`), bridges, formations, local
  avoidance, effect of `CommandSet`/radar, veteran-based `ArmorSet`/`WeaponSet` selection; 645 of the 2,115
  retail objects stay non-instantiable.

## 8. Input, UI and localisation

**Result:** menus and in-game UI reproduce the entire single-player operating path
resolution-independently on the basis of the original data.

Scope:

- implement mouse, keyboard, hotkeys, selection, control groups and command modes;
- connect WND layouts, fonts, textures, animations and STR localisation;
- implement main menu, options, skirmish setup, loading screen, HUD, command bar, tooltips, menus and
  result screen;
- resolution, aspect ratio, scaling, audio/graphics options and persistent settings;
- secure focus, pause behaviour and basic accessibility.

Completed when the program can be operated without developer arguments from the start menu into a match and back,
all needed texts are resolvable and UI tests cover several resolutions and
aspect ratios.

**Status: completed on 30 September 2026, with the restrictions named below.** All numbers taken over from the
subject documents of the packages; details in `docs/PRESENTATION-INPUT.md`, `docs/GAME-SHELL.md`,
`docs/GAME-HUD.md`, `docs/CLIENT-UI-RENDERING.md`.

**Implemented:**

- **Input:** `CnCNew.Presentation.Input` (`GameInputController`, `SelectionModel`, `CommandModeController`,
  `CommandBarModel`, `HotkeyMap`, `CameraInputController`) translates events in the order of
  `GameClient.cpp` (meta keys 20, command-bar hotkeys 25, placement 30, GUI command 40, selection 50, camera 60,
  context commands 70) into typed simulation commands. `Data/English/CommandMap.ini`: 96 entries, 0 problems,
  0 conflicts; 820 command buttons (7 without a bound command), 472 command sets, 0 problems; 14 synthetic
  tests plus one real-data test, two runs byte-identical (`tests/CnCNew.Presentation.Tests`).
- **Menus (`CnCNew.Game/Shell`):** state machine boot → menus (MainMenu/Options/SkirmishSetup+MapSelect/
  Credits/QuitConfirm) → MatchLoading → InMatch → Results → back, 8 screens from real WND layouts, 0 unregistered
  callbacks/0 unresolved labels over all screens, 54 golden hashes (8 screens × 6 resolutions from
  800×600 to 2560×1080), 0 file-name fallbacks among 64 listed multiplayer maps.
- **HUD (`CnCNew.Game/Hud`):** `MatchController` binds command bar (14 slots + 9 queue slots), minimap
  (click/drag/right-click command), drag selection, cameo/hotkey/cost/cooldown overlays, money/energy bars and
  an EVA text substitute ticker from real `ControlBar.wnd`/`QuitMenu.wnd` data, exclusively through
  `LiveMatch.Commands`. Golden draw-list hash over four resolutions (800×600 to 2560×1080), all 14
  command buttons completely in the picture.
- **Settings:** `OptionsScreen` edits a working copy of `GameSettings` and writes it persistently only on
  "Accept" through `SettingsStore` (`settings.json`, atomic); wired are resolution, the three
  volume sliders and the scroll speed.
- **UI rendering/localisation:** `CnCNew.Client.Stride/Ui/UiRenderer` draws the `UiDrawList`s generated by shell/HUD
  resolution-independently by `SpriteBatch`; text comes from FreeType (`Stride.SharpFont`), not from
  `SpriteFont`/GDI. Last verification run (32 PNGs over four resolutions): `commands=5778 sprites=12542
  drawCalls=854 batches=361 missingImages=8 missingTextures=0 missingLabels=12 missingGlyphs=0`; two independent
  runs deliver identical SHA-256 for all 32 PNGs.
- Build unchanged 21 warnings (known Stride asset-compiler baseline), 0 new.

**Acceptance:**

- *Operable without developer arguments from the start menu into a match and back:* fulfilled for skirmish (MainMenu →
  SkirmishSetup → MatchLoading → InMatch → Results → MainMenu); multiplayer, load/replay and
  campaign entry stay without function (buttons visible, but disabled – outside the product scope,
  see `CLAUDE.md`).
- *All needed texts resolvable:* fulfilled with a restriction – 0 measured fallbacks for map names and
  shell labels; but `ShellTextMeasure` delivers synthetic instead of real glyph metrics, and FreeType glyphs are
  pixel-exact to the FreeType rasterisation, not to the original GDI rasterisation (`CreateFont`/`ExtTextOut`) – deviations
  show at most at very small font sizes.
- *UI tests cover several resolutions/aspect ratios:* fulfilled – golden hashes for shell (6 resolutions),
  HUD (4 resolutions) and the UI renderer (4 resolutions) are byte-identical over repeated runs.
- *Resolution, aspect ratio, scaling, audio/graphics options persistent:* **not completely fulfilled.**
  Resolution, volume and scroll speed are wired and persistent; display mode (window/full screen),
  language, aspect ratio and accessibility text scaling do exist in `GameSettings`, but have no gadget of their own in
  the shipped `OptionsMenu.wnd` and are not reachable from this screen.

**Known limits:**

- No multiplayer, load/replay or campaign entry from the main menu (buttons visible, disabled);
  the associated multiplayer/campaign WND files (`GameSpyGameOptionsMenu.wnd`, `LanGameOptionsMenu.wnd`,
  `GameSpyLoadScreen.wnd`, `MultiplayerLoadScreen.wnd`, `ChallengeLoadScreen.wnd`) are not loaded.
- Display mode/language/aspect ratio/accessibility text scaling are not reachable through the shipped
  `OptionsMenu.wnd` (see above).
- `RadioButtonUserMaps` delivers an empty list (no import path for custom maps).
- No mouse click ends the credits (only Escape) – the message model so far knows no "any click on
  a non-gadget window" message.
- No formation/group-offset logic (`groupMoveToPosition` gives all units the same target point); no
  command "fire at position"; context commands for hijacking, car bomb, sabotage, salvaging, capturing buildings and
  hacking have no simulation commands and stay untranslated (`FeedbackKind.Unsupported`).
- `docs/PRESENTATION-INPUT.md` §10 lists six missing simulation read/write APIs (among others
  build-cost dry run, read access to the production queue, `SpecialPowerModuleBase.AvailableOnFrame`,
  `UpgradeMuxModule.Executed`) as well as missing commands for fire at position, permanent attack march, guard,
  waypoints as AI states, formation movement, capturing/hijacking/sabotage/hacking, parachute drop,
  weapon switch, overcharge, beacon, upgrade cancel, special-power construction and line building.
- FreeType glyphs are pixel-exact to FreeType, not to the original GDI rasterisation (see above).

## 9. Skirmish, missions, campaigns and savegames

**Result:** the defined product scope is playable: complete skirmish as well as all
single-player campaigns with progress and new savegames.

Scope:

- complete skirmish setup, start positions, teams, rules, AI profiles and statistics;
- port script engine, triggers, counters, camera sequences, briefings and cutscenes;
- reproduce mission objectives, defeat/victory paths, campaign order and unlocks;
- build a versioned savegame format of our own on stable simulation snapshots;
- check every map and mission through automated load and headless sequences.

Completed when all skirmish maps can be started and ended, every campaign mission passes its
essential success and failure paths and save/load continues an identical
simulation state.

**Status: part A items 2–5 (script execution, campaign logic, savegames, headless check) and part B item 11 implemented on 1 October 2026; the phase
is thereby not yet completed in the sense of the acceptance (see "Known limits").** All numbers freshly measured; details, source references and
deviations in `docs/SCRIPT-ENGINE.md` and `docs/CAMPAIGN-AND-SAVEGAMES.md`.

**Phase 10 interim state (1 October 2026, part of the play-through acceptance, not yet completed):** command-button abilities
(`NAMED_/TEAM_USE_COMMANDBUTTON_ABILITY(_ON_NAMED)`, the four most frequent of the family) now trigger real `UseSpecialPowerCommand` commands;
`SpecialAbilityUpdate` is an honest shell (reload mechanism without the staged ability effect) and makes 36 previously blocked
objects instantiable (Ranger, Jarmen Kell, GLA Rebel abilities). Local side without `playerIsHuman`: `MissionOptions.HumanSideName` plus
`CampaignTool --human-side` solve this on the API side; the general selection remains shell work. Numbers: 1,530 of 2,115 objects instantiable
(72.3 %, before 70.6 %), 88 of 205 module types, module uses covered 15,730 of 19,553 (80.4 %), compatibility baseline 747 → 751.
`CampaignTool audit`: missing action executions 5.7 % → 4.3 %, end probes still victory 10/24, defeat 15/24 in total (composition
shifted, `docs/SCRIPT-ENGINE.md` §9). Garrison, map reveal, AI team building, vision conditions, `SKIRMISH_*` and skirmish setup (part A
item 1) stay open; details in `docs/handoffs/HANDOFF-PHASE11.md`.

**Phase 12 state (2 October 2026, freshly measured; brief `docs/handoffs/HANDOFF-PHASE12.md`, preparation for `docs/handoffs/HANDOFF-PHASE13.md`):**

- **Objective driver (item 1, core):** `MissionObjectiveDriver`/`ObjectiveDriver` plan backwards from the `VICTORY`/`DEFEAT` scripts (plan per end script, leaves and blockers, opposite direction, direction-aware counters,
  standstill detection with union of all alternatives, defence leaves; `docs/SCRIPT-ENGINE.md` §9). Result (`CampaignTool drive`, 12,000 ticks): **victory on the own script path 22/24, defeat 24/24**
  (before 10/24 and 16/24). Open and named: `MD_GLA03` (the carrier is immortal, the victory script waits for the beam of `ParticleUplinkCannonUpdate`, not implemented) and `MD_CHI04` (long
  wave mission). The static reachability is now **victory clean 24/24, defeat clean 24/24**.
- **Actions (item 2):** 29 types executed (override relationships, the `TEAM_ALL_USE_COMMANDBUTTON_*` family, hunt with command button, `TEAM_LOAD_TRANSPORTS`, `TEAM_PANIC`, facing direction, stealth switch,
  unmanned, priority among others) and 8 recorded (`TEAM_SET_ATTITUDE`, `TEAM_WANDER(_IN_PLACE)`, `NAMED_SET_HELD`, rank, wreck lifetime): actions **160** executed / 129 passed on / **10** recorded / **45**
  missing of 344 (before 131/129/2/82); conditions unchanged 80/29 (the missing conditions are in no end chain). Hits on missing action types in the audit 1.1 % → 0.3 %.
- **Modules:** seven mission-object modules (`FireWeaponWhenDamagedBehavior`, `HordeUpdate`, `FireWeaponPower`, `BaikonurLaunchPower`, `TechBuildingBehavior`, `RadiusDecalUpdate`, `FlightDeckBehavior` as a shell,
  `docs/SIMULATION-CORE.md` §27): 98 of 205 module types, **1,680 of 2,115 objects instantiable (79.4 %)**, module uses covered 15,903 of 19,553 (81.3 %), compatibility baseline 765 entries.
- **Persona faction (item 4):** `MissionOptions.HumanFaction`, `ScriptProgram.WithHumanSide` (synthetic side `ThePlayer` for `GC_LaserGeneral`), `CampaignCatalog.WithPersonas(ChallengeMode.ini)`;
  `GC_LaserGeneral` now has a human side of its own (`docs/CAMPAIGN-AND-SAVEGAMES.md` §7).
- **Client wiring (item 5):** `ClientRuntime` and `StrideInputTranslator` connect HUD draw list, input and shell outputs to the Stride client, plus the background art of the command bar
  (`ControlBarScheme.ini`, `RightHUDImage`) and the minimap (`docs/GAME-HUD.md` §15).
- **Not implemented:** item 3 (**reinforcement with transport/formations**: all `CINE_*` transport aircraft are not instantiable, the Chinook delivery is missing) and item 6 (**VM semantics gaps**, only
  documented and left with reasons: `docs/SCRIPT-ENGINE.md` §8 item 23). Both are the subject of `docs/handoffs/HANDOFF-PHASE13.md`.
- Unchanged: retail reference match winner FactionGLA at tick 19,200, end hash `28BE35C1174E369F`; stress test `56882F763EABEF67`; no version jumps (`WorldSnapshot` 7, replay 7, `MissionSnapshot` 2,
  `CommandTypes.Version` 10; `humanFaction` is written in the mission state only when the value is set); 21 explained build warnings.

**Phase 11 state (1 October 2026, freshly measured; brief `docs/handoffs/HANDOFF-PHASE11.md`, preparation for `docs/handoffs/HANDOFF-PHASE12.md`):** the script VM now also executes the
`SKIRMISH_*` conditions (decision "porting" instead of a `SkirmishAi` substitute, `docs/SCRIPT-ENGINE.md` §5c), vision/special-power conditions, map reveal, garrison/containers, build switches,
`BUILD_TEAM`/`TEAM_MERGE_INTO_TEAM`/`RECRUIT_TEAM`, the waypoint variants of the command-button abilities, **sequential scripts** (§5a) and **team-event scripts** (§5b); upgrade/`FIRE_WEAPON`
buttons have a path of their own. Conditions **80** executed / 29 missing of 109, actions **131** executed / 129 passed on / **2 recorded** / **82** missing of 344
(before the phase: 63/46 and 73/129/0/142). The shell selects the human side of a challenge map itself (`ChallengeSides`), `CampaignController.StartCurrentMission` has the
latency parameter, and a campaign mission plays behind the full skirmish command bar (`LiveGame`/`LiveMission`/`MissionController`, `docs/GAME-HUD.md` §14).
**New evidence for "the mission ends on its own script path":** `CampaignTool reachability` checks statically whether the `VICTORY`/`DEFEAT` scripts of every map can
fire at all with the implemented types (chains over flags, counters, timers, unlocks and team events). Before (state of Phase 10, same analysis): victory clean 4 / restricted 18 / blocked 2,
defeat clean 0 / restricted 24; **now: victory clean 21 / restricted 3 / blocked 0, defeat clean 24 / 24 maps** (table: `docs/SCRIPT-ENGINE.md` §9). "Clean" means:
every condition and action on the chain is implemented; it does **not** mean that a player can win the mission (state conditions are leaves). The elimination end probes of the
`CampaignTool audit` are unchanged victory 10/24, defeat 15/24 – they do not pursue objectives. The new objective driver (`CampaignTool drive`, `MissionObjectiveDriver`) fulfils the world-state conditions of the scripts and counts only when a `VICTORY`/`DEFEAT` action of the script itself fires: **victory on the script path 10/24, defeat 16/24** (`docs/SCRIPT-ENGINE.md` §9, with the finding of what the others fail at). Still open: `TEAM_HUNT_WITH_COMMAND_BUTTON`, `TEAM_SET_ATTITUDE`,
`TEAM_LOAD_TRANSPORTS`, the `TEAM_ALL_USE_COMMANDBUTTON_ON_NEAREST_*` family, the override relationships, reinforcement with transport, `PLAYER_COMPLETED/MIDWAY_SPECIAL_POWER`, the persona faction
of the challenge selection (`docs/SCRIPT-ENGINE.md` §9, blocker list).

**History (state before Phase 11, only for traceability; the numbers below come from Phase 9):**

- **Script VM** (`CnCNew.Simulation/Scripting`): deterministic execution of the decoded World Builder scripts (counters, flags, timers, random timers,
  one-shot scripts, difficulty flags, deferred evaluation, subroutines, teams with instantiation, named objects, area and
  damage conditions, waypoint paths, commands as validated player commands, attack priorities, reinforcement, victory/defeat). Conditions 63 executed
  / 46 missing of 109, actions 69 executed / 129 passed on to the client / 146 missing of 344; missing types are counted at run time
  (`MissingActionHits`/`MissingConditionHits`), unresolvable references as `Diagnostics`.
- **Mission session** (`MissionSession`): world from map data (sides, relationships, placed objects, AI build lists, start base of human challenge players),
  scripts + automatic combat + world step per tick; `MissionSnapshot` (version 1) wraps the `WorldSnapshot` (version 7) and stores script state and
  command sequences. Objects come into being immediately as in the original (`SimulationWorld.MaterializePendingSpawns`). New world accesses: `SetMoney`,
  `SetRelationship`, `TransferOwnership`.
- **Campaign** (`CnCNew.Game/Campaign`): `CampaignCatalog` (Campaign.ini, 17 campaigns/85 missions, unknown keys reported), `CampaignProgress`
  (victory advances, defeat repeats, attempt counter for `MISSION_ATTEMPTS`), `CampaignController`/`MissionRun`.
- **Savegame** (`CnCNew.Game/Save`): `SaveGame` version 1 (header + progress + embedded mission state, SHA-256 checksum, canonical bytes,
  understandable rejection reasons), `SaveGameStore` (atomic, lists damaged files with the reason).
- **Checking:** `CnCNew.CampaignTool` (`list`, `audit`, `playthrough`) and `SimTool mission-run`. 24 unique campaign maps: 24/24 deterministic (two runs),
  24/24 identical after save/restore, end probes by eliminating the opponents victory 10/24 and by eliminating the viewer defeat 15/24;
  one challenge campaign was played through over five missions with save/load per mission. New tests: 11 script/mission tests in
  `Tests.Scripting.cs`, `CampaignTests.cs` (7 + real data).
- **Part B:** item 11 (map scripts) see above; item 12 was already done. `CommandButtonHuntUpdate` and `LockWeaponCreate` are bound as honest shell modules
  (`docs/SCRIPT-ENGINE.md` §8): 1,494 of 2,115 objects instantiable (70.6 %, before 69.5 %), 87 of 205 module types, module uses
  covered 15,639 of 19,553 (80.0 %), compatibility baseline 749 → 747. Items 9 (collision) and 10 (build/rule limits) are **open**.

**Phase 13 state (3 October 2026, freshly measured; brief "finishing the open items" from `docs/handoffs/HANDOFF-PHASE13.md`, preparation for `docs/handoffs/HANDOFF-PHASE14.md`):**

- **The two open victory missions (item 2):** `MD_GLA03` through `ParticleUplinkCannonUpdate` (beam times, sweep, damage pulses, `LaserBeamRadius`) and the driver leaf "capture the power at the carrier, charge, trigger" (victory `CINE_ENDVictory` @3,613);
  `MD_CHI04` through `UndeadBody`, `BattleBusSlowDeathBehavior`, `SubObjectsUpgrade`, `AnimationSteeringUpdate` (victory `END_Final_` @48,348, which is why the default of `drive` is now 60,000 ticks). **`CampaignTool drive`: victory 24/24, defeat 24/24**
  (end of Phase 12: 22/24 and 24/24); `reachability` unchanged clean 24/24 and 24/24.
- **Reinforcement with transport (item 3):** `DeliverPayloadAIUpdate`, `ParachuteContain`, `JetSlowDeathBehavior`, `CountermeasuresBehavior`; `CREATE_REINFORCEMENT_TEAM` with `teamTransport`/`teamStartsFull`/`teamTransportsExit`
  (`ScriptEngine.Reinforcements.cs`); `CampaignTool transports`/`teams`. Not implemented: formations/attack march (`asTeam`); `CINE_U06_CruiseMissileFLY` stays blocked on `JetAIUpdate`.
- **VM semantics (item 5, `docs/SCRIPT-ENGINE.md` §8 item 23):** done: inactive singleton teams are `NULL`, team building and recruiting run through the build/readiness queues with start actions
  (`ScriptEngine.TeamQueues.cs`), upgrade buttons report readiness (`IUpgradeModule.WouldUpgrade`). Deliberately open and with reasons: "idle" without AI state, `…_USING_WAYPOINT_PATH` without path flight, `FIRE_WEAPON` without `WeaponSlot`.
- **Playability (item 1, partial step):** `CampaignTool drive --commands-only` runs the driver without any world write (commands instead of killing/placing/money; `SkirmishAi` as the economy and army agent of the viewer, `docs/SCRIPT-ENGINE.md` §9 "commands-only mode"). **Measurement (15,000 ticks): 0 world writes, victory through the own scripts only on 1 of 24 maps (`MD_GLA04`, where the script ends by itself)** – the agent loses its only wave against the prefabricated enemy bases, money/science/owner/flags are reachable only through writes. The playability is thereby **not** verified, but measurable; a campaign player agent is the subject of `docs/handoffs/HANDOFF-PHASE14.md`.
- **Script actions (item 4, part):** `TEAM_FACE_NAMED` and `PLAYER_SELL_EVERYTHING` executed, `NAMED_SET_STOPPING_DISTANCE`, `NAMED_SET_REPULSOR`, `SET_BASE_CONSTRUCTION_SPEED` recorded: actions **162** executed / 129 passed on / **13** recorded / **40** missing of 344 (before 160/129/10/45).
- **Client (item 6):** the smoke tests (`--smoke-test`, `--shell`, `--mission GC_ChemGeneral`) run headless without error with the new modules; an interactive click-through by a human did **not** take place.
- **Modules and numbers:** module catalogue **107 of 205**, **1,735 of 2,115 objects instantiable (82.0 %)**, module uses covered 16,085 of 19,553 (82.3 %), compatibility baseline **771**. Script VM unchanged 80/29 conditions,
  160/129/10/45 actions. Audit (900 ticks): 24/24 deterministic, 24/24 identical after save/load, end probes victory 13/24, defeat 15/24, hits on missing action types 0.3 %.
  Retail reference match: winner FactionGLA at tick 15,000, end hash `532F19F360C59550` (before tick 19,200, `28BE35C1174E369F`; cause: the new flight/vehicle modules, not the team queues); stress test unchanged `56882F763EABEF67`.
  No version jumps (`WorldSnapshot` 7, replay 7, `MissionSnapshot` 2, `CommandTypes.Version` 10).

**Known limits:**

**Phase 14 state (3 October 2026, freshly measured; brief from `docs/handoffs/HANDOFF-PHASE14.md`, preparation for `docs/handoffs/HANDOFF-PHASE15.md`):** the module coverage is closed: **all 183 simulation modules of the catalogue are implemented, 2,115 of 2,115 retail objects
instantiable**, module uses covered 16,558 of 19,553 (84.7 %; the rest are the 22 presentation modules), compatibility baseline 751. Four independently developed groups (air 10 modules, crates/special/collision 21, structure/world 24,
slaves/spawn/container 12) and the last nine blockers (`SalvageCrateCollide`, `UnitCrateCollide`, two sabotage crates, `SmartBombTargetHomingUpdate`, `SupplyWarehouseCripplingBehavior`, `RailedTransport*`) are merged (`docs/SIMULATION-CORE.md` §30);
a **weapon-slot model** (primary/secondary/tertiary, clips and reloading per slot, locks, set selection by conditions, best-weapon selection; §30.6) makes the active `WeaponSet` selection the only source of the fired weapon (`WorldSnapshot` 7 → 8,
replay 7 → 8, `CommandTypes.Version` 10 → 11). Script side: formations (`asTeam`), path flight of the particle beam, air deliveries by OCL (`DeliverPayload`, special powers at the map edge), death weapons, `NAMED_SET_HELD`, disabled factories, train hold,
garrison exit side, booby traps, bridge state and script names that travel along; the VM executes **109 of 109 conditions** and 187 action types (129 passed on, 19 recorded, 9 missing – none of them used in a campaign map).
The objective driver reaches **victory and defeat on the script path on 24/24 maps**; `audit --ticks 900`: 24/24 deterministic and identical after save/load, 0 hits on missing types, elimination end probes 13/24 victories and 15/24 defeats; reachability
clean 24/24 and 24/24. Reference values: retail reference match winner FactionGLA at tick 13,890, end hash `76F22CB0CA79BEEF`; stress test `56882F763EABEF67`; clean solution build 21 explained warnings. Commands-only agent (`drive --commands-only`, 15,000 ticks, without any world write): victory through the own scripts still only on **1 of 24 maps** (`MD_GLA04`) – the new modules have not improved the playability.

**Known limits (state after Phase 14):**

- **The playability of the missions by a human is not verified.** Verified is: the end scripts of every map are statically reachable (victory and defeat each 24/24 "clean"), every map runs
  deterministically and continues identically after save/load, and the goal-directed driver reaches victory on the own script path on 24/24 and defeat on 24/24 maps (since Phase 13; before 22/24). The driver, however, replaces player actions
  by interventions in the world (placed units, removed opponents in defence areas, UI signals); "yes" verifies the script chain, not that a player can win with the implemented units. The
  elimination end probes still reach only 13/24 victories and 15/24 defeats. `MD_GLA03` and `MD_CHI04` have been closed since Phase 13; in Phase 14 `MD_GLA03` (victory) and `MD_GLA04` (defeat) had to be readjusted in the driver after the new opponent modules (`docs/SCRIPT-ENGINE.md` §9).
- Script actions still missing (static, scripts/maps): `SKIRMISH_MOVE_TO_APPROACH_PATH` 10/3, `SKIRMISH_FOLLOW_APPROACH_PATH` 7/3, `AI_PLAYER_BUILD_SUPPLY_CENTER` 5/1, `SET_TRAIN_HELD` 5/1,
  `TEAM_CAPTURE_NEAREST_UNOWNED_FACTION_UNIT` 5/4, `TEAM_GUARD_SUPPLY_CENTER` 5/1, …; missing conditions unchanged `PLAYER_COMPLETED/MIDWAY_SPECIAL_POWER`, `SUPPLY_SOURCE_SAFE/ATTACKED`,
  `PLAYER_BUILT_UPGRADE`, `TEAM_STATE_IS`, `PLAYER_POWER_COMPARE_PERCENT`, `SKIRMISH_TECH_BUILDING_WITHIN_DISTANCE` (`docs/SCRIPT-ENGINE.md` §9). **Reinforcement with transport** has been simulated since Phase 13 for aircraft, parachute and `teamStartsFull` (open: Chinook delivery, `CINE_U06_CruiseMissileFLY`), formations/attack march are not simulated; ten actions are only recorded (`SCRIPT-ENGINE.md` §8 item 17).
- The remaining VM semantics gaps (state Phase 14: the complete team state machine of the original, path flight for missiles other than the particle beam, the nine missing skirmish/weapon-path actions) are in `docs/SCRIPT-ENGINE.md` §8 item 23; formations, path flight of the beam and `WeaponSlot` have been implemented since Phase 14.
- The skirmish setup (part A item 1) has been completed since Phase 10 (all 64 skirmish maps with ≥2 start positions start and end, `SimTool skirmish-sweep`); a skirmish savegame was
  deliberately not supported until Phase 24 (`SkirmishMatchSession.CreateSave()` threw); since then a running skirmish is saved and continued (`docs/SIMULATION-CORE.md` §45.3).
- Objects whose template is missing in the retail base content (map-overlay definitions such as roads, fences, trees) are skipped and counted; `*_HUNT`/`*_ATTACK_*` are movement orders plus
  automatic combat (no formation, no attack march).
- Unlocks (campaign order of the challenge campaigns, profile) are not modelled; menu, briefing, load list, saving in the game and mission completion are connected
  (`docs/CAMPAIGN-AND-SAVEGAMES.md` §7), and the Stride client has processed the shell outputs since Phase 12 (`ClientRuntime`, `docs/GAME-HUD.md` §15); the wiring is checked headless by smoke test, not played by a human.
- Missions are not replays (script actions are not recorded commands); truth is the snapshot.
- Reference values after Phase 14: retail reference match winner FactionGLA at tick 13,890, end hash `76F22CB0CA79BEEF` (Phase 13: tick 15,000, `532F19F360C59550`; Phase 10–12: tick 19,200, `28BE35C1174E369F`); stress test `56882F763EABEF67`.

## 10. Tick rate, fixed point and multiplayer through dedicated servers

**Status: planned on 5 October 2026. As of 9 October 2026: Phases 18 to 23 are completed, of the
multiplayer part (Phases 24 to 29) nothing is built; next follows Phase 24.** Briefs: `docs/handoffs/HANDOFF-PHASE18.md` to
`docs/handoffs/HANDOFF-PHASE29.md`; common frame: `docs/MULTIPLAYER-PLAN.md`.

**Result:** the simulation runs at 60 Hz with a parameterised tick rate and Q39.24 fixed point,
is verified as deterministic across machine boundaries, and matches with up to eight players (humans and
AI mixed) run as lockstep through a relay server that neither simulates nor loads game data.
The local skirmish uses the same path with an embedded server.

Scope (one phase per item):

- 18: reference manifest as the yardstick, determinism across machine boundaries, performance profile of real
  matches, checked inventory of the tick-rate coupling (`docs/TICKRATE-AUDIT.md`); go-ahead;
- 19: lower the tick costs of real matches, result-identical;
- 20, 21: tick rate as a parameter in simulation, presentation, game layer, UI, sound, tools and
  tests, result-identical and image-identical at 30 Hz;
- 22: fixed point Q47.16 → Q39.24 as a single, measured change;
- 23: time-step invariance, rate-equivalence suite (30/60/120 Hz) as a newly designed acceptance,
  60 Hz as the default, new survey of all reference values – **done on 9 October 2026, measured acceptance and
  limits below**;
- 24: AI in the hashed and saved session state, skirmish savegame, match description,
  two-stage command path, tick source;
- 25: protocol, room and relay in the process; local skirmish over the network path; AI slots;
- 26: dedicated server, UDP transport, time synchronisation, adaptive buffers, eight clients;
- 27: desync diagnosis, rejoining, spectators, server replay;
- 28: server operation and security;
- 29: multiplayer in the client, latency perception, acceptance with humans.

Completed when the rate-equivalence suite passes at 30, 60 and 120 Hz, all reference values at
60 Hz are in the reference manifest, the manifest passes identically on at least two different
machines, eight clients stay hash-identical through a dedicated server under a disturbed network, a
disconnected client rejoins hash-identically and humans have accepted a local skirmish, a LAN match and an
internet match with AI slots.

Known limits (state of the planning): map reveal by manipulated clients is not preventable in the
lockstep model; the campaign stays local (its scripts accept feedback from the
presentation); the server distributes no maps and no data.

**Measured acceptance of Phase 23 (9 October 2026, state `f3acc2e`; `docs/TICKRATE-MIGRATION.md` §15.7):**

- Rate-equivalence suite: 0 of 4,388 quantities outside the tolerance at 60 and at 120 Hz, in all ten groups (first run: 479 / 625 of 4,307); the tolerances were set before the first run and have never been changed.
- Default 60 Hz (`e8632bb`, one line); both manifests written at 60 Hz (`ea52f87`) and checked twice as identical with 50 of 50 runs (Windows, Release).
- All test programs pass at 30, 60 and 120 Hz (`Simulation.Tests` 513 of 513, with real data 543 of 543; state `1b82e45`); clean build 0 warnings; compat-check 751.
- The retail reference match is the same game at 30, 60 and 120 Hz (winner FactionGLA in original frame 14,130, 26,626 commands).
- Distribution comparison 30 against 60 Hz over 320 matches per rate: 39 of 39 checks passed.
- Performance gate at 60 Hz (8 players, 30 game minutes): mean, 99 % quantile, catch-up speed and freedom from gen-2 reached.

**Known limits of Phase 23 (named, not fixed):**

- **No human has seen or heard the game at 60 Hz.** There was one viewing of an AI match at 30 Hz (8 October 2026), before the corrections that followed from it.
- Of the acceptance sentence of this section, "the manifest identical on at least two different machines" is **not fulfilled** (second machine open); with the 60 Hz manifests the Linux run and the determinism matrix are also not measured (`docs/DETERMINISM.md` §2c).
- "The suite passes" means: the measured quantities of its cases lie within the tolerance – not that every match is identical. On shipped maps 2 of 16 pairings end at 60 Hz in a different frame (7 s and 84.5 s, the same winner); 33 of 320 matches of the survey end more than one second away from the 30 Hz match, 3 with a different outcome; 120 Hz is not measured for pairings and survey.
- The equality of whole AI matches rests on a unit deciding its own attack once per original frame; the brief of the phase forbade that by its wording for target acquisition. The confirmation lies with the project owner (`docs/MULTIPLAYER-PLAN.md` §7a no. 1).
- Performance gate: maximum 141–143 ms per tick (target ≤ 8 ms) and allocation 8 KB per tick missed; the maximum goes down only with a behaviour change that the project owner has reserved.
- Not converted: some movers still compute `value per tick × r` (Spectre, parachute, flares, particle beam, shot-down jet, gravity of the physics modules); `SlavedUpdate` and the regeneration of the occupants heal per tick; script VM, special abilities, bridges, water and cliffs are not investigated; the suite knows only the flat map.
- Three behaviour changes at 30 Hz were added (lost wake request, factory exit, AI and movement after the first viewing); the balance after them is not assessed.
- A savegame written at 30 Hz is rejected by the client without an option and loads only with `--tick-rate 30`; the draw order of the roads depends on the rate.
- The remaining open decisions of the project owner (eleven): `docs/MULTIPLAYER-PLAN.md` §7a.

## 11. Patch integration, quality and shippable completion

**Result:** from the domains arises a reproducibly buildable, diagnosable and locally
shippable product with clearly selectable original and bug-fix behaviour.

Scope:

- check GeneralsGameCode fixes by subsystem and adopt each with a regression test;
- integrate GeneralsGamePatch as an overlay layer of its own and keep balance changes separately
  configurable;
- add crash reporting, structured logs, diagnostic dumps, profiler and support bundle;
- eliminate dependency warnings or isolate build tools, document licences and
  attributions;
- automate clean-room build, fresh asset import, complete test matrix, long-term runs and
  packaging;
- finish installation, migration and troubleshooting documentation;
- package and operating documentation for the dedicated server (Windows, Linux) as well as the version and
  compatibility rule for savegames, replays and the network protocol from the first release on.

Completed when a fresh machine can reproducibly turn source code and local
original installation into a startable package from documented prerequisites, the entire
regression test matrix is green, no unresolved high-priority defects are open and the scope defined in this
document (single player and multiplayer skirmishes through dedicated servers) has been accepted end to end.

## Order and change rule

The steps are completed in the stated order. Findings may make later
steps more precise; changes to goal, delimitation or completion criteria of this plan are,
however, justified and versioned separately. Steps 1 to 8 are completed, step 9 is far advanced, but not ended in the sense of its acceptance (see "Known limits" above).
**Phase 15 "Tech-demo maturity I – world rendering and security of the dependencies" (4 October 2026, freshly measured; `docs/DEMO-FINDINGS.md`, `docs/CLIENT-OBJECTS.md`): **Security:** the four vulnerable transitive tooling packages of Stride are raised to patch versions through `build/StrideToolingPins.props`, `scripts/audit-vulnerable.ps1` reports no known vulnerability, the suppression of `NU1901`/`NU1903` is removed, the clean solution build has **11** warnings (10 `YamlAssemblyRegistry` and 1 `AssetCompiler` note from Stride's tool, before 21). **World:** the "half buildings" had three rendering causes (release of the shared index buffer, inverted winding order of the object meshes, missing lighting) plus ignored bone visibility – fixed; `PresentationTool model-audit` draws 1,585,401 of 1,585,417 triangles (0 lost in the render plan); turrets follow the shots, tyres rotate with the distance travelled, muzzle flash appears only after a shot, unexplored scenery stays hidden, the HUD overlay never drawn before (selection, health bars, command lines, rally points, healing symbols) is drawn, the supply Chinook shows the unloading animation only at the depot. The reported finding of wrongly rotated map objects was **not reproduced** (`angle-audit`: the stored angle aligns 64.6 % of the fence rows). In passing: pathfinding around standing units (488 → 0 overlap ticks), AI rally point free of buildings, battle plan as a typed option (`CommandTypes.Version` 12, replay 9). Addendum 4 October 2026: roads, fog darkening, steering/suspension and 95 of 106 model states are built; three staged audit demos (`docs/DEMO-SCENARIOS.md`) uncovered gaps of the simulation, which are closed – turret state with fire clearance (`WorldSnapshot` 9), staged building capture, winged flight with a runway from model bones; new reference values: reference match winner FactionGLA tick 15,000, end hash `01C01E3B91EAE18C` (after the permanent turret attack state and idle scan; `CommandTypes.Version` 13, replay 10), stress test unchanged `56882F763EABEF67`. **Not verified:** the visual acceptance by a human (the checklist in `docs/CLIENT-OBJECTS.md` is not ticked off, the checksums of `scripts/capture-world.ps1` are provisional), sound, cursor, waypoints, text and mission in the client (Phase 16), the playability (Phase 17); 54 model states (doors, pre-attack, packing, afterburner …) are never set, steering/suspension, roads and the darkening of objects in the fog are missing.**

Phase 16 "Tech-demo maturity II – operation" (begun, not completed; 4 October 2026, **partly; nothing of it accepted by a human**; `docs/DEMO-FINDINGS.md` nos. 16–19, `docs/handoffs/HANDOFF-PHASE16.md`): **Sound** is wired in the client and measured (`ClientAudio`/`GameAudio`: weapons, FX, building, EVA, voices, UI, music; counters per source, `--audio-capture`, `--mute`; `docs/GAME-AUDIO.md`), but has never been heard. **Mouse pointer** from the original cursor files (`CursorReader`, software cursor, change by context/mode/modifier), **waypoint paths** and target markers are built (`docs/PRESENTATION-INPUT.md` §11/§12, `docs/GAME-HUD.md` §16). **Feedback of a human** worked off: flashing and tick sound of the building capture (`SimEventKind.CaptureFlash`), five shell defects (button states, sliders, hover on texts, hidden save buttons, template font size – the transition animations are still missing), camera scrolling (was around 3000 times too slow); "dozer does not build in the mission" is reproduced and fixed (the script intro of `GC_ChemGeneral` pulled the camera away under the pointer; input lock, letterbox and camera pan of the script are now obeyed; `scripts/check-dozer-build.ps1` replays the clicks in the real client). **Mission in the client:** the director shows script camera, letterbox, fades, input lock, subtitles, counters, banners and script sounds and counts every script output (`docs/SCRIPT-ENGINE.md` §7a; `GC_ChemGeneral` 17 of 29 displayed, `MD_USA01` 72 of 223). **Simulation** (`docs/SIM-FIRING-TRACKER.md`, `docs/SIM-TURRET-AIM.md` §8a, `docs/SIMULATION-CORE.md` §32): firing stages `CONTINUOUS_FIRE_*` as a weapon bonus (Gatlings now shoot faster), `PreAttackDelay` (29 weapons, `PREATTACK_A/B/C`), damaged locomotors, `StructureToppleUpdate` reaches its end state, ability ends attack, `SimTool`/`CampaignTool` with bone table; the AI shift is broken down (128 runs: the decisive factor for China/GLA is the rule "a new command ends the aiming", `PursueCommand` plays no role in `ai-match`, no behaviour correction); the skirmish AI places its supply centre at the depot (`AIPlayer::buildBySupplies`) and attacks in groups: **12 of 16** AI runs decided (before 5 to 7). **Reference values (4 October 2026, measured):** retail reference match winner FactionGLA at **tick 13,500**, end hash `0946FB676DEC615C`, chain `EA6D12F180930D71`; stress test unchanged `56882F763EABEF67` / `B8E663493F2C73D5`; `ai-match` Alpine Assault China/GLA China at 7,545 (`39344016A5EE8F91`), AirForce/China stalemate (`E222322067493EB4`), America/GLA GLA at 8,865 (`542BE1D4891FEB71`); `WorldSnapshot` 9, replay 10, `CommandTypes.Version` 13, `MissionSnapshot` 2 unchanged; module catalogue 183 of 205, 2,115 of 2,115 instantiable, compatibility baseline 751, clean solution build 11 warnings. **Campaign re-measured:** objective driver victory 24/24 and defeat 24/24, audit 24/24 deterministic and identical after save/load, elimination end probes victory 13/24, defeat 15/24; commands-only agent still **1 of 24** (also with 60,000 ticks on four maps no victory). **Not done:** text comparison with the original (capture set and findings table), the script outputs not displayed (image effects, camera paths, speech subtitles), the click-through shell → campaign → mission → save/load → completion with a human, script sounds of the missions, idle target acquisition of the turrets, landing-approach arc of the jets, Black Lotus data stream, drawn paratroopers; **Phase 17 (playability) is not begun** apart from the new survey of the numbers and the AI corrections. Addendum 5 October 2026: texts (GDI font path, sentence wrapping, tooltips; `docs/TEXT-AUDIT.md`), shell transitions and screen stack (`docs/GAME-SHELL.md` §12/§13), placement preview and a state for all 92 script output types (69 displayed; `docs/SCRIPT-ENGINE.md` §7a) are built and carried by all tests, reference values unchanged (reference match tick 13,500, `0946FB676DEC615C`); **still not seen or heard by any human.**

The next entry point is `docs/handoffs/HANDOFF-PHASE16.md` (rest: visual acceptance, texts, mission in the client, click-through), after that `docs/handoffs/HANDOFF-PHASE17.md` (playability of the missions by a human, commands-only agent, performance, approximations of the new modules). The findings of the first tech demo are in `docs/DEMO-FINDINGS.md`; the statement "Phase 7 completed" applies only to the libraries and smoke tests, not to rendering fidelity.

Phase 17 "Playability" (5 October 2026, **begun, not completed; nothing of it seen, heard or played by a human**; re-measure; `docs/SCRIPT-ENGINE.md` §9 "Phase 17", `docs/SIMULATION-CORE.md` §33/§34, `docs/GAME-HUD.md`, `docs/DEMO-FINDINGS.md`, handoff `docs/handoffs/HANDOFF-PHASE18.md`): **Target acquisition in the simulation:** units choose their victims themselves (`AutoAcquireEnemiesWhenIdle` as a strict bit list, `MoodAttackCheckRate`, after `AIUpdateInterface::getNextMoodTarget`), new `AttackMoveCommand`; `MissionSession.DriveCombat` and `SkirmishAi.Fire` are dropped, the units of a human fire by themselves when idle at enemies in range (not at ordinary buildings). **Commands-only agent:** the first unfulfilled condition of the victory chain is expressed per map as a command (attack instead of "remove surplus", `CaptureCommand` with upgrade purchase instead of change of ownership, scouts, science purchase, UI signal; a commands-only run now commands only its own units); the agent knows depots of non-hostile sides and builds its supply centre first. Result (0 world writes): at 15,000 ticks still **1 of 24** (`MD_GLA04`), at 60,000 ticks (18 maps measured) **3 maps** (`MD_GLA04`, `MD_CHI01` @18,492, `MD_CHI02` @29,605); it now fails at combat and economy of the agent (infantry waves against base defence, no second depot), no longer at rejected writes. **Operation:** message list of the HUD with the original texts (the earlier "ticker" was never drawn), letterbox after `W3DDisplay::renderLetterBox` (no bars at 16:9), typing sound of the subtitles, localised skirmish entries with a preselected map, tooltip with requirements also on locked buttons. **Measured on the merged state:** clean build **0 warnings**; all test programs green (`Game.Tests` Release), compat-check 751, 183/205 modules, 2,115/2,115 objects; retail reference match winner FactionGLA at **tick 14,490**, end hash `23B4B25DF62765D8`, chain `DE5849AF9F8493C4` (tick 13,500 / `0946FB676DEC615C` is superseded); stress test unchanged `56882F763EABEF67` / `B8E663493F2C73D5`; 16 AI runs **11 decided** (before 12), all deterministic; `CommandTypes.Version` **14**, replay **11**, `WorldSnapshot` 9, `MissionSnapshot` 2; objective driver victory 24/24 and defeat 24/24, audit 24/24 deterministic and identical after save/load, end probes 13/24 and 15/24, reachability clean 24/24; three scene audits green. Performance measured quietly: stress test 60.5–61.6 µs/tick before as after; `mission-run` 3,000 ticks `MD_USA05` 73.4 → 66.7 s, `MD_GLA05` 135.6 → 147.9 s (no profile created). **Not done:** clicking through and playing by a human, an agent that wins the challenge maps, the five stalemates of the AI runs (economy after the first depot, army composition), AI difficulty levels (Easy/Medium/Hard Army start the same AI), profile of the mission runs, `PowerSabotage` consumer. **Closing session (6 October 2026, measured at state `6eb7ae4`; `docs/SIMULATION-CORE.md` §35/§36):** the skirmish AI plans its next supply centre at another, safe source as soon as the stock next to its own centres falls below 3,000 credits (`AIPlayer::findSupplyCenter`/`isSupplySourceSafe`): **12 instead of 11** of the 16 AI runs decided, no winner changes, all deterministic (new end hashes in §36.2). The army selection by value and range was measured twice (10 and 11 of 16, the mission agent lost `MD_CHI02`) and withdrawn. AI difficulty: no verified effect applies in skirmish, the setup offers one computer army (`GUI:MediumAI`), `SkirmishDifficultyIgnored` is dropped; the solo bonuses of the campaign on easy/hard are not implemented. Unchanged: reference match tick 14,490 `23B4B25DF62765D8`/`DE5849AF9F8493C4`, stress test `56882F763EABEF67`, versions, compat-check 751, 0 warnings; objective driver 24/24 and 24/24, audit 24/24, end probes 13/24 and 15/24; commands-only agent 1 of 24 (15,000 ticks) and 3 of 24 (60,000 ticks, all maps measured, `MD_USA01`/`MD_USA04` aborted). Phase 17 thus ends **without its acceptance goal**: the playability by a human is not verified; the rest is at the end of `docs/handoffs/HANDOFF-PHASE17.md` and lies, as far as it changes the simulation, behind the programme from `docs/handoffs/HANDOFF-PHASE18.md` on.

Phase 18 "Measurement basis" (6 October 2026, measured at state `b432fb7`; `docs/DETERMINISM.md`, `docs/DETERMINISM-FINDINGS.md`, `docs/PERFORMANCE.md`, `docs/TICKRATE-AUDIT.md`, `docs/SIMULATION-CORE.md` §37, go-ahead in `docs/MULTIPLAYER-PLAN.md` §4a): **Reference manifest** `baselines/reference-manifest.json` (50 runs: reference match, stress test, 16 AI runs, three 8-player runs, skirmish overview, 27 mission runs) and **input manifest** `baselines/input-manifest.json` are from now on the yardstick instead of the hashes in the documentation (`SimTool reference-manifest check`, `input-manifest check`). **Determinism matrix:** the full manifest passes twice in a row and with tiered compilation off, PGO off, ReadyToRun off, hardware intrinsics off, AVX2 off, server GC, the cultures de-DE/tr-TR/ar-SA and under **Linux (WSL2)**; the debug build only with the quick set; **open:** a second physical machine with a different CPU and a different .NET version. **Findings D1–D9** worked off: real were culture dependence in the script state, incomplete terrain fingerprints, platform-dependent line endings in the definition snapshot (Linux and Windows would have rejected each other) and missing `MarkDirty` on 19 of 45 mission maps (15 mission runs of the manifest have new hashes; reference match tick 14,490 `23B4B25DF62765D8`, stress test `56882F763EABEF67` and all AI runs unchanged); the object table now enumerates in ID order, bone table and map values are produced without floating-point runtime routines, `SourceRules` also covers the code that feeds the simulation. **Performance, measured quietly:** 8 players on Twilight Flame 4.98 ms per tick (99 %: 13.6 ms, maximum 547 ms), extrapolated to 60 Hz 30 % of one core and 3.4-fold catch-up speed; `MD_GLA05` 12.6–13.5 ms (76–81 % of one core at 60 Hz); 81 % of the update time is `AIUpdateInterface`, one third of the tick vision and hash. **Tick-rate inventory checked:** 1,959 coupled places, ten contradictions to the plan. Clean build 0 warnings, all test programs green, compat-check 751, objective driver 24/24 and 24/24. **Go-ahead:** the programme continues as planned (Phase 19 performance), with the condition to make up the run on a second machine before Phase 26. Nothing from Phases 15–17 is accepted by a human.

Phase 19 "Performance" (6 October 2026, measured at state `16d337c`; `docs/PERFORMANCE.md` §6a, `docs/PERF-MOVEMENT.md`, `docs/PERF-VISION.md`, `docs/PERF-HASH.md`, `docs/PERF-MISSIONS.md`, `docs/PERF-GAME-LAYER.md`): the tick costs of real matches are lowered **without any change of results** – measured quietly, all ten end hashes of the measurement runs identical to before: 8 players on Twilight Flame **1.14 ms per tick instead of 4.98** (99 %: 3.2 instead of 13.6 ms; maximum 94 instead of 547 ms; at 60 Hz 6.8 % of one core and 16.2-fold catch-up speed), `MD_GLA05` **3.4 instead of 13.5 ms**, `MD_USA05` 2.5 instead of 5.9 ms; allocation per tick 52 instead of 95 KB (8 players) and 10 KB instead of 4 MB (`MD_GLA05`), no gen-2 collections any more in the skirmish. By what means: `Fixed` without 128-bit software division, movement scans without allocation and only over movable objects, pathfinding with one state byte per cell, remembered answers and abort when the target is proven unreachable; vision recomputed only where circles have changed, vision hash from the first changed place on; object description with prepared module frames; owner, kind and module-type lists in the world instead of world passes per script condition, target acquisition and module (`PointDefenseLaserUpdate` scanned all objects per laser and tick). **Targets:** catch-up speed, freedom from gen-2 and "audit per map under one minute" (9–57 s) reached; mean (≤ 1.0 ms) and 99 % (≤ 3 ms) narrowly missed; **maximum (≤ 8 ms) missed** – it arises when many units search long paths at the same time, and goes down only with a behaviour change (shared group path, cell budget per tick or a different search), which was not made. Two proposals for faster hash constructions (vision row by row; object hash with module hashes) are documented, but change every reference hash and are an **open decision of the project owner**. **Reference values:** the full manifest passes unchanged under Windows and Linux (now around 10 instead of 27 minutes) and tick by tick against the fully computed hash (718,191 ticks, 0 deviations); a further `MarkDirty` defect (`PauseCountdown`) is fixed in a commit of its own and changes exactly one mission run of the manifest; reference match tick 14,490 `23B4B25DF62765D8`, stress test `56882F763EABEF67`, the 16 AI runs and all other runs unchanged. Clean build 0 warnings, all test programs green, compat-check 751, objective driver 24/24 and 24/24, audit 24/24. New tools: `SimTool perf --json`, `perf-compare`, `path-bench`, `PresentationTool sync-perf`, `scripts/perf-compare.ps1`. **Not measured quietly:** the game layer (`LiveGame.SyncAfterTick`, under load around 2 ms per tick before and 1.6 ms after three output-neutral cuts) and the scatter between runs. What stays open is the section "Rest of Phase 17" at the end of `docs/handoffs/HANDOFF-PHASE17.md`.

Phase 20 "Tick rate as a value of the world – simulation" (6 October 2026, measured at state `9299a2d`; `docs/TICKRATE-MIGRATION.md`): the simulation carries its rate as a value (30/60/120 Hz, time service, catalogues per rate, times on the 30 Hz grid of the original); 262 + 188 + 11 places converted, `SourceRules` forbids bare tick numbers. Result-identical at 30 Hz: full reference manifest identical twice under Windows and under Linux, input manifest identical, all test programs green, 0 warnings. At 60 and 120 Hz the reference match, two AI matches and three missions run through deterministically and without overflow; the behaviour there is **not assessed** (Phase 23; the reference match finds no winner at 120 Hz). Presentation, game layer, sound and tools still assume 30 Hz (Phase 21).

Phase 21 "Tick rate as a parameter – presentation, game layer, UI, sound, tools, tests" (7 October 2026, measured at state `2a0a6f5`; `docs/TICKRATE-MIGRATION.md` §14): outside the simulation nothing assumes 1/30 s per tick any more – one constant for the original frame (`OriginalFrame.PerSecond`), presentation clock `GameClock`, rate of the session from `GameData.Rate`, client `--tick-rate N` (default 30), `SaveGame` version 2 with rate. Image-identical and result-identical at 30 Hz: all reproducible checksums (screens, HUD, UI renderer, audits, captures, sound) unchanged, full reference manifest identical under Windows and Linux, all test programs green, 0 warnings. Timing at 30/60/120 Hz equal within one original frame for all checked elements; three quantities follow the simulation and are on the list for Phase 23. **No human has seen or heard the game at 60 Hz** (visual checklist §14.7).

Phase 22 "Fixed point Q39.24" (7 October 2026, measured at state `8d74ef8`; `docs/FIXED-POINT-Q39-24.md`): `Fixed` has 24 instead of 16 fractional bits; the change is a single commit (`d7fca91`) and changes every reference value as planned. New versions: `WorldSnapshot` 10, replay 12, `CommandTypes.Version` 15, `MissionSnapshot` 3, `SaveGame` 3. New reference match: FactionGLA at tick 17,805, `D0CA5F4EE456D6CE` (new driver seed `5EED0007`); stress test `56882F763EABEF67` with chain `754FA4DCB9A41DF7`. Compared with 16 bits: missions, objective driver, audit and skirmish overview with the same outcome; 5 of the 16 AI runs with a different outcome (chaotic amplification of a rounding, balance not assessed). The overflow when squaring placeholder ranges is fixed without a change of results (`SquaredReach`). Full manifest identical under Windows and Linux, all test programs green, 0 warnings; at 60/120 Hz nothing rounds to zero any more when building the catalogue. Only reported: unchecked target coordinates in commands can make the movement overflow (Phase 27/28).

Phase 23 "Time-step invariance and 60 Hz as the default" (7 to 9 October 2026, state `f3acc2e`; `docs/TICKRATE-MIGRATION.md` §15, `docs/SIMULATION-CORE.md` §39–§43, `docs/PERFORMANCE.md` §6c, `docs/DEMO-FINDINGS.md`): the simulation runs at **60 Hz** by default (`e8632bb`; the rate remains a value of the world, 30 and 120 Hz remain supported and are checked by the rate-equivalence suite against 30 Hz). Principle: the 30 Hz step is the definition, higher rates subdivide it – the decision is made in the first tick of the original frame of an object, the 30 Hz position is reached with the last tick, observers read the frame end. The suite (4,388 measured game quantities in ten groups, tolerances fixed before the first run and never changed) went in four rounds from 479 / 625 quantities outside the tolerance (60 / 120 Hz) to 0 / 0; the retail reference match is the same game at 30, 60 and 120 Hz. Three defects at 30 Hz were fixed along the way, each with its own manifest: the lost wake request of a slow death, the factory exit, and – after the first viewing of an AI match by the project owner – the AI saving for vehicles, own target places, boarding and army composition. **The reference values stand at 60 Hz in `baselines/reference-manifest.json`** (reference match winner FactionGLA at tick 28,260 = original frame 14,130, end hash `CA05181C36C62C7A`; stress test `9EE9F730D124C3BD`; 14 of 16 AI runs decided; `WorldSnapshot` 10, replay 12, `CommandTypes.Version` 15, `MissionSnapshot` 3, `SaveGame` 3 unchanged); older hashes, victory ticks and tick numbers in this document are 30 Hz values of their state. Performance gate at 60 Hz (8 players, 30 game minutes, measured quietly): mean 0.68–0.69 ms per tick, 99 % 1.6–1.8 ms, catch-up speed 26-fold – reached; maximum 141–143 ms and allocation 8 KB per tick – missed. Besides: a spectator mode (AI against AI in a window, `docs/SPECTATOR-MODE.md`), the skirmish AI of the original as a selectable session with three difficulty levels (not the default, not selectable in the client; `docs/SKIRMISH-AI-ORIGINAL.md`), factory exits, scroll speed, mouse assignment, build keys. **Limits:** no human has seen or heard the game at 60 Hz – there was one viewing of an AI match at 30 Hz on 8 October, before the corrections that followed from it, and nothing about spectator mode, exits, scroll speed, mouse assignment or build keys is accepted; "the suite passes" does not mean that every match is identical: 2 of 16 pairings on shipped maps end at 60 Hz in a different frame, 33 of 320 matches of the survey end more than one second away from the 30 Hz match, and 120 Hz is not measured there; the equality of whole AI matches rests on a self-acquisition per original frame of the unit, whose confirmation lies with the project owner; with the 60 Hz manifests the Linux run and the determinism matrix are not measured; a savegame written at 30 Hz loads only with `--tick-rate 30`; eleven open decisions of the project owner are in `docs/MULTIPLAYER-PLAN.md` §7a. Next step: Phase 24 (`docs/handoffs/HANDOFF-PHASE24.md`).

Phase 24 "Session core" (11 October 2026; `docs/SIMULATION-CORE.md` §45, `docs/PRESENTATION-INPUT.md` §13, `docs/CAMPAIGN-AND-SAVEGAMES.md` §8): the state of the built-in `SkirmishAi` is part of the session state (described, loaded and hashed by one routine; a source test names every field), the session hash is world plus computer players plus counters, a running skirmish is saved and continued (`SkirmishMatchSession.Save`/`Restore`, `SkirmishSaveGame`, the main menu's load list), a match is started from a `MatchDescription`, the replay (format 14) carries it and can be played with the computer players deciding again, orders pass a two-stage command path (intent, pre-check, receipt, final answer) and an `ITickDriver` releases the ticks; the lockstep probe plays with an artificial input delay. **Acceptance as measured:** no result quantity of the reference manifest moved; 19 of 19 runs continue identically from 77 saves; three processes build the same match from one description; tier 4 with 36 of 36 steps. **Known limits:** AI strength is counted, not obeyed; the setup screen passes faction and money only; the in-game menu saves to one slot without a dialog; nobody has saved a skirmish, loaded one or played with a delay in a window.

Phase 25 "Protocol, room and relay in the process" (11 October 2026; `docs/NETWORK-PROTOCOL.md`, `docs/SIMULATION-CORE.md` §46, `docs/PRESENTATION-INPUT.md` §13.6, `docs/GAME-SHELL.md` §14): protocol version 1 with 25 messages and their limits; the build identity (commit and modified flag) in handshake and match description; `CnCNew.Net` (codec, loopback network, protocol client) and `CnCNew.Server` (room and relay; no game data); the lockstep driver as tick source and command scheduler in one; the local skirmish over an embedded relay; the setup screen as the room of the local game; the multi-client probe (`NetTool`). **Acceptance as measured:** a local skirmish through the relay has the replay bytes and session hash of direct submission and no added delay; the reference manifest is unchanged in every result quantity; 48 of 48 probe runs with one to eight clients are hash-identical in every tick and replay from the server's recording; the layer rules are a test. **Known limits:** everything ran in one process over a clock set by hand - no socket, no clock synchronisation, a fixed playback buffer; a slot's colour is carried, not drawn; AI strength is counted, not obeyed; nobody has seen a match through the relay in a window.

Phase 26 "Dedicated server and real transport" (12 October 2026; `docs/NETWORK-PROTOCOL.md` §12 to §17, `docs/DEDICATED-SERVER.md`, `docs/SIMULATION-CORE.md` §47): the UDP transport behind the connection interface (stateless cookie handshake, a reliable byte stream of framed messages, an urgent channel whose messages ride along until acknowledged, time limits, a strict parser); protocol version 2 (room name, ping and pong, lag state, leave); clock synchronisation by the median of ping samples and a playback delay measured from the arrivals in milliseconds, followed by rate and never by a jump; the rule for members that fall behind (shown, the game slowed, dropped) as configuration with the plan's proposal as defaults; the leave of a player as a command of the bundle after the original's `MSG_SELF_DESTRUCT` (command table 17, replay format 15, one manifest commit: 24 of 1,925 fields, no result quantity); several rooms behind one port and `tools/CnCNew.DedicatedServer`; the client without a window, the UDP proxy and the harness that runs matches of real processes. **Acceptance as measured, on one machine:** eight client processes through the server and the proxy hash-identical on 36 of 36 lines (delay 10 to 150 ms, jitter 0 to 30 ms, loss 0 to 3 %) and over a standard match of an hour; order to execution twice the delay plus 7 to 65 ms; a hitching or leaving client slows nobody, a lost connection slows the others for 8 s at half speed. **Known limits:** no second physical machine and no LAN - the acceptance "two machines" is not claimed; nothing measured on a real network; the window client cannot join a server; a client that only keeps up with the slowed game costs the others 14.4 s in three incidents; bandwidth untuned (6 to 10 KB/s per client and direction); the rule's numbers and what becomes of a leaver's objects are built proposals, not decisions; nothing seen by a human.

Phase 27 "Robustness on the network" (12 October 2026; `docs/NETWORK-PROTOCOL.md` §18 and §19, `docs/NETWORK-DIAGNOSTICS.md`, `docs/DEDICATED-SERVER.md`, `docs/DETERMINISM.md` §2d, `docs/SIMULATION-CORE.md` §48): protocol version 3 (rejoin token and watch flag in the hello, five messages); a seat held for a lost player and the way back - the state of a member through the server, passed on only with the hash all clients agreed on, loaded, hashed again, then the bundles since; watchers sent the match on a delay, and into a running match; the relay's recording as a replay file written by the dedicated server; pause limited per player and in length, the speed the host's; the lag rule's recovery time and a close after the end; the desync diagnosis - parts of the session hash asked for after a desync, a package per client, `NetTool desync-diff`; `LiveMatch.Join` and `RemoteRelay` for the window client; `NetTool` processes under WSL and a narrow line in the proxy. No format of the simulation and no reference value changed. **Acceptance as measured, on one machine:** a built-in error is found down to tick, object and field, on the fixture and with real processes on a real map; a disconnected client rejoins and is hash-identical afterwards (a match of an hour with rejoins at minute 10, 30 and 55 and a late watcher: nine of nine clients in the same state, back in time after 0.6 to 1.6 s; on five lines at minute five 0.4 s to 21 s); a watcher follows on a delay of 5 s; the server's replay of that hour plays to the hash the clients agreed on; the session state stays JSON packed with Brotli (82 to 775 KB, written in 20 to 80 ms, read in at most 115 ms - inside the limits of the brief); the matrix again 36 of 36 lines hash-identical and 12 of 12 at 30 and 120 Hz and on a second map; under Linux (WSL) the reference manifest 50 of 50 and one match of Windows and Linux processes. **Tier 4 with 44 of 44 steps ok** at `8f6f94f` (5,194 s; 0 warnings; all test programs; simulation, game, presentation and network tests at 30, 60 and 120 Hz; both manifests identical; the rate-equivalence suite; the long run; the client checks; `check-match`; `session-check`; `net-probe`; `net-match`; the new `net-robust`, `net-join` and `net-cross-os`; the staged scenes). `NetTool probe-suite` 48 of 48. Campaign at 60 Hz (missions stay local): reachability clean 24/24 and 24/24, audit 24 of 24 identical on a second run and after save/restore with end probes 13/24 and 15/24, objective driver victory 24/24 and defeat 24/24 - as before the phase. Performance of the simulation, the whole `scripts/perf-profile.ps1` before (`f25fa7a`) and after: the same final hashes, means within -2.4 % and +2.2 % (8 players: 832 against 834 µs per tick, maximum 139 against 146 ms). **Known limits:** no second physical machine and no real network - the Linux runs are the same CPU; **votes are not built**; the partial sums are not in every hash report (asked for after a desync instead); the server's defaults hold no seat and delay no watcher (the owner's decision D-19; 5 minutes and 120 s are the proposal); a delay of 120 s was not run with real processes; no congestion control - on 256 kbit/s a state takes 15 s and delays the orders of both ends; the rejoin token travels in clear text until Phase 28 encrypts the connection; the replay is played by a tool, the client has no replay viewer; the window client has no lobby screen and its joined match was checked in a smoke test, seen by nobody.

Phase 28 "Server operation and security" (13 October 2026; `docs/NETWORK-PROTOCOL.md` §20 and §21, `docs/DEDICATED-SERVER.md` §9 to §13, `docs/SIMULATION-CORE.md` §49): transport version 2 (the handshake carries an ECDH exchange signed by the server's permanent key; AES-256-GCM per datagram with a number and a replay window; base class library only) and protocol version 4 (room verifier and player key in the hello, challenge and response for both, `Kick`, four reject reasons); limits of the transport and the room, each configuration with a counter and a defined reaction; a coordinate limit for a player's commands and the list of commands that are a player's orders (D-5 closed, no reference value moved); watchers decide nothing; the server's key file, ban file, logs with retention and shortened addresses, SIGTERM; the self-contained package for `win-x64` and `linux-x64` with a start guide; `NetTool load`, `seal-cost`, `net-match --server | --server-package`; the suite "Fuzz"; the checks `net-load`, `server-package`, `server-package-linux`. **Acceptance as measured:** the fuzz run found no crash and no growth in 33 million inputs (a crash by a key the fuzzer does not generate was found by a test and fixed); the limits take effect in tests; the connection is authenticated and encrypted, and somebody who records it can neither take a seat nor displace a connection; the server runs as a standalone package under Windows and, in WSL without a runtime in its environment, under Linux; the capacity table is measured for one machine; tier 4 with 47 of 47 steps ok at `c1946aa`. **Known limits, named:** the endurance run is 9 hours, not 24, and crossed no day's boundary; no second computer, no real network, no hostile load from outside; the first meeting of a client without a pinned fingerprint is unprotected; a player that lies about its hash ends the match (D-24); the limits' numbers are proposals (D-26); no listing of rooms, no lobby, no host in the client (Phase 29); nobody has seen any of it.

Phase 29 "Multiplayer in the client" (14 October 2026; `docs/MULTIPLAYER-FINDINGS.md`, `docs/GAME-SHELL.md` §15 and §16, `docs/GAME-HUD.md` §21, `docs/PRESENTATION-OBJECTS.md` §12, `docs/NETWORK-PROTOCOL.md` §22 and §23, `docs/DEDICATED-SERVER.md` §14): **built and checked on one machine, without a human.** Protocol version 5; `ServerCore` as the library of the dedicated program and of "Host game"; the multiplayer screens on the original's layouts (`NetworkDirectConnect.wnd`, `LanLobbyMenu.wnd`, `PopupHostGame.wnd`, `PopupJoinGame.wnd`, `LanGameOptionsMenu.wnd`, `LanMapSelectMenu.wnd`, `MultiplayerLoadScreen.wnd`); `NetHud`; interpolation of health, construction progress, wheel travel and chassis pose. **Acceptance statement, honestly:** the brief's acceptance ("humans have worked through the lines of the checklist") is **not met** - no human took part. In its place: `ShellScript` drives the real client through the screens (`check-net-lobby`, `check-net-acceptance`), `NetScreenTests` and `NetHudTests` pin 474 pictures and read their texts, `NetStats` measures a click. Of the thresholds fixed before the measurement T1, T4 and T5 hold and T3 was not met; the extrapolation T3 prescribed is built, measured without effect and off. Tier 4 with 49 of 49 steps ok at the state of `a3cfef6` (5,678 s; 0 warnings; all test programs; simulation, game, presentation and network tests at 30, 60 and 120 Hz; both manifests identical; the rate-equivalence suite; the long run; the client checks; `check-match`; `session-check`; `net-probe`; `net-match`, `net-robust`, `net-join`, `net-cross-os`, `net-load`, `server-package`, `server-package-linux`; the new `net-lobby` and `net-acceptance`; the staged scenes) - the second run: the first stopped at the audit of the packages, which found five advisories published since Phase 28 against `System.Security.Cryptography.Xml` and `.Pkcs` 10.0.12 of the Stride tooling (pinned to 10.0.13 in `a3cfef6`). **Known limits:** a window client cannot come back into a running match; no watcher's way and no fog choice through the menus; no button to remove a member; no start position box; English texts only; one machine, no cable, no second CPU.

Phase 30 "Patch integration, quality, shippable completion" (14 October 2026; `docs/ACCEPTANCE-MACHINE.md`, `docs/GGC-FIXES.md`, `docs/PATCH-LAYER.md`, `docs/TROUBLESHOOTING.md`, `docs/COMPATIBILITY.md`, `docs/THIRD-PARTY.md`): **done by machine, on one machine, without a human.** The rule profile and three adopted fixes of GeneralsGameCode (the original profile and every reference value unchanged), the generated catalogue of 354 fixes (83 CRC-gated ones not yet reviewed), the patch layer as an import of its own (its definitions do not compile yet: four colour lines and two engine modules), crash reports, run logs and support bundles for client and server, the self-contained client package and the proof that no package holds original data, the licence list, the clean-room build, the version rule as a proposal (D-31). **Acceptance statement, honestly:** the brief's acceptance ("accepted end to end by humans") is **not met** - on the owner's instruction every acceptance was done by machine; `docs/ACCEPTANCE-MACHINE.md` names per item what a program checked and what only a human can judge. Tier 4 with 53 of 53 steps ok at `dfcd72e` (6,602 s; 0 warnings; all test programs; simulation, game, presentation and network tests at 30, 60 and 120 Hz; both manifests identical; the rate-equivalence suite with 0 / 0 in all ten groups - 689 cases, 4,808 quantities; the long run; the client checks; `check-match`; `session-check`; `net-probe`; `net-match`, `net-robust`, `net-join`, `net-cross-os`, `net-load`, `net-lobby`, `net-acceptance`, `server-package`, `server-package-linux`; the new `diagnostics`, `client-package`, `licences` and `ggc-catalogue`; the staged scenes). **Known limits:** no shadows, no sky in `MD_USA01`'s intro, no debris of creation lists, the model viewer's Crusader; the patch layer not playable; a second machine, a machine without .NET and a write-protected installation directory not tried.

Phase 34 "S3-M6: the movement profile, the fidelity corrections F-34, F-38, F-39" (16 October 2026; `docs/PERF-MOVEMENT.md` §9 and §10, `docs/SIMULATION-CORE.md` section 52, `docs/PERFORMANCE.md` 6d, `docs/GGC-FIXES.md` 3.1d): **done by machine, on one machine, without a human; the tick maximum target is missed.** `--rules movement-budget` (not the default, D-3) gives ground units a path queue with 60,000 expanded cells per logic frame of the original, searches that pause and continue, and one search per group order. Thresholds committed before the first change: M1 (longest tick after 300 at most 8 ms) missed with 13.7 / 13.8 ms (the original 123 / 128 ms), M2 (mean at most 5 % above the original) holds with -15 %, M3 (the original keeps both manifests) holds. The rate-equivalence suite under the profile passes in 9 of 10 groups (one case of the angry mob outside, cause not found); the AI survey under both profiles is reported, not judged. F-34, F-38 and F-39 are corrected in the original profile, each with its manifest commit where the manifest moved (F-39 moved nothing), and `door-exit-to-rally`, `bike-trainable-from-rider` and `enter-needs-vertical-overlap` are adopted behind the rule profile. Tier 4 with 54 of 54 steps ok at `b6b5764` (6,363 s; 0 warnings; all test programs; simulation, game, presentation and network tests at 30, 60 and 120 Hz; both manifests identical; the rate-equivalence suite under the original with 0 / 0; the long run; the client checks; every network step; `patch-layer`; the staged scenes). **Known limits:** the tick maximum; the angry-mob case; no human has watched a group march under the profile.

Phase 33 "S3-M1: tactics profiles and strengths of the built-in AI" (15 October 2026; `docs/SKIRMISH-AI.md`, `docs/SIMULATION-CORE.md` section 51, `docs/GGC-FIXES.md` 3.1c): **done by machine, on one machine, without a human.** Every computer slot names a tactics profile and a strength (match description encoding 3, protocol 6, skirmish session state 3; shell, client `--tactics`, tools `--tactics`/`--strength`); the strength is obeyed and changes only what the AI may know and how fast it decides. The rule "no standing still" decides the two stalemated manifest pairings (16 of 16; 307 instead of 290 of 320 survey matches decided). The survey `ai-profiles` (768 matches) met 12 of 13 thresholds fixed before it ran; the first design of the strengths missed them the wrong way round and was set again by measurement. Five more community fixes behind the rule profile (16 in all); every CRC-gated fix decided. Tier 4 with 54 of 54 steps ok at `6c60147` (6,469 s; 0 warnings; all test programs; simulation, game, presentation and network tests at 30, 60 and 120 Hz; both manifests identical; the rate-equivalence suite; the long run; the client checks; every network step; `patch-layer`; the staged scenes). **Known limits:** balance not assessed; rush's first attack misses its threshold; profiles only at medium strength in the shell; the network room offers one computer army; no human has watched a profile.

Phase 32 "S3-M7: the patch layer playable, the fixes reviewed" (15 October 2026; `docs/PATCH-LAYER.md` 2a, `docs/GGC-FIXES.md` 3.1b to 3.3, `docs/SIMULATION-CORE.md` section 50): **done by machine, on one machine, without a human.** The patch layer's definitions compile (colour keyframes with an ignored token as a counted compatibility case; `StatusBitsUpgrade` and `SabotageSupplyDropzoneCrateCollide` implemented; `baselines/compatibility-patch.json`, 706 entries); the layer plays deterministically and is kept apart from the original by its definition fingerprint on a server (`scripts/check-patch-layer.ps1`). All 96 CRC-gated markers of GeneralsGameCode reviewed; eleven fixes adopted behind the rule profile, eight candidates deferred with reasons, three undecided. Tier 4 with 54 of 54 steps ok at `8d57fb7` (6,340 s; 0 warnings; all test programs; simulation, game, presentation and network tests at 30, 60 and 120 Hz; both manifests identical; the rate-equivalence suite; the long run; the client checks; every network step; the new `patch-layer`; the staged scenes) - the second run: the first stopped at `ggc-catalogue`, because the catalogue script still named the booby-trap fix by its old enum member (`8d57fb7`). **Known limits:** balance not assessed; the patch's converted parts (textures, models, string table) are not in the layer; no human has played the layer.

**Plan change of 5 October 2026:** after `docs/handoffs/HANDOFF-PHASE17.md` follows step 10 (tick rate, fixed point, multiplayer; `docs/MULTIPLAYER-PLAN.md`, `docs/handoffs/HANDOFF-PHASE18.md` to `docs/handoffs/HANDOFF-PHASE29.md`), after that step 11 (`docs/handoffs/HANDOFF-PHASE30.md`). The briefs 18 to 30 already exist; every phase continues the "Starting point" of the next brief at its end instead of rewriting it.
