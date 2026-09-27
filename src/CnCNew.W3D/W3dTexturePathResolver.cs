namespace CnCNew.W3D;

public sealed record W3dTextureResolution(string Reference, string? VirtualPath, IReadOnlyList<string> Candidates)
{
    public bool Found => VirtualPath is not null;

    public string Diagnostic => Found
        ? $"W3D texture '{Reference}' resolved to '{VirtualPath}'."
        : $"W3D texture '{Reference}' was not found; tried: {string.Join(", ", Candidates)}.";
}

public static class W3dTexturePathResolver
{
    public static W3dTextureResolution Resolve(string reference, Func<string, bool> exists)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentNullException.ThrowIfNull(exists);

        var normalized = reference.Replace('\\', '/').TrimStart('/');
        if (normalized.Split('/').Any(segment => segment is "." or ".."))
        {
            throw new InvalidDataException($"Invalid W3D texture reference '{reference}'.");
        }

        var fileName = Path.GetFileName(normalized);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var candidates = new[]
        {
            normalized,
            $"Art/Textures/{fileName}",
            $"Art/Textures/{baseName}.dds",
            $"Art/Textures/{baseName}.tga",
        }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return new W3dTextureResolution(reference, candidates.FirstOrDefault(exists), candidates);
    }
}

public sealed class W3dTexturePathCache(Func<string, bool> exists)
{
    private readonly Dictionary<string, W3dTextureResolution> _entries = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _entries.Count;

    public W3dTextureResolution Resolve(string reference)
    {
        if (!_entries.TryGetValue(reference, out var result))
        {
            result = W3dTexturePathResolver.Resolve(reference, exists);
            _entries.Add(reference, result);
        }

        return result;
    }

    public void Clear() => _entries.Clear();
}
