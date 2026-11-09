using System.Diagnostics;

namespace CnCNew.Launcher;

public enum StepId { Checks, Folder, Download, MakeGame, Verify, Play }

public enum StepState { Pending, Running, Done, Failed, Skipped }

/// <summary>A change of what the launcher is doing: which step, in which state, the sentence for the headline and a fraction for the bar when there is one.</summary>
public sealed record EngineProgress(StepId Step, StepState State, string Headline, string Detail = "", double? Fraction = null);

public sealed record EngineResult(bool Succeeded, string Message, StepId? FailedStep = null, bool Cancelled = false)
{
    public static EngineResult Ok(string message = "") => new(true, message);
    public static EngineResult Fail(StepId step, string message) => new(false, message, step);
}

/// <summary>
/// The work of the launcher without a window: download the release, check its hash, unpack it, let the packaged patcher make the game from the Steam copy,
/// optionally check the simulation, repair, update and remove. Every long step reports through <see cref="Progress"/>; every failure is a message in plain words.
/// Network, processes and the clock are injected, so the whole flow runs in tests against a fake server and a fake patcher.
/// </summary>
public sealed class LauncherEngine(HttpClient http, IToolRunner tools, string dataDirectory, ReleaseClient releases, Func<InstallLayout, bool>? gameIsRunning = null, Action<string>? log = null)
{
    private readonly Downloader _downloader = new(http);
    private readonly Func<InstallLayout, bool> _gameIsRunning = gameIsRunning ?? DefaultGameIsRunning;

    public event Action<EngineProgress>? Progress;

    public string DownloadDirectory => Path.Combine(dataDirectory, "downloads");

    private void Report(StepId step, StepState state, string headline, string detail = "", double? fraction = null)
    {
        log?.Invoke($"{step} {state}: {headline} {detail}".TrimEnd());
        Progress?.Invoke(new EngineProgress(step, state, headline, detail, fraction));
    }

    public Task<ReleaseCheck> CheckLatestAsync(CancellationToken cancellation = default) => releases.GetLatestAsync(cancellation);

    /// <summary>The first install, or an update: fetch the release's client package, replace the client, then let the patcher make (or restore) the game content.</summary>
    public async Task<EngineResult> InstallAsync(string folder, ReleaseInfo release, bool verifySimulation, CancellationToken cancellation = default)
    {
        var layout = new InstallLayout(folder);
        try
        {
            var packaged = await InstallPackageAsync(layout, release, cancellation).ConfigureAwait(false);
            if (!packaged.Succeeded) return packaged;
            var made = await MakeGameAsync(layout, cancellation).ConfigureAwait(false);
            if (!made.Succeeded) return made;
            await VerifyAsync(layout, verifySimulation, cancellation).ConfigureAwait(false);
            Report(StepId.Play, StepState.Done, "Ready to play", $"Version {release.Version} is installed.");
            return EngineResult.Ok($"Version {release.Version} is installed.");
        }
        catch (OperationCanceledException)
        {
            return new EngineResult(false, "Cancelled. Nothing is broken; start again when you like.", Cancelled: true);
        }
    }

    /// <summary>Download, check the hash, unpack and replace the client. The game content is not touched.</summary>
    public async Task<EngineResult> InstallPackageAsync(InstallLayout layout, ReleaseInfo release, CancellationToken cancellation = default)
    {
        var package = release.ClientPackage;
        if (package is null) return EngineResult.Fail(StepId.Download, $"Version {release.Version} has no game package for Windows yet. Look again later.");
        if (release.ClientHash is not { } hashAsset)
            return EngineResult.Fail(StepId.Download, $"Version {release.Version} has no checksum file, so the download cannot be verified. The launcher does not install what it cannot verify.");
        if (_gameIsRunning(layout)) return EngineResult.Fail(StepId.Download, "The game is running. Close it and try again.");

        var archive = Path.Combine(DownloadDirectory, package.Name);
        var staging = Path.Combine(layout.Root, ".staging");
        try
        {
            Report(StepId.Download, StepState.Running, "Downloading the game package", Size(package.Size), 0);
            Directory.CreateDirectory(layout.Root);
            var transfer = new Progress<TransferProgress>(value =>
                Report(StepId.Download, StepState.Running, "Downloading the game package", $"{Size(value.Bytes)} of {Size(value.Total ?? package.Size)}", value.Fraction));
            await _downloader.DownloadAsync(package.DownloadUrl, archive, transfer, cancellation).ConfigureAwait(false);

            Report(StepId.Download, StepState.Running, "Checking the download", "Comparing its checksum with the one published with the release.");
            var expected = Hashing.ParseExpected(await _downloader.ReadTextAsync(hashAsset.DownloadUrl, cancellation).ConfigureAwait(false), package.Name);
            if (expected is null) return EngineResult.Fail(StepId.Download, "The release's checksum file could not be read. The package was not installed.");
            var actual = await Hashing.Sha256FileAsync(archive, cancellation).ConfigureAwait(false);
            if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
            {
                TryDelete(archive);
                return EngineResult.Fail(StepId.Download, "The downloaded package does not match its published checksum: it is damaged or was altered on the way. It was deleted and not installed. Try again.");
            }

            Report(StepId.Download, StepState.Running, "Installing the program", "Unpacking the files.");
            await Task.Run(() => PackageInstaller.Unpack(archive, staging, new Progress<double>(value => Report(StepId.Download, StepState.Running, "Installing the program", "Unpacking the files.", value)), cancellation), cancellation).ConfigureAwait(false);
            await Task.Run(() => PackageInstaller.Apply(staging, layout, new ReleaseRecord(release.Version.ToString(), release.Tag, DateTimeOffset.UtcNow)), cancellation).ConfigureAwait(false);
            TryDelete(archive);
            Report(StepId.Download, StepState.Done, "Program installed", $"Version {release.Version}.");
            return EngineResult.Ok();
        }
        catch (DownloadException exception) { return EngineResult.Fail(StepId.Download, exception.Message); }
        catch (PackageException exception) { return EngineResult.Fail(StepId.Download, exception.Message); }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); } catch (IOException) { }
        }
    }

    /// <summary>Lets the packaged patcher make the game from the Steam copy (or check and restore what is there). It only reads Steam's files.</summary>
    public async Task<EngineResult> MakeGameAsync(InstallLayout layout, CancellationToken cancellation = default)
    {
        Report(StepId.MakeGame, StepState.Running, "Making the game from your Steam copy", "Looking for the game.", 0);
        var result = await tools.RunAsync(layout.PatcherExe, ["install", "auto", layout.GameDirectory], layout.Root, line =>
        {
            var parsed = PatcherOutput.Parse(line);
            if (parsed.Kind == PatcherEventKind.Progress)
                Report(StepId.MakeGame, StepState.Running, parsed.Text, $"{(int)(parsed.Fraction * 100)} %", parsed.Fraction);
            else if (parsed.Text.Length > 0)
                Report(StepId.MakeGame, StepState.Running, "Making the game from your Steam copy", parsed.Text);
        }, cancellation).ConfigureAwait(false);
        if (result.Cancelled) throw new OperationCanceledException(cancellation);
        if (!result.Succeeded) return EngineResult.Fail(StepId.MakeGame, PatcherOutput.Explain(result));
        if (layout.Inspect() != InstallState.Ready) return EngineResult.Fail(StepId.MakeGame, "The patcher finished, but the game files are not where they belong. See the log.");
        Report(StepId.MakeGame, StepState.Done, "Game made", "Every file was checked.");
        return EngineResult.Ok();
    }

    /// <summary>The optional comparison with the reference values. A difference is a finding that is shown, never a failure of the install.</summary>
    public async Task<EngineResult> VerifyAsync(InstallLayout layout, bool requested, CancellationToken cancellation = default)
    {
        if (!requested) { Report(StepId.Verify, StepState.Skipped, "Simulation check", "Off. You can switch it on in the settings."); return EngineResult.Ok(); }
        if (!layout.HasSimulationCheck)
        {
            Report(StepId.Verify, StepState.Skipped, "Simulation check", "This package does not include the check tool, so the option was ignored.");
            return EngineResult.Ok();
        }
        var simulationTool = layout.SimulationCheckExe;
        var baselines = layout.SimulationCheckBaselines;
        var steps = new (string Name, string[] Arguments)[]
        {
            ("the game data", ["input-manifest", "check", layout.ContentDirectory, Path.Combine(baselines, "input-manifest.json")]),
            ("nine reference matches", ["reference-manifest", "check", layout.ContentDirectory, Path.Combine(baselines, "reference-manifest.json"), "--set", "quick"]),
        };
        var differences = new List<string>();
        for (var index = 0; index < steps.Length; index++)
        {
            Report(StepId.Verify, StepState.Running, "Checking the simulation", $"Comparing {steps[index].Name} with the reference values.", (double)index / steps.Length);
            var result = await tools.RunAsync(simulationTool, steps[index].Arguments, layout.Root, _ => { }, cancellation).ConfigureAwait(false);
            if (result.Cancelled) throw new OperationCanceledException(cancellation);
            if (!result.Succeeded) differences.Add(steps[index].Name);
        }
        if (differences.Count == 0) Report(StepId.Verify, StepState.Done, "Simulation checked", "Everything matches the reference values of the project.");
        else Report(StepId.Verify, StepState.Done, "Simulation differs", $"Differences in {string.Join(" and ", differences)}. The game still works; this machine computes differently from the reference machine, and may not be able to play online with others.");
        return EngineResult.Ok();
    }

    /// <summary>Checks every file of the client against the list written at install time, fetches the package again if one is damaged, and lets the patcher check the game files.</summary>
    public async Task<EngineResult> RepairAsync(string folder, CancellationToken cancellation = default)
    {
        var layout = new InstallLayout(folder);
        try
        {
            var record = layout.ReadRelease();
            if (record is null) return EngineResult.Fail(StepId.Download, "There is no installation in this folder to repair.");
            Report(StepId.Download, StepState.Running, "Checking the program files", "", 0);
            var damage = await PackageInstaller.FindDamageAsync(layout, new Progress<double>(value => Report(StepId.Download, StepState.Running, "Checking the program files", "", value)), cancellation).ConfigureAwait(false);
            if (damage is null || damage.Count > 0)
            {
                Report(StepId.Download, StepState.Running, "Fetching the program again", damage is null ? "The list of files is missing." : $"{damage.Count} file(s) are missing or changed.");
                var check = await releases.GetByTagAsync(record.Tag, cancellation).ConfigureAwait(false);
                if (check.Release is null) return EngineResult.Fail(StepId.Download, $"The program files are damaged and version {record.Version} could not be fetched again: {check.Message}");
                var packaged = await InstallPackageAsync(layout, check.Release, cancellation).ConfigureAwait(false);
                if (!packaged.Succeeded) return packaged;
            }
            else Report(StepId.Download, StepState.Done, "Program files are whole");
            var made = await MakeGameAsync(layout, cancellation).ConfigureAwait(false);
            if (!made.Succeeded) return made;
            Report(StepId.Play, StepState.Done, "Everything checked", "Ready to play.");
            return EngineResult.Ok("Everything checked.");
        }
        catch (OperationCanceledException)
        {
            return new EngineResult(false, "Cancelled.", Cancelled: true);
        }
    }

    /// <summary>
    /// Removes the install folder - only a folder this launcher made (it holds our release record), never anything else. Saved games and settings live in the
    /// user's profile (<c>%APPDATA%\CnCNew</c>) and stay.
    /// </summary>
    public EngineResult Uninstall(string folder)
    {
        var layout = new InstallLayout(folder);
        if (layout.ReadRelease() is null) return EngineResult.Fail(StepId.Folder, "That folder was not made by this launcher, so it is not removed.");
        if (_gameIsRunning(layout)) return EngineResult.Fail(StepId.Folder, "The game is running. Close it first.");
        try
        {
            Directory.Delete(layout.Root, recursive: true);
            return EngineResult.Ok("The game was removed. Your saved games and settings are kept.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return EngineResult.Fail(StepId.Folder, $"Some files could not be removed ({exception.Message}). Close the game and try again.");
        }
    }

    /// <summary>Starts the game with the content of the install folder. Returns an error message, or null when it started.</summary>
    public static string? Play(InstallLayout layout)
    {
        if (layout.Inspect() != InstallState.Ready) return "The game is not installed completely. Use Repair.";
        try
        {
            var start = new ProcessStartInfo(layout.ClientExe) { WorkingDirectory = Path.GetDirectoryName(layout.ClientExe)!, UseShellExecute = false };
            start.ArgumentList.Add("--content-dir");
            start.ArgumentList.Add(layout.ContentDirectory);
            Process.Start(start)?.Dispose();
            return null;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return $"The game could not be started: {exception.Message}";
        }
    }

    public static bool DefaultGameIsRunning(InstallLayout layout)
    {
        try
        {
            foreach (var process in Process.GetProcessesByName("CnCNew.Client.Stride"))
                using (process)
                {
                    try
                    {
                        if (process.MainModule?.FileName is { } path && path.StartsWith(layout.Root, StringComparison.OrdinalIgnoreCase)) return true;
                    }
                    catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException) { return true; }
                }
        }
        catch (InvalidOperationException) { }
        return false;
    }

    public static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):F1} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):F0} MB",
        >= 1L << 10 => $"{bytes / 1024.0:F0} KB",
        _ => $"{bytes} bytes",
    };

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }
}
