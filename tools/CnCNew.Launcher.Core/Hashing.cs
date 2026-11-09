using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace CnCNew.Launcher;

public static partial class Hashing
{
    private const int BufferSize = 64 * 1024;
    /// <summary>The SHA-256 a checksum file states for <paramref name="fileName"/>: the usual <c>hash  name</c> lines, or a file holding only the hash. Null when it states none.</summary>
    public static string? ParseExpected(string checksumText, string fileName)
    {
        string? only = null;
        foreach (var line in checksumText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = Line().Match(line);
            if (!match.Success) continue;
            var hash = match.Groups[1].Value.ToLowerInvariant();
            var name = match.Groups[2].Value.Trim().TrimStart('*');
            if (name.Length == 0) { only ??= hash; continue; }
            if (name.Equals(fileName, StringComparison.OrdinalIgnoreCase) || Path.GetFileName(name).Equals(fileName, StringComparison.OrdinalIgnoreCase)) return hash;
            only ??= hash;
        }
        return only;
    }

    public static async Task<string> Sha256FileAsync(string path, CancellationToken cancellation = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellation).ConfigureAwait(false)).ToLowerInvariant();
    }

    [GeneratedRegex(@"^([0-9a-fA-F]{64})(?:\s+(.*))?$")]
    private static partial Regex Line();
}
