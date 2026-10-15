using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

// The fixes of the community source adopted in Phase 32 (docs/GGC-FIXES.md section 3.1): each scene runs under the original profile and under a profile that
// names the fix, and shows the difference the fix makes.
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> GameFixFiles32() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/GameFixFixture32.ini"] = """
        Weapon FixGun
          PrimaryDamage = 1
          AttackRange = 10.0
          DamageType = EXPLOSION
        End

        Armor FixHalf
          Armor = Default 50%
        End

        Object FixSite
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = InstantDeathBehavior Death01
            Weapon = FixGun
          End
        End

        Object FixBus
          KindOf = SELECTABLE VEHICLE
          ArmorSet
            Conditions = None
            Armor = FixHalf
          End
          Body = UndeadBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
            SecondLifeMaxHealth = 200.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object FixCarrier
          KindOf = SELECTABLE VEHICLE TRANSPORT
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = TransportContain Transport01
            Slots = 4
            AllowInsideKindOf = VEHICLE
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object FixHumvee
          KindOf = SELECTABLE VEHICLE TRANSPORT
          TransportSlotCount = 2
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = TransportContain Transport01
            Slots = 2
            AllowInsideKindOf = INFANTRY
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object FixRider
          KindOf = SELECTABLE INFANTRY
          TransportSlotCount = 1
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object FixNuke
          KindOf = PROJECTILE
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = NeutronMissileSlowDeathBehavior Slow01
            DestructionDelay = 500
            Blast1Enabled = Yes
            Blast1Delay = 100
            Blast1InnerRadius = 40.0
            Blast1OuterRadius = 80.0
            Blast1MaxDamage = 100.0
            Blast1MinDamage = 10.0
          End
        End

        Object FixBigDummy
          KindOf = STRUCTURE
          Geometry = CYLINDER
          GeometryMajorRadius = 20.0
          GeometryMinorRadius = 20.0
          GeometryHeight = 10.0
          Body = StructureBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
        End

        Object FixVeteran
          KindOf = SELECTABLE VEHICLE
          IsTrainable = Yes
          ExperienceRequired = 0 100 250 600
          ExperienceValue = 10 30 90 300
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    private static SimulationWorld FixWorld(RuleProfile rules, out Feeder feeder)
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: GameFixFiles32(), rules: rules);
        var world = NewWorld(fixture);
        feeder = new Feeder(world);
        return world;
    }

    private static Entity FixSpawn(SimulationWorld world, Feeder feeder, string template, int owner, int x, int y)
    {
        feeder.Spawn(owner, template, owner: owner, x: x, y: y);
        world.Step();
        return world.Entities.Where(value => value.Template.Name == template).OrderBy(value => value.Id.Value).Last();
    }

    /// <summary><see cref="GameFix.NoDeathWeaponFromScaffold"/> (<c>InstantDeathBehavior::onDie</c>): a building site that dies fires its death weapon only in the original.</summary>
    public static void GameFixNoDeathWeaponFromScaffold()
    {
        int Shots(RuleProfile rules, bool scaffold)
        {
            var world = FixWorld(rules, out var feeder);
            var site = FixSpawn(world, feeder, "FixSite", 1, 100, 100);
            if (scaffold) world.SetStatus(site.Id, 1UL << ObjectStatusBits.UnderConstruction);
            world.Kill(site.Id);
            var shots = 0;
            for (var step = 0; step < 4; step++) shots += world.Step().Events.Count(value => value.Kind == SimEventKind.WeaponFireRequested && value.Text == "FixGun");
            return shots;
        }
        var rules = RuleProfile.Parse("no-death-weapon-from-scaffold");
        Check.That(Shots(RuleProfile.Original, true) == 1, "The original: a building site fires its death weapon.");
        Check.That(Shots(rules, true) == 0, "With the fix a building site fires none.");
        Check.That(Shots(rules, false) == 1, "With the fix a finished building still fires it.");
    }

    /// <summary><see cref="GameFix.NoExitFromContainedTransport"/> (<c>TransportContain::isSpecificRiderFreeToExit</c>): a rider of a transport that is itself carried cannot get out with the fix.</summary>
    public static void GameFixNoExitFromContainedTransport()
    {
        bool Exits(RuleProfile rules)
        {
            var world = FixWorld(rules, out var feeder);
            var carrier = FixSpawn(world, feeder, "FixCarrier", 1, 100, 100);
            var humvee = FixSpawn(world, feeder, "FixHumvee", 1, 110, 100);
            var rider = FixSpawn(world, feeder, "FixRider", 1, 120, 100);
            Check.That(feeder.EnterContainer(1, rider.Id.Value, humvee.Id.Value).Accepted, "The rider boards the humvee.");
            world.Step();
            Check.That(feeder.EnterContainer(1, humvee.Id.Value, carrier.Id.Value).Accepted, "The humvee boards the carrier.");
            world.Step();
            Check.That(rider.ContainedBy == humvee.Id && humvee.ContainedBy == carrier.Id, "Setup: a rider in a humvee in a carrier.");
            return feeder.ExitContainer(1, rider.Id.Value).Accepted;
        }
        Check.That(Exits(RuleProfile.Original), "The original: the rider may leave the carried humvee.");
        Check.That(!Exits(RuleProfile.Parse("no-exit-from-contained-transport")), "With the fix it may not.");
    }

    /// <summary><see cref="GameFix.NoVeterancyEffectsWhenDead"/> (<c>Object::onVeterancyLevelChanged</c>): a dead object keeps a new level but gets no weapon bonus from it with the fix.</summary>
    public static void GameFixNoVeterancyEffectsWhenDead()
    {
        (int Level, uint Conditions) Promote(RuleProfile rules)
        {
            var world = FixWorld(rules, out var feeder);
            var veteran = FixSpawn(world, feeder, "FixVeteran", 1, 100, 100);
            world.Kill(veteran.Id);
            world.AddExperiencePoints(veteran, 100);
            return (veteran.VeterancyLevel, veteran.WeaponBonusConditions);
        }
        var original = Promote(RuleProfile.Original);
        var fixedRun = Promote(RuleProfile.Parse("no-veterancy-effects-when-dead"));
        Check.That(original.Level == 1 && original.Conditions != 0, $"The original: a dead object that reaches a level gets its weapon bonus ({original.Level}, {original.Conditions}).");
        Check.That(fixedRun.Level == 1 && fixedRun.Conditions == 0, $"With the fix it keeps the level and gets no bonus ({fixedRun.Level}, {fixedRun.Conditions}).");
    }

    /// <summary><see cref="GameFix.NeutronBlastBoundingSphere"/> (<c>NeutronMissileSlowDeathBehavior::doBlast</c>): an object whose sphere reaches into the outer radius is hit with the fix only.</summary>
    public static void GameFixNeutronBlastBoundingSphere()
    {
        Fixed Lost(RuleProfile rules)
        {
            var world = FixWorld(rules, out var feeder);
            var nuke = FixSpawn(world, feeder, "FixNuke", 1, 100, 100);
            var dummy = FixSpawn(world, feeder, "FixBigDummy", 2, 190, 100);
            world.Kill(nuke.Id);
            for (var step = 0; step < world.Frames(30); step++) world.Step();
            return Fixed.FromInt(1000) - dummy.Body!.Health;
        }
        var original = Lost(RuleProfile.Original);
        var fixedRun = Lost(RuleProfile.Parse("neutron-blast-bounding-sphere"));
        Check.That(original == Fixed.Zero, $"The original: a centre 90 away from an 80 blast is not hit, whatever its size ({original}).");
        Check.That(fixedRun > Fixed.Zero, $"With the fix its bounding sphere (radius 20) reaches in and it is hit ({fixedRun}).");
    }

    /// <summary><see cref="GameFix.BoobyTrapClearedOnRemoval"/> (<c>StickyBombUpdate::onDelete</c>): a booby trap removed before it goes off unmarks its victim with the fix only.</summary>
    public static void GameFixStickyBombClearsTrapOnRemoval()
    {
        bool StillMarked(RuleProfile rules)
        {
            var world = CratesWorld(out var feeder, rules: rules);
            var victim = CsSpawn(world, feeder, "TrapVictim", 2, 100, 100);
            var bomb = CsSpawn(world, feeder, "FixtureBoobyTrap", 2, 0);
            bomb.Modules.OfType<StickyBombUpdate>().Single().InitStickyBombAtOffset(world, victim, Fixed.FromInt(4), Fixed.FromInt(-3));
            Check.That(victim.HasStatus(ObjectStatusBits.Index("BOOBY_TRAPPED")), "Setup: the victim is marked.");
            world.RequestDestroy(bomb.Id);
            CsRun(world, 3);
            Check.That(!CsExists(world, bomb), "Setup: the bomb is gone.");
            return victim.HasStatus(ObjectStatusBits.Index("BOOBY_TRAPPED"));
        }
        Check.That(StillMarked(RuleProfile.Original), "The original: the victim stays marked after its trap is gone.");
        Check.That(!StillMarked(RuleProfile.Parse("sticky-bomb-clears-trap-on-removal")), "With the fix the mark goes with the trap.");
    }

    /// <summary><see cref="GameFix.BunkerBusterOnlyWhenKillingSelf"/> (<c>BunkerBusterBehavior::onDie</c>): a bunker buster shot down on its way busts only in the original.</summary>
    public static void GameFixBunkerBusterOnlyWhenKillingSelf()
    {
        var rules = RuleProfile.Parse("bunker-buster-only-when-killing-self");
        var original = BusterRun(true, RuleProfile.Original, shootDown: true);
        var fixedRun = BusterRun(true, rules, shootDown: true);
        Check.That(original.Alive == 0, $"The original: a bomb killed in flight still busts the bunker it was aimed at ({original.Alive} alive).");
        Check.That(fixedRun.Alive == 2, $"With the fix it busts nothing ({fixedRun.Alive} alive).");
        Check.That(BusterRun(true, rules).Alive == 0, "With the fix a bomb that reaches its target still busts.");
    }

    /// <summary><see cref="GameFix.BountyRounding"/> and <see cref="GameFix.ProportionalSupplyBoost"/>: the arithmetic of the two fixes.</summary>
    public static void GameFixBountyAndSupplyArithmetic()
    {
        var fivePercent = Fixed.FromDecimal(0.05m);
        Check.That(GameFixRules.CashBounty(Fixed.FromInt(1000) * fivePercent, false) == 51, "The original: 5 % of 1,000 rounds up to 51 (5 % is a hair above 0.05 in the fixed point).");
        Check.That(GameFixRules.CashBounty(Fixed.FromInt(1000) * fivePercent, true) == 50, "With the fix it pays 50.");
        Check.That(GameFixRules.CashBounty(Fixed.FromInt(1001) * fivePercent, true) == 51 && GameFixRules.CashBounty(Fixed.FromInt(1001) * fivePercent, false) == 51,
            "A real fraction still rounds up in both.");
        Check.That(GameFixRules.ProportionalSupplyBoost(100, 3, 6) == 50 && GameFixRules.ProportionalSupplyBoost(100, 6, 6) == 100 && GameFixRules.ProportionalSupplyBoost(100, 0, 0) == 0,
            "The boost in proportion to the boxes delivered; nothing for a carrier without boxes.");
    }
}
