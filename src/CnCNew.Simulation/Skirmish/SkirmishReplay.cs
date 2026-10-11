using System.Collections.Immutable;
using CnCNew.Data.World;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;

namespace CnCNew.Simulation.Skirmish;

/// <summary>The first command the playback produced differently from the recording.</summary>
/// <param name="Tick">The tick the command belongs to.</param>
/// <param name="Player">Whose command it is (the system is player 0).</param>
/// <param name="Recorded">The recorded command, or null when the playback produced one the recording does not have.</param>
/// <param name="Played">The command of the playback, or null when the recording has one the playback did not produce.</param>
public sealed record ReplayDeviation(long Tick, PlayerId Player, SimCommand? Recorded, SimCommand? Played)
{
    public override string ToString()
        => $"tick {Tick}, player {Player.Value}: recorded {(Recorded is null ? "nothing" : Describe(Recorded))}, played {(Played is null ? "nothing" : Describe(Played))}";

    private static string Describe(SimCommand command) => $"{command.GetType().Name} #{command.Sequence}";
}

/// <param name="HumanCommands">Commands of human players that were fed into the playback.</param>
/// <param name="AuditedCommands">Commands of the system and of the computer players the playback produced itself and found equal in the recording.</param>
/// <param name="FirstDeviation">The first difference between what was recorded and what was played, or null when there is none.</param>
public sealed record SkirmishReplayResult(SkirmishMatchSession Session, long EndTick, ulong FinalWorldHash, ulong FinalSessionHash, int HumanCommands, int AuditedCommands, ReplayDeviation? FirstDeviation);

/// <summary>
/// Plays the replay of a skirmish as a match: the header's <see cref="MatchDescription"/> builds the session, the commands of the human players are fed in at their ticks, and
/// the computer players and the system run along and decide again. Their recorded commands are not fed in - they are the <b>audit trail</b>: what the playback produces is
/// compared with them command by command (by their encoded bytes), and the first difference is reported with tick and player. A playback without a difference is a proof that
/// the computer players are deterministic over the whole match; <see cref="ReplayRunner"/> stays the playback that needs no AI (it submits every recorded command to a bare world).
/// </summary>
public static class SkirmishReplay
{
    /// <summary>The description in the header of a skirmish replay; null for a replay that has none (a fixture run of the bare world).</summary>
    public static MatchDescription? DescriptionOf(ReplayHeader header)
        => header.MatchDescription.Length == 0 ? null : MatchDescription.Decode(Convert.FromBase64String(header.MatchDescription));

    /// <param name="stopAtFirstDeviation">Stop where the first difference shows (the default); otherwise the playback runs to the end of the recording regardless.</param>
    /// <param name="audit">Compare what system and computer players produce with the recording (the default). A recording that holds the humans' commands only - a relay's,
    /// which never sees a computer player - is played without the comparison.</param>
    public static SkirmishReplayResult Play(ReplayFile replay, TemplateCatalog catalog, string definitionFingerprint, TerrainMap terrain, ImmutableArray<MapObject> mapObjects, bool stopAtFirstDeviation = true,
        bool audit = true)
    {
        ArgumentNullException.ThrowIfNull(replay);
        var description = DescriptionOf(replay.Header) ?? throw new ReplayRejectedException("The replay has no match description in its header; it can only be run on a bare world (ReplayRunner).");
        ReplayRunner.CheckCompatibility(replay.Header, definitionFingerprint);
        SkirmishMatchSession session;
        try
        {
            session = SkirmishMatchSession.Start(description, catalog, terrain, mapObjects, replay.Header.CheckpointInterval, definitionFingerprint);
        }
        catch (ArgumentException exception)
        {
            throw new ReplayRejectedException($"The replay's match cannot be built here: {exception.Message}");
        }
        var humans = session.Players.Where(player => !player.IsComputer).Select(player => player.Id.Value).ToHashSet();
        var recorded = replay.Commands;
        var played = session.RecordedCommands;
        var end = replay.Header.EndTick;
        var index = 0;
        var playedIndex = 0;
        var fed = 0;
        var audited = 0;
        ReplayDeviation? deviation = null;
        var pendingRecorded = new List<SimCommand>();
        var pendingPlayed = new List<SimCommand>();

        // Compares what the session produced for one tick with what the recording holds for it, both in replay order (player, sequence).
        void Audit(long tick)
        {
            pendingPlayed.Clear();
            for (; playedIndex < played.Count; playedIndex++)
                if (!humans.Contains(played[playedIndex].Player.Value)) pendingPlayed.Add(played[playedIndex]);
            pendingPlayed.Sort(ByPlayerAndSequence);
            var count = Math.Max(pendingPlayed.Count, pendingRecorded.Count);
            for (var position = 0; position < count; position++)
            {
                var was = position < pendingRecorded.Count ? pendingRecorded[position] : null;
                var now = position < pendingPlayed.Count ? pendingPlayed[position] : null;
                if (was is not null && now is not null && ReplayFormat.EncodeCommand(was).AsSpan().SequenceEqual(ReplayFormat.EncodeCommand(now)))
                {
                    audited++;
                    continue;
                }
                deviation ??= new ReplayDeviation(tick, (was ?? now)!.Player, was, now);
                return;
            }
        }

        while (session.World.Tick < end && (deviation is null || !stopAtFirstDeviation))
        {
            var tick = session.World.Tick;
            pendingRecorded.Clear();
            while (index < recorded.Length && recorded[index].Tick <= tick)
            {
                var command = recorded[index++];
                if (humans.Contains(command.Player.Value))
                {
                    session.Submit(command);
                    fed++;
                }
                else
                {
                    pendingRecorded.Add(command);
                }
            }
            session.Step();
            if (audit) Audit(tick);
        }
        // What the recording still holds after its own end tick would be a recording that does not fit its header.
        if (audit && deviation is null && index < recorded.Length && recorded[index..].Any(command => !humans.Contains(command.Player.Value)))
        {
            var left = recorded[index..].First(command => !humans.Contains(command.Player.Value));
            deviation = new ReplayDeviation(left.Tick, left.Player, left, null);
        }
        return new SkirmishReplayResult(session, session.World.Tick, session.World.StateHash, session.StateHash(), fed, audited, deviation);
    }

    private static int ByPlayerAndSequence(SimCommand a, SimCommand b)
    {
        var byPlayer = a.Player.Value.CompareTo(b.Player.Value);
        return byPlayer != 0 ? byPlayer : a.Sequence.CompareTo(b.Sequence);
    }
}
