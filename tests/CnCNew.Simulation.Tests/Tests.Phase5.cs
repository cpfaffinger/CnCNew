using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// Small, synthetic, legally unproblematic phase-5 fixture: one weapon pair, one armor pair, four upgrades (three
    /// object-scoped, one player-scoped), one locomotor, and objects exercising the upgrade family, combat, movement and
    /// production. Mirrors the shapes found in the shipped data without reproducing any original content.
    /// </summary>
    private static IReadOnlyDictionary<string, string> Phase5Files() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/Phase5Fixture.ini"] = """
        Weapon TestGun
          PrimaryDamage = 20
          AttackRange = 50.0
          DamageType = EXPLOSION
          ClipSize = 2
          ClipReloadTime = 2000
          DelayBetweenShots = 500
        End
        Weapon BigGun
          PrimaryDamage = 40
          AttackRange = 60.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 300
        End

        Armor NoArmor
          Armor = Default 100%
        End
        Armor HalfArmor
          Armor = Default 50%
        End

        Upgrade Upgrade_Armor
          Type = OBJECT
          BuildCost = 100
          BuildTime = 1
        End
        Upgrade Upgrade_Weapon
          Type = OBJECT
          BuildCost = 50
          BuildTime = 1
        End
        Upgrade Upgrade_Health
          Type = OBJECT
          BuildCost = 30
          BuildTime = 1
        End
        Upgrade Upgrade_PlayerWide
          Type = PLAYER
          BuildCost = 200
          BuildTime = 1
        End

        Locomotor TestLocomotor
          Speed = 60.0
          Acceleration = 30.0
          Braking = 30.0
          TurnRate = 180.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End

        Object Fighter
          KindOf = SELECTABLE VEHICLE
          BuildCost = 200
          BuildTime = 2
          Locomotor = SET_NORMAL TestLocomotor
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY TestGun
          End
          WeaponSet
            Conditions = PLAYER_UPGRADE
            Weapon = PRIMARY BigGun
          End
          ArmorSet
            Conditions = None
            Armor = NoArmor
          End
          ArmorSet
            Conditions = PLAYER_UPGRADE
            Armor = HalfArmor
          End
          Behavior = ArmorUpgrade ArmorUp01
            TriggeredBy = Upgrade_Armor
          End
          Behavior = WeaponSetUpgrade WeaponUp01
            TriggeredBy = Upgrade_Weapon
          End
          Behavior = MaxHealthUpgrade HealthUp01
            TriggeredBy = Upgrade_Health
            AddMaxHealth = 50.0
            ChangeType = SAME_CURRENTHEALTH
          End
          Behavior = AIUpdateInterface AI01
          End
        End

        Object Factory
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = ProductionUpdate Prod01
            MaxQueueEntries = 2
          End
        End

        Object Ninja
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = StealthUpdate Stealth01
            StealthDelay = 1000
            MoveThresholdSpeed = 1.0
            StealthForbiddenConditions = MOVING
          End
        End

        Object Medic
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AutoHealBehavior Heal01
            StartsActive = Yes
            HealingAmount = 5
            HealingDelay = 100
            Radius = 100.0
            AffectsWholePlayer = Yes
          End
        End
        """,
    };

    public static void Phase5Economy()
    {
        var fixture = new SyntheticFixture(files: Phase5Files());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "Fighter");
        world.Step();

        feeder.PurchaseUpgrade(1, "Upgrade_PlayerWide");
        world.Step();
        Check.That(world.RejectionCounts[(int)CommandRejection.InsufficientFunds] == 1, "Buying an upgrade with no money is rejected.");

        feeder.GiveMoney(1, 1000);
        world.Step();
        Check.That(world.MoneyOf(new PlayerId(1)) == Fixed.FromInt(1000), "GiveMoneyCommand credits the balance.");

        feeder.PurchaseUpgrade(1, "Upgrade_PlayerWide");
        world.Step();
        Check.That(world.MoneyOf(new PlayerId(1)) == Fixed.FromInt(800) && world.PlayerUpgrades(new PlayerId(1)).Any(),
            "The player-scoped upgrade completes and its cost (200) is withdrawn.");

        feeder.PurchaseUpgrade(1, "Upgrade_PlayerWide");
        world.Step();
        Check.That(world.RejectionCounts[(int)CommandRejection.AlreadyOwned] == 1 && world.MoneyOf(new PlayerId(1)) == Fixed.FromInt(800),
            "Repurchasing a completed player upgrade is rejected and does not withdraw money again.");

        var fighter = world.FindEntity(new EntityId(1))!;
        feeder.PurchaseUpgrade(1, "Upgrade_Armor", target: 1);
        world.Step();
        Check.That(fighter.CompletedUpgrades.Any() && fighter.HasPlayerUpgradeArmor && world.MoneyOf(new PlayerId(1)) == Fixed.FromInt(700),
            "An object-scoped upgrade completes on its target and withdraws its own cost (100).");

        feeder.PurchaseUpgrade(1, "Upgrade_DoesNotExist");
        world.Step();
        Check.That(world.RejectionCounts[(int)CommandRejection.UnknownUpgrade] == 1, "An unknown upgrade name is rejected.");

        feeder.PurchaseUpgrade(2, "Upgrade_Weapon", target: 1);
        world.Step();
        Check.That(world.RejectionCounts[(int)CommandRejection.NotPermitted] == 1, "Purchasing an object upgrade for another player's object is rejected.");
    }

    public static void Phase5Combat()
    {
        var fixture = new SyntheticFixture(files: Phase5Files());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "Fighter", x: 0, y: 0);
        feeder.Spawn(2, "Fighter", x: 10, y: 0);
        world.Step();
        var attacker = world.FindEntity(new EntityId(1))!;
        var target = world.FindEntity(new EntityId(2))!;
        Check.That(attacker.WeaponClip == 2, "The clip starts full for the default weapon set (ClipSize=2).");

        feeder.FireWeapon(1, 1, 2);
        world.Step();
        Check.That(target.Body!.Health == Fixed.FromInt(80) && attacker.WeaponClip == 1,
            "20 damage at the default armor's 100% coefficient; the clip decrements by one shot.");

        feeder.FireWeapon(1, 1, 2);
        world.Step();
        Check.That(world.RejectionCounts[(int)CommandRejection.WeaponNotReady] == 1, "Firing again before DelayBetweenShots (500ms = 15 ticks) elapses is rejected.");

        for (var i = 0; i < 16; i++) world.Step();
        feeder.FireWeapon(1, 1, 2);
        world.Step();
        Check.That(attacker.WeaponClip == 0 && target.Body.Health == Fixed.FromInt(60), "The clip empties on the second shot once the delay has elapsed.");

        feeder.FireWeapon(1, 1, 2);
        world.Step();
        Check.That(world.RejectionCounts[(int)CommandRejection.WeaponNotReady] == 2, "Firing an empty clip is rejected while it reloads.");

        for (var i = 0; i < 62; i++) world.Step();
        feeder.FireWeapon(1, 1, 2);
        world.Step();
        Check.That(attacker.WeaponClip == 1 && target.Body.Health == Fixed.FromInt(40), "The clip refills after ClipReloadTime (2000ms = 60 ticks) and firing resumes.");

        feeder.Spawn(2, "Fighter", x: 1000, y: 0);
        world.Step();
        for (var i = 0; i < 16; i++) world.Step(); // let DelayBetweenShots (15 ticks) elapse so the rejection below is OutOfRange, not WeaponNotReady.
        feeder.FireWeapon(1, 1, 3);
        world.Step();
        Check.That(world.RejectionCounts[(int)CommandRejection.OutOfRange] == 1, "A target beyond AttackRange (50) is rejected.");

        feeder.Spawn(1, "Factory", x: 0, y: 0);
        world.Step();
        feeder.FireWeapon(1, 4, 2);
        world.Step();
        Check.That(world.RejectionCounts[(int)CommandRejection.NoWeapon] == 1, "An object with no weapon set cannot fire.");
    }

    public static void Phase5UpgradeEffects()
    {
        var fixture = new SyntheticFixture(files: Phase5Files());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "Fighter", x: 0, y: 0);
        feeder.Spawn(2, "Fighter", x: 10, y: 0);
        world.Step();
        var attacker = world.FindEntity(new EntityId(1))!;
        var target = world.FindEntity(new EntityId(2))!;

        // ArmorUpgrade: player 2 upgrades entity 2's armor, halving incoming damage. GiveMoney takes effect only when its
        // own tick executes, so the purchase must be submitted in a later tick to see the credited balance at Submit-time
        // validation (Validate() checks against current state, not same-tick effects still pending execution).
        feeder.GiveMoney(2, 1000);
        world.Step();
        feeder.PurchaseUpgrade(2, "Upgrade_Armor", target: 2);
        world.Step();
        Check.That(target.HasPlayerUpgradeArmor, "ArmorUpgrade switches the target to its PLAYER_UPGRADE armor set.");
        feeder.FireWeapon(1, 1, 2);
        world.Step();
        Check.That(target.Body!.Health == Fixed.FromInt(90), "20 damage at the upgraded armor's 50% coefficient deals 10.");

        // WeaponSetUpgrade: player 1 upgrades entity 1's weapon set (TestGun -> BigGun, unlimited clip).
        feeder.GiveMoney(1, 1000);
        world.Step();
        feeder.PurchaseUpgrade(1, "Upgrade_Weapon", target: 1);
        world.Step();
        Check.That(attacker.HasPlayerUpgradeWeapon && attacker.WeaponClip == int.MaxValue, "WeaponSetUpgrade switches weapons and refills the (unlimited) clip.");
        for (var i = 0; i < 10; i++) world.Step();
        feeder.FireWeapon(1, 1, 2);
        world.Step();
        Check.That(target.Body.Health == Fixed.FromInt(70), "The upgraded BigGun deals 40 damage at 50% armor (20), leaving 70.");

        // MaxHealthUpgrade (SAME_CURRENTHEALTH): the cap rises, current health is unchanged.
        feeder.PurchaseUpgrade(1, "Upgrade_Health", target: 1);
        world.Step();
        Check.That(attacker.Body!.MaxHealth == Fixed.FromInt(150) && attacker.Body.Health == Fixed.FromInt(100),
            "MaxHealthUpgrade raises the cap by AddMaxHealth (50) and leaves current health unchanged for SAME_CURRENTHEALTH.");
    }

    public static void Phase5Movement()
    {
        var fixture = new SyntheticFixture(files: Phase5Files());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "Fighter", x: 0, y: 0);
        world.Step();
        var fighter = world.FindEntity(new EntityId(1))!;
        Check.That(!fighter.HasMoveTarget, "No move order at spawn.");

        feeder.SetMoveTarget(1, 1, 100, 0);
        var arrived = false;
        for (var i = 0; i < 200 && !arrived; i++)
        {
            var result = world.Step();
            if (result.Events.Any(value => value.Kind == SimEventKind.MoveArrived)) arrived = true;
        }
        Check.That(arrived, "The unit reaches its move target within a generous tick budget.");
        Check.That(fighter.X == Fixed.FromInt(100) && fighter.Y == Fixed.Zero, "Position snaps exactly to the target on arrival.");
        Check.That(!fighter.HasMoveTarget && fighter.Speed == Fixed.Zero, "The move order clears and speed returns to zero.");

        feeder.Spawn(1, "Factory", x: 0, y: 0);
        world.Step();
        feeder.SetMoveTarget(1, 2, 50, 50);
        world.Step();
        Check.That(world.RejectionCounts[(int)CommandRejection.CannotMove] == 1, "An object without AIUpdateInterface cannot be given a move order.");
    }

    public static void Phase5Production()
    {
        var fixture = new SyntheticFixture(files: Phase5Files());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "Factory", x: 5, y: 5);
        world.Step();
        feeder.GiveMoney(1, 1000);
        world.Step();

        feeder.QueueProduction(1, 1, "Fighter");
        world.Step();
        Check.That(world.MoneyOf(new PlayerId(1)) == Fixed.FromInt(800), "BuildCost (200) is withdrawn in full immediately at queue time.");

        Entity? spawned = null;
        for (var i = 0; i < 70 && spawned is null; i++)
        {
            world.Step();
            spawned = world.Entities.FirstOrDefault(value => value.Template.Name == "Fighter");
        }
        Check.That(spawned is not null && spawned.Owner.Value == 1 && spawned.X == Fixed.FromInt(5) && spawned.Y == Fixed.FromInt(5),
            "The queued unit spawns at the producer's position, owned by the producer's owner, once BuildTime (2s = 60 ticks) elapses.");

        feeder.QueueProduction(1, 1, "Fighter");
        world.Step();
        var beforeCancel = world.MoneyOf(new PlayerId(1));
        feeder.CancelProduction(1, 1, 0);
        world.Step();
        Check.That(world.MoneyOf(new PlayerId(1)) == beforeCancel + Fixed.FromInt(200), "Cancelling a queued unit refunds its cost in full.");

        feeder.QueueProduction(1, 1, "Fighter");
        feeder.QueueProduction(1, 1, "Fighter");
        world.Step();
        feeder.QueueProduction(1, 1, "Fighter");
        world.Step();
        Check.That(world.RejectionCounts[(int)CommandRejection.QueueFull] == 1, "A full production queue (MaxQueueEntries = 2) rejects further orders.");
    }

    public static void Phase5StealthAndHeal()
    {
        var fixture = new SyntheticFixture(files: Phase5Files());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        var stealthedBit = ObjectStatusBits.Index("STEALTHED");
        feeder.Spawn(1, "Ninja", x: 0, y: 0);
        world.Step();
        var ninja = world.FindEntity(new EntityId(1))!;
        Check.That(!ninja.HasStatus(stealthedBit), "Not stealthed immediately at spawn: StealthDelay has not elapsed.");

        for (var i = 0; i < 35; i++) world.Step();
        Check.That(ninja.HasStatus(stealthedBit), "The object cloaks after StealthDelay (1000ms = 30 ticks) of no forbidden condition.");

        feeder.Spawn(1, "Medic", x: 0, y: 0);
        feeder.Damage(1, 30);
        world.Step();
        Check.That(ninja.Body!.Health == Fixed.FromInt(20), "The ninja takes 30 damage (50 -> 20).");
        for (var i = 0; i < 10; i++) world.Step();
        Check.That(ninja.Body.Health > Fixed.FromInt(20), "AutoHealBehavior (AffectsWholePlayer) heals the damaged ally over time.");
    }

    /// <summary>Block 0.3: <c>ActiveBody::attemptDamage</c>'s fear-sound roll (<c>GameLogicRandomValue(0,99) &lt; 25</c> when crossing 25% health) must consume the logic RNG exactly when the reference would, or golden-master comparisons desync.</summary>
    public static void Phase5FearSoundRng()
    {
        static uint[] LogicState(SimulationWorld world)
        {
            var state = new uint[6];
            world.Streams.Logic.CopyState(state);
            return state;
        }

        var fixture = new SyntheticFixture();
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "Barrel"); // ActiveBody MaxHealth=100, no OnSpawn RNG draw (no LifetimeUpdate).
        world.Step();
        var before = LogicState(world);

        feeder.Damage(1, 10); // 100 -> 90: stays above 25%.
        world.Step();
        Check.That(LogicState(world).SequenceEqual(before), "Damage that stays above the 25% threshold draws no logic RNG.");

        feeder.Damage(1, 80); // 90 -> 10: crosses 25% (threshold 25).
        world.Step();
        var afterCross = LogicState(world);
        Check.That(!afterCross.SequenceEqual(before), "Crossing the 25% health threshold consumes exactly one logic RNG draw (the fear-sound roll).");

        feeder.Damage(1, 5); // 10 -> 5: already below the threshold, must not roll again.
        world.Step();
        Check.That(LogicState(world).SequenceEqual(afterCross), "Damage while already below the threshold does not draw again.");

        var world2 = NewWorld(fixture);
        var feeder2 = new Feeder(world2);
        feeder2.Spawn(1, "Barrel");
        world2.Step();
        feeder2.Damage(1, 10);
        world2.Step();
        feeder2.Damage(1, 80);
        world2.Step();
        Check.That(LogicState(world2).SequenceEqual(afterCross), "The same seed and command sequence draws the identical RNG value (determinism).");
    }

    public static void Phase5Victory()
    {
        var fixture = new SyntheticFixture(files: Phase5Files());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        Check.That(world.IsPlayerDefeated(new PlayerId(1)) && world.IsPlayerDefeated(new PlayerId(2)) && world.TryGetWinner() is null,
            "Both players start with no objects at all: neither is the winner.");

        feeder.Spawn(1, "Fighter", x: 0, y: 0);
        feeder.Spawn(2, "Fighter", x: 10, y: 0);
        world.Step();
        Check.That(!world.IsPlayerDefeated(new PlayerId(1)) && !world.IsPlayerDefeated(new PlayerId(2)) && world.TryGetWinner() is null,
            "Two players with objects: no winner yet.");

        feeder.Kill(2);
        world.Step();
        Check.That(world.IsPlayerDefeated(new PlayerId(2)) && world.TryGetWinner() == new PlayerId(1), "The last player standing is the winner.");
    }
}
