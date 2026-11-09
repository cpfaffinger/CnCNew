using System.Collections.Immutable;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>What a group of wheeled vehicles on a flat map did with a move order: when the last stopped (game seconds, -1: some still move after the budget), how far each stands from its ordered point, the turns they made.</summary>
    private sealed record JamResult(double SecondsToSettle, double[] Distances, double Turns, (double X, double Y)[] Places, double[] Stops);

    /// <summary>The owner's finding of 8 November 2026: vehicles that block one another turn in circles for ever. <paramref name="units"/> are (x, y, toX, toY).</summary>
    private static JamResult RunJam(RealFireWorld data, SimulationRate rate, string template, (int X, int Y, int ToX, int ToY)[] units, int seconds)
    {
        var catalog = TemplateCatalog.Build(data.Graph, ModuleBindingTable.Default, rate, rules: RuleProfile.Product);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
        var terrain = TerrainMap.Build(SyntheticMaps.Build(120, 120, 1, (_, _) => 0), "jam");
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "real-jam", data.Fingerprint, terrain.Fingerprint), catalog, terrain);
        ulong sequence = 0;
        foreach (var unit in units)
            Check.That(world.Submit(new SpawnCommand(new PlayerId(1), ++sequence, world.Tick, template, new PlayerId(1), Fixed.FromInt(unit.X), Fixed.FromInt(unit.Y), Fixed.Zero, Fixed.Zero)).Accepted, "The vehicle spawns.");
        world.Step();
        var vehicles = world.Entities.Where(value => value.Template.Name == template).OrderBy(value => value.Id.Value).ToArray();
        var orderedAt = world.Tick;
        for (var index = 0; index < vehicles.Length; index++)
            Check.That(world.Submit(new SetMoveTargetCommand(new PlayerId(1), ++sequence, world.Tick, vehicles[index].Id, Fixed.FromInt(units[index].ToX), Fixed.FromInt(units[index].ToY))).Accepted, "The move is accepted.");
        var angles = vehicles.Select(value => value.Angle).ToArray();
        var rotation = 0.0;
        double settled = -1;
        var stops = Enumerable.Repeat(-1.0, vehicles.Length).ToArray();
        var frames = seconds * SimulationRate.OriginalFramesPerSecond;
        // Whole frames of the original: the three rates are compared where each has finished the same frame.
        for (var frame = 0; frame < frames; frame++)
        {
            for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
            for (var index = 0; index < vehicles.Length; index++)
            {
                var turn = Math.Abs((double)(vehicles[index].Angle - angles[index]).ToDecimal());
                rotation += turn > Math.PI ? 2 * Math.PI - turn : turn;
                angles[index] = vehicles[index].Angle;
            }
            for (var index = 0; index < vehicles.Length; index++)
                if (stops[index] < 0 && !vehicles[index].HasMoveTarget) stops[index] = (double)(world.Tick - orderedAt) / rate.TicksPerOriginalFrame / SimulationRate.OriginalFramesPerSecond;
            if (settled < 0 && vehicles.All(value => !value.HasMoveTarget)) settled = (double)(world.Tick - orderedAt) / rate.TicksPerOriginalFrame / SimulationRate.OriginalFramesPerSecond;
        }
        var distances = vehicles.Select((value, index) => Math.Sqrt(Math.Pow((double)value.X.ToDecimal() - units[index].ToX, 2) + Math.Pow((double)value.Y.ToDecimal() - units[index].ToY, 2))).ToArray();
        return new JamResult(settled, distances, rotation / (2 * Math.PI), vehicles.Select(value => ((double)value.X.ToDecimal(), (double)value.Y.ToDecimal())).ToArray(), stops);
    }

    private static void SameJamAtEveryRate(string what, JamResult thirty, JamResult other, SimulationRate rate)
    {
        Check.That(Math.Abs(thirty.SecondsToSettle - other.SecondsToSettle) < 0.2, $"{what}: the vehicles stop at {other.SecondsToSettle:F2} s at {rate}, at {thirty.SecondsToSettle:F2} s at 30 Hz.");
        for (var index = 0; index < thirty.Places.Length; index++)
            Check.That(Math.Abs(thirty.Places[index].X - other.Places[index].X) < 1 && Math.Abs(thirty.Places[index].Y - other.Places[index].Y) < 1, $"{what}: vehicle {index} stands at the same place at {rate} and at 30 Hz.");
    }

    /// <summary>
    /// <c>AIUpdateInterface::blockedBy</c>: "if (getCurLocomotor()->isMovingBackwards()) return false; // don't collide". A Dozer whose goal lies 30 units behind it backs up; another Dozer crossing in front of it
    /// (the two overlap on the ground plan, and the other does not move away) held it before, because the check looked at where the front of the unit points: it stopped for the crossing Dozer
    /// (2.8 to 3.2 seconds against 1.7). Equal at 30, 60 and 120 Hz.
    /// </summary>
    public static void RealReversingDozerIsHeldByNobody(string directory)
    {
        var data = OpenRealFireData(directory);
        JamResult? thirty = null;
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var result = RunJam(data, rate, "AmericaVehicleDozer", [(600, 600, 570, 600), (614, 600, 614, 1000)], 20);
            Console.WriteLine($"       {rate}: the reversing Dozer stops after {result.Stops[0]:F2} s at {result.Places[0].X:F1}, {result.Places[0].Y:F1}");
            Check.That(result.Stops[0] > 0 && result.Stops[0] < 2.2, $"{rate}: the reversing Dozer is at its goal within 2.2 seconds ({result.Stops[0]:F2} s).");
            Check.That(Math.Abs(result.Places[0].X - 570) < 2 && Math.Abs(result.Places[0].Y - 600) < 2, $"{rate}: and stands on it ({result.Places[0].X:F1}, {result.Places[0].Y:F1}).");
            if (thirty is null) thirty = result;
            else Check.That(Math.Abs(thirty.Stops[0] - result.Stops[0]) < 0.2, $"{rate}: it stops when the 30 Hz Dozer does ({result.Stops[0]:F2} against {thirty.Stops[0]:F2}).");
        }
    }
}
