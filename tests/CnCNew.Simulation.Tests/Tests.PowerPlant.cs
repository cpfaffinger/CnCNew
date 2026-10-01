using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

static partial class Tests
{
    private static IReadOnlyDictionary<string, string> PowerPlantFiles() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/PowerPlantFixture.ini"] = """
        Upgrade Upgrade_Rods
          Type = OBJECT
          BuildCost = 10
          BuildTime = 1
        End
        Object RodStation
          KindOf = STRUCTURE
          EnergyProduction = 5
          EnergyBonus = 3
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = PowerPlantUpdate Plant01
            RodsExtendTime = 1000
          End
          Behavior = PowerPlantUpgrade PlantUp01
            TriggeredBy = Upgrade_Rods
          End
        End
        Object OverStation
          KindOf = STRUCTURE
          EnergyProduction = 5
          EnergyBonus = 4
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = PowerPlantUpdate Plant01
            RodsExtendTime = 1000
          End
          Behavior = OverchargeBehavior Over01
            HealthPercentToDrainPerSecond = 30%
            NotAllowedWhenHealthBelowPercent = 90%
          End
        End
        Locomotor CaptureLoco
          Surfaces = GROUND
          TurnRate = 360.0
          Speed = 60.0
          Acceleration = 60.0
          Braking = 60.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End
        Object Capturer
          KindOf = SELECTABLE INFANTRY
          Locomotor = SET_NORMAL CaptureLoco
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object CapturableStation
          KindOf = STRUCTURE CAPTURABLE
          EnergyProduction = 5
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object PlainStation
          KindOf = STRUCTURE
          EnergyProduction = 5
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    public static void PowerPlantUpgradeAddsAndRemovesTheBonus()
    {
        var fixture = new SyntheticFixture(files: PowerPlantFiles());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        var owner = new PlayerId(1);
        feeder.Spawn(1, "RodStation", owner: 1, x: 50, y: 50);
        world.Step();
        Check.That(world.EnergyProductionOf(owner) == 5, "A finished plant produces its base energy.");
        var plant = world.FindEntity(new EntityId(1))!.Modules.OfType<PowerPlantUpdate>().Single();
        Check.That(!plant.Extended, "The rods start retracted.");

        feeder.GiveMoney(1, 100);
        world.Step();
        feeder.PurchaseUpgrade(1, "Upgrade_Rods", target: 1);
        world.Step();
        world.Step();
        Check.That(world.EnergyProductionOf(owner) == 8, "PowerPlantUpgrade adds EnergyBonus (3) to the owner.");
        Check.That(plant.Extended && !plant.IsExtendComplete(world.Tick), "The upgrade extends the rods; the animation timer is still running.");
        for (var i = 0; i < 40; i++) world.Step();
        Check.That(plant.IsExtendComplete(world.Tick), "The extension completes after RodsExtendTime.");

        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
        Check.That(restored.StateHash == world.StateHash && restored.EnergyProductionOf(owner) == 8, "The bonus survives a snapshot round trip without being counted twice.");

        feeder.Destroy(1);
        world.Step();
        world.Step();
        Check.That(world.FindEntity(new EntityId(1)) is null && world.EnergyProductionOf(owner) == 0, "Removing the plant takes base production and bonus away.");
    }

    public static void OverchargeTogglesDrainsAndSwitchesItselfOff()
    {
        var fixture = new SyntheticFixture(files: PowerPlantFiles());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        var owner = new PlayerId(1);
        feeder.Spawn(1, "OverStation", owner: 1, x: 50, y: 50);
        feeder.Spawn(1, "OverStation", owner: 1, x: 90, y: 50);
        world.Step();
        var first = world.FindEntity(new EntityId(1))!;
        var overcharge = first.Modules.OfType<OverchargeBehavior>().Single();
        var plant = first.Modules.OfType<PowerPlantUpdate>().Single();
        Check.That(world.EnergyProductionOf(owner) == 10 && !overcharge.Active, "Two idle plants produce 5 each.");

        overcharge.Toggle(world);
        Check.That(overcharge.Active && plant.Extended && world.EnergyProductionOf(owner) == 14, "Enabling adds the bonus (4) and extends the rods.");
        overcharge.Enable(world, true);
        Check.That(world.EnergyProductionOf(owner) == 14, "Enabling twice does not add the bonus twice.");

        world.Step();
        world.Step();
        Check.That(first.Body!.Health < first.Body.MaxHealth, "While enabled the plant drains health every tick.");
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
        Check.That(restored.StateHash == world.StateHash && restored.FindEntity(new EntityId(1))!.Modules.OfType<OverchargeBehavior>().Single().Active, "The active state round-trips.");

        for (var i = 0; i < 40; i++) world.Step();
        Check.That(!overcharge.Active && !plant.Extended && world.EnergyProductionOf(owner) == 10,
            "Below NotAllowedWhenHealthBelowPercent the plant switches itself off and the bonus is withdrawn.");
        var health = first.Body.Health;
        for (var i = 0; i < 10; i++) world.Step();
        Check.That(first.Body.Health == health, "A switched-off plant stops draining.");

        var second = world.FindEntity(new EntityId(2))!.Modules.OfType<OverchargeBehavior>().Single();
        second.Enable(world, true);
        Check.That(world.EnergyProductionOf(owner) == 14, "The second plant can be enabled independently.");
        feeder.Destroy(2);
        world.Step();
        world.Step();
        Check.That(world.FindEntity(new EntityId(2)) is null && world.EnergyProductionOf(owner) == 5, "Removing an overcharged plant takes its production and its bonus away.");
    }

    /// <summary>Phase 10: the TOGGLE_OVERCHARGE command (CommandButton.ini) reaches OverchargeBehavior.Toggle, which was reachable from code only.</summary>
    public static void OverchargeCommandTogglesTheModule()
    {
        var fixture = new SyntheticFixture(files: PowerPlantFiles());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "OverStation", owner: 1, x: 50, y: 50);
        feeder.Spawn(1, "RodStation", owner: 1, x: 90, y: 50);
        world.Step();
        var overcharge = world.FindEntity(new EntityId(1))!.Modules.OfType<OverchargeBehavior>().Single();

        Check.That(feeder.Overcharge(2, 1).Reason == CommandRejection.NotPermitted, "Only the owner toggles it.");
        Check.That(feeder.Overcharge(1, 99).Reason == CommandRejection.UnknownEntity, "The target must exist.");
        Check.That(feeder.Overcharge(1, 2).Reason == CommandRejection.CannotToggleOvercharge, "A structure with no OverchargeBehavior module cannot be toggled.");
        Check.That(feeder.Overcharge(1, 1).Accepted, "The owner toggles its own plant.");
        world.Step();
        Check.That(overcharge.Active, "The command reached OverchargeBehavior.Toggle.");
        Check.That(feeder.Overcharge(1, 1).Accepted, "Toggling again is still a valid command (it switches back off).");
        world.Step();
        Check.That(!overcharge.Active, "The second command toggled it back off.");
    }

    /// <summary>
    /// Phase 10: a direct CaptureCommand (reduced <c>ActionManager::canCaptureBuilding</c>, see <c>CommandTypes.Capture</c>) changes ownership
    /// and moves the power pool: the static EnergyProduction and, when active, a PowerPlantUpgrade/OverchargeBehavior bonus.
    /// </summary>
    public static void CaptureTransfersOwnershipAndThePowerPool()
    {
        var fixture = new SyntheticFixture(files: PowerPlantFiles());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        var attacker = new PlayerId(1);
        var defender = new PlayerId(2);
        feeder.Spawn(1, "Capturer", owner: 1, x: 55, y: 50);                 // id 1
        feeder.Spawn(2, "CapturableStation", owner: 2, x: 50, y: 50);        // id 2, already in reach of the capturer at (55,50)
        feeder.Spawn(2, "PlainStation", owner: 2, x: 200, y: 50);            // id 3: not CAPTURABLE, not at war yet
        feeder.Spawn(2, "OverStation", owner: 2, x: 50, y: 90);              // id 4: out of reach
        world.Step();
        Check.That(world.EnergyProductionOf(defender) == 15, "Three defender structures produce 5 each.");

        Check.That(feeder.Capture(2, 1, 2).Reason == CommandRejection.NotPermitted, "Only the owner of the actor commands it.");
        Check.That(feeder.Capture(1, 99, 2).Reason == CommandRejection.UnknownEntity, "The actor must exist.");
        Check.That(feeder.Capture(1, 1, 99).Reason == CommandRejection.UnknownEntity, "The target must exist.");
        Check.That(feeder.Capture(1, 1, 3).Reason == CommandRejection.CannotCapture, "Neutral and not CAPTURABLE: cannot be captured.");
        Check.That(feeder.Capture(1, 1, 4).Reason == CommandRejection.CannotCapture, "CAPTURABLE but out of reach.");
        Check.That(feeder.Capture(1, 1, 1).Reason == CommandRejection.CannotCapture, "A unit cannot capture itself (not a STRUCTURE and same entity).");

        Check.That(feeder.Capture(1, 1, 2).Accepted, "CAPTURABLE, neutral (not an ally) and in reach: accepted.");
        world.Step();
        var station = world.FindEntity(new EntityId(2))!;
        Check.That(station.Owner == attacker, "Ownership moved to the capturing player.");
        Check.That(world.EnergyProductionOf(defender) == 10 && world.EnergyProductionOf(attacker) == 5, "The captured structure's EnergyProduction moved with it.");

        // At war, even a non-CAPTURABLE structure can be captured once in reach.
        feeder.SetRelationship(1, 2, Relationship.Enemies);
        feeder.SetMoveTarget(1, 1, 200, 55);
        var capturer = world.FindEntity(new EntityId(1))!;
        StepUntil(world, 200, () => (capturer.X - Fixed.FromInt(200)).Abs() < Fixed.FromInt(5));
        Check.That((capturer.X - Fixed.FromInt(200)).Abs() < Fixed.FromInt(5), "The capturer walked next to the plain station.");
        Check.That(feeder.Capture(1, 1, 3).Accepted, "An enemy structure needs no CAPTURABLE kind.");
        world.Step();
        Check.That(world.FindEntity(new EntityId(3))!.Owner == attacker, "The plain station is captured too.");

        // Capturing an active OverchargeBehavior moves its dynamic bonus as well as the base production.
        feeder.SetMoveTarget(1, 1, 50, 85);
        StepUntil(world, 200, () => (capturer.X - Fixed.FromInt(50)).Abs() < Fixed.FromInt(5) && (capturer.Y - Fixed.FromInt(85)).Abs() < Fixed.FromInt(5));
        var overStation = world.FindEntity(new EntityId(4))!;
        overStation.Modules.OfType<OverchargeBehavior>().Single().Enable(world, true);
        Check.That(world.EnergyProductionOf(defender) == 5 + 4, "The defender's last plant is overcharged: base 5 + bonus 4.");
        Check.That(feeder.Capture(1, 1, 4).Accepted, "The last enemy structure, now in reach, is captured.");
        world.Step();
        Check.That(world.EnergyProductionOf(defender) == 0, "The defender has no structures left.");
        Check.That(world.EnergyProductionOf(attacker) == 5 + 5 + 5 + 4, "The attacker's pool gained base + bonus from the overcharged plant too.");
        Check.That(overStation.Modules.OfType<OverchargeBehavior>().Single().Active, "Capture does not switch Overcharge off; it keeps running for the new owner.");
    }
}
