using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Schema;

namespace CnCNew.Data.Registry;

/// <summary>
/// Explicit catalog of all 205 module types used by the shipped Zero Hour data. Block kind and interface masks were
/// extracted mechanically from the reference headers (<c>getInterfaceMask()</c>, <c>getModuleType()</c>; Draw modules
/// from <c>W3DModuleFactory.cpp</c>). Modules without a field schema are known but not implemented
/// (<see cref="ModuleDescriptor.CompatibilityOnly"/>): they are inventoried and counted, never instantiated.
/// </summary>
public static class ModuleCatalog
{
    private static IEnumerable<ModuleDescriptor> Build()
    {
        var implemented = ImplementedSchemas();
        foreach (var (name, block, codes) in Rows)
        {
            var interfaces = ParseCodes(codes);
            var category = block is ModuleBlockKind.Draw or ModuleBlockKind.ClientUpdate ? ModuleCategory.Presentation : ModuleCategory.Simulation;
            var owners = ImmutableHashSet.Create(ModuleOwnerKind.Object);
            if (implemented.TryGetValue(name, out var schema))
                yield return new ModuleDescriptor(name, block, interfaces, owners, schema, false, null, category);
            else
                yield return new ModuleDescriptor(name, block, interfaces, owners, [], true, PlannedTarget(name, category), category);
        }
    }

    private static string PlannedTarget(string name, ModuleCategory category)
    {
        if (category == ModuleCategory.Presentation) return "phase-7-presentation";
        var phase6 = name.EndsWith("AIUpdate", StringComparison.Ordinal) || name == "AIUpdateInterface"
            || name.StartsWith("Bridge", StringComparison.Ordinal) || name.StartsWith("Railroad", StringComparison.Ordinal)
            || name.StartsWith("RailedTransport", StringComparison.Ordinal)
            || name is "PhysicsBehavior" or "ToppleUpdate" or "StructureToppleUpdate" or "StructureCollapseUpdate" or "FloatUpdate"
                or "ParkingPlaceBehavior" or "FlightDeckBehavior" or "WaveGuideUpdate" or "TensileFormationUpdate" or "HordeUpdate"
                or "SlavedUpdate" or "MobMemberSlavedUpdate" or "DynamicShroudClearingRangeUpdate" or "HeightDieUpdate";
        return phase6 ? "phase-6-world-movement-ai" : "phase-5-rules-combat-economy";
    }

    private static ModuleInterfaces ParseCodes(string codes)
    {
        var result = ModuleInterfaces.None;
        foreach (var code in codes)
            result |= code switch
            {
                'U' => ModuleInterfaces.Update, 'D' => ModuleInterfaces.Die, 'M' => ModuleInterfaces.Damage, 'C' => ModuleInterfaces.Create,
                'L' => ModuleInterfaces.Collide, 'B' => ModuleInterfaces.Body, 'N' => ModuleInterfaces.Contain, 'G' => ModuleInterfaces.Upgrade,
                'S' => ModuleInterfaces.SpecialPower, 'X' => ModuleInterfaces.Destroy, 'W' => ModuleInterfaces.Draw, 'E' => ModuleInterfaces.ClientUpdate,
                _ => throw new InvalidOperationException($"Unknown interface code '{code}'."),
            };
        return result;
    }

    // Strict schemas of the simulation modules implemented in phase 4. Field lists come from the reference
    // FieldParse tables (ActiveBody.cpp, DieModule.cpp, LifetimeUpdate.h, DeletionUpdate.h, FXListDie.cpp, ...) and were
    // verified against every occurrence in the shipped INIs (see docs/SIMULATION-CORE.md).
    private static ImmutableDictionary<string, ImmutableArray<FieldSchema>> ImplementedSchemas()
    {
        FieldSchema Real(string name) => new(name, FieldType.Real, Source: "ActiveBody.cpp");
        ImmutableArray<FieldSchema> Body() =>
        [
            Real("MaxHealth"), Real("InitialHealth"), Real("SubdualDamageCap"),
            new("SubdualDamageHealRate", FieldType.DurationMs, Source: "ActiveBody.cpp"), Real("SubdualDamageHealAmount"),
        ];
        ImmutableArray<FieldSchema> DieMux() =>
        [
            new("DeathTypes", FieldType.AllNoneDeltaFlags, Table: NameTables.DeathType, Source: "DieModule.cpp DieMuxData"),
            new("VeterancyLevels", FieldType.AllNoneDeltaFlags, Table: NameTables.Veterancy, Source: "DieModule.cpp DieMuxData"),
            new("ExemptStatus", FieldType.Flags, Table: NameTables.ObjectStatus, Source: "DieModule.cpp DieMuxData"),
            new("RequiredStatus", FieldType.Flags, Table: NameTables.ObjectStatus, Source: "DieModule.cpp DieMuxData"),
        ];
        ImmutableArray<FieldSchema> Lifetime() =>
        [
            new("MinLifetime", FieldType.DurationMs, Required: true, Source: "LifetimeUpdate.h / DeletionUpdate.h"),
            new("MaxLifetime", FieldType.DurationMs, Required: true, Source: "LifetimeUpdate.h / DeletionUpdate.h"),
        ];
        var builder = ImmutableDictionary.CreateBuilder<string, ImmutableArray<FieldSchema>>(StringComparer.Ordinal);
        builder["ActiveBody"] = Body();
        builder["StructureBody"] = Body();
        builder["ImmortalBody"] = Body();
        builder["HighlanderBody"] = Body();
        builder["InactiveBody"] = [];
        builder["LifetimeUpdate"] = Lifetime();
        builder["DeletionUpdate"] = Lifetime();
        builder["DestroyDie"] = DieMux();
        builder["KeepObjectDie"] = DieMux();
        builder["CreateObjectDie"] =
        [
            ..DieMux(),
            new("CreationList", FieldType.Text, Source: "CreateObjectDie.cpp"),
            new("TransferPreviousHealth", FieldType.Bool, Source: "CreateObjectDie.cpp"),
        ];
        builder["InstantDeathBehavior"] =
        [
            ..DieMux(),
            new("FX", FieldType.ReferenceList, ReferenceKind: DefinitionKind.FxList, Source: "InstantDeathBehavior.cpp"),
            new("OCL", FieldType.Text, Source: "InstantDeathBehavior.cpp"),
            new("Weapon", FieldType.ReferenceList, ReferenceKind: DefinitionKind.Weapon, Source: "InstantDeathBehavior.cpp"),
        ];
        builder["FXListDie"] =
        [
            ..DieMux(),
            new("DeathFX", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "FXListDie.cpp"),
            new("OrientToObject", FieldType.Bool, Source: "FXListDie.cpp"),
            new("StartsActive", FieldType.Bool, Source: "UpgradeMux"),
            new("TriggeredBy", FieldType.ReferenceList, ReferenceKind: DefinitionKind.Upgrade, Source: "UpgradeMux"),
            new("ConflictsWith", FieldType.ReferenceList, ReferenceKind: DefinitionKind.Upgrade, Source: "UpgradeMux"),
        ];
        return builder.ToImmutable();
    }

    private static readonly (string Name, ModuleBlockKind Block, string Codes)[] Rows =
    [
        ("AIUpdateInterface", ModuleBlockKind.Behavior, "U"),
        ("ActiveBody", ModuleBlockKind.Body, "B"),
        ("AnimatedParticleSysBoneClientUpdate", ModuleBlockKind.ClientUpdate, "E"),
        ("AnimationSteeringUpdate", ModuleBlockKind.Behavior, "U"),
        ("ArmorUpgrade", ModuleBlockKind.Behavior, "G"),
        ("AssaultTransportAIUpdate", ModuleBlockKind.Behavior, "U"),
        ("AssistedTargetingUpdate", ModuleBlockKind.Behavior, "U"),
        ("AutoDepositUpdate", ModuleBlockKind.Behavior, "U"),
        ("AutoFindHealingUpdate", ModuleBlockKind.Behavior, "U"),
        ("AutoHealBehavior", ModuleBlockKind.Behavior, "UMG"),
        ("BaikonurLaunchPower", ModuleBlockKind.Behavior, "S"),
        ("BaseRegenerateUpdate", ModuleBlockKind.Behavior, "UM"),
        ("BattleBusSlowDeathBehavior", ModuleBlockKind.Behavior, "UD"),
        ("BattlePlanUpdate", ModuleBlockKind.Behavior, "U"),
        ("BeaconClientUpdate", ModuleBlockKind.ClientUpdate, "E"),
        ("BoneFXDamage", ModuleBlockKind.Behavior, "M"),
        ("BoneFXUpdate", ModuleBlockKind.Behavior, "U"),
        ("BridgeBehavior", ModuleBlockKind.Behavior, "UDM"),
        ("BridgeScaffoldBehavior", ModuleBlockKind.Behavior, "U"),
        ("BridgeTowerBehavior", ModuleBlockKind.Behavior, "DM"),
        ("BunkerBusterBehavior", ModuleBlockKind.Behavior, "UD"),
        ("CashBountyPower", ModuleBlockKind.Behavior, "S"),
        ("CashHackSpecialPower", ModuleBlockKind.Behavior, "S"),
        ("CheckpointUpdate", ModuleBlockKind.Behavior, "U"),
        ("ChinookAIUpdate", ModuleBlockKind.Behavior, "U"),
        ("CleanupAreaPower", ModuleBlockKind.Behavior, "S"),
        ("CleanupHazardUpdate", ModuleBlockKind.Behavior, "U"),
        ("CommandButtonHuntUpdate", ModuleBlockKind.Behavior, "U"),
        ("CommandSetUpgrade", ModuleBlockKind.Behavior, "G"),
        ("ConvertToCarBombCrateCollide", ModuleBlockKind.Behavior, "L"),
        ("ConvertToHijackedVehicleCrateCollide", ModuleBlockKind.Behavior, "L"),
        ("CostModifierUpgrade", ModuleBlockKind.Behavior, "G"),
        ("CountermeasuresBehavior", ModuleBlockKind.Behavior, "UG"),
        ("CreateCrateDie", ModuleBlockKind.Behavior, "D"),
        ("CreateObjectDie", ModuleBlockKind.Behavior, "D"),
        ("CrushDie", ModuleBlockKind.Behavior, "D"),
        ("DamDie", ModuleBlockKind.Behavior, "D"),
        ("DefaultProductionExitUpdate", ModuleBlockKind.Behavior, "U"),
        ("DeletionUpdate", ModuleBlockKind.Behavior, "U"),
        ("DeliverPayloadAIUpdate", ModuleBlockKind.Behavior, "U"),
        ("DemoTrapUpdate", ModuleBlockKind.Behavior, "U"),
        ("DeployStyleAIUpdate", ModuleBlockKind.Behavior, "U"),
        ("DestroyDie", ModuleBlockKind.Behavior, "D"),
        ("DozerAIUpdate", ModuleBlockKind.Behavior, "U"),
        ("DumbProjectileBehavior", ModuleBlockKind.Behavior, "U"),
        ("DynamicShroudClearingRangeUpdate", ModuleBlockKind.Behavior, "U"),
        ("EMPUpdate", ModuleBlockKind.Behavior, "U"),
        ("EjectPilotDie", ModuleBlockKind.Behavior, "D"),
        ("EnemyNearUpdate", ModuleBlockKind.Behavior, "U"),
        ("ExperienceScalarUpgrade", ModuleBlockKind.Behavior, "G"),
        ("FXListDie", ModuleBlockKind.Behavior, "DG"),
        ("FireOCLAfterWeaponCooldownUpdate", ModuleBlockKind.Behavior, "U"),
        ("FireSpreadUpdate", ModuleBlockKind.Behavior, "U"),
        ("FireWeaponCollide", ModuleBlockKind.Behavior, "L"),
        ("FireWeaponPower", ModuleBlockKind.Behavior, "S"),
        ("FireWeaponUpdate", ModuleBlockKind.Behavior, "U"),
        ("FireWeaponWhenDamagedBehavior", ModuleBlockKind.Behavior, "UMG"),
        ("FireWeaponWhenDeadBehavior", ModuleBlockKind.Behavior, "DG"),
        ("FirestormDynamicGeometryInfoUpdate", ModuleBlockKind.Behavior, "U"),
        ("FlammableUpdate", ModuleBlockKind.Behavior, "UM"),
        ("FlightDeckBehavior", ModuleBlockKind.Behavior, "UD"),
        ("FloatUpdate", ModuleBlockKind.Behavior, "U"),
        ("GarrisonContain", ModuleBlockKind.Behavior, "UDMLN"),
        ("GenerateMinefieldBehavior", ModuleBlockKind.Behavior, "UDG"),
        ("GrantScienceUpgrade", ModuleBlockKind.Behavior, "G"),
        ("GrantStealthBehavior", ModuleBlockKind.Behavior, "U"),
        ("GrantUpgradeCreate", ModuleBlockKind.Behavior, "C"),
        ("HackInternetAIUpdate", ModuleBlockKind.Behavior, "U"),
        ("HealContain", ModuleBlockKind.Behavior, "UDMLN"),
        ("HeightDieUpdate", ModuleBlockKind.Behavior, "U"),
        ("HelicopterSlowDeathBehavior", ModuleBlockKind.Behavior, "UD"),
        ("HelixContain", ModuleBlockKind.Behavior, "UDMLN"),
        ("HighlanderBody", ModuleBlockKind.Body, "B"),
        ("HijackerUpdate", ModuleBlockKind.Behavior, "U"),
        ("HiveStructureBody", ModuleBlockKind.Body, "B"),
        ("HordeUpdate", ModuleBlockKind.Behavior, "U"),
        ("ImmortalBody", ModuleBlockKind.Body, "B"),
        ("InactiveBody", ModuleBlockKind.Body, "B"),
        ("InstantDeathBehavior", ModuleBlockKind.Behavior, "D"),
        ("InternetHackContain", ModuleBlockKind.Behavior, "UDMLN"),
        ("JetAIUpdate", ModuleBlockKind.Behavior, "U"),
        ("JetSlowDeathBehavior", ModuleBlockKind.Behavior, "UD"),
        ("KeepObjectDie", ModuleBlockKind.Behavior, "D"),
        ("LaserUpdate", ModuleBlockKind.ClientUpdate, "E"),
        ("LeafletDropBehavior", ModuleBlockKind.Behavior, "U"),
        ("LifetimeUpdate", ModuleBlockKind.Behavior, "U"),
        ("LockWeaponCreate", ModuleBlockKind.Behavior, "C"),
        ("LocomotorSetUpgrade", ModuleBlockKind.Behavior, "G"),
        ("MaxHealthUpgrade", ModuleBlockKind.Behavior, "G"),
        ("MinefieldBehavior", ModuleBlockKind.Behavior, "UDML"),
        ("MissileAIUpdate", ModuleBlockKind.Behavior, "U"),
        ("MissileLauncherBuildingUpdate", ModuleBlockKind.Behavior, "U"),
        ("MobMemberSlavedUpdate", ModuleBlockKind.Behavior, "U"),
        ("ModelConditionUpgrade", ModuleBlockKind.Behavior, "G"),
        ("MoneyCrateCollide", ModuleBlockKind.Behavior, "L"),
        ("NeutronBlastBehavior", ModuleBlockKind.Behavior, "UD"),
        ("NeutronMissileSlowDeathBehavior", ModuleBlockKind.Behavior, "UD"),
        ("NeutronMissileUpdate", ModuleBlockKind.Behavior, "UD"),
        ("OCLSpecialPower", ModuleBlockKind.Behavior, "S"),
        ("OCLUpdate", ModuleBlockKind.Behavior, "U"),
        ("ObjectCreationUpgrade", ModuleBlockKind.Behavior, "G"),
        ("OverchargeBehavior", ModuleBlockKind.Behavior, "UM"),
        ("OverlordContain", ModuleBlockKind.Behavior, "UDMLN"),
        ("ParachuteContain", ModuleBlockKind.Behavior, "UDMLN"),
        ("ParkingPlaceBehavior", ModuleBlockKind.Behavior, "UD"),
        ("ParticleUplinkCannonUpdate", ModuleBlockKind.Behavior, "U"),
        ("PassengersFireUpgrade", ModuleBlockKind.Behavior, "G"),
        ("PhysicsBehavior", ModuleBlockKind.Behavior, "UL"),
        ("PilotFindVehicleUpdate", ModuleBlockKind.Behavior, "U"),
        ("PointDefenseLaserUpdate", ModuleBlockKind.Behavior, "U"),
        ("PoisonedBehavior", ModuleBlockKind.Behavior, "UM"),
        ("PowerPlantUpdate", ModuleBlockKind.Behavior, "U"),
        ("PowerPlantUpgrade", ModuleBlockKind.Behavior, "G"),
        ("PreorderCreate", ModuleBlockKind.Behavior, "C"),
        ("ProductionUpdate", ModuleBlockKind.Behavior, "UD"),
        ("ProjectileStreamUpdate", ModuleBlockKind.Behavior, "U"),
        ("PropagandaTowerBehavior", ModuleBlockKind.Behavior, "UD"),
        ("QueueProductionExitUpdate", ModuleBlockKind.Behavior, "U"),
        ("RadarUpdate", ModuleBlockKind.Behavior, "U"),
        ("RadarUpgrade", ModuleBlockKind.Behavior, "G"),
        ("RadiusDecalUpdate", ModuleBlockKind.Behavior, "U"),
        ("RailedTransportAIUpdate", ModuleBlockKind.Behavior, "U"),
        ("RailedTransportContain", ModuleBlockKind.Behavior, "UDMLN"),
        ("RailedTransportDockUpdate", ModuleBlockKind.Behavior, "U"),
        ("RailroadBehavior", ModuleBlockKind.Behavior, "UL"),
        ("RebuildHoleBehavior", ModuleBlockKind.Behavior, "UD"),
        ("RebuildHoleExposeDie", ModuleBlockKind.Behavior, "D"),
        ("RepairDockUpdate", ModuleBlockKind.Behavior, "U"),
        ("ReplaceObjectUpgrade", ModuleBlockKind.Behavior, "G"),
        ("RiderChangeContain", ModuleBlockKind.Behavior, "UDMLN"),
        ("SabotageCommandCenterCrateCollide", ModuleBlockKind.Behavior, "L"),
        ("SabotageFakeBuildingCrateCollide", ModuleBlockKind.Behavior, "L"),
        ("SabotageInternetCenterCrateCollide", ModuleBlockKind.Behavior, "L"),
        ("SabotageMilitaryFactoryCrateCollide", ModuleBlockKind.Behavior, "L"),
        ("SabotagePowerPlantCrateCollide", ModuleBlockKind.Behavior, "L"),
        ("SabotageSuperweaponCrateCollide", ModuleBlockKind.Behavior, "L"),
        ("SabotageSupplyCenterCrateCollide", ModuleBlockKind.Behavior, "L"),
        ("SalvageCrateCollide", ModuleBlockKind.Behavior, "L"),
        ("SlavedUpdate", ModuleBlockKind.Behavior, "U"),
        ("SlowDeathBehavior", ModuleBlockKind.Behavior, "UD"),
        ("SmartBombTargetHomingUpdate", ModuleBlockKind.Behavior, "U"),
        ("SpawnBehavior", ModuleBlockKind.Behavior, "UDM"),
        ("SpawnPointProductionExitUpdate", ModuleBlockKind.Behavior, "U"),
        ("SpecialAbility", ModuleBlockKind.Behavior, "S"),
        ("SpecialAbilityUpdate", ModuleBlockKind.Behavior, "U"),
        ("SpecialPowerCompletionDie", ModuleBlockKind.Behavior, "D"),
        ("SpecialPowerCreate", ModuleBlockKind.Behavior, "C"),
        ("SpectreGunshipDeploymentUpdate", ModuleBlockKind.Behavior, "U"),
        ("SpectreGunshipUpdate", ModuleBlockKind.Behavior, "U"),
        ("SpyVisionSpecialPower", ModuleBlockKind.Behavior, "S"),
        ("SpyVisionUpdate", ModuleBlockKind.Behavior, "UG"),
        ("SquishCollide", ModuleBlockKind.Behavior, "L"),
        ("StealthDetectorUpdate", ModuleBlockKind.Behavior, "U"),
        ("StealthUpdate", ModuleBlockKind.Behavior, "U"),
        ("StealthUpgrade", ModuleBlockKind.Behavior, "G"),
        ("StickyBombUpdate", ModuleBlockKind.Behavior, "U"),
        ("StructureBody", ModuleBlockKind.Body, "B"),
        ("StructureCollapseUpdate", ModuleBlockKind.Behavior, "UD"),
        ("StructureToppleUpdate", ModuleBlockKind.Behavior, "UD"),
        ("SubObjectsUpgrade", ModuleBlockKind.Behavior, "G"),
        ("SupplyCenterCreate", ModuleBlockKind.Behavior, "C"),
        ("SupplyCenterDockUpdate", ModuleBlockKind.Behavior, "U"),
        ("SupplyCenterProductionExitUpdate", ModuleBlockKind.Behavior, "U"),
        ("SupplyTruckAIUpdate", ModuleBlockKind.Behavior, "U"),
        ("SupplyWarehouseCreate", ModuleBlockKind.Behavior, "C"),
        ("SupplyWarehouseCripplingBehavior", ModuleBlockKind.Behavior, "UM"),
        ("SupplyWarehouseDockUpdate", ModuleBlockKind.Behavior, "U"),
        ("SwayClientUpdate", ModuleBlockKind.ClientUpdate, "E"),
        ("TechBuildingBehavior", ModuleBlockKind.Behavior, "UD"),
        ("TensileFormationUpdate", ModuleBlockKind.Behavior, "U"),
        ("ToppleUpdate", ModuleBlockKind.Behavior, "UL"),
        ("TransitionDamageFX", ModuleBlockKind.Behavior, "M"),
        ("TransportAIUpdate", ModuleBlockKind.Behavior, "U"),
        ("TransportContain", ModuleBlockKind.Behavior, "UDMLN"),
        ("TunnelContain", ModuleBlockKind.Behavior, "UDMCLN"),
        ("UndeadBody", ModuleBlockKind.Body, "B"),
        ("UnitCrateCollide", ModuleBlockKind.Behavior, "L"),
        ("UnpauseSpecialPowerUpgrade", ModuleBlockKind.Behavior, "G"),
        ("UpgradeDie", ModuleBlockKind.Behavior, "D"),
        ("VeterancyCrateCollide", ModuleBlockKind.Behavior, "L"),
        ("VeterancyGainCreate", ModuleBlockKind.Behavior, "C"),
        ("W3DDebrisDraw", ModuleBlockKind.Draw, "W"),
        ("W3DDefaultDraw", ModuleBlockKind.Draw, "W"),
        ("W3DDependencyModelDraw", ModuleBlockKind.Draw, "W"),
        ("W3DLaserDraw", ModuleBlockKind.Draw, "W"),
        ("W3DModelDraw", ModuleBlockKind.Draw, "W"),
        ("W3DOverlordAircraftDraw", ModuleBlockKind.Draw, "W"),
        ("W3DOverlordTankDraw", ModuleBlockKind.Draw, "W"),
        ("W3DOverlordTruckDraw", ModuleBlockKind.Draw, "W"),
        ("W3DPoliceCarDraw", ModuleBlockKind.Draw, "W"),
        ("W3DProjectileStreamDraw", ModuleBlockKind.Draw, "W"),
        ("W3DRopeDraw", ModuleBlockKind.Draw, "W"),
        ("W3DScienceModelDraw", ModuleBlockKind.Draw, "W"),
        ("W3DSupplyDraw", ModuleBlockKind.Draw, "W"),
        ("W3DTankDraw", ModuleBlockKind.Draw, "W"),
        ("W3DTankTruckDraw", ModuleBlockKind.Draw, "W"),
        ("W3DTracerDraw", ModuleBlockKind.Draw, "W"),
        ("W3DTreeDraw", ModuleBlockKind.Draw, "W"),
        ("W3DTruckDraw", ModuleBlockKind.Draw, "W"),
        ("WanderAIUpdate", ModuleBlockKind.Behavior, "U"),
        ("WaveGuideUpdate", ModuleBlockKind.Behavior, "U"),
        ("WeaponBonusUpdate", ModuleBlockKind.Behavior, "U"),
        ("WeaponBonusUpgrade", ModuleBlockKind.Behavior, "G"),
        ("WeaponSetUpgrade", ModuleBlockKind.Behavior, "G"),
        ("WorkerAIUpdate", ModuleBlockKind.Behavior, "U"),
    ];

    public static ModuleRegistry Registry { get; } = new(Build());
}
