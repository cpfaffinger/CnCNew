> **Stage:** 3 – Changes · **Milestone:** S3-M3 Replay browser · **Phase:** 35
> **Status:** done by machine (16 October 2026, at `STATE35`); the side items not done (`docs/REPLAY-BROWSER.md`, `docs/PERF-MOVEMENT.md` §10) · **Successor:** HANDOFF-PHASE36.md
> **Predecessor:** HANDOFF-PHASE34.md · **Results in:** docs/GAME-SHELL.md, docs/CAMPAIGN-AND-SAVEGAMES.md, docs/REPLAY-BROWSER.md (new), docs/plan/STAGE-3-CHANGES.md
> **Side items:** the residue of S3-M6 (the rate-equivalence case of the angry mob under `movement-budget`; the tick maximum of the profile)
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 35): the client records every skirmish as a replay, lists the replays in the shell and plays them back with pause, speed and seeking

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md` (paragraphs "Phase 24", "Phase 27", "Phase 30" and
"Phase 34"), `README.md`, `docs/plan/PROGRAMME.md` (§5 order, §7 decisions), `docs/plan/STAGE-3-CHANGES.md` (S3-M3, S3-M2), `docs/GAME-SHELL.md` (the load/replay
submenu), `docs/CAMPAIGN-AND-SAVEGAMES.md` §8 (the skirmish savegame and the replay format 15), `docs/SIMULATION-CORE.md` §45 (match description, `SkirmishReplay.Play`)
and `docs/NETWORK-DIAGNOSTICS.md` (the server replay). Prerequisite: Phase 34. The simulation-touching work of Stage 3 (S3-M1, S3-M6, S3-M7) is done; this milestone
touches no simulation and no manifest.

## Goal in one sentence

A player finds every skirmish they played - locally or through a server - in a replay list of the shell, sees whether each file can be played and why not, and plays
it back to the same result with pause, speed, seeking and the choice of whose view to show.

## Starting point (measured, after Phase 34 – re-measure when this phase starts)

- **State:** `master` at `b6b5764`; Tier 4 with 54 of 54 steps ok at `b6b5764` (6,363 s; 0 warnings; all test programs; simulation, game, presentation and network tests at 30, 60 and 120 Hz; both manifests identical; the rate-equivalence suite under the original with 0 / 0; the long run; the client checks; every network step; `patch-layer`; the staged scenes). Versions: `CommandTypes.Version` 17, replay 15, `WorldSnapshot` 10, `MissionSnapshot` 3, `SaveGame` 3, match
  description encoding 3, skirmish session state 3, protocol 6, transport 2.
- **The reference manifest** (`3b6fa80`): retail reference match FactionGLA at tick 32,520, `6052051714309B17` / `84077720F511C1ED`; stress test `9EE9F730D124C3BD` /
  `F338463BE66D99C0`; 16 AI pairings 16 decided. Phase 34 changed it twice, each with one cause: F-34 (`3d0d276`: five runs, four pairings end earlier with the same
  winners) and F-38 (`3b6fa80`: the chain hashes of two mission runs); F-39 changed nothing.
- **The movement profile `movement-budget`** (S3-M6, Phase 34; `docs/PERF-MOVEMENT.md` §9 and §10, `docs/SIMULATION-CORE.md` §52): a path queue with 60,000 cells per
  frame of the original and one search per group order, an option of the rule profile; **not the default** (D-3 open). In the 60 Hz gate the mean is 15 % lower and the
  longest tick after the first 300 falls from 123–128 ms to 13.7–13.8 ms - **the target of 8 ms is missed**; what remains is the computer players' placement checks, the
  command phase and the queue's own share. The rate-equivalence suite under the profile: 9 of 10 groups; one case (`Boss_InfantryAngryMobNexus`, 4 quantities, the 30 Hz
  run walks 31 units further) outside, cause not found. In the AI survey under the profile the GLA wins 2 instead of 9 of 80 matches (reported, not judged).
- **The rule profile** has nineteen fixes (`GameFix` 1 to 19); of the 96 CRC-gated markers 26 are adopted, 17 equivalent, 51 not applicable, 2 rejected. The fidelity
  register has F-34, F-38 and F-39 closed.
- **What exists for replays:** replay format 15 with the match description in its header (`ReplayFormat`, `SkirmishMatchSession.ToReplay`, `SkirmishReplay.Play` - a
  replay played as a match with the computer players deciding again, reporting the first differing command), the server's replay files (`--replay-dir`,
  `NetTool replay-check`), the main menu's load/replay submenu with the replay button disabled, the skirmish savegame (`SkirmishSaveGame`, one slot), the spectator
  mode (`--spectate`, `docs/SPECTATOR-MODE.md`).
- **Not verified, carried on:** no human has watched a profile, played on the patch layer, seen an adopted fix or watched a group march under the movement profile; a
  second physical machine and a real network (E1, D-10); the owner's open decisions D-2 to D-8, D-10, D-17 to D-20, D-23 to D-33.

## What is already done – do not build twice

The replay format and its checks (versions, fingerprints, damaged files refused), `SkirmishReplay.Play`, the server replay, the savegame store and its list
(`SaveGameStore`, the load screen), the spectator HUD, `ShellScript` and the screen hash tests (`NetScreenTests` as the model for screens driven by a script).

## Decisions that are fixed

- A replay is bound to its simulation version and its profile fingerprint; there is no migration before the first release (D-31 open for after it).
- Presentation and game layer only: no simulation change, the reference and input manifests unchanged.

## Brief (in this order)

1. **Reproduce the starting state** (tier 4 unless Phase 34 ended with it on the same commit).
2. **Recording:** the client writes every skirmish it plays (local and joined) as a replay into a user directory (beside the savegames; `--settings-dir`), with name,
   date, map, players, outcome and duration readable without playing it; a match that ends by a desync or a drop is kept and marked.
3. **The list:** the disabled replay button of the main menu becomes the entry to a list on the original's layout, sorted, with the compatibility state of each file
   (simulation version, fingerprints, rate, rule profile) and the reason when it cannot be played; delete and rename.
4. **Playback:** pause, speed (the presentation's), seek to a checkpoint (restore from the nearest snapshot, then run forward - a replay keeps snapshots at a fixed
   game-time interval, or the browser builds them on the first playback), the view of one player or all revealed (S3-M2), the result screen at the end equal to
   the recorded outcome.
5. **Frameworks instead of a human** (`docs/ACCEPTANCE-MACHINE.md`): a shell script records a match, finds it in the list, plays it to the end and seeks; screen hashes
   of the list in six resolutions; the playback reaches the recorded final hash at every seek; an incompatible file (other build, other profile, damaged) is refused
   with its reason - each at 30, 60 and 120 Hz where a rate is involved.
6. **Side items as time allows:** the angry-mob case of the rate suite under `movement-budget`; the computer players' placement checks under a budget within the movement
   profile (the next step towards 8 ms; an AI change within the profile, with its own survey).
7. **Documents** and the next brief.

## Bottlenecks and showstoppers of this phase

A replay of a network match is recorded by the client from the bundles it played (or taken from the server's file): both must give the same match. Seeking must not
change the result - the snapshot is the truth, as in a mission.

## Out of scope

Match analysis with graphics (S3-M4), a modern skin (S3-M5), making the movement profile the default (D-3), every judgement of a human.

## Non-negotiable rules

`CLAUDE.md`, `docs/TESTING.md`; nothing of the game in the repository (a replay of a real match is a file of the user, never a fixture); the manifests unchanged.

## Acceptance

By machine: a scripted session records, lists, plays to the same final hash, seeks to three checkpoints with the same hash at each and refuses three kinds of
incompatible file with the right reason; the screen hashes are stable; tier 4 green. What only a human could judge - that the list is easy to read and the playback
controls feel right - is named as such.

## Final report and N+1

Numbers with commit hashes, the tier that ran, what was not run; continue the "Starting point" of the next brief, the stage document, the registers and the index.
