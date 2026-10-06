using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    private static readonly int[] VeteranXpRequired = [0, 100, 250, 600];
    private static readonly int[] VeteranXpValue = [10, 30, 90, 300];

    /// <summary>
    /// Veterancy fixture. Every number is chosen so that the Q16.16 arithmetic is exact (health bonuses 125/150/200 %, damage bonuses
    /// in quarters), which lets the golden master compare with integer models instead of tolerances.
    /// </summary>
    private static IReadOnlyDictionary<string, string> VeterancyFiles() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/GameData.ini"] = """
        GameData
          HealthBonus_Veteran = 125%
          HealthBonus_Elite = 150%
          HealthBonus_Heroic = 200%
          WeaponBonus = VETERAN RANGE 110%
          WeaponBonus = ELITE DAMAGE 125%
          WeaponBonus = HERO DAMAGE 125%
        End
        """,
        ["Data/INI/Object/VeterancyFixture.ini"] = """
        Weapon VetGun
          PrimaryDamage = 20
          AttackRange = 50.0
          DamageType = EXPLOSION
          ClipSize = 2
          ClipReloadTime = 2000
          DelayBetweenShots = 500
          WeaponBonus = VETERAN DAMAGE 150%
          WeaponBonus = ELITE DAMAGE 150%
          WeaponBonus = VETERAN RATE_OF_FIRE 125%
          WeaponBonus = ELITE RATE_OF_FIRE 200%
          WeaponBonus = HERO RATE_OF_FIRE 150%
          WeaponBonus = PLAYER_UPGRADE DAMAGE 200%
        End
        Armor NoArmor
          Armor = Default 100%
        End

        Upgrade Upgrade_Scalar
          Type = OBJECT
          BuildCost = 10
          BuildTime = 1
        End
        Upgrade Upgrade_Booster
          Type = OBJECT
          BuildCost = 10
          BuildTime = 1
        End

        Object Recruit
          KindOf = SELECTABLE INFANTRY
          IsTrainable = Yes
          ExperienceValue = 10 30 90 300
          ExperienceRequired = 0 100 250 600
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY VetGun
          End
          Behavior = ExperienceScalarUpgrade Scalar01
            TriggeredBy = Upgrade_Scalar
            AddXPScalar = 0.25
          End
          Behavior = WeaponBonusUpgrade Bonus01
            TriggeredBy = Upgrade_Booster
          End
        End

        Object Graduate
          KindOf = SELECTABLE INFANTRY
          IsTrainable = Yes
          ExperienceValue = 10 30 90 300
          ExperienceRequired = 0 100 250 600
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = VeterancyGainCreate Grad01
            StartingLevel = ELITE
          End
        End

        Object Dummy
          KindOf = SELECTABLE INFANTRY
          IsTrainable = Yes
          ExperienceValue = 10 30 90 300
          ExperienceRequired = 0 100 250 600
          Body = ActiveBody Body01
            MaxHealth = 1.0
            InitialHealth = 1.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object Decoy
          KindOf = SELECTABLE INFANTRY IGNORED_IN_GUI
          ExperienceValue = 500 500 500 500
          Body = ActiveBody Body01
            MaxHealth = 1.0
            InitialHealth = 1.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object Tank
          KindOf = SELECTABLE VEHICLE
          ExperienceValue = 10 30 90 300
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object Gunner
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY VetGun
          End
        End
        """,
    };

    private static SimulationWorld VeterancyWorld(out SyntheticFixture fixture, out Feeder feeder)
    {
        fixture = new SyntheticFixture(SimulationRate.Original, files: VeterancyFiles());
        var world = NewWorld(fixture);
        feeder = new Feeder(world);
        return world;
    }

    private static int VeterancyLevelFor(int experience)
    {
        var level = 0;
        for (var index = 0; index < VeteranXpRequired.Length; index++)
            if (experience >= VeteranXpRequired[index]) level = index;
        return level;
    }

    private static Fixed VeteranHealthBonus(int level) => level switch { 0 => Fixed.One, 1 => Fixed.FromDecimal(1.25m), 2 => Fixed.FromDecimal(1.5m), _ => Fixed.FromDecimal(2m) };

    /// <summary>Spawns a one-hit-point victim of the given veterancy at (10,0) for player 2 and shoots it with the recruit until it dies.</summary>
    private static void KillDummy(SimulationWorld world, Feeder feeder, uint shooter, int victimLevel)
    {
        feeder.Spawn(2, "Dummy", x: 10);
        world.Step();
        var dummy = world.Entities.Where(value => value.Template.Name == "Dummy").OrderBy(value => value.Id.Value).Last();
        if (victimLevel > 0) world.SetVeterancy(dummy.Id, victimLevel);
        for (var tick = 0; tick < 200 && world.FindEntity(dummy.Id) is { } alive && alive.Body!.Health > Fixed.Zero; tick++)
        {
            feeder.FireWeapon(1, shooter, dummy.Id.Value);
            world.Step();
        }
        Check.That(world.FindEntity(dummy.Id) is not { } left || left.Body!.Health <= Fixed.Zero, "The dummy dies.");
    }

    /// <summary>
    /// Experience on kill, level thresholds, health scaling and the engine-made veterancy upgrades. The expected experience, level and
    /// maximum health after every kill are recomputed from the rules (<c>ExperienceTracker::addExperiencePoints</c>: experience of the
    /// victim's level, level = highest threshold reached; <c>Object::onVeterancyLevelChanged</c>: max health by the ratio of the health
    /// bonuses with the health ratio preserved).
    /// </summary>
    public static void VeterancyExperienceAndHealthGoldenMaster()
    {
        var world = VeterancyWorld(out _, out var feeder);
        feeder.Spawn(1, "Recruit", x: 0);
        world.Step();
        var recruitId = IdOf(world, "Recruit");
        var recruit = world.FindEntity(new EntityId(recruitId))!;
        Check.That(recruit.VeterancyLevel == 0 && recruit.Experience == 0, "A fresh recruit is at level 0 with no experience.");

        var experience = 0;
        foreach (var victimLevel in new[] { 0, 0, 1, 2, 2, 3, 3 })
        {
            KillDummy(world, feeder, recruitId, victimLevel);
            experience += VeteranXpValue[victimLevel];
            var level = VeterancyLevelFor(experience);
            Check.That(recruit.Experience == experience, $"After killing a level-{victimLevel} dummy the recruit holds {experience} experience (has {recruit.Experience}).");
            Check.That(recruit.VeterancyLevel == level, $"Experience {experience} means level {level} (has {recruit.VeterancyLevel}).");
            var expectedMax = Fixed.FromInt(100) * VeteranHealthBonus(level);
            Check.That(recruit.Body!.MaxHealth == expectedMax && recruit.Body.Health == expectedMax, $"Level {level}: maximum health {expectedMax.Floor()} with the ratio preserved (has {recruit.Body.MaxHealth.Floor()}/{recruit.Body.Health.Floor()}).");
            var conditionBit = level switch { 0 => 0u, 1 => 1u << WeaponBonusTable.VeteranCondition, 2 => 1u << WeaponBonusTable.EliteCondition, _ => 1u << WeaponBonusTable.HeroCondition };
            Check.That(recruit.WeaponBonusConditions == conditionBit, $"Level {level} sets exactly its own weapon bonus condition (has {recruit.WeaponBonusConditions}).");
        }
        Check.That(recruit.VeterancyLevel == 3 && experience == 10 + 10 + 30 + 90 + 90 + 300 + 300, "The kill sequence ends at level 3 with 830 experience.");
        foreach (var name in new[] { "Upgrade_Veterancy_VETERAN", "Upgrade_Veterancy_ELITE", "Upgrade_Veterancy_HEROIC" })
        {
            Check.That(world.Upgrades.TryGet(name, out var info) && recruit.CompletedUpgrades.Has(info.Index), $"The engine grants {name} on the way up.");
        }

        // The engine upgrades are not for sale.
        feeder.GiveMoney(1, 500);
        world.Step();
        var before = world.RejectionCounts[(int)CommandRejection.NotPermitted];
        feeder.PurchaseUpgrade(1, "Upgrade_Veterancy_VETERAN", target: recruitId);
        world.Step();
        Check.That(world.RejectionCounts[(int)CommandRejection.NotPermitted] == before + 1, "A veterancy upgrade cannot be purchased.");
    }

    /// <summary>Players without a killing entity of their own, non-trainable killers and <c>IGNORED_IN_GUI</c> victims give no experience.</summary>
    public static void VeterancyGrantRules()
    {
        var world = VeterancyWorld(out _, out var feeder);
        feeder.Spawn(1, "Recruit", x: 0);
        feeder.Spawn(1, "Gunner", x: 0);
        world.Step();
        var recruit = world.FindEntity(new EntityId(IdOf(world, "Recruit")))!;
        var gunner = world.FindEntity(new EntityId(IdOf(world, "Gunner")))!;

        KillDummy(world, feeder, gunner.Id.Value, 0);
        Check.That(gunner.Experience == 0 && gunner.VeterancyLevel == 0, "A non-trainable killer gains no experience.");

        feeder.Spawn(2, "Decoy", x: 10);
        world.Step();
        var decoy = world.Entities.Single(value => value.Template.Name == "Decoy");
        for (var tick = 0; tick < 200 && decoy.Body!.Health > Fixed.Zero; tick++)
        {
            feeder.FireWeapon(1, recruit.Id.Value, decoy.Id.Value);
            world.Step();
        }
        Check.That(decoy.Body!.Health <= Fixed.Zero && recruit.Experience == 0, "Killing an IGNORED_IN_GUI object gives no experience.");

        KillDummy(world, feeder, recruit.Id.Value, 0);
        Check.That(recruit.Experience == VeteranXpValue[0], "A normal enemy kill still counts.");
    }

    /// <summary>
    /// Weapon bonuses by veterancy level. The model is <c>Weapon::computeBonus</c> (1 + the sum of the deltas of the global and the
    /// weapon's own bonus for every set condition) for damage and range, and floor(time / rate-of-fire multiplier) for the shot delay and
    /// the clip reload. Level 1 also has +10 % global range, so a target at 52 (range 50) is reachable at level 1 only, which also proves
    /// that reaching level 2 clears the level-1 condition.
    /// </summary>
    public static void VeterancyWeaponBonusGoldenMaster()
    {
        // (damage multiplier in quarters, rate of fire multiplier in quarters) per level, from the fixture's bonus lines.
        var damageQuarters = new[] { 4, 6, 4 + 1 + 2, 4 + 1 };   // 1.0; 1.0+0.5; 1.0+0.25+0.5; 1.0+0.25
        var rateQuarters = new[] { 4, 5, 8, 6 };                  // 1.0; 1.25; 2.0; 1.5
        for (var level = 0; level <= 3; level++)
        {
            var world = VeterancyWorld(out _, out var feeder);
            feeder.Spawn(1, "Recruit", x: 0);
            feeder.Spawn(2, "Tank", x: 40);
            world.Step();
            var recruitId = IdOf(world, "Recruit");
            world.SetVeterancy(new EntityId(recruitId), level);
            var near = IdOf(world, "Tank");

            var ticks = new List<long>();
            var damages = new List<long>();
            for (var i = 0; i < 400 && ticks.Count < 6; i++)
            {
                feeder.FireWeapon(1, recruitId, near);
                foreach (var simEvent in world.Step().Events.Where(value => value.Kind == SimEventKind.WeaponFired))
                {
                    ticks.Add(simEvent.Tick);
                    damages.Add(simEvent.Value);
                }
            }
            Check.That(ticks.Count == 6, $"Level {level}: six shots observed.");

            var rangeWorld = VeterancyWorld(out _, out var rangeFeeder);
            rangeFeeder.Spawn(1, "Recruit", x: 0);
            rangeFeeder.Spawn(2, "Tank", x: 52);
            rangeWorld.Step();
            rangeWorld.SetVeterancy(new EntityId(IdOf(rangeWorld, "Recruit")), level);
            var shots = 0;
            for (var i = 0; i < 5; i++)
            {
                rangeFeeder.FireWeapon(1, IdOf(rangeWorld, "Recruit"), IdOf(rangeWorld, "Tank"));
                shots += rangeWorld.Step().Events.Count(value => value.Kind == SimEventKind.WeaponFired);
            }
            Check.That((shots > 0) == (level == 1), $"Level {level}: a target at 52 is {(level == 1 ? "in" : "out of")} range (range 50, +10 % at level 1 only), shots {shots}.");

            var delay = 15 * 4 / rateQuarters[level];      // floor(15 / multiplier)
            var reload = 60 * 4 / rateQuarters[level];     // floor(60 / multiplier)
            var expected = new List<long>();
            var ready = ticks[0];
            var clip = 2;
            for (var shot = 0; shot < 6; shot++)
            {
                expected.Add(ready);
                clip--;
                if (clip == 0) { ready += reload; clip = 2; }
                else ready += delay;
            }
            Check.That(ticks.SequenceEqual(expected), $"Level {level}: shot ticks {string.Join(",", ticks)} must equal the model {string.Join(",", expected)} (delay {delay}, reload {reload}).");
            var expectedDamage = Fixed.FromInt(20 * damageQuarters[level] / 4).Raw;
            Check.That(damages.All(value => value == expectedDamage), $"Level {level}: every shot deals {20 * damageQuarters[level] / 4}.");
        }
    }

    /// <summary>ExperienceScalarUpgrade, WeaponBonusUpgrade (PLAYER_UPGRADE condition) and VeterancyGainCreate (the minimum level).</summary>
    public static void VeterancyUpgradesAndStartingLevel()
    {
        var world = VeterancyWorld(out var fixture, out var feeder);
        feeder.Spawn(1, "Recruit", x: 0);
        feeder.Spawn(1, "Graduate", x: 0);
        feeder.Spawn(2, "Tank", x: 40);
        world.Step();
        var recruit = world.FindEntity(new EntityId(IdOf(world, "Recruit")))!;
        var graduate = world.FindEntity(new EntityId(IdOf(world, "Graduate")))!;
        var tank = IdOf(world, "Tank");

        Check.That(graduate.VeterancyLevel == 2 && graduate.Experience == VeteranXpRequired[2], "VeterancyGainCreate sets the minimum level: elite with the experience of that level.");
        Check.That(graduate.Body!.MaxHealth == Fixed.FromInt(150) && graduate.Body.Health == Fixed.FromInt(150), "The starting level scales the spawned health (150 %).");
        Check.That(graduate.WeaponBonusConditions == 1u << WeaponBonusTable.EliteCondition, "The starting level sets the elite weapon bonus condition.");
        Check.That(world.Upgrades.TryGet("Upgrade_Veterancy_ELITE", out var elite) && graduate.CompletedUpgrades.Has(elite.Index), "The starting level grants the engine upgrade.");

        feeder.GiveMoney(1, 500);
        world.Step();
        feeder.PurchaseUpgrade(1, "Upgrade_Scalar", target: recruit.Id.Value);
        feeder.PurchaseUpgrade(1, "Upgrade_Booster", target: recruit.Id.Value);
        for (var i = 0; i < 5; i++) world.Step();
        Check.That(recruit.ExperienceScalar == Fixed.FromDecimal(1.25m), "ExperienceScalarUpgrade adds AddXPScalar to the scalar.");
        Check.That(recruit.WeaponBonusConditions == 1u << WeaponBonusTable.PlayerUpgradeCondition, "WeaponBonusUpgrade sets the PLAYER_UPGRADE condition.");

        KillDummy(world, feeder, recruit.Id.Value, 0);
        Check.That(recruit.Experience == 12, "10 experience at scalar 1.25 is floor(12.5) = 12.");

        var damages = new List<long>();
        for (var i = 0; i < 100 && damages.Count < 1; i++)
        {
            feeder.FireWeapon(1, recruit.Id.Value, tank);
            damages.AddRange(world.Step().Events.Where(value => value.Kind == SimEventKind.WeaponFired).Select(value => value.Value));
        }
        Check.That(damages.Count == 1 && damages[0] == Fixed.FromInt(40).Raw, "PLAYER_UPGRADE DAMAGE 200 % doubles the 20 damage.");

        // A round trip in the middle of a veteran career keeps every new field, and two restored copies stay identical.
        world.SetVeterancy(recruit.Id, 2);
        world.Step();
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
        Check.That(restored.StateHash == world.StateHash, "The veterancy state round-trips through a snapshot.");
        var copy = restored.FindEntity(recruit.Id)!;
        Check.That(copy.Experience == recruit.Experience && copy.ExperienceScalar == recruit.ExperienceScalar
            && copy.WeaponBonusConditions == recruit.WeaponBonusConditions && copy.VeterancyLevel == 2, "Experience, scalar, level and bonus conditions survive the snapshot.");
        var second = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
        for (var i = 0; i < 120; i++)
            Check.That(restored.Step().StateHash == second.Step().StateHash, "Two restored copies stay identical (tick " + i + ").");
    }

    private static IReadOnlyDictionary<string, string> VeteranSetFiles() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/VeteranSetFixture.ini"] = """
        Weapon WeakGun
          PrimaryDamage = 10
          AttackRange = 50.0
          DamageType = EXPLOSION
        End
        Weapon StrongGun
          PrimaryDamage = 99
          AttackRange = 50.0
          DamageType = EXPLOSION
        End
        Weapon TestHit
          PrimaryDamage = 100
          AttackRange = 50.0
          DamageType = EXPLOSION
        End
        Armor WeakArmor
          Armor = Default 100%
        End
        Armor StrongArmor
          Armor = Default 25%
        End
        Object Promotable
          KindOf = SELECTABLE INFANTRY
          IsTrainable = Yes
          ExperienceValue = 0 0 0 0
          ExperienceRequired = 0 1 2 3
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = DestroyDie Destroy01
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY WeakGun
          End
          WeaponSet
            Conditions = VETERAN
            Weapon = PRIMARY StrongGun
          End
          ArmorSet
            Conditions = None
            Armor = WeakArmor
          End
          ArmorSet
            Conditions = ELITE
            Armor = StrongArmor
          End
        End
        Object Attacker
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY TestHit
          End
        End
        Object VeteranDummy
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 1000000.0
            InitialHealth = 1000000.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    /// <summary>
    /// Phase 10: a veterancy-conditioned <c>WeaponSet</c>/<c>ArmorSet</c> (<c>Conditions = VETERAN</c>/<c>ELITE</c>/<c>HERO</c>) wins over the
    /// base set once the object reaches that rank, reduced to single-condition sets (see <c>ObjectTemplate.VeteranArmor</c>'s doc comment: no
    /// shipped Zero Hour object combines veterancy with another condition, so this covers everything the real game data needs).
    /// </summary>
    public static void VeterancyArmorAndWeaponSetSelection()
    {
        var fixture = new SyntheticFixture(SimulationRate.Original, files: VeteranSetFiles());
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), SimulationRate.Original, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "Promotable", x: 0, y: 0);
        feeder.Spawn(1, "VeteranDummy", x: 40, y: 0);
        feeder.Spawn(2, "Attacker", owner: 2, x: 0, y: 10);
        world.Step();
        var promotable = IdOf(world, "Promotable");
        var dummy = IdOf(world, "VeteranDummy");
        var attacker = IdOf(world, "Attacker");

        long FireAt(uint source, int player, uint target)
        {
            for (var i = 0; i < 50; i++)
            {
                feeder.FireWeapon(player, source, target);
                var hit = world.Step().Events.FirstOrDefault(value => value.Kind == SimEventKind.WeaponFired && value.Entity.Value == source);
                if (hit.Kind == SimEventKind.WeaponFired) return hit.Value;
            }
            throw new InvalidOperationException("The weapon never fired.");
        }

        Check.That(FireAt(promotable, 1, dummy) == Fixed.FromInt(10).Raw, "A regular object uses its default WeaponSet (WeakGun, 10 damage).");
        Check.That(FireAt(attacker, 2, promotable) == Fixed.FromInt(100).Raw, "A regular object uses its default ArmorSet too (WeakArmor 100 %, full 100 damage taken).");

        world.SetVeterancy(new EntityId(promotable), 1); // VETERAN
        Check.That(FireAt(promotable, 1, dummy) == Fixed.FromInt(99).Raw, "Once VETERAN, the WeaponSet Conditions = VETERAN (StrongGun, 99 damage) is used instead of the default.");
        Check.That(FireAt(attacker, 2, promotable) == Fixed.FromInt(100).Raw, "VETERAN alone does not switch the ArmorSet: no ELITE set exists for rank 1, so the default (100 %) still applies.");

        world.SetVeterancy(new EntityId(promotable), 2); // ELITE
        Check.That(FireAt(attacker, 2, promotable) == Fixed.FromInt(25).Raw, "Once ELITE, the ArmorSet Conditions = ELITE (StrongArmor 25 %) applies: 100 damage becomes 25.");
        Check.That(FireAt(promotable, 1, dummy) == Fixed.FromInt(10).Raw, "ELITE has no WeaponSet of its own, so the object falls back to the default WeaponSet, not the VETERAN one.");
    }
}
