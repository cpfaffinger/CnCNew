> **Stage:** 1 – Port · **Milestone:** S1-M5 World, terrain, movement, vision, construction · **Phase:** 7
> **Status:** done (30 September 2026)
> **Predecessor:** HANDOFF-PHASE6.md · **Successor:** HANDOFF-PHASE8.md · **Results in:** docs/SIMULATION-CORE.md §13–§20, docs/GAME-RULES.md
> **Side items:** S3-M1 the built-in `SkirmishAi` (own development, not a port)
> **Plan:** `docs/plan/PROGRAMME.md` · **Index:** `docs/handoffs/README.md` · **Format:** `docs/plan/BRIEF-FORMAT.md`

# N+1 handoff: Phase 6 – world, terrain, movement, vision and skirmish AI

You work autonomously on this repository (`<repo>` = root of the working directory). First read `CLAUDE.md`, `README.md`,
`docs/ARCHITECTURE.md`, `docs/ROADMAP.md`, `docs/IMPLEMENTATION-PLAN.md` (§5 and §6), `docs/GAME-RULES.md` (especially §4, §6c, §6e–§6h,
§10) and `docs/SIMULATION-CORE.md`. `docs/handoffs/HANDOFF-PHASE5.md` and `docs/handoffs/HANDOFF-PHASE6.md` are kept as history; their numbers are
outdated.

## Honest status of Phase 5 (starting point)

Phase 5 is completed, but with deliberate limits (complete: `docs/GAME-RULES.md` §10, `docs/IMPLEMENTATION-PLAN.md` §5):

- **Block 0.4 open:** the RNG sequence was never compared against a compiled original (no compiler, no original runs). Golden
  masters are derived from the source code. If a possibility is available to compile an original or to capture original runs,
  that has priority; otherwise do not guess.
- **No construction process:** dozers and workers move and deliver but do not build. The reference match assembles bases from start objects plus
  `SpawnSupplyInfrastructure`. No selling, no building site check, no rally point.
- **No terrain, no collision, no pathfinding, no projectile flight time**, no turn-rate-limited orientation.
- The effect of `CommandSet`/`CommandButton` and radar is missing. `ChinookAIUpdate` is missing (America has no
  supply economy in the reference match). There is no `UseSpecialPowerCommand` generator in the reference match.
- Veteran-based `ArmorSet`/`WeaponSet` selection and science purchase points from experience are not modelled;
  `FireWeaponWhenDeadBehavior` has no area damage.

## Reproduction basis (as of the end of Phase 5, 29 September 2026; re-measure before continuing to work)

- Full inventory with all map overlays: 228 INI files loaded (28 excluded), 65,875 blocks, 12,574 definitions, 16,380 modules,
  333,061 keys, 18,138/19,688 references resolved (1,550 visible fallbacks), snapshot 144,813,963 bytes, SHA-256
  `D98CE0154ADF721DC58E9FF1FE769ADA646A058CE7B9AA9A8F0AC99254E5AB67`.
- Retail base data (`--maps exclude`): 12,550 definitions, 2,115 objects, 16,065 modules, snapshot 144,203,312 bytes, SHA-256
  `9F5307957A55CD4683E8A79CBEB079320BE69400802DA38D2FFDDB200713E264`.
- Compatibility baseline 746 entries; module catalogue 205 types, 74 implemented; 1,384 of 2,115 objects instantiable (65.4 %),
  731 blocked; module uses covered 15,364 of 19,553 (78.6 %); 80 modules of the category "Phase 5", 29 "Phase 6", 22 "Phase 7" open.
- Command table 19 types (`CommandTypes.Version = 7`), snapshot version 3, replay format v4.
- Retail reference match: 13 factions, 0 skipped start objects, 707 deliveries (93,075), winner player 12 at tick 26,880,
  final hash `B6E0CDECA3168EFC`.
- Stress test (216,000 ticks): final hash `D80B367CEEF6D72D`, chain hash `8E1FA50D21433123`, 914,394 commands.
- Asset runtime content 25,682 files; IniTool audit 256/256 INI files, 66,625 blocks.

## Part A – world and terrain (data model first)

Scope according to `docs/IMPLEMENTATION-PLAN.md` §6: MAP data to a playable world: heightfield, texture layers, water, bridges, waypoints,
trigger areas and props. First read the map formats on the real data (`.local/phase2-import-a`, map overlays of the full inventory measurement
with `--maps include`) and on the original (`references/`), then build them as a data model of our own.

1. **Map format and world model:** load path via `CnCNew.Formats`/`CnCNew.Import`, run-time access only via `CnCNew.Content`; the
   simulation sees only an integer/fixed-point world model from `CnCNew.Data` (no floating-point numbers, observe `SourceRules`).
2. **Footprints, ground types, collision:** foundation for `PhysicsBehavior` forces, `ToppleUpdate` (76 objects, of which 66 blocked only
   by it), real `CrushDie`/`SquishCollide` (inert so far, §6g), `HeightDieUpdate` (31/15), `OCLAdjustPositionToPassable` and
   `CREATE_AT_EDGE_*` positions (so far `CreationSkipped`).
3. **Pathfinding and movement:** deterministic pathfinding with local avoidance and recalculation, formations, turn-rate-limited
   orientation, `Locomotor` sets; the straight-line movement from §4 is replaced, not supplemented. Two runs must be byte-identical.
4. **Projectile flight time:** `DumbProjectileBehavior`, `MissileAIUpdate` (44/29); damage then takes effect on impact, not on the shot.

## Part B – vision, construction process and skirmish AI

5. **Vision range, shroud, fog, stealth and reveal:** `STEALTHED`/`DETECTED` get a consumer in the targeting system (§6);
   target selection by vision.
6. **Construction process:** dozers/workers build (`DozerAIUpdate`, `WorkerAIUpdate`), building site check, construction, repair by dozers, selling,
   rally point; after that remove the substitute `SpawnSupplyInfrastructure` in the reference match.
7. **`ChinookAIUpdate`/`TransportAIUpdate`** (America supply, 11/0 and 24/13 respectively), `TunnelContain`, `HelixContain`.
8. **Skirmish AI:** build plans, economy, attack, defence, ability use (`CommandButtonHuntUpdate` 79/18,
   `SpecialAbilityUpdate` 64/3, `UseSpecialPowerCommand` generator in the reference match). Several long AI-versus-AI runs must
   end deterministically and without deadlock.
9. **Command set and radar effect**, veteran-based `ArmorSet`/`WeaponSet` selection, rank and science purchase points from
   experience, area damage for `FireWeaponWhenDeadBehavior`.

Before every step measure with `SimTool audit-coverage .local/phase2-import-a` what a module unlocks on its own. Most frequent blockers at the
end of Phase 5 (objects in total / blocked only by this module): `CommandButtonHuntUpdate` 79/18, `ToppleUpdate` 76/66,
`SpecialAbilityUpdate` 64/3, `JetSlowDeathBehavior` 59/0, `MissileAIUpdate` 44/29, `CountermeasuresBehavior` 40/0, `JetAIUpdate` 34/2,
`HeightDieUpdate` 31/15, `DeliverPayloadAIUpdate` 28/1, `MobMemberSlavedUpdate` 27/27, `RailroadBehavior` 25/25,
`TransportAIUpdate` 24/13, `BaikonurLaunchPower` 23/23, `SlavedUpdate` 20/7, `HelicopterSlowDeathBehavior` 18/0,
`PointDefenseLaserUpdate` 17/4.

**Acceptance of Phase 6** (`docs/IMPLEMENTATION-PLAN.md` §6): the vertical slice map and a representative map set load
completely, units navigate reliably, several long AI-versus-AI runs end deterministically without deadlock or illicit
information. If limits remain, name them honestly in `IMPLEMENTATION-PLAN.md`.

## Non-negotiable rules (unchanged, see `CLAUDE.md`)

- Unknown keys/modules/enums/references never fall back silently to defaults; every case is explicit, counted,
  documented and secured against `baselines/compatibility.json`.
- Determinism: canonical sorting, no wall-clock time, no dependency on `Dictionary`/`HashSet` order; two independent
  runs byte-identical. `CnCNew.Simulation` depends only on `CnCNew.Data` (`SourceRules` test); every `IUpdateModule` stores
  `nextWake`.
- `TreatWarningsAsErrors=true`; only the ten known NU1901/NU1903 warnings are explained.
- Always re-measure inventory numbers, never adopt them from docs or this handoff.
- Third-party code may be read, not copied unexamined; every ported algorithm needs a source reference (file, function).
- New commands increase `CommandTypes.Version` (currently 7) and get codes of their own; existing ones never change. Changes to the
  snapshot or replay format increase its version (currently snapshot 3, replay 4); older ones are rejected with a clear message.
- Work only locally, no remote, push nothing; several commits each closed in subject matter; working tree clean at the end;
  at the end of this pass write and commit `docs/handoffs/HANDOFF-PHASE8.md` (or a new continuation brief).

## Final report

Numbers (with commit hashes), implemented rules/modules, determinism, test, stress and replay results, module coverage and
instantiation rate before/after with blocker list, golden master derivations, known limits and technical debt,
`git status` and the confirmation "no remote, nothing pushed".
