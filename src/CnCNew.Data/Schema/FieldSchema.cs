using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Data.Definitions;
using CnCNew.Data.Ini;

namespace CnCNew.Data.Schema;

/// <summary>Grammar of a schema-typed key. <see cref="Opaque"/> keys are known but keep the heuristic Phase-3 typing.</summary>
public enum FieldType
{
    Text, Bool, Integer, Money, Real, Distance, Angle, Percent, DurationMs,
    Enum, Flags, AllNoneDeltaFlags, ColorInt, IntegerList, Reference, ReferenceList, LocomotorSet, Opaque
}

/// <summary>What a repeated key inside one definition means (derived from the reference parse functions).</summary>
public enum RepeatPolicy
{
    /// <summary>The last occurrence wins; repeats inside one block are counted as compatibility cases.</summary>
    Overwrite,
    /// <summary>Every occurrence is kept in order (<c>push_back</c>/append parsers).</summary>
    Accumulate,
    /// <summary>An occurrence replaces an earlier one with the same first token, otherwise it is appended.</summary>
    IndexedByFirstToken,
    /// <summary>Same as <see cref="IndexedByFirstToken"/> but keyed by the first two tokens.</summary>
    IndexedByFirstTwoTokens,
}

public sealed record FieldSchema(
    string Name,
    FieldType Type,
    bool Required = false,
    RepeatPolicy Repeat = RepeatPolicy.Overwrite,
    NameTable? Table = null,
    DefinitionKind? ReferenceKind = null,
    long Min = long.MinValue,
    long Max = long.MaxValue,
    int Count = 0,
    string? Source = null)
{
    /// <summary>
    /// Number of trailing tokens the reference parser reads past and ignores: single-token types read one token
    /// (<c>INI::parseBool</c>, <c>parseReal</c>, <c>parseByteSizedIndexList</c>, ...), integer lists read exactly Count.
    /// </summary>
    public int IgnoredTokenCount(string text)
    {
        var count = ValueGrammar.Tokenize(text).Length;
        return Type switch
        {
            FieldType.Bool or FieldType.Integer or FieldType.Money or FieldType.Real or FieldType.Distance or FieldType.Angle
                or FieldType.DurationMs or FieldType.Enum => Math.Max(0, count - 1),
            FieldType.IntegerList => Math.Max(0, count - Count),
            _ => 0,
        };
    }

    public DefinitionValue Parse(string text, IniSourceLocation location)
    {
        var value = text.Trim();
        var full = value;
        if (IgnoredTokenCount(value) > 0 && Type != FieldType.IntegerList)
            value = ValueGrammar.Tokenize(value)[0];
        var result = ParseCore(value, location);
        return result.OriginalText == full ? result : result with { OriginalText = full };
    }

    private DefinitionValue ParseCore(string value, IniSourceLocation location)
    {
        switch (Type)
        {
            case FieldType.Opaque:
                return DefinitionValueParser.Parse(Name, value, location);
            case FieldType.Text:
                return new(DefinitionValueKind.Text, value, value);
            case FieldType.Bool:
                return ValueGrammar.TryBool(value, out var flag)
                    ? new(DefinitionValueKind.Boolean, flag, value)
                    : throw ValueGrammar.Error($"'{Name}' expects Yes or No, got '{value}'.", location);
            case FieldType.Integer:
                return new(DefinitionValueKind.Integer, ParseInteger(value, location), value);
            case FieldType.Money:
                return new(DefinitionValueKind.Money, new Money(ParseInteger(value, location)), value);
            case FieldType.Real:
                return ParseReal(value, location, out var realCompat) is { } real
                    ? new(DefinitionValueKind.Number, real, value)
                    : new(DefinitionValueKind.Compatibility, realCompat!, value);
            case FieldType.Distance:
                return ParseReal(value, location, out var distanceCompat) is { } distance
                    ? new(DefinitionValueKind.Distance, new Distance(distance), value)
                    : new(DefinitionValueKind.Compatibility, distanceCompat!, value);
            case FieldType.Angle:
                return new(DefinitionValueKind.Angle, new Angle(ParseRealStrict(value, location)), value);
            case FieldType.Percent:
                if (!value.EndsWith('%')) throw ValueGrammar.Error($"'{Name}' expects a percentage such as 50%, got '{value}'.", location);
                return new(DefinitionValueKind.Percentage, new Percentage(ParseRealStrict(value[..^1], location) / 100m), value);
            case FieldType.DurationMs:
                // INI::scanUnsignedInt is sscanf("%u"): negative numbers wrap modulo 2^32 (shipped MD_GLA01 uses -1).
                if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var ms) || ms < int.MinValue || ms > uint.MaxValue)
                    throw ValueGrammar.Error($"'{Name}' expects an unsigned integer duration in milliseconds, got '{value}'.", location);
                return new(DefinitionValueKind.Time, new GameTime(ms < 0 ? ms + 4294967296L : ms), value);
            case FieldType.Enum:
                return ValueGrammar.ParseEnum(Table ?? throw new InvalidOperationException($"Schema '{Name}' has no table."), value, location);
            case FieldType.Flags:
                return ValueGrammar.ParseFlags(Table ?? throw new InvalidOperationException($"Schema '{Name}' has no table."), value, location);
            case FieldType.AllNoneDeltaFlags:
                return ValueGrammar.ParseAllNoneDeltaFlags(Table ?? throw new InvalidOperationException($"Schema '{Name}' has no table."), value, location);
            case FieldType.ColorInt:
                var color = ValueGrammar.ParseColorForm(value, location);
                return color.Kind == DefinitionValueKind.Color ? color : throw ValueGrammar.Error($"'{Name}' expects a plain color.", location);
            case FieldType.IntegerList:
                var parts = ValueGrammar.Tokenize(value);
                // ThingTemplate::parseIntList reads exactly Count tokens and ignores the rest; extras are reported by the compiler.
                if (parts.Length < Count) throw ValueGrammar.Error($"'{Name}' expects {Count} integers, got {parts.Length}.", location);
                return new(DefinitionValueKind.IntegerList, parts.Take(Count).Select(part => ParseInteger(part, location)).ToImmutableArray(), value);
            case FieldType.Reference:
                var single = ValueGrammar.Tokenize(value);
                if (single.Length != 1) throw ValueGrammar.Error($"'{Name}' expects one name, got '{value}'.", location);
                return single[0].Equals("NONE", StringComparison.OrdinalIgnoreCase)
                    ? new(DefinitionValueKind.Sentinel, new DefinitionReference(ReferenceKind ?? DefinitionKind.Compatibility, "NONE", true), value)
                    : new(DefinitionValueKind.Reference, new DefinitionReference(ReferenceKind!.Value, single[0]), value);
            case FieldType.ReferenceList:
                var names = ValueGrammar.Tokenize(value);
                if (names.Length == 0) throw ValueGrammar.Error($"'{Name}' expects at least one name.", location);
                return new(DefinitionValueKind.Reference,
                    names.Select(name => name.Equals("NONE", StringComparison.OrdinalIgnoreCase)
                        ? new DefinitionReference(ReferenceKind!.Value, "NONE", true)
                        : new DefinitionReference(ReferenceKind!.Value, name)).ToImmutableArray(), value);
            case FieldType.LocomotorSet:
                var tokens = ValueGrammar.Tokenize(value);
                if (tokens.Length == 0 || !NameTables.LocomotorSet.TryIndexOf(tokens[0], out var setIndex))
                    throw ValueGrammar.Error($"'{Name}' expects a locomotor set name ({NameTables.LocomotorSet.Source}) first, got '{value}'.", location);
                return new(DefinitionValueKind.Reference, new TaggedReferenceList(NameTables.LocomotorSet.Canonical(setIndex),
                    tokens.Skip(1).Where(token => !token.Equals("None", StringComparison.OrdinalIgnoreCase))
                        .Select(token => new DefinitionReference(DefinitionKind.Locomotor, token)).ToImmutableArray()), value);
            default:
                throw new InvalidOperationException($"Unhandled field type {Type}.");
        }
    }

    private long ParseInteger(string value, IniSourceLocation location)
    {
        if (!ValueGrammar.TryInteger(value, out var result))
            throw ValueGrammar.Error($"'{Name}' expects an integer, got '{value}'.", location);
        if (result < Min || result > Max)
            throw ValueGrammar.Error($"'{Name}' value {result} is outside {Min}..{Max}.", location);
        return result;
    }

    private decimal ParseRealStrict(string value, IniSourceLocation location)
        => ValueGrammar.TryReal(value, out var result)
            ? result
            : throw ValueGrammar.Error($"'{Name}' expects a finite number, got '{value}'.", location);

    // Exact numbers are typed values. Numbers followed by text are accepted the way sscanf("%f") accepts them, but only
    // as an explicit compatibility value (the shipped 18.5.0 and 40v.0 typos); no numeric prefix at all is an error.
    private decimal? ParseReal(string value, IniSourceLocation location, out CompatibilityValue? compatibility)
    {
        compatibility = null;
        if (ValueGrammar.TryReal(value, out var result)) return result;
        if (ValueGrammar.TryRealPrefix(value, out var prefix, out _))
        {
            compatibility = new CompatibilityValue(
                Name.Equals("GeometryMajorRadius", StringComparison.OrdinalIgnoreCase) && value == "18.5.0"
                    ? "shipped-double-decimal-radius" : "shipped-real-trailing-text",
                prefix.ToString(CultureInfo.InvariantCulture));
            return null;
        }
        throw ValueGrammar.Error($"'{Name}' expects a finite number, got '{value}'.", location);
    }
}

/// <summary>Case-insensitive key lookup over a fixed schema list.</summary>
public sealed class SchemaSet
{
    private readonly ImmutableDictionary<string, FieldSchema> _byName;

    public SchemaSet(string name, IEnumerable<FieldSchema> fields)
    {
        Name = name;
        var builder = ImmutableDictionary.CreateBuilder<string, FieldSchema>(StringComparer.OrdinalIgnoreCase);
        foreach (var field in fields.OrderBy(value => value.Name, StringComparer.Ordinal))
            if (!builder.TryAdd(field.Name, field))
                throw new InvalidOperationException($"Duplicate schema key '{field.Name}' in {name}.");
        _byName = builder.ToImmutable();
        Fields = [.._byName.Values.OrderBy(value => value.Name, StringComparer.Ordinal)];
    }

    public string Name { get; }
    public ImmutableArray<FieldSchema> Fields { get; }
    public bool TryGet(string key, out FieldSchema schema) => _byName.TryGetValue(key, out schema!);
}
