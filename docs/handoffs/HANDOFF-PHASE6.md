# N+1 handoff: Phase 5 – remaining scope, then Phase 6 (world, terrain, movement, vision, skirmish AI)

> **Superseded by `docs/handoffs/HANDOFF-PHASE7.md`** (29 September 2026, completion of Phase 5). Part A of this document is done (supply,
> experience, teams, repair, area damage, further module unlocks); what remained open were Block 0.4, the construction process, radar/command set effect and terrain.
> The document is kept as history; the numbers below are outdated, current ones are in `CLAUDE.md` and in the new handoff.

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md`, `README.md`,
`docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, `docs/IMPLEMENTATION-PLAN.md`, `docs/GAME-RULES.md` and `docs/SIMULATION-CORE.md`.
`docs/handoffs/HANDOFF-PHASE5.md` is kept as the history of the Phase 5 blocks (the numbers there are outdated).

## Honest status of Phase 5

Phase 5 is **not fully accepted**. The acceptance criterion ("headless reference match of all factions without special paths"
and golden master scenarios against known original values) is only partly met:

- Achieved: economy, sciences, upgrades, direct combat, simple movement, production, garrison/transport, creation lists,
  crates, special power subsystem, "last survivor" as victory. The reference match runs on retail data with all 13 factions
  and their **real command centres**, deterministically, with replay and snapshot continuation.
- Special paths in the reference match: 13 start objects are skipped (9 dozers, 4 GLA workers, because their AI modules are missing), there is
  no supply (money only from starting capital and bounty/crates) and the generator issues no `UseSpecialPowerCommand`.
- Missing: teams/diplomacy, supply/transport of the economy, repair/selling, radar effect, area damage, experience and
  veteran-based `ArmorSet`/`WeaponSet` selection, dozer/worker AI, command set effect.
- Missing: golden master for supply and experience; Block 0.4 (RNG comparison with a compiled original, no compiler available).
- Deliberate simplifications of the special powers (`docs/GAME-RULES.md` §6d): no construction model, no disabled state, no
  `createOwner=false`, no `OCLAdjustPositionToPassable` adjustment, no gunship approach/AI, no view objects, no EVA/sound,
  no script notifications, bounty without a team model, `SpecialPowerCompletionDie` is inert, the three edge positions
  (`CREATE_AT_EDGE_*`) create nothing (`CreationSkipped(NoTerrainEdge)`), but trigger power and reload.

Decision for the next pass: **first close the Phase 5 remainder, as far as it is possible without terrain**
(section "Part A"), then begin Phase 6 (Part B). What needs terrain/collision belongs after Phase 6, not into
Phase 5 substitute solutions.

## Reproduction basis (as of the end of the special power block; re-measure before continuing to work)

Commits of the last block: `5f34b41` (schema/catalogue/baseline), `9efbf2c` (subsystem, command, modules), `1de0b6c` (tests);
after that the documentation. All local, no remote.

- Build: 0 errors, exactly the 10 known NU1901/NU1903 warnings. All test programs green, also `--long` in Release.
- Full inventory: 228 INI files (28 excluded), 65,875 blocks, 12,574 definitions, 16,380 modules, 333,061 keys,
  18,050/19,600 references resolved (1,550 fallbacks), snapshot 144,745,050 bytes, SHA-256
  `D0C06A2B06CD7065474A3EB729E36B87E1815DAE2D461E564D023EAA019295CF`.
- Retail base (`--maps exclude`): 146 files (110 excluded), 63,747 blocks, 12,550 definitions, 16,065 modules,
  327,293 keys, 2,115 objects, 17,658/19,181 references, SHA-256
  `7110C5BA69917279ADD6B0270754AA081ADEF2DC37166FFB598CAF2313F979DA`.
- Compatibility baseline 761 entries (full inventory: deferred-field 519 entries/20,936 uses, definition 36/2,590,
  diagnostic 9/4,907, duplicate-key 24/120, excluded-file 28/28, module 145/4,441); `compat-check` passes.
- Module catalogue 205 types, **60 implemented** (12 Phase 4, 24 + 14 + 10 Phase 5); instantiability **1,224 of 2,115
  (57.9 %)**, 891 blocked; module uses 15,078 of 19,553 (77.1 %). Asset runtime content 25,682 files.
- Stress test (`SimTool stress`, 216,000 ticks): final hash `4A99EE46FD623A09`, chain hash `76A8FCEEEE625672`, 914,394 commands,
  peak 15,775 entities, snapshot at tick 108,000 with 10,262,573 bytes, restore hash `1D2E96E0BB12303A`, 0 B/tick when idle.
- Reference match (`SimTool reference-match`, retail): 13 factions, 13,978 commands (625 production, 2,153 movement, 10,875
  shots), winner player 1 (America) at tick 11,385 (6.3 min), replay 11,686 ticks/462,194 bytes/11 checkpoints, 43
  `UnknownEntity` rejections, final hash `83EBD54549BB1DB5`, chain hash `928D78E6DD172836`, continuation from tick 5,843 identical.
- Commands: 17 types, `CommandTypes.Version = 5`, replay format v2; rejection codes 1–29 (25–29 special power).

## Part A – rest of Phase 5 (in this order; each with tests, real-data audit, docs, two runs)

Before every step measure with `SimTool audit-coverage .local/phase2-import-a` what a module unlocks **on its own**. State
(objects in total / only this module missing): `CommandButtonHuntUpdate` 79/10, `ToppleUpdate` 76/66, `SpecialAbilityUpdate` 64/2,
`JetSlowDeathBehavior` 59/0, `MissileAIUpdate` 44/29, `CrushDie` 42/39, `CountermeasuresBehavior` 40/0, `FireWeaponUpdate` 36/26,
`SpawnBehavior` 35/6, `AutoFindHealingUpdate` 34/10, `JetAIUpdate` 34/2, `HeightDieUpdate` 31/15, `DeliverPayloadAIUpdate` 28/1,
`MobMemberSlavedUpdate` 27/27, `RailroadBehavior` 25/25, `TransportAIUpdate` 24/13, `BaikonurLaunchPower` 23/23,
`ReplaceObjectUpgrade` 20/16, `SlavedUpdate` 20/7, `RepairDockUpdate` 17/16, `DozerAIUpdate` 10/10.

1. **Supply economy** (`SupplyCenterDockUpdate`, `SupplyWarehouseDockUpdate`, `SupplyTruckAIUpdate`, `WorkerAIUpdate`,
   `DozerAIUpdate`, `DockUpdate` base): first read the requirements on the original (`references/`), then build it as a
   dock/economy model of our own with catalogue schemas. After that put in the 13 skipped start objects of the reference match and
   add a golden master for supply income with an independent recalculation. That is the largest open item for the
   acceptance criterion "without special paths".
2. **Experience and veteran levels**: awarding experience on a kill, `ExperienceTracker`, `ArmorSet`/`WeaponSet` selection by
   level, golden master.
3. **Teams/diplomacy** as a data model (lift bounty and victory rule to teams, see "bounty without a team model").
4. **Repair/selling** (`RepairDockUpdate`, `AutoFindHealingUpdate` – caution: the fixture `Blocked` in
   `tests/CnCNew.Simulation.Tests/Tests.Basics.cs` uses `AutoFindHealingUpdate` as an example of a module that is not
   implemented; switch to another one as soon as it is implemented).
5. **Further module unlocks after measurement**, without terrain/collision: `CommandButtonHuntUpdate`, `SpecialAbilityUpdate`
   (handler of the unit abilities, only through it do the `SpecialAbility` powers become useful in the command stream),
   `MissileAIUpdate`, `FireWeaponUpdate`, `ReplaceObjectUpgrade`, `CountermeasuresBehavior`, `JetAIUpdate`. Extend the
   reference match generator by `UseSpecialPowerCommand` as soon as sensible targets exist.
6. **Radar effect, area damage**, command set effect (which buttons an object offers).
7. Block 0.4 (RNG comparison) stays open as long as no compiled original is available; do not guess.

Only when 1–3 are in place and the reference match runs without skipped start objects may Phase 5 be documented as
completed; if limits remain, name them honestly in `IMPLEMENTATION-PLAN.md`.

## Part B – Phase 6: world, terrain, movement, vision, skirmish AI

Scope according to `docs/IMPLEMENTATION-PLAN.md` §6: MAP data to a playable world (heightfield, water, bridges, waypoints,
trigger areas, props), footprints, collision, ground types, formations, deterministic pathfinding with local avoidance,
vision range/shroud/fog/stealth, skirmish AI (build plans, attack, defence, ability use). With that the
terrain/physics modules also become possible that deliberately stayed open in Phase 5 (`ToppleUpdate` 76/66, `CrushDie` 42/39,
`HeightDieUpdate` 31/15, real `PhysicsBehavior` forces, `OCLAdjustPositionToPassable`, `CREATE_AT_EDGE_*` positions).

Rules for the layering: terrain and navigation as a data model in `CnCNew.Data`/`CnCNew.Simulation` (integer/fixed point,
no floating-point numbers, observe `SourceRules`); map formats via `CnCNew.Formats`/`CnCNew.Import`, run-time access only via
`CnCNew.Content`. The map overlays of the full inventory measurement (`--maps include`) are the entry point.

## Non-negotiable rules (unchanged, see `CLAUDE.md`)

- Unknown keys/modules/enums/references never fall back silently to defaults; every case is explicit, counted,
  documented and secured against `baselines/compatibility.json`.
- Determinism: canonical sorting, no wall-clock time, no dependency on `Dictionary`/`HashSet` order; two independent
  runs byte-identical. `CnCNew.Simulation` depends only on `CnCNew.Data` (`SourceRules` test); every `IUpdateModule` contains
  `nextWake`.
- `TreatWarningsAsErrors=true`; only the ten known NU1901/NU1903 warnings are explained.
- Always re-measure inventory numbers, never adopt them from docs or this handoff.
- Third-party code may be read, not copied unexamined; every ported algorithm needs a source reference (file,
  function).
- New commands increase `CommandTypes.Version` (currently 5) and get codes of their own; existing ones never change.
- Work only locally, no remote, push nothing; several commits each closed in subject matter; working tree clean at the end;
  at the end of this pass write and commit `docs/handoffs/HANDOFF-PHASE7.md` (or a new continuation brief).

## Final report

Numbers (with commit hashes), implemented rules/modules, determinism, test, stress and replay results,
module coverage and instantiation rate before/after with blocker list, golden master derivations, known limits and
technical debt, `git status` and the confirmation "no remote, nothing pushed".
