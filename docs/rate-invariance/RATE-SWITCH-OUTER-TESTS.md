# Tick rate switch of the outer test programs

As of: 8 October 2026. Sections 1 to 6 are measured on the base commit `0740ebf` (plus the commits of that package that only change test code)
and describe the switch and the state at that time. **The current state before the change of the default rate to 60 Hz is in section 7**
(base `4d9d9e5`): there all test programs are re-measured at 30, 60 and 120 Hz, every failure is classified, and the golden files are
assessed individually. Where section 7 contradicts sections 2 to 6, section 7 applies.

`tests/CnCNew.Game.Tests`, `tests/CnCNew.Presentation.Tests` and `tests/CnCNew.Ui.Tests` now have the same switch as the simulation tests
(`docs/TICKRATE-MIGRATION.md` §14.9.2): the default test worlds, catalogues and `GameData` loads can be built with 60 or 120 Hz. The run
is a **probe for Phase 23**, not an acceptance test: it says which expectations break when `GameData.DefaultSessionRate` is set to 60 Hz.
It does not say that anything behaves, looks or sounds right at 60 Hz – many tests check bounds that a different behaviour also
fulfils, and no human has seen any of this.

Only test code is changed. Nothing under `src/` or `tools/`, no file under `baselines/` and `tests/**/baselines`.

## 1. Invocation

```powershell
dotnet run --project tests/CnCNew.Ui.Tests -- --tick-rate 60 [--content-dir game/content]
dotnet run --project tests/CnCNew.Presentation.Tests -- --tick-rate 60 [--content-dir game/content]
dotnet build -c Release tests/CnCNew.Game.Tests
dotnet run -c Release --no-build --project tests/CnCNew.Game.Tests -- --content-dir game/content --tick-rate 60 [--only <Suite>[,<Suite>]]
$env:CNCNEW_TEST_TICK_RATE = '60'   # the same via the environment; the option takes precedence
```

Allowed are 30, 60 and 120; everything else aborts with a message. Without the option nothing changes: 30 Hz, no additional output line.
With it the program reports the rate in the first line ("Default test worlds run at 60 Hz (--tick-rate/CNCNEW_TEST_TICK_RATE): a probe, the reference
is 30 Hz.").

Run time: `Game.Tests` (Release, with content) needs about double the 30 Hz time at 60 Hz and about four times at 120 Hz, because the
tests run in game time (on the loaded machine of this measurement over an hour at 120 Hz; the times are not recorded individually). Whoever starts several rates
side by side copies `bin/Release/net10.0` per run into a sibling directory **of the same depth** (`bin/<name>/net10.0`): the HUD baseline
is searched for relative to the program directory, and a run from a different depth writes a new file instead of comparing.

## 2. How the switch is built

- `tests/Shared/TestRateOption.cs`: reads and checks option and environment variable (from the process itself, because static fixtures can be created before the first statement
  of the program), `Announce()` writes the first line. The file does not know the simulation, so that `Ui.Tests` can include it.
- `tests/Shared/TestTime.cs`, class `TestRate`: `Default` (the `SimulationRate`), `Time` (time service for fixtures without a world), `IsOriginal`, `TicksPerFrame`,
  `Frames(n)` (original frames in ticks of the default rate, where no world is at hand). The helpers for expectations are the existing
  `TestTime.Ticks(world, seconds)`, `Frames(world, n)`, `TickSeconds(world)`, `FrameSeconds`.
- `Presentation.Tests`: 36 places (`SyntheticFixture`, `WorldConfiguration`, `TemplateCatalog.Build`, the replay header of the input tests, the FX and
  audio-cue fixtures without a world) take `TestRate.Default` or `TestRate.Time` respectively.
- `Game.Tests`: 14 `GameData.Load(…, rate: TestRate.Default)`, 5 fixtures/catalogues, 3 audio-cue filters.
- `Ui.Tests` builds no world and no catalogue (the project does not reference the simulation; windows, transitions and texts run in original frames of
  wall time). The option is accepted, checked and reported and selects nothing there.

**What still names its rate itself:**

| Test | Rate | Reason |
|---|---|---|
| `TimeScaleTests` (`Game.Tests`), `ObjectTests.TimeScale.cs`, `FxTests.TimeScale.cs`, `UnitConditionTests.TimeScale.cs`, one test each in `JetOrderTests`, `CaptureFlashTests`, `AudioDirectorTests` | 30, 60 and 120 Hz | time-scale tests: they compare the rates with each other |
| `CampaignTests` "Savegame: the header carries the tick rate…" | 30 and 60 Hz | rejection of a foreign rate |
| `ShellTests` "Menu shell walks every screen and draws cleanly" (54 hashes in `baselines/screens.txt`) | until `c29338a` 30 Hz via `GoldenRate.Data`, since then the rate of the suite | the hashes are the same at 30, 60 and 120 Hz (§7.6) |
| `HudTests` "Golden draw list hash" (`baselines/hud-drawlist-1280x720.sha256`) | as above | as above |

`GoldenRate` (`tests/CnCNew.Game.Tests/GoldenRate.cs`) additionally loaded 30 Hz data once at a different rate; the file is dropped with `c29338a`.
Not pinned, because the hashed content depends on no world: the draw-list comparison `DrawListGolden` in `Ui.Tests`, the golden hash of the
bone table (`DeterminismTests` D1), the triangle count in `RoadRealDataTests`.

**Observation before the pinning** (the same state, without `GoldenRate`): the HUD hash is the same at 60 and 120 Hz as at 30 Hz. The 54 screen hashes
are too, as soon as the short match behind the score screen lasts twenty seconds of game time instead of 600 ticks (with 600 ticks only
`ScoreScreen@…` deviated, at 60 and 120 Hz with the same hash). The pinning to 30 Hz is thus caution, not a measured need.

## 3. Numbers per program and rate

Counted are the test lines (`PASS`/`FAIL` or `ok`/`FAIL` respectively). "before" is the state with the switch but without converted expectations; "after" the final state.

| Program | Rate | before passed / failed | after passed / failed |
|---|---|---|---|
| `Ui.Tests` (five suites, without individual lines) | 30, 60, 120 Hz, without and with `--content-dir` | all passed | all passed |
| `Presentation.Tests` without `--content-dir` | 30 Hz | 145 / 0 | 145 / 0 |
| | 60 Hz | 136 / 9 | 143 / 2 |
| | 120 Hz | 131 / 14 | 142 / 3 |
| `Presentation.Tests` with `--content-dir` | 30 Hz | 162 / 0 | 162 / 0 |
| | 60 Hz | 152 / 10 | 160 / 2 |
| | 120 Hz | 144 / 18 | 159 / 3 |
| `Game.Tests` Release with `--content-dir` | 30 Hz | 137 / 0 | 137 / 0 |
| | 60 Hz | 122 / 15 | 136 / 1 |
| | 120 Hz | 120 / 17 | 136 / 1 |

On the `Game.Tests` numbers: "before" is measured without `GoldenRate` (with it the score-screen hash drops out of both lists: 123 / 14 and 121 / 16,
derived, not measured). "after" at 60 and 120 Hz is a full run with one failure still open in `MissionEffectsTests` (135 / 2), plus
the run `--only MissionEffectsTests` after its conversion (13 / 0 at both rates); a full run of the final state is repeated only at 30 Hz.
Without `--content-dir` `Game.Tests` skips most suites; these runs are not counted separately.

## 4. Converted expectations

Rule as in `docs/TICKRATE-MIGRATION.md` §14.5: a number that means game time goes through a helper and is the same number at 30 Hz; a number that
really means ticks or steps stays and carries a short reason.

| File | What is converted | What deliberately stays in ticks |
|---|---|---|
| `Presentation.Tests/ObjectTests.cs` | state sequence after a shot (`FIRING_A` one original frame, reloading); turret: step per tick from the rate, quarter turn, holding, turning back in frames | "the step of the shot" is one step |
| `Presentation.Tests/FxTests.cs` | lock window of the damage effects (`ThrottleTime` 1000 ms + 1 step) | tick 100 as a starting value |
| `Presentation.Tests/InputTests.cs` | build time of the power plant (2 s), budgets of pursuit and attack march, rate in the replay header from the world | sequence numbers, "one step later" |
| `Presentation.Tests/UnitConditionTests.cs` | expiry time of the firing stages (1000 ms), all run budgets from 30 steps, window of `TURRET_ROTATE` | settling loops of 5 steps |
| `Presentation.Tests/StructureConditionTests.cs` | door times of the weapons factory (98/90/120/45 frames), sampling of the particle cannon and of the hacker in frames | loops of 3 and 6 steps |
| `Presentation.Tests/CaptureFlashTests.cs` | event ticks of the restart test are ticks to the original frame n | `TintAt`/`Prune` take original frames (no ticks) |
| `Presentation.Tests/JetOrderTests.cs` | "order ends with the last missile" within 3 frames | – |
| `Game.Tests/MissionTextTests.cs` | one step of the file is one original frame (all counts are frame counts of the original texts); message line stamped with `FrameClock` | two steps "the tick in which the script ran" (`StepTick`) |
| `Game.Tests/MissionEffectsTests.cs` | screen filter sampled per original frame (`StepFrame`); camera move with frozen time in frames | "world stood still": at most two ticks |
| `Game.Tests/MissionHudTests.cs` | duration of the opening sequence (20 to 40 s) | – |
| `Game.Tests/HudTests.cs` | lock time of the under-attack line (events 200/400/700 frames later) | – |
| `Game.Tests/AudioDirectorTests.cs` | ticks of the mock world in the Eva test are original frames | – |
| `Game.Tests/ShellTests.cs`, `ShellTransitionTests.cs` | match behind the score screen: 20 s of game time | – |
| `Game.Tests/ModelGeometryTests.cs`, `DeterminismTests.cs` (D4) | expected world fingerprint contains the rate (`Rate.Combine`), as `GameData.Load` forms it | run budgets of the determinism runs (900 and 45,000 ticks: steps, not time) |

Not converted are budgets that were sufficient at all three rates (except in `UnitConditionTests.cs`, there all are converted). They can become too tight after a
change of the simulation; that then shows as a failure at 60 or 120 Hz, not at 30 Hz.

## 5. Remaining failures (state `0740ebf`; at the state of section 7 each of these four tests passes at all three rates)

Classes: **expectation** (the test names a tick count that means game time – would have to be converted), **deliberately 30 Hz** (names its rate itself, §2; does not
fail), **real rate dependency** (the code under `src/` behaves differently in game time – not fixed and not hidden).

| Test | Program, rate | Class | Values 30 / 60 / 120 Hz | Presumed cause |
|---|---|---|---|---|
| `CaptureFlash` "every flash tints the building in the capturer's colour, decays to none and restarts; nothing else is tinted" | `Presentation.Tests`, 60 and 120 Hz | real rate dependency (simulation, known: `docs/TICKRATE-MIGRATION.md` §5.1, §14.4) | flashes per preparation of 2000 ms: 4 / 9 / 19; first flash until the end of the last tint 1.0333 / 1.4167 / 1.6583 s. Message at 60 Hz: "a preparation of 60 ticks flashes four times (70,86,98,108,117,125,132,139,146)" | `src/CnCNew.Simulation/Modules/BuiltIn/SpecialAbilityStaging.cs:369` (`StepCaptureFlash`, call line 359): the flash phase is advanced per tick |
| `CaptureFlash` "without selection flash settings no tint is projected" | as above | the same | the same | the same; the test aborts at the same first check |
| `Turrets are the simulation's: they turn toward the victim of the fire order at their turn rate, the shot waits, they hold and recentre` (`ObjectTests`) | `Presentation.Tests`, only 120 Hz | real rate dependency (simulation, small) | fire commands that wait for the turret: 15 / 30 / 59 (0.5000 / 0.5000 / 0.4917 s); at the shot the turret no longer turns at 30 and 60 Hz, at 120 Hz it still does (`TURRET_ROTATE` set). Message: "… (shot at tick 61, rotating True, 59 orders waited)" | `src/CnCNew.Simulation/World/SimulationWorld.Turrets.cs:319` (`IsAligned`, threshold `TurretAlignThreshold` 0.035 rad ≈ 2°, line 19) against `TurnAngle` (line 565): the turret only snaps when the remainder is smaller than one step. At 180°/s the step is 6° / 3° / 1.5°; only at 120 Hz does it lie below the threshold, and the shot falls one tick before the snapping |
| `MissionTextTests` "Named timers and counters stack upward from their position, right-aligned in the right half, ready font and flash at 0:00" | `Game.Tests`, 60 and 120 Hz | real rate dependency (script VM and display; to be decided) | a counter set to 3000 with `SET_COUNTER`, displayed as a countdown: "1:40" / "0:50" / "0:25" | `src/CnCNew.Simulation/Scripting/ScriptEngine.Actions.cs:54` (`SET_COUNTER` stores the raw value; only `SET_TIMER` converts frames into ticks, line 63 ff.) together with `src/CnCNew.Game/Shell/MissionPresentation.cs:586` (`CounterForDisplay` reads a value displayed as a countdown as ticks of the world). Whether a shipped map sets a countdown this way is not checked; real timers (`SET_TIMER`, `DISPLAY_COUNTDOWN_TIMER`) are equal at all rates (`TimeScaleTests`) |

Consequences for the reading:

- The two `CaptureFlash` tests check, behind the flash count, also the envelope per tick age (`[1, 1, 0.75, 0.5, 0.25, 0, 0]`). This part is written for 30 Hz steps
  and is not reached at 60 and 120 Hz; it is **not checked** there and would have to be converted to original frames after the repair of the flash sequence
  (the envelope itself is verified at all rates by the time-scale test of the same file).
- The counter test checks, behind the text, also positions and blink frames (16 and 31, already converted to `FrameClock`); this part, too, is not reached at 60 and 120 Hz.
- A failure of the class "expectation" is no longer open at the final state.

## 6. Not done, not verified

- `Game.Tests` without `--content-dir` and the Debug build have not run at 60 and 120 Hz.
- The numbers are measured on one machine and under load; run times are not performance figures.
- `--determinism-full` (`DeterminismTests`) has run at no rate.
- The golden tests are pinned to 30 Hz; that they also deliver the same hashes at 60 and 120 Hz has been observed once (§2), but is no longer part of the suite.
- No picture, no sound and no gameplay at 60 or 120 Hz has been checked by a human.

## 7. Before the change to 60 Hz: all test programs at 30, 60 and 120 Hz

As of 8 October 2026, base `4d9d9e5` (the simulation is there, according to the rate-equivalence suite, the same game at 30, 60 and 120 Hz), final state the branch of this
package. The default rate is **not** changed (`GameData.DefaultSessionRate` stays 30 Hz; the change is a commit of its own by the project owner). Measured with
`--tick-rate 30|60|120`, Release, real data `game/content`. "Frame" means a logic frame of the original (1/30 s), "tick" a step of the simulation.
**No human has seen or heard any of this.**

Classes: **T** – the expectation or the step measure of the test assumes "one tick is one frame"; **P** – code of the presentation, of the game layer or of a tool assumes
it; **S** – the simulation itself differs in game time between the rates.

### 7.1 Numbers per program and rate

Counted are test lines (passed / failed). "before" is `4d9d9e5` (or `502e75b` for the rows that are measured only there), "after" the final state.

| Program | Rate | before | after |
|---|---|---|---|
| `Simulation.Tests` without `--content-dir` | 30 / 60 / 120 Hz | all green (508 counted at the final state) | 508 / 0 at every rate |
| `Simulation.Tests` with `--content-dir` | 30 Hz | all green | 537 / 0 |
| | 60 Hz | 2 failed (Crusader turret, original AI) | **537 / 0** |
| | 120 Hz | 2 failed (the same) | **536 / 1** (reference match without a winner, §7.4) |
| `Presentation.Tests` without `--content-dir` | 30 / 60 / 120 Hz | not measured separately | 155 / 0 at every rate |
| `Presentation.Tests` with `--content-dir` | 30 Hz | 172 / 0 | 172 / 0 |
| | 60 Hz | 167 / 5 | **172 / 0** |
| | 120 Hz | 156 / 16 | **172 / 0** |
| `Game.Tests` Release with `--content-dir` | 30 Hz | all green | 162 / 0 |
| | 60 Hz | 3 failed | **162 / 0** |
| | 120 Hz | 5 failed | **162 / 0** |
| `Ui.Tests` (five suites, without individual lines) | 30 / 60 / 120 Hz, without and with `--content-dir` | all passed | all passed |

The rate-equivalence suite (ten groups in `Simulation.Tests --content-dir`, per run at 30, 60 and 120 Hz, whatever the `--tick-rate`) reports at the final state in every group
**0 quantities outside the tolerance at 60 Hz and 0 at 120 Hz** (`locomotion` 837, `flight` 74, `weapons` 1,474, `projectiles` 743, `turrets` 216, `overtime` 418, `economy` 162,
`timed` 383, `scripts` 15, `ai` 66 quantities); no tolerance, no case and no 30 Hz value is touched. `Simulation.Tests` with real data now contains one more test (§7.3).
The counter test named in §5 ("Named timers and counters stack upward …") passes at all three rates without this package having touched it.

### 7.2 Every failure, classified

| Test (program) | Rate | Class | Cause, values 30 / 60 / 120 Hz | Correction | Commit |
|---|---|---|---|---|---|
| "Real data: a Crusader keeps its turret on a Battlemaster it chases …" (`Simulation.Tests`) | 60, 120 | T | the loop measured the aiming error after every tick; between two frames the hull has covered only one share of its turn, the turret aims at the frame end: 3.00° against the bound 2.6° | one round of the loop is one frame: commands at the frame beginning, reading at the frame end. Now 0.00° / 0.00° / 0.00°, 7 shots, 45 waited commands at every rate | `e965b6b` |
| "Real data: the original skirmish AI with the shipped script library on Alpine Assault" (`Simulation.Tests`) | 60, 120 | T (behind it S, §7.3) | `MaxTicks = 4_500` and "saved at tick 3,000" were ticks: 150 / 75 / 37.5 s of game time, in which the base does not stand (4 instead of at least 5 buildings) | budget 4,500 frames, saved after 3,000 frames | `c204be1` |
| "Condition derivation from simulation state: No new work …" (`Presentation.Tests`) | 60, 120 | T | "no new progress" was written as work state 1; the first work step counts a whole frame above 30 Hz (1 / 2 / 4 ticks of work) | the comparison is with the work state of the building site | `a1691c1` |
| "Turrets are the simulation's: they turn toward the victim …" (`Presentation.Tests`) | 60, 120 | T | `TurretWaits` has counted frames of waiting since the package TURRETS (15 / 15 / 15), expected were ticks (15 / 30 / 60); at 120 Hz the fire command began before the first frame of the new object (a new turret does nothing until then: angle after the first tick 0 rad) | expectation 15; the commands begin with the first frame of the shooter (`TestFrames.StepToFrameOf`) | `a1691c1` |
| "UnitConditions.TURRET_ROTATE while a turret turns toward its aim" (`Presentation.Tests`) | 120 | T | as above: command before the first frame of the shooter | as above | `a1691c1` |
| "UnitConditions.FREEFALL of a sliding avalanche rock" (`Presentation.Tests`) | 60, 120 | T | the test compared `TensileFormationUpdate.Life` (counts updates, one per tick) with 100; the presentation correctly compares with 100 frames in ticks (100 / 200 / 400) | expectation in frames | `a1691c1` |
| "CaptureFlash.every flash tints the building …" (`Presentation.Tests`) | 60, 120 | T | the envelope `[1, 1, 0.75, 0.5, 0.25, 0, 0]` was read per tick after the flash; it counts frames. The flash count itself has been 4 at every rate since the package TURRETS (before 4 / 9 / 19, §5) | reading in the tick n frames after the flash; the time-scale test of the same file now demands equally many flashes at all three rates | `a1691c1` |
| "JetOrderTests.A gun fighter on an attack order makes pass after pass …" (`Presentation.Tests`) | 60, 120 | T | the minimum speed was read from the tick after the appearance; a new object begins its first frame one frame after the appearance and has speed 0 until then | the reading starts from the first frame of the fighter | `a1691c1` |
| seven "Input: …" tests and two `CursorTests` (`Presentation.Tests`) | 120 | T | "two ticks after the creation" meant "appeared and seen"; the shroud is recomputed after the last tick of its frame: first refresh in tick 0 / 1 / 3. Plus the command ticks 2, 3, 4 of the acceptance test as fixed numbers | `Arena.StepFrames(2)` or `(3)` respectively (frames); the command ticks count from the start of the sequence | `a1691c1` |
| "UnitConditions.real data: combat bike topples, leans and the car is crushed" (`Presentation.Tests`) | 120 | T | the rider is created one frame after the bike and mounts one frame later; five ticks were not enough at 120 Hz ("Sequence contains no matching element") | five frames | `a1691c1` |
| "MissionPresentationTests.Selecting a named unit reaches NAMED_SELECTED; a UI interaction reaches FLAG" (`Game.Tests`) | 60, 120 | T | the VM answers in its next update, and that lies on a frame boundary: one `session.Step()` after the signal contains an update at 30 Hz, at 60 and 120 Hz not necessarily | one frame of steps after every signal | `2adef6b` |
| "MissionEffectsTests.MAP_SWITCH_BORDER confines the camera …" (`Game.Tests`) | 60, 120 | T | the same | `StepOnce` → `StepFrame` | `2adef6b` |
| "MissionEffectsTests.Sound outputs reach the audio control …" and "… Object, HUD and flag outputs reach their consumers …" (`Game.Tests`) | 120 | T | the same: the signal fell on tick 2, the next update is tick 4; the script did not run in the counted window (hence the missing key `NAMED_FLASH_WHITE`) | `StepOnce` → `StepFrame` | `2adef6b` |
| "MissionTextTests.Letterbox bars … only the top bar fades out" (`Game.Tests`) | 60, 120 | T | `CAMERA_LETTERBOX_END` ran 0 / 1 / 3 ticks after the one counted tick, the fade-out began correspondingly later: level after 15 frames 0.5 / 0.51667 / 0.525 | tick by tick until the end of the letterbox (at most one frame), then 15 frames; 0.5 at every rate | `2adef6b` |
| `scenario-audit` tank-duel "crusader's turret waited until tick 444 to turn back (due at 443)" (`PresentationTool`) | 120 | T | the bound of the check disregarded where the death lies in its frame; in game time the rates are equal (death frame 382, first movement 444, rest 458) | bound 3 / 2 / 2 frames instead of 3 / 2 / 1 | `04bba38` |
| `scripts/check-dozer-build.ps1` "mission-intro-clicks" | 60 | T | the script searched for "input locked for 7?? ticks"; the client reports 750 / 1,500 ticks, the same 25 s | the rate is read from the client line, bound 700 to 799 frames | `0b5bdd2` |
| `scripts/check-spectator.ps1` (weak, not red) | 60 | T | demanded `ticks >= 3000`, at 60 Hz only 50 s | demands `gameSeconds >= 100` | `8a97c34` |

**No failure of class P.** Checked and without a finding: the handshake of the mission director with the VM – `ScriptEngine.SelectNamed` stays set until `ClearSelection`,
`SignalUiInteraction` is only cleared at the end of an update that has really executed the scripts, `CompletePresentation` takes effect immediately, and
`MissionPresentationDirector.AfterStep` empties the outputs in every tick: a signal between two frame boundaries is not lost, an output on a boundary reaches its
consumer at every rate. Nothing is changed under `src/CnCNew.Presentation`, `src/CnCNew.Game` and in the client; the 54 screen hashes, the HUD hash and all tests at 30 Hz
are unchanged. Findings in the presentation that are not fixed: §7.7.

### 7.3 Original AI: the same match at 30, 60 and 120 Hz (class S, five causes)

**The session itself (`ScriptedSkirmishSession`) computes correctly:** purchase point and victory check lie on multiples of frames in ticks, the VM and the C++ part
(`ScriptEngine.AiPlayer.cs`) think on frame boundaries, deadlines go through the time service. The only rate error at the call was the tick budget of the test (§7.2). Behind it lay that a
match of the original AI was **not the same** at 60 and 120 Hz: China (hard) against GLA (medium) on Alpine Assault, seed 5, deviated from frame 89. The search was done with a new
diagnostic mode (`SimTool rate-compare <content> --original-diff A,B --fields`, `26c7483`: all described fields of all objects after every frame, rule as in
`docs/rate-invariance/RATE-INVARIANCE-ROUND3-OBSERVERS.md` §5). Every cause is a reader or a constant of the simulation, one commit each; at 30 Hz none changes anything.

| # | First deviation before | Finding, values 30 / 60 / 120 Hz | Correction (file, symbol, rule from `RATE-INVARIANCE-CONSOLIDATION.md` §1.3) | after that equal until | Commit |
|---|---|---|---|---|---|
| 1 | frame 89 (2.97 s) | a producer without an exit (here a driving GLA worker at which the AI orders a worker) put the unit at its momentary position: x 1135.8813 / 1136.1882 (120 Hz not read) | `Phase5Modules.cs`, `ProductionUpdate.OwnPlace`: `Entity.FrameEnd` / `FrameEndAngle` (rule 3) | frame 4937 | `52f3c96` |
| 2 | frame 4938 (164.6 s) | `SlavedUpdate.OnEnslave` woke "in the next tick": `framesToWait` of the three soldiers of a Stinger site at the frame end 6 / 10 / 18 (in phase it would be 12 and 24 ticks) | `SlaveModules.cs`, `OnEnslave`: first update one frame after the enslaving (rule 7) | frame 6729 | `a8ee485` |
| 3 | frame 6730 (224.3 s) | initial speed of a missile per frame as tick value × ticks per frame: 200 units per second are raw 111848107 / 111848106 / 111848108; speed of a Stinger missile in the following frame raw 145402539 / 145402538 / 145402540, position from then on 2 to 3 raw values off | `ProjectileModules.cs`, `MissileFrames` (table next to `MissileData` with the value of the 30 Hz catalogue, pattern `LocomotorFrames`), `MissileAIUpdate.Launch` | frame 6909 | `c692e0a` |
| 4 | frame 6910 (230.3 s) | `DockUpdateBase.Update` handed over the dock in every tick, the dock machines of the dockers ask on frame boundaries: worker 48 enters the supply warehouse in frame 6911 / 6910 / 6910 (`nextActionTick` 6916 / 13830 / 27660, that is frame 6916 / 6915 / 6915) | `DockModules.cs`, `DockUpdateBase.Update`: hand-over on the frame grid of the world, like the machines (the escape "original grid" (Ausweg „Originalraster“) for a state machine; the freeing of orphaned places stays per tick) | frame 10131 | `4776231` |
| 5 | frame 10132 (337.7 s) | a turning fighter published the remainder of its path, not that of its turn: two napalm missiles of a MiG launch with heading 2.55564 / 2.52074 / 2.50328 rad | `FrameGrid.Flight.cs` `FlightFrameStep.TurnsTo`, `Phase5Modules.cs` `WingedStepTurnsTo`, `JetAIUpdate.FlyOn`: `Entity.SetAheadAngle` (rule 3) | frame 10531 | `be8f6f5` |

**Result:** China against GLA is equal at 30 against 60 Hz and at 30 against 120 Hz over **10,531 frames (351 s)** in every compared field of every object (before 88 frames).

**First remaining deviation, frame 10532 (351.07 s), not fixed:** in this frame a Battlemaster (583) leaves the factory, and a Tank Hunter (551) resumes its march.
At 30 Hz 551 receives its new target in the same tick before the appearance of 583, at 60 and 120 Hz one tick after it; the two goal places thereby fall
out swapped and different: 551 (1369.16 | 1871.13) against (1393.67 | 1866.23), 583 (1315.91 | 1898.74) against (1344.64 | 1876.03), equal at 60 and 120 Hz. This is the choice of the
goal places (`FindFreeGoal`) or the tick in which a unit marches on after a battle respectively – territory of the package running in parallel (goal places in
`AIUpdateInterface.Move`); the cause is **not traced down to the symbol** here.

**New test** (`c204be1`): "Real data: the original skirmish AI plays the same match at 30, 60 and 120 Hz" (`Tests.OriginalSkirmishAi.cs`) steps the same match for 7,200 frames
(four minutes) at the three rates side by side and compares after every frame who exists, whom it belongs to, position, heading, health, money and open build orders. A deviation
counts when it lasts three frames (the rule of the tool; four frame ends deviated for a shorter time). **Projectiles in flight are excluded:** with the catalogues of the tests (without
model geometry) the stream of a toxin truck stands, in the three frames of its life, 2 to 5 raw values (3 · 10⁻⁷ units) off the 30 Hz stream (frame 4030, x raw
14011849797 against 14011849799); with the catalogues of the tool (with model geometry) this does not occur. Not traced. Without the five corrections the test fails in frame 89.
**No** original-AI case is included in the group `ai` of the suite: the suite builds its scenes only from the definition graph on a flat map, the original AI needs
a shipped map and the script library; the three-rate test carries the statement.

**Observation at 30 Hz, not touched:** the original AI orders its first worker at a **worker** (object 48, `ProductionCompleted 48 … GLAInfantryWorker` in
frame 89), the second at the command centre. `QueueFirstOfKind` asks every own object with `ProductionUpdate`, and `CanQueueProductionTemplate` allows it. Whether that is intended
is a question for the 30 Hz behaviour of the AI and not for the rate.

**Check runs of the five commits:** for every single commit `SimTool` built, `input-manifest check` identical and `reference-manifest check --set quick --jobs 3` 9 runs identical;
at the state `be8f6f5` `--set full --jobs 4` **50 runs identical**. Suite after that: §7.1. Simulation tests without real data green at 30, 60 and 120 Hz (508).

### 7.4 What stays red

| Test | Rate | Class | Values | State |
|---|---|---|---|---|
| "Real data: retail reference match, determinism and snapshot continuation" (`Simulation.Tests --content-dir`): "The retail match ends with a winner" | only 120 Hz | S (chaotic consequence of a known, open rate dependency), with a T share (tick budget) | winner FactionGLA at tick 14,130 (7.85 min) / 30,930 (8.59 min) / **no winner in 216,000 ticks** (30 min of game time; 13,926 commands, 21,732 shots) | not fixed |

Classification, with what is measured:

- The reference match is **already at the starting state not the same game** at the three rates (`SimTool reference-match --tick-rate N`, catalogues with model geometry, state `4d9d9e5`):
  winner FactionGLA at tick 17,805 (9.9 min) / 33,450 (9.3 min) / 56,760 (7.9 min). The new diagnostic mode `rate-compare --reference-diff --fields` (`f6e529c`) shows the first
  deviation in **frame 0**: the starting Chinooks climb from the tick after their appearance, that is, one share ahead of the frame of the world (z at the frame end 10 / 12.5). This is the
  decision "first frame for all modules" left open in `docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md` §6 (integrators begin with the tick after the creation).
- The match is metastable (`docs/FIXED-POINT-Q39-24.md`: of 36 driver seeds four to five end without a winner at 30 Hz). Every change above 30 Hz can shift its
  end. At the starting state it ended with a winner at 120 Hz; after the first correction of this package (`52f3c96`) it no longer finds one with either catalogue (tool: aborted after
  55 minutes of run time, budget not reached). At 60 Hz it ends with the same winner before as after the corrections (33,450 → 30,930 ticks).
- Sample over four further driver seeds at 120 Hz with a budget of 54,000 ticks (7.5 min; too short for a verdict): winner in 2 of 4 at the starting state, 1 of 4 at the
  final state. **Whether the accumulation is coincidental is not decided.**
- T share: `ReferenceMatchOptions.MaxTicks` is a tick budget (216,000 ticks are 120 / 60 / 30 minutes of game time). Converted to game time the test would run at 120 Hz over
  864,000 ticks; that has not been tried.

At 30 and 60 Hz – today's and the future default rate – no test is red.

### 7.5 Tools and scripts at 60 Hz

| What | 30 Hz | 60 Hz | 120 Hz | Remark |
|---|---|---|---|---|
| `tools/CnCNew.UiRenderTool` | – | 164 of 164 files byte-identical to 30 Hz | 164 of 164 | had no `--tick-rate` and loaded the default rate; option added (`da60580`) |
| `PresentationTool scenario-audit`, `scripts/audit-scenarios.ps1` (3 scenes) | passed | passed | passed (before `04bba38`: 1 failure, §7.2) | repeated at the final state with the corrections of the simulation |
| `scripts/audit-scenarios.ps1 -Scenario exits` (9 scenes) | passed | passed | passed | repeated at the final state |
| `PresentationTool sync-perf --hud --digest` | runs | runs | runs | the digest hashes every tick and is a different one per rate (§7.6) |
| `scripts/check-spectator.ps1` | passed | passed | exit code 0 | had `-TickRate`; bound in game time (`8a97c34`) |
| `scripts/check-dozer-build.ps1` (5 runs) | passed | passed (before `0b5bdd2`: 1 failure) | passed | `-TickRate` added (0 = default of the client) |
| `scripts/check-build-hotkeys.ps1` (3 factions) | passed | passed | exit code 0 | `-TickRate` added |

The three client scripts have run at the state `4d9d9e5` plus the tool commits, **not** once more after the five corrections of the simulation. Further scripts that start the client
or a tool with a world: `capture-world.ps1`, `capture-scenarios.ps1`, `demo.ps1`, `demo-factory-exits.ps1`, `demo-spectator.ps1` have `-TickRate`; the remaining
`demo-*.ps1` pass `@args` on to `demo.ps1`; `capture-text.ps1` has it now (`0b5bdd2`). **`scripts/perf-profile.ps1` has `-TickRate` with its own default 30** and does not follow
the change by itself (deliberately not changed: performance comparisons need a named rate). `perf-compare.ps1` and `audit-vulnerable.ps1` build no world.

### 7.6 Golden files and checksums: does the content depend on the rate?

Checked-in golden values are only the first six rows; everything further is checksums that are in document tables or temporary manifests.

| Golden | Where, who checks | measured at 60 / 120 Hz | Recommendation |
|---|---|---|---|
| 54 screen hashes | `tests/CnCNew.Game.Tests/baselines/screens.txt`, `ShellTests` | equal / equal | **rate-independent, pinning removed** (`c29338a`); nothing to do |
| HUD draw list | `tests/CnCNew.Game.Tests/baselines/hud-drawlist-1280x720.sha256`, `HudTests` | equal / equal | **rate-independent, pinning removed** (`c29338a`, `GoldenRate.cs` deleted); nothing to do |
| `DrawListGolden` | in the source of `tests/CnCNew.Ui.Tests/WndTests.cs` | equal / equal | rate-independent (no world); nothing to do |
| Bone table (D1) | constant in `tests/CnCNew.Game.Tests/DeterminismTests.cs` | equal / equal | rate-independent; nothing to do |
| Triangle count of the roads (206,956) | `Presentation.Tests/RoadRealDataTests.cs` | equal / equal | no world; nothing to do |
| Fingerprint encoding, `GoldenRng` | `Data.Tests`, `Audio.Tests` | not run; neither world nor rate involved | nothing to do |
| Output of the `UiRenderTool` (164 files) | document tables, `capture-text.ps1` | 164 of 164 / 164 of 164 | rate-independent; nothing to do |
| Manifest of `capture-text.ps1` | temporary manifest | 166 of 166 pixel-identical / not measured | rate-independent |
| Audio capture (WAV) tank-duel, 300 frames | document tables | equal at 30 / 60 / 120 (`B9C6F66CF73D4BF5`) | rate-independent for this scene; the three other WAV are not measured |
| Total checksums of `scenario-audit` (3 scenes) | document tables only | all three different | **regenerate at 60 Hz after the change**: one header line more (the rate), in capture-building the Ranger begins half a frame earlier (`[MOVING]` in frame 12 / 11 / 11), in tank-duel turret angles read in the middle of the swing deviate by 1 to 2° |
| `sync-perf --digest` | document tables only | different by design (hashes every tick) | regenerate at 60 Hz |
| `fx-audit` | document tables only | names `SimulationRate.Original` itself | stays at 30 Hz, with this reason |
| `object-audit`, `terrain-audit` | document tables only | run once, not compared between rates; no use of a rate found in the code | nothing to do |
| Manifest of `capture-world.ps1` (19 scenes, "preliminary") | temporary manifest | 9 of 19 pixel-identical at 60 Hz | regenerate at 60 Hz – decide the road finding first (§7.7) |
| Film strips of `capture-scenarios.ps1` (77 pictures) | document tables only | 13 of 14, 0 of 29, 0 of 34 equal at 60 Hz | regenerate at 60 Hz |

The captures are compared at the state before the five corrections of the simulation and only 30 against 60 Hz.

### 7.7 Findings that are not fixed

| Finding | Class | Values, evidence | Why open |
|---|---|---|---|
| Draw order of the roads in the client depends on the rate | P | `src/CnCNew.Client.Stride/Layers/TerrainLayer.cs`, `CreateRoads`: all road meshes have the same bounds, the order of equal sort keys is not defined. Still picture jet-strafe in frame 60: 1,262 pixels at the roadside different between 30 and 60 Hz, 60 and 120 Hz equal, with `CNCNEW_ROADS=0` all three equal. Carries the whole difference of `roads-junctions`, `roads-crossing`, `roads-overview` | own sort keys per batch make the roads rate-equal, but no direction reproduces every 30 Hz picture (one changes `roads-overview` at 30 Hz by 12,810 pixels, the other hits the old 60 Hz picture). That would break "30 Hz unchanged"; which stacking is right needs a look at the original. The patch lies in the scratch directory of the session (`road-sort-order.patch`), not in the repo |
| Half a frame of offset in staged scenes | S, not traced down to the symbol | capture-building: `[MOVING]` of the Ranger in frame 12 / 11 / 11, x at t = 30: 11.0 / 11.3; `match-waypoints`: 7,155 pixels (yaw angle of a dozer); the Eva line of the jet scene begins at 30 Hz at a different sample | pattern: a command given at a frame boundary takes effect one tick later, at 60 Hz thus half a frame earlier. Candidates: `SimulationWorld.Submit` and the one `World.Step()` in `LiveScenario.Populate` |
| Pictures that deviate at 60 Hz, without classification | – | tank-duel: all 29 film-strip pictures (picture 10 faintly over the whole screen, largest channel difference 13); `match-start` 1 pixel; `mission-start` 161 pixels | not investigated |
| Goal places after leaving the factory / resumption of the march | S | §7.3, frame 10532 | territory of the parallel package |
| Stream projectile of the toxin truck | S | §7.3, 2 to 5 raw values over three frames, only without model geometry | not traced |
| Reference match not rate-equal; at 120 Hz without a winner | S | §7.4 | open decision "first frame for all modules" |

### 7.8 To do after the change

1. Regenerate the checksums of `scenario-audit`, `sync-perf --digest`, `capture-world.ps1` and `capture-scenarios.ps1` at 60 Hz and replace the document tables (§7.6);
   `fx-audit` stays at 30 Hz.
2. `scripts/perf-profile.ps1`: decide the default `-TickRate 30` deliberately.
3. `baselines/reference-manifest.json` is defined at 30 Hz and not touched by the default rate of the game layer as long as `SimTool` names its rate; that is not
   checked here, because no default was changed.
4. The comments in `tests/Shared/TestTime.cs` ("golden hashes … name their rate themselves") have not been quite right since `c29338a` and are not adjusted.

### 7.9 Not done, not verified

- `Game.Tests` without `--content-dir`, the Debug build of `Game.Tests` and `--determinism-full` have not run at 60 and 120 Hz; `Presentation.Tests` and `Ui.Tests` only as a
  Release build via the copied binary directories.
- The three client scripts (§7.5) and all captures are not repeated after the corrections of the simulation; captures are not compared at 120 Hz.
- The original AI is compared only in one pairing on one map with one seed (China hard against GLA medium, Alpine Assault, seed 5), 14,400 frames; from frame 10532 it
  deviates. Other pairings, maps, seeds and a human in the slot are not measured.
- For the five corrections of the simulation there is, apart from the three-rate test of the match, no test of its own per cause.
- Performance is not measured (the hand-over of the dock and the one comparison per production do not lie in the hot path; the rate of the AI survey from
  `docs/SKIRMISH-AI-ORIGINAL.md` §7 is not re-surveyed).
- All runs ran side by side on a loaded machine; run times are not performance figures.
- No picture, no sound and no gameplay at 60 or 120 Hz has been checked by a human. Equal checksums verify rate independence, not correctness.
