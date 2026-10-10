> **Stage:** 2 – Engine · **Milestone:** S2-M3 Tick rate as a value of the world · **Phase:** 21
> **Status:** done (7 October 2026, state `2a0a6f5`)
> **Predecessor:** HANDOFF-PHASE20.md · **Successor:** HANDOFF-PHASE22.md · **Results in:** docs/TICKRATE-MIGRATION.md §14
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 21): Tick rate as a parameter – presentation, game layer, UI, sound, client, tools and tests (frame-identical at 30 Hz)

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md`, **`docs/MULTIPLAYER-PLAN.md`**, **`docs/TICKRATE-AUDIT.md`** (§2, §3, §4, §6, §7), `docs/PRESENTATION-OBJECTS.md`, `docs/PRESENTATION-FX.md`, `docs/CLIENT-FX.md`, `docs/GAME-AUDIO.md`, `docs/GAME-HUD.md`, `docs/GAME-SHELL.md`, `docs/SCRIPT-ENGINE.md` §7a and the final report of Phase 20.
Prerequisite: Phase 20 is completed – the simulation carries its rate as a value of the world and runs through at 60 and 120 Hz.
This phase may run **in parallel with Phase 22** (fixed point) in a worktree of its own; 21 is merged before 22.

## Goal in one sentence

Nothing outside the simulation assumes that a tick lasts 1/30 second. **At 30 Hz every frame, every sound and every checksum stays the same;** at 60 Hz everything visible and audible lasts as long in seconds as at 30 Hz.

## Honest status (starting point; places from `docs/TICKRATE-AUDIT.md`)

1. **Particles, lights, tracers and decals run on the simulation tick.** `LiveGame.SyncAfterTick` calls `Fx.Update()` once per tick; the FX data is authored in 30-per-second frames of the original (`FxValueReader.FramesPerSecond`). At 60 Hz every effect would run twice as fast. `FxExecutor` in addition mixes FX frames with simulation ticks (throttling of the damage effects).
2. **Copies of the 30** in `Suspension.cs`, `FrameProjector.cs` (`3 * 30`), `UnitConditionTracker.cs`, `SoundCues.cs`, `HudMessageFeed.cs`, `InGameUiSettings.cs` (own constant, used by `MissionText`), `MissionPresentation.cs` (`LetterboxFadeFrames`), `LiveGame.cs`, `Program.cs` (smoke test step), several tools.
3. **Envelopes in ticks:** muzzle flash 4 ticks, firing state exactly one tick (`ConditionDeriver`), selection blinking, heal icon, cheering, sound cue window, EVA intervals, HUD messages, superweapon clocks.
4. **The mission presentation uses the tick as its clock** (camera, fades, subtitles, counters, image filter per step).
5. **Integration per tick in the display:** suspension (`Suspension`), alpha decay of the mission texts, image filter.
6. **Already correct** (on wall-clock time or frame time): model and skeleton animation, texture animation, water, camera, command markers, shell transitions. Do not touch, but check along in the time-scale test.
7. **Tests and tools:** around 105 fixed steps `1.0 / 30` and around 845 lines with hand-written tick expectations in 84 test files (not all concern this phase).

**Addendum from Phase 20 (6 October 2026, state `9299a2d`):** the simulation is converted and yields the same results at 30 Hz (`docs/TICKRATE-MIGRATION.md`). Authoritative for this phase are there §6 (16 items, what consumers outside the simulation must change), §8 (units of the public values: what are frames of the original and what are ticks of the rate) and §9 (tools). The most important items: `SimulationWorld.VisionIntervalTicks` is now only an alias for callers outside the simulation (replace by `world.VisionInterval`, then remove); `GameData.SessionRate` is the one named rate of the game layer and builds the catalogue; `SaveGame` (version 1) writes and checks no rate; 14 + 1 + 3 uses of `SimulationRate.OriginalFramesPerSecond` in `CnCNew.Game`, `CnCNew.Presentation` and the tools are only made compilable, not decided; sabotage durations leave the simulation as a tick count in the event value; `TensileFormationUpdate.Life` and the timer `CounterValue` of the script VM count ticks of the rate; `ScriptOutput.Arguments` are unscaled map parameters (frames of the original), `ScriptOutput.Tick` is a tick of the world; `StandingOrders`/`LiveGame.BeforeTick` generate commands at the tick cadence (changes the replay content at 60 Hz). The simulation runs through deterministically at 60 and 120 Hz (reference match, two AI skirmishes, three missions); its behaviour there is not assessed. Tools: `--tick-rate N` on `SimTool` and `CampaignTool`. Performance comparisons only as a direct switch between two binary directories in the same period (on the evening of 6 October the machine delivered 24–44 % higher absolute values than `docs/PERFORMANCE.md` §6a at equal speed of both states).

## Decisions that are fixed

- **Two concepts, two names.** The *simulation rate* comes from the world and is variable. The *original frame* (1/30 s) is the fixed unit in which the original authored client content (particles, shell transitions, `InGameUI` times, camera values). There is exactly one named constant for it outside the simulation, and it is never confused with the simulation rate.
- **The clock of the presentation is game time in seconds**, derived from tick, rate and interpolation fraction. It stands still during pause, runs with the game speed and does not jump. Wall-clock time remains for everything that is independent of the game (shell, mouse cursor, camera).
- The presentation does not change the simulation: hashes of the reference manifest stay unchanged.

## Brief (in this order)

1. **Reproduce the starting state.** Build, all test programs, manifest, and save all display checksums at 30 Hz as the yardstick: the 54 screen hashes and the HUD drawlist hash (`tests/CnCNew.Game.Tests/baselines`), the UI renderer PNGs, the capture scenes (`scripts/capture-*.ps1`, `audit-scenarios.ps1`), FX bench and FX audit hashes, the audio capture checksums.
2. **Game layer.** `LiveGame`: tick duration from the rate of the world; the upper limit of ticks per frame as a time budget (today 8 ticks ≙ 0.27 s of game time) instead of as a tick count; the `SimulationWorld.VisionIntervalTicks` coupling of the fog display via the world; provide the game-time clock.
3. **Effects.** `FxExecutor`/`ParticleWorld` run on the original-frame grid, driven by game time (accumulator in `LiveGame` or in the executor), not by the simulation tick. The throttling of the damage effects gets a single clock. At 30 Hz every original frame falls on a tick – the FX checksums stay the same.
4. **Object display.** Muzzle flash, firing state, heal icon, cheering, selection blinking, decay curves: durations in seconds or original frames. `ConditionDeriver`: "has fired in the last tick" becomes "has fired within the last 1/30 s" (otherwise the state would be visible only half as long at 60 Hz). `Suspension`: compute spring and damper with the step size, data per original frame.
5. **Sound and HUD.** `EvaCatalog`/`AudioDirector` (intervals, lockout times), `SoundCues` (windows, maximum number per time instead of per tick), `HudMessageFeed`, `SuperweaponTimers`, command feedback (`CommandFeedback` durations).
6. **Mission.** `MissionPresentation` (clock, letterbox fade as wall-clock time as in the original, image filter per time), `MissionText` (`InGameUiSettings` without its own rate constant), `LiveMission.ClientPresentationLatencyTicks` as time; `CAMERA_BW_MODE_*` in original frames.
7. **Client and tools.** `Program.cs` (fixed step in the smoke test from the rate), `--tick-rate N` on the client (default 30 until Phase 23), `PresentationTool`, `UiRenderTool`, `CampaignTool`, the demo and capture scripts: compute tick budgets from game time.
8. **Tests.** Fixed steps `1.0 / 30` via a helper from the rate of the respective world; express tick expectations that actually mean times in game time (a helper that converts seconds into ticks of the test world). Expectations that really mean ticks (scheduler, sequences) stay. Every test file from the inventory is assessed; the result is a list "file × converted / deliberately in ticks / not affected".
9. **Time-scale test at 60 Hz.** For every element from items 3 to 6 a check that its duration in seconds is the same at 30 and 60 Hz (tolerance: one original frame): lifetime of a particle system, muzzle flash, blink sequence of the capture, heal icon, EVA lockout time, subtitle duration, camera move, settling time of the suspension travel. The simulation at 60 Hz is good enough for that (Phase 20), even if its rules are not yet right there.
10. **Search run.** The patterns from `docs/TICKRATE-AUDIT.md` §8 no longer yield an unassessed hit outside the simulation; a source test forbids in `CnCNew.Game`, `CnCNew.Presentation`, `CnCNew.Ui`, `CnCNew.Client.Stride` every rate constant except the one for the original frame.
11. **Docs:** `docs/PRESENTATION-OBJECTS.md`, `docs/PRESENTATION-FX.md`, `docs/CLIENT-FX.md`, `docs/GAME-AUDIO.md`, `docs/GAME-HUD.md`, `docs/SCRIPT-ENGINE.md` §7a, `docs/TICKRATE-AUDIT.md` (presentation part done), `docs/MULTIPLAYER-PLAN.md`, `CLAUDE.md`, README, ARCHITECTURE, ROADMAP, IMPLEMENTATION-PLAN.

## Bottlenecks and showstoppers of this phase

- **Checksum equality at 30 Hz** is the proof; every deviation is an error in the rebuild. Where an accumulator in floating point shifts a step by one frame, keep the time in integers (ticks and rate), do not sum in `double`.
- **Smoke tests and golden hashes prove reproducibility, not correctness** (`CLAUDE.md`). The time-scale test verifies durations; whether it *looks and sounds right* at 60 Hz only a human can say. Create a visual checklist for Phase 23 (effects, muzzle flash, suspension, mission intro), report none of it as accepted.
- **What deliberately becomes different at 60 Hz:** the interpolation has steps half as large; positions are finer. That is intended and not a finding.

## Out of scope

More interpolation (turret, construction progress) and extrapolation (Phase 29); the switch of the default to 60 Hz (Phase 23).

## Acceptance

All checksums saved under item 1 are unchanged at 30 Hz; the reference manifest passes; the time-scale test passes at 60 Hz; the search run is empty; the test list is complete. The final report names numbers with commit hashes and explicitly what no human has seen. At the end update the "starting point" in `docs/handoffs/HANDOFF-PHASE22.md`.
