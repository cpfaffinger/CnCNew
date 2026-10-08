using System.Diagnostics;
using System.Globalization;
using CnCNew.Data.Definitions;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;

namespace CnCNew.SimTool;

/// <summary>
/// <c>rate-compare</c>: single cases of the rate-equivalence suite (docs/SIMULATION-CORE.md §39). A case stages a small scene with retail objects, runs it at
/// 30, 60 and 120 Hz and prints its game quantities side by side with the tolerance verdict. The suite names its rates itself: <c>--tick-rate</c> is ignored.
/// </summary>
internal static class RateCompare
{
    public const string Syntax = "rate-compare <content-dir> (--list | --group G | <case-id> | --ai-diff FACTION,FACTION [--rates A,B] [--context N] [--dump DIR] [--state [--positions] [--frames N]]) [--tsv <file>]";

    public static int Run(string[] args, Func<string, CompiledDefinitionGraph> compile)
    {
        var directory = args[1];
        var list = args.Contains("--list");
        var group = Value(args, "--group");
        var tsv = Value(args, "--tsv");
        var caseId = args.Length > 2 && !args[2].StartsWith("--", StringComparison.Ordinal) ? args[2] : null;
        if (Value(args, "--ai-diff") is { } pairing) return AiDiff(args, pairing, compile(directory));
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

    /// <summary>
    /// The first command at which the command streams of one skirmish of the group <c>ai</c> part between two rates, in game time: the cause of everything after it.
    /// Commands are compared in order, by game time (1/120 s) and by their text without the tick.
    /// </summary>
    private static int AiDiff(string[] args, string pairing, CompiledDefinitionGraph graph)
    {
        var factions = pairing.Split(',');
        var rates = (Value(args, "--rates") ?? "30,60").Split(',').Select(text => SimulationRate.FromTicksPerSecond(int.Parse(text, CultureInfo.InvariantCulture))).ToArray();
        var context = int.Parse(Value(args, "--context") ?? "6", CultureInfo.InvariantCulture);
        if (factions.Length != 2 || rates.Length != 2)
        {
            Console.Error.WriteLine(Syntax);
            return 2;
        }
        var catalogs = new RateCatalogs(graph);
        if (args.Contains("--state"))
        {
            // The first logic frame of the original after which the two worlds differ, and the objects that differ.
            var limit = long.Parse(Value(args, "--frames") ?? "14400", CultureInfo.InvariantCulture);
            var frame = RateCases.SkirmishAiFirstStateDifference(catalogs, rates[0], rates[1], factions[0], factions[1], limit, context, args.Contains("--positions"), Console.WriteLine, long.Parse(Value(args, "--watch") ?? "0", CultureInfo.InvariantCulture), long.Parse(Value(args, "--watch-from") ?? "9223372036854775807", CultureInfo.InvariantCulture));
            Console.WriteLine(frame < 0 ? $"The worlds are equal at the end of each of {limit} frames." : $"First difference after frame {frame} ({(frame / 30m).ToString("0.0000", CultureInfo.InvariantCulture)} s of game time).");
            return frame < 0 ? 0 : 1;
        }
        var first = RateCases.SkirmishAiCommandStream(catalogs, rates[0], factions[0], factions[1]);
        var second = RateCases.SkirmishAiCommandStream(catalogs, rates[1], factions[0], factions[1]);
        if (Value(args, "--dump") is { } dump)
        {
            Directory.CreateDirectory(dump);
            File.WriteAllLines(Path.Combine(dump, $"commands-{rates[0].TicksPerSecond}.txt"), first.Select(Line));
            File.WriteAllLines(Path.Combine(dump, $"commands-{rates[1].TicksPerSecond}.txt"), second.Select(Line));
        }
        var common = Math.Min(first.Count, second.Count);
        var index = 0;
        while (index < common && first[index] == second[index]) index++;
        Console.WriteLine($"{factions[0]} against {factions[1]}: {first.Count} commands at {rates[0].TicksPerSecond} Hz, {second.Count} at {rates[1].TicksPerSecond} Hz.");
        if (index == common && first.Count == second.Count)
        {
            Console.WriteLine("The command streams are equal.");
            return 0;
        }
        var at = index < first.Count ? first[index].Units : second[index].Units;
        Console.WriteLine($"First difference at command {index} ({Seconds(at)} s of game time); {index} commands before it are equal.");
        for (var line = Math.Max(0, index - context); line < Math.Min(Math.Max(first.Count, second.Count), index + context); line++)
        {
            var mark = line < index ? " " : "!";
            Console.WriteLine($"{mark} {line} {rates[0].TicksPerSecond} Hz: {(line < first.Count ? Line(first[line]) : "-")}");
            if (line >= index) Console.WriteLine($"{mark} {line} {rates[1].TicksPerSecond} Hz: {(line < second.Count ? Line(second[line]) : "-")}");
        }
        return 1;

        static string Seconds(long units) => (units / 120m).ToString("0.0000", CultureInfo.InvariantCulture);
        static string Line((long Units, string Text) command) => $"{Seconds(command.Units)} s {command.Text}";
    }

    private static string? Value(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
