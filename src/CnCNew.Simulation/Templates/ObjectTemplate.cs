using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.Templates;

/// <summary>A simulation module of a template: its binding, its typed data, and its position in declaration order.</summary>
public sealed record ModuleSpec(string Name, string Tag, IModuleBinding Binding, ModuleData Data);

/// <summary>
/// One alternative-satisfies-the-line requirement of a <c>Prerequisites</c> block (<c>ThingTemplate::parsePrerequisites</c>):
/// an <c>Object</c> line names alternative built units, a <c>Science</c> line names one required science. All lines are AND'ed.
/// </summary>
public sealed record PrerequisiteLine(bool IsScience, ImmutableArray<string> Alternatives);

/// <summary>The terrain classes a locomotor may cross (<c>Locomotor::Surfaces</c>, <c>LocomotorSurfaceType</c>).</summary>
[Flags]
public enum LocomotorSurfaces
{
    None = 0,
    Ground = 1,
    Water = 2,
    Cliff = 4,
    Air = 8,
    Rubble = 16,
}

/// <summary>
/// Movement constants of one <c>Locomotor</c> selection, already converted to simulation units. Without a terrain the object
/// moves in a straight line and turns instantly (the phase 5 mode, kept for terrain-free worlds); with a terrain it follows
/// a path over the navigation grid, limited by <see cref="TurnRatePerTick"/> and by the <see cref="Surfaces"/> it can cross.
/// </summary>
public sealed record LocomotorInfo(Fixed Speed, Fixed Acceleration, Fixed TurnRatePerTick, Fixed Braking, Fixed MinSpeed, Fixed CloseEnoughDist,
    LocomotorSurfaces Surfaces = LocomotorSurfaces.None)
{
    /// <summary><c>Locomotor::m_preferredHeight</c>: the flight height above the terrain of an aircraft (0 for ground locomotors).</summary>
    public Fixed PreferredHeight { get; init; }

    /// <summary><c>Locomotor::m_speedLimitZ</c>: the fastest an airborne object descends under its own control, per tick (0 when the locomotor sets none).</summary>
    public Fixed SpeedLimitZ { get; init; }

    /// <summary><c>Locomotor::m_lift</c>: the upward acceleration of an aircraft at full lift, per tick² (<c>Lift</c> in the data is per second²).</summary>
    public Fixed Lift { get; init; }
}

/// <summary>
/// The runtime view of an <c>Object</c> definition. Presentation modules (Draw, ClientUpdate) are listed but never
/// instantiated; a simulation module without implementation makes the template not instantiable and is named in
/// <see cref="MissingModules"/> (never dropped silently).
/// </summary>
public sealed class ObjectTemplate
{
    internal ObjectTemplate(int index, ObjectDefinition definition, ImmutableArray<ModuleSpec> modules,
        ImmutableArray<string> presentationModules, ImmutableArray<string> missingModules,
        ArmorInfo defaultArmor, ArmorInfo? playerUpgradeArmor, ImmutableArray<ArmorInfo?> veteranArmor,
        int defaultWeapon, int playerUpgradeWeapon, ImmutableArray<int> veteranWeapon, LocomotorInfo? locomotor,
        ImmutableArray<PrerequisiteLine> prerequisites, ImmutableArray<string> commandSets)
    {
        CommandSets = commandSets;
        Buildable = definition.Fields.LastOrDefault(field => field.Name.Equals("Buildable", StringComparison.OrdinalIgnoreCase))?.Value.Value is EnumValue buildable ? buildable.Name : "Yes";
        var laserWidth = definition.Modules.FirstOrDefault(module => module.Name.Equals("W3DLaserDraw", StringComparison.OrdinalIgnoreCase))?.Fields
            .LastOrDefault(field => field.Name.Equals("OuterBeamWidth", StringComparison.OrdinalIgnoreCase))?.Value.OriginalText.Trim();
        LaserBeamRadius = laserWidth is not null && decimal.TryParse(laserWidth, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var width) ? Fixed.FromDecimal(width / 2m) : Fixed.Zero;
        Index = index;
        Id = definition.Id;
        Name = definition.OriginalName;
        Modules = modules;
        PresentationModules = presentationModules;
        MissingModules = missingModules;
        Side = Text(definition, "Side");
        KindOf = definition.Fields.LastOrDefault(field => field.Name.Equals("KindOf", StringComparison.OrdinalIgnoreCase))?.Value.Value is FlagsValue flags ? flags.Names : [];
        Geometry = definition.Fields.LastOrDefault(field => field.Name.Equals("Geometry", StringComparison.OrdinalIgnoreCase))?.Value.Value is EnumValue geometry ? geometry.Name : string.Empty;
        GeometryMajorRadius = Real(definition, "GeometryMajorRadius");
        GeometryMinorRadius = Real(definition, "GeometryMinorRadius");
        GeometryHeight = Real(definition, "GeometryHeight");
        GeometryIsSmall = definition.Fields.LastOrDefault(field => field.Name.Equals("GeometryIsSmall", StringComparison.OrdinalIgnoreCase))?.Value.Value is true;
        VisionRange = Real(definition, "VisionRange");
        ShroudClearingRange = definition.Fields.Any(field => field.Name.Equals("ShroudClearingRange", StringComparison.OrdinalIgnoreCase)) ? Real(definition, "ShroudClearingRange") : VisionRange;
        TransportSlotCount = definition.Fields.LastOrDefault(field => field.Name.Equals("TransportSlotCount", StringComparison.OrdinalIgnoreCase))?.Value.Value is long slots ? checked((int)slots) : 0;
        BuildCost = definition.Fields.LastOrDefault(field => field.Name.Equals("BuildCost", StringComparison.OrdinalIgnoreCase))?.Value.Value is Money cost ? Fixed.FromInt(cost.Value) : Fixed.Zero;
        BuildTicks = definition.Fields.LastOrDefault(field => field.Name.Equals("BuildTime", StringComparison.OrdinalIgnoreCase))?.Value.Value is decimal seconds ? SimulationTiming.SecondsToTicks(seconds) : 0;
        EnergyProduction = definition.Fields.LastOrDefault(field => field.Name.Equals("EnergyProduction", StringComparison.OrdinalIgnoreCase))?.Value.Value is long energy ? checked((int)energy) : 0;
        EnergyBonus = definition.Fields.LastOrDefault(field => field.Name.Equals("EnergyBonus", StringComparison.OrdinalIgnoreCase))?.Value.Value is long bonus ? checked((int)bonus) : 0;
        MaxSimultaneousOfType = definition.Fields.LastOrDefault(field => field.Name.Equals("MaxSimultaneousOfType", StringComparison.OrdinalIgnoreCase))?.Value.Value is long maxType ? checked((int)maxType) : 0;
        IsTrainable = definition.Fields.LastOrDefault(field => field.Name.Equals("IsTrainable", StringComparison.OrdinalIgnoreCase))?.Value.Value is true;
        ExperienceValue = IntegerArray(definition, "ExperienceValue");
        ExperienceRequired = IntegerArray(definition, "ExperienceRequired");
        DefaultArmor = defaultArmor;
        PlayerUpgradeArmor = playerUpgradeArmor;
        VeteranArmor = veteranArmor;
        DefaultWeapon = defaultWeapon;
        PlayerUpgradeWeapon = playerUpgradeWeapon;
        VeteranWeapon = veteranWeapon;
        Locomotor = locomotor;
        Prerequisites = prerequisites;
        CrusherLevel = definition.Fields.LastOrDefault(field => field.Name.Equals("CrusherLevel", StringComparison.OrdinalIgnoreCase))?.Value.Value is long crusher ? checked((int)crusher) : 0;
        CrushableLevel = definition.Fields.LastOrDefault(field => field.Name.Equals("CrushableLevel", StringComparison.OrdinalIgnoreCase))?.Value.Value is long crushable ? checked((int)crushable) : 255;
    }

    /// <summary>Position in the ordinally sorted list of all object definitions (stable for one definition graph).</summary>
    public int Index { get; }
    public DefinitionId Id { get; }
    public string Name { get; }
    public string Side { get; }
    public ImmutableArray<string> KindOf { get; }
    public string Geometry { get; }
    public Fixed GeometryMajorRadius { get; }

    /// <summary><c>W3DLaserDraw::getLaserTemplateWidth</c>: half the <c>OuterBeamWidth</c> of a laser object's draw module (0 for any other object); the logic reads it for the radius of a beam (<c>LaserUpdate::getCurrentLaserRadius</c>).</summary>
    public Fixed LaserBeamRadius { get; }
    public Fixed GeometryMinorRadius { get; }
    public Fixed GeometryHeight { get; }

    public bool IsBox => Geometry.Equals("BOX", StringComparison.OrdinalIgnoreCase);
    private bool IsSphere => Geometry.Equals("SPHERE", StringComparison.OrdinalIgnoreCase);

    /// <summary><c>GeometryInfo::getBoundingCircleRadius</c> (<c>calcBoundingStuff</c>): the corner distance of a box, the major radius of a cylinder or sphere.</summary>
    public Fixed BoundingCircleRadius => IsBox ? Fixed.Sqrt(GeometryMajorRadius * GeometryMajorRadius + GeometryMinorRadius * GeometryMinorRadius) : GeometryMajorRadius;

    /// <summary><c>GeometryInfo::getBoundingSphereRadius</c>: a sphere its radius, a cylinder half its height (at least the radius), a box the half-diagonal.</summary>
    public Fixed BoundingSphereRadius
    {
        get
        {
            var halfHeight = GeometryHeight / Fixed.FromInt(2);
            if (IsBox) return Fixed.Sqrt(GeometryMajorRadius * GeometryMajorRadius + GeometryMinorRadius * GeometryMinorRadius + halfHeight * halfHeight);
            if (IsSphere) return GeometryMajorRadius;
            return halfHeight < GeometryMajorRadius ? GeometryMajorRadius : halfHeight;
        }
    }

    /// <summary><c>GeometryInfo::getZDeltaToCenterPosition</c>: how far above the object position the geometry centre lies (a sphere is centred on its position).</summary>
    public Fixed CenterZDelta => IsSphere ? Fixed.Zero : GeometryHeight / Fixed.FromInt(2);
    /// <summary><c>ThingTemplate::m_isGeometrySmall</c>: small geometry never blocks the pathfinding grid.</summary>
    public bool GeometryIsSmall { get; }
    /// <summary><c>ThingTemplate::m_visionRange</c>: <c>StealthDetectorUpdate</c>'s <c>DetectionRange</c> falls back to this when unset (the shipped default: every occurrence leaves <c>DetectionRange</c> commented out).</summary>
    public Fixed VisionRange { get; }
    /// <summary><c>ThingTemplate::m_shroudClearingRange</c>: how far the object reveals the shroud; the reference default (-1) means <see cref="VisionRange"/> (Object.cpp, <c>Object::Object</c>).</summary>
    public Fixed ShroudClearingRange { get; }
    /// <summary><c>ThingTemplate::getTransportSlotCount</c>: how many <c>TransportContain</c> slots this object occupies (0 = not transportable).</summary>
    public int TransportSlotCount { get; }
    public Fixed BuildCost { get; }
    public long BuildTicks { get; }
    public int EnergyProduction { get; }
    /// <summary>The object's own <c>CommandSet</c> plus every set a <c>CommandSetUpgrade</c> module can switch to (all of them, whatever the upgrade state).</summary>
    public ImmutableArray<string> CommandSets { get; }
    /// <summary><c>ThingTemplate::getEnergyBonus</c>: extra production a <c>PowerPlantUpgrade</c> adds to the owner.</summary>
    public int EnergyBonus { get; }
    /// <summary>
    /// <c>ThingTemplate::getMaxSimultaneousOfType</c> (<c>Player::canBuildMoreOfType</c>); 0 (absent key) means unlimited.
    /// <c>MaxSimultaneousLinkKey</c> (grouping several templates under one shared cap) is not modeled: no object in this
    /// grouping is counted beyond its own template index, so a link-keyed family's effective cap is looser than retail's.
    /// </summary>
    public int MaxSimultaneousOfType { get; }
    /// <summary><c>ThingTemplate::m_isTrainable</c>: only trainable objects take over a veterancy level from an object creation list.</summary>
    public bool IsTrainable { get; }
    /// <summary><c>ThingTemplate::m_experienceValue</c>: the experience a killer earns for destroying this object, by the victim's own veterancy level (absent key: all zero, the reference default).</summary>
    public ImmutableArray<int> ExperienceValue { get; }
    /// <summary><c>ThingTemplate::m_experienceRequired</c>: the experience at which this object reaches level 0..3 (absent key: all zero).</summary>
    public ImmutableArray<int> ExperienceRequired { get; }
    public ImmutableArray<ModuleSpec> Modules { get; }
    public ImmutableArray<string> PresentationModules { get; }
    public ImmutableArray<string> MissingModules { get; }
    public bool IsInstantiable => MissingModules.IsEmpty;

    /// <summary><c>ArmorSet Conditions = None</c>; every object has one (the compiled default, possibly all-1.0 coefficients).</summary>
    public ArmorInfo DefaultArmor { get; }
    /// <summary><c>ArmorSet Conditions = PLAYER_UPGRADE</c>, if the object declares one (selected once <c>ArmorUpgrade</c> completes).</summary>
    public ArmorInfo? PlayerUpgradeArmor { get; }
    /// <summary>
    /// <c>ArmorSet Conditions = VETERAN</c>/<c>ELITE</c>/<c>HERO</c> (index by <c>Entity.VeterancyLevel</c>, 1..3; index 0 is unused), if the object
    /// declares one. The original selects the best-scoring set via <c>SparseMatchFinder</c> across every combination of simultaneous
    /// conditions (veterancy, <c>PLAYER_UPGRADE</c>, second life, crate upgrades, ...); this only resolves single-condition sets (exactly one of
    /// <c>None</c>/<c>PLAYER_UPGRADE</c>/<c>VETERAN</c>/<c>ELITE</c>/<c>HERO</c>), matching the retail Zero Hour object data, where no shipped
    /// <c>ArmorSet</c>/<c>WeaponSet</c> ever combines two conditions (verified: zero occurrences of <c>Conditions = ... VETERAN|ELITE|HERO</c> in
    /// <c>.local/phase2-import-a</c>). A set requiring more than one simultaneous condition is not modeled and falls back to the next best
    /// single-condition match, a named simplification with no effect on the shipped game.
    /// </summary>
    public ImmutableArray<ArmorInfo?> VeteranArmor { get; }
    /// <summary>Index into the world's <see cref="WeaponCatalog"/> of the <c>WeaponSet Conditions=None</c> primary weapon, or -1.</summary>
    public int DefaultWeapon { get; }
    /// <summary>Primary weapon of the <c>WeaponSet Conditions = PLAYER_UPGRADE</c> variant, if any, else -1.</summary>
    public int PlayerUpgradeWeapon { get; }
    /// <summary>Primary weapon of the <c>WeaponSet Conditions = VETERAN</c>/<c>ELITE</c>/<c>HERO</c> variant (index 1..3), or -1. See <see cref="VeteranArmor"/> for the selection simplification.</summary>
    public ImmutableArray<int> VeteranWeapon { get; }

    /// <summary>
    /// Primary weapon of the <c>WeaponSet Conditions = CARBOMB</c> variant, or -1 (<c>ThingTemplate::findWeaponTemplateSet(WEAPONSET_CARBOMB)</c>; every shipped
    /// vehicle that has one declares it alone). Selected for an entity that <c>ConvertToCarBombCrateCollide</c> turned into a car bomb.
    /// </summary>
    public int CarBombWeapon { get; init; } = -1;

    /// <summary>
    /// Primary weapon of the <c>WeaponSet Conditions = CRATEUPGRADE_ONE</c> (index 1) / <c>CRATEUPGRADE_TWO</c> (index 2) variant, or -1 (index 0 is unused); selected for a salvager
    /// that picked up weapon crates (<see cref="World.Entity.CrateWeaponLevel"/>). Only the single-condition sets are resolved, like <see cref="VeteranWeapon"/>: the
    /// <c>CRATEUPGRADE_x PLAYER_UPGRADE</c> sets (which differ in the unsimulated secondary weapon only) fall back to the single-condition one.
    /// </summary>
    public ImmutableArray<int> CrateUpgradeWeapon { get; init; } = [-1, -1, -1];

    /// <summary><c>ArmorSet Conditions = CRATE_UPGRADE_ONE</c> (index 1) / <c>CRATE_UPGRADE_TWO</c> (index 2) variant, or <see langword="null"/>; the <c>SECOND_LIFE</c> combinations are not modeled.</summary>
    public ImmutableArray<ArmorInfo?> CrateUpgradeArmor { get; init; } = [null, null, null];

    /// <summary>
    /// The <c>SECONDARY</c> weapon of the unconditional <c>WeaponSet</c>, or -1. Entities fire one weapon here (<see cref="DefaultWeapon"/>); the secondary is only used by modules that
    /// address a weapon slot themselves (<c>AssistedTargetingUpdate</c>), with their own clip bookkeeping.
    /// </summary>
    public int SecondaryWeapon { get; init; } = -1;
    public LocomotorInfo? Locomotor { get; }

    /// <summary>Every locomotor set the object declares (<c>Locomotor = SET_X Name</c>, the first resolvable name of each), by set name (<c>SET_TAXIING</c>, <c>SET_SUPERSONIC</c>, ...); <see cref="Locomotor"/> is the <c>SET_NORMAL</c> entry.</summary>
    public ImmutableDictionary<string, LocomotorInfo> LocomotorSets { get; init; } = ImmutableDictionary<string, LocomotorInfo>.Empty;
    public ImmutableArray<PrerequisiteLine> Prerequisites { get; }

    /// <summary><c>ThingTemplate::m_crusherLevel</c> (<c>Object.cpp canCrushOrSquish</c>): crushes a <see cref="CrushableLevel"/> it exceeds; 0 (the reference default) crushes nothing.</summary>
    public int CrusherLevel { get; }
    /// <summary><c>ThingTemplate::m_crushableLevel</c>; 255 (the reference default) can never be crushed.</summary>
    public int CrushableLevel { get; }

    /// <summary><c>ThingTemplate::m_buildable</c> (<c>BuildableStatus</c>: <c>Yes</c>, <c>Ignore_Prerequisites</c>, <c>No</c>, <c>Only_By_AI</c>); absent key: <c>Yes</c>, the reference default.</summary>
    public string Buildable { get; }

    public bool IsKindOf(string kind) => KindOf.Contains(kind, StringComparer.OrdinalIgnoreCase);

    private static string Text(ObjectDefinition definition, string name)
        => definition.Fields.LastOrDefault(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.OriginalText.Trim() ?? string.Empty;

    private static ImmutableArray<int> IntegerArray(ObjectDefinition definition, string name)
    {
        var value = definition.Fields.LastOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.Value;
        return value is ImmutableArray<long> list ? [..list.Select(item => checked((int)item))] : [0, 0, 0, 0];
    }

    private static Fixed Real(ObjectDefinition definition, string name)
    {
        var field = definition.Fields.LastOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        return field?.Value.Value switch
        {
            Distance distance => Fixed.FromDecimal(distance.Value),
            CompatibilityValue compatibility => Fixed.FromDecimal(decimal.Parse(compatibility.NormalizedValue, CultureInfo.InvariantCulture)),
            _ => Fixed.Zero,
        };
    }
}

public sealed class TemplateBuildException(string message) : FormatException(message);

/// <summary>All static game data resolved from one compiled definition graph: objects, upgrades, weapons and armor.</summary>
public sealed class TemplateCatalog
{
    private readonly ImmutableDictionary<string, ObjectTemplate> _byName;

    private TemplateCatalog(ImmutableArray<ObjectTemplate> templates, CommandCatalog commands, UpgradeCatalog upgrades, WeaponCatalog weapons, ScienceCatalog sciences, SpecialPowerCatalog specialPowers,
        FactionCatalog factions, CrateCatalog crates, ObjectCreationListCatalog creationLists, Fixed unitDamagedThreshold, Fixed unitReallyDamagedThreshold, decimal gravity, Fixed baseRegenHealthPercentPerSecond, long baseRegenDelayTicks, Fixed standardMinefieldDistance, int valuePerSupplyBox, Fixed sellPercentage, ImmutableArray<Fixed> healthBonus, WeaponBonusTable globalWeaponBonus)
    {
        HealthBonus = healthBonus;
        GlobalWeaponBonus = globalWeaponBonus;
        ValuePerSupplyBox = valuePerSupplyBox;
        SellPercentage = sellPercentage;
        StandardMinefieldDistance = standardMinefieldDistance;
        BaseRegenHealthPercentPerSecond = baseRegenHealthPercentPerSecond;
        BaseRegenDelayTicks = baseRegenDelayTicks;
        Templates = templates;
        Commands = commands;
        Upgrades = upgrades;
        Weapons = weapons;
        Sciences = sciences;
        SpecialPowers = specialPowers;
        Factions = factions;
        Crates = crates;
        CreationLists = creationLists;
        Gravity = gravity;
        UnitDamagedThreshold = unitDamagedThreshold;
        UnitReallyDamagedThreshold = unitReallyDamagedThreshold;
        var builder = ImmutableDictionary.CreateBuilder<string, ObjectTemplate>(StringComparer.OrdinalIgnoreCase);
        foreach (var template in templates) builder[template.Name] = template;
        _byName = builder.ToImmutable();
    }

    public ImmutableArray<ObjectTemplate> Templates { get; }
    public CommandCatalog Commands { get; }
    public UpgradeCatalog Upgrades { get; }
    public WeaponCatalog Weapons { get; }
    public ScienceCatalog Sciences { get; }
    public SpecialPowerCatalog SpecialPowers { get; }
    public FactionCatalog Factions { get; }
    public CrateCatalog Crates { get; }
    public ObjectCreationListCatalog CreationLists { get; }
    /// <summary><c>GlobalData::m_gravity</c> in world units per second² (negative; <c>GameData.ini Gravity = -64.0</c>).</summary>
    public decimal Gravity { get; }
    /// <summary><c>GlobalData::m_unitDamagedThresh</c>/<c>m_unitReallyDamagedThresh</c> (<c>GameData.ini</c> <c>UnitDamagedThreshold</c>/<c>UnitReallyDamagedThreshold</c>).</summary>
    public Fixed UnitDamagedThreshold { get; }
    public Fixed UnitReallyDamagedThreshold { get; }
    /// <summary><c>GlobalData::m_baseRegenHealthPercentPerSecond</c> as a ratio (<c>GameData.ini BaseRegenHealthPercentPerSecond = 0.3%</c> is 0.003).</summary>
    public Fixed BaseRegenHealthPercentPerSecond { get; }
    /// <summary><c>GlobalData::m_baseRegenDelay</c> in ticks: how long a structure must stay undamaged before base regeneration resumes.</summary>
    public long BaseRegenDelayTicks { get; }
    /// <summary><c>GlobalData::m_standardMinefieldDistance</c>: the default <c>DistanceAroundObject</c> of <c>GenerateMinefieldBehavior</c> (<c>GameData.ini StandardMinefieldDistance = 40</c>).</summary>
    public Fixed StandardMinefieldDistance { get; }
    /// <summary><c>GlobalData::m_baseValuePerSupplyBox</c> (<c>GameData.ini ValuePerSupplyBox = 75</c>): what one carried box pays at a supply center.</summary>
    public int ValuePerSupplyBox { get; }
    /// <summary><c>GlobalData::m_sellPercentage</c> as a ratio (<c>GameData.ini SellPercentage = 50%</c>): the share of a structure's cost a sale refunds; the reference default is 1.0.</summary>
    public Fixed SellPercentage { get; }
    /// <summary><c>GlobalData::m_healthBonus[level]</c>: the max-health multiplier of veterancy level 0..3 (<c>GameData.ini HealthBonus_Veteran/Elite/Heroic</c>; every absent value is the reference default 1.0, level 0 is always 1.0).</summary>
    public ImmutableArray<Fixed> HealthBonus { get; }
    /// <summary><c>GlobalData::m_weaponBonusSet</c>: the <c>WeaponBonus</c> lines of <c>GameData.ini</c>, applied to every weapon before its own set.</summary>
    public WeaponBonusTable GlobalWeaponBonus { get; }
    public ObjectTemplate this[int index] => Templates[index];
    public bool TryGet(string name, out ObjectTemplate template) => _byName.TryGetValue(name, out template!);

    /// <summary>Whether one of the template's command sets carries a build or construct button for <paramref name="objectName"/>.</summary>
    public bool OffersBuild(ObjectTemplate builder, string objectName)
    {
        foreach (var set in builder.CommandSets)
            foreach (var name in Commands.BuildsOf(set))
                if (name.Equals(objectName, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
    public int InstantiableCount => Templates.Count(value => value.IsInstantiable);

    public static TemplateCatalog Build(CompiledDefinitionGraph graph, ModuleBindingTable bindings)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(bindings);
        var upgrades = UpgradeCatalog.Build(graph);
        var weapons = WeaponCatalog.Build(graph);
        var armors = ArmorCatalog.Build(graph);
        var locomotors = BuildLocomotors(graph);
        var sciences = ScienceCatalog.Build(graph);
        var specialPowers = SpecialPowerCatalog.Build(graph, sciences);
        var factions = FactionCatalog.Build(graph, sciences);
        var crates = CrateCatalog.Build(graph, sciences);
        var creationLists = ObjectCreationListCatalog.Build(graph);
        var (unitDamagedThreshold, unitReallyDamagedThreshold) = ResolveDamageThresholds(graph);
        var gravity = ResolveGravity(graph);
        var (baseRegen, baseRegenDelay) = ResolveBaseRegen(graph);
        var minefieldDistance = ResolveMinefieldDistance(graph);
        var valuePerSupplyBox = ResolveValuePerSupplyBox(graph);
        var sellPercentage = ResolveSellPercentage(graph);
        var healthBonus = ResolveHealthBonus(graph);
        var globalWeaponBonus = graph.Definitions.OfType<GlobalDataDefinition>().FirstOrDefault() is { } globalData ? WeaponBonusTable.From(globalData) : WeaponBonusTable.Empty;
        var templates = ImmutableArray.CreateBuilder<ObjectTemplate>();
        var index = 0;
        foreach (var definition in graph.Definitions.OfType<ObjectDefinition>().OrderBy(value => value.Id.ToString(), StringComparer.Ordinal))
        {
            var specs = ImmutableArray.CreateBuilder<ModuleSpec>();
            var presentation = ImmutableArray.CreateBuilder<string>();
            var missing = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var module in definition.Modules)
            {
                if (!bindings.Registry.TryGet(module.Name, out var descriptor))
                {
                    missing.Add(module.Name);
                    continue;
                }
                if (descriptor.Category == ModuleCategory.Presentation)
                {
                    presentation.Add(module.Name);
                    continue;
                }
                if (!bindings.TryGet(module.Name, out var binding))
                {
                    missing.Add(module.Name);
                    continue;
                }
                try
                {
                    specs.Add(new ModuleSpec(module.Name, module.Tag, binding, binding.ParseData(module, upgrades)));
                }
                catch (UnsupportedModuleConfigurationException unsupported)
                {
                    missing.Add($"{module.Name}[{unsupported.Feature}]");
                }
                catch (Exception exception) when (exception is InvalidDataException or OverflowException or ArgumentException)
                {
                    throw new TemplateBuildException($"Object '{definition.OriginalName}', module '{module.Name}' ({module.Tag}): {exception.Message}");
                }
            }

            var (defaultArmor, playerUpgradeArmor, veteranArmor) = ResolveArmor(definition, armors);
            var (defaultWeapon, playerUpgradeWeapon, veteranWeapon) = ResolveWeapon(definition, weapons);
            var locomotor = ResolveLocomotor(definition, locomotors);
            var prerequisites = ResolvePrerequisites(definition);
            var commandSets = ImmutableArray.CreateBuilder<string>();
            if (definition.Fields.LastOrDefault(field => field.Name.Equals("CommandSet", StringComparison.OrdinalIgnoreCase)) is { } ownSet) commandSets.Add(ownSet.Value.OriginalText.Trim());
            foreach (var module in definition.Modules.Where(value => value.Name.Equals("CommandSetUpgrade", StringComparison.Ordinal)))
                foreach (var field in module.Fields.Where(value => value.Name.Equals("CommandSet", StringComparison.OrdinalIgnoreCase) || value.Name.Equals("CommandSetAlt", StringComparison.OrdinalIgnoreCase)))
                    if (field.Value.OriginalText.Trim() is { Length: > 0 } name && !commandSets.Contains(name)) commandSets.Add(name);
            templates.Add(new ObjectTemplate(index++, definition, specs.ToImmutable(), presentation.ToImmutable(), [..missing],
                defaultArmor, playerUpgradeArmor, veteranArmor, defaultWeapon, playerUpgradeWeapon, veteranWeapon, locomotor, prerequisites, commandSets.ToImmutable())
            {
                LocomotorSets = ResolveLocomotorSets(definition, locomotors),
                CarBombWeapon = ResolveCarBombWeapon(definition, weapons),
                SecondaryWeapon = ResolveSecondaryWeapon(definition, weapons),
                CrateUpgradeWeapon = ResolveCrateUpgradeWeapon(definition, weapons),
                CrateUpgradeArmor = ResolveCrateUpgradeArmor(definition, armors),
            });
        }
        var commands = CommandCatalog.Build(graph);
        var catalog = new TemplateCatalog(templates.ToImmutable(), commands, upgrades, weapons, sciences, specialPowers, factions, crates, creationLists, unitDamagedThreshold, unitReallyDamagedThreshold, gravity, baseRegen, baseRegenDelay, minefieldDistance, valuePerSupplyBox, sellPercentage, healthBonus, globalWeaponBonus)
        {
            MaxTunnelCapacity = ResolveMaxTunnelCapacity(graph),
        };
        foreach (var crate in crates.Crates)
            foreach (var entry in crate.Objects)
                if (!catalog.TryGet(entry.ObjectName, out _))
                    throw new TemplateBuildException($"Crate '{crate.Name}': CrateObject '{entry.ObjectName}' is not an Object definition.");
        // WeaponTemplate::postProcessLoad: m_projectileTmpl = TheThingFactory->findTemplate(m_projectileName) (asserts when missing). A name
        // without an Object definition is a data error here, never a silent "no projectile".
        var projectiles = new int[weapons.Weapons.Length];
        foreach (var weapon in weapons.Weapons)
        {
            if (weapon.ProjectileObject.Length == 0) { projectiles[weapon.Index] = -1; continue; }
            projectiles[weapon.Index] = catalog.TryGet(weapon.ProjectileObject, out var projectile)
                ? projectile.Index
                : throw new TemplateBuildException($"Weapon '{weapon.Name}': ProjectileObject '{weapon.ProjectileObject}' is not an Object definition.");
        }
        catalog.ProjectileTemplates = [..projectiles];
        return catalog;
    }

    /// <summary><c>GlobalData::m_maxTunnelCapacity</c> (<c>GameData.ini MaxTunnelCapacity = 10</c>); absent key: the reference default 0.</summary>
    private static int ResolveMaxTunnelCapacity(CompiledDefinitionGraph graph)
    {
        var gameData = graph.Definitions.OfType<GlobalDataDefinition>().FirstOrDefault();
        var value = gameData?.Fields.LastOrDefault(field => field.Name.Equals("MaxTunnelCapacity", StringComparison.OrdinalIgnoreCase))?.Value.Value;
        return value is long integer ? checked((int)integer) : 0;
    }

    /// <summary><c>GlobalData::m_maxTunnelCapacity</c>: how many objects one player's tunnel network holds (<c>TunnelTracker::getContainMax</c>).</summary>
    public int MaxTunnelCapacity { get; private init; }

    /// <summary>Per weapon index, the index of its <see cref="WeaponInfo.ProjectileObject"/> template, or -1 when it fires none.</summary>
    public ImmutableArray<int> ProjectileTemplates { get; private set; } = [];

    /// <summary><see cref="Gravity"/> in world units per tick² (<c>GameData.ini Gravity</c> / 900), negative (downward).</summary>
    public Fixed GravityPerTickSquared => SimulationTiming.PerSecondSquaredToPerTickSquared(Gravity);

    /// <summary>
    /// <c>calcDamageState</c> falls back to <c>BODY_PRISTINE</c> when <c>TheGlobalData</c> is unavailable; the closest
    /// equivalent here is the retail default (<c>GameData.ini</c>: 0.7/0.35) when the single <c>GameData</c> definition,
    /// or these two specific fields, are absent — never observed in the shipped data, but keeps a synthetic fixture
    /// without a <c>GameData</c> block working instead of throwing.
    /// </summary>
    private static (Fixed Damaged, Fixed ReallyDamaged) ResolveDamageThresholds(CompiledDefinitionGraph graph)
    {
        var gameData = graph.Definitions.OfType<GlobalDataDefinition>().FirstOrDefault();
        Fixed Get(string name, decimal fallback)
        {
            var value = gameData?.Fields.LastOrDefault(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.Value;
            return value is decimal real ? Fixed.FromDecimal(real) : Fixed.FromDecimal(fallback);
        }
        return (Get("UnitDamagedThreshold", 0.7m), Get("UnitReallyDamagedThreshold", 0.35m));
    }

    /// <summary>Same fallback rule as the damage thresholds; -64 is the retail <c>GameData.ini</c> value.</summary>
    private static decimal ResolveGravity(CompiledDefinitionGraph graph)
    {
        var gameData = graph.Definitions.OfType<GlobalDataDefinition>().FirstOrDefault();
        var value = gameData?.Fields.LastOrDefault(field => field.Name.Equals("Gravity", StringComparison.OrdinalIgnoreCase))?.Value.Value;
        return value is decimal real ? real : -64m;
    }

    /// <summary>Absent keys mean "no base regeneration" (the reference default is 0, which parks every <c>BaseRegenerateUpdate</c>).</summary>
    private static (Fixed PerSecond, long DelayTicks) ResolveBaseRegen(CompiledDefinitionGraph graph)
    {
        var gameData = graph.Definitions.OfType<GlobalDataDefinition>().FirstOrDefault();
        object? Get(string name) => gameData?.Fields.LastOrDefault(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.Value;
        var perSecond = Get("BaseRegenHealthPercentPerSecond") is Percentage percentage ? Fixed.FromDecimal(percentage.Ratio) : Fixed.Zero;
        var delay = Get("BaseRegenDelay") is GameTime time ? SimulationTiming.DurationMillisecondsToTicks(time.Milliseconds) : 0;
        return (perSecond, delay);
    }

    /// <summary>Absent key: the reference default of <c>GlobalData</c> (<c>m_standardMinefieldDistance = 40</c>).</summary>
    private static Fixed ResolveMinefieldDistance(CompiledDefinitionGraph graph)
    {
        var gameData = graph.Definitions.OfType<GlobalDataDefinition>().FirstOrDefault();
        var value = gameData?.Fields.LastOrDefault(field => field.Name.Equals("StandardMinefieldDistance", StringComparison.OrdinalIgnoreCase))?.Value.Value;
        return value is Distance distance ? Fixed.FromDecimal(distance.Value) : Fixed.FromInt(40);
    }

    /// <summary>Absent key: the reference default of <c>GlobalData</c> (<c>m_baseValuePerSupplyBox = 100</c>).</summary>
    private static int ResolveValuePerSupplyBox(CompiledDefinitionGraph graph)
    {
        var gameData = graph.Definitions.OfType<GlobalDataDefinition>().FirstOrDefault();
        var value = gameData?.Fields.LastOrDefault(field => field.Name.Equals("ValuePerSupplyBox", StringComparison.OrdinalIgnoreCase))?.Value.Value;
        return value is long integer ? checked((int)integer) : 100;
    }

    /// <summary>Absent key: the reference default of <c>GlobalData</c> (<c>m_sellPercentage = 1.0f</c>).</summary>
    private static Fixed ResolveSellPercentage(CompiledDefinitionGraph graph)
    {
        var gameData = graph.Definitions.OfType<GlobalDataDefinition>().FirstOrDefault();
        var value = gameData?.Fields.LastOrDefault(field => field.Name.Equals("SellPercentage", StringComparison.OrdinalIgnoreCase))?.Value.Value;
        return value is Percentage percentage ? Fixed.FromDecimal(percentage.Ratio) : Fixed.One;
    }

    /// <summary>GlobalData.cpp: every <c>m_healthBonus[i]</c> starts at 1.0, so an absent key (or an absent GameData) changes nothing.</summary>
    private static ImmutableArray<Fixed> ResolveHealthBonus(CompiledDefinitionGraph graph)
    {
        var gameData = graph.Definitions.OfType<GlobalDataDefinition>().FirstOrDefault();
        Fixed Get(string name) => gameData?.Fields.LastOrDefault(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.Value is Percentage percentage
            ? Fixed.FromDecimal(percentage.Ratio) : Fixed.One;
        return [Fixed.One, Get("HealthBonus_Veteran"), Get("HealthBonus_Elite"), Get("HealthBonus_Heroic")];
    }

    private static ImmutableArray<string> ConditionTokens(DefinitionSection set)
    {
        var field = set.Fields.LastOrDefault(value => value.Name.Equals("Conditions", StringComparison.OrdinalIgnoreCase));
        if (field is null) return [];
        var text = field.Value.OriginalText.Trim();
        return text.Equals("None", StringComparison.OrdinalIgnoreCase) ? [] : [..text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries)];
    }

    /// <summary>Maps a single-condition-set token to a 1..3 veterancy index (<c>Entity.VeterancyLevel</c>), or 0 when it names none of the three.</summary>
    private static int VeterancyConditionIndex(string condition)
    {
        if (condition.Equals("VETERAN", StringComparison.OrdinalIgnoreCase)) return 1;
        if (condition.Equals("ELITE", StringComparison.OrdinalIgnoreCase)) return 2;
        if (condition.Equals("HERO", StringComparison.OrdinalIgnoreCase)) return 3;
        return 0;
    }

    private static (ArmorInfo Default, ArmorInfo? PlayerUpgrade, ImmutableArray<ArmorInfo?> Veteran) ResolveArmor(ObjectDefinition definition, ImmutableDictionary<string, ArmorInfo> armors)
    {
        ArmorInfo? defaultArmor = null;
        ArmorInfo? playerUpgrade = null;
        var veteran = new ArmorInfo?[4];
        foreach (var section in definition.Sections.Where(value => value.Declaration.Equals("ArmorSet", StringComparison.OrdinalIgnoreCase)))
        {
            var armorName = section.Fields.LastOrDefault(value => value.Name.Equals("Armor", StringComparison.OrdinalIgnoreCase))?.Value.Value switch
            {
                DefinitionReference { IsNone: false } reference => reference.Name,
                _ => null,
            };
            if (armorName is null || !armors.TryGetValue(armorName, out var info)) continue;
            var conditions = ConditionTokens(section);
            if (conditions.Length == 0) defaultArmor ??= info;
            else if (conditions is [var only])
            {
                if (only.Equals("PLAYER_UPGRADE", StringComparison.OrdinalIgnoreCase)) playerUpgrade ??= info;
                else if (VeterancyConditionIndex(only) is > 0 and var level) veteran[level] ??= info;
            }
        }
        return (defaultArmor ?? ArmorInfo.Default, playerUpgrade, [..veteran]);
    }

    private static int ResolveSecondaryWeapon(ObjectDefinition definition, WeaponCatalog weapons)
    {
        foreach (var section in definition.Sections.Where(value => value.Declaration.Equals("WeaponSet", StringComparison.OrdinalIgnoreCase)))
        {
            if (!ConditionTokens(section).IsEmpty) continue;
            var secondary = section.Fields.Where(value => value.Name.Equals("Weapon", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(value => value.Value.OriginalText.TrimStart().StartsWith("SECONDARY", StringComparison.OrdinalIgnoreCase));
            return secondary?.Value.Value is DefinitionReference { IsNone: false } reference && weapons.TryGet(reference.Name, out var info) ? info.Index : -1;
        }
        return -1;
    }

    /// <summary><c>WeaponSet Conditions = CRATEUPGRADE_ONE</c>/<c>CRATEUPGRADE_TWO</c> alone: the primary weapon of each level.</summary>
    private static ImmutableArray<int> ResolveCrateUpgradeWeapon(ObjectDefinition definition, WeaponCatalog weapons)
    {
        var levels = new[] { -1, -1, -1 };
        foreach (var section in definition.Sections.Where(value => value.Declaration.Equals("WeaponSet", StringComparison.OrdinalIgnoreCase)))
        {
            if (ConditionTokens(section) is not [var only] || CrateConditionIndex(only, "CRATEUPGRADE_") is not (> 0 and var level) || levels[level] >= 0) continue;
            var primary = section.Fields.Where(value => value.Name.Equals("Weapon", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(value => value.Value.OriginalText.TrimStart().StartsWith("PRIMARY", StringComparison.OrdinalIgnoreCase));
            if (primary?.Value.Value is DefinitionReference { IsNone: false } reference && weapons.TryGet(reference.Name, out var info)) levels[level] = info.Index;
        }
        return [..levels];
    }

    /// <summary><c>ArmorSet Conditions = CRATE_UPGRADE_ONE</c>/<c>CRATE_UPGRADE_TWO</c> alone: the armor of each level.</summary>
    private static ImmutableArray<ArmorInfo?> ResolveCrateUpgradeArmor(ObjectDefinition definition, ImmutableDictionary<string, ArmorInfo> armors)
    {
        var levels = new ArmorInfo?[3];
        foreach (var section in definition.Sections.Where(value => value.Declaration.Equals("ArmorSet", StringComparison.OrdinalIgnoreCase)))
        {
            if (ConditionTokens(section) is not [var only] || CrateConditionIndex(only, "CRATE_UPGRADE_") is not (> 0 and var level) || levels[level] is not null) continue;
            var armorName = section.Fields.LastOrDefault(value => value.Name.Equals("Armor", StringComparison.OrdinalIgnoreCase))?.Value.Value switch
            {
                DefinitionReference { IsNone: false } reference => reference.Name,
                _ => null,
            };
            if (armorName is not null && armors.TryGetValue(armorName, out var info)) levels[level] = info;
        }
        return [..levels];
    }

    /// <summary>Maps <c>&lt;prefix&gt;ONE</c>/<c>&lt;prefix&gt;TWO</c> to 1/2, else 0.</summary>
    private static int CrateConditionIndex(string condition, string prefix)
    {
        if (condition.Equals(prefix + "ONE", StringComparison.OrdinalIgnoreCase)) return 1;
        return condition.Equals(prefix + "TWO", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
    }

    private static int ResolveCarBombWeapon(ObjectDefinition definition, WeaponCatalog weapons)
    {
        foreach (var section in definition.Sections.Where(value => value.Declaration.Equals("WeaponSet", StringComparison.OrdinalIgnoreCase)))
        {
            if (ConditionTokens(section) is not [var only] || !only.Equals("CARBOMB", StringComparison.OrdinalIgnoreCase)) continue;
            var primary = section.Fields.Where(value => value.Name.Equals("Weapon", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(value => value.Value.OriginalText.TrimStart().StartsWith("PRIMARY", StringComparison.OrdinalIgnoreCase));
            return primary?.Value.Value is DefinitionReference { IsNone: false } reference && weapons.TryGet(reference.Name, out var info) ? info.Index : -1;
        }
        return -1;
    }

    private static (int Default, int PlayerUpgrade, ImmutableArray<int> Veteran) ResolveWeapon(ObjectDefinition definition, WeaponCatalog weapons)
    {
        var defaultWeapon = -1;
        var playerUpgradeWeapon = -1;
        var veteran = new[] { -1, -1, -1, -1 };
        foreach (var section in definition.Sections.Where(value => value.Declaration.Equals("WeaponSet", StringComparison.OrdinalIgnoreCase)))
        {
            var primary = section.Fields.Where(value => value.Name.Equals("Weapon", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(value => value.Value.OriginalText.TrimStart().StartsWith("PRIMARY", StringComparison.OrdinalIgnoreCase));
            var weaponName = primary?.Value.Value switch { DefinitionReference { IsNone: false } reference => reference.Name, _ => null };
            var index = weaponName is not null && weapons.TryGet(weaponName, out var info) ? info.Index : -1;
            var conditions = ConditionTokens(section);
            if (conditions.Length == 0) { if (defaultWeapon < 0) defaultWeapon = index; }
            else if (conditions is [var only])
            {
                if (only.Equals("PLAYER_UPGRADE", StringComparison.OrdinalIgnoreCase)) { if (playerUpgradeWeapon < 0) playerUpgradeWeapon = index; }
                else if (VeterancyConditionIndex(only) is > 0 and var level) { if (veteran[level] < 0) veteran[level] = index; }
            }
        }
        return (defaultWeapon, playerUpgradeWeapon, [..veteran]);
    }

    private static ImmutableDictionary<string, LocomotorInfo> BuildLocomotors(CompiledDefinitionGraph graph)
    {
        var builder = ImmutableDictionary.CreateBuilder<string, LocomotorInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in graph.Definitions.OfType<LocomotorDefinition>())
        {
            decimal Get(string name) => definition.Fields.LastOrDefault(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.Value is decimal value ? value : 0m;
            var speed = Get("Speed");
            var acceleration = Get("Acceleration");
            var braking = Get("Braking");
            var turnRate = Get("TurnRate");
            var minSpeed = Get("MinSpeed");
            var closeEnough = Get("CloseEnoughDist");
            var surfaces = LocomotorSurfaces.None;
            if (definition.Fields.LastOrDefault(field => field.Name.Equals("Surfaces", StringComparison.OrdinalIgnoreCase))?.Value.Value is FlagsValue surfaceFlags)
                foreach (var name in surfaceFlags.Names)
                    surfaces |= Enum.Parse<LocomotorSurfaces>(name, ignoreCase: true);
            builder[definition.OriginalName] = new LocomotorInfo(
                SimulationTiming.PerSecondToPerTick(speed), SimulationTiming.PerSecondSquaredToPerTickSquared(acceleration),
                SimulationTiming.DegreesPerSecondToRadiansPerTick(turnRate),
                braking > 0 ? SimulationTiming.PerSecondSquaredToPerTickSquared(braking) : SimulationTiming.PerSecondSquaredToPerTickSquared(acceleration),
                SimulationTiming.PerSecondToPerTick(minSpeed),
                // CloseEnoughDist is a plain distance (not a rate): no per-tick conversion. 0 falls back to one tick's travel below.
                Fixed.FromDecimal(closeEnough), surfaces)
            {
                PreferredHeight = Fixed.FromDecimal(Get("PreferredHeight")),
                SpeedLimitZ = SimulationTiming.PerSecondToPerTick(Get("SpeedLimitZ")),
                Lift = SimulationTiming.PerSecondSquaredToPerTickSquared(Get("Lift")),
            };
        }
        return builder.ToImmutable();
    }

    /// <summary>All <c>Locomotor = SET_X ...</c> entries (<c>AIUpdateModuleData::parseLocomotorSet</c>): per set name the first listed locomotor that exists.</summary>
    private static ImmutableDictionary<string, LocomotorInfo> ResolveLocomotorSets(ObjectDefinition definition, ImmutableDictionary<string, LocomotorInfo> locomotors)
    {
        var sets = ImmutableDictionary.CreateBuilder<string, LocomotorInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in definition.Fields.Where(value => value.Name.Equals("Locomotor", StringComparison.OrdinalIgnoreCase)))
            if (field.Value.Value is TaggedReferenceList tagged)
                foreach (var reference in tagged.References)
                    if (locomotors.TryGetValue(reference.Name, out var info))
                    {
                        sets[tagged.Tag] = info;
                        break;
                    }
        return sets.ToImmutable();
    }

    private static LocomotorInfo? ResolveLocomotor(ObjectDefinition definition, ImmutableDictionary<string, LocomotorInfo> locomotors)
    {
        // Only the SET_NORMAL entry is used for straight-line movement (phase 5 scope); other sets (freefall, wander, taxi, panic) are phase 6.
        foreach (var field in definition.Fields.Where(value => value.Name.Equals("Locomotor", StringComparison.OrdinalIgnoreCase)))
            if (field.Value.Value is TaggedReferenceList { Tag: "SET_NORMAL" } tagged)
                foreach (var reference in tagged.References)
                    if (locomotors.TryGetValue(reference.Name, out var info)) return info;
        return null;
    }

    private static ImmutableArray<PrerequisiteLine> ResolvePrerequisites(ObjectDefinition definition)
    {
        var section = definition.Sections.FirstOrDefault(value => value.Declaration.Equals("Prerequisites", StringComparison.OrdinalIgnoreCase));
        if (section is null) return [];
        var lines = ImmutableArray.CreateBuilder<PrerequisiteLine>();
        foreach (var field in section.Fields)
        {
            var isScience = field.Name.Equals("Science", StringComparison.OrdinalIgnoreCase);
            if (!isScience && !field.Name.Equals("Object", StringComparison.OrdinalIgnoreCase)) continue;
            var names = field.Value.Value switch
            {
                DefinitionReference { IsNone: false } single => ImmutableArray.Create(single.Name),
                ImmutableArray<DefinitionReference> many => [..many.Where(value => !value.IsNone).Select(value => value.Name)],
                _ => ImmutableArray<string>.Empty,
            };
            if (!names.IsEmpty) lines.Add(new PrerequisiteLine(isScience, names));
        }
        return lines.ToImmutable();
    }
}
