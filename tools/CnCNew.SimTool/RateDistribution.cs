using System.Globalization;
using System.Text;

namespace CnCNew.SimTool.Survey;

// Everything in this file is pure (BCL only, no simulation): the statistics, the survey file and the report of `rate-distribution`.
// The tests compile it as source (tests/CnCNew.Simulation.Tests). docs/SIMULATION-CORE.md §40 derives the limits.

/// <summary>One match of a survey (<c>ai-survey</c>): one line of the TSV file.</summary>
/// <param name="WinnerSlot">1 when the first-named faction won, 2 for the second, 0 without a winner.</param>
/// <param name="EndTick">The tick the winner was found in, or the tick the match stopped at without one.</param>
/// <param name="EndGameMilliseconds">Game time of <paramref name="EndTick"/> in whole milliseconds (<c>EndTick * 1000 / TickRate</c>).</param>
/// <param name="RecordedTicks">Ticks played, the grace period after a victory included; the state of <paramref name="FinalHash"/>.</param>
internal sealed record SurveyRow(
    string Pairing, string Map, string FactionA, string FactionB, uint Seed, int TickRate, string Outcome, string WinnerFaction, int WinnerSlot,
    long EndTick, long EndGameMilliseconds, long RecordedTicks, long Commands, string FinalHash,
    long FirstBarracksTickA, long FirstBarracksTickB, long FirstAttackTickA, long FirstAttackTickB)
{
    public const string Winner = "winner";
    public const string Stalemate = "stalemate";
    public const string Failed = "failed";

    public bool Decided => Outcome == Winner;
    public double GameMinutes => EndGameMilliseconds / 60_000.0;
}

/// <summary>A survey file: tab separated, one header line, LF line ends, sorted by pairing (ordinal) and seed, no time stamps.</summary>
internal sealed class SurveyFile
{
    public static readonly string[] Columns =
    [
        "pairing", "map", "factionA", "factionB", "seed", "tickRate", "outcome", "winnerFaction", "winnerSlot", "endTick", "endGameMilliseconds", "recordedTicks",
        "commands", "finalHash", "firstBarracksTickA", "firstBarracksTickB", "firstAttackTickA", "firstAttackTickB",
    ];

    private SurveyFile(IReadOnlyList<SurveyRow> rows) => Rows = rows;

    public IReadOnlyList<SurveyRow> Rows { get; }
    public int TickRate => Rows[0].TickRate;
    public IEnumerable<string> Pairings => Rows.Select(row => row.Pairing).Distinct(StringComparer.Ordinal);

    public static string Line(SurveyRow row) => string.Join('\t',
        row.Pairing, row.Map, row.FactionA, row.FactionB, Text(row.Seed), Text(row.TickRate), row.Outcome, row.WinnerFaction, Text(row.WinnerSlot), Text(row.EndTick),
        Text(row.EndGameMilliseconds), Text(row.RecordedTicks), Text(row.Commands), row.FinalHash,
        Text(row.FirstBarracksTickA), Text(row.FirstBarracksTickB), Text(row.FirstAttackTickA), Text(row.FirstAttackTickB));

    public static string Write(IEnumerable<SurveyRow> rows)
    {
        var text = new StringBuilder(string.Join('\t', Columns)).Append('\n');
        foreach (var row in rows.OrderBy(row => row.Pairing, StringComparer.Ordinal).ThenBy(row => row.Seed)) text.Append(Line(row)).Append('\n');
        return text.ToString();
    }

    /// <exception cref="FormatException">The text is not a complete, consistent survey; the message names the file and the line.</exception>
    public static SurveyFile Read(string text, string name)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length == 0 || lines[0] != string.Join('\t', Columns)) throw new FormatException($"{name}: the first line is not the header of a survey ({string.Join(' ', Columns)}).");
        var rows = new List<SurveyRow>();
        var seen = new HashSet<(string, uint)>();
        for (var index = 1; index < lines.Length; index++)
        {
            if (lines[index].Length == 0)
            {
                if (index == lines.Length - 1) break;
                throw new FormatException($"{name}, line {index + 1}: empty line.");
            }
            var cells = lines[index].Split('\t');
            if (cells.Length != Columns.Length) throw new FormatException($"{name}, line {index + 1}: {cells.Length} fields instead of {Columns.Length}.");
            SurveyRow row;
            try
            {
                row = new SurveyRow(cells[0], cells[1], cells[2], cells[3], uint.Parse(cells[4], NumberStyles.None, CultureInfo.InvariantCulture), int.Parse(cells[5], NumberStyles.None, CultureInfo.InvariantCulture),
                    cells[6], cells[7], int.Parse(cells[8], NumberStyles.None, CultureInfo.InvariantCulture), Long(cells[9]), Long(cells[10]), Long(cells[11]), Long(cells[12]), cells[13],
                    Long(cells[14]), Long(cells[15]), Long(cells[16]), Long(cells[17]));
            }
            catch (Exception exception) when (exception is FormatException or OverflowException)
            {
                throw new FormatException($"{name}, line {index + 1}: a number cannot be read.");
            }
            if (row.Outcome == SurveyRow.Failed) throw new FormatException($"{name}, line {index + 1}: the match {row.Pairing} seed {Text(row.Seed)} failed; a survey with failed matches is not compared.");
            if (row.Outcome is not (SurveyRow.Winner or SurveyRow.Stalemate)) throw new FormatException($"{name}, line {index + 1}: unknown outcome '{row.Outcome}'.");
            if (row.Decided != row.WinnerSlot is 1 or 2 || (!row.Decided && row.WinnerSlot != 0)) throw new FormatException($"{name}, line {index + 1}: outcome '{row.Outcome}' and winner slot {Text(row.WinnerSlot)} contradict each other.");
            if (row.Decided && row.WinnerFaction != (row.WinnerSlot == 1 ? row.FactionA : row.FactionB)) throw new FormatException($"{name}, line {index + 1}: the winner faction is not the faction of the winner slot.");
            if (row.TickRate <= 0 || row.EndTick < 0 || row.EndGameMilliseconds != row.EndTick * 1000 / row.TickRate) throw new FormatException($"{name}, line {index + 1}: end tick, tick rate and game time contradict each other.");
            if (!seen.Add((row.Pairing, row.Seed))) throw new FormatException($"{name}, line {index + 1}: {row.Pairing} seed {Text(row.Seed)} is listed twice.");
            if (rows.Count > 0 && row.TickRate != rows[0].TickRate) throw new FormatException($"{name}, line {index + 1}: tick rate {Text(row.TickRate)} in a survey at {Text(rows[0].TickRate)} Hz.");
            var same = rows.FirstOrDefault(other => other.Pairing == row.Pairing);
            if (same is not null && (same.FactionA != row.FactionA || same.FactionB != row.FactionB || same.Map != row.Map)) throw new FormatException($"{name}, line {index + 1}: {row.Pairing} has another map or other factions than above.");
            if (row.FactionA == row.FactionB) throw new FormatException($"{name}, line {index + 1}: a mirror match cannot be attributed to a faction.");
            rows.Add(row);
        }
        if (rows.Count == 0) throw new FormatException($"{name}: no matches.");
        return new SurveyFile([..rows.OrderBy(row => row.Pairing, StringComparer.Ordinal).ThenBy(row => row.Seed)]);
    }

    /// <summary>Two surveys are compared only when they played the same pairings (map, factions) under the same seeds.</summary>
    /// <exception cref="FormatException">The sets differ; the message names the first difference.</exception>
    public static void RequireSameDesign(SurveyFile reference, SurveyFile candidate)
    {
        var left = reference.Rows.ToDictionary(row => (row.Pairing, row.Seed));
        var right = candidate.Rows.ToDictionary(row => (row.Pairing, row.Seed));
        foreach (var row in reference.Rows)
        {
            if (!right.TryGetValue((row.Pairing, row.Seed), out var other)) throw new FormatException($"The surveys differ: {row.Pairing} seed {Text(row.Seed)} is only in the reference.");
            if (other.Map != row.Map || other.FactionA != row.FactionA || other.FactionB != row.FactionB) throw new FormatException($"The surveys differ: {row.Pairing} has another map or other factions in the candidate.");
        }
        foreach (var row in candidate.Rows)
            if (!left.ContainsKey((row.Pairing, row.Seed))) throw new FormatException($"The surveys differ: {row.Pairing} seed {Text(row.Seed)} is only in the candidate.");
    }

    private static long Long(string text) => long.Parse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);
    private static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>The distribution functions the comparison needs, written out from their definitions (series, continued fractions) so that every value can be checked against a closed form.</summary>
internal static class SurveyStatistics
{
    public static double NormalDensity(double x) => Math.Exp(-0.5 * x * x) / Math.Sqrt(2 * Math.PI);

    /// <summary>P(Z &gt; x) of the standard normal distribution.</summary>
    public static double NormalUpperTail(double x)
    {
        if (double.IsNaN(x)) return double.NaN;
        if (x < 0) return 1 - NormalUpperTail(-x);
        if (x > 40) return 0;
        if (x < 3)
        {
            // Phi(x) = 1/2 + phi(x) * sum over k of x^(2k+1) / (1 * 3 * ... * (2k+1)).
            double term = x, sum = x;
            for (var k = 1; k < 500 && term > sum * 1e-17; k++)
            {
                term *= x * x / (2 * k + 1);
                sum += term;
            }
            return 0.5 - NormalDensity(x) * sum;
        }
        // Q(x) = phi(x) / (x + 1 / (x + 2 / (x + 3 / ...))), evaluated from a fixed depth backwards.
        var fraction = x;
        for (var k = 600; k >= 1; k--) fraction = x + k / fraction;
        return NormalDensity(x) / fraction;
    }

    public static double NormalCdf(double x) => NormalUpperTail(-x);

    /// <summary>The x with P(Z &lt;= x) = <paramref name="p"/>, by bisection on the tail function.</summary>
    public static double NormalQuantile(double p)
    {
        if (!(p > 0 && p < 1)) throw new ArgumentOutOfRangeException(nameof(p), "A quantile needs a probability strictly between 0 and 1.");
        if (p < 0.5) return -NormalQuantile(1 - p);
        var tail = 1 - p;
        double low = 0, high = 40;
        for (var step = 0; step < 200; step++)
        {
            var middle = 0.5 * (low + high);
            if (middle == low || middle == high) break;
            if (NormalUpperTail(middle) > tail) low = middle;
            else high = middle;
        }
        return 0.5 * (low + high);
    }

    /// <summary>ln Gamma(x) for x &gt; 0: the argument is shifted above 15 with Gamma(x + 1) = x Gamma(x), then Stirling's series.</summary>
    public static double LogGamma(double x)
    {
        if (!(x > 0)) throw new ArgumentOutOfRangeException(nameof(x), "ln Gamma is implemented for positive arguments.");
        var shift = 0.0;
        while (x < 15)
        {
            shift -= Math.Log(x);
            x += 1;
        }
        var inverse = 1 / x;
        var square = inverse * inverse;
        var series = inverse * (1.0 / 12 - square * (1.0 / 360 - square * (1.0 / 1260 - square * (1.0 / 1680 - square / 1188))));
        return shift + (x - 0.5) * Math.Log(x) - x + 0.5 * Math.Log(2 * Math.PI) + series;
    }

    /// <summary>The regularized incomplete beta function I_x(a, b), by its continued fraction (modified Lentz), on the side of x where it converges fast.</summary>
    public static double RegularizedBeta(double x, double a, double b)
    {
        if (!(a > 0 && b > 0)) throw new ArgumentOutOfRangeException(nameof(a), "I_x(a, b) needs positive a and b.");
        if (double.IsNaN(x)) return double.NaN;
        if (x <= 0) return 0;
        if (x >= 1) return 1;
        var front = Math.Exp(a * Math.Log(x) + b * Math.Log(1 - x) + LogGamma(a + b) - LogGamma(a) - LogGamma(b));
        return x < (a + 1) / (a + b + 2) ? front * BetaFraction(x, a, b) / a : 1 - front * BetaFraction(1 - x, b, a) / b;
    }

    private static double BetaFraction(double x, double a, double b)
    {
        const double Tiny = 1e-300;
        static double Guard(double value) => Math.Abs(value) < Tiny ? Tiny : value;
        var c = 1.0;
        var d = 1 / Guard(1 - (a + b) * x / (a + 1));
        var h = d;
        for (var m = 1; m <= 100_000; m++)
        {
            var even = m * (b - m) * x / ((a + 2 * m - 1) * (a + 2 * m));
            d = 1 / Guard(1 + even * d);
            c = Guard(1 + even / c);
            h *= d * c;
            var odd = -(a + m) * (a + b + m) * x / ((a + 2 * m) * (a + 2 * m + 1));
            d = 1 / Guard(1 + odd * d);
            c = Guard(1 + odd / c);
            var factor = d * c;
            h *= factor;
            if (Math.Abs(factor - 1) < 1e-15) return h;
        }
        throw new ArithmeticException($"The continued fraction of the incomplete beta function did not converge (x {x.ToString("R", CultureInfo.InvariantCulture)}, a {a.ToString("R", CultureInfo.InvariantCulture)}, b {b.ToString("R", CultureInfo.InvariantCulture)}).");
    }

    /// <summary>P(|T| &gt;= |t|) of Student's t distribution: I_{df / (df + t^2)}(df / 2, 1 / 2).</summary>
    public static double StudentTwoSided(double t, double degreesOfFreedom)
    {
        if (!(degreesOfFreedom > 0)) throw new ArgumentOutOfRangeException(nameof(degreesOfFreedom));
        if (double.IsInfinity(t)) return 0;
        return RegularizedBeta(degreesOfFreedom / (degreesOfFreedom + t * t), 0.5 * degreesOfFreedom, 0.5);
    }

    /// <summary>The t &gt;= 0 with P(|T| &gt;= t) = <paramref name="twoSided"/>.</summary>
    public static double StudentCritical(double twoSided, double degreesOfFreedom)
    {
        if (!(twoSided > 0 && twoSided <= 1)) throw new ArgumentOutOfRangeException(nameof(twoSided));
        double low = 0, high = 1;
        while (StudentTwoSided(high, degreesOfFreedom) > twoSided) high *= 2;
        for (var step = 0; step < 200; step++)
        {
            var middle = 0.5 * (low + high);
            if (middle == low || middle == high) break;
            if (StudentTwoSided(middle, degreesOfFreedom) > twoSided) low = middle;
            else high = middle;
        }
        return 0.5 * (low + high);
    }

    public sealed record Proportions(double Share1, double Share2, double Difference, double StandardError, double Z);

    /// <summary>Two-proportion z statistic with the pooled variance. Without any variance (both samples all or nothing alike) the difference is 0 and so is z.</summary>
    public static Proportions TwoProportions(long hits1, long n1, long hits2, long n2)
    {
        if (n1 <= 0 || n2 <= 0 || hits1 < 0 || hits2 < 0 || hits1 > n1 || hits2 > n2) throw new ArgumentOutOfRangeException(nameof(n1), "Counts of a proportion are out of range.");
        double share1 = (double)hits1 / n1, share2 = (double)hits2 / n2;
        var pooled = (double)(hits1 + hits2) / (n1 + n2);
        var error = Math.Sqrt(pooled * (1 - pooled) * (1.0 / n1 + 1.0 / n2));
        return new Proportions(share1, share2, share2 - share1, error, error > 0 ? (share2 - share1) / error : 0);
    }

    public sealed record Sample(int Count, double Mean, double Variance)
    {
        public double Deviation => Math.Sqrt(Variance);

        /// <summary>Mean and the unbiased variance (divisor n - 1; 0 for fewer than two values).</summary>
        public static Sample Of(IReadOnlyCollection<double> values)
        {
            if (values.Count == 0) return new Sample(0, double.NaN, double.NaN);
            var mean = values.Sum() / values.Count;
            return new Sample(values.Count, mean, values.Count < 2 ? 0 : values.Sum(value => (value - mean) * (value - mean)) / (values.Count - 1));
        }
    }

    public sealed record Welch(double T, double DegreesOfFreedom, double P);

    /// <summary>Welch's t test of two samples with at least two values each, degrees of freedom after Welch and Satterthwaite, two-sided p.</summary>
    public static Welch WelchTest(Sample first, Sample second)
    {
        if (first.Count < 2 || second.Count < 2) throw new ArgumentException("Welch's test needs two values per sample.");
        double v1 = first.Variance / first.Count, v2 = second.Variance / second.Count;
        var difference = second.Mean - first.Mean;
        if (v1 + v2 <= 0) return difference == 0 ? new Welch(0, first.Count + second.Count - 2, 1) : new Welch(double.PositiveInfinity * Math.Sign(difference), first.Count + second.Count - 2, 0);
        var t = difference / Math.Sqrt(v1 + v2);
        var freedom = (v1 + v2) * (v1 + v2) / (v1 * v1 / (first.Count - 1) + v2 * v2 / (second.Count - 1));
        return new Welch(t, freedom, StudentTwoSided(t, freedom));
    }

    /// <summary>
    /// Exact test of a 2 x c table with fixed margins (Fisher, Freeman and Halton): the probability, under the multivariate hypergeometric distribution, of all
    /// tables that are at most as probable as the observed one. Every table is enumerated.
    /// </summary>
    public static double FisherFreemanHalton(IReadOnlyList<int> row1, IReadOnlyList<int> row2)
    {
        if (row1.Count != row2.Count || row1.Count == 0 || row1.Any(value => value < 0) || row2.Any(value => value < 0)) throw new ArgumentException("The two rows of the table need the same number of non-negative cells.");
        var columns = row1.Zip(row2, (a, b) => a + b).ToArray();
        int total1 = row1.Sum(), total = columns.Sum();
        if (total == 0) return 1;
        var logFactorial = new double[total + 1];
        for (var n = 1; n <= total; n++) logFactorial[n] = logFactorial[n - 1] + Math.Log(n);
        var constant = logFactorial[total1] + logFactorial[total - total1] + columns.Sum(value => logFactorial[value]) - logFactorial[total];
        double LogProbability(IReadOnlyList<int> first)
        {
            var log = constant;
            for (var j = 0; j < columns.Length; j++) log -= logFactorial[first[j]] + logFactorial[columns[j] - first[j]];
            return log;
        }

        var observed = LogProbability(row1);
        var sum = 0.0;
        var cells = new int[columns.Length];
        void Enumerate(int column, int left)
        {
            if (column == columns.Length - 1)
            {
                if (left > columns[column]) return;
                cells[column] = left;
                var log = LogProbability(cells);
                // Ties count: a table as probable as the observed one belongs to the sum whatever the rounding of the logarithms.
                if (log <= observed + 1e-9) sum += Math.Exp(log);
                return;
            }
            for (var value = Math.Min(left, columns[column]); value >= 0; value--)
            {
                cells[column] = value;
                Enumerate(column + 1, left - value);
            }
        }
        Enumerate(0, total1);
        return Math.Min(1, sum);
    }

    /// <summary>
    /// Smallest change of a proportion that a two-sided two-proportion z test at level <paramref name="alpha"/> finds with the given power (normal
    /// approximation, solved by bisection). <paramref name="upwards"/> picks the direction; NaN when no change inside [0, 1] reaches the power.
    /// </summary>
    public static double DetectableProportionChange(double baseline, long n1, long n2, double alpha, double power, bool upwards)
    {
        var critical = NormalQuantile(1 - alpha / 2);
        double Power(double delta)
        {
            var other = upwards ? baseline + delta : baseline - delta;
            var pooled = (baseline * n1 + other * n2) / (n1 + n2);
            var underNull = Math.Sqrt(pooled * (1 - pooled) * (1.0 / n1 + 1.0 / n2));
            var underAlternative = Math.Sqrt(baseline * (1 - baseline) / n1 + other * (1 - other) / n2);
            if (underAlternative <= 0) return delta > critical * underNull ? 1 : 0;
            return NormalCdf((delta - critical * underNull) / underAlternative) + NormalCdf((-delta - critical * underNull) / underAlternative);
        }

        double low = 0, high = upwards ? 1 - baseline : baseline;
        if (high <= 0 || Power(high) < power) return double.NaN;
        for (var step = 0; step < 100; step++)
        {
            var middle = 0.5 * (low + high);
            if (Power(middle) >= power) high = middle;
            else low = middle;
        }
        return high;
    }

    /// <summary>Exact power of the exact test at level <paramref name="alpha"/> for two rows of <paramref name="n"/> matches with a binary outcome of probability <paramref name="p1"/> and <paramref name="p2"/>.</summary>
    public static double ExactTestPower(int n, double p1, double p2, double alpha)
    {
        var first = Binomial(n, p1);
        var second = Binomial(n, p2);
        var power = 0.0;
        for (var x = 0; x <= n; x++)
            for (var y = 0; y <= n; y++)
                if (FisherFreemanHalton([x, n - x], [y, n - y]) < alpha) power += first[x] * second[y];
        return power;
    }

    private static double[] Binomial(int n, double p)
    {
        var values = new double[n + 1];
        for (var k = 0; k <= n; k++)
        {
            var log = LogGamma(n + 1) - LogGamma(k + 1) - LogGamma(n - k + 1);
            var tail = (k == 0 ? 1 : Math.Pow(p, k)) * (k == n ? 1 : Math.Pow(1 - p, n - k));
            values[k] = Math.Exp(log) * tail;
        }
        return values;
    }

    /// <summary>
    /// Smallest difference of two means, in standard deviations, that a two-sided t test at level <paramref name="alpha"/> finds with the given power:
    /// (t_{1 - alpha/2} + t_{power}) * sqrt(1/n1 + 1/n2) with n1 + n2 - 2 degrees of freedom (the usual approximation of the non-central distribution).
    /// </summary>
    public static double DetectableMeanDifference(int n1, int n2, double alpha, double power)
    {
        var freedom = n1 + n2 - 2;
        if (freedom < 1) return double.NaN;
        return (StudentCritical(alpha, freedom) + StudentCritical(2 * (1 - power), freedom)) * Math.Sqrt(1.0 / n1 + 1.0 / n2);
    }
}

/// <summary>The verdict of one test of the comparison.</summary>
internal enum SurveyVerdict { Pass, Fail, NotTestable }

/// <summary>
/// <c>rate-distribution</c>: two surveys of the same pairings and seeds at two tick rates, compared as distributions. The limits are fixed in advance
/// (<see cref="Alpha"/>, Bonferroni within each family of tests) and printed in the report; nothing here is fitted to data.
/// </summary>
internal static class RateDistribution
{
    /// <summary>Level of every family of tests, two-sided.</summary>
    public const double Alpha = 0.05;
    public const double Power = 0.80;

    public sealed record Result(string Report, int Passed, int Failed, int NotTestable)
    {
        public int ExitCode => Failed == 0 ? 0 : 1;
    }

    public static int Run(string[] args)
    {
        if (args.Length < 3 || args[1].StartsWith("--", StringComparison.Ordinal) || args[2].StartsWith("--", StringComparison.Ordinal))
        {
            Console.Error.WriteLine("Usage: CnCNew.SimTool rate-distribution <tsv-reference> <tsv-candidate> [--out <report.md>]");
            return 2;
        }
        Result result;
        try
        {
            var reference = SurveyFile.Read(File.ReadAllText(args[1]), Path.GetFileName(args[1]));
            var candidate = SurveyFile.Read(File.ReadAllText(args[2]), Path.GetFileName(args[2]));
            SurveyFile.RequireSameDesign(reference, candidate);
            result = Compare(reference, candidate);
        }
        catch (Exception exception) when (exception is FormatException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"error: {exception.Message}");
            return 2;
        }
        Console.Out.Write(result.Report);
        var index = Array.IndexOf(args, "--out");
        if (index >= 0 && index + 1 < args.Length) File.WriteAllText(Path.GetFullPath(args[index + 1]), result.Report, new UTF8Encoding(false));
        return result.ExitCode;
    }

    public static Result Compare(SurveyFile reference, SurveyFile candidate)
    {
        SurveyFile.RequireSameDesign(reference, candidate);
        var text = new StringBuilder();
        int passed = 0, failed = 0, untestable = 0;
        string Verdict(SurveyVerdict verdict)
        {
            switch (verdict)
            {
                case SurveyVerdict.Pass: passed++; return "PASS";
                case SurveyVerdict.Fail: failed++; return "FAIL";
                default: untestable++; return "NOT TESTABLE";
            }
        }
        void Line(string line = "") => text.Append(line).Append('\n');

        var pairings = reference.Pairings.ToArray();
        var factions = reference.Rows.SelectMany(row => new[] { row.FactionA, row.FactionB }).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
        int total = reference.Rows.Count, tests = pairings.Length;
        var perPairing = reference.Rows.GroupBy(row => row.Pairing).Select(group => group.Count()).ToArray();
        var seedsText = perPairing.Min() == perPairing.Max() ? N(perPairing[0]) : $"{N(perPairing.Min())} to {N(perPairing.Max())}";
        var pooledCritical = SurveyStatistics.NormalQuantile(1 - Alpha / 2);
        var factionAlpha = Alpha / factions.Length;
        var factionCritical = SurveyStatistics.NormalQuantile(1 - factionAlpha / 2);
        var pairingAlpha = Alpha / tests;
        string Rate(SurveyFile file) => $"{N(file.TickRate)} Hz";

        Line($"# Rate distribution: reference {Rate(reference)}, candidate {Rate(candidate)}");
        Line();
        Line($"{N(total)} matches per rate: {N(tests)} pairings, {seedsText} seeds per pairing, the same pairings and seeds in both surveys. Durations are game minutes of decided matches.");
        Line();
        Line("## Limits (fixed in advance, not fitted to these data)");
        Line();
        Line($"- Every family of tests is two-sided at alpha = {F(Alpha, "0.00")}.");
        Line($"- a. Share of decided matches, pooled: two-proportion z test with pooled variance; pass if |z| <= {F(pooledCritical, "0.000")}.");
        Line($"- b. Win share per faction, pooled: two-proportion z test per faction, Bonferroni over {N(factions.Length)} factions (alpha = {F(factionAlpha, "0.0000")} each); pass if every |z| <= {F(factionCritical, "0.000")}.");
        Line($"- c. Duration, stratified by pairing: D = mean of the per-pairing mean differences, SE = sqrt(sum of s2_ref/n_ref + s2_cand/n_cand) / m over the m pairings with at least two decided matches at both rates; pass if |D| <= {F(pooledCritical, "0.000")} * SE.");
        Line($"- d. Winner distribution per pairing (first faction wins / second faction wins / undecided): exact test of the 2 x 3 table (Fisher-Freeman-Halton, full enumeration), Bonferroni over {N(tests)} pairings; pass if p >= {F(pairingAlpha, "0.000000")}.");
        Line($"- e. Duration per pairing: Welch's t test with Welch-Satterthwaite degrees of freedom, Bonferroni over {N(tests)} pairings; pass if p >= {F(pairingAlpha, "0.000000")}; a pairing with fewer than two decided matches at either rate is not testable and is not counted as passed.");
        Line();

        // ---------------------------------------------------------------------------------------------------------------- a
        Line("## Pooled over all pairings");
        Line();
        Line("### a. Share of decided matches");
        Line();
        long decidedReference = reference.Rows.Count(row => row.Decided), decidedCandidate = candidate.Rows.Count(row => row.Decided);
        var decided = SurveyStatistics.TwoProportions(decidedReference, total, decidedCandidate, total);
        Line("| rate | decided | matches | share |");
        Line("|---|---|---|---|");
        Line($"| {Rate(reference)} | {N(decidedReference)} | {N(total)} | {F(decided.Share1)} |");
        Line($"| {Rate(candidate)} | {N(decidedCandidate)} | {N(total)} | {F(decided.Share2)} |");
        Line();
        Line($"difference {Signed(decided.Difference)}, standard error {F(decided.StandardError)}, half-width {F(pooledCritical, "0.000")} * SE = {F(pooledCritical * decided.StandardError)}, z = {Signed(decided.Z, "0.000")}, limit |z| <= {F(pooledCritical, "0.000")}: **{Verdict(Math.Abs(decided.Z) <= pooledCritical ? SurveyVerdict.Pass : SurveyVerdict.Fail)}**");
        if (decided.StandardError == 0) Line("(no variance: every match of both surveys ended the same way, decided or not)");
        Line();

        // ---------------------------------------------------------------------------------------------------------------- b
        Line("### b. Win share per faction");
        Line();
        Line($"| faction | matches | wins {Rate(reference)} | share | wins {Rate(candidate)} | share | difference | SE | half-width | z | limit | verdict |");
        Line("|---|---|---|---|---|---|---|---|---|---|---|---|");
        var factionMatches = new Dictionary<string, int>(StringComparer.Ordinal);
        var factionShares = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var faction in factions)
        {
            var matches = reference.Rows.Count(row => row.FactionA == faction || row.FactionB == faction);
            long winsReference = reference.Rows.Count(row => row.Decided && row.WinnerFaction == faction), winsCandidate = candidate.Rows.Count(row => row.Decided && row.WinnerFaction == faction);
            var share = SurveyStatistics.TwoProportions(winsReference, matches, winsCandidate, matches);
            factionMatches[faction] = matches;
            factionShares[faction] = share.Share1;
            Line($"| {faction} | {N(matches)} | {N(winsReference)} | {F(share.Share1)} | {N(winsCandidate)} | {F(share.Share2)} | {Signed(share.Difference)} | {F(share.StandardError)} | {F(factionCritical * share.StandardError)} | {Signed(share.Z, "0.000")} | {F(factionCritical, "0.000")} | {Verdict(Math.Abs(share.Z) <= factionCritical ? SurveyVerdict.Pass : SurveyVerdict.Fail)} |");
        }
        Line();
        Line($"critical value: the {F(1 - factionAlpha / 2, "0.0000")} quantile of the normal distribution = {F(factionCritical, "0.0000")}. The tests of two factions that meet are not independent (a win of one is a loss of the other); Bonferroni holds under any dependence.");
        Line();

        // ---------------------------------------------------------------------------------------------------------------- c, e
        var strata = pairings.Select(pairing =>
        {
            var first = SurveyStatistics.Sample.Of([..reference.Rows.Where(row => row.Pairing == pairing && row.Decided).Select(row => row.GameMinutes)]);
            var second = SurveyStatistics.Sample.Of([..candidate.Rows.Where(row => row.Pairing == pairing && row.Decided).Select(row => row.GameMinutes)]);
            return (Pairing: pairing, Reference: first, Candidate: second, Testable: first.Count >= 2 && second.Count >= 2);
        }).ToArray();
        var testable = strata.Where(stratum => stratum.Testable).ToArray();
        var allReference = SurveyStatistics.Sample.Of([..reference.Rows.Where(row => row.Decided).Select(row => row.GameMinutes)]);
        var allCandidate = SurveyStatistics.Sample.Of([..candidate.Rows.Where(row => row.Decided).Select(row => row.GameMinutes)]);
        Line("### c. Duration of decided matches, stratified by pairing");
        Line();
        var durationError = double.NaN;
        if (testable.Length == 0)
        {
            Line($"No pairing has two decided matches at both rates: **{Verdict(SurveyVerdict.NotTestable)}**");
        }
        else
        {
            var meanDifference = testable.Sum(stratum => stratum.Candidate.Mean - stratum.Reference.Mean) / testable.Length;
            durationError = Math.Sqrt(testable.Sum(stratum => stratum.Reference.Variance / stratum.Reference.Count + stratum.Candidate.Variance / stratum.Candidate.Count)) / testable.Length;
            var within = Math.Abs(meanDifference) <= pooledCritical * durationError;
            Line($"m = {N(testable.Length)} of {N(tests)} pairings enter. D = {Signed(meanDifference, "0.000")} min, SE = {F(durationError, "0.000")} min, half-width {F(pooledCritical, "0.000")} * SE = {F(pooledCritical * durationError, "0.000")} min, limit |D| <= half-width: **{Verdict(within ? SurveyVerdict.Pass : SurveyVerdict.Fail)}**");
        }
        if (allReference.Count > 0 && allCandidate.Count > 0)
            Line($"Pooled mean duration (not stratified, for orientation only): {F(allReference.Mean, "0.000")} min over {N(allReference.Count)} matches at {Rate(reference)}, {F(allCandidate.Mean, "0.000")} min over {N(allCandidate.Count)} at {Rate(candidate)}, ratio {F(allCandidate.Mean / allReference.Mean)}.");
        Line();

        // ---------------------------------------------------------------------------------------------------------------- d
        Line("## Per pairing");
        Line();
        Line("### d. Winner distribution");
        Line();
        Line($"| pairing | {Rate(reference)} A / B / undecided | {Rate(candidate)} A / B / undecided | p | limit | verdict |");
        Line("|---|---|---|---|---|---|");
        foreach (var pairing in pairings)
        {
            int[] Counts(SurveyFile file) => [file.Rows.Count(row => row.Pairing == pairing && row.WinnerSlot == 1), file.Rows.Count(row => row.Pairing == pairing && row.WinnerSlot == 2), file.Rows.Count(row => row.Pairing == pairing && !row.Decided)];
            int[] first = Counts(reference), second = Counts(candidate);
            var p = SurveyStatistics.FisherFreemanHalton(first, second);
            Line($"| {pairing} | {N(first[0])} / {N(first[1])} / {N(first[2])} | {N(second[0])} / {N(second[1])} / {N(second[2])} | {F(p, "0.000000")} | {F(pairingAlpha, "0.000000")} | {Verdict(p >= pairingAlpha ? SurveyVerdict.Pass : SurveyVerdict.Fail)} |");
        }
        Line();
        Line("A is the first-named faction of the pairing, B the second.");
        Line();

        // ---------------------------------------------------------------------------------------------------------------- e
        Line("### e. Duration of decided matches");
        Line();
        Line($"| pairing | n {Rate(reference)} | mean min | sd | n {Rate(candidate)} | mean min | sd | difference | t | df | p | limit | verdict |");
        Line("|---|---|---|---|---|---|---|---|---|---|---|---|---|");
        foreach (var stratum in strata)
        {
            string Mean(SurveyStatistics.Sample sample) => sample.Count == 0 ? "-" : F(sample.Mean, "0.000");
            string Deviation(SurveyStatistics.Sample sample) => sample.Count < 2 ? "-" : F(sample.Deviation, "0.000");
            var head = $"| {stratum.Pairing} | {N(stratum.Reference.Count)} | {Mean(stratum.Reference)} | {Deviation(stratum.Reference)} | {N(stratum.Candidate.Count)} | {Mean(stratum.Candidate)} | {Deviation(stratum.Candidate)}";
            if (!stratum.Testable)
            {
                Line($"{head} | - | - | - | - | {F(pairingAlpha, "0.000000")} | {Verdict(SurveyVerdict.NotTestable)} |");
                continue;
            }
            var welch = SurveyStatistics.WelchTest(stratum.Reference, stratum.Candidate);
            Line($"{head} | {Signed(stratum.Candidate.Mean - stratum.Reference.Mean, "0.000")} | {(double.IsInfinity(welch.T) ? "inf" : Signed(welch.T, "0.000"))} | {F(welch.DegreesOfFreedom, "0.0")} | {F(welch.P, "0.000000")} | {F(pairingAlpha, "0.000000")} | {Verdict(welch.P >= pairingAlpha ? SurveyVerdict.Pass : SurveyVerdict.Fail)} |");
        }
        Line();

        // ------------------------------------------------------------------------------------------------------- false alarms
        Line("## What a failure means");
        Line();
        Line($"If the two rates had the same distributions, each of the five families (a, b, c, d, e) would raise a false alarm with a probability of at most {F(Alpha, "0.00")}: a and c are single tests at that level, b, d and e are corrected within the family (Bonferroni), so the expected number of failing lines is at most {F(Alpha, "0.00")} among the {N(factions.Length)} of b, {F(Alpha, "0.00")} among the {N(tests)} of d and {F(Alpha, "0.00")} among the {N(tests)} of e.");
        Line($"Over the whole report that is at most {F(5 * Alpha, "0.00")} expected false alarms, and at most a {F(5 * Alpha, "0.00")} probability of at least one (union bound; {F(1 - Math.Pow(1 - Alpha, 5))} if the families were independent). The families are not corrected against each other: one failing line is weak evidence, a failing family together with a consistent direction elsewhere is not.");
        var size = SurveyStatistics.ExactTestPower(perPairing[0], 0.5, 0.5, pairingAlpha);
        Line($"The exact test d is discrete and therefore stricter than its level: with {N(perPairing[0])} matches per rate and a true win share of 0.5 at both rates it rejects with probability {F(size, "0.000000")} per pairing (level {F(pairingAlpha, "0.000000")}).");
        Line();

        // -------------------------------------------------------------------------------------------- minimum detectable effect
        Line($"## Minimum detectable effects (power {F(Power, "0.00")})");
        Line();
        Line("What the comparison can see at this size; smaller true differences pass more often than not. Computed from the design and, where a spread is needed, from the reference survey.");
        Line();
        string Change(double baseline, long n, double alpha)
        {
            double up = SurveyStatistics.DetectableProportionChange(baseline, n, n, alpha, Power, true), down = SurveyStatistics.DetectableProportionChange(baseline, n, n, alpha, Power, false);
            return $"{(double.IsNaN(up) ? "not reachable" : "+" + F(up))} / {(double.IsNaN(down) ? "not reachable" : "-" + F(down))}";
        }
        Line("| test | n per rate | level | baseline | detectable change (up / down) |");
        Line("|---|---|---|---|---|");
        Line($"| a. decided share | {N(total)} | {F(Alpha, "0.0000")} | 0.5000 (widest spread) | {Change(0.5, total, Alpha)} |");
        Line($"| a. decided share | {N(total)} | {F(Alpha, "0.0000")} | {F(decided.Share1)} (reference) | {Change(decided.Share1, total, Alpha)} |");
        foreach (var faction in factions)
            Line($"| b. win share {faction} | {N(factionMatches[faction])} | {F(factionAlpha, "0.0000")} | {F(factionShares[faction])} (reference) | {Change(factionShares[faction], factionMatches[faction], factionAlpha)} |");
        foreach (var matches in factionMatches.Values.Distinct().Order())
            Line($"| b. win share, any faction with {N(matches)} matches | {N(matches)} | {F(factionAlpha, "0.0000")} | 0.5000 (widest spread) | {Change(0.5, matches, factionAlpha)} |");
        Line();
        Line("Normal approximation of the two-proportion test, solved for the change.");
        Line();
        var n = perPairing[0];
        Line($"d. Winner distribution of one pairing, {N(n)} matches per rate, level {F(pairingAlpha, "0.000000")}, exact power by enumerating every pair of outcomes (two outcomes; a third category only spreads the counts further):");
        Line();
        Line("| share at the reference rate | smallest share at the candidate rate found with the power | change |");
        Line("|---|---|---|");
        foreach (var baseline in new[] { 0.5, 0.25, 0.1, 0.0 })
        {
            var found = double.NaN;
            for (var step = 1; step <= 200 && double.IsNaN(found); step++)
            {
                var other = baseline + step * 0.005;
                if (other > 1 + 1e-12) break;
                if (SurveyStatistics.ExactTestPower(n, baseline, Math.Min(1, other), pairingAlpha) >= Power) found = Math.Min(1, other);
            }
            Line($"| {F(baseline, "0.000")} | {(double.IsNaN(found) ? "not reachable" : F(found, "0.000"))} | {(double.IsNaN(found) ? "-" : "+" + F(found - baseline, "0.000"))} |");
        }
        Line();
        Line("By symmetry the same changes hold downwards from 0.5, 0.75, 0.9 and 1.0 (searched in steps of 0.005).");
        Line();
        Line($"e. Duration of one pairing, level {F(pairingAlpha, "0.000000")}, in standard deviations of the match duration within the pairing ((t_(1-alpha/2) + t_power) * sqrt(2/n), n - 1 + n - 1 degrees of freedom):");
        Line();
        Line("| decided matches per rate | detectable difference in sd |" + (testable.Length > 0 ? " in game minutes (pooled sd within the pairings of the reference) |" : string.Empty));
        Line("|---|---|" + (testable.Length > 0 ? "---|" : string.Empty));
        var withinFreedom = strata.Where(stratum => stratum.Reference.Count >= 2).Sum(stratum => stratum.Reference.Count - 1);
        var withinDeviation = withinFreedom > 0 ? Math.Sqrt(strata.Where(stratum => stratum.Reference.Count >= 2).Sum(stratum => (stratum.Reference.Count - 1) * stratum.Reference.Variance) / withinFreedom) : double.NaN;
        foreach (var count in new[] { n, n / 2, n / 4 }.Where(count => count >= 2).Distinct())
        {
            var effect = SurveyStatistics.DetectableMeanDifference(count, count, pairingAlpha, Power);
            Line($"| {N(count)} | {F(effect, "0.000")} |" + (testable.Length > 0 ? $" {F(effect * withinDeviation, "0.000")} |" : string.Empty));
        }
        Line();
        var both = SurveyStatistics.NormalQuantile(1 - Alpha / 2) + SurveyStatistics.NormalQuantile(Power);
        Line($"c. Stratified duration: detectable |D| = ({F(pooledCritical, "0.000")} + {F(SurveyStatistics.NormalQuantile(Power), "0.000")}) * SE = {F(both, "0.000")} * SE. If all {N(tests)} pairings had {N(n)} decided matches at both rates and one standard deviation sd: SE = sd * sqrt(2 / ({N(n)} * {N(tests)})) = {F(Math.Sqrt(2.0 / (n * tests)))} sd, detectable {F(both * Math.Sqrt(2.0 / (n * tests)), "0.000")} sd.");
        if (!double.IsNaN(durationError) && allReference.Count > 0)
            Line($"With the SE of these data ({F(durationError, "0.000")} min): detectable |D| = {F(both * durationError, "0.000")} min, {F(100 * both * durationError / allReference.Mean, "0.0")} % of the pooled mean duration of the reference.");
        Line();
        Line("Limits of the method: seeds are paired by number only (the same seed at two rates is not the same match), so the tests treat the two surveys as independent samples; undecided matches carry no duration (a shift of long matches beyond the budget shows in a and d, not in c and e); a pass means \"no difference of at least the sizes above was found\", not \"the rates are equal\".");
        Line();

        Line("## Verdict");
        Line();
        Line($"{N(passed)} passed, {N(failed)} failed, {N(untestable)} not testable. Exit code {N(failed == 0 ? 0 : 1)} (1 when any test failed; a test that is not testable is listed above and is neither a pass nor a failure).");
        return new Result(text.ToString(), passed, failed, untestable);
    }

    private static string N(long value) => value.ToString(CultureInfo.InvariantCulture);
    private static string F(double value, string format = "0.0000") => value.ToString(format, CultureInfo.InvariantCulture);
    private static string Signed(double value, string format = "0.0000") => (value < 0 ? "-" : "+") + Math.Abs(value).ToString(format, CultureInfo.InvariantCulture);
}
