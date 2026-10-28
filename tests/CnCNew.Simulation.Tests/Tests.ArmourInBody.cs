using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

// F-48: ActiveBody::attemptDamage applies the armour to whatever reaches the body; a weapon passes its raw damage, HighlanderBody and UndeadBody cap and decide on the
// raw amount, and attemptHealing scales a heal by the armour as well.
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> ArmourFiles() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/ArmourFixture.ini"] = """
        Weapon ArmourGun
          PrimaryDamage = 150
          AttackRange = 10.0
          DamageType = EXPLOSION
        End

        Armor ArmourWeak
          Armor = Default 200%
        End

        Armor ArmourHalf
          Armor = Default 50%
        End

        Object ArmourShooter
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
        End

        Object ArmourHero
          KindOf = SELECTABLE INFANTRY
          ArmorSet
            Conditions = None
            Armor = ArmourWeak
          End
          Body = HighlanderBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object ArmourTank
          KindOf = SELECTABLE VEHICLE
          ArmorSet
            Conditions = None
            Armor = ArmourHalf
          End
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object ArmourBus
          KindOf = SELECTABLE VEHICLE
          ArmorSet
            Conditions = None
            Armor = ArmourHalf
          End
          Body = UndeadBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
            SecondLifeMaxHealth = 200.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    private static (SimulationWorld World, Feeder Feeder) ArmourWorld()
    {
        var world = NewWorld(new SyntheticFixture(TestRate.Default, files: ArmourFiles()));
        return (world, new Feeder(world));
    }

    private static Entity ArmourSpawn(SimulationWorld world, Feeder feeder, string template, int owner, int x)
    {
        feeder.Spawn(owner, template, owner: owner, x: x, y: 100);
        world.Step();
        return world.Entities.Where(value => value.Template.Name == template).OrderBy(value => value.Id.Value).Last();
    }

    public static void ArmourIsAppliedByTheBody()
    {
        // A weapon's damage: the hero's HighlanderBody caps the raw 150 to 99 and its armour doubles that - it dies, where an armoured amount capped would leave it one point.
        {
            var (world, feeder) = ArmourWorld();
            var shooter = ArmourSpawn(world, feeder, "ArmourShooter", 1, 100);
            var hero = ArmourSpawn(world, feeder, "ArmourHero", 2, 110);
            Check.That(world.Weapons.TryGet("ArmourGun", out var gun), "Setup: the weapon is in the catalog.");
            world.DealWeaponDamage(shooter, gun, hero, hero.X, hero.Y, hero.Z);
            Check.That(hero.EffectivelyDead, $"A hero weak to the weapon dies through the cap of its raw amount ({hero.Body!.Health}).");
        }
        // A weapon's damage at an UndeadBody: the raw 150 reaches its health of 100, so its second life begins, though the armour halves the blow.
        {
            var (world, feeder) = ArmourWorld();
            var shooter = ArmourSpawn(world, feeder, "ArmourShooter", 1, 100);
            var bus = ArmourSpawn(world, feeder, "ArmourBus", 2, 110);
            world.Weapons.TryGet("ArmourGun", out var gun);
            world.DealWeaponDamage(shooter, gun, bus, bus.X, bus.Y, bus.Z);
            Check.That(((UndeadBody)bus.Body!).IsSecondLife && bus.Body.MaxHealth == Fixed.FromInt(200), "The bus's second life begins on the raw amount.");
        }
        // Damage that is not a weapon's (here the test command's) reaches the body through its armour as well, and so does healing.
        {
            var (world, feeder) = ArmourWorld();
            var tank = ArmourSpawn(world, feeder, "ArmourTank", 2, 110);
            feeder.Damage(tank.Id.Value, 40m);
            world.Step();
            Check.That(tank.Body!.Health == Fixed.FromInt(80), $"Half armour takes half of 40 ({tank.Body.Health}).");
            feeder.Damage(tank.Id.Value, 20m, damageType: DamageTypes.Healing);
            world.Step();
            Check.That(tank.Body.Health == Fixed.FromInt(90), $"Half armour heals half of 20 ({tank.Body.Health}).");
        }
    }
}
