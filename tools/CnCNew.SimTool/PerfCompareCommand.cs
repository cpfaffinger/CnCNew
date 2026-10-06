using System.Globalization;
using CnCNew.SimTool.Manifests;
using CnCNew.SimTool.Perf;

namespace CnCNew.SimTool;

/// <summary>
/// <c>SimTool perf-compare &lt;old.json&gt; &lt;new.json&gt;</c> (docs/PERF-GAME-LAYER.md): two summaries written with <c>--json</c>, metric by metric. A report, not a
/// test: times depend on the machine, so a regression never fails the command. Exit code 0 when the table was printed, 2 when the two runs are not the
/// same measurement (other parameters, another final hash without <c>--allow-different-hash</c>), 1 when a file is not a usable summary.
/// </summary>
internal static class PerfCompareCommand
{
    public const string Usage = "perf-compare <old.json> <new.json> [--threshold-percent N] [--min-us N] [--all] [--allow-different-hash] [--out FILE]";

    public static int Run(string[] args)
    {
        if (args.Length < 3) throw new ArgumentException(Usage);
        var invariant = CultureInfo.InvariantCulture;
        string? Arg(string name) => Array.IndexOf(args, name) is var index and >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        var options = new PerfCompareOptions
        {
            ThresholdPercent = double.Parse(Arg("--threshold-percent") ?? "10", NumberStyles.Float, invariant),
            NoiseFloorNs = PerfSummary.Nanoseconds(double.Parse(Arg("--min-us") ?? "1", NumberStyles.Float, invariant)),
            AllowDifferentHash = args.Contains("--allow-different-hash"),
            All = args.Contains("--all"),
        };
        if (!(options.ThresholdPercent >= 0)) throw new ArgumentException("--threshold-percent must not be negative.");
        ManifestObject old, @new;
        try
        {
            old = PerfSummary.Read(args[1]);
            @new = PerfSummary.Read(args[2]);
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 1;
        }
        var comparison = PerfCompare.Compare(old, @new, options);
        var text = PerfCompare.Format(old, @new, comparison, options);
        Console.Out.Write(text);
        if (Arg("--out") is { } file) File.WriteAllText(Path.GetFullPath(file), text, new System.Text.UTF8Encoding(false));
        return comparison.Refusal is null ? 0 : 2;
    }
}
