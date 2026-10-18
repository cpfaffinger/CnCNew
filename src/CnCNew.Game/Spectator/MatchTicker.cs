using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Game.Analysis;
using CnCNew.Game.Replays;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.World;

namespace CnCNew.Game.Spectator;

/// <summary>
/// One line of the observer's event ticker: when (a frame of the original), what (<c>first-blood</c>, <c>structure-lost</c>, <c>superweapon</c>, <c>captured</c>,
/// <c>defeated</c>), whose (the player the line is about), the words, and the place on the map (-1 when the event has none).
/// </summary>
public sealed record TickerLine(long Frame, string Kind, int Player, string Text, int X, int Y)
{
    public bool HasPlace => X >= 0 && Y >= 0;
    public long Second => Frame / CnCNew.Data.OriginalFrame.PerSecond;

    /// <summary>The line as a check reads it: <c>m:ss kind player text @x,y</c>.</summary>
    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{Second / 60}:{Second % 60:00} {Kind} {Player} {Text}{(HasPlace ? $" @{X},{Y}" : string.Empty)}");
}

/// <summary>
/// The observer's event ticker (docs/SPECTATOR-MODE.md §14): the moments of a match that matter - the first blood, a building lost, a superweapon fired, a building
/// captured, a player defeated - as lines with their place. It is the core of the match analysis (<see cref="MatchAnalyzer"/>) read while the match runs: every tick
/// is told to it, and its events become lines.
/// <para>
/// It only moves forward: a tick it has seen already is not told again. A playback that seeks back and plays the same ticks again (<see cref="ReplayPlayback"/>) therefore
/// keeps the one ticker of the match, and the lines a viewer sees are those up to the frame the playback stands at (<see cref="UpTo"/>) - the same lines however the
/// viewer sought. The lines are a function of the match: the same match gives the same lines, at every rate at the same frames of the original.
/// </para>
/// </summary>
public sealed class MatchTicker
{
    private readonly MatchAnalyzer _analyzer;
    private readonly ImmutableArray<ReplayPlayer> _players;
    private readonly List<TickerLine> _lines = [];
    private readonly int _ticksPerFrame;
    private long _lastTick;
    private int _cursor;
    private bool _firstBlood;

    /// <param name="world">The world of the match at the tick the ticker begins.</param>
    /// <param name="players">The players as the replay header names them (their names, factions, slots).</param>
    public MatchTicker(SimulationWorld world, ImmutableArray<ReplayPlayer> players, IReadOnlyDictionary<int, int> colors)
    {
        ArgumentNullException.ThrowIfNull(world);
        _players = players;
        _analyzer = new MatchAnalyzer(world, players, colors);
        _ticksPerFrame = world.Rate.TicksPerOriginalFrame;
        _lastTick = world.Tick;
    }

    /// <summary>Every line so far, in the order of the match.</summary>
    public IReadOnlyList<TickerLine> Lines => _lines;

    /// <summary>The tick the ticker has seen up to.</summary>
    public long LastTick => _lastTick;

    /// <summary>The lines up to frame <paramref name="frame"/> (the frame the viewer's match stands at).</summary>
    public IEnumerable<TickerLine> UpTo(long frame) => _lines.TakeWhile(line => line.Frame <= frame);

    public long FrameOf(long tick) => tick / _ticksPerFrame;

    /// <summary>
    /// Tells the ticker a tick of the match: the result of the step and the world after it; <paramref name="recorded"/> is the session's recording of commands so far (the
    /// commands of this tick are at its end), which gives a superweapon fired at a place its place. A tick the ticker has seen already is ignored.
    /// </summary>
    public void Observe(TickResult result, SimulationWorld world, IReadOnlyList<SimCommand> recorded)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(recorded);
        if (world.Tick <= _lastTick) return;
        _lastTick = world.Tick;
        _analyzer.Observe(result, world);
        if (_analyzer.EventCount == _cursor) return;
        _analyzer.PlacePowers(result.Tick, CommandsOf(recorded, result.Tick));
        for (; _cursor < _analyzer.EventCount; _cursor++) Add(_analyzer.EventAt(_cursor));
    }

    private static IEnumerable<SimCommand> CommandsOf(IReadOnlyList<SimCommand> recorded, long tick)
    {
        var start = recorded.Count;
        while (start > 0 && recorded[start - 1].Tick >= tick) start--;
        for (var index = start; index < recorded.Count; index++)
            if (recorded[index].Tick == tick) yield return recorded[index];
    }

    private string Label(int player)
    {
        foreach (var setup in _players)
            if (setup.Player == player)
            {
                var who = setup.Name.Length > 0 ? setup.Name : $"Player {player}";
                return $"{who} ({AnalysisCharts.FactionShort(setup.Faction)})";
            }
        return $"Player {player}";
    }

    private void Add(AnalysisEvent item)
    {
        switch (item.Kind)
        {
            case "unit-lost" or "structure-lost":
                if (!_firstBlood && item.Other != 0)
                {
                    _firstBlood = true;
                    _lines.Add(new TickerLine(item.Frame, "first-blood", item.Other, $"First blood: {Label(item.Other)} killed {Label(item.Player)}'s {item.Name}", item.X, item.Y));
                }
                if (item.Kind == "structure-lost")
                    _lines.Add(new TickerLine(item.Frame, "structure-lost", item.Player, $"{Label(item.Player)} lost {item.Name}{(item.Other != 0 ? " to " + Label(item.Other) : string.Empty)}", item.X, item.Y));
                break;
            case "superweapon":
                _lines.Add(new TickerLine(item.Frame, "superweapon", item.Player, $"{Label(item.Player)} fired {item.Name}", item.X, item.Y));
                break;
            case "captured":
                _lines.Add(new TickerLine(item.Frame, "captured", item.Player, $"{Label(item.Player)} captured {item.Name}{(item.Other != 0 ? " from " + Label(item.Other) : string.Empty)}", item.X, item.Y));
                break;
            case "defeated":
                _lines.Add(new TickerLine(item.Frame, "defeated", item.Player, $"{Label(item.Player)} was defeated", -1, -1));
                break;
        }
    }
}
