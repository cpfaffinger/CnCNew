> **Stage:** release gates · **Milestone:** R4 Multiplayer release (and R1 for its single-player items 1, 2, 5–8; items 3 and 4 are S3-M7) · **Phase:** 30
> **Status:** planned, postponed behind Stage 4; its items are distributed to R1, S3-M7 and R4 (`docs/plan/PROGRAMME.md` §4, §6)
> **Predecessor:** HANDOFF-PHASE29.md · **Results in:** docs/INSTALLATION.md, docs/DEDICATED-SERVER.md
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 30): Patch integration, quality and shippable completion (postponed; originally the step after Phase 17)

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md`, `README.md`, `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, `docs/IMPLEMENTATION-PLAN.md` (§11 "Patch integration, quality and shippable completion" together with "Order and change rule"), `docs/MULTIPLAYER-PLAN.md`, `docs/MULTIPLAYER-FINDINGS.md`, `docs/DEMO-FINDINGS.md` and the final report of Phase 29.

## Why this brief is placed here

Until 5 October 2026 this was the step immediately after Phase 17 (`IMPLEMENTATION-PLAN.md`, then §10; `ROADMAP.md`, then §5). With the scope change of 5 October 2026 (multiplayer via dedicated server, 60 Hz, parameterised tick rate) the programme of Phases 18–29 comes before it: tick rate, fixed-point resolution and formats change every reference value and make savegames and replays unreadable. A package shipped before these changes would have to be broken afterwards. That is why the completion comes last and now also covers the server.

## Honest status (starting point – re-measure when this phase starts)

This section is a placeholder with the knowledge of 5 October 2026 and **will be overwritten by Phase 29 with the measured state**. What is known today:

- Much from Phases 15–17 is built but not accepted by a human (visual check of the world, sound, mouse cursor, texts, clicking through the shell, playability of the missions; commands-only agent 1 of 24). Whatever of this is still open when this phase starts belongs at the beginning of this brief – a shippable completion presupposes the acceptance.
- No community patch is integrated; `references/` contains GeneralsGameCode and GeneralsGamePatch for reading only.
- There is no installation package, no crash reporting, no licence/attribution overview.

## Brief (in this order)

1. **Reproduce the starting state** and rewrite this "starting point" section with measured numbers: build (0 warnings), all test programs, all audits, the reference values at 60 Hz, the state of the acceptances by humans (single player *and* multiplayer).
2. **Close the open acceptances from Phases 15–17 and 29** (with the human): every unchecked line in `docs/CLIENT-OBJECTS.md`, `docs/DEMO-FINDINGS.md`, `docs/MULTIPLAYER-FINDINGS.md` is accepted, fixed or named as a known limit with a reason.
   This includes the "Rest of Phase 17" at the end of `docs/handoffs/HANDOFF-PHASE17.md` (agent that wins skirmishes, AI economy and difficulty, clicking through), as far as it was not done before Phase 18.
3. **Catalogue the GeneralsGameCode fixes** by subsystem (simulation, AI, pathfinding, scripts, rendering, UI); understand every adopted fix, rewrite it and back it with a regression test and a source reference (file, function). Every fix that changes the simulation is a measured hash change with new reference values and – because all players need the same simulation – a new simulation version in the handshake.
4. **Import GeneralsGamePatch as its own overlay layer** (via `CnCNew.Import`, its own definition fingerprint); balance changes separately switchable. In multiplayer the chosen layer is part of the match description: a room names original or patch, and only clients with the same fingerprint join.
5. **Diagnostics:** crash report with a diagnostics package (client and server), structured logs, profiler switches, support bundle. None of it contains original data or leaves the machine without an explicit action of the human.
6. **Dependencies and legal matters:** `scripts/audit-vulnerable.ps1` for all projects including the server, document licences and attributions, prove the separation of original data, generated artefacts and own code in the package.
7. **Reproducible completion:** clean-room build on a fresh machine, fresh asset import, complete test matrix (including the rate-equivalence suite from Phase 23 and the network matrix from Phase 26), long-duration runs, package for the client (Windows) and the dedicated server (Windows, Linux) – automated and documented.
8. **Documentation for humans:** installation, import of one's own original installation, operating a server, troubleshooting (including "my match has diverged"), version and compatibility rule for savegames, replays and the network protocol from the first release on.

## Non-negotiable rules

Unchanged `CLAUDE.md` and `docs/MULTIPLAYER-PLAN.md` §6. In addition: from the first release on, every change to tick rate, fixed-point format, snapshot, replay or protocol format is a break for players and needs its own, justified decision of the project owner.

## Acceptance

A fresh machine turns source code and a local original installation, from documented prerequisites, reproducibly into a startable client and server package; the whole test matrix is green; no unresolved high-priority defects are open; single player (skirmish, campaign) and multiplayer (skirmish via dedicated server, up to eight players with AI slots) are accepted end to end by humans. The final report names numbers with commit hashes and explicitly what is not verified.
