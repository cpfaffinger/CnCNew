using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Definitions;
using CnCNew.Data.Ini;
using CnCNew.Data.Registry;
using CnCNew.Data.Schema;
using CnCNew.Data.Snapshots;

ParserPreservesStructureAndLocations();
IncludesAndSearchPriority();
IncludeCyclesAreRejected();
InheritanceOverridesAndModuleMutations();
ConditionsSelectDeterministically();
ValuesAreTypedAndValidated();
ReferencesResolveAndRejectWrongTypes();
RegistryValidatesDuplicatesOwnersAndSchemas();
CompatibilityIsExplicit();
CorruptInputIsRejected();
SnapshotsAreDeterministic();

// Phase 4, block 0.
ModuleCatalogIsExplicitAndConsistent();
CompatibilityBaselineDetectsEveryDeviation();
TypoInModuleNameIsDistinguishableFromKnownUnimplementedModule();
LabeledValueGrammarIsStrict();
FlagsAndEnumsUseStrictNameTables();
ObjectHeaderIsSchemaTyped();
ModuleFieldsAreStrictlyTyped();
LoadPlanFollowsRetailOrderAndExcludesNonRetailFiles();
MultiFileDefinitionsReportWinners();
RepeatableKeysKeepMultiplicity();
DefaultThingTemplateIsInheritedWithInterfaceRules();
ReskinAndModuleEditsFollowTheReference();
IdsAndSnapshotsIgnoreEnumerationOrder();
TruncatedInputsAreRejected();
FullCompilationsAreByteIdentical();
FingerprintStreamingMatchesBuffer();
ObjectCreationListsAreTypedNuggets();
WeaponBonusesAreTypedAndStrict();
SingleBitsMatchTheRuntimeCast();
FingerprintEncodingIsPinned();

var contentDirectory = args.SkipWhile(value => value != "--content-dir").Skip(1).FirstOrDefault();
if (contentDirectory is not null) RealDataCatalogMatchesObservedModules(contentDirectory);

Console.WriteLine("All typed definition compiler tests passed.");
return 0;

static void ParserPreservesStructureAndLocations()
{
    const string text = """
    ; comment
    Object DemoTank
      Side = America
      Behavior = ActiveBody ModuleTag_02
        MaxHealth = 100.0 ; inline comment
      End
    End
    """;
    var document = SageIniParser.Parse(text, "fixture.ini");
    Assert(document.Blocks.Single().Type == "Object", "Top-level type mismatch.");
    Assert(document.DescendantBlocks().Count() == 2, "Nested block count mismatch.");
    Assert(document.Blocks.Single().Location == new IniSourceLocation("fixture.ini", 2, 1), "Source location mismatch.");
}

static void IncludesAndSearchPriority()
{
    var source = new MemorySource(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Data/INI/root.ini"] = "#include \"shared.ini\"\nObject Tank\n  BuildCost = 20\nEnd\n",
        ["Data/INI/shared.ini"] = "Object Tank\n  BuildCost = 10\nEnd\n",
        ["shared.ini"] = "Object Wrong\nEnd\n",
    }, ["Data/INI/root.ini"]);
    var graph = DefinitionCompiler.Compile(source);
    var tank = graph.Definitions.OfType<ObjectDefinition>().Single(value => value.OriginalName == "Tank");
    Assert(((Money)tank.Fields.Single().Value.Value).Value == 20, "Owner-relative include or override order failed.");
    Assert(!graph.Definitions.Any(value => value.OriginalName == "Wrong"), "Include search priority was not owner-relative.");
}

static void IncludeCyclesAreRejected()
{
    var source = new MemorySource(new Dictionary<string, string> { ["a.ini"] = "#include b.ini\n", ["b.ini"] = "#include a.ini\n" }, ["a.ini"]);
    Throws<DefinitionCompileException>(() => DefinitionCompiler.Compile(source), "Include cycle");
}

static void InheritanceOverridesAndModuleMutations()
{
    var source = One("""
    Object Base
      BuildCost = 10
      Behavior = ActiveBody Keep
        MaxHealth = 100
      End
      Behavior = SlowDeath RemoveMe
      End
    End
    Object Child : Base
      BuildCost = 20
      WeaponSet
        Conditions = None
        Weapon = PRIMARY DemoGun
      End
      RemoveModule RemoveMe
      ReplaceModule Keep
        Behavior = ActiveBody Replaced
          MaxHealth = 200
        End
      End
      AddModule
        Behavior = AutoHeal Added
          HealingAmount = 5
        End
      End
    End
    """);
    var graph = DefinitionCompiler.Compile(source);
    var child = graph.Definitions.OfType<ObjectDefinition>().Single(value => value.OriginalName == "Child");
    Assert(((Money)child.Fields.Single().Value.Value).Value == 20, "Inherited field override failed.");
    Assert(child.Modules.Select(value => value.Tag).SequenceEqual(["Replaced", "Added"]), "Module mutation order failed.");
    Assert(child.Sections.Single().Declaration == "WeaponSet" && child.Sections.Single().Fields.Length == 2, "Nested typed section was not retained.");
    Assert(graph.Audit.Compatibility.Any(value => value.Category == "diagnostic" && value.Name == "non-original-inheritance-syntax"),
        "The non-original inheritance syntax must stay visible.");
}

static void ConditionsSelectDeterministically()
{
    var source = One("""
    #if DEBUG
    Object DebugOnly
    End
    #else
    Object RetailOnly
    End
    #endif
    """);
    var options = DefinitionCompilerOptions.Audit with { Conditions = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase, "DEBUG") };
    var graph = DefinitionCompiler.Compile(source, options: options);
    Assert(graph.Definitions.Single().OriginalName == "DebugOnly", "Condition selection failed.");
}

static void ValuesAreTypedAndValidated()
{
    Assert(DefinitionValueParser.Parse("DelayTime", "1500", new("x", 1, 1)).Kind == DefinitionValueKind.Time, "Time typing failed.");
    Assert(DefinitionValueParser.Parse("AttackRange", "300", new("x", 1, 1)).Kind == DefinitionValueKind.Distance, "Distance typing failed.");
    Assert(DefinitionValueParser.Parse("Speed", "2.5", new("x", 1, 1)).Kind == DefinitionValueKind.Speed, "Speed typing failed.");
    Assert(DefinitionValueParser.Parse("Angle", "90", new("x", 1, 1)).Kind == DefinitionValueKind.Angle, "Angle typing failed.");
    Assert(DefinitionValueParser.Parse("Bonus", "125%", new("x", 1, 1)).Kind == DefinitionValueKind.Percentage, "Percentage typing failed.");
    Assert(DefinitionValueParser.Parse("BuildCost", "1200", new("x", 1, 1)).Kind == DefinitionValueKind.Money, "Money typing failed.");
    Assert(DefinitionValueParser.Parse("Color", "1 2 3", new("x", 1, 1)).Kind == DefinitionValueKind.Color, "Color typing failed.");
    Assert(DefinitionValueParser.Parse("Offset", "1 2 3", new("x", 1, 1)).Kind == DefinitionValueKind.Vector3, "Vector typing failed.");
    Throws<FormatException>(() => DefinitionValueParser.Parse("Value", "NaN", new("x", 1, 1)), "Non-finite");
    Throws<FormatException>(() => DefinitionValueParser.Parse("Value", "1e1000", new("x", 1, 1)), "outside the supported range");
}

static void ReferencesResolveAndRejectWrongTypes()
{
    var valid = DefinitionCompiler.Compile(One("CommandSet Cmds\nEnd\nObject Tank\n  CommandSet = Cmds\nEnd\n"),
        options: DefinitionCompilerOptions.Audit with { StrictReferences = true });
    Assert(valid.References.Single().Resolved, "Reference did not resolve.");
    var wrong = One("Armor Cmds\nEnd\nObject Tank\n  CommandSet = Cmds\nEnd\n");
    Throws<DefinitionCompileException>(() => DefinitionCompiler.Compile(wrong, options: DefinitionCompilerOptions.Audit with { StrictReferences = true }), "Missing CommandSet reference");
}

static void RegistryValidatesDuplicatesOwnersAndSchemas()
{
    var descriptor = new ModuleDescriptor("ActiveBody", ModuleBlockKind.Body, ModuleInterfaces.Body, ImmutableHashSet.Create(ModuleOwnerKind.Object),
        [new FieldSchema("MaxHealth", FieldType.Real, Required: true)], false, null, ModuleCategory.Simulation);
    Throws<InvalidOperationException>(() => new ModuleRegistry([descriptor, descriptor]), "Duplicate module");
    var registry = new ModuleRegistry([descriptor]);
    var graph = DefinitionCompiler.Compile(One("Object Tank\n  Body = ActiveBody Body\n    MaxHealth = 100.0\n  End\nEnd\n"), registry,
        DefinitionCompilerOptions.Audit with { StrictUnknownModules = true });
    Assert(graph.Definitions.Single().Modules.Length == 1, "Registered module was not compiled.");
    Throws<DefinitionCompileException>(() => DefinitionCompiler.Compile(One("Object Tank\n  Body = ActiveBody Body\n    Unknown = 1\n  End\nEnd\n"), registry), "Unknown field");
    Throws<DefinitionCompileException>(() => DefinitionCompiler.Compile(One("Object Tank\n  Body = ActiveBody Body\n  End\nEnd\n"), registry), "requires field");
    Throws<DefinitionCompileException>(() => DefinitionCompiler.Compile(One("Object Tank\n  Behavior = ActiveBody Body\n    MaxHealth = 1\n  End\nEnd\n"), registry), "must be declared with");
    Throws<DefinitionCompileException>(() => DefinitionCompiler.Compile(One("Object Tank\n  Behavior = Mystery Tag\n  End\nEnd\n"), registry,
        DefinitionCompilerOptions.Audit with { StrictUnknownModules = true }), "Unknown module");
}

static void CompatibilityIsExplicit()
{
    var graph = DefinitionCompiler.Compile(One("MysteryDefinition X\nEnd\nObject Tank\n  Behavior = MysteryModule Tag\n  End\nEnd\n"));
    Assert(graph.Audit.Compatibility.Any(value => value.Category == "definition" && value.Name == "MysteryDefinition"), "Unknown definition was not inventoried.");
    Assert(graph.Audit.Compatibility.Any(value => value.Category == "module" && value.Name == "MysteryModule"), "Unknown module was not inventoried.");
}

static void CorruptInputIsRejected()
{
    Throws<IniParseException>(() => SageIniParser.Parse("Object Broken\n  BuildCost = 1\n", "broken.ini"), "missing its End");
    Throws<IniParseException>(() => SageIniParser.Parse("End\n", "broken.ini"), "Unexpected End");
}

static void SnapshotsAreDeterministic()
{
    var source = One("Weapon Gun\n  PrimaryDamage = 10\nEnd\nObject Tank\n  CommandSet = Cmds\nEnd\n");
    var first = DefinitionSnapshotWriter.WriteCanonical(DefinitionCompiler.Compile(source));
    var second = DefinitionSnapshotWriter.WriteCanonical(DefinitionCompiler.Compile(source));
    Assert(first.AsSpan().SequenceEqual(second), "Canonical snapshots differ.");
}

// ---------------------------------------------------------------------------------------------- block 0.2

static void ModuleCatalogIsExplicitAndConsistent()
{
    var catalog = ModuleRegistry.Default.Descriptors.ToArray();
    Assert(catalog.Length == 205, $"Expected 205 catalogued module types, found {catalog.Length}.");
    Assert(catalog.Select(value => value.Name).SequenceEqual(catalog.Select(value => value.Name).Order(StringComparer.OrdinalIgnoreCase)),
        "The catalog must be sorted.");
    Assert(catalog.Select(value => value.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() == catalog.Length, "Duplicate catalog names.");
    foreach (var descriptor in catalog)
    {
        Assert((descriptor.BlockKind == ModuleBlockKind.Body) == descriptor.Interfaces.HasFlag(ModuleInterfaces.Body), $"{descriptor.Name}: Body block kind and interface disagree.");
        Assert((descriptor.Category == ModuleCategory.Presentation) == (descriptor.BlockKind is ModuleBlockKind.Draw or ModuleBlockKind.ClientUpdate), $"{descriptor.Name}: category and block kind disagree.");
        Assert(descriptor.CompatibilityOnly == (descriptor.PlannedTarget is not null), $"{descriptor.Name}: planned target must exist exactly for non-implemented modules.");
    }
    var implemented = catalog.Where(value => !value.CompatibilityOnly).Select(value => value.Name).Order(StringComparer.Ordinal).ToArray();
    string[] expected =
    [
        // phase 4 (12)
        "ActiveBody", "CreateObjectDie", "DeletionUpdate", "DestroyDie", "FXListDie", "HighlanderBody", "ImmortalBody",
        "InactiveBody", "InstantDeathBehavior", "KeepObjectDie", "LifetimeUpdate", "StructureBody",
        // phase 5 (24): Block 0.2 (StealthUpdate, AutoHealBehavior) + the upgrade family + movement + production
        // + the next blocker modules by measured instantiability impact (docs/HANDOFF-PHASE5.md)
        "AIUpdateInterface", "ArmorUpgrade", "AutoHealBehavior", "CommandSetUpgrade", "CreateCrateDie", "ExperienceScalarUpgrade",
        "FireWeaponWhenDeadBehavior", "FlammableUpdate", "GarrisonContain", "LocomotorSetUpgrade", "MaxHealthUpgrade",
        "ModelConditionUpgrade", "ObjectCreationUpgrade", "PoisonedBehavior", "ProductionUpdate", "RadarUpgrade",
        "SlowDeathBehavior", "StealthDetectorUpdate", "StealthUpdate", "StealthUpgrade", "TransitionDamageFX",
        "TransportContain", "WeaponBonusUpgrade", "WeaponSetUpgrade",
        // reference-match unlock batch (docs/GAME-RULES.md §7a)
        "BaseRegenerateUpdate", "GenerateMinefieldBehavior", "PhysicsBehavior", "SquishCollide",
        "DefaultProductionExitUpdate", "SupplyCenterProductionExitUpdate", "QueueProductionExitUpdate", "HealContain", "EjectPilotDie",
        "VeterancyGainCreate", "PreorderCreate", "RadarUpdate", "RebuildHoleExposeDie", "RebuildHoleBehavior",
        // special-power unlock batch (docs/GAME-RULES.md §6d)
        "SpecialAbility", "OCLSpecialPower", "CashBountyPower", "CashHackSpecialPower", "SpecialPowerCreate", "GrantUpgradeCreate",
        "GrantScienceUpgrade", "UnpauseSpecialPowerUpgrade", "SpecialPowerCompletionDie", "SpectreGunshipDeploymentUpdate",
        // supply economy batch (docs/GAME-RULES.md §6e)
        "SupplyCenterCreate", "SupplyWarehouseCreate", "SupplyCenterDockUpdate", "SupplyWarehouseDockUpdate", "SupplyTruckAIUpdate", "WorkerAIUpdate",
        "AutoDepositUpdate", "SpawnBehavior", "DozerAIUpdate",
        // repair/heal batch (docs/GAME-RULES.md §6f)
        "RepairDockUpdate", "AutoFindHealingUpdate",
        // combat batch: private-weapon detonation and object replacement (docs/GAME-RULES.md §6g)
        "FireWeaponUpdate", "ReplaceObjectUpgrade", "CrushDie",
        // power plant batch (docs/GAME-RULES.md §10)
        "PowerPlantUpdate", "PowerPlantUpgrade", "OverchargeBehavior",
        // phase 8 E1: projectile flight (docs/GAME-RULES.md §6i), transports and tunnels (§6j)
        "DumbProjectileBehavior", "MissileAIUpdate", "PointDefenseLaserUpdate",
        "TransportAIUpdate", "ChinookAIUpdate", "HelicopterSlowDeathBehavior", "TunnelContain", "HelixContain",
        // phase 9: shells the campaign maps' placed infantry need (docs/SCRIPT-ENGINE.md §8)
        "CommandButtonHuntUpdate", "LockWeaponCreate",
        // phase 10: command-button abilities need their carrier to exist (docs/SCRIPT-ENGINE.md §8, docs/GAME-RULES.md §6d)
        "SpecialAbilityUpdate",
        // phase 10: topple, structure collapse and height-threshold death (docs/SIMULATION-CORE.md §23)
        "ToppleUpdate", "StructureToppleUpdate", "HeightDieUpdate",
        // phase 12: the modules the campaign missions need to create their named objects (docs/SCRIPT-ENGINE.md §9)
        "RadiusDecalUpdate", "TechBuildingBehavior", "FireWeaponWhenDamagedBehavior", "FireWeaponPower", "BaikonurLaunchPower", "HordeUpdate", "FlightDeckBehavior",
        // phase 13: the aircraft of scripted reinforcements (docs/SCRIPT-ENGINE.md §5)
        "DeliverPayloadAIUpdate", "ParachuteContain", "JetSlowDeathBehavior", "CountermeasuresBehavior", "ParticleUplinkCannonUpdate",
        // phase 13: the Battle Bus and two animation-only modules (docs/SCRIPT-ENGINE.md §9)
        "UndeadBody", "BattleBusSlowDeathBehavior", "SubObjectsUpgrade", "AnimationSteeringUpdate",
        // phase 14, slaves/spawns/containers: drones, mobs, hives, propaganda, riders, overlords, hackers (docs/SIMULATION-CORE.md §29)
        "SlavedUpdate", "MobMemberSlavedUpdate", "HiveStructureBody", "SpawnPointProductionExitUpdate", "PropagandaTowerBehavior", "RiderChangeContain", "OverlordContain",
        "InternetHackContain", "PassengersFireUpgrade", "AssaultTransportAIUpdate", "HackInternetAIUpdate", "TensileFormationUpdate",
        // air group: airfields and aircraft, silo doors, neutron missile, pulses, bunker busters, the Spectre gunship (docs/SIMULATION-CORE.md)
        "JetAIUpdate", "ParkingPlaceBehavior", "SpectreGunshipUpdate", "MissileLauncherBuildingUpdate", "NeutronMissileUpdate", "NeutronMissileSlowDeathBehavior", "NeutronBlastBehavior",
        "EMPUpdate", "LeafletDropBehavior", "BunkerBusterBehavior",
        // crates, collide modules and specials (docs/SIMULATION-CORE.md): crate family, hijack, sabotage, pilot, sticky bomb, stealth grant, wander, projectile stream
        "MoneyCrateCollide", "VeterancyCrateCollide", "ConvertToCarBombCrateCollide", "ConvertToHijackedVehicleCrateCollide", "HijackerUpdate", "SabotageCommandCenterCrateCollide",
        "SabotageSuperweaponCrateCollide", "SabotageMilitaryFactoryCrateCollide", "SabotagePowerPlantCrateCollide", "SabotageSupplyCenterCrateCollide", "PilotFindVehicleUpdate",
        "StickyBombUpdate", "GrantStealthBehavior", "WanderAIUpdate", "ProjectileStreamUpdate",
        "CleanupHazardUpdate", "CleanupAreaPower", "SpyVisionUpdate", "SpyVisionSpecialPower", "AssistedTargetingUpdate", "BattlePlanUpdate",
        // structures and world group (docs/SIMULATION-CORE.md §29)
        "UpgradeDie", "StructureCollapseUpdate", "FireWeaponCollide", "FireSpreadUpdate", "DeployStyleAIUpdate", "FloatUpdate", "RailroadBehavior", "DemoTrapUpdate", "MinefieldBehavior",
        "BridgeBehavior", "BridgeTowerBehavior", "BridgeScaffoldBehavior", "OCLUpdate", "DynamicShroudClearingRangeUpdate", "FireOCLAfterWeaponCooldownUpdate", "WaveGuideUpdate", "DamDie",
        "CheckpointUpdate", "EnemyNearUpdate", "FirestormDynamicGeometryInfoUpdate", "WeaponBonusUpdate", "CostModifierUpgrade", "BoneFXDamage", "BoneFXUpdate",
        // the last nine (docs/SIMULATION-CORE.md): salvage and unit crates, the fake-building and Internet-Center saboteurs, the MOAB's homing, the crippled warehouse and the ferry
        "SalvageCrateCollide", "UnitCrateCollide", "SabotageFakeBuildingCrateCollide", "SabotageInternetCenterCrateCollide", "SmartBombTargetHomingUpdate", "SupplyWarehouseCripplingBehavior",
        "RailedTransportAIUpdate", "RailedTransportContain", "RailedTransportDockUpdate",
    ];
    Assert(catalog.Where(value => value.CompatibilityOnly && value.Category == ModuleCategory.Simulation).Count() == 0, "Every simulation module of the catalog is implemented.");
    Assert(implemented.SequenceEqual(expected.Order(StringComparer.Ordinal)), "The implemented module set changed unexpectedly: " + string.Join(", ", implemented));
    Assert(ModuleRegistry.Default.GetRequired("FXListDie").Interfaces == (ModuleInterfaces.Die | ModuleInterfaces.Upgrade), "FXListDie mask must match the reference (DIE|UPGRADE).");
    Assert(ModuleRegistry.Default.GetRequired("W3DModelDraw").Category == ModuleCategory.Presentation, "Draw modules are presentation.");
    Assert(ModuleRegistry.Default.GetRequired("LaserUpdate").BlockKind == ModuleBlockKind.ClientUpdate, "LaserUpdate is a ClientUpdate module in the reference.");
}

// ---------------------------------------------------------------------------------------------- block 0.1

static void CompatibilityBaselineDetectsEveryDeviation()
{
    var files = new Dictionary<string, string>
    {
        ["fixture.ini"] = "Terrain Grass\nEnd\nObject Tank\n  Behavior = SlowDeathBehavior A\n  End\n  VoiceMove = NoSound\nEnd\n",
    };
    var strict = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = false };
    var graph = DefinitionCompiler.Compile(new MemorySource(files), ModuleRegistry.Default, strict);
    var baseline = CompatibilityBaseline.Serialize(graph.Audit.Compatibility, "synthetic");
    Assert(CompatibilityBaseline.Compare(baseline, graph.Audit.Compatibility).Count == 0, "A fresh baseline must match itself.");

    files["fixture.ini"] += "Terrain Sand\nEnd\n";
    var changed = DefinitionCompiler.Compile(new MemorySource(files), ModuleRegistry.Default, strict);
    Assert(CompatibilityBaseline.Compare(baseline, changed.Audit.Compatibility).Any(value => value.StartsWith("CHANGED", StringComparison.Ordinal) && value.Contains("Terrain", StringComparison.Ordinal)),
        "A changed frequency must be reported.");

    files["fixture.ini"] = "Object Tank\n  Behavior = SlowDeathBehavior A\n  End\n  Draw = W3DModelDraw B\n  End\nEnd\n";
    var added = DefinitionCompiler.Compile(new MemorySource(files), ModuleRegistry.Default, strict);
    var differences = CompatibilityBaseline.Compare(baseline, added.Audit.Compatibility);
    Assert(differences.Any(value => value.StartsWith("NEW", StringComparison.Ordinal) && value.Contains("W3DModelDraw", StringComparison.Ordinal)), "A new entry must be reported.");
    Assert(differences.Any(value => value.StartsWith("MISSING", StringComparison.Ordinal) && value.Contains("Terrain", StringComparison.Ordinal)), "A vanished entry must be reported.");
    Throws<InvalidDataException>(() => CompatibilityBaseline.Compare("{\"schemaVersion\":99,\"mode\":\"x\",\"entries\":[]}", graph.Audit.Compatibility), "Unsupported");
}

static void TypoInModuleNameIsDistinguishableFromKnownUnimplementedModule()
{
    // Every simulation module is implemented now; the known-but-not-instantiated modules are the presentation (Draw) ones.
    var known = "Object Tank\n  Draw = W3DModelDraw A\n  End\nEnd\n";
    var typo = "Object Tank\n  Behavior = CreateCrateDei A\n  End\nEnd\n";
    var lenient = DefinitionCompilerOptions.Audit;
    var knownGraph = DefinitionCompiler.Compile(One(known), ModuleRegistry.Default, lenient);
    var typoGraph = DefinitionCompiler.Compile(One(typo), ModuleRegistry.Default, lenient);
    Assert(knownGraph.Audit.Compatibility.Single(value => value.Category == "module").PlannedTarget == "phase-7-presentation", "Known modules carry a planned target.");
    Assert(typoGraph.Audit.Compatibility.Single(value => value.Category == "module").PlannedTarget == "unknown-module-not-in-catalog", "A typo must be marked as uncatalogued.");
    var strict = lenient with { StrictUnknownModules = true };
    DefinitionCompiler.Compile(One(known), ModuleRegistry.Default, strict);
    Throws<DefinitionCompileException>(() => DefinitionCompiler.Compile(One(typo), ModuleRegistry.Default, strict), "Unknown module");
}

// ---------------------------------------------------------------------------------------------- block 0.3

static void LabeledValueGrammarIsStrict()
{
    var location = new IniSourceLocation("x", 1, 1);
    var color = DefinitionValueParser.Parse("Anything", "R:255 G:0 B:128", location);
    Assert(color.Kind == DefinitionValueKind.Color && (ColorValue)color.Value == new ColorValue(255, 0, 128, 255), "Color form failed.");
    var withAlpha = DefinitionValueParser.Parse("Anything", "R:1 G:2 B:3 A:4", location);
    Assert((ColorValue)withAlpha.Value == new ColorValue(1, 2, 3, 4), "Color with alpha failed.");
    var comment = DefinitionValueParser.Parse("Anything", "R:32 G:64 B:128 A:0 //dim blue, additive", location);
    Assert(comment.Kind == DefinitionValueKind.Color, "Trailing // comments after a color are tolerated.");
    var keyframe = DefinitionValueParser.Parse("Color1", "R:255 G:183 B:111 20", location);
    Assert(keyframe.Kind == DefinitionValueKind.ColorKeyframe && ((ColorKeyframeValue)keyframe.Value).Frame == 20, "Color keyframe failed.");
    var vec3 = DefinitionValueParser.Parse("Anything", "X: 60.0  Y:  35.0   Z:0.0", location);
    Assert(vec3.Kind == DefinitionValueKind.Vector3 && ((Vector3Value)vec3.Value).Y == 35.0m, "Vector3 with irregular blanks failed.");
    Assert(DefinitionValueParser.Parse("Anything", "X:-30 Y:15: Z:10", location).Kind == DefinitionValueKind.Vector3, "The shipped 'Y:15:' typo must parse like the reference.");
    Assert(((Vector3Value)DefinitionValueParser.Parse("Anything", "X:.071 Y:6.73 Z:7.273", location).Value).X == 0.071m, "Leading-dot decimal failed.");
    Assert(DefinitionValueParser.Parse("Anything", "X:0 Y:518", location).Kind == DefinitionValueKind.Vector2, "Vector2 failed.");
    Throws<FormatException>(() => DefinitionValueParser.Parse("Anything", "R:256 G:0 B:0", location), "0 to 255");
    Throws<FormatException>(() => DefinitionValueParser.Parse("Anything", "R:1 G:2", location), "Malformed color");
    Throws<FormatException>(() => DefinitionValueParser.Parse("Anything", "R:1 G:2 B:3 A:4 5", location), "trailing");
    Throws<FormatException>(() => DefinitionValueParser.Parse("Anything", "X:1", location), "Malformed vector");
    Throws<FormatException>(() => DefinitionValueParser.Parse("Anything", "X:1 Y:abc", location), "not a finite number");
    Throws<FormatException>(() => DefinitionValueParser.Parse("Anything", "X:1 Y:2 Z:3 W:4", location), "Malformed vector");
    Assert(ValueGrammar.TryRealPrefix("40v.0", out var prefix, out var trailing) && prefix == 40m && trailing, "sscanf-style prefix parsing failed.");
    Assert(!ValueGrammar.TryRealPrefix("v40", out _, out _), "Text without a numeric prefix must be rejected.");
}

static void FlagsAndEnumsUseStrictNameTables()
{
    var location = new IniSourceLocation("x", 1, 1);
    var flags = (FlagsValue)ValueGrammar.ParseFlags(NameTables.KindOf, "STRUCTURE selectable IMMOBILE", location).Value;
    Assert(flags.Names.SequenceEqual(["SELECTABLE", "IMMOBILE", "STRUCTURE"]), "Flags must be canonical and in table order: " + string.Join(",", flags.Names));
    Assert(((FlagsValue)ValueGrammar.ParseFlags(NameTables.KindOf, "NONE", location).Value).Names.Length == 0, "NONE clears flags.");
    var relative = (FlagsValue)ValueGrammar.ParseFlags(NameTables.KindOf, "+IMMOBILE -SELECTABLE", location).Value;
    Assert(relative.IsRelative && relative.ResolveAgainst(flags).Names.SequenceEqual(["IMMOBILE", "STRUCTURE"]), "Relative flags must resolve against the inherited value.");
    Throws<FormatException>(() => ValueGrammar.ParseFlags(NameTables.KindOf, "STRUCTURE TYPO", location), "not a member of KindOf");
    Throws<FormatException>(() => ValueGrammar.ParseFlags(NameTables.KindOf, "STRUCTURE +SELECTABLE", location), "may not be mixed");
    Throws<FormatException>(() => ValueGrammar.ParseFlags(NameTables.KindOf, "STRUCTURE NONE", location), "NONE may not be mixed");
    var death = (FlagsValue)ValueGrammar.ParseAllNoneDeltaFlags(NameTables.DeathType, "ALL -CRUSHED -SPLATTED", location).Value;
    Assert(death.Names.Length == NameTables.DeathType.Count - 2 && !death.Contains("CRUSHED"), "ALL -X semantics failed.");
    var only = (FlagsValue)ValueGrammar.ParseAllNoneDeltaFlags(NameTables.DeathType, "NONE +CRUSHED +SPLATTED", location).Value;
    Assert(only.Names.SequenceEqual(["CRUSHED", "SPLATTED"]), "NONE +X semantics failed.");
    Throws<FormatException>(() => ValueGrammar.ParseAllNoneDeltaFlags(NameTables.DeathType, "CRUSHED", location), "expected ALL, NONE");
    Assert(((EnumValue)ValueGrammar.ParseEnum(NameTables.Geometry, "box", location).Value).Name == "BOX", "Enum names are canonicalised.");
    Throws<FormatException>(() => ValueGrammar.ParseEnum(NameTables.Geometry, "TRIANGLE", location), "not a member of Geometry");
    Assert(NameTables.KindOf.Count == 120 && NameTables.ObjectStatus.Count == 45 && NameTables.DeathType.Count == 21, "Name tables must match the reference sizes.");
}

static void ObjectHeaderIsSchemaTyped()
{
    var graph = DefinitionCompiler.Compile(One("""
    Object Soldier
      KindOf = INFANTRY SELECTABLE
      Geometry = CYLINDER
      GeometryMajorRadius = 12.0
      GeometryIsSmall = YES
      BuildTime = 10.0
      BuildCost = 1000
      DisplayColor = R:1 G:2 B:3
      ExperienceValue = 1 2 3 4
      Shadow = SHADOW_VOLUME
      Locomotor = SET_NORMAL SoldierLocomotor
      PlacementViewAngle = -135
    End
    """));
    var soldier = graph.Definitions.OfType<ObjectDefinition>().Single();
    DefinitionValue Field(string name) => soldier.Fields.Single(field => field.Name == name).Value;
    Assert(Field("KindOf").Kind == DefinitionValueKind.Flags, "KindOf must be Flags.");
    Assert(((EnumValue)Field("Geometry").Value).Name == "CYLINDER", "Geometry must be an Enum.");
    Assert(Field("GeometryMajorRadius").Kind == DefinitionValueKind.Distance, "Radius must be a Distance.");
    Assert((bool)Field("GeometryIsSmall").Value, "GeometryIsSmall = YES must parse case-insensitively.");
    Assert(Field("BuildTime").Kind == DefinitionValueKind.Number, "BuildTime is seconds, not milliseconds.");
    Assert(((Money)Field("BuildCost").Value).Value == 1000, "BuildCost is money.");
    Assert(Field("DisplayColor").Kind == DefinitionValueKind.Color, "DisplayColor must be a Color.");
    Assert(((ImmutableArray<long>)Field("ExperienceValue").Value).SequenceEqual([1L, 2, 3, 4]), "ExperienceValue must be an integer list.");
    Assert(((Angle)Field("PlacementViewAngle").Value).Degrees == -135m, "PlacementViewAngle must be an Angle.");
    Assert(((TaggedReferenceList)Field("Locomotor").Value).Tag == "SET_NORMAL", "Locomotor must keep its set name.");
    Assert(graph.References.Any(edge => edge.Target.Kind == DefinitionKind.Locomotor && edge.Target.Name == "SoldierLocomotor"), "Locomotor references must produce edges.");
    Throws<DefinitionCompileException>(() => DefinitionCompiler.Compile(One("Object A\n  Mystery = 1\nEnd\n")), "Unknown Object key");
    Throws<FormatException>(() => DefinitionCompiler.Compile(One("Object A\n  KindOf = NOT_A_KIND\nEnd\n")), "not a member of KindOf");
    Throws<FormatException>(() => DefinitionCompiler.Compile(One("Object A\n  Geometry = TRIANGLE\nEnd\n")), "not a member of Geometry");
    Throws<FormatException>(() => DefinitionCompiler.Compile(One("Object A\n  DisplayColor = R:300 G:0 B:0\nEnd\n")), "0 to 255");
    Throws<FormatException>(() => DefinitionCompiler.Compile(One("Object A\n  GeometryIsSmall = Maybe\nEnd\n")), "Yes or No");
    Throws<FormatException>(() => DefinitionCompiler.Compile(One("Object A\n  ExperienceValue = 1 2 3\nEnd\n")), "expects 4 integers");
    Throws<FormatException>(() => DefinitionCompiler.Compile(One("Object A\n  TransportSlotCount = 256\nEnd\n")), "outside 0..255");
    Throws<FormatException>(() => DefinitionCompiler.Compile(One("Object A\n  Locomotor = SET_UNKNOWN X\nEnd\n")), "locomotor set name");
    var lenient = DefinitionCompiler.Compile(One("Object A\n  Mystery = 1\nEnd\n"), options: DefinitionCompilerOptions.Audit with { StrictObjectKeys = false });
    Assert(lenient.Audit.Compatibility.Any(value => value.Category == "deferred-field" && value.Name == "Object/Mystery"), "Lenient mode must count unknown object keys.");
    var quirks = DefinitionCompiler.Compile(One("Object Q\n  GeometryMajorRadius = 18.5.0\n  GeometryHeight = 40v.0\n  ExperienceValue = 1 2 3 4 5\n  EditorSorting = STRUCTURE EXTRA\n  CommandSet = = Cmds\nEnd\n"));
    Assert(quirks.Diagnostics.Count(value => value.Code == "compatibility-value") == 2, "The two shipped real-number typos are explicit compatibility values.");
    Assert(quirks.Diagnostics.Count(value => value.Code == "compatibility-ignored-tokens") == 2, "Ignored trailing tokens must be diagnosed.");
    var q = quirks.Definitions.OfType<ObjectDefinition>().Single();
    Assert(q.Fields.Single(field => field.Name == "GeometryMajorRadius").Value.Value is CompatibilityValue { Code: "shipped-double-decimal-radius", NormalizedValue: "18.5" }, "18.5.0 compatibility value.");
    Assert(q.Fields.Single(field => field.Name == "GeometryHeight").Value.Value is CompatibilityValue { Code: "shipped-real-trailing-text", NormalizedValue: "40" }, "40v.0 compatibility value.");
}

static void ModuleFieldsAreStrictlyTyped()
{
    var registry = ModuleRegistry.Default;
    var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true };
    var ok = DefinitionCompiler.Compile(One("""
    Object A
      Body = ActiveBody B
        MaxHealth = 100.0
        InitialHealth = 100.0
        SubdualDamageCap = 200
        SubdualDamageHealRate = 500
        SubdualDamageHealAmount = 50
      End
      Behavior = DestroyDie D
        DeathTypes = ALL -CRUSHED
        ExemptStatus = UNDER_CONSTRUCTION
      End
      Behavior = LifetimeUpdate L
        MinLifetime = 1000
        MaxLifetime = 1500
      End
    End
    """), registry, options);
    var a = ok.Definitions.OfType<ObjectDefinition>().Single();
    Assert(a.Modules.Length == 3 && a.Modules[0].BlockKind == ModuleBlockKind.Body, "Typed modules failed to compile.");
    Assert(a.Modules[1].Fields.Single(field => field.Name == "DeathTypes").Value.Value is FlagsValue { Table: "DeathType" }, "DeathTypes must be typed flags.");
    Assert(a.Modules[2].Fields.All(field => field.Value.Kind == DefinitionValueKind.Time), "Lifetimes must be durations.");
    Throws<FormatException>(() => DefinitionCompiler.Compile(One("Object A\n  Body = ActiveBody B\n    MaxHealth = abc\n  End\nEnd\n"), registry, options), "expects a finite number");
    Throws<FormatException>(() => DefinitionCompiler.Compile(One("Object A\n  Behavior = LifetimeUpdate L\n    MinLifetime = 1.5\n    MaxLifetime = 2\n  End\nEnd\n"), registry, options), "unsigned integer duration");
    Throws<FormatException>(() => DefinitionCompiler.Compile(One("Object A\n  Behavior = DestroyDie D\n    ExemptStatus = NOT_A_STATUS\n  End\nEnd\n"), registry, options), "not a member of ObjectStatus");
    Throws<DefinitionCompileException>(() => DefinitionCompiler.Compile(One("Object A\n  Behavior = LifetimeUpdate L\n    MinLifetime = 1\n  End\nEnd\n"), registry, options), "requires field 'MaxLifetime'");
    Throws<DefinitionCompileException>(() => DefinitionCompiler.Compile(One("Object A\n  Behavior = DestroyDie D\n    Mystery = 1\n  End\nEnd\n"), registry, options), "Unknown field 'Mystery'");
    Throws<DefinitionCompileException>(() => DefinitionCompiler.Compile(One("Object A\n  Behavior = DestroyDie D\n    DeathTypes = ALL\n    DeathTypes = NONE\n  End\nEnd\n"), registry, options), "set twice");
    var wrapped = DefinitionCompiler.Compile(One("Object A\n  Behavior = DeletionUpdate D\n    MinLifetime = -1\n    MaxLifetime = -1\n  End\nEnd\n"), registry, options);
    var wrappedTime = (GameTime)wrapped.Definitions.OfType<ObjectDefinition>().Single().Modules.Single().Fields[0].Value.Value;
    Assert(wrappedTime.Milliseconds == 4294967295m, "Negative durations wrap like sscanf %u.");
    Assert(wrapped.Diagnostics.Any(value => value.Code == "compatibility-unsigned-wrap"), "Wrapping must be diagnosed.");
}

// ---------------------------------------------------------------------------------------------- block 0.4

static void LoadPlanFollowsRetailOrderAndExcludesNonRetailFiles()
{
    var files = new Dictionary<string, string>
    {
        ["Data/INI/Default/GameData.ini"] = "GameData\n  Marker = default\nEnd\n",
        ["Data/INI/GameData.ini"] = "GameData\n  Marker = main\nEnd\n",
        ["Data/INI/GameDataDebug.ini"] = "GameData\n  Marker = debug\nEnd\n",
        ["Data/INI/MappedImages/TextureSize_512/A.ini"] = "MappedImage Shared\n  Texture = size512\nEnd\n",
        ["Data/INI/MappedImages/HandCreated/B.ini"] = "MappedImage Shared\n  Texture = hand\nEnd\n",
        ["Data/English/Language.ini"] = "Language\n  Marker = english\nEnd\n",
        ["Data/German/Language.ini"] = "Language\n  Marker = german\nEnd\n",
        ["Data/Chinese/Language9x.ini"] = "Language\n  Marker = win9x\nEnd\n",
        ["Data/Scripts/Scripts.ini"] = "ScriptAction\n  Marker = tool\nEnd\n",
        ["Maps/M1/map.ini"] = "GameData\n  Marker = map1\nEnd\n",
        ["Maps/M2/map.ini"] = "GameData\n  Marker = map2\nEnd\n",
        ["Data/INI/Unknown.ini"] = "Terrain Odd\nEnd\n",
    };
    var strict = DefinitionCompilerOptions.Audit with { StrictLoadPlan = true };
    var retail = DefinitionCompiler.Compile(new MemorySource(files), options: strict with { MapOverlays = MapOverlayMode.Exclude });
    string Text(CompiledDefinitionGraph graph, string id, string key)
        => graph.Definitions.Single(value => value.Id.ToString() == id).Fields.Single(field => field.Name == key).Value.OriginalText;
    Assert(Text(retail, "gamedata://gamedata", "Marker") == "main", "Retail GameData must be default, then main; the debug file is not loaded.");
    Assert(Text(retail, "compatibility://mappedimage/shared", "Texture") == "hand", "HandCreated loads after TextureSize_512 (Image.cpp) and wins.");
    Assert(Text(retail, "compatibility://language/language", "Marker") == "english", "Only the selected language is loaded.");
    Assert(retail.ExcludedFiles.Any(value => value.Path == "Data/INI/GameDataDebug.ini" && value.Class == IniFileClass.DebugOnly), "The debug file is excluded as DebugOnly.");
    Assert(retail.ExcludedFiles.Any(value => value.Path == "Data/Scripts/Scripts.ini" && value.Class == IniFileClass.ToolOnly), "Scripts.ini is tool only.");
    Assert(retail.ExcludedFiles.Any(value => value.Path == "Data/Chinese/Language9x.ini" && value.Class == IniFileClass.LanguageVariant), "9x variants are excluded.");
    Assert(retail.ExcludedFiles.Any(value => value.Path == "Data/INI/Unknown.ini" && value.Class == IniFileClass.Unclassified), "Strict plans exclude unclassified files visibly.");
    Assert(retail.ExcludedFiles.Count(value => value.Class == IniFileClass.MapOverlay) == 2, "Excluded map overlays are listed.");

    var full = DefinitionCompiler.Compile(new MemorySource(files), options: strict);
    Assert(Text(full, "gamedata://gamedata", "Marker") == "map2", "Map overlays apply after the base data, in path order.");
    Assert(full.LoadedFiles.Last().Class == IniFileClass.MapOverlay, "Map overlays load last.");

    var german = DefinitionCompiler.Compile(new MemorySource(files), options: strict with { Language = "German", MapOverlays = MapOverlayMode.Exclude });
    Assert(Text(german, "compatibility://language/language", "Marker") == "german", "The language is selectable.");

    var lenient = DefinitionCompiler.Compile(new MemorySource(files), options: DefinitionCompilerOptions.Audit with { MapOverlays = MapOverlayMode.Exclude });
    Assert(lenient.LoadedFiles.Any(value => value.Path == "Data/INI/Unknown.ini" && value.Class == IniFileClass.Unclassified), "Lenient plans still load unclassified files, after all retail files.");
    Assert(lenient.LoadedFiles.Select(value => value.Class).ToArray() is [.., IniFileClass.Unclassified], "Unclassified files load last.");
}

static void MultiFileDefinitionsReportWinners()
{
    var files = new Dictionary<string, string>
    {
        ["Data/INI/Default/Object.ini"] = "Object DefaultThingTemplate\nEnd\n",
        ["Data/INI/Object/B.ini"] = "Object Twice\n  BuildCost = 1\nEnd\n",
        ["Data/INI/Object/Sub/A.ini"] = "Object Twice\n  BuildCost = 2\nEnd\n",
        ["Data/INI/Object/C.ini"] = "Object Twice\n  BuildCost = 3\nEnd\nObject Single\nEnd\n",
    };
    var graph = DefinitionCompiler.Compile(new MemorySource(files), options: DefinitionCompilerOptions.Audit with { StrictLoadPlan = true });
    var twice = graph.MultiFileDefinitions.Single();
    // INI::loadDirectory: files directly in the directory first (sorted), then sub-directory files.
    Assert(twice.Files.SequenceEqual(["Data/INI/Object/B.ini", "Data/INI/Object/C.ini", "Data/INI/Object/Sub/A.ini"]), "Directory order: " + string.Join(";", twice.Files));
    Assert(twice.Winner == "Data/INI/Object/Sub/A.ini" && twice.OrderSourced, "The last loaded file wins.");
    var twiceCost = ((Money)graph.Definitions.OfType<ObjectDefinition>().Single(value => value.OriginalName == "Twice").Fields.Single(field => field.Name == "BuildCost").Value.Value).Value;
    Assert(twiceCost == 2, "Load order and winner must agree.");
}

static void RepeatableKeysKeepMultiplicity()
{
    var graph = DefinitionCompiler.Compile(One("""
    Armor Body
      Armor = DEFAULT 100%
      Armor = FLAME 50%
      Armor = DEFAULT 80%
    End
    Weapon Gun
      ScatterTarget = X:0 Y:1
      ScatterTarget = X:1 Y:0
      AntiAirborneVehicle = Yes
      AntiAirborneVehicle = No
    End
    Object Walker
      Locomotor = SET_NORMAL A
      Locomotor = SET_WANDER B C
      Locomotor = SET_NORMAL D
    End
    """));
    var armor = graph.Definitions.OfType<ArmorDefinition>().Single();
    Assert(armor.Fields.Select(field => field.Value.OriginalText).SequenceEqual(["DEFAULT 80%", "FLAME 50%"]), "Indexed keys replace in place: " + string.Join(";", armor.Fields.Select(field => field.Value.OriginalText)));
    var weapon = graph.Definitions.OfType<WeaponDefinition>().Single();
    Assert(weapon.Fields.Where(field => field.Name == "ScatterTarget").Select(field => ((Vector2Value)field.Value.Value).X).SequenceEqual([0m, 1m]), "Accumulated keys keep multiplicity and order.");
    Assert(weapon.Fields.Single(field => field.Name == "AntiAirborneVehicle").Value.OriginalText == "No", "Scalar repeats: last wins.");
    Assert(graph.Audit.Compatibility.Any(value => value.Category == "duplicate-key" && value.Name == "Weapon/AntiAirborneVehicle" && value.Count == 1), "Scalar repeats are counted.");
    var walker = graph.Definitions.OfType<ObjectDefinition>().Single();
    var sets = walker.Fields.Where(field => field.Name == "Locomotor").Select(field => (TaggedReferenceList)field.Value.Value).ToArray();
    Assert(sets.Length == 2 && sets[0].Tag == "SET_NORMAL" && sets[0].References[0].Name == "D" && sets[1].Tag == "SET_WANDER" && sets[1].References.Length == 2, "Locomotor sets are indexed by set name.");
}

static void DefaultThingTemplateIsInheritedWithInterfaceRules()
{
    const string defaultObject = """
    Object DefaultThingTemplate
      Side = Civilian
      KindOf = NONE
      Body = InactiveBody ModuleTag_DefaultInactiveBody
      End
      Behavior = DestroyDie ModuleTag_DefaultDestroyDie
      End
      Draw = W3DDefaultDraw ModuleTag_DefaultDraw
      End
      InheritableModule
        Behavior = AutoHealBehavior ModuleTag_DefaultAutoHealBehavior
          HealingAmount = 10
        End
      End
      OverrideableByLikeKind
        Behavior = StealthUpdate ModuleTag_DefaultStealthUpdate
          StealthDelay = 2000
        End
      End
      WeaponSet
        Conditions = None
      End
      ArmorSet
        Conditions = None
      End
    End
    """;
    var files = new Dictionary<string, string>
    {
        ["Data/INI/Default/Object.ini"] = defaultObject,
        ["Data/INI/Object/Test.ini"] = """
        Object Rock
          Body = ActiveBody T1
            MaxHealth = 10
            InitialHealth = 10
          End
          Behavior = SlowDeathBehavior T2
          End
        End
        Object Soldier
          KindOf = INFANTRY
          IsTrainable = Yes
          Body = ActiveBody T1
            MaxHealth = 10
            InitialHealth = 10
          End
          Behavior = FlammableUpdate T2
          End
          WeaponSet
            Conditions = None
          End
          WeaponSet
            Conditions = PLAYER_UPGRADE
          End
        End
        Object Prop
        End
        """,
    };
    var graph = DefinitionCompiler.Compile(new MemorySource(files), ModuleRegistry.Default, DefinitionCompilerOptions.Audit with { StrictLoadPlan = true });
    string[] Modules(string name) => graph.Definitions.OfType<ObjectDefinition>().Single(value => value.OriginalName == name).Modules.Select(value => value.Name).ToArray();
    // Rock: ActiveBody replaces InactiveBody (BODY); SlowDeath (UPDATE|DIE) drops DestroyDie (DIE), the untrainable AutoHeal (UPDATE) and, not being a
    // candidate kind, the overrideable StealthUpdate. Nothing shares DRAW, so the default draw stays.
    Assert(Modules("Rock").SequenceEqual(["W3DDefaultDraw", "ActiveBody", "SlowDeathBehavior"]), "Rock: " + string.Join(",", Modules("Rock")));
    // Soldier: trainable, INFANTRY candidate -> inherited AutoHeal and StealthUpdate stay; DestroyDie stays (no DIE module).
    Assert(Modules("Soldier").SequenceEqual(["DestroyDie", "W3DDefaultDraw", "AutoHealBehavior", "StealthUpdate", "ActiveBody", "FlammableUpdate"]), "Soldier: " + string.Join(",", Modules("Soldier")));
    // Prop defines nothing: everything is inherited unchanged.
    Assert(Modules("Prop").SequenceEqual(["InactiveBody", "DestroyDie", "W3DDefaultDraw", "AutoHealBehavior", "StealthUpdate"]), "Prop: " + string.Join(",", Modules("Prop")));
    var soldier = graph.Definitions.OfType<ObjectDefinition>().Single(value => value.OriginalName == "Soldier");
    Assert(soldier.Sections.Select(value => value.Declaration).SequenceEqual(["ArmorSet", "WeaponSet", "WeaponSet"]), "The first explicit WeaponSet replaces the default sets only once.");
    Assert(((FlagsValue)soldier.Fields.Single(field => field.Name == "KindOf").Value.Value).Names.SequenceEqual(["INFANTRY"]), "Own fields override the default.");
    Assert(soldier.Fields.Single(field => field.Name == "Side").Value.OriginalText == "Civilian", "Default fields are inherited.");
}

static void ReskinAndModuleEditsFollowTheReference()
{
    var graph = DefinitionCompiler.Compile(One("""
    Object Base
      KindOf = STRUCTURE
      BuildCost = 5
      Draw = W3DModelDraw D1
      End
      Body = ActiveBody B1
        MaxHealth = 10
      End
    End
    ObjectReskin Skin Base
      Geometry = BOX
      Draw = W3DModelDraw D2
      End
    End
    """), ModuleRegistry.Default);
    var skin = graph.Definitions.OfType<ObjectDefinition>().Single(value => value.OriginalName == "Skin");
    Assert(skin.Modules.Select(value => value.Tag).SequenceEqual(["B1", "D2"]), "Reskin replaces the parent draw module and keeps behaviors: " + string.Join(",", skin.Modules.Select(value => value.Tag)));
    Assert(skin.Fields.Any(field => field.Name == "BuildCost"), "Reskin copies the parent.");
    Throws<DefinitionCompileException>(() => DefinitionCompiler.Compile(One("Object Base\nEnd\nObjectReskin Skin Base\n  BuildCost = 1\nEnd\n")), "may only set appearance keys");
    Throws<DefinitionCompileException>(() => DefinitionCompiler.Compile(One("ObjectReskin Skin Missing\nEnd\n")), "must come after");
    Throws<DefinitionCompileException>(() => DefinitionCompiler.Compile(One("""
    Object Base
      Body = ActiveBody B1
        MaxHealth = 10
      End
    End
    Object Base
      ReplaceModule B1
        Body = ImmortalBody B1
          MaxHealth = 10
        End
      End
    End
    """), ModuleRegistry.Default), "same type");
    Throws<DefinitionCompileException>(() => DefinitionCompiler.Compile(One("""
    Object Base
      Body = ActiveBody B1
        MaxHealth = 10
      End
    End
    Object Base
      ReplaceModule B1
        Body = ActiveBody B1
          MaxHealth = 20
        End
      End
    End
    """), ModuleRegistry.Default), "new unique tag");
    var edited = DefinitionCompiler.Compile(One("""
    Object Base
      Body = ActiveBody B1
        MaxHealth = 10
      End
      Behavior = FlammableUpdate F1
      End
    End
    Object Base
      ReplaceModule B1
        Body = ActiveBody B2
          MaxHealth = 20
        End
      End
      RemoveModule F1
      RemoveModule Ghost
    End
    """), ModuleRegistry.Default);
    Assert(edited.Definitions.OfType<ObjectDefinition>().Single().Modules.Select(value => value.Tag).SequenceEqual(["B2"]), "Replace appends at the end; remove drops by tag.");
    Assert(edited.Diagnostics.Any(value => value.Code == "compatibility-missing-remove-target"), "A missing remove target is diagnosed.");
    var repeatedTag = DefinitionCompiler.Compile(One("Object Twin\n  Behavior = FlammableUpdate T\n  End\n  Draw = W3DModelDraw T\n  End\nEnd\n"), ModuleRegistry.Default);
    Assert(repeatedTag.Definitions.OfType<ObjectDefinition>().Single().Modules.Length == 2 && repeatedTag.Diagnostics.Any(value => value.Code == "compatibility-duplicate-module-tag"),
        "A repeated module tag keeps both modules (retail behaviour) and is diagnosed.");
    var unclosed = DefinitionCompiler.Compile(One("Object Glitch\n  AddModule\n    Behavior = FlammableUpdate F1\n    End\n    Side = GLA\n    ArmorSet\n      Conditions = None\n    End\n  End\nEnd\n"), ModuleRegistry.Default);
    var glitch = unclosed.Definitions.OfType<ObjectDefinition>().Single();
    Assert(glitch.Modules.Length == 1 && glitch.Sections.Any(value => value.Declaration == "ArmorSet") && glitch.Fields.Any(field => field.Name == "Side"),
        "Fields and blocks inside AddModule apply to the object like the reference parser (no phantom module).");
}

// ---------------------------------------------------------------------------------------------- block 0.5

static void IdsAndSnapshotsIgnoreEnumerationOrder()
{
    var files = new Dictionary<string, string>
    {
        ["Data/INI/Default/Object.ini"] = "Object DefaultThingTemplate\n  Side = Civilian\nEnd\n",
        ["Data/INI/Object/A.ini"] = "Object Alpha\n  BuildCost = 1\nEnd\nWeapon Zeta\n  PrimaryDamage = 5\nEnd\n",
        ["Data/INI/Object/B.ini"] = "Object Beta\n  BuildCost = 2\nEnd\nWeapon Eta\n  PrimaryDamage = 6\nEnd\n",
        ["Data/INI/Weapon.ini"] = "Weapon Gun\n  PrimaryDamage = 7\nEnd\n",
        ["Maps/M/map.ini"] = "Object Alpha\n  BuildCost = 9\nEnd\n",
    };
    var keys = files.Keys.ToArray();
    var reference = DefinitionSnapshotWriter.WriteCanonical(DefinitionCompiler.Compile(new MemorySource(files, keys)));
    var referenceIds = DefinitionCompiler.Compile(new MemorySource(files, keys)).Definitions.Select(value => value.Id.ToString()).ToArray();
    var permutations = new[] { keys.Reverse().ToArray(), keys.OrderBy(value => value.Length).ThenByDescending(value => value, StringComparer.Ordinal).ToArray(), [keys[2], keys[4], keys[0], keys[3], keys[1]] };
    foreach (var permutation in permutations)
    {
        var graph = DefinitionCompiler.Compile(new MemorySource(files, permutation));
        Assert(DefinitionSnapshotWriter.WriteCanonical(graph).AsSpan().SequenceEqual(reference), "The snapshot must not depend on EnumerateIniFiles order.");
        Assert(graph.Definitions.Select(value => value.Id.ToString()).SequenceEqual(referenceIds), "Definition ids and order must be stable.");
    }
    var ids = referenceIds.ToArray();
    Assert(ids.SequenceEqual(ids.Order(StringComparer.Ordinal)), "Definitions are ordered ordinally by id.");
}

static void TruncatedInputsAreRejected()
{
    const string valid = "Object A\n  BuildCost = 1\n  Body = ActiveBody B\n    MaxHealth = 5\n  End\nEnd\n";
    DefinitionCompiler.Compile(One(valid), ModuleRegistry.Default);
    // Cutting only the final line break leaves a complete document; every shorter prefix must be rejected.
    DefinitionCompiler.Compile(One(valid[..^1]), ModuleRegistry.Default);
    for (var length = 1; length < valid.Length - 1; length++)
    {
        var text = valid[..length];
        try
        {
            DefinitionCompiler.Compile(One(text), ModuleRegistry.Default, DefinitionCompilerOptions.Audit with { StrictUnknownModules = true });
        }
        catch (Exception exception) when (exception is IniParseException or FormatException or DefinitionCompileException or ArgumentException)
        {
            continue;
        }
        throw new InvalidOperationException($"A truncated input was silently accepted: '{text.Replace("\n", "\\n")}'");
    }
}

static void FullCompilationsAreByteIdentical()
{
    var files = new Dictionary<string, string>
    {
        ["Data/INI/Default/Object.ini"] = "Object DefaultThingTemplate\n  Side = Civilian\n  Body = InactiveBody T\n  End\nEnd\n",
        ["Data/INI/Object/Units.ini"] = """
        Object Alpha
          KindOf = INFANTRY SELECTABLE
          Body = ActiveBody B
            MaxHealth = 10
            InitialHealth = 10
          End
          Behavior = LifetimeUpdate L
            MinLifetime = 100
            MaxLifetime = 200
          End
          Locomotor = SET_NORMAL Foot
        End
        """,
        ["Data/INI/Locomotor.ini"] = "Locomotor Foot\n  Speed = 30\nEnd\n",
    };
    var first = DefinitionSnapshotWriter.WriteCanonical(DefinitionCompiler.Compile(new MemorySource(files), ModuleRegistry.Default, DefinitionCompilerOptions.Audit with { StrictLoadPlan = true }));
    var second = DefinitionSnapshotWriter.WriteCanonical(DefinitionCompiler.Compile(new MemorySource(files.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value)), ModuleRegistry.Default, DefinitionCompilerOptions.Audit with { StrictLoadPlan = true }));
    Assert(first.AsSpan().SequenceEqual(second), "Two independent compilations must be byte identical.");
    Assert(!System.Text.Encoding.UTF8.GetString(first).Contains(":\\", StringComparison.Ordinal), "Snapshots must not contain absolute paths.");
}

static void FingerprintStreamingMatchesBuffer()
{
    var graph = DefinitionCompiler.Compile(One("Object A\n  BuildCost = 1\nEnd\n"));
    var buffered = DefinitionSnapshotWriter.WriteCanonical(graph);
    using var stream = new MemoryStream();
    DefinitionSnapshotWriter.WriteCanonical(graph, stream);
    Assert(stream.ToArray().AsSpan().SequenceEqual(buffered), "Streaming and buffered snapshots must be byte identical.");
    var (hash, length) = DefinitionSnapshotWriter.ComputeFingerprint(graph);
    Assert(length == buffered.Length && hash == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(buffered)), "The streaming fingerprint must equal the SHA-256 of the snapshot.");
}

static void RealDataCatalogMatchesObservedModules(string directory)
{
    var content = ContentFileSystem.Open(directory);
    var source = new ContentSource(content);
    var graph = DefinitionCompiler.Compile(source, ModuleRegistry.Default, DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true });
    var observed = graph.Definitions.SelectMany(value => value.Modules).Select(value => value.Name).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray();
    var catalog = ModuleRegistry.Default.Descriptors.Select(value => value.Name).Order(StringComparer.Ordinal).ToArray();
    Assert(observed.SequenceEqual(catalog), $"Catalog ({catalog.Length}) and observed module set ({observed.Length}) differ: "
        + string.Join(",", catalog.Except(observed).Concat(observed.Except(catalog))));
    Assert(graph.Audit.Compatibility.All(value => !(value.Category == "module" && value.PlannedTarget == "unknown-module-not-in-catalog")), "No module may be uncatalogued.");
    Console.WriteLine($"Real data: catalog == observed module set ({observed.Length} module types).");

    // Drift guard: the compatibility inventory must equal the reviewed, checked-in baseline.
    var root = AppContext.BaseDirectory;
    while (root is not null && !File.Exists(Path.Combine(root, "CnCNew.slnx"))) root = Path.GetDirectoryName(root);
    if (root is not null && File.Exists(Path.Combine(root, "baselines", "compatibility.json")))
    {
        var differences = CompatibilityBaseline.Compare(File.ReadAllText(Path.Combine(root, "baselines", "compatibility.json")), graph.Audit.Compatibility);
        Assert(differences.Count == 0, "The compatibility inventory drifted from baselines/compatibility.json:\n  " + string.Join("\n  ", differences.Take(20)));
        Console.WriteLine($"Real data: compatibility inventory equals the baseline ({graph.Audit.Compatibility.Length} entries).");
    }
}

static void ObjectCreationListsAreTypedNuggets()
{
    const string valid = """
    Object Bit
    End
    FXList FX_Final
    End
    ObjectCreationList OCL_Test
      CreateObject
        ObjectNames = Bit
        Count = 3
        Offset = X:0 Y:0 Z:5
        DispositionIntensity = 0.1 End
      End
      CreateDebris
        ModelNames = ModelA ignoredA ModelB ignoredB
        FXFinal = FX_Final
      End
      DeliverPayload
        Payload = Bit 2
        Transport = Bit
      End
    End
    """;
    var options = DefinitionCompilerOptions.Audit with { StrictReferences = true };
    var graph = DefinitionCompiler.Compile(One(valid), options: options);
    var list = graph.Definitions.OfType<ObjectCreationListDefinition>().Single();
    Assert(list.Sections.Length == 3 && list.Sections[0].Declaration == "CreateObject", "Nuggets must be kept in order.");
    Assert(graph.Diagnostics.Any(value => value.Code == "compatibility-ignored-tokens"), "The retail name-pair quirk must be a counted compatibility case.");
    Assert(graph.References.Any(edge => edge.Target.Name == "Bit" && edge.Field.EndsWith("Payload", StringComparison.Ordinal) && edge.Resolved), "A counted payload must produce a reference edge.");

    var redefined = DefinitionCompiler.Compile(One(valid + "\nObjectCreationList OCL_Test\n  FireWeapon\n  End\nEnd\n".Replace("FireWeapon", "ApplyRandomForce")), options: options);
    Assert(redefined.Definitions.OfType<ObjectCreationListDefinition>().Single().Sections.Length == 1, "A repeated definition must replace the earlier nuggets.");

    Throws<IniParseException>(() => DefinitionCompiler.Compile(One("ObjectCreationList A\n  CreateSomething\n  End\nEnd\n")), "Unexpected End");
    Throws<DefinitionCompileException>(() => DefinitionCompiler.Compile(One("ObjectCreationList A\n  CreateObject\n    Bogus = 1\n  End\nEnd\n")), "Unknown key");
    Throws<DefinitionCompileException>(() => DefinitionCompiler.Compile(One("ObjectCreationList A\n  Foo = 1\nEnd\n")), "nugget blocks only");
}

static MemorySource One(string text) => new(new Dictionary<string, string> { ["fixture.ini"] = text }, ["fixture.ini"]);

static void WeaponBonusesAreTypedAndStrict()
{
    var graph = DefinitionCompiler.Compile(One("""
    Weapon Gun
      WeaponBonus = VETERAN DAMAGE 125%
      WeaponBonus = VETERAN RATE_OF_FIRE 120%
      WeaponBonus = ELITE DAMAGE 150%
      WeaponBonus = VETERAN DAMAGE 130%
    End
    GameData
      HealthBonus_Veteran = 120%
      WeaponBonus = HERO RANGE 110%
    End
    """));
    var weapon = graph.Definitions.OfType<WeaponDefinition>().Single();
    var bonuses = weapon.Fields.Where(field => field.Name == "WeaponBonus").Select(field => (WeaponBonusValue)field.Value.Value).ToArray();
    Assert(bonuses.Length == 3 && bonuses.Count(value => value is { Condition: "VETERAN", Field: "DAMAGE" }) == 1, "WeaponBonus lines are indexed by condition and field.");
    Assert(bonuses.Single(value => value is { Condition: "VETERAN", Field: "DAMAGE" }).Ratio == 1.3m, "The last duplicate wins, percentages become ratios.");
    Throws<FormatException>(() => DefinitionCompiler.Compile(One("Weapon W\n  WeaponBonus = TYPO DAMAGE 125%\nEnd\n")), "not a member of WeaponBonusCondition");
    Throws<FormatException>(() => DefinitionCompiler.Compile(One("Weapon W\n  WeaponBonus = VETERAN SPEED 125%\nEnd\n")), "not a member of WeaponBonusField");
    Throws<FormatException>(() => DefinitionCompiler.Compile(One("Weapon W\n  WeaponBonus = VETERAN DAMAGE 125\nEnd\n")), "percentage");
    Assert(NameTablesPhase5.WeaponBonusCondition.Count == 27, "WeaponBonus condition table size.");
}

// Phase 18, D9: the definition fingerprint is the SHA-256 of a JSON text. Its line ends used to be the platform's (CRLF on Windows, LF elsewhere) and its string
// escaping is the runtime's default JSON encoder. The line end and the indentation are pinned now; the escaping is guarded by this golden value: a runtime that
// escapes differently fails here instead of silently rejecting every other machine.
static void FingerprintEncodingIsPinned()
{
    // Every class of character the default encoder treats specially: HTML-sensitive ASCII, quote, backslash, non-ASCII (Latin-1 source), and plain text.
    var graph = DefinitionCompiler.Compile(One("Weapon Gun\n  PrimaryDamage = 10\nEnd\nObject T\u00E4nk\n  CommandSet = Cmds\n  DisplayName = OBJECT:<'+&`\"q\\x>\u00F6\u00DF\nEnd\n"));
    var bytes = DefinitionSnapshotWriter.WriteCanonical(graph);
    var text = System.Text.Encoding.UTF8.GetString(bytes);
    var (sha, length) = DefinitionSnapshotWriter.ComputeFingerprint(graph);
    Assert(length == bytes.Length && sha == Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)), "The streamed fingerprint is the hash of the canonical bytes.");
    var lineFeeds = text.Count(value => value == '\n');
    Assert(lineFeeds > 20 && lineFeeds == System.Text.RegularExpressions.Regex.Matches(text, "\r\n").Count, "The canonical snapshot ends its lines with CR LF on every platform.");
    Assert(text.Contains("\n  \"definitions\": [\r\n    {", StringComparison.Ordinal), "The canonical snapshot is indented with two spaces.");
    foreach (var escape in new[] { "\\u003C", "\\u0027", "\\u002B", "\\u0026", "\\u0060", "\\u0022", "\\\\x", "\\u003E", "\\u00F6\\u00DF", "T\\u00E4nk" })
        Assert(text.Contains(escape, StringComparison.Ordinal), $"The canonical snapshot escapes as expected: {escape} is missing.");
    Console.WriteLine($"  fingerprint encoding: {bytes.Length} bytes, SHA-256 {sha}");
    Assert(sha == "024D8B2194B55BC497B1BD2DF50DFF4B5EE3A70BDC5256485D8E6FA28AD8BA43" && bytes.Length == 1884, $"The canonical encoding changed: SHA-256 {sha} of {bytes.Length} bytes.");
}

// Phase 18, D2: the integer conversion of single-precision bits is the runtime's (decimal)float, value and scale, for every bit pattern tried.
static void SingleBitsMatchTheRuntimeCast()
{
    static bool Same(decimal a, decimal b)
    {
        Span<int> left = stackalloc int[4];
        Span<int> right = stackalloc int[4];
        decimal.GetBits(a, left);
        decimal.GetBits(b, right);
        return left.SequenceEqual(right);
    }

    long compared = 0, overflow = 0;
    void Compare(int bits)
    {
        var single = BitConverter.Int32BitsToSingle(bits);
        if (!float.IsFinite(single))
        {
            Assert(!CnCNew.Data.World.SingleBits.IsFinite(bits), $"Bits {bits:X8} are not finite.");
            Throws<ArgumentException>(() => CnCNew.Data.World.SingleBits.ToDecimal(bits), "not a finite number");
            return;
        }
        Assert(CnCNew.Data.World.SingleBits.IsFinite(bits), $"Bits {bits:X8} are finite.");
        decimal expected;
        try
        {
            expected = (decimal)single;
        }
        catch (OverflowException)
        {
            overflow++;
            Throws<OverflowException>(() => CnCNew.Data.World.SingleBits.ToDecimal(bits), "does not fit a decimal");
            return;
        }
        var actual = CnCNew.Data.World.SingleBits.ToDecimal(bits);
        compared++;
        if (!Same(expected, actual))
            throw new InvalidOperationException($"Single bits {bits:X8} ({single:R}): the runtime cast gives {expected} (scale {expected.Scale}), the integer routine {actual} (scale {actual.Scale}).");
    }

    // Hand-picked: zeros, subnormals, the limits of the decimal range, exact halves of the seventh digit, powers of two and ten, map-like coordinates.
    foreach (var value in new[] { 0f, -0f, float.Epsilon, -float.Epsilon, 1f, -1f, 0.1f, 0.5f, 1.5f, 2.5f, 10f, 100f, 1e6f, 1e7f, 9999999f, 9999999.5f, 16777216f, 16777217f, 1e-7f, 1e-10f, 1e-28f,
                 5e-29f, 4.9e-29f, 1e-30f, 7.9e28f, 7.92e28f, 7.93e28f, 1e29f, float.MaxValue, float.MinValue, 1234567.5f, 123456.75f, 0.0625f, 3.1415927f, -1.5707964f, 2560f, 1265.625f,
                 0.3333333f, 0.6666667f, 999999.94f, 999999.97f, 99999.99f, 9999999.9f, 1e22f, 1e23f, 3.4e-5f, float.PositiveInfinity, float.NegativeInfinity, float.NaN })
        Compare(BitConverter.SingleToInt32Bits(value));

    // Every exponent with a spread of mantissas, both signs: 256 * 2 * 4096 patterns.
    for (var exponent = 0; exponent < 256; exponent++)
        for (var step = 0; step < 4096; step++)
        {
            var mantissa = (int)((uint)step * 2_654_435_761u >> 9) & 0x7FFFFF;
            Compare((exponent << 23) | mantissa);
            Compare(unchecked((int)0x80000000) | (exponent << 23) | mantissa);
        }

    // A fixed pseudo-random walk over the whole 32-bit space (xorshift32), and one concentrated on the magnitudes of map coordinates (2^-8 .. 2^16).
    var state = 0x9E3779B9u;
    for (var index = 0; index < 4_000_000; index++)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        Compare(unchecked((int)state));
        var exponent = 119 + (int)(state % 24);
        Compare((int)((state & 0x807FFFFFu) | ((uint)exponent << 23)));
    }
    Console.WriteLine($"  single bits -> decimal: {compared} bit patterns identical to the runtime cast (value and scale), {overflow} overflow alike.");
    Assert(compared > 9_000_000 && overflow > 0, "The comparison covered the range.");
}

static void Assert(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static void Throws<T>(Action action, string messagePart) where T : Exception
{
    try { action(); }
    catch (T exception) when (exception.Message.Contains(messagePart, StringComparison.OrdinalIgnoreCase)) { return; }
    catch (Exception exception) { throw new InvalidOperationException($"Expected {typeof(T).Name} containing '{messagePart}', got {exception.GetType().Name}: {exception.Message}"); }
    throw new InvalidOperationException($"Expected {typeof(T).Name} containing '{messagePart}', but nothing was thrown.");
}

file sealed class MemorySource : IDefinitionSource
{
    private readonly IReadOnlyDictionary<string, string> _files;
    private readonly IReadOnlyList<string> _roots;
    public MemorySource(IReadOnlyDictionary<string, string> files, IReadOnlyList<string>? roots = null)
    {
        _files = files;
        _roots = roots ?? [..files.Keys];
    }
    public IEnumerable<string> EnumerateIniFiles() => _roots;
    public Stream OpenRead(string virtualPath) => new MemoryStream(System.Text.Encoding.Latin1.GetBytes(_files[virtualPath]));
    public bool Exists(string virtualPath) => _files.ContainsKey(virtualPath);
}

file sealed class ContentSource(ContentFileSystem content) : IDefinitionSource
{
    public IEnumerable<string> EnumerateIniFiles() => content.EnumerateFiles().Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
    public Stream OpenRead(string virtualPath) => content.OpenRead(virtualPath);
    public bool Exists(string virtualPath) => content.Exists(virtualPath);
}

