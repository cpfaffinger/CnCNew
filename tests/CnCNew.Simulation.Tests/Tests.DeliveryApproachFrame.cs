using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
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
}
