using System.Diagnostics;
using System.Globalization;
using CnCNew.Data.World;
using CnCNew.Simulation.Scripting;

namespace CnCNew.SimTool;

/// <summary>
/// The script engine's parts for <c>perf-mission --script-detail</c>: per section of <see cref="ScriptEngine.Update"/> and per condition/action type the
/// number of calls (a deterministic counter: two builds that evaluate the same scripts print the same numbers), the time and the bytes allocated. Conditions
/// and actions nest; a type's own share ("self") excludes what ran inside it. The clock and the allocation counter are read here, never in the simulation.
/// </summary>
internal sealed class ScriptRecorder : IScriptObserver
{
    private sealed class Stat
    {
        public long Calls;
        public long Ticks;
        public long SelfTicks;
        public long Bytes;
        public long SelfBytes;
        public long MaxTicks;
    }

    private struct Frame
    {
        public long Start;
        public long StartBytes;
        public long ChildTicks;
        public long ChildBytes;
    }

    private static readonly ScriptSection[] Sections = Enum.GetValues<ScriptSection>();
    private readonly Dictionary<string, Stat> _conditions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Stat> _actions = new(StringComparer.Ordinal);
    private readonly Stat[] _sections = [..Sections.Select(_ => new Stat())];
    private Frame[] _stack = new Frame[64];
    private int _depth;
    private long _sectionStart;
    private long _sectionBytes;

    public void SectionStarted(ScriptSection section)
    {
        _sectionBytes = GC.GetAllocatedBytesForCurrentThread();
        _sectionStart = Stopwatch.GetTimestamp();
    }

    public void SectionEnded(ScriptSection section)
    {
        var elapsed = Stopwatch.GetTimestamp() - _sectionStart;
        var stat = _sections[(int)section];
        stat.Calls++;
        stat.Ticks += elapsed;
        stat.Bytes += GC.GetAllocatedBytesForCurrentThread() - _sectionBytes;
        if (elapsed > stat.MaxTicks) stat.MaxTicks = elapsed;
    }

    public void ConditionStarted(MapCondition condition) => Push();
    public void ConditionEnded(MapCondition condition) => Pop(_conditions, condition.TypeName);
    public void ActionStarted(MapScriptAction action) => Push();
    public void ActionEnded(MapScriptAction action) => Pop(_actions, action.TypeName);

    private void Push()
    {
        if (_depth == _stack.Length) Array.Resize(ref _stack, _depth * 2);
        _stack[_depth++] = new Frame { StartBytes = GC.GetAllocatedBytesForCurrentThread(), Start = Stopwatch.GetTimestamp() };
    }

    private void Pop(Dictionary<string, Stat> table, string type)
    {
        var now = Stopwatch.GetTimestamp();
        var bytesNow = GC.GetAllocatedBytesForCurrentThread();
        var frame = _stack[--_depth];
        var elapsed = now - frame.Start;
        var bytes = bytesNow - frame.StartBytes;
        if (!table.TryGetValue(type, out var stat)) table[type] = stat = new Stat();
        stat.Calls++;
        stat.Ticks += elapsed;
        stat.SelfTicks += elapsed - frame.ChildTicks;
        stat.Bytes += bytes;
        stat.SelfBytes += bytes - frame.ChildBytes;
        if (elapsed > stat.MaxTicks) stat.MaxTicks = elapsed;
        if (_depth > 0)
        {
            _stack[_depth - 1].ChildTicks += elapsed;
            _stack[_depth - 1].ChildBytes += bytes;
        }
    }

    public void Report(int steps, int top, SortedDictionary<string, string> summary)
    {
        static double Us(double ticks) => ticks * 1e6 / Stopwatch.Frequency;
        var invariant = CultureInfo.InvariantCulture;
        Console.WriteLine();
        Console.WriteLine($"Script engine by section over {steps} steps (--script-detail; the detail itself costs two clock reads and two allocation reads per condition and action):");
        Console.WriteLine($"  {"section",-14} {"us/tick",10} {"max us",10} {"bytes/tick",12}");
        foreach (var section in Sections)
        {
            var stat = _sections[(int)section];
            Console.WriteLine($"  {section,-14} {Us(stat.Ticks) / Math.Max(1, steps),10:F1} {Us(stat.MaxTicks),10:F1} {stat.Bytes / Math.Max(1, steps),12}");
            summary[$"script_{section}_us"] = (Us(stat.Ticks) / Math.Max(1, steps)).ToString("F1", invariant);
            summary[$"script_{section}_bytes"] = (stat.Bytes / Math.Max(1, steps)).ToString(invariant);
        }
        Table("Conditions", _conditions, steps, top, summary, "conditions");
        Table("Actions", _actions, steps, top, summary, "actions");
    }

    private static void Table(string title, Dictionary<string, Stat> table, int steps, int top, SortedDictionary<string, string> summary, string key)
    {
        static double Us(double ticks) => ticks * 1e6 / Stopwatch.Frequency;
        var rows = table.OrderByDescending(pair => pair.Value.SelfTicks).ThenBy(pair => pair.Key, StringComparer.Ordinal).ToList();
        long calls = 0, ticks = 0, bytes = 0;
        foreach (var (_, stat) in rows)
        {
            calls += stat.Calls;
            ticks += stat.SelfTicks;
            bytes += stat.SelfBytes;
        }
        Console.WriteLine();
        Console.WriteLine($"{title} by type, top {Math.Min(top, rows.Count)} of {rows.Count} by own time: {calls} calls = {(double)calls / Math.Max(1, steps):F1} per tick, {Us(ticks) / Math.Max(1, steps):F1} us and {bytes / Math.Max(1, steps)} bytes per tick (own share, nested calls excluded):");
        Console.WriteLine($"  {"type",-52} {"calls",10} {"calls/tick",10} {"self us/tick",12} {"mean us",9} {"max us",10} {"self B/tick",12} {"B/call",9}");
        foreach (var (name, stat) in rows.Take(top))
            Console.WriteLine($"  {name,-52} {stat.Calls,10} {(double)stat.Calls / Math.Max(1, steps),10:F1} {Us(stat.SelfTicks) / Math.Max(1, steps),12:F1} {Us(stat.SelfTicks) / stat.Calls,9:F2} {Us(stat.MaxTicks),10:F1} {stat.SelfBytes / Math.Max(1, steps),12} {stat.SelfBytes / stat.Calls,9}");
        summary[$"script_{key}_calls"] = calls.ToString(CultureInfo.InvariantCulture);
        summary[$"script_{key}_bytes_per_tick"] = (bytes / Math.Max(1, steps)).ToString(CultureInfo.InvariantCulture);
    }
}
