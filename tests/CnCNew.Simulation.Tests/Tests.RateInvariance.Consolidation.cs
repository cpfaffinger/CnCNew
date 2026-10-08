using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Phase 23, consolidation of the rate-invariance packages (<c>docs/RATE-INVARIANCE-CONSOLIDATION.md</c>): what happens where the frames of two objects meet. Every test names
/// its rates itself and compares 60 and 120 Hz with 30 Hz at the end of every logic frame of the original.
/// </summary>
internal static partial class Tests
{
    /// <summary>The positions, headings and order states of all walkers after every logic frame of a run in which two groups walk through each other.</summary>
    private static List<string> ConsolidationCrossing(SimulationRate rate, int frames)
    {
        var (world, feeder, _) = RatedWorld(rate, GroundRateFiles(), NavTerrain());
        var stride = rate.TicksPerOriginalFrame;
        // Two rows of four, 60 units apart, each walker ordered to the place of the one opposite: every path crosses the other row.
        for (var index = 0; index < 4; index++) feeder.Spawn(1, "RateWalker", x: 60, y: 60 + index * 9);
        for (var index = 0; index < 4; index++) feeder.Spawn(1, "RateWalker", x: 120, y: 60 + index * 9);
        for (var tick = 0; tick < stride; tick++) world.Step();
        var walkers = world.Entities.Where(value => value.Template.Name == "RateWalker").OrderBy(value => value.Id.Value).ToArray();
        Check.That(walkers.Length == 8, $"{rate}: eight walkers appeared.");
        for (var index = 0; index < 4; index++)
        {
            Check.That(feeder.SetMoveTarget(1, walkers[index].Id.Value, 120, 60 + (3 - index) * 9).Accepted, $"{rate}: the order of walker {index} is accepted.");
            Check.That(feeder.SetMoveTarget(1, walkers[index + 4].Id.Value, 60, 60 + (3 - index) * 9).Accepted, $"{rate}: the order of walker {index + 4} is accepted.");
        }
        var trace = new List<string>();
        for (var frame = 0; frame < frames; frame++)
        {
            for (var tick = 0; tick < stride; tick++) world.Step();
            trace.Add(string.Join(";", walkers.Select(walker => $"{walker.X.Raw},{walker.Y.Raw},{walker.Angle.Raw},{(walker.HasMoveTarget ? 1 : 0)}")));
        }
        Check.That(walkers.All(walker => !walker.IsInsideFrameStep), $"{rate}: at the end of a logic frame no walker has a rest of its step left.");
        return trace;
    }

    /// <summary>
    /// Convention §1.3, rule 3 (<c>StepBlocked</c>, <c>StationaryBlockersAhead</c>, <c>Entity.FrameEnd</c>): a mover that decides its frame sees a mover that ran before it in the
    /// tick where that one's frame ends, and one that arrives with this frame as standing. Two rows of walkers that walk through each other (local avoidance, replanning around
    /// those that have arrived) are in the same place after every logic frame at 30, 60 and 120 Hz, bit for bit. Before, the later mover saw the earlier one a share into its step.
    /// </summary>
    public static void RateConsolidationMoversThatMeetAreTheSame()
    {
        const int Frames = 400;
        var reference = ConsolidationCrossing(SimulationRate.Hz30, Frames);
        Check.That(reference[^1] != reference[0], "The walkers move.");
        foreach (var (rate, factor) in WorldRates)
        {
            if (factor == 1) continue;
            var trace = ConsolidationCrossing(rate, Frames);
            var first = Enumerable.Range(0, Frames).FirstOrDefault(index => trace[index] != reference[index], -1);
            Check.That(first < 0, $"{rate}: the eight walkers are where the 30 Hz walkers are after every logic frame (first difference after frame {first + 1}).");
        }
    }
}
