using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;

static partial class Tests
{
    /// <summary>
    /// A hijacker of player 2 at (40, 105) is sent to take an idle tank of player 1 at (105, 105), which stands on open ground or, with <paramref name="onCliff"/>, in the middle of a block of
    /// cliff cells 30 units wide that the hijacker cannot walk onto; whether the tank was taken after 20 seconds, and how far the hijacker stands from it.
    /// </summary>
    private static (bool Taken, decimal Distance) HijackAcross(SimulationRate rate, bool onCliff)
    {
        var files = new Dictionary<string, string>(SyntheticFixture.Files) { ["Data/INI/Object/SquishSpares.ini"] = SquishSparesIni };
        var cliffs = onCliff ? Enumerable.Range(9, 3).SelectMany(x => Enumerable.Range(9, 3).Select(y => (x, y))).ToArray() : [];
        var (world, feeder, _) = RatedWorld(rate, files, NavTerrain(cliffs));
        feeder.Spawn(1, "SquishTank", owner: 1, x: 105, y: 105);
        feeder.Spawn(2, "SquishHijacker", owner: 2, x: 40, y: 105);
        world.Step();
        while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
        var vehicle = world.Entities.Single(value => value.Template.Name == "SquishTank");
        var man = world.Entities.Single(value => value.Template.Name == "SquishHijacker");
        Check.That(feeder.MoveAndEnter(2, man.Id.Value, vehicle.Id.Value).Accepted, $"{rate}: the hijack is accepted.");
        for (var step = 0; step < world.Frames(600); step++) world.Step();
        var (dx, dy) = (man.X - vehicle.X, man.Y - vehicle.Y);
        return (vehicle.Owner == new PlayerId(2), (decimal)CnCNew.Simulation.Numerics.Fixed.Sqrt(dx * dx + dy * dy).ToDecimal());
    }

    /// <summary>
    /// #370: the collide modules of an approach act on its goal object only by touching it (<c>PartitionContactList::processContactList</c>); nothing in <c>AIUpdateInterface</c> calls
    /// them. A hijacker that walks to an idle tank on open ground takes it; one whose way ends at a cliff 20 units from the tank, within two cells of the bounding circles where the port's
    /// stand-in acted, does not. At 30, 60 and 120 Hz.
    /// </summary>
    public static void AnApproachActsOnlyByTouching()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var open = HijackAcross(rate, onCliff: false);
            Check.That(open.Taken, $"{rate}: a hijacker that reaches the tank takes it.");
            var cliff = HijackAcross(rate, onCliff: true);
            Check.That(!cliff.Taken && cliff.Distance > 9, $"{rate}: a hijacker that stops short of the tank does not take it (taken {cliff.Taken}, {cliff.Distance:F1} units from it).");
        }
    }
}
