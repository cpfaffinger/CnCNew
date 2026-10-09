using System.Diagnostics;
using CnCNew.Import;

/// <summary>
/// The patcher commands: thin drivers over <see cref="InstallationInspector"/> and <see cref="GameInstaller"/>.
/// Exit codes: 0 done / identical, 1 the game directory is not the master, 2 usage, refused target or I/O error.
/// </summary>
internal static class InstallCommands
{
    public const int Different = 1;
    public const int Failed = 2;

    public static async Task<int> RunAsync(Func<Task<int>> command)
    {
        try
        {
            return await command();
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return Failed;
        }
    }

    public static async Task<int> CheckInstallAsync(string gameDirectory, string[] options)
    {
        if (!TryParse(options, ["--master"], [], out var values, out _)) return Usage();
        var master = LoadMaster(values);
        var watch = Stopwatch.StartNew();
        var result = await InstallationInspector.CheckAsync(gameDirectory, master, new StepProgress<InstallationCheckProgress>("check", value => (value.ProcessedFiles, value.TotalFiles)));
        Console.Write(result.FormatReport());
        Console.WriteLine($"Checked in {watch.Elapsed.TotalSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)} s.");
        return result.IsIdentical ? 0 : Different;
    }

    public static async Task<int> DescribeInstallAsync(string gameDirectory, string outputPath, string[] options)
    {
        if (!TryParse(options, ["--name", "--id"], [], out var values, out _)) return Usage();
        var master = await InstallationInspector.DescribeAsync(
            gameDirectory,
            values.GetValueOrDefault("--name"),
            values.GetValueOrDefault("--id"),
            new StepProgress<InstallationCheckProgress>("hash", value => (value.ProcessedFiles, value.TotalFiles)),
            new StepProgress<ImportProgress>("export (dry run)", value => (value.Processed, value.Total)));
        var output = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        await File.WriteAllBytesAsync(output, InstallationMasterSerializer.Write(master));
        Console.WriteLine($"Described \"{master.Name}\" ({master.Id}, Steam app {master.SteamAppId}): {master.FileCount} files, {master.TotalBytes} bytes.");
        Console.WriteLine($"Expected export: {master.ExpectedExport.RuntimeFileCount} runtime files, content manifest {master.ExpectedExport.ContentManifestBytes} bytes, " +
                          $"SHA-256 {master.ExpectedExport.ContentManifestSha256} ({master.ExpectedExport.Importer} {master.ExpectedExport.ImporterVersion}).");
        Console.WriteLine($"Wrote {output}");
        return 0;
    }

    public static async Task<int> InstallAsync(string gameDirectory, string targetDirectory, string[] options)
    {
        if (!TryParse(options, ["--master"], [], out var values, out _)) return Usage();
        var master = LoadMaster(values);
        var watch = Stopwatch.StartNew();
        var result = await GameInstaller.InstallAsync(gameDirectory, targetDirectory, master, InstallSteps());
        return Report(result, watch);
    }

    public static async Task<int> CheckExportAsync(string contentDirectory, string[] options)
    {
        if (!TryParse(options, ["--master"], [], out var values, out _)) return Usage();
        var master = LoadMaster(values);
        var problems = await GameInstaller.VerifyExportAsync(contentDirectory, master, InstallSteps());
        if (problems.Count == 0)
        {
            Console.WriteLine($"The content is the known export of \"{master.Name}\": {master.ExpectedExport.RuntimeFileCount} runtime files, " +
                              $"content manifest SHA-256 {master.ExpectedExport.ContentManifestSha256}.");
            return 0;
        }
        Console.WriteLine($"The content is NOT the known export of \"{master.Name}\": {problems.Count} problem(s).");
        foreach (var problem in problems) Console.WriteLine($"  {problem}");
        return Different;
    }

    public static int Uninstall(string targetDirectory, string[] options)
    {
        if (!TryParse(options, [], ["--yes"], out _, out var flags)) return Usage();
        if (!flags.Contains("--yes"))
        {
            Console.Error.WriteLine("error: uninstall removes files and asks nothing; pass --yes to confirm.");
            return Failed;
        }
        var result = GameInstaller.Uninstall(targetDirectory);
        Console.WriteLine(result.Message);
        foreach (var leftover in result.Leftovers) Console.WriteLine($"  kept: {leftover}");
        return result.Removed ? 0 : Failed;
    }

    public static void PrintUsage()
    {
        Console.Error.WriteLine("  CnCNew.AssetTool check-install <game-directory> [--master <steam-master.json>]");
        Console.Error.WriteLine("  CnCNew.AssetTool describe-install <game-directory> <out.json> [--name <name>] [--id <id>]");
        Console.Error.WriteLine("  CnCNew.AssetTool install <game-directory> <target-directory> [--master <steam-master.json>]");
        Console.Error.WriteLine("  CnCNew.AssetTool check-export <content-directory> [--master <steam-master.json>]");
        Console.Error.WriteLine("  CnCNew.AssetTool uninstall <target-directory> --yes");
    }

    private static int Report(InstallResult result, Stopwatch watch)
    {
        Console.WriteLine($"{result.Outcome}: {result.Message.TrimEnd()}");
        foreach (var path in result.MissingRuntimeFiles) Console.WriteLine($"  restored (was missing): {path}");
        foreach (var path in result.ChangedRuntimeFiles) Console.WriteLine($"  restored (was changed): {path}");
        foreach (var path in result.ExtraRuntimeFiles) Console.WriteLine($"  removed (not part of the export): {path}");
        if (result.Record is { } record)
            Console.WriteLine($"Install record: master {record.MasterId}, product {record.ProductVersion} ({record.ProductCommit}), verified {(record.Verified ? "true" : "false")}.");
        Console.WriteLine($"Finished in {watch.Elapsed.TotalSeconds.ToString("F1", System.Globalization.CultureInfo.InvariantCulture)} s.");
        return result.Outcome switch
        {
            InstallOutcome.InstallationDiffers => Different,
            InstallOutcome.TargetRefused => Failed,
            _ => 0,
        };
    }

    private static InstallationMaster LoadMaster(IReadOnlyDictionary<string, string> values)
    {
        if (values.TryGetValue("--master", out var path))
        {
            var loaded = InstallationMaster.Load(path);
            Console.WriteLine($"Master: {loaded.Id} from {path}");
            return loaded;
        }
        var builtIn = InstallationMaster.LoadBuiltIn();
        Console.WriteLine($"Master: {builtIn.Id} (built in from baselines/steam-master.json)");
        return builtIn;
    }

    private static bool TryParse(string[] options, string[] valueOptions, string[] flagOptions, out Dictionary<string, string> values, out HashSet<string> flags)
    {
        values = new Dictionary<string, string>(StringComparer.Ordinal);
        flags = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < options.Length; index++)
        {
            if (flagOptions.Contains(options[index]) && flags.Add(options[index])) continue;
            if (valueOptions.Contains(options[index]) && index + 1 < options.Length && values.TryAdd(options[index], options[index + 1])) { index++; continue; }
            Console.Error.WriteLine($"error: unknown, repeated or incomplete option '{options[index]}'.");
            return false;
        }
        return true;
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Usage:");
        PrintUsage();
        return Failed;
    }

    private static StepProgress<InstallProgress> InstallSteps() => new(null, value => (value.Processed, value.Total), value => value.Step);

    // Prints a line per step every ten percent; reports can arrive from several threads, so the state is locked.
    private sealed class StepProgress<T>(string? fixedStep, Func<T, (int Processed, int Total)> read, Func<T, string>? step = null) : IProgress<T>
    {
        private readonly object _gate = new();
        private string _step = string.Empty;
        private int _lastTenth = -1;

        public void Report(T value)
        {
            var name = fixedStep ?? step!(value);
            var (processed, total) = read(value);
            var tenth = total == 0 ? 10 : (int)((long)processed * 10 / total);
            lock (_gate)
            {
                if (name != _step) { _step = name; _lastTenth = -1; }
                if (tenth <= _lastTenth) return;
                _lastTenth = tenth;
                Console.WriteLine($"{name}: {tenth * 10,3}% ({processed}/{total})");
            }
        }
    }
}
