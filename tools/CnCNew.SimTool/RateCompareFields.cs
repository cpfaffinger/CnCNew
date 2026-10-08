using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.SimTool;

/// <summary>
/// <c>rate-compare --ai-diff A,B --fields</c>: the first logic frame of the original after which any described field of any object differs between two rates of one skirmish of
/// the group <c>ai</c>, and <c>--trace ID[,ID…] --trace-from F --trace-to G</c>: the fields of single objects tick by tick at both rates. A diagnosis, not a measurement.
/// <para>
/// Fields are compared in game units: an integer or a fixed-point value of the faster rate counts as equal when it is the slower rate's value times the ratio of the rates (a tick,
/// a duration in ticks, a value per tick). Fields that exist only above 30 Hz (the bookkeeping of a frame's shares) are skipped unless <c>--strict</c> is given.
/// </para>
/// </summary>
internal static class RateCompareFields
{
    public static int Run(string[] args, string pairing, RateCatalogs catalogs, SimulationRate rateA, SimulationRate rateB, int shown)
    {
        var factions = pairing.Split(',');
        var limit = long.Parse(Value(args, "--frames") ?? "14400", CultureInfo.InvariantCulture);
        var from = long.Parse(Value(args, "--from") ?? "0", CultureInfo.InvariantCulture);
        var strict = args.Contains("--strict");
        var atFirst = Value(args, "--at") == "first";
        // A module's wake tick is bookkeeping of the scheduler (a module that wakes every tick and one that sleeps until it is called differ without any difference in the game).
        var ignore = new Regex(Value(args, "--ignore") ?? @"\.nextWake$|\.previousHealth$", RegexOptions.CultureInvariant);
        var trace = (Value(args, "--trace") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(text => uint.Parse(text, CultureInfo.InvariantCulture)).ToArray();
        var traceFrom = long.Parse(Value(args, "--trace-from") ?? "9223372036854775807", CultureInfo.InvariantCulture);
        var traceTo = long.Parse(Value(args, "--trace-to") ?? "9223372036854775807", CultureInfo.InvariantCulture);
        var a = RateCases.SkirmishAiSession(catalogs, rateA, factions[0], factions[1]);
        var b = RateCases.SkirmishAiSession(catalogs, rateB, factions[0], factions[1]);
        var ratio = rateB.TicksPerOriginalFrame / rateA.TicksPerOriginalFrame;
        var last = new Dictionary<(int, uint), Dictionary<string, Field>>();
        // A difference counts once it has lasted this many frames: a state that one rate reaches a share of a frame earlier and the other catches up with is no divergence.
        var persist = int.Parse(Value(args, "--persist") ?? "3", CultureInfo.InvariantCulture);
        var streaks = new Dictionary<string, (long Since, int Count)>(StringComparer.Ordinal);
        for (long frame = 0; frame < limit && !a.IsOver && !b.IsOver; frame++)
        {
            var tracing = trace.Length > 0 && frame >= traceFrom && frame <= traceTo;
            // --at first: the worlds are compared after the first tick of every frame, where every decision of the frame has fallen, with positions and angles read where
            // the frame ends - what an observer of that tick reads. Without it they are compared after the last tick of the frame.
            StepFrame(a, rateA, frame, tracing, trace, last, 0, 0, atFirst ? 1 : rateA.TicksPerOriginalFrame);
            StepFrame(b, rateB, frame, tracing, trace, last, 1, 0, atFirst ? 1 : rateB.TicksPerOriginalFrame);
            if (atFirst)
            {
                var first = Compare();
                StepFrame(a, rateA, frame, tracing, trace, last, 0, 1, rateA.TicksPerOriginalFrame);
                StepFrame(b, rateB, frame, tracing, trace, last, 1, 1, rateB.TicksPerOriginalFrame);
                if (first) return 1;
                continue;
            }
            if (Compare()) return 1;

            bool Compare()
            {
            if (frame < from || (trace.Length > 0 && frame <= traceTo)) return false;
            var found = new Dictionary<string, string>(StringComparer.Ordinal);
            var others = new Dictionary<uint, Entity>();
            foreach (var entity in b.World.Entities) others[entity.Id.Value] = entity;
            foreach (var entity in a.World.Entities)
            {
                if (!others.Remove(entity.Id.Value, out var other))
                {
                    found[$"{entity.Id.Value} {entity.Template.Name}"] = $"exists only at {rateA.TicksPerSecond} Hz";
                    continue;
                }
                var building = entity.HasStatus(CnCNew.Simulation.Modules.ObjectStatusBits.UnderConstruction);
                var left = Fields(entity, a.World.Tick, atFirst);
                var right = Fields(other, b.World.Tick, atFirst);
                foreach (var path in left.Keys.Union(right.Keys).Order(StringComparer.Ordinal))
                {
                    if (ignore.IsMatch(path)) continue;
                    // A construction site is half a frame of work ahead above 30 Hz by design (the first work step counts a whole frame, so the building is complete in the tick of the 30 Hz step).
                    if (building && (path.EndsWith(".health", StringComparison.Ordinal) || path.EndsWith(".previousHealth", StringComparison.Ordinal) || path == ".constructionWork")) continue;
                    var hasLeft = left.TryGetValue(path, out var x);
                    var hasRight = right.TryGetValue(path, out var y);
                    if (hasLeft && hasRight && Same(x, y, ratio)) continue;
                    if (!hasLeft && !strict) continue;
                    found[$"{entity.Id.Value} {entity.Template.Name} owner {entity.Owner.Value} {path}"] = $"{rateA.TicksPerSecond} Hz {(hasLeft ? x.ToString() : "<absent>")} | {rateB.TicksPerSecond} Hz {(hasRight ? y.ToString() : "<absent>")}";
                }
            }
            foreach (var entity in others.Values) found[$"{entity.Id.Value} {entity.Template.Name}"] = $"exists only at {rateB.TicksPerSecond} Hz";
            foreach (var key in streaks.Keys.Where(key => !found.ContainsKey(key)).ToArray()) streaks.Remove(key);
            var different = 0;
            foreach (var (key, text) in found.OrderBy(item => item.Key, StringComparer.Ordinal))
            {
                var streak = streaks.TryGetValue(key, out var had) ? (had.Since, had.Count + 1) : (frame, 1);
                streaks[key] = streak;
                if (streak.Item2 < persist) continue;
                if (different++ < shown) Console.WriteLine($"since frame {streak.Item1}: {key}: {text}");
            }
            if (different > 0)
            {
                var since = streaks.Values.Where(item => item.Count >= persist).Min(item => item.Since);
                Console.WriteLine($"First lasting difference after frame {since} ({(since / 30m).ToString("0.0000", CultureInfo.InvariantCulture)} s of game time): {different} fields or objects differ for {persist} frames.");
                return true;
            }
            return false;
            }
        }
        Console.WriteLine(trace.Length > 0 ? "Trace done." : $"All compared fields are equal at the end of each of {limit} frames.");
        return 0;
    }

    private static void StepFrame(SkirmishMatchSession session, SimulationRate rate, long frame, bool tracing, uint[] trace, Dictionary<(int, uint), Dictionary<string, Field>> last, int side, int fromTick, int toTick)
    {
        for (var tick = fromTick; tick < toTick; tick++)
        {
            session.Step();
            if (!tracing) continue;
            foreach (var id in trace)
            {
                var entity = session.World.FindEntity(new CnCNew.Simulation.Ids.EntityId(id));
                var now = entity is null ? [] : Fields(entity, 0, false);
                var had = last.TryGetValue((side, id), out var before);
                if (!had)
                {
                    last[(side, id)] = now;
                    continue;
                }
                var text = new StringBuilder();
                if (entity is null && before!.Count > 0) text.Append("  <the object is gone>\n");
                else
                foreach (var path in now.Keys.Union(had ? before!.Keys : []).Order(StringComparer.Ordinal))
                {
                    var hasNow = now.TryGetValue(path, out var value);
                    var hasOld = had && before!.TryGetValue(path, out var old) && (!hasNow || old.Equals(value));
                    if (hasNow && hasOld) continue;
                    text.Append("  ").Append(path).Append(" = ").Append(hasNow ? value.ToString() : "<gone>").Append('\n');
                }
                last[(side, id)] = now;
                if (text.Length > 0) Console.Write($"{rate.TicksPerSecond} Hz frame {frame} tick {tick + 1}/{rate.TicksPerOriginalFrame} object {id} {entity?.Template.Name}:\n{text}");
            }
        }
    }

    private readonly record struct Field(char Kind, long Number, string Text)
    {
        public override string ToString() => Kind switch
        {
            'f' => $"{Fixed.FromRaw(Number)} (raw {Number.ToString(CultureInfo.InvariantCulture)})",
            'i' => Number.ToString(CultureInfo.InvariantCulture),
            _ => Text,
        };
    }

    private static bool Same(Field x, Field y, int ratio)
    {
        if (x.Kind != y.Kind) return false;
        if (x.Kind == 't') return x.Text == y.Text;
        if (x.Number == y.Number) return true;
        // A tick: the faster rate's value lies in the same logic frame of the original (tick t of the slower rate is the ticks ratio * t … ratio * t + ratio - 1). A countdown in
        // ticks: it ends in the first tick of the frame the slower rate's ends in (ratio * d - (ratio - 1) … ratio * d). The two cannot be told apart here.
        if (x.Kind == 'i') return y.Number > (x.Number - 1) * ratio && y.Number < x.Number * ratio + ratio;
        // A value per tick: the faster rate's value times the ratio, up to the bits a division loses.
        return Math.Abs(y.Number * ratio - x.Number) <= 2 * ratio;
    }

    private static Dictionary<string, Field> Fields(Entity entity, long worldTick, bool frameEnd)
    {
        var writer = new FieldWriter();
        entity.Describe(writer);
        var fields = writer.Fields;
        if (!frameEnd) return fields;
        // What an observer of the tick just stepped reads: the position and the heading at the end of the frame the object has begun, a turret at the end of its frame's step.
        var (x, y, z) = entity.FrameEnd(worldTick - 1);
        fields[".x"] = new Field('f', x.Raw, string.Empty);
        fields[".y"] = new Field('f', y.Raw, string.Empty);
        fields[".z"] = new Field('f', z.Raw, string.Empty);
        fields[".angle"] = new Field('f', entity.FrameEndAngle(worldTick - 1).Raw, string.Empty);
        foreach (var key in fields.Keys.Where(key => key.EndsWith(".goalAngle", StringComparison.Ordinal) || key.EndsWith(".goalPitch", StringComparison.Ordinal)).ToArray())
            fields[key[..^10] + (key.EndsWith("Angle", StringComparison.Ordinal) ? ".angle" : ".pitch")] = fields[key];
        return fields;
    }

    private static string? Value(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>Collects a description as path → value. Elements of an array are addressed by the first text they contain when they have one, else by position.</summary>
    private sealed class FieldWriter : IStateWriter
    {
        public Dictionary<string, Field> Fields { get; } = new(StringComparer.Ordinal);
        private readonly List<string> _path = [];
        private readonly List<int> _counters = [];
        private readonly List<int> _labelAt = [];

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

        public void Bool(string name, bool value) => Add(name, new Field('t', 0, value ? "true" : "false"));
        public void Int(string name, long value) => Add(name, new Field('i', value, string.Empty));
        public void UInt(string name, ulong value) => Add(name, new Field('t', 0, value.ToString(CultureInfo.InvariantCulture)));
        public void Fixed(string name, Fixed value) => Add(name, new Field('f', value.Raw, string.Empty));

        public void Text(string name, string value)
        {
            if (_labelAt.Count > 0 && _labelAt[^1] is >= 0 and var index && _path[index].EndsWith(']') && !_path[index].Contains(' '))
                _path[index] = _path[index][..^1] + " " + value + "]";
            Add(name, new Field('t', 0, value));
        }

        private void Add(string name, Field value)
        {
            if (_counters.Count > 0 && _counters[^1] >= 0) name = $"{name}[{_counters[^1]++}]";
            var path = string.Join('.', _path.Where(part => part.Length > 0)) + "." + name;
            var unique = path;
            for (var copy = 2; !Fields.TryAdd(unique, value); copy++) unique = $"{path}#{copy}";
        }
    }
}
