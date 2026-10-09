using System.Runtime.CompilerServices;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.Templates;

/// <summary>
/// The movement constants of a locomotor per logic frame of the original (1/30 s): the travel of one frame at full speed, the change of that travel per frame, the turn of one frame.
/// Ground movement decides once per logic frame with these values and covers the frame's step in equal shares per tick (<c>AIUpdateInterface.Move</c>,
/// <c>docs/rate-invariance/RATE-INVARIANCE-GROUND.md</c>), so the numbers of a decision have to be the numbers the 30 Hz step uses.
/// </summary>
public sealed class LocomotorFrame(Fixed speed, Fixed acceleration, Fixed turnRate, Fixed braking, Fixed minSpeed)
{
    public Fixed Speed { get; } = speed;
    public Fixed Acceleration { get; } = acceleration;
    public Fixed TurnRate { get; } = turnRate;
    public Fixed Braking { get; } = braking;
    public Fixed MinSpeed { get; } = minSpeed;

    /// <summary><see cref="LocomotorInfo.SpeedLimitZ"/> per logic frame (flight: <c>docs/rate-invariance/RATE-INVARIANCE-ROUND3-FLIGHT.md</c>).</summary>
    public Fixed SpeedLimitZ { get; init; }

    /// <summary><see cref="LocomotorInfo.Lift"/> per logic frame².</summary>
    public Fixed Lift { get; init; }
}

/// <summary>
/// The per-frame constants of the locomotors of every catalog built above 30 Hz. A catalog of 60 or 120 Hz carries its constants per tick, each rounded once for that rate; multiplied
/// back they miss the 30 Hz values by a step or two of the resolution, and a comparison such as "remaining distance against braking distance" that is an exact tie in the data (a
/// braking distance of 110.000 units) then falls differently at each rate. So the catalog builder hands the 30 Hz conversion of the same source numbers to this table, and a decision
/// above 30 Hz computes with the bits the 30 Hz step computes with. The table is derived data keyed by the catalog's own <see cref="LocomotorInfo"/> instances: no field of a catalog
/// type (the input manifest hashes those), no state, not hashed. A locomotor that no builder registered (hand-made in a test) gets its per-tick constants scaled.
/// </summary>
public static class LocomotorFrames
{
    private static readonly ConditionalWeakTable<LocomotorInfo, LocomotorFrame> Known = new();

    /// <summary>
    /// Called by the catalog builder for the finished <paramref name="info"/> (and its damaged form) with the source numbers it was built from, in units per second, per second² and
    /// degrees per second. Nothing to do at 30 Hz, where the catalog's constants are the per-frame constants.
    /// </summary>
    public static void Register(LocomotorInfo info, SimulationTime timing, decimal speed, decimal acceleration, decimal braking, decimal turnRate, decimal minSpeed,
        decimal speedDamaged, decimal accelerationDamaged, decimal turnRateDamaged, decimal speedLimitZ = 0m, decimal lift = 0m, decimal liftDamaged = 0m)
    {
        if (timing.TicksPerOriginalFrame == 1) return;
        // The same expressions as LocomotorInfo's builder (TemplateCatalog, ObjectTemplate.cs), with the time service of the original's rate.
        var original = SimulationRate.Original.Time;
        var normalBraking = braking > 0 ? original.PerSecondSquaredToPerTickSquared(braking) : original.PerSecondSquaredToPerTickSquared(acceleration);
        var limitZ = original.PerSecondToPerTick(speedLimitZ);
        Known.AddOrUpdate(info, new LocomotorFrame(original.PerSecondToPerTick(speed), original.PerSecondSquaredToPerTickSquared(acceleration),
            original.DegreesPerSecondToRadiansPerTick(turnRate), normalBraking, original.PerSecondToPerTick(minSpeed))
        {
            SpeedLimitZ = limitZ,
            Lift = original.PerSecondSquaredToPerTickSquared(lift),
        });
        if (info.Damaged is not { } damaged) return;
        Known.AddOrUpdate(damaged, new LocomotorFrame(original.PerSecondToPerTick(speedDamaged), original.PerSecondSquaredToPerTickSquared(accelerationDamaged),
            original.DegreesPerSecondToRadiansPerTick(turnRateDamaged), braking > 0 ? normalBraking : original.PerSecondSquaredToPerTickSquared(accelerationDamaged),
            original.PerSecondToPerTick(minSpeed))
        {
            SpeedLimitZ = limitZ,
            Lift = original.PerSecondSquaredToPerTickSquared(liftDamaged),
        });
    }

    /// <summary>The per-frame constants of <paramref name="info"/>, a locomotor of a catalog with <paramref name="ticksPerOriginalFrame"/> ticks per logic frame of the original (2 or 4).</summary>
    public static LocomotorFrame Of(LocomotorInfo info, int ticksPerOriginalFrame)
    {
        if (Known.TryGetValue(info, out var known)) return known;
        // Per tick to per frame: once for a speed or a turn, twice for an acceleration.
        long stride = ticksPerOriginalFrame;
        var acceleration = info.Acceleration * stride;
        var braking = info.Braking * stride;
        var lift = info.Lift * stride;
        return new LocomotorFrame(info.Speed * stride, acceleration * stride, info.TurnRatePerTick * stride, braking * stride, info.MinSpeed * stride)
        {
            SpeedLimitZ = info.SpeedLimitZ * stride,
            Lift = lift * stride,
        };
    }
}
