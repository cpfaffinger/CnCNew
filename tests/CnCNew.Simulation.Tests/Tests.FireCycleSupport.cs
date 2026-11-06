using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>Stepping a world to the shot of an attack order (F-140: the attack's machine fires an order's shot on its own frame, not in the order's tick).</summary>
static partial class Tests
{
    /// <summary>
    /// F-140: an attack order aims in the order's frame and fires in the shooter's next one (<c>AIAttackAimAtTargetState::update</c> succeeds into <c>FIRE_WEAPON</c>, whose update
    /// comes a frame later; one already in its fire state fires at once). Steps the order's tick and on through the first tick of the shooter's next logic frame of the original, and
    /// returns every event of those ticks.
    /// </summary>
    internal static List<SimEvent> StepThroughShot(SimulationWorld world, Entity shooter)
    {
        var events = new List<SimEvent>(world.Step().Events);
        while (!world.Time.BeginsFrameOf(shooter, world.Tick)) events.AddRange(world.Step().Events);
        events.AddRange(world.Step().Events);
        return events;
    }

    internal static List<SimEvent> StepThroughShot(SimulationWorld world, uint shooter) => StepThroughShot(world, world.FindEntity(new EntityId(shooter))!);

    /// <summary>The tick of <paramref name="shooter"/>'s first shot among <paramref name="events"/>, -1 for none.</summary>
    internal static long FiredTick(List<SimEvent> events, Entity shooter)
        => events.Where(value => value.Kind == SimEventKind.WeaponFired && value.Entity == shooter.Id).Select(value => value.Tick).DefaultIfEmpty(-1).First();

    /// <summary>The weapons <paramref name="source"/> fired among <paramref name="events"/>.</summary>
    internal static string[] WeaponsFiredIn(List<SimEvent> events, uint source)
        => [..events.Where(value => value.Kind == SimEventKind.WeaponFired && value.Entity.Value == source).Select(value => value.Text)];

    /// <summary>Steps until <paramref name="shooter"/> fires, at most <paramref name="frames"/> frames: the tick of the shot and its events (-1 and none when it does not fire).</summary>
    internal static (long Tick, List<SimEvent> Events) StepToShot(SimulationWorld world, Entity shooter, int frames = 4)
    {
        for (var i = 0; i < world.Frames(frames); i++)
        {
            var result = world.Step();
            if (result.Events.Any(value => value.Kind == SimEventKind.WeaponFired && value.Entity == shooter.Id)) return (result.Tick, [..result.Events]);
        }
        return (-1, []);
    }

    /// <summary>A wait of <paramref name="ticks"/> before the shot an order gives: the order's shot falls in the shooter's next frame (F-140), at any tick of the order's frame.</summary>
    internal static bool InTheOrdersNextFrame(long ticks) => ticks > 0 && ticks <= TestRate.Frames(1) + TestRate.Frames(1) - 1;
}
