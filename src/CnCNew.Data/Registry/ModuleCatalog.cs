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
    private sealed record ImplementedModuleSpec(ImmutableArray<FieldSchema> Schema, bool AllowsNestedSections = false);

    private static IEnumerable<ModuleDescriptor> Build()
    {
        var implemented = ImplementedSchemas();
        foreach (var (name, block, codes) in Rows)
        {
            var interfaces = ParseCodes(codes);
            var category = block is ModuleBlockKind.Draw or ModuleBlockKind.ClientUpdate ? ModuleCategory.Presentation : ModuleCategory.Simulation;
            var owners = ImmutableHashSet.Create(ModuleOwnerKind.Object);
            if (implemented.TryGetValue(name, out var spec))
                yield return new ModuleDescriptor(name, block, interfaces, owners, spec.Schema, false, null, category, spec.AllowsNestedSections);
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
            || name is "ToppleUpdate" or "StructureToppleUpdate" or "StructureCollapseUpdate" or "FloatUpdate"
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

    // Strict schemas of the implemented simulation modules. Field lists come from the reference FieldParse tables
    // (ActiveBody.cpp, DieModule.cpp, LifetimeUpdate.h, DeletionUpdate.h, FXListDie.cpp, StealthUpdate.cpp/.h,
    // AutoHealBehavior.h, Module.cpp UpgradeMuxData, the eleven *Upgrade.cpp modules, ProductionUpdate.h) and were
    // verified against every occurrence in the shipped INIs (see docs/SIMULATION-CORE.md and docs/GAME-RULES.md).
    private static ImmutableDictionary<string, ImplementedModuleSpec> ImplementedSchemas()
    {
        FieldSchema Real(string name, string source) => new(name, FieldType.Real, Source: source);
        ImmutableArray<FieldSchema> Body() =>
        [
            Real("MaxHealth", "ActiveBody.cpp"), Real("InitialHealth", "ActiveBody.cpp"), Real("SubdualDamageCap", "ActiveBody.cpp"),
            new("SubdualDamageHealRate", FieldType.DurationMs, Source: "ActiveBody.cpp"), Real("SubdualDamageHealAmount", "ActiveBody.cpp"),
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
        // Module.cpp UpgradeMuxData: shared by AutoHealBehavior and every *Upgrade module. FXListUpgrade (presentation FX) is Opaque.
        ImmutableArray<FieldSchema> UpgradeMux() =>
        [
            new("TriggeredBy", FieldType.ReferenceList, ReferenceKind: DefinitionKind.Upgrade, Source: "Module.cpp UpgradeMuxData"),
            new("ConflictsWith", FieldType.ReferenceList, ReferenceKind: DefinitionKind.Upgrade, Source: "Module.cpp UpgradeMuxData"),
            new("RemovesUpgrades", FieldType.ReferenceList, ReferenceKind: DefinitionKind.Upgrade, Source: "Module.cpp UpgradeMuxData"),
            new("RequiresAllTriggers", FieldType.Bool, Source: "Module.cpp UpgradeMuxData"),
            new("FXListUpgrade", FieldType.Opaque, Source: "Module.cpp UpgradeMuxData"),
        ];
        var builder = ImmutableDictionary.CreateBuilder<string, ImplementedModuleSpec>(StringComparer.Ordinal);
        void Add(string name, ImmutableArray<FieldSchema> schema, bool allowsNestedSections = false) => builder[name] = new ImplementedModuleSpec(schema, allowsNestedSections);

        Add("ActiveBody", Body());
        Add("StructureBody", Body());
        Add("ImmortalBody", Body());
        Add("HighlanderBody", Body());
        Add("InactiveBody", []);
        Add("LifetimeUpdate", Lifetime());
        Add("DeletionUpdate", Lifetime());
        Add("DestroyDie", DieMux());
        Add("KeepObjectDie", DieMux());
        Add("CreateObjectDie",
        [
            ..DieMux(),
            new("CreationList", FieldType.Reference, ReferenceKind: DefinitionKind.ObjectCreationList, Source: "CreateObjectDie.cpp"),
            new("TransferPreviousHealth", FieldType.Bool, Source: "CreateObjectDie.cpp"),
        ]);
        Add("CreateCrateDie",
        [
            ..DieMux(),
            new("CrateData", FieldType.ReferenceList, Repeat: RepeatPolicy.Accumulate, ReferenceKind: DefinitionKind.Crate, Source: "CreateCrateDie.cpp"),
        ]);
        Add("InstantDeathBehavior",
        [
            ..DieMux(),
            new("FX", FieldType.ReferenceList, ReferenceKind: DefinitionKind.FxList, Source: "InstantDeathBehavior.cpp"),
            new("OCL", FieldType.Reference, ReferenceKind: DefinitionKind.ObjectCreationList, Source: "InstantDeathBehavior.cpp"),
            new("Weapon", FieldType.ReferenceList, ReferenceKind: DefinitionKind.Weapon, Source: "InstantDeathBehavior.cpp"),
        ]);
        Add("SpawnBehavior",
        [
            ..DieMux(),
            new("SpawnNumber", FieldType.Integer, Min: 0, Source: "SpawnBehavior.cpp"),
            new("SpawnReplaceDelay", FieldType.DurationMs, Source: "SpawnBehavior.cpp"),
            new("OneShot", FieldType.Bool, Source: "SpawnBehavior.cpp"),
            new("CanReclaimOrphans", FieldType.Bool, Source: "SpawnBehavior.cpp"),
            new("AggregateHealth", FieldType.Bool, Source: "SpawnBehavior.cpp"),
            new("ExitByBudding", FieldType.Bool, Source: "SpawnBehavior.cpp"),
            new("SpawnTemplateName", FieldType.ReferenceList, Repeat: RepeatPolicy.Accumulate, ReferenceKind: DefinitionKind.Object, Source: "SpawnBehavior.cpp"),
            new("SpawnedRequireSpawner", FieldType.Bool, Source: "SpawnBehavior.cpp"),
            new("PropagateDamageTypesToSlavesWhenExisting", FieldType.AllNoneDeltaFlags, Table: NameTables.DamageType, Source: "SpawnBehavior.cpp"),
            new("InitialBurst", FieldType.Integer, Min: 0, Source: "SpawnBehavior.cpp"),
            new("SlavesHaveFreeWill", FieldType.Bool, Source: "SpawnBehavior.cpp"),
        ]);
        Add("FXListDie",
        [
            ..DieMux(),
            new("DeathFX", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "FXListDie.cpp"),
            new("OrientToObject", FieldType.Bool, Source: "FXListDie.cpp"),
            new("StartsActive", FieldType.Bool, Source: "UpgradeMux"),
            new("TriggeredBy", FieldType.ReferenceList, ReferenceKind: DefinitionKind.Upgrade, Source: "UpgradeMux"),
            new("ConflictsWith", FieldType.ReferenceList, ReferenceKind: DefinitionKind.Upgrade, Source: "UpgradeMux"),
        ]);

        // ---------------------------------------------------------------------------------------------- phase 5

        Add("StealthUpdate",
        [
            new("StealthDelay", FieldType.DurationMs, Source: "StealthUpdate.cpp"),
            new("MoveThresholdSpeed", FieldType.Real, Source: "StealthUpdate.cpp"),
            new("StealthForbiddenConditions", FieldType.Flags, Table: NameTablesPhase5.StealthForbiddenCondition, Source: "StealthUpdate.h TheStealthLevelNames"),
            new("HintDetectableConditions", FieldType.Flags, Table: NameTables.ObjectStatus, Source: "StealthUpdate.cpp"),
            new("RequiredStatus", FieldType.Flags, Table: NameTables.ObjectStatus, Source: "StealthUpdate.cpp"),
            new("ForbiddenStatus", FieldType.Flags, Table: NameTables.ObjectStatus, Source: "StealthUpdate.cpp"),
            new("FriendlyOpacityMin", FieldType.Percent, Source: "StealthUpdate.cpp"),
            new("FriendlyOpacityMax", FieldType.Percent, Source: "StealthUpdate.cpp"),
            new("PulseFrequency", FieldType.DurationMs, Source: "StealthUpdate.cpp"),
            new("DisguisesAsTeam", FieldType.Bool, Source: "StealthUpdate.cpp"),
            new("RevealDistanceFromTarget", FieldType.Real, Source: "StealthUpdate.cpp"),
            new("OrderIdleEnemiesToAttackMeUponReveal", FieldType.Bool, Source: "StealthUpdate.cpp"),
            new("DisguiseFX", FieldType.Opaque, Source: "StealthUpdate.cpp"),
            new("DisguiseRevealFX", FieldType.Opaque, Source: "StealthUpdate.cpp"),
            new("DisguiseTransitionTime", FieldType.DurationMs, Source: "StealthUpdate.cpp"),
            new("DisguiseRevealTransitionTime", FieldType.DurationMs, Source: "StealthUpdate.cpp"),
            new("InnateStealth", FieldType.Bool, Source: "StealthUpdate.cpp"),
            new("UseRiderStealth", FieldType.Bool, Source: "StealthUpdate.cpp"),
            new("EnemyDetectionEvaEvent", FieldType.Opaque, Source: "StealthUpdate.cpp"),
            new("OwnDetectionEvaEvent", FieldType.Opaque, Source: "StealthUpdate.cpp"),
            new("BlackMarketCheckDelay", FieldType.DurationMs, Source: "StealthUpdate.cpp"),
            new("GrantedBySpecialPower", FieldType.Bool, Source: "StealthUpdate.cpp"),
        ]);
        Add("AutoHealBehavior",
        [
            ..UpgradeMux(),
            new("StartsActive", FieldType.Bool, Source: "AutoHealBehavior.h"),
            new("SingleBurst", FieldType.Bool, Source: "AutoHealBehavior.h"),
            new("HealingAmount", FieldType.Integer, Source: "AutoHealBehavior.h"),
            new("HealingDelay", FieldType.DurationMs, Source: "AutoHealBehavior.h"),
            new("Radius", FieldType.Distance, Source: "AutoHealBehavior.h"),
            new("KindOf", FieldType.Flags, Table: NameTables.KindOf, Source: "AutoHealBehavior.h"),
            new("ForbiddenKindOf", FieldType.Flags, Table: NameTables.KindOf, Source: "AutoHealBehavior.h"),
            new("RadiusParticleSystemName", FieldType.Opaque, Source: "AutoHealBehavior.h"),
            new("UnitHealPulseParticleSystemName", FieldType.Opaque, Source: "AutoHealBehavior.h"),
            new("StartHealingDelay", FieldType.DurationMs, Source: "AutoHealBehavior.h"),
            new("AffectsWholePlayer", FieldType.Bool, Source: "AutoHealBehavior.h"),
            new("SkipSelfForHealing", FieldType.Bool, Source: "AutoHealBehavior.h"),
        ]);
        Add("ArmorUpgrade", UpgradeMux());
        Add("WeaponSetUpgrade", UpgradeMux());
        Add("WeaponBonusUpgrade", UpgradeMux());
        Add("LocomotorSetUpgrade", UpgradeMux());
        Add("StealthUpgrade", UpgradeMux());
        Add("MaxHealthUpgrade",
        [
            ..UpgradeMux(),
            new("AddMaxHealth", FieldType.Real, Source: "MaxHealthUpgrade.cpp"),
            new("ChangeType", FieldType.Enum, Table: NameTablesPhase5.MaxHealthChangeType, Source: "MaxHealthUpgrade.cpp"),
        ]);
        Add("RadarUpgrade",
        [
            ..UpgradeMux(),
            new("DisableProof", FieldType.Bool, Source: "RadarUpgrade.cpp"),
        ]);
        // CommandSetUpgrade.cpp: all three fields are INI::parseAsciiString (unresolved names looked up dynamically at run
        // time, not INI-time references), so Text is the faithful typing, not Reference.
        Add("CommandSetUpgrade",
        [
            ..UpgradeMux(),
            new("CommandSet", FieldType.Text, Source: "CommandSetUpgrade.cpp"),
            new("CommandSetAlt", FieldType.Text, Source: "CommandSetUpgrade.cpp"),
            new("TriggerAlt", FieldType.Text, Source: "CommandSetUpgrade.cpp"),
        ]);
        Add("ExperienceScalarUpgrade",
        [
            ..UpgradeMux(),
            new("AddXPScalar", FieldType.Real, Source: "ExperienceScalarUpgrade.cpp"),
        ]);
        // ModelConditionFlags::parseSingleBitFromINI needs a full ModelCondition name table (a presentation concept, not yet
        // typed); the flag itself has no simulation consumer in phase 5, so it stays Opaque (known, counted, not mechanical).
        Add("ModelConditionUpgrade",
        [
            ..UpgradeMux(),
            new("ConditionFlag", FieldType.Opaque, Source: "ModelConditionUpgrade.cpp"),
        ]);
        Add("ObjectCreationUpgrade",
        [
            ..UpgradeMux(),
            new("UpgradeObject", FieldType.Reference, ReferenceKind: DefinitionKind.ObjectCreationList, Source: "ObjectCreationUpgrade.cpp"),
        ]);
        // Field list verified against every occurrence in .local/phase2-import-a (510 AIUpdateInterface blocks, 34 files):
        // exactly these four top-level fields occur; AIUpdate.cpp's full FieldParse table is much larger (idle-scan timing,
        // mood, etc.) but nothing else appears in the shipped data, so nothing else is typed (avoids guessing spellings for
        // fields that cannot be checked against real usage). Turret/AltTurret are nested blocks (167/4 occurrences: aim,
        // traverse and weapon-slot control) compiled with TurretSchema (every key of TurretAI.cpp's table typed, unknown keys are
        // errors) and read by the simulation's turret model (docs/SIM-TURRET-AIM.md) — hence AllowsNestedSections.
        Add("AIUpdateInterface",
        [
            new("AutoAcquireEnemiesWhenIdle", FieldType.Bool, Source: "AIUpdate.cpp"),
            new("MoodAttackCheckRate", FieldType.DurationMs, Source: "AIUpdate.cpp"),
            new("ForbidPlayerCommands", FieldType.Bool, Source: "AIUpdate.cpp"),
            new("TurretsLinked", FieldType.Bool, Source: "AIUpdate.cpp"),
        ], allowsNestedSections: true);
        // ------------------------------------------------------------------------- supply economy (docks, gatherers, deposits)
        // Field lists verified against every occurrence in .local/phase2-import-a (docs/GAME-RULES.md 6e). The AI-derived modules
        // carry the AIUpdateInterface base fields (AIUpdate.cpp) plus their own table.
        ImmutableArray<FieldSchema> AiBase(string source) =>
        [
            new("AutoAcquireEnemiesWhenIdle", FieldType.Bool, Source: source),
            new("MoodAttackCheckRate", FieldType.DurationMs, Source: source),
            new("ForbidPlayerCommands", FieldType.Bool, Source: source),
            new("TurretsLinked", FieldType.Bool, Source: source),
        ];
        ImmutableArray<FieldSchema> DozerFields(string source) =>
        [
            new("RepairHealthPercentPerSecond", FieldType.Percent, Source: source),
            new("BoredTime", FieldType.DurationMs, Source: source),
            new("BoredRange", FieldType.Distance, Source: source),
        ];
        ImmutableArray<FieldSchema> TruckFields(string source) =>
        [
            new("MaxBoxes", FieldType.Integer, Min: 0, Source: source),
            new("SupplyCenterActionDelay", FieldType.DurationMs, Source: source),
            new("SupplyWarehouseActionDelay", FieldType.DurationMs, Source: source),
            new("SupplyWarehouseScanDistance", FieldType.Distance, Source: source),
            new("SuppliesDepletedVoice", FieldType.Opaque, Source: source),
        ];
        Add("SupplyTruckAIUpdate", [..AiBase("SupplyTruckAIUpdate.cpp"), ..TruckFields("SupplyTruckAIUpdate.cpp")], allowsNestedSections: true);
        Add("WorkerAIUpdate",
        [
            ..AiBase("WorkerAIUpdate.cpp"), ..DozerFields("WorkerAIUpdate.cpp"), ..TruckFields("WorkerAIUpdate.cpp"),
            new("UpgradedSupplyBoost", FieldType.Integer, Source: "WorkerAIUpdate.cpp"),
        ], allowsNestedSections: true);
        Add("DozerAIUpdate", [..AiBase("DozerAIUpdate.cpp"), ..DozerFields("DozerAIUpdate.cpp")], allowsNestedSections: true);
        ImmutableArray<FieldSchema> DockBase(string source) =>
        [
            new("NumberApproachPositions", FieldType.Integer, Min: -1, Source: source),
            new("AllowsPassthrough", FieldType.Bool, Source: source),
        ];
        Add("SupplyCenterDockUpdate", [..DockBase("DockUpdate.cpp"), new("GrantTemporaryStealth", FieldType.DurationMs, Source: "SupplyCenterDockUpdate.cpp")]);
        Add("SupplyWarehouseDockUpdate",
        [
            ..DockBase("DockUpdate.cpp"),
            new("StartingBoxes", FieldType.Integer, Min: 0, Source: "SupplyWarehouseDockUpdate.cpp"),
            new("DeleteWhenEmpty", FieldType.Bool, Source: "SupplyWarehouseDockUpdate.cpp"),
        ]);
        Add("RepairDockUpdate", [..DockBase("DockUpdate.cpp"), new("TimeForFullHeal", FieldType.DurationMs, Source: "RepairDockUpdate.cpp")]);
        Add("AutoFindHealingUpdate",
        [
            new("ScanRate", FieldType.DurationMs, Source: "AutoFindHealingUpdate.cpp"),
            new("ScanRange", FieldType.Distance, Source: "AutoFindHealingUpdate.cpp"),
            new("NeverHeal", FieldType.Real, Source: "AutoFindHealingUpdate.cpp"),
            new("AlwaysHeal", FieldType.Real, Source: "AutoFindHealingUpdate.cpp"),
        ]);
        Add("FireWeaponUpdate",
        [
            new("Weapon", FieldType.Reference, ReferenceKind: DefinitionKind.Weapon, Source: "FireWeaponUpdate.cpp"),
            new("InitialDelay", FieldType.DurationMs, Source: "FireWeaponUpdate.cpp"),
            new("ExclusiveWeaponDelay", FieldType.DurationMs, Source: "FireWeaponUpdate.cpp"),
        ]);
        Add("ReplaceObjectUpgrade",
        [
            ..UpgradeMux(),
            new("ReplaceObject", FieldType.Reference, ReferenceKind: DefinitionKind.Object, Source: "ReplaceObjectUpgrade.cpp"),
        ]);
        // CrushDie.h buildFieldParse; the sounds are presentation (Opaque), the percentages are parseInt.
        Add("CrushDie",
        [
            ..DieMux(),
            new("TotalCrushSound", FieldType.Opaque, Source: "CrushDie.h"),
            new("BackEndCrushSound", FieldType.Opaque, Source: "CrushDie.h"),
            new("FrontEndCrushSound", FieldType.Opaque, Source: "CrushDie.h"),
            new("TotalCrushSoundPercent", FieldType.Integer, Source: "CrushDie.h"),
            new("BackEndCrushSoundPercent", FieldType.Integer, Source: "CrushDie.h"),
            new("FrontEndCrushSoundPercent", FieldType.Integer, Source: "CrushDie.h"),
        ]);
        Add("SupplyCenterCreate", []);
        Add("SupplyWarehouseCreate", []);
        // UpgradedBoost is "UpgradeType:<name> Boost:<int>", repeatable (parseUpgradePair): a token pair the generic typer cannot read, so
        // it stays a counted deferred-field entry that the binding parses from the original text.
        Add("AutoDepositUpdate",
        [
            new("DepositTiming", FieldType.DurationMs, Source: "AutoDepositUpdate.cpp"),
            new("DepositAmount", FieldType.Integer, Source: "AutoDepositUpdate.cpp"),
            new("InitialCaptureBonus", FieldType.Integer, Source: "AutoDepositUpdate.cpp"),
            new("ActualMoney", FieldType.Bool, Source: "AutoDepositUpdate.cpp"),
            new("UpgradedBoost", FieldType.Opaque, Repeat: RepeatPolicy.Accumulate, Source: "AutoDepositUpdate.cpp"),
        ]);
        Add("ProductionUpdate",
        [
            new("MaxQueueEntries", FieldType.Integer, Min: 0, Source: "ProductionUpdate.h"),
            // parseAppendQuantityModifier: "<TemplateName> <Count=1>", push_back'd (repeatable) — not the plain integer it looks like.
            new("QuantityModifier", FieldType.Opaque, Repeat: RepeatPolicy.Accumulate, Source: "ProductionUpdate.h"),
            new("NumDoorAnimations", FieldType.Opaque, Source: "ProductionUpdate.h"),
            new("DoorOpeningTime", FieldType.Opaque, Source: "ProductionUpdate.h"),
            new("DoorWaitOpenTime", FieldType.Opaque, Source: "ProductionUpdate.h"),
            new("DoorCloseTime", FieldType.Opaque, Source: "ProductionUpdate.h"),
            new("ConstructionCompleteDuration", FieldType.Opaque, Source: "ProductionUpdate.h"),
            new("DisabledTypesToProcess", FieldType.Opaque, Source: "ProductionUpdate.h"),
        ]);

        // ------------------------------------------------------------------------------------------ next blockers
        Add("FlammableUpdate",
        [
            new("BurnedDelay", FieldType.DurationMs, Source: "FlammableUpdate.cpp"),
            new("AflameDuration", FieldType.DurationMs, Source: "FlammableUpdate.cpp"),
            new("AflameDamageDelay", FieldType.DurationMs, Source: "FlammableUpdate.cpp"),
            new("AflameDamageAmount", FieldType.Integer, Source: "FlammableUpdate.cpp"),
            new("BurningSoundName", FieldType.Opaque, Source: "FlammableUpdate.cpp"),
            new("FlameDamageLimit", FieldType.Real, Source: "FlammableUpdate.cpp"),
            new("FlameDamageExpiration", FieldType.DurationMs, Source: "FlammableUpdate.cpp"),
        ]);
        {
            // TransitionDamageFX.h has 111 keys: three DamageTypeFlags fields plus 12 slots x 3 body-damage-states x
            // 3 effect kinds (FX list, object creation list, particle system). Only DamageOCLTypes and the 36 OCL name
            // slots are mechanically relevant (see the simulation-layer doc comment); FX lists and particle systems —
            // pure presentation — are still typed (Opaque, an implemented module may not have unlisted keys) but
            // deferred.
            ImmutableArray<FieldSchema> Slots(string prefix, FieldType type, DefinitionKind? referenceKind = null)
            {
                var slots = ImmutableArray.CreateBuilder<FieldSchema>();
                for (var i = 1; i <= 12; i++) slots.Add(new FieldSchema($"{prefix}{i}", type, ReferenceKind: referenceKind, Source: "TransitionDamageFX.h"));
                return slots.ToImmutable();
            }
            Add("TransitionDamageFX",
            [
                new("DamageFXTypes", FieldType.Opaque, Source: "TransitionDamageFX.h"),
                new("DamageOCLTypes", FieldType.AllNoneDeltaFlags, Table: NameTables.DamageType, Source: "TransitionDamageFX.h"),
                new("DamageParticleTypes", FieldType.Opaque, Source: "TransitionDamageFX.h"),
                ..Slots("DamagedFXList", FieldType.Opaque), ..Slots("ReallyDamagedFXList", FieldType.Opaque), ..Slots("RubbleFXList", FieldType.Opaque),
                ..Slots("DamagedOCL", FieldType.Reference, DefinitionKind.ObjectCreationList), ..Slots("ReallyDamagedOCL", FieldType.Reference, DefinitionKind.ObjectCreationList), ..Slots("RubbleOCL", FieldType.Reference, DefinitionKind.ObjectCreationList),
                ..Slots("DamagedParticleSystem", FieldType.Opaque), ..Slots("ReallyDamagedParticleSystem", FieldType.Opaque), ..Slots("RubbleParticleSystem", FieldType.Opaque),
            ]);
        }
        Add("SlowDeathBehavior",
        [
            ..DieMux(),
            new("SinkRate", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("ProbabilityModifier", FieldType.Integer, Source: "SlowDeathBehavior.cpp"),
            // Needs the actual damage dealt at the killing blow, not yet threaded through OnDie (see the simulation-layer doc comment).
            new("ModifierBonusPerOverkillPercent", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("SinkDelay", FieldType.DurationMs, Source: "SlowDeathBehavior.cpp"),
            new("SinkDelayVariance", FieldType.DurationMs, Source: "SlowDeathBehavior.cpp"),
            new("DestructionDelay", FieldType.DurationMs, Source: "SlowDeathBehavior.cpp"),
            new("DestructionDelayVariance", FieldType.DurationMs, Source: "SlowDeathBehavior.cpp"),
            new("DestructionAltitude", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("FX", FieldType.Opaque, Repeat: RepeatPolicy.Accumulate, Source: "SlowDeathBehavior.cpp"),
            new("OCL", FieldType.Opaque, Repeat: RepeatPolicy.Accumulate, Source: "SlowDeathBehavior.cpp"),
            new("Weapon", FieldType.Opaque, Repeat: RepeatPolicy.Accumulate, Source: "SlowDeathBehavior.cpp"),
            new("FlingForce", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("FlingForceVariance", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("FlingPitch", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("FlingPitchVariance", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
        ]);
        Add("FireWeaponWhenDeadBehavior",
        [
            ..DieMux(),
            ..UpgradeMux(),
            new("StartsActive", FieldType.Bool, Source: "FireWeaponWhenDeadBehavior.h"),
            new("DeathWeapon", FieldType.Reference, ReferenceKind: DefinitionKind.Weapon, Source: "FireWeaponWhenDeadBehavior.h"),
        ]);
        Add("StealthDetectorUpdate",
        [
            new("DetectionRate", FieldType.DurationMs, Source: "StealthDetectorUpdate.cpp"),
            new("DetectionRange", FieldType.Distance, Source: "StealthDetectorUpdate.cpp"),
            new("InitiallyDisabled", FieldType.Bool, Source: "StealthDetectorUpdate.cpp"),
            new("PingSound", FieldType.Opaque, Source: "StealthDetectorUpdate.cpp"),
            new("LoudPingSound", FieldType.Opaque, Source: "StealthDetectorUpdate.cpp"),
            new("IRBeaconParticleSysName", FieldType.Opaque, Source: "StealthDetectorUpdate.cpp"),
            new("IRParticleSysName", FieldType.Opaque, Source: "StealthDetectorUpdate.cpp"),
            new("IRBrightParticleSysName", FieldType.Opaque, Source: "StealthDetectorUpdate.cpp"),
            new("IRGridParticleSysName", FieldType.Opaque, Source: "StealthDetectorUpdate.cpp"),
            new("IRParticleSysBone", FieldType.Opaque, Source: "StealthDetectorUpdate.cpp"),
            new("ExtraRequiredKindOf", FieldType.Flags, Table: NameTables.KindOf, Source: "StealthDetectorUpdate.cpp"),
            new("ExtraForbiddenKindOf", FieldType.Flags, Table: NameTables.KindOf, Source: "StealthDetectorUpdate.cpp"),
            // No Contain system yet (garrisons/transports): containedBy is always null, so these are typed but currently inert.
            new("CanDetectWhileGarrisoned", FieldType.Bool, Source: "StealthDetectorUpdate.cpp"),
            new("CanDetectWhileContained", FieldType.Bool, Source: "StealthDetectorUpdate.cpp"),
        ]);
        Add("BaseRegenerateUpdate", []);
        // PhysicsUpdate.cpp PhysicsBehaviorModuleData::buildFieldParse. The frictions are per-second values in the INI (parseFrictionPerSec).
        Add("PhysicsBehavior",
        [
            Real("Mass", "PhysicsUpdate.cpp"), Real("ShockResistance", "PhysicsUpdate.cpp"), Real("ShockMaxYaw", "PhysicsUpdate.cpp"),
            Real("ShockMaxPitch", "PhysicsUpdate.cpp"), Real("ShockMaxRoll", "PhysicsUpdate.cpp"),
            Real("ForwardFriction", "PhysicsUpdate.cpp"), Real("LateralFriction", "PhysicsUpdate.cpp"), Real("ZFriction", "PhysicsUpdate.cpp"),
            Real("AerodynamicFriction", "PhysicsUpdate.cpp"), Real("CenterOfMassOffset", "PhysicsUpdate.cpp"),
            new("AllowBouncing", FieldType.Bool, Source: "PhysicsUpdate.cpp"),
            new("AllowCollideForce", FieldType.Bool, Source: "PhysicsUpdate.cpp"),
            new("KillWhenRestingOnGround", FieldType.Bool, Source: "PhysicsUpdate.cpp"),
            Real("MinFallHeightForDamage", "PhysicsUpdate.cpp"), Real("FallHeightDamageFactor", "PhysicsUpdate.cpp"), Real("PitchRollYawFactor", "PhysicsUpdate.cpp"),
            new("VehicleCrashesIntoBuildingWeaponTemplate", FieldType.Reference, ReferenceKind: DefinitionKind.Weapon, Source: "PhysicsUpdate.cpp"),
            new("VehicleCrashesIntoNonBuildingWeaponTemplate", FieldType.Reference, ReferenceKind: DefinitionKind.Weapon, Source: "PhysicsUpdate.cpp"),
        ]);
        Add("SquishCollide", []);
        // GenerateMinefieldBehavior.cpp buildFieldParse + the UpgradeMuxData; the die filter is not part of it (a plain BehaviorModuleData).
        Add("GenerateMinefieldBehavior",
        [
            ..UpgradeMux(),
            new("MineName", FieldType.Reference, ReferenceKind: DefinitionKind.Object, Source: "GenerateMinefieldBehavior.cpp"),
            new("UpgradedMineName", FieldType.Reference, ReferenceKind: DefinitionKind.Object, Source: "GenerateMinefieldBehavior.cpp"),
            new("UpgradedTriggeredBy", FieldType.Reference, ReferenceKind: DefinitionKind.Upgrade, Source: "GenerateMinefieldBehavior.cpp"),
            new("GenerationFX", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "GenerateMinefieldBehavior.cpp"),
            new("DistanceAroundObject", FieldType.Distance, Source: "GenerateMinefieldBehavior.cpp"),
            Real("MinesPerSquareFoot", "GenerateMinefieldBehavior.cpp"),
            new("GenerateOnlyOnDeath", FieldType.Bool, Source: "GenerateMinefieldBehavior.cpp"),
            new("BorderOnly", FieldType.Bool, Source: "GenerateMinefieldBehavior.cpp"),
            new("SmartBorder", FieldType.Bool, Source: "GenerateMinefieldBehavior.cpp"),
            new("SmartBorderSkipInterior", FieldType.Bool, Source: "GenerateMinefieldBehavior.cpp"),
            new("AlwaysCircular", FieldType.Bool, Source: "GenerateMinefieldBehavior.cpp"),
            new("Upgradable", FieldType.Bool, Source: "GenerateMinefieldBehavior.cpp"),
            new("RandomJitter", FieldType.Percent, Source: "GenerateMinefieldBehavior.cpp"),
            new("SkipIfThisMuchUnderStructure", FieldType.Percent, Source: "GenerateMinefieldBehavior.cpp"),
        ]);
        // ------------------------------------------------------------------------- retail match, batch 2
        // The production exits (Default/SupplyCenter/QueueProductionExitUpdate.cpp). Rally points and stealth only steer the produced unit's AI or
        // stealth (phase 6), so they are typed and have no consumer.
        ImmutableArray<FieldSchema> ProductionExit(string source) =>
        [
            new("UnitCreatePoint", FieldType.Vector3, Source: source),
            new("NaturalRallyPoint", FieldType.Vector3, Source: source),
        ];
        Add("DefaultProductionExitUpdate", [..ProductionExit("DefaultProductionExitUpdate.cpp"), new("UseSpawnRallyPoint", FieldType.Bool, Source: "DefaultProductionExitUpdate.cpp")]);
        Add("SupplyCenterProductionExitUpdate", [..ProductionExit("SupplyCenterProductionExitUpdate.cpp"), new("GrantTemporaryStealth", FieldType.DurationMs, Source: "SupplyCenterProductionExitUpdate.cpp")]);
        Add("QueueProductionExitUpdate",
        [
            ..ProductionExit("QueueProductionExitUpdate.cpp"),
            new("ExitDelay", FieldType.DurationMs, Source: "QueueProductionExitUpdate.cpp"),
            new("InitialBurst", FieldType.Integer, Source: "QueueProductionExitUpdate.cpp"),
        ]);
        // HealContainModuleData = OpenContainModuleData (see GarrisonContain for the Opaque rationale) + TimeForFullHeal.
        Add("HealContain",
        [
            ..DieMux(),
            new("ContainMax", FieldType.Integer, Source: "OpenContain.cpp"),
            new("EnterSound", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("ExitSound", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("DamagePercentToUnits", FieldType.Percent, Source: "OpenContain.cpp"),
            new("BurnedDeathToUnits", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("AllowInsideKindOf", FieldType.Flags, Table: NameTables.KindOf, Source: "OpenContain.cpp"),
            new("ForbidInsideKindOf", FieldType.Flags, Table: NameTables.KindOf, Source: "OpenContain.cpp"),
            new("PassengersAllowedToFire", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("PassengersInTurret", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("NumberOfExitPaths", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("DoorOpenTime", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("WeaponBonusPassedToPassengers", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("AllowAlliesInside", FieldType.Bool, Source: "OpenContain.cpp"),
            new("AllowEnemiesInside", FieldType.Bool, Source: "OpenContain.cpp"),
            new("AllowNeutralInside", FieldType.Bool, Source: "OpenContain.cpp"),
            new("TimeForFullHeal", FieldType.DurationMs, Source: "HealContain.cpp"),
        ]);
        Add("EjectPilotDie",
        [
            ..DieMux(),
            new("GroundCreationList", FieldType.Reference, ReferenceKind: DefinitionKind.ObjectCreationList, Source: "EjectPilotDie.cpp"),
            new("AirCreationList", FieldType.Reference, ReferenceKind: DefinitionKind.ObjectCreationList, Source: "EjectPilotDie.cpp"),
            new("InvulnerableTime", FieldType.DurationMs, Source: "EjectPilotDie.cpp"),
        ]);
        Add("VeterancyGainCreate",
        [
            new("StartingLevel", FieldType.Enum, Table: NameTables.Veterancy, Source: "VeterancyGainCreate.cpp"),
            new("ScienceRequired", FieldType.Reference, ReferenceKind: DefinitionKind.Science, Source: "VeterancyGainCreate.cpp"),
        ]);
        Add("PreorderCreate", []);
        Add("CommandButtonHuntUpdate",
        [
            new("ScanRate", FieldType.DurationMs, Source: "CommandButtonHuntUpdate.cpp"),
            Real("ScanRange", "CommandButtonHuntUpdate.cpp"),
        ]);
        Add("LockWeaponCreate", [new("SlotToLock", FieldType.Enum, Table: NameTablesPhase5.WeaponSlot, Source: "LockWeaponCreate.cpp")]);
        // Phase 12: modules the campaign missions need to create their named objects (docs/SCRIPT-ENGINE.md §9).
        Add("RadiusDecalUpdate", []);
        Add("TechBuildingBehavior",
        [
            new("PulseFX", FieldType.Opaque, Source: "TechBuildingBehavior.cpp"),
            new("PulseFXRate", FieldType.DurationMs, Source: "TechBuildingBehavior.cpp"),
        ]);
        Add("FireWeaponWhenDamagedBehavior",
        [
            ..UpgradeMux(),
            new("StartsActive", FieldType.Bool, Source: "FireWeaponWhenDamagedBehavior.h"),
            new("ReactionWeaponPristine", FieldType.Reference, ReferenceKind: DefinitionKind.Weapon, Source: "FireWeaponWhenDamagedBehavior.h"),
            new("ReactionWeaponDamaged", FieldType.Reference, ReferenceKind: DefinitionKind.Weapon, Source: "FireWeaponWhenDamagedBehavior.h"),
            new("ReactionWeaponReallyDamaged", FieldType.Reference, ReferenceKind: DefinitionKind.Weapon, Source: "FireWeaponWhenDamagedBehavior.h"),
            new("ReactionWeaponRubble", FieldType.Reference, ReferenceKind: DefinitionKind.Weapon, Source: "FireWeaponWhenDamagedBehavior.h"),
            new("ContinuousWeaponPristine", FieldType.Reference, ReferenceKind: DefinitionKind.Weapon, Source: "FireWeaponWhenDamagedBehavior.h"),
            new("ContinuousWeaponDamaged", FieldType.Reference, ReferenceKind: DefinitionKind.Weapon, Source: "FireWeaponWhenDamagedBehavior.h"),
            new("ContinuousWeaponReallyDamaged", FieldType.Reference, ReferenceKind: DefinitionKind.Weapon, Source: "FireWeaponWhenDamagedBehavior.h"),
            new("ContinuousWeaponRubble", FieldType.Reference, ReferenceKind: DefinitionKind.Weapon, Source: "FireWeaponWhenDamagedBehavior.h"),
            new("DamageTypes", FieldType.AllNoneDeltaFlags, Table: NameTables.DamageType, Source: "FireWeaponWhenDamagedBehavior.h"),
            Real("DamageAmount", "FireWeaponWhenDamagedBehavior.h"),
        ]);
        Add("FireWeaponPower", [..SpecialPowerBase(), new("MaxShotsToFire", FieldType.Integer, Source: "FireWeaponPower.cpp")]);
        Add("BaikonurLaunchPower", [..SpecialPowerBase(), new("DetonationObject", FieldType.Text, Source: "BaikonurLaunchPower.cpp")]);
        // FlightDeckBehavior.cpp: the carrier's runway/launch fields are recognized so the shipped data compiles; the module is a shell (no aircraft are launched, landed or replaced).
        Add("FlightDeckBehavior",
        [
            new("NumRunways", FieldType.Integer, Source: "FlightDeckBehavior.cpp"),
            new("NumSpacesPerRunway", FieldType.Integer, Source: "FlightDeckBehavior.cpp"),
            new("Runway1Spaces", FieldType.Opaque, Source: "FlightDeckBehavior.cpp"), new("Runway1Takeoff", FieldType.Opaque, Source: "FlightDeckBehavior.cpp"),
            new("Runway1Landing", FieldType.Opaque, Source: "FlightDeckBehavior.cpp"), new("Runway1Taxi", FieldType.Opaque, Source: "FlightDeckBehavior.cpp"),
            new("Runway1Creation", FieldType.Opaque, Source: "FlightDeckBehavior.cpp"), new("Runway1CatapultSystem", FieldType.Opaque, Source: "FlightDeckBehavior.cpp"),
            new("Runway2Spaces", FieldType.Opaque, Source: "FlightDeckBehavior.cpp"), new("Runway2Takeoff", FieldType.Opaque, Source: "FlightDeckBehavior.cpp"),
            new("Runway2Landing", FieldType.Opaque, Source: "FlightDeckBehavior.cpp"), new("Runway2Taxi", FieldType.Opaque, Source: "FlightDeckBehavior.cpp"),
            new("Runway2Creation", FieldType.Opaque, Source: "FlightDeckBehavior.cpp"), new("Runway2CatapultSystem", FieldType.Opaque, Source: "FlightDeckBehavior.cpp"),
            Real("HealAmountPerSecond", "FlightDeckBehavior.cpp"),
            Real("ApproachHeight", "FlightDeckBehavior.cpp"),
            Real("LandingDeckHeightOffset", "FlightDeckBehavior.cpp"),
            new("ParkingCleanupPeriod", FieldType.DurationMs, Source: "FlightDeckBehavior.cpp"),
            new("HumanFollowPeriod", FieldType.DurationMs, Source: "FlightDeckBehavior.cpp"),
            new("PayloadTemplate", FieldType.Text, Source: "FlightDeckBehavior.cpp"),
            new("ReplacementDelay", FieldType.DurationMs, Source: "FlightDeckBehavior.cpp"),
            new("DockAnimationDelay", FieldType.DurationMs, Source: "FlightDeckBehavior.cpp"),
            new("LaunchWaveDelay", FieldType.DurationMs, Source: "FlightDeckBehavior.cpp"),
            new("LaunchRampDelay", FieldType.DurationMs, Source: "FlightDeckBehavior.cpp"),
            new("LowerRampDelay", FieldType.DurationMs, Source: "FlightDeckBehavior.cpp"),
            new("CatapultFireDelay", FieldType.DurationMs, Source: "FlightDeckBehavior.cpp"),
        ]);
        Add("HordeUpdate",
        [
            new("UpdateRate", FieldType.DurationMs, Source: "HordeUpdate.cpp"),
            new("KindOf", FieldType.Flags, Table: NameTables.KindOf, Source: "HordeUpdate.cpp"),
            new("Count", FieldType.Integer, Source: "HordeUpdate.cpp"),
            new("Radius", FieldType.Distance, Source: "HordeUpdate.cpp"),
            new("RubOffRadius", FieldType.Distance, Source: "HordeUpdate.cpp"),
            new("AlliesOnly", FieldType.Bool, Source: "HordeUpdate.cpp"),
            new("ExactMatch", FieldType.Bool, Source: "HordeUpdate.cpp"),
            new("Action", FieldType.Text, Source: "HordeUpdate.cpp TheHordeActionTypeNames (HORDE)"),
            new("FlagSubObjectNames", FieldType.Opaque, Source: "HordeUpdate.cpp"),
            new("AllowedNationalism", FieldType.Bool, Source: "HordeUpdate.cpp"),
        ]);
        Add("PowerPlantUpdate", [new("RodsExtendTime", FieldType.DurationMs, Source: "PowerPlantUpdate.cpp")]);
        Add("PowerPlantUpgrade", [..UpgradeMux()]);
        Add("OverchargeBehavior",
        [
            new("HealthPercentToDrainPerSecond", FieldType.Percent, Source: "OverchargeBehavior.cpp"),
            new("NotAllowedWhenHealthBelowPercent", FieldType.Percent, Source: "OverchargeBehavior.cpp"),
        ]);
        Add("RadarUpdate", [new("RadarExtendTime", FieldType.DurationMs, Source: "RadarUpdate.cpp")]);
        Add("RebuildHoleExposeDie",
        [
            ..DieMux(),
            new("HoleName", FieldType.Reference, ReferenceKind: DefinitionKind.Object, Source: "RebuildHoleExposeDie.cpp"),
            Real("HoleMaxHealth", "RebuildHoleExposeDie.cpp"),
            new("TransferAttackers", FieldType.Bool, Source: "RebuildHoleExposeDie.cpp"),
        ]);
        Add("RebuildHoleBehavior",
        [
            new("WorkerObjectName", FieldType.Reference, ReferenceKind: DefinitionKind.Object, Source: "RebuildHoleBehavior.cpp"),
            new("WorkerRespawnDelay", FieldType.DurationMs, Source: "RebuildHoleBehavior.cpp"),
            new("HoleHealthRegen%PerSecond", FieldType.Percent, Source: "RebuildHoleBehavior.cpp"),
        ]);
        Add("PoisonedBehavior",
        [
            new("PoisonDamageInterval", FieldType.DurationMs, Source: "PoisonedBehavior.cpp"),
            new("PoisonDuration", FieldType.DurationMs, Source: "PoisonedBehavior.cpp"),
        ]);
        Add("GarrisonContain",
        [
            // OpenContainModuleData (OpenContain.cpp): the shared contain fields. Mechanically modeled: DieMux (for
            // onDie), ContainMax, DamagePercentToUnits, Allow/ForbidInsideKindOf, AllowAlliesInside/AllowEnemiesInside/
            // AllowNeutralInside (using the same "same owner = ally, no neutral" approximation as StealthDetectorUpdate
            // — no team/diplomacy system yet). Presentation or not-yet-built-subsystem, all Opaque: EnterSound/ExitSound,
            // BurnedDeathToUnits, PassengersAllowedToFire/PassengersInTurret (no firing-from-container), NumberOfExitPaths/
            // DoorOpenTime (no exit-path/door animation), WeaponBonusPassedToPassengers (no WeaponBonus system at all,
            // project-wide).
            ..DieMux(),
            new("ContainMax", FieldType.Integer, Source: "OpenContain.cpp"),
            new("EnterSound", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("ExitSound", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("DamagePercentToUnits", FieldType.Percent, Source: "OpenContain.cpp"),
            new("BurnedDeathToUnits", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("AllowInsideKindOf", FieldType.Flags, Table: NameTables.KindOf, Source: "OpenContain.cpp"),
            new("ForbidInsideKindOf", FieldType.Flags, Table: NameTables.KindOf, Source: "OpenContain.cpp"),
            new("PassengersAllowedToFire", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("PassengersInTurret", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("NumberOfExitPaths", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("DoorOpenTime", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("WeaponBonusPassedToPassengers", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("AllowAlliesInside", FieldType.Bool, Source: "OpenContain.cpp"),
            new("AllowEnemiesInside", FieldType.Bool, Source: "OpenContain.cpp"),
            new("AllowNeutralInside", FieldType.Bool, Source: "OpenContain.cpp"),
            // GarrisonContainModuleData (GarrisonContain.cpp): HealObjects/TimeForFullHeal are mechanically modeled.
            // MobileGarrison, ImmuneToClearBuildingAttacks (no ClearBuilding weapon effect) and IsEnclosingContainer are
            // presentation/not-yet-built-subsystem (Opaque); InitialRoster (starting occupants) is deferred — it needs
            // synchronous nested spawning at construction time, a bigger change to the spawn pipeline than this first
            // cut of the containment subsystem justifies (see the simulation-layer doc comment).
            new("MobileGarrison", FieldType.Opaque, Source: "GarrisonContain.h"),
            new("HealObjects", FieldType.Bool, Source: "GarrisonContain.h"),
            new("TimeForFullHeal", FieldType.DurationMs, Source: "GarrisonContain.h"),
            new("InitialRoster", FieldType.Opaque, Source: "GarrisonContain.h"),
            new("ImmuneToClearBuildingAttacks", FieldType.Opaque, Source: "GarrisonContain.h"),
            new("IsEnclosingContainer", FieldType.Opaque, Source: "GarrisonContain.h"),
        ]);
        Add("TransportContain",
        [
            // OpenContainModuleData shared fields (see GarrisonContain's schema comment for the rationale behind each
            // Opaque choice). ContainMax is inherited but unused: TransportContain::getContainMax only ever reads its
            // own Slots field, but ContainMax is still parseable, so it must still be typed.
            ..DieMux(),
            new("ContainMax", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("EnterSound", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("ExitSound", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("DamagePercentToUnits", FieldType.Percent, Source: "OpenContain.cpp"),
            new("BurnedDeathToUnits", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("AllowInsideKindOf", FieldType.Flags, Table: NameTables.KindOf, Source: "OpenContain.cpp"),
            new("ForbidInsideKindOf", FieldType.Flags, Table: NameTables.KindOf, Source: "OpenContain.cpp"),
            new("PassengersAllowedToFire", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("PassengersInTurret", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("NumberOfExitPaths", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("DoorOpenTime", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("WeaponBonusPassedToPassengers", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("AllowAlliesInside", FieldType.Bool, Source: "OpenContain.cpp"),
            new("AllowEnemiesInside", FieldType.Bool, Source: "OpenContain.cpp"),
            new("AllowNeutralInside", FieldType.Bool, Source: "OpenContain.cpp"),
            // TransportContainModuleData's own fields. Slots and HealthRegen%PerSec are mechanically modeled (see the
            // simulation-layer doc comment); the rest need real movement/positioning, a capture system or a WeaponBonus
            // system this engine does not have, and are therefore Opaque.
            new("Slots", FieldType.Integer, Source: "TransportContain.h"),
            new("ScatterNearbyOnExit", FieldType.Opaque, Source: "TransportContain.h"),
            new("OrientLikeContainerOnExit", FieldType.Opaque, Source: "TransportContain.h"),
            new("KeepContainerVelocityOnExit", FieldType.Opaque, Source: "TransportContain.h"),
            new("GoAggressiveOnExit", FieldType.Opaque, Source: "TransportContain.h"),
            new("ResetMoodCheckTimeOnExit", FieldType.Opaque, Source: "TransportContain.h"),
            new("DestroyRidersWhoAreNotFreeToExit", FieldType.Opaque, Source: "TransportContain.h"),
            new("ExitBone", FieldType.Opaque, Source: "TransportContain.h"),
            new("ExitPitchRate", FieldType.Opaque, Source: "TransportContain.h"),
            new("InitialPayload", FieldType.Opaque, Source: "TransportContain.h"),
            new("HealthRegen%PerSec", FieldType.Real, Source: "TransportContain.h"),
            new("ExitDelay", FieldType.Opaque, Source: "TransportContain.h"),
            new("ArmedRidersUpgradeMyWeaponSet", FieldType.Opaque, Source: "TransportContain.h"),
            // DemoGeneral.ini sets this key twice (identical values, a copy-paste artifact in the shipped data).
            new("DelayExitInAir", FieldType.Opaque, Repeat: RepeatPolicy.Accumulate, Source: "TransportContain.h"),
        ]);

        // SpecialPower.cpp SpecialPowerModuleData (the base of every special-power module). InitiateSound is presentation.
        ImmutableArray<FieldSchema> SpecialPowerBase() =>
        [
            new("SpecialPowerTemplate", FieldType.Reference, ReferenceKind: DefinitionKind.SpecialPower, Source: "SpecialPowerModule.cpp"),
            new("UpdateModuleStartsAttack", FieldType.Bool, Source: "SpecialPowerModule.cpp"),
            new("StartsPaused", FieldType.Bool, Source: "SpecialPowerModule.cpp"),
            new("InitiateSound", FieldType.Opaque, Source: "SpecialPowerModule.cpp"),
            new("ScriptedSpecialPowerOnly", FieldType.Bool, Source: "SpecialPowerModule.cpp"),
        ];
        Add("SpecialAbility", SpecialPowerBase());
        // SpecialAbilityUpdate.h: the staging fields of the capture powers (StartAbilityRange, AbilityAbortRange, UnpackTime, PreparationTime, PackTime,
        // PackUnpackVariationFactor, NeedToFaceTarget, DoCaptureFX, AwardXPForTriggering) are read by CnCNew.Simulation SpecialAbilityUpdate
        // (docs/SIM-STAGED-CAPTURE.md); the special-object fields are recognized (so the shipped data compiles without an unknown-field error) but not read.
        Add("SpecialAbilityUpdate",
        [
            ..SpecialPowerBase(),
            Real("StartAbilityRange", "SpecialAbilityUpdate.cpp"),
            Real("AbilityAbortRange", "SpecialAbilityUpdate.cpp"),
            new("PreparationTime", FieldType.DurationMs, Source: "SpecialAbilityUpdate.cpp"),
            new("PersistentPrepTime", FieldType.DurationMs, Source: "SpecialAbilityUpdate.cpp"),
            new("PackTime", FieldType.DurationMs, Source: "SpecialAbilityUpdate.cpp"),
            new("UnpackTime", FieldType.DurationMs, Source: "SpecialAbilityUpdate.cpp"),
            Real("PackUnpackVariationFactor", "SpecialAbilityUpdate.cpp"),
            new("PreTriggerUnstealthTime", FieldType.DurationMs, Source: "SpecialAbilityUpdate.cpp"),
            new("SkipPackingWithNoTarget", FieldType.Bool, Source: "SpecialAbilityUpdate.cpp"),
            new("SpecialObject", FieldType.Reference, ReferenceKind: DefinitionKind.Object, Source: "SpecialAbilityUpdate.cpp"),
            new("SpecialObjectAttachToBone", FieldType.Text, Source: "SpecialAbilityUpdate.cpp"),
            new("MaxSpecialObjects", FieldType.Integer, Source: "SpecialAbilityUpdate.cpp"),
            new("SpecialObjectsPersistent", FieldType.Bool, Source: "SpecialAbilityUpdate.cpp"),
            new("UniqueSpecialObjectTargets", FieldType.Bool, Source: "SpecialAbilityUpdate.cpp"),
            new("SpecialObjectsPersistWhenOwnerDies", FieldType.Bool, Source: "SpecialAbilityUpdate.cpp"),
            new("AlwaysValidateSpecialObjects", FieldType.Bool, Source: "SpecialAbilityUpdate.cpp"),
            new("FlipOwnerAfterUnpacking", FieldType.Bool, Source: "SpecialAbilityUpdate.cpp"),
            new("FleeRangeAfterCompletion", FieldType.Real, Source: "SpecialAbilityUpdate.cpp"),
            new("EffectDuration", FieldType.DurationMs, Source: "SpecialAbilityUpdate.cpp"),
            new("EffectValue", FieldType.Integer, Source: "SpecialAbilityUpdate.cpp"),
            new("DoCaptureFX", FieldType.Bool, Source: "SpecialAbilityUpdate.cpp"),
            new("DisableFXParticleSystem", FieldType.Opaque, Source: "SpecialAbilityUpdate.cpp"),
            new("PackSound", FieldType.Opaque, Source: "SpecialAbilityUpdate.cpp"),
            new("UnpackSound", FieldType.Opaque, Source: "SpecialAbilityUpdate.cpp"),
            new("PrepSoundLoop", FieldType.Opaque, Source: "SpecialAbilityUpdate.cpp"),
            new("TriggerSound", FieldType.Opaque, Source: "SpecialAbilityUpdate.cpp"),
            new("LoseStealthOnTrigger", FieldType.Bool, Source: "SpecialAbilityUpdate.cpp"),
            new("AwardXPForTriggering", FieldType.Integer, Source: "SpecialAbilityUpdate.cpp"),
            new("ApproachRequiresLOS", FieldType.Bool, Source: "SpecialAbilityUpdate.cpp"),
            new("NeedToFaceTarget", FieldType.Bool, Source: "SpecialAbilityUpdate.cpp"),
            new("PersistenceRequiresRecharge", FieldType.Bool, Source: "SpecialAbilityUpdate.cpp"),
        ]);
        Add("OCLSpecialPower",
        [
            ..SpecialPowerBase(),
            // "SCIENCE OCL" pairs, kept in file order: the first one whose science the player owns wins.
            new("UpgradeOCL", FieldType.TextList, Repeat: RepeatPolicy.Accumulate, Count: 2, Source: "OCLSpecialPower.cpp parseOCLSpecialPowerUpgrade"),
            new("OCL", FieldType.Reference, ReferenceKind: DefinitionKind.ObjectCreationList, Source: "OCLSpecialPower.cpp"),
            new("CreateLocation", FieldType.Enum, Table: NameTablesPhase5.OclCreateLocation, Source: "OCLSpecialPower.cpp"),
            new("ReferenceObject", FieldType.Text, Source: "OCLSpecialPower.cpp"),
            new("OCLAdjustPositionToPassable", FieldType.Bool, Source: "OCLSpecialPower.cpp"),
        ]);
        Add("CashBountyPower",
        [
            ..SpecialPowerBase(),
            new("Bounty", FieldType.Percent, Source: "CashBountyPower.cpp"),
        ]);
        Add("CashHackSpecialPower",
        [
            ..SpecialPowerBase(),
            new("MoneyAmount", FieldType.Integer, Source: "CashHackSpecialPower.cpp"),
            new("UpgradeMoneyAmount", FieldType.TextList, Repeat: RepeatPolicy.Accumulate, Count: 2, Source: "CashHackSpecialPower.cpp parseCashHackSpecialPowerUpgradePair"),
        ]);
        Add("SpecialPowerCreate", []);
        Add("GrantUpgradeCreate",
        [
            new("UpgradeToGrant", FieldType.Reference, ReferenceKind: DefinitionKind.Upgrade, Source: "GrantUpgradeCreate.cpp"),
            new("ExemptStatus", FieldType.Flags, Table: NameTables.ObjectStatus, Source: "GrantUpgradeCreate.cpp"),
        ]);
        Add("GrantScienceUpgrade",
        [
            ..UpgradeMux(),
            new("GrantScience", FieldType.Reference, ReferenceKind: DefinitionKind.Science, Source: "GrantScienceUpgrade.cpp"),
        ]);
        Add("UnpauseSpecialPowerUpgrade",
        [
            ..UpgradeMux(),
            new("SpecialPowerTemplate", FieldType.Reference, ReferenceKind: DefinitionKind.SpecialPower, Source: "UnpauseSpecialPowerUpgrade.cpp"),
        ]);
        Add("SpecialPowerCompletionDie",
        [
            ..DieMux(),
            new("SpecialPowerTemplate", FieldType.Reference, ReferenceKind: DefinitionKind.SpecialPower, Source: "SpecialPowerCompletionDie.cpp"),
        ]);
        Add("SpectreGunshipDeploymentUpdate",
        [
            new("SpecialPowerTemplate", FieldType.Reference, ReferenceKind: DefinitionKind.SpecialPower, Source: "SpectreGunshipDeploymentUpdate.cpp"),
            new("RequiredScience", FieldType.Reference, ReferenceKind: DefinitionKind.Science, Source: "SpectreGunshipDeploymentUpdate.cpp"),
            new("GunshipTemplateName", FieldType.Reference, ReferenceKind: DefinitionKind.Object, Source: "SpectreGunshipDeploymentUpdate.cpp"),
            new("AttackAreaRadius", FieldType.Real, Source: "SpectreGunshipDeploymentUpdate.cpp"),
            new("CreateLocation", FieldType.Enum, Table: NameTablesPhase5.GunshipCreateLocation, Source: "SpectreGunshipDeploymentUpdate.cpp"),
        ]);

        // ------------------------------------------------------------------------- transports, tunnels, projectiles (phase 8, E1)
        // Field lists: the reference FieldParse tables (sources named per field), verified against every occurrence in .local/phase2-import-a.
        ImmutableArray<FieldSchema> OpenContainFields(string containMaxSource) =>
        [
            ..DieMux(),
            new("ContainMax", FieldType.Opaque, Source: containMaxSource),
            new("EnterSound", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("ExitSound", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("DamagePercentToUnits", FieldType.Percent, Source: "OpenContain.cpp"),
            new("BurnedDeathToUnits", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("AllowInsideKindOf", FieldType.Flags, Table: NameTables.KindOf, Source: "OpenContain.cpp"),
            new("ForbidInsideKindOf", FieldType.Flags, Table: NameTables.KindOf, Source: "OpenContain.cpp"),
            new("PassengersAllowedToFire", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("PassengersInTurret", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("NumberOfExitPaths", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("DoorOpenTime", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("WeaponBonusPassedToPassengers", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("AllowAlliesInside", FieldType.Bool, Source: "OpenContain.cpp"),
            new("AllowEnemiesInside", FieldType.Bool, Source: "OpenContain.cpp"),
            new("AllowNeutralInside", FieldType.Bool, Source: "OpenContain.cpp"),
        ];
        ImmutableArray<FieldSchema> TransportContainFields() =>
        [
            new("Slots", FieldType.Integer, Source: "TransportContain.h"),
            new("ScatterNearbyOnExit", FieldType.Opaque, Source: "TransportContain.h"),
            new("OrientLikeContainerOnExit", FieldType.Opaque, Source: "TransportContain.h"),
            new("KeepContainerVelocityOnExit", FieldType.Opaque, Source: "TransportContain.h"),
            new("GoAggressiveOnExit", FieldType.Opaque, Source: "TransportContain.h"),
            new("ResetMoodCheckTimeOnExit", FieldType.Opaque, Source: "TransportContain.h"),
            new("DestroyRidersWhoAreNotFreeToExit", FieldType.Opaque, Source: "TransportContain.h"),
            new("ExitBone", FieldType.Opaque, Source: "TransportContain.h"),
            new("ExitPitchRate", FieldType.Opaque, Source: "TransportContain.h"),
            new("InitialPayload", FieldType.Opaque, Source: "TransportContain.h"),
            new("HealthRegen%PerSec", FieldType.Real, Source: "TransportContain.h"),
            new("ExitDelay", FieldType.Opaque, Source: "TransportContain.h"),
            new("ArmedRidersUpgradeMyWeaponSet", FieldType.Opaque, Source: "TransportContain.h"),
            new("DelayExitInAir", FieldType.Opaque, Repeat: RepeatPolicy.Accumulate, Source: "TransportContain.h"),
        ];
        // TunnelContainModuleData = OpenContainModuleData + TimeForFullHeal (parseDurationReal). ContainMax is parseable but unused: the
        // capacity is GlobalData::m_maxTunnelCapacity (TunnelTracker::getContainMax).
        Add("TunnelContain", [..OpenContainFields("OpenContain.cpp (unused: TunnelTracker::getContainMax)"), new("TimeForFullHeal", FieldType.DurationMs, Source: "TunnelContain.h")]);
        // HelixContainModuleData = TransportContainModuleData + PayloadTemplateName (parseAsciiStringVectorAppend) + ShouldDrawPips (UI).
        Add("HelixContain",
        [
            ..OpenContainFields("OpenContain.cpp (unused: TransportContain::getContainMax reads Slots)"), ..TransportContainFields(),
            new("PayloadTemplateName", FieldType.ReferenceList, Repeat: RepeatPolicy.Accumulate, ReferenceKind: DefinitionKind.Object, Source: "HelixContain.cpp"),
            new("ShouldDrawPips", FieldType.Opaque, Source: "HelixContain.cpp"),
        ]);
        // TransportAIUpdate has no field table of its own (TransportAIUpdate.h): the AIUpdate base plus Turret sections.
        Add("TransportAIUpdate", AiBase("TransportAIUpdate.cpp / AIUpdate.cpp"), allowsNestedSections: true);
        // ChinookAIUpdateModuleData (ChinookAIUpdate.cpp buildFieldParse) extends SupplyTruckAIUpdateModuleData; the ropes and the rotor wash
        // are presentation (Opaque), the combat-drop timing is typed for the documented but not simulated rappel.
        Add("ChinookAIUpdate",
        [
            ..AiBase("ChinookAIUpdate.cpp"), ..TruckFields("ChinookAIUpdate.cpp"),
            new("UpgradedSupplyBoost", FieldType.Integer, Source: "ChinookAIUpdate.cpp"),
            new("NumRopes", FieldType.Integer, Source: "ChinookAIUpdate.cpp"),
            new("PerRopeDelayMin", FieldType.DurationMs, Source: "ChinookAIUpdate.cpp"),
            new("PerRopeDelayMax", FieldType.DurationMs, Source: "ChinookAIUpdate.cpp"),
            new("RopeWidth", FieldType.Opaque, Source: "ChinookAIUpdate.cpp"),
            new("RopeColor", FieldType.Opaque, Source: "ChinookAIUpdate.cpp"),
            new("RopeWobbleLen", FieldType.Opaque, Source: "ChinookAIUpdate.cpp"),
            new("RopeWobbleAmplitude", FieldType.Opaque, Source: "ChinookAIUpdate.cpp"),
            new("RopeWobbleRate", FieldType.Opaque, Source: "ChinookAIUpdate.cpp"),
            new("RopeFinalHeight", FieldType.Real, Source: "ChinookAIUpdate.cpp"),
            new("RappelSpeed", FieldType.Real, Source: "ChinookAIUpdate.cpp"),
            new("MinDropHeight", FieldType.Real, Source: "ChinookAIUpdate.cpp"),
            new("RotorWashParticleSystem", FieldType.Opaque, Source: "ChinookAIUpdate.cpp"),
        ], allowsNestedSections: true);
        // HelicopterSlowDeathBehaviorModuleData (HelicopterSlowDeathUpdate.cpp buildFieldParse) extends SlowDeathBehaviorModuleData.
        Add("HelicopterSlowDeathBehavior",
        [
            ..DieMux(),
            new("SinkRate", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("ProbabilityModifier", FieldType.Integer, Source: "SlowDeathBehavior.cpp"),
            new("ModifierBonusPerOverkillPercent", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("SinkDelay", FieldType.DurationMs, Source: "SlowDeathBehavior.cpp"),
            new("SinkDelayVariance", FieldType.DurationMs, Source: "SlowDeathBehavior.cpp"),
            new("DestructionDelay", FieldType.DurationMs, Source: "SlowDeathBehavior.cpp"),
            new("DestructionDelayVariance", FieldType.DurationMs, Source: "SlowDeathBehavior.cpp"),
            new("DestructionAltitude", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("FX", FieldType.Opaque, Repeat: RepeatPolicy.Accumulate, Source: "SlowDeathBehavior.cpp"),
            new("OCL", FieldType.Opaque, Repeat: RepeatPolicy.Accumulate, Source: "SlowDeathBehavior.cpp"),
            new("Weapon", FieldType.Opaque, Repeat: RepeatPolicy.Accumulate, Source: "SlowDeathBehavior.cpp"),
            new("FlingForce", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("FlingForceVariance", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("FlingPitch", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("FlingPitchVariance", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            Real("SpiralOrbitTurnRate", "HelicopterSlowDeathUpdate.cpp parseAngularVelocityReal"),
            Real("SpiralOrbitForwardSpeed", "HelicopterSlowDeathUpdate.cpp parseVelocityReal"),
            Real("SpiralOrbitForwardSpeedDamping", "HelicopterSlowDeathUpdate.cpp"),
            Real("MinSelfSpin", "HelicopterSlowDeathUpdate.cpp parseAngularVelocityReal"),
            Real("MaxSelfSpin", "HelicopterSlowDeathUpdate.cpp parseAngularVelocityReal"),
            new("SelfSpinUpdateDelay", FieldType.DurationMs, Source: "HelicopterSlowDeathUpdate.cpp parseDurationReal"),
            Real("SelfSpinUpdateAmount", "HelicopterSlowDeathUpdate.cpp parseAngleReal"),
            new("FallHowFast", FieldType.Percent, Source: "HelicopterSlowDeathUpdate.cpp"),
            new("MinBladeFlyOffDelay", FieldType.DurationMs, Source: "HelicopterSlowDeathUpdate.cpp parseDurationReal"),
            new("MaxBladeFlyOffDelay", FieldType.DurationMs, Source: "HelicopterSlowDeathUpdate.cpp parseDurationReal"),
            new("AttachParticle", FieldType.Opaque, Source: "HelicopterSlowDeathUpdate.cpp"),
            new("AttachParticleBone", FieldType.Opaque, Source: "HelicopterSlowDeathUpdate.cpp"),
            new("AttachParticleLoc", FieldType.Opaque, Source: "HelicopterSlowDeathUpdate.cpp"),
            new("BladeObjectName", FieldType.Opaque, Source: "HelicopterSlowDeathUpdate.cpp"),
            new("BladeBoneName", FieldType.Opaque, Source: "HelicopterSlowDeathUpdate.cpp"),
            new("OCLEjectPilot", FieldType.Reference, ReferenceKind: DefinitionKind.ObjectCreationList, Source: "HelicopterSlowDeathUpdate.cpp"),
            new("FXBlade", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "HelicopterSlowDeathUpdate.cpp"),
            new("OCLBlade", FieldType.Reference, ReferenceKind: DefinitionKind.ObjectCreationList, Source: "HelicopterSlowDeathUpdate.cpp"),
            new("FXHitGround", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "HelicopterSlowDeathUpdate.cpp"),
            new("OCLHitGround", FieldType.Reference, ReferenceKind: DefinitionKind.ObjectCreationList, Source: "HelicopterSlowDeathUpdate.cpp"),
            new("FXFinalBlowUp", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "HelicopterSlowDeathUpdate.cpp"),
            new("OCLFinalBlowUp", FieldType.Reference, ReferenceKind: DefinitionKind.ObjectCreationList, Source: "HelicopterSlowDeathUpdate.cpp"),
            new("DelayFromGroundToFinalDeath", FieldType.DurationMs, Source: "HelicopterSlowDeathUpdate.cpp parseDurationReal"),
            new("FinalRubbleObject", FieldType.Reference, ReferenceKind: DefinitionKind.Object, Source: "HelicopterSlowDeathUpdate.cpp"),
            new("SoundDeathLoop", FieldType.Opaque, Source: "HelicopterSlowDeathUpdate.cpp"),
            Real("MaxBraking", "HelicopterSlowDeathUpdate.cpp parseAccelerationReal"),
        ]);
        // DumbProjectileBehaviorModuleData (DumbProjectileBehavior.cpp buildFieldParse).
        Add("DumbProjectileBehavior",
        [
            new("MaxLifespan", FieldType.DurationMs, Source: "DumbProjectileBehavior.cpp"),
            new("TumbleRandomly", FieldType.Bool, Source: "DumbProjectileBehavior.cpp"),
            new("DetonateCallsKill", FieldType.Bool, Source: "DumbProjectileBehavior.cpp"),
            new("OrientToFlightPath", FieldType.Bool, Source: "DumbProjectileBehavior.cpp"),
            Real("FirstHeight", "DumbProjectileBehavior.cpp"),
            Real("SecondHeight", "DumbProjectileBehavior.cpp"),
            new("FirstPercentIndent", FieldType.Percent, Source: "DumbProjectileBehavior.cpp"),
            new("SecondPercentIndent", FieldType.Percent, Source: "DumbProjectileBehavior.cpp"),
            new("GarrisonHitKillRequiredKindOf", FieldType.Flags, Table: NameTables.KindOf, Source: "DumbProjectileBehavior.cpp"),
            new("GarrisonHitKillForbiddenKindOf", FieldType.Flags, Table: NameTables.KindOf, Source: "DumbProjectileBehavior.cpp"),
            new("GarrisonHitKillCount", FieldType.Integer, Min: 0, Source: "DumbProjectileBehavior.cpp"),
            new("GarrisonHitKillFX", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "DumbProjectileBehavior.cpp"),
            Real("FlightPathAdjustDistPerSecond", "DumbProjectileBehavior.cpp parseVelocityReal"),
        ]);
        // MissileAIUpdateModuleData (MissileAIUpdate.cpp buildFieldParse) extends AIUpdateModuleData.
        Add("MissileAIUpdate",
        [
            ..AiBase("MissileAIUpdate.cpp / AIUpdate.cpp"),
            new("TryToFollowTarget", FieldType.Bool, Source: "MissileAIUpdate.cpp"),
            new("FuelLifetime", FieldType.DurationMs, Source: "MissileAIUpdate.cpp"),
            new("IgnitionDelay", FieldType.DurationMs, Source: "MissileAIUpdate.cpp"),
            Real("InitialVelocity", "MissileAIUpdate.cpp parseVelocityReal"),
            Real("DistanceToTravelBeforeTurning", "MissileAIUpdate.cpp"),
            Real("DistanceToTargetBeforeDiving", "MissileAIUpdate.cpp"),
            Real("DistanceToTargetForLock", "MissileAIUpdate.cpp"),
            new("IgnitionFX", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "MissileAIUpdate.cpp"),
            new("UseWeaponSpeed", FieldType.Bool, Source: "MissileAIUpdate.cpp"),
            new("DetonateOnNoFuel", FieldType.Bool, Source: "MissileAIUpdate.cpp"),
            Real("DistanceScatterWhenJammed", "MissileAIUpdate.cpp"),
            new("GarrisonHitKillRequiredKindOf", FieldType.Flags, Table: NameTables.KindOf, Source: "MissileAIUpdate.cpp"),
            new("GarrisonHitKillForbiddenKindOf", FieldType.Flags, Table: NameTables.KindOf, Source: "MissileAIUpdate.cpp"),
            new("GarrisonHitKillCount", FieldType.Integer, Min: 0, Source: "MissileAIUpdate.cpp"),
            new("GarrisonHitKillFX", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "MissileAIUpdate.cpp"),
            new("DetonateCallsKill", FieldType.Bool, Source: "MissileAIUpdate.cpp"),
            new("KillSelfDelay", FieldType.DurationMs, Source: "MissileAIUpdate.cpp"),
        ]);
        // PointDefenseLaserUpdateModuleData (PointDefenseLaserUpdate.cpp buildFieldParse). PredictTargetVelocityFactor has no effect in the
        // reference (the predicted position is computed and then not used), it is typed for completeness.
        Add("PointDefenseLaserUpdate",
        [
            new("WeaponTemplate", FieldType.Reference, ReferenceKind: DefinitionKind.Weapon, Source: "PointDefenseLaserUpdate.cpp"),
            new("PrimaryTargetTypes", FieldType.Flags, Table: NameTables.KindOf, Source: "PointDefenseLaserUpdate.cpp"),
            new("SecondaryTargetTypes", FieldType.Flags, Table: NameTables.KindOf, Source: "PointDefenseLaserUpdate.cpp"),
            new("ScanRate", FieldType.DurationMs, Source: "PointDefenseLaserUpdate.cpp"),
            Real("ScanRange", "PointDefenseLaserUpdate.cpp"),
            Real("PredictTargetVelocityFactor", "PointDefenseLaserUpdate.cpp"),
        ]);
        // ToppleUpdateModuleData (ToppleUpdate.cpp buildFieldParse). FX/stump-burn presentation and the cosmetic planar
        // realignment (ReorientToppledRubble, the "x-axis" rotation in applyTopplingForce) are Opaque: phase 10's collision
        // module tracks only the fall angle and the resulting death/stump spawn, not a 3D model transform.
        Add("ToppleUpdate",
        [
            new("ToppleFX", FieldType.Opaque, Source: "ToppleUpdate.cpp"),
            new("BounceFX", FieldType.Opaque, Source: "ToppleUpdate.cpp"),
            new("StumpName", FieldType.Reference, ReferenceKind: DefinitionKind.Object, Source: "ToppleUpdate.cpp"),
            new("KillWhenStartToppling", FieldType.Bool, Source: "ToppleUpdate.cpp"),
            new("KillWhenFinishedToppling", FieldType.Bool, Source: "ToppleUpdate.cpp"),
            new("KillStumpWhenToppled", FieldType.Bool, Source: "ToppleUpdate.cpp"),
            new("ToppleLeftOrRightOnly", FieldType.Bool, Source: "ToppleUpdate.cpp"),
            new("ReorientToppledRubble", FieldType.Opaque, Source: "ToppleUpdate.cpp"),
            new("InitialVelocityPercent", FieldType.Percent, Source: "ToppleUpdate.cpp"),
            new("InitialAccelPercent", FieldType.Percent, Source: "ToppleUpdate.cpp"),
            new("BounceVelocityPercent", FieldType.Percent, Source: "ToppleUpdate.cpp"),
        ]);
        // StructureToppleUpdateModuleData (StructureToppleUpdate.cpp buildFieldParse). The burst-FX cadence, every FX list,
        // the damage-FX type gate and the OCL/AngleFX custom parsers are Opaque presentation; the crushing weapon and the
        // delay/integrity/decay fields that drive the actual collapse and its ground damage are typed and consumed.
        Add("StructureToppleUpdate",
        [
            ..DieMux(),
            new("MinToppleDelay", FieldType.DurationMs, Source: "StructureToppleUpdate.cpp"),
            new("MaxToppleDelay", FieldType.DurationMs, Source: "StructureToppleUpdate.cpp"),
            new("MinToppleBurstDelay", FieldType.Opaque, Source: "StructureToppleUpdate.cpp"),
            new("MaxToppleBurstDelay", FieldType.Opaque, Source: "StructureToppleUpdate.cpp"),
            new("StructuralIntegrity", FieldType.Real, Source: "StructureToppleUpdate.cpp"),
            new("StructuralDecay", FieldType.Real, Source: "StructureToppleUpdate.cpp"),
            new("DamageFXTypes", FieldType.Opaque, Source: "StructureToppleUpdate.cpp"),
            new("TopplingFX", FieldType.Opaque, Source: "StructureToppleUpdate.cpp"),
            new("ToppleDelayFX", FieldType.Opaque, Source: "StructureToppleUpdate.cpp"),
            new("ToppleStartFX", FieldType.Opaque, Source: "StructureToppleUpdate.cpp"),
            new("ToppleDoneFX", FieldType.Opaque, Source: "StructureToppleUpdate.cpp"),
            new("CrushingFX", FieldType.Opaque, Source: "StructureToppleUpdate.cpp"),
            new("CrushingWeaponName", FieldType.Reference, ReferenceKind: DefinitionKind.Weapon, Source: "StructureToppleUpdate.cpp"),
            new("OCL", FieldType.Opaque, Repeat: RepeatPolicy.Accumulate, Source: "StructureToppleUpdate.cpp"),
            new("AngleFX", FieldType.Opaque, Repeat: RepeatPolicy.Accumulate, Source: "StructureToppleUpdate.cpp"),
        ]);
        // HeightDieUpdateModuleData (HeightDieUpdate.cpp buildFieldParse). DestroyAttachedParticlesAtHeight is a presentation
        // cleanup hint (no particle systems exist to destroy) and stays Opaque.
        Add("HeightDieUpdate",
        [
            new("TargetHeight", FieldType.Real, Source: "HeightDieUpdate.cpp"),
            new("TargetHeightIncludesStructures", FieldType.Bool, Source: "HeightDieUpdate.cpp"),
            new("OnlyWhenMovingDown", FieldType.Bool, Source: "HeightDieUpdate.cpp"),
            new("DestroyAttachedParticlesAtHeight", FieldType.Opaque, Source: "HeightDieUpdate.cpp"),
            new("SnapToGroundOnDeath", FieldType.Bool, Source: "HeightDieUpdate.cpp"),
            new("InitialDelay", FieldType.DurationMs, Source: "HeightDieUpdate.cpp"),
        ]);

        // JetSlowDeathBehaviorModuleData (JetSlowDeathBehavior.cpp buildFieldParse) extends SlowDeathBehaviorModuleData. RollRate/RollRateDelta/PitchRate turn the model
        // and DeathLoopSound plays a sound: typed, no consumer.
        Add("JetSlowDeathBehavior",
        [
            ..DieMux(),
            new("SinkRate", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("ProbabilityModifier", FieldType.Integer, Source: "SlowDeathBehavior.cpp"),
            new("ModifierBonusPerOverkillPercent", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("SinkDelay", FieldType.DurationMs, Source: "SlowDeathBehavior.cpp"),
            new("SinkDelayVariance", FieldType.DurationMs, Source: "SlowDeathBehavior.cpp"),
            new("DestructionDelay", FieldType.DurationMs, Source: "SlowDeathBehavior.cpp"),
            new("DestructionDelayVariance", FieldType.DurationMs, Source: "SlowDeathBehavior.cpp"),
            new("DestructionAltitude", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("FX", FieldType.Opaque, Repeat: RepeatPolicy.Accumulate, Source: "SlowDeathBehavior.cpp"),
            new("OCL", FieldType.Opaque, Repeat: RepeatPolicy.Accumulate, Source: "SlowDeathBehavior.cpp"),
            new("Weapon", FieldType.Opaque, Repeat: RepeatPolicy.Accumulate, Source: "SlowDeathBehavior.cpp"),
            new("FlingForce", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("FlingForceVariance", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("FlingPitch", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("FlingPitchVariance", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("FXOnGroundDeath", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "JetSlowDeathBehavior.cpp"),
            new("OCLOnGroundDeath", FieldType.Reference, ReferenceKind: DefinitionKind.ObjectCreationList, Source: "JetSlowDeathBehavior.cpp"),
            new("FXInitialDeath", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "JetSlowDeathBehavior.cpp"),
            new("OCLInitialDeath", FieldType.Reference, ReferenceKind: DefinitionKind.ObjectCreationList, Source: "JetSlowDeathBehavior.cpp"),
            new("DelaySecondaryFromInitialDeath", FieldType.DurationMs, Source: "JetSlowDeathBehavior.cpp"),
            new("FXSecondary", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "JetSlowDeathBehavior.cpp"),
            new("OCLSecondary", FieldType.Reference, ReferenceKind: DefinitionKind.ObjectCreationList, Source: "JetSlowDeathBehavior.cpp"),
            new("FXHitGround", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "JetSlowDeathBehavior.cpp"),
            new("OCLHitGround", FieldType.Reference, ReferenceKind: DefinitionKind.ObjectCreationList, Source: "JetSlowDeathBehavior.cpp"),
            new("DelayFinalBlowUpFromHitGround", FieldType.DurationMs, Source: "JetSlowDeathBehavior.cpp"),
            new("FXFinalBlowUp", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "JetSlowDeathBehavior.cpp"),
            new("OCLFinalBlowUp", FieldType.Reference, ReferenceKind: DefinitionKind.ObjectCreationList, Source: "JetSlowDeathBehavior.cpp"),
            new("DeathLoopSound", FieldType.Opaque, Source: "JetSlowDeathBehavior.cpp"),
            Real("RollRate", "JetSlowDeathBehavior.cpp"),
            new("RollRateDelta", FieldType.Percent, Source: "JetSlowDeathBehavior.cpp"),
            Real("PitchRate", "JetSlowDeathBehavior.cpp"),
            new("FallHowFast", FieldType.Percent, Source: "JetSlowDeathBehavior.cpp"),
        ]);
        // CountermeasuresBehaviorModuleData (CountermeasuresBehavior.h). FlareBoneBaseName places the flare on the model (presentation); MustReloadAtAirfield has no airfield to
        // reload at here. VolleyArcAngle is in degrees (INI::parseAngleReal, converted by the module).
        Add("CountermeasuresBehavior",
        [
            ..UpgradeMux(),
            new("FlareTemplateName", FieldType.Reference, ReferenceKind: DefinitionKind.Object, Source: "CountermeasuresBehavior.h"),
            new("FlareBoneBaseName", FieldType.Text, Source: "CountermeasuresBehavior.h"),
            new("VolleySize", FieldType.Integer, Min: 0, Source: "CountermeasuresBehavior.h"),
            Real("VolleyArcAngle", "CountermeasuresBehavior.h parseAngleReal"),
            Real("VolleyVelocityFactor", "CountermeasuresBehavior.h"),
            new("DelayBetweenVolleys", FieldType.DurationMs, Source: "CountermeasuresBehavior.h"),
            new("NumberOfVolleys", FieldType.Integer, Min: 0, Source: "CountermeasuresBehavior.h"),
            new("ReloadTime", FieldType.DurationMs, Source: "CountermeasuresBehavior.h"),
            new("EvasionRate", FieldType.Percent, Source: "CountermeasuresBehavior.h"),
            new("MustReloadAtAirfield", FieldType.Bool, Source: "CountermeasuresBehavior.h"),
            new("MissileDecoyDelay", FieldType.DurationMs, Source: "CountermeasuresBehavior.h"),
            new("ReactionLaunchLatency", FieldType.DurationMs, Source: "CountermeasuresBehavior.h"),
        ]);
        // ParachuteContainModuleData (ParachuteContain.cpp) on OpenContainModuleData (OpenContain.cpp; see TransportContain for the Opaque choices).
        Add("ParachuteContain",
        [
            ..DieMux(),
            new("ContainMax", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("EnterSound", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("ExitSound", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("DamagePercentToUnits", FieldType.Percent, Source: "OpenContain.cpp"),
            new("BurnedDeathToUnits", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("AllowInsideKindOf", FieldType.Flags, Table: NameTables.KindOf, Source: "OpenContain.cpp"),
            new("ForbidInsideKindOf", FieldType.Flags, Table: NameTables.KindOf, Source: "OpenContain.cpp"),
            new("PassengersAllowedToFire", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("PassengersInTurret", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("NumberOfExitPaths", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("DoorOpenTime", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("WeaponBonusPassedToPassengers", FieldType.Opaque, Source: "OpenContain.cpp"),
            new("AllowAlliesInside", FieldType.Bool, Source: "OpenContain.cpp"),
            new("AllowEnemiesInside", FieldType.Bool, Source: "OpenContain.cpp"),
            new("AllowNeutralInside", FieldType.Bool, Source: "OpenContain.cpp"),
            Real("PitchRateMax", "ParachuteContain.cpp parseAngularVelocityReal"),
            Real("RollRateMax", "ParachuteContain.cpp parseAngularVelocityReal"),
            Real("LowAltitudeDamping", "ParachuteContain.cpp"),
            Real("ParachuteOpenDist", "ParachuteContain.cpp"),
            Real("KillWhenLandingInWaterSlop", "ParachuteContain.cpp"),
            new("FreeFallDamagePercent", FieldType.Percent, Source: "ParachuteContain.cpp"),
            new("ParachuteOpenSound", FieldType.Opaque, Source: "ParachuteContain.cpp"),
        ]);
        // UndeadBodyModuleData (UndeadBody.cpp): ActiveBody plus the second life's maximum health.
        Add("UndeadBody", [..Body(), Real("SecondLifeMaxHealth", "UndeadBody.cpp")]);
        // BattleBusSlowDeathBehaviorModuleData (BattleBusSlowDeathBehavior.cpp) extends SlowDeathBehaviorModuleData.
        Add("BattleBusSlowDeathBehavior",
        [
            ..DieMux(),
            new("SinkRate", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("ProbabilityModifier", FieldType.Integer, Source: "SlowDeathBehavior.cpp"),
            new("ModifierBonusPerOverkillPercent", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("SinkDelay", FieldType.DurationMs, Source: "SlowDeathBehavior.cpp"),
            new("SinkDelayVariance", FieldType.DurationMs, Source: "SlowDeathBehavior.cpp"),
            new("DestructionDelay", FieldType.DurationMs, Source: "SlowDeathBehavior.cpp"),
            new("DestructionDelayVariance", FieldType.DurationMs, Source: "SlowDeathBehavior.cpp"),
            new("DestructionAltitude", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("FX", FieldType.Opaque, Repeat: RepeatPolicy.Accumulate, Source: "SlowDeathBehavior.cpp"),
            new("OCL", FieldType.Opaque, Repeat: RepeatPolicy.Accumulate, Source: "SlowDeathBehavior.cpp"),
            new("Weapon", FieldType.Opaque, Repeat: RepeatPolicy.Accumulate, Source: "SlowDeathBehavior.cpp"),
            new("FlingForce", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("FlingForceVariance", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("FlingPitch", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("FlingPitchVariance", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("FXStartUndeath", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "BattleBusSlowDeathBehavior.cpp"),
            new("OCLStartUndeath", FieldType.Reference, ReferenceKind: DefinitionKind.ObjectCreationList, Source: "BattleBusSlowDeathBehavior.cpp"),
            new("FXHitGround", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "BattleBusSlowDeathBehavior.cpp"),
            new("OCLHitGround", FieldType.Reference, ReferenceKind: DefinitionKind.ObjectCreationList, Source: "BattleBusSlowDeathBehavior.cpp"),
            Real("ThrowForce", "BattleBusSlowDeathBehavior.cpp"),
            new("PercentDamageToPassengers", FieldType.Percent, Source: "BattleBusSlowDeathBehavior.cpp"),
            new("EmptyHulkDestructionDelay", FieldType.DurationMs, Source: "BattleBusSlowDeathBehavior.cpp"),
        ]);
        // SubObjectsUpgrade.cpp: the sub-object names only steer the model (typed, no consumer in the simulation).
        Add("SubObjectsUpgrade",
        [
            ..UpgradeMux(),
            new("ShowSubObjects", FieldType.Opaque, Repeat: RepeatPolicy.Accumulate, Source: "SubObjectsUpgrade.cpp"),
            new("HideSubObjects", FieldType.Opaque, Repeat: RepeatPolicy.Accumulate, Source: "SubObjectsUpgrade.cpp"),
        ]);
        Add("AnimationSteeringUpdate", [new("MinTransitionTime", FieldType.DurationMs, Source: "AnimationSteeringUpdate.h")]);
        // ParticleUplinkCannonUpdateModuleData (ParticleUplinkCannonUpdate.cpp buildFieldParse). The charge/antenna/ready times, bones, flares, sounds, scorch marks and the shroud reveal are
        // presentation (typed, no consumer); beam timing, path, damage and the beam object drive the simulated effect.
        Add("ParticleUplinkCannonUpdate",
        [
            new("SpecialPowerTemplate", FieldType.Reference, ReferenceKind: DefinitionKind.SpecialPower, Source: "ParticleUplinkCannonUpdate.cpp"),
            new("BeginChargeTime", FieldType.DurationMs, Source: "ParticleUplinkCannonUpdate.cpp"),
            new("RaiseAntennaTime", FieldType.DurationMs, Source: "ParticleUplinkCannonUpdate.cpp"),
            new("ReadyDelayTime", FieldType.DurationMs, Source: "ParticleUplinkCannonUpdate.cpp"),
            new("WidthGrowTime", FieldType.DurationMs, Source: "ParticleUplinkCannonUpdate.cpp"),
            new("BeamTravelTime", FieldType.DurationMs, Source: "ParticleUplinkCannonUpdate.cpp"),
            new("TotalFiringTime", FieldType.DurationMs, Source: "ParticleUplinkCannonUpdate.cpp"),
            Real("RevealRange", "ParticleUplinkCannonUpdate.cpp"),
            new("OuterEffectBoneName", FieldType.Text, Source: "ParticleUplinkCannonUpdate.cpp"),
            new("OuterEffectNumBones", FieldType.Integer, Min: 0, Source: "ParticleUplinkCannonUpdate.cpp"),
            new("OuterNodesLightFlareParticleSystem", FieldType.Opaque, Source: "ParticleUplinkCannonUpdate.cpp"),
            new("OuterNodesMediumFlareParticleSystem", FieldType.Opaque, Source: "ParticleUplinkCannonUpdate.cpp"),
            new("OuterNodesIntenseFlareParticleSystem", FieldType.Opaque, Source: "ParticleUplinkCannonUpdate.cpp"),
            new("ConnectorBoneName", FieldType.Text, Source: "ParticleUplinkCannonUpdate.cpp"),
            new("ConnectorMediumLaserName", FieldType.Opaque, Source: "ParticleUplinkCannonUpdate.cpp"),
            new("ConnectorIntenseLaserName", FieldType.Opaque, Source: "ParticleUplinkCannonUpdate.cpp"),
            new("ConnectorMediumFlare", FieldType.Opaque, Source: "ParticleUplinkCannonUpdate.cpp"),
            new("ConnectorIntenseFlare", FieldType.Opaque, Source: "ParticleUplinkCannonUpdate.cpp"),
            new("FireBoneName", FieldType.Text, Source: "ParticleUplinkCannonUpdate.cpp"),
            new("LaserBaseLightFlareParticleSystemName", FieldType.Opaque, Source: "ParticleUplinkCannonUpdate.cpp"),
            new("LaserBaseMediumFlareParticleSystemName", FieldType.Opaque, Source: "ParticleUplinkCannonUpdate.cpp"),
            new("LaserBaseIntenseFlareParticleSystemName", FieldType.Opaque, Source: "ParticleUplinkCannonUpdate.cpp"),
            new("ParticleBeamLaserName", FieldType.Reference, ReferenceKind: DefinitionKind.Object, Source: "ParticleUplinkCannonUpdate.cpp"),
            Real("SwathOfDeathDistance", "ParticleUplinkCannonUpdate.cpp"),
            Real("SwathOfDeathAmplitude", "ParticleUplinkCannonUpdate.cpp"),
            new("TotalScorchMarks", FieldType.Integer, Min: 0, Source: "ParticleUplinkCannonUpdate.cpp"),
            Real("ScorchMarkScalar", "ParticleUplinkCannonUpdate.cpp"),
            new("BeamLaunchFX", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "ParticleUplinkCannonUpdate.cpp"),
            new("DelayBetweenLaunchFX", FieldType.DurationMs, Source: "ParticleUplinkCannonUpdate.cpp"),
            new("GroundHitFX", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "ParticleUplinkCannonUpdate.cpp"),
            Real("DamagePerSecond", "ParticleUplinkCannonUpdate.cpp"),
            new("TotalDamagePulses", FieldType.Integer, Min: 0, Source: "ParticleUplinkCannonUpdate.cpp"),
            new("DamageType", FieldType.Enum, Table: NameTables.DamageType, Source: "ParticleUplinkCannonUpdate.cpp"),
            new("DeathType", FieldType.Enum, Table: NameTables.DeathType, Source: "ParticleUplinkCannonUpdate.cpp"),
            Real("DamageRadiusScalar", "ParticleUplinkCannonUpdate.cpp"),
            new("PoweringUpSoundLoop", FieldType.Opaque, Source: "ParticleUplinkCannonUpdate.cpp"),
            new("UnpackToIdleSoundLoop", FieldType.Opaque, Source: "ParticleUplinkCannonUpdate.cpp"),
            new("FiringToPackSoundLoop", FieldType.Opaque, Source: "ParticleUplinkCannonUpdate.cpp"),
            new("GroundAnnihilationSoundLoop", FieldType.Opaque, Source: "ParticleUplinkCannonUpdate.cpp"),
            new("DamagePulseRemnantObjectName", FieldType.Reference, ReferenceKind: DefinitionKind.Object, Source: "ParticleUplinkCannonUpdate.cpp"),
            Real("ManualDrivingSpeed", "ParticleUplinkCannonUpdate.cpp"),
            Real("ManualFastDrivingSpeed", "ParticleUplinkCannonUpdate.cpp"),
            new("DoubleClickToFastDriveDelay", FieldType.DurationMs, Source: "ParticleUplinkCannonUpdate.cpp"),
        ]);
        // DeliverPayloadAIUpdateModuleData (DeliverPayloadAIUpdate.h): only the keys of scripted reinforcement delivery; the nugget data (dive, strafe, visible payload) is the OCL's.
        Add("DeliverPayloadAIUpdate",
        [
            ..AiBase("DeliverPayloadAIUpdate.cpp / AIUpdate.cpp"),
            new("DoorDelay", FieldType.DurationMs, Source: "DeliverPayloadAIUpdate.h"),
            new("PutInContainer", FieldType.Reference, ReferenceKind: DefinitionKind.Object, Source: "DeliverPayloadAIUpdate.h"),
            Real("DeliveryDistance", "DeliverPayloadAIUpdate.h"),
            new("MaxAttempts", FieldType.Integer, Min: 0, Source: "DeliverPayloadAIUpdate.h"),
            new("DropDelay", FieldType.DurationMs, Source: "DeliverPayloadAIUpdate.h"),
            new("DropOffset", FieldType.Vector3, Source: "DeliverPayloadAIUpdate.h"),
            new("DropVariance", FieldType.Vector3, Source: "DeliverPayloadAIUpdate.h"),
            new("DeliveryDecal", FieldType.Opaque, Source: "DeliverPayloadAIUpdate.h"),
            Real("DeliveryDecalRadius", "DeliverPayloadAIUpdate.h"),
        ]);

        // ------------------------------------------------------------------------- slaves, spawns and containers (phase 14)
        // Field lists are the reference FieldParse tables (sources named per field), verified against every occurrence in .local/phase2-import-a.
        Add("SlavedUpdate",
        [
            new("GuardMaxRange", FieldType.Integer, Source: "SlavedUpdate.h"),
            new("GuardWanderRange", FieldType.Integer, Source: "SlavedUpdate.h"),
            new("AttackRange", FieldType.Integer, Source: "SlavedUpdate.h"),
            new("AttackWanderRange", FieldType.Integer, Source: "SlavedUpdate.h"),
            new("ScoutRange", FieldType.Integer, Source: "SlavedUpdate.h"),
            new("ScoutWanderRange", FieldType.Integer, Source: "SlavedUpdate.h"),
            new("RepairRange", FieldType.Integer, Source: "SlavedUpdate.h"),
            Real("RepairMinAltitude", "SlavedUpdate.h"),
            Real("RepairMaxAltitude", "SlavedUpdate.h"),
            new("DistToTargetToGrantRangeBonus", FieldType.Integer, Source: "SlavedUpdate.h"),
            Real("RepairRatePerSecond", "SlavedUpdate.h"),
            new("RepairWhenBelowHealth%", FieldType.Integer, Source: "SlavedUpdate.h"),
            new("RepairMinReadyTime", FieldType.DurationMs, Source: "SlavedUpdate.h"),
            new("RepairMaxReadyTime", FieldType.DurationMs, Source: "SlavedUpdate.h"),
            new("RepairMinWeldTime", FieldType.DurationMs, Source: "SlavedUpdate.h"),
            new("RepairMaxWeldTime", FieldType.DurationMs, Source: "SlavedUpdate.h"),
            new("RepairWeldingSys", FieldType.Opaque, Source: "SlavedUpdate.h"),
            new("RepairWeldingFXBone", FieldType.Opaque, Source: "SlavedUpdate.h"),
            new("StayOnSameLayerAsMaster", FieldType.Bool, Source: "SlavedUpdate.h"),
        ]);
        Add("MobMemberSlavedUpdate",
        [
            new("MustCatchUpRadius", FieldType.Integer, Source: "MobMemberSlavedUpdate.h"),
            new("CatchUpCrisisBailTime", FieldType.Integer, Min: 0, Source: "MobMemberSlavedUpdate.h"),
            new("NoNeedToCatchUpRadius", FieldType.Integer, Source: "MobMemberSlavedUpdate.h"),
            Real("Squirrelliness", "MobMemberSlavedUpdate.h"),
        ]);
        // HiveStructureBodyModuleData (HiveStructureBody.h) = StructureBody's fields + the two damage-type lists (default NONE).
        Add("HiveStructureBody",
        [
            ..Body(),
            new("PropagateDamageTypesToSlavesWhenExisting", FieldType.AllNoneDeltaFlags, Table: NameTables.DamageType, Source: "HiveStructureBody.h"),
            new("SwallowDamageTypesIfSlavesNotExisting", FieldType.AllNoneDeltaFlags, Table: NameTables.DamageType, Source: "HiveStructureBody.h"),
        ]);
        Add("SpawnPointProductionExitUpdate", [new("SpawnPointBoneName", FieldType.Text, Source: "SpawnPointProductionExitUpdate.h")]);
        // PropagandaTowerBehaviorModuleData (PropagandaTowerBehavior.cpp buildFieldParse).
        Add("PropagandaTowerBehavior",
        [
            Real("Radius", "PropagandaTowerBehavior.cpp"),
            new("DelayBetweenUpdates", FieldType.DurationMs, Source: "PropagandaTowerBehavior.cpp"),
            new("HealPercentEachSecond", FieldType.Percent, Source: "PropagandaTowerBehavior.cpp"),
            new("UpgradedHealPercentEachSecond", FieldType.Percent, Source: "PropagandaTowerBehavior.cpp"),
            new("PulseFX", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "PropagandaTowerBehavior.cpp"),
            new("UpgradedPulseFX", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "PropagandaTowerBehavior.cpp"),
            new("UpgradeRequired", FieldType.Reference, ReferenceKind: DefinitionKind.Upgrade, Source: "PropagandaTowerBehavior.cpp"),
            new("AffectsSelf", FieldType.Bool, Source: "PropagandaTowerBehavior.cpp"),
        ]);
        // RiderChangeContainModuleData (RiderChangeContain.cpp): TransportContain's fields (InitialPayload typed: one <template> [count]) + Rider1..8
        // (<template> <model condition> <weapon set flag> <object status> <command set> <locomotor set>) + the scuttle timing.
        Add("RiderChangeContain",
        [
            ..OpenContainFields("OpenContain.cpp (unused: TransportContain::getContainMax reads Slots)"),
            ..TransportContainFields().Where(field => field.Name != "InitialPayload"),
            new("InitialPayload", FieldType.CountedReference, ReferenceKind: DefinitionKind.Object, Source: "TransportContain.cpp parseInitialPayload"),
            new("Rider1", FieldType.TextList, Count: 6, Source: "RiderChangeContain.cpp parseRiderInfo"),
            new("Rider2", FieldType.TextList, Count: 6, Source: "RiderChangeContain.cpp parseRiderInfo"),
            new("Rider3", FieldType.TextList, Count: 6, Source: "RiderChangeContain.cpp parseRiderInfo"),
            new("Rider4", FieldType.TextList, Count: 6, Source: "RiderChangeContain.cpp parseRiderInfo"),
            new("Rider5", FieldType.TextList, Count: 6, Source: "RiderChangeContain.cpp parseRiderInfo"),
            new("Rider6", FieldType.TextList, Count: 6, Source: "RiderChangeContain.cpp parseRiderInfo"),
            new("Rider7", FieldType.TextList, Count: 6, Source: "RiderChangeContain.cpp parseRiderInfo"),
            new("Rider8", FieldType.TextList, Count: 6, Source: "RiderChangeContain.cpp parseRiderInfo"),
            new("ScuttleDelay", FieldType.DurationMs, Source: "RiderChangeContain.cpp"),
            new("ScuttleStatus", FieldType.Opaque, Source: "RiderChangeContain.cpp"),
        ]);
        // OverlordContainModuleData (OverlordContain.cpp) = TransportContain's fields + the payload names (parseAsciiStringVectorAppend) + ExperienceSinkForRider.
        Add("OverlordContain",
        [
            ..OpenContainFields("OpenContain.cpp (unused: TransportContain::getContainMax reads Slots)"), ..TransportContainFields(),
            new("PayloadTemplateName", FieldType.ReferenceList, Repeat: RepeatPolicy.Accumulate, ReferenceKind: DefinitionKind.Object, Source: "OverlordContain.cpp"),
            new("ExperienceSinkForRider", FieldType.Bool, Source: "OverlordContain.cpp"),
        ]);
        // InternetHackContainModuleData (InternetHackContain.cpp) adds nothing to TransportContain's table.
        Add("InternetHackContain", [..OpenContainFields("OpenContain.cpp (unused: TransportContain::getContainMax reads Slots)"), ..TransportContainFields()]);
        Add("PassengersFireUpgrade", [..UpgradeMux()]);
        // AssaultTransportAIUpdateModuleData (AssaultTransportAIUpdate.h): the AIUpdate base plus two reals.
        Add("AssaultTransportAIUpdate",
        [
            ..AiBase("AssaultTransportAIUpdate.cpp / AIUpdate.cpp"),
            Real("MembersGetHealedAtLifeRatio", "AssaultTransportAIUpdate.h"),
            Real("ClearRangeRequiredToContinueAttackMove", "AssaultTransportAIUpdate.h"),
        ], allowsNestedSections: true);
        // HackInternetAIUpdateModuleData (HackInternetAIUpdate.h): the AIUpdate base plus the cash and timing keys.
        Add("HackInternetAIUpdate",
        [
            ..AiBase("HackInternetAIUpdate.cpp / AIUpdate.cpp"),
            new("UnpackTime", FieldType.DurationMs, Source: "HackInternetAIUpdate.h"),
            new("PackTime", FieldType.DurationMs, Source: "HackInternetAIUpdate.h"),
            Real("PackUnpackVariationFactor", "HackInternetAIUpdate.h"),
            new("CashUpdateDelay", FieldType.DurationMs, Source: "HackInternetAIUpdate.h"),
            new("CashUpdateDelayFast", FieldType.DurationMs, Source: "HackInternetAIUpdate.h"),
            new("RegularCashAmount", FieldType.Integer, Min: 0, Source: "HackInternetAIUpdate.h"),
            new("VeteranCashAmount", FieldType.Integer, Min: 0, Source: "HackInternetAIUpdate.h"),
            new("EliteCashAmount", FieldType.Integer, Min: 0, Source: "HackInternetAIUpdate.h"),
            new("HeroicCashAmount", FieldType.Integer, Min: 0, Source: "HackInternetAIUpdate.h"),
            new("XpPerCashUpdate", FieldType.Integer, Min: 0, Source: "HackInternetAIUpdate.h"),
        ], allowsNestedSections: true);
        // TensileFormationUpdateModuleData (TensileFormationUpdate.cpp): the avalanche's enabled flag and the crack sound (presentation).
        Add("TensileFormationUpdate",
        [
            new("Enabled", FieldType.Bool, Source: "TensileFormationUpdate.cpp"),
            new("CrackSound", FieldType.Opaque, Source: "TensileFormationUpdate.cpp"),
        ]);

        // ---- Air group: aircraft, airfields, missiles and pulse objects (src/CnCNew.Simulation/Modules/BuiltIn/AirModules.cs, AirWeaponModules.cs).
        // JetAIUpdateModuleData (JetAIUpdate.cpp buildFieldParse) on top of the AIUpdate base; the nested Turret block of the Comanche is allowed (typed by TurretSchema; a turret without a turn rate does not gate fire, as in the reference).
        Add("JetAIUpdate",
        [
            ..AiBase("JetAIUpdate.cpp / AIUpdate.cpp"),
            new("OutOfAmmoDamagePerSecond", FieldType.Percent, Source: "JetAIUpdate.cpp"),
            new("NeedsRunway", FieldType.Bool, Source: "JetAIUpdate.cpp"),
            new("KeepsParkingSpaceWhenAirborne", FieldType.Bool, Source: "JetAIUpdate.cpp"),
            new("TakeoffDistForMaxLift", FieldType.Percent, Source: "JetAIUpdate.cpp"),
            new("TakeoffPause", FieldType.DurationMs, Source: "JetAIUpdate.cpp"),
            Real("MinHeight", "JetAIUpdate.cpp"),
            Real("ParkingOffset", "JetAIUpdate.cpp"),
            Real("SneakyOffsetWhenAttacking", "JetAIUpdate.cpp"),
            new("AttackLocomotorType", FieldType.Enum, Table: NameTables.LocomotorSet, Source: "JetAIUpdate.cpp"),
            new("AttackLocomotorPersistTime", FieldType.DurationMs, Source: "JetAIUpdate.cpp"),
            new("AttackersMissPersistTime", FieldType.DurationMs, Source: "JetAIUpdate.cpp"),
            new("ReturnForAmmoLocomotorType", FieldType.Enum, Table: NameTables.LocomotorSet, Source: "JetAIUpdate.cpp"),
            new("LockonTime", FieldType.DurationMs, Source: "JetAIUpdate.cpp"),
            new("LockonCursor", FieldType.Reference, ReferenceKind: DefinitionKind.Object, Source: "JetAIUpdate.cpp"),
            Real("LockonInitialDist", "JetAIUpdate.cpp"),
            Real("LockonFreq", "JetAIUpdate.cpp"),
            new("LockonAngleSpin", FieldType.Angle, Source: "JetAIUpdate.cpp"),
            new("LockonBlinky", FieldType.Bool, Source: "JetAIUpdate.cpp"),
            new("ReturnToBaseIdleTime", FieldType.DurationMs, Source: "JetAIUpdate.cpp"),
        ], allowsNestedSections: true);
        // ParkingPlaceBehaviorModuleData (ParkingPlaceBehavior.h): the airfield's parking rows/columns, runways and healing. The bone positions of the model have no counterpart; the layout is computed.
        Add("ParkingPlaceBehavior",
        [
            new("NumRows", FieldType.Integer, Min: 0, Source: "ParkingPlaceBehavior.h"),
            new("NumCols", FieldType.Integer, Min: 0, Source: "ParkingPlaceBehavior.h"),
            Real("ApproachHeight", "ParkingPlaceBehavior.h"),
            Real("LandingDeckHeightOffset", "ParkingPlaceBehavior.h"),
            new("HasRunways", FieldType.Bool, Source: "ParkingPlaceBehavior.h"),
            new("ParkInHangars", FieldType.Bool, Source: "ParkingPlaceBehavior.h"),
            Real("HealAmountPerSecond", "ParkingPlaceBehavior.h"),
        ]);
        // SpectreGunshipUpdateModuleData (SpectreGunshipUpdate.cpp buildFieldParse); the two radius decals are nested blocks (presentation).
        Add("SpectreGunshipUpdate",
        [
            new("SpecialPowerTemplate", FieldType.Reference, ReferenceKind: DefinitionKind.SpecialPower, Source: "SpectreGunshipUpdate.cpp"),
            new("GattlingTemplateName", FieldType.Reference, ReferenceKind: DefinitionKind.Object, Source: "SpectreGunshipUpdate.cpp"),
            new("HowitzerFiringRate", FieldType.DurationMs, Source: "SpectreGunshipUpdate.cpp"),
            new("OrbitTime", FieldType.DurationMs, Source: "SpectreGunshipUpdate.cpp"),
            new("HowitzerFollowLag", FieldType.DurationMs, Source: "SpectreGunshipUpdate.cpp"),
            Real("AttackAreaRadius", "SpectreGunshipUpdate.cpp"),
            Real("StrafingIncrement", "SpectreGunshipUpdate.cpp"),
            Real("OrbitInsertionSlope", "SpectreGunshipUpdate.cpp"),
            Real("RandomOffsetForHowitzer", "SpectreGunshipUpdate.cpp"),
            Real("TargetingReticleRadius", "SpectreGunshipUpdate.cpp"),
            Real("GunshipOrbitRadius", "SpectreGunshipUpdate.cpp"),
            new("HowitzerWeaponTemplate", FieldType.Reference, ReferenceKind: DefinitionKind.Weapon, Source: "SpectreGunshipUpdate.cpp"),
            new("GattlingStrafeFXParticleSystem", FieldType.Opaque, Source: "SpectreGunshipUpdate.cpp"),
        ], allowsNestedSections: true);
        // MissileLauncherBuildingUpdateModuleData (MissileLauncherBuildingUpdate.h): the silo door timing and its effects at each door state.
        Add("MissileLauncherBuildingUpdate",
        [
            new("SpecialPowerTemplate", FieldType.Reference, ReferenceKind: DefinitionKind.SpecialPower, Source: "MissileLauncherBuildingUpdate.h"),
            new("DoorOpenTime", FieldType.DurationMs, Source: "MissileLauncherBuildingUpdate.h"),
            new("DoorWaitOpenTime", FieldType.DurationMs, Source: "MissileLauncherBuildingUpdate.h"),
            new("DoorCloseTime", FieldType.DurationMs, Source: "MissileLauncherBuildingUpdate.h"),
            new("DoorOpeningFX", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "MissileLauncherBuildingUpdate.h"),
            new("DoorOpenFX", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "MissileLauncherBuildingUpdate.h"),
            new("DoorWaitingToCloseFX", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "MissileLauncherBuildingUpdate.h"),
            new("DoorClosingFX", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "MissileLauncherBuildingUpdate.h"),
            new("DoorClosedFX", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "MissileLauncherBuildingUpdate.h"),
            new("DoorOpenIdleAudio", FieldType.Opaque, Source: "MissileLauncherBuildingUpdate.h"),
        ]);
        // NeutronMissileUpdateModuleData (NeutronMissileUpdate.cpp buildFieldParse); the delivery decal is a nested block (presentation).
        Add("NeutronMissileUpdate",
        [
            Real("DistanceToTravelBeforeTurning", "NeutronMissileUpdate.cpp"),
            Real("MaxTurnRate", "NeutronMissileUpdate.cpp parseAngularVelocityReal"),
            Real("ForwardDamping", "NeutronMissileUpdate.cpp"),
            Real("RelativeSpeed", "NeutronMissileUpdate.cpp"),
            Real("TargetFromDirectlyAbove", "NeutronMissileUpdate.cpp"),
            new("LaunchFX", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "NeutronMissileUpdate.cpp"),
            new("SpecialSpeedTime", FieldType.DurationMs, Source: "NeutronMissileUpdate.cpp"),
            Real("SpecialSpeedHeight", "NeutronMissileUpdate.cpp"),
            Real("SpecialAccelFactor", "NeutronMissileUpdate.cpp"),
            Real("SpecialJitterDistance", "NeutronMissileUpdate.cpp"),
            new("IgnitionFX", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "NeutronMissileUpdate.cpp"),
            Real("DeliveryDecalRadius", "NeutronMissileUpdate.cpp"),
        ], allowsNestedSections: true);
        // NeutronMissileSlowDeathBehaviorModuleData (NeutronMissileSlowDeathUpdate.cpp buildFieldParse): the SlowDeathBehavior base plus nine blast stages.
        Add("NeutronMissileSlowDeathBehavior",
        [
            ..DieMux(),
            new("SinkRate", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("ProbabilityModifier", FieldType.Integer, Source: "SlowDeathBehavior.cpp"),
            new("ModifierBonusPerOverkillPercent", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("SinkDelay", FieldType.DurationMs, Source: "SlowDeathBehavior.cpp"),
            new("SinkDelayVariance", FieldType.DurationMs, Source: "SlowDeathBehavior.cpp"),
            new("DestructionDelay", FieldType.DurationMs, Source: "SlowDeathBehavior.cpp"),
            new("DestructionDelayVariance", FieldType.DurationMs, Source: "SlowDeathBehavior.cpp"),
            new("DestructionAltitude", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("FX", FieldType.Opaque, Repeat: RepeatPolicy.Accumulate, Source: "SlowDeathBehavior.cpp"),
            new("OCL", FieldType.Opaque, Repeat: RepeatPolicy.Accumulate, Source: "SlowDeathBehavior.cpp"),
            new("Weapon", FieldType.Opaque, Repeat: RepeatPolicy.Accumulate, Source: "SlowDeathBehavior.cpp"),
            new("FlingForce", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("FlingForceVariance", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("FlingPitch", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            new("FlingPitchVariance", FieldType.Opaque, Source: "SlowDeathBehavior.cpp"),
            Real("ScorchMarkSize", "NeutronMissileSlowDeathUpdate.cpp"),
            new("FXList", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "NeutronMissileSlowDeathUpdate.cpp"),
            ..Enumerable.Range(1, 9).SelectMany(stage => new FieldSchema[]
            {
                new($"Blast{stage}Enabled", FieldType.Bool, Source: "NeutronMissileSlowDeathUpdate.cpp"),
                new($"Blast{stage}Delay", FieldType.DurationMs, Source: "NeutronMissileSlowDeathUpdate.cpp parseDurationReal"),
                new($"Blast{stage}ScorchDelay", FieldType.DurationMs, Source: "NeutronMissileSlowDeathUpdate.cpp parseDurationReal"),
                Real($"Blast{stage}InnerRadius", "NeutronMissileSlowDeathUpdate.cpp"),
                Real($"Blast{stage}OuterRadius", "NeutronMissileSlowDeathUpdate.cpp"),
                Real($"Blast{stage}MaxDamage", "NeutronMissileSlowDeathUpdate.cpp"),
                Real($"Blast{stage}MinDamage", "NeutronMissileSlowDeathUpdate.cpp"),
                Real($"Blast{stage}ToppleSpeed", "NeutronMissileSlowDeathUpdate.cpp"),
                Real($"Blast{stage}PushForce", "NeutronMissileSlowDeathUpdate.cpp"),
            }),
        ]);
        // NeutronBlastBehaviorModuleData (NeutronBlastBehavior.h).
        Add("NeutronBlastBehavior",
        [
            Real("BlastRadius", "NeutronBlastBehavior.h"),
            new("AffectAirborne", FieldType.Bool, Source: "NeutronBlastBehavior.h"),
            new("AffectAllies", FieldType.Bool, Source: "NeutronBlastBehavior.h"),
        ]);
        // EMPUpdateModuleData (EMPUpdate.h): the growth/fade of the pulse object and what it disables. Colors, scale, the spark system and the victim kind filter (commented out in the update) are presentation.
        Add("EMPUpdate",
        [
            new("Lifetime", FieldType.DurationMs, Source: "EMPUpdate.h"),
            new("StartFadeTime", FieldType.DurationMs, Source: "EMPUpdate.h"),
            Real("StartScale", "EMPUpdate.h"),
            new("DisabledDuration", FieldType.DurationMs, Source: "EMPUpdate.h"),
            Real("TargetScaleMax", "EMPUpdate.h"),
            Real("TargetScaleMin", "EMPUpdate.h"),
            new("StartColor", FieldType.Opaque, Source: "EMPUpdate.h"),
            new("EndColor", FieldType.Opaque, Source: "EMPUpdate.h"),
            new("DisableFXParticleSystem", FieldType.Opaque, Source: "EMPUpdate.h"),
            Real("SparksPerCubicFoot", "EMPUpdate.h"),
            Real("EffectRadius", "EMPUpdate.h"),
            new("DoesNotAffect", FieldType.Flags, Table: NameTablesPhase5.WeaponAffectsMask, Source: "EMPUpdate.h"),
            new("DoesNotAffectMyOwnBuildings", FieldType.Bool, Source: "EMPUpdate.h"),
            new("VictimRequiredKindOf", FieldType.Flags, Table: NameTables.KindOf, Source: "EMPUpdate.h"),
            new("VictimForbiddenKindOf", FieldType.Flags, Table: NameTables.KindOf, Source: "EMPUpdate.h"),
        ]);
        // LeafletDropBehaviorModuleData (EMPUpdate.h).
        Add("LeafletDropBehavior",
        [
            new("Delay", FieldType.DurationMs, Source: "EMPUpdate.h"),
            new("DisabledDuration", FieldType.DurationMs, Source: "EMPUpdate.h"),
            Real("AffectRadius", "EMPUpdate.h"),
            new("LeafletFXParticleSystem", FieldType.Opaque, Source: "EMPUpdate.h"),
        ]);
        // BunkerBusterBehaviorModuleData (BunkerBusterBehavior.cpp buildFieldParse).
        Add("BunkerBusterBehavior",
        [
            new("UpgradeRequired", FieldType.Reference, ReferenceKind: DefinitionKind.Upgrade, Source: "BunkerBusterBehavior.cpp"),
            new("DetonationFX", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "BunkerBusterBehavior.cpp"),
            new("CrashThroughBunkerFX", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "BunkerBusterBehavior.cpp"),
            new("CrashThroughBunkerFXFrequency", FieldType.DurationMs, Source: "BunkerBusterBehavior.cpp"),
            Real("SeismicEffectRadius", "BunkerBusterBehavior.cpp"),
            Real("SeismicEffectMagnitude", "BunkerBusterBehavior.cpp"),
            new("ShockwaveWeaponTemplate", FieldType.Reference, ReferenceKind: DefinitionKind.Weapon, Source: "BunkerBusterBehavior.cpp"),
            new("OccupantDamageWeaponTemplate", FieldType.Reference, ReferenceKind: DefinitionKind.Weapon, Source: "BunkerBusterBehavior.cpp"),
        ]);

        // ===== BEGIN crates, collide modules and specials (CrateModules.cs, SpecialsModules.cs) =====
        // CrateCollide.cpp CrateCollideModuleData: the fields every *CrateCollide shares. ExecuteAnimation* drive a screen-space 2D animation (presentation, typed, no consumer).
        ImmutableArray<FieldSchema> CrateCollide() =>
        [
            new("RequiredKindOf", FieldType.Flags, Table: NameTables.KindOf, Source: "CrateCollide.cpp"),
            new("ForbiddenKindOf", FieldType.Flags, Table: NameTables.KindOf, Source: "CrateCollide.cpp"),
            new("ForbidOwnerPlayer", FieldType.Bool, Source: "CrateCollide.cpp"),
            new("BuildingPickup", FieldType.Bool, Source: "CrateCollide.cpp"),
            new("HumanOnly", FieldType.Bool, Source: "CrateCollide.cpp"),
            new("PickupScience", FieldType.Reference, ReferenceKind: DefinitionKind.Science, Source: "CrateCollide.cpp"),
            new("ExecuteFX", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "CrateCollide.cpp"),
            new("ExecuteAnimation", FieldType.Text, Source: "CrateCollide.cpp"),
            Real("ExecuteAnimationTime", "CrateCollide.cpp"),
            Real("ExecuteAnimationZRise", "CrateCollide.cpp"),
            new("ExecuteAnimationFades", FieldType.Bool, Source: "CrateCollide.cpp"),
        ];
        Add("MoneyCrateCollide",
        [
            ..CrateCollide(),
            new("MoneyProvided", FieldType.Integer, Min: 0, Source: "MoneyCrateCollide.h"),
            // "UpgradeType:<name> Boost:<int>" (parseUpgradePair), repeatable: a token pair the generic typer cannot read, parsed by the binding like AutoDepositUpdate's.
            new("UpgradedBoost", FieldType.Opaque, Repeat: RepeatPolicy.Accumulate, Source: "MoneyCrateCollide.h"),
        ]);
        Add("VeterancyCrateCollide",
        [
            ..CrateCollide(),
            new("EffectRange", FieldType.Integer, Min: 0, Source: "VeterancyCrateCollide.h"),
            new("AddsOwnerVeterancy", FieldType.Bool, Source: "VeterancyCrateCollide.h"),
            new("IsPilot", FieldType.Bool, Source: "VeterancyCrateCollide.h"),
        ]);
        Add("ConvertToCarBombCrateCollide", [..CrateCollide(), new("FXList", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "ConvertToCarBombCrateCollide.h")]);
        Add("ConvertToHijackedVehicleCrateCollide", CrateCollide());
        Add("HijackerUpdate",
        [
            new("AttachToTargetBone", FieldType.Text, Source: "HijackerUpdate.h"),
            new("ParachuteName", FieldType.Reference, ReferenceKind: DefinitionKind.Object, Source: "HijackerUpdate.h"),
        ]);
        Add("SabotageCommandCenterCrateCollide", CrateCollide());
        Add("SabotageSuperweaponCrateCollide", CrateCollide());
        Add("SabotageMilitaryFactoryCrateCollide", [..CrateCollide(), new("SabotageDuration", FieldType.DurationMs, Source: "SabotageMilitaryFactoryCrateCollide.h")]);
        Add("SabotagePowerPlantCrateCollide", [..CrateCollide(), new("SabotagePowerDuration", FieldType.DurationMs, Source: "SabotagePowerPlantCrateCollide.h")]);
        Add("SabotageSupplyCenterCrateCollide", [..CrateCollide(), new("StealCashAmount", FieldType.Integer, Min: 0, Source: "SabotageSupplyCenterCrateCollide.h")]);
        Add("PilotFindVehicleUpdate",
        [
            new("ScanRate", FieldType.DurationMs, Source: "PilotFindVehicleUpdate.cpp"),
            Real("ScanRange", "PilotFindVehicleUpdate.cpp"),
            Real("MinHealth", "PilotFindVehicleUpdate.cpp"),
        ]);
        Add("StickyBombUpdate",
        [
            new("AttachToTargetBone", FieldType.Text, Source: "StickyBombUpdate.h"),
            Real("OffsetZ", "StickyBombUpdate.h"),
            new("GeometryBasedDamageWeapon", FieldType.Reference, ReferenceKind: DefinitionKind.Weapon, Source: "StickyBombUpdate.h"),
            new("GeometryBasedDamageFX", FieldType.Reference, ReferenceKind: DefinitionKind.FxList, Source: "StickyBombUpdate.h"),
        ]);
        Add("GrantStealthBehavior",
        [
            Real("StartRadius", "GrantStealthBehavior.cpp"),
            Real("FinalRadius", "GrantStealthBehavior.cpp"),
            Real("RadiusGrowRate", "GrantStealthBehavior.cpp"),
            new("RadiusParticleSystemName", FieldType.Opaque, Source: "GrantStealthBehavior.cpp"),
            new("KindOf", FieldType.Flags, Table: NameTables.KindOf, Source: "GrantStealthBehavior.cpp"),
        ]);
        Add("WanderAIUpdate", AiBase("AIUpdate.cpp"), allowsNestedSections: true);
        Add("ProjectileStreamUpdate", []);
        Add("CleanupHazardUpdate",
        [
            new("WeaponSlot", FieldType.Enum, Table: NameTablesPhase5.WeaponSlot, Source: "CleanupHazardUpdate.cpp"),
            new("ScanRate", FieldType.DurationMs, Source: "CleanupHazardUpdate.cpp"),
            Real("ScanRange", "CleanupHazardUpdate.cpp"),
        ]);
        Add("CleanupAreaPower", [..SpecialPowerBase(), Real("MaxMoveDistanceFromLocation", "CleanupAreaPower.cpp")]);
        Add("SpyVisionUpdate",
        [
            ..UpgradeMux(),
            new("NeedsUpgrade", FieldType.Bool, Source: "SpyVisionUpdate.h"),
            new("SelfPowered", FieldType.Bool, Source: "SpyVisionUpdate.h"),
            new("SelfPoweredDuration", FieldType.DurationMs, Source: "SpyVisionUpdate.h"),
            new("SelfPoweredInterval", FieldType.DurationMs, Source: "SpyVisionUpdate.h"),
            new("SpyOnKindof", FieldType.Flags, Table: NameTables.KindOf, Source: "SpyVisionUpdate.h"),
        ]);
        Add("SpyVisionSpecialPower",
        [
            ..SpecialPowerBase(),
            new("BaseDuration", FieldType.DurationMs, Source: "SpyVisionSpecialPower.cpp"),
            new("BonusDurationPerCaptured", FieldType.DurationMs, Source: "SpyVisionSpecialPower.cpp"),
            new("MaxDuration", FieldType.DurationMs, Source: "SpyVisionSpecialPower.cpp"),
        ]);
        Add("AssistedTargetingUpdate",
        [
            new("AssistingClipSize", FieldType.Integer, Min: 0, Source: "AssistedTargetingUpdate.cpp"),
            new("AssistingWeaponSlot", FieldType.Enum, Table: NameTablesPhase5.WeaponSlot, Source: "AssistedTargetingUpdate.cpp"),
            new("LaserFromAssisted", FieldType.Text, Source: "AssistedTargetingUpdate.cpp"),
            new("LaserToTarget", FieldType.Text, Source: "AssistedTargetingUpdate.cpp"),
        ]);
        // BattlePlanUpdateModuleData (BattlePlanUpdate.cpp buildFieldParse). Messages, sounds and announcements are presentation (typed, no consumer); the rest drives the simulated plans.
        Add("BattlePlanUpdate",
        [
            new("SpecialPowerTemplate", FieldType.Reference, ReferenceKind: DefinitionKind.SpecialPower, Source: "BattlePlanUpdate.cpp"),
            new("BombardmentPlanAnimationTime", FieldType.DurationMs, Source: "BattlePlanUpdate.cpp"),
            new("HoldTheLinePlanAnimationTime", FieldType.DurationMs, Source: "BattlePlanUpdate.cpp"),
            new("SearchAndDestroyPlanAnimationTime", FieldType.DurationMs, Source: "BattlePlanUpdate.cpp"),
            new("TransitionIdleTime", FieldType.DurationMs, Source: "BattlePlanUpdate.cpp"),
            new("BombardmentPlanUnpackSoundName", FieldType.Text, Source: "BattlePlanUpdate.cpp"),
            new("BombardmentPlanPackSoundName", FieldType.Text, Source: "BattlePlanUpdate.cpp"),
            new("BombardmentMessageLabel", FieldType.Text, Source: "BattlePlanUpdate.cpp"),
            new("BombardmentAnnouncementName", FieldType.Text, Source: "BattlePlanUpdate.cpp"),
            new("SearchAndDestroyPlanUnpackSoundName", FieldType.Text, Source: "BattlePlanUpdate.cpp"),
            new("SearchAndDestroyPlanIdleLoopSoundName", FieldType.Text, Source: "BattlePlanUpdate.cpp"),
            new("SearchAndDestroyPlanPackSoundName", FieldType.Text, Source: "BattlePlanUpdate.cpp"),
            new("SearchAndDestroyMessageLabel", FieldType.Text, Source: "BattlePlanUpdate.cpp"),
            new("SearchAndDestroyAnnouncementName", FieldType.Text, Source: "BattlePlanUpdate.cpp"),
            new("HoldTheLinePlanUnpackSoundName", FieldType.Text, Source: "BattlePlanUpdate.cpp"),
            new("HoldTheLinePlanPackSoundName", FieldType.Text, Source: "BattlePlanUpdate.cpp"),
            new("HoldTheLineMessageLabel", FieldType.Text, Source: "BattlePlanUpdate.cpp"),
            new("HoldTheLineAnnouncementName", FieldType.Text, Source: "BattlePlanUpdate.cpp"),
            new("ValidMemberKindOf", FieldType.Flags, Table: NameTables.KindOf, Source: "BattlePlanUpdate.cpp"),
            new("InvalidMemberKindOf", FieldType.Flags, Table: NameTables.KindOf, Source: "BattlePlanUpdate.cpp"),
            new("BattlePlanChangeParalyzeTime", FieldType.DurationMs, Source: "BattlePlanUpdate.cpp"),
            Real("HoldTheLinePlanArmorDamageScalar", "BattlePlanUpdate.cpp"),
            Real("SearchAndDestroyPlanSightRangeScalar", "BattlePlanUpdate.cpp"),
            Real("StrategyCenterSearchAndDestroySightRangeScalar", "BattlePlanUpdate.cpp"),
            new("StrategyCenterSearchAndDestroyDetectsStealth", FieldType.Bool, Source: "BattlePlanUpdate.cpp"),
            Real("StrategyCenterHoldTheLineMaxHealthScalar", "BattlePlanUpdate.cpp"),
            new("StrategyCenterHoldTheLineMaxHealthChangeType", FieldType.Enum, Table: NameTablesPhase5.MaxHealthChangeType, Source: "BattlePlanUpdate.cpp"),
            new("VisionObjectName", FieldType.Text, Source: "BattlePlanUpdate.cpp"),
        ]);
        // ===== END crates, collide modules and specials =====

        // ------------------------------------------------------------------------------------------ structures and world group
        // Field lists from the reference module-data tables named in each Source, checked against every occurrence in the shipped INIs.
        Add("UpgradeDie", [..DieMux(), new("UpgradeToRemove", FieldType.Reference, ReferenceKind: DefinitionKind.Upgrade, Source: "UpgradeDie.h")]);
        Add("DamDie", DieMux());
        // StructureCollapseUpdate.cpp: <phase> <name> ... lines (the phase is INITIAL, DELAY, BURST or FINAL) are parsed by the module.
        Add("StructureCollapseUpdate",
        [
            ..DieMux(),
            new("MinCollapseDelay", FieldType.DurationMs, Source: "StructureCollapseUpdate.cpp"),
            new("MaxCollapseDelay", FieldType.DurationMs, Source: "StructureCollapseUpdate.cpp"),
            new("MinBurstDelay", FieldType.DurationMs, Source: "StructureCollapseUpdate.cpp"),
            new("MaxBurstDelay", FieldType.DurationMs, Source: "StructureCollapseUpdate.cpp"),
            Real("CollapseDamping", "StructureCollapseUpdate.cpp"),
            Real("MaxShudder", "StructureCollapseUpdate.cpp"),
            new("BigBurstFrequency", FieldType.Integer, Source: "StructureCollapseUpdate.cpp"),
            new("OCL", FieldType.Opaque, Repeat: RepeatPolicy.Accumulate, Source: "StructureCollapseUpdate.cpp"),
            new("FXList", FieldType.Opaque, Repeat: RepeatPolicy.Accumulate, Source: "StructureCollapseUpdate.cpp"),
        ]);
        // DeployStyleAIUpdate.h on top of the AIUpdate base: the Turret block is a nested section typed by TurretSchema (turrets are simulated, docs/SIM-TURRET-AIM.md; the deploy-only options stay as before).
        Add("DeployStyleAIUpdate",
        [
            ..AiBase("DeployStyleAIUpdate.cpp / AIUpdate.cpp"),
            new("UnpackTime", FieldType.DurationMs, Source: "DeployStyleAIUpdate.h"),
            new("PackTime", FieldType.DurationMs, Source: "DeployStyleAIUpdate.h"),
            new("ResetTurretBeforePacking", FieldType.Bool, Source: "DeployStyleAIUpdate.h"),
            new("TurretsFunctionOnlyWhenDeployed", FieldType.Bool, Source: "DeployStyleAIUpdate.h"),
            new("TurretsMustCenterBeforePacking", FieldType.Bool, Source: "DeployStyleAIUpdate.h"),
            new("ManualDeployAnimations", FieldType.Bool, Source: "DeployStyleAIUpdate.h"),
        ], allowsNestedSections: true);
        Add("FireWeaponCollide",
        [
            new("CollideWeapon", FieldType.Reference, ReferenceKind: DefinitionKind.Weapon, Source: "FireWeaponCollide.h"),
            new("FireOnce", FieldType.Bool, Source: "FireWeaponCollide.cpp"),
            new("RequiredStatus", FieldType.Flags, Table: NameTables.ObjectStatus, Source: "FireWeaponCollide.cpp"),
            new("ForbiddenStatus", FieldType.Flags, Table: NameTables.ObjectStatus, Source: "FireWeaponCollide.cpp"),
        ]);
        Add("FireSpreadUpdate",
        [
            new("OCLEmbers", FieldType.Reference, ReferenceKind: DefinitionKind.ObjectCreationList, Source: "FireSpreadUpdate.cpp"),
            new("MinSpreadDelay", FieldType.DurationMs, Source: "FireSpreadUpdate.cpp"),
            new("MaxSpreadDelay", FieldType.DurationMs, Source: "FireSpreadUpdate.cpp"),
            Real("SpreadTryRange", "FireSpreadUpdate.cpp"),
        ]);
        // RailroadGuideAIUpdate.h (RailroadBehaviorModuleData): PhysicsBehavior's own keys never occur on a train and are not part of the schema.
        Add("RailroadBehavior",
        [
            new("PathPrefixName", FieldType.Text, Source: "RailroadGuideAIUpdate.h"),
            new("CrashFXTemplateName", FieldType.Text, Source: "RailroadGuideAIUpdate.h"),
            new("IsLocomotive", FieldType.Bool, Source: "RailroadGuideAIUpdate.h"),
            new("CarriageTemplateName", FieldType.ReferenceList, Repeat: RepeatPolicy.Accumulate, ReferenceKind: DefinitionKind.Object, Source: "RailroadGuideAIUpdate.h"),
            new("BigMetalBounceSound", FieldType.Opaque, Source: "RailroadGuideAIUpdate.h"),
            new("SmallMetalBounceSound", FieldType.Opaque, Source: "RailroadGuideAIUpdate.h"),
            new("MeatyBounceSound", FieldType.Opaque, Source: "RailroadGuideAIUpdate.h"),
            Real("RunningGarrisonSpeedMax", "RailroadGuideAIUpdate.h"),
            Real("KillSpeedMin", "RailroadGuideAIUpdate.h"),
            Real("SpeedMax", "RailroadGuideAIUpdate.h"),
            Real("Acceleration", "RailroadGuideAIUpdate.h"),
            Real("Braking", "RailroadGuideAIUpdate.h"),
            new("WaitAtStationTime", FieldType.DurationMs, Source: "RailroadGuideAIUpdate.h"),
            new("RunningSound", FieldType.Opaque, Source: "RailroadGuideAIUpdate.h"),
            new("ClicketyClackSound", FieldType.Opaque, Source: "RailroadGuideAIUpdate.h"),
            new("WhistleSound", FieldType.Opaque, Source: "RailroadGuideAIUpdate.h"),
            Real("Friction", "RailroadGuideAIUpdate.h"),
        ]);
        // BridgeBehavior.cpp: <FX|OCL>:<name> Delay:<ms> [Bone:<name>] lines are parsed by the module; the scaffold speeds drive the (unsimulated) repair scaffolding.
        Add("BridgeBehavior",
        [
            Real("LateralScaffoldSpeed", "BridgeBehavior.cpp"),
            Real("VerticalScaffoldSpeed", "BridgeBehavior.cpp"),
            new("BridgeDieFX", FieldType.Opaque, Repeat: RepeatPolicy.Accumulate, Source: "BridgeBehavior.cpp"),
            new("BridgeDieOCL", FieldType.Opaque, Repeat: RepeatPolicy.Accumulate, Source: "BridgeBehavior.cpp"),
        ]);
        Add("BridgeTowerBehavior", []);
        Add("BridgeScaffoldBehavior", []);
        Add("DemoTrapUpdate",
        [
            new("DefaultProximityMode", FieldType.Bool, Source: "DemoTrapUpdate.cpp"),
            new("DetonationWeaponSlot", FieldType.Enum, Table: NameTablesPhase5.WeaponSlot, Source: "DemoTrapUpdate.cpp"),
            new("ProximityModeWeaponSlot", FieldType.Enum, Table: NameTablesPhase5.WeaponSlot, Source: "DemoTrapUpdate.cpp"),
            new("ManualModeWeaponSlot", FieldType.Enum, Table: NameTablesPhase5.WeaponSlot, Source: "DemoTrapUpdate.cpp"),
            Real("TriggerDetonationRange", "DemoTrapUpdate.cpp"),
            new("IgnoreTargetTypes", FieldType.Flags, Table: NameTables.KindOf, Source: "DemoTrapUpdate.cpp"),
            new("ScanRate", FieldType.DurationMs, Source: "DemoTrapUpdate.cpp"),
            new("AutoDetonationWithFriendsInvolved", FieldType.Bool, Source: "DemoTrapUpdate.cpp"),
            new("DetonationWeapon", FieldType.Reference, ReferenceKind: DefinitionKind.Weapon, Source: "DemoTrapUpdate.cpp"),
            new("DetonateWhenKilled", FieldType.Bool, Source: "DemoTrapUpdate.cpp"),
        ]);
        Add("MinefieldBehavior",
        [
            new("DetonationWeapon", FieldType.Reference, ReferenceKind: DefinitionKind.Weapon, Source: "MinefieldBehavior.cpp"),
            new("DetonatedBy", FieldType.Flags, Table: NameTablesStructures.Relationship, Source: "MinefieldBehavior.cpp"),
            new("StopsRegenAfterCreatorDies", FieldType.Bool, Source: "MinefieldBehavior.cpp"),
            new("Regenerates", FieldType.Bool, Source: "MinefieldBehavior.cpp"),
            new("WorkersDetonate", FieldType.Bool, Source: "MinefieldBehavior.cpp"),
            new("CreatorDeathCheckRate", FieldType.DurationMs, Source: "MinefieldBehavior.cpp"),
            new("ScootFromStartingPointTime", FieldType.DurationMs, Source: "MinefieldBehavior.cpp"),
            new("NumVirtualMines", FieldType.Integer, Min: 1, Source: "MinefieldBehavior.cpp"),
            Real("RepeatDetonateMoveThresh", "MinefieldBehavior.cpp"),
            new("DegenPercentPerSecondAfterCreatorDies", FieldType.Percent, Source: "MinefieldBehavior.cpp"),
            new("CreationList", FieldType.Reference, ReferenceKind: DefinitionKind.ObjectCreationList, Source: "MinefieldBehavior.cpp"),
        ]);
        // OCLUpdate.cpp: FactionOCL is "Faction:<side> OCL:<name>" and is parsed by the module.
        Add("OCLUpdate",
        [
            new("OCL", FieldType.Reference, ReferenceKind: DefinitionKind.ObjectCreationList, Source: "OCLUpdate.cpp"),
            new("FactionOCL", FieldType.Opaque, Repeat: RepeatPolicy.Accumulate, Source: "OCLUpdate.cpp"),
            new("MinDelay", FieldType.DurationMs, Source: "OCLUpdate.cpp"),
            new("MaxDelay", FieldType.DurationMs, Source: "OCLUpdate.cpp"),
            new("CreateAtEdge", FieldType.Bool, Source: "OCLUpdate.cpp"),
            new("FactionTriggered", FieldType.Bool, Source: "OCLUpdate.cpp"),
        ]);
        // DynamicShroudClearingRangeUpdate.cpp: GridDecalTemplate is a nested decal section (presentation).
        Add("DynamicShroudClearingRangeUpdate",
        [
            new("ChangeInterval", FieldType.DurationMs, Source: "DynamicShroudClearingRangeUpdate.cpp"),
            new("GrowInterval", FieldType.DurationMs, Source: "DynamicShroudClearingRangeUpdate.cpp"),
            new("ShrinkDelay", FieldType.DurationMs, Source: "DynamicShroudClearingRangeUpdate.cpp"),
            new("ShrinkTime", FieldType.DurationMs, Source: "DynamicShroudClearingRangeUpdate.cpp"),
            new("GrowDelay", FieldType.DurationMs, Source: "DynamicShroudClearingRangeUpdate.cpp"),
            new("GrowTime", FieldType.DurationMs, Source: "DynamicShroudClearingRangeUpdate.cpp"),
            Real("FinalVision", "DynamicShroudClearingRangeUpdate.cpp"),
        ], allowsNestedSections: true);
        Add("FireOCLAfterWeaponCooldownUpdate",
        [
            ..UpgradeMux(),
            new("WeaponSlot", FieldType.Enum, Table: NameTablesPhase5.WeaponSlot, Source: "FireOCLAfterWeaponCooldownUpdate.cpp"),
            new("OCL", FieldType.Reference, ReferenceKind: DefinitionKind.ObjectCreationList, Source: "FireOCLAfterWeaponCooldownUpdate.cpp"),
            new("MinShotsToCreateOCL", FieldType.Integer, Min: 0, Source: "FireOCLAfterWeaponCooldownUpdate.cpp"),
            new("OCLLifetimePerSecond", FieldType.Integer, Min: 0, Source: "FireOCLAfterWeaponCooldownUpdate.cpp"),
            new("OCLLifetimeMaxCap", FieldType.DurationMs, Source: "FireOCLAfterWeaponCooldownUpdate.cpp"),
        ]);
        Add("FloatUpdate", [new("Enabled", FieldType.Bool, Source: "FloatUpdate.cpp")]);
        Add("WaveGuideUpdate",
        [
            new("WaveDelay", FieldType.DurationMs, Source: "WaveGuideUpdate.cpp"),
            Real("YSize", "WaveGuideUpdate.cpp"),
            Real("LinearWaveSpacing", "WaveGuideUpdate.cpp"),
            Real("WaveBendMagnitude", "WaveGuideUpdate.cpp"),
            Real("WaterVelocity", "WaveGuideUpdate.cpp"),
            Real("PreferredHeight", "WaveGuideUpdate.cpp"),
            Real("ShorelineEffectDistance", "WaveGuideUpdate.cpp"),
            Real("DamageRadius", "WaveGuideUpdate.cpp"),
            Real("DamageAmount", "WaveGuideUpdate.cpp"),
            Real("ToppleForce", "WaveGuideUpdate.cpp"),
            new("RandomSplashSound", FieldType.Opaque, Source: "WaveGuideUpdate.cpp"),
            new("RandomSplashSoundFrequency", FieldType.Integer, Source: "WaveGuideUpdate.cpp"),
            new("BridgeParticle", FieldType.Opaque, Source: "WaveGuideUpdate.cpp"),
            Real("BridgeParticleAngleFudge", "WaveGuideUpdate.cpp"),
            new("LoopingSound", FieldType.Opaque, Source: "WaveGuideUpdate.cpp"),
        ]);
        Add("CheckpointUpdate", [new("ScanDelayTime", FieldType.DurationMs, Source: "CheckpointUpdate.h")]);
        Add("EnemyNearUpdate", [new("ScanDelayTime", FieldType.DurationMs, Source: "EnemyNearUpdate.h")]);
        {
            // DynamicGeometryInfoUpdate.cpp fields plus FirestormDynamicGeometryInfoUpdate.cpp's; the particle systems, FX list, offset and scorch are presentation.
            var firestorm = ImmutableArray.CreateBuilder<FieldSchema>();
            firestorm.Add(new("InitialDelay", FieldType.DurationMs, Source: "DynamicGeometryInfoUpdate.cpp"));
            firestorm.Add(Real("InitialHeight", "DynamicGeometryInfoUpdate.cpp"));
            firestorm.Add(Real("InitialMajorRadius", "DynamicGeometryInfoUpdate.cpp"));
            firestorm.Add(Real("InitialMinorRadius", "DynamicGeometryInfoUpdate.cpp"));
            firestorm.Add(Real("FinalHeight", "DynamicGeometryInfoUpdate.cpp"));
            firestorm.Add(Real("FinalMajorRadius", "DynamicGeometryInfoUpdate.cpp"));
            firestorm.Add(Real("FinalMinorRadius", "DynamicGeometryInfoUpdate.cpp"));
            firestorm.Add(new("TransitionTime", FieldType.DurationMs, Source: "DynamicGeometryInfoUpdate.cpp"));
            firestorm.Add(new("ReverseAtTransitionTime", FieldType.Bool, Source: "DynamicGeometryInfoUpdate.cpp"));
            firestorm.Add(new("DelayBetweenDamageFrames", FieldType.DurationMs, Source: "FirestormDynamicGeometryInfoUpdate.cpp"));
            firestorm.Add(Real("DamageAmount", "FirestormDynamicGeometryInfoUpdate.cpp"));
            firestorm.Add(Real("MaxHeightForDamage", "FirestormDynamicGeometryInfoUpdate.cpp"));
            for (var system = 1; system <= 16; system++) firestorm.Add(new($"ParticleSystem{system}", FieldType.Opaque, Source: "FirestormDynamicGeometryInfoUpdate.cpp"));
            firestorm.Add(new("FXList", FieldType.Opaque, Source: "FirestormDynamicGeometryInfoUpdate.cpp"));
            firestorm.Add(Real("ParticleOffsetZ", "FirestormDynamicGeometryInfoUpdate.cpp"));
            firestorm.Add(Real("ScorchSize", "FirestormDynamicGeometryInfoUpdate.cpp"));
            Add("FirestormDynamicGeometryInfoUpdate", firestorm.ToImmutable());
        }
        Add("CostModifierUpgrade",
        [
            ..UpgradeMux(),
            new("EffectKindOf", FieldType.Flags, Table: NameTables.KindOf, Source: "CostModifierUpgrade.cpp"),
            new("Percentage", FieldType.Percent, Source: "CostModifierUpgrade.cpp"),
        ]);
        Add("WeaponBonusUpdate",
        [
            new("RequiredAffectKindOf", FieldType.Flags, Table: NameTables.KindOf, Source: "WeaponBonusUpdate.cpp"),
            new("ForbiddenAffectKindOf", FieldType.Flags, Table: NameTables.KindOf, Source: "WeaponBonusUpdate.cpp"),
            new("BonusDuration", FieldType.DurationMs, Source: "WeaponBonusUpdate.cpp"),
            new("BonusDelay", FieldType.DurationMs, Source: "WeaponBonusUpdate.cpp"),
            Real("BonusRange", "WeaponBonusUpdate.cpp"),
            new("BonusConditionType", FieldType.Enum, Table: NameTablesPhase5.WeaponBonusCondition, Source: "WeaponBonusUpdate.cpp"),
        ]);
        {
            // BoneFXUpdate.h: <State><Kind><1..8> per body-damage state (Pristine, Damaged, ReallyDamaged, Rubble) and effect kind (FXList, OCL, ParticleSystem), plus the three damage-type masks.
            // All of it is effect playback at model bones: typed, never simulated (docs/SIMULATION-CORE.md; an OCL slot makes the module refuse to bind).
            var bones = ImmutableArray.CreateBuilder<FieldSchema>();
            bones.Add(new("DamageFXTypes", FieldType.Opaque, Source: "BoneFXUpdate.h"));
            bones.Add(new("DamageOCLTypes", FieldType.Opaque, Source: "BoneFXUpdate.h"));
            bones.Add(new("DamageParticleTypes", FieldType.Opaque, Source: "BoneFXUpdate.h"));
            foreach (var kind in new[] { "FXList", "OCL", "ParticleSystem" })
                foreach (var state in new[] { "Pristine", "Damaged", "ReallyDamaged", "Rubble" })
                    for (var slot = 1; slot <= 8; slot++) bones.Add(new($"{state}{kind}{slot}", FieldType.Opaque, Source: "BoneFXUpdate.h"));
            Add("BoneFXUpdate", bones.ToImmutable());
            Add("BoneFXDamage", []);
        }

        // ===== BEGIN the last nine modules (LastBlockerModules.cs): salvage/unit crates, two sabotage crates, smart bomb, crippled warehouse and the ferry =====
        Add("SalvageCrateCollide",
        [
            ..CrateCollide(),
            new("WeaponChance", FieldType.Percent, Source: "SalvageCrateCollide.h"),
            new("LevelChance", FieldType.Percent, Source: "SalvageCrateCollide.h"),
            // MoneyChance is parsed by the reference and never read: the money branch is the else of the other two (SalvageCrateCollide.cpp executeCrateBehavior).
            new("MoneyChance", FieldType.Percent, Source: "SalvageCrateCollide.h"),
            new("MinMoney", FieldType.Integer, Min: 0, Source: "SalvageCrateCollide.h"),
            new("MaxMoney", FieldType.Integer, Min: 0, Source: "SalvageCrateCollide.h"),
        ]);
        Add("UnitCrateCollide",
        [
            ..CrateCollide(),
            new("UnitCount", FieldType.Integer, Min: 0, Source: "UnitCrateCollide.h"),
            new("UnitName", FieldType.Reference, ReferenceKind: DefinitionKind.Object, Source: "UnitCrateCollide.h"),
        ]);
        Add("SabotageFakeBuildingCrateCollide", CrateCollide());
        Add("SabotageInternetCenterCrateCollide", [..CrateCollide(), new("SabotageDuration", FieldType.DurationMs, Source: "SabotageInternetCenterCrateCollide.h")]);
        Add("SmartBombTargetHomingUpdate", [Real("CourseCorrectionScalar", "SmartBombTargetHomingUpdate.h")]);
        Add("SupplyWarehouseCripplingBehavior",
        [
            new("SelfHealSupression", FieldType.DurationMs, Source: "SupplyWarehouseCripplingBehavior.cpp"),
            new("SelfHealDelay", FieldType.DurationMs, Source: "SupplyWarehouseCripplingBehavior.cpp"),
            Real("SelfHealAmount", "SupplyWarehouseCripplingBehavior.cpp"),
        ]);
        // The ferry: RailedTransportAIUpdate.cpp (AIUpdate base + PathPrefixName), RailedTransportContain.cpp (adds nothing to TransportContain's table) and RailedTransportDockUpdate.cpp.
        Add("RailedTransportAIUpdate",
        [
            ..AiBase("RailedTransportAIUpdate.cpp / AIUpdate.cpp"),
            new("PathPrefixName", FieldType.Text, Source: "RailedTransportAIUpdate.cpp"),
        ], allowsNestedSections: true);
        Add("RailedTransportContain", [..OpenContainFields("OpenContain.cpp (unused: TransportContain::getContainMax reads Slots)"), ..TransportContainFields()]);
        Add("RailedTransportDockUpdate",
        [
            ..DockBase("DockUpdate.cpp"),
            new("PullInsideDuration", FieldType.DurationMs, Source: "RailedTransportDockUpdate.cpp"),
            new("PushOutsideDuration", FieldType.DurationMs, Source: "RailedTransportDockUpdate.cpp"),
            Real("ToleranceDistance", "RailedTransportDockUpdate.cpp"),
        ]);
        // ===== END the last nine modules =====

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
