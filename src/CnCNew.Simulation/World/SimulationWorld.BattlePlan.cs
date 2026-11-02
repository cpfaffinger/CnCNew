using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;

namespace CnCNew.Simulation.World;

/// <summary>
/// The player-side half of the strategy center's battle plans (<c>Player.cpp</c>: <c>changeBattlePlan</c>, <c>applyBattlePlanBonusesForPlayerObjects</c>, <c>applyBattlePlanBonusesForObject</c>,
/// <c>removeBattlePlanBonusesForObject</c>, <c>doesObjectQualifyForBattlePlan</c>). Port notes: the reference stores a hold-the-line damage scalar <em>on every body</em> by multiplying it in and
/// out; with fixed point that would drift from 1 with every plan change, so the scalars are derived instead — an object qualifying for the player's hold-the-line plan is scaled by that plan's armor
/// scalar, one qualifying for search and destroy sees its shroud scaled by that plan's sight scalar — with the same result whenever the reference's bookkeeping is consistent. The weapon-bonus
/// switches are real per-object bits, set and cleared exactly like <c>localApplyBattlePlanBonusesToObject</c> (including that applying one plan's bonus clears the other two plans' bits on the
/// objects it reaches). Projectiles are skipped: they inherit their launcher's bonus conditions when launched.
/// </summary>
public sealed partial class SimulationWorld
{
    private static readonly int BombardmentBonusBit = NameTablesPhase5.WeaponBonusCondition.TryIndexOf("BATTLEPLAN_BOMBARDMENT", out var bombardment) ? bombardment : throw new InvalidOperationException("BATTLEPLAN_BOMBARDMENT");
    private static readonly int HoldTheLineBonusBit = NameTablesPhase5.WeaponBonusCondition.TryIndexOf("BATTLEPLAN_HOLDTHELINE", out var hold) ? hold : throw new InvalidOperationException("BATTLEPLAN_HOLDTHELINE");
    private static readonly int SearchAndDestroyBonusBit = NameTablesPhase5.WeaponBonusCondition.TryIndexOf("BATTLEPLAN_SEARCHANDDESTROY", out var search) ? search : throw new InvalidOperationException("BATTLEPLAN_SEARCHANDDESTROY");

    /// <summary>One player's battle plan bookkeeping (<c>m_bombardBattlePlans</c> and friends, <c>m_battlePlanBonuses</c>): centers per plan, the standing scalars and the aggregated switches for new objects.</summary>
    private sealed class BattlePlanState
    {
        public int Bombardment { get; set; }
        public int HoldTheLine { get; set; }
        public int SearchAndDestroy { get; set; }
        public Fixed HoldTheLineArmor { get; set; } = Fixed.One;
        public Fixed SearchAndDestroySight { get; set; } = Fixed.One;
        public bool HasAggregate { get; set; }
        public int AggregateBombardment { get; set; }
        public int AggregateHoldTheLine { get; set; }
        public int AggregateSearchAndDestroy { get; set; }
        public ImmutableArray<string> ValidKindOf { get; set; } = [];
        public ImmutableArray<string> InvalidKindOf { get; set; } = [];

        public void Describe(IStateWriter writer)
        {
            writer.BeginObject("battlePlan");
            writer.Int("bombardment", Bombardment);
            writer.Int("holdTheLine", HoldTheLine);
            writer.Int("searchAndDestroy", SearchAndDestroy);
            writer.Fixed("holdTheLineArmor", HoldTheLineArmor);
            writer.Fixed("searchAndDestroySight", SearchAndDestroySight);
            writer.Bool("hasAggregate", HasAggregate);
            writer.Int("aggregateBombardment", AggregateBombardment);
            writer.Int("aggregateHoldTheLine", AggregateHoldTheLine);
            writer.Int("aggregateSearchAndDestroy", AggregateSearchAndDestroy);
            writer.Text("validKindOf", string.Join(' ', ValidKindOf));
            writer.Text("invalidKindOf", string.Join(' ', InvalidKindOf));
            writer.EndObject();
        }

        public static BattlePlanState Load(IStateReader reader) => new()
        {
            Bombardment = checked((int)reader.Int("bombardment")),
            HoldTheLine = checked((int)reader.Int("holdTheLine")),
            SearchAndDestroy = checked((int)reader.Int("searchAndDestroy")),
            HoldTheLineArmor = reader.Fixed("holdTheLineArmor"),
            SearchAndDestroySight = reader.Fixed("searchAndDestroySight"),
            HasAggregate = reader.Bool("hasAggregate"),
            AggregateBombardment = checked((int)reader.Int("aggregateBombardment")),
            AggregateHoldTheLine = checked((int)reader.Int("aggregateHoldTheLine")),
            AggregateSearchAndDestroy = checked((int)reader.Int("aggregateSearchAndDestroy")),
            ValidKindOf = [..reader.Text("validKindOf").Split(' ', StringSplitOptions.RemoveEmptyEntries)],
            InvalidKindOf = [..reader.Text("invalidKindOf").Split(' ', StringSplitOptions.RemoveEmptyEntries)],
        };
    }

    /// <inheritdoc cref="IWorldServices.ChangeBattlePlan"/>
    public void ChangeBattlePlan(PlayerId player, BattlePlanKind plan, int delta, BattlePlanBonus bonus)
    {
        if (!_players.TryGetValue(player.Value, out var owner)) return;
        var state = owner.BattlePlan ??= new BattlePlanState();
        var add = false;
        var remove = false;
        switch (plan)
        {
            case BattlePlanKind.Bombardment:
                state.Bombardment += delta;
                add = state.Bombardment == 1 && delta == 1;
                remove = state.Bombardment == 0 && delta == -1;
                break;
            case BattlePlanKind.HoldTheLine:
                state.HoldTheLine += delta;
                add = state.HoldTheLine == 1 && delta == 1;
                remove = state.HoldTheLine == 0 && delta == -1;
                if (add) state.HoldTheLineArmor = bonus.ArmorScalar;
                break;
            case BattlePlanKind.SearchAndDestroy:
                state.SearchAndDestroy += delta;
                add = state.SearchAndDestroy == 1 && delta == 1;
                remove = state.SearchAndDestroy == 0 && delta == -1;
                if (add) state.SearchAndDestroySight = bonus.SightRangeScalar;
                break;
        }
        if (add) ApplyBattlePlanForPlayerObjects(owner, state, bonus);
        else if (remove)
        {
            // Inverse the bonuses first (the scalars are derived here, only the switches matter for the objects).
            var inverted = bonus with
            {
                ArmorScalar = Fixed.One / Fixed.Max(bonus.ArmorScalar, Fixed.FromDecimal(0.01m)),
                SightRangeScalar = Fixed.One / Fixed.Max(bonus.SightRangeScalar, Fixed.FromDecimal(0.01m)),
                Bombardment = bonus.Bombardment > 0 ? -1 : bonus.Bombardment,
                HoldTheLine = bonus.HoldTheLine > 0 ? -1 : bonus.HoldTheLine,
                SearchAndDestroy = bonus.SearchAndDestroy > 0 ? -1 : bonus.SearchAndDestroy,
            };
            ApplyBattlePlanForPlayerObjects(owner, state, inverted);
        }
    }

    /// <inheritdoc cref="IWorldServices.BattlePlansActive"/>
    public int BattlePlansActive(PlayerId player, BattlePlanKind plan)
        => _players.TryGetValue(player.Value, out var owner) && owner.BattlePlan is { } state
            ? plan switch { BattlePlanKind.Bombardment => state.Bombardment, BattlePlanKind.HoldTheLine => state.HoldTheLine, BattlePlanKind.SearchAndDestroy => state.SearchAndDestroy, _ => 0 }
            : 0;

    /// <summary><c>Player::applyBattlePlanBonusesForPlayerObjects</c>: fold the bonus into the aggregate new objects get, then hand it to every object the player owns.</summary>
    private void ApplyBattlePlanForPlayerObjects(PlayerState owner, BattlePlanState state, BattlePlanBonus bonus)
    {
        if (!state.HasAggregate)
        {
            state.HasAggregate = true;
            state.AggregateBombardment = bonus.Bombardment;
            state.AggregateHoldTheLine = bonus.HoldTheLine;
            state.AggregateSearchAndDestroy = bonus.SearchAndDestroy;
            state.ValidKindOf = bonus.ValidKindOf;
            state.InvalidKindOf = bonus.InvalidKindOf;
        }
        else
        {
            state.AggregateBombardment = Math.Max(0, state.AggregateBombardment + bonus.Bombardment);
            state.AggregateHoldTheLine = Math.Max(0, state.AggregateHoldTheLine + bonus.HoldTheLine);
            state.AggregateSearchAndDestroy = Math.Max(0, state.AggregateSearchAndDestroy + bonus.SearchAndDestroy);
        }
        // GameFix.BattlePlanAggregate: the objects get the switches of every plan the player still runs (the aggregate, with the kinds it was made with); the reference hands
        // them only the incoming bonus, whose zero switches clear the other plans' bits.
        var aggregate = Templates.Rules.Has(Rules.GameFix.BattlePlanAggregate);
        foreach (var entity in _entities.Values)
            if (entity.Owner == owner.Id)
            {
                if (aggregate) ApplyBattlePlanBonusToEntity(entity, state.AggregateBombardment, state.AggregateHoldTheLine, state.AggregateSearchAndDestroy, state.ValidKindOf, state.InvalidKindOf);
                else ApplyBattlePlanBonusToEntity(entity, bonus.Bombardment, bonus.HoldTheLine, bonus.SearchAndDestroy, bonus.ValidKindOf, bonus.InvalidKindOf);
            }
    }

    /// <summary><c>localApplyBattlePlanBonusesToObject</c>: a qualifying object gets each <c>BATTLEPLAN_*</c> weapon bonus set while its switch is positive and cleared otherwise.</summary>
    private void ApplyBattlePlanBonusToEntity(Entity entity, int bombardment, int holdTheLine, int searchAndDestroy, ImmutableArray<string> valid, ImmutableArray<string> invalid)
    {
        if (entity.Template.IsKindOf("PROJECTILE") || !valid.Any(entity.Template.IsKindOf) || invalid.Any(entity.Template.IsKindOf)) return;
        var conditions = entity.WeaponBonusConditions;
        conditions = bombardment > 0 ? conditions | (1u << BombardmentBonusBit) : conditions & ~(1u << BombardmentBonusBit);
        conditions = holdTheLine > 0 ? conditions | (1u << HoldTheLineBonusBit) : conditions & ~(1u << HoldTheLineBonusBit);
        conditions = searchAndDestroy > 0 ? conditions | (1u << SearchAndDestroyBonusBit) : conditions & ~(1u << SearchAndDestroyBonusBit);
        if (conditions == entity.WeaponBonusConditions) return;
        entity.WeaponBonusConditions = conditions;
        MarkDirty(entity);
    }

    /// <summary><c>Player::applyBattlePlanBonusesForObject</c>: a new (or newly acquired) object receives the standing bonuses of its player.</summary>
    private void ApplyBattlePlanToEntity(Entity entity)
    {
        if (!_players.TryGetValue(entity.Owner.Value, out var owner) || owner.BattlePlan is not { HasAggregate: true } state) return;
        ApplyBattlePlanBonusToEntity(entity, state.AggregateBombardment, state.AggregateHoldTheLine, state.AggregateSearchAndDestroy, state.ValidKindOf, state.InvalidKindOf);
    }

    /// <summary><c>Player::removeBattlePlanBonusesForObject</c>: an object that leaves the player loses every battle-plan weapon bonus it was given.</summary>
    private void RemoveBattlePlanFromEntity(Entity entity)
    {
        if (!_players.TryGetValue(entity.Owner.Value, out var owner) || owner.BattlePlan is not { HasAggregate: true } state) return;
        ApplyBattlePlanBonusToEntity(entity, -1, -1, -1, state.ValidKindOf, state.InvalidKindOf);
    }

    /// <inheritdoc cref="IWorldServices.ParalyzeTroops"/>
    public void ParalyzeTroops(PlayerId player, ImmutableArray<string> validKinds, ImmutableArray<string> invalidKinds, long untilTick)
    {
        untilTick = DisabledMasks.EnabledFrom(Time, untilTick);
        foreach (var entity in _entities.Values)
        {
            if (entity.Owner != player || entity.DestroyRequested || !validKinds.Any(entity.Template.IsKindOf) || invalidKinds.Any(entity.Template.IsKindOf)) continue;
            // paralyzeTroop: Object::setDisabledUntil(DISABLED_PARALYZED, ...) - the object's modules but those that process it stand (its AI among them) until the plan has settled.
            entity.ParalyzedUntil = untilTick;
            foreach (var module in entity.ModuleArray)
                if (module is AIUpdateInterface ai) ai.StopForDisable(this);
            DelayWeaponsUntil(entity, untilTick, false);
            MarkDirty(entity);
        }
    }

    /// <inheritdoc cref="IWorldServices.DamageScalarOf"/>
    public Fixed DamageScalarOf(Entity entity)
    {
        if (!_players.TryGetValue(entity.Owner.Value, out var owner) || owner.BattlePlan is not { HoldTheLine: > 0 } state || state.HoldTheLineArmor == Fixed.One) return Fixed.One;
        return state.ValidKindOf.Any(entity.Template.IsKindOf) && !state.InvalidKindOf.Any(entity.Template.IsKindOf) && !entity.Template.IsKindOf("PROJECTILE") ? state.HoldTheLineArmor : Fixed.One;
    }

    /// <summary>The factor the search-and-destroy plan of the object's player applies to the object's shroud clearing range (1 when the player has none or the object does not qualify).</summary>
    private Fixed BattlePlanSightScalarOf(Entity entity)
    {
        if (!_players.TryGetValue(entity.Owner.Value, out var owner) || owner.BattlePlan is not { SearchAndDestroy: > 0 } state || state.SearchAndDestroySight == Fixed.One) return Fixed.One;
        return state.ValidKindOf.Any(entity.Template.IsKindOf) && !state.InvalidKindOf.Any(entity.Template.IsKindOf) && !entity.Template.IsKindOf("PROJECTILE") ? state.SearchAndDestroySight : Fixed.One;
    }

    /// <inheritdoc cref="IWorldServices.SetShroudScalar"/>
    public void SetShroudScalar(Entity entity, Fixed scalar)
    {
        entity.ShroudScalar = scalar;
        MarkDirty(entity);
    }
}
