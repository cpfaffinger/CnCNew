using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// The exit path of produced units (<c>docs/SIMULATION-CORE.md</c> §42) under the frame convention of <c>docs/RATE-INVARIANCE-CONSOLIDATION.md</c> §1.3: the place in front of the
/// factory is chosen after the whole logic frame of the original the unit appears in, the unit and whoever makes room for it move with the next frame. Every test names its
/// rates itself and compares 60 and 120 Hz with 30 Hz at the end of every logic frame.
/// </summary>
internal static partial class Tests
{
    /// <summary>
    /// A factory at (70, 100) builds three units one after the other; with <paramref name="rally"/> the owner set a rally point first. Returns the state of every unit after each
    /// logic frame of the original, the frame each unit appeared in, and, with <paramref name="snapshotAfterTicks"/>, checks that a snapshot taken that many ticks after the
    /// first unit appeared continues like the live world.
    /// </summary>
    private static (List<string> Frames, List<int> Born) ExitRun(SimulationRate rate, bool rally, int frames, int snapshotAfterTicks = -1)
    {
        var terrain = NavTerrain();
        var (world, feeder, fixture) = RatedWorld(rate, ConstructionFiles(), terrain);
        var stride = rate.TicksPerOriginalFrame;
        feeder.GiveMoney(1, 1000);
        feeder.Spawn(1, "ExitBarracks", x: 70, y: 100);
        for (var tick = 0; tick < stride; tick++) world.Step();
        var factory = ConstructionFind(world, "ExitBarracks").Id.Value;
        if (rally) Check.That(feeder.SetRallyPoint(1, factory, 170, 112).Accepted, $"{rate}: the rally point is accepted.");
        for (var unit = 0; unit < 3; unit++) Check.That(feeder.QueueProduction(1, factory, "Grunt").Accepted, $"{rate}: order {unit + 1} is accepted.");

        var trace = new List<string>();
        var born = new List<int>();
        SimulationWorld? restored = null;
        var sinceFirst = -1;
        for (var frame = 0; frame < frames; frame++)
        {
            for (var tick = 0; tick < stride; tick++)
            {
                world.Step();
                // Everything the exit writes (the order, the place chosen later, the second leg) reaches the tick hash: throws when a change missed MarkDirty.
                world.VerifyIncrementalHash();
                if (restored is not null)
                {
                    restored.Step();
                    Check.That(restored.StateHash == world.StateHash, $"{rate}: live and restored world agree in frame {frame + 1}, tick {tick}, after a snapshot {snapshotAfterTicks} ticks after the unit appeared.");
                }
                if (sinceFirst < 0 && world.Entities.Any(value => value.Template.Name == "Grunt")) sinceFirst = 0;
                else if (sinceFirst >= 0) sinceFirst++;
                if (snapshotAfterTicks >= 0 && sinceFirst == snapshotAfterTicks && restored is null)
                {
                    var bytes = WorldSnapshot.Write(world);
                    restored = WorldSnapshot.Restore(bytes, fixture.Templates, fixture.Fingerprint, terrain);
                    Check.That(restored.StateHash == world.StateHash, $"{rate}: the world restored while the unit waits for its place has the hash of the live one.");
                    Check.That(WorldSnapshot.Write(restored).AsSpan().SequenceEqual(bytes), $"{rate}: its snapshot is the same bytes.");
                }
            }
            var grunts = world.Entities.Where(value => value.Template.Name == "Grunt").OrderBy(value => value.Id.Value).ToArray();
            while (born.Count < grunts.Length) born.Add(frame);
            Check.That(grunts.All(grunt => !grunt.IsInsideFrameStep), $"{rate}: at the end of logic frame {frame + 1} no unit has a rest of its step left.");
            trace.Add(string.Join(";", grunts.Select(grunt => $"{grunt.X.Raw},{grunt.Y.Raw},{grunt.Angle.Raw},{(grunt.HasMoveTarget ? 1 : 0)},{grunt.MoveTargetX.Raw},{grunt.MoveTargetY.Raw}")));
        }
        Check.That(snapshotAfterTicks < 0 || restored is not null, $"{rate}: the snapshot was taken.");
        return (trace, born);
    }

    /// <summary>
    /// Three units leave a factory one after the other, the later ones ask the earlier ones to make room (and, with a rally point, all go on and find places there): position,
    /// heading, order state and goal of every unit after every logic frame of the original are those of 30 Hz at 60 and 120 Hz, bit for bit, and so is the frame each unit appears in.
    /// </summary>
    public static void RateExitsAreTheSame()
    {
        const int Frames = 450;
        foreach (var rally in new[] { false, true })
        {
            var (reference, referenceBorn) = ExitRun(SimulationRate.Hz30, rally, Frames);
            Check.That(referenceBorn.Count == 3, $"Rally {rally}: three units are built at 30 Hz ({referenceBorn.Count}).");
            Check.That(reference[^1].Split(';').All(unit => unit.Split(',')[3] == "0"), $"Rally {rally}: all three have settled at 30 Hz.");
            var places = reference[^1].Split(';').Select(unit => unit.Split(',')).Select(parts => (X: long.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), Y: long.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture))).ToArray();
            Check.That(places.Distinct().Count() == 3, $"Rally {rally}: no two units rest on the same spot.");
            Check.That(places.All(place => Math.Abs(place.X - Fixed.FromInt(70).Raw) > Fixed.FromInt(30).Raw || Math.Abs(place.Y - Fixed.FromInt(100).Raw) > Fixed.FromInt(30).Raw), $"Rally {rally}: all three rest outside the factory.");
            foreach (var (rate, factor) in WorldRates)
            {
                if (factor == 1) continue;
                var (trace, born) = ExitRun(rate, rally, Frames);
                Check.That(born.SequenceEqual(referenceBorn), $"{rate}, rally {rally}: the units appear in the frames they appear in at 30 Hz ({string.Join(",", born)} against {string.Join(",", referenceBorn)}).");
                var first = Enumerable.Range(0, Frames).FirstOrDefault(index => trace[index] != reference[index], -1);
                Check.That(first < 0, $"{rate}, rally {rally}: the units are where the 30 Hz units are after every logic frame (first difference after frame {first + 1}).");
            }
        }
    }

    /// <summary>
    /// Above 30 Hz a unit waits for the last tick of the frame it appeared in before its place in front of the factory is chosen (<c>SimulationWorld.ProcessExitClaims</c>); that
    /// wait is state of the world. A snapshot taken in every tick of that frame, and one a few frames into the exit drive, continues like the live world.
    /// </summary>
    public static void RateExitsSnapshotWhileWaitingForThePlace()
    {
        foreach (var (rate, factor) in WorldRates)
            foreach (var rally in new[] { false, true })
            {
                for (var after = 0; after < factor; after++) ExitRun(rate, rally, 200, after);
                ExitRun(rate, rally, 200, 7 * factor + 1);
            }
    }
}
