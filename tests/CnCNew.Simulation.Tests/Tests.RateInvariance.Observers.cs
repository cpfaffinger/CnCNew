using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Phase 23, round 3 (docs/RATE-INVARIANCE-ROUND3-OBSERVERS.md): whoever looks at another object to decide something reads it where the logic frame of the original it has begun
/// ends, a unit decides its attack in the ticks its own frames begin with, a move ends in the first tick of its last frame, and "the next tick" of the phases after the updates
/// is the next frame. Together: a fight of two computer players' groups is, at the end of every logic frame, the 30 Hz fight.
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> ObserverFiles() => new Dictionary<string, string>(AcquisitionFiles())
    {
        ["Data/INI/Object/RateInvarianceObserversFixture.ini"] = """
        Weapon ObsBlast
          PrimaryDamage = 12
          PrimaryDamageRadius = 20.0
          AttackRange = 120.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 700
        End
        Weapon ObsTurretGun
          PrimaryDamage = 6
          AttackRange = 150.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 400
        End

        Object ObsSoldier
          KindOf = SELECTABLE INFANTRY
          VisionRange = 150.0
          Locomotor = SET_NORMAL ChaseLoco
          Body = ActiveBody Body01
            MaxHealth = 90.0
            InitialHealth = 90.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY AcqGun
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = Yes
            MoodAttackCheckRate = 250
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object ObsGrenadier
          KindOf = SELECTABLE INFANTRY
          VisionRange = 150.0
          Locomotor = SET_NORMAL ChaseLoco
          Body = ActiveBody Body01
            MaxHealth = 70.0
            InitialHealth = 70.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY ObsBlast
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = Yes
            MoodAttackCheckRate = 250
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object ObsTank
          KindOf = SELECTABLE VEHICLE
          VisionRange = 150.0
          Locomotor = SET_NORMAL ChaseLoco
          Body = ActiveBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY ObsTurretGun
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = Yes
            MoodAttackCheckRate = 250
            Turret
              TurretTurnRate = 180
              AllowsPitch = No
              RecenterTime = 500
              ControlledWeaponSlots = PRIMARY
            End
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    private static readonly ImmutableArray<PlayerConfiguration> TwoComputers = [new(new PlayerId(1), "Alpha", IsComputer: true), new(new PlayerId(2), "Beta", IsComputer: true)];

    /// <summary>One line per living object at the end of a logic frame: everything a fight decides with.</summary>
    private static string ObserverFrame(SimulationWorld world)
    {
        var text = new System.Text.StringBuilder();
        foreach (var entity in world.Entities.OrderBy(value => value.Id.Value))
        {
            text.Append(entity.Id.Value.ToString(CultureInfo.InvariantCulture)).Append(':')
                .Append(entity.X.Raw.ToString(CultureInfo.InvariantCulture)).Append(',').Append(entity.Y.Raw.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(entity.Angle.Raw.ToString(CultureInfo.InvariantCulture)).Append(',').Append((entity.Body?.Health ?? Fixed.Zero).Raw.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(entity.HasMoveTarget ? 'm' : 's').Append(entity.EffectivelyDead ? 'd' : 'a').Append(world.AcquiredVictimOf(entity).Value.ToString(CultureInfo.InvariantCulture));
            if (entity.TurretOf(0) is { } turret) text.Append(',').Append(turret.Angle.Raw.ToString(CultureInfo.InvariantCulture));
            text.Append(';');
        }
        return text.ToString();
    }

    private static (List<string> Frames, List<int> Shots, int Left) ObserverFight(SimulationRate rate, int frames)
    {
        var fixture = new SyntheticFixture(rate, files: ObserverFiles());
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, TwoComputers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        var feeder = new Feeder(world);
        var stride = rate.TicksPerOriginalFrame;
        var kinds = new[] { "ObsSoldier", "ObsGrenadier", "ObsSoldier", "ObsTank", "ObsGrenadier", "ObsSoldier" };
        for (var index = 0; index < kinds.Length; index++)
        {
            feeder.Spawn(1, kinds[index], owner: 1, x: 0, y: 30 * index);
            feeder.Spawn(2, kinds[kinds.Length - 1 - index], owner: 2, x: 700, y: 20 + 30 * index);
        }
        // The objects appear with the first tick; orders are given at a frame boundary, as a computer player gives them.
        for (var tick = 0; tick < stride; tick++) world.Step();
        foreach (var entity in world.Entities.OrderBy(value => value.Id.Value).ToArray())
            Check.That(feeder.AttackMove(entity.Owner.Value, entity.Id.Value, entity.Owner.Value == 1 ? 700 : 0, 90).Accepted, $"{rate}: the attack-move of {entity.Id.Value} is accepted.");
        var trace = new List<string>();
        var shots = new List<int>();
        for (var frame = 0; frame < frames; frame++)
        {
            var fired = 0;
            for (var tick = 0; tick < stride; tick++) fired += world.Step().Events.Count(value => value.Kind == SimEventKind.WeaponFired);
            trace.Add(ObserverFrame(world));
            shots.Add(fired);
        }
        return (trace, shots, world.Entities.Count());
    }

    public static void RateInvarianceObserversAFightIsTheThirtyHertzFight()
    {
        const int frames = 1500;
        var reference = ObserverFight(SimulationRate.Hz30, frames);
        Check.That(reference.Shots.Sum() > 100 && reference.Left < 12, $"30 Hz: the groups fight (shots {reference.Shots.Sum()}, {reference.Left} of 12 objects left).");
        foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var fight = ObserverFight(rate, frames);
            var first = Enumerable.Range(0, frames).FirstOrDefault(index => fight.Frames[index] != reference.Frames[index] || fight.Shots[index] != reference.Shots[index], -1);
            Check.That(first < 0, $"{rate}: positions, headings, health, orders, victims, turrets and the shots of every logic frame are those of 30 Hz "
                                  + $"(first difference in frame {first}: shots {(first < 0 ? 0 : fight.Shots[first])} against {(first < 0 ? 0 : reference.Shots[first])}).");
        }
    }
}
