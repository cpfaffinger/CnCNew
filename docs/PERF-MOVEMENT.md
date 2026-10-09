# Performance: movement and pathfinding (Phase 19)

As of: 6 October 2026, working branch `worktree-agent-a26ca0ad009d62c7a`, starting state `74ccdde`. Brief: `docs/handoffs/HANDOFF-PHASE19.md`,
cost centre "movement and pathfinding" – `AIUpdateInterface` and what it calls (`NavigationGrid`, `SimulationWorld.Collision.cs`),
including the modules derived from it (`TransportAIUpdate`, `SupplyTruckAIUpdate`, `DozerAIUpdate`, `AssaultTransportAIUpdate`,
`ChinookAIUpdate`, `WorkerAIUpdate`). The measuring tools are in `docs/PERFORMANCE.md`; this document describes only what was added for this
cost centre, what was measured, what was changed and what remains.

## 1. The rule

**No result changes.** Every change yields the same paths (cell by cell, waypoint by waypoint), the same avoidance steps,
the same collisions in the same order and thus the same hashes. Checked after every commit with
`SimTool reference-manifest check … --set quick` (9 runs, unchanged) and at the end with `--set full` (§8). Permitted were only: faster
ways of computing with the same value, caching of answers that demonstrably cannot change, data structures without allocation,
and ending a search early whose result is already certain. Not touched: spreading work over ticks, node budgets,
approximations, changed tie-breaking rules (§7).

What the pathfinding keeps between two calls (search records per cell, queue, buffers, remembered answers) is working memory
of the grid and **not state**: not in the hash, not in the snapshot. A restored world begins with empty buffers and computes
the same paths.

## 2. Measuring tool

The simulation has no clock (`docs/PERFORMANCE.md` §1). For the movement there is a second hook beside `ITickPhaseObserver`:

- `IMovementProbe` (`src/CnCNew.Simulation/Terrain/MovementProbe.cs`), set via `NavigationGrid.Probe`, normally `null`, no
  state. The simulation reports the beginning and end of four kinds of work (`MovementWork`: `PathSearch`, `StationaryBlockers`, `LocalAvoidance`,
  `FootprintCollisions`) and per path search a `PathSearchInfo` with deterministic counters: start cell, goal cell, surfaces, result,
  **expanded cells** (entries taken from the queue, as the budget counts them), insertions into the queue,
  escape cells, temporarily blocked cells, cells and waypoints of the path, distance of the path end from the goal, and whether the answer came from
  memory or the search ended early.
- `SimTool perf … --movement [--movement-tick N]` and `perf-mission … --movement` (`tools/CnCNew.SimTool/MovementRecorder.cs`) read the
  clock there and print: calls, sum, µs per tick, mean, maximum and allocation per call of the four kinds of work; the searches by result, by
  calling module, by size and (for partial paths) by distance of the path end from the goal; the five ticks and the five units with the most
  expanded cells; all searches of one tick (`--movement-tick`, otherwise the most expensive). The counters are also in the `summary` line
  (`move_*`).
- `SimTool path-bench <content> --map <karte> [--searches N] [--rounds N] [--seed N]` (`tools/CnCNew.SimTool/PathBench.cs`): a fixed
  list of searches on the terrain of a real map (without buildings), several times; every search counts with its fastest pass. Output:
  expanded cells, ns per expanded cell and a checksum over all returned paths that must be the same for every state that
  searches the same way.

**The counters are the yardstick.** The machine was occupied by four further agents during the whole work (CPU load 100 %); the same
binary ran, depending on the moment, slower by a factor of 2 to 4 than in the quiet measurement of Phase 18 (the phase `Vision`, which
nobody touched here, needed 2,262 to 2,971 µs per tick instead of 716 when quiet). All times in this document are therefore **not measured
quietly**; comparisons come from runs of two states side by side. Cells, calls and searches by contrast are the same in every run.

## 3. Where the time went (starting state)

Twilight Flame, 8 AI players, 54,000 ticks (final hash `0A69FC72FF6975CD`, chain `78D32DCBCE8A99EB`), and `MD_GLA05`, 3,000 ticks (final hash
`826CCD82C8536DC2`); state `74ccdde` plus probe, not measured quietly:

| Work | 8 players: calls | µs per tick | mean µs | `MD_GLA05`: calls | µs per tick | mean µs |
|---|---:|---:|---:|---:|---:|---:|
| `StationaryBlockersAhead` | 4,773,549 | 1,975 | 22.3 | 24,610 | 1,138 | 138.7 |
| `AvoidLocalBlockers` | 5,267,887 | 1,961 | 20.1 | 36,108 | 1,302 | 108.2 |
| Path search | 83,462 | 829 | 536 | 595 | 1,287 | 6,490 |
| `ResolveFootprintCollisions` | 5,333,813 | 251 | 2.5 | 37,057 | 2,034 | 164.7 |
| Sum | | 5,016 | | | 5,761 | |
| Phase `Updates` in the same run | | 5,849 | | | 11,540 | |

In the skirmish 86 % of the update phase was in these four kinds of work; the rest of `AIUpdateInterface` (steering, braking, dock and
supply state machines) is small beside it. Four causes:

1. **Fixed-point arithmetic via 128-bit division.** `Fixed` computed every product and every quotient via `Int128` (one 128-bit division
   executed in software per operation) and the root as a Newton iteration on `Int128`. A circle test costs three to ten products.
2. **Every moving ground unit scans all objects of the world up to seven times per tick** (`StationaryBlockersAhead` in every tick, not
   every 30; up to five `StepBlocked` in `AvoidLocalBlockers`; `ResolveFootprintCollisions`). Per pair the bounding circle was recomputed
   (`BoundingCircleRadius`: string comparison against `BOX`, for boxes additionally a root), and the enumerator ran over
   `IReadOnlyCollection<Entity>` (one allocation per scan). In `MD_GLA05` there are 2,250 objects for a handful of moving units.
3. **Path searches whose result is certain or that nobody needs.** Of 213,410,509 expanded cells 105,200,000 fell to 526 searches
   that exhausted their budget of 200,000 cells:
   - 304 of them were detour searches (`ReplanAround`, every 30 ticks per unit with stationary units ahead), whose result only counts if
     it reaches the goal – the partial path was discarded. In `MD_GLA05` those were 85 searches with 12.3 of 12.34 million cells; a single
     unit (`AmericaTankCrusader` #2211) flooded the map 84 times.
   - 214 were commands to a place where nobody can stand. 131 of 137 such searches (after deducting the repetitions) end at most
     two cells before the goal: the search had its answer after a few hundred cells and afterwards expanded everything reachable.
   - In addition groups: units that ask the same question in the same tick (same start cell, same goal).
4. **The search itself**: per neighbouring cell a bounds check, obstacle counter, terrain flags and up to two `HashSet` queries (escape cells,
   temporarily blocked cells), four separate arrays per cell, per search 1,060 to 1,121 bytes of allocation (list, builder, queue), a
   heap with up to 70,000 entries.

### The 547 ms tick (tick 52,843 of the 8-player run)

Measured quietly 547 ms (Phase 18), in the runs here 1,196 ms. The probe shows: **73 path searches in one tick**, together **5,350,066
cells**. The list of the probe is limited to 60 lines; all 60 come from `AIUpdateInterface`, go to the same goal (cell 436,312),
are successful and expand 71,628 to 82,361 cells each (path 347 to 363 cells, 10 to 11 waypoints). It is an infantry group
(`Infa_ChinaInfantryTankHunter`, `Infa_ChinaInfantryMiniGunner`) that plans its path in the same tick (the same command or the same replan
after a footprint change; the probe does not distinguish that) and whose members stand on only six different cells (331,74
twenty-three times; 332,75 seventeen times; 331,75 eleven times; 316,60 six times; 329,72 twice; 333,76 once). The goal lies in a building footprint (180 escape cells), the path leads around obstacles that the
octile heuristic does not know; therefore every search expands a third of the map. No budget run, no unreachable goal: dozens of times
the same expensive, correct search.

After the changes: the same 73 requests, but among the 60 shown only **six real searches** (one per start cell, together
444,697 cells), all other answers come from the memory of the grid (§4.4); the six searches needed together about 45 ms in the loaded run.
The tick is thus cheaper by about a factor of 10 and no longer among the five most expensive of the run, but not below the target
of 8 ms: six searches with 72,000 to 82,000 cells each remain (§6).

## 4. The changes

Every change is one commit; the numbers are also in the commit message. "Side by side" means: both states ran at the same time on
the same loaded machine.

### 4.1 `Fixed`: product, quotient and root without 128-bit division (`40fcbc6`)

The product is the 128-bit product of the magnitudes (`Math.BigMul`), rounded by "add half, shift"; the quotient computes in
64 bits if magnitude(a) · 2¹⁶ fits (otherwise the previous 128-bit path); the root runs as the same Newton iteration on 64-bit words
if raw · 2¹⁶ fits.

*Why result-neutral:* "to the nearest, on a tie away from zero" is, on magnitudes, "add half, truncate" – the same formula
as before, only without general division. The Newton iteration ends from every start value above the root at ⌊√⌋; the nearer start value
changes the number of steps, not the result. Overflow throws as before. *Test:* `FixedFastPathsMatchWideArithmetic` compares 1.6 million
products and quotients and 1.2 million roots with the 128-bit formulas kept in the test (boundary values, random bit widths, the ranges
of the simulation). *Side by side, 18,000 ticks, µs per tick:* tick 11,678 → 7,228; `Updates` 7,530 → 3,393; `StationaryBlockers` 2,536 → 1,051;
`LocalAvoidance` 2,269 → 580; `FootprintCollisions` 440 → 106; path search 1,188 → 955; `Acquisition` 166 → 100. The change acts on every
cost centre that computes in fixed point.

### 4.2 Object scans of a movement step (`4605da9`, `e5ebf91`, `e0a93fa`, correction `309dc7d`)

- The bounding circle of a template (`ObjectTemplate.BoundingCircleRadius`) is computed once per template and looked up in a table of the **world**
  by template index (the template is immutable, the value cannot go stale). The first attempt (`4605da9`) had put the
  value as a field into the template; that changed no simulation result, but the checksum of the catalogs in the input manifest, which
  describes the catalogs field by field (`input-manifest check` reported `catalogs.templates` and `catalogs.whole` as different). The correction
  restores `ObjectTemplate.cs` byte-identically to the starting state. **Note:** templates and other catalog types get no fields in a
  result-neutral phase; derived values belong in the world.
- The scans use the struct enumerator of the object table instead of `IReadOnlyCollection` (no allocation, same order).
- Before the exact circle test a pair is dropped if one coordinate alone lies further apart than the limit plus one unit. One
  unit is far more than the rounding of a product (at most 1/131,072): (limit + 1)² exceeds limit² by at least 1, so
  the rounded square of the coordinate difference stays above the rounded square of the limit, for `<` as for `<=`. The exact test itself is
  unchanged.
- The four look-ahead points of `StationaryBlockersAhead` depend only on the moving unit and are computed once per call.
- `StationaryBlockersAhead` answers in a list of the world (the only caller plans immediately and keeps nothing).
- **Lists instead of all objects:** the world keeps two lists in ascending id order, filled and emptied exactly where an object
  enters the object table and leaves it (`TrackCollidable`/`UntrackCollidable`): the objects with a locomotor (only they are considered by the
  avoidance and stationary-blocker scan) and the objects with `ToppleUpdate` or `SquishCollide` (only for them do `TryTopple`/`TrySquish` do
  anything). Template and module list of an object never change; the scans visit the same objects in the same order as before,
  without those they skipped or for which nothing happened. `TryTopple`/`TrySquish` look for their module in a loop instead of with
  `OfType().FirstOrDefault()`.
- **Position first:** the world remembers the largest bounding circle (as a magnitude) of all mobile objects that it has registered. An object that
  is further away on one axis than the radius of the unit plus this radius plus one unit does not pass the exact test with its
  own, smaller radius either and is discarded after reading its position, before anything else is read from it.

*Test:* `MovementScansMatchTheFullScan` compares `AvoidLocalBlockers` and `StationaryBlockersAhead` with the earlier full scans (kept in
the test) on six random scenes, about 86,000 pairs of unit and step, with objects that come into being and vanish between the rounds.
The crush path is covered by the existing collision tests and the manifest runs (`MD_USA05`, `MD_GLA05`).

### 4.3 Path search: one state byte per cell, no allocation (`46e7415`)

The same search: the same A*, the same neighbour order, the same keys, the same budget counting (stale entries of the
queue count too), the same rule for the nearest cell, the same smoothing. What is changed is how it reads and writes: one byte per
cell (terrain class, "covered by a footprint", two marks of the running search for escape cells and temporarily blocked cells), a
table with 32 entries per search for "may be entered", one record of 16 bytes per cell instead of four arrays, the four straight
neighbours are checked once and reused for the diagonals, all intermediate results lie in buffers of the grid (1,121 → 88 bytes
per search, that is the result itself).

*Test:* `PathSearchMatchesTheOriginalSearch` compares 24,000 searches on ten random maps (cliffs, walls with gaps, a
closed ring, a lake, footprints that come and go, blockers, starts and goals in footprints, repetitions) point by point with
the earlier search, which is kept in the test; `PathSearchAtTheBudgetMatchesTheOriginalSearch` the same for searches at the budget on a map
with 520 × 440 cells. *Side by side, 18,000 ticks:* path search 1,757 → 851 µs per tick (463 → 225 ns per expanded cell); counters
unchanged (29,337 searches, 68,227,327 cells, 74,908,102 insertions).

### 4.4 The grid remembers answers until a footprint changes (`978d9c1`)

A search without temporary blockers is a function of start cell, goal cell, surfaces, terrain (immutable) and footprints.
As long as no footprint is added or vanishes, the same question has the same answer, whoever asks and in whichever tick. The grid
keeps up to 4,096 answers and discards all of them as soon as `AddFootprint`/`RemoveFootprint` runs. Only the last point of a path that
arrives is the exact goal position of the asker instead of a cell centre; it is inserted when the answer is given out again. Searches around temporary blockers
are never remembered (their cells belong to the question).

*Test:* a third of the 24,000 questions is asked again, in the same round or after a footprint change, half of them with a different
exact goal position in the same cell. *54,000 ticks:* 29,315 of 83,462 searches from memory (35 %).

### 4.5 A detour search that cannot arrive stops (`a8aeec5`)

`NavigationGrid.FindPathToGoal` returns the path of `FindPath` if it arrives, otherwise `null`; `ReplanAround` uses it. It is the same
search, in addition: a goal cell that the search may not enter ends it immediately. And beside the search a flood runs from the goal
(1,024 cells per 1,024 taken from the queue), backwards along the moves of the search: into an open cell, diagonally only between
two open cells. These moves are the same in both directions, only the start itself does not have to be open. If the flood is complete
without having hit the start, there is no path, and the search ends; if it hits the start, it is switched off.

*Why result-neutral:* a search that arrives yields exactly the path as before. One that does not arrive yielded a path that the
caller discarded. *Test:* each of the 24,000 questions additionally via `FindPathToGoal`; goals behind a closed wall; long searches that
arrive while the flood runs along for many steps; goals that enclose their blockers. *`MD_GLA05`:* 12,340,044 → 118,878 expanded
cells in the whole run (with §4.7 then 55,217).

### 4.6 Queue as a ring of small heaps (`7c73537`)

The order in which a search takes cells from its queue depends only on the enqueued values (smallest total cost,
then smallest cell index), not on the data structure. The total costs of the enqueued cells never lie below those of the cell that is
being expanded (the octile distance drops per step by at most its cost), and at most 70 above it (the most expensive step, diagonally
onto an escape cell, costs 56, the distance grows by at most 14). The queue is therefore a ring of 128 heaps of cell indices,
one per total cost value. The same cells in the same order, including the stale entries that count towards the budget. A
value outside the window would be a faulty search and throws. *`path-bench` side by side:* 127.0 → 103.7 ns per expanded cell,
cells (13,746,547) and path checksum (`A38F733774279489`) the same.

### 4.7 A search ends when its nearest cell is certain (`64e5979`)

After 256 expanded cells and at every doubling the search asks whether its answer can still change, provided its nearest
cell is at most 16 cells away from the goal:

- A cell with the *same* distance to the goal wins only with lower cost (or equal cost and a smaller index). Every cell that
  is still expanded has at least the total cost of the next cell of the queue; as soon as these exceed the total cost of the
  nearest cell, a cell equally far away costs more.
- A cell with a *smaller* distance lies in a small block around the goal. A flood from the open cells of this block, backwards
  along the moves of the search, either hits a cell that this search has enqueued or expanded (then a nearer cell is
  reachable: keep searching), grows beyond 4,096 cells (keep searching) or runs dry: then no path leads from the start to a nearer cell,
  because every such path would have to enter a cell of the flood.

If both hold, the search ends and returns the path to the nearest cell. Its predecessor chain is certain, because expanded cells are never
changed again: it is the path that the search would have returned after flooding the map. The goal itself lies in the block; the search
would therefore not have arrived either.

*Test:* the roughly 4,000 partial paths among the 24,000 random searches; in addition, on the map with 520 × 440 cells, goals on single cliff cells,
on the wall, behind it, in and on a cliff ring, for two surface sets, each plain, around blockers and via
`FindPathToGoal`; the test checks via the probe that the early end occurs. *54,000 ticks:* searches at the budget 138 → 0; 393 searches end
early after together 265,472 cells.

## 5. Before and after (not measured quietly)

Starting state (`74ccdde` plus probe) and final state ran side by side on the loaded machine. Counters are exact; times are higher by a
factor of 3 to 4 than on a quiet machine and fluctuate between the runs (the column "Vision" shows the load: nobody touched this
phase in this branch, quiet it was 716 µs). "After" is the state `e0a93fa`; the correction `309dc7d` after it reads the same
bounding circle from a table of the world instead of from a field of the template and has not been re-measured separately.

### 5.1 Twilight Flame, 8 players, 54,000 ticks (final hash `0A69FC72FF6975CD`, chain `78D32DCBCE8A99EB` in both)

| Quantity | before | after | factor |
|---|---:|---:|---:|
| Phase `Updates`, µs per tick | 11,812 | 1,670 | 7.1 |
| `AIUpdateInterface`, sum ms (4,974,122 calls) | 505,187 | 36,327 | 13.9 |
| `AIUpdateInterface`, mean µs per call | 101.6 | 7.3 | 13.9 |
| `TransportAIUpdate`, sum ms | 26,367 | 1,624 | 16.2 |
| `SupplyTruckAIUpdate`, sum ms | 24,956 | 2,548 | 9.8 |
| `DozerAIUpdate`, sum ms | 8,633 | 1,153 | 7.5 |
| `ChinookAIUpdate`, sum ms | 10,727 | 6,638 | 1.6 |
| `StationaryBlockersAhead`, µs per tick (µs per call) | 4,098 (46.4) | 121 (1.37) | 33.9 |
| `AvoidLocalBlockers`, µs per tick (µs per call) | 3,842 (39.4) | 215 (2.20) | 17.9 |
| `ResolveFootprintCollisions`, µs per tick (µs per call) | 491 (4.97) | 22 (0.22) | 22.5 |
| Path search, µs per tick | 1,586 | 231 | 6.9 |
| Path searches (requests) | 83,462 | 83,462 | – |
| of which from memory | 0 | 29,315 | – |
| expanded cells | 213,410,509 | 76,822,493 | 2.8 |
| Searches at the budget (200,000 cells) | 526 | 0 | – |
| ns per expanded cell | 401 | 162 | 2.5 |
| Allocation per search / per scan, bytes | 1,060 / 44–134 | 158 / 0 | – |
| most expensive tick by cells | 52,843: 5,350,066 | 25,198: 1,891,288 | 2.8 |
| Tick 52,843: real searches / cells (60 requests shown) | 60 / 4,405,450 | 6 / 444,697 | 9.9 |
| Allocation per tick, bytes (whole tick) | 95,627 | 75,938 | – |
| Tick, mean µs (all cost centres; Vision 2,971 and 2,262 respectively) | 18,661 | 6,895 | 2.7 |

The mean tick contains vision and hash, on which others are working; in the final state they make up 71 % of the tick (`Vision` 2,262, `VisionHash`
1,281, `Hash` 1,332 of 6,895 µs in the loaded run).

### 5.2 `MD_GLA05`, 3,000 ticks (final hash `826CCD82C8536DC2`, chain `10330D7D179054FD` in both)

| Quantity | before | after | factor |
|---|---:|---:|---:|
| `AIUpdateInterface`, sum ms (33,465 calls) | 31,477 | 581 | 54 |
| `AIUpdateInterface`, mean µs per call | 940.6 | 17.4 | 54 |
| `SupplyTruckAIUpdate`, sum ms | 1,343 | 78 | 17 |
| `TransportAIUpdate`, sum ms | 844 | 38 | 22 |
| `StationaryBlockersAhead`, µs per call (24,610 calls) | 262.9 | 2.98 | 88 |
| `AvoidLocalBlockers`, µs per call (36,108 calls) | 207.4 | 3.37 | 62 |
| `ResolveFootprintCollisions`, µs per call (37,057 calls) | 296.6 | 8.31 | 36 |
| Path search, sum ms (595 requests) | 9,283 | 52 | 178 |
| expanded cells | 12,340,044 | 55,217 | 223 |
| Phase `Updates`, µs per tick | 21,698 | 9,436 | 2.3 |
| Tick, mean µs (Vision 5,333 and 6,536, scripts 8,074 and 11,456 respectively) | 46,575 | 38,342 | – |

The load was even higher in this run than in the skirmish (quiet, the tick of this mission needed 13.5 ms). In the update phase
`PointDefenseLaserUpdate` (17,761 → 16,005 ms, a different cost centre) remains with 57 % of the module time; `AIUpdateInterface` is at 2 %. The 85
detour searches that previously flooded about 145,000 cells each now end after 256 cells (83 via §4.7, before §4.5 takes effect).

### 5.3 Classification against the targets

A quiet measurement was not possible. If one converts the loaded times with the ratio that the unchanged phase `Vision` shows in the
same run (2,262 µs instead of 716 quiet, factor 3.2), the update phase of the 8-player run would be at about **0.5 ms per tick** (starting
value measured quietly: 3.26 ms) and the share of this cost centre (path search and the three scans, 589 µs loaded) at about
**0.2 ms**. That is an estimate, not a measured value; the quiet measurement with `scripts/perf-profile.ps1` is outstanding and belongs to the coordinator.

| Target (whole tick, all cost centres) | this cost centre after the changes |
|---|---|
| Mean ≤ 1.0 ms | estimated 0.2 ms (path search about 70 µs, scans about 110 µs); the rest of the update phase about 0.3 ms |
| 99 % ≤ 3 ms | not verifiable under load; the ticks with group searches (about 2.5 % of the ticks have ten or more requests) determine it |
| Maximum ≤ 8 ms | **not reached**: the most expensive tick expands 1,891,288 cells (69 searches); at an estimated 50 ns per cell quiet that is about 90 ms |
| no gen-2 collections, allocation near 0 | scans 0 bytes; per search 158 bytes (the result); the remaining 76 KB per tick lie outside |

## 6. What remains, and why it does not drop further without a change of results

1. **Many long, successful searches in one tick.** The most expensive tick (25,198) has 69 requests with 1,891,288 cells; the ticks 28,144,
   28,174 and 28,210 each 25 detour searches with about 41,000 cells each (a group that replans every 30 ticks around stationary units and
   whose detour arrives and is used). In total 76.6 of 76.8 million cells fall to searches that arrive (33,787 without and 19,102
   with blockers). Each is a different question (different start cell or different blockers), every answer is used, and the returned path
   depends on the order in which A* expands equivalent cells: a method that expands fewer cells (better heuristic,
   hierarchical search, a backward search from the common goal for the whole group) yields different paths for paths of equal length. Exact
   remains only making every cell cheaper; that has been done (factor 2.5), the rest is branch prediction and heap.
2. **Searches whose goal lies in a large, separate area.** The proof from §4.7 gives up after 4,096 cells, that from §4.5 floods the
   goal area at most as far as the search itself has come. If the goal lies in a large area that is separated from the start (other
   side of a river without a crossing), the search floods on up to the budget. In the 8-player run that no longer occurred (0 searches at the budget);
   the test `PathSearchAtTheBudgetMatchesTheOriginalSearch` contains the case on purpose.
3. **The scans are linear in the number of mobile objects** (in the skirmish about 200, 1.4 to 2.2 µs per scan loaded). A
   spatial index would make them independent of the object count and would be result-neutral, but needs a hook at every place that
   changes a position (`Entity.X`/`Y` are written in dozens of places; `Entity.cs` belongs to a different
   cost centre in this phase). The scan over all objects remains in `DeliverCollisions` for moving objects with collision modules of their own.
4. **`StationaryBlockersAhead` runs for every moving unit in every tick**, not every 30 (the counter `_blockerReplanTick` is only
   set if blockers were found). Changing that would be a change of behaviour.
5. **Supply vehicles without a destination**: `ChinookAIUpdate` (482,176 calls, 13.8 µs loaded) and `SupplyTruckAIUpdate` (8.7 µs) are now,
   after `AIUpdateInterface` and `HordeUpdate`, the most expensive modules of the skirmish. A vehicle that finds no depot oscillates between
   "searching" and "regrouping" and in doing so scans all objects up to four times every two ticks (`SupplyNetwork.FindBestWarehouse`/`…Center`
   with a module search per object, `SupplyMachine.Closest` with `IsKindOf` as a string search). That could be sped up result-neutrally
   with a list of the docks and precomputed `KindOf` bits per template; not done.
6. **Outside this cost centre**, but now visible in the update phase: `HordeUpdate` (118,420 calls, 65 µs per call loaded,
   8.5 % of the module time), `PointDefenseLaserUpdate`, and the work of the scheduler including measurement (30 % of the phase does not lie in modules).

## 7. What a behaviour-changing step would need and would bring

The maximum of 8 ms is not reachable with exact means as long as a group of 70 units plans 70 paths in the same tick. Three
steps would reach it; each changes hashes and needs, according to `docs/handoffs/HANDOFF-PHASE19.md` item 5, a commit of its own with a newly
written manifest and a comparison of results:

1. **One path per group.** Units that get the same command to the same goal in the same tick share one search (from the group's centroid
   or from the first member) and walk their own stretch up to this path. Effect in the profile: tick 25,198 from 69 searches to a few; the
   sum of the cells would drop by an estimated half. Changes the paths of all group members except one.
2. **Cell budget per tick, order by id.** The world releases a fixed number of cells per tick (about 100,000, at an estimated 50 ns quiet
   about 5 ms); searches that no longer fit wait for the next tick, the unit stands still that long or follows its old
   path. Deterministic, because by counted quantities. Changes when units set off; needs state (waiting requests) in the snapshot.
3. **A different search** (goal-directed heuristic from a precomputed zone map as in the original, or Jump Point Search): expands
   a tenth of the cells for the same path, but yields different paths on ties. The biggest lever for the mean of the path search.

In addition, likewise behaviour-changing: `StationaryBlockersAhead` only every few ticks (today every tick), and the replan of all moving
ground units on every footprint change anywhere on the map (`_pathObstacles != navigation.ObstacleSum`) only for paths that touch the changed
cells.

## 8. Checks

All on the final state `309dc7d` (simulation sources), unless said otherwise:

| Check | Result |
|---|---|
| `SimTool reference-manifest check … --set quick` after every commit that changes the simulation | 9 runs identical each time (ten times) |
| `SimTool reference-manifest check … --set full` | **50 runs identical**, in three parts with `--only` (23 skirmishes, sweep of all maps, reference match and stress test; 14 missions; 13 missions), about 21 minutes each with three workers; one run in one piece would have exceeded the time limit under the load |
| `SimTool input-manifest check … baselines/input-manifest.json` | identical (not on `e0a93fa`: see §4.2, fixed with `309dc7d`) |
| `tests/CnCNew.Simulation.Tests` (Debug) | passed, among them `SourceRules` and the four new tests |
| `tests/CnCNew.Simulation.Tests -c Release -- --long --content-dir …` | passed |
| `dotnet build CnCNew.slnx` after deleting `bin`/`obj` of the Stride client | 0 warnings, 0 errors |
| Hashes of the profile runs with observer and probe | 8 players, 54,000 ticks: `0A69FC72FF6975CD` / `78D32DCBCE8A99EB`; 18,000 ticks: `72CF0F846E0D9AA6` / `5E446FE63C5BEF46`; `MD_GLA05`, 3,000 ticks: `826CCD82C8536DC2` / `10330D7D179054FD` – the same in every state |

The manifest files under `baselines/` are unchanged. Not checked: a quiet time measurement (§5.3), the other test programs of the
solution (none of them touches the changed sources other than via the simulation), the client.

New tests (`tests/CnCNew.Simulation.Tests/Tests.PerfMovement.cs`), each against the earlier implementation kept in the test, inputs
from a fixed seed:

| Test | compares |
|---|---|
| `FixedFastPathsMatchWideArithmetic` | product, quotient, root with the 128-bit formulas |
| `MovementScansMatchTheFullScan` | `AvoidLocalBlockers`, `StationaryBlockersAhead` and the stored bounding circle with the full scan over all objects |
| `PathSearchMatchesTheOriginalSearch` | 24,000 paths (with repetitions, footprint changes, blockers, `FindPathToGoal`) with the original search |
| `PathSearchAtTheBudgetMatchesTheOriginalSearch` | searches at the budget, long detours, goals where nobody can stand, on a map with 520 × 440 cells; checks via the probe that the early ends occur |
