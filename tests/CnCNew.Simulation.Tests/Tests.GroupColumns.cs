using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

// AIGroup::friend_computeGroundPath, friend_moveInfantryToPos, friend_moveVehicleToPos: a group that goes far walks one wide path, in lanes.
static partial class Tests
{
    private const string ColumnIni = """
        Locomotor ColumnFront
          Surfaces = GROUND
          AirborneTargetingHeight = 30
          Speed = 60.0
          SpeedDamaged = 60.0
          TurnRate = 720.0
          Acceleration = 600.0
          Braking = 600.0
          GroupMovementPriority = MOVES_FRONT
          Appearance = TWO_LEGS
        End
        Locomotor ColumnBack
          Surfaces = GROUND
          AirborneTargetingHeight = 30
          Speed = 60.0
          SpeedDamaged = 60.0
          TurnRate = 720.0
          Acceleration = 600.0
          Braking = 600.0
          GroupMovementPriority = MOVES_BACK
          Appearance = TWO_LEGS
        End
        Locomotor ColumnTread
          Surfaces = GROUND
          AirborneTargetingHeight = 30
          Speed = 60.0
          SpeedDamaged = 60.0
          TurnRate = 720.0
          Acceleration = 600.0
          Braking = 600.0
          Appearance = TREADS
        End
        Object ColumnRifle
          KindOf = INFANTRY SELECTABLE
          Geometry = CYLINDER
          GeometryMajorRadius = 5.0
          GeometryMinorRadius = 5.0
          GeometryHeight = 10.0
          Locomotor = SET_NORMAL ColumnFront
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object ColumnRocket
          KindOf = INFANTRY SELECTABLE
          Geometry = CYLINDER
          GeometryMajorRadius = 5.0
          GeometryMinorRadius = 5.0
          GeometryHeight = 10.0
          Locomotor = SET_NORMAL ColumnBack
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object ColumnTank
          KindOf = VEHICLE SELECTABLE
          Geometry = CYLINDER
          GeometryMajorRadius = 8.0
          GeometryMinorRadius = 8.0
          GeometryHeight = 10.0
          Locomotor = SET_NORMAL ColumnTread
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object ColumnWall
          KindOf = STRUCTURE IMMOBILE
          Geometry = BOX
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 300.0
          GeometryHeight = 30.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
        End
        Object ColumnPost
          KindOf = STRUCTURE IMMOBILE
          Geometry = BOX
          GeometryMajorRadius = 5.0
          GeometryMinorRadius = 5.0
          GeometryHeight = 30.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
        End
        """;

    private static SyntheticFixture ColumnFixture()
        => new(TestRate.Default, files: new Dictionary<string, string>(SyntheticFixture.Files) { ["Data/INI/Object/Columns.ini"] = ColumnIni });

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture) ColumnWorld()
    {
        var fixture = ColumnFixture();
        var terrain = TerrainMap.Build(SyntheticMaps.Build(140, 140, 1), "columns");
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
        return (world, new Feeder(world), fixture);
    }

    private static Entity[] ColumnSquad(SimulationWorld world, Feeder feeder, string template, int count, int x, int y)
    {
        for (var index = 0; index < count; index++) feeder.Spawn(1, template, owner: 1, x: x - index / 4 * 18, y: y + (index % 4 - 2) * 18);
        for (var tick = 0; tick < world.Frames(2); tick++) world.Step();
        return world.Entities.Where(value => value.Template.Name == template).OrderBy(value => value.Id.Value).ToArray();
    }

    private static decimal ColumnMean(IEnumerable<Entity> units) => units.Average(unit => unit.MoveTargetX.ToDecimal());

    public static void AGroupGoesInColumns()
    {
        // Twelve men far from their goal: three lanes at the goal, each its own place.
        var (world, feeder, _) = ColumnWorld();
        var men = ColumnSquad(world, feeder, "ColumnRifle", 12, 200, 700);
        Check.That(GroupMove(world, men, 1200, 700).Accepted, "They take the order.");
        world.Step();
        var across = men.Select(unit => (unit.MoveTargetY - Fixed.FromInt(700)).ToDecimal()).ToArray();
        Check.That(across.Max() - across.Min() <= 2 * 22 + 2 * 10, $"Three lanes at the goal ({across.Min()} to {across.Max()}).");
        Check.That(men.Select(unit => (unit.MoveTargetX, unit.MoveTargetY)).Distinct().Count() == men.Length, "Every man has a place of his own.");
        Check.That(men.Max(unit => unit.MoveTargetX) - men.Min(unit => unit.MoveTargetX) >= Fixed.FromInt(22), "They stand one behind the other.");
        for (var tick = 0; tick < world.Frames(2000) && men.Any(unit => unit.HasMoveTarget); tick++) world.Step();
        Check.That(men.All(unit => !unit.HasMoveTarget), "Everybody arrives.");

        // GroupMovementPriority: the rockets behind the rifles, however they stand.
        (world, feeder, _) = ColumnWorld();
        var rifles = ColumnSquad(world, feeder, "ColumnRifle", 6, 200, 700);
        var rockets = ColumnSquad(world, feeder, "ColumnRocket", 6, 280, 700);
        Check.That(GroupMove(world, rifles.Concat(rockets), 1200, 700).Accepted, "A mixed order.");
        world.Step();
        Check.That(ColumnMean(rifles) > ColumnMean(rockets), $"The front ones walk in front ({ColumnMean(rifles)}, {ColumnMean(rockets)}).");

        // Two are no column: each keeps its offset (m_minInfantryForGroup).
        (world, feeder, _) = ColumnWorld();
        var two = ColumnSquad(world, feeder, "ColumnRifle", 2, 200, 700);
        var offset = two[1].Y - two[0].Y;
        Check.That(GroupMove(world, two, 1200, 700).Accepted, "A pair.");
        world.Step();
        Check.That(two[1].MoveTargetY - two[0].MoveTargetY == offset, "Two keep the offset they had.");

        // Vehicles: lanes wider apart, and three at the goal only from five on.
        (world, feeder, _) = ColumnWorld();
        var tanks = ColumnSquad(world, feeder, "ColumnTank", 6, 200, 700);
        Check.That(GroupMove(world, tanks, 1200, 700).Accepted, "Six tanks.");
        world.Step();
        var lanes = tanks.Select(unit => unit.MoveTargetY).Distinct().Count();
        Check.That(lanes >= 3 && tanks.Max(unit => unit.MoveTargetY) - tanks.Min(unit => unit.MoveTargetY) >= Fixed.FromInt(2 * 32 - 20), $"They spread over three lanes ({lanes}).");

        // A group attack-move is no column and keeps no offset: every member is sent to the point itself (groupAttackMoveToPosition).
        (world, feeder, _) = ColumnWorld();
        var hunters = ColumnSquad(world, feeder, "ColumnRifle", 8, 200, 700);
        Check.That(GroupMove(world, hunters, 1200, 700, attack: true).Accepted, "An attack-move.");
        world.Step();
        var nearest = hunters.Select(unit => Fixed.Sqrt((unit.MoveTargetX - Fixed.FromInt(1200)) * (unit.MoveTargetX - Fixed.FromInt(1200)) + (unit.MoveTargetY - Fixed.FromInt(700)) * (unit.MoveTargetY - Fixed.FromInt(700)))).Min();
        Check.That(nearest <= Fixed.FromInt(10), $"One of them has the point ({nearest}).");
    }

    public static void AColumnGoesRoundWhatStandsInItsWay()
    {
        var (world, feeder, fixture) = ColumnWorld();
        feeder.Spawn(2, "ColumnWall", owner: 2, x: 700, y: 700);
        var men = ColumnSquad(world, feeder, "ColumnRifle", 12, 200, 700);
        Check.That(GroupMove(world, men, 1200, 700).Accepted, "They take the order.");
        for (var tick = 0; tick < world.Frames(2); tick++) world.Step();
        var nodes = men.Select(unit => unit.Modules.OfType<AIUpdateInterface>().Single().PathNodes.ToArray()).ToArray();
        Check.That(nodes.All(path => path.Length >= 2), "A lane has its points on the way.");
        var corners = nodes.Select(path => path.Max(node => ColumnAbs(node.Y - Fixed.FromInt(700)))).ToArray();
        Check.That(corners.All(corner => corner >= Fixed.FromInt(290)), $"It goes round the end of the wall ({corners.Min()}).");
        Check.That(corners.Distinct().Count() >= 3, "In lanes of their own.");

        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, world.Terrain);
        Check.That(WorldSnapshot.Write(restored).AsSpan().SequenceEqual(WorldSnapshot.Write(world)), "The lanes survive a snapshot.");

        var budget = world.Frames(3000);
        for (var tick = 0; tick < budget && men.Any(unit => unit.HasMoveTarget); tick++) world.Step();
        Check.That(men.All(unit => !unit.HasMoveTarget), "Everybody arrives.");
        Check.That(men.All(unit => unit.X > Fixed.FromInt(1000)), "On the far side.");
    }

    private static Fixed ColumnAbs(Fixed value) => value < Fixed.Zero ? -value : value;

    public static void AGroundPathWantsItsWidth()
    {
        // A wall with a gap of two cells on the straight way and a gap of eight beside it: the path takes the wide one.
        ColumnFixture().Templates.TryGet("ColumnPost", out var post);
        var grid = new NavigationGrid(TerrainMap.Build(SyntheticMaps.Build(120, 120, 1), "gaps"));
        var id = 1UL;
        for (var y = 5; y < 1200; y += 10)
            if (y is not (> 500 and < 520) && y is not (> 540 and < 620)) grid.AddFootprint(id++, post!, Fixed.FromInt(605), Fixed.FromInt(y), Fixed.Zero);

        var path = grid.FindGroundPath(Fixed.FromInt(300), Fixed.FromInt(510), 0, Fixed.FromInt(900), Fixed.FromInt(510), 0, 6);
        Check.That(path.Length >= 3, $"A path that bends ({path.Length} nodes).");
        Check.That(path[0].X == Fixed.FromInt(300) && path[0].Y == Fixed.FromInt(510), "It begins where the group stands.");
        var crossing = path.Zip(path.Skip(1)).First(leg => leg.First.X <= Fixed.FromInt(605) && leg.Second.X >= Fixed.FromInt(605));
        var at = crossing.First.Y + (crossing.Second.Y - crossing.First.Y) * (Fixed.FromInt(605) - crossing.First.X) / (crossing.Second.X - crossing.First.X);
        Check.That(at > Fixed.FromInt(540) && at < Fixed.FromInt(620), $"Through the wide gap ({at}).");

        // Nothing leads through a closed wall.
        grid.AddFootprint(id++, post!, Fixed.FromInt(605), Fixed.FromInt(505), Fixed.Zero);
        grid.AddFootprint(id++, post!, Fixed.FromInt(605), Fixed.FromInt(515), Fixed.Zero);
        for (var y = 545; y < 620; y += 10) grid.AddFootprint(id++, post!, Fixed.FromInt(605), Fixed.FromInt(y), Fixed.Zero);
        Check.That(grid.FindGroundPath(Fixed.FromInt(300), Fixed.FromInt(510), 0, Fixed.FromInt(900), Fixed.FromInt(510), 0, 6).IsDefaultOrEmpty, "No way, no path: the group walks alone.");
    }
}
