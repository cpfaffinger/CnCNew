# Performance: the state hash (`TickPhase.Hash`)

As of: Phase 19, cost centre "state hash" (`docs/handoffs/HANDOFF-PHASE19.md` item 4; starting point `docs/PERFORMANCE.md` §6/§7).
Starting commit `74ccdde`, branch `worktree-agent-ac725b90d346064f7`, 6 October 2026.

**The iron rule of this work:** no hash value changes. Every tick hash, final hash and chain hash of every run stays
bit-identical, and the incremental hash is equal to the full recomputation in every tick. Everything that remains here is a
different *computation* of the same values. A different hash *construction* is proposed in §7, not implemented.

**All times in this document are not measured quietly.** The machine (AMD Ryzen 7 PRO 6850U, 16 logical processors)
was occupied by four further agents during the whole work; the tick times were at times two to five times
above the quiet values from `docs/PERFORMANCE.md` §6. Comparisons therefore come from processes **running at the same time**
(old and new state side by side, same load) and from **counters** that the tool keeps deterministically (objects,
words, bytes). The counters are exact, the times are ratios under load. The quiet measurement is outstanding.

**Summary.** What has remained is three value-neutral changes (§4.1–4.3): under the same load the hash phase still costs
roughly two thirds (skirmish with 8 players: mean 598 → 426 µs; `MD_USA05` 1,880 → 1,263; `MD_GLA05` 2,520 → 1,679), the
allocation in the phase is gone. Two more far-reaching attempts were measured and **not** adopted (§4.4, §4.5): one
halves the hash phase of the missions once more but takes away from the hash a protection on which places without
`MarkDirty` rely today (reverted, described as a named decision for the coordinator); the other was safe
but yielded nothing. Found along the way: **an existing D8 error in the starting state** that affects one value of the
reference manifest (§4.6, not fixed because the correction changes this value), and a concealed one that is
fixed. What remains is structure: §6 and the proposal in §7.

## 1. How the hash is built today

- `StateHash.Mix(hash, word)` is a SplitMix64 step: `z = hash + word + constant`, two xorshift multiplications,
  one xorshift. `HashStateWriter` folds with it **sequentially**: every call of `IStateWriter` is one word (a text:
  its length, then **one word per character**). Field names do not go into the hash, only values and structure
  (`BeginObject` = `0xB1`, `EndObject` = `0xE1`, `BeginArray` = `0xB2`, `EndArray` = `0xE2`).
- Per object: `HashEntity` = folding of `Entity.Describe` from `StateHash.Seed`. The description is: the object's own
  fields (ID, template index, **template name as text**, owner, position, status, …, turrets, firing stages, acquisition),
  then for every module a frame (`BeginObject`, index, **module name as text**, **module tag as text**, `BeginObject`),
  the module's own state (`SimModule.Describe`) and two `EndObject`.
- World: `_entitySum` is the **sum** (mod 2⁶⁴) of the object hashes; in addition sums of the timers and of the pending commands.
  `FinishHash` folds in every tick the global part (tick, ID counter, random streams, players with money/upgrades/…,
  deferred damage, rejections, spawn queue, vision hash) and the three sums.
- Incremental: `MarkDirty(entity)` enters an object into the dirty list; `ComputeIncrementalHash` re-describes exactly
  these objects and corrects the sum by `new − old`. `ComputeFullHash` describes everything (constructor, snapshot,
  check); `VerifyIncrementalHash` compares the two.

Three properties of this construction determine everything that follows:

1. **The folding is a dependency chain.** Every `Mix` needs the result of the previous one. The state after a
   piece of text depends on every word before it (position, status, modules before it). A precomputed "state after the
   module name" is therefore valid only at the same place of the same stream with the same lead-in – that is, practically never.
   What can be precomputed is the **words**, not the hash state (§4.2).
2. **The wake tick of every update module is hashed** (`nextWake`, mandatory according to `SourceRules`). A module that runs
   every tick thereby changes the hash of its object in every tick – even if it does nothing else. "Marked as changed
   although nothing has changed" does not exist in this construction: the test `UpdateChangeWatch` checks over
   149,155 real module updates of all 183 module types that **every** update changes the description of its object,
   and the counter `HashWork.Unchanged` shows 0.00–0.53 % re-described objects with an unchanged hash.
3. **The scheduler marks every object whose module has run as a whole.** Almost every object carries a module that
   runs every tick (`StealthUpdate`). This marking therefore incidentally covers up every place that changes such an object from
   outside and forgets `MarkDirty` (§4.4). That is not a design, but today it is a part of the safety.

## 2. Measuring tool

`SimTool perf` / `perf-mission` (commands in `docs/PERFORMANCE.md` §3) additionally print in probe (b):

- **Counters of the incremental hash** (`SimulationWorld.HashWorkDone`, no state): re-described objects, folded
  words, objects with an unchanged hash; allocation within the hash phase.
- **The two halves of the phase** on the final state, in a warm loop: writing down words; folding in one chain; folding
  side by side (`StateHash.FoldStreams`).
- **Per module type**, what a `Describe` writes and allocates (counted, not timed).
- With `--prefix-probe` (costs tick time, not for time measurements): every 250 ticks all objects are written down in two
  consecutive ticks and compared: share of the words before the first change; the changed
  objects broken down into own fields / module states / frames; objects on which only wake ticks have moved; which
  module types are behind "exactly one module changed".
- A **prototype** for §7 (a different hash, is only measured): module name and tag as one word each.

## 3. Measured shares (starting state)

Counters over the whole run, mean per tick:

| Run | Objects re-described per tick | Words per tick | Words per object | of which own fields | of which module state | of which module frame (index, name, tag) | Hash unchanged |
|---|---:|---:|---:|---:|---:|---:|---:|
| Twilight Flame, 8 players, 18,000 ticks | 173.3 (89 % of the objects) | 134,637 | 777 | 72 | 60 | 645 (83 %) | 0.17 % |
| `MD_USA05`, 3,000 ticks | 1,311.8 (70 %) | 426,247 | 325 | 55 | 15 | 255 (78 %) | 0.00 % |
| `MD_GLA05`, 3,000 ticks | 1,738.8 (77 %) | 553,584 | 318 | 53 | 13 | 252 (79 %) | 0.53 % |

Roughly 75 % of all words are text characters (final state Twilight Flame: 138,755 of 184,273), almost all from module name and
tag; the own state of all modules together is 5–8 % of the words.

Where the time comes from:

| Share | Finding |
|---|---|
| **Folding** | The phase is bound to the latency of the one chain: 3.4–4.0 ns per word in one chain (isolated microbenchmark; under load 4–10 ns). With 777 words that is roughly 3 µs per object – in the starting state three quarters of the phase in the warm state. |
| **Describing** | 0.8–1.2 µs per object warm (Twilight Flame), of which the largest part is the two texts per module through the writer interface. In the run there is the addition that every re-described object including all modules (also the sleeping ones) is read from cold memory. After §4.1/§4.2 this is the larger part of the phase (§4.5). |
| **Too many objects "changed"** | In the missions, **only wake ticks** have moved on **95.5 % (`MD_USA05`) and 92.7 % (`MD_GLA05`) respectively** of the re-described objects (Twilight Flame 27.7 %). Behind this is almost solely `StealthUpdate` (92–94 % of the cases "exactly one module changed" in the missions): almost every object carries the module, it runs every tick, and almost no object can cloak. In Twilight Flame `StealthUpdate` and `GenerateMinefieldBehavior` (waits every tick for its upgrade) share roughly 8 % each, then `SupplyWarehouseDockUpdate` 4.5 %, `ChinookAIUpdate` 3 %. |
| **Global part** | 7–10 µs per tick in the missions (10–12 sides), 0.3–0.5 % of the phase. Not touched. |
| **Allocation in the phase** | 3,042 bytes per tick (Twilight Flame), 3,739 (`MD_USA05`) of 82–218 KB per tick in total: field names composed at runtime (`"start" + "X"`, `"tower" + index`) that the hash writer never looks at. |

The assumption of the handoff "`MarkDirty` for every module update, whether something changes or not" does not hit the core:
something always changes (the wake tick). And the obvious conclusion "then do less for these objects" is exactly
the place at which this work falsified a hash (§4.4).

## 4. The changes

### 4.1 Folding four objects side by side (`369fe0d`) – stays

`ComputeIncrementalHash` lets the changed objects write their words into a reused buffer
(`WordStateWriter`: the same words in the same order that `HashStateWriter` would mix) and folds them with
`StateHash.FoldStreams`: four objects at the same time, each from the start value, each word through the same `Mix`. In one chain a processor
waits most of the time for the multiplication; four chains independent of each other overlap.

**Value-neutral because** every stream gets exactly the words in exactly the order through exactly the function as before;
only the order in which the streams advance *among each other* is different, and they share no state. The
object hashes are added into the sum one after another in list order as before. `ComputeFullHash` and
`VerifyIncrementalHash` continue to use the simple `HashStateWriter`: the full computation remains an independent
judge of the new path. Tests: `FoldStreams` against the sequential hash for 400 random sets of streams (empty streams,
fewer than four); `WordStateWriter` against `HashStateWriter` for every kind of call.

No threads, no vector types, no unsafe code: only four local variables in a loop.

### 4.2 Preparing the words of the module frames (`158aee7`) – stays

Index, name and tag of a module never change after attaching. The world holds their words per template and module slot
(written once by `Entity.DescribeModuleFrame`, the only definition of the frame); the word writer of the
incremental hash copies them instead of sending two strings per module through the interface.

**Why not the hash state:** see §1, property 1 – the state before the frame is a different one for every object and in every
tick. What is prepared is the encoded words; the folding sees the same words in the same order.
**Value-neutral because** the prepared words come from the same routine that every other writer continues to get call
by call (full hash, snapshot, tools). Test: the word description of every template of the synthetic fixture
and of all 2,115 instantiable objects of the real data, living and dying, folds to the hash of the written-out
description.

### 4.3 Not building field names for a writer that discards them (`e4290ca`) – stays

`IStateWriter.KeepsNames` (default `true`; `false` for the two hash writers). Five places that compose a name at
runtime (`WritePoint` of the projectiles and of the aircraft, `FireWeaponWhenDamagedBehavior`, bridge towers,
late rejection reasons) give the hash the bare name and build the real one only for writers that keep names.
**Value-neutral because** names never go into the hash; values and order are untouched. Test: every compared
description additionally goes to a writer that keeps names; it must mix to the same hash and must not see any name twice within
one object (a shortened name that reached it would collide).

### 4.4 Hashing one module alone when its update vouches for it (`cf36a42`) – **reverted** (`791bbf5`)

**The idea.** An update module promises at the end of its own `Update` that nothing has changed outside its own state
(`StealthUpdate`, when the status of the object is the same; `GenerateMinefieldBehavior`, as long as it
waits for its upgrade). The scheduler then marks only the module; the hash holds the words of the object, replaces those
of the one module and continues the folding from a checkpoint before this module. The object is **not** re-described.

**The effect** (three states at the same time, heavy load; hash phase mean / median in µs): `MD_GLA05` 5,237.9 / 3,672.7
(start) → 4,084.8 / 2,854.3 (after §4.3) → 1,860.2 / 1,346.7; `MD_USA05` 4,599.3 / 2,747.3 → 3,056.8 / 1,788.3 →
1,809.8 / 1,085.6; Twilight Flame unchanged (1,235 → 1,199). 90.6–92.4 % of the mission objects went the module path, the
folded words fell from 426,247 to 173,733 and from 553,584 to 177,145 per tick respectively.

**Checked** (this state, every tick against the full recomputation): quick set (9 runs), the five D8 missions,
reference match and 21 further missions – 34 different runs. One deviation, in
`mission_GC_SuperWeaponsGeneral_GLADemolitionGeneral`; it occurs **in the starting state in exactly the same way** and does not belong to
this attempt (§4.6). No error has been proven against the attempt itself in these runs.

**Why reverted nevertheless.** Because of property 3 from §1: until then the whole marking of the scheduler covers up every
place that changes an object with a module running every tick from outside and forgets `MarkDirty` – the object
is re-described every tick anyway. The promise of *one* module cannot vouch for what *other* code writes into
its object. That such places exist is proven: one was found while reading
(`StealthUpdate.MarkDetected`, called from the update of the detector; under the attempt the hash of the detected
object would be stale), a second is the existing error from §4.6 – and that one escaped the Phase 18 check. Further ones
are to be expected on paths that no checked run takes (player commands, late mission phases). The attempt turns
every such concealed place into a wrong hash. By the rule of this work ("when in doubt leave it marked") that is
not an optimisation but a decision about the risk – and that belongs to the coordinator: the commit `cf36a42` is
back with `git revert 791bbf5`.

**Correction to the history:** the message of the revert commit `791bbf5` attributes the deviation in
`mission_GC_SuperWeaponsGeneral_GLADemolitionGeneral` to the attempt. That was the assumption at the time of the revert and
is wrong; the cross-check on the starting state (§4.6) came afterwards. The reason for the reversal is the one named above.

**What remains of it (commit `fix(sim)` after the revert):** `StealthUpdate.MarkDetected` marks the detected object
itself. No hash value changes because of it (the object is marked anyway by its module running every tick). Test
`Determinism D8: detection from another entity's update reaches the tick hash`: the hash is checked immediately after the call,
before a tick can cover it up; without the correction it fails ("changed without MarkDirty: 1 Cloaked").

**What the attempt shows:** the big lever of the missions is not the folding but the **not describing** – and that
can only be had without reservation when the marking holds without the protection of the scheduler (§7 C).

### 4.5 Continuing the folding behind a compared, unchanged beginning – **measured, not adopted**

The safe variant of the same idea, without a promise: every frequently described object continues to be **described completely**
(the scheduler marks as always); its beginning (own fields, states of the front modules, the words in between) is
compared **word by word** with a held copy, and only if it is equal does the folding continue at a checkpoint
behind it. Value-neutral by construction (the folding runs from left to right; an equal beginning has the same
folding state), no new D8 surface. Built, tested (every tick checked), the quick set with `--verify-incremental`
ran without deviation for the first version (60,896 ticks). The source code is not in the branch.

Measured (at the same time as the state after §4.3): the folded words fell in `MD_GLA05` from 553,584 to 158,576–203,703
per tick, in `MD_USA05` from 426,247 to 162,027–215,274 (66.7–98.1 % of the objects continued, 207–249 words of beginning on
average) – **but the time not reliably**: `MD_USA05` 1,783 → 1,288 and 1,498 → 1,458 µs; `MD_GLA05` 1,913 → 1,641,
2,004 → 2,482 and 1,946 → 1,979–2,141 µs in three measurements; Twilight Flame 2–9 % **slower** (524 → 562–571, 585 → 598).
After §4.1 a word still costs roughly 1.1 ns in folding; the comparison of the beginning and the additionally touched
memory lines cost about as much as the saved folding yields. The rest of the phase is the describing, and that
remains. Not adopted: complexity without a proven gain.

### 4.6 Find: an existing D8 error in the starting state (`PauseCountdown`) – **not fixed, reported**

`SpecialPowerModuleBase.PauseCountdown` (called by the script actions `NAMED_STOP_SPECIAL_POWER_COUNTDOWN` /
`NAMED_START_SPECIAL_POWER_COUNTDOWN` and by `UnpauseSpecialPowerUpgrade`) changes `pausedCount`, `pausedOnFrame` and
`availableOnFrame` without marking the object. At the **starting commit `74ccdde`**,
`reference-manifest check --set full --only mission_GC_SuperWeaponsGeneral_GLADemolitionGeneral --explain-incremental` reports:

> INCREMENTAL HASH MISMATCH in 1 of 3000 verified ticks; first: after tick 2100 (690 objects): Incremental state hash
> `15DC6118968B6B1A` differs from the full hash `C9B158ECE41535CA` at tick 2101 (changed without MarkDirty: 659
> `SupW_AmericaParticleCannonUplink`) – `modules.[3 SpecialAbility].state.availableOnFrame`: 7201 → 9301,
> `pausedCount`: 1 → 0.

Consequences, as described in `docs/DETERMINISM-FINDINGS.md` §6: the value of this run in the reference manifest (chain
`CC877D4FC06A1DF9`, final hash `69721DF608A6C803`) is the hash of a stale intermediate state; the run hashes differently
as soon as something computes the full hash (in check mode: chain `E7EAD89208DD9861`). Without check mode the run passes the
manifest – which is why it has not been noticed so far; the Phase 18 check ran 1,500 ticks per map, the error occurs at
tick 2,100.

The correction is one line (`world.MarkDirty(Entity)` at the end of `PauseCountdown`) and **changes exactly this
reference value**: chain `ADCCB4A1B93CBAD7`, final hash `5E436B3BD943DFC2`, snapshot SHA-256 `5C52347C…6426E`; the incremental
hash is then equal to the full one in all 3,000 ticks, and the remaining 49 runs of the manifest stay identical (measured with the
correction: 20 + 21 + 8 runs identical, 718,191 ticks checked without deviation). It is **not in the branch**: this
work was not allowed to change any hash value or the manifest. Recommendation: a commit of the coordinator's own with the newly
written value of this one run, as with `99a02fe`/`df523e4`; a test for it (call of `PauseCountdown`,
`VerifyIncrementalHash` immediately afterwards) fails today and passes with the line.

## 5. Before/after (state of the branch)

**Not measured quietly.** Hash phase, µs per tick, mean / median; both states of a row ran at the same time, otherwise
nothing of this work. This is the measurement with the lowest foreign load of the day (the starting state lay 11–13 %
above its quiet values from `docs/PERFORMANCE.md` §6.2 in the process: 530.8 / 1,681.2 / 2,271.3 µs).

| Run | Start `74ccdde` | State of the branch `2f815d7` | Factor (mean) | Tick without the first 300, mean |
|---|---:|---:|---:|---:|
| Twilight Flame, 8 players, 18,000 ticks | 598.1 / 647.0 | 425.8 / 456.2 | 1.40 | 7,841 → 7,495 |
| `MD_USA05`, 3,000 ticks | 1,880.1 / 1,798.2 | 1,263.4 / 1,215.7 | 1.49 | 8,694 → 8,131 |
| `MD_GLA05`, 3,000 ticks | 2,520.2 / 2,464.6 | 1,679.1 / 1,638.4 | 1.50 | 20,007 → 20,580 |

(The tick times of the last column do not contain the work of the other agents on other cost centres; their scatter
between two simultaneous processes is larger than the gain of the hash phase in `MD_GLA05`.)

The same pairs under heavier load (earlier measurements, each at the same time, "after" = `e4290ca`): Twilight Flame
847.1 / 917.4 → 584.5 / 623.2 and 1,736.1 / 1,233.3 → 1,235.4 / 832.2; `MD_USA05` 2,288.9 / 2,203.9 → 1,497.9 / 1,441.9
and 4,599.3 / 2,747.3 → 3,056.8 / 1,788.3; `MD_GLA05` 3,057.9 / 2,991.4 → 2,003.7 / 1,965.7 and 5,237.9 / 3,672.7 →
4,084.8 / 2,854.3 – factors 1.3 to 1.5. Single steps (at the same time as the predecessor): Twilight Flame 9,000 ticks
1,436.9 → 1,275.2 (§4.1) → 978.2 (§4.2) → 875.0 (§4.3, own run); `MD_USA05` 1,500 ticks 3,176.2 → 2,515.9 → 2,097.6
→ 1,994.0.

Transferred to the quiet starting values that would be roughly 380 / 1,130 / 1,510 µs – **an extrapolation, not a measurement**.

Stress test (`SimTool stress`, 216,000 ticks, "Run 1", µs per tick, alternating start / branch): 97.5 / 85.5 – 112.4 / 50.8
– 62.8 / 54.7. Final hash `56882F763EABEF67`, chain `B8E663493F2C73D5` in all six runs. The scatter between two
runs of the same state is as large as the difference; a deterioration is not discernible.

Counters (exact, independent of the machine):

| Run | Objects per tick | Words folded per tick | Allocation in the hash phase, bytes per tick before → after |
|---|---:|---:|---:|
| Twilight Flame, 8 players, 18,000 ticks | 173.3 | 134,637 (unchanged) | 3,042 → 26 |
| `MD_USA05`, 3,000 ticks | 1,311.8 | 426,247 (unchanged) | 3,739 → 209 |
| `MD_GLA05`, 3,000 ticks | 1,738.8 | 553,584 (unchanged) | (not collected) → 209 |

The remaining allocation is the one-time growth of the word buffer. The allocation of the whole tick falls accordingly
(Twilight Flame 81,903 → 77,881 bytes per tick).

Warm loop on the final state (under load, therefore only the ratio): one chain 4.1–10 ns per word, side by side
1.1 (isolated microbenchmark) to 2.2–4.5 ns per word; writing down 1.5–2.1 → roughly 1.0 ns per word with prepared
frames.

Final hash and chain are those of the starting state in each of these runs: Twilight Flame 18,000 ticks `72CF0F846E0D9AA6` /
`5E446FE63C5BEF46`, `MD_USA05` `1CB28E6AA923BDEF` / `0816C3438CB7F689`, `MD_GLA05` `826CCD82C8536DC2` /
`10330D7D179054FD`.

## 6. What remains, and why it does not fall further in this construction

| Remainder | Size | Why it remains |
|---|---|---|
| **Describing** every marked object with all modules | Missions: 1,300–1,700 objects per tick, on 93–96 % of which only a wake tick has moved; after §4.1/§4.2 the larger part of the phase | Whoever does not read everything does not see what a place without `MarkDirty` has changed (§4.4). Without reliable marking per module or field there is no other way. |
| **Folding** all words of a marked object | 318–777 words per object, of which 78–83 % are module frames that never change | The frames stand between the states in the same chain. Skipping the beginning is possible but, after §4.5, yields nothing any more. |
| Folding itself | roughly 1.1 ns per word side by side (isolated) | Four chains fully load the one multiplier of the core; eight chains were not faster in the microbenchmark. Vector instructions are forbidden in the core (`SourceRules`) and have no 64-bit multiplication. |
| Global part | 7–10 µs per tick | 0.3–0.5 % of the phase. |

Checked and discarded, apart from §4.4 and §4.5:

- **Skipping modules with an empty `Describe`:** 2–12 % of the modules; would need a declared property per module class.
  Too little yield for a further place that has to be right.
- **Eight instead of four chains:** equally fast in the microbenchmark.

## 7. Proposal for the coordinator: a different construction (part A built in Phase 52, D-4; `docs/SIMULATION-CORE.md` §66.3)

According to handoff item 4 this is a separate, named decision. The measurement says: the target value of Phase 19 (tick on
average ≤ 1.0 ms) leaves roughly 100 µs for the hash if one grants it a tenth of the tick; with today's
construction an estimated 380 µs remain on a quiet machine in the skirmish with 8 players and 1.1–1.5 ms in the missions (§5).
The rest is structure, not code. Three parts, in the order of their yield for the missions, which is the **reverse** of the
order of their risk:

**A. Module frame as one word, object hash from partial hashes** (changes every hash value, nothing else).

- Per template and module slot a precomputed **frame key** (today's folding of index, name, tag, once).
- `module hash = Fold(Mix(Mix(Seed, object ID), frame key), state words of the module)`.
- `object hash = Fold(Fold(Seed, own fields), module hash₀ … module hashₙ₋₁)` (the order is preserved).
- Effect on the word count (measured with the prototype writer of the tool, names as one word each): 254 instead of 787
  words per object (Twilight Flame), 110 instead of 294 (`MD_USA05`), 107 instead of 302 (`MD_GLA05`). With partial hashes
  the remaining frame words are dropped in addition: roughly 150 (72 own + 60 state + 17 module hashes) and 76 and 72 respectively.
- That lowers the **folding** to a fifth to a quarter and the writing down by the frames. The **describing** of the
  modules remains. Extrapolation from words × 1.1 ns (measured in isolation), **not a measurement**: Twilight Flame roughly 173 ×
  (0.3 µs describing + 0.17 µs folding) ≈ 80 µs per tick; missions 1,300–1,700 × (0.3–0.5 µs + 0.08 µs) ≈ 0.5–1.0 ms.
  The times of the prototype in the tool were not usable under the load of this machine; the word counts are exact.

**B. Not hashing the wake tick of a module running every tick as an absolute number** (changes every hash value and the
meaning of a hashed field).

- A module that says "again in the next tick" carries no information except "awake": the tick is in the global part.
  What would be hashed is "awake" (a fixed word) instead of `tick + 1`; a later wake tick still as a number. The snapshot keeps the
  number (restoring needs it).
- Then the 93–96 % of the mission objects on which only wake ticks move are **really unchanged**. By itself
  that saves nothing yet (they continue to be marked and described); it is the prerequisite for C.

**C. Marking that holds without the protection of the scheduler** (changes no hash value, but is the rebuild from
`docs/DETERMINISM-FINDINGS.md` §13 item 2).

- Today the scheduler marks every object whose module ran; that covers up missing `MarkDirty` calls (§4.4, §4.6:
  one concealed place found while reading, one that is not concealed is a real error in the starting state). Only when every change of a hashed field marks by itself
  (setters, or a check run that reaches every write site) may an update report "nothing changed", and the
  scheduler may do without it.
- With B and C together roughly 1,200–1,650 of the 1,300–1,700 objects per tick would drop out of the phase entirely in the missions. The
  attempt in §4.4 measured the extent, without B (the trailing part behind the module was still folded): factor 2.5 to 2.8
  compared with the start under load. With B the trailing part would be dropped as well.

**What would have to be rewritten for A and B:** every tick, final and chain hash of the reference manifest (50 runs), the
snapshot hashes there (the snapshot carries the state hash), the checkpoints in recorded replays (replay format
+1), the golden values of the tests (stress test `56882F763EABEF67` / `B8E663493F2C73D5`, reference match, mission values) and
the reference values in `CLAUDE.md` and the documents. **What must stay the same and carries the proof:** winner, end tick,
victory tick, command count, `commandStreamSha256`, rejected commands and the object lists of every run; the input manifest.
Phases 22 and 23 rewrite the manifest anyway – there the change costs the least. C needs no new
manifest, but the check mode on every path that player commands reach.

## 8. Final checks

State `2f815d7` (the code state of the branch; afterwards only this document), 6 October 2026, Release tool from a
copy of the output directory:

| Check | Result |
|---|---|
| `SimTool reference-manifest check <content> baselines/reference-manifest.json --set full` | **50 runs identical** (2,092 s with 6 jobs side by side) |
| the same with `--verify-incremental` | 718,191 ticks checked; **49 runs without deviation**, among them `MD_GLA05`, `MD_USA01` (3,677 ticks), `MD_USA02`, `MD_GLA03`, `GC_LaserGeneral`, `MD_USA05` (3,000 each), the reference match (14,791), the stress test (216 checkpoints) and the skirmish overview (192,000); **1 deviation** in `mission_GC_SuperWeaponsGeneral_GLADemolitionGeneral`, identical at the starting commit (§4.6) |
| `--set quick` and `--set quick --verify-incremental` after each of the three remaining commits | 9 runs identical; 0 deviations in 60,896 ticks |
| `SimTool input-manifest check <content> baselines/input-manifest.json` | identical |
| `SimTool stress` | `56882F763EABEF67`, chain `B8E663493F2C73D5` (six runs, §5) |
| Reference match (in the manifest and in the test "retail match") | FactionGLA at tick 14,490, as in the manifest |
| `tests/CnCNew.Simulation.Tests` Debug | 353 passed, 0 failed |
| `tests/CnCNew.Simulation.Tests -c Release -- --long --content-dir <content>` | 368 passed, 0 failed (among them the 216,000-tick run and `SourceRules`) |
| `dotnet build CnCNew.slnx` after deleting `bin`/`obj` of `src/CnCNew.Client.Stride` | 0 warnings, 0 errors |

The manifest files are unchanged.

Not checked: the remaining test programs of the solution (`CnCNew.Game.Tests` with its `DeterminismTests`), the client,
a second machine, a quiet measurement.

For the coordinator three things therefore remain to be decided: the existing D8 error from §4.6 (one line, one
manifest value), the reverted attempt from §4.4 (factor 2.5–2.8 instead of 1.5 in the missions against the protection of the
scheduler) and the different construction from §7.
