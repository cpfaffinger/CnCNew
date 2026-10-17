using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CnCNew.Game.Diagnostics;
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
/// <c>--slack N</c> lets fixed-point values up to N raw steps apart count as equal (to look behind a known rounding difference).
/// Fields are compared in game units: an integer or a fixed-point value of the faster rate counts as equal when it is the slower rate's value times the ratio of the rates (a tick,
/// a duration in ticks, a value per tick). Fields that exist only above 30 Hz (the bookkeeping of a frame's shares) are skipped unless <c>--strict</c> is given.
/// </para>
/// <para>The fields and their comparison are the shared ones of the diagnosis tools (<see cref="StateFields"/>, docs/MATCH-ANALYSIS.md section 5); this tool adds its rule of
/// equality between two rates.</para>
/// </summary>
internal static class RateCompareFields
{
    /// <summary>What the comparison steps: a match of the built-in AI or one of the original AI (<c>--original-diff</c>).</summary>
    public sealed record Match(SimulationWorld World, Func<bool> Over, Func<TickResult> Advance)
    {
        public bool IsOver => Over();
        public TickResult Step() => Advance();
    }

    public static int Run(string[] args, string pairing, RateCatalogs catalogs, SimulationRate rateA, SimulationRate rateB, int shown)
    {
        var factions = pairing.Split(',');
        Match Of(SimulationRate rate)
        {
            var session = RateCases.SkirmishAiSession(catalogs, rate, factions[0], factions[1]);
            return new Match(session.World, () => session.IsOver, session.Step);
        }
        return Run(args, Of(rateA), Of(rateB), rateA, rateB, shown);
    }

    public static int Run(string[] args, Match a, Match b, SimulationRate rateA, SimulationRate rateB, int shown)
    {
        var limit = long.Parse(Value(args, "--frames") ?? "14400", CultureInfo.InvariantCulture);
        var from = long.Parse(Value(args, "--from") ?? "0", CultureInfo.InvariantCulture);
        var strict = args.Contains("--strict");
        var atFirst = Value(args, "--at") == "first";
        // A module's wake tick is bookkeeping of the scheduler (a module that wakes every tick and one that sleeps until it is called differ without any difference in the game).
        var ignore = new Regex(Value(args, "--ignore") ?? @"\.nextWake$|\.previousHealth$", RegexOptions.CultureInvariant);
        var trace = (Value(args, "--trace") ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries).Select(text => uint.Parse(text, CultureInfo.InvariantCulture)).ToArray();
        var traceFrom = long.Parse(Value(args, "--trace-from") ?? "9223372036854775807", CultureInfo.InvariantCulture);
        var traceTo = long.Parse(Value(args, "--trace-to") ?? "9223372036854775807", CultureInfo.InvariantCulture);
        var ratio = rateB.TicksPerOriginalFrame / rateA.TicksPerOriginalFrame;
        var last = new Dictionary<(int, uint), Dictionary<string, StateField>>();
        // A difference counts once it has lasted this many frames: a state that one rate reaches a share of a frame earlier and the other catches up with is no divergence.
        var persist = int.Parse(Value(args, "--persist") ?? "3", CultureInfo.InvariantCulture);
        // --slack N: fixed-point values up to N raw steps apart count as equal (a diagnosis of what parts beyond a known rounding difference; the default 0 is the measurement).
        var slack = long.Parse(Value(args, "--slack") ?? "0", CultureInfo.InvariantCulture);
        var streaks = new Dictionary<string, (long Since, int Count)>(StringComparer.Ordinal);
        var randomParted = false;
        // --random-trace F: who draws from the logic random stream in frame F and the --random-frames (2) after it, at both rates (a phase of the tick, or a module's update).
        var randomFrom = long.Parse(Value(args, "--random-trace") ?? "-1", CultureInfo.InvariantCulture);
        var randomSpan = long.Parse(Value(args, "--random-frames") ?? "2", CultureInfo.InvariantCulture);
        var drawsA = new DrawObserver(a.World, rateA);
        var drawsB = new DrawObserver(b.World, rateB);
        if (randomFrom >= 0)
        {
            a.World.PhaseObserver = drawsA;
            b.World.PhaseObserver = drawsB;
        }
        for (long frame = 0; frame < limit && !a.IsOver && !b.IsOver; frame++)
        {
            var tracing = trace.Length > 0 && frame >= traceFrom && frame <= traceTo;
            drawsA.Frame = drawsB.Frame = frame;
            drawsA.Active = drawsB.Active = randomFrom >= 0 && frame >= randomFrom && frame <= randomFrom + randomSpan;
            if (randomFrom >= 0 && frame > randomFrom + randomSpan) return 0;
            // --at first: the worlds are compared after the first tick of every frame, where every decision of the frame has fallen, with positions and angles read where
            // the frame ends - what an observer of that tick reads. Without it they are compared after the last tick of the frame.
            StepFrame(a, rateA, frame, tracing, trace, last, 0, 0, atFirst ? 1 : rateA.TicksPerOriginalFrame, drawsA.Active);
            StepFrame(b, rateB, frame, tracing, trace, last, 1, 0, atFirst ? 1 : rateB.TicksPerOriginalFrame, drawsB.Active);
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
            if (!randomParted)
            {
                // The number and order of draws from the logic stream per game time must be the same at every rate: the first frame after which the streams differ.
                Span<uint> left = stackalloc uint[6];
                Span<uint> right = stackalloc uint[6];
                a.World.LogicRandom.CopyState(left);
                b.World.LogicRandom.CopyState(right);
                if (!left.SequenceEqual(right))
                {
                    randomParted = true;
                    Console.WriteLine($"frame {frame}: the logic random streams differ from here on.");
                }
            }
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
                // A construction site is half a frame of work ahead above 30 Hz by design (the first work step counts a whole frame, so the building is complete in the tick of the 30 Hz step).
                var rules = new StateFieldRules(
                    Ignore: path => ignore.IsMatch(path) || (building && (path.EndsWith(".health", StringComparison.Ordinal) || path.EndsWith(".previousHealth", StringComparison.Ordinal) || path == ".constructionWork")),
                    Equal: (x, y) => Same(x, y, ratio) || (slack > 0 && x.Kind == StateFieldKind.Fixed && y.Kind == StateFieldKind.Fixed && Math.Abs(x.Number - y.Number) <= slack),
                    LeftOnly: true, RightOnly: strict);
                foreach (var difference in StateFields.Compare(left, right, rules))
                    found[$"{entity.Id.Value} {entity.Template.Name} owner {entity.Owner.Value} {difference.Path}"] =
                        $"{rateA.TicksPerSecond} Hz {(difference.Left is { } x ? x.ToString() : "<absent>")} | {rateB.TicksPerSecond} Hz {(difference.Right is { } y ? y.ToString() : "<absent>")}";
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

    private static void StepFrame(Match session, SimulationRate rate, long frame, bool tracing, uint[] trace, Dictionary<(int, uint), Dictionary<string, StateField>> last, int side, int fromTick, int toTick, bool events = false)
    {
        for (var tick = fromTick; tick < toTick; tick++)
        {
            var result = session.Step();
            if (events)
                foreach (var item in result.Events)
                    if (item.Kind != CnCNew.Simulation.Events.SimEventKind.FxRequested)
                        Console.WriteLine($"{rate.TicksPerSecond} Hz frame {frame} tick {tick + 1}/{rate.TicksPerOriginalFrame}: {item.Kind} {item.Entity.Value} {item.Other.Value} {item.Text} {item.Value}");
            if (!tracing) continue;
            foreach (var id in trace)
            {
                var entity = session.World.FindEntity(new CnCNew.Simulation.Ids.EntityId(id));
                var now = entity is null ? [] : Fields(entity, 0, false);
                var had = last.TryGetValue((side, id), out var before);
                if (!had)
                {
                    // Where the object's own frames lie in the frames of the world: the tick it appeared in.
                    if (entity is not null) Console.WriteLine($"{rate.TicksPerSecond} Hz object {id} {entity.Template.Name}: appeared in tick {entity.SpawnTick.ToString(CultureInfo.InvariantCulture)} (tick {(entity.SpawnTick % rate.TicksPerOriginalFrame + 1).ToString(CultureInfo.InvariantCulture)}/{rate.TicksPerOriginalFrame} of frame {(entity.SpawnTick / rate.TicksPerOriginalFrame).ToString(CultureInfo.InvariantCulture)})");
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

    /// <summary>Prints who changes the state of the logic random stream: a tool's observer of the tick phases, it changes nothing.</summary>
    private sealed class DrawObserver(SimulationWorld world, SimulationRate rate) : ITickPhaseObserver
    {
        private readonly uint[] _before = new uint[6];
        private readonly uint[] _now = new uint[6];
        public long Frame;
        public bool Active;

        private void Mark() => world.LogicRandom.CopyState(_before);

        private void Report(string who)
        {
            world.LogicRandom.CopyState(_now);
            if (Active && !_now.AsSpan().SequenceEqual(_before)) Console.WriteLine($"{rate.TicksPerSecond} Hz frame {Frame} tick {world.Tick % rate.TicksPerOriginalFrame + 1}/{rate.TicksPerOriginalFrame}: {who} draws");
            Mark();
        }

        public void PhaseStarted(TickPhase phase) => Report($"something before phase {phase}");
        public void PhaseEnded(TickPhase phase) => Report($"phase {phase}");
        public void UpdateStarted(CnCNew.Simulation.Modules.SimModule module) => Report("phase Updates (between two modules)");
        public void UpdateEnded(CnCNew.Simulation.Modules.SimModule module) => Report($"{module.ModuleName} of {module.Entity.Id.Value} {module.Entity.Template.Name}");
    }

    private static bool Same(StateField x, StateField y, int ratio)
    {
        if (x.Kind != y.Kind) return false;
        if (x.Kind == StateFieldKind.Text) return x.Text == y.Text;
        if (x.Number == y.Number) return true;
        // A tick: the faster rate's value lies in the same logic frame of the original (tick t of the slower rate is the ticks ratio * t … ratio * t + ratio - 1). A countdown in
        // ticks: it ends in the first tick of the frame the slower rate's ends in (ratio * d - (ratio - 1) … ratio * d). The two cannot be told apart here.
        if (x.Kind == StateFieldKind.Integer) return y.Number > (x.Number - 1) * ratio && y.Number < x.Number * ratio + ratio;
        // A value per tick: the faster rate's value times the ratio, up to the bits a division loses.
        return Math.Abs(y.Number * ratio - x.Number) <= 2 * ratio;
    }

    private static Dictionary<string, StateField> Fields(Entity entity, long worldTick, bool frameEnd)
    {
        var fields = StateFields.Of(entity.Describe);
        if (!frameEnd) return fields;
        // What an observer of the tick just stepped reads: the position and the heading at the end of the frame the object has begun, a turret at the end of its frame's step.
        var (x, y, z) = entity.FrameEnd(worldTick - 1);
        fields[".x"] = StateField.FixedRaw(x.Raw);
        fields[".y"] = StateField.FixedRaw(y.Raw);
        fields[".z"] = StateField.FixedRaw(z.Raw);
        fields[".angle"] = StateField.FixedRaw(entity.FrameEndAngle(worldTick - 1).Raw);
        foreach (var key in fields.Keys.Where(key => key.EndsWith(".goalAngle", StringComparison.Ordinal) || key.EndsWith(".goalPitch", StringComparison.Ordinal)).ToArray())
            fields[key[..^10] + (key.EndsWith("Angle", StringComparison.Ordinal) ? ".angle" : ".pitch")] = fields[key];
        return fields;
    }

    private static string? Value(string[] args, string name)
    {
        var index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }
}
