> **Stage:** 3 – Changes · **Milestone:** S3-M7 Bug-fix and balance layers (completion) · **Phase:** 32
> **Status:** current (starting state measured 14 October 2026 at `dfcd72e`; not begun)
> **Predecessor:** HANDOFF-PHASE30.md · **Results in:** docs/PATCH-LAYER.md, docs/GGC-FIXES.md, docs/GGC-FIXES-CATALOGUE.md, docs/SIMULATION-CORE.md, baselines/compatibility-patch.json (new)
> **Side items:** R1/R4 – the machine acceptance of `docs/ACCEPTANCE-MACHINE.md` kept green
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 32): the patch layer playable, and the review of the community's logic fixes

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md` (paragraph "Phase 30"), `README.md`,
`docs/plan/PROGRAMME.md` (§5 order, §7 decisions D-31 to D-33), `docs/plan/STAGE-3-CHANGES.md` (S3-M7), `docs/PATCH-LAYER.md`, `docs/GGC-FIXES.md`,
`docs/GGC-FIXES-CATALOGUE.md`, `docs/TYPED-DEFINITIONS.md`, `docs/PHASE3-COMPATIBILITY.md`, `docs/SIMULATION-CORE.md` and the final report of Phase 30.
Prerequisite: Phase 30 (the rule profile, the patch layer's import, the fix catalogue).

**Order.** By `PROGRAMME.md` §5 the simulation-touching Stage 3 work runs S3-M1 (AI) → S3-M6 (pathfinding) → S3-M7. This brief continues S3-M7 directly
because Phase 30 began it and left a concrete work list; the owner may put S3-M1 first - then this brief waits, unchanged.

## Goal in one sentence

The core of GeneralsGamePatch compiles in this port's strict compiler and plays a match - a layer with its own definition fingerprint, its own compatibility
baseline and a measured difference to the original - and every CRC-gated fix of GeneralsGameCode is reviewed: adopted behind the rule profile, or rejected with
its reason.

## Starting point (measured, after Phase 30 – re-measure when this phase starts)

**State:** `origin/master` = `dfcd72e` and the documentation commits after it; working tree clean. Tier 4 with 53 of 53 steps ok at `dfcd72e` (6,602 s; 0 warnings; all test programs; simulation, game, presentation and network tests at 30, 60 and 120 Hz; both manifests identical; the rate-equivalence suite with 0 / 0 in all ten groups - 689 cases, 4,808 quantities; the long run; the client checks; `check-match`; `session-check`; `net-probe`; `net-match`, `net-robust`, `net-join`, `net-cross-os`, `net-load`, `net-lobby`, `net-acceptance`, `server-package`, `server-package-linux`; the new `diagnostics`, `client-package`, `licences` and `ggc-catalogue`; the staged scenes). Versions unchanged by Phase 30: protocol 5, transport 2; `CommandTypes.Version` 17, replay 15, match description encoding 2, skirmish session state 2, `WorldSnapshot` 10, `MissionSnapshot` 3, `SaveGame` 3, `SkirmishSaveGame` 1, desync package 1. The reference manifest is untouched (retail reference match FactionGLA at tick 32,520, `6052051714309B17` / `84077720F511C1ED`; stress test `9EE9F730D124C3BD` / `F338463BE66D99C0`; 16 pairings 14 decided); the input manifest changed in one entry in `5a62f8c` (the catalog's rule profile). Compatibility baseline 713.

**The rule profile and the fixes** (`docs/GGC-FIXES.md`): three fixes adopted (`poison-kill-credit`, `flame-kill-credit`, `no-sabotage-of-unfinished-or-sold`); the catalogue has 354 markers, 96 CRC-gated, of which 13 are reviewed and **83 are not** (the review column of `docs/GGC-FIXES-CATALOGUE.md` is empty for them). Measured at 60 Hz: the reference match is the same under `--rules fixes` (fingerprint `E4E8D95BC0A8E1C3`, chain `84077720F511C1ED`); the AI match ToxinGeneral against InfantryGeneral on Alpine Assault ends `86D86CDFDEA40971` instead of `3C7D25C4459BD941`.

**The patch layer** (`docs/PATCH-LAYER.md`): `AssetTool import-layer auto game/content-patch --patch references/generals-game-patch/Patch104pZH` imports 25,636 runtime files (338 replaced, 28 added; patch commit `6c7f40d6af6a4babb77a33a4502ec7656446c13e`). `IniTool compat-check game/content-patch baselines/compatibility.json` stops at the first error: `Data/INI/ParticleSystem.ini(12399,3): Unexpected trailing text '0 5' after color`; a probe that let those four lines through stopped next at `Data/INI/Object/AirforceGeneral.ini(6527,3): Unknown module 'StatusBitsUpgrade'`; a text search found `SabotageSupplyDropzoneCrateCollide` as the only other module the shipped data does not use. Nothing after those is known.

**Also measured at the end of Phase 30:** `NetTool probe-suite` 48 of 48 runs hash-identical on all clients in every tick and equal to their replay (673 s). The matrix of Phases 26 to 29 (eight client processes - six players, two watchers, two computer players - on Twilight Flame through the UDP proxy, 100 s a line, delay 10/40/80/150 ms x jitter 0/10/30 ms x loss 0/1/3 %): **36 of 36 lines hash-identical**, 15,611 orders, no resync, nobody slowed; against Phase 29 order to execution +0.2 ms (median) and -2.1 ms (99 %), bandwidth -1 B/s up and -14 B/s down on average over the lines. Campaign at 60 Hz: reachability clean 24/24 and 24/24; audit 24 of 24 identical on a second run and after save/restore with end probes 13/24 and 15/24 (exit code 1 as in Phases 28 and 29: `MD_CAMPEA_DEMO` names a map the master installation does not have); objective driver victory 24/24 and defeat 24/24 - as before the phase. Performance: `scripts/perf-profile.ps1` with the binaries of `66b5f18` and of `dfcd72e` in the same hours: every one of the ten runs with the same final hash on both sides; means within -3.9 % and +1.2 % (8 players: 865.5 against 860.3 µs per tick, 99 % 2.19 against 2.17 ms, maximum 150 against 149 ms, 8,443 bytes per tick on both sides) - the rule profile's checks cost nothing measurable in the original profile. Clean room: the commit `dfcd72e` cloned into a fresh directory with empty NuGet and tool caches, built in 120 s (259 packages fetched, 0 warnings), the content exported afresh from the Steam installation in 268 s (25,608 runtime files, the known export), input manifest identical, the quick reference set 9 of 9 identical, `Simulation.Tests` and `Data.Tests` with that content green, the client package (933 files, 317 MB) and both server packages (204 files each, 81 and 83 MB) built and free of original data - every step ok (15 October 2026). The first run found the empty-file defect of the separation check (`dfcd72e`).

**Open from before, unchanged:** every acceptance by a human (`docs/ACCEPTANCE-MACHINE.md`); a second physical machine (E1, D-10); decisions D-2 to D-8, D-17 to D-20, D-23 to D-33.

## What is already done – do not build twice

- The rule profile (`src/CnCNew.Simulation/Rules/RuleProfile.cs`, `TemplateCatalog.Rules`, `--rules`), three adopted fixes and their tests
  (`tests/CnCNew.Simulation.Tests/Tests.GameFixes.cs`), the generated catalogue with its review column (`scripts/ggc-fix-catalogue.py`, table `REVIEW`).
- The overlay of the import (`GameFileSystem.OpenInstallation(root, overlay)`, `RuntimeContentImporter.ImportAsync(..., layer:)`, `GeneralsGamePatchLayer`,
  `AssetTool import-layer`), the manifest's layer options, the W3D remainder rule.
- The match description and the room compare the definition fingerprint: a layer that compiles is already refused by a client without it.

## Decisions that are fixed

- The original profile and the original content are the yardstick: the reference and input manifests never change because of a fix or the layer
  (`docs/GGC-FIXES.md` §3).
- A fix is adopted only behind the rule profile, with new state written only when set (Phase 30).
- Open, with the default this brief uses: D-32 (which fixes a release offers - default: all adopted fixes in the profile `fixes`, the original stays the default
  profile), D-33 (the patch layer for players - default: not offered by the installer until a human or the owner says so).

## Brief (in this order)

1. **Reproduce the starting state** (tier 4 unless Phase 30 ended with it on the same commit; `AssetTool import-layer` into `game/content-patch`).
2. **The colour key lines.** A compatibility case of its own for a colour key with tokens after the frame (`ParticleSystemTemplate::parseRGBColorKeyframe` reads
   colour and frame and never looks at the rest): explicit, counted, in a baseline of the layer (`baselines/compatibility-patch.json`), never silent.
3. **The two modules** `StatusBitsUpgrade` and `SabotageSupplyDropzoneCrateCollide` (read `references/ea-generals-zero-hour` and GeneralsGameCode): catalogue
   schema, binding, implementation, tests, entry in `docs/SIMULATION-CORE.md`. The original content never uses them, so the manifests stay.
4. **Compile the layer to the end**: every further finding is a compatibility case or a module, one cause per commit; then `IniTool compat-check` with the
   layer's baseline and a real-data test that the layer's definitions compile.
5. **Play it**: `SimTool reference-match` and the 16 AI pairings against `game/content-patch`; a match on a server with two clients of the layer, and one client
   without it refused by `Definitions`. Measure and document the difference to the original (winners, game time); balance is not judged.
6. **Review the 83 CRC-gated fixes** of the catalogue: for each, whether this port reproduces the defect; adopt the ones it does behind the profile (procedure of
   `docs/GGC-FIXES.md` §3), reject the rest with the reason in `REVIEW`. Every adopted fix: a test under both profiles at 30, 60 and 120 Hz, and an AI match
   measured under both profiles.
7. **Documents**: `docs/PATCH-LAYER.md`, `docs/GGC-FIXES.md`, the catalogue (`--check`), `docs/COMPATIBILITY.md` if a format moves.

## Bottlenecks and showstoppers of this phase

- The compiler stops at the first error: the size of step 4 is unknown until it is done.
- The patch's converted parts (Photoshop textures, Blender models, the string table) stay outside the layer: a patched object may reference a texture the layer
  does not have - a missing texture is counted by the renderer, never invented.

## Out of scope

S3-M1 (AI), S3-M6 (pathfinding), the human acceptance of R1/R4, a second machine, the converted parts of the patch, the patch's language and audio bundles.

## Non-negotiable rules

`CLAUDE.md`, `docs/TESTING.md`; nothing of the patch and nothing compiled from it is committed (only checksums, names, counts); the manifests are the original's.

## Acceptance

The layer's definitions compile with its own counted baseline; a reference match and the AI pairings run on it deterministically (twice identical); a client
of the layer and one without it are kept apart by the server; every CRC-gated marker of the catalogue has a review entry; tier 4 green. What only a human could
judge (whether the patch plays as its authors meant) is named as such.

## Final report and N+1

Numbers with commit hashes, the tier that ran, what was not run; continue the "Starting point" of the next brief, the stage document's milestone status, the
decision register and the index.
