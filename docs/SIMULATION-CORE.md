# Deterministic simulation core (Phase 4, extended in Phases 5 and 6)

As of: 30 September 2026 (Phase 6 completed, Phase 8 package E1: projectile flight time, transports, tunnels, Chinook; §1–§12 describe the core and the Phase 5 rules, `docs/GAME-RULES.md`
the game rules, §13–§20 the Phase 6 extensions: terrain, navigation, vision, construction, power plants, skirmish AI, §21 package E1)

`CnCNew.Simulation` is the headless, deterministic world: fixed tick, stable IDs,
reproducible random numbers, command queue, object/module life cycle, timers, events,
canonical snapshots, state hashes and a replay harness. The project depends exclusively on
`CnCNew.Data` (no Stride, no UI, no content/archive/file system access). A test
(`SourceRules`) enforces this mechanically: forbidden constructs in the source text (wall-clock time, ambient
random numbers, `float`/`double`, threads, file access, reflection) and every assembly reference other than
`CnCNew.Data` and the BCL make the test fail.

**Note (9 October 2026, Phase 23):** since `e8632bb` the simulation runs at **60 Hz** by default. Hash values, winning ticks, tick budgets and run times in the sections before §43 are copies of the respective state at 30 Hz and **historical**; authoritative is `baselines/reference-manifest.json` (60 Hz, schema 2), the current values are in §43.9.

The domain foundations of the definition data (block 0 of the phase) are in `TYPED-DEFINITIONS.md` and
`PHASE3-COMPATIBILITY.md`; this document describes the core itself.

## 1. Tick and numerics

### Time and units

The tick rate is a **value of the world** (Phase 20; details, counts and the open lists in `docs/TICKRATE-MIGRATION.md`):

- **Rate:** `WorldConfiguration.Rate` of type `SimulationRate`. Exactly 30, 60 and 120 Hz (30 · 2ᵏ) are supported; everything else is rejected when
  a world, catalog, snapshot or replay is created. The catalog (`TemplateCatalog.Build(…, rate, …)`) is **built for one rate**
  – it contains ticks and values per tick –, and a world rejects a catalog of another rate. Until Phase 23 all sessions ran at
  30 Hz. **Since Phase 23 (`e8632bb`, 8 October 2026) the default of a session is 60 Hz**: the one definition is
  `ProductRate.TicksPerSecond` in `CnCNew.Game` (from it `GameData.DefaultSessionRate`, client, tools and tests). The simulation itself
  knows no default – whoever creates a world names the rate –, and the rate remains a value of the world. 30 and 120 Hz remain
  supported and are checked against 30 Hz by the rate-equivalence suite (§39); above 30 Hz the frame convention from §43 applies
  ("the 30 Hz step is the definition, higher rates subdivide it"). What is recorded at a rate carries its rate: replays, snapshots and
  savegames are rejected by a session of another rate, a manifest is checked with the rate of its file.
- **Three units for time.** *Tick*: one step of the simulation (`SimulationWorld.Tick` is the number of executed ticks and at the same time
  the number of the next one); state, snapshots, replays and commands carry ticks of the rate of the world. *Original frame*: 1/30 s
  (`LOGICFRAMES_PER_SECOND`, `GameCommon.h:72`; `SimulationRate.OriginalFramesPerSecond` is the only named 30 of the simulation) –
  the unit of all constants of the original, of all own deadlines and rhythms and of the frame counters of the map scripts. *Seconds and
  milliseconds*: the unit of the data. There is no wall-clock time in the core.
- **Grid rule:** durations stay on the 30 Hz grid of the original. A duration from the data is rounded up to whole frames as in the original
  and then multiplied by rate/30 (a reload time of 50 ms lasts two frames at every rate); a constant in frames
  is multiplied by rate/30; whatever is rounded, clamped, counted or rolled at run time is computed in frames and only the
  result is scaled (`FrameGrid`: `AtLeastOneFrame`, `CountdownReload`, `DrawFrames`, `FloorToFrame`). Quantities per second are divided by the
  rate or its square, additive raw values per frame by rate/30 or its square.
- **One way:** every conversion goes through the time service `SimulationTime` – at run time `world.Time`, during the catalog build the time service of the
  rate (`configuration.Time`, `timing`). No module computes with a rate. Literals that mean order ("in the next tick") are
  written with `TickOrder.Next` and never scaled; `SimulationTiming` now only contains what does not depend on the rate (`SleepNone`,
  `SleepForever`, degrees → radians, ratios). The test `SourceRules` forbids bare tick numbers in the simulation
  (`docs/TICKRATE-MIGRATION.md` §10).
- **Not done:** correct behaviour at 60 and 120 Hz. The simulation runs through there and is deterministic
  (`docs/TICKRATE-MIGRATION.md` §11); integration per tick, multiplicative factors per frame and sampling per tick are listed for
  Phase 23 in §5 there.

- **Kind of number:** state consists only of integers and `Fixed`. Since Phase 22 `Fixed` is a Q39.24 fixed point in
  a `long` (24 fractional bits, resolution 2⁻²⁴ ≈ 6·10⁻⁸ world units, range ±5.5·10^11; until Phase 22: Q47.16,
  resolution 2⁻¹⁶, range ±1.4·10^14 – all raw values, hashes and format versions in the sections before §38 come
  from that time and are superseded). The number of fractional bits is in exactly one place (`Fixed.FractionBits`);
  the arithmetic (`FixedMath`) is derived for a number of bits, not for sixteen (§38, `docs/FIXED-POINT-Q39-24.md`).
  Reason for the kind of number: the original keeps
  binary32 `Real` in the state and sets `setFPMode()` for it (`INI::load`); integer arithmetic is bit-identical on every
  platform. Products and quotients are exactly those of the 128-bit formulas. A value above 741,455 must not be squared
  (its square leaves the range and throws).
- **Rounding rule (everywhere):** to the nearest number, half values away from zero. Overflow throws `OverflowException`;
  nothing wraps or saturates silently. Division by zero throws.
- **Conversions** (`SimulationTime`; R = rate, f = R/30). Rounding and order per function are in
  `docs/TICKRATE-MIGRATION.md` §2; at 30 Hz every row is the former expression.

| Data value | Rule | Reference |
|---|---|---|
| Duration in ms → whole ticks | `ceil(ms · 30 / 1000) · f`, exact in integer/decimal arithmetic; 0 ms = 0 ticks; negative → error | `INI::parseDurationUnsignedInt` (`ceilf`) |
| Duration in ms → ticks (unrounded) | `Fixed(ms · R / 1000)` | `INI::parseDurationReal` |
| Seconds (`BuildTime`) → ticks | `ceil(s · 30) · f` | – |
| Original frames → ticks | `n · f` (exact) | constants of the original in logic frames |
| Speed per s → per tick | `v / R` | `ConvertVelocityInSecsToFrames` |
| Acceleration per s² → per tick² | `a / R²` | `ConvertAccelerationInSecsToFrames` |
| Raw value per frame → per tick (additive) | `v / f`, per frame² `v / f²` | fields that the original applies per frame |
| Degrees → radians | `deg · π / 180`, π to 28 digits | `INI::parseAngleReal` |
| Degrees/s → radians/tick | `deg · π / 180 / R` | `ConvertAngularVelocityInDegreesPerSecToRadsPerFrame` |
| Percent | ratio (`50 %` → 0.5) as `Fixed` | `INI::parsePercentToReal` |

- **Deviation for durations:** the original computes `ceilf((Real)msec · (30.0f/1000.0f))` in binary32. A test emulates this
  in IEEE binary32 and compares with the exact rule over 0 to 5,000,000 ms (≈ 83 minutes): **6,310 of 5,000,001 values
  deviate**, all by exactly one tick downwards (24-bit mantissa); the first difference is at 4,369,067 ms
  (≈ 73 minutes). All durations below 4,000,000 ms (≈ 66 minutes) are identical. The core computes exactly.
- **Limit values** (tested): 0 ms, 1 ms, 33/34 ms (rounding), 4,294,967,295 ms (128,849,019 ticks), overflow of
  `decimal`/`long`, negative durations, `Fixed` overflow and underflow, `Halbwert` rounding in multiplication, division and
  decimal conversion, `Floor`/`Ceiling`/`Round` for negative values. Negative durations in the data follow
  `sscanf("%u")`: `MinLifetime = -1` (map `MD_GLA01`) yields 4,294,967,295 ms and is made visible as
  `compatibility-unsigned-wrap`.

## 2. IDs and canonical order

- `EntityId` starts at 1, **0 is invalid** (`INVALID_ID`, `GameLogic::allocateObjectID`: `m_nextObjID = 1`, then
  `+1`). IDs are **never reused** (the original also assigns monotonically; the gap search is there as a
  `@todo`). `EventId`, `TimerId` and `CommandId` are likewise monotonic counters from 1; `PlayerId` 0 is the built-in
  system instance (scripts, tools, tests), real players start at 1; `WorldId` is assigned by the creator of the world.
- All counters are part of the state (hash and snapshot).
- **Update order** depends neither on `Dictionary`/`HashSet` order nor on threads: updates by
  (wake time, entity ID, module index), commands by (player ID, sequence), timers by (due time, timer ID), destructions
  in ascending entity ID, spawns in reservation order (= ascending ID), events by event ID.

### Sleepy update model

The model of the original is adopted (`GameLogic::m_sleepyUpdates`, `UpdateModule`): every update module has an
absolute wake time, a min-heap with back reference (`indexInLogic`) holds the scheduled modules, `update()` returns
the sleep duration (≥ 1, `UPDATE_SLEEP_NONE = 1`, `UPDATE_SLEEP_FOREVER = 0x3fffffff`), wake times are clamped to
`UPDATE_SLEEP_FOREVER`, `setWakeFrame` from the module's own `update()` is ignored, and the special case
"already awake, wants the next tick" changes nothing (`friend_awakenUpdateModule`).

Deliberate deviations:
1. **Total order instead of heap chance.** The original compares only `(Frame << 2) | Phase`; on a tie the
   heap structure decides. The core additionally sorts by entity ID and module index; the order is thereby independent of the
   heap implementation.
2. **No update phases.** `SleepyUpdatePhase` (0..3) is not yet used by any implemented module; all modules
   run in phase 0. As soon as a module needs phases, the key is extended (limit).
3. **"Forever" = not scheduled.** Modules with a wake time ≥ `UPDATE_SLEEP_FOREVER` are not in the heap (the original keeps
   them at the end of the heap); as a result the table returns to zero after all lifetimes have expired.
4. **Destroyed entities.** An entity that was marked for destruction in this tick is no longer updated
   (the original does not check this in the sleepy loop and deletes only at the end of the frame).

## 3. Random numbers

- **Algorithm:** `SageRandom` is a 1:1 port of `RandomValue.cpp` (`randomValue`, `seedRandom`,
  `GetGameLogicRandomValue`): six 32-bit words, addition with carry across all words, then a carry-propagating
  increment of the lowest word. The edge cases of the original are preserved too: `delta = hi - lo + 1` overflows as
  `unsigned` (the full range yields `hi` without a draw), ranges with `hi < lo` behave as in the original.
- **Streams and seed derivation** (`RandomStreams`): `logic`, `cosmetic` ("client" in the original) and `audio`. Each stream
  is initialised as in `InitRandom(seed)` with the same world seed via `seedRandom` and then advances
  independently. Only `logic` may be drawn by the simulation and is part of the tick hash. `cosmetic` and `audio` belong to the
  presentation, are in the snapshot, but **not** in the hash. There is no ambient generator (`Random.Shared` is
  forbidden and is checked mechanically).
- **Deviation for Real random:** the original multiplies by a binary32 factor; `Range(Fixed, Fixed)` computes exactly
  `lo + (hi − lo) · r / (2^32 − 1)` in fixed point (never exceeds `hi`).
- **Golden values, computed independently.** There is no C++ compiler on this machine (`gcc`, `clang`, `cl` are missing), so the
  reference could not be compiled. Instead a separate Python transcription was made
  (`randomValue`, `seedRandom`, `range` with explicit 32-bit masks, copied directly from `RandomValue.cpp`, without looking at the
  C# code). Tested are the first 8 raw values and the state afterwards for the seeds `0`, `1`, `0x12345678`,
  `0xFFFFFFFF`; range draws `[0,99]` and `[-5,5]` (12 values each); the state after **1,000,000 draws** (seed
  `0x12345678`: `d020b74b dafd10ec e5c0df3d 405d1ab8 3717abd5 8222d41c`); constructed carry chains including the
  full increment cascade (`[0,0,0,0,0,0xFFFFFFFF]` → return value 0 and zero state); the full-range case. The
  hand calculation is thus the documented substitute proof; a comparison with the compiled original remains open as a
  debt for a machine with a compiler (Phase 5 needs it anyway for the golden master scenarios).

## 4. World, tick phases and life cycle

`SimulationWorld.Step()` executes exactly these phases in this order:

| # | Phase | Content |
|---|---|---|
| 1 | Commands | commands with target tick = current tick, ordered by (player, sequence); spawn reserves an ID and puts it into the spawn queue, destroy marks, damage/kill take effect immediately, timers are created |
| 2 | Timers | due timers by (due time, ID); repetition sets `Fälligkeit = aktueller Tick + Intervall` |
| 3 | Updates | sleepy scheduler; marked entities are skipped; the return value determines the next wake time |
| 4 | Events | delivery in event ID order; events created during delivery are handled in the same pass |
| 5 | Destroy | marked entities in ascending ID: `OnRemove`, remove scheduler entries and own timers, clear the table |
| 5a | Delayed hits | due hits of weapons without a projectile object in insertion order (`WeaponStore::update`, Phase 8, §21) |
| 6 | Spawn | create reserved entities in ID order; modules receive `OnSpawn` (may draw `logic`); updates are scheduled |
| 7 | Events | events of the spawn phase are delivered |
| 8 | Hash | the tick counter increases, the state hash is computed |

Rules:
- **Mutations during an iteration go through queues.** A spawn only becomes visible in phase 6 (so an entity is
  never updated in the tick in which it is created; the earliest update is the next tick), a destruction only in
  phase 5. The original creates objects immediately; the spawn queue is a deliberate, documented deviation – the ID is
  however already assigned at the request, so that the requester knows it.
- **An entity destroyed at the end of the tick** (`RequestDestroy`) receives no further updates in this tick, but its
  `EntityDestroyed` event and all events created until then are delivered **before** it is removed (`OnRemove`
  runs afterwards).
- **Death is not destruction.** `Kill` triggers death modules; an entity without a destroying death module (`KeepObjectDie`)
  remains as a corpse, as in the original. `Destroy` removes without death modules.
- **Protection against endless loops:** updates and events per tick are hard-limited (50 and 20 million respectively); exceeding
  throws. There are no threads and no locks; a deadlock is excluded by construction (the test
  `SourceRules` forbids `Thread`, `Task.Run`, `Parallel`, `lock`).

## 5. Modules

### Binding

`CnCNew.Data` holds descriptors without a factory (`ModuleDescriptor`: name, block kind, interface mask, owner kinds,
field schema, `CompatibilityOnly`, target phase, category); the catalog contains all **205** module types of the content (an
earlier count of 206 contained the phantom module `ArmorSet`, which arose from an `AddModule` never closed in
`Maps/MD_CHI03/map.ini`, whose fields the original applies to the object). Phase 5 has implemented **35** of these 205 types
(12 from Phase 4 plus 23 new ones: `StealthUpdate`, `AutoHealBehavior`, the eleven `*Upgrade` modules,
`AIUpdateInterface`, `ProductionUpdate`, `FlammableUpdate`, `TransitionDamageFX`, `SlowDeathBehavior`,
`FireWeaponWhenDeadBehavior`, `StealthDetectorUpdate`, `PoisonedBehavior`, `GarrisonContain`, `TransportContain` –
see `docs/GAME-RULES.md` §3/§6/§6a). `CnCNew.Simulation` owns the
explicit, ordinally sorted, reflection-free binding table `BuiltInModules.Bindings` (name → `ModuleBinding<TData>` with
parser and factory) and `ModuleBindingTable` checks it against the descriptors on creation:

| Error case | Result |
|---|---|
| unsorted table, duplicate name | `ModuleBindingException` |
| name without descriptor | `ModuleBindingException` (unknown module) |
| descriptor `CompatibilityOnly` (not implemented) | `ModuleBindingException` |
| presentation module (Draw/ClientUpdate) | `ModuleBindingException` |
| wrong owner kind | `ModuleBindingException` |
| implemented simulation module without binding | `ModuleBindingException` |

`StrictUnknownModules` passes on the content data; the test `catalog == beobachtete Modulmenge` (205 names) runs against
`game/content`. Modules that are not implemented remain visible compatibility cases: an object with such
a module is **not instantiable** and names it in `ObjectTemplate.MissingModules`; Draw/ClientUpdate modules
are listed (`PresentationModules`), but never instantiated.

### Selection with numbers

Basis: all module entries of the base objects in the retail content including those inherited from the `DefaultThingTemplate`
(19,529 uses in 2,113 objects; `SimTool audit-modules <content-dir>`). The twelve implemented modules cover
5,497 uses (28.1 %):

| Module | Uses | Objects | Role |
|---|---:|---:|---|
| `DestroyDie` | 1,295 | 1,295 | death module (default and explicit) |
| `ActiveBody` | 1,217 | 1,215 | body with health |
| `FXListDie` | 1,144 | 1,008 | death module, effect request |
| `CreateObjectDie` | 629 | 536 | death module, object creation list request |
| `InactiveBody` | 453 | 453 | default body (props) |
| `StructureBody` | 365 | 365 | building body |
| `LifetimeUpdate` | 136 | 136 | timed death |
| `InstantDeathBehavior` | 114 | 56 | instant death with effects |
| `KeepObjectDie` | 62 | 62 | keep the corpse |
| `ImmortalBody` | 42 | 42 | immortal body |
| `HighlanderBody` | 21 | 21 | body with a 1 HP floor |
| `DeletionUpdate` | 19 | 19 | timed destruction |

Criterion: highest frequency among the modules that get by without a further subsystem (weapons, movement, AI, upgrades, players)
and fully cover creating, ticking, events, dying and destroying. `SlowDeathBehavior` (1,623) and
`FlammableUpdate` (843) were more frequent at that time, but each needed its own mechanical model
(weighted death lottery and fire/damage-over-time state machine respectively) and followed with Phase 5 (see
`docs/GAME-RULES.md` §3, subsection "Damage states, fire, poison and death").

The modules from Phase 5 on (supply and docking, spawners, repair, `FireWeaponUpdate`, `ReplaceObjectUpgrade`, `CrushDie`, experience and
veterancy levels, teams) are described with catalog schema, binding, tests and semantics in `docs/GAME-RULES.md` §6e–§6h and §7; the
up-to-date count is provided by `SimTool audit-modules`.

### Semantics of the implemented modules

- **Bodies** (`ActiveBody` family): starting health `InitialHealth`, cap `MaxHealth`, floor 0. Death is the transition
  "health ≤ 0 and previous > 0" (`ActiveBody::attemptDamage`) and calls the death modules exactly once in module
  order; dead entities take no more damage. `Kill` = `DAMAGE_UNRESISTABLE` with kill flag and amount = maximum
  health (`Object::kill`). Healing (`DAMAGE_HEALING`) is clamped. `ImmortalBody`: `delta = max(delta, −Gesundheit + 1)`,
  never dead. `HighlanderBody`: amount `min(Betrag, Gesundheit − 1)` except for `UNRESISTABLE`. `InactiveBody`: from spawn on
  "effectively dead", only unresistable damage takes effect and triggers the death modules once.
- **Death filter** (`DieMuxData`): `DeathTypes` (start always `ALL`, `+X`/`−X`/`ALL`/`NONE`), `VeterancyLevels`,
  `ExemptStatus`, `RequiredStatus` with the rules from `DieModule.cpp`.
- **`DestroyDie`** requests the destruction; **`KeepObjectDie`** does nothing; **`FXListDie`** creates an
  `FxRequested` event (with `StartsActive = No` it is dormant – activation belongs to the upgrade system); **`CreateObjectDie`**
  a `CreationListRequested` event (an empty list, as used in map overlays for switching off, requests nothing) and
  has executed the list since Phase 5 (`SimulationWorld.CreateFromList`, `docs/GAME-RULES.md` §6b);
  **`InstantDeathBehavior`** in the order effects → object creation lists → weapons (`WeaponFireRequested`) and then
  destruction. Object creation lists are executed (one list per module, draw as in the original); `FxRequested` and
  `WeaponFireRequested` remain contracts for the presentation and later phases respectively.
- **`LifetimeUpdate`/`DeletionUpdate`:** at spawn `logic.Range(min, max)` is drawn once (at least 1 tick), the
  expiry module sleeps until then; `LifetimeUpdate` **kills**, `DeletionUpdate` **destroys**.
- **Field schemas** are in `ModuleCatalog` (source per field: `ActiveBody.cpp`, `DieModule.cpp`, `LifetimeUpdate.h`, …) and
  were checked against every occurrence in the content; unknown keys, duplicate keys, wrong value kinds and missing
  mandatory fields (`MinLifetime`/`MaxLifetime`) are errors.

Deliberately **not** in Phase 4: armor coefficients (coefficient 1), damage multipliers, damage states and
damage effects, the fear signal together with its `GameLogicRandomValue` draw (`ActiveBody::attemptDamage`), subdual damage (all
subdual types do not affect health), `KILLPILOT`/`KILL_GARRISONED`/`STATUS` damage, the constructor object of the
`StructureBody`. The random consumptions of the original have to be added for the golden master comparisons of Phase 5.

## 6. Commands

Typed, tick-addressed, versioned type table, now `CommandTypes.Version = 9` (Phase 5 added 13 types, version 6
brought `DockCommand`, version 7 `SetRelationshipCommand`, version 8 the six construction commands of Phase 6, version 9 `MoveAndEnterCommand` and `EvacuateCommand`; existing codes never change):

| Code | Type | Purpose | system only |
|---:|---|---|:-:|
| 1 | `SpawnCommand` | create an entity from a template (owner 0 = sender; foreign owners system only) | |
| 2 | `DestroyCommand` | remove an entity (owner or system) | |
| 3 | `DamageCommand` | raw damage | ✔ |
| 4 | `KillCommand` | like `Object::kill` | ✔ |
| 5 | `SetTimerCommand` | timer on the entity or on the world (world timers notify only) | ✔ |
| 6 | `CancelTimerCommand` | delete a timer | ✔ |
| 7 | `GiveMoneyCommand` | credit a player's balance directly (scenario/test tool; the reference match no longer writes it) | ✔ |
| 8 | `PurchaseUpgradeCommand` | buy an upgrade (player-bound or object-bound), cost debited immediately | |
| 9 | `QueueProductionCommand` | enqueue a unit in the production queue of an entity | |
| 10 | `CancelProductionCommand` | cancel a queue slot, full refund | |
| 11 | `SetMoveTargetCommand` | set a straight-line movement order (Phase 5 movement model) | |
| 12 | `FireWeaponCommand` | direct weapon shot at a target (no auto target, no line of sight) | |
| 13 | `GiveSciencePointsCommand` | credit a player's "Science Purchase Points" directly (rank from experience is not modelled) | ✔ |
| 14 | `PurchaseScienceCommand` | buy a science (`Player::attemptToPurchaseScience`), completed immediately | |
| 15 | `EnterContainerCommand` | put an entity into a container (`ContainModuleInterface::addToContain`), immediately, no path search | |
| 16 | `ExitContainerCommand` | remove an entity from its container (owner of the unit or of the container) | |
| 17 | `UseSpecialPowerCommand` | trigger a special power of an object (`ActionManager::canDoSpecialPower`, target: none, object or location) | |
| 18 | `DockCommand` | send an object to dock at a dock (`aiDock`: suppliers deliver or fetch, the dock becomes their preferred one) | |
| 19 | `SetRelationshipCommand` | set the directed relationship of a player to another (`Player::setPlayerRelationship`) | ✔ |
| 20 | `ConstructCommand` | a builder (dozer/worker) places a building at a position with an angle (`MSG_DOZER_CONSTRUCT`, §16) | |
| 21 | `SellCommand` | sell a building (§16) | |
| 22 | `SetRallyPointCommand` | set the rally point of a production building to a location (§16) | |
| 23 | `ResumeConstructCommand` | continue an interrupted construction site with a (different) builder (`DozerAIUpdate::resumeConstruction`) | |
| 24 | `CancelConstructCommand` | cancel a construction site, the cost is refunded | |
| 25 | `RepairCommand` | a builder repairs a damaged own building (`MSG_REPAIR`, free of charge, §16) | |
| 26 | `MoveAndEnterCommand` | an own object walks to a container (transport, tunnel, building) and enters on arrival (`aiEnter`, §21) | |
| 27 | `EvacuateCommand` | the owner unloads a container, a mobile one optionally only at a position; a tunnel releases the whole network (`MSG_EVACUATE`, §21) | |

`Submit` checks immediately and rejects **explicitly, counted and as a `CommandRejected` event**: `Late` (the tick lies in the past),
`Duplicate` (same (player, sequence) as the last accepted one), `OutOfOrder` (smaller sequence), `UnknownPlayer`,
`NotPermitted`, `InvalidArguments` (sequence 0, negative values, invalid types), `UnknownTemplate` (unknown or not
instantiable). At execution time `UnknownEntity`, `NotPermitted` (foreign entity) and `UnknownTimer` follow; Phase 5
adds `InsufficientFunds`, `AlreadyOwned`, `UnknownUpgrade`, `NoWeapon`, `WeaponNotReady`, `OutOfRange`, `QueueFull`,
`CannotMove`, `CannotProduce`, `UnknownScience`, `ScienceNotPurchasable`, `InsufficientSciencePurchasePoints`,
`MissingPrerequisite`, `CannotEnterContainer` (KindOf/relationship filter, capacity or a too heavily damaged/dead
container), `NotContained` and for special powers `UnknownSpecialPower`, `MissingScience`, `SpecialPowerNotReady`,
`InvalidSpecialPowerTarget` and `SpecialPowerHandlerMissing` and for docking `CannotDock` (code 30; details and numerics:
`docs/GAME-RULES.md` §4/§1a/§6a/§6d/§6e). Phase 6 adds `TargetNotVisible` (31, `FireWeaponCommand` at a target invisible to the player, §15), `InvalidPlacement` (32, building site
not permitted: terrain, footprint, obstacle or unexplored), `CannotConstruct` (33: builder, template, prerequisite or role do not fit) and `CannotSell` (34). Sequences are strictly
ascending per player; commands of the same tick run by (player ID, sequence), independent of the order of enqueueing. The counters
per reason belong to the state. **Important:** `Validate()` checks at `Submit()` against the *current* world state, not
against the state after commands enqueued earlier in the same tick – a `GiveMoneyCommand` and a
`PurchaseUpgradeCommand` building on it in the same tick see the credit only once a `Step()` has been executed in between.

## 7. Snapshots and hashes

**State hash.** A 64-bit hash from SplitMix64 steps (`StateHash.Mix`), not cryptographic (it detects
deviations, it does not withstand attacks). Reason for the choice: no package dependency is needed
(`System.IO.Hashing` would be a new NuGet package), the result is independent of endianness and hardware intrinsics, and
the input is canonicalised integers anyway. **Incremental:** per entity the hash is computed over the same
`Describe` traversal that also writes the snapshot (a single place for "what is state"); the
entity hashes are added, an entity is only rehashed if the core has touched it in this tick (every call into
a module marks it). Timers and pending commands are sums likewise; the small global parts (tick, ID counters,
`logic` state, player table, rejection counters, spawn/destroy queue, pending events) are rehashed
every tick. `VerifyIncrementalHash()` compares with a complete recomputation (tests and the stress run check this
regularly, the snapshot restore always).

**Snapshot.** `WorldSnapshot.Write` produces canonical UTF-8 JSON (fixed property order, integers only –
`Fixed` as a raw value –, no paths or timestamps, every collection sorted by ID or by (tick, player, sequence)
respectively): header `format = cncnew-world-snapshot`, `version = 7` (2 added the teams of the player table, 3 the computer player flag, 4 the terrain fingerprint, 5 the vision maps – the vision hash is part of the state hash –, 6 construction, sale and rally point state, 7 see below), `tickRate`, `definitionFingerprint`, `fixture`, `worldId`,
`seed`, `stateHash` (version 7, Phase 8: tunnel network per player, delayed hits, start data of a projectile in the spawn queue, event position), then `state` (tick, ID counters, `logic` state, player table, rejection counters, spawn/destroy queue,
events, entities with module states, timers, commands) and `presentation` (`cosmetic`, `audio`). **SHA-256** identifies a
snapshot (`WorldSnapshot.Sha256`). `Restore` is deliberately kept minimal and internal (Phase 9 owns the later
save contract) and rejects: empty/truncated/mangled files, foreign version/format, an unsupported
tick rate (since Phase 20 the world is created with the stored rate – 30, 60 or 120 Hz –, and the catalog passed in
must be built for exactly this rate; `docs/TICKRATE-MIGRATION.md` §7), a deviating
definition fingerprint, deviating templates (name and index are checked against each other) and every state whose
recomputed hash does not match the stored one (integrity check).

**Energy restoration (Phase 6).** The energy production of a player is set explicitly from the snapshot after the entities are loaded,
because `PowerPlantUpgrade` and `OverchargeBehavior` change it via `IWorldServices.AddEnergyProduction` and would
otherwise be counted twice or not at all when the modules are registered (the integrity hash failed in the test because of this).

**Definition fingerprint.** SHA-256 of the canonical definition snapshot, streamed while writing
(`DefinitionSnapshotWriter.ComputeFingerprint`), without holding the 142 MB snapshot in memory.

## 8. Replay

Binary format, little endian: `CNCRPLAY`, u32 format version, u32 command table version, u32 tick rate, 32 bytes of
definition fingerprint, fixture ID, u32 seed, u32 world ID, player table, end tick, checkpoint interval, commands
(sorted by (tick, player, sequence)), checkpoints (tick, hash) and a concluding SHA-256 over everything preceding.
`ReplayFormat.Read` rejects: wrong magic, foreign version (before the hash check, with a clear message), truncated or
mangled files (hash deviation), surplus bytes, unknown command types, unsorted commands. `ReplayRunner`
rejects a replay whose fingerprint or command table version does not fit, whose tick rate is not supported or
is not that of the catalog or of the world passed in (`Run(replay, world)` checks the rate of the world), and reports deviations at
checkpoints as `ReplayDivergenceException` with tick and hashes. The continuation (snapshot → restore → continued run) uses
the same runner and starts at the tick of the world.

**Format v2 (Phase 5).** The player table additionally carries the faction name (empty = no faction), so that a replay
can be assigned to a faction match without side information; version 1 is rejected with a clear message. In addition
the format now encodes **all 17 command types** (previously only the six from Phase 4): `GiveMoney`, `PurchaseUpgrade`,
`QueueProduction`, `CancelProduction`, `SetMoveTarget`, `FireWeapon`, `GiveSciencePoints`, `PurchaseScience`,
`EnterContainer`, `ExitContainer` and `UseSpecialPower` have been added. `Tests.ReferenceMatch` writes and reads back every type and
compares the commands; an unknown type in the writer is an error, not a silent omission.

**Format v3 and v4.** v3 adds the team number per player (`PlayerConfiguration.Team`, 0 = no team), v4 the computer player flag
(`IsComputer`, prerequisite for `AutoFindHealingUpdate`). The command table by now encodes **all 19 command types**
(`DockCommand` and `SetRelationshipCommand` have been added); older format versions are rejected with a clear message.

**Format v7 (Phase 8, E1).** Additionally encodes `MoveAndEnterCommand` (u32 actor, u32 container) and `EvacuateCommand` (u32 container, bool position,
i64 X, i64 Y raw); command table version 9, **all 27 command types**; v6 is rejected with a clear message (`Tests.Transports`).

**Format v5 and v6 (Phase 6).** v5 carries the terrain fingerprint in the header (`TerrainMap.Fingerprint`, 32 bytes; the runner rejects a
replay whose terrain does not fit, as with the definition fingerprint); v6 encodes the six construction commands. The command table
thus comprises **all 25 command types**; `Tests.Construction` (`ConstructionSnapshotAndReplay`) checks snapshot and replay with construction commands.

## 9. Acceptance commands

```powershell
dotnet build CnCNew.slnx
dotnet run --project tests/CnCNew.Simulation.Tests
dotnet run --project tests/CnCNew.Simulation.Tests -- --content-dir game/content
dotnet run -c Release --project tests/CnCNew.Simulation.Tests -- --long
dotnet run --project tests/CnCNew.Data.Tests -- --content-dir game/content
dotnet run -c Release --project tools/CnCNew.SimTool -- stress
dotnet run -c Release --project tools/CnCNew.SimTool -- replay-record .local/stress.replay
dotnet run -c Release --project tools/CnCNew.SimTool -- replay-run .local/stress.replay
dotnet run -c Release --project tools/CnCNew.SimTool -- hash .local/stress.replay
dotnet run -c Release --project tools/CnCNew.SimTool -- audit-modules game/content
dotnet run -c Release --project tools/CnCNew.SimTool -- audit-coverage game/content
dotnet run -c Release --project tools/CnCNew.SimTool -- reference-match game/content
dotnet run -c Release --project tools/CnCNew.SimTool -- ai-match game/content --map "Maps/Alpine Assault/Alpine Assault.map" --factions China,GLA --max-ticks 45000
dotnet run --project tools/CnCNew.MapTool -- audit game/content
dotnet run --project tools/CnCNew.IniTool -- compat-check game/content baselines/compatibility.json
dotnet run --project tools/CnCNew.IniTool -- load-order-report game/content
```

## 10. Stress, memory and budgets

`SimTool stress` records the synthetic scenario (`StressScenario`, fixture `synthetic-lifecycle-v1`, exclusively
own, legally unobjectionable definitions) as a replay and executes it several times. The driver observes only
world events and its own `SageRandom`; load: ramp to 10,500 long-lived entities, then per tick one kill/destroy
with a replacement spawn, every 2 ticks a spark, every 3 ticks a puff, every 40 ticks an immortal entity, random damage,
owner destroys, timers (repeating notify and damage timers on about 45 % of the new entities, world timers), deliberately
invalid commands; the last 6,000 ticks destroy all survivors.

First measurement on 28 September 2026 (Release, `dotnet run -c Release`) on: AMD64 Family 25 Model 68 Stepping 1
(AuthenticAMD), 16 logical processors, 28 GiB RAM, Windows 11 (10.0.26200), .NET 10.0.12, workstation GC:

| Quantity | Measured value |
|---|---|
| Ticks | 216,000 (two hours of game time) |
| Commands | 914,394 (replay file 47,094,803 bytes in format v2, 216 checkpoints) |
| Spawn/Destroy | 401,237 / 401,237 |
| simultaneously living entities | peak 15,775 (target ≥ 10,000) |
| Modules / timers / scheduled updates (peak) | 42,105 / 7,099 / 30 |
| Run time per 216,000-tick run | 22 s (avg. 102 µs per tick), slowest tick 30.7 ms |
| Recording of the scenario (driver + world) | 37 s |
| Allocation of `Step()` in steady state | avg. 1,350 bytes per tick under churn (entity and module objects of the spawns); **0 bytes** with a world at rest (test) |
| Snapshot at tick 108,000 | 10,262,399 bytes for 13,220 entities (≈ 776 bytes per entity), restore 0.7 s (restore time not re-measured) |
| Peak working set of the process | 532 MiB (including the 915,000 command objects of the recorder) |
| Error commands | 2,170 `UnknownEntity` rejections, identical in every run |

Results: three fresh runs yield identical tick hashes over all 216,000 ticks (chain hash `E230DA5F04821478`,
final hash `A299C38BFE07681D`); the run with a snapshot at tick 108,000 and continuation yields the same 216,000 hashes;
entity, module, update, timer, event, command, spawn and destroy tables are at zero at the end; no deadlock
(no threads, all loops bounded); the incremental hash matched the complete
recomputation in every sample.

**Re-measurement on 29 September 2026** (after object creation list execution and `CreateCrateDie`; chain and final hash have
changed, because `CreateObjectDie`/`InstantDeathBehavior` now consume random draws and create objects): chain hash
`36211DD938214B9C`, final hash `9E4AF85CBFBB84C6`, 401,237 spawns and as many destroys, avg. 54.7 µs per tick, snapshot
at tick 108,000: 10,262,399 bytes. All runs (repeated independently, with snapshot continuation) agree tick by tick. The
command, entity and error numbers as well as the snapshot size of the table are also re-measured (replay v2); the other quantities are not.

**Re-measurement after the special power subsystem** (29 September 2026, ten further modules; the synthetic fixture is
unchanged, but player state and snapshot now carry the bounty rate and shared ready frames): chain hash
`76A8FCEEEE625672`, final hash `4A99EE46FD623A09`, 914,394 commands, peak 15,775 entities, snapshot at tick 108,000:
10,262,573 bytes (SHA-256 `A14A06E08A3C05796FF59E5B94E9AC1F0E31F45A5CABE4E1285C3A578AC896A0`), hash after restore
`1D2E96E0BB12303A`; three fresh runs and the snapshot continuation agree tick by tick, stress run 1,950 bytes per tick,
tick at rest 0 bytes (the readiness state uses an index loop over the sorted list, no enumerator allocation).

**Re-measurement at the completion of Phase 5** (29 September 2026, 74 bound modules, supply, experience, teams, repair, area damage; the
synthetic fixture is unchanged, snapshot version 3, replay format v4): chain hash `8E1FA50D21433123`, final hash `D80B367CEEF6D72D`,
replay 47,094,813 bytes (SHA-256 `AF97976870D69C52E48C889A753E955D85C7DDE94F5061A3F8AEF4610310B0CC`), 914,394 commands, 216 verified
checkpoints, avg. 59.9 µs per tick (12.9 s per run), slowest tick 21.07 ms, peak 15,775 entities, 42,064 modules and 7,048 timers, 401,237
spawns and as many destroys, 2,170 `UnknownEntity` rejections, snapshot at tick 108,000: 11,135,228 bytes (SHA-256
`0372D5025C1C2433435BB03F06E77ACC1E40D5D7B55C8A136718D221C8CEAFBA`), restore 0.66 s, hash after restore `413FAD6458E026C6`, allocation
1,994 bytes per tick under churn, peak working memory 596 MiB. Three fresh runs and the snapshot continuation agree tick by tick;
all tables are at zero at the end. The older re-measurements above are superseded and have only been left as history.

**Re-measurement on 30 September 2026** (Phase 6, Release, same machine; snapshot v6, replay v6, vision maps in the state hash;
the synthetic scenario uses neither terrain nor navigation nor construction): 216,000 ticks in 13.0 s (60.2 µs per tick), 914,394 commands,
slowest tick 18.93 ms, allocation 2,054 bytes per tick under churn, peaks 15,775 entities / 42,064 modules / 29 scheduled updates /
7,048 timers, chain hash `C298F5F74993D6CD`, final hash `D1521974C861A4B9`. Snapshot at tick 108,000: 11,862,400 bytes for 13,220 entities,
SHA-256 `7E3DBFDCBCD4D46502FA0C571598E826343C14D23F97A0D2919BF53661009A60`, restore 0.59 s (hash `ACCDC2E5315991DB`). Working set (peak)
596 MiB. Three fresh runs and the snapshot continuation agree tick by tick; 401,237 spawns/destroys and 2,170 `UnknownEntity` rejections
as before. Chain hash and final hash differ from those of 29 September, because the state representation (vision hash,
terrain fingerprint) and the stealth rule (§15) have changed; the scenario itself remained unchanged.

**Re-measurement Phase 8, package E1** (30 September 2026, Release, same machine, measured in parallel with an AI evaluation, therefore slower; snapshot v7, replay v7):
216,000 ticks, 914,394 commands, replay 47,094,815 bytes, run 1 20.6 s (95.5 µs per tick), slowest tick 31.34 ms, allocation 2,158 bytes per tick under churn,
peaks 15,775 entities / 42,064 modules / 29 scheduled updates / 7,048 timers, **chain hash `3AECEB0579E460AF`, final hash `ED6F9329F59662E3`**; snapshot at
tick 108,000 11,862,512 bytes (SHA-256 `6A8D0B9BD9F64F22384FF8A39D7D1AA25F0C8D0D959527D6365CC3E572DBBA8C`), restore 0.72 s (hash `3D5CD16AC1FEAA30`), working set 567 MiB.
Three fresh runs and the continuation agree tick by tick, 401,237 spawns/destroys, 2,170 `UnknownEntity`. The hashes change because the global
state now describes the tunnel network per player and the (empty) list of delayed hits; the synthetic fixture has no projectiles.

**Budgets** (with the measured value as the basis, about 2.7-fold margin): 216,000-tick run of this scenario ≤ 60 s;
avg. ≤ 300 µs per tick; slowest tick ≤ 100 ms (the real real-time budget, 33.3 ms at 30 Hz, is only set with the rules of
Phase 5 under real load); working set ≤ 1 GiB; tick at rest 0 bytes, churn ≤ 4 KiB per tick; snapshot ≤ 1 KiB
per entity. The numbers are regression thresholds, not performance commitments for the game.

## 11. Module coverage of the content

`SimTool audit-coverage` compiles the retail base content (without map overlays; Phase 5 corrected the INI parser –
see §12 – which changes the object count from 2,113 to **2,115** and the fingerprint; 12,550 definitions,
fingerprint `2FAE6DCC…`) and instantiates every object that has only implemented or presentation modules:

- **State after merging collision and the special-ability shell (1 October 2026, to be re-measured): both branches below were measured independently
  of each other on the same Phase 9 state and merged afterwards; 91 of 205 module types implemented (4 new compared with the Phase 9 state:
  `ToppleUpdate`, `StructureToppleUpdate`, `HeightDieUpdate`, `SpecialAbilityUpdate`).** Instantiability, module usages, compatibility baseline
  and reference match hash are to be re-measured after the merge (see the final report of this phase for the fresh numbers).
- **State after Phase 10, collision (1 October 2026, freshly measured): 2,115 objects, 1,588 fully instantiable (75.1 %)**, 527 blocked; 90 of 205 module types
  implemented (3 new: `ToppleUpdate`, `StructureToppleUpdate`, `HeightDieUpdate`, §23), they cover 15,752 of 19,553 module usages (80.6 %); fingerprint `D112D8B5AF345559…`.
  `ToppleUpdate` (76/66) and `HeightDieUpdate` (31/24) disappear completely from the blocker list; largest remaining blockers: `SpecialAbilityUpdate` 64/36,
  `JetSlowDeathBehavior` 59/0, `CountermeasuresBehavior` 40/0, `JetAIUpdate` 34/5, `DeliverPayloadAIUpdate` 28/1, `MobMemberSlavedUpdate` 27/27, `RailroadBehavior` 25/25,
  `BaikonurLaunchPower` 23/23. Retail reference match (`SimTool reference-match`): winner FactionAmerica at tick 10,575 (replay 10,876 ticks), final hash (`final hash`, run 1)
  `057C6678FE8F3381` (changes compared with the Phase 9 state because previously blocked objects – among others trees and height-die objects on the start maps – are now instantiable
  and take part in the fight); two fresh runs and a continuation from tick 5,438 yield identical hashes. The synthetic 216,000-tick stress test carries none of the three
  new modules and stays unchanged at `ED6F9329F59662E3`. Compatibility baseline 756 entries (previously 749): 9 `module` entries are dropped (now implemented), 16 new
  `deferred-field` entries for pure presentation fields of the three modules (FX lists, `ReorientToppledRubble`, salvo timing, `DestroyAttachedParticlesAtHeight`).
- **State after Phase 10, special-ability shell (1 October 2026, freshly measured): 2,115 objects, 1,530 fully instantiable (72.3 %)**, 585 blocked; 88 of 205
  module types implemented (1 new: `SpecialAbilityUpdate`, honest shell, see §23), they cover 15,730 of 19,553 module usages (80.4 %);
  compatibility baseline 751 entries. `SpecialAbilityUpdate` drops out of the blocker list (previously 64 objects/36 blocked only by it). The
  retail reference match changes because of this (Ranger, Jarmen Kell, GLA Rebel and others are now fully instantiable, not only their shell):
  winner FactionAmerica at tick 14,550 (replay 14,851 ticks), final hash (`final hash`, run 1) `3DA826C10BB6781B`; previously tick 13,665 (13,966 ticks),
  `D4779BE413835B6D`. Details in §23.
- State after the Phase 9 mission modules (1 October 2026): 2,115 objects, 1,494 fully instantiable (70.6 %), 621 blocked; 87 of 205 module types implemented (2 new: `CommandButtonHuntUpdate`, `LockWeaponCreate` – honest shell modules, see below), they cover 15,639 of 19,553 module usages (80.0 %); fingerprint `2B9D902AEC46A6DE…`. Largest remaining blockers (objects in total / blocked only by it): `ToppleUpdate` 76/66, `SpecialAbilityUpdate` 64/36, `JetSlowDeathBehavior` 59/0, `CountermeasuresBehavior` 40/0, `JetAIUpdate` 34/5, `HeightDieUpdate` 31/24, `DeliverPayloadAIUpdate` 28/1, `MobMemberSlavedUpdate` 27/27, `RailroadBehavior` 25/25, `BaikonurLaunchPower` 23/23. **Shell modules:** `CommandButtonHuntUpdate` (86 usages, no keys; in the original it is triggered only by `TEAM_HUNT_WITH_COMMAND_BUTTON`) holds no state, `LockWeaponCreate` locks the primary slot (`SlotToLock = PRIMARY` in all 12 usages; the simulation carries only one weapon, any other slot is an `UnsupportedModuleConfigurationException`); the triggering script actions remain missing and are counted (`docs/SCRIPT-ENGINE.md` §8). The retail reference match changes because of this (Rangers and others now come into being as start objects): winner FactionAmerica at tick 13,665 (replay 13,966 ticks), final hash (`final hash`, run 1) `D4779BE413835B6D`; previously tick 13,800 (14,101 ticks), `95022B80366E841E`. The stress test (216,000 ticks) stays at `ED6F9329F59662E3`.
- **State after Phase 8, package E1 (30 September 2026, freshly measured): 2,115 objects, 1,470 fully instantiable (69.5 %)**, 645 blocked;
  85 of 205 module types implemented (8 new: `DumbProjectileBehavior`, `MissileAIUpdate`, `PointDefenseLaserUpdate`, `TransportAIUpdate`, `ChinookAIUpdate`,
  `HelicopterSlowDeathBehavior`, `TunnelContain`, `HelixContain`, §21), they cover 15,543 of 19,553 module usages (79.5 %); fingerprint
  `2A31BC2A83D63A58…`. All 1,470 objects are created in one world (9,670 modules). Not implemented: 72 modules "Phase 5", 26 "Phase 6", 22 "Phase 7".
  Largest remaining blockers: `CommandButtonHuntUpdate` 79/18, `ToppleUpdate` 76/66, `SpecialAbilityUpdate` 64/3, `JetSlowDeathBehavior` 59/0,
  `CountermeasuresBehavior` 40/0, `JetAIUpdate` 34/5, `HeightDieUpdate` 31/24, `DeliverPayloadAIUpdate` 28/1, `MobMemberSlavedUpdate` 27/27, `RailroadBehavior` 25/25,
  `BaikonurLaunchPower` 23/23. The state after Phase 6 is kept below as history.
- History (state after Phase 6, 30 September 2026): 2,115 objects, 1,393 fully instantiable (65.9 %), 722 blocked;
  77 of 205 module types are implemented (12 from Phase 4, 62 from Phase 5, 3 from Phase 6: `PowerPlantUpdate`, `PowerPlantUpgrade`, `OverchargeBehavior`;
  183 simulation and 22 presentation modules in the catalog), they cover 15,382 of 19,553 module usages (78.7 %); the fingerprint of the retail base content is `1248585D0BAAB79F…`.
  All 1,393 objects are created in one world (8,925 modules). The following numbers describe the state at the end of Phase 5 and are kept as history.
- History (end of Phase 5): 2,115 objects, 1,384 fully instantiable (65.4 %), 731 blocked; 74 of 205 module types implemented (12 from Phase 4, 62 from Phase 5), they cover 15,364 of 19,553
  module usages (78.6 %). Compared with Phase 4 (26/2,113,
  1.2 %) a jump by a factor of ~46, carried by block 0.2, the eleven `*Upgrade` modules, `AIUpdateInterface`,
  `ProductionUpdate`, the six blocker modules from §6/§6a, the containment pair `GarrisonContain`/`TransportContain` and the
  fourteen modules from `docs/GAME-RULES.md` §6c (`PhysicsBehavior`, `SquishCollide`, the three production exits,
  `BaseRegenerateUpdate`, `GenerateMinefieldBehavior`, `HealContain`, hole modules etc. – together +336 objects) as well as the
  ten special power modules from `docs/GAME-RULES.md` §6d (+25 objects, among them the 13 command centers of the factions).
  `CreateCrateDie` and the creation list execution (`docs/GAME-RULES.md` §6b) alone unlock no object. The completion of
  Phase 5 added (1,224 → 1,384, +160 objects): supply economy and spawners (§6e), repair dock and `AutoFindHealingUpdate`
  (§6f), `FireWeaponUpdate`, `ReplaceObjectUpgrade` and `CrushDie` (§6g), experience modules (§6h).
- All 1,384 instantiable objects are created in one world and run through identically twice in the real-data test
  (test `--content-dir`); the stress test (`SimTool stress`, §10) uses a synthetic fixture and is untouched by the new
  modules except for the extent of the state (hashes re-measured, §10). The reference match on retail data
  (`docs/GAME-RULES.md` §7a) instantiates real units, production buildings and command centers of all 13 factions without
  a skipped start object.
- Remaining most frequent blockers (objects / of these blocked only by this module, fresh measurement of 30 September):
  `CommandButtonHuntUpdate` 79/18, `ToppleUpdate` 76/66, `SpecialAbilityUpdate` 64/3, `JetSlowDeathBehavior` 59/0,
  `MissileAIUpdate` 44/29, `CountermeasuresBehavior` 40/0, `JetAIUpdate` 34/2, `HeightDieUpdate` 31/15,
  `DeliverPayloadAIUpdate` 28/1, `MobMemberSlavedUpdate` 27/27, `RailroadBehavior` 25/25, `TransportAIUpdate` 24/13, `BaikonurLaunchPower` 23/23. The modules not yet
  implemented are distributed, by the phase noted in the catalog, over 77 (Phase 5), 29 (Phase 6) and 22 (Phase 7). `ToppleUpdate` needs real collision detection (verified by reading the
  reference source, see `docs/handoffs/HANDOFF-PHASE5.md`); Phase 6 deliberately introduced no collision (§14), the module stays open. The complete, up-to-date list is delivered by
  the tool.
- **Rule for update modules:** every module that implements `IUpdateModule` saves and loads its wake time
  (`nextWake`). Otherwise `SimulationWorld.Register` wakes restored modules with a negative wake time anew from the
  restore tick (`_tick + SleepNone`), which makes the continuation after a snapshot deviate from the original run.
  The `SourceRules` test enforces the string `"nextWake"` in every such class.

## 12. Known limits and technical debt

- **Phase 6 (completed)**: terrain, navigation, vision, construction and skirmish AI are implemented, but simplified; the limits are collected in §20 and in
  `docs/IMPLEMENTATION-PLAN.md` §6.
- **Phase 5 (completed)**: economy with supply, upgrades, sciences, direct combat with area damage and
  `WeaponBonus`, experience and veterancy levels, teams and diplomacy, garrison/transport, repair dock, simple movement,
  production and victory/defeat are implemented (`docs/GAME-RULES.md`), but deliberately simplified; terrain, pathfinding, construction and selling have existed since Phase 6 (§13–§16), what remains open is
  collision (projectile flight time has been implemented since Phase 8, §21), the effect of `CommandSet` and radar and the armor/weapon set selection by veterancy level. Every simplification is stated with a source reference in the module or
  catalog comment; the list of limits is in `docs/GAME-RULES.md` §10.
- An INI parser bug was fixed in Phase 5: `SageIniParser` compared the `End` keyword against the *entire*
  line instead of (like the original, `INI::initFromINIMulti`, `strtok`) only against its first token; two objects
  (`AmericaVehicle.ini`, `BossGeneral.ini`) carry a typo `End Upgrade_AmericaHallfireDrone`, which the original
  tolerates, but which the old parser continued as an open block. This slightly changes the object count (2,113 → 2,115)
  and the definition fingerprint.
- The update phases of the original (`SleepyUpdatePhase`) are not modelled.
- The fear sound random consumption (`ActiveBody::attemptDamage`, `GameLogicRandomValue(0,99)` when falling below the
  25 % threshold) is ported (block 0.3 completed, `VoiceFearRequested` event, test `Phase5FearSoundRng`).
- The comparison of the RNG with a compiled original is pending (no compiler available); the hand calculation is independent
  and suffices as evidence of algorithm equality, not as evidence for reuse in network games.
- `Restore` is internal; forward/backward compatibility of snapshots (currently version 7, older ones are rejected) is a topic of Phase 9 only.
- The state hash is not cryptographic and the snapshot JSON size (≈ 485 bytes per entity) is not optimised.
- Requests generated by the core (`FxRequested`, `WeaponFireRequested`, `VoiceFearRequested`, `CrateRequested`) are
  not executed; they are contracts for presentation. `CreationListRequested` is a log: the list is executed in the world
  (`CreateFromList`); nuggets that are not simulated (`CreateDebris`, `ApplyRandomForce`, `FireWeapon`, `Attack`,
  `DeliverPayload`) report themselves with `CreationSkipped`. Further deviations from `ObjectCreationList.cpp` (draw order
  because of delayed spawn, flat terrain, `FixedTrig` instead of binary32, a single `LifetimeUpdate` draw) are in
  `docs/GAME-RULES.md` §6b.
- The definition domains other than Object, Weapon, Armor, Upgrade, Science and Locomotor are still flatly typed
  (numbers in `TYPED-DEFINITIONS.md`): CommandButton, Faction, GameData; `SpecialPower` has been strictly typed since Phase 5. `ObjectCreationList`, `CrateData` and
  `GameData/Gravity` are typed.
- The reference match (`ReferenceMatchScenario`, `SimTool reference-match`) runs deterministically and replayably on synthetic and on retail
  data (13 of 13 factions with their real command centers, without
  dozers/workers, supply and special powers in the command stream; measurement in `docs/GAME-RULES.md` §7a). Golden-master
  scenarios exist for the damage chain and the production clock; supply and experience are pending. The remaining mechanics are
  covered by targeted integration tests (`Tests.Phase5.cs`) and the 216,000-tick stress test with real objects.
- **Live/replay gap at `SharedReadyFrame` (found in Phase 8, package G2; fixed in Phase 9):** `SpecialPowerBase.SharedReadyFrame()`
  (`src/CnCNew.Simulation/Modules/BuiltIn/SpecialPowerModules.cs`) created the shared ready tick of a `SharedSynced` special power
  lazily, on the *first* `IsReady()` access, and wrote it into `SimulationWorld` while doing so. For objects that own the power from the start
  (no `PurchaseScienceCommand`, hence no `OnSpecialPowerCreation`), this reading access was the only creation point. The skirmish AI
  calls `IsReady()` in its own decision logic in every real tick; a pure `ReplayRunner` run executes no AI code and
  never reached this access at the same tick (if at all). As a result `SkirmishMatchSession.Step()` (live) could yield a different
  `World.StateHash` than `ReplayRunner.Run` over the same `Session.ToReplay()` recording, reproduced on `LiveMatch` on
  "Alpine Assault": after an AI `ConstructCommand` at tick 13/14, `players[2].sharedReadyFrames` had eight entries live against zero in the
  replay (diagnosis by comparing `WorldSnapshot.Write` of both worlds; not an effect of the presentation layer, it also reproduced with pure
  `Session.Step()`). Affected were exclusively `SharedSynced` special powers without an explicit grant command.
  **Fix (Phase 9):** `SpecialPowerModuleBase.OnSpawn` now creates the ready tick deterministically for objects that are not under construction
  (`SharedReadyFrame(world)` instead of the previous omission for `SharedSynced` powers); `IsReady()` now only reads it, without creating it
  itself. Objects that are still under construction continue to receive it via `OnSpecialPowerCreation` (construction completion via `SpecialPowerCreate`,
  or science grant via `AddScience`) — the same creation points as for non-shared powers. **Not affected** were and are
  all numbers measured in this file (reference match, stress test and AI run hashes in §19 and in `CLAUDE.md`): they arise from live-against-
  live comparisons (two independent runs of the same session), which were never touched by this gap because both runs produce the same AI
  query sequence. Re-measured after the fix: the retail reference match (`SimTool reference-match`) yields an unchanged final hash
  `4DCB46FE3D2E8214` (winner FactionAmerica, tick 13,800) — this scenario runs through `ReferenceMatchScenario`, not `SkirmishAi`, and
  does not touch the fixed place. A skirmish AI run that does touch the place (`SimTool ai-match game/content --map
  "Maps/Alpine Assault/Alpine Assault.map" --factions FactionAmericaAirForceGeneral,FactionChina --max-ticks 45000`; America uses the
  shared special power 8 times) changes its result as expected (AI timing shifts): winner FactionChina at tick 16,155 (previously not
  measured with this exact invocation, hence no before/after hash), final hash `0FDF97B63EFECC6A`, still identical over two fresh runs and
  snapshot continuation. The 16-map AI overview in §19 (Phase 6 / package E1 comparison) was **not** re-measured for this fix;
  that is a separate, extensive balance survey and not a pure determinism check – whoever needs its numbers must run them again.
  The 216,000-tick stress test (`SimTool stress`, synthetic fixture) stays exactly the same (`ED6F9329F59662E3`, checked by comparing before/after
  the fix with otherwise unchanged code) — the fixture carries no `SharedSynced` special power.

## 12a. Live/replay determinism: test gap (closed)

The existing replay tests (`Tests.ReferenceMatch.cs`, `Tests.Skirmish.cs`, `Tests.State.cs`) so far compared exclusively two
`ReplayRunner` runs with each other or the winner, never the `World.StateHash` of a live-run `SkirmishMatchSession` against a
`ReplayRunner` run over its own `ToReplay()` recording. That is why no existing test had found the gap described above; it
only became visible in package G2 (`CnCNew.Game.Tests`) when testing a real live match. Phase 9 adds exactly this comparison:
`Tests.Skirmish.cs` → `SkirmishLiveSessionMatchesItsOwnReplay` starts a `SkirmishMatchSession` directly (without `SkirmishMatchScenario`'s
detour via a copy), lets it run until `IsOver`, compares `session.World.StateHash` with `ReplayRunner.Run(session.ToReplay())` and
additionally checks that the AI actually used the shared special power (`AlphaShared`/`BetaShared`, `SharedSyncedTimer = Yes` on the command center of the
test fixture), so that the test really runs through the gap instead of merely not hitting it by chance. The test fails
on the unfixed code (verified) and passes with the fix above.

## 13. Terrain and maps (Phase 6)

`CnCNew.Formats` decodes the map file (`SageMapDecoder`; containers CkMp, EAR (RefPack) and ZL5, blend tile versions 6–8) into an
immutable `MapData` (`CnCNew.Data.World`: height grid, blend tiles, objects, waypoints and waypoint links, polygon triggers,
build list). The simulation reads no map format: `TerrainMap` (`CnCNew.Simulation/Terrain`) is built from `MapData` and is
the only terrain object the world knows.

- Grid with `CellSize` 10 world coordinates, `CellFlags` `Cliff` (from the blend tile cliff attribute of the map) and `Water` (polygon trigger with a water area). The height is sampled from the height grid
  in fixed point.
- Waypoints (`Player_N_Start` yields `StartPositions`), water areas and a **fingerprint** (canonical, never paths or time).
  The fingerprint is in the snapshot (v4) and in the replay (v5); a replay on a different terrain is rejected.
- Not taken over (`MapTool audit` counts them): `GlobalLighting` (150 maps, 130,800 bytes) and `SidesList`/`PlayerScriptsList` (150 maps, 9,542,461 bytes), that is
  lighting, player lists and scripts of the map; scripts do not belong to Phase 6.
- **`MapTool audit`** (real data, 30 September): 150 maps decoded, 0 failed; containers CkMp 4 / EAR 145 / ZL5 1; blend tile versions v6 3 / v7 31 / v8 116;
  30,391,581 height points, 30,259,192 cells, of which 8,253,119 cliff cells; 281,480 objects (0 discarded because of the height range), 25,463 waypoints, 16,489 waypoint links,
  at most 8 start waypoints per map (75 maps without a start waypoint), 2,488 polygon triggers (of which 526 water), 1,631 sides, 7,704 teams, 1,356 build list entries.
  Terrain: 150 maps built, 16,475,736 path cells (3,278,645 cliff, 1,856,627 water), 258 start positions, largest extent 589 cells, 0 rejected;
  digest of the fingerprints `723F6640405C5445189D523BA257D182149CBFCEC01A6C8F0B053031FB130459`.
- **Ground paths between start positions:** 462 pairs, 456 complete, 6 only partial, 0 blocked, 1,679 waypoints, digest
  `C775CD5CD41E90BC7C55E054E6C0F04EC60A0E02D29DFC5705359B1128BF8481`. The six partial paths are a known limit (§20).

## 14. Navigation and movement (Phase 6)

`NavigationGrid` is an 8-connected A* on the cell grid: costs 10 (straight) and 14 (diagonal), **no corner cutting**, ties by cell index,
expansion budget 200,000 per request (after that the result is `Partial` or `Blocked`, never a silent substitute path). Cell classes in this order of precedence: obstacle,
cliff, water, free; allowed is what the `Surfaces` of the locomotor permit (now typed as a flags field instead of a compatibility value).

- **Obstacles** are buildings and other `OBSTACLE` entities. The footprint is the cells whose centre lies in the shape enlarged by a quarter cell, plus the cell
  of the object itself (an approximation, not a copy of the pathfinder classification of the original). `ObstacleSum` is an order-independent
  sum of the obstacles; if it changes, running movements re-plan.
- **Escape cells:** if a unit (radius 12 cells) or its destination (radius 8) stands inside an obstacle, cells of the obstacle may be entered with a surcharge of 4. This prevents
  trapped units and is a deliberately named simplification, not original behaviour.
- `IsFootprintLegal` is the terrain/footprint part of `BuildAssistant::isLocationLegalToBuild` (§16).
- `AIUpdateInterface` follows planned paths (`PathStatus` `Found`/`Partial`/`Blocked`), re-plans when `ObstacleSum` has changed, limits the turn (`TurnRatePerTick`,
  `FixedTrig` received `Atan2`, `AngleDifference`, `Normalize`) and puts ground units on the terrain height. A partial or missing path ends the command; there is no
  silent substitute movement.
- Limits: point-shaped units (no real blocking of cells by other units), a flat A* with a budget (no hierarchy, no bridges), approximated footprint.
  Since Phase 10 (§23) there is a limited local avoidance movement against other units (`AvoidLocalBlockers`, not part of path planning) as well as real
  crush/squish/topple collision as soon as two footprints overlap (`ResolveFootprintCollisions`); complete steering/avoidance (flocking) remains open.

## 15. Vision and fog of war (Phase 6)

`VisionMap` holds one byte per player and cell (bit 0 visible, bit 1 explored) and is recomputed every `VisionIntervalTicks` = 3 ticks; the vision hash belongs to the state hash
(snapshot v5). Revealing is circular, without a height line of sight.

- Objects with a vision range reveal for their owner and its allies; `REVEAL_TO_ALL` reveals for everyone; construction sites reveal only their `BoundingCircleRadius`.
- `CanPlayerSee` excludes contained entities and undetected `STEALTHED` entities; `EntitiesVisibleTo` delivers in ascending entity ID order. These queries are the only
  source of information of the skirmish AI (§18).
- `FireWeaponCommand` on a target invisible to the player is rejected with `TargetNotVisible`.
- Bug fix: `STEALTHED` is only set if `CAN_STEALTH` exists (innate, `StealthUpgrade`, `GrantStealth`); before, it was set regardless of that and made all objects appear stealthed.
- Limits: no height occlusion, update only every 3 ticks, no detector module (stealthed units stay hidden until the module exists).

## 16. Construction, selling, repair and rally points (Phase 6)

A structure is a real entity with `UNDER_CONSTRUCTION` from the start; its health rises linearly with the work of the builder over `BuildTime`
(`SimulationWorld.Construction.cs`). `CheckConstruct` checks in this order: builder exists → permission → `BuilderOf` (builder matches the template via `CommandCatalog`) →
template → `CanBuildTemplate` (`STRUCTURE`, `Buildable` Yes/Only_By_AI, side) → prerequisites (`PrerequisitesSatisfied`: science and object lines, only finished objects) →
credits → building site (`IsFootprintLegal` plus explored). The costs are debited at the start; cancelling refunds them.

- **Selling:** `SaleDurationTicks` = 179, proceeds = `BuildCost` × `SellPercentage` (now typed). Selling ends running production of the building.
- **Repair:** free of charge (`AIUpdateInterface.IsRepairable`), a simplification.
- **Rally point:** `SetRallyPointCommand` sets the point to which newly produced units walk.
- Snapshot v6 carries construction, sale and rally point state; the replay v6 the six commands.
- Limits (as of Phase 6, see §23 for the Phase 10 state): `QueueProduction` checked neither prerequisite nor capability of the building, `MaxSimultaneousOfType`
  was not checked. Repair is (and remains) free of charge – that is not a simplification, see §23.

## 17. Power plant modules and CommandCatalog (Phase 6)

`PowerPlantUpgrade` (`EnergyBonus` on upgrade), `PowerPlantUpdate` (extend rods, passive) and `OverchargeBehavior` (energy bonus with health loss, switches itself off at low health) change the energy production of the
player via `IWorldServices.AddEnergyProduction`; `ObjectTemplate.EnergyBonus` and `CommandSets` are new. `TemplateCatalog.Commands` (`CommandCatalog`) resolves `CommandSet` → `CommandButton` and
answers `OffersBuild` (which object can build or produce what); the AI uses it for roles (§18). Limits (as of Phase 6): no capture behaviour, no
toggle command for overcharge, reset also removes the bonus. Since Phase 10: `OverchargeCommand` and a reduced power plant capture (`CaptureCommand`), §23.

## 18. Skirmish AI (Phase 6)

`SkirmishAi` (`Simulation/Skirmish/SkirmishAi.cs`) is a pure command issuer per computer player: every 15 ticks (`ThinkInterval`) it decides in a fixed order – workers,
buildings, construction sites, repair, science, special powers, army, rally points, army orders, suppliers – and writes exclusively typed commands (`FireInterval` 5). It is an independent new development,
not a port of the script system of the original.

- **Roles** result from modules and `KindOf` (command center, supply building, production building per class, power plant, defence, builder, supplier, combat unit), never from names.
- **Information rule:** the AI plans only with `EntitiesVisibleTo`, a memory of seen enemies (`MemoryTicks` 900, revisit every `RevisitTicks` 3600) and the public start positions
  (`HintPositions`). The test `SkirmishAiOnlyUsesWhatItSees` checks in a 60-tick window that it uses nothing it does not see or remember.
- Constants: `RetryTicks` 300, `OrderRefreshTicks` 150, attack threshold 8–30 units (`MinAttackThreshold`/`MaxAttackThreshold`), at most 400 placement checks per think step (`PlacementChecksPerThink`), `DefenseRadius` 450,
  `EngageRadius` 260, repair below 70 % health.
- **`SkirmishMatchScenario`** (`SimTool ai-match`): compiles the content without map overlays, builds the terrain, places the supply warehouses of the map, gives every faction its start objects, 10,000 starting money and a science point every 2,700 ticks (substitute for the non-existent rank system, credited by the system) and lets all computer players run until victory or `--max-ticks`. Exit code 0 only if run 2 and the continuation from a snapshot
  are tick-identical (`SkirmishMatchOptions.Standard`: world seed `0xC0FFEE06`, AI seed `0x5EED0006`, 108,000 ticks, checkpoint 300).

## 19. Results of the AI-versus-AI runs (30 September 2026)

*Historical (30 Hz, state of this section): the tables, victory ticks and hashes here are superseded; authoritative is `baselines/reference-manifest.json` (since `ea52f87` at 60 Hz), the current values are in §43.9.*

Four maps (Alpine Assault, Armored Fury, Bitter Winter, Death Valley) × four pairings, at most 45,000 ticks; **each of the 16 runs is identical to run 2 and the snapshot continuation**.
15 runs end with a winner, one (Alpine Assault, America versus China) stays undecided up to the limit.

| Map | Pairing | Result |
|---|---|---|
| Alpine Assault | China, GLA | China, tick 38,655 |
| Alpine Assault | America, China | stalemate at 45,000 |
| Alpine Assault | GLA, America | GLA, tick 10,980 |
| Alpine Assault | ChinaTankGeneral, AmericaAirForceGeneral | China Tank General, tick 22,335 |
| Armored Fury | China, GLA | GLA, tick 7,335 |
| Armored Fury | America, China | China, tick 15,840 |
| Armored Fury | GLA, America | GLA, tick 6,330 |
| Armored Fury | ChinaTankGeneral, AmericaAirForceGeneral | China Tank General, tick 16,695 |
| Bitter Winter | China, GLA | China, tick 28,515 |
| Bitter Winter | America, China | China, tick 18,480 |
| Bitter Winter | GLA, America | GLA, tick 9,465 |
| Bitter Winter | ChinaTankGeneral, AmericaAirForceGeneral | China Tank General, tick 23,565 |
| Death Valley | China, GLA | China, tick 21,600 |
| Death Valley | America, China | China, tick 20,565 |
| Death Valley | GLA, America | GLA, tick 8,355 |
| Death Valley | ChinaTankGeneral, AmericaAirForceGeneral | China Tank General, tick 19,095 |

America never won in Phase 6: the four America factions had no supply economy because `ChinookAIUpdate`/`TransportAIUpdate` were missing (present since package E1, repetition below). That is not a balance statement
but a side finding of the module gap. The retail reference match (`SimTool reference-match`, 13 factions, no start object skipped) ends after Phase 6 with
winner player 12 (`FactionGLAStealthGeneral`) at tick 26,880, replay 27,181 ticks, final hash `5C370ED383EC9F6B`, replay file 949,193 bytes (SHA-256 `DCEE89309520371C0288EDB44309DA212AEE167D129343B6BB55BEA9F08A78C2`), 27 checkpoints;
run 2 (chain hash `EB8BDC1482BC20DF`) and the continuation at tick 13,590 are identical. 27,607 commands (1,240 production, 7,194 movement, 13 docking, 18,591 shots) in 7.8 s.

### Repetition after package E1 (Phase 8: Chinook, transports, tunnels, projectile flight time)

Same invocations (`SimTool ai-match … --factions <A>,<B> --max-ticks 45000`, default seeds), all 16 runs, without selection. **Every run is identical to run 2 and
the snapshot continuation**, and a second, independent evaluation with a freshly built tool delivered the same 16 results.

| Map | Pairing | Phase 6 | Package E1 |
|---|---|---|---|
| Alpine Assault | China, GLA | China, tick 38,655 | **stalemate at 45,000** |
| Alpine Assault | America, China | stalemate at 45,000 | **America, tick 10,575** |
| Alpine Assault | GLA, America | GLA, tick 10,980 | GLA, tick 18,870 |
| Alpine Assault | ChinaTankGeneral, AmericaAirForceGeneral | China Tank General, tick 22,335 | China Tank General, tick 22,155 |
| Armored Fury | China, GLA | GLA, tick 7,335 | GLA, tick 9,240 |
| Armored Fury | America, China | China, tick 15,840 | **America, tick 9,390** |
| Armored Fury | GLA, America | GLA, tick 6,330 | GLA, tick 30,735 |
| Armored Fury | ChinaTankGeneral, AmericaAirForceGeneral | China Tank General, tick 16,695 | China Tank General, tick 17,430 |
| Bitter Winter | China, GLA | China, tick 28,515 | China, tick 32,490 |
| Bitter Winter | America, China | China, tick 18,480 | **America, tick 19,980** |
| Bitter Winter | GLA, America | GLA, tick 9,465 | GLA, tick 18,240 |
| Bitter Winter | ChinaTankGeneral, AmericaAirForceGeneral | China Tank General, tick 23,565 | China Tank General, tick 34,515 |
| Death Valley | China, GLA | China, tick 21,600 | China, tick 39,555 |
| Death Valley | America, China | China, tick 20,565 | **America, tick 10,155** |
| Death Valley | GLA, America | GLA, tick 8,355 | GLA, tick 16,320 |
| Death Valley | ChinaTankGeneral, AmericaAirForceGeneral | China Tank General, tick 19,095 | China Tank General, tick 19,035 |

Honest assessment: **America now wins 4 of 4 runs against China** (previously 0 of 4, including the former stalemate), **against GLA still 0 of 4**, and
the Air Force General pairing still loses 4 of 4. The former stalemate (Alpine Assault, America versus China) has disappeared, in exchange there is **a new stalemate**
(Alpine Assault, China versus GLA: China stands at the end with 29 objects, GLA with 7, among them command center, tunnel and workers; China attacks only once).
So 15 of 16 runs still end with a winner. The America victories against China are not attributable to the Chinook economy alone: in three of the
four victories America issues not a single dock command (the Chinooks supply on their own via the supply machine), but the Humvees (now instantiable,
`TransportAIUpdate`) and the flight time of the Chinese flame projectiles (Dragon Tanks no longer hit instantly) shift the balance of power as well;
a breakdown by cause was not measured. GLA victories take longer on average (tunnel healing, flight time of the rockets). No balance statement.
## 20. Limits of Phase 6

- **Navigation:** point-shaped units (since Phase 10, §23, with limited local avoidance movement and real crush/squish/topple collision on overlap, but without real
  steering/flocking), flat A* with a budget, approximated footprint with escape cells (surcharge), locomotors only via `Surfaces`; a partial or missing path ends the
  command. Of 462 start position pairs, 6 are only partially walkable.
- **Vision:** circular, no height line of sight, update every 3 ticks, no detectors. (Both present since Phase 10: `StealthDetectorUpdate`
  was added with the Phase 7/8/9 blocker modules, a height line of sight for detectors since Phase 10, §23; the circular vision range/revealing itself
  (`VisionMap`, §15) remains without height occlusion.)
- **Construction:** `QueueProduction` without prerequisite/capability check, `MaxSimultaneousOfType` unchecked, repair free of charge. (The first two fixed since Phase 10,
  §23; repair is still free of charge, but that is not a simplification – see §23.)
- **Power plants:** no capture, no toggle command for `Overcharge`, reset also removes the bonus. (The first two present since Phase 10, as a
  reduced `CaptureCommand` and `OverchargeCommand` respectively, §23.)
- **AI:** special powers not classified, start positions of the opponents are public knowledge (`HintPositions`), only supply warehouses placed from the map, starting money and science points from the system, no aircraft/airfields,
  (since package E1 with Chinook economy, tunnels and transports, §21), army composition limited by instantiability, evenly matched pairings can run into a stalemate (1 of 16), without detectors stealthed survivors can stay
  hidden.
- **Open from earlier phases:** block 0.4 (RNG comparison with a compiled original; projectile flight time and `ChinookAIUpdate`/`TransportAIUpdate` since package E1, §21;
  `ToppleUpdate`/`StructureToppleUpdate`/`HeightDieUpdate` and real `CrushDie`/`SquishCollide` collision since Phase 10, §23). Partly clarified since Phase 10 (§23): effect
  of `CommandSet`/radar, veteran `ArmorSet`/`WeaponSet` (implemented, but without measurable effect in retail data), power plant capture and overcharge toggle command
  (reduced versions). Still open: `CommandButtonHuntUpdate` and `SpecialAbilityUpdate` as full implementations (they remain honest shell modules).

## 21. Phase 8, package E1: projectile flight time, transports, tunnels, Chinook

Rules with source references: `docs/GAME-RULES.md` §6i (flight time) and §6j (transports, tunnels, Chinook). Short version of the core changes:

- **Eight new modules** with catalog schema (`ModuleCatalog`, fields from the FieldParse tables, checked against the content), binding and tests:
  `DumbProjectileBehavior`, `MissileAIUpdate`, `PointDefenseLaserUpdate` (`ProjectileModules.cs`); `TransportAIUpdate`, `ChinookAIUpdate`,
  `HelicopterSlowDeathBehavior`, `TunnelContain`, `HelixContain` (`TransportModules.cs`). `PointDefenseLaserUpdate` and `HelicopterSlowDeathBehavior` were added
  because otherwise no Chinook of the Air Force General, respectively no Chinook at all, would have become instantiable.
- **Flight time in the core** (`SimulationWorld.Projectiles.cs`): `FireWeaponAt` replaces the instant damage; delayed hits are an ordered list in the global
  state (hash, snapshot) and are evaluated after the destruction phase (tick phase 5a); projectile objects come into being via the spawn queue, their launch data
  (`ProjectileLaunch`) lie in the queue entry. New events `ProjectileLaunched` (27) and `WeaponImpact` (28); events with a location carry `HasPosition` and
  X/Y/Z. New skip reason `CreationSkipReasons.ProjectileNotInstantiable` (10).
- **Fixed point:** Bézier points from the Bernstein form (no accumulated forward differences), curve length after `BezierSegment::getApproximateLength` with recursion limit 24,
  flight ticks `ceil` on raw values (`(d + v − 1) / v`), directions via `Fixed.Sqrt`, gravity `GameData.ini Gravity / 900` per tick²
  (`TemplateCatalog.GravityPerTickSquared`), angles via `FixedTrig`. No `float`/`double` (`SourceRules`).
- **Tunnel network:** `TunnelTracker` per player in the world state (hash and snapshot); `TunnelContain` is an `IContainerModule` on the shared network,
  `IWorldAttached` connects it on registration (also after restore), `ISellModule` maps `onSelling`.
- **Commands 26/27** (`MoveAndEnterCommand`, `EvacuateCommand`), `CommandTypes.Version = 9`; **snapshot v7, replay v7** (older ones are rejected with a clear message).
- **Skirmish AI:** Chinooks as suppliers, tunnels (healing and shortcut), transports (load/unload infantry); see §19 for the evaluation.

**Measurements (30 September 2026, all executed twice and compared):**

| Quantity | Value |
|---|---|
| Full inventory (`IniTool compile`) | 228 INI files (28 excluded), 65,875 blocks, 12,574 definitions, 16,380 modules, 333,061 keys, 18,321/19,871 references (1,550 fallbacks), snapshot 144,895,033 bytes, SHA-256 `09B84AC13823DC02C250F6BC95507BC58E13CF270E028E0DC1B5E55AA5391AA5` |
| Retail base (`--maps exclude`) | 146 INI files, 12,550 definitions, snapshot 144,284,158 bytes, SHA-256 `2A31BC2A83D63A58752FD5F98B7C47215978AD8DD3C1B2870A83DA31DDDEAFE7` |
| Compatibility baseline | 749 entries (previously 741): 16 new `deferred-field` (presentation fields of the new modules: ropes/rotor wash of the Chinook, particles/bones/sound of the helicopter crash handling, exit/display fields of `HelixContain`), 8 `module` entries are dropped (now implemented), `compatibility-module` 4,133 → 3,974 usages |
| Modules | 205 types, 85 implemented, 1,470 of 2,115 objects instantiable (69.5 %), 15,543 of 19,553 usages (79.5 %) |
| Stress test | final hash `ED6F9329F59662E3`, chain hash `3AECEB0579E460AF` (§10) |
| Retail reference match | 13 factions, America now with Chinook supply (no `supply note`); winner player 1 (FactionAmerica) at tick 13,800, replay 14,101 ticks, 1,197,235 bytes (SHA-256 `80BB3FF93421C4D7EB1F7FEA2ADCF3BBA5BB0A2B006F828EB5C3A78199AF3B98`, two recordings byte-identical), 36,880 commands, 428 deliveries with 114,450 credits, final hash `4DCB46FE3D2E8214`, chain hash `795E3C9868716E1B`, continuation from tick 7,050 identical |

**Limits:** projectiles do not collide (item 9 of the handoff); decoys, jammers, cruise altitude, veterancy-dependent detonation effects, rappelling,
firing from containers, airfield healing of the Chinook, nemesis target selection of the tunnel defenders and bunker buster effects are missing; Chinook altitude change and
helicopter crash are kinematic instead of via `PhysicsBehavior`. 40 of 157 projectile weapons fire a non-instantiable object and visibly
(`CreationSkipped`) fall back to the projectile-less flight time path.
## 22. Phase 9: script VM, mission session and state formats

Since Phase 9 the simulation additionally carries the execution of the World Builder scripts (`Scripting/`), the mission session and its state (`Mission/`); design,
source references, coverage tables, deviations and measured values are in `docs/SCRIPT-ENGINE.md`, campaign and savegame in `docs/CAMPAIGN-AND-SAVEGAMES.md`.

- **New world accesses** (`World/SimulationWorld.Scripting.cs`): `HasPlayer`, `SetMoney`, `SetRelationship`, `TransferOwnership` (change of owner incl. energy production),
  `RequestSpawn(…, healthPercent)`, `MaterializePendingSpawns()` (creates requested spawns immediately, like `newObject` in the original). No command code, no change to the snapshot (version 7)
  or replay (version 7); `CommandTypes.Version` stays 9.
- **State:** `MissionSnapshot` version 1 (`cncnew-mission-state`) embeds the unchanged world snapshot as raw JSON and adds script state (`ScriptEngine.Describe`, in
  one `IStateWriter` pass for hash and file), command sequences per player and the start options; `MissionSession.StateHash` = world and script hash.
- **Determinism:** all script collections are sorted; random timers draw from `LogicRandom`; the first evaluation time of delayed scripts is name-derived instead of random
  (deviation 3 in `docs/SCRIPT-ENGINE.md` §8). The test `SourceRules` applies unchanged to the new code and passes.
- **Not replayable:** script actions (create, kill, money, change of owner, relationships) are direct world changes, not recorded commands; missions are compared via snapshots,
  not via `ReplayRunner`.

## 23. Phase 10, Part B item 9: topple, height die and real crush/squish collision

Sources (file, function) and compatibility questions are at the modules themselves (`Modules/BuiltIn/ToppleAndFallModules.cs`); this section summarises semantics, simplifications and
measured values. All three modules are pure `CnCNew.Data` consumers (no wall-clock time, no `float`/`double`, no threads) and pass `SourceRules`.

### `ToppleUpdate` (`ToppleUpdate.cpp`)

An upright object (tree, lamp post, fence) that is rammed by an object with `CrusherLevel` > 1 (`onCollide`, here `SimulationWorld.Collision.cs` → `TryTopple`) topples: a
scalar angular velocity/acceleration drives an accumulation from 0 up to a fixed limit (`π/2 − π/64`, as in the original); if the limit is reached with positive
velocity, `BounceVelocityPercent` damps the velocity until it falls below a fixed threshold (0.01) and the object comes to rest. `KillWhenStartToppling`
kills immediately (no fall animation); otherwise `KillWhenFinishedToppling` (default on) kills with the death type `TOPPLED` as soon as the toppling comes to rest, and optionally also the
stump created via `StumpName` (`KillStumpWhenToppled`). `ToppleLeftOrRightOnly` restricts the fall direction to the nearer of the two object axes rotated by ±90°
(`angleClosestTo`) – this influences where corpse and stump actually come to lie, not only the looks. **Not modelled** (purely cosmetic, since no 3D transformation
exists): the plane realignment rotation from `applyTopplingForce` (`ReorientToppledRubble`), topple/bounce FX and the particle/sound effects of the hit; these fields are
`Opaque` in the catalog schema. A mismatch of the three percent fields can (as in the original) lead to a stable limit cycle that never falls below the threshold – not a bug
of the port but a property of the formula; the test fixture chooses values that reliably converge within a few ticks.

### `StructureToppleUpdate` (`StructureToppleUpdate.cpp`)

Reacts to the normal death of the building (`DieMuxData`, default mask): after a random delay (`MinToppleDelay`/`MaxToppleDelay`, drawn from `LogicRandom`) the
building topples in a direction derived from the attacker position (plus ±π/8 jitter, also from `LogicRandom` – deterministic, not the non-synchronised
`GameLogicRandomValueReal` of the original) via a sinusoidally accelerated accumulation, damped by `StructuralIntegrity`/`StructuralDecay`, up to `π/2`. During the toppling
`CrushingWeaponName` fires ground hits along the fall line as soon as the tilt angle is ≤ π/6 (`applyCrushingDamage`), via the existing weapon infrastructure
(`IWorldServices.FireWeaponAt`). **Simplified:** the original fires a grid of shots per tick over the entire building width (`doDamageLine`); ported is only the
foremost point of the line (`m_lastCrushedLocation` progress), the lateral spread is dropped – less area damage under a collapsing building, not none; the
module reports this explicitly once per object (`SimEventKind.CreationSkipped`, `CreationSkipReasons.SettingNotSimulated`, text "StructureToppleUpdate crushing grid sweep"). FX lists,
the salvo cadence (`Min/MaxToppleBurstDelay`) and the `OCL`/`AngleFX` debris lists are `Opaque` (pure looks); the building changes no model state and is never removed itself
(the module is its only die module and triggers no destruction – like the original rubble, which persists as an object).

### `HeightDieUpdate` (`HeightDieUpdate.cpp`)

A pure height test, every tick after `InitialDelay`: dies (`IWorldServices.Kill`) as soon as the Z position falls below the target height (terrain height plus `TargetHeight`, or – with
`TargetHeightIncludesStructures` – the height of the highest `STRUCTURE` object within its own `BoundingCircleRadius`, if greater), unless `OnlyWhenMovingDown` is set and the
object is currently not moving downwards. A contained object (transport) is never checked. `SnapToGroundOnDeath` sets the Z position exactly to the terrain height on dying; even
without the flag the same line prevents sinking below the terrain. **Not modelled:** the bridge/tunnel layer height, which the original takes into account in addition to the flat
terrain height (no path layers in this simulation) and `DestroyAttachedParticlesAtHeight` (pure particle clean-up looks, `Opaque`).

### Real collision for `CrushDie`/`SquishCollide`

Both modules had existed since Phase 5 only so that their objects become instantiable; their effect (crush flags and crush death respectively) was never triggered because there was no
collision detection (cf. the comments at the modules themselves). Since Phase 10 `AIUpdateInterface.Move`/`MoveStraight` (`Modules/BuiltIn/Phase5Modules.cs`) calls, after every
position change of a ground object, `IWorldServices.ResolveFootprintCollisions(mover, stepX, stepY)` (implemented in the new file `World/SimulationWorld.Collision.cs`):

- **Topple** (`TryTopple`): if the bounding circles of attacker and target overlap and the attacker has `CrusherLevel` > 1, `ToppleUpdate.ApplyTopplingForce` is called on the
  target (direction from the attacker to the target, velocity = current velocity of the attacker) – no alliance check, as in the original.
- **Squish** (`TrySquish`): if the target is not an ally of the attacker (`RelationshipOf`), its `SquishCollide` is present, the two bounding circles overlap with a target radius reduced to 1.0
  (as in the original: "the tank must really hit the infantry") and the actual step of this tick (`stepX`/`stepY`, not a velocity recomputed from
  `Angle`/`Speed` – this avoids sign errors from `Sin`/`Cos` rounding exactly on the driving line) reduces the squared distance to the target, the
  target dies immediately (`DamageType CRUSH`, `DeathType CRUSHED`, damage `HUGE_DAMAGE_AMOUNT`, unresistable in its amount, but passed through as normal damage).
- `mover.Template.CrusherLevel`/`CrushableLevel` are new, strictly typed fields on `ObjectTemplate` (`Templates/ObjectTemplate.cs`, `Object` keys `CrusherLevel`/
  `CrushableLevel`, default 0/255 as in the original); `CrushableLevel` is not evaluated yet (in the original only for `canCrushOrSquish`, which does not exist in this simulation –
  every unit with `SquishCollide` is squishable, regardless of its own `CrushableLevel`; a future phase can add this).

### Local avoidance movement (`AvoidLocalBlockers`, not an original function)

Not an original mechanism but a deliberately simple addition so that ground units do not pass through each other without complaint: before `AIUpdateInterface` applies the planned step to the
next waypoint, `AvoidLocalBlockers` checks whether the target position lies within the bounding circle of another living, mobile unit (`Template.Locomotor` set
– buildings already block via `NavigationGrid`); if so, the step vector is rotated one after the other by four fixed angles (±45°, ±90°) and checked again, the first free
attempt wins. If all four attempts remain blocked, the unit moves unchanged into the blocker (which may trigger `ResolveFootprintCollisions` there). **Honest
limits:** this is a limited avoidance, no steering/no flocking – it looks only one step ahead, does not coordinate with the blocker and can oscillate with an
oncoming unit; it works only with an existing `NavigationGrid` (not in the terrain-less test mode) and never changes the planned path itself, only the single
step.

### Non-negotiable entries

Catalog schema and binding: `src/CnCNew.Data/Registry/ModuleCatalog.cs`, `src/CnCNew.Simulation/Modules/BuiltIn/ToppleAndFallModules.cs`,
`src/CnCNew.Simulation/Modules/BuiltIn/BuiltInModules.cs` (registration). Tests: `tests/CnCNew.Simulation.Tests/Tests.Collision.cs` (five test cases: crush/squish,
`ToppleUpdate` including a continuation test after a snapshot in the middle of the fall, `StructureToppleUpdate`, `HeightDieUpdate`, local avoidance movement), registered in
`tests/CnCNew.Simulation.Tests/Program.cs`. Compatibility baseline after `IniTool compat-baseline`: 756 entries (previously 749, see §11). Additive extensions to shared
files that other agents could be editing in parallel: two new methods on `IWorldServices`/`SimulationWorld` (`AvoidLocalBlockers`, `ResolveFootprintCollisions`, both in the
new file `World/SimulationWorld.Collision.cs`), two new fields on `ObjectTemplate` (`CrusherLevel`, `CrushableLevel`), one new entry in the binding list
`BuiltInModules.cs` and two minimal calls (no logic change) in the movement code of `AIUpdateInterface` (`Modules/BuiltIn/Phase5Modules.cs`).

## 24. Phase 10: command button abilities, local side

Details, source references and measured values in `docs/SCRIPT-ENGINE.md` §5/§7/§8/§9; this section names only the new interfaces.

- **New module** `SpecialAbilityUpdate` (`Modules/BuiltIn/SpecialPowerModules.cs`): a `SpecialPowerModuleBase` shell without an effect of its own (see
  `docs/GAME-RULES.md` §6d); catalog schema in `CnCNew.Data.Registry.ModuleCatalog` (31 fields used in the content, source `SpecialAbilityUpdate.cpp`).
  Module types implemented now 88 of 205 (previously 87); instantiability 1,530 of 2,115 objects (72.3 %, previously 70.6 %), module usages
  covered 15,730 of 19,553 (80.4 %, previously 80.0 %); compatibility baseline **751** entries (previously 747 — five new `deferred-field` entries for
  the sound/particle fields of the module, one `MISSING module` entry is dropped; `IniTool compat-baseline` rewritten, subject of review).
- **New, additive API** (no existing signature changed):
  - `CnCNew.Simulation.Templates.CommandCatalog.TryGetSpecialPowerAbility(string buttonName, out string specialPower)`: resolves a `CommandButton`
    (`Command = SPECIAL_POWER*`) globally to its `SpecialPowerTemplate` name; `CommandCatalog.Build` fills the table in addition to the
    existing build lists.
  - `MissionSession.LocalPlayerOf(ScriptProgram, string humanSideName = "")` (additional parameter, default unchanged) and
    `MissionOptions.HumanSideName` (additional record parameter with default `""`): the host can name the side of the human viewer,
    independently of `playerIsHuman` (Generals' Challenge maps without a marked human side). `CampaignController.StartCurrentMission` gets
    the same additional parameter; `CampaignTool audit|playthrough` a new `--human-side <Name>`.
- **State format:** `MissionSnapshot.Version` **2** (previously 1): `options.humanSideName` added; older mission states are rejected with
  `SnapshotFormatException` (no silent migration). World snapshot (7), replay (7) and `CommandTypes.Version` (9) are unchanged.
- **Determinism:** `SpecialAbilityUpdate` adds no state of its own (the inherited reload machine of `SpecialPowerModuleBase` covers it);
  the `SourceRules` test passes unchanged. `CommandCatalog`'s new table is built from the definitions, not changed at run time.

## 25. Phase 10: construction/rule limits, veteran sets, height line of sight, capture

Source and brief: `docs/handoffs/HANDOFF-PHASE10.md` item 10 (continuation of the parts A/B left open in Phase 9). New files: `World/SimulationWorld.PowerAndCapture.cs`
(overcharge/capture command, the `MaxSimultaneousOfType` counter shares `World/SimulationWorld.Construction.cs`). Changed files: `Commands/Commands.cs` (`CommandTypes.Version` 9 → 10,
codes 28/29, rejection reasons 35–38), `Replay/ReplayFormat.cs` (encoding of the two new commands, container format itself unchanged), `Events/SimEvent.cs` (`StructureCaptured` = 29),
`Templates/ObjectTemplate.cs` (`MaxSimultaneousOfType`, `VeteranArmor`/`VeteranWeapon`), `World/SimulationWorld.cs` (`ActiveArmor`/`ActiveWeaponIndex` by veterancy level, `IsDock`,
`AddEnergyProduction` neighbourhood), `World/SimulationWorld.Construction.cs` (`CanProduceTemplate`, `ExceedsMaxSimultaneousOfType`, `CanQueueProductionTemplate`, public
`CheckConstruct`), `Modules/BuiltIn/Phase5Modules.cs` (`UpgradeMuxModule.Executed`, `ProductionUpdate.Queue`/`CountInQueue`), `Modules/BuiltIn/SpecialPowerModules.cs`
(`SpecialPowerModuleBase.AvailableOnFrame`), `Modules/BuiltIn/Phase5BlockerModules.cs` (`StealthDetectorUpdate`, height line of sight), `Terrain/TerrainMap.cs` (`HasHeightLineOfSight`),
`Skirmish/SkirmishMatchSession.cs` (`CreateSave` lock), `Testing/ReferenceMatchScenario.cs` (production selection now respects prerequisites).

### Construction rules (item 1)

- **Prerequisite and capability check for units:** `QueueProductionCommand` so far checked only `IsInstantiable`, ownership and money; `ConstructCommand` (buildings) had
  had `CanBuildTemplate`/`PrerequisitesSatisfied` since Phase 6. `CanProduceTemplate` (`SimulationWorld.Construction.cs`) transfers the same rule to produced units: the
  template must not be a `STRUCTURE` (those are placed only via `ConstructCommand` – `Player::canBuildMoreOfType`'s own `checkProductionInterface` distinction in
  `Player.cpp`), `Buildable = No` is locked, `Only_By_AI` only for computer players, the side must match that of the producer (an empty side on both sides counts as a free pass, as with
  buildings). `PrerequisitesSatisfied` (unchanged, §16) is now also called for units. New rejection reason `CannotProduceTemplate` (35); `MissingPrerequisite` (12, already
  present) is reused because the meaning is identical. Source: `Player::canBuild` (`Player.cpp`), whose `BSTATUS_*` case distinction does not distinguish between buildings and
  units.
- **`MaxSimultaneousOfType`:** `ObjectTemplate.MaxSimultaneousOfType` (0 = unlimited) is now typed (`ThingTemplate::getMaxSimultaneousOfType`). `ExceedsMaxSimultaneousOfType`
  counts the living, not dead objects of exactly this template of the player plus – only for non-`STRUCTURE` templates – all entries of the same template still standing in any `ProductionUpdate` queue
  of this player (`ProductionUpdate.CountInQueue`, newly public); source `Player::canBuildMoreOfType`/`countExisting` (`Player.cpp`). Applies to
  `ConstructCommand` and `QueueProductionCommand` alike. New rejection reason `ProductionLimitReached` (36). **Not modelled:** `MaxSimultaneousLinkKey` (a
  `NameKeyType` grouping of several templates under one shared counter); in `game/content` the key occurs in 17 files, but never together with
  `MaxSimultaneousOfType` at exactly the grouped place that would make the difference visible – the gap is documented, not measured to be without consequence.
  Measured effect on real data: `AmericaInfantry.ini` (Colonel Burton, `MaxSimultaneousOfType = 1`), `AirforceGeneral.ini`, `TankGeneral.ini` and 21 further object files carry
  the key; the skirmish AI previously had no awareness of the cap whatsoever and could (without the server-side prohibition that is new here) have requested any number of copies of an actually unique
  hero unit – `SkirmishAi`'s own `PrerequisitesMet` pre-check still does not know `MaxSimultaneousOfType`, so it now relies on this server-side
  barrier as a net (a rejected command is only a lost think step for the AI, not an error).
- **Repair costs – not a misbehaviour but an already correct port:** checked against `DozerAIUpdate.cpp` (`privateRepair`/`DozerPrimaryStateMachine`'s healing step:
  `health = maxHealth * getRepairHealthPerSecond() / LOGICFRAMES_PER_SECOND`, then `attemptHealingFromSoleBenefactor`) and `RepairDockUpdate.cpp` (`action`:
  `healthToAddPerFrame = (maxHealth - health) / framesForFullHeal`) – **neither of the two repair paths in the original deducts money.** The previous doc wording "Repair:
  free of charge … a simplification" (§16/§20, `docs/GAME-RULES.md` §11) was therefore imprecise: it is not a simplification but the faithful port of both source functions. There is
  therefore intentionally **no** code change to the existing free repair.
- **Power plant capture and the `Overcharge` command** (`World/SimulationWorld.PowerAndCapture.cs`): `OverchargeBehavior` (implemented since Phase 6, `RetailMatchModules.cs`) never had
  a command that reaches `Toggle` – the button `CommandButton.ini`'s `Command = TOGGLE_OVERCHARGE` led nowhere. `OverchargeCommand` (type code 28) checks ownership,
  aliveness, completion and the presence of an `OverchargeBehavior` module (otherwise `CannotToggleOvercharge`, 38) and then calls its already existing `Toggle` – no
  new game mechanic, only the missing wire to the existing one.
  Power plant capture, in contrast, is a real gap: in the original "capturing a building" hangs completely on `SpecialAbilityUpdate` (`ActionManager::canCaptureBuilding` requires
  `obj->hasSpecialPower(SPECIAL_*_CAPTURE_BUILDING)`, a channelled, science- and stealth-dependent ability), and `SpecialAbilityUpdate` is, with 64 blocked objects (36
  only by it), the second-largest remaining blocker (§11) – an independent module comprising several unrelated abilities (stealth, hijack, hack, combat drop, capture),
  whose complete implementation goes beyond the scope of this phase. `CaptureCommand` (type code 29, rejection reason `CannotCapture` = 37) instead reproduces only the **result**:
  any own, mobile (owning an `AIUpdateInterface`) object that is not under construction can directly take over a finished, not sold `STRUCTURE` target visible to the player
  if it either belongs to an enemy or carries `KINDOF_CAPTURABLE` and does not belong to an ally (reduced
  `ActionManager::canCaptureBuilding`), and if the actor already stands in range (`BoundingCircleRadius` + 2 cells, like the builder range in `UpdateTask`, §16) –
  **without** the channelling timer, science reservation or stealth detection check of the real ability. `ExecuteCapture` transfers the static `EnergyProduction` (as the
  script engine's own, unchanged `TransferOwnership` already did for `NAMED_TRANSFER_ASSETS_TO_PLAYER`) and additionally every dynamic bonus currently granted (an executed
  `PowerPlantUpgrade`, an active `OverchargeBehavior`) – that was the actual gap: both modules had no change-of-owner callback whatsoever, which `TransferOwnership` did not reach
  before either. Not transferred/reset: production queue, upgrades, garrison contents. Source for the limited cut of the mechanism: `CommandTypes.Capture`'s
  XML comment in `Commands.cs` cites the same place in `ActionManager.cpp`.
- **`CommandSet`/radar – checked, no further effect found:** `CommandSet` already has a real game effect (`TemplateCatalog.Commands`/`CommandCatalog.OffersBuild`, used by the
  skirmish AI for roles, §17/§18); what was missing was not "effect" but the `Overcharge` command described above – the same finding as above, documented here only from the
  `CommandSet` perspective. Radar was checked against the reference: `Player::hasRadar()` (`Player.h`/`.cpp`) is read nowhere in `GameLogic` outside these two
  files – no special power, no building, no vision rule depends on it. The only game effect of radar in the original is the minimap overview and
  radar pings, both presentation. `RadarUpdate`/`RadarUpgrade` (implemented since Phase 6) already carry the complete simulation contract (`Extended`/`IsExtendComplete`);
  there is no further simulation effect to catch up on.

### Veteran `ArmorSet`/`WeaponSet` selection (item 2)

`ObjectTemplate.VeteranArmor`/`VeteranWeapon` (a 4-element array each, index 1..3 = `Entity.VeterancyLevel`) are filled from `ArmorSet`/`WeaponSet` blocks with exactly one condition
`VETERAN`/`ELITE`/`HERO` (`ObjectTemplate.ResolveArmor`/`ResolveWeapon`, `VeterancyConditionIndex`). `SimulationWorld.ActiveArmor`/`ActiveWeaponIndex` choose first the veteran set matching the
current level, then `PLAYER_UPGRADE`, last the default set. **Modelling limit (unchanged compared with Phase 5, only extended):** only sets with exactly
one condition are resolved; a set that requires two conditions at the same time (e.g. `PLAYER_UPGRADE VETERAN`) is not distinguished – matching score-based over arbitrary combinations like the `SparseMatchFinder` of the
original remains open. **Measured effect on real data: none.** A full-text search over `game/content` for
`Conditions` with `VETERAN`/`ELITE`/`HERO` in an `ArmorSet` or `WeaponSet` block yields **zero hits** – not a single Zero Hour object uses these conditions at all, the
veterancy level in the original still acts mainly via weapon bonus and health (§6h in `docs/GAME-RULES.md`). The selection is thus correct for future data, but changes
neither `audit-coverage` nor `reference-match` on the real content.

### Detectors and height line of sight (item 3)

`StealthDetectorUpdate` already existed (since the Phase 7/8/9 blocker modules, `Phase5BlockerModules.cs`) and already detected stealthed objects in range correctly (checked:
`Tests.Phase5StealthDetector` passed unchanged) – the open gap was not the detection itself but the missing terrain occlusion. `TerrainMap.HasHeightLineOfSight`
(`Terrain/TerrainMap.cs`) is an independent line-of-sight check not ported from the original (the original resolves lines of sight via `PartitionManager`'s
object-to-object tests, which this project does not have): it walks the straight line between two eye points in steps of `CellSize` (at most `MaxLineOfSightSamples` = 64 samples;
anything longer than that is undersampled – a named limit without consequence at the vision/detection ranges that really occur) and compares at every sample the terrain height
(`HeightAt`) against the linearly interpolated height of the line of sight; the first sample at which the terrain exceeds the line blocks. This is pure occlusion: an observer standing
higher gets no range bonus, she is only blocked less often ("sees over" lower terrain up to the regular range limit); only terrain height counts,
buildings/trees/cliffs-as-obstacle are not geometry here. `StealthDetectorUpdate.Update` calls it between detector and target, with the "eye height" = ground position plus the full
`GeometryHeight` of the object (no eye height field of its own in the catalog – a named simplification); without terrain (synthetic fixtures) the behaviour stays exactly as before (every
target in range is reachable). The circular vision range/revealing computation itself (`VisionMap`, §15) remains unchanged without height occlusion – that would be a
shadow-casting computation per cell and considerably more expensive; only the detector-to-target check (pairwise, as expensive as the O(objects) scan that exists anyway) got the height check.

### Missing read/write APIs (item 4, `docs/PRESENTATION-INPUT.md` §10, `docs/GAME-HUD.md`)

All five wishes open in Phase 7/8 (package C3) are done, without exception as small, reading additions without a protocol change:

| API | File | Remark |
|---|---|---|
| `SimulationWorld.CheckConstruct(PlayerId, ConstructCommand)` | `World/SimulationWorld.Construction.cs` | public dry run of the exact same private check that `Submit`/`ExecuteCommands` also use |
| `ProductionUpdate.Queue` | `Modules/BuiltIn/Phase5Modules.cs` | `ImmutableArray<ProductionQueueItem>`, head first, template/cost/remaining ticks |
| `SpecialPowerModuleBase.AvailableOnFrame(IWorldServices)` | `Modules/BuiltIn/SpecialPowerModules.cs` | for shared powers reads the same shared ready frame as `IsReady` |
| `UpgradeMuxModule.Executed` | `Modules/BuiltIn/Phase5Modules.cs` | reads the `UpgradeMuxState.Executed` that was `protected` so far |
| `SimulationWorld.IsDock(EntityId)` | `World/SimulationWorld.cs` | replaces the module name comparison (`SupplyCenterDockUpdate` etc.) from outside |

None of these additions changes snapshot, replay or `CommandTypes.Version` – pure read accesses.

### Skirmish: every map can be started and ended (item 5)

New tool `SimTool skirmish-sweep <content-dir> [--max-ticks N]`: compiles the content once, then runs over every `.map` file, builds its terrain, and with at least
two start positions starts a short, two-sided `SkirmishMatchScenario` (`FactionAmerica` versus `FactionAmericaAirForceGeneral`, the first two from `ReferenceMatchScenario.Participants`)
with `--max-ticks` (default 3,000) as a hard budget. "Can be started and ended" here means: the session runs through its full life cycle (start objects, AI think steps, victory or
tick budget) without an exception – no balance or win rate measurement (for that `ai-match` and the table in §19).

**Result (30.09./1.10.2026, `game/content`, `--max-ticks 3000`):** 150 maps; 75 without any start waypoint (campaign maps, the same number as `MapTool audit`), 11 with exactly
one (not enough for two players); **all 64 maps with at least two start positions start and end cleanly** (0 crashes; since `--max-ticks` is small compared with the victory times measured
in §19, practically all end at the tick budget instead of with a winner, which is irrelevant for this measurement – what matters is the exact life cycle without an exception). Duration: 75 s.
With that, the acceptance "all skirmish maps can be started and ended", open since `docs/handoffs/HANDOFF-PHASE8.md`, is measured and passed for every map with enough start positions; maps without
(enough) start positions are not skirmish maps (campaign maps or single-start challenge maps) and not an adverse finding.

### Repetition of the 16-map AI overview after the `SharedReadyFrame` fix (§19)

All 16 runs from §19's "Repetition after package E1" table executed again with the same invocations (`SimTool ai-match … --max-ticks 45000`, default seeds), since the
`SharedReadyFrame` fix described in §12/§12a had landed after the last measurement of this table:

| Map | Pairing | Package E1 | Phase 10 (now) | Changed? |
|---|---|---|---|---|
| Alpine Assault | China, GLA | stalemate at 45,000 | stalemate at 45,000 | no |
| Alpine Assault | America, China | America, tick 10,575 | America, tick 10,575 | no |
| Alpine Assault | GLA, America | GLA, tick 18,870 | GLA, tick 18,870 | no |
| Alpine Assault | ChinaTankGeneral, AmericaAirForceGeneral | China Tank General, tick 22,155 | China Tank General, tick 23,325 | **yes** |
| Armored Fury | China, GLA | GLA, tick 9,240 | GLA, tick 9,240 | no |
| Armored Fury | America, China | America, tick 9,390 | America, tick 9,390 | no |
| Armored Fury | GLA, America | GLA, tick 30,735 | GLA, tick 30,735 | no |
| Armored Fury | ChinaTankGeneral, AmericaAirForceGeneral | China Tank General, tick 17,430 | China Tank General, tick 19,020 | **yes** |
| Bitter Winter | China, GLA | China, tick 32,490 | China, tick 32,490 | no |
| Bitter Winter | America, China | America, tick 19,980 | America, tick 19,980 | no |
| Bitter Winter | GLA, America | GLA, tick 18,240 | GLA, tick 18,240 | no |
| Bitter Winter | ChinaTankGeneral, AmericaAirForceGeneral | China Tank General, tick 34,515 | China Tank General, tick 27,990 | **yes** |
| Death Valley | China, GLA | China, tick 39,555 | China, tick 39,555 | no |
| Death Valley | America, China | America, tick 10,155 | America, tick 10,155 | no |
| Death Valley | GLA, America | GLA, tick 16,320 | GLA, tick 16,320 | no |
| Death Valley | ChinaTankGeneral, AmericaAirForceGeneral | China Tank General, tick 19,035 | China Tank General, tick 15,900 | **yes** |

Each of the 16 runs is still identical to run 2 and the snapshot continuation. Conspicuous and consistent: **all 12 pairings without `FactionAmericaAirForceGeneral`/`FactionChinaTankGeneral`
are tick-identical to the package E1 table, all 4 pairings with exactly these two generals deviate.** This is not a side effect of the `SharedReadyFrame` fix itself (which had already landed before
this measurement and is already evidenced in §12 with an `ai-match` run of its own) but of the `MaxSimultaneousOfType` barrier that is new here (above, item 1): `AirforceGeneral.ini`
and `TankGeneral.ini` both carry the key for their unique general units, and `SkirmishAi`'s own pre-check (`PrerequisitesMet`) never knew this barrier – before,
the AI could (as far as the template was otherwise instantiable and affordable) in theory request several copies of an actually unique unit, which the original forbids;
now the second attempt is correctly rejected. A rejected production is only a lost think step (`Backoff`) for the AI, but shifts subsequent decisions and
thus, via the deterministic cascade, the entire further course of the game – hence the changed, but still deterministic victory times. No balance statement.

### Skirmish savegame: decision (item 6)

> **Superseded by Phase 24 (§45.1 and §45.3):** the state of `SkirmishAi` is part of the session state, `SkirmishMatchSession.Save`/`Restore` exist and `CreateSave()` is gone.
> What follows is the decision of Phase 10 as it stood.

**Decision: no saving of a running skirmish session as a resumable live game in this phase; `SkirmishMatchSession.CreateSave()` refuses this loudly and documented,
instead of writing a silently incomplete savegame.**

Two things that sound similar but are to be distinguished:

1. **A completed or partially recorded run already saves and loads exactly today:** `ToReplay()` plus a `WorldSnapshot` checkpoint
   (`SkirmishMatchOptions.CheckpointInterval`) yield a tick-identical continuation – exactly that is what every "snapshot continuation" check in `ai-match`/`reference-match` has been checking
   since Phase 6/8/9, and it needs no AI state at all, because a replay only re-executes the commands already generated by the AI, never the AI itself.
2. **What is missing is pausing a still running, not completely recorded session and continuing it as a game that keeps deciding live.** For that
   `SkirmishAi` itself would have to survive the round trip: per computer player it holds considerable memory and process knowledge lying outside `World` (enemy/warehouse memory with
   timestamps, order and retreat timer tables, production-in-progress tracking, rally/scouting targets, bad-spot avoidance, a `SageRandom` stream of its own – the
   complete field list is in `SkirmishAi.cs`), none of which `WorldSnapshot` carries. Writing only the world would continue every AI with an erased memory
   on loading – not a clean restart but a quietly wrong one: the AI immediately "discovers" again enemies and warehouses it had long known, and forgets running production and
   retreat time locks, without anything reporting an error.

A `SkirmishAiSnapshot` that closes gap 2 is real, but limited continuation work (a flat state dump, no new mechanic) and deliberately not part of this phase.
Until it exists, `SkirmishMatchSession.CreateSave()` is the one, documented, loud failure point for saving a live session (test: `Tests.Skirmish.cs` →
`SkirmishSessionRefusesToSave`), instead of a save routine that silently drops the AI and delivers a broken savegame.

### Measured numbers (1 October 2026, `game/content`, unchanged compared with Phase 9 where not noted otherwise)

- `SimTool audit-coverage`: 2,115 objects, **1,494 instantiable (70.6 %) – unchanged**, 621 blocked, fingerprint `2B9D902AEC46A6DE…` unchanged (no new
  simulation module requirement from Phase 10).
- `SimTool audit-modules` with table of contents: 19,553 module usages, 15,639 covered (80.0 %) – **unchanged**.
- `IniTool compat-check`: 747 entries – **unchanged** (`MaxSimultaneousOfType` is an object top-level field, not a module field, and was never listed as `deferred-field`; the new
  typing therefore changes no baseline).
- `SimTool reference-match`: **changed**, because `ReferenceMatchScenario`'s random production selection now respects `CanQueueProductionTemplate` (before, it could choose templates
  whose prerequisite buildings are never built in this scenario – the scenario deliberately builds no buildings except the start command centers, §7a) – 13 factions, 46,284 commands
  (0 rejected), winner player 10 (`FactionGLA`) at tick 17,580, final hash `EC8267B594CC4D8A`, two fresh runs and a snapshot continuation at tick 8,940 tick-identical; previously (Phase
  9) winner `FactionAmerica` at tick 13,665, `D4779BE413835B6D`. Neither winner nor hash is a quality attribute of this scenario (pure random production without construction buildings);
  what matters is 0 rejected commands (the selection is now completely valid again) and the unchanged determinism/continuation guarantee.
- `MapTool audit`: untouched by Phase 10 (no change to `CnCNew.Formats`/`SageMapDecoder`/`TerrainMap.Build`/`Classify`; `skirmish-sweep`'s 150/150 decoded, 0 failed
  maps with identical 75/11/64 numbers confirm this indirectly).
- `dotnet build CnCNew.slnx`: still 0 errors, 21 known warnings with a clean `CnCNew.Client.Stride` `bin`/`obj`.
- `tests/CnCNew.Simulation.Tests` (synthetic and `--content-dir`) as well as `tests/CnCNew.Game.Tests` (without and with `--content-dir`): all pass.

## 26. Phase 11: world interfaces for the script VM

Source and brief: `docs/handoffs/HANDOFF-PHASE11.md` (remaining script families); the script side is in `docs/SCRIPT-ENGINE.md` §4–§5a. All new world states follow the same rule as
`SimEvent`'s position since Phase 8: **they are only written if they deviate from the default**. A run that uses none of these interfaces keeps the state shape and
hash from before (`SimTool reference-match` still winner FactionGLA at tick 19,200, final hash `28BE35C1174E369F`; `SimTool stress` final hash `56882F763EABEF67`, the same before and after this
phase – the number `ED6F9329F59662E3` named in earlier states comes from the time before Phase 9 and was no longer reproducible already at the start of this phase). Snapshot,
replay and command versions therefore stay unchanged (`WorldSnapshot` 7, replay 7, `MissionSnapshot` 2, `CommandTypes.Version` 10); an older state loads unchanged, a new one
with used interfaces would not be readable for an older version – deliberately not treated as a version bump because no existing run is affected (decision, subject of review).

- **Permanent reveal** (`Terrain/VisionMap.cs`, `World/SimulationWorld.Scripting.cs`): a third bit per cell and player (`PermanentBit`, always together with "visible" and
  "explored"); `BeginRefresh` leaves it standing, everything else is recomputed. `RevealMapPermanently`/`ExploreMap`/`ShroudMap`/`ExploreArea`/`ShroudArea`/`RevealAreaPermanently` set
  or clear it (sources: `ScriptActions::doRevealMapEntire`, `doRevealMapEntirePermanently`, `doShroudMapEntire`, `doRevealMapAtWaypoint`, `doShroudMapAtWaypoint`,
  `doRevealMapAtWaypointPermanent`; the original reveals "a short glance" with `doShroudReveal` + `undoShroudReveal` in the same frame, the result is "explored, not visible", and
  exactly that is what `ExploreMap`/`ExploreArea` set). A state with bit 2 is checked on loading (bit 2 requires bits 0 and 1).
- **Build switches** (`PlayerState.CanBuildBase`/`CanBuildUnits`, `SetCanBuild`, `CanBuildStructures`/`CanBuildUnits`): `Player::allowedToBuild` – placing structures (`ConstructCommand` →
  `CannotConstruct`) and queueing units (`QueueProductionCommand` → `CannotProduceTemplate`) respectively is locked as long as the switch is off; no new rejection reason, hence no new
  `CommandTypes.Version`. Serialised only if off (`canBuildBase`/`canBuildUnits`).
- **Read/write APIs:** `SimulationWorld.WarehouseBoxes(id)` and `SupplyWarehouseDockUpdate.SetCashValue` (`WAREHOUSE_SET_VALUE`: `ceil(cash / ValuePerSupplyBox)` boxes, like
  `SupplyWarehouseDockUpdate::setCashValue`), `SpecialPowerInfo.RadiusCursorRadius` (from `SpecialPower.ini`, for `SKIRMISH_FIRE_SPECIAL_POWER_AT_MOST_COST`),
  `CommandCatalog.TryGetButton` (command kind, `Upgrade`, `WeaponSlot` of every button; basis of the new paths for upgrade/`FIRE_WEAPON` buttons),
  `ScriptEngine.IsNamedSelected` (read access to the selection reported by the client).

## 27. Phase 12: mission object modules, override relationships, change of owner

The missions of the campaign name objects that are only instantiable with seven further modules. They are in `Modules/BuiltIn/Phase12Modules.cs` (schemas in `ModuleCatalog`, bindings in
`BuiltInModules`, test `Phase12ModulesCreateAndBehave`), each module with a source reference; what a module leaves to the presentation is stated at it.

| Module | Source | Behaviour |
| --- | --- | --- |
| `FireWeaponWhenDamagedBehavior` | `FireWeaponWhenDamagedBehavior.cpp` | after activation (`StartsActive` or matching upgrade) a damaging hit of an accepted `DamageTypes` type with at least `DamageAmount` fires the reaction weapon of the current body state at the object itself (`forceFireWeapon(obj, position)`); the continuous weapon of the state fires as often as it is ready; reload = `DelayBetweenShots`, state in the object |
| `HordeUpdate` | `HordeUpdate.cpp`, `AIUpdateInterface::evaluateMoraleBonus` | counts objects with `HordeUpdate` in the `Radius` (allies, `KindOf`, possibly exact template, `RubOffRadius`); from `Count` on the weapon bonus condition `HORDE` applies (bit from `NameTablesPhase5.WeaponBonusCondition`) |
| `FireWeaponPower` | `FireWeaponPower.cpp` | the special power reloads and makes the object fire `MaxShotsToFire` shots of its default weapon at object/location/itself, `DelayBetweenShots` per shot (without approach/turn time of the AI) |
| `BaikonurLaunchPower` | `BaikonurLaunchPower.cpp` | at the target location the `DetonationObject` of the power's owner comes into being (death and weapon of the object do the rest); without a location only the shared reload (the original only opens the door); a template that cannot be created is reported as a skipped creation |
| `TechBuildingBehavior` | `TechBuildingBehavior.cpp` | a destroyed tech building becomes neutral; `PulseFX` and the captured model are presentation (only the rate remains) |
| `RadiusDecalUpdate` | `RadiusDecalUpdate.cpp` | pure presentation (target circle of a superweapon), without state |
| `FlightDeckBehavior` | `FlightDeckBehavior.cpp` | honest **shell**: fields bound, flight deck operation (take-off/landing/repair) not simulated, the object can exist (the carrier of `MD_GLA03`, whose destruction is the mission objective) |

State of these modules is only written if it deviates from the initial value (no version bumps; `WorldSnapshot` 7, replay 7, `CommandTypes.Version` 10 remain). New in the catalog: 98 of 205
module types, 1,680 of 2,115 retail objects instantiable (79.4 %), 15,903 of 19,553 module usages covered (81.3 %), compatibility baseline 765 entries (re-measured, `baselines/compatibility.json`).

**Override relationships** (`SimulationWorld.RelationshipOverride`): a hook `Func<Entity, Entity, Relationship?>` that `RelationshipBetween` consults before the player relationship; `AreEnemies(Entity, Entity)`,
target selection and combat (`SimulationWorld.Combat`) and the target acquisition of the units (§33; until Phase 16 `MissionSession.DriveCombat`) use the entity variant. The hook belongs to the script engine (`docs/SCRIPT-ENGINE.md` §5, §8 item 19) and is `null`
as long as no override is set – runs without it stay byte-identical (retail reference match and stress test unchanged).

**`IWorldServices.TransferOwnership`:** the module interface now offers the change of owner (for `TechBuildingBehavior`; `SimulationWorld.TransferOwnership` from §22 also moves the energy production).
**`StealthUpdate.ScriptUnstealthed`:** switch for `NAMED/TEAM_SET_STEALTH_ENABLED`, only saved if set.

## 28. Phase 13: aircraft, parachute, uplink and vehicle modules, team queues

Phase 13 (`git log b620a50..HEAD`) closes the mission modules on which the reinforcement and victory chains of the campaign depended. All modules are in the project with catalog schema (`ModuleCatalog`), binding (`BuiltInModules`),
`Describe`/`Load` (state only if it deviates from the initial value) and test (`Tests.Flight.cs`); what the original solves via physics or bone transformations is replaced by kinematics and named at the module.
Flying locomotors still travel straight ahead to a target point (`PreferredHeight`, new `SpeedLimitZ`).

| Module | Source | Behaviour |
| --- | --- | --- |
| `DeliverPayloadAIUpdate` | `DeliverPayloadAIUpdate.cpp` | state machine of the transport aircraft: approach to the delivery point (`DeliveryDistance`, `DoorDelay`, `DropDelay`, `DropOffset`/`DropVariance`, `PutInContainer`), dropping the cargo (parachute via `ParachuteContain`, `IOverrideDestination` for the exact location), `ConsiderNewApproach` (`MaxAttempts`), leaving the map (`RecoverFromOffMap`, `HeadOffMap`, `ClosestEdgePoint`); if the aircraft dies, the crew dies (`TransportContain.OnDie`) |
| `ParachuteContain` | `ParachuteContain.cpp` | the parachute carries the cargo: free fall down to `OpenDistance`, then descent; landing in water kills, fall damage `FreeFallDamagePercent`; sway/damping fields are presentation |
| `JetSlowDeathBehavior` | `JetSlowDeathBehavior.cpp` | `SlowDeathBehavior` for aircraft: on the ground only ground effects and disappearing; in the air participation in the lottery (`ISlowDeathCandidate`/`SlowDeathLottery`), afterwards straight on with the share `FallHowFast` of gravity, secondary effects `DelaySecondaryFromInitialDeath`, impact `FXHitGround`/`OCLHitGround` |
| `CountermeasuresBehavior` | `CountermeasuresBehavior.cpp` | after the upgrade `NumberOfVolleys · VolleySize` decoys; a guided missile at the aircraft (`ICountermeasuresModule.ReportMissile`) is marked with `EvasionRate` (one draw of the logic stream), redirects to the next decoy after `MissileDecoyDelay` and then does no damage (`DetonateProjectile(damage:false)`); decoys are real entities, moved kinematically |
| `ParticleUplinkCannonUpdate` | `ParticleUplinkCannonUpdate.cpp` | special power handler (`ISpecialPowerHandler`): sequence charge/alignment/beam/decay, beam times (`WidthGrowTime`, `BeamTravelTime`, `TotalFiringTime`), sweep (`SwathOfDeathDistance`/`Amplitude`, rotation formula and S-curve of the source), `TotalDamagePulses` damage pulses (`DamagePerSecond`, `DamageRadiusScalar`, beam radius `LaserBeamRadius` new in the template model) along the swath; `ParticleUplinkCannonUpdate` was the only blocker of 8 objects and of the victory objective of `MD_GLA03` |
| `UndeadBody` | `UndeadBody.cpp` | `ActiveBody` with two lives: lethal damage leaves one hit point, starts the second life (`SecondLifeMaxHealth`, full health) and lets the matching slow death modules draw lots for which one *begins* |
| `BattleBusSlowDeathBehavior` | `BattleBusSlowDeathBehavior.cpp` | the first death of the bus: throw (`ThrowForce` divided by mass), landing and effects, afterwards the second life |
| `SubObjectsUpgrade`, `AnimationSteeringUpdate` | `SubObjectsUpgrade.cpp`, `AnimationSteeringUpdate.cpp` | pure model animation: honest shells with `WouldUpgrade` and without state respectively (the object can exist) |

Accompanying changes: `LocomotorInfo.SpeedLimitZ`; `AIUpdateInterface.OrderMove` is virtual, `OrderEvacuate(…, thenExit)` for the return flight; `IUpgradeModule.WouldUpgrade` (default method, overridden by
the upgrade modules) delivers the readiness of the upgrade buttons (`docs/SCRIPT-ENGINE.md` §8 item 18); `DetonateProjectile(…, damage)`; the script side (reinforcement with transport, team queues,
start actions) is in `docs/SCRIPT-ENGINE.md` §5/§8.

Measurement (3 October 2026, `game/content`; without a version bump: `WorldSnapshot` 7, replay 7, `MissionSnapshot` 2, `CommandTypes.Version` 10): module catalog **107 of 205** implemented (183 simulation,
22 presentation modules), **1,735 of 2,115** retail objects instantiable (82.0 %), module usages covered 16,085 of 19,553 (82.3 %), compatibility baseline **771** entries. Largest remaining
blockers by objects (of these only by this module): `JetAIUpdate` 34/34, `MobMemberSlavedUpdate` 27/27, `RailroadBehavior` 25/25, `SlavedUpdate` 20/7, `PropagandaTowerBehavior` 14/13, `UpgradeDie` 13/0,
`HiveStructureBody` 11/4, `RiderChangeContain` 11/11, `DeployStyleAIUpdate` 10/10, `StructureCollapseUpdate` 10/10, `ParkingPlaceBehavior` 9/9. Retail reference match: winner FactionGLA at tick 15,000,
`final hash` (run 1) `532F19F360C59550` (previously tick 19,200, `28BE35C1174E369F`: the new modules – already measured at the state `5416d81`, before the team queues – change the scenario;
repetition and snapshot continuation identical over 15,301 ticks, chain hash `5B799AA9398CE305`); stress test (216,000 ticks) unchanged `56882F763EABEF67`, chain hash `B8E663493F2C73D5`.

## 29. Phase 13, continuation: air deliveries, special powers at the map edge, death weapons, formations

The first part of Phase 13 (§28) added modules for reinforcements and the mission chains. The continuation closes gaps that concerned the **playability** of the special powers – until then all
power objects with `CREATE_AT_EDGE_*` and `DeliverPayload` did nothing (only `CreationSkipped` events), and `FireWeaponWhenDeadBehavior`/`InstantDeathBehavior` requested their weapon only as an event that was never executed.

| Building block | Source | Behaviour |
| --- | --- | --- |
| `DeliverPayload` nugget (`PayloadDelivery`, `DeliveryRun`) | `ObjectCreationList.cpp` `DeliverPayloadNugget::create` | `FormationSize` transports at the start point (laterally offset, alternating, `FormationSpacing`, `WeaponConvergenceFactor`, `WeaponErrorRadius`, `DelayDeliveryMax`), the `Payload` objects (each optionally packed in `PutInContainer`) are created along with them and put into the transport; `SpecialPowerCompletionDie` gets the creator (only the first transport/the first payload). With `createOwner=false` (`USE_OWNER_OBJECT`) the object itself is the transport. Not modelled: `StartAtMaxSpeed` (a physics impulse), the target marker |
| `OCLSpecialPower` | `OCLSpecialPower.cpp` `doSpecialPowerAtLocation` | the three edge positions (`CREATE_AT_EDGE_NEAR_SOURCE`/`_NEAR_TARGET`/`_FARTHEST_FROM_TARGET`: nearest edge point, farthest corner + 300) and the target as the second point of the list; without terrain still `NoTerrainEdge` |
| `DeliverPayloadAIUpdate` (run) | `DeliverPayloadAIUpdate.cpp` | `PreOpenDistance` (only on approach), `FireWeapon` (the transport fires its weapon at target + offset, the occupant is deleted), visible payload (`VisiblePayloadTemplateName`: missiles are launched at the target with `VisiblePayloadWeaponTemplate`, anything else falls), `ParachuteDirectly`, `SelfDestructObject`, dive with fire (`DiveStart/EndDistance`, strafing with the default weapon), start delay; the entire run is in the state |
| `PhysicsBehavior` (fall) | `PhysicsUpdate.cpp` | an object released in the air (bomb) falls with gravity, lands and rests (`KillWhenRestingOnGround` kills); `HeightDieUpdate` mostly ends it one tick earlier |
| Death weapons | `WeaponStore::createAndFireTempWeapon` | `FireWeaponWhenDeadBehavior`/`InstantDeathBehavior` fire their weapon at their own location (`IWorldServices.FireTempWeapon`); the event remains for the presentation |
| `FireWeapon` nugget | `FireWeaponNugget::create` | the source fires the weapon once at the second point; `Attack`, `CreateDebris`, `ApplyRandomForce` remain `NuggetNotSimulated` (effects/physics without game effect and a target command without a consumer respectively) |
| `ParticleUplinkCannonUpdate` (waypoint mode) | `ParticleUplinkCannonUpdate.cpp` `initiateIntentToDoSpecialPower(way)` | a script (`NAMED_USE_COMMANDBUTTON_ABILITY_USING_WAYPOINT_PATH`) offers the module the path from the nearest waypoint (`PrepareScriptedPath`); the beam travels with `ManualFastDrivingSpeed` from waypoint to waypoint and stops at the end (the fork in the path is chosen in order, not randomly) |
| Formations | `AIStates.cpp` `AIFollowWaypointPathState` (`m_moveAsGroup`), `AIGroup::getSpeed` | `TEAM_FOLLOW_WAYPOINTS(_EXACT)` with `asTeam`: every member keeps its offset to the group centre, all follow one team waypoint (the first member at the destination advances it, the others switch), nobody is faster than the slowest member (`AIUpdateInterface.SpeedCap`, only set on a group march) |
| World hooks | `ScriptEngine::adjustToppleDirection`, `GameLogic::setBuildableStatusOverride` | `SimulationWorld.ToppleDirectionOverride`/`BuildableOverride` (script tables, in their state), `BuildableOf(template)` in production, construction and `SkirmishAi` |

Measurement (3 October 2026): retail reference match and stress test do not change through these building blocks (the scenario has no death weapons with splash, no special powers and no group marches; measured `532F19F360C59550` and `56882F763EABEF67` respectively);
`CampaignTool drive` still victory and defeat 24/24; test `FlightStrikePowerDeliversBombs` (two bombers, six bombs, damage to the target, completion message, resumption from the snapshot), `FlightUplinkFollowsAScriptedWaypointPath`,
`ScriptTeamFollowsAPathAsAFormation`.

## 30. Phase 14: air, crate/special and structure/world modules

Three independently developed module groups (each in a worktree of its own, merged afterwards) close the blockers that `audit-coverage` listed at the top after Phase 13. All modules have catalog schema
(`ModuleCatalog`), binding (`BuiltInModules`), `Describe`/`Load` (state only if it deviates from the initial value) and tests; what the original solves via physics, model bones or rendering is named at the module as
an approximation. During the merge, two collision interfaces that had come into being in parallel were unified into **one** (`ICollideModule` with `WouldLikeToCollideWith`, `Entity.CollideModules`,
`SimulationWorld.DeliverCollisions` of the crate group; the distribution of the structure group was dropped), and `SpawnExtras`/`SpawnSetup` now carry `Enslaver` and `Producer` side by side (snapshot fields
`enslaver`/`producer`, only if set).

### 30.1 Air group (`AirModules.cs`, `JetAIUpdate.cs`, `AirWeaponModules.cs`)

| Module | Source | Behaviour |
| --- | --- | --- |
| `ParkingPlaceBehavior` | `ParkingPlaceBehavior.cpp` | Airfield: `NumRows·NumCols` parking spaces (reserveSpace/releaseSpace, an aircraft keeps its space until death or landing elsewhere), one runway per column with exactly one waiting aircraft (`reserveRunway`, `transferRunwayReservationToNextInLineForTakeoff`, `releaseRunway`), healing of parked aircraft (`HealAmountPerSecond`, every 6 ticks), production exit (`IProductionExit`: hangar position, helicopters at the helipad without a space), destruction kills everything parked/taking off/landing; **geometry computed instead of model bones** (space, taxiway, runway and approach points from building position, building orientation and geometry radii), `defectAllParkedUnits` and flight decks not modelled |
| `JetAIUpdate` | `JetAIUpdate.cpp` | State machine of the aircraft: hangar→parking space→align→load ammunition; a command to a parked aircraft is stored and triggers reserve runway→taxi→pause (`TakeoffPause`, waiting time behind the aircraft in front)→take-off (lift with the square of the runway fraction)→stored command; return with an empty `RETURN_TO_BASE` clip (return locomotor) or after `ReturnToBaseIdleTime`, approach point, landing permission, glide with minimum speed, roll-out, taxi to the space, reload countermeasures, reload weapon (proportionally `ClipReloadTime`); without an airfield the jet circles over the home position and takes `OutOfAmmoDamagePerSecond`; helicopters take off/land vertically at the helipad; locomotor sets `SET_TAXIING`/attack/return; kinematic instead of physics and pathfinding |
| `SpectreGunshipUpdate` | `SpectreGunshipUpdate.cpp` | Gunship: approach to a circle around the target (entry slope 0.5–0.8), transit locomotor up to the orbit, gattling only released in the orbit, every `HowitzerFiringRate` ticks target search in the crosshair (computer players also in the attack area), gattling aim point wanders with `StrafingIncrement`, howitzer after `HowitzerFollowLag` with random offset, departure after `OrbitTime`, the gattling is cleaned up with it; on maps with terrain the carrier creates the gunship at the map edge (`SpectreGunshipDeploymentUpdate`) |
| `MissileLauncherBuildingUpdate` | `MissileLauncherBuildingUpdate.cpp` | Silo door follows the reload of the superweapon: opens `DoorOpenTime` before readiness, open at readiness, wait after use, close (at most half the remaining time), one effect list per state; pure state sequence without a game effect of its own |
| `NeutronMissileUpdate` | `NeutronMissileUpdate.cpp` | Flight by hand: launch vertically, `DistanceToTravelBeforeTurning`, turn limited by `MaxTurnRate` (without arc functions) to the point above the target, then dive with half the speed, acceleration `RelativeSpeed` minus `ForwardDamping`, special height curve for `SpecialSpeedTime`; impacts on the ground (`kill`, so that the death modules run); without jitter, exhaust, decal |
| `NeutronMissileSlowDeathBehavior` | `NeutronMissileSlowDeathUpdate.cpp` | `SlowDeathBehavior` with up to nine explosion stages: after an (unrounded) delay toppling and damage in the outer radius (full in the inner radius, falling linearly, at least `MinDamage`, EXPLOSION/EXPLODED by armor), ground FX on activation; burning/scorch mark only rendering |
| `NeutronBlastBehavior` | `NeutonBlastBehavior.cpp` | On the death of the projectile within the `BlastRadius`: infantry and occupants die, technicals die, the remaining vehicles (no drones) become ownerless (neutral, stopped); air targets only with `AffectAirborne`, allies only with `AffectAllies` |
| `EMPUpdate` | `EMPUpdate.cpp` | EMP pulse: after `StartFadeTime` effect within the `EffectRadius` (default 200): infantry untouched, flying non-hardened aircraft die, buildings only faction buildings, otherwise paralysed for `DisabledDuration` (`Entity.EmpDisabledUntil`: no movement, no weapon), `DoesNotAffect=ALLIES` and own buildings (`DoesNotAffectMyOwnBuildings`) excepted; dies after `Lifetime`; two random draws on creation |
| `LeafletDropBehavior` | `EMPUpdate.cpp` | Leaflet bomb: on death it paralyses enemy infantry and vehicles within the `AffectRadius` (default 60) for `DisabledDuration`; as in the original the update function sleeps after the first tick as long as the delay has not expired |
| `BunkerBusterBehavior` | `BunkerBusterBehavior.cpp` | On the death of the bomb (with `UpgradeRequired` of the owner): occupants of the targeted garrison/tunnel container are forced out with the occupant weapon (100 damage) and killed, or die, detonation FX at the target, shockwave weapon there; crash FX every `CrashThroughBunkerFXFrequency` ticks during the kill-self wait; seismics only rendering |

Accompanying changes: `AIUpdateInterface.IsIdle`/`OrderEnter` virtual, `ActiveLocomotor` virtual, `OrderIdle` new, `Update` rests under EMP; `IProductionExit` with template-aware default methods
(`IsFreeToExitFor`, `ClaimExit(world, template)`); `ObjectTemplate.LocomotorSets`, `LocomotorInfo.Lift`, `WeaponInfo.ReloadsAtBase` (a dry `RETURN_TO_BASE` clip reloads only at the airfield);
`Entity.EmpDisabledUntil`; `MissileAIUpdate.Victim`/`IsKillingSelf`. **Limits:** ~~the nuclear missiles do not launch from the silo yet~~ and ~~a jet with a stored command appears "idle" to the script~~ have been **refuted** since Phase 15 (real-data tests: the launch via `UseSpecialPowerCommand` and `OCLSpecialPower` begins at the silo and kills the target; the `IsIdle` override of the jet is seen via the base type `AIUpdateInterface`, which the scripts use); disabling acts only on movement and weapons;
not modelled: defection of captured airfields, carrier decks, fog of war for the gunship, the patch 1.01 restriction of the EMP (needs a creator link).

### 30.2 Crate, collision and special group (`CrateModules.cs`, `SpecialsModules.cs`, `CleanupAndSpyModules.cs`, `BattlePlanModule.cs`)

| Module | Source | Behaviour |
| --- | --- | --- |
| `MoneyCrateCollide` | `CrateCollide.cpp onCollide/isValidToExecute`, `MoneyCrateCollide.cpp executeCrateBehavior` | A crate that is driven over pays `MoneyProvided` plus the first owned `UpgradedBoost` to the player of the toucher and disappears. Neutral touchers, targets without AI (except `BuildingPickup`), forbidden kinds, air crates and parachutes do not pay. |
| `VeterancyCrateCollide` | `VeterancyCrateCollide.cpp` | Raises the toucher, with `EffectRange` all own objects in range, by one level, with `AddsOwnerVeterancy` by the level of the carrier (pilot). Pilot only for own ground vehicles. Without AI and target object nothing is consumed, as in the original (level-up crates without AI stay lying). |
| `ConvertToCarBombCrateCollide` | `ConvertToCarBombCrateCollide.cpp`, `Object::defect`, `checkAndDetonateBoobyTrap` | The terrorist sent to the target turns a vehicle with `WeaponSet CARBOMB` into a car bomb (`IS_CARBOMB`, the world selects the CARBOMB weapon). The vehicle defects to the terrorist and inherits his veterancy level and script name (`ObjectNameTransferred`), the terrorist is consumed. A booby trap at the target goes off first. No aircraft, boat or existing car bomb. |
| `ConvertToHijackedVehicleCrateCollide`, `HijackerUpdate` | `ConvertToHijackedVehicleCrateCollide.cpp`, `HijackerUpdate.cpp` | Takes over an enemy ground vehicle (not `IMMUNE_TO_CAPTURE`, drone, loaded transport or already hijacked vehicle), sets `HIJACKED`, aligns the veterancy level and ends its order; the hijacker hides in the vehicle (without `EjectPilotDie` he is consumed), follows it and, when it is gone, appears visible and idle at its last position (from the air with a `ParachuteName` parachute). |
| `SabotageCommandCenterCrateCollide`, `…SuperweaponCrateCollide` | `Sabotage*CrateCollide.cpp` | A saboteur at the enemy command center restarts the charging of all special powers; likewise for `FS_SUPERWEAPON`/`FS_STRATEGY_CENTER`. It must be the target object of his order. |
| `SabotageMilitaryFactoryCrateCollide` | `SabotageMilitaryFactoryCrateCollide.cpp` | Barracks, war factory or airfield (no carrier) are disabled for `SabotageDuration`; simulated effect: the production queue stands still (`ProductionUpdate.DisableUntil`). |
| `SabotagePowerPlantCrateCollide` | `SabotagePowerPlantCrateCollide.cpp`, `Energy::setPowerSabotagedTillFrame` | Sets the "power sabotaged" tick of the enemy player (`SabotagePowerDuration`). Without a consumer in the simulation (no low-power state), only state. |
| `SabotageSupplyCenterCrateCollide` | `SabotageSupplyCenterCrateCollide.cpp` | Books up to `StealCashAmount` from the account of the enemy to that of the saboteur, limited to the enemy's credits. |
| `PilotFindVehicleUpdate` | `PilotFindVehicleUpdate.cpp` | An idle AI pilot searches every `ScanRate` for the nearest healthy (`MinHealth`) own vehicle in `ScanRange` that accepts its collision module, and walks there (`OrderApproachGoal`). Human players: sleeps. The one-time way back to the base is missing (no base centre). |
| `StickyBombUpdate` | `StickyBombUpdate.cpp` (`initStickyBomb`, `update`, `detonate`), `Object::checkAndDetonateBoobyTrap` | Sticks to a victim (follows it, `OffsetZ`), a booby trap sets `BOOBY_TRAPPED`. `detonate` damages with `GeometryBasedDamageWeapon` (both radii plus victim bounding circle, without relationship filter), clears the status and kills the bomb. If the victim dies, the trap goes off; allies do not trigger it. The script actions `NAMED_/TEAM_SET_BOOBYTRAPPED` put it at a random point of the victim outline (§30.4). |
| `GrantStealthBehavior` | `GrantStealthBehavior.cpp`, `StealthUpdate::receiveGrant` | The radius grows per tick by `RadiusGrowRate`; every living allied object of the `KindOf` with `StealthUpdate` permanently receives stealth. At the `FinalRadius` the marker disappears. |
| `WanderAIUpdate` | `WanderAIUpdate.cpp` | If an object is idle, it walks to a point 5 to 50 further in x and y (two draws from the logic stream), afterwards normal AI update; never sleeps. |
| `ProjectileStreamUpdate` | `ProjectileStreamUpdate.cpp` | Ring buffer (20) of the projectiles of a spray weapon with a gap on target change, cutting off at the front, self-destruction without projectiles and owner, `Points` for the drawing. Without game effect; the weapon does not create stream objects yet (`ProjectileStreamName` is untyped). |
| `CleanupHazardUpdate`, `CleanupAreaPower` | `CleanupHazardUpdate.cpp`, `CleanupAreaPower.cpp` | Searches every `ScanRate` for the nearest `CLEANUP_HAZARD` object in `ScanRange`, walks into weapon range and fires until it is gone; in area mode (the special power calls `setCleanupAreaParameters`) the search runs around the point until nothing remains; a player or script command aborts it. Only slot PRIMARY. |
| `SpyVisionUpdate`, `SpyVisionSpecialPower` | `SpyVisionUpdate.cpp`, `SpyVisionSpecialPower.cpp` | As long as it is active, the owner sees through the eyes of every enemy object of the `SpyOnKindof` (reference counter per object, revealed in the vision computation). Switches on and off by upgrade, special power (`BaseDuration` plus `BonusDurationPerCaptured`, with a container capped at `MaxDuration`) or self-timed, off on removal. |
| `AssistedTargetingUpdate` | `AssistedTargetingUpdate.cpp`, `Weapon::processRequestAssistance` | If a neighbouring weapon with `RequestAssistRange` fires at a victim, all free objects of the same kind in range attack with their slot weapon for `AssistingClipSize` shots. Feedback lasers are missing (rendering). |
| `BattlePlanUpdate` | `BattlePlanUpdate.cpp`, `Player::changeBattlePlan`, `applyBattlePlanBonusesForObject` | Cycle Idle, Unpacking, Active, Packing with the animation times. Active: `BATTLEPLAN_*` weapon bonus for all matching objects of the player (also new and taken-over ones), Hold the Line halves (`HoldTheLinePlanArmorDamageScalar`) the damage, Search and Destroy widens the revealing. Depending on the plan the center doubles hit points or vision respectively and detects stealth. While packing the troops are paralysed for `BattlePlanChangeParalyzeTime`. The plan comes via `SelectPlan` or `Initiate` (the choice is in `target.X`: 1 Bombardment, 2 Hold the Line, 3 Search and Destroy – a typed option field with a version bump of the commands is missing). |

Interfaces: hijack, car bomb, sabotage and pilot orders run via `AIUpdateInterface.OrderApproachGoal(world, goal, fromPlayer)` (no new `SimCommand`); new events `CrateCollected` 80,
`ObjectConverted` 81, `Sabotaged` 82, `ObjectNameTransferred` 83 (the script takes the name along, §30.4). **Approximations:** collisions fire only for movers (whoever stands on a crate collects it only on the
next move); hijack and car bomb do not take over the vision range; the pilot does not return to the base; spy vision does not change owner on capture; battle plan (since Phase 15 with the typed field `UseSpecialPowerCommand.Option` = `OPTION_ONE/TWO/THREE` of the command button, `CommandTypes.Version` 12, replay 9) scales only the revealing range
(not the vision range per unit), the bombardment gun counts as always centred; armor/vision scalars are derived instead of multiplied, to avoid fixed-point drift.

### 30.3 Structure and world group (`StructureModules.cs`, `RailroadModules.cs`, `MineModules.cs`, `BridgeModules.cs`, `WorldEffectModules.cs`)

| Module | Source | Behaviour |
| --- | --- | --- |
| `RailroadBehavior` | `RailroadGuideAIUpdate.cpp` (`update`, `loadTrackData`, `createCarriages`, `getPulled`, `updatePositionTrackDistance`, `FindPosByPathDistance`, `onCollide`) | The locomotive searches for the nearest map waypoint as an anchor, the waypoint links (`getLink(0)`) are the track (loop detected). Accelerates `(v+0.02)·Acceleration` up to `SpeedMax`, brakes behind `…Station`/`…Disembark`/`…PingPong` points, waits `WaitAtStationTime` (`SET_TRAIN_HELD` holds it there). Carriages from `CarriageTemplateName` or unconnected allied carriages behind the hook; a carriage without a puller becomes the lead car and coasts to a stop (`Friction`). Open track: car into the wings/end of the track (destroy). The locomotive kills from `KillSpeedMin` on (otherwise crush damage), train against train both, base structures/demo traps die, objects with physics are pushed. Not: audio, path lock at the station, physics impulse |
| `UpgradeDie` | `UpgradeDie.cpp` | On death the producer (`getProducerID`, entered by the creation list) loses the object upgrade `UpgradeToRemove`; without a producer nothing |
| `StructureCollapseUpdate` | `StructureCollapseUpdate.cpp` | After `onDie` a random delay, then collapse with `COLLAPSE` acceleration (gravity·(1−`CollapseDamping`)) until below the geometry height; the phases INITIAL/DELAY/BURST/FINAL each select an FX/OCL list (one logic draw per list), bursts every `MinBurstDelay..MaxBurstDelay` (large with 1/`BigBurstFrequency`). Shaking and sinking only model |
| `DeployStyleAIUpdate` | `DeployStyleAIUpdate.cpp` | States READY_TO_MOVE/DEPLOY/READY_TO_ATTACK/UNDEPLOY: a fire command in range starts the deploying (`UnpackTime`, status `DEPLOYED`), shots until then discarded and counted (`WeaponNotReady`); a move command packs up (`PackTime`) and drives afterwards; reversal of a running deploy/undeploy. The gate (`IFireGate`) is only asked on execution of an accepted fire command, never during the check (otherwise a discarded command would change the state). Not: guard auto-deployment, turret centring |
| `FireWeaponCollide` | `FireWeaponCollide.cpp` | If something touches the object while all `RequiredStatus` (e.g. `AFLAME`) and no `ForbiddenStatus` are set, the collision weapon fires at the toucher every time; `FireOnce` has no effect, as in the original |
| `FireSpreadUpdate` | `FireSpreadUpdate.cpp`, `FlammableUpdate::tryToIgnite` | If the object burns, it creates embers (`OCLEmbers`) and ignites the nearest still ignitable object in `SpreadTryRange` (3D), afterwards sleep `MinSpreadDelay..MaxSpreadDelay` |
| `BridgeBehavior`, `BridgeTowerBehavior`, `BridgeScaffoldBehavior` | `BridgeBehavior.cpp`, `BridgeTowerBehavior.cpp`, `BridgeScaffoldBehavior.cpp` | Damage/healing to the four towers in the same proportion (not with a tower as the source); death kills towers and objects without physics on the deck (own box), `BridgeDieFX/OCL` each exactly once after their delay; healing from rubble revives; a tower passes damage on to the other towers and the bridge, its death kills the bridge; the scaffold moves RISE → BUILD_ACROSS or TEAR_DOWN_ACROSS → SINK (destroyed) with slowing in the last quarter, driven only from outside. Towers via `SetTower`; no bridge terrain, no layers, no scaffold construction |
| `DemoTrapUpdate` | `DemoTrapUpdate.cpp` | If an enemy (not in the air, not an ignored kind) walks into `TriggerDetonationRange`, it fires `DetonationWeapon` (if set) and kills itself; allies block except with `AutoDetonationWithFriendsInvolved`; mode (proximity/manual/detonation) as module state instead of weapon slot (no command wired); `DetonateWhenKilled` |
| `MinefieldBehavior` | `MinefieldBehavior.cpp` | `NumVirtualMines`: a touch (by `DetonatedBy`, workers only with `WorkersDetonate`) triggers a virtual mine at the nearest footprint point (weapon, counter, health to the proportion, at least 0.1, `CreationList`), again only after `RepeatDetonateMoveThresh`; damage/healing reconcile counter and health; without a creator degeneration. Not: mine clearer immunity, scoot |
| `OCLUpdate` | `OCLUpdate.cpp` | Creates its list every `MinDelay..MaxDelay` (at the object or the map edge), the first attempt only starts the timer; `FactionTriggered`: rests while neutral, list by the `PlayerTemplate` side of the owner |
| `DynamicShroudClearingRangeUpdate` | `DynamicShroudClearingRangeUpdate.cpp` | Vision range grows from 0 to the template value, holds, shrinks to `FinalVision` (countdown `ShrinkDelay+ShrinkTime`), handed to the vision map every `ChangeInterval` (`IShroudRangeProvider`); decals presentation |
| `FireOCLAfterWeaponCooldownUpdate` | `FireOCLAfterWeaponCooldownUpdate.cpp` | Counts consecutive shots of the monitored weapon, creates the list after the cooldown/weapon change at `MinShotsToCreateOCL` with lifetime `OCLLifetimePerSecond`/cap; since the weapon slot model (§30.6) it reads the real slot, also SECONDARY |
| `FloatUpdate` | `FloatUpdate.cpp` | When activated (`Enabled`) the object floats on the water level of the water area; all shipped usages (buoys) deactivated, swaying only model |
| `WaveGuideUpdate`, `DamDie` | `WaveGuideUpdate.cpp`, `DamDie.cpp` | Disabled from the start; activated and set to `WaveGuide1` after `WaveDelay`, moves along the waypoint chain, destroyed at the end. Per tick damage to all points of the parabolic front below `PreferredHeight` (bridges excepted), wet, knocked over, `DamageAmount` water damage (death FLOODED); on death `DamDie` activates all `WAVEGUIDE` objects. Not: water level, shore effects |
| `CheckpointUpdate`, `EnemyNearUpdate` | `CheckpointUpdate.cpp`, `EnemyNearUpdate.cpp` | Checkpoint: every tick enemy/ally in vision range, open with an ally without an enemy, gate radius ±1/3 per tick (only state, pathfinding does not see it); EnemyNear: every `ScanDelayTime` nearest enemy in vision range, flag `IsEnemyNear` |
| `FirestormDynamicGeometryInfoUpdate` | `DynamicGeometryInfoUpdate.cpp`, `FirestormDynamicGeometryInfoUpdate.cpp` | After `InitialDelay` the geometry grows linearly in `TransitionTime` (back with `ReverseAtTransitionTime`); every `DelayBetweenDamageFrames` flame damage (by armor) to everything within the current bounding circle and not above `MaxHeightForDamage` |
| `WeaponBonusUpdate` | `WeaponBonusUpdate.cpp`, `TempWeaponBonusHelper.cpp` | Every `BonusDelay` living allies (and passengers) in the radius with the required kinds receive the condition `BonusConditionType` for `BonusDuration`; another bonus replaces the old one. Timers in the source (ends with it) |
| `CostModifierUpgrade` | `CostModifierUpgrade.cpp`, `Player::addKindOfProductionCostChange` | After the upgrade, objects with all `EffectKindOf` cost the owner `1+Percentage` (product, rounded down to an integer; on production and construction); death/destruction takes it back, capture moves it to the new owner |
| `BoneFXDamage`, `BoneFXUpdate` | `BoneFXDamage.cpp`, `BoneFXUpdate.cpp` | honest shells: effects on model bones are presentation, no state; a filled OCL slot makes the object non-instantiable. The presentation had no counterpart until Phase 35 (`docs/PRESENTATION-FX.md`, "Attached effects") |

Accompanying changes: for reference fields `FieldSchema` permits trailing tokens like the original (`UpgradeToRemove = Upgrade_X ModuleTag_13`); `TerrainMap` waypoints carry their id and the first link
(track, wave guide); new `IWorldServices` members and interfaces `IProducerReceiver`, `IFireGate`, `IShroudRangeProvider`, `IOwnerChangeReceiver`. **Limits:** bridge terrain, bridge layers and
scaffold construction are missing (only landmark bridges whose box is the deck); `FireOCLAfterWeaponCooldownUpdate` never had an effect before the slot model (§30.6) because every usage observes SECONDARY; `WaveGuideUpdate` raises no water; `WeaponBonusUpdate` keeps its timers in the beacon.

### 30.4 Script side (`ScriptEngine.Phase13.cs`)

| Action / condition | Source | Behaviour |
| --- | --- | --- |
| `NAMED_SET_HELD` | `ScriptActions::doNamedSetHeld` | `Entity.ScriptDisableMask` bit 1 (`DISABLED_HELD`): AI update and production rest; previously only recorded |
| `PLAYER_DISABLE_FACTORIES`, `PLAYER_ENABLE_FACTORIES` | `Player::setObjectsEnabled` | bit 2 (`OBJECT_STATUS_SCRIPT_DISABLED`) for all existing objects of the template of the player; production stands still, objects built later are not affected (as in the original) |
| `SET_TRAIN_HELD` | `ScriptActions::doNamedSetTrainHeld` | `RailroadBehavior.SetHeld`: the locomotive stays at the station |
| `NAMED_SET_EVAC_LEFT_OR_RIGHT` | `GarrisonContain::exitObjectViaDoor` | `GarrisonContain.EvacDisposition` (1 left, 2 right): the occupant appears in a pocket next to the building (`±¼` of the length, ½–2 widths to the side) and walks to a point ten widths away (three logic draws) |
| `NAMED_SET_BOOBYTRAPPED`, `TEAM_SET_BOOBYTRAPPED` | `ScriptActions::doNamedSetBoobytrapped`, `GeometryInfo::makeRandomOffsetOnPerimeter` | the trap object is requested, attached at the random point of the victim outline (box: outline, otherwise angle; rotated into the victim coordinate system) as soon as it exists (`_pendingTraps`, in the state) |
| `BRIDGE_BROKEN`, `BRIDGE_REPAIRED` | `TerrainLogic::isBridgeBroken/isBridgeRepaired` | true only for one update after the bridge object changes into or out of the rubble damage state (`_bridgeStates`, in the state; the first evaluation remembers the initial state) |
| Name transfer (`ObjectNameTransferred`) | `ScriptEngine::transferObjectName` | the script name of the consumed object (car bomb, hijack, pilot) afterwards belongs to the object that took over |

Not implemented (`CampaignTool coverage`; used in no campaign map, `CampaignTool usage` empty): `SKIRMISH_BUILD_BUILDING`, `SKIRMISH_BUILD_BASE_DEFENSE_FRONT/_FLANK`,
`SKIRMISH_BUILD_STRUCTURE_FRONT/_FLANK`, `SKIRMISH_ATTACK_NEAREST_GROUP_WITH_VALUE`, `SKIRMISH_PERFORM_COMMANDBUTTON_ON_MOST_VALUABLE_OBJECT`, `AI_PLAYER_BUILD_TYPE_NEAREST_TEAM`,
`NAMED_FIRE_WEAPON_FOLLOWING_WAYPOINT_PATH` – the first six belong to `AISkirmishPlayer` (build list, base radius, side `AIData`), which the simulation with the `SkirmishAi` substitute does not share.

### 30.5 The last nine blocker modules (`LastBlockerModules.cs`, `FerryModules.cs`)

| Module | Source | Behaviour |
| --- | --- | --- |
| `SalvageCrateCollide` | `SalvageCrateCollide.cpp` | Only `SALVAGER` take the crate. Order as in the original: `ARMOR_SALVAGER` always takes the next armor level, then `WEAPON_SALVAGER` with `WeaponChance` the next weapon level, then trainable units with `LevelChance` a veterancy level, otherwise money between `MinMoney` and `MaxMoney`. Random numbers are only drawn for chances not equal to 100 % and for min not equal to max. Levels lie at `Entity.CrateWeaponLevel`/`CrateArmorLevel` and select the `CRATEUPGRADE_ONE/TWO` WeaponSet and the `CRATE_UPGRADE_ONE/TWO` ArmorSet respectively. |
| `UnitCrateCollide` | `UnitCrateCollide.cpp` | The player of the toucher receives `UnitCount` objects `UnitName`, with the orientation of the toucher (a ring around the toucher instead of the collision search). An unknown or non-instantiable name leaves the crate standing and reports `CreationSkipped`. |
| `SabotageFakeBuildingCrateCollide` | `SabotageFakeBuilding.cpp` | An enemy `FS_FAKE` building receives unresisted damage in the amount of its maximum health (`DETONATED`) and falls apart immediately. |
| `SabotageInternetCenterCrateCollide` | `SabotageInternetCenterCrateCollide.cpp` | All internet centers of the victim switch off their `SpyVisionUpdate` for `SabotageDuration`; hackers in the hit center stand still for the duration (`HackInternetAIUpdate.DisableUntil`, no money). The marker `DISABLED_HACKED` at the center itself is not saved. |
| `SmartBombTargetHomingUpdate` | `SmartBombTargetHomingUpdate.cpp` | After `SetTargetPosition` (by `DeliverPayloadAIUpdate` at the drop) x/y moves every tick in flight to `Ziel·(1−s)+Position·s` with `s` = `CourseCorrectionScalar` clamped to [0,1]. The height is left to the fall, the zero point is rejected. |
| `SupplyWarehouseCripplingBehavior` | `SupplyWarehouseCripplingBehavior.cpp` | On the transition into `REALLYDAMAGED` the dock is crippled (`SetDockCrippled`): approach yes, admission never, a truck between entry and exit dies, one that has only approached is set idle with a forced supply wish. Every hit starts the healing rest `SelfHealSupression`, afterwards it heals `SelfHealAmount` per `SelfHealDelay`. An object without a dock is not crippled (the original would dereference null). |
| `RailedTransportAIUpdate` | `RailedTransportAIUpdate.cpp` | The ferry travels waypoint paths `<Präfix>Start%02d`/`End%02d` (up to 32) hop by hop, initially chooses the path with the nearest end point and is "in transit" on the way (dock closed). Within 5 units of the end point or when idle the dock opens. `ExecuteRailedTransport` takes the next path in turn (no command type – that would change the replay format), `OrderEvacuate` unloads via the dock. Player commands other than evacuate are ignored. No map of the retail content contains the ferry. |
| `RailedTransportContain` | `RailedTransportContain.cpp` | A `TransportContain`: after the last removal the dock opens; exit only with the dock open (otherwise `NotPermitted`); an exit command hands over to the dock (`unloadSingleObject`, unloads the first of the list). |
| `RailedTransportDockUpdate` | `RailedTransportDockUpdate.cpp` | A docker within `ToleranceDistance` is held, not selectable, turned towards the ferry and pulled over `PullInsideDuration` up to 6 units to the centre, then put into the ferry; unloading fetches the first passenger out and pushes him over `PushOutsideDuration` to the exit point (up to 3 units). Without model bones: waiting spot at the edge of the bounding circle, exit one main axis in front of the ferry. |

With that, every module of the catalog intended for the simulation is implemented (183 of 183); only the 22 presentation modules remain open.

### 30.6 The weapon slot model (`WeaponSets.cs`, `SimulationWorld.WeaponSlots.cs`, `WeaponSlotState.cs`)

Up to here every object fired one weapon (`DefaultWeapon`, `PlayerUpgradeWeapon`, `VeteranWeapon` …); `AssistedTargetingUpdate` and `FireOCLAfterWeaponCooldownUpdate` kept a slot bookkeeping of their own or never had an effect. The slot model
makes the active `WeaponSet` selection the only source for which weapon an object fires; the special selections of the other groups (car bomb, crate levels) feed it.

| Building block | Source | Behaviour |
| --- | --- | --- |
| Weapon sets and slots | `ThingTemplate::findWeaponTemplateSet`, `WeaponSet.cpp` | Every `WeaponSet` of the template is parsed strictly (primary/secondary/tertiary, `AutoChooseSources`, `PreferredAgainst`, `ShareWeaponReloadTime`, `WeaponLockSharedAcrossSets`); `DefaultWeapon` remains an alias of the primary slot of the default set. Only objects with more than one weapon carry slot state (`Entity.WeaponSlots`), single-weapon objects keep shape and snapshot. |
| Clips and reloading per slot | `Weapon.cpp` (`fireWeapon`, `reloadAmmo`) | Own clip, ready tick and last shot per slot. An empty clip waits `ClipReloadTime`, otherwise `DelayBetweenShots`; `ShareWeaponReloadTime` sets all slots to the same tick; `RETURN_TO_BASE` weapons never reload by time (ready tick `SleepForever`, only the airfield fills). |
| Best weapon choice | `WeaponSet::chooseBestWeaponForTarget` (reduced) | Checked from tertiary downwards, on a tie the lower slot wins; takes into account `AutoChooseSources` per command source (player/script/AI), the anti mask against the target, the damage by armor, the range and `PreferredAgainst`; a preferred weapon stays even while reloading. Weapons that cannot hurt the target are rejected (except `UNRESISTABLE`). |
| Locks | `Object::setWeaponLock`, `SetWeaponLockCommand`, `LockWeaponCreate` | Temporary (command button, special ability, `CleanupHazard`, `AssistedTargeting`): ends on an empty clip or a move command; permanent (`LockWeaponCreate`, `SWITCH_WEAPON`): only by release or set change. `LockWeaponCreate` is thereby no longer a shell module. |
| Set selection by conditions | `WeaponSetFlagsOf`, `WeaponSetResolver.Find` | Flags Veteran/Elite/Hero, `PLAYER_UPGRADE`, `CRATEUPGRADE_ONE/TWO` (from `Entity.CrateWeaponLevel`) and `CARBOMB` (from `IS_CARBOMB`); combined sets (`CRATEUPGRADE_ONE PLAYER_UPGRADE`) are found by sparse matching. A set change (`WeaponSetFlagsChanged`) builds the slots afresh (full clips, lock released, unless shared). |
| `AssistedTargetingUpdate`, `CleanupHazardUpdate` | as §30.2 | fire via `TryFireWeaponSlot` from the real slot and lock it temporarily; the clip bookkeeping of their own is dropped. |
| `FireOCLAfterWeaponCooldownUpdate` | as §30.3 | now observes the slot (also SECONDARY): current weapon, last shot, ready tick; creates the list after the series or immediately on the weapon change – in the retail content it thereby has an effect for the first time. |
| `JetAIUpdate`, `FireWeaponPower`, `SpectreGunship`, EMP, `DeployStyleAIUpdate` | as §30.1/§30.3 | `isOutOfSpecialReloadAmmo` holds when all `RETURN_TO_BASE` weapons are empty; reload time is the maximum over all slots; `FireWeaponPower` calls `ReloadAllAmmo` and fires the current weapon; the paralysis delays all slots (`DelayWeaponsUntil`); the gate of `DeployStyleAIUpdate` runs after the choice and before the consumption. |
| Commands and scripts | `FireWeaponCommand`/`SetWeaponLockCommand`, `FIRE_WEAPON`, `SWITCH_WEAPON` | `FireWeaponCommand` carries an optional slot and the command source; script command buttons fire from the button slot and lock it. **Version bumps:** `WorldSnapshot` 7 → 8, replay 7 → 8, `CommandTypes.Version` 10 → 11. |

**Approximations:** the weapon choice knows only `PREFER_MOST_DAMAGE`; range is a filter without approaching; turret and pitch conditions are missing; the garrison special cases of the damage estimate are missing; `FireWeaponPower` has no
AI attack command and no approach/departure; `AssistedTargetingUpdate` remains a module shot without the feedback lasers; `CleanupHazardUpdate` does not set the veteran flag on creation.

### 30.7 Measurement (3 October 2026, `game/content`, fresh)

- Module catalog **183 of 205** implemented – all 183 simulation modules (open only the 22 presentation modules); instantiability **2,115 of 2,115 (100.0 %)**; module usages covered **16,558 of 19,553 (84.7 %)** (the rest are
  presentation modules); compatibility baseline **751** entries. Real data: 2,477 weapon sets on 2,115 objects, 187 objects with slot state.
- Retail reference match: winner FactionGLA at **tick 13,890**, `final hash` (run 1) `76F22CB0CA79BEEF`, chain hash `35F54CAD40A43E6E`; second run and snapshot continuation at tick 7,095 identical (the values of Phase 13 – tick 15,000,
  `532F19F360C59550` – are superseded: more producible objects and the slot weapon choice change the scenario). Stress test (216,000 ticks) unchanged `56882F763EABEF67`, chain hash `B8E663493F2C73D5`.
- Versions: `WorldSnapshot` 8, replay 8, `CommandTypes.Version` 11, `MissionSnapshot` 2.

## 31. Addendum Phase 15 (4 October 2026): turret state, staged capture, winged flight

Three behaviour gaps that the staged demos (`docs/DEMO-SCENARIOS.md`) uncovered are closed; the details are in documents of their own:

- **Turret state with fire clearance** (`docs/SIM-TURRET-AIM.md`): typed `Turret`/`AltTurret` blocks, `TurretState` per turret, the weapon of a slot controlled by the turret waits for the alignment;
  `WorldSnapshot` 8 → 9. The remark "turret and pitch conditions are missing" further up is thereby superseded. Reference match now tick 13,875, final hash `482FC8C52D507662`, chain `34AB50A397B0125C`.
  Addendum: permanent attack state (in `TurretState`, `PursueCommand` type 31 for the pursuit, replay 10, `CommandTypes.Version` 13) and idle scan (`TurretAI` idle, randomness from a hash stream of its own instead of `LogicRandom`) – reference match now tick 15,000, `01C01E3B91EAE18C`, chain `045DA5259A104F01`.
- **Staged building capture** (`docs/SIM-STAGED-CAPTURE.md`): for `SPECIAL_INFANTRY_CAPTURE_BUILDING` and `SPECIAL_BLACKLOTUS_CAPTURE_BUILDING`, `SpecialAbilityUpdate` executes approaching, unpacking, preparing
  (flag), change of owner and packing; the mentions of `SpecialAbilityUpdate` as a shell in §11, §23, §25 and §30 now apply only to the remaining abilities.
- **Winged flight and airfield geometry** (`docs/SIM-JET-FLIGHT.md`): `Appearance = WINGS` flies with a minimum speed, a limited turn rate and circles at `CirclingRadius`; the runway
  of the airfield comes from a fed-in bone table of the airfield models (`ModelGeometry`, part of `GameData.Fingerprint`).

## 32. Addendum Phase 16 (4 October 2026): firing stages, pre-attack, damaged locomotors, supply center and attack groups of the AI, AI overview anew

*Historical (30 Hz, state of this section): the tables, victory ticks and hashes here are superseded; authoritative is `baselines/reference-manifest.json` (since `ea52f87` at 60 Hz), the current values are in §43.9.*

Domain documents: `docs/SIM-FIRING-TRACKER.md` (firing stages `CONTINUOUS_FIRE_*` as a weapon bonus, `PreAttackDelay`, `SpeedDamaged` and siblings, `CaptureFlash`, ability ends attack) and
`docs/SIM-TURRET-AIM.md` §8a (breakdown of the AI shift by effects). In addition: `StructureToppleUpdate` reaches its final state (angle and angular velocity with 32 additional
fractional bits, `docs/MODEL-CONDITIONS-BUILDINGS.md` §6); `SimTool` and `CampaignTool` build the catalog with the model bone table of the airfields like the client (`ModelGeometryLoader`).
No new modules, no version bumps (`WorldSnapshot` 9, replay 10, `CommandTypes.Version` 13, `MissionSnapshot` 2): all new state is optional and is only written if it is set.

### Supply center of the skirmish AI (`AIPlayer::buildBySupplies`, `AIPlayer::findSupplyCenter`)

**Finding.** `SimTool ai-match … --trace N` (new: every N ticks money, buildings, army, gatherers, waves per player, boxes on the map and the distance supply center–nearest filled supply depot) showed why
most AI games ended in a stalemate: `SkirmishAi` placed the supply center, like every building, on rings around the base, 400 to 660 units away from the nearest supply depot. Two gatherers thus brought around 15 boxes
per 100 seconds; after a few minutes both sides stood at 0 credits and conducted two to four attack waves in 45,000 ticks.

**Implementation** (`SkirmishAi.FindSupplySource`, `FindSupplyPlacement`):

* Choice of the supply depot like `findSupplyCenter`: the filled supply depot nearest to the base (minimum value 1,000, halved until above 100) that belongs to no enemy, near which (`SUPPLY_CENTER_CLOSE_DIST` = 20 cells plus
  radii) no supply center of its own stands and which does not lie considerably nearer to the enemy than to its own base (60/40 rule; the enemy base is only what the player has seen of it). Like the
  computer player of the reference, the choice considers **all** supply depots of the map, seen or not – the only place at which `SkirmishAi` knows beyond its own vision.
* Place like `buildBySupplies`: three cells from the supply depot in the direction of the base; if the place is not legal, the first legal one on growing squares around it (two cells wider each, up to twice
  `SUPPLY_CENTER_CLOSE_DIST`), with footprint, distance and reachability check. An unexplored place cannot be built on (`ConstructCommand`): the construction worker drives there first.
* Without any supply depot on the map it stays with the rings around the base. The second supply center (from ten army units on) comes into being at the nearest free supply depot.

**Effect** (Alpine Assault, China versus GLA, `--trace 3000`): distance 128 and 107 respectively instead of 587 and 393 respectively; China wins at tick 7,425 instead of a stalemate. Over the 16 runs: 10 instead of 5 decided.

### The 16 AI runs (§19) anew, 4 October 2026

Same invocations as in §19 (`--max-ticks 45000`, default seeds); every run is identical to run 2 and the snapshot continuation.

| Map | Pairing | first turret version (`8b8c8a7`) | attack state (`2b854d3`) | + firing stages, pre-attack | + damaged locomotors, supply center at the supply depot | **now** (+ attack in groups) | final hash now |
|---|---|---|---|---|---|---|---|
| Alpine Assault | China, GLA | China, tick 43,965 | stalemate at 45,000 | stalemate at 45,000 | China, tick 7,425 | **China, tick 7,545** | `39344016A5EE8F91` |
| Alpine Assault | America, China | stalemate at 45,000 | stalemate at 45,000 | stalemate at 45,000 | America, tick 15,615 | **America, tick 16,800** | `745839146322DDE6` |
| Alpine Assault | GLA, America | America, tick 12,120 | America, tick 12,120 | America, tick 12,120 | America, tick 9,135 | **America, tick 10,740** | `DDDFE22494F29A34` |
| Alpine Assault | ChinaTankGeneral, AmericaAirForceGeneral | stalemate at 45,000 | stalemate at 45,000 | stalemate at 45,000 | AmericaAirForceGeneral, tick 18,120 | **stalemate at 45,000** | `3CD8D8C4D53675C0` |
| Armored Fury | China, GLA | stalemate at 45,000 | stalemate at 45,000 | stalemate at 45,000 | stalemate at 45,000 | **China, tick 11,460** | `6CAD71B81D3CE47A` |
| Armored Fury | America, China | America, tick 41,130 | America, tick 36,375 | stalemate at 45,000 | stalemate at 45,000 | **America, tick 33,255** | `63486D2572E76BCE` |
| Armored Fury | GLA, America | America, tick 22,410 | America, tick 22,410 | America, tick 22,410 | America, tick 27,600 | **America, tick 40,260** | `9801D2537695316C` |
| Armored Fury | ChinaTankGeneral, AmericaAirForceGeneral | AmericaAirForceGeneral, tick 9,405 | AmericaAirForceGeneral, tick 9,405 | AmericaAirForceGeneral, tick 9,405 | stalemate at 45,000 | **stalemate at 45,000** | `E5ED792B12D35CC0` |
| Bitter Winter | China, GLA | stalemate at 45,000 | stalemate at 45,000 | stalemate at 45,000 | China, tick 15,435 | **China, tick 12,930** | `CB890471FCB4B084` |
| Bitter Winter | America, China | stalemate at 45,000 | stalemate at 45,000 | stalemate at 45,000 | stalemate at 45,000 | **stalemate at 45,000** | `0FDEB99B1DEFAAA6` |
| Bitter Winter | GLA, America | America, tick 14,565 | America, tick 14,565 | America, tick 14,565 | America, tick 10,800 | **America, tick 14,430** | `A3A6295689F3F0E5` |
| Bitter Winter | ChinaTankGeneral, AmericaAirForceGeneral | stalemate at 45,000 | stalemate at 45,000 | stalemate at 45,000 | stalemate at 45,000 | **stalemate at 45,000** | `0EC68A99E3ED5ACD` |
| Death Valley | China, GLA | China, tick 8,805 | China, tick 8,805 | China, tick 8,805 | China, tick 7,350 | **China, tick 11,160** | `DA30DBBC2434E499` |
| Death Valley | America, China | stalemate at 45,000 | China, tick 35,985 | stalemate at 45,000 | America, tick 16,260 | **China, tick 9,975** | `8DB4AA743B8F349A` |
| Death Valley | GLA, America | stalemate at 45,000 | stalemate at 45,000 | stalemate at 45,000 | America, tick 10,110 | **America, tick 13,755** | `2992C9E0876B2C95` |
| Death Valley | ChinaTankGeneral, AmericaAirForceGeneral | stalemate at 45,000 | stalemate at 45,000 | stalemate at 45,000 | stalemate at 45,000 | **AmericaAirForceGeneral, tick 32,280** | `C443660F229E157E` |

Decided runs: 7, 7, 5, 10, **12** of 16. The earlier table "Phase 10" (16 of 16 decided) has been superseded since the pathfinding around standing units (Phase 15) and was not surveyed anew until now.
**Attack in groups.** After the supply center six stalemates with a different pattern remained: the coffers were no longer empty, but the units produced during an attack walked individually into the
enemy defence (Armored Fury, China versus GLA: GLA produced 236 units and counted a single wave because the attack never ended). The reference sends teams; `SkirmishAi.CommandArmy` therefore
now treats the wave as the set of units that marched off together: stragglers gather at the rally point and follow as soon as they are at least half the attack threshold; the wave counts as
broken when less than a third of the threshold (at least three) is left of it. Defence (`FindThreat`) still takes all. Effect: 12 instead of 10 decided runs – three won
(Armored Fury China/GLA and America/China, Death Valley Tank/AirForce), one lost (Alpine Assault Tank/AirForce). With 16 runs that is not strong evidence; it was adopted because it is closer to the model.
The four remaining stalemates (three times ChinaTankGeneral versus AmericaAirForceGeneral, Bitter Winter America/China) are the subject of Phase 17. No balance statement. State after the target acquisition of the units (Phase 17): §33.6.

Further reference values (4 October 2026, `SimTool` Release, world fingerprint with bone table `CC9641FC1D68F326…`):

| Run | Result |
|---|---|
| `reference-match` | winner FactionGLA, tick 13,500, final hash `0946FB676DEC615C`, chain `EA6D12F180930D71`; run 2 and snapshot continuation identical |
| `stress` (216,000 ticks) | final hash `56882F763EABEF67`, chain `B8E663493F2C73D5` (unchanged) |
| `ai-match` Alpine Assault, China/GLA | China, tick 7,545, final hash `39344016A5EE8F91`, chain `FFF843E719A2C2DD` |
| `ai-match` Alpine Assault, AmericaAirForceGeneral/China | stalemate, final hash `E222322067493EB4`, chain `F6ED83919F0E7C8F` |
| `ai-match` Alpine Assault, America/GLA | GLA, tick 8,865, final hash `542BE1D4891FEB71`, chain `0B244A5DD2471633` |

The path of the reference match over this day: tick 15,000 (`01C01E3B91EAE18C`) → 14,490 (`1104C4CD97A0D4CA`; firing stages and pre-attack delay) → 13,500 (damaged locomotors). The reference scenario does not use
`SkirmishAi`; the bone table of the tools and the ability rule did not change it.

## 33. Phase 17 (5 October 2026): target acquisition of the units and attack move

Until Phase 16 the simulation chose no victims: three command issuers did that (`MissionSession.DriveCombat`, `SkirmishAi.Fire`, `StandingOrders.FireAtNearestEnemy` of the presentation), each with its own rule
and its own cadence, and a marching unit lost its target until the next think step of its command issuer (`docs/SIM-TURRET-AIM.md` §6, §8a). Now every unit acquires its victim itself, and there is
an attack move as a command. Code: `World/SimulationWorld.Acquisition.cs`, `World/AcquisitionState.cs`, `Scripting/ScriptEngine.Acquisition.cs`; tests: `Tests.Acquisition.cs`.

### 33.1 Data: `AutoAcquireEnemiesWhenIdle` strictly typed

The key was typed as `Bool`; but `INI::parseBitString32` with `TheAutoAcquireEnemiesNames` (`AIUpdate.cpp` `AIUpdateModuleData::buildFieldParse`, `AIUpdate.h`) reads a bit list. Of the values of the
retail content (text search over `Data/INI`: 271 times `Yes`, 62 times `No`, 28 times `Yes Stealthed`, 17 times `No NotWhileAttacking`, 11 times `No ATTACK_BUILDINGS`, 5 times `Yes Stealthed ATTACK_BUILDINGS`, 2 times
`No ATTACK_BUILDINGS Stealthed`, once each `YES ATTACK_BUILDINGS` and `Yes Stealthed ATTACK_BUILDINGS Stealthed`), in 65 lines everything after the first token was lost as "skipped by the reference" –
which was wrong: the reference reads them.

* `NameTablesPhase5.AutoAcquireEnemies` (`YES`, `STEALTHED`, `NO`, `NOTWHILEATTACKING`, `ATTACK_BUILDINGS`; bits `AAS_Idle` 0x01 … `AAS_Idle_Attack_Buildings` 0x10), field type `Flags` in `AIUpdateInterface` and
  all derived AI modules (`ModuleCatalog`, `AiBase`). An unknown token is an error, `NONE` and `+`/`-` behave as in `parseBitString32`.
* `AIUpdateData.AutoAcquire` (`[Flags] AutoAcquire`, same bit values) and `AIUpdateData.MoodCheckTicks` (`MoodAttackCheckRate`, default `LOGICFRAMES_PER_SECOND * 2` = 60 ticks like the constructor of
  `AIUpdateModuleData`; in the content 50 times 250 ms, 4 times 500 ms). `NO` is a bit of its own that nobody checks: "No ATTACK_BUILDINGS" sets two bits and still acquires nothing when idle.
* `baselines/compatibility.json` is **unchanged** (751 entries, `IniTool compat-check` and a freshly generated `compat-baseline` are byte-identical to the file): the lost tokens were diagnostics of the
  graph that the collective counter of the baseline did not carry. The real-data test now checks that no `compatibility-ignored-tokens` diagnostic names `AutoAcquireEnemiesWhenIdle` any more.
* Consequence: definition snapshot and world fingerprint change (the field is a `Flags` value instead of a `Bool`).

### 33.2 Rules

The reference calls `AIUpdateInterface::getNextMoodTarget` from three places: `AIIdleState::update` (resting unit), `TurretAI::friend_checkForIdleMoodTarget` from `TurretAIIdleState::update` (resting turret, while the
body does something else) and `AIAttackMoveToState::update` (attack move). `StepAcquisition` runs once per tick after all movements and before `StepTurrets`, over the objects with an AI module in id order:

| State of the unit (`AcquireMode`) | Condition | Behaviour | Reference |
|---|---|---|---|
| Idle | `AIUpdateInterface.IsIdle`, bit `YES` | searches for a victim **in weapon range**; fires as long as it stays in range; if it leaves the range, it is given up and searched for anew two frames later | `AIIdleState::update` → `aiAttackObject(enemy, NO_MAX_SHOTS_LIMIT, CMD_FROM_AI)` |
| Turret | body busy (drives, builds, docks), bit `YES`, object has an aiming turret (`TurretInfo.Gates`) | like idle, but only with weapons on a turret; the unit keeps driving | `friend_checkForIdleMoodTarget`, `m_targetWasSetByIdleMood` |
| Attack move | `AttackMoveCommand` (no `YES` needed) | searches while marching; stops for the fight, fires, after the death of the victim immediately searches for the next one and without a victim marches on to the destination | `AIAttackMoveToState::update` (`forceRetargetThisFrame`, `friend_endingMove`) |
| none | otherwise (no `YES`, stealthed without `STEALTHED`, disabled) | nothing | `getNextMoodTarget` returns `NULL` |

* **Cadence** (`m_nextMoodCheckTime`): on entry into one of the states the search is set to "now + 2 frames" (`resetNextMoodCheckTime`, `ForceIdleMSEC` = 67 ms in `AIData.ini`), afterwards every
  `MoodAttackCheckRate`; the first interval after an entry is shifted by up to half a rate (`m_randomlyOffsetMoodCheck`). The randomness for that is, as with the scanning of the turrets, a hash of
  (world seed, entity id, tick) and draws nothing from `LogicRandom`.
* **Who does not search:** objects under construction, sold objects, objects shut down by EMP or script (`DISABLED_HELD`) and contained objects; stealthed units without the bit `STEALTHED` ("Sorry, stealthed and not allowed to idle fire
  when stealthed"; as in the reference this does not apply to the attack move); sleeping and passive teams of a computer player ("Human units don't have a mood").
* **Choice of victim** (`AI::findClosestEnemy`): the nearest enemy (centre distance, on a tie the smaller id) that lives, is not a projectile/`INERT`/mine, is not inside a container, is visible to the player of the
  unit (`CanPlayerSee`: fog) and is not stealthed and undetected (`PartitionFilterStealthedAndUndetected`), and against which a weapon is effective that the unit may choose itself
  (`PartitionFilterPossibleToAttack`: `AutoChooseSources` with `CMD_FROM_AI`, `AntiMask`, damage through the armor, pinned slot).
* **Buildings:** `PartitionFilterInsignificantBuildings` (`AttackIgnoreInsignificantBuildings = Yes`): an empty building with a container that is not a faction building (`FS_*`) is taken by nobody.
  `PartitionFilterRejectBuildings`: other enemy buildings are victims for units of a **computer player** (`m_acquireEnemies`) and for units with `ATTACK_BUILDINGS`; all others take only
  `FS_BASE_DEFENSE` and occupied buildings. A resting unit of the human therefore does not shoot at a barracks by itself – as in the original.
* **Attack priority** (missions, `IAcquisitionAdvisor` = `ScriptEngine`): if the unit has a priority set, the highest priority wins after deducting one point per `AttackPriorityDistanceModifier`
  (100) of distance, at least 1; on a tie the higher raw priority, then the nearer one; priority 0 is never attacked. Without a set "the nearest" applies (`getDefaultAttackInfo`).
* **Range of the search** (`AI::getAdjustedVisionRangeForObject`): whoever may not pursue searches only in weapon range (`AI::WITHIN_ATTACK_RANGE`). Whoever may pursue – only ground units of a
  computer player **on an attack move** – searches up to `VisionRange` × `GuardOuterModifierAI` (1.333), for alert teams × 1.1 and for aggressive ones × 1.5, runs after the victim (`AIAttackApproachTargetState`;
  the destination of the pursuit is updated every 15 ticks, the turrets follow as a pursuit) and gives it up when it is further than two search radii away or no longer visible.
* **Shot:** a unit with a victim fires in every tick in which it may – the same path as an accepted `FireWeaponCommand` with `FireOrigin.Ai` (`ChooseWeapon`, `IFireGate`, turret alignment with
  attack state, `PreAttackDelay`, clip, `WeaponFired` event), but without a command. A deploy vehicle thereby deploys by itself as soon as it has a victim.
* **End:** victim dead, invisible, no longer an enemy or out of range (without pursuit); every new AI command (`EndAttack`: movement, idle, docking, building, ability) clears victim **and** attack move.
* **Ordered attacks:** an accepted `FireWeaponCommand` (origin player or AI, not script button) or `PursueCommand` makes its target the victim of the unit (`Ordered`); as long as such commands
  arrive – up to 45 ticks after the time at which the unit could have fired again – the unit does not search and fire itself. That way nothing shoots twice: `StandingOrders.Attack`, the reference scenario
  and `ObjectiveDriver` continue to run their attacks via commands.
* **Aircraft** (`KINDOF_AIRCRAFT`): do not stop on an attack move and do not pursue, they fire in passing; a stored attack move of a parked jet remains one (`JetAIUpdate`
  carries it with the deferred command).

### 33.3 `AttackMoveCommand`

`AttackMoveCommand(Unit, X, Y)` (type 32; `MSG_DO_ATTACKMOVETO` → `aiAttackMoveToPosition`): `AIUpdateInterface.OrderMove(…, attackMove: true)` → `IWorldServices.BeginAttackMove`. The world holds the state
(`AcquisitionState.AttackMove`, destination, `Halted`, `Chasing`); for the fight it takes the movement destination from the unit (`StopInternalMove`) and gives it back afterwards (`StartInternalMove` to the destination), without
this being a new command. Queries for command issuers: `SimulationWorld.IsAttackMoving`, `AttackMoveGoalOf`, `AcquiredVictimOf`. A halted unit on an attack move has no movement destination; command issuers that
read "stands still" as "needs a new command" therefore also ask `IsAttackMoving` (`ScriptEngine.Marching`, `SkirmishAi.CommandArmy`, `StandingOrders`).

**Versions:** `CommandTypes.Version` 13 → **14**, `ReplayFormat.CurrentVersion` 10 → **11** (old replays fail at the command table). `WorldSnapshot.Version` stays **9**: the new state per object
(`acquisition`: victim, ordered, attack move with destination, mode and next search time) is only written if the object itself searches or attacks, and only read if it is there.
`MissionSnapshot` stays **2** (`combatIntervalTicks` keeps name and field; 0 switches off the target acquisition of the mission, `SimulationWorld.SelfAcquisition`).

### 33.4 The command issuers

| Command issuer | before | now |
|---|---|---|
| `MissionSession` | `DriveCombat`: every armed object fired by command every 3 ticks at the nearest enemy in range – also while walking, also with `AutoAcquire = No`, also without an AI module | dropped; the session sets `World.AcquisitionAdvisor = Engine` and counts `WeaponFired` |
| `ScriptEngine` | hunt/attack commands as `SetMoveTargetCommand`, shots through `DriveCombat` | hunt, team and area attack as `AttackMoveCommand` (`AttackToward`: a unit that is fighting or already marching there gets no new command); attack on a named object additionally as a fire command (`FireAtOrdered`); movements of alert/aggressive teams of a computer player become attack moves (`privateMoveToPosition`, `MAA_Action_To_AttackMove`); mood from `teamAggressiveness` (`Team.cpp`, `TeamTemplateInfo`) and `TEAM_SET_ATTITUDE` |
| `SkirmishAi` | `Fire()` every 5 ticks, `MoveGoalFor` (approach to 70 % of the range) | `Fire` and `MoveGoalFor` are dropped; `CommandArmy` sends the army to the group destination by `AttackMoveCommand` |
| `StandingOrders` (presentation) | attack movement and guarding fired by command at the nearest enemy | attack movement = `AttackMoveCommand`; no fire command any more except when attacking a named target |
| `ObjectiveDriver` (commands-only mode) | move command to the victim, shots through `DriveCombat` | attack move plus attack command on the victim (`Assault`, `DriveAssaults`) |

### 33.5 Approximations and what is missing

* **No pursuit from idle.** In the reference a resting unit of a computer player runs after a victim in its search radius (`aiAttackObject`); here it stays where it is and takes only what is in
  range. Pursuit happens only on an attack move. Reason: the command issuers position their units (rally point, script posts); a unit that leaves its post collides with their bookkeeping.
* **`AIData.ini` is not typed:** `GuardOuterModifierAI` 1.333, `AlertRangeModifier` 1.1, `AggressiveRangeModifier` 1.5, `AttackPriorityDistanceModifier` 100, `ForceIdleMSEC` 67 and
  `AttackIgnoreInsignificantBuildings = Yes` are in the code as named constants with the values of the shipped file. `GuardOuterModifierHuman` (2.2) has no effect because units of the human search
  only in weapon range anyway.
* **Passive teams** acquire nothing (the reference lets them answer their last attacker); **sleeping** teams keep moving when a script orders it (`MAA_Action_To_Idle` is missing).
  `NAMED_SET_ATTITUDE` (single object) remains recorded without effect; `m_attackCommonTarget` (team attacks a common target) is missing.
* **`NOTWHILEATTACKING`** is read but has no effect of its own: a unit with a victim never searches for a second one here.
* **Line of sight:** `AttackUsesLineOfSight`/`KINDOF_ATTACK_NEEDS_LINE_OF_SIGHT` (`AI::CAN_SEE`) is not checked; vision is that of the player (fog) – the reference requires the fog test only for units of the
  human, here it applies to all.
* **Distances** are centre distances (the reference measures `FROM_BOUNDINGSPHERE_2D`); the empty-building filter asks "has a container and is empty" instead of `isGarrisonable`.
* **There is no firing from containers** (garrison, `isPassengerAllowedToFire`): contained objects acquire nothing. `DriveCombat` let them fire; that was not verified behaviour.
* **Objects without an AI module acquire nothing** (in the reference every attacking object has one). `DriveCombat` let them shoot too.
* **Deferred movements:** only `JetAIUpdate` carries the attack move through a deferred command; a `DeployStyleAIUpdate` vehicle that first has to pack up, and a hacker that first packs up, execute it as
  a simple movement. `AIAttackMoveToState` repeats a failed pathfinding (`ATTACK_RETRY_COUNT`); here the attack move ends when the movement ends.
* **Unit ability:** `OBJECT_STATUS_IS_USING_ABILITY` is not set; a unit that executes an ability and counts as idle while doing so could acquire a victim.
* **Presentation and operation** (attack movement in the client, resting units of the human shooting by themselves) **no human has seen**.

### 33.6 Measured values (Release, retail data, 5 October 2026)

"Before" is the state `4eea97b`, re-measured with the same tools (it agrees with §32); "after" is this state.

| Run | before (`4eea97b`) | after |
|---|---|---|
| `SimTool reference-match` | winner FactionGLA, tick 13,500, final hash `0946FB676DEC615C`, chain `EA6D12F180930D71` | winner FactionGLA, tick **14,490**, final hash `23B4B25DF62765D8`, chain `DE5849AF9F8493C4`; run 2 and snapshot continuation (tick 7,395) identical |
| `SimTool stress` (216,000 ticks) | final hash `56882F763EABEF67`, chain `B8E663493F2C73D5` | **unchanged**: final hash and chain the same, so in the synthetic fixture no object gets an attack state (it would be in the hash of the object) |
| 16 AI runs (`ai-match … --max-ticks 45000`) | 12 of 16 decided | **11 of 16** decided; all 16 tick-exactly identical in run 2 and after snapshot continuation (table below) |
| `CampaignTool audit --ticks 900 --campaign CHALLENGE_0` | 7/7 deterministic, 7/7 identical after save/load, elimination ends victory 7/7, defeat 6/7 | **unchanged** 7/7, 7/7, 7/7, 6/7 (4,984 instead of 4,980 scripts executed, 0 hits on missing types) |
| `CampaignTool drive --campaign CHALLENGE_0` (60,000 ticks) | victory 7/7, defeat 7/7 | **unchanged**, every line (script, tick, interventions) the same |
| `CampaignTool drive --campaign GLA` | victory 5/5, defeat 5/5 | **unchanged** 5/5 and 5/5, same scripts and ticks; `MD_GLA03` needs 258 instead of 227 world interventions |
| `scripts/audit-scenarios.ps1` | three scenes green | **three scenes green** |
| Tests | – | `Simulation.Tests` Release with `--long --content-dir`: 348 passed (8 new: 7 synthetic, 1 real data); `Presentation.Tests` passed; `Game.Tests` (Release, `--content-dir`) passed |

The reference match shifts (its scenario still orders every shot; new is that units shoot by themselves in the gaps between its commands and that its commands lock the unit's own target
choice). The real-data test `RealAcquisition` lets a Crusader and a Battlemaster fight without any command: first shot in tick 3, 13 shots, one destroyed, two runs hash-identical.

**The 16 AI runs** (default seeds; "before" coincides with the column "now" from §32):

| Map | Pairing | before | final hash before | **after** | final hash after | chain after |
|---|---|---|---|---|---|---|
| Alpine Assault | China, GLA | China, tick 7,545 | `39344016A5EE8F91` | **China, tick 23,310** | `54DD2C9094141CC8` | `582FA4C6DF853C4B` |
| Alpine Assault | America, China | America, tick 16,800 | `745839146322DDE6` | **America, tick 16,965** | `BA10AC1EB0EB8AC9` | `F669459C2FA6E39C` |
| Alpine Assault | GLA, America | America, tick 10,740 | `DDDFE22494F29A34` | **America, tick 8,895** | `23A93C58C775A691` | `B29CF881F6B1CC59` |
| Alpine Assault | ChinaTankGeneral, AmericaAirForceGeneral | stalemate | `3CD8D8C4D53675C0` | **stalemate** | `2EDDC1B7BF19F28B` | `0C7DD733866C09BA` |
| Armored Fury | China, GLA | China, tick 11,460 | `6CAD71B81D3CE47A` | **stalemate** | `89B05A25738A9E6D` | `35DFAE191CAD2649` |
| Armored Fury | America, China | America, tick 33,255 | `63486D2572E76BCE` | **America, tick 19,095** | `4A99580AC54F4EBD` | `AD65CED96F474586` |
| Armored Fury | GLA, America | America, tick 40,260 | `9801D2537695316C` | **America, tick 37,365** | `3C7BC00FB4E3E8C1` | `0C664B1DEFEFDF44` |
| Armored Fury | ChinaTankGeneral, AmericaAirForceGeneral | stalemate | `E5ED792B12D35CC0` | **stalemate** | `C8ACAB511BFC8C86` | `15D05EFD38B29EEB` |
| Bitter Winter | China, GLA | China, tick 12,930 | `CB890471FCB4B084` | **China, tick 17,220** | `26CD35AC8AA660F5` | `2B86E2038EDFAAB7` |
| Bitter Winter | America, China | stalemate | `0FDEB99B1DEFAAA6` | **stalemate** | `3C403645D06C2E92` | `6F84497D225021F4` |
| Bitter Winter | GLA, America | America, tick 14,430 | `A3A6295689F3F0E5` | **America, tick 8,895** | `AB725263AEF8E3B7` | `CDFCDF66730FF07B` |
| Bitter Winter | ChinaTankGeneral, AmericaAirForceGeneral | stalemate | `0EC68A99E3ED5ACD` | **stalemate** | `A4B3ACB444C989B4` | `BA6DCDD5A38758EF` |
| Death Valley | China, GLA | China, tick 11,160 | `DA30DBBC2434E499` | **China, tick 8,565** | `9F0F8D7D28E8654D` | `90CDEBE18E7C6B01` |
| Death Valley | America, China | China, tick 9,975 | `8DB4AA743B8F349A` | **America, tick 9,330** | `E98A532C90F89642` | `AC321A0CFA9C00E1` |
| Death Valley | GLA, America | America, tick 13,755 | `2992C9E0876B2C95` | **America, tick 8,175** | `3F33A4FEDEBA85C1` | `3B00774A84E69278` |
| Death Valley | ChinaTankGeneral, AmericaAirForceGeneral | AmericaAirForceGeneral, tick 32,280 | `C443660F229E157E` | **ChinaTankGeneral, tick 32,475** | `828EE15100622D42` | `0C3AF1B086B9428E` |

Decided: before 12, after **11**. Two outcomes flip: Armored Fury China/GLA (China at 11,460 → stalemate) and Death Valley Tank/AirForce (AirForce at 32,280 → ChinaTankGeneral at 32,475); Death Valley
America/China changes the winner (China → America). Of the eleven decided runs seven end earlier than before and four later (clearly: Alpine Assault China/GLA 7,545 → 23,310 and Bitter Winter China/GLA 12,930 →
17,220; narrowly: Alpine Assault America/China and Death Valley Tank/AirForce). With 16 runs that is no statement about strength; **no balance statement**.

**What the traces of the five stalemates show** (`SimTool ai-match … --trace N --trace-army`; `--trace-army` is new and prints per player the army by type with "marching / attack move / fighting", the
distance to the nearest enemy building and the self-report of the AI):

| Run | Picture at tick 45,000 |
|---|---|
| Alpine Assault, Tank/AirForce | China: 7 buildings, army 0 since tick 27,000, 150 credits, distance center–supply depot 956 (the near supply depot is empty), production stands at 74. AirForce: 13 buildings, 0 credits, 4 gatherers, produces 200 units – exclusively Rangers and Missile Defenders – which die in groups of 4 to 12 at China's one defence; the first wave never ends (waves 1) |
| Armored Fury, China/GLA | China: 9 buildings, 0 credits, army 0–2, distance 654. GLA: 9 buildings, 30 credits, 295 units produced, almost only Rebels, which march up spread over 180 to 1,150 units and die at one defence |
| Armored Fury, Tank/AirForce | both 12–13 buildings, 125–150 credits, distances 654 and 521, armies 4 and 6 below the attack threshold (16 and 22) |
| Bitter Winter, America/China | both frozen since tick 30,000: 25 and 250 credits, distances 1,173 and 1,127, armies 2 and 0, the supplies of the map stay at 1,068 boxes |
| Bitter Winter, Tank/AirForce | AirForce has 3 buildings, no army, no construction worker, no barracks; China 11 buildings, but 125 credits, distance 1,173 and 3 units below the threshold 12 – it no longer attacks |

Common to all: at the end both sides have no money (the supply depot next to the supply center is empty, the nearest filled one 500 to 1,200 units away, the money does not suffice for a further
center), and the armies consist of the respective cheapest infantry, which does not overcome a single base defence. The target acquisition shows no error in the traces (units on an attack move
fight and march on; "fighting" and "marching" at the same time are pursuits). That is the finding from §32 and `SIM-TURRET-AIM.md` §8a, now with numbers per run.

**A correction attempt, measured and discarded:** "an army that has not grown for 90 seconds attacks even below the threshold" (obvious for Bitter Winter Tank/AirForce). All 16 runs anew:
the same 11 decided, the eleven with an identical final hash, the five stalemates remained stalemates (only their hashes changed). The change is therefore not in the code. What the traces name as the cause –
economy after the first supply depot has run empty and the composition of the army – is a brief of its own for `SkirmishAi` (hold back money for a second center, do not build only the cheapest).

## 34. Phase 17 (5 October 2026): `SkirmishAi` as the agent of a mission, performance measured on a quiet machine

### 34.1 What has changed in `SkirmishAi`

The commands-only mode of the objective driver (`docs/SCRIPT-ENGINE.md` §9) lets `SkirmishAi` play for the viewer of a mission. Two bugs were noticed only there and are fixed in the AI itself; three
switches are set only by the mission agent (a skirmish match sets none of them).

| Change | Where | Rationale |
| --- | --- | --- |
| Supply depots of a **non-hostile side** count as supply depots (`Observe`) | `SkirmishAi.Observe` | mission maps give the `SupplyDock`s to a civilian player instead of the system player; `FindSupplySource` already accepted them, the memory `_warehouses` did not – "warehouses known 0", no gatherer was sent |
| Supply center at the base after three futile placement searches next to the source (`SupplyPlacementPatience`) | `BuildStructures` | `AIPlayer::buildBySupplies` does not know the case "no legal place"; here the center otherwise stayed unbuilt forever (`GC_ChemGeneral`: supply depot at the map edge) |
| `Objectives` (attack targets before its own choice) | `AttackTarget` | the victims of the mission objectives, as briefing and target markers name them to a player |
| `CommitEverything` (attack without threshold, no retreat to gather) | `CommandArmy` | a side without production buildings otherwise waited forever below the attack threshold |
| `ObjectiveReserve` (money the AI does not touch) | `Think` | the purchase that a mission objective needs (capture upgrade) |
| Option `EconomyFirst` (build nothing else until the first supply center stands) | `BuildStructures` | without income the starting money of the Challenge maps went into power plants and barracks |

The 16 AI-versus-AI runs are untouched by this: the final hashes of the merged state are those from §33.6 (measured there without these changes).

### 34.2 Performance (quiet machine, no parallel runs, Release, 5 October 2026)

| Run | before (`4eea97b`) | after (`2a66661`) |
| --- | --- | --- |
| `SimTool stress`, run 1 (216,000 ticks), measured twice | 13.1 s / 13.3 s (60.5 / 61.6 µs per tick) | 13.3 s / 13.3 s (61.5 / 61.6 µs per tick) |
| Final hash / chain | `56882F763EABEF67` / `B8E663493F2C73D5` | unchanged |
| `SimTool mission-run` `MD_USA05`, 3,000 ticks (total run time of the tool) | 73.4 s | 66.7 s |
| `SimTool mission-run` `MD_GLA05`, 3,000 ticks | 135.6 s | 147.9 s |

The stress test shows no loss (the 150 µs per tick from Phase 14 were load, not code; under load the same state measured 166 µs today). The mission runs change their hashes because the target acquisition now lies in the
simulation; `MD_GLA05` is 9 % slower, `MD_USA05` 9 % faster. **Not done:** a profile of the hottest modules. The mission runs are the place for it (the number above is the total run time of the tool
with loading and repetition runs, not a value per tick).

## 35. Phase 17 (5 October 2026): difficulty of a computer player in skirmish – finding, no simulation change

The brief was to give the simulation a difficulty axis for computer players that is evidenced in the data and to tie the three armies of the skirmish setup
("Easy/Medium/Hard Army") to it. The examination of the reference yields: **none of the effects evidenced in the data applies to a computer player in skirmish.**
Therefore nothing was changed in the simulation (no field, no snapshot entry, no version); instead the setup now offers only one army
(`docs/GAME-SHELL.md` §6). Nothing was invented.

### 35.1 What the data contain

| File | Content | measured |
| --- | --- | --- |
| `Data/INI/GameData.ini` | `WeaponBonus = SOLO_AI_EASY RATE_OF_FIRE 80%`, `SOLO_AI_NORMAL … 100%`, `SOLO_AI_HARD … 120%` | three lines; for `SOLO_HUMAN_*` there is no `WeaponBonus` line |
| `Data/INI/GameData.ini` | `HumanSoloPlayerHealthBonus_Easy = 150%`, `_Normal = 100%`, `_Hard = 80%` | `AISoloPlayerHealthBonus_*` does not occur (default 1.0, `GlobalData::GlobalData`) |
| `Data/INI/Default/AIData.ini` | `SideInfo` with `ResourceGatherersEasy/Normal/Hard` | 12 sides; per side all three values are the same (2, for the GLA sides 5) |

The `WeaponBonus` lines are already strictly typed in `CnCNew.Data` (`GameDataSchema`, `FieldType.WeaponBonusEntry`); the condition names `SOLO_*` are in the
name table. Nothing changes in `baselines/compatibility.json`.

### 35.2 Where the reference applies them

| Effect | Location (file, function) | applies in skirmish? |
| --- | --- | --- |
| Weapon bonus `SOLO_AI_*` / `SOLO_HUMAN_*` on every object of the player | `Common/RTS/Player.cpp`, `Player::friend_applyDifficultyBonusesForObject` (called from `Object::setReceivingDifficultyBonus`, `Object.cpp`; `Object::initObject` sets it as long as `ScriptEngine::getObjectsShouldReceiveDifficultyBonus` holds) | **no** – the entire body is under `if (TheGameLogic->isInSinglePlayerGame())` |
| Hit point factor `m_soloPlayerHealthBonusForDifficulty[Spielertyp][Schwierigkeit]` | the same function | **no**, the same condition; for computer players 1.0 anyway |
| Game mode | `GameLogic/System/GameLogic.cpp`, `GameLogic::isInSinglePlayerGame`: only `GAME_SINGLE_PLAYER` (or its replay). The skirmish menu starts `GAME_SKIRMISH` (`SkirmishGameOptionsMenu.cpp`, `reallyDoStart`: `MSG_NEW_GAME` with `GAME_SKIRMISH`) | – |
| Slot → difficulty | `GameLogic::startNewGame`: `SLOT_EASY_AI`/`SLOT_MED_AI`/`SLOT_BRUTAL_AI` → `skirmishDifficulty` = `DIFFICULTY_EASY`/`NORMAL`/`HARD`; `Player::initFromDict` passes it to `AIPlayer::setAIDifficulty`; `Player::getPlayerDifficulty` delivers it | yes – the axis itself exists per computer player |
| Number of gatherers | `GameLogic/AI/AIPlayer.cpp`, `AIPlayer::checkForSupplyCenter` (`AISideInfo::m_easy/m_normal/m_hard`) | yes, but without effect: the shipped values are the same per side |
| Scripts with easy/normal/hard flags | `ScriptEngine::executeScript` (`Script::isEasy/isNormal/isHard` against `Player::getPlayerDifficulty`), `TeamPrototype::evaluateProductionCondition` (`Common/RTS/Team.cpp`) | yes – that is the actual difference of the three armies (`SkirmishScripts.scb`) |
| Common team target | `Team::setTeamTargetObject` (`Team.cpp`): a computer player of the level easy sets no common attack target | yes, but only for script teams |
| Choice of victim when attacking a squad | `AIAttackSquadState::chooseVictim` (`GameLogic/AI/AIStates.cpp`): easy random, normal the nearest, hard all the same one | yes, but only via `aiAttackTeam` (script action or `AIGroup`) |

There is no further place that reads `getPlayerDifficulty`, `getAIDifficulty` or `m_difficulty` (searched in `GameEngine/Source/Common` and `GameEngine/Source/GameLogic`).

### 35.3 Conclusion

- The two numeric effects of the data (rate of fire 80 %/120 %, hit points) are **campaign rules** ("solo"), not skirmish rules. Applying them to skirmish opponents would be invented tuning.
- The difficulty of the original that is effective in skirmish lies in the AI scripts and in two team behaviours that depend on script teams. This project does not execute `SkirmishScripts.scb`
  (`SkirmishAi` replaces them, §19/§32) and `SkirmishAi` works without script teams; a replica would be imitation, not evidence. **That remains the difference from the original.**
- The one computer player of this simulation thus corresponds to `DIFFICULTY_NORMAL`: no bonus, no malus. The setup therefore offers `GUI:Open`, `GUI:Closed` and `GUI:MediumAI`
  ("Medium Army") – one entry of the original in its meaning, instead of three that start the same thing. `GameShell.SkirmishDifficultyIgnored` has been dropped.
- **Side finding, not worked on:** the same function applies in campaign missions, and there this simulation does **not** apply it – `MissionOptions.Difficulty` controls only the
  script flags (`ScriptEngine`), neither the weapon bonus `SOLO_AI_EASY/HARD` nor `HumanSoloPlayerHealthBonus_Easy/Hard` take effect; the script action that switches the bonus off and on
  per map (`OBJECT_ALLOW_BONUSES`, `ScriptActions::doEnableOrDisableObjectDifficultyBonuses`) is also only recorded and has no consumer. On "normal" this is without effect (100 %), on "easy"/"hard"
  a mission deviates from the original. That lay outside this brief (skirmish) and is a decision before the freezing of the simulation.

### 35.4 Measurement (Release, 5 October 2026)

No source of `CnCNew.Simulation`, `CnCNew.Data` or `tools/CnCNew.SimTool` was changed; the runs confirm the state and are "before" and "after" at the same time.
There is no effect table easy/hard because there is no effect; `SimTool ai-match` has not received an option `--difficulty`.

| Run | Result | Final hash | Chain | Run 2 / continuation from snapshot |
| --- | --- | --- | --- | --- |
| `SimTool reference-match` | FactionGLA at tick 14,490 | `23B4B25DF62765D8` | `DE5849AF9F8493C4` | identical / identical |
| `SimTool stress` (216,000 ticks) | – | `56882F763EABEF67` | `B8E663493F2C73D5` | identical |
| `ai-match` Alpine Assault, `FactionChina,FactionGLA`, 45,000 ticks | FactionChina at 23,310 | `54DD2C9094141CC8` | `582FA4C6DF853C4B` | identical / identical |
| `ai-match` Alpine Assault, `FactionAmerica,FactionChina` | FactionAmerica at 16,965 | `BA10AC1EB0EB8AC9` | `F669459C2FA6E39C` | identical / identical |
| `ai-match` Alpine Assault, `FactionGLA,FactionAmerica` | FactionAmerica at 8,895 | `23A93C58C775A691` | `B29CF881F6B1CC59` | identical / identical |

## 36. Completion of Phase 17 (6 October 2026): second supply center of the AI; army selection by value measured and discarded

*Historical (30 Hz, state of this section): the tables, victory ticks and hashes here are superseded; authoritative is `baselines/reference-manifest.json` (since `ea52f87` at 60 Hz), the current values are in §43.9.*

### 36.1 Finding and rule

The five stalemates from §33.6 all end the same way: the supply depot next to the supply center is empty, the nearest filled one lies 500 to 1,200 units away, and `SkirmishAi` planned a further center only with
an army of ten units (`PlanGoals`) – which without income never existed again. In the reference the skirmish scripts decide when a further center is built (`AI_PLAYER_BUILD_SUPPLY_CENTER` →
`AIPlayer::buildBySupplies`, condition `SUPPLY_SOURCE_SAFE` → `AIPlayer::isSupplySourceSafe`); the project does not execute these scripts. The rule here therefore takes the building blocks of the reference and a trigger
condition of its own:

| Building block | Reference (`GameLogic/AI/AIPlayer.cpp`) | here (`Skirmish/SkirmishAi.cs`) |
| --- | --- | --- |
| What a center can still gather | `AIPlayer::queueSupplyTruck`: "Make sure we have supplies near it" – supply depots within the radius `SUPPLY_CENTER_CLOSE_DIST` (20 cells) plus radius, not hostile, with boxes; a center without such a source gets no more gatherers | `SupplyStockInReach`: sum of the box values of these supply depots over all own centers |
| Where the next center goes | `AIPlayer::findSupplyCenter` (already since Phase 16: `FindSupplySource`) | unchanged |
| Whether the source is safe | `AIPlayer::isSupplySourceSafe` → `isLocationSafe`: no enemy within the radius `SupplyCenterSafeRadius` (AIData.ini: 300) plus radius; gatherers, dozers and insignificant buildings do not count | `SupplySourceSafe`: visible enemy units and armed buildings as well as remembered armed buildings (only what the player sees or has seen; the reference reads the partition) |
| **Trigger** | script (not executed) | **own:** as soon as the stock in reach falls below 3,000 credits (`LowSupplyStock`), at most four centers; the goal then stands before all other construction goals, and as long as the money does not suffice, army production holds back the price (the existing reserve of `BuildStructures`) |
| Idle gatherers | – | `OrderCarriers` sends them to the nearest supply depot that still contains something (before: to the nearest, even if it was empty) |

The threshold 3,000 is a choice of our own (the center must still be affordable from the remaining income), not a value of the data. The rule has no unit test: the synthetic fixture knows no
supply center with a dock module; it is evidenced by the 16 runs below.

### 36.2 The 16 AI runs (Release, `ai-match … --max-ticks 45000`, default seeds; before = state `1a1732c`, after = `6eb7ae4`)

| Map | Pairing | before | **after** | final hash after | chain after |
|---|---|---|---|---|---|
| Alpine Assault | China, GLA | China, tick 23,310 | China, tick 23,370 | `0BD4FE5EEB2C37E7` | `BF9939E5A3B45736` |
| Alpine Assault | America, China | America, tick 16,965 | unchanged | `BA10AC1EB0EB8AC9` | `F669459C2FA6E39C` |
| Alpine Assault | GLA, America | America, tick 8,895 | unchanged | `23A93C58C775A691` | `B29CF881F6B1CC59` |
| Alpine Assault | ChinaTankGeneral, AmericaAirForceGeneral | stalemate | stalemate | `D99B60905846A6B1` | `80EDA8449C86CEE6` |
| Armored Fury | China, GLA | stalemate | stalemate | `7196F712342A3BDA` | `FF370FDA722BFA3B` |
| Armored Fury | America, China | America, tick 19,095 | America, tick 19,095 (different hash) | `0E0E07558397DB62` | `E41C4EF3D1DFBEF4` |
| Armored Fury | GLA, America | America, tick 37,365 | America, tick 29,865 | `58B066C7284C21B2` | `64E8453DEE5297E4` |
| Armored Fury | ChinaTankGeneral, AmericaAirForceGeneral | stalemate | stalemate | `7AC8B5574DE21417` | `68F33BE69B8EDF6D` |
| Bitter Winter | China, GLA | China, tick 17,220 | unchanged | `26CD35AC8AA660F5` | `2B86E2038EDFAAB7` |
| Bitter Winter | America, China | stalemate | **America, tick 33,750** | `38380B920A732955` | `5FED11D99834B1B6` |
| Bitter Winter | GLA, America | America, tick 8,895 | unchanged | `AB725263AEF8E3B7` | `CDFCDF66730FF07B` |
| Bitter Winter | ChinaTankGeneral, AmericaAirForceGeneral | stalemate | stalemate | `9D040D6ACB8E269D` | `24F626B9E8F8CCA2` |
| Death Valley | China, GLA | China, tick 8,565 | unchanged | `9F0F8D7D28E8654D` | `90CDEBE18E7C6B01` |
| Death Valley | America, China | America, tick 9,330 | unchanged | `E98A532C90F89642` | `AC321A0CFA9C00E1` |
| Death Valley | GLA, America | America, tick 8,175 | unchanged | `3F33A4FEDEBA85C1` | `3B00774A84E69278` |
| Death Valley | ChinaTankGeneral, AmericaAirForceGeneral | ChinaTankGeneral, tick 32,475 | ChinaTankGeneral, tick 31,770 | `FDAD90E9E139AFE9` | `6E2EE5C9DA09EFD1` |

Decided: before 11, after **12**; no winner changes; seven runs are hash-identical (the rule never takes effect there), all 16 are identical in run 2 and after the snapshot continuation. The four remaining stalemates
(three times ChinaTankGeneral versus AmericaAirForceGeneral, Armored Fury China/GLA) still end with armies of the cheapest infantry in front of a base defence. **No balance statement.** Unchanged, because
the reference scenario and the stress test do not use `SkirmishAi`: reference match winner FactionGLA at tick 14,490, `23B4B25DF62765D8`, chain `DE5849AF9F8493C4`; stress test `56882F763EABEF67` / `B8E663493F2C73D5`.

### 36.3 Army selection by value and range: two measurement rounds, reverted

The attempt was to no longer let `ProduceArmy` choose randomly from what is currently affordable, but per producer the most valuable unit (price, doubled if the weapon reaches further than any seen
enemy base defence), factories before barracks, saving instead of small purchases. Abort criterion of the session: at most two measurement rounds.

| State | 16 AI runs decided | What the trace shows |
| --- | --- | --- |
| supply rule only (§36.2) | **12** | – |
| Round 1: value = price × range bonus | 10 | price is not a combat value: America builds 18 `AmericaTankAvenger` (anti-air, 2,000 credits), which stand in front of the base and cannot shoot at it; China builds `ChinaVehicleTroopCrawler` |
| Round 2: only units whose weapon hits ground targets (`AntiGround`), no transports | 11 | one stalemate ends (Armored Fury Tank/AirForce: ChinaTankGeneral at 18,765), two victories become stalemates (Armored Fury and Bitter Winter America/China) |

The commands-only agent of the missions (`docs/SCRIPT-ENGINE.md` §9) with the version from round 2: at 15,000 ticks unchanged 1 of 24, at 60,000 ticks **2 instead of 3** maps (`MD_CHI02` is lost, `MD_CHI01`
ends at tick 20,733 instead of 18,492). So the change holds up neither in skirmish nor in the missions and is **not in the code**. What the measurement teaches: the price is no good as a measure, and saving keeps the army
below the attack threshold longer. A viable approach needs a combat value per target (damage against armor, as `WeaponSet`/`ArmorSet` provide it) and teams instead of single purchases
(`AIPlayer::selectTeamToBuild`); that is a separate, measured piece of work behind the programme from `docs/MULTIPLAYER-PLAN.md`.

## 37. Phase 18 (6 October 2026): reference manifest, input manifest and check mode of the incremental hash

No change to the simulation. New are two files under `baselines/` and the tool commands that write and check them (`tools/CnCNew.SimTool`: `ReferenceManifest.cs`, `InputManifest.cs`,
`IncrementalHashCheck.cs`, `ManifestDocument.cs`, `CanonicalObjectHasher.cs`). From here on **the manifest is authoritative**; hash values in this and other documentation are transcripts
(`docs/MULTIPLAYER-PLAN.md` §6 rule 2). Every change to the two files is a subject of review like `baselines/compatibility.json`.

```powershell
dotnet build -c Release tools/CnCNew.SimTool
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- reference-manifest check game/content baselines/reference-manifest.json --set quick
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- reference-manifest check game/content baselines/reference-manifest.json [--set full] [--only <id>[,<id>]] [--jobs N]
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- reference-manifest check game/content baselines/reference-manifest.json --set quick --verify-incremental
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- reference-manifest write game/content baselines/reference-manifest.json [--set full]
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- reference-manifest list game/content
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- reference-manifest show game/content --only <id> [--explain-incremental]
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- input-manifest check game/content baselines/input-manifest.json
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- input-manifest write game/content baselines/input-manifest.json
```

### 37.1 File form

Both files are a strict subset of JSON (`ManifestDocument`): only texts, integers, booleans and objects; no arrays, no fractions, no `null`. The notation is unambiguous: UTF-8 without
BOM, LF, two spaces per level, keys on every level sorted ordinally (runs are keys in `runs`, hence sorted by id), numbers in invariant notation, an escaping of its own (only `"`,
`\` and control characters below U+0020; not that of the runtime, cf. finding D9). The files contain no absolute paths, timestamps, machine names or run times; maps are given as logical content paths.
The reader rejects everything outside the model (also duplicate keys and a BOM). Times appear only on the console (progress on `stderr`, the result per run in id order on `stdout`).

### 37.2 Runs of the reference manifest

50 runs, of which 9 in the tier `quick`. `full` means "all"; every line of the file carries its tier (`set`). `--only` selects runs by id, independently of the tier; unknown ids are an error
(exit code 2). Seeds are the default values of the respective `…Options.Standard`, tick rate 30.

| Run (id) | Kind | Tier | Parameters |
|---|---|---|---|
| `reference-match` | retail reference match (`ReferenceMatchScenario`) | quick | 13 factions, at most 216,000 ticks |
| `stress` | synthetic stress test (`StressScenario`) | quick | 216,000 ticks, target population 10,500 |
| `<Karte>_<A>-<B>` (16) | AI versus AI (`SkirmishMatchScenario`, like `ai-match`) | 4 quick, 12 full | Alpine Assault, Armored Fury, Bitter Winter, Death Valley × China/GLA, America/China, GLA/America, ChinaTankGeneral/AmericaAirForceGeneral; `--max-ticks 45000` |
| `ai8_TwilightFlame`, `ai8_FortressAvalanche`, `ai8_IronDragon` | eight AIs | full | the first eight playable factions in catalog order (what `ai-match --players 8` takes, written out in the manifest), 9,000 ticks |
| `ai8_TwilightFlame_short` | eight AIs, shortened | quick | 3,000 ticks |
| `skirmish-sweep` | all maps with at least two start positions, two AIs, 3,000 ticks | full | 150 maps, 64 played |
| `mission_<Karte>[_<Fraktion>]` (27) | mission over 3,000 ticks (`MissionScenario`, like `mission-run`) | 2 quick, 25 full | per campaign the first and the last mission |

- **Quick selection of the AI runs:** `AlpineAssault_GLA-America`, `BitterWinter_GLA-America`, `DeathValley_America-China`, `DeathValley_China-GLA` – the shortest decided ones, three maps, every base faction
  at least twice. Quick missions: `mission_MD_USA05`, `mission_MD_GLA05`.
- **Eight players:** Twilight Flame (486 × 469 cells, the largest map with eight start positions), Fortress Avalanche (418 × 413) and Iron Dragon (531 × 282, different aspect ratio). Death Valley also has
  eight start positions, but is already among the two-player maps. 9,000 ticks (five game minutes) are a limit of the run time, not an end of the game: all three end without a winner. Such a run costs
  220 to 305 s here (recording and three replays), at 45,000 ticks it would be well over 20 minutes per run.
- **Missions:** per campaign the first and the last mission of its order, with only one mission that one. Campaigns that play the same map with the same human faction share the run
  (`MD_USA_1_DEMO` and `USA` → `mission_MD_USA01`; the parameter `campaigns` names all). Challenge campaigns run with their persona (`HumanFaction`, `ChallengeSides.HumanSideFor`), hence nine different
  runs on `GC_ChinaBoss`. That yields 27 runs from 17 campaigns. The map of `MD_CAMPEA_DEMO` is missing in the content; that is in the manifest as `unavailable` and is checked along.
- **Sweep:** one entry with counters (64 started and ended, 0 with a winner, 0 failed, 75 maps without and 11 with only one start position), end tick/final hash/chain per map and a SHA-256 over
  these lines (`combinedSha256`). The sweep plays live and is not replayed.

The values named in `docs/handoffs/HANDOFF-PHASE18.md` and §36.2 (reference match, stress test, 16 AI runs, `MD_USA05`, `MD_GLA05`) are in the manifest unchanged.

### 37.3 What is recorded per run

`kind`, `set`, `parameters` (map, factions, seeds, tick limits, tick rate) and `results`. For recorded runs (reference match, stress test, AI): `endTick`, `outcome` (`winner`/`stalemate`/`none`),
`winner`, `victoryTick`, `finalHash`, `chainHash` (chain over all tick hashes of run 1), number of commands and SHA-256 of the command stream (the replay file without checkpoints – distinguishes "the AI decided
differently" from "the world computed differently"), `identicalInRun2` (second fresh replay, tick by tick), `snapshotTick`/`snapshotBytes`/`snapshotSha256` and `identicalAfterSnapshotContinuation`
(snapshot at half the length, restore, rest; compared with run 1). For missions additionally script and object counters, `saveTick`, `hashAtSave` (before saving), `hashAfterRestore` (from the
restored session) and two booleans:

- `identicalAfterSaveRestore`: saved and restored session continue running the same – that is what `mission-run` and the campaign audit check;
- `identicalToUninterruptedRunAfterSaveRestore`: both additionally equal run 1, which was never saved, and the hash at the save point is the same before and after loading.

`write` writes what was measured – also a false boolean, with a warning. `check` recomputes everything and reports per run `OK` or the **first deviating field** with expected and actual
value, plus deviating header values (`simulation`: tick rate, `CommandTypes.Version`, replay, snapshot versions) and runs that only the file or only the tool knows; exit code 1 on every deviation.

### 37.4 Finding D8 confirmed: five missions carry a stale incremental hash

In five of the 27 mission runs `identicalToUninterruptedRunAfterSaveRestore` is **false** (recorded as such in the manifest): `mission_MD_USA01`, `mission_MD_USA02`, `mission_MD_GLA03`, `mission_MD_GLA05`,
`mission_GC_LaserGeneral_ChinaNukeGeneral`. `identicalAfterSaveRestore` is true everywhere. Cause: an object changes its state without the world knowing it as changed; its old hash stays in the
sum until something recomputes all hashes – `WorldSnapshot.Write` does that (`ComputeFullHash`). The session therefore hashes differently after saving than a run that has never saved; the previous checks
compared only the two continuations with each other and did not see it. For lockstep that means: two machines with the same state report different hashes as soon as one of them writes a snapshot.
The documented mission hashes of these five maps (also `MD_GLA05` `317B464E8BA98377`) are hashes with stale parts.

`--verify-incremental` calls `SimulationWorld.VerifyIncrementalHash` in run 1 after every tick (stress test: every 1,000 ticks; the check refreshes the intermediate states, an affected run then deviates
in the hash as well). `--explain-incremental` additionally names object and fields: it describes every object with `Entity.Describe` and compares its hash with the one remembered by the world (`Entity.CachedHash`,
read via reflection – the only access to internals, no intervention in the simulation). Result (tier `quick` with explanation, plus the four other affected missions individually):

| Run | Ticks with deviation | Object, module, field |
|---|---|---|
| Reference match, 4 AI runs, `ai8_TwilightFlame_short`, `MD_USA05` (per tick), stress test (216 samples) | 0 | – |
| `mission_MD_GLA05` | 1 (after tick 854) | five `SupplyDock`: `SupplyWarehouseDockUpdate.boxes` (e.g. 400 → 740). Place: `SupplyWarehouseDockUpdate.SetCashValue` (`Modules/BuiltIn/DockModules.cs`) sets `Entity.Dirty = true` instead of notifying the world – the object is not in the dirty list, and later `MarkDirty` calls return immediately because of the set flag. Caller: script action `WAREHOUSE_SET_VALUE` (`ScriptEngine.ExtendedActions.cs`) |
| `mission_MD_USA01` | 4 (from tick 58) | `AmericaVehicleChinook`: `TransportContain.contained[]`, `usedSlots` on boarding |
| `mission_MD_USA02` | 27 (from tick 1,338) | `CivilianBunker01`: `GarrisonContain.contained[]` (`id`, `since`) on garrisoning; `AmericaCommandCenter`: `OCLSpecialPower.availableOnFrame` (0 → 2,485, tick 2,455) |
| `mission_MD_GLA03` | 29 (from tick 65) | `CivilianBunker01`, `GreekShop`, `GreekHouse2/3`, `AmericaFireBase`: `GarrisonContain.contained[]` |
| `mission_GC_LaserGeneral_ChinaNukeGeneral` | 24 (from tick 37) | `GuardTower`: `GarrisonContain.contained[]`; `Lazr_AmericaParticleCannonUplink`: `ParticleUplinkCannonUpdate.pendingPath[]`, `pendingPathTick` (tick 360) |

By the look of it, the cases have in common that a script sets the state (garrisoning, boarding, warehouse value, special power, beam path); in no AI run of the tier `quick` and not in the reference match did
a deviation occur. Only the place in `SetCashValue` has been read up in the source; the call paths of the other four fields have not been examined. **Not fixed** (brief of this work: report, do not
change). The correction changes the hashes of the five missions and belongs in a commit of its own with a new manifest; afterwards the five booleans must be true. Not run with explanation: the 12 AI runs,
the three long eight-player runs, the sweep and the remaining 21 missions of the tier `full` (their continuation after loading equals the uninterrupted run, which rules out a stale hash at the save point at
tick 1,500, not after it).

### 37.5 Input manifest

On a deviation between two machines, `baselines/input-manifest.json` first answers the question whether the inputs are already different.

| Section | Content | Source |
|---|---|---|
| `definitions` | SHA-256, length and number of definitions of the retail base data (map overlays excluded) | `DefinitionSnapshotWriter.ComputeFingerprint` |
| `modelGeometry` | hash of the bone table (airfield layouts from the models), number of airfields | `ModelGeometry.Fingerprint`, built as in the client (`ModelGeometryLoader`) |
| `worldFingerprint` | what world, replay and savegame check: definitions + bone table | `ModelGeometry.Combine` |
| `catalogs` | per catalog (`templates`, `weapons`, `upgrades`, `specialPowers`, `sciences`, `commands`, `factions`, `crates`, `creationLists`) and for the whole `TemplateCatalog` (`whole`, including the global values from `GameData.ini`) a content hash, the number of entries and of the described values | `CanonicalObjectHasher` |
| `files` | size and SHA-256 of `Campaign.ini` and `ChallengeMode.ini` | content manifest |
| `maps` | per map (150): size and SHA-256 of the file according to the content manifest, terrain fingerprint, cells, number of start positions | `ContentFileSystem.GetEntry`, `TerrainMap.Fingerprint` |

The catalogs have no serialised form of their own. `CanonicalObjectHasher` therefore describes the object graph itself: numbers, booleans and enums as a value, `decimal` invariant, fractions as a bit pattern,
texts with length; a struct is its fields; an object is the SHA-256 of type name and all instance fields (also private ones, also those of the base classes, sorted ordinally by type and name – not in the
order of reflection); arrays and lists keep their order, dictionaries are sorted by the description of the key, sets by that of the element. Memory addresses, hash codes of the
runtime and insertion orders do not enter. Measured: 588,087 described values for the whole catalog, three independent processes deliver the same file.

**Covered** by this is everything that `TemplateCatalog.Build` makes of the definitions and the bone table (templates including parsed module data and everything they reference, weapons, upgrades,
sciences, special powers, command sets, factions, crates, creation lists, global values).

**Not covered:**
- Code. Delegates are only counted (366: per module binding parser and factory); which program evaluates the data is in neither of the two manifests (finding D5: there is no identifier of the code state).
- The comparer of a dictionary (ordinal or case-insensitive) and static state.
- The decoded map objects, scripts and waypoint ids as well as their conversion to `Fixed` (findings D2/D5): the terrain fingerprint does not contain them. They are captured only indirectly – via the file hash
  and via the mission and AI runs of the reference manifest.
- The file hashes are taken from the content manifest, not recomputed (the runtime checks only the length on opening; the content is checked by `AssetTool verify`).
- The synthetic fixture of the stress test (it is source code).
- The overlay definitions of the maps (`map.ini`): like the client, the runs use the retail base data.

### 37.6 Execution and run times

The runs are independent and are processed by `--jobs N` worker threads (default: half the number of logical processors), the heaviest first. Every worker builds its own catalogs (nothing from
the simulation is shared between threads; compiling runs sequentially); all workers must reach the same world fingerprint, otherwise the run aborts. The output does not depend on the
order in which runs finish.

Measured on 6 October 2026 on a machine with 16 logical processors, Release, `--jobs 6`, **not quiet** (other processes were running alongside): `write --set quick` 283 s; `write --set full` 1,663 s (27.7 min;
of which about 13 minutes together with a second run of our own); `check --set full` passed twice, 1,599 s (`--jobs 5`) and 1,438 s (24.0 min); `check --set quick` passed, 262 s;
`input-manifest write` and `check` 13 s each. `check --set quick --verify-incremental`: 268 s, 60,896 ticks checked, one deviation (`mission_MD_GLA05`, §37.4) and therefore exit code 1 –
as long as D8 is open, that is the expected outcome of this mode. The longest single run is `mission_MD_USA01`
(632 s for 3,000 ticks in three and a half passes).

### 37.7 Tests

`tests/CnCNew.Simulation.Tests/Tests.ReferenceManifest.cs` (three tests): canonical notation and round trip, strict reading, "first deviating field" on a synthetic manifest, independence of the
content hash from insertion orders. `ManifestDocument.cs` and `CanonicalObjectHasher.cs` depend only on the BCL and are compiled into the test project as linked source files (no reference to an
executable project, no new dependency). The runs themselves are checked by `check`; a test over the real content would be the same half-hour run.

### 37.8 Limits

- One machine, one operating system, one .NET version: the manifest is the yardstick for the determinism matrix (brief 3 of Phase 18), not its result.
- Missions run 3,000 ticks, eight-player runs 9,000: late game events of these runs are not captured. The 16 AI runs cover up to 45,000 ticks.
- The sweep and the missions are not replays; their command stream is not in the manifest.
- `write --set quick` writes a file with only the quick runs; the checked-in file is written with `--set full`.

## 38. Phase 22 (6 October 2026): fixed point Q39.24, formats and new reference values

The complete log (preparatory work, comparison of all result quantities, overflow analysis, matrix, performance, counters at 60 and 120 Hz) is `docs/FIXED-POINT-Q39-24.md`. Here is what applies to the core.

### 38.1 Numerics

- `Fixed`: `long`, **24 fractional bits** (`Fixed.FractionBits`), resolution 2⁻²⁴, range ± 5.5 · 10¹¹. Rounding unchanged (to the nearest number, half values away from zero), overflow throws.
  New: `Fixed.FromFraction(zähler, nenner)` for constants that are a fraction, `Fixed.Epsilon` (one step). `ToDecimal()` is exact up to 65,536 units, above that rounded to the 28 digits of a `decimal`
  (far below half a raw step).
- `FixedMath` (`Numerics/FixedMath.cs`) carries the arithmetic on raw values with the bit count as an argument: product, quotient (long division on machine words for large dividends), root
  (64-bit root plus bitwise extension against the exact 128-bit square), π and its fractions, arctangent table, `Atan2` (CORDIC, one step per fractional bit), `Sin`/`Cos`
  (Taylor up to x¹³/x¹²). Constants with 61 fractional bits, rounded to the resolution. Measured largest error at 24 bits: `Sin` 1.0 · 10⁻⁷, `Cos` 1.2 · 10⁻⁷, `Atan2` 2.7 · 10⁻⁷ rad (at 16 bits:
  2.6 · 10⁻⁵, 2.4 · 10⁻⁵, 5.9 · 10⁻⁵). The earlier statement in the source ("`Atan2` below 10⁻³ rad") was a bound, not a measured value.
- `FixedTrig.PiFraction(zähler, nenner)` for angle constants (π/6 of the movement).
- **The square of a value above 741,455 does not fit** (the root of the largest number). The data contain such values (ranges 999,999 and 1,000,000, "instant" speeds 999,999, health 99,999,999,
  `AbilityAbortRange` 10,000,000). A range, a radius or a limit is therefore never squared with `*`, but compared with a squared distance via **`SquaredReach`** (`Numerics/SquaredReach.cs`):
  `SquaredReach.Of(reach).Contains(d²)` is `d² <= reach * reach`, `ContainsStrictly` is `<`, with the square in 128 bits – bit-identical to the old expression where that could compute, and "always within" where the
  square leaves the range. `Fixed.MultiplyDivide(a, b, c)` is `a * b / c` with both roundings for a product that alone would leave the range (health ratio). The catalog value remains what the
  data say; nothing is capped on loading. The finding of Phase 22 is thereby fixed, no reference value has changed (`docs/FIXED-POINT-Q39-24.md` §5a). Rule: `Tests.SquaredReachSourceRules`
  (no `x * x` in the simulation except coordinate components and 21 named exceptions); tools: `SimTool rate-audit <content> --ranges`, `SimTool range-audit <content>`.
- **The simulation is not `checked` as a whole**, only the operators of `Fixed`. Raw value computations are in `checked(…)` or compute in 128 bits (`VisionMap`, `StructureToppleUpdate`,
  `ConstructionHealthTarget`).
- **Vision circles** (`VisionMap`): the test "cell centre in the circle" is defined at 16 fractional bits (centre and radius truncated to that), whatever resolution `Fixed` has. The square of a radius above
  46,340 units counts as "greater than every squared distance" (`SquaredRadius`), which it is for every cell within 32,768 units; the differences remain checked.
- **Idle scan of the turrets:** the fraction of a draw is the lowest 16 bits of the hash as leading binary digits, further digits from the bits above.

### 38.2 Formats

`WorldSnapshot.Version` **10**, `ReplayFormat.CurrentVersion` **12**, `CommandTypes.Version` **15**, `MissionSnapshot.Version` **3**, `SaveGame.Version` **2**. All five carry raw values; a state with 16
fractional bits (9 / 11 / 14 / 2 / 1) is rejected at its version with a clear message and never reinterpreted (`Tests.StatesOfSixteenFractionBitsAreRejected`, `Tests.Scripting`, `CampaignTests`). The
version statements in the sections before are history.

### 38.3 Reference values (yardstick: `baselines/reference-manifest.json`, state `d7fca91`)

| Quantity | Value | before |
|---|---|---|
| Reference match (`ReferenceMatchOptions.Standard`, driver seed now `5EED0007`) | winner FactionGLA at tick **17,805**, final hash `D0CA5F4EE456D6CE`, chain `C5EE1090E1C24D7B` | until Phase 21: tick 14,490, `23B4B25DF62765D8` / `DE5849AF9F8493C4` (seed `5EED0005`); with the new seed at 16 bits: tick 14,055, `AE9C688309DA37E4` / `FC6DFA759B1DCAF0` |
| Stress test (216,000 ticks) | final hash `56882F763EABEF67` (empty final state, unchanged), chain **`754FA4DCB9A41DF7`** | chain `B8E663493F2C73D5` |
| 16 AI runs | 12 decided; five with a different outcome than at 16 bits, one change of winner (`docs/FIXED-POINT-Q39-24.md` §7.2) | 12 decided |
| 27 mission runs, skirmish overview, 8-player runs | outcomes as before; new hashes | – |
| Campaign | objective driver victory 24/24, defeat 24/24 (on every map the same end script in the same tick as at 16 bits); audit 24/24 deterministic and identical after save/load, end probes 13/24 and 15/24; reachability clean 24/24 and 24/24 | the same numbers |
| World fingerprint of the inputs | `49A55E310F780F60` | – |

The seed of the reference match was changed in a commit of its own (`41c3bc0`) because with the old one the match does not end at 24 bits; the survey over 36 seeds is in
`docs/FIXED-POINT-Q39-24.md` §6. The AI tables in §19, §32 and §36 and all hashes in the sections before are superseded.

### 38.4 Tools

- `SimTool reference-manifest results <content> --out <json> [--commands <dir>] [--set quick|full] [--only …]`: per run, what is not a hash (outcome, end tick, objects and money per player, commands by type,
  events by kind; missions: outcome, tick and name of the end script, script counters) – and on request the command stream as text with quantities in whole units. Two states that are **supposed** to
  differ (resolution, tick rate) are compared with it: quantity by quantity and via the first deviating command line.
- `SimTool reference-match … [--world-seed HEX] [--driver-seed HEX] [--record-only]`: the course of the reference match under other seeds.
- `SimTool rate-audit <content> [--tick-rate N] [--ranges [--ranges-above N]]`: also runs at 30 Hz and lists the value range of the catalog (largest magnitude, every value above the root of the largest number;
  with `--ranges` the largest magnitude of every fixed-point field).
- `SimTool range-audit <content> [--frames N] [--distance N]`: every retail object with a weapon gets a fire command on a tank and a soldier; lists every run that throws, and the weapons
  with values above the square limit.
- `Tests.FixedScaleSourceRules`: the scale is stated once – no `65536`/`16777216`/`<< 16`/`<< 24` in any project (33 named exceptions), outside the simulation only
  `CnCNew.Presentation.FixedConvert`, no raw value as a number.

## 39. Phase 23 (7 October 2026): rate-equivalence suite

The suite shows whether the game at 60 and 120 Hz is *the same game* as at 30 Hz – not through hashes (they cannot match), but through measured game quantities in seconds and
world units. It measures; it does not decide what is correct. Its first run **is meant to** fail broadly: the list of failures is the work backlog for step 3 of the phase
(`docs/handoffs/HANDOFF-PHASE23.md`). This section defines the suite and its tolerances; the tolerance table (§39.2) was fixed before the suite ran completely for the first time, and is never adjusted to a
result.

### 39.1 Structure

- **Scenes and measurements** live in `src/CnCNew.Simulation/Testing/RateEquivalence/` (namespace `CnCNew.Simulation.Testing`), so that test and tool run the same cases. The folder
  is subject to `SourceRules` like the rest of the simulation: no floating point, no wall-clock time, no bare tick counts (budgets in whole seconds through `RateScene.Seconds`, run times through
  `Time.WholeSecondsToTicks`), no `x * x` on ranges. **Every measured value is an integer.**
- **Types:** `RateQuantityKind` (six kinds, §39.2), `RateQuantity(Name, Kind, Units, Present)`, `RateCase(Id, Group, Measure)`, `RateScene` (rate, catalog of the rate, flat map, two
  hostile players who see the whole map; the world is created on first access), `RateQuantities`/`RateMeasure` (helpers), `RateTolerance.Within(kind, reference30, value)`,
  `RateCases.Group(catalogs, name)`/`RateCases.All(catalogs)` (fixed cases and generators), `RateRunner` (runs a case at three rates, builds the table and the TSV file).
- **Units:** times in 1/120 s (`Ticks · 120 / Rate`, an integer at 30, 60 and 120 Hz), distances, angles and amounts as the raw value of `Fixed`, counts as a number.
- **Time convention:** a time is "the tick in which it happened, minus the tick in which the command was executed". A command that is submitted before a step is executed in that
  step; what happens in the same step has the time 0 – at every rate. (`RateScene.StepUntil` and `RateCases.Elapsed` keep that in one place.) A measurement begins with
  `RateScene.Align` at a frame boundary of the original (`world.Time.IsOriginalFrameBoundary`) and after the step that created the objects; every `Spawn` takes one original frame,
  so that before the measurement the same game time has passed at every rate. The suite itself adds no offset below one frame.
- **Lengths of paths** are sampled once per original frame (the same chords at every rate); health is compared once per frame for "frames with healing".
- **Rates:** the suite names its rates itself (`RateCatalogs.Rates`: 30, 60, 120 Hz). It observes neither `--tick-rate` nor `TestRate.Default` and never sets `SimulationTime.Probe`.
- **Catalog:** the real definition graph is compiled once per process (`Tests.RateCatalogsOf`, `SimTool` `CompileGraph`), the three catalogs are created on first access. Test and
  tool build **without model geometry** (parking and runway points of the airfields from the template geometry), so that both print the same numbers; `rate-compare` says so in its
  first line. Cases are created from the 30 Hz catalog and are found again in the others through the template or weapon name; what a scene derives from data (distance, travel distance),
  it takes from the 30 Hz catalog (`RateScene.Reference`), so that the scene is the same at every rate.
- **Map:** `FlatMap.Build` (the same code as `SyntheticMaps.Build` of the tests so far, which now delegates to it; the bytes of the existing maps are unchanged). Default 400 × 400
  cells (4,000 units), flight cases 600 × 600.
- **Self-acquisition off:** units do not choose victims themselves in the scenes (`SimulationWorld.SelfAcquisition = false`), except in the AI group.
- **Staging without a command:** where there is no player command, the scene calls the place that the command would call, and says so in the source: `SimulationWorld.FireWeaponAt` (projectiles),
  `JetAIUpdate.OrderLand` (return home of an aircraft), `HackInternetAIUpdate.StartHacking`, `SimulationWorld.CompleteUpgrade`/`GrantScience`/`SetVeterancy`, `ISpecialPowerModule.SetReadyFrame`
  (capture without waiting time), the flags of the weapon sets (`HasPlayerUpgradeWeapon`, `CrateWeaponLevel`). Damage, kills and neutral objects come through system commands.
- **Two read accessors are new** (no fields, no state): `ToppleUpdate.IsDown` and `ToppleUpdate.ToppleAngle`.

### 39.2 Tolerances (fixed before the first run; the reference is always the 30 Hz value, 60 **and** 120 Hz must lie within)

| Kind (`RateQuantityKind`) | What | Permitted deviation from the 30 Hz value | Reason |
|---|---|---|---|
| `ExactTime` | Durations that come from the data and lie on the 30 Hz grid: shot interval, reload, `PreAttackDelay`, build, production and upgrade time, sale, cooldowns, lifetimes, script timers, `EndGameDelay` | **exactly equal** | Phase 20 put these times on the grid of the original (`docs/TICKRATE-MIGRATION.md` §2); every deviation is a one-tick offset or a counter N + 1 |
| `Count` | Number of shots, pulses, events, flashes in fixed game time | **exactly equal** | Events on the grid occur equally often at every rate |
| `MovementEventTime` | Points in time that follow from movement or integration: arrival, time to top speed, time for 90°/180°, flight time, impact, landing, duration of a topple, first shot after a turret swing | **\|Δ\| ≤ 1 original frame** (4/120 s) | the original does not resolve finer than one frame; one step more or less at a threshold is at most one frame |
| `Distance` | Distances and end positions in world units | **\|Δ\| ≤ 0.5 %** of the 30 Hz value, at least **0.5 units** | 0.5 % is below every quantity visible in the game; the floor (one twentieth of a pathfinding cell of 10 units) applies to quantities near zero such as the arrival error |
| `Angle` | Angles in radians | **\|Δ\| ≤ 0.5 %** of the 30 Hz value, at least **0.01 rad** | like `Distance`; 0.01 rad is 0.57° |
| `RateOver60s` | Amounts over fixed game time: damage and healing per 60 s, income per 5 minutes, refund | **\|Δ\| ≤ 1 %** of the 30 Hz value | one pulse more or less at the window boundary with over 100 pulses; above that it is a rate error |
| – | a quantity that is present at one rate and missing at another (the event did not occur within the budget) | **failure** | |
| – | an exception at one rate | **failure** of all quantities of that rate, own row `(exception)` | |

A quantity that is missing at **all three** rates is neither passed nor a difference between the rates: it stands as `unmeasured` in the table and is counted, but not rated as a
failure. The numbers stand as constants in `RateTolerance` (`OneOriginalFrameUnits`, `HalfPercentDivisor`, `OnePercentDivisor`, `DistanceFloorRaw`, `AngleFloorRaw`).

### 39.3 Groups

| Group | Cases | What is measured |
|---|---|---|
| `locomotion` | one per distinct locomotor of the retail data (grouped by the 30 Hz values of `LocomotorInfo`, the first object by name stands for each) | Straight-line travel: top speed, time and distance until then, braking distance and time, arrival time, arrival error, path length; time for 90° and 180°; a square course (time, path length, end error); the damaged state (`SpeedDamaged` …) on the same straight |
| `flight` | Raptor, Aurora, MiG and Comanche from the airfield; Comanche and Chinook free; the paradrop of the command center; the Raptor attack of the scene audits | Production, taxiing to the parking space, takeoff, cruise, turn-around, landing, taxiing; climb, flight and stop of the helicopters; aircraft, drop and landing of the paratroopers; first and last shot, return home and reload |
| `weapons` | one per distinct weapon that an object carries in a default, veteran, upgrade or crate set (grouped by times, damage, range, speed and projectile) | Command to first shot (for turrets as a movement event), shot interval, clip, reload, second reload, time of the 20th shot (firing stages), shots and damage in 60 s on a standard target, first damage after the first shot, effective range (bisection over the question with which a fire command is checked) |
| `projectiles` | one per distinct projectile of a weapon (projectile object, speed, scatter, range), by module: `DumbProjectileBehavior`, `MissileAIUpdate`, `NeutronMissileUpdate`, others | Flight time, impact point, apex height and path length onto a ground point; flight time and hit on a standing and on a crossing target; mean scatter over 40 shots |
| `turrets` | one per distinct turret of a ground object (turn rate, recentre time, scan and swing values) | Swing by 90° and 180° until the first shot, turret angle at the shot, holding and return to the rest position, idle scan (first and third look, looks in 120 s, largest angle) |
| `overtime` | one per distinct field object (poison, radiation, fire), poison directly, one per distinct `FlammableUpdate`, 18 healing and repair cases | Lifetime of the field, damage pulses, damage and death of tank and soldier; burn duration and fire damage; first and second healing, time to full health, frames with healing, healing in 60 s (self-healing, base regeneration, medic, hospital, repair bay, dozer, repair dock, barracks, tunnel, troop transport) |
| `economy` | 4 construction sites, 7 productions, 5 upgrades, 3 sales, 3 gatherers, 3 income sources | Construction start and construction time; production time, second unit, leaving the factory; upgrade time; sale and refund; first delivery, round trip, deliveries and income in 5 minutes; cadence of black market, oil derrick and hacker |
| `timed` | one per special power (recharge), per distinct `LifetimeUpdate`/`DeletionUpdate`, per distinct set of `SlowDeathBehavior`, per distinct `ToppleUpdate`, `StructureToppleUpdate`, `StructureCollapseUpdate`, `StealthUpdate`, `StealthDetectorUpdate`; in addition helicopter crash, Battle Bus, mines, vision, reveal, staged capture, avalanche, EMP, firestorm, train | Recharge time; lifetime; kill to removal; crash; topple (duration and angle), collapse; mine damage; stealth, loss of stealth, detection; vision radius; flashes and duration of the capture; slide distance of the avalanche; EMP duration; starting, braking and stop of the train |
| `scripts` | 5 cases with synthetic map scripts on the real catalog | Timers in seconds and in frames (fixed and random), counters per evaluation, evaluation delay, sequence waiting times and paths, `EndGameDelay` (`VICTORY`, `DEFEAT`, `QUICKVICTORY`) |
| `ai` | 3 pairings of the `SkirmishAi` on a flat map, 8 game minutes | first command, first build order, first building, first barracks, first attack command; commands in the first minute, in three and in eight minutes; build and production orders; money after eight minutes |

The number of cases per group, the groupings of the generators and what they could not stage are printed by `SimTool rate-compare <content> --list`; the state of the first run is in §39.6.

### 39.4 Running

```powershell
# Suite (Release, mit Realdaten); ohne --content-dir sind die Tests nicht registriert
dotnet build -c Release tests/CnCNew.Simulation.Tests
dotnet run -c Release --no-build --project tests/CnCNew.Simulation.Tests -- --content-dir game/content --only RateEquivalence --rate-report <datei außerhalb des Repos>
dotnet run -c Release --no-build --project tests/CnCNew.Simulation.Tests -- --content-dir game/content --only "RateEquivalence: weapons"

# Einzelfälle
dotnet build -c Release tools/CnCNew.SimTool
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- rate-compare game/content --list
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- rate-compare game/content --group turrets [--tsv <datei>]
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- rate-compare game/content "locomotion/Nuke_ChinaTankBattleMaster"
```

One test `RateEquivalence: <gruppe>` per group. A group test catches exceptions per case and per rate (own row), collects **all** quantities, prints the table
(`case | quantity | kind | 30 Hz | 60 Hz | 120 Hz | d60 | d120 | result`; times in seconds, distances in world units), the run time of the group and then throws exactly once:
"n of m quantities in k cases outside tolerance". The test writes nothing into the repo; `--rate-report <datei>` (or the environment variable `CNCNEW_RATE_REPORT`) writes the whole table as
TSV with the integer units, the permitted deviation and the verdict to a place outside the repo (a path inside the repo is rejected). `rate-compare <case-id>` takes the exact
identifier or a part of it; `--tick-rate` is ignored. Two runs produce a byte-identical file (§39.6).

### 39.5 Not covered

What a generator or a fixed case cannot stage stands as `not covered` in the output of `--list` and of the group test. The list:

- **Locomotion:** locomotors that only an upgrade or a locomotor set other than `SET_NORMAL` gives; slopes, water and cliffs (the map is flat); avoidance around standing units,
  collision and formations (whole group movements are a matter for the distribution comparison, step 4).
- **Flight:** parking and runway points from the model bones (the world of the client); the remaining `JetAIUpdate` objects of the generals (not checked per object). **The paradrop of the
  special power cannot be measured:** the five parachutes die at every rate in the step of their creation, the soldiers stand at the map edge (a finding, not a rate difference; §39.6).
  An aircraft has no player command "back to base" (`MoveAndEnterCommand` requires a container); the landing is triggered through `JetAIUpdate.OrderLand`.
- **Weapons:** weapons only in sets that the suite cannot switch on (car bomb, hijack, mine clearing, rider), and weapons that no object carries in a set (fired by modules,
  creation lists and special powers); bonuses of battle plans and of upgrades that do not switch the set; the effective range of weapons that only aircraft carry; weapons that
  hit only projectiles, mines or parachutes (appear as `unmeasured`).
- **Projectiles:** launch points at weapon bones and the carrier's own movement; every projectile launches from the centre of a standing tank.
- **Turrets:** second and coupled turrets, pitch (anti-air), turrets on aircraft.
- **Damage and healing:** healing in an occupied civilian building (`GarrisonContain`), self-healing that only an upgrade or a rank starts, fire that jumps from object to object.
- **Economy:** the construction time of every single object (catalog durations, read with one conversion), several dozers at one construction site, power shortage, drop zone, bounty, the ranks of the
  hacker beyond five minutes. **An upgrade is finished at every rate in the tick of the purchase** – the simulation keeps no upgrade time (a finding, not a rate difference).
- **Timed modules:** the sequences of the superweapons after triggering (Scud Storm, particle cannon, A-10, carpet bomb, artillery, Spectre, leaflets, cluster mines), battle plans, bridge scaffold,
  checkpoint, wave guide, the steering of the MOAB; recharge after a use and shared timers after the first use; stealth of aircraft, mines and crews.
- **Scripts:** the 109 condition and 344 action types individually, team queues, reinforcement with transport, the scripts of the campaign maps (step 4 compares them per map).
- **AI:** outcome and length of whole matches, army and economy later in the match, more than two players, real maps (step 4).
- **Not checked:** that every row of the list in `docs/TICKRATE-MIGRATION.md` §5 is captured by a case – the mapping in §39.6 names what was found and what is not staged.

### 39.6 First complete run (7 October 2026, Release, real data `game/content`)

Measured at the state of this section; nothing in the simulation was changed for it (`input-manifest check` identical, `reference-manifest check --set quick` 9 runs identical, all 414
simulation tests without real data green including `SourceRules`). Two runs in a row produce a byte-identical TSV file. After the very first run one measurement of the suite
was corrected (lifetimes and field lifetimes read "counts as dead" instead of "removed from the world" and showed 0 s at every rate for objects without a body – a false "passed"); the
tolerances are untouched, the numbers below are those of the corrected run (first run: 639 instead of 643 failures).

| Group | Cases | Quantities | outside at 60 Hz | at 120 Hz | Quantities with failure | Cases with failure | unmeasured at every rate | Run time |
|---|---|---|---|---|---|---|---|---|
| `locomotion` | 54 | 837 | 152 | 211 | 217 | 52 | 50 | 10.7 s |
| `flight` | 8 | 74 | 21 | 21 | 23 | 7 | 5 | 1.8 s |
| `weapons` | 194 | 1,474 | 43 | 57 | 57 | 27 | 43 | 51.6 s |
| `projectiles` | 91 | 743 | 54 | 103 | 106 | 53 | 16 | 4.7 s |
| `turrets` | 31 | 216 | 28 | 36 | 40 | 23 | 4 | 2.4 s |
| `overtime` | 45 | 418 | 80 | 84 | 85 | 32 | 55 | 9.2 s |
| `economy` | 25 | 81 | 22 | 25 | 26 | 18 | 0 | 3.8 s |
| `timed` | 221 | 383 | 53 | 56 | 56 | 33 | 36 | 7.7 s |
| `scripts` | 5 | 15 | 4 | 5 | 5 | 3 | 0 | 0.7 s |
| `ai` | 3 | 66 | 22 | 27 | 28 | 3 | 0 | 53.7 s |
| **Total** | **677** | **4,307** | **479** | **625** | **643** | **251** | **209** | **about 2.5 min** |

Groupings of the generators: 442 objects drive on a move command with a locomotor without wings, 54 distinct locomotors; 363 weapon templates, of which 194 distinct ones in
a weapon set that can be switched on; 91 distinct projectiles; 31 distinct turrets; 845 flammable objects, but only 4 distinct `FlammableUpdate`.

**The failures by probable cause** (values: 30 / 60 / 120 Hz; mapping to `docs/TICKRATE-MIGRATION.md` §5, where there is a row). "Probable" means: inferred from the pattern
of the numbers and the source, not verified by a correction.

| # | Cause | §5 | Example | Extent |
|---|---|---|---|---|
| 1 | Euler step of ground movement, braking distance and arrival radius in tick units | 5.2 `AIUpdateInterface.Move`, `LocomotorInfo.CloseEnoughDist`, `Entity.Speed` | Chinook, 1,500 units straight ahead: distance to top speed 195.0 / 191.25 / 189.375; braking distance 110.0 / 111.25 / 111.875; arrival 11.7000 / 11.7833 / 11.8417 s; square course 21.1333 / 21.4667 / 21.7083 s | `locomotion`: 52 of 54 cases (distance to top speed 30/33 cases at 60/120 Hz, braking distance 19/23, braking time 11/30, arrival 23/36, course 41/47, damaged 17/32); in addition gatherer round trip (`economy`, 3 cases: 27.4000 / 27.4167 / 27.5667 s), sequence path (`scripts`), repair and entering (`overtime`) |
| 2 | Winged flight and airfield: taxiing, takeoff, circular path, landing | 5.2 `JetAIUpdate` (`TaxiAlongPath`, `TakeOff`, `Land`), `CircleStep`, `MoveWinged` | Raptor: takeoff 4.8333 / 4.7667 / 4.7250 s; Aurora: arrival 23.1000 / 23.5667 / 23.8083 s; Raptor attack: first shot 18.6333 / 18.7167 / 18.7083 s, parked and reloaded 60.8000 / 60.8500 / 61.1750 s | `flight`: 7 of 8 cases |
| 3 | Projectiles: flight duration in whole ticks, tracking and arrival threshold per tick | 5.2 `DumbProjectileBehavior`, `MissileAIUpdate`; 5.6 `FlightTicks` | Battlemaster shell onto a ground point: 0.3000 / 0.2667 / 0.2500 s; Aurora bomb onto a crossing target: miss distance 12.583 / 12.287 / 11.895 | `projectiles`: 53 of 91 cases (flight time 3 cases at 60 Hz, 12 at 120 Hz; miss distance with a moving target 19/35; path length 12/15); `weapons` "first damage after the first shot" 9 cases, only at 120 Hz |
| 4 | Neutron missile: damping and thrust per frame | 5.1 `NeutronMissileData`, 5.2 `NeutronMissileUpdate` | Flight time 8.0667 / 6.5667 / 5.8083 s (−19 %, −28 %); miss distance with a moving target 119.25 / 88.62 / 73.06 | 2 cases (`NeutronMissileWeapon`, `SupW_NeutronMissileWeapon`) |
| 5 | Turret: fire clearance against a fixed angle | 5.2 `IsAligned` (0.035 rad), `AimStep` | Fire Base, 90°: turret angle at the first shot 1.57080 / 1.55334 / 1.54462 rad (the shot falls up to 0.026 rad before the target); the swing **time** lies within one frame for all 31 turrets | `turrets`: angle 10/15 cases (90°) and 7/7 (180°); return 2 cases; third idle look 1/4 of 8 |
| 6 | One tick instead of one frame: first update one tick after appearing, counter N + 1 | 5.3 `Register`, `RunUpdates`, `ProcessSpawns`, `StepTurrets`, `StealthUpdate.MarkDetected` | Poison field: first damage 0.0333 / 0.0167 / 0.0083 s; stealth after creation 2.0333 / 2.0167 / 2.0083 s; first idle look 0.6333 / 0.6167 / 0.6083 s; loss of stealth ends 0.5000 / 0.4833 / 0.4750 s; avalanche notices the damage 0.9667 / 0.9500 / 0.9417 s | `overtime` fields: 42 quantities (first damage) and 17 (death of a victim); `timed`: stealth 9 of 14, loss of stealth 8 of 17, avalanche 1; `turrets`: 8 of 8 |
| 7 | Duration "N frames minus one tick" | 5.3 `_saleDurationTicks` (sale, as predicted), `HackInternetAIUpdate`, `BaseRegenerateUpdate.OnDamage`; **new:** production, construction, self-healing | Sale 5.9667 / 5.9833 / 5.9917 s; Ranger from the barracks 4.9667 / 4.9833 / 4.9917 s; power plant built 15.2333 / 15.2500 / 15.2500 s; hacker pays 8.1000 / 8.1167 / 8.1250 s; base regeneration begins 2.9667 / 2.9833 / 2.9917 s; mine heals itself 14.9333 / 14.9500 / 14.9583 s | Sale 3 of 3, production 7 of 7 (and the 4 airfield productions), construction 2/4 of 4, hacker 1, start of healing 7 of 11 |
| 8 | Shot count of continuous-fire weapons: one shot more in 60 s | 5.3 `StepFiringTrackers`/`PreAttackDone` (N + 1), 5.4 `AutoFire` | Gattling tank 440 / 441 / 441 shots; toxin truck 899 / 900 / 900; last shot 59.6000 / 59.7333 / 59.7333 s | `weapons`: 16/18 cases (including the two findings below: reload 0 and fire wall) |
| 9 | Counter per evaluation of the scripts | 5.4 `ScriptEngine.Update` (W6), timer values | Counter after 10 s: 300 / 600 / 1,200; "counter reaches 150" after 4.9667 / 2.4833 / 1.2417 s | 1 case, 2 quantities; sequence waiting time 90 frames 3.0333 / 3.0167 / 3.0083 s (5.4 `ProcessSequences`); `QUICKVICTORY` one step (order, measured as a duration) |
| 10 | Flash phase of the staged capture | 5.2 `SpecialAbilityUpdate.StepCaptureFlash` | Flashes 49 / 99 / 199; first flash 12.6000 / 11.7833 / 11.2000 s | 3 of 3 cases (Ranger, Red Guard, Rebel) |
| 11 | Toppling: angular acceleration and tipping force | 5.1 `ToppleUpdateData.InitialAccelPercent`, 5.2 `ToppleUpdate.Update`, `TryTopple` | Tree: fall 1.2000 / 0.8000 / 0.5667 s; wall 0.2667 / 0.2167 / 0.1667 s (the end angle is equal) | 4 of 5 measured `ToppleUpdate` |
| 12 | Helicopter crash: damping and spiral per frame | 5.1 `HelicopterSlowDeathData`, 5.2/5.3 `HelicopterSlowDeathBehavior` | Comanche: last position 26.524 / 20.950 / 24.518 units from the place it was shot down, rotation 18.117 / 17.174 / 17.061 rad (the duration is equal: 6.6000 / 6.6000 / 6.5833 s) | 3 of 3 |
| 13 | Train: factors per frame | 5.1 `RailroadData`, `RailroadBehavior` | Top speed reached after 2.4333 / 1.1000 / 1.0000 s; stands at the station after 19.8000 / 16.1833 / 14.7417 s, at 2,703 / 2,618 / 2,578 (station at 2,500) | 1 case, 4 quantities |
| 14 | Healing in smaller slices | 5.6 `RepairDockUpdate`, `HealContain`, 5.2 `TunnelContain.Update`; AutoHeal | Repair dock: frames with healing 150 / 151 / 151; tunnel 75 / 76 / 76; speaker tower 750 / 751 / 751 | 5 cases at 60 Hz, 4 at 120 Hz (the amount healed in 60 s is equal) |
| 15 | AI: number of commands | 5.4 `SkirmishAi` (`Think`, the world has moved more finely between two thinking steps) | USA against China: commands in three minutes 274 / 297 / 300, in eight 696 / 659 / 691, production orders 65 / 61 / 63, money 0 / 675 / 600; **first command and first build order are exactly equal for all six players**, the first barracks within one frame at 60 Hz (36.5000 / 36.5333 / 36.5417 s) | 3 of 3 cases, 28 quantities |

**Failures without a row in §5 (new findings):**

- **A reload of 0 is one tick, not one frame:** battleship guns (`BattleshipDemoGun`, `BattleshipUSSBenderGun`): reload 0.0333 / 0.0167 / 0.0083 s, shots in 60 s 110 / 112 / 112,
  damage +1.9 %. 2 cases.
- **Fire wall of the Dragon Tank:** 838 / 899 / 899 shots in 60 s (30 Hz loses about 7 %), damage 14,605.5 / 14,250.5 / 14,253.0; also the upgrade version. 2 cases. Cause not investigated.
- **Production and construction take N frames minus one tick** (row 7): the same form as the sale, but not listed in §5.
- **Recharge from creation** for six objects with a superweapon (`Lazr_LaserCannon`, `SupW_CruiseMissile`, two neutron missiles, particle cannon, Scud Storm):
  120.0000 / 120.0167 / 120.0083 s – the ready tick lies two ticks behind the grid at 60 Hz, one tick at 120 Hz. The remaining 62 powers are exactly equal.
- **Three locomotors with 2 or 1 unit per second at 30 Hz have half of that at 60 Hz and a quarter at 120 Hz** (`AirF_AmericaCrateParachute`, `CINE_U04_AmericaParachute`,
  `HorizontalArrow`: 2.000 / 1.000 / 0.500): a speed per tick that does not scale with the rate.
- **Combat bike and mob:** `Boss_VehicleCombatBikeTerrorist` reaches its top speed after 2.0333 / 4.6667 / 1.3250 s and drives 1,447 / 1,458 / 1,440 units for 1,440; the mob nexus drives the
  square course with 400.06 / 404.46 / 430.00 units. Not monotonic in the rate; not investigated.
- **Scatter shots that never impact:** `ArtilleryPlatformGun` 8 / 8 / 7 of 40, `BattleshipUSSBenderGun` 21 / 20 / 20 (at every rate only a part of the 40 projectiles launched in the same step
  impacts; the number depends on the rate).
- **Individual cases:** cluster mine removed after 4.8000 / 4.7000 / 4.6667 s; Helix, lowest height before removal 0.000 / 0.798 / 0.935; `CINE_BShipWeaponDetonationObject` does not end at 30 Hz within
  300 s, at 60 and 120 Hz after 0.0667 s; Patriot against air targets at 120 Hz +1.2 % damage in 60 s; `AirF_AmericaCrateParachute` removed after the kill after
  3.0000 / 2.9833 / 2.9750 s.

**Findings that are not a rate difference** (equal at every rate, noticed while staging the scenes): the paradrop of the special power loses all five parachutes in the step
of their creation; an upgrade is finished in the tick of the purchase; the Battle Bus dies immediately on `KillCommand` (no second life, no throw measurable) and reaches its destination on a straight
drive at no rate with `MoveArrived`; an aircraft has no player command for the return home; the avalanche does not slide on the flat map (slide distance 0, the recursion from §5.1 is therefore
**not** checked); the troop transport does not heal its occupants.

**Passed, although §5 names a difference:**

- §5.1 `EmpUpdate` (`_scale`): paralysis after 0.3000 s and until 30.3000 s at every rate – as noted there, no effect on the game.
- §5.1/§5.2 `StructureToppleUpdate` (decay, crush shots per tick): the one distinct retail object (`GLAVictoryStatue_Topple`) lies down after 4.3333 / 4.3000 / 4.3000 s, with one
  hit and one dead among eight bystanders at every rate. The doubled crush shots do not show in this setup.
- §5.2 `StructureCollapseUpdate`: collapse 2.4667 / 2.4833 / 2.4917 s (`AsianBank`), within one frame for every distinct collapse.
- §5.2 turret swing (`AimStep`, `RecenterStep`): the time to the first shot lies within one frame for all 31 turrets; only the angle at the shot falls outside (row 5).
- §5.2 helicopter crash: duration and half height within one frame (only position and rotation fall outside, row 12).
- §5.3 `FirestormDynamicGeometryInfoUpdate` and §5.6 `DynamicShroudClearingRangeUpdate`: firestorm, spy satellite and radar van ping equal in every quantity.
- §5.4 `PointDefenseLaserUpdate`, `FireWeaponWhenDamagedBehavior`, `MinefieldBehavior`: not staged or (mine) damage equal, first damage at 60 Hz within one frame.
- §5.6 `WeaponInfo.WeaponSpeed`, quantisation of the catalog values: shot interval and clip duration are exactly equal for all weapons, the reload for all except the two with
  reload time 0, the time of the 20th shot (firing stages) for 117 of 121, "command to first shot" for all; the effective range for all ground carriers.
- Scripts: timers in seconds and in frames, fixed and random, the evaluation delay and `EndGameDelay` are exactly equal.

**Not checked:** whether each of the 95 rows from §5 is hit by a case; the causes are assigned, not verified by correction; no case has been looked at by a human.

**Addendum (9 October 2026):** the course over the rounds and the final state (0 / 0 in all ten groups) are in §39.7; what became of each of the 95 rows is in `docs/TICKRATE-MIGRATION.md` §5.
### 39.7 Course over the rounds and final state (9 October 2026)

Quantities outside the tolerance at 60 Hz / at 120 Hz per group. The tolerances from §39.2 were not touched in the whole phase. Columns: first run (§39.6, `91cb830`), after round 1
(seven packages, `62fbb6a`), after round 2 (consolidation, `3b44506`), after round 3 (`502e75b`: observers and flight, in between the correction of the lost wake request and the factory exit), final state
(`1b82e45`, after the AI corrections and round 4; counted from the log of the final check).

| Group | Cases (end) | Quantities (end) | first run | after round 1 | after round 2 | after round 3 | **Final state** | unmeasured at every rate (end) |
|---|---:|---:|---|---|---|---|---|---:|
| `locomotion` | 54 | 837 | 152 / 211 | 12 / 12 | 2 / 2 | 0 / 0 | **0 / 0** | 50 |
| `flight` | 8 | 74 | 21 / 21 | 0 / 4 | 0 / 3 | 0 / 0 | **0 / 0** | 0 |
| `weapons` | 194 | 1,474 | 43 / 57 | 0 / 0 | 0 / 0 | 0 / 0 | **0 / 0** | 43 |
| `projectiles` | 91 | 743 | 54 / 103 | 10 / 9 | 4 / 4 | 0 / 0 | **0 / 0** | 16 |
| `turrets` | 31 | 216 | 28 / 36 | 4 / 0 | 4 / 0 | 0 / 0 | **0 / 0** | 4 |
| `overtime` | 45 | 418 | 80 / 84 | 2 / 2 | 0 / 0 | 0 / 0 | **0 / 0** | 52 |
| `economy` | 34 | 162 | 22 / 25 | 7 / 10 | 0 / 0 | 0 / 0 | **0 / 0** | 0 |
| `timed` | 221 | 383 | 53 / 56 | 3 / 3 | 1 / 1 | 0 / 0 | **0 / 0** | 34 |
| `scripts` | 5 | 15 | 4 / 5 | 0 / 0 | 0 / 0 | 0 / 0 | **0 / 0** | 0 |
| `ai` | 3 | 66 | 22 / 27 | 25 / 24 | 24 / 23 | 0 / 0 | **0 / 0** | 0 |
| **Total** | **686** | **4,388** | **479 / 625** | **63 / 64** | **35 / 33** | **0 / 0** | **0 / 0** | **199** |

Up to the factory exit the suite had 677 cases and 4,307 quantities (`economy` 25 cases, 81 quantities); the columns up to "after round 2" count against this set.

**Added later:**

- **Nine cases `economy/production exit <Gebäude>`** (command center, barracks, war factory of the three factions; 81 quantities: "order until outside the footprint", "order until all rest",
  rest distance, distances between the rest places; §42.4) – all bit-identical to 30 Hz at 60 and 120 Hz.
- **The paradrop is measurable** (five quantities of the group `flight` were unmeasured at every rate because the drop set nobody down – a 30 Hz bug, fixed in `09b4d21`).
- **Diagnosis instead of cases:** `SimTool rate-compare --ai-diff A,B`, `--reference-diff` and `--map-diff A,B --map KARTE` (each with `--fields`) compare two rates of a whole skirmish field by field
  at the end of every frame and name the first deviating frame (`docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md` §2, `…-ROUND3-OBSERVERS.md` §8, `…-ROUND4-FIRST-FRAME.md` §8). They are a tool, not cases
  of the suite, and have no tolerance.
- **Three-rate tests per package** (`Tests.RateInvariance.*.cs`: projectiles, physics, time offset, ground, flight, turrets, consolidation, observers, round 3 flight/deploy, exits, round 4) name their
  rates themselves and mostly require bit-identity at every frame boundary.

**Eleven corrections of the measurement** (never of a tolerance, never of a case): `a6fdfea` (healing and detector read the last instead of the first step of a frame), `fbe8d9a`/`466d85f` (speed sampled at the
end of the frame), `be4d800` (Battle Bus through damage instead of `KillCommand`), `1f87669` and `2f62a6b` (two chained `StepUntil` lost one tick instead of one frame; `2f62a6b` thereby raises the
30 Hz values of the "return" by one frame, 2.9333 → 2.9667 s), `f791682` (turret angle read at the end of the frame), `234f3c3` and `3a8d256` (commands and course of the deployment case at frame boundaries), `43443f5`
(moving victim read at the end of the frame), `f55c16d` (vision as a state of the end of the frame), `b99bd9c` (a drive ends when the object stands). Common denominator: the suite reads a state at the end
of the frame and counts an event in the first tick – like the frame convention (§43).

**What the suite still does not cover** (in addition to §39.5; from the packages):

- **Only the flat map.** No scene stages slopes, water, cliffs and bridges; the avalanche does not slide on the flat map (its recursion is verified only by a three-rate test on a slope),
  slopes are measured on the four maps of the manifest through `--map-diff`, not in the suite.
- **No case** for bridge scaffold, ferry, flood wave, guided bomb, free fall, `GarrisonContain`, battle plans, the mine as a drive of its own and the laser of the point defense; one three-rate test each
  or (laser) the skirmishes of the group `ai`.
- **The group `ai` is three pairings on a flat map over eight minutes.** That they are equal in every quantity says nothing about shipped maps: there 14 of 16 pairings agree in
  winner and frame of the victory, and 120 Hz is not measured (§43.9).
- **The suite commands at frame boundaries.** A command in the middle of a frame (a human) has no 30 Hz counterpart and no case.
- **199 quantities are unmeasured at every rate** (the event occurs within the budget at no rate); they count neither as passed nor as a failure.
- **"Passes" means:** every measured quantity lies at 60 and 120 Hz within the tolerance of its 30 Hz value – not that every skirmish is identical, and not that a human has seen a case.

Running and phase completion: `dotnet run -c Release --no-build --project tests/CnCNew.Simulation.Tests -- --content-dir game/content --only RateEquivalence --rate-report <datei>`; the
expectation is 0 / 0 in all ten groups.

## 40. Distribution comparison between rates (Phase 23, 7 October 2026): `ai-survey` and `rate-distribution`

A whole match is chaotic (§38.3, `docs/FIXED-POINT-Q39-24.md` §7.2): one rounding in the first fight is enough for a different winner. Two tick rates therefore cannot be compared run by run,
but only as **distributions** over many seeds (`docs/handoffs/HANDOFF-PHASE23.md` item 4). This section describes the tool for that. It is pure tool code
(`tools/CnCNew.SimTool/AiSurvey.cs`, `RateDistribution.cs`); it changes nothing in the simulation or in `baselines/`. **The survey itself (640 matches) is not contained here** – the section lays down
the method and the limits before any numbers exist.

### 40.1 Commands

```powershell
dotnet build -c Release tools/CnCNew.SimTool
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- ai-survey game/content --out <30.tsv> --tick-rate 30 [--seeds 20] [--first-seed 1] [--minutes 25] [--pairings all|<id>,<id>] [--jobs N]
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- ai-survey game/content --out <60.tsv> --tick-rate 60
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- rate-distribution <30.tsv> <60.tsv> [--out <bericht.md>]
```

- **`ai-survey`** plays each of the 16 AI pairings of the reference manifest (§37.2; the table comes from `ReferenceManifest.AiPairingRuns()`, the same source as the manifest) once per seed, **as a recording
  only** (no replay, no continuation from a snapshot). The seed acts exactly as with `ai-match --seed N`: `AiSeed = N`, `WorldSeed = N ^ 0xC0FFEE06`. The budget is **game time**:
  `MaxTicks = minutes × 60 × rate` (25 minutes = 45,000 ticks at 30 Hz = the budget of the manifest, 90,000 ticks at 60 Hz). `--jobs` works like `reference-manifest check`: every worker builds its own
  catalogs for the rate, nothing of the simulation is shared between threads, all workers must reach the same world fingerprint.
- **Output file** (TSV, LF, one header line, sorted by pairing and seed, no timestamps, no wall time – two identical invocations yield the same bytes, independent of `--jobs`):
  `pairing`, `map`, `factionA`, `factionB`, `seed`, `tickRate`, `outcome` (`winner`/`stalemate`, as the session reports it; `failed` if a match throws an exception – then exit code 1),
  `winnerFaction`, `winnerSlot` (1 = first-named faction, 2 = second, 0 = none), `endTick` (tick of the win, without a winner the tick of the abort), `endGameMilliseconds`
  (`endTick × 1000 / tickRate`, integer), `recordedTicks` (ticks played including the run-on time after the win; the state of `finalHash`), `commands` (number of recorded commands),
  `finalHash` (state hash of the world at the end of the recording), `firstBarracksTickA/B` (first tick after which the side owns a finished building of the kind `FS_BARRACKS`, −1 = never),
  `firstAttackTickA/B` (first tick after which `SkirmishAiStats.Waves` of the side is above 0 – the first attack wave of the AI, −1 = never). The four milestones are only read after each tick.
  Progress goes to stderr, each finished line with its run time (an aborted run leaves its finished matches in the log).
- **`rate-distribution`** reads two surveys (reference, candidate), **rejects them** if the sets of pairing and seed, the maps or the factions differ or a file contains a
  failed match, and prints a report (Markdown) with statistic, limit and verdict per test. Exit code 0: no test failed; 1: at least one failed; 2: input error.
  A **not testable** line (see e) is neither passed nor failed; the report counts it separately, and it does not turn the exit code into 1.

### 40.2 The limits (laid down beforehand, not fitted to data)

Every family of tests is two-sided at the level α = 0.05. Within a family of k tests Bonferroni applies (α/k per test), because it holds under arbitrary dependence. With n matches per rate,
x decided or won matches respectively, and the indices 30/60 for reference and candidate:

| | Quantity | Test | Limit |
|---|---|---|---|
| a | Share of decided matches, all pairings together | two-proportion z-test with pooled variance: p̂ = (x₃₀ + x₆₀)/(n₃₀ + n₆₀), SE = √(p̂(1 − p̂)(1/n₃₀ + 1/n₆₀)), z = (p₆₀ − p₃₀)/SE | passed if \|z\| ≤ 1.960 = Φ⁻¹(0.975) |
| b | Win rate **per faction** (wins of the faction / matches in which it takes part) | the same z-test per faction, Bonferroni over the k = 5 factions | passed if every \|z\| ≤ Φ⁻¹(1 − 0.05/(2·5)) = 2.5758 |
| c | Duration of decided matches in game minutes, **stratified by pairing** | over the m pairings with at least two decided matches at both rates: dᵢ = x̄₆₀,ᵢ − x̄₃₀,ᵢ, vᵢ = s²₃₀,ᵢ/n₃₀,ᵢ + s²₆₀,ᵢ/n₆₀,ᵢ, D = Σdᵢ/m, SE = √(Σvᵢ)/m | passed if \|D\| ≤ 1.960 · SE |
| d | Winner distribution per pairing (A wins / B wins / undecided) | exact test of the 2 × 3 table after Fisher, Freeman and Halton: sum of the probabilities of all tables with the same marginal totals that are at most as probable as the observed one (complete enumeration); Bonferroni over 16 pairings | passed if p ≥ 0.05/16 = 0.003125 |
| e | Duration of decided matches per pairing | Welch test: t = (x̄₆₀ − x̄₃₀)/√(v₃₀ + v₆₀) with v = s²/n, degrees of freedom ν = (v₃₀ + v₆₀)²/(v₃₀²/(n₃₀ − 1) + v₆₀²/(n₆₀ − 1)), two-sided p = I_{ν/(ν + t²)}(ν/2, 1/2); Bonferroni over 16 pairings | passed if p ≥ 0.003125; with fewer than two decided matches at one rate **not testable** |

Reasons and details:

- **Why b per faction:** "first-named" and "second-named" side mean nothing across pairings. Base factions occur in 8 pairings (160 matches per rate), the two generals in 4 (80).
  The tests of two factions that play against each other are dependent (the win of the one is the defeat of the other); Bonferroni needs no independence.
- **Why c stratified:** the pairings have very different durations (in the manifest the wins lie between tick 7,815 and 42,840, that is between a good 4 and just under 24 game minutes); a Welch test on the lumped-together samples would count the spread between pairings as noise
  and misread a shift of the share of decided matches per pairing as a difference in duration. The report additionally prints the ratio of the pooled means, for orientation only.
- **Family size:** the report divides by the number of pairings or factions **in the file** – 16 and 5 for the full survey. For e all pairings count, including the not testable ones (the number
  therefore does not depend on the result). A partial run (`--pairings`) accordingly has a weaker correction; only the full survey is authoritative.
- **Without variance:** if all matches of both surveys end the same way, the difference is 0 and z = 0 (passed); two samples without spread pass e with equal means and fail otherwise.
- **Numerics** (`SurveyStatistics`, `double`, invariant culture): Φ via the series Φ(x) = ½ + φ(x)·Σ x^(2k+1)/(1·3·…·(2k+1)) below 3 and the continued fraction Q(x) = φ(x)/(x + 1/(x + 2/(x + …))) above it; the
  quantile by bisection (no table); ln Γ via the Stirling series after shifting the argument above 15; the regularized incomplete beta function via its continued fraction (modified
  Lentz method) on the rapidly converging side; the tables of d via logarithms of the factorials.

**What a failure means.** If both rates were distributed identically, each of the five families would raise a false alarm with a probability of at most 5 %; at most 0.05 false lines are expected among the 5
of b, 0.05 among the 16 of d and 0.05 among the 16 of e. Over the whole report that is at most 0.25 expected false alarms and at most a 25 % probability of at least one (22.6 % with
independent families) – the families are **not** corrected against each other. A single failed line is therefore weak evidence, a failed family with the same direction in
another one strong evidence. The exact test d is discrete and stricter than its level: with 20 matches per rate and a true win rate of 0.5 it rejects per pairing with probability 0.0011 instead of 0.0031.

### 40.3 What the comparison can see: smallest detectable differences (power 80 %)

Calculated (`rate-distribution` prints the table for every survey; the values here apply to the plan 16 pairings × 20 seeds = 320 matches per rate):

| Test | n per rate | Level | Baseline value | smallest detectable change |
|---|---|---|---|---|
| a share decided | 320 | 0.05 | 0.50 (largest spread) | ±0.110 (11 percentage points) |
| b win rate of a base faction | 160 | 0.01 | 0.50 | ±0.187 |
| b win rate of a general | 80 | 0.01 | 0.50 | ±0.259 |
| c duration, stratified | 16 × 20 decided | 0.05 | – | 2.802 · SE; with the same standard deviation σ in all pairings 0.221 σ |
| d winner distribution of a pairing | 20 | 0.003125 | 0.00 → | 0.52 |
| | | | 0.10 → | 0.69 |
| | | | 0.25 → | 0.86 |
| | | | 0.50 → | **not reachable** (even 0.50 → 1.00 is found with only 75 %) |
| e duration of a pairing | 20 / 10 / 5 decided | 0.003125 | – | 1.27 σ / 1.91 σ / 3.20 σ |

- a and b: normal approximation of the z-test, solved for the change δ (bisection): Φ((δ − z_{1−α/2}·SE₀)/SE₁) ≥ 0.8 with SE₀ under the null hypothesis (pooled) and SE₁ under the alternative.
- d: **exact** power by enumerating all 21 × 21 outcome pairs of two binomial distributions (two categories; a third one only spreads the counts further), search in steps of 0.005;
  downward the same values apply mirrored (1.00 → 0.48 etc.).
- e: (t_{1−α/2; 2n−2} + t_{0.8; 2n−2}) · √(2/n) in standard deviations of the match duration within the pairing (usual approximation of the noncentral distribution); the report converts them into minutes with the pooled
  standard deviation of the reference. c: (1.960 + 0.842) · SE with the SE of the data.

**Honest reading:** In aggregate the comparison sees a shift of the share of decided matches from about 11 percentage points and of the win rate of a base faction from about 19. **Per pairing it sees
only coarse flips with 20 seeds** (an almost certain side becomes a coin toss or vice versa) and differences in duration above one standard deviation. A passed comparison means "no difference
of at least this size found", not "the rates are equal".

### 40.4 Limits of the method

- Seeds are paired by number only: the same seed is not the same match at two rates. The tests treat both surveys as independent samples.
- Undecided matches have no duration. If long matches slip beyond the budget, that shows in a and d, not in c and e; the duration tests are then biased in favor of shorter matches.
- The matches of a pairing share map and opening (the first barracks construction falls on almost the same tick across seeds); the spread between seeds stems from the fighting. The survey measures
  the distribution over **these** seeds and the four maps, not "the game".
- The table has only five factions and four maps; a deviation that only hits other factions or large maps stays invisible. Balance is not assessed, only the difference between
  two rates.
- The milestone columns (barracks, first attack wave) are collected, but **not tested** by `rate-distribution`; they serve the search for causes when a test fails.
- The campaign part of item 4 of the brief (objective driver: victory/defeat per map, time of the final script) is not part of these tools.

### 40.5 Tests and measured sample

`tests/CnCNew.Simulation.Tests` includes `RateDistribution.cs` as source text (`Tests.RateSurvey.cs`, four tests, `-- --only "Rate survey"`): normal distribution against known values and against the
Simpson integral of the density; ln Γ, beta function and t distribution against closed forms (I_x(1,b), I_x(a,1), I_x(2,3) = 6x² − 8x³ + 3x⁴, I_x(½,½) = (2/π)·asin √x; t with 1, 2 and 4 degrees of freedom);
the exact test against tables counted out by hand (2 × 3 with probabilities 1/6, 1/3, 1/3, 1/6; Fisher 2 × 2 with tie 7462/C(24,12)); Welch by hand; round trip of the file, rejection
of deviating sets (missing seed, other seeds, other map, mixed rate, duplicate line, failed match) and the verdicts of a report on two small synthetic surveys.

Sample on 7 October 2026 (Release, 2 pairings × 2 seeds, **not a survey**): two invocations with different `--jobs` yield byte-identical files at 30 Hz and at 60 Hz respectively;
`AlpineAssault_China-GLA` with seed 1 ends in the survey as with `ai-match … --max-ticks 45000 --seed 1` (winner FactionGLA at tick 9,930, 1,794 commands, 10,231 ticks, final hash `BDC0218A0BA5EE96`).
Run time of a match over the full budget (one worker, `ArmoredFury_America-China`, stalemate): 13.2 s at 30 Hz and 24.5 s at 60 Hz; decided matches
(`DeathValley_America-China`, seeds 2 and 3): 6.0 s and 3.5 s at 30 Hz (15,076 and 10,261 ticks), 14.3 s and 7.1 s at 60 Hz (49,351 and 27,571 ticks). On top of that comes, once per worker, the catalog build
(about 12 to 16 s, one after another under a process-wide lock). Not measured on a quiet machine, for planning only.

### 40.6 The surveys of Phase 23 (7 to 9 October 2026)

Per rate 320 matches (16 pairings × 20 seeds, 25 game minutes), `rate-distribution <30.tsv> <60.tsv>`; files and reports in the scratchpad of the session (`survey\r0-…` to `r3-…`). The limits from
§40.2 are unchanged. "r0" to "r2" compare against the same 30 Hz survey (at 30 Hz nothing had changed until then: the 30 Hz file after round 1 and after round 2 is byte for byte the
one of r0); "r3" needed a new 30 Hz reference, because factory exit and AI corrections changed the behaviour at 30 Hz.

| Survey | State (60 Hz) | decided 30 Hz | decided 60 Hz | a: z (limit 1.960) | b: win rate per faction, difference 60 − 30 Hz (limit \|z\| ≤ 2.576) | c: duration D, SE (min) | pooled mean duration 30 / 60 Hz (min) | Tests |
|---|---|---:|---:|---|---|---|---|---|
| r0 | before any correction (`36d6b1c`) | 253 | 224 | **−2.631, failed** | America −0.100 (z −2.03), ChinaTank −0.075 (z −1.96); all passed | −0.404, 0.407 | 8.402 / 7.831 | 38 passed, 1 failed |
| r1 | after round 1 (`62fbb6a`) | 253 | 244 | −0.854 | America −0.044 (z −0.92), AirForce −0.025, China −0.019, ChinaTank −0.013, GLA +0.025 | −0.103, 0.366 | 8.402 / 8.002 | 39 passed |
| r2 | after round 2 (`3b44506`) | 253 | 245 | −0.761 | America −0.044 (z −0.92), AirForce +0.013, China −0.038, ChinaTank +0.013, GLA +0.019 | −0.114, 0.387 | 8.402 / 8.136 | 39 passed |
| r3 | after the AI corrections (`cca6f92`) | 305 | 301 | −0.705 | America −0.038 (z −0.88), AirForce −0.013, China +0.025, ChinaTank +0.013, GLA −0.013 | −0.009, 0.286 | 9.328 / 9.227 | 39 passed |

**The same seed at 30 and 60 Hz** (not part of `rate-distribution`; a separate evaluation of the two TSV files: same outcome and winner, end at most one second apart):

| State | the same outcome and winner | in addition end within one second | Source |
|---|---:|---:|---|
| after the AI corrections (`cca6f92`, r3) | 297 of 320 (according to `docs/AI-OBSERVED-2026-10-08.md` §11.4) or 295 (according to round 4) | **188** (working notes of the session) or 192 (round 4's own evaluation) | two evaluations of the same state, not reconciled |
| after round 4 (`1b82e45`) | **317 of 320** | **287 of 320**; end time equal 285, same number of commands 282 | `docs/rate-invariance/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md` §1.3 |

Reading: the distribution comparison already passed after round 1; that it passes means "no difference of the size named in §40.3 found". The seed comparison is the sharper statement and
shows the rest: 33 of 320 matches end at 60 Hz more than one second away from the 30 Hz match, 3 with a different outcome. **Not measured:** the verdict of `rate-distribution` after round 4 and
at the switched-over state; 120 Hz in every survey. **Balance is not assessed** – in the 30 Hz survey r3 ChinaTankGeneral wins 70 of 80 matches against AirForce (9 of 80); before the
AI corrections AirForce was ahead (§42.6).

## 41. The escape "original grid" (German: Ausweg „Originalraster“)

Stipulation from `docs/handoffs/HANDOFF-PHASE23.md`: logic that the original executes once per logic frame (1/30 s) and that cannot sensibly be converted to another step size may run on every
(rate/30)-th tick – on the ticks for which `SimulationTime.IsOriginalFrameBoundary(tick)` holds. For movement, command acceptance and target acquisition the escape is not permitted. Every place is listed
in this table; at 30 Hz each of them is unchanged (every tick begins a frame).

| Place | What runs per original frame | Why not convertible | Source in the original |
|---|---|---|---|
| `Scripting/ScriptEngine.cs`, `ScriptEngine.Update` (`BeginsFrame`) – the script VM as a whole | end-game timer, timers (count frames), the evaluation of all scripts, team events, sequential scripts with their wait times, team build orders and queues, the standing commands of the VM (`UpdateFollowers`, `UpdateButtonHunts`, `UpdatePhase13`) | maps compare timers with frame literals, do arithmetic on timers, count their own evaluations and query edges "since the previous evaluation"; "one command per update" and "idle for two updates" are rules of the original in frames. Evidence: `MD_USA02` ran 250 s later at 60 Hz (`docs/SCRIPT-ENGINE.md` §2) | `GameLogic.cpp`, `GameLogic::update` (`TheScriptEngine->UPDATE()` once per frame); `ScriptEngine.cpp`, `ScriptEngine::update`, `setTimer`, `setSequentialTimer`, `evaluateAndProgressAllSequentialScripts`, `startEndGameTimer`/`startQuickEndGameTimer`; `Team.cpp`, `Team::updateState`; `ScriptConditions.cpp`, `evaluateUnitHasEmptied` |
| the same place under `FREEZE_TIME` | as above; because the tick of the world stands still, the VM counts the calls itself (`frozenPhase`) | as above; in the original, script timers keep running while time is frozen, one update per frame | `GameLogic::update` (return after the scripts on `isTimeFrozen`), `ScriptEngine::isTimeFrozenScript` |

What **deliberately stays in every tick** at the same place: `ScriptEngine.Observe` (the events of every world step), the remembering of named objects that existed (`NoteNamedObjectsThatExist`), the
information the VM gives to the target acquisition of the world and the acceptance, in the world step, of the commands issued by the VM. The place is measured over the 24 campaign maps at 30, 60 and 120 Hz
(`docs/TICKRATE-MIGRATION.md` §15.3).

Places outside the simulation that have been running per original frame since Phase 21 already (effects, messages, standing orders of the input) are listed in `docs/TICKRATE-MIGRATION.md` §14; they are presentation and
not a case of this table.

### 41.1 The complete list (as of 9 October 2026, all packages of Phase 23)

The table above names the script VM. The packages of the phase used the escape in further places and employed it in two forms: on the **grid of the world** (ticks with
`IsOriginalFrameBoundary`, the form of the stipulation) and on the **frame of the object** (whole frames after the tick in which the object came into being, received a command or began a state –
`EntityFrames.BeginsFrameOf`). The second form holds nobody up until the next frame boundary of the world; but it is just as much "once per original frame" and is therefore listed here. Every row is unchanged at
30 Hz (every tick begins a frame). Package documents: `docs/rate-invariance/RATE-INVARIANCE-<PAKET>.md`; "OBSERVERS", "R3-FLIGHT" and "ROUND4" are `…-ROUND3-OBSERVERS.md`, `…-ROUND3-FLIGHT.md`,
`…-ROUND4-FIRST-FRAME.md`.

**A. Closest to the prohibition "not for target acquisition" – the confirmation of the project owner is pending** (`docs/TICKRATE-MIGRATION.md` §15.7.7, decision 1). The pass or the module respectively keeps running
in every tick, commands are accepted in every tick, and a commanded shot is fired in the tick of the command; **but what a unit decides of its own accord, it now decides only
once per frame.**

| Place | What runs once per original frame, and on whose frame | Why not subdividable | Source in the original | Package | Commit |
|---|---|---|---|---|---|
| `SimulationWorld.StepAcquisition` | the independent decision of a unit – look, attack, pursue, halt, march on –, in the first tick of the frame **of the unit** (tick of its appearance) | if the pass reads everything at the frame end, its inputs are the same in all ticks of a frame; a shot in the second tick is a different game (different victim, different reload). Cost: a unit freed by a command in the middle of the frame searches up to r − 1 ticks later | – (package names no place) | OBSERVERS §2 | `f6dacdb` |
| `PointDefenseLaserUpdate.Update` | scan and shot in the first tick of the frame **of the carrier**, carrier and targets at the frame end; after an empty scan the shot pause rests for the remainder of the frame (`_quietFrame`) | per tick the laser saw projectiles at their momentary position and fired one frame earlier at 60 Hz; a frame without a target does not count the pause down at 30 Hz | – | `docs/AI-OBSERVED-2026-10-08.md` §11.3, ROUND4 | `3c24352`, `887b51e` |
| `CleanupHazardUpdate.Update` | scan, loss of target, approach command and shot, in the frame **of the owner** | state machine with one step per frame; decided per tick, its counter leaves the grid | – | ROUND4 | `7880aed` |
| `DeployStyleAIUpdate.AllowsFire` | in the frame of becoming ready, fire attempts that come **before** the update of the unit are discarded (r − 1 ticks); the unit's own target acquisition after the update gets through | at 30 Hz the command of the same tick is discarded and only that of the next frame is accepted; "one step later" is one frame | – | R3-FLIGHT §3 | `c1a57ee` |

**B. State machines and decisions "one step per frame".**

| Place | What runs once per original frame, and on whose frame | Why not subdividable | Source in the original | Package | Commit |
|---|---|---|---|---|---|
| `ScriptEngine.Update` (also under `FREEZE_TIME`) | the script VM as a whole, grid of the **world** | table above | table above | `docs/SCRIPT-ENGINE.md` §2 | `727c4d6`, `2fe8b32`, `d710835` |
| `AIUpdateInterface.Update` → `DockMachine.Update` | the state changes of docking (approach finished, clearance, entry, exit), grid of the **world**; the work of the dock runs per tick | every change costs one update; that can only be counted | `AIDockMachine` | GROUND | `096160d` |
| `SupplyCarrierAI.Update` → `SupplyMachine.Update` | idle → wants to deliver → dock command, grid of the **world** | as above | `SupplyTruckStateMachine` | GROUND | `096160d` |
| `DockMachine.ActionDelay` | the action of a supply dock on a docker without wait time, from entering onward | "at the next update" is one frame (`ZeroIsNextFrame`) | – | ROUND4 | `d9b5490` |
| `AssaultTransportAIUpdate.Update` | member list, look after its own shot, commands to the troops, frame **of the transport** | "in the next update" is one frame | – | ROUND4 | `015e975` |
| `AIUpdateInterface.UpdateTask` (three checks), `UpdateGoal` | "is the task still worthwhile" (target gone, construction finished or foreign, nothing to repair), range of the build order and tracking toward a target object, frame **of the builder** | what is checked is the situation that the 30 Hz run checks too; the work itself stays per tick | – | GROUND, OBSERVERS | `6e018fe`, `3d90f25` |
| `JetAIUpdate.Update`, parked aircraft without an order | the idle look (report healing, execute stored command, return time), frame **of the aircraft**; a command ends the idle frame immediately | state machine, one step per frame | – | R3-FLIGHT §8 | `cc7fd75` |
| `ContainChainModules.EnterPayload` | a carrier's check whether its initial crew has appeared, frame **of the carrier** | state change "one update after the appearance" | – | CONSOLIDATION | `f8ce7c4` |
| `SimulationWorld.StepTurret` (`OccupyFrame`) | a turret step without rotation (setting the scan deadline, change of side of a sweeping turret) occupies one frame **of the turret** | one step of the turret is one frame | `TurretAI::friend_turnTowardsAngle` | TURRETS, ROUND4 | `276a8de`, `43acce8` |
| `StealthDetectorUpdate.Update` with `DetectionRate = 0` | one scan per frame instead of per tick (lower bound `AtLeastOneFrame`; retail has no such detector) | – | `StealthDetectorUpdate.cpp`, `update` (`markAsDetected(m_updateRate + 1)`) | TIMING | `f922979` |

**C. Sampling and collision** (a denser sampling finds overlaps earlier and in a different order).

| Place | What runs once per original frame, and on whose frame | Why not subdividable | Source in the original | Package | Commit |
|---|---|---|---|---|---|
| `AIUpdateInterface.AdvanceFrame` → `ResolveFootprintCollisions`, `TryTopple`, `TrySquish` | the check for overlap (squishing, toppling, `ICollideModule`), in the first tick of the frame **of the mover**, which stands at the frame end while doing so (until round 4: in the last tick) | at 30 Hz the check happens after every whole step | – | GROUND, ROUND4 | `1c65446`, `e910174` |
| `MinefieldBehavior.OnCollide` | the comparison "has the unit moved by `RepeatDetonateMoveThresh` since the last detonation", grid **of the reporter** (of the mover) | the threshold stands against the distance since the last comparison; a finer cadence detonates faster | – | TIMING, CONSOLIDATION | `eca983f`, `0136733` |
| `RailroadBehavior.Collide` | collision response of the train (killing, crush damage, pushing away by a quarter of the overlap), frame **of the locomotive** | damage and push are amounts per frame; the quarter decays geometrically | – | PHYSICS | `9871bc2` |
| `RailedTransportDockUpdate` | the arrival question (6 or 3 units respectively), from the start of pulling or pushing respectively | the original asks once per frame | – | PHYSICS | `b53cbd3` |
| `WaveGuideUpdate.DoDamage`, end of path | sampling of the front, end of the path, frame **of the wave** | a denser sampling hits objects that a coarser one passes by | – | PHYSICS | `c333364` |
| `HeightDieUpdate.Update` | height check and "is moving downward", frame **of the object** (tick of creation), read at the frame end | compares with the height of the previous frame; per tick the object dies at a height the original never sees | – | PHYSICS, CONSOLIDATION | `5440888`, `ad2b9e9`, `f0770fe` |

**D. Healing in the slice of the original** (slices per tick are rounded individually and do not add up to the frame slice; a hit in the first tick of a frame would hit a different health).

| Place | What runs once per original frame, and on whose frame | Package | Commit |
|---|---|---|---|
| `HealContain.Update`, `GarrisonContain.HealOne`, `RepairDockUpdate.Action` | the healing slice, counted from entry or first action respectively | PHYSICS | `b05da71` |
| `TunnelContain.Update` | the healing slice of an occupant, counted from its entry | FLIGHT | `81931ab` |
| `PropagandaTowerBehavior.Give` | the healing slice, frame **of the tower** (bonus and scan stay per tick) | CONSOLIDATION | `f05af4f` |
| `AIUpdateInterface.UpdateTask` (repair) | the healing slice, frame **of the builder** | OBSERVERS | `64f438f` |
| `RebuildHoleBehavior.Update` | the regeneration, frame **of the hole** | OBSERVERS | `2a433ba` |
| `TransportContain` (regeneration of the occupants) | the slice of one second over 30 frames, counted from boarding | `docs/AI-OBSERVED-2026-10-08.md` §11.3 | `df639ff` |

**E. Random draws, events and statistics** (a random draw is not divisible; the number of draws per game time must be equal).

| Place | What runs once per original frame, and on whose frame | Package | Commit |
|---|---|---|---|
| `SlavedUpdate.DoRepairLogic` | height random draw and move command of a drone in the repair state, grid of the **world** | TURRETS | `01121cc`, `e1252df` |
| `WanderAIUpdate.Update` | the look of an idle wanderer (two draws, one command), grid of the **world** | TURRETS | `022bec8` |
| `GrantStealthBehavior.Update` | growth of the radius and granting of the stealth, frame **of the marker** | TURRETS | `f1c5ceb` |
| `SpecialAbilityUpdate.StepCaptureFlash` | the flash phase of the capture, frame **of the preparation** (`SpecialAbilityUpdate::continuePreparation`) | TURRETS | `d923cea` |
| `EmpUpdate.Update` | the growth fraction of the EMP measure, frame **of the pulse** (no gameplay effect) | PROJECTILES | `9132127` |
| `SimulationWorld.NoteTurretWait` | the incrementing of `TurretWaits` (statistics, part of the hash), grid of the **world** | TURRETS | `dc888d3` |

**Not an escape, but the agreed subdivision** (decision per frame, progress in every tick – §43): ground movement, winged flight and the states of the jet, turrets, projectiles and missiles,
toppling, train, avalanche, Battle Bus, collapse, bridge scaffold, parachute, crash, Chinook, Spectre (its frame step lies on the grid of the world, because its howitzer cadence `tick % HowitzerFiringRate`
lies there), dynamic shroud reveal. Likewise not an escape: vision (runs unchanged every three frames, since `a8eed4a` after the **last** tick of its frame) and the readers that merely read a different
position (§43.4).

## 42. Factory exit (8 October 2026): exit path of freshly built units

**The commit for this section changes the simulation at 30 Hz** (every run with production gets new hashes). The reference manifest and the input manifest are deliberately **not** rewritten in this
commit; that happens in a separate commit with this cause. The fix was written on 7 October against an older state and
held back; here it stands on the state after Phase 23 (`docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md`) and follows its frame convention. **No human has seen any of this.**

### 42.1 Finding and yardstick

`ProductionUpdate.Update` put the finished unit on the `UnitCreatePoint` – inside the building – and gave it no path. It stayed standing there, three units built one after another
stood inside each other; only a rally point set by the player moved them (`docs/DEMO-SCENARIOS.md`, section "Factory exits", `docs/DEMO-FINDINGS.md` no. 34).

The original (read, not copied): `DefaultProductionExitUpdate.cpp` and `QueueProductionExitUpdate.cpp`, `exitObjectViaDoor` – the unit is put on the `UnitCreatePoint` rotated into the building
and gets a path with `aiFollowExitProductionPath` (`AI_FOLLOW_EXIT_PRODUCTION_PATH`, `AIStates.cpp`, `AIFollowPathState`): first
`getNaturalRallyPoint(…, offset = TRUE)` – the `NaturalRallyPoint` of the data, pushed outwards by `2 * PATHFIND_CELL_SIZE_F` (20) in its own direction –, then, if
set, the rally point of the player, which `exitObjectViaDoor` adjusts for the unit beforehand with `Pathfinder::adjustDestination`. The unit's own factory is no obstacle on this
path (`ignoreObstacle`). Idle allies on the path are sent away (`Pathfinder::moveAllies`, `AIUpdateInterface::aiMoveAwayFromUnit`).
`SupplyCenterProductionExitUpdate.cpp` drives via the unshifted `NaturalRallyPoint` and sends the truck to work immediately.

### 42.2 Behaviour

| Step | Behaviour | Source / own decision |
| --- | --- | --- |
| Exit point | `NaturalRallyPoint` (newly typed: `ProductionExitData.NaturalRallyPoint`), pushed outwards by two pathfinding cells (20) in its own direction, rotated into the building (`ProductionExitBase.TryGetExitPath`). A module without a `NaturalRallyPoint` (0, 0, 0) has no exit path | `getNaturalRallyPoint(…, offset = TRUE)`; a point that does not lie on the x axis also moves sideways in the process, as in the original |
| Door point | the same point, only one cell (10) outwards; the path of the unit leads via it first (`AIUpdateInterface.OrderExitProduction`: intermediate point of the path, no stop; `_viaPending`) | **own decision:** this way the unit leaves the building on the door side, even if its stopping place lies to the side. The pathfinding leaves the footprint in which the unit stands by itself (replacement for `ignoreObstacle`). If the door point is not reachable (built over), the unit drives directly to the goal |
| Making room | If an idle own unit stands on the exit point, it is sent to the nearest free place beside it, the new unit takes the point (`SimulationWorld.ClaimExitPoint`) | reduced after `Pathfinder::moveAllies` / `aiMoveAwayFromUnit` (the original clears idle allies away along the whole path) |
| Free place | `IWorldServices.FindFreeGoal`: places in rows across the direction of travel (0, +1, −1, … ±3 places, four rows; spacing = twice the bounding radius + 5, at least one cell); free means: cell walkable and not built on, no other movable object stands there or is on its way there. If everything is occupied, it stays with the goal itself | reduced after `Pathfinder::adjustDestination`; the numbers of rows and places are own values |
| Rally point | first the exit path, then – on a free place at the rally point – the rally point (`_exitRallyPending`) | `exitObjectViaDoor`: `adjustDestination` for the rally point |
| Another command during the exit | replaces the whole exit path including the door point and the second leg – every command, not only a move command (`AIUpdateInterface.EndExitPath`; §42.6) | the exit path is a state of the AI of the original (`AI_FOLLOW_EXIT_PRODUCTION_PATH`), a new command replaces it |
| Supply depot | unchanged: the truck is taken over by the supply AI immediately (`ForceSupplyWanting`), without an exit path | deviation, only reported |

### 42.3 Frame convention (30 / 60 / 120 Hz)

At 30 Hz the spawn phase of the tick in which the unit comes into being chooses the place and sends whoever stands there aside; in doing so it sees every object after its whole step
of this frame. The unit and those sent away move with the next tick. Above 30 Hz, `docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md` §1.3 applies:

| What | at 30 Hz | above 30 Hz (r ticks per frame) | Rule |
| --- | --- | --- | --- |
| The unit comes into being | end of tick T | end of the **first** tick of the frame of the factory (`ProductionUpdate.Enqueue` counts the frame of the order in full, package TIMING) | 1, 7 |
| Choice of place and "making room" | spawn phase of T | spawn phase of the **last** tick of this frame (`SimulationWorld.ProcessExitClaims`, list `_exitClaims`): only then has every object in phase covered its step. Until then the command stands with the exit point itself; a unit that has received another command in the meantime or is no longer alive keeps what it has. Units that came into being in the same tick **after** the choosing one do not exist for it (at 30 Hz they come into being only after its choice) | 3, 5 |
| First step of the unit and of those sent away | tick T + 1 | first tick of the next frame (`DeferSpawnPhaseOrders` for the unit; those sent away are commanded in the last tick of the frame and run in the next tick) | 7 |
| Passing the door point, new path plan | decision per tick | decision in the first tick of the frame of the unit (part of `Move`, which above 30 Hz decides once per frame, package GROUND) | 1 |
| Second leg to the rally point | in the update that ends the first | in the **last** tick of the frame of the unit: there an arrival ends by itself (`BeginArrival`); a first leg that ends in the first tick (no path out) waits for that (`EntityFrames.BeginsFrameOf` of the following tick) | 2, 7 |

The escape "original grid" (§41) is not used: nothing runs on the grid of the world, the choice of place hangs on the frame of the unit (tick of its coming into being).

**State:** `exitRallyX/Y` and `viaX/Y` of the AI, `doorX/Y` and `exitX/Y` of a waiting spawn, above 30 Hz the list `exitClaims` of the world (unit, tick) – all written only
when present; no version bumps (`WorldSnapshot` 10, Replay 12, `CommandTypes.Version` 15). The list of the world stands in the header of the state and is hashed in full in every tick;
`RetargetExit` and `OrderMove` report the unit with `MarkDirty`.

**Input manifest:** exactly one new field, `ProductionExitData.NaturalRallyPoint` (previously typed by the catalog, but not read by the module). `input-manifest check`:
`catalogs.templates` and `catalogs.whole` differ, everything else is the same.

**Cost:** no new pass per tick. Per tick of the world one comparison (`_exitClaims.Count`, at 30 Hz always 0). As long as a unit is exiting, per decision of its movement
(per frame) a distance comparison to the door point runs, and per update a query of `_exitRallyPending`; the path plan of the exit is two path searches instead of one (once per plan). Per **built
unit** once: one pass over the movable objects (`GoalIsFree`); only if the exit point is occupied, a second one and up to 27 places with one pass each
(`FindFreeGoal`), the same once at the arrival with a rally point. Not measured on a quiet machine (no comparison run of two binary directories).

### 42.4 Measured (8 October 2026)

**Scenes** (`scripts/audit-scenarios.ps1 -Scenario exits` and the three overall scenes `exit-<fraktion>-all`): before, 12 of 12 scenes red (0 of 27 units of the nine single scenes
leave their building), now 12 of 12 green at 30 Hz **and** with `-TickRate 60`; the three classic scenes pass at both rates. First frame outside the box after
appearing, the same at 30 and 60 Hz for every unit:

| | America | China | GLA |
| --- | --- | --- | --- |
| Command center | +93 / +93 / +93 | +93 / +94 / +94 | +52 / +52 / +52 |
| Barracks | +86 / +86 / +86 | +47 / +48 / +48 | +66 / +66 / +66 |
| War factory / arms dealer | +64 / +64 / +64 | +69 / +69 / +69 | +46 / +46 / +46 |

The last frame with movement and the resting place of each of the 27 units at 60 Hz are also those of 30 Hz (e.g. barracks America: resting places (75, 19), (75, −19), (75, 0)).

**Rate equivalence** (§39; new case group `economy/production exit <Gebäude>` for command center, barracks and war factory of the three factions: three orders, per unit "order
until outside the footprint", "order until all rest", resting distance to the factory, distances between the resting places; read at the frame end): 9 cases, 81 quantities, **all bit-identical at 60 and 120 Hz
to 30 Hz** (e.g. `AmericaCommandCenter`: 8.0667 / 13.0667 / 18.0667 s, all rest after 18.8000 s). Whole suite before → after, quantities outside the tolerance at 60 / 120 Hz:

| Group | before | after |
| --- | --- | --- |
| `locomotion` | 2 / 2 | 2 / 2 |
| `flight` | 0 / 3 | 0 / 3 |
| `weapons` | 0 / 0 | 0 / 0 |
| `projectiles` | 4 / 4 | 4 / 4 |
| `turrets` | 4 / 0 | 4 / 0 |
| `overtime` | 0 / 0 | 0 / 0 |
| `economy` (81 → 162 quantities) | 0 / 0 | 0 / 0 |
| `timed` | 1 / 1 | 1 / 1 |
| `scripts` | 0 / 0 | 0 / 0 |
| `ai` | 24 / 23 | 24 / 26 |
| Total (4,307 → 4,388 quantities) | 35 / 33 | 35 / 36 |

Outside `ai` the same set of 14 rows fails as before. In six existing cases `economy/produce …` "queue to both units standing" has changed (the
units now drive out, e.g. Ranger 9.9667 × 3 → 13.8333 / 13.8500 / 13.8583 s; the rest is the arrival in the last tick of the frame, within the tolerance). The group `ai`
(whole skirmishes, chaotic) is worse by three quantities at 120 Hz, with the follow-up commits from §42.6 at 27 / 26; not traced (the first frame with a deviating position
in the match America against China lies, with exit path and follow-up commits, still at 3543, 118.10 s, as on `main`: the whole build-up with all exits is, between 30 and 60 Hz,
bit-identical). No tolerance was touched.

**Tests:** `Simulation.Tests` green at 30 Hz and with `--tick-rate 60` and `120`; new `ConstructionExitPath` (appearing, exit, making room, rally point, continuation from the
snapshot), "Rate invariance, exits: …" (three units without and with a rally point: position, heading, command state and goal after each of 450 frames bit-identical at 30, 60 and 120 Hz, in
every tick `VerifyIncrementalHash`) and the snapshot test (a snapshot in every tick of the frame in which the unit waits for its place, and one in the middle of the exit, continues
with the same hash at 30, 60 and 120 Hz). Cross-check: with the choice of place in the tick of coming into being above 30 Hz too (the unchanged adoption of the old commit) both tests
fail (at 60 Hz, in frame 95 one unit has a remainder of its step at the frame end: the one sent away runs beside its phase). `Presentation.Tests` (without and with content), `Ui.Tests`, `Game.Tests` (Release, with content),
`check-dozer-build`, `check-build-hotkeys`, `check-spectator`: green; solution build 0 warnings.

**Reference manifest** (`reference-manifest check --set full`, not rewritten): **23 of 50 runs differ** – `reference-match`, all 16 AI runs, the four 8-player runs
(`ai8_FortressAvalanche`, `ai8_IronDragon`, `ai8_TwilightFlame`, `ai8_TwilightFlame_short`), `skirmish-sweep` and `mission_MD_USA05`; **27 are identical**: `stress` (the synthetic
factory has no `NaturalRallyPoint`) and the remaining 26 mission runs. `--set quick --verify-incremental`: 95,230 ticks, 0 deviations between incremental and full hash.
Reference match now: winner FactionGLA at **tick 14,130** (before 17,805), final hash `5BF9BB7CED6D7A35`, chain `56149C0105E992D7`, second run and continuation from the snapshot identical
(26,626 commands instead of 33,648, 13,589 shots of the scenario instead of 15,940).

**Result quantities instead of hashes** (`reference-manifest results`, before = binaries of `main`):

- 27 mission runs: all result quantities the same (outcome, shot counts, event counts) – also `mission_MD_USA05`, whose hash changes. `stress` the same.
- 16 AI runs **only with the commit of the exit path (`df7443d`), superseded by §42.6** (with the two follow-up commits again 12 of 16 with the winners of `main`): **8 instead of 12 decided**. Four runs now end in a stalemate instead of with a winner (Alpine Assault and Death Valley, each America against China and ChinaTank against
  AirForce; before, America resp. AirForce won at tick 16,665, 14,610, 9,390, 10,635); **one change of winner** (Death Valley China against GLA: China at 8,580 → GLA at 8,940); seven
  runs have the same winner at a different time (China against GLA: Alpine 10,710 → 19,395, Armored Fury 42,840 → 11,865, Bitter Winter 7,950 → 13,935; GLA against America, winner America:
  Alpine 7,815 → 7,590, Armored Fury 28,800 → 18,795, Bitter Winter 9,075 → 9,150, Death Valley 8,475 → 11,955); four were and remain a stalemate. With that, all eight pairings America
  resp. AirForce against China resp. ChinaTank end in a stalemate. Looked at in one run (Death Valley America against China, `ai-match … --trace`): no standstill – 301 instead of 53 production orders,
  9,422 instead of 2,767 shots, China pushes America back to one building by tick 40,500 and does not finish it; China's build-up begins later (at tick 1,500 still 5,400 credits and 2
  orders instead of 200 credits and 17). Cause and fix: §42.6.
- The four 8-player runs: before as after a stalemate at the tick budget.
- `skirmish-sweep` (150 maps, 3,000 ticks): all 64 maps that start have different numbers; everywhere the same direction in the early build-up (e.g. Alpine Assault: 24 instead of 29
  finished productions, 10 instead of 8 finished buildings). No map ends with an error.

**Campaign** (30 Hz; before = logs of the binaries of `main` from the same day): `drive` victory 24/24, defeat 24/24, on every map the same end script at the same tick and
with the same interventions as before; `audit --ticks 900` 24/24 deterministic and identical after save/load, end probes victory 13/24, defeat 15/24, 0 hits on missing types,
no line different from before; `reachability` clean 24/24 and 24/24.

### 42.5 Known limits (only reported)

- The unit still comes into being immediately on completion and not only after `DoorOpeningTime` (the presentation opens the door correspondingly earlier).
- `QuantityModifier` is still not applied: the Chinese barracks builds one Red Guard per order instead of two.
- Trucks of the supply depot and `SpawnBehavior` spawns (tunnel, Stinger site) do not use the exit path; `QueueProductionExitUpdate` does not snap the point to
  the cell grid (`snapPosition`); "making room" applies only to the exit point, not to arbitrary paths.
- Above 30 Hz the choice of place reads objects that are not in phase with the factory (commanded in the middle of the frame) at their momentary position resp. their goal (rule 6 of the convention);
  a command to the new unit in the r − 1 ticks before the choice of place that does not replace its move command (an attack command) does not change the choice of place.
- The timeline of the doors in the overall scenes differs between 30 and 60 Hz in two entries by one frame
  (presentation, not the subject of this commit; not checked whether that is also the case on `main`).

### 42.6 Addendum (8 October 2026): why the AI skirmishes got worse, and two follow-up commits

Question after §42.4: 8 instead of 12 of the 16 AI runs decided, all pairings America/AirForce against China/ChinaTank in a stalemate, China spends no money for 50 seconds. Two causes,
one commit each; both belong to the exit path (`df7443d`) and stand after its test and documentation commit, because the history is not rewritten.

**Cause 1 – a weakness of the built-in `SkirmishAi` that the exit path only exposes** (commit `fix(skirmish-ai): a builder driving out of its factory can be given work`).
`SkirmishAi.PickBuilder` took only builders that are idle (`IsIdle`). Before, a freshly built dozer stood idle in the building and got its construction order in the next think step;
now it drives out for 50 to 95 frames and is not idle while doing so – the think step found no builder, the construction goal slipped by one think interval, after that the
order of the goals fell out differently. Evidence (Death Valley, America against China, command streams from `reference-manifest results --commands`): the streams of `main` and `df7443d` are the same up to
line 34; then `main` has in tick 628 `ConstructCommand Builder=29 ChinaBarracks` for the dozer finished in tick 627, `df7443d` has nothing for dozer 29 until
`778 SetMoveTargetCommand Target=29` and builds the barracks only in tick 1468 with the other dozer (production from 2233 instead of 1108). Fix: `AIUpdateInterface.IsOnUnorderedMove`
(the object moves, but nobody has commanded it: exit, second leg, making room; derived from the command state, no new state) and `PickBuilder` also takes
such builders. The computer player of the original asks its dozers whether a task is pending (`AIPlayer::findDozer`, `DozerAIInterface::isAnyTaskPending`), not whether they
stand still. With the commit the command stream of this match is, over all 641 commands, that of `main` (winner America at 9,390).

**Cause 2 – a bug in `df7443d` itself** (commit `fix(sim): any order replaces the exit path, not only a move order`). The second leg (rally point) and the door point were
discarded only by `OrderMove`. A unit built with a rally point set that got another command during the exit – construction task, dock, stop, an attack led by the
world –, kept the second leg and, as soon as it stood, ran off to the rally point; the command was thereby aborted. Test `ConstructionExitPathYieldsToAnOrder`
(30, 60, 120 Hz): a dozer that gets a construction order inside the building builds and stays; on `df7443d` it stood at the rally point, the construction site at one work step. In the
AI skirmishes of the manifest the case does not occur (the AI sets rally points only at barracks and factories and commands their units with move commands); found while
looking through what a command during the exit does. Fix: `EndExitPath` everywhere a movement is replaced or stopped from outside (`OrderMove`, `OrderDock`,
`StartInternalMove`, `StopInternalMove`).

**Checked and without a finding:** the factory does not wait for a free exit point (`IsFreeToExit` unchanged; the unit takes a place beside it); trucks of the
supply depot are untouched; units sent away go at most three places to the side or four rows back; in the examined match no construction order of the AI was rejected (rejections only for special abilities).

**The 16 AI runs of the manifest** (winner and winning tick; `reference-manifest results`):

| Run | `main` | only `df7443d` | with both follow-up commits |
| --- | --- | --- | --- |
| `AlpineAssault_America-China` | America 16665 | stalemate | America 16665 |
| `AlpineAssault_China-GLA` | China 10710 | China 19395 | China 10665 |
| `AlpineAssault_ChinaTankGeneral-AmericaAirForceGeneral` | AirForce 14610 | stalemate | AirForce 15195 |
| `AlpineAssault_GLA-America` | America 7815 | America 7590 | America 7815 |
| `ArmoredFury_America-China` | stalemate | stalemate | stalemate |
| `ArmoredFury_China-GLA` | China 42840 | China 11865 | China 11595 |
| `ArmoredFury_ChinaTankGeneral-AmericaAirForceGeneral` | stalemate | stalemate | stalemate |
| `ArmoredFury_GLA-America` | America 28800 | America 18795 | America 13215 |
| `BitterWinter_America-China` | stalemate | stalemate | stalemate |
| `BitterWinter_China-GLA` | China 7950 | China 13935 | China 7950 |
| `BitterWinter_ChinaTankGeneral-AmericaAirForceGeneral` | stalemate | stalemate | stalemate |
| `BitterWinter_GLA-America` | America 9075 | America 9150 | America 9090 |
| `DeathValley_America-China` | America 9390 | stalemate | America 9390 |
| `DeathValley_China-GLA` | China 8580 | GLA 8940 | China 10515 |
| `DeathValley_ChinaTankGeneral-AmericaAirForceGeneral` | AirForce 10635 | stalemate | AirForce 10635 |
| `DeathValley_GLA-America` | America 8475 | America 11955 | America 8415 |
| decided | 12 of 16 | 8 of 16 | **12 of 16**, the same twelve winners as `main` |

**Distribution instead of single runs** (`ai-survey --tick-rate 30 --seeds 10`, 16 pairings × 10 seeds = 160 matches; `main` = the first ten seeds of the reference survey, over 20 seeds
253 of 320):

| | `main` | only `df7443d` | with both follow-up commits | discarded variant "door stays" |
| --- | --- | --- | --- | --- |
| decided | 128 (80.0 %) | 106 (66.2 %) | **124 (77.5 %)** | 120 (75.0 %) |
| mean duration of the decided ones (ticks) | 14,963 | 15,344 | 14,319 | 15,259 |
| wins America (of 80) | 61 | 50 | 62 | 46 |
| wins AirForce (of 40) | 19 | 16 | 18 | 29 |
| wins China (of 80) | 23 | 19 | 18 | 32 |
| wins ChinaTank (of 40) | 5 | 2 | 5 | 2 |
| wins GLA (of 80) | 20 | 19 | 21 | 11 |
| decided America–China (of 40) | 26 | 14 | 26 | 16 |
| decided China–GLA (of 40) | 38 | 34 | 35 | 35 |
| decided ChinaTank–AirForce (of 40) | 24 | 18 | 23 | 31 |
| decided GLA–America (of 40) | 40 | 40 | 40 | 38 |
| first barracks, mean (tick, side A / B) | 1,054 / 1,054 | 1,573 / 1,252 | 1,054 / 1,054 | 1,133 / 1,135 |
| first attack, mean (tick, side A / B) | 3,236 / 2,739 | 3,752 / 3,070 | 3,206 / 2,688 | 3,256 / 2,700 |

With both follow-up commits the share of decided matches lies four matches below `main` (77.5 against 80.0 %; over 160 matches no verified difference), the distribution of wins
and the time of the first barracks are those of `main`. **Balance still not assessed.**

**Measured and discarded, not in the branch** (the patch lies in the scratchpad of the session): "a unit that gets another command inside the building still drives through the
door first" (the door point would stay on every replacing command). The original is not verified to know this, the dozers of the AI thereby need on average 80 ticks longer to the first barracks,
and the distribution of wins shifts clearly (America 46 instead of 61 wins, China 32 instead of 23; 10 instead of 12 of the 16 manifest runs decided). **Consequence, open:** a unit that
gets a command while still inside the building – with the AI the dozer in the tick after its appearance – leaves the building on the shortest path, i.e. through a wall, as on `main`.
The dozer seen by the project owner that "clips out" still exists for such units; units driving out by themselves take the door.

**Re-measured after the follow-up commits:** `Simulation.Tests` green at 30, 60 and 120 Hz (new `ConstructionExitPathYieldsToAnOrder`, `ConstructionUnorderedMove`); suite outside
`ai` unchanged (the same 14 rows), the 81 quantities of the nine exit cases bit-identical, `ai` 27 / 26; all twelve exit scenes and the three classic ones green at 30 and 60 Hz
with the same frame numbers as in §42.4; `reference-manifest check --set full`: the same 23 of 50 runs differ, reference match unchanged tick 14,130, `5BF9BB7CED6D7A35`,
`56149C0105E992D7`; `--set quick --verify-incremental`: 61,661 ticks, 0 deviations; campaign at 30 Hz `drive` 24/24 and 24/24 with the same end scripts and ticks, `audit` 24/24,
end probes 13/24 and 15/24; input manifest as in §42.3; `Game.Tests` (Release, with content), `Presentation.Tests`, `Ui.Tests`, `check-dozer-build`, `check-build-hotkeys`,
`check-spectator` green, solution build 0 warnings. The side of the AI (cause 1) is measured on real data, not covered by a test of its own for the `SkirmishAi`.

**Open:** the stalemates that remain (4 of 16, as on `main`) are matches in which one side pushes the other back to a few buildings and does not finish it (cf. §36); the
difference of four matches in the survey is not broken down; 60/120 Hz of the AI skirmishes not surveyed.

## 43. Phase 23: frame convention of the simulation above 30 Hz

As of 9 October 2026 (`f3acc2e`). Merged from `docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md` §1.3, `docs/rate-invariance/RATE-INVARIANCE-ROUND3-OBSERVERS.md` and `docs/rate-invariance/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md`; where
a package document of the first round says something else (the arrival "with the last share", the collision check "in the last tick"), this section applies. "Frame" means logic frame of the original
(1/30 s), "tick" a step of the simulation, r = ticks per frame (1, 2, 4). **No human has seen any of this.** The log of the phase is in `docs/TICKRATE-MIGRATION.md` §15.

### 43.1 Principle

The 30 Hz step is the definition of the correct behaviour. A higher rate **subdivides** it: what the original decides once per frame is decided once per frame, with the quantities
per frame; what moves as a result advances by a share in every tick. A single object therefore stands, after each of its frames, where the 30 Hz step puts it – in the
three-rate tests bit for bit. The convention governs what follows from this for two objects (or two modules of one object) that **look at** each other.

### 43.2 The rules

1. **Decisions are made in the first tick of the frame.** Second-order quantities, state machines and events "once per frame" fall on the first tick – in seconds the point in time of the 30 Hz tick.
2. **The position reaches its 30 Hz value with the last tick of the frame**; in between the object lies on the straight line to it.
3. **The frame belongs to the object**, not to the module and not to the world: an object that comes into being or is commanded at a frame boundary is in phase with the world; one that gets a
   command in the middle of the frame counts its movement frames from this tick on. Commands are accepted in every tick and wait for nothing. Two modules of one object decide in the same tick in the
   order of their module index (`EntityFrames.BeginsFrameOf`: the phase is the tick of coming into being).
4. **Whoever looks at another object in order to decide something reads it at the end of the frame it has begun** (§43.3). A mover that has already had its turn in this tick counts with its position after
   step T; one that is still to come, with its position after step T − 1 – exactly as at 30 Hz. The answer does not flip in the middle of the frame.
5. **An event that an observer triggers at the frame-end position puts the object on this position** (`Entity.CompleteFrame`): death by `HeightDieUpdate`, come into range, stopped,
   entered – and since round 3 **every death** (`SimulationWorld.OnEntityDied` completes the begun frame, also that of the turret). The mover covers no further share of this frame afterwards.
6. **Vision runs after the last tick of its frame** (unchanged every three frames) and reads the momentary position, which then, for every object in phase, is the position after step T. Whoever reads
   vision reads until then the state of the previous frame – never one that at 30 Hz does not yet exist at this game time.
7. **"One tick later" is one frame later.** A new object exists at the end of the tick that requested it; its first frame begins r ticks after that. An object requested in the spawn phase
   comes into being one frame later (`PendingSpawn.NotBefore`), a command from the spawn phase (rally point) is executed with the first frame
   (`SimulationWorld.DeferSpawnPhaseOrders`), an object killed after the destruction phase is removed one frame later (`Entity.DestroyNotBefore`), a march that the target acquisition gives after the
   updates begins in the next frame (`BeginMoveWithNextFrame`).
8. **A notification has one grid, that of the notifier** (`ICollideModule.OnCollide` comes per frame of the driving one; the receiver keeps none of its own).
9. **What has not been converted reads the momentary position** and lies at most one frame step of the observed one away from the 30 Hz value (§43.9).

### 43.3 The frame-end view at the object

`World/Entity.cs` carries what a mover still lacks of its begun frame; an observer adds it when reading.

| Symbol | What |
|---|---|
| `Entity.AheadX/Y/Z`, `AheadTick`, `SetAhead`, `CoverShare` | the not yet covered remainder of the frame step and the tick in which the mover last touched it; a remainder that its mover has touched neither in this nor in the previous tick (stopped, paralysed) no longer counts |
| `Entity.FrameEnd(tick)` | position at the end of the begun frame; at 30 Hz the position |
| `Entity.AheadAngle`, `HasAheadAngle`, `FrameEndAngle` | the heading at the frame end (round 3: a shooter that stops in the middle of the frame launches its missile with it) |
| `Entity.SetAheadHeight` | the height at the frame end (round 4: a ground vehicle on a slope) |
| `Entity.SpeedPerFrame` | the speed per frame for a second moving module of the same object (Chinook), only above 30 Hz |
| `Entity.CompleteFrame` | puts the object on its frame end (rule 5) |
| `Entity.DestroyNotBefore` | removal at the earliest one frame after the request (rule 7) |
| `EntityFrames.BeginsFrameOf` (`Time/FrameGrid.Consolidation.cs`) | the ticks with which the frames of an object begin |
| `IWorldServices.IsSignificantlyAboveTerrainAtFrameEnd` | "in the air?" from the height at the frame end |

**Who publishes:** the ground movement (`AIUpdateInterface.AdvanceFrame`: position, heading, height), shells, missiles and the neutron missile, all flight movers via `FlightFrameStep.Publish` (jet,
helicopter, Chinook, parachute including rider, shot-down jet and helicopter, Spectre, flare). **Not publishing:** train, avalanche, ferry, wave, free fall of `PhysicsBehavior`.

### 43.4 Who reads at the frame end

Converted are the readers at which a failure of the suite or the first deviation of a match was measured – not all (several hundred places read `X`/`Y` of another object):

- **Target acquisition:** `FindMoodTarget`/`ConsiderMoodTarget` measure from frame end to frame end; `Engage`, `WithinWeaponRange`, `FireWeaponAt` (pursuit target, "in range", launch of a projectile with
  position and heading of the frame end). The decision itself is made in the first tick of the frame of the unit (§41.1 A).
- **Turrets:** `DesiredTurretAngle`, `DesiredTurretPitch`, `KeepsAttackIntent`.
- **Damage:** `DealDamageCore` – main victim and every candidate in the radius at the frame end (before, of 40 scatter shots a different number failed depending on the rate).
- **Movement:** `StepBlockedAtFrameEnd`, `StationaryBlockersAheadAtFrameEnd`, `StandsStillAtFrameEnd`, `FootprintsOverlapAtFrameEnd`; `HordeUpdate.Evaluate`; `MobMemberSlavedUpdate` (Nexus);
  `OpenContain.Add`/`Remove`, `UpdateEnter`/`UpdateGoal`, `OrderApproachGoal`/`OrderEnter`; the choice of place of the factory exit (`GoalIsFree`, §42.3).
- **Modules:** `HeightDieUpdate`, `PointDefenseLaserUpdate`, `CleanupHazardUpdate`, `AssaultTransportAIUpdate`, `PilotFindVehicleUpdate`.
- **The suite and the tests** read a state at the frame end (`RateScene.StepUntilFrameEnd`); whoever above 30 Hz reads a position "one tick after" an event reads one share before the
  frame end and measures the reading, not the behaviour.

### 43.5 The first frame of new objects

The general version ("every update of a new object at the earliest `SpawnTick + r`") has been measured twice and **not adopted**: integrators and modules that count their updates from the first one
ran r − 1 ticks too late (13 red simulation tests at 60 Hz, 15 at 120 Hz). The conversion is per module, where a measured deviation begins: modules whose first update decides an event
or creates something (field, stealth, create modules, leaflet, `SpawnBehavior`, `AutoFindHealingUpdate`), `JetAIUpdate`, `ChinookAIUpdate`, `PilotFindVehicleUpdate`,
`HelicopterSlowDeathBehavior` (one frame after the death), the projectiles (first step one frame after the launch). **Rule of thumb for new modules:** if the first update decides an event, it comes
one frame after coming into being. **Not converted:** `DeliverPayloadAIUpdate`, parachute, ferry, dynamic reveal, firestorm, `FireOCLAfterWeaponCooldownUpdate`, enemy-proximity scan, checkpoint.

### 43.6 End of a march, stop and coasting

A march ends in the **first** tick of its last frame (`BeginArrival`: `MoveArrived`, no movement goal any more) and covers the remainder in the remaining ticks (`Coast`); this way everyone who looks at the
object in the first tick (target acquisition, the container it is walking into) sees it arrived as at 30 Hz. If an observer stops a unit after the updates (pursuit ends, victim in
range), it completes its frame and makes the next march decision only in the next one (`StopInternalMoveAtFrameEnd`, `SettleFrame`, `moveHoldUntil` only above 30 Hz). Whoever reads the event or
`HasMoveTarget` as "stands at the goal" and immediately takes the **momentary** position reads one share before the goal – the presentation has not been checked for this.

### 43.7 State that is written only above 30 Hz

At 30 Hz a frame is one tick: every share is the whole step, every phase zero, every remainder zero. The bookkeeping of the subdivision is therefore **written only when it deviates from the
default** – at 30 Hz never. That is the reason why all rounds of the rate invariance left the 30 Hz manifest unchanged (snapshots, hashes and replays byte-identical; `WorldSnapshot` 10, Replay 12,
`CommandTypes.Version` 15 without a version bump). At 60 and 120 Hz the content of a snapshot changes within a frame; a snapshot in the middle of the frame continues bit-identically (tested per package).
This includes: `frameSpeed`, `frameTicksLeft`, `frameDue`, `frameStepX/Y`, `frameAngle`, `frameArriving`, `moveHoldUntil` of the ground movement; `subStep` of the projectiles; `FramePhase` of the physics modules;
`FlightFrameStep` of the flight movers; `subSteps`, `goalAngle`/`goalPitch`, `startAngle`/`startPitch` of the turrets; `abilityFacePhase`; the `Ahead…` fields of the object; `PendingSpawn.NotBefore`,
`DestroyNotBefore` and the list `exitClaims` of the world; `_quietFrame` of the laser; two fields of `DeployStyleAIUpdate` in the frame of becoming ready.

### 43.8 The helpers side by side, and the proposal

Every package brought its own family; they compute the same thing with different rounding and a different way of counting (complete table: `docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md` §7).

| Family (file) | Share of a frame step | Way of counting, state | Constants per frame | Users |
|---|---|---|---|---|
| `ProjectileFrameGrid` (`Time/FrameGrid.Projectiles.cs`) | step / n, the last share takes the remainder | `subStep` 0 … n − 1 in the module | reconstructed from the tick value (decimal number with four places), otherwise × n; missiles since round 4 from `LocomotorFrames` | shells, missiles, neutron missile, free fall, guided bomb |
| `FramePhysics` (`Time/FrameGrid.Physics.cs`) | `floor(Schritt · k / n) − floor(Schritt · (k − 1) / n)`, sum exact | `FramePhase` 0 … n − 1 | × n resp. × n² | toppling, train, Battle Bus, collapse, reveal |
| `FlightFrames`, `FlightFrameStep` (`Time/FrameGrid.Flight.cs`) | raw value / n truncated towards zero, the first tick takes the remainder | `left` n − 1 … 0, as an object in the snapshot | × n, × n²; winged flight and jet since round 3 from `LocomotorFrames` | flight, jet, parachute, Chinook, Spectre, crash |
| `AIUpdateInterface.AdvanceFrame` (`Phase5Modules.cs`) | `Roh · (i + 1) / n − Roh · i / n`, sum exact | `frameTicksLeft` n … 1, `frameDue` | `LocomotorFrames` (the 30 Hz conversion of the same source numbers, table next to the catalog type) | ground movement |
| `TurretState.SubSteps`, `TurretInfo.RatesPerOriginalFrame` | remainder / remaining sub-steps downwards, the last one sets the goal | `SubSteps` | table like `LocomotorFrames` | turrets |
| `FrameGrid`, `FrameGridTiming.ZeroIsNextFrame` (`Time/FrameGrid.cs`, `FrameGrid.Timing.cs`) | – (durations on the grid) | – | – | everywhere |
| `Entity.Ahead…`, `EntityFrames.BeginsFrameOf` | no rounding of its own (the mover supplies its shares) | `AheadTick` | – | observers |

**Proposal for later** (changes results at 60 and 120 Hz, never at 30 Hz; a step of its own with the suite as the measure, not done): *one* share helper in the form of
`FramePhysics.FrameSlice` (sum exact, sign-correct, no state except the number of the tick in the frame) and the remainder at the object (`Entity.Ahead…`) as the only bookkeeping of what is missing – then
`subStep`, `FlightFrameStep`, `frameTicksLeft` and `FramePhase` cease to exist as a state of their own each, and every mover publishes by itself. For constants per frame the pattern of
`LocomotorFrames` instead of `× n` and instead of the decimal reconstruction; with that the movers that today still compute `Wert je Tick × r` would also disappear (§43.9). Pitfall, already occurred once:
parallel packages that introduce helpers of the same name collide only when building – build after every merge.

### 43.9 New reference values, and what is not the same

**Reference values at 60 Hz** (`baselines/reference-manifest.json`, schema 2, written in `ea52f87`; checked twice with 50 of 50 runs identical): retail reference match winner FactionGLA at
**tick 28,260** (original frame 14,130), final hash `CA05181C36C62C7A`, chain `21ACEDA63325A658`, 26,626 commands; stress test 216,000 steps (at 60 Hz one hour of game time) final hash
`9EE9F730D124C3BD`, chain `F338463BE66D99C0`; 16 AI runs **14 decided**; world fingerprint of the input manifest `C81DC18A282037CD`. Versions unchanged: `WorldSnapshot` 10, Replay 12,
`CommandTypes.Version` 15, `MissionSnapshot` 3, `SaveGame` 3. Superseded: tick 17,805 / `D0CA5F4EE456D6CE` / `C5EE1090E1C24D7B` (§38, 30 Hz) and the 30 Hz intermediate states of the phase, tick 14,130 /
`5BF9BB7CED6D7A35` / `56149C0105E992D7` (§42.4). A manifest is checked with the rate that its file names; a differing `--tick-rate` is rejected.

**The reference match is the same game at 30, 60 and 120 Hz:** winner FactionGLA in tick 14,130 / 28,260 / 56,520, at every rate frame 14,130, 26,626 commands, and `rate-compare --reference-diff
--fields` finds, between 30 and 60 as well as between 30 and 120 Hz, no deviating field at the end of any frame (`docs/rate-invariance/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md` §1.1).

**Not the same, not converted, not measured** – the merged list is in `docs/TICKRATE-MIGRATION.md` §15.7.6. In short: 2 of 16 pairings on shipped maps end at
60 Hz in a different frame; 33 of 320 matches of the survey end more than one second beside the 30 Hz match, 3 with a different outcome; 120 Hz is not measured for pairings and survey;
Spectre, parachute, flare, particle beam, shot-down jet and the gravity of the physics modules still compute `Wert je Tick × r`; `SlavedUpdate` and the regeneration of the occupants in
`ContainModules.cs` still heal per tick; script VM, special abilities, bridges, water and cliffs have not been examined. The decision on the self-acquisition per frame of the unit and the
three related places (§41.1 A) lies with the project owner.

## 44. Phase 31: fidelity package A (10 October 2026)

Three places where the port visibly behaved differently from the original (`docs/ACCEPTANCE.md` §6, fidelity register F-03, F-04, F-05 of `docs/plan/STAGE-1-PORT.md` §3), each one
cause, one implementation commit and one manifest commit. Stage 1 work: the yardstick is the function of the original, read and rewritten. **No human has seen any of it**; the
scenes are headless.

### 44.1 F-03: leaving a container (`OpenContain::exitObjectViaDoor`)

**What the original does – and what it does not.** The brief of the phase described the finding as "the riders are scattered when `ScatterNearbyOnExit` is set". The source says otherwise,
in *Generals* and in *Zero Hour* alike (`GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Contain/`):

- `TransportContain::onRemoving` (`TransportContain.cpp`, line 330 ff.) calls `scatterToNearbyPosition(rider)` **only when the transport is effectively dead** (line 412). The key
  `ScatterNearbyOnExit` (`m_scatterNearbyOnExit`, default yes) is parsed and read by nothing in `TransportContain`; only `MobNexusContain` consults a key of this name. The data set it to
  `No` in 47 of 48 places – without effect.
- A rider of a **living** transport leaves through `aiExit` → `AIExitState::update` (`AIStates.cpp`, line 6449): it waits while the door is busy (`TransportContain::isExitBusy`: the
  frame of the last exit plus `ExitDelay`), then `OpenContain::exitObjectViaDoor` (`OpenContain.cpp`, line 939): `removeFromContain`, and with `NumberOfExitPaths > 0` (default 1) the rider
  is set on the bone `ExitStart` with the container's heading and follows a path to the bone `ExitEnd` (`ExitStartNN`/`ExitEndNN`, cycling, when there are several paths), which
  `Pathfinder::adjustToPossibleDestination` (`AIPathfind.cpp`, line 5534) moves to the nearest cell the rider can stand on – "so they don't stack up". The container is the ignored
  obstacle of that walk and the rider ignores collisions for one second (`setIgnoreCollisionTime(LOGICFRAMES_PER_SECOND)`). With `NumberOfExitPaths = 0` the rider stays where
  `removeFromContain` put it: on the container.
- `GarrisonContain::exitObjectViaDoor` (`GarrisonContain.cpp`, line 1497) has no bones and no door: the occupant stands on the building's position and walks to the nearest possible
  destination of that position (`EVAC_BURST_FROM_CENTER`), or leaves to the side (`EVAC_TO_LEFT`/`RIGHT`, already built).
- `OpenContain::scatterToNearbyPosition` (`OpenContain.cpp`, line 688): a random angle, a random distance between one and one and a half bounding-circle radii of the container (two
  draws of the logic stream, in this order), the rider faces that way, stands on the container's centre and walks to the point, ignoring the container.

**What was built** (the source decides, not the brief's summary of it):

| Piece | Where | Rule |
|---|---|---|
| Keys typed strictly | `ModuleCatalog.cs`: `NumberOfExitPaths` (integer), `ExitDelay` (duration), `ScatterNearbyOnExit` (bool) | the compatibility baseline shrinks from 731 to 719 entries (the twelve `…Contain/ExitDelay`, `/NumberOfExitPaths`, `/ScatterNearbyOnExit` entries); `ScatterNearbyOnExit` is typed and deliberately read by nothing |
| Exit bones | `ModelGeometry.ExitPaths`, `ModelGeometryLoader.ReadExitPaths` | per object with `TransportContain`, `HelixContain`, `OverlordContain`, `RiderChangeContain` or `InternetHackContain`: `ExitStart`/`ExitEnd` per path from the model of the state without any condition, exact and rounded like the airfield bones; a missing bone is "no pose" (the original then answers with the object's position, `Object::getSingleLogicalBonePosition`). Measured on the master installation: 100 containers, 166 paths, 82 with both bones, 76 containers without any exit bone (Chinook, Overlord, Avenger, Spectre …). The table is part of the world fingerprint |
| The door | `TransportContain.ExitViaDoor`, `ReleaseThroughDoor`, `ExitObjectViaDoor` (`ContainModules.cs`) | a rider that is told to leave (exit command, evacuation) is released at once when the door is free, else queued; of several waiting riders the lowest id goes first (the riders poll in the order of their updates); every removal keeps the door busy for `ExitDelay` (`onRemoving`). The rider is put on `ExitStart`, faces the way the container faces and walks to `ExitEnd`, adjusted by `SimulationWorld.FindPossibleDestination` |
| Nearest possible destination | `SimulationWorld.FindPossibleDestination` (`SimulationWorld.ExitGoals.cs`) | the point itself when free, else the reference's square spiral over cell centres (`i++` × δ, `j++` × δ, δ + 1, `i--`, `j--`, δ + 1 …), at most 400 cells. "Free" is this simulation's test (`GoalIsFree`: cell enterable, no structure, no mobile object standing there or heading there) – there is no goal-cell table and no zone table here |
| The rider's walk | `AIUpdateInterface.OrderExitContainer`, `IgnoredObstacle`, `IgnoresCollisions` (`Phase5Modules.cs`, `SimulationWorld.Collision.cs`) | a move with the container as the ignored obstacle; for one second of game time after a door exit the local avoidance does not shove the rider's step aside; the free-goal check every 15th frame does not touch the exit leg |
| Garrison | `GarrisonContain.ExitViaDoor` | on an exit order of a living building the occupant walks from the building's position to its nearest possible destination; all occupants leave in the same frame ("Garrison doesn't have reserveDoor or exitDelay") |
| Destroyed transport | `TransportContain.Remove` → `ScatterToNearbyPosition` | the riders of an effectively dead transport, and only they, are scattered; two draws per rider, dead riders included (they stay in the contain list until they are destroyed) |

**State and formats.** New state is written only where it differs from an idle object's: `exitNotBusy`, `whichExitPath`, `exitWanted` of the container; `exitFrom`, `ignoreCollisionsUntil` of the
rider's AI. `exitNotBusy` is set by every removal, so worlds in which a rider has left a transport differ; no format version changes (`WorldSnapshot` 10).

**Frame convention.** The first rider leaves in the tick of the order (as before), the following ones `ExitDelay` later each – a duration on the original grid, so the exits fall into the
same frames at every rate. The rider's walk is an ordinary move order. Three-rate test: the same exit frames (0, 8, 16, 24 after the order for `ExitDelay = 250`) and the same position of
every rider at the end of every logic frame at 30, 60 and 120 Hz, with and without bones, and a snapshot taken while riders wait for the door continues identically.

**Tests.** `Tests.ContainerExit.cs` (prefix "Container exit": one by one and apart; bones and cycling, a table that disagrees with the module is refused; no exit paths; destroyed transport;
three rates and snapshot), `ModelGeometryTests` (the Humvee's six bones against `AVHUMMER.W3D`, the measured counts), and the scene `acc-board`, whose audit no longer knows a finding
S1: after both evacuations the five Rangers stand 22.7 to 36.3 from the Humvee's centre (bounding radii 7.0 and 15.7), smallest margin between two bounding circles 2.3 and 1.5.

**Deliberately not built, with reasons.** The door animation (`DoorOpenTime`, model states `DOOR_1_OPENING`/`CLOSING`); a rally point of a container (`OpenContain::m_rallyPoint` –
no command sets one here); `GoAggressiveOnExit`, `ResetMoodCheckTimeOnExit`, `OrientLikeContainerOnExit`, `KeepContainerVelocityOnExit`, `ExitBone`, `ExitPitchRate`, `DelayExitInAir`
(still opaque); the reachability test of the reference's spiral (zones); the front/back search of a bunker that stands in a cliff; the bones of a damaged model state (the table is
read from the state without any condition). `HealContain` still puts a healed occupant on the building (entered into the fidelity register as F-34). The riders that
`DeliverPayloadAIUpdate` and the ferry take out go their own ways as before (their calls of `Remove` are unchanged).

**Measured (10 October 2026, implementation `f9abff8`, manifests rewritten in the commit after it; one cause).** Result quantities of the 50 manifest runs before and after
(`SimTool reference-manifest results`): **38 runs unchanged** in every quantity, **47 with the same outcome**, 41 with the same end tick. 26 of the 27 mission runs, the stress test, the
skirmish overview and the reference match are unchanged (reference match: winner FactionGLA at tick 28,260, end hash `CA05181C36C62C7A`, chain `21ACEDA63325A658` – none of them
evacuates a living transport or building); their `commandStreamSha256`/`snapshotSha256` change with the world fingerprint (`05EDB8339FE38C53`, before `AC622E33080D89EB`: the exit bones are part
of it). Twelve runs differ: `mission_MD_USA02` (same outcome and end tick; the same number of exits, but 81 instead of 63 shots and 79 instead of 60 deaths in its 6,000 ticks - which of
its exits makes the difference was not broken down further), two eight-player runs (stalemates before and after) and nine of the sixteen
pairings, all nine with an American side, whose AI loads and evacuates transports. Three outcomes change, and in each the first deviating
command follows the first `EvacuateCommand` of the run by 30 to 150 ticks (the riders now stand beside their transport one by one instead of on its centre at once; from there the
matches diverge chaotically):

| Run | Before | After | First evacuation | First deviating command |
|---|---|---|---|---|
| `AlpineAssault_America-China` | stalemate (tick 90,000) | winner FactionAmerica (end tick 37,441) | tick 14,908 | tick 14,966 |
| `ArmoredFury_America-China` | winner FactionAmerica (74,701) | stalemate (90,000) | tick 12,838 | tick 12,986 |
| `BitterWinter_America-China` | winner FactionChina (44,611) | winner FactionAmerica (55,501) | tick 17,458 | tick 17,488 |

Decided AI runs: 14 of the 16 pairings on shipped maps before and after (one stalemate became a win, one win a stalemate). **The balance is not assessed** (D-9). After the
rewrite `input-manifest check` is identical and `reference-manifest check --set full` passes twice with 50 of 50 runs identical; the rate-equivalence suite is 0 / 0 at 60 and
120 Hz in all ten groups with the new case "leaving a transport" (locomotion: 55 cases).

### 44.2 F-04: a destination of its own for every unit (`AIGroup::computeIndividualDestination`, `Pathfinder::adjustDestination`)

**What the original does.** A move order to a selected group is one message (`MSG_DO_MOVETO`) to an `AIGroup`. `AIGroup::groupMoveToPosition` (`GameLogic/AI/AIGroup.cpp`, line 1552)
ends, for every group that is neither a formation nor led along a group path, in one loop: the members are sorted by their distance from the goal, nearest first ("works better if you
let the near units get the first paths"), the nearest member's position becomes the centre, and `computeIndividualDestination` (line 480) gives each member the goal plus its own offset
from that centre, shortened to six of its bounding-circle radii; for a ground mover `Pathfinder::adjustDestination` (`AIPathfind.cpp`, line 5355, with `checkForAdjust`, line 5201, and
`checkDestination`, line 4924) moves it to the nearest free cell of the square spiral (at most 400 cells; "don't usurp your allies goals", "don't move to an occupied cell") and
`updateGoal` reserves it. The goals the members held before are released first (`removeGoal`). A single unit's destination is adjusted in the same way when its move begins
(`AIInternalMoveToState::onEnter`, `AIStates.cpp`, line 1662). A unit then walks one path to a place that is its own from the start.

**What was built.**

| Piece | Where | Rule |
|---|---|---|
| The group order | `GroupMoveCommand` (`Commands.cs`, type code 33) | several units, one point, optionally as an attack-move. Validated as a whole when it is submitted (every unit the player's and able to move; at most 1,024 units); a member that is gone when it executes takes no part. **`CommandTypes.Version` 15 → 16, replay format 12 → 13**: older replays and command tables are rejected by their version. `WorldSnapshot` stays 10 (a pending command is written by its own type) |
| Individual destinations | `SimulationWorld.ExecuteGroupMove` (`SimulationWorld.GroupMoves.cs`) | the loop of `groupMoveToPosition` as described above: sorted near to far (ties by id), centre = the nearest member, offset shortened to six bounding radii, adjusted for ground movers, each member's order given at once so that its goal counts for the next; positions are read at the frame end (§43.2, rule 4) |
| Adjustment of every move | `SimulationWorld.IndividualDestination` | `SetMoveTargetCommand` and `AttackMoveCommand` of a ground mover: the ordered point when free, else the nearest free cell of the spiral. A unit ordered onto the very point it stands on (this simulation's stop order) and an aircraft keep the point |
| "Free" at an order | `GoalIsFree(…, atOrder)`, `FindDestination` (`SimulationWorld.ExitGoals.cs`) | no structure on the cell and no claim of a unit that is not the mover's enemy and does not fly (standing units claim their place, moving ones their goal). The ordered point itself is not refused for ground the mover cannot enter; a spiral cell has to be enterable when the ordered point is, and not enterable when it is not (`checkForAdjust` accepts a cell with a path to it, or - when the ordered point has none - one on the ordered point's side: a boat sent to a taken point on land gets a place on land and goes as far as the water does) |
| Who gives the order | `CommandModeController.MoveGroup` (presentation), `ScenarioOrderKind.MoveGroup` (scenes) | a player's move or attack-move order for more than one selected unit is one `GroupMoveCommand`; one unit keeps the single commands. The scene `acc-march` gives one group order per leg |

**Deliberately not a guess.** The first version derived a group from consecutive single commands with the same target. That also caught the script VM, which sends the members of a
team to one waypoint with single commands and counts a member as arrived within 40 units of that waypoint (`ScriptEngine.UpdateFollowers`): with offsets of up to six radii such a
member never arrives. The original distinguishes the cases by who gives the order, and so does the port now; the single commands of the VM and of the built-in `SkirmishAi` only get
the adjustment every move gets. **Open, entered as F-35:** in the original the VM's team moves (`MOVE_TEAM_TO`, hunts) and the AI's team orders are group orders too.

**Not built, with reasons.** The column moves of far-away infantry and vehicles along one group path (`friend_moveInfantryToPos`, `friend_moveVehicleToPos`), the tightening of a
group ordered into its own bounds (`groupTightenToPosition`, `GroupMoveClickToGatherFactor`), formations the player set (`friend_moveFormationToPos`), the path-cost test of an
adjusted cell against the group goal (`checkForAdjust` with `groupDest`, `tightenPath`), cliffs as refused destinations, the failure of a move for which no cell is free (the
destination then stays as ordered), the delayed mood check of stealth units, the logical map extent for human players. The free-place check near the goal every 15th frame
(`TryFreeGoalSpot`) stays for a goal that is taken after the order.

**Frame convention.** The order is executed in the tick it is given for; the destinations depend on positions at the frame end and on nothing of the rate. Three-rate test: the same
destinations and the same position of every unit at the end of every logic frame at 30, 60 and 120 Hz.

**Tests.** `Tests.GroupMove.cs` (prefix "Group move": offsets, shortening and places of their own; coinciding places, an occupied goal of a single mover, the stop order; validation,
snapshot with a pending order, replay round trip, attack group; three rates), `InputTests` (the selection gives one group order), the rate-equivalence case "group order" in the
locomotion group, and the scene `acc-march`, whose audit no longer knows a finding S2 and fails on a detour: on the way out every unit drives 0.0 to 1.8 units more than the straight
line (before: two Rangers 418 and 453 units for 234 and 244) and rests 0 to 80 from the goal on a place of its own; on the way back the six Rangers walk 145.4 for 145.3 and the three
tanks 188 to 202 for 145 (they turn round first); smallest margin between two bounding circles 2.6 at the end of both legs.

**Measured (10 October 2026, implementation `39a1656`, manifests rewritten in the commit after it; one cause).** No run of the manifest gives a group order (only the presentation
and the scenes do); what moves the runs is the adjustment of every single move at its order - the AI's attack-moves, its builders' and collectors' moves, the moves of the script VM.
Result quantities of the 50 runs before (after F-03) and after: **22 runs unchanged** in every quantity, **48 with the same outcome**, 35 with the same end tick. The reference match
(winner FactionGLA at tick 28,260, `CA05181C36C62C7A` / `21ACEDA63325A658`), the stress test and 20 of the 27 mission runs are unchanged; seven mission runs keep their outcome and
differ in counts (`MD_USA02`: 57 instead of 81 shots; `MD_GLA05`: 620 instead of 642). The skirmish overview still starts and ends all 64 of its maps. The four eight-player runs stay
stalemates. All sixteen pairings on shipped maps keep being decided 14 times; fifteen end at another tick, two change their outcome:

| Run | Before (after F-03) | After | First deviating command |
|---|---|---|---|
| `ArmoredFury_America-China` | stalemate (tick 90,000) | winner FactionAmerica (end tick 42,991) | tick 5,216: an attack-move 30 ticks later and to another point |
| `BitterWinter_America-China` | winner FactionAmerica (55,501) | stalemate (90,000) | tick 1,798: a construction order 30 ticks earlier |

In both, and in `AlpineAssault_China-GLA` (same winner, end tick 17,791 → 81,631), the command streams are equal up to a point after the first moves of the run and then differ in
the timing of an order of the AI - a unit stands on another place than before, and the match diverges from there. **The balance is not assessed** (D-9). After the rewrite
`input-manifest check` is identical, `reference-manifest check --set full` passes twice with 50 of 50 runs identical, and the rate-equivalence suite is 0 / 0 at 60 and 120 Hz in
all ten groups with the new case "group order" (locomotion: 56 cases, 896 quantities).

### 44.3 F-05: the slow death plays its phases (`SlowDeathBehavior::doPhaseStuff`)

**What the original does.** `SlowDeathBehavior::beginSlowDeath` (`GameLogic/Object/Behavior/SlowDeathBehavior.cpp`, line 232 ff.) draws the sink delay and the destruction delay
with their variances, then the midpoint `GameLogicRandomValue(BEGIN_MIDPOINT_RATIO * m_destructionFrame, END_MIDPOINT_RATIO * m_destructionFrame)` (0.35 and 0.65), and ends with
`doPhaseStuff(SDPHASE_INITIAL)`. `update` (line 386 ff.) calls `doPhaseStuff(SDPHASE_MIDPOINT)` once the midpoint frame has come and `doPhaseStuff(SDPHASE_FINAL)` at the destruction
frame, before the object is destroyed. `doPhaseStuff` (line 339 ff.) returns at once when the module names no list at all; else, for the phase, it draws one entry of the `FX` list
and plays it at the object, one of the `OCL` list and creates it at the object, one of the `Weapon` list and fires it once at the object's own position
(`createAndFireTempWeapon`) - three draws at most, in this order, also for a list of one. The keys are `<phase> <name>...` and append to the list of their phase
(`parseFX`, `parseOCL`, `parseWeapon`).

**What was built.**

| Piece | Where | Rule |
|---|---|---|
| Keys typed strictly | `FieldType.TaggedReferenceList`, `NameTables.SlowDeathPhase`, `ModuleCatalog.cs` | `FX`, `OCL` and `Weapon` of `SlowDeathBehavior`, `HelicopterSlowDeathBehavior`, `JetSlowDeathBehavior`, `BattleBusSlowDeathBehavior` and `NeutronMissileSlowDeathBehavior`: a phase name (`INITIAL`, `MIDPOINT`, `FINAL`), then references to effect lists, creation lists or weapons. In the retail data: 1,210 `FX INITIAL`, 198 `FX FINAL`, 693 `OCL INITIAL`, 26 `OCL MIDPOINT`, 252 `OCL FINAL`, 1 `Weapon INITIAL`, 4 `Weapon MIDPOINT`, 43 `Weapon FINAL` lines, each with one name |
| The lists | `SlowDeathPhases` (`Phase5BlockerModules.cs`) | three lists per phase; `NONE` is an empty entry that takes part in the draw and does nothing |
| The midpoint | `SlowDeathBehavior.BeginSlowDeath`, `SlowDeathPhases.MidpointBounds` | the third draw of every slow death, between 0.35f and 0.65f of the destruction delay in logic frames. The bounds are the single-precision products cut off to whole frames (the logic runs the FPU with 24 bits of precision, `GameLogic.cpp`, line 215), computed with whole numbers: 20 frames give 7 to 13, 30 give 10 to 19 |
| The phases | `SlowDeathPhases.Do`, `SlowDeathBehavior.Update` | INITIAL in the tick of the death, MIDPOINT and FINAL at their frames (both in one update when they fall together, FINAL before the removal). The effect list is an `FxRequested` event, the creation list goes through `CreateFromList`, the weapon through `FireTempWeapon`. An unknown creation list or weapon and a nugget the simulation does not create are counted `CreationSkipped` events with their reason - nothing is silent |
| The dying object stops | `AIUpdateInterface.StopAtDeath`, called by `SimulationWorld.OnEntityDied` | `SlowDeathBehavior::onDie` calls `ai->markAsDead()`, and `AIDeadState` (`AIStates.cpp`, line 1477 ff.) clears the locomotor's goal in every update: a dying ground object's move, dock, builder task and approach end where the logic frame of the original ends in which it died - above 30 Hz the frame it was covering is completed, and an object that has not had its turn in the tick of its death takes that frame's step first, so that the place is the same at every tick rate (the first version stopped it at once and failed the AI group of the suite at 60 and 120 Hz) - and it takes no further order ("the dead don't listen very well"). Before, a dying vehicle drove on during its slow death - invisible as long as nothing was left behind; with a hull it lay at another place at another tick rate (found by the three-rate test of the original skirmish AI with real data). Not modelled: the rolling out by physics; what dies in the air is moved by its own death module |

**State and formats.** A slow death that has begun writes `midpointTick` and, once played, `midpointDone`; a living object writes what it wrote before. No format version changes.

**Frame convention.** The delays and the midpoint are drawn in logic frames of the original and scaled (the same draws at every rate); the phases fall on the death tick plus whole
frames. Three-rate test: every effect request, the hull, the weapon's damage and the removal in the same logic frame at 30, 60 and 120 Hz; a snapshot during the slow death continues
identically.

**Tests.** `Tests.SlowDeathPhases.cs` (prefix "Slow death phases": the three phases, a list of two drawn both ways over spread seeds, a death without lists, the bounds of the
midpoint; three rates and snapshot; a vehicle killed while driving stops, takes no order and leaves its hull where it died, at every rate), the rate-equivalence cases "slow death …" of the group of special powers and time modules, which now also measure what a dying object played
and created and when, and the scene `tank-duel`: `scenario-audit --name tank-duel --drawables-at 390` lists the hull (`ChinaVehicleBattleMasterDeadHull`, model `nvbtmstr_d1`) where
the Battlemaster died in frame 382 and went in frame 388.

**Not built, counted or named.**

- **The debris.** `OCL_ChinaTankBattleMasterDebris` creates the hull with a `CreateObject` nugget and the flying pieces with `CreateDebris` nuggets. `CreateFromList` does not create
  debris (a physics object with a model of its own): each is a counted `CreationSkipped` (reason "nugget not simulated"), three in `tank-duel`. Entered as F-36.
- **The subclasses.** `HelicopterSlowDeathBehavior`, `JetSlowDeathBehavior`, `BattleBusSlowDeathBehavior` and `NeutronMissileSlowDeathBehavior` extend the same behaviour in the
  original and play the base lists too; here their keys are typed and their modules play only their own effect and creation keys, as before (the midpoint draw is not made for them).
  Entered as F-37.
- `SinkRate`, `DestructionAltitude` (the hull's sinking is presentation), `FlingForce`/`FlingPitch` (no physics impulse), `ModifierBonusPerOverkillPercent`, the time scale of
  the game LOD (`getSlowDeathScale`: always 1 here).
- Whether the presentation shows every requested effect list is its own count (`docs/CLIENT-FX.md`); **no human has seen a death explosion or a wreck**.

**Measured (10 October 2026, implementation `94711fb`, manifests rewritten in the commit after it; one cause).** Every slow death draws once more (the midpoint), plays its lists
and leaves what its creation lists name; a dying ground unit stops; hulls lie where units died and death weapons hurt. Result quantities of the 50 runs before (after F-04 and the
member-order fix of §44.5, which moved no quantity) and after: **23 runs unchanged** in every quantity (the stress test - end hash `9EE9F730D124C3BD` and chain `F338463BE66D99C0`
as before, so no object of its synthetic fixture draws for a slow death -, the short eight-player run and 21 mission runs, whose counted quantities are the same), **48 with the
same outcome**, 34 with the same end tick; **all 27 mission runs keep outcome and outcome tick**. The world fingerprint is `4E6F6F5623FE86DC` (before `05EDB8339FE38C53`: the typed
keys are part of the catalogue), so `commandStreamSha256`/`snapshotSha256` change in every run that carries it.

**New reference values:** retail reference match **winner FactionGLA at tick 32,520** (original frame 16,260; end tick 33,121), end hash `6052051714309B17`, chain
`84077720F511C1ED`, 30,035 commands. Superseded: tick 28,260 / `CA05181C36C62C7A` / `21ACEDA63325A658` (Phase 23, unchanged through F-03 and F-04). In the reference match 1,969
objects die instead of 1,871 and 12,963 come into being instead of 12,165 (the hulls and what the death lists create); 2,185 creations are skipped instead of 2,030 (among them the
debris pieces).

Of the sixteen pairings on shipped maps **14 are decided, as before**; two change their outcome:

| Run | Before (after F-04) | After | First deviating command |
|---|---|---|---|
| `AlpineAssault_America-China` | winner FactionAmerica (end tick 46,141) | stalemate (90,000) | tick 7,768 of 90,000 (command 292 of 2,190) |
| `BitterWinter_America-China` | stalemate (90,000) | winner FactionAmerica (end tick 61,381) | tick 8,816 (command 311 of 3,527) |

Both deviate early in the match and diverge from there; which death list or which stopped unit moves which fight was not broken down. **The balance is not assessed** (D-9).
After the rewrite `input-manifest check` is identical, `reference-manifest check --set full` passes twice with 50 of 50 runs identical, and the rate-equivalence suite is 0 / 0 at
60 and 120 Hz in all ten groups (4,808 quantities; the slow-death cases now carry six quantities each).

### 44.4 The steps side by side

| Step | Implementation | Manifest | Runs unchanged / same outcome (of 50) | Pairings decided (of 16) | Reference match | Formats |
|---|---|---|---|---|---|---|
| start (`885896e`) | – | – | – | 14 | GLA, tick 28,260, `CA05181C36C62C7A` | `CommandTypes` 15, replay 12; world fingerprint `AC622E33080D89EB` |
| F-03 door exit | `f9abff8` | `12533c6` | 38 / 47 | 14 | unchanged | world fingerprint `05EDB8339FE38C53` |
| F-04 own destinations | `39a1656` | `1a191cd` | 22 / 48 | 14 | unchanged | `CommandTypes` 16, replay 13 |
| member order of a team (§44.5) | `f41c851` | `2b34dba` | 50 / 50 in the quantities (one run in its hashes) | 14 | unchanged | – |
| F-05 slow-death phases | `94711fb` | `bab51c3` | 23 / 48 | 14 | GLA, tick 32,520, `6052051714309B17` | world fingerprint `4E6F6F5623FE86DC` |

`WorldSnapshot` 10, `MissionSnapshot` 3 and `SaveGame` 3 are unchanged through all steps; the compatibility baseline went from 731 to 713 entries (18 opaque keys typed).

### 44.5 Found on the way: the member order of a script team is state (`f41c851`, manifest `2b34dba`)

Not one of the three findings, but a defect they uncovered, fixed with a commit and a manifest commit of its own. The script VM keeps the members of a team in the order they
joined and gives its orders member by member; it **wrote them sorted by id**, so a restored session gave the same orders in another order than the uninterrupted one. Until F-04 two
move orders of one tick did not depend on their order. Since a move gets its destination when it is ordered (§44.2), the first member takes the free place and the second the next
one: the real-data test "a session of the original skirmish AI saved inside a frame continues identically" failed on the state of F-04 - saved at tick 16,006, the restored session
ordered two units in the other order at tick 16,294 and differed in the position of a tank from tick 16,297 on. The tier 3 gate of F-04 did not contain that test (it is part of
`sim-content`, tier 4); the fix therefore comes with a synthetic test of its own ("Scripts: the members of a team are saved in the order they joined", tier 1/2), and the real-data
test now reports the first tick at which the saved documents of the two sessions differ, the place in the document, and whether their navigation grids agree.

The members are now written in their own order, and the state hash follows. Effect on the manifest: **one run of 50**, `mission_MD_USA05`, in its five hash fields (final hash
`6C8132705A887217` → `48324DF5137AFF7F`); every result quantity of that run is unchanged, no other run moves, the input manifest is identical. A savegame or mission snapshot
written before carries its teams in id order and is read as such - no format version changes, but a session restored from it continues with that order.

**What this says about the tiers:** between `39a1656` and the fix, a restored session of the original AI could diverge from the uninterrupted one. The built-in `SkirmishAi`, the
reference match and the missions of the manifest were not affected (their checks passed at every step), and savegames of skirmishes do not exist yet (Phase 24). Other collections
of the VM that are written sorted (`.Order()` in `ScriptEngine.Describe`) were not examined for the same pattern - a point for step 2 of Phase 24 ("no field forgotten").

### 44.6 Closing measurements (10 October 2026, state `bab51c3`, Windows, Release)

**Tier 4** (`pwsh scripts/test.ps1 -Tier full -KeepGoing`, 4,111 s): 34 of 34 steps ok - clean build 0 warnings; every test program; compat-check 713; content against its manifest;
known vulnerabilities; `Simulation.Tests` at 30, 60 and 120 Hz, with real data (including the rate-equivalence suite, 0 / 0 in ten groups, 689 cases, 4,808 quantities) and the long
run; `Game.Tests` and `Presentation.Tests` with real data at 30, 60 and 120 Hz; `input-manifest check` identical; `reference-manifest check --set full` 50 of 50 identical; the three
client checks (`check-spectator`, `check-dozer-build -Quick`, `check-build-hotkeys`; they need a display, which the machine has had since this day through a virtual monitor); the
three staged scenes. **Campaign** (60 Hz): `CampaignTool reachability` victory clean 24 and defeat clean 24; `audit` 24 of 24 identical on a second run and after save/restore, end
probes victory 13/24 and defeat 15/24 (the 25th map of the list, `CampEADemo`, is missing in the retail content, as before); `drive` (120,000 ticks) victory 24/24 and defeat 24/24
through the missions' own scripts. All four are the values of before the phase (`docs/TICKRATE-MIGRATION.md` §15.7.4).

**Performance, direct switch** between the binaries of the starting state (`885896e`) and of this state, the same period, two runs each, one after another
(`SimTool perf game/content --map "Maps/Twilight Flame/Twilight Flame.map" --players 8 --ticks 36000`, 60 Hz; not `scripts/perf-profile.ps1`, and the machine was not measured for
quietness):

| | mean per tick | 99 % | maximum | allocation per tick | collections gen 0/1/2 | objects (max) |
|---|---|---|---|---|---|---|
| start, run 1 / 2 | 943 / 953 µs | 2.58 / 2.59 ms | 44.4 / 51.7 ms | 10.66 KB | 147/17/0, 147/16/1 | 243 |
| after Phase 31, run 1 / 2 | 951 / 959 µs | 2.39 / 2.34 ms | 43.7 / 45.0 ms | 12.07 KB | 169/18/0, 168/19/0 | 249 |

The two states do not play the same match (end hash `A192D2CAF6D15B10` against `370CA4B95D688B3E`), so the comparison is one of typical cost, not of the same work: the mean is
within one percent, the allocation is 13 % higher (not broken down; hulls and the objects of the death lists are more objects, and a group order and a door exit search free cells).

**Not run:** `scripts/perf-profile.ps1` (the quiet profile with its comparison), the mission performance runs, the determinism matrix and the Linux run at this state, the survey
over 320 matches and the distribution comparison 30 against 60 Hz, the commands-only agent, the 16 pairings at 120 Hz, `scripts/acceptance.ps1`. **No human has seen any of it:**
riders walking out of a transport, a group spreading at its destination, a death explosion with its hull - the scene audits measure positions, events and objects, not the picture.

## 45. Phase 24: the session core (11 October 2026)

Stage 2, milestone S2-M7 (`docs/plan/STAGE-2-ENGINE.md`), brief `docs/handoffs/HANDOFF-PHASE24.md`. The phase builds no network. It makes a skirmish session something that can be
handed to another machine: its whole state - the computer players included - is hashed and saved, its initial state follows from one value, its recording can be played with the
computer players deciding again, and its commands pass through a path that does not care in which tick they are executed. §45.1 to §45.4 are the simulation's part; the command
path and the tick source are `docs/PRESENTATION-INPUT.md` §13, the savegame of the game layer `docs/CAMPAIGN-AND-SAVEGAMES.md` §8.

**No behaviour changes.** The world hash, the world snapshot and every run of the reference manifest are what they were (end ticks, winners, end hashes, chains, command counts
of all 50 runs); the one manifest commit of the phase changes 23 of 1,925 fields - the 22 `commandStreamSha256` values and `replayFormatVersion` -, because the replay container
carries one more header field (§45.4). New next to the world hash is the session hash (§45.1).

### 45.1 The computer players are session state (`SkirmishAi.State.cs`, `SkirmishMatchSession.StateHash`)

`SkirmishAi` kept around fifty fields of its own outside every snapshot and hash (register E11 of `docs/MULTIPLAYER-PLAN.md`). `SkirmishAi.Describe(IStateWriter)` now writes
every one of them, `Load(IStateReader)` reads them into an AI that was constructed with the same options over the restored world, and `StateHash()` is the hash over the same
description - one routine for both, like every module of the world, so what is hashed and what is saved cannot drift apart.

- **What is state:** the random stream; the sequence counter; budget, reserve and the placement counter; the army's state (attacking, wave, thresholds, rally point, army
  goal, objective, standing army order); the memories (enemies, warehouses, sightings); the timers and tables (retry, power and science back-off, orders, idle since, site
  watch, boarding, rally set, in-flight production, cleared hints, given-up objectives, bad spots); the scouting state (target, seen cells, unreachable cells); the owner's
  settings (`Objectives`, `CommitEverything`, `ObjectiveReserve`); the counters of `Stats`. **Also state, although they are remembered answers:** what a producer offers and
  which structures a role may build (`_producible`, `_structureCandidates`, `_builderTemplates`) - they are asked of the world when first needed, and
  `SimulationWorld.BuildableOf` can be overridden by a script, so a restored AI must go on with the answer it had.
- **What is not state** is listed in `SkirmishAi.NotState` with its reason (21 entries): the world, catalogue, options and what follows from them; `_tick` (set at the start of
  every update); the five lists an observation rebuilds before a think reads them; three caches that are pure functions of templates; the cache of the hash.
- **Order.** A map or a set is written sorted by its key - nothing acts in its order. A list (`_badSpots`, `_inFlight`) is written in its own order. The one place that
  enumerated a hash table (`_boarding`, to drop expired entries) is now an ordered table; no decision depended on it, and no hash of an AI run changed.
- **Two source tests** keep this true when the class grows (model: "every `IUpdateModule` stores `nextWake`"): *the state of SkirmishAi names every field* - every instance
  field is written by `Describe` and read by `Load`, or it is in `NotState`; an entry that names no field fails - and *SkirmishAi never acts in the order of a hash table* - a
  `Dictionary` or `HashSet` field may only be used through its keyed members and `Count`.
- **The hash is kept** between the updates that observe (every fifth original frame per AI): asking for it in every tick costs a comparison.

`SkirmishMatchSession.StateHash()` is the **session hash**: the world hash, the hash of every computer player in player order (0 for a human), the victory tick, the winner, the
end tick, the system's sequence counter and every human's. It is what two machines compare; the world hash alone would notice a deviation in an AI only when the worlds have
drifted apart. The world hash itself is unchanged and stays what the reference manifest records.

**The other sorted writes of the script VM** (the open point of §44.5) were examined: `ScriptEngine.Describe` and the descriptions of its parts contain no `.Order()` or
`OrderBy` any more - every collection is written in its own enumeration order, and all of them are ordered collections or lists. The member order of a team was the only case.

**Thinking times.** An AI observes every 5 original frames and thinks every 15, shifted by `player mod 5` frames. With eight players at most two think in the same frame
(players 1 and 6, 2 and 7, 3 and 8); never all. A shift by `player mod 15` would separate them all and changes every run with more than four players; it was not made
(measured cost in §45.6).

### 45.2 The match description (`MatchDescription`)

An immutable value with **one binary encoding** (`Encode`/`Decode`, little endian, magic `CNCMATCH`, encoding version 1) and a fingerprint (SHA-256 over the bytes). It holds
everything a machine needs to build tick 0 and nothing else:

| Field | Meaning |
|---|---|
| `CommandTableVersion`, `WorldStateVersion` | the simulation version; a machine with another one refuses the match |
| `TickRate` | 30, 60 or 120; the catalogue must be built for it |
| `MapPath`, `TerrainFingerprint` | the map; the fingerprint covers heights, cells, water, waypoints and every map object and is what is checked |
| `DefinitionFingerprint` | the definitions; checked against the fingerprint the caller passes |
| `Seed`, `AiSeed` | the world's streams and every "random" choice of the description; the computer players' own streams (`AiSeed + (player - 1) * 7919`) |
| `MaxTicks` | the length of the match in ticks of `TickRate` |
| `StartMoney`, `GraceFrames`, `SciencePointIntervalFrames` | the rules; the two times in logic frames of the original, the same game time at every rate |
| `Slots` | per slot: kind (closed, human, computer, spectator), faction or empty for "random", team, colour or -1, start position or -1, AI strength (0 to 2), army composition or -1 |

`Resolve(catalog, terrain, definitionFingerprint)` decides what the description leaves open and returns a `ResolvedMatch`: **random factions and start positions are drawn from
a stream seeded by the description's seed** (factions first, then positions, in slot order) - never by the caller; a random colour is the lowest free index (no draw: only that
every machine shows the same one matters). The slots that take part are numbered from player 1 in slot order; a closed slot and a spectator take no number and no start position.
A computer slot that asks for a strength other than medium is **counted** (`ResolvedMatch.AiStrengthsIgnored`, a note of the session), not obeyed: the built-in AI has one
strength. **The local player is not part of the description** - who plays at this machine is a matter of the presentation.

`SkirmishMatchSession.Start(description, catalog, terrain, mapObjects, checkpointInterval, definitionFingerprint)` is the entry. The older call with options and slots
(`Start(options, catalog, fingerprint, terrain, mapObjects, slots)`) is kept as a shorthand for tools and tests: it writes the description (`Describe`: slot `i` is player
`i + 1` at start position `i`) and starts from it, and it refuses a participant that is not the one the catalogue offers for its faction.

**Deviation from the brief:** `MaxTicks` is a tick count, not game time. The description names its rate, so the value is unambiguous, and the budgets of the tools and of the
reference manifest are exact tick counts that a value in frames could not express at 60 Hz. And the description carries two seeds, not one, because the existing runs use two
independent ones; deriving one from the other would change every AI run.

**Tests:** *Match description* (two tests: encoding and decoding, each of twenty field changes changes the fingerprint; two sessions from the same bytes agree in the session
hash at tick 0 and over 600 ticks, random choices by the seed, numbering, refusals) and `scripts/check-match-description.ps1` (tier 3): one process writes the bytes of a match
with a random faction and random start positions (`SimTool match-describe`), **three further processes** build it from the 292 bytes and print the resolved slots and the
session hash at tick 0 and tick 1,800 (`SimTool match-start`) - one of them started with another tick rate on its command line; the outputs are equal line for line.

### 45.3 The session state (`SkirmishMatchSession.Save` / `Restore`)

`Save()` writes one canonical document (format `cncnew-skirmish-state`, version 1): the tick rate and the two fingerprints, the match description (its bytes and its
fingerprint), the session's counters (winner, victory tick, end tick, the system's and the humans' sequence counters, notes), one state block per computer player, the
**recording so far** (so the continued match still yields its whole replay) and the world snapshot, with the session hash over all of it. `Restore` rebuilds the session and
refuses a document of another version, rate, definitions or terrain, and one whose restored state does not hash to the recorded session hash. `CreateSave()`, which threw since
Phase 10 (§18, "Skirmish savegame: decision"), is gone; that decision is superseded.

**Tests.** *Session state: a skirmish saved at any tick continues identically* (synthetic match, save points at frame boundaries, inside a frame, in a think tick and the tick
before it): every restored session writes the bytes it was read from, has the session hash of the uninterrupted one after every tick, ends alike and yields the same replay
bytes. *The session hash covers the AI*: one remembered value changed in the saved AI block is refused by the integrity check. A check by mutation: leaving one field out of
`Load` makes the continuation test fail at the first restore.

**The acceptance run** is `SimTool session-check` (tier 4): the 16 pairings of the manifest's AI table and its three eight-player maps (five minutes each) as live sessions,
saved at up to six ticks (600 to 66,671; three of the six lie inside a frame at 60 Hz), every save restored and continued to the end of the match.

| | Result (11 October 2026, 60 Hz) |
|---|---|
| runs | 19 of 19 continue identically from every save |
| saves | 77 |
| continued ticks compared | 2.56 million, every one with the session hash of the uninterrupted session |
| largest save | 3.5 MB (eight players on Twilight Flame); two players: 0.4 to 0.8 MB |
| replay of every continued session | byte-identical to the uninterrupted one |

A session of the original AI (`ScriptedSkirmishSession`) had its state in hash and savegame from the beginning and is untouched; one of its slots played by the built-in AI
still makes it refuse to save (not converted - the built-in AI could now be written there too).

### 45.4 The replay (`ReplayFormat` 14, `SkirmishReplay`)

**Format 14:** the header of a replay carries the match description (after the checkpoint interval: u32 length, then the canonical bytes; length 0 for a recording of a bare
world such as the reference match and the stress test). Version 13 is refused.

The content of the file is unchanged: every command, the rejected ones included, ordered by (tick, player, sequence). What changed is what a playback can do with it:

- `ReplayRunner.Run` stays the playback of the **bare world**: it submits every recorded command and needs no AI. It is what the reference manifest verifies.
- `SkirmishReplay.Play(replay, catalog, definitionFingerprint, terrain, mapObjects)` plays the replay **as a match**: the description builds the session, the commands of
  the human players are fed in at their ticks, and the system and the computer players run along and decide again. Their recorded commands are not fed in - they are the
  **audit trail**: what the playback produces in a tick is compared with what the recording holds for it, command by command, by their encoded bytes
  (`ReplayFormat.EncodeCommand`; a command with a list compares by reference otherwise). The first difference is reported with tick, player, the recorded and the played
  command (`ReplayDeviation`), and the playback stops there unless told otherwise.

**Deviation from the brief:** the brief names the commands "of the humans and of the system" as the content that is fed in. The system's commands (start money, start objects,
warehouses, science points) follow from the description and are produced by the session itself, so they are audited like the AI's, not fed in.

**Tests.** *Skirmish replay* (synthetic match of a human who gives group orders against a computer player): the playback produces every recorded command of system and AI,
ends in the world hash and session hash of the recorded match and in the world of the bare playback; with one recorded AI order removed the audit names its tick and player
and stops in that tick; with one human order removed the match is another match; a replay without a description and one for other definitions are refused.
`SimTool session-check` plays the replay of each of its 19 runs this way: no deviation, between 814 and 3,823 commands audited per run.

### 45.5 The pre-check of a command (`SimulationWorld.Check`)

`Check(command)` answers what `Submit` would answer for a command against the world as it is now, without its tick and its sequence number (`Validate` with the envelope left
out: late, sequence zero, duplicate, out of order). It changes nothing - no counter, no event, no pending command, no draw. It is the first stage of the two-stage command
path (`docs/PRESENTATION-INPUT.md` §13); only the answer at submission counts. Test (*Command path: a pre-check changes nothing*): state hash and snapshot bytes are the
same before and after 10,000 pre-checks of accepted and rejected commands, and the pre-check gives the reason the submission gives.

**The order inside a tick.** The world executes a tick's commands sorted by (player, sequence), whatever the order of submission; validation of one command does not depend
on the pending commands of other players. What must be the same on every machine is therefore *which* commands are submitted for a tick, and each player's own order - the
bundle of the command path guarantees both. That the order is not free is shown by a control in the test *the order of two players' commands inside a tick*: two group orders
to one point, executed in the other order, end in another world (a group order takes its free places when it is executed, §44.2).

**Sequence numbers** are assigned at submission by the session, per player, in the order of submission. Every player has one source - a human's orders arrive through the
bundle, a computer player's from its AI, the system's from the session -, so the rule "strictly increasing per player" holds without separate number ranges; in a mission the
human shares the session's counter with the scripts and is numbered at submission like them. Test: *sequence numbers rise strictly per player whatever the sources and the delay*.

### 45.6 Closing measurements (11 October 2026, state `e96d93f`, Windows, Release)

**Tier 4** (`pwsh scripts/test.ps1 -Tier full -KeepGoing`, 4,192 s): 36 of 36 steps ok - clean build 0 warnings; every test program; compat-check 713; content against its
manifest; known vulnerabilities; `Simulation.Tests` at 30, 60 and 120 Hz, with real data (with the rate-equivalence suite, 0 / 0 in ten groups) and the long run; `Game.Tests`
and `Presentation.Tests` with real data at 30, 60 and 120 Hz; `input-manifest check` identical; `reference-manifest check --set full` 50 of 50 identical (a second time in the
tier 2 run after the manifest was written); the three client checks through the new command path (`check-spectator`, `check-dozer-build -Quick`, `check-build-hotkeys`); the
two new steps `check-match` and `session-check`; the three staged scenes. A first tier 4 run was stopped after it found a defect of a new test at 30 Hz (its save points were
tick numbers; `e96d93f` makes them game time) - the tier that found it was run again in full. **Campaign** (60 Hz): reachability victory clean 24 and defeat clean 24;
`audit` 24 of 24 identical on a second run and after save/restore, end probes 13/24 and 15/24; `drive` (120,000 ticks) victory 24/24 and defeat 24/24 - the values of before
the phase (§44.6).

**Performance: `scripts/perf-profile.ps1`**, the whole profile once with the binaries of the state before the phase (`bab51c3`) and once with this state, one after the other
in the same hour, CPU load 2 % before every run, nothing else running (`scripts/perf-compare.ps1`; 60 Hz; matches of 30 game minutes with their replay, missions of 100 and
300 game seconds). **Every run ends in the same final hash on both sides**, so the two columns measure the same work:

| Run | Mean per tick before / after | Change | 99 % before / after | Maximum before / after | Allocation per tick |
|---|---|---|---|---|---|
| small, 2 players | 214.6 / 215.9 µs | +0.6 % | 0.66 / 0.66 ms | 43.9 / 44.9 ms | 2.4 KB |
| medium, 2 players | 266.9 / 266.2 µs | -0.2 % | 0.88 / 0.88 ms | 43.4 / 45.0 ms | 2.4 KB |
| medium, 4 players | 495.7 / 496.9 µs | +0.2 % | 1.18 / 1.23 ms | 44.0 / 45.6 ms | 5.3 / 5.2 KB |
| large, 2 players | 164.4 / 169.4 µs | +3.0 % | 0.72 / 0.59 ms | 43.5 / 45.7 ms | 1.8 KB |
| large, 4 players | 363.9 / 369.9 µs | +1.6 % | 1.03 / 1.05 ms | 43.9 / 46.0 ms | 3.6 KB |
| large, 8 players | 849.7 / 837.2 µs | -1.5 % | 2.22 / 2.12 ms | 140.1 / 148.7 ms | 8.5 KB |
| `MD_USA05`, 100 s / 300 s | 2,030 / 2,086 and 1,831 / 1,845 µs | +2.8 % / +0.7 % | 7.9 / 8.3 and 4.2 / 4.2 ms | 127 / 139 and 126 / 126 ms | 3.1 / 2.4 KB |
| `MD_GLA05`, 100 s / 300 s | 2,951 / 2,956 and 2,619 / 2,623 µs | +0.2 % / +0.2 % | 11.6 / 11.7 and 7.1 / 7.0 ms | 146 / 139 and 127 / 129 ms | 2.8 / 2.2 KB |

The phase is not measurable in the tick: the means lie within -1.5 % and +3.0 % of each other, with both signs, which is the scatter between two runs of the same binary
(§44.6 measured the same run twice at 943 and 953 µs). The tick maximum of the eight-player run (140 to 149 ms, decision D-3) is unchanged and is what a tick driver that
waits for bundles has to withstand.

**The cost of the computer players with eight of them** (the bottleneck named by the brief; the large eight-player run above): the AI phase of the session takes 32.8 µs
per tick on average for all eight (3.9 % of the tick; before the phase 33.2 µs), median 9.3 µs, 95 % 137 µs, 99 % 219 µs, **maximum 33.7 ms** in one tick, and allocates
4.0 KB per tick - the largest single share of the run's 8.5 KB. An AI observes in one tick of ten and thinks in one of thirty at 60 Hz; at most two of the eight think in
the same frame (§45.1). **The session hash** costs nothing measurable beside this: every AI keeps its hash between its observations. **Saving and restoring** (measured inside
`session-check` with three runs in parallel, so upper bounds): the slowest save of a two-player match took 25 ms, of an eight-player match 12 ms (3.5 MB); the slowest
restore 49 ms.

**Not run:** the determinism matrix and a Linux run at this state; the survey over 320 matches and the distribution comparison 30 against 60 Hz; the commands-only agent; the
16 pairings at 120 Hz; `session-check` at 30 and 120 Hz (the synthetic session tests run at all three rates); `scripts/acceptance.ps1`; the performance of the game layer
(`sync-perf`) with the command path in it. **No human has seen or done any of it:** saved a skirmish from the menu, loaded one from the load list, or played with
`--input-delay-ticks`.

## 46. Phase 25: what the network layers needed from the simulation (11 October 2026)

The protocol, the relay and the client are `docs/NETWORK-PROTOCOL.md`. The simulation changed in four small places, none of them behaviour:

- **`MatchDescription.SimulationId` (encoding 2).** The description states the build it was opened for: the commit and "modified" flag of whoever opened the match
  (`CnCNew.Net.BuildIdentity`; the simulation does not know its own build). `Resolve(..., simulationId)` refuses a stated identifier other than the caller's. **Empty means
  "not stated"** - a match that never leaves its process -, which is what `SkirmishMatchSession.Describe` writes for tools, tests and the reference manifest; otherwise every
  recorded description, and with it `commandStreamSha256` of 20 manifest runs, would change with every commit. Encoding 1 is refused; the session state (`Save`) is version 2.
  **Manifest (`e0d6e01`, one cause):** 20 of 1,925 fields, the `commandStreamSha256` of the runs whose replay carries a description; every result quantity of all 50 runs
  unchanged.
- **One command on its own:** `ReplayFormat.DecodeCommand` and the offsets of tick, type, player and sequence in the bytes of a command - the relay stamps player and tick
  there without decoding (`CnCNew.Net.CommandWire`).
- **`SkirmishReplay.Play(audit: false)`** for a recording that holds the humans' commands only (the relay's: it never sees a computer player or a system command); the
  playback then produces system and computer players without comparing them, and its final session hash is what is compared.
- **`SkirmishMatchSession.AiOptionsOf`** (the options of the computer player of a slot, also what a command generator of the multi-client probe takes) and
  **`HumanCommandCounts`** (what a relay continues its order numbers from when a saved match is resumed).

The rule of §45.5 holds across machines: the sequence number of a command is assigned by the session at submission. On the wire it is 0; the relay's order number says in
which order a player's commands are submitted, and that order - the bundle's - is the same on every client.

## 47. Phase 26: a player leaves a running match (11 October 2026)

A multiplayer match must go on when one of its players is gone, and what "gone" means for the world has to happen in the same tick on every machine. It is therefore a
**command**: `PlayerLeaveCommand(Player, Sequence, Tick, TransferToAlly)`, command type 34 (`CommandTypes.PlayerLeave`). **Command table 17, replay format 15** (a bool after
the command header); the reference manifest was rewritten for this one cause (`4cbf535`: the two version fields and the `commandStreamSha256` of the 22 runs that record a
stream - of 1,925 fields 24 -, no result quantity). `WorldSnapshot` 10, `MissionSnapshot` 3, `SaveGame` 3 and the skirmish session state 2 are unchanged: a pending command is
described by its fields, as every command is.

**Source.** `GameLogic::logicMessageDispatcher`, case `MSG_SELF_DESTRUCT` (`GameLogicDispatch.cpp`); `Player::killPlayer` and `Player::transferAssetsFromThat`
(`Player.cpp`); `Team::killTeam` and `Team::evacuateTeam` (`Team.cpp`); the two senders: `Network::quitGame` (the menu; argument TRUE) and
`Network::processDestroyPlayerCommand` (a disconnected player; argument FALSE).

**What it does** (`SimulationWorld.PlayerLeave.cs`):

- *With* `TransferToAlly` (the original's quit through the menu): the players are walked in list order for the first one that is **allied with the leaver in both
  directions** and **not defeated** (`IsPlayerDefeated`, the original's `hasSinglePlayerBeenDefeated`). It takes over every object of the leaver (`TransferOwnership`, the
  path a script's transfer and a capture use: energy and battle-plan bonuses move with the object) and **all its money**. Then `killPlayer` runs "just to be safe", as in the
  original - on what is left, which is nothing unless an object could not change hands.
- *Without* it (the original's destroyed player after a disconnect), or when no such ally lives: `Player::killPlayer` - first every living object of the player that holds
  riders puts **all of them out at once** (`Team::evacuateTeam` → `removeAllContained`; not through the door, which is for a living transport's order), then **every object
  that is still the player's and not already dead is killed** (`Object::kill`: unresistable damage with the kill flag, so die modules, slow deaths and death weapons run). A
  **`TECH_BUILDING`** is not killed but handed to the neutral player. The leaver's money stays where it is.
- The command is **the leaving player's own** - the original sets the message's player index to the destroyed player. So it takes the next sequence number of that player,
  a replay feeds it like any order of a human, and the system's own sequence (science points) is untouched. The system player cannot leave (`NotPermitted`); an unknown
  player is `UnknownPlayer`.

**Who gives it.** In a network match only the relay, inside a tick bundle (`docs/NETWORK-PROTOCOL.md` §14.2); a client's intent of this type is refused. Nothing in a local
game gives it: the local skirmish has one member, and when it leaves the room is abandoned. The quit menu of the client does not send it yet (Phase 29).

**Deviations and what is not modelled.** The original keeps beacons out of the transfer and destroys them directly in `killTeam` (they are "effectively dead"); this
simulation has no beacons. `m_isPlayerDead` ("OCLs don't ever again spawn useful units for us") has no state of its own here: creation lists that require a living player
ask `IsPlayerDefeated`, which is true once everything is dead. The original's `becomingLocalPlayer`/control-bar part of `killPlayer` is presentation and belongs to the client
(Phase 29: what a player sees whose own seat was dropped).

**Tests** (`Tests.PlayerLeave.cs`): riders are put out and everything of the player dies, a tech building stands and belongs to nobody, the others and the leaver's money are
untouched, the pending command survives a snapshot and the live and the restored world agree tick by tick, the command round-trips through the replay format; with the
transfer the first living two-way ally owns everything and has the money, a one-way "ally" gets nothing, a rider that changed hands stays in its transport; with a dead ally
everything dies and the last player with objects has won. In the network tests the same command arrives through a bundle after a leave, a lost line, a frozen and a too slow
client, and the relay's recording with it replays into the same session hash (`docs/NETWORK-PROTOCOL.md` §15).

## 48. Phase 27: the state hash taken apart (12 October 2026)

Phase 27 (`docs/NETWORK-PROTOCOL.md` §18, `docs/NETWORK-DIAGNOSTICS.md`) needed two things from the simulation, and neither changes a result: no reference value, no
format version and no hash moved (`CommandTypes.Version` 17, replay 15, `WorldSnapshot` 10, skirmish session state 2).

**The parts of the hash** (`SimulationWorld.Diagnostics.cs`). The world composes its state hash from three sums it keeps up to date incrementally - objects, timers,
pending commands (§37) - and the hash of everything else. `SimulationWorld.StateHashParts()` returns exactly these four values by name (`world.objects`,
`world.timers`, `world.commands`, `world.global`); `SkirmishMatchSession.StateHashParts()` adds one part per computer player (`ai.player<N>`, the AI's own state hash of
§45.1) and `session.counters` (victory tick, winner, end tick, the sequence counters). `SimulationWorld.CopyObjectHashes` copies the hash every object contributes, in
id order. All three only read; the state hash is composed as before. Two sessions whose hash differs differ in at least one part - that is what the relay names at a
desync, and the object table is what `NetTool desync-diff` names the object by.

**A state that travels.** A member that joins a running match is handed the session state of another member: `SkirmishMatchSession.Save` and `Restore`, the pair of
the skirmish savegame (§45.3), unchanged. What is new is who checks it: the relay passes the state on only when its stated session hash is the one all clients reported
for the tick before it, and the receiver's `Restore` checks definitions, terrain and the state's own hash as it does for a file. The state is taken between two ticks.
`Restore` therefore must reject every damaged document with one of its named exceptions - a client must not crash on a state from the wire; the test "a damaged state
is turned away" feeds it one.

**Not built for it:** no second, binary state format (measured and not needed, `docs/NETWORK-DIAGNOSTICS.md` §7); no script state among the parts (a
`ScriptedSkirmishSession` is not played by several clients yet).

## 49. Phase 28: what a player's command may say (12 October 2026)

Phase 28 opens the server's port to everybody (`docs/NETWORK-PROTOCOL.md` §20, `docs/DEDICATED-SERVER.md` §9), and with it the simulation is handed commands by
somebody who means harm. Two small rules of the simulation follow; **no reference value, no format version and no hash moved** (`CommandTypes.Version` 17, replay 15,
`WorldSnapshot` 10, skirmish session state 2; input manifest identical, quick reference set 9 of 9 at commit `6cc6501`; the full set of 50 runs, the input manifest and the
rate-equivalence suite identical in tier 4 at the end of the phase).

**A coordinate limit (decision D-5, open since Phase 22).** `Fixed` has a range of about ±5.5 · 10¹¹, and a distance is compared by its square: a command whose
target lay farther than about 524,000 units away made the movement end with an `OverflowException` - on every machine alike, so not a desync but an exception in
every simulation of a match (reported in §38, never fixed). `SimulationWorld.Check` and `Submit` now reject, with `CommandRejection.InvalidArguments`, a command of a
player that states a point beyond `SimulationWorld.CommandCoordinateLimit` = **131,072 units** on any axis, either way, or an angle beyond
`CommandAngleLimit` = **1,024 radians**. With both ends of a distance inside the limit its square stays a factor of sixteen below the range. The largest map of
the game is a few thousand units wide; no order a client can give by a click comes near the limit, and an order *to* the limit is an order like any other (it is
then the map's edge that stops the unit). Checked are the points of `SetMoveTarget`, `AttackMove`, `GroupMove`, `Pursue`, `UseSpecialPower`, `Construct` (and its
angle), `SetRallyPoint`, `Evacuate` and `Spawn` (and its height and angle). **The limit binds players, not the system:** scripts and fixtures place objects where
they like, and no manifest run gives such an order - which is why nothing moved. The check is part of the pre-check (`docs/PRESENTATION-INPUT.md` §13), so the
client's receipt already says no.

**Which commands are orders.** `CommandTypes.IsPlayerOrder(code)` is true for the 24 of 34 command types a player gives and false for the ten that are the
system's or a fixture's: `Spawn`, `Destroy`, `Damage`, `Kill`, `SetTimer`, `CancelTimer`, `GiveMoney`, `GiveSciencePoints`, `SetRelationship` and `PlayerLeave`.
The simulation itself still accepts `Spawn` and `Destroy` from a player for its own objects - tests and tools build their scenes that way -, which over a wire
would be a free army; so the list is asked where commands arrive from outside: `CommandWire.IsPlausible` refuses an intent of another type, and
`CommandWire.TryDecodeIntent` is how a client decodes the commands of a bundle - one that does not decode is skipped by every client alike and counted
(`LockstepDriver.UndecodableCommands`), instead of throwing in the middle of a tick.

**Tests** (`Tests.CommandLimits.cs`, "Command limits"): each of the nine commands beyond the limit is rejected in the pre-check and at submission and changes
nothing; a move to the limit itself is accepted and one step beyond it is not; the system may spawn beyond it; exactly 24 types are orders and the ten named ones
are not. In the network tests ("Hostile members", "Fuzz, commands") a member sends such commands through a relay: 4.5 million changed real orders, of which 77,028
were accepted and simulated, without an exception in the pre-check, the submission or the ticks after.

**Not examined:** values of a command that are no coordinates (a template name, a count, an option) are the handler's to reject as before, and the fuzzing reaches
the handlers only through orders it can still make valid; script actions and the system's commands are not bounded - they are not reachable from a wire.

## 50. Phase 32: two modules for the patch layer (15 October 2026)

Two engine modules of the original that the shipped data never uses, implemented because the community patch GeneralsGamePatch uses them (`docs/PATCH-LAYER.md`).
The module catalogue has 207 types since (205 before); the original content's objects, manifests and reference values are unchanged by them.

- **`StatusBitsUpgrade`** (`StatusBitsUpgrade.cpp`, `StatusBitsUpgrade::upgradeImplementation`): an upgrade module with the masks `StatusToSet` and `StatusToClear`
  (object status names, `NameTables.ObjectStatus`). When its upgrade arrives the object's status gets the first mask and then loses the second - a bit in both
  ends cleared, as in the reference. Nothing is undone when the upgrade goes away (the reference has no downgrade). The patch uses it to make drones
  unselectable and to mark riders (`STATUS_RIDER1`). Test: `PatchModuleStatusBitsUpgrade`.
- **`SabotageSupplyDropzoneCrateCollide`** (`SabotageSupplyDropzoneCrateCollide.cpp`, `isValidToExecute`, `executeCrateBehavior`): a saboteur entering an enemy
  `FS_SUPPLY_DROPZONE` restarts the zone's `OCLUpdate` timer (`OCLUpdate::resetTimer`, new `OCLUpdate.ResetTimer`: the next drop is a whole new delay away) and
  moves up to `StealCashAmount` from the owner to its own player, clamped to what the owner has; radar blip, EVA and floating texts are presentation. It shares
  the common sabotage checks of this port (alive, enemy, the unit's goal object) and the adopted fix `no-sabotage-of-unfinished-or-sold` (`docs/GGC-FIXES.md`).
  Test: `PatchModuleSabotageSupplyDropzone`.

## 51. Phase 33: tactics profiles and strengths of the built-in AI (15 October 2026)

The built-in `SkirmishAi` takes two options per computer slot, both part of the match description (encoding 3) and of the AI's session state:

- **A tactics profile** (`AiTactics`: balanced, rush, turtle, eco; `SkirmishAi.Profiles.cs`, `TacticsPlan`) - the numbers of §35, §36 and §42 that were constants: the army size
  of the first wave and its growth after a wave falls apart, supply carriers per center, from which army size a second and a third supply center are planned, the
  money above which a second barracks is planned, the base defences wanted, the radius of the defence reaction, and for rush the army drawn among the cheap
  candidates (at most twice the cheapest price). **Balanced holds the old constants** and is the default: a slot without a profile and a balanced slot play the same match.
- **A strength** (`AiStrength`: easy, medium, hard; `StrengthPlan`) - how often the AI decides (every 10, 15 or 30 frames of the original: easy is hasty, hard deliberate - measured, this AI plays weaker the more often it decides), how long it remembers a
  mobile enemy out of sight (10, 30, 60 s) and a sighting toward the enemy's force (1, 3, 6 min), whether it weighs its army against that force (easy: no, a
  uniform draw), and whether the map's start positions are attack targets before anything has been seen (easy: no). **No strength changes a rule of the
  simulation** - no money, damage, build time or vision a human does not get. Medium is exactly the old values.

`MatchSlotDescription.AiStrength` is obeyed since this phase (it was counted and not obeyed, §45); an unknown strength or profile is refused by
`MatchDescription.Resolve`. The AI writes `tactics` and `strength` into its state only when they are set and refuses a state written for others; the skirmish
session state is version 3. Tests: `SkirmishTacticsProfilesBehave` (with the same money and seed rush marches first, then balanced, eco and turtle; only the turtle
raises base defences before it has an army), `SkirmishStrengthsDecideAtTheirPace` (60, 40 and 20 decisions of easy, medium and hard in 20 s of game time at every rate; easy never weighs its
army), `SkirmishProfilesTravelWithTheMatch` (balanced = no profile; a restored session continues hash-identically), each at 30, 60 and 120 Hz. The reference
manifest moved in the command-stream hash of its 20 AI runs only (the description's encoding), no result quantity.

**The rule "no standing still"** (`131f590`, manifest `80fb676`): an army at rest that has not grown for the profile's patience (2 min balanced and eco, 1 min rush,
4 min turtle) marches with what it has, as long as that is a third of its threshold. It changes the default AI: in the 320-match survey 307 instead of 290 matches
are decided, and in the manifest the two stalemated pairings end with a winner (16 of 16 decided); no winner of a decided pairing changed. The survey and its
thresholds: `docs/SKIRMISH-AI.md`.

## 52. Phase 34: the movement profile, and the fidelity corrections F-34, F-38, F-39 (16 October 2026)

### 52.1 The movement profile `movement-budget`

An option of the rule profile (`RuleProfile.Movement`, `MovementProfile.Budgeted`, name `movement-budget`, `--rules movement-budget`, combinable with fixes), folded into
the definition fingerprint like a fix - so a match description, a replay, a save and a room carry it, and a machine with the other profile refuses them. The original
profile is the default (D-3 open) and keeps the reference and input manifests: the profile is a field-less subtype of `RuleProfile`, so the input manifest, which
describes the catalog field by field, is unchanged for the original. Under the profile:

- **The path queue** (`SimulationWorld.PathQueue.cs`): a ground unit asks the world for its path (`RequestPath`) instead of searching itself, and takes the answer at
  its next decision (`TryTakePath`); while it waits it is `WaitingForPath` - it keeps a path it still has, or stands for its whole frame (it decides again in its next
  frame, not in the middle of it), and its order is never dropped. A detour around stationary blockers is an arriving-only question; the old path stays until it
  comes. The queue is worked once per logic frame of the original, at the end of the update phase of the tick on the frame boundary, with **60,000 expanded cells per
  frame** (`PathCellBudget`; counted work, never time; about 4.5 ms at 75 ns a cell). A search that does not fit pauses (`NavigationGrid.Slice`: begin, continue to a
  number of expansions, finish) and continues in the next frame; when a footprint changed meanwhile (`ObstacleSum`) it begins again; a restored world, which has no
  search memory, runs the paused search silently up to where it was. Queue, answers and paused searches are state, written only when the queue is not empty.
- **The same at every rate:** a decision above 30 Hz belongs to the frame whose boundary ends the object's own frame (`DecisionFrame`: at 60 Hz ticks 2f - 1 and 2f
  are frame f); the requests of a frame are worked in the order of their requester's id, not in the order they came (which above 30 Hz follows the phases of the
  objects' frames); at the boundary every decision of the frame has been made, as after the one 30 Hz tick.
- **The shared group path:** among the requests of one frame, a member of the same group order (`PathQuery.Group`, set by `GroupMoveCommand`) as an earlier member,
  with the same surfaces, a goal within 8 cells and a start within 24 cells does not search: it walks straight to the first point of the leader's path it can reach,
  follows it, and leaves it straight for its own goal. When either straight walk is not clear, or the leader's search does not arrive, it is queued with a search of its
  own. Units that are not of one group order never share (a first version shared among any units near each other; it made an AI match differ between the rates).
- Not queued: aircraft, the first leg of a factory exit (`_viaPending`), and the computer player's placement check (`SkirmishAi.Reachable`, which asks the grid
  directly for an arriving-only path - the same answer, no state).

Tests, each at 30, 60 and 120 Hz: `MovementBudgetSlicedSearchEqualsTheWholeSearch` (2,160 searches on six random maps in slices of 1 to 400 expansions, a quarter
with the memory lost between slices, equal to the search in one piece), `MovementBudgetQueuesWaitsAndArrives` (eight searches that do not fit a frame: no frame works
more than the budget, work only on a frame boundary, waiting units keep their orders, all arrive; the original profile does not use the queue),
`MovementBudgetSharesTheGroupPath`, `MovementBudgetSameAtEveryRate` (eight searches and a group order arrive in the same frames at 30, 60 and 120 Hz),
`MovementBudgetContinuesFromASnapshotMidSearch` (a world saved while a search is paused continues hash-identically for 600 frames).

### 52.2 The fidelity corrections of the original profile

Each a change of the original's own behaviour, with its manifest commit when the manifest moved:

- **F-34** (`c2e48e2`, manifest `3d0d276`): `HealContain` lets a healed occupant out through its door like every `OpenContain` rider (`HealContain::update` ->
  `removeFromContain` -> `OpenContain::exitObjectViaDoor`), cycling through `NumberOfExitPaths` (parsed, default 1); `ExitObjectViaDoor` moved into
  `ContainModuleBase`. The input manifest moved in two entries (the new field), the reference manifest in 43 fields of 5 runs (four AI pairings end earlier, the same
  winners; the eight-player run gives other commands). Test `HealContainReleasesThroughTheDoor`.
- **F-38** (`f3c4009`, `d58d9d0`, manifest `3b6fa80`): entering a container is no longer decided in two dimensions (`AIEnterState::update`): a boarder on the ground
  gives up on a container that something airborne carries; at the end of its approach it waits while the container is above the terrain and the boarder is not, and
  stays awake while it waits; the touch on the way in needs an overlap in height (the partition manager's collision is three-dimensional). `AIEnterState::onEnter`
  tells the container that somebody wants in, and an idle Chinook (no move, not docked, supply machine idle) lands while a living object's enter order aims at it and
  takes off again when none does (`ChinookAIUpdate::update`, `OpenContain::hasObjectsWantingToEnterOrExit`) - the port lacked that as well; the wanters are read from
  the boarders' enter orders, not kept. The reference manifest moved in the chain hashes of two mission runs (`MD_CHI05`, `MD_USA01`; end state, outcome and tick
  unchanged). The Chinook test now orders a grunt into an idle hovering Chinook.
- **F-39** (`a13d5de`): rider and combat bike exchange their levels both ways (`RiderChangeContain::onContaining`/`onRemoving`): the bike takes its rider's level - also
  a lower one - and the rider's experience goes to nothing; when the rider leaves, after the death check, it takes the bike's level and the bike's experience goes to
  nothing; both only for a rider that a rider line names. New world services `SetVeterancyLevelOf`, `SetExperienceAndLevel`. **Neither manifest moved** (measured:
  the full set written with a SimTool at `d58d9d0` equals the one at `3b6fa80` field for field). Test `RiderChangeContainSwapsLevels`.

After them the three community fixes they blocked are adopted behind the rule profile (`3bd8ceb`, `docs/GGC-FIXES.md` 3.1d): `door-exit-to-rally` (17),
`bike-trainable-from-rider` (18; `Entity.IsTrainable`, an override saved only when set), `enter-needs-vertical-overlap` (19).

## 53. Phase 39: fidelity package B (27 October 2026)

The findings F-06 to F-10 and F-43 of the fidelity register (`docs/plan/STAGE-1-PORT.md` §3), each read in the original's source and either fixed - one cause, one implementation
commit, one manifest commit, a test that fails on the code before at 30, 60 and 120 Hz (`tests/CnCNew.Simulation.Tests/Tests.FidelityB.cs`) - or named as the original's own
behaviour. Stage 1 work: the yardstick is the original's function. **No human has seen any of it.**

### 53.1 F-08: `QuantityModifier` (`ProductionUpdate::queueCreateUnit`, `ProductionUpdate::update`)

`queueCreateUnit` gives a production entry the count of the first `QuantityModifier` whose template exists and is the ordered one (`isEquivalentTo`); the order is paid once. When the
entry is finished, `update` tries `getProductionQuantityRemaining` exits in one frame: each that the exit lets go (`reserveDoorForExit`) makes a unit and counts it
(`oneProductionSuccessful`); the entry stays at the head of the queue, "overbuilding", until all have left. `QueueProductionExitUpdate` closes its door for `ExitDelay` after each
unit ("Mainly for the multiple produced Red Guard. Make them come out one at a time"), so the second Red Guard leaves 300 ms after the first. `cancelUnitCreate` deposits the
whole cost, also when one of the units has left. Built: `ProductionData.QuantityModifiers` (parsed from the opaque field as `parseAppendQuantityModifier` reads it, a name the data
does not define matches nothing, as `findTemplate` returning null does), `ProductionEntry.Quantity`/`Produced` (written to the state only when not 1 and 0). Equivalence by
reskin (`m_reskinnedFrom`) is not modelled: no shipped modifier names a reskinned template. Data: the Chinese barracks of the four China factions (`ChinaInfantryRedguard 2`,
`Nuke_`, `Tank_` and the Boss's) and `Infa_ChinaInfantryMiniGunner 1` (no change). Implementation `b42a210`, manifest `c5fc4aa`: the reference manifest moved in the 16
runs with a China faction (12 pairings end at another tick, Armored Fury China-GLA changes its winner from GLA to China, Alpine Assault America-China becomes a
stalemate - 15 of 16 pairings decided); the 27 mission runs, the stress test, the reference match and the sweep are unchanged; the input manifest moved in the
template catalogue only (the new field). Test `FidelityQuantityModifier`.

### 53.2 F-09: the unit comes into being when its door is open (`ProductionUpdate::update`, `updateDoors`, `setHoldDoorOpen`)

The original reserves a door for the finished unit (`reserveDoorForExit`), opens it (or keeps it open, or pops a closing door back to waiting open) and makes the object only when
the door waits open (`door->m_doorWaitOpenFrame != 0`) - more than `DoorOpeningTime` after it began to open (`updateDoors`, strict comparison). The port made the unit at
completion and ran the door in the presentation `DoorOpeningTime` ahead. Now the simulation keeps the four doors (`ProductionUpdate.Door`: the tick each phase began, the hold of
the airfield and when it was released) and the construction-complete phase; a transition carries its own tick, so a sleeping module catches up exactly as one updated every frame;
"more than" is one logic frame of the original at every rate. The exit modules answer `IProductionExit.ReserveDoor` (one door when free; the airfield the first free space, whose
door is its number, and none needed for `PRODUCED_AT_HELIPAD`), `ClaimExit(world, produced, door)` and `UnreserveDoor`; `ParkingPlaceBehavior` holds a space's door open while an
aircraft has the space (`reserveSpace`, `releaseSpace`, `purgeDead`, and the new aircraft from `exitObjectViaDoor`). A release counts until the frame after it (what releases a
space, an aircraft, is updated after the airfield). The presentation (`StructureConditionTracker`) reads the doors from the simulation instead of imitating them; a factory's unit now
appears `DoorOpeningTime` and one frame after the same order at a factory without a door (31 frames for one second).
Implementation `08def33`, manifest `60ca6d5`: 37 of 50 runs moved - the 16 pairings, the four eight-player runs, the reference match and the sweep in their
results, 15 mission runs in their hashes only (the doors are state; every result quantity of the 27 missions is equal). All 16 pairings are decided again (Alpine
Assault America-China won by China), Death Valley America-China changes its winner (America -> China); the reference match is won by FactionGLA at tick 51,660
(`3E2BFE48E3E4F02D` / `ED8B4247184A9F13`; it was tick 32,520). The input manifest did not move. The presentation's door tests now follow the original's timing (each
phase one frame more than its time; `CONSTRUCTION_COMPLETE` re-armed in the loop that makes the unit, so it lasts twice its duration and a frame). Test
`FidelityUnitAtOpenDoor`. The airfield's reservation at queue time followed as F-45 (§53.8).

### 53.3 F-07: a clip without reload time (`Weapon::getStatus`, the attack state)

The original's weapon is ready again in the frame of the shot that emptied a clip without reload time, but its AI fires once per frame; the port fires one shot per command, and at
30 Hz a second `FireWeaponCommand` in the same tick fired again (above 30 Hz the port already waited a frame). `ConsumeShot` now makes such a weapon ready in the next frame at
every rate. Nothing else is different at 30 Hz: every other attempt comes a frame later anyway. Implementation `6968ff4`; **neither manifest moved** (both are 60 Hz runs,
where the port already waited a frame; measured with the full set). Test `FidelityNoReloadClipFiresOncePerFrame`.

### 53.4 F-06: `SupplyMachine` over a player's command - the original's behaviour

`SupplyTruckWantsToPickUpOrDeliverBoxesState::update` orders the dock without asking what the truck does (`aiDock(..., CMD_FROM_AI)`, `SupplyTruckAIUpdate.cpp`), and
`StateMachine::updateStateMachine` runs a state's `update` before its transition conditions; `GameLogic::update` processes the frame's commands before the object updates. A player's
command that arrives in the frame after the machine entered "wanting" is therefore overwritten in the original as well. Not changed; the register names it as the original's.

### 53.5 F-10: `<This Team>` in a mission's team scripts (`ScriptEngine::executeScript`, `getTeamNamed`)

`executeScript` keeps `m_callingTeam` and sets only `m_conditionTeam` for the instances its conditions name; `getTeamNamed(THIS_TEAM)` returns the calling team first. The mission
VM cleared the calling team (the original-AI sessions had the two variables since Phase 23), so a team's create script whose conditions name no team had no `<This Team>`
(`TEAM_HUNT <This Team>` did nothing). Now one rule for both. Implementation `83510c7`, manifest `e62d747`: 11 mission runs moved (the nine Generals' Challenge boss
runs, `MD_CHI05`, `MD_GLA03`), every one with its outcome and its end; nothing else moved. Test `FidelityThisTeamInMissionCreateScript` (a team's create script kills
its own team).

### 53.6 F-43: upgrades take their build time (`ProductionUpdate::queueUpgrade`, `update`)

The port completed an upgrade in the purchase command; the original queues it in a producer's queue for its build time (`UpgradeTemplate::calcTimeToBuild`: `BuildTime`
seconds of logic frames), pays it when queued, completes it when its time is over (`update`, `PRODUCTION_UPGRADE`: the player's upgrade, or `us->giveUpgrade` for an
object upgrade) and refunds it in full on cancel (`cancelUpgrade`). Built (`SimulationWorld.Upgrades.cs`, `ProductionUpdate.EnqueueUpgrade`):

- A `PurchaseUpgradeCommand` names the producer: the object itself for an object upgrade, the building for a player upgrade (the control bar's button names the
  selected building - `MSG_QUEUE_UPGRADE` goes to the selected group); without one the world takes the first of the player's buildings, in id order, that can research it
  now (`AIPlayer::buildUpgrade` walks the build list - the order is the one difference, named). A script's upgrade button is researched at its carrier
  (`Object::doCommandButton`).
- The checks, in `AIGroup::queueUpgrade`'s and `queueUpgrade`'s order: the money (`canAffordUpgrade`), owned (`hasUpgrade` / `hasUpgradeComplete`), in production - a
  player upgrade in any of the player's queues (`hasUpgradeInProduction`), an object upgrade in the producer's queue (`isUpgradeInQueue`) - the new rejection
  `UpgradeInProduction`, a producer without the button (`Object::canProduceUpgrade`, every command set a `CommandSetUpgrade` can switch to counts) or without a queue,
  a full queue. `affectedByUpgrade` (an object upgrade needs a module that reacts to it) is not checked, as before.
- The command bar shows an upgrade in the queue with its button's picture and progress and follows `ControlBarCommand.cpp`: a full queue restricts, owned or in
  production shows like "cannot afford" (`ButtonReason.InProduction`), then the money. The observer's research tab lists what is in research with its building and
  progress (`SpectatorPlayerPanel.Researching`). The original AI's queue scan skips upgrade entries.
- Tests that switch on a module through an upgrade grant it directly (`Feeder.GrantUpgrade`, the world's `CompleteUpgrade` as a script's grant); `Phase5Economy`
  keeps the purchase rules a fixture without a producer can show.

Implementation `400ea98`, manifest `f678994`: 16 mission runs moved, 6 of them in their results (`GC_ChemGeneral` against two generals, `GC_SuperWeaponsGeneral` against the Demolitions general, `MD_CHI05`,
`MD_GLA05`, `MD_USA05`: scripted upgrades take their time, a few script orders are refused while one is in production), every run with its outcome and its end;
the skirmish runs are unchanged (the built-in AI buys no upgrade). Test `FidelityUpgradesTakeTheirBuildTime`; the rate-equivalence suite's upgrade cases (economy group)
measure the research time now (29.97 s for a 30 s upgrade) and are equal at 30, 60 and 120 Hz.

### 53.7 F-44: a producer that dies refunds its queue (`ProductionUpdate::onDie`)

Found while reading `ProductionUpdate.cpp` for F-43: `ProductionUpdate` is also a die module in the original (`getDie`), and `onDie` calls
`cancelAndRefundAllProduction` - the owner gets back everything the dying building had queued, units and upgrades alike (each through `cancelUnitCreate` /
`cancelUpgrade`, so the full cost). The port refunded on a sale (`BuildAssistant::sellObject`) and a defection only; a destroyed factory kept the money of its queue.
`ProductionUpdate.OnDie` now does what `onDie` does. Implementation `81918dc`, manifest `c7f3def`: 9 runs moved, 47 of 50 keep their outcome and their end; two pairings end later with the same winner (Alpine Assault America-China, Bitter Winter ChinaTank-AirForce), and **the retail reference match no longer ends**: the refunds of its dying factories keep seven factions alive with two objects each, and it runs to its limit of 432,000 ticks as a stalemate (deterministic: checkpoints, a fresh run and a continuation from a snapshot identical). Whether to choose another driver seed for it, as Phase 22 did, is left to its own commit (Phase 40). Test `FidelityDeadProducerRefundsItsQueue`.

### 53.8 F-45: an airfield reserves its spaces when it queues (`ProductionUpdate::queueCreateUnit`, `ParkingPlaceBehavior::shouldReserveDoorWhenQueued`)

Found while reading `ProductionUpdate.cpp` for F-09: `queueCreateUnit` asks every `ParkingPlaceBehavior` of the producer whether it reserves a door when an aircraft
is queued (`shouldReserveDoorWhenQueued`: anything but `PRODUCED_AT_HELIPAD`) and reserves the space then; `BuildAssistant::canMakeUnit` refuses the order when no
space is free (`CANMAKE_PARKING_PLACES_FULL`). The port queued any number of aircraft and held the finished one at the head of the queue until a space was free. Now the
queue order reserves the space (`ParkingPlaceBehavior.ReserveDoor`, the entry keeps its door), a full airfield refuses it (the new rejection `ParkingPlacesFull`; the
command bar shows `ButtonReason.ParkingFull`) and a cancelled order gives its space back (`unreserveDoorForExit`). The air test of a full airfield follows the original.
Implementation `8f644ff`, manifest `6614543`: no result quantity of the 50 runs moved (no run queues more aircraft than its airfield has spaces), but the three mission runs of `GC_AirGeneral` (Laser, Infantry and Stealth general) change their chain hash and that of their continuation from a snapshot - final hash, end and outcome as before; the input manifest is unchanged. Test `FidelityAirfieldReservesItsSpaceWhenQueued`.

### 53.9 F-46: a decoyed missile takes the newest flare (`CountermeasuresBehavior::calculateCountermeasureToDivertTo`)

Found by reading Zero Hour Reforged's changelog (`docs/REFORGED-FIXES.md` R-21): the original walks back from the newest flare at most `max(VolleySize, 1)` passes and steps
back only past a flare that is gone (`--it` in its `else`); an existing flare is weighed again on the next pass, so the newest flare that still exists is taken, however
near an older one is. The port took the nearest of the last `VolleySize` flares - what Reforged changed it to, not the original. `CountermeasuresBehavior.PickFlare` is the
original's walk (it stops at the oldest flare instead of reading before it). Implementation `96a3508`; **neither manifest moved** (measured with the full set: no run of the manifest lets a missile meet a flare older than the newest). Test `ReforgedCountermeasureTakesTheNewestFlare`.

### 53.10 F-47: a toppling structure crushes across its footprint (`StructureToppleUpdate::applyCrushingDamage`, `doDamageLine`)

Found by the same reading (R-22): below `THETA_CEILING` the original fires, every frame, lines of shots from the last crushed distance to the leading edge and one more at the
leading edge; a line is a shot every 25 units from `-facingWidth` while below `facingWidth` and one at `facingWidth`, the width being half the length of
`(MajorRadius * sin, MinorRadius * cos)` of the orientation less the topple angle. The port fired one shot at the leading edge when it advanced and reported the sweep as not
simulated. A shot's offset is `(i * sin, i * cos)` of the topple angle as the original writes it - across the fall only at 0 and 90 degrees, along it at 45; Reforged
corrects it, the original profile keeps it (a rule-profile candidate). The debris creation list of a line (`doPhaseStuff(STPHASE_FINAL)`) stays out, as the slow-death
debris (F-36). Implementation `9671fb0`, manifest `f84e33c`: nine mission runs change their hashes and save-point snapshot (their structures' state lost the "not simulated" flag); no result quantity moved but `MD_GLA01`'s count of skipped creations (80 -> 79) - every outcome, end and shot count as before. Test `ReforgedToppleCrushLine` (the shots of a line at 0, 90 and 45 degrees) and the collision test of a
falling building.

### 53.11 The retail reference match takes a new driver seed (after F-44)

Since F-44 the match with driver seed `5EED0007` ran to its limit of 432,000 ticks without a winner, and the real-data test "the retail match ends with a winner" failed
in the first tier 4 run of the phase. Eleven seeds were surveyed at 60 Hz (`SimTool reference-match --driver-seed HEX --record-only`): eight end within 15 game minutes, two
(5EED0009, 5EED0007) run to the limit, one was not finished. `5EED0003` ends with the same winner, FactionGLA, at the same original frame (16,410) at 30, 60 and 120 Hz;
`5EED0001` ends at another frame at 120 Hz. The seed is changed in a commit of its own (`91f8f60`), the manifest in another (`f5c3ae4`: the 13 fields of the run
`reference-match`, the other 49 runs and the input manifest unchanged): winner FactionGLA at tick 32,820, end hash `F5751AAB8AD42D2F`, chain `A0751445A92EF9C7`, 31,480
commands. The same tier 4 run found the test "the original skirmish AI plays the same match at 30, 60 and 120 Hz" failing from frame 6,102: the teams' air fight it stops
before now begins earlier, and with catalogs without model geometry a shot-down MiG falls elsewhere above 30 Hz; with the product's catalogs the match is equal in every
field for all 7,200 frames (`SimTool rate-compare --original-diff`), so the test's window is 6,000 frames (`283c3e7`).

## 54. Phase 40: the Zero Hour Reforged review (October 2026)

Zero Hour Reforged (EA's GPL source worked on as a game) is read as a second source of fixes (`docs/REFORGED-FIXES.md`); every place where its changelog led to a
difference between this port and the original is checked in the original's source and fixed in the original profile as Stage 1 work - one cause, one implementation
commit, a test that fails on the code before at 30, 60 and 120 Hz, one manifest commit where a manifest moved. Nothing of Reforged's code is copied. **No human has seen
any of it.**

### 54.1 F-52: the last damage is recorded (`ActiveBody::attemptDamage`, `attemptHealing`, `AIPlayer::isSupplySourceAttacked`)

`ActiveBody` keeps the frame and the source of the last damage; a heal is recorded as well. Within the frame of a record and the next one only a damage from a vehicle,
infantry or faction structure replaces a record whose source exists ("we prefer the one that tells who the attacker is"), and a damage with an existing source tells the
victim's player it was attacked (`Player::setAttackedBy`). The port wrote no record: `StealthUpdate`'s `TAKING_DAMAGE` never held, and neither did the scripts' and the
original AI's `SKIRMISH_SUPPLY_SOURCE_ATTACKED`, which now scans every ten original frames, only while the player was attacked in them, over its cash generators, dozers and
harvesters. New state, written only when set: `Entity.LastDamageSource`, `LastDamageHealing`, `PlayerState.AttackedTick`, the script engine's `supplyAttackCheckFrames`.
Tests `ReforgedLastDamageIsRecorded`, `ReforgedSupplySourceAttacked`. Manifest `e2170ca`: 46 of 50 runs change their state hashes only; no outcome, winner, end tick or command count moves.

### 54.2 F-57: turrets are switched by their modules (`TurretAI::setTurretEnabled`, `BattlePlanUpdate`, `DeployStyleAIUpdate`, `Object::isAbleToAttack`)

A switched-off turret runs no state machine (`TurretAI::updateTurretAI`): it neither aims, scans nor turns, and its weapons do not fire; an object without `CAN_ATTACK` whose
every weapon sits on such a turret cannot attack (`Object::isAbleToAttack`: it acquires nobody, its attack ends, an order to fire is refused with `NoWeapon`). The switch is
state (`TurretState.Enabled`, written only when it differs from `InitiallyDisabled`). `BattlePlanUpdate::onObjectCreated` switches the strategy center's turret off (the
shipped turret is not `InitiallyDisabled`; the port's comment and `docs/SIM-TURRET-AIM.md` had taken the stealth detector's key for the turret's); bombardment switches it on
once unpacked; leaving bombardment the center goes idle and recentres the turret once, packs when it stands at its natural angle and switches it off. While an enemy stays in
range the idle center aims at it again (`setTurretTargetObject` puts the turret back to `AIM`) and bombardment does not pack - the original's code. `DeployStyleAIUpdate` with
`TurretsFunctionOnlyWhenDeployed` switches the turret on when ready to attack and off when packing - the Nuke Cannon's turret now aims before its first shot - and with
`TurretsMustCenterBeforePacking` a move order aligns the turret first (`ALIGNING_TURRETS`, standing still). Both modules decide on the turret's rest in the first tick of the
object's own frame (the turret reaches it within a frame, which at 30 Hz is seen only in the next one). Tests `TurretSwitchStrategyCenter`, `TurretSwitchDeployStyle`; four
older tests that encoded the old behaviour are adapted. Manifests `1186ea8`: seven mission runs change their hashes only; the input manifest gains `DeployStyleData`'s two keys.

### 54.3 F-49: a sniper does not aim at an empty structure (`WeaponTemplate::estimateWeaponTemplateDamage`)

A weapon of `DAMAGE_SNIPER` estimates 0 against a structure whose first contain module holds nobody, so it cannot target it; with an occupant the estimate is the armoured
damage again. The comment of `EstimateDamage` named the rule, the code lacked it. The garrison cases of the same function (`DAMAGE_SURRENDER`, `AllowAttackGarrisonedBldgs`)
stay unmodelled. Test `SniperDoesNotTargetAnEmptyStructure`. No manifest moved.

### 54.4 F-53: the landed hulk of a Battle Bus is held (`BattleBusSlowDeathBehavior::update`)

When the thrown bus comes down the original goes idle, scrubs its physics and sets `DISABLED_HELD`; the port halted it and nothing more. The hulk now ends its attack and
carries the held bit (`Entity.ScriptHeld`), whose readers keep it from acquiring and moving. Not done: `AIGroup`'s and `Team::tryToRecruit`'s skipping of held members (the
port's group orders reach the hulk, which its AI then does not carry out). The battle bus test checks the bit and a move order. No manifest moved.

### 54.5 F-51: riders who may not leave a dying transport die with it (`TransportContain::killRidersWhoAreNotFreeToExit`)

`OpenContain::onDie` kills - or destroys with `DestroyRidersWhoAreNotFreeToExit` - every rider for which `isSpecificRiderFreeToExit` fails before it puts the rest out: the
transport's AI must let it out (`getAiFreeToExit`: a Chinook only when landed - the combat drop is not simulated -, a payload plane only alive with its doors open), a
transport on an airborne locomotor may always put riders out, otherwise the rider needs an AI with a locomotor whose surfaces fit the pathfinding cell under the transport
(`Pathfinder::validMovementTerrain`: an obstacle cell counts as fitting). The port destroyed only a payload plane's riders. `DestroyRidersWhoAreNotFreeToExit` is read now (23
shipped blocks). Test `RidersNotFreeToExitDieWithTheirTransport`. Input manifest `6a6a398` (the new key); the reference manifest is unchanged.

### 54.6 The central disable rule (`GameLogic::update`, `UpdateModule::getDisabledTypesToProcess`)

A disabled object runs only the update modules whose `getDisabledTypesToProcess` shares a type with its disabled mask; a skipped module asks again the next tick. The port
ran every module and had a few check an EMP or a script hold themselves. `DisabledMask` holds the thirteen types of `DisabledTypes.cpp`; `IWorldServices.DisabledFlagsOf`
gives the ones the port models - EMP, paralyzed (§54.8), held (a script, the ferry, the Battle Bus hulk, a container that holds its riders, §54.9), script-disabled. The modules'
masks are the original's: the AI family, missiles, `AutoHealBehavior`, `CountermeasuresBehavior`, `StealthUpdate`, `StealthDetectorUpdate` and `FireWeaponPower` (the original's
power is an attack order of the AI) process `DISABLED_HELD`; `BaseRegenerateUpdate` `DISABLED_UNDERPOWERED`; `ParticleUplinkCannonUpdate` and `SpectreGunshipUpdate` four types;
`OCLUpdate`, the physics, the railroad, poison, the propaganda tower, the slow deaths, the build-complete modules and the weapon bonus beacon all of them; every other none.
`ProductionUpdate` reads `DisabledTypesToProcess` (typed now, default `DISABLED_HELD`, `HELD UNDERPOWERED` on eight shipped launchers): an EMP stops a factory's queue, a held
factory produces on. `OCLUpdate` pushes its next creation on while disabled; a propaganda tower disabled other than held lets go of everybody; a disabled beacon gives
nothing. An EMP stops its victim's AI at once (`StopForDisable`). The compatibility baselines lose `ProductionUpdate/DisabledTypesToProcess` (712 and 705 entries). Test
`DisableRuleStopsTheModulesThatDoNotProcessIt`. Commit `a1e8115`, manifests `a19c0fc`.

### 54.7 A disable holds through its last frame (`Object::checkDisabledStatus`)

`checkDisabledStatus` runs after the updates of a frame and clears a disable whose frame has come (`now >= until`): a disable "until" frame U holds through U. The port let
it go at the first tick of U. `DisabledMasks.EnabledFrom` gives the first tick it no longer holds, for the EMP (and the weapons it holds back), the paralysis, a sabotaged
factory and a hacker of a sabotaged internet center. Test `DisableHoldsThroughItsLastFrame` (a factory disabled until ten frames on is eleven frames late). Commit `c63a520`; no field of either manifest moved.

### 54.8 The turret of a disabled object stands, and paralysis disables the object (`AIUpdate::update`, `paralyzeTroop`)

`TurretAI::updateTurretAI` runs inside `AIUpdate::update` and only while the object is not paralyzed, unmanned, EMP'd, subdued or hacked: a turret stands while its object
carries a disable other than held (`DisabledTurretStands`). `BattlePlanUpdate`'s `paralyzeTroop` sets `DISABLED_PARALYZED` on the object: `Entity.ParalyzedUntil` (saved when
set) replaces the AI's own field, so everything of the troop that does not process it stands, its turret too (`ParalysisDisablesTheObject`). Commits `df2e372`, `7c821fa`; no field of either manifest moved.

### 54.9 A container holds its riders (`onContaining`: `setDisabled(DISABLED_HELD)`)

Every transport (and the containers built on it), garrison, tunnel, helix and parachute holds its riders; `OpenContain` and `HealContain` do not
(`IContainerModule.HoldsRiders`). A held rider runs only its AI, auto-heal, countermeasures, stealth, production and the modules of all types - a lifetime does not run out
inside a transport (`TransportHoldsItsRiders`). The port ran every module of a rider. Commit `6343803`, manifest `491e9f8`.

### 54.10 F-16: the brown-out (`Energy`, `Player::onPowerBrownOutChange`)

`Energy` keeps production and consumption apart; every change of either asks `hasSufficientPower` and `doPowerDisable` sets `DISABLED_UNDERPOWERED` on every `POWERED`
object of the player in a brown-out (Patriots, Gattling cannons, speaker towers, the superweapons, the laser Crusader) and clears it after. A sabotaged power plant counts as no
production and starts the brown-out at once; `Player::update` ends it the frame after the sabotage. The port kept the net sum and disabled nothing. `PlayerState.EnergyConsumption`
and `Entity.Underpowered` are saved when set; the scripts' power conditions, the original AI, the command bar and the low-power cue read `HasSufficientPower`,
`ProducedEnergyOf`, `EnergyConsumptionOf`. Test `BrownOutDisablesPoweredObjects`. Commit `8b6a7bb`, manifest `7e235b8`.

### 54.11 F-16: build time under low power (`ThingTemplate::calcTimeToBuild`, `ProductionUpdate::update`, `DozerAIUpdate`)

`calcTimeToBuild` divides the build time by the production speed of the supply ratio (production / consumption; the production itself without consumers; 0 while
sabotaged; at most 1): one less the shortfall times `LowEnergyPenaltyModifier`, at least `MinLowEnergyProductionSpeed`, below full supply at most
`MaxLowEnergyProductionSpeed` (1.0, 0.5, 0.8), truncated to frames. `ProductionUpdate` compares the frames under construction with it every frame; `DozerAIUpdate` adds
`100 / calcTimeToBuild` to a structure's percent every frame of work. The port built at full speed. **A consequence of the original's code that no community source changes:
a player without production and consumption - every GLA player - has a ratio of 0 and builds units and structures at half speed.** Upgrades do not depend on power
(`UpgradeTemplate::calcTimeToBuild`). A queue entry keeps elapsed ticks and its last total; a structure keeps `ConstructionProgress` (a share of 2^40, each step's share of
its total under power, rounded up, so a build at full power completes at the step it did). The three `GameData` keys are typed; data without them (synthetic test data only) has
no penalty. `ProductionTimeChange` and the handicap are not modelled (no data uses them); `MultipleFactory` stays deferred (the shipped value 1.0 changes nothing). Tests
`LowPowerSlowsProduction`, `LowPowerSlowsConstruction`. The staged scene `jet-strafe` stood its airfield alone, so its Raptors came out at half speed and too late for
the scene: it now has a power plant beside the airfield, as a player of the original would. Commits `0c8d463` (manifest `949259e`) and `ba4183c` (manifest `2b5006a`); the retail reference match runs to its limit since (§54.18).

### 54.12 F-56: the disabled edge (`Object::onDisabledEdge`)

When an object becomes disabled and when its last disable ends, the original tells `SpyVisionUpdate`, cancels a dozer's task (going down), and takes a power plant's production
- with its upgrade's and an active overcharge's `EnergyBonus` - out of its player's energy and gives it back; a disabled producer neither enters nor leaves the energy
(`friend_adjustPowerForPlayer`), and `PowerPlantUpgrade`/`OverchargeBehavior::onCapture` move nothing while it is disabled. The port finds the edge at the end of the tick among
the objects the tick touched and the disabled ones (the original fires it in the setter; at 30 Hz the same frame). An EMP'd power plant now browns out its base. The radar part
belongs to F-50. Test `DisabledPowerPlantStopsProducing`. Commit `dda7e16`; no field of either manifest moved.

### 54.13 A disable pauses the special powers (`Object::pauseAllSpecialPowers`)

`setDisabledUntil` pauses the object's special powers once for every disabled type but `DISABLED_HELD` when it is set, `clearDisabled` resumes them once when it is
cleared: a superweapon's clock stands while its building is underpowered or EMP'd. The edge pass finds the types that came and went (`Entity.PowerPauseTypes`, derived) and
pauses once per type, counted from the start of the frame it is found in, so the pause is as long at every rate. Test `DisablePausesSpecialPowers`. Commit `95acd6d`, manifest `a0fe31e`.

### 54.14 F-50: the player's radar (`Player::addRadar`, `hasRadar`, `RadarUpgrade`, `W3DLeftHUDDraw`, `LeftHUDInput`)

A player counts its radars: `RadarUpgrade` adds one when upgraded (the American command center grants it at creation, China buys it, the GLA's radar van has a disable-proof
one) and takes it away on delete and capture, not while the object is disabled (its edge took it away already); a brown-out switches the radar off unless a disable-proof
one is there. The client draws the minimap and lets it take clicks only while the radar is on - forced by a mission (`RADAR_FORCE_ENABLE`/`RADAR_REVERT_TO_NORMAL`), or not
hidden (`RADAR_DISABLE`/`RADAR_ENABLE`) and the viewer has one; otherwise the left HUD eats the click. The port had no radar per player and drew the minimap always. A
spectator's minimap is its own. Tests `PlayerRadar`, `HudTests` "Minimap click moves the camera". Commits `86393d1` (manifest `464896d`) and `ff88d52`.

### 54.15 F-48: the body applies the armour (`ActiveBody::attemptDamage`, `attemptHealing`)

The original passes every damage raw to the body; `ActiveBody::attemptDamage` adjusts it by the current armour (`m_curArmor.adjustDamage`), and `attemptHealing` does the
same with a heal. `HighlanderBody` caps the raw amount at one point below the health and `UndeadBody` decides its second life on the raw amount, both before the armour; the
damage modules read the raw `in.m_amount` (the bridges' share of the damage). The port applied the armour in the weapon (`DealDamageCore`) and in three other places that
deal damage (the wave guide's water, the flame field, the air blast of the neutron and fuel-air bombs), so a hero weak to a weapon survived its cap with one point, a second
life began only when the armoured amount reached the health, and everything else - burning, poison, crushing, falling, the scripts' damage - and every heal reached the body
without armour. The body now adjusts what it is given and the callers pass the raw amount; the weapon still returns what its primary victim's armour makes of it for the
impact event. A heal is scaled by the armour's `HEALING` coefficient, which the shipped armours set only through `DEFAULT` (35 of the 53 armours name it: 19 at 100 %, 11 between 25 and 90 % - the Stinger soldier's and the projectiles' at 25 % -, `TreeArmor`, `InvulnerableAllArmor`, `HazardousMaterialArmor` and `AvalancheArmor` at 0 % and `InvulnerableArmor` at 1 %).
`HiveStructureBody` hands the raw amount to the slave, whose own armour applies. Test `ArmourIsAppliedByTheBody`. Commit `8622f6f`, manifest `20f22c5`.

### 54.16 The damage modules read the dealt amount before the clip (`out.m_actualDamageDealt`)

`attemptDamage` records the amount after the armour and the damage scalar (`m_actualDamageDealt`) and the health it took (`m_actualDamageClipped`); the damage modules
read the first - `FireWeaponWhenDamagedBehavior`'s threshold, `FlammableUpdate`'s flame limit, `PoisonedBehavior`'s poison. The port passed the clipped amount, so a killing
blow counted only the health that was left (a trap with 20 left that took 50 fired nothing as it died). The damage event keeps the clipped amount. Test
`DamageModulesReadTheUnclippedAmount`. Commit `815e461`, manifest `b8a581a`.

### 54.17 F-59: a power opens its view at the target (`SpecialPowerModule::createViewObject`)

Found while reading R-29 (Reforged sets Frenzy's `ViewObjectDuration` and `ViewObjectRange` to 0 because "a Frenzy fired into the fog scouted an enemy base"): every trigger
of a power at a place - `doSpecialPowerAtObject` at the object's position, `doSpecialPowerAtLocation` at the location, `markSpecialPowerTriggered` by the particle
cannon, the Spectre gunship and its deployment - calls `createViewObject`, which creates the `GameData`'s `SpecialPowerViewObject` (`SuperweaponPing`) for the power's owner at
the place, sets its shroud clearing range to the power's `ViewObjectRange` and the lifetime of its `DeletionUpdate` to `ViewObjectDuration`; a trigger without a place
(`doSpecialPower`, a waypoint path, a special ability) opens none. 37 of the shipped powers have both values (the superweapons, the generals' powers, Frenzy's 250 for 30 s).
The port typed the two fields and read neither: no power ever showed its owner the place it hit. `ISpecialPowerModule.Trigger` takes the place, the world queues the view
object with the power's range (`Entity.ShroudRange`, saved when set; the vision takes it over the template's) and duration (`SpawnSetup.DeletionTicks`); `GameData`'s
`SpecialPowerViewObject` is typed (compatibility baseline 708 entries, the patch layer's 701). The Spectre's takeover marks the gunship's power once more before it fires
it, as `SpectreGunshipDeploymentUpdate` does. A script that fires an ability along a waypoint path passes the closest waypoint as a location: for any power but the particle
cannon (whose own trigger passes the first waypoint, as the original's) that opens a view the original does not; whether a shipped map fires another power that way is not checked. Test
`PowerOpensItsViewAtTheTarget`. Commit `c4117e9`, manifest `2df0271`.

### 54.18 The retail reference match takes driver seed 5EED0001 (after F-16)

Since F-16 (a player below full power builds slower - every GLA player at half speed, as `calcTimeToBuild` reads a ratio of 0 for no production and no consumption)
the match with driver seed `5EED0003` runs to its limit of 432,000 ticks without a winner. Of the seeds surveyed at 60 Hz on the state of this phase `5EED0001`,
`5EED0002` and `5EED0005` end (`5EED0004` was stopped unfinished); `5EED0001` ends with FactionGLA at the same original frame (18,945) at 30, 60 and 120 Hz and is the
seed (`72ea6ca`, manifest `f5b85bb`). The same seed still ends at the same frame with the line of sight, the garrison owner and
firing from inside of §55. `reference-manifest write --only <id>` measures only the named runs into an existing manifest (`6043210`).

## 55. Phase 41: the attack's line of sight, the garrison's owner, firing from inside (October 2026)

The causes of their own size the Reforged review left (`docs/handoffs/HANDOFF-PHASE41.md`): three readings of the original first (`docs/ATTACK-LINE-OF-SIGHT.md`,
`docs/STEALTH-GARRISON.md`, `docs/GUARD-STATE.md`, `docs/FIRE-FROM-CONTAINERS.md`), then each cause with a test that fails on the code before at 30, 60 and 120 Hz and its own
manifest commit. F-54 (the stealth garrison) and F-58 (guarding) are read, not built.

### 55.1 F-55: the attack needs a line of sight (`Pathfinder::isAttackViewBlockedByObstacle`, `outOfWeaponRangeObject`, `PartitionFilterLineOfSight`, `findAttackPath`)

An attacker of the kind `ATTACK_NEEDS_LINE_OF_SIGHT` (AIData's `AttackUsesLineOfSight`, true) does not fire through the terrain or a structure. Five steps, `docs/ATTACK-LINE-OF-SIGHT.md` §4:
(a) `TerrainMap.IsClearLineOfSight`, the vertex Bresenham of `BaseHeightMapRenderObjClass::isClearLineOfSight` (a square as high as its highest corner, the start square tested
and the end square never, a fudge of 0.5, clear off the field and above the map's highest point); (b) `NavigationGrid.IsAttackLineBlocked`, the 4-connected cell walk with
the exceptions of `attackBlockedByObstacleCallback` (the attacker, the victim, either one's slaver, the attacker's container, a see-through structure, the structure covering
the victim's cell; cliff and water cells never block) - each cell keeps its count, the sum of the covering ids and the opaque count, so where footprints overlap the set is the
owner (the original keeps the first claimer; a restored world would not know it); (c) `outOfWeaponRangeObject` counts a blocked view as out of range for an attacker on the
ground with a weapon that is not a contact weapon at a victim not significantly above the terrain - the units' own attacks and turrets (`CanAttack`) and an ordered attack
(`ChooseWeaponInView`: the weapon is chosen as before and a blocked view of it is `OutOfRange`; the client's `InRange` keeps approaching); (d) the acquisition passes a
blocked candidate by (`AcquisitionViewBlocked`: the terrain between the two tops for every candidate, then the attack's view for one on the ground); (e) the approach goes
where it can fire (`FindAttackPosition`: nine half-cell points toward the victim, then a search of at most 2,500 cells - cost so far plus the distance less half the attack
distance, ties by cell index -, the first cell passable, inside the range less a quarter cell and with a clear view), for a pursuit the unit cannot fire from and for a
computer unit's chase. Not modelled: `checkDestination`'s test of other units' goals, the vehicles' strip-back, pathfind layers (bridges), leech range. Tests
`TerrainLineOfSightFollowsTheOriginal`, `AttackLineBlockedByStructures`, `AttackNeedsAClearView`, `AcquisitionPassesBlockedCandidates`, `ApproachGoesWhereItCanFire`; the
rate-equivalence suite 0 / 0 after (c), (d) and (e). Commits `2d1a8e0`, `49fc4be` (no behaviour), `05c31a8` (manifest `bdfe102`), `6744770` (manifest `d2ab940`), `8a8d661` (manifest `69193b5`).

### 55.2 The rule fix `contact-weapon-not-view-blocked` (`GameFix` 20)

The community source's fix in `isAttackViewBlockedByObstacle`: an attacker whose current weapon is a contact weapon is never view-blocked, so a suicide bomber standing in
another structure's cells sets its charge off at a building it touches (Reforged R-09). Behind the rule profile; `docs/GGC-FIXES.md` 3.1e. Test under both profiles.

### 55.3 A destroyed rider leaves its container (`Object::onDestroy`)

`Object::onDestroy` takes an object that is destroyed inside a container out of it (`removeFromContain`, "the old cleanUpContain safeguard"); the port left a dead rider in
its container's list until the container pruned it - a garrison only while it heals. The destroy phase now removes it. Test `DestroyedRiderLeavesItsContainer`. Commit `4173564`; no field of either manifest moved.

### 55.4 F-61: a garrisoned building belongs to its occupants (`GarrisonContain::recalcApparentControllingPlayer`, `ActionManager::canEnterObject`)

A garrisoned building is its first rider's player's and goes back to the owner it had when the last rider is gone (`setTeam`, `m_originalTeam`; saved while set); a
container that is not the enterer's player's takes nobody while somebody is inside and never when it is a faction structure, in the four places the port checks an entry.
Before, a garrisoned civilian building stayed neutral - nobody ever took it for an enemy - and two players could share one. Not modelled yet: `OBJECT_STATUS_CAN_ATTACK`,
the stealth garrison's exception (F-54), `onCollide`'s ejection of another player's riders. Three old tests set up two players in one building and now keep to one. Test
`GarrisonBelongsToItsOccupants`. Commit `d24c085`, manifest `1887314`.

### 55.5 F-60: riders fire from inside a container (`Object::isAbleToAttack`, `isPassengerAllowedToFire`)

Step 0 types `PassengersAllowedToFire` for the transport family and `IsEnclosingContainer` (compatibility baselines 703 and 696 entries). Step 2: `IsPassengerAllowedToFire`
per container as the original (a garrison always; a transport infantry with the flag; an Overlord infantry or its portable structure, never when itself contained; a Helix its
portable structure always, infantry with the flag; tunnel, cave, parachute and heal containers by the flag no data sets). Step 3: the `GARRISONED` weapon bonus of garrison
and Helix riders (range 133 %, damage 125 % in the shipped data). Step 5: a contained rider acts when its container lets it fire - the container's hold is no stop, the AI
processes `DISABLED_HELD` -, searches its largest weapon range plus the container's bounding circle, never chases, drops a victim out of range. **Stand-in until the fire
points are built** (for the garrisons replaced by §55.6) (steps 1 and 4 of `docs/FIRE-FROM-CONTAINERS.md`): range, search, view and shot are measured from the container - for an enclosing garrison from the
point of its bounding circle nearest the victim. Not built: the Battle Bus's `WeaponBonusPassedToPassengers`, orders through the container, `ArmedRidersUpgradeMyWeaponSet`,
the drawing of riders at their points, clearing a garrison. Tests in `Tests.FireFromContainers.cs`. Commits `a4585da` (manifest `dba394b`), `a52452b`, `91099aa` (manifest `7ba5ec5`), `7d5c9a7` (manifest `ba8fd0e`).

### 55.6 F-60, steps 1 and 4: the fire points of a garrison and the stations of a fire base (`loadGarrisonPoints`, `matchObjectsToGarrisonPoints`, `pickAStationForMe`)

Step 1 reads the bones (`ModelGeometryLoader.ReadFirePoints`, `ModelGeometry.FirePoints`, part of the world fingerprint like the exit paths): for an enclosing
garrison `FIREPOINT01..40` of the model states {GARRISONED}, {GARRISONED DAMAGED} and {GARRISONED REALLYDAMAGED}; for a fire base `STATION01..ContainMax` of
{GARRISONED}; for a transport and an Overlord `FIREPOINT01..32` of the model without a condition. As `Drawable::getPristineBonePositions` reads them: over the draw modules in
order, consecutive from `01` until the first missing bone, only public bones (`FirePoint` is a `StandardPublicBone` of `GameData.ini`, the stations are `ExtraPublicBone`s of
the four fire bases), with the template's `Scale` as root (`validateCachedBones`). Measured on the master installation: 164 garrisons with 3,288 pristine points (at most 40), the four fire bases with 16 stations (at z -7.18 below the origin - the bunker is sunk), 55 transports with 301 points.

Step 4 for the garrisons (`GarrisonContain`, `SimulationWorld.GarrisonPoints.cs`): a rider without a victim stands at the building's centre (`onContaining`); its range test
while it looks is made from the free point closest to the candidate (`calcBestGarrisonPosition` - its own point counts as taken, and a building with every point taken
lets nobody in range, as in the original); when it attacks it steps to that point (`attemptBestFirePointPosition`) and measures range, view and shot from there; once per
logic frame of the original, in the first tick of the building's own frame, a rider at a point that attacks nobody or whose victim is out of range goes back to the centre,
a rider with a victim takes the free point closest to it, and a rider at a point moves to a free one strictly closer to its victim (`removeInvalidObjectsFromGarrisonPoints`,
`addValidObjectsToGarrisonPoints`, `trackTargets`); the 40 points beyond the model's are the centre. The set follows the body's damage state (`findConditionIndex`, rubble
uses the really damaged one). A fire base puts each rider on the first free station when it comes in and back on the ground when it leaves (`removeObjectFromStationPoint`,
"Patch 1.01"). Point and station occupancy is state, written only while somebody stands at one. The step-5 stand-in for a garrison (the building's bounding circle) is
gone; a transport's riders were still measured from the transport until §55.7 (its fire points were read, not used: `OpenContain::redeployOccupants` moves them on every change
of the transport's pose, a state of its own). Not modelled: the night and snow variants of a model the original would have cached at the first entry, the gun drawable
and its muzzle flash (step 8), `MobileGarrison` (the original keeps the points where the building first stood). Tests in `Tests.FireFromContainers.cs`
(`FireFromContainersFirePoints`, the rate case on fire points) and `ModelGeometryTests` (real data). Step 1 moves only the fingerprint fields of 48 runs (69 of 1,927, manifest `c016cbf`); step 4 the hashes of 18 mission runs with garrisoned riders (104 fields), no outcome, end tick or command count (manifest `14d17c3`); the rate-equivalence suite 0 / 0. Commits `e7e86a0`, `532e5cb`..

### 55.7 F-60, step 4 continued: the match on entry, the transports' fire points (`redeployOccupants`, `putObjAtNextFirePoint`, `getSingleLogicalBonePositionOnTurret`)

Three causes. (a) A rider that enters an enclosing garrison makes it match its points at once (`OpenContain::addToContain` calls the garrison's `redeployOccupants`, which is
`matchObjectsToGarrisonPoints`), not only at the building's next frame: a rider at a point whose victim died in the middle of the building's frame goes back to the centre when
another comes in. No field of either manifest moved (`2cc126e`).. (b) The table carries the turret bone of a container with `PassengersInTurret` (the rest pose of the main turret's angle bone of the default
state): the four Overlords and the Tank General's Emperor; the bone table's golden hash is new (`843CCD2C…`). The fingerprint fields of 48 runs and the input manifest's geometry entry (`4c59644`, manifest `2e5e05e`).. (c) A transport's rider is measured - range, search,
view, shot - from where `OpenContain::redeployOccupants` puts it, which the original does on every change of the transport's transform: the `FIREPOINTnn` of its place among
the riders in the order they came in, the points used round from the first; with `PassengersInTurret` the point turns with the turret about its bone (`Pre_Rotate_Z` turns
the rotation part only, so the turn is about the pivot); a Helix's riders 8 above it, its portable structure on it (`HelixContain::redeployOccupants`, `update`); a container
without points its own position. The position is derived from the container's pose where its begun frame ends, not kept: after a rider leaves a standing transport the others
take their new points at once, the original at the transport's next move - registered as a deviation. The transport's points are those of its model without a condition;
the original reads the current model, so a damaged transport whose damaged model moves its points is not followed. Four pairings with America change (`5a04fbd`, manifest `d381fd6`): Armored Fury America-China (America at tick 40,291 instead of 59,701), Armored Fury GLA-America (37,711 instead of 60,541), Bitter Winter GLA-America (30,181 instead of 30,211), and Bitter Winter America-China, a stalemate before, is won by America at tick 72,991 - **16 of 16 pairings decided**; the reference match, the stress test and every mission run unchanged; balance not assessed.. Tests `FireFromContainersMatchOnEntry` (fails
without (a)), `FireFromContainersTransportFirePoints` (a Humvee with a point 40 east and one 40 west: its first rider fires at an enemy 130 east and not at one 130 west,
without points at neither, turned round at the western one), `ModelGeometryTests` (the Overlord's `FIREPOINT01` (-15, 0, 15) about `TURRET01` (0, 0, 10.19)); the
rate-equivalence suite 0 / 0 in all ten groups.

### 55.8 F-60, step 3 completed: the Battle Bus passes its weapon bonus (`Weapon::computeBonus`, `OpenContain::getWeaponBonusPassedToPassengers`)

`WeaponBonusPassedToPassengers` is typed (Bool on `TransportContain` and the contain modules built on its field list; six shipped Battle Buses set it; compatibility baselines
702 and 695 entries) and read into `TransportContainData` (`7ae72b1`, manifest `dec6f7d`: the input manifest and the fingerprint fields of 48 runs). `SimulationWorld.WeaponBonusOf(source)` is `computeBonus`'s condition set: the shooter's own, and,
when the container it rides in passes its bonus on, the container's own conditions (not its container's). Every weapon calculation of a shooter reads it - the longest range,
the range test, the damage estimate and the damage, the reload and clip, the turret's pitch range, the pre-attack delay, the conditions a projectile carries; the rider's own
conditions stay its own. Test `FireFromContainersPassedBonus` (a veteran bus gives its rider range 120 instead of 100 and it fires at 115; a veteran Humvee passes nothing).
No field of either manifest moved (`cfc16aa`): no run of the manifest has a veteran or otherwise conditioned Battle Bus with a rider..

### 55.9 F-54: the stealth garrison (`m_stealthUnitsContained`, `m_hideGarrisonedStateFromNonallies`, `getApparentControllingPlayer`, `StealthDetectorUpdate`)

Read in `docs/STEALTH-GARRISON.md`; F-61 (§55.4) was its step 1. **The count** (`ccb940b`): `StealthUnitsContained` - the riders of kind `STEALTH_GARRISON` (their kind, not their
stealth state), derived from the riders; the original's count is a counter that `killAllContained` does not lower (R-18) - not modelled. **Capture** (`9f74c4c`):
`canCaptureBuilding` refuses a stealthed, undetected target (not a disguised one) and a garrisonable building with riders not of that kind - the port allowed both.
**The hidden state** (`539217e`): a garrison is hidden while riders are inside, all of kind `STEALTH_GARRISON`, the first not `DETECTED` (the original's flag is recalculated on
every change of its riders and whenever its first rider's `DETECTED` flips, so it equals this derived value); hidden, it shows a player its owner does not count as an ally the
owner it had before the riders (`getApparentControllingPlayer`, the F-61 record of the original owner). Target acquisition reads it as `findClosestEnemy` does: without
`ATTACK_BUILDINGS` the building's apparent owner must be the unit's enemy before the computer player's exception (`PartitionFilterRejectBuildings`) - the port had folded that
exception into the building flag -, and a container whose riders are hidden stealth riders is passed by when its apparent owner is the unit's enemy
(`PartitionFilterStealthedAndUndetected`'s building branch; an empty container is not hidden). A capturer refuses a building that appears to contain friendlies. **Entering**
(`1e4c97e`): `canEnterObject` lets another player in when only stealth riders are inside, without the room test; on arrival (`onCollide`) the other player's riders are sent
out, those of kind `STEALTH_GARRISON` marked detected for their `StealthDelay`, and the enterer goes in - at the same touch here (a garrison lets its riders out at once), at a
later one in the original (`aiExit` is a state). **The detector** (`b7adda8`): it scans with `PartitionFilterStealthedOrStealthGarrisoned` - a stealthed object, or a
garrison holding a stealth rider -; a rider its container encloses is out of the partition and never found itself; for a found garrison every rider with a `StealthUpdate`
that is not the detector's player's nor its ally is marked for the scan's period plus two frames; relationship and kind filters apply to the building; the port's terrain
line of sight stayed on both branches then (the original has none in this code; removed in §55.11). Tests in `Tests.StealthGarrison.cs` (the count; capture; the hidden state against an idle
rifleman, a detected rider, a plain garrison and a unit with `ATTACK_BUILDINGS` against a neutral and an own building; capture of a hidden one; entering a full hidden
garrison; three detectors - in range, out of range, looking only for infantry), each failing on the code before at 30, 60 and 120 Hz. **Step 5** (`baa0fb2`): no stealth inside a container that is not garrisonable (`allowedToStealth`; `IsGarrisonable` - a garrison, and an Overlord whose
portable structure's container is one). **Step 6** (`9b43a51`): a stealth rider thrown out is detected for its `StealthDelay` - the bunker buster, a combat bike's new
rider, the capture of a garrisonable building; the kick-out of `Object::defect` is not modelled at all. **The presentation** (`51ed3ec`): for the player a frame is drawn
for, a hidden garrison of a non-ally shows no `GARRISONED` and is drawn, and remembered in the fog, in the colour of its owner from before; the tooltip owner, the minimap
colour and the attack cursor are not changed yet. Steps 2 to 4, entering, step 6 and the drawing move no field of either manifest; step 5 changes the hashes of the mission runs `MD_GLA03` and `MD_USA02` (15 of 1,929 fields), no outcome or end tick (manifest `b5282ac`).

### 55.10 F-58: guarding as a state of the simulation (`AIGuardMachine`, `AIGuardRetaliateMachine`, `aiGuardPosition`, `aiGuardObject`, `aiGuardArea`)

Read in `docs/GUARD-STATE.md`. **Step 1** (`96cb34c`): a new command `GuardCommand` (`MSG_DO_GUARD_POSITION` / `MSG_DO_GUARD_OBJECT` to a group, `groupGuardPosition`): every
unit guards the same point or object in one of the three modes of the guard buttons. `GuardState` (the entity's `guard`, written only while it guards) and
`SimulationWorld.Guard.cs` run the original's machine per unit: IDLE looks every `GuardEnemyScanRate` (15 frames; the first after a hashed 0..15) for the closest enemy within
the inner range of the guarded centre - a human's unit 1.8 x vision, a computer's 1.1 x, times a computer team's mood, a contained unit its largest weapon range - that the unit
could attack here or after moving; INNER attacks it within that range, OUTER follows it within the outer range (2.2 / 1.333) and gives up when it has not been back inside
the inner range for `GuardChaseUnitsDuration` (300 frames); RETURN walks back, looking every `GuardEnemyReturnScanRate` (30 frames); an attacker that hurt the unit and that it
can answer is attacked from INNER, RETURN and IDLE (ATTACK_AGGRESSOR, the body's last damage record read and cleared). `GUARD_WITHOUT_PURSUIT` has no OUTER,
`GUARD_FLYING_UNITS_ONLY` looks at flying units only; GET_CRATE never holds, as in the original. The guard decides in the first tick of the unit's own original frame (the
acquisition's loop runs it instead of the unit's own acquisition), reads positions where frames end, and ends with the unit's next order; a guarding unit is not idle. The
client's three guard buttons send the command with their mode; its standing order for the guard is gone. **`CommandTypes.Version` 18, replay format 16**; the guard is an
order of a player (25 of 35 types), fuzzed with the other commands; `WorldSnapshot` stays 10. **The fog** (`d63b5cb`): the guard's look has no fog filter (Reforged adds one)
and its attack walks after what it found - its victim need not be visible. **Step 3** (`5013d05`): `TEAM_GUARD_AREA` guards the trigger area - its bounds centre is the
guarded position, the scans look inside the polygon out to the area's radius as the original computes it (`halfHeight = (hi.y + lo.y) / 2`, the plus kept), OUTER at least that
far; the guard keeps the area by name, the mission gives the world its areas. Not modelled: the original's gate that looks only when some object entered or left a trigger area,
or one was made or destroyed, within the last scan period. **Step 4** (`a1a13a0`): `NAMED_GUARD`, `TEAM_GUARD` and `UNIT_GUARD_FOR_FRAMECOUNT` guard the own position,
`TEAM_GUARD_POSITION` the waypoint, `TEAM_GUARD_OBJECT` the named object, `TEAM_GUARD_SUPPLY_CENTER` the warehouse's side (the first pick of a warehouse under attack is
not modelled); `TEAM_GUARD_FOR_FRAMECOUNT` stays idle as in the original. Tests in `Tests.Guard.cs` (the chase, the return, the frames at 30, 60 and 120 Hz, a save in the
middle of the chase, a move ending the guard; giving up; the modes; a computer's ranges; the answer to an attacker beyond the inner range) and `Tests.Phase13Actions.cs` (the
script actions; the area: an enemy inside is attacked, one outside the polygon but within the radius is not). Measured: step 1 changes the two version fields and the command-stream hashes of the 22 runs whose description carries them (manifest `f93ac7c`), step 4 the hashes of twelve mission runs whose scripts guard (`e0623a3`), the fog rule nothing, step 3 the mission run `MD_GLA05` - shots, objects, script executions, not its outcome (`8dfb467`); the input manifest is unchanged throughout, no outcome or end tick moved. **Step 6, the player's retaliation** (`32e7cdd`): a hit on a human player's object by an enemy that is no airborne target,
within 200 between the bounding circles, makes every idle, mobile object of the player and its allies within 120 + the victim's radius that could attack the attacker enter
`AIGuardRetaliateMachine` - ATTACK_AGGRESSOR on the attacker (outer + inner range from where it stood, 300 frames), RETURN, an IDLE that looks once (INNER 1.5 x inner, OUTER
0.67 x (outer + inner), no ordinary buildings) and ends; the exit when the retaliator leaves its inner range. The mode is on for every human player (the original's client
switches it on in the match's first second; the option to turn it off is not built). The hits are noted during the tick and decided at its end, a hit from outside a step at
once: the team relations of a decision are not read in the middle of the combat phase (an equivalence test of the entity lists found that a call there moved the script
engine's team table). It changes only `MD_GLA05` again (manifest `80f52cd`, not its outcome): the AI pairings and the reference match have no human player. **The rule fix `guard-answers-only-when-idle`** (`bbdded6`, `GameFix` 21; Reforged's R-35): the community source
removes the attack-aggressor condition from the guard's INNER and RETURN and the retaliation's RETURN - a guard shot while it fights or walks back answers once it stands idle.
Not built: step 2's guarded object moving off (built into the command and IDLE's check, not tested on its own), step 5 (the tunnel network guard), step 7 (`DeployStyleAIUpdate` deploying in the
guard's IDLE, which needs the guard's own moves to pack the unit first; `JetAIUpdate`'s reload resume; `FlightDeckBehavior`'s order passing), the enter guard of templates
with `isEnterGuard`.
**The guard order's own tick** (`e0d9c7d`, manifest `4ef56b8`): the guard order is taken in the command phase, before the tick's updates, like any other order; its return
move had been held for the unit's next frame as if it were one of the guard's own decisions, so above 30 Hz the move began one frame late - the original skirmish AI's match
diverged at 60 Hz from frame 4,159 (found by bisecting to `5013d05`, the area guard). The script's `TEAM_GUARD_AREA` goes through the command path as the other script guard
actions do (`GuardCommand.Area` with the trigger area's name, refused when unknown or longer than 256 characters); the guard tests give their orders at the start of a frame of
the original at every rate. Thirteen mission runs whose scripts give guard orders change their hashes; every outcome and end tick is kept; the input manifest is unchanged.

### 55.11 The stealth detector without a line of sight, and two re-opened community fixes

**The detector** (`2996901`): `StealthDetectorUpdate::update` scans with `iterateObjectsInRange` and no line of sight - a candidate within the detection range is found
behind a ridge as well. Phase 10 had added a terrain test of its own; it is removed (the side item of the brief), and its test of that time says the opposite now.
No field of either manifest moved. **`no-power-edge-for-unfinished-plant`** (`5640d20`, `GameFix` 22): `Object::onDisabledEdge` of the community source changes no energy for a
power plant under construction - an EMP on a plant still being built took 5 from its player under the original rules (test: -5 against 0, the finished plant 5 either way).
F-56 gave the port the path the fix acts on. **`undead-lethal-by-armour`** (`ec50e56`, `GameFix` 23): `UndeadBody::attemptDamage` decides by `estimateDamage`, the amount after
the armour - a raw 150 that the armour halves no longer starts a bus's second life at a health of 100. Phase 32 rejected it while the port applied the armour before the body;
F-48 moved it into the body. **`takeoff-waits-for-one-jet`** (`4711e39`, `GameFix` 24; Reforged's R-34): the original's `JetPauseBeforeTakeoffState::findWaiter` keeps a jet at the start
of its runway while any jet holding a runway taxis - also each jet that taxis up behind it once the front jets hand their runways on; the community source waits only for the
first jet found on the other runway (four jets on two runways: under the original no jet starts its run while another taxis, with the fix a front jet does). All three behind
the rule profile, tested under both profiles at 30, 60 and 120 Hz. The catalogue's 96 CRC-gated markers: 31 adopted (23 fixes), 17
equivalent, 47 not applicable, 1 rejected (§55.12 moves one more).

### 55.12 F-62: how a Chinook lets its riders out (`ChinookEvacuateState`, `AIExitState::update`, `ChinookAIUpdate::update`)

Read for Reforged's R-34, whose third item ("a Chinook unloads all at once") is the original's behaviour that the community source changed. **The original**
(`ChinookAIUpdate::aiDoCommand`): an unload at a place (`AICMD_MOVE_TO_POSITION_AND_EVACUATE`, and `_AND_EXIT` for a reinforcement) runs the Chinook's own states - move,
land, `ChinookEvacuateState`, take off; its `onEnter` calls `removeAllContained(FALSE)`: every rider is out in that frame, put where the Chinook stands
(`removeFromContainViaIterator`), and stands there. A bare evacuation (`AICMD_EVACUATE`) lands and goes the standard way, every rider told `aiExit`; a rider waits while the
container's AI answers `WAIT_TO_EXIT` (`AIExitState::update`; only `ChinookAIUpdate::getAiFreeToExit`, for a Chinook not on the ground) and while the door is busy
(`ExitDelay`), and an idle Chinook lands while somebody wants in or out and takes off only when nobody does (`ChinookAIUpdate::update`, `hasObjectsWantingToEnterOrExit`).
**The port** (`5403e91`) let the riders of an unload at a place out through the door as well, and the Chinook climbed away while riders still queued there; the riders at the
door never asked whether the Chinook was down. Now: an unload at a place is `EvacuateContainer(atOnce)` - `removeAllContained`; the transport's door releases nobody while its
AI says wait (`AIUpdateInterface.RidersWaitToExit`); a landed Chinook with riders at its door stays down, also in the frame of the evacuation, when its supply machine still
reads busy. `getTeam()->setActive()` of the evacuate state has no counterpart. Test `ChinookUnloadAtAPlaceAtOnceAndAtTheDoorOneByOne` at 30, 60 and 120 Hz: three riders out in
one step the frame after the Chinook is down, where it stands; a bare evacuation three frames apart (`ExitDelay` 100 ms) with the Chinook on the ground until the last is out.
The manifest (`76866ea`) moved in 9 of 1,929 fields, all of `mission_MD_USA02`, whose script reinforcement arrives by Chinook: its hashes, its snapshot and its shots (41 -> 36); outcome and end tick unchanged; the input manifest is identical. **The rule fix `chinook-unloads-one-by-one`** (`9f81dba`, `GameFix` 25): `ChinookAIUpdate::aiDoCommand` of the community source sends an unload at a place down the
standard evacuation unless somebody already wants in or out - the riders leave through the door one after the other; this port does not do so for the `_AND_EXIT` form,
because the standard way would fly the reinforcement home with its riders aboard (Reforged found and corrected that in its own copy, `4e780d3b`). Test
`GameFixChinookUnloadsOneByOne` under both profiles at 30, 60 and 120 Hz. The catalogue's 96 CRC-gated markers: 32 adopted (24 fixes), 16 equivalent, 47 not applicable,
1 rejected.

### 55.13 F-60 completed: orders through the container, armed riders, the garrison gun

**Orders through the container** (`eb06bc0`, step 6; `AIGroup::groupAttackObjectPrivate`): a container whose riders may fire (`isPassengerAllowedToFire()`, at least one rider)
passes an attack order on to every rider whose `getAbleToAttackSpecificObject` is possible - in reach or after moving -, then attacks itself if it can. A `PursueCommand` on
such a container is not refused for not moving; each rider takes the victim as the engagement of its own look (`SimulationWorld.PassAttackToRiders`), fires while it is in
reach of its fire point and ends the attack when it is not, as the original's attack state fails for a rider that cannot approach. The client treats such a container as armed
(`SimulationQueries.CanTakeAttackOrder`, `InRangeOrRiders`): the attack cursor when a rider reaches the target, one `PursueCommand` for a building instead of a standing order.
Not built: an attack on a position (`groupAttackPosition` - the port has no command for it) and the Chinook's re-issue of its victim every ten frames. No field of the reference manifest moved: no run of it orders a garrison or a transport whose riders may fire.
**Armed riders** (`1474a80`, step 7; `TransportContain::letRidersUpgradeWeaponSet`): after every rider that comes in or goes out, a transport with
`ArmedRidersUpgradeMyWeaponSet` sets its `PLAYER_UPGRADE` weapon set flag while an infantry rider carries a weapon that is no contact weapon and a damage weapon
(`Weapon::isDamageWeapon`: `DEPLOY` and `DISARM` are, `HACK` is not, else a primary or secondary damage above zero) and clears it otherwise - the flag a `WeaponSetUpgrade`
raises, as in the original. The key is typed (compatibility baselines 702 -> 701 and 695 -> 694). The typed key moves the input manifest (definitions, catalogs, world fingerprint) and the fingerprint fields of the reference manifest (`ad5f09a`: 69 of 1,929, `commandStreamSha256` and `snapshotSha256`); no end hash, winner or end tick changed - no run carries a transport with the flag and an armed rider.
**The garrison gun** (`4dc82c7`, step 8, presentation only; `putObjectAtGarrisonPoint`, `trackTargets`, `updateEffects`): a `GarrisonGun` drawable at every occupied point
of an enclosing garrison the viewer sees, where the rider stands, facing the rider's victim, with FIRING_A from the frame after the rider's shot until more than
`MUZZLE_FLASH_LIFETIME` (30/7, so 4) frames have passed - five frames -, not for a poison weapon (`FrameProjector.GarrisonGuns`); no object of the simulation. Test
`GarrisonGunTests` at 30, 60 and 120 Hz. No human has seen it.
