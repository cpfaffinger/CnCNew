using System.Collections.Immutable;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.World;

namespace CnCNew.Game.Shell;

/// <summary>Per-player counters the score screen shows; a field stays 0 when the simulation never raised the matching event.</summary>
public sealed class PlayerTally
{
    public int UnitsBuilt { get; internal set; }
    public int UnitsLost { get; internal set; }
    public int UnitsDestroyed { get; internal set; }
    public int BuildingsBuilt { get; internal set; }
    public int BuildingsLost { get; internal set; }
    public long Resources { get; internal set; }
}

/// <summary>
/// Aggregates <see cref="SimEvent"/>s of a running <c>SkirmishMatchSession</c> into per-player score-screen counters.
/// The host feeds it by calling <see cref="Record"/> once per <c>TickResult</c> while the match runs (<c>LiveMatch</c>
/// itself does not expose step events, so this is opt-in on the host's side); if nothing was ever recorded,
/// <see cref="HasData"/> is false and the score screen renders <c>-</c> for every counter instead of a fabricated zero.
/// Only <see cref="SimEventKind"/> values that unambiguously carry an owning <see cref="PlayerId"/> are counted; there is
/// no event that distinguishes a destroyed building from a destroyed unit yet, so <c>BuildingsDestroyed</c> is not
/// tracked (documented in <c>docs/GAME-SHELL.md</c> as a request to the simulation).
/// </summary>
public sealed class MatchStatistics
{
    private readonly Dictionary<int, PlayerTally> _tallies = [];

    public bool HasData { get; private set; }

    public void Record(TickResult result)
    {
        if (result.Events is null) return;
        foreach (var e in result.Events)
        {
            if (e.Player.Value == 0) continue; // PlayerId.System or unset; not attributable to a scoreboard row.
            HasData = true;
            var tally = _tallies.TryGetValue(e.Player.Value, out var existing) ? existing : _tallies[e.Player.Value] = new PlayerTally();
            switch (e.Kind)
            {
                case SimEventKind.ProductionCompleted: tally.UnitsBuilt++; break;
                case SimEventKind.EntityDied: tally.UnitsLost++; break;
                case SimEventKind.EntityDestroyed: tally.UnitsDestroyed++; break;
                case SimEventKind.ConstructionCompleted: tally.BuildingsBuilt++; break;
                case SimEventKind.StructureSold: tally.BuildingsLost++; break;
                case SimEventKind.SupplyDeposited: tally.Resources += e.Value; break;
            }
        }
    }

    public PlayerTally? TryGet(PlayerId player) => _tallies.TryGetValue(player.Value, out var tally) ? tally : null;

    /// <summary>A copy that counts on independently (what a replay's checkpoint keeps of the counters so far).</summary>
    public MatchStatistics Clone()
    {
        var copy = new MatchStatistics { HasData = HasData };
        foreach (var (player, tally) in _tallies)
            copy._tallies[player] = new PlayerTally
            {
                UnitsBuilt = tally.UnitsBuilt, UnitsLost = tally.UnitsLost, UnitsDestroyed = tally.UnitsDestroyed,
                BuildingsBuilt = tally.BuildingsBuilt, BuildingsLost = tally.BuildingsLost, Resources = tally.Resources,
            };
        return copy;
    }
}

/// <summary>What the score screen needs about one player row.</summary>
public sealed record MatchResultRow(PlayerConfiguration Player, bool IsWinner, bool IsDefeated, PlayerTally? Tally);

/// <summary>A finished (or aborted) skirmish, prepared for the score screen: player rows in slot order plus optional per-player tallies.</summary>
public sealed record MatchResult(ImmutableArray<MatchResultRow> Rows, long EndTick)
{
    public static MatchResult From(SkirmishMatchSession session, MatchStatistics? statistics)
        => From(session.Players, session.Winner, session.World, session.EndTick, statistics);

    /// <summary>The result of a match of either session kind (<c>LiveMatch</c> serves both) from its players, winner, world and end tick.</summary>
    public static MatchResult From(IEnumerable<PlayerConfiguration> players, PlayerId? winner, SimulationWorld world, long endTick, MatchStatistics? statistics)
    {
        var rows = players.Select(player => new MatchResultRow(player, winner == player.Id,
            world.IsPlayerDefeated(player.Id), statistics?.HasData == true ? statistics.TryGet(player.Id) : null));
        return new MatchResult([..rows], endTick);
    }
}
