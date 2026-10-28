using System.Collections.Immutable;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// F-58, guarding (docs/GUARD-STATE.md, <c>AIGuardMachine</c>): a rifleman (vision 150, rifle 100) guards a point. A human's guard looks 1.8 x vision (270) around the point, follows up to
/// 2.2 x vision (330), a computer's 1.1 x (165) and 1.333 x (200).
/// </summary>
static partial class Tests
{
    private const string GuardIni = """
        Locomotor GdFast
          Surfaces = GROUND
          Speed = 90
          Acceleration = 900
          Braking = 900
          TurnRate = 3600
        End
        Object GdDummy
          KindOf = SELECTABLE VEHICLE
          VisionRange = 100.0
          Locomotor = SET_NORMAL FcFeet
          Body = ActiveBody Body01
            MaxHealth = 40.0
            InitialHealth = 40.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object GdRunner
          KindOf = SELECTABLE VEHICLE
          VisionRange = 100.0
          Locomotor = SET_NORMAL GdFast
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Weapon GdLongRifle
          PrimaryDamage = 1
          AttackRange = 180.0
          DamageType = SMALL_ARMS
          ClipSize = 0
          DelayBetweenShots = 300
        End
        Object GdSniper
          KindOf = SELECTABLE INFANTRY
          VisionRange = 200.0
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY GdLongRifle
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """;

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture) GuardWorld(SimulationRate rate, bool computer = false)
    {
        var fixture = new SyntheticFixture(rate, files: FireFromContainerFiles(GuardIni));
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, computer ? ComputerAgainstHuman : CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        return (world, new Feeder(world), fixture);
    }

    private static Entity GdSpawn(SimulationWorld world, Feeder feeder, string template, int owner, int x, int y)
    {
        feeder.Spawn(0, template, owner: owner, x: x, y: y);
        world.Step();
        return world.Entities.Where(value => value.Template.Name == template).OrderBy(value => value.Id.Value).Last();
    }

    private static Fixed GdDistance(Entity entity, int x, int y)
    {
        var dx = entity.X - Fixed.FromInt(x);
        var dy = entity.Y - Fixed.FromInt(y);
        return Fixed.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Step 1, the position guard: an enemy 200 from the point - beyond the rifle's 100, within the human guard's inner 270 - is driven to and killed, and the guard walks back to its
    /// point and is idle there; a plain rifleman at the same place (no guard) never moves. A move order ends the guard. The same frames at 30, 60 and 120 Hz, and a save in the
    /// middle of the chase continues identically.
    /// </summary>
    public static void GuardPositionChasesAndReturns()
    {
        (long Killed, long Back) Run(SimulationRate rate, bool guard, bool save)
        {
            var (world, feeder, fixture) = GuardWorld(rate);
            var guardian = GdSpawn(world, feeder, "FcRifleman", 1, 0, 0);
            var dummy = GdSpawn(world, feeder, "GdDummy", 2, 200, 0);
            if (guard) Check.That(feeder.Guard(1, [guardian.Id.Value], 0, 0).Accepted, $"{rate}: the guard order is accepted.");
            long killed = -1, back = -1;
            for (var step = 0; step < world.Frames(600); step++)
            {
                world.Step();
                if (save && step == world.Frames(60))
                {
                    var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
                    Check.That(restored.StateHash == world.StateHash, $"{rate}: a world in the middle of the guard's chase restores to the same hash.");
                    for (var next = 0; next < world.Frames(30); next++) Check.That(world.Step().StateHash == restored.Step().StateHash, $"{rate}: step {next} after the restore agrees.");
                }
                if (killed < 0 && (dummy.EffectivelyDead || !world.Entities.Contains(dummy))) killed = world.Time.ToOriginalFrames(world.Tick);
                if (killed >= 0 && back < 0 && GdDistance(guardian, 0, 0) < Fixed.FromInt(20) && !guardian.HasMoveTarget) back = world.Time.ToOriginalFrames(world.Tick);
            }
            return (killed, back);
        }

        var plain = Run(TestRate.Default, guard: false, save: false);
        Check.That(plain.Killed < 0, "A rifleman without a guard does not walk to an enemy beyond its rifle.");
        var at60 = Run(SimulationRate.Hz60, guard: true, save: false);
        Check.That(at60.Killed > 0 && at60.Back > at60.Killed, $"The guard drives to the enemy 200 away, kills it (frame {at60.Killed}) and is back at its point (frame {at60.Back}).");
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz120 })
        {
            var other = Run(rate, guard: true, save: false);
            Check.That(Math.Abs(other.Killed - at60.Killed) <= 1 && Math.Abs(other.Back - at60.Back) <= 1, $"{rate}: the same frames as at 60 Hz ({other.Killed}/{other.Back} against {at60.Killed}/{at60.Back}).");
        }
        Run(TestRate.Default, guard: true, save: true);

        var (moveWorld, moveFeeder, _) = GuardWorld(TestRate.Default);
        var mover = GdSpawn(moveWorld, moveFeeder, "FcRifleman", 1, 0, 0);
        moveFeeder.Guard(1, [mover.Id.Value], 0, 0);
        moveWorld.Step();
        Check.That(moveWorld.IsGuarding(mover), "Setup: the rifleman guards.");
        moveFeeder.SetMoveTarget(1, mover.Id.Value, 100, 100);
        moveWorld.Step();
        Check.That(!moveWorld.IsGuarding(mover), "A move order ends the guard.");
    }

    /// <summary>
    /// Step 1, giving up: an enemy that runs off beyond the outer range (330) is let go, the guard returns; in <c>GUARD_WITHOUT_PURSUIT</c> one that leaves the inner range (270) is let go
    /// at once (no OUTER); <c>GUARD_FLYING_UNITS_ONLY</c> ignores a ground enemy; a computer player's guard looks only 1.1 x vision (165) around its point.
    /// </summary>
    public static void GuardGivesUpAndModes()
    {
        Fixed Furthest(GuardMode mode, int runTo, bool computer = false, int start = 200, bool expectBack = true)
        {
            var (world, feeder, _) = GuardWorld(TestRate.Default, computer);
            var guardian = GdSpawn(world, feeder, "FcRifleman", 1, 0, 0);
            var runner = GdSpawn(world, feeder, "GdRunner", 2, start, 0);
            feeder.Guard(1, [guardian.Id.Value], 0, 0, mode: mode);
            for (var step = 0; step < world.Frames(30); step++) world.Step();
            if (runTo != start) feeder.SetMoveTarget(2, runner.Id.Value, runTo, 0);
            var furthest = Fixed.Zero;
            for (var step = 0; step < world.Frames(900); step++)
            {
                world.Step();
                furthest = Fixed.Max(furthest, GdDistance(guardian, 0, 0));
            }
            if (expectBack) Check.That(GdDistance(guardian, 0, 0) < Fixed.FromInt(20), $"{mode} (run to {runTo}, computer {computer}): the guard is back at its point in the end ({GdDistance(guardian, 0, 0)}).");
            return furthest;
        }

        var follows = Furthest(GuardMode.Normal, 310);
        Check.That(follows > Fixed.FromInt(120), $"A guard follows an enemy into its outer range ({follows}).");
        var noPursuit = Furthest(GuardMode.WithoutPursuit, 310);
        Check.That(noPursuit < follows, $"Without pursuit it lets the enemy go when it leaves the inner range ({noPursuit} against {follows}).");
        var flying = Furthest(GuardMode.FlyingUnitsOnly, 200);
        Check.That(flying < Fixed.FromInt(10), $"Guarding against flying units only, it ignores a ground enemy ({flying}).");
        var computerFar = Furthest(GuardMode.Normal, 180, computer: true, start: 180);
        Check.That(computerFar < Fixed.FromInt(10), $"A computer player's guard does not look as far as 180 (its inner range is 165) ({computerFar}).");
        var computerNear = Furthest(GuardMode.Normal, 150, computer: true, start: 150, expectBack: false);
        Check.That(computerNear > Fixed.FromInt(20), $"It takes an enemy at 150 ({computerNear}).");
    }

    /// <summary>
    /// Step 1, ATTACK_AGGRESSOR (<c>hasAttackedMeAndICanReturnFire</c>): a computer player's guard (inner 165, outer 200) is shot by a sniper 175 away - beyond its inner range, so its scans
    /// do not find it -: it answers and walks at the sniper. Without the guard the same rifleman never moves.
    /// </summary>
    public static void GuardAnswersAnAttacker()
    {
        Fixed Furthest(bool guard)
        {
            var (world, feeder, _) = GuardWorld(TestRate.Default, computer: true);
            var guardian = GdSpawn(world, feeder, "FcRifleman", 1, 0, 0);
            var sniper = GdSpawn(world, feeder, "GdSniper", 2, 175, 0);
            if (guard) feeder.Guard(1, [guardian.Id.Value], 0, 0);
            for (var step = 0; step < world.Frames(30); step++) world.Step();
            feeder.FireWeapon(2, sniper.Id.Value, guardian.Id.Value);
            var furthest = Fixed.Zero;
            for (var step = 0; step < world.Frames(150); step++)
            {
                world.Step();
                furthest = Fixed.Max(furthest, GdDistance(guardian, 0, 0));
            }
            return furthest;
        }

        Check.That(Furthest(guard: true) > Fixed.FromInt(30), "The guard answers the sniper that shot it.");
        Check.That(Furthest(guard: false) < Fixed.FromInt(30), "Without the guard the rifleman stays.");
    }
}
