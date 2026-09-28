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

    /// <summary>Player/faction data model and science purchase (block 0.1 continuation): <c>PlayerTemplate</c> credits start money and intrinsic sciences once at construction; <c>PurchaseScienceCommand</c> ports <c>Player::attemptToPurchaseScience</c>'s gate order.</summary>
    public static void Phase5Sciences()
    {
        var fixture = new SyntheticFixture(files: Phase5Files());
        ImmutableArray<PlayerConfiguration> players = [new(new PlayerId(1), "Alpha", "TestFaction"), new(new PlayerId(2), "Beta")];
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), 1, players, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
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

    /// <summary>The next blocker module by frequency (<c>docs/HANDOFF-PHASE5.md</c>): <c>FlammableUpdate</c> ignites once accumulated flame damage exceeds its limit, burns periodically, turns permanently <c>BURNED</c> if it stays aflame long enough, and its <c>onDamage</c> hook runs through the new <see cref="IDamageModule"/> callback in <c>ActiveBody.AttemptDamage</c>.</summary>
    public static void Phase5Flammable()
    {
        var fixture = new SyntheticFixture(files: Phase5Files());
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
        while (world.Tick <= igniteTick + 3) world.Step();
        Check.That(body.Health == Fixed.FromInt(84), "The first periodic aflame-damage tick deals AflameDamageAmount (5).");

        // BurnedDelay = 200ms = 6 ticks: BURNED sets while still AFLAME (and a second damage tick has landed by then).
        while (world.Tick <= igniteTick + 6) world.Step();
        Check.That(target.HasStatus(aflameBit) && target.HasStatus(burnedBit) && body.Health == Fixed.FromInt(79),
            "BURNED sets after BurnedDelay while still AFLAME; a second periodic damage tick has also landed.");

        // AflameDuration = 300ms = 9 ticks: AFLAME clears; already BURNED, so it can never ignite again.
        while (world.Tick <= igniteTick + 9) world.Step();
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
        var fixture = new SyntheticFixture(files: Phase5Files());
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

        feeder.FireWeapon(1, 1, 2);
        result = world.Step();
        Check.That(body.Health == Fixed.Zero && body.DamageState == BodyDamageStates.Rubble, "Overkill clamps health to 0 (RUBBLE), not negative.");
        Check.That(result.Events.Any(value => value.Kind == SimEventKind.CreationListRequested && value.Text == "OCL_Rubble"), "The RUBBLE OCL fires.");
        Check.That(result.Events.Any(value => value.Kind == SimEventKind.EntityDied), "onBodyDamageStateChange runs before the death check, in the same tick as the killing blow.");
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
