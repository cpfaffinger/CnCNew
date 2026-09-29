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
        // traverse and weapon-slot control) kept generically as DefinitionSections, not read by phase 5's straight-line
        // movement model (see docs/GAME-RULES.md known limits) — hence AllowsNestedSections.
        Add("AIUpdateInterface",
        [
            new("AutoAcquireEnemiesWhenIdle", FieldType.Bool, Source: "AIUpdate.cpp"),
            new("MoodAttackCheckRate", FieldType.DurationMs, Source: "AIUpdate.cpp"),
            new("ForbidPlayerCommands", FieldType.Bool, Source: "AIUpdate.cpp"),
            new("TurretsLinked", FieldType.Bool, Source: "AIUpdate.cpp"),
        ], allowsNestedSections: true);
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
