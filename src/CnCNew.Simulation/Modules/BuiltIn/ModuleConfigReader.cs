using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>Typed accessors over a compiled module configuration. Absent optional fields use the reference constructor default, stated at each call.</summary>
internal static class ModuleConfigReader
{
    public static DefinitionField? Find(ModuleConfiguration configuration, string name)
        => configuration.Fields.LastOrDefault(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Every occurrence of an accumulating key, in file order (<c>UpgradeOCL</c>, <c>UpgradeMoneyAmount</c>).</summary>
    public static IEnumerable<DefinitionField> All(ModuleConfiguration configuration, string name)
        => configuration.Fields.Where(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The <c>SCIENCE VALUE</c> pairs of an accumulating two-token key.</summary>
    public static ImmutableArray<(string First, string Second)> Pairs(ModuleConfiguration configuration, string name)
        => [..All(configuration, name).Select(field => field.Value.Value is ImmutableArray<string> { Length: 2 } tokens ? (tokens[0], tokens[1]) : throw Unexpected(configuration, field))];

    public static Fixed Real(ModuleConfiguration configuration, string name, Fixed fallback)
    {
        var field = Find(configuration, name);
        if (field is null) return fallback;
        return field.Value.Value switch
        {
            decimal number => Fixed.FromDecimal(number),
            long integer => Fixed.FromInt(integer),
            Distance distance => Fixed.FromDecimal(distance.Value),
            Percentage percentage => Fixed.FromDecimal(percentage.Ratio),
            Money money => Fixed.FromInt(money.Value),
            CompatibilityValue compatibility => Fixed.FromDecimal(decimal.Parse(compatibility.NormalizedValue, System.Globalization.CultureInfo.InvariantCulture)),
            _ => throw Unexpected(configuration, field),
        };
    }

    /// <summary>A duration in milliseconds converted to whole ticks, rounded up (see <see cref="SimulationTiming"/>).</summary>
    public static long Ticks(ModuleConfiguration configuration, string name, long fallback)
    {
        var field = Find(configuration, name);
        if (field is null) return fallback;
        return field.Value.Value is GameTime time ? SimulationTiming.DurationMillisecondsToTicks(time.Milliseconds) : throw Unexpected(configuration, field);
    }

    public static long Int(ModuleConfiguration configuration, string name, long fallback)
    {
        var field = Find(configuration, name);
        if (field is null) return fallback;
        return field.Value.Value switch
        {
            long integer => integer,
            CompatibilityValue compatibility => long.Parse(compatibility.NormalizedValue, System.Globalization.CultureInfo.InvariantCulture),
            _ => throw Unexpected(configuration, field),
        };
    }

    public static bool Bool(ModuleConfiguration configuration, string name, bool fallback)
    {
        var field = Find(configuration, name);
        if (field is null) return fallback;
        return field.Value.Value is bool value ? value : throw Unexpected(configuration, field);
    }

    public static string Text(ModuleConfiguration configuration, string name)
    {
        var field = Find(configuration, name);
        if (field is null) return string.Empty;
        return field.Value.Kind switch
        {
            DefinitionValueKind.Text => (string)field.Value.Value,
            DefinitionValueKind.Sentinel => string.Empty,
            DefinitionValueKind.Reference when field.Value.Value is DefinitionReference reference => reference.Name,
            _ => field.Value.OriginalText.Trim(),
        };
    }

    public static ImmutableArray<string> Names(ModuleConfiguration configuration, string name)
    {
        var field = Find(configuration, name);
        if (field is null) return [];
        return field.Value.Value switch
        {
            DefinitionReference { IsNone: true } => [],
            DefinitionReference reference => [reference.Name],
            ImmutableArray<DefinitionReference> references => [..references.Where(value => !value.IsNone).Select(value => value.Name)],
            string text => [text],
            ImmutableArray<string> tokens => tokens,
            _ => throw Unexpected(configuration, field),
        };
    }

    /// <summary>The names of every occurrence of an accumulating reference key, in file order.</summary>
    public static ImmutableArray<string> AccumulatedNames(ModuleConfiguration configuration, string name)
        => [..All(configuration, name).SelectMany(field => field.Value.Value switch
        {
            DefinitionReference { IsNone: true } => [],
            DefinitionReference reference => [reference.Name],
            ImmutableArray<DefinitionReference> references => references.Where(value => !value.IsNone).Select(value => value.Name),
            _ => throw Unexpected(configuration, field),
        })];

    public static Point3 Vector(ModuleConfiguration configuration, string name)
    {
        var field = Find(configuration, name);
        if (field is null) return default;
        return field.Value.Value is Vector3Value vector
            ? new Point3(Fixed.FromDecimal(vector.X), Fixed.FromDecimal(vector.Y), Fixed.FromDecimal(vector.Z))
            : throw Unexpected(configuration, field);
    }

    public static FlagsValue? Flags(ModuleConfiguration configuration, string name)
        => Find(configuration, name)?.Value.Value as FlagsValue;

    private static InvalidDataException Unexpected(ModuleConfiguration configuration, DefinitionField field)
        => new($"{field.Location.Source}({field.Location.Line}): field '{field.Name}' of module '{configuration.Name}' has an unexpected value kind {field.Value.Kind}.");
}
