# N+1 handoff: Phase 4 – deterministic simulation core

> **Status: completed on 28 September 2026** (results in `docs/SIMULATION-CORE.md`, `docs/TYPED-DEFINITIONS.md`,
> `docs/IMPLEMENTATION-PLAN.md`). The next brief is in `docs/handoffs/HANDOFF-PHASE5.md`. This document is kept as history.

You work autonomously on this repository (`<repo>` = root of the working directory).

## Brief

Implement Phase 4 of the implementation plan completely: a headless running,
deterministic simulation core (`CnCNew.Simulation`) with a fixed tick, stable IDs,
reproducible RNG streams, command queue, object/module/behaviour life cycle, canonical
state snapshots, replay harness and stress test. Before that a small **Block 0** closes the gaps
from Phase 3 that are necessary for Phase 4 (see below).

Before making changes read completely `CLAUDE.md` (binding rules, commands, Git rules),
`README.md`, `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, `docs/IMPLEMENTATION-PLAN.md`,
`docs/CONTENT-PIPELINE.md`, `docs/TYPED-DEFINITIONS.md` (incl. "Known limits") and
`docs/PHASE3-COMPATIBILITY.md`.

## Binding starting state

- Branch `main`, no remote; push nothing, create no remote. Phases 1–3 are completed
  (last Phase 3 commits `07b5959`, `a030a98`, docs `ae71e55`, `ebe04fe`).
- If `CLAUDE.md`, `AGENTS.md`, `docs/handoffs/HANDOFF-PHASE4.md` and the doc addenda to
  `docs/TYPED-DEFINITIONS.md` / `docs/IMPLEMENTATION-PLAN.md` are still unversioned: first
  check them in as a commit of their own `docs: add agent guidance and phase 4 handoff`.
- Verified on 28 September 2026: solution build (only the 10 known transitive
  Stride NU1901/NU1903 warnings) and all eight test programs green; `AssetTool verify`,
  `IniTool audit`, `ImageTool audit-tga`, `W3dTool audit` green.
- Fresh full compilation: 256/256 INIs, 66,623 blocks, 12,677 definitions, 16,360 modules,
  335,592 keys, 12,441/12,468 references resolved (27 visible compatibility cases: 25
  veterancy pseudo-upgrades, `DieByFireUSA`, `Boss_VehicleSentryDrone`), 41 definition domains
  (2,801 instances) and 206 modules inventoried, snapshot 112,664,082 bytes, SHA-256
  `14154C512DED47C4AD72B651F8FCB0BA702EACBF8678C51DDAB7D125BEC675D4`. Re-measure everything.
- Number of definitions per kind: Object 2,127, Weapon 364, Armor 53, Damage 11, Upgrade 82,
  Science 96, Faction 15, CommandButton 820, CommandSet 479, FxList 429, Fx 1,087,
  AudioEvent 1,410, DialogEvent 2,569, MusicTrack 69, Locomotor 184, SpecialPower 80,
  GameData 1, others (Compatibility) 2,801.

## Architecture boundaries

- `CnCNew.Simulation` depends only on `CnCNew.Data`. No Stride, DirectX, UI, content,
  archive, filesystem or network access; no `IniNode`, no raw INI strings.
- No wall-clock time (`DateTime`, `Stopwatch`, `Environment.TickCount`) in the core, no threads in the
  tick, no `Random.Shared`, no `Dictionary`/`HashSet` iteration order in
  state-relevant logic, no reflection order, no unbound `float`/`double`
  in the state (see numerics).
- The presentation later reads only immutable snapshots and writes only typed commands.
- Unknown modules must not be instantiated. Existing modules that are not implemented
  remain visible, non-executable compatibility cases.

## Block 0: Phase 3 hardening (only as far as necessary for Phase 4)

The re-examination of Phase 3 confirmed numbers and determinism but found gaps in substance
(`docs/TYPED-DEFINITIONS.md`, "Known limits"). Close exactly these items beforehand; everything
beyond that (domain-specific types for Weapon/Armor/Locomotor/… ) is advance work for
Phase 5 and is only documented as debt with numbers. Block 0 must not displace Phase 4.

0.1 **Compatibility list as a checked baseline.** So far the compiler automatically counts every
    unregistered module/definition name; the list is fixed nowhere.
    Check in a small, reviewed baseline (names, owner kinds, frequencies, target phase;
    no asset contents) and add `IniTool compat-check`, which ends with exit code ≠ 0 on every deviation (new,
    missing, entry changed in frequency). In addition a test with
    synthetic data. A typo in the module name must be distinguishable from a known,
    not yet implemented module.
0.2 **Module binding.** `ModuleDescriptor.Factory` is `Func<ModuleConfiguration, object>` –
    untyped and not bindable in Data without Data knowing the simulation. Design the
    separation cleanly: Data keeps the descriptors (name, owner kinds, field schema,
    `CompatibilityOnly`, target phase) including all 206 existing modules as explicitly known,
    not implemented entries; `CnCNew.Simulation` owns an explicit, sorted,
    reflection-free binding table module name → typed factory and checks it at start against the
    Data descriptors (unknown name, wrong owner, duplicate = error). The strict mode
    (`StrictUnknownModules`) must pass on the existing data and fail on every new name.
    Test: catalogue == module set observed in the full content.
0.3 **Value grammar for what Phase 4 reads.** First measure the real forms in the content
    (known: `R:255 G:0 B:0 [A:..]` 9,150×, `X:0 Y:0 Z:0` 6,161×, `KindOf = A B C` 1,964×,
    repeated `Locomotor = SET_NORMAL <Name>`). Implement real `Color`, `Vector2/3`,
    `Enum` and `Flags` values with strict name tables that are derived from the reference and
    cited there (e.g. `KindOfMaskType` names); an unknown token is an error, not
    `Text`. Scope: all fields that the Phase 4 modules and the object instantiation read
    (at least `KindOf`, side/geometry basics, module fields from 0.2). The rest → Phase 5.
    Validate mandatory fields and key schemas for these fields; unknown keys on
    such definitions are errors or are in an explicit, counted list.
0.4 **Load order and multiple definitions.** Reconcile the order of the compiler with
    `references/ea-generals-zero-hour/GeneralsMD/Code/GameEngine/Source/Common/GameEngine.cpp`
    (lines approx. 363–532: `initSubsystem` with default/main INI and `Data\INI\Object`) as well as the
    load kinds `INI_LOAD_OVERWRITE`/`CREATE_OVERRIDES`/`MULTIFILE` in `INI.cpp`. Produce a
    report of all definitions that occur in more than one file (with file order), and
    prove that the compiler order yields the same winners as the original; otherwise
    correct it. In addition check which keys occur several times within a definition header
    and whether overwriting (`fields.RemoveAll`) is correct in substance for them;
    repeatable keys keep their multiplicity and order.
0.5 **Add missing Phase 3 tests** (small, synthetic): definition IDs and snapshots
    independent of the insertion or `EnumerateIniFiles` order respectively; truncated
    inputs in the compiler; wrongly typed module field value; enum/flags/color/vector error cases;
    two full compilations synthetically byte-identical.

Block 0 ends with a commit of its own and updated numbers; the snapshot hash changes
as expected – explain every deviation (field kind counters, number of `Text`/`List` before/after)
and document the new reference hash.

## Complete scope of Phase 4

New projects: `src/CnCNew.Simulation`, `tests/CnCNew.Simulation.Tests` (console program in the style
of the other tests), `tools/CnCNew.SimTool` (replay, hash, stress test, audit); enter all of them in
`CnCNew.slnx`.

1. **Tick and numerics.** Fixed logic rate 30 Hz (`LOGICFRAMES_PER_SECOND`,
   `GameCommon.h:72`). Use integer or fixed-point arithmetic (justify the choice; e.g.
   Q format with `long`) for position, speed, angle and damage; no `float`/`double`
   in the state. Define conversions from the Data values (`GameTime` in ms, speed per
   second, angle in degrees, percent) into ticks, distance per tick and fixed point explicitly –
   including the rounding rule. For this check `ConvertDurationFromMsecsToFrames` and relatives in
   `GameCommon.h:80–107` as well as the duration/speed parsers in `INI.cpp` and document
   what is adopted and what deviates. Limit values (overflow, negative values, 0 ms) are tested.
2. **IDs and order.** Monotonic, deterministic world/entity/player/command/event/timer IDs
   (check entity ID assignment and invalid ID 0 against `GameLogic.cpp`). Canonical
   update order independent of hash or thread order. Check the "sleepy update"
   model of the original (`UpdateModule` with wake frame, `UPDATE_SLEEP_FOREVER`, sorting by
   wake frame then object ID) and adopt it or justify the deviation.
3. **RNG.** Named streams (at least `logic` and a `cosmetic` stream that is irrelevant
   for the logic) with a fixed algorithm, documented seed derivation, serialisable
   state and golden sequences. Check `RandomValue.cpp` of the reference and implement the
   `logic` stream true to the original or justify a deliberate deviation. Recalculate golden values
   independently (compile the reference only if a compiler is available; otherwise a
   documented, independent hand calculation). No `Random.Shared`.
4. **World and life cycle.** Entity storage, spawn/destroy transactions, module ownership, events,
   timers and clearly separated tick phases (e.g. commands → updates → events → destroy → spawn →
   hash). Mutations during an iteration run via deterministic queues; an object destroyed at the end of
   the tick receives no further updates in this tick, but its events are
   delivered in a defined way. Document the phase order.
5. **Modules.** From the registration (0.2) implement the minimal life cycle modules that create,
   tick, deliver events to and destroy a synthetic object: choose on the basis of the
   frequencies in the content (`ActiveBody`, `InactiveBody`, `ImmortalBody`, `StructureBody`,
   `LifetimeUpdate`, `DeletionUpdate`, `DestroyDie`, `InstantDeathBehavior`, … – back the selection
   with numbers). Every implemented module has a strict field schema from the real INIs.
   No presentation modules (`W3D*Draw`, `*ClientUpdate`) in the simulation. In addition produce
   an audit "module coverage": how many of the 2,127 objects can be completely
   instantiated today, how many fail on which not implemented module (frequency list).
6. **Commands.** Typed, tick-addressed command queue with a versioned command type table,
   validated player/entity assignment, stable order at the same tick (player ID,
   then command sequence) and explicit, logged rejection of late, duplicate or
   invalid commands. Phase 4 uses only synthetic commands (e.g. spawn, destroy, damage,
   set timer); gameplay commands follow in Phase 5.
7. **Snapshots and hashes.** Canonical, versioned, machine-readable world snapshot without
   absolute paths, timestamps or unordered collections, plus a fast state hash per tick
   (justify the algorithm; a new package dependency only with justification, preferably BCL/
   `System.IO.Hashing`) and SHA-256 for complete snapshots. Restore only if the contract
   is stable without anticipating Phase 9; the continuation run (item 8) needs it, however – then keep
   restore minimal and internal.
8. **Replay harness.** Versioned replay format: header (schema version, tick rate,
   fingerprint of the definition graph, fixture/map identifier, seed) + tick-addressed
   commands + periodic checkpoint hashes. The fingerprint of the definitions must come from the
   canonical snapshot or a streaming hash; a replay on deviating
   definitions is rejected. Two fresh runs and a snapshot/continuation run yield
   identical tick hashes over at least 216,000 ticks (two hours of game time). Damaged,
   truncated or foreign-version snapshots/replays are rejected.
9. **Memory and stress.** Headless stress test with at least 10,000 simultaneously living
   entities, at least 100,000 spawn/destroy operations, many timers, events and commands
   over at least 216,000 ticks. Check leaks (entity/module/timer/event tables return to the
   initial state, allocations per tick measured in the steady state), deadlocks and
   non-determinism. Define a justified run time and memory budget and document the
   measured values together with the hardware.
10. **Docs.** Update README, architecture, roadmap, implementation plan; new
    `docs/SIMULATION-CORE.md` with tick phases, numerics, RNG, ID/queue order, snapshot and
    replay format, module binding, module coverage, budgets and acceptance commands; document the compatibility
    baseline and Block 0 results; update `CLAUDE.md` if needed.

## Minimum tests

- Block 0: all items from 0.1–0.5.
- Tick rate, time conversion, fixed-point rounding and limit values;
- ID stability and canonical iteration order, in particular with permuted
  insertion order;
- Golden RNG sequences, separate streams, seed derivation, state round trip;
- Spawn, tick, event, timer, destroy and mutation during a tick;
- Binding table: factory, wrong owner, duplicate, unknown and not implemented module;
- Command order, duplicates, late and invalid commands;
- identical snapshots/hashes with different insertion orders;
- replay equality over long runs and after snapshot continuation;
- damaged snapshots/replays, version and definition fingerprint errors;
- stress test without leaks, deadlocks or deviation;
- an optional real-data test (`--content-dir .local/phase2-import-a`) that instantiates only
  existing objects compiled via `CnCNew.Data`.

Fixtures are small, synthetic and legally unobjectionable; tests of the simulation compile
synthetic INIs via an in-memory `IDefinitionSource`. New warnings and silent fallbacks
count as errors until explained.

## Definition of Done

Phase 4 is only completed when:

- solution build and all existing and new tests are green (warnings: only the known
  NU1901/NU1903);
- Block 0 is completely implemented, `compat-check` passes against the baseline and the strict
  module mode passes on the existing data;
- identical definitions/fixture/seed/commands yield bit-identical tick hashes over long runs,
  also after snapshot continuation;
- world/entity/command/event orders are specified and tested;
- the module contract is bound without reflection and without engine types and `CnCNew.Simulation`
  depends only on `CnCNew.Data`;
- memory and headless stress test pass and budgets are documented;
- documentation and code agree and the implementation plan honestly names known limits;
- the working tree is clean, all changes are present in several commits each closed in subject matter,
  no original assets, local snapshots, manifests, captures, build outputs or IDE data
  were committed and no remote was created or anything pushed.

## Final report and N+1

Report: Block 0 results (with new numbers and reference hash), implementation,
determinism rules, test/stress/replay results together with hash values, module coverage and
object instantiation rate, known limits and technical debt, all commit hashes,
final Git status as well as the confirmation "no remote, nothing pushed".

After that produce as `docs/handoffs/HANDOFF-PHASE5.md` a self-contained N+1 handoff for Phase 5
"Game rules, combat, economy and production" – including the remaining debt in
domain-specific definition types – and commit it. If Phase 4 is not complete,
produce a precise continuation prompt for Phase 4 instead.
