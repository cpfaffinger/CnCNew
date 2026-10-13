> **Stage:** 4 – Multiplayer · **Milestone:** S4-M5 Multiplayer in the client, latency perception, acceptance with humans · **Phase:** 29
> **Status:** next – starting point continued on 13 October 2026 after Phase 28 (section "Starting point after Phase 28"), not begun; the first phase whose brief asks for humans
> **Predecessor:** HANDOFF-PHASE28.md · **Successor:** HANDOFF-PHASE30.md · **Results in:** docs/MULTIPLAYER-FINDINGS.md (new), docs/GAME-SHELL.md, docs/GAME-HUD.md, docs/PRESENTATION-OBJECTS.md
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 29): Multiplayer in the client – shell, lobby, in-game indicators, latency perception, acceptance with humans

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md`, **`docs/MULTIPLAYER-PLAN.md`**, `docs/GAME-SHELL.md`, `docs/GAME-HUD.md`, `docs/PRESENTATION-INPUT.md`, `docs/PRESENTATION-OBJECTS.md`, `docs/NETWORK-PROTOCOL.md`, `docs/DEDICATED-SERVER.md` and the final report of Phase 28.
Prerequisite: Phase 28 is completed. This phase is the first of the programme whose acceptance is **not possible without humans**.

## Honest status (starting point)

Everything so far is verified headless. A human cannot start a network match yet: the "Multiplayer" button in the main menu is visible but disabled (`docs/GAME-SHELL.md`). On 5 October 2026 the skirmish setup showed team and colour selection but did not pass them on (`MatchSetup` knew only map, own faction and opponent list); Phases 24 and 25 extended the match description by teams, colours and start positions and turned the setup into the local room – first check what of this really arrives in the user interface. In the game there is no indicator for connection state, waiting players, dropped players or a desync.

Existing original layouts in the imported content (`Window/Menus/`, read only, nothing into the repo): `LanLobbyMenu.wnd`, `LanGameOptionsMenu.wnd`, `LanMapSelectMenu.wnd`, `NetworkDirectConnect.wnd`, `DisconnectScreen.wnd`, `ObserverQuit.wnd` as well as the GameSpy/WOL layouts (do not use: no GameSpy in scope).

On latency: a player's command is executed only in the tick that the server assigns. The delay is measured in Phase 26. How it *feels* depends on what the client shows immediately – and so far that is coupled to the execution (acknowledgement sound, target marker and rejection came with the synchronous answer of the simulation; Phase 24 made the path two-stage).

### Starting point after Phase 28 (13 October 2026, state `c1946aa`; re-measure)

This section continues the starting point above with what Phase 28 left; where the two differ, this one is newer. Log of Phase 28:
`docs/NETWORK-PROTOCOL.md` §20 (the sealed transport, the server's key, limits, the room password, the player key, the kick, orders nobody can give, who decides)
and §21 (measurements); `docs/DEDICATED-SERVER.md` §9 (threat model, limits, fuzzing), §10 (operation), §11 (capacity and endurance), §12 (versions), §13 (the
package); `docs/SIMULATION-CORE.md` §49 (what a player's command may say); register E1, E19 in `docs/MULTIPLAYER-PLAN.md` §4; decisions D-5 (closed), D-17,
D-19, D-20, D-23 and the new D-24 to D-26 in `docs/plan/PROGRAMME.md` §7.

**The prerequisite holds on one machine.** A server with an open port exists as a package for Windows and Linux; its connection is authenticated and encrypted,
what a stranger or a member may cost is bounded and counted, and it ran for nine hours under load without growing or hanging. **No second computer has
taken part, nothing has crossed a cable, and no hostile load came from outside** (E1, D-10). No human has seen any of it.

**What exists, by name** (so that this phase builds on it instead of beside it):

- **Protocol version 4, transport version 2.** `Hello` carries the room verifier and the player key; `PasswordChallenge` (35), `PasswordResponse` (36: the
  password's answer and the key's signature), `Kick` (37); reject reasons `BadPassword`, `Kicked`, `BadIdentity`, `Banned` besides the older ones (`ProtocolVersion`,
  `SimulationVersion`, `FormatVersion`, `Definitions`, `ModifiedBuild`, `RoomFull`, `MatchRunning`, `ServerFull`, `SeatNotHeld`, `CannotCatchUp`); drop reasons
  `Kicked`, `Flood`; end reason `ServerLimit`. **Every one of these is something the shell of this phase has to say to a human in words** (item 2 of the brief):
  the texts exist in the `Reject` message, the reason is the code to switch on.
- **The server's key and the client's trust:** `UdpClientEndpoint` hands the server's identity key to a callback before the connection is used;
  `CnCNew.Game.Lockstep.KnownServers` (a JSON file of address and fingerprint, trust on first use; `ServerTrust.Known | New | Changed`) and
  `RemoteRelay.Connect(server, definitions, name, room, …, known, serverKey, roomPassword, playerKey)` are the client's side; `RemoteRelay.Trust` and
  `ServerFingerprint` say what happened, and a changed key throws a `RemoteRelayException` whose text names both fingerprints. The window client's options are
  `--server-key FINGERPRINT`, `--known-servers FILE` (default `%LOCALAPPDATA%\\CnCNew\\known-servers.json`), `--room-password TEXT`, `--player-key FILE` (default
  `player-key.pem` there, made on the first join), `--no-player-key`. **There is no screen for any of it**: "fingerprint of the server on first contact" in item 2
  is exactly `ServerTrust.New` and has to become a dialog; `Changed` has to become a refusal a human understands.
- **The room password** is a verifier derived by the client (`RoomPassword.Verifier(password, room)`); whoever opens a room sets it. **The host removes a member**
  with `LockstepClient.Kick(connectionId)`. Neither has a control in the client.
- **There is no listing of rooms.** A client names its room in its hello and the server tells nobody which rooms exist. The "room list of the server" of item 2
  needs a message the protocol does not have (and with it protocol version 5, a limit for it, and a line in the threat model - a listing tells a stranger who
  plays).
- **"Host game" has no host yet.** `LocalRelay` is the embedded relay of a local game, in memory, with every limit off. A client that hosts for others needs the
  other half - `UdpServerEndpoint` and `RoomDirectory` pumped by the game's loop (the two network layers start no thread and read no clock but the injected one),
  a key, and the limits of a public server. `tools/CnCNew.DedicatedServer/Program.cs` is that loop as a program; nothing of it is a library yet.
- **The limits** (`RelayOptions`, `UdpOptions`; `docs/DEDICATED-SERVER.md` §9.2) are in force on a dedicated server: 500 messages a second per member, 64 intents
  per player and tick, 20 s without a hash report, 180 s of loading. A client of this phase that chats, drags sliders in a lobby or sends orders in bursts must
  stay inside them; the embedded relay has none.
- **Somebody who watches decides nothing** (`RelayServer.Decides`): a watcher's hash is compared, a watcher that differs is dropped (`watcher-desync`), and the
  match goes on. A match only computer players play is decided by its watchers. A player that reports a wrong hash still ends the match for all (D-24).
- **A command on the wire is an order of a player** (`CommandTypes.IsPlayerOrder`: 24 of 34 types); a command that does not decode is skipped by every client
  alike (`LockstepDriver.UndecodableCommands`); a point beyond 131,072 units is rejected by the pre-check (`SimulationWorld.CommandCoordinateLimit`). The immediate
  local feedback of item 4 can rely on the pre-check saying no to such an order at once.
- **Tools to build this phase's checks on:** `NetTool load` (rooms of members that speak the whole protocol and simulate nothing - a lobby can be filled with
  them), `NetTool net-match --server HOST:PORT --server-log FILE` (real client processes against a server that keeps running), `--server-package DIR`,
  `--room-password`, `--client-server-key`; `NetTool client --server-key --room-password --player-key`; `NetTool seal-cost`; the window client's `--smoke-test
  --join … --join-until-tick N --join-order-every N --join-result FILE` and `--input-script FILE` (`docs/GAME-HUD.md` §17), which is how a click path is driven
  without a human.
- **Checks:** `scripts/check-net-load.ps1` (`net-load`, gate of `net`), `scripts/check-server-package.ps1` (`server-package`, gate of `net`; `-Platform linux` is
  `server-package-linux`, tier 4, NOT RUN without WSL), and the checks of Phases 26 and 27, which now run over the sealed transport with the server's key pinned;
  `check-net-join` also proves the window client's player key and its remembering of the server's key, in files of the run.

**For this phase in particular:**

- **The acceptance of this phase asks for humans, and the owner's standing instruction (12 October 2026) is that a brief must be workable by a machine alone.**
  Items 1 ("clarify with the human"), 4 ("the human confirms") and 5 ("acceptance with humans") are to be replaced by frameworks that do what a human would do and
  measure what a human would judge - a client driven through the shell by an input script in several processes at once, golden hashes of every new screen, the
  time from a synthetic click to the first changed frame - and by an honest list of what such a framework cannot know (whether it *feels* right). What needs a
  display runs on the virtual monitor of the development machine; what needs hardware that is not there ends with exit code 3 and is shown as NOT RUN.
- **Three findings of Phase 28 came from running real processes, not from tests** (`docs/NETWORK-PROTOCOL.md` §21.5, §21.6, `docs/DEDICATED-SERVER.md` §11.3): a
  bundle larger than a datagram was not repeated early, a client that held the cookies of two time slots was replaced by its own second connect - both as old as
  the transport -, and a server that had run for an hour closed every new room at once. The clocks of the `Net.Tests` fixtures now start at three hours for that
  reason; a new time limit of this phase must count from the thing it limits, never from the start of a clock.
  Latency work in this phase (item 4) should expect the next one: measure on the lines of the matrix, look at the 99 % value of each client, not at the mean.
- **The server's key is new on every start unless it has a key file**; a client that remembered it refuses the server after a restart. Harnesses pin the key
  their server printed. A check of this phase that starts a server twice needs `--key-file`.
- **A player key is a permanent pseudonym.** The window client states one by default; a lobby that shows "the same player as last time" would be built on it,
  and whether it is stated by default is the owner's decision (D-25).
- **Measured at this state** (`docs/NETWORK-PROTOCOL.md` §21, `docs/DEDICATED-SERVER.md` §11): the matrix over the sealed transport 36 of 36 lines
  hash-identical, order to execution unchanged against Phase 27 (median -0.4 ms, 99 % +0.4 ms), +1.5 KB/s per client and direction (10.9 KB/s up, 10.7 down);
  sealing and opening 0.3 to 0.6 µs per datagram, a handshake 1.7 to 2.0 ms of the server's core; 12 of 12 lines at 30 and 120 Hz and on a second map; a rejoin on
  five lines as in Phase 27 with the limits in force (0.3 s to 21 s); a standard match of an hour with three rejoins and a late watcher - nine of nine clients in
  the same state, the server's replay played to the agreed hash; capacity on this machine: about 1.1 % of one core and 0.7 MB per room of eight, 64 rooms at 74 %
  of the server's one core; fuzzing: 33 million changed inputs without an exception; the packaged server, started detached, ran for nine hours, 8 h 25 min of it under load in five pieces - 1,280 rooms opened and closed, 1,000 of 1,000 load matches and 280 of 280 real matches finished in step, 294 of 294 rejoins, the working set at 54 MB at the end as at the start, no hanging room (its first attempt found a server that closed every new room after an hour of running, fixed in `c1946aa`); tier 4 with 47 of 47 steps ok at `c1946aa`. `NetTool probe-suite` 48 of 48. Campaign at 60 Hz
  (missions stay local): reachability clean 24/24 and 24/24, audit 24 of 24 identical on a second run and after save/restore with end probes 13/24 and 15/24,
  objective driver victory 24/24 and defeat 24/24 - as before the phase. Performance of the simulation, the whole `scripts/perf-profile.ps1` before (`8f6f94f`)
  and after: the same final hashes, means within -7.0 % and +0.9 % (8 players: 833 against 834 µs per tick, maximum 148 against 143 ms).

**The yardstick** (`baselines/reference-manifest.json` at `4cbf535`, 60 Hz): untouched by Phase 28 - no manifest commit. Retail reference match FactionGLA at tick
32,520, final hash `6052051714309B17`, chain `84077720F511C1ED`; stress test `9EE9F730D124C3BD` / `F338463BE66D99C0`; 16 pairings 14 decided. Versions: protocol
**4**, transport **2**; `CommandTypes.Version` 17, replay 15, match description encoding 2, skirmish session state 2, `WorldSnapshot` 10, `MissionSnapshot` 3,
`SaveGame` 3, `SkirmishSaveGame` 1; desync package 1. Compatibility baseline 713.

**Still open from before:** the second physical machine with a different CPU and a real network (E1, D-10); the decisions D-17 to D-20 and D-23, and now D-24
to D-26; no lobby screen, no listing of rooms, no host in the client; no human has seen a match through a server.

## Brief (in this order)

1. **Reproduce the starting state**, re-measure the reference values; clarify with the human when he is available for the acceptance and whether a second machine is at hand.
2. **Shell.**
   - Main menu → Multiplayer → direct connection (`NetworkDirectConnect.wnd`: address, port, name, password; fingerprint of the server on first contact) → room list of the server → room (`LanLobbyMenu.wnd`/`LanGameOptionsMenu.wnd`: slots human/AI/open/closed/spectator, faction, team, colour, start position on the map preview, map, starting money, ready, start by the room creator, chat) → shared loading screen with the progress of all players → match → result for all players → back into the room.
   - "Host game" starts the embedded server in the client (the same code as the dedicated server) and opens the same room.
   - The local skirmish remains the same room with one human; there is no second setup screen with its own logic.
   - Every deviation when joining (version, fingerprint, full, password) is shown to the human with its cause; every case is counted and tested. Missing strings never fall back silently to a default.
   - Golden hashes of the new screens in all six resolutions as for the existing ones (`docs/GAME-SHELL.md`).
3. **In-game indicators.** Connection quality (network delay, buffer, catching up), "waiting for player" with names and remaining deadline, player dropped/back again, pause with requester, desync screen with a pointer to the diagnostics package (`DisconnectScreen.wnd`), chat to all/allies, spectator view (no own player: fog selectable per player or revealed, no commands, `ObserverQuit.wnd`). `--net-stats` writes the same values to the console.
4. **Latency perception.**
   - **Immediate local feedback:** selection, mouse cursor, target marker, waypoint line, acknowledgement voice and placement preview react in the frame of the click. The local pre-check (Phase 24) decides whether "cannot do that" comes immediately; the final answer of the simulation comes with the execution and corrects only in the rare case of a deviation (counted).
   - **More interpolation:** today `FrameProjector` interpolates only position and rotation of the hull. Interpolate turret angle, barrel pitch, construction progress, health bar and suspension between two ticks so that the display is smooth at every frame rate.
   - **Display lag:** the interpolation shows the state up to one tick late. Measure whether an extrapolation of own, moving units (one tick ahead, with rollback on deviation) helps noticeably; build it in only if the measurement and the human confirm it, and keep it switchable.
   - **Measurement:** time from click to execution and from click to first visible reaction as a histogram in the client (`--net-stats`), for 0/20/40/80 ms network delay via the proxy, before and after this item.
5. **Acceptance with humans** (checklist in `docs/MULTIPLAYER-FINDINGS.md`, form as in `docs/DEMO-FINDINGS.md`: per line "worked / did not work / worked with defects" and cause):
   - one human against AI via the embedded server (the new local skirmish) – does it feel like before or better?
   - two humans in the LAN via "Host game";
   - two to eight humans via a dedicated server on the internet, mixed with AI slots;
   - disconnect and rejoin, spectators, pause, a deliberately slow machine.
   Every finding is reproduced, traced back to its cause and fixed or named with a reason.
6. **Docs:** `docs/GAME-SHELL.md`, `docs/GAME-HUD.md`, `docs/PRESENTATION-INPUT.md`, `docs/PRESENTATION-OBJECTS.md`, `docs/MULTIPLAYER-FINDINGS.md`, `docs/MULTIPLAYER-PLAN.md` (completion of the programme: what is measured, what remains open), README, ARCHITECTURE, ROADMAP, IMPLEMENTATION-PLAN, `CLAUDE.md`.

## Bottlenecks and showstoppers of this phase

- **Presentation reads the running world.** HUD and input query the simulation directly (`SimulationQueries`, `CommandModeController`). Immediate feedback must never show or change a state that only the execution can know; it is pure display and influences no hash (prove it with every measurement).
- **Standing orders** (`StandingOrders`, waypoint sequences) are submitted by the presentation tick by tick; with delay they must come neither twice nor too late (test with 6 ticks of delay).
- **Smoke tests do not prove usability.** No item of this phase counts as done because a `--smoke-test` is green.

## Acceptance

Humans have worked through the lines of the checklist; the findings are in `docs/MULTIPLAYER-FINDINGS.md`; the latency histograms before and after item 4 are measured. The final report names explicitly what no human has seen. At the end check `docs/handoffs/HANDOFF-PHASE30.md` and update the "starting point".
