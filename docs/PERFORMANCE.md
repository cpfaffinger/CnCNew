# Performance profile of the simulation

As of: Phase 18, item 5 (`docs/handoffs/HANDOFF-PHASE18.md`); register `docs/MULTIPLAYER-PLAN.md` §4, E4 and E20.

This document describes the measuring tool (`SimTool perf`, `SimTool perf-mission`, `scripts/perf-profile.ps1`), the
tick phases as they are measured, and how a quiet measurement proceeds. **Nothing is optimised here** – that is
Phase 19. The result table in §6 is empty until the coordinator has run the runs on an unloaded machine;
what was measured incidentally while building the tool is explicitly kept separate in §7.

## 1. Principle: the simulation has no clock

`CnCNew.Simulation` must not know wall time (test `SourceRules`: no `Stopwatch`, no `DateTime`, no threads,
no `float`). The phase times are therefore **not measured in the simulation**. The simulation only reports where a
phase begins and ends; the tool reads the clock.

- `ITickPhaseObserver` (`src/CnCNew.Simulation/World/TickPhase.cs`): `PhaseStarted(TickPhase)`, `PhaseEnded(TickPhase)`
  and – for the breakdown of the update phase – `UpdateStarted(SimModule)`, `UpdateEnded(SimModule)`.
- `SimulationWorld.PhaseObserver` is normally `null`. The property is **not part of the state**: it is neither
  hashed nor written into the snapshot; a restored world has no observer.
- Without an observer `Step()` runs as before: a single null check at the beginning, then the same code as before the
  change. With an observer `StepObserved` runs, the same calls in the same order, only with the reports
  in between. In `RunUpdates` one null check per module update is added. Nothing is allocated.
- `MissionSession.Step` and `SkirmishMatchSession.Step` report their own shares through the same observer of the world
  (scripts or AI before the world step, event follow-up or victory check after it).
- `SimulationWorld.DirtyEntityCount` (read-only) tells the tool how many objects the hash phase is about to
  re-describe.

Secured by `tests/CnCNew.Simulation.Tests/Tests.TickPhases.cs`: a world with an observer delivers the same
tick hashes as without (stress replay without terrain, skirmish with AI and vision, mission with scripts); the observer
sees every phase exactly once per tick in the order named below; module updates always lie within the
update phase; the snapshot with an observer is byte-identical to the one without; the AIs issue the same commands.

## 2. The tick phases as they are measured

The names are those of the code (`TickPhase`, order of `SimulationWorld.Step`). There is **no separate
collision phase**: movement and the delivery of collisions (`DeliverCollisions`) run within the module updates.

| No. | `TickPhase` | Method | Content |
|---:|---|---|---|
| 1 | `Commands` | `ExecuteCommands` | commands of this tick, ordered by player and sequence |
| 2 | `Timers` | `FireTimers` | due timers |
| 3 | `Updates` | `RunUpdates` | sleeping module updates (movement, collision, production, AI of the units …) |
| 4 | `Acquisition` | `StepAcquisition` | target acquisition |
| 5 | `Turrets` | `StepTurrets` | turrets |
| 6 | `FiringTrackers` | `StepFiringTrackers` | firing stages |
| 7 | `Events` | `DeliverEvents` | delivery of the events that have arisen up to here |
| 8 | `Sales` | `ProcessSales` | sales |
| 9 | `Destroys` | `ProcessDestroys` | removal of the destroyed objects |
| 10 | `DelayedDamage` | `ProcessDelayedDamage` | delayed damage |
| 11 | `Spawns` | `ProcessSpawns` | creation of the noted objects |
| 12 | `SpawnEvents` | `DeliverEvents` | events from 9 to 11 |
| 13 | `Vision` | `RevealVision` | fields of view: clear all cells of all players, then one circle per looking object and viewer; only every third tick (`VisionIntervalTicks`), otherwise empty |
| 14 | `VisionHash` | `VisionMap.Hash` | hash over the vision cells; runs exactly when 13 runs |
| 15 | `Hash` | `ComputeIncrementalHash` | advance the tick counter, re-describe touched objects, state hash |

In addition the shares of the sessions (marked with `+` in the output):

| `TickPhase` | Where | Content |
|---|---|---|
| `SkirmishAi` | `SkirmishMatchSession.Step`, before the world step | science point substitute and `SkirmishAi.Update` of all computer players |
| `SkirmishVictory` | after it | victory check every 15 ticks |
| `MissionScripts` | `MissionSession.Step`, before the world step | `ScriptEngine.Update` |
| `MissionEvents` | after it | shot counting and `ScriptEngine.Observe` |

"(unattributed)" in the output is the time between the phases including the clock accesses of the observer itself.

**Breakdown of the update phase by module type.** It manages without intervention in the individual modules: measurement takes place
at the one place where the scheduler calls an update (`RunUpdates`). Per module name the tool shows calls,
sum, mean and maximum. The difference between the sum of the modules and the phase is the work of the scheduler
(heap) plus the clock accesses. Every update costs two clock accesses and one table lookup through the measurement (order of magnitude
50–100 ns); with millions of very short updates this distorts the phase `Updates` upwards. `--no-modules` switches the
breakdown off, `--no-observer` the observer entirely (then only the whole tick is measured).

**Resolution.** The clock is `Stopwatch.GetTimestamp()`; under Windows that is 100 ns. Phases that are shorter appear
as 0.0 or 0.1 µs; their means are to be read only by order of magnitude.

## 3. The commands

```powershell
dotnet build -c Release tools/CnCNew.SimTool
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- perf game/content --map "Maps/Twilight Flame/Twilight Flame.map" --players 8 --ticks 18000 --replay --csv <datei.csv>
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- perf game/content --map "Maps/Alpine Assault/Alpine Assault.map" --factions FactionChina,FactionGLA --ticks 54000 --replay
dotnet run -c Release --no-build --project tools/CnCNew.SimTool -- perf-mission game/content --map "Maps/MD_USA05/MD_USA05.map" --ticks 3000 --csv <datei.csv>
pwsh scripts/perf-profile.ps1 [-Ticks 54000] [-MissionTicks 3000,9000] [-Only 'large-*'] [-OutDir <verzeichnis>] [-TimeoutMinutes 60]
```

Options of `perf`: `--map` (logical map path), `--players N` (the first N playable factions, like `ai-match`)
or `--factions A,B,…`, `--ticks T` (tick budget; a match ends earlier when a winner is determined, plus follow-up),
`--seed N`, `--replay`, `--csv <datei>`, `--top K` (modules, default 15), `--sample N` (window of the CSV, default 300
ticks), `--no-modules`, `--no-observer`. Instead of players, `perf-mission` knows `--difficulty easy|normal|hard`.

Output per run:

1. Machine, runtime, clock resolution; map with cell size; duration of the setup (definitions, model geometry, map).
2. **Time per tick** in µs: mean, median, 95 % and 99 % quantile (nearest rank), maximum; the same values per phase and
   their share of the total time; the mean without the first 300 ticks (JIT); the world step alone.
3. **Longest tick** (overall and after the first 300 ticks): tick, duration, object count, allocation and the phase that
   determines it.
4. **Update phase by module type** (the K most expensive).
5. **Allocations** (`GC.GetAllocatedBytesForCurrentThread` around every session step): mean, median, maximum per tick,
   sum, extrapolated per game minute; number of GC runs of generations 0/1/2 during the run.
6. **Object count**: minimum, maximum, end; in the CSV one row per window (tick, objects, tick time mean/maximum,
   allocation, touched objects per tick, mean per phase; for missions additionally the cost of a complete
   script hash at this point).
7. **Final hash and chain** of the run. They must equal the run without an observer: `ai-match --max-ticks T` or
   `mission-run --ticks T` with the same parameters print the same pair. In addition the comparison of the incrementally kept
   world hash with a full recomputation; a deviation is a state change without `MarkDirty` (finding D8) and
   is reported as `WARNING` without letting the run fail.
8. With `--replay`: the recorded commands on a fresh world **without AI and without observer**: µs per tick,
   ticks per second, **catch-up speed** as a multiple of real time at 30 Hz (ticks per second / 30) and at 60 Hz (/ 60),
   allocation and GC; the final hash of the replay must equal that of the observed run (otherwise exit code 1).
9. **Snapshot** at the end: size in bytes, bytes per object, writing and restoring in ms (in each case the best of
   three passes), hash comparison after restoring.
10. **Extrapolation to 60 Hz**: share of one core (mean tick time × 60) and catch-up factor. Assumption: the same cost per
    tick. That is an upper estimate for everything that works per tick, and a lower one for everything that depends on time (at
    60 Hz twice as many ticks per game minute).
11. **Probes** for the three leads of the handoff (§5).
12. One line `summary: key=value;…` for the script.

Numbers are printed with a point as the decimal separator (invariant culture), independent of the system.

## 4. A quiet measurement

1. Machine: mains operation, power profile "High performance" ("Höchstleistung"), no other agents, no IDE indexing, no build, no
   client. The CPU load before the start should be below 5 %; the script writes it into the table per run
   but does not check it.
2. `pwsh scripts/perf-profile.ps1` from the repo. The script builds `SimTool` in Release, copies the binaries to
   `<OutDir>\bin` (a later build does not disturb the measurement) and runs the list **strictly one after another**. Every run
   has a time limit (`-TimeoutMinutes`, default 60); a run that exceeds it is terminated and carried as `TIMEOUT`.
3. Output under `%TEMP%\cncnew-perf` (or `-OutDir`, outside the repo): per run `<name>.txt` and `<name>.csv`,
   at the end `summary.md` with two tables (overview and tick phases).
4. Run twice and compare the two `summary.md`: the final hashes must be equal, the means should scatter by
   a few percent. If they scatter more, the machine was not quiet.
5. Take the tables over into §6, with date, commit and the hardware line of the output.

The runs of the script:

| Name | Map | Cells | Start positions | Players | Ticks |
|---|---|---|---:|---:|---:|
| `small-2p` | `Maps/Scorched Earth/Scorched Earth.map` | 190 × 190 | 2 | 2 | 54,000 |
| `medium-2p` | `Maps/Tournament Tundra/Tournament Tundra.map` | 268 × 269 | 4 | 2 | 54,000 |
| `medium-4p` | as above | 268 × 269 | 4 | 4 | 54,000 |
| `large-2p` | `Maps/Twilight Flame/Twilight Flame.map` | 486 × 469 | 8 | 2 | 54,000 |
| `large-4p` | as above | 486 × 469 | 8 | 4 | 54,000 |
| `large-8p` | as above | 486 × 469 | 8 | 8 | 54,000 |
| `mission-MD_USA05-3000`, `-9000` | `Maps/MD_USA05/MD_USA05.map` | 527 × 521 | – | 10 sides | 3,000 and 9,000 |
| `mission-MD_GLA05-3000`, `-9000` | `Maps/MD_GLA05/MD_GLA05.map` | 589 × 589 | – | 12 sides | 3,000 and 9,000 |

The maps are chosen by size (the two-player map with the smallest playing area, a medium four-player map,
the eight-player map with the largest area; cells = path cells according to `SimTool perf`); the medium and the large map additionally run with fewer players so that
map size and player count can be separated. All skirmishes run with `--replay`. `-Ticks` and `-Only` shorten the
programme for a trial run. A match that is decided before the tick budget ends earlier; the column "Ticks" of the
table names the ones actually run.

## 5. The three leads of the handoff

The tool can confirm or refute each of the three unverified leads from `docs/handoffs/HANDOFF-PHASE18.md` item 5:

- **(a) The vision hash is recomputed every three ticks over every cell of every player.** Phase `VisionHash` (and
  `Vision` for the clearing and revealing) per tick and per refresh; in addition, at the end, 20 direct calls of
  `VisionMap.Hash()` on the final state, with ns per cell byte. The number of cell bytes is cells × (players + system).
- **(b) `Describe` hashes template, module and tag names character by character.** A counting `IStateWriter` describes all
  objects of the final state: words in total, of which from texts (one word per character plus the length), divided by
  field (`templateName`, `name`, `tag`). In addition the time comparison of the real `HashStateWriter` with a writer that
  mixes only the length of a text: the difference is the characters. In the run: phase `Hash` and the number of objects touched per tick
  (`DirtyEntityCount`).
- **(c) `MissionSession.StateHash()` describes the script state completely every time.** `perf-mission` measures
  `ScriptEngine.StateHash()` 20 times at the end without a state change in between (cost, allocation, words) and once per
  CSV window over the run, in each case outside the tick time. `MissionSession.Step` itself does not call the combined hash
  (it delivers the hash of the world); the number is the surcharge for a caller that wants it per tick.

## 6. Results (quiet measurement)

Measured on 6 October 2026, state `b432fb7`, with `scripts/perf-profile.ps1` (default list, strictly one after another, nothing else on the machine; CPU load before the runs 0–6 %, before the first 15 %). Hardware: AMD Ryzen 7 PRO 6850U (AMD64 Family 25 Model 68), 16 logical processors, 28 GiB, Windows 10.0.26200, .NET 10.0.12, workstation GC. One machine, one run per row – no scatter collected. In all ten runs the incremental hash at the end is equal to the fully computed one.

### 6.1 Overview (times in microseconds per tick)

| Run | Map (cells) | Players | Ticks | Mean | Median | 95 % | 99 % | Maximum | Bytes per tick | GC 0/1/2 | Objects min/max/end | Replay without AI | Catch-up at 60 Hz | Share of one core at 60 Hz | Snapshot (bytes; write/read ms) | Final hash |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---|---:|---:|---:|---|---|
| small-2p | Scorched Earth (190×190) | 2 | 23,656 (decided) | 505.8 | 375.1 | 959.7 | 1,647.8 | 37,349 | 12,629 | 36/1/0 | 8/77/51 | 387.4 | 43.0× | 3.0 % | 294,114; 1.9/5.1 | `A8969552F4CE324B` |
| medium-2p | Tournament Tundra (268×269) | 2 | 21,706 (decided) | 784.0 | 693.4 | 1,682.2 | 2,025.9 | 56,250 | 24,460 | 64/6/0 | 16/152/67 | 668.1 | 24.9× | 4.7 % | 484,356; 2.7/7.0 | `FF4E157157884F29` |
| medium-4p | Tournament Tundra | 4 | 54,000 | 1,866.3 | 1,371.5 | 5,149.9 | 7,371.2 | 86,975 | 34,095 | 221/35/1 | 20/274/46 | 1,795.3 | 9.3× | 11.2 % | 577,560; 1.7/5.4 | `95EC2A8639D7A807` |
| large-2p | Twilight Flame (486×469) | 2 | 54,000 | 797.4 | 489.6 | 1,680.5 | 1,879.5 | 138,596 | 24,065 | 156/8/0 | 16/105/61 | 749.0 | 22.3× | 4.8 % | 1,066,372; 3.3/8.5 | `868959729974E7FD` |
| large-4p | Twilight Flame | 4 | 52,441 (decided) | 2,071.2 | 1,593.4 | 3,943.8 | 5,156.3 | 253,739 | 28,620 | 180/24/1 | 20/200/124 | 2,014.8 | 8.3× | 12.4 % | 1,923,632; 5.6/16.7 | `F34474400B0A780D` |
| large-8p | Twilight Flame | 8 | 54,000 | **4,978.1** | 4,294.5 | 8,358.5 | 13,597.8 | **547,113** | 95,273 | 616/68/2 | 28/337/155 | 4,870.3 | **3.4×** | **29.9 %** | 3,198,580; 8.9/22.7 | `0A69FC72FF6975CD` |
| mission-MD_USA05 | MD_USA05 (527×521), 10 sides | – | 3,000 | 5,944.9 | 4,502.0 | 9,435.2 | 20,081.8 | 114,379 | 208,129 | 74/2/0 | 1,835/1,909/1,869 | – | 2.8× (live) | 35.7 % | 6,549,327; 33.8/134.6 | `1CB28E6AA923BDEF` |
| mission-MD_USA05 | | – | 9,000 | 5,546.7 | 4,190.0 | 8,681.2 | 12,145.5 | 116,693 | 202,246 | 218/5/0 | 1,835/1,909/1,869 | – | 3.0× (live) | 33.3 % | 6,550,224; 33.7/130.6 | `D5AC3B28DC65F103` |
| mission-MD_GLA05 | MD_GLA05 (589×589), 12 sides | – | 3,000 | **13,468.0** | 10,942.7 | 20,047.3 | 37,188.2 | 160,996 | 4,093,904 | 1,435/6/2 | 2,249/2,304/2,249 | – | **1.2× (live)** | **80.8 %** | 9,009,216; 43.1/163.7 | `826CCD82C8536DC2` |
| mission-MD_GLA05 | | – | 9,000 | 12,644.2 | 9,909.4 | 18,759.9 | 35,937.5 | 168,834 | 4,088,597 | 4,302/92/7 | 2,243/2,304/2,247 | – | 1.3× (live) | 75.9 % | 9,022,665; 43.0/119.1 | `5733B70B4C846DB1` |

### 6.2 Tick phases (mean in microseconds per tick; phases below 5 µs omitted)

| Run | Updates | Acquisition | Vision | VisionHash | Hash | AI | Scripts |
|---|---:|---:|---:|---:|---:|---:|---:|
| small-2p | 221.9 | 12.5 | 108.9 | 22.1 | 116.1 | 18.6 | – |
| medium-2p | 370.8 | 15.4 | 162.2 | 41.7 | 171.1 | 17.1 | – |
| medium-4p | 1,167.2 | 21.1 | 284.4 | 67.0 | 297.5 | 24.0 | – |
| large-2p | 293.4 | 9.0 | 215.7 | 128.5 | 134.7 | 12.4 | – |
| large-4p | 1,090.5 | 17.6 | 407.1 | 212.0 | 315.6 | 23.7 | – |
| large-8p | 3,264.2 | 36.9 | 716.0 | 378.5 | 530.8 | 44.7 | – |
| MD_USA05 (3,000) | 2,179.5 | 311.5 | 921.3 | 562.3 | 1,681.2 | – | 278.8 |
| MD_USA05 (9,000) | 1,980.1 | 276.4 | 854.7 | 564.5 | 1,635.0 | – | 231.4 |
| MD_GLA05 (3,000) | 5,261.5 | 540.8 | 1,531.8 | 841.2 | 2,271.3 | – | 2,999.7 |
| MD_GLA05 (9,000) | 4,628.1 | 502.8 | 1,670.3 | 822.4 | 2,164.6 | – | 2,844.0 |

### 6.3 What the measurement says

1. **The cost depends on player count and map, not on the object count.** 8 players with at most 337 objects cost 4.98 ms per tick; the synthetic stress test with over 13,000 objects 61 µs. From 2 to 8 players on the same map the tick rises 6.2-fold.
2. **Two thirds are module updates, and of those four fifths `AIUpdateInterface`** (large-8p: 81.4 % of the update time, 28.8 µs on average per call, up to 52 ms in one call). That is movement including pathfinding. The longest tick of all (547 ms at tick 52,843, 158 objects) consists 100 % of updates: a single tick that costs 16 frames at 30 Hz. In `MD_GLA05`, `PointDefenseLaserUpdate` is added with 30.6 % of the update time.
3. **Vision and hash together cost one third** (large-8p: Vision 716 + VisionHash 379 + Hash 531 = 1,626 µs of 4,978). All three grow with map area and player count, not with what changes.
4. **The three leads of the handoff:** (a) confirmed – `VisionHash` 379 µs on average on Twilight Flame with 8 players, 822–841 µs on `MD_GLA05`; (b) partly – the hash phase costs 135–531 µs in skirmishes and 1.6–2.3 ms in missions, the bigger lever according to the preliminary measurement (§7) is that most objects count as changed every tick; (c) is true but does not act on the tick (nobody calls `MissionSession.StateHash()` per tick).
5. **Scripts:** in `MD_GLA05` 2.8–3.0 ms per tick (22 % of the tick) and 4 MB of allocation per tick with 1,435 gen-0 collections in 3,000 ticks; in `MD_USA05` 0.23–0.28 ms.
6. **Extrapolation to 60 Hz** (mean × 60, without anything changing in the code): skirmish with 8 players **30 % of one core**, catch-up speed **3.4-fold**; `MD_USA05` 33–36 % and roughly 3-fold; `MD_GLA05` **76–81 %** and only **1.2- to 1.3-fold**. The target values for Phase 19 (`docs/handoffs/HANDOFF-PHASE19.md`) are to be measured against this; the quantiles count more than the mean: the 99 % quantile of the 8-player run is at 13.6 ms and thus close to a whole 60 Hz tick (16.7 ms), that of `MD_GLA05` at 36–37 ms above it.
7. **Snapshot:** 3.2 MB for 155 objects on Twilight Flame (the largest part is not the object but map and vision), 9.0 MB for 2,249 objects in `MD_GLA05`; writing 9–43 ms, reading 23–164 ms.

Not measured: scatter between runs, a second machine, server GC, the client (presentation) beside the simulation, the cost of the observer itself (the run without an observer is the column "Replay without AI"; large-8p: 4,870 compared with 4,978 µs with observer and AI).

## 6a. Results after Phase 19 (quiet measurement, 6 October 2026, state `16d337c`)

The same list, the same machine, the same script as §6 (`scripts/perf-profile.ps1`, strictly one after another, CPU load before the runs 0–6 %, before the first 14 %). **All ten final hashes are those from §6** – no result was changed. One run per row, no scatter collected.

### 6a.1 Overview (microseconds per tick; in parentheses the value from §6)

| Run | Mean | Median | 95 % | 99 % | Maximum | Bytes per tick | GC 0/1/2 | Replay without AI | Catch-up at 60 Hz | Share of one core at 60 Hz |
|---|---:|---:|---:|---:|---:|---:|---|---:|---:|---:|
| small-2p | 290.3 (505.8) | 209.1 | 768.3 | 1,408.0 | 43,399 (37,349) | 3,260 (12,629) | 9/2/0 | 131.0 | 127× | 1.7 % |
| medium-2p | 368.5 (784.0) | 268.3 | 869.2 | 1,482.6 | 45,378 (56,250) | 4,059 (24,460) | 11/1/0 | 200.4 | 83× | 2.2 % |
| medium-4p | 543.6 (1,866.3) | 444.0 | 1,261.8 | 2,100.5 | 42,869 (86,975) | 7,625 (34,095) | 49/5/0 | 424.1 | 39× | 3.3 % |
| large-2p | 267.5 (797.4) | 193.6 | 605.3 | 1,343.7 | 43,757 (138,596) | 3,768 (24,065) | 25/2/0 | 191.9 | 87× | 1.6 % |
| large-4p | 595.8 (2,071.2) | 456.3 | 1,066.9 | 1,804.0 | 114,619 (253,739) | 7,231 (28,620) | 45/10/0 | 514.2 | 32× | 3.6 % |
| **large-8p** | **1,141.2 (4,978.1)** | 870.0 | 2,027.0 | **3,202.4 (13,597.8)** | **93,710 (547,113)** | 52,123 (95,273) | 337/39/0 (616/68/2) | 1,029.1 | **16.2× (3.4×)** | **6.8 % (29.9 %)** |
| MD_USA05, 3,000 ticks | 2,463.3 (5,944.9) | 1,840.9 | 4,975.0 | 12,323.2 (20,081.8) | 112,306 (114,379) | 16,470 (208,129) | 6/1/0 | – | 6.8× live (2.8×) | 14.8 % (35.7 %) |
| MD_USA05, 9,000 ticks | 1,990.2 (5,546.7) | 1,509.7 | 3,010.4 | 8,999.6 | 113,753 | 12,588 | 13/2/0 | – | 8.4× live | 11.9 % |
| MD_GLA05, 3,000 ticks | **3,430.3 (13,468.0)** | 2,624.8 | 7,958.2 | 14,611.1 (37,188.2) | 151,803 (160,996) | 10,187 (4,093,904) | 4/2/0 (1,435/6/2) | – | 4.9× live (1.2×) | 20.6 % (80.8 %) |
| MD_GLA05, 9,000 ticks | 2,924.9 (12,644.2) | 2,202.0 | 4,821.4 | 9,654.0 | 119,479 | 11,423 | 12/1/0 (4,302/92/7) | – | 5.7× live | 17.5 % |

### 6a.2 Tick phases (mean in microseconds per tick; in parentheses §6)

| Run | Updates | Acquisition | Vision | VisionHash | Hash | AI | Scripts |
|---|---:|---:|---:|---:|---:|---:|---:|
| large-2p | 82.8 (293.4) | 9.9 | 34.0 (215.7) | 40.3 (128.5) | 83.6 (134.7) | 12.6 | – |
| large-4p | 232.0 (1,090.5) | 15.4 | 54.6 (407.1) | 102.5 (212.0) | 163.2 (315.6) | 22.9 | – |
| large-8p | 472.3 (3,264.2) | 27.5 (36.9) | 85.0 (716.0) | 228.2 (378.5) | 279.1 (530.8) | 42.1 | – |
| MD_USA05 (3,000) | 967.8 (2,179.5) | 25.3 (311.5) | 110.1 (921.3) | 237.5 (562.3) | 956.4 (1,681.2) | – | 153.6 (278.8) |
| MD_GLA05 (3,000) | 1,111.0 (5,261.5) | 45.6 (540.8) | 301.8 (1,531.8) | 497.5 (841.2) | 1,245.1 (2,271.3) | – | 204.3 (2,999.7) |

### 6a.3 Against the target values of the phase (`docs/handoffs/HANDOFF-PHASE19.md`; 8 players, 30 game minutes, large map)

| Quantity | Target | before | after | reached? |
|---|---|---:|---:|---|
| Tick time mean | ≤ 1.0 ms | 4.98 ms | 1.14 ms | **narrowly not** (factor 4.4 gained) |
| 99 % quantile | ≤ 3 ms | 13.60 ms | 3.20 ms | **narrowly not** |
| Maximum | ≤ 8 ms | 547 ms | 94 ms | **no** |
| Catch-up speed | ≥ 8-fold at 60 Hz | 3.4-fold | 16.2-fold | **yes** |
| Gen-2 runs in the match | none | 2 | 0 | **yes** |
| Allocation per tick | close to 0 | 95 KB | 52 KB | **no** |
| Campaign audit per map (900 ticks) | under 1 minute | up to 168 s (under load) | 9–57 s (24 maps, under parallel load) | **yes** |

### 6a.4 What remains and why it does not fall further without a change of result

1. **The maximum (94 ms in the skirmish, 112–152 ms in missions).** In the skirmish these are ticks in which many units search long, successful paths at once (one case with 69 searches and 1.89 million cells); every search delivers its path of today, a cheaper search would choose a different one among equally expensive paths. In the missions it is the first tick (according to the measurement of the mission agent for the most part JIT compilation, not mission work). It only goes further with a change of behaviour in a commit of its own with a rewritten manifest: a shared path per group, a cell budget per tick with a queue, or a different search (`docs/PERF-MOVEMENT.md`).
2. **The state hash** is now the largest single phase (8 players 279 µs, `MD_GLA05` 1,245 µs) ahead of the vision hash (228 and 498 µs respectively). Both are a single chain over all words or all cell bytes; this cannot be done exactly incrementally. Two documented proposals for a different construction, **not implemented, decision of the project owner**: vision hash row by row (prototype 11 to 21 times faster, `docs/PERF-VISION.md` §7.1); object hash with frame key, module hashes and "awake" instead of an absolute wake tick (`docs/PERF-HASH.md` §6–§7). Both change every hash in the manifest.
3. **Allocation:** 52 KB per tick with 8 players (before 95 KB), in the missions 10–16 KB (before 0.2–4 MB). The rest is spread over the AI and the module updates and has not been removed individually.
4. **Linear searches over movable objects** (avoidance, collision, traps, detectors): a spatial index would be result-neutral but needs a hook on every position change in `Entity.cs`.

Details per cost centre with the proof of result equality: `docs/PERF-MOVEMENT.md`, `docs/PERF-VISION.md`, `docs/PERF-HASH.md`, `docs/PERF-MISSIONS.md`, `docs/PERF-GAME-LAYER.md` (game layer, `perf-compare`, what stands in the way of a simulation thread of its own). Comparison of two measurement directories: `pwsh scripts/perf-compare.ps1 <alt> <neu>`.

## 6b. Addendum Phase 22 (6 October 2026): fixed point Q39.24, measured back to back

Phase 22 switched `Fixed` from 16 to 24 fractional bits (`docs/FIXED-POINT-Q39-24.md`). The computing cost was not supposed to rise in the process. Absolute numbers of other days are not comparable on this machine
(the values here lie above those from §6a because measurement was done with the observer and not with `scripts/perf-profile.ps1` and a second agent was computing alongside at times); what was compared is therefore the
`SimTool` binaries of Phase 20 (`4c45764`), of the preparatory work (`a0e7b23`, still 16 bits) and of the switch (`d7fca91`), alternating in the same time window. Mean per tick in microseconds.

| Run | same work? | Phase 20, 16 bits | switch, 24 bits |
|---|---|---|---|
| Stress test, 216,000 ticks, first run (`SimTool stress`), three runs alternating | yes (command stream and events identical) | 57.6 / 57.7 / 58.3 | **57.1 / 56.7 / 56.7** |
| `MD_USA05`, 3,000 ticks, six runs | yes | mean 3,233 (2,582 – 3,899) | mean 3,330 (2,820 – 3,894) |
| `MD_GLA05`, 3,000 ticks, five runs | almost | mean 4,957 (4,537 – 5,865) | mean 5,106 (4,261 – 6,068) |
| 8 players, Twilight Flame, 12,000 ticks, five runs | no (different match from tick 2,773) | mean 1,765 (1,597 – 1,944) | mean 1,772 (1,612 – 1,940) |

- **No measurable increase.** The stress test – the same work, quiet window – is one to two percent faster at 24 bits; the other runs scatter by ± 15 % and their means differ by less.
- That this is so is due to two pieces of the preparatory work: (1) `FixedMath` derives quotient and root for the bit count – at 24 bits every quotient with a dividend above 32,768 units and every
  root above 32,768 (every squared distance above 181 units) would otherwise take the 128-bit software path that Phase 19 had taken out; (2) the vision circle test continues to compute in machine words (defined at 16 fractional
  bits): with exact 128-bit squares the phase "Vision" needed 171 – 187 µs instead of 132 – 135 µs. Preparatory work against Phase 20, both 16 bits, the same final hashes: whole tick 1,666 / 1,647 against
  1,670 / 1,649 µs, Vision 146 / 135 against 147 / 140 µs, `MD_GLA05` 4,547 / 4,601 against 4,583 / 4,569 µs.
- **Snapshot size:** + 0.82 % over all snapshots of the reference manifest in the same tick (118,737,497 → 119,710,407 bytes); stress test at tick 108,000 + 2.0 %, `MD_GLA05` + 0.4 %.
- `reference-manifest check --set full`: 439 s alone with six workers (at the end of the preparatory work at 16 bits: 427 s).
- **Not measured:** `scripts/perf-profile.ps1` with the quiet comparison against the previous run (§4) – the machine could not be got quiet; the tables in §6a are thus the last quietly measured
  absolute numbers and apply to 16 bits.

## 6c. Performance gate of Phase 23 (9 October 2026, measured quietly)

Phase 23 ends with a gate: the game now runs at 60 Hz by default, so the target values of Phase 19 (`docs/handoffs/HANDOFF-PHASE19.md`; 8 players, 30 game minutes, large map) must hold **at
60 Hz**. Measured on the development machine with CPU load 0–11 % before the runs, strictly one after another, with `SimTool perf … --replay` from copied binary directories; "old" is the
starting state of the phase (`20ffce9`), "new" the state before the switch (`1b82e45`, all four rounds of the rate invariance, factory exit and AI corrections; the switch itself is one line
afterwards and changes no computation path). Logs in the scratchpad of the session (`perf-gate`). One to three runs per row, no scatter collected over more. **`catchup` and `core_percent` have been measured at the rate of the run since
`0740ebf`** (before, they were called `catchup30`/`catchup60` and were extrapolations); budgets have been game time since `2b11488` (`--seconds`, `--minutes`).

### 6c.1 The gate: 8 players, Twilight Flame, 30 game minutes

| Quantity | Target | 60 Hz, run 1 | 60 Hz, run 2 | reached? | for comparison: the same state at 30 Hz |
|---|---|---:|---:|---|---:|
| Ticks | – | 108,000 | 108,000 | – | 54,000 |
| Tick time mean | ≤ 1.0 ms | 683.2 µs | 694.0 µs | **yes** | 820.7 µs |
| 95 % quantile | – | 1,296.8 µs | 1,311.7 µs | – | 1,602.6 µs |
| 99 % quantile | ≤ 3 ms | 1,614.9 µs | 1,793.3 µs | **yes** | 2,466.1 µs |
| Maximum | ≤ 8 ms | **141,365 µs** | **142,897 µs** | **no** | 46,499 µs |
| Catch-up speed (replay without AI) | ≥ 8-fold at 60 Hz | 26.7-fold | 26.5-fold | **yes** | 45.9-fold (at 30 Hz) |
| Share of one core in real time | – | 4.1 % | 4.2 % | – | 2.5 % |
| Gen-2 runs (GC 0/1/2) | none | 103/16/0 | 103/15/0 | **yes** | 112/20/0 |
| Allocation per tick | close to 0 | 7,978 bytes | 7,977 bytes | **no** | 17,357 bytes |
| Final hash, incremental equal to full | – | `CD9DC2A2A6699DAD`, yes | `CD9DC2A2A6699DAD`, yes | – | `D27D776C3FCA6413` |

- **Missed: the maximum.** In both runs it is the same tick (step 95,550, 202 objects), 100 % in the phase `Updates`: a tick in which many units search long paths at once – the
  finding from §6a.4. At 30 Hz the maximum of the same state was the first tick (46 ms, half of it `Spawns`); after the first 300 ticks it is at 40.3 ms there (step 16,034, also `Updates`).
  That the value at 60 Hz is higher than the 94 ms from §6a has not been broken down (different match after the AI corrections, double tick count). It can only be lowered with a
  change of behaviour (shared group path, cell budget per tick with a queue or a different search); the project owner has reserved this decision for himself – **not done**.
- **Missed: the allocation.** Roughly 8 KB per tick (at 30 Hz 17 KB per tick, i.e. about the same amount per game second): 3,693 bytes in the AI, 3,568 in the module updates, 273 in the creation, 265
  in the target acquisition. No gen-2 runs.
- **Reached: mean, 99 % and catch-up speed.** The tick costs less on average at 60 Hz than at 30 Hz – presumably because above 30 Hz only the first tick of a frame decides and the others cover a
  fraction (not broken down by phases); per game second it is 41 ms of computing time at 60 Hz against 25 ms at 30 Hz (4.1 % against 2.5 % of one core).

### 6c.2 Old against new at 30 Hz, alternating

The rate invariance has put comparisons and fields into every hot path that never do anything at 30 Hz. What that costs is shown only by the same work:

| Run | same work? | old (`20ffce9`), µs per tick | new (`1b82e45`), µs per tick |
|---|---|---|---|
| Stress test, 216,000 steps, first run (`SimTool stress`), three runs alternating | **yes** (914,394 commands, final hash `56882F763EABEF67` in all six) | 48.7 / 49.6 / 48.8 | 52.6 / 50.1 / 51.8 (**+5 %**) |
| 8 players, Twilight Flame, 12,000 ticks, two runs | no (different match: the factory exit and the AI corrections change 30 Hz; final hash `F78F071AF5EC8A6E` against `AB24F9CE16D5DE30`) | 1,105.5 / 1,103.1; 99 % 3,981 / 4,165; maximum 45.8 / 46.4 ms | 1,040.2 / 1,055.5; 99 % 3,998 / 4,137; maximum 46.2 / 46.6 ms |
| `MD_GLA05`, 3,000 ticks | no (different final hash) | 3,019.2; 99 % 13,177 | 3,064.3; 99 % 13,952 |
| `MD_USA05`, 3,000 ticks | no (different final hash) | 2,193.9; 99 % 9,863 | 2,191.5; 99 % 9,673 |

Only the first row is robust: **with the same work a tick costs roughly 5 % more at 30 Hz** (three runs per side, the ranges do not overlap). The three other rows are different
matches or mission courses and only show that nothing falls out of line (−6 % to +1.5 % on average). The absolute values are in the order of magnitude of §6a (there 1,141 µs over 54,000 ticks);
the direct switch remains the only admissible comparison.

### 6c.3 Missions at 60 Hz

| Run | Ticks | Mean | 95 % | 99 % | Maximum | Bytes per tick | GC 0/1/2 | Catch-up live | Share of one core |
|---|---:|---:|---:|---:|---:|---:|---|---:|---:|
| `MD_GLA05`, 100 s, 60 Hz | 6,000 | 2,384.1 | 3,962.8 | 10,155.6 | 109,166 | 5,676 | 4/2/0 | 7.0× | 14.3 % |
| `MD_GLA05`, 300 s, 60 Hz | 17,097 (the run ends before the budget) | 2,138.6 | 3,397.1 | 5,098.8 | 128,023 | 7,374 | 16/3/0 | 7.8× | 12.8 % |
| `MD_USA05`, 100 s, 60 Hz | 6,000 | 1,667.7 | 2,589.1 | 7,728.6 | 101,452 | 8,695 | 6/1/0 | 10.0× | 10.0 % |
| `MD_USA05`, 300 s, 60 Hz | 18,000 | 1,486.0 | 2,278.6 | 3,000.0 | 101,458 | 6,625 | 14/2/0 | 11.2× | 8.9 % |
| `MD_GLA05`, 300 s, 30 Hz | 9,000 | 2,560.9 | 3,874.9 | 9,067.2 | 119,318 | 13,443 | 14/2/0 | – | 7.7 % |
| `MD_USA05`, 300 s, 30 Hz | 9,000 | 1,817.1 | 2,667.8 | 8,126.7 | 112,167 | 12,651 | 14/2/0 | – | 5.5 % |

The large mission `MD_GLA05` costs 13–14 % of one core at 60 Hz and catches up live only 7- to 8-fold (no target value of the handoff applies to missions; for a network game the catch-up speed would be
tight – missions stay local). The maximum of all mission runs is at 101–128 ms; it has not been examined whether it is still the first tick.

### 6c.4 What the gate says and what it does not

- **Passed with two named exceptions:** mean, 99 % quantile, catch-up speed and freedom from gen-2 runs hold at 60 Hz; maximum and allocation do not. The switch to 60 Hz has nevertheless taken place
  (decision of the project owner of 8 October 2026 to reach the goal of the phase); the maximum remains with him as an open decision (`docs/MULTIPLAYER-PLAN.md` §7).
- **Not measured:** `scripts/perf-profile.ps1` as a whole quiet run with comparison against the previous one (after its conversion to game time the script has only been checked for syntax); the remaining nine
  rows from §6a.1 at 60 Hz; the game layer (`sync-perf`) at 60 Hz; the original AI (according to `docs/SKIRMISH-AI-ORIGINAL.md` §7.3 it costs 1,123 µs per tick with 8 players for scripts and
  AI alone, roughly 27 times the built-in one); 120 Hz; the scatter over more than two runs; a second machine.
- The 8-player runs of the gate were measured at the state `1b82e45` at 60 Hz (`tick_rate=60` in the summary), not with the binaries after the switch; the difference is the one line `ProductRate`.

## 7. Preliminary, not measured quietly

Everything in this section dates from 6 October 2026, state `b6b7454` plus tool, **from a machine on which
other agents were building and testing at the same time** (CPU load before the runs 60–96 %). The numbers show that the
tool works and where the time goes by order of magnitude; they are **not results** and do not belong in §6.
Machine: AMD64 Family 25 Model 68, 16 logical processors, Windows 10.0.26200, .NET 10.0.12, workstation GC.

### 7.1 Hashes with and without observer

| Run | without observer | with observer (`perf`) |
|---|---|---|
| `reference-match` (new state, no observer) | FactionGLA at tick 14,490, `23B4B25DF62765D8`, chain `DE5849AF9F8493C4` – as in `docs/handoffs/HANDOFF-PHASE18.md` | – |
| `stress` / `hash` of the stress replay (old state `e5046b7` and new state, 13 runs each) | `56882F763EABEF67`, chain `B8E663493F2C73D5` | – |
| Scorched Earth, 2 players, 18,000 ticks | `ai-match` old and new state: `7C4FC065EF2E3022`, chain `D3754175A10F3630` | `7C4FC065EF2E3022`, chain `D3754175A10F3630` |
| Twilight Flame, 8 players, 18,000 ticks | handoff (5 October): `72CF0F846E0D9AA6`, chain `5E446FE63C5BEF46`; `perf --no-observer`: the same | `72CF0F846E0D9AA6`, chain `5E446FE63C5BEF46` |
| `MD_USA05`, 3,000 ticks | handoff: `1CB28E6AA923BDEF`, chain `0816C3438CB7F689` | `1CB28E6AA923BDEF`, chain `0816C3438CB7F689` |
| `MD_GLA05`, 3,000 ticks | handoff: `317B464E8BA98377`, chain `6823506872EBFF9E` | `317B464E8BA98377`, chain `6823506872EBFF9E` |

`tests/CnCNew.Simulation.Tests` (Debug and Release with `--long`) are green, among them `SourceRules` and the two new
observer tests.

### 7.2 Stress test without observer, old against new state

Ten alternating runs `SimTool hash` of the recorded stress replay (216,000 ticks, whole process run time),
under load: old state minimum 29.7 s, median 35.4 s; new state minimum 29.1 s, median 34.7 s. Three alternating
runs `SimTool stress` ("Run 1", µs per tick): old 91.8 / 103.9 / 77.3, new 153.8 / 89.8 / 91.4. The scatter between
two runs of the same state is larger than any difference between the states; a deterioration is not
discernible, **but the quiet value (61 µs per tick according to the handoff) has not been re-measured here** – that belongs to the quiet
measurement.

### 7.3 Two skirmishes and two missions (not quiet)

| Run | Cells | Ticks | Mean µs | Median | 95 % | 99 % | Max | World alone | AI / scripts | Bytes per tick | GC 0/1/2 | Objects min/max/end | Replay µs | Catch-up 30 / 60 Hz | Core at 60 Hz |
|---|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|---|---:|---|---:|
| Scorched Earth, 2 players | 190 × 190 | 18,000 | 824.9 | 600.6 | 1,675.9 | 2,920.2 | 71,252 | 780.9 | 42.5 | 11,782 | 26/3/0 | 8/61/57 | 601.4 | 55.4× / 27.7× | 4.9 % |
| Twilight Flame, 8 players | 486 × 469 | 18,000 | 8,245.7 | 6,698.7 | 15,638.3 | 28,521.5 | 733,460 | 8,087.6 | 155.1 | 81,898 | 178/28/1 | 28/268/234 | 5,456.5 | 6.1× / 3.1× | 49.5 % |
| `MD_USA05` | 527 × 521 | 3,000 | 9,050.3 | 7,184.9 | 16,810.5 | 27,324.0 | 268,823 | 8,411.4 | 631.6 | 208,549 | 74/3/0 | 1,835/1,909/1,869 | – | – | 54.3 % |
| `MD_GLA05` | 589 × 589 | 3,000 | 15,647.2 | 12,714.4 | 25,541.5 | 48,480.4 | 190,279 | 12,071.2 | 3,570.0 | 4,093,461 | 1,435/8/2 | 2,249/2,304/2,249 | – | – | 93.9 % |

The same eight-player run with `--no-observer` later ran at lower load with 5,090 µs on average; the difference
to 8,246 µs is machine load, not the observer (the replay of the same run without an observer needed 5,457 µs).
`MD_GLA05` ran half an hour later under higher load with 30,287 µs on average. That is how large the scatter is here.

Phases on average per tick (µs) and share of the tick time:

| Phase | Scorched Earth 2 | Twilight Flame 8 | `MD_USA05` | `MD_GLA05` |
|---|---:|---:|---:|---:|
| `Updates` | 354.6 (43.0 %) | 5,226.9 (63.4 %) | 3,328.4 (36.8 %) | 5,997.4 (38.3 %) |
| `Acquisition` | 19.9 (2.4 %) | 105.8 (1.3 %) | 536.7 (5.9 %) | 629.1 (4.0 %) |
| `Vision` | 189.1 (22.9 %) | 1,123.8 (13.6 %) | 1,333.0 (14.7 %) | 1,818.2 (11.6 %) |
| `VisionHash` | 42.5 (5.2 %) | 674.1 (8.2 %) | 841.7 (9.3 %) | 960.1 (6.1 %) |
| `Hash` | 164.2 (19.9 %) | 935.0 (11.3 %) | 2,354.8 (26.0 %) | 2,646.3 (16.9 %) |
| `SkirmishAi` / `MissionScripts` | 42.5 (5.2 %) | 155.1 (1.9 %) | 631.6 (7.0 %) | 3,570.0 (22.8 %) |
| all others together | under 2 % | under 1 % | under 1 % | under 1 % |

Update phase by module type, share of the phase: Twilight Flame `AIUpdateInterface` 78.1 % (1.39 million calls, mean
52.7 µs, maximum 88 ms), `SupplyTruckAIUpdate` 5.0 %, `DozerAIUpdate` 3.5 %, `TransportAIUpdate` 3.4 %; Scorched Earth
`AIUpdateInterface` 66.6 %; `MD_USA05` `AIUpdateInterface` 24.8 %, `DemoTrapUpdate` 22.6 %, `WorkerAIUpdate` 16.9 %,
`StealthUpdate` 3.86 million calls at 0.05 µs each; `MD_GLA05` `AIUpdateInterface` 46.4 %, `PointDefenseLaserUpdate` 28.3 %.
The longest tick of the eight-player run (tick 12,683) took 733 ms and lay 100 % in `Updates` – a single
outlier of this size is exactly what E20 means. In the two-player run the longest tick after the start-up (70 ms,
tick 929) lay 92 % in `SkirmishAi`.

Snapshot at the end: Scorched Earth 304,301 bytes for 57 objects (2.8 ms writing, 8.1 ms reading); Twilight Flame
3,452,779 bytes for 234 objects (12.0 / 33.4 ms); `MD_USA05` 6,549,327 bytes for 1,869 objects (49.4 / 171.8 ms);
`MD_GLA05` 9,009,216 bytes for 2,249 objects (50.1 / 178.6 ms). The size depends above all on the map (the vision cells are in the snapshot per player as
Base64), less on the object count.

### 7.4 The three leads (not quiet)

- **(a) confirmed.** `VisionMap.Hash()` runs on every refresh (every third tick) over every cell byte of every
  player including the system player: Twilight Flame 486 × 469 × 9 = 2,051,406 bytes, 1.2 ms per call alone
  (0.6 ns per byte), in the run 2.0 ms per refresh = 8.2 % of the tick time; `MD_GLA05` 4,509,973 bytes, 2.8 ms per call,
  6.1 %. The cost grows with map area × player count, not with the object count. Larger than the hash is the
  revealing before it (`Vision`, 11.6 to 22.9 %), which likewise first touches every cell of every player.
- **(b) partly confirmed.** Roughly three quarters of all words that an object description gives into the hash are
  text characters (Twilight Flame 138,755 of 184,273; predominantly module name and module tag, hardly the template name). In time
  it is, depending on the run, 10 to 50 % of the description (poorly reproducible under load), i.e. roughly 2.5 to 8.4 % of the
  tick time. The bigger lever of the hash phase is a different one: **per tick 70 to 89 % of all objects are marked as touched**
  and re-described (Twilight Flame 173.7 of 195 on average; `MD_GLA05` 1,737 of 2,249), because every
  module update marks its object – including `StealthUpdate` with 0.05 µs of work.
- **(c) confirmed, but today without effect on the tick.** `ScriptEngine.StateHash()` re-describes the whole
  script state on every call (no incremental path, 13 to 21 KB of allocation per call): `MD_USA05` 17,344
  words, 146 µs; `MD_GLA05` 28,605 words, 187 µs (over the run between 0.4 and 19 ms under load). `MissionSession.Step`
  does not call `MissionSession.StateHash()`; a lockstep comparison per tick would cost 1 to 2 % of the mean tick
  time.

### 7.5 Side finding: incremental hash not equal to the full hash in `MD_GLA05` (D8)

At the end of every run the tool compares the incrementally kept hash of the world with a full recomputation.
In `MD_GLA05` after 3,000 ticks they deviate (incremental `406F4DD61EC777F3`, full `0EB5EC8A435DBDD8`); after 300
ticks and in the other runs they are equal. This is a case of finding D8 from `docs/handoffs/HANDOFF-PHASE18.md`: a
state change without `MarkDirty`. Consequence: the documented final hash `317B464E8BA98377` of this run is the hash of a
stale intermediate state, and a saved game at this point restores a world with a different hash.
The run with and without an observer is equally affected by this (the same final hash); the cause has not been searched for and fixed
here.

## 8. Limits

- One thread is measured; `GC.GetAllocatedBytesForCurrentThread` counts only its allocations. The simulation
  uses no further threads.
- GC pauses are contained in the tick times of the tick into which they fall (that is intended for E20) but are not shown
  individually; an outlier in the 99 % quantile with an inconspicuous phase split is an indication of this.
- The first ticks pay for the JIT; therefore the mean and the longest tick exist additionally without the first 300.
- The replay measures the whole loop of `ReplayRunner.Run` (enqueueing commands and `Step`), not individual ticks.
- The phase `Vision` contains clearing and revealing together; a finer division would need a further hook.
- A skirmish run with `--no-observer` has no phase split and no AI share.
- The tool measures the core, not the client: presentation, sound and input are not included.
