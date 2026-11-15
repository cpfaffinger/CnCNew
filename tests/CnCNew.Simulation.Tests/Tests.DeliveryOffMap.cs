using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// #374: a delivery plane is off the map only beyond the map's border (<c>DeliverPayloadAIUpdate::isOffMap</c>, <c>getExtentIncludingBorder</c>).
/// </summary>
static partial class Tests
{
    private const int OffMapBorder = 5;
    private const int OffMapCells = 20;

    /// <summary>The ground of the plane tests: a ramp that rises 5 units per vertex towards the east edge, so the plane leaves the map over high ground.</summary>
    private static TerrainMap OffMapTerrain() => TerrainMap.Build(SyntheticMaps.Build(OffMapCells, OffMapCells, OffMapBorder, (x, _) => x * 8), "off-map");

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
}
