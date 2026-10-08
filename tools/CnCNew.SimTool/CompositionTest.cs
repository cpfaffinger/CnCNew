using System.Globalization;
using System.Text;
using CnCNew.SimTool.Survey;

namespace CnCNew.SimTool;

/// <summary>
/// <c>composition-test</c>: the acceptance test of an army composition rule, fixed before the surveys are played (<c>docs/AI-OBSERVED-2026-10-08.md</c>).
/// <list type="number">
/// <item>Paired: two surveys of the same pairings and seeds, in one the first player's AI uses the rule and the second's does not, in the other the seats are swapped. The
/// AI with the rule must win more of the decided matches than the one without: an exact two-sided binomial test of its wins among the decided matches against one half,
/// at α = 0.05, with the exact (Clopper–Pearson) 95 % interval of its win share.</item>
/// <item>Symmetric: a survey with the rule on both sides. Its share of decided matches must not be lower than the baseline's by more than the half-width of the 95 %
/// interval of the difference of two proportions.</item>
/// </list>
/// Reads survey files only; statistics in double precision (a tool, not the simulation).
/// </summary>
internal static class CompositionTest
{
    private const string UsageText = """
        Usage: CnCNew.SimTool composition-test --rule-first <tsv> --rule-second <tsv> --symmetric <tsv> --baseline <decided>/<matches> [--out <report.md>]
          --rule-first:  survey with `--composition exchange,uniform`;  --rule-second: the same pairings and seeds with `--composition uniform,exchange`
          --symmetric:   survey with `--composition exchange,exchange`; --baseline: decided matches of the survey without the rule (e.g. 253/320)
        """;

    public static int Run(string[] args)
    {
        var first = ManifestCommands.Option(args, "--rule-first");
        var second = ManifestCommands.Option(args, "--rule-second");
        var symmetric = ManifestCommands.Option(args, "--symmetric");
        var baseline = ManifestCommands.Option(args, "--baseline")?.Split('/');
        if (first is null || second is null || symmetric is null || baseline is not { Length: 2 }) return ManifestCommands.Fail(UsageText);
        var invariant = CultureInfo.InvariantCulture;
        var ruleFirst = SurveyFile.Read(File.ReadAllText(first), first);
        var ruleSecond = SurveyFile.Read(File.ReadAllText(second), second);
        var both = SurveyFile.Read(File.ReadAllText(symmetric), symmetric);
        var keysFirst = ruleFirst.Rows.Select(row => (row.Pairing, row.Seed)).ToHashSet();
        if (!keysFirst.SetEquals(ruleSecond.Rows.Select(row => (row.Pairing, row.Seed)))) return ManifestCommands.Fail("The two paired surveys do not cover the same pairings and seeds.");

        int wins = 0, losses = 0, undecided = 0;
        var perPairing = new SortedDictionary<string, (int Wins, int Losses, int Undecided)>(StringComparer.Ordinal);
        void Count(SurveyRow row, int ruleSlot)
        {
            var entry = perPairing.GetValueOrDefault(row.Pairing);
            if (!row.Decided) { undecided++; entry.Undecided++; }
            else if (row.WinnerSlot == ruleSlot) { wins++; entry.Wins++; }
            else { losses++; entry.Losses++; }
            perPairing[row.Pairing] = entry;
        }
        foreach (var row in ruleFirst.Rows) Count(row, 1);
        foreach (var row in ruleSecond.Rows) Count(row, 2);

        var decided = wins + losses;
        var pValue = BinomialTwoSided(wins, decided);
        var (low, high) = ClopperPearson(wins, decided);
        var share = decided == 0 ? 0 : (double)wins / decided;
        var pairedPass = decided > 0 && wins > losses && pValue < 0.05;

        var baseDecided = int.Parse(baseline[0], invariant);
        var baseMatches = int.Parse(baseline[1], invariant);
        var ruleDecided = both.Rows.Count(row => row.Decided);
        var ruleMatches = both.Rows.Count;
        var p1 = (double)baseDecided / baseMatches;
        var p2 = (double)ruleDecided / ruleMatches;
        var halfWidth = 1.959964 * Math.Sqrt(p1 * (1 - p1) / baseMatches + p2 * (1 - p2) / ruleMatches);
        var symmetricPass = p2 >= p1 - halfWidth;

        var report = new StringBuilder();
        report.Append(invariant, $"# Composition rule: acceptance test\n\n");
        report.Append(invariant, $"## 1. Paired survey ({ruleFirst.Rows.Count + ruleSecond.Rows.Count} matches: the rule on one side, both seat orders)\n\n");
        report.Append(invariant, $"- AI with the rule: **{wins} wins, {losses} losses**, {undecided} undecided\n");
        report.Append(invariant, $"- win share among the {decided} decided matches: {share:P1}, exact 95 % interval [{low:P1}, {high:P1}]\n");
        report.Append(invariant, $"- exact two-sided binomial test against one half: p = {pValue:G4}\n");
        report.Append(invariant, $"- criterion (more wins than losses and p < 0.05): **{(pairedPass ? "PASSED" : "FAILED")}**\n\n");
        report.Append("| pairing | wins with the rule | losses | undecided |\n|---|---|---|---|\n");
        foreach (var (pairing, entry) in perPairing) report.Append(invariant, $"| {pairing} | {entry.Wins} | {entry.Losses} | {entry.Undecided} |\n");
        report.Append(invariant, $"\n## 2. Symmetric survey ({ruleMatches} matches: the rule on both sides)\n\n");
        report.Append(invariant, $"- decided: **{ruleDecided} of {ruleMatches}** ({p2:P1}); baseline without the rule: {baseDecided} of {baseMatches} ({p1:P1})\n");
        report.Append(invariant, $"- half-width of the 95 % interval of the difference of the two shares: {halfWidth:P1}\n");
        report.Append(invariant, $"- criterion (share with the rule not below {p1 - halfWidth:P1}): **{(symmetricPass ? "PASSED" : "FAILED")}**\n\n");
        report.Append(invariant, $"## Verdict\n\n{(pairedPass && symmetricPass ? "Both criteria passed." : "Not passed: the rule stays behind its option and is off by default.")}\n");
        Console.Write(report.ToString());
        if (ManifestCommands.Option(args, "--out") is { } path) File.WriteAllText(Path.GetFullPath(path), report.ToString(), new UTF8Encoding(false));
        return pairedPass && symmetricPass ? 0 : 1;
    }

    private static double LogChoose(int n, int k)
    {
        double sum = 0;
        for (var index = 1; index <= k; index++) sum += Math.Log(n - k + index) - Math.Log(index);
        return sum;
    }

    private static double Pmf(int k, int n, double p)
        => p <= 0 ? (k == 0 ? 1 : 0) : p >= 1 ? (k == n ? 1 : 0) : Math.Exp(LogChoose(n, k) + k * Math.Log(p) + (n - k) * Math.Log(1 - p));

    /// <summary>The exact two-sided binomial test against p = 1/2: the probability of every outcome that is at most as likely as the observed one.</summary>
    internal static double BinomialTwoSided(int successes, int trials)
    {
        if (trials == 0) return 1;
        var observed = Pmf(successes, trials, 0.5);
        double total = 0;
        for (var k = 0; k <= trials; k++)
        {
            var probability = Pmf(k, trials, 0.5);
            if (probability <= observed * (1 + 1e-9)) total += probability;
        }
        return Math.Min(1, total);
    }

    private static double Cdf(int k, int n, double p)
    {
        double total = 0;
        for (var index = 0; index <= k; index++) total += Pmf(index, n, p);
        return total;
    }

    /// <summary>The exact (Clopper–Pearson) 95 % interval of a binomial share, by bisection on the distribution function.</summary>
    internal static (double Low, double High) ClopperPearson(int successes, int trials)
    {
        if (trials == 0) return (0, 1);
        double Solve(Func<double, bool> tooLow)
        {
            double lo = 0, hi = 1;
            for (var step = 0; step < 60; step++)
            {
                var mid = (lo + hi) / 2;
                if (tooLow(mid)) lo = mid;
                else hi = mid;
            }
            return (lo + hi) / 2;
        }
        // Lower bound: the p at which P(X >= successes) = 0.025; upper bound: the p at which P(X <= successes) = 0.025.
        var low = successes == 0 ? 0 : Solve(p => 1 - Cdf(successes - 1, trials, p) < 0.025);
        var high = successes == trials ? 1 : Solve(p => Cdf(successes, trials, p) > 0.025);
        return (low, high);
    }
}
