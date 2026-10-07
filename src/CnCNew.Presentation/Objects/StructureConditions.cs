using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Objects;

/// <summary>
/// Model conditions of buildings, docks, doors and packing equipment that are a pure function of the state one simulation module keeps (docs/MODEL-CONDITIONS-BUILDINGS.md). The reference
/// sets these bits from inside the modules (<c>MissileLauncherBuildingUpdate::switchToState</c>, <c>BattlePlanUpdate::setStatus</c>, <c>DockUpdate::onDockReached</c>,
/// <c>DeployStyleAIUpdate::setMyState</c>, <c>HackInternetAIUpdate</c> states, <c>ParticleUplinkCannonUpdate::setLogicalStatus</c>, <c>TechBuildingBehavior::update</c>,
/// <c>FlammableUpdate::update</c>, <c>StructureCollapseUpdate</c>/<c>StructureToppleUpdate</c>/<c>TensileFormationUpdate</c>, <c>HelicopterSlowDeathBehaviorUpdate</c>, <c>EnemyNearUpdate</c>,
/// <c>SpectreGunshipUpdate</c>, <c>Locomotor::moveTowardsPositionHover</c>, <c>RailroadGuideAIUpdate</c>); here each is read from the module's state. The conditions that need a memory
/// (doors that follow a timer, one-shot latches) are in <see cref="StructureConditionTracker"/>.
/// </summary>
public static class StructureConditions
{
    private static readonly int BurnedStatus = ObjectStatusBits.Index("BURNED");

    public static readonly ModelConditionFlag[] DoorOpening = Doors("OPENING");
    public static readonly ModelConditionFlag[] DoorClosing = Doors("CLOSING");
    public static readonly ModelConditionFlag[] DoorWaitingOpen = Doors("WAITING_OPEN");
    public static readonly ModelConditionFlag[] DoorWaitingToClose = Doors("WAITING_TO_CLOSE");

    public static readonly ModelConditionFlag Packing = ModelConditions.Parse("PACKING");
    public static readonly ModelConditionFlag Unpacking = ModelConditions.Parse("UNPACKING");
    public static readonly ModelConditionFlag DockingActive = ModelConditions.Parse("DOCKING_ACTIVE");
    public static readonly ModelConditionFlag ConstructionComplete = ModelConditions.Parse("CONSTRUCTION_COMPLETE");
    public static readonly ModelConditionFlag Captured = ModelConditions.Parse("CAPTURED");
    public static readonly ModelConditionFlag Smoldering = ModelConditions.Parse("SMOLDERING");
    public static readonly ModelConditionFlag PostCollapse = ModelConditions.Parse("POST_COLLAPSE");
    public static readonly ModelConditionFlag SpecialDamaged = ModelConditions.Parse("SPECIAL_DAMAGED");
    public static readonly ModelConditionFlag EnemyNear = ModelConditions.Parse("ENEMYNEAR");
    public static readonly ModelConditionFlag OverWater = ModelConditions.Parse("OVER_WATER");

    private static ModelConditionFlag[] Doors(string state) => [..Enumerable.Range(1, 4).Select(door => ModelConditions.Parse($"DOOR_{door}_{state}"))];

    /// <summary>Adds the conditions that follow from the state of the entity's modules to <paramref name="set"/>.</summary>
    public static ModelConditionSet Apply(Entity entity, SimulationWorld world, ModelConditionSet set)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(world);
        foreach (var module in entity.Modules)
        {
            switch (module)
            {
                case MissileLauncherBuildingUpdate silo:
                    // MissileLauncherBuildingUpdate::switchToState: exactly one of the four door bits per state, none while closed.
                    set = silo.Door switch
                    {
                        MissileLauncherBuildingUpdate.DoorState.Opening => set.With(DoorOpening[0]),
                        MissileLauncherBuildingUpdate.DoorState.Open => set.With(DoorWaitingOpen[0]),
                        MissileLauncherBuildingUpdate.DoorState.WaitingToClose => set.With(DoorWaitingToClose[0]),
                        MissileLauncherBuildingUpdate.DoorState.Closing => set.With(DoorClosing[0]),
                        _ => set,
                    };
                    break;
                case BattlePlanUpdate center:
                    set = BattlePlan(center, set);
                    break;
                case SpectreGunshipUpdate gunship:
                    // SpectreGunshipUpdate: DOOR_1_CLOSING on insertion and departure, DOOR_1_OPENING while it orbits (the ship opens up to shoot).
                    set = gunship.Status switch
                    {
                        SpectreGunshipUpdate.GunshipStatus.Orbiting => set.With(DoorOpening[0]),
                        SpectreGunshipUpdate.GunshipStatus.Inserting or SpectreGunshipUpdate.GunshipStatus.Departing => set.With(DoorClosing[0]),
                        _ => set,
                    };
                    break;
                case TechBuildingBehavior when !entity.EffectivelyDead && world.IsPlayableSide(entity.Owner):
                    // TechBuildingBehavior::update: captured while a playable side owns it; onDie clears it and hands the building to the neutral player.
                    set = set.With(Captured);
                    break;
                case EnemyNearUpdate { IsEnemyNear: true }:
                    set = set.With(EnemyNear);
                    break;
                case StructureCollapseUpdate { IsCollapsed: true }:
                case StructureToppleUpdate { IsToppled: true }:
                    // The rubble model replaces the standing one: StructureCollapseUpdate/StructureToppleUpdate clear RUBBLE and set POST_COLLAPSE when the building is down.
                    set = set.Without(ModelConditions.Rubble).With(PostCollapse);
                    break;
                case TensileFormationUpdate { Enabled: true }:
                    // TensileFormationUpdate::update sets POST_COLLAPSE on every update of a formation that has been set sliding.
                    set = set.With(PostCollapse);
                    break;
                case HelicopterSlowDeathBehavior { HasHitGround: true }:
                    set = set.With(SpecialDamaged);
                    break;
                case ParticleUplinkCannonUpdate cannon:
                    set = Uplink(cannon.StatusAt(world), set);
                    break;
                case RailroadBehavior when entity.Z < world.GroundHeightAt(entity.X, entity.Y) - TunnelDepth:
                    // RailroadGuideAIUpdate: "turn off smoke effects if we are in a tunnel" reuses OVER_WATER for a locomotive or carriage more than 3 below the ground.
                    set = set.With(OverWater);
                    break;
                case DeployStyleAIUpdate deploy:
                    // DeployStyleAIUpdate::setMyState: DEPLOY shows UNPACKING, UNDEPLOY shows PACKING (DEPLOYED follows the DEPLOYED object status).
                    if (deploy.IsUnpacking) set = set.With(Unpacking);
                    else if (deploy.IsPacking) set = set.With(Packing);
                    DockActive(deploy, ref set);
                    HoverOverWater(entity, world, ref set);
                    break;
                case HackInternetAIUpdate hacker:
                    // UnpackingState keeps UNPACKING set, HackInternetState swaps it for FIRING_A (the typing animation), PackingState shows PACKING.
                    if (hacker.IsUnpacking) set = set.With(Unpacking);
                    else if (hacker.IsHacking) set = set.With(ModelConditions.FiringA);
                    else if (hacker.IsPacking) set = set.With(Packing);
                    DockActive(hacker, ref set);
                    HoverOverWater(entity, world, ref set);
                    break;
                case AIUpdateInterface ai:
                    DockActive(ai, ref set);
                    HoverOverWater(entity, world, ref set);
                    break;
            }
        }

        // FlammableUpdate::update sets BURNED and SMOLDERING together when the burn-out delay passes, and nothing clears either.
        if (entity.HasStatus(BurnedStatus)) set = set.With(Smoldering);

        // DockUpdate::onEnterReached / onDockReached / onExitReached put the same bits on the dock as on the docker.
        world.GetDockActivity(entity, out var dockerInside, out var dockActive);
        if (dockerInside) set = set.With(ModelConditions.Docking);
        if (dockActive) set = set.With(DockingActive);
        return set;
    }

    /// <summary>Depth below the ground at which a train counts as being in a tunnel (<c>RailroadGuideAIUpdate::update</c>: <c>ground - 3.0</c>).</summary>
    public static readonly Fixed TunnelDepth = Fixed.FromInt(3);

    private static void DockActive(AIUpdateInterface ai, ref ModelConditionSet set)
    {
        if (ai.IsDockActive) set = set.With(DockingActive);
    }

    /// <summary>
    /// <c>Locomotor::moveTowardsPositionHover</c>: a hover locomotor sets OVER_WATER while the object stands over a water area and clears it again on land. The reference changes the flag only while the object
    /// moves; standing still it keeps what it had, which is the same unless the object was put onto land or water without moving.
    /// </summary>
    private static void HoverOverWater(Entity entity, SimulationWorld world, ref ModelConditionSet set)
    {
        if (entity.Template.Locomotor is { IsHover: true } && world.Terrain is { } terrain && terrain.IsUnderwater(entity.X, entity.Y)) set = set.With(OverWater);
    }

    /// <summary>
    /// <c>BattlePlanUpdate::setStatus</c>: unpacking a plan sets <c>DOOR_n_OPENING</c>, running it <c>DOOR_n_WAITING_TO_CLOSE</c> and packing it <c>DOOR_n_CLOSING</c>, with n = 1 for bombardment, 2 for
    /// hold the line and 3 for search and destroy; idle sets none.
    /// </summary>
    private static ModelConditionSet BattlePlan(BattlePlanUpdate center, ModelConditionSet set)
    {
        var door = (int)center.CurrentPlan - 1;
        if (door is < 0 or > 2) return set;
        return center.Status switch
        {
            BattlePlanUpdate.TransitionStatus.Unpacking => set.With(DoorOpening[door]),
            BattlePlanUpdate.TransitionStatus.Active => set.With(DoorWaitingToClose[door]),
            BattlePlanUpdate.TransitionStatus.Packing => set.With(DoorClosing[door]),
            _ => set,
        };
    }

    /// <summary>
    /// <c>ParticleUplinkCannonUpdate::setLogicalStatus</c>: preparing shows UNPACKING, almost ready up to and including post-fire shows DEPLOYED, packing shows PACKING; idle and charging set nothing.
    /// </summary>
    private static ModelConditionSet Uplink(ParticleUplinkCannonUpdate.UplinkStatus status, ModelConditionSet set) => status switch
    {
        ParticleUplinkCannonUpdate.UplinkStatus.Preparing => set.With(Unpacking),
        ParticleUplinkCannonUpdate.UplinkStatus.AlmostReady or ParticleUplinkCannonUpdate.UplinkStatus.ReadyToFire
            or ParticleUplinkCannonUpdate.UplinkStatus.Firing or ParticleUplinkCannonUpdate.UplinkStatus.PostFire => set.With(ModelConditions.Deployed),
        ParticleUplinkCannonUpdate.UplinkStatus.Packing => set.With(Packing),
        _ => set,
    };
}

/// <summary>
/// The model conditions that need a memory, one table per entity, advanced once per simulated tick: the production doors of <c>ProductionUpdate::updateDoors</c> (and the airfield's per-space doors held open
/// by <c>ParkingPlaceBehavior</c>), <c>CONSTRUCTION_COMPLETE</c>, and the latched bits whose reference module sets them on an event and clears them on the next one (the bomber's doors
/// <c>DeliveringState::onExit</c>, the checkpoint gate <c>CheckpointUpdate::update</c>, the repair drone's arm <c>SlavedUpdate::setRepairModelConditionStates</c>).
/// <para>
/// <b>Production doors.</b> The reference opens a door only when the finished unit is ready to leave, creates the unit when the door is fully open (<c>ProductionUpdate::update</c>:
/// <c>door->m_doorWaitOpenFrame != 0</c>), keeps the door open for <c>DoorWaitOpenTime</c> after the last unit and then closes it for <c>DoorCloseTime</c>. The simulation creates the unit the
/// moment the queue entry is done and has no door, so the door is run on the simulation's clock instead: it starts opening <c>DoorOpeningTime</c> (and one tick) before the head entry completes (the
/// remaining ticks of the entry are known), is fully open when the unit appears, and from then on follows <c>updateDoors</c> exactly (same transitions, same strict comparisons, same pop to
/// waiting-open of a door that is still closing when the next unit is ready, same hold-open). Opened this early the whole cycle is shifted by <c>DoorOpeningTime</c> against the reference,
/// which is the price of seeing the unit leave through an open door.
/// </para>
/// </summary>
public sealed class StructureConditionTracker
{
    private const int MaxDoors = 4;

    private sealed class Door
    {
        public long Opened = -1;
        public long WaitOpen = -1;
        public long Closed = -1;
        public bool Hold;

        /// <summary>The last sampled tick at which the door was held open: a held door cannot start closing, so the closing of a released door starts no earlier than the tick after.</summary>
        public long LastHold = -1;

        public bool IsClosed => Opened < 0 && WaitOpen < 0 && Closed < 0;
    }

    private sealed class Entry
    {
        public readonly Door[] Doors = [new(), new(), new(), new()];
        public long ConstructionComplete = -1;
        public bool PlaneDoorsOpened;
        public bool CheckpointAlly;
        public bool CheckpointEnemy;
        public int CheckpointDoor;
        public ModelConditionFlag? DroneArm;
    }

    private readonly Dictionary<uint, Entry> _entries = [];

    /// <summary>Entries held (entities with a production, delivery, checkpoint or repair-drone module that were seen).</summary>
    public int Count => _entries.Count;

    /// <summary>The remembered conditions of <paramref name="entity"/> after the tick that was just simulated; call once per entity per tick, after the simulation stepped.</summary>
    public ModelConditionSet Update(Entity entity, SimulationWorld world)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(world);
        ProductionUpdate? production = null;
        ParkingPlaceBehavior? parking = null;
        DeliverPayloadAIUpdate? plane = null;
        CheckpointUpdate? checkpoint = null;
        SlavedUpdate? drone = null;
        foreach (var module in entity.Modules)
        {
            switch (module)
            {
                case ProductionUpdate value: production = value; break;
                case ParkingPlaceBehavior value: parking = value; break;
                case DeliverPayloadAIUpdate value: plane = value; break;
                case CheckpointUpdate value: checkpoint = value; break;
                case SlavedUpdate value: drone = value; break;
            }
        }

        // A producer without door animations and without a construction-complete phase, and a drone that cannot repair, have nothing to remember.
        if (production is { Data: { NumDoorAnimations: 0, ConstructionCompleteTicks: 0 } }) production = null;
        if (drone is { CanRepair: false }) drone = null;
        if (production is null && plane is null && checkpoint is null && drone is null) return ModelConditionSet.Empty;
        if (!_entries.TryGetValue(entity.Id.Value, out var entry)) _entries[entity.Id.Value] = entry = new Entry();
        var set = ModelConditionSet.Empty;
        if (production is not null) set = Production(entry, entity, world, production, parking, set);
        if (plane is not null) set = Plane(entry, plane, set);
        if (checkpoint is not null) set = Checkpoint(entry, checkpoint, set);
        if (drone is not null) set = Drone(entry, drone, set);
        return set;
    }

    /// <summary>Forgets the entities that no longer exist, so the table does not grow over a long match.</summary>
    public void Prune(Func<uint, bool> exists)
    {
        foreach (var id in _entries.Keys.Where(id => !exists(id)).ToArray()) _entries.Remove(id);
    }

    // ------------------------------------------------------------------------------------------------ production

    private static ModelConditionSet Production(Entry entry, Entity entity, SimulationWorld world, ProductionUpdate production, ParkingPlaceBehavior? parking, ModelConditionSet set)
    {
        var data = production.Data;
        var doorCount = Math.Clamp(data.NumDoorAnimations, 0, MaxDoors);
        if (doorCount == 0 && data.ConstructionCompleteTicks == 0) return set;
        var now = world.Tick;

        if (parking is not null)
        {
            // ParkingPlaceBehavior::reserveSpace / releaseSpace / purgeDead: the door of a space stays open while an aircraft holds the space; a closed door opens (ProductionUpdate::setHoldDoorOpen).
            for (var space = 0; space < Math.Min(parking.SpaceCount, doorCount); space++)
            {
                var door = entry.Doors[space];
                door.Hold = parking.OccupantOf(space).IsValid || parking.IsReservedForExit(space);
                if (door.Hold) door.LastHold = now;
                if (door.Hold && door.IsClosed) door.Opened = now;
            }
        }

        // The head entry is about to be handed to the exit: ProductionUpdate::update then reserves a door, opens it and starts the construction-complete phase.
        // The door is waiting open in the tick after its opening time has passed (the strict comparison of updateDoors), so it starts one tick more than DoorOpeningTime
        // before the entry completes: the unit appears behind a door that already shows DOOR_n_WAITING_OPEN, not in the last tick of DOOR_n_OPENING.
        var lead = doorCount > 0 ? data.DoorOpeningTicks + 1 : 0;
        if (!entity.HasStatus(ObjectStatusBits.UnderConstruction) && production.TryGetHead(out var head) && head.RemainingTicks <= Math.Max(lead, 1))
        {
            var produced = world.Templates[head.TemplateIndex];
            var exit = entity.Modules.OfType<IProductionExit>().FirstOrDefault();
            if (exit is null || exit.IsFreeToExitFor(produced))
            {
                var start = Math.Min(now, now + head.RemainingTicks - lead);
                var exitDoor = parking is not null && !produced.IsKindOf("PRODUCED_AT_HELIPAD") ? FirstFreeSpace(parking) : 0;
                if (exitDoor >= 0 && exitDoor < doorCount) ReserveDoor(entry.Doors[exitDoor], now, start);
                if (entry.ConstructionComplete < 0 && data.ConstructionCompleteTicks > 0) entry.ConstructionComplete = start;
            }
        }

        for (var index = 0; index < doorCount; index++)
        {
            var door = entry.Doors[index];
            UpdateDoor(door, data);
            if (door.Opened >= 0) set = set.With(StructureConditions.DoorOpening[index]);
            if (door.WaitOpen >= 0) set = set.With(StructureConditions.DoorWaitingOpen[index]);
            if (door.Closed >= 0) set = set.With(StructureConditions.DoorClosing[index]);
        }

        // ProductionUpdate::update: the construction-complete phase ends when more than ConstructionCompleteDuration has passed.
        if (entry.ConstructionComplete >= 0)
        {
            if (now - entry.ConstructionComplete > data.ConstructionCompleteTicks) entry.ConstructionComplete = -1;
            else set = set.With(StructureConditions.ConstructionComplete);
        }

        void UpdateDoor(Door door, ProductionData timing)
        {
            // ProductionUpdate::updateDoors, with the frame stamps chained so that a gap between two samples (an object out of sight) cannot lose a transition.
            bool moved;
            do
            {
                moved = false;
                if (door.Opened >= 0)
                {
                    if (now - door.Opened > timing.DoorOpeningTicks)
                    {
                        door.WaitOpen = door.Opened + timing.DoorOpeningTicks + 1;
                        door.Opened = -1;
                        moved = true;
                    }
                }
                else if (door.WaitOpen >= 0)
                {
                    if (now - door.WaitOpen > timing.DoorWaitOpenTicks && !door.Hold)
                    {
                        door.Closed = Math.Max(door.WaitOpen + timing.DoorWaitOpenTicks + 1, door.LastHold + 1);
                        door.WaitOpen = -1;
                        moved = true;
                    }
                }
                else if (door.Closed >= 0 && !door.Hold && now - door.Closed > timing.DoorCloseTicks)
                {
                    door.Closed = -1;
                    moved = true;
                }
            }
            while (moved);
        }

        return set;
    }

    /// <summary><c>ProductionUpdate::update</c>, the branch that serves a finished unit: a closed door starts opening, a waiting-open door is kept waiting, a closing door pops to waiting-open.</summary>
    private static void ReserveDoor(Door door, long now, long start)
    {
        if (door.IsClosed) door.Opened = start;
        else if (door.WaitOpen >= 0) door.WaitOpen = now;
        else if (door.Closed >= 0)
        {
            door.WaitOpen = now;
            door.Closed = -1;
            door.Opened = -1;
        }
    }

    /// <summary><c>ParkingPlaceBehavior::reserveDoorForExit</c> (<c>findEmptyPPI</c>): the door of the first space nobody holds or has reserved; -1 when there is none.</summary>
    private static int FirstFreeSpace(ParkingPlaceBehavior parking)
    {
        for (var space = 0; space < parking.SpaceCount; space++)
            if (!parking.OccupantOf(space).IsValid && !parking.IsReservedForExit(space)) return space;
        return -1;
    }

    // ------------------------------------------------------------------------------------------------ latches

    /// <summary><c>DeliveringState::onEnter</c> sets DOOR_1_OPENING, <c>onExit</c> swaps it for DOOR_1_CLOSING, which stays until the next delivery.</summary>
    private static ModelConditionSet Plane(Entry entry, DeliverPayloadAIUpdate plane, ModelConditionSet set)
    {
        if (plane.DoorsOpen)
        {
            entry.PlaneDoorsOpened = true;
            return set.With(StructureConditions.DoorOpening[0]);
        }
        return entry.PlaneDoorsOpened ? set.With(StructureConditions.DoorClosing[0]) : set;
    }

    /// <summary><c>CheckpointUpdate::update</c>: only a change of the ally/enemy scan sets a door bit (open: DOOR_1_OPENING, otherwise DOOR_1_CLOSING); until then the gate shows neither.</summary>
    private static ModelConditionSet Checkpoint(Entry entry, CheckpointUpdate checkpoint, ModelConditionSet set)
    {
        if (checkpoint.AllyNear != entry.CheckpointAlly || checkpoint.EnemyNear != entry.CheckpointEnemy)
        {
            entry.CheckpointAlly = checkpoint.AllyNear;
            entry.CheckpointEnemy = checkpoint.EnemyNear;
            entry.CheckpointDoor = !checkpoint.EnemyNear && checkpoint.AllyNear ? 1 : 2;
        }
        return entry.CheckpointDoor switch
        {
            1 => set.With(StructureConditions.DoorOpening[0]),
            2 => set.With(StructureConditions.DoorClosing[0]),
            _ => set,
        };
    }

    /// <summary>
    /// <c>SlavedUpdate</c>: a drone that can repair is packed up when it is created and after every repair (<c>onObjectCreated</c>, <c>endRepair</c>). <c>setRepairState</c> sets one bit of the arm
    /// through <c>setRepairModelConditionStates</c>, which clears the previous one: UNPACKING when the arm comes out of its packed state, FIRING_B when it extends, FIRING_C when it retracts.
    /// Going from unpacking to ready, from extending to welding and from retracting to ready sets nothing, so the bit of the last call stays.
    /// </summary>
    private static ModelConditionSet Drone(Entry entry, SlavedUpdate drone, ModelConditionSet set)
    {
        if (!drone.CanRepair) return set;
        switch (drone.Repair)
        {
            case SlavedUpdate.RepairState.Unpacking:
                entry.DroneArm = StructureConditions.Unpacking;
                break;
            case SlavedUpdate.RepairState.Extending:
                entry.DroneArm = ModelConditions.Firing[1];
                break;
            case SlavedUpdate.RepairState.Retracting:
                entry.DroneArm = ModelConditions.Firing[2];
                break;
            case SlavedUpdate.RepairState.Ready or SlavedUpdate.RepairState.Welding:
                break;
            default:
                entry.DroneArm = StructureConditions.Packing;
                break;
        }
        return entry.DroneArm is { } arm ? set.With(arm) : set;
    }
}
