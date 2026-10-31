using System.Diagnostics;
using System.Globalization;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Testing;

/// <summary>
/// Phase 23: the rate-equivalence suite (docs/SIMULATION-CORE.md §39). Every case stages a small scene with retail objects, runs it at 30, 60 and 120 Hz and
/// compares game quantities in seconds and world units against the tolerance of their kind; the reference is the 30 Hz value. The suite names its rates
/// itself and ignores <c>--tick-rate</c>/<see cref="TestRate.Default"/>. Scenes and measurements live in <c>src/CnCNew.Simulation/Testing/RateEquivalence</c>
/// and are shared with <c>SimTool rate-compare</c>. A group test prints the whole table and then fails once with the number of quantities outside tolerance.
/// </summary>
static partial class Tests
{
    public const string RateReportOption = "--rate-report";
    public const string RateReportVariable = "CNCNEW_RATE_REPORT";

    private static RateCatalogs? _rateCatalogs;
    private static string? _rateCatalogsDirectory;
    private static bool _rateReportStarted;

    /// <summary>The real definition graph, compiled once per process; the three catalogs are built on first use.</summary>
    private static RateCatalogs RateCatalogsOf(string directory)
    {
        if (_rateCatalogs is not null && _rateCatalogsDirectory == directory) return _rateCatalogs;
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        _rateCatalogs = new RateCatalogs(DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options), RateSuiteRules());
        _rateCatalogsDirectory = directory;
        return _rateCatalogs;
    }

    /// <summary>The file the full table is written to as tab-separated text (<c>--rate-report FILE</c> or <c>CNCNEW_RATE_REPORT</c>); it must lie outside the repository.</summary>
    /// <summary>The rule profile the suite measures under: <c>--rules NAME</c> (or <c>CNCNEW_TEST_RULES</c>), the product's without it - e.g. the movement profile of Phase 34.</summary>
    private static CnCNew.Simulation.Rules.RuleProfile RateSuiteRules()
    {
        var arguments = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(arguments, "--rules");
        var text = index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : Environment.GetEnvironmentVariable("CNCNEW_TEST_RULES");
        return string.IsNullOrEmpty(text) ? CnCNew.Simulation.Rules.RuleProfile.Product : CnCNew.Simulation.Rules.RuleProfile.Parse(text);
    }

    private static string? RateReportPath()
    {
        var arguments = Environment.GetCommandLineArgs();
        var index = Array.IndexOf(arguments, RateReportOption);
        var path = index >= 0 && index + 1 < arguments.Length ? arguments[index + 1] : Environment.GetEnvironmentVariable(RateReportVariable);
        if (string.IsNullOrEmpty(path)) return null;
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(RepositoryRoot()) + Path.DirectorySeparatorChar;
        Check.That(!full.StartsWith(root, StringComparison.OrdinalIgnoreCase), $"The rate report must be written outside the repository ({full}).");
        return full;
    }

    public static void RateEquivalenceGroup(string directory, string group)
    {
        var catalogs = RateCatalogsOf(directory);
        var watch = Stopwatch.StartNew();
        var cases = RateCases.Group(catalogs, group);
        foreach (var note in cases.Notes) Console.WriteLine($"       {group}: {note}");
        foreach (var gap in cases.NotCovered) Console.WriteLine($"       {group}: not covered: {gap}");
        Console.WriteLine("       " + RateRunner.TableHeader);
        var results = new List<RateCaseResult>();
        foreach (var item in cases.Cases)
        {
            var result = RateRunner.Run(catalogs, item);
            results.Add(result);
            foreach (var row in result.Rows) Console.WriteLine("       " + RateRunner.TableLine(row));
        }

        if (RateReportPath() is { } path)
        {
            // One file for the whole run: the first group of the process writes the header, every group appends its rows.
            var text = RateRunner.Tsv(results);
            if (_rateReportStarted) File.AppendAllText(path, text[(RateRunner.TsvHeader.Length + 1)..]);
            else File.WriteAllText(path, text);
            _rateReportStarted = true;
        }

        var summary = RateRunner.Summary(results);
        Console.WriteLine($"       {group}: {summary.Cases} cases, {summary.Quantities} quantities, {summary.Failed60} outside tolerance at 60 Hz, {summary.Failed120} at 120 Hz, " +
                          $"{summary.Unmeasured} unmeasured at every rate; {watch.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s");
        Check.That(summary.Cases > 0, $"The group {group} has no case.");
        // F-88: the cases that part between the rates under the budgeted movement, each to be closed by name; a listed case that no longer parts fails, so the list cannot go stale.
        var known = RateSuiteRules().Movement == CnCNew.Simulation.Rules.MovementProfile.Budgeted ? KnownMovementRateCases : [];
        var unexpected = results.Where(result => !known.Contains(result.Case.Id)).ToArray();
        Check.That(RateRunner.Summary(unexpected).Failed == 0, RateRunner.SummaryLine(unexpected));
        var settled = results.Where(result => known.Contains(result.Case.Id) && !result.Failed).Select(result => result.Case.Id).ToArray();
        Check.That(settled.Length == 0, $"These cases no longer part and leave the list: {string.Join(", ", settled)}.");
    }

    private static readonly string[] KnownMovementRateCases =
    [
        "locomotion/Boss_InfantryAngryMobNexus",
        "ai/skirmish FactionAmerica against FactionChina",
        "ai/skirmish FactionGLA against FactionAmerica",
    ];
}
