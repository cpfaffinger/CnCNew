# N+1 handoff: Phase 5 – continuation (game rules, combat, economy and production)

> **Superseded by `docs/handoffs/HANDOFF-PHASE6.md` and `docs/handoffs/HANDOFF-PHASE7.md`** (29 September 2026, after the special power subsystem). This document is kept as
> the history of the Phase 5 blocks; the numbers below are the state *before* the special power block and were not carried along –
> current values are in the new handoff and in `CLAUDE.md`.

You work autonomously on this repository (`<repo>` = root of the working directory).

## Why a continuation brief instead of Phase 6

Over several passes Block 0 (0.1–0.3), a first working core of the main scope (economy,
sciences, upgrades, direct combat, simple movement, production, victory/defeat), six further blocker modules
(damage states/`TransitionDamageFX`, `FlammableUpdate`, `SlowDeathBehavior`, `FireWeaponWhenDeadBehavior`,
`StealthDetectorUpdate`, `PoisonedBehavior`) as well as the complete containment subsystem (`GarrisonContain`, the
largest single jump so far with +167 instantiable objects, and `TransportContain`, +2) were implemented, tested and
documented (commits see below). The
full scope of the target picture of `docs/GAME-RULES.md` – teams/diplomacy, supply/transport, repair/selling, radar effect,
special abilities, full armour/weapon best selection, a headless reference match of all factions and
golden master scenarios – is **not** reached. That was named as possible from the start: "If Phase 5 is not
complete, generate a precise continuation prompt for Phase 5 instead." This document is this
continuation prompt.

## What is already done (with numbers, re-measure before continuing to work)

First read `docs/GAME-RULES.md` completely (in particular the new §3 subsections and §6a), in addition the
updated sections in `docs/SIMULATION-CORE.md` (§5, §6, §11, §12), `docs/TYPED-DEFINITIONS.md` (the
Block 0.1 sections at the end) and `docs/PHASE3-COMPATIBILITY.md`.

Commits since the last handoff (in this order, `git log`), three per module in each case: data schema+baseline,
simulation implementation, tests (exception: `GarrisonContain` has four, see below):

1. Damage states (`ActiveBody.CalcDamageState`) and `TransitionDamageFX` (`c99fc29`, `93d4954`, `6604046`).
2. `FlammableUpdate` (`0e8f63d`, `ffed6a0`, `66cc0de`) — introduced the generic `IDamageModule` hook
   (`OnDamage`/`OnBodyDamageStateChange`).
3. `SlowDeathBehavior` (`e0d20f7`, `cdd74fa`, `63e03c0`) — weighted sibling lottery on death.
4. `FireWeaponWhenDeadBehavior` (`289ad03`, `8009713`, `f661af4`).
5. `StealthDetectorUpdate` (`5c1c59f`, `35a4985`, `1d0159b`) — introduced `ObjectTemplate.VisionRange` and
   `StealthUpdate.MarkDetected`/the `DETECTED` expiry mechanism.
6. `PoisonedBehavior` (`085ab72`, `2551b0a`, `e0fd3f3`) — introduced `IDamageModule.OnHealing` and closed an
   existing gap (`OnBodyDamageStateChange` was missing in the healing path so far).
7. `GarrisonContain` (`5a16c3c` schema+baseline, `784cb22` a standalone fix — `ModuleConfigReader.Real` did not yet know
   `Percentage`-typed values, uncovered by `DamagePercentToUnits` —, `d3f0a9a` containment
   infrastructure + module, `0e77be1` tests) — largest single jump of this phase, see below.
8. `TransportContain` (`515c7a0` schema+baseline, `64965d3` `ContainModuleBase` extraction + module, `52fe5e7` tests) —
   second implementer of the containment infrastructure, justified the `ContainModuleBase` extraction.
9. `CreateCrateDie` (`323a827` schema+baseline `CrateData`, `918498a` `CrateCatalog` + module) — draw order as in
   `CreateCrateDie.cpp`; crate objects still need `*CrateCollide`.
10. Creation lists (`0998461` `ObjectCreationList` strictly typed, `3c097b5` tests/fixtures, `e2d7b0a`
    `GameData/Gravity` typed, `adc2e94` `SimulationWorld.CreateFromList` with delayed `SpawnSetup`,
    `9a32488` tests including `FixedTrig`) — `docs/GAME-RULES.md` §6b.

11. Reference match and golden master (`19b2df4` replay format v2 with factions and all 16 command types, `e5bf4cf`
    `ReferenceMatchScenario` + `SimTool reference-match` + synthetic proof, `dd3e6df` golden master for
    damage chain and production clock, `9959b49` docs) — `docs/GAME-RULES.md` §7a. At first the match ran only on
    the synthetic fixture.
12. Retail match (`2bb18e2` and the commits before it, `git log` from `6712faf`): fourteen modules (`docs/GAME-RULES.md` §6c),
    `--diagnose` for `SimTool reference-match`, real-data test `Tests.RealReferenceMatch` and the defeat rule
    `Team::hasAnyObjects` (§7). After that the match ran on retail data with 13 factions (at first with barracks as a substitute).
13. Special power subsystem (`5f34b41` schema/catalogue/baseline, `9efbf2c` modules, command, science wake-up and bounty, `1de0b6c`
    tests): ten modules, `UseSpecialPowerCommand` (code 17, `CommandTypes.Version = 5`), the real command centres of all 13
    factions are instantiable and in the reference match (`docs/GAME-RULES.md` §6d/§7a).

**Important reprioritisation compared with the previous handoff:** the original order "by frequency"
was replaced throughout by fresh `audit-coverage` measurements. `CreateCrateDie` unlocks **0 objects on its own**
(every occurrence additionally depends on another module that is still missing) and would need a new `Crate`
catalogue **and** a relationship/diplomacy check — deliberately postponed. `ToppleUpdate` was verified by reference study
as a Phase 6 candidate (needs real collision detection), not just presumed. `GarrisonContain` (167
on its own) turned out to be by far the largest single blocker, although it stood further back in the original
"by frequency" list. **Before every further module measure for yourself what it unlocks on its own**
(`SimTool audit-coverage`) — the frequency of a module use is not a good indicator of the unlock gain.

**Starting state to reproduce** (29 September 2026, re-measure – do not trust these numbers):

- `dotnet build CnCNew.slnx`: 0 errors, 10 known NU1901/NU1903 warnings.
- All eight test programs green, including `CnCNew.Simulation.Tests` with `--content-dir .local/phase2-import-a`
  and `-c Release -- stress` (216,000 ticks, identical hashes over three fresh runs and snapshot continuation).
- Module catalogue: 205 types, **50 implemented** (12 from Phase 4, 38 from Phase 5).
- Compatibility baseline: **767 entries**, `compat-check` passes.
- Full inventory: 228 loaded files, 65,875 blocks, 12,574 definitions, 16,380 modules, 333,061 keys,
  17,291/18,841 references resolved (1,550 visible fallbacks), snapshot 144,432,181 bytes, SHA-256
  `A1BB5D3464024113750A338C9DE97D9454CA718595EFD6DBB5395F6B5AB2170B` (hash changed compared with the last measurement:
  `ObjectCreationList`, `CrateData`, `GameData/Gravity` are now strictly typed).
- Retail base data (`--maps exclude`): 2,115 objects, SHA-256
  `45E6802750BFBAB72B20EBAA1EBC2ED54C555BBB86B50C586F3BCD6F536C3606`.
- Instantiability: **1,199 of 2,115 objects (56.7 %)**, module uses 14,624 of 19,553 (74.8 %); compared with
  26/2,113 at the end of Phase 4 and 863/2,115 before the retail match modules. Retail base fingerprint `63639A608923B2B4`
  (short form; the snapshot hashes above come from an earlier measurement and are to be determined anew).
- Stress test (`SimTool stress`, synthetic fixture, unchanged compared with the measurement with 36 modules): 216,000 ticks, final hash `9E4AF85CBFBB84C6`, chain hash
  `36211DD938214B9C` (creation lists now consume random draws), runs and snapshot continuation identical, all
  tables return to zero.
- `CommandTypes.Version = 4` (16 command types; `EnterContainerCommand`/`ExitContainerCommand` new); replay format
  version 2 (factions in the player table, all 16 command types encodable).
- 47 simulation tests green (`Tests.ReferenceMatch`, `Tests.Phase5GoldenDamageChain`, `Tests.Phase5GoldenProduction`
  new); stress test (freshly measured): 914,394 commands, replay file 47,094,803 bytes, peak 15,775 entities,
  2,170 `UnknownEntity` rejections, snapshot at tick 108,000: 10,262,399 bytes.

**Remaining blockers by objects that still depend exclusively on them** (freshly measured,
`SimTool audit-coverage`, 29 September, after the retail match modules): `ToppleUpdate` 66, `CrushDie` 39,
`MissileAIUpdate` 26, `FireWeaponUpdate` 26, `MobMemberSlavedUpdate` 27, `RailroadBehavior` 25, `BaikonurLaunchPower` 23,
`ReplaceObjectUpdate` family (`ReplaceObjectUpgrade` 16), `RepairDockUpdate` 15, `TransportAIUpdate` 13, `PropagandaTowerBehavior` 11,
`CommandButtonHuntUpdate` 10, `AutoFindHealingUpdate` 10, `DozerAIUpdate` 10; `SpecialAbility` (88 objects),
`SpecialAbilityUpdate` (64) and `OCLSpecialPower` (60) depend only jointly on the special power subsystem, which unlocks the real
command centres of all factions. `ToppleUpdate` needs real collision detection (`ToppleUpdate::onCollide` triggers only
on a real geometry overlap of a vehicle, checked by reference study; the only other callers of
`applyTopplingForce` in the original — `NeutronMissileSlowDeathUpdate`, `TensileFormationUpdate`, `WaveGuideUpdate` —
are themselves unimplemented Phase 6 modules) — explicitly Phase 6. `PhysicsBehavior`/`SquishCollide` are implemented as a
rest state (§6c in `docs/GAME-RULES.md`).

**Implemented** (details, numerics and every deliberate simplification with source reference in `docs/GAME-RULES.md`):

- Economy, players/factions, sciences, upgrades, combat, movement, production, victory: see the previous handoffs
  (unchanged in this pass) and `docs/GAME-RULES.md` §1–§7.
- Damage states (`PRISTINE`/`DAMAGED`/`REALLYDAMAGED`/`RUBBLE`) and `TransitionDamageFX` (OCL request on the
  change into a worse state, only with a resolvable damage source).
- `FlammableUpdate`: flame damage accumulation, ignition, periodic self-damage, permanent `BURNED`.
- `SlowDeathBehavior`: weighted sibling lottery, delayed destruction, `Entity.SlowDeathStarted` lock.
- `FireWeaponWhenDeadBehavior`: `DeathWeapon` on death, `StartsActive`/`TriggeredBy`, renewed `ConflictsWith` check
  at death itself (not only at activation).
- `StealthDetectorUpdate`: periodic unstealth search, `StealthUpdate.MarkDetected`/`DETECTED` expiry,
  `ObjectTemplate.VisionRange` as a fallback for `DetectionRange`.
- `PoisonedBehavior`: periodic poison after-damage with the amount actually inflicted, extension on
  renewed poisoning, healing ends the cycle (new `IDamageModule.OnHealing` hook).
- **Containment (`docs/GAME-RULES.md` §6a), complete:** `Entity.ContainedBy`/`Entity.Container`, `IContainerModule`,
  `ContainModuleBase` (shared gating/enter/exit/death logic), `EnterContainerCommand`/`ExitContainerCommand`.
  `GarrisonContain`: capacity, `KindOf`/relationship filter (same owner = ally, no teams), lock and
  immediate ejection at `REALLYDAMAGED` (except `GARRISONABLE_UNTIL_DESTROYED`), periodic healing, death applies
  `DamagePercentToUnits` and ejects survivors (does not kill them). `TransportContain`: weighted capacity
  (`ObjectTemplate.TransportSlotCount`), continuous `HealthRegen%PerSec` healing instead of a ramp.
- Block 0.1 (Weapon/Armor/Upgrade/Science/Locomotor/Faction), 0.2 (`StealthUpdate`/`AutoHealBehavior`) and 0.3
  (fear signal) complete, unchanged since the last handoff.

**Not done / deliberately postponed** (see `docs/GAME-RULES.md` §10 and `docs/IMPLEMENTATION-PLAN.md` §5):

- Block 0.4 (RNG comparison with a compiled original) — still no compiler available, remains documented debt.
- Teams/diplomacy/alliances as a data model of its own (victory stays "last survivor" without teams;
  `StealthDetectorUpdate`/`GarrisonContain` make do with "same owner = ally").
- `CommandSet`/`CommandButton` as a real data model (so far only the state machine of `CommandSetUpgrade`, no effect).
- Special abilities (`SpecialAbility*`, `OCLSpecialPower`); the list execution itself exists, `CreateDebris`,
  `ApplyRandomForce`, `FireWeapon`, `Attack` and `DeliverPayload` are reported as `CreationSkipped`.
- Area damage, projectile flight time, `WeaponBonus` multipliers.
- Full veteran-based `ArmorSet`/`WeaponSet` best selection (`SparseMatchFinder`).
- Awarding experience/veterancy from combat; awarding rank/`SciencePurchasePoints` from experience.
- Energy slowdown factor in production (`ProductionUpdate::calcTimeToBuild`).
- `InitialRoster`/`InitialPayload` (synchronous nested creation on construction), firing out of the
  container, garrison points (art-derived), capture handling. Supply (`SupplyCenter*`, `SupplyTruckAIUpdate`,
  `SupplyWarehouse*`), construction (`DozerAIUpdate`, `BuildAssistant`), repair, selling, radar effect — all still
  completely open.
- `*CrateCollide` modules (the crates themselves).
- `ToppleUpdate` and real forces/collision for `PhysicsBehavior`/`SquishCollide` (explicitly Phase 6 — for
  `ToppleUpdate` verified by reference study, see above).
- **Reference match on retail data:** runs (`SimTool reference-match .local/phase2-import-a`): 13 of 13 factions,
  winner player 1 at tick 7,320, replay 7,621 ticks, final hash `73135C0BF7144ED6`, runs and snapshot continuation
  identical (`docs/GAME-RULES.md` §7a). **Substitute bases:** all 13 start bases are barracks or a Gatling tower, because the
  command centres need `OCLSpecialPower`, `CashBountyPower`, `CashHackSpecialPower`, `SpecialAbility`,
  `GrantUpgradeCreate`, `GrantScienceUpgrade` and `SpectreGunshipDeploymentUpdate`; 13 start objects (9 dozers,
  4 GLA workers) are skipped. Next block: the special power subsystem (typed `SpecialPower` definition,
  `SpecialPowerCatalog`, ready frames shared per player, `UseSpecialPower` command with a `CommandTypes.Version` increase,
  modules `OCLSpecialPower`, `CashBountyPower`, `CashHackSpecialPower`, `SpecialAbility` with explicit rejection without a
  handler, `SpecialPowerCreate`, `GrantUpgradeCreate`, `GrantScienceUpgrade`, `UnpauseSpecialPowerUpgrade`,
  `SpecialPowerCompletionDie`, `SpectreGunshipDeploymentUpdate`), after that the real command centres in the match.
- **Golden master:** damage chain and production clock are secured with an independent model (`Tests.GoldenMaster.cs`),
  supply income and experience are missing for lack of an implementation. There are no original runs as a reference, only
  calculations derived from the source code.

## Brief for this pass

Continue the remaining scope of Phase 5, in this order (every step with tests, real-data audit,
docs update, as described in `CLAUDE.md` "Procedure of a phase" (Ablauf einer Phase)). Before every step measure for yourself which
module unlocks how many objects **on its own** (`SimTool audit-coverage .local/phase2-import-a`) — do not rely
on the list above, it is already a snapshot in time:

1. ~~`CreateCrateDie`~~ – done (item 9 above).
2. ~~Type and execute creation lists~~ – done (item 10 above).
3. **Teams/diplomacy as a data model**, if necessary for the reference match (item 4) — otherwise postpone and stay,
   in the headless reference match, with "last survivor without teams" (documented simplification).
4. ~~**Enable the reference match on retail data.**~~ Done; the real command centres followed with the special power subsystem (item 13).
   Original brief: the machinery is in place (`ReferenceMatchScenario`, `SimTool
   reference-match`); instantiable armed units and production buildings are missing. Procedure: first
   re-measure `reference-match` and `audit-coverage`, then per side specifically unlock the modules that make the smallest
   set of units plus command centre/barracks/war factory instantiable (candidates: `DefaultProductionExitUpdate`,
   `QueueProductionExitUpdate`, `BaseRegenerateUpdate`, `HealContain`, `RebuildHoleBehavior`/`RebuildHoleExposeDie`,
   `GenerateMinefieldBehavior`, `PreorderCreate`, `RadarUpdate`, an `OCLSpecialPower`/`SpecialPower` subsystem, and for the
   units `PhysicsBehavior`/`SquishCollide` in anticipation of Phase 6). Only when at least two factions take part,
   match from start to victory, capable of ≥ 216,000 ticks, two fresh runs plus snapshot continuation hash-identical, prove it in the docs
   and in a test under `--content-dir`.
5. **Golden master scenarios for supply income and experience**, as soon as both are implemented (damage chain and
   production clock are done), with an independent recalculation and source reference.
6. **Docs**: update `docs/GAME-RULES.md`, `SIMULATION-CORE.md`, `TYPED-DEFINITIONS.md`, `PHASE3-COMPATIBILITY.md`, README,
   ARCHITECTURE, ROADMAP, IMPLEMENTATION-PLAN, CLAUDE.md; fresh numbers, none adopted.

If Phase 5 is still not complete after that, write a precise continuation prompt again instead of a
Phase 6 handoff — that is still explicitly allowed.

## Non-negotiable rules (unchanged, see `CLAUDE.md`)

- Unknown keys/modules/enums/references never fall back silently to defaults; every case is explicit,
  counted, documented and secured against `baselines/compatibility.json`.
- Determinism: canonical sorting, no wall-clock time, no dependency on `Dictionary`/`HashSet` order; two
  independent runs must be byte-identical. `CnCNew.Simulation` still depends only on `CnCNew.Data`
  (`SourceRules` test).
- `TreatWarningsAsErrors=true`; only the ten known NU1901/NU1903 warnings are explained.
- Always re-measure inventory numbers, never adopt them from docs or this handoff.
- Third-party code may be read, not copied unexamined; every ported algorithm needs a source reference
  (file, function) in the code comment.
- New commands increase `CommandTypes.Version` (currently 4) and get codes of their own; existing ones never change.
- Work only locally, no remote, push nothing; several commits each closed in subject matter; working tree clean at the end.

## Final report

As in the previous passes: numbers (with commit hashes), implemented rules/modules, determinism, test,
stress and replay results, module coverage and instantiation rate before/after with blocker list,
golden master derivations (if reached), known limits and technical debt, `git status` and the confirmation
"no remote, nothing pushed".
