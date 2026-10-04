using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary><c>BattlePlanStatus</c>: the strategy center's three plans (<c>OPTION_ONE</c>, <c>OPTION_TWO</c>, <c>OPTION_THREE</c> of its command buttons) and "none".</summary>
public enum BattlePlanKind : byte
{
    None = 0,
    Bombardment = 1,
    HoldTheLine = 2,
    SearchAndDestroy = 3,
}

/// <summary>
/// <c>BattlePlanBonuses</c> (<c>BattlePlanUpdate.h</c>): what one strategy center hands to its player's army. The three integers are the weapon-bonus switches (<c>BATTLEPLAN_*</c>, set while
/// positive), the scalars multiply armor damage and sight, and the kind-of lists choose who qualifies (a valid kind and none of the invalid kinds).
/// </summary>
public sealed record BattlePlanBonus(
    Fixed ArmorScalar, Fixed SightRangeScalar, int Bombardment, int SearchAndDestroy, int HoldTheLine, ImmutableArray<string> ValidKindOf, ImmutableArray<string> InvalidKindOf)
{
    /// <summary>The reference's "no change" default of <c>BattlePlanUpdate</c>'s constructor.</summary>
    public static BattlePlanBonus None(ImmutableArray<string> valid, ImmutableArray<string> invalid) => new(Fixed.One, Fixed.One, 0, 0, 0, valid, invalid);

    public bool Qualifies(Entity entity) => ValidKindOf.Any(entity.Template.IsKindOf) && !InvalidKindOf.Any(entity.Template.IsKindOf);
}

/// <summary>
/// <c>BattlePlanUpdateModuleData</c>. Times are ticks, scalars default to 1 (no change), <c>StrategyCenterSearchAndDestroyDetectsStealth</c> to yes and the max-health change type to
/// <c>PRESERVE_RATIO</c>, as in the reference constructor. Messages, sounds, announcements and the vision object name are presentation (typed, not read: the reference's
/// <c>createVisionObject</c> is commented out in favour of <c>ShroudRevealToAllRange</c>).
/// </summary>
public sealed record BattlePlanUpdateData(
    string PowerName, long BombardmentTicks, long HoldTheLineTicks, long SearchAndDestroyTicks, long TransitionIdleTicks, long ParalyzeTicks,
    Fixed HoldTheLineArmorScalar, Fixed SearchAndDestroySightScalar, Fixed CenterSightScalar, bool CenterDetectsStealth, Fixed CenterMaxHealthScalar, string CenterMaxHealthChangeType,
    ImmutableArray<string> ValidKindOf, ImmutableArray<string> InvalidKindOf) : ModuleData
{
    public static BattlePlanUpdateData Parse(ModuleConfiguration configuration) => new(
        SpecialPowerModules.RequiredPower(configuration),
        ModuleConfigReader.Ticks(configuration, "BombardmentPlanAnimationTime", 0), ModuleConfigReader.Ticks(configuration, "HoldTheLinePlanAnimationTime", 0),
        ModuleConfigReader.Ticks(configuration, "SearchAndDestroyPlanAnimationTime", 0), ModuleConfigReader.Ticks(configuration, "TransitionIdleTime", 0),
        ModuleConfigReader.Ticks(configuration, "BattlePlanChangeParalyzeTime", 0),
        ModuleConfigReader.Real(configuration, "HoldTheLinePlanArmorDamageScalar", Fixed.One), ModuleConfigReader.Real(configuration, "SearchAndDestroyPlanSightRangeScalar", Fixed.One),
        ModuleConfigReader.Real(configuration, "StrategyCenterSearchAndDestroySightRangeScalar", Fixed.One),
        ModuleConfigReader.Bool(configuration, "StrategyCenterSearchAndDestroyDetectsStealth", true),
        ModuleConfigReader.Real(configuration, "StrategyCenterHoldTheLineMaxHealthScalar", Fixed.One),
        ModuleConfigReader.Find(configuration, "StrategyCenterHoldTheLineMaxHealthChangeType")?.Value.Value is EnumValue change ? change.Name : "PRESERVE_RATIO",
        ModuleConfigReader.Flags(configuration, "ValidMemberKindOf")?.Names ?? [], ModuleConfigReader.Flags(configuration, "InvalidMemberKindOf")?.Names ?? []);
}

/// <summary>
/// <c>BattlePlanUpdate.cpp</c> (the strategy center): the unpack / active / pack / idle cycle of one battle plan and the bonuses it hands out. A plan is chosen with <see cref="SelectPlan"/>
/// (the special power's <see cref="ISpecialPowerHandler.Initiate"/> reads it from <see cref="SpecialPowerTarget.Option"/>: 1 = bombardment, 2 = hold the line, 3 = search and destroy, the command button's
/// <c>OPTION_ONE/TWO/THREE</c>, carried by <c>UseSpecialPowerCommand.Option</c>). The module unpacks for the plan's animation time, then <em>activates</em>:
/// <c>changeBattlePlan(+1)</c> on the player, which sets the <c>BATTLEPLAN_*</c> weapon bonus on every qualifying object and gives hold the line its damage scalar and search and destroy its sight
/// scalar (see <c>SimulationWorld.BattlePlan.cs</c>), the center itself gets its own bonus (double maximum health for hold the line, a wider shroud and active stealth detection for search and
/// destroy). Choosing another plan makes it pack: the bonuses are removed again and every qualifying troop of the player is <em>paralyzed</em> for <c>BattlePlanChangeParalyzeTime</c> (it
/// neither moves nor fires). Removing the center removes its plan. Turrets do not exist in the simulation, so the bombardment turret is always "centred" and the pack-up never waits for it.
/// Not modelled: the radar blip, messages, sounds and door animations; the unit sight <em>range</em> (only the shroud clearing range is scaled, the simulation has no per-object vision range);
/// the move of a plan's bonuses to a new owner when the center is captured.
/// </summary>
public sealed class BattlePlanUpdate(BattlePlanUpdateData data) : SimModule, IUpdateModule, ISpecialPowerHandler
{
    /// <summary><c>TransitionStatus</c>.</summary>
    public enum TransitionStatus : byte { Idle = 0, Unpacking = 1, Active = 2, Packing = 3 }

    private long _nextWake = -1;
    private TransitionStatus _status;
    private BattlePlanKind _currentPlan;
    private BattlePlanKind _desiredPlan;
    private BattlePlanKind _planAffectingArmy;
    private long _nextReadyTick;
    private Fixed _armorScalar = Fixed.One;
    private Fixed _sightScalar = Fixed.One;
    private int _bombardment;
    private int _searchAndDestroy;
    private int _holdTheLine;

    public override string ModuleName => "BattlePlanUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public TransitionStatus Status => _status;
    public BattlePlanKind DesiredPlan => _desiredPlan;

    /// <summary><c>getActiveBattlePlan</c>: the plan that is unpacked and working, <see cref="BattlePlanKind.None"/> while in transition.</summary>
    public BattlePlanKind ActivePlan => _status == TransitionStatus.Active ? _planAffectingArmy : BattlePlanKind.None;

    public override void OnSpawn(IWorldServices world) => _nextWake = world.Tick + SimulationTiming.SleepNone;

    /// <summary>Choose the plan the center should be in (<c>m_desiredPlan</c>).</summary>
    public void SelectPlan(BattlePlanKind plan) => _desiredPlan = plan;

    bool ISpecialPowerHandler.Handles(IWorldServices world, int powerIndex) => world.SpecialPowers.TryGet(data.PowerName, out var info) && info.Index == powerIndex;

    void ISpecialPowerHandler.Initiate(IWorldServices world, int powerIndex, in SpecialPowerTarget target)
    {
        // initiateIntentToDoSpecialPower: OPTION_ONE/TWO/THREE choose the plan; anything else is "an unsupported strategy" and does nothing.
        if (target.Option is >= 1 and <= 3) _desiredPlan = (BattlePlanKind)target.Option;
    }

    public long Update(IWorldServices world)
    {
        if (_nextReadyTick > world.Tick) return SimulationTiming.SleepNone;
        switch (_status)
        {
            case TransitionStatus.Idle:
                // Idle happens at the start with no plan chosen and after a plan was packed up while the next one waits to unpack.
                if (_desiredPlan != BattlePlanKind.None)
                {
                    _currentPlan = _desiredPlan;
                    SetStatus(world, TransitionStatus.Unpacking);
                }
                break;
            case TransitionStatus.Unpacking:
                SetStatus(world, TransitionStatus.Active);
                break;
            case TransitionStatus.Active:
                // A different plan was chosen: pack up. Bombardment needs the turret centred first; without a turret model it always is, but only an object with AI ever gets here.
                if (_currentPlan != _desiredPlan && (_currentPlan != BattlePlanKind.Bombardment || CrateModules.AiOf(Entity) is not null)) SetStatus(world, TransitionStatus.Packing);
                break;
            case TransitionStatus.Packing:
                SetStatus(world, TransitionStatus.Idle);
                break;
        }
        return SimulationTiming.SleepNone;
    }

    private long AnimationTicks(BattlePlanKind plan) => plan switch
    {
        BattlePlanKind.Bombardment => data.BombardmentTicks,
        BattlePlanKind.HoldTheLine => data.HoldTheLineTicks,
        BattlePlanKind.SearchAndDestroy => data.SearchAndDestroyTicks,
        _ => 0,
    };

    private void SetStatus(IWorldServices world, TransitionStatus next)
    {
        if (_status == next) return;
        switch (next)
        {
            case TransitionStatus.Idle:
                _currentPlan = BattlePlanKind.None;
                _nextReadyTick = world.Tick + data.TransitionIdleTicks;
                break;
            case TransitionStatus.Unpacking:
                _nextReadyTick = world.Tick + AnimationTicks(_currentPlan);
                break;
            case TransitionStatus.Active:
                SetBattlePlan(world, _currentPlan);
                break;
            case TransitionStatus.Packing:
                SetBattlePlan(world, BattlePlanKind.None);
                _nextReadyTick = world.Tick + AnimationTicks(_currentPlan);
                break;
        }
        _status = next;
        world.MarkDirty(Entity);
    }

    private BattlePlanBonus Bonus() => new(_armorScalar, _sightScalar, _bombardment, _searchAndDestroy, _holdTheLine, data.ValidKindOf, data.InvalidKindOf);

    /// <summary><c>BattlePlanUpdate::setBattlePlan</c>: drop the bonuses of the plan that affected the army so far, then hand out those of <paramref name="plan"/> (none: paralyze the troops).</summary>
    private void SetBattlePlan(IWorldServices world, BattlePlanKind plan)
    {
        var player = Entity.Owner;
        if (player.IsSystem) return;
        switch (_planAffectingArmy)
        {
            case BattlePlanKind.Bombardment:
                world.ChangeBattlePlan(player, BattlePlanKind.Bombardment, -1, Bonus());
                break;
            case BattlePlanKind.HoldTheLine:
                world.ChangeBattlePlan(player, BattlePlanKind.HoldTheLine, -1, Bonus());
                if (data.CenterMaxHealthScalar != Fixed.One) ScaleMaxHealth(world, Fixed.One / data.CenterMaxHealthScalar);
                break;
            case BattlePlanKind.SearchAndDestroy:
                world.ChangeBattlePlan(player, BattlePlanKind.SearchAndDestroy, -1, Bonus());
                if (data.CenterSightScalar != Fixed.One) world.SetShroudScalar(Entity, Fixed.One);
                if (data.CenterDetectsStealth) DetectStealth(world, false);
                break;
        }
        _armorScalar = Fixed.One;
        _sightScalar = Fixed.One;
        _bombardment = 0;
        _searchAndDestroy = 0;
        _holdTheLine = 0;

        switch (plan)
        {
            case BattlePlanKind.None:
                world.ParalyzeTroops(player, data.ValidKindOf, data.InvalidKindOf, world.Tick + data.ParalyzeTicks);
                break;
            case BattlePlanKind.Bombardment:
                _bombardment = 1;
                world.ChangeBattlePlan(player, BattlePlanKind.Bombardment, 1, Bonus());
                break;
            case BattlePlanKind.HoldTheLine:
                if (data.CenterMaxHealthScalar != Fixed.Zero) ScaleMaxHealth(world, data.CenterMaxHealthScalar);
                _armorScalar = data.HoldTheLineArmorScalar;
                _holdTheLine = 1;
                world.ChangeBattlePlan(player, BattlePlanKind.HoldTheLine, 1, Bonus());
                break;
            case BattlePlanKind.SearchAndDestroy:
                if (data.CenterSightScalar != Fixed.One) world.SetShroudScalar(Entity, data.CenterSightScalar);
                if (data.CenterDetectsStealth) DetectStealth(world, true);
                _searchAndDestroy = 1;
                _sightScalar = data.SearchAndDestroySightScalar;
                world.ChangeBattlePlan(player, BattlePlanKind.SearchAndDestroy, 1, Bonus());
                break;
        }
        _planAffectingArmy = plan;
    }

    /// <summary><c>body->setMaxHealth(body->getMaxHealth() * scalar, changeType)</c>.</summary>
    private void ScaleMaxHealth(IWorldServices world, Fixed scalar)
    {
        if (Entity.Body is not IMaxHealthAdjustable adjustable) return;
        var maxHealth = Entity.Body.MaxHealth;
        adjustable.AdjustMaxHealth(world, maxHealth * scalar - maxHealth, data.CenterMaxHealthChangeType);
    }

    private void DetectStealth(IWorldServices world, bool enabled)
    {
        foreach (var module in Entity.Modules)
            if (module is StealthDetectorUpdate detector) { detector.SetEnabled(world, enabled); return; }
    }

    public override void OnRemove(IWorldServices world)
    {
        // onDelete: a destroyed center takes its plan back, or the army would keep a bonus nobody supplies.
        if (_planAffectingArmy != BattlePlanKind.None && !Entity.Owner.IsSystem) world.ChangeBattlePlan(Entity.Owner, _planAffectingArmy, -1, Bonus());
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("status", (int)_status);
        writer.Int("currentPlan", (int)_currentPlan);
        writer.Int("desiredPlan", (int)_desiredPlan);
        writer.Int("planAffectingArmy", (int)_planAffectingArmy);
        writer.Int("nextReadyTick", _nextReadyTick);
        writer.Fixed("armorScalar", _armorScalar);
        writer.Fixed("sightScalar", _sightScalar);
        writer.Int("bombardment", _bombardment);
        writer.Int("searchAndDestroy", _searchAndDestroy);
        writer.Int("holdTheLine", _holdTheLine);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        var status = reader.Int("status");
        if (status is < 0 or > (int)TransitionStatus.Packing) throw new InvalidDataException("Battle plan transition status out of range.");
        _status = (TransitionStatus)status;
        _currentPlan = Plan(reader.Int("currentPlan"));
        _desiredPlan = Plan(reader.Int("desiredPlan"));
        _planAffectingArmy = Plan(reader.Int("planAffectingArmy"));
        _nextReadyTick = reader.Int("nextReadyTick");
        _armorScalar = reader.Fixed("armorScalar");
        _sightScalar = reader.Fixed("sightScalar");
        _bombardment = checked((int)reader.Int("bombardment"));
        _searchAndDestroy = checked((int)reader.Int("searchAndDestroy"));
        _holdTheLine = checked((int)reader.Int("holdTheLine"));
    }

    private static BattlePlanKind Plan(long value) => value is >= 0 and <= (int)BattlePlanKind.SearchAndDestroy ? (BattlePlanKind)value : throw new InvalidDataException("Battle plan out of range.");
}
