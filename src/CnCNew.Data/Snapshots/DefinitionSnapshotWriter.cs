using System.Text.Json;
using System.Text.Json.Serialization;
using CnCNew.Data.Definitions;

namespace CnCNew.Data.Snapshots;

public static class DefinitionSnapshotWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static byte[] WriteCanonical(CompiledDefinitionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var model = new
        {
            schemaVersion = 1,
            definitions = graph.Definitions.OrderBy(value => value.Id.ToString(), StringComparer.Ordinal).Select(value => new
            {
                id = value.Id.ToString(), kind = value.Id.Kind.ToString(), name = value.OriginalName,
                source = new { path = value.Location.Source.Replace('\\', '/'), value.Location.Line, value.Location.Column },
                fields = value.Fields.OrderBy(field => field.Name, StringComparer.Ordinal).Select(field => new
                {
                    field.Name, kind = field.Value.Kind.ToString(), value = CanonicalValue(field.Value), original = field.Value.OriginalText,
                    source = new { path = field.Location.Source.Replace('\\', '/'), field.Location.Line, field.Location.Column }
                }),
                modules = value.Modules.Select(module => new
                {
                    module.Name, module.Tag, owner = module.OwnerKind.ToString(),
                    fields = module.Fields.OrderBy(field => field.Name, StringComparer.Ordinal).Select(field => new { field.Name, kind = field.Value.Kind.ToString(), value = CanonicalValue(field.Value), original = field.Value.OriginalText })
                })
            }),
            references = graph.References.Select(edge => new { source = edge.Source.ToString(), edge.Field, targetKind = edge.Target.Kind.ToString(), edge.Target.Name, edge.Resolved, edge.Fallback }),
            compatibility = graph.Audit.Compatibility.Select(value => new { value.Category, value.Name, value.Count, owners = value.OwnerKinds, value.PlannedTarget }),
        };
        return JsonSerializer.SerializeToUtf8Bytes(model, Options);
    }

    private static object CanonicalValue(DefinitionValue value) => value.Value switch
    {
        decimal number => number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        GameTime time => new { milliseconds = time.Milliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        Distance distance => new { value = distance.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        Speed speed => new { value = speed.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        Angle angle => new { degrees = angle.Degrees.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        Percentage percentage => new { ratio = percentage.Ratio.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        Money money => money.Value,
        Vector2Value vector => new { x = vector.X.ToString(System.Globalization.CultureInfo.InvariantCulture), y = vector.Y.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        Vector3Value vector => new { x = vector.X.ToString(System.Globalization.CultureInfo.InvariantCulture), y = vector.Y.ToString(System.Globalization.CultureInfo.InvariantCulture), z = vector.Z.ToString(System.Globalization.CultureInfo.InvariantCulture) },
        ColorValue color => new { color.Red, color.Green, color.Blue, color.Alpha },
        DefinitionReference reference => new { kind = reference.Kind.ToString(), reference.Name, reference.IsNone },
        _ => value.Value,
    };
}
