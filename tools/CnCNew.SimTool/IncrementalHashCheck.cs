using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Text;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.World;

namespace CnCNew.SimTool;

internal enum VerifyMode
{
    None,
    /// <summary>After every checked tick: <see cref="SimulationWorld.VerifyIncrementalHash"/>.</summary>
    Verify,
    /// <summary>As <see cref="Verify"/>, and before it: which objects changed since the world last hashed them, and in which fields.</summary>
    Explain,
}

/// <summary>
/// Finding D8 of <c>docs/handoffs/HANDOFF-PHASE18.md</c>: the world keeps the hash of every object and re-hashes only the objects marked dirty. A state change
/// that forgets <c>MarkDirty</c> leaves a stale hash that nobody notices until something recomputes all of them (a snapshot does). This class counts
/// the ticks in which the incremental hash differs from a full recomputation and, in <see cref="VerifyMode.Explain"/>, names the object and the fields.
/// <para>
/// The explanation uses only what the simulation already offers (<see cref="Entity.Describe"/> with a writer of its own and <see cref="HashStateWriter"/>)
/// plus one read of the internal <c>Entity.CachedHash</c> by reflection: an object is stale when the hash of its present description is not the hash the
/// world has cached for it. The description the cached hash belongs to is remembered here from the last tick in which both agreed.
/// </para>
/// </summary>
internal sealed class IncrementalVerifier(VerifyMode mode, int interval = 1)
{
    private const int MaxExamples = 4;
    private const int MaxFindings = 12;

    private static readonly Lazy<Func<Entity, ulong>> CachedHash = new(() =>
    {
        var getter = typeof(Entity).GetProperty("CachedHash", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetMethod
                     ?? throw new InvalidOperationException("Entity.CachedHash no longer exists; --explain-incremental reads it by reflection and has to be adapted.");
        return getter.CreateDelegate<Func<Entity, ulong>>();
    });

    private readonly List<string> _first = [];
    private readonly HashStateWriter _hasher = new();
    private readonly Dictionary<uint, (ulong Hash, string Text)> _known = [];
    private readonly SortedDictionary<string, (long Count, long FirstTick, string Example)> _findings = new(StringComparer.Ordinal);

    public bool Enabled => mode != VerifyMode.None;
    public long Ticks { get; private set; }
    public long Mismatches { get; private set; }

    /// <summary>Call after a tick. The world's tick already counts the tick that just ran.</summary>
    public void AfterTick(SimulationWorld world)
    {
        if (mode == VerifyMode.None || world.Tick % interval != 0) return;
        Ticks++;
        var stale = mode == VerifyMode.Explain ? FindStale(world) : -1;
        try
        {
            world.VerifyIncrementalHash();
        }
        catch (InvalidOperationException exception)
        {
            Mismatches++;
            if (_first.Count < MaxExamples) _first.Add($"after tick {world.Tick - 1} ({world.Entities.Count} objects): {exception.Message}");
            if (stale == 0) Record("no object is stale: the difference is in the timer or command sums", world.Tick - 1, string.Empty);
        }
    }

    public JobResult Result(Manifests.ManifestObject values) => new(values, Ticks, Mismatches, [.._first],
        [.._findings.OrderByDescending(pair => pair.Value.Count).ThenBy(pair => pair.Key, StringComparer.Ordinal).Take(MaxFindings)
            .Select(pair => $"{pair.Value.Count}x, first after tick {pair.Value.FirstTick}: {pair.Key}{(pair.Value.Example.Length == 0 ? string.Empty : " | e.g. " + pair.Value.Example)}")]);

    private void Record(string key, long tick, string example)
        => _findings[key] = _findings.TryGetValue(key, out var known) ? (known.Count + 1, known.FirstTick, known.Example) : (1, tick, example);

    /// <summary>Compares every object's present hash with the world's cached one; remembers the description of every object whose cache is current.</summary>
    private int FindStale(SimulationWorld world)
    {
        // Reading the hash re-hashes whatever is marked dirty right now (a host may have changed the world after the step); what is stale after that was never marked.
        _ = world.StateHash;
        var stale = 0;
        foreach (var entity in world.Entities)
        {
            _hasher.Reset();
            entity.Describe(_hasher);
            var now = _hasher.Hash;
            var cached = CachedHash.Value(entity);
            var id = entity.Id.Value;
            if (now == cached)
            {
                if (!_known.TryGetValue(id, out var known) || known.Hash != now) _known[id] = (now, Lines(entity));
                continue;
            }
            stale++;
            var present = Lines(entity);
            var changes = _known.TryGetValue(id, out var before) && before.Hash == cached ? Changes(before.Text, present) : ["(the hashed description is not known: the object was never seen with a current hash)"];
            var fields = string.Join(", ", changes.Select(change => change.Split(':')[0]).Distinct(StringComparer.Ordinal).Take(8));
            Record($"{entity.Template.Name}: {fields}", world.Tick - 1, $"object #{id}: {string.Join("; ", changes.Take(6))}");
        }
        return stale;
    }

    private static string Lines(Entity entity)
    {
        var writer = new PathStateWriter();
        entity.Describe(writer);
        return writer.ToString();
    }

    /// <summary>The fields whose value differs, as <c>path: before -> now</c>; an array element is named by its first text field (a module by its name).</summary>
    private static List<string> Changes(string before, string now)
    {
        static Dictionary<string, string> Parse(string text)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var split = line.IndexOf('\t');
                result[line[..split]] = line[(split + 1)..];
            }
            return result;
        }
        var left = Parse(before);
        var right = Parse(now);
        var changes = new List<string>();
        foreach (var path in left.Keys.Union(right.Keys).Order(StringComparer.Ordinal))
        {
            var old = left.GetValueOrDefault(path, "<absent>");
            var current = right.GetValueOrDefault(path, "<absent>");
            if (old != current) changes.Add($"{path}: {old} -> {current}");
        }
        return changes;
    }

    /// <summary>Writes a description as lines <c>path TAB value</c>. Elements of an array are addressed by the first text they contain when they have one, else by position.</summary>
    private sealed class PathStateWriter : IStateWriter
    {
        private readonly StringBuilder _text = new();
        private readonly List<string> _path = [];
        private readonly List<int> _counters = [];
        private readonly List<int> _labelAt = [];

        public override string ToString() => _text.ToString();

        public void BeginObject(string? name)
        {
            if (name is null && _counters.Count > 0)
            {
                _path.Add($"[{_counters[^1]++}]");
                _labelAt.Add(_path.Count - 1);
            }
            else
            {
                _path.Add(name ?? string.Empty);
                _labelAt.Add(-1);
            }
            _counters.Add(-1);
        }

        public void EndObject()
        {
            _counters.RemoveAt(_counters.Count - 1);
            _path.RemoveAt(_path.Count - 1);
            _labelAt.RemoveAt(_labelAt.Count - 1);
        }

        public void BeginArray(string name)
        {
            _path.Add(name);
            _labelAt.Add(-1);
            _counters.Add(0);
        }

        public void EndArray() => EndObject();

        public void Bool(string name, bool value) => Line(name, value ? "true" : "false");
        public void Int(string name, long value) => Line(name, value.ToString(CultureInfo.InvariantCulture));
        public void UInt(string name, ulong value) => Line(name, value.ToString(CultureInfo.InvariantCulture));
        public void Fixed(string name, Fixed value) => Line(name, "raw " + value.Raw.ToString(CultureInfo.InvariantCulture));

        public void Text(string name, string value)
        {
            // The first text of an array element names it from then on (modules write their name first).
            if (_labelAt.Count > 0 && _labelAt[^1] is >= 0 and var index && _path[index].EndsWith(']') && !_path[index].Contains(' '))
                _path[index] = _path[index][..^1] + " " + value + "]";
            Line(name, "\"" + value + "\"");
        }

        private void Line(string name, string value)
        {
            if (_counters.Count > 0 && _counters[^1] >= 0) name = $"{name}[{_counters[^1]++}]";
            _text.Append(string.Join('.', _path.Where(part => part.Length > 0))).Append('.').Append(name).Append('\t').Append(value.Replace('\n', ' ').Replace('\t', ' ')).Append('\n');
        }
    }
}
