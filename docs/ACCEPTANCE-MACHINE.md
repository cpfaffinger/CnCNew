# Acceptance by machine (Phase 30)

The brief of Phase 30 asks that the open acceptances of Phases 15 to 17 and 29 be "accepted with the human". On the owner's instruction for this phase
every acceptance is done by a machine instead: nothing waits for a person or for hardware that is not there, and for every line this document says **what a
program checked** and **what only a human can judge**. A line a program cannot check is named as such, never counted as passed.

The single-player catalogue is the one of `scripts/acceptance.ps1` (`scripts/acceptance/catalogue.json`, 85 items in 13 groups, [ACCEPTANCE.md](ACCEPTANCE.md)); its
agent pre-check of 9 October is `scripts/acceptance/precheck.json` (20 right, 4 wrong-fixed, 8 wrong-open, 53 cannot-judge). Multiplayer is
[MULTIPLAYER-FINDINGS.md](MULTIPLAYER-FINDINGS.md) sections 4 and 5 (Phase 29, unchanged by this phase). The findings lists are [DEMO-FINDINGS.md](DEMO-FINDINGS.md) and
the checklist of [CLIENT-OBJECTS.md](CLIENT-OBJECTS.md) (section "Visual check of the world").

## 1. What a program can prove, and what it cannot

A program proves that a scene **runs** (the client starts it, draws every frame, ends with exit code 0, at 30 and 60 Hz), that it is **the same** as yesterday
(golden hashes of screens and draw lists, the reference manifest), that a **number** holds (a measured quantity within a tolerance fixed beforehand, a count
of sounds, a path length), and that a **path through the user interface** works (a scripted person clicks through the real client). It does not prove that a
picture **looks like the original**, that motion **feels right** or that a sound **sounds right** - those judgements stay a human's and are listed per line as
"only a human". A golden hash proves reproducibility, never correctness.

## 2. The checks this table refers to

| Name | What it is | Where it runs |
| --- | --- | --- |
| install | `Import.Tests` (install, repair, uninstall on a synthetic installation; `--game-dir` against the real master), `check-client-package` (the packaged patcher finds Steam and checks it; the packaged client runs), `clean-room` (fresh import, `check-export`) | tier 2/4, gate of client |
| shell | `Game.Tests` `ShellTests`, `ShellTransitionTests`, `CampaignShellTests`, `NetScreenTests` (54 screen hashes in six resolutions and 474 multiplayer pictures, texts read back), `check-net-lobby`/`check-net-acceptance` (a scripted person through the real client's menus) | tier 2/3/4 |
| hud | `Game.Tests` `HudTests`, `MissionHudTests`, `HotkeyTests`, `NetHudTests` (draw-list hash, pinned pictures, text) | tier 2/3/4 |
| input | `check-dozer-build` (click path in the real client), `check-build-hotkeys`, `Presentation.Tests` (command map, placement, cursor, both mouse setups) | gate of client, tier 4 |
| scenes | `audit-scenarios` (the staged scenes `acc-*`, capture, tank duel, jet strafe: numbers from the running client against expectations, documented findings as `KNOWN`) | tier 4 |
| world | `PresentationTool object-audit`, `fx-audit`, `terrain-audit`, `model-audit` (1,585,401 of 1,585,417 triangles drawn), `Presentation.Tests` | tier 2/4 |
| sim | `Simulation.Tests` (with real data), the rate-equivalence suite (0 / 0 quantities outside tolerance at 60 and 120 Hz), the reference manifest (50 runs) | tier 2/3/4 |
| sound | `Audio.Tests`, `AudioDirectorTests`, the sound captures of the scenes (counted cues, clipped samples) | tier 2/4 |
| match | `check-match`, `check-spectator`, `session-check`, the 16 AI pairings of the manifest | tier 4 |
| campaign | `CampaignTool reachability`, `audit`, `drive` (objective driver), `outputs` (script outputs shown) | completion of a phase |
| perf | `scripts/perf-profile.ps1`, client `--net-stats`, `PresentationTool smoothness` | completion of a phase |
| analysis | `Game.Tests` `MatchAnalysisTests` (the field comparison, the export format under three cultures, a match analysed live and from its replay with the same bytes, the contents against the score screen, refusals, the report, 54 screen hashes, a scripted person), `check-replay-browser` (the analysis from the score screen and from the list in the real client), `SimTool match-analysis` (two runs the same SHA-256) - docs/MATCH-ANALYSIS.md | tier 2/3/4 |
| replay | `Game.Tests` `ReplayBrowserTests` (the file, the store, every compatibility reason, a recorded match played to every recorded hash, seeks, the server's file, refusals, 54 screen hashes, a scripted person) and `NetShellTests` (both games of a match of a server record it), `check-replay-browser` (the same in the real client) - docs/REPLAY-BROWSER.md | tier 2/3/4 |

## 3. Every line

State: **machine** - what the line asks that a program can check is checked by the named check and passes at the completion of this phase; **limit** - a named
known limit, with its reason; **fixed** - a defect found by the pre-check, corrected and held by a test. "Only a human" is what no program here can judge.

| Item | State | Checked by | Only a human |
| --- | --- | --- | --- |
| INST-01 installer | machine | install: the patcher checks the master file by file, exports, verifies the export (`check-export`); the clean room does it from an empty cache | the wording of the console |
| INST-02 start through Start-CnCNew.cmd | machine | install: the packaged client runs two smoke tests from the package's own directory; `Start-CnCNew.cmd` is generated and its content checked | that the window opens on a person's desktop |
| INST-03 uninstall | machine | install: `Uninstall` removes only the owned entries, keeps foreign files, refuses a Steam directory (`InstallTests`) | - |
| SHELL-01 main menu | machine | shell (screen hashes, 6 resolutions) | likeness to the original |
| SHELL-02 sub-menus | machine | shell | likeness |
| SHELL-03 skirmish setup, map, load, score | machine | shell; `check-net-lobby` goes through setup and load screen in the real client | likeness |
| SHELL-04 options, sliders, quit | machine | shell (`ShellTests`: slider and button states) | feel of the sliders |
| SHELL-05 font and texts | machine | shell (texts read back from the pictures' text layer), `TEXT-AUDIT.md` | whether the glyphs look like the original's |
| SHELL-06 transitions, button states | machine | `ShellTransitionTests` (15 effect kinds by frame) | how the motion looks |
| SHELL-07 menu music and clicks | machine | sound (`AudioDirectorTests`: the cues fire) | how it sounds |
| SHELL-08 skirmish and mission from the menu | machine | `CampaignShellTests`, `check-net-lobby` (a match from the menus in the real client) | - |
| SHELL-09 a 30 Hz save is refused | machine | `CampaignShellTests` (save of another rate refused with the reason) | - |
| HUD-01 command bar | machine | hud | likeness |
| HUD-02 selection, portrait, tooltip | machine | hud | likeness |
| HUD-03 queue, money, power | machine | hud | likeness |
| HUD-04 game menu | machine | hud | likeness |
| HUD-05 several units selected | machine | hud (draw list of a multi-selection) | likeness |
| HUD-06 messages | machine | hud (`HudTests` message list with the original's texts) | timing as felt |
| HUD-07 radar | machine | hud (radar picture hash, fog on the radar) | likeness |
| INPUT-01 left selects, right orders | machine | input (`check-dozer-build`: the click path in the real client) | feel |
| INPUT-02 classic mouse | machine | input (`Presentation.Tests`: the command map of both setups) | feel |
| INPUT-03 scrolling, rotating, zooming | machine | input (scroll speed at the screen edge in game time, Phase 23) | feel |
| INPUT-04 construction hot keys | machine | `check-build-hotkeys` in the real client | - |
| INPUT-05 placement preview | machine | input, hud (preview hashes on a legal and a refused spot) | likeness |
| INPUT-06 placing and building | machine | `check-dozer-build` | feel |
| INPUT-07 pointer and waypoints | machine | input (cursor by mode, waypoint path drawn) | likeness |
| INPUT-08 pointer changes, rally point | machine | input | feel |
| WORLD-01 terrain, start base | machine | world (`terrain-audit`, `object-audit`) | likeness |
| WORLD-02 low camera | machine | world | likeness |
| WORLD-03 roads | machine | world (`CLIENT-ROADS.md` checks) | likeness; the draw order of roads depends on the rate (decision D-7) |
| WORLD-04 unexplored terrain | machine | world (`ObjectTests` scenery stays hidden until explored) | likeness |
| WORLD-05 models in the viewer | **limit** | `model-audit` draws every triangle of every model in the game scenes | the model viewer (`--model-path`) shows the Crusader without its turret and with a pink patch: a defect of the viewer path only (the game scenes draw it complete); cause not investigated - the viewer is a developer tool, not part of the game |
| WORLD-06 grown base, team colours, shadows | **limit** | world (team colours by player) | **no object casts a shadow** - not built (`CLIENT-OBJECTS.md`, known limitations); a fidelity debt of Stage 1 |
| WORLD-07 fog in the window | machine | world, sim (vision per original frame) | how the fade looks |
| UNIT-01 starting, driving, braking | machine | sim (rate suite: movement quantities within tolerance), scenes (`acc-march`) | feel |
| UNIT-02 turning | machine | sim, scenes | feel |
| UNIT-03 arriving and standing | machine | sim, scenes | feel |
| UNIT-04 a place of one's own | **fixed** (Phase 31, F-04) | `GroupMove` tests: every member gets its own destination (`AIGroup::computeIndividualDestination`) | how the group spreads as seen |
| UNIT-05 boarding | machine | sim (`BoardingObstacle`), scenes (`acc-board`) | - |
| UNIT-06 leaving a transport | **fixed** (Phase 31, F-03) | `ContainerExit` tests: riders leave through the door to a free place | how it looks |
| UNIT-07..09 factory exits | machine | sim (`RateInvariance.Exits`), scenes (`exit-*`) | - |
| UNIT-10 war factory up close | machine | world (door states, model conditions) | likeness of the door animation |
| UNIT-11 turrets and fire release | machine | sim (`SIM-TURRET-AIM.md` tests), scenes (`tank-duel`) | - |
| UNIT-12 projectile paths | **fixed** | sim (`Projectiles`), the detonation effect once at the impact | how the arc looks |
| UNIT-13 jets | machine | sim (`JetFlight`, `JetFlightReal`), scenes (`jet-strafe`) | feel |
| UNIT-14 capturing | machine | scenes (`capture-building`, flashes and tick sounds counted) | - |
| UNIT-15 wheels, steering, suspension | **fixed** | world (`SuspensionTracker` numbers: steer 19.8 and 30 degrees, nod 3.4 degrees) | look |
| UNIT-16 repair, wrench | machine | hud (`OverlayIsDrawn`) | look |
| UNIT-17 construction | **fixed** | world (scaffold, crane, then the building; no damaged states while building) | look |
| FX-01 muzzle flashes | machine | world (`fx-audit`, flash only after a shot) | duration against the original |
| FX-02 tracers and hits | machine | world (`fx-audit`) | look |
| FX-03 explosions, smoke, wreck | **fixed, with a limit** (Phase 31, F-05) | sim (slow-death phases play effect, creation list and weapon) | **debris of creation lists is counted and skipped** (F-36): a dying tank leaves no flying turret; the look |
| FX-04 smoke trails, fire at the impact | **corrected** (Phase 35) | `fx-audit` checked only the references of the FX lists; the effects objects carry - flame trails, missile exhaust, the fire and smoke of damaged and burning objects, model-state systems, projectile streams - were not built at all (the owner saw it on 16 October 2026). Now `AttachedEffectsTests` (`docs/PRESENTATION-FX.md` "Attached effects") | look; bone rotations and animated bones not followed |
| SND-01 shot and impact sounds | **fixed** | sound (one detonation sound per shot, clipped samples 181 to 62) | how it sounds |
| SND-02 many sounds at once | machine | sound (cue counts of `acc-firefight`) | how it sounds |
| SND-03 tick of the capture | machine | scenes (ticks counted at every rate) | how it sounds |
| SND-04 construction and Eva | machine | sound (`AudioDirectorTests`) | how it sounds |
| SND-05 music, voices, Eva | machine | sound | how it sounds |
| SND-06 sound of the mission scripts | machine | campaign (`outputs`: script sounds played and counted) | how it sounds |
| SKIRM-01 building a base | machine | `check-dozer-build`, match | feel |
| SKIRM-02 fighting | machine | match (AI pairings, `check-match`) | feel |
| SKIRM-03 a skirmish the computer plays | machine | match (`check-spectator`, 16 pairings, 14 decided) | - |
| SPEC-01..06 spectator | machine | `check-spectator`, `SpectatorTests` (panels, fog views, speed, no orders) | readability, feel |
| SPEC-07 end of the match | **limit** | match (the winner line appears; 14 of 16 pairings decided) | the pre-check's seed 7 ended after 4:49 with a China that never had an army - AI behaviour of one seed, not of the spectator mode; the AI changed since (Phase 23, `0fe9923`) and the seed was not looked at again; whether the balance is wanted is an open decision of the owner (`PROGRAMME.md` §7) |
| SPEC-08 the AI builds vehicles | **limit** | match (army composition measured over 320 matches in Phase 23) | as SPEC-07 (seed 11) |
| SPEC-09 chance and repeatability | machine | match (`session-check`: 19 runs continue identically from 77 saves) | - |
| MISS-01, 03, 04 mission GC_ChemGeneral | machine | campaign (reachability 24/24, audit 24/24, objective driver 24/24 and 24/24), `check-dozer-build` (building after the intro in the real client) | - |
| MISS-02 intro of MD_USA01 | **limit** | campaign (script outputs counted) | **no sky is drawn** (known), and the scripted camera pitch near the horizon shows a rock face and flat blue; cause not found |
| RATE-01..05 60 against 30 Hz | machine | sim (rate suite 0 / 0), scenes at both rates, the reference match the same game at 30, 60 and 120 Hz | that nobody sees a difference |
| PERF-01 smoothness | machine | perf (`PresentationTool smoothness`, `--net-stats`: the next tick late in 0 to 5 of 7,178 frames) | feel |
| PERF-02 loading time | machine | perf (load steps logged by the client; `GameData.Load` measured in the smoke tests) | whether it is acceptable |
| PERF-03 spectator at eight times speed | machine | perf (catch-up speed 26.5-fold at 60 Hz) | feel |
| REPLAY-01 every skirmish is recorded | machine | replay (local, a match of a server on both games, a spectator's match; the ending of each kind by its facts) | - |
| REPLAY-02 the list and the state of each file | machine | replay (nine states in six resolutions; every reason with words of its own) | **whether the list is easy to read** |
| REPLAY-03 playback to the recorded result | machine | replay (every recorded session hash, the final hash and the score, at 30, 60 and 120 Hz; in the window client) | how the playback looks |
| REPLAY-04 pause, speed, seek, view | machine | replay (three seeks at the recorded hash, pause, speed and view kept; in the window client 0.1 to 0.3 s per seek) | **whether the controls feel right** |
| REPLAY-05 an incompatible file says why | machine | replay (another build, rule profile, rate, map; a changed byte; a header that does not describe its match) | whether the words are understood |
| ANALYSIS-01 the statistics of a match | machine | analysis (live and from the replay the same bytes; collected, built and lost equal to the score screen's counters; every sample on the frame grid at 30, 60 and 120 Hz) | **whether the numbers tell the course of the match a person remembers** |
| ANALYSIS-02 the export | machine | analysis (schema and version, the same bytes twice and under three cultures; the tool prints the same SHA-256 twice) | - |
| ANALYSIS-03 the report | machine | analysis (one page, the same bytes twice, every chart and both heat maps in it, no script, no address) | **whether the report is readable; whether the colours can be told apart** |
| ANALYSIS-04 the screen after a match and from the list | machine | analysis (six pages in six resolutions pinned; a scripted person and the window client open it from the score screen and from the list) | **how the screen looks and reads** |
| ANALYSIS-05 the score screen's counters | machine | analysis, `ShellTests` (the counters count by the owner of an object; F-42: not yet only the `SCORE` kinds of the original) | whether the numbers are the original's |

## 4. The findings lists

- **[DEMO-FINDINGS.md](DEMO-FINDINGS.md)** nos. 1 to 24 and the visual checklist of 60 Hz: every line is either fixed with its test (the document says which) or one of
  the limits of section 3 (shadows, sky, debris, viewer); what remains is "only a human" in the sense above. The numbers 7, 9 and 10 that stand there as "open"
  were closed by Phase 16 (sound wired, script outputs shown, GDI text path) - the later sections of that document say so; this phase adds the machine state
  of section 3.
- **[CLIENT-OBJECTS.md](CLIENT-OBJECTS.md)**, "Visual check of the world": its lines are WORLD-01 to WORLD-07, UNIT-10 to UNIT-17 and FX-01 to FX-04 above.
- **[MULTIPLAYER-FINDINGS.md](MULTIPLAYER-FINDINGS.md)**: unchanged since Phase 29 (sections 4 and 5 there name per line what a program checked and what only a
  human could judge); the support bundle and the crash report of this phase add the diagnosis a player can hand on.

## 5. Not verified

- Every "only a human" of section 3: no person has seen, heard or played the game for an acceptance.
- A second physical machine and a clean machine without .NET: the client package is self-contained and runs from its own directory on this machine, which
  has the SDK installed; that it starts on a machine without any .NET is not shown.
- The four limits (WORLD-05, WORLD-06, MISS-02, FX-03 debris) and the AI seeds of SPEC-07/08.
