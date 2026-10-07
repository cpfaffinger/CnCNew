using System.Globalization;
using CnCNew.SimTool.Survey;

// The statistics of `SimTool rate-distribution` (docs/SIMULATION-CORE.md §40) against values that follow from closed forms or from counting by hand.
static partial class Tests
{
    private static void SurveyClose(double actual, double expected, double tolerance, string what)
        => Check.That(Math.Abs(actual - expected) <= tolerance, $"{what}: {actual.ToString("R", CultureInfo.InvariantCulture)} instead of {expected.ToString("R", CultureInfo.InvariantCulture)}.");

    public static void RateSurveyNormalDistribution()
    {
        SurveyClose(SurveyStatistics.NormalUpperTail(0), 0.5, 1e-15, "Q(0)");
        SurveyClose(SurveyStatistics.NormalUpperTail(1), 0.15865525393145707, 1e-14, "Q(1)");
        SurveyClose(SurveyStatistics.NormalUpperTail(1.96), 0.024997895148220435, 1e-14, "Q(1.96)");
        SurveyClose(SurveyStatistics.NormalUpperTail(-1.96), 1 - 0.024997895148220435, 1e-14, "Q(-1.96)");
        // The series below 3 and the continued fraction from 3 on meet.
        SurveyClose(SurveyStatistics.NormalUpperTail(3), 0.0013498980316300946, 1e-15, "Q(3)");
        SurveyClose(SurveyStatistics.NormalUpperTail(Math.BitDecrement(3.0)), 0.0013498980316300946, 1e-15, "Q(3 - ulp)");
        SurveyClose(SurveyStatistics.NormalUpperTail(6) / 9.865876450376946e-10, 1, 1e-10, "Q(6), relative");
        // Independent of any quoted value: the tail is the integral of the density (Simpson's rule on [x, x + 12]).
        foreach (var x in new[] { 0.3, 1.7, 2.9, 3.1, 4.5 })
        {
            const int Steps = 24_000;
            var h = 12.0 / Steps;
            var sum = SurveyStatistics.NormalDensity(x) + SurveyStatistics.NormalDensity(x + 12);
            for (var step = 1; step < Steps; step++) sum += (step % 2 == 1 ? 4 : 2) * SurveyStatistics.NormalDensity(x + step * h);
            SurveyClose(SurveyStatistics.NormalUpperTail(x), sum * h / 3, 1e-12, $"Q({x.ToString(CultureInfo.InvariantCulture)}) against the integral");
        }

        SurveyClose(SurveyStatistics.NormalQuantile(0.975), 1.959963984540054, 1e-12, "quantile 0.975");
        SurveyClose(SurveyStatistics.NormalQuantile(0.995), 2.5758293035489004, 1e-12, "quantile 0.995");
        SurveyClose(SurveyStatistics.NormalQuantile(0.9), 1.2815515655446004, 1e-12, "quantile 0.9");
        SurveyClose(SurveyStatistics.NormalQuantile(0.025), -1.959963984540054, 1e-12, "quantile 0.025");
        SurveyClose(SurveyStatistics.NormalQuantile(0.5), 0, 1e-12, "quantile 0.5");
        // The corrected levels of the report: the quantile is the inverse of the distribution function.
        foreach (var k in new[] { 1, 3, 5, 16, 32 })
        {
            var p = 1 - 0.05 / (2 * k);
            SurveyClose(SurveyStatistics.NormalCdf(SurveyStatistics.NormalQuantile(p)), p, 1e-14, $"round trip for {k} tests");
        }
        Check.Throws<ArgumentOutOfRangeException>(() => SurveyStatistics.NormalQuantile(1), "between 0 and 1");
    }

    public static void RateSurveyBetaAndStudent()
    {
        SurveyClose(SurveyStatistics.LogGamma(1), 0, 1e-13, "ln Gamma(1)");
        SurveyClose(SurveyStatistics.LogGamma(5), Math.Log(24), 1e-13, "ln Gamma(5)");
        SurveyClose(SurveyStatistics.LogGamma(0.5), 0.5 * Math.Log(Math.PI), 1e-13, "ln Gamma(1/2)");
        // Gamma(10.5) = 19!! / 2^10 * sqrt(pi), 19!! = 654,729,075.
        SurveyClose(SurveyStatistics.LogGamma(10.5), Math.Log(654_729_075.0 / 1024) + 0.5 * Math.Log(Math.PI), 1e-12, "ln Gamma(10.5)");
        SurveyClose(SurveyStatistics.LogGamma(21), Enumerable.Range(1, 20).Sum(value => Math.Log(value)), 1e-11, "ln Gamma(21)");

        foreach (var x in new[] { 0.01, 0.2, 0.5, 0.77, 0.99 })
        {
            var text = x.ToString(CultureInfo.InvariantCulture);
            SurveyClose(SurveyStatistics.RegularizedBeta(x, 1, 1), x, 1e-13, $"I_{text}(1, 1)");
            SurveyClose(SurveyStatistics.RegularizedBeta(x, 3.5, 1), Math.Pow(x, 3.5), 1e-13, $"I_{text}(3.5, 1)");
            SurveyClose(SurveyStatistics.RegularizedBeta(x, 1, 4.25), 1 - Math.Pow(1 - x, 4.25), 1e-13, $"I_{text}(1, 4.25)");
            // 12 * integral of t (1 - t)^2.
            SurveyClose(SurveyStatistics.RegularizedBeta(x, 2, 3), 6 * x * x - 8 * x * x * x + 3 * x * x * x * x, 1e-13, $"I_{text}(2, 3)");
            SurveyClose(SurveyStatistics.RegularizedBeta(x, 0.5, 0.5), 2 / Math.PI * Math.Asin(Math.Sqrt(x)), 1e-12, $"I_{text}(1/2, 1/2)");
            SurveyClose(SurveyStatistics.RegularizedBeta(x, 7.3, 2.6) + SurveyStatistics.RegularizedBeta(1 - x, 2.6, 7.3), 1, 1e-13, $"symmetry at {text}");
        }
        SurveyClose(SurveyStatistics.RegularizedBeta(0.5, 9.75, 9.75), 0.5, 1e-13, "I_0.5(a, a)");
        Check.That(SurveyStatistics.RegularizedBeta(0, 2, 3) == 0 && SurveyStatistics.RegularizedBeta(1, 2, 3) == 1, "I_0 = 0 and I_1 = 1.");

        foreach (var t in new[] { 0.0, 0.3, 1.0, 2.5, 12.706204736174694, 40.0 })
        {
            var text = t.ToString(CultureInfo.InvariantCulture);
            // One degree of freedom is the Cauchy distribution, two and four have closed forms as well.
            SurveyClose(SurveyStatistics.StudentTwoSided(t, 1), 1 - 2 / Math.PI * Math.Atan(t), 1e-12, $"p(t = {text}, 1 df)");
            SurveyClose(SurveyStatistics.StudentTwoSided(t, 2), 1 - t / Math.Sqrt(2 + t * t), 1e-12, $"p(t = {text}, 2 df)");
            var u = t / Math.Sqrt(1 + t * t / 4);
            SurveyClose(SurveyStatistics.StudentTwoSided(t, 4), 1 - 0.75 * u * (1 - u * u / 12), 1e-12, $"p(t = {text}, 4 df)");
            SurveyClose(SurveyStatistics.StudentTwoSided(-t, 4), SurveyStatistics.StudentTwoSided(t, 4), 0, "the sign of t");
        }
        SurveyClose(SurveyStatistics.StudentTwoSided(12.706204736174694, 1), 0.05, 1e-12, "the 97.5 % point of 1 df");
        SurveyClose(SurveyStatistics.StudentTwoSided(2.2281388519649385, 10), 0.05, 1e-9, "the 97.5 % point of 10 df");
        SurveyClose(SurveyStatistics.StudentTwoSided(1.959963984540054, 1e8), 0.05, 1e-7, "many degrees of freedom approach the normal distribution");
        SurveyClose(SurveyStatistics.StudentTwoSided(1.3, 7.4), (SurveyStatistics.StudentTwoSided(1.3, 7.3) + SurveyStatistics.StudentTwoSided(1.3, 7.5)) / 2, 1e-5, "fractional degrees of freedom lie between their neighbours");
        SurveyClose(SurveyStatistics.StudentCritical(0.05, 10), 2.2281388519649385, 1e-8, "critical t of 10 df");
        SurveyClose(SurveyStatistics.StudentCritical(0.05, 2), 4.302652729911275, 1e-9, "critical t of 2 df");
        SurveyClose(SurveyStatistics.StudentTwoSided(SurveyStatistics.StudentCritical(0.05 / 16, 38), 38), 0.05 / 16, 1e-13, "critical value round trip");
    }

    public static void RateSurveyExactTestAndTwoSampleTests()
    {
        // Rows 2 and 2, columns 1, 1, 2: four tables with 1/6, 1/3, 1/3, 1/6.
        SurveyClose(SurveyStatistics.FisherFreemanHalton([1, 1, 0], [0, 0, 2]), 1.0 / 3, 1e-13, "2 x 3 table, the rarest table");
        SurveyClose(SurveyStatistics.FisherFreemanHalton([0, 0, 2], [1, 1, 0]), 1.0 / 3, 1e-13, "2 x 3 table, rows swapped");
        SurveyClose(SurveyStatistics.FisherFreemanHalton([1, 0, 1], [0, 1, 1]), 1, 1e-13, "2 x 3 table, the most probable table");
        // An empty column leaves Fisher's 2 x 2 test: 1, 9, 9, 1 of 20.
        SurveyClose(SurveyStatistics.FisherFreemanHalton([3, 0, 0], [0, 3, 0]), 0.1, 1e-13, "3 against 3");
        // 2 x 2 with a tie on the far side: (3640 + 91 + 3640 + 91) / C(24, 12).
        SurveyClose(SurveyStatistics.FisherFreemanHalton([1, 9], [11, 3]), 7462.0 / 2_704_156, 1e-13, "Fisher's test with a tie");
        SurveyClose(SurveyStatistics.FisherFreemanHalton([7, 9, 4], [7, 9, 4]), 1, 1e-12, "equal rows");
        SurveyClose(SurveyStatistics.FisherFreemanHalton([0, 0, 0], [0, 0, 0]), 1, 0, "an empty table");
        // 20 wins of A against 20 wins of B: one table of C(40, 20) on each side.
        SurveyClose(SurveyStatistics.FisherFreemanHalton([20, 0, 0], [0, 20, 0]) * 137_846_528_820.0, 2, 1e-9, "20 against 20");
        // The probabilities of all tables sum to 1: a table that is the most probable one gets p = 1 (rows 20 and 20, columns 14, 18, 8).
        SurveyClose(SurveyStatistics.FisherFreemanHalton([7, 9, 4], [7, 9, 4]), 1, 1e-12, "sum over every table");
        Check.That(SurveyStatistics.FisherFreemanHalton([12, 6, 2], [5, 6, 9]) is > 0.01 and < 0.05, "A moderate shift over three columns.");
        Check.Throws<ArgumentException>(() => SurveyStatistics.FisherFreemanHalton([1, 2], [1, 2, 3]), "same number");

        var proportions = SurveyStatistics.TwoProportions(30, 100, 45, 100);
        SurveyClose(proportions.Difference, 0.15, 1e-15, "difference of two shares");
        SurveyClose(proportions.StandardError, Math.Sqrt(0.375 * 0.625 * 0.02), 1e-15, "pooled standard error");
        SurveyClose(proportions.Z, 2.19089, 1e-5, "z of 30/100 against 45/100");
        var none = SurveyStatistics.TwoProportions(20, 20, 20, 20);
        Check.That(none.Z == 0 && none.StandardError == 0, "Two samples without variance have no difference.");

        // {1..5}: mean 3, variance 2.5; {4, 6, 8, 10}: mean 7, variance 20/3. t = 4 / sqrt(0.5 + 5/3), df = (13/6)^2 / (0.25/4 + (25/9)/3).
        var first = SurveyStatistics.Sample.Of([1, 2, 3, 4, 5]);
        var second = SurveyStatistics.Sample.Of([4, 6, 8, 10]);
        SurveyClose(first.Variance, 2.5, 1e-14, "variance");
        SurveyClose(second.Variance, 20.0 / 3, 1e-14, "variance");
        var welch = SurveyStatistics.WelchTest(first, second);
        SurveyClose(welch.T, 4 / Math.Sqrt(13.0 / 6), 1e-13, "Welch t");
        SurveyClose(welch.DegreesOfFreedom, 169.0 / 36 / (0.0625 + 25.0 / 27), 1e-12, "Welch-Satterthwaite");
        SurveyClose(welch.DegreesOfFreedom, 4.7494, 1e-4, "Welch-Satterthwaite, to four places by hand");
        Check.That(welch.P > SurveyStatistics.StudentTwoSided(welch.T, 5) && welch.P < SurveyStatistics.StudentTwoSided(welch.T, 4), "The p of 4.75 degrees of freedom lies between those of 4 and 5.");
        // Equal variances and sizes: Welch is the pooled t test with 2n - 2 degrees of freedom.
        var shifted = SurveyStatistics.WelchTest(first, SurveyStatistics.Sample.Of([2, 3, 4, 5, 6]));
        SurveyClose(shifted.DegreesOfFreedom, 8, 1e-12, "equal samples");
        SurveyClose(shifted.T, 1, 1e-13, "a shift of one standard error");
        SurveyClose(shifted.P, SurveyStatistics.StudentTwoSided(1, 8), 0, "p of the shift");
        var constant = SurveyStatistics.WelchTest(SurveyStatistics.Sample.Of([3, 3]), SurveyStatistics.Sample.Of([3, 3]));
        Check.That(constant.P == 1 && SurveyStatistics.WelchTest(SurveyStatistics.Sample.Of([3, 3]), SurveyStatistics.Sample.Of([4, 4])).P == 0, "Samples without variance: equal means pass, different ones do not.");
        Check.Throws<ArgumentException>(() => SurveyStatistics.WelchTest(first, SurveyStatistics.Sample.Of([1])), "two values");

        // Power: the textbook approximation (z_alpha/2 + z_power) * sqrt(2 p q / n) is 0.1107 for 320 matches at 0.5; the solved value accounts for the variance at the shifted share.
        var change = SurveyStatistics.DetectableProportionChange(0.5, 320, 320, 0.05, 0.8, true);
        Check.That(change is > 0.105 and < 0.1107, $"Detectable change of a share at n = 320: {change.ToString(CultureInfo.InvariantCulture)}.");
        SurveyClose(SurveyStatistics.DetectableProportionChange(0.5, 320, 320, 0.05, 0.8, false), change, 1e-9, "symmetric around 0.5");
        Check.That(double.IsNaN(SurveyStatistics.DetectableProportionChange(1, 320, 320, 0.05, 0.8, true)), "No change upwards from 1.");
        Check.That(double.IsNaN(SurveyStatistics.DetectableProportionChange(0.5, 4, 4, 0.05 / 16, 0.8, true)), "Four matches cannot show a change at a corrected level.");
        SurveyClose(SurveyStatistics.ExactTestPower(20, 0, 1, 0.05 / 16), 1, 1e-12, "power against the opposite outcome");
        foreach (var share in new[] { 0.1, 0.5, 0.8 })
            Check.That(SurveyStatistics.ExactTestPower(20, share, share, 0.05 / 16) <= 0.05 / 16, "The exact test keeps its level.");
        Check.That(SurveyStatistics.ExactTestPower(20, 0.5, 0.9, 0.05 / 16) < SurveyStatistics.ExactTestPower(20, 0.5, 1, 0.05 / 16), "Power grows with the difference.");
        // (t_0.975 + t_0.8) * sqrt(2 / n) with 2 df for two pairs: (4.302653 + 1.060660) * 1.
        SurveyClose(SurveyStatistics.DetectableMeanDifference(2, 2, 0.05, 0.8), 4.302652729911275 + 1.0606601717798212, 1e-8, "detectable mean difference, 2 against 2");
    }

    private static SurveyRow SurveyMatch(string pairing, uint seed, int rate, int winnerSlot, double minutes)
    {
        var endTick = (long)(minutes * 60 * rate);
        return new SurveyRow(pairing, $"Maps/{pairing}.map", "FactionOne", pairing.StartsWith('M') ? "FactionTwo" : "FactionThree", seed, rate, winnerSlot == 0 ? SurveyRow.Stalemate : SurveyRow.Winner,
            winnerSlot == 0 ? "-" : winnerSlot == 1 ? "FactionOne" : pairing.StartsWith('M') ? "FactionTwo" : "FactionThree", winnerSlot, endTick, endTick * 1000 / rate, endTick + 300, 1234, "00000000DEADBEEF", 900, 950, -1, 4000);
    }

    /// <summary>Two pairings with ten seeds each. <paramref name="shift"/> moves durations and winners of the first pairing.</summary>
    private static List<SurveyRow> SyntheticSurvey(int rate, bool shift)
    {
        var rows = new List<SurveyRow>();
        for (uint seed = 1; seed <= 10; seed++)
        {
            // M: A wins seven of ten in about ten minutes, one undecided; shifted: B wins every match after about twenty.
            rows.Add(shift ? SurveyMatch("M_One-Two", seed, rate, 2, 20 + seed % 3) : SurveyMatch("M_One-Two", seed, rate, seed == 10 ? 0 : seed <= 7 ? 1 : 2, seed == 10 ? 25 : 10 + seed % 3));
            // N: undecided but once.
            rows.Add(SurveyMatch("N_One-Three", seed, rate, seed == 4 ? 2 : 0, seed == 4 ? 12 : 25));
        }
        return rows;
    }

    public static void RateSurveyFilesAndReport()
    {
        var text = SurveyFile.Write(SyntheticSurvey(30, false).AsEnumerable().Reverse());
        Check.That(!text.Contains('\r') && text.EndsWith('\n') && text.StartsWith("pairing\tmap\tfactionA\tfactionB\tseed\ttickRate\toutcome\t", StringComparison.Ordinal), "A survey is tab separated with LF line ends and a header.");
        var reference = SurveyFile.Read(text, "a.tsv");
        Check.That(reference.Rows.Count == 20 && reference.TickRate == 30 && reference.Rows[0] is { Pairing: "M_One-Two", Seed: 1 } && reference.Rows[19] is { Pairing: "N_One-Three", Seed: 10 }, "Rows are sorted by pairing and seed whatever the order they were written in.");
        Check.That(SurveyFile.Write(reference.Rows) == text && SurveyFile.Write(SyntheticSurvey(30, false)) == text, "Writing what was read gives the same text.");
        Check.That(SurveyFile.Read(text.Replace("\n", "\r\n", StringComparison.Ordinal), "crlf.tsv").Rows.Count == 20, "CRLF line ends are read.");
        Check.That(reference.Rows[9] is { Outcome: "stalemate", WinnerSlot: 0, EndGameMilliseconds: 1_500_000 } && Math.Abs(reference.Rows[0].GameMinutes - 11) < 1e-12, "Fields survive.");

        Check.Throws<FormatException>(() => SurveyFile.Read("pairing\tseed\n", "short.tsv"), "header");
        Check.Throws<FormatException>(() => SurveyFile.Read(text + text[(text.IndexOf('\n') + 1)..], "twice.tsv"), "listed twice");
        Check.Throws<FormatException>(() => SurveyFile.Read(text.Replace("\t30\twinner\t", "\t30\tdraw\t", StringComparison.Ordinal), "outcome.tsv"), "unknown outcome");
        Check.Throws<FormatException>(() => SurveyFile.Read(text.Replace("\twinner\tFactionOne\t1\t", "\twinner\tFactionOne\t2\t", StringComparison.Ordinal), "slot.tsv"), "winner faction");
        Check.Throws<FormatException>(() => SurveyFile.Read(text.Replace("\t30\tstalemate\t-\t0\t", "\t30\tfailed\t-\t0\t", StringComparison.Ordinal), "failed.tsv"), "failed");
        Check.Throws<FormatException>(() => SurveyFile.Read(text.Replace("\t1234\t", "\t12x4\t", StringComparison.Ordinal), "number.tsv"), "number");
        Check.Throws<FormatException>(() => SurveyFile.Read(text[..text.IndexOf('\n')] + "\n", "empty.tsv"), "no matches");
        var mixed = SyntheticSurvey(30, false);
        mixed[3] = SurveyMatch(mixed[3].Pairing, mixed[3].Seed, 60, 0, 25);
        Check.Throws<FormatException>(() => SurveyFile.Read(SurveyFile.Write(mixed), "mixed.tsv"), "tick rate");

        // Mismatched sets are refused: a seed missing on one side, another seed, another pairing, another map.
        var candidate = SurveyFile.Read(SurveyFile.Write(SyntheticSurvey(60, false)), "b.tsv");
        SurveyFile.RequireSameDesign(reference, candidate);
        var fewer = SurveyFile.Read(SurveyFile.Write(SyntheticSurvey(60, false).Where(row => row is not { Pairing: "N_One-Three", Seed: 7 })), "fewer.tsv");
        Check.Throws<FormatException>(() => SurveyFile.RequireSameDesign(reference, fewer), "N_One-Three seed 7 is only in the reference");
        Check.Throws<FormatException>(() => SurveyFile.RequireSameDesign(fewer, reference), "N_One-Three seed 7 is only in the candidate");
        Check.Throws<FormatException>(() => RateDistribution.Compare(reference, fewer), "only in the reference");
        var otherSeeds = SurveyFile.Read(SurveyFile.Write(SyntheticSurvey(60, false).Select(row => row with { Seed = row.Seed + 10 })), "seeds.tsv");
        Check.Throws<FormatException>(() => SurveyFile.RequireSameDesign(reference, otherSeeds), "seed 1 is only in the reference");
        var otherMap = SurveyFile.Read(SurveyFile.Write(SyntheticSurvey(60, false).Select(row => row with { Map = "Maps/Other.map" })), "map.tsv");
        Check.Throws<FormatException>(() => SurveyFile.RequireSameDesign(reference, otherMap), "another map");

        // The same distribution at both rates: nothing fails; the pairing without decided matches is not testable and is not counted as passed.
        var same = RateDistribution.Compare(reference, candidate);
        Check.That(same.Failed == 0 && same.ExitCode == 0, "Equal surveys pass:\n" + same.Report);
        // a, three factions, c, two winner tables and one duration pass; the duration of N (one decided match) is not testable.
        Check.That(same.Passed == 1 + 3 + 1 + 2 + 1 && same.NotTestable == 1, $"Counted {same.Passed} passed, {same.NotTestable} not testable.");
        Check.That(same.Report.Contains("| N_One-Three | 1 | 12.000 | - | 1 | 12.000 | - | - | - | - | - | 0.025000 | NOT TESTABLE |", StringComparison.Ordinal), "A pairing with one decided match is reported as not testable.");
        Check.That(same.Report.Contains("pass if |z| <= 1.960.", StringComparison.Ordinal) && same.Report.Contains("Bonferroni over 3 factions (alpha = 0.0167 each); pass if every |z| <= 2.394.", StringComparison.Ordinal)
            && same.Report.Contains("Bonferroni over 2 pairings; pass if p >= 0.025000", StringComparison.Ordinal), "The limits stand in the header of the report.");
        Check.That(same.Report.Contains("| 30 Hz | 10 | 20 | 0.5000 |", StringComparison.Ordinal) && same.Report.Contains("| M_One-Two | 7 / 2 / 1 | 7 / 2 / 1 | 1.000000 | 0.025000 | PASS |", StringComparison.Ordinal), "Counts of the report.");
        Check.That(same.Report == RateDistribution.Compare(reference, candidate).Report && !same.Report.Contains('\r'), "The report is reproducible and uses LF.");

        // A candidate in which the second faction wins every match of M after twice the time fails where it should.
        var shifted = RateDistribution.Compare(reference, SurveyFile.Read(SurveyFile.Write(SyntheticSurvey(60, true)), "c.tsv"));
        Check.That(shifted.ExitCode == 1 && shifted.Failed >= 4, $"A shifted survey fails ({shifted.Failed} failures):\n" + shifted.Report);
        Check.That(shifted.Report.Contains("| M_One-Two | 7 / 2 / 1 | 0 / 10 / 0 | 0.000", StringComparison.Ordinal) && shifted.Report.Contains("| FAIL |", StringComparison.Ordinal), "The winner table of the shifted pairing fails.");
        // c by hand: one stratum; reference durations of the nine decided matches 11, 12, 10, 11, 12, 10, 11, 12, 10 (mean 11, variance 0.75), candidate
        // 21, 22, 20, 21, 22, 20, 21, 22, 20, 21 (mean 21, variance 2/3): D = +10, SE = sqrt(0.75 / 9 + (2/3) / 10) = 0.387.
        Check.That(shifted.Report.Contains("m = 1 of 2 pairings enter. D = +10.000 min, SE = 0.387 min, half-width 1.960 * SE = 0.759 min, limit |D| <= half-width: **FAIL**", StringComparison.Ordinal), "The stratified duration test:\n" + shifted.Report);
    }
}
