using System.Diagnostics;
using System.Globalization;
using CnCNew.Data.Definitions;
using CnCNew.Simulation.Testing;

namespace CnCNew.SimTool;

/// <summary>
/// <c>rate-compare</c>: single cases of the rate-equivalence suite (docs/SIMULATION-CORE.md §39). A case stages a small scene with retail objects, runs it at
/// 30, 60 and 120 Hz and prints its game quantities side by side with the tolerance verdict. The suite names its rates itself: <c>--tick-rate</c> is ignored.
/// </summary>
internal static class RateCompare
{
    public const string Syntax = "rate-compare <content-dir> (--list | --group G | <case-id>) [--tsv <file>]";

    public static int Run(string[] args, Func<string, CompiledDefinitionGraph> compile)
    {
        var directory = args[1];
        var list = args.Contains("--list");
        var group = Value(args, "--group");
        var tsv = Value(args, "--tsv");
        var caseId = args.Length > 2 && !args[2].StartsWith("--", StringComparison.Ordinal) ? args[2] : null;
        if (!list && group is null && caseId is null)
        {
            Console.Error.WriteLine(Syntax);
            return 2;
        }
        if (args.Contains(ToolRate.Option)) Console.WriteLine($"note: {ToolRate.Option} is ignored; every case runs at 30, 60 and 120 Hz.");

        var catalogs = new RateCatalogs(compile(directory));
        Console.WriteLine("note: the catalogs are built without model geometry (airfield and runway points from the template geometry), exactly as the test suite builds them, so both print the same numbers.");

        if (list)
        {
            foreach (var entry in RateCases.AllGroups(catalogs))
            {
                Console.WriteLine($"{entry.Name}: {entry.Cases.Length.ToString(CultureInfo.InvariantCulture)} cases");
                foreach (var note in entry.Notes) Console.WriteLine($"  note: {note}");
                foreach (var gap in entry.NotCovered) Console.WriteLine($"  not covered: {gap}");
                foreach (var item in entry.Cases) Console.WriteLine($"  {item.Id}");
            }
            return 0;
        }

        RateCase[] cases;
        if (group is not null) cases = [..RateCases.Group(catalogs, group).Cases];
        else
        {
            var all = RateCases.All(catalogs);
            cases = [..all.Where(item => item.Id.Equals(caseId, StringComparison.OrdinalIgnoreCase))];
            if (cases.Length == 0) cases = [..all.Where(item => item.Id.Contains(caseId!, StringComparison.OrdinalIgnoreCase))];
            if (cases.Length == 0)
            {
                Console.Error.WriteLine($"error: no case named or containing '{caseId}' (see --list).");
                return 2;
            }
        }

        var watch = Stopwatch.StartNew();
        var results = new List<RateCaseResult>();
        Console.WriteLine(RateRunner.TableHeader);
        foreach (var item in cases)
        {
            var result = RateRunner.Run(catalogs, item);
            results.Add(result);
            foreach (var row in result.Rows) Console.WriteLine(RateRunner.TableLine(row));
        }
        Console.WriteLine($"{RateRunner.SummaryLine(results)}; {watch.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)} s");
        if (tsv is not null)
        {
            File.WriteAllText(tsv, RateRunner.Tsv(results));
            Console.WriteLine($"report: {tsv}");
        }
        return results.Any(result => result.Failed) ? 1 : 0;
    }

    private static string? Value(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
