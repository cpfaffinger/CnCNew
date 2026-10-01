using System.Collections.Immutable;
using System.Linq;
using CnCNew.Game.Live;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.World;

namespace CnCNew.Game.Hud;

/// <summary>One player's derived statistics at the end of a match. See <see cref="MatchStatsTracker"/> for how each field is derived.</summary>
public sealed record PlayerMatchStats(PlayerId Player, int UnitsBuilt, int StructuresBuilt, int UnitsLost, int UnitsKilled, Fixed MoneyEarned);

/// <summary>Produced once when <see cref="SkirmishMatchSession.IsOver"/> becomes true (<see cref="MatchController.MatchEnded"/>).</summary>
public sealed record MatchResult(PlayerId? Winner, long EndTick, ImmutableArray<PlayerMatchStats> Players);

/// <summary>
/// Per-player match statistics derived purely from <see cref="SimEvent"/>s observed through <see cref="LiveMatch.TickStepped"/>;
/// it never reads simulation state that is not already exposed through an event, so it cannot affect determinism. Derivations
/// (documented again in <c>docs/GAME-HUD.md</c>, since none of this is part of the simulation contract):
/// <list type="bullet">
/// <item>units built: a non-structure <see cref="SimEventKind.EntitySpawned"/> with a real player. The initial map population
/// spawns before the first <see cref="LiveMatch.Advance"/> call and never reaches <see cref="LiveMatch.TickStepped"/>, so only
/// units actually produced during the match are counted.</item>
/// <item>structures built: <see cref="SimEventKind.ConstructionCompleted"/> (already carries the owning player).</item>
/// <item>units lost: the owner of the died entity (<see cref="SimEventKind.EntityDied"/>), looked up in the world before any
/// later removal; unresolvable (already removed) deaths are not counted.</item>
/// <item>units killed: the owner of the killer entity (<see cref="SimEvent.Other"/>), when it differs from the victim's owner
/// and neither is the system player; friendly fire and suicides score nothing, matching <c>Object::scoreTheKill</c>.</item>
/// <item>money earned: <see cref="SimEventKind.SupplyDeposited"/> credited to the depositing entity's owner (the event itself
/// carries no player field, only the depositing entity).</item>
/// </list>
/// </summary>
public sealed class MatchStatsTracker
{
    private sealed class Row
    {
        public int UnitsBuilt;
        public int StructuresBuilt;
        public int UnitsLost;
        public int UnitsKilled;
        public Fixed MoneyEarned;
    }

    private readonly Dictionary<int, Row> _rows = [];

    private Row RowOf(PlayerId player) => _rows.TryGetValue(player.Value, out var row) ? row : _rows[player.Value] = new Row();

    /// <summary>Folds one tick's events in. Call once per <see cref="LiveMatch.TickStepped"/> with the same world the tick produced.</summary>
    public void Observe(TickResult result, SimulationWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        foreach (var ev in result.Events)
        {
            switch (ev.Kind)
            {
                case SimEventKind.EntitySpawned when !ev.Player.IsSystem:
                    if (!world.Templates.TryGet(ev.Text, out var spawned) || !spawned.IsKindOf("STRUCTURE")) RowOf(ev.Player).UnitsBuilt++;
                    break;
                case SimEventKind.ConstructionCompleted when !ev.Player.IsSystem:
                    RowOf(ev.Player).StructuresBuilt++;
                    break;
                case SimEventKind.EntityDied:
                {
                    var victim = world.FindEntity(ev.Entity);
                    if (victim is not null && !victim.Owner.IsSystem) RowOf(victim.Owner).UnitsLost++;
                    if (victim is not null && ev.Other.IsValid && world.FindEntity(ev.Other) is { } killer
                        && !killer.Owner.IsSystem && killer.Owner != victim.Owner)
                        RowOf(killer.Owner).UnitsKilled++;
                    break;
                }
                case SimEventKind.SupplyDeposited:
                    if (world.FindEntity(ev.Entity) is { } depositor && !depositor.Owner.IsSystem) RowOf(depositor.Owner).MoneyEarned += Fixed.FromInt(ev.Value);
                    break;
            }
        }
    }

    /// <summary>The result so far (only meaningful once <see cref="SkirmishMatchSession.IsOver"/>, but harmless to call earlier).</summary>
    public MatchResult Snapshot(SkirmishMatchSession session)
    {
        ArgumentNullException.ThrowIfNull(session);
        return Snapshot(session.Players, session.Winner, session.World.Tick);
    }

    /// <summary>The result of any game (skirmish or mission) from its players, winner and end tick.</summary>
    public MatchResult Snapshot(IEnumerable<PlayerConfiguration> configurations, PlayerId? winner, long tick)
    {
        ArgumentNullException.ThrowIfNull(configurations);
        var players = ImmutableArray.CreateBuilder<PlayerMatchStats>();
        foreach (var player in configurations.OrderBy(configuration => configuration.Id.Value))
        {
            var row = RowOf(player.Id);
            players.Add(new PlayerMatchStats(player.Id, row.UnitsBuilt, row.StructuresBuilt, row.UnitsLost, row.UnitsKilled, row.MoneyEarned));
        }
        return new MatchResult(winner, tick, players.ToImmutable());
    }
}
