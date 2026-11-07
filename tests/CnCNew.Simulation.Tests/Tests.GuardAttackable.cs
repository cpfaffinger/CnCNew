using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// A unit on guard attacks only what it can fight (<c>PartitionFilterPossibleToAttack</c>, <c>getAbleToAttackSpecificObject</c>): a sniper's round does nothing to a vehicle's armour, so
/// a guarding sniper neither walks at a vehicle in its guard area nor stays on it once it has come near, and walks back to its post.
/// </summary>
static partial class Tests
{
    private const string GuardAttackableIni = """
        Armor GdHardArmor
          Armor = Default 100%
          Armor = SNIPER 0%
        End
        Weapon GdSnipeRifle
          PrimaryDamage = 50
          AttackRange = 120.0
          DamageType = SNIPER
          ClipSize = 0
          DelayBetweenShots = 300
        End
        Object GdMarksman
          KindOf = SELECTABLE INFANTRY
          VisionRange = 150.0
          Locomotor = SET_NORMAL FcFeet
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY GdSnipeRifle
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = Yes
            MoodAttackCheckRate = 250
          End
        End
        Object GdWreck
          KindOf = VEHICLE HULK UNATTACKABLE NO_COLLIDE
          Body = ActiveBody Body01
            MaxHealth = 1.0
            InitialHealth = 1.0
          End
        End
        Object GdTank
          KindOf = SELECTABLE VEHICLE
          VisionRange = 100.0
          ArmorSet
            Conditions = None
            Armor = GdHardArmor
          End
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
        End
        """;

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture) GuardAttackableWorld(SimulationRate rate, bool computer)
    {
        var fixture = new SyntheticFixture(rate, files: FireFromContainerFiles(GuardIni + Environment.NewLine + GuardAttackableIni), rules: CnCNew.Simulation.Rules.RuleProfile.Original);
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, computer ? ComputerAgainstHuman : CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        return (world, new Feeder(world), fixture);
    }

    /// <summary>The furthest a guard of the post (0, 0) goes from it in 20 s with an enemy of <paramref name="enemy"/> standing 200 away, and how far from the post it ends.</summary>
    private static (Fixed Furthest, Fixed End) GuardAttackableRun(SimulationRate rate, string enemy, bool computer = false)
    {
        var (world, feeder, _) = GuardAttackableWorld(rate, computer);
        var sniper = GdSpawn(world, feeder, "GdMarksman", 1, 0, 0);
        // The enemy stands where only a walking guard could reach it, inside the guard's inner range.
        GdSpawn(world, feeder, enemy, 2, computer ? 150 : 200, 0);
        feeder.Guard(1, [sniper.Id.Value], 0, 0);
        var furthest = Fixed.Zero;
        for (var step = 0; step < world.Frames(600); step++)
        {
            world.Step();
            furthest = Fixed.Max(furthest, GdDistance(sniper, 0, 0));
        }
        return (furthest, GdDistance(sniper, 0, 0));
    }

    /// <summary>
    /// The guard's look and its attack ask what the unit's idle look asks (<c>PartitionFilterPossibleToAttack</c> is <c>getAbleToAttackSpecificObject</c>): armour that takes nothing from the weapon, and
    /// objects of kind <c>UNATTACKABLE</c> (the wreck a vehicle leaves, which has default armour a sniper's round would "hurt") are no victims.
    /// </summary>
    public static void GuardIgnoresWhatItCannotHurt()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        foreach (var computer in new[] { false, true })
        {
            var tank = GuardAttackableRun(rate, "GdTank", computer);
            Check.That(tank.Furthest < Fixed.FromInt(5), $"{rate} (computer {computer}): a guarding sniper does not walk at a vehicle its round cannot hurt (furthest {tank.Furthest}).");
            var wreck = GuardAttackableRun(rate, "GdWreck", computer);
            Check.That(wreck.Furthest < Fixed.FromInt(5), $"{rate} (computer {computer}): a guarding sniper does not walk at the wreck of a vehicle, which no weapon can target (furthest {wreck.Furthest}).");
            var soft = GuardAttackableRun(rate, "GdDummy", computer);
            Check.That(soft.Furthest > Fixed.FromInt(20), $"{rate} (computer {computer}): the same guard does walk at an enemy it can hurt (furthest {soft.Furthest}).");
        }
    }
}
