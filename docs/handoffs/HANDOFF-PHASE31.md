> **Stage:** 1 – Port · **Milestone:** S1-M11 Playability and acceptance by a human (fidelity package A: F-03, F-04, F-05) · **Phase:** 31
> **Status:** **done** (10 October 2026) – ran before Phase 24 by decision D-12; implementation / manifest commits `f9abff8`/`12533c6` (F-03), `39a1656`/`1a191cd` (F-04), `94711fb`/`bab51c3` (F-05), and a defect found on the way, `f41c851`/`2b34dba`; result and what differs from this brief: `docs/SIMULATION-CORE.md` §44 (F-03: the original does not scatter the riders of a living transport – built is `OpenContain::exitObjectViaDoor`; F-05: the debris is counted, not created). Built and measured headless, not seen by a human
> **Predecessor:** HANDOFF-PHASE23.md (last completed phase) · **Successor:** HANDOFF-PHASE24.md · **Results in:** docs/SIMULATION-CORE.md, docs/GAME-RULES.md, docs/ACCEPTANCE.md §6, docs/plan/STAGE-1-PORT.md §3
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 31): Fidelity package A – passengers scatter on exit, every unit of a group gets its own destination, the slow death plays its phases

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md`, `docs/TESTING.md`,
`docs/plan/PROGRAMME.md` (§2 Stage 1, §5, §7), `docs/plan/STAGE-1-PORT.md` (S1-M11 and the fidelity register §3),
`docs/ACCEPTANCE.md` §6 (the three findings with their scenes and source references), `docs/SIMULATION-CORE.md` (§43 frame
convention, §41.1, the state hash and `MarkDirty`), `docs/GAME-RULES.md` and `docs/MULTIPLAYER-PLAN.md` §6 (one cause per
reference change). Prerequisite: Phase 23 is completed and the reference manifest at 60 Hz passes (measured on 10 October
2026 at `8588905`, 50 of 50 runs).

## Goal in one sentence

Three places where the port visibly behaves differently from the original are made to behave like the original – each
one cause, one commit, one rewritten manifest – so that Phase 24 records its reference values on a simulation that no
longer carries these three deviations in every match.

## Starting point (measured)

- State `2777a90` and later (docs only since): clean build 0 warnings; `reference-manifest check --set full` 50 of 50 at
  60 Hz; `input-manifest check` identical; compat-check 731; `Simulation.Tests` green at 30, 60, 120 Hz and with real data;
  `Game.Tests` (Release), `Presentation.Tests`, `Ui.Tests` green (fresh machine, 10 October 2026; `BUILDING.md`). Re-measure:
  tier 4 at the start unless the previous phase ended with it on the same commit (`pwsh scripts/test.ps1 -Tier full`).
- Versions: `WorldSnapshot` 10, replay 12, `CommandTypes.Version` 15, `MissionSnapshot` 3, `SaveGame` 3. Reference match
  FactionGLA at tick 28,260, `CA05181C36C62C7A`; stress test `9EE9F730D124C3BD`; 14 of 16 AI runs decided; objective driver
  24/24 and 24/24 at 30/60/120 Hz; the rate-equivalence suite 0 / 0 in ten groups.
- The three findings are reproduced by staged scenes without a window (`docs/ACCEPTANCE.md` §6, `docs/DEMO-SCENARIOS.md`,
  `scripts/audit-scenarios.ps1`): `acc-board` (F-03), `acc-march` (F-04), `tank-duel` with `scenario-audit --name tank-duel
  --drawables-at 390` (F-05).

## What is already done – do not build twice

- Containment (`ContainModuleBase`, `TransportContain`, `OpenContain`-style removal in
  `src/CnCNew.Simulation/Modules/BuiltIn/ContainModules.cs`, `SimulationWorld.EvacuateContainer` in
  `World/SimulationWorld.Containers.cs`); the key `ScatterNearbyOnExit` is parsed as opaque (`ModuleCatalog.cs`) and read by
  nothing.
- Group movement: `SimulationWorld.TryFreeGoalSpot`/`FindFreeGoal` (`World/SimulationWorld.ExitGoals.cs`) ask for a free
  place near an occupied goal every 15th frame (`AIUpdateInterface.GoalSpotCheckDue`, `Modules/BuiltIn/Phase5Modules.cs`);
  formations `asTeam` (Phase 14); the pathfinder with stationary blockers (`StationaryBlockersAhead`, Phase 15).
- `SlowDeathBehavior` (`Modules/BuiltIn/Phase5BlockerModules.cs`): the weighted sibling lottery and the destruction timer;
  its comment lists `FX`/`OCL`/`Weapon` per phase as not modelled. Effect lists, creation lists (`SimulationWorld.CreateFromList`,
  delayed `SpawnSetup`) and weapon firing exist elsewhere and are reusable; the presentation already plays `FXList`
  requests of other modules (`TransitionDamageFX`).
- The frame convention above 30 Hz (`docs/SIMULATION-CORE.md` §43) and the suite that checks it.

## Decisions that are fixed

- D-12 (10 October 2026): this package runs before Phase 24.
- `docs/MULTIPLAYER-PLAN.md` §6 rules 2 and 3: one cause per reference change; the manifest is rewritten in a commit of its
  own with before/after values and justification – here **three times**, one per finding.
- Stage 1 rule (`docs/plan/PROGRAMME.md` §2): the change makes the port behave like the original; the reference is the
  function named per item, read and rewritten, cited in the code comment.
- Random draws (F-03 scatter angle and distance) come from the logic stream in the order of the original; nothing from a
  tick count, nothing from wall-clock time.

## Brief (in this order)

1. **Reproduce the starting state** (tier 4 if owed) and record the result quantities of the manifest runs
   (`reference-manifest results … --out <json> --commands <dir>`, scratchpad) as the standard of comparison for every step.
2. **F-03 – scatter on exit.** Read `TransportContain::onRemoving` (`GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Contain/TransportContain.cpp`, line 330 ff.) and
   `OpenContain::scatterToNearbyPosition` (`OpenContain.cpp`, line 688 ff.): when `m_scatterNearbyOnExit` is set (the data
   default), draw a random angle and a distance between one and one and a half bounding radii of the container, put the rider
   on the container's centre, let him ignore the container as an obstacle and walk to that point (`aiMoveToPosition`). Type
   `ScatterNearbyOnExit` strictly (compatibility baseline shrinks by its opaque entry; `compat-check`), implement the scatter
   for every open-container exit (transport, garrison exit where the original scatters, parachute landing if it goes through
   the same path – check the reference per container kind), with the frame convention (the walk starts in the first tick of
   the rider's own original frame). Test: `acc-board` – after the evacuation the five Rangers stand apart at one to one and a
   half radii; a three-rate test that the end positions are equal within the suite's tolerance; rewrite the manifest in a
   commit of its own after the implementation commit, with the comparison of result quantities (expectation: the same
   winners in the large majority of runs; every other deviation examined until its cause is named).
3. **F-04 – own destination per unit of a group.** Read `AIGroup::computeIndividualDestination` (`GameLogic/AI/AIGroup.cpp`,
   line 480) and `Pathfinder::adjustDestination`/`checkDestination` (`GameLogic/AI/AIPathfind.cpp`, lines 5355 and 4924): when a
   group is ordered to one point, every unit keeps its offset from the group centre and its destination is moved to the
   nearest free cell at the moment of the order; a unit then walks one path to a place that is its own. Where the command
   path carries a group order today (presentation issues one `SetMoveTargetCommand` per unit with the same target; the
   script VM and the AI issue per unit as well), compute the individual destinations **in the simulation at the order**
   (deterministic, from the ordered set of unit IDs and the formation offsets), not in the presentation. The free-goal
   search every 15th frame stays for the cases the original keeps it for (`AIUpdateInterface` goal spot), but no longer
   produces the detour of `acc-march`. Test: `acc-march` – all ten units reach their places without a unit walking around
   the group, distances travelled within a named factor of the straight line; the AI runs of the manifest re-measured
   (group attacks change); three-rate check; manifest commit of its own.
4. **F-05 – the slow death plays its phases.** Read `SlowDeathBehavior::doPhaseStuff` (`GameLogic/Object/Behavior/SlowDeathBehavior.cpp`,
   line 339 ff.), `beginSlowDeath` (line 332) and `update` (lines 466 and 472): at the beginning, at the midpoint and at the
   end of the slow death the effect list, the creation list and the weapon of the phase (`INITIAL`/`MIDPOINT`/`FINAL`) are
   played, created and fired. Type the per-phase `FX`/`OCL`/`Weapon` keys of `SlowDeathBehavior` strictly (they are opaque
   today), play the effects through the existing `FXList` event path, create through `CreateFromList`, fire through the
   weapon path; the hull and debris of `tank-duel` (`ChinaVehicleBattleMasterDeadHull`, two pieces of debris) come into
   being. Count what the data references and the simulation cannot yet create (`CreationSkipped` with reason) – nothing
   silent. Test: `tank-duel` – `scenario-audit --drawables-at 390` lists the hull and the debris; the suite's slow-death
   group stays 0 / 0 at 60 and 120 Hz (phase times on the original grid); manifest commit of its own (every death in every
   run changes: the stress test, every AI run, every mission).
5. **After each of the three:** `pwsh scripts/test.ps1 -Tier gate` before the push; the manifest rewritten only in the
   commit that names the one cause; `docs/ACCEPTANCE.md` §6 marks the finding as fixed with the commit; the fidelity
   register `docs/plan/STAGE-1-PORT.md` §3 sets F-03/F-04/F-05 to "done (<commit>)"; `docs/SIMULATION-CORE.md` and
   `docs/GAME-RULES.md` describe the rule with its source reference.
6. **Completion:** tier 4 (`pwsh scripts/test.ps1 -Tier full`) on the merged state, including the rate-equivalence suite
   0 / 0, both manifests, the scene audits; `pwsh scripts/perf-profile.ps1` against the previous profile (the scatter and
   the phase effects add objects – name the cost); update the "Starting point" of `docs/handoffs/HANDOFF-PHASE24.md` with the
   measured state (new reference values, versions, what was not run), the milestone status in `docs/plan/STAGE-1-PORT.md`
   and `docs/plan/PROGRAMME.md` §3, the index `docs/handoffs/README.md` (Phase 31 done, Phase 24 current), `CLAUDE.md`.

## Bottlenecks and showstoppers of this phase

- **Three manifest rewrites in a row.** Every intermediate state must pass its own manifest; never two causes in one
  rewrite. Keep the result-quantity comparison per step (`reference-manifest results`) – winners, end ticks, shot and
  object counts – and explain every outcome change (chaotic amplification is permissible and named as such; a wrong rule is
  not).
- **Formats.** If a new field must be saved (scatter target, individual destination, slow-death phase state), it is written
  only when it deviates from the default; if bytes change at the default, `WorldSnapshot` (and the formats that embed it)
  raise their version and old states are rejected with a clear message.
- **Rate invariance.** Every new timed thing (phase times, walk start) lies on the original grid and follows §43; run the
  suite after every step, not only at the end. A new quantity that the suite does not measure gets a case in the fitting
  group (slow death in "special powers and time modules", scatter and group destinations in "locomotion").
- **The AI runs will shift** (group destinations change every attack). That is expected; the number of decided pairings
  and the winners are reported before/after, not assessed for balance (D-9 stays open).

## Out of scope

F-06 to F-09 (D-13) and F-10 (D-14): each its own package when decided; the acceptance run by a human (possible at any
time); everything of Phase 24.

## Non-negotiable rules

`CLAUDE.md`, `docs/TESTING.md` (tier 2 before every commit, tier 3 before every push of code, tier 4 at the completion),
`docs/MULTIPLAYER-PLAN.md` §6, the Stage 1 rule of `docs/plan/PROGRAMME.md` §2. Foreign code is read and rewritten, never
copied; every ported rule cites file and function. Nothing falls back silently: a creation the simulation cannot perform is
counted with its reason. Commit and push to `origin master` when finished (`pwsh scripts/audit-publish.ps1` before the push).

## Acceptance

The three scenes show the original's behaviour (`acc-board`: riders scattered; `acc-march`: no detour; `tank-duel`: hull
and debris); the manifest was rewritten exactly three times, each with one named cause and a result-quantity comparison;
the suite is 0 / 0 at 60 and 120 Hz; tier 4 is green on the merged state; the fidelity register and `docs/ACCEPTANCE.md`
§6 carry the commits. What no human has seen is named as such (the scenes are headless; the look of the scatter, the march
and the death explosion is judged by the owner's acceptance run).

## Final report and N+1

Numbers with commit hashes (the three implementation commits, the three manifest commits), the result-quantity comparison
per step, the tier that ran and what was not run, the performance comparison, the updated documents, and the "Starting
point" of `docs/handoffs/HANDOFF-PHASE24.md` continued with the measured state. Phase 24 is the next brief; it is not
rewritten.
