using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Game.Live;
using CnCNew.Game.Shell;
using CnCNew.Net;
using CnCNew.Net.Udp;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.World;

namespace CnCNew.Game.Replays;

/// <summary>
/// Records a skirmish as it is played (docs/REPLAY-BROWSER.md): beside the session's own recording of every command it keeps the session hash every
/// <see cref="HashIntervalSeconds"/> of game time and the score screen's counters, and when the match ends it writes the replay file - the simulation's replay of the
/// session with a header the list reads (<see cref="MatchRecordingFormat"/>). It reads the match and writes nothing into it. A match of the original AI is not recorded:
/// its truth is its state, and a replay of it could not be played (<see cref="CanRecord"/>).
/// </summary>
public sealed class MatchRecorder
{
    /// <summary>How often the session hash of the recorded match is kept, in seconds of game time: the ticks a playback is checked at.</summary>
    public const int HashIntervalSeconds = 5;

    private readonly LiveMatch _match;
    private readonly List<(long Tick, ulong Hash)> _hashes = [];
    private readonly long _hashInterval;
    private readonly string _source;

    private MatchRecorder(LiveMatch match)
    {
        _match = match;
        _hashInterval = match.World.Time.WholeSecondsToTicks(HashIntervalSeconds);
        StartTick = match.World.Tick;
        _source = match.Remote is null ? "local" : "network";
        Statistics = new MatchStatistics();
        Statistics.Begin(match.World);
        if (StartTick % _hashInterval == 0) _hashes.Add((StartTick, match.Session.StateHash()));
        match.TickStepped += OnTick;
    }

    /// <summary>Whether a match can be recorded: one of the simulation's own AI that is not itself a playback.</summary>
    public static bool CanRecord(LiveMatch match) => match.Scripted is null && match.Replay is null;

    /// <summary>Starts recording <paramref name="match"/>; null for a match that cannot be recorded (<see cref="CanRecord"/>).</summary>
    public static MatchRecorder? Attach(LiveMatch match)
    {
        ArgumentNullException.ThrowIfNull(match);
        return CanRecord(match) ? new MatchRecorder(match) : null;
    }

    /// <summary>The counters of the score screen, fed by every tick of the recorded match.</summary>
    public MatchStatistics Statistics { get; }

    /// <summary>The tick the recording began at.</summary>
    public long StartTick { get; }

    /// <summary>The name the file was written under, once <see cref="Finish"/> wrote it.</summary>
    public string? WrittenName { get; private set; }

    public bool Finished { get; private set; }

    private void OnTick(TickResult result)
    {
        if (Finished) return;
        Statistics.Record(result, _match.World);
        if (_match.World.Tick % _hashInterval == 0) _hashes.Add((_match.World.Tick, _match.Session.StateHash()));
    }

    /// <summary>How the match came to its end, from what this game and its server know: the rules, a desync, the server, the connection, a choice of the player.</summary>
    /// <param name="leftByChoice">This player left the running match (the in-game menu's Exit in a match of a server).</param>
    public static ReplayEnding EndingOf(LiveMatch match, bool leftByChoice)
    {
        ArgumentNullException.ThrowIfNull(match);
        if (match.Remote is not { } relay) return EndingOf(match.World.Tick, match.IsOver, leftByChoice, network: false, null, null, isOpen: true, UdpCloseReason.None);
        var client = relay.Client;
        return EndingOf(match.World.Tick, match.IsOver, leftByChoice, network: true, client.Desync, client.End, client.IsOpen, relay.CloseReason);
    }

    /// <summary>
    /// The same from the facts alone: a desync the server reported comes first (the replay is this game's own match up to there), then a choice of the player to leave, a
    /// match the server ended for another reason than its rules, the rules themselves (or the server's normal end), a connection that closed, and else a game closed while
    /// the match ran. A local match ends by its rules or is abandoned.
    /// </summary>
    public static ReplayEnding EndingOf(long tick, bool isOver, bool leftByChoice, bool network, DesyncMessage? desync, MatchEndMessage? end, bool isOpen, UdpCloseReason close)
    {
        if (!network) return new ReplayEnding(isOver ? ReplayEndingKind.Completed : ReplayEndingKind.Abandoned, tick);
        if (desync is not null) return new ReplayEnding(ReplayEndingKind.Desync, tick, $"The games were out of sync since tick {desync.Tick}.");
        if (leftByChoice && !isOver) return new ReplayEnding(ReplayEndingKind.Left, tick);
        if (end is { Reason: not EndReason.Finished }) return new ReplayEnding(ReplayEndingKind.ServerEnded, tick, NetText.End(end.Reason));
        if (isOver || end is not null) return new ReplayEnding(ReplayEndingKind.Completed, tick);
        if (!isOpen) return new ReplayEnding(ReplayEndingKind.ConnectionLost, tick, NetText.Close(close));
        return new ReplayEnding(ReplayEndingKind.Abandoned, tick);
    }

    /// <summary>The replay file of the match as it stands, ended by <paramref name="ending"/> and dated <paramref name="recorded"/>.</summary>
    public byte[] Build(ReplayEnding ending, DateTimeOffset recorded)
    {
        ArgumentNullException.ThrowIfNull(ending);
        var session = _match.Session;
        var replay = session.ToReplay();
        var description = session.Description;
        var local = _match.Setup.Spectator || _match.Setup.AutoPlay ? 0 : _match.Human.Value;
        var members = _match.Remote?.Client.Room?.Members ?? [];
        var players = ImmutableArray.CreateBuilder<ReplayPlayer>();
        foreach (var slot in session.Match.Slots)
        {
            var computer = slot.Description.Kind == MatchSlotKind.Computer;
            var name = members.FirstOrDefault(member => member.Slot == slot.Slot).Name ?? string.Empty;
            players.Add(new ReplayPlayer(slot.Player.Value, slot.Slot, slot.Participant.Faction.Name, computer ? string.Empty : name, slot.Description.Team, slot.Color, computer,
                computer ? AiName(slot.Description) : string.Empty));
        }
        var result = MatchResult.From(_match.Players, _match.Winner, _match.World, replay.Header.EndTick, Statistics);
        var rows = result.Rows.Select(row => new ReplayResultRow(row.Player.Id.Value, row.IsWinner, row.IsDefeated, row.Tally?.UnitsBuilt ?? 0, row.Tally?.UnitsLost ?? 0,
            row.Tally?.UnitsDestroyed ?? 0, row.Tally?.BuildingsBuilt ?? 0, row.Tally?.BuildingsLost ?? 0, row.Tally?.Resources ?? 0));
        var info = new MatchRecordingInfo(recorded.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture), _source, description.MapPath, description.TickRate,
            _match.Data.Rules.Name, description.DefinitionFingerprint, description.TerrainFingerprint, description.SimulationId, description.CommandTableVersion, description.WorldStateVersion,
            ReplayFormat.CurrentVersion, MatchDescription.EncodingVersion, local, players.ToImmutable(), ending with { Tick = replay.Header.EndTick }, _match.Winner?.Value ?? 0,
            replay.Header.EndTick, StartTick, _hashInterval, [.. _hashes], session.StateHash(), session.World.StateHash, [.. rows]);
        return MatchRecordingFormat.Write(info, ReplayFormat.Write(replay));
    }

    /// <summary>Ends the recording and writes the file into <paramref name="store"/> under a new name (<see cref="ReplayStore.NewName"/>); returns the name.</summary>
    public string Finish(ReplayStore store, ReplayEnding ending, DateTimeOffset recorded)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (Finished) throw new InvalidOperationException("The recording was finished already.");
        var bytes = Build(ending, recorded);
        Finished = true;
        _match.TickStepped -= OnTick;
        var name = store.NewName(recorded, _match.Session.Description.MapPath);
        store.Save(name, bytes);
        WrittenName = name;
        return name;
    }

    /// <summary>A computer slot's AI as the tools spell it: <c>builtin</c>, its tactics profile and its strength when they are not the defaults.</summary>
    public static string AiName(MatchSlotDescription slot)
    {
        var tactics = slot.AiTactics switch
        {
            (int)AiTactics.Balanced => "/balanced",
            (int)AiTactics.Rush => "/rush",
            (int)AiTactics.Turtle => "/turtle",
            (int)AiTactics.Eco => "/eco",
            _ => string.Empty,
        };
        var strength = slot.AiStrength switch { 0 => "/easy", 2 => "/hard", _ => string.Empty };
        return "builtin" + tactics + strength;
    }
}
