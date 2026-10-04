using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>
/// The slave family of the reference engine: objects that are tied to a master (<c>SlavedUpdate</c>: drones and stinger soldiers,
/// <c>MobMemberSlavedUpdate</c>: the members of an angry mob), the spawner side that keeps them (<c>SpawnBehavior</c>, in
/// <c>SpawnModules.cs</c>), the body that lets a hive take damage through its slaves (<c>HiveStructureBody</c>) and the exit that
/// places the slaves of a structure at its spawn points (<c>SpawnPointProductionExitUpdate</c>).
/// </summary>
internal static class SlaveModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<SlavedData>("SlavedUpdate", ModuleOwnerKind.Object, (configuration, _) => SlavedData.Parse(configuration), data => new SlavedUpdate(data)),
        new ModuleBinding<MobMemberData>("MobMemberSlavedUpdate", ModuleOwnerKind.Object, (configuration, _) => MobMemberData.Parse(configuration), data => new MobMemberSlavedUpdate(data)),
        new ModuleBinding<HiveStructureBodyData>("HiveStructureBody", ModuleOwnerKind.Object, (configuration, _) => HiveStructureBodyData.Parse(configuration), data => new HiveStructureBody(data)),
        new ModuleBinding<SpawnPointExitData>("SpawnPointProductionExitUpdate", ModuleOwnerKind.Object, (configuration, _) => SpawnPointExitData.Parse(configuration), data => new SpawnPointProductionExitUpdate(data)),
    ];
}

/// <summary><c>SlavedUpdateInterface</c> (<c>UpdateModule.h</c>): what a spawner or a creation list tells the object it has just made its slave.</summary>
public interface ISlavedModule
{
    /// <summary><c>getSlaverID</c>; invalid once the slave was released or never enslaved.</summary>
    EntityId Slaver { get; }

    /// <summary><c>onEnslave</c>: called once the new object exists (<c>SpawnBehavior::createSpawn</c>, <c>GenericObjectCreationNugget::doStuffToObj</c>).</summary>
    void OnEnslave(IWorldServices world, Entity slaver);

    /// <summary><c>onSlaverDie</c>.</summary>
    void OnSlaverDie(IWorldServices world);

    /// <summary><c>onSlaverDamage</c>: the reference only makes slaves with a <c>ProneUpdate</c> react (not simulated), so the shipped modules ignore it.</summary>
    void OnSlaverDamage(IWorldServices world, in DamageInfo info);

    /// <summary><c>isSelfTasking</c>.</summary>
    bool IsSelfTasking { get; }
}

/// <summary><c>SpawnBehaviorInterface</c>: the living slaves of a spawner, as <c>HiveStructureBody</c> and the mob members need them.</summary>
public interface ISpawnerModule
{
    /// <summary><c>m_spawnIDs</c>: every spawn that has not been seen dead yet, in creation order.</summary>
    IReadOnlyList<EntityId> Slaves { get; }
}

/// <summary>The part of a spawn-point exit that remembers who stands on which spawn point (<c>m_spawnPointOccupier</c>), told by the spawner once it asked for the entity.</summary>
public interface IProductionExitOccupancy
{
    void Occupy(EntityId occupant);
}

/// <summary>Shared geometry and lookups of the slave modules.</summary>
internal static class SlaveSupport
{
    /// <summary><c>getDistanceSquared(a, b, FROM_BOUNDINGSPHERE_2D)</c> between two objects: the distance of the centres on the ground plane minus both bounding circles, never negative.</summary>
    public static Fixed BoundingDistanceSquared2D(Entity from, Entity to)
        => ShrunkSquared(Distance2D(from.X - to.X, from.Y - to.Y) - from.Template.BoundingCircleRadius - to.Template.BoundingCircleRadius);

    /// <summary><c>getDistanceSquared(object, point, FROM_BOUNDINGSPHERE_2D)</c>.</summary>
    public static Fixed BoundingDistanceSquared2D(Entity from, Fixed x, Fixed y)
        => ShrunkSquared(Distance2D(from.X - x, from.Y - y) - from.Template.BoundingCircleRadius);

    public static Fixed CenterDistanceSquared2D(Entity from, Entity to)
    {
        var dx = from.X - to.X;
        var dy = from.Y - to.Y;
        return dx * dx + dy * dy;
    }

    public static Fixed CenterDistanceSquared3D(Entity from, Entity to)
    {
        var dz = from.Z - to.Z;
        return CenterDistanceSquared2D(from, to) + dz * dz;
    }

    private static Fixed Distance2D(Fixed dx, Fixed dy) => Fixed.Sqrt(dx * dx + dy * dy);

    private static Fixed ShrunkSquared(Fixed distance) => distance <= Fixed.Zero ? Fixed.Zero : distance * distance;

    /// <summary>The first entity of <paramref name="ids"/> closest to the point on the ground plane (<c>SpawnBehavior::getClosestSlave</c>: any object still found, nearest wins, the first among equals).</summary>
    public static Entity? Closest(IWorldServices world, IEnumerable<EntityId> ids, Fixed x, Fixed y)
    {
        Entity? closest = null;
        var best = Fixed.MaxValue;
        foreach (var id in ids)
        {
            if (world.FindEntity(id) is not { } candidate) continue;
            var dx = candidate.X - x;
            var dy = candidate.Y - y;
            var distance = dx * dx + dy * dy;
            if (closest is not null && distance >= best) continue;
            closest = candidate;
            best = distance;
        }
        return closest;
    }

    /// <summary><c>GameLogicRandomValue(0, 2*PI)</c> picks from the integer overload (the float limits convert to 0 and 6), so the "random direction" of the reference is a whole number of radians in [0, 6].</summary>
    public static Fixed RandomDirection(IWorldServices world) => Fixed.FromInt(world.LogicRandom.Range(0, 6));

    public static AIUpdateInterface? AiOf(Entity entity) => entity.ModuleArray.OfType<AIUpdateInterface>().FirstOrDefault();

    public static bool IsFlying(Entity entity) => entity.Template.Locomotor is { } locomotor && (locomotor.Surfaces & LocomotorSurfaces.Air) != 0;

    public static Fixed Ratio(Fixed health, Fixed maxHealth) => maxHealth > Fixed.Zero ? health / maxHealth : Fixed.One;

    public static int WeaponBonusBit(string name)
        => NameTablesPhase5.WeaponBonusCondition.TryIndexOf(name, out var index) ? index : throw new InvalidOperationException($"No {name} weapon bonus condition.");
}

// ==================================================================================================================== SlavedUpdate

/// <summary>
/// <c>SlavedUpdateModuleData</c> (<c>SlavedUpdate.h</c>): the keys the shipped drones and stinger soldiers use. The ranges are whole numbers (<c>parseInt</c>), the durations
/// frames of the logic stream, <c>RepairRatePerSecond</c> and the altitudes reals. <c>RepairWeldingSys</c>/<c>RepairWeldingFXBone</c> (sparks) and
/// <c>StayOnSameLayerAsMaster</c> (bridge layers do not exist here) are typed and have no consumer.
/// </summary>
public sealed record SlavedData(
    int GuardMaxRange, int GuardWanderRange, int AttackRange, int AttackWanderRange, int ScoutRange, int ScoutWanderRange, int DistToTargetToGrantRangeBonus,
    int RepairRange, Fixed RepairMinAltitude, Fixed RepairMaxAltitude, Fixed RepairRatePerSecond, int RepairWhenHealthBelowPercentage,
    long MinReadyTicks, long MaxReadyTicks, long MinWeldTicks, long MaxWeldTicks) : ModuleData
{
    public static SlavedData Parse(ModuleConfiguration configuration)
    {
        int Int(string name) => checked((int)ModuleConfigReader.Int(configuration, name, 0));
        var data = new SlavedData(
            Int("GuardMaxRange"), Int("GuardWanderRange"), Int("AttackRange"), Int("AttackWanderRange"), Int("ScoutRange"), Int("ScoutWanderRange"), Int("DistToTargetToGrantRangeBonus"),
            Int("RepairRange"), ModuleConfigReader.Real(configuration, "RepairMinAltitude", Fixed.Zero), ModuleConfigReader.Real(configuration, "RepairMaxAltitude", Fixed.Zero),
            ModuleConfigReader.Real(configuration, "RepairRatePerSecond", Fixed.Zero), Int("RepairWhenBelowHealth%"),
            ModuleConfigReader.Ticks(configuration, "RepairMinReadyTime", 0), ModuleConfigReader.Ticks(configuration, "RepairMaxReadyTime", 0),
            ModuleConfigReader.Ticks(configuration, "RepairMinWeldTime", 0), ModuleConfigReader.Ticks(configuration, "RepairMaxWeldTime", 0));
        if (data.MinReadyTicks > data.MaxReadyTicks || data.MinWeldTicks > data.MaxWeldTicks || data.RepairMinAltitude > data.RepairMaxAltitude)
            throw new InvalidDataException("SlavedUpdate: a minimum exceeds its maximum.");
        return data;
    }
}

/// <summary>
/// <c>SlavedUpdate.cpp</c>: the object follows a master (<see cref="ISlavedModule"/>). It runs the reference's priority list every <c>SLAVED_UPDATE_RATE</c> frames (a quarter
/// of a second; every tick while the repair state machine works): repair the master when it is hurt, hover over the master's victim (attack range) and grant the
/// <c>DRONE_SPOTTING</c> weapon bonus when close, scout ahead of the master's move destination, repair an idle master that is merely hurt, and otherwise stay within
/// <c>GuardMaxRange</c> of the master. A slave whose master is gone crashes (the reference sets <c>DISABLED_UNMANNED</c> and lets physics kill a flier; here a flying slave is
/// killed, a ground slave is just released), a master that changed sides takes its slave with it (<c>defect</c>).
/// <para>
/// Differences, all deliberate: the master's current victim is not stored anywhere in this simulation (attacks are fire commands), so it is taken to be the nearest enemy
/// within the master's weapon range while the master fired during the last two seconds; "pathing" is <see cref="AIUpdateInterface.OrderMove"/>; the welding sparks, model
/// conditions, locomotor sets (panic, ultra accurate, precise z) and layers are presentation or have no counterpart; the repair spot is kept apart from the guard offset
/// (the reference writes an absolute position into that offset, which sends the drone far away once after a repair); a stealthy master does not grant stealth to the
/// slave; <c>onSlaverDamage</c> does nothing without <c>ProneUpdate</c>. The random "direction" is the integer overload of the reference (0 to 6 radians).
/// </para>
/// </summary>
public sealed class SlavedUpdate(SlavedData data) : SimModule, IUpdateModule, ISlavedModule
{
    /// <summary><c>SLAVED_UPDATE_RATE = LOGICFRAMES_PER_SECOND / 4</c> in integer arithmetic.</summary>
    private const int UpdateRate = SimulationTiming.LogicFramesPerSecond / 4;

    private const int CloseEnoughSquared = 15 * 15;
    private const int RepairDistanceSquared = 12 * 12;
    private const long RecentFireTicks = 2 * SimulationTiming.LogicFramesPerSecond;

    private static readonly int UnselectableBit = ObjectStatusBits.Index("UNSELECTABLE");
    private static readonly int DroneSpottingBit = SlaveSupport.WeaponBonusBit("DRONE_SPOTTING");

    /// <summary><c>RepairStates</c> of the repair arm; the presentation derives the model conditions <c>PACKING</c>, <c>UNPACKING</c>, <c>FIRING_B</c> and <c>FIRING_C</c> from it (<c>setRepairModelConditionStates</c>).</summary>
    public enum RepairState { None, Unpacking, Packing, Ready, Extending, Retracting, Welding }

    private long _nextWake = -1;
    private EntityId _slaver;
    private Fixed _offsetX;
    private Fixed _offsetY;
    private int _framesToWait;
    private RepairState _repairState;
    private bool _repairing;

    public override string ModuleName => "SlavedUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public EntityId Slaver => _slaver;
    public bool IsSelfTasking => false;
    public bool IsRepairing => _repairing;

    /// <summary>The state of the repair arm (<c>m_repairState</c>).</summary>
    public RepairState Repair => _repairState;

    /// <summary>The slave can repair its master (<c>RepairRatePerSecond</c> above zero): the reference packs such an object up when it is created (<c>onObjectCreated</c>).</summary>
    public bool CanRepair => data.RepairRatePerSecond > Fixed.Zero;

    public override void OnSpawn(IWorldServices world) => _nextWake = SimulationTiming.SleepForever;

    public void OnEnslave(IWorldServices world, Entity slaver)
    {
        // startSlavedEffects: the pinned stray point, and the slave can no longer be selected.
        _slaver = slaver.Id;
        var direction = SlaveSupport.RandomDirection(world);
        _offsetX = Fixed.FromInt(data.GuardMaxRange) * FixedTrig.Cos(direction);
        _offsetY = Fixed.FromInt(data.GuardMaxRange) * FixedTrig.Sin(direction);
        Entity.Status |= 1UL << UnselectableBit;
        world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepNone);
    }

    public void OnSlaverDie(IWorldServices world) => StopSlavedEffects();

    public void OnSlaverDamage(IWorldServices world, in DamageInfo info) { }

    private void StopSlavedEffects()
    {
        _slaver = EntityId.Invalid;
        _offsetX = Fixed.Zero;
        _offsetY = Fixed.Zero;
        Entity.Status &= ~(1UL << UnselectableBit);
    }

    public long Update(IWorldServices world)
    {
        if (_framesToWait > 0) _framesToWait--;
        if (_repairState == RepairState.None)
        {
            if (_framesToWait > 0) return SimulationTiming.SleepNone;
            _framesToWait = UpdateRate;
        }
        // Not enslaved (or released): nothing to follow. The reference polls; the world wakes this module again from OnEnslave.
        if (!_slaver.IsValid) return SimulationTiming.SleepForever;
        if (SlaveSupport.AiOf(Entity) is not { } ai || Entity.Template.Locomotor is null) return SimulationTiming.SleepNone;

        var master = world.FindEntity(_slaver);
        if (master is null || master.EffectivelyDead || master.DestroyRequested)
        {
            StopSlavedEffects();
            ai.StopInternalMove();
            if (SlaveSupport.IsFlying(Entity)) world.Kill(Entity.Id);
            return SimulationTiming.SleepNone;
        }
        if (world.RelationshipOf(master.Owner, Entity.Owner) != Relationship.Allies) world.TransferOwnership(Entity.Id, master.Owner);

        // The drone satisfies the spotting condition again below, on every update it still deserves it.
        if ((master.WeaponBonusConditions & (1u << DroneSpottingBit)) != 0)
        {
            master.WeaponBonusConditions &= ~(1u << DroneSpottingBit);
            world.MarkDirty(master);
        }

        // "NOTE: Health percentage will always be 100 should the drone be incapable of repairing."
        var healthPercentage = 100;
        if (data.RepairRatePerSecond > Fixed.Zero && master.Body is { } body && body.MaxHealth > Fixed.Zero)
            healthPercentage = checked((int)(body.Health * Fixed.FromInt(100) / body.MaxHealth).Floor());

        if (healthPercentage <= data.RepairWhenHealthBelowPercentage)
        {
            DoRepairLogic(world, master, ai);
            return SimulationTiming.SleepNone;
        }

        if (data.AttackRange != 0 && MasterTarget(world, master) is { } target)
        {
            EndRepair();
            DoAttackLogic(world, master, ai, target);
            return SimulationTiming.SleepNone;
        }

        if (data.ScoutRange != 0 && master.HasMoveTarget)
        {
            var half = Fixed.FromInt(data.GuardMaxRange) / Fixed.FromInt(2);
            if (SlaveSupport.BoundingDistanceSquared2D(master, master.MoveTargetX, master.MoveTargetY) > half * half)
            {
                EndRepair();
                DoScoutLogic(world, master, ai);
                return SimulationTiming.SleepNone;
            }
        }

        if (healthPercentage < 100)
        {
            DoRepairLogic(world, master, ai);
            return SimulationTiming.SleepNone;
        }

        if (data.GuardMaxRange != 0)
        {
            var pinnedX = master.X + _offsetX;
            var pinnedY = master.Y + _offsetY;
            var pinnedZ = world.GroundHeightAt(pinnedX, pinnedY);
            var dx = Entity.X - pinnedX;
            var dy = Entity.Y - pinnedY;
            var dz = Entity.Z - pinnedZ;
            var stray = Fixed.FromInt(2 * data.GuardMaxRange);
            if (ai.IsIdle && dx * dx + dy * dy + dz * dz > Fixed.FromInt(CloseEnoughSquared))
            {
                EndRepair();
                DoGuardLogic(world, ai, pinnedX, pinnedY);
            }
            else if (SlaveSupport.CenterDistanceSquared3D(Entity, master) > stray * stray)
            {
                EndRepair();
                DoGuardLogic(world, ai, pinnedX, pinnedY);
            }
        }
        return SimulationTiming.SleepNone;
    }

    /// <summary>Stands in for <c>AIUpdateInterface::getCurrentVictim</c> of the master (see the class summary).</summary>
    private static Entity? MasterTarget(IWorldServices world, Entity master)
    {
        if (master.LastFireTick < 0 || world.Tick - master.LastFireTick > RecentFireTicks || SimulationWorld.WeaponIndexIn(master, master.CurrentWeaponSlot) < 0) return null;
        var range = world.Weapons[SimulationWorld.WeaponIndexIn(master, master.CurrentWeaponSlot)].RangeWith(world.Templates.GlobalWeaponBonus, master.WeaponBonusConditions);
        Entity? best = null;
        var bestDistance = range * range;
        foreach (var candidate in world.Entities)
        {
            if (candidate.EffectivelyDead || candidate.DestroyRequested || candidate.Body is null || !world.AreEnemies(master.Owner, candidate.Owner)) continue;
            var distance = SlaveSupport.CenterDistanceSquared2D(master, candidate);
            if (distance > bestDistance || (best is not null && distance == bestDistance && candidate.Id.Value > best.Id.Value)) continue;
            best = candidate;
            bestDistance = distance;
        }
        return best;
    }

    /// <summary><c>doAttackLogic</c>: go to the victim, but no further from the master than <c>AttackRange</c>; grant the spotting bonus when close.</summary>
    private void DoAttackLogic(IWorldServices world, Entity master, AIUpdateInterface ai, Entity target)
    {
        var distance = SlaveSupport.BoundingDistanceSquared2D(Entity, target.X, target.Y);
        var (x, y) = ClampedGoal(master, target.X, target.Y, target.Z, data.AttackRange, distance);
        if (data.AttackWanderRange != 0)
        {
            var direction = SlaveSupport.RandomDirection(world);
            x += Fixed.FromInt(data.AttackWanderRange) * FixedTrig.Cos(direction);
            y += Fixed.FromInt(data.AttackWanderRange) * FixedTrig.Sin(direction);
        }
        ai.OrderMove(world, x, y, false);
        var bonus = Fixed.FromInt(data.DistToTargetToGrantRangeBonus);
        if (distance < bonus * bonus)
        {
            master.WeaponBonusConditions |= 1u << DroneSpottingBit;
            world.MarkDirty(master);
        }
    }

    /// <summary><c>doScoutLogic</c>: hover over the master's move destination, no further from the master than <c>ScoutRange</c>.</summary>
    private void DoScoutLogic(IWorldServices world, Entity master, AIUpdateInterface ai)
    {
        var distance = SlaveSupport.BoundingDistanceSquared2D(Entity, master.MoveTargetX, master.MoveTargetY);
        var (x, y) = ClampedGoal(master, master.MoveTargetX, master.MoveTargetY, world.GroundHeightAt(master.MoveTargetX, master.MoveTargetY), data.ScoutRange, distance);
        if (data.ScoutWanderRange != 0)
        {
            var direction = SlaveSupport.RandomDirection(world);
            x += Fixed.FromInt(data.ScoutWanderRange) * FixedTrig.Cos(direction);
            y += Fixed.FromInt(data.ScoutWanderRange) * FixedTrig.Sin(direction);
        }
        ai.OrderMove(world, x, y, false);
    }

    /// <summary>The goal itself when it is within <paramref name="range"/> of the slave, otherwise the point <paramref name="range"/> from the master towards it (the reference normalizes the master-to-goal vector in three dimensions).</summary>
    private (Fixed X, Fixed Y) ClampedGoal(Entity master, Fixed goalX, Fixed goalY, Fixed goalZ, int range, Fixed distanceSquared)
    {
        var limit = Fixed.FromInt(range);
        if (distanceSquared <= limit * limit) return (goalX, goalY);
        var dx = goalX - master.X;
        var dy = goalY - master.Y;
        var dz = goalZ - master.Z;
        var length = Fixed.Sqrt(dx * dx + dy * dy + dz * dz);
        return length <= Fixed.Zero ? (master.X, master.Y) : (master.X + dx * limit / length, master.Y + dy * limit / length);
    }

    /// <summary><c>doGuardLogic</c>: back to the pinned point (re-rolled around the master when the slave may wander).</summary>
    private void DoGuardLogic(IWorldServices world, AIUpdateInterface ai, Fixed pinnedX, Fixed pinnedY)
    {
        if (data.GuardWanderRange != 0)
        {
            var direction = SlaveSupport.RandomDirection(world);
            _offsetX = Fixed.FromInt(data.GuardMaxRange) * FixedTrig.Cos(direction);
            _offsetY = Fixed.FromInt(data.GuardMaxRange) * FixedTrig.Sin(direction);
            pinnedX += _offsetX;
            pinnedY += _offsetY;
        }
        ai.OrderMove(world, pinnedX, pinnedY, false);
    }

    /// <summary><c>doRepairLogic</c>: fly to the master, run the unpack/extend/weld/retract states and heal it while welding.</summary>
    private void DoRepairLogic(IWorldServices world, Entity master, AIUpdateInterface ai)
    {
        var closeEnough = SlaveSupport.BoundingDistanceSquared2D(Entity, master) < Fixed.FromInt(RepairDistanceSquared);
        if (closeEnough)
        {
            switch (_repairState)
            {
                case RepairState.None:
                    SetRepairState(world, master, RepairState.Ready);
                    break;
                case RepairState.Ready:
                case RepairState.Extending:
                    if (_framesToWait == 0) SetRepairState(world, master, RepairState.Welding);
                    break;
                case RepairState.Unpacking:
                case RepairState.Welding:
                case RepairState.Retracting:
                    if (_framesToWait == 0) SetRepairState(world, master, RepairState.Ready);
                    break;
            }
        }
        else
        {
            _repairing = false;
            // The hover altitude is the locomotor's business here; the draw keeps the logic stream aligned with the reference.
            _ = world.LogicRandom.Range(data.RepairMinAltitude, data.RepairMaxAltitude);
            ai.OrderMove(world, master.X, master.Y, false);
            if (_framesToWait == 0) SetRepairState(world, master, RepairState.Ready);
        }
        if (closeEnough && _repairing && master.Body is not null)
        {
            var amount = data.RepairRatePerSecond / Fixed.FromInt(SimulationTiming.LogicFramesPerSecond);
            world.Damage(master.Id, new DamageInfo(Entity.Id, amount, DamageTypes.Healing, DeathTypes.None, false));
        }
    }

    /// <summary><c>endRepair</c>.</summary>
    private void EndRepair()
    {
        if (_repairState == RepairState.None) return;
        _repairState = RepairState.None;
        _framesToWait = UpdateRate;
        _repairing = false;
    }

    /// <summary><c>setRepairState</c>: the transitions and their waiting times (the model conditions and the sparks are presentation).</summary>
    private void SetRepairState(IWorldServices world, Entity master, RepairState requested)
    {
        if (requested == _repairState) return;
        switch (requested)
        {
            case RepairState.Unpacking:
            case RepairState.Packing:
                _framesToWait = 15;
                break;
            case RepairState.Ready:
                switch (_repairState)
                {
                    case RepairState.None:
                        _repairState = RepairState.Unpacking;
                        _framesToWait = 15;
                        break;
                    case RepairState.Welding:
                        _repairState = RepairState.Retracting;
                        _framesToWait = 5;
                        MoveToNewRepairSpot(world, master);
                        break;
                    default:
                        _repairState = RepairState.Ready;
                        _framesToWait = world.LogicRandom.Range(checked((int)data.MinReadyTicks), checked((int)data.MaxReadyTicks));
                        break;
                }
                break;
            case RepairState.Welding:
                if (_repairState == RepairState.Ready)
                {
                    _repairState = RepairState.Extending;
                    _framesToWait = 5;
                }
                else
                {
                    _repairState = RepairState.Welding;
                    _framesToWait = world.LogicRandom.Range(checked((int)data.MinWeldTicks), checked((int)data.MaxWeldTicks));
                    // "Don't actually start healing until we fire our first sparks."
                    _repairing = true;
                }
                break;
        }
    }

    /// <summary><c>moveToNewRepairSpot</c>: after a weld the drone flies to another point around the master (<c>RepairRange</c>).</summary>
    private void MoveToNewRepairSpot(IWorldServices world, Entity master)
    {
        if (data.RepairRange == 0 || SlaveSupport.AiOf(Entity) is not { } ai) return;
        var direction = SlaveSupport.RandomDirection(world);
        _ = world.LogicRandom.Range(data.RepairMinAltitude, data.RepairMaxAltitude);
        ai.OrderMove(world, master.X + Fixed.FromInt(data.RepairRange) * FixedTrig.Cos(direction), master.Y + Fixed.FromInt(data.RepairRange) * FixedTrig.Sin(direction), false);
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.UInt("slaver", _slaver.Value);
        writer.Fixed("offsetX", _offsetX);
        writer.Fixed("offsetY", _offsetY);
        writer.Int("framesToWait", _framesToWait);
        writer.Int("repairState", (int)_repairState);
        writer.Bool("repairing", _repairing);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _slaver = new EntityId(checked((uint)reader.UInt("slaver")));
        _offsetX = reader.Fixed("offsetX");
        _offsetY = reader.Fixed("offsetY");
        _framesToWait = checked((int)reader.Int("framesToWait"));
        var state = reader.Int("repairState");
        if (state < 0 || state > (int)RepairState.Welding) throw new InvalidDataException("Repair state out of range.");
        _repairState = (RepairState)state;
        _repairing = reader.Bool("repairing");
    }
}

// ============================================================================================================ MobMemberSlavedUpdate

/// <summary>
/// <c>MobMemberSlavedUpdateModuleData</c> (<c>MobMemberSlavedUpdate.h</c>): <c>MustCatchUpRadius</c> (default 50), <c>NoNeedToCatchUpRadius</c> (default 25, read by no code path of the
/// reference's <c>update</c>), <c>Squirrelliness</c> (clamped to [0, 1]; steers self-tasking, not simulated) and <c>CatchUpCrisisBailTime</c> (default 999999 updates).
/// </summary>
public sealed record MobMemberData(int MustCatchUpRadius, int NoNeedToCatchUpRadius, Fixed Squirrelliness, long CatchUpCrisisBailTime) : ModuleData
{
    public static MobMemberData Parse(ModuleConfiguration configuration)
    {
        var squirrelliness = ModuleConfigReader.Real(configuration, "Squirrelliness", Fixed.Zero);
        return new MobMemberData(
            checked((int)ModuleConfigReader.Int(configuration, "MustCatchUpRadius", 50)),
            checked((int)ModuleConfigReader.Int(configuration, "NoNeedToCatchUpRadius", 25)),
            Fixed.Min(Fixed.One, Fixed.Max(Fixed.Zero, squirrelliness)),
            ModuleConfigReader.Int(configuration, "CatchUpCrisisBailTime", 999999));
    }
}

/// <summary>
/// <c>MobMemberSlavedUpdate.cpp</c>: a member of an angry mob keeps up with its nexus (the master). Every 16th tick it measures the distance to the nexus: beyond
/// <c>MustCatchUpRadius</c> it walks to where the nexus is going (its move destination, unless it is already headed within five cells of it) or, when the nexus stands
/// still, to the nexus itself; beyond three times that radius for more than <c>CatchUpCrisisBailTime / 3</c> checks it walks straight to the nexus, and for more than
/// <c>CatchUpCrisisBailTime</c> checks it dies. Without a master (it was never enslaved, or the nexus is gone) the member dies at its first check. Not simulated: the member's
/// colour tint, locomotor-set shuffling (the walking/panic/wander sets do not exist; the random draw that picks one is kept), the victim bookkeeping (members auto-acquire
/// targets like every armed object; see <c>MissionSession.DriveCombat</c>) and the squirrelliness self-tasking.
/// </summary>
public sealed class MobMemberSlavedUpdate(MobMemberData data) : SimModule, IUpdateModule, ISlavedModule
{
    private const int CheckInterval = 16;

    private long _nextWake = -1;
    private EntityId _slaver;
    private int _framesToWait;
    private long _crisisTimer;

    public override string ModuleName => "MobMemberSlavedUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public EntityId Slaver => _slaver;
    public bool IsSelfTasking => false;

    public override void OnSpawn(IWorldServices world)
    {
        _framesToWait = world.LogicRandom.Range(0, 20);
        _nextWake = world.Tick + SimulationTiming.SleepNone;
    }

    public void OnEnslave(IWorldServices world, Entity slaver) => _slaver = slaver.Id;

    public void OnSlaverDie(IWorldServices world) => _slaver = EntityId.Invalid;

    public void OnSlaverDamage(IWorldServices world, in DamageInfo info) { }

    public long Update(IWorldServices world)
    {
        if (Entity.EffectivelyDead) return SimulationTiming.SleepForever;
        var master = _slaver.IsValid ? world.FindEntity(_slaver) : null;
        if (master is null || master.DestroyRequested)
        {
            _slaver = EntityId.Invalid;
            world.Kill(Entity.Id);
            return SimulationTiming.SleepNone;
        }
        if (SlaveSupport.AiOf(Entity) is not { } ai || SlaveSupport.AiOf(master) is null) return SimulationTiming.SleepNone;
        if (++_framesToWait < CheckInterval) return SimulationTiming.SleepNone;
        _framesToWait = 0;
        if (Entity.Template.Locomotor is null) return SimulationTiming.SleepNone;

        var radius = Fixed.FromInt(data.MustCatchUpRadius);
        var distance = SlaveSupport.CenterDistanceSquared3D(Entity, master);
        if (distance > radius * radius)
        {
            if (master.HasMoveTarget)
            {
                // "only if I am not headed there already": my goal within five path cells of the master's goal.
                var cells = Fixed.FromInt(5 * TerrainMap.CellSize);
                var dx = Entity.MoveTargetX - master.MoveTargetX;
                var dy = Entity.MoveTargetY - master.MoveTargetY;
                if (!Entity.HasMoveTarget || dx * dx + dy * dy > cells * cells) ai.OrderMove(world, master.MoveTargetX, master.MoveTargetY, false);
            }
            else ai.OrderMove(world, master.X, master.Y, false);

            var crisis = radius * Fixed.FromInt(3);
            if (distance > crisis * crisis)
            {
                _crisisTimer++;
                if (_crisisTimer > data.CatchUpCrisisBailTime)
                {
                    world.Kill(Entity.Id);
                    return SimulationTiming.SleepNone;
                }
                if (_crisisTimer > data.CatchUpCrisisBailTime / 3) ai.OrderMove(world, master.X, master.Y, false);
            }
        }
        else if (Entity.HasMoveTarget)
        {
            _crisisTimer = 0;
            // The reference rolls which locomotor set to walk with (wander, panic, normal or none of them); there is one locomotor here, the draw stays.
            _ = world.LogicRandom.Range(0, 10);
        }
        else _crisisTimer = 0;
        return SimulationTiming.SleepNone;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.UInt("slaver", _slaver.Value);
        writer.Int("framesToWait", _framesToWait);
        writer.Int("crisisTimer", _crisisTimer);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _slaver = new EntityId(checked((uint)reader.UInt("slaver")));
        _framesToWait = checked((int)reader.Int("framesToWait"));
        _crisisTimer = reader.Int("crisisTimer");
    }
}

// ============================================================================================================== HiveStructureBody

/// <summary>
/// <c>HiveStructureBodyModuleData</c> (<c>HiveStructureBody.h</c>): a structure body plus <c>PropagateDamageTypesToSlavesWhenExisting</c> and
/// <c>SwallowDamageTypesIfSlavesNotExisting</c> (damage-type lists, default none).
/// </summary>
public sealed record HiveStructureBodyData(BodyData Body, ulong PropagateMask, ulong SwallowMask) : ModuleData
{
    public static HiveStructureBodyData Parse(ModuleConfiguration configuration) => new(
        BodyData.Parse(configuration),
        Mask(configuration, "PropagateDamageTypesToSlavesWhenExisting"),
        Mask(configuration, "SwallowDamageTypesIfSlavesNotExisting"));

    private static ulong Mask(ModuleConfiguration configuration, string name)
        => ModuleConfigReader.Flags(configuration, name) is { } flags ? ObjectStatusBits.Mask(flags, NameTables.DamageType) : 0;
}

/// <summary>
/// <c>HiveStructureBody::attemptDamage</c>: damage of a propagated type does not hurt the hive when it has slaves — the slave closest to the shooter takes it instead
/// (<c>SpawnBehaviorInterface::getClosestSlave</c>); a hive that houses riders instead of spawning slaves (<c>getContain</c>, the Internet Center) passes it on to the rider
/// closest to the shooter. Without a slave or rider the damage is swallowed when its type is in the swallow list, otherwise (and for every other type, and when the shooter is
/// unknown) the structure takes it. The hive's own armor was applied before the hit reached the body, so the slave receives the damage the hive would have taken — the reference
/// hands the same unadjusted struct on and lets the slave's armor apply; the hive's armor is the only one that is used here.
/// </summary>
public sealed class HiveStructureBody(HiveStructureBodyData data) : ActiveBody(data.Body, "HiveStructureBody")
{
    public override DamageOutcome AttemptDamage(IWorldServices world, in DamageInfo info)
    {
        if (info.DamageType >= 0 && info.DamageType < 64 && (data.PropagateMask & (1UL << info.DamageType)) != 0)
        {
            IReadOnlyList<EntityId>? candidates = null;
            if (Entity.ModuleArray.OfType<ISpawnerModule>().FirstOrDefault() is { } spawner) candidates = spawner.Slaves;
            else if (Entity.Container is { } container) candidates = container.Contained;
            if (candidates is not null && info.Source.IsValid && world.FindEntity(info.Source) is { } shooter)
            {
                if (SlaveSupport.Closest(world, candidates, shooter.X, shooter.Y) is { } slave) return world.Damage(slave.Id, info);
                if ((data.SwallowMask & (1UL << info.DamageType)) != 0) return DamageOutcome.NoEffect;
            }
        }
        return base.AttemptDamage(world, info);
    }
}

// ======================================================================================================= SpawnPointProductionExitUpdate

/// <summary><c>SpawnPointProductionExitUpdateModuleData</c>: the bone name (<c>SpawnPointBoneName</c>) that the object's model carries one bone with per spawn point; the model is presentation data, so the name has no consumer.</summary>
public sealed record SpawnPointExitData(string SpawnPointBoneName) : ModuleData
{
    public static SpawnPointExitData Parse(ModuleConfiguration configuration) => new(ModuleConfigReader.Text(configuration, "SpawnPointBoneName"));
}

/// <summary>
/// <c>SpawnPointProductionExitUpdate.cpp</c>: a production exit with a fixed number of spawn points (at most <c>MAX_SPAWN_POINTS</c> = 10) that each hold one object: it is
/// free while a point is unoccupied (a point whose occupant no longer exists is released), the object appears on the first free point, facing outwards, on the ground.
/// The points are the bones of the model in the reference; here there is no model, so their number is the owner's <c>SpawnNumber</c> and they lie on the owner's bounding
/// circle, evenly spaced from its heading. The reference holds the new object (<c>DISABLED_HELD</c>), which has no counterpart: held spawns stay where they are because the
/// shipped slaves (<c>SlavedUpdate</c> without ranges) never walk by themselves.
/// </summary>
public sealed class SpawnPointProductionExitUpdate(SpawnPointExitData data) : SimModule, IProductionExit, IProductionExitOccupancy, IWorldAttached
{
    private const int MaxSpawnPoints = 10;

    private readonly List<(EntityId Id, long Tick)> _occupiers = [];
    private int _claimed = -1;

    public override string ModuleName => "SpawnPointProductionExitUpdate";
    public string BoneName => data.SpawnPointBoneName;

    /// <summary>How many spawn points the object has (see the class summary).</summary>
    public int SpawnPointCount => Math.Min(MaxSpawnPoints, Entity.ModuleArray.OfType<SpawnBehavior>().FirstOrDefault()?.SpawnNumber ?? 0);

    private IWorldServices? _world;

    /// <summary>The exit asks the world whether its occupants still exist from <see cref="IsFreeToExit"/>, which has no world argument.</summary>
    public void Attach(IWorldServices world) => _world = world;

    public bool IsFreeToExit
    {
        get
        {
            if (_world is not null) Revalidate(_world);
            return FreeSlot() >= 0;
        }
    }

    private int FreeSlot()
    {
        var count = SpawnPointCount;
        while (_occupiers.Count < count) _occupiers.Add((EntityId.Invalid, 0));
        for (var slot = 0; slot < count; slot++)
            if (!_occupiers[slot].Id.IsValid) return slot;
        return -1;
    }

    /// <summary><c>revalidateOccupiers</c>: a spawn point whose occupant is gone is free again (an occupant claimed this very tick has not been created yet and still counts).</summary>
    private void Revalidate(IWorldServices world)
    {
        for (var slot = 0; slot < _occupiers.Count; slot++)
        {
            var (id, tick) = _occupiers[slot];
            if (id.IsValid && world.Tick > tick && world.FindEntity(id) is null) _occupiers[slot] = (EntityId.Invalid, 0);
        }
    }

    public (Fixed X, Fixed Y, Fixed Z, Fixed Angle) ClaimExit(IWorldServices world)
    {
        _world = world;
        var slot = FreeSlot();
        if (slot < 0) throw new InvalidOperationException("A spawn point exit thought it had room but then failed.");
        _claimed = slot;
        var angle = FixedTrig.Normalize(Entity.Angle + FixedTrig.TwoPi * Fixed.FromInt(slot) / Fixed.FromInt(Math.Max(SpawnPointCount, 1)));
        var radius = Entity.Template.BoundingCircleRadius;
        var x = Entity.X + radius * FixedTrig.Cos(angle);
        var y = Entity.Y + radius * FixedTrig.Sin(angle);
        return (x, y, world.GroundHeightAt(x, y), angle);
    }

    public void Occupy(EntityId occupant)
    {
        if (_claimed < 0 || _claimed >= _occupiers.Count) return;
        _occupiers[_claimed] = (occupant, _world?.Tick ?? 0);
        _claimed = -1;
    }

    public override void Describe(IStateWriter writer)
    {
        if (_occupiers.All(occupier => !occupier.Id.IsValid)) return;
        writer.BeginArray("occupiers");
        foreach (var (id, tick) in _occupiers)
        {
            writer.BeginObject(null);
            writer.UInt("id", id.Value);
            writer.Int("tick", tick);
            writer.EndObject();
        }
        writer.EndArray();
    }

    public override void Load(IStateReader reader)
    {
        _occupiers.Clear();
        if (!reader.Has("occupiers")) return;
        foreach (var entry in reader.Array("occupiers")) _occupiers.Add((new EntityId(checked((uint)entry.UInt("id"))), entry.Int("tick")));
    }
}
