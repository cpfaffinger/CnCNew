using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Data.Schema;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Random;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.Timers;
using CnCNew.Simulation.World;

var run = new Runner(args.SkipWhile(value => value != "--only").Skip(1).FirstOrDefault());
run.Test("Fixed-point arithmetic, rounding and limits", Tests.FixedPoint);
run.Test("Tick rate, duration and unit conversions", Tests.Timing);
run.Test("Reference float conversion vs exact ticks (documented deviation)", Tests.FloatDeviation);
run.Test("Golden RNG sequences (independent transcription)", Tests.GoldenRandom);
run.Test("RNG streams: seeds, state round trip, isolation", Tests.RandomStreamsBehave);
run.Test("Ids: monotone, start at 1, 0 invalid", Tests.IdsAreMonotone);
run.Test("Binding table validation", Tests.BindingTable);
run.Test("Templates from the synthetic fixture", Tests.FixtureTemplates);
run.Test("Spawn, tick, damage, death and destroy lifecycle", Tests.Lifecycle);
run.Test("Body and die module semantics", Tests.BodyAndDieModules);
run.Test("Lifetime and deletion updates", Tests.LifetimeAndDeletion);
run.Test("Update order, mutation during iteration, event delivery", Tests.UpdateOrderAndMutation);
run.Test("Timers", Tests.TimerBehaviour);
run.Test("Command validation and order", Tests.CommandValidationAndOrder);
run.Test("State hash: determinism, order independence, incremental == full", Tests.HashDeterminism);
run.Test("Snapshot round trip and continuation", Tests.SnapshotRoundTrip);
run.Test("Corrupt, truncated and foreign snapshots are rejected", Tests.SnapshotRejection);
run.Test("Replay format round trip and rejection", Tests.ReplayFormatTests);
run.Test("Replay equality, divergence detection and continuation", Tests.ReplayRuns);
run.Test("Stress scenario returns to baseline (short run)", Tests.ShortStress);
run.Test("Definition insertion order does not change the simulation", Tests.DefinitionOrderIndependence);
run.Test("Idle ticks do not allocate", Tests.IdleAllocation);
run.Test("Determinism rules hold in the simulation sources", Tests.SourceRules);
run.Test("Phase 5: fear-sound RNG consumption (block 0.3)", Tests.Phase5FearSoundRng);
run.Test("Phase 5: economy and upgrade purchase", Tests.Phase5Economy);
run.Test("Phase 5: weapon fire, armor, clip and reload", Tests.Phase5Combat);
run.Test("Phase 5: armor/weapon/max-health upgrade effects", Tests.Phase5UpgradeEffects);
run.Test("Phase 5: straight-line movement and arrival", Tests.Phase5Movement);
run.Test("Phase 5: production queue, spawn, cancel and refund", Tests.Phase5Production);
run.Test("Phase 5: stealth cloaking and auto-heal", Tests.Phase5StealthAndHeal);
run.Test("Phase 5: FlammableUpdate ignite/burn/damage cycle", Tests.Phase5Flammable);
run.Test("Phase 5: body damage states and TransitionDamageFX", Tests.Phase5DamageStateTransitions);
run.Test("Phase 5: player/faction setup and science purchase", Tests.Phase5Sciences);
run.Test("Phase 5: SlowDeathBehavior weighted sibling lottery", Tests.Phase5SlowDeath);
run.Test("Phase 5: FireWeaponWhenDeadBehavior activation and conflict re-check", Tests.Phase5FireWeaponWhenDead);
run.Test("Phase 5: StealthDetectorUpdate marks and expires DETECTED", Tests.Phase5StealthDetector);
run.Test("Phase 10: StealthDetectorUpdate respects height line of sight", Tests.Phase5StealthDetectorHeightLineOfSight);
run.Test("Phase 5: PoisonedBehavior periodic damage, re-poison and healing", Tests.Phase5Poisoned);
run.Test("Phase 5: BaseRegenerateUpdate delay, bursts and stop at full health", Tests.Phase5BaseRegenerate);
run.Test("Phase 5: PhysicsBehavior, SquishCollide and GenerateMinefieldBehavior", Tests.MatchMinefieldAndPhysics);
run.Test("Phase 5: BaseRegenerateUpdate wake tick survives a snapshot", Tests.MatchBaseRegenerateSnapshot);
run.Test("Phase 5: production exits, burst and exit delay", Tests.RetailProductionExits);
run.Test("Phase 5: HealContain heals and releases occupants", Tests.RetailHealContain);
run.Test("Phase 5: EjectPilotDie, VeterancyGainCreate, PreorderCreate, RadarUpdate", Tests.RetailSmallModules);
run.Test("Phase 5: RebuildHoleExposeDie and RebuildHoleBehavior", Tests.RetailRebuildHole);
run.Test("Phase 5: GarrisonContain capacity, entry, healing and ejection", Tests.Phase5Garrison);
run.Test("Phase 5: TransportContain slot-weighted capacity and health regen", Tests.Phase5Transport);
run.Test("Phase 5: CreateCrateDie conditions, draw order and requests", Tests.Phase5CreateCrateDie);
run.Test("Phase 5: ObjectCreationList execution and skip events", Tests.Phase5CreationLists);
run.Test("Phase 5: ObjectCreationList determinism and snapshot", Tests.Phase5CreationListDeterminism);
run.Test("Fixed-point Sin/Cos accuracy", Tests.FixedTrigAccuracy);
run.Test("Phase 5: last player standing wins", Tests.Phase5Victory);
run.Test("Phase 5: reference match scenario, replay v2 factions, determinism", Tests.ReferenceMatch);
run.Test("Special powers: use command, recharge, OCL origins, pause and science gating", Tests.SpecialPowerUse);
run.Test("Special powers: shared timers, create hooks, science grant and gunship handler", Tests.SpecialPowerWiring);
run.Test("Special powers: cash hack and cash bounty", Tests.SpecialPowerCash);
run.Test("Special powers: snapshot continuation and replay round-trip", Tests.SpecialPowerSnapshot);
run.Test("Phase 5 golden master: damage chain with armor, clip and reload", Tests.Phase5GoldenDamageChain);
run.Test("Phase 5 golden master: production clock and ledger", Tests.Phase5GoldenProduction);
run.Test("Supply economy golden master: independent trip recomputation, ledger and determinism", Tests.SupplyEconomyGoldenMaster);
run.Test("Veterancy golden master: experience, levels and health scaling", Tests.VeterancyExperienceAndHealthGoldenMaster);
run.Test("Veterancy: no experience for non-trainable killers and IGNORED_IN_GUI victims", Tests.VeterancyGrantRules);
run.Test("Veterancy golden master: weapon bonus damage, range and rate of fire per level", Tests.VeterancyWeaponBonusGoldenMaster);
run.Test("Veterancy: experience scalar, weapon bonus upgrade, starting level and snapshot", Tests.VeterancyUpgradesAndStartingLevel);
run.Test("Veterancy: veteran ArmorSet/WeaponSet selection by rank", Tests.VeterancyArmorAndWeaponSetSelection);
run.Test("Supply economy: an unordered hauler stays idle", Tests.SupplyIdleHaulerEarnsNothing);
run.Test("Supply economy: warehouse queue and DeleteWhenEmpty", Tests.SupplyQueueAndDeleteWhenEmpty);
run.Test("Supply economy: DockCommand validation", Tests.SupplyDockCommandValidation);
run.Test("Supply economy: AutoDepositUpdate rhythm, boost and neutral owners", Tests.SupplyAutoDeposit);
run.Test("Supply economy: snapshot continuation and DockCommand replay round trip", Tests.SupplySnapshotAndReplay);
run.Test("SpawnBehavior: creation, replacement delay, SpawnedRequireSpawner and snapshot", Tests.SpawnBehaviorReplaceAndRequireSpawner);
run.Test("SpawnBehavior: OneShot, missing exit and non-instantiable templates", Tests.SpawnBehaviorOneShotAndSkips);
run.Test("SpawnBehavior: unsupported configuration is a named missing module", Tests.SpawnBehaviorUnsupportedConfiguration);
run.Test("Teams: relationship matrix, SetRelationshipCommand, snapshot and replay round trip", Tests.TeamsRelationshipMatrix);
run.Test("Teams: kill scoring needs an enemy", Tests.TeamsKillScoring);
run.Test("Teams: winning alliance", Tests.TeamsVictoryConditions);
run.Test("Teams: container gating by relationship", Tests.TeamsContainRelationship);
run.Test("Repair: RepairDockUpdate heals over TimeForFullHeal", Tests.RepairDockHealsOverTime);
run.Test("Repair: RepairDockUpdate snapshot mid-repair", Tests.RepairDockSnapshot);
run.Test("Repair: AutoFindHealingUpdate walks a hurt computer unit into a pad", Tests.AutoFindHealingWalksToPad);
run.Test("Repair: AutoFindHealingUpdate refusals", Tests.AutoFindHealingRefusals);
run.Test("Repair: AutoFindHealingUpdate picks the closest pad", Tests.AutoFindHealingPicksClosestPad);
run.Test("Repair: computer flag in the replay header", Tests.RepairPlayerTypeReplayHeader);
run.Test("Combat: radius damage rings and the enemies-only filter", Tests.RadiusDamageRings);
run.Test("Combat: default affects mask and bounding sphere", Tests.RadiusDamageDefaultsAndBoundingSphere);
run.Test("Combat: FireWeaponUpdate initial delay, cadence and SELF filter", Tests.FireWeaponUpdateCadence);
run.Test("Combat: FireWeaponUpdate clip and reload", Tests.FireWeaponUpdateClip);
run.Test("Combat: FireWeaponUpdate ExclusiveWeaponDelay", Tests.FireWeaponUpdateExclusiveDelay);
run.Test("Combat: FireWeaponUpdate snapshot", Tests.FireWeaponUpdateSnapshot);
run.Test("Combat: ReplaceObjectUpgrade swaps the template", Tests.ReplaceObjectUpgradeSwapsTemplate);
run.Test("Combat: CrushDie binds and stays inert", Tests.CrushDieIsInert);
run.Test("Terrain: height sampling, border and clamping", Tests.TerrainHeightSampling);
run.Test("Terrain: cliff and water cell classification, overlapping water", Tests.TerrainCellClassification);
run.Test("Terrain: world placement, snapshot v5 and replay v5 carry the fingerprint", Tests.TerrainInWorldSnapshotAndReplay);
run.Test("Terrain: start positions, waypoints, fingerprint and boundary validation", Tests.TerrainStartsAndFingerprint);
run.Test("Terrain: height-based line of sight", Tests.TerrainHeightLineOfSight);
run.Test("Navigation: integer Atan2, angle difference and normalization", Tests.NavigationAtan2);
run.Test("Navigation: surface priorities, footprints and the obstacle sum", Tests.NavigationGridRules);
run.Test("Navigation: A* around walls, smoothing, partial and blocked paths", Tests.NavigationPathfinding);
run.Test("Navigation: path following, turn rate, terrain height and legacy locomotors", Tests.NavigationMovement);
run.Test("Navigation: water, cliffs, air and unreachable goals", Tests.NavigationSurfacesAndUnreachableGoals);
run.Test("Navigation: re-planning after a structure is built or destroyed", Tests.NavigationRepathsAfterConstruction);
run.Test("Navigation: snapshot restore with paths in flight and two-run determinism", Tests.NavigationSnapshotsAndDeterminism);
run.Test("Vision: circular reveal, exploration memory and shroud clearing range", Tests.VisionRevealsACircleAndKeepsExploration);
run.Test("Vision: allies, construction sites and reveal-to-all", Tests.VisionAlliesSharingAndConstruction);
run.Test("Vision: targeting is gated by visibility and stealth", Tests.VisionGatesTargeting);
run.Test("Vision: snapshot round trip and two-run determinism", Tests.VisionSnapshotAndDeterminism);
run.Test("Construction: a dozer builds a structure over BuildTime ticks", Tests.ConstructionBuildsAStructure);
run.Test("Construction: cancelling a site refunds it in full", Tests.ConstructionCancelRefunds);
run.Test("Construction: sale takes 179 ticks and pays SellPercentage", Tests.ConstructionSellTimingAndPayout);
run.Test("Construction: selling a producer refunds its queue", Tests.ConstructionSellCancelsProduction);
run.Test("Construction: produced units walk to the rally point", Tests.ConstructionRallyPoint);
run.Test("Construction: dozers repair damaged structures", Tests.ConstructionRepair);
run.Test("Construction: a second builder resumes an unfinished site", Tests.ConstructionResumeWithAnotherBuilder);
run.Test("Construction: rejections for permission, prerequisites, funds and kind", Tests.ConstructionRejections);
run.Test("Construction: production rejections (side, buildable, prerequisites, MaxSimultaneousOfType)", Tests.ConstructionProductionRejections);
run.Test("Construction: placement on terrain", Tests.ConstructionPlacementRules);
run.Test("Construction: snapshot mid-construction and mid-sale, replay round trip", Tests.ConstructionSnapshotAndReplay);
run.Test("Navigation: escape from and goal inside a footprint", Tests.NavigationFootprintEscape);
run.Test("Scripts: the support table matches the legacy type catalog", Tests.ScriptSupportTableIsConsistent);
run.Test("Scripts: counters, flags, timers and one-shot scripts", Tests.ScriptEngineCountersFlagsAndTimers);
run.Test("Scripts: activation, difficulty, false actions, delay and subroutines", Tests.ScriptEngineActivationDifficultyAndSubroutines);
run.Test("Scripts: teams, named objects, money, relations, ownership and victory", Tests.ScriptEngineWorldActionsAndConditions);
run.Test("Scripts: orders are validated player commands", Tests.ScriptEngineOrdersAreRealCommands);
run.Test("Scripts: damage history, kinds in areas, built objects and finished paths", Tests.ScriptEngineDamageHistoryAreasAndPaths);
run.Test("Scripts: attack priorities steer targeting", Tests.ScriptEngineAttackPrioritiesAndReinforcements);
run.Test("Scripts: missing types are counted, presentation is forwarded", Tests.ScriptEngineMissingTypesAreCounted);
run.Test("Scripts: command-button abilities fire the named special power", Tests.ScriptEngineCommandButtonAbilities);
run.Test("Scripts: an explicit human side overrides the map default", Tests.ScriptEngineHumanSideSelection);
run.Test("Scripts: SKIRMISH_* conditions (units/value in area, outside area, attacked by player, area exists)", Tests.ScriptSkirmishConditions);
run.Test("Scripts: special-power, sight and discovery conditions", Tests.ScriptSpecialPowerAndSightConditions);
run.Test("Scripts: map reveal (permanent, undo, quick look) and shroud", Tests.ScriptMapRevealAndShroud);
run.Test("Scripts: construction switches and world snapshot", Tests.ScriptBuildSwitchesAndSciences);
run.Test("Scripts: garrison, enter and exit actions", Tests.ScriptContainerAndGarrisonActions);
run.Test("Scripts: warehouse value and team merge", Tests.ScriptWarehouseAndTeamActions);
run.Test("Scripts: BUILD_TEAM produces and activates the team", Tests.ScriptBuildTeamProducesTheTeam);
run.Test("Scripts: other command buttons and waypoint abilities", Tests.ScriptOtherCommandButtonsAndWaypointAbilities);
run.Test("Scripts: named special powers, countdowns, named reveals and recorded-only actions", Tests.ScriptSpecialPowerActionsRevealsAndRecording);
run.Test("Scripts: RECRUIT_TEAM takes free units into the team", Tests.ScriptRecruitTeam);
run.Test("Scripts: team event scripts (create, enemy sighted, unit destroyed, destroyed)", Tests.ScriptTeamEventScripts);
run.Test("Scripts: the objective driver ends a mission through its own victory and defeat scripts", Tests.ScriptObjectiveDriver);
run.Test("Scripts: the goal-directed driver plans backwards from the ending and avoids the opposite ending", Tests.ScriptObjectiveDriverPlansBackwards);
run.Test("Scripts: the commands-only driver uses orders, not world writes", Tests.ScriptCommandsOnlyDriverUsesOrdersNotWrites);
run.Test("Scripts: the commands-only driver declines what only a write can do", Tests.ScriptCommandsOnlyDriverDeclinesWhatOnlyAWriteCanDo);
run.Test("Scripts: team facing a named object, selling everything and the recorded settings", Tests.ScriptPhase13SettingActions);
run.Test("Scripts: the driver moves counters the right way", Tests.ScriptObjectiveDriverMovesCountersTheRightWay);
run.Test("Scripts: explaining a script leaves the engine untouched", Tests.ScriptExplainLeavesTheEngineUntouched);
run.Test("Scripts: override relations between teams and players", Tests.ScriptOverrideRelations);
run.Test("Scripts: team command buttons (nearest enemy, partial use)", Tests.ScriptTeamCommandButtons);
run.Test("Scripts: hunting with a command button", Tests.ScriptHuntWithCommandButton);
run.Test("Scripts: facing, unmanned objects and recorded-only actions", Tests.ScriptFacingUnmannedAndRecorded);
run.Test("Mission: a chosen persona adds or adapts the human side and survives save/restore", Tests.MissionHumanFactionAddsAndAdaptsTheHumanSide);
run.Test("Modules: damage reaction weapon, horde, fire-weapon and launch powers, tech building and the shells", Tests.Phase12ModulesCreateAndBehave);
run.Test("Flight: scripted paradrop (plane, parachutes, landing, deletion, continuation)", Tests.FlightParadropReinforcement);
run.Test("Flight: a ground transport sets its team down, returns and is deleted", Tests.FlightTransportReturnsAndLeaves);
run.Test("Flight: a shot-down payload plane takes its riders with it", Tests.FlightShotDownPlaneDestroysItsPayload);
run.Test("Flight: countermeasures divert missiles to flares", Tests.FlightCountermeasuresDivertMissiles);
run.Test("Flight: the particle uplink cannon burns its target and restores mid-beam", Tests.FlightParticleUplinkCannonBurnsItsTarget);
run.Test("Flight: a battle bus has two lives and is thrown by the first death", Tests.FlightBattleBusHasTwoLives);
run.Test("Slaves: the fixture objects of the slave/spawn/container group are instantiable; a bad rider line is a data error", Tests.SlaveFixtureObjectsAreInstantiable);
run.Test("Slaves: a drone is enslaved by its spawner, follows its master and is released when it dies", Tests.SlavedUpdateFollowsItsMaster);
run.Test("Slaves: a creation list enslaves its drone, a slave defects with its master and a masterless flier crashes", Tests.SlavedUpdateCreationListAndUnmanned);
run.Test("Slaves: a battle drone repairs its hurt master", Tests.SlavedBattleDroneRepairsItsMaster);
run.Test("Slaves: a scout drone grants the spotting bonus while its master attacks", Tests.SlavedScoutDroneGrantsSpottingBonus);
run.Test("Slaves: the angry mob nexus (budding, aggregate health, members that keep up or die)", Tests.SpawnBehaviorMobNexus);
run.Test("Slaves: a mob member without a nexus dies", Tests.MobMemberWithoutNexusDies);
run.Test("Slaves: a hive passes damage to its slaves, swallows it without them, and refills its spawn points", Tests.HiveStructureBodyPassesDamageToSlaves);
run.Test("Slaves: the propaganda tower buffs and heals allies in range, does not stack and lets go", Tests.PropagandaTowerBuffsAlliesInRange);
run.Test("Slaves: a combat bike swaps riders, is scuttled when deserted and takes its rider along", Tests.RiderChangeContainSwapsRiders);
run.Test("Slaves: an overlord redirects its container to the structure on its back", Tests.OverlordContainRedirectsToItsStructure);
run.Test("Slaves: hackers earn cash in the internet center, pack before they move", Tests.HackersEarnCashInsideTheInternetCenter);
run.Test("Slaves: the passenger-fire upgrade sets the container's flag", Tests.PassengersFireUpgradeSetsTheFlag);
run.Test("Slaves: an assault transport deploys its healthy troops on a deploy shot", Tests.AssaultTransportDeploysItsTroops);
run.Test("Slaves: avalanche rocks slide down the slope once one is hurt and then rest", Tests.TensileFormationSlidesDownTheSlope);
run.Test("Scripts: an upgrade command button is ready until the upgrade is owned", Tests.FlightUpgradeButtonReadiness);
run.Test("Scripts: team creation runs the production-condition start actions", Tests.FlightTeamCreationRunsStartActions);
run.Test("Scripts: sequential scripts advance on idle, wait on timers, loop, stop and survive save/restore", Tests.ScriptSequentialScripts);
run.Test("Scripts: save/restore continues hash-identically, two runs equal", Tests.ScriptEngineSnapshotContinuesIdentically);
run.Test("Scripts: corrupt, truncated and foreign mission states are rejected", Tests.ScriptEngineSnapshotRejection);
run.Test("Modules: CommandButtonHuntUpdate and LockWeaponCreate shells", Tests.MissionBlockerShellModules);
run.Test("Power: PowerPlantUpgrade adds and removes the energy bonus", Tests.PowerPlantUpgradeAddsAndRemovesTheBonus);
run.Test("Power: OverchargeBehavior toggles, drains and switches itself off", Tests.OverchargeTogglesDrainsAndSwitchesItselfOff);
run.Test("Power: the TOGGLE_OVERCHARGE command reaches the module", Tests.OverchargeCommandTogglesTheModule);
run.Test("Power: CaptureCommand transfers ownership and the power pool", Tests.CaptureTransfersOwnershipAndThePowerPool);
run.Test("Skirmish: CommandCatalog build offers", Tests.CommandCatalogBuilds);
run.Test("Skirmish: two AIs play a match, replay, determinism and snapshot continuation", Tests.SkirmishAiPlaysAMatch);
run.Test("Skirmish: the AI knows no enemy structure before it has seen one", Tests.SkirmishAiOnlyUsesWhatItSees);
run.Test("Skirmish: a live session hash-matches a fresh replay of its own recording", Tests.SkirmishLiveSessionMatchesItsOwnReplay);
run.Test("Skirmish: a live session refuses to be saved (explicit, loud guard)", Tests.SkirmishSessionRefusesToSave);
run.Test("Projectiles: straight shell flight time, impact event and detonation effect", Tests.ProjectileStraightShellFlightTime);
run.Test("Projectiles: ballistic arc path points and flight time", Tests.ProjectileBallisticArc);
run.Test("Projectiles: missile flight time (instant acceleration)", Tests.ProjectileMissileFlightTime);
run.Test("Projectiles: missile ignition delay and acceleration", Tests.ProjectileMissileAcceleration);
run.Test("Projectiles: delayed hit without a projectile object", Tests.ProjectileDelayedHit);
run.Test("Projectiles: scatter radius", Tests.ProjectileScatter);
run.Test("Projectiles: point-defense laser shoots a missile down", Tests.ProjectilePointDefenseLaser);
run.Test("Projectiles: snapshot mid-flight and two-run determinism", Tests.ProjectileSnapshotAndDeterminism);
run.Test("Tunnels: shared network, capacity, entry rules and exit through another tunnel", Tests.TunnelSharedNetwork);
run.Test("Tunnels: healing, hand-over on death and cave-in", Tests.TunnelHealingAndCaveIn);
run.Test("Tunnels: selling the last tunnel lets the riders out", Tests.TunnelSaleEvacuates);
run.Test("Transports: walk in, drive and unload at a position", Tests.TransportLoadAndUnloadAtPosition);
run.Test("Chinook: flight height, supply trips, Supply Lines boost and landing to unload", Tests.ChinookSupplyFlightAndLanding);
run.Test("Chinook: helicopter slow death falls, crashes and leaves a hull", Tests.HelicopterSlowDeathFall);
run.Test("Helix: portable structure rides free, follows and dies with it", Tests.HelixPortableStructure);
run.Test("Transports: snapshot v7, determinism and replay v7 commands", Tests.TransportSnapshotAndReplay);
run.Test("Collision: real CrushDie/SquishCollide footprint overlap", Tests.CollisionCrushAndSquish);
run.Test("Collision: ToppleUpdate push, kill, stump and mid-fall snapshot continuation", Tests.CollisionTopple);
run.Test("Collision: StructureToppleUpdate delayed collapse and reduced crushing sweep", Tests.CollisionStructureTopple);
run.Test("Collision: HeightDieUpdate threshold, delay, structures and ground floor", Tests.CollisionHeightDie);
run.Test("Collision: bounded local avoidance around another unit's footprint", Tests.CollisionLocalAvoidance);
var contentDirectory = args.SkipWhile(value => value != "--content-dir").Skip(1).FirstOrDefault();
if (args.Contains("--long")) run.Test("Long run: 216,000 ticks, fresh runs and snapshot continuation", Tests.LongRun);
if (contentDirectory is not null) run.Test("Real data: instantiate compiled shipped objects", () => Tests.RealData(contentDirectory));
if (contentDirectory is not null) run.Test("Real data: shells, missiles, Chinook supply and tunnels", () => Tests.RealFlightAndTransports(contentDirectory));
if (contentDirectory is not null) run.Test("Real data: retail reference match, determinism and snapshot continuation", () => Tests.RealReferenceMatch(contentDirectory));
return run.Finish();

/// <param name="only">With <c>--only TEXT</c>, just the tests whose name contains TEXT run (a development aid; the full run takes no argument).</param>
sealed class Runner(string? only)
{
    private int _failed;
    private int _passed;

    public void Test(string name, Action test)
    {
        if (only is not null && !name.Contains(only, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            test();
            _passed++;
            Console.WriteLine($"  ok   {name}");
        }
        catch (Exception exception)
        {
            _failed++;
            Console.Error.WriteLine($"  FAIL {name}: {exception.Message}");
            Console.Error.WriteLine(exception.StackTrace);
        }
    }

    public int Finish()
    {
        if (_failed > 0)
        {
            Console.Error.WriteLine($"{_failed} simulation test(s) failed, {_passed} passed.");
            return 1;
        }
        Console.WriteLine("All simulation core tests passed.");
        return 0;
    }
}

static class Check
{
    public static void That(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Throws<T>(Action action, string messagePart) where T : Exception
    {
        try { action(); }
        catch (T exception) when (exception.Message.Contains(messagePart, StringComparison.OrdinalIgnoreCase)) { return; }
        catch (Exception exception) { throw new InvalidOperationException($"Expected {typeof(T).Name} containing '{messagePart}', got {exception.GetType().Name}: {exception.Message}"); }
        throw new InvalidOperationException($"Expected {typeof(T).Name} containing '{messagePart}', but nothing was thrown.");
    }
}

/// <summary>A test module that records what the world does to it, and can be told to act. Not part of the production bindings.</summary>
sealed class ProbeModule(ProbeLog log, ProbeData data) : SimModule, IUpdateModule, IEventReceiver
{
    private long _nextWake = -1;
    private int _updates;

    public override string ModuleName => "ProbeModule";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public override void OnSpawn(IWorldServices world)
    {
        _nextWake = world.Tick + data.FirstWake;
        log.Add($"spawn:{Entity.Id}:{world.Tick}");
    }

    public long Update(IWorldServices world)
    {
        _updates++;
        log.Add($"update:{Entity.Id}:{world.Tick}");
        if (data.DestroyTarget != 0 && world.Tick == data.DestroyAtTick) world.RequestDestroy(new EntityId(data.DestroyTarget));
        if (data.EmitAtTick == world.Tick) world.Emit(SimEventKind.FxRequested, Entity.Id, EntityId.Invalid, "probe", 1);
        return data.Sleep;
    }

    public void OnEvent(IWorldServices world, in SimEvent simEvent) => log.Add($"event:{Entity.Id}:{simEvent.Kind}:{world.Tick}");
    public override void OnRemove(IWorldServices world) => log.Add($"remove:{Entity.Id}:{world.Tick}");

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("updates", _updates);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _updates = (int)reader.Int("updates");
    }
}

sealed record ProbeData(long FirstWake, long Sleep, uint DestroyTarget, long DestroyAtTick, long EmitAtTick) : ModuleData;

sealed class ProbeLog
{
    public List<string> Entries { get; } = [];
    public void Add(string entry) => Entries.Add(entry);
}
