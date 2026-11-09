System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
var only = args.SkipWhile(value => value != "--only").Skip(1).FirstOrDefault();
var run = new Runner(only);
run.Test("Version: parsing, ordering, pre-releases", Tests.Versions);
run.Test("Releases: the GitHub JSON, the assets the launcher looks for", Tests.ReleaseJson);
run.Test("Releases: not found, rate limit, server error, offline, nonsense - each an answer in plain words", Tests.ReleaseFailures);
run.Test("Checksums: the formats a .sha256 file comes in", Tests.ChecksumFiles);
run.Test("Download: progress, the file appears only when whole, a short transfer leaves nothing", Tests.Download);
run.Test("Package: unpacking, the single top folder, unsafe names, a package that is none", Tests.Unpack);
run.Test("Package: an update keeps the game folder; damage is found by the file list", Tests.ApplyAndDamage);
run.Test("Folder rules: Steam, protected folders, drives, other people's files", Tests.Folders);
run.Test("Settings: round trip, defaults for a missing or damaged file", Tests.Settings);
run.Test("Steam copy: libraries, the app manifest, the language", Tests.SteamLocating);
run.Test("Preflight: each failure says what to do", Tests.PreflightChecks);
run.Test("Patcher output: progress lines, plain-word failures", Tests.PatcherLines);
run.Test("Process runner: output lines, exit code, a program that does not exist", Tests.ProcessRunner);
run.Test("Engine: a first install end to end against a fake GitHub and a fake patcher", Tests.FirstInstall);
run.Test("Engine: a wrong checksum installs nothing", Tests.WrongChecksum);
run.Test("Engine: a release without a checksum or package is refused", Tests.IncompleteRelease);
run.Test("Engine: an update replaces the program and keeps the game", Tests.Update);
run.Test("Engine: repair fetches only what is damaged; the patcher failing is explained", Tests.Repair);
run.Test("Engine: the simulation check is off by default and skipped when the package lacks it", Tests.SimulationCheck);
run.Test("Engine: uninstall removes only what the launcher made", Tests.Uninstall);
run.Test("Update offer: newer, equal, pre-release, skipped", Tests.UpdateOffers);
run.Test("Step list: states follow the engine", Tests.Steps);
run.Test("Self update: the new launcher is verified, the old file is removed", Tests.SelfUpdating);
run.Test("Layer rules: the launcher references no project and no package of the game", Tests.LayerRules);
return run.Finish();

sealed class Runner(string? only)
{
    private int _failed;
    private int _passed;

    public void Test(string name, Action test)
    {
        if (only is not null && !name.Contains(only, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            test();
            _passed++;
            Console.WriteLine($"  ok   {name}");
        }
        catch (Exception exception)
        {
            _failed++;
            Console.Error.WriteLine($"  FAIL {name}: {exception.Message}");
            Console.Error.WriteLine(exception.StackTrace);
        }
    }

    public int Finish()
    {
        if (_failed > 0)
        {
            Console.Error.WriteLine($"{_failed} launcher test(s) failed, {_passed} passed.");
            return 1;
        }
        Console.WriteLine($"All {_passed} launcher tests passed.");
        return 0;
    }
}

static class Check
{
    public static void That(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException($"{message} (expected {expected}, got {actual})");
    }

    public static void Contains(string text, string part, string message)
    {
        if (!text.Contains(part, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException($"{message} (looked for '{part}' in '{text}')");
    }
}
