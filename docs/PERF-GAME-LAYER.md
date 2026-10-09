# Performance of the game layer, measurement summaries and the way to a simulation thread

As of: Phase 19, items 6, 7 and 8 (`docs/handoffs/HANDOFF-PHASE19.md`). Supplements `docs/PERFORMANCE.md` (measuring tool of the simulation, quiet
starting measurement); how `SimTool perf` measures is described there. This document describes

1. the machine-readable summary of the measurement runs and the comparison of two runs (`--json`, `SimTool perf-compare`, §1 and §2),
2. the measurement of the game layer per tick (`PresentationTool sync-perf`, §3), the measured values (§4) and what was removed from them (§5),
3. what stands in the way of putting the simulation on a thread of its own (§6).

**All times in this document are not measured quietly.** The machine was used by four further agents during the runs
(CPU utilisation before the runs 70–85 %). Reliable are the ratios within one run, the counted quantities (allocation, GC collections,
object counts) and the checksums; the absolute times need to be re-measured on a quiet machine (commands in §7).

## 1. Machine-readable summary (`--json`)

`SimTool perf`, `SimTool perf-mission` and `PresentationTool sync-perf` write a summary of the run with `--json <datei>`.
`--label <text>` puts a free text into it, usually the commit; **the tool does not ask git** (`scripts/perf-profile.ps1`
passes the short hash of `HEAD` without `-Label`).

The file is a `ManifestDocument` (`tools/CnCNew.SimTool/ManifestDocument.cs`, the same form as the reference manifests under
`baselines/`): UTF-8 without BOM, LF, two spaces per level, keys in ordinal order, only texts, integers,
truth values and objects. The same run yields the same bytes apart from the measured values, on every machine and under every culture. Times
are therefore **whole nanoseconds** (field name ends in `Ns`), sizes whole bytes. The pure logic lies in
`tools/CnCNew.SimTool/PerfSummary.cs` and is included as source code in `CnCNew.PresentationTool` and in `CnCNew.Simulation.Tests`
(no new dependency).

Schema 1:

| Field | Content |
|---|---|
| `kind`, `schema` | `"perf-summary"`, `1`; everything else is rejected |
| `tool` | `perf`, `perf-mission` or `sync-perf` |
| `label` | free text of the caller (commit) |
| `hardware` | the hardware line of the output (processor, logical processors, memory, operating system, runtime, GC kind, clock resolution) |
| `parameters` | everything that determines which run this is: `map`, `ticks` (budget), `observer`, `modules`; with `perf` additionally `factions`, `aiSeed`, `worldSeed`; with `perf-mission` `difficulty`, `worldSeed`; with `sync-perf` `factions`, `seed`, `viewport`, `hud`, `digest` |
| `results` | `ticks` (actually run) and `finalHash` always; with `perf` and `perf-mission` `chainHash` and `incrementalEqualsFull`, with `perf` in addition `winner`, `victoryTick`, with `perf-mission` `outcome`; with `sync-perf` `winner` and with `--digest` `presentationDigest` |
| `tick` | `meanNs`, `medianNs`, `p95Ns`, `p99Ns`, `maxNs` of the whole step |
| `phases.<Name>` | the same five values per phase (`TickPhase` or `PresentationPhase`); in addition `(unattributed).meanNs` and `(world).meanNs` or `(game layer).meanNs` |
| `modules.<Name>` | the most expensive module types of the update phase (`--top`): `calls`, `totalNs`, `meanNs`, `maxNs` |
| `allocation` | `bytesPerTickMean`, `bytesPerTickMedian`, `bytesPerTickMax`, `gen0`, `gen1`, `gen2` |
| `objects` | `min`, `max`, `end`; with `sync-perf` additionally `drawablesMax`, `drawablesEnd`, `particlesMax`, `soundCues` |
| `replay` | only with `--replay`: `perTickNs`, `ticksPerSecond` (catch-up speed = ticks per second / tick rate), `bytesPerTickMean`, `gen0..2`, `hashEqualsLive` |
| `snapshot` | `bytes`, `bytesPerObject`, `writeNs`, `restoreNs`, `restoredHashEqual` |

Tested in `tests/CnCNew.Simulation.Tests/Tests.PerfSummary.cs` (canonical form, round trip via a file, strict check
of `kind`/`schema`).

## 2. Comparison of two runs (`SimTool perf-compare`)

```powershell
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- perf-compare <alt.json> <neu.json> [--threshold-percent N] [--min-us N] [--all] [--allow-different-hash] [--out <datei>]
pwsh scripts/perf-profile.ps1 -Label "<commit>" -OutDir <verzeichnis>     # writes <name>.txt, <name>.csv, <name>.json per run
pwsh scripts/perf-compare.ps1 <altesVerzeichnis> <neuesVerzeichnis> [-ThresholdPercent N] [-AllowDifferentHash]
```

`perf-compare` prints a table old / new / change per metric and marks changes above the threshold (default 10 %) as
`REGRESSION` or `better`:

- **Lower is better** for all times, allocations, GC collections and snapshot sizes; **higher is better** for `ticksPerSecond`.
  Pure counted quantities (`objects`, `results`, `calls`) are shown but never judged.
- Times that lie below 1 µs on **both** sides (`--min-us`) are not judged: the clock resolves 100 ns, a phase of
  0.3 µs doubles by chance.
- Without `--all` the table shows the mean per phase and the sum per module; `--all` shows every metric.
- The comparison is **refused** (exit code 2, line `REFUSED: …`) if the summaries come from different commands,
  if `parameters` differs (with field and both values) or if the **final hash** is different. A different final hash means:
  that is a different run, the simulation has not done the same work, the times say nothing about speed. With
  `--allow-different-hash` the table comes anyway, under a line `WARNING: DIFFERENT FINAL HASH: …`.
- A different hardware line is a warning (`not comparable`), not an abort.
- **Exit code 0 as soon as a table was printed – also with regressions.** There is deliberately no failing test from
  times (`docs/handoffs/HANDOFF-PHASE19.md` item 8): they depend on the machine and its load. Exit code 1 only on unreadable input.

Example (two runs of the **same** state on the loaded machine, shortened – this is how large the scatter is here):

```
perf-compare: perf, old "run-a" against new "run-b"; threshold 10 %, times below 1 us on both sides are not judged.
parameters: aiSeed=1592590342, factions=FactionAmerica,FactionAmericaAirForceGeneral, map=Maps/Scorched Earth/Scorched Earth.map, modules=true, observer=true, ticks=3000, worldSeed=3237998086

| metric | old | new | delta | |
|---|---:|---:|---:|---|
| tick.maxNs | 66279.9 us | 63232.5 us | -4.6 % |  |
| tick.meanNs | 1036.1 us | 1114.0 us | +7.5 % |  |
| tick.p99Ns | 3505.5 us | 4545.7 us | +29.7 % | REGRESSION |
| phases.Hash.meanNs | 142.4 us | 164.7 us | +15.7 % | REGRESSION |
| phases.Updates.meanNs | 472.0 us | 475.2 us | +0.7 % |  |
| allocation.bytesPerTickMean | 4632 | 4631 | 0.0 % |  |
| replay.ticksPerSecond | 3162 | 2992 | -5.4 % |  |
| snapshot.writeNs | 2741.9 us | 2014.7 us | -26.5 % | better |
| results.ticks | 3000 | 3000 | 0.0 % |  |
...
20 regression(s) and 2 improvement(s) beyond 10 % in 61 metrics. Times depend on the machine and its load: this is a report, not a test.
```

`scripts/perf-compare.ps1` calls `perf-compare` for every run whose `<name>.json` lies in both directories, writes per
run `compare-<name>.txt` and an overview `compare.md` (mean, 99 %, maximum, allocation, replay speed, number of marked
changes, final hash). Runs that lie only on one side, and refused comparisons, are listed as such in the table. The script
never fails because of a time. The comparison logic is tested in `Tests.PerfSummary.cs` (marking, threshold, noise limit, refusal
on different parameters, a different command and a different final hash, missing metric, table form).

From now on the completion of a phase includes: `scripts/perf-profile.ps1` on a quiet machine, keeping the directory and comparing it with that of the
previous phase.

## 3. Measurement of the game layer (`PresentationTool sync-perf`)

```powershell
dotnet build -c Release tools/CnCNew.PresentationTool
dotnet run -c Release --no-build --project tools/CnCNew.PresentationTool -- sync-perf game/content --map "Maps/Twilight Flame/Twilight Flame.map" --players 8 --ticks 18000 --hud --json <datei.json> --label "<commit>"
dotnet run -c Release --no-build --project tools/CnCNew.PresentationTool -- sync-perf game/content --map "Maps/Twilight Flame/Twilight Flame.map" --players 8 --ticks 18000 --hud --digest
```

Options: `--map`, `--players N` (the first N playable factions, like `SimTool perf`) or `--factions A,B,…`, `--ticks`, `--seed`,
`--viewport BxH` (default 1280x720), `--hud`, `--digest`, `--json`, `--label`.

The tool starts a `LiveMatch` without a window. The seat of the viewer (player 1) is also played by the computer
(`MatchSetup.AutoPlay`), so it is **the same match as `SimTool perf` with the same players** – the final hash of the world must be the same
and is (Twilight Flame, 8 players, 18,000 ticks: `72CF0F846E0D9AA6` in both tools). Per step it calls exactly what
the client calls per frame: `LiveGame.Advance` with the length of one tick. That is one `BeforeTick`, one simulation step, one
`SyncAfterTick`, the `TickStepped` subscribers, the fading of the fog and **one** `FrameProjector.Project` – one frame per tick as a
stand-in for 30 frames per second. At 60 Hz frame rate and 30 Hz tick rate the per-frame parts (`FogAdvance`, `Project`, HUD)
occur twice as often as the per-tick parts.

The tool reads the clock. `LiveGame.Advance` and `FrameProjector.Advance` only report to an optional observer where their parts
begin and end (`IPresentationPhaseObserver`, `PresentationPhase` in `src/CnCNew.Presentation/PresentationPhase.cs`;
`LiveGame.PhaseObserver`). In the game the observer is `null`: one null check per part, no allocation, no different sequence.

The parts, named as in the code (indented = contained in the part above):

| `PresentationPhase` | Where | Content |
|---|---|---|
| `BeforeTick` | `LiveGame.Advance` | the `BeforeTick` callback: with HUD `GameInputController.Tick` (prune the selection, standing orders), otherwise empty |
| `StepSimulation` | | the session step (AI plus world step) |
| `SyncAfterTick` | | everything that follows up to `FogUpdate` |
| – `ProjectorObserve` | `FrameProjector.Observe` | events of the tick for unit states and selection flashing |
| – `ProjectorAdvance` | `FrameProjector.Advance` | the four following parts plus the cleanup every 150 ticks |
| – – `ProjectorAdvanceVisibleEntities` | `SimulationWorld.EntitiesVisibleTo` | list of the objects visible to the viewer (code of the simulation) |
| – – `ProjectorAdvanceCapture` | `CaptureEntity` per object | model conditions, state selection, turrets, suspension |
| – – `ProjectorAdvanceGhosts` | `UpdateGhosts` | remembered enemy buildings |
| – – `ProjectorAdvanceScenery` | `UpdateSceneryExploration` | which scenery is explored and which lies in the fog |
| – `FxViewRefresh` | `SimulationFxWorldView.Refresh` | pose of every object for the effects |
| – `FxExecuteEvents` | `FxExecutor.ExecuteEvents` | effects and sound requests of the events |
| – `FxUpdate` | `FxExecutor.Update` | particles, tracers, lights, decals |
| – `DrainSoundCues` | `FxExecutor.DrainSoundCues` | handover to the list of the game |
| – `FogUpdate` | `FogOfWarGrid.Update` | cell states from the vision map, every third tick |
| `TickStepped` | `LiveGame.Advance` | the subscribers (with HUD statistics and messages) |
| `FogAdvance` | `FogOfWarGrid.Advance` | fading of the brightness levels, per frame |
| `OnAdvanced` | | script camera of a mission, per frame |
| `Project` | `FrameProjector.Project` | the `RenderFrame` with interpolation, per frame |
| `HudUpdate`, `HudBuildDrawList` | measured by the tool, only with `--hud` | `MatchController.Update` and `BuildDrawList`, per frame |

**Checksum of the presentation (`--digest`).** Per tick the following are fed into a SHA-256 outside the measured time: every `DrawableInstance`
of the frame with all fields (position, angle, owner, colour, condition bits, chosen states, turrets, suspension, flashing …), the
brightness levels of the fog (`FogOfWarGrid.CopyLevels`), the effect picture (`FxExecutor.Capture`: particles as raw bytes, tracers,
lights, decals, beams, shakes) and the sound requests of the tick. Two states with the same checksum have produced the same presentation over the
whole run. That is the evidence for "output-neutral" in §5. Not included: the draw list of the HUD
(nothing was changed there) and everything that only the Stride layers make of the frame.

**Limits.** With `--hud` a real `MatchController` is attached to the match, but without inputs, without a selection and without a mouse pointer over the
world: the command bar is empty. A full selection costs more (`CommandBarModel.Build` checks every button). Sound (`AudioDirector`), the
Stride layers and the minimap are not included. The allocation per tick is the sum of simulation and game layer (one
thread).

## 4. Measured values (not measured quietly)

Measured on 6 October 2026 with `PresentationTool sync-perf … --map "Maps/Twilight Flame/Twilight Flame.map" --players 8 --ticks 18000 --hud`
(486 × 469 cells, 632 map objects, frame 1280 × 720, HUD without a selection). Machine: AMD64 Family 25 Model 68 (Ryzen 7 PRO 6850U),
16 logical processors, 28 GiB, Windows 10.0.26200, .NET 10.0.12, workstation GC, clock resolution 100 ns. **Not quiet**: four further
agents were building and measuring at the same time; the same state ran in the simulation step between 9.5 and 17.6 ms per tick on average
(quiet according to `docs/PERFORMANCE.md` §6: about 5 ms at 54,000 ticks). In all runs: 18,000 ticks, no winner, final hash of the world
`72CF0F846E0D9AA6` (as `SimTool perf` for this match), 28 to 268 objects (end 234), 2 to 151 drawables per frame (end 106),
at most 1,468 particles, 43,255 sound requests.

### 4.1 Breakdown before the changes (state `5d0aac2`, the run with the lowest load)

Microseconds per simulated tick; indented rows are contained in the row above.

| Part | Mean | Median | 95 % | 99 % | Maximum | Share of the tick |
|---|---:|---:|---:|---:|---:|---:|
| Tick in total (`Advance` plus HUD) | 11,567.1 | 9,782.1 | 19,737.1 | 38,119.8 | 701,284 | 100.0 % |
| `BeforeTick` | 3.8 | 2.8 | 5.8 | 10.3 | 5,515 | 0.0 % |
| `StepSimulation` | 9,507.9 | 7,564.2 | 17,403.0 | 33,758.3 | 698,674 | 82.2 % |
| `SyncAfterTick` | **907.7** | 725.0 | 1,766.3 | 2,178.0 | 35,240 | 7.8 % |
| – `ProjectorObserve` | 2.4 | 2.3 | 4.5 | 6.0 | 1,654 | 0.0 % |
| – `ProjectorAdvance` | 374.8 | 343.3 | 610.7 | 844.0 | 28,157 | 3.2 % |
| – – `ProjectorAdvanceVisibleEntities` | 35.9 | 34.4 | 59.3 | 79.2 | 6,230 | 0.3 % |
| – – `ProjectorAdvanceCapture` | 226.5 | 199.9 | 412.2 | 545.5 | 26,656 | 2.0 % |
| – – `ProjectorAdvanceGhosts` | 10.7 | 9.1 | 20.3 | 27.7 | 3,228 | 0.1 % |
| – – `ProjectorAdvanceScenery` | 98.6 | 95.5 | 126.2 | 200.2 | 7,259 | 0.9 % |
| – `FxViewRefresh` | 36.8 | 32.2 | 66.7 | 92.1 | 1,994 | 0.3 % |
| – `FxExecuteEvents` | 59.6 | 56.6 | 137.9 | 177.5 | 11,340 | 0.5 % |
| – `FxUpdate` | 125.8 | 107.6 | 286.7 | 389.2 | 16,675 | 1.1 % |
| – `DrainSoundCues` | 1.7 | 1.8 | 3.2 | 4.3 | 48 | 0.0 % |
| – `FogUpdate` (every third tick) | 305.8 | 0.1 | 1,050.0 | 1,202.1 | 20,064 | 2.6 % |
| `TickStepped` | 6.2 | 4.3 | 12.8 | 19.3 | 3,448 | 0.1 % |
| `FogAdvance` (per frame) | **733.7** | 738.3 | 903.6 | 1,033.7 | 15,336 | 6.3 % |
| `OnAdvanced` (per frame) | 0.2 | 0.1 | 0.5 | 1.0 | 60 | 0.0 % |
| `Project` (per frame) | 29.3 | 24.4 | 45.5 | 67.5 | 10,963 | 0.3 % |
| `HudUpdate` (per frame) | 111.5 | 100.9 | 174.8 | 227.2 | 16,847 | 1.0 % |
| `HudBuildDrawList` (per frame) | 265.3 | 238.0 | 407.7 | 573.0 | 35,258 | 2.3 % |
| **Game layer alone** (tick without `StepSimulation`) | **2,059.3** | | | | | 17.8 % |

Allocation (simulation and game layer together): 173,881 bytes per tick on average, 180,440 in the median, 570,320 at the maximum; GC 377/58/2.
For comparison: `SimTool perf` measured 81,898 bytes per tick for the same match for the simulation alone (`docs/PERFORMANCE.md` §7.3, older
state, likewise not quiet) – the game layer accordingly allocates about as much as the simulation, about 90 KB per tick.

What the table says:

1. `SyncAfterTick` costs 0.9 ms per tick, the game layer in total 2.1 ms: in the same run 22 % of the simulation step on top.
   If the simulation reaches the goal of Phase 19 (1 ms per tick), the unchanged game layer would be **as expensive as the simulation
   or more expensive**.
2. The two largest items depended on the **map area**, not on what happens: `FogAdvance` (every cell per frame) and `FogUpdate` (every
   cell every third tick), together 1.04 ms = half of the game layer.
3. The rest is spread out: HUD 377 µs per frame already without a selection, capture of the objects 227 µs, effects 222 µs, scenery 99 µs.
4. The maxima (15 to 35 ms in individual parts) coincide with garbage collections and the load of the machine and cannot be
   evaluated here.

### 4.2 Before and after, measured directly one after the other

State `5d0aac2` (before the changes) and `b989808` (after), two runs directly one after the other; nevertheless the load was lower
in the second run: the **unchanged** parts ran 15 to 27 % faster (`StepSimulation` −21.5 %, `FxViewRefresh` −24.2 %, `HudUpdate`
−23.9 %). The changed parts are to be read against that. Mean in microseconds per tick (`SimTool perf-compare ab-base.json ab-final.json`):

| Part | before | after | change | remark |
|---|---:|---:|---:|---|
| `StepSimulation` | 15,337.8 | 12,033.9 | −21.5 % | unchanged: measure of the load |
| `FxViewRefresh` | 69.0 | 52.3 | −24.2 % | unchanged |
| `HudUpdate` + `HudBuildDrawList` | 653.2 | 505.5 | −22.6 % | unchanged |
| `FogAdvance` | 1,095.9 | **10.0** | **−99.1 %** | §5.1 |
| `ProjectorAdvanceScenery` | 147.0 | **67.2** | **−54.3 %** | §5.2 |
| `ProjectorAdvanceCapture` | 367.0 | **176.7** | **−51.8 %** | §5.3 |
| `FogUpdate` | 434.7 | 472.7 | +8.7 % | see below |
| `SyncAfterTick` | 1,380.0 | 1,052.0 | −23.8 % | |
| **Game layer alone** | **3,193.5** | **1,617.9** | **−49.3 %** | 17.2 % → 11.9 % of the tick |
| Allocation per tick (bytes, exact) | 173,885 | **165,950** | −4.6 % | GC 377/75/3 → 359/58/2 |

Converted to the load of the first run from §4.1 (factor from the unchanged parts) the game layer after the changes is
at roughly **1.1 to 1.2 ms per tick instead of 2.06 ms**; that is an estimate, not a measured value.

**`FogUpdate`** has one comparison more per cell through §5.1. In the pair of runs above it rises by 8.7 % while the load dropped. Three
alternating short runs (2,400 ticks, per state) however show no difference that stands out from the scatter: the 95 % quantile –
the cost of one refresh – was at 1,161 / 7,870 / 1,266 µs before and at 1,417 / 1,229 / 1,262 µs after. An attempt to rearrange the
loop (write the state only on a change, rare path moved out) yielded 1,163 / 1,222 /
8,916 µs in the same runs, i.e. nothing measurable, and was not adopted. Whether `FogUpdate` has become a few percent more expensive is **not
decidable** on this machine; against the 0.7 to 1.1 ms that `FogAdvance` saves per frame it does not matter.

### 4.3 Extrapolation

At 60 ticks per second and one frame per tick: game layer before 2.06 ms × 60 = 12.4 % of one core (run §4.1), after, according to the
estimate above, about 7 %. At 30 Hz tick rate and 60 frames per second the per-frame parts occur twice: before
`FogAdvance` + `Project` + HUD = 1.14 ms per frame = 6.8 % of one core for that alone, after about 0.4 ms = 2.5 %.

## 5. What was removed and why it is output-neutral

Every change is a commit of its own with one cause. Evidence of the neutrality for all three: the checksum of the presentation over
18,000 ticks (Twilight Flame, 8 players, `--hud --digest`) is before and after every change
`65AE95BEE88937CA2F903C39AED954C814A3523619BB51C35D7A85D49B7DAF44`, the final hash of the world `72CF0F846E0D9AA6`. In addition, on the final state
against the state before it: the three scenes of `scenario-audit` (`capture-building`, `tank-duel`, `jet-strafe`) pass and print
**byte-identical** output (the whole time course, not only the result); `object-audit` and `fx-audit` print byte-identical
output (SHA-256 of the output `032CF63D…B3CFEABE` and `5504C152…CE4BEDF59` respectively), `terrain-audit` apart from the one line
`build time total (not deterministic)`; `tests/CnCNew.Presentation.Tests` and `tests/CnCNew.Game.Tests` (Release, with content)
pass (`All game tests passed.`, exit code 0). The simulation was not touched.

The before/after times per change come from runs under changing load; beside them there is in each case an unchanged phase
of the same pair of runs as a measure of the load.

### 5.1 The fog fades only cells that are still under way (`a15dc3b`)

`FogOfWarGrid.Advance` ran **per frame** over every cell of the map (Twilight Flame: 486 × 469 = 227,934 cells) and computed the
fade rule for each. A cell whose level has reached the level of its state is a fixed point of this rule
(`Math.Max(target, target - step) == target`). The grid therefore keeps a list of the cells that are still under way: a cell
enters it when its state changes to one with a different level (`SetState`), and drops out when its level arrives.
The order does not matter (cells are independent), `Snap` empties the list.

- `FogAdvance`: mean **733.7 µs → 15.4 µs** per frame (median 738.3 → 8.5), although the second run ran under about twice the load
  (`StepSimulation` 9,508 → 17,647 µs, `FxViewRefresh` 36.8 → 75.3 µs).
- `FogUpdate` has one comparison more per cell (old against new state): 305.8 → 649.2 µs in the same pair of runs, i.e. within the range
  of the load (factor 2.1 against 1.9 to 2.0 of the unchanged phases); the pair of runs under equal load is in §4.
- Neutral, because for resting cells the same value would come out and for the others the same computation runs with the same numbers. New
  test `VisionTests.FadingEqualsTheFullPass`: the grid against the plain rule over all cells, 400 rounds with state changes
  in the middle of the fading, steps of length 0, `Snap` and the path via the `VisionMap`, for three settings (among them equal levels
  for two states).

### 5.2 Scenery positions are converted to fixed point once (`9c77402`)

`UpdateSceneryExploration` converted **in every tick** the position of every scenery object not taken over from `float` via `decimal`
to `Fixed` (two `decimal` conversions per object; Twilight Flame has 632 map objects) before it asked the vision map. Scenery
never moves: the two coordinates are converted once in the constructor (as before only with a viewer) and read from a
field.

- `ProjectorAdvanceScenery`: mean **199.4 → 82.0 µs**, median 122.1 → 57.8 (two runs under equal load).
- Neutral, because the same conversion of the same `float` gives the same `Fixed`.

### 5.3 The chosen model states stay as long as visual and condition bits stay (`b989808`)

`FrameProjector.CaptureEntity` chose in every tick for every visible object the best model state per draw module
(`BestStateSelector.FindBest` over all states, via LINQ with a new array). The choice is a pure function of the `ObjectVisual`
and of the condition bits. An object whose bits have not changed since the previous capture takes over the choice of the previous
capture; the choice itself is now a simple loop.

- `ProjectorAdvanceCapture`: mean **377.7 → 307.2 µs**, median 264.3 → 176.3 (two runs under about equal load).
- Allocation per tick (simulation and game layer together, exact count): **174,039 → 166,161 bytes**.
- Neutral, because for the same inputs the same immutable array comes out (same instance of the visual, same bits).

### 5.4 What was not removed and why

| Place | Cost (run before the changes, §4) | Why not |
|---|---:|---|
| `FogUpdate` | 306 µs on average, about 1.05 ms on every third tick | The vision map offers only queries per cell (`VisionMap.IsVisible`/`IsExplored`); 227,934 cells × up to two calls. It would be faster only with a bulk query or a change counter in `VisionMap` – that is code of the simulation and in this phase a matter of the vision brief. Proposal: `VisionMap` hands out the cell bytes of a player as `ReadOnlySpan<byte>` or keeps the set of the cells changed since the last refresh; `FogOfWarGrid.SetState` is already prepared for that. |
| `ProjectorAdvanceVisibleEntities` | 36 µs | `SimulationWorld.EntitiesVisibleTo` (new list and sorting per tick): code of the simulation. |
| `ProjectorAdvanceCapture` (rest) | about 150–200 µs | `ConditionDeriver.Derive` reads status, modules, weapons, upgrades and terrain per object; the values can change in every tick, caching would need a change notification of the simulation per object. The capture (`Capture`, one instance per object and tick) is part of the interpolation between two ticks. |
| `FxUpdate`, `FxExecuteEvents` | 126 µs and 60 µs | Particle simulation (up to 1,468 particles) and effects of the events; the order of the random numbers determines the output. No repeated effort found. |
| `FxViewRefresh` | 37 µs | The pose of every object is copied per tick so that death effects find the last pose of an object already removed. Which objects die in the next tick is not known beforehand. |
| `HudUpdate`, `HudBuildDrawList` | 112 µs and 265 µs per frame, with an empty selection | The HUD rebuilds the command bar, the player values and the whole draw list of the window tree per frame. A cache with exact invalidation would be possible, but is a brief of its own: the checksum of this tool does not cover the draw list, and the expensive case (full selection, pointer over the world) is not measured here. |
| `Project` | 29 µs per frame | One `DrawableInstance` per object and frame is the contract of the `RenderFrame` (immutable). |

## 6. What stands in the way of a simulation thread of its own

Not built; `SourceRules` forbids threads in the core, and that stays so. The question is whether the **client** could let `StepSimulation` run on a
second thread while the first draws. Today everything runs on one thread, in this order per frame
(`ClientRuntime.Update` → `RunningGame.Step`):

1. Input events → `MatchController.Process` (read the world, enqueue commands).
2. `LiveGame.Advance`: up to eight ticks, per tick `BeforeTick` → `StepSimulation` → `SyncAfterTick` → `TickStepped`; after that
   `Fog.Advance`, `OnAdvanced`, `Project`.
3. `MatchController.Update` (HUD), then the layers terrain, objects, effects with `LiveGame.ObjectFrame`.
4. Input script, minimap dots, `AudioDirector.Update`.
5. When drawing `MatchController.BuildDrawList` (tooltips, superweapon timers, mission overlay).

### 6.1 What is already immutable

- `RenderFrame`/`DrawableInstance` (`FrameProjector.Project`): the Stride layers `ObjectLayerCore`, `FxLayer` and `TerrainLayer`
  **never** read the world, only `ObjectFrame`, `Fx.Capture`, `Fog`, `Camera`, `Heights`, `TerrainModel`.
- `TickResult.Events` for statistics (`MatchStatistics.Record`).
- `FrameProjector` holds between `Advance` and `Project` only captures of its own (`Capture`: numbers, `Fixed`, immutable `ObjectVisual`),
  no `Entity`. Outside the simulation **nobody** holds an `Entity` beyond one call (selection, standing orders,
  command sources: only ids).
- `CnCNew.Audio` and `CnCNew.Ui` do not know the simulation at all.

### 6.2 Read accesses to the running, mutable world

Count (grep over `src`, pattern `world.`/`World.` with member, `FindEntity`, `EntitiesVisibleTo`, `IsPositionVisibleTo`,
`IsPositionExploredBy`, `SimulationQueries.`, `Session.`, `.Engine.`; comment lines and a few parameters of the same name included,
i.e. an upper bound): **`CnCNew.Presentation` 213 places in 19 files, `CnCNew.Game` 113 in 20, `CnCNew.Client.Stride` 22 in 4,
together about 350 places in 43 files.** `SimulationQueries` alone has about 80 call sites in 10 files.

| When | Who | What is read | Places (rough split of the count by the place where the code of the file runs) |
|---|---|---|---:|
| **in the tick**, before the step (`BeforeTick`) | `GameInputController.Tick` → `SelectionModel.Prune`, `CommandModeController.Update`, `StandingOrders.Update` | `FindEntity`, owner, status, `CanPlayerSee`, `IsAttackMoving`, `AttackTargetOf`, `ChooseWeapon`, range, angle, tick; **enqueues commands while doing so** | 35 |
| **in the tick**, after the step (`SyncAfterTick`) | `FrameProjector.Advance` with `ConditionDeriver`, `StructureConditions`, `SuspensionTracker`, `UnitConditionTracker`, `TurretTracker`, `CaptureConditions`; `FrameProjector.Observe` | `EntitiesVisibleTo`, per object pose, body, status, modules, weapon state, turrets, construction progress, upgrades, docks, terrain height; `IsPositionVisibleTo`/`IsPositionExploredBy` for ghosts and scenery | 50 |
| | `SimulationFxWorldView.Refresh` and `GetPose`/`GroundHeight` (from `FxExecutor.ExecuteEvents`, `ParticleWorld.Step`, `SoundCues`) | all objects (pose, owner, rank), `FindEntity`, terrain height | 4 |
| | `FogOfWarGrid.Update` | every cell of the vision map of the viewer | 2 |
| **in the tick** (`TickStepped`) | `MatchStatsTracker.Observe`, `HudMessageFeed.Observe`, `InputScript` | `FindEntity` for victim, perpetrator, depositor; player list; in the test script a full object scan | 15 |
| **in the tick**, missions only | `MissionPresentationDirector.BeforeStep`/`AfterStep` | `Engine.DrainOutputs`, `Engine.NamedObjects`, `TeamInstances`, `FindEntity`, pose; controls HUD, flashing, sound | 20 |
| **per frame** after `Advance` | `MatchController.Update`: `CommandModeController.Hover`/`Evaluate`, `EntityPicker.Pick`, `CommandBarModel.Build`/`Hud`, `ComputeOverlay`, `OrderMarkers` | object under the pointer (full scan with visibility check), per selected object and button: modules, production queue via `Describe`, prerequisites (full scan), money, power, sciences, upgrades, special powers, ready ticks; rally points; health bars; placement check (navigation grid, exploration) | 110 |
| **per frame**, when drawing | `MatchController.BuildDrawList`: `SuperweaponTimers.Collect`, `CommandButtonDescription.Build`, `RefreshDynamicWindows` | full object scan per tick, special power readiness, prices, missing prerequisites | 15 |
| **per frame** | `AudioDirector` via `LiveAudioView` | `EnergyProductionOf`, `RelationshipOf`, `Tick` (everything else comes from `Frame`, `DrainSoundCues`, camera) | 3 |
| **per frame**, mission | `MissionPresentationDirector.ApplyCameraTo`, `BuildOverlayCommands` | pose of a named object for the camera, `Engine.CounterValue` for counters | 5 |
| **on input** | `GameInputController.Process`/`DispatchMeta`, `SelectionModel` (click, box, double click, groups), `EntityPicker`, `CommandModeController.IssueContext`/`Activate`/`CompleteTarget`/`PlacementRelease`, `GameAudio.OnSelectionChanged` | full object scans with visibility check and projection, relationships, containers, weapon choice; **enqueues commands and needs the answer immediately** | 75 |
| only setup and end | constructors, `LiveScenario` stage, saving (`SaveGame.Write` reads the whole world), match result | – | 20 |

Shared mutable objects of the presentation itself (uncritical today, because one thread): `FogOfWarGrid` (states in the tick,
levels per frame, read by `TerrainLayer` and the minimap), `FxExecutor`/`ParticleWorld` (step in the tick, `Capture` per frame hands out
beams and shakes once), `FrameProjector` (capture in the tick, `Project` per frame; scripts change flashing and
house colour in the tick), `LiveGame._soundCues`, `SelectionModel`/`CommandModeController`/`StandingOrders` (pruned in the tick, used on input
and per frame), `HudMessageFeed`, `MissionPresentationDirector`, `AudioDirector` (scripts play sounds in the tick).

### 6.3 Write accesses from outside

1. **Commands** via `ICommandSink.Issue` (`SkirmishSessionCommandSink`, `MissionCommandSink`): 16 command kinds from
   `CommandModeController`, plus `StandingOrders` in every tick. `Issue` is **synchronous** and returns `SubmitResult`; the caller
   uses `Accepted` immediately for sound, feedback and change of mode and reads `world.Tick` while doing so.
2. **Directly into the script VM**, not via commands: `ScriptEngine.CompletePresentation` (before the step), `SelectNamed` (on a
   change of selection), `SignalUiInteraction`, `DrainOutputs` (consuming read).
3. `LiveScenario` (staged demos): `World.Submit`, `World.Step`, `RevealMapPermanently`, `CompleteUpgrade`, `SetReadyFrame` directly.
4. Loading a saved game replaces the whole session.

### 6.4 The four actual obstacles

1. **The HUD asks the world instead of getting a picture.** About 110 places per frame and 75 on input compute availability,
   prerequisites, queues, pointer context and placeability directly on living objects, partly via
   `SimModule.Describe` (a complete state tree of the module, only to read the queue). That is the largest item.
2. **Standing orders are a client-side state machine in the tick.** `StandingOrders.Update` has to see the state of exactly tick N and
   bring its commands in tick N; the commands are in the replay. If the simulation runs ahead, the result changes. For the
   network game that has to be clarified anyway (`docs/MULTIPLAYER-PLAN.md` §2.3: one match path via the lockstep driver) – most cleanly
   the logic moves into the simulation as a command with a duration.
3. **Commands expect an immediate answer.** With a lockstep driver a command is enqueued for a later tick anyway;
   the pre-check ("may I do that?") then has to run against the last picture, the result of the tick comes back as an event.
4. **Missions interleave script VM and presentation in the tick** (fetch outputs, report completion back, switch HUD/sound/flashing).
   `CompletePresentation` is a feedback of the presentation into the simulation; `docs/MULTIPLAYER-PLAN.md` §6 rule 5 forbids it
   for network matches, missions stay local.

### 6.5 What an immutable picture per tick would have to contain

So that a client thread never reads the running world, the simulation thread would have to hand over a package after every tick
(order of magnitude: what `FrameProjector.Advance` already captures today, plus what the HUD asks for per frame):

| Part | Content | Today's source |
|---|---|---|
| Head | tick, `IsOver`, winner, state hash | `LiveGame`, session |
| Events | `TickResult.Events`, **supplemented by the fields that subscribers look up today** (owner and template of victim, perpetrator, depositor) | `MatchStatsTracker`, `HudMessageFeed`, `SelectionFlash`, `UnitConditionTracker` |
| Objects | per object that the viewer may see **or select**: id, template, owner, pose and angle (also as `Fixed` for queries), speed, health, status bits, rank, container (content, capacity) and carrier, construction progress and builder, the condition bits of the model (the result of `ConditionDeriver`, not its inputs), turret and running-gear values, last shot ticks, selectability, visibility/stealth for the viewer | `FrameProjector.CaptureEntity`, `EntityPicker`, `SelectionModel` |
| Command view per own object | active command set, per button available/reason, production queue with progress, rally point, upgrades, special powers with ready tick, weapon range, move/attack/build target, whether it can build, dock, repair, enter | `CommandBarModel`, `SimulationQueries`, `SuperweaponTimers`, `ComputeOverlay` |
| Players | money, power (production/consumption), sciences and points, upgrades, relationships, defeated | `CommandBarModel.Hud`, `LiveAudioView`, `MatchResult` |
| Vision | cell states of the viewer – or better only the cells changed since the last picture | `FogOfWarGrid.Update` |
| Placement | a query that does not go via the picture: footprint against the navigation grid. Either the grid (or its changes) in the picture, or the check as a request to the simulation thread with the answer in the next picture | `SimulationQueries.CheckPlacement` |
| Effects | the poses of all objects for the effects (today `SimulationFxWorldView`, a copy per tick already exists) and the terrain height (immutable, the client can hold it itself) | `FxWorldView` |
| Mission | script outputs of the tick, named objects and teams with pose, counter values | `MissionPresentationDirector` |

Order, if it is ever built: (1) `FrameProjector`, `FxWorldView` and `FogOfWarGrid` already today read exclusively **in the
tick** – they can run unchanged on the simulation thread and hand over their result (those are the about 55 places from
`SyncAfterTick`); what is needed is only the handover of the `Capture` list, fog states and effect state instead of shared objects.
(2) Supplement events with the looked-up fields (15 places). (3) Convert the HUD to a command view (about 200 places;
the actual effort, and the same that a spectator mode or replay mode would need). (4) Standing orders into the simulation.
The gain would be limited as soon as the goals of Phase 19 are reached (a tick is then small against a frame, and in lockstep
the client has to wait for the tick anyway); a thread of its own helps above all against single long ticks (the maximum in
`docs/PERFORMANCE.md` §6) and when catching up after a rejoin.

## 7. Commands

For `CLAUDE.md` (section "Build, Tests, Audits"):

```powershell
dotnet run -c Release --project tools/CnCNew.SimTool -- perf game/content --map "Maps/Twilight Flame/Twilight Flame.map" --players 8 --ticks 18000 --replay --json <datei.json> --label <commit>
dotnet run -c Release --project tools/CnCNew.SimTool -- perf-mission game/content --map "Maps/MD_USA05/MD_USA05.map" --ticks 3000 --json <datei.json> --label <commit>
dotnet run -c Release --project tools/CnCNew.SimTool -- perf-compare <alt.json> <neu.json> [--threshold-percent N] [--min-us N] [--all] [--allow-different-hash] [--out <datei>]
dotnet run -c Release --project tools/CnCNew.PresentationTool -- sync-perf game/content --map "Maps/Twilight Flame/Twilight Flame.map" --players 8 --ticks 18000 --hud [--digest] [--json <datei.json>] [--label <commit>]
pwsh scripts/perf-profile.ps1 [-Label <commit>] [-OutDir <verzeichnis>] [-Ticks 54000] [-Only 'large-*']
pwsh scripts/perf-compare.ps1 <altesVerzeichnis> <neuesVerzeichnis> [-ThresholdPercent 10] [-AllowDifferentHash]
```

Quiet re-measurement of the game layer: nothing else on the machine, `dotnet build -c Release tools/CnCNew.PresentationTool`, then the
`sync-perf` run above twice (the final hashes and – with `--digest` – the checksums must be the same, the means should scatter by a few
percent) and replace the table in §4. Before a change to `FrameProjector`, `FogOfWarGrid`, `FxExecutor` or
`LiveGame.SyncAfterTick`: `--digest` before and after; a different checksum is a different presentation.
