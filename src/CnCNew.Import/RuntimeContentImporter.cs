using System.Security.Cryptography;
using System.Text;
using CnCNew.Content;
using CnCNew.Data.Ini;
using CnCNew.Formats;
using CnCNew.Images;
using CnCNew.IO;
using CnCNew.W3D;

namespace CnCNew.Import;

public sealed record ImportProgress(int Processed, int Total, string VirtualPath);
public sealed record ImportResult(RuntimeContentManifest Manifest, int CacheHits, int WrittenFiles, IReadOnlyDictionary<string, int> TypeCounts);
public sealed record DependencyAuditResult(int AssetCount, int Resolved, int Fallbacks, IReadOnlyDictionary<string, int> FallbackCounts);

public static class RuntimeContentImporter
{
    public const string ImporterName = "CnCNew.RuntimeContentImporter";
    public const string ImporterVersion = "2.0.0";

    public static IReadOnlyList<string> ValidateAsset(string virtualPath, Stream stream)
    {
        var format = AssetFormat.Classify(virtualPath);
        if (format.Strategy == "unclassified")
            throw new InvalidDataException($"{virtualPath}: unclassified extension '{Path.GetExtension(virtualPath)}'.");
        return ValidateAndReadReferences(virtualPath, stream, format);
    }

    public static DependencyAuditResult AuditDependencies(ContentFileSystem content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var paths = content.EnumerateFiles().ToArray();
        var byPath = paths.ToDictionary(path => path, StringComparer.OrdinalIgnoreCase);
        var byName = paths.GroupBy(path => Path.GetFileName(path)!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var graph = paths.ToDictionary(path => path, _ => new List<string>(), StringComparer.OrdinalIgnoreCase);
        var fallbackCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var resolved = 0;
        foreach (var path in paths)
        {
            using var stream = content.OpenRead(path);
            var references = ValidateAsset(path, stream);
            var type = AssetFormat.Classify(path).Type;
            foreach (var reference in references)
            {
                var target = ResolveReferencePath(path, reference, byPath, byName);
                if (target is not null)
                {
                    if (!target.Equals(path, StringComparison.OrdinalIgnoreCase))
                    {
                        graph[path].Add(target);
                        resolved++;
                    }
                    continue;
                }
                var fallback = GetFallback(type, reference);
                if (fallback is null) throw new InvalidDataException($"{path}: unresolved runtime reference '{reference}'.");
                fallbackCounts[fallback] = fallbackCounts.GetValueOrDefault(fallback) + 1;
            }
        }
        ValidateAcyclic(graph);
        return new DependencyAuditResult(paths.Length, resolved, fallbackCounts.Values.Sum(), fallbackCounts);
    }

    public static async Task<ImportResult> ImportAsync(
        string sourceDirectory,
        string destinationDirectory,
        IProgress<ImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var destination = Path.GetFullPath(destinationDirectory);
        var parent = Path.GetDirectoryName(destination) ?? throw new InvalidDataException("Import destination has no parent.");
        Directory.CreateDirectory(parent);
        var staging = Path.Combine(parent, $".{Path.GetFileName(destination)}.staging-{Guid.NewGuid():N}");
        Directory.CreateDirectory(staging);
        try
        {
            using var source = GameFileSystem.OpenInstallation(sourceDirectory);
            var virtualPaths = source.EnumerateFiles().Where(path => !path.EndsWith(".big", StringComparison.OrdinalIgnoreCase)).ToArray();
            EnsureNoCaseCollisions(virtualPaths);
            var old = TryReadManifest(destination);
            var oldById = old?.Assets.ToDictionary(asset => asset.Id, StringComparer.Ordinal);
            var drafts = new List<Draft>(virtualPaths.Length);
            var cacheHits = 0;
            var written = 0;

            for (var index = 0; index < virtualPaths.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = ContentPath.Normalize(virtualPaths[index]);
                progress?.Report(new ImportProgress(index, virtualPaths.Length, path));
                await using var input = source.OpenRead(path);
                var (bytes, hash) = await HashAsync(input, cancellationToken);
                input.Position = 0;
                var format = AssetFormat.Classify(path);
                if (format.Strategy == "unclassified")
                    throw new InvalidDataException($"{path}: unclassified extension '{Path.GetExtension(path)}'.");

                var references = ValidateAndReadReferences(path, input, format);
                var relative = ToSafeRelativePath(path);
                var output = ResolveContained(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(output)!);
                var id = ContentPath.ToAssetId(path);
                if (oldById is not null && oldById.TryGetValue(id, out var previous) &&
                    previous.Sha256.Equals(hash, StringComparison.OrdinalIgnoreCase))
                {
                    var previousArtifact = previous.Artifacts.Single();
                    var previousPath = ResolveContained(destination, previousArtifact.Path);
                    if (File.Exists(previousPath) && new FileInfo(previousPath).Length == bytes)
                    {
                        File.Copy(previousPath, output);
                        cacheHits++;
                    }
                    else await CopySourceAsync(source, path, output, cancellationToken);
                }
                else await CopySourceAsync(source, path, output, cancellationToken);
                written++;

                var candidates = source.GetCandidates(path).Select(value => new RuntimeCandidate(value.Instance, value.Source, value.Length)).ToArray();
                drafts.Add(new Draft(path, id, format, bytes, hash, references, candidates));
            }

            var manifest = BuildManifest(drafts);
            await File.WriteAllBytesAsync(Path.Combine(staging, "content-manifest.json"), RuntimeManifestSerializer.WriteCanonical(manifest), cancellationToken);
            Publish(staging, destination);
            staging = string.Empty;
            var counts = manifest.Assets.GroupBy(asset => asset.Type).ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            return new ImportResult(manifest, cacheHits, written, counts);
        }
        finally
        {
            if (staging.Length != 0 && Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static IReadOnlyList<string> ValidateAndReadReferences(string path, Stream stream, AssetFormatInfo format)
    {
        try
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".ini":
                    using (var reader = new StreamReader(stream, Encoding.Latin1, false, leaveOpen: true))
                    {
                        var text = reader.ReadToEnd();
                        stream.Position = 0;
                        SageIniParser.Parse(text, path);
                        return LegacyAssetReaders.ExtractReferences(text);
                    }
                case ".str": LegacyAssetReaders.ReadStr(stream); break;
                case ".wnd": return LegacyAssetReaders.ReadWnd(stream).References;
                case ".map": LegacyAssetReaders.ValidateSageMap(stream); break;
                case ".scb": LegacyAssetReaders.ValidateScriptBinary(stream); break;
                case ".wak": LegacyAssetReaders.ValidateWak(stream); break;
                case ".dds": LegacyAssetReaders.ReadDds(stream); break;
                case ".tga": TgaReader.Read(stream); break;
                case ".ani": LegacyAssetReaders.ReadRiff(stream, "ACON"); break;
                case ".wav": LegacyAssetReaders.ReadRiff(stream, "WAVE"); break;
                case ".mp3": LegacyAssetReaders.ValidateMp3(stream); break;
                case ".bik": LegacyAssetReaders.ReadBink(stream); break;
                case ".w3d":
                    var read = W3dFileReader.Read(stream);
                    return W3dMeshDecoder.Decode(read).SelectMany(mesh => mesh.MaterialVariants)
                        .SelectMany(variant => variant.Materials.Textures).Select(texture => texture.Name)
                        .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            }
            return [];
        }
        catch (Exception error) when (error is InvalidDataException or EndOfStreamException or OverflowException)
        {
            throw new InvalidDataException($"{path}: {format.Type} validation failed: {error.Message}", error);
        }
    }

    private static RuntimeContentManifest BuildManifest(IReadOnlyList<Draft> drafts)
    {
        var byPath = drafts.ToDictionary(value => value.Path, StringComparer.OrdinalIgnoreCase);
        var byName = drafts.GroupBy(value => Path.GetFileName(value.Path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var assets = new List<RuntimeAsset>(drafts.Count);
        foreach (var draft in drafts)
        {
            var dependencies = new List<RuntimeDependency>();
            foreach (var reference in draft.References)
            {
                var resolution = ResolveReference(draft.Path, reference, byPath, byName);
                if (resolution is not null)
                {
                    if (resolution.Id != draft.Id)
                        dependencies.Add(new RuntimeDependency(resolution.Id, "direct", "resolved", null));
                    continue;
                }
                var fallback = GetFallback(draft.Format.Type, reference);
                if (fallback is null)
                    throw new InvalidDataException($"{draft.Path}: unresolved runtime reference '{reference}'.");
                dependencies.Add(new RuntimeDependency(ContentPath.ToAssetId(reference), "direct", "fallback", fallback));
            }

            var diagnostics = draft.Format.ProductScope ? Array.Empty<RuntimeDiagnostic>() :
                new[] { new RuntimeDiagnostic("info", "out-of-product-scope", $"Classified as {draft.Format.Type}; retained only for provenance.") };
            assets.Add(new RuntimeAsset(
                draft.Id, draft.Path, draft.Format.Type, draft.Hash, draft.Bytes,
                new RuntimeOrigin(draft.Candidates[0].Source, 0, draft.Candidates),
                dependencies.Distinct().ToArray(),
                [new RuntimeArtifact(ToSafeRelativePath(draft.Path).Replace('\\', '/'), draft.Hash, draft.Bytes)], diagnostics));
        }
        return RuntimeManifestSerializer.Canonicalize(new RuntimeContentManifest(
            RuntimeContentManifest.CurrentSchemaVersion,
            new RuntimeImporter(ImporterName, ImporterVersion, new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["audio"] = "copy-original; external free decoder",
                ["bink"] = "copy-original; FFmpeg/libav external decoder fallback",
                ["caseSemantics"] = "ordinal-ignore-case",
                ["hash"] = "sha256",
            }), assets));
    }

    private static Draft? ResolveReference(string owner, string reference, IReadOnlyDictionary<string, Draft> byPath, IReadOnlyDictionary<string, Draft[]> byName)
    {
        var normalized = reference.Replace('\\', '/').TrimStart('/');
        var candidates = new List<string> { normalized };
        if (!normalized.Contains('/'))
        {
            candidates.Add("Art/Textures/" + normalized);
            var stem = Path.GetFileNameWithoutExtension(normalized);
            if (!Path.HasExtension(normalized))
            {
                candidates.Add("Art/Textures/" + normalized + ".dds");
                candidates.Add("Art/Textures/" + normalized + ".tga");
            }
            else
            {
                candidates.Add("Art/Textures/" + stem + ".dds");
                candidates.Add("Art/Textures/" + stem + ".tga");
            }
            var ownerDirectory = Path.GetDirectoryName(owner)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(ownerDirectory)) candidates.Add(ownerDirectory + "/" + normalized);
        }
        foreach (var candidate in candidates)
            if (byPath.TryGetValue(candidate, out var match)) return match;
        if (byName.TryGetValue(Path.GetFileName(normalized), out var names) && names.Length == 1) return names[0];
        return null;
    }

    private static string? ResolveReferencePath(string owner, string reference, IReadOnlyDictionary<string, string> byPath, IReadOnlyDictionary<string, string[]> byName)
    {
        var normalized = reference.Replace('\\', '/').TrimStart('/');
        var candidates = new List<string> { normalized };
        if (!normalized.Contains('/'))
        {
            candidates.Add("Art/Textures/" + normalized);
            var stem = Path.GetFileNameWithoutExtension(normalized);
            if (!Path.HasExtension(normalized)) { candidates.Add("Art/Textures/" + normalized + ".dds"); candidates.Add("Art/Textures/" + normalized + ".tga"); }
            else { candidates.Add("Art/Textures/" + stem + ".dds"); candidates.Add("Art/Textures/" + stem + ".tga"); }
            var directory = Path.GetDirectoryName(owner)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(directory)) candidates.Add(directory + "/" + normalized);
        }
        foreach (var candidate in candidates) if (byPath.TryGetValue(candidate, out var target)) return target;
        return byName.TryGetValue(Path.GetFileName(normalized), out var names) && names.Length == 1 ? names[0] : null;
    }

    private static string? GetFallback(string ownerType, string reference)
    {
        if (ownerType == "westwood/w3d") return "visible-magenta-texture";
        return Path.GetExtension(reference).ToLowerInvariant() switch
        {
            ".dds" or ".tga" or ".jpg" or ".jpeg" or ".bmp" => "visible-magenta-texture",
            ".wav" or ".mp3" => "silent-audio",
            ".bik" => "skip-video",
            ".map" => "disabled-missing-map",
            _ => null,
        };
    }

    private static void ValidateAcyclic(IReadOnlyDictionary<string, List<string>> graph)
    {
        var states = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        foreach (var node in graph.Keys) Visit(node);
        void Visit(string node)
        {
            if (states.TryGetValue(node, out var state))
            {
                if (state == 1) throw new InvalidDataException($"Runtime dependency cycle: {string.Join(" -> ", stack.Reverse())} -> {node}.");
                return;
            }
            states[node] = 1; stack.Push(node);
            foreach (var target in graph[node]) Visit(target);
            stack.Pop(); states[node] = 2;
        }
    }

    private static RuntimeContentManifest? TryReadManifest(string destination)
    {
        var path = Path.Combine(destination, "content-manifest.json");
        if (!File.Exists(path)) return null;
        try { using var stream = File.OpenRead(path); return RuntimeManifestSerializer.Read(stream); }
        catch (InvalidDataException) { return null; }
        catch (System.Text.Json.JsonException) { return null; }
    }

    private static void Publish(string staging, string destination)
    {
        if (!Directory.Exists(destination)) { Directory.Move(staging, destination); return; }
        var backup = destination + ".previous-" + Guid.NewGuid().ToString("N");
        Directory.Move(destination, backup);
        try { Directory.Move(staging, destination); }
        catch { Directory.Move(backup, destination); throw; }
        Directory.Delete(backup, recursive: true);
    }

    private static async Task<(long Bytes, string Hash)> HashAsync(Stream stream, CancellationToken cancellationToken)
    {
        var hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return (stream.Length, Convert.ToHexString(hash).ToLowerInvariant());
    }
    private static async Task CopySourceAsync(GameFileSystem source, string path, string output, CancellationToken cancellationToken)
    {
        await using var input = source.OpenRead(path);
        await using var target = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None, 131072, true);
        await input.CopyToAsync(target, cancellationToken);
    }
    private static void EnsureNoCaseCollisions(IEnumerable<string> paths)
    {
        var exact = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
            if (exact.TryGetValue(path, out var previous) && previous != path)
                throw new InvalidDataException($"Case collision between '{previous}' and '{path}'.");
            else exact[path] = path;
    }
    private static string ToSafeRelativePath(string path)
        => string.Join(Path.DirectorySeparatorChar, ContentPath.Normalize(path).Split('/').Select(EscapeSegment));
    private static string EscapeSegment(string segment)
    {
        var builder = new StringBuilder();
        foreach (var character in segment)
            if (character == '%' || character < 32 || character is '<' or '>' or ':' or '"' or '|' or '?' or '*')
                foreach (var value in Encoding.UTF8.GetBytes([character])) builder.Append('%').Append(value.ToString("X2"));
            else builder.Append(character);
        while (builder.Length > 0 && builder[^1] is '.' or ' ') { var c = builder[^1]; builder.Length--; builder.Append(c == '.' ? "%2E" : "%20"); }
        var result = builder.ToString();
        var baseName = result.Split('.')[0];
        if (baseName.Equals("CON", StringComparison.OrdinalIgnoreCase) || baseName.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            baseName.Equals("AUX", StringComparison.OrdinalIgnoreCase) || baseName.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            (baseName.Length == 4 && (baseName.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || baseName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && baseName[3] is >= '1' and <= '9'))
            result = "%" + ((byte)result[0]).ToString("X2") + result[1..];
        return result;
    }
    private static string ResolveContained(string root, string relative)
    {
        var resolved = Path.GetFullPath(relative.Replace('/', Path.DirectorySeparatorChar), root);
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"Path '{relative}' escapes import root.");
        return resolved;
    }
    private sealed record Draft(string Path, string Id, AssetFormatInfo Format, long Bytes, string Hash, IReadOnlyList<string> References, RuntimeCandidate[] Candidates);
}
