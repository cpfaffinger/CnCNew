using CnCNew.Simulation.Events;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;

/// <summary>
/// <c>RailroadBehavior::onCollide</c>, "Alert all the players of recent disaster": a train car that runs into something with physics raises <see cref="SimEventKind.TrainWhistle"/>
/// (the presentation plays the car's <c>WhistleSound</c> for it). Structures that die at once, objects without physics and things far off the track whistle nothing.
/// </summary>
static partial class Tests
{
    /// <summary>The frames of the original (counted from the first full frame) in which a whistle was raised, with the entity and the one it ran into.</summary>
    private static List<(int Frame, uint Car, uint Other)> TrainWhistles(SimulationRate rate, params (string Template, int Player, int X, int Y)[] spawns)
    {
        var (world, fixture, _) = RatedStructuresWorld(rate);
        foreach (var spawn in spawns) RequestSpawn(world, fixture, spawn.Template, spawn.Player, spawn.X, spawn.Y);
        var whistles = new List<(int, uint, uint)>();
        // The train is placed in the world's first tick, so its frames are the world's: the series is read where they end (as RateInvarianceTrain does).
        for (var tick = 1; tick < world.Time.TicksPerOriginalFrame; tick++) world.Step();
        for (var frame = 0; frame < 400; frame++)
            for (var tick = 0; tick < (frame == 0 ? 1 : world.Time.TicksPerOriginalFrame); tick++)
                foreach (var item in world.Step().Events)
                    if (item.Kind == SimEventKind.TrainWhistle) whistles.Add((frame, item.Entity.Value, item.Other.Value));
        return whistles;
    }

    private static Fixed ConductorSpeedAtFrame(SimulationRate rate)
    {
        var (world, fixture, _) = RatedStructuresWorld(rate);
        RequestSpawn(world, fixture, "SwLoco", 0, 50, 100);
        for (var tick = 0; tick < world.Frames(60); tick++) world.Step();
        return world.Entities.Where(entity => entity.Template.Name == "SwLoco").Select(entity => entity.Modules.OfType<RailroadBehavior>().Single().ConductorSpeedPerFrame).Single();
    }

    public static void TrainWhistlesWhenItRunsIntoSomething()
    {
        var reference = TrainWhistles(SimulationRate.Hz30, ("SwHeavy", 2, 95, 104), ("SwLoco", 0, 50, 100));
        Check.That(reference.Count > 0, "A locomotive that grinds along a vehicle raises the whistle.");
        Check.That(reference.All(item => item.Car != item.Other), "The whistle names the car and what it ran into.");
        foreach (var (rate, name) in new[] { (SimulationRate.Hz60, "60 Hz"), (SimulationRate.Hz120, "120 Hz") })
        {
            var other = TrainWhistles(rate, ("SwHeavy", 2, 95, 104), ("SwLoco", 0, 50, 100));
            Check.That(other.Select(item => item.Frame).SequenceEqual(reference.Select(item => item.Frame)),
                "The whistle comes in the same frames of the original at " + name + ": " + string.Join(",", other.Select(item => item.Frame).Distinct()) + " instead of " + string.Join(",", reference.Select(item => item.Frame).Distinct()) + ".");
        }

        // The clatter's volume is the conductor's speed per frame of the original over ten: the same number at every rate.
        var speeds = new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 }.Select(ConductorSpeedAtFrame).ToArray();
        Check.That(speeds[0] > Fixed.One && (speeds[1] - speeds[0]).Abs() <= Fixed.FromRaw(4) && (speeds[2] - speeds[0]).Abs() <= Fixed.FromRaw(4),
            "The conductor's speed per frame of the original is the same at 30, 60 and 120 Hz: " + string.Join(", ", speeds) + ".");

        Check.That(TrainWhistles(SimulationRate.Hz30, ("SwHeavy", 2, 140, 160), ("SwLoco", 0, 50, 100)).Count == 0, "Something 60 units off the track is not run into, so nothing whistles.");
        Check.That(TrainWhistles(SimulationRate.Hz30, ("SwWalker", 2, 100, 100), ("SwLoco", 0, 50, 100)).Count == 0, "An object without physics is no collision (the reference returns before the whistle).");
        Check.That(TrainWhistles(SimulationRate.Hz30, ("SwPlant", 1, 170, 100), ("SwLoco", 0, 50, 100)).Count == 0, "A base structure is destroyed without the whistle (the reference returns first).");
    }
}
