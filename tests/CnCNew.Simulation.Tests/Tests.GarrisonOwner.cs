using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

// Object::onDestroy: a destroyed rider leaves its container.
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> GarrisonOwnerFiles() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/GarrisonOwnerFixture.ini"] = """
        Object OwnBuilding
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = GarrisonContain Garrison01
            ContainMax = 3
          End
        End

        Object OwnBarracks
          KindOf = STRUCTURE FS_BARRACKS
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = GarrisonContain Garrison01
            ContainMax = 3
          End
        End

        Object OwnTruck
          KindOf = SELECTABLE VEHICLE TRANSPORT
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = TransportContain Transport01
            Slots = 2
            AllowInsideKindOf = INFANTRY
          End
        End

        Object OwnSoldier
          KindOf = SELECTABLE INFANTRY
          TransportSlotCount = 1
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture) GarrisonOwnerWorld()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: GarrisonOwnerFiles());
        var world = NewWorld(fixture);
        return (world, new Feeder(world), fixture);
    }

    private static Entity GarrisonSpawn(SimulationWorld world, Feeder feeder, string template, int owner)
    {
        feeder.Spawn(0, template, owner: owner, x: 100, y: 100);
        world.Step();
        return world.Entities.Where(value => value.Template.Name == template).OrderBy(value => value.Id.Value).Last();
    }

    public static void DestroyedRiderLeavesItsContainer()
    {
        var (world, feeder, _) = GarrisonOwnerWorld();
        var truck = GarrisonSpawn(world, feeder, "OwnTruck", 1);
        var soldier = GarrisonSpawn(world, feeder, "OwnSoldier", 1);
        Check.That(feeder.EnterContainer(1, soldier.Id.Value, truck.Id.Value).Accepted, "Setup: the soldier boards the truck.");
        world.Step();
        Check.That(truck.Container!.ContainCount == 1, "Setup: the truck holds the soldier.");
        feeder.Kill(soldier.Id.Value);
        for (var step = 0; step < world.Frames(3); step++) world.Step();
        Check.That(world.FindEntity(soldier.Id) is null && truck.Container.ContainCount == 0, $"A destroyed rider is taken out of its container ({truck.Container.ContainCount} left).");
    }
}
