using System.Collections.Immutable;
using CnCNew.Data.Definitions;

namespace CnCNew.Data.Schema;

/// <summary>
/// Key schema of an <c>Object</c> definition header, derived from <c>ThingTemplate::s_objectFieldParseTable</c>
/// (GeneralsMD/Code/GameEngine/Source/Common/Thing/ThingTemplate.cpp). Keys that only carry presentation data
/// (audio, portraits, cameo images, …) are <see cref="FieldType.Opaque"/>: known, counted, but not yet typed.
/// </summary>
public static class ObjectSchema
{
    private const string Table = "ThingTemplate::s_objectFieldParseTable";

    public static SchemaSet Object { get; } = new("Object", Build());

    /// <summary>Keys that <c>ObjectReskin</c> may set (<c>s_objectReskinFieldParseTable</c>); everything else is a module block.</summary>
    public static ImmutableHashSet<string> ReskinKeys { get; } = ImmutableHashSet.Create(StringComparer.OrdinalIgnoreCase,
        "Geometry", "GeometryMajorRadius", "GeometryMinorRadius", "GeometryHeight", "GeometryIsSmall",
        "FenceWidth", "FenceXOffset", "MaxSimultaneousOfType", "MaxSimultaneousLinkKey");

    private static IEnumerable<FieldSchema> Build()
    {
        FieldSchema Opaque(string name) => new(name, FieldType.Opaque, Source: Table);
        FieldSchema Real(string name) => new(name, FieldType.Real, Source: Table);
        FieldSchema Dist(string name) => new(name, FieldType.Distance, Source: Table);
        FieldSchema Bool(string name) => new(name, FieldType.Bool, Source: Table);
        FieldSchema Byte(string name) => new(name, FieldType.Integer, Min: 0, Max: 255, Source: Table);
        FieldSchema Enum(string name, NameTable table) => new(name, FieldType.Enum, Table: table, Source: Table);

        yield return new("DisplayName", FieldType.Text, Source: Table);
        yield return Enum("RadarPriority", NameTables.RadarPriority);
        yield return Byte("TransportSlotCount");
        yield return Real("FenceWidth");
        yield return Real("FenceXOffset");
        yield return Bool("IsBridge");
        yield return Dist("VisionRange");
        yield return Dist("ShroudClearingRange");
        yield return Dist("ShroudRevealToAllRange");
        yield return new("PlacementViewAngle", FieldType.Angle, Source: Table);
        yield return Real("FactoryExitWidth");
        yield return Real("FactoryExtraBibWidth");
        yield return new("SkillPointValue", FieldType.IntegerList, Count: 4, Source: Table);
        yield return new("ExperienceValue", FieldType.IntegerList, Count: 4, Source: Table);
        yield return new("ExperienceRequired", FieldType.IntegerList, Count: 4, Source: Table);
        yield return Bool("IsTrainable");
        yield return Bool("EnterGuard");
        yield return Bool("HijackGuard");
        yield return new("Side", FieldType.Text, Source: Table);
        yield return Enum("Buildable", NameTables.BuildableStatus);
        yield return new("BuildCost", FieldType.Money, Min: 0, Max: 65535, Source: Table);
        yield return Real("BuildTime");
        yield return new("RefundValue", FieldType.Money, Min: 0, Max: 65535, Source: Table);
        yield return Enum("BuildCompletion", NameTables.BuildCompletion);
        yield return new("EnergyProduction", FieldType.Integer, Source: Table);
        yield return new("EnergyBonus", FieldType.Integer, Source: Table);
        yield return Bool("IsForbidden");
        yield return Bool("IsPrerequisite");
        yield return new("DisplayColor", FieldType.ColorInt, Source: Table);
        yield return Enum("EditorSorting", NameTables.EditorSorting);
        yield return new("KindOf", FieldType.Flags, Table: NameTables.KindOf, Source: Table);
        yield return new("CommandSet", FieldType.Reference, ReferenceKind: DefinitionKind.CommandSet, Source: Table);
        yield return new("BuildVariations", FieldType.ReferenceList, ReferenceKind: DefinitionKind.Object, Source: Table);
        foreach (var key in new[] { "SelectPortrait", "ButtonImage", "UpgradeCameo1", "UpgradeCameo2", "UpgradeCameo3", "UpgradeCameo4", "UpgradeCameo5" })
            yield return Opaque(key);
        foreach (var key in new[]
        {
            "VoiceSelect", "VoiceGroupSelect", "VoiceMove", "VoiceAttack", "VoiceEnter", "VoiceFear", "VoiceSelectElite", "VoiceCreated",
            "VoiceTaskUnable", "VoiceTaskComplete", "VoiceMeetEnemy", "VoiceGarrison", "VoiceDefect", "VoiceAttackSpecial", "VoiceAttackAir",
            "VoiceGuard", "SoundMoveStart", "SoundMoveStartDamaged", "SoundMoveLoop", "SoundMoveLoopDamaged", "SoundAmbient",
            "SoundAmbientDamaged", "SoundAmbientReallyDamaged", "SoundAmbientRubble", "SoundStealthOn", "SoundStealthOff", "SoundCreated",
            "SoundOnDamaged", "SoundOnReallyDamaged", "SoundEnter", "SoundExit", "SoundPromotedVeteran", "SoundPromotedElite",
            "SoundPromotedHero", "SoundFallingFromPlane",
        })
            yield return Opaque(key);
        yield return Real("Scale");
        yield return Enum("Geometry", NameTables.Geometry);
        yield return Dist("GeometryMajorRadius");
        yield return Dist("GeometryMinorRadius");
        yield return Dist("GeometryHeight");
        yield return Bool("GeometryIsSmall");
        yield return new("Shadow", FieldType.Flags, Table: NameTables.Shadow, Source: Table);
        yield return Real("ShadowSizeX");
        yield return Real("ShadowSizeY");
        yield return Real("ShadowOffsetX");
        yield return Real("ShadowOffsetY");
        yield return new("ShadowTexture", FieldType.Text, Source: Table);
        yield return new("OcclusionDelay", FieldType.DurationMs, Source: Table);
        yield return new("Locomotor", FieldType.LocomotorSet, Repeat: RepeatPolicy.IndexedByFirstToken, Source: Table);
        yield return Real("InstanceScaleFuzziness");
        yield return Byte("StructureRubbleHeight");
        yield return new("ThreatValue", FieldType.Integer, Min: 0, Max: 65535, Source: Table);
        yield return Opaque("MaxSimultaneousOfType");
        yield return new("MaxSimultaneousLinkKey", FieldType.Text, Source: Table);
        yield return Byte("CrusherLevel");
        yield return Byte("CrushableLevel");
    }
}

/// <summary>
/// Repeatable keys per top-level declaration type. A key not listed here is a scalar: the last occurrence wins and
/// repeats inside one block are counted as <c>duplicate-key</c> compatibility cases. Each entry cites the reference
/// parse function that shows accumulation or per-index assignment.
/// </summary>
public static class RepeatableKeys
{
    public static RepeatPolicy Lookup(string declarationType, string key)
        => Table.TryGetValue((declarationType, key), out var policy) ? policy : RepeatPolicy.Overwrite;

    private static readonly ImmutableDictionary<(string, string), RepeatPolicy> Table = Build();

    private static ImmutableDictionary<(string, string), RepeatPolicy> Build()
    {
        var builder = ImmutableDictionary.CreateBuilder<(string, string), RepeatPolicy>(new TupleComparer());
        void Add(string type, string key, RepeatPolicy policy) => builder.Add((type, key), policy);
        // Armor.cpp ArmorTemplate::parseArmorCoefficients: one coefficient per damage type.
        Add("Armor", "Armor", RepeatPolicy.IndexedByFirstToken);
        // DamageFX.cpp parseMajorFXList/parseMinorFXList: one FX per damage type (optionally per veterancy level).
        Add("DamageFX", "MajorFX", RepeatPolicy.IndexedByFirstToken);
        Add("DamageFX", "MinorFX", RepeatPolicy.IndexedByFirstToken);
        Add("DamageFX", "VeterancyMajorFX", RepeatPolicy.IndexedByFirstTwoTokens);
        Add("DamageFX", "VeterancyMinorFX", RepeatPolicy.IndexedByFirstTwoTokens);
        // Weapon.cpp parseWeaponBonusSet / GlobalData.cpp WeaponBonusSet: one bonus per (condition, field).
        Add("Weapon", "WeaponBonus", RepeatPolicy.IndexedByFirstTwoTokens);
        Add("GameData", "WeaponBonus", RepeatPolicy.IndexedByFirstTwoTokens);
        // Weapon.cpp parseScatterTarget appends; GlobalData.cpp parseAsciiStringVectorAppend appends.
        Add("Weapon", "ScatterTarget", RepeatPolicy.Accumulate);
        Add("GameData", "StandardPublicBone", RepeatPolicy.Accumulate);
        // INIMapCache.cpp, Anim2D.cpp parseImage, Credits.cpp parseText, CrateSystem.cpp, TerrainRoads.cpp push_back entries.
        Add("MapCache", "supplyPosition", RepeatPolicy.Accumulate);
        Add("MapCache", "techPosition", RepeatPolicy.Accumulate);
        Add("Animation", "Image", RepeatPolicy.Accumulate);
        Add("Credits", "Text", RepeatPolicy.Accumulate);
        Add("Credits", "Style", RepeatPolicy.Accumulate);
        Add("CrateData", "CrateObject", RepeatPolicy.Accumulate);
        Add("Bridge", "TransitionToFX", RepeatPolicy.Accumulate);
        Add("Bridge", "TransitionToOCL", RepeatPolicy.Accumulate);
        // AIUpdate.cpp AIUpdateModuleData::parseLocomotorSet: one locomotor list per set name.
        Add("Object", "Locomotor", RepeatPolicy.IndexedByFirstToken);
        Add("ObjectReskin", "Locomotor", RepeatPolicy.IndexedByFirstToken);
        return builder.ToImmutable();
    }

    private sealed class TupleComparer : IEqualityComparer<(string, string)>
    {
        public bool Equals((string, string) x, (string, string) y)
            => StringComparer.OrdinalIgnoreCase.Equals(x.Item1, y.Item1) && StringComparer.OrdinalIgnoreCase.Equals(x.Item2, y.Item2);
        public int GetHashCode((string, string) value)
            => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(value.Item1), StringComparer.OrdinalIgnoreCase.GetHashCode(value.Item2));
    }
}
