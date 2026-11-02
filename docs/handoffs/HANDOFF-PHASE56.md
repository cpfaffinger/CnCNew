> **Stage:** 3 - Changes, 4 - Multiplayer, 1 - Port · **Milestone:** S1-M11, S3-M1, S4-M5 · **Phase:** 56
> **Status:** done by machine (2 November 2026, `71ae8b24`; begun at `fc464d62`)
> **Predecessor:** HANDOFF-PHASE55.md · **Successor:** HANDOFF-PHASE57.md · **Results in:** `docs/SKIRMISH-AI.md`, `docs/GAME-SHELL.md`, `docs/SIMULATION-CORE.md`, `docs/ACCEPTANCE-FINDINGS.md`
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 56): what needs no person, while the owner's next acceptance waits.

You work autonomously on this repository. First read `CLAUDE.md` (paragraph "The owner's first report"), `docs/ACCEPTANCE-FINDINGS.md` and `docs/plan/PROGRAMME.md` §6.

## Goal in one sentence

Everything a program can build or prove before a person sits down again, so that the owner's next run asks as little as possible.

## Starting point (measured)

The owner postponed the next acceptance on 2 November 2026. At `fc464d62`: gate 32 of 32, `Game.Tests` at 30 and 120 Hz green, the audit without a hit, pushed. 22 goals are open in `scripts/acceptance/accepted.json`.

## What is already done - do not build twice

The server, the protocol and the clients without a window carry a rejoin, watchers, a kick and votes; only the menus lack them (`docs/GAME-SHELL.md` §15). Terrain bridges are in `docs/SIMULATION-CORE.md` §68.

## Decisions that are fixed

The built-in AI stays the default (no. 9 of Phase 23), and it may be better than the original's walk, because it is Stage 3. No version of a format moves without a reason.

## Brief (in this order)

1. The computer's economy after SPEC-08: is "8 of 16 decided" a stalled AI or only a short clock? Measure, then fix what is really wrong.
2. The two open ends of the bridges: what falls from the air lands under a deck, and the cursor does not pick a deck.
3. The menus' rest of Phase 29: back into a running match, a watcher's way with the choice of fog, a button to remove a member, the start position box.
4. F-23's model states the simulation can now set, and F-33: the export without the original's program files. **Added on the way:** a sweep of what the source says is not simulated found subdual damage (F-108) and the combat drop (F-107), both built.
5. The cosmetic leftovers of the report, the cost of `StepBlockedMovers` and of the launch table, `scripts/perf-profile.ps1`, the clean room.
6. The open goals of `accepted.json`: move to `byMachine` what a program can prove.

### Item 1: what was measured, and the thresholds, committed before the fix

`SimTool ai-survey --seeds 4 --minutes 45` at `fc464d62`: 58 of 64 matches decided. So "8 of 16 decided" was mostly a short clock: Alpine Assault America against China is 26 units to 1 when the 25 minutes end. **But GLA wins 4 of 32 matches and stands still:** with 10,000 credits it starts nothing for three minutes on Death Valley, is defeated after 4.5 minutes and never has a barracks. The first barracks of a GLA player comes at about 115 s, against 43 to 53 s for China and America. Causes, seen in a throwaway log: its only worker stays reserved as a scout for 60 s after it arrived, it is sent only to the spot in front of the source and never to a legal spot it has not seen, and a second builder is made only after the first building stands.

Thresholds for the same survey after the fix: **T1** a GLA player's first barracks at most 60 s (median over the 32 GLA matches); **T2** no GLA player without a barracks; **T3** at least 58 of 64 decided; **T4** GLA wins at least 8 of 32 (reported, the balance is not judged: this is only that the defect is gone).

### The result (2 November 2026)

Every item is built. Item 1: the survey at the final state decides 62 of 64 matches, a GLA player's first barracks stands after 29 s and every GLA player has one, GLA wins 9 of 32: **T1 to T4 are met**, T3 and T4 only at the end of the phase and not by the AI's fix alone (`docs/SKIRMISH-AI.md` §7c). Item 2: a landing on a deck and the cursor on a deck. Item 3: a game that lost its connection comes back, the watcher's way, removing a member, start places. Item 4: the sweep of the data found much more than the brief named, and a program checking what a scene shows found more than the sweep: F-107 to F-110, §78. Item 6: 17 goals by a program, 26 open of 120: what they judge is a look or a sound.

**Found on the way, each by a program:** a weapon's `FireOCL` was never read, so no poison cloud, radiation field or firestorm existed on real data, while a test with a stand-in weapon said a firestorm rose (F-109); the beam of a weapon was not drawn (F-110); six kinds of button had their data and their modules and answered "not supported"; a flung body stuck to the ground; a saved value lay outside its object, and a restored world forgot it; a unit killed in a tick held up a mover at the end of the frame at 60 Hz and not at 30. The tier found one thing the phase had left: the fixture of `Import.Tests` from before the export left out documents.

**Measured:** tier 4 at `21cde75b` 57 of 58 steps ok (9,539 s); `import` failed on the fixture, fixed in `71ae8b24` and green alone, also with `--game-dir` against the real installation (350 files read, the export reproduced); both manifests identical, the rate-equivalence suite as named in `CLAUDE.md`. Campaign at 60 Hz: reachability clean 24/24 and 24/24, objective driver 24/24 and 24/24, audit 24 of 24 identical on a second run and after save/restore with end probes 13/24 and 16/24 (exit code 1 as before: `MD_CAMPEA_DEMO`).

**Not built, named:** the list that is now `HANDOFF-PHASE57.md`. **No human has seen or heard any of the phase's work.**

## Bottlenecks and showstoppers

A real-time step of a tier fails under load: nothing heavy beside `net-*` steps, and a failure there is run again alone before it is believed.

## Out of scope

The second physical machine and a real network (E1, D-10); what only a person can hear or judge.

## Non-negotiable rules

`CLAUDE.md`, `docs/TESTING.md`. A threshold is committed before its measurement.

## Acceptance

Tier 4 at the final state; every item fixed, proved by a program, or named with its reason.

## Final report and N+1

Numbers with commit hashes, the tier that ran, what was not run, what no human has seen; the registers of `docs/plan/` brought up to date.
