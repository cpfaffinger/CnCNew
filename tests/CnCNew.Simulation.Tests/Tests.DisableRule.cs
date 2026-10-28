using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>
/// The central disable rule (<c>GameLogic::update</c>, Phase 40, docs/SIMULATION-CORE.md §54): a disabled object runs only the update modules whose
/// <c>getDisabledTypesToProcess</c> shares a type with its disabled mask. A producer processes <c>DISABLED_HELD</c> by default (<c>ProductionUpdateModuleData</c>) and the
/// types its data name: an EMP stops its queue, holding it does not, a script disable does.
/// </summary>
static partial class Tests
{
    public static void DisableRuleStopsTheModulesThatDoNotProcessIt()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: new Dictionary<string, string>(RetailModuleFiles())
        {
            ["Data/INI/Object/DisableRule.ini"] = """
            Object PoweredFactory
              KindOf = STRUCTURE
              Body = StructureBody Body01
                MaxHealth = 500.0
                InitialHealth = 500.0
              End
              Behavior = DestroyDie Destroy01
              End
              Behavior = ProductionUpdate Prod01
                MaxQueueEntries = 3
                DisabledTypesToProcess = DISABLED_HELD DISABLED_EMP
              End
              Behavior = DefaultProductionExitUpdate Exit01
                UnitCreatePoint = X:10.0 Y:-4.0 Z:0.0
                NaturalRallyPoint = X:20.0 Y:0.0 Z:0.0
              End
            End
            """,
        });
        // QuickUnit takes one second; each case runs three seconds.
        int Produced(string factory, System.Action<SimulationWorld, Entity> disable)
        {
            var world = NewWorld(fixture);
            var feeder = new Feeder(world);
            feeder.Spawn(1, factory, x: 300, y: 300);
            world.Step();
            feeder.GiveMoney(1, 1000);
            world.Step();
            var producer = world.Entities.Single(entity => entity.Template.Name == factory);
            disable(world, producer);
            feeder.QueueProduction(1, producer.Id.Value, "QuickUnit");
            for (var tick = 0; tick < world.Frames(90); tick++) world.Step();
            return CountOf(world, "QuickUnit");
        }

        Check.That(Produced("DefaultExitFactory", (_, _) => { }) == 1, "Setup: a factory makes its unit in three seconds.");
        Check.That(Produced("DefaultExitFactory", (world, factory) => world.DisableByEmpUntil(factory, world.Tick + world.Frames(300))) == 0,
            "An EMP stops the queue of a factory: its ProductionUpdate processes only DISABLED_HELD.");
        Check.That(Produced("DefaultExitFactory", (world, factory) => world.SetScriptDisable(factory, Entity.ScriptHeld, true)) == 1,
            "A held factory goes on producing (ProductionUpdate's default DisabledTypesToProcess is DISABLED_HELD).");
        Check.That(Produced("DefaultExitFactory", (world, factory) => world.SetScriptDisable(factory, Entity.ScriptDisabled, true)) == 0,
            "A script-disabled factory makes nothing.");
        Check.That(Produced("PoweredFactory", (world, factory) => world.DisableByEmpUntil(factory, world.Tick + world.Frames(300))) == 1,
            "A factory whose data name DISABLED_EMP produces through an EMP.");
        Check.That(Produced("DefaultExitFactory", (world, factory) => world.DisableByEmpUntil(factory, world.Tick + world.Frames(15))) == 1,
            "After a short EMP the queue goes on.");
    }

    /// <summary>
    /// <c>Object::checkDisabledStatus</c> runs after the updates of a frame and clears a disable whose frame has come: a factory disabled before its update in frame f "until"
    /// f + 10 skips the updates of frames f to f + 10 - eleven frames - and its unit comes eleven frames late. The port let it go one frame early.
    /// </summary>
    public static void DisableHoldsThroughItsLastFrame()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: RetailModuleFiles());
        var frame = 0L;
        long Completed(int disableFrames)
        {
            var world = NewWorld(fixture);
            frame = world.Frames(1);
            var feeder = new Feeder(world);
            feeder.Spawn(1, "DefaultExitFactory", x: 300, y: 300);
            world.Step();
            feeder.GiveMoney(1, 1000);
            world.Step();
            var factory = world.Entities.Single(entity => entity.Template.Name == "DefaultExitFactory");
            while (world.Tick % world.Frames(1) != 0) world.Step();
            feeder.QueueProduction(1, factory.Id.Value, "QuickUnit");
            world.Step();
            while (world.Tick % world.Frames(1) != 0) world.Step();
            var start = world.Tick;
            if (disableFrames > 0) world.DisableByEmpUntil(factory, world.Tick + world.Frames(disableFrames));
            while (CountOf(world, "QuickUnit") == 0 && world.Tick < start + world.Frames(200)) world.Step();
            return world.Tick - start;
        }

        var free = Completed(0);
        var disabled = Completed(10);
        Check.That(disabled - free == 11 * frame, $"A disable until ten frames on holds eleven frames of updates ({free} against {disabled} ticks).");
    }

    /// <summary>
    /// <c>AIUpdate::update</c> steps its turrets (<c>updateTurretAI</c>) only while the object is not paralyzed, unmanned, EMP'd, subdued or hacked - and not at all while the AI is
    /// skipped: an EMP'd tank's turret stands where it was and turns on when the disable ends.
    /// </summary>
    public static void DisabledTurretStands()
    {
        var (world, feeder, _) = TurretWorld(("TurretTank", 1, 0, 0, 0m), ("TurretTarget", 2, 0, 100, 0m));
        var tank = Slotted(world, "TurretTank");
        feeder.FireWeapon(1, tank.Id.Value, IdOf(world, "TurretTarget"));
        for (var tick = 0; tick < world.Frames(3); tick++) world.Step();
        var turning = tank.TurretOf(0)!.Value.Angle;
        Check.That(turning != CnCNew.Simulation.Numerics.Fixed.Zero, "Setup: the turret turns towards the target.");
        world.DisableByEmpUntil(tank, world.Tick + world.Frames(10));
        for (var tick = 0; tick < world.Frames(10); tick++) world.Step();
        Check.That(tank.TurretOf(0)!.Value.Angle == turning, $"An EMP'd tank's turret stands ({turning.Raw} -> {tank.TurretOf(0)!.Value.Angle.Raw} raw).");
        for (var tick = 0; tick < world.Frames(5); tick++) world.Step();
        Check.That(tank.TurretOf(0)!.Value.Angle != turning, "It turns on when the disable is over.");
    }

    /// <summary>
    /// <c>BattlePlanUpdate</c>'s <c>paralyzeTroop</c> disables the object (<c>DISABLED_PARALYZED</c>), not only its AI: the turret stands with the rest of what does not process it,
    /// and the unit acts again when the paralysis is over. The port paralyzed the AI module alone, and the turret went on turning.
    /// </summary>
    public static void ParalysisDisablesTheObject()
    {
        var (world, feeder, _) = TurretWorld(("TurretTank", 1, 0, 0, 0m), ("TurretTarget", 2, 0, 100, 0m));
        var tank = Slotted(world, "TurretTank");
        feeder.FireWeapon(1, tank.Id.Value, IdOf(world, "TurretTarget"));
        for (var tick = 0; tick < world.Frames(3); tick++) world.Step();
        var turning = tank.TurretOf(0)!.Value.Angle;
        world.ParalyzeTroops(tank.Owner, ["VEHICLE"], [], world.Tick + world.Frames(10));
        Check.That((tank.DisabledFlags(world.Tick) & DisabledMask.Paralyzed) != 0, "The paralyzed tank carries DISABLED_PARALYZED.");
        for (var tick = 0; tick < world.Frames(10); tick++) world.Step();
        Check.That(tank.TurretOf(0)!.Value.Angle == turning, $"A paralyzed tank's turret stands ({turning.Raw} -> {tank.TurretOf(0)!.Value.Angle.Raw} raw).");
        for (var tick = 0; tick < world.Frames(5); tick++) world.Step();
        Check.That(tank.DisabledFlags(world.Tick) == DisabledMask.None && tank.TurretOf(0)!.Value.Angle != turning, "It turns on when the paralysis is over.");
    }

    /// <summary>
    /// A transport holds its riders (<c>TransportContain::onContaining</c> sets <c>DISABLED_HELD</c>), so a rider's update modules that do not process it stand while it rides - a
    /// lifetime does not run out inside; a <c>HealContain</c> is an <c>OpenContain</c> and holds nobody. The port ran every rider's modules.
    /// </summary>
    public static void TransportHoldsItsRiders()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: new Dictionary<string, string>(RetailModuleFiles())
        {
            ["Data/INI/Object/HeldRiders.ini"] = """
            Object HoldingTruck
              KindOf = SELECTABLE VEHICLE
              Body = ActiveBody Body01
                MaxHealth = 300.0
                InitialHealth = 300.0
              End
              Behavior = DestroyDie Destroy01
              End
              Behavior = AIUpdateInterface AI01
              End
              Behavior = TransportContain Transport01
                Slots = 3
                AllowInsideKindOf = INFANTRY
              End
            End
            Object ShortLivedRider
              KindOf = SELECTABLE INFANTRY
              TransportSlotCount = 1
              Body = ActiveBody Body01
                MaxHealth = 50.0
                InitialHealth = 50.0
              End
              Behavior = DestroyDie Destroy01
              End
              Behavior = LifetimeUpdate Life01
                MinLifetime = 1000
                MaxLifetime = 1000
              End
            End
            """,
        });
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "HoldingTruck", x: 100, y: 100);
        feeder.Spawn(1, "Barracks", x: 300, y: 300);
        feeder.Spawn(1, "ShortLivedRider", x: 100, y: 100);
        feeder.Spawn(1, "ShortLivedRider", x: 300, y: 300);
        world.Step();
        var truck = world.Entities.Single(entity => entity.Template.Name == "HoldingTruck");
        var barracks = world.Entities.Single(entity => entity.Template.Name == "Barracks");
        var riders = world.Entities.Where(entity => entity.Template.Name == "ShortLivedRider").OrderBy(entity => entity.Id.Value).ToArray();
        Check.That(world.TryEnterContainer(riders[0], truck) && world.TryEnterContainer(riders[1], barracks), "Setup: one rider boards the truck, one the barracks.");
        Check.That(world.DisabledFlagsOf(riders[0]) == DisabledMask.Held && world.DisabledFlagsOf(riders[1]) == DisabledMask.None, "The transport holds its rider; the barracks does not.");
        for (var tick = 0; tick < world.Frames(60); tick++) world.Step();
        Check.That(world.FindEntity(riders[0].Id) is { EffectivelyDead: false }, "A held rider's lifetime does not run out while it rides.");
        Check.That(world.FindEntity(riders[1].Id) is null or { EffectivelyDead: true }, "The barracks' occupant lives its second.");
    }

    /// <summary>
    /// F-16: <c>Energy</c> keeps production and consumption apart and every change of either calls <c>Player::onPowerBrownOutChange</c>: in a brown-out every <c>POWERED</c> object
    /// of the player carries <c>DISABLED_UNDERPOWERED</c> (a powered factory makes nothing - its <c>ProductionUpdate</c> processes only held), a new power plant ends it, and a
    /// sabotaged power plant starts it again until the sabotage is over (<c>Player::update</c>). The port kept a net sum and disabled nothing.
    /// </summary>
    public static void BrownOutDisablesPoweredObjects()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: new Dictionary<string, string>(RetailModuleFiles())
        {
            ["Data/INI/Object/BrownOut.ini"] = """
            Object HungryFactory
              KindOf = STRUCTURE POWERED
              EnergyProduction = -5
              Body = StructureBody Body01
                MaxHealth = 500.0
                InitialHealth = 500.0
              End
              Behavior = DestroyDie Destroy01
              End
              Behavior = ProductionUpdate Prod01
                MaxQueueEntries = 3
              End
              Behavior = DefaultProductionExitUpdate Exit01
                UnitCreatePoint = X:10.0 Y:-4.0 Z:0.0
                NaturalRallyPoint = X:20.0 Y:0.0 Z:0.0
              End
            End
            Object SmallPlant
              KindOf = STRUCTURE
              EnergyProduction = 10
              Body = StructureBody Body01
                MaxHealth = 500.0
                InitialHealth = 500.0
              End
              Behavior = DestroyDie Destroy01
              End
            End
            """,
        });
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        var player = new PlayerId(1);
        feeder.Spawn(1, "HungryFactory", x: 300, y: 300);
        world.Step();
        feeder.GiveMoney(1, 1000);
        world.Step();
        var factory = world.Entities.Single(entity => entity.Template.Name == "HungryFactory");
        Check.That(!world.HasSufficientPower(player) && world.ProducedEnergyOf(player) == 0 && world.EnergyConsumptionOf(player) == 5, "Setup: the factory consumes 5 and nothing produces.");
        Check.That((world.DisabledFlagsOf(factory) & DisabledMask.Underpowered) != 0, "A powered object of a player in a brown-out is underpowered.");
        feeder.QueueProduction(1, factory.Id.Value, "QuickUnit");
        for (var tick = 0; tick < world.Frames(60); tick++) world.Step();
        Check.That(CountOf(world, "QuickUnit") == 0, "The underpowered factory makes nothing.");

        feeder.Spawn(1, "SmallPlant", x: 100, y: 100);
        world.Step();
        Check.That(world.HasSufficientPower(player) && world.DisabledFlagsOf(factory) == DisabledMask.None, "A power plant ends the brown-out.");
        for (var tick = 0; tick < world.Frames(40); tick++) world.Step();
        Check.That(CountOf(world, "QuickUnit") == 1, "The factory produces again.");

        world.SetPowerSabotagedUntil(player, world.Tick + world.Frames(30));
        Check.That(!world.HasSufficientPower(player) && world.ProducedEnergyOf(player) == 0 && world.DisabledFlagsOf(factory) == DisabledMask.Underpowered,
            "A sabotaged power plant starts the brown-out at once.");
        for (var tick = 0; tick < world.Frames(30); tick++) world.Step();
        Check.That(world.DisabledFlagsOf(factory) == DisabledMask.Underpowered, "It lasts to the end of the sabotage.");
        for (var tick = 0; tick < world.Frames(2); tick++) world.Step();
        Check.That(world.HasSufficientPower(player) && world.DisabledFlagsOf(factory) == DisabledMask.None, "Player::update ends it the frame after.");
    }

    /// <summary>
    /// F-16: <c>ThingTemplate::calcTimeToBuild</c> divides a unit's build time by the production speed of the player's supply ratio (<c>GameData</c>: penalty modifier 1.0,
    /// minimum 0.5, maximum below full supply 0.8), and <c>ProductionUpdate::update</c> compares the frames under construction with it every frame. A player without production
    /// and consumption has a ratio of 0 - half speed -, production alone is full speed, half the supply is half speed, 10 of 11 is the maximum 0.8 (a 30-frame unit takes 37). The
    /// port built at full speed always.
    /// </summary>
    public static void LowPowerSlowsProduction()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: new Dictionary<string, string>(RetailModuleFiles())
        {
            ["Data/INI/GameData.ini"] = "GameData\n  MinLowEnergyProductionSpeed = 0.5\n  MaxLowEnergyProductionSpeed = 0.8\n  LowEnergyPenaltyModifier = 1.0\nEnd\n",
            ["Data/INI/Object/LowPower.ini"] = """
            Object TenPlant
              KindOf = STRUCTURE
              EnergyProduction = 10
              Body = StructureBody Body01
                MaxHealth = 500.0
                InitialHealth = 500.0
              End
            End
            Object TwentyConsumer
              KindOf = STRUCTURE
              EnergyProduction = -20
              Body = StructureBody Body01
                MaxHealth = 500.0
                InitialHealth = 500.0
              End
            End
            Object ElevenConsumer
              KindOf = STRUCTURE
              EnergyProduction = -11
              Body = StructureBody Body01
                MaxHealth = 500.0
                InitialHealth = 500.0
              End
            End
            """,
        });
        var frame = 0L;
        long Built(params string[] power)
        {
            var world = NewWorld(fixture);
            var feeder = new Feeder(world);
            frame = world.Frames(1);
            feeder.Spawn(1, "DefaultExitFactory", x: 300, y: 300);
            foreach (var building in power) feeder.Spawn(1, building, x: 100, y: 100);
            world.Step();
            feeder.GiveMoney(1, 1000);
            world.Step();
            var factory = world.Entities.Single(entity => entity.Template.Name == "DefaultExitFactory");
            var start = world.Tick;
            feeder.QueueProduction(1, factory.Id.Value, "QuickUnit");
            while (CountOf(world, "QuickUnit") == 0 && world.Tick < start + world.Frames(300)) world.Step();
            return world.Tick - start;
        }

        var full = Built("TenPlant");
        Check.That(Built() - full == 30 * frame, $"Without power at all a 30-frame unit takes 60 frames ({Built()} against {full} ticks).");
        Check.That(Built("TenPlant", "TwentyConsumer") - full == 30 * frame, "With half the supply it takes 60 frames.");
        Check.That(Built("TenPlant", "ElevenConsumer") - full == 7 * frame, "With 10 of 11 it takes 37 frames (the maximum speed 0.8 below full supply, truncated).");
    }

    /// <summary>
    /// F-16: <c>DozerAIUpdate</c> adds <c>100 / calcTimeToBuild</c> to the construction percent in every frame of work, so a structure builds under the player's power as a unit
    /// does: a player without production and consumption builds a two-second plant in 120 frames of work instead of 60. The port built at full speed always.
    /// </summary>
    public static void LowPowerSlowsConstruction()
    {
        var files = new Dictionary<string, string>(ConstructionFiles())
        {
            ["Data/INI/GameData.ini"] = "GameData\n  SellPercentage = 50%\n  MinLowEnergyProductionSpeed = 0.5\n  MaxLowEnergyProductionSpeed = 0.8\n  LowEnergyPenaltyModifier = 1.0\nEnd\n",
        };
        var fixture = new SyntheticFixture(TestRate.Default, files: files);
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, ""), fixture.Templates);
        var feeder = new Feeder(world);
        feeder.GiveMoney(1, 1000);
        world.Step();
        feeder.Spawn(1, "Dozer", x: 0, y: 0);
        world.Step();
        var dozer = world.Entities.Single(entity => entity.Template.Name == "Dozer");
        Check.That(feeder.Construct(1, dozer.Id.Value, "Plant", 60, 0).Accepted, "Setup: the dozer is ordered to build the plant.");
        world.Step();
        var site = world.Entities.Single(entity => entity.Template.Name == "Plant");
        var work = 0;
        var done = false;
        for (var tick = 0; tick < world.Frames(400) && !done; tick++)
        {
            var before = site.ConstructionWork;
            world.Step();
            if (site.ConstructionWork > before) work++;
            done = !site.HasStatus(ObjectStatusBits.UnderConstruction);
        }
        Check.That(done && work == world.Frames(119) + 1, $"Without power the two-second plant takes 120 frames of work, the first step counting a whole frame ({work} steps).");
        Check.That(site.Body!.Health == site.Body.MaxHealth, "The finished structure has exactly its maximum health.");
    }

    /// <summary>
    /// The rule fix <c>no-power-edge-for-unfinished-plant</c> (<c>Object::onDisabledEdge</c>): an EMP on a power plant that is still being built takes 5 from its player's production under the
    /// original rules - it produces nothing yet - and nothing with the fix; either way the finished plant produces 5. At 30, 60 and 120 Hz.
    /// </summary>
    public static void GameFixNoPowerEdgeForUnfinishedPlant()
    {
        foreach (var rate in new[] { CnCNew.Simulation.Time.SimulationRate.Hz30, CnCNew.Simulation.Time.SimulationRate.Hz60, CnCNew.Simulation.Time.SimulationRate.Hz120 })
        {
            (int WhileDisabled, int Finished) Run(CnCNew.Simulation.Rules.RuleProfile rules)
            {
                var fixture = new SyntheticFixture(rate, files: ConstructionFiles(), rules: rules);
                var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, ""), fixture.Templates);
                var feeder = new Feeder(world);
                feeder.GiveMoney(1, 1000);
                world.Step();
                feeder.Spawn(1, "Dozer", x: 0, y: 0);
                world.Step();
                var dozer = world.Entities.Single(entity => entity.Template.Name == "Dozer");
                feeder.Construct(1, dozer.Id.Value, "Plant", 60, 0);
                for (var step = 0; step < world.Frames(10); step++) world.Step();
                var site = world.Entities.Single(entity => entity.Template.Name == "Plant");
                Check.That(site.HasStatus(ObjectStatusBits.UnderConstruction), $"{rate}: setup - the plant is being built.");
                world.DisableByEmpUntil(site, world.Tick + world.Frames(5));
                world.Step();
                var whileDisabled = world.ProducedEnergyOf(new PlayerId(1));
                for (var step = 0; step < world.Frames(300) && site.HasStatus(ObjectStatusBits.UnderConstruction); step++) world.Step();
                for (var step = 0; step < world.Frames(10); step++) world.Step();
                return (whileDisabled, world.ProducedEnergyOf(new PlayerId(1)));
            }

            var original = Run(CnCNew.Simulation.Rules.RuleProfile.Original);
            var fixedRun = Run(CnCNew.Simulation.Rules.RuleProfile.Parse("no-power-edge-for-unfinished-plant"));
            Check.That(original.WhileDisabled == -5 && fixedRun.WhileDisabled == 0, $"{rate}: the EMP takes 5 from the unfinished plant's player under the original rules, nothing with the fix ({original.WhileDisabled}, {fixedRun.WhileDisabled}).");
            Check.That(original.Finished == 5 && fixedRun.Finished == 5, $"{rate}: the finished plant produces 5 either way ({original.Finished}, {fixedRun.Finished}).");
        }
    }

    /// <summary>
    /// F-56: <c>Object::onDisabledEdge</c> takes a power plant's production out of its player's energy when the plant becomes disabled and gives it back when its last disable
    /// ends - an EMP'd plant browns out the base it powers. A disabled plant that is destroyed does not take its production out a second time
    /// (<c>friend_adjustPowerForPlayer</c>). The port's plant kept producing under an EMP.
    /// </summary>
    public static void DisabledPowerPlantStopsProducing()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: new Dictionary<string, string>(RetailModuleFiles())
        {
            ["Data/INI/Object/EdgePower.ini"] = """
            Object EdgePlant
              KindOf = STRUCTURE
              EnergyProduction = 10
              Body = StructureBody Body01
                MaxHealth = 500.0
                InitialHealth = 500.0
              End
              Behavior = DestroyDie Destroy01
              End
            End
            Object EdgeConsumer
              KindOf = STRUCTURE POWERED
              EnergyProduction = -5
              Body = StructureBody Body01
                MaxHealth = 500.0
                InitialHealth = 500.0
              End
            End
            """,
        });
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        var player = new PlayerId(1);
        feeder.Spawn(1, "EdgePlant", x: 100, y: 100);
        feeder.Spawn(1, "EdgeConsumer", x: 300, y: 300);
        world.Step();
        var plant = world.Entities.Single(entity => entity.Template.Name == "EdgePlant");
        var consumer = world.Entities.Single(entity => entity.Template.Name == "EdgeConsumer");
        Check.That(world.ProducedEnergyOf(player) == 10 && world.HasSufficientPower(player) && !consumer.Underpowered, "Setup: the plant powers the consumer.");
        world.DisableByEmpUntil(plant, world.Tick + world.Frames(20));
        world.Step();
        Check.That(world.ProducedEnergyOf(player) == 0 && !world.HasSufficientPower(player) && consumer.Underpowered, "The EMP'd plant produces nothing: the base browns out.");
        for (var tick = 0; tick < world.Frames(22); tick++) world.Step();
        Check.That(world.ProducedEnergyOf(player) == 10 && world.HasSufficientPower(player) && !consumer.Underpowered, "When the EMP is over the plant produces again.");
        world.DisableByEmpUntil(plant, world.Tick + world.Frames(20));
        world.Step();
        world.Kill(plant.Id);
        for (var tick = 0; tick < world.Frames(2); tick++) world.Step();
        Check.That(world.ProducedEnergyOf(player) == 0 && world.EnergyConsumptionOf(player) == 5, $"A disabled plant that dies takes nothing out a second time ({world.ProducedEnergyOf(player)}).");
    }

    /// <summary>
    /// <c>Object::setDisabledUntil</c> pauses the object's special powers for every disabled type but held (<c>pauseAllSpecialPowers</c>), and <c>clearDisabled</c> resumes them: a
    /// superweapon EMP'd for twenty frames is ready twenty frames later. The port's powers counted down through every disable.
    /// </summary>
    public static void DisablePausesSpecialPowers()
    {
        long ReadyAfter(bool emp)
        {
            var fixture = new SyntheticFixture(TestRate.Default, files: CratesAndSpecialsFiles());
            var world = NewWorld(fixture);
            var feeder = new Feeder(world);
            feeder.Spawn(1, "SabSuperweapon", x: 200, y: 100);
            world.Step();
            while (world.Tick % world.Frames(1) != 0) world.Step();
            var building = world.Entities.Single(entity => entity.Template.Name == "SabSuperweapon");
            var power = building.Modules.OfType<ISpecialPowerModule>().Single();
            var start = world.Tick;
            if (emp) world.DisableByEmpUntil(building, world.Tick + world.Frames(20));
            while (!power.IsReady(world) && world.Tick < start + world.Frames(300)) world.Step();
            return world.Tick - start;
        }

        var free = ReadyAfter(false);
        var disabled = ReadyAfter(true);
        Check.That(disabled - free == TestRate.Default.Time.OriginalFrames(20), $"An EMP of twenty frames delays the power by twenty frames ({free} against {disabled} ticks).");
    }

    /// <summary>
    /// F-50: a player's radar (<c>Player::addRadar</c>, <c>hasRadar</c>): the radar upgrade gives it, a brown-out switches it off unless one of the player's radars is
    /// <c>DisableProof</c> (the GLA's), the building's disabled edge takes it away and gives it back, its death takes it away. The port had no radar per player.
    /// </summary>
    public static void PlayerRadar()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: new Dictionary<string, string>(RetailModuleFiles())
        {
            ["Data/INI/Object/Radar.ini"] = """
            Upgrade Upgrade_TestRadar
              Type = OBJECT
              BuildCost = 0
              BuildTime = 0
            End
            Upgrade Upgrade_TestProofRadar
              Type = OBJECT
              BuildCost = 0
              BuildTime = 0
            End
            Object RadarBuilding
              KindOf = STRUCTURE
              Body = StructureBody Body01
                MaxHealth = 500.0
                InitialHealth = 500.0
              End
              Behavior = DestroyDie Destroy01
              End
              Behavior = RadarUpgrade Radar01
                TriggeredBy = Upgrade_TestRadar
              End
            End
            Object ProofRadarBuilding
              KindOf = STRUCTURE
              Body = StructureBody Body01
                MaxHealth = 500.0
                InitialHealth = 500.0
              End
              Behavior = DestroyDie Destroy01
              End
              Behavior = RadarUpgrade Radar01
                TriggeredBy = Upgrade_TestProofRadar
                DisableProof = Yes
              End
            End
            Object RadarConsumer
              KindOf = STRUCTURE
              EnergyProduction = -5
              Body = StructureBody Body01
                MaxHealth = 500.0
                InitialHealth = 500.0
              End
              Behavior = DestroyDie Destroy01
              End
            End
            """,
        });
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        var player = new PlayerId(1);
        feeder.Spawn(1, "RadarBuilding", x: 100, y: 100);
        world.Step();
        var building = world.Entities.Single(entity => entity.Template.Name == "RadarBuilding");
        Check.That(!world.HasRadar(player), "Setup: no radar before the upgrade.");
        feeder.GrantUpgrade(1, "Upgrade_TestRadar", building.Id.Value);
        world.Step();
        Check.That(world.HasRadar(player), "The radar upgrade gives the player a radar.");

        world.DisableByEmpUntil(building, world.Tick + world.Frames(10));
        world.Step();
        Check.That(!world.HasRadar(player), "An EMP'd radar building takes the radar away (its disabled edge).");
        for (var tick = 0; tick < world.Frames(12); tick++) world.Step();
        Check.That(world.HasRadar(player), "It gives it back when the EMP is over.");

        feeder.Spawn(1, "RadarConsumer", x: 300, y: 300);
        world.Step();
        Check.That(!world.HasRadar(player), "A brown-out switches the radar off.");
        feeder.Spawn(1, "ProofRadarBuilding", x: 500, y: 500);
        world.Step();
        var proof = world.Entities.Single(entity => entity.Template.Name == "ProofRadarBuilding");
        feeder.GrantUpgrade(1, "Upgrade_TestProofRadar", proof.Id.Value);
        world.Step();
        Check.That(world.HasRadar(player), "A disable-proof radar keeps it on in a brown-out.");

        world.Kill(proof.Id);
        world.Kill(building.Id);
        for (var tick = 0; tick < world.Frames(2); tick++) world.Step();
        Check.That(!world.HasRadar(player), "Their deaths take the radar away.");
    }
}
