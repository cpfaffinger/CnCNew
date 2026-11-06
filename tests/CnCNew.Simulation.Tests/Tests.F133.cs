using System.Collections.Immutable;
using CnCNew.Data.World;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// F-133: a train placed in the world's first tick, as a map's train is, on a track that bends over uneven ground and through a tunnel. Above 30 Hz the
/// locomotive began its frames a tick late (it waited one tick for its carriages, not one frame), a carriage turned about where the frame's last tick had left
/// it instead of where the frame began, and a carriage that ended a frame in the tunnel kept the height of the frame's last tick.
/// </summary>
static partial class Tests
{
    private static TerrainMap F133Terrain()
    {
        var waypoints = new (string Name, decimal X, decimal Y, decimal Z)[]
        {
            ("RailA", 50m, 100m, 0m), ("RailB", 200m, 110m, 0m), ("RailCTunnel", 300m, 190m, 0m), ("RailD", 400m, 130m, 0m), ("RailE", 560m, 210m, 0m),
        };
        var map = SyntheticMaps.Build(60, 40, 1, height: (x, y) => (x * 7 + y * 3) % 40, waypoints: waypoints);
        var links = ImmutableArray.CreateBuilder<MapWaypointLink>();
        links.Add(new MapWaypointLink(1, 2));
        links.Add(new MapWaypointLink(2, 3));
        links.Add(new MapWaypointLink(3, 4));
        links.Add(new MapWaypointLink(4, 5));
        return TerrainMap.Build(map with { WaypointLinks = links.ToImmutable() }, "f133");
    }

    private static SimulationWorld F133World(SimulationRate rate, out SyntheticFixture fixture, out TerrainMap terrain)
    {
        fixture = new SyntheticFixture(rate, files: StructuresFiles());
        terrain = F133Terrain();
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, StructuresPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
        RequestSpawn(world, fixture, "SwLoco", 0, 50, 100);
        return world;
    }

    public static void F133TrainPlacedAtTheStartIsTheSameAtEveryRate()
    {
        var tunnelFrames = new Dictionary<int, int>();
        AtThreeRates(rate =>
        {
            var world = F133World(rate, out _, out _);
            var series = new List<long>();
            var tunnel = 0;
            for (var frame = 0; frame < 700; frame++)
            {
                for (var tick = 0; tick < world.Time.TicksPerOriginalFrame; tick++) world.Step();
                long mixed = 17;
                foreach (var entity in world.Entities.Where(entity => entity.Template.Name is "SwLoco" or "SwCar").OrderBy(entity => entity.Id.Value))
                {
                    var rail = entity.Modules.OfType<RailroadBehavior>().Single();
                    if (rail.IsWaitingInWings || rail.IsEndOfLine) continue;
                    if (rail.InTunnel) tunnel++;
                    mixed = unchecked(mixed * 1_000_003 + entity.X.Raw * 31 + entity.Y.Raw * 7 + entity.Z.Raw * 3 + entity.Angle.Raw);
                }
                series.Add(mixed);
            }
            tunnelFrames[rate.TicksPerSecond] = tunnel;
            return series;
        }, (reference, other, rate) =>
        {
            Check.That(tunnelFrames[30] > 20, "Setup: carriages run through the tunnel at 30 Hz (" + tunnelFrames[30] + " carriage frames).");
            SameSeries(reference, other, "F-133: place, height and heading of the train at the end of every frame at " + rate);
        });

        // A save taken inside a frame of the train carries the turning point and the height of the frame's start.
        foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var world = F133World(rate, out var fixture, out var terrain);
            for (var tick = 0; tick < world.Frames(120) + 1; tick++) world.Step();
            var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, terrain);
            Check.That(restored.StateHash == world.StateHash, "F-133: a train saved inside a frame restores to the same state (" + rate.TicksPerSecond + " Hz).");
            for (var tick = 0; tick < world.Frames(200); tick++)
                Check.That(world.Step().StateHash == restored.Step().StateHash, "F-133: the restored train runs on identically at " + rate.TicksPerSecond + " Hz, step " + tick + ".");
        }
    }
}
