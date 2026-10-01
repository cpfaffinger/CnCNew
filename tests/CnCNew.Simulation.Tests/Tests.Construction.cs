using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

static partial class Tests
{
    private static IReadOnlyDictionary<string, string> ConstructionFiles() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/GameData.ini"] = "GameData\n  SellPercentage = 50%\nEnd\n",
        ["Data/INI/Object/ConstructionFixture.ini"] = """
        Locomotor GroundLoco
          Surfaces = GROUND
          TurnRate = 360.0
          Speed = 60.0
          Acceleration = 60.0
          Braking = 60.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End

        Object Dozer
          KindOf = SELECTABLE VEHICLE DOZER
          VisionRange = 100.0
          ShroudClearingRange = 100.0
          Locomotor = SET_NORMAL GroundLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = DozerAIUpdate AI01
            RepairHealthPercentPerSecond = 10%
          End
        End
        Object Grunt
          KindOf = SELECTABLE INFANTRY
          Locomotor = SET_NORMAL GroundLoco
          BuildCost = 20
          BuildTime = 1
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object Plant
          KindOf = SELECTABLE STRUCTURE
          BuildCost = 100
          BuildTime = 2
          EnergyProduction = 5
          Geometry = BOX
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object Tower
          KindOf = SELECTABLE STRUCTURE
          BuildCost = 50
          BuildTime = 1
          Geometry = BOX
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          Behavior = DestroyDie Destroy01
          End
          Prerequisites
            Object = Plant
          End
        End
        Object Barracks
          KindOf = SELECTABLE STRUCTURE
          BuildCost = 100
          BuildTime = 1
          Geometry = BOX
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = ProductionUpdate Prod01
            MaxQueueEntries = 2
          End
        End
        Object Secret
          KindOf = STRUCTURE
          Buildable = No
          BuildCost = 10
          BuildTime = 1
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
        End
        Object AiOnly
          KindOf = STRUCTURE
          Buildable = Only_By_AI
          BuildCost = 10
          BuildTime = 1
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
        End
        Object Freebie
          KindOf = STRUCTURE
          Buildable = Ignore_Prerequisites
          BuildCost = 10
          BuildTime = 1
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Prerequisites
            Object = Plant
          End
        End
        Object ForeignTrooper
          KindOf = SELECTABLE INFANTRY
          Side = OtherFaction
          Locomotor = SET_NORMAL GroundLoco
          BuildCost = 20
          BuildTime = 1
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object SecretTrooper
          KindOf = SELECTABLE INFANTRY
          Buildable = No
          BuildCost = 20
          BuildTime = 1
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object EliteTrooper
          KindOf = SELECTABLE INFANTRY
          Locomotor = SET_NORMAL GroundLoco
          BuildCost = 30
          BuildTime = 1
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
          Prerequisites
            Object = Plant
          End
        End
        Object HeroTrooper
          KindOf = SELECTABLE INFANTRY
          Locomotor = SET_NORMAL GroundLoco
          BuildCost = 30
          BuildTime = 1
          MaxSimultaneousOfType = 1
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """,
    };

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture, TerrainMap? Terrain) ConstructionSetup(bool withTerrain = false, IEnumerable<(int X, int Y)>? cliffs = null, int money = 1000)
    {
        var fixture = new SyntheticFixture(files: ConstructionFiles());
        var terrain = withTerrain ? NavTerrain(cliffs) : null;
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain?.Fingerprint ?? ""), fixture.Templates, terrain);
        var feeder = new Feeder(world);
        if (money > 0)
        {
            feeder.GiveMoney(1, money);
            feeder.GiveMoney(2, money);
        }
        world.Step();
        return (world, feeder, fixture, terrain);
    }

    private static Entity ConstructionFind(SimulationWorld world, string template, int owner = 1, bool includeSites = true)
        => world.Entities.First(value => value.Template.Name == template && value.Owner.Value == owner && (includeSites || !value.HasStatus(ObjectStatusBits.UnderConstruction)));

    private static bool IsSite(Entity entity) => entity.HasStatus(ObjectStatusBits.UnderConstruction);

    /// <summary>Steps until the predicate holds and returns the number of steps (the limit when it never does).</summary>
    private static int StepUntil(SimulationWorld world, int limit, Func<bool> done)
    {
        for (var tick = 0; tick < limit; tick++)
        {
            world.Step();
            if (done()) return tick + 1;
        }
        return limit;
    }

    public static void ConstructionBuildsAStructure()
    {
        var (world, feeder, _, _) = ConstructionSetup();
        feeder.Spawn(1, "Dozer", x: 0, y: 0);
        world.Step();
        var dozer = world.FindEntity(new EntityId(1))!;
        Check.That(feeder.Construct(1, dozer.Id.Value, "Plant", 60, 0).Accepted, "A dozer may be ordered to build an affordable structure.");
        var money = world.MoneyOf(new PlayerId(1));
        world.Step();
        var site = ConstructionFind(world, "Plant");
        Check.That(IsSite(site) && site.BuilderId == dozer.Id && site.Body!.Health == Fixed.One, "The site exists at once, under construction, with one health point.");
        Check.That(world.MoneyOf(new PlayerId(1)) == money - Fixed.FromInt(100), "The full cost is charged when construction starts.");
        Check.That(world.EnergyProductionOf(new PlayerId(1)) == 0, "A site does not produce energy.");

        var previous = site.Body!.Health;
        var work = 0;
        var completedAt = -1;
        for (var tick = 0; tick < 400 && completedAt < 0; tick++)
        {
            var before = site.ConstructionWork;
            var result = world.Step();
            if (site.ConstructionWork == before + 1) work++;
            Check.That(site.Body.Health >= previous, "Health only rises while building.");
            previous = site.Body.Health;
            if (result.Events.Any(value => value.Kind == SimEventKind.ConstructionCompleted && value.Entity == site.Id)) completedAt = tick;
        }
        Check.That(completedAt >= 0 && work == 60, $"BuildTime 2 s means 60 ticks of builder work (measured {work}).");
        Check.That(!IsSite(site) && site.Body!.Health == site.Body.MaxHealth, "The finished structure has exactly its maximum health.");
        Check.That(world.EnergyProductionOf(new PlayerId(1)) == 5, "Energy production starts on completion.");
        StepUntil(world, 5, () => false);
        Check.That(!world.FindEntity(dozer.Id)!.HasMoveTarget, "The dozer stops once the structure is complete.");
    }

    public static void ConstructionCancelRefunds()
    {
        var (world, feeder, _, _) = ConstructionSetup();
        feeder.Spawn(1, "Dozer", x: 0, y: 0);
        world.Step();
        feeder.Construct(1, 1, "Plant", 60, 0);
        StepUntil(world, 20, () => false);
        var site = ConstructionFind(world, "Plant");
        Check.That(IsSite(site) && world.MoneyOf(new PlayerId(1)) == Fixed.FromInt(900), "The site is building and its cost is spent.");
        Check.That(feeder.CancelConstruct(2, site.Id.Value).Reason == CommandRejection.NotPermitted, "Only the owner cancels a site.");
        Check.That(feeder.CancelConstruct(1, site.Id.Value).Accepted, "The owner may cancel a site.");
        StepUntil(world, 4, () => false);
        Check.That(world.MoneyOf(new PlayerId(1)) == Fixed.FromInt(1000), "Cancelling refunds the full cost.");
        Check.That(world.FindEntity(site.Id) is null && world.EnergyProductionOf(new PlayerId(1)) == 0, "The site is gone and never touched the energy balance.");
        Check.That(feeder.CancelConstruct(1, site.Id.Value).Reason == CommandRejection.UnknownEntity, "A vanished site cannot be cancelled again.");
        StepUntil(world, 10, () => false);
        Check.That(!world.FindEntity(new EntityId(1))!.HasMoveTarget, "The builder drops the task with its site.");
    }

    public static void ConstructionSellTimingAndPayout()
    {
        var (world, feeder, _, _) = ConstructionSetup();
        feeder.Spawn(1, "Plant", x: 50, y: 50);
        feeder.Spawn(1, "Dozer", x: 0, y: 0);
        world.Step();
        var plant = world.FindEntity(new EntityId(1))!;
        Check.That(world.EnergyProductionOf(new PlayerId(1)) == 5, "A spawned complete structure produces energy.");
        Check.That(feeder.Sell(1, 2).Reason == CommandRejection.CannotSell, "Only structures are sold.");
        Check.That(feeder.Sell(2, 1).Reason == CommandRejection.NotPermitted, "Only the owner sells.");
        Check.That(feeder.Sell(1, 1).Accepted, "The owner sells a finished structure.");
        var start = 0L;
        var sold = -1L;
        for (var tick = 0; tick < 300 && sold < 0; tick++)
        {
            var result = world.Step();
            if (tick == 0)
            {
                start = world.Tick;
                Check.That(plant.HasStatus(ObjectStatusBits.Sold), "The structure is marked SOLD when the order executes.");
            }
            if (result.Events.Any(value => value.Kind == SimEventKind.StructureSold && value.Entity == plant.Id && value.Value == Fixed.FromInt(50).Raw)) sold = world.Tick - start;
        }
        Check.That(sold == 179, $"The refund arrives 179 ticks after the order (measured {sold}).");
        Check.That(world.MoneyOf(new PlayerId(1)) == Fixed.FromInt(1050), "SellPercentage 50 % of BuildCost 100 is paid out.");
        world.Step();
        Check.That(world.FindEntity(plant.Id) is null && world.EnergyProductionOf(new PlayerId(1)) == 0, "The sold structure is removed and stops producing energy.");
        Check.That(feeder.Sell(1, 1).Reason == CommandRejection.UnknownEntity, "A sold structure cannot be sold again.");
    }

    public static void ConstructionSellCancelsProduction()
    {
        var (world, feeder, _, _) = ConstructionSetup();
        feeder.Spawn(1, "Barracks", x: 50, y: 50);
        world.Step();
        feeder.QueueProduction(1, 1, "Grunt");
        world.Step();
        Check.That(world.MoneyOf(new PlayerId(1)) == Fixed.FromInt(980), "Queueing charges the unit.");
        feeder.Sell(1, 1);
        world.Step();
        Check.That(world.MoneyOf(new PlayerId(1)) == Fixed.FromInt(1000), "Selling drops the queue and refunds it at once.");
        Check.That(feeder.QueueProduction(1, 1, "Grunt").Reason == CommandRejection.CannotProduce, "A sold producer accepts no new orders.");
        Check.That(feeder.Sell(1, 1).Reason == CommandRejection.CannotSell, "A structure already being sold cannot be sold again.");
    }

    public static void ConstructionRallyPoint()
    {
        var (world, feeder, fixture, _) = ConstructionSetup();
        feeder.Spawn(1, "Barracks", x: 100, y: 100);
        world.Step();
        Check.That(feeder.SetRallyPoint(2, 1, 150, 100).Reason == CommandRejection.NotPermitted, "Only the owner sets a rally point.");
        Check.That(feeder.SetRallyPoint(1, 1, 150, 100).Accepted, "The owner sets a rally point.");
        feeder.QueueProduction(1, 1, "Grunt");
        var spawned = StepUntil(world, 100, () => world.Entities.Any(value => value.Template.Name == "Grunt"));
        Check.That(spawned < 100, "The unit is produced.");
        var grunt = ConstructionFind(world, "Grunt");
        Check.That(grunt.HasMoveTarget && grunt.MoveTargetX == Fixed.FromInt(150) && grunt.MoveTargetY == Fixed.FromInt(100), "A produced unit walks to the rally point.");
        StepUntil(world, 100, () => !grunt.HasMoveTarget);
        Check.That(!grunt.HasMoveTarget && (grunt.X - Fixed.FromInt(150)).Abs() < Fixed.FromInt(2), "It arrives there.");

        var bytes = WorldSnapshot.Write(world);
        var restored = WorldSnapshot.Restore(bytes, fixture.Templates, fixture.Fingerprint);
        var forked = feeder.ForkTo(restored);
        feeder.QueueProduction(1, 1, "Grunt");
        forked.QueueProduction(1, 1, "Grunt");
        for (var tick = 0; tick < 60; tick++) world.Step();
        for (var tick = 0; tick < 60; tick++) restored.Step();
        Check.That(world.StateHash == restored.StateHash, "The rally point survives a snapshot and steers the next unit identically.");
    }

    public static void ConstructionRepair()
    {
        var (world, feeder, _, _) = ConstructionSetup();
        feeder.Spawn(1, "Plant", x: 50, y: 0);
        feeder.Spawn(1, "Dozer", x: 0, y: 0);
        feeder.Spawn(1, "Grunt", x: 0, y: 0);
        world.Step();
        Check.That(feeder.Repair(1, 2, 1).Reason == CommandRejection.CannotConstruct, "An undamaged structure needs no repair.");
        feeder.Damage(1, 200);
        world.Step();
        var plant = world.FindEntity(new EntityId(1))!;
        Check.That(plant.Body!.Health == Fixed.FromInt(300), "The structure is damaged.");
        Check.That(feeder.Repair(1, 3, 1).Reason == CommandRejection.CannotConstruct, "Only builders repair.");
        Check.That(feeder.Repair(2, 2, 1).Reason == CommandRejection.NotPermitted, "Only the owner commands the repair.");
        Check.That(feeder.Repair(1, 2, 3).Reason == CommandRejection.CannotConstruct, "Units are not repaired by dozers.");
        Check.That(feeder.Repair(1, 2, 1).Accepted, "The dozer repairs its damaged structure.");
        var ticks = StepUntil(world, 400, () => plant.Body.Health >= plant.Body.MaxHealth);
        Check.That(plant.Body.Health == plant.Body.MaxHealth && ticks > 100 && ticks < 200, $"10 % of 500 per second heals 200 points in about 120 ticks plus the approach (measured {ticks}).");
        StepUntil(world, 3, () => false);
        Check.That(!world.FindEntity(new EntityId(2))!.HasMoveTarget && feeder.Repair(1, 2, 1).Reason == CommandRejection.CannotConstruct, "The dozer goes idle at full health.");
    }

    public static void ConstructionResumeWithAnotherBuilder()
    {
        var (world, feeder, _, _) = ConstructionSetup();
        feeder.Spawn(1, "Dozer", x: 0, y: 0);
        feeder.Spawn(1, "Dozer", x: 0, y: 20);
        world.Step();
        feeder.Construct(1, 1, "Plant", 60, 0);
        StepUntil(world, 30, () => false);
        var site = ConstructionFind(world, "Plant");
        var workBefore = site.ConstructionWork;
        Check.That(workBefore > 0 && IsSite(site), "The first builder has started working.");
        Check.That(feeder.ResumeConstruct(1, 2, 3).Reason == CommandRejection.UnknownEntity || feeder.ResumeConstruct(1, 2, site.Id.Value).Accepted, "A second dozer takes over the site.");
        world.Step();
        Check.That(site.BuilderId.Value == 2, "The site now belongs to the second builder.");
        var ticks = StepUntil(world, 200, () => !IsSite(site));
        Check.That(!IsSite(site) && site.ConstructionWork == 60, "The structure completes after exactly BuildTime ticks of work, whoever did them.");
                Check.That(feeder.ResumeConstruct(1, 2, site.Id.Value).Reason == CommandRejection.CannotConstruct, "A finished structure cannot be resumed.");
        StepUntil(world, 5, () => false);
        Check.That(!world.FindEntity(new EntityId(1))!.HasMoveTarget && !world.FindEntity(new EntityId(2))!.HasMoveTarget, "Both dozers are idle afterwards.");
    }

    public static void ConstructionRejections()
    {
        var (world, feeder, _, _) = ConstructionSetup();
        feeder.Spawn(1, "Dozer", x: 0, y: 0);
        feeder.Spawn(1, "Grunt", x: 0, y: 0);
        world.Step();
        Check.That(feeder.Construct(2, 1, "Plant", 60, 0).Reason == CommandRejection.NotPermitted, "Nobody commands another player's dozer.");
        Check.That(feeder.Construct(1, 99, "Plant", 60, 0).Reason == CommandRejection.UnknownEntity, "The builder must exist.");
        Check.That(feeder.Construct(1, 2, "Plant", 60, 0).Reason == CommandRejection.CannotConstruct, "Only dozers and workers build.");
        Check.That(feeder.Construct(1, 1, "NoSuchThing", 60, 0).Reason == CommandRejection.UnknownTemplate, "The template must exist.");
        Check.That(feeder.Construct(1, 1, "Grunt", 60, 0).Reason == CommandRejection.CannotConstruct, "Only structures are built by dozers.");
        Check.That(feeder.Construct(1, 1, "Secret", 60, 0).Reason == CommandRejection.CannotConstruct, "Buildable = No cannot be built.");
        Check.That(feeder.Construct(1, 1, "AiOnly", 60, 0).Reason == CommandRejection.CannotConstruct, "Only_By_AI needs a computer player.");
        Check.That(feeder.Construct(1, 1, "Tower", 60, 0).Reason == CommandRejection.MissingPrerequisite, "The tower needs a plant first.");
        Check.That(feeder.Construct(1, 1, "Freebie", 60, 0).Accepted, "Ignore_Prerequisites skips the prerequisite lines.");
        world.Step();

        Check.That(feeder.Construct(1, 1, "Plant", 60, 40).Accepted, "A plant is affordable.");
        world.Step();
        Check.That(feeder.Construct(1, 1, "Tower", 60, 80).Reason == CommandRejection.MissingPrerequisite, "A structure under construction does not satisfy a prerequisite.");
        StepUntil(world, 300, () => !IsSite(ConstructionFind(world, "Plant")));
        Check.That(feeder.Construct(1, 1, "Tower", 60, 80).Accepted, "A finished plant satisfies it.");
        world.Step();

        var (poor, poorFeeder, _, _) = ConstructionSetup(money: 0);
        poorFeeder.Spawn(1, "Dozer", x: 0, y: 0);
        poor.Step();
        Check.That(poorFeeder.Construct(1, 1, "Plant", 60, 0).Reason == CommandRejection.InsufficientFunds, "Without the cost the order is rejected.");

        // CheckConstruct's public dry run (docs/PRESENTATION-INPUT.md §10 point 1) agrees with Submit and has no side effect.
        var moneyBefore = world.MoneyOf(new PlayerId(1));
        var dry = world.CheckConstruct(new PlayerId(1), new ConstructCommand(new PlayerId(1), 999, world.Tick, new EntityId(1), "Tower", Fixed.FromInt(60), Fixed.FromInt(200), Fixed.Zero));
        Check.That(dry == 0 && world.MoneyOf(new PlayerId(1)) == moneyBefore, "The public dry run reports the same outcome Submit would and spends nothing.");
    }

    /// <summary>Phase 10: prerequisites, side/buildable and MaxSimultaneousOfType applied to units queued at a producer, not just structures.</summary>
    public static void ConstructionProductionRejections()
    {
        var (world, feeder, _, _) = ConstructionSetup();
        feeder.Spawn(1, "Barracks", x: 50, y: 50);
        world.Step();
        var barracks = world.Entities.Single(value => value.Template.Name == "Barracks").Id.Value;
        Check.That(feeder.QueueProduction(1, barracks, "Plant").Reason == CommandRejection.CannotProduceTemplate, "A STRUCTURE template cannot be queued at a producer; it must be placed via ConstructCommand.");
        Check.That(feeder.QueueProduction(1, barracks, "SecretTrooper").Reason == CommandRejection.CannotProduceTemplate, "Buildable = No cannot be produced either.");
        Check.That(feeder.QueueProduction(1, barracks, "ForeignTrooper").Reason == CommandRejection.CannotProduceTemplate, "A unit of another side cannot be produced here.");
        Check.That(feeder.QueueProduction(1, barracks, "EliteTrooper").Reason == CommandRejection.MissingPrerequisite, "A unit's own Prerequisites block is enforced exactly like a structure's.");
        Check.That(feeder.QueueProduction(1, barracks, "Grunt").Accepted, "A plain roster unit with no prerequisite is still accepted.");
        world.Step();

        feeder.Spawn(1, "Plant", x: 90, y: 90);
        world.Step();
        Check.That(feeder.QueueProduction(1, barracks, "EliteTrooper").Accepted, "Once the prerequisite structure exists, the unit becomes queueable.");
        // Let the two-slot queue (Grunt, then EliteTrooper) fully drain before testing MaxSimultaneousOfType, so QueueFull never masks it.
        StepUntil(world, 100, () => world.Entities.Count(value => value.Template.Name is "Grunt" or "EliteTrooper") >= 2);

        Check.That(feeder.QueueProduction(1, barracks, "HeroTrooper").Accepted, "The first of a MaxSimultaneousOfType = 1 unit is accepted.");
        var producedAt = StepUntil(world, 60, () => world.Entities.Any(value => value.Template.Name == "HeroTrooper"));
        Check.That(producedAt < 60, "The hero unit is produced.");
        Check.That(feeder.QueueProduction(1, barracks, "HeroTrooper").Reason == CommandRejection.ProductionLimitReached, "A second one is refused while the first is alive.");
        var hero = world.Entities.Single(value => value.Template.Name == "HeroTrooper").Id.Value;
        feeder.Kill(hero);
        world.Step();
        Check.That(feeder.QueueProduction(1, barracks, "HeroTrooper").Accepted, "Once the live one is dead, another may be queued.");

        feeder.Spawn(1, "Barracks", x: 150, y: 50);
        world.Step();
        var secondBarracks = world.Entities.Last(value => value.Template.Name == "Barracks").Id.Value;
        Check.That(feeder.QueueProduction(1, secondBarracks, "HeroTrooper").Reason == CommandRejection.ProductionLimitReached, "The cap counts a unit still queued elsewhere, not just completed ones.");
    }

    public static void ConstructionPlacementRules()
    {
        var (world, feeder, _, _) = ConstructionSetup(withTerrain: true, cliffs: [(15, 15)]);
        feeder.Spawn(1, "Dozer", x: 20, y: 20);
        for (var tick = 0; tick < 6; tick++) world.Step();
        Check.That(feeder.Construct(1, 1, "Plant", 60, 60).Accepted, "A free explored spot is legal.");
        world.Step();
        Check.That(feeder.Construct(1, 1, "Plant", 65, 60).Reason == CommandRejection.InvalidPlacement, "A site blocks its footprint for the next order.");
        Check.That(feeder.Construct(1, 1, "Plant", 180, 180).Reason == CommandRejection.InvalidPlacement, "Unexplored ground is refused.");
        Check.That(feeder.Construct(1, 1, "Plant", 5000, 60).Reason == CommandRejection.InvalidPlacement, "Outside the map is refused.");
        var cliff = feeder.Construct(1, 1, "Plant", 155, 155);
        Check.That(cliff.Reason == CommandRejection.InvalidPlacement, "A cliff cell is refused (or the spot is unexplored).");
        StepUntil(world, 300, () => !IsSite(ConstructionFind(world, "Plant")));
        Check.That(!IsSite(ConstructionFind(world, "Plant")), "On real terrain the dozer walks to the site and finishes it.");
    }

    public static void ConstructionSnapshotAndReplay()
    {
        var (world, feeder, fixture, _) = ConstructionSetup();
        feeder.Spawn(1, "Dozer", x: 0, y: 0);
        feeder.Spawn(1, "Plant", x: 50, y: 100);
        feeder.Spawn(1, "Barracks", x: 100, y: 100);
        world.Step();
        feeder.Construct(1, 1, "Tower", 60, 0);
        feeder.Sell(1, 3);
        StepUntil(world, 30, () => false);
        var site = ConstructionFind(world, "Tower");
        Check.That(IsSite(site) && site.ConstructionWork > 0 && ConstructionFind(world, "Barracks").HasStatus(ObjectStatusBits.Sold), "Snapshot taken mid-construction and mid-sale.");

        var bytes = WorldSnapshot.Write(world);
        var restored = WorldSnapshot.Restore(bytes, fixture.Templates, fixture.Fingerprint);
        Check.That(WorldSnapshot.Write(restored).AsSpan().SequenceEqual(bytes), "The snapshot round-trips byte-identically.");
        Check.That(restored.EnergyProductionOf(new PlayerId(1)) == world.EnergyProductionOf(new PlayerId(1)), "Energy is rebuilt correctly (sites do not count).");
        for (var tick = 0; tick < 260; tick++)
            Check.That(world.Step().StateHash == restored.Step().StateHash, "The restored world builds and sells identically.");
        Check.That(!IsSite(ConstructionFind(world, "Tower")) && world.Entities.All(value => value.Template.Name != "Barracks"), "The construction finished and the sale completed.");
        Check.That(world.MoneyOf(new PlayerId(1)) == restored.MoneyOf(new PlayerId(1)), "Money agrees.");

        // Replay: the same scripted commands run through the replay runner reproduce the live run tick by tick.
        var players = TwoPlayers;
        var commands = ImmutableArray.Create<SimCommand>(
            new SpawnCommand(new PlayerId(1), 1, 0, "Dozer", new PlayerId(1), Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero),
            new SpawnCommand(new PlayerId(1), 2, 0, "Barracks", new PlayerId(1), Fixed.FromInt(100), Fixed.FromInt(100), Fixed.Zero, Fixed.Zero),
            new GiveMoneyCommand(PlayerId.System, 1, 0, new PlayerId(1), Fixed.FromInt(500)),
            new ConstructCommand(new PlayerId(1), 3, 2, new EntityId(1), "Plant", Fixed.FromInt(60), Fixed.Zero, Fixed.Zero),
            new SetRallyPointCommand(new PlayerId(1), 4, 3, new EntityId(2), Fixed.FromInt(120), Fixed.FromInt(100)),
            new QueueProductionCommand(new PlayerId(1), 5, 3, new EntityId(2), "Grunt"),
            new ConstructCommand(new PlayerId(1), 6, 5, new EntityId(1), "Plant", Fixed.FromInt(60), Fixed.FromInt(40), Fixed.Zero),
            new CancelConstructCommand(new PlayerId(1), 7, 20, new EntityId(4)),
            new ResumeConstructCommand(new PlayerId(1), 8, 25, new EntityId(1), new EntityId(3)),
            new SellCommand(new PlayerId(1), 9, 40, new EntityId(2)),
            new RepairCommand(new PlayerId(1), 10, 60, new EntityId(1), new EntityId(3)),
            new ResumeConstructCommand(new PlayerId(1), 11, 70, new EntityId(1), new EntityId(4)));
        commands = [..commands.OrderBy(value => value.Tick).ThenBy(value => value.Player.Value).ThenBy(value => value.Sequence)];
        var header = new ReplayHeader(ReplayFormat.CurrentVersion, CommandTypes.Version, 30, fixture.Fingerprint, SyntheticFixture.FixtureId, 1, new WorldId(1), players, 400, 50);
        var probe = ReplayRunner.Run(new ReplayFile(header, commands, []), fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { RecordTickHashes = true });
        var file = ReplayFormat.Read(ReplayFormat.Write(new ReplayFile(header, commands, probe.Checkpoints)));
        Check.That(file.Commands.SequenceEqual(commands), "All six construction commands survive the replay file.");
        var again = ReplayRunner.Run(file, fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { RecordTickHashes = true });
        Check.That(again.TickHashes.AsSpan().SequenceEqual(probe.TickHashes) && again.FinalHash == probe.FinalHash, "Two replay runs agree tick by tick.");
        var live = ReplayRunner.CreateWorld(header, fixture.Templates);
        ReplayRunner.Run(file, live);
        Check.That(live.Entities.Any(value => value.Template.Name == "Plant" && !IsSite(value)) && live.Entities.All(value => value.Template.Name != "Barracks"),
            "The replayed world built the plant, sold the barracks and rejected the stale resume.");
    }
}
