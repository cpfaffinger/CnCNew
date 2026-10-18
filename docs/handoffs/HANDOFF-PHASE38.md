> **Stage:** 3 – Changes · **Milestone:** S3-M5 Modern user interface · **Phase:** 38
> **Status:** done by machine (18 October 2026, the code at `c50b725`, the phase closed at the commit after `89490f5`); the side items: the late long tick broken down (`docs/PERF-MOVEMENT.md` §13), D-34 prepared as a measured proposal (`docs/SKIRMISH-AI.md` §7a, §7b); no human has used the modern skin
> **Predecessor:** HANDOFF-PHASE37.md · **Successor:** HANDOFF-PHASE39.md · **Results in:** docs/UI-MODERN.md (new), docs/GAME-SHELL.md, docs/GAME-HUD.md, docs/SPECTATOR-MODE.md, docs/MATCH-ANALYSIS.md, docs/plan/STAGE-3-CHANGES.md
> **Side items:** the late long tick under `movement-budget` (the command phase and the updates of one tick, `docs/PERF-MOVEMENT.md` §12); D-34 prepared for the owner (the GLA fix as a measured proposal, not adopted)
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 38): a modern skin beside the original one - first the design decision as a document, then the skin as a selectable profile of the same draw-list
contract, an options screen for what the original's does not reach, and the screens of Stage 3 and 4 in it

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md` (paragraphs "Publication and clean content", "Phase 29",
"Phase 36" and "Phase 37"), `README.md`, `docs/plan/PROGRAMME.md` (§5 order, §7 decisions - D-21), `docs/plan/STAGE-3-CHANGES.md` (S3-M5), `docs/UI-VANILLA-FIT.md`
(the original's layouts, the scaling), `docs/CLIENT-UI-RENDERING.md` (the draw list, the renderer, the font path), `docs/UI-WND.md`, `docs/GAME-SHELL.md` (the screens, §15
to §18), `docs/GAME-HUD.md`, `docs/SPECTATOR-MODE.md` §14 and `docs/MATCH-ANALYSIS.md` §4 (the screens of Stage 3 that were drawn by the game itself). Prerequisite: Phase 37.
This milestone touches no simulation and no manifest.

## Goal in one sentence

A player can choose a modern skin - laid out for the window's own resolution, with readable type and the screens of the new features designed for it - beside the
original's, which stays the default; every screen of both skins is pinned by hashes in six resolutions, and the choice is an option, never a change of the game.

## Starting point (measured, after Phase 37 – re-measure when this phase starts)

- **State:** `master` at `7574bd4`; Tier 4 with 55 of 55 steps ok at `7574bd4` (7,132 s; 0 warnings; all test programs; simulation, game, presentation and network tests
  at 30, 60 and 120 Hz; both manifests identical; the rate-equivalence suite; the long run; the client checks; every network step; `replay-browser` with the analysis and
  the observer; `patch-layer`; the staged scenes). Versions unchanged by Phase 37: `CommandTypes.Version` 17, replay 15, `WorldSnapshot` 10, `MissionSnapshot` 3,
  `SaveGame` 3, match description encoding 3, skirmish session state 3, protocol 6, transport 2, the replay file 1, the analysis export 1.
- **The reference manifest** (`3b6fa80`, unchanged): retail reference match FactionGLA at tick 32,520, `6052051714309B17` / `84077720F511C1ED`; stress test
  `9EE9F730D124C3BD` / `F338463BE66D99C0`; 16 AI pairings 16 decided.
- **The screens a skin must cover** and their pinned pictures: the shell (54 hashes in `baselines/screens.txt`), the replay list (54), the analysis (54), the network
  screens (336) and the network HUD (138), the observer HUD (144, per rate). The analysis pages and the observer HUD are drawn by the game itself (no layout of the
  original); everything else lies on the original's 800 x 600 layouts (`docs/UI-VANILLA-FIT.md`).
- **The observer** (S3-M2, Phase 37, `docs/SPECTATOR-MODE.md` §14): the event ticker from the analysis core, the views of every player, the research tab, the camera at
  an event - for a match and a replay. The score screen counts as the original's `ScoreKeeper` (F-42 closed).
- **Campaign at 60 Hz (at `7574bd4`):** reachability clean 24/24 and 24/24; audit 24 of 24 identical on a second run and after save/restore with end probes 13/24 and
  15/24 (exit code 1 as before: `MD_CAMPEA_DEMO`); objective driver victory 24/24 and defeat 24/24.
- **Performance** (`scripts/perf-profile.ps1` with the binaries of `c8cbfed` and of `7574bd4` one after the other): every one of the ten runs with the same final hash on
  both sides; means within -4.4 % and +2.0 % (8 players: 889.2 against 876.5 µs per tick). Under `movement-budget`, warmed up (`SimTool perf --warm-up`), the longest
  tick after 300 is 11.2 / 10.1 ms, late in the match in the command phase and the updates of one tick (`docs/PERF-MOVEMENT.md` §12).
- **Clean room:** the commit `7574bd4` cloned into a fresh directory with empty caches (1,130 files, nothing of `game/`, `.local/`, `references/`), built in 156 s (259
  packages, 0 warnings), the content exported afresh in 118 s (25,608 files, the known export), input manifest identical, the quick reference set 9 of 9,
  `Simulation.Tests` and `Data.Tests` with that content green, the client package (933 files, 318 MB) and both server packages (204 files, 81 and 83 MB) free of original
  data - every step ok.
- **Open, carried on:** F-43 (upgrades complete when bought), D-34 (the GLA AI's missing supply stash - a fix measured, not adopted), the angry-mob rate case, the tick
  maximum under the profile above 8 ms.
- **Not verified, carried on:** no human has watched the observer, read a report, used the replay list, or seen the attached effects; a second physical machine and a real
  network (E1, D-10); the owner's open decisions D-2 to D-8, D-10, D-17 to D-21, D-23 to D-34.

## What is already done – do not build twice

The draw-list contract (`UiDrawList`: fill, border, image, text, line, clip, custom) and its Stride renderer, the WND runtime on the original's layouts with its scaling
(`docs/UI-VANILLA-FIT.md`), the GDI font path, the transitions, `ShellScript` and the golden hashes of the shell (54), the replay list (54), the analysis screen (54), the
network screens (336) and HUD (138) and the observer (144), and the screens of Stage 3 that the game draws itself (the analysis pages, the observer HUD).

## Decisions that are fixed

- Presentation only: no simulation change; the reference and input manifests unchanged.
- The original skin stays the default until the owner decides (D-21); the modern one is a profile chosen in the options and on the command line, saved in the settings.
- Both skins draw through the same draw-list contract; no second renderer.
- Nothing of the original's art is copied into the repository; the modern skin uses no art of the game it does not load at run time.

## Brief (in this order)

1. **Reproduce the starting state** (tier 4 unless Phase 37 ended with it on the same commit).
2. **The design decision as a document before any pixel** (S3-M5 (a)): native-resolution layouts against integer scaling, the command bar at 16:9, typography and its
   sizes, colours and contrast, what stays the original's (cameos, portraits); written to `docs/UI-MODERN.md` and committed first.
3. **The skin as a profile** (S3-M5 (b)): a skin interface the shell, the HUD, the observer and the analysis screens draw through; the original skin as its first
   implementation (pictures unchanged - all existing hashes stay), the modern one beside it; `--skin original|modern`, a setting.
4. **An options screen** (S3-M5 (c)) for what `OptionsMenu.wnd` does not reach: display mode, aspect ratio, text scaling, audio devices, the hotkey view.
5. **The screens of Stage 3 and 4 in the modern skin** (S3-M5 (d), (e)): the lobby and the in-game indicators of Phase 29, the replay list, the analysis, the observer.
6. **Frameworks instead of a human:** golden hashes of both skins in six resolutions; a `ShellScript` person who switches the skin in the options and walks shell →
   skirmish → result → analysis → replay without a developer argument; a check that the original skin's hashes are unchanged.
7. **Side items as time allows** (named in the header).
8. **Documents** and the next brief.

## Bottlenecks and showstoppers of this phase

The original's layouts are 800 x 600; a native layout needs its own geometry for every screen - build the shell's way first (main menu, skirmish setup, score, options),
then the rest. A text measure that differs from the renderer's moves every centred label: measure with the font the renderer draws.

## Out of scope

Changes of the simulation, balance, localisation beyond what the original's text files hold, every judgement of a human.

## Non-negotiable rules

`CLAUDE.md`, `docs/TESTING.md`; nothing of the game in the repository; the manifests unchanged; the original skin's pictures unchanged.

## Acceptance

By machine: both skins pinned in six resolutions; a scripted person switches the skin and walks the shell; the original's hashes unchanged; tier 4 green. What only a
human could judge - that the modern skin is better to read and use than the original, the acceptance of S3-M5 - is named as such.

## Final report and N+1

Numbers with commit hashes, the tier that ran, what was not run; continue the "Starting point" of the next brief, the stage document, the registers and the index.
