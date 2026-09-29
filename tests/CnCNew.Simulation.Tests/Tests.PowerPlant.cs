using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;

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
}
