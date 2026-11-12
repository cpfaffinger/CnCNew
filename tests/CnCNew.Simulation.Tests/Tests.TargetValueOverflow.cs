using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>
/// #301: the target value of rule fix 27 (<c>target-by-damage-potential</c>, the port's own; the original's <c>getNextMoodTarget</c> has no such value) divides the damage a weapon
/// does in its window by the victim's health. A victim left with a sliver of health (a troop crawler at 40 raw units, 2.4e-6, in <c>econ-survey</c> Death Valley China against GLA seed 8)
/// before a weapon of a million damage (a terrorist's suicide weapon) made a quotient beyond the range of <see cref="Fixed"/>, and the match ended with an <see cref="OverflowException"/>.
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> TargetValueOverflowFiles() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/TargetValueOverflowFixture.ini"] = """
        Weapon TvoBlast
          PrimaryDamage = 1000000
          AttackRange = 10.0
          DamageType = EXPLOSION
        End

        Weapon TvoPistol
          PrimaryDamage = 1
          AttackRange = 100.0
          DamageType = EXPLOSION
          DelayBetweenShots = 1000
        End

        Object TvoBomber
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY TvoBlast
          End
        End

        Object TvoGunner
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY TvoPistol
          End
        End

        Object TvoWreck
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 240.0
            InitialHealth = 0.0000024
          End
        End

        Object TvoTruck
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 240.0
            InitialHealth = 240.0
          End
        End
        """,
    };

    /// <summary>
    /// The kill rate is the share of the health taken, at most the whole: a million damage against a sliver of health is the whole victim, not an overflow; a pistol against a whole
    /// truck keeps its share below one. At 30, 60 and 120 Hz.
    /// </summary>
    public static void TargetValueOfASliverOfHealthIsWhole()
    {
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, TargetValueOverflowFiles());
            feeder.Spawn(1, "TvoBomber", owner: 1, x: 0);
            feeder.Spawn(1, "TvoGunner", owner: 1, x: 0, y: 50);
            feeder.Spawn(2, "TvoWreck", owner: 2, x: 30);
            feeder.Spawn(2, "TvoTruck", owner: 2, x: 30, y: 50);
            for (var tick = 0; tick < factor; tick++) world.Step();
            Entity Get(string name) => world.FindEntity(new EntityId(IdOf(world, name)))!;
            var (bomber, gunner, wreck, truck) = (Get("TvoBomber"), Get("TvoGunner"), Get("TvoWreck"), Get("TvoTruck"));
            Check.That(wreck.Body!.Health > Fixed.Zero && wreck.Body.Health < Fixed.FromDecimal(0.00001m), $"{rate}: the wreck has a sliver of health ({wreck.Body.Health.Raw} raw).");
            var onWreck = world.TargetValueParts(bomber, wreck);
            Check.That(onWreck.KillRate == Fixed.One, $"{rate}: a million damage takes the whole sliver ({onWreck.KillRate}).");
            var onTruck = world.TargetValueParts(bomber, truck);
            Check.That(onTruck.KillRate == Fixed.One, $"{rate}: a million damage takes the whole truck ({onTruck.KillRate}).");
            var pistol = world.TargetValueParts(gunner, truck);
            Check.That(pistol.KillRate > Fixed.Zero && pistol.KillRate < Fixed.One, $"{rate}: a pistol takes a share of the truck ({pistol.KillRate}).");
        }
    }
}
