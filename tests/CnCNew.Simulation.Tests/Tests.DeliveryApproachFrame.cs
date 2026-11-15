using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>
/// #305: <c>DeliverPayloadAIUpdate</c>'s approach asks once per logic frame of the original whether the plane is close enough to its target (<c>isCloseEnoughToTarget</c>, which
/// remembers the distance to tell an inbound plane). Above 30 Hz it asked in every tick, in the second from the middle of the frame's step (Death Valley ChinaTank against AirForce,
/// a cargo plane's remembered distance at frame 16,273).
/// </summary>
static partial class Tests
{
    /// <summary>
    /// A plane carries a bomb to a target from four starting places: its doors open and the bomb leaves in the same frame of the plane and at the same place at 30, 60 and 120 Hz.
    /// </summary>
    public static void ADeliveryApproachDecidesOncePerFrame()
    {
        var reference = new Dictionary<int, (long Open, long Drop, Fixed X)>();
        foreach (var (rate, _) in WorldRates)
            for (var start = 0; start <= 3; start++)
            {
                var fixture = new SyntheticFixture(rate, files: LastBlockerFiles());
                ImmutableArray<PlayerConfiguration> players = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta")];
                var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, players, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
                var feeder = new Feeder(world);
                var bomber = CsSpawn(world, feeder, "LbBomber", 1, start, 0);
                var bomb = CsSpawn(world, feeder, "LbDumbBomb", 1, start, 0);
                while (world.Tick < world.Time.OriginalFrames(5)) world.Step();
                Check.That(world.TryEnterContainer(bomb, bomber), $"{rate}: the bomb is loaded into the plane.");
                var deliver = bomber.Modules.OfType<DeliverPayloadAIUpdate>().Single();
                deliver.DeliverViaModuleData(world, Fixed.FromInt(400), Fixed.Zero);
                long open = -1, drop = -1;
                var dropX = Fixed.Zero;
                while (drop < 0 && world.Tick < world.Time.OriginalFrames(400))
                {
                    var tick = world.Tick;
                    world.Step();
                    // Counted in the plane's own frames (it came into being at a tick of its own).
                    var frame = world.Time.ToOriginalFrames(tick - bomber.SpawnTick);
                    if (open < 0 && deliver.DoorsOpen) open = frame;
                    if (drop < 0 && !bomb.ContainedBy.IsValid) (drop, dropX) = (frame, bomb.X);
                }
                Check.That(open > 0 && drop >= open, $"{rate}, start {start}: the plane opens its doors (frame {open}) and drops the bomb (frame {drop}).");
                if (!reference.TryGetValue(start, out var expected)) reference[start] = (open, drop, dropX);
                else Check.That((open, drop, dropX) == expected,
                    $"{rate}, start {start}: the doors open in frame {open} and the bomb leaves in frame {drop} at {dropX.ToDecimal():0.000} (30 Hz: {expected.Open}, {expected.Drop}, {expected.X.ToDecimal():0.000}).");
            }
    }

    /// <summary>
    /// A plane flies its delivery over rising ground: it holds its height over the ground where its last frame ended, and stands at the same height at the end of every one of its
    /// frames at 30, 60 and 120 Hz (Death Valley America against China, a cargo plane's height at frame 12,133).
    /// </summary>
    public static void ADeliveryPlaneClimbsOncePerFrame()
    {
        List<(Fixed X, Fixed Y, Fixed Z)>? reference = null;
        foreach (var (rate, factor) in WorldRates)
        {
            var fixture = new SyntheticFixture(rate, files: LastBlockerFiles());
            var terrain = NavTerrain(height: (x, y) => x * 4 + y * 2);
            ImmutableArray<PlayerConfiguration> players = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta")];
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, players, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
            var feeder = new Feeder(world);
            var bomber = CsSpawn(world, feeder, "LbBomber", 1, 10, 30);
            var bomb = CsSpawn(world, feeder, "LbDumbBomb", 1, 10, 30);
            while (world.Tick < world.Time.OriginalFrames(5)) world.Step();
            Check.That(world.TryEnterContainer(bomb, bomber), $"{rate}: the bomb is loaded into the plane.");
            bomber.Modules.OfType<DeliverPayloadAIUpdate>().Single().DeliverViaModuleData(world, Fixed.FromInt(180), Fixed.FromInt(160));
            var heights = new List<(Fixed X, Fixed Y, Fixed Z)>();
            // Over the map and beyond its edge (frame 65), where the plane is deleted (#371).
            for (var frame = 0; frame < 90; frame++)
            {
                for (var tick = 0; tick < factor; tick++) world.Step();
                heights.Add((bomber.X, bomber.Y, bomber.Z));
            }
            Check.That(heights.Select(value => value.Z).Distinct().Count() > 10, $"{rate}: the plane climbs with the ground.");
            if (reference is null)
            {
                reference = heights;
                continue;
            }
            var first = Enumerable.Range(0, heights.Count).FirstOrDefault(index => heights[index] != reference[index], -1);
            Check.That(first < 0, $"{rate}: the plane's height is the 30 Hz one at the end of every frame (first difference in frame {first}: " +
                $"{(first < 0 ? "-" : $"{heights[first].X.ToDecimal():0.000},{heights[first].Y.ToDecimal():0.000},{heights[first].Z.ToDecimal():0.000} against {reference[first].X.ToDecimal():0.000},{reference[first].Y.ToDecimal():0.000},{reference[first].Z.ToDecimal():0.000}")}).");
        }
    }

    /// <summary>
    /// #371: a plane that has delivered heads off the map and is deleted once it is beyond the edge (<c>HeadOffMapState::update</c> and <c>CleanUpState::onEnter</c>); a plane
    /// that misses its target and overshoots the map on its new approach comes back at the edge (<c>ConsiderNewApproachState</c>'s off-map condition,
    /// <c>RecoverFromOffMapState::update</c>), and leaves for good after its last attempt. Each plane stands where the 30 Hz plane stands at the end of every frame and is removed
    /// in the same frame of the plane, at 30, 60 and 120 Hz, from four starting places that put the crossings of the edge at other shares of a frame.
    /// </summary>
    public static void ADeliveryPlaneLeavesTheMapAsAt30Hz()
    {
        PlaneLeavesTheMap(recovers: false);
        PlaneLeavesTheMap(recovers: true);
    }

    private static void PlaneLeavesTheMap(bool recovers)
    {
        var reference = new Dictionary<int, List<(bool Alive, Fixed X, Fixed Y, Fixed Z)>>();
        var length = recovers ? 400 : 90;
        foreach (var (rate, factor) in WorldRates)
            for (var start = 0; start <= 3; start++)
            {
                var fixture = new SyntheticFixture(rate, files: LastBlockerFiles());
                var terrain = NavTerrain(height: (x, y) => x * 4 + y * 2);
                ImmutableArray<PlayerConfiguration> players = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta")];
                var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, players, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
                var feeder = new Feeder(world);
                var y = recovers ? 100 : 30;
                var bomber = CsSpawn(world, feeder, "LbBomber", 1, 10 + start, y);
                var bomb = CsSpawn(world, feeder, "LbDumbBomb", 1, 10 + start, y);
                while (world.Tick < world.Time.OriginalFrames(5)) world.Step();
                Check.That(world.TryEnterContainer(bomb, bomber), $"{rate}: the bomb is loaded into the plane.");
                var deliver = bomber.Modules.OfType<DeliverPayloadAIUpdate>().Single();
                if (!recovers) deliver.DeliverViaModuleData(world, Fixed.FromInt(180), Fixed.FromInt(160));
                else
                {
                    // Sent to a point the target lies far beside: the plane arrives without coming close, flies on beyond the edge and comes back, twice.
                    var run = new DeliveryRun(Fixed.FromInt(10), Fixed.Zero, 2, world.Time.OriginalFrames(100), Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero,
                        false, 0, 0, string.Empty, string.Empty, false, false, Fixed.Zero, Fixed.Zero, false, Fixed.Zero);
                    deliver.DeliverPayload(world, new DeliveryOrder(Fixed.FromInt(120), Fixed.FromInt(100), Fixed.FromInt(120), Fixed.FromInt(190), run, 0));
                }
                var frames = new List<(bool Alive, Fixed X, Fixed Y, Fixed Z)>();
                var outside = 0;
                for (var frame = 0; frame < length; frame++)
                {
                    for (var tick = 0; tick < factor; tick++) world.Step();
                    frames.Add((world.FindEntity(bomber.Id) is not null, bomber.X, bomber.Y, bomber.Z));
                    if (frames[^1].Alive && (bomber.X < Fixed.Zero || bomber.X > terrain.ExtentX)) outside++;
                }
                var gone = frames.FindIndex(value => !value.Alive);
                Check.That(gone > 0, $"{rate}, start {start}: the plane leaves the map and is deleted (first frame without it: {gone}).");
                Check.That(!recovers || outside > 1, $"{rate}, start {start}: the plane is beyond the edge at the end of {outside} frames before its last.");
                if (!reference.TryGetValue(start, out var expected))
                {
                    reference[start] = frames;
                    continue;
                }
                var first = Enumerable.Range(0, frames.Count).FirstOrDefault(index => frames[index] != expected[index], -1);
                Check.That(first < 0, $"{rate}, start {start}: the plane is where the 30 Hz plane is at the end of every frame and is deleted in frame {gone} " +
                    $"(30 Hz: {expected.FindIndex(value => !value.Alive)}; first difference in frame {first}: " +
                    $"{(first < 0 ? "-" : $"{frames[first].Alive} {frames[first].X.ToDecimal():0.000},{frames[first].Y.ToDecimal():0.000},{frames[first].Z.ToDecimal():0.000} against {expected[first].Alive} {expected[first].X.ToDecimal():0.000},{expected[first].Y.ToDecimal():0.000},{expected[first].Z.ToDecimal():0.000}")}).");
            }
    }
}
