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
        Weapon FuelMissileGun
          PrimaryDamage = 60
          PrimaryDamageRadius = 5.0
          AttackRange = 400.0
          ProjectileObject = FuelMissile
          DelayBetweenShots = 0
        End
        Object FuelMissile
          KindOf = PROJECTILE SMALL_MISSILE
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Locomotor = SET_NORMAL SlowMissileLoco
          Behavior = MissileAIUpdate Missile01
            TryToFollowTarget = Yes
            FuelLifetime = 200
            IgnitionDelay = 100
            InitialVelocity = 60
            DistanceToTravelBeforeTurning = 10
            DistanceToTargetForLock = 30
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object FuelMissileLauncher
          KindOf = VEHICLE SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY FuelMissileGun
          End
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
    private static RateFlight SameFlightAtEveryRate(string launcher, string projectile, int distance, int budgetFrames, long? expectedImpactFrame)
    {
        RateFlight? reference = null;
        foreach (var (rate, factor) in WorldRates)
        {
            var flight = FlyAtDummy(rate, factor, launcher, projectile, distance, budgetFrames);
            reference ??= flight;
            var expected = expectedImpactFrame ?? reference.ImpactFrame;
            Check.That(flight.ImpactFrame == expected && expected > 0, $"{rate}: {projectile} lands in logic frame F+{expected} (measured F+{flight.ImpactFrame}).");
            Check.That(flight.DummyHealth == reference.DummyHealth, $"{rate}: {projectile} deals the damage of 30 Hz.");
            Check.That(flight.Frames.Count == reference.Frames.Count, $"{rate}: {projectile} was followed for the same number of frames.");
            for (var frame = 0; frame < reference.Frames.Count; frame++)
                Check.That(flight.Frames[frame] == reference.Frames[frame],
                    $"{rate}: after logic frame {frame} {projectile} is at {Show(reference.Frames[frame])} as at 30 Hz (measured {Show(flight.Frames[frame])}).");
            // Inside a frame the projectile lies on the chord between the two frame states, as many shares along as ticks have passed. The frame of the impact is left out:
            // an arrival or a hit on the ground puts the projectile there in the tick that decides it, as at 30 Hz.
            for (var tick = 0; tick < flight.Ticks.Count; tick++)
            {
                var frame = tick / factor;
                var share = tick % factor + 1;
                if (frame == 0 || frame == flight.ImpactFrame || share == factor || flight.Ticks[tick] is not { } at || flight.Frames[frame - 1] is not { } from || flight.Frames[frame] is not { } to) continue;
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

    /// <summary>
    /// §5.2 <c>MissileAIUpdate</c>: the two hand-computed missiles of Tests.Projectiles.cs fly the 30 Hz flight at every rate. With an acceleration of one top speed per frame the
    /// missile is 10 units out after its first frame (before: 10 / 7.5 / 6.25, because speed and acceleration were stepped per tick) and lands in F+10; with an ignition delay of
    /// three frames and one unit per frame² it follows the table 2, 4, 7, 11 … 96 (before, after frame 10: 56 / 50 / 47.19) and lands in F+15. The kill-self wait ends in the same frame.
    /// </summary>
    public static void RateProjectilesMissilesFlyTheThirtyHertzFlight()
    {
        var fast = SameFlightAtEveryRate("MissileLauncher", "FastMissile", 100, 16, 10);
        Check.That(fast.Frames[1] is { } first && first.X == Fixed.FromInt(10), "After its first frame the missile is one full step out.");
        Check.That(fast.Frames[12] is not null && fast.Frames[13] is null, "KillSelfDelay (3 frames) after the detonation removes the missile, in F+13.");
        var slow = SameFlightAtEveryRate("SlowMissileLauncher", "SlowMissile", 100, 18, 15);
        int[] expected = [2, 4, 7, 11, 16, 22, 29, 37, 46, 56, 66, 76, 86, 96];
        for (var frame = 0; frame < expected.Length; frame++)
            Check.That(slow.Frames[frame + 1] is { } at && at.X == Fixed.FromInt(expected[frame]), $"After frame {frame + 1} the slow missile is at x = {expected[frame]}.");
    }

    /// <summary>
    /// §5.2 <c>MissileAIUpdate</c>: fuel, the distance before turning and the fall without fuel are judged per frame. The missile coasts three frames, burns six, and then falls
    /// under gravity per frame² until it meets the ground short of the dummy — the same frames and the same place at every rate.
    /// </summary>
    public static void RateProjectilesMissileWithoutFuelFallsTheSameWay()
    {
        var flight = SameFlightAtEveryRate("FuelMissileLauncher", "FuelMissile", 300, 40, null);
        Check.That(flight.ImpactFrame is > 6 and < 30, $"The missile runs dry and lands on the ground (frame F+{flight.ImpactFrame}).");
        Check.That(flight.DummyHealth == Fixed.FromInt(1000), "It lands short of the dummy.");
    }

    /// <summary>
    /// §5.1/§5.2 <c>NeutronMissileUpdate</c>: thrust per frame and damping share per frame are one step, taken once per logic frame; the missile of Tests.AirWeapons.cs is in the
    /// same place after every frame at 30, 60 and 120 Hz, bit for bit, until <c>HeightDieUpdate</c> ends the dive (that module looks every tick and may see the height inside a
    /// frame: the death may come up to one frame early). Before, the recursion ran per tick with the per-frame numbers and the missile arrived much earlier at a higher rate.
    /// </summary>
    public static void RateProjectilesNeutronMissileFliesTheThirtyHertzFlight()
    {
        List<(Fixed X, Fixed Y, Fixed Z)>? reference = null;
        foreach (var (rate, factor) in WorldRates)
        {
            var fixture = new SyntheticFixture(rate, files: AirWeaponFixtureFiles);
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 13, AirPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, string.Empty), fixture.Templates);
            var feeder = new Feeder(world);
            feeder.Spawn(1, "AirSilo", owner: 1, x: 200, y: 200);
            feeder.Spawn(2, "AirDummy", owner: 2, x: 600, y: 200);
            world.Step();
            Check.That(feeder.FireWeapon(1, Get(world, "AirSilo").Id.Value, Get(world, "AirDummy").Id.Value).Accepted, $"{rate}: the silo launches.");
            var frames = new List<(Fixed X, Fixed Y, Fixed Z)>();
            Entity? missile = null;
            for (var step = 1; step <= world.Frames(400); step++)
            {
                world.Step();
                missile ??= world.Entities.FirstOrDefault(value => value.Template.Name == "AirNeutronMissile");
                if (missile is null) continue;
                if (missile.EffectivelyDead) break;
                if (step % factor == 0) frames.Add((missile.X, missile.Y, missile.Z));
            }
            reference ??= frames;
            Check.That(reference.Count > 60 && frames.Count <= reference.Count && frames.Count >= reference.Count - 1, $"{rate}: the missile dives for {reference.Count} frames, at most one less (measured {frames.Count}).");
            for (var frame = 0; frame < frames.Count; frame++)
                Check.That(frames[frame] == reference[frame], $"{rate}: after frame {frame} the missile is at {Show(reference[frame])} as at 30 Hz (measured {Show(frames[frame])}).");
        }
    }
}
