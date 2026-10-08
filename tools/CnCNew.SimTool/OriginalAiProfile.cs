using System.Diagnostics;
using System.Globalization;
using CnCNew.Data.World;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.World;

namespace CnCNew.SimTool;

/// <summary>
/// <c>ai-match … --profile</c>: where the time of a scripted skirmish goes - by tick phase of the world, by section of the script engine, by condition and action type and by module type.
/// A measuring hook only (<see cref="ITickPhaseObserver"/>, <see cref="IScriptObserver"/>): nothing of it is state.
/// </summary>
internal sealed class OriginalAiProfile : ITickPhaseObserver, IScriptObserver
{
    private readonly long[] _phase = new long[64];
    private readonly long[] _phaseStart = new long[64];
    private readonly long[] _section = new long[32];
    private readonly long[] _sectionStart = new long[32];
    private readonly Dictionary<string, (long Ticks, long Count)> _conditions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (long Ticks, long Count)> _actions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (long Ticks, long Count)> _modules = new(StringComparer.Ordinal);
    private long _itemStart;
    private long _moduleStart;

    public void PhaseStarted(TickPhase phase) => _phaseStart[(int)phase] = Stopwatch.GetTimestamp();
    public void PhaseEnded(TickPhase phase) => _phase[(int)phase] += Stopwatch.GetTimestamp() - _phaseStart[(int)phase];
    public void UpdateStarted(SimModule module) => _moduleStart = Stopwatch.GetTimestamp();
    public void UpdateEnded(SimModule module) => Add(_modules, module.ModuleName, Stopwatch.GetTimestamp() - _moduleStart);
    public void SectionStarted(ScriptSection section) => _sectionStart[(int)section] = Stopwatch.GetTimestamp();
    public void SectionEnded(ScriptSection section) => _section[(int)section] += Stopwatch.GetTimestamp() - _sectionStart[(int)section];
    public void ConditionStarted(MapCondition condition) => _itemStart = Stopwatch.GetTimestamp();
    public void ConditionEnded(MapCondition condition) => Add(_conditions, condition.TypeName, Stopwatch.GetTimestamp() - _itemStart);
    public void ActionStarted(MapScriptAction action) => _itemStart = Stopwatch.GetTimestamp();
    public void ActionEnded(MapScriptAction action) => Add(_actions, action.TypeName, Stopwatch.GetTimestamp() - _itemStart);

    private static void Add(Dictionary<string, (long Ticks, long Count)> table, string key, long ticks)
    {
        var current = table.GetValueOrDefault(key);
        table[key] = (current.Ticks + ticks, current.Count + 1);
    }

    public void Print(long ticks)
    {
        static string Us(long stopwatchTicks, long per) => (stopwatchTicks * 1_000_000.0 / Stopwatch.Frequency / Math.Max(1, per)).ToString("F1", CultureInfo.InvariantCulture);
        Console.WriteLine("Profile (microseconds per tick):");
        Console.WriteLine("  world phases: " + string.Join(", ", Enum.GetValues<TickPhase>().Where(phase => _phase[(int)phase] > 0).OrderByDescending(phase => _phase[(int)phase]).Select(phase => $"{phase} {Us(_phase[(int)phase], ticks)}")));
        Console.WriteLine("  script sections: " + string.Join(", ", Enum.GetValues<ScriptSection>().Where(section => _section[(int)section] > 0).OrderByDescending(section => _section[(int)section]).Select(section => $"{section} {Us(_section[(int)section], ticks)}")));
        void Top(string title, Dictionary<string, (long Ticks, long Count)> table)
        {
            Console.WriteLine($"  {title} (us per tick, calls per tick):");
            foreach (var (name, value) in table.OrderByDescending(pair => pair.Value.Ticks).Take(12))
                Console.WriteLine($"    {name}: {Us(value.Ticks, ticks)}, {(value.Count / (double)Math.Max(1, ticks)).ToString("F1", CultureInfo.InvariantCulture)}");
        }
        Top("conditions", _conditions);
        Top("actions", _actions);
        Top("module updates", _modules);
    }
}
