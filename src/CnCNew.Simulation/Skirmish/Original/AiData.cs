using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Data.Definitions;
using CnCNew.Simulation.Numerics;

namespace CnCNew.Simulation.Skirmish.Original;

/// <summary>One <c>Structure</c> block of a <c>SkirmishBuildList</c> (<c>BuildListInfo</c>, <c>AI.cpp TAiData::parseSkirmishBuildList</c> / <c>BuildListInfo::parseStructure</c>).</summary>
/// <param name="AngleDegrees">The angle as written; the reference converts it to radians and does not turn it with the base (<c>AISkirmishPlayer::adjustBuildList</c>).</param>
public sealed record AiBuildListEntry(string Template, Fixed X, Fixed Y, Fixed AngleDegrees, int Rebuilds, bool InitiallyBuilt, bool AutomaticallyBuild);

/// <summary>A <c>SideInfo</c> block (<c>AISideInfo</c>): gatherers per supply centre by difficulty, the base defence and up to five skill sets (sciences in purchase order).</summary>
public sealed record AiSideInfo(string Side, int GatherersEasy, int GatherersNormal, int GatherersHard, string BaseDefenseStructure, ImmutableArray<ImmutableArray<string>> SkillSets);

/// <summary>
/// The part of <c>AIData.ini</c> the original skirmish AI reads (<c>TAiData</c>, <c>AI.cpp</c>), typed strictly: an unknown key inside <c>SideInfo</c>, <c>SkillSet</c>, <c>SkirmishBuildList</c> or
/// <c>Structure</c>, a malformed number or a missing required key throws; top-level keys this catalogue does not read are listed in <see cref="UnreadKeys"/> (they belong to other consumers:
/// guard ranges, group pathfinding, retaliation) and a top-level key outside the reference's field table throws.
/// <para>
/// The definition compiler still carries <c>AIData</c> as a compatibility definition (<c>baselines/compatibility.json</c>, category <c>definition</c>); this catalogue reads the original text of
/// its fields and is deliberately not part of <c>TemplateCatalog</c>, so the input manifest and every existing fingerprint stay as they are.
/// </para>
/// </summary>
public sealed class AiData
{
    /// <summary>The scalar keys of <c>TAiData</c>'s field table (<c>AI.cpp</c>) that this catalogue reads.</summary>
    private static readonly string[] ReadKeys =
    [
        "StructureSeconds", "TeamSeconds", "Wealthy", "Poor", "StructuresWealthyRate", "StructuresPoorRate", "TeamsWealthyRate", "TeamsPoorRate", "TeamResourcesToStart",
        "MaxRecruitRadius", "SkirmishBaseDefenseExtraDistance", "RotateSkirmishBases", "SupplyCenterSafeRadius", "RebuildDelayTimeSeconds",
    ];

    /// <summary>The other keys of the same table: valid, read elsewhere or not at all.</summary>
    private static readonly string[] OtherKeys =
    [
        "GuardInnerModifierAI", "GuardOuterModifierAI", "GuardInnerModifierHuman", "GuardOuterModifierHuman", "GuardChaseUnitsDuration", "GuardEnemyScanRate", "GuardEnemyReturnScanRate",
        "AlertRangeModifier", "AggressiveRangeModifier", "AttackPriorityDistanceModifier", "ForceIdleMSEC", "ForceSkirmishAI", "AttackUsesLineOfSight", "EnableRepulsors", "RepulsedDistance",
        "WallHeight", "AttackIgnoreInsignificantBuildings", "SkirmishGroupFudgeDistance", "MinInfantryForGroup", "MinVehiclesForGroup", "MinDistanceForGroup", "DistanceRequiresGroup",
        "InfantryPathfindDiameter", "VehiclePathfindDiameter", "AIDozerBoredRadiusModifier", "AICrushesInfantry", "MaxRetaliationDistance", "RetaliationFriendsRadius",
        "MinClumpDensity", "MinDistanceForGroup", "MaxRetaliateDistance", "UseLocoDistanceRatio",
    ];

    private AiData() { }

    public Fixed StructureSeconds { get; private init; }
    public Fixed TeamSeconds { get; private init; }
    public int Wealthy { get; private init; }
    public int Poor { get; private init; }
    public Fixed StructuresWealthyRate { get; private init; }
    public Fixed StructuresPoorRate { get; private init; }
    public Fixed TeamsWealthyRate { get; private init; }
    public Fixed TeamsPoorRate { get; private init; }
    public Fixed TeamResourcesToStart { get; private init; }
    public Fixed MaxRecruitRadius { get; private init; }
    public Fixed SkirmishBaseDefenseExtraDistance { get; private init; }
    public bool RotateSkirmishBases { get; private init; }
    public Fixed SupplyCenterSafeRadius { get; private init; }
    public int RebuildDelaySeconds { get; private init; }
    public ImmutableArray<AiSideInfo> Sides { get; private init; } = [];
    public ImmutableSortedDictionary<string, ImmutableArray<AiBuildListEntry>> BuildLists { get; private init; } = ImmutableSortedDictionary<string, ImmutableArray<AiBuildListEntry>>.Empty;

    /// <summary>Top-level keys of the file that are valid but not read here, in ordinal order.</summary>
    public ImmutableArray<string> UnreadKeys { get; private init; } = [];

    public AiSideInfo? FindSide(string side)
    {
        foreach (var info in Sides)
            if (string.Equals(info.Side, side, StringComparison.Ordinal)) return info;
        return null;
    }

    /// <summary>
    /// Reads every <c>AIData</c> block of the graph in load order (the reference parses each block into the one <c>TAiData</c>: a later block overrides scalars and appends side infos and
    /// build lists). Returns <see langword="null"/> when the graph has no such block (synthetic fixtures).
    /// </summary>
    public static AiData? TryBuild(CompiledDefinitionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var blocks = graph.Definitions.Where(definition => string.Equals(definition.OriginalName, "AIData", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (blocks.Length == 0) return null;
        var scalars = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var sides = ImmutableArray.CreateBuilder<AiSideInfo>();
        var lists = ImmutableSortedDictionary.CreateBuilder<string, ImmutableArray<AiBuildListEntry>>(StringComparer.Ordinal);
        foreach (var block in blocks)
        {
            foreach (var field in block.Fields)
            {
                var name = Canonical(field.Name, ReadKeys) ?? Canonical(field.Name, OtherKeys) ?? throw Invalid($"unknown key '{field.Name}'", field);
                scalars[name] = field.Value.OriginalText.Trim();
            }
            foreach (var section in block.Sections)
            {
                var (type, name) = Declaration(section);
                if (type.Equals("SideInfo", StringComparison.OrdinalIgnoreCase)) sides.Add(ReadSide(name, section));
                else if (type.Equals("SkirmishBuildList", StringComparison.OrdinalIgnoreCase)) lists[name] = ReadBuildList(section);
                else throw new InvalidDataException($"AIData: unknown block '{section.Declaration}' ({section.Location.Source}:{section.Location.Line.ToString(CultureInfo.InvariantCulture)}).");
            }
        }
        Fixed Real(string key) => scalars.TryGetValue(key, out var text) ? Number(text, key) : throw new InvalidDataException($"AIData: required key '{key}' is missing.");
        int Whole(string key) => checked((int)Real(key).Floor());
        return new AiData
        {
            StructureSeconds = Real("StructureSeconds"), TeamSeconds = Real("TeamSeconds"), Wealthy = Whole("Wealthy"), Poor = Whole("Poor"),
            StructuresWealthyRate = Real("StructuresWealthyRate"), StructuresPoorRate = Real("StructuresPoorRate"), TeamsWealthyRate = Real("TeamsWealthyRate"), TeamsPoorRate = Real("TeamsPoorRate"),
            TeamResourcesToStart = Real("TeamResourcesToStart"), MaxRecruitRadius = Real("MaxRecruitRadius"), SkirmishBaseDefenseExtraDistance = Real("SkirmishBaseDefenseExtraDistance"),
            RotateSkirmishBases = scalars.TryGetValue("RotateSkirmishBases", out var rotate) && Flag(rotate, "RotateSkirmishBases"),
            SupplyCenterSafeRadius = Real("SupplyCenterSafeRadius"), RebuildDelaySeconds = Whole("RebuildDelayTimeSeconds"),
            Sides = sides.ToImmutable(), BuildLists = lists.ToImmutable(),
            UnreadKeys = [..scalars.Keys.Where(key => Array.IndexOf(ReadKeys, key) < 0)],
        };
    }

    private static AiSideInfo ReadSide(string side, DefinitionSection section)
    {
        int easy = 0, normal = 0, hard = 0;
        var defense = string.Empty;
        foreach (var field in section.Fields)
        {
            var text = field.Value.OriginalText.Trim();
            switch (field.Name)
            {
                case "ResourceGatherersEasy": easy = checked((int)Number(text, field.Name).Floor()); break;
                case "ResourceGatherersNormal": normal = checked((int)Number(text, field.Name).Floor()); break;
                case "ResourceGatherersHard": hard = checked((int)Number(text, field.Name).Floor()); break;
                case "BaseDefenseStructure1": defense = text; break;
                default: throw Invalid($"unknown key '{field.Name}' in SideInfo {side}", field);
            }
        }
        var sets = new ImmutableArray<string>[5];
        for (var index = 0; index < sets.Length; index++) sets[index] = [];
        foreach (var child in section.Children)
        {
            var (type, _) = Declaration(child);
            if (type.Length != "SkillSet1".Length || !type.StartsWith("SkillSet", StringComparison.Ordinal) || type[^1] < '1' || type[^1] > '5')
                throw new InvalidDataException($"AIData: unknown block '{child.Declaration}' in SideInfo {side}.");
            var sciences = ImmutableArray.CreateBuilder<string>();
            foreach (var field in child.Fields)
            {
                if (field.Name != "Science") throw Invalid($"unknown key '{field.Name}' in {type} of SideInfo {side}", field);
                sciences.Add(field.Value.OriginalText.Trim());
            }
            sets[type[^1] - '1'] = sciences.ToImmutable();
        }
        return new AiSideInfo(side, easy, normal, hard, defense, [..sets]);
    }

    private static ImmutableArray<AiBuildListEntry> ReadBuildList(DefinitionSection section)
    {
        if (section.Fields.Length > 0) throw Invalid($"unexpected key '{section.Fields[0].Name}' in {section.Declaration}", section.Fields[0]);
        var entries = ImmutableArray.CreateBuilder<AiBuildListEntry>();
        foreach (var child in section.Children)
        {
            var (type, template) = Declaration(child);
            if (!type.Equals("Structure", StringComparison.OrdinalIgnoreCase) || template.Length == 0) throw new InvalidDataException($"AIData: unknown block '{child.Declaration}' in {section.Declaration}.");
            Fixed x = Fixed.Zero, y = Fixed.Zero, angle = Fixed.Zero;
            var rebuilds = 0;
            bool initially = false, automatic = true, located = false;
            foreach (var field in child.Fields)
            {
                var text = field.Value.OriginalText.Trim();
                switch (field.Name)
                {
                    case "Location":
                        (x, y) = Coordinates(text, field);
                        located = true;
                        break;
                    case "Rebuilds": rebuilds = checked((int)Number(text, field.Name).Floor()); break;
                    case "Angle": angle = Number(text, field.Name); break;
                    case "InitiallyBuilt": initially = Flag(text, field.Name); break;
                    case "AutomaticallyBuild": automatic = Flag(text, field.Name); break;
                    // BuildListInfo::parseStructure also knows Name, Script, StartingHealth, RallyPointOffset, AutomaticallyBuild, IsUnsellable, IsRepairable; the shipped lists use none of them.
                    default: throw Invalid($"key '{field.Name}' of a build list structure is not supported", field);
                }
            }
            if (!located) throw new InvalidDataException($"AIData: structure '{template}' in {section.Declaration} has no Location.");
            entries.Add(new AiBuildListEntry(template, x, y, angle, rebuilds, initially, automatic));
        }
        return entries.ToImmutable();
    }

    private static (string Type, string Name) Declaration(DefinitionSection section)
    {
        var parts = section.Declaration.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        return (parts.Length > 0 ? parts[0] : string.Empty, parts.Length > 1 ? parts[1] : string.Empty);
    }

    private static string? Canonical(string name, string[] table)
    {
        foreach (var known in table)
            if (known.Equals(name, StringComparison.OrdinalIgnoreCase)) return known;
        return null;
    }

    /// <summary><c>X:501.22 Y:546.25</c> (<c>INI::parseCoord2D</c>).</summary>
    private static (Fixed X, Fixed Y) Coordinates(string text, DefinitionField field)
    {
        Fixed? x = null, y = null;
        foreach (var token in text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.StartsWith("X:", StringComparison.OrdinalIgnoreCase)) x = Number(token[2..], field.Name);
            else if (token.StartsWith("Y:", StringComparison.OrdinalIgnoreCase)) y = Number(token[2..], field.Name);
            else if (!token.StartsWith("Z:", StringComparison.OrdinalIgnoreCase)) throw Invalid($"malformed coordinate '{text}'", field);
        }
        if (x is null || y is null) throw Invalid($"malformed coordinate '{text}'", field);
        return (x.Value, y.Value);
    }

    private static Fixed Number(string text, string key)
    {
        var token = text.Split([' ', '\t', ';'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return decimal.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? Fixed.FromDecimal(value)
            : throw new InvalidDataException($"AIData: '{key}' = '{text}' is not a number.");
    }

    private static bool Flag(string text, string key)
    {
        var token = text.Split([' ', '\t', ';'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        if (token.Equals("Yes", StringComparison.OrdinalIgnoreCase)) return true;
        if (token.Equals("No", StringComparison.OrdinalIgnoreCase)) return false;
        throw new InvalidDataException($"AIData: '{key}' = '{text}' is neither Yes nor No.");
    }

    private static InvalidDataException Invalid(string message, DefinitionField field)
        => new($"AIData: {message} ({field.Location.Source}:{field.Location.Line.ToString(CultureInfo.InvariantCulture)}).");
}
