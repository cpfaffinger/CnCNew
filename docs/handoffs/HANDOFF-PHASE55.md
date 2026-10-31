> **Stage:** 1 - Port · **Milestone:** S1-M11 · **Phase:** 55
> **Status:** next (starting state: the end of Phase 54)
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

Done (31 October 2026, `docs/CLIENT-SHADOWS.md`, `docs/CLIENT-TERRAIN.md`, `docs/CLIENT-FX.md`, `docs/GAME-SHELL.md`): stencil shadows and decals, cloud shadows, the sky box, the black border, snow and rain, the shell map, the soft water edge, light pulses, smudges, the user's own maps, credits that end; a script's pitch and zoom (the "no sky" of `MD_USA01` was a camera defect). Script outputs 76 of 92 displayed. **Left of the brief:** the rest of F-25 (`CAMEO_FLASH`, the camera along a path, the shaker, `WATER_CHANGE_HEIGHT*`, drawn paratroopers), chat in a local skirmish and diplomacy (F-24), F-30, F-92, F-88, `scripts/perf-profile.ps1`, the clean room with a real download.
