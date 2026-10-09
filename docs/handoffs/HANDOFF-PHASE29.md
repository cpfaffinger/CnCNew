N+1 handoff (Phase 29): Multiplayer in the client – shell, lobby, in-game indicators, latency perception, acceptance with humans

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md`, **`docs/MULTIPLAYER-PLAN.md`**, `docs/GAME-SHELL.md`, `docs/GAME-HUD.md`, `docs/PRESENTATION-INPUT.md`, `docs/PRESENTATION-OBJECTS.md`, `docs/NETWORK-PROTOCOL.md`, `docs/DEDICATED-SERVER.md` and the final report of Phase 28.
Prerequisite: Phase 28 is completed. This phase is the first of the programme whose acceptance is **not possible without humans**.

## Honest status (starting point)

Everything so far is verified headless. A human cannot start a network match yet: the "Multiplayer" button in the main menu is visible but disabled (`docs/GAME-SHELL.md`). On 5 October 2026 the skirmish setup showed team and colour selection but did not pass them on (`MatchSetup` knew only map, own faction and opponent list); Phases 24 and 25 extended the match description by teams, colours and start positions and turned the setup into the local room – first check what of this really arrives in the user interface. In the game there is no indicator for connection state, waiting players, dropped players or a desync.

Existing original layouts in the imported content (`Window/Menus/`, read only, nothing into the repo): `LanLobbyMenu.wnd`, `LanGameOptionsMenu.wnd`, `LanMapSelectMenu.wnd`, `NetworkDirectConnect.wnd`, `DisconnectScreen.wnd`, `ObserverQuit.wnd` as well as the GameSpy/WOL layouts (do not use: no GameSpy in scope).

On latency: a player's command is executed only in the tick that the server assigns. The delay is measured in Phase 26. How it *feels* depends on what the client shows immediately – and so far that is coupled to the execution (acknowledgement sound, target marker and rejection came with the synchronous answer of the simulation; Phase 24 made the path two-stage).

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
