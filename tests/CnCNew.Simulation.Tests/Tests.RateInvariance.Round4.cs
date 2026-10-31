using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;

/// <summary>
/// Phase 23, round 4 (docs/rate-invariance/RATE-INVARIANCE-ROUND4-FIRST-FRAME.md): whole matches are the same game at 30, 60 and 120 Hz. The test names its rates itself.
/// </summary>
static partial class Tests
{
    /// <summary>
    /// The retail reference match at 30, 60 and 120 Hz: the generator plays every faction from what it sees of the world (the nearest enemy of every unit, to the raw step of its
    /// position), so its command stream is the same at every rate only while the worlds are. The first 6,000 logic frames of the original (200 s: the bases, the supply trips of
    /// trucks, workers and Chinooks, the first battles of thirteen factions) have to give the same commands in the same frames. Before round 4 the streams parted in the first
    /// second (the starting Chinooks climbed from the tick after they appeared).
    /// </summary>
    public static void RealReferenceMatchAcrossRates(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        const int Frames = 6_000;

        List<string> Stream(SimulationRate rate)
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            var scenario = new ReferenceMatchScenario(ReferenceMatchOptions.Standard with { MaxTicks = rate.Time.OriginalFrames(Frames) });
            var recorded = scenario.Record(catalog, fingerprint);
            var stride = rate.TicksPerOriginalFrame;
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
            try
            {
                // A command as text without its tick, behind the frame it was given in: every command of the generator is given where a frame begins.
                return [..recorded.Commands.Select(command =>
                {
                    Check.That(command.Tick % stride == 0, $"{rate}: the generator gives its commands where a logic frame of the original begins (tick {command.Tick}).");
                    var tick = command.Tick.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    return $"frame {command.Tick / stride}: {(command.ToString() ?? string.Empty).Replace($"Tick = {tick}, ", string.Empty, StringComparison.Ordinal)}";
                })];
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = culture;
            }
        }

        var reference = Stream(SimulationRate.Hz30);
        Check.That(reference.Count > 5_000, $"The compared stretch is a match that is being played ({reference.Count} commands).");
        foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var stream = Stream(rate);
            var common = Math.Min(stream.Count, reference.Count);
            var first = Enumerable.Range(0, common).FirstOrDefault(index => stream[index] != reference[index], common);
            Check.That(first == common && stream.Count == reference.Count,
                $"The retail match at {rate.TicksPerSecond} Hz is the 30 Hz match over {Frames} frames ({stream.Count} commands, {reference.Count} at 30 Hz; first difference at command {first}: " +
                $"30 Hz: {(first < reference.Count ? reference[first] : "-")}; {rate.TicksPerSecond} Hz: {(first < stream.Count ? stream[first] : "-")}).");
        }
        Console.WriteLine($"       retail match across rates: {Frames} frames, {reference.Count} commands, the same commands in the same frames at 30, 60 and 120 Hz");
    }
}
