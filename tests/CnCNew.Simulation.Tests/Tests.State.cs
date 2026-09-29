using System.Collections.Immutable;
using System.Text;
using System.Text.RegularExpressions;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Timers;
using CnCNew.Simulation.World;

static partial class Tests
{
    private static readonly StressOptions ShortOptions = new(3_000, 300, 0xC0FFEE01, 0x5EED0001, 400, 250);

    /// <summary>Records a short stress scenario and adds checkpoints from a second run.</summary>
    private static (SyntheticFixture Fixture, ReplayFile Replay, StressScenario Scenario) ShortReplay(StressOptions? options = null)
    {
        options ??= ShortOptions;
        var fixture = new SyntheticFixture();
        var scenario = new StressScenario(options);
        var recorded = scenario.Record(fixture);
        var withCheckpoints = ReplayRunner.Run(recorded, fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { RecordCheckpointInterval = options.CheckpointInterval });
        return (fixture, recorded with { Checkpoints = withCheckpoints.Checkpoints }, scenario);
    }

    public static void HashDeterminism()
    {
        var (fixture, replay, _) = ShortReplay();
        var first = ReplayRunner.Run(replay, fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { RecordTickHashes = true });
        var second = ReplayRunner.Run(replay, fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { RecordTickHashes = true });
        Check.That(first.TickHashes.AsSpan().SequenceEqual(second.TickHashes), "Two fresh runs must produce identical tick hashes.");
        Check.That(first.TickHashes.Distinct().Count() > first.TickHashes.Length / 2, "Tick hashes must actually change (not a constant).");

        // Incremental hash == full recomputation on every tick.
        var verified = ReplayRunner.Run(replay, fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { VerifyIncrementalEvery = 1 });
        Check.That(verified.FinalHash == first.FinalHash, "Verification must not change the outcome.");

        // Submission order inside one tick does not matter.
        ulong[] Hashes(bool reversed)
        {
            var world = NewWorld(fixture, 5);
            var commands = new List<SimCommand>();
            for (var index = 0; index < 6; index++)
            {
                var player = 1 + index % 2;
                commands.Add(new SpawnCommand(new PlayerId(player), (ulong)(index / 2 + 1), 2, index % 3 == 0 ? "Spark" : "Barrel", PlayerId.System, Fixed.FromInt(index), Fixed.Zero, Fixed.Zero, Fixed.Zero));
            }
            // Keep each player's sequences increasing while changing the interleaving of players.
            var ordered = reversed ? commands.OrderByDescending(value => value.Player.Value).ThenBy(value => value.Sequence) : commands.OrderBy(value => value.Player.Value).ThenBy(value => value.Sequence);
            foreach (var command in ordered) world.Submit(command);
            var list = new List<ulong>();
            for (var tick = 0; tick < 60; tick++) list.Add(world.Step().StateHash);
            return [..list];
        }
        Check.That(Hashes(false).AsSpan().SequenceEqual(Hashes(true)), "Different submission orders of the same tick must give identical hashes.");

        // The logic stream is part of the hash, the cosmetic stream is not.
        var quiet = NewWorld(fixture, 5);
        var noisy = NewWorld(fixture, 5);
        var tampered = NewWorld(fixture, 5);
        foreach (var world in new[] { quiet, noisy, tampered }) new Feeder(world).Spawn(1, "Spark");
        for (var tick = 0; tick < 5; tick++)
        {
            noisy.Streams.Cosmetic.NextUInt32();
            noisy.Streams.Audio.NextUInt32();
            Check.That(quiet.Step().StateHash == noisy.Step().StateHash, "Cosmetic and audio draws must not change the state hash.");
            if (tick == 2) tampered.LogicRandom.NextUInt32();
            tampered.Step();
        }
        Check.That(quiet.StateHash != tampered.StateHash, "A logic draw must change the state hash.");

        // Different seeds differ as soon as a module draws random numbers.
        var seedA = NewWorld(fixture, 1);
        var seedB = NewWorld(fixture, 2);
        new Feeder(seedA).Spawn(1, "Spark");
        new Feeder(seedB).Spawn(1, "Spark");
        Check.That(seedA.Step().StateHash != seedB.Step().StateHash, "Different seeds give different states.");
    }

    public static void SnapshotRoundTrip()
    {
        var (fixture, replay, _) = ShortReplay();
        SimulationWorld RunTo(long tick)
        {
            var world = ReplayRunner.CreateWorld(replay.Header, fixture.Templates);
            ReplayRunner.Run(replay, world, new ReplayRunOptions { StopTick = tick });
            return world;
        }
        var world = RunTo(1500);
        var bytes = WorldSnapshot.Write(world);
        Check.That(bytes.AsSpan().SequenceEqual(WorldSnapshot.Write(RunTo(1500))), "Two independent runs write byte-identical snapshots.");
        Check.That(!Encoding.UTF8.GetString(bytes).Contains(":\\", StringComparison.Ordinal) && !Encoding.UTF8.GetString(bytes).Contains("/Users/", StringComparison.Ordinal), "Snapshots contain no absolute paths.");
        Console.WriteLine($"       snapshot at tick 1500: {bytes.Length} bytes, {world.Metrics.Entities} entities, SHA-256 {WorldSnapshot.Sha256(bytes)[..16]}");

        var restored = WorldSnapshot.Restore(bytes, fixture.Templates, fixture.Fingerprint);
        Check.That(restored.Tick == world.Tick && restored.StateHash == world.StateHash, "Restore reproduces tick and state hash.");
        Check.That(WorldSnapshot.Write(restored).AsSpan().SequenceEqual(bytes), "Write(Restore(s)) == s.");
        Check.That(restored.Metrics == world.Metrics, "Restore reproduces every table size.");

        var continued = ReplayRunner.Run(replay, world, new ReplayRunOptions { RecordTickHashes = true });
        var resumed = ReplayRunner.Run(replay, restored, new ReplayRunOptions { RecordTickHashes = true, VerifyIncrementalEvery = 100 });
        Check.That(continued.TickHashes.AsSpan().SequenceEqual(resumed.TickHashes) && continued.TicksExecuted == 1500, "The continuation after restore is hash-identical tick by tick.");

        // The initial state round-trips, and so do pending commands and pending events.
        var fresh = NewWorld(fixture, 3);
        var freshRestored = WorldSnapshot.Restore(WorldSnapshot.Write(fresh), fixture.Templates, fixture.Fingerprint);
        Check.That(freshRestored.StateHash == fresh.StateHash, "An empty world round-trips.");
        var pending = NewWorld(fixture, 3);
        var feeder = new Feeder(pending);
        for (var tick = 0; tick < 5; tick++) pending.Step();
        feeder.Spawn(1, "Spark", 12);
        feeder.Timer(0, 3, 2, TimerActionKind.Notify, tag: 5, tick: 9);
        feeder.Kill(1, 6, 1);   // rejected: raises an event that stays queued until the next tick
        Check.That(pending.Metrics.PendingCommands == 2 && pending.Metrics.PendingEvents == 1, "Fixture: pending commands and events exist.");
        var pendingRestored = WorldSnapshot.Restore(WorldSnapshot.Write(pending), fixture.Templates, fixture.Fingerprint);
        Check.That(pendingRestored.StateHash == pending.StateHash && pendingRestored.Metrics == pending.Metrics, $"Pending commands and events survive the snapshot: {pending.Metrics} / {pendingRestored.Metrics}; {pending.StateHash:X16} / {pendingRestored.StateHash:X16}");
        for (var tick = 0; tick < 30; tick++)
            Check.That(pending.Step().StateHash == pendingRestored.Step().StateHash, "Pending commands execute identically after restore.");
        Check.That(pending.Metrics.Timers == 1 && pending.Metrics.SpawnedTotal == 1, "The restored timer and spawn ran in both worlds.");
    }

    public static void SnapshotRejection()
    {
        var (fixture, replay, _) = ShortReplay();
        var world = ReplayRunner.CreateWorld(replay.Header, fixture.Templates);
        ReplayRunner.Run(replay, world, new ReplayRunOptions { StopTick = 600 });
        var bytes = WorldSnapshot.Write(world);
        var expectedHash = world.StateHash;

        void Rejected(byte[] data, string what, string? fingerprint = null)
        {
            try { WorldSnapshot.Restore(data, fixture.Templates, fingerprint ?? fixture.Fingerprint); }
            catch (SnapshotFormatException) { return; }
            throw new InvalidOperationException($"{what}: the defective snapshot was accepted.");
        }
        Rejected([], "empty");
        Rejected(Encoding.UTF8.GetBytes("not json"), "garbage");
        foreach (var fraction in new[] { 0.01, 0.1, 0.5, 0.9, 0.999 })
            Rejected(bytes[..(int)(bytes.Length * fraction)], $"truncated to {fraction:P1}");
        Rejected(bytes[..^1], "missing final byte");
        var text = Encoding.UTF8.GetString(bytes);
        Rejected(Encoding.UTF8.GetBytes(text.Replace($"\"version\":{WorldSnapshot.Version}", $"\"version\":{WorldSnapshot.Version + 1}", StringComparison.Ordinal)), "foreign version");
        Rejected(Encoding.UTF8.GetBytes(text.Replace("cncnew-world-snapshot", "other-format", StringComparison.Ordinal)), "foreign format");
        Rejected(Encoding.UTF8.GetBytes(text.Replace("\"tickRate\":30", "\"tickRate\":60", StringComparison.Ordinal)), "foreign tick rate");
        Rejected(bytes, "definition fingerprint mismatch", new string('0', 64));
        Rejected(Encoding.UTF8.GetBytes(text.Replace("\"tick\":600", "\"tick\":601", StringComparison.Ordinal)), "tampered tick");

        // Flip bytes: each defective copy is rejected, or it is semantically identical to the original.
        var flips = 0;
        for (var position = 20; position < bytes.Length; position += Math.Max(1, bytes.Length / 240))
        {
            var copy = (byte[])bytes.Clone();
            copy[position] ^= 0x01;
            try
            {
                var restored = WorldSnapshot.Restore(copy, fixture.Templates, fixture.Fingerprint);
                Check.That(restored.ComputeFullHash() == expectedHash, $"A flipped byte at {position} produced a different accepted state.");
            }
            catch (SnapshotFormatException)
            {
                flips++;
            }
        }
        Check.That(flips > 200, $"Nearly every flipped byte must be detected ({flips} of about 240).");

        // A different template catalog is refused (names are cross-checked, not only indices).
        var other = new SyntheticFixture(files: new Dictionary<string, string>(SyntheticFixture.Files)
        {
            ["Data/INI/Object/Extra.ini"] = "Object AAAFirst\nEnd\n",
        });
        Check.Throws<SnapshotFormatException>(() => WorldSnapshot.Restore(bytes, other.Templates, fixture.Fingerprint), "template");
    }

    public static void ReplayFormatTests()
    {
        var (fixture, replay, _) = ShortReplay();
        var bytes = ReplayFormat.Write(replay);
        var read = ReplayFormat.Read(bytes);
        Check.That((read.Header with { Players = default }) == (replay.Header with { Players = default }) && read.Header.Players.SequenceEqual(replay.Header.Players) && read.Commands.SequenceEqual(replay.Commands) && read.Checkpoints.SequenceEqual(replay.Checkpoints), "Replay round trip preserves header, commands and checkpoints.");
        Check.That(ReplayFormat.Write(read).AsSpan().SequenceEqual(bytes), "The format is canonical.");
        Console.WriteLine($"       replay: {bytes.Length} bytes, {replay.Commands.Length} commands, {replay.Checkpoints.Length} checkpoints");

        void Rejected(byte[] data, string what)
        {
            try { ReplayFormat.Read(data); }
            catch (ReplayFormatException) { return; }
            throw new InvalidOperationException($"{what}: the defective replay was accepted.");
        }
        Rejected([], "empty");
        for (var length = 0; length < 400; length += 7) Rejected(bytes[..length], $"truncated to {length}");
        for (var length = 400; length < bytes.Length; length += Math.Max(1, bytes.Length / 200)) Rejected(bytes[..length], $"truncated to {length}");
        Rejected(bytes[..^1], "missing final byte");
        for (var position = 0; position < bytes.Length; position += Math.Max(1, bytes.Length / 300))
        {
            var copy = (byte[])bytes.Clone();
            copy[position] ^= 0x10;
            Rejected(copy, $"flipped byte {position}");
        }
        Rejected([..bytes, 0], "trailing byte");
        var badMagic = (byte[])bytes.Clone();
        badMagic[0] = (byte)'X';
        Rejected(badMagic, "bad magic");
        var futureVersion = (byte[])bytes.Clone();
        futureVersion[8] = (byte)(ReplayFormat.CurrentVersion + 1);
        try { ReplayFormat.Read(futureVersion); throw new InvalidOperationException("A foreign version was accepted."); }
        catch (ReplayFormatException exception) { Check.That(exception.Message.Contains("Unsupported", StringComparison.Ordinal), "Version errors are named: " + exception.Message); }

        // Refusal at run time: definitions, command table and tick rate.
        Check.Throws<ReplayRejectedException>(() => ReplayRunner.Run(replay, fixture.Templates, new string('A', 64)), "fingerprint");
        var foreignTable = replay with { Header = replay.Header with { CommandTableVersion = CnCNew.Simulation.Commands.CommandTypes.Version + 1 } };
        Check.Throws<ReplayRejectedException>(() => ReplayRunner.Run(ReplayFormat.Read(ReplayFormat.Write(foreignTable)), fixture.Templates, fixture.Fingerprint), "command table");
        var foreignRate = replay with { Header = replay.Header with { TickRate = 60 } };
        Check.Throws<ReplayRejectedException>(() => ReplayRunner.Run(ReplayFormat.Read(ReplayFormat.Write(foreignRate)), fixture.Templates, fixture.Fingerprint), "60 Hz");
        Check.Throws<ReplayFormatException>(() => ReplayFormat.Write(replay with { Header = replay.Header with { DefinitionFingerprint = "abc" } }), "fingerprint");

        // The fingerprint really depends on the definitions.
        var changed = new SyntheticFixture(files: new Dictionary<string, string>(SyntheticFixture.Files)
        {
            ["Data/INI/Weapon.ini"] = "Weapon TestWeapon\n  PrimaryDamage = 11\nEnd\n",
        });
        Check.That(changed.Fingerprint != fixture.Fingerprint, "A changed definition changes the fingerprint.");
        Check.Throws<ReplayRejectedException>(() => ReplayRunner.Run(replay, changed.Templates, changed.Fingerprint), "different definitions");
    }

    public static void ReplayRuns()
    {
        var (fixture, replay, _) = ShortReplay();
        var straight = ReplayRunner.Run(replay, fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { RecordTickHashes = true });
        Check.That(straight.VerifiedCheckpoints == replay.Checkpoints.Length && straight.VerifiedCheckpoints >= 10, "Every checkpoint was verified.");
        Check.That(straight.SubmittedCommands == replay.Commands.Length, "Every recorded command was submitted.");
        var observed = ReplayRunner.CreateWorld(replay.Header, fixture.Templates);
        ReplayRunner.Run(replay, observed);
        Check.That(observed.RejectionCounts[(int)CommandRejection.UnknownEntity] > 0, "The scenario's deliberately invalid commands are rejected explicitly (and identically in every run).");
        var viaFile = ReplayRunner.Run(ReplayFormat.Read(ReplayFormat.Write(replay)), fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { RecordTickHashes = true });
        Check.That(viaFile.TickHashes.AsSpan().SequenceEqual(straight.TickHashes), "A replay read from bytes reproduces the same hashes.");

        var tamperedCheckpoints = replay.Checkpoints.ToArray();
        tamperedCheckpoints[4] = tamperedCheckpoints[4] with { Hash = tamperedCheckpoints[4].Hash ^ 1 };
        try
        {
            ReplayRunner.Run(replay with { Checkpoints = [..tamperedCheckpoints] }, fixture.Templates, fixture.Fingerprint);
            throw new InvalidOperationException("A wrong checkpoint was not detected.");
        }
        catch (ReplayDivergenceException exception)
        {
            Check.That(exception.Tick == tamperedCheckpoints[4].Tick, "The divergence names the checkpoint tick.");
        }
        var dropped = replay.Commands.RemoveAt(replay.Commands.Length / 2);
        Check.Throws<ReplayDivergenceException>(() => ReplayRunner.Run(replay with { Commands = dropped }, fixture.Templates, fixture.Fingerprint), "diverged");

        // Snapshot continuation equals the straight run (also through the byte format).
        var world = ReplayRunner.CreateWorld(replay.Header, fixture.Templates);
        var head = ReplayRunner.Run(replay, world, new ReplayRunOptions { StopTick = 1234, RecordTickHashes = true });
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
        var tail = ReplayRunner.Run(replay, restored, new ReplayRunOptions { RecordTickHashes = true });
        Check.That(head.TickHashes.Concat(tail.TickHashes).SequenceEqual(straight.TickHashes), "Head + snapshot continuation == straight run, tick by tick.");
        Check.That(tail.FinalHash == straight.FinalHash, "Same final hash.");
    }

    public static void ShortStress()
    {
        var options = new StressOptions(12_000, 1_200, 0xC0FFEE01, 0x5EED0001, 1_500, 1_000);
        var fixture = new SyntheticFixture();
        var scenario = new StressScenario(options);
        var replay = scenario.Record(fixture);
        var maxEntities = 0;
        var maxTimers = 0;
        var world = ReplayRunner.CreateWorld(replay.Header, fixture.Templates);
        var result = ReplayRunner.Run(replay, world, new ReplayRunOptions
        {
            VerifyIncrementalEvery = 500,
            AfterTick = (w, _) =>
            {
                var metrics = w.Metrics;
                maxEntities = Math.Max(maxEntities, metrics.Entities);
                maxTimers = Math.Max(maxTimers, metrics.Timers);
            },
        });
        var end = world.Metrics;
        Check.That(scenario.PeakAlive >= 1_200 && maxEntities >= 1_200, $"The scenario reaches its target population (peak {scenario.PeakAlive}).");
        Check.That(end.Entities == 0 && end.Modules == 0 && end.ScheduledUpdates == 0 && end.Timers == 0 && end.PendingEvents == 0 && end.PendingCommands == 0 && end.PendingSpawns == 0 && end.PendingDestroys == 0,
            $"Every table returns to its baseline: {end}");
        Check.That(end.SpawnedTotal == end.DestroyedTotal && end.SpawnedTotal > 5_000, $"Spawn/destroy balance ({end.SpawnedTotal}/{end.DestroyedTotal}).");
        Check.That(maxTimers > 300, "Timers were exercised.");
        Check.That(world.RejectionCounts[(int)CommandRejection.UnknownEntity] > 0, "Invalid commands were rejected.");
        var again = ReplayRunner.Run(replay, fixture.Templates, fixture.Fingerprint);
        Check.That(again.FinalHash == result.FinalHash, "The stress replay is deterministic.");
    }

    public static void DefinitionOrderIndependence()
    {
        var original = new SyntheticFixture();
        var reversedFiles = SyntheticFixture.Files.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value);
        var permuted = new SyntheticFixture(files: reversedFiles);
        Check.That(original.Fingerprint == permuted.Fingerprint, "The definition fingerprint must not depend on file enumeration order.");
        Check.That(original.Templates.Templates.Select(value => value.Name).SequenceEqual(permuted.Templates.Templates.Select(value => value.Name)), "Template indices must not depend on file enumeration order.");
        var scenario = new StressScenario(ShortOptions);
        var replay = scenario.Record(original);
        var a = ReplayRunner.Run(replay, original.Templates, original.Fingerprint, new ReplayRunOptions { RecordTickHashes = true });
        var b = ReplayRunner.Run(replay, permuted.Templates, permuted.Fingerprint, new ReplayRunOptions { RecordTickHashes = true });
        Check.That(a.TickHashes.AsSpan().SequenceEqual(b.TickHashes), "Permuted definition insertion order must give identical simulation hashes.");
    }

    public static void LongRun()
    {
        // The full acceptance run of the phase: 216,000 ticks (two hours of game time), two fresh runs, snapshot continuation.
        var options = StressOptions.Standard;
        var fixture = new SyntheticFixture();
        var scenario = new StressScenario(options);
        var replay = scenario.Record(fixture);
        Check.That(scenario.PeakAlive >= 10_000, $"At least 10,000 simultaneously living entities (peak {scenario.PeakAlive}).");
        var first = ReplayRunner.Run(replay, fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { RecordTickHashes = true, VerifyIncrementalEvery = 10_000 });
        var second = ReplayRunner.Run(replay, fixture.Templates, fixture.Fingerprint, new ReplayRunOptions { RecordTickHashes = true });
        Check.That(first.TickHashes.Length == 216_000 && first.TickHashes.AsSpan().SequenceEqual(second.TickHashes), "Two fresh 216,000-tick runs are identical tick by tick.");
        var head = ReplayRunner.CreateWorld(replay.Header, fixture.Templates);
        var headRun = ReplayRunner.Run(replay, head, new ReplayRunOptions { StopTick = 108_000, RecordTickHashes = true });
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(head), fixture.Templates, fixture.Fingerprint);
        var tail = ReplayRunner.Run(replay, restored, new ReplayRunOptions { RecordTickHashes = true });
        Check.That(headRun.TickHashes.Concat(tail.TickHashes).SequenceEqual(first.TickHashes), "The snapshot continuation is identical over 216,000 ticks.");
        Check.That(restored.Metrics.SpawnedTotal >= 100_000 && restored.Metrics.Entities == 0 && restored.Metrics.Timers == 0 && restored.Metrics.ScheduledUpdates == 0, "Over 100,000 spawns and everything back at baseline.");
    }

    public static void IdleAllocation()
    {
        var fixture = new SyntheticFixture();
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        for (var index = 0; index < 500; index++) feeder.Spawn(1 + index % 2, "Barrel");
        for (var tick = 0; tick < 100; tick++) world.Step();
        Check.That(world.Metrics.Entities == 500, "Fixture: 500 barrels.");
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var tick = 0; tick < 300; tick++) world.Step();
        var perTick = (GC.GetAllocatedBytesForCurrentThread() - before) / 300;
        Console.WriteLine($"       idle tick with 500 entities: {perTick} bytes allocated per tick");
        Check.That(perTick <= 64, $"An idle tick must not allocate (measured {perTick} bytes).");
    }

    public static void SourceRules()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "CnCNew.slnx"))) root = Path.GetDirectoryName(root);
        Check.That(root is not null, "Repository root not found.");
        var source = Path.Combine(root!, "src", "CnCNew.Simulation");
        var forbidden = new (string Name, Regex Pattern)[]
        {
            ("wall time", new Regex(@"\b(DateTime|DateTimeOffset|Stopwatch|TimeProvider)\b|Environment\.TickCount")),
            ("ambient randomness", new Regex(@"Random\.Shared|System\.Random\b|new Random\(|Guid\.NewGuid|RandomNumberGenerator")),
            ("floating point in state", new Regex(@"\b(float|double|Single|Double)\b|MathF\.|Math\.(Sin|Cos|Sqrt|Pow|Exp|Log)")),
            ("threads in the core", new Regex(@"\bThread\b|Task\.Run|\bParallel\b|ThreadPool|lock \(")),
            ("file system or process access", new Regex(@"\b(File|Directory|Process|Environment)\.")),
            ("reflection", new Regex(@"System\.Reflection|GetType\(\)\.GetMethod|Activator\.|typeof\(.*\)\.GetProperties")),
        };
        var violations = new List<string>();
        foreach (var file in Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            var lineNumber = 0;
            foreach (var raw in File.ReadLines(file))
            {
                lineNumber++;
                var line = Regex.Replace(raw, "\"(\\\\.|[^\"\\\\])*\"", "\"\"");
                var comment = line.IndexOf("//", StringComparison.Ordinal);
                if (comment >= 0) line = line[..comment];
                foreach (var (name, pattern) in forbidden)
                    if (pattern.IsMatch(line)) violations.Add($"{Path.GetRelativePath(root!, file)}({lineNumber}): {name}: {raw.Trim()}");
            }
        }
        Check.That(violations.Count == 0, "Forbidden constructs in the simulation:\n  " + string.Join("\n  ", violations));

        // A restored update module without its saved wake tick would be re-armed one tick late (World.Register) and the run would drift from the live one.
        var missingWake = new List<string>();
        foreach (var file in Directory.EnumerateFiles(Path.Combine(source, "Modules"), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") || file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) continue;
            foreach (var chunk in Regex.Split(File.ReadAllText(file), @"(?m)^(?=(?:public |internal )?(?:sealed |abstract )*class )").Skip(1))
            {
                var header = chunk[..Math.Max(chunk.IndexOf('\n'), 0)];
                if (header.Contains("IUpdateModule", StringComparison.Ordinal) && !chunk.Contains("\"nextWake\"", StringComparison.Ordinal))
                    missingWake.Add($"{Path.GetRelativePath(root!, file)}: {header.Trim()}");
            }
        }
        Check.That(missingWake.Count == 0, "Update modules must save and restore their wake tick (\"nextWake\"):\n  " + string.Join("\n  ", missingWake));

        var references = typeof(SimulationWorld).Assembly.GetReferencedAssemblies().Select(value => value.Name!).ToArray();
        var allowed = references.Where(name => name == "CnCNew.Data" || name.StartsWith("System", StringComparison.Ordinal) || name is "netstandard" or "mscorlib").ToArray();
        Check.That(references.Except(allowed).Count() == 0, "The simulation may only reference CnCNew.Data and the BCL, found: " + string.Join(", ", references.Except(allowed)));
        Check.That(references.Contains("CnCNew.Data"), "The simulation builds on the data layer.");
        var project = File.ReadAllText(Path.Combine(source, "CnCNew.Simulation.csproj"));
        Check.That(project.Contains("CnCNew.Data.csproj", StringComparison.Ordinal) && !project.Contains("PackageReference", StringComparison.Ordinal) && Regex.Matches(project, "ProjectReference").Count == 1,
            "The project file references only CnCNew.Data and no package.");
    }

    public static void RealData(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var source = new RealSource(content);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(source, ModuleRegistry.Default, options);
        var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default);
        SpecialPowerRealData(catalog);
        var report = ModuleCoverage.Compute(catalog);
        Console.WriteLine($"       real data: {report.Objects} objects, {report.Instantiable} instantiable, {report.Blocked} blocked, {report.WithoutSimulationModules} without simulation modules");
        foreach (var blocker in report.Blockers.Take(8)) Console.WriteLine($"         blocker {blocker.Module}: {blocker.ObjectsBlocked} objects ({blocker.ObjectsBlockedOnlyByThisModule} only by it)");
        Check.That(report.Objects == graph.Definitions.OfType<ObjectDefinition>().Count(), "Every object definition has a template.");

        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "P1"));
        ulong[] Run()
        {
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), 77, players, "shipped-objects", fingerprint), catalog);
            var sequence = 0UL;
            foreach (var template in catalog.Templates.Where(value => value.IsInstantiable))
                world.Submit(new SpawnCommand(new PlayerId(1), ++sequence, 0, template.Name, PlayerId.System, Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero));
            var hashes = new List<ulong> { world.Step().StateHash };
            Check.That(world.Metrics.Entities == report.Instantiable, $"All {report.Instantiable} instantiable objects spawn ({world.Metrics.Entities}).");
            for (var tick = 1; tick < 5; tick++) hashes.Add(world.Step().StateHash);
            world.VerifyIncrementalHash();
            var system = 0UL;
            foreach (var entity in world.Entities.OrderBy(value => value.Id.Value).ToArray())
                world.Submit(new KillCommand(PlayerId.System, ++system, world.Tick, entity.Id));
            for (var tick = 0; tick < 400; tick++) hashes.Add(world.Step().StateHash);
            world.VerifyIncrementalHash();
            return [..hashes];
        }
        Check.That(Run().AsSpan().SequenceEqual(Run()), "Instantiating and killing every instantiable shipped object twice gives identical tick hashes.");
    }
}

static partial class Tests
{
    /// <summary>The retail reference match: at least two factions play a full match on the shipped data, twice, plus a mid-match snapshot continuation.</summary>
    public static void RealReferenceMatch(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var participants = ReferenceMatchScenario.Participants(catalog, out _);
        Check.That(participants.Length >= 2, $"At least two retail factions can field a base and an armed unit ({participants.Length}).");

        var scenario = new ReferenceMatchScenario(ReferenceMatchOptions.Standard);
        var recorded = scenario.Record(catalog, fingerprint);
        Check.That(scenario.Winner is not null && scenario.VictoryTick > 0, $"The retail match ends with a winner (orders {scenario.Orders}, shots {scenario.Shots}, production {scenario.ProductionOrders}).");
        Check.That(scenario.ProductionOrders > 10 && scenario.Shots > 100, "The generator plays a real match.");
        Check.That(scenario.StartObjectsSkipped == 0, $"Every start object of every faction is instantiable ({scenario.StartObjectsSkipped} skipped).");
        Check.That(scenario.SupplyDeliveries > 0, $"Trucks deliver supplies to the supply centers ({scenario.SupplyDeliveries} deliveries, {scenario.SupplyDeliveryMoney} money).");
        Console.WriteLine($"       retail match: {participants.Length} factions, {recorded.Commands.Length} commands, peak {scenario.PeakAlive} objects, winner {scenario.WinnerFaction} at tick {scenario.VictoryTick}, {scenario.SupplyDeliveries} supply deliveries");

        var checkpoints = ReplayRunner.Run(recorded, catalog, fingerprint, new ReplayRunOptions { RecordCheckpointInterval = ReferenceMatchOptions.Standard.CheckpointInterval }).Checkpoints;
        var replay = ReplayFormat.Read(ReplayFormat.Write(recorded with { Checkpoints = checkpoints }));
        var first = ReplayRunner.Run(replay, catalog, fingerprint, new ReplayRunOptions { RecordTickHashes = true, VerifyIncrementalEvery = 5_000 });
        Check.That(first.VerifiedCheckpoints == replay.Checkpoints.Length && first.RejectedCommands == 0, $"Every checkpoint verifies and no command is rejected ({first.RejectedCommands}).");
        var second = ReplayRunner.Run(replay, catalog, fingerprint, new ReplayRunOptions { RecordTickHashes = true });
        Check.That(first.TickHashes.AsSpan().SequenceEqual(second.TickHashes) && first.FinalHash == second.FinalHash, "Two fresh runs are hash identical on every tick.");

        var half = replay.Header.EndTick / 2;
        var head = ReplayRunner.CreateWorld(replay.Header, catalog);
        var headRun = ReplayRunner.Run(replay, head, new ReplayRunOptions { StopTick = half, RecordTickHashes = true });
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(head), catalog, fingerprint);
        var tail = ReplayRunner.Run(replay, restored, new ReplayRunOptions { RecordTickHashes = true });
        Check.That(headRun.TickHashes.Concat(tail.TickHashes).SequenceEqual(second.TickHashes), "A snapshot taken mid-match continues hash identically.");
    }
}


file sealed class RealSource(ContentFileSystem content) : IDefinitionSource
{
    public IEnumerable<string> EnumerateIniFiles() => content.EnumerateFiles().Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
    public Stream OpenRead(string virtualPath) => content.OpenRead(virtualPath);
    public bool Exists(string virtualPath) => content.Exists(virtualPath);
}
