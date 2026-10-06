using CnCNew.Data.Definitions;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Objects;

/// <summary>
/// The model condition bits of units that depend on a history the simulation does not keep: a presentation-side port of the reference's small state machines, driven once per simulated tick
/// by the sim events (<see cref="Observe"/>) and the captured entity state (<see cref="Update"/>). It only draws; it never influences the simulation and
/// holds no simulation state (a world restored from a snapshot starts it empty, so the bits restart from "no history"). docs/MODEL-CONDITIONS-UNITS.md names the reference function behind each bit.
/// <list type="bullet">
/// <item><description><c>FiringTracker</c>: CONTINUOUS_FIRE_SLOW/MEAN/FAST from the simulation's own tracker (<see cref="SimulationWorld.ContinuousFireOf"/>), which also speeds the weapon up.</description></item>
/// <item><description><c>AnimationSteeringUpdate::update</c>: CENTER_TO_LEFT/RIGHT and LEFT/RIGHT_TO_CENTER from the turning direction (the heading changed by a full turn rate in one tick, <c>Locomotor::rotateObjAroundLocoPivot</c>'s <c>TURN_POSITIVE/NEGATIVE</c>).</description></item>
/// <item><description><c>PhysicsBehavior::update</c> (SPLATTED after a falling death) and <c>CrushDie::onDie</c> (FRONTCRUSHED/BACKCRUSHED from the crush point nearest to the crusher): from the <see cref="SimEventKind.EntityDied"/> event.</description></item>
/// <item><description><c>TurretAI::friend_turnTowardsAngle</c>: TURRET_ROTATE from the simulation's turret state (<see cref="TurretTracker.IsRotating"/>).</description></item>
/// <item><description><c>Object::setSpecialModelConditionState</c> (<see cref="Cheer"/>): SPECIAL_CHEERING for three seconds.</description></item>
/// <item><description><c>TensileFormationUpdate::update</c>: FREEFALL while an avalanche rock drops.</description></item>
/// </list>
/// </summary>
public sealed class UnitConditionTracker
{
    /// <summary><c>AIGroup::groupCheer</c>: <c>LOGICFRAMES_PER_SECOND * 3</c>.</summary>
    public const int CheerTicks = 3 * 30;

    /// <summary><c>TensileFormationUpdate::update</c>: FREEFALL only while <c>m_life &lt; 100</c> and the rock moved more than 0.2 vertically.</summary>
    private const int TensileFreefallLife = 100;

    // The sim turns by exactly the locomotor's turn rate per tick when the goal is further away; the angle is renormalised, so allow a few raw units of rounding.
    private static readonly Fixed TurnTolerance = Fixed.FromFraction(1, 8192);
    private static readonly Fixed TensileDrop = Fixed.FromDecimal(0.2m);
    private static readonly Fixed Half = Fixed.FromDecimal(0.5m);
    private static readonly int SplattedDeath = NameTables.DeathType.TryIndexOf("SPLATTED", out var splatted) ? splatted : throw new InvalidOperationException("SPLATTED is not a death type.");

    private enum Steering : byte { Straight, CenterToRight, CenterToLeft, RightToCenter, LeftToCenter }

    private enum Turn : byte { None, Positive, Negative }

    private enum Crush : byte { None, Front, Back, Total }

    private sealed class UnitState
    {
        // AnimationSteeringUpdate
        public Steering Steer;
        public long NextSteerFrame;
        public Fixed LastAngle;
        public bool HasAngle;

        // deaths
        public bool Splatted;
        public bool FrontCrushed;
        public bool BackCrushed;

        // Object::m_smcUntil
        public long CheerUntil = -1;

        // TensileFormationUpdate
        public Fixed LastZ;
        public bool HasZ;

        public bool Resolved;
        public AnimationSteeringUpdate? SteeringModule;
        public TensileFormationUpdate? Tensile;
    }

    private readonly Dictionary<uint, UnitState> _states = [];
    private readonly Dictionary<ObjectTemplate, bool> _needsState = [];

    /// <summary>Feeds one simulation event (shots and deaths); call for every event of a tick before <see cref="Update"/>.</summary>
    public void Observe(SimEvent simEvent, SimulationWorld world)
    {
        switch (simEvent.Kind)
        {
            case SimEventKind.EntityDied:
                Died(simEvent, world);
                break;
        }
    }

    /// <summary><c>AIGroup::groupCheer</c> (the <c>ALL_CHEER</c> test key): the objects show SPECIAL_CHEERING for <see cref="CheerTicks"/> ticks after <paramref name="tick"/>; a new cheer restarts the time.</summary>
    public void Cheer(IEnumerable<EntityId> entities, long tick)
    {
        foreach (var id in entities) StateOf(id.Value).CheerUntil = tick + CheerTicks;
    }

    /// <summary>The bits the tracker adds to <paramref name="entity"/> for the tick <paramref name="tick"/> that was just simulated; <paramref name="turrets"/> must already have been updated for it.</summary>
    public ModelConditionSet Update(Entity entity, SimulationWorld world, long tick, TurretTracker turrets)
    {
        var set = ModelConditionSet.Empty;
        var id = entity.Id.Value;
        if (turrets.IsRotating(id)) set = set.With(UnitFlags.TurretRotate);
        // FiringTracker: the stage is simulation state (mean and fast are the object's weapon bonus conditions).
        set = SimulationWorld.ContinuousFireOf(entity) switch
        {
            ContinuousFireStage.Slow => set.With(UnitFlags.ContinuousFireSlow),
            ContinuousFireStage.Mean => set.With(UnitFlags.ContinuousFireMean),
            ContinuousFireStage.Fast => set.With(UnitFlags.ContinuousFireFast),
            _ => set,
        };
        if (!_states.TryGetValue(id, out var state))
        {
            if (!NeedsState(entity)) return set;
            state = new UnitState();
            _states[id] = state;
        }
        if (!state.Resolved) Resolve(state, entity);

        if (state.SteeringModule is not null)
        {
            StepSteering(state, entity, tick);
            set = state.Steer switch
            {
                Steering.CenterToRight => set.With(UnitFlags.CenterToRight),
                Steering.CenterToLeft => set.With(UnitFlags.CenterToLeft),
                Steering.RightToCenter => set.With(UnitFlags.RightToCenter),
                Steering.LeftToCenter => set.With(UnitFlags.LeftToCenter),
                _ => set,
            };
        }

        if (state.Tensile is { } tensile)
        {
            if (tensile.Enabled && tensile.Life is > 0 and < TensileFreefallLife && state.HasZ && (entity.Z - state.LastZ).Abs() > TensileDrop) set = set.With(UnitFlags.Freefall);
            state.LastZ = entity.Z;
            state.HasZ = true;
        }

        if (state.Splatted) set = set.With(UnitFlags.Splatted);
        if (state.FrontCrushed) set = set.With(UnitFlags.FrontCrushed);
        if (state.BackCrushed) set = set.With(UnitFlags.BackCrushed);
        if (tick < state.CheerUntil) set = set.With(UnitFlags.SpecialCheering);
        return set;
    }

    /// <summary>Forgets the objects that no longer exist, so the table does not grow over a long match.</summary>
    public void Prune(Func<uint, bool> exists)
    {
        foreach (var id in _states.Keys.Where(id => !exists(id)).ToArray()) _states.Remove(id);
    }

    // ------------------------------------------------------------------------------------------------ events

    private UnitState StateOf(uint id)
    {
        if (!_states.TryGetValue(id, out var state)) _states[id] = state = new UnitState();
        return state;
    }

    /// <summary>A falling death splats (<c>PhysicsBehavior::update</c> sets SPLATTED when fall damage kills); a crush death of a <c>CrushDie</c> object picks the crushed ends (<c>CrushDie::onDie</c>).</summary>
    private void Died(SimEvent simEvent, SimulationWorld world)
    {
        if (simEvent.Value == SplattedDeath)
        {
            StateOf(simEvent.Entity.Value).Splatted = true;
            return;
        }
        if (simEvent.Value != DeathTypes.Crushed || world.FindEntity(simEvent.Entity) is not { } victim) return;
        var modules = victim.Modules;
        var crushDie = false;
        for (var index = 0; index < modules.Count && !crushDie; index++) crushDie = modules[index].ModuleName == "CrushDie";
        if (!crushDie) return;

        var state = StateOf(simEvent.Entity.Value);
        var crush = world.FindEntity(simEvent.Other) is { } crusher ? CrushLocation(crusher, victim, state.FrontCrushed, state.BackCrushed) : Crush.Total;
        if (crush == Crush.None) return;
        state.FrontCrushed = crush is Crush.Total or Crush.Front;
        state.BackCrushed = crush is Crush.Total or Crush.Back;
    }

    /// <summary><c>crushLocationCheck</c> (CrushDie.cpp): of the middle point and the front and back crush points (half the victim's major radius along its heading) not crushed yet, the nearest to the crusher.</summary>
    private static Crush CrushLocation(Entity crusher, Entity victim, bool frontCrushed, bool backCrushed)
    {
        var offset = victim.Template.GeometryMajorRadius * Half;
        var offsetX = FixedTrig.Cos(victim.Angle) * offset;
        var offsetY = FixedTrig.Sin(victim.Angle) * offset;
        Fixed Distance(Fixed x, Fixed y) => (x - crusher.X) * (x - crusher.X) + (y - crusher.Y) * (y - crusher.Y);

        var result = Crush.None;
        var best = Fixed.MaxValue;
        if (!frontCrushed && !backCrushed)
        {
            result = Crush.Total;
            best = Distance(victim.X, victim.Y);
        }
        if (!frontCrushed)
        {
            var distance = Distance(victim.X + offsetX, victim.Y + offsetY);
            if (distance < best)
            {
                result = backCrushed ? Crush.Total : Crush.Front;
                best = distance;
            }
        }
        if (!backCrushed)
        {
            var distance = Distance(victim.X - offsetX, victim.Y - offsetY);
            if (distance < best) result = frontCrushed ? Crush.Total : Crush.Back;
        }
        return result;
    }

    // ------------------------------------------------------------------------------------------------ per-entity update

    /// <summary>Whether objects of this template have a module the tracker follows every tick (decided once per template: an object's modules never change).</summary>
    private bool NeedsState(Entity entity)
    {
        if (_needsState.TryGetValue(entity.Template, out var needs)) return needs;
        var modules = entity.Modules;
        needs = false;
        for (var index = 0; index < modules.Count && !needs; index++) needs = modules[index] is AnimationSteeringUpdate or TensileFormationUpdate;
        _needsState[entity.Template] = needs;
        return needs;
    }

    private static void Resolve(UnitState state, Entity entity)
    {
        state.Resolved = true;
        var modules = entity.Modules;
        for (var index = 0; index < modules.Count; index++)
        {
            if (modules[index] is AnimationSteeringUpdate steering) state.SteeringModule = steering;
            else if (modules[index] is TensileFormationUpdate tensile) state.Tensile = tensile;
        }
    }

    /// <summary><c>AnimationSteeringUpdate::update</c>, a state machine with a minimum hold time per state.</summary>
    private static void StepSteering(UnitState state, Entity entity, long now)
    {
        var turn = TurnOf(state, entity);
        if (now < state.NextSteerFrame) return;
        var hold = state.SteeringModule!.MinTransitionTicks;
        switch (state.Steer)
        {
            case Steering.Straight:
                if (turn == Turn.Negative) Begin(Steering.CenterToRight);
                else if (turn == Turn.Positive) Begin(Steering.CenterToLeft);
                break;
            case Steering.CenterToRight:
                if (turn != Turn.Negative) Begin(Steering.RightToCenter);
                break;
            case Steering.CenterToLeft:
                if (turn != Turn.Positive) Begin(Steering.LeftToCenter);
                break;
            case Steering.LeftToCenter:
            case Steering.RightToCenter:
                if (turn == Turn.None)
                {
                    state.Steer = Steering.Straight;
                    state.NextSteerFrame = now;
                }
                break;
        }

        void Begin(Steering next)
        {
            state.Steer = next;
            state.NextSteerFrame = now + hold;
        }
    }

    /// <summary>
    /// <c>PhysicsBehavior::getTurning</c>: <c>rotateObjAroundLocoPivot</c> reports TURN_POSITIVE (counter-clockwise, to the left) or TURN_NEGATIVE when the object still has to turn more than its maximum turn
    /// rate, i.e. it turned by exactly that rate this tick; a smaller step finishes the turn (TURN_NONE). The simulation's movement turns the same way (<c>AIUpdateInterface</c>), so the heading change shows it.
    /// </summary>
    private static Turn TurnOf(UnitState state, Entity entity)
    {
        var turn = Turn.None;
        if (state.HasAngle && entity.Template.Locomotor is { } locomotor && locomotor.TurnRatePerTick > Fixed.Zero)
        {
            var delta = FixedTrig.AngleDifference(state.LastAngle, entity.Angle);
            if ((delta - locomotor.TurnRatePerTick).Abs() <= TurnTolerance) turn = Turn.Positive;
            else if ((delta + locomotor.TurnRatePerTick).Abs() <= TurnTolerance) turn = Turn.Negative;
        }
        state.LastAngle = entity.Angle;
        state.HasAngle = true;
        return turn;
    }
}
