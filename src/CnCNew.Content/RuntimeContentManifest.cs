using System.Text.Json;
using System.Text.Json.Serialization;

namespace CnCNew.Content;

public sealed record RuntimeContentManifest(
    int SchemaVersion,
    RuntimeImporter Importer,
    IReadOnlyList<RuntimeAsset> Assets)
{
    public const int CurrentSchemaVersion = 2;
}

public sealed record RuntimeImporter(string Name, string Version, IReadOnlyDictionary<string, string> Options);

public sealed record RuntimeAsset(
    string Id,
    string VirtualPath,
    string Type,
    string Sha256,
    long Bytes,
    RuntimeOrigin Origin,
    IReadOnlyList<RuntimeDependency> Dependencies,
    IReadOnlyList<RuntimeArtifact> Artifacts,
    IReadOnlyList<RuntimeDiagnostic> Diagnostics);

public sealed record RuntimeOrigin(string SelectedSource, int Priority, IReadOnlyList<RuntimeCandidate> Candidates);
public sealed record RuntimeCandidate(int Priority, string Source, long Bytes);
public sealed record RuntimeDependency(string TargetId, string Kind, string Status, string? Fallback);
public sealed record RuntimeArtifact(string Path, string Sha256, long Bytes);
public sealed record RuntimeDiagnostic(string Severity, string Code, string Message);

public static class RuntimeManifestSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static RuntimeContentManifest Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var manifest = JsonSerializer.Deserialize<RuntimeContentManifest>(stream, Options)
            ?? throw new InvalidDataException("Runtime manifest is empty.");
        Validate(manifest);
        return Canonicalize(manifest);
    }

    public static byte[] WriteCanonical(RuntimeContentManifest manifest)
    {
        Validate(manifest);
        return JsonSerializer.SerializeToUtf8Bytes(Canonicalize(manifest), Options);
    }

    public static RuntimeContentManifest Canonicalize(RuntimeContentManifest manifest)
        => manifest with
        {
            Importer = manifest.Importer with
            {
                Options = new SortedDictionary<string, string>(
                    manifest.Importer.Options.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal),
                    StringComparer.Ordinal),
            },
            Assets = manifest.Assets.OrderBy(asset => asset.Id, StringComparer.Ordinal).Select(asset => asset with
            {
                Dependencies = asset.Dependencies.OrderBy(value => value.TargetId, StringComparer.Ordinal)
                    .ThenBy(value => value.Kind, StringComparer.Ordinal).ToArray(),
                Artifacts = asset.Artifacts.OrderBy(value => value.Path, StringComparer.Ordinal).ToArray(),
                Diagnostics = asset.Diagnostics.OrderBy(value => value.Code, StringComparer.Ordinal)
                    .ThenBy(value => value.Message, StringComparer.Ordinal).ToArray(),
                Origin = asset.Origin with
                {
                    Candidates = asset.Origin.Candidates.OrderBy(value => value.Priority).ToArray(),
                },
            }).ToArray(),
        };

    public static void Validate(RuntimeContentManifest manifest)
    {
        if (manifest.SchemaVersion != RuntimeContentManifest.CurrentSchemaVersion)
        {
            throw new InvalidDataException(
                $"Unsupported runtime manifest schema {manifest.SchemaVersion}; expected {RuntimeContentManifest.CurrentSchemaVersion}.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Importer.Name) || string.IsNullOrWhiteSpace(manifest.Importer.Version))
        {
            throw new InvalidDataException("Runtime manifest importer name and version are required.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in manifest.Assets)
        {
            var path = ContentPath.Normalize(asset.VirtualPath);
            if (asset.Id != ContentPath.ToAssetId(path))
            {
                throw new InvalidDataException($"Asset '{asset.Id}' does not match canonical path '{path}'.");
            }

            if (!ids.Add(asset.Id) || !paths.Add(path))
            {
                throw new InvalidDataException($"Duplicate or case-colliding runtime asset '{path}'.");
            }

            if (asset.Bytes < 0 || asset.Sha256.Length != 64 || !asset.Sha256.All(Uri.IsHexDigit))
            {
                throw new InvalidDataException($"Asset '{asset.Id}' has invalid size or SHA-256 metadata.");
            }

            if (string.IsNullOrWhiteSpace(asset.Type) || string.IsNullOrWhiteSpace(asset.Origin.SelectedSource))
            {
                throw new InvalidDataException($"Asset '{asset.Id}' has incomplete type or origin metadata.");
            }

            foreach (var artifact in asset.Artifacts)
            {
                ContentPath.Normalize(artifact.Path);
                if (artifact.Bytes < 0 || artifact.Sha256.Length != 64 || !artifact.Sha256.All(Uri.IsHexDigit))
                {
                    throw new InvalidDataException($"Asset '{asset.Id}' has invalid artifact metadata.");
                }
            }
        }
    }
}
