using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Content;
using CnCNew.Data.Definitions;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Data.World;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.Views;
using CnCNew.Simulation.World;

/// <summary>
/// The last nine modules (<c>docs/SIMULATION-CORE.md</c>): <c>SalvageCrateCollide</c>, <c>UnitCrateCollide</c>, <c>SabotageFakeBuildingCrateCollide</c>, <c>SabotageInternetCenterCrateCollide</c>,
/// <c>SmartBombTargetHomingUpdate</c>, <c>SupplyWarehouseCripplingBehavior</c> and the ferry (<c>RailedTransportAIUpdate</c>, <c>RailedTransportContain</c>, <c>RailedTransportDockUpdate</c>).
/// </summary>
static partial class Tests
{
    private static readonly string[] LastBlockerModules =
    [
        "SalvageCrateCollide", "UnitCrateCollide", "SabotageFakeBuildingCrateCollide", "SabotageInternetCenterCrateCollide", "SmartBombTargetHomingUpdate",
        "SupplyWarehouseCripplingBehavior", "RailedTransportAIUpdate", "RailedTransportContain", "RailedTransportDockUpdate",
    ];

    private const string LastBlockerIni = """
        Locomotor LbLoco
          Surfaces = GROUND
          TurnRate = 99999.0
          Speed = 60.0
          Acceleration = 30000.0
          Braking = 30000.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End
        Locomotor LbPlaneLoco
          Surfaces = AIR
          TurnRate = 90.0
          Speed = 120.0
          Acceleration = 600.0
          Braking = 600.0
          MinSpeed = 0.0
          PreferredHeight = 60.0
          CloseEnoughDist = 1.0
        End
        Armor LbArmor
          Armor = Default 100%
        End
        Armor LbArmorOne
          Armor = Default 50%
        End
        Armor LbArmorTwo
          Armor = Default 25%
        End
        Weapon LbGun
          PrimaryDamage = 10
          AttackRange = 80.0
          DamageType = EXPLOSION
          ClipSize = 1
          ClipReloadTime = 1000
        End
        Weapon LbGunOne
          PrimaryDamage = 20
          AttackRange = 80.0
          DamageType = EXPLOSION
          ClipSize = 2
          ClipReloadTime = 1000
        End
        Weapon LbGunTwo
          PrimaryDamage = 30
          AttackRange = 80.0
          DamageType = EXPLOSION
          ClipSize = 3
          ClipReloadTime = 1000
        End
        Upgrade Upgrade_LbSpy
          Type = PLAYER
          BuildCost = 10
          BuildTime = 1
        End

        ; ------------------------------------------------------------------ salvage crates and the units that pick them up
        Object LbSalvageCrate
          KindOf = CRATE
          Geometry = BOX
          GeometryMajorRadius = 4.0
          GeometryMinorRadius = 4.0
          GeometryHeight = 4.0
          Behavior = SalvageCrateCollide Salvage01
            ForbiddenKindOf = PROJECTILE
            WeaponChance = 100%
            LevelChance = 100%
            MoneyChance = 0%
            MinMoney = 40
            MaxMoney = 40
          End
        End
        Object LbSalvageRandomCrate
          KindOf = CRATE
          Geometry = BOX
          GeometryMajorRadius = 4.0
          GeometryMinorRadius = 4.0
          GeometryHeight = 4.0
          Behavior = SalvageCrateCollide Salvage01
            ForbiddenKindOf = PROJECTILE
            WeaponChance = 50%
            LevelChance = 50%
            MinMoney = 10
            MaxMoney = 90
          End
        End
        Object LbSalvageTank
          KindOf = SELECTABLE VEHICLE SALVAGER WEAPON_SALVAGER ARMOR_SALVAGER
          IsTrainable = Yes
          ExperienceRequired = 0 100 250 600
          ExperienceValue = 10 30 90 300
          Geometry = CYLINDER
          GeometryMajorRadius = 6.0
          GeometryMinorRadius = 6.0
          Locomotor = SET_NORMAL LbLoco
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
            Weapon = PRIMARY LbGun
          End
          WeaponSet
            Conditions = CRATEUPGRADE_ONE
            Weapon = PRIMARY LbGunOne
          End
          WeaponSet
            Conditions = CRATEUPGRADE_TWO
            Weapon = PRIMARY LbGunTwo
          End
          ArmorSet
            Conditions = None
            Armor = LbArmor
          End
          ArmorSet
            Conditions = CRATE_UPGRADE_ONE
            Armor = LbArmorOne
          End
          ArmorSet
            Conditions = CRATE_UPGRADE_TWO
            Armor = LbArmorTwo
          End
        End
        Object LbSalvageJeep
          KindOf = SELECTABLE VEHICLE SALVAGER
          IsTrainable = Yes
          ExperienceRequired = 0 100 250 600
          ExperienceValue = 10 30 90 300
          Geometry = CYLINDER
          GeometryMajorRadius = 6.0
          GeometryMinorRadius = 6.0
          Locomotor = SET_NORMAL LbLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object LbPlainTank
          KindOf = SELECTABLE VEHICLE
          Geometry = CYLINDER
          GeometryMajorRadius = 6.0
          GeometryMinorRadius = 6.0
          Locomotor = SET_NORMAL LbLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End

        ; ------------------------------------------------------------------ the free-units crate
        Object LbFreeCrate
          KindOf = CRATE
          Geometry = BOX
          GeometryMajorRadius = 4.0
          GeometryMinorRadius = 4.0
          GeometryHeight = 4.0
          Behavior = UnitCrateCollide Free01
            ForbiddenKindOf = PROJECTILE
            UnitCount = 2
            UnitName = LbPlainTank
          End
        End
        Object LbBadUnitCrate
          KindOf = CRATE
          Geometry = BOX
          GeometryMajorRadius = 4.0
          GeometryMinorRadius = 4.0
          GeometryHeight = 4.0
          Behavior = UnitCrateCollide Free01
            ForbiddenKindOf = PROJECTILE
            UnitCount = 1
            UnitName = LbBrokenUnit
          End
        End
        Object LbBrokenUnit
          KindOf = SELECTABLE VEHICLE
          Behavior = SpawnBehavior Stand01
            CanReclaimOrphans = Yes
          End
        End

        ; ------------------------------------------------------------------ the saboteurs, a decoy and an Internet Center
        Object LbSaboteur
          KindOf = SELECTABLE INFANTRY
          Geometry = CYLINDER
          GeometryMajorRadius = 2.0
          GeometryMinorRadius = 2.0
          Locomotor = SET_NORMAL LbLoco
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = SabotageFakeBuildingCrateCollide SabFake01
            BuildingPickup = Yes
          End
          Behavior = SabotageInternetCenterCrateCollide SabNet01
            BuildingPickup = Yes
            SabotageDuration = 1000
          End
        End
        Object LbFakeBuilding
          KindOf = SELECTABLE STRUCTURE FS_FAKE
          Geometry = CYLINDER
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 10.0
          Body = StructureBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object LbRealBuilding
          KindOf = SELECTABLE STRUCTURE
          Geometry = CYLINDER
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 10.0
          Body = StructureBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object LbInternetCenter
          KindOf = SELECTABLE STRUCTURE FS_INTERNET_CENTER
          Geometry = CYLINDER
          GeometryMajorRadius = 10.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 10.0
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = InternetHackContain Hack01
            Slots = 4
            AllowInsideKindOf = MONEY_HACKER
          End
          Behavior = SpyVisionUpdate Spy01
            TriggeredBy = Upgrade_LbSpy
            NeedsUpgrade = Yes
            SelfPowered = Yes
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object LbHacker
          KindOf = INFANTRY SELECTABLE MONEY_HACKER
          TransportSlotCount = 1
          Locomotor = SET_NORMAL LbLoco
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = HackInternetAIUpdate Hack01
            UnpackTime = 300
            PackTime = 300
            PackUnpackVariationFactor = 0.0
            CashUpdateDelay = 1000
            CashUpdateDelayFast = 500
            RegularCashAmount = 5
            VeteranCashAmount = 6
            EliteCashAmount = 8
            HeroicCashAmount = 10
          End
          Behavior = DestroyDie Destroy01
          End
        End

        ; ------------------------------------------------------------------ bombs and the plane that drops them
        Object LbSmartBomb
          KindOf = PROJECTILE
          TransportSlotCount = 1
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = PhysicsBehavior Physics01
            Mass = 75.0
          End
          Behavior = SmartBombTargetHomingUpdate Smart01
            CourseCorrectionScalar = 0.8
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object LbDumbBomb
          KindOf = PROJECTILE
          TransportSlotCount = 1
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = PhysicsBehavior Physics01
            Mass = 75.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object LbBomber
          KindOf = SELECTABLE VEHICLE TRANSPORT AIRCRAFT
          Locomotor = SET_NORMAL LbPlaneLoco
          Body = ActiveBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DeliverPayloadAIUpdate Deliver01
            MaxAttempts = 1
            DoorDelay = 34
            DropDelay = 100
            DeliveryDistance = 80.0
          End
          Behavior = TransportContain Transport01
            Slots = 10
            AllowInsideKindOf = PROJECTILE
          End
          Behavior = DestroyDie Destroy01
          End
        End

        ; ------------------------------------------------------------------ the warehouse that cripples and heals
        Object LbWarehouse
          KindOf = STRUCTURE
          Geometry = CYLINDER
          GeometryMajorRadius = 20.0
          GeometryMinorRadius = 20.0
          GeometryHeight = 10.0
          Body = ImmortalBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = SupplyWarehouseCripplingBehavior Cripple01
            SelfHealSupression = 1000
            SelfHealDelay = 500
            SelfHealAmount = 5
          End
          Behavior = SupplyWarehouseDockUpdate Dock01
            NumberApproachPositions = 1
            StartingBoxes = 10
          End
          Behavior = SupplyWarehouseCreate Create01
          End
        End

        ; ------------------------------------------------------------------ the ferry and its passengers
        Object LbFerry
          KindOf = SELECTABLE TRANSPORT
          Geometry = BOX
          GeometryMajorRadius = 20.0
          GeometryMinorRadius = 10.0
          GeometryHeight = 10.0
          Locomotor = SET_NORMAL LbLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = RailedTransportContain Contain01
            Slots = 2
            AllowInsideKindOf = INFANTRY VEHICLE
            DoorOpenTime = 500
            ScatterNearbyOnExit = No
          End
          Behavior = RailedTransportAIUpdate Ai01
            PathPrefixName = Ferry
          End
          Behavior = RailedTransportDockUpdate Dock01
            NumberApproachPositions = 3
            PullInsideDuration = 600
            PushOutsideDuration = 600
            ToleranceDistance = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object LbPassenger
          KindOf = SELECTABLE INFANTRY
          TransportSlotCount = 1
          Geometry = CYLINDER
          GeometryMajorRadius = 2.0
          GeometryMinorRadius = 2.0
          Locomotor = SET_NORMAL LbLoco
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """;

    private static IReadOnlyDictionary<string, string> LastBlockerFiles() => new Dictionary<string, string>(SupplyFiles())
    {
        ["Data/INI/GameData.ini"] = $"GameData\n  ValuePerSupplyBox = {BoxValue}\n  Gravity = -64.0\nEnd\n",
        ["Data/INI/Object/LastBlockers.ini"] = LastBlockerIni,
    };

    /// <summary>Alpha (1) and Beta (2), enemies; a terrain only when the test needs one.</summary>
    private static SimulationWorld LbWorld(out Feeder feeder, out SyntheticFixture fixture, TerrainMap? terrain = null, uint seed = 1)
    {
        fixture = new SyntheticFixture(SimulationRate.Original, files: LastBlockerFiles());
        ImmutableArray<PlayerConfiguration> players = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta")];
        var configuration = new WorldConfiguration(new WorldId(1), SimulationRate.Original, seed, players, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain?.Fingerprint ?? string.Empty);
        var world = terrain is null ? new SimulationWorld(configuration, fixture.Templates) : new SimulationWorld(configuration, fixture.Templates, terrain);
        feeder = new Feeder(world);
        return world;
    }

    private static SimulationWorld LbRestore(SimulationWorld live, SyntheticFixture fixture, TerrainMap? terrain = null)
        => WorldSnapshot.Restore(WorldSnapshot.Write(live), fixture.Templates, fixture.Fingerprint, terrain);

    /// <summary>The live world and a copy restored from its snapshot stay hash identical for <paramref name="ticks"/> ticks.</summary>
    private static void LbSnapshotContinues(SimulationWorld live, SyntheticFixture fixture, string what, int ticks = 120, TerrainMap? terrain = null)
    {
        var restored = LbRestore(live, fixture, terrain);
        Check.That(restored.StateHash == live.StateHash, $"{what}: the snapshot restores to the same hash at tick {live.Tick}.");
        for (var tick = 0; tick < ticks; tick++) Check.That(live.Step().StateHash == restored.Step().StateHash, $"{what}: the restored copy diverges at tick +{tick}.");
    }

    private static Entity LbOne(SimulationWorld world, string template, int nth = 0) => world.FindEntity(new EntityId(IdOf(world, template, nth)))!;

    // ============================================================================================================ the catalog

    public static void LastBlockerModulesAreImplemented()
    {
        var registry = ModuleBindingTable.Default.Registry;
        foreach (var name in LastBlockerModules)
        {
            Check.That(registry.TryGet(name, out var descriptor) && !descriptor.CompatibilityOnly && descriptor.IsImplementedSimulationModule, $"{name} is an implemented simulation module.");
            Check.That(ModuleBindingTable.Default.TryGet(name, out _), $"{name} has a binding.");
        }
        Check.That(!registry.Descriptors.Any(value => value.Category == ModuleCategory.Simulation && value.CompatibilityOnly), "No simulation module of the catalog is left unimplemented.");
        var fixture = new SyntheticFixture(SimulationRate.Original, files: LastBlockerFiles());
        var missing = fixture.Templates.Templates.Where(value => value.Name.StartsWith("Lb", StringComparison.Ordinal) && !value.IsInstantiable).Select(value => value.Name + ":" + string.Join(",", value.MissingModules)).ToArray();
        Check.That(missing.Length == 1 && missing[0].StartsWith("LbBrokenUnit", StringComparison.Ordinal), "Every fixture object instantiates except the deliberately broken one: " + string.Join("; ", missing));
        Check.That(fixture.Templates.TryGet("LbSalvageTank", out var tank) && CrateWeapon(tank, 0) < 0 && CrateWeapon(tank, 1) >= 0
            && CrateWeapon(tank, 2) >= 0 && CrateWeapon(tank, 1) != CrateWeapon(tank, 2) && tank.CrateUpgradeArmor[1] is not null && tank.CrateUpgradeArmor[2] is not null && tank.CrateUpgradeArmor[0] is null,
            "The CRATEUPGRADE weapon sets and CRATE_UPGRADE armor sets are resolved per level.");
        Check.That(fixture.Templates.TryGet("LbPlainTank", out var plain) && CrateWeapon(plain, 1) < 0 && CrateWeapon(plain, 2) < 0 && plain.CrateUpgradeArmor.All(armor => armor is null), "An object without crate sets has none.");
    }

    /// <summary>The primary weapon of the weapon set that names exactly <c>CRATEUPGRADE_ONE</c> (level 1) or <c>CRATEUPGRADE_TWO</c> (level 2); -1 when there is none (level 0 never has one).</summary>
    private static int CrateWeapon(ObjectTemplate template, int level) => level == 0 ? -1 : ExactSetPrimary(template, level == 1 ? "CRATEUPGRADE_ONE" : "CRATEUPGRADE_TWO");

    /// <summary>The primary weapon of the weapon set that names exactly <c>CARBOMB</c>; -1 when there is none.</summary>
    private static int CarBombWeapon(ObjectTemplate template) => ExactSetPrimary(template, "CARBOMB");

    private static int ExactSetPrimary(ObjectTemplate template, string condition)
    {
        if (!NameTablesPhase5.WeaponSetCondition.TryIndexOf(condition, out var bit)) throw new InvalidOperationException(condition);
        return template.WeaponSets.FirstOrDefault(set => set.Conditions == 1u << bit)?.Primary ?? -1;
    }

    // ============================================================================================================ SalvageCrateCollide

    public static void LastBlockersSalvageCrate()
    {
        var world = LbWorld(out var feeder, out var fixture);
        var alpha = new PlayerId(1);
        var tank = CsSpawn(world, feeder, "LbSalvageTank", 1, 0);
        Check.That(tank.CrateWeaponLevel == 0 && tank.CrateArmorLevel == 0 && tank.WeaponClip == 1, "A fresh salvager has no crate upgrades and the base weapon.");
        Check.That(world.AdjustDamageByArmor(tank, DamageTypes.Explosion, Fixed.FromInt(100)) == Fixed.FromInt(100), "The base armor takes full damage.");

        void Pickup(Entity unit, string crate, int x = 30)
        {
            var box = CsSpawn(world, feeder, crate, 0, x, (int)unit.Y.ToDecimal());
            feeder.SetMoveTarget(1, unit.Id.Value, x + 70, (int)unit.Y.ToDecimal());
            CsRun(world, 80, () => !CsExists(world, box));
            Check.That(!CsExists(world, box), $"{crate} was picked up.");
            feeder.SetMoveTarget(1, unit.Id.Value, 0, (int)unit.Y.ToDecimal());
            CsRun(world, 80, () => unit.X < Fixed.FromInt(2));
        }

        var random = RandomFingerprint(world);
        Pickup(tank, "LbSalvageCrate");
        Check.That(tank.CrateArmorLevel == 1 && tank.CrateWeaponLevel == 0, "An armor salvager takes the armor set first.");
        Check.That(world.AdjustDamageByArmor(tank, DamageTypes.Explosion, Fixed.FromInt(100)) == Fixed.FromInt(50), "CRATE_UPGRADE_ONE halves the damage.");
        Pickup(tank, "LbSalvageCrate");
        Check.That(tank.CrateArmorLevel == 2 && world.AdjustDamageByArmor(tank, DamageTypes.Explosion, Fixed.FromInt(100)) == Fixed.FromInt(25), "A second crate gives CRATE_UPGRADE_TWO.");
        Pickup(tank, "LbSalvageCrate");
        Check.That(tank.CrateArmorLevel == 2 && tank.CrateWeaponLevel == 1 && tank.WeaponClip == 2, $"Fully armored, a weapon salvager takes the weapon set and its clip is refilled for the new weapon ({tank.WeaponClip}).");
        Pickup(tank, "LbSalvageCrate");
        Check.That(tank.CrateWeaponLevel == 2 && tank.WeaponClip == 3, "A second weapon crate gives CRATEUPGRADE_TWO.");
        Check.That(RandomFingerprint(world) == random, "Exactly 100 % chances and a fixed money amount draw no random numbers.");
        Check.That(tank.VeterancyLevel == 0 && world.MoneyOf(alpha) == Fixed.Zero, "Nothing else was granted yet.");
        Pickup(tank, "LbSalvageCrate");
        Check.That(tank.VeterancyLevel == 1 && tank.Experience == 100, $"Fully upgraded, a trainable salvager gains one level (level {tank.VeterancyLevel}, experience {tank.Experience}).");
        Pickup(tank, "LbSalvageCrate");
        Pickup(tank, "LbSalvageCrate");
        Check.That(tank.VeterancyLevel == 3 && world.MoneyOf(alpha) == Fixed.Zero, "Levels continue up to heroic.");
        Pickup(tank, "LbSalvageCrate");
        Check.That(tank.VeterancyLevel == 3 && world.MoneyOf(alpha) == Fixed.FromInt(40), $"At the heroic rank the crate pays money (MinMoney = MaxMoney = 40; {world.MoneyOf(alpha)}).");
        Check.That(RandomFingerprint(world) == random, "Still no random draw.");

        // Veterancy and the crate level coexist, and survive a snapshot.
        LbSnapshotContinues(world, fixture, "salvage upgrades", 20);
        var restored = LbRestore(world, fixture);
        var restoredTank = restored.FindEntity(tank.Id)!;
        Check.That(restoredTank.CrateArmorLevel == 2 && restoredTank.CrateWeaponLevel == 2 && restored.AdjustDamageByArmor(restoredTank, DamageTypes.Explosion, Fixed.FromInt(100)) == Fixed.FromInt(25), "The crate levels survive the snapshot and still select the sets.");

        // Only salvagers pick the crate up.
        var plain = CsSpawn(world, feeder, "LbPlainTank", 1, 0, 200);
        var untouched = CsSpawn(world, feeder, "LbSalvageCrate", 0, 30, 200);
        feeder.SetMoveTarget(1, plain.Id.Value, 100, 200);
        CsRun(world, 80);
        Check.That(CsExists(world, untouched) && plain.CrateArmorLevel == 0, "A unit that is not a SALVAGER leaves the salvage crate alone.");
        var before = world.MoneyOf(alpha);

        // A plain salvager (no weapon or armor salvager) levels up, and with a random crate the draws follow the reference order.
        var jeep = CsSpawn(world, feeder, "LbSalvageJeep", 1, 0, 300);
        Pickup(jeep, "LbSalvageCrate");
        Check.That(jeep.VeterancyLevel == 1 && jeep.CrateArmorLevel == 0 && jeep.CrateWeaponLevel == 0, "A plain salvager can only gain levels or money.");
        var drawn = RandomFingerprint(world);
        for (var index = 0; index < 6; index++) Pickup(jeep, "LbSalvageRandomCrate");
        Check.That(RandomFingerprint(world) != drawn, "A crate with random chances consumes draws.");
        Check.That(jeep.VeterancyLevel is >= 1 and <= 3 && world.MoneyOf(alpha) >= before, $"Level or money was granted (level {jeep.VeterancyLevel}, money {world.MoneyOf(alpha)}).");
        Check.That(world.MoneyOf(alpha) == before || ((world.MoneyOf(alpha) - before).ToDecimal() is >= 10 and <= 6 * 90), "The random payout lies in MinMoney..MaxMoney.");
    }

    /// <summary>The random salvage crate is deterministic: two worlds with the same seed end in the same state hash.</summary>
    public static void LastBlockersSalvageCrateIsDeterministic()
    {
        ulong Run()
        {
            var world = LbWorld(out var feeder, out _, seed: 9);
            var jeep = CsSpawn(world, feeder, "LbSalvageJeep", 1, 0);
            for (var index = 0; index < 5; index++)
            {
                var crate = CsSpawn(world, feeder, "LbSalvageRandomCrate", 0, 30);
                feeder.SetMoveTarget(1, jeep.Id.Value, 100, 0);
                CsRun(world, 80, () => !CsExists(world, crate));
                feeder.SetMoveTarget(1, jeep.Id.Value, 0, 0);
                CsRun(world, 80, () => jeep.X < Fixed.FromInt(2));
            }
            return world.StateHash;
        }
        Check.That(Run() == Run(), "Two runs with the same seed pick up the same random crates the same way.");
    }

    // ============================================================================================================ UnitCrateCollide

    public static void LastBlockersUnitCrate()
    {
        var world = LbWorld(out var feeder, out var fixture);
        var runner = CsSpawn(world, feeder, "LbPlainTank", 1, 0);
        var crate = CsSpawn(world, feeder, "LbFreeCrate", 0, 30);
        feeder.SetMoveTarget(1, runner.Id.Value, 100, 0);
        var events = CsRun(world, 80, () => !CsExists(world, crate));
        Check.That(!CsExists(world, crate), "The crate was picked up.");
        CsRun(world, 3);
        var tanks = world.Entities.Where(value => value.Template.Name == "LbPlainTank" && value.Id != runner.Id).ToArray();
        Check.That(tanks.Length == 2 && tanks.All(value => value.Owner == new PlayerId(1)), $"Two new tanks belong to the toucher's player ({tanks.Length}).");
        Check.That(tanks.All(value => value.Angle == runner.Angle), "They face the way the toucher faces.");
        var gap = Fixed.Sqrt((tanks[0].X - tanks[1].X) * (tanks[0].X - tanks[1].X) + (tanks[0].Y - tanks[1].Y) * (tanks[0].Y - tanks[1].Y));
        Check.That(gap > Fixed.FromInt(4) && tanks.All(value => Fixed.Sqrt((value.X - runner.X) * (value.X - runner.X) + (value.Y - runner.Y) * (value.Y - runner.Y)) >= Fixed.FromInt(11)),
            "They are spread around the toucher instead of stacked on it.");
        var collected = events.Single(value => value.Kind == SimEventKind.CrateCollected);
        Check.That(collected.Other == runner.Id && collected.Text == "UnitCrateCollide" && collected.Value == 2, "The pickup is reported with the unit count.");
        LbSnapshotContinues(world, fixture, "unit crate", 40);

        // A name that is no instantiable object leaves the crate where it is and reports the skipped creation.
        var rover = CsSpawn(world, feeder, "LbPlainTank", 2, 0, 200);
        var bad = CsSpawn(world, feeder, "LbBadUnitCrate", 0, 30, 200);
        feeder.SetMoveTarget(2, rover.Id.Value, 100, 200);
        var badEvents = CsRun(world, 80);
        Check.That(CsExists(world, bad), "The crate stays when its unit cannot be created.");
        Check.That(badEvents.Any(value => value.Kind == SimEventKind.CreationSkipped && value.Text == "LbBrokenUnit" && value.Value == CreationSkipReasons.TemplateNotInstantiable), "The skipped creation is reported, not swallowed.");
    }

    // ============================================================================================================ SabotageFakeBuildingCrateCollide

    public static void LastBlockersSabotageFakeBuilding()
    {
        var world = LbWorld(out var feeder, out _);
        var fake = CsSpawn(world, feeder, "LbFakeBuilding", 2, 200);
        var real = CsSpawn(world, feeder, "LbRealBuilding", 2, 200, 100);
        var friendly = CsSpawn(world, feeder, "LbFakeBuilding", 1, 200, 200);
        var bystander = CsSpawn(world, feeder, "LbFakeBuilding", 2, 200, 300);

        Entity Send(int y, Entity building)
        {
            var spy = CsSpawn(world, feeder, "LbSaboteur", 1, 100, y);
            CsAi(spy).OrderApproachGoal(world, building, false);
            return spy;
        }

        var spy1 = Send(0, fake);
        var events = CsRun(world, 120, () => !CsExists(world, spy1));
        Check.That(!CsExists(world, spy1), "The saboteur is used up.");
        CsRun(world, 2);
        Check.That(!CsExists(world, fake) || fake.EffectivelyDead, "The decoy blew up at once.");
        Check.That(events.Any(value => value.Kind == SimEventKind.Sabotaged && value.Entity == fake.Id && value.Text == "SabotageFakeBuildingCrateCollide"), "The sabotage is reported.");
        Check.That(CsExists(world, bystander) && !bystander.EffectivelyDead, "A decoy the saboteur was not sent to is not touched while it walks by.");

        var spy2 = Send(100, real);
        CsRun(world, 120);
        Check.That(CsExists(world, spy2) && CsExists(world, real) && !real.EffectivelyDead && real.Body!.Health == real.Body.MaxHealth, "A building that is not a decoy is not a target.");
        var spy3 = Send(200, friendly);
        CsRun(world, 120);
        Check.That(CsExists(world, spy3) && !friendly.EffectivelyDead, "A friendly decoy is not a target.");
    }

    // ============================================================================================================ SabotageInternetCenterCrateCollide

    public static void LastBlockersSabotageInternetCenter()
    {
        var world = LbWorld(out var feeder, out var fixture);
        var beta = new PlayerId(2);
        var center = CsSpawn(world, feeder, "LbInternetCenter", 2, 200);
        var second = CsSpawn(world, feeder, "LbInternetCenter", 2, 200, 100);
        var hacker = CsSpawn(world, feeder, "LbHacker", 2, 200);
        var spyUpdate = center.Modules.OfType<SpyVisionUpdate>().Single();
        var secondSpy = second.Modules.OfType<SpyVisionUpdate>().Single();
        Check.That(world.TryEnterContainer(hacker, center), "The hacker enters its center.");
        world.CompleteUpgrade(beta, null, "Upgrade_LbSpy");
        world.Step();
        Check.That(spyUpdate.Active && secondSpy.Active, "Both centers spy once the upgrade is done.");
        CsRun(world, 40);
        var income = world.MoneyOf(beta);
        Check.That(income > Fixed.Zero && hacker.Modules.OfType<HackInternetAIUpdate>().Single().IsHacking, "The hacker earns cash inside its center.");

        var spy = CsSpawn(world, feeder, "LbSaboteur", 1, 100);
        CsAi(spy).OrderApproachGoal(world, center, false);
        var events = CsRun(world, 120, () => !CsExists(world, spy));
        var hitAt = world.Tick;
        Check.That(!CsExists(world, spy), "The saboteur reached the center.");
        var sabotaged = events.Single(value => value.Kind == SimEventKind.Sabotaged);
        Check.That(sabotaged.Entity == center.Id && sabotaged.Value == 30, $"The sabotage is reported with its duration in ticks ({sabotaged.Value}).");
        Check.That(!spyUpdate.Active && !secondSpy.Active, "Every Internet Center of the victim stops spying, the stacked second one too.");
        var frozen = world.MoneyOf(beta);
        CsRun(world, 24);
        Check.That(world.MoneyOf(beta) == frozen, $"The hackers inside earn nothing while the center is sabotaged (money {frozen} -> {world.MoneyOf(beta)}).");
        LbSnapshotContinues(world, fixture, "sabotaged center", 100);
        CsRun(world, 70);
        Check.That(spyUpdate.Active && secondSpy.Active, $"Spying resumes after the duration (disabled at tick {hitAt}, now {world.Tick}).");
        Check.That(world.MoneyOf(beta) > frozen, "The hacker earns cash again after the duration.");

        // An ally's center and a decoy are not targets.
        var friendlyCenter = CsSpawn(world, feeder, "LbInternetCenter", 1, 200, 300);
        var spy2 = CsSpawn(world, feeder, "LbSaboteur", 1, 100, 300);
        CsAi(spy2).OrderApproachGoal(world, friendlyCenter, false);
        CsRun(world, 120);
        Check.That(CsExists(world, spy2), "A friendly center is not a target.");
    }

    // ============================================================================================================ SmartBombTargetHomingUpdate

    public static void LastBlockersSmartBombHoming()
    {
        var world = LbWorld(out var feeder, out var fixture);
        var bombId = world.RequestSpawn(fixture.Templates.TryGet("LbSmartBomb", out var bombTemplate) ? bombTemplate.Index : throw new InvalidOperationException("LbSmartBomb"), new PlayerId(1), Fixed.Zero, Fixed.Zero, Fixed.FromInt(300), Fixed.Zero);
        world.Step();
        var bomb = world.FindEntity(bombId)!;
        var homing = bomb.Modules.OfType<SmartBombTargetHomingUpdate>().Single();
        Check.That(!homing.TargetReceived, "A bomb nobody told a target is not homing.");
        CsRun(world, 5);
        Check.That(bomb.X == Fixed.Zero && bomb.Y == Fixed.Zero, "Without a target the bomb does not move sideways.");
        homing.SetTargetPosition(world, Fixed.Zero, Fixed.Zero, Fixed.Zero);
        Check.That(!homing.TargetReceived, "The zero point is refused like the reference refuses it.");
        homing.SetTargetPosition(world, Fixed.FromInt(100), Fixed.FromInt(-40), Fixed.Zero);
        world.Step();
        // CourseCorrectionScalar 0.8: one step goes 20 % of the way.
        Check.That((bomb.X - Fixed.FromInt(20)).Abs() < Fixed.FromDecimal(0.01m) && (bomb.Y + Fixed.FromInt(8)).Abs() < Fixed.FromDecimal(0.01m), $"One tick moves a fifth of the way ({bomb.X}, {bomb.Y}).");
        LbSnapshotContinues(world, fixture, "smart bomb", 30);
        CsRun(world, 60);
        Check.That((bomb.X - Fixed.FromInt(100)).Abs() < Fixed.FromDecimal(0.05m) && (bomb.Y + Fixed.FromInt(40)).Abs() < Fixed.FromDecimal(0.05m), $"The bomb converges on the target ({bomb.X}, {bomb.Y}).");
        Check.That(bomb.Z == Fixed.FromInt(300), "The height is left to the fall.");

        // Close to the ground the bomb is no longer "significantly above the terrain" and keeps its course.
        var low = CsSpawn(world, feeder, "LbSmartBomb", 1, 0, 200);
        low.Modules.OfType<SmartBombTargetHomingUpdate>().Single().SetTargetPosition(world, Fixed.FromInt(50), Fixed.FromInt(200), Fixed.Zero);
        CsRun(world, 10);
        Check.That(low.X == Fixed.Zero, "A bomb on the ground does not home.");
    }

    /// <summary>The bomb leaves its plane (<c>DeliverPayloadAIUpdate</c>), is told the point the plane was sent to and lands on it; a plain bomb lands where it was dropped.</summary>
    public static void LastBlockersSmartBombFromPlane()
    {
        Fixed Landing(string bombTemplate)
        {
            var world = LbWorld(out var feeder, out _);
            var bomber = CsSpawn(world, feeder, "LbBomber", 1, 0, 0);
            var bomb = CsSpawn(world, feeder, bombTemplate, 1, 0, 0);
            Check.That(world.TryEnterContainer(bomb, bomber), "The bomb is loaded into the plane.");
            bomber.Modules.OfType<DeliverPayloadAIUpdate>().Single().DeliverViaModuleData(world, Fixed.FromInt(400), Fixed.Zero);
            Fixed dropX = Fixed.Zero;
            for (var tick = 0; tick < 400; tick++)
            {
                world.Step();
                if (!bomb.ContainedBy.IsValid && dropX == Fixed.Zero) dropX = bomb.X;
                if (dropX != Fixed.Zero && bomb.Z <= world.GroundHeightAt(bomb.X, bomb.Y) && !bomb.Modules.OfType<PhysicsBehavior>().Single().Falling) break;
            }
            Check.That(dropX > Fixed.FromInt(250) && dropX < Fixed.FromInt(390), $"The plane dropped the bomb on approach ({dropX}).");
            if (bombTemplate == "LbSmartBomb")
            {
                var homing = bomb.Modules.OfType<SmartBombTargetHomingUpdate>().Single();
                Check.That(homing.TargetReceived && homing.TargetX == Fixed.FromInt(400) && homing.TargetY == Fixed.Zero, "The plane told the bomb its target.");
            }
            world.VerifyIncrementalHash();
            return bomb.X;
        }
        var smart = Landing("LbSmartBomb");
        var dumb = Landing("LbDumbBomb");
        Check.That((smart - Fixed.FromInt(400)).Abs() < Fixed.One, $"The smart bomb lands on its target ({smart}).");
        Check.That((dumb - Fixed.FromInt(400)).Abs() > Fixed.FromInt(40), $"A plain bomb lands where it was dropped ({dumb}).");
    }

    // ============================================================================================================ SupplyWarehouseCripplingBehavior

    private static void LbSupplyBase(SimulationWorld world, Feeder feeder)
    {
        feeder.Spawn(1, "Depot", x: -100);
        feeder.Spawn(1, "LbWarehouse", x: 100);
        world.Step();
    }

    public static void LastBlockersCripplingBlocksAndHealsTheWarehouse()
    {
        var world = LbWorld(out var feeder, out var fixture);
        LbSupplyBase(world, feeder);
        var warehouse = LbOne(world, "LbWarehouse");
        var cripple = warehouse.Modules.OfType<SupplyWarehouseCripplingBehavior>().Single();
        Check.That(cripple.NextWake == SimulationTiming.SleepForever, "The module sleeps until the warehouse is hit.");
        Check.That(warehouse.WarehouseSupplyBoxes() == 10, "The warehouse starts with its boxes.");

        // 70 % damage: below the really-damaged edge (35 %), so the dock is crippled.
        world.Damage(warehouse.Id, new DamageInfo(EntityId.Invalid, Fixed.FromInt(700), DamageTypes.Explosion, DeathTypes.Normal, false));
        var hit = world.Tick;
        Check.That(((ActiveBody)warehouse.Body!).DamageState == BodyDamageStates.ReallyDamaged, "The hit put the warehouse into the really-damaged state.");
        Check.That(cripple.HealingSuppressedUntil == hit + 30, $"The hit starts the 1 s healing suppression ({cripple.HealingSuppressedUntil}, hit at {hit}).");
        Check.That(cripple.NextWake == hit + 30, "The module wakes when the suppression ends.");
        world.Step();

        feeder.Spawn(1, "Hauler", x: 0);
        world.Step();
        var hauler = LbOne(world, "Hauler");
        Check.That(world.Submit(new DockCommand(new PlayerId(1), 900, world.Tick, hauler.Id, warehouse.Id)).Accepted, "The dock order is accepted.");
        CsRun(world, 100);
        Check.That(warehouse.WarehouseSupplyBoxes() == 10 && hauler.CarriedSupplyBoxes() == 0 && !hauler.EffectivelyDead, "A crippled warehouse lets nobody in: no boxes leave it and the hauler is unharmed.");
        Check.That(warehouse.Body!.Health > Fixed.FromInt(300) && warehouse.Body.Health < Fixed.FromInt(350), $"After the rest it heals 5 every 15 ticks ({warehouse.Body.Health}).");
        LbSnapshotContinues(world, fixture, "crippled warehouse", 60);

        // 5 per 15 ticks: from 300 to above 350 takes 11 heals; then the dock works again and the hauler loads.
        CsRun(world, 600, () => warehouse.WarehouseSupplyBoxes() < 10);
        Check.That(warehouse.WarehouseSupplyBoxes() < 10 && warehouse.Body.Health > Fixed.FromInt(350), $"Once healed out of the really-damaged state the dock works again ({warehouse.WarehouseSupplyBoxes()} boxes left, health {warehouse.Body.Health}).");
        Check.That(!hauler.EffectivelyDead, "The hauler that waited lived.");
    }

    public static void LastBlockersCripplingKillsADockerInsideAndRestsOnEveryHit()
    {
        var world = LbWorld(out var feeder, out _);
        LbSupplyBase(world, feeder);
        feeder.Spawn(1, "Hauler", x: 0);
        world.Step();
        var warehouse = LbOne(world, "LbWarehouse");
        var hauler = LbOne(world, "Hauler");
        Check.That(world.Submit(new DockCommand(new PlayerId(1), 900, world.Tick, hauler.Id, warehouse.Id)).Accepted, "The dock order is accepted.");
        // The hauler is "inside" from the entry until it leaves: the first box that leaves the warehouse proves it is in there.
        CsRun(world, 400, () => warehouse.WarehouseSupplyBoxes() < 10);
        Check.That(warehouse.WarehouseSupplyBoxes() < 10 && !hauler.EffectivelyDead, "The hauler is being loaded.");
        world.Damage(warehouse.Id, new DamageInfo(EntityId.Invalid, Fixed.FromInt(700), DamageTypes.Explosion, DeathTypes.Normal, false));
        world.Step();
        Check.That(hauler.EffectivelyDead, "A truck that is between the entry and the exit when the dock is crippled is killed.");

        // Every hit restarts the suppression.
        var cripple = warehouse.Modules.OfType<SupplyWarehouseCripplingBehavior>().Single();
        CsRun(world, 10);
        var first = cripple.HealingSuppressedUntil;
        world.Damage(warehouse.Id, new DamageInfo(EntityId.Invalid, Fixed.FromInt(10), DamageTypes.Explosion, DeathTypes.Normal, false));
        Check.That(cripple.HealingSuppressedUntil == world.Tick + 30 && cripple.HealingSuppressedUntil > first, "A further hit pushes the end of the suppression out.");

        // A warehouse that is only scratched heals back to full health and then sleeps for good.
        var scratched = LbWorld(out var scratchFeeder, out _);
        LbSupplyBase(scratched, scratchFeeder);
        var other = LbOne(scratched, "LbWarehouse");
        scratched.Damage(other.Id, new DamageInfo(EntityId.Invalid, Fixed.FromInt(10), DamageTypes.Explosion, DeathTypes.Normal, false));
        CsRun(scratched, 120);
        var module = other.Modules.OfType<SupplyWarehouseCripplingBehavior>().Single();
        Check.That(other.Body!.Health == other.Body.MaxHealth && module.NextWake == SimulationTiming.SleepForever, $"Healed to full the module goes back to sleep (health {other.Body.Health}, next wake {module.NextWake}).");
    }

    // ============================================================================================================ the ferry

    /// <summary>A 60 x 40 cell flat map with two ferry paths: path 1 Start01 -> Bend01 -> End01 (the way back and forth), path 2 Start02 -> End02.</summary>
    private static TerrainMap FerryTerrain()
    {
        var waypoints = new (string Name, decimal X, decimal Y, decimal Z)[]
        {
            ("FerryStart01", 100m, 100m, 0m), ("FerryBend01", 250m, 200m, 0m), ("FerryEnd01", 400m, 100m, 0m),
            ("FerryStart02", 100m, 300m, 0m), ("FerryEnd02", 400m, 300m, 0m),
        };
        var map = SyntheticMaps.Build(60, 40, 1, waypoints: waypoints);
        ImmutableArray<MapWaypointLink> links = [new(1, 2), new(2, 3), new(4, 5)];
        return TerrainMap.Build(map with { WaypointLinks = links }, "ferry");
    }

    private static void LbDockPassenger(SimulationWorld world, Feeder feeder, Entity ferry, int x, int y, out Entity passenger)
    {
        passenger = CsSpawn(world, feeder, "LbPassenger", 1, x, y);
        Check.That(feeder.Dock(1, passenger.Id.Value, ferry.Id.Value).Accepted, "The dock order for the ferry is accepted.");
    }

    public static void LastBlockersFerryLoadsSailsAndUnloads()
    {
        var terrain = FerryTerrain();
        var world = LbWorld(out var feeder, out var fixture, terrain);
        var ferry = CsSpawn(world, feeder, "LbFerry", 1, 390, 110);
        var ai = ferry.Modules.OfType<RailedTransportAIUpdate>().Single();
        world.Step();
        Check.That(ai.PathCount == 2 && ai.CurrentPath == 0 && ai.InTransit && !ai.DockIsOpen, $"The ferry found both paths, picked the one whose end is closest and sails there with its dock closed ({ai.PathCount} paths, path {ai.CurrentPath}).");
        CsRun(world, 40, () => !ai.InTransit);
        Check.That(!ai.InTransit && ai.DockIsOpen && (ferry.X - Fixed.FromInt(400)).Abs() <= Fixed.FromInt(5), $"Within 5 units of the end waypoint the ferry stops being in transit and opens its dock ({ferry.X}, {ferry.Y}).");

        // Orders from the player (other than the evacuation) are ignored.
        feeder.SetMoveTarget(1, ferry.Id.Value, 50, 50);
        CsRun(world, 10);
        Check.That(!ferry.HasMoveTarget && (ferry.X - Fixed.FromInt(400)).Abs() <= Fixed.FromInt(6), "A move order from the player does nothing to the ferry.");

        // The first passenger docks: held and unselectable while it is pulled in, then inside.
        LbDockPassenger(world, feeder, ferry, 370, 100, out var first);
        var seenPulled = false;
        var seenBusy = false;
        CsRun(world, 300, () =>
        {
            seenBusy |= ai.DockIsLoadingOrUnloading;
            if (ai.DockIsLoadingOrUnloading) seenPulled |= (first.ScriptDisableMask & Entity.ScriptHeld) != 0 && first.HasStatus(ObjectStatusBits.Index("UNSELECTABLE"));
            return first.ContainedBy.IsValid;
        });
        Check.That(seenPulled, $"While it is pulled in the passenger is held and cannot be selected (busy {seenBusy}, docking {CsAi(first).IsDocking}, at ({first.X}, {first.Y}), ferry ({ferry.X}, {ferry.Y}), open {ai.DockIsOpen}, contained {first.ContainedBy.Value}, tick {world.Tick}).");
        Check.That(first.ContainedBy == ferry.Id && ferry.Container!.ContainCount == 1, $"The passenger is inside the ferry ({first.ContainedBy.Value}).");
        Check.That(!ai.DockIsLoadingOrUnloading && (first.ScriptDisableMask & Entity.ScriptHeld) != 0, "The pull-in is over; the rider stays held inside.");
        var snapshot = LbRestore(world, fixture, terrain);

        LbDockPassenger(world, feeder, ferry, 372, 104, out var second);
        CsRun(world, 300, () => second.ContainedBy.IsValid);
        Check.That(second.ContainedBy == ferry.Id && ferry.Container!.ContainCount == 2, "A second passenger boards.");
        LbDockPassenger(world, feeder, ferry, 372, 96, out var third);
        CsRun(world, 120);
        Check.That(!third.ContainedBy.IsValid, "A full ferry takes nobody else: the docker is not let in.");
        CsAi(third).OrderIdle(world);
        world.Step();

        // Execute: the ferry takes the next path (round robin: path 2) with its dock closed.
        ai.ExecuteRailedTransport(world);
        Check.That(ai.InTransit && !ai.DockIsOpen && ai.CurrentPath == 1, "Executing starts the next path and closes the dock.");
        CsRun(world, 20);
        Check.That(feeder.ExitContainer(1, first.Id.Value).Reason == CommandRejection.NotPermitted, "Nobody may leave a ferry in transit.");
        var stranger = CsSpawn(world, feeder, "LbPassenger", 1, 40, 40);
        feeder.Dock(1, stranger.Id.Value, ferry.Id.Value);
        CsRun(world, 5);
        Check.That(!CsAi(stranger).IsDocking, "A closed dock refuses a docker at once.");
        LbSnapshotContinues(world, fixture, "ferry in transit", 100, terrain);
        var visited = new HashSet<string>();
        CsRun(world, 900, () => { if ((ferry.X - Fixed.FromInt(100)).Abs() < Fixed.FromInt(3) && (ferry.Y - Fixed.FromInt(300)).Abs() < Fixed.FromInt(3)) visited.Add("start"); return !ai.InTransit; });
        Check.That(visited.Contains("start") && !ai.InTransit && ai.DockIsOpen && (ferry.X - Fixed.FromInt(400)).Abs() <= Fixed.FromInt(6) && (ferry.Y - Fixed.FromInt(300)).Abs() <= Fixed.FromInt(6),
            $"The ferry sailed to the start of path 2 and along it to the end, where the dock opened ({ferry.X}, {ferry.Y}).");

        // One passenger leaves through the dock (the first in the list).
        Check.That(feeder.ExitContainer(1, second.Id.Value).Accepted, "An exit command is accepted at the end of the path.");
        CsRun(world, 2);
        Check.That(ai.DockIsLoadingOrUnloading && !first.ContainedBy.IsValid && second.ContainedBy == ferry.Id, "The dock unloads the first rider of the list, not the one that was named.");
        CsRun(world, 200, () => !ai.DockIsLoadingOrUnloading);
        Check.That(!ai.DockIsLoadingOrUnloading && (first.ScriptDisableMask & Entity.ScriptHeld) == 0 && !first.HasStatus(ObjectStatusBits.Index("UNSELECTABLE")), "The unloaded rider is free and selectable again.");
        Check.That(Fixed.Sqrt((first.X - ferry.X) * (first.X - ferry.X) + (first.Y - ferry.Y) * (first.Y - ferry.Y)) is var offset && (offset - Fixed.FromInt(20)).Abs() < Fixed.FromInt(3), $"It stands at the ferry's exit point, one major radius ahead ({offset}).");
        Check.That(ferry.Container!.ContainCount == 1, "The other passenger is still aboard.");

        // An evacuation unloads the rest; the dock stays open.
        LbSnapshotContinues(world, fixture, "ferry docked again", 60, terrain);
        Check.That(feeder.Evacuate(1, ferry.Id.Value).Accepted, "The evacuation is accepted.");
        CsRun(world, 5);
        Check.That(ai.DockIsLoadingOrUnloading, "The evacuation starts the unloading.");
        var mid = LbRestore(world, fixture, terrain);
        for (var tick = 0; tick < 120; tick++) Check.That(world.Step().StateHash == mid.Step().StateHash, $"A snapshot taken mid-unload continues identically (tick +{tick}).");
        CsRun(world, 200, () => !ai.DockIsLoadingOrUnloading);
        Check.That(ferry.Container.ContainCount == 0 && !second.ContainedBy.IsValid && (second.ScriptDisableMask & Entity.ScriptHeld) == 0 && ai.DockIsOpen, "Everybody is out and the ferry is open for the next load.");
        world.VerifyIncrementalHash();

        // The snapshot of the first boarding replays it identically.
        for (var tick = 0; tick < 10; tick++) snapshot.Step();
        Check.That(snapshot.FindEntity(first.Id)!.ContainedBy == ferry.Id, "The snapshot taken right after the first boarding still has the passenger inside.");
    }

    public static void LastBlockersFerryDeterminismAndEvacuationRules()
    {
        ulong Run()
        {
            var terrain = FerryTerrain();
            var world = LbWorld(out var feeder, out _, terrain);
            var ferry = CsSpawn(world, feeder, "LbFerry", 1, 390, 110);
            var ai = ferry.Modules.OfType<RailedTransportAIUpdate>().Single();
            CsRun(world, 40, () => !ai.InTransit);
            LbDockPassenger(world, feeder, ferry, 370, 100, out var passenger);
            CsRun(world, 300, () => passenger.ContainedBy.IsValid);
            ai.ExecuteRailedTransport(world);
            // While in transit an evacuation does nothing.
            Check.That(feeder.Evacuate(1, ferry.Id.Value).Accepted, "The evacuation command is accepted even in transit.");
            CsRun(world, 5);
            Check.That(passenger.ContainedBy == ferry.Id && !ai.DockIsLoadingOrUnloading, "An evacuation of a ferry in transit unloads nobody.");
            CsRun(world, 900, () => !ai.InTransit);
            feeder.Evacuate(1, ferry.Id.Value);
            CsRun(world, 300, () => !passenger.ContainedBy.IsValid && !ai.DockIsLoadingOrUnloading);
            Check.That(!passenger.ContainedBy.IsValid, "The evacuation at the end of the path unloads the passenger.");
            return world.StateHash;
        }
        Check.That(Run() == Run(), "Two ferry runs end in the same state hash.");

        // A map without ferry paths: the ferry has nowhere to go, stays idle with an open dock and ignores Execute.
        var world = LbWorld(out var feeder, out _, SyntheticMapsTerrainWithoutFerry());
        var lonely = CsSpawn(world, feeder, "LbFerry", 1, 100, 100);
        var lonelyAi = lonely.Modules.OfType<RailedTransportAIUpdate>().Single();
        CsRun(world, 30);
        Check.That(lonelyAi.PathCount == 0 && lonelyAi.CurrentPath == -1 && !lonelyAi.InTransit && lonelyAi.DockIsOpen, "Without paths the ferry never sails and its dock stays open.");
        lonelyAi.ExecuteRailedTransport(world);
        Check.That(!lonelyAi.InTransit && lonelyAi.CurrentPath == -1, "Execute without a path does nothing.");
    }

    private static TerrainMap SyntheticMapsTerrainWithoutFerry() => TerrainMap.Build(SyntheticMaps.Build(60, 40, 1), "plain");

    // ============================================================================================================ real data

    /// <summary>
    /// Retail data: every object that uses one of the nine modules is instantiable, nothing in the retail base data is blocked any more, and the nine kinds of object spawn, run, die and
    /// stay deterministic; a retail salvager collects a retail salvage crate, the retail ferry finds the waypoint paths of a map made for it.
    /// </summary>
    public static void RealLastBlockers(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, SimulationRate.Original);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);

        var blocked = catalog.Templates.Where(template => !template.IsInstantiable).Select(template => template.Name + ":" + string.Join("/", template.MissingModules)).ToArray();
        Check.That(blocked.Length == 0, $"Every retail object is instantiable ({blocked.Length} blocked: {string.Join(", ", blocked.Take(10))}).");
        var carriers = catalog.Templates.Where(template => template.Modules.Any(module => LastBlockerModules.Contains(module.Name))).ToArray();
        var usage = LastBlockerModules.ToDictionary(name => name, name => carriers.Count(template => template.Modules.Any(module => module.Name == name)));
        Check.That(usage.Values.All(count => count > 0), "Each of the nine modules is used by a retail object: " + string.Join(", ", usage.Select(pair => $"{pair.Key} {pair.Value}")));
        Console.WriteLine($"       real last blockers: {carriers.Length} retail objects use the nine modules ({string.Join(", ", usage.Select(pair => $"{pair.Key} {pair.Value}"))}): {string.Join(", ", carriers.Select(template => template.Name))}");

        var ferryTerrain = FerryTerrain();
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
        (ulong[] Hashes, int Created) Run()
        {
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), SimulationRate.Original, 5, players, "real-last-blockers", fingerprint, ferryTerrain.Fingerprint), catalog, ferryTerrain);
            var sequence = 0UL;
            var column = 0;
            foreach (var template in carriers)
            {
                var x = 150 + column % 3 * 90;
                var y = 100 + column / 3 * 80;
                column++;
                Check.That(world.Submit(new SpawnCommand(PlayerId.System, ++sequence, world.Tick, template.Name, new PlayerId(column % 2 + 1), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, $"{template.Name} spawns.");
            }
            var hashes = new List<ulong>();
            hashes.Add(world.Step().StateHash);
            var created = carriers.Count(template => world.Entities.Any(entity => entity.Template.Index == template.Index));
            for (var tick = 1; tick < 150; tick++) hashes.Add(world.Step().StateHash);
            foreach (var entity in world.Entities.OrderBy(value => value.Id.Value).ToArray())
                world.Submit(new KillCommand(PlayerId.System, ++sequence, world.Tick, entity.Id));
            for (var tick = 0; tick < 300; tick++) hashes.Add(world.Step().StateHash);
            world.VerifyIncrementalHash();
            return ([..hashes], created);
        }
        var (first, created) = Run();
        Check.That(created == carriers.Length, $"Every retail object of the nine modules was created ({created} of {carriers.Length}).");
        Check.That(first.SequenceEqual(Run().Hashes), "Two runs are hash identical on every tick.");

        // The retail salvager takes the retail salvage crate: GLA players only (PickupScience), armor first.
        var gla = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East", "FactionGLA"), new PlayerConfiguration(new PlayerId(2), "West", "FactionAmerica"));
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(2), SimulationRate.Original, 5, gla, "real-salvage", fingerprint), catalog);
        Check.That(world.Sciences.TryGet("SCIENCE_GLA", out var glaScience) && world.PlayerSciences(new PlayerId(1)).Has(glaScience.Index) && !world.PlayerSciences(new PlayerId(2)).Has(glaScience.Index), "The GLA player owns SCIENCE_GLA through its faction, the American does not.");
        var feeder = new Feeder(world);
        var lane = 0;
        void Collect(string unit, string crateName, int owner, Action<Entity> check)
        {
            var template = catalog.TryGet(unit, out var found) && found.IsInstantiable ? found : throw new InvalidOperationException($"{unit} is not instantiable.");
            var y = 100 + 80 * lane++;
            var salvager = CsSpawn(world, feeder, template.Name, owner, 0, y);
            var crate = CsSpawn(world, feeder, crateName, 0, 40, y);
            feeder.SetMoveTarget(owner, salvager.Id.Value, 150, y);
            CsRun(world, 400, () => !CsExists(world, crate));
            check(salvager);
            if (CsExists(world, crate)) Check.That(owner == 2, $"{unit} left the crate where it was");
        }
        Collect("GLAVehicleBattleBus", "SalvageCrate", 1, bus =>
        {
            Check.That(bus.CrateArmorLevel == 1 && bus.Template.CrateUpgradeArmor[1] is not null, $"The retail Battle Bus (ARMOR_SALVAGER) took the first armor set of the retail SalvageCrate (armor level {bus.CrateArmorLevel}).");
            Check.That(world.AdjustDamageByArmor(bus, DamageTypes.Explosion, Fixed.FromInt(100)) <= Fixed.FromInt(100), "The crate armor never takes more damage than full damage.");
        });
        Collect("GLATankScorpion", "SalvageCrate", 1, scorpion =>
        {
            Check.That(scorpion.CrateWeaponLevel == 1 && scorpion.CrateArmorLevel == 0 && CrateWeapon(scorpion.Template, 1) >= 0, $"The retail Scorpion (WEAPON_SALVAGER) took the first weapon set (weapon level {scorpion.CrateWeaponLevel}).");
            Check.That(scorpion.WeaponClip == (catalog.Weapons[CrateWeapon(scorpion.Template, 1)].ClipSize is var clip and > 0 ? clip : int.MaxValue) && CrateWeapon(scorpion.Template, 1) != scorpion.Template.DefaultWeapon, "Its weapon set switched to the upgraded weapon and the clip was refilled for it.");
        });
        Collect("GLAVehicleBattleBus", "SalvageCrate", 2, american => Check.That(american.CrateArmorLevel == 0, "Without SCIENCE_GLA (PickupScience) the crate stays."));

        // The retail ferry reads the "Ferry" waypoint paths of a map made for it.
        var terrain = FerryTerrain();
        var ferryWorld = new SimulationWorld(new WorldConfiguration(new WorldId(3), SimulationRate.Original, 5, players, "real-ferry", fingerprint, terrain.Fingerprint), catalog, terrain);
        var ferryFeeder = new Feeder(ferryWorld);
        var ferry = CsSpawn(ferryWorld, ferryFeeder, "CivilianVehicleFerry", 1, 390, 110);
        var ai = ferry.Modules.OfType<RailedTransportAIUpdate>().Single();
        CsRun(ferryWorld, 60);
        Check.That(ai.PathCount == 2 && ai.CurrentPath == 0, $"The retail ferry found the paths of the map ({ai.PathCount} paths, current {ai.CurrentPath}).");
    }
}
