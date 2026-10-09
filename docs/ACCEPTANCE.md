# Acceptance by a human: catalogue, guided run, pre-check

As of 9 October 2026. **Nothing in this project has been accepted by a human** – not the look, not the sound, not the controls, not playability, not 60 Hz. The
project owner looked at the running game twice for a few minutes and each time found real defects that every test had passed (`docs/DEMO-FINDINGS.md`). This document
is the template for the acceptance: one catalogue of items, one scene per item that can be started by its id, a script that leads through them and records the
verdicts, a picture page that needs nobody at the keyboard, and the reading of an agent before – which is **not** an acceptance.

## 1. The three commands

```powershell
pwsh scripts/acceptance.ps1 -Capture -All   # unattended, about 50 minutes: film strips at 60 and 30 Hz, sound, scene audits, index.html
pwsh scripts/acceptance.ps1 -Stills         # opens the page; one key per item that can be judged from still pictures (22 items, about 10 minutes)
pwsh scripts/acceptance.ps1                 # the part in the window: one window per scene, one key per item (56 items, about 51 minutes)
```

Afterwards `pwsh scripts/acceptance.ps1 -Report` prints the summary and writes it as Markdown; that file is what goes back to the developer.
`-Resume` continues where the last run stopped, `-Item <id>` and `-Group <group>` narrow the run, `-List` shows everything with ids, minutes and totals and starts nothing.

Keys after every scene, once per item: `p` pass, `f` fail (then one line of text), `s` skip, `r` show the scene again, `c` show the same scene at 30 Hz,
`q` save and quit. `-Compare` shows every scene at 30 Hz after the 60 Hz window without being asked; with `-SideBySide` both windows open at once at three quarters of
the size, 60 Hz on the left (see §7 for what is verified about that).

## 2. What the script does, and what it never does

- It reads `scripts/acceptance/catalogue.json` (items, groups, scenes) and `scripts/acceptance/precheck.json` (the agent's readings).
- **Window run:** per scene it prints instruction and expectation of every item that shares the scene, starts the client windowed at 60 Hz with a caption over the
  picture (scene, rate, the ids and titles of the items; `--caption`), waits until the window is closed and then asks one key per item. Staged scenes start again by
  themselves when they have run their length (`--loop`), so a scene can be watched several times. Spectator items start `scripts/demo-spectator.ps1` as it is;
  installation items print the command to run by hand.
- **Capture:** per scene and rate the client runs without input in the fixed-step mode (`--smoke-test`: one picture is one original frame of 1/30 s game time at every
  rate) and writes a film strip (`--filmstrip`), for sound scenes the mixer output as `audio.wav` (`--audio-capture`); menus come from the `UiRenderTool`. Scenes with
  a headless audit run `PresentationTool scenario-audit` and its result line stands on the page. Pictures are converted to JPEG, a contact sheet per strip is added.
- **Results:** every verdict is appended at once to `%LOCALAPPDATA%\CnCNew\acceptance\results-<date>.json` (`-ResultsPath` chooses another file): item id, verdict,
  note, scene, tick rate, whether it was compared with 30 Hz, the commit, the hash of the content manifest (from `install.json`), a time stamp, `by: human`.
  The agent's readings travel in the separate field `preCheck` of the same file and are never an entry. The latest entry of an item counts.
- **Content:** `-ContentDir`, else `game\content` of the developer checkout, else the installation made by `scripts/install.ps1`
  (`%LOCALAPPDATA%\CnCNew\Game`, `-InstallDir`), found through its `install.json`. `-UseInstalledClient` runs the installed client instead of building one
  (it must contain the scenes, that is: be published from this commit or later).
- **It never** writes into the repository (results, report, pictures and sound are refused there and inside a Steam library), never touches the Steam installation,
  never sends anything anywhere. The picture page contains pictures and sound of the game; it says so at its top and must not be published or committed.
- **Without a human:** `-Answers "p,f:too dark,s,q"` supplies the keys (a note cannot contain a comma), `-NoWindow` starts no window, `-SelfTest` runs a guided run
  with fed answers, a resume and a report in a throw-away directory and checks the results file.

A failed item is reproduced without a window from its id alone: `pwsh scripts/acceptance.ps1 -Item <id> -Capture` renders the strips of its scene(s) again into the
output directory (default `%TEMP%\cncnew-acceptance`, `-OutDir`), and the report prints that command beside every failed item.

## 3. The catalogue

85 items in 13 groups (`pwsh scripts/acceptance.ps1 -List`). Sources: the 27 boxes of the former "Sichtprüfliste 60 Hz" of `docs/DEMO-FINDINGS.md` (mapping there),
the checklists of `docs/SPECTATOR-MODE.md` §11, `docs/CLIENT-OBJECTS.md`, `docs/DEMO-SCENARIOS.md`, `docs/UI-VANILLA-FIT.md` §6/§9, `docs/INSTALLATION.md`, and
the "not proven" lists of `CLAUDE.md`; duplicates are merged, and each item names its sources in the JSON.

| Group | Items | In the window | Minutes in the window | From stills | Optional |
| --- | ---: | ---: | ---: | ---: | ---: |
| INST – installation and start | 3 | 1 | 1.0 | 0 | 2 |
| SHELL – shell and menus | 9 | 3 | 4.5 | 5 | 1 |
| HUD – HUD and command bar on the original layouts | 7 | 2 | 0.8 | 5 | 0 |
| INPUT – mouse, scrolling, keys, placement | 8 | 5 | 5.5 | 2 | 1 |
| WORLD – terrain, roads, objects, light, fog | 7 | 1 | 1.0 | 6 | 0 |
| UNIT – unit behaviour | 17 | 14 | 11.9 | 1 | 2 |
| FX – effects and muzzle flashes | 4 | 4 | 1.5 | 0 | 0 |
| SND – sound, music, Eva | 6 | 6 | 1.4 | 0 | 0 |
| SKIRM – skirmish against the AI | 3 | 2 | 5.5 | 1 | 0 |
| SPEC – spectator mode | 9 | 7 | 8.4 | 1 | 1 |
| MISS – campaign mission: intro and scripts | 4 | 4 | 5.5 | 0 | 0 |
| RATE – 60 Hz against 30 Hz | 5 | 4 | 4.2 | 1 | 0 |
| PERF – performance feel | 3 | 3 | 0.0 | 0 | 0 |
| **Sum** | **85** | **56** | **51.2** | **22 (10.6 min)** | **7 (10.4 min)** |

**The time is planned, not measured with a person:** per scene about half a minute of loading (counted on the first item of the scene) plus the length of the scene
or of the actions asked for; the scene lengths are the measured ones (for example `acc-march` 49 s, `tank-duel` 30 s, `capture-building` 32 s). The window part
without the optional items is 51 minutes in 18 scenes; the seven optional items (installer, uninstall, the old saved game, the classic mouse setup, the factory exits
of China and GLA, two starts of the spectator for the seed) add 10 minutes and are only asked with `-IncludeOptional`. The three PERF items cost nothing: they are
judged while other scenes run.

| Id | Title | Min | Window or stills | Scene |
| --- | --- | ---: | --- | --- |
| INST-01 | The installer creates a game directory | 3.0 | window (motion), optional | `install` |
| INST-02 | Start through Start-CnCNew.cmd | 1.0 | window (motion, sound) | `start-installed` |
| INST-03 | Uninstalling removes only our own directory | 1.0 | window (motion), optional | `uninstall` |
| SHELL-01 | Main menu: looks | 0.5 | stills | `shell-stills` |
| SHELL-02 | Sub-menus: single player, difficulty, challenge, briefing | 0.5 | stills | `shell-stills` |
| SHELL-03 | Skirmish setup, map selection, loading screen, score screen | 0.5 | stills | `shell-stills` |
| SHELL-04 | Options, sliders, quit dialog | 0.5 | stills | `shell-stills` |
| SHELL-05 | Font and texts | 0.5 | stills | `shell-stills` |
| SHELL-06 | Transitions and button states in the window | 1.5 | window (motion) | `shell` |
| SHELL-07 | Menu music and clicks | 0.5 | window (sound) | `shell` |
| SHELL-08 | Starting a skirmish and a mission from the menu | 2.5 | window (motion) | `shell` |
| SHELL-09 | A game saved at 30 Hz is refused | 2.0 | window (motion), optional | `shell-30` |
| HUD-01 | Command bar, idle | 0.5 | stills | `hud-tour` |
| HUD-02 | Selection: portrait, construction menu, tooltip | 0.5 | stills | `hud-tour` |
| HUD-03 | Building with a queue, money, power | 0.5 | stills | `hud-tour` |
| HUD-04 | Game menu (Esc) | 0.5 | stills | `hud-tour` |
| HUD-05 | Several units selected | 0.5 | stills | `hud-multiselect` |
| HUD-06 | Messages at the top left | 0.3 | window (motion) | `tank-duel` |
| HUD-07 | Radar | 0.5 | window (motion) | `skirmish` |
| INPUT-01 | Mouse: left selects, right orders | 1.0 | window (motion) | `exit-america-all` |
| INPUT-02 | The original's mouse setup (`--mouse classic`) | 1.0 | window (motion), optional | `skirmish-classic` |
| INPUT-03 | Scrolling, rotating, zooming | 1.0 | window (motion) | `skirmish` |
| INPUT-04 | Construction hot keys, including S, F and E | 1.0 | window (motion) | `exit-america-all` |
| INPUT-05 | Placement: preview on a legal and on a refused spot | 0.5 | stills | `placement` |
| INPUT-06 | Placing and building with the mouse | 1.5 | window (motion) | `skirmish` |
| INPUT-07 | Pointer and waypoints | 0.5 | stills | `cursor-move` |
| INPUT-08 | Pointer changes and rally point in the window | 1.0 | window (motion) | `exit-america-all` |
| WORLD-01 | Terrain and start base | 0.5 | stills | `skirmish` |
| WORLD-02 | Low camera: walls, selection brackets | 0.3 | stills | `match-lowangle` |
| WORLD-03 | Roads | 0.5 | stills | `roads-junctions` |
| WORLD-04 | Unexplored terrain | 0.3 | stills | `match-fog` |
| WORLD-05 | Models in the viewer | 0.5 | stills | `viewer-commandcenter` |
| WORLD-06 | Grown base, team colours, shadows | 0.5 | stills | `skirmish-autoplay` |
| WORLD-07 | Fog: revealing and darkening in the window | 1.0 | window (motion) | `skirmish` |
| UNIT-01 | Starting, driving, braking | 1.0 | window (motion) | `acc-march` |
| UNIT-02 | Turning | 0.2 | window (motion) | `acc-march` |
| UNIT-03 | Arriving and standing | 0.2 | window (motion) | `acc-march` |
| UNIT-04 | A place of one's own at the goal | 0.3 | window (motion) | `acc-march` |
| UNIT-05 | Boarding | 1.0 | window (motion) | `acc-board` |
| UNIT-06 | Leaving a transport | 0.3 | window (motion) | `acc-board` |
| UNIT-07 | Factory exits, America | 0.7 | window (motion) | `exit-america-all` |
| UNIT-08 | Factory exits, China | 1.2 | window (motion), optional | `exit-china-all` |
| UNIT-09 | Factory exits, GLA | 1.2 | window (motion), optional | `exit-gla-all` |
| UNIT-10 | War factory up close: door and seat of the model | 0.5 | stills | `exit-america-warfactory` |
| UNIT-11 | Turrets and fire release | 1.0 | window (motion) | `tank-duel` |
| UNIT-12 | Projectile paths | 0.8 | window (motion) | `acc-artillery` |
| UNIT-13 | Jets | 2.0 | window (motion) | `jet-strafe` |
| UNIT-14 | Capturing a building | 1.2 | window (motion, sound) | `capture-building` |
| UNIT-15 | Wheels, steering, suspension | 1.1 | window (motion) | `acc-wheels` |
| UNIT-16 | Repair and the wrench symbol | 1.1 | window (motion) | `acc-repair` |
| UNIT-17 | Construction | 1.0 | window (motion) | `acc-build` |
| FX-01 | Muzzle flashes | 0.8 | window (motion) | `acc-firefight` |
| FX-02 | Tracers and hits | 0.2 | window (motion) | `acc-firefight` |
| FX-03 | Explosions, smoke, wreck | 0.3 | window (motion) | `tank-duel` |
| FX-04 | Smoke trails and fire at the impact | 0.2 | window (motion) | `acc-artillery` |
| SND-01 | Shot and impact sounds | 0.2 | window (sound) | `tank-duel` |
| SND-02 | Fire fight: many sounds at once | 0.1 | window (sound) | `acc-firefight` |
| SND-03 | Tick sound of the capture | 0.0 | window (sound) | `capture-building` |
| SND-04 | Construction and Eva | 0.1 | window (sound) | `acc-build` |
| SND-05 | Music, unit voices, Eva in a skirmish | 1.0 | window (sound) | `skirmish` |
| SND-06 | Sound of the mission scripts | 0.0 | window (sound) | `mission-usa01` |
| SKIRM-01 | Building a base | 3.0 | window (motion) | `skirmish` |
| SKIRM-02 | Fighting | 2.5 | window (motion, sound) | `skirmish` |
| SKIRM-03 | A skirmish the computer plays | 0.5 | stills | `skirmish-autoplay` |
| SPEC-01 | Start and readability | 0.5 | stills | `spectator` |
| SPEC-02 | Panels and buttons | 1.5 | window (motion) | `spectator` |
| SPEC-03 | Zoom and overview | 0.7 | window (motion) | `spectator` |
| SPEC-04 | Fog views | 0.5 | window (motion) | `spectator` |
| SPEC-05 | The AI's camera | 1.0 | window (motion) | `spectator` |
| SPEC-06 | Speed, pause, no orders | 0.7 | window (motion) | `spectator` |
| SPEC-07 | End of the match | 1.5 | window (motion) | `spectator` |
| SPEC-08 | The AI builds vehicles and gathers | 2.5 | window (motion) | `spectator-china` |
| SPEC-09 | Chance and repeatability | 1.0 | window (motion), optional | `spectator-random` |
| MISS-01 | Intro of the mission GC_ChemGeneral | 1.0 | window (motion) | `mission-chem` |
| MISS-02 | Intro of MD_USA01: texts, fades, counters | 1.5 | window (motion, sound) | `mission-usa01` |
| MISS-03 | Building after the intro | 1.0 | window (motion) | `mission-chem` |
| MISS-04 | The mission's scripts run | 2.0 | window (motion) | `mission-chem` |
| RATE-01 | Movement: 60 Hz against 30 Hz | 1.2 | window (motion) | `acc-march` |
| RATE-02 | Effects and sound: 60 Hz against 30 Hz | 1.0 | window (motion, sound) | `tank-duel` |
| RATE-03 | Capture: 60 Hz against 30 Hz | 1.0 | window (motion, sound) | `capture-building` |
| RATE-04 | Mission intro: 60 Hz against 30 Hz | 1.0 | window (motion) | `mission-chem` |
| RATE-05 | Roads: 60 Hz against 30 Hz | 0.5 | stills | `roads-overview` |
| PERF-01 | Smoothness in a skirmish | 0.0 | window (motion) | `skirmish` |
| PERF-02 | Loading time | 0.0 | window (motion) | `skirmish` |
| PERF-03 | Spectator at eight times speed | 0.0 | window (motion) | `spectator` |

"Window" items need motion or sound, so no still picture can decide them; "stills" items are decided on the page. Each item's instruction and expectation (one or two
sentences for a non-developer, naming what the original does where that is the yardstick) stand in `scripts/acceptance/catalogue.json` and on the page.

## 4. The scenes

44 scenes, 38 of them with a capture; every item names one (some a few more for the page). Where an older demo shows the item it is used (`tank-duel`, `capture-building`, `jet-strafe`, the
factory exits, skirmish, mission, shell, spectator). Seven staged scenes are new (`src/CnCNew.Game/Live/AcceptanceScenes.cs`, the `LiveScenario` pattern, retail
templates by name only), each with a headless audit (`tools/CnCNew.PresentationTool/AcceptanceAudit.cs`, `pwsh scripts/audit-scenarios.ps1 -Scenario <name>`):

| Scene | Shows | What the audit checks |
| --- | --- | --- |
| `acc-march` | three Crusaders, a Humvee and six Rangers sent to one point and back | every vehicle at rest and not turning at the end of a leg, everybody within 110 of the goal, no two bounding circles inside each other, drawn in every frame; detours and soldiers still walking are reported as known finding S2 |
| `acc-wheels` | a Humvee on a zig-zag, a dozer there and back | every corner reached, wheels turned with the distance, the Humvee's wheels steer and return to straight, the dozer's body nods and settles |
| `acc-board` | five Rangers board a Humvee, are driven, let out, and again | everybody inside before the drive, path to the vehicle not longer than 1.5 times the straight line, everybody outside and alive after the evacuation; riders inside each other afterwards are reported as known finding S1 |
| `acc-artillery` | a Tomahawk launcher and an Inferno Cannon shell a command centre | both fire at least twice, the target is damaged, a projectile is drawn at least 15 above the ground |
| `acc-repair` | a dozer repairs a power plant at 48 % health | damaged model state after the hit, undamaged state again from half health, whole at the end, the wrench shown from the first repair step and gone when whole |
| `acc-firefight` | a Gattling tank and three Rangers against six Rebels | shots on both sides, deaths, the Gattling drawn in firing states but not all the time |
| `acc-build` | a dozer builds a power plant and a barracks | both finished with full health, progress never falls back, construction states drawn, **no damage state while under construction** |

A check that fails for a documented finding of the simulation is printed as `KNOWN` and does not fail the audit, so that anything new still does. New scenario
orders for these scenes: `Enter`, `Evacuate`, `Repair`, `Build`, `AttackMove` and the staging order `Damage` (`LiveScenario.cs`); existing scenes use none of them,
and `Game.Tests` (`ScenarioTests`) runs every scene twice and compares the state hash. `scenario-audit` also learned `--drawables-at N` (everything a frame draws
besides scenery, and every simulated object it does not draw) and lists skipped object creations.

Client options added for the acceptance (`src/CnCNew.Client.Stride`): `--caption TEXT` (a line over the picture and in the window title, `|` starts a new line) and
`--loop` (a staged scene starts again from a fresh world when it has run its length; not in a smoke test).

## 5. Pre-check by the agent

`-Capture -All` was run three times (the last time after all fixes: 38 scenes, 658 pictures and 24 sound captures in 36.5 minutes, 450 MB, no scene with trouble), and the agent looked at every contact sheet and at single frames where the sheet
was too small. **A reading of a still picture is not an acceptance.** It is recorded only in `scripts/acceptance/precheck.json` and shown beside the human verdict.

| Reading | Items |
| --- | ---: |
| looks right | 20 |
| looked wrong, fixed | 4 |
| looks wrong, open | 8 |
| cannot be judged from stills | 53 |

| Id | Reading | Note |
| --- | --- | --- |
| INST-01 | cannot be judged from stills | not run by the agent in this package; the installer was measured by its own package (docs/INSTALLATION.md) |
| INST-02 | cannot be judged from stills | needs a person at the installed directory |
| INST-03 | cannot be judged from stills | not run |
| SHELL-01 | looks right | logo, frame, six buttons, hover and pressed pictures differ; nothing to compare it with but the layout data. The large black area is the missing background scene (known) |
| SHELL-02 | looks right | each page shows its own buttons; three difficulty levels; the custom-mission button is grey |
| SHELL-03 | looks right | map preselected, slot entries readable; the score screen is sparse - not compared with the original |
| SHELL-04 | cannot be judged from stills | sliders and Yes/No row are there; logo and button fragments of the main menu stay visible to the right of the options box - whether the original shows that is not known to the agent |
| SHELL-05 | cannot be judged from stills | nothing cut off in the pictures; likeness to the original's lettering cannot be read from our own pictures |
| SHELL-06 | cannot be judged from stills | the strip shows the frame and the buttons fading in over frames 15 to 60; smoothness needs the window |
| SHELL-07 | cannot be judged from stills | sound |
| SHELL-08 | cannot be judged from stills | needs clicking |
| SHELL-09 | cannot be judged from stills | needs clicking |
| HUD-01 | looks right | bar over the full width, radar left, side logo right, money at the top centre |
| HUD-02 | looks right | portrait, two rows of construct buttons, tooltip with name, cost and text; the tooltip covers the neighbouring buttons (known deviation) |
| HUD-03 | looks right | five Crusaders in the queue of the war factory, money dropped; the power bar is one green colour (known) |
| HUD-04 | looks right | logo and five buttons, centred |
| HUD-05 | cannot be judged from stills | ten rings and health bars; the bar shows three commands at its right end and the side logo in the portrait field - plausible, not checked against the original |
| HUD-06 | cannot be judged from stills | the line stands in every picture from frame 40 to 440 because new hits renew it; its duration needs the window |
| HUD-07 | cannot be judged from stills | needs clicking |
| INPUT-01 | cannot be judged from stills | needs a mouse |
| INPUT-02 | cannot be judged from stills | needs a mouse |
| INPUT-03 | cannot be judged from stills | needs a mouse; the speed is tested against the reference formula |
| INPUT-04 | cannot be judged from stills | needs a keyboard; scripts/check-build-hotkeys.ps1 passes 3 of 3 |
| INPUT-05 | looks right | translucent building with a green hatch on free ground, red on the command centre |
| INPUT-06 | cannot be judged from stills | needs a mouse; scripts/check-dozer-build.ps1 passes 5 of 5 |
| INPUT-07 | looks right | green four-arrow pointer over free ground; blue line with three diamonds from the dozer |
| INPUT-08 | cannot be judged from stills | needs a mouse |
| WORLD-01 | looks right | soft texture blends, command centre with lit walls, black beyond the explored circle |
| WORLD-02 | looks right | lit walls, corner brackets and bar on the command centre, ring and bar on the dozer, dozer on its tyres |
| WORLD-03 | cannot be judged from stills | junction and crossing are closed and identical at both rates; in the overview the gravel road and the rails overlap differently at 30 and 60 Hz (known, RATE-05) |
| WORLD-04 | looks right | all black |
| WORLD-05 | looks wrong, open | command centre and barracks complete; the Crusader in the viewer shows hull and barrel but no turret, with a pink patch under the hull; the dozer is seen from an angle that cannot be read. Only the model viewer: in the game scenes both look right |
| WORLD-06 | looks wrong, open | team colours and fences fine; no object casts any shadow on the ground (not built, docs/CLIENT-OBJECTS.md known limitations) - the world looks flat; the strip stays on the base and shows no fight |
| WORLD-07 | cannot be judged from stills | motion |
| UNIT-01 | cannot be judged from stills | motion; the audit gives the same paths and arrival frames at 30 and 60 Hz |
| UNIT-02 | cannot be judged from stills | motion |
| UNIT-03 | cannot be judged from stills | motion |
| UNIT-04 | looks wrong, open | finding S2 for the simulation: the four vehicles take places 36 to 82 from the goal and stand; of the six soldiers, who arrive last, two walk a wide arc around the parked group (418 and 453 units for a 234 and 244 unit trip, at rest 21 and 23 s after the order instead of 12 s), and on the way back one is still walking after 24 s |
| UNIT-05 | looks right | by numbers: every Ranger walks 67 to 82 units for 74 to 83 to the vehicle's edge and is inside after 103 to 125 frames |
| UNIT-06 | looks wrong, open | finding S1 for the simulation: after the evacuation all five Rangers stand exactly on the Humvee's centre, inside the vehicle and inside each other |
| UNIT-07 | looks right | audit green; dozers and tanks stand in front of their buildings side by side. The barracks lies below the visible section |
| UNIT-08 | looks right | audit green; Chinese command bar scheme |
| UNIT-09 | looks right | audit green |
| UNIT-10 | cannot be judged from stills | the tank appears inside the hall and leaves through the lower side; whether the door leaf is open cannot be read at this size |
| UNIT-11 | looks right | audit: every shot with 0 degrees turret error, aim held until the death; the motion itself needs the window |
| UNIT-12 | looked wrong, fixed (`d4b1f04`) | the detonation effect also played at the target the moment the weapon fired, before the projectile arrived; now once, at the impact. The missile's flight needs the window |
| UNIT-13 | cannot be judged from stills | audit green; the airfield strip shows the Raptors in their bays, one in the air east of the runway at frames 750 and 900, both parked again from frame 1650. The scene's own camera looked at empty ground between airfield and targets (the height it asks for is above the zoom limit), so the catalogue starts it looking at the airfield and captures the target area as a second strip. The flight itself needs the window |
| UNIT-14 | looks right | walk, flag, the barracks tinted in the Ranger's colour in every second picture, blue trim after frame 828; 49 flashes by the audit |
| UNIT-15 | looked wrong, fixed (`60afc45`) | Humvee and dozer drew their headlight cones as a pale beam by day; by numbers the wheels steer up to 19.8 and 30 degrees and return to 0, the dozer nods up to 3.4 degrees. The Humvee's body hardly moves (0.05 degrees roll) - that is what its locomotor data say |
| UNIT-16 | cannot be judged from stills | the wrench is shown from the first repair step to full health (audit); it is small (about 22 pixels) and floats well above the chimney, at the place of the health bar - size and place against the original need a person |
| UNIT-17 | looked wrong, fixed (`f87c919`) | the building site was drawn in the really-damaged and then the damaged state until half of the work was done; now scaffold and crane, then the finished building |
| FX-01 | cannot be judged from stills | motion; the audit sees the Gattling in a firing state in 134 of 600 frames, with the slow, mean and fast states |
| FX-02 | cannot be judged from stills | motion; large brown smoke puffs stand where the Rebels are hit - their size against the original needs a person |
| FX-03 | looks wrong, open | finding S3 for the simulation: the Battlemaster vanishes 6 frames after its death with nothing but the smoke of the last shell - no death explosion, no flying turret, no wreck. Fixed besides (d4b1f04): every impact was drawn twice |
| FX-04 | cannot be judged from stills | motion; fire on the building after the Inferno shells is visible |
| SND-01 | looked wrong, fixed (`d4b1f04`) | 28 detonation sounds for 14 shots at both rates, now 14; clipped samples in the capture 181 before, 62 after. How it sounds needs ears |
| SND-02 | cannot be judged from stills | sound |
| SND-03 | cannot be judged from stills | sound |
| SND-04 | cannot be judged from stills | sound |
| SND-05 | cannot be judged from stills | sound |
| SND-06 | cannot be judged from stills | sound |
| SKIRM-01 | cannot be judged from stills | needs playing |
| SKIRM-02 | cannot be judged from stills | needs playing |
| SKIRM-03 | looks right | scaffold and crane, then power plant, barracks, supply centre, war factory; the camera stays on the base |
| SPEC-01 | cannot be judged from stills | header and panel are there; the panel text is very small at 1280x720 - readability needs a person |
| SPEC-02 | cannot be judged from stills | the four panels appear in the strip |
| SPEC-03 | cannot be judged from stills | the overview shows the whole map; outside the map the picture is one light blue |
| SPEC-04 | cannot be judged from stills | the view of one player shows black outside his explored area |
| SPEC-05 | cannot be judged from stills | motion |
| SPEC-06 | cannot be judged from stills | motion |
| SPEC-07 | looks wrong, open | the winner line appears; but with seed 7 the match is over after 4:49 and China never has an army (army value 0 in every picture) - a matter for the AI package, not for the spectator mode |
| SPEC-08 | looks wrong, open | with seed 11 China is defeated after 4:07 without having fielded a vehicle; a matter for the AI package, which is being reworked in parallel - pick another seed there |
| SPEC-09 | cannot be judged from stills | needs two starts |
| MISS-01 | cannot be judged from stills | camera tour over the map, command bar hidden until frame 800, placeholder lines '(speech: ...)' at the bottom; the motion needs the window |
| MISS-02 | looks wrong, open | the low camera angles of this intro look wrong: for the first 17 seconds a rock face fills the top third and one flat light blue the rest, at frames 1400 to 1500 the ground runs along the top edge with the same blue below. There is no sky (known) and the scripted pitch near the horizon is probably not what the original shows; cause not found |
| MISS-03 | cannot be judged from stills | scripts/check-dozer-build.ps1 passes |
| MISS-04 | cannot be judged from stills | needs playing |
| RATE-01 | cannot be judged from stills | motion; the pictures of both rates show the same positions frame for frame |
| RATE-02 | cannot be judged from stills | motion and sound; the same sound events at both rates (14 shots, 14 detonations) |
| RATE-03 | looks right | audit: 49 flashes and the capture in frame 828 at both rates |
| RATE-04 | cannot be judged from stills | motion |
| RATE-05 | cannot be judged from stills | the two overview pictures differ where the gravel road crosses the rails; which is right the agent cannot say |
| PERF-01 | cannot be judged from stills | feel |
| PERF-02 | cannot be judged from stills | the headless runs take 30 to 40 s from start to the first frame on this machine |
| PERF-03 | cannot be judged from stills | feel |

### 5.1 Defects found and fixed

| Commit | What was seen | Cause | Fix |
| --- | --- | --- | --- |
| `d4b1f04` | tank duel: 28 detonation sounds for 14 shots at 30 and at 60 Hz; an explosion at the target the moment the gun fired | `FxExecutor.WeaponFired` still played `ProjectileDetonationFX` at the target when a projectile weapon fired ("the simulation resolves shots instantly") although the simulation has long flown projectile objects and requests the effect at the detonation | the immediate effect is removed; 14 sounds for 14 shots, clipped samples of the capture 181 → 62; test in `FxTests` |
| `f87c919` | a building site is drawn really damaged, then damaged, until half of the work is done (`acc-build` audit: `REALLYDAMAGED AWAITING_CONSTRUCTION`) | `ConditionDeriver.Derive` took the damage state from the health, which grows with the work. The original does not evaluate the visual damage state of a structure under construction (`ActiveBody::internalChangeHealth`, `ActiveBody.cpp`: "we do not show visual changes for damage states when things are under construction") | no damage condition while `UnderConstruction`; the audit of `acc-build` fails on it |
| `60afc45` | Humvee, dozer and other vehicles draw a pale beam in front of them in every picture | the headlight cones of the models were drawn by day. The original hides every sub-object whose name contains `HEADLIGHT` unless the drawable has the `NIGHT` condition (`W3DModelDraw::hideAllHeadlights`, `W3DModelDraw.cpp`) | the object layer does the same and counts what it hid (`headlightsHidden=` in the client's report) |
| `a433f90` | three of the new scenes had their subject behind the command bar or half outside the picture | camera and one lane of the scenes | positions only |

None of these touches `src/CnCNew.Simulation` or `src/CnCNew.Data`.

### 5.2 Found and not fixed (presentation, shell, scenes)

- **No shadows.** No object casts a shadow on the ground; the world looks flat (WORLD-06). Not built (`docs/CLIENT-OBJECTS.md`, known limitations).
- **Intro of `MD_USA01`** (MISS-02): at the low camera angles of this intro a rock face fills the top third and one flat light blue the rest for the first 17 seconds,
  and at frames 1400–1500 the ground runs along the top edge of the picture. There is no sky (known); whether the scripted pitch is applied as in the original was
  not found out.
- **Model viewer** (WORLD-05): the Crusader shows hull and barrel but no turret and a pink patch under the hull; only `--model-path`, the game scenes are right.
- **Wrench symbol** (UNIT-16): about 22 pixels wide and well above the chimney of the power plant, at the place of the health bar; size and place against the
  original (`Drawable::drawHealing`) were not compared.
- **Options screen** (SHELL-04): logo and button fragments of the main menu stay visible to the right of the options box.
- **`jet-strafe`**: the scene asks for a camera height of 520, which is above the player's zoom limit, so its own camera shows empty ground between airfield and
  targets. The catalogue starts it with `--look` at the airfield and captures the target area as a second strip (`jet-strafe-target`); the scene itself is unchanged.
- **`exit-<faction>-all`**: the barracks lies below the visible section (the same zoom limit); the instruction says to scroll.
- **Both spectator matches end within five minutes** (seed 7: America wins at 4:49, China never has an army; seed 11: GLA wins at 4:07, China fields no vehicle).
  That is the skirmish AI, which another package reworks in parallel; SPEC-07 and SPEC-08 are marked accordingly.
- **Large brown smoke puffs** where infantry is hit in `acc-firefight`, and a **pinkish** tint in the last smoke of the tank duel: size and colour against the
  original need a person (FX-02, FX-03).

## 6. Findings for the simulation

Not changed here (the rule of this package: no change of simulation behaviour). Each is reproduced by a scene without a window.

**S1 – Passengers stand inside their transport after getting out.** Scene `acc-board`, items UNIT-06. Seen: after the evacuation order all five Rangers stand exactly
on the Humvee's centre – distance 0.0 to the vehicle and to each other (audit: "smallest margin between two bounding circles −20.4") – and stay there. Cause:
`OpenContain`-style removal in `src/CnCNew.Simulation/Modules/BuiltIn/ContainModules.cs` (`Remove(…, positionAtContainer)`) puts the rider on the container's position
and nothing moves him; `SimulationWorld.EvacuateContainer` (`World/SimulationWorld.Containers.cs`) does no more; the key `ScatterNearbyOnExit` is typed as opaque
(`ModuleCatalog.cs`) and never read. The original: `TransportContain::onRemoving` (`GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Contain/TransportContain.cpp`,
line 330 ff.) calls `scatterToNearbyPosition(rider)` when `m_scatterNearbyOnExit` is set (the default); `OpenContain::scatterToNearbyPosition`
(`OpenContain.cpp`, line 688 ff.) draws a random angle and a distance between one and one and a half bounding radii of the container, puts the rider on the
container's centre, tells him to ignore the container as an obstacle and to walk to that point (`aiMoveToPosition`).

**S2 – Soldiers who arrive after the vehicles walk around the parked group.** Scene `acc-march`, item UNIT-04. Seen (identical at 30 and 60 Hz): ten units are sent
to one point. The vehicles arrive first and stop 0 to 82 from the goal. Of the six Rangers two leave the straight line 44 units before the goal, walk 70 units
south or north, around the group and back in: 418 and 453 units for a trip of 234 and 244, at rest 636 and 688 frames after the order where the others need 360 to
386. On the way back one Ranger is still walking after 720 frames (24 s, 470 units for 222) and another needs 564. Cause: the free place beside an occupied goal is
asked for near the goal, every 15th frame (`AIUpdateInterface.GoalSpotCheckDue`, `Modules/BuiltIn/Phase5Modules.cs`; `SimulationWorld.TryFreeGoalSpot`/`FindFreeGoal`,
`World/SimulationWorld.ExitGoals.cs`), and the place found may lie behind the parked vehicles, around which the path finder then plans
(`StationaryBlockersAhead`). The original gives every unit of a group its own destination when the order is given: `AIGroup::computeIndividualDestination`
(`GameLogic/AI/AIGroup.cpp`, line 480) keeps each unit's offset from the group centre, and `Pathfinder::adjustDestination` (`GameLogic/AI/AIPathfind.cpp`, line 5355,
with `checkDestination`, line 4924) moves it to the nearest free cell; the unit then walks one path to a place that is its own from the start
(see also `docs/AI-OBSERVED-2026-10-08.md` §3.3). This is the "one stands, two walk around him" of 8 October in a player's group move.

**S3 – A destroyed vehicle vanishes without explosion, debris or wreck.** Scene `tank-duel`, item FX-03. Seen: the Battlemaster dies in frame 382, is drawn dying
for a few frames and is gone by frame 390; `scenario-audit --name tank-duel --drawables-at 390` lists no other object, no skipped creation and no second death.
The data give its `SlowDeathBehavior` an effect and a creation list for the final phase (`FX = FINAL FX_BattleMasterExplosionOneFinal`,
`OCL = FINAL OCL_ChinaTankBattleMasterDebris`: the hull `ChinaVehicleBattleMasterDeadHull`, which lies for 20 s and sinks after 14 s, and two pieces of debris).
Cause: `SlowDeathBehavior` (`Modules/BuiltIn/Phase5BlockerModules.cs`) models the lottery and the destruction timer only; its own comment lists `FX`/`OCL`/`Weapon`
per phase as not modelled. The original: `SlowDeathBehavior::doPhaseStuff` (`GameLogic/Object/Behavior/SlowDeathBehavior.cpp`, line 339 ff.) plays the effect list,
creates the object list and fires the weapon of the phase – at the beginning (`beginSlowDeath`, line 332), at the midpoint and at the end (`update`, lines 466 and
472). It concerns every unit and building with a slow death: death explosions, wrecks, debris and death weapons are all missing.

## 7. Verification (9 October 2026)

| Check | Result |
| --- | --- |
| `dotnet build CnCNew.slnx` | 0 warnings (incremental build; not repeated with deleted `bin`/`obj` of the client) |
| `dotnet build tools/CnCNew.UiRenderTool` | 0 warnings |
| `Ui.Tests` | passed |
| `Presentation.Tests -- --content-dir <content>` | passed |
| `Game.Tests` (Release) `-- --content-dir <content>` | passed, including the 22 scenes of `ScenarioTests`; no golden file changed |
| `scripts/audit-scenarios.ps1` and `-Scenario exits` | 3 of 3 and 9 of 9 passed |
| the seven new scenes, `audit-scenarios.ps1 -Scenario acc-… [-TickRate 30]` | 7 of 7 passed at 60 and at 30 Hz (`acc-march` with 4, `acc-board` with 2 known findings) |
| `scripts/check-dozer-build.ps1`, `check-build-hotkeys.ps1` | 5 of 5, 3 of 3 |
| `acceptance.ps1 -List`, `-SelfTest` (guided run with fed answers, resume, report, a results path inside the repository refused) | passed |
| `-Stills`, `-Resume`, `-Report` against the real content with `-Answers` and `-NoWindow` | passed (3 passed, 2 failed with their notes, 2 skipped, 78 open in the report) |
| `acceptance.ps1 -Item WORLD-04 -Capture`, `-Capture -All` | 0.4 min; 38 scenes in 36.5 min on this machine, exit code 0, every scene audit on the page green |
| windowed: `--loop` restarts a scene (seen twice in the log), `--caption` drawn (capture) | passed |
| `acceptance.ps1 -Compare -SideBySide` with two windows | passed: two client windows of 960x540 ran at once, were placed side by side (60 Hz at 0,40, 30 Hz at 976,40), and the verdict was recorded after both were closed; the two windows load independently and are not started in step |
| `scripts/audit-publish.ps1 -ContentDir <content>` | at `73ebdff`: 5,084 blobs in the history, 0 equal to a game file, 0 binary; one path with a verbatim game line of 60 characters or more, older than this package (`tests/CnCNew.Simulation.Tests/Tests.BattlePlan.cs`, one `key = value` line) |

`SimTool input-manifest check` and `reference-manifest check` were not run: no commit of this package touches `src/CnCNew.Simulation` or `src/CnCNew.Data`.

## 8. How results flow back

1. The owner runs the three commands (or parts of them, in any order, on any day – the results file of the day is continued, `-Resume` takes the latest one).
2. `pwsh scripts/acceptance.ps1 -Report` writes `%LOCALAPPDATA%\CnCNew\acceptance\report-<date>.md`: counts, the failed items with the note, scene, rate and the
   command that reproduces them without a window, then passed, skipped and open items, each with the agent's reading beside it.
3. He pastes the report to the developer (or the agent). A failed item's id is enough to start: `-Item <id> -Capture` for the strips, `audit-scenarios.ps1
   -Scenario <scene>` for the numbers, the window with `-Item <id>`.
4. A fixed item is asked again by `-Item <id>`; the newer verdict replaces the older one in the report, the file keeps both.

## 9. Not done, not verified

- No human has used any of this. The guided run was driven only with fed answers and without windows; the single keys on a real console (`[Console]::ReadKey`) and
  the wait for a window a person closes were not exercised by a person.
- The planned minutes are estimates (§3).
- The run from an installation (`-InstallDir`, `-UseInstalledClient`) was not executed; only the developer checkout was.
- Scenes without a staged scene: the cheering after a capture, the superweapon clock, Chinook at the supply dock, water, bridges, night.
- The 30 Hz pictures of the page were compared with the 60 Hz ones by eye on the contact sheets only.
