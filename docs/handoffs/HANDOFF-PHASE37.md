> **Stage:** 3 – Changes · **Milestone:** S3-M2 Spectator and observer · **Phase:** 37
> **Status:** current (starting state measured 17 October 2026 at `c2dcc4f`; not begun)
> **Predecessor:** HANDOFF-PHASE36.md · **Results in:** docs/SPECTATOR-MODE.md, docs/REPLAY-BROWSER.md, docs/MATCH-ANALYSIS.md, docs/GAME-HUD.md, docs/plan/STAGE-3-CHANGES.md
> **Side items:** the residue of S3-M6 (the long thinks of the computer players under `movement-budget`; the angry-mob rate case); the score screen's counters as the original's `ScoreKeeper` counts them (F-42)
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 37): the observer features of S3-M2 - free camera and the switch of view, production and research panels per player, an event ticker, and the
spectator of a human match from its replay - built on the spectator mode of Phase 23, the replay browser of Phase 35 and the analysis of Phase 36

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md` (paragraphs "Phase 23", "Phase 35" and "Phase 36"),
`README.md`, `docs/plan/PROGRAMME.md` (§5 order, §7 decisions), `docs/plan/STAGE-3-CHANGES.md` (S3-M2, S3-M3, S3-M4), `docs/SPECTATOR-MODE.md` (the observer HUD, where
every value comes from, its checklist §11), `docs/REPLAY-BROWSER.md` (the playback, the seeks, the view), `docs/MATCH-ANALYSIS.md` (the events with their place, the
samples) and `docs/GAME-HUD.md`. Prerequisite: Phase 36. This milestone touches no simulation and no manifest.

## Goal in one sentence

A person who watches - a match of computer players, or a replay of a match they played - follows it from the display alone: they move the camera freely, see the
match as one player saw it or everything, see what every player produces and researches, and are told of the moments that matter (first blood, buildings lost,
superweapons) as they happen; every part of it is checked by a machine and pinned by hashes.

## Starting point (measured, after Phase 36 – re-measure when this phase starts)

- **State:** `master` at `c2dcc4f`; Tier 4 with 55 of 55 steps ok at `c2dcc4f` (6,756 s; 0 warnings; all test programs; simulation, game, presentation and network tests
  at 30, 60 and 120 Hz; both manifests identical; the rate-equivalence suite; the long run; the client checks; every network step; `replay-browser` with the analysis;
  `patch-layer`; the staged scenes). Versions unchanged by Phase 36: `CommandTypes.Version` 17, replay 15, `WorldSnapshot` 10, `MissionSnapshot` 3, `SaveGame` 3, match
  description encoding 3, skirmish session state 3, protocol 6, transport 2, the replay file of the game layer 1; new: the analysis export `cncnew-match-analysis` schema 1.
- **The reference manifest** (`3b6fa80`, unchanged): retail reference match FactionGLA at tick 32,520, `6052051714309B17` / `84077720F511C1ED`; stress test
  `9EE9F730D124C3BD` / `F338463BE66D99C0`; 16 AI pairings 16 decided.
- **The analysis** (S3-M4, Phase 36, `docs/MATCH-ANALYSIS.md`): `MatchAnalyzer` on a running match or a replay played again (the same bytes from both); the events with
  their frame and place - losses with the killer's player, buildings, sales, captures, upgrades, sciences, special powers and superweapons with their target, defeats;
  samples every five seconds of game time on the frame grid; `MatchAnalysisScreen`; `SimTool match-analysis`. An hour of eight players: 136 to 138 s to play again and
  analyse, 3.0 MB of JSON. **This is the input of S3-M2's event ticker and panels.**
- **The score screen's counters** count by the owner of an object since `31b03c2` (F-42: not yet only the original's `SCORE` kinds); a screen shown again forgets its last
  click (`f0371ee`).
- **Campaign at 60 Hz (at `c2dcc4f`):** reachability clean 24/24 and 24/24; audit 24 of 24 identical on a second run and after save/restore with end probes 13/24 and
  15/24 (exit code 1 as before: `MD_CAMPEA_DEMO`); objective driver victory 24/24 and defeat 24/24.
- **Performance** (`scripts/perf-profile.ps1` with the binaries of `55870fb` and of `c2dcc4f` one after the other): every one of the ten runs with the same final hash on
  both sides; means within -3.6 % and +2.2 % (8 players: 881.9 against 854.3 µs per tick, 99 % 2.26 against 2.17 ms, maximum 124 against 124 ms). The attached effects
  cost 55 to 65 µs per tick of an eight-player match in the game layer (`docs/PRESENTATION-FX.md`).
- **Clean room:** the commit `c2dcc4f` cloned into a fresh directory with empty caches (1,126 files, nothing of `game/`, `.local/`, `references/`), built in
  143 s (259 packages, 0 warnings), the content exported afresh in 153 s (25,608 files, the known export), input manifest identical, the quick reference set 9 of 9,
  `Simulation.Tests` and `Data.Tests` with that content green, the client package (933 files, 318 MB) and both server packages (204 files, 81 and 83 MB) free of
  original data - every step ok.
- **Side items carried on:** the long thinks of the computer players under `movement-budget` (13.8 to 23.9 ms; the placement is no longer the cause, `docs/PERF-MOVEMENT.md`
  §11 - the other parts of the think are not broken down); the angry-mob rate case (narrowed in §11, cause not found); F-42; the GLA computer players that build no supply
  stash on an eight-player Twilight Flame (`docs/SKIRMISH-AI.md` §7).
- **Not verified, carried on:** no human has read a report, used the analysis screen, the replay list or a playback, or seen the attached effects; a second physical machine
  and a real network (E1, D-10); the owner's open decisions D-2 to D-8, D-10, D-17 to D-20, D-23 to D-33.

## What is already done – do not build twice

The spectator mode (`--spectate`, `SpectatorController`, `SpectatorHud`, `SpectatorStats`: money, income per minute, army by type, production queues, losses and kills,
power - from the events of every tick; the choice of one player's fog or all revealed; speed and pause), the replay playback with its seeks and the same observer HUD
(`ReplayPlayback`, Home/PageUp/PageDown), the analysis with its events by frame and place (`MatchAnalyzer`, `AnalysisEvent`: losses with the killer, buildings, upgrades,
sciences, powers and superweapons with their target, captures, defeats) and its screen, and `ShellScript` for a person at the menus.

## Decisions that are fixed

- Presentation, game layer and tools only: no simulation change; the reference and input manifests unchanged.
- What the ticker tells is computed from what the simulation already tells (the analysis' core and the observer statistics); a moment that cannot be had without a
  simulation change is named and left out.
- Every panel and the ticker are the same at 30, 60 and 120 Hz for the same match (times on the original-frame grid), and the same on two runs.

## Brief (in this order)

1. **Reproduce the starting state** (tier 4 unless Phase 36 ended with it on the same commit).
2. **Free camera and view switching** (S3-M2 (b)): a free camera for the observer (not bound to a player's start), the switch between every player's view and all
   revealed by key and button, kept across a seek of a replay.
3. **Panels per player** (S3-M2 (b)): production (queues and construction) and research (upgrades, sciences) of every player, from the observer statistics and the world.
4. **The event ticker** (S3-M2 (b)): first blood, buildings lost, superweapons, captures, a player defeated - from the analysis' core, with a click that moves the camera
   to the place where there is one.
5. **A human match from its replay** (S3-M2 (c)): the observer of a replay of the replay browser has all of the above; the network spectator (Phase 27/29) is named, not
   rebuilt.
6. **Frameworks instead of a human:** the observer's draw lists pinned by hashes in six resolutions; the ticker's lines of a recorded match the same at 30, 60 and 120 Hz
   and on two runs; a `ShellScript` person who opens a replay, switches views, follows a ticker line and seeks.
7. **Side items as time allows** (named in the header).
8. **Documents** and the next brief.

## Bottlenecks and showstoppers of this phase

The observer HUD lies over the original's control bar area: what does not fit the original's layouts is drawn by the game itself, as the analysis screen does, and
named. A ticker line needs a place on the map: losses carry the victim's place when its object was still in the world; a superweapon its target.

## Out of scope

The modern skin (S3-M5), a spectator on the network beyond what Phases 27 and 29 built, balance judgements, every judgement of a human.

## Non-negotiable rules

`CLAUDE.md`, `docs/TESTING.md`; nothing of the game in the repository (a replay of a real match is a file of the user, never a fixture); the manifests unchanged.

## Acceptance

By machine: the observer's draw lists pinned; the ticker the same at every rate and on two runs; a scripted person follows a replay; tier 4 green. What only a human
could judge - that a person names the players' intentions from the display alone (the acceptance of S3-M2) - is named as such.

## Final report and N+1

Numbers with commit hashes, the tier that ran, what was not run; continue the "Starting point" of the next brief, the stage document, the registers and the index.
