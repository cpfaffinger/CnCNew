using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

// The movement profile movement-budget (Stage 3, S3-M6; docs/PERF-MOVEMENT.md section 9): the world's path queue with a cell budget per tick, searches that pause and continue,
// waiting units that keep their orders, the shared group path, and a snapshot taken in the middle of a paused search.
static partial class Tests
{
    private static readonly RuleProfile MovementBudget = RuleProfile.Parse(RuleProfile.BudgetedMovementName);

    private static IReadOnlyDictionary<string, string> MovementProfileFiles() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/MovementProfileFixture.ini"] = """
        Locomotor RunLoco
          Surfaces = GROUND
          Speed = 300
          Acceleration = 3000
          Braking = 3000
          TurnRate = 3600
        End
        Object Runner
          KindOf = INFANTRY SELECTABLE
          Geometry = CYLINDER
          GeometryMajorRadius = 3.0
          GeometryMinorRadius = 3.0
          GeometryHeight = 10.0
          Locomotor = SET_NORMAL RunLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    /// <summary>A map of 220 x 160 cells with a cliff wall down the middle and its only gap at the bottom: a search from the left top to the right top floods the left half.</summary>
    private static TerrainMap WalledTerrain()
    {
        var cliffs = new List<(int X, int Y)>();
        for (var y = 0; y < 150; y++) cliffs.Add((110, y));
        return TerrainMap.Build(SyntheticMaps.Build(220, 160, 1, (_, _) => 0, cliffs.Select(cell => (cell.X + 1, cell.Y + 1))), "walled");
    }

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture, TerrainMap Terrain, Entity[] Runners) MovementWorld(RuleProfile rules, params (int X, int Y)[] places)
        => MovementWorldAt(TestRate.Default, rules, places);

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture, TerrainMap Terrain, Entity[] Runners) MovementWorldAt(SimulationRate rate, RuleProfile rules, params (int X, int Y)[] places)
    {
        var terrain = WalledTerrain();
        var fixture = new SyntheticFixture(rate, files: MovementProfileFiles(), rules: rules);
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
        var feeder = new Feeder(world);
        foreach (var (x, y) in places) feeder.Spawn(1, "Runner", owner: 1, x: x, y: y);
        world.Step();
        return (world, feeder, fixture, terrain, world.Entities.Where(value => value.Template.Name == "Runner").OrderBy(value => value.Id.Value).ToArray());
    }

    // Eight runners on the left, each more than ShareStartCells apart, ordered to eight places on the right: eight searches of about 20,000 cells each.
    private static readonly (int X, int Y)[] ApartStarts = [(50, 50), (50, 400), (50, 750), (50, 1100), (400, 50), (400, 400), (400, 750), (400, 1100)];

    private static void OrderAcross(SimulationWorld world, Entity[] runners)
    {
        ulong sequence = 1000;
        for (var index = 0; index < runners.Length; index++)
        {
            var submitted = world.Submit(new SetMoveTargetCommand(new PlayerId(1), sequence++, world.Tick, runners[index].Id, Fixed.FromInt(2000), Fixed.FromInt(100 + 150 * index)));
            Check.That(submitted.Accepted, $"The move is accepted ({submitted.Reason}).");
        }
    }

    /// <summary>
    /// A search run in slices of any size - and continued after its memory was lost, as in a restored world - returns exactly what the search in one piece returns, for plain and
    /// arriving-only questions, with blockers and footprints.
    /// </summary>
    public static void MovementBudgetSlicedSearchEqualsTheWholeSearch()
    {
        var random = new PerfRandom(0x34_0001);
        var templates = PerfFootprintTemplates();
        long compared = 0, paused = 0;
        for (var map = 0; map < 6; map++)
        {
            var terrain = PerfTerrain(random, 30 + 11 * map, 34 + 9 * map, map % 3 * 6);
            var grid = new NavigationGrid(terrain);
            var footprints = new List<(ulong Id, ObjectTemplate Template, Fixed X, Fixed Y, Fixed Angle)>();
            ulong nextId = 1;
            for (var round = 0; round < 12; round++)
            {
                for (var change = 0; change < (round == 0 ? 6 : 1); change++) PerfChangeFootprints(random, grid, footprints, templates, ref nextId);
                for (var index = 0; index < 30; index++)
                {
                    var perf = PerfRandomQuery(random, terrain, footprints);
                    var arriving = random.Below(3) == 0;
                    var query = new PathQuery(perf.StartX, perf.StartY, perf.GoalX, perf.GoalY, perf.Surfaces, [..perf.Blocks], arriving);
                    var whole = arriving ? grid.FindPathToGoal(query.StartX, query.StartY, query.GoalX, query.GoalY, query.Surfaces, perf.Blocks)
                        : grid.FindPath(query.StartX, query.StartY, query.GoalX, query.GoalY, query.Surfaces, perf.Blocks);
                    var slice = 1 + random.Below(400);
                    var lose = random.Below(4) == 0;
                    var done = 0;
                    PathResult? sliced;
                    var first = true;
                    while (true)
                    {
                        // Now and then the memory is lost between two slices (a restored world): the search begins again and runs silently up to where it was.
                        if (lose && !first) grid.DropSlice();
                        var ended = grid.Slice(query, false, done, done + slice, out sliced, out done, out _);
                        first = false;
                        if (ended) break;
                        paused++;
                        // Another search in between must not disturb the paused one.
                        grid.FindPath(perf.GoalX, perf.GoalY, perf.StartX, perf.StartY, LocomotorSurfaces.Ground);
                    }
                    Check.That(whole is null ? sliced is null : sliced is not null && SamePath(sliced, whole),
                        $"Map {map}, round {round}, query {index}: the sliced search ({(sliced is null ? "none" : PathText(sliced))}) equals the whole one ({(whole is null ? "none" : PathText(whole))}).");
                    compared++;
                }
            }
        }
        Check.That(compared == 6 * 12 * 30 && paused > 1000, $"{compared} searches compared, {paused} pauses.");
    }

    /// <summary>
    /// The queue under the movement profile: eight long searches in one frame do not fit the budget of a frame; the queue works through them over several frames, never more than
    /// the budget in one and only in a tick on a frame boundary, the runners that wait stand and keep their orders, and every runner arrives. The original profile does not use the queue.
    /// </summary>
    public static void MovementBudgetQueuesWaitsAndArrives()
    {
        var (world, _, _, _, runners) = MovementWorld(MovementBudget, ApartStarts);
        OrderAcross(world, runners);
        var budget = SimulationWorld.PathCellBudget;
        var offFrameWork = 0;
        var arrived = new HashSet<uint>();
        var waited = 0;
        var maxCells = 0L;
        var lastCells = 0L;
        for (var tick = 0; tick < world.Frames(900) && arrived.Count < runners.Length; tick++)
        {
            var result = world.Step();
            foreach (var simEvent in result.Events) if (simEvent.Kind == SimEventKind.MoveArrived) arrived.Add(simEvent.Entity.Value);
            maxCells = Math.Max(maxCells, world.PathStats.Cells - lastCells);
            // The queue works only in a tick on a frame boundary (the tick just stepped is world.Tick - 1).
            if (world.PathStats.Cells != lastCells && !world.Time.IsOriginalFrameBoundary(world.Tick - 1)) offFrameWork++;
            lastCells = world.PathStats.Cells;
            foreach (var runner in runners)
            {
                var ai = runner.Modules.OfType<AIUpdateInterface>().Single();
                if (!ai.WaitingForPath) continue;
                waited++;
                Check.That(runner.HasMoveTarget, "A runner that waits for its path keeps its order.");
            }
        }
        Check.That(world.PathStats.TicksWithWorkLeft > 0 && waited > 0, $"The searches did not fit one tick: {world.PathStats.TicksWithWorkLeft} ticks ended with work left, {waited} runner-ticks of waiting.");
        Check.That(maxCells <= budget, $"No frame worked more cells than its budget ({maxCells} of {budget}).");
        Check.That(offFrameWork == 0, $"The queue worked only in the last tick of a frame ({offFrameWork} other ticks).");
        Check.That(arrived.Count == runners.Length, $"Every runner arrived ({arrived.Count} of {runners.Length}).");

        var (original, _, _, _, others) = MovementWorld(RuleProfile.Original, ApartStarts);
        OrderAcross(original, others);
        for (var tick = 0; tick < original.Frames(60); tick++) original.Step();
        Check.That(original.PathStats.Requests == 0 && others.All(runner => !runner.Modules.OfType<AIUpdateInterface>().Single().WaitingForPath), "The original profile does not use the queue.");
    }

    /// <summary>A group of runners standing together and ordered to one place shares one search: the followers walk the leader's path and every runner arrives.</summary>
    public static void MovementBudgetSharesTheGroupPath()
    {
        (int X, int Y)[] together = [(50, 50), (62, 50), (74, 50), (50, 62), (62, 62), (74, 62)];
        var (world, _, _, _, runners) = MovementWorld(MovementBudget, together);
        Check.That(world.Submit(new GroupMoveCommand(new PlayerId(1), 1000, world.Tick, [..runners.Select(runner => runner.Id)], Fixed.FromInt(2000), Fixed.FromInt(100), false)).Accepted, "The group order is accepted.");
        var arrived = new HashSet<uint>();
        for (var tick = 0; tick < world.Frames(900) && arrived.Count < runners.Length; tick++)
            foreach (var simEvent in world.Step().Events) if (simEvent.Kind == SimEventKind.MoveArrived) arrived.Add(simEvent.Entity.Value);
        Check.That(world.PathStats.Shared >= 4, $"The followers share the leader's search ({world.PathStats.Shared} shared of {world.PathStats.Requests} requests).");
        Check.That(arrived.Count == runners.Length, $"Every runner of the group arrived ({arrived.Count} of {runners.Length}).");
    }

    /// <summary>
    /// The queue is the same at every rate: eight runners whose searches do not fit one frame, and a group sharing a path, arrive in the same logic frame of the original at
    /// 30, 60 and 120 Hz (the queue works once per frame, in the order of the requesters' ids).
    /// </summary>
    public static void MovementBudgetSameAtEveryRate()
    {
        (int X, int Y)[] together = [(50, 50), (62, 50), (74, 50), (50, 62), (62, 62), (74, 62)];
        string? reference = null;
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var (world, _, _, _, runners) = MovementWorldAt(rate, MovementBudget, [..ApartStarts, ..together.Select(place => (place.X + 600, place.Y + 400))]);
            while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
            OrderAcross(world, runners[..ApartStarts.Length]);
            Check.That(world.Submit(new GroupMoveCommand(new PlayerId(1), 2000, world.Tick, [..runners[ApartStarts.Length..].Select(runner => runner.Id)], Fixed.FromInt(2000), Fixed.FromInt(1400), false)).Accepted, "The group order is accepted.");
            var start = world.Tick;
            var arrived = new SortedDictionary<uint, long>();
            for (var tick = 0; tick < world.Frames(1200) && arrived.Count < runners.Length; tick++)
                foreach (var simEvent in world.Step().Events)
                    if (simEvent.Kind == SimEventKind.MoveArrived && !arrived.ContainsKey(simEvent.Entity.Value)) arrived[simEvent.Entity.Value] = world.Time.ToOriginalFrames(world.Tick - 1 - start);
            Check.That(arrived.Count == runners.Length, $"{rate}: every runner arrived ({arrived.Count} of {runners.Length}).");
            var text = string.Join(", ", arrived.Values);
            if (reference is null) reference = text;
            else Check.That(text == reference, $"{rate}: the runners arrive in the frames of 30 Hz ({text} against {reference}).");
        }
    }

    /// <summary>A world saved while a search is paused in the queue continues hash-identically: the restored world runs the search again up to where it was.</summary>
    public static void MovementBudgetContinuesFromASnapshotMidSearch()
    {
        var (world, _, fixture, terrain, runners) = MovementWorld(MovementBudget, ApartStarts);
        OrderAcross(world, runners);
        var steps = 0;
        while (world.PathStats.TicksWithWorkLeft == 0 && steps++ < world.Frames(60)) world.Step();
        Check.That(world.PathStats.TicksWithWorkLeft > 0 && runners.Any(runner => runner.Modules.OfType<AIUpdateInterface>().Single().WaitingForPath), "A search is paused in the queue.");
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, terrain);
        Check.That(restored.StateHash == world.StateHash, "The restored world has the saved one's hash.");
        for (var tick = 0; tick < world.Frames(600); tick++)
        {
            var expected = world.Step().StateHash;
            var actual = restored.Step().StateHash;
            if (expected != actual) throw new InvalidOperationException($"The restored world differs after {tick + 1} ticks ({actual:X16} instead of {expected:X16}).");
        }
    }
}
