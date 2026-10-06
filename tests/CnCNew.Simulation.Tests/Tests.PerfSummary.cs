using CnCNew.SimTool.Manifests;
using CnCNew.SimTool.Perf;

static partial class Tests
{
    private static ManifestObject SyntheticPerfSummary(string label = "old", long meanNs = 4_978_100, long updatesNs = 3_264_200, string hash = "0A69FC72FF6975CD", long ticks = 54_000, long ticksPerSecond = 205)
    {
        var parameters = new ManifestObject().Add("map", "Maps/Twilight Flame/Twilight Flame.map").Add("ticks", ticks).Add("observer", true).Add("modules", true)
            .Add("factions", "FactionAmerica,FactionChina").Add("aiSeed", 6).Add("worldSeed", 3_237_998_086);
        return PerfSummary.Create("perf", label, "Test CPU; 16 logical processors", parameters)
            .Add("results", new ManifestObject().Add("ticks", ticks).Add("finalHash", hash).Add("chainHash", "5E446FE63C5BEF46").Add("incrementalEqualsFull", true))
            .Add("tick", PerfSummary.Distribution(meanNs / 1000.0, 4294.5, 8358.5, 13597.8, 547113.0))
            .Add("phases", new ManifestObject()
                .Add("Updates", PerfSummary.Distribution(updatesNs / 1000.0, 2800, 6000, 9000, 546000))
                .Add("Vision", PerfSummary.Distribution(716.0, 0, 2100, 2400, 5000))
                .Add("Timers", PerfSummary.Distribution(0.2, 0.1, 0.4, 0.9, 30)))
            .Add("modules", new ManifestObject().Add("AIUpdateInterface", new ManifestObject().Add("calls", 4_100_000).Add("totalNs", 143_000_000_000).Add("meanNs", 28_800).Add("maxNs", 52_000_000)))
            .Add("allocation", new ManifestObject().Add("bytesPerTickMean", 95_273).Add("bytesPerTickMedian", 20_000).Add("bytesPerTickMax", 9_000_000).Add("gen0", 616).Add("gen1", 68).Add("gen2", 0))
            .Add("objects", new ManifestObject().Add("min", 28).Add("max", 337).Add("end", 155))
            .Add("replay", new ManifestObject().Add("perTickNs", 4_870_300).Add("ticksPerSecond", ticksPerSecond))
            .Add("snapshot", new ManifestObject().Add("bytes", 3_198_580).Add("writeNs", 8_900_000).Add("restoreNs", 22_700_000));
    }

    public static void PerfSummaryRoundTrip()
    {
        var summary = SyntheticPerfSummary();
        var bytes = ManifestDocument.WriteBytes(summary);
        var text = System.Text.Encoding.UTF8.GetString(bytes);
        Check.That(!text.Contains('\r') && text.EndsWith("}\n", StringComparison.Ordinal), "A perf summary uses LF line ends and ends with one.");
        Check.That(text.StartsWith("{\n  \"allocation\": {\n    \"bytesPerTickMax\": 9000000,\n    \"bytesPerTickMean\": 95273,\n", StringComparison.Ordinal), "Fields are written in ordinal order: " + text[..80]);
        Check.That(text.Contains("\"meanNs\": 4978100", StringComparison.Ordinal) && text.Contains("\"p99Ns\": 13597800", StringComparison.Ordinal), "Times are whole nanoseconds.");
        var read = ManifestDocument.Read(bytes);
        PerfSummary.Validate(read, "round trip");
        Check.That(ManifestDocument.FirstDifference(summary, read) is null, "A perf summary read back equals the one written.");
        Check.That(ManifestDocument.WriteBytes(read).AsSpan().SequenceEqual(bytes), "Writing what was read gives the same bytes.");

        var path = Path.Combine(Path.GetTempPath(), "cncnew-perf-summary-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            PerfSummary.Write(path, summary);
            Check.That(File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes), "The file holds exactly the canonical bytes.");
            Check.That(ManifestDocument.FirstDifference(summary, PerfSummary.Read(path)) is null, "The file reads back as the same summary.");
        }
        finally
        {
            File.Delete(path);
        }

        Check.That(PerfSummary.Nanoseconds(0.05) == 50 && PerfSummary.Nanoseconds(4978.1) == 4_978_100, "Microseconds become whole nanoseconds.");
        var distribution = PerfSummary.DistributionOfNanoseconds([.. Enumerable.Range(1, 100).Select(value => (long)value * 1000)]);
        Check.That(((ManifestNumber)distribution["meanNs"]).Value == 50_500 && ((ManifestNumber)distribution["medianNs"]).Value == 50_000 && ((ManifestNumber)distribution["p95Ns"]).Value == 95_000
                   && ((ManifestNumber)distribution["p99Ns"]).Value == 99_000 && ((ManifestNumber)distribution["maxNs"]).Value == 100_000, "Quantiles by nearest rank.");

        Check.Throws<InvalidDataException>(() => PerfSummary.Validate(new ManifestObject().Add("kind", "reference-manifest"), "x"), "not a perf summary");
        Check.Throws<InvalidDataException>(() => PerfSummary.Validate(new ManifestObject().Add("kind", PerfSummary.Kind).Add("schema", 99), "x"), "schema 99");
        Check.Throws<InvalidDataException>(() => PerfSummary.Validate(new ManifestObject().Add("kind", PerfSummary.Kind).Add("schema", PerfSummary.Schema).Add("tool", "perf").Add("label", "").Add("hardware", ""), "x"), "no object 'parameters'");
    }

    public static void PerfCompareMarksRegressionsAndRefusesOtherRuns()
    {
        var options = new PerfCompareOptions();
        var old = SyntheticPerfSummary();
        PerfRow Row(PerfComparison comparison, string path) => comparison.Rows.Single(row => row.Path == path);

        var same = PerfCompare.Compare(old, SyntheticPerfSummary("new"), options);
        Check.That(same.Refusal is null && same.Regressions == 0 && same.Improvements == 0 && same.Warnings.IsEmpty, "The same numbers give no mark.");
        Check.That(Row(same, "tick.meanNs").DeltaPercent == 0, "No change is 0 %.");
        Check.That(same.Rows.All(row => !row.Path.StartsWith("phases.", StringComparison.Ordinal) || row.Path.EndsWith(".meanNs", StringComparison.Ordinal)), "Without --all a phase shows its mean only.");
        Check.That(PerfCompare.Compare(old, old, options with { All = true }).Rows.Any(row => row.Path == "phases.Updates.p99Ns"), "--all shows every metric.");

        // 20 % slower tick, 30 % slower update phase, replay catch-up 20 % down: three regressions with the default threshold of 10 %.
        var slower = SyntheticPerfSummary("new", meanNs: 5_973_720, updatesNs: 4_243_460, ticksPerSecond: 164);
        var worse = PerfCompare.Compare(old, slower, options);
        Check.That(worse.Refusal is null, "Same parameters and hash: compared.");
        Check.That(Row(worse, "tick.meanNs").Verdict == PerfVerdict.Regression && Math.Abs(Row(worse, "tick.meanNs").DeltaPercent!.Value - 20.0) < 0.01, "A tick that is 20 % slower is a regression.");
        Check.That(Row(worse, "phases.Updates.meanNs").Verdict == PerfVerdict.Regression, "A phase that is 30 % slower is a regression.");
        Check.That(Row(worse, "replay.ticksPerSecond").Verdict == PerfVerdict.Regression, "Fewer ticks per second is a regression (higher is better).");
        Check.That(worse.Regressions == 3 && worse.Improvements == 0, $"Exactly the three changed metrics are marked, not {worse.Regressions}/{worse.Improvements}.");
        Check.That(PerfCompare.Compare(old, slower, options with { ThresholdPercent = 25 }).Regressions == 1, "With a threshold of 25 % only the phase (+30 %) is marked.");
        var faster = PerfCompare.Compare(slower, old, options);
        Check.That(faster.Regressions == 0 && faster.Improvements == 3, "The other way round the same three are improvements.");

        // Counts are shown, never judged; times below the noise floor on both sides are not judged either.
        var noisy = SyntheticPerfSummary("new");
        ((ManifestObject)noisy.Object("phases")["Timers"])["meanNs"] = 900;
        noisy.Object("objects")["max"] = 900;
        noisy.Object("allocation")["gen2"] = 2;
        var noise = PerfCompare.Compare(old, noisy, options);
        Check.That(Row(noise, "phases.Timers.meanNs").Verdict == PerfVerdict.Unmarked, "0.2 us to 0.9 us is below the noise floor of 1 us.");
        Check.That(Row(noise, "objects.max").Verdict == PerfVerdict.Unmarked && Row(noise, "objects.max").Direction == PerfDirection.Neutral, "Object counts are not judged.");
        Check.That(Row(noise, "allocation.gen2").Verdict == PerfVerdict.Regression && Row(noise, "allocation.gen2").DeltaPercent is null, "From no gen-2 collection to two is a regression without a percentage.");
        Check.That(PerfCompare.Compare(old, noisy, options with { NoiseFloorNs = 0 }).Rows.Single(row => row.Path == "phases.Timers.meanNs").Verdict == PerfVerdict.Regression, "Without a noise floor the small phase is judged.");

        // Other parameters: never compared.
        var otherRun = PerfCompare.Compare(old, SyntheticPerfSummary("new", ticks: 18_000), options with { AllowDifferentHash = true });
        Check.That(otherRun.Refusal is { } reason && reason.Contains("parameters.ticks", StringComparison.Ordinal) && reason.Contains("54000", StringComparison.Ordinal) && reason.Contains("18000", StringComparison.Ordinal) && otherRun.Rows.IsEmpty,
            "Different parameters are refused with the field and both values: " + otherRun.Refusal);
        var otherTool = SyntheticPerfSummary("new");
        otherTool["tool"] = "perf-mission";
        Check.That(PerfCompare.Compare(old, otherTool, options).Refusal is { } toolReason && toolReason.Contains("different commands", StringComparison.Ordinal), "Summaries of different commands are refused.");

        // Another final hash is another run: refused unless asked for, and then said loudly.
        var otherHash = SyntheticPerfSummary("new", hash: "FFFFFFFFFFFFFFFF");
        var refused = PerfCompare.Compare(old, otherHash, options);
        Check.That(refused.Refusal is { } hashReason && hashReason.Contains("DIFFERENT FINAL HASH", StringComparison.Ordinal) && hashReason.Contains("--allow-different-hash", StringComparison.Ordinal) && refused.Rows.IsEmpty, "A different final hash is refused.");
        var allowed = PerfCompare.Compare(old, otherHash, options with { AllowDifferentHash = true });
        Check.That(allowed.Refusal is null && allowed.Warnings.Any(warning => warning.Contains("DIFFERENT FINAL HASH", StringComparison.Ordinal)) && allowed.Rows.Length > 0, "With --allow-different-hash the table comes with a warning.");
        var otherMachine = SyntheticPerfSummary("new");
        otherMachine["hardware"] = "Another CPU";
        Check.That(PerfCompare.Compare(old, otherMachine, options).Warnings.Any(warning => warning.Contains("not comparable", StringComparison.Ordinal)), "Another machine is a warning.");

        // A metric only one side has is shown without a verdict.
        var missing = SyntheticPerfSummary("new");
        missing["replay"] = new ManifestObject();
        Check.That(Row(PerfCompare.Compare(old, missing, options), "replay.perTickNs") is { New: null, Verdict: PerfVerdict.Unmarked }, "A missing metric is shown, not judged.");

        var text = PerfCompare.Format(old, slower, worse, options);
        Check.That(!text.Contains('\r') && text.Contains("| tick.meanNs | 4978.1 us | 5973.7 us | +20.0 % | REGRESSION |\n", StringComparison.Ordinal), "The table shows microseconds, the change and the mark: " + text);
        Check.That(text.Contains("| objects.max | 337 | 337 | 0.0 % |  |\n", StringComparison.Ordinal) && text.Contains("3 regression(s) and 0 improvement(s)", StringComparison.Ordinal), "Counts are shown as they are; the last line counts the marks.");
        Check.That(PerfCompare.Format(old, otherHash, refused, options).Contains("REFUSED: DIFFERENT FINAL HASH", StringComparison.Ordinal), "A refusal is printed instead of a table.");
        Check.That(PerfCompare.Format(old, otherHash, allowed, options).Contains("WARNING: DIFFERENT FINAL HASH", StringComparison.Ordinal), "The warning stands above the table.");
    }
}
