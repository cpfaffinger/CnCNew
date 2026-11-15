using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// #374: a delivery plane that leaves the map holds its height over the ground of the height field's edge (<c>BaseHeightMapRenderObjClass::getHeightMapHeight</c> clamps to the
/// field), is off the map only beyond the map's border (<c>DeliverPayloadAIUpdate::isOffMap</c>, <c>getExtentIncludingBorder</c>), and blows up when it has been turned away from
/// its exit (<c>HeadOffMapState::update</c>).
/// </summary>
static partial class Tests
{
    private const int OffMapBorder = 5;
    private const int OffMapCells = 20;

    /// <summary>The ground of the plane tests: a ramp that rises 5 units per vertex towards the east edge, so the plane leaves the map over high ground.</summary>
    private static TerrainMap OffMapTerrain() => TerrainMap.Build(SyntheticMaps.Build(OffMapCells, OffMapCells, OffMapBorder, (x, _) => x * 8), "off-map");

    /// <summary>
    /// The height field's own rule beyond its interpolated part: a point on the first vertex, the last two vertices or beyond reads its cell's corner vertex clamped to the field,
    /// without interpolation (<c>getClipHeight</c>); inside it the two triangles are interpolated.
    /// </summary>
    public static void TheGroundBeyondTheHeightFieldIsItsEdge()
    {
        // Border 1 and 4 cells: 6 vertices (0..5) a row, world (0, 0) is vertex (1, 1), heights x * 8 + y raw (0.625 units each).
        var terrain = TerrainMap.Build(SyntheticMaps.Build(4, 4, 1, (x, y) => x * 8 + y), "edge");
        Fixed Raw(int value) => Fixed.FromInt(value) * Fixed.FromDecimal(0.625m);
        // (15, 5) lies in the lower triangle of cell (2, 1): 25 + 0.5 * (26 - 25) + 0.5 * (17 - 25) = 21.5 raw.
        Check.That(terrain.HeightAt(Fixed.FromInt(15), Fixed.FromInt(5)) == Fixed.FromDecimal(21.5m) * Fixed.FromDecimal(0.625m), "Inside the field the triangles are interpolated.");
        // Vertex 4 (world 30..40) is the second last: its cell reads the corner (4, iy) without interpolation.
        Check.That(terrain.HeightAt(Fixed.FromInt(35), Fixed.FromInt(5)) == Raw(4 * 8 + 1), $"The second last column reads its corner vertex ({terrain.HeightAt(Fixed.FromInt(35), Fixed.FromInt(5)).ToDecimal()}).");
        Check.That(terrain.HeightAt(Fixed.FromInt(-5), Fixed.FromInt(15)) == Raw(0 * 8 + 2), "The first column reads its corner vertex.");
        Check.That(terrain.HeightAt(Fixed.FromInt(1000), Fixed.FromInt(15)) == Raw(5 * 8 + 2), "Beyond the field the edge vertex of the row is read.");
        Check.That(terrain.HeightAt(Fixed.FromInt(1000), Fixed.FromInt(-1000)) == Raw(5 * 8 + 0), "Beyond a corner the corner vertex is read.");
        Check.That(terrain.HeightAt(Fixed.FromInt(15), Fixed.FromInt(1000)) == Raw(2 * 8 + 5), "Beyond the far row the edge vertex of the column is read.");
    }

    /// <summary>One frame end of a plane: alive, where, and what it says about its height.</summary>
    private readonly record struct OffMapFrame(bool Alive, Fixed X, Fixed Y, Fixed Z);

    /// <summary>A plane that delivers a bomb east of its start and heads east off the map over the ramp; its frame ends, at the given rate.</summary>
    private static List<OffMapFrame> OffMapRun(SimulationRate rate, int factor, int start, int frames, System.Action<SimulationWorld, Entity, int>? atFrameEnd = null)
    {
        var fixture = new SyntheticFixture(rate, files: LastBlockerFiles());
        var terrain = OffMapTerrain();
        ImmutableArray<PlayerConfiguration> players = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta")];
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, players, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
        var feeder = new Feeder(world);
        var bomber = CsSpawn(world, feeder, "LbBomber", 1, 20 + start, 100);
        var bomb = CsSpawn(world, feeder, "LbDumbBomb", 1, 20 + start, 100);
        while (world.Tick < world.Time.OriginalFrames(5)) world.Step();
        Check.That(world.TryEnterContainer(bomb, bomber), $"{rate}: the bomb is loaded into the plane.");
        bomber.Modules.OfType<DeliverPayloadAIUpdate>().Single().DeliverViaModuleData(world, Fixed.FromInt(120), Fixed.FromInt(100));
        var result = new List<OffMapFrame>();
        for (var frame = 0; frame < frames; frame++)
        {
            for (var tick = 0; tick < factor; tick++) world.Step();
            result.Add(new OffMapFrame(world.FindEntity(bomber.Id) is { } alive && !alive.EffectivelyDead, bomber.X, bomber.Y, bomber.Z));
            atFrameEnd?.Invoke(world, bomber, frame);
        }
        return result;
    }

    private static string Describe(OffMapFrame frame) => $"{frame.Alive} {frame.X.ToDecimal():0.000},{frame.Y.ToDecimal():0.000},{frame.Z.ToDecimal():0.000}";

    /// <summary>
    /// A plane that crosses the east edge over high ground keeps its preferred height over the ground where its last frame ended - the height field's edge beyond the map, not 0
    /// (before, it dropped from about 185 to 60) - and stands where the 30 Hz plane stands at the end of every frame at 30, 60 and 120 Hz.
    /// </summary>
    public static void ADeliveryPlaneHoldsItsHeightBeyondTheMap()
    {
        var terrain = OffMapTerrain();
        var preferred = Fixed.FromInt(60);
        var reference = new Dictionary<int, List<OffMapFrame>>();
        foreach (var (rate, factor) in WorldRates)
            for (var start = 0; start <= 3; start++)
            {
                var frames = OffMapRun(rate, factor, start, 200);
                var beyond = 0;
                for (var index = 1; index < frames.Count; index++)
                {
                    var (before, now) = (frames[index - 1], frames[index]);
                    if (!now.Alive || before.X <= terrain.ExtentX) continue;
                    beyond++;
                    var expected = terrain.HeightAt(before.X, before.Y) + preferred;
                    Check.That(now.Z == expected, $"{rate}, start {start}: beyond the edge the plane flies {preferred.ToDecimal()} over the ground where its last frame ended " +
                        $"(frame {index}: {Describe(now)}, expected height {expected.ToDecimal():0.000}).");
                }
                Check.That(beyond > 0, $"{rate}, start {start}: the plane is beyond the edge at the end of {beyond} frames.");
                if (!reference.TryGetValue(start, out var expectedFrames))
                {
                    reference[start] = frames;
                    continue;
                }
                var first = Enumerable.Range(0, frames.Count).FirstOrDefault(index => frames[index] != expectedFrames[index], -1);
                Check.That(first < 0, $"{rate}, start {start}: the plane is where the 30 Hz plane is at the end of every frame (first difference in frame {first}: " +
                    $"{(first < 0 ? "-" : $"{Describe(frames[first])} against {Describe(expectedFrames[first])}")}).");
            }
    }
    /// <summary>
    /// A plane heading off the map is gone only when its frame begins beyond the map's border (<c>getExtentIncludingBorder</c>, 50 units here), not at the map's edge, and it is
    /// deleted in the same frame of the plane at 30, 60 and 120 Hz.
    /// </summary>
    public static void ADeliveryPlaneLeavesTheMapBeyondItsBorder()
    {
        var terrain = OffMapTerrain();
        var edge = Fixed.FromInt((terrain.VertexWidth - terrain.Border) * TerrainMap.CellSize);
        var reference = new Dictionary<int, int>();
        foreach (var (rate, factor) in WorldRates)
            for (var start = 0; start <= 3; start++)
            {
                var frames = OffMapRun(rate, factor, start, 200);
                var gone = frames.FindIndex(value => !value.Alive);
                Check.That(gone > 1, $"{rate}, start {start}: the plane leaves the map and is deleted (first frame without it: {gone}).");
                Check.That(frames[gone - 1].X >= edge && frames[gone - 2].X < edge,
                    $"{rate}, start {start}: the plane is deleted in the frame that begins beyond the border at {edge.ToDecimal()} (frame {gone}; before it {Describe(frames[gone - 2])}, {Describe(frames[gone - 1])}).");
                if (!reference.TryGetValue(start, out var expected)) reference[start] = gone;
                else Check.That(gone == expected, $"{rate}, start {start}: the plane is deleted in frame {gone} (30 Hz: {expected}).");
            }
    }
    /// <summary>
    /// A plane heading off the map that is turned away from its exit blows up (<c>HeadOffMapState::update</c>: it turned in its last frame and faces less than 0.3 of the way it
    /// headed off). The port has nothing that deflects a plane on its way out, so the test turns it round itself at the end of a frame (a stand-in for a collision); the plane is
    /// killed in the same frame after the turn at 30, 60 and 120 Hz, far inside the map. A plane that is not turned leaves the map as before.
    /// </summary>
    public static void ADeliveryPlaneTurnedOffItsExitBlowsUp()
    {
        var angle = typeof(Entity).GetProperty(nameof(Entity.Angle))!;
        var clearAhead = typeof(Entity).GetMethod("ClearAheadAngle", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        var reference = new Dictionary<int, (int Turned, int Gone)>();
        foreach (var (rate, factor) in WorldRates)
            for (var start = 0; start <= 3; start++)
            {
                var fixture = new SyntheticFixture(rate, files: LastBlockerFiles());
                var terrain = OffMapTerrain();
                ImmutableArray<PlayerConfiguration> players = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta")];
                var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, players, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
                var feeder = new Feeder(world);
                var bomber = CsSpawn(world, feeder, "LbBomber", 1, 20 + start, 100);
                var bomb = CsSpawn(world, feeder, "LbDumbBomb", 1, 20 + start, 100);
                while (world.Tick < world.Time.OriginalFrames(5)) world.Step();
                Check.That(world.TryEnterContainer(bomb, bomber), $"{rate}: the bomb is loaded into the plane.");
                // One pass, a drop every three frames: the frame after the drop finds the plane empty and sends it off the map, eastwards.
                var run = new DeliveryRun(Fixed.FromInt(40), Fixed.Zero, 1, world.Time.OriginalFrames(3), Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero,
                    false, 0, 0, string.Empty, string.Empty, false, false, Fixed.Zero, Fixed.Zero, false, Fixed.Zero);
                bomber.Modules.OfType<DeliverPayloadAIUpdate>().Single().DeliverPayload(world, new DeliveryOrder(Fixed.FromInt(120), Fixed.FromInt(100), Fixed.FromInt(120), Fixed.FromInt(100), run, 0));
                int dropped = -1, turned = -1, gone = -1;
                var where = Fixed.Zero;
                for (var frame = 0; frame < 200 && gone < 0; frame++)
                {
                    for (var tick = 0; tick < factor; tick++) world.Step();
                    if (world.FindEntity(bomber.Id) is not { EffectivelyDead: false })
                    {
                        gone = frame;
                        break;
                    }
                    if (dropped < 0 && !bomb.ContainedBy.IsValid) dropped = frame;
                    if (dropped >= 0 && turned < 0 && frame == dropped + 10)
                    {
                        turned = frame;
                        where = bomber.X;
                        clearAhead.Invoke(bomber, null);
                        angle.SetValue(bomber, FixedTrig.Normalize(bomber.Angle + FixedTrig.Pi));
                    }
                }
                Check.That(turned > 0 && gone > turned && gone <= turned + 3 && where < terrain.ExtentX,
                    $"{rate}, start {start}: the plane turned round at {where.ToDecimal():0.000} in frame {turned} blows up at once (gone in frame {gone}).");
                if (!reference.TryGetValue(start, out var expected)) reference[start] = (turned, gone);
                else Check.That((turned, gone) == expected, $"{rate}, start {start}: turned in frame {turned}, gone in frame {gone} (30 Hz: {expected.Turned}, {expected.Gone}).");
            }
    }
}
