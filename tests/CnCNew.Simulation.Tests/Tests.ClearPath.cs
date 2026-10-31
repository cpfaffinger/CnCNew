using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// F-84: "Harvesters &amp; dozers want a clear path" (<c>Pathfinder::moveAllies</c>): an idle ally on a node of a new path makes way. Before, only a factory's exit point moved anybody, and an idle dozer
/// on the approach point of a shipped dock held a supply truck there for ever: its computer player collected nothing.
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> ClearPathFiles() => new Dictionary<string, string>(SupplyFiles())
    {
        ["Data/INI/Object/ClearPathFixture.ini"] = """
        Locomotor ClearLoco
          Surfaces = GROUND
          TurnRate = 360.0
          Speed = 60.0
          Acceleration = 60.0
          Braking = 60.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End

        Object ClearHauler
          KindOf = SELECTABLE VEHICLE HARVESTER
          Locomotor = SET_NORMAL ClearLoco
          Geometry = CYLINDER
          GeometryMajorRadius = 12.0
          GeometryHeight = 10.0
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = SupplyTruckAIUpdate AI01
            MaxBoxes = 2
            SupplyCenterActionDelay = 100
            SupplyWarehouseActionDelay = 100
            SupplyWarehouseScanDistance = 400.0
          End
        End
        Object ClearSoldier
          KindOf = SELECTABLE INFANTRY
          Locomotor = SET_NORMAL ClearLoco
          Geometry = CYLINDER
          GeometryMajorRadius = 12.0
          GeometryHeight = 10.0
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """,
    };

    private static (SimulationWorld World, Feeder Feeder, Entity Hauler, Entity Warehouse) ClearPathWorld(SimulationRate rate, (string Template, int Owner, int X, int Y)? blocker)
    {
        var (world, feeder, _) = RatedWorld(rate, ClearPathFiles(), TerrainMap.Build(SyntheticMaps.Build(60, 60, 1), "clear-path"));
        feeder.Spawn(1, "Depot", x: 100, y: 300);
        feeder.Spawn(1, "Warehouse", x: 400, y: 300);
        feeder.Spawn(1, "ClearHauler", x: 250, y: 300);
        if (blocker is { } other) feeder.Spawn(other.Owner, other.Template, owner: other.Owner, x: other.X, y: other.Y);
        // The order is given in the first tick of a frame of the original, at every rate.
        for (var tick = 0; tick < world.Frames(1); tick++) world.Step();
        return (world, feeder, world.Entities.First(value => value.Template.Name == "ClearHauler"), world.Entities.Single(value => value.Template.Name == "Warehouse"));
    }

    public static void IdleAlliesMakeWayForAHarvester()
    {
        string? reference = null;
        foreach (var (rate, _) in WorldRates)
        {
            // Where the truck goes for the warehouse: the approach point of its dock.
            var (scout, _, scoutHauler, scoutHouse) = ClearPathWorld(rate, null);
            Check.That(scout.Submit(new DockCommand(new PlayerId(1), 900, scout.Tick, scoutHauler.Id, scoutHouse.Id)).Accepted, $"{rate}: the dock order is accepted.");
            for (var tick = 0; tick < scout.Frames(3); tick++) scout.Step();
            var (spotX, spotY) = ((int)scoutHauler.MoveTargetX.Floor(), (int)scoutHauler.MoveTargetY.Floor());

            var (world, _, hauler, warehouse) = ClearPathWorld(rate, ("ClearHauler", 1, spotX, spotY));
            var idle = world.Entities.Last(value => value.Template.Name == "ClearHauler");
            var (idleX, idleY) = (idle.X, idle.Y);
            Check.That(world.Submit(new DockCommand(new PlayerId(1), 900, world.Tick, hauler.Id, warehouse.Id)).Accepted, $"{rate}: the dock order is accepted with an ally on the approach point.");
            var deposits = RunCollecting(world, world.Frames(1500), SimEventKind.SupplyDeposited);
            Check.That(idle.X != idleX || idle.Y != idleY, $"{rate}: the idle ally has made way.");
            Check.That(deposits.Count > 0, $"{rate}: the truck delivers.");
            var line = string.Join(",", deposits.Select(value => (value.Tick - hauler.SpawnTick) / world.Time.TicksPerOriginalFrame)) + $" {idle.X} {idle.Y}";
            reference ??= line;
            Check.That(line == reference, $"{rate}: the same deliveries at every rate ({line} against {reference}).");

            // An enemy does not make way.
            var (hostile, _, enemyHauler, enemyHouse) = ClearPathWorld(rate, ("ClearSoldier", 2, spotX, spotY));
            var enemy = hostile.Entities.Single(value => value.Template.Name == "ClearSoldier");
            var (enemyX, enemyY) = (enemy.X, enemy.Y);
            hostile.Submit(new DockCommand(new PlayerId(1), 900, hostile.Tick, enemyHauler.Id, enemyHouse.Id));
            for (var tick = 0; tick < hostile.Frames(300); tick++) hostile.Step();
            Check.That(enemy.X == enemyX && enemy.Y == enemyY, $"{rate}: an enemy stays where it stands.");
        }
    }
}
