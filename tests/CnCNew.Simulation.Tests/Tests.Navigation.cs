using System.Collections.Immutable;
using System.Linq;
using CnCNew.Data.World;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    private const int NavCells = 20;

    private const string NavigationLocomotorBody = """
          Speed = 60.0
          Acceleration = 60.0
          Braking = 60.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        """;

    // The default object template of the fixture (like the shipped data) sets GeometryIsSmall = Yes, so blocking structures say No.
    private static IReadOnlyDictionary<string, string> NavigationFiles() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/NavigationFixture.ini"] = $$"""
        Locomotor GroundLoco
          Surfaces = GROUND
          TurnRate = 90.0
        {{NavigationLocomotorBody}}
        End
        Locomotor ClimberLoco
          Surfaces = GROUND CLIFF
          TurnRate = 360.0
        {{NavigationLocomotorBody}}
        End
        Locomotor WaterLoco
          Surfaces = WATER
          TurnRate = 360.0
        {{NavigationLocomotorBody}}
        End
        Locomotor AirLoco
          Surfaces = AIR
          TurnRate = 360.0
        {{NavigationLocomotorBody}}
        End
        Locomotor LegacyLoco
          TurnRate = 360.0
        {{NavigationLocomotorBody}}
        End

        Object Walker
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL GroundLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object Climber
          KindOf = SELECTABLE INFANTRY
          Locomotor = SET_NORMAL ClimberLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object Boat
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL WaterLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object Flyer
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL AirLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object Legacy
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL LegacyLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object Wall
          KindOf = STRUCTURE
          Geometry = BOX
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 45.0
          GeometryHeight = 10.0
          GeometryIsSmall = No
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object Post
          KindOf = STRUCTURE
          Geometry = CYLINDER
          GeometryMajorRadius = 12.0
          GeometryMinorRadius = 12.0
          GeometryHeight = 10.0
          GeometryIsSmall = Yes
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    private static TerrainMap NavTerrain(IEnumerable<(int X, int Y)>? cliffs = null, IEnumerable<MapPolygonTrigger>? water = null, Func<int, int, int>? height = null)
        => TerrainMap.Build(SyntheticMaps.Build(NavCells, NavCells, 1, height ?? ((_, _) => 0), cliffs?.Select(cell => (cell.X + 1, cell.Y + 1)), water), "navigation");

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture) NavSetup(TerrainMap? terrain = null)
    {
        var fixture = new SyntheticFixture(SimulationRate.Original, files: NavigationFiles());
        var map = terrain ?? NavTerrain();
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), SimulationRate.Original, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, map.Fingerprint), fixture.Templates, map);
        return (world, new Feeder(world), fixture);
    }

    /// <summary>Steps until the entity ends its order; returns the tick count and whether MoveArrived fired.</summary>
    private static (int Ticks, bool Arrived) NavRun(SimulationWorld world, uint id, int limit, Action<Entity>? perTick = null)
    {
        var entity = world.FindEntity(new EntityId(id))!;
        var arrived = false;
        for (var tick = 0; tick < limit; tick++)
        {
            var result = world.Step();
            if (result.Events.Any(value => value.Kind == SimEventKind.MoveArrived && value.Entity.Value == id)) arrived = true;
            perTick?.Invoke(entity);
            if (!entity.HasMoveTarget) return (tick + 1, arrived);
        }
        return (limit, arrived);
    }

    private static (int X, int Y) NavCellOf(Entity entity) => (TerrainMap.CellOf(entity.X), TerrainMap.CellOf(entity.Y));

    // Cliff cells forming the border of a 5x5 block (cells 6..10); its inside (7..9) cannot be entered on foot.
    private static (int X, int Y)[] NavRing() => Enumerable.Range(6, 5).SelectMany(offset => new[] { (offset, 6), (offset, 10), (6, offset), (10, offset) }).Distinct().ToArray();

    private static bool NavInsideRingBlock(int x, int y) => x is >= 6 and <= 10 && y is >= 6 and <= 10;

    public static void NavigationAtan2()
    {
        var worst = 0.0;
        for (var step = 0; step < 720; step++)
            foreach (var magnitude in new[] { 1, 7, 300 })
            {
                var angle = step * Math.PI / 360.0 - Math.PI;
                var y = (int)Math.Round(Math.Sin(angle) * magnitude * 100);
                var x = (int)Math.Round(Math.Cos(angle) * magnitude * 100);
                if (x == 0 && y == 0) continue;
                var expected = Math.Atan2(y, x);
                var actual = (double)FixedTrig.Atan2(Fixed.FromRaw(y * 655), Fixed.FromRaw(x * 655)).ToDecimal();
                var difference = Math.Abs(actual - expected);
                if (difference > Math.PI) difference = 2 * Math.PI - difference;
                worst = Math.Max(worst, difference);
            }
        Check.That(worst < 2e-3, $"Atan2 stays within 2e-3 rad of the reference (worst {worst:E2}).");
        Check.That(FixedTrig.Atan2(Fixed.Zero, Fixed.FromInt(5)).Abs() < Fixed.FromRaw(4), "Atan2 of the positive x axis is 0.");
        Check.That((FixedTrig.Atan2(Fixed.FromInt(3), Fixed.Zero) - FixedTrig.HalfPi).Abs() < Fixed.FromRaw(8), "Atan2 of the positive y axis is a quarter turn.");
        Check.That((FixedTrig.Atan2(Fixed.Zero, Fixed.FromInt(-5)) - FixedTrig.Pi).Abs() < Fixed.FromRaw(8), "Atan2 of the negative x axis is pi.");
        Check.That((FixedTrig.AngleDifference(FixedTrig.Pi - Fixed.FromRaw(100), -FixedTrig.Pi + Fixed.FromRaw(100)) - Fixed.FromRaw(200)).Abs() < Fixed.FromRaw(2),
            "The angle difference takes the short way over the +-pi seam.");
        Check.That((FixedTrig.Normalize(FixedTrig.TwoPi + Fixed.One) - Fixed.One).Abs() < Fixed.FromRaw(4), "Normalize folds a full turn away.");
    }

    public static void NavigationGridRules()
    {
        // Cell (2,2) is cliff and water at once; (3,2) water only; (4,2) plain.
        var terrain = NavTerrain([(2, 2)], [SyntheticMaps.Water("Lake", 10, (20, 20), (40, 20), (40, 30), (20, 30))]);
        var (world, feeder, _) = NavSetup(terrain);
        var grid = world.Navigation!;
        Check.That(!grid.IsPassable(2, 2, LocomotorSurfaces.Ground | LocomotorSurfaces.Water) && grid.IsPassable(2, 2, LocomotorSurfaces.Cliff),
            "A cliff cell that is also under water is a cliff: only cliff locomotors cross it.");
        Check.That(grid.IsPassable(3, 2, LocomotorSurfaces.Water) && !grid.IsPassable(3, 2, LocomotorSurfaces.Ground), "Water cells need the water surface.");
        Check.That(grid.IsPassable(4, 2, LocomotorSurfaces.Ground) && !grid.IsPassable(4, 2, LocomotorSurfaces.Water), "Plain cells need the ground surface.");
        Check.That(!grid.IsPassable(4, 2, LocomotorSurfaces.None) && !grid.IsPassable(-1, 0, LocomotorSurfaces.Ground) && !grid.IsPassable(0, NavCells, LocomotorSurfaces.Ground),
            "No surface means nothing is passable, and cells outside the map never are.");

        var empty = grid.ObstacleSum;
        feeder.Spawn(1, "Wall", x: 100, y: 100);
        feeder.Spawn(1, "Post", x: 30, y: 150);
        world.Step();
        var wallTemplate = world.FindEntity(new EntityId(1))!.Template;
        Check.That(NavigationGrid.Blocks(wallTemplate) && !NavigationGrid.Blocks(world.FindEntity(new EntityId(2))!.Template), "Only structures without small geometry block.");
        Check.That(grid.ObstacleSum != empty, "A wall changes the obstacle sum.");
        Check.That(grid.IsBlockedByObstacle(10, 10) && grid.IsBlockedByObstacle(10, 6) && grid.IsBlockedByObstacle(10, 13) && !grid.IsBlockedByObstacle(10, 3) && !grid.IsBlockedByObstacle(7, 10),
            "The wall covers its box (10 by 45 half extents, grown by a quarter cell) and nothing beyond.");
        Check.That(!grid.IsBlockedByObstacle(3, 15), "Small-geometry structures leave the grid free (Pathfinder::classifyObjectFootprint).");
        var withWall = grid.ObstacleSum;
        Check.That(!grid.IsPassable(10, 10, LocomotorSurfaces.Ground | LocomotorSurfaces.Water | LocomotorSurfaces.Cliff), "A blocked cell is impassable for every surface.");

        feeder.Destroy(1);
        world.Step();
        world.Step();
        Check.That(withWall != grid.ObstacleSum && grid.ObstacleSum == empty && !grid.IsBlockedByObstacle(10, 10), "Destroying the wall removes its footprint and restores the sum.");

        var gridA = new NavigationGrid(terrain);
        var gridB = new NavigationGrid(terrain);
        gridA.AddFootprint(7, wallTemplate, Fixed.FromInt(50), Fixed.FromInt(50), Fixed.Zero);
        gridA.AddFootprint(9, wallTemplate, Fixed.FromInt(150), Fixed.FromInt(150), Fixed.Zero);
        gridB.AddFootprint(9, wallTemplate, Fixed.FromInt(150), Fixed.FromInt(150), Fixed.Zero);
        gridB.AddFootprint(7, wallTemplate, Fixed.FromInt(50), Fixed.FromInt(50), Fixed.Zero);
        Check.That(gridA.ObstacleSum != 0 && gridA.ObstacleSum == gridB.ObstacleSum, "The obstacle sum does not depend on the order of the changes.");
        gridA.RemoveFootprint(7, wallTemplate, Fixed.FromInt(50), Fixed.FromInt(50), Fixed.Zero);
        gridA.RemoveFootprint(9, wallTemplate, Fixed.FromInt(150), Fixed.FromInt(150), Fixed.Zero);
        Check.That(gridA.ObstacleSum == 0 && !gridA.IsBlockedByObstacle(5, 5), "Removing every footprint restores the empty grid.");
    }

    public static void NavigationPathfinding()
    {
        var (world, feeder, _) = NavSetup();
        var grid = world.Navigation!;
        var ground = LocomotorSurfaces.Ground;

        var open = grid.FindPath(Fixed.FromInt(5), Fixed.FromInt(5), Fixed.FromInt(195), Fixed.FromInt(95), ground);
        Check.That(open.Status == PathStatus.Found && open.Points.Length == 1 && open.Points[0] == new PathPoint(Fixed.FromInt(195), Fixed.FromInt(95)),
            "On open ground smoothing reduces the path to the goal alone.");
        var same = grid.FindPath(Fixed.FromInt(5), Fixed.FromInt(5), Fixed.FromInt(8), Fixed.FromInt(3), ground);
        Check.That(same.Status == PathStatus.Found && same.Points.Length == 1, "Start and goal in one cell give a direct path.");

        feeder.Spawn(1, "Wall", x: 100, y: 100);
        world.Step();
        var around = grid.FindPath(Fixed.FromInt(100), Fixed.FromInt(35), Fixed.FromInt(100), Fixed.FromInt(165), ground);
        Check.That(around.Status == PathStatus.Found && around.Points.Length >= 2, "The path bends around the wall.");
        Check.That(around.Points[^1] == new PathPoint(Fixed.FromInt(100), Fixed.FromInt(165)), "A complete path ends exactly on the requested goal.");
        Check.That(around.Points.All(point => !grid.IsBlockedByObstacle(TerrainMap.CellOf(point.X), TerrainMap.CellOf(point.Y))), "No waypoint lies on an obstacle cell.");
        var again = grid.FindPath(Fixed.FromInt(100), Fixed.FromInt(35), Fixed.FromInt(100), Fixed.FromInt(165), ground);
        Check.That(again.Points.SequenceEqual(around.Points), "The same query gives the same path.");

        var caged = NavSetup(NavTerrain(NavRing()));
        var cagedGrid = caged.World.Navigation!;
        var partial = cagedGrid.FindPath(Fixed.FromInt(15), Fixed.FromInt(15), Fixed.FromInt(85), Fixed.FromInt(85), ground);
        Check.That(partial.Status == PathStatus.Partial && partial.Points.Length >= 1, "A goal inside a cliff ring is only reached partially.");
        var last = partial.Points[^1];
        Check.That(!NavInsideRingBlock(TerrainMap.CellOf(last.X), TerrainMap.CellOf(last.Y)), "The partial path stays outside the ring.");
        Check.That(cagedGrid.FindPath(Fixed.FromInt(85), Fixed.FromInt(85), Fixed.FromInt(85), Fixed.FromInt(85), ground).Status == PathStatus.Found, "Goal equal to start is trivially reached.");
        Check.That(cagedGrid.FindPath(Fixed.FromInt(85), Fixed.FromInt(85), Fixed.FromInt(15), Fixed.FromInt(15), ground | LocomotorSurfaces.Cliff).Status == PathStatus.Found,
            "A cliff locomotor crosses the ring.");

        // One passable cell (5,5) surrounded by cliffs on all eight sides.
        var pit = Enumerable.Range(4, 3).SelectMany(x => Enumerable.Range(4, 3).Select(y => (x, y))).Where(cell => cell != (5, 5)).ToArray();
        var trapped = NavSetup(NavTerrain(pit)).World.Navigation!;
        Check.That(trapped.FindPath(Fixed.FromInt(55), Fixed.FromInt(55), Fixed.FromInt(150), Fixed.FromInt(150), ground).Status == PathStatus.Blocked,
            "A start with no enterable neighbour has no path at all.");
        var none = trapped.FindPath(Fixed.FromInt(150), Fixed.FromInt(150), Fixed.FromInt(5), Fixed.FromInt(5), LocomotorSurfaces.None);
        Check.That(none.Status == PathStatus.Blocked && none.Points.IsEmpty, "Without any surface nothing can move.");
    }

    public static void NavigationMovement()
    {
        var (world, feeder, _) = NavSetup();
        feeder.Spawn(1, "Wall", x: 100, y: 100);
        feeder.Spawn(1, "Walker", x: 100, y: 25);
        world.Step();
        var walker = world.FindEntity(new EntityId(2))!;
        var grid = world.Navigation!;
        var turnLimit = Fixed.FromDecimal(0.0523599m) + Fixed.FromRaw(64);

        feeder.SetMoveTarget(1, 2, 100, 175);
        var previousAngle = walker.Angle;
        var maxTurn = Fixed.Zero;
        var entered = 0;
        var (ticks, arrived) = NavRun(world, 2, 900, unit =>
        {
            var turn = FixedTrig.AngleDifference(previousAngle, unit.Angle).Abs();
            if (turn > maxTurn) maxTurn = turn;
            previousAngle = unit.Angle;
            var cell = NavCellOf(unit);
            if (grid.IsBlockedByObstacle(cell.X, cell.Y)) entered++;
        });
        Check.That(arrived, $"The walker gets around the wall and arrives (ticks {ticks}).");
        Check.That(entered == 0, "The walker never stands on an obstacle cell.");
        Check.That(maxTurn <= turnLimit, $"The heading changes by at most the turn rate per tick ({maxTurn.Raw} raw vs {turnLimit.Raw}).");
        Check.That(walker.Speed == Fixed.Zero && !walker.HasMoveTarget && walker.Y > Fixed.FromInt(170), "It stops at the goal.");
        Check.That(ticks > 150 / 2, "The detour is longer than the straight line would be.");

        // Terrain height: a ramp along x. The ground unit follows it, the flyer keeps its altitude.
        var ramp = NavSetup(NavTerrain(height: (x, _) => x * 4));
        ramp.Feeder.Spawn(1, "Walker", x: 15, y: 105);
        ramp.Feeder.Spawn(1, "Flyer", x: 15, y: 55);
        ramp.World.Step();
        var flyer = ramp.World.FindEntity(new EntityId(2))!;
        var flyerZ = flyer.Z;
        ramp.Feeder.SetMoveTarget(1, 1, 175, 105);
        var walkerOnRamp = ramp.World.FindEntity(new EntityId(1))!;
        var (_, walkerArrived) = NavRun(ramp.World, 1, 400);
        Check.That(walkerArrived && walkerOnRamp.Z == ramp.World.Terrain!.HeightAt(walkerOnRamp.X, walkerOnRamp.Y) && walkerOnRamp.Z > Fixed.FromInt(20),
            "A ground unit is snapped to the terrain height along its path.");
        ramp.Feeder.SetMoveTarget(1, 2, 175, 55);
        var (_, flyerArrived) = NavRun(ramp.World, 2, 400);
        Check.That(flyerArrived && flyer.Z == flyerZ, "An air unit keeps its altitude.");

        // Locomotors without a Surfaces flag cannot cross terrain: the order ends without an arrival.
        var legacy = NavSetup();
        legacy.Feeder.Spawn(1, "Legacy", x: 20, y: 20);
        legacy.World.Step();
        legacy.Feeder.SetMoveTarget(1, 1, 150, 150);
        var (_, legacyArrived) = NavRun(legacy.World, 1, 30);
        var legacyUnit = legacy.World.FindEntity(new EntityId(1))!;
        Check.That(!legacyArrived && !legacyUnit.HasMoveTarget && legacyUnit.X == Fixed.FromInt(20) && legacyUnit.Y == Fixed.FromInt(20),
            "A locomotor without surfaces neither moves nor reports an arrival.");
    }

    public static void NavigationSurfacesAndUnreachableGoals()
    {
        var lake = SyntheticMaps.Water("Lake", 10, (100, 0), (200, 0), (200, 200), (100, 200));
        var terrain = NavTerrain(water: [lake], height: (x, _) => x >= 11 ? 0 : 40);
        var (world, feeder, _) = NavSetup(terrain);
        Check.That(world.Terrain!.CellAt(15, 5).HasFlag(CellFlags.Water) && !world.Terrain.CellAt(5, 5).HasFlag(CellFlags.Water), "Fixture: the east half is a lake.");
        feeder.Spawn(1, "Boat", x: 155, y: 55);
        feeder.Spawn(1, "Walker", x: 45, y: 55);
        world.Step();
        var boat = world.FindEntity(new EntityId(1))!;
        var walker = world.FindEntity(new EntityId(2))!;

        feeder.SetMoveTarget(1, 1, 155, 145);
        var (_, boatArrived) = NavRun(world, 1, 400);
        Check.That(boatArrived && boat.Y > Fixed.FromInt(140), "A boat crosses the lake.");

        feeder.SetMoveTarget(1, 1, 45, 55);
        var onWater = true;
        var (_, boatLandArrived) = NavRun(world, 1, 400, unit => onWater &= world.Terrain.CellAt(NavCellOf(unit).X, NavCellOf(unit).Y).HasFlag(CellFlags.Water));
        Check.That(!boatLandArrived && onWater && boat.X > Fixed.FromInt(90), "A land goal is only approached to the shore; the boat never leaves the water and reports no arrival.");

        feeder.SetMoveTarget(1, 2, 155, 55);
        var dry = true;
        var (_, walkerLakeArrived) = NavRun(world, 2, 400, unit => dry &= !world.Terrain.CellAt(NavCellOf(unit).X, NavCellOf(unit).Y).HasFlag(CellFlags.Water));
        Check.That(!walkerLakeArrived && dry && walker.X < Fixed.FromInt(105), "A ground unit ordered into the lake stops at the shore.");

        // A cliff ring: the goal inside is unreachable for walkers and reachable for a climber.
        var walled = NavSetup(NavTerrain(NavRing()));
        walled.Feeder.Spawn(1, "Walker", x: 15, y: 15);
        walled.Feeder.Spawn(1, "Climber", x: 25, y: 15);
        walled.World.Step();
        walled.Feeder.SetMoveTarget(1, 1, 85, 85);
        var (_, blockedArrived) = NavRun(walled.World, 1, 600);
        var stuck = walled.World.FindEntity(new EntityId(1))!;
        Check.That(!blockedArrived && !stuck.HasMoveTarget && stuck.Speed == Fixed.Zero, "An unreachable goal ends the order at the closest reachable point without an arrival event.");
        Check.That(!NavInsideRingBlock(NavCellOf(stuck).X, NavCellOf(stuck).Y), "The walker stays outside the ring.");
        walled.Feeder.SetMoveTarget(1, 2, 85, 85);
        var (_, climberArrived) = NavRun(walled.World, 2, 600);
        Check.That(climberArrived, "A climber crosses the cliffs.");
    }

    public static void NavigationRepathsAfterConstruction()
    {
        var (world, feeder, _) = NavSetup();
        feeder.Spawn(1, "Walker", x: 100, y: 25);
        world.Step();
        var walker = world.FindEntity(new EntityId(1))!;
        var grid = world.Navigation!;
        feeder.SetMoveTarget(1, 1, 100, 175);
        for (var tick = 0; tick < 36; tick++) world.Step();
        Check.That(walker.Y > Fixed.FromInt(26) && walker.Y < Fixed.FromInt(50), $"The walker is on its way, still north of where the wall will stand (y {walker.Y}).");

        feeder.Spawn(1, "Wall", x: 100, y: 100);
        var entered = 0;
        var (ticks, arrived) = NavRun(world, 1, 900, unit =>
        {
            var cell = NavCellOf(unit);
            if (grid.IsBlockedByObstacle(cell.X, cell.Y)) entered++;
        });
        Check.That(arrived && entered == 0, $"A structure placed on the route makes the walker re-plan and still arrive (ticks {ticks}).");

        var freed = NavSetup();
        freed.Feeder.Spawn(1, "Wall", x: 100, y: 100);
        freed.Feeder.Spawn(1, "Walker", x: 100, y: 25);
        freed.World.Step();
        freed.Feeder.SetMoveTarget(1, 2, 100, 175);
        for (var tick = 0; tick < 10; tick++) freed.World.Step();
        freed.Feeder.Destroy(1);
        var crossedFormerWall = false;
        var (_, freedArrived) = NavRun(freed.World, 2, 900, unit => crossedFormerWall |= NavCellOf(unit) == (10, 10));
        Check.That(freedArrived && crossedFormerWall, "Destroying the wall mid-way lets the walker take the direct route through its former cells.");
    }

    private static SimulationWorld NavBusyWorld(out SyntheticFixture fixture, out TerrainMap terrain)
    {
        terrain = NavTerrain([(3, 3), (3, 4), (3, 5)], [SyntheticMaps.Water("Pond", 5, (140, 140), (190, 140), (190, 190), (140, 190))], (x, y) => x > 12 && y > 12 ? 0 : 20);
        var setup = NavSetup(terrain);
        fixture = setup.Fixture;
        setup.Feeder.Spawn(1, "Wall", x: 100, y: 100);
        setup.Feeder.Spawn(2, "Wall", x: 60, y: 140);
        for (var index = 0; index < 4; index++)
        {
            setup.Feeder.Spawn(1, "Walker", x: 15 + index * 10, y: 20);
            setup.Feeder.Spawn(2, "Climber", x: 185 - index * 10, y: 30);
        }
        setup.Feeder.Spawn(1, "Boat", x: 160, y: 160);
        setup.Feeder.Spawn(2, "Flyer", x: 10, y: 190);
        setup.World.Step();
        for (uint id = 3; id <= 10; id++)
        {
            var owner = id % 2 == 1 ? 1 : 2;
            setup.Feeder.SetMoveTarget(owner, id, 20 + (int)id * 15, 180 - (int)id * 5);
        }
        setup.Feeder.SetMoveTarget(1, 11, 185, 185);
        setup.Feeder.SetMoveTarget(2, 12, 190, 10);
        return setup.World;
    }

    public static void NavigationSnapshotsAndDeterminism()
    {
        var first = NavBusyWorld(out var fixture, out var terrain);
        var second = NavBusyWorld(out _, out _);
        for (var tick = 0; tick < 40; tick++)
            Check.That(first.Step().StateHash == second.Step().StateHash, "Two identical runs agree in every tick.");

        var bytes = WorldSnapshot.Write(first);
        var restored = WorldSnapshot.Restore(bytes, fixture.Templates, fixture.Fingerprint, terrain);
        Check.That(restored.Navigation!.ObstacleSum == first.Navigation!.ObstacleSum && restored.Navigation.ObstacleSum != 0, "A restored world rebuilds the same footprints.");
        Check.That(WorldSnapshot.Write(restored).AsSpan().SequenceEqual(bytes), "A snapshot with paths in flight round-trips byte-identically.");
        for (var tick = 0; tick < 400; tick++)
        {
            var expected = first.Step().StateHash;
            var actual = restored.Step().StateHash;
            if (expected != actual) throw new InvalidOperationException($"Restored run diverges at continuation tick {tick}.");
            second.Step();
        }
        Check.That(WorldSnapshot.Write(restored).AsSpan().SequenceEqual(WorldSnapshot.Write(first)), "A restored run ends byte-identical to the continuous run.");
        Check.That(second.StateHash == first.StateHash, "The second independent run ends in the same state.");
    }
}
