using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.Testing;

/// <summary>One quantity of a case at the three rates. A value is null when the quantity was absent at that rate (or the rate threw).</summary>
public sealed record RateRow(string Group, string Case, string Quantity, RateQuantityKind Kind, long? Hz30, long? Hz60, long? Hz120, bool Failed60, bool Failed120, string Note)
{
    /// <summary>Not measured at any rate: neither a pass nor a difference between rates; reported, not counted as a failure.</summary>
    public bool Unmeasured => Hz30 is null && Hz60 is null && Hz120 is null && Note.Length == 0;

    public bool Failed => Failed60 || Failed120;

    public string Result => Unmeasured ? "unmeasured" : !Failed ? "ok" : Failed60 && Failed120 ? "FAIL 60+120" : Failed60 ? "FAIL 60" : "FAIL 120";
}

/// <summary>The rows of one case, in the order the 30 Hz measurement named its quantities.</summary>
public sealed record RateCaseResult(RateCase Case, ImmutableArray<RateRow> Rows)
{
    public bool Failed => Rows.Any(row => row.Failed);
}

/// <summary>
/// Runs a case at 30, 60 and 120 Hz and compares each quantity with its 30 Hz value. An exception at one rate becomes a row of its own and fails every
/// quantity at that rate; a quantity that is present at one rate and absent at another fails.
/// </summary>
public static class RateRunner
{
    public static RateCaseResult Run(RateCatalogs catalogs, RateCase rateCase)
    {
        var measured = new ImmutableArray<RateQuantity>[RateCatalogs.Rates.Length];
        var errors = new string?[RateCatalogs.Rates.Length];
        for (var index = 0; index < RateCatalogs.Rates.Length; index++)
        {
            try
            {
                measured[index] = rateCase.Measure(new RateScene(catalogs, RateCatalogs.Rates[index]));
            }
            catch (Exception exception)
            {
                measured[index] = [];
                var message = exception.Message.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');
                errors[index] = $"{exception.GetType().Name}: {message}";
            }
        }

        var rows = ImmutableArray.CreateBuilder<RateRow>();
        if (errors.Any(error => error is not null))
        {
            var note = string.Join(" | ", RateCatalogs.Rates.Select((rate, index) => errors[index] is { } error ? $"{rate}: {error}" : null).Where(text => text is not null));
            rows.Add(new RateRow(rateCase.Group, rateCase.Id, "(exception)", RateQuantityKind.Count, null, null, null, errors[0] is not null || errors[1] is not null, errors[0] is not null || errors[2] is not null, note));
        }

        var names = new List<(string Name, RateQuantityKind Kind)>();
        foreach (var list in measured)
            foreach (var quantity in list)
                if (!names.Any(known => known.Name == quantity.Name)) names.Add((quantity.Name, quantity.Kind));
        foreach (var (name, kind) in names)
        {
            var values = new long?[measured.Length];
            for (var index = 0; index < measured.Length; index++)
            {
                var hits = measured[index].Where(quantity => quantity.Name == name).ToArray();
                if (hits.Length > 1) throw new InvalidOperationException($"Case {rateCase.Id} names the quantity {name} twice.");
                if (hits.Length == 1 && hits[0].Kind != kind) throw new InvalidOperationException($"Case {rateCase.Id} gives the quantity {name} two kinds.");
                values[index] = hits.Length == 1 && hits[0].Present ? hits[0].Units : null;
            }
            rows.Add(new RateRow(rateCase.Group, rateCase.Id, name, kind, values[0], values[1], values[2], Differs(kind, values[0], values[1]), Differs(kind, values[0], values[2]), string.Empty));
        }
        return new RateCaseResult(rateCase, rows.ToImmutable());
    }

    private static bool Differs(RateQuantityKind kind, long? reference30, long? value)
    {
        if (reference30 is null && value is null) return false;
        if (reference30 is null || value is null) return true;
        return !RateTolerance.Within(kind, reference30.Value, value.Value);
    }

    public const string TableHeader = "case | quantity | kind | 30 Hz | 60 Hz | 120 Hz | d60 | d120 | result";

    /// <summary>One line of the table a person reads: times in seconds, distances in world units, angles in radians.</summary>
    public static string TableLine(RateRow row)
    {
        var text = $"{row.Case} | {row.Quantity} | {row.Kind} [{RateMeasure.Unit(row.Kind)}] | {Show(row.Kind, row.Hz30)} | {Show(row.Kind, row.Hz60)} | {Show(row.Kind, row.Hz120)} | " +
                   $"{Delta(row.Kind, row.Hz30, row.Hz60)} | {Delta(row.Kind, row.Hz30, row.Hz120)} | {row.Result}";
        return row.Note.Length == 0 ? text : text + " | " + row.Note;
    }

    private static string Show(RateQuantityKind kind, long? units) => units is { } value ? RateMeasure.Display(kind, value) : "-";

    private static string Delta(RateQuantityKind kind, long? reference30, long? value)
    {
        if (reference30 is null || value is null) return "-";
        var difference = value.Value - reference30.Value;
        return (difference >= 0 ? "+" : "-") + RateMeasure.Display(kind, Math.Abs(difference));
    }

    public const string TsvHeader = "group\tcase\tquantity\tkind\tunit\tunits30\tunits60\tunits120\tshown30\tshown60\tshown120\tlimit\tresult\tnote";

    /// <summary>The same row for a file: the integer units next to the displayed values and the allowed difference in units.</summary>
    public static string TsvLine(RateRow row)
    {
        static string Raw(long? units) => units is { } value ? value.ToString(CultureInfo.InvariantCulture) : string.Empty;
        var limit = row.Hz30 is { } reference ? RateTolerance.Limit(row.Kind, reference).ToString(CultureInfo.InvariantCulture) : string.Empty;
        return string.Join('\t', row.Group, row.Case, row.Quantity, row.Kind, RateMeasure.Unit(row.Kind), Raw(row.Hz30), Raw(row.Hz60), Raw(row.Hz120),
            Show(row.Kind, row.Hz30), Show(row.Kind, row.Hz60), Show(row.Kind, row.Hz120), limit, row.Result, row.Note);
    }

    /// <summary>The whole report as tab-separated text with <c>\n</c> line ends, identical for identical results.</summary>
    public static string Tsv(IEnumerable<RateCaseResult> results)
    {
        var lines = new List<string> { TsvHeader };
        foreach (var result in results)
            foreach (var row in result.Rows) lines.Add(TsvLine(row));
        return string.Join('\n', lines) + "\n";
    }

    /// <summary>"n of m quantities in k cases outside tolerance" and the split by rate.</summary>
    public static (int Quantities, int Failed, int Failed60, int Failed120, int Unmeasured, int Cases, int FailedCases) Summary(IReadOnlyCollection<RateCaseResult> results)
    {
        var rows = results.SelectMany(result => result.Rows).ToArray();
        return (rows.Length, rows.Count(row => row.Failed), rows.Count(row => row.Failed60), rows.Count(row => row.Failed120), rows.Count(row => row.Unmeasured), results.Count, results.Count(result => result.Failed));
    }

    public static string SummaryLine(IReadOnlyCollection<RateCaseResult> results)
    {
        var summary = Summary(results);
        return string.Create(CultureInfo.InvariantCulture,
            $"{summary.Failed} of {summary.Quantities} quantities in {summary.FailedCases} cases outside tolerance ({summary.Failed60} at 60 Hz, {summary.Failed120} at 120 Hz; {summary.Cases} cases, {summary.Unmeasured} quantities unmeasured at every rate)");
    }
}
