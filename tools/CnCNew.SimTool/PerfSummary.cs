using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using CnCNew.SimTool.Manifests;

namespace CnCNew.SimTool.Perf;

/// <summary>
/// The machine-readable summary of one measuring run (<c>SimTool perf --json</c>, <c>perf-mission --json</c>, <c>PresentationTool sync-perf --json</c>;
/// docs/PERF-GAME-LAYER.md). It is a <see cref="ManifestObject"/>, so the file has exactly one spelling (ordinal key order, whole numbers, LF): times are
/// whole nanoseconds, sizes whole bytes. Pure code without a clock; the tools measure, this file only names the fields.
/// </summary>
public static class PerfSummary
{
    public const string Kind = "perf-summary";
    public const long Schema = 1;

    /// <summary>The frame of a summary; the caller adds <c>results</c>, <c>tick</c>, <c>phases</c>, <c>modules</c>, <c>allocation</c>, <c>objects</c>, <c>replay</c>, <c>snapshot</c>.</summary>
    /// <param name="tool">The command that measured (<c>perf</c>, <c>perf-mission</c>, <c>sync-perf</c>).</param>
    /// <param name="label">Free text of the caller, by convention the commit; the tool does not ask git.</param>
    /// <param name="parameters">Everything that decides which run this is; two summaries are only comparable when these are equal.</param>
    public static ManifestObject Create(string tool, string label, string hardware, ManifestObject parameters)
        => new ManifestObject().Add("kind", Kind).Add("schema", Schema).Add("tool", tool).Add("label", label).Add("hardware", hardware).Add("parameters", parameters);

    /// <summary>Microseconds (as the tools print them) to whole nanoseconds.</summary>
    public static long Nanoseconds(double microseconds) => (long)Math.Round(microseconds * 1000.0, MidpointRounding.AwayFromZero);

    public static ManifestObject Distribution(double meanUs, double medianUs, double p95Us, double p99Us, double maxUs)
        => new ManifestObject().Add("meanNs", Nanoseconds(meanUs)).Add("medianNs", Nanoseconds(medianUs)).Add("p95Ns", Nanoseconds(p95Us)).Add("p99Ns", Nanoseconds(p99Us)).Add("maxNs", Nanoseconds(maxUs));

    /// <summary>Mean, median, 95 % and 99 % quantile (nearest rank) and maximum of values given in nanoseconds.</summary>
    public static ManifestObject DistributionOfNanoseconds(IReadOnlyList<long> values)
    {
        if (values.Count == 0) return Distribution(0, 0, 0, 0, 0);
        var sorted = values.ToArray();
        Array.Sort(sorted);
        decimal sum = 0;
        foreach (var value in sorted) sum += value;
        long Rank(double share) => sorted[Math.Clamp((int)Math.Ceiling(share * sorted.Length) - 1, 0, sorted.Length - 1)];
        return new ManifestObject().Add("meanNs", (long)Math.Round(sum / sorted.Length, MidpointRounding.AwayFromZero)).Add("medianNs", Rank(0.50)).Add("p95Ns", Rank(0.95)).Add("p99Ns", Rank(0.99)).Add("maxNs", sorted[^1]);
    }

    public static void Write(string path, ManifestObject summary) => File.WriteAllBytes(path, ManifestDocument.WriteBytes(summary));

    public static ManifestObject Read(string path)
    {
        var summary = ManifestDocument.Read(File.ReadAllBytes(path));
        Validate(summary, path);
        return summary;
    }

    /// <summary>Refuses anything that is not a summary of this schema.</summary>
    public static void Validate(ManifestObject summary, string source)
    {
        if (!summary.TryGet("kind", out var kind) || kind is not ManifestText { Value: Kind }) throw new InvalidDataException($"{source}: not a perf summary (kind is not \"{Kind}\").");
        if (!summary.TryGet("schema", out var schema) || schema is not ManifestNumber { Value: Schema }) throw new InvalidDataException($"{source}: perf summary schema {(schema is null ? "<missing>" : schema.Display)} is not the supported schema {Schema}.");
        foreach (var name in new[] { "tool", "label", "hardware" })
            if (!summary.TryGet(name, out var value) || value is not ManifestText) throw new InvalidDataException($"{source}: the perf summary has no text '{name}'.");
        foreach (var name in new[] { "parameters", "results", "tick" })
            if (!summary.TryGet(name, out var value) || value is not ManifestObject) throw new InvalidDataException($"{source}: the perf summary has no object '{name}'.");
    }
}

public enum PerfDirection
{
    /// <summary>Counts that describe the run (objects, calls, ticks): shown, never judged.</summary>
    Neutral,
    LowerIsBetter,
    HigherIsBetter,
}

public enum PerfVerdict
{
    /// <summary>Within the threshold, below the noise floor, or not judged.</summary>
    Unmarked,
    Better,
    Regression,
}

/// <summary>One metric of two summaries; a side that does not have it is <see langword="null"/>.</summary>
public sealed record PerfRow(string Path, long? Old, long? New, PerfDirection Direction, PerfVerdict Verdict)
{
    /// <summary>Change in percent of the old value; <see langword="null"/> when a side is missing or the old value is 0.</summary>
    public double? DeltaPercent => Old is { } old && New is { } @new && old != 0 ? 100.0 * (@new - old) / old : null;
}

public sealed record PerfCompareOptions
{
    /// <summary>A judged metric that moved by more than this share of its old value is marked.</summary>
    public double ThresholdPercent { get; init; } = 10;

    /// <summary>A different final hash means a different run; without this the comparison is refused.</summary>
    public bool AllowDifferentHash { get; init; }

    /// <summary>Times where both sides are below this are not judged (the clock resolves 100 ns; a phase of 0.3 us doubles by chance).</summary>
    public long NoiseFloorNs { get; init; } = 1000;

    /// <summary>Every metric; otherwise of a phase only the mean and of a module only the total.</summary>
    public bool All { get; init; }
}

/// <param name="Refusal">Why the two runs are not compared at all; <see cref="Rows"/> is then empty.</param>
/// <param name="Warnings">What the reader must know before reading a number (another hash, another machine).</param>
public sealed record PerfComparison(string? Refusal, ImmutableArray<string> Warnings, ImmutableArray<PerfRow> Rows)
{
    public int Regressions => Rows.Count(row => row.Verdict == PerfVerdict.Regression);
    public int Improvements => Rows.Count(row => row.Verdict == PerfVerdict.Better);
}

/// <summary>Compares two <see cref="PerfSummary"/> documents metric by metric. Pure: no clock, no files.</summary>
public static class PerfCompare
{
    // Sections whose numbers are metrics, in the order they are shown.
    private static readonly string[] Sections = ["tick", "phases", "modules", "allocation", "objects", "replay", "snapshot", "results"];

    public static PerfComparison Compare(ManifestObject old, ManifestObject @new, PerfCompareOptions options)
    {
        PerfSummary.Validate(old, "old");
        PerfSummary.Validate(@new, "new");
        if (old.Text("tool") != @new.Text("tool"))
            return Refuse($"The summaries come from different commands ({old.Text("tool")} and {@new.Text("tool")}).");
        if (ManifestDocument.FirstDifference(old.Object("parameters"), @new.Object("parameters"), "parameters") is { } difference)
            return Refuse($"The runs have different parameters ({difference.Path}: old {difference.Expected}, new {difference.Actual}); they are not the same measurement.");

        var warnings = ImmutableArray.CreateBuilder<string>();
        var oldHash = HashOf(old);
        var newHash = HashOf(@new);
        if (oldHash != newHash)
        {
            var message = $"DIFFERENT FINAL HASH: old {oldHash}, new {newHash}. These are two different runs: the simulation did not do the same work, so the times say nothing about speed.";
            if (!options.AllowDifferentHash) return Refuse(message + " Pass --allow-different-hash to print the table anyway.");
            warnings.Add(message);
        }
        if (old.Text("hardware") != @new.Text("hardware")) warnings.Add("Different machine or runtime (the hardware lines differ): the times are not comparable.");

        var rows = ImmutableArray.CreateBuilder<PerfRow>();
        foreach (var section in Sections)
        {
            var left = old.TryGet(section, out var leftNode) ? leftNode as ManifestObject : null;
            var right = @new.TryGet(section, out var rightNode) ? rightNode as ManifestObject : null;
            if (left is null && right is null) continue;
            Walk(section, left, right, options, rows);
        }
        return new PerfComparison(null, warnings.ToImmutable(), rows.ToImmutable());

        static PerfComparison Refuse(string reason) => new(reason, [], []);
        static string HashOf(ManifestObject summary) => summary.Object("results").TryGet("finalHash", out var hash) ? hash.Display : ManifestDifference.Missing;
    }

    private static void Walk(string path, ManifestObject? old, ManifestObject? @new, PerfCompareOptions options, ImmutableArray<PerfRow>.Builder rows)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        if (old is not null) names.UnionWith(old.Values.Keys);
        if (@new is not null) names.UnionWith(@new.Values.Keys);
        foreach (var name in names)
        {
            ManifestNode? left = null, right = null;
            old?.TryGet(name, out left!);
            @new?.TryGet(name, out right!);
            var child = path + "." + name;
            if (left is ManifestObject || right is ManifestObject)
            {
                Walk(child, left as ManifestObject, right as ManifestObject, options, rows);
                continue;
            }
            if (left is not ManifestNumber && right is not ManifestNumber) continue;
            // A phase runs once per tick, so its mean is its cost; a module's cost is its total (calls times mean).
            if (!options.All && path.StartsWith("phases.", StringComparison.Ordinal) && name != "meanNs") continue;
            if (!options.All && path.StartsWith("modules.", StringComparison.Ordinal) && name != "totalNs") continue;
            var before = (left as ManifestNumber)?.Value;
            var after = (right as ManifestNumber)?.Value;
            var direction = DirectionOf(path, name);
            rows.Add(new PerfRow(child, before, after, direction, Judge(name, before, after, direction, options)));
        }
    }

    public static PerfDirection DirectionOf(string section, string name)
    {
        if (section == "results" || section == "objects" || name == "calls") return PerfDirection.Neutral;
        if (name == "ticksPerSecond") return PerfDirection.HigherIsBetter;
        return PerfDirection.LowerIsBetter;
    }

    private static PerfVerdict Judge(string name, long? old, long? @new, PerfDirection direction, PerfCompareOptions options)
    {
        if (direction == PerfDirection.Neutral || old is not { } before || @new is not { } after || before == after) return PerfVerdict.Unmarked;
        if (name.EndsWith("Ns", StringComparison.Ordinal) && before < options.NoiseFloorNs && after < options.NoiseFloorNs) return PerfVerdict.Unmarked;
        var worse = direction == PerfDirection.LowerIsBetter ? after > before : after < before;
        // From nothing to something has no percentage; it is a change beyond every threshold.
        var beyond = before == 0 || Math.Abs(100.0 * (after - before) / before) > options.ThresholdPercent;
        return !beyond ? PerfVerdict.Unmarked : worse ? PerfVerdict.Regression : PerfVerdict.Better;
    }

    /// <summary>The comparison as a markdown table (also readable on a console), LF line ends, invariant numbers.</summary>
    public static string Format(ManifestObject old, ManifestObject @new, PerfComparison comparison, PerfCompareOptions options)
    {
        var invariant = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        text.Append(invariant, $"perf-compare: {old.Text("tool")}, old \"{old.Text("label")}\" against new \"{@new.Text("label")}\"; threshold {options.ThresholdPercent.ToString("0.##", invariant)} %, times below {(options.NoiseFloorNs / 1000.0).ToString("0.###", invariant)} us on both sides are not judged.\n");
        text.Append("parameters: ").Append(string.Join(", ", old.Object("parameters").Values.Select(pair => $"{pair.Key}={pair.Value.Display}"))).Append('\n');
        if (comparison.Refusal is { } refusal)
        {
            text.Append("REFUSED: ").Append(refusal).Append('\n');
            return text.ToString();
        }
        foreach (var warning in comparison.Warnings) text.Append("WARNING: ").Append(warning).Append('\n');
        text.Append('\n');
        text.Append("| metric | old | new | delta | |\n|---|---:|---:|---:|---|\n");
        foreach (var row in comparison.Rows)
        {
            var delta = row.DeltaPercent is { } percent ? percent.ToString("+0.0;-0.0;0.0", invariant) + " %" : row.Old == row.New ? "0.0 %" : "n/a";
            var mark = row.Verdict switch { PerfVerdict.Regression => "REGRESSION", PerfVerdict.Better => "better", _ => "" };
            text.Append(invariant, $"| {row.Path} | {Show(row.Path, row.Old)} | {Show(row.Path, row.New)} | {delta} | {mark} |\n");
        }
        text.Append('\n');
        text.Append(invariant, $"{comparison.Regressions} regression(s) and {comparison.Improvements} improvement(s) beyond {options.ThresholdPercent.ToString("0.##", invariant)} % in {comparison.Rows.Length} metrics. Times depend on the machine and its load: this is a report, not a test.\n");
        return text.ToString();

        // Times in microseconds with one decimal, everything else as it is.
        static string Show(string path, long? value) => value is not { } number ? "-"
            : path.EndsWith("Ns", StringComparison.Ordinal) ? (number / 1000.0).ToString("0.0", CultureInfo.InvariantCulture) + " us"
            : number.ToString(CultureInfo.InvariantCulture);
    }
}
