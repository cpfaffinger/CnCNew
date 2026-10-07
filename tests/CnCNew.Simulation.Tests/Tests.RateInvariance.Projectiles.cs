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

    /// <summary>One flight on the frame grid: where the projectile is after each logic frame since the shot (null once it is gone), after each tick, and the frame of the impact event.</summary>
    private sealed record RateFlight(List<(Fixed X, Fixed Y, Fixed Z)?> Frames, List<(Fixed X, Fixed Y, Fixed Z)?> Ticks, long ImpactFrame, Fixed DummyHealth);

    /// <summary>
    /// Fires <paramref name="launcher"/> at a dummy <paramref name="distance"/> away and follows the projectile. The shot is given in tick F; "after frame j" is the state after the
    /// tick F + j of 30 Hz, which is the state after the last tick of that frame at a higher rate. The impact has to fall on the first tick of a frame.
    /// </summary>
    private static RateFlight FlyAtDummy(SimulationRate rate, int factor, string launcher, string projectile, int distance, int budgetFrames)
    {
        var (world, feeder, _) = RateProjectileWorld(rate);
        var fire = FireAtDummy(world, feeder, launcher, distance, out var dummy);
        var frames = new List<(Fixed X, Fixed Y, Fixed Z)?>();
        var ticks = new List<(Fixed X, Fixed Y, Fixed Z)?>();
        long impact = -1;
        for (var step = 1; step <= (budgetFrames + 1) * factor; step++)
        {
            var result = world.Step();
            if (impact < 0 && result.Events.Any(value => value.Kind == SimEventKind.WeaponImpact))
            {
                Check.That((result.Tick - fire) % factor == 0, $"{rate}: {projectile} lands on the first tick of a logic frame (tick F+{result.Tick - fire}).");
                impact = (result.Tick - fire) / factor;
            }
            var entity = world.Entities.FirstOrDefault(value => value.Template.Name == projectile);
            (Fixed X, Fixed Y, Fixed Z)? where = entity is null ? null : (entity.X, entity.Y, entity.Z);
            ticks.Add(where);
            if (step % factor == 0) frames.Add(where);
        }
        return new RateFlight(frames, ticks, impact, dummy.Body!.Health);
    }

    /// <summary>The flight of <paramref name="launcher"/>'s projectile is the 30 Hz flight at every rate: the same place after every logic frame, bit for bit, the same impact frame, the same damage.</summary>
    private static RateFlight SameFlightAtEveryRate(string launcher, string projectile, int distance, int budgetFrames, long expectedImpactFrame)
    {
        RateFlight? reference = null;
        foreach (var (rate, factor) in WorldRates)
        {
            var flight = FlyAtDummy(rate, factor, launcher, projectile, distance, budgetFrames);
            reference ??= flight;
            Check.That(flight.ImpactFrame == expectedImpactFrame, $"{rate}: {projectile} lands in logic frame F+{expectedImpactFrame} (measured F+{flight.ImpactFrame}).");
            Check.That(flight.DummyHealth == reference.DummyHealth, $"{rate}: {projectile} deals the damage of 30 Hz.");
            Check.That(flight.Frames.Count == reference.Frames.Count, $"{rate}: {projectile} was followed for the same number of frames.");
            for (var frame = 0; frame < reference.Frames.Count; frame++)
                Check.That(flight.Frames[frame] == reference.Frames[frame],
                    $"{rate}: after logic frame {frame} {projectile} is at {Show(reference.Frames[frame])} as at 30 Hz (measured {Show(flight.Frames[frame])}).");
            // Inside a frame the projectile lies on the chord between the two frame states, as many shares along as ticks have passed.
            for (var tick = 0; tick < flight.Ticks.Count; tick++)
            {
                var frame = tick / factor;
                var share = tick % factor + 1;
                if (frame == 0 || share == factor || flight.Ticks[tick] is not { } at || flight.Frames[frame - 1] is not { } from || flight.Frames[frame] is not { } to) continue;
                var x = from.X + (to.X - from.X) * share / Fixed.FromInt(factor);
                var z = from.Z + (to.Z - from.Z) * share / Fixed.FromInt(factor);
                Check.That((at.X - x).Abs().Raw <= 8 && (at.Z - z).Abs().Raw <= 8, $"{rate}: tick {tick} of {projectile} lies {share}/{factor} along the chord of its frame (x {at.X} against {x}, z {at.Z} against {z}).");
            }
        }
        return reference!;
    }

    private static string Show((Fixed X, Fixed Y, Fixed Z)? point) => point is { } value ? $"({value.X}, {value.Y}, {value.Z})" : "gone";

    /// <summary>
    /// §5.2 <c>DumbProjectileBehavior</c>: the straight shell of the hand-computed 30 Hz test (200 units at 10 per frame: twenty path points, on point 0 in frame F+1, on the last
    /// in F+20, detonation in F+21) flies the same path at 60 and 120 Hz. Before, the path had one point per tick: 20 / 40 / 80 points of 10 / 5 / 2.5 units and the detonation one
    /// tick after the last, 0.700 / 0.683 / 0.675 s after the shot.
    /// </summary>
    public static void RateProjectilesStraightShellFliesTheThirtyHertzPath()
    {
        var flight = SameFlightAtEveryRate("ShellLauncher", "TestShell", 200, 24, 21);
        Check.That(flight.Frames[1] == (Fixed.Zero, Fixed.Zero, Fixed.Zero) && flight.Frames[20] is { } last && last.X == Fixed.FromInt(200), "The shell is on point 0 after frame 1 and on the last point after frame 20.");
        Check.That(flight.Frames[21] is null, "The shell is gone with the detonation.");
    }

    /// <summary>§5.2 <c>DumbProjectileBehavior</c>: the ballistic arc (14 path points, landing in F+15) is the same arc at every rate; before it had 14 / 27 / 53 points and landed after 0.467 / 0.450 / 0.442 s of flight.</summary>
    public static void RateProjectilesBallisticArcFliesTheThirtyHertzPath()
    {
        var flight = SameFlightAtEveryRate("ArcLauncher", "ArcShell", 120, 18, 15);
        var peak = flight.Ticks.Max(value => value?.Z ?? Fixed.Zero);
        Check.That(peak > Fixed.FromInt(22) && peak <= Fixed.FromDecimal(22.5m), $"The arc peaks below 22.5 (measured {peak}).");
    }

    /// <summary>A projectile restored inside a frame flies on identically (the share of the frame already flown is state).</summary>
    public static void RateProjectilesSnapshotInsideAFrame()
    {
        foreach (var (rate, factor) in WorldRates)
        {
            List<ulong> Run(int snapshotAt)
            {
                var (world, feeder, fixture) = RateProjectileWorld(rate);
                feeder.Spawn(1, "ShellLauncher", x: 0, y: 0);
                feeder.Spawn(1, "ArcLauncher", x: 0, y: 20);
                feeder.Spawn(1, "MissileLauncher", x: 0, y: 40);
                feeder.Spawn(1, "SlowMissileLauncher", x: 0, y: 60);
                feeder.Spawn(2, "Dummy", x: 250, y: 30);
                world.Step();
                var dummy = Get(world, "Dummy").Id.Value;
                foreach (var name in new[] { "ShellLauncher", "ArcLauncher", "MissileLauncher", "SlowMissileLauncher" })
                    feeder.FireWeapon(1, Get(world, name).Id.Value, dummy);
                var hashes = new List<ulong>();
                for (var step = 0; step < world.Frames(45); step++)
                {
                    if (step == snapshotAt)
                    {
                        Check.That(world.Entities.Count(value => value.Template.IsKindOf("PROJECTILE")) == 4, $"{rate}: four projectiles are in flight at the snapshot.");
                        world = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
                    }
                    hashes.Add(world.Step().StateHash);
                }
                return hashes;
            }
            var plain = Run(-1);
            for (var offset = 0; offset < factor; offset++)
                Check.That(plain.SequenceEqual(Run(4 * factor + offset)), $"{rate}: a snapshot taken {offset} ticks into a frame continues identically.");
        }
    }
}
