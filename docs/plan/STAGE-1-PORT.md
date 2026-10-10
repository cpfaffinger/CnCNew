# Stage 1 – Port

As of: 10 October 2026, state `2777a90`. Frame: [PROGRAMME.md](PROGRAMME.md). Numbers are quoted from the documents that
measured them (date and commit given there) and are re-measured before use.

## 1. Goal, rule, yardstick

The original *Generals – Zero Hour*, as it is, in a new engine: campaign, missions and skirmish complete, no intended or
unintended change of behaviour, with the tooling that turns the user's own Steam installation into runtime content and
checks it. **Rule:** a change in this stage makes the port more like the original; a deliberate deviation is Stage 3.
**Yardstick:** the original (its source code read in `references/`, its data, a human against the running original);
measured by coverage, staged scenes, the acceptance catalogue (`docs/ACCEPTANCE.md`) and the fidelity debt register (§3).

The stage is **not finished**: everything that tools can verify is built and measured; what only a human can judge (look,
sound, playability) is outstanding (S1-M11), and the fidelity register has entries without a disposition.

## 2. Milestones

### S1-M1 Import, formats and runtime content — done

- **Delivered (27 September 2026; Phases 1 and 2 of the first plan):** BIG reader, import VFS with the search priority of
  the original, materialised archive-free content (schema 2, origin and SHA-256 manifest), readers for INI, STR, MAP/SCB/WAK,
  WND, W3D (meshes, materials, hierarchies, animations), DDS/TGA, ANI/CUR, WAV, MP3, BIK (container only), full-content
  audits (`AssetTool verify`, `IniTool audit`, `ImageTool audit-tga`, `W3dTool audit`, `MapTool audit`). Documents:
  `docs/CONTENT-PIPELINE.md`, `docs/W3D-MATERIALS.md`, `docs/W3D-SKELETONS.md`, `docs/AUDIO.md`, `docs/MAP-SCRIPTS.md`.
- **Numbers (master installation, 9 October 2026):** 25,608 runtime files; 224 INI files; 150 maps; 8,894 W3D files.
- **Open:** none inside the milestone. The Bink video decoder is a permanent limit (F-02).

### S1-M2 Typed game data and compatibility baseline — done

- **Delivered (27/28 September 2026; Phase 3 and block 0 of Phase 4):** `CnCNew.Data` compiles the INI content into the
  immutable `CompiledDefinitionGraph` (retail load order, `DefaultThingTemplate` inheritance, Add/Remove/ReplaceModule,
  strict value grammar, name tables from the reference); module catalogue of 205 types without factories; every unknown key,
  module, enum or reference is a counted compatibility case in `baselines/compatibility.json` (`IniTool compat-check`).
  Documents: `docs/TYPED-DEFINITIONS.md`, `docs/PHASE3-COMPATIBILITY.md`.
- **Numbers (9 October 2026):** 12,435 definitions of the retail base, 2,115 objects, compatibility baseline 731 entries.
- **Open:** none.

### S1-M3 Deterministic simulation core — done

- **Delivered (28 September 2026; Phase 4):** `CnCNew.Simulation` depends only on `CnCNew.Data`; fixed tick, fixed-point
  numerics, original RNG with named streams, monotonic IDs, command queue with versioned command types and counted
  rejections, object/module/timer/event life cycle in eight tick phases, canonical snapshots, incremental state hash,
  replay format, 216,000-step stress test; `SourceRules` forbids wall-clock time, ambient random numbers, floating point,
  threads, file access and reflection in the core. Document: `docs/SIMULATION-CORE.md`.
- **Changed later by Stage 2:** the rate is a value of the world (60 Hz default), `Fixed` is Q39.24, `WorldSnapshot` 10,
  replay 12, `CommandTypes.Version` 15 – the core's contract is unchanged, its numbers are those of Stage 2.
- **Open:** F-01 (RNG comparison with a compiled original; permanent until an original can be compiled or recorded).

### S1-M4 Game rules, combat, economy, production — done, with fidelity debts

- **Delivered (29 September to 1 October 2026; Phases 5, 6, 7 package E1, 10):** economy and supply (docks, depots,
  centres, carriers, workers, Chinook), upgrades (`UpgradeMux`), sciences, direct and area combat, weapon-slot model
  (primary/secondary/tertiary, clips, locks, set selection; Phase 14), experience and veterancy, teams and diplomacy,
  repair, containment (garrison, transport, tunnel, parachute), special powers, crates, creation lists, construction
  process, selling, rally points, build and rule limits (prerequisites, `MaxSimultaneousOfType`, overcharge, capture),
  collision (toppling, crushing, height death), target acquisition by the units themselves (`AttackMoveCommand`, Phase 17),
  firing stages and `PreAttackDelay` (Phase 16). Documents: `docs/GAME-RULES.md`, `docs/SIMULATION-CORE.md`,
  `docs/SIM-*.md`.
- **Open (fidelity register):** F-03 to F-09, F-11 to F-14, F-16 to F-19. The three findings of the acceptance pre-check
  (F-03 to F-05) are the next package (D-12).

### S1-M5 World, terrain, navigation, vision, construction — done, with named limits

- **Delivered (30 September 2026 and later; Phases 6, 10, 15):** `MapData` from all 150 maps, `TerrainMap` (cells, heights,
  water, start positions), `NavigationGrid` (deterministic A*, footprints, escape cells, replanning, caches since Phase 19,
  stationary blockers since Phase 15), `VisionMap` (visible/explored per player, sharing, stealth, detectors with height line
  of sight, permanent reveal), construction sites, local avoidance, formations (`asTeam`, Phase 14), bridge state (Phase 14).
- **Open:** F-20 (bridge terrain, layers, scaffold, AI bridge repair), F-21 (team state machine, "idle"), F-28 (the
  pathfinder is simpler than the original's: flat, approximated footprint, budget). Beyond the original (shared group paths,
  cell budgets) is S3-M6.

### S1-M6 Module coverage: every object of the retail data — done

- **Delivered (3 October 2026; Phases 7 to 14):** all **183 simulation modules** of the catalogue are implemented,
  **2,115 of 2,115** retail objects instantiable; module uses covered 16,558 of 19,553 (the rest are the 22 presentation
  modules, which the simulation never instantiates). Document: `docs/SIMULATION-CORE.md` §30 (tables per module, source
  and approximation).
- **Open:** the approximations named in §30 are entries of the fidelity register (F-16 to F-19); the 22 presentation
  modules are the client's (S1-M8: model states, F-23).

### S1-M7 Script VM, missions, campaign, savegames — done headless

- **Delivered (1 to 3 October 2026; Phases 9 to 14):** deterministic script VM over the decoded World Builder scripts –
  **109 of 109 condition types**, 187 action types executed, 129 passed to the presentation as `ScriptOutput`, 19 recorded
  (value stored, no consumer), 9 missing (used in no campaign map); sequential and team-event scripts, team queues,
  reinforcement with transport, path flight of the particle beam; `MissionSession`/`MissionSnapshot` (version 3);
  `CnCNew.Game/Campaign` (17 campaigns, 85 missions, personas of the challenge), `SaveGame` (version 3; missions are not
  replays, truth is the snapshot); `CampaignTool` (`list`, `audit`, `playthrough`, `reachability`, `drive`, `outputs`,
  `coverage`). Documents: `docs/SCRIPT-ENGINE.md`, `docs/CAMPAIGN-AND-SAVEGAMES.md`.
- **Measured (9 October 2026, 30/60/120 Hz):** objective driver victory 24/24 and defeat 24/24 through the map's own end
  script; audit 24/24 deterministic and identical after save/load; reachability clean 24/24; elimination end probes 13/24
  and 15/24. The driver replaces player actions by interventions in the world and verifies the **script chain, not the
  playability**; the commands-only agent (0 world writes) wins `MD_GLA04` and `MD_CHI01`, `MD_CHI02` only after 43 game
  minutes.
- **Open:** F-10 (`<This Team>` in the mission VM), F-22 (recorded actions without consumer), F-30 (unlocks and profile);
  playability by a human is S1-M11.

### S1-M8 World presentation — built, not accepted

- **Delivered (30 September to 5 October 2026; Phases 7, 8, 15):** terrain (heightfield, blend textures, water, fog of war,
  minimap, roads), objects (W3D hierarchies, skeletons, animations, 95 of 106 model states, turrets following the shots,
  wheels, steering and suspension, team colours, fog darkening, hidden unexplored scenery), effects (particles, tracers,
  decals, lighting), HUD overlay (selection, health bars, command lines, rally points). `PresentationTool model-audit` draws
  1,585,401 of 1,585,417 triangles. Documents: `docs/PRESENTATION-WORLD.md`, `docs/PRESENTATION-OBJECTS.md`,
  `docs/PRESENTATION-FX.md`, `docs/CLIENT-TERRAIN.md`, `docs/CLIENT-ROADS.md`, `docs/CLIENT-OBJECTS.md`, `docs/CLIENT-FX.md`,
  `docs/MODEL-CONDITIONS-*.md`.
- **Open:** the visual checklist of `docs/CLIENT-OBJECTS.md` is not ticked off by a human; the checksums of
  `scripts/capture-world.ps1` are provisional; F-23 (model states without simulation behaviour, shadows, water depth, rare
  effect kinds, billboards, environment mapping); D-7 (road draw order depends on the rate, Stage 2).
- **Acceptance:** by a human through `scripts/acceptance.ps1` (groups world, units, buildings, effects).

### S1-M9 Input, shell, HUD, texts, original layouts — built, not accepted

- **Delivered (30 September to 9 October 2026; Phases 8, 12, 16, 17, publication):** input in the order of
  `GameClient.cpp` (selection, command modes, hotkeys, `CommandMap.ini` 96 entries); shell from the real WND layouts (main
  menu, options, skirmish setup with map list, campaign and challenge selection, difficulty question, briefing, load list,
  saving in the game, result screen; transitions; screen stack); HUD (command bar, minimap, cameos, tooltips with
  requirements, message list with the original texts, superweapon clocks, letterbox after `renderLetterBox`); texts on the GDI
  font path with sentence wrapping; settings persisted; **the user interface fits the unmodified layouts of the Steam master**
  (`docs/UI-VANILLA-FIT.md`: command bar over the full width, scheme by its own `ScreenCreationRes`, 54 screen hashes).
  Documents: `docs/PRESENTATION-INPUT.md`, `docs/GAME-SHELL.md`, `docs/GAME-HUD.md`, `docs/TEXT-AUDIT.md`,
  `docs/UI-WND.md`, `docs/UI-TEXT-AND-SETTINGS.md`, `docs/CLIENT-UI-RENDERING.md`.
- **Open:** F-24 (options not reachable, custom maps, generals-powers bar, experience window, diplomacy, chat, "Eigene
  Mission", menu battle scene, skybox and weather); the click-through by a human (S1-M11). Multiplayer screens are Stage 4;
  a modern UI is Stage 3 (D-21).

### S1-M10 Sound, cursor, waypoints, mission in the client — built, never heard or seen

- **Delivered (4 to 5 October 2026; Phases 16, 17):** `AudioDirector`/`GameAudio` wired in the client (weapons, effects,
  building, EVA, voices, UI, music; `waveOut` output because Stride's audio layer crashes; `--audio-capture`, `--mute`);
  software mouse pointer from the original cursor files with context and modifier changes; waypoint paths and target
  markers; the mission director shows script camera, letterbox, fades, input lock, subtitles with typing sound, counters,
  banners and script sounds and counts every script output (69 of 92 types displayed, 2 in the simulation, 21 not, with a
  reason). Documents: `docs/GAME-AUDIO.md`, `docs/PRESENTATION-INPUT.md` §11/§12, `docs/SCRIPT-ENGINE.md` §7a.
- **Open:** F-25 (the 21 output types, script sounds of the missions, Black Lotus data stream, drawn paratroopers, landing
  approach arc); all runs were device-free – **nothing was heard**.

### S1-M11 Playability and acceptance by a human — open (the acceptance goal of the stage)

- **What exists:** the acceptance catalogue and guided run (`docs/ACCEPTANCE.md`, `scripts/acceptance.ps1`: 85 items in 13
  groups, `-Capture -All` stills unattended, 56 window items in around 51 minutes, `-Report`); staged scenes
  (`docs/DEMO-SCENARIOS.md`, seven `acc-*` scenes, `scripts/audit-scenarios.ps1`); the demo scripts (`scripts/demo-*.ps1`);
  the visual checklist for 60 Hz (`docs/DEMO-FINDINGS.md`); the commands-only agent as the headless measure of
  playability (`CampaignTool drive --commands-only`); three viewings by a human so far (4 October twice, 8 October one AI
  match at 30 Hz), each with corrections (`docs/DEMO-FINDINGS.md`, `docs/AI-OBSERVED-2026-10-08.md`).
- **Deliverables:** (a) the fidelity package A – F-03, F-04, F-05 – each one cause, one commit, one manifest (D-12);
  (b) the acceptance run by the project owner with a verdict per item; every finding reproduced, fixed or named as a limit
  with a reason; (c) the click-through shell → campaign → mission → save/load → completion and skirmish setup → match →
  result, reported per screen "worked / did not work / worked with defects"; (d) a human hears music, EVA and effects;
  (e) a human plays one campaign mission and one skirmish to the end; (f) the known 30 Hz findings F-06 to F-09 and
  F-10, F-11 fixed or decided (D-13 to D-15).
- **Acceptance:** the items of the catalogue carry a verdict by a human; no open finding without a disposition.
- **Note:** the commands-only agent is a measure, not an acceptance; making the built-in AI strong enough to win the
  challenge maps is S3-M1.

### S1-M12 Installer and clean content from the Steam master — built, not accepted

- **Delivered (9 October 2026):** `baselines/steam-master.json` describes the one supported source version (350 files,
  only paths, sizes, SHA-256); `scripts/install.ps1` (logic in `CnCNew.Import`: `InstallationMaster`, `InstallationCheck`,
  `GameInstaller`; `AssetTool check-install | describe-install | install | check-export`) checks the installation file by
  file, exports every file into a new directory (`%LOCALAPPDATA%\CnCNew\Game`: `content`, `bin`, `install.json`,
  `Start-CnCNew.cmd`) and only reads the Steam installation; `scripts/dev-content.ps1` puts the same content into `game/`;
  `scripts/setup-dev.ps1` restores a developer machine. Documents: `docs/INSTALLATION.md`, `BUILDING.md`.
- **Open (F-33):** no human has started the installed game through `Start-CnCNew.cmd`; the search for Steam outside the
  default path is untested; the export contains the original program files, which the client does not need; the content
  manifest hash applies only under Windows; the client needs .NET 10 on the machine (not self-contained → R1 package).

### S1-M13 The skirmish AI of the original as a faithful computer opponent — built as an option

- **Delivered (8/9 October 2026; side product of Phase 23):** `ScriptedSkirmishSession` runs the C++ half of
  `AIPlayer`/`AISkirmishPlayer` and the script library (`SkirmishScripts.scb`) with three difficulty levels; its whole state
  lies in state, hash and savegame from the start; selectable in client, shell and spectator (`--ai original
  --difficulty easy|medium|hard`), in `SimTool ai-match`, `ai-census`, `perf`. Cost at 60 Hz with eight players 110–119 µs
  per tick (built-in AI 36 µs). Document: `docs/SKIRMISH-AI-ORIGINAL.md`.
- **Decision (10 October 2026):** the built-in `SkirmishAi` stays the default; the original AI is an option.
- **Open (F-15, D-16):** twelve named deviations (§6.2 there): prerequisite-free build path, placement without shroud, map
  objects by default only structures and warehouses, gatherers by effect, factory search over all structures, science
  points on a timer instead of the rank system (F-11), bridge repair, rebuild hole, cluster mines, human only in the first
  slot, first evaluation offset derived from the name. Not accepted by a human in the window.

### S1-M14 Tooling of the port — done, grows

`AssetTool`, `IniTool`, `MapTool`, `ImageTool`, `W3dTool`, `SimTool` (reference runs, manifests, surveys, performance),
`CampaignTool`, `PresentationTool`, `UiRenderTool`, the test programs (one per project, no framework), the scripts
(install, developer content, demos, captures, audits, acceptance, `test.ps1`, `audit-publish.ps1`, `perf-profile.ps1`).
Every new check of the plan is a command of one of these tools first.

### R1 Port release — open

Passed when (PROGRAMME.md §6): the acceptance by a human is done and every finding has a disposition; the fidelity register
has no entry without a disposition; the installer was run by a human from a Steam installation through `Start-CnCNew.cmd`;
a client package for Windows (self-contained or with a documented .NET requirement) and the documentation for humans
exist; tier 4 is green at the state of the package. The items of the former Phase 30 that belong here: 1 (starting state),
2 (open acceptances of Phases 15 to 17), 5 (diagnostics for the client), 6 (dependencies and legal matters), 7 (clean-room
build, client package), 8 (documentation for humans).

## 3. Fidelity debt register

Every known place where the port behaves differently from the original, or where that is not verified. "Fix" means Stage 1
work with its own manifest commit; "decision" waits for the project owner (PROGRAMME.md §7); "limit" is named and not
planned to change. New deviations are entered here before they are fixed or left.

| ID | Deviation | Original (source) | Found | Disposition |
|---|---|---|---|---|
| F-01 | The RNG sequence was never compared against a compiled original; golden masters are derived from the source code | `RandomValue.cpp` | Phase 4/5 | limit until an original can be compiled or recorded |
| F-02 | No Bink video decoder; movies are counted (`Stats.SkippedMovies`) and skipped | Bink SDK (proprietary) | Phase 7 | limit; substitute display (still image, text) is R1 work |
| F-03 | Passengers stand inside their transport after getting out | `TransportContain::onRemoving`, `OpenContain::scatterToNearbyPosition` | acceptance S1 | **fix (D-12, package A)** |
| F-04 | A group sent to one point gets no own destination per unit; late units walk around the parked group | `AIGroup::computeIndividualDestination`, `Pathfinder::adjustDestination` | acceptance S2 | **fix (D-12, package A)** |
| F-05 | `SlowDeathBehavior` runs no per-phase effect, creation list or weapon: no death explosion, debris or wreck | `SlowDeathBehavior::doPhaseStuff` | acceptance S3 | **fix (D-12, package A)** |
| F-06 | `SupplyMachine` overwrites a player's command that arrives in the frame between "wants to deliver" and the dock command | – | Phase 23 (`RATE-INVARIANCE-GROUND.md` §6) | fix (D-13) |
| F-07 | A magazine without reload time fires once more through a second command in the same tick | – | Phase 23 | fix (D-13) |
| F-08 | `QuantityModifier` is not applied (one Red Guard instead of two) | `ProductionUpdate` | Phase 23 | fix (D-13) |
| F-09 | A produced unit comes into being at completion, not when the gate opens | `ProductionUpdate`, exit modules | Phase 23 | fix (D-13) |
| F-10 | `<This Team>` in a team's create script is resolved for AI sessions only, not in the mission VM | `ScriptEngine` | after Phase 23 | fix (D-14) |
| F-11 | No rank system: skill points per kill, `Rank.ini` thresholds, purchase points per rank; the session gives one purchase point every 90 s | `Player::addSkillPointsForKill`, `addSciencePurchasePoints` | Phase 23 (`SKIRMISH-AI-ORIGINAL.md` §6.2 no. 7) | decision D-15 |
| F-12 | Solo bonuses of the campaign on easy and hard (`SOLO_AI_EASY/HARD` rate of fire, `HumanSoloPlayerHealthBonus_*`) are not applied | `Player::friend_applyDifficultyBonusesForObject` | Phase 17 | decision D-6 |
| F-13 | A standard skirmish ends after one hour of game time, also for a human | the original has no such limit | Phase 23 | decision D-8 |
| F-14 | A unit with an immediate command leaves the factory by the shortest route instead of through the gate (the gate variant is built and measured, not merged) | exit modules | Phase 23 | decision D-2 (which behaviour has the original?) |
| F-15 | Twelve deviations of the original AI option (`SKIRMISH-AI-ORIGINAL.md` §6.2) | `AIPlayer`, `AISkirmishPlayer` | Phase 23 | decision D-16 (default: leave as named deviations) |
| F-16 | `PowerSabotage` has no consumer (no underpowered state) | sabotage crates | Phase 14 | fix |
| F-17 | Approximations of target acquisition (`docs/SIMULATION-CORE.md` §33.5) | `AIUpdateInterface::getNextMoodTarget` | Phase 17 | fix per item |
| F-18 | Reasons for rejecting a building site are not those of the original | `BuildAssistant::isLocationLegalToBuild` | Phase 17 | fix |
| F-19 | Approximations of Phase 14 (`docs/SIMULATION-CORE.md` §30): `DemoTrapUpdate` without weapon-slot command; ferry (`RailedTransport*`) without command type and retail map; weapon choice only `PREFER_MOST_DAMAGE`; collisions only for moving objects | per module | Phase 14 | fix per item or limit with reason |
| F-20 | Bridges: state only; bridge terrain, layers and scaffold construction missing; AI bridge repair missing | `Bridge*`, `AIPlayer::updateBridgeRepair` | Phases 6, 14, 23 | fix when a map or the AI needs it; otherwise limit |
| F-21 | No team state machine of the original ("idle" without AI state; `TEAM_STATE_IS`) | `AIStateMachine`, `TeamState` | Phase 13 | fix |
| F-22 | 19 script actions are recorded without a consumer (attitude, rank, wreck lifetime, bonuses …); 9 missing (used in no campaign map) | `ScriptActions.cpp` | Phase 14 | fix per consumer; the 9 missing stay counted |
| F-23 | Presentation: 11 model states without simulation behaviour (`EXPLODED_*`, `JAMMED`, `PANICKING`, `RAPPELLING`, `PREORDER`, `DOCKING_BEGINNING` …); no blob or contact shadow; `TransparentWaterDepth` unevaluated; drawable and smudge particles, light pulses and ray effects not drawn; camera-aligned billboards and HLOD aggregates unresolved; `AnimationMode.Manual` frame 0; environment mapping with a fixed view direction | `W3D*Draw`, `W3DWater`, `ParticleSys` | Phases 7, 15 | fix per item; visual acceptance decides the order |
| F-24 | User interface: display mode, language, aspect ratio and text scaling not reachable; custom maps list empty; "Eigene Mission" without function; generals-powers bar, experience window, diplomacy and chat not loaded; credits end only with Escape; battle scene behind the main menu black; skybox, border shadow and weather of the script outputs missing | `OptionsMenu.wnd`, `MainMenu.cpp`, `InGameUI` | Phases 8, 16, publication | fix per item |
| F-25 | Sound and mission presentation: 21 of 92 script output types not displayed (image effects, camera paths, speech subtitles); script sounds of the missions; Black Lotus data stream; drawn paratroopers; landing approach arc of the jets | `ScriptActions`, `W3DDisplay`, `JetAIUpdate` | Phase 16 | fix per item |
| F-26 | The first evaluation offset of delayed AI scripts is derived from the script name, not drawn from the logic random stream (a restored engine must not draw again) | `ScriptEngine::newMap` | Phase 23 | limit (deliberate; same spread, different individual offsets) |
| F-27 | Text rasterisation follows GDI through `GdiFontProvider` since Phase 16; pixel comparison by a human outstanding | `CreateFont`/`ExtTextOut` | Phase 16 | acceptance (S1-M11) |
| F-28 | The pathfinder is simpler than the original's: flat A* with an expansion budget, approximated footprint, escape cells with a surcharge; a failed path ends the command | `AIPathfind.cpp` (layers, zones) | Phase 6 | limit for the port; beyond the original is S3-M6 |
| F-29 | Determinism-driven: self-acquisition once per original frame of the unit above 30 Hz (decided 10 October 2026); at 30 Hz nothing differs | – | Phase 23 | decided, no action |
| F-30 | Unlocks and profile of the challenge campaigns are not modelled | `CampaignManager` | Phase 9 | fix |
| F-31 | Missions are not replays (script actions are direct world changes); truth is the snapshot | the original replays missions | Phase 9 | limit (design) |
| F-32 | Skirmish savegame unsupported (`CreateSave()` throws) | the original saves skirmishes | Phase 10 | closed by S2-M7 (Phase 24) |
| F-33 | Installer: Steam outside the default path untested; export contains the original program files; manifest hash Windows-only; client not self-contained | – | publication | fix in R1 |

## 4. Human acceptance backlog

What only a human can judge, collected from `docs/DEMO-FINDINGS.md`, `docs/ACCEPTANCE.md`, `docs/CLIENT-OBJECTS.md`,
`docs/TICKRATE-MIGRATION.md` §14.7 and the "Rest of Phase 17" (`docs/handoffs/HANDOFF-PHASE17.md`): world rendering
(models, bones, states, fog, roads), sound (`waveOut`), mouse cursor, waypoints, texts, shell transitions, mission in the
client (intro, objective, completion), message list, letterbox, tooltips, units firing by themselves, attack-move,
skirmish setup with one computer army, spectator mode, factory exits, scroll speed, mouse assignment, build keys, the
installed game through `Start-CnCNew.cmd`, and everything at 60 Hz. The run is `pwsh scripts/acceptance.ps1`; findings go
into `docs/DEMO-FINDINGS.md` with number, cause and status.

## 5. Permanent limits of the stage

Bink video (F-02); the RNG comparison (F-01) as long as no original can be compiled or recorded; missions are not replays
(F-31); the derived evaluation offset (F-26); the simpler pathfinder as the port's pathfinder (F-28; the better one is an
option in Stage 3).
