using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Data.Ini;

namespace CnCNew.Data.Definitions;

public readonly record struct DefinitionId
{
    public DefinitionId(DefinitionKind kind, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Kind = kind;
        Name = name.ToLowerInvariant();
    }

    public DefinitionKind Kind { get; }
    public string Name { get; }
    public override string ToString() => $"{Kind.ToString().ToLowerInvariant()}://{Name}";
}

public enum DefinitionKind
{
    Object, Weapon, WeaponSet, Armor, Damage, Upgrade, Science, Player, Faction,
    CommandButton, CommandSet, FxList, Fx, AudioEvent, DialogEvent, MusicTrack,
    AmbientStream, Locomotor, ExperienceLevel, SpecialPower, GameData, GlobalData,
    Compatibility
}

public readonly record struct GameTime(decimal Milliseconds);
public readonly record struct Distance(decimal Value);
public readonly record struct Speed(decimal Value);
public readonly record struct Angle(decimal Degrees);
public readonly record struct Percentage(decimal Ratio);
public readonly record struct Money(long Value);
public readonly record struct Vector2Value(decimal X, decimal Y);
public readonly record struct Vector3Value(decimal X, decimal Y, decimal Z);
public readonly record struct ColorValue(byte Red, byte Green, byte Blue, byte Alpha = 255);
public readonly record struct ColorKeyframeValue(ColorValue Color, long Frame);
public readonly record struct DefinitionReference(DefinitionKind Kind, string Name, bool IsNone = false);

/// <summary>A value of a strict name table; <see cref="Name"/> is the canonical spelling of the table.</summary>
public sealed record EnumValue(string Table, string Name);

/// <summary>
/// A bit set over a strict name table. Absolute sets list every member (canonical spelling, table order).
/// Relative sets (<c>+A -B</c> tokens) list added and removed members and are resolved by the compiler
/// against the inherited value; resolved sets are always absolute.
/// </summary>
public sealed record FlagsValue(string Table, ImmutableArray<string> Names, bool IsRelative = false, ImmutableArray<string> Removed = default)
{
    public ImmutableArray<string> RemovedNames => Removed.IsDefault ? [] : Removed;
    public bool Contains(string name) => Names.Contains(name, StringComparer.OrdinalIgnoreCase);

    public FlagsValue ResolveAgainst(FlagsValue? baseValue)
    {
        if (!IsRelative) return this;
        var table = NameTables.All.Single(value => value.Name == Table);
        var set = new SortedSet<int>();
        if (baseValue is not null)
            foreach (var name in baseValue.Names) set.Add(Index(table, name));
        foreach (var name in Names) set.Add(Index(table, name));
        foreach (var name in RemovedNames) set.Remove(Index(table, name));
        return new FlagsValue(Table, [..set.Select(table.Canonical)]);
    }

    private static int Index(NameTable table, string name)
        => table.TryIndexOf(name, out var index) ? index : throw new InvalidDataException($"'{name}' is not part of name table {table.Name}.");
}

/// <summary>A set-tagged list of references such as <c>Locomotor = SET_NORMAL A B</c>.</summary>
public sealed record TaggedReferenceList(string Tag, ImmutableArray<DefinitionReference> References);

public enum DefinitionValueKind
{
    Text, Boolean, Integer, Number, Time, Distance, Speed, Angle, Percentage, Money,
    Vector2, Vector3, Color, Enum, Flags, List, Reference, Sentinel, Compatibility,
    IntegerList, ColorKeyframe
}

public sealed record DefinitionValue(DefinitionValueKind Kind, object Value, string OriginalText);
public sealed record CompatibilityValue(string Code, string NormalizedValue);
public sealed record DefinitionField(string Name, DefinitionValue Value, IniSourceLocation Location);

/// <summary>Strict value grammar shared by the generic parser and the schema-driven parsers.</summary>
public static class ValueGrammar
{
    private static readonly char[] LabelSeparators = [' ', '\t', ':'];
    private static readonly char[] TokenSeparators = [' ', '	', '='];

    /// <summary>INI tokens are separated by blanks and '=' (<c>INI::m_seps</c>), so stray '=' characters vanish.</summary>
    public static string[] Tokenize(string text) => text.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries);

    public static bool TryReal(string text, out decimal value)
        => decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    /// <summary>
    /// <c>INI::scanReal</c> uses <c>sscanf("%f")</c>, which accepts the longest numeric prefix and ignores the rest
    /// (shipped data contains <c>18.5.0</c> and <c>40v.0</c>). Returns false when there is no numeric prefix at all.
    /// </summary>
    public static bool TryRealPrefix(string text, out decimal value, out bool hadTrailingText)
    {
        value = 0m;
        hadTrailingText = false;
        var end = 0;
        if (end < text.Length && text[end] is '+' or '-') end++;
        var digits = 0;
        while (end < text.Length && char.IsAsciiDigit(text[end])) { end++; digits++; }
        if (end < text.Length && text[end] == '.')
        {
            var probe = end + 1;
            var fraction = 0;
            while (probe < text.Length && char.IsAsciiDigit(text[probe])) { probe++; fraction++; }
            if (digits + fraction > 0) { end = probe; digits += fraction; }
        }
        if (digits == 0) return false;
        if (!TryReal(text[..end], out value)) return false;
        hadTrailingText = end != text.Length;
        return true;
    }

    public static bool TryInteger(string text, out long value)
        => long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    /// <summary>INI booleans are exactly <c>Yes</c> or <c>No</c> (case-insensitive, <c>INI::scanBool</c>).</summary>
    public static bool TryBool(string text, out bool value)
    {
        if (text.Equals("Yes", StringComparison.OrdinalIgnoreCase)) { value = true; return true; }
        if (text.Equals("No", StringComparison.OrdinalIgnoreCase)) { value = false; return true; }
        value = false;
        return false;
    }

    /// <summary>
    /// Parses <c>Label:value Label:value ...</c> forms the way <c>INI::getNextSubToken</c> does: labels are
    /// matched case-insensitively and ':' behaves as a separator like blanks do (<c>X: 60.0  Y:  35.0</c>).
    /// Returns the numeric texts and the remaining tokens after the required and optional labels.
    /// </summary>
    public static bool TryLabeled(string text, IReadOnlyList<string> required, IReadOnlyList<string> optional,
        out string[] values, out string[] rest)
    {
        var tokens = text.Split(LabelSeparators, StringSplitOptions.RemoveEmptyEntries);
        var found = new List<string>();
        var index = 0;
        foreach (var label in required)
        {
            if (index + 1 >= tokens.Length || !tokens[index].Equals(label, StringComparison.OrdinalIgnoreCase))
            { values = []; rest = []; return false; }
            found.Add(tokens[index + 1]);
            index += 2;
        }
        foreach (var label in optional)
        {
            if (index + 1 < tokens.Length && tokens[index].Equals(label, StringComparison.OrdinalIgnoreCase))
            {
                found.Add(tokens[index + 1]);
                index += 2;
            }
            else break;
        }
        values = [..found];
        rest = tokens[index..];
        return true;
    }

    public static DefinitionValue ParseColorForm(string text, IniSourceLocation location)
    {
        if (!TryLabeled(text, ["R", "G", "B"], ["A"], out var values, out var rest))
            throw Error($"Malformed color '{text}'; expected 'R:n G:n B:n [A:n]'.", location);
        var bytes = new byte[values.Length];
        for (var i = 0; i < values.Length; i++)
            if (!byte.TryParse(values[i], NumberStyles.None, CultureInfo.InvariantCulture, out bytes[i]))
                throw Error($"Color component '{values[i]}' in '{text}' must be an integer from 0 to 255.", location);
        var color = new ColorValue(bytes[0], bytes[1], bytes[2], bytes.Length == 4 ? bytes[3] : (byte)255);
        if (rest.Length == 0 || rest[0].StartsWith("//", StringComparison.Ordinal)) return new(DefinitionValueKind.Color, color, text);
        if (bytes.Length == 3 && rest.Length == 1 && long.TryParse(rest[0], NumberStyles.None, CultureInfo.InvariantCulture, out var frame))
            return new(DefinitionValueKind.ColorKeyframe, new ColorKeyframeValue(color, frame), text);
        throw Error($"Unexpected trailing text '{string.Join(' ', rest)}' after color in '{text}'.", location);
    }

    public static DefinitionValue ParseVectorForm(string text, IniSourceLocation location)
    {
        if (!TryLabeled(text, ["X", "Y"], ["Z"], out var values, out var rest) || rest.Length != 0)
            throw Error($"Malformed vector '{text}'; expected 'X:n Y:n [Z:n]'.", location);
        var numbers = new decimal[values.Length];
        for (var i = 0; i < values.Length; i++)
            if (!TryReal(values[i], out numbers[i]))
                throw Error($"Vector component '{values[i]}' in '{text}' is not a finite number.", location);
        return numbers.Length == 2
            ? new(DefinitionValueKind.Vector2, new Vector2Value(numbers[0], numbers[1]), text)
            : new(DefinitionValueKind.Vector3, new Vector3Value(numbers[0], numbers[1], numbers[2]), text);
    }

    public static DefinitionValue ParseEnum(NameTable table, string text, IniSourceLocation location)
    {
        var token = text.Trim();
        if (!table.TryIndexOf(token, out var index))
            throw Error($"'{token}' is not a member of {table.Name} ({table.Source}).", location);
        return new(DefinitionValueKind.Enum, new EnumValue(table.Name, table.Canonical(index)), text);
    }

    /// <summary>Bit lists with the reference semantics of <c>BitFlags::parse</c> (NONE, plain lists, +/- deltas).</summary>
    public static DefinitionValue ParseFlags(NameTable table, string text, IniSourceLocation location)
    {
        var tokens = text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var added = new SortedSet<int>();
        var removed = new SortedSet<int>();
        var normal = false;
        var delta = false;
        foreach (var token in tokens)
        {
            if (token.Equals("NONE", StringComparison.OrdinalIgnoreCase))
            {
                if (normal || delta) throw Error($"NONE may not be mixed with other flags in '{text}'.", location);
                added.Clear(); removed.Clear();
                return new(DefinitionValueKind.Flags, new FlagsValue(table.Name, []), text);
            }
            if (token[0] is '+' or '-')
            {
                if (normal) throw Error($"Plain and +/- flags may not be mixed in '{text}'.", location);
                var index = Lookup(table, token[1..], text, location);
                if (token[0] == '+') { added.Add(index); removed.Remove(index); } else { removed.Add(index); added.Remove(index); }
                delta = true;
            }
            else
            {
                if (delta) throw Error($"Plain and +/- flags may not be mixed in '{text}'.", location);
                added.Add(Lookup(table, token, text, location));
                normal = true;
            }
        }
        if (delta)
            return new(DefinitionValueKind.Flags, new FlagsValue(table.Name, [..added.Select(table.Canonical)], true, [..removed.Select(table.Canonical)]), text);
        return new(DefinitionValueKind.Flags, new FlagsValue(table.Name, [..added.Select(table.Canonical)]), text);
    }

    /// <summary>
    /// <c>INI::parseDeathTypeFlags</c>: always starts from ALL; ALL/NONE reset, +X sets, -X clears. The result is absolute.
    /// </summary>
    public static DefinitionValue ParseAllNoneDeltaFlags(NameTable table, string text, IniSourceLocation location)
    {
        var set = new SortedSet<int>(Enumerable.Range(0, table.Count));
        foreach (var token in text.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (token.Equals("ALL", StringComparison.OrdinalIgnoreCase)) { set.Clear(); foreach (var i in Enumerable.Range(0, table.Count)) set.Add(i); }
            else if (token.Equals("NONE", StringComparison.OrdinalIgnoreCase)) set.Clear();
            else if (token[0] == '+') set.Add(Lookup(table, token[1..], text, location));
            else if (token[0] == '-') set.Remove(Lookup(table, token[1..], text, location));
            else throw Error($"Unknown token '{token}' in '{text}'; expected ALL, NONE, +NAME or -NAME.", location);
        }
        return new(DefinitionValueKind.Flags, new FlagsValue(table.Name, [..set.Select(table.Canonical)]), text);
    }

    private static int Lookup(NameTable table, string token, string text, IniSourceLocation location)
        => table.TryIndexOf(token, out var index)
            ? index
            : throw Error($"'{token}' in '{text}' is not a member of {table.Name} ({table.Source}).", location);

    public static FormatException Error(string message, IniSourceLocation location)
        => new($"{location.Source}({location.Line},{location.Column}): {message}");
}

public static class DefinitionValueParser
{
    public static DefinitionValue Parse(string key, string text, IniSourceLocation location)
    {
        var value = text.Trim();
        if (value.Equals("NONE", StringComparison.OrdinalIgnoreCase))
            return new(DefinitionValueKind.Sentinel, new DefinitionReference(DefinitionKind.Compatibility, "NONE", true), value);
        if (value.Equals("Yes", StringComparison.OrdinalIgnoreCase) || value.Equals("No", StringComparison.OrdinalIgnoreCase))
            return new(DefinitionValueKind.Boolean, value.Equals("Yes", StringComparison.OrdinalIgnoreCase), value);
        if (Tokenize(value).Any(token => token.Equals("NaN", StringComparison.OrdinalIgnoreCase)
                || token.Equals("Infinity", StringComparison.OrdinalIgnoreCase)
                || token.Equals("+Infinity", StringComparison.OrdinalIgnoreCase)
                || token.Equals("-Infinity", StringComparison.OrdinalIgnoreCase)))
            throw ValueGrammar.Error($"Non-finite numeric value '{value}' is not allowed.", location);

        // Labeled forms are unambiguous and independent of the key name.
        if (value.StartsWith("R:", StringComparison.OrdinalIgnoreCase)) return ValueGrammar.ParseColorForm(value, location);
        if (value.StartsWith("X:", StringComparison.OrdinalIgnoreCase)) return ValueGrammar.ParseVectorForm(value, location);

        if (value.EndsWith('%') && TryDecimal(value[..^1], out var percent))
            return new(DefinitionValueKind.Percentage, new Percentage(percent / 100m), value);
        if (LooksLikeTime(key) && TryDecimal(FirstToken(value), out var time))
            return new(DefinitionValueKind.Time, new GameTime(time), value);
        if (LooksLikeDistance(key) && TryDecimal(FirstToken(value), out var distance))
            return new(DefinitionValueKind.Distance, new Distance(distance), value);
        if (LooksLikeSpeed(key) && TryDecimal(FirstToken(value), out var speed))
            return new(DefinitionValueKind.Speed, new Speed(speed), value);
        if (LooksLikeAngle(key) && TryDecimal(FirstToken(value), out var angle))
            return new(DefinitionValueKind.Angle, new Angle(angle), value);
        if (LooksLikeMoney(key) && long.TryParse(FirstToken(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var money))
            return new(DefinitionValueKind.Money, new Money(money), value);
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
            return new(DefinitionValueKind.Integer, integer, value);
        if (TryDecimal(value, out var number))
            return new(DefinitionValueKind.Number, number, value);
        if (key.Equals("GeometryMajorRadius", StringComparison.OrdinalIgnoreCase) && value == "18.5.0")
            return new(DefinitionValueKind.Compatibility, new CompatibilityValue("shipped-double-decimal-radius", "18.5"), value);
        if (LooksNumeric(value))
            throw ValueGrammar.Error($"Numeric value '{value}' is invalid or outside the supported range.", location);

        var tokens = Tokenize(value);
        if (LooksLikeColor(key) && tokens.Length is 3 or 4 && tokens.All(token => byte.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)))
        {
            var bytes = tokens.Select(token => byte.Parse(token, CultureInfo.InvariantCulture)).ToArray();
            return new(DefinitionValueKind.Color, new ColorValue(bytes[0], bytes[1], bytes[2], bytes.Length == 4 ? bytes[3] : (byte)255), value);
        }
        if (LooksLikeVector(key) && tokens.Length is 2 or 3 && tokens.All(token => TryDecimal(token, out _)))
        {
            var numbers = tokens.Select(token => decimal.Parse(token, NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
            return numbers.Length == 2
                ? new(DefinitionValueKind.Vector2, new Vector2Value(numbers[0], numbers[1]), value)
                : new(DefinitionValueKind.Vector3, new Vector3Value(numbers[0], numbers[1], numbers[2]), value);
        }
        if (TryReferenceKind(key, out var referenceKind) && tokens.Length > 0)
        {
            if (tokens[^1].Equals("NONE", StringComparison.OrdinalIgnoreCase))
                return new(DefinitionValueKind.Sentinel, new DefinitionReference(referenceKind, "NONE", true), value);
            if (key.Equals("TriggeredBy", StringComparison.OrdinalIgnoreCase) || key.Equals("ConflictsWith", StringComparison.OrdinalIgnoreCase)
                || key.Equals("BuildVariations", StringComparison.OrdinalIgnoreCase) || key.Equals("Sciences", StringComparison.OrdinalIgnoreCase))
                return new(DefinitionValueKind.Reference, tokens.Select(token => new DefinitionReference(referenceKind, token)).ToImmutableArray(), value);
            return new(DefinitionValueKind.Reference, new DefinitionReference(referenceKind, tokens[^1]), value);
        }
        if (tokens.Length > 1)
            return new(DefinitionValueKind.List, tokens.ToImmutableArray(), value);
        return new(DefinitionValueKind.Text, value, value);
    }

    public static bool TryReferenceKind(string key, out DefinitionKind kind)
    {
        var normalized = key.Replace("_", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
        kind = normalized switch
        {
            "object" or "objectname" or "buildvariation" or "buildvariations" => DefinitionKind.Object,
            "weapon" or "primaryweapon" or "secondaryweapon" or "tertiaryweapon" => DefinitionKind.Weapon,
            "weaponset" => DefinitionKind.WeaponSet,
            "armorset" => DefinitionKind.Armor,
            "upgrade" or "triggeredby" or "conflictswith" => DefinitionKind.Upgrade,
            "science" or "sciences" or "prerequisitescience" => DefinitionKind.Science,
            "commandset" => DefinitionKind.CommandSet,
            "commandbutton" => DefinitionKind.CommandButton,
            "fxlist" or "fx" or "deathfx" => DefinitionKind.FxList,
            "audioevent" or "sound" or "unitselectsound" or "unitmovesound" or "unitattacksound" => DefinitionKind.AudioEvent,
            "locomotor" => DefinitionKind.Locomotor,
            "experiencelevel" => DefinitionKind.ExperienceLevel,
            "specialpower" or "specialpowertemplate" => DefinitionKind.SpecialPower,
            "playertemplate" => DefinitionKind.Faction,
            _ => DefinitionKind.Compatibility,
        };
        return kind != DefinitionKind.Compatibility;
    }

    private static string FirstToken(string value) => Tokenize(value).FirstOrDefault() ?? string.Empty;
    private static string[] Tokenize(string value) => value.Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries);
    private static bool TryDecimal(string value, out decimal result)
        => decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
    private static bool LooksNumeric(string value)
        => value.Length > 0 && (char.IsDigit(value[0]) || value[0] is '+' or '-')
            && value.All(character => char.IsDigit(character) || character is '+' or '-' or '.' or 'e' or 'E');
    private static bool LooksLikeTime(string key) => HasAny(key, "Time", "Delay", "Duration", "Interval", "Lifetime", "Reload", "Cooldown");
    private static bool LooksLikeDistance(string key) => HasAny(key, "Range", "Radius", "Distance", "Height", "Width", "Length");
    private static bool LooksLikeSpeed(string key) => HasAny(key, "Speed", "Velocity");
    private static bool LooksLikeAngle(string key) => HasAny(key, "Angle", "Pitch", "Yaw", "Arc");
    private static bool LooksLikeMoney(string key) => HasAny(key, "Cost", "Cash", "Money", "Bounty", "Price");
    private static bool LooksLikeColor(string key) => key.Contains("Color", StringComparison.OrdinalIgnoreCase);
    private static bool LooksLikeVector(string key) => HasAny(key, "Position", "Offset", "Vector", "Point");
    private static bool HasAny(string value, params string[] terms) => terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));
}
