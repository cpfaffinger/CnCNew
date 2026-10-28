using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

// F-55: the attack's view test. Step (a): the terrain line of BaseHeightMapRenderObjClass::isClearLineOfSight; step (b): the obstacle walk of
// Pathfinder::isAttackViewBlockedByObstacle over the pathfind cells; step (c): outOfWeaponRangeObject counts a blocked view as out of range.
static partial class Tests
{
    public static void TerrainLineOfSightFollowsTheOriginal()
    {
        // 10 x 10 cells, border 1: world x in [10k, 10k+10) is vertex square k+1. Vertex column 5 is a ridge of 16 raw (10 m), so squares 4 and 5 are 10 high.
        var ridge = TerrainMap.Build(SyntheticMaps.Build(10, 10, 1, (x, _) => x == 5 ? 16 : 0), "ridge");
        static Fixed F(decimal value) => Fixed.FromDecimal(value);
        bool Clear(TerrainMap map, decimal x0, decimal z0, decimal x1, decimal z1) => map.IsClearLineOfSight(F(x0), F(50), F(z0), F(x1), F(50), F(z1));

        Check.That(!Clear(ridge, 5, 9.4m, 95, 9.4m), "A ridge 10 high blocks a level line at 9.4 (more than 0.5 above it).");
        Check.That(Clear(ridge, 5, 9.5m, 95, 9.5m), "At 9.5 the ridge is exactly 0.5 above the line: clear (LOS_FUDGE).");
        Check.That(!Clear(ridge, 95, 9.4m, 5, 9.4m), "The same line walked the other way is blocked as well.");
        Check.That(Clear(ridge, 5, 0, 5, 0) && Clear(ridge, 41, 0, 49, 0), "A line inside one square is clear, whatever stands there.");
        Check.That(!Clear(ridge, 41, 0, 95, 0), "The start square is tested: a line starting on the ridge is blocked.");
        Check.That(Clear(ridge, -50, 0, 95, 0), "A walk that starts off the field stops at once: clear (the original leaves the loop).");

        // The end square is never tested: vertex column 8 raised makes squares 7 and 8 high; a line ending in square 7 passes, one ending in square 8 does not.
        var wall = TerrainMap.Build(SyntheticMaps.Build(10, 10, 1, (x, _) => x == 8 ? 16 : 0), "wall");
        Check.That(Clear(wall, 5, 0, 65, 0), "A line ending in a high square: its end square is not tested.");
        Check.That(!Clear(wall, 5, 0, 75, 0), "One square further the high square is passed and blocks.");

        // The sight line climbs by an equal share per square: from 0 up to 30 over nine squares it is 13.3 at square 4 - above the ridge.
        Check.That(Clear(ridge, 5, 0, 95, 30), "A line that climbs over the ridge is clear.");
        Check.That(!Clear(ridge, 5, 0, 95, 15), "A line that climbs too slowly is blocked.");
    }

    private static IReadOnlyDictionary<string, string> AttackViewFiles() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/AttackViewFixture.ini"] = """
        Object ViewWall
          KindOf = STRUCTURE
          GeometryIsSmall = No
          Geometry = CYLINDER
          GeometryMajorRadius = 15.0
          GeometryHeight = 20.0
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
        End

        Object ViewFence
          KindOf = STRUCTURE CAN_SEE_THROUGH_STRUCTURE
          GeometryIsSmall = No
          Geometry = CYLINDER
          GeometryMajorRadius = 15.0
          GeometryHeight = 5.0
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
        End
        """,
    };

    public static void AttackLineBlockedByStructures()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: AttackViewFiles());
        fixture.Templates.TryGet("ViewWall", out var wall);
        fixture.Templates.TryGet("ViewFence", out var fence);
        static Fixed F(int value) => Fixed.FromInt(value);
        var flat = TerrainMap.Build(SyntheticMaps.Build(10, 10, 1), "flat");
        bool Blocked(NavigationGrid grid, int x1, int y1, params ulong[] excepted) => grid.IsAttackLineBlocked(F(5), F(55), F(x1), F(y1), excepted);

        var grid = new NavigationGrid(flat);
        grid.AddFootprint(7, wall!, F(50), F(55), Fixed.Zero);
        Check.That(Blocked(grid, 95, 55), "A structure between attacker and victim blocks the line.");
        Check.That(!Blocked(grid, 95, 55, 7), "Not when it is one of the excepted (the attacker's own, the victim, a slaver, the container).");
        Check.That(!Blocked(grid, 50, 55), "Nor when it covers the victim's own cell (a victim standing in it).");
        Check.That(!Blocked(grid, 25, 55), "A line that ends short of it is clear.");

        var seeThrough = new NavigationGrid(flat);
        seeThrough.AddFootprint(8, fence!, F(50), F(55), Fixed.Zero);
        Check.That(!Blocked(seeThrough, 95, 55), "A structure one can see through (CAN_SEE_THROUGH_STRUCTURE) does not block.");

        // Row 5 of cells is cliff: a structure on cliff or water cells leaves them as they are, and they do not block the view.
        var cliffs = TerrainMap.Build(SyntheticMaps.Build(10, 10, 1, cliffs: Enumerable.Range(0, 10).Select(cx => (cx + 1, 6))), "cliffs");
        var onCliff = new NavigationGrid(cliffs);
        onCliff.AddFootprint(7, wall!, F(50), F(55), Fixed.Zero);
        Check.That(!Blocked(onCliff, 95, 55), "A structure over cliff cells does not block a line along them.");

        // Two overlapping footprints: the answer does not depend on the order they came in, and is the same again after one left and came back.
        NavigationGrid Overlap(bool reversed)
        {
            var built = new NavigationGrid(flat);
            if (reversed) { built.AddFootprint(9, wall!, F(60), F(55), Fixed.Zero); built.AddFootprint(7, wall!, F(50), F(55), Fixed.Zero); }
            else { built.AddFootprint(7, wall!, F(50), F(55), Fixed.Zero); built.AddFootprint(9, wall!, F(60), F(55), Fixed.Zero); }
            return built;
        }
        var ab = Overlap(false);
        var ba = Overlap(true);
        foreach (var (x1, excepted) in new[] { (95, Array.Empty<ulong>()), (95, new ulong[] { 7 }), (55, Array.Empty<ulong>()), (55, new ulong[] { 7, 9 }) })
            Check.That(Blocked(ab, x1, 55, excepted) == Blocked(ba, x1, 55, excepted), $"The same answer whichever footprint came first (to {x1}, {excepted.Length} excepted).");
        Check.That(Blocked(ab, 95, 55, 7), "With one of two overlapping structures excepted the other still blocks.");
        // The cells both cover (4 to 6) pass for a victim among them; a cell only one covers blocks - the original's answer there depends on which came first.
        Check.That(!ab.IsAttackLineBlocked(F(45), F(55), F(65), F(55), []), "A line inside the cells both cover, to a victim among them, is clear.");
        Check.That(Blocked(ab, 55, 55), "A line to that victim through a cell only one of them covers is blocked.");
        ab.RemoveFootprint(9, wall!, F(60), F(55), Fixed.Zero);
        ab.AddFootprint(9, wall!, F(60), F(55), Fixed.Zero);
        Check.That(Blocked(ab, 95, 55) && !ab.IsAttackLineBlocked(F(45), F(55), F(65), F(55), []), "Removed and added again, the grid answers as before.");
    }

    private static IReadOnlyDictionary<string, string> AttackGateFiles() => new Dictionary<string, string>(AcquisitionFiles())
    {
        ["Data/INI/Object/AttackViewFixture.ini"] = AttackViewFiles()["Data/INI/Object/AttackViewFixture.ini"],
        ["Data/INI/Object/AttackGateFixture.ini"] = """
        Object LosSoldier
          KindOf = SELECTABLE INFANTRY ATTACK_NEEDS_LINE_OF_SIGHT
          VisionRange = 150.0
          Locomotor = SET_NORMAL ChaseLoco
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY AcqGun
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = Yes
            MoodAttackCheckRate = 250
          End
        End
        """,
    };

    /// <summary>A world on a flat map of 300 x 300 with the objects; returns the shots the first object fired over three seconds and the answer to an attack order it was given after the first frames.</summary>
    private static (int Shots, SubmitResult Order) AttackGateScene(string attacker, string? between)
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: AttackGateFiles());
        var terrain = TerrainMap.Build(SyntheticMaps.Build(30, 30, 1), "attack-gate");
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
        var feeder = new Feeder(world);
        feeder.Spawn(1, attacker, owner: 1, x: 50, y: 150);
        feeder.Spawn(2, "AcqMute", owner: 2, x: 130, y: 150);
        if (between is not null) feeder.Spawn(2, between, owner: 2, x: 90, y: 150);
        // The order is given once the first vision pass has seen the victim.
        for (var step = 0; step < world.Frames(3); step++) world.Step();
        var shooter = world.Entities.First(value => value.Template.Name == attacker);
        var victim = world.Entities.First(value => value.Template.Name == "AcqMute");
        var order = feeder.FireWeapon(1, shooter.Id.Value, victim.Id.Value);
        var shots = 0;
        for (var step = 0; step < world.Frames(90); step++)
            foreach (var value in world.Step().Events)
                if (value.Kind == SimEventKind.WeaponFired && value.Entity == shooter.Id) shots++;
        return (shots, order);
    }

    public static void AttackNeedsAClearView()
    {
        var blocked = AttackGateScene("LosSoldier", "ViewWall");
        Check.That(blocked.Shots == 0, $"A unit that needs a line of sight does not fire through a structure ({blocked.Shots} shots).");
        Check.That(!blocked.Order.Accepted && blocked.Order.Reason == CommandRejection.OutOfRange, $"An order to fire through it is out of range ({blocked.Order}).");
        var open = AttackGateScene("LosSoldier", null);
        Check.That(open.Shots > 0 && open.Order.Accepted, $"Without the structure it fires ({open.Shots} shots).");
        var fence = AttackGateScene("LosSoldier", "ViewFence");
        Check.That(fence.Shots > 0, $"Through a structure one can see through it fires ({fence.Shots} shots).");
        var blind = AttackGateScene("AcqSoldier", "ViewWall");
        Check.That(blind.Shots > 0 && blind.Order.Accepted, $"A unit without the kind fires through the structure ({blind.Shots} shots).");
    }
}
