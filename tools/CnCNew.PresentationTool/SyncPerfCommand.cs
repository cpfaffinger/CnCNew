using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Presentation;
using CnCNew.Presentation.Fx;
using CnCNew.Presentation.Objects;
using CnCNew.SimTool.Manifests;
using CnCNew.SimTool.Perf;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Ui.Settings;

namespace CnCNew.PresentationTool;

/// <summary>
/// <c>sync-perf</c> (docs/PERF-GAME-LAYER.md): what the game layer costs around every simulation tick. Runs a <see cref="LiveMatch"/> with computer players in
/// every slot headless, exactly as the client drives it: one <see cref="LiveGame.Advance"/> of one tick length per step, which is <c>BeforeTick</c>, the
/// simulation step, <c>SyncAfterTick</c>, the <c>TickStepped</c> handlers, the fog fade and one <c>FrameProjector.Project</c> (a stand-in for one frame at
/// 30 fps). The game reports where its parts begin and end (<see cref="IPresentationPhaseObserver"/>); the clock is read here. With <c>--digest</c> every
/// frame, the fog levels, the effects and the sound cues are hashed into one SHA-256: two builds that print the same digest produced the same presentation.
/// </summary>
internal static class SyncPerfCommand
{
    public const string Usage = "sync-perf <content-dir> --map <virtual-map-path> (--players N | --factions A,B,..) [--ticks N] [--seed N] [--viewport WxH] [--hud] [--digest] [--json FILE] [--label TEXT]";

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private static readonly PresentationPhase[] Phases = Enum.GetValues<PresentationPhase>();

    // How the phases nest, for the indentation of the table.
    private static int DepthOf(PresentationPhase phase) => phase switch
    {
        PresentationPhase.ProjectorObserve or PresentationPhase.ProjectorAdvance or PresentationPhase.FxViewRefresh or PresentationPhase.FxExecuteEvents
            or PresentationPhase.FxUpdate or PresentationPhase.DrainSoundCues or PresentationPhase.FogUpdate => 1,
        PresentationPhase.ProjectorAdvanceVisibleEntities or PresentationPhase.ProjectorAdvanceCapture or PresentationPhase.ProjectorAdvanceGhosts or PresentationPhase.ProjectorAdvanceScenery => 2,
        _ => 0,
    };

    public static int Run(string[] args)
    {
        if (args.Length < 1) throw new ArgumentException(Usage);
        CultureInfo.CurrentCulture = Invariant;
        string? Option(string key) => Array.IndexOf(args, key) is var index and >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        var contentDir = args[0];
        var mapPath = Option("--map") ?? throw new ArgumentException("--map <virtual-map-path> is required.\n" + Usage);
        var ticks = long.Parse(Option("--ticks") ?? "18000", Invariant);
        var seed = int.Parse(Option("--seed") ?? "0", Invariant);
        var viewportText = (Option("--viewport") ?? "1280x720").Split('x');
        var viewport = new Vector2(int.Parse(viewportText[0], Invariant), int.Parse(viewportText[1], Invariant));
        var withHud = args.Contains("--hud");
        var withDigest = args.Contains("--digest");

        var hardware = $"{Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER")}; {Environment.ProcessorCount} logical processors; {GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024)} MiB memory; "
                       + $"{RuntimeInformation.OSDescription}; {RuntimeInformation.FrameworkDescription}; Server GC {System.Runtime.GCSettings.IsServerGC}; clock resolution {(1e9 / Stopwatch.Frequency).ToString("F0", Invariant)} ns.";
        Console.WriteLine("Hardware: " + hardware);
        var setup = Stopwatch.StartNew();
        var data = GameData.Load(contentDir);
        var wanted = Option("--factions")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var chosen = wanted is null
            ? data.Factions.Take(int.Parse(Option("--players") ?? "2", Invariant)).Select(value => value.Faction.Name).ToImmutableArray()
            : [..wanted.Select(name => data.Factions.FirstOrDefault(value => value.Faction.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.Faction.Name ?? throw new ArgumentException($"Unknown or unplayable faction {name}."))];
        if (chosen.Length < 2) throw new ArgumentException("A match needs at least two players.");
        // The viewer's slot is played by the computer too: the same match as `SimTool perf` with these players (same seeds, same final hash).
        var matchSetup = new MatchSetup(mapPath, chosen[0], chosen[1..], SkirmishMatchOptions.Standard with { MaxTicks = ticks }) { AutoPlay = true, Seed = seed };
        var live = LiveMatch.Create(data, matchSetup, viewport);
        var hud = withHud ? new MatchController(live, GameSettings.Default) : null;
        var scenery = live.Frame.Drawables.Count(drawable => drawable.IsStatic);
        Console.WriteLine($"Sync perf on {mapPath} ({live.Terrain.CellsX}x{live.Terrain.CellsY} cells), definitions {data.Fingerprint[..16]}; {chosen.Length} computer players: {string.Join(", ", chosen)}; viewer player {live.Human.Value}.");
        Console.WriteLine($"Setup (definitions, model geometry, map, terrain model, minimap): {setup.Elapsed.TotalSeconds:F1} s. Budget {ticks} ticks; {live.Map.Objects.Length} map objects, {scenery} scenery drawables in the first frame; "
                          + $"HUD {(withHud ? "attached (MatchController: BeforeTick, TickStepped, Update, BuildDrawList; nothing selected, no input)" : "absent (BeforeTick and TickStepped are empty)")}; digest {(withDigest ? "on" : "off")}.");

        var recorder = new Recorder();
        live.PhaseObserver = recorder;
        using var digest = withDigest ? IncrementalHash.CreateHash(HashAlgorithmName.SHA256) : null;
        var buffer = new MemoryStream();
        var writer = new BinaryWriter(buffer, Encoding.UTF8);
        var fogLevels = new byte[live.Fog.CellsX * live.Fog.CellsY];
        var tickSeconds = 1.0 / SimulationRate.OriginalFramesPerSecond;
        var total = new List<long>();
        var hudUpdate = new List<long>();
        var hudDraw = new List<long>();
        var allocations = new List<long>();
        var objects = new List<int>();
        var drawables = new List<int>();
        var particles = new List<int>();
        long cues = 0;
        var gcBefore = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        var wall = Stopwatch.StartNew();
        while (live.Tick < ticks && !live.IsOver)
        {
            var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            live.Advance(tickSeconds);
            var afterAdvance = Stopwatch.GetTimestamp();
            long afterUpdate = afterAdvance, afterDraw = afterAdvance;
            if (hud is not null)
            {
                hud.Update(tickSeconds);
                afterUpdate = Stopwatch.GetTimestamp();
                hud.BuildDrawList((int)viewport.X, (int)viewport.Y);
                afterDraw = Stopwatch.GetTimestamp();
            }
            allocations.Add(GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
            total.Add(afterDraw - start);
            hudUpdate.Add(afterUpdate - afterAdvance);
            hudDraw.Add(afterDraw - afterUpdate);
            recorder.Commit();
            objects.Add(live.World.Entities.Count);
            drawables.Add(live.Frame.Drawables.Length);
            particles.Add(live.Fx.Particles.Stats.Particles);
            // Outside the timed part. Draining here is what the audio director does once per frame.
            var drained = live.DrainSoundCues();
            cues += drained.Length;
            if (digest is not null) Feed(digest, buffer, writer, live, drained, fogLevels);
        }
        wall.Stop();
        live.PhaseObserver = null;
        var gcAfter = (GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2));
        var steps = total.Count;
        if (steps == 0) throw new InvalidOperationException("The match was over before the first tick.");
        var finalHash = live.World.StateHash;
        Console.WriteLine(live.Winner is null
            ? $"Match: {steps} ticks in {wall.Elapsed.TotalSeconds:F1} s wall time, no winner (tick budget)."
            : $"Match: {steps} ticks in {wall.Elapsed.TotalSeconds:F1} s wall time, winner player {live.Winner.Value.Value}.");

        var json = PerfSummary.Create("sync-perf", Option("--label") ?? string.Empty, hardware, new ManifestObject()
            .Add("map", mapPath).Add("ticks", ticks).Add("factions", string.Join(",", chosen)).Add("seed", seed).Add("viewport", $"{(int)viewport.X}x{(int)viewport.Y}").Add("hud", withHud).Add("digest", withDigest));
        var whole = Distribution(total);
        json.Add("tick", whole);
        var phasesJson = new ManifestObject();
        json.Add("phases", phasesJson);
        Console.WriteLine();
        Console.WriteLine($"Time per simulated tick in microseconds over {steps} ticks (one LiveGame.Advance of one tick length{(withHud ? " plus the HUD's Update and BuildDrawList" : "")}; quantiles by nearest rank; nested phases are indented and contained in the one above):");
        Console.WriteLine($"  {"",-38} {"mean",10} {"median",10} {"95 %",10} {"99 %",10} {"max",11} {"share",8}");
        var totalMean = Mean(whole);
        void Row(string name, ManifestObject distribution)
            => Console.WriteLine($"  {name,-38} {Us(distribution, "meanNs"),10:F1} {Us(distribution, "medianNs"),10:F1} {Us(distribution, "p95Ns"),10:F1} {Us(distribution, "p99Ns"),10:F1} {Us(distribution, "maxNs"),11:F1} {(totalMean <= 0 ? 0 : 100.0 * Mean(distribution) / totalMean),6:F1} %");
        Row("tick (everything below)", whole);
        double attributed = 0;
        foreach (var phase in Phases)
        {
            var distribution = Distribution(recorder.Values(phase));
            phasesJson.Add(phase.ToString(), distribution);
            if (DepthOf(phase) == 0) attributed += Mean(distribution);
            Row(new string(' ', 2 * DepthOf(phase)) + phase, distribution);
        }
        if (hud is not null)
        {
            var update = Distribution(hudUpdate);
            var draw = Distribution(hudDraw);
            phasesJson.Add("HudUpdate", update).Add("HudBuildDrawList", draw);
            attributed += Mean(update) + Mean(draw);
            Row("HudUpdate", update);
            Row("HudBuildDrawList", draw);
        }
        phasesJson.Add("(unattributed)", new ManifestObject().Add("meanNs", (long)Math.Round(totalMean - attributed)));
        var gameLayer = totalMean - Mean((ManifestObject)phasesJson[nameof(PresentationPhase.StepSimulation)]);
        phasesJson.Add("(game layer)", new ManifestObject().Add("meanNs", (long)Math.Round(gameLayer)));
        Console.WriteLine($"  {"(unattributed)",-38} {(totalMean - attributed) / 1000,10:F1}");
        Console.WriteLine($"  game layer alone (the tick without StepSimulation): mean {gameLayer / 1000:F1} us = {100.0 * gameLayer / totalMean:F1} % of the tick; at 60 Hz with one projection per tick {gameLayer * 60 / 1e7:F1} % of one core.");

        long allocated = 0;
        foreach (var value in allocations) allocated += value;
        var sortedAllocations = allocations.ToArray();
        Array.Sort(sortedAllocations);
        Console.WriteLine();
        Console.WriteLine($"Allocations (simulation and game layer together): {allocated / steps} bytes/tick mean, {sortedAllocations[steps / 2]} median, {sortedAllocations[^1]} max; GC gen0/1/2 = {gcAfter.Item1 - gcBefore.Item1}/{gcAfter.Item2 - gcBefore.Item2}/{gcAfter.Item3 - gcBefore.Item3}.");
        Console.WriteLine($"Objects: min {objects.Min()}, max {objects.Max()}, end {objects[^1]}. Drawables per frame: min {drawables.Min()}, max {drawables.Max()}, end {drawables[^1]}. Particles: max {particles.Max()}, end {particles[^1]}. Sound cues: {cues}.");
        json.Add("allocation", new ManifestObject().Add("bytesPerTickMean", allocated / steps).Add("bytesPerTickMedian", sortedAllocations[steps / 2]).Add("bytesPerTickMax", sortedAllocations[^1])
            .Add("gen0", gcAfter.Item1 - gcBefore.Item1).Add("gen1", gcAfter.Item2 - gcBefore.Item2).Add("gen2", gcAfter.Item3 - gcBefore.Item3));
        json.Add("objects", new ManifestObject().Add("min", objects.Min()).Add("max", objects.Max()).Add("end", objects[^1])
            .Add("drawablesMax", drawables.Max()).Add("drawablesEnd", drawables[^1]).Add("particlesMax", particles.Max()).Add("soundCues", cues));
        var results = new ManifestObject().Add("ticks", steps).Add("finalHash", finalHash.ToString("X16")).Add("winner", live.Winner is { } winner ? winner.Value : 0L);
        Console.WriteLine($"Final world hash {finalHash:X16} (`SimTool perf` with the same map, players and ticks prints the same hash).");
        if (digest is not null)
        {
            var text = Convert.ToHexString(digest.GetHashAndReset());
            results.Add("presentationDigest", text);
            Console.WriteLine($"Presentation digest {text} (SHA-256 over every frame's drawables, the fog levels, the effects frame and the sound cues of all {steps} ticks).");
        }
        json.Add("results", results);
        if (Option("--json") is { } file)
        {
            var path = Path.GetFullPath(file);
            PerfSummary.Write(path, json);
            Console.WriteLine($"JSON: summary written to {Path.GetFileName(path)} (compare two of them with `SimTool perf-compare <old.json> <new.json>`).");
        }
        return 0;
    }

    private static ManifestObject Distribution(List<long> stopwatchTicks)
    {
        var nanoseconds = new long[stopwatchTicks.Count];
        for (var index = 0; index < nanoseconds.Length; index++) nanoseconds[index] = (long)Math.Round(stopwatchTicks[index] * 1e9 / Stopwatch.Frequency);
        return PerfSummary.DistributionOfNanoseconds(nanoseconds);
    }

    private static double Mean(ManifestObject distribution) => ((ManifestNumber)distribution["meanNs"]).Value;
    private static double Us(ManifestObject distribution, string name) => ((ManifestNumber)distribution[name]).Value / 1000.0;

    /// <summary>Everything the presentation produced for this tick, in a fixed binary form.</summary>
    private static void Feed(IncrementalHash digest, MemoryStream buffer, BinaryWriter writer, LiveGame live, ImmutableArray<SoundCue> cues, byte[] fogLevels)
    {
        buffer.SetLength(0);
        var frame = live.Frame;
        writer.Write(frame.Tick);
        writer.Write(frame.Alpha);
        writer.Write(frame.Viewer?.Value ?? -1);
        writer.Write(frame.Drawables.Length);
        foreach (var drawable in frame.Drawables)
        {
            writer.Write(drawable.Entity.Value);
            writer.Write(drawable.SceneryIndex);
            writer.Write(drawable.Visual.TemplateName);
            Write(writer, drawable.Position);
            writer.Write(drawable.Yaw);
            writer.Write(drawable.Owner.Value);
            writer.Write(drawable.TeamColor.Red); writer.Write(drawable.TeamColor.Green); writer.Write(drawable.TeamColor.Blue); writer.Write(drawable.TeamColor.Alpha);
            writer.Write(drawable.Health01);
            writer.Write(drawable.ConstructionProgress01);
            writer.Write(drawable.Conditions.ToString());
            writer.Write(drawable.Selections.Length);
            foreach (var selection in drawable.Selections)
            {
                writer.Write(selection.StateIndex);
                writer.Write(selection.Model ?? "\0");
            }
            Write(writer, drawable.TurretYaw); Write(writer, drawable.TurretPitch); Write(writer, drawable.AltTurretYaw); Write(writer, drawable.AltTurretPitch);
            writer.Write(drawable.IsStatic); writer.Write(drawable.IsGhost); writer.Write(drawable.ShowsHealing); writer.Write(drawable.Fogged);
            writer.Write(drawable.WheelTravel);
            writer.Write(drawable.FiredSlots);
            var chassis = drawable.Chassis;
            writer.Write(chassis.Pitch); writer.Write(chassis.Roll); writer.Write(chassis.LiftZ); writer.Write(chassis.WheelAngle);
            writer.Write(chassis.FrontLeft); writer.Write(chassis.FrontRight); writer.Write(chassis.RearLeft); writer.Write(chassis.RearRight);
            Write(writer, drawable.FlashTint);
            writer.Write(drawable.Opacity);
        }

        live.Fog.CopyLevels(fogLevels);
        writer.Write(fogLevels);

        // Capture hands ray and shake requests out once, as the effects layer does every frame.
        var fx = live.Fx.Capture();
        writer.Write(fx.Frame);
        writer.Write(fx.Particles.Particles.Length);
        writer.Write(MemoryMarshal.AsBytes(fx.Particles.Particles.AsSpan()));
        writer.Write(fx.Particles.Batches.Length);
        foreach (var batch in fx.Particles.Batches) writer.Write(batch.ToString());
        writer.Write(fx.Tracers.Length);
        foreach (var tracer in fx.Tracers) writer.Write(tracer.ToString());
        writer.Write(fx.Lights.Length);
        foreach (var light in fx.Lights) writer.Write(light.ToString());
        writer.Write(fx.Decals.Length);
        foreach (var decal in fx.Decals) writer.Write(decal.ToString());
        writer.Write(fx.RayEffects.Length);
        foreach (var ray in fx.RayEffects) writer.Write(ray.ToString());
        writer.Write(fx.ViewShakes.Length);
        foreach (var shake in fx.ViewShakes) writer.Write(shake.ToString());

        writer.Write(cues.Length);
        foreach (var cue in cues) writer.Write(cue.ToString());
        writer.Flush();
        digest.AppendData(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static void Write(BinaryWriter writer, Vector3 value)
    {
        writer.Write(value.X);
        writer.Write(value.Y);
        writer.Write(value.Z);
    }

    private static void Write(BinaryWriter writer, float? value)
    {
        writer.Write(value.HasValue);
        writer.Write(value ?? 0f);
    }

    /// <summary>Reads the clock at the boundaries the game reports; one value per tick and phase, in clock units. Phases nest, so each has its own start.</summary>
    private sealed class Recorder : IPresentationPhaseObserver
    {
        private readonly long[] _start = new long[Phases.Length];
        private readonly long[] _current = new long[Phases.Length];
        private readonly List<long>[] _values = [..Phases.Select(_ => new List<long>())];

        public List<long> Values(PresentationPhase phase) => _values[(int)phase];
        public void PhaseStarted(PresentationPhase phase) => _start[(int)phase] = Stopwatch.GetTimestamp();
        public void PhaseEnded(PresentationPhase phase) => _current[(int)phase] += Stopwatch.GetTimestamp() - _start[(int)phase];

        public void Commit()
        {
            for (var index = 0; index < _current.Length; index++)
            {
                _values[index].Add(_current[index]);
                _current[index] = 0;
            }
        }
    }
}
