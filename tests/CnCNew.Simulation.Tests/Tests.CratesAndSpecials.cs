using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// The crate, hijack, sabotage and special modules (<c>docs/SIMULATION-CORE.md</c>, "crates and collide modules"): money and veterancy crates, the pilot, the terrorist's car bomb,
/// the hijacker, the five saboteurs, the sticky bomb and booby trap, the GPS scrambler's stealth grant, the wanderer and the projectile stream bookkeeping.
/// </summary>
static partial class Tests
{
    private const string CratesLocomotor = """
        Locomotor CrateLoco
          Surfaces = GROUND
          TurnRate = 99999.0
          Speed = 60.0
          Acceleration = 30000.0
          Braking = 30000.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End
        """;

    private static IReadOnlyDictionary<string, string> CratesAndSpecialsFiles() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/CratesFixture.ini"] = $$"""
        {{CratesLocomotor}}
        Upgrade Upgrade_Supply
          Type = PLAYER
          BuildCost = 10
          BuildTime = 1
        End
        Weapon CarBombTestWeapon
          PrimaryDamage = 50
          AttackRange = 30.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 0
        End
        Weapon TrapWeapon
          PrimaryDamage = 100
          PrimaryDamageRadius = 10.0
          SecondaryDamage = 50
          SecondaryDamageRadius = 30.0
          AttackRange = 0.0
          DamageType = EXPLOSION
        End
        SpecialPower SPECIAL_CrateCc
          Enum = SPECIAL_DAISY_CUTTER
          ReloadTime = 1000
        End
        SpecialPower SPECIAL_CrateSuper
          Enum = SPECIAL_NAPALM_STRIKE
          ReloadTime = 1000
        End

        ; ------------------------------------------------------------------ crates and the objects that touch them
        Object MoneyCrate
          KindOf = CRATE
          Geometry = BOX
          GeometryMajorRadius = 4.0
          GeometryMinorRadius = 4.0
          GeometryHeight = 4.0
          Behavior = MoneyCrateCollide Money01
            ForbiddenKindOf = PROJECTILE
            MoneyProvided = 1000
            UpgradedBoost = UpgradeType:Upgrade_Supply Boost:250
          End
        End
        Object LevelCrate
          KindOf = CRATE
          Geometry = BOX
          GeometryMajorRadius = 4.0
          GeometryMinorRadius = 4.0
          GeometryHeight = 4.0
          Behavior = VeterancyCrateCollide Level01
            ForbiddenKindOf = PROJECTILE
            EffectRange = 100
          End
        End
        Object CrateRunner
          KindOf = SELECTABLE VEHICLE
          Geometry = CYLINDER
          GeometryMajorRadius = 5.0
          GeometryMinorRadius = 5.0
          Locomotor = SET_NORMAL CrateLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object CrateMissile
          KindOf = PROJECTILE
          Geometry = CYLINDER
          GeometryMajorRadius = 2.0
          GeometryMinorRadius = 2.0
          Locomotor = SET_NORMAL CrateLoco
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object TrainableTank
          KindOf = SELECTABLE VEHICLE
          IsTrainable = Yes
          ExperienceRequired = 0 100 250 600
          ExperienceValue = 10 30 90 300
          Geometry = CYLINDER
          GeometryMajorRadius = 6.0
          GeometryMinorRadius = 6.0
          Locomotor = SET_NORMAL CrateLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object TrainableDozer
          KindOf = SELECTABLE VEHICLE DOZER
          IsTrainable = Yes
          ExperienceRequired = 0 100 250 600
          ExperienceValue = 10 30 90 300
          Geometry = CYLINDER
          GeometryMajorRadius = 6.0
          GeometryMinorRadius = 6.0
          Locomotor = SET_NORMAL CrateLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object CratePilot
          KindOf = SELECTABLE INFANTRY
          Geometry = CYLINDER
          GeometryMajorRadius = 2.0
          GeometryMinorRadius = 2.0
          Locomotor = SET_NORMAL CrateLoco
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = VeterancyCrateCollide Pilot01
            RequiredKindOf = VEHICLE
            ForbiddenKindOf = DOZER
            EffectRange = 0
            AddsOwnerVeterancy = Yes
            IsPilot = Yes
          End
          Behavior = PilotFindVehicleUpdate Find01
            ScanRate = 100
            ScanRange = 200.0
            MinHealth = 0.5
          End
        End
        Object AreaPilot
          KindOf = SELECTABLE INFANTRY
          Geometry = CYLINDER
          GeometryMajorRadius = 2.0
          GeometryMinorRadius = 2.0
          Locomotor = SET_NORMAL CrateLoco
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = VeterancyCrateCollide Pilot01
            EffectRange = 60
            AddsOwnerVeterancy = No
            IsPilot = No
          End
        End

        ; ------------------------------------------------------------------ car bomb
        Object FixtureCar
          KindOf = SELECTABLE VEHICLE
          Geometry = CYLINDER
          GeometryMajorRadius = 5.0
          GeometryMinorRadius = 5.0
          Locomotor = SET_NORMAL CrateLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY None
          End
          WeaponSet
            Conditions = CARBOMB
            Weapon = PRIMARY CarBombTestWeapon
          End
        End
        Object PlainTruck
          KindOf = SELECTABLE VEHICLE
          Geometry = CYLINDER
          GeometryMajorRadius = 5.0
          GeometryMinorRadius = 5.0
          Locomotor = SET_NORMAL CrateLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object FixtureTerrorist
          KindOf = SELECTABLE INFANTRY
          Geometry = CYLINDER
          GeometryMajorRadius = 2.0
          GeometryMinorRadius = 2.0
          Locomotor = SET_NORMAL CrateLoco
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = ConvertToCarBombCrateCollide CarBomb01
            RequiredKindOf = VEHICLE
            FXList = FX_Test
          End
        End

        ; ------------------------------------------------------------------ hijacking
        Object FixtureChute
          KindOf = PARACHUTE
        End
        Object FixtureHijacker
          KindOf = SELECTABLE INFANTRY
          IsTrainable = Yes
          ExperienceRequired = 0 100 250 600
          ExperienceValue = 10 30 90 300
          Geometry = CYLINDER
          GeometryMajorRadius = 2.0
          GeometryMinorRadius = 2.0
          Locomotor = SET_NORMAL CrateLoco
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = HijackerUpdate Hijack01
            ParachuteName = FixtureChute
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = ConvertToHijackedVehicleCrateCollide Convert01
            RequiredKindOf = VEHICLE
          End
        End
        Object EjectingTruck
          KindOf = SELECTABLE VEHICLE
          IsTrainable = Yes
          ExperienceRequired = 0 100 250 600
          ExperienceValue = 10 30 90 300
          Geometry = CYLINDER
          GeometryMajorRadius = 5.0
          GeometryMinorRadius = 5.0
          Locomotor = SET_NORMAL CrateLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = EjectPilotDie Eject01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object ImmuneBus
          KindOf = SELECTABLE VEHICLE IMMUNE_TO_CAPTURE
          Geometry = CYLINDER
          GeometryMajorRadius = 5.0
          GeometryMinorRadius = 5.0
          Locomotor = SET_NORMAL CrateLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object LoadedTransport
          KindOf = SELECTABLE VEHICLE TRANSPORT
          Geometry = CYLINDER
          GeometryMajorRadius = 5.0
          GeometryMinorRadius = 5.0
          Locomotor = SET_NORMAL CrateLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = TransportContain Transport01
            Slots = 4
            AllowInsideKindOf = INFANTRY
          End
        End

        ; ------------------------------------------------------------------ sabotage
        Object FixtureSpy
          KindOf = SELECTABLE INFANTRY
          Geometry = CYLINDER
          GeometryMajorRadius = 2.0
          GeometryMinorRadius = 2.0
          Locomotor = SET_NORMAL CrateLoco
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = SabotageCommandCenterCrateCollide SabCc01
            BuildingPickup = Yes
          End
          Behavior = SabotageSuperweaponCrateCollide SabSuper01
            BuildingPickup = Yes
          End
          Behavior = SabotageMilitaryFactoryCrateCollide SabFactory01
            BuildingPickup = Yes
            SabotageDuration = 3000
          End
          Behavior = SabotagePowerPlantCrateCollide SabPower01
            BuildingPickup = Yes
            SabotagePowerDuration = 2000
          End
          Behavior = SabotageSupplyCenterCrateCollide SabSupply01
            BuildingPickup = Yes
            StealCashAmount = 300
          End
        End
        Object SabCommandCenter
          KindOf = STRUCTURE COMMANDCENTER
          Geometry = CYLINDER
          GeometryMajorRadius = 15.0
          GeometryMinorRadius = 15.0
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = SpecialAbility Power01
            SpecialPowerTemplate = SPECIAL_CrateCc
          End
        End
        Object SabSuperweapon
          KindOf = STRUCTURE FS_SUPERWEAPON
          Geometry = CYLINDER
          GeometryMajorRadius = 15.0
          GeometryMinorRadius = 15.0
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = SpecialAbility Power01
            SpecialPowerTemplate = SPECIAL_CrateSuper
          End
        End
        Object SabPowerPlant
          KindOf = STRUCTURE FS_POWER
          Geometry = CYLINDER
          GeometryMajorRadius = 15.0
          GeometryMinorRadius = 15.0
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object SabSupplyCenter
          KindOf = STRUCTURE FS_SUPPLY_CENTER
          Geometry = CYLINDER
          GeometryMajorRadius = 15.0
          GeometryMinorRadius = 15.0
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object SabWarFactory
          KindOf = STRUCTURE FS_WARFACTORY
          Geometry = CYLINDER
          GeometryMajorRadius = 15.0
          GeometryMinorRadius = 15.0
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = ProductionUpdate Prod01
            MaxQueueEntries = 3
          End
        End
        Object SabProduct
          KindOf = SELECTABLE VEHICLE
          BuildCost = 10
          BuildTime = 1
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        ; ------------------------------------------------------------------ sticky bombs and booby traps
        Object FixtureBoobyTrap
          KindOf = BOOBY_TRAP NO_COLLIDE MINE
          Body = HighlanderBody Body01
            MaxHealth = 1.0
            InitialHealth = 1.0
          End
          Behavior = StickyBombUpdate Sticky01
            GeometryBasedDamageWeapon = TrapWeapon
            GeometryBasedDamageFX = FX_Test
          End
        End
        Object FixtureTimedBomb
          KindOf = PROJECTILE MINE NO_COLLIDE
          Body = HighlanderBody Body01
            MaxHealth = 1.0
            InitialHealth = 1.0
          End
          Behavior = LifetimeUpdate Life01
            MinLifetime = 1000
            MaxLifetime = 1000
          End
          Behavior = StickyBombUpdate Sticky01
            OffsetZ = 7.0
          End
        End
        Object TrapVictim
          KindOf = SELECTABLE STRUCTURE
          Geometry = CYLINDER
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          Body = StructureBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object TrapBystander
          KindOf = SELECTABLE INFANTRY
          Geometry = CYLINDER
          GeometryMajorRadius = 1.0
          GeometryMinorRadius = 1.0
          Body = ActiveBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        ; ------------------------------------------------------------------ GPS scrambler, wanderer, stream
        Object FixtureScrambler
          KindOf = IMMOBILE
          Body = ImmortalBody Body01
            MaxHealth = 1.0
            InitialHealth = 1.0
          End
          Behavior = GrantStealthBehavior Grant01
            StartRadius = 10.0
            FinalRadius = 50.0
            RadiusGrowRate = 10.0
            KindOf = INFANTRY
          End
        End
        Object VisibleInfantry
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
        Object VisibleVehicle
          KindOf = SELECTABLE VEHICLE
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
        Object PlainInfantry
          KindOf = SELECTABLE INFANTRY
          TransportSlotCount = 1
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object FixtureWanderer
          KindOf = SELECTABLE INFANTRY
          Geometry = CYLINDER
          GeometryMajorRadius = 1.0
          GeometryMinorRadius = 1.0
          Locomotor = SET_NORMAL CrateLoco
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = WanderAIUpdate Wander01
          End
        End
        Object FixtureStream
          KindOf = INERT
          Behavior = ProjectileStreamUpdate Stream01
          End
        End
        Object StreamShot
          KindOf = PROJECTILE
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        """,
    };

    private static SimulationWorld CratesWorld(out Feeder feeder, bool computerAlpha = false, uint seed = 1)
    {
        var fixture = new SyntheticFixture(SimulationRate.Original, files: CratesAndSpecialsFiles());
        ImmutableArray<PlayerConfiguration> players = [new(new PlayerId(1), "Alpha", IsComputer: computerAlpha), new(new PlayerId(2), "Beta")];
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), SimulationRate.Original, seed, players, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        feeder = new Feeder(world);
        return world;
    }

    private static Entity CsSpawn(SimulationWorld world, Feeder feeder, string template, int owner, int x, int y = 0)
    {
        var before = world.Entities.Select(value => value.Id.Value).ToHashSet();
        feeder.Spawn(owner, template, owner: owner, x: x, y: y);
        world.Step();
        return world.Entities.Single(value => !before.Contains(value.Id.Value));
    }

    private static AIUpdateInterface CsAi(Entity entity) => entity.Modules.OfType<AIUpdateInterface>().First();

    private static bool CsExists(SimulationWorld world, Entity entity) => world.FindEntity(entity.Id) is not null;

    /// <summary>Steps until <paramref name="done"/> holds (at most <paramref name="limit"/> ticks) and returns every event of those ticks.</summary>
    private static List<SimEvent> CsRun(SimulationWorld world, int limit, Func<bool>? done = null)
    {
        var events = new List<SimEvent>();
        for (var tick = 0; tick < limit; tick++)
        {
            events.AddRange(world.Step().Events);
            if (done is not null && done()) break;
        }
        return events;
    }

    // ============================================================================================================ money crate

    public static void SpecialsMoneyCrate()
    {
        var world = CratesWorld(out var feeder);
        var alpha = new PlayerId(1);
        var beta = new PlayerId(2);
        Check.That(!ModuleBindingTable.Default.Registry.TryGet("MoneyCrateCollide", out var descriptor) || !descriptor.CompatibilityOnly, "The money crate module is implemented.");

        var runner = CsSpawn(world, feeder, "CrateRunner", 1, 0);
        var crate = CsSpawn(world, feeder, "MoneyCrate", 0, 30);
        Check.That(crate.Owner.IsSystem, "The crate belongs to the neutral player.");
        feeder.SetMoveTarget(1, runner.Id.Value, 100, 0);
        var events = CsRun(world, 60, () => !CsExists(world, crate));
        Check.That(!CsExists(world, crate), "The crate was picked up by the unit that drove over it.");
        Check.That(world.MoneyOf(alpha) == Fixed.FromInt(1000), $"MoneyProvided was paid ({world.MoneyOf(alpha)}).");
        var collected = events.Single(value => value.Kind == SimEventKind.CrateCollected);
        Check.That(collected.Other == runner.Id && collected.Text == "MoneyCrateCollide" && collected.Value == 1000, "The pick-up is reported.");

        // The completed Supply upgrade of the toucher's player adds its boost.
        world.CompleteUpgrade(beta, null, "Upgrade_Supply");
        var boosted = CsSpawn(world, feeder, "CrateRunner", 2, 0, 100);
        var second = CsSpawn(world, feeder, "MoneyCrate", 0, 30, 100);
        feeder.SetMoveTarget(2, boosted.Id.Value, 100, 100);
        CsRun(world, 60, () => !CsExists(world, second));
        Check.That(!CsExists(world, second) && world.MoneyOf(beta) == Fixed.FromInt(1250), $"UpgradedBoost adds 250 when the upgrade is owned ({world.MoneyOf(beta)}).");

        // Neutral objects and forbidden kinds never pick a crate up.
        var neutral = CsSpawn(world, feeder, "CrateRunner", 0, 0, 200);
        var missile = CsSpawn(world, feeder, "CrateMissile", 1, 0, 300);
        var guarded = CsSpawn(world, feeder, "MoneyCrate", 0, 30, 200);
        var guarded2 = CsSpawn(world, feeder, "MoneyCrate", 0, 30, 300);
        feeder.SetMoveTarget(0, neutral.Id.Value, 100, 200);
        feeder.SetMoveTarget(1, missile.Id.Value, 100, 300);
        CsRun(world, 60);
        Check.That(CsExists(world, guarded) && CsExists(world, guarded2), "A neutral unit and a PROJECTILE leave the crates alone.");
        Check.That(Math.Abs((double)neutral.X.ToDecimal() - 100) < 2 && Math.Abs((double)missile.X.ToDecimal() - 100) < 2, "Both drove across the crates.");
        Check.That(world.MoneyOf(alpha) == Fixed.FromInt(1000), "No money was paid for them.");
    }

    // ============================================================================================================ veterancy crate and pilot

    public static void SpecialsVeterancyCrateAndPilot()
    {
        var world = CratesWorld(out var feeder);
        var tank = CsSpawn(world, feeder, "TrainableTank", 1, 60);
        var pilot = CsSpawn(world, feeder, "CratePilot", 1, 0);
        world.SetVeterancy(pilot.Id, 1);
        var ai = CsAi(pilot);
        Check.That(ai.GoalObject == EntityId.Invalid, "A pilot has no goal object before it is ordered anywhere.");
        ai.OrderApproachGoal(world, tank, false);
        Check.That(ai.GoalObject == tank.Id, "The approach order names the goal object.");
        var events = CsRun(world, 80, () => !CsExists(world, pilot));
        Check.That(!CsExists(world, pilot), "The pilot climbed into the vehicle and is gone.");
        Check.That(tank.VeterancyLevel == 1 && tank.Experience == 100, $"AddsOwnerVeterancy gave the vehicle the pilot's one level (level {tank.VeterancyLevel}, experience {tank.Experience}).");
        Check.That(events.Any(value => value.Kind == SimEventKind.ObjectNameTransferred && value.Entity == pilot.Id && value.Other == tank.Id), "The pilot hands its script name to the vehicle.");

        // A pilot walking past a vehicle it was not sent to does nothing; a regular pilot (level 0) could never gain a level.
        var tank2 = CsSpawn(world, feeder, "TrainableTank", 1, 60, 100);
        var passing = CsSpawn(world, feeder, "CratePilot", 1, 0, 100);
        world.SetVeterancy(passing.Id, 1);
        feeder.SetMoveTarget(1, passing.Id.Value, 120, 100);
        CsRun(world, 80);
        Check.That(CsExists(world, passing) && tank2.VeterancyLevel == 0, "Walking over the vehicle without it being the goal changes nothing.");
        var regular = CsSpawn(world, feeder, "CratePilot", 1, 0, 200);
        var tank3 = CsSpawn(world, feeder, "TrainableTank", 1, 60, 200);
        CsAi(regular).OrderApproachGoal(world, tank3, false);
        CsRun(world, 80);
        Check.That(CsExists(world, regular) && tank3.VeterancyLevel == 0, "A level-0 pilot has no veterancy to give, so the module is never valid.");

        // Another player's vehicle, a dozer and a full-level vehicle are refused too.
        var enemyTank = CsSpawn(world, feeder, "TrainableTank", 2, 60, 300);
        var dozer = CsSpawn(world, feeder, "TrainableDozer", 1, 60, 400);
        var pilot2 = CsSpawn(world, feeder, "CratePilot", 1, 0, 300);
        var pilot3 = CsSpawn(world, feeder, "CratePilot", 1, 0, 400);
        world.SetVeterancy(pilot2.Id, 1);
        world.SetVeterancy(pilot3.Id, 1);
        CsAi(pilot2).OrderApproachGoal(world, enemyTank, false);
        CsAi(pilot3).OrderApproachGoal(world, dozer, false);
        CsRun(world, 80);
        Check.That(CsExists(world, pilot2) && enemyTank.VeterancyLevel == 0, "A pilot only enters a vehicle of its own player.");
        Check.That(CsExists(world, pilot3) && dozer.VeterancyLevel == 0, "ForbiddenKindOf = DOZER keeps pilots out of dozers.");

        // EffectRange promotes every object of the toucher's player nearby, the toucher included.
        var leader = CsSpawn(world, feeder, "TrainableTank", 1, 300, 500);
        var near = CsSpawn(world, feeder, "TrainableTank", 1, 330, 500);
        var far = CsSpawn(world, feeder, "TrainableTank", 1, 400, 500);
        var enemyNear = CsSpawn(world, feeder, "TrainableTank", 2, 310, 500);
        var area = CsSpawn(world, feeder, "AreaPilot", 1, 270, 500);
        CsAi(area).OrderApproachGoal(world, leader, false);
        CsRun(world, 40, () => !CsExists(world, area));
        Check.That(!CsExists(world, area), "The area pilot was consumed.");
        Check.That(leader.VeterancyLevel == 1 && near.VeterancyLevel == 1, "The toucher and a friendly vehicle within EffectRange 60 were promoted.");
        Check.That(far.VeterancyLevel == 0 && enemyNear.VeterancyLevel == 0, "Out of range and enemy vehicles were not.");

        // The plain level-up crate has no AIUpdateInterface: the reference's executeCrateBehavior returns before doing anything, so it is never consumed.
        var crate = CsSpawn(world, feeder, "LevelCrate", 0, 500, 600);
        var rider = CsSpawn(world, feeder, "TrainableTank", 1, 470, 600);
        feeder.SetMoveTarget(1, rider.Id.Value, 540, 600);
        CsRun(world, 60);
        Check.That(CsExists(world, crate) && rider.VeterancyLevel == 0, "A level-up crate without AI is not consumed (executeCrateBehavior needs a goal object).");
    }

    public static void SpecialsPilotFindsVehicle()
    {
        var world = CratesWorld(out var feeder, computerAlpha: true);
        var weak = CsSpawn(world, feeder, "TrainableTank", 1, 150);
        var healthy = CsSpawn(world, feeder, "TrainableTank", 1, 190);
        var foreign = CsSpawn(world, feeder, "TrainableTank", 2, 20);
        world.Damage(weak.Id, new DamageInfo(EntityId.Invalid, Fixed.FromInt(70), DamageTypes.Explosion, DeathTypes.Normal, false));
        var pilot = CsSpawn(world, feeder, "CratePilot", 1, 0);
        world.SetVeterancy(pilot.Id, 2);
        CsRun(world, 200, () => !CsExists(world, pilot));
        Check.That(!CsExists(world, pilot), "The idle AI pilot found a vehicle and entered it.");
        Check.That(healthy.VeterancyLevel == 2 && weak.VeterancyLevel == 0, "It skipped the vehicle under MinHealth (30 % of 100) and entered the healthy one, taking two levels with it.");
        Check.That(foreign.VeterancyLevel == 0, "A nearer enemy vehicle is no candidate.");

        // A human player's pilot never scans.
        var humanWorld = CratesWorld(out var humanFeeder);
        var tank = CsSpawn(humanWorld, humanFeeder, "TrainableTank", 1, 40);
        var human = CsSpawn(humanWorld, humanFeeder, "CratePilot", 1, 0);
        humanWorld.SetVeterancy(human.Id, 1);
        CsRun(humanWorld, 100);
        Check.That(CsExists(humanWorld, human) && tank.VeterancyLevel == 0, "The pilot of a human player waits for orders.");
    }

    // ============================================================================================================ car bomb

    public static void SpecialsCarBomb()
    {
        var world = CratesWorld(out var feeder);
        var terrorist = CsSpawn(world, feeder, "FixtureTerrorist", 1, 0);
        var car = CsSpawn(world, feeder, "FixtureCar", 2, 60);
        var truck = CsSpawn(world, feeder, "PlainTruck", 2, 60, 80);
        var victim = CsSpawn(world, feeder, "PlainTruck", 2, 300, 0);
        Check.That(CarBombWeapon(car.Template) >= 0 && CarBombWeapon(truck.Template) < 0, "Only the car has a CARBOMB weapon set.");
        Check.That(car.Template.DefaultWeapon < 0, "Its normal weapon set is empty.");
        Check.That(!feeder.FireWeapon(2, car.Id.Value, victim.Id.Value).Accepted || world.Step().Events.Any(value => value.Kind == SimEventKind.CommandRejected), "An unconverted car cannot fire.");

        // Walking past without the car being the goal object converts nothing.
        feeder.SetMoveTarget(1, terrorist.Id.Value, 120, 0);
        CsRun(world, 80);
        Check.That(CsExists(world, terrorist) && car.Owner == new PlayerId(2) && !car.HasStatus(ObjectStatusBits.Index("IS_CARBOMB")), "The terrorist walked through the car without converting it.");

        // Sent to a vehicle without a car-bomb weapon set, nothing happens either.
        CsAi(terrorist).OrderApproachGoal(world, truck, false);
        CsRun(world, 80);
        Check.That(CsExists(world, terrorist) && truck.Owner == new PlayerId(2), "A vehicle without a CARBOMB weapon set cannot be converted.");

        CsAi(terrorist).OrderApproachGoal(world, car, false);
        var events = CsRun(world, 120, () => !CsExists(world, terrorist));
        Check.That(!CsExists(world, terrorist), "The terrorist is spent by the conversion.");
        Check.That(car.Owner == new PlayerId(1), "The car defected to the terrorist's player.");
        Check.That(car.HasStatus(ObjectStatusBits.Index("IS_CARBOMB")), "The car is marked IS_CARBOMB.");
        Check.That(events.Any(value => value.Kind == SimEventKind.ObjectConverted && value.Entity == car.Id && value.Text == "CARBOMB"), "The conversion is reported.");
        Check.That(events.Any(value => value.Kind == SimEventKind.FxRequested && value.Text == "FX_Test" && value.Entity == car.Id), "The conversion FX plays at the car.");
        Check.That(events.Any(value => value.Kind == SimEventKind.ObjectNameTransferred && value.Entity == terrorist.Id && value.Other == car.Id), "The script name moves to the car.");

        // It now fires the car-bomb weapon.
        CsAi(car).OrderMove(world, victim.X - Fixed.FromInt(10), victim.Y, true);
        CsRun(world, 120, () => !CsAi(car).Entity.HasMoveTarget);
        var before = victim.Body!.Health;
        feeder.FireWeapon(1, car.Id.Value, victim.Id.Value);
        world.Step();
        Check.That(victim.Body!.Health == before - Fixed.FromInt(50), $"The converted car fires the CARBOMB weapon ({before} -> {victim.Body!.Health}).");

        // A car that is already a bomb is not valid for a second terrorist.
        var second = CsSpawn(world, feeder, "FixtureTerrorist", 1, 300, 100);
        CsAi(second).OrderApproachGoal(world, car, false);
        CsRun(world, 80);
        Check.That(CsExists(world, second), "A car bomb cannot be converted again.");
    }

    // ============================================================================================================ hijacking

    public static void SpecialsHijack()
    {
        var world = CratesWorld(out var feeder);
        var hijacker = CsSpawn(world, feeder, "FixtureHijacker", 1, 0);
        world.SetVeterancy(hijacker.Id, 1);
        var truck = CsSpawn(world, feeder, "EjectingTruck", 2, 80);
        world.SetVeterancy(truck.Id, 2);
        var bus = CsSpawn(world, feeder, "ImmuneBus", 2, 80, 60);
        var friend = CsSpawn(world, feeder, "EjectingTruck", 1, 80, 120);
        var loaded = CsSpawn(world, feeder, "LoadedTransport", 2, 80, 180);
        var passenger = CsSpawn(world, feeder, "PlainInfantry", 2, 80, 180);
        Check.That(world.TryEnterContainer(passenger, loaded), "A passenger boards the transport.");

        // Refused targets: immune to capture, an own vehicle, a transport with passengers.
        foreach (var refused in new[] { bus, friend, loaded })
        {
            var probe = CsSpawn(world, feeder, "FixtureHijacker", 1, 0, (int)refused.Y.ToDecimal());
            CsAi(probe).OrderApproachGoal(world, refused, false);
            CsRun(world, 60);
            Check.That(CsExists(world, probe) && !refused.HasStatus(ObjectStatusBits.Index("HIJACKED")), $"The hijacker leaves {refused.Template.Name} ({refused.Owner}) alone.");
        }

        CsAi(hijacker).OrderApproachGoal(world, truck, false);
        var events = CsRun(world, 120, () => truck.Owner == new PlayerId(1));
        Check.That(truck.Owner == new PlayerId(1), "The truck changed hands.");
        Check.That(truck.HasStatus(ObjectStatusBits.Index("HIJACKED")), "It is marked HIJACKED.");
        Check.That(events.Any(value => value.Kind == SimEventKind.ObjectConverted && value.Entity == truck.Id && value.Text == "HIJACKED"), "The hijacking is reported.");
        Check.That(truck.VeterancyLevel == 2 && hijacker.VeterancyLevel == 2, "Both share the higher veterancy level.");
        Check.That(CsExists(world, hijacker), "A vehicle that can eject a pilot keeps its hijacker (hidden).");
        var hidden = (1UL << ObjectStatusBits.Index("NO_COLLISIONS")) | (1UL << ObjectStatusBits.Index("MASKED")) | (1UL << ObjectStatusBits.Index("UNSELECTABLE"));
        Check.That((hijacker.Status & hidden) == hidden, "The hijacker is hidden: NO_COLLISIONS, MASKED and UNSELECTABLE.");
        Check.That(CsAi(hijacker).GoalObject == EntityId.Invalid, "The hijacker's order is over.");

        // The hijacker rides along.
        var hijackerModule = hijacker.Modules.OfType<HijackerUpdate>().Single();
        Check.That(hijackerModule.InVehicle, "HijackerUpdate rides in the vehicle.");
        feeder.SetMoveTarget(1, truck.Id.Value, 300, 0);
        CsRun(world, 40);
        // The hijacker's update runs before the truck's in a tick (lower id), so it is at most one tick's travel (2 units) behind.
        Check.That((truck.X - hijacker.X).Abs() <= Fixed.FromInt(2) && hijacker.Y == truck.Y && truck.X > Fixed.FromInt(100), $"The hijacker follows the truck ({hijacker.X} vs {truck.X}).");

        // A snapshot taken mid-ride continues identically.
        var bytes = WorldSnapshot.Write(world);
        var fixture = new SyntheticFixture(SimulationRate.Original, files: CratesAndSpecialsFiles());
        var restored = WorldSnapshot.Restore(bytes, fixture.Templates, fixture.Fingerprint);
        for (var tick = 0; tick < 20; tick++) Check.That(world.Step().StateHash == restored.Step().StateHash, $"The ride continues identically after a snapshot restore (tick {tick}).");

        // When the truck is destroyed the hijacker reappears where it was.
        var lastX = truck.X;
        var lastY = truck.Y;
        world.Kill(truck.Id);
        CsRun(world, 5, () => !hijackerModule.InVehicle);
        Check.That(!CsExists(world, truck), "The truck is gone.");
        Check.That(!hijackerModule.InVehicle && (hijacker.Status & hidden) == 0, "The hijacker is visible and collidable again.");
        Check.That(Math.Abs((double)(hijacker.X - lastX).ToDecimal()) < 12 && hijacker.Y == lastY, $"It reappears where the truck was ({hijacker.X}, {hijacker.Y}).");

        // A vehicle that cannot eject a pilot just swallows the hijacker.
        var simple = CsSpawn(world, feeder, "PlainTruck", 2, 300, 300);
        var second = CsSpawn(world, feeder, "FixtureHijacker", 1, 220, 300);
        CsAi(second).OrderApproachGoal(world, simple, false);
        CsRun(world, 80, () => simple.Owner == new PlayerId(1));
        CsRun(world, 2);
        Check.That(simple.Owner == new PlayerId(1) && !CsExists(world, second), "Without an EjectPilotDie the hijacker is used up and the vehicle is stolen.");
    }

    // ============================================================================================================ sabotage

    public static void SpecialsSabotage()
    {
        var world = CratesWorld(out var feeder);
        var alpha = new PlayerId(1);
        var beta = new PlayerId(2);
        feeder.GiveMoney(2, 500);
        feeder.GiveMoney(1, 1000);
        world.Step();
        var cc = CsSpawn(world, feeder, "SabCommandCenter", 2, 200, 0);
        var super = CsSpawn(world, feeder, "SabSuperweapon", 2, 200, 100);
        var plant = CsSpawn(world, feeder, "SabPowerPlant", 2, 200, 200);
        var supply = CsSpawn(world, feeder, "SabSupplyCenter", 2, 200, 300);
        var factory = CsSpawn(world, feeder, "SabWarFactory", 2, 200, 400);
        var friendly = CsSpawn(world, feeder, "SabCommandCenter", 1, 200, 500);
        CsRun(world, 40);
        var ccPower = cc.Modules.OfType<ISpecialPowerModule>().Single();
        var superPower = super.Modules.OfType<ISpecialPowerModule>().Single();
        Check.That(ccPower.IsReady(world) && superPower.IsReady(world), "Both special powers have recharged.");

        Entity Send(int y, Entity building)
        {
            var spy = CsSpawn(world, feeder, "FixtureSpy", 1, 100, y);
            CsAi(spy).OrderApproachGoal(world, building, false);
            return spy;
        }

        var spy1 = Send(0, cc);
        CsRun(world, 100, () => !CsExists(world, spy1));
        Check.That(!CsExists(world, spy1) && !ccPower.IsReady(world) && superPower.IsReady(world), "A command-center saboteur restarts the recharge of the center's powers only.");
        var spy2 = Send(100, super);
        CsRun(world, 100, () => !CsExists(world, spy2));
        Check.That(!CsExists(world, spy2) && !superPower.IsReady(world), "A superweapon saboteur restarts the recharge of the superweapon.");

        var stolenBefore = world.MoneyOf(alpha);
        var spy3 = Send(300, supply);
        CsRun(world, 100, () => !CsExists(world, spy3));
        Check.That(world.MoneyOf(alpha) == stolenBefore + Fixed.FromInt(300) && world.MoneyOf(beta) == Fixed.FromInt(200), $"StealCashAmount (300) moved from the victim to the saboteur ({world.MoneyOf(alpha)}/{world.MoneyOf(beta)}).");
        var spy3b = Send(300, supply);
        CsRun(world, 100, () => !CsExists(world, spy3b));
        Check.That(world.MoneyOf(beta) == Fixed.Zero && world.MoneyOf(alpha) == stolenBefore + Fixed.FromInt(500), "The theft is clamped to what the victim has.");

        Check.That(world.PowerSabotagedUntil(beta) == 0, "No power sabotage yet.");
        var spy4 = Send(200, plant);
        long hit = -1;
        CsRun(world, 100, () => { if (!CsExists(world, spy4)) hit = world.Tick; return hit >= 0; });
        Check.That(world.PowerSabotagedUntil(beta) >= hit + 59 && world.PowerSabotagedUntil(beta) <= hit + 61, $"A power-plant saboteur sets the victim's power-sabotaged tick 2 s ahead ({world.PowerSabotagedUntil(beta)}, now {hit}).");

        // A military-factory saboteur stops the production queue for SabotageDuration (3 s = 90 ticks).
        var spy5 = Send(400, factory);
        CsRun(world, 100, () => !CsExists(world, spy5));
        Check.That(!CsExists(world, spy5), "The factory saboteur arrived.");
        var disabledAt = world.Tick;
        feeder.GiveMoney(2, 100);
        world.Step();
        feeder.QueueProduction(2, factory.Id.Value, "SabProduct");
        CsRun(world, 80);
        Check.That(!world.Entities.Any(value => value.Template.Name == "SabProduct"), $"Nothing leaves the sabotaged factory while it is disabled (tick {world.Tick - disabledAt} after the sabotage).");
        CsRun(world, 100, () => world.Entities.Any(value => value.Template.Name == "SabProduct"));
        Check.That(world.Entities.Any(value => value.Template.Name == "SabProduct") && world.Tick - disabledAt >= 110, $"The queue resumes once the duration is over ({world.Tick - disabledAt} ticks after the sabotage).");

        // A snapshot taken while the factory is disabled keeps it disabled.
        var spy6 = Send(400, factory);
        CsRun(world, 100, () => !CsExists(world, spy6));
        feeder.GiveMoney(2, 100);
        world.Step();
        feeder.QueueProduction(2, factory.Id.Value, "SabProduct");
        CsRun(world, 20);
        var bytes = WorldSnapshot.Write(world);
        var restored = WorldSnapshot.Restore(bytes, new SyntheticFixture(SimulationRate.Original, files: CratesAndSpecialsFiles()).Templates, new SyntheticFixture(SimulationRate.Original, files: CratesAndSpecialsFiles()).Fingerprint);
        for (var tick = 0; tick < 150; tick++) Check.That(world.Step().StateHash == restored.Step().StateHash, $"The disabled factory continues identically after a restore (tick {tick}).");
        // An ally's building is not a target.
        var spy7 = Send(500, friendly);
        CsRun(world, 100);
        Check.That(CsExists(world, spy7), "A saboteur does nothing to a friendly building.");
    }

    // ============================================================================================================ sticky bomb, booby trap

    public static void SpecialsStickyBombAndBoobyTrap()
    {
        var world = CratesWorld(out var feeder);
        var victim = CsSpawn(world, feeder, "TrapVictim", 2, 100, 100);
        var near = CsSpawn(world, feeder, "TrapBystander", 1, 115, 100);   // 15 from the centre
        var middle = CsSpawn(world, feeder, "TrapBystander", 1, 135, 100); // 35
        var far = CsSpawn(world, feeder, "TrapBystander", 1, 160, 100);    // 60
        var bomb = CsSpawn(world, feeder, "FixtureBoobyTrap", 2, 0);
        var sticky = bomb.Modules.OfType<StickyBombUpdate>().Single();
        sticky.InitStickyBombAtOffset(world, victim, Fixed.FromInt(4), Fixed.FromInt(-3));
        Check.That(sticky.Target == victim.Id && bomb.X == Fixed.FromInt(104) && bomb.Y == Fixed.FromInt(97), $"The bomb sits at the given offset of the victim ({bomb.X}, {bomb.Y}).");
        Check.That(victim.HasStatus(ObjectStatusBits.Index("BOOBY_TRAPPED")), "A BOOBY_TRAP kind marks the victim BOOBY_TRAPPED.");

        // Friends do not set off friends' traps; an enemy does.
        var friend = CsSpawn(world, feeder, "TrapBystander", 2, 300, 300);
        Check.That(!StickyBombUpdate.CheckAndDetonateBoobyTrap(world, victim, friend), "An ally of the trap's owner does not trigger it.");
        Check.That(victim.HasStatus(ObjectStatusBits.Index("BOOBY_TRAPPED")) && CsExists(world, bomb), "The trap is still armed.");
        var enemy = CsSpawn(world, feeder, "TrapBystander", 1, 300, 320);
        var events = new List<SimEvent>();
        Check.That(StickyBombUpdate.CheckAndDetonateBoobyTrap(world, victim, enemy), "An enemy sets the trap off.");
        events.AddRange(CsRun(world, 3));
        Check.That(!victim.HasStatus(ObjectStatusBits.Index("BOOBY_TRAPPED")), "The status is cleared again.");
        Check.That(!CsExists(world, bomb), "The bomb is spent.");
        // Both radii are widened by the victim's bounding circle (10): primary 20, secondary 40, measured from the bounding spheres.
        Check.That(near.Body!.Health == Fixed.FromInt(900), $"A bystander 15 away took the primary damage ({near.Body!.Health}).");
        Check.That(middle.Body!.Health == Fixed.FromInt(950), $"A bystander 35 away took the secondary damage ({middle.Body!.Health}).");
        Check.That(far.Body!.Health == Fixed.FromInt(1000), "A bystander 60 away took none.");
        Check.That(victim.Body!.Health == Fixed.FromInt(900), $"The victim itself took the primary damage ({victim.Body!.Health}).");
        Check.That(events.Any(value => value.Kind == SimEventKind.FxRequested && value.Text == "FX_Test" && value.HasPosition), "The geometry-based FX is requested at the victim.");

        // A trapped object that dies sets its trap off through the bomb's own update.
        var victim2 = CsSpawn(world, feeder, "TrapVictim", 2, 500, 100);
        var bystander2 = CsSpawn(world, feeder, "TrapBystander", 1, 515, 100);
        var bomb2 = CsSpawn(world, feeder, "FixtureBoobyTrap", 2, 0, 50);
        bomb2.Modules.OfType<StickyBombUpdate>().Single().InitStickyBomb(world, victim2, null);
        world.Kill(victim2.Id);
        CsRun(world, 5);
        Check.That(!CsExists(world, bomb2) && bystander2.Body!.Health < Fixed.FromInt(1000), "A booby-trapped object that dies explodes its trap.");

        // A timed bomb follows a mobile victim (plus OffsetZ), a dead victim removes it, and its own lifetime kills it.
        var runner = CsSpawn(world, feeder, "CrateRunner", 1, 700, 100);
        var timed = CsSpawn(world, feeder, "FixtureTimedBomb", 2, 0, 80);
        timed.Modules.OfType<StickyBombUpdate>().Single().InitStickyBomb(world, runner, null);
        Check.That(timed.X == runner.X && timed.Z == runner.Z + Fixed.FromInt(7), "A timed bomb rides on its mobile victim with OffsetZ.");
        feeder.SetMoveTarget(1, runner.Id.Value, 760, 100);
        CsRun(world, 20);
        Check.That(timed.X == runner.X && timed.Y == runner.Y && runner.X > Fixed.FromInt(710), "It follows the victim.");
        Check.That(!runner.HasStatus(ObjectStatusBits.Index("BOOBY_TRAPPED")), "A timed bomb is no booby trap.");
        world.Kill(runner.Id);
        CsRun(world, 3);
        Check.That(!CsExists(world, timed), "A dead victim removes a timed bomb.");

        // A snapshot with an attached bomb restores it.
        var runner2 = CsSpawn(world, feeder, "CrateRunner", 1, 900, 100);
        var timed2 = CsSpawn(world, feeder, "FixtureTimedBomb", 2, 0, 80);
        timed2.Modules.OfType<StickyBombUpdate>().Single().InitStickyBomb(world, runner2, null);
        feeder.SetMoveTarget(1, runner2.Id.Value, 980, 100);
        CsRun(world, 5);
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), new SyntheticFixture(SimulationRate.Original, files: CratesAndSpecialsFiles()).Templates, new SyntheticFixture(SimulationRate.Original, files: CratesAndSpecialsFiles()).Fingerprint);
        for (var tick = 0; tick < 60; tick++) Check.That(world.Step().StateHash == restored.Step().StateHash, $"An attached bomb continues identically after a restore (tick {tick}).");
        Check.That(!CsExists(world, timed2), "The timed bomb's lifetime (1 s) killed it.");
    }

    // ============================================================================================================ GPS scrambler stealth grant

    public static void SpecialsGrantStealth()
    {
        var world = CratesWorld(out var feeder);
        var stealth = ObjectStatusBits.Index("STEALTHED");
        var canStealth = ObjectStatusBits.Index("CAN_STEALTH");
        var close = CsSpawn(world, feeder, "VisibleInfantry", 1, 25);
        var outer = CsSpawn(world, feeder, "VisibleInfantry", 1, 45);
        var outside = CsSpawn(world, feeder, "VisibleInfantry", 1, 70);
        var enemy = CsSpawn(world, feeder, "VisibleInfantry", 2, 20);
        var vehicle = CsSpawn(world, feeder, "VisibleVehicle", 1, 30);
        var plain = CsSpawn(world, feeder, "PlainInfantry", 1, 30);
        var marker = CsSpawn(world, feeder, "FixtureScrambler", 1, 0);
        Check.That(!close.HasStatus(stealth), "Nothing is stealthed before the marker's first scan.");
        CsRun(world, 1);
        Check.That(!close.HasStatus(stealth), "The first scan (radius 20) does not reach 25.");
        CsRun(world, 1);
        Check.That(close.HasStatus(stealth) && close.HasStatus(canStealth), "The second scan (radius 30) grants stealth to the infantry at 25.");
        Check.That(!outer.HasStatus(stealth), "The one at 45 is not yet inside.");
        CsRun(world, 2);
        Check.That(outer.HasStatus(stealth), "The final scan (radius 50) reaches 45.");
        Check.That(!CsExists(world, marker), "The marker is destroyed after its final scan.");
        Check.That(!outside.HasStatus(stealth), "An object outside the final radius gets nothing.");
        Check.That(!enemy.HasStatus(stealth), "An enemy inside the radius gets nothing.");
        Check.That(!vehicle.HasStatus(stealth), "A vehicle is not of the accepted kind.");
        Check.That(!plain.HasStatus(stealth), "An object without a StealthUpdate cannot be granted anything.");

        // The grant is permanent: it survives a snapshot.
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), new SyntheticFixture(SimulationRate.Original, files: CratesAndSpecialsFiles()).Templates, new SyntheticFixture(SimulationRate.Original, files: CratesAndSpecialsFiles()).Fingerprint);
        for (var tick = 0; tick < 100; tick++) Check.That(world.Step().StateHash == restored.Step().StateHash, $"The grant continues identically after a restore (tick {tick}).");
        Check.That(close.HasStatus(stealth), "Stealth is still on.");

        // A stealth grant with a duration ends when it runs out, and at once when the player gives an order.
        var timedUnit = CsSpawn(world, feeder, "VisibleInfantry", 1, 600);
        var stealthModule = timedUnit.Modules.OfType<StealthUpdate>().Single();
        stealthModule.ReceiveGrant(world, true, 5);
        Check.That(timedUnit.HasStatus(stealth), "ReceiveGrant switches stealth on.");
        CsRun(world, 3);
        Check.That(timedUnit.HasStatus(stealth), "The grant lasts a few ticks.");
        CsRun(world, 4);
        Check.That(!timedUnit.HasStatus(stealth) && !timedUnit.HasStatus(canStealth), "It ended when the frames ran out.");
        stealthModule.ReceiveGrant(world, true, 100);
        stealthModule.ReceiveGrant(world, false, 0);
        Check.That(!timedUnit.HasStatus(stealth), "ReceiveGrant(false) revokes it.");
    }

    // ============================================================================================================ WanderAIUpdate

    public static void SpecialsWander()
    {
        var world = CratesWorld(out var feeder, seed: 11);
        var wanderer = CsSpawn(world, feeder, "FixtureWanderer", 1, 100, 100);
        var positions = new List<(Fixed X, Fixed Y)>();
        for (var tick = 0; tick < 200; tick++)
        {
            world.Step();
            positions.Add((wanderer.X, wanderer.Y));
        }
        Check.That(wanderer.X > Fixed.FromInt(130) && wanderer.Y > Fixed.FromInt(130), $"The idle wanderer keeps walking to points 5..50 further on ({wanderer.X}, {wanderer.Y}).");
        var monotone = true;
        for (var index = 1; index < positions.Count; index++) monotone &= positions[index].X >= positions[index - 1].X && positions[index].Y >= positions[index - 1].Y;
        Check.That(monotone, "Every leg only increases x and y.");
        Check.That(positions.Select(value => value.X).Distinct().Count() > 20, "It moved in many steps.");

        // Same seed, same walk; another seed, another walk; and a restore continues the walk.
        var twin = CratesWorld(out var twinFeeder, seed: 11);
        CsSpawn(twin, twinFeeder, "FixtureWanderer", 1, 100, 100);
        for (var tick = 0; tick < 200; tick++) twin.Step();
        Check.That(twin.StateHash == world.StateHash, "The walk is deterministic for a seed.");
        var other = CratesWorld(out var otherFeeder, seed: 12);
        var otherWanderer = CsSpawn(other, otherFeeder, "FixtureWanderer", 1, 100, 100);
        for (var tick = 0; tick < 200; tick++) other.Step();
        Check.That(otherWanderer.X != wanderer.X || otherWanderer.Y != wanderer.Y, "Another seed gives another walk.");
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), new SyntheticFixture(SimulationRate.Original, files: CratesAndSpecialsFiles()).Templates, new SyntheticFixture(SimulationRate.Original, files: CratesAndSpecialsFiles()).Fingerprint);
        for (var tick = 0; tick < 100; tick++) Check.That(world.Step().StateHash == restored.Step().StateHash, $"The walk continues identically after a restore (tick {tick}).");
    }

    // ============================================================================================================ ProjectileStreamUpdate

    public static void SpecialsProjectileStream()
    {
        var world = CratesWorld(out var feeder);
        var stream = CsSpawn(world, feeder, "FixtureStream", 1, 0);
        var module = stream.Modules.OfType<ProjectileStreamUpdate>().Single();
        var owner = CsSpawn(world, feeder, "CrateRunner", 1, 10, 0);
        var victimA = CsSpawn(world, feeder, "TrapBystander", 2, 100, 0);
        var victimB = CsSpawn(world, feeder, "TrapBystander", 2, 100, 50);
        var shots = new List<Entity>();
        for (var index = 0; index < 5; index++) shots.Add(CsSpawn(world, feeder, "StreamShot", 1, 20 + index * 10, 0));

        Check.That(module.Count == 0, "A new stream is empty.");
        module.AddProjectile(world, owner.Id, shots[0].Id, victimA.Id, false, default, default, default);
        Check.That(module.Count == 2 && module.Owner == owner.Id, "The first shot at a victim opens the stream with a hole in front of it.");
        module.AddProjectile(world, owner.Id, shots[1].Id, victimA.Id, false, default, default, default);
        module.AddProjectile(world, owner.Id, shots[2].Id, victimA.Id, false, default, default, default);
        Check.That(module.Count == 4, "Shots at the same victim extend the chain.");
        module.AddProjectile(world, owner.Id, shots[3].Id, victimB.Id, false, default, default, default);
        Check.That(module.Count == 6, "A new victim breaks the chain with a hole.");
        module.AddProjectile(world, owner.Id, shots[4].Id, EntityId.Invalid, true, Fixed.FromInt(7), Fixed.FromInt(7), Fixed.Zero);
        Check.That(module.Count == 8, "A position shot after an object shot also breaks the chain.");

        var points = module.Points(world).ToArray();
        Check.That(points.Length == 8 && points[0] == (Fixed.Zero, Fixed.Zero, Fixed.Zero) && points[4] == (Fixed.Zero, Fixed.Zero, Fixed.Zero), "Holes are reported as the origin.");
        Check.That(points[1].X == Fixed.FromInt(20) && points[3].X == Fixed.FromInt(40) && points[5].X == Fixed.FromInt(50) && points[7].X == Fixed.FromInt(60), "Projectiles are reported at their positions.");

        // Gone projectiles are only culled from the front.
        world.Kill(shots[1].Id);
        CsRun(world, 2);
        Check.That(module.Count == 7, $"The hole at the front was culled at the first update ({module.Count}).");
        Check.That(module.Points(world).ElementAt(1) == (Fixed.Zero, Fixed.Zero, Fixed.Zero), "A projectile that vanished in the middle only leaves a gap.");
        world.Kill(shots[0].Id);
        CsRun(world, 2);
        Check.That(module.Count == 5, $"Culling removes the hole and the gone projectiles from the front ({module.Count}).");
        Check.That(CsExists(world, stream), "The stream lives while its owner exists.");

        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), new SyntheticFixture(SimulationRate.Original, files: CratesAndSpecialsFiles()).Templates, new SyntheticFixture(SimulationRate.Original, files: CratesAndSpecialsFiles()).Fingerprint);
        var restoredModule = restored.Entities.Single(value => value.Template.Name == "FixtureStream").Modules.OfType<ProjectileStreamUpdate>().Single();
        Check.That(restoredModule.Count == module.Count && restoredModule.Owner == module.Owner, "The buffer survives a snapshot.");
        world.Kill(owner.Id);
        foreach (var shot in shots.Skip(2)) world.Kill(shot.Id);
        CsRun(world, 6);
        Check.That(!CsExists(world, stream), "With an empty buffer and no owner the stream removes itself.");
        for (var tick = 0; tick < 6; tick++) restored.Step();
        Check.That(restored.Entities.Any(value => value.Template.Name == "FixtureStream"), "(The restored copy's owner still lives, so its stream does too.)");
    }
}
