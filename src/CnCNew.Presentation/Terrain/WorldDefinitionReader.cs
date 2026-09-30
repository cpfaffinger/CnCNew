using System.Numerics;
using CnCNew.Data.Definitions;

namespace CnCNew.Presentation.Terrain;

/// <summary>
/// Strict access to the presentation-relevant keys that <c>CnCNew.Data</c> still types heuristically (deferred GameData keys, the
/// <c>Terrain</c>, <c>WaterSet</c> and <c>WaterTransparency</c> compatibility blocks). A missing key or an unexpected value kind throws;
/// nothing falls back to a default.
/// </summary>
internal static class WorldDefinitionReader
{
    public static GameDefinition GameData(CompiledDefinitionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        GameDefinition? found = null;
        foreach (var definition in graph.Definitions)
        {
            if (definition.Id.Kind != DefinitionKind.GameData) continue;
            if (found is not null) throw new InvalidDataException("The definition graph holds more than one GameData definition.");
            found = definition;
        }
        return found ?? throw new InvalidDataException("The definition graph has no GameData definition.");
    }

    public static GameDefinition Compatibility(CompiledDefinitionGraph graph, string declarationType, string name)
    {
        var id = new DefinitionId(DefinitionKind.Compatibility, $"{declarationType.ToLowerInvariant()}/{name}");
        foreach (var definition in graph.Definitions)
            if (definition.Id == id) return definition;
        throw new InvalidDataException($"The definition graph has no '{declarationType} {name}' block.");
    }

    public static DefinitionValue Required(GameDefinition definition, string key)
    {
        DefinitionValue? value = null;
        foreach (var field in definition.Fields)
            if (field.Name.Equals(key, StringComparison.OrdinalIgnoreCase)) value = field.Value;
        return value ?? throw new InvalidDataException($"{definition.Id} has no '{key}' key.");
    }

    public static bool Has(GameDefinition definition, string key)
        => definition.Fields.Any(field => field.Name.Equals(key, StringComparison.OrdinalIgnoreCase));

    public static float Real(GameDefinition definition, string key)
    {
        var value = Required(definition, key);
        return value.Kind switch
        {
            DefinitionValueKind.Number => (float)(decimal)value.Value,
            DefinitionValueKind.Integer => (long)value.Value,
            DefinitionValueKind.Angle => (float)((Angle)value.Value).Degrees,
            DefinitionValueKind.Distance => (float)((Distance)value.Value).Value,
            DefinitionValueKind.Speed => (float)((Speed)value.Value).Value,
            _ => throw new InvalidDataException($"{definition.Id} key '{key}' is {value.Kind} '{value.OriginalText}', expected a real number."),
        };
    }

    public static int Integer(GameDefinition definition, string key)
    {
        var value = Required(definition, key);
        return value.Kind == DefinitionValueKind.Integer
            ? checked((int)(long)value.Value)
            : throw new InvalidDataException($"{definition.Id} key '{key}' is {value.Kind} '{value.OriginalText}', expected an integer.");
    }

    public static bool Boolean(GameDefinition definition, string key)
    {
        var value = Required(definition, key);
        return value.Kind == DefinitionValueKind.Boolean
            ? (bool)value.Value
            : throw new InvalidDataException($"{definition.Id} key '{key}' is {value.Kind} '{value.OriginalText}', expected Yes/No.");
    }

    public static string Text(GameDefinition definition, string key)
    {
        var value = Required(definition, key);
        return value.Kind == DefinitionValueKind.Text
            ? (string)value.Value
            : throw new InvalidDataException($"{definition.Id} key '{key}' is {value.Kind} '{value.OriginalText}', expected text.");
    }

    public static ColorValue Color(GameDefinition definition, string key)
    {
        var value = Required(definition, key);
        return value.Kind == DefinitionValueKind.Color
            ? (ColorValue)value.Value
            : throw new InvalidDataException($"{definition.Id} key '{key}' is {value.Kind} '{value.OriginalText}', expected a colour.");
    }

    /// <summary><c>INI::parseRGBColor</c>: each byte channel divided by 255.</summary>
    public static Vector3 UnitColor(GameDefinition definition, string key)
    {
        var color = Color(definition, key);
        return new Vector3(color.Red / 255f, color.Green / 255f, color.Blue / 255f);
    }

    public static Vector3 Vector(GameDefinition definition, string key)
    {
        var value = Required(definition, key);
        return value.Kind == DefinitionValueKind.Vector3 && value.Value is Vector3Value vector
            ? new Vector3((float)vector.X, (float)vector.Y, (float)vector.Z)
            : throw new InvalidDataException($"{definition.Id} key '{key}' is {value.Kind} '{value.OriginalText}', expected 'X:n Y:n Z:n'.");
    }
}
