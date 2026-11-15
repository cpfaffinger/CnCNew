using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>
/// #305: <c>OCLUpdate::update</c> runs once per logic frame of the original. A reinforcement pad (<c>FactionTriggered</c>) captured in a frame's first tick after its update starts its
/// timer in its next frame at 30 Hz; above 30 Hz the update of the frame's second tick saw the new owner and drew the delay half a frame early, which parted the logic random streams
/// (Death Valley America against China, a tech reinforcement pad captured at frame 8,410).
/// </summary>
static partial class Tests
{
    /// <summary>
    /// A neutral pad changes hands where a capture after its update in a frame's first tick would put it: after the frame at 30 Hz, after the frame's first tick above. Its timer
    /// starts in the next frame, its first creation falls in the same frame, and the logic random stream is the same at the end, at 30, 60 and 120 Hz.
    /// </summary>
    public static void ACapturedPadStartsItsTimerInItsNextFrame()
    {
        var results = new List<(long Started, long Next, long Created, string Random)>();
        foreach (var (rate, factor) in WorldRates)
        {
            var fixture = new SyntheticFixture(rate, files: StructuresFiles());
            var terrain = StructuresTerrain();
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, StructuresPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
            Check.That(fixture.Templates.TryGet("SwPad", out var info), "Setup: the pad template.");
            var id = world.RequestSpawn(info!.Index, PlayerId.System, CnCNew.Simulation.Numerics.Fixed.FromInt(300), CnCNew.Simulation.Numerics.Fixed.FromInt(200),
                CnCNew.Simulation.Numerics.Fixed.Zero, CnCNew.Simulation.Numerics.Fixed.Zero);
            while (world.Tick < world.Time.OriginalFrames(20)) world.Step();
            // The capture: after the frame's first tick (all of the frame at 30 Hz).
            world.Step();
            Check.That(world.TransferOwnership(id, new PlayerId(1)), $"{rate}: the pad changes hands.");
            var pad = world.FindEntity(id)!.Modules.OfType<OCLUpdate>().Single();
            while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
            for (var tick = 0; tick < factor; tick++) world.Step();
            var started = world.Time.ToOriginalFrames(pad.TimerStartedTick);
            var due = world.Time.ToOriginalFrames(pad.NextCreationTick);
            long created = -1;
            while (created < 0 && world.Tick < world.Time.OriginalFrames(120))
            {
                var tick = world.Tick;
                world.Step();
                if (world.Entities.Any(entity => entity.Template.Name == "SwDebris")) created = world.Time.ToOriginalFrames(tick);
            }
            var random = new uint[6];
            world.LogicRandom.CopyState(random);
            results.Add((started, due, created, string.Join(",", random)));
            System.Console.WriteLine($"       {rate}: the timer starts in frame {results[^1].Started}, the list is due in frame {results[^1].Next} and runs in frame {created}");
            Check.That(started == 21 && created == due, $"{rate}: the pad starts its timer in the frame after its capture (frame {started}) and creates when it is due (frame {created}, due {due}).");
        }
        Check.That(results.Distinct().Count() == 1, "The timer, the creation and the logic random stream are the same at every rate.");
    }
}
