> **Stage:** 1 - Port · **Milestone:** S1-M11 · **Phase:** 55
> **Status:** done by machine (1 November 2026)
> **Predecessor:** HANDOFF-PHASE54.md · **Results in:** `docs/CLIENT-OBJECTS.md`, `docs/CLIENT-TERRAIN.md`, `docs/CLIENT-FX.md`, `docs/GAME-SHELL.md`
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 55): what the picture lacks (F-23, F-24, F-25), then the release candidate.

You work autonomously on this repository. First read `CLAUDE.md` (paragraph "Phase 54"), `docs/plan/PROGRAMME.md` §6 and §7.

## Goal in one sentence

Shadows, sky, weather, the shell map and the effects not drawn yet; then a package that passes the clean room with a real download.

## Starting point (measured, after Phase 54 - re-measure when this phase starts)

`docs/SIMULATION-CORE.md` §70.

## Brief (in this order)

1. F-23 to F-25. 2. F-30 (unlocks of the challenge), F-92 (team actions reach all instances), F-88 (the rate cases that still part). 3. `scripts/perf-profile.ps1`. 4. `scripts/clean-room.ps1` with the download of the patch.

## Acceptance

Tier 4; every rendering change looked at by machine and named for a human in `scripts/acceptance/catalogue.json`.

## Progress

Done (1 November 2026, the register's rest): F-92 (`docs/SIMULATION-CORE.md` §71), F-30 (`docs/CAMPAIGN-AND-SAVEGAMES.md` §2), F-25 (§72, `docs/SCRIPT-ENGINE.md` §7a, `docs/CLIENT-FX.md`, `docs/PRESENTATION-OBJECTS.md` 11a, `docs/SIM-JET-FLIGHT.md`), F-24 (`docs/GAME-HUD.md` §23, §73); F-91 judged not small and left open with its reason. The beacon raises `CommandTypes.Version` to 22 and the replay format to 21: both manifests want a commit of their own, with no result quantity moving.

Done (31 October 2026, `docs/CLIENT-SHADOWS.md`, `docs/CLIENT-TERRAIN.md`, `docs/CLIENT-FX.md`, `docs/GAME-SHELL.md`): stencil shadows and decals, cloud shadows, the sky box, the black border, snow and rain, the shell map, the soft water edge, light pulses, smudges, the user's own maps, credits that end; a script's pitch and zoom (the "no sky" of `MD_USA01` was a camera defect). Script outputs 76 of 92 displayed. Then F-92 (a team action reaches the newest instance), F-30 (the original has no unlocks: the record and medals in `SkirmishStats.ini`, portraits, the VS load screen), the rest of F-25 (82 of 91 used output types displayed), F-24 (diplomacy, beacons in a match of a server; the original has no chat in a local skirmish). **Left:** F-88 (two rate cases), F-91 (a mission's computer player has no `AIPlayer`: 1,859 lines of the original, every mission would change), `scripts/perf-profile.ps1`, the clean room with a real download.

## Closing measurements (1 November 2026, state `d799ce13`)

Tier 4 with 58 of 58 steps ok (8,024 s). Clean room: the commit cloned into a fresh directory with empty caches, built in 145 s (259 packages, 0 warnings), the patch fetched at its commit and the content exported afresh in 125 s (25,663 runtime files, the known export), input manifest identical, the quick reference set 9 of 9, `Simulation.Tests` and `Data.Tests` with that content green, the client package (937 files, 318.6 MB) and both server packages (204 files, 81.5 and 83.5 MB) free of original data. `scripts/perf-profile.ps1`: 8 players 731 µs per tick on average, 99 % 2.85 ms, longest tick 55.9 ms (141 ms before the budgeted movement), `MD_GLA05` 2.0 to 2.3 ms; all runs ok.

**What is left needs a person or a second computer:** the acceptance (`Start-Acceptance.cmd`, 103+ items), the second physical machine and a real network (D-10), the balance (D-9). The register has no entry without a disposition; F-88 and F-06 are named limits.
