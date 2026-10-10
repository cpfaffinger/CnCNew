> **Stage:** 1 – Port · **Milestone:** S1-M11 Playability and acceptance by a human · **Phase:** 17
> **Status:** ended without its acceptance goal (6 October 2026); the rest is in the section "Rest of Phase 17" at the end
> **Predecessor:** HANDOFF-PHASE16.md · **Successor:** HANDOFF-PHASE18.md · **Results in:** docs/SIMULATION-CORE.md §33–§36, docs/SCRIPT-ENGINE.md §9, docs/GAME-HUD.md, docs/DEMO-FINDINGS.md
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

N+1 handoff (Phase 17): Finish Phase 9 (continuation 7) – playability by humans, commands-only agent, clicking through the client, performance, approximations of the new modules

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md`, `README.md`, `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, `docs/IMPLEMENTATION-PLAN.md` (§9 "Phase 9" together with "Known limits"), `docs/SCRIPT-ENGINE.md` (§5, §8, §9), `docs/SIMULATION-CORE.md` (§29, §30), `docs/CAMPAIGN-AND-SAVEGAMES.md` and `docs/GAME-HUD.md` §14/§15.
Prerequisite: Phase 15 (world rendering, security) and Phase 16 (controls, sound, mission in the client) are completed; without them "playability" cannot be judged (`docs/DEMO-FINDINGS.md`). This brief continues `IMPLEMENTATION-PLAN.md` §9. §9 is **not yet completed**: the acceptance demands "every campaign mission passes its essential success and failure paths". What is verified is the script chain (objective driver, reachability, determinism, save/load), **not** that a human can win with the implemented units.

## Re-measured on the evening of 4 October 2026 (replaces the numbers below where they differ)

- **Versions and reference values:** `WorldSnapshot` 9, replay 10, `CommandTypes.Version` 13, `MissionSnapshot` 2; retail reference match winner FactionGLA at tick 13,500, final hash `0946FB676DEC615C`, chain
  `EA6D12F180930D71`; stress test `56882F763EABEF67` / `B8E663493F2C73D5`; clean solution build **11** warnings; compatibility baseline 751; 183/205 modules, 2,115/2,115 objects.
- **Campaign (each map individually, 24 maps including `Training01`):** `drive` (60,000 ticks) victory 24/24, defeat 24/24; `audit --ticks 900` 24/24 deterministic and identical after save/load,
  0 hits on missing types, elimination end probes victory 13/24, defeat 15/24 (state `096290b`).
- **Commands-only agent** (`drive --commands-only --outcome victory --ticks 15000`, 2,400 s time limit each): still **1 map** (`MD_GLA04`); 22 not reached, `MD_USA01` aborted after 2,400 s. With
  `--ticks 60000` on `GC_ChemGeneral`, `GC_NukeGeneral`, `GC_TankGeneral`, `MD_CHI01`: likewise no victory. The time limit is therefore not the bottleneck (`docs/SCRIPT-ENGINE.md` §9).
- **Skirmish AI:** supply centre at the depot (`AIPlayer::buildBySupplies`) and attacking in groups raise the 16 AI runs from 5–7 to **12 decided**; three times ChinaTankGeneral against
  AmericaAirForceGeneral and Bitter Winter America/China remain open (`docs/SIMULATION-CORE.md` §32). `SimTool ai-match … --trace N` shows money, army, collectors and distance over time – measure with it first.
- **For the agent it follows from this:** it builds and attacks but fulfils no mission objectives. The next step in item 1 is not "more time" but to express, per map, the first unfulfilled condition of the
  victory chain (`CampaignTool drive --explain`) as a command.
- **Moved here from Phase 16:** idle target acquisition and attack-move of the units (`friend_checkForIdleMoodTarget`, `docs/SIM-TURRET-AIM.md` §6/§8a) – only with it does a marching unit not lose its
  target until the next thinking step of the commander.
## Honest status (starting point, state after Phase 14, 4 October 2026; Phase 16 is not yet included here)

**Changes by Phase 15 that shift the numbers below (re-measure all of them!):** `CommandTypes.Version` 12 and replay 9 (typed battle plan option; `WorldSnapshot` 8 and `MissionSnapshot` 2 unchanged); pathfinding around stationary units and an AI rally point free of buildings
shift the behaviour of the skirmish AI (16 AI-versus-AI runs: 8 instead of 10 decided, all deterministic); retail reference match still winner FactionGLA at tick 13,890, final hash `76F22CB0CA79BEEF`; stress test `56882F763EABEF67`. The playability numbers of this
handoff were only partly re-measured after the sim changes of Phase 15: the commands-only agent (`drive --commands-only`, 15,000 ticks, 6 runs in parallel, 1,500 s time limit each) still reaches victory via its own scripts on only **1 map** (`MD_GLA04`, tick 1,022); 22 further maps end without victory, `MD_USA01` was aborted after 1,500 s (no result). **Not re-measured:** objective driver 24/24, audit end probes 13/24 and 15/24 – collect anew before any statement. The rendering (Phase 15) is technically finished but accepted by no human
(checklist in `docs/CLIENT-OBJECTS.md`).

Phase 14 (`git log 5428d89..HEAD` or the merge commits "air modules", "crate, special and collide modules", "structures and world modules", "last nine blocker modules", "weapon slot model") did a large part of the handoff 14 brief:

- **Module coverage closed:** all 183 simulation modules implemented, **2,115 of 2,115** retail objects instantiable, module uses 16,558/19,553 (84.7 %; the rest are the 22 presentation modules), compatibility baseline 751 (`docs/SIMULATION-CORE.md` §30 with tables per module, source and approximation).
- **Weapon slot model:** primary/secondary/tertiary, clips and reloading per slot, locks, set selection (veteran/`PLAYER_UPGRADE`/`CRATEUPGRADE_*`/`CARBOMB`), best-weapon choice (reduced), `FireOCLAfterWeaponCooldownUpdate` takes effect for the first time (§30.6). Versions: `WorldSnapshot` 8, replay 8, `CommandTypes.Version` 11, `MissionSnapshot` 2.
- **Script side:** formations (`asTeam`), path flight of the particle beam, OCL deliveries (`DeliverPayload`, special powers at the map edge), death weapons, `NAMED_SET_HELD`, `PLAYER_DISABLE/ENABLE_FACTORIES`, `SET_TRAIN_HELD`, `NAMED_SET_EVAC_LEFT_OR_RIGHT`, booby traps, `BRIDGE_BROKEN/_REPAIRED`, script names that travel along; the VM executes **109/109 conditions** and 187 action types (129 passed through, 19 recorded, **9 missing, none used in a campaign map**: `SKIRMISH_BUILD_BUILDING`, `SKIRMISH_BUILD_BASE_DEFENSE_FRONT/_FLANK`, `SKIRMISH_BUILD_STRUCTURE_FRONT/_FLANK`, `SKIRMISH_ATTACK_NEAREST_GROUP_WITH_VALUE`, `SKIRMISH_PERFORM_COMMANDBUTTON_ON_MOST_VALUABLE_OBJECT`, `AI_PLAYER_BUILD_TYPE_NEAREST_TEAM`, `NAMED_FIRE_WEAPON_FOLLOWING_WAYPOINT_PATH`).
- **Objective driver:** victory 24/24 and defeat 24/24 on its own script path (60,000 ticks); `audit --ticks 900` 24/24 deterministic and identical after save/load, 0 hits on missing types, elimination end probes victory 13/24, defeat 15/24; `reachability` clean 24/24 and 24/24. Two regressions through the new opponent modules (`MD_GLA03` victory, `MD_GLA04` defeat) are fixed in the driver (`docs/SCRIPT-ENGINE.md` §9): presence conditions only once the script is switched live, repair of captured carriers/placed units, the planner avoids area placements in a tie (`Leaf.Risky`).
- **Numbers (re-measure):** retail reference match winner FactionGLA at tick 13,890, final hash `76F22CB0CA79BEEF`, chain hash `35F54CAD40A43E6E`; stress test `56882F763EABEF67` (chain `B8E663493F2C73D5`); clean solution build 21 explained warnings; all test programs green.

**Deliberately not done (each with a reason):**

- *Playability by a human:* the commands-only agent (`CampaignTool drive --commands-only`, `SkirmishAi` as economy/army agent) writes nothing into the world and reaches victory via its own scripts only on a few maps (number and maps: `docs/SCRIPT-ENGINE.md` §9, section "Commands-only mode", state Phase 14). A human has never played the shell and a mission interactively; the smoke tests (`--smoke-test`, `--match`, `--mission`) run headless.
- *Nine missing script actions* (see above): the first six belong to `AISkirmishPlayer` (build list, base radius, `AIData`), which the simulation does not share because of the `SkirmishAi` replacement; used in no campaign map.
- *Approximations of the new modules* (all named in `docs/SIMULATION-CORE.md` §30): airfield/jet geometry without model bones and without physics; (nuclear missile launch at the silo: verified by a real-data test since Phase 15, no action needed); bridge terrain, bridge layers and scaffold construction are missing; ferry (`RailedTransport*`) without a command type (`EXECUTE_RAILED_TRANSPORT` would change the replay format) and without a retail map; `DemoTrapUpdate` without a weapon slot command; `PowerSabotage` without a consumer; weapon choice only `PREFER_MOST_DAMAGE`; collisions only for moving objects; (the jet `IsIdle` suspicion has been refuted by a real-data test since Phase 15).
- *Performance:* under parallel load `audit --ticks 900` took 2–10 minutes per map (total run over an hour, therefore each campaign individually); the stress test showed 150 µs/tick under load compared with 70–80 µs in quiet runs. A quiet before/after measurement (`SimTool stress`, `mission-run` on `MD_USA05`, `MD_GLA05`) with a profile is missing.

## Brief

1. **Playability instead of world interventions (core).** Re-measure: `CampaignTool drive --commands-only --outcome victory --ticks 15000`, each campaign individually (`--campaign NAME`, in parallel; the USA maps need over an hour), and update the table per map in `SCRIPT-ENGINE.md` §9. Then work off the most frequent causes of "not reached" per map (command agent, not driver world interventions): make `SkirmishAi` better for the viewer (attack threshold `--agent-threshold`, defence of the base, reinforcements), air and special powers via real commands, bridge crossings/ferries, mission objectives that need commands instead of teleport. Every step with a measurement on all maps; no value without a run.
2. **Click through the client (final pass; the rendering and control errors of the tech demo are the subject of Phases 15 and 16, `docs/DEMO-FINDINGS.md`).** `--shell` interactively (choose campaign, briefing, mission, save/load, completion), `--mission` and `--match` with mouse and hotkeys; fix errors found; connect sound and film, if desired. Results as an honest report (what worked, what did not), not just a smoke test.
3. **Performance.** Quiet measurement (no parallel runs): `SimTool stress`, `mission-run` on the largest maps, profile of the hottest modules (new: jet/parking place, weapon slot choice, collision distribution); goal: no loss compared with Phase 13 without a hash change.
4. **Work off approximations**, by benefit (silo launch, jet `IsIdle` and the typed battle plan field have been done since Phase 15): `PowerSabotage` consumer (underpowered state), bridge terrain only if a map needs it.
5. **Optional:** port the six `AISkirmishPlayer` actions (build list/base radius/`AIData`), if item 1 shows that the skirmish AI scripts (`SkirmishScripts.scb`) are needed.
6. After every group: update `ScriptCoverage` (test `ScriptSupportTableIsConsistent`), rewrite `SCRIPT-ENGINE.md` §5/§8/§9 and `SIMULATION-CORE.md`, `CampaignTool audit --ticks 900` (each campaign individually), `drive` (60,000 ticks, each campaign in parallel), `reachability`.

## Non-negotiable rules (unchanged, see `CLAUDE.md`)

- Unknown keys/modules/enums/references never fall back silently to defaults; every case is explicit, counted, documented and secured against `baselines/compatibility.json` (regenerate with `IniTool compat-baseline`, justify the change).
- Determinism: sorted collections, no wall-clock time, no `float`/`double`, no `Random.Shared`; `SourceRules` applies to `CnCNew.Simulation`. Every new state belongs in `Describe`/`Load` and is written only if it deviates from the default.
- `TreatWarningsAsErrors=true`; 21 explained warnings with a clean build (see `CLAUDE.md`), every other one is an error.
- Versions: incompatible changes increase `WorldSnapshot` (8), replay (8), `MissionSnapshot` (2), `CommandTypes.Version` (11).
- Third-party code only to be read, not copied; cite every ported action/condition/every module individually with file and function.
- Always re-measure inventory numbers. Local only, no remote, push nothing; several commits each closed in subject matter; working tree clean at the end. **Changed on 5 October 2026:** `docs/handoffs/HANDOFF-PHASE18.md` already exists (start of the programme tick rate/fixed point/multiplayer, `docs/MULTIPLAYER-PLAN.md`) and is **not rewritten** at the end of this phase, but updated in its "starting point" section with the reference values measured then. What remains open of this brief (in particular everything that needs a human) goes as a section "Rest of Phase 17" to the end of this document and as a reference into `docs/handoffs/HANDOFF-PHASE30.md` item 2.
- **Preserve line endings:** per file `git ls-files --eol <datei>`; CRLF in the index have `README.md`, `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, `docs/IMPLEMENTATION-PLAN.md`, everything else is LF (`Entity.cs` has mixed endings: in merges Git takes the whole file as a conflict – then take the version of `main` and apply the hunks by hand). For the CRLF files use PowerShell 7 (`pwsh`) with `[IO.File]::ReadAllText/WriteAllText` (UTF-8 without BOM); never start script files with umlauts with Windows PowerShell 5 (reads ANSI) – or use the Edit tool.
- **Background runs always with a bounded timeout** (`timeout` on the process, waiting times 3–10 minutes). Long runs per campaign in parallel (`--campaign NAME`); when rebuilding a tool during a run put the output into another directory (`dotnet build -c Release … -o <scratchpad>/ct2`), otherwise the file is locked. A Release `SimTool`/`CampaignTool` binary is **stale** after source changes until it has been rebuilt (`--no-build` otherwise shows old numbers).
- Working papers in the scratchpad of the session, nothing generated into the repo.

## Final report

Numbers with commit hashes, the table of the commands-only runs per map (before/after), results of clicking through the client, performance measurement (quiet), changed/newly secured approximations with source references, determinism/save-load results, updated docs (README, ARCHITECTURE, ROADMAP, IMPLEMENTATION-PLAN, SCRIPT-ENGINE, SIMULATION-CORE, CLAUDE.md) and the updated "starting point" in `docs/handoffs/HANDOFF-PHASE18.md`. The final report names explicitly what is *not* verified.

## Note on the plan change of 5 October 2026

The project owner has taken multiplayer via dedicated server, 60 Hz and a parameterised tick rate into the scope (`docs/MULTIPLAYER-PLAN.md`, Phases 18–29). For this brief it follows from this: (1) From Phase 18 on the programme compares "before/after" via hashes; every rule correction of this phase should therefore be merged *before* the start of Phase 18. (2) Item 3 (performance) is continued in Phase 18/19 with a profile of real matches; here the quiet before/after measurement is enough. (3) The former follow-up step (patch integration, quality, package) is now Phase 30.

## Rest of Phase 17 (as of 6 October 2026, after the closing session)

Phase 17 has, with limited scope, **ended, its acceptance goal is not reached**: the playability of the missions by a human is still not verified. This section replaces the numbers further up where
they differ. From `docs/handoffs/HANDOFF-PHASE18.md` on nobody changes the behaviour of the simulation any more until the programme from `docs/MULTIPLAYER-PLAN.md` releases it; everything below that changes the simulation therefore lies
behind the programme (`docs/handoffs/HANDOFF-PHASE30.md` item 2).

### Measured state (`6eb7ae4`, one machine, Release)

- Clean build 0 warnings; all test programs green (`Game.Tests` Release); `compat-check` 751; 183/205 modules, 2,115/2,115 objects; three scene audits green.
- `CommandTypes.Version` 14, replay 11, `WorldSnapshot` 9, `MissionSnapshot` 2. Reference match winner FactionGLA at tick 14,490, `23B4B25DF62765D8`, chain `DE5849AF9F8493C4`; stress test `56882F763EABEF67` / `B8E663493F2C73D5`.
- 16 AI runs: **12 decided** (before 11), all deterministic; table with final hashes and chains: `docs/SIMULATION-CORE.md` §36.2.
- `drive` (60,000 ticks) victory 24/24, defeat 24/24; `audit --ticks 900` 24/24 deterministic and identical after save/load, 0 hits on missing types, end probes 13/24 and 15/24; `reachability` clean 24/24 and 24/24.
- Commands-only agent (0 world writes, all 24 maps measured): 15,000 ticks **1 of 24** (`MD_GLA04`), 60,000 ticks **3 of 24** (`MD_GLA04`, `MD_CHI01` @18,492, `MD_CHI02` @29,605); `MD_USA01` and `MD_USA04` aborted at 60,000 ticks after 3,600 s.

### What the closing session decided

- **AI economy:** second supply centre at another, safe source as soon as the stock runs low (§36.1). Done.
- **Army composition:** by value and range – two measurement rounds, worse than chance, reverted (§36.3). **Open**, with a finding.
- **AI difficulty:** no verified effect applies in skirmish (`Player::friend_applyDifficultyBonusesForObject` only in `GAME_SINGLE_PLAYER`); the setup offers one computer army, `SkirmishDifficultyIgnored` is dropped (§35). Done by merging.

### Open (changes the simulation: behind the programme, `HANDOFF-PHASE30.md` item 2)

1. **An agent that wins skirmishes.** Combat value per target instead of price, teams instead of individual purchases (`AIPlayer::selectTeamToBuild`, team lists), after that the mission sequences per map (escort `MD_GLA01`, defence waves `MD_CHI04`, transport across water `MD_USA04`, units handed over late `MD_USA03`/`Training01`, capture without a capture unit `MD_GLA02`). Table per map: `docs/SCRIPT-ENGINE.md` §9 "Phase 17".
2. **The four remaining stalemates** of the AI runs (three times ChinaTankGeneral against AmericaAirForceGeneral, Armored Fury China/GLA): the same cause as item 1.
3. **Solo bonuses of the campaign:** `SOLO_AI_EASY/HARD` (rate of fire 80 %/120 %) and `HumanSoloPlayerHealthBonus_Easy/Hard` (150 %/80 %) are applied by the original in missions on easy and hard, not by this simulation (`MissionOptions.Difficulty` only switches script flags). Missions on "normal" are untouched.
4. **Approximations of target acquisition** (`docs/SIMULATION-CORE.md` §33.5), `PowerSabotage` consumer, reasons for rejecting a building site.
5. **Performance:** profile of the mission runs (`mission-run` `MD_GLA05`: 147.9 s tool run time for 3,000 ticks) – a profile without a behaviour change is part of Phase 18.

### Open (needs a human, possible at any time)

Nothing from Phases 15, 16 and 17 has been seen, heard or played: world rendering, sound (`waveOut`), mouse cursor, waypoints, texts, shell transitions, mission in the client, message list, letterbox, tooltips, units
firing by themselves, attack-move, the skirmish setup with one computer army. The demos (`scripts/demo-*.ps1`) are waiting for feedback; findings go into `docs/DEMO-FINDINGS.md` (number, cause, status).
Clicking through shell → campaign → mission → save/load → completion and skirmish setup → match → result: report per screen "worked / did not work / worked with defects".

### Tools and pitfalls

- `CampaignTool drive … --commands-only --outcome victory --explain --timeline N`, `--watch TEXT`, `--watch-flag`; each map individually with `--map "/NAME.map"`.
- `SimTool ai-match … --trace N --trace-army` shows money, army by type and the self-report of the AI.
- A comma in a `-File` argument of `pwsh` does not become an array; `git commit -F -` with a here-string does not work in PowerShell (message into a file); typographic quotation marks („ “) end a double-quoted string in PowerShell – German texts in single quotation marks or in files.
- Start tools for long runs from a copy of the `bin` directory; a test from an `-o` directory does not find the repo root (`SourceRules`).
- Several sessions commit to `main` at the same time: before every commit `git log -3`, before writing a new file check whether it exists in the meantime.
