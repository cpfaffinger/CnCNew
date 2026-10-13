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

(Filled in by the measurement: `scripts/measure-net-latency.ps1`.)

## 4. The acceptance lines of the brief

(Filled in at the end of the phase.)
