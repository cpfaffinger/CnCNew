> **Stage:** 1 – Port · **Milestone:** S1-M11 Playability and acceptance by a human (fidelity package B: F-06 to F-10, F-43) · **Phase:** 39
> **Status:** done by machine (27 October 2026; `docs/SIMULATION-CORE.md` §53; starting state measured 18 October 2026 at `89490f5`)
> **Predecessor:** HANDOFF-PHASE38.md · **Results in:** docs/SIMULATION-CORE.md, docs/GAME-RULES.md, docs/SCRIPT-ENGINE.md, docs/plan/STAGE-1-PORT.md §3, docs/SPECTATOR-MODE.md (research in progress)
> **Side items:** the modern skin's open ends (S3-M5: tooltips, message list and superweapon clocks in the skin's style; the campaign screens and the mission HUD pinned in the modern skin); D-34 stays the owner's
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 39): Fidelity package B - the known findings of Phase 23 (F-06 to F-09, decision D-13 "fix"), `<This Team>` in the mission VM (F-10, D-14 "fix")
and upgrades that take their build time (F-43), each one cause, one implementation commit and one manifest commit

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md` (paragraphs "Phase 31", "Phase 37" and "Phase 38"),
`README.md`, `docs/plan/PROGRAMME.md` (§5 order, §7 decisions D-13, D-14, D-15), `docs/plan/STAGE-1-PORT.md` (§3, the fidelity register; S1-M11), `docs/SIMULATION-CORE.md`
(§44 - fidelity package A is the model for this phase; §42 the factory exit; §43 the frame convention), `docs/rate-invariance/RATE-INVARIANCE-GROUND.md` §6 (F-06),
`docs/SCRIPT-ENGINE.md` (teams, F-10), `docs/SPECTATOR-MODE.md` §14 (the research tab that waits for F-43), `docs/TESTING.md`. Prerequisite: Phase 38.
**This phase changes the simulation: every fix moves reference values; each gets its own manifest commit with its cause.**

## Goal in one sentence

The five known fidelity findings of Phase 23 and F-43 behave as the original's source does - each fixed with a test that fails before the fix, at 30, 60 and 120 Hz, with
one manifest commit per cause - or, where the source says otherwise, are named as limits with the reason.

## Starting point (measured, after Phase 38 – re-measure when this phase starts)

- **State:** `master` after Phase 38 (the code at `c50b725`, measured at `89490f5`; the commits after it are documentation). Tier 4 with 56 of 56 steps ok at `89490f5`
  (7,282 s; 0 warnings; all test programs; simulation, game, presentation and network tests at 30, 60 and 120 Hz; both manifests identical; the rate-equivalence suite;
  the long run; the client checks; every network step; `replay-browser`; `skins`; `patch-layer`; the staged scenes). Versions: `CommandTypes.Version` 17, replay 15,
  `WorldSnapshot` 10, `MissionSnapshot` 3, `SaveGame` 3, match description encoding 3, skirmish session state 3, protocol 6, transport 2, the replay file 1, the analysis
  export 1, settings schema 2.
- **The reference manifest** (`3b6fa80`, unchanged since Phase 34): retail reference match FactionGLA at tick 32,520, `6052051714309B17` / `84077720F511C1ED`; stress
  test `9EE9F730D124C3BD` / `F338463BE66D99C0`; 16 AI pairings 16 decided; 27 mission runs.
- **Campaign at 60 Hz (at `89490f5`):** reachability clean 24/24 and 24/24; audit 24 of 24 identical on a second run and after save/restore with end probes 13/24 and
  15/24 (exit code 1 as before: `MD_CAMPEA_DEMO` names a map the master installation does not have); objective driver victory 24/24 and defeat 24/24.
- **Performance** (`scripts/perf-profile.ps1` with the binaries of `d718187` and of `89490f5`): every one of the ten runs with the same final hash on both sides; means
  within -5.9 % and +0.3 % (8 players: 878.9 against 876.5 µs per tick). Under `movement-budget`, warmed up, the repeatable longest tick is tick 101,610 (8.4 / 9.6 ms:
  a wave of 40 single attack-move orders and the path queue's frame budget, `docs/PERF-MOVEMENT.md` §13).
- **Clean room:** the commit `89490f5` cloned into `C:\cr38` with empty caches (1,150 files), built in 153 s (259 packages, 0 warnings), the content exported afresh in
  134 s (25,608 files, the known export), input manifest identical, the quick reference set 9 of 9, `Simulation.Tests` and `Data.Tests` with that content green, the
  client package (933 files, 318 MB) and both server packages (204 files, 81 and 83 MB) free of original data - every step ok.
- **The fidelity register** (`docs/plan/STAGE-1-PORT.md` §3): F-06 to F-10 "fix" (D-13, D-14), F-43 open (an upgrade is complete when bought; the observer's research
  tab shows nothing "in research" because of it), F-11 (rank system, D-15), F-12, F-13, F-14 decisions; F-24 partly closed by Phase 38 (display mode, the wide-screen
  view, text size and sound device reachable).
- **Stage 3 is built by machine** (S3-M1 to S3-M7); none of its milestones is accepted by a human (R3). D-34 is prepared as a measured proposal
  (`docs/SKIRMISH-AI.md` §7a/§7b, `docs/proposals/D-34-scout-reserved.patch`) and stays the owner's.
- **Not verified, carried on:** no human has used the modern skin, watched the observer, read a report, used the replay list or seen the attached effects; a second
  physical machine and a real network (E1, D-10); the owner's open decisions D-2 to D-8, D-10, D-17 to D-21, D-23 to D-34.

## What is already done – do not build twice

The fidelity package A (Phase 31: F-03 door exit, F-04 group destinations, F-05 slow-death phases, `docs/SIMULATION-CORE.md` §44) is the model: one cause per commit, a
test that fails on the unfixed code, the manifest written in its own commit, the result quantities compared (`SimTool reference-manifest results`) and the outcome
changes named. The rate-equivalence suite, `SimTool rate-compare --fields` (the first deviating frame of two rates), the field comparison of `desync-diff`, the analysis
(`SimTool match-analysis`) and the observer's research tab (which shows "in research" as soon as the simulation queues an upgrade) exist.

## Decisions that are fixed

- D-13 (default "fix, one cause per commit") for F-06 to F-09, D-14 (default "fix") for F-10 - the owner may decide otherwise; the brief uses the defaults.
- F-43: the original queues an upgrade in the building's production queue for its build time (`ProductionUpdate::queueUpgrade`); the port completes it when bought. Fix.
- F-11 (rank system, D-15 "implement as a rule of the game") is **not** in this phase (it is larger and changes every match; a phase of its own).
- The rule profile keeps the community fixes off in the original profile; a fidelity fix of the original's behaviour is not a profile, it is the port.
- The skins are presentation (Phase 38): nothing here touches them; the original skin stays the default (D-21).

## Brief (in this order)

1. **Reproduce the starting state** (tier 4 unless Phase 38 ended with it on the same commit; it did at `89490f5`, and later commits are documentation only).
2. **F-08 `QuantityModifier`** (`ProductionUpdate`: one order of a Red Guard gives two): the smallest cause first; a test that counts the units of one order.
3. **F-09 a produced unit comes into being when the gate opens** (`ProductionUpdate`, the exit modules): read the original's order of door, creation and exit; a test that
   reads the tick of the unit's creation against the door state.
4. **F-06 `SupplyMachine` overwrites a player's command** that arrives between "wants to deliver" and the dock command: a test that gives the order in that frame.
5. **F-07 a magazine without reload time fires once more** through a second command in the same tick: a test of two commands in one tick.
6. **F-10 `<This Team>` in a team's create script in the mission VM** (fixed for AI sessions only): a test on a synthetic mission script; the campaign checks (reachability,
   audit, objective driver) before and after.
7. **F-43 upgrades take their build time**: the upgrade is queued in the building's queue (`UpgradeInfo.BuildTicks`), paid when queued, completed when its time is over,
   cancelled with a refund as the original does; the command bar shows its progress; the observer's research tab shows it "in research" (Phase 37 built the tab for it).
   Session state, savegames and the version of `WorldSnapshot` if the queue entry needs a field (then a version step with its reason).
8. For every cause: tier 2 before the commit, the manifest in its own commit (`reference-manifest write --set full`), `reference-manifest results` against the previous
   manifest, the outcome changes named in `docs/SIMULATION-CORE.md`; tier 4 before the push of a manifest commit.
9. **Side items as time allows** (named in the header).
10. **Documents** and the next brief.

## Bottlenecks and showstoppers of this phase

Each fix moves reference values; two fixes in one manifest commit cannot be told apart afterwards. A fix that makes a command's result depend on the order of commands
needs tier 4 before its push (Phase 31's pitfall). Above 30 Hz read a position at the frame end, never one tick after an event (`docs/SIMULATION-CORE.md` §43).

## Out of scope

F-11 (rank system), F-12 and F-13 (decisions D-6 and D-8), F-14 (D-2), the balance (D-9), D-34 (the owner's), every judgement of a human.

## Non-negotiable rules

`CLAUDE.md`, `docs/TESTING.md`; nothing of the game in the repository; one cause per commit; a manifest deviation is a finding, never a number to adopt; the rate-equivalence
suite stays at 0 / 0 in all ten groups.

## Acceptance

By machine: every item fixed with a test that fails before the fix, at 30, 60 and 120 Hz, or named as a limit with the source's reason; one manifest commit per cause with
its result comparison; the campaign checks as before or every change explained; tier 4 green. What only a human can judge - whether the fixed behaviour looks like the
original's in a match - is named as such.

## Final report and N+1

Numbers with commit hashes, the tier that ran, what was not run, what no human has seen; continue the "Starting point" of the next brief, the stage document, the fidelity
register (F-06 to F-10, F-43), the decision register and the index.
