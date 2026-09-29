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
    LocomotorSurfaces Surfaces = LocomotorSurfaces.None);

/// <summary>
/// The runtime view of an <c>Object</c> definition. Presentation modules (Draw, ClientUpdate) are listed but never
/// instantiated; a simulation module without implementation makes the template not instantiable and is named in
/// <see cref="MissingModules"/> (never dropped silently).
/// </summary>
public sealed class ObjectTemplate
{
    internal ObjectTemplate(int index, ObjectDefinition definition, ImmutableArray<ModuleSpec> modules,
        ImmutableArray<string> presentationModules, ImmutableArray<string> missingModules,
        ArmorInfo defaultArmor, ArmorInfo? playerUpgradeArmor, int defaultWeapon, int playerUpgradeWeapon, LocomotorInfo? locomotor,
        ImmutableArray<PrerequisiteLine> prerequisites)
    {
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
        IsTrainable = definition.Fields.LastOrDefault(field => field.Name.Equals("IsTrainable", StringComparison.OrdinalIgnoreCase))?.Value.Value is true;
        ExperienceValue = IntegerArray(definition, "ExperienceValue");
        ExperienceRequired = IntegerArray(definition, "ExperienceRequired");
        DefaultArmor = defaultArmor;
        PlayerUpgradeArmor = playerUpgradeArmor;
        DefaultWeapon = defaultWeapon;
        PlayerUpgradeWeapon = playerUpgradeWeapon;
        Locomotor = locomotor;
        Prerequisites = prerequisites;
    }

    /// <summary>Position in the ordinally sorted list of all object definitions (stable for one definition graph).</summary>
    public int Index { get; }
    public DefinitionId Id { get; }
    public string Name { get; }
    public string Side { get; }
    public ImmutableArray<string> KindOf { get; }
    public string Geometry { get; }
    public Fixed GeometryMajorRadius { get; }
    public Fixed GeometryMinorRadius { get; }
    public Fixed GeometryHeight { get; }

    private bool IsBox => Geometry.Equals("BOX", StringComparison.OrdinalIgnoreCase);
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
    /// <summary>Index into the world's <see cref="WeaponCatalog"/> of the <c>WeaponSet Conditions=None</c> primary weapon, or -1.</summary>
    public int DefaultWeapon { get; }
    /// <summary>Primary weapon of the <c>WeaponSet Conditions = PLAYER_UPGRADE</c> variant, if any, else -1.</summary>
    public int PlayerUpgradeWeapon { get; }
    public LocomotorInfo? Locomotor { get; }
    public ImmutableArray<PrerequisiteLine> Prerequisites { get; }

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

    private TemplateCatalog(ImmutableArray<ObjectTemplate> templates, UpgradeCatalog upgrades, WeaponCatalog weapons, ScienceCatalog sciences, SpecialPowerCatalog specialPowers,
        FactionCatalog factions, CrateCatalog crates, ObjectCreationListCatalog creationLists, Fixed unitDamagedThreshold, Fixed unitReallyDamagedThreshold, decimal gravity, Fixed baseRegenHealthPercentPerSecond, long baseRegenDelayTicks, Fixed standardMinefieldDistance, int valuePerSupplyBox, ImmutableArray<Fixed> healthBonus, WeaponBonusTable globalWeaponBonus)
    {
        HealthBonus = healthBonus;
        GlobalWeaponBonus = globalWeaponBonus;
        ValuePerSupplyBox = valuePerSupplyBox;
        StandardMinefieldDistance = standardMinefieldDistance;
        BaseRegenHealthPercentPerSecond = baseRegenHealthPercentPerSecond;
        BaseRegenDelayTicks = baseRegenDelayTicks;
        Templates = templates;
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
    /// <summary><c>GlobalData::m_healthBonus[level]</c>: the max-health multiplier of veterancy level 0..3 (<c>GameData.ini HealthBonus_Veteran/Elite/Heroic</c>; every absent value is the reference default 1.0, level 0 is always 1.0).</summary>
    public ImmutableArray<Fixed> HealthBonus { get; }
    /// <summary><c>GlobalData::m_weaponBonusSet</c>: the <c>WeaponBonus</c> lines of <c>GameData.ini</c>, applied to every weapon before its own set.</summary>
    public WeaponBonusTable GlobalWeaponBonus { get; }
    public ObjectTemplate this[int index] => Templates[index];
    public bool TryGet(string name, out ObjectTemplate template) => _byName.TryGetValue(name, out template!);
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

            var (defaultArmor, playerUpgradeArmor) = ResolveArmor(definition, armors);
            var (defaultWeapon, playerUpgradeWeapon) = ResolveWeapon(definition, weapons);
            var locomotor = ResolveLocomotor(definition, locomotors);
            var prerequisites = ResolvePrerequisites(definition);
            templates.Add(new ObjectTemplate(index++, definition, specs.ToImmutable(), presentation.ToImmutable(), [..missing],
                defaultArmor, playerUpgradeArmor, defaultWeapon, playerUpgradeWeapon, locomotor, prerequisites));
        }
        var catalog = new TemplateCatalog(templates.ToImmutable(), upgrades, weapons, sciences, specialPowers, factions, crates, creationLists, unitDamagedThreshold, unitReallyDamagedThreshold, gravity, baseRegen, baseRegenDelay, minefieldDistance, valuePerSupplyBox, healthBonus, globalWeaponBonus);
        foreach (var crate in crates.Crates)
            foreach (var entry in crate.Objects)
                if (!catalog.TryGet(entry.ObjectName, out _))
                    throw new TemplateBuildException($"Crate '{crate.Name}': CrateObject '{entry.ObjectName}' is not an Object definition.");
        return catalog;
    }

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

    private static (ArmorInfo Default, ArmorInfo? PlayerUpgrade) ResolveArmor(ObjectDefinition definition, ImmutableDictionary<string, ArmorInfo> armors)
    {
        ArmorInfo? defaultArmor = null;
        ArmorInfo? playerUpgrade = null;
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
            else if (conditions is [var only] && only.Equals("PLAYER_UPGRADE", StringComparison.OrdinalIgnoreCase)) playerUpgrade ??= info;
        }
        return (defaultArmor ?? ArmorInfo.Default, playerUpgrade);
    }

    private static (int Default, int PlayerUpgrade) ResolveWeapon(ObjectDefinition definition, WeaponCatalog weapons)
    {
        var defaultWeapon = -1;
        var playerUpgradeWeapon = -1;
        foreach (var section in definition.Sections.Where(value => value.Declaration.Equals("WeaponSet", StringComparison.OrdinalIgnoreCase)))
        {
            var primary = section.Fields.Where(value => value.Name.Equals("Weapon", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault(value => value.Value.OriginalText.TrimStart().StartsWith("PRIMARY", StringComparison.OrdinalIgnoreCase));
            var weaponName = primary?.Value.Value switch { DefinitionReference { IsNone: false } reference => reference.Name, _ => null };
            var index = weaponName is not null && weapons.TryGet(weaponName, out var info) ? info.Index : -1;
            var conditions = ConditionTokens(section);
            if (conditions.Length == 0) { if (defaultWeapon < 0) defaultWeapon = index; }
            else if (conditions is [var only] && only.Equals("PLAYER_UPGRADE", StringComparison.OrdinalIgnoreCase)) { if (playerUpgradeWeapon < 0) playerUpgradeWeapon = index; }
        }
        return (defaultWeapon, playerUpgradeWeapon);
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
                Fixed.FromDecimal(closeEnough), surfaces);
        }
        return builder.ToImmutable();
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
