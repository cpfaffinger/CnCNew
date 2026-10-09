# N+1 handoff: Phase 7 – world presentation and open simulation debt

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md`, `README.md`,
`docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, `docs/IMPLEMENTATION-PLAN.md` (§6 "Status" and §7), `docs/SIMULATION-CORE.md` (especially
§13–§20), `docs/GAME-RULES.md` (§10 and §11), `docs/W3D-MATERIALS.md` and `docs/CONTENT-PIPELINE.md`. `docs/handoffs/HANDOFF-PHASE5.md` to
`docs/handoffs/HANDOFF-PHASE7.md` are kept as history; their numbers are outdated.

The numbering: this brief covers step 7 of the overall plan (`IMPLEMENTATION-PLAN.md` §7, "Complete audiovisual
world presentation"). In addition it carries the simulation debt from Phase 6 (Part B) that has priority for a readable presentation.

## Honest status of Phase 6 (starting point)

Phase 6 is completed (commits `c62332a`, `d1a0bdd`, `964150a`, `894a270`, `18c5b46`, `582fafb`), but with deliberate limits
(complete: `IMPLEMENTATION-PLAN.md` §6, `SIMULATION-CORE.md` §20):

- **Acceptance met with restrictions:** 150 of 150 retail maps decode and build terrain; navigation is deterministic but point-shaped
  without collision (6 of 462 start pairs only partly walkable); 16 AI-versus-AI runs are deterministic, 15 end with a winner, 1 in a stalemate
  (Alpine Assault, America against China). **America never wins**, because `ChinookAIUpdate`/`TransportAIUpdate` are missing and therefore no supply economy arises.
- **Not implemented:** bridges, formations, local avoidance/collision, map scripts (`SidesList`/`PlayerScriptsList` are skipped),
  height line of sight, detectors, aircraft in the AI, check of build prerequisites in `QueueProduction`, `MaxSimultaneousOfType`, capture
  of power plants, toggle command for `Overcharge`, effect of `CommandSet`/radar, veteran-based `ArmorSet`/`WeaponSet` selection.
- **Block 0.4 open:** the RNG sequence was never compared against a compiled original; do not guess.
- The skirmish AI is a new development of our own (`SkirmishAi`), not a port of the original scripts; opponent start positions are public knowledge.

## Reproduction basis (as of the end of Phase 6, 30 September 2026; re-measure before continuing to work)

- Full inventory with all map overlays: 228 INI files loaded (28 excluded), 65,875 blocks, 12,574 definitions, 16,380 modules,
  333,061 keys, 18,138/19,688 references resolved (1,550 visible fallbacks), snapshot 144,832,736 bytes, SHA-256
  `AEB5108DDA27ABAC7445452AA2743B794AD0BF1102F2687F8D7D8F7B87D2ADB7`.
- Retail base data (`--maps exclude`): 12,550 definitions, 2,115 objects, snapshot 144,221,861 bytes, SHA-256
  `1248585D0BAAB79F5202FDB81F26A2B00962A9D393B0CE1FF93C5FFDF6A5C6E7`.
- Compatibility baseline 741 entries; module catalogue 205 types, 77 implemented; 1,393 of 2,115 objects instantiable (65.9 %),
  722 blocked; module uses covered 15,382 of 19,553 (78.7 %); 77 modules of the category "Phase 5", 29 "Phase 6", 22 "Phase 7" open.
- Command table 25 types (`CommandTypes.Version = 8`), snapshot version 6, replay format v6, rejection codes up to 34.
- Retail reference match: 13 factions, winner player 12 at tick 26,880, final hash `5C370ED383EC9F6B`, run 2 chain hash `EB8BDC1482BC20DF`.
- Stress test (216,000 ticks, 13.0 s): final hash `D1521974C861A4B9`, chain hash `C298F5F74993D6CD`, 914,394 commands, snapshot at tick 108,000
  11,862,400 bytes.
- `MapTool audit`: 150/150 maps, 30,391,581 height points, 281,480 objects, 16,475,736 path cells, ground paths 462 pairs (456 complete, 6 partial, 0 blocked).
- Asset runtime content 25,682 files; `IniTool audit` 256/256 INI files, 66,625 blocks.

## Part A – world presentation (plan §7)

The presentation reads exclusively immutable simulation snapshots and writes only typed input commands; Stride types stay in
`CnCNew.Client.Stride`. Before building it must be clarified which views the simulation delivers today (`IWorldServices`, `EntitiesVisibleTo`,
`VisionMap`, `TerrainMap`) and whether a read-only snapshot reader (`SimulationSnapshot`/render projection) must be added.

1. **Terrain rendering:** height field, blend tiles/texture layers (`MapData`, `TextureClasses`), water from the polygon triggers (`TerrainMap.Water`),
   cliffs; map preview from one of the 150 maps (define and document the vertical slice map).
2. **Objects:** W3D hierarchies, skeletons, animations, condition states and turrets (foundations in `CnCNew.W3D`, `docs/W3D-MATERIALS.md`);
   `ModelConditionState` changes from snapshot states (movement, attack, damage, build/sell progress, `UNDER_CONSTRUCTION`).
3. **Camera, selection, command feedback:** selection rectangle, move/attack/build commands as typed commands (`ConstructCommand` with
   building site preview via the building site check), rally points; vision/fog from `VisionMap` (visible/explored).
4. **FX:** particles, tracers, projectiles, decals, lighting and shadows; projectiles first need the flight time simulation (Part B).
5. **Audio and video:** free runtime path, no Miles/Bink SDK dependency; derive events from snapshots.
6. **Profiling:** streaming, resource lifetime, memory and golden image budgets; extend `--smoke-test` of the Stride client.

**Acceptance of §7:** a complete headless match (`SimTool ai-match`, replay) can be visualised and controlled live, all visible
object states change correctly, defined performance, memory and golden image budgets are met on the target hardware.
If limits remain, name them honestly in `IMPLEMENTATION-PLAN.md`.

## Part B – simulation debt that blocks or distorts the presentation

Before every step measure with `SimTool audit-coverage .local/phase2-import-a` what a module unlocks on its own. Most frequent blockers (objects
in total / blocked only by this module): `CommandButtonHuntUpdate` 79/18, `ToppleUpdate` 76/66, `SpecialAbilityUpdate` 64/3,
`JetSlowDeathBehavior` 59/0, `MissileAIUpdate` 44/29, `CountermeasuresBehavior` 40/0, `JetAIUpdate` 34/2, `HeightDieUpdate` 31/15,
`DeliverPayloadAIUpdate` 28/1, `MobMemberSlavedUpdate` 27/27, `RailroadBehavior` 25/25, `TransportAIUpdate` 24/13, `BaikonurLaunchPower` 23/23.

7. **`ChinookAIUpdate`/`TransportAIUpdate`** (America supply), `TunnelContain`, `HelixContain`; after that repeat the 16-run evaluation
   (`SIMULATION-CORE.md` §19) and check whether America wins and the stalemate disappears.
8. **Projectile flight time:** `DumbProjectileBehavior`, `MissileAIUpdate` (44/29); damage then takes effect on impact.
9. **Collision:** `ToppleUpdate` (76/66), real `CrushDie`/`SquishCollide`, `HeightDieUpdate`; connect a collision or avoidance model to
   `NavigationGrid` (formations, local avoidance are unfinished plan scope from §6).
10. **Build and rule limits:** prerequisite and ability check in `QueueProduction`, `MaxSimultaneousOfType`, repair cost,
    power plant capture, overcharge command, `CommandSet`/radar effect, veteran-based `ArmorSet`/`WeaponSet`, `CommandButtonHuntUpdate`,
    `SpecialAbilityUpdate`, detectors and height line of sight.
11. **Map scripts** (`SidesList`/`PlayerScriptsList`) are the way to missions and campaign (plan §9); reading them is a prerequisite, not part of §7.

## Non-negotiable rules (unchanged, see `CLAUDE.md`)

- Unknown keys/modules/enums/references never fall back silently to defaults; every case is explicit, counted,
  documented and secured against `baselines/compatibility.json`.
- Determinism: canonical sorting, no wall-clock time, no dependency on `Dictionary`/`HashSet` order; two independent
  runs byte-identical. `CnCNew.Simulation` depends only on `CnCNew.Data` (`SourceRules` test); every `IUpdateModule` stores `nextWake`.
- `TreatWarningsAsErrors=true`; only the ten known NU1901/NU1903 warnings are explained.
- Always re-measure inventory numbers, never adopt them from docs or this handoff.
- Third-party code may be read, not copied unexamined; every ported algorithm needs a source reference (file, function).
- New commands increase `CommandTypes.Version` (currently 8) and get codes of their own; existing ones never change. Changes to the
  snapshot or replay format increase its version (currently snapshot 6, replay 6); older ones are rejected with a clear message.
- Work only locally, no remote, push nothing; several commits each closed in subject matter; working tree clean at the end;
  at the end of this pass write and commit `docs/handoffs/HANDOFF-PHASE9.md` (or a new continuation brief).
- In practice: put large edit scripts into a `.js` file in the scratchpad with the Write tool instead of complex heredocs; a running
  SimTool process locks the Release DLL.

## Final report

Numbers (with commit hashes), implemented functions/modules, determinism, test, stress and replay results, performance and
golden image measurements, module coverage and instantiation rate before/after with blocker list, known limits and technical
debt, `git status` and the confirmation "no remote, nothing pushed".
