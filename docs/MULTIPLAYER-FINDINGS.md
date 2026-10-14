# Multiplayer in the client: what was checked, by what, and what is left for a human

Phase 29 (`docs/handoffs/HANDOFF-PHASE29.md`, milestone S4-M5). The brief asks for an acceptance by humans in three places. No human took part in this phase, and
no second machine: every line below is checked by a program, and says what that program proves and what it does not. A line that only a human can judge says so;
nothing here claims that a human saw, heard or played anything.

The form of a line: **checked by a program** - what exactly, with the check that does it - and **not checked** - what a human would still have to judge.

## 1. The frameworks

| Instead of | There is | What it is |
| --- | --- | --- |
| a human who clicks through the menus | `ShellScript` (`src/CnCNew.Game/Shell/ShellScript.cs`), the client's `--shell-script FILE`, `scripts/check-net-lobby.ps1` | Steps that wait for what a person would see (a screen, a text, a button that can be pressed) and then act through input events - pointer, button, keys, typed characters - handed to the shell the way the device's are. A step has a time limit; a failed step names what the shell showed instead. Several window clients run at once, against a server inside one of them or a dedicated one. |
| eyes on every screen | `NetScreenTests` with `tests/CnCNew.Game.Tests/baselines/net-screens.txt` | A scripted lobby puts the screens into every state a server can put a client in. Each state is drawn in six resolutions and its draw list pinned by a hash (a checksum, no picture); the test also reads the texts, the enabled and hidden windows. No two states share a hash in any resolution. |
| "does it feel right" | `NetStats` (`src/CnCNew.Game/Lockstep/NetStats.cs`), the client's `--net-stats` with `--net-order-every N` | The stand-in for a person clicks: camera over a unit, the unit selected, the order button pressed and released on the ground beside it. Measured from the release: to the end of the first drawn frame that shows the HUD's answer, to the tick the order is executed in, to the end of the first frame drawn after that; and from frame to frame how evenly units move and how the interpolated quantities step. |

What none of them is: a person. A hash proves that a picture is the same as the one recorded, not that the recorded one is right; the texts and states the tests
read are the part that is checked for being right. A measured time is the time to a drawn frame, not to light leaving a monitor, and it starts at the input event
inside the client, not at a finger.

## 2. Latency: the thresholds, fixed before the measurement

The brief leaves two decisions to "the measurement and the human": whether the local feedback is immediate enough, and whether an extrapolation of the viewer's
own moving units is built. They are decided by the numbers below. **These thresholds were written and committed before any line with a network was measured**
(the commit that adds this section precedes the commit that adds the numbers of section 3; the instrument itself was tried on a local match while it was built -
those trial numbers are not results).

Set-up of every line: a dedicated server, a UDP proxy with the stated delay in each direction (no jitter, no loss), one window client and one client without a
window in a room of two, 60 Hz, a display at 60 frames a second; the window client's stand-in clicks every two seconds. The line "local" is a skirmish through
the server embedded in the game. Lines: local, 0 ms, 20 ms, 40 ms, 80 ms.

| | Quantity | Threshold | If it holds | If it does not |
| --- | --- | --- | --- | --- |
| T1 | click to the first changed frame (the marker of the order), 99 % value | at most 34 ms (two display frames) on every line, and the median on every network line within 5 ms of the local line's | the local feedback is immediate; nothing is built | the path from the click to the marker is corrected and measured again |
| T2 | click to execution | none - it is what the network costs: about twice the delay plus the playback delay | recorded | recorded |
| T3 | evenness of motion: the share of frames in which a moving unit's speed differs by more than half from the frame before ("jumps"), and in which it stands still between two moving frames ("holds") | on every network line at most 2 percentage points above the local line, each; and the 99 % frame time at most 5 ms above the local line's | **no extrapolation is built.** Interpolation shows a state at most one tick old - 16.7 ms at 60 Hz, less than one display frame and less than T1 allows for the feedback itself | an extrapolation of the viewer's own moving units is built, switchable, and the lines measured again |
| T4 | the quantities newly interpolated between two ticks (health, construction progress, wheel travel, suspension), drawn at 144 frames a second from a recorded match | the share of frames in which a quantity on its way changes rises from about the tick share (60 / 144 = 42 %) to at least 90 %, the 99 % step falls, and every frame drawn exactly at a tick is the same as before | the interpolation stays | it is corrected or taken out |
| T5 | no influence on the simulation | on every line the window client (feedback, statistics and pictures on) and the client without a window (none of it) report the same session hash for every tick; the server agrees on every tick | - | a defect; nothing is measured until it is found |

Why T3 decides the extrapolation: what an extrapolation can win is the age of the shown state, at most one tick. On the 80 ms line an order is executed after
roughly 180 ms; one tick is a tenth of that. It could only pay if the interpolation itself made the picture uneven under a network - which is what T3 measures.

## 3. Latency: the numbers

14 October 2026, one machine (AMD Ryzen 3 PRO 4350GE, Windows 11, Release), `pwsh scripts/measure-net-latency.ps1 -MatchSeconds 120`: 60 Hz, a display at 60
frames a second (frame time median 16.7 ms, 99 % 19.1 to 19.8 ms on every line), Alpine Assault, the window client plays USA and its stand-in clicks every two
seconds - 59 clicks a line, all 59 answered by the HUD and all 59 executed, no deviation between pre-check and execution, none refused. Times in milliseconds.
"Before" and "after" are the same binaries with `--no-value-interpolation` and without (the interpolation of section T4 off and on); "held" and "ahead" are the
same binaries without and with `--extrapolate`.

### 3.1 From the click to the first frame that shows it (T1)

| Line | before: median / 95 % / 99 % | after | held | ahead |
| --- | --- | --- | --- | --- |
| local (embedded server) | 2.2 / 3.6 / 27.2 | 2.2 / 4.1 / 21.0 | 2.4 / 3.8 / 19.9 | 2.2 / 3.3 / 19.3 |
| 0 ms each way | 1.9 / 3.7 / 20.5 | 2.1 / 5.2 / 20.5 | 2.1 / 3.6 / 20.2 | 2.1 / 9.0 / 23.3 |
| 20 ms | 1.7 / 3.0 / 19.3 | 2.1 / 4.2 / 25.0 | 2.0 / 3.6 / 22.6 | 2.0 / 3.3 / 22.3 |
| 40 ms | 1.9 / 5.1 / 23.3 | 1.9 / 9.3 / 22.7 | 2.0 / 3.4 / 19.1 | 2.0 / 3.8 / 22.6 |
| 80 ms | 1.9 / 3.3 / 19.7 | 1.9 / 3.1 / 19.7 | 2.1 / 3.7 / 23.3 | 1.9 / 8.9 / 24.5 |

**T1 holds** in all twenty measurements: the 99 % value is at most 27.2 ms (limit 34), and every median lies within 0.5 ms of the local line's (limit 5). The
marker of an order stands in the frame of the click whatever the line. With 59 clicks the 99 % value is the largest one measured; it is one frame that took longer.
What the number is: from the input event inside the client to the end of the draw of the frame that carries the HUD's answer - not the device's own delay before
the event, and not the display's after the draw.

### 3.2 From the click to the execution (T2)

| Line | Click to execution: median / 99 % (after) | Order delay in game time: median | For comparison: twice the delay |
| --- | --- | --- | --- |
| local | 0.3 / 27.5 | 0 | 0 |
| 0 ms | 17.1 / 35.1 | 16.7 | 0 |
| 20 ms | 50.5 / 68.7 | 50.0 | 40 |
| 40 ms | 100.3 / 119.2 | 100.0 | 80 |
| 80 ms | 183.5 / 202.0 | 183.3 | 160 |

An order is executed twice the delay plus one or two ticks later (17 to 33 ms: the tick it is put into and the playback delay; between two runs of the same line
the median moved by one tick, 16.7 ms, on three lines). This is what the network costs; the frame after the execution follows 2 ms later. The feedback of T1 is what
bridges it - on the 80 ms line the marker stands 181 ms before the unit turns.

### 3.3 How evenly units move (T3) - **the threshold was not met, and the remedy it prescribed does nothing**

| Line | Jumps: before / after / held / ahead | Holds: before / after / held / ahead | Frames in which a unit moved (held) |
| --- | --- | --- | --- |
| local | 2.28 / 2.58 / 2.66 / 2.10 % | 0.63 / 0.62 / 0.62 / 0.63 % | 5,119 of 7,161 |
| 0 ms | 4.98 / 5.30 / 4.48 / 5.49 % | 1.44 / 1.44 / 1.44 / 1.44 % | 3,548 of 7,180 |
| 20 ms | 4.28 / 4.80 / 4.91 / 4.37 % | 1.44 / 1.44 / 1.44 / 1.44 % | |
| 40 ms | 4.81 / 4.86 / 4.71 / 4.55 % | 1.44 / 1.44 / 1.44 / 1.44 % | |
| 80 ms | 4.10 / 4.45 / 4.31 / 4.53 % | 1.22 / 1.22 / 1.21 / 1.22 % | 3,874 of 7,176 |

By the letter of T3 the jumps exceed the local line by more than 2 percentage points on three of the four network lines (by 2.0 to 2.7 points; the holds stay
within 0.8, the frame time within 0.2 ms). The consequence written down beforehand was carried out: **an extrapolation was built, switchable**
(`FrameProjector.Project(alpha, ahead)`, `LiveGame.Extrapolate`, the client's `--extrapolate`: when the next tick is late, what moved in the last tick is drawn on
along that step for up to one tick; nothing is remembered, so the frame after the tick arrived is drawn from the tick's own state) and all lines were measured
again with and without it.

**It changes nothing**: jumps 4.3 to 5.5 % with it against 4.3 to 4.9 % without, the holds identical to the hundredth. And the measurement says why: a frame was
drawn ahead in **0 to 5 of 7,178 frames** a line. The playback delay does what it was built for (docs/NETWORK-PROTOCOL.md section 15) - the next tick is there
when its time comes - so there is next to nothing for an extrapolation to bridge. It is therefore **off by default** and stays in the code as an option.

What the excess over the local line is, read from the same numbers (an interpretation made after the measurement, named as one): the local line is not the same
match. Against the computer player the stand-in's unit moves in 71 % of the frames, in the two-player matches of the network lines in 49 to 54 %; the number of
holds is the number of times the unit stopped to turn (32 locally, 47 to 51 on the lines, of 59 orders each), and a share of fewer moving frames is larger. The
comparison that isolates the network is among the network lines: from 0 to 80 ms neither jumps (4.5 / 4.9 / 4.7 / 4.3 % held) nor holds (1.44 / 1.44 / 1.44 /
1.21 %) grow with the delay. **A local line as the yardstick for evenness was a mistake of the set-up; T3 as written is reported as not met, not as passed.** What
a human would have to judge: whether a unit that turns on the spot before it drives reads as stutter - it is the same with and without a network.

### 3.4 The newly interpolated quantities (T4) - holds

`PresentationTool smoothness` (no window): two computer players on Alpine Assault, 240 s at 60 Hz, drawn at 144 frames a second, every object visible.

| Quantity | Frames in which it changed: stepped / interpolated | Ratio | Median step: stepped / interpolated | 99 % step | Largest step |
| --- | --- | --- | --- | --- | --- |
| health, % of full | 6,831 / 17,088 | 2.50 | 0.166 / 0.069 | 13.9 / 5.0 | 100 / 41.7 |
| construction, % of done | 5,933 / 14,246 | 2.40 | 0.167 / 0.069 | 0.167 / 0.069 | 0.333 / 0.139 |
| wheel travel, world units | 129,264 / 310,739 | 2.40 | 0.333 / 0.139 | 3.75 / 1.56 | 7.27 / 3.03 |

A quantity that changes every tick changes in 60 of 144 frames when it steps with the tick (42 %); 2.40 times as many frames is every frame in which it is on its
way (100 %, limit 90 %). The steps fall by the same factor. The hash over all frames drawn exactly at a tick (2,880 of 34,561) is the same in both runs
(`57E1055BCB023F00`), and so is the session hash at the end (`A25B297D4EA516BA`). The pose of the chassis is interpolated by the same rule and was not measured on
its own. On the latency lines (60 frames a second at 60 Hz, where a frame usually is a tick) "before" and "after" do not differ beyond their scatter, as expected.

### 3.5 No influence on the simulation (T5) - holds

On every one of the sixteen network lines the window client (HUD, feedback, statistics, pictures, with and without interpolation and extrapolation) and the client
without a window ended with the same hash chain over all 7,200 ticks, and the server agreed on the session hash of both through tick 7,198 or 7,199 (the last
reports are on their way when the match ends). No desync, no deviation between a pre-check and its execution in 944 orders.

## 4. The acceptance lines of the brief

Each line of item 5 of the brief, as the scenario that stands in for it. "Checked by a program" is what ran and passed on 14 October 2026 on one machine; "not
checked" is what is left for a human or for hardware that was not there.

### 4.1 One human against the computer through the embedded server (the local skirmish)

- **Checked by a program** (`scripts/check-net-lobby.ps1`, client `local`): the window client is driven from the main menu through Single Player, Skirmish and Start
  into a match; the ticks advance (600 and more) and the viewer has its units. The match runs through the server embedded in the game (since Phase 25), with no
  added delay: on the local line of section 3 an order is executed in the frame of the click (median 0.3 ms) and the marker stands 2.2 ms after it.
  `scripts/check-dozer-build.ps1` and `scripts/check-build-hotkeys.ps1` (unchanged) click through building in such a match.
- **Not checked:** "does it feel like before or better" - nobody played it. A whole match to its end in the window (the standard match is an hour).

### 4.2 Two humans through "Host game"

- **Checked by a program** (`scripts/check-net-lobby.ps1`, clients `host`, `guest`, `wary`; headless the same way in `NetShellTests`): two window clients on the
  loopback interface. The host goes Multiplayer > Network > Host, opens a locked room; the guest types the address, is asked about the server's key with the
  fingerprint and says yes, sees the room in the list marked as locked, is refused with a wrong password and told so, enters with the right one, takes the open
  seat, the two exchange a chat line, the guest accepts, the host starts; both show the load screen with both names, play twenty seconds with eleven orders each
  (22 intents through the server), end at the same tick with the same session hash and the same hash chain over every tick, stand on the score screen and are in
  the room again. A third client that knows another key for the address refuses the server with both fingerprints and says nothing to it. The files of the visit
  lie in the run's directories. 62 s for the whole check.
- **Not checked:** two machines and a cable or a wireless network between them - both games ran on one computer, so nothing about a firewall, a router or a
  second CPU (E1, D-10) is shown; how the screens look to a person (the hashes of `NetScreenTests` pin them, they do not judge them); a human typing.

### 4.3 Two to eight humans through a dedicated server, mixed with computer players

- **Checked by a program** (`scripts/check-net-acceptance.ps1`): a dedicated server behind a proxy with 40 ms each way, 10 ms of jitter and 1 % loss; window
  clients Alice (opens the room through the menus, picks Twilight Flame, makes two seats computer armies, starts) and Bob (finds the room in the list, enters,
  accepts); three clients without a window as the players Carol, Dave and Erin; one as a watcher; 120 s of game time. Alice, Bob, Dave and the watcher end with
  one hash chain over all 7,200 ticks; the server agreed on every tick through 7,198 and reported no desync; both window clients gave 59 orders that went through
  the server. 179 s for the whole check. The matrix of Phases 26 to 28 (36 lines of delay, jitter and loss with eight client processes) still passes with
  protocol 5 (section 6).
- **Not checked:** the internet - every line is a proxy on the loopback interface; eight *window* clients at once (two were run; six of the eight participants
  simulate without a window or are computer players); people.

### 4.4 Disconnect and rejoin, spectators, pause, a deliberately slow machine

- **Checked by a program** (the same scenario; both window clients print what their indicators show, and the check reads it by name):
  - *a lost connection that comes back*: Carol's process drops its connection at tick 1500 and returns after 14 s with its token. Both window clients show
    `Carol is 2.0 s behind - dropped in 10 s` with `The game runs at half speed until everybody has caught up`, then `Carol lost the connection - the seat is held
    for 20 s`, then the notice `Carol is back.`;
  - *a slow machine*: Dave's process stands still for 1.5 s at tick 2700 - both show `Dave is 0.5 s behind`, and it catches up;
  - *a player who is gone for good*: Erin's process vanishes at tick 3600 - both show her seat held for 20 s and then the notice `Erin did not come back in time.`;
  - *pause*: Alice's stand-in asks for a pause at tick 5400 and ends it two seconds later - Bob's screen says `Paused by Alice`, hers `Paused by you`;
  - *a spectator*: a client without a window watches from the room on and ends in the same state as the players.
  Headless, with the HUD of both games (`NetShellTests` "In the match"): the connection line, a chat line to all that both message lists show, a line to the
  allies that only its sender's shows, the pause with its requester. A rejoin by token, a late watcher, a delayed watcher and the desync diagnosis with real
  processes: `scripts/check-net-robustness.ps1` (Phase 27, unchanged and passing).
- **Not checked:** a **window** client that comes back into a running match - the menus cannot do it (section 5); a spectator in a window that entered through
  the menus; what a slow *graphics* machine does (the slow client was slow in its simulation loop); whether the indicators are readable and in the right place.

### 4.5 Time limits, for an hour

- **Checked by a program** (`scripts/check-net-endurance.ps1 -Minutes 65`): see section 6.

## 5. What was found on the way, what is not built, what nobody has seen

**Found by the frameworks** (each fixed, each with the check that found it):

| Finding | Found by | Since |
| --- | --- | --- |
| The client threw in the first frame after a match that was started from the menus (the Stride compositor outlived the scene). Every match that ended in the shell, local ones too | the first run of two scripted window clients | the shell's match path (Phase 12); no check had played a match to its end in a window |
| Both games were half a second behind the server at tick 0: the world's layers were built after the server had started its clock | the hosted server's `behind` events in the same run | new in this phase; the layers are now built before the server is told "loaded" |
| A host that changed anything in its room threw everybody out of their seats; a start that closed an empty seat took everybody's "ready" back and was refused | the measurement script (a client without a window sat down while the host picked a map) and the acceptance scenario (eight seats, seven taken) | the relay's rule since Phase 25; now seats survive a change, and closing empty seats keeps "ready" |
| A lobby that browsed a server which lists nothing tried to enter on a connection the server was already closing | `NetLobbyTests` | new in this phase |
| The stand-in's clicks hit the control bar, a building, a tree - and gave no order | `--net-stats` (clicks answered: 4 of 14) | the instrument itself; it now looks for a place where the cursor says "move", as a person does |
| On the HUD a held seat stayed "held for 0 s" for the rest of the match, and a return was never told | the acceptance scenario | new in this phase (`LockstepClient` dropped the end of a hold; it now keeps every change) |
| In every match one of the two games closed its connection before the server had ended the match, and the server counted a player who lost the connection in a running match | the endurance run's events, broken down by reason and phase (188 of 188 matches) | the client's end of a match since Phase 27; it now keeps the connection until the server's end, three seconds at most |
| `System.Security.Cryptography.Xml` and `.Pkcs` 10.0.12 of the Stride tooling have five advisories published since Phase 28 | the package audit of tier 4 | pinned to 10.0.13 |

**Not built** (each named where a person would look for it):

- **A window client cannot come back into a running match.** The server holds a seat and the protocol has the token (Phase 27); clients without a window use it.
  The menus do not: after a lost connection the score screen leads back to the room's name, and a room whose match runs takes nobody (`MatchRunning`). A person
  who lost the connection cannot rejoin from the window.
- **No watcher's way through the menus.** A member without a seat watches (the room's last row can be left); there is no "watch" button, no choice of whose fog
  to see, and `ObserverQuit.wnd` is not used. `--join --watch` on the command line is unchanged.
- **No button to remove a member** (the lobby can; the screen has no control for it). **No start position box.** **No votes** (D-23).
- **No list of servers and no discovery**: a server is an address somebody was given. "Online" of the main menu stays inert.
- **The original's `DisconnectScreen.wnd`** is not used (it is built around votes); the waiting panel of the HUD says what it would.
- **Texts are English only** and this project's own; the game's language files have no words for a relay.
- **`select` of the script scrolls with the wheel**; a person can also drag the scroll bar - not scripted.

**Not verified - what no human has seen, heard or played, and what no machine here could show:**

- Every screen of section 15 of `docs/GAME-SHELL.md` and every indicator of section 21 of `docs/GAME-HUD.md`: whether they look right, are readable, sit where
  one expects them. The hashes pin what was recorded; the tests read texts and states.
- How a match with delay *feels*. Measured: the marker comes in the frame of the click, the unit turns twice the delay and a tick or two later.
- A second physical machine, another CPU, a real network, a NAT, a firewall prompt of the operating system when "Host" opens its port (E1, D-10).
- Sound in the menus of the new screens (the click sounds are requested; nobody heard them).
- A window on a display faster than 60 frames a second (the interpolation was measured without a window at 144).

**Decisions that are the project owner's** (built as an option or a default with its reason, none taken as decided):

| | Question | What is built |
| --- | --- | --- |
| D-27 | Shall a server list its rooms to anybody who asks? | yes by default (`listRooms`), `--no-room-list` turns it off; the list names no person (threat T16) |
| D-28 | Which builds may enter a hosted game? | the build that hosts, changed or not; a dedicated server stays with released builds unless told otherwise |
| D-29 | Shall the late-tick extrapolation be on? | off (`--extrapolate`): measured without effect |
| D-30 | Chat to the allies when a seat is in no team | reaches the sender only |
| D-17, D-19, D-23 to D-26 | unchanged | the indicators show what the server's rule does (deadline, held seat); the rules themselves were not touched |

## 6. Closing measurements (14 and 15 October 2026, one machine)

- **Tier 4:** Tier 4 with 49 of 49 steps ok at the state of `a3cfef6` (5,678 s; 0 warnings; all test programs; simulation, game, presentation and network tests at 30, 60 and 120 Hz; both manifests identical; the rate-equivalence suite; the long run; the client checks; `check-match`; `session-check`; `net-probe`; `net-match`, `net-robust`, `net-join`, `net-cross-os`, `net-load`, `server-package`, `server-package-linux`; the new `net-lobby` and `net-acceptance`; the staged scenes) - the second run: the first stopped at the audit of the packages, which found five advisories published since Phase 28 against `System.Security.Cryptography.Xml` and `.Pkcs` 10.0.12 of the Stride tooling (pinned to 10.0.13 in `a3cfef6`).
- **Endurance** (`scripts/check-net-endurance.ps1`): one hosted server for 82.0 minutes of the final code (62 asked; the run ends at the end of a cycle): 188 cycles, each a guest that connected (the first time asked about the key), stood in front of the rooms (once for five and a half minutes - longer than a server lets a connection browse; the lobby came back by itself), entered, accepted, played 15 s, returned and left; 188 matches on both games, every one ended by its rules at the same tick and in the same state, 376 departures after the server had ended the match and none from a running one, 188 departures from the room by choice, no desync, managed memory 162 to 163 MB. A first run of 82 minutes before the end-of-match fix had the same result except one departure from the running match in every match (section 5 of docs/MULTIPLAYER-FINDINGS.md); the one before it ended after 58.4 minutes because its script held too few cycles.
- **The matrix of Phases 26 to 28 with protocol 5** (eight client processes - six players, two watchers, two computer players - on Twilight Flame through the
  UDP proxy, 100 s a line, delay 10/40/80/150 ms x jitter 0/10/30 ms x loss 0/1/3 %): 36 of 36 lines hash-identical on all eight clients; against Phase 28 the median over the lines of the change of order to execution is -0.1 ms (median), +0.1 ms (95 %) and +0.8 ms (99 %), of the bandwidth +9 B/s up and +38 B/s down (10.9 and 10.7 KB/s per client and direction on average).
- **`NetTool probe-suite`:** 48 of 48 runs hash-identical on all clients in every tick and equal to their replay (582 s).
- **Campaign at 60 Hz** (missions stay local): reachability clean 24/24 and 24/24; audit 24 of 24 identical on a second run and after save/restore with end probes 13/24 and 15/24 (exit code 1 as in Phase 28: `MD_CAMPEA_DEMO` names a map the master installation does not have); objective driver victory 24/24 and defeat 24/24 - as before the phase.
- **Performance of the simulation** (`scripts/perf-profile.ps1`, the binaries of `c1946aa` and of this phase in the same hours): every one of the ten runs with the same final hash on both sides; means within -1.5 % and +3.8 % (8 players: 826.5 against 830.9 µs per tick; maximum 148 against 138 ms; bytes per tick 8,550 against 8,551) - the multiplayer client changes nothing of the simulation's cost.
- **Not run:** the determinism matrix's switch and culture rows; the long standard match of an hour with rejoins (Phase 28's run was not repeated - the
  endurance of this phase is the hosted server's); a second machine.

## 7. Phase 30

Unchanged lines; Phase 30 adds what a player can hand on when a match goes wrong - the crash report, the run log and the support bundle with the desync
packages ([TROUBLESHOOTING.md](TROUBLESHOOTING.md)) - and keeps `net-lobby` and `net-acceptance` in the gate. The single-player items are in
[ACCEPTANCE-MACHINE.md](ACCEPTANCE-MACHINE.md).
