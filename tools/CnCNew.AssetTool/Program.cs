using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CnCNew.Archives;
using CnCNew.IO;
using CnCNew.Import;
using CnCNew.Content;

return await AssetTool.RunAsync(args);

internal static class AssetTool
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            return args switch
            {
                ["list", var archive] => ListArchive(archive),
                ["extract", var archive, var destination] => await ExtractArchiveAsync(archive, destination),
                ["extract-all", var source, var destination] => await ExtractAllAsync(source, destination),
                ["inventory", var source, var output] => await InventoryAsync(source, output),
                ["materialize", var source, var destination] => await MaterializeAsync(source, destination),
                ["runtime-import", var source, var destination] => await RuntimeImportAsync(source, destination),
                ["verify", var content] => await VerifyAsync(content),
                ["audit-formats", var content] => AuditFormats(content),
                ["audit-dependencies", var content] => AuditDependencies(content),
                ["check-install", var game, .. var options] => await InstallCommands.RunAsync(() => InstallCommands.CheckInstallAsync(game, options)),
                ["describe-install", var game, var output, .. var options] => await InstallCommands.RunAsync(() => InstallCommands.DescribeInstallAsync(game, output, options)),
                ["install", var game, var target, .. var options] => await InstallCommands.RunAsync(() => InstallCommands.InstallAsync(game, target, options)),
                ["import-layer", var game, var content, .. var options] => await InstallCommands.RunAsync(() => InstallCommands.ImportLayerAsync(game, content, options)),
                ["check-export", var content, .. var options] => await InstallCommands.RunAsync(() => InstallCommands.CheckExportAsync(content, options)),
                ["uninstall", var target, .. var options] => await InstallCommands.RunAsync(() => Task.FromResult(InstallCommands.Uninstall(target, options))),
                _ => PrintUsage(),
            };
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 1;
        }
    }

    private static int ListArchive(string archivePath)
    {
        using var archive = BigArchive.Open(archivePath);
        foreach (var entry in archive.Entries)
        {
            Console.WriteLine($"{entry.Length,12}  {entry.Path}");
        }

        Console.WriteLine($"{archive.Entries.Count} entries");
        return 0;
    }

    private static async Task<int> ExtractArchiveAsync(string archivePath, string destination)
    {
        using var archive = BigArchive.Open(archivePath);
        await archive.ExtractToAsync(destination);
        Console.WriteLine($"Extracted {archive.Entries.Count} entries from {archivePath}");
        return 0;
    }

    private static async Task<int> ExtractAllAsync(string sourceDirectory, string destinationDirectory)
    {
        var sourceRoot = Path.GetFullPath(sourceDirectory);
        var destinationRoot = Path.GetFullPath(destinationDirectory);
        var archives = Directory.EnumerateFiles(sourceRoot, "*.big", SearchOption.AllDirectories)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var manifest = new List<ArchiveManifest>();
        foreach (var archivePath in archives)
        {
            var relativeArchive = Path.GetRelativePath(sourceRoot, archivePath).Replace('\\', '/');
            var relativeDestination = relativeArchive[..^Path.GetExtension(relativeArchive).Length];
            var archiveDestination = Path.Combine(destinationRoot, relativeDestination.Replace('/', Path.DirectorySeparatorChar));

            using var archive = BigArchive.Open(archivePath);
            await archive.ExtractToAsync(archiveDestination);
            manifest.Add(new ArchiveManifest(relativeArchive, archive.Entries.Count, archive.Entries.Sum(entry => (long)entry.Length)));
            Console.WriteLine($"{relativeArchive}: {archive.Entries.Count} entries");
        }

        Directory.CreateDirectory(destinationRoot);
        await File.WriteAllTextAsync(
            Path.Combine(destinationRoot, "archives.json"),
            JsonSerializer.Serialize(manifest, JsonOptions));
        Console.WriteLine($"Extracted {archives.Length} archives into {destinationRoot}");
        return 0;
    }

    private static async Task<int> InventoryAsync(string sourceDirectory, string outputPath)
    {
        var sourceRoot = Path.GetFullPath(sourceDirectory);
        var files = Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var inventory = new List<FileManifest>(files.Length);

        foreach (var path in files)
        {
            await using var stream = File.OpenRead(path);
            var hash = await SHA256.HashDataAsync(stream);
            inventory.Add(new FileManifest(
                Path.GetRelativePath(sourceRoot, path).Replace('\\', '/'),
                stream.Length,
                Convert.ToHexString(hash).ToLowerInvariant()));
        }

        var fullOutputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullOutputPath)!);
        await File.WriteAllTextAsync(fullOutputPath, JsonSerializer.Serialize(inventory, JsonOptions));
        Console.WriteLine($"Inventoried {files.Length} files into {fullOutputPath}");
        return 0;
    }

    private static async Task<int> MaterializeAsync(string sourceDirectory, string destinationDirectory)
    {
        using var fileSystem = GameFileSystem.OpenInstallation(sourceDirectory);
        var destinationRoot = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(destinationRoot);

        var virtualPaths = fileSystem.EnumerateFiles()
            .Where(path => !path.EndsWith(".big", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var manifest = new List<MaterializedFileManifest>(virtualPaths.Length);

        foreach (var virtualPath in virtualPaths)
        {
            var physicalRelativePath = ToSafePhysicalRelativePath(virtualPath);
            var destinationPath = Path.GetFullPath(physicalRelativePath, destinationRoot);
            var rootWithSeparator = destinationRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!destinationPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"Virtual path '{virtualPath}' escapes the content directory.");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await using var input = fileSystem.OpenRead(virtualPath);
            await using var output = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                1024 * 128,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var (bytes, sha256) = await CopyAndHashAsync(input, output);
            var candidates = fileSystem.GetCandidates(virtualPath)
                .Select(candidate => new CandidateManifest(candidate.Instance, candidate.Source, candidate.Length))
                .ToArray();
            manifest.Add(new MaterializedFileManifest(
                virtualPath,
                physicalRelativePath.Replace('\\', '/'),
                bytes,
                sha256,
                candidates[0].Source,
                candidates));
        }

        await File.WriteAllTextAsync(
            Path.Combine(destinationRoot, "content-manifest.json"),
            JsonSerializer.Serialize(manifest, JsonOptions));
        Console.WriteLine($"Materialized {manifest.Count} files into {destinationRoot}");
        return 0;
    }

    private static async Task<int> RuntimeImportAsync(string sourceDirectory, string destinationDirectory)
    {
        var lastReported = -1;
        var progress = new Progress<ImportProgress>(value =>
        {
            var percent = value.Total == 0 ? 100 : value.Processed * 100 / value.Total;
            if (percent != lastReported && percent % 5 == 0)
            {
                lastReported = percent;
                Console.WriteLine($"import: {percent,3}% ({value.Processed}/{value.Total}) {value.VirtualPath}");
            }
        });
        var result = await RuntimeContentImporter.ImportAsync(sourceDirectory, destinationDirectory, progress);
        Console.WriteLine($"Published schema {result.Manifest.SchemaVersion} with {result.Manifest.Assets.Count} assets; " +
                          $"cache hits {result.CacheHits}, written {result.WrittenFiles}.");
        foreach (var group in result.TypeCounts.OrderBy(value => value.Key, StringComparer.Ordinal))
            Console.WriteLine($"{group.Value,7}  {group.Key}");
        return 0;
    }

    private static async Task<int> VerifyAsync(string contentDirectory)
    {
        var content = ContentFileSystem.Open(contentDirectory);
        await content.VerifyIntegrityAsync();
        Console.WriteLine($"Verified {content.FileCount} runtime files against size and SHA-256 metadata.");
        return 0;
    }

    private static int AuditFormats(string contentDirectory)
    {
        var content = ContentFileSystem.Open(contentDirectory);
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var processed = 0;
        foreach (var path in content.EnumerateFiles())
        {
            using var stream = content.OpenRead(path);
            RuntimeContentImporter.ValidateAsset(path, stream);
            var type = CnCNew.Formats.AssetFormat.Classify(path).Type;
            counts[type] = counts.GetValueOrDefault(type) + 1;
            processed++;
        }
        foreach (var group in counts.OrderBy(value => value.Key, StringComparer.Ordinal))
            Console.WriteLine($"{group.Value,7}  {group.Key}");
        Console.WriteLine($"Validated {processed} materialized assets.");
        return 0;
    }

    private static int AuditDependencies(string contentDirectory)
    {
        var content = ContentFileSystem.Open(contentDirectory);
        var result = RuntimeContentImporter.AuditDependencies(content);
        Console.WriteLine($"Audited {result.AssetCount} assets: {result.Resolved} resolved references, {result.Fallbacks} explicit fallbacks.");
        foreach (var fallback in result.FallbackCounts.OrderBy(value => value.Key, StringComparer.Ordinal))
            Console.WriteLine($"{fallback.Value,7}  fallback:{fallback.Key}");
        return 0;
    }

    private static async Task<(long Bytes, string Sha256)> CopyAndHashAsync(Stream input, Stream output)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 128];
        long bytes = 0;
        int read;
        while ((read = await input.ReadAsync(buffer)) > 0)
        {
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read));
            bytes += read;
        }

        return (bytes, Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static string ToSafePhysicalRelativePath(string virtualPath)
        => string.Join(
            Path.DirectorySeparatorChar,
            virtualPath.Split('/').Select(EscapeWindowsSegment));

    private static string EscapeWindowsSegment(string segment)
    {
        var escaped = new StringBuilder(segment.Length);
        foreach (var character in segment)
        {
            if (character == '%' || character < 32 || character is '<' or '>' or ':' or '"' or '|' or '?' or '*')
            {
                foreach (var value in Encoding.UTF8.GetBytes([character]))
                {
                    escaped.Append('%').Append(value.ToString("X2"));
                }
            }
            else
            {
                escaped.Append(character);
            }
        }

        while (escaped.Length > 0 && escaped[^1] is '.' or ' ')
        {
            var trailing = escaped[^1];
            escaped.Length--;
            escaped.Append(trailing == '.' ? "%2E" : "%20");
        }

        var candidate = escaped.ToString();
        var baseName = candidate.Split('.')[0];
        if (baseName.Equals("CON", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("AUX", StringComparison.OrdinalIgnoreCase)
            || baseName.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || (baseName.Length == 4
                && (baseName.StartsWith("COM", StringComparison.OrdinalIgnoreCase)
                    || baseName.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))
                && baseName[3] is >= '1' and <= '9'))
        {
            candidate = "%" + ((byte)candidate[0]).ToString("X2") + candidate[1..];
        }

        return candidate;
    }

    private static int PrintUsage()
    {
        Console.Error.WriteLine("Usage:");
        Console.Error.WriteLine("  CnCNew.AssetTool list <archive.big>");
        Console.Error.WriteLine("  CnCNew.AssetTool extract <archive.big> <destination>");
        Console.Error.WriteLine("  CnCNew.AssetTool extract-all <game-directory> <destination>");
        Console.Error.WriteLine("  CnCNew.AssetTool inventory <directory> <manifest.json>");
        Console.Error.WriteLine("  CnCNew.AssetTool materialize <game-directory> <content-directory>");
        Console.Error.WriteLine("  CnCNew.AssetTool runtime-import <game-directory> <content-directory>");
        Console.Error.WriteLine("  CnCNew.AssetTool verify <content-directory>");
        Console.Error.WriteLine("  CnCNew.AssetTool audit-formats <content-directory>");
        Console.Error.WriteLine("  CnCNew.AssetTool audit-dependencies <content-directory>");
        InstallCommands.PrintUsage();
        return 2;
    }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private sealed record ArchiveManifest(string Path, int Entries, long ExpandedBytes);
    private sealed record FileManifest(string Path, long Bytes, string Sha256);
    private sealed record CandidateManifest(int Instance, string Source, long Bytes);
    private sealed record MaterializedFileManifest(
        string VirtualPath,
        string PhysicalPath,
        long Bytes,
        string Sha256,
        string SelectedSource,
        IReadOnlyList<CandidateManifest> Candidates);
}

