namespace CnCNew.Content;

public static class ContentPath
{
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Path.IsPathRooted(path))
        {
            throw new InvalidDataException($"Content path '{path}' must be relative.");
        }

        var segments = path.Replace('\\', '/').TrimStart('/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Any(segment => segment is "." or ".."))
        {
            throw new InvalidDataException($"Invalid virtual content path '{path}'.");
        }

        return string.Join('/', segments);
    }

    public static string ToAssetId(string virtualPath)
        => "asset://" + Normalize(virtualPath).ToLowerInvariant();
}
