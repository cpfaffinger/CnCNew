# Performance of the campaign missions (Phase 19, sub-brief scripts, target acquisition, search modules)

As of 6 October 2026. Subject: what makes a mission tick expensive, as far as it is not movement/pathfinding, vision or the state hash –
the script VM (`src/CnCNew.Simulation/Scripting`), the target acquisition (`SimulationWorld.Acquisition.cs`) and the modules that search the
world on every update (`PointDefenseLaserUpdate`, `CheckpointUpdate`, `DemoTrapUpdate`, `StealthDetectorUpdate`, the dock search of the supply units).

**Rule of the work:** no result changes. Every commit passes `SimTool reference-manifest check … --set quick` unchanged
(in it `mission_MD_USA05` and `mission_MD_GLA05`), the final state additionally `--set full`, `input-manifest check`, the check of the incremental
hash and campaign audit and driver (§6). The manifest files are not touched.

**All times in this document are not measured quietly.** The machine (AMD Ryzen 7 PRO 6850U, 16 logical processors) ran during the
whole work with four further agents; the same measurement scatters by a factor of two to three (`AIUpdateInterface`, which was not touched
here, lay between 388 and 1,228 µs per call depending on the run). What is reliable are therefore the **counters** (calls, evaluated conditions, allocated
bytes – all deterministic) and times **in proportion within the same run**. The quiet comparison measurement according to
`docs/PERFORMANCE.md` §4 is outstanding (§7).

## 1. Measuring tool

Since this work `SimTool perf-mission` (docs/PERFORMANCE.md §3) can say *where* time and memory go:

- **Allocation per tick phase** ("Allocated bytes per tick by phase") and **per module type** (columns `bytes/tick`, `bytes/call` of the module table):
  `GC.GetAllocatedBytesForCurrentThread` at the same places at which the clock is already read.
- **`--script-detail`**: the script VM reports to an `IScriptObserver` (`Scripting/ScriptObserver.cs`, the counterpart to `ITickPhaseObserver`)
  the sections of `ScriptEngine.Update` (`ScriptSection`: Housekeeping, Scripts, Followers, ButtonHunts, TeamEvents, Sequences, TeamBuilds,
  TeamQueues, WorldOrders) as well as the beginning and end of every condition evaluation and every action. The tool (`ScriptRecorder.cs`) keeps per
  condition and action type calls, self time, maximum and self bytes (nested calls subtracted). The clock is in the tool; the
  simulation holds none (`SourceRules`). The observer is not state; without it the VM runs the same calls in the same order.
  The option is not the default, because two clock and two counter accesses per condition would otherwise be in every profile of the phase `MissionScripts`.
- **First step**: how much of the first tick the runtime spends compiling methods (`System.Runtime.JitInfo`).

```powershell
dotnet build -c Release tools/CnCNew.SimTool
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- perf-mission game/content --map "Maps/MD_GLA05/MD_GLA05.map" --ticks 3000 --script-detail --top 30
```

## 2. Measured breakdown of the starting state (`74ccdde`)

### 2.1 The 4 MB per tick are not the scripts

`docs/PERFORMANCE.md` §6.3 item 5 attributes the 4 MB of allocation per tick of `MD_GLA05` to the scripts. The measurement per phase refutes that
(`MD_GLA05`, 1,500 ticks):

| Phase | Bytes per tick |
|---|---:|
| Updates | 5,174,216 |
| of which `PointDefenseLaserUpdate` | 5,155,999 (191,090 per call) |
| Acquisition | 232,842 |
| MissionScripts | 97,321 |
| all others together | 7,200 |
| **Sum** | **5,511,603** |

Cause in the laser: `ScanRate = 0` in the retail data (`AirforceGeneral.ini`), so each of the 27 lasers searches for a target **every tick**, and the
search went over all of the about 2,250 objects with `PrimaryTargetTypes.Any(other.Template.IsKindOf)` – two delegates and one LINQ call per object,
plus four arrays per search. 40,473 calls in 1,500 ticks, 110.8 µs per call, 28.1 % of the update phase.

The 233 KB in the target acquisition were the script VM by a detour: `IAcquisitionAdvisor.AttitudeOf` → `ScriptEngine.TeamOfEntity` rebuilt the
dictionary object → team on the first call of every tick and ran over all teams on **every** call (enumerator of a `SortedDictionary`).

### 2.2 Script VM by section and condition type

`MD_GLA05`, 3,000 ticks, `--script-detail`. 690,622 condition evaluations (230.2 per tick) in 33 types, 471 actions (0.2 per tick) in 66 types;
102 scripts executed their actions.

| Section of `ScriptEngine.Update` | µs per tick | Bytes per tick |
|---|---:|---:|
| Scripts (all script lists: conditions and actions) | 4,392.2 | 88,869 |
| TeamEvents (enemy sighted/all clear, team destroyed, idle) | 1,898.1 | 4,652 |
| Housekeeping (`PruneTeams`, timers) | 100.4 | 9,049 |
| Followers (standing orders: paths, hunt, attack) | 50.1 | 6,101 |
| ButtonHunts, Sequences, TeamBuilds, TeamQueues, WorldOrders | together 4.9 | 0 |

| Condition type | Calls per tick | Self time µs per tick | µs per call | Bytes per call |
|---|---:|---:|---:|---:|
| `PLAYER_HAS_COMPARISON_UNIT_TYPE_IN_TRIGGER_AREA` | 40.4 | 2,367.4 | 58.60 | 824 |
| `BUILT_BY_PLAYER` | 5.0 | 401.6 | 80.33 | 1,699 |
| `PLAYER_ALL_DESTROYED` | 4.2 | 236.8 | 56.91 | 336 |
| `SKIRMISH_PLAYER_HAS_UNITS_IN_AREA` | 11.1 | 198.1 | 17.90 | 160 |
| `PLAYER_LOST_OBJECT_TYPE` | 2.5 | 173.3 | 68.43 | 1,091 |
| `PLAYER_HAS_OBJECT_COMPARISON` | 2.1 | 131.5 | 61.98 | 932 |
| `SKIRMISH_SPECIAL_POWER_READY` | 2.0 | 130.7 | 65.32 | 1,171 |
| `PLAYER_ALL_BUILDFACILITIES_DESTROYED` | 1.0 | 84.4 | 84.36 | 2,991 |
| `PLAYER_HAS_N_OR_FEWER_BUILDINGS` | 1.0 | 62.2 | 62.24 | 881 |
| `PLAYER_HAS_COMPARISON_UNIT_KIND_IN_TRIGGER_AREA` | 1.0 | 61.1 | 61.09 | 951 |
| `TIMER_EXPIRED` | 67.9 | 38.0 | 0.56 | 64 |
| `FLAG` | 33.7 | 18.5 | 0.55 | 64 |
| `SKIRMISH_PLAYER_HAS_BEEN_ATTACKED_BY_PLAYER` | 14.8 | 13.6 | 0.92 | 120 |
| remaining 20 types | 43.5 | 86.4 | – | – |
| **all** | **230.2** | **4,003.6** | | 70,574 per tick |

Findings:

1. **Everything above 50 µs per call is the same place:** `PlayerEntities(player)` filtered, per condition, all objects of the world with a LINQ `Where`
   over the boxed table enumerator and sorted the result by id. Ten condition types, 60 to 70 calls per tick, 3.7 of 4.0 ms.
2. **Every condition allocated 64 bytes**, `TIMER_EXPIRED` and `CONDITION_TRUE` too: in the large `switch` of `EvaluateCondition` lambdas read
   the parameter list, so the compiler creates the closure object on entry into the method – whether a lambda is reached or not.
   The same in `ExecuteScriptBody` (once per script and tick, for inactive scripts too: the 38 KB per tick of the section Scripts outside
   the conditions) and in the `switch` of the `SKIRMISH_*` conditions (48 bytes).
3. **TeamEvents** is almost as expensive as half of all conditions: `EnemyInSight` ran, for every living member of every team with an
   enemy-sighted or all-clear script, over the whole world until it found an enemy within sight range – so in the normal case (none there) entirely.
4. **Actions cost nothing** (33.8 µs per tick on average, almost all of it in the first tick: map reveal 6 to 13 ms per action).
5. **Housekeeping**: `PruneTeams` asked every tick for every named object whether its name is in the sorted set of the names that ever existed,
   and built four filtered arrays and one delegate per team.

### 2.3 Modules that search the world

| Module | Map | Calls in 3,000 ticks | µs per call | Bytes per call | What it does per call |
|---|---|---:|---:|---:|---|
| `PointDefenseLaserUpdate` | `MD_GLA05` | 80,973 (27 lasers) | 93.9 | 191,090 | all objects, kinds via LINQ (§2.1) |
| `CheckpointUpdate` | `MD_GLA05` | 2,999 (1 post) | 236.3 | 13,101 | all objects twice; per candidate edge distance = up to three 128-bit roots |
| `StealthDetectorUpdate` | `MD_GLA05` / `MD_USA05` | 6,081 / 12,608 | 68.0 / 72.9 | 356 / 104 | all objects; per object first the player relationship (three table accesses), only then the stealth bit |
| `DemoTrapUpdate` | `MD_USA05` | 44,985 (15 traps) | 80.5 | 227 | all objects, per object two 128-bit products for the squared distance |
| `WorkerAIUpdate` | `MD_USA05` | 11,992 | 190.7 | 956 | among other things dock search over all objects with a module search per object; the rest is movement |

Two causes among these apply to the whole simulation:

- **`ObjectTemplate.IsKindOf` allocated 24 bytes per call.** It was `ImmutableArray<string>.Contains(kind, StringComparer.OrdinalIgnoreCase)`;
  this call allocates in .NET 10 and takes about four times as long as a loop over the handful of names (measured individually, Release:
  24 B and 40–84 ns against 0 B and 9–17 ns). Found through the checkpoint: its 12.9 KB per call were 540 such calls.
- **`Fixed.Sqrt` began Newton's iteration at `max(raw, scale)`**, so for a squared distance at the square itself, and needed about one
  128-bit division per bit by which the start was too large (measured individually: 80–225 ns per root).

### 2.4 Target acquisition

`StepAcquisition` runs per tick over all objects whose AI searches for targets when idle; one look (`FindMoodTarget`) ran over **all** objects of the
world and discarded the own ones and those of the system player individually. In addition the team query from §2.1. Measured quietly (PERFORMANCE.md §6.2):
503–541 µs per tick in `MD_GLA05`, 276–312 in `MD_USA05`; under the load of this work 974–2,329 µs.

### 2.5 The first tick

The longest tick of a mission is tick 0 (quiet 169 ms in `MD_GLA05`, of which 47 % scripts). Measured at the final state in `MD_USA05`: first step
293.0 ms, **of which 218.0 ms compiling methods** (JIT) on this thread – the large `switch` methods of the VM are compiled on the first pass.
The rest are the actions of the first tick, above all the map reveal. That is a property of the process, not of the mission
(a second mission start in the same process does not pay it), and not changed here (§7).

Tick 4,657 of `MD_GLA05` (93 ms, 94 % updates, 8 MB) lies in the update phase outside this sub-brief; its memory share was for the
most part the laser (5 MB in every tick).

## 3. Common means: lists instead of a search over all objects

`SimulationWorld.Indexes.cs` gives three views of the object table, each in ascending id order and with **exactly the members
that a search over `Entities` with the respective filter visits**:

| Query | Filter of the replaced search |
|---|---|
| `EntitiesOwnedBy(PlayerId)` | `entity.Owner == owner` |
| `EntitiesOfKind(string)` | `entity.Template.IsKindOf(kind)` (without regard to case) |
| `EntitiesWithModule<T>()` | a module of the object is a `T` (class or interface) |

- **Not state:** neither hashed nor saved. Every list is built on the first query in one pass over the table – a world
  that never asks (stress test) pays nothing, and a restored world builds the same lists from the same table.
- **Exact maintenance** at the three places at which the answer can change: `Register` (object comes into the table), `ProcessDestroys`
  (goes out) and `SetOwner` – now the one place that sets `Entity.Owner` of a registered object (`TransferOwnership`,
  `ExecuteCapture`). Kinds and modules of an object never change.
- **Order:** the table enumerates by id (`EntityTable`), the lists too. Where a caller reads several lists one after the other (the lists
  of the enemy players, the lists of several kinds), it is justified at the respective change why the result does not depend on the order.
- Whoever iterates a list must not change its membership while doing so; script actions that kill or transfer objects get, as
  before, a copy of their own (`ScriptEngine.PlayerEntities`).

## 4. The changes

Every change is one commit with one cause; "manifest quick" means `reference-manifest check --set quick` at exactly this state.

| No. | Commit | Content |
|---:|---|---|
| 1 | `b4a0598` | measuring tool: `IScriptObserver`, `--script-detail`, allocation per phase and module |
| 2 | `432abae` | lists by owner and kind; `PointDefenseLaserUpdate` reads the lists of its target kinds |
| 3 | `4b1c3a9` | object conditions of the scripts read the owner list |
| 4 | `3426c0c` | `EnemyInSight` of the team events looks only at the objects of the enemy players |
| 5 | `c51b37d` | table object → team is refilled only after a change (`TeamTable`, `TeamMembers`, `MembershipStamp`) |
| 6 | `9c16bd1` | target acquisition: `FindMoodTarget` looks only at the objects of the enemy players |
| 7 | `0926b7f` | `WorldEffectModules.Closest` (checkpoint, `EnemyNearUpdate`): players with the sought relationship, range test before the roots |
| 8 | `cc39b54` | script loop and housekeeping without allocation |
| 9 | `da6a4ed` | `Fixed.Sqrt`: start value near the root |
| 10 | `9b1abf0` | `ObjectTemplate.IsKindOf` as a loop |
| 11 | `43f1370` | `DemoTrapUpdate`: range test before the products |
| 12 | `cde8162` | `StealthDetectorUpdate`: stealth bit first |
| 13 | `66a2f07` | tests (§5) |
| 14 | `3c4fb10` | list by module type; dock search of the supply units and rally-point search read lists |

Why each of them does not change the result:

**2 – Laser.** Each of the four selections (primary/secondary, in/out of range) is the minimum of (distance, id) over its candidates – `Closer`
breaks ties itself by id –, and a selection out of range is used only if its list has no candidate in range;
then it was never skipped either. So the result does not depend on the order, and an object with two listed kinds that
comes twice changes nothing the second time. Whether a template is primary, secondary or neither is looked up once per template (in the module,
not in the module data: the catalog hash of the input manifest covers fields). No random number is drawn in the search.

**3 – Object conditions.** `PlayerEntities` was `Entities.Where(Owner == p && CountsAsObject).OrderBy(Id)`; the table already enumerates by id,
the sorting never changed the order. The conditions now run over `EntitiesOwnedBy(p)` with the same filter; its template part
(not `PROJECTILE`/`INERT`/`MINE`, `STRUCTURE`, `MP_COUNT_FOR_VICTORY`, build facility) is looked up once per template. The type list of a
count (`objectTypesFromParam`) is resolved once per count instead of per object – within the count it does not change. Evaluation order,
counters of missing types, edge memories, one-shot deactivation: untouched. The same 690,622 evaluations before and after.

**4 – Enemy sighted.** Without a relationship override of a script, "enemy" is the relationship of the two owners (`AreEnemies` →
`RelationshipOf`); only objects of the enemy players of the member can answer. The question is "is there one", the order is irrelevant.
The system player and unknown owners are nobody's enemy. With an override (`_world.RelationshipOverride`) the search stays per object.

**5 – Object → team.** The rule *when* the table is refreshed (different tick or different member sum) is unchanged – it
decides what a caller sees after a member change within a tick, and an "always fresh" table would be a
behaviour change. Only the work is saved: every change of a membership or of the team set counts a stamp up
(`TeamMembers`, `TeamTable`); if it still stands, counting would give the same sum and filling the same table. For this `ScriptTeam.Members` is a
`TeamMembers` (`IReadOnlyList<uint>`) instead of a `List<uint>`; only the VM can change it.

**6 – Target acquisition.** Like 4: without an override a victim must belong to an enemy player (`IsAttackable` → `AreEnemies`). The checks per
candidate are the same code (`ConsiderMoodTarget`). The selection is the maximum of a strict order – (effective priority, priority, closer,
smaller id) for a unit with attack priorities, otherwise (closer, smaller id); which one applies depends only on the unit –, so
independent of order. No check draws a random number or changes state.

**7 – Checkpoint.** The relationship is one of the owners; candidates are the objects of the players with the sought relationship. Selection =
minimum of (edge distance, id). The system player is never an enemy and is excluded for allies; an owner that the world does not know is only
its own ally (that is what `RelationshipOf` says, and that is how it is rebuilt). Range test: if a candidate lies on one axis farther
away than range + both radii (estimated upwards: major + minor semi-axis + 1, without a root) + 1 unit, then its edge distance
is greater than the range, whatever the other axis says; the one unit is far more than the rounding of the squares and of the root can take away
(together below 2⁻¹⁵). The distance test itself is unchanged.

**8 – Script loop.** Team conditions are loops over the member ids instead of LINQ with lambdas (a living member is, as in
`LiveMembers`, one that exists and is not marked for removal). The scripts and groups per list are fixed as arrays in
program order (the program never changes); whether a script or a group is active is still asked only when it is its
turn. The instances of the condition team of a script are collected, as before, before the first evaluation in id order, only without a
closure and without an array in the normal case (none or one). `PruneTeams` now looks only at the names that are not yet known as "has existed"
(a set derived from the two tables, rebuilt on loading). Memory keys of the enter/leave conditions, the argument
of a standing order and the text of an "unknown …" diagnostic are functions of their input and are built once per input; the diagnostic
is still counted on every run.

**9 – Root.** Newton's iteration over the integers falls from every start value ≥ root to ⌊√target⌋ and stays there; the start value
2^⌈bits/2⌉ lies above the root. The same value for every input, only fewer divisions.

**10 – `IsKindOf`.** The same comparison rule (`OrdinalIgnoreCase`) over the same names. No new field on the template.

**11 – Demo trap.** If an object lies on one axis more than one unit beyond the trigger range, its squared distance is at least
range² + 2·range + 1; the rounding of the two products (half a raw-value unit each) does not bring that below the limit. The circle test,
the list sorted by id, the order of the checks on it and the early return for a friend in range are unchanged.

**12 – Stealth detection.** The checks are independent of each other and have no side effects; their order decides only what is looked at in vain.
The objects that pass, and the order in which they are marked as detected, are the same.

**14 – Dock search.** Only an object with a dock module has a cost (`Cost` first asks `DockOf`); the objects with an `IDockModule` are therefore
all the candidates there are (a superset to which the same test is applied). Selection = minimum of (cost, id). The rally-point search
(`Closest` by kind) runs over the kind list in the same id order as the search it replaces.

**Not used** (as excluded by the brief): evaluating scripts less often, staggering, budgets, changed order, approximations, threads,
wall-clock time, `float`.

## 5. Tests

`tests/CnCNew.Simulation.Tests/Tests.Indexes.cs`, all through the public interface, on worlds that are changed randomly with a fixed seed
(creating, killing, removing, change of owner, combat):

| Test | Comparison |
|---|---|
| `WorldEntityListsMatchScan` | owner, kind and module lists against the filtered search over all objects (members and order): asked from the first tick on, asked only in the middle of the run and only at the end, and in sessions restored from the snapshot (four times in the run); asking does not change the world hash |
| `ScriptObjectQueriesMatchScan` | the object conditions of the scripts (read through `ExplainScript`: everything destroyed, build facilities, building counts, count by type and type list, in the area by type and kind, units and value in the area, built by player) against the search with the old filter and sorting; also directly after a kill or change of owner that no tick has processed yet, and after restoring |
| `EnemyOwnerSearchesMatchPerObjectSearch` | target acquisition and "enemy sighted" by enemy player against the search per object: a second session gets a relationship override that changes no relationship, and must have the same world hash and the same script counters tick by tick |
| `CheckpointScanMatchesWorldScan` | what checkpoints of both players and of the system player see, against the search over the world with edge distances, in all four situations |
| `FixedSqrtMatchesReferenceIteration` | over 40,000 values (powers of two, square numbers and their neighbours, map distances, random patterns) against the old iteration and against the definition of the rounded-down root |
| `KindLookupMatchesComparerLookup` | `IsKindOf` against `ImmutableArray.Contains` with the comparer |

The demo trap, the stealth detection, the laser and the dock search have no comparison test of their own against their old search (their search is not
callable from outside); they are under their existing behaviour tests and under the manifest (`MD_USA05`: 15 traps; `MD_GLA05`: 27 lasers).

## 6. Result

### 6.1 Before/after (not measured quietly)

State `74ccdde` against the final state, `perf-mission`, 3,000 ticks, per map alternately old, new, old, new; during the runs our own
check runs and the work of four further agents were running. Times in µs. The yardstick for the load is `AIUpdateInterface` per call (not changed here).

**`MD_GLA05`** (589×589 cells, 12 sides, 2,249–2,304 objects; end hash `826CCD82C8536DC2`, chain `10330D7D179054FD` in all four runs)

| Quantity | old 1 | new 1 | old 2 | new 2 |
|---|---:|---:|---:|---:|
| Tick, mean | 54,152 | 33,420 | 49,529 | 24,647 |
| Tick, median | 40,229 | 17,289 | 35,414 | 18,145 |
| Phase `MissionScripts` per tick | 13,469 | 953 | 12,130 | 962 |
| Phase `Acquisition` per tick | 2,325 | 208 | 2,015 | 229 |
| Phase `Updates` per tick | 21,167 | 16,007 | 20,255 | 11,243 |
| `PointDefenseLaserUpdate` per call | 217.9 | 1.80 | 216.4 | 1.43 |
| `CheckpointUpdate` per call | 522.5 | 66.4 | 461.8 | 60.6 |
| `StealthDetectorUpdate` per call | 169.8 | 96.3 | 147.1 | 96.0 |
| *Yardstick* `AIUpdateInterface` per call | 857.8 | 1,009.1 | 812.0 | 639.3 |
| **Bytes per tick** | **4,093,975** | **15,501** | 4,093,968 | 15,516 |
| GC Gen 0/1/2 in 3,000 ticks | 1,435/19/2 | 6/2/0 | 1,436/18/2 | 7/3/1 |

**`MD_USA05`** (527×521 cells, 10 sides, 1,835–1,909 objects; end hash `1CB28E6AA923BDEF`, chain `0816C3438CB7F689` in all four runs)

| Quantity | old 1 | new 1 | old 2 | new 2 |
|---|---:|---:|---:|---:|
| Tick, mean | 21,626 | 24,353 | 26,603 | 14,201 |
| Tick, median | 14,478 | 11,233 | 16,361 | 9,853 |
| Phase `MissionScripts` per tick | 1,901 | 725 | 1,521 | 527 |
| Phase `Acquisition` per tick | 1,340 | 133 | 1,520 | 106 |
| Phase `Updates` per tick | 8,811 | 10,481 | 10,928 | 5,384 |
| `DemoTrapUpdate` per call | 139.6 | 64.5 | 196.6 | 41.0 |
| `StealthDetectorUpdate` per call | 154.1 | 60.5 | 151.6 | 43.2 |
| `WorkerAIUpdate` per call | 356.3 | 378.0 | 438.6 | 254.0 |
| *Yardstick* `AIUpdateInterface` per call | 719.9 | 1,390.8 | 931.2 | 515.5 |
| **Bytes per tick** | **209,110** | **20,147** | 208,752 | 20,150 |
| GC Gen 0/1/2 in 3,000 ticks | 75/6/0 | 8/2/0 | 75/6/0 | 7/1/0 |

Reading: the machine was about four times slower in these runs than in the quiet measurement (old: 49–54 ms per tick against quiet 12.6–13.5 ms),
and the load fluctuated from run to run (yardstick 516 to 1,391 µs). Statements that this supports:

- The **allocation** is deterministic: `MD_GLA05` from 4.09 MB to 15.5 KB per tick (factor 264), `MD_USA05` from 209 KB to 20 KB. Gen 0 collections
  in 3,000 ticks from 1,435 to 6 to 7.
- **Script phase** `MD_GLA05`: about one thirteenth (13,469/953, 12,130/962), `MD_USA05`: about 2.6 to 2.9.
- **Target acquisition**: about one ninth to one twelfth on both maps.
- Laser 1/120 to 1/150, checkpoint about 1/8, demo trap 1/2 to 1/5, stealth detection `MD_USA05` 1/2.5 to 1/3.5.
- The **whole tick** of `MD_GLA05` becomes clearly shorter in both pairs (median 40 → 17 and 35 → 18 ms); how much of that remains when quiet,
  only the quiet measurement will say. If one converts the quiet starting values (PERFORMANCE.md §6.2: scripts 2.8–3.0 ms, target acquisition 0.50–0.54 ms, laser
  30.6 % of 4.6–5.3 ms updates) with the ratios above, about 4.5 to 5 ms of 12.6–13.5 ms drop out in `MD_GLA05`. That is a
  calculation, not a measurement.

### 6.2 Checks on the final state

| Check | Result |
|---|---|
| `reference-manifest check --set quick` after each of the commits 1–12 (table §4), each built at exactly this state | 9 runs identical, twelve times |
| `reference-manifest check --set full` | 50 runs identical (3,297 s under load, `--jobs 5`); at the same time the check of commit 14 |
| `input-manifest check` | identical |
| `reference-manifest check --set quick --verify-incremental` | 60,896 ticks checked, 0 deviations, 9 runs identical |
| `CampaignTool audit --ticks 900 --campaign CHALLENGE_0` | 7/7 deterministic, 7/7 identical after save/load; output equal to that of the starting state except for the times; end probes victory 7/7, defeat 6/7 (as before) |
| `CampaignTool audit --ticks 900 --campaign GLA` | 5/5 and 5/5; output equal to that of the starting state except for the times; end probes victory 1/5, defeat 2/5 (as before) |
| `CampaignTool drive --campaign CHALLENGE_0` (60,000 ticks) | victory and defeat on 7 of 7 maps through the script of the mission |
| `tests/CnCNew.Simulation.Tests` (Debug) | all passed |
| `tests/CnCNew.Simulation.Tests -c Release -- --long --content-dir …` | 370 tests passed (368 in the run from a copy of the output directory; the two source rule tests do not find the repo root from there and were repeated in place: passed) |
| `tests/CnCNew.Game.Tests -c Release -- --only CampaignTests,CampaignShellTests,MissionPresentationTests,MissionHudTests,MissionEffectsTests,MissionTextTests` | passed (728 s) |
| clean solution build | `dotnet build CnCNew.slnx` after deleting `bin`/`obj` of the client: 0 warnings, 0 errors |

**Duration of the campaign audit per map** (900 ticks; not quiet, old and new state one after the other under different load):

| Campaign | old, s per map | new, s per map |
|---|---|---|
| `CHALLENGE_0` (7 maps) | 32, 67, 23, 30, 35, 108, 106 (sum 401) | 15, 26, 10, 22, 13, 54, 59 (sum 199) |
| `GLA` (5 maps) | 45, 41, 84, 122, 168 (sum 460) | 76, 43, 98, 117, 146 (sum 480) |

The goal "under one minute per map" is reached for `CHALLENGE_0` even under load. For `GLA` this measurement shows **no** gain and
three of five maps above one minute; the new run ran together with the full manifest. Whether the maps stay under one minute when quiet
(the handoff names 20–60 s as the quiet starting value) is not verified.

## 7. What remains and why

Measured at the final state (`MD_GLA05`, `--script-detail`, under load; orders of magnitude):

| Remainder | Size | Why it remains |
|---|---|---|
| Conditions: 230 evaluations per tick | about 260 µs per tick under load, 1 µs per evaluation | The original evaluates every active script every frame; evaluating less often or skipping is excluded. What would still be possible: `TIMER_EXPIRED`/`FLAG`/`COUNTER` (102 per tick) look up their counter by name in a sorted dictionary; remembering the counter per condition demands an invalidation on `Load` and after `ExplainScript` and brings an estimated 10–30 µs per tick. Not done. |
| Section TeamEvents | 100–220 µs | Per team with an event script several passes over the members (living, mobile, idle) with small arrays (3.1 KB per tick). |
| Section Followers | 60–150 µs, 5.5 KB per tick | Standing orders; the hunt (`NearestEnemy`) still sorts all objects, commands are created through closures. |
| Section Housekeeping | 55–105 µs | `PruneTeams` asks per tick for every team member whether its object still exists. That could be skipped only with a proof that since the last time nothing was removed and nothing was added (counter of the world, stamps of the memberships, four further tables); the gain is out of all proportion to the risk. |
| Target acquisition | 105–230 µs per tick under load | The base cost per object that searches for targets, and the looks over the objects of the enemy players. |
| `DemoTrapUpdate`, `CheckpointUpdate`, `StealthDetectorUpdate`, search of the team events | 41–96 µs per call | They still go over a list of objects and discard almost all of them. That is properly remedied only with a **spatial index** of the world. It is missing because `Entity.X/Y` are simple properties that the movement sets – an index that is carried along on every position change intervenes in `Entity` and in the movement modules, on which others are working in parallel. Proposal for afterwards: a uniform grid in `SimulationWorld`, maintained in a position setter; the callers here are prepared for it (each justifies why its result does not depend on the order of the candidates). |
| `StealthDetectorUpdate` in `MD_GLA05` | 96 µs per call | There it is the height line of sight to the stealthed objects in range (`TerrainMap.HasHeightLineOfSight`), territory of vision. |
| `WorkerAIUpdate`, `SupplyTruckAIUpdate`, `TransportAIUpdate` | 250–1,000 µs per call | Movement and pathfinding, not subject of this sub-brief. |
| First tick | 290–390 ms under load, of which 220–280 ms JIT | A matter of process start (ReadyToRun or a warm-up run would be decisions about delivery, not a change of the simulation); the rest are the actions of the first tick, above all the map reveal (vision). |
| `SimulationWorld.IsPlayerDefeated` | rare | Still one LINQ pass over all objects per call (victory check, `MULTIPLAYER_*` conditions); did not come to the top in any profile, and the file belongs to others in this phase. |
| Hash, vision, vision hash, `AIUpdateInterface` | together the largest part of the tick | Other sub-briefs of the phase. |

Open:

- **The quiet measurement** (`scripts/perf-profile.ps1`, PERFORMANCE.md §4) on the merged state of the phase. Only it says how much of a core
  a mission tick still needs at 60 Hz; all times here arose under load.
- **Audit duration of the `GLA` campaign** and of the remaining campaigns to be re-measured quietly (§6.2).
- **Merging:** outside its territory this work changes one place each in `ObjectTemplate.IsKindOf` (loop) and `Fixed.Sqrt`
  (start value), adds `SimulationWorld.Indexes.cs`, inserts four lines in `SimulationWorld.cs` (`Register`, `ProcessDestroys`) and one each in
  `SimulationWorld.Scripting.cs`/`.PowerAndCapture.cs` (`SetOwner`) and extends `IWorldServices` by four members. With the parallel
  work on movement, vision and hash there can be conflicts there; `tools/CnCNew.SimTool/PerfCommand.cs` is likewise being edited in parallel.
- `docs/PERFORMANCE.md` §6.3 item 5 (4 MB attributed to the scripts) is superseded by §2.1; the document itself has not been touched
  in this work.
