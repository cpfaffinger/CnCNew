using CnCNew.Simulation.Events;
using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// F-138: two shares of a frame above 30 Hz that were lost or set back. (1) An attack order turns a standing unit on the spot in the command phase (<c>TurnInPlace</c>: the first share
/// at once, the next one a tick later); the unit's own update in the same tick took that next share as missed and voided it, so the unit kept one step of its turn where the 30 Hz unit took
/// the frame whole. (2) A unit that stands on its move target decides an arrival with no step and no turn left; killed in the first tick of its frame, the death's own turn was set back by
/// the frame's last share.
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> F138Files()
    {
        var files = new Dictionary<string, string>(RatedWorldFiles());
        foreach (var (name, text) in ShockFiles) files[name] = text;
        foreach (var (name, text) in F138OwnFiles) files[name] = text;
        return files;
    }

    private static readonly IReadOnlyDictionary<string, string> F138OwnFiles = new Dictionary<string, string>
    {
        ["Data/INI/Object/F138Fixture.ini"] = """
        Locomotor F138Loco
          Surfaces = GROUND
          TurnRate = 113.0
          Speed = 47.0
          Acceleration = 31.0
          Braking = 53.0
          MinSpeed = 0.0
        End
        Weapon F138Gun
          PrimaryDamage = 1
          AttackRange = 500.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 1000
        End
        Weapon F138Blast
          PrimaryDamage = 500
          PrimaryDamageRadius = 100.0
          AttackRange = 200.0
          DamageType = EXPLOSION
          ShockWaveAmount = 50.0
          ShockWaveRadius = 100.0
          ShockWaveTaperOff = 0.33
        End
        Weapon F138Missile
          PrimaryDamage = 500
          PrimaryDamageRadius = 30.0
          AttackRange = 500.0
          DamageType = EXPLOSION
          ShockWaveAmount = 50.0
          ShockWaveRadius = 100.0
          ShockWaveTaperOff = 0.33
          WeaponSpeed = 60
          ClipSize = 0
          DelayBetweenShots = 100000
        End
        Object F138Launcher
          KindOf = STRUCTURE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY F138Missile
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object F138Victim
          KindOf = SELECTABLE VEHICLE
          Geometry = CYLINDER
          GeometryMajorRadius = 4.0
          GeometryMinorRadius = 4.0
          Locomotor = SET_NORMAL F138Loco
          Body = ActiveBody Body01
            MaxHealth = 5.0
            InitialHealth = 5.0
          End
          Behavior = PhysicsBehavior Physics01
            Mass = 5.0
            ShockMaxYaw = 0.3
            ShockMaxPitch = 0.1
            ShockMaxRoll = 0.1
            ForwardFriction = 30.0
            AerodynamicFriction = 3.0
            MinFallHeightForDamage = 1000.0
          End
          Behavior = SlowDeathBehavior Death01
            DestructionDelay = 5000
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object F138Gunner
          KindOf = SELECTABLE VEHICLE
          Geometry = CYLINDER
          GeometryMajorRadius = 4.0
          GeometryMinorRadius = 4.0
          Locomotor = SET_NORMAL F138Loco
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY F138Gun
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """,
    };

    /// <summary>Steps to the first tick of the next logic frame of the original (the world's next step is that tick).</summary>
    private static void F138ToFrameStart(SimulationWorld world)
    {
        while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
    }

    /// <summary>
    /// A standing gunner whose module is due in the tick of the order (something woke it - a wake request for that tick stands for a command, damage or a dock) is ordered to attack an enemy
    /// 90 degrees to its side, the order repeated every tick as the client does. At the end of each of the first frames its angle is the one the 30 Hz unit has (a full step of its turn rate
    /// per frame). Before, the 60 and 120 Hz unit's own update took the next share of the turn as missed and voided it: it stood half and a quarter of a step behind.
    /// </summary>
    public static void F138AttackOrderTurnsAStandingUnitLikeAt30Hz()
    {
        const int frames = 5;
        var reference = new Fixed[frames];
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, F138Files());
            feeder.Spawn(1, "F138Gunner", owner: 1, x: 100, y: 100);
            feeder.Spawn(2, "TurretTarget", owner: 2, x: 100, y: 200);
            feeder.SetRelationship(1, 2, Relationship.Enemies);
            world.Step();
            F138ToFrameStart(world);
            var gunner = Slotted(world, "F138Gunner");
            var victim = IdOf(world, "TurretTarget");
            var ai = gunner.Modules.OfType<AIUpdateInterface>().Single();
            var before = gunner.Angle;
            for (var frame = 0; frame < frames; frame++)
            {
                for (var share = 0; share < factor; share++)
                {
                    world.WakeAt(gunner, ai, world.Tick);
                    Check.That(feeder.FireWeapon(1, gunner.Id.Value, victim).Accepted, $"{rate}: the attack order is accepted.");
                    world.Step();
                }

                var turned = (gunner.Angle - before).Abs();
                if (rate == SimulationRate.Hz30) reference[frame] = turned;
                else Check.That((turned - reference[frame]).Abs() <= Fixed.FromDecimal(0.0005m),
                    $"{rate}: {frame + 1} frames after the order the unit has turned {turned.Raw}, at 30 Hz {reference[frame].Raw} (raw).");
                Check.That(turned > Fixed.Zero, $"{rate}: the unit turns toward its victim.");
            }
        }
    }

    /// <summary>
    /// A unit stands on its move target and is ordered there again in every frame, so it decides an arrival with no step and no turn in the first tick of each frame. A missile that
    /// kills and flings it lands after its update, in every tick of a frame in turn (the launch is moved by a tick from case to case; the unit's frames lie skewed against the world's
    /// as well). The physics module is woken for the tick after the death - what any impulse that turns the dead object in that tick stands for - and the heading it gives must
    /// stay: the frame's last share, one tick after the death, used to set the angle back to the one the arrival began with.
    /// </summary>
    public static void F138DeathSettlesAnArrivalThatOnlyHasItsAngleLeft()
    {
        var deaths = 0;
        var atFrameStart = 0;
        foreach (var (rate, factor) in WorldRates)
            for (var offset = 0; offset < factor; offset++)
                for (var skew = 0; skew < factor; skew++)
                {
                    var (world, feeder, _) = RatedWorld(rate, F138Files());
                    feeder.Spawn(1, "F138Launcher", owner: 1, x: 0, y: 0);
                    feeder.SetRelationship(1, 2, Relationship.Enemies);
                    world.Step();
                    for (var index = 0; index < skew; index++) world.Step();
                    feeder.Spawn(2, "F138Victim", owner: 2, x: 50, y: 0);
                    world.Step();
                    var unit = Slotted(world, "F138Victim");
                    var launcher = Slotted(world, "F138Launcher");
                    var physics = unit.Modules.OfType<PhysicsBehavior>().Single();
                    F138ToFrameStart(world);
                    var fired = false;
                    var died = false;
                    for (var tick = 0; tick < 200 * factor && !died; tick++)
                    {
                        if (world.Time.BeginsFrameOf(unit, world.Tick)) feeder.SetMoveTarget(2, unit.Id.Value, 50, 0);
                        if (!fired && tick == offset) fired = feeder.FireWeapon(1, launcher.Id.Value, unit.Id.Value).Accepted;
                        var result = world.Step();
                        if (result.Events.Any(value => value.Kind == SimEventKind.EntityDied && value.Entity == unit.Id))
                        {
                            died = true;
                            deaths++;
                            if (world.Time.BeginsFrameOf(unit, world.Tick)) atFrameStart++;
                        }
                    }

                    Check.That(fired && died, $"{rate}: the missile kills the unit.");
                    var angleAtDeath = unit.Angle;
                    world.WakeAt(unit, physics, world.Tick);
                    world.Step();
                    Check.That(unit.Angle != angleAtDeath,
                        $"{rate}, launch +{offset}, skew {skew}: the heading the fling gives the dead unit in the tick after its death stays ({angleAtDeath.Raw} before, {unit.Angle.Raw} after).");
                }

        Check.That(deaths > 0 && atFrameStart > 0, $"Setup: some missiles landed in the first tick of the unit's frame ({atFrameStart} of {deaths}).");
    }
}
