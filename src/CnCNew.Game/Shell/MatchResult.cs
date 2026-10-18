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
/// Most events of the simulation name only their object (a death, a finished unit, a supply deposit carry no player): the player is the object's owner - or, for a death, the killer's owner -
/// read from the world, or remembered from when the object appeared if it is gone already (the same attribution as the match analysis, docs/MATCH-ANALYSIS.md).
/// </para>
/// <para>
/// What counts is the original's <c>ScoreKeeper</c> (F-42, Phase 37): built - a unit of the kind <c>INFANTRY</c> or <c>VEHICLE</c> with <c>SCORE</c> or <c>SCORE_CREATE</c> when
/// its factory finishes it (<c>Player::onUnitCreated</c>), a <c>STRUCTURE</c> with <c>SCORE</c> or <c>SCORE_CREATE</c> when its construction completes
/// (<c>onStructureConstructionComplete</c>); lost and destroyed - only a death with a causer (<c>ActiveBody</c> calls <c>scoreTheKill</c> of the damage's source), of a
/// playable player's object that is not <c>IGNORED_IN_GUI</c> and not under construction, a <c>STRUCTURE</c> with <c>SCORE</c> or <c>SCORE_DESTROY</c> or an
/// <c>INFANTRY</c>/<c>VEHICLE</c> with <c>SCORE</c> or <c>SCORE_DESTROY</c>; destroyed counts for the causer's player only against an enemy (<c>addObjectDestroyed</c>). Resources
/// are the supply deposits only - the original adds crates, bounties, the cash hack, oil derricks and prisoners (<c>addMoneyEarned</c>), which the simulation raises no
/// event for. Not built: a structure built again by the GLA's rebuild hole is counted once more (the original skips <c>isRebuild</c>), and <c>removeObjectBuilt</c>.
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
                    if (Identify(world, e.Entity) is { Owner.IsSystem: false } factory && e.Value >= 0 && e.Value < world.Templates.Templates.Length
                        && ScoresWhenBuilt(world.Templates[(int)e.Value]) == ScoreKind.Unit)
                        TallyOf(factory.Owner).UnitsBuilt++;
                    break;
                case SimEventKind.ConstructionCompleted:
                {
                    if (Identify(world, e.Entity) is not { } building || ScoresWhenBuilt(building.Template) != ScoreKind.Building) break;
                    var owner = !e.Player.IsSystem ? e.Player : building.Owner;
                    if (!owner.IsSystem) TallyOf(owner).BuildingsBuilt++;
                    break;
                }
                case SimEventKind.SupplyDeposited:
                    if (Identify(world, e.Entity) is { Owner.IsSystem: false } center) TallyOf(center.Owner).Resources += e.Value;
                    break;
                case SimEventKind.EntityDied:
                {
                    if (!_dead.Add(e.Entity.Value) || Identify(world, e.Entity) is not { } victim || victim.Owner.IsSystem) break;
                    // Object::scoreTheKill is called for the source of the killing damage; a death without one (a sale, a timer) scores nothing.
                    if (!e.Other.IsValid || Identify(world, e.Other) is not { } killer) break;
                    if (victim.Template.IsKindOf("IGNORED_IN_GUI")) break;
                    if (world.FindEntity(e.Entity) is { } dying && dying.HasStatus(CnCNew.Simulation.Modules.ObjectStatusBits.UnderConstruction)) break;
                    var kind = ScoresWhenLost(victim.Template);
                    if (kind == ScoreKind.None) break;
                    if (kind == ScoreKind.Building) TallyOf(victim.Owner).BuildingsLost++; else TallyOf(victim.Owner).UnitsLost++;
                    if (killer.Owner.IsSystem || killer.Owner == victim.Owner || !world.AreEnemies(killer.Owner, victim.Owner)) break;
                    if (kind == ScoreKind.Building) TallyOf(killer.Owner).BuildingsDestroyed++; else TallyOf(killer.Owner).UnitsDestroyed++;
                    break;
                }
            }
        }
    }

    private enum ScoreKind { None, Unit, Building }

    /// <summary><c>ScoreKeeper::addObjectBuilt</c>: the scoring building masks (<c>STRUCTURE</c> with <c>SCORE</c> or <c>SCORE_CREATE</c>), else an infantry or vehicle with them.</summary>
    private static ScoreKind ScoresWhenBuilt(CnCNew.Simulation.Templates.ObjectTemplate template)
        => Scores(template, "SCORE_CREATE");

    /// <summary><c>ScoreKeeper::addObjectLost</c> and <c>addObjectDestroyed</c>: the same with <c>SCORE_DESTROY</c> for <c>SCORE_CREATE</c>.</summary>
    private static ScoreKind ScoresWhenLost(CnCNew.Simulation.Templates.ObjectTemplate template)
        => Scores(template, "SCORE_DESTROY");

    private static ScoreKind Scores(CnCNew.Simulation.Templates.ObjectTemplate template, string second)
    {
        var scores = template.IsKindOf("SCORE") || template.IsKindOf(second);
        if (!scores) return ScoreKind.None;
        if (template.IsKindOf("STRUCTURE")) return ScoreKind.Building;
        return template.IsKindOf("INFANTRY") || template.IsKindOf("VEHICLE") ? ScoreKind.Unit : ScoreKind.None;
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
