> **Stage:** release gates · **Milestone:** R4 Multiplayer release (and R1 for its single-player items 1, 2, 5–8; items 3 and 4 are S3-M7) · **Phase:** 30
> **Status:** planned, postponed behind Stage 4; its items are distributed to R1, S3-M7 and R4 (`docs/plan/PROGRAMME.md` §4, §6)
> **Predecessor:** HANDOFF-PHASE29.md · **Results in:** docs/INSTALLATION.md, docs/DEDICATED-SERVER.md
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 30): Patch integration, quality and shippable completion (postponed; originally the step after Phase 17)

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md`, `README.md`, `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, `docs/IMPLEMENTATION-PLAN.md` (§11 "Patch integration, quality and shippable completion" together with "Order and change rule"), `docs/MULTIPLAYER-PLAN.md`, `docs/MULTIPLAYER-FINDINGS.md`, `docs/DEMO-FINDINGS.md` and the final report of Phase 29.

## Why this brief is placed here

Until 5 October 2026 this was the step immediately after Phase 17 (`IMPLEMENTATION-PLAN.md`, then §10; `ROADMAP.md`, then §5). With the scope change of 5 October 2026 (multiplayer via dedicated server, 60 Hz, parameterised tick rate) the programme of Phases 18–29 comes before it: tick rate, fixed-point resolution and formats change every reference value and make savegames and replays unreadable. A package shipped before these changes would have to be broken afterwards. That is why the completion comes last and now also covers the server.

## Honest status (starting point after Phase 29, 14 October 2026 – re-measure when this phase starts)

**State:** `origin/master` = `a3cfef6` and the documentation commits after it; working tree clean. Tier 4 with 49 of 49 steps ok at the state of `a3cfef6` (5,678 s; 0 warnings; all test programs; simulation, game, presentation and network tests at 30, 60 and 120 Hz; both manifests identical; the rate-equivalence suite; the long run; the client checks; `check-match`; `session-check`; `net-probe`; `net-match`, `net-robust`, `net-join`, `net-cross-os`, `net-load`, `server-package`, `server-package-linux`; the new `net-lobby` and `net-acceptance`; the staged scenes) - the second run: the first stopped at the audit of the packages, which found five advisories published since Phase 28 against `System.Security.Cryptography.Xml` and `.Pkcs` 10.0.12 of the Stride tooling (pinned to 10.0.13 in `a3cfef6`). Versions: protocol **5**, transport 2;
`CommandTypes.Version` 17, replay 15, match description encoding 2, skirmish session state 2, `WorldSnapshot` 10, `MissionSnapshot` 3, `SaveGame` 3,
`SkirmishSaveGame` 1, desync package 1. The yardstick (`baselines/reference-manifest.json`, 60 Hz) is untouched since `4cbf535`: retail reference match FactionGLA
at tick 32,520, final hash `6052051714309B17`, chain `84077720F511C1ED`; stress test `9EE9F730D124C3BD` / `F338463BE66D99C0`; 16 pairings 14 decided.
Compatibility baseline 713.

**What exists for multiplayer** (all of it on one machine): the dedicated server as a package for Windows and Linux (Phase 28); the menus' way to a room on a
server and back, "Host game", the in-match indicators, the latency measurement (Phase 29, `docs/MULTIPLAYER-FINDINGS.md`).

**The acceptances by humans are all open.** Nothing of Phases 15 to 17 (world, sound, cursor, texts, shell, missions), nothing of Phase 23 (60 Hz) and nothing
of Phase 29 (the multiplayer screens, the indicators, how delay feels, a match between two people) has been seen, heard or played by a human. For Phase 29 the
owner replaced the acceptance by frameworks; `docs/MULTIPLAYER-FINDINGS.md` says per line what a program checked and what only a human could judge (sections 4
and 5). Item 2 of this brief ("close the open acceptances ... with the human") therefore starts from zero on the human side - or, if the owner keeps the rule of
Phase 29, is done the same way: `scripts/acceptance.ps1` for the single-player items, `scripts/check-net-lobby.ps1`, `scripts/check-net-acceptance.ps1`,
`scripts/check-net-endurance.ps1` and `scripts/measure-net-latency.ps1` for multiplayer.

**Still open from before:** the second physical machine with a different CPU and a real network (E1, D-10); the decisions D-17 to D-20 and D-23 to D-30; no
community patch is integrated (`references/` is read only); no installation package for the client beyond `scripts/install.ps1`, no crash reporting. From
Phase 29: a window client cannot come back into a running match (the server and the protocol can); no watcher's way through the menus; no button to remove a
member; no start position box; the texts of the multiplayer screens and indicators are English only.

**What the frameworks of Phase 29 found** and will find again when something breaks: a match that ended in the shell threw in the client (fixed); a client that
closed its connection before the server had ended the match was counted as lost in a running match (fixed: it waits up to three seconds for the server's end).
A shippable completion should keep `net-lobby` and `net-acceptance` in its gate: they are the only checks that play a match through the menus in the real client.

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
