using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Game.Data;
using CnCNew.Game.Live;
using CnCNew.Game.Lockstep;
using CnCNew.Game.Shell;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.World;

namespace CnCNew.Game.Replays;

/// <param name="CheckpointSeconds">How far apart (in seconds of game time) the playback keeps the state of the match to seek to; a multiple of
/// <see cref="MatchRecorder.HashIntervalSeconds"/>, so that every checkpoint lies on a tick the recording has a hash for.</param>
public sealed record ReplayPlaybackOptions(int CheckpointSeconds = ReplayPlayback.DefaultCheckpointSeconds);

/// <summary>Where a seek ended: the tick, the session hash there and the hash the recorded match had there (null when the recording kept none at that tick).</summary>
public sealed record ReplaySeek(long Tick, ulong SessionHash, ulong? RecordedHash, long SteppedTicks, bool Restored)
{
    public bool HashMatches => RecordedHash is null || RecordedHash == SessionHash;
}

/// <summary>
/// The playback of a replay file in the client (docs/REPLAY-BROWSER.md): the match is built again from the description in the replay's header, the commands of the human players
/// are fed in at their ticks, and the computer players and the system decide again, as <see cref="SkirmishReplay.Play"/> does - but shown by a <see cref="LiveMatch"/> in
/// spectator mode, with pause and speed of its tick driver, the view of one player or all revealed, and seeking.
/// <para>
/// <b>Seeking</b> goes to a checkpoint: the state of the match the playback kept every <see cref="ReplayPlaybackOptions.CheckpointSeconds"/> of game time on the way (the
/// browser builds them on the first playback; tick 0 is the description itself). A seek restores the nearest checkpoint at or before the target - or runs on from where the
/// playback stands, if that is nearer - and steps forward to the target without drawing; then a fresh presentation is put in front of the session
/// (<see cref="Generation"/> counts them, the host rebuilds its scene). The snapshot is the truth: a restored session goes on exactly as the uninterrupted one, and every
/// tick for which the recording kept a session hash is checked against it (<see cref="HashesChecked"/>, <see cref="HashMismatches"/>).
/// </para>
/// </summary>
public sealed class ReplayPlayback
{
    public const int DefaultCheckpointSeconds = 30;

    private sealed record Checkpoint(long Tick, byte[]? PackedState, MatchStatistics Statistics, ulong Hash);

    private readonly GameData _data;
    private readonly MapData _map;
    private readonly TerrainMap _terrain;
    private readonly Vector2 _viewport;
    private readonly ImmutableArray<SimCommand> _humanCommands;
    private readonly SortedDictionary<long, Checkpoint> _checkpoints = [];
    private readonly MatchDescription _description;
    private readonly string _simulationId;
    private int _next;

    private ReplayPlayback(GameData data, MatchRecording recording, ReplayFile replay, MatchDescription description, MapData map, TerrainMap terrain, Vector2 viewport,
        ReplayPlaybackOptions options, string simulationId)
    {
        _data = data;
        Recording = recording;
        Replay = replay;
        _description = description;
        _map = map;
        _terrain = terrain;
        _viewport = viewport;
        _simulationId = simulationId;
        var time = data.Rate.Time;
        var hashInterval = Math.Max(1, recording.Info.HashInterval);
        var seconds = Math.Max(MatchRecorder.HashIntervalSeconds, options.CheckpointSeconds / MatchRecorder.HashIntervalSeconds * MatchRecorder.HashIntervalSeconds);
        CheckpointInterval = time.WholeSecondsToTicks(seconds);
        if (CheckpointInterval % hashInterval != 0) CheckpointInterval = (CheckpointInterval / hashInterval + 1) * hashInterval;
        Statistics = new MatchStatistics();
        var session = Start();
        Statistics.Begin(session.World);
        var humans = session.Match.Slots.Where(slot => slot.Description.Kind == MatchSlotKind.Human).Select(slot => slot.Player.Value).ToHashSet();
        _humanCommands = [.. replay.Commands.Where(command => humans.Contains(command.Player.Value))];
        _checkpoints[0] = new Checkpoint(0, null, Statistics.Clone(), session.StateHash());
        Match = Present(session);
        CheckHash(session);
    }

    public MatchRecording Recording { get; }
    public MatchRecordingInfo Info => Recording.Info;
    public ReplayFile Replay { get; }

    /// <summary>The tick the recording ends at: the playback is over there (or earlier, when the match ended by its rules).</summary>
    public long EndTick => Replay.Header.EndTick;

    /// <summary>Ticks between two checkpoints.</summary>
    public long CheckpointInterval { get; }

    /// <summary>The presentation of the playback as it stands; replaced by every seek.</summary>
    public LiveMatch Match { get; private set; }

    /// <summary>Counts the presentations: a host that shows <see cref="Match"/> builds its scene again when this changed.</summary>
    public int Generation { get; private set; }

    /// <summary>The score screen's counters of the playback so far (kept with every checkpoint, so they stay whole across a seek).</summary>
    public MatchStatistics Statistics { get; private set; }

    /// <summary>Ticks for which the recording kept a session hash and the playback compared its own.</summary>
    public int HashesChecked { get; private set; }

    public int HashMismatches { get; private set; }
    public long FirstMismatchTick { get; private set; } = -1;

    /// <summary>The ticks the playback holds a checkpoint for, in order.</summary>
    public IReadOnlyCollection<long> CheckpointTicks => _checkpoints.Keys;

    /// <summary>The memory the kept checkpoints take: their packed states.</summary>
    public long CheckpointBytes => _checkpoints.Values.Sum(checkpoint => (long)(checkpoint.PackedState?.Length ?? 0));

    public bool IsOver => Match.IsOver;

    // ------------------------------------------------------------------------------------------------------------ open

    /// <summary>
    /// Reads and checks a replay file and builds its match at tick 0. A file that cannot be played here is refused with its reason (<see cref="ReplayRefusedException"/>): the
    /// header against this game (<see cref="ReplayCompatibility.Judge"/>), then the replay inside it - which must agree with its header - and the map, whose terrain must be the
    /// one the match was played on.
    /// </summary>
    public static ReplayPlayback Open(GameData data, ReadOnlySpan<byte> file, Vector2 viewport, ReplayPlaybackOptions? options = null, ReplayMachine? machine = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        var prepared = Prepare(data, file, machine);
        try
        {
            return new ReplayPlayback(data, prepared.Recording, prepared.Replay, prepared.Description, prepared.Map, prepared.Terrain, viewport, options ?? new ReplayPlaybackOptions(),
                prepared.SimulationId);
        }
        catch (ArgumentException exception)
        {
            throw new ReplayRefusedException($"The replay's match cannot be built here: {exception.Message}", ReplayCompatibilityKind.OtherSimulation, exception);
        }
    }

    /// <summary>A replay file read and checked, with what its match is built from: the description, the map and its terrain, and the build this game states.</summary>
    internal sealed record Prepared(MatchRecording Recording, ReplayFile Replay, MatchDescription Description, MapData Map, TerrainMap Terrain, string SimulationId);

    /// <summary>
    /// Reads and checks a replay file as a playback does (and the analysis of a replay, <c>ReplayAnalysis</c>): the header against this game, then the replay inside it - which
    /// must agree with its header - and the map, whose terrain must be the one the match was played on. A file that cannot be played here is refused with its reason.
    /// </summary>
    internal static Prepared Prepare(GameData data, ReadOnlySpan<byte> file, ReplayMachine? machine)
    {
        ArgumentNullException.ThrowIfNull(data);
        var recording = MatchRecordingFormat.Read(file);
        machine ??= ReplayMachine.Of(data);
        var judged = ReplayCompatibility.Judge(recording.Info, machine);
        if (!judged.IsPlayable) throw new ReplayRefusedException(judged.Reason, judged.Kind);
        ReplayFile replay;
        try
        {
            replay = ReplayFormat.Read(recording.Replay);
        }
        catch (ReplayFormatException exception)
        {
            throw new ReplayRefusedException($"The replay is damaged: {exception.Message}", ReplayCompatibilityKind.Damaged, exception);
        }
        var info = recording.Info;
        var description = SkirmishReplay.DescriptionOf(replay.Header) ?? throw new ReplayRefusedException("The replay holds no match description; it is not a match this game recorded.");
        if (replay.Header.TickRate != info.TickRate || !string.Equals(replay.Header.DefinitionFingerprint, info.DefinitionFingerprint, StringComparison.OrdinalIgnoreCase)
            || description.SimulationId != info.SimulationId || description.MapPath != info.MapPath || replay.Header.EndTick != info.EndTick)
            throw new ReplayRefusedException("The replay is damaged: its header does not describe the match inside it.");
        try
        {
            ReplayRunner.CheckCompatibility(replay.Header, machine.DefinitionFingerprint);
        }
        catch (ReplayRejectedException exception)
        {
            throw new ReplayRefusedException(exception.Message, ReplayCompatibilityKind.OtherDefinitions, exception);
        }
        MapData map;
        using (var stream = data.Content.OpenRead(description.MapPath)) map = SageMapDecoder.Decode(stream);
        var terrain = TerrainMap.Build(map, description.MapPath);
        if (!string.Equals(terrain.Fingerprint, description.TerrainFingerprint, StringComparison.Ordinal))
            throw new ReplayRefusedException($"This game's '{description.MapPath}' is not the map the match was played on (its terrain differs).", ReplayCompatibilityKind.OtherMap);
        return new Prepared(recording, replay, description, map, terrain, machine.SimulationId);
    }

    private SkirmishMatchSession Start() => LiveMatch.StartPlayback(_data, _description, _terrain, _map, Replay.Header.CheckpointInterval, _simulationId);

    private LiveMatch Present(SkirmishMatchSession session)
    {
        Generation++;
        return LiveMatch.ForPlayback(_data, this, session, _map, _terrain, _viewport, Info.LocalPlayer);
    }

    // ------------------------------------------------------------------------------------------------------------ the steps

    /// <summary>Whether <paramref name="session"/> is at the end of the playback: the match ended by its rules, or the recording ends here.</summary>
    internal bool Ends(SkirmishMatchSession session) => session.IsOver || session.World.Tick >= EndTick;

    /// <summary>The recorded commands of the human players for the tick the session is about to step, submitted as they were (tick, player, sequence).</summary>
    internal void BeforeStep(SkirmishMatchSession session)
    {
        var tick = session.World.Tick;
        while (_next < _humanCommands.Length && _humanCommands[_next].Tick < tick) _next++;
        while (_next < _humanCommands.Length && _humanCommands[_next].Tick == tick) session.Submit(_humanCommands[_next++]);
    }

    /// <summary>After a step: the counters, the recorded hash of this tick if there is one, and a checkpoint on the interval.</summary>
    internal void AfterStep(SkirmishMatchSession session, TickResult result)
    {
        Statistics.Record(result, session.World);
        CheckHash(session);
        var tick = session.World.Tick;
        if (tick % CheckpointInterval == 0 && !_checkpoints.ContainsKey(tick))
            _checkpoints[tick] = new Checkpoint(tick, SessionTransfer.Pack(session.Save()), Statistics.Clone(), session.StateHash());
    }

    private void CheckHash(SkirmishMatchSession session)
    {
        if (Info.HashInterval <= 0 || session.World.Tick % Info.HashInterval != 0 || Info.HashAt(session.World.Tick) is not { } recorded) return;
        HashesChecked++;
        if (recorded == session.StateHash()) return;
        HashMismatches++;
        if (FirstMismatchTick < 0) FirstMismatchTick = session.World.Tick;
    }

    // ------------------------------------------------------------------------------------------------------------ seeking

    /// <summary>The checkpoint before the one the playback stands at or just passed (a second of grace, so that pressing twice goes back twice).</summary>
    public long PreviousCheckpoint()
    {
        var tick = Match.Tick;
        var grace = _data.Rate.Time.WholeSecondsToTicks(1);
        var candidates = AllCheckpointTicks().Where(at => at + grace < tick).ToArray();
        return candidates.Length == 0 ? 0 : candidates[^1];
    }

    /// <summary>The next checkpoint after the tick the playback stands at (the end of the recording when there is none).</summary>
    public long NextCheckpoint()
    {
        var tick = Match.Tick;
        foreach (var at in AllCheckpointTicks())
            if (at > tick) return at;
        return EndTick;
    }

    /// <summary>Every tick a checkpoint lies on, kept or still to be kept, up to the end of the recording.</summary>
    public IEnumerable<long> AllCheckpointTicks()
    {
        for (var tick = 0L; tick < EndTick; tick += CheckpointInterval) yield return tick;
    }

    /// <summary>
    /// Goes to <paramref name="target"/>: from the nearest kept checkpoint at or before it (or on from where the playback stands, when that is nearer), stepping forward without
    /// drawing; then <see cref="Match"/> is a fresh presentation of the session there. A target beyond the end of the playback stops at the end.
    /// </summary>
    public ReplaySeek SeekTo(long target)
    {
        target = Math.Clamp(target, 0, EndTick);
        var paused = Match.Paused;
        var speed = Match.Speed;
        var viewer = Match.FogViewer;
        var current = Match.Session;
        var best = _checkpoints.Keys.Where(tick => tick <= target).Max();
        SkirmishMatchSession session;
        var restored = false;
        if (current.World.Tick <= target && current.World.Tick >= best) session = current;
        else
        {
            var checkpoint = _checkpoints[best];
            session = checkpoint.PackedState is { } packed
                ? LiveMatch.RestorePlayback(_data, SessionTransfer.Unpack(packed, int.MaxValue), _terrain)
                : Start();
            if (session.StateHash() != checkpoint.Hash) throw new InvalidOperationException($"The checkpoint at tick {best} does not restore to the state it was taken from.");
            Statistics = checkpoint.Statistics.Clone();
            _next = 0;
            restored = true;
        }
        var from = session.World.Tick;
        while (session.World.Tick < target && !Ends(session))
        {
            BeforeStep(session);
            var result = session.Step();
            AfterStep(session, result);
        }
        Match = Present(session);
        Match.Paused = paused;
        Match.Speed = speed;
        Match.ShowWorldAs(viewer);
        var hash = session.StateHash();
        return new ReplaySeek(session.World.Tick, hash, Info.HashInterval > 0 && session.World.Tick % Info.HashInterval == 0 ? Info.HashAt(session.World.Tick) : null, session.World.Tick - from, restored);
    }

    // ------------------------------------------------------------------------------------------------------------ the end

    /// <summary>What the score screen shows at the end of the playback: the players, the winner and the counters of the playback.</summary>
    public MatchResult Result() => MatchResult.From(Match.Players, Match.Winner, Match.World, Match.Tick, Statistics);

    /// <summary>
    /// Whether the playback came to the result the recording holds: the same end tick, winner and defeated players, and - for a recording that began at tick 0 - the same
    /// counters of every player.
    /// </summary>
    public bool ResultMatchesRecorded(out string difference)
    {
        var result = Result();
        difference = string.Empty;
        if (Match.Tick != Info.EndTick) { difference = $"the playback ends at tick {Match.Tick}, the recording at tick {Info.EndTick}"; return false; }
        if ((Match.Winner?.Value ?? 0) != Info.Winner) { difference = $"the playback's winner is player {Match.Winner?.Value ?? 0}, the recording's player {Info.Winner}"; return false; }
        if (result.Rows.Length != Info.Result.Length) { difference = "the playback has other players than the recording"; return false; }
        for (var index = 0; index < result.Rows.Length; index++)
        {
            var played = result.Rows[index];
            var recorded = Info.Result[index];
            if (played.Player.Id.Value != recorded.Player || played.IsWinner != recorded.IsWinner || played.IsDefeated != recorded.IsDefeated)
            {
                difference = $"player {recorded.Player} won or lost otherwise in the playback";
                return false;
            }
            if (Info.StartTick != 0) continue;
            var tally = played.Tally;
            if ((tally?.UnitsBuilt ?? 0) != recorded.UnitsBuilt || (tally?.UnitsLost ?? 0) != recorded.UnitsLost || (tally?.UnitsDestroyed ?? 0) != recorded.UnitsDestroyed
                || (tally?.BuildingsBuilt ?? 0) != recorded.BuildingsBuilt || (tally?.BuildingsLost ?? 0) != recorded.BuildingsLost || (tally?.Resources ?? 0) != recorded.Resources)
            {
                difference = $"the counters of player {recorded.Player} differ from the recording";
                return false;
            }
        }
        return true;
    }

    /// <summary>The final session hash of the playback equals the one the recorded match ended with.</summary>
    public bool FinalHashMatches => IsOver && Match.Session.StateHash() == Info.FinalSessionHash;
}
