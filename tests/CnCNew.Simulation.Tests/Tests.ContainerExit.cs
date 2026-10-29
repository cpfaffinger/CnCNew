using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Leaving a transport (fidelity finding F-03, <c>docs/SIMULATION-CORE.md</c> §44): <c>OpenContain::exitObjectViaDoor</c> for the riders of a living transport - one after
/// the other through the door (<c>ExitDelay</c>), from the <c>ExitStart</c> bone to a place of their own near the <c>ExitEnd</c> bone - and
/// <c>OpenContain::scatterToNearbyPosition</c> for the riders of a destroyed one.
/// </summary>
internal static partial class Tests
{
    private static IReadOnlyDictionary<string, string> ContainerExitFiles() => new Dictionary<string, string>(TransportFiles())
    {
        ["Data/INI/Object/ContainerExitFixture.ini"] = """
        Object DoorTruck
          KindOf = VEHICLE SELECTABLE TRANSPORT
          Geometry = CYLINDER
          GeometryMajorRadius = 12.0
          GeometryMinorRadius = 12.0
          GeometryHeight = 10.0
          Locomotor = SET_NORMAL WalkLoco
          Body = ActiveBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          Behavior = TransportAIUpdate AI01
          End
          Behavior = TransportContain Transport01
            Slots = 5
            ExitDelay = 250
            NumberOfExitPaths = 3
            ScatterNearbyOnExit = No
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object PlainTruck
          KindOf = VEHICLE SELECTABLE TRANSPORT
          Locomotor = SET_NORMAL WalkLoco
          Body = ActiveBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          Behavior = TransportAIUpdate AI01
          End
          Behavior = TransportContain Transport01
            Slots = 5
            NumberOfExitPaths = 0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    private static ModelGeometry DoorTruckBones() => new([], [new KeyValuePair<string, ImmutableArray<ExitPath>>("DoorTruck",
    [
        new ExitPath(new BonePose(Fixed.Zero, Fixed.FromInt(8), Fixed.Zero, Fixed.Zero), new BonePose(Fixed.Zero, Fixed.FromInt(60), Fixed.Zero, Fixed.Zero)),
        new ExitPath(new BonePose(Fixed.Zero, Fixed.FromInt(-8), Fixed.Zero, Fixed.Zero), new BonePose(Fixed.Zero, Fixed.FromInt(-60), Fixed.Zero, Fixed.Zero)),
        new ExitPath(new BonePose(Fixed.FromInt(-10), Fixed.Zero, Fixed.Zero, Fixed.Zero), null),
    ])]);

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture, TemplateCatalog Templates) ContainerExitWorld(SimulationRate rate, ModelGeometry geometry, string truckName, int riders)
    {
        var fixture = new SyntheticFixture(rate, files: ContainerExitFiles());
        var templates = TemplateCatalog.Build(fixture.Graph, fixture.Bindings, rate, geometry);
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 11, CombatPlayers, SyntheticFixture.FixtureId, geometry.Combine(fixture.Fingerprint), string.Empty), templates, null);
        var feeder = new Feeder(world);
        feeder.Spawn(1, truckName, x: 200, y: 200);
        for (var index = 0; index < riders; index++) feeder.Spawn(1, "Grunt", x: 300 + 30 * index, y: 300);
        for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
        var truck = world.Entities.Single(value => value.Template.Name == truckName);
        foreach (var grunt in world.Entities.Where(value => value.Template.Name == "Grunt").OrderBy(value => value.Id.Value).ToArray())
            Check.That(world.TryEnterContainer(grunt, truck), $"{rate}: grunt {grunt.Id.Value} boards.");
        for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
        return (world, feeder, fixture, templates);
    }

    private static Entity[] Grunts(SimulationWorld world) => world.Entities.Where(value => value.Template.Name == "Grunt").OrderBy(value => value.Id.Value).ToArray();

    private static bool Apart(Entity a, Entity b)
    {
        var dx = a.X - b.X;
        var dy = a.Y - b.Y;
        var touching = a.Template.BoundingCircleRadius + b.Template.BoundingCircleRadius;
        return dx * dx + dy * dy > touching * touching;
    }

    /// <summary>
    /// One evacuation of four riders: per logic frame of the original the riders' state, and the frame each rider left in. With <paramref name="snapshotAfterTicks"/> a snapshot
    /// taken that many ticks after the order continues like the live world.
    /// </summary>
    private static (List<string> Frames, int[] Left) DoorRun(SimulationRate rate, ModelGeometry geometry, int frames, int snapshotAfterTicks = -1)
    {
        var (world, feeder, fixture, templates) = ContainerExitWorld(rate, geometry, "DoorTruck", 4);
        var truck = world.Entities.Single(value => value.Template.Name == "DoorTruck");
        var grunts = Grunts(world);
        Check.That(feeder.Evacuate(1, truck.Id.Value).Accepted, $"{rate}: the evacuation is ordered.");
        var trace = new List<string>();
        var left = Enumerable.Repeat(-1, grunts.Length).ToArray();
        SimulationWorld? restored = null;
        var ticks = 0;
        for (var frame = 0; frame < frames; frame++)
        {
            for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++)
            {
                world.Step();
                world.VerifyIncrementalHash();
                ticks++;
                if (restored is not null)
                {
                    restored.Step();
                    Check.That(restored.StateHash == world.StateHash, $"{rate}: live and restored world agree in frame {frame + 1}, tick {tick}.");
                }
                if (ticks == snapshotAfterTicks)
                {
                    var bytes = WorldSnapshot.Write(world);
                    restored = WorldSnapshot.Restore(bytes, templates, geometry.Combine(fixture.Fingerprint), null);
                    Check.That(restored.StateHash == world.StateHash && WorldSnapshot.Write(restored).AsSpan().SequenceEqual(bytes), $"{rate}: a world restored while riders wait for the door is the live one.");
                }
            }
            for (var index = 0; index < grunts.Length; index++)
                if (left[index] < 0 && !grunts[index].ContainedBy.IsValid) left[index] = frame;
            trace.Add(string.Join(";", grunts.Select(grunt => $"{(grunt.ContainedBy.IsValid ? 1 : 0)},{grunt.X.Raw},{grunt.Y.Raw},{(grunt.HasMoveTarget ? 1 : 0)}")));
        }
        Check.That(snapshotAfterTicks < 0 || restored is not null, $"{rate}: the snapshot was taken.");
        return (trace, left);
    }

    /// <summary>Riders leave a living transport one by one, <c>ExitDelay</c> apart and lowest id first, and end beside the transport and beside each other.</summary>
    public static void ContainerExitRidersLeaveOneByOne()
    {
        var rate = TestRate.Default;
        var (world, feeder, _, _) = ContainerExitWorld(rate, ModelGeometry.None, "DoorTruck", 4);
        var truck = world.Entities.Single(value => value.Template.Name == "DoorTruck");
        var grunts = Grunts(world);
        Check.That(feeder.Evacuate(1, truck.Id.Value).Accepted, "The evacuation is ordered.");
        var left = new long[grunts.Length];
        var order = world.Tick;
        for (var tick = 0; tick < world.Frames(120); tick++)
        {
            world.Step();
            for (var index = 0; index < grunts.Length; index++)
                if (left[index] == 0 && !grunts[index].ContainedBy.IsValid) left[index] = world.Tick;
        }
        // ExitDelay = 250 ms is 8 logic frames (parseDurationUnsignedInt rounds up).
        var gaps = Enumerable.Range(1, grunts.Length - 1).Select(index => world.InFrames(left[index] - left[index - 1])).ToArray();
        Check.That(left.All(value => value > 0) && gaps.All(gap => gap == 8), $"Each rider leaves 8 frames after the one before, lowest id first: order at tick {order}, exits at {string.Join(", ", left)}.");
        Check.That(grunts.All(grunt => !grunt.HasMoveTarget), "Everybody has come to rest.");
        Check.That(grunts.All(grunt => Apart(grunt, truck)), $"Nobody stands inside the truck: {string.Join(" ", grunts.Select(grunt => $"({grunt.X},{grunt.Y})"))}.");
        for (var first = 0; first < grunts.Length; first++)
            for (var second = first + 1; second < grunts.Length; second++)
                Check.That(Apart(grunts[first], grunts[second]), $"Riders {first} and {second} do not stand inside each other.");
        Check.That(grunts.All(grunt => grunt.Angle == truck.Angle || !grunt.HasMoveTarget), "The riders came out facing the way the truck faces.");
    }

    /// <summary>The exit bones: every rider starts on the next path's <c>ExitStart</c> and walks to its <c>ExitEnd</c>; a missing bone is the container's position.</summary>
    public static void ContainerExitUsesTheExitBones()
    {
        var rate = TestRate.Default;
        var (world, feeder, _, _) = ContainerExitWorld(rate, DoorTruckBones(), "DoorTruck", 4);
        var truck = world.Entities.Single(value => value.Template.Name == "DoorTruck");
        var grunts = Grunts(world);
        Check.That(feeder.Evacuate(1, truck.Id.Value).Accepted, "The evacuation is ordered.");
        var starts = new (Fixed X, Fixed Y)?[grunts.Length];
        for (var tick = 0; tick < world.Frames(200); tick++)
        {
            for (var index = 0; index < grunts.Length; index++)
                if (starts[index] is null && grunts[index].ContainedBy.IsValid) starts[index] = (Fixed.MinValue, Fixed.MinValue);
            world.Step();
            for (var index = 0; index < grunts.Length; index++)
                if (starts[index] is { X: var x } && x == Fixed.MinValue && !grunts[index].ContainedBy.IsValid) starts[index] = (grunts[index].X, grunts[index].Y);
        }
        // The truck faces +x (angle 0): a bone at (bx, by) lies at (200 + bx, 200 + by). The riders move in the tick they come out, so the start is read within one step (speed 1 per frame).
        static bool Near((Fixed X, Fixed Y)? point, int x, int y) => point is { } p && (p.X - Fixed.FromInt(x)).Abs() <= Fixed.FromInt(2) && (p.Y - Fixed.FromInt(y)).Abs() <= Fixed.FromInt(2);
        Check.That(Near(starts[0], 200, 208) && Near(starts[1], 200, 192) && Near(starts[2], 190, 200) && Near(starts[3], 200, 208),
            $"The riders start on ExitStart01, 02, 03 and 01 again: {string.Join(" ", starts.Select(start => $"({start?.X},{start?.Y})"))}.");
        Check.That(grunts[0].X == Fixed.FromInt(200) && grunts[0].Y == Fixed.FromInt(260), $"The first rider stands on ExitEnd01 ({grunts[0].X}, {grunts[0].Y}).");
        Check.That(grunts[1].X == Fixed.FromInt(200) && grunts[1].Y == Fixed.FromInt(140), $"The second rider stands on ExitEnd02 ({grunts[1].X}, {grunts[1].Y}).");
        Check.That(Apart(grunts[2], truck) && Apart(grunts[3], grunts[0]) && grunts.All(grunt => !grunt.HasMoveTarget),
            "The third rider (no ExitEnd03 bone: the truck's position) and the fourth (ExitEnd01 is taken) stand on places of their own.");

        // A table that disagrees with the module is refused when the door first needs it.
        var wrong = new ModelGeometry([], [new KeyValuePair<string, ImmutableArray<ExitPath>>("DoorTruck", [new ExitPath(null, null)])]);
        var (other, otherFeeder, _, _) = ContainerExitWorld(rate, wrong, "DoorTruck", 1);
        var otherTruck = other.Entities.Single(value => value.Template.Name == "DoorTruck");
        otherFeeder.Evacuate(1, otherTruck.Id.Value);
        Check.Throws<System.IO.InvalidDataException>(() => { for (var tick = 0; tick < 8; tick++) other.Step(); }, "exit paths");
    }

    /// <summary><c>NumberOfExitPaths = 0</c>: the rider is put on the container and stays there, as in the original.</summary>
    public static void ContainerExitWithoutExitPathsStaysOnTheContainer()
    {
        var (world, feeder, _, _) = ContainerExitWorld(TestRate.Default, ModelGeometry.None, "PlainTruck", 2);
        var truck = world.Entities.Single(value => value.Template.Name == "PlainTruck");
        Check.That(feeder.Evacuate(1, truck.Id.Value).Accepted, "The evacuation is ordered.");
        for (var tick = 0; tick < world.Frames(30); tick++) world.Step();
        Check.That(Grunts(world).All(grunt => !grunt.ContainedBy.IsValid && grunt.X == truck.X && grunt.Y == truck.Y && !grunt.HasMoveTarget), "Without exit paths (and without ExitDelay) both riders are out at once, on the truck's position.");
    }

    /// <summary>The riders of a destroyed transport are scattered: one to one and a half bounding radii away, each with its own two draws.</summary>
    public static void ContainerExitDeadTransportScattersItsRiders()
    {
        var (world, _, _, _) = ContainerExitWorld(TestRate.Default, ModelGeometry.None, "DoorTruck", 3);
        var truck = world.Entities.Single(value => value.Template.Name == "DoorTruck");
        var grunts = Grunts(world);
        var (truckX, truckY) = (truck.X, truck.Y);
        var radius = truck.Template.BoundingCircleRadius;
        world.Kill(truck.Id);
        world.Step();
        Check.That(grunts.All(grunt => !grunt.ContainedBy.IsValid && !grunt.EffectivelyDead), "All riders are out in the tick of the death, alive (no DamagePercentToUnits).");
        Check.That(grunts.All(grunt => grunt.HasMoveTarget), "Each of them walks to its scatter point.");
        foreach (var grunt in grunts)
        {
            var dx = grunt.MoveTargetX - truckX;
            var dy = grunt.MoveTargetY - truckY;
            var distance = Fixed.Sqrt(dx * dx + dy * dy);
            Check.That(distance >= radius - Fixed.FromFraction(1, 100) && distance <= radius + radius / Fixed.FromInt(2) + Fixed.FromFraction(1, 100), $"Scatter point {distance} from the truck, between {radius} and {radius + radius / Fixed.FromInt(2)}.");
        }
        Check.That(grunts.Select(grunt => (grunt.MoveTargetX.Raw, grunt.MoveTargetY.Raw)).Distinct().Count() == grunts.Length, "Every rider drew its own point.");
        for (var tick = 0; tick < world.Frames(60); tick++) world.Step();
        Check.That(grunts.All(grunt => !grunt.HasMoveTarget && (grunt.X != truckX || grunt.Y != truckY)), "They walked there.");
    }

    /// <summary>The same evacuation at 30, 60 and 120 Hz: the same frames of exit, the same places at the end of every logic frame; a snapshot in mid-exit continues identically.</summary>
    public static void ContainerExitIsTheSameAtEveryRate()
    {
        foreach (var geometry in new[] { ModelGeometry.None, DoorTruckBones() })
        {
            var reference = DoorRun(SimulationRate.Hz30, geometry, 150, snapshotAfterTicks: 5);
            Check.That(reference.Left.SequenceEqual(new[] { 0, 8, 16, 24 }), $"30 Hz: the riders leave in frames 0, 8, 16 and 24 after the order ({string.Join(", ", reference.Left)}).");
            foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
            {
                var run = DoorRun(rate, geometry, 150, snapshotAfterTicks: 5 * rate.TicksPerOriginalFrame + 1);
                Check.That(run.Left.SequenceEqual(reference.Left), $"{rate}: the riders leave in the frames of 30 Hz ({string.Join(", ", run.Left)}).");
                var first = Enumerable.Range(0, reference.Frames.Count).FirstOrDefault(frame => run.Frames[frame] != reference.Frames[frame], -1);
                Check.That(first < 0, $"{rate}: the riders are where 30 Hz has them at the end of every logic frame (first difference in frame {first + 1}: {(first < 0 ? "" : run.Frames[first] + " against " + reference.Frames[first])}).");
            }
        }
    }

    /// <summary>
    /// F-71: riders leave a transport that keeps turning (a new place to drive to in another direction every frame) one by one through its doors. The door's bones turn with the
    /// transport where its frame ends - above 30 Hz the rider that the transport let out in its own update met a heading that had turned only one share of the frame and walked to another
    /// place (a ranger left an ambulance in a match). Every rider leaves in the frame and for the place 30 Hz has.
    /// </summary>
    public static void RiderLeavesATurningTransportWhereTheFrameEnds()
    {
        List<string>? reference = null;
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            // On ground with a navigation grid: a mover without one drives straight and does not turn.
            var geometry = DoorTruckBones();
            var fixture = new SyntheticFixture(rate, files: ContainerExitFiles());
            var templates = TemplateCatalog.Build(fixture.Graph, fixture.Bindings, rate, geometry);
            var terrain = TerrainMap.Build(SyntheticMaps.Build(60, 60, 1, (_, _) => 0), "turning");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 11, CombatPlayers, SyntheticFixture.FixtureId, geometry.Combine(fixture.Fingerprint), terrain.Fingerprint), templates, terrain);
            var feeder = new Feeder(world);
            feeder.Spawn(1, "DoorTruck", x: 300, y: 300);
            for (var index = 0; index < 4; index++) feeder.Spawn(1, "Grunt", x: 400 + 30 * index, y: 400);
            for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
            var truck = world.Entities.Single(value => value.Template.Name == "DoorTruck");
            var grunts = Grunts(world);
            foreach (var grunt in grunts) Check.That(world.TryEnterContainer(grunt, truck), $"{rate}: grunt {grunt.Id.Value} boards.");
            for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
            foreach (var grunt in grunts) Check.That(feeder.ExitContainer(1, grunt.Id.Value).Accepted, $"{rate}: grunt {grunt.Id.Value} is told to get out.");
            var left = new List<string>();
            var angles = new HashSet<long>();
            for (var frame = 0; frame < 40; frame++)
            {
                // Eight compass points, five steps on each frame: the truck has to turn by 135 degrees every frame.
                (int X, int Y)[] points = [(300, 0), (212, 212), (0, 300), (-212, 212), (-300, 0), (-212, -212), (0, -300), (212, -212)];
                var (px, py) = points[frame * 3 % 8];
                Check.That(feeder.SetMoveTarget(1, truck.Id.Value, 300 + px, 300 + py).Accepted,
                    $"{rate}: the truck is sent another way in frame {frame}.");
                for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
                angles.Add(truck.Angle.Raw);
                foreach (var grunt in grunts)
                    if (!grunt.ContainedBy.IsValid && !left.Any(line => line.StartsWith($"{grunt.Id.Value}:", StringComparison.Ordinal)))
                        left.Add($"{grunt.Id.Value}: frame {frame} target {grunt.MoveTargetX.Raw},{grunt.MoveTargetY.Raw}");
            }
            Check.That(left.Count == 4 && angles.Count > 10, $"{rate}: all four riders are out ({left.Count}) of a truck that turned ({angles.Count} headings).");
            if (reference is null)
            {
                reference = left;
                continue;
            }
            for (var index = 0; index < left.Count; index++)
                Check.That(left[index] == reference[index], $"{rate}: rider {index} leaves as at 30 Hz ({left[index]} against {reference[index]}).");
        }
    }
}
