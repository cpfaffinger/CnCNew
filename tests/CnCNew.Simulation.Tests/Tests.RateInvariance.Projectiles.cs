using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Phase 23, package "projectiles" (<c>docs/RATE-INVARIANCE-PROJECTILES.md</c>): what flies takes its decisions once per logic frame of the original, on its own phase, exactly as at
/// 30 Hz, and flies the step of that frame in equal shares over the ticks of the frame. Each test runs the same scene at 30, 60 and 120 Hz and compares the state at the frame
/// boundaries with the 30 Hz run — positions bit for bit where the inputs are the same numbers, event times in logic frames.
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> RateProjectileFiles() => new Dictionary<string, string>(ProjectileFiles())
    {
        ["Data/INI/Object/RateProjectileFixture.ini"] = """
        Weapon ThirdRifle
          PrimaryDamage = 25
          AttackRange = 400.0
          WeaponSpeed = 100
          DelayBetweenShots = 0
        End
        Object ThirdRifleman
          KindOf = INFANTRY SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY ThirdRifle
          End
        End
        """,
    };

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture) RateProjectileWorld(SimulationRate rate)
    {
        var fixture = new SyntheticFixture(rate, files: RateProjectileFiles());
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        return (world, new Feeder(world), fixture);
    }

    /// <summary>
    /// §5.6 <c>FlightTicks</c>: 200 units at 100 per second. The 30 Hz catalog holds 10/3 per frame rounded down, so sixty frames of flight fall just short of 200 and the hit lands
    /// after 61 frames. At 60 Hz the catalog holds 5/3 per tick rounded <em>up</em>; twice that reaches 200 in sixty frames. The count is taken against the 30 Hz speed at every rate.
    /// </summary>
    public static void RateProjectilesDelayedHitCountsAgainstTheOriginalFrameSpeed()
    {
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder, _) = RateProjectileWorld(rate);
            var fire = FireAtDummy(world, feeder, "ThirdRifleman", 200, out _);
            var launched = world.Step().Events.Single(value => value.Kind == SimEventKind.ProjectileLaunched);
            Check.That(launched.Value - fire == 61 * factor, $"{rate}: the hit lands 61 logic frames after the shot (measured {launched.Value - fire} ticks).");
        }
    }

    /// <summary>The per-frame speed found from a per-tick catalog value is the value of the 30 Hz catalog, bit for bit, for authored speeds with up to four decimals.</summary>
    public static void RateProjectilesFrameSpeedIsTheThirtyHertzValue()
    {
        decimal[] perSecond = [1m, 7m, 20m, 33.3m, 99.99m, 100m, 120m, 133.3333m, 300m, 600m, 750.5m, 999m, 1000m, 1333m, 2000m, 20000m, 99999m];
        foreach (var value in perSecond)
        {
            var expected = SimulationRate.Hz30.Time.PerSecondToPerTick(value);
            foreach (var (rate, _) in WorldRates)
            {
                var found = rate.Time.PerTickToPerOriginalFrame(rate.Time.PerSecondToPerTick(value));
                Check.That(found == expected, $"{rate}: {value} per second is {expected.Raw} raw per frame at 30 Hz (found {found.Raw}).");
            }
        }
        // What the count was taken against before: the per-tick speed times the ticks per frame. For 100 per second that product reaches 200 units in sixty frames at 60 Hz only.
        long FramesFor(SimulationRate rate, int factor) => (Fixed.FromInt(200).Raw + rate.Time.PerSecondToPerTick(100m).Raw * factor - 1) / (rate.Time.PerSecondToPerTick(100m).Raw * factor);
        Check.That(FramesFor(SimulationRate.Hz30, 1) == 61 && FramesFor(SimulationRate.Hz60, 2) == 60 && FramesFor(SimulationRate.Hz120, 4) == 61,
            $"Scaled per-tick speeds count 61 / 60 / 61 frames ({FramesFor(SimulationRate.Hz30, 1)} / {FramesFor(SimulationRate.Hz60, 2)} / {FramesFor(SimulationRate.Hz120, 4)}).");
        // A value that is no catalog speed (here: one with seven decimals) is scaled by the whole factor instead – off by the last bits at most.
        var odd = SimulationRate.Hz120.Time.PerSecondToPerTick(100.1234567m);
        var scaled = SimulationRate.Hz120.Time.PerTickToPerOriginalFrame(odd);
        Check.That((scaled.Raw - SimulationRate.Hz30.Time.PerSecondToPerTick(100.1234567m).Raw) is >= -2 and <= 2, "A speed with more decimals is scaled, within two raw units of the 30 Hz value.");
        foreach (var (rate, factor) in WorldRates)
        {
            var whole = Fixed.FromInt(12);
            var sum = Fixed.Zero;
            for (var sub = 0; sub < factor; sub++) sum += rate.Time.FrameShare(whole / Fixed.FromInt(7), sub);
            Check.That(sum == whole / Fixed.FromInt(7), $"{rate}: the shares of one frame add up to the step exactly.");
        }
    }
}
