# Performance: vision and vision hash (Phase 19, cost centre `Vision` / `VisionHash`)

As of 6 October 2026. The subject is the two tick phases `TickPhase.Vision` (`SimulationWorld.RevealVision`) and
`TickPhase.VisionHash` (hash over the vision cells), files `src/CnCNew.Simulation/Terrain/VisionMap.cs` and
`src/CnCNew.Simulation/World/SimulationWorld.Vision.cs`. **No result is changed:** the cell bytes after every refresh, the
value of the vision hash, the state hash, the snapshots and all queries are the same as before; the unchanged reference manifest
passes (§5). The hash construction itself is **not** touched; why it is the remaining main load and what a prototype of a
different construction yields is in §7 as a proposal for a decision.

**All times in this document are not measured on a quiet machine.** During the whole work the machine was occupied by several parallel
briefs (tick times two to four times as high as in the quiet profile from `docs/PERFORMANCE.md` §6). The comparison is therefore made
(a) through **deterministic counters** (cells, bytes – independent of the load) and (b) through tool builds **running at the same time**
(old and new share the disturbance). The quiet re-measurement with `scripts/perf-profile.ps1` is outstanding.

## 1. Starting point

Measured on a quiet machine at the end of Phase 18 (`docs/PERFORMANCE.md` §6.2, mean per tick in µs; vision runs only every third tick, so one
refresh costs three times as much):

| Run | Cells × slots | `Vision` | `VisionHash` |
|---|---|---:|---:|
| Twilight Flame, 8 players | 486×469 × 9 = 2,051,406 bytes | 716 | 379 |
| Twilight Flame, 4 / 2 players | × 5 / × 3 | 407 / 216 | 212 / 129 |
| `MD_USA05` | 527×521 × 11 = 3,020,237 | 921 | 562 |
| `MD_GLA05` | 589×589 × 13 = 4,509,973 | 1,532–1,670 | 822–841 |

Both phases grew with map area × number of players, not with what changes: `BeginRefresh` went over every cell byte of every
slot, `Reveal` checked, per looking object and viewer, every cell of the enclosing square against the radius, `Hash` mixed
every cell byte. Yet per refresh only **266 cell bytes** change on average (Twilight Flame, 8 players, 18,000 ticks) or
**1,071** (`MD_GLA05`, 3,000 ticks) – measured with `--vision-probe` (§8).

## 2. How vision and vision hash are defined (unchanged)

- **State:** per slot (system player = slot 0, then the players in list order) one byte per path cell: bit 0 visible, bit 1
  explored, bit 2 permanently revealed (always together with the other two). Valid values are 0, 2, 3 and 7; `Load` rejects everything
  else.
- **Refresh** (every `VisionIntervalTicks` = 3rd tick, after the spawns): "visible" is forgotten (permanent cells stay 7),
  then every living, not contained object with a positive range reveals a circle (cell centre within the radius) – for
  the owner and its allies, for `REVEAL_TO_ALL` for everyone, additionally for spies (`VisionSpiedBy`). Range:
  `ShroudClearingRange` or the perimeter of a construction site, module override, `ShroudScalar`, vision bonus of the battle plan.
- **Script actions** between two refreshes write cells directly (reveal permanently and take back, explore, shroud;
  whole map or circle).
- **Vision hash:** `VisionMap.Hash()` – **one** chain of `StateHash.Mix` (one SplitMix64 step per word) over all cell bytes of all
  slots in slot and cell order, eight bytes per word (first byte least significant), closed per slot by a word from the
  remaining bytes (zero if none are left). The value is in the global description as `visionHash` and thus in every
  state hash.

`VisionMap.Hash()`, `BeginRefreshReference()` and `RevealReference()` are the old code, preserved literally. They define the
result and are the oracle of the tests; `VisionMap.ReferenceMode` (tool: `--vision-reference`) lets a world compute entirely
with them.

## 3. What was changed and why it is result-neutral

Everything new is bookkeeping beside the cells: **no state**, never hashed, never saved, restorable from the cells (after
`Load` and after a reference pass the affected slot is treated completely once).

### 3.1 The vision hash continues the chain at the first changed byte (`cd99d8a`)

Every chain step depends on the previous one. A changed byte therefore forces **every later word of every later slot** to be
mixed anew; only the part of the chain **before** the first change can be reused exactly. `VisionMap.CurrentHash()`:

1. keeps the value, a copy of the cells in the state last hashed and the chain value before every 64th word (512 cell bytes);
2. compares the rows written since the last call per slot with the copy (`CommonPrefixLength`, stops at the first
   difference); without a difference the kept value applies;
3. otherwise continues at the chain value before the first difference and mixes from there to the end.

The words are read as a whole instead of being assembled byte by byte (on little endian the same word; on big endian they are
swapped – the value stays machine-independent). That alone makes the pass a good twice as fast; the chain itself remains the
limit (a step waits for the previous one: about 13 cycles per eight bytes, not parallelisable).

*Result-neutral because:* by construction the value is that of `Hash()` over the same cells (the chain value before word *k* depends only on
the bytes before it; according to the comparison those are unchanged). Checked after every refresh of whole runs (`--vision-probe`: "always
equal") and in the tests. Side effect: script actions and `Load` no longer compute the hash themselves; it is computed when the
state hash reads it (several script actions of one tick cost one pass instead of one each).

### 3.2 The refresh clears and reveals row by row in runs (`1de652f`)

- **Forgetting:** the pass over every cell changes a cell only if it is visible and not permanent. Such cells
  arise only through a circle of the refresh or through `Load`; per slot and row the span into which circles
  have written is therefore remembered (after `Load`: everything), and only there is cleared – eight cells per step, with the cell rule of the old code for **every**
  byte value (not only the valid ones).
- **Revealing:** within a row the squared distance is convex in the column; the cells in the circle are therefore **one** run around the
  column that lies nearest to the centre. A row nearer to the centre contains the run of the previous one, a farther one lies within
  it – the two ends move only outwards or only inwards (two radius checks per row plus the steps). The run is written in
  one piece. The check per cell is the same integer computation as before.

*Result-neutral because:* the same set of cells per circle, the same cell rule when forgetting; the order of the circles does not matter
for the cells (only bits are set).

### 3.3 Viewer tables without allocation (`b3ef269`)

Per refresh `RevealVision` created a list and an array per player, an array of all players and a LINQ closure and
asked every looking object for `REVEAL_TO_ALL` by name comparison. The tables are now kept and refilled (alliances
are still read in every refresh). *Result-neutral:* the same circles for the same viewers. Not measurable in time
(within the scatter); the allocations of the phase disappear.

The commit had in addition precomputed the answer to `REVEAL_TO_ALL` as a public property of the template
(`ObjectTemplate.RevealsToAll`). **That was a mistake:** the input manifest describes every public value of the template catalog,
the two catalog hashes (`templates`, `whole`) changed, `input-manifest check` reported two deviations from `b3ef269` to `dfa9745`.
The reference manifest was not affected. `6f15118` takes the property back (`ObjectTemplate.cs` is again byte for
byte the file of `74ccdde`); the refresh again asks `IsKindOf("REVEAL_TO_ALL")` per looking object. Lesson for the remaining
cost centres: **a new public property on a catalog type is a manifest change**, even if it only caches an existing
value.

### 3.4 A slot with unchanged circles stays untouched (`c4253ae`)

The circles of a refresh are collected (`Reveal` between `BeginCollectedRefresh()` and `EndCollectedRefresh()`, that is what the
two calls are named since 3.6) and applied only at the end. If the circles of a slot are
the same as in the refresh before **and** nothing else has written its cells since (no script action, no `Load`,
no reference pass), the slot is not touched. *Result-neutral because:* "forgetting what is visible and drawing the same circles
again" ends with the bytes it began with. In missions that holds for about three quarters of all slot refreshes (sides
whose objects stand still).

### 3.5 A slot with few changes is redrawn only around them (`425522f`)

The circles are sorted (does not matter for the cells) and matched against those of the last refresh: *gone*, *stayed*,
*come*. Then per slot one of two ways:

- **Patching:** forget only the cells of the circles that are gone; redraw every circle that stayed within the cell rectangle of every circle
  that is gone; draw the circles that have come.
- **Redrawing** (like 3.2): forget everything visible of the slot, draw all circles.

The way with less work is chosen, counted in cells of the rectangles it would go over (integer, deterministic); if something
else has written the cells, the slot is always redrawn. *Result-neutral because:* both ways end with "visible exactly where a
circle of this refresh or a permanent mark lies" – outside the circles that are gone nothing was forgotten, there the
circles that stayed still show; inside, everything that reaches into it is redrawn. The choice of the way can therefore not
influence the result. A circle entirely beside the map reveals no cell and does not count.

### 3.6 The plain form is preserved (`dfa9745`)

`c4253ae` had switched `BeginRefresh()`/`Reveal()` themselves to "collecting". With that, a `Reveal` outside the world no longer wrote its cells
immediately – two tests of the presentation (`tests/CnCNew.Presentation.Tests/VisionTests.cs`), which fill a `VisionMap` by
hand, failed at the states `c4253ae` and `425522f`. Product code was not affected (only the world calls `Reveal`, and it
completed the refresh); the error was noticed only in the final run of the presentation tests. Since the fix the old
contract applies again: `BeginRefresh()` forgets immediately, `Reveal()` writes immediately; the collected form has names of its own
(`BeginCollectedRefresh()`/`EndCollectedRefresh()`), and only the world uses it. Whoever uses the plain form forces a complete redraw
for the affected slots in the next collected pass. Wrong nesting throws
`InvalidOperationException`. The way of the world is the same code as in 3.5.

## 4. Tests

`tests/CnCNew.Simulation.Tests/Tests.VisionEquivalence.cs` (commit `62787e0`, extended with `425522f` and the fix 3.6), the oracle is in each case the old
computation over every cell:

- **Random sequences on six map shapes** (whole hash words with a checkpoint exactly at the end; remainder of seven bytes; smaller than two
  words; one column; …), fixed seed, 700 steps each: refreshes with lookers that stay, move, appear and
  disappear; circles on, across and beside the map edge; radius zero, negative, huge, smaller than a cell; every script operation;
  `Load` into fresh and into running maps; a refresh in reference mode in between; several changes before a hash call.
  In addition the plain form (writing immediately) between collected refreshes and single circles outside any refresh.
  Compared are cell bytes, full hash and kept hash. The test requires that every shape has often taken all three ways of a refresh
  (untouched, patched, redrawn).
- **Kept hash:** mixes nothing without a change, continues behind the unchanged beginning (byte counts checked); a slot with
  the same circles stays untouched, after a script action it is redrawn.
- **Two worlds with the same commands over 1,500 ticks**, one in reference mode: units that drive, die and appear,
  construction sites, a `REVEAL_TO_ALL` object, changing alliances between three players, every reveal and shroud action (also for the
  system player), three snapshot restores (also one tick after a refresh). The same in every tick: state hash,
  cell bytes, vision hash, `EntitiesVisibleTo` of all players; the snapshots of both worlds are byte-identical.

Counter-check: two deliberately built-in errors (written rows not reported; overlap check off by one row) make
the tests fail.

## 5. Checks against the manifest

`SimTool reference-manifest check … --set quick` after each of the seven simulation commits (3.1 to 3.6 and `6f15118`): each time
"9 runs identical". The manifest files are unchanged.

Completion (run in five parts with `--only` because of the machine load, together all 50 runs):

| Check | State `dfa9745` | Final state `6f15118` |
|---|---|---|
| `reference-manifest check --set full` (9 + 12 + 4 + 18 + 7 runs) | 50 runs identical | 50 runs identical |
| `input-manifest check` | **2 deviations** (catalog hashes, see 3.3) | identical |
| `reference-manifest check --only mission_MD_USA05 --verify-incremental` | 3,000 ticks checked, 0 deviations | 3,000 ticks checked, 0 deviations |
| `tests/CnCNew.Simulation.Tests` (Debug) | 353 passed | 353 passed |
| `tests/CnCNew.Simulation.Tests -c Release -- --long --content-dir …` | 367 passed | 367 passed |
| `tests/CnCNew.Presentation.Tests` | passed (two failures at `c4253ae` and `425522f`, see 3.6) | passed |
| `tests/CnCNew.Game.Tests -c Release -- --only LiveMatchTests,HudTests` | passed | passed |
| clean `dotnet build CnCNew.slnx` (`bin`/`obj` of the client deleted) | 0 warnings, 0 errors | 0 warnings, 0 errors |

Between `dfa9745` and `6f15118` lies a single change of an expression (precomputed truth value against the call that
computes it). Final hash and chain of the three measurement runs are in every state the same as at the starting state
`74ccdde`: Twilight Flame 8 players 18,000 ticks `72CF0F846E0D9AA6` / `5E446FE63C5BEF46`, `MD_GLA05` 3,000 ticks `826CCD82C8536DC2` /
`10330D7D179054FD`, `MD_USA05` 3,000 ticks `1CB28E6AA923BDEF` / `0816C3438CB7F689`.

## 6. Before / after

### 6.1 Counters per refresh (deterministic)

| Quantity | Twilight Flame, 8 players, 18,000 ticks | `MD_GLA05`, 3,000 ticks | `MD_USA05`, 3,000 ticks |
|---|---:|---:|---:|
| Cell bytes of the table | 2,051,406 | 4,509,973 | 3,020,237 |
| Circles | 167.8 | 423.8 | 330.3 |
| changed cell bytes (mean) | 266.0 | 1,070.6 | not collected |
| **cleared cell bytes** before → after | 2,051,406 → 105,837 (5.2 %) | 4,509,973 → 50,961 (1.1 %) | 3,020,237 → 25,613 (0.8 %) |
| **radius checks** before → after | 758,108 → 39,404 | 1,780,134 → 33,653 | not collected → 13,676 |
| **written cell bytes** before → after | 593,692 → 580,715 | 1,380,146 → 329,382 | 681,611 → 131,678 |
| Circles not drawn fully | 8.0 | 384.5 | 298.1 |
| Slot refreshes untouched / patched / new | 20.4 / 7.7 / 71.9 % | 76.2 / 15.7 / 8.1 % | 77.8 / 18.4 / 3.7 % |
| **mixed hash bytes** before → after | 2,051,406 → 1,641,700 (80.0 %) | 4,509,973 → 3,514,582 (77.9 %) | 3,020,237 → 1,683,018 (55.7 %) |
| compared bytes (new) | 7,279 | 19,853 | 40,610 |

"Written cell bytes before" for `MD_USA05` is the state after 3.2 (same set of cells as before). Refreshes entirely without
a changed byte: 114 of 6,000 (Twilight Flame), 0 of 1,000 (`MD_GLA05`).

### 6.2 Times (**not measured on a quiet machine**; mean per tick in µs; per row the named states ran at the same time)

| Run | States (at the same time) | `Vision` | `VisionHash` |
|---|---|---:|---:|
| Twilight Flame, 8 players | **Start `74ccdde` → final state `dfa9745`** | **3,028.2 → 330.4** | **1,500.4 → 558.0** |
| | Start → 3.1 → 3.2 → 3.3 | 2,287.6 → 2,361.7 → 426.4 → 449.9 | 1,699.8 → 630.8 → 540.7 → 588.0 |
| | Start → 3.3 → 3.4 | 2,528.7 → 440.6 → 379.7 | 1,945.2 → 618.4 → 592.3 |
| | 3.4 → 3.5 | 224.1 → 214.9 | 387.6 → 383.4 |
| `MD_GLA05` | **Start → final state** | **7,984.3 → 498.6** | **4,024.9 → 1,221.4** |
| | Start → 3.1 → 3.2 → 3.3 | 5,425.4 → 5,508.7 → 942.1 → 887.1 | 2,925.7 → 1,252.8 → 1,137.9 → 1,164.2 |
| | 3.3 → 3.4 | 1,020.0 → 639.6 | 1,180.7 → 1,150.3 |
| | 3.4 → 3.5 | 604.1 → 373.9 | 1,075.2 → 1,116.6 |
| `MD_USA05` | **Start → final state** | **3,136.8 → 234.3** | **1,668.2 → 438.6** |
| | Start → 3.3 | 5,092.9 → 1,004.5 | 3,465.5 → 725.1 |
| | 3.3 → 3.4 | 531.5 → 271.4 | 444.8 → 421.9 |
| | 3.4 → 3.5 | 465.3 → 301.6 | 605.1 → 541.8 |

The load varied strongly between the runs (mean tick time of the starting state on Twilight Flame between 16.4 and 18.6 ms, on a quiet machine
it would be about 5); only values of the same row are comparable. Ratio after/before – direct (bold rows) and chained through the
individual steps:

| Run | `Vision` direct / chained | `VisionHash` direct / chained |
|---|---:|---:|
| Twilight Flame, 8 players | 0.11 / 0.16 (6 to 9 times less) | 0.37 / 0.30–0.35 (about 3 times less) |
| `MD_GLA05` | 0.062 / 0.064 (about 16 times less) | 0.30 / 0.40 (2.5 to 3.3 times less) |
| `MD_USA05` | 0.075 / 0.065 (13 to 15 times less) | 0.26 / 0.21 (4 to 5 times less) |

The whole mean tick time in the direct pair: Twilight Flame 18,442 → 15,237 µs, `MD_GLA05` 47,424 → 31,700 µs, `MD_USA05`
16,461 → 12,854 µs (at this load to be read only as a direction). Allocation per tick in the Twilight Flame run: 81,950 → 80,168 bytes (the
tables from 3.3).

### 6.3 What remains (µs per refresh)

Measured, **not on a quiet machine** (direct pair, final state `dfa9745`; mean per tick × 3):

| Run | Revealing | Hash | mean tick time of the same run |
|---|---:|---:|---:|
| Twilight Flame, 8 players | 991 | 1,674 | 15,237 |
| `MD_GLA05` | 1,496 | 3,664 | 31,700 |
| `MD_USA05` | 703 | 1,316 | 12,854 |

**Extrapolation** to the quiet starting values from §1 with the ratios from 6.2 (a calculation, not a measurement; the quiet
Twilight Flame run moreover had 54,000 instead of 18,000 ticks):

| Run | Revealing per refresh | Hash per refresh | both per tick |
|---|---:|---:|---:|
| Twilight Flame, 8 players | ≈ 235–350 | ≈ 340–425 | ≈ 190–260 (before 1,095) |
| `MD_GLA05` | ≈ 285–320 | ≈ 740–1,010 | ≈ 340–445 (before 2,373–2,492) |
| `MD_USA05` | ≈ 180–210 | ≈ 350–445 | ≈ 180–220 (before 1,483) |

Against the goal of the phase (tick mean ≤ 1 ms with 8 players) vision has thus fallen from about 1.1 ms to about a fifth to a quarter
of a millisecond per tick; more than half of that is the hash.

## 7. What does not fall further, and why

### 7.1 The hash: the chain allows no more (proposal for a decision)

After 3.1, 56–80 % of all cell bytes are still mixed per refresh, although on average 266 or 1,071 bytes change. That
is not due to the implementation but to the construction: the first change almost always lies in the first player slot (with eight
players after a fifth of the chain), and everything behind it depends on it. There is no exact incremental form of a
SplitMix chain over all bytes: the step `h ← F(h + w + c)` is not linear, two chain pieces cannot be joined
without recomputing the second. The full pass itself is limited by the dependency of the steps and exhausted with the word-wise
reading (no intrinsics, no `unsafe`).

**Proposal (changes the value of the vision hash and thus every state hash – not part of this work):** a two-level
construction.

- row value `R(slot, row)` = chain over the words of the row, started with `Mix(Seed, slot << 32 | row)`;
- vision hash = chain over all row values in slot and row order.

Order-sensitive as before, the same mixer, the same quality class (detects deviation, withstands no attack). Only the rows that
have really changed need recomputing (the bookkeeping from 3.1 already knows them), plus the chain over the
row values (4,221 steps on Twilight Flame with 8 players, 7,657 on `MD_GLA05`).

Prototype in the measurement tool (`--vision-probe`, not in the product; measured in the same, **not quiet** run as the kept hash of
state 3.1):

| Run | changed rows per refresh | prototype per refresh | kept hash (3.1) in the same run | ratio |
|---|---:|---:|---:|---:|
| Twilight Flame, 8 players, 18,000 ticks | 183.4 of 4,221 | 127.4 µs | 1,423.1 µs | 11 times less |
| `MD_GLA05`, 3,000 ticks | 100.5 of 7,657 | 167.9 µs | 3,462.9 µs | 21 times less |

The prototype measures the re-hashing of the changed rows and the chain over all row values, not the finding of the rows (the comparison
with the copy costs 7,000–40,000 bytes per refresh today, i.e. a few µs). A third level (slot value over the row values of the
slot) would also limit the upper chain to the changed slots. Extrapolated to quiet values, about a tenth
to a twentieth of the hash would remain: about 30–40 µs per refresh on Twilight Flame, about 35–50 µs on `MD_GLA05`.

Price: the value changes, so **every** hash of the reference manifest (all 50 runs) – according to `docs/handoffs/HANDOFF-PHASE19.md` item 4 a
commit of its own with one cause, a rewritten manifest and the proof that the *results* (winner, final tick, object lists)
of all runs stay the same. The cell bytes and the snapshot content do not change; a snapshot version does not have to rise as long as
the vision hash is not stored there (it is recomputed on loading). Recommendation: decide together with the other value-changing
hash decisions of the phase in one go, not individually.

### 7.2 Revealing in the skirmish: units drive through their own base

In missions, revealing after 3.4/3.5 is close to "only what changes" (0.8–1.1 % of the table cleared, 19–24 % of the former
write work). In the skirmish with eight AI players it is not: 72 % of the slot refreshes redraw the whole slot, because the driving
units (collectors, army) cut the large, overlapping circles of their own buildings and patching would then touch more cells
than redrawing. The write work (581,000 cell bytes per refresh for 266 changed ones) is memory-bound.

What would fix that exactly is the method of the original: **a counter per cell and slot** ("how many circles cover this cell"),
which a moving circle changes only in the crescent between old and new position; visible is whatever has a counter > 0 or is permanent.
That is feasible with identical results (script actions and `Load` force, as today, a full reconciliation of the slot), but costs two bytes
per cell and slot (4 MB on Twilight Flame with 8 players, 9 MB on `MD_GLA05`), a mapping of old to new circles per object and
a test series of its own. Not implemented: the yield would be an estimated further third to half of the remaining reveal time in the
skirmish (≈ 100–200 µs per refresh on a quiet machine), and the hash (7.1) is the larger remaining item.

### 7.3 Not part of this cost centre

- `FogOfWarGrid.Update` (game layer, `LiveGame.SyncAfterTick`) reads the vision of the human per refresh cell by cell through
  `IsVisible`/`IsExplored` (two calls per cell). Its inputs are unchanged. `VisionMap.CellBytes(slot)` now hands out the raw
  bytes; with that the fog could be taken over row by row and only for written rows (item 7 of the handoff, a different
  brief).
- `Save`/`Load` of the vision (Base64 per slot) are not in the tick.
- Memory: the bookkeeping costs one further byte per cell and slot (copy of the hashed state) plus 1/64 of that for the
  chain values and eight bytes per row and slot for the row spans – on Twilight Flame with 8 players about 2.1 MB, on `MD_GLA05`
  about 4.7 MB.

## 8. Tool

`SimTool perf` and `perf-mission` (see `docs/PERFORMANCE.md` §3) now additionally print under probe (a):

- the comparison of the kept hash with the full pass on the final state;
- the **counters** of the vision work (`VisionMap.Counters`: refreshes, cleared and written cell bytes, circles,
  radius checks, share of untouched / patched / redrawn slots, hash calls, mixed and compared bytes). They are
  deterministic and thus comparable on a busy machine as well.

New options:

- `--vision-probe`: after every refresh the number of changed cell bytes and rows, the comparison of kept hash against full
  pass, and the time of the prototype from 7.1. The probe costs time; it is in "unattributed", not in the phases.
- `--vision-reference`: the world computes vision and hash with the old code over every cell (for A/B runs with the same tool).
  Final hash and chain are the same.
