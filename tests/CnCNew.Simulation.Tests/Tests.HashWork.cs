using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>
/// The incremental state hash writes the words of a description down and folds several entities side by side (docs/PERF-HASH.md). These tests hold the two shortcuts to the
/// plain definition: <see cref="HashStateWriter"/> fed by <see cref="Entity.Describe"/>.
/// </summary>
static partial class Tests
{
    /// <summary>A small fixed generator for test data (not the simulation's).</summary>
    private static ulong NextTestWord(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        var z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    public static void HashWordsAndStreamsEqualTheSequentialHash()
    {
        // Every kind of call: the words written down, folded, are the hash writer's value.
        static void Feed(IStateWriter writer)
        {
            writer.BeginObject(null);
            writer.UInt("id", 42);
            writer.Int("negative", -7);
            writer.Int("min", long.MinValue);
            writer.Bool("yes", true);
            writer.Bool("no", false);
            writer.Fixed("fixed", Fixed.FromDecimal(-12.375m));
            writer.Text("empty", string.Empty);
            writer.Text("ascii", "ModuleTag_01");
            writer.Text("other", "Größe 中😀\0end");
            writer.BeginArray("list");
            for (var index = 0; index < 5000; index++) writer.Int("value", index * 977L);
            writer.BeginObject("inner");
            writer.Text("long", new string('x', 9000));
            writer.EndObject();
            writer.EndArray();
            writer.EndObject();
        }
        var hasher = new HashStateWriter();
        Feed(hasher);
        var words = new WordStateWriter();
        Feed(words);
        Check.That(StateHash.Fold(StateHash.Seed, words.Words) == hasher.Hash, "Folding the written words gives the hash writer's value.");
        // 12 calls that are one word each, 5000 list values, and four texts: one word for the length plus one per UTF-16 unit (13 units in the third text: the emoji is two).
        Check.That(words.Count == 12 + 5000 + (1 + 0) + (1 + 12) + (1 + 13) + (1 + 9000), $"One word per call, a text its length and one word per UTF-16 unit ({words.Count}).");

        // Append and Truncate: copied words are the same words; truncating forgets exactly the tail.
        var copy = new WordStateWriter();
        copy.UInt("lead", 1);
        var mark = copy.Count;
        copy.Text("dropped", "dropped");
        copy.Truncate(mark);
        copy.Append(words.Words);
        copy.Append(words.Words);
        hasher.Reset();
        hasher.UInt("lead", 1);
        Feed(hasher);
        Feed(hasher);
        Check.That(StateHash.Fold(StateHash.Seed, copy.Words) == hasher.Hash, "Appended words fold like the calls that produced them.");
        Check.Throws<ArgumentOutOfRangeException>(() => copy.Truncate(copy.Count + 1), "count");

        // Streams of every length next to each other, also empty ones, also fewer than four: each hash is the sequential fold of its own words.
        var state = 2026UL;
        for (var round = 0; round < 400; round++)
        {
            var streams = (int)(NextTestWord(ref state) % 71);
            var ends = new int[streams];
            var total = 0;
            for (var index = 0; index < streams; index++)
            {
                var kind = NextTestWord(ref state) % 10;
                total += kind == 0 ? 0 : kind < 4 ? (int)(NextTestWord(ref state) % 5) : (int)(NextTestWord(ref state) % 400);
                ends[index] = total;
            }
            var data = new ulong[total];
            for (var index = 0; index < total; index++) data[index] = NextTestWord(ref state);
            var hashes = new ulong[streams];
            Array.Fill(hashes, 0xDEADUL);
            StateHash.FoldStreams(data, ends, hashes);
            for (var index = 0; index < streams; index++)
            {
                var from = index == 0 ? 0 : ends[index - 1];
                hasher.Reset();
                for (var word = from; word < ends[index]; word++) hasher.UInt("word", data[word]);
                Check.That(hashes[index] == hasher.Hash, $"Round {round}: stream {index} of {streams} ({ends[index] - from} words) folds to the sequential hash.");
            }
        }
    }

    /// <summary>For every entity of the world: the description written down as words (with the prepared module openings) folds to the hash of the plain description.</summary>
    private static int CheckWordDescriptions(SimulationWorld world, HashStateWriter hasher, WordStateWriter words)
    {
        var count = 0;
        foreach (var entity in world.Entities)
        {
            hasher.Reset();
            entity.Describe(hasher);
            words.Reset();
            entity.Describe(words);
            Check.That(StateHash.Fold(StateHash.Seed, words.Words) == hasher.Hash, $"Tick {world.Tick}: the word description of {entity.Template.Name} ({entity.Id.Value}) folds to its hash.");
            // A writer that keeps names (the snapshot) gets the same values in the same order: where a description saves building a name for the hash, it changes nothing else.
            NamedHash.Reset();
            entity.Describe(NamedHash);
            Check.That(NamedHash.Hash == hasher.Hash, $"Tick {world.Tick}: {entity.Template.Name} ({entity.Id.Value}) describes the same values to a writer that keeps names.");
            count++;
        }
        return count;
    }

    private static readonly NameKeepingHashWriter NamedHash = new();

    /// <summary>Mixes like <see cref="HashStateWriter"/> but is a writer that keeps names (<see cref="IStateWriter.KeepsNames"/> stays true) and insists on getting real ones.</summary>
    private sealed class NameKeepingHashWriter : IStateWriter
    {
        private readonly HashStateWriter _inner = new();
        private readonly Stack<HashSet<string>?> _scopes = new();
        public ulong Hash => _inner.Hash;

        public void Reset()
        {
            _inner.Reset();
            _scopes.Clear();
        }

        /// <summary>Inside an object every name is used once: a shortened name ("tower" for "tower0" and "tower1") would collide here.</summary>
        private void Name(string name)
        {
            if (_scopes.Count > 0 && _scopes.Peek() is { } scope) Check.That(scope.Add(name), $"The name '{name}' is written twice into one object.");
        }

        public void BeginObject(string? name)
        {
            if (name is not null) Name(name);
            _scopes.Push(new HashSet<string>(StringComparer.Ordinal));
            _inner.BeginObject(name);
        }

        public void EndObject()
        {
            _scopes.Pop();
            _inner.EndObject();
        }

        public void BeginArray(string name)
        {
            Name(name);
            _scopes.Push(null);
            _inner.BeginArray(name);
        }

        public void EndArray()
        {
            _scopes.Pop();
            _inner.EndArray();
        }

        public void Bool(string name, bool value) { Name(name); _inner.Bool(name, value); }
        public void Int(string name, long value) { Name(name); _inner.Int(name, value); }
        public void UInt(string name, ulong value) { Name(name); _inner.UInt(name, value); }
        public void Fixed(string name, Fixed value) { Name(name); _inner.Fixed(name, value); }
        public void Text(string name, string value) { Name(name); _inner.Text(name, value); }
    }

    /// <summary>
    /// Watches the scheduler: every module update must leave its entity with a different description at the end of the update phase (the wake tick alone moves it). That is why
    /// "do not describe an entity again after an update that changed nothing" has no case to apply to; if this ever fails, such a case exists and the question is open again.
    /// </summary>
    private sealed class UpdateChangeWatch(SimulationWorld world) : ITickPhaseObserver
    {
        private readonly HashStateWriter _hasher = new();
        private readonly List<(Entity Entity, SimModule Module, ulong Before)> _open = [];
        public long Updates { get; private set; }
        public int DirtyAtHash { get; private set; }

        private ulong Hash(Entity entity)
        {
            _hasher.Reset();
            entity.Describe(_hasher);
            return _hasher.Hash;
        }

        public void PhaseStarted(TickPhase phase)
        {
            if (phase == TickPhase.Hash) DirtyAtHash = world.DirtyEntityCount;
        }

        public void PhaseEnded(TickPhase phase)
        {
            if (phase != TickPhase.Updates) return;
            foreach (var (entity, module, before) in _open)
                Check.That(Hash(entity) != before, $"Tick {world.Tick}: the update of {module.ModuleName} left {entity.Template.Name} ({entity.Id.Value}) with the description it had.");
            _open.Clear();
        }

        public void UpdateStarted(SimModule module)
        {
            Updates++;
            _open.Add((module.Entity, module, Hash(module.Entity)));
        }

        public void UpdateEnded(SimModule module) { }
    }

    public static void HashWorkOnTheSyntheticFixture()
    {
        var fixture = new SyntheticFixture();
        var hasher = new HashStateWriter();
        var words = new WordStateWriter();

        // Every instantiable template, alive and dying.
        var world = NewWorld(fixture, 11);
        var watch = new UpdateChangeWatch(world);
        world.PhaseObserver = watch;
        var feeder = new Feeder(world);
        var templates = fixture.Templates.Templates.Where(value => value.IsInstantiable).ToArray();
        Check.That(templates.Length >= 8, $"The fixture has instantiable templates ({templates.Length}).");
        for (var copy = 0; copy < 3; copy++)
            foreach (var template in templates) Check.That(feeder.Spawn(1 + copy % 2, template.Name, x: copy * 10).Accepted, template.Name + " spawns.");
        var described = 0;
        for (var tick = 0; tick < 40; tick++)
        {
            world.Step();
            world.VerifyIncrementalHash();
            described += CheckWordDescriptions(world, hasher, words);
        }
        foreach (var entity in world.Entities.ToArray()) feeder.Kill(entity.Id.Value);
        for (var tick = 0; tick < 200; tick++)
        {
            world.Step();
            world.VerifyIncrementalHash();
            described += CheckWordDescriptions(world, hasher, words);
        }
        Check.That(described > 1000 && watch.Updates > 0, $"Descriptions were compared and module updates watched ({described}, {watch.Updates}).");

        // An idle entity stays clean and is not described again; a change marks exactly the changed one.
        var idle = NewWorld(fixture, 12);
        var idleWatch = new UpdateChangeWatch(idle);
        idle.PhaseObserver = idleWatch;
        var idleFeeder = new Feeder(idle);
        for (var index = 0; index < 5; index++) idleFeeder.Spawn(1, "Barrel", x: index * 20);
        for (var tick = 0; tick < 20; tick++) idle.Step();
        var before = idle.HashWorkDone;
        for (var tick = 0; tick < 50; tick++)
        {
            idle.Step();
            Check.That(idleWatch.DirtyAtHash == 0, "No update ran and nothing changed: no entity is marked.");
        }
        Check.That(idle.HashWorkDone == before, "Idle ticks describe no entity again.");
        var victim = idle.Entities.First();
        idleFeeder.Damage(victim.Id.Value, 1m);
        idle.Step();
        var after = idle.HashWorkDone;
        Check.That(idleWatch.DirtyAtHash == 1 && after.Entities == before.Entities + 1 && after.Unchanged == before.Unchanged && after.Words > before.Words,
            $"One damaged entity is marked, described once and comes out with another hash ({idleWatch.DirtyAtHash}, {after.Entities - before.Entities}, {after.Unchanged - before.Unchanged}).");
        idle.VerifyIncrementalHash();

        // The counters are not state: a world that hashed a lot and a restored copy of it agree, and the stress replay hashes as it always did while every tick is verified.
        var (stressFixture, replay, _) = ShortReplay();
        var plain = ReplayRunner.Run(replay, stressFixture.Templates, stressFixture.Fingerprint, new ReplayRunOptions { RecordTickHashes = true });
        var watchedWorld = ReplayRunner.CreateWorld(replay.Header, stressFixture.Templates);
        var stressWatch = new UpdateChangeWatch(watchedWorld);
        watchedWorld.PhaseObserver = stressWatch;
        var watched = ReplayRunner.Run(replay, watchedWorld, new ReplayRunOptions
        {
            RecordTickHashes = true,
            VerifyIncrementalEvery = 1,
            AfterTick = (_, result) =>
            {
                if (result.Tick % 97 == 0) CheckWordDescriptions(watchedWorld, hasher, words);
            },
        });
        Check.That(watched.TickHashes.AsSpan().SequenceEqual(plain.TickHashes), "The verified, watched stress run hashes like the plain one.");
        var work = watchedWorld.HashWorkDone;
        Check.That(work.Entities > 0 && work.Words > work.Entities && stressWatch.Updates > 0, $"The stress run described entities and ran updates ({work.Entities}, {work.Words}, {stressWatch.Updates}).");
    }

    /// <summary>The same on the shipped data: every instantiable object, alive for a while and then dying, every module type with its first updates.</summary>
    public static void HashWorkOnRealData(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "P1"));
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), 77, players, "shipped-objects", fingerprint), catalog);
        var watch = new UpdateChangeWatch(world);
        world.PhaseObserver = watch;
        var sequence = 0UL;
        var instantiable = 0;
        foreach (var template in catalog.Templates.Where(value => value.IsInstantiable))
        {
            world.Submit(new SpawnCommand(new PlayerId(1), ++sequence, 0, template.Name, PlayerId.System, Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero));
            instantiable++;
        }
        var hasher = new HashStateWriter();
        var words = new WordStateWriter();
        var described = 0;
        var moduleTypes = new HashSet<string>(StringComparer.Ordinal);
        for (var tick = 0; tick < 30; tick++)
        {
            world.Step();
            world.VerifyIncrementalHash();
            described += CheckWordDescriptions(world, hasher, words);
            if (tick == 0)
                foreach (var entity in world.Entities)
                    foreach (var module in entity.Modules) moduleTypes.Add(module.ModuleName);
        }
        var system = 0UL;
        foreach (var entity in world.Entities.ToArray()) world.Submit(new KillCommand(PlayerId.System, ++system, world.Tick, entity.Id));
        for (var tick = 0; tick < 300; tick++)
        {
            world.Step();
            if (tick % 10 != 0) continue;
            world.VerifyIncrementalHash();
            described += CheckWordDescriptions(world, hasher, words);
        }
        var work = world.HashWorkDone;
        Console.WriteLine($"       real data: {instantiable} instantiable templates, {moduleTypes.Count} module types, {described} descriptions compared, {watch.Updates} module updates watched; "
                          + $"incremental hash: {work.Entities} entities described, {work.Words} words, {work.Unchanged} unchanged");
        Check.That(described > instantiable * 30 && watch.Updates > instantiable, "Every instantiable shipped object was described and updated.");
    }
}
