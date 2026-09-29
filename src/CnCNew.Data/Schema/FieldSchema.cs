using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Data.Definitions;
using CnCNew.Data.Ini;

namespace CnCNew.Data.Schema;

/// <summary>Grammar of a schema-typed key. <see cref="Opaque"/> keys are known but keep the heuristic Phase-3 typing.</summary>
public enum FieldType
{
    Text, Bool, Integer, Money, Real, Distance, Angle, Percent, DurationMs,
    Enum, Flags, AllNoneDeltaFlags, ColorInt, IntegerList, Reference, ReferenceList, LocomotorSet, Opaque,
    /// <summary>The repeatable <c>&lt;Default|EnumName&gt; &lt;percent&gt;</c> pair of <c>Armor</c>/<c>AmountForMajorFX</c>-style keys.</summary>
    CoefficientByEnum,
    /// <summary><c>WeaponTemplate::parseShotDelay</c>: a plain millisecond duration (min == max) or <c>Min:x Max:y</c>.</summary>
    ShotDelayMs,
    /// <summary><c>INI::parseCoord3D</c>: <c>X:n Y:n Z:n</c> with all three labels required.</summary>
    Vector3,
    /// <summary>Exactly <see cref="FieldSchema.Count"/> plain tokens (<c>getNextAsciiString</c> called that often); further tokens are ignored.</summary>
    TextList,
    /// <summary>
    /// <c>ObjectCreationList.cpp parseDebrisObjectNames</c>: the retail loop stores a token, then reads and discards the next one, so only the
    /// odd-positioned tokens (1st, 3rd, ...) are kept. Yields a reference list when a reference kind is set, otherwise a text list.
    /// </summary>
    RetailAlternatingNames,
    /// <summary><c>&lt;Name&gt; [count]</c> with the count defaulting to 1 (<c>DeliverPayloadNugget::parsePayload</c>).</summary>
    CountedReference,
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
    string? Source = null,
    /// <summary>For <see cref="FieldType.CoefficientByEnum"/>: how the value token after the enum name/<c>Default</c> is parsed.</summary>
    FieldType? InnerType = null)
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
            FieldType.IntegerList or FieldType.TextList => Math.Max(0, count - Count),
            FieldType.RetailAlternatingNames => count / 2,
            FieldType.CountedReference => Math.Max(0, count - 2),
            _ => 0,
        };
    }

    public DefinitionValue Parse(string text, IniSourceLocation location)
    {
        var value = text.Trim();
        var full = value;
        var readsWholeText = Type is FieldType.IntegerList or FieldType.TextList or FieldType.RetailAlternatingNames or FieldType.CountedReference;
        if (IgnoredTokenCount(value) > 0 && !readsWholeText)
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
                return new(DefinitionValueKind.Time, new GameTime(ParseUnsignedMs(value, location)), value);
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
            case FieldType.ShotDelayMs:
                decimal minMs, maxMs;
                if (ValueGrammar.TryLabeled(value, ["Min", "Max"], [], out var delayValues, out var delayRest) && delayRest.Length == 0)
                {
                    minMs = ParseMsReal(delayValues[0], location);
                    maxMs = ParseMsReal(delayValues[1], location);
                }
                else
                    minMs = maxMs = ParseMsReal(value, location);
                return new(DefinitionValueKind.ShotDelay, new ShotDelayValue(new GameTime(minMs), new GameTime(maxMs)), value);
            case FieldType.Vector3:
                if (!ValueGrammar.TryLabeled(value, ["X", "Y", "Z"], [], out var vectorTexts, out var vectorRest) || vectorRest.Length != 0)
                    throw ValueGrammar.Error($"'{Name}' expects 'X:n Y:n Z:n', got '{value}'.", location);
                var vector = vectorTexts.Select(part => ValueGrammar.TryReal(part, out var component)
                    ? component : throw ValueGrammar.Error($"'{Name}' component '{part}' is not a finite number.", location)).ToArray();
                return new(DefinitionValueKind.Vector3, new Vector3Value(vector[0], vector[1], vector[2]), value);
            case FieldType.TextList:
                var listTokens = ValueGrammar.Tokenize(value);
                if (listTokens.Length < Count) throw ValueGrammar.Error($"'{Name}' expects {Count} names, got {listTokens.Length}.", location);
                return new(DefinitionValueKind.List, listTokens.Take(Count).ToImmutableArray(), value);
            case FieldType.RetailAlternatingNames:
                var allNames = ValueGrammar.Tokenize(value);
                if (allNames.Length == 0) throw ValueGrammar.Error($"'{Name}' expects at least one name.", location);
                var keptNames = allNames.Where((_, index) => index % 2 == 0).ToImmutableArray();
                return ReferenceKind is { } namesKind
                    ? new(DefinitionValueKind.Reference, keptNames.Select(name => new DefinitionReference(namesKind, name)).ToImmutableArray(), value)
                    : new(DefinitionValueKind.List, keptNames, value);
            case FieldType.CountedReference:
                var pairTokens = ValueGrammar.Tokenize(value);
                if (pairTokens.Length == 0) throw ValueGrammar.Error($"'{Name}' expects '<name> [count]'.", location);
                var pairCount = pairTokens.Length > 1 ? ParseInteger(pairTokens[1], location) : 1;
                return new(DefinitionValueKind.Reference,
                    new CountedReference(new DefinitionReference(ReferenceKind!.Value, pairTokens[0]), checked((int)pairCount)), value);
            case FieldType.CoefficientByEnum:
                var coefficientTokens = ValueGrammar.Tokenize(value);
                if (coefficientTokens.Length != 2) throw ValueGrammar.Error($"'{Name}' expects '<Default|name> <value>', got '{value}'.", location);
                var table = Table ?? throw new InvalidOperationException($"Schema '{Name}' has no table.");
                string? enumName = null;
                if (!coefficientTokens[0].Equals("Default", StringComparison.OrdinalIgnoreCase))
                {
                    if (!table.TryIndexOf(coefficientTokens[0], out var enumIndex))
                        throw ValueGrammar.Error($"'{coefficientTokens[0]}' in '{value}' is neither 'Default' nor a member of {table.Name} ({table.Source}).", location);
                    enumName = table.Canonical(enumIndex);
                }
                var innerSchema = this with { Type = InnerType ?? throw new InvalidOperationException($"Schema '{Name}' has no InnerType."), Table = null };
                var inner = innerSchema.ParseCore(coefficientTokens[1], location);
                return new(DefinitionValueKind.Coefficient, new CoefficientValue(enumName, inner), value);
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

    // INI::scanUnsignedInt is sscanf("%u"): negative numbers wrap modulo 2^32 (shipped MD_GLA01 uses MinLifetime = -1).
    private long ParseUnsignedMs(string value, IniSourceLocation location)
    {
        if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var ms) || ms < int.MinValue || ms > uint.MaxValue)
            throw ValueGrammar.Error($"'{Name}' expects an unsigned integer duration in milliseconds, got '{value}'.", location);
        return ms < 0 ? ms + 4294967296L : ms;
    }

    private decimal ParseMsReal(string value, IniSourceLocation location)
        => ValueGrammar.TryReal(value, out var result) && result >= 0m
            ? result
            : throw ValueGrammar.Error($"'{Name}' expects a non-negative duration in milliseconds, got '{value}'.", location);

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
