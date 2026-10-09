# N+1 handoff: Phase 9 – skirmish, missions, campaigns and savegames

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md`, `README.md`,
`docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, `docs/IMPLEMENTATION-PLAN.md` (§7 "Status", §8 "Status" and §9), `docs/SIMULATION-CORE.md`
(§12, §12a, §19–§21), `docs/GAME-RULES.md` §10/§11 as well as the specialist documents of the presentation and input layer:
`docs/PRESENTATION-INPUT.md`, `docs/GAME-SHELL.md`, `docs/GAME-HUD.md`, `docs/GAME-AUDIO.md`, `docs/CLIENT-TERRAIN.md`,
`docs/CLIENT-OBJECTS.md`, `docs/CLIENT-FX.md`, `docs/CLIENT-UI-RENDERING.md`. `docs/handoffs/HANDOFF-PHASE5.md` to `docs/handoffs/HANDOFF-PHASE8.md`
are kept as history; their numbers are outdated.

The numbering: this brief covers step 9 of the overall plan (`IMPLEMENTATION-PLAN.md` §9, "Skirmish, missions, campaigns and
savegames"). In addition it carries the simulation debt items 9–11 postponed from Phase 8 from `docs/handoffs/HANDOFF-PHASE8.md` (collision,
build/rule limits, map scripts) as well as a documented, unfixed determinism error.

## Honest status of Phase 7/8 (starting point)

Phase 7 and Phase 8 are completed (merge commits among others `4da086f`, `b90f861`, `05b5c0c`, `cf117b0`, `0344582`, `86ce4fd`, as well as
`523525c`/`ae642e7`/`a948efb`; complete list with `git log --oneline 034c5e6..472c65d`), but with deliberate limits (complete:
`IMPLEMENTATION-PLAN.md` §7/§8, in each case the section "Known limits"):

- **Acceptance met with restrictions:** a complete skirmish match runs, headless and visualisable, from the main menu to the
  result screen and back (`CnCNew.Game/Shell` + `CnCNew.Game/Hud` + `CnCNew.Client.Stride`); all four world layers (terrain, objects,
  FX, UI) and the audio path yield byte-identical SHA-256/statistics over repeated runs. Multiplayer, load/playback and
  campaign entry are visible in the menu but disabled (outside the product scope so far) – **that changes now with the
  campaign as part of Phase 9.**
- **Not implemented/known to be incomplete:** turrets render in the rest pose (no turret angles read live), no contact shadows, some
  thin two-sided W3D meshes shade black on the back side (Stride pipeline limit), no Bink video decoder (no SDK licence),
  no formation/group offset logic, no command "fire at position", hijacking/car bomb/sabotage/salvaging/capturing
  buildings/hacking remain untranslated, display mode/language/aspect ratio/accessibility text scaling are present in `GameSettings`
  but not reachable from the shipped `OptionsMenu.wnd`. Full list: `IMPLEMENTATION-PLAN.md` §7/§8.
- **Known, unfixed determinism error (important for savegames!):** `SpecialPowerBase.SharedReadyFrame()`
  (`src/CnCNew.Simulation/Modules/BuiltIn/SpecialPowerModules.cs:133`) creates the ready tick of a shared special power while reading
  (side effect of `IsReady()`) instead of setting it once at `OnSpawn`/`OnSpecialPowerCreation`. The skirmish AI queries `IsReady()`
  live in every tick, a pure `ReplayRunner` run never – as a result `SkirmishMatchSession.Step()` (live) can yield a different
  `World.StateHash` than `ReplayRunner.Run` over its own `Session.ToReplay()` recording, for `SharedSynced` special powers without
  an explicit grant command. Complete diagnosis in `docs/SIMULATION-CORE.md` §12/§12a. **This does not affect the previous
  reference numbers** (they are live-against-live comparisons, see there), **but every future savegame/replay format that compares a
  game run live against a restored one must either fix this error beforehand or explicitly steer around it** (e.g.
  by savegames treating only `WorldSnapshot`, never `ReplayFile`, as the truth – `WorldSnapshot.Restore` already does that correctly, see
  §7 below). A fix changes the AI timing and with it every reference match/stress/AI run hash; it belongs in a separate, measured
  simulation phase, not in passing into this one.
- The skirmish AI is a new development of our own (`SkirmishAi`), not a port of the original scripts; map scripts (`SidesList`/
  `PlayerScriptsList`) are decoded by `SageMapDecoder` but not executed by the simulation core (see Part B, item 11).

## Reproduction basis (as of the end of Phase 8, 30 September 2026; re-measure before continuing to work)

- Module catalogue: 205 types, 85 implemented (`SimTool audit-modules .local/phase2-import-a`); instantiability 1,470 of 2,115 objects
  (69.5 %, `SimTool audit-coverage`); module uses covered 15,543 of 19,553 (79.5 %); compatibility baseline 749 entries
  (`IniTool compat-check .local/phase2-import-a baselines/compatibility.json`).
- Largest remaining blockers (objects in total / blocked only by this module, `SimTool audit-coverage`):
  `CommandButtonHuntUpdate` 79/18, `ToppleUpdate` 76/66, `SpecialAbilityUpdate` 64/3, `JetSlowDeathBehavior` 59/0 (Phase 7 category,
  pure presentation), `HeightDieUpdate` 31/24, `StructureToppleUpdate` 6/4, `CountermeasuresBehavior` 40/0.
- Command table: `CommandTypes.Version = 9`; snapshot version 7, replay format v7.
- Retail reference match (`SimTool reference-match`): 13 factions, winner FactionAmerica at tick 13,800, final hash `4DCB46FE3D2E8214`.
- Stress test (216,000 ticks, `SimTool stress`): unchanged since Phase 6 (modules of Phases 5–6); re-measure with the new transport/
  projectile modules if the fixture touches them.
- `MapTool audit`: unchanged 150/150 maps since Phase 6; **World Builder scripts are now decoded as well** (see below and
  `docs/MAP-SCRIPTS.md`) – before, 75 of the 150 maps failed at this point (25 campaign maps with an older
  condition chunk format, 50 on a wrong assumption about `NameKeyType`), now 0.
- **New: World Builder scripts decoded** (`docs/MAP-SCRIPTS.md`, commits `73b8ae8`/`ef6191e`/`bef2cd8`): 1,631 script lists, 23,906 scripts,
  2,294 groups, 33,890 conditions (82 of 109 types used), 82,563 actions (291 of 344 types used) over all 150
  retail maps – complete usage table sorted by frequency in `MapTool audit`. **Only the decoding is finished;
  not a line of it is executed** – that is Part A, item 2 below.
- World presentation: terrain 0.266 ms/frame CPU mean, 33–77 MiB GPU per map (`docs/CLIENT-TERRAIN.md`); objects 0.19–0.60 ms/frame,
  0 missing models/textures/animations over the checked skirmishes (`docs/CLIENT-OBJECTS.md`); FX bench with 10,000 active particles
  1.735 ms per build (`docs/CLIENT-FX.md`).
- Input/UI: `CommandMap.ini` 96 entries/0 problems; 820 command buttons/472 command sets/0 problems; 8 shell screens with
  54 golden hashes over 6 resolutions; UI renderer verification over 32 PNGs byte-identical (`docs/PRESENTATION-INPUT.md`,
  `docs/GAME-SHELL.md`, `docs/CLIENT-UI-RENDERING.md`).
- Build: `dotnet build CnCNew.slnx` with a **clean** `bin`/`obj` of `CnCNew.Client.Stride` (otherwise misleadingly low, since the
  Stride asset compiler step is skipped incrementally): 21 warnings (10 known `NU1901`/`NU1903` + 11 explained
  Stride console notes, see `CLAUDE.md`), 0 errors.

## Part A – skirmish, missions, campaigns, savegames (plan §9)

`CnCNew.Game/Shell` already knows skirmish completely (`MatchSetup`, `LiveMatch`, result screen); the gap is campaign/missions
and savegames. **The decoding of the World Builder scripts has been finished since this pass**
(`MapData.PlayerScripts`, `docs/MAP-SCRIPTS.md`, `MapScriptCatalog` for the type identity) – read this document before you begin item 2,
it saves the renewed reverse-engineering work on the binary format and explains in particular why `NameKeyType` is discarded
and the raw enum index is used instead.

1. **Finish skirmish:** start positions/teams/rules/AI profiles from `docs/handoffs/HANDOFF-PHASE8.md` Part B item 10 (build/rule limits) and
   item 9 (collision) are prerequisites for a fair, complete skirmish – see Part B below before you report them as "done".
   Complete the statistics (`MatchResult` already exists in `CnCNew.Game/Hud`, see `docs/GAME-HUD.md`) on the result screen.
2. **Script engine (execution), triggers, counters, camera sequences, briefings, cutscenes:** the raw data is already in
   `MapData.PlayerScripts` (`MapScript`/`MapScriptGroup`/`MapOrCondition`/`MapCondition`/`MapScriptAction`/`MapScriptParameter`,
   `docs/MAP-SCRIPTS.md`). What remains to be done is the **execution**: a deterministic script VM without wall-clock time/ambient random numbers in
   `CnCNew.Simulation` (the test `SourceRules` applies here too) that evaluates per tick the active scripts of a script list
   (OR-of-AND condition tree, observe `IsOneShot`/`DelayEvaluationSeconds`/difficulty level flags) and on success/failure executes the
   true/false action list. `docs/MAP-SCRIPTS.md` §5 names the frequency distribution of all 82 used condition and
   291 action types as the basis for prioritisation (the most frequent are `CONDITION_TRUE`, `TIMER_EXPIRED`, `FLAG`, `COUNTER` and
   `ENABLE_SCRIPT`/`SET_FLAG`/`SET_COUNTER`/`SET_(MILLISECOND_)TIMER`/`CALL_SUBROUTINE`); types not implemented need a
   `MissingConditions`/`MissingActions` count following the same pattern as `MissingModules` for objects – never a silent
   failure. Cutscenes/camera sequences/briefings are a matter of presentation (`CnCNew.Client.Stride`/`CnCNew.Game`), not of
   simulation.
3. **Mission objectives, defeat/victory paths, campaign order, unlocks:** a typed campaign/mission model of its own in
   `CnCNew.Game` (not `CnCNew.Data`, which stays renderer/engine/archive/filesystem-independent – check whether a new data model
   belongs in `CnCNew.Data` at all or rather in `CnCNew.Game`, because it links progress/unlocking with the local user profile).
4. **Savegame format:** build on `WorldSnapshot` (version 7, see above), **not** on `ReplayFile` – the determinism error above
   affects only replays, `WorldSnapshot.Restore` is the already correct, checked source of truth (see
   `Tests.State.cs`/`Tests.ReferenceMatch.cs`: "A snapshot taken in the middle of the match continues hash-identically"). A
   savegame is at least `WorldSnapshot` plus campaign progress plus map reference; version it like snapshot/replay (own
   version number, clear rejection message on incompatibility).
5. **Automated load/headless check of every map/mission:** extend the `MapTool audit` pattern to missions; every campaign mission
   must run through its essential success and failure paths in a headless test run (no manual playthrough as the only evidence).

**Acceptance of §9 (from the plan, verbatim):** All skirmish maps can be started and ended, every campaign mission passes its
essential success and failure paths, save/load continues an identical simulation state. If limits remain, name them
honestly in `IMPLEMENTATION-PLAN.md` §9.

## Part B – simulation debt postponed from Phase 8

Before every step measure with `SimTool audit-coverage .local/phase2-import-a` what a module unlocks on its own (numbers above, as of the end of
Phase 8 – re-measure).

9. **Collision:** `ToppleUpdate` (76 objects/66 blocked only by it), `StructureToppleUpdate` (6/4), real `CrushDie`/`SquishCollide`
   (implemented today, but without real collision geometry – check what is missing), `HeightDieUpdate` (31/24); connect a collision or
   avoidance model to `NavigationGrid`. That is a prerequisite for a fair skirmish and for mission scripts that react to
   unit collisions.
10. **Build and rule limits:** prerequisite and ability check in `QueueProduction`, `MaxSimultaneousOfType`, repair cost,
    power plant capture, overcharge command, `CommandSet`/radar effect, veteran-based `ArmorSet`/`WeaponSet`,
    `CommandButtonHuntUpdate` (79/18), `SpecialAbilityUpdate` (64/3), detectors, height line of sight. `docs/PRESENTATION-INPUT.md` §10 and
    `docs/GAME-HUD.md` list concrete missing read/write APIs that the input layer already misses (build cost dry run,
    read access to production queues, `SpecialPowerModuleBase.AvailableOnFrame`, `UpgradeMuxModule.Executed`, a public
    dock marker) – deal with them directly when closing these gaps, they are small, clearly specified additions.
11. **Map scripts** (`SidesList`/`PlayerScriptsList`): the decoding is done (`MapData.PlayerScripts`,
    `docs/MAP-SCRIPTS.md`); what remains open is the execution – see Part A, item 2, that is part of the main brief of this phase, not
    optional.
12. **(New, from Phase 7/8) fix the `SharedReadyFrame` determinism error or deliberately steer around it** (see above, "Honest status"). —
    **Done** (30 September 2026, outside the actual Phase 9 main brief, added afterwards as a standalone fix): the
    ready tick is now set deterministically at `OnSpawn` (objects that already bring the power along) or `OnSpecialPowerCreation`
    (granted powers), `IsReady()` only reads it. Live-against-replay hash comparison test added
    (`Tests.Skirmish.cs` → `SkirmishLiveSessionMatchesItsOwnReplay`, demonstrably fails on the unfixed code). Retail
    reference match and stress test hashes unchanged (they do not touch the place); a skirmish AI run that touches it changes as
    expected. Full diagnosis, measurement and commit references in `docs/SIMULATION-CORE.md` §12/§12a and `CLAUDE.md`. **Not** done in this
    pass: re-measuring the 16-map AI overview in `docs/SIMULATION-CORE.md` §19 (a balance survey of its own, not
    part of the determinism correction) – whoever needs its numbers for another decision must run it again. The
    remaining main brief of this phase (Part A: finishing skirmish, script engine, campaign, savegames; Part B, items 9–11) is untouched by this
    fix and is still outstanding.

## Non-negotiable rules (unchanged, see `CLAUDE.md`)

- Unknown keys/modules/enums/references never fall back silently to defaults; every case is explicit, counted,
  documented and secured against `baselines/compatibility.json`.
- Determinism: canonical sorting, no wall-clock time, no dependency on `Dictionary`/`HashSet` order; two independent
  runs byte-identical. `CnCNew.Simulation` depends only on `CnCNew.Data` (`SourceRules` test); every `IUpdateModule` stores `nextWake`.
  A new script VM (Part A, item 2) is subject to the same rules – no exception for "only campaign logic".
- `TreatWarningsAsErrors=true`; the 21 explained warnings (as of Phase 8, see `CLAUDE.md`) are the reference value for a
  **clean** build; every other new warning is an error until it is explained.
- Always re-measure inventory numbers, never adopt them from docs or this handoff.
- Third-party code may be read, not copied unexamined; every ported algorithm needs a source reference (file, function).
  That applies especially to item 2 (script VM): `references/.../GameLogic/ScriptEngine/ScriptActions.cpp` (7,568 lines) and
  `ScriptConditions.cpp` (2,992 lines) are the only reliable reference for the semantics of every action/condition that the VM
  implements – cite each one individually, not wholesale.
- New commands increase `CommandTypes.Version` (currently 9) and get codes of their own; existing ones never change. Changes to the
  snapshot or replay format increase its version (currently snapshot 7, replay 7); older ones are rejected with a clear message. A
  new savegame format gets a version number of its own following the same pattern.
- Work only locally, no remote, push nothing; several commits each closed in subject matter; working tree clean at the end;
  at the end of this pass write and commit `docs/handoffs/HANDOFF-PHASE10.md` (or a new continuation brief).
- In practice: put large edit scripts into a file in the scratchpad with the Write tool instead of complex heredocs; a running
  SimTool process locks the Release DLL. `tools/CnCNew.UiRenderTool` is intentionally not in `CnCNew.slnx` (two Stride `Game` exe
  projects in one solution trigger additional but harmless Stride asset compiler console notes, see `CLAUDE.md`) – proceed the same way with new
  standalone Stride tools, or deliberately accept the consequence (higher explained warning count) and document it.

## Final report

Numbers (with commit hashes), implemented functions/modules, determinism, test, stress and replay results (including: was the
`SharedReadyFrame` error fixed or deliberately steered around?), performance and golden image measurements, module coverage and
instantiation rate before/after with blocker list, known limits and technical debt, `git status` and the confirmation
"no remote, nothing pushed".
