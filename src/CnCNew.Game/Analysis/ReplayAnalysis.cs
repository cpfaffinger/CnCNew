using CnCNew.Game.Data;
using CnCNew.Game.Live;
using CnCNew.Game.Replays;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Skirmish;

namespace CnCNew.Game.Analysis;

/// <summary>
/// The analysis of a running match: <see cref="MatchAnalyzer"/> is told every tick the match steps (<see cref="LiveGame.TickStepped"/>), from the tick it was attached at. It
/// reads the match and writes nothing into it. <see cref="Finish"/> is called once the match has ended (the score screen offers the result).
/// </summary>
public sealed class LiveMatchAnalysis
{
    private readonly LiveMatch _match;
    private readonly MatchAnalyzer _analyzer;
    private MatchAnalysis? _result;

    private LiveMatchAnalysis(LiveMatch match)
    {
        _match = match;
        var members = match.Remote?.Client.Room?.Members ?? [];
        var players = MatchAnalyzer.PlayersOf(match.Session, slot => members.FirstOrDefault(member => member.Slot == slot).Name ?? string.Empty);
        _analyzer = new MatchAnalyzer(match.World, players, MatchAnalyzer.ColorsOf(match.Players, match.Data.Graph));
        match.TickStepped += OnTick;
    }

    /// <summary>Starts the analysis of <paramref name="match"/>; null for a match that is not a skirmish of the simulation's own AI or that is itself a playback.</summary>
    public static LiveMatchAnalysis? Attach(LiveMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return MatchRecorder.CanRecord(match) ? new LiveMatchAnalysis(match) : null;
    }

    public long StartTick => _analyzer.StartTick;

    private void OnTick(CnCNew.Simulation.World.TickResult result)
    {
        if (_result is null) _analyzer.Observe(result, _match.World);
    }

    /// <summary>The analysis of the match up to now; called a second time it returns the same result. <paramref name="ending"/> is how the match ended (<see cref="MatchRecordingFormat.KindName"/>).</summary>
    public MatchAnalysis Finish(string ending)
    {
        if (_result is { } done) return done;
        _match.TickStepped -= OnTick;
        var commands = _match.Session.ToReplay().Commands;
        return _result = _analyzer.Finish(_match.World, commands, _match.Winner, _match.Session.Description.MapPath, _match.Data.Rules.Name, ending, "live");
    }
}

/// <summary>
/// The analysis of a replay file, played again without a picture (docs/MATCH-ANALYSIS.md): the file is checked as for a playback (<see cref="ReplayPlayback.Prepare"/>), the
/// match is built again from its description, the human players' commands are fed in at their ticks and the computer players decide again - as the playback does - and every
/// tick is told to a <see cref="MatchAnalyzer"/>. Every tick for which the recording kept a session hash is compared: an analysis whose playback left the recorded match is
/// refused (<see cref="ReplayRefusedException"/>), never handed out.
/// <para>
/// The work is done in pieces (<see cref="Advance"/>), so that a screen can show its progress between frames; <see cref="Run"/> does it in one go.
/// </para>
/// </summary>
public sealed class ReplayAnalysis
{
    private readonly ReplayPlayback.Prepared _prepared;
    private readonly SkirmishMatchSession _session;
    private readonly MatchAnalyzer _analyzer;
    private readonly SimCommand[] _humanCommands;
    private readonly string _source;
    private int _next;
    private MatchAnalysis? _result;

    private ReplayAnalysis(GameData data, ReplayPlayback.Prepared prepared, string source)
    {
        _prepared = prepared;
        _source = source;
        _session = LiveMatch.StartPlayback(data, prepared.Description, prepared.Terrain, prepared.Map, prepared.Replay.Header.CheckpointInterval, prepared.SimulationId);
        var humans = _session.Match.Slots.Where(slot => slot.Description.Kind == MatchSlotKind.Human).Select(slot => slot.Player.Value).ToHashSet();
        _humanCommands = [.. prepared.Replay.Commands.Where(command => humans.Contains(command.Player.Value))];
        _analyzer = new MatchAnalyzer(_session.World, prepared.Recording.Info.Players, MatchAnalyzer.ColorsOf(_session.Players, data.Graph));
        Rules = data.Rules.Name;
        Check();
    }

    private string Rules { get; }

    /// <summary>Reads and checks a replay file and builds its match at tick 0; a file that cannot be played here is refused with its reason.</summary>
    /// <param name="source">What the export names as the source of the analysis (the file's name, for example); no path of the machine should go into it.</param>
    public static ReplayAnalysis Open(GameData data, ReadOnlySpan<byte> file, string source = "replay", ReplayMachine? machine = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var prepared = ReplayPlayback.Prepare(data, file, machine);
        try
        {
            return new ReplayAnalysis(data, prepared, source);
        }
        catch (ArgumentException exception)
        {
            throw new ReplayRefusedException($"The replay's match cannot be built here: {exception.Message}", ReplayCompatibilityKind.OtherSimulation, exception);
        }
    }

    /// <summary>The whole analysis of a replay file in one go.</summary>
    public static MatchAnalysis Run(GameData data, ReadOnlySpan<byte> file, string source = "replay", ReplayMachine? machine = null)
    {
        var analysis = Open(data, file, source, machine);
        while (!analysis.Advance(int.MaxValue)) { }
        return analysis.Result!;
    }

    public MatchRecordingInfo Info => _prepared.Recording.Info;

    /// <summary>The tick the playback stands at.</summary>
    public long Tick => _session.World.Tick;

    /// <summary>The tick the recording ends at.</summary>
    public long EndTick => _prepared.Replay.Header.EndTick;

    /// <summary>0..1: how far the analysis has come.</summary>
    public double Progress => EndTick <= 0 ? 1 : Math.Clamp(Tick / (double)EndTick, 0, 1);

    public int HashesChecked { get; private set; }

    /// <summary>The analysis, once <see cref="Advance"/> returned true.</summary>
    public MatchAnalysis? Result => _result;

    private bool Ends => _session.IsOver || _session.World.Tick >= EndTick;

    /// <summary>Plays up to <paramref name="ticks"/> more ticks; true once the analysis is complete (<see cref="Result"/>).</summary>
    public bool Advance(int ticks)
    {
        if (_result is not null) return true;
        for (var count = 0; count < ticks && !Ends; count++)
        {
            var tick = _session.World.Tick;
            while (_next < _humanCommands.Length && _humanCommands[_next].Tick < tick) _next++;
            while (_next < _humanCommands.Length && _humanCommands[_next].Tick == tick) _session.Submit(_humanCommands[_next++]);
            var result = _session.Step();
            _analyzer.Observe(result, _session.World);
            Check();
        }
        if (!Ends) return false;
        _result = _analyzer.Finish(_session.World, _prepared.Replay.Commands, _session.Winner, _prepared.Description.MapPath, Rules,
            MatchRecordingFormat.KindName(Info.Ending.Kind), _source);
        return true;
    }

    private void Check()
    {
        var info = Info;
        var tick = _session.World.Tick;
        if (info.HashInterval <= 0 || tick % info.HashInterval != 0 || info.HashAt(tick) is not { } recorded) return;
        HashesChecked++;
        if (recorded != _session.StateHash())
            throw new ReplayRefusedException($"The match played again left the recorded match at tick {tick}; no analysis is given for it.", ReplayCompatibilityKind.Damaged);
    }
}
