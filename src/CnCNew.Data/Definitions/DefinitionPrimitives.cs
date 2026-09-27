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
public readonly record struct DefinitionReference(DefinitionKind Kind, string Name, bool IsNone = false);

public enum DefinitionValueKind
{
    Text, Boolean, Integer, Number, Time, Distance, Speed, Angle, Percentage, Money,
    Vector2, Vector3, Color, Enum, Flags, List, Reference, Sentinel, Compatibility
}

public sealed record DefinitionValue(DefinitionValueKind Kind, object Value, string OriginalText);
public sealed record CompatibilityValue(string Code, string NormalizedValue);
public sealed record DefinitionField(string Name, DefinitionValue Value, IniSourceLocation Location);

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
            throw Error($"Non-finite numeric value '{value}' is not allowed.", location);

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
            throw Error($"Numeric value '{value}' is invalid or outside the supported range.", location);

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
            "playertemplate" => DefinitionKind.Player,
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
    private static FormatException Error(string message, IniSourceLocation location)
        => new($"{location.Source}({location.Line},{location.Column}): {message}");
}
