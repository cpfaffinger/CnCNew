N+1 handoff (Phase 27): Robustness on the network – desync diagnosis, rejoin, spectators, server replay

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md`, **`docs/MULTIPLAYER-PLAN.md`**, `docs/NETWORK-PROTOCOL.md`, `docs/DEDICATED-SERVER.md`, `docs/SIMULATION-CORE.md` (snapshot, state hash, replay) and the final report of Phase 26.
Prerequisite: Phase 26 is completed (eight client processes via the dedicated server, latency table measured).

## Honest status (starting point)

After Phase 26 a match runs as long as nothing goes wrong. For the three cases that are certain to occur in operation there is nothing yet:

- **Desync.** The server compares hashes and detects a deviation, but nobody can say *which object on which machine from which tick on* deviates. Without this tool every desync report of a player is worthless.
- **Connection loss.** A disconnected player drops out (Phase 26); he cannot come back.
- **Spectators and recording.** The server has all tick bundles but does not write them as a replay; spectators do not exist.

Verified numbers on the snapshot (old, re-measure): the world snapshot is JSON (`JsonState`), around 485–776 bytes per object, in the stress test 10–12 MB with 13,220 objects, restore 0.7 s (`docs/SIMULATION-CORE.md`). For the transfer to a rejoining client this is unmeasured.

## Brief (in this order)

1. **Reproduce the starting state**, re-measure the reference values of Phase 26.
2. **Desync diagnosis.**
   - Besides the total hash, the hash report of the clients carries the partial sums that the world keeps separately anyway (objects, timers, pending commands, random streams, per AI player the AI state from Phase 24, script state). At the first deviating tick the server reports *which partial sum* deviates on *which client*.
   - On a desync report every client immediately writes a diagnostics package: match description, all received tick bundles as a replay, its hash chain, a snapshot of the current tick and a table "object ID → object hash". None of it contains original data.
   - `tools/CnCNew.NetTool desync-diff <paket-a> <paket-b>`: replays the replay locally, finds the first deviating tick against both hash chains and names the deviating objects and fields (snapshot comparison field by field).
   - Test: a deliberately built-in error (test switch that changes a field of an object on one client) is found by the tool exactly down to tick, object and field.
3. **Snapshot for the transfer.** Measure first: size and write/read time of the session snapshot (world + AI) in the late 8-player match, raw and with `BrotliStream` (BCL). If the compressed JSON snapshot is below the limits set in the plan (proposal: ≤ 2 MB, writing + compressing ≤ 150 ms, reading ≤ 1 s), the format stays; otherwise a compact binary format with its own version field and the same fields (test: JSON and binary snapshot produce the same hash).
4. **Rejoin.**
   - At checkpoint ticks the server knows the hash confirmed by everyone. A client that rejoins receives the snapshot of a checkpoint **from a fellow player via the server**, loads it, computes the hash and compares it with that of the server – a wrong or manipulated snapshot is detected this way without the server having to simulate. After that the client replays the bundles since the checkpoint at catch-up speed.
   - Fallback path without a donor: replaying all bundles from tick 0 (only sensible as long as the catch-up speed from Phase 23 manages it in acceptable time; the limit is in the docs).
   - Recognition via a session token from the handshake; the deadline within which a seat is kept free is configuration. The rule for the dropped player from Phase 26 is extended accordingly (seat held / finally dropped), as system commands in the bundle.
   - Tests: disconnect and rejoin at three points in time (early, middle, late) with proxy disturbances; hashes identical afterwards; a corrupted donor snapshot is rejected.
5. **Spectators.** A spectator is a client without a player seat: it receives the same bundles delayed by a configurable period (default e.g. 120 s, against eavesdropping by fellow players), joins via the same snapshot path and sends no commands (the server discards them, counted). The display without an own player (fog, selection) is the subject of Phase 29; here the headless proof is enough.
6. **Server replay.** At the end of a match (also after an abort) the server writes the match description and all bundles in the replay format; `SimTool` and the client play the file and reach the hashes of the clients. AI commands are not in the bundle (the AI runs in the simulation, Phase 24); the replay therefore contains the AI slots of the match description, and playback lets the AI run along.
7. **Pause and votes.** Pause on request (number and duration limited), resume; everything as system commands or server state, none of it touches the simulation outside the bundles.
8. **Docs:** `docs/NETWORK-PROTOCOL.md`, `docs/DEDICATED-SERVER.md`, new `docs/NETWORK-DIAGNOSTICS.md` (diagnostics package, tool, procedure for a desync report), `docs/MULTIPLAYER-PLAN.md`, README, ROADMAP, IMPLEMENTATION-PLAN, `CLAUDE.md`.

## Bottlenecks and showstoppers of this phase

- **Completeness of the snapshot.** A rejoin uncovers every gap in the snapshot that a match running straight through never shows (state that lives only in memory). The test "load the snapshot at every checkpoint of a 60-minute match and continue playing, hash stays identical" runs for all factions.
- **Cost of the checkpoint.** Writing a snapshot must not make the donor stall visibly; measure the time per checkpoint and set it against the tick budget. If necessary, write the snapshot spread over several ticks from an immutable copy – never from the running world.
- **Hash report rhythm.** Too rarely: a desync is noticed late. Too often: bandwidth. The rhythm is measured and documented.

## Acceptance

A built-in desync is found exactly down to tick, object and field. A disconnected client rejoins and is hash-identical afterwards. A spectator follows a match with a delay. The server writes a replay that reproduces the hashes. The final report names numbers (snapshot sizes and times, rejoin duration per point in time, bandwidth of the hash reports) with commit hashes and what is not verified. At the end check `docs/handoffs/HANDOFF-PHASE28.md` and update the "starting point".
