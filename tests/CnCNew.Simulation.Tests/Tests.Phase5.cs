using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
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
            InnateStealth = Yes
          End
        End

        Object Visible
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = StealthUpdate Stealth01
            StealthDelay = 1000
            InnateStealth = No
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

        Object Flammable
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = FlammableUpdate Fire01
            FlameDamageLimit = 10
            AflameDuration = 300
            AflameDamageDelay = 100
            AflameDamageAmount = 5
            BurnedDelay = 200
            FlameDamageExpiration = 1000
          End
        End

        Weapon BurnGun
          PrimaryDamage = 35
          AttackRange = 50.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 0
        End
        Object Shooter
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = DestroyDie Destroy01
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY BurnGun
          End
        End
        Object Transitioning
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = TransitionDamageFX Fx01
            DamagedOCL1 = OCL_Damaged
            ReallyDamagedOCL1 = OCL_ReallyDamaged
            RubbleOCL1 = OCL_Rubble
          End
        End

        Object Zombie
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = SlowDeathBehavior ModuleTag_Death01
            DestructionDelay = 500
          End
          Behavior = SlowDeathBehavior ModuleTag_Death02
            DestructionDelay = 500
          End
          Behavior = SlowDeathBehavior ModuleTag_Death03
            DeathTypes = NONE +BURNED
            DestructionDelay = 0
          End
        End

        Weapon SuicideWeapon
          PrimaryDamage = 10
          AttackRange = 999.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 0
        End
        Weapon AltSuicideWeapon
          PrimaryDamage = 5
          AttackRange = 999.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 0
        End

        Upgrade Upgrade_Detonator
          Type = OBJECT
          BuildCost = 10
          BuildTime = 1
        End
        Upgrade Upgrade_AltDetonator
          Type = PLAYER
          BuildCost = 10
          BuildTime = 1
        End

        Object Suicider
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = FireWeaponWhenDeadBehavior FireDead01
            DeathWeapon = SuicideWeapon
            StartsActive = Yes
            ConflictsWith = Upgrade_AltDetonator
          End
          Behavior = FireWeaponWhenDeadBehavior FireDead02
            DeathWeapon = AltSuicideWeapon
            StartsActive = No
            TriggeredBy = Upgrade_Detonator
          End
        End

        Object Spotter
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = StealthDetectorUpdate Detector01
            DetectionRate = 500
            DetectionRange = 50.0
          End
        End

        Object Cloaked
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 30.0
            InitialHealth = 30.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = StealthUpdate Stealth01
            InnateStealth = Yes
          End
        End

        Object Poisonable
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = PoisonedBehavior Poison01
            PoisonDamageInterval = 100
            PoisonDuration = 500
          End
        End

        Object Bunker
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 200.0
            InitialHealth = 200.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = GarrisonContain Garrison01
            ContainMax = 2
            HealObjects = Yes
            TimeForFullHeal = 300
            DamagePercentToUnits = 50%
          End
        End

        Object Rebel
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object Tankish
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object Chinook
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = TransportContain Transport01
            Slots = 3
            HealthRegen%PerSec = 30
            AllowInsideKindOf = INFANTRY
          End
        End

        Object Trooper
          KindOf = SELECTABLE INFANTRY
          TransportSlotCount = 2
          Body = ActiveBody Body01
            MaxHealth = 60.0
            InitialHealth = 60.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Science SCIENCE_Intrinsic
          IsGrantable = Yes
        End
        Science SCIENCE_Basic
          SciencePurchasePointCost = 3
        End
        Science SCIENCE_Advanced
          PrerequisiteSciences = SCIENCE_Basic
          SciencePurchasePointCost = 5
        End

        PlayerTemplate TestFaction
          Side = Test
          StartMoney = 500
          IntrinsicSciences = SCIENCE_Intrinsic
        End
        """,
    };

    public static void Phase5Economy()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: Phase5Files());
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
        var fixture = new SyntheticFixture(TestRate.Default, files: Phase5Files());
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

        for (var i = 0; i < world.Frames(16); i++) world.Step();
        feeder.FireWeapon(1, 1, 2);
        world.Step();
        Check.That(attacker.WeaponClip == 0 && target.Body.Health == Fixed.FromInt(60), "The clip empties on the second shot once the delay has elapsed.");

        feeder.FireWeapon(1, 1, 2);
        world.Step();
        Check.That(world.RejectionCounts[(int)CommandRejection.WeaponNotReady] == 2, "Firing an empty clip is rejected while it reloads.");

        for (var i = 0; i < world.Frames(62); i++) world.Step();
        feeder.FireWeapon(1, 1, 2);
        world.Step();
        Check.That(attacker.WeaponClip == 1 && target.Body.Health == Fixed.FromInt(40), "The clip refills after ClipReloadTime (2000ms = 60 ticks) and firing resumes.");

        feeder.Spawn(2, "Fighter", x: 1000, y: 0);
        world.Step();
        for (var i = 0; i < world.Frames(16); i++) world.Step(); // let DelayBetweenShots (15 ticks) elapse so the rejection below is OutOfRange, not WeaponNotReady.
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
        var fixture = new SyntheticFixture(TestRate.Default, files: Phase5Files());
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
        for (var i = 0; i < world.Frames(10); i++) world.Step();
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
        var fixture = new SyntheticFixture(TestRate.Default, files: Phase5Files());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "Fighter", x: 0, y: 0);
        world.Step();
        var fighter = world.FindEntity(new EntityId(1))!;
        Check.That(!fighter.HasMoveTarget, "No move order at spawn.");

        feeder.SetMoveTarget(1, 1, 100, 0);
        var arrived = false;
        for (var i = 0; i < world.Frames(200) && !arrived; i++)
        {
            var result = world.Step();
            if (result.Events.Any(value => value.Kind == SimEventKind.MoveArrived)) arrived = true;
        }
        Check.That(arrived, "The unit reaches its move target within a generous tick budget.");
        // Above 30 Hz the move ends in the first tick of its last logic frame and the object covers the rest of that frame's step in the ticks after it.
        for (var i = 1; i < world.Frames(1); i++) world.Step();
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
        var fixture = new SyntheticFixture(TestRate.Default, files: Phase5Files());
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
        for (var i = 0; i < world.Frames(70) && spawned is null; i++)
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
        var fixture = new SyntheticFixture(TestRate.Default, files: Phase5Files());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        var stealthedBit = ObjectStatusBits.Index("STEALTHED");
        feeder.Spawn(1, "Ninja", x: 0, y: 0);
        world.Step();
        var ninja = world.FindEntity(new EntityId(1))!;
        Check.That(!ninja.HasStatus(stealthedBit), "Not stealthed immediately at spawn: StealthDelay has not elapsed.");

        for (var i = 0; i < world.Frames(35); i++) world.Step();
        Check.That(ninja.HasStatus(stealthedBit), "The object cloaks after StealthDelay (1000ms = 30 ticks) of no forbidden condition.");

        feeder.Spawn(1, "Visible", x: 50, y: 0);
        for (var i = 0; i < world.Frames(90); i++) world.Step();
        var visible = world.Entities.First(entity => entity.Template.Name == "Visible");
        Check.That(!visible.HasStatus(stealthedBit), "A StealthUpdate without InnateStealth or a granted CAN_STEALTH never cloaks (the default module every object carries).");

        feeder.Spawn(1, "Medic", x: 0, y: 0);
        feeder.Damage(1, 30);
        world.Step();
        Check.That(ninja.Body!.Health == Fixed.FromInt(20), "The ninja takes 30 damage (50 -> 20).");
        for (var i = 0; i < world.Frames(10); i++) world.Step();
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

        var fixture = new SyntheticFixture(TestRate.Default);
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

    /// <summary>Player/faction data model and science purchase (block 0.1 continuation): <c>PlayerTemplate</c> credits start money and intrinsic sciences once at construction; <c>PurchaseScienceCommand</c> ports <c>Player::attemptToPurchaseScience</c>'s gate order.</summary>
    public static void Phase5Sciences()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: Phase5Files());
        ImmutableArray<PlayerConfiguration> players = [new(new PlayerId(1), "Alpha", "TestFaction"), new(new PlayerId(2), "Beta")];
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, players, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        var feeder = new Feeder(world);

        Check.That(world.MoneyOf(new PlayerId(1)) == Fixed.FromInt(500), "PlayerTemplate.StartMoney is credited once at construction.");
        Check.That(world.PlayerSciences(new PlayerId(1)).Any() && world.Sciences.TryGet("SCIENCE_Intrinsic", out var intrinsic) && world.PlayerSciences(new PlayerId(1)).Has(intrinsic.Index),
            "PlayerTemplate.IntrinsicSciences is granted once at construction.");
        Check.That(world.MoneyOf(new PlayerId(2)) == Fixed.Zero && !world.PlayerSciences(new PlayerId(2)).Any(), "A player without a faction starts with nothing.");

        // SCIENCE_Intrinsic has no purchase cost (0): not directly purchasable, even though it is already owned.
        feeder.PurchaseScience(1, "SCIENCE_Intrinsic");
        world.Step();
        Check.That(world.RejectionCounts[(int)CommandRejection.AlreadyOwned] == 1, "Repurchasing an already-owned (here: intrinsic) science is rejected.");

        // SCIENCE_Advanced requires SCIENCE_Basic first.
        feeder.GiveSciencePoints(1, 10);
        world.Step();
        feeder.PurchaseScience(1, "SCIENCE_Advanced");
        world.Step();
        Check.That(world.RejectionCounts[(int)CommandRejection.MissingPrerequisite] == 1, "A science whose prerequisite is not yet owned is rejected.");

        feeder.PurchaseScience(1, "SCIENCE_Basic");
        world.Step();
        Check.That(world.SciencePointsOf(new PlayerId(1)) == 7 && world.Sciences.TryGet("SCIENCE_Basic", out var basic) && world.PlayerSciences(new PlayerId(1)).Has(basic.Index),
            "Purchasing SCIENCE_Basic (cost 3) withdraws the points and grants it.");

        feeder.PurchaseScience(1, "SCIENCE_Advanced");
        world.Step();
        Check.That(world.SciencePointsOf(new PlayerId(1)) == 2 && world.Sciences.TryGet("SCIENCE_Advanced", out var advanced) && world.PlayerSciences(new PlayerId(1)).Has(advanced.Index),
            "With its prerequisite owned, SCIENCE_Advanced (cost 5) now purchases successfully.");

        // Now out of points for a second SCIENCE_Advanced-costed purchase (there is only one, so reuse the insufficient-funds path directly).
        feeder.GiveSciencePoints(2, 1);
        world.Step();
        feeder.PurchaseScience(2, "SCIENCE_Basic");
        world.Step();
        Check.That(world.RejectionCounts[(int)CommandRejection.InsufficientSciencePurchasePoints] == 1, "Too few science purchase points is rejected.");

        feeder.PurchaseScience(1, "SCIENCE_DoesNotExist");
        world.Step();
        Check.That(world.RejectionCounts[(int)CommandRejection.UnknownScience] == 1, "An unknown science name is rejected.");
    }

    /// <summary>The next blocker module by frequency (<c>docs/handoffs/HANDOFF-PHASE5.md</c>): <c>FlammableUpdate</c> ignites once accumulated flame damage exceeds its limit, burns periodically, turns permanently <c>BURNED</c> if it stays aflame long enough, and its <c>onDamage</c> hook runs through the new <see cref="IDamageModule"/> callback in <c>ActiveBody.AttemptDamage</c>.</summary>
    public static void Phase5Flammable()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: Phase5Files());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        var aflameBit = ObjectStatusBits.Index("AFLAME");
        var burnedBit = ObjectStatusBits.Index("BURNED");
        feeder.Spawn(1, "Flammable");
        world.Step();
        var target = world.FindEntity(new EntityId(1))!;
        var body = target.Body!;

        feeder.Damage(1, 5, damageType: DamageTypes.Flame);
        world.Step();
        Check.That(!target.HasStatus(aflameBit) && body.Health == Fixed.FromInt(95), "5 flame damage (below the 10-point limit) does not ignite.");

        feeder.Damage(1, 6, damageType: DamageTypes.Flame);
        world.Step();
        Check.That(target.HasStatus(aflameBit) && body.Health == Fixed.FromInt(89), "Exceeding FlameDamageLimit (10) ignites the object; its own damage still applies.");
        var igniteTick = world.Tick - 1;

        // AflameDamageDelay = 100ms = 3 ticks: periodic self-damage of AflameDamageAmount (5) each time.
        while (world.Tick <= igniteTick + world.Frames(3)) world.Step();
        Check.That(body.Health == Fixed.FromInt(84), "The first periodic aflame-damage tick deals AflameDamageAmount (5).");

        // BurnedDelay = 200ms = 6 ticks: BURNED sets while still AFLAME (and a second damage tick has landed by then).
        while (world.Tick <= igniteTick + world.Frames(6)) world.Step();
        Check.That(target.HasStatus(aflameBit) && target.HasStatus(burnedBit) && body.Health == Fixed.FromInt(79),
            "BURNED sets after BurnedDelay while still AFLAME; a second periodic damage tick has also landed.");

        // AflameDuration = 300ms = 9 ticks: AFLAME clears; already BURNED, so it can never ignite again.
        while (world.Tick <= igniteTick + world.Frames(9)) world.Step();
        Check.That(!target.HasStatus(aflameBit) && target.HasStatus(burnedBit) && body.Health == Fixed.FromInt(74),
            "AFLAME clears after AflameDuration; a third periodic damage tick lands first. BURNED stays set.");

        var healthBeforeReignite = body.Health;
        feeder.Damage(1, 20, damageType: DamageTypes.Flame);
        world.Step();
        Check.That(!target.HasStatus(aflameBit) && body.Health == healthBeforeReignite - Fixed.FromInt(20),
            "Once BURNED, further flame damage applies normally but never reignites.");
    }

    /// <summary>
    /// Body damage states (<c>ActiveBody::calcDamageState</c>, retail default thresholds 0.7/0.35 — no <c>GameData</c>
    /// block in this fixture) and <c>TransitionDamageFX</c> (<c>onBodyDamageStateChange</c>: only fires when getting
    /// worse, only with a resolvable damage source).
    /// </summary>
    public static void Phase5DamageStateTransitions()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: Phase5Files());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "Shooter", x: 0, y: 0);
        feeder.Spawn(2, "Transitioning", x: 10, y: 0);
        world.Step();
        var target = world.FindEntity(new EntityId(2))!;
        var body = (ActiveBody)target.Body!;
        Check.That(body.DamageState == BodyDamageStates.Pristine, "A full-health object starts PRISTINE.");

        // No damage source (DamageCommand): the health still drops and the state still transitions...
        feeder.Damage(2, 35);
        var result = world.Step();
        Check.That(body.DamageState == BodyDamageStates.Damaged, "Health 65% (<= 0.7) is DAMAGED.");
        Check.That(!result.Events.Any(value => value.Kind == SimEventKind.CreationListRequested), "...but TransitionDamageFX requests nothing without a resolvable damage source.");

        // FireWeaponCommand: a real source entity, so the OCL now fires.
        feeder.FireWeapon(1, 1, 2);
        result = world.Step();
        Check.That(body.DamageState == BodyDamageStates.ReallyDamaged, "Health 30% (<= 0.35) is REALLYDAMAGED.");
        Check.That(result.Events.Any(value => value.Kind == SimEventKind.CreationListRequested && value.Text == "OCL_ReallyDamaged"), "The REALLYDAMAGED OCL fires with a real damage source.");

        for (var wait = 1; wait < world.Frames(1); wait++) world.Step(); // a weapon without a delay fires once per frame
        feeder.FireWeapon(1, 1, 2);
        result = world.Step();
        Check.That(body.Health == Fixed.Zero && body.DamageState == BodyDamageStates.Rubble, "Overkill clamps health to 0 (RUBBLE), not negative.");
        Check.That(result.Events.Any(value => value.Kind == SimEventKind.CreationListRequested && value.Text == "OCL_Rubble"), "The RUBBLE OCL fires.");
        Check.That(result.Events.Any(value => value.Kind == SimEventKind.EntityDied), "onBodyDamageStateChange runs before the death check, in the same tick as the killing blow.");
    }

    /// <summary>
    /// The third of the next blocker modules by frequency: <c>SlowDeathBehavior</c>. Multiple sibling instances on one
    /// object (up to 7 in real data, e.g. <c>AirforceGeneral.ini</c>'s <c>ColonelBurton</c>) enter a weighted lottery on a
    /// matching death; exactly one wins and alone schedules the delayed destruction, guarded by
    /// <see cref="Entity.SlowDeathStarted"/> so the other siblings' own <c>OnDie</c> calls (same death event) no-op.
    /// </summary>
    public static void Phase5SlowDeath()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: Phase5Files());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "Zombie");
        world.Step();
        var zombie = world.FindEntity(new EntityId(1))!;
        Check.That(!zombie.SlowDeathStarted, "SlowDeathStarted is false before death.");

        var deaths = zombie.Modules.OfType<SlowDeathBehavior>().ToArray();
        Check.That(deaths.Length == 3, "All three SlowDeathBehavior siblings are attached.");
        Check.That(deaths.All(value => value.ProbabilityWeight == 1), "ProbabilityModifier defaults to 0, and the weight is clamped to at least 1 (Math.Max).");

        feeder.Kill(1);
        world.Step();
        var deathTick = world.Tick - 1;
        Check.That(zombie.SlowDeathStarted, "The weighted lottery among applicable siblings runs exactly once on a matching death.");
        Check.That(world.FindEntity(new EntityId(1)) is not null, "Unlike DestroyDie, the object is not destroyed on the same tick as its death.");

        // DestructionDelay = 500ms = 15 ticks for either applicable sibling (Death03 is inapplicable: its DeathTypes
        // filter only matches BURNED, not the Normal death type KillCommand uses, so it never joins the lottery pool).
        while (world.Tick <= deathTick + world.Frames(14)) world.Step();
        Check.That(world.FindEntity(new EntityId(1)) is not null, "Still alive one tick before DestructionDelay elapses.");

        while (world.Tick <= deathTick + world.Frames(15)) world.Step();
        Check.That(world.FindEntity(new EntityId(1)) is null, "Destroyed exactly once DestructionDelay (15 ticks) has elapsed, regardless of which of the two applicable siblings won the lottery.");

        // Same seed, same command sequence: the lottery roll and delay draws reproduce identically (determinism).
        var world2 = NewWorld(fixture);
        var feeder2 = new Feeder(world2);
        feeder2.Spawn(1, "Zombie");
        world2.Step();
        feeder2.Kill(1);
        world2.Step();
        var deathTick2 = world2.Tick - 1;
        while (world2.Tick <= deathTick2 + world2.Frames(14)) world2.Step();
        Check.That(world2.FindEntity(new EntityId(1)) is not null, "Determinism: still alive one tick early on a fresh, identically seeded world.");
        while (world2.Tick <= deathTick2 + world2.Frames(15)) world2.Step();
        Check.That(world2.FindEntity(new EntityId(1)) is null, "Determinism: destroyed at the identical tick offset on a fresh, identically seeded world.");
    }

    /// <summary>
    /// Re-prioritized ahead of <c>CreateCrateDie</c>/<c>PoisonedBehavior</c> (see <c>Phase5BlockerModules</c>'s doc
    /// comment): <c>FireWeaponWhenDeadBehavior</c> fires its <c>DeathWeapon</c> on a matching death when active, either
    /// from construction (<c>StartsActive</c>) or a later <c>TriggeredBy</c> upgrade — and, uniquely, re-checks
    /// <c>ConflictsWith</c> again at death time, because the <c>StartsActive</c> path never passed through the
    /// activation-time conflict check at all (ChemicalGeneral.ini's mutually exclusive demo-trap detonators).
    /// </summary>
    public static void Phase5FireWeaponWhenDead()
    {
        // StartsActive fires; the TriggeredBy sibling does not, since its upgrade was never purchased.
        var fixture = new SyntheticFixture(TestRate.Default, files: Phase5Files());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "Suicider");
        world.Step();
        feeder.Kill(1);
        var result = world.Step();
        Check.That(result.Events.Any(value => value.Kind == SimEventKind.WeaponFireRequested && value.Text == "SuicideWeapon"),
            "StartsActive fires its DeathWeapon on a matching death.");
        Check.That(!result.Events.Any(value => value.Kind == SimEventKind.WeaponFireRequested && value.Text == "AltSuicideWeapon"),
            "The TriggeredBy sibling stays inactive: its upgrade was never purchased.");
        Check.That(world.FindEntity(new EntityId(1)) is null, "DestroyDie still removes the entity (independent of FireWeaponWhenDeadBehavior).");

        // TriggeredBy activates the second sibling; both fire independently (no mutual exclusion between them).
        var world2 = NewWorld(fixture);
        var feeder2 = new Feeder(world2);
        feeder2.Spawn(1, "Suicider");
        world2.Step();
        feeder2.GiveMoney(1, 1000);
        world2.Step();
        feeder2.PurchaseUpgrade(1, "Upgrade_Detonator", target: 1);
        world2.Step();
        feeder2.Kill(1);
        var result2 = world2.Step();
        Check.That(result2.Events.Any(value => value.Kind == SimEventKind.WeaponFireRequested && value.Text == "SuicideWeapon"), "StartsActive still fires.");
        Check.That(result2.Events.Any(value => value.Kind == SimEventKind.WeaponFireRequested && value.Text == "AltSuicideWeapon"), "The now-triggered sibling fires too.");

        // A conflicting upgrade purchased AFTER construction silences the StartsActive sibling at death time, even though
        // giveSelfUpgrade's construction-time activation never itself checked for a conflict.
        var world3 = NewWorld(fixture);
        var feeder3 = new Feeder(world3);
        feeder3.Spawn(1, "Suicider");
        world3.Step();
        feeder3.GiveMoney(1, 1000);
        world3.Step();
        feeder3.PurchaseUpgrade(1, "Upgrade_AltDetonator");
        world3.Step();
        feeder3.Kill(1);
        var result3 = world3.Step();
        Check.That(!result3.Events.Any(value => value.Kind == SimEventKind.WeaponFireRequested),
            "A conflicting upgrade purchased after construction silences the StartsActive sibling at death time; the TriggeredBy sibling was never triggered either.");
    }

    /// <summary>
    /// The next self-contained blocker by measured impact (9 objects unlocked alone): <c>StealthDetectorUpdate</c>
    /// periodically marks nearby enemy <c>STEALTHED</c> objects <c>DETECTED</c> (<see cref="StealthUpdate.MarkDetected"/>),
    /// skips the detector owner's allies (same owner, same team or an explicit alliance), and
    /// the mark expires once nothing re-marks it.
    /// </summary>
    public static void Phase5StealthDetector()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: Phase5Files());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        var detectedBit = ObjectStatusBits.Index("DETECTED");
        var stealthedBit = ObjectStatusBits.Index("STEALTHED");

        // Commands execute in (Player, Sequence) order regardless of submission order (ExecuteCommands), so the same-owner
        // "ally" (player 1, like the detector) always allocates entity id 2 and the enemy (player 2) always id 3.
        feeder.Spawn(1, "Spotter", x: 0, y: 0);
        feeder.Spawn(2, "Cloaked", x: 10, y: 0); // enemy, well within DetectionRange (50).
        feeder.Spawn(1, "Cloaked", x: 10, y: 0); // same owner as the detector: an "ally" under the no-diplomacy approximation.
        world.Step();
        var ally = world.FindEntity(new EntityId(2))!;
        var enemy = world.FindEntity(new EntityId(3))!;
        Check.That(enemy.HasStatus(ObjectStatusBits.Index("CAN_STEALTH")), "InnateStealth grants CAN_STEALTH at spawn.");
        // StealthDelay 0: one frame of the original until the first update, one more as the floor of the delay - game time, so counted in frames.
        for (var i = 0; i < world.Frames(3); i++) world.Step();
        Check.That(enemy.HasStatus(stealthedBit) && ally.HasStatus(stealthedBit), "InnateStealth cloaks both within a few frames (StealthDelay 0).");

        for (var i = 0; i < world.Frames(20); i++) world.Step(); // DetectionRate = 500ms = 15 ticks, plus the random initial phasing (1..15).
        Check.That(enemy.HasStatus(detectedBit), "The enemy stealthed unit is DETECTED once the detector's periodic scan reaches it.");
        Check.That(enemy.HasStatus(stealthedBit), "DETECTED coexists with STEALTHED: it reveals the unit but does not un-cloak it.");
        Check.That(!ally.HasStatus(detectedBit), "A stealthed unit owned by the detector itself is never marked DETECTED.");

        // Destroying the detector stops the periodic re-marking; the DETECTED mark then expires on its own (its duration
        // is UpdateRateTicks + 1, StealthDetectorUpdate.cpp's "updateRate PLUS 1... 'till we are called again").
        feeder.Destroy(1);
        var expiry = world.Tick + world.Frames(16);
        while (world.Tick <= expiry) world.Step();
        Check.That(!enemy.HasStatus(detectedBit), "DETECTED expires once nothing re-marks it before its duration elapses.");
    }

    /// <summary>Phase 10: a <see cref="TerrainMap.HasHeightLineOfSight"/> occlusion check on top of the range test in <see cref="Phase5StealthDetector"/>.</summary>
    public static void Phase5StealthDetectorHeightLineOfSight()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: Phase5Files());
        var detectedBit = ObjectStatusBits.Index("DETECTED");
        var stealthedBit = ObjectStatusBits.Index("STEALTHED");

        // A 25 m ridge (vertices 2..4, world x roughly 10..30) sits between the detector (x=0) and the target (x=40); both are
        // comfortably inside DetectionRange = 50.
        var ridged = TerrainMap.Build(SyntheticMaps.Build(20, 4, 1, (x, _) => x is >= 2 and <= 4 ? 40 : 0), "detector-ridge");
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, ridged.Fingerprint), fixture.Templates, ridged);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "Spotter", x: 0, y: 15);
        feeder.Spawn(2, "Cloaked", x: 40, y: 15);
        world.Step();
        var enemy = world.FindEntity(new EntityId(2))!;
        for (var i = 0; i < world.Frames(6); i++) world.Step();
        Check.That(enemy.HasStatus(stealthedBit), "The target is cloaked.");
        for (var i = 0; i < world.Frames(20); i++) world.Step();
        Check.That(!enemy.HasStatus(detectedBit), "A ridge between the detector and the target blocks detection even within DetectionRange.");

        // The identical layout without the ridge detects normally: the range test alone is not what is being exercised here.
        var flat = TerrainMap.Build(SyntheticMaps.Build(20, 4, 1), "detector-flat");
        var openWorld = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, flat.Fingerprint), fixture.Templates, flat);
        var openFeeder = new Feeder(openWorld);
        openFeeder.Spawn(1, "Spotter", x: 0, y: 15);
        openFeeder.Spawn(2, "Cloaked", x: 40, y: 15);
        openWorld.Step();
        var openEnemy = openWorld.FindEntity(new EntityId(2))!;
        for (var i = 0; i < openWorld.Frames(26); i++) openWorld.Step();
        Check.That(openEnemy.HasStatus(detectedBit), "The same layout without the ridge detects normally.");
    }

    /// <summary>
    /// The last of the originally named "next blocker modules by frequency" (docs/handoffs/HANDOFF-PHASE5.md), even though a
    /// fresh audit-coverage run showed it unlocks zero objects alone (every occurrence is also blocked by a still-missing
    /// module — see Phase5BlockerModules's doc comment): <c>PoisonedBehavior</c> reapplies a poison hit's actual damage
    /// periodically for its duration, re-poisoning extends the duration and only ever moves the next tick earlier, and
    /// healing cures it outright.
    /// </summary>
    public static void Phase5Poisoned()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: Phase5Files());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "Poisonable");
        world.Step();
        var target = world.FindEntity(new EntityId(1))!;
        var body = target.Body!;

        feeder.Damage(1, 10, damageType: DamageTypes.Poison);
        world.Step();
        Check.That(body.Health == Fixed.FromInt(290), "The initial poison hit applies its own damage immediately.");
        var doseTick = world.Tick - 1;

        // PoisonDamageInterval = 100ms = 3 ticks: the actual damage dealt by the original hit (10) reapplies every interval.
        while (world.Tick <= doseTick + world.Frames(3)) world.Step();
        Check.That(body.Health == Fixed.FromInt(280), "The first periodic poison tick reapplies the original actual damage dealt (10).");

        while (world.Tick <= doseTick + world.Frames(12)) world.Step();
        Check.That(body.Health == Fixed.FromInt(250), "Four periodic ticks have landed by now (at +3, +6, +9, +12).");

        // PoisonDuration = 500ms = 15 ticks: the fifth and final periodic tick lands exactly at the duration boundary,
        // then the poison wears off (the reference: "unless the poison killed us... turn off our special effects").
        while (world.Tick <= doseTick + world.Frames(15)) world.Step();
        Check.That(body.Health == Fixed.FromInt(240), "The fifth periodic tick lands exactly at the duration boundary, then the poison wears off.");

        var healthAfterWearOff = body.Health;
        while (world.Tick <= doseTick + world.Frames(18)) world.Step();
        Check.That(body.Health == healthAfterWearOff, "No further ticks land once the poison has worn off.");

        // A fresh dose while not currently poisoned starts a clean cycle; a second dose shortly after, while the first
        // cycle is still ticking, extends the duration but only ever moves the next damage tick EARLIER (the
        // reference's min(), never later) — and the periodic tick that follows reapplies the LATEST dose's amount.
        feeder.Damage(1, 5, damageType: DamageTypes.Poison);
        world.Step();
        var secondDoseTick = world.Tick - 1;
        Check.That(body.Health == healthAfterWearOff - Fixed.FromInt(5), "A fresh poison dose applies its own damage immediately.");

        feeder.Damage(1, 20, damageType: DamageTypes.Poison);
        world.Step();
        var healthAfterThirdDose = body.Health;
        Check.That(healthAfterThirdDose == healthAfterWearOff - Fixed.FromInt(5) - Fixed.FromInt(20), "Re-poisoning while still ticking applies its own damage too.");

        while (world.Tick <= secondDoseTick + world.Frames(3)) world.Step();
        Check.That(body.Health == healthAfterThirdDose - Fixed.FromInt(20),
            "The periodic tick lands at the FIRST dose's schedule (+3 from the second dose, not +3 from the third) but reapplies the LATEST dose's amount (20, not 5).");

        // Healing cures the poison outright (IDamageModule.OnHealing).
        var healthBeforeHeal = body.Health;
        feeder.Damage(1, 5, damageType: DamageTypes.Healing);
        world.Step();
        var healTick = world.Tick - 1;
        Check.That(body.Health == healthBeforeHeal + Fixed.FromInt(5), "Healing applies normally.");
        var healthAfterHeal = body.Health;
        while (world.Tick <= healTick + world.Frames(20)) world.Step();
        Check.That(body.Health == healthAfterHeal, "Healing cures the poison outright: no further periodic ticks land.");
    }

    /// <summary>
    /// <c>BaseRegenerateUpdate</c> (the reference-match unlock batch): structures stay idle at full health, wake
    /// <c>BaseRegenDelay</c> after every hit, then heal <c>BaseRegenHealthPercentPerSecond</c> of their maximum health in
    /// bursts of three ticks' worth every three ticks, and a fresh hit restarts the delay.
    /// </summary>
    public static void Phase5BaseRegenerate()
    {
        var files = new Dictionary<string, string>(Phase5Files())
        {
            ["Data/INI/GameData.ini"] = "GameData\n  BaseRegenHealthPercentPerSecond = 12.5%\n  BaseRegenDelay = 1000\nEnd\n",
            ["Data/INI/Object/BaseRegen.ini"] = """
            Object Regenerator
              KindOf = STRUCTURE
              Body = StructureBody Body01
                MaxHealth = 1000.0
                InitialHealth = 1000.0
              End
              Behavior = DestroyDie Destroy01
              End
              Behavior = BaseRegenerateUpdate Regen01
              End
            End
            """,
        };
        var world = NewWorld(new SyntheticFixture(TestRate.Default, files: files));
        var feeder = new Feeder(world);
        feeder.Spawn(1, "Regenerator");
        world.Step();
        var body = world.FindEntity(new EntityId(1))!.Body!;
        Check.That(world.Metrics.ScheduledUpdates == 0 || body.Health == Fixed.FromInt(1000), "A structure at full health does not regenerate.");

        feeder.Damage(1, 300);
        world.Step();
        var hitTick = world.Tick - 1;
        Check.That(body.Health == Fixed.FromInt(700), "The hit lands immediately.");

        // BaseRegenDelay = 1000 ms = 30 ticks: nothing heals before the delay has elapsed.
        while (world.Tick <= hitTick + world.Frames(29)) world.Step();
        Check.That(body.Health == Fixed.FromInt(700), "No healing before BaseRegenDelay has elapsed.");

        // 1000 * 12.5% * 3 / 30 = 12.5 per burst (12.5% is exact in fixed point), one burst every three ticks.
        while (world.Tick <= hitTick + world.Frames(30)) world.Step();
        Check.That(body.Health == Fixed.FromDecimal(712.5m), "The first burst heals three ticks' worth once the delay elapsed.");
        while (world.Tick <= hitTick + world.Frames(33)) world.Step();
        Check.That(body.Health == Fixed.FromInt(725), "One burst every three ticks.");

        // A fresh hit restarts the delay.
        feeder.Damage(1, 50);
        world.Step();
        var secondHit = world.Tick - 1;
        var afterSecondHit = body.Health;
        Check.That(afterSecondHit == Fixed.FromInt(675), "The second hit lands immediately.");
        while (world.Tick <= secondHit + world.Frames(29)) world.Step();
        Check.That(body.Health == afterSecondHit, "The second hit restarted the delay.");

        // Heals up to the maximum, then sleeps for good until damaged again.
        while (world.Tick <= secondHit + world.Frames(30 + 3 * 40)) world.Step();
        Check.That(body.Health == Fixed.FromInt(1000), "Regeneration stops at the maximum health.");
        Check.That(world.Metrics.ScheduledUpdates == 0, "A fully healed structure has no scheduled update left.");
    }

    /// <summary>
    /// The containment subsystem's first module (167 objects unlocked alone, the largest single blocker by measured
    /// impact — see <c>docs/handoffs/HANDOFF-PHASE5.md</c>): <c>GarrisonContain</c> capacity, <c>KindOf</c> gating, allied/enemy
    /// entry (the schema defaults admit allies and enemies alike; the relationship gating itself is covered by the
    /// teams tests), periodic healing, death ejection with <c>DamagePercentToUnits</c>, and immediate ejection on
    /// crossing into <c>REALLYDAMAGED</c>.
    /// </summary>
    public static void Phase5Garrison()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: Phase5Files());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "Bunker", x: 0, y: 0);
        feeder.Spawn(1, "Rebel", x: 5, y: 5);
        feeder.Spawn(2, "Rebel", x: 5, y: 5);
        feeder.Spawn(2, "Tankish", x: 5, y: 5);
        world.Step();
        var bunker = world.FindEntity(new EntityId(1))!;
        var allyRebel = world.FindEntity(new EntityId(2))!;
        var enemyRebel = world.FindEntity(new EntityId(3))!;
        var tank = world.FindEntity(new EntityId(4))!;

        feeder.Damage(3, 20); // enemyRebel: 50 -> 30, so the later healing check is observable.
        world.Step();

        feeder.EnterContainer(2, 4, 1);
        world.Step();
        Check.That(world.RejectionCounts[(int)CommandRejection.CannotEnterContainer] == 1, "A vehicle cannot enter a Garrison: AllowInsideKindOf defaults to INFANTRY only.");

        feeder.EnterContainer(1, 2, 1);
        feeder.EnterContainer(2, 3, 1);
        world.Step();
        Check.That(bunker.Container!.ContainCount == 2 && allyRebel.ContainedBy == bunker.Id && enemyRebel.ContainedBy == bunker.Id,
            "Both an own and an enemy infantry unit enter: AllowAlliesInside and AllowEnemiesInside default to true.");
        Check.That(allyRebel.X == bunker.X && allyRebel.Y == bunker.Y, "A contained entity snaps to the container's position.");

        feeder.Spawn(1, "Rebel", x: 5, y: 5);
        world.Step();
        var thirdRebel = world.FindEntity(new EntityId(5))!;
        feeder.EnterContainer(1, 5, 1);
        world.Step();
        Check.That(world.RejectionCounts[(int)CommandRejection.CannotEnterContainer] == 2, "A full Garrison (ContainMax = 2) rejects further entry.");

        // HealObjects: TimeForFullHeal = 300ms = 9 ticks; MaxHealth 50 heals in slivers of 50/9 per tick.
        for (var i = 0; i < world.Frames(9); i++) world.Step();
        Check.That(enemyRebel.Body!.Health == Fixed.FromInt(50), "HealObjects heals a contained occupant back to full within TimeForFullHeal.");

        feeder.ExitContainer(1, 2);
        world.Step();
        Check.That(!allyRebel.ContainedBy.IsValid && bunker.Container.ContainCount == 1, "Exiting clears ContainedBy and frees a slot.");
        feeder.EnterContainer(1, 5, 1);
        world.Step();
        Check.That(bunker.Container.ContainCount == 2, "The freed slot admits the previously rejected unit.");

        // Death: DamagePercentToUnits applies to each occupant's current health, then survivors are ejected (not killed) -
        // OpenContain::onDie calls removeAllContained, never killAllContained, for a container like GarrisonContain
        // whose killRidersWhoAreNotFreeToExit is a no-op.
        feeder.Kill(1);
        world.Step();
        Check.That(!enemyRebel.ContainedBy.IsValid && !thirdRebel.ContainedBy.IsValid, "The container's death ejects every occupant.");
        Check.That(enemyRebel.Body!.Health == Fixed.FromInt(25), "DamagePercentToUnits (50%) applies to each occupant's current health before ejection.");
        Check.That(!enemyRebel.EffectivelyDead, "Ejected occupants survive unless the damage percentage alone kills them.");

        // A separate scenario: crossing into REALLYDAMAGED ejects everyone immediately, independent of death.
        var world2 = NewWorld(fixture);
        var feeder2 = new Feeder(world2);
        feeder2.Spawn(1, "Bunker", x: 0, y: 0);
        feeder2.Spawn(2, "Rebel", x: 0, y: 0);
        world2.Step();
        var bunker2 = world2.FindEntity(new EntityId(1))!;
        var rebel2 = world2.FindEntity(new EntityId(2))!;
        feeder2.EnterContainer(2, 2, 1);
        world2.Step();
        Check.That(bunker2.Container!.ContainCount == 1, "Setup: the occupant entered.");
        // Retail default thresholds (no GameData block): REALLYDAMAGED at a ratio in (0, 0.35]. MaxHealth 200, so 170
        // damage leaves 30 (ratio 0.15).
        feeder2.Damage(1, 170);
        world2.Step();
        Check.That(((ActiveBody)bunker2.Body!).DamageState == BodyDamageStates.ReallyDamaged, "Setup: the bunker is now REALLYDAMAGED.");
        Check.That(!rebel2.ContainedBy.IsValid && bunker2.Container.ContainCount == 0, "Crossing into REALLYDAMAGED ejects every occupant immediately.");

        _ = tank;
    }

    /// <summary>
    /// The containment subsystem's second implementor (2 objects unlocked alone — small, but the reason
    /// <c>IContainerModule</c>/<c>ContainModuleBase</c> were worth extracting now): <c>TransportContain</c>'s
    /// slot-weighted capacity (<see cref="Templates.ObjectTemplate.TransportSlotCount"/>, not a simple headcount) and
    /// continuous <c>HealthRegen%PerSec</c> healing (unlike <c>GarrisonContain</c>'s ramp-to-full-over-a-duration).
    /// </summary>
    public static void Phase5Transport()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: Phase5Files());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "Chinook", x: 0, y: 0);
        feeder.Spawn(1, "Trooper", x: 5, y: 5);
        feeder.Spawn(1, "Rebel", x: 5, y: 5); // TransportSlotCount defaults to 0: never transportable.
        world.Step();
        var chinook = world.FindEntity(new EntityId(1))!;
        var trooper1 = world.FindEntity(new EntityId(2))!;

        feeder.Damage(2, 30); // Trooper1: 60 -> 30, so the later regen check is observable.
        world.Step();

        feeder.EnterContainer(1, 3, 1);
        world.Step();
        Check.That(world.RejectionCounts[(int)CommandRejection.CannotEnterContainer] == 1, "An object with TransportSlotCount 0 (Rebel, the default) can never board a transport.");

        feeder.EnterContainer(1, 2, 1);
        world.Step();
        Check.That(chinook.Container!.ContainCount == 1, "Trooper (TransportSlotCount 2) boards, using 2 of the Chinook's 3 slots.");

        feeder.Spawn(1, "Trooper", x: 5, y: 5);
        world.Step();
        feeder.EnterContainer(1, 4, 1);
        world.Step();
        Check.That(world.RejectionCounts[(int)CommandRejection.CannotEnterContainer] == 2,
            "A second Trooper (needs 2 more slots) is rejected: only 1 of 3 slots remain (weighted capacity, not a simple headcount).");

        // HealthRegen%PerSec = 30: MaxHealth 60 heals continuously at 60*30/100 = 18/sec = 0.6/tick, every tick (unlike
        // GarrisonContain's ramp-to-full-over-a-fixed-duration).
        for (var i = 0; i < world.Frames(10); i++) world.Step();
        Check.That(trooper1.Body!.Health > Fixed.FromInt(30) && trooper1.Body.Health < Fixed.FromInt(60), "HealthRegen%PerSec heals a contained occupant continuously, not all at once.");

        feeder.Kill(1);
        world.Step();
        Check.That(!trooper1.ContainedBy.IsValid, "The transport's death ejects its occupant.");
    }

    public static void Phase5Victory()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: Phase5Files());
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
