# Stage 1 – Port

As of: 10 October 2026, state `2777a90`; the fidelity package A (Phase 31) entered at `bab51c3`. Frame: [PROGRAMME.md](PROGRAMME.md). Numbers are quoted from the documents that
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
- **Numbers (9 October 2026):** 12,435 definitions of the retail base, 2,115 objects, compatibility baseline 731 entries (713 since Phase 31: 18 opaque keys of the containers and the slow deaths typed).
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
- **Deliverables:** (a) **done (Phase 31)** the fidelity package A – F-03, F-04, F-05 – each one cause, one commit, one manifest (D-12,
  decided 10 October 2026; brief `docs/handoffs/HANDOFF-PHASE31.md`; result `docs/SIMULATION-CORE.md` §44; follow-ups F-34 to F-37);
  (b) the acceptance run by the project owner with a verdict per item; every finding reproduced, fixed or named as a limit
  with a reason; (c) the click-through shell → campaign → mission → save/load → completion and skirmish setup → match →
  result, reported per screen "worked / did not work / worked with defects"; (d) a human hears music, EVA and effects;
  (e) a human plays one campaign mission and one skirmish to the end; (f) the known 30 Hz findings F-06 to F-09 and
  F-10, F-11 fixed or decided (D-13 to D-15) - **done for F-06 to F-10 in Phase 39** (`docs/SIMULATION-CORE.md` §53: F-07 to F-10 fixed, F-06 the
  original's behaviour; with F-43 to F-47 found on the way; F-11, the rank system of D-15, is a phase of its own); the first reading of Zero Hour Reforged entered F-48 to F-57 (Phase 40).
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
| F-03 | Passengers stand inside their transport after getting out | `OpenContain::exitObjectViaDoor`, `AIExitState::update`, `TransportContain::onRemoving` (scatter only for a destroyed transport), `GarrisonContain::exitObjectViaDoor` | acceptance S1 | **done** (Phase 31: implementation `f9abff8`, manifest `12533c6`; `docs/SIMULATION-CORE.md` §44.1). Left out, named there: door animation, container rally point, `GoAggressiveOnExit` and the other exit keys, bones of damaged model states |
| F-04 | A group sent to one point gets no own destination per unit; late units walk around the parked group | `AIGroup::groupMoveToPosition`, `computeIndividualDestination`, `Pathfinder::adjustDestination`, `AIInternalMoveToState::onEnter` | acceptance S2 | **done** (Phase 31: implementation `39a1656`, manifest `1a191cd`; `docs/SIMULATION-CORE.md` §44.2; `CommandTypes.Version` 16, replay 13). Left out, named there: column moves along a group path, tightening, player formations, path-cost test |
| F-05 | `SlowDeathBehavior` runs no per-phase effect, creation list or weapon: no death explosion, debris or wreck | `SlowDeathBehavior::beginSlowDeath`, `doPhaseStuff`, `update` | acceptance S3 | **done** (Phase 31: implementation `94711fb`, manifest `bab51c3`; `docs/SIMULATION-CORE.md` §44.3) for effect list, creation list, weapon and the midpoint draw of `SlowDeathBehavior`; the debris is F-36, the subclasses F-37 |
| F-06 | `SupplyMachine` overwrites a player's command that arrives in the frame between "wants to deliver" and the dock command | `SupplyTruckWantsToPickUpOrDeliverBoxesState::update` (orders the dock without a check), `StateMachine::updateStateMachine` (update before the transition conditions), `GameLogic::update` (commands before the object updates) | Phase 23 (`RATE-INVARIANCE-GROUND.md` §6) | **the original's behaviour** (Phase 39, `docs/SIMULATION-CORE.md` §53.4): the original loses the command as well; not changed in the original profile. Zero Hour Reforged lists it among EA's bugs ("A supply truck told to go somewhere as it pulls off the dock goes there") - a candidate for the rule profile (Phase 40) |
| F-07 | A magazine without reload time fires once more through a second command in the same tick | `Weapon::getStatus`, the attack state fires once per frame | Phase 23 | **done** (Phase 39: `6968ff4`; no manifest commit - both manifests are 60 Hz runs, and above 30 Hz the port already waited a frame; `docs/SIMULATION-CORE.md` §53.3) |
| F-08 | `QuantityModifier` is not applied (one Red Guard instead of two) | `ProductionUpdate::queueCreateUnit`, `ProductionUpdate::update` | Phase 23 | **done** (Phase 39: `b42a210`, manifest `c5fc4aa`; §53.1). Not modelled: reskin equivalence (`isEquivalentTo`), no shipped modifier needs it |
| F-09 | A produced unit comes into being at completion, not when the gate opens | `ProductionUpdate::update`, `updateDoors`, `setHoldDoorOpen`, `ParkingPlaceBehavior::reserveDoorForExit` | Phase 23 | **done** (Phase 39: `08def33`, manifest `60ca6d5`; §53.2) |
| F-10 | `<This Team>` in a team's create script is resolved for AI sessions only, not in the mission VM | `ScriptEngine::executeScript`, `getTeamNamed` | after Phase 23 | **done** (Phase 39: `83510c7`, manifest `e62d747`; §53.5) |
| F-11 | No rank system: skill points per kill, `Rank.ini` thresholds, purchase points per rank; the session gives one purchase point every 90 s | `Player::addSkillPointsForKill`, `addSciencePurchasePoints` | Phase 23 (`SKIRMISH-AI-ORIGINAL.md` §6.2 no. 7) | decision D-15 |
| F-12 | Solo bonuses of the campaign on easy and hard (`SOLO_AI_EASY/HARD` rate of fire, `HumanSoloPlayerHealthBonus_*`) are not applied | `Player::friend_applyDifficultyBonusesForObject` | Phase 17 | decision D-6 |
| F-13 | A standard skirmish ends after one hour of game time, also for a human | the original has no such limit | Phase 23 | decision D-8 |
| F-14 | A unit with an immediate command leaves the factory by the shortest route instead of through the gate (the gate variant is built and measured, not merged) | exit modules | Phase 23 | decision D-2 (which behaviour has the original?) |
| F-15 | Twelve deviations of the original AI option (`SKIRMISH-AI-ORIGINAL.md` §6.2) | `AIPlayer`, `AISkirmishPlayer` | Phase 23 | decision D-16 (default: leave as named deviations) |
| F-16 | `PowerSabotage` has no consumer (no underpowered state) | sabotage crates | Phase 14 | **done** (Phase 40: `8b6a7bb` / `7e235b8`, `0c8d463`, `ba4183c`; §54.10, §54.11) |
| F-17 | Approximations of target acquisition (`docs/SIMULATION-CORE.md` §33.5) | `AIUpdateInterface::getNextMoodTarget` | Phase 17 | fix per item |
| F-18 | Reasons for rejecting a building site are not those of the original | `BuildAssistant::isLocationLegalToBuild` | Phase 17 | fix |
| F-19 | Approximations of Phase 14 (`docs/SIMULATION-CORE.md` §30): `DemoTrapUpdate` without weapon-slot command; ferry (`RailedTransport*`) without command type and retail map; weapon choice only `PREFER_MOST_DAMAGE`; collisions only for moving objects | per module | Phase 14 | fix per item or limit with reason |
| F-20 | Bridges: state only; bridge terrain, layers and scaffold construction missing; AI bridge repair missing | `Bridge*`, `AIPlayer::updateBridgeRepair` | Phases 6, 14, 23 | fix when a map or the AI needs it; otherwise limit |
| F-21 | No team state machine of the original ("idle" without AI state; `TEAM_STATE_IS`) | `AIStateMachine`, `TeamState` | Phase 13 | fix |
| F-22 | 19 script actions are recorded without a consumer (attitude, rank, wreck lifetime, bonuses …); 9 missing (used in no campaign map) | `ScriptActions.cpp` | Phase 14 | fix per consumer; the 9 missing stay counted |
| F-23 | Presentation: 11 model states without simulation behaviour (`EXPLODED_*`, `JAMMED`, `PANICKING`, `RAPPELLING`, `PREORDER`, `DOCKING_BEGINNING` …); no blob or contact shadow; `TransparentWaterDepth` unevaluated; drawable and smudge particles, light pulses and ray effects not drawn; camera-aligned billboards and HLOD aggregates unresolved; `AnimationMode.Manual` frame 0; environment mapping with a fixed view direction | `W3D*Draw`, `W3DWater`, `ParticleSys` | Phases 7, 15 | fix per item; visual acceptance decides the order |
| F-24 | User interface: display mode, language, aspect ratio and text scaling not reachable (**Phase 38:** display mode, the wide-screen view, text size and sound device are reachable through "More options" and applied by the client; language is not - only English is supported); custom maps list empty; "Eigene Mission" without function; generals-powers bar, experience window, diplomacy and chat not loaded; credits end only with Escape; battle scene behind the main menu black; skybox, border shadow and weather of the script outputs missing | `OptionsMenu.wnd`, `MainMenu.cpp`, `InGameUI` | Phases 8, 16, publication | fix per item |
| F-25 | Sound and mission presentation: 21 of 92 script output types not displayed (image effects, camera paths, speech subtitles); script sounds of the missions; Black Lotus data stream; drawn paratroopers; landing approach arc of the jets | `ScriptActions`, `W3DDisplay`, `JetAIUpdate` | Phase 16 | fix per item |
| F-26 | The first evaluation offset of delayed AI scripts is derived from the script name, not drawn from the logic random stream (a restored engine must not draw again) | `ScriptEngine::newMap` | Phase 23 | limit (deliberate; same spread, different individual offsets) |
| F-27 | Text rasterisation follows GDI through `GdiFontProvider` since Phase 16; pixel comparison by a human outstanding | `CreateFont`/`ExtTextOut` | Phase 16 | acceptance (S1-M11) |
| F-28 | The pathfinder is simpler than the original's: flat A* with an expansion budget, approximated footprint, escape cells with a surcharge; a failed path ends the command | `AIPathfind.cpp` (layers, zones) | Phase 6 | limit for the port; beyond the original is S3-M6 |
| F-29 | Determinism-driven: self-acquisition once per original frame of the unit above 30 Hz (decided 10 October 2026); at 30 Hz nothing differs | – | Phase 23 | decided, no action |
| F-30 | Unlocks and profile of the challenge campaigns are not modelled | `CampaignManager` | Phase 9 | fix |
| F-31 | Missions are not replays (script actions are direct world changes); truth is the snapshot | the original replays missions | Phase 9 | limit (design) |
| F-32 | Skirmish savegame unsupported (`CreateSave()` throws) | the original saves skirmishes | Phase 10 | **closed** (Phase 24: `SkirmishMatchSession.Save`/`Restore`, `SkirmishSaveGame`; `docs/SIMULATION-CORE.md` §45.3, `docs/CAMPAIGN-AND-SAVEGAMES.md` §8) |
| F-34 | `HealContain` puts a healed occupant on the building's position instead of letting it walk out through the building's exit | `HealContain::update` → `exitObjectViaDoor` | Phase 31 | **closed** (Phase 34: `c2e48e2`, manifest `3d0d276` - through the door like every `OpenContain` rider, `NumberOfExitPaths` parsed; the community fix `door-exit-to-rally` adopted after it, `docs/GGC-FIXES.md` 3.1d; `docs/SIMULATION-CORE.md` §52) |
| F-35 | The team moves of the script VM (`MOVE_TEAM_TO`, hunts, waypoint paths) and the army orders of the AI are single move commands per unit, not group orders; the VM counts a member as arrived within 40 units of the ordered point | `AIGroup::groupMoveToPosition`, `groupFollowWaypointPath` | Phase 31 | **done** for the group moves (Phase 46, §60.2; `groupFollowWaypointPath` and `groupHunt` are orders per member in the original too - the VM's arrival test is F-78; the built-in AI is not the original's) |
| F-36 | `CreateDebris` nuggets of creation lists are not simulated (the flying pieces of a wreck, 3 in `tank-duel`); each is a counted `CreationSkipped` | `ObjectCreationList.cpp`, `GenericObjectCreationNugget` with debris | Phase 31 | **done** (Phase 44, §58.4: `GenericDebris` pieces thrown by the nugget's disposition, bouncing, dying at rest; the look is the presentation's; not modelled: the client's level of detail that skips debris) |
| F-37 | `HelicopterSlowDeathBehavior`, `JetSlowDeathBehavior`, `BattleBusSlowDeathBehavior` and `NeutronMissileSlowDeathBehavior` do not play the per-phase lists of the base behaviour and do not draw its midpoint | `SlowDeathBehavior::doPhaseStuff` through the subclasses | Phase 31 | **done** (Phase 44, §58.1: the four subclasses on `SlowDeathClock` - the base's three draws, the initial, midpoint and final lists) |
| F-38 | Entering a container is decided in 2-D: a boarder enters a container that hovers above the terrain; the original waits until the container is down | `AIEnterState::update` (the goal above the terrain keeps the move going) | Phase 33 (fix review) | **closed** (Phase 34: `f3c4009`, `d58d9d0`, manifest of its own - the wait at the end of the approach, the give-up on a carried container, the touch only with an overlap in height, and the Chinook that lands for its boarders (`ChinookAIUpdate::update`, which the port lacked as well); `enter-needs-vertical-overlap` adopted after it; `docs/SIMULATION-CORE.md` §52) |
| F-39 | A rider of a `RiderChangeContain` (the combat bike) only raises the bike's level; the original sets the bike to the rider's level, zeroes the rider's experience and hands the bike's level back on leaving | `RiderChangeContain::onContaining`, `onRemoving` | Phase 33 (fix review) | **closed** (Phase 34: `a13d5de`, manifest of its own - both directions, only for a rider a rider line names, after the death check; `bike-trainable-from-rider` adopted after it; `docs/SIMULATION-CORE.md` §52) |
| F-40 | An ordered attack is checked against the player's sight at every later command and a target that is no longer detected is dropped; the original checks stealth only when approaching and pursuing, not in the fire state | `AIAttackApproachTargetState::updateInternal`, `AIAttackPursueTargetState` | Phase 33 (fix review) | review (the community fix for disarming mines depends on it) |
| F-41 | The effects an object carries continuously are missing: no `ParticleSysBone` system of a model state is created, the body's fire and smoke (`ActiveBody`, `GameData` `Auto*Particle*`) and the particle systems of `BoneFXUpdate` and `TransitionDamageFX` are never shown, a missile has no exhaust, a projectile stream is not drawn, a vehicle raises no dust, an effect at a bone or a muzzle stands at the object's origin, and `Model = NULL` is drawn as the placeholder box - the Dragon tank shows no flames, nothing smokes or burns | `W3DModelDraw::setModelState`, `ActiveBody::updateBodyParticleSystems`, `BoneFXUpdate::update`, `TransitionDamageFX::onBodyDamageStateChange`, `MissileAIUpdate::doIgnitionState`, `W3DProjectileStreamDraw::doDrawModule`, `W3DTruckDraw`/`W3DTankDraw::doDrawModule` | the project owner, by watching (16 October 2026, Phase 35) | **closed by machine** (Phase 35: `4e081b2`, `docs/PRESENTATION-FX.md` "Attached effects"; bone rotations, animated bones, the damage-type filters and the stream's texture scroll not reproduced; not seen by a human) |
| F-42 | The score screen's counters: until Phase 36 they counted only events that carry a player and showed 0 units built, lost and destroyed and 0 resources for everybody (fixed by machine in Phase 36, `31b03c2`: by the owner of the object); until Phase 37 every unit, worker and structure counted, where the original's `ScoreKeeper` counts only objects of the kinds `SCORE`, `SCORE_CREATE`, `SCORE_DESTROY` and the scoring building masks, a loss only with a causer and a kill only against an enemy | `ScoreKeeper::addObjectBuilt`, `addObjectDestroyed`, `addObjectLost`; `Object::scoreTheKill`; `Player::onUnitCreated` | the match analysis (Phase 36), whose numbers did not agree with the score screen | **closed by machine** (Phase 37, `da2168b`; not built: the money of crates, bounties, the cash hack, oil derricks and prisoners - no event -, `removeObjectBuilt` and the rebuild hole's `isRebuild`; not seen by a human) |
| F-43 | An upgrade is complete when it is bought: `PurchaseUpgradeCommand` pays and calls `CompleteUpgrade` at once, where the original queues the upgrade in the building's production queue for its build time (`UpgradeInfo.BuildTicks` is read and never waited for), with the progress bar of the command bar and a research the observer could show | `ProductionUpdate::queueUpgrade`, `ProductionUpdate::update`, `AIGroup::queueUpgrade`, `AIPlayer::buildUpgrade`, `ControlBarCommand.cpp` | the observer's research tab (Phase 37) | **done** (Phase 39: `400ea98`, manifest `f678994`; §53.6) |
| F-44 | A producer that dies keeps the money of its queue | `ProductionUpdate::onDie` -> `cancelAndRefundAllProduction` | Phase 39 (reading `ProductionUpdate.cpp` for F-43) | **done** (Phase 39: `81918dc`, manifest `c7f3def`; §53.7) |
| F-45 | An airfield queues more aircraft than it has spaces and holds the finished one back | `ProductionUpdate::queueCreateUnit`, `ParkingPlaceBehavior::shouldReserveDoorWhenQueued`, `BuildAssistant::canMakeUnit` (`CANMAKE_PARKING_PLACES_FULL`) | Phase 39 (reading `ProductionUpdate.cpp` for F-09) | **done** (Phase 39: `8f644ff`, manifest `6614543`; §53.8) |
| F-46 | A decoyed missile takes the nearest of the last `VolleySize` flares, where the original takes the newest flare that still exists | `CountermeasuresBehavior::calculateCountermeasureToDivertTo` (`--it` only past a missing flare) | Phase 39 (reading Zero Hour Reforged, `docs/REFORGED-FIXES.md` R-21) | **done** (Phase 39: `96a3508`, no manifest moved; `docs/SIMULATION-CORE.md` §53.9) |
| F-47 | A toppling structure fires its crushing weapon along the leading edge only; the width sweep of `applyCrushingDamage` is left out | `StructureToppleUpdate::applyCrushingDamage` | Phase 39 (`docs/REFORGED-FIXES.md` R-22) | **done** (Phase 39: `9671fb0`, manifest `f84e33c`; §53.10 - EA's sweep as it is; Reforged's perpendicular is a rule-profile candidate) |
| F-48 | Weapon damage reaches a body already armour-adjusted: `HighlanderBody` and `UndeadBody` cap or decide on the armoured amount, where the original caps the raw amount and applies the armour in `ActiveBody::attemptDamage` (a unit weak to the weapon dies through the cap there); damage that is not a weapon's - burning (`FlammableUpdate`), crushing, falling, healing (`attemptHealing` adjusts it as well) - reaches a body without armour, and damage modules see the armoured amount where the original passes `in.m_amount` | `HighlanderBody::attemptDamage`, `UndeadBody::attemptDamage`, `ActiveBody::attemptDamage` (`m_curArmor.adjustDamage`) | Phase 39 (`docs/REFORGED-FIXES.md` R-17; the Phase 32 note on `UndeadBody` in `docs/GGC-FIXES.md`) | **done** (Phase 40: `8622f6f`, manifest `20f22c5`; §54.15) |
| F-49 | A sniper's damage at a structure with a contain module and nobody inside is not refused (the comment of `EstimateDamage` names the rule, the code lacks it) | `Weapon::estimateWeaponDamage` (`KINDOF_STRUCTURE` and `DAMAGE_SNIPER` with `getContainCount() == 0` gives 0) | Phase 39 (`docs/REFORGED-FIXES.md` R-06) | **done** (Phase 40: `96b40dd`, no manifest moved; `docs/SIMULATION-CORE.md` §54.3) |
| F-50 | A player's radar is not modelled: the minimap always shows the map and its objects, where the original shows it only while the player has a radar (`Player::addRadar`/`removeRadar` by `RadarUpgrade` - the command center's, the radar van's -, `disableRadar` in a power outage unless `DisableProof`, the radar's edge sounds, `RadarUpgrade::onCapture`/`onDelete`) | `Player::hasRadar`, `RadarUpgrade.cpp`, `Object::onDisabledEdge` (`RadarUpgrade` branch), `Player::onPowerBrownOutChange` | Phase 40 (reading `RadarUpgrade.cpp` for `docs/REFORGED-FIXES.md` R-16) | **done** (Phase 40: `86393d1`, `ff88d52`; §54.14) |
| F-51 | The riders of a transport that cannot let them out are put out alive when it dies: the port kills or destroys riders only for a payload plane with its doors shut, where the original asks every `TransportContain` (`killRidersWhoAreNotFreeToExit`): a Chinook that is not landed (`ChinookAIUpdate::getAiFreeToExit`, also not during a combat drop for a rider that cannot rappel), a rider without a locomotor, or one that cannot stand on the ground below a transport that is not airborne - killed, or destroyed with `DestroyRidersWhoAreNotFreeToExit` | `TransportContain::killRidersWhoAreNotFreeToExit`, `isSpecificRiderFreeToExit`, `OpenContain::onDie` | Phase 40 (`docs/REFORGED-FIXES.md` R-23; the port's comment in `ContainModules.cs` still says it has no terrain to ask) | **done** (Phase 40: `b5e8f58`, input manifest `6a6a398`; §54.5) |
| F-52 | An object's last damage is never recorded: `Entity.LastDamageTick` is read - the stealth condition `TAKING_DAMAGE` (`StealthUpdate`), the script condition `SKIRMISH_SUPPLY_SOURCE_ATTACKED` (`ScriptEngine.Phase13.cs`) - but written nowhere since it was introduced (`bba189a`), so a unit that may not hide while taken under fire stays hidden and the original AI never sees its supply source attacked. The original records the frame and the `DamageInfo` in `ActiveBody::attemptDamage` (preferring, within one frame and the next, the damage whose source is a vehicle, infantry or faction structure) and in `attemptHealing` (where healing overwrites it; `StealthUpdate` ignores a healing record) | `ActiveBody::attemptDamage`, `attemptHealing` (`m_lastDamageInfo`, `m_lastDamageTimestamp`), `StealthUpdate::allowedToStealth`, `AIPlayer::isSupplySourceAttacked` | Phase 40 (`docs/REFORGED-FIXES.md` R-30) | **done** (Phase 40: `74db784`, manifest `e2170ca`; §54.1 - the stealth condition and the supply-source check read it; guard, tunnel, bone FX and topple read no record in the port) |
| F-53 | The landed hulk of a Battle Bus is not held: the original sets `DISABLED_HELD` when the thrown bus has come down, so group orders pass it by (`AIGroup` skips held members - the attack orders Reforged restores), a team does not recruit it (`Team::tryToRecruit`) and its aim state gives up a target out of range (`AIAttackAimAtTargetState::update`); the port halts the hulk and nothing more (its own comment in `VehicleModules.cs`), although it has the bit (`Entity.ScriptHeld`, set by scripts) | `BattleBusSlowDeathBehavior::update` (`setDisabled(DISABLED_HELD)`), the readers of `DISABLED_HELD` | Phase 40 (`docs/REFORGED-FIXES.md` R-05) | **done** (Phase 40: `7e63dd4`, no manifest moved; §54.4 - the group and recruit skips of held members are not modelled) |
| F-54 | The stealth garrison is not modelled: nothing of the port reads `KINDOF_STEALTH_GARRISON`. In the original a container counts its stealth-garrison riders (`OpenContain::addToContainList`); a building held only by undetected ones shows its enemies its apparent owner and an empty state (`GarrisonContain`, `m_hideGarrisonedStateFromNonallies`), an enemy may try to enter it as if empty (`ActionManager::canEnterObject`), target acquisition passes it by (`PartitionManager`) and a stealth detector reveals the occupants (`StealthDetectorUpdate`) | `OpenContain.cpp` (`m_stealthUnitsContained`), `GarrisonContain.cpp`, `ActionManager.cpp`, `PartitionManager.cpp`, `StealthDetectorUpdate.cpp` | Phase 40 (`docs/REFORGED-FIXES.md` R-18) | **done** (Phase 41, §55.9: the count, capture, the hidden state, entering, the detector, no stealth in a transport, exposure, the drawing; not modelled: the kick-out of `defect`, R-18's stale count, the tooltip owner, the minimap colour, the attack cursor) |
| F-55 | An attack never needs a line of sight: 245 objects carry `ATTACK_NEEDS_LINE_OF_SIGHT` and `AttackUsesLineOfSight = Yes` is set, so in the original such a unit does not fire through terrain or an obstacle (a structure in the pathfind cells along the line) and moves to a place with a clear view (`AIAttackState`, `AIAttackApproachTargetState`, target acquisition); the port fires through both - named as a limit in `docs/SIMULATION-CORE.md` §33 since Phase 17, never entered here | `Pathfinder::isAttackViewBlockedByObstacle`, `Weapon::isClearGoalFiringLineOfSightTerrain`, `AIStates.cpp` (its six callers), `PartitionManager.cpp` (`isViewBlockedByObstacle`) | Phase 40 (`docs/REFORGED-FIXES.md` R-09) | **done** (Phase 41: steps (a) to (e) of `docs/ATTACK-LINE-OF-SIGHT.md`, `05c31a8`, `6744770`, `8a8d661` with their manifests; §55.1). Not modelled: `checkDestination`, the vehicles' strip-back, bridges, leech range |
| F-56 | `Object::onDisabledEdge` is not modelled: when an object becomes disabled (EMP, hacked, paralysed, script-disabled) and again when it recovers, the original takes a power plant's production - with its upgrade's `EnergyBonus` and an active overcharge - from its player and gives it back, takes and gives back a radar (F-50), cancels a dozer's task and calls every module's `onDisabledEdge`; the port's disable (`AirWeaponModules.DisableUntil`, `EmpDisabledUntil`) stops movement and weapons only, so an EMP'd power plant keeps powering the base. The port keeps its disable kinds apart (EMP on the entity, `DISABLED_HELD` and script-disabled in `ScriptDisableMask`, hacked inside the hacker and sabotage modules, paralysed in others): an object-level mask of the original's `DisabledType`s with its until-ticks comes first | `Object::onDisabledEdge`, `Energy::adjustPower`, `BehaviorModule::onDisabledEdge` | Phase 40 (`docs/REFORGED-FIXES.md` R-33) | **done** (Phase 40: `dda7e16`, on the disable rule of §54.6 to §54.9; §54.12) |
| F-57 | The strategy center's gun is not switched by its battle plan: the original disables the center's turret when the module starts (`BattlePlanUpdate`, `enableTurret(false)`), enables it when the bombardment plan is active, centres it and disables it again before packing - and a disabled turret runs no state machine, so its weapon does not fire (`TurretAI::updateTurretAI`); the port never switches the turret (its comment in `BattlePlanModule.cs` takes the turret for `InitiallyDisabled`, which the shipped turret is not), so the gun can be fired on a command under every plan and without one, and the pack-up never waits for the turret | `BattlePlanUpdate::enableTurret`, `BattlePlanUpdate::update` (the bombardment branch), `TurretAI::setTurretEnabled` | Phase 40 (`docs/REFORGED-FIXES.md` R-02) | **done** (Phase 40: `296b646`, manifests `1186ea8`; §54.2) |
| F-58 | Guarding is not a state of the simulation: the original's guard (`AIGuardMachine`, `AIGuardRetaliateMachine`, `AITNGuardMachine` - a position, an object or an area; an inner range it holds and an outer range it chases to, scaled by `GuardOuterModifierHuman`/`AI`, the return to its post, retaliation against an attacker, the tunnel network's guard) is an AI state every player's and every script's guard order enters; the port has only a standing order of the human's client that walks to the point and leaves the rest to the idle target acquisition (`StandingOrders`, `StandingOrderKind.Guard`; `docs/SIMULATION-CORE.md` §33.5 names `GuardOuterModifierHuman` as without effect) | `AIGuard.cpp`, `AIGuardRetaliate.cpp`, `AITNGuard.cpp`, `AIUpdateInterface::aiGuardPosition`/`aiGuardObject`/`aiGuardArea` | Phase 40 (`docs/REFORGED-FIXES.md` R-35) | **done in part** (Phase 41, §55.10: the guard command, the guard of a position, an object and an area, the script guard actions, the player's retaliation; open: the tunnel network's guard, `JetAIUpdate`/`FlightDeckBehavior` reading the guard, the option to turn retaliation off); step 7 for `DeployStyleAIUpdate` and `JetAIUpdate` in Phase 42 (§56.2; the flight deck is a shell) |
| F-59 | No power opens its view at the target: `ViewObjectDuration`/`ViewObjectRange` were typed and never read (37 shipped powers) | `SpecialPowerModule::createViewObject` | Phase 40 (reading R-29) | **done** (Phase 40: `c4117e9`, manifest `2df0271`; §54.17) |
| F-60 | Riders never fire from inside a container (`MayAct` excluded every contained object; `PassengersAllowedToFire` unread) | `Object::isAbleToAttack`, `isPassengerAllowedToFire` per container, `GarrisonContain` fire points | Phase 41 (`docs/FIRE-FROM-CONTAINERS.md`) | **done** (Phase 41: steps 0 to 8 - `7d5c9a7` and the steps of §55.5 to §55.8; orders through the container `eb06bc0`, armed riders `1474a80`, the garrison gun `4dc82c7` - §55.13); Phase 42: clearing a garrison (`DAMAGE_KILL_GARRISONED`, the shell's `GarrisonHitKill`) and the Chinook's re-issue (§56.3); an attack on a position through a container open (no command for it) |
| F-61 | A garrisoned building stays neutral and two players can share one | `GarrisonContain::recalcApparentControllingPlayer`, `onRemoving`, `ActionManager::canEnterObject` | Phase 41 (`docs/STEALTH-GARRISON.md`) | **done** (Phase 41: `d24c085`, manifest `1887314`; §55.4); `CAN_ATTACK` and `onCollide`'s ejection covered (§56.3) |
| F-62 | A Chinook told to unload at a place let its riders out one by one through the door, and climbed away while some still waited; riders at its door did not wait for it to be down | `ChinookAIUpdate::aiDoCommand`, `ChinookEvacuateState::onEnter`, `AIExitState::update`, `ChinookAIUpdate::update` | Phase 41 (Reforged's R-34) | **done** (Phase 41: `5403e91`, manifest `76866ea`; §55.12) |
| F-63 | A weapon's shock wave is not modelled: `ShockWaveAmount`, `ShockWaveRadius`, `ShockWaveTaperOff` (11 weapons) are deferred fields; the original pushes the victims, turns them at random, stuns them while they fly and kills one that lands on its back, off a cliff or in water | `Weapon::dealDamageInternal`, `Object::attemptDamage`, `PhysicsBehavior::applyShock`, `applyRandomRotation`, `setStunned`, `testStunnedUnitForDestruction` | Phase 41 (Reforged's R-24; `docs/SHOCK-WAVE.md`) | **done** (Phase 42: the four steps of `docs/SHOCK-WAVE.md`, §56.1); the movement waits while the object flies - a named difference |
| F-64 | No attack on a place: the player's force-fire on the ground and every `FIRE_WEAPON` button at a place or at the own place (Dragon tank's fire wall, toxin tractor, Comanche rocket pods, mine clearing, battleship, the bomb truck's and convoy truck's detonation, the demo suicide) are refused by the client as unsupported; `FireWeaponPower` fires without approach or turret; `groupAttackPosition` reaches no rider or slave | `AIUpdateInterface::privateAttackPosition`, `AIAttackState` on a position (`outOfWeaponRangePosition`), `AIGroup::groupAttackPosition`, `GameLogicDispatch` (`MSG_DO_FORCE_ATTACK_GROUND`, `MSG_DO_WEAPON`, `MSG_DO_WEAPON_AT_LOCATION`), `FireWeaponPower` | Phase 42 (`docs/ATTACK-POSITION.md`) | **done** (Phase 43, §57: the state, the client, the container, `FireWeaponPower`, the scripts' buttons, the `Attack` nugget - the Scud Storm fires; not modelled: a mine clearer's continue range, a contact weapon's reachable place) |
| F-65 | The slaves of a spawner follow no attack of their spawner: `SlavesHaveFreeWill` is read and has no effect; in the original a spawner's slaves without free will attack what it attacks (`AIGroup::groupAttackObject`/`groupAttackPosition` -> `SpawnBehavior::orderSlavesToAttackTarget`/`orderSlavesToAttackPosition`) | `SpawnBehavior.cpp`, `AIGroup.cpp` | Phase 43 (F-64 step 3) | **done** (Phase 44, §58.2: a forced attack of the slaves on an object, the attack on a place; not modelled: `orderSlavesToGoIdle` - the port has no stop order) |
| F-66 | The objects a creation list makes are not thrown: `SEND_IT_OUT`, `SEND_IT_FLYING`, `SEND_IT_UP`, `RANDOM_FORCE` and `WHIRLING` give an object with a physics module a force and spin rates in the original (`doStuffToObj`), and draw their values from the logic stream; the port placed the object and drew none of them | `ObjectCreationList.cpp` (`doStuffToObj`, `calcRandomForce`) | Phase 44 (F-36) | **done** (Phase 44, §58.3; the rest in Phase 45, §59.9 - `ExtraBounciness` is never read by the original, `INHERIT_VELOCITY` reads a fall or flight only) |
| F-67 | A dozer never gets bored: the original's idle dozer repairs the closest hurt structure of its player within `BoredRange` once it has stood idle without a task for more than `BoredTime`, and looks again every `BoredTime`; the port's dozer stood | `DozerAIUpdate.cpp` (`DozerPrimaryIdleState::update`, `findObjectToRepair`), `ActionManager::canRepairObject` | Phase 44 | **done** (Phase 44, §58.5; the mine-clearing half of boredom is F-69) |
| F-68 | The GLA worker always runs the supply truck's brain: the original's worker acts as a dozer (builds, repairs, gets bored) while its supply brain is busy and nothing forces it to gather, and as a supply truck while it gathers or docks | `WorkerAIUpdate.cpp` (`WorkerStateMachine`, `supplyTruckSubMachineWantsToEnter`, `supplyTruckSubMachineReadyToLeave`) | Phase 44 (F-67) | **done** (Phase 45, §59.1) |
| F-69 | No mine clearing: the weapon set flag `MINE_CLEARING_DETAIL` is never raised (an idle or bored dozer and worker raise it, a `USES_MINE_CLEARING_WEAPONSET` button sets it for the selection), a bored dozer does not look for an enemy or neutral mine to disarm (`findMine`), and `ContinueAttackRange` (after a kill, the next mine of the same player in range) is not read; the client refuses the mine-clearing button | `DozerAIUpdate.cpp` (`findMine`), `WorkerAIUpdate::update`, `AIGroup::setMineClearingDetail`, `AIStates.cpp` (`ContinueAttackRange`) | Phase 44 (F-67) | **done** (Phase 45, §59.7, four steps) |
| F-70 | Above 30 Hz a bored builder (F-67) began its repair a frame late: the idle state gave the order where the builder's frame ends and slept to the next frame end, so the module missed the first tick of the next frame and the walk began half a step late | the port's own defect (Phase 44), `DozerPrimaryIdleState::update` | Phase 45 (F-68's test) | **done** (Phase 45, §59.2) |
| F-71 | In the default match America against China on Alpine Assault a ranger left an ambulance for another place at 60 Hz than at 30 Hz (frame 6,510): the door's bones turned with the container's heading inside its frame step, while its centre was read where the frame ends | the frame convention, `docs/SIMULATION-CORE.md` §43 | Phase 44 (tier 4) | **done** (Phase 45, §59.3) |
| F-72 | Above 30 Hz the idle state of a builder (F-67) missed a task that ended in the first tick of the builder's frame: it did not stamp that frame and was bored a frame early | the port's own defect (Phase 44), `DozerPrimaryIdleState::update` | Phase 45 (rate comparison) | **done** (Phase 45, §59.4) |
| F-73 | A heal aura (`AutoHealBehavior` with a `Radius`, the ambulance's) compared positions as they stood in the tick: above 30 Hz a mover inside its frame step was healed after it had left the radius at 30 Hz | `AutoHealBehavior::update`, `docs/SIMULATION-CORE.md` §43 | Phase 45 (rate comparison) | **done** (Phase 45, §59.5) |
| F-74 | Above 30 Hz the movement of an object whose flight ended in a frame's first tick decided its first step in that frame's later ticks, half a step early | the frame convention, `docs/SIMULATION-CORE.md` §43 | Phase 45 (rate comparison) | **done** (Phase 45, §59.6) |
| F-75 | `AutoFindHealingUpdate` counted its countdown in ticks from whatever tick it ran: above 30 Hz a module that resumed inside a frame (after a hold) kept counting off the object's frames, and the scan came a frame early | `AutoFindHealingUpdate::update` | Phase 45 (rate comparison) | **done** (Phase 45, §59.8) |
| F-76 | In the default match America against China on Bitter Winter a Humvee's turret recentred a tick early at 60 Hz (frame 25,631): a turret that stands still decides in every tick above 30 Hz, and saw a victim killed by delayed damage in a frame's first tick in the frame's second tick | the frame convention, `docs/SIMULATION-CORE.md` §43 | Phase 45 (rate comparison) | **done** (Phase 46, §60.1) |
| F-77 | On Bitter Winter the hulk a dying Chinese supply truck leaves (`DeadChinaSupplyTruckHulk`, its slow death's `OCL FINAL`) and its debris had another heading (frame 33,486) and another place (frame 39,040) at 60 Hz than at 30 Hz: a creation list read its source inside the source's frame step |\.previousHealth$|constructionProgress| **done** (Phase 47, §61.1, two steps) | the frame convention, `docs/SIMULATION-CORE.md` §43 | Phase 46 (rate comparison) | fix |
| F-78 | The script VM's waypoint following went on only within 40 units of a waypoint and ordered the unit to it again otherwise; the original's `AIFollowWaypointPathState` goes on when the move to the waypoint has ended, whether it arrived or not - a unit whose move ended farther away (a waypoint inside a structure) stood for good | `AIFollowWaypointPathState::update` | Phase 46 (F-35) | **done** (Phase 47, §61.2) |
| F-79 | On Bitter Winter a Chinese supply truck at work in a supply center's dock (object 3732) left it a frame early at 60 Hz (frame 39,929) when the center was destroyed: the dock machine runs every tick while the dock works on its docker and saw the dock gone in the frame's second tick |\.previousHealth$|constructionProgress| **done** (Phase 48, §62.1) | the frame convention, `docs/SIMULATION-CORE.md` §43 | Phase 47 (rate comparison) | fix |
| F-80 | In the default matches America against China on Armored Fury (frame 29,669) and Bitter Winter (frame 49,244) a Crusader's next target check differed between 30 and 60 Hz: above 30 Hz the second leg of the exit path is ordered with the last tick of the unit's frame, after that frame's acquisition, and the unit decided again a frame later than at 30 Hz | `AIUpdateInterface::privateMoveToPosition`, the exit path of `exitObjectViaDoor`, the frame convention (`docs/SIMULATION-CORE.md` §43, §42) | Phase 48 (rate comparison) | **done** (Phase 49, §63.1) |
| F-81 | In the pairing ChinaTank against AirForce on Armored Fury (frame 18,840) and Death Valley (frame 12,964) a horde member's count differed between 30 and 60 Hz: a module of a disabled object asked again in the next tick and woke inside a frame of its object when the disable ended there | `GameLogic::update`, the frame convention (`docs/SIMULATION-CORE.md` §43) | Phase 49 (rate comparison) | **done** (Phase 49, §63.3) |
| F-82 | In the pairing ChinaTank against AirForce on Bitter Winter (frame 17,687) two gattling tanks kept a Chinook that flew out of sight for another frame at 60 Hz: the sight of an object was read inside its frame step | the frame convention, rule 3 (`docs/SIMULATION-CORE.md` §43) | Phase 49 (rate comparison) | **done** (Phase 49, §63.4) |
| F-83 | `SpecialAbilityUpdate` stages the two capture powers only: every other ability is a shell, its order is refused as `SpecialPowerHandlerMissing` and a player or a computer player cannot use it - Colonel Burton's and Kell's timed and remote charges, the Tank Hunter's TNT, the Missile Defender's laser lock, the hacker's and Black Lotus's disable and cash hacks, the booby trap, the disguise as a vehicle, the Helix's napalm and nuke bombs, the laser-guided howitzer (found through the rejection counts of `ai-match`, Phase 49) | `SpecialAbilityUpdate::triggerAbilityEffect`, `StickyBombUpdate` | Phase 49 | fix |
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
