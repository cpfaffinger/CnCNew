> **Stage:** 3 - Changes, 4 - Multiplayer, 1 - Port · **Milestone:** S1-M11, S3-M1, S4-M5 · **Phase:** 56
> **Status:** in progress (begun 2 November 2026 at `fc464d62`)
> **Predecessor:** HANDOFF-PHASE55.md · **Results in:** `docs/SKIRMISH-AI.md`, `docs/GAME-SHELL.md`, `docs/SIMULATION-CORE.md`, `docs/ACCEPTANCE-FINDINGS.md`
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
4. F-23's model states the simulation can now set, and F-33: the export without the original's program files.
5. The cosmetic leftovers of the report, the cost of `StepBlockedMovers` and of the launch table, `scripts/perf-profile.ps1`, the clean room.
6. The open goals of `accepted.json`: move to `byMachine` what a program can prove.

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
