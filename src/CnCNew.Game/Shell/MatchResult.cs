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
    public int BuildingsDestroyed { get; internal set; }
    public long Resources { get; internal set; }
}

/// <summary>
/// Aggregates the <see cref="SimEvent"/>s of a running skirmish into per-player score-screen counters. The host feeds it by calling <see cref="Record"/> once per
/// <c>TickResult</c> with the world that produced it; if nothing was ever recorded, <see cref="HasData"/> is false and the score screen renders <c>-</c> for every counter
/// instead of a fabricated zero.
/// <para>
/// Most events name only their object (a death, a finished unit, a supply deposit carry no player): the player is the object's owner - or, for a death, the killer's owner -
/// read from the world, or remembered from when the object appeared if it is gone already (the same attribution as the match analysis, docs/MATCH-ANALYSIS.md). Units are
/// infantry, vehicles and aircraft including workers, buildings are structures (<c>SpectatorStats.Classify</c>); a kill counts only against somebody else's object. Not as
/// the original: its <c>ScoreKeeper</c> counts only objects of the kinds <c>SCORE</c>, <c>SCORE_CREATE</c> and the scoring building masks (fidelity register F-42).
/// </para>
/// </summary>
public sealed class MatchStatistics
{
    private readonly Dictionary<int, PlayerTally> _tallies = [];
    private readonly Dictionary<uint, (PlayerId Owner, CnCNew.Simulation.Templates.ObjectTemplate Template)> _known = [];
    private readonly HashSet<uint> _dead = [];

    private bool _begun;

    public bool HasData { get; private set; }

    /// <summary>Notes the objects of the world the counting begins with (an object that dies and is removed within one tick is gone when its death is read).</summary>
    public void Begin(SimulationWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _begun = true;
        foreach (var entity in world.Entities) _known[entity.Id.Value] = (entity.Owner, entity.Template);
    }

    private PlayerTally TallyOf(PlayerId player)
    {
        HasData = true;
        return _tallies.TryGetValue(player.Value, out var existing) ? existing : _tallies[player.Value] = new PlayerTally();
    }

    private (PlayerId Owner, CnCNew.Simulation.Templates.ObjectTemplate Template)? Identify(SimulationWorld world, EntityId id)
        => world.FindEntity(id) is { } entity ? (entity.Owner, entity.Template) : _known.TryGetValue(id.Value, out var known) ? known : null;

    public void Record(TickResult result, SimulationWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (!_begun) Begin(world);
        if (result.Events is null) return;
        foreach (var e in result.Events)
        {
            switch (e.Kind)
            {
                case SimEventKind.EntitySpawned:
                    if (world.FindEntity(e.Entity) is { } spawned) _known[e.Entity.Value] = (spawned.Owner, spawned.Template);
                    break;
                case SimEventKind.EntityDestroyed:
                    _known.Remove(e.Entity.Value);
                    break;
                case SimEventKind.ProductionCompleted:
                    if (Identify(world, e.Entity) is { Owner.IsSystem: false } factory) TallyOf(factory.Owner).UnitsBuilt++;
                    break;
                case SimEventKind.ConstructionCompleted:
                {
                    var owner = !e.Player.IsSystem ? e.Player : Identify(world, e.Entity)?.Owner ?? PlayerId.System;
                    if (!owner.IsSystem) TallyOf(owner).BuildingsBuilt++;
                    break;
                }
                case SimEventKind.SupplyDeposited:
                    if (Identify(world, e.Entity) is { Owner.IsSystem: false } center) TallyOf(center.Owner).Resources += e.Value;
                    break;
                case SimEventKind.EntityDied:
                {
                    if (!_dead.Add(e.Entity.Value) || Identify(world, e.Entity) is not { } victim || victim.Owner.IsSystem) break;
                    var kind = CnCNew.Game.Spectator.SpectatorStats.Classify(victim.Template);
                    if (kind == CnCNew.Game.Spectator.SpectatorUnitClass.None) break;
                    var structure = kind == CnCNew.Game.Spectator.SpectatorUnitClass.Structure;
                    if (structure) TallyOf(victim.Owner).BuildingsLost++; else TallyOf(victim.Owner).UnitsLost++;
                    if (e.Other.IsValid && Identify(world, e.Other) is { Owner.IsSystem: false } killer && killer.Owner != victim.Owner)
                    {
                        if (structure) TallyOf(killer.Owner).BuildingsDestroyed++; else TallyOf(killer.Owner).UnitsDestroyed++;
                    }
                    break;
                }
            }
        }
    }

    public PlayerTally? TryGet(PlayerId player) => _tallies.TryGetValue(player.Value, out var tally) ? tally : null;

    /// <summary>A copy that counts on independently (what a replay's checkpoint keeps of the counters so far).</summary>
    public MatchStatistics Clone()
    {
        var copy = new MatchStatistics { HasData = HasData, _begun = _begun };
        foreach (var (player, tally) in _tallies)
            copy._tallies[player] = new PlayerTally
            {
                UnitsBuilt = tally.UnitsBuilt, UnitsLost = tally.UnitsLost, UnitsDestroyed = tally.UnitsDestroyed,
                BuildingsBuilt = tally.BuildingsBuilt, BuildingsLost = tally.BuildingsLost, BuildingsDestroyed = tally.BuildingsDestroyed, Resources = tally.Resources,
            };
        foreach (var (id, known) in _known) copy._known[id] = known;
        copy._dead.UnionWith(_dead);
        return copy;
    }
}

/// <summary>What the score screen needs about one player row.</summary>
public sealed record MatchResultRow(PlayerConfiguration Player, bool IsWinner, bool IsDefeated, PlayerTally? Tally);

/// <summary>A finished (or aborted) skirmish, prepared for the score screen: player rows in slot order plus optional per-player tallies.</summary>
public sealed record MatchResult(ImmutableArray<MatchResultRow> Rows, long EndTick)
{
    /// <summary>The analysis of the match (docs/MATCH-ANALYSIS.md), when the host analysed it while it ran; the score screen then offers it.</summary>
    public CnCNew.Game.Analysis.MatchAnalysis? Analysis { get; init; }

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
