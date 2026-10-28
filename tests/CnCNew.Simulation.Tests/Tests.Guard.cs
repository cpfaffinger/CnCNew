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
        Object GdCannon
          KindOf = SELECTABLE VEHICLE CAN_ATTACK
          VisionRange = 150.0
          Locomotor = SET_NORMAL FcFeet
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY FcRifle
          End
          Behavior = DeployStyleAIUpdate AI01
            AutoAcquireEnemiesWhenIdle = Yes
            PackTime = 500
            UnpackTime = 500
          End
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

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture) GuardWorld(SimulationRate rate, bool computer = false, CnCNew.Simulation.Rules.RuleProfile? rules = null)
    {
        var fixture = new SyntheticFixture(rate, files: FireFromContainerFiles(GuardIni), rules: rules ?? CnCNew.Simulation.Rules.RuleProfile.Original);
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, computer ? ComputerAgainstHuman : CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        return (world, new Feeder(world), fixture);
    }

    private static Entity GdSpawn(SimulationWorld world, Feeder feeder, string template, int owner, int x, int y)
    {
        feeder.Spawn(0, template, owner: owner, x: x, y: y);
        // One whole frame of the original, so that the orders that follow are given at the start of the same frame at every rate.
        for (var step = 0; step < world.Frames(1); step++) world.Step();
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

    /// <summary>
    /// Step 6, the player's retaliation (<c>ActiveBody::attemptDamage</c>, <c>AIGuardRetaliateMachine</c>): a human player's idle rifleman 100 from a friend that a sniper shoots from 160 away -
    /// 260 from the rifleman, beyond its rifle - drives at the sniper. A computer player's rifleman in the same place stays (only a human player retaliates, and the sniper is beyond
    /// its idle look of 200). A unit that is not idle does not join.
    /// </summary>
    public static void GuardRetaliation()
    {
        Fixed Moved(bool human, bool busy)
        {
            var (world, feeder, _) = GuardWorld(TestRate.Default, computer: true);
            // ComputerAgainstHuman: player 1 is the computer, player 2 the human.
            var side = human ? 2 : 1;
            var rifleman = GdSpawn(world, feeder, "FcRifleman", side, 0, 0);
            var friend = GdSpawn(world, feeder, "GdRunner", side, 100, 0);
            var sniper = GdSpawn(world, feeder, "GdSniper", 3 - side, 260, 0);
            if (busy) feeder.SetMoveTarget(side, rifleman.Id.Value, 0, -400);
            for (var step = 0; step < world.Frames(10); step++) world.Step();
            var start = (rifleman.X, rifleman.Y);
            feeder.FireWeapon(3 - side, sniper.Id.Value, friend.Id.Value);
            var furthest = Fixed.Zero;
            for (var step = 0; step < world.Frames(120); step++)
            {
                world.Step();
                var dx = rifleman.X - start.X;
                furthest = Fixed.Max(furthest, dx);
            }
            return furthest;
        }

        Check.That(Moved(human: true, busy: false) > Fixed.FromInt(30), "A human player's idle rifleman drives at the sniper that shot its friend.");
        Check.That(Moved(human: false, busy: false) < Fixed.FromInt(5), "A computer player's rifleman does not.");
        Check.That(Moved(human: true, busy: true) < Fixed.FromInt(5), "A rifleman that is walking does not join.");
    }

    /// <summary>
    /// The rule fix <c>guard-answers-only-when-idle</c> (the community source; Reforged's R-35): a guard (flying units only, so its own scans pass a ground sniper by) walks back to its post
    /// 200 away and is shot by a sniper on the way. Under the original rules RETURN answers at once and turns to the sniper; with the fix it walks on to its post and answers there.
    /// At 30, 60 and 120 Hz.
    /// </summary>
    public static void GameFixGuardAnswersOnlyWhenIdle()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            Fixed FromPost(CnCNew.Simulation.Rules.RuleProfile rules)
            {
                var (world, feeder, _) = GuardWorld(rate, rules: rules);
                var guardian = GdSpawn(world, feeder, "FcRifleman", 1, 200, 0);
                var sniper = GdSpawn(world, feeder, "GdSniper", 2, 100, 150);
                feeder.Guard(1, [guardian.Id.Value], 0, 0, mode: GuardMode.FlyingUnitsOnly);
                for (var step = 0; step < world.Frames(30); step++) world.Step();
                feeder.FireWeapon(2, sniper.Id.Value, guardian.Id.Value);
                for (var step = 0; step < world.Frames(150); step++) world.Step();
                return GdDistance(guardian, 0, 0);
            }

            var original = FromPost(CnCNew.Simulation.Rules.RuleProfile.Original);
            var fixedRun = FromPost(CnCNew.Simulation.Rules.RuleProfile.Parse("guard-answers-only-when-idle"));
            Check.That(original > Fixed.FromInt(60), $"{rate}: under the original rules the returning guard turns to the sniper ({original} from its post).");
            Check.That(fixedRun < Fixed.FromInt(40), $"{rate}: with the fix it walks on towards its post ({fixedRun} from it).");
        }
    }

    /// <summary>
    /// F-58 step 7, <c>DeployStyleAIUpdate::update</c>: a deploying unit on guard sets itself up at its post while it is idle there ("idle guarders deploy for fastest response");
    /// when the guard then chases an enemy beyond its weapon it packs first and only then drives (the guard's move waits, it is not lost), and deploys again to fire. At 30, 60
    /// and 120 Hz.
    /// </summary>
    public static void GuardDeployStyleDeploysAndPacks()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var (world, feeder, _) = GuardWorld(rate);
            var cannon = GdSpawn(world, feeder, "GdCannon", 1, 0, 0);
            var deploy = cannon.Modules.OfType<CnCNew.Simulation.Modules.BuiltIn.DeployStyleAIUpdate>().Single();
            Check.That(feeder.Guard(1, [cannon.Id.Value], 0, 0).Accepted, $"{rate}: the guard order is accepted.");
            for (var step = 0; step < world.Frames(60); step++) world.Step();
            Check.That(deploy.IsDeployed, $"{rate}: idle at its post, the guard has set itself up.");
            var dummy = GdSpawn(world, feeder, "GdDummy", 2, 200, 0);
            var (lastX, lastY) = (cannon.X, cannon.Y);
            var moved = false;
            for (var step = 0; step < world.Frames(900) && !dummy.EffectivelyDead && world.Entities.Contains(dummy); step++)
            {
                // Packed when the tick began (its move runs before it decides to set up for an attack in reach) or when it ended (a pack that finishes in the tick lets it drive in it).
                var packed = deploy.IsPacked;
                world.Step();
                packed |= deploy.IsPacked;
                if (cannon.X != lastX || cannon.Y != lastY)
                {
                    moved = true;
                    Check.That(packed, $"{rate}: it drives only when packed (tick {world.Tick}).");
                }
                (lastX, lastY) = (cannon.X, cannon.Y);
            }
            Check.That(moved && (dummy.EffectivelyDead || !world.Entities.Contains(dummy)), $"{rate}: it drove after the enemy and killed it (moved {moved}).");
        }
    }
}
