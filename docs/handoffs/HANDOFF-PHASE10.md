> **Stage:** 1 – Port · **Milestone:** S1-M7 (+ S1-M4 build and rule limits, S1-M5 collision) · **Phase:** 10
> **Status:** done (1 October 2026)
> **Predecessor:** HANDOFF-PHASE9.md · **Successor:** HANDOFF-PHASE11.md · **Results in:** docs/SCRIPT-ENGINE.md, docs/SIMULATION-CORE.md §23–§25, docs/CAMPAIGN-AND-SAVEGAMES.md §7
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

# N+1 handoff: Finish Phase 9 – skirmish, shell connection, playability of the missions

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md`, `README.md`, `docs/ARCHITECTURE.md`,
`docs/ROADMAP.md`, `docs/IMPLEMENTATION-PLAN.md` (§9 "Status" and "Known limits"), **`docs/SCRIPT-ENGINE.md`**, **`docs/CAMPAIGN-AND-SAVEGAMES.md`**,
`docs/MAP-SCRIPTS.md`, `docs/SIMULATION-CORE.md` (§12, §19–§22), `docs/GAME-RULES.md` §10/§11, `docs/GAME-SHELL.md`, `docs/GAME-HUD.md`,
`docs/PRESENTATION-INPUT.md`. `docs/handoffs/HANDOFF-PHASE9.md` remains as history; its parts A2–A5 and B11/B12 are done, its numbers outdated.

This brief continues Phase 9 (`IMPLEMENTATION-PLAN.md` §9). **Phase 9 is not yet completed:** the acceptance demands "all skirmish maps can be started
and ended, every campaign mission passes its essential success and failure paths, save/load continues an identical simulation state".
The third part is verified for missions, the other two are not.

## Honest status (starting point, as of 1 October 2026)

**Finished and measured** (details, source references and numbers in the two new documents):

- Deterministic **script VM** in `CnCNew.Simulation/Scripting` (conditions 63/109, actions 69 executed + 129 passed through of 344; the rest is counted at
  run time, never silently passed over), **mission session** (`MissionSession`) with sides/relationships/placed objects/AI build lists/start base,
  saveable state (`MissionSnapshot` v1 on `WorldSnapshot` v7).
- **Campaign logic** (`CampaignCatalog`, `CampaignProgress`, `CampaignController`, `MissionRun`) and **savegames** (`SaveGame` v1, `SaveGameStore`) in
  `CnCNew.Game`; **`CnCNew.CampaignTool`** (`list`, `audit`, `playthrough`) and `SimTool mission-run`.
- 24 unique campaign maps: 24/24 deterministic, 24/24 identical after save/restore; end probes by elimination victory 10/24,
  defeat 15/24; `playthrough --campaign CHALLENGE_0` plays five missions with save/load per mission, at `GC_LaserGeneral` (no human side
  on the map) the probe stops.
- `CommandButtonHuntUpdate` and `LockWeaponCreate` as honest shell modules; 1,494 of 2,115 objects instantiable; baseline 747.

**Not done (brief of this phase):**

1. **Part A item 1 – finish skirmish** (untouched): setup/start positions/teams/rules/AI profiles/statistics; "all skirmish maps can be started and ended"
   is not measured (`MapTool audit`: 75 of 150 maps have no start waypoints, 462 start pairs, 456 fully walkable). The 16-map AI overview in
   `SIMULATION-CORE.md` §19 has not been re-measured since the `SharedReadyFrame` fix. **A skirmish savegame is not possible** (the skirmish AI holds
   state outside the world); decide whether `SkirmishAi` becomes serialisable or skirmish explicitly stays without saving.
2. **Shell connection** (`CnCNew.Game/Shell`, Stride UI): "Campaign" and "Load game" in the main menu are disabled. To be built: campaign selection and difficulty,
   briefing (`CampaignMission.ObjectiveLabels/IntroMovie/BriefingVoice/UnitNames/LocationLabel`), load list (`SaveGameStore.List`), saving in the game, the
   mission completion (victory/defeat → `CampaignController.Conclude`), `ScriptOutput` playback (speech, film, text, camera, radar; without a Bink decoder
   films stay placeholders) and feedback to the VM (`CompletePresentation`, `SignalUiInteraction`, `SelectNamed`). Connection list: `CAMPAIGN-AND-SAVEGAMES.md` §7.
3. **Playability of the missions** – the core of the open acceptance. Order by run-time frequency (`CampaignTool audit`, table `SCRIPT-ENGINE.md` §9):
   - **Choose the local side**: maps without `playerIsHuman` (e.g. `GC_LaserGeneral`) need the side of the human from the host (`MissionOptions`/`CampaignProgress`);
     `LocalPlayerOf` falls back to player 1 today. Generals' Challenge: `Campaign.PlayerFaction`/general selection.
   - **Command-button abilities** (`NAMED_/TEAM_USE_COMMANDBUTTON_ABILITY*`, 89+27+20 hits) and **`SpecialAbilityUpdate`** (64 objects, 36 blocked only by it:
     Ranger abilities, Colonel Burton, Jarmen Kell, hackers …) – these are the most used campaign units; in addition extend `CommandButtonHuntUpdate`
     (`TEAM_HUNT_WITH_COMMAND_BUTTON`).
   - **Garrison actions** (`TEAM_/NAMED_GARRISON_*`, `*_EXIT_*`, `NAMED_ENTER_NAMED`), **`MAP_REVEAL_*`/`MAP_SHROUD_*`** (needs permanent reveal in
     `VisionMap`), **`BUILD_TEAM`/`TEAM_MERGE_INTO_TEAM`/`RECRUIT_TEAM`**, **`PLAYER_ENABLE/DISABLE_*_CONSTRUCTION`**, `WAREHOUSE_SET_VALUE`,
     `TEAM_SET_OVERRIDE_RELATION_*`, `OBJECT_ALLOW_BONUSES`, `PLAYER_AFFECT_RECEIVING_EXPERIENCE`, camera-bound conditions.
   - **Vision conditions** (`*_DISCOVERED`, `*_SIGHTED`, `ENEMY_SIGHTED`), `PLAYER_TRIGGERED_SPECIAL_POWER`/`*_COMPLETED_*` (events `SpecialPowerUsed`).
   - **`SKIRMISH_*` scripts of the original AI** (37,000+ hits per condition on the challenge maps): decide whether the skirmish AI of the challenge opponents executes these
     scripts (port of the conditions/actions of the `Skirmish` family) or the own `SkirmishAi` replaces them.
   - **Reinforcement with transport** (`teamTransport`, `teamStartsFull`, aircraft/parachute), real formations and attack movement, `*_FOLLOW_WAYPOINTS`
     with `asTeam`.
   - After that measure the **end probes** again and examine every UNMET mission individually with `--explain` (goal: every mission reaches victory and defeat on
     the path intended by the script, not only by elimination). Missing map overlay definitions (roads, fences, trees):
     check whether `MapOverlayMode` belongs on `Include` for missions (compilation per map).
4. **Part B item 9 – collision** (`ToppleUpdate` 76/66, `StructureToppleUpdate`, `HeightDieUpdate` 31/24, real `CrushDie`/`SquishCollide`) and
   **item 10 – build/rule limits** (prerequisite/ability check in `QueueProduction`, `MaxSimultaneousOfType`, repair cost, power plant capture,
   overcharge command, `CommandSet`/radar effect, veteran-based `ArmorSet`/`WeaponSet`, detectors, height line of sight; the small read/write APIs
   from `PRESENTATION-INPUT.md` §10/`GAME-HUD.md`). Both are prerequisites for a fair skirmish and for mission scripts that react to collisions.

## Reproduction basis (re-measure before continuing to work)

- Build `dotnet build CnCNew.slnx`: 21 explained warnings with a **clean** `bin`/`obj` of `CnCNew.Client.Stride` (10 `NU1901`/`NU1903` + 11 Stride notes), 0 errors;
  `CnCNew.CampaignTool` references `CnCNew.Game` (no Stride) and does not change the number.
- All test programs from `CLAUDE.md` pass, in addition `CnCNew.Game.Tests` with `CampaignTests` (Campaign.ini, progress, control flow, savegame, store).
- `IniTool compat-check`: 747 entries; `SimTool audit-coverage`: 1,494/2,115 (70.6 %), blockers see `SIMULATION-CORE.md` §11; module uses 15,639 of 19,553.
- Retail reference match `SimTool reference-match`: tick 13,665, `final hash` `D4779BE413835B6D`; stress test `ED6F9329F59662E3`; `MapTool audit` 150/150.
- `CampaignTool audit … --ticks 900` (Release, approx. 6 minutes): 24 maps; results see `SCRIPT-ENGINE.md` §9. If one of these numbers changes without a change of your own, that is
  a finding.

## Non-negotiable rules (unchanged, see `CLAUDE.md`)

- Unknown keys/modules/enums/references never fall back silently to defaults; every case is explicit, counted, documented and secured against
  `baselines/compatibility.json`. New script actions/conditions: enter in `ScriptCoverage` (the test `ScriptSupportTableIsConsistent` checks the
  names), cite the source function, test in `Tests.Scripting.cs`, line in `SCRIPT-ENGINE.md` §4/§5, deviations in §8. New modules: catalogue schema, binding, test,
  entry in `SIMULATION-CORE.md`, baseline (`IniTool compat-baseline`) – changes to it are subject to review.
- Determinism: sorted collections, no wall-clock time, no `float`/`double`, no `Random.Shared`; `SourceRules` applies to the VM. Everything that is script state belongs in
  `ScriptEngine.Describe/Load` (otherwise a restored run deviates – the continuation test catches that).
- Snapshot/replay/mission formats: changes increase the respective version (`WorldSnapshot` 7, replay 7, `MissionSnapshot` 1, `SaveGame` 1, `CommandTypes.Version` 9);
  older ones are rejected with a clear message.
- Third-party code only to be read, not copied; cite every ported action/condition individually with file and function.
- Always re-measure inventory numbers. Before/after numbers for every module/script change (instantiability, coverage, `CampaignTool audit`).
- Work only locally, no remote, push nothing; several commits each closed in subject matter; working tree clean at the end; at the end write `docs/handoffs/HANDOFF-PHASE11.md` and
  commit it.
- **Preserve line endings:** the repo is predominantly LF, individual files (`README.md`, `docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, `docs/IMPLEMENTATION-PLAN.md` among others) are CRLF;
  when editing by script use `newline=''` and check `git diff --stat` (an accidental conversion produces diffs over whole files).
- In practice: run large edit scripts as a file in the scratchpad instead of complex heredocs (Bash quoting breaks on apostrophes/quotes); a running audit process locks the
  Release DLLs; `tools/CnCNew.UiRenderTool` stays outside the solution.

## Final report

Numbers with commit hashes, implemented script actions/conditions and modules with source references, determinism, test, stress and continuation results, the
`CampaignTool audit` table before/after, instantiation rate and blocker list before/after, known limits, `git status` and the confirmation "no remote, nothing
pushed".
