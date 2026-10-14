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

(Filled in at the end of the phase.)
