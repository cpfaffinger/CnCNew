using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>
/// The containment subsystem: <c>GarrisonContain</c> (167 objects unlocked alone, the largest single blocker by
/// measured impact) and <c>TransportContain</c> (2 objects alone — small, but a real second implementor of
/// <see cref="ContainModuleBase"/>, which is why the shared surface was worth extracting now rather than left
/// duplicated). Deliberately not a full port of the reference's <c>OpenContain</c> base (firepoints/exit
/// paths/weapon-bonus passthrough/capture handling/collision-triggered entry — all need infrastructure phase 5 does
/// not have): capacity, <c>KindOf</c>/relationship gating, death ejection, and periodic healing/regen are modeled;
/// everything else is typed (Opaque where genuinely unmodeled) and documented at each schema entry.
/// </summary>
internal static class ContainModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<GarrisonContainData>("GarrisonContain", ModuleOwnerKind.Object, (configuration, _) => GarrisonContainData.Parse(configuration), data => new GarrisonContain(data)),
        new ModuleBinding<TransportContainData>("TransportContain", ModuleOwnerKind.Object, (configuration, _) => TransportContainData.Parse(configuration), data => new TransportContain(data)),
        new ModuleBinding<HelixContainData>("HelixContain", ModuleOwnerKind.Object, (configuration, _) => HelixContainData.Parse(configuration), data => new HelixContain(data)),
        new ModuleBinding<TunnelContainData>("TunnelContain", ModuleOwnerKind.Object, (configuration, _) => TunnelContainData.Parse(configuration), data => new TunnelContain(data)),
    ];
}

/// <summary>
/// <c>OpenContainModuleData</c>'s shared fields (<c>OpenContain.cpp</c>), reused by <see cref="GarrisonContainData"/>
/// and <see cref="TransportContainData"/>. <see cref="AllowInsideKindOf"/> has no engine-wide default (unlike
/// <c>OpenContain</c>'s own "everything allowed"): both current subtypes narrow it to <c>INFANTRY</c> in their own
/// constructors, so callers pass that default in explicitly.
/// </summary>
public sealed record OpenContainData(
    DieMuxData DieMux, Fixed DamagePercentToUnits,
    ImmutableArray<string> AllowInsideKindOf, ImmutableArray<string> ForbidInsideKindOf,
    bool AllowAlliesInside, bool AllowEnemiesInside, bool AllowNeutralInside)
{
    public static OpenContainData Parse(RatedModuleConfiguration configuration, ImmutableArray<string> defaultAllowInsideKindOf) => new(
        DieMuxData.Parse(configuration),
        ModuleConfigReader.Real(configuration, "DamagePercentToUnits", Fixed.Zero),
        ModuleConfigReader.Flags(configuration, "AllowInsideKindOf")?.Names ?? defaultAllowInsideKindOf,
        ModuleConfigReader.Flags(configuration, "ForbidInsideKindOf")?.Names ?? [],
        ModuleConfigReader.Bool(configuration, "AllowAlliesInside", true),
        ModuleConfigReader.Bool(configuration, "AllowEnemiesInside", true),
        ModuleConfigReader.Bool(configuration, "AllowNeutralInside", true));
}

/// <summary>
/// Shared <c>OpenContain</c> behavior: <c>KindOf</c>/relationship gating (<c>Relationship</c> of the container's owner towards the candidate's), entering/leaving (position snap
/// to the container, clearing a pending move order), and death handling. <c>OpenContain::onDie</c> applies
/// <c>DamagePercentToUnits</c> to each occupant's current health, then <c>killRidersWhoAreNotFreeToExit</c> (a no-op but for
/// <see cref="TransportContain"/>) and <c>removeAllContained</c>. Capacity and the periodic <see cref="Update"/> behavior are subtype-specific.
/// </summary>
public abstract class ContainModuleBase(OpenContainData data) : SimModule, IUpdateModule, IDieModule, IContainerModule
{
    protected readonly List<EntityId> Riders = [];
    private long _nextWake = -1;

    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public abstract int ContainMax { get; }

    /// <summary>
    /// <c>OpenContain::exitObjectViaDoor</c> (OpenContain.cpp): the rider is taken out (<c>removeFromContain</c>, which puts it on the container), and with at least one exit path
    /// it is set on the path's <c>ExitStart</c> bone with the container's heading and walks to the <c>ExitEnd</c> bone, moved to the nearest place it can stand on
    /// (<c>Pathfinder::adjustToPossibleDestination</c>) - the riders of one transport do not end on one spot. It ignores the container as an obstacle on that walk and other
    /// units' collisions for a second. A model without the bone answers with the container's position (<c>Object::getSingleLogicalBonePosition</c>), which the adjustment then
    /// moves off the container. Not modeled: the door animation (<c>DoorOpenTime</c>), a rally point of the container, the fall of a rider that leaves in the air.
    /// </summary>
    protected void ExitObjectViaDoor(IWorldServices world, Entity rider, int numberOfExitPaths, ref int whichExitPath)
    {
        Remove(world, rider, true);
        world.Emit(SimEventKind.EntityUncontained, rider.Id, Entity.Id, string.Empty, 0);
        if (numberOfExitPaths <= 0) return;
        var (centreX, centreY, _) = Entity.FrameEnd(world.Tick);
        ExitPath? path = null;
        if (world.Templates.Geometry.ExitPaths.TryGetValue(Entity.Template.Name, out var paths))
        {
            if (paths.Length != numberOfExitPaths)
                throw new InvalidDataException($"The model geometry of '{Entity.Template.Name}' has {paths.Length} exit paths, the module has {numberOfExitPaths}.");
            path = paths[numberOfExitPaths > 1 ? whichExitPath - 1 : 0];
        }
        // "To cycle from 1 to n": only a container with several paths counts.
        if (numberOfExitPaths > 1) whichExitPath = whichExitPath % numberOfExitPaths + 1;
        var cos = FixedTrig.Cos(Entity.Angle);
        var sin = FixedTrig.Sin(Entity.Angle);
        (Fixed X, Fixed Y) World(BonePose? bone) => bone is { } pose ? (centreX + pose.X * cos - pose.Y * sin, centreY + pose.X * sin + pose.Y * cos) : (centreX, centreY);
        var (startX, startY) = World(path?.Start);
        var (endX, endY) = World(path?.End);
        rider.X = startX;
        rider.Y = startY;
        rider.Angle = Entity.Angle;
        world.MarkDirty(rider);
        if (CrateModules.AiOf(rider) is not { } ai) return;
        world.FindPossibleDestination(rider, ref endX, ref endY);
        // The container has no rally point of its own here (OpenContain::m_rallyPointExists is never set). The rule fix door-exit-to-rally (the community source) takes
        // the rally point of the object's production exit instead: an infantryman healed in a barracks walks on to the barracks' rally point.
        var hasRally = false;
        Fixed rallyX = Fixed.Zero, rallyY = Fixed.Zero;
        if (world.Templates.Rules.Has(Rules.GameFix.DoorExitToRally) && Entity.Modules.OfType<IProductionExit>().Any())
            foreach (var module in Entity.Modules)
                if (module is ProductionUpdate production && production.TryGetRally(out rallyX, out rallyY)) { hasRally = true; break; }
        ai.OrderExitContainer(world, Entity, endX, endY, true, hasRally, rallyX, rallyY);
    }

    public int ContainCount => Riders.Count;
    public IReadOnlyList<EntityId> Contained => Riders;

    /// <inheritdoc cref="IContainerModule.HoldsRiders"/>
    public virtual bool HoldsRiders => true;

    /// <summary>
    /// The switched part of <c>OpenContain::m_passengersAllowedToFire</c>: set by <c>PassengersFireUpgrade</c> (never cleared, as in the original) and, from their own data, by
    /// <c>InternetHackContain</c>, <c>OverlordContain</c> and <c>RiderChangeContain</c>. Saved only when set. The flag the container has is <see cref="PassengersMayFire"/>.
    /// </summary>
    public bool PassengersAllowedToFire { get; set; }

    /// <summary>The value of <c>PassengersAllowedToFire</c> in the container's data (<c>OpenContain</c>'s constructor copies it into <c>m_passengersAllowedToFire</c>).</summary>
    protected virtual bool PassengersFireByData => false;

    /// <summary>
    /// <c>OpenContain::m_passengersAllowedToFire</c>: the data's value, or switched on since (<see cref="PassengersAllowedToFire"/>). The data's part is not state: the flag is only ever
    /// switched on, so the two together are the original's one flag.
    /// </summary>
    public bool PassengersMayFire => PassengersAllowedToFire || PassengersFireByData;

    /// <summary>
    /// <c>OpenContain::isPassengerAllowedToFire</c> (OpenContain.cpp; the rule of every container without its own - tunnel, cave, parachute, heal): no unless the flag is set, and a
    /// container that is itself inside another one asks that one (without an id). No shipped tunnel, parachute or heal container sets the flag, so their riders never fire.
    /// </summary>
    public virtual bool IsPassengerAllowedToFire(IWorldServices world, Entity? rider) => PassengersMayFire && OuterLetsFire(world, Entity);

    /// <inheritdoc cref="IContainerModule.PassesWeaponBonus"/>
    public virtual bool PassesWeaponBonus => false;

    /// <summary>The part of <c>OpenContain::isPassengerAllowedToFire</c> after the flag: a container that rides in another one passes the question on, without an id.</summary>
    internal static bool OuterLetsFire(IWorldServices world, Entity container)
        => !container.ContainedBy.IsValid || (world.FindEntity(container.ContainedBy)?.Container is { } outer && outer.IsPassengerAllowedToFire(world, null));

    /// <inheritdoc cref="IContainerModule.IsSpecialOverlordStyleContainer"/>
    public virtual bool IsSpecialOverlordStyleContainer => false;

    /// <summary>
    /// <c>WEAPONBONUSCONDITION_GARRISONED</c>: set on a rider by <c>GarrisonContain::onContaining</c> and <c>HelixContain::onContaining</c> and cleared by their <c>onRemoving</c> - not by
    /// <c>TransportContain</c> - so the weapon bonus lines of the data (<c>GARRISONED RANGE 133%</c>, <c>GARRISONED DAMAGE 125%</c> in GameData.ini) apply to the rider's weapons.
    /// </summary>
    internal static readonly uint GarrisonedBonus = 1u << SlaveSupport.WeaponBonusBit("GARRISONED");

    public abstract bool IsValidContainerFor(IWorldServices world, Entity candidate, bool checkCapacity);

    protected bool PassesKindOfAndRelationship(IWorldServices world, Entity candidate)
    {
        if (!data.AllowInsideKindOf.IsEmpty && !data.AllowInsideKindOf.Any(candidate.Template.IsKindOf)) return false;
        if (data.ForbidInsideKindOf.Any(candidate.Template.IsKindOf)) return false;
        return world.RelationshipOf(Entity.Owner, candidate.Owner) switch
        {
            Relationship.Allies => data.AllowAlliesInside,
            Relationship.Enemies => data.AllowEnemiesInside,
            _ => data.AllowNeutralInside,
        };
    }

    public virtual void Add(IWorldServices world, Entity candidate)
    {
        // Both ends of the relation change their hashed state (the rider list here, the container link and position there). Marked here, once, so that no
        // caller can forget it: a scripted garrison entry on arrival did (finding D8, GC_NukeGeneral tick 40 and six other missions).
        world.MarkDirty(Entity);
        world.MarkDirty(candidate);
        Riders.Add(candidate.Id);
        candidate.ContainedBy = Entity.Id;
        candidate.HasMoveTarget = false;
        // Where the container's begun frame ends (docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md §1.3, rule 3): a container that has had its turn in this tick has made its whole step
        // at 30 Hz. 30 Hz: its position.
        var (x, y, _) = Entity.FrameEnd(world.Tick);
        candidate.X = x;
        candidate.Y = y;
    }

    public virtual void Remove(IWorldServices world, Entity candidate, bool positionAtContainer)
    {
        world.MarkDirty(Entity);
        world.MarkDirty(candidate);
        Riders.Remove(candidate.Id);
        candidate.ContainedBy = EntityId.Invalid;
        if (positionAtContainer) (candidate.X, candidate.Y, _) = Entity.FrameEnd(world.Tick);
        // F-60: a rider that attacked from inside ends that attack when it leaves (the original's exitObjectViaDoor gives it a new state, aiFollowPath).
        if (candidate.Acquisition is { Victim.IsValid: true }) world.EndAttack(candidate);
    }

    public virtual void OnDie(IWorldServices world, in DamageInfo info)
    {
        if (!data.DieMux.IsApplicable(Entity, info)) return;
        if (data.DamagePercentToUnits > Fixed.Zero)
            foreach (var riderId in Riders.ToArray())
            {
                var rider = world.FindEntity(riderId);
                if (rider?.Body is null) continue;
                world.Damage(rider.Id, new DamageInfo(info.Source, rider.Body.Health * data.DamagePercentToUnits, DamageTypes.Unresistable, DeathTypes.Normal, false));
            }
        KillRidersWhoAreNotFreeToExit(world);
        EjectAll(world);
    }

    /// <summary><c>OpenContain::killRidersWhoAreNotFreeToExit</c>: nothing; only a transport has riders that may not leave.</summary>
    protected virtual void KillRidersWhoAreNotFreeToExit(IWorldServices world) { }

    protected void EjectAll(IWorldServices world)
    {
        foreach (var riderId in Riders.ToArray())
        {
            var rider = world.FindEntity(riderId);
            if (rider is null) continue;
            Remove(world, rider, true);
            world.Emit(SimEventKind.EntityUncontained, rider.Id, Entity.Id, string.Empty, 0);
        }
    }

    /// <summary>Removes anything that died while contained (both reference <c>update()</c>s do this first).</summary>
    protected void PruneDead(IWorldServices world)
    {
        foreach (var riderId in Riders.ToArray())
        {
            var rider = world.FindEntity(riderId);
            if (rider is null || rider.EffectivelyDead) Riders.Remove(riderId);
        }
    }

    public abstract long Update(IWorldServices world);

    /// <summary>
    /// Above 30 Hz only: the tick inside a logic frame of the original (1 .. ticks per frame - 1) in which a rider came in, where it is not the frame's first. A container
    /// that gives its riders something once per frame of the original counts the frames from there (<see cref="TransportContain"/>'s regeneration). At 30 Hz it stays empty
    /// and nothing of it is written.
    /// </summary>
    protected Dictionary<uint, int> EnterPhases { get; } = [];

    protected void DescribeContained(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.BeginArray("contained");
        foreach (var riderId in Riders)
        {
            writer.BeginObject(null);
            writer.UInt("id", riderId.Value);
            if (EnterPhases.TryGetValue(riderId.Value, out var phase)) writer.Int("phase", phase);
            writer.EndObject();
        }
        writer.EndArray();
        if (PassengersAllowedToFire) writer.Bool("passengersFire", true);
    }

    protected void LoadContained(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        Riders.Clear();
        EnterPhases.Clear();
        foreach (var entry in reader.Array("contained"))
        {
            Riders.Add(new EntityId(checked((uint)entry.UInt("id"))));
            if (entry.Has("phase")) EnterPhases[checked((uint)entry.UInt("id"))] = checked((int)entry.Int("phase"));
        }
        PassengersAllowedToFire = reader.Has("passengersFire") && reader.Bool("passengersFire");
    }
}

// ==================================================================================================== GarrisonContain

/// <summary>
/// <c>OpenContainModuleData</c> plus <c>GarrisonContainModuleData</c>'s own two mechanically relevant fields.
/// <see cref="Base"/>'s <c>AllowInsideKindOf</c> defaults to <c>INFANTRY</c> — <c>GarrisonContainModuleData</c>'s own
/// constructor narrows the generic <c>OpenContain</c> default (which allows everything) to infantry only.
/// </summary>
public sealed record GarrisonContainData(OpenContainData Base, int ContainMax, bool HealObjects, long TimeForFullHealTicks) : ModuleData
{
    /// <summary>
    /// <c>IsEnclosingContainer</c> (default yes, GarrisonContain.h): the riders are inside the walls. No on the four fire bases, whose riders sit on the building's stations,
    /// visible. Read for where a rider fires from (F-60).
    /// </summary>
    public bool IsEnclosingContainer { get; init; } = true;

    public static GarrisonContainData Parse(RatedModuleConfiguration configuration) => new(
        OpenContainData.Parse(configuration, ["INFANTRY"]),
        checked((int)ModuleConfigReader.Int(configuration, "ContainMax", -1)),
        ModuleConfigReader.Bool(configuration, "HealObjects", false),
        ModuleConfigReader.Ticks(configuration, "TimeForFullHeal", 0))
    {
        IsEnclosingContainer = ModuleConfigReader.Bool(configuration, "IsEnclosingContainer", true),
    };
}

/// <summary>
/// <c>GarrisonContain.cpp</c>. <see cref="IsValidContainerFor"/> extends the shared gating with the Garrison-specific
/// rule: not valid while <c>REALLYDAMAGED</c> (exactly that state — <c>RUBBLE</c> buildings are already dead) unless
/// <c>KindOf = GARRISONABLE_UNTIL_DESTROYED</c>. The same rule ejects everyone the moment the building's damage state
/// transitions into <c>REALLYDAMAGED</c> (<see cref="IDamageModule.OnBodyDamageStateChange"/>). Healing
/// (<see cref="GarrisonContainData.HealObjects"/>) follows <c>healSingleObject</c>: a sliver of
/// <c>MaxHealth / TimeForFullHeal</c> per tick until the occupant has been inside for the full duration, then a hard
/// snap to full. Deliberately not modeled: <c>InitialRoster</c> (needs synchronous nested spawning, a bigger pipeline
/// change than this first cut justifies), firing from inside, garrison points/positions (art-derived, not INI data),
/// the apparent-controlling-player/team-swap system (no team system), capture handling (no capture system).
/// </summary>
public sealed class GarrisonContain(GarrisonContainData data) : ContainModuleBase(data.Base), IDamageModule, IContainerModule
{
    private readonly Dictionary<uint, long> _containedSinceTick = [];

    public override string ModuleName => "GarrisonContain";
    public override int ContainMax => data.ContainMax;

    /// <summary><c>IsEnclosingContainer</c> of the data: the riders are inside the walls (every garrison but the four fire bases).</summary>
    public bool IsEnclosing => data.IsEnclosingContainer;

    /// <summary>
    /// <c>GarrisonContain::isPassengerAllowedToFire</c>: "a GarrisonContain always lets people shoot out" - whatever the data's flag says and whatever the rider is - unless the
    /// building is <c>DISABLED_SUBDUED</c>. Nothing in this simulation sets <c>DISABLED_SUBDUED</c> yet (there is no subdual damage), so the test never refuses here today.
    /// </summary>
    public override bool IsPassengerAllowedToFire(IWorldServices world, Entity? rider) => (world.DisabledFlagsOf(Entity) & DisabledMask.Subdued) == 0;

    public override void OnSpawn(IWorldServices world)
        => NextWake = data.HealObjects ? world.Tick + SimulationTiming.SleepNone : SimulationTiming.SleepForever;

    public override bool IsValidContainerFor(IWorldServices world, Entity candidate, bool checkCapacity)
    {
        if (Entity.EffectivelyDead || !PassesKindOfAndRelationship(world, candidate)) return false;
        if (Entity.Body is ActiveBody body && body.DamageState == BodyDamageStates.ReallyDamaged && !Entity.Template.IsKindOf("GARRISONABLE_UNTIL_DESTROYED"))
            return false;
        return !checkCapacity || data.ContainMax < 0 || ContainCount < data.ContainMax;
    }

    public override void Add(IWorldServices world, Entity candidate)
    {
        base.Add(world, candidate);
        // onContaining: "give the object a garrisoned version of its weapon".
        candidate.WeaponBonusConditions |= GarrisonedBonus;
        _containedSinceTick[candidate.Id.Value] = world.Tick;
        // The riders of an enclosing garrison are matched to its fire points once per logic frame of the original (update, UPDATE_SLEEP_NONE); a fire base puts each on a station.
        if (data.HealObjects || data.IsEnclosingContainer) world.WakeAt(Entity, this, world.Tick);
        if (!data.IsEnclosingContainer) PickStation(world, candidate);
        RecalcControllingPlayer(world);
        // addToContain's redeployOccupants: an enclosing garrison matches its riders to its points at once, not only at its next frame.
        if (data.IsEnclosingContainer) world.MatchGarrisonPoints(Entity, this);
    }

    // ------------------------------------------------------------------------------------------- fire points (F-60, step 4)

    // GarrisonContain::m_garrisonPointData: the rider at each of the 40 points (invalid: free) and the victim it was put there for.
    private readonly EntityId[] _pointRider = new EntityId[ContainerFirePoints.MaxGarrisonPoints];
    private readonly EntityId[] _pointTarget = new EntityId[ContainerFirePoints.MaxGarrisonPoints];
    private int _pointsInUse;

    // GarrisonContain::m_stationPointList: the rider of each station of a fire base (invalid: free).
    private EntityId[] _stationRider = [];

    private ContainerFirePoints? FirePointsOf(IWorldServices world) => world.Templates.Geometry.FirePoints.TryGetValue(Entity.Template.Name, out var points) ? points : null;

    /// <summary><c>findConditionIndex</c>: which of the three sets of fire points the body's state uses - rubble uses the really damaged one.</summary>
    private int ConditionIndex() => Entity.Body is ActiveBody body ? Math.Min(body.DamageState, BodyDamageStates.ReallyDamaged) : BodyDamageStates.Pristine;

    private (Fixed X, Fixed Y, Fixed Z) Place(BonePose pose)
    {
        var cos = FixedTrig.Cos(Entity.Angle);
        var sin = FixedTrig.Sin(Entity.Angle);
        return (Entity.X + pose.X * cos - pose.Y * sin, Entity.Y + pose.X * sin + pose.Y * cos, Entity.Z + pose.Z);
    }

    /// <summary><c>m_garrisonPoint[condition][index]</c>: a fire point of the model, or the building's centre for an index beyond the model's points (<c>loadGarrisonPoints</c>).</summary>
    public (Fixed X, Fixed Y, Fixed Z) PointPosition(IWorldServices world, int condition, int index)
        => FirePointsOf(world) is { } points && condition < points.Garrison.Length && index < points.Garrison[condition].Length
            ? Place(points.Garrison[condition][index])
            : (Entity.X, Entity.Y, Entity.Z);

    /// <summary><c>findClosestFreeGarrisonPointIndex</c>: the free point closest to the target in three dimensions, the lowest index on a tie; -1 when all 40 are taken.</summary>
    private int ClosestFreePoint(IWorldServices world, int condition, Fixed x, Fixed y, Fixed z)
    {
        if (_pointsInUse == ContainerFirePoints.MaxGarrisonPoints) return -1;
        var closest = -1;
        var best = Fixed.Zero;
        for (var index = 0; index < ContainerFirePoints.MaxGarrisonPoints; index++)
        {
            if (_pointRider[index].IsValid) continue;
            var (px, py, pz) = PointPosition(world, condition, index);
            var distance = SquaredDistance(px - x, py - y, pz - z);
            if (closest < 0 || distance < best) (closest, best) = (index, distance);
        }
        return closest;
    }

    private static Fixed SquaredDistance(Fixed dx, Fixed dy, Fixed dz) => dx * dx + dy * dy + dz * dz;

    /// <summary>
    /// <c>calcBestGarrisonPosition</c>: where a rider of this enclosing garrison would fire at the target from - the free point closest to it. A rider's own point is taken and so
    /// not free (a quirk of the original); false when every point is taken.
    /// </summary>
    public bool TryBestPosition(IWorldServices world, Fixed x, Fixed y, Fixed z, out (Fixed X, Fixed Y, Fixed Z) position)
    {
        var condition = ConditionIndex();
        var index = ClosestFreePoint(world, condition, x, y, z);
        position = index >= 0 ? PointPosition(world, condition, index) : default;
        return index >= 0;
    }

    /// <summary><c>getObjectGarrisonPointIndex</c>: the point the rider stands at, or -1 at the centre.</summary>
    public int PointOf(EntityId rider)
    {
        if (_pointsInUse == 0) return -1;
        for (var index = 0; index < ContainerFirePoints.MaxGarrisonPoints; index++)
            if (_pointRider[index] == rider) return index;
        return -1;
    }

    /// <summary><c>putObjectAtGarrisonPoint</c>: the rider stands at the point.</summary>
    private void PutAt(IWorldServices world, Entity rider, EntityId target, int condition, int index)
    {
        _pointRider[index] = rider.Id;
        _pointTarget[index] = target;
        _pointsInUse++;
        (rider.X, rider.Y, rider.Z) = PointPosition(world, condition, index);
        world.MarkDirty(Entity);
        world.MarkDirty(rider);
    }

    /// <summary><c>putObjectAtBestGarrisonPoint</c>: a rider at the centre takes the free point closest to (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>).</summary>
    public void PutAtBestPoint(IWorldServices world, Entity rider, EntityId target, Fixed x, Fixed y, Fixed z)
    {
        if (PointOf(rider.Id) >= 0) return;
        var condition = ConditionIndex();
        var index = ClosestFreePoint(world, condition, x, y, z);
        if (index >= 0) PutAt(world, rider, target, condition, index);
    }

    /// <summary><c>removeObjectFromGarrisonPoint</c>: the rider leaves its point and stands at the building's centre again.</summary>
    public void RemoveFromPoint(IWorldServices world, Entity rider)
    {
        var index = PointOf(rider.Id);
        if (index < 0) return;
        _pointRider[index] = EntityId.Invalid;
        _pointTarget[index] = EntityId.Invalid;
        _pointsInUse--;
        (rider.X, rider.Y, rider.Z) = (Entity.X, Entity.Y, Entity.Z);
        world.MarkDirty(Entity);
        world.MarkDirty(rider);
    }

    /// <summary>
    /// <c>trackTargets</c> for one rider at a point: it moves to a free point strictly closer to its victim's position (<c>calcDistSqr</c> against its own position).
    /// </summary>
    public void TrackTarget(IWorldServices world, Entity rider, EntityId victim, Fixed x, Fixed y, Fixed z)
    {
        var own = PointOf(rider.Id);
        if (own < 0) return;
        var condition = ConditionIndex();
        var index = ClosestFreePoint(world, condition, x, y, z);
        if (index < 0) return;
        var (px, py, pz) = PointPosition(world, condition, index);
        if (SquaredDistance(px - x, py - y, pz - z) >= SquaredDistance(rider.X - x, rider.Y - y, rider.Z - z)) return;
        RemoveFromPoint(world, rider);
        PutAt(world, rider, victim, condition, index);
    }

    /// <summary>The riders at points, in the order of the points (<c>removeInvalidObjectsFromGarrisonPoints</c> walks them so).</summary>
    public IEnumerable<EntityId> RidersAtPoints()
    {
        for (var index = 0; index < ContainerFirePoints.MaxGarrisonPoints; index++)
            if (_pointRider[index].IsValid) yield return _pointRider[index];
    }

    /// <summary>
    /// <c>pickAStationForMe</c> of a fire base: the first free station of the model (<c>loadStationGarrisonPoints</c>, up to <c>ContainMax</c>), where the rider then stands
    /// (<c>positionObjectsAtStationGarrisonPoints</c>). A rider that finds none stays where it came in.
    /// </summary>
    private void PickStation(IWorldServices world, Entity rider)
    {
        var stations = FirePointsOf(world)?.Stations ?? [];
        if (_stationRider.Length != stations.Length) Array.Resize(ref _stationRider, stations.Length);
        for (var index = 0; index < stations.Length; index++)
        {
            if (_stationRider[index].IsValid) continue;
            _stationRider[index] = rider.Id;
            (rider.X, rider.Y, rider.Z) = Place(stations[index]);
            return;
        }
    }

    /// <summary><c>removeObjectFromStationPoint</c>, and the rider is put on the ground again ("Patch 1.01").</summary>
    private void LeaveStation(IWorldServices world, Entity rider)
    {
        for (var index = 0; index < _stationRider.Length; index++)
            if (_stationRider[index] == rider.Id) _stationRider[index] = EntityId.Invalid;
        rider.Z = world.GroundHeightAt(rider.X, rider.Y);
    }

    // The owner the building had before its first rider came in (recorded only while somebody is inside).
    private PlayerId _originalOwner;
    private bool _hasOriginalOwner;

    /// <summary>
    /// <c>GarrisonContain::recalcApparentControllingPlayer</c> (F-61): a garrisoned building belongs to its first rider's player (<c>setTeam</c> of that player's default
    /// team) and goes back to the owner it had before when the last one is gone (<c>onRemoving</c>: <c>m_originalTeam</c>). Not modelled: <c>OBJECT_STATUS_CAN_ATTACK</c>
    /// (riders fire from no container here yet, F-60) and the hidden state of a stealth garrison (F-54).
    /// </summary>
    private void RecalcControllingPlayer(IWorldServices world)
    {
        if (!_hasOriginalOwner)
        {
            _originalOwner = Entity.Owner;
            _hasOriginalOwner = true;
        }
        if (Riders.Count > 0)
        {
            if (world.FindEntity(Riders[0]) is { } first) world.TransferOwnership(Entity.Id, first.Owner);
            return;
        }
        world.TransferOwnership(Entity.Id, _originalOwner);
        _hasOriginalOwner = false;
        world.MarkDirty(Entity);
    }

    /// <summary><c>GarrisonContain::m_evacDisposition</c> (<c>NAMED_SET_EVAC_LEFT_OR_RIGHT</c>): 1 = leave to the left, 2 = to the right, anything else bursts from the centre.</summary>
    public int EvacDisposition { get; set; }

    public override void Remove(IWorldServices world, Entity candidate, bool positionAtContainer)
    {
        // onRemoving: off its fire point (back to the centre) or its station (onto the ground).
        if (data.IsEnclosingContainer) RemoveFromPoint(world, candidate);
        else LeaveStation(world, candidate);
        base.Remove(world, candidate, positionAtContainer);
        // onRemoving: "give the object back a regular weapon".
        candidate.WeaponBonusConditions &= ~GarrisonedBonus;
        _containedSinceTick.Remove(candidate.Id.Value);
        if (positionAtContainer && EvacDisposition is 1 or 2) LeaveToTheSide(world, candidate);
        RecalcControllingPlayer(world);
    }

    /// <summary>
    /// <c>GarrisonContain::exitObjectViaDoor</c> for an occupant that is told to leave a living building (<c>aiExit</c>). With the default disposition
    /// (<c>EVAC_BURST_FROM_CENTER</c>) the occupant stands on the building's position, faces the way the building faces and walks to the nearest place it can stand on
    /// (<c>Pathfinder::adjustToPossibleDestination</c> of that very position), ignoring the building: nobody stays inside the walls, and no two occupants take the same place.
    /// "Garrison doesn't have reserveDoor or exitDelay": all leave in the same frame. The side exits are <see cref="LeaveToTheSide"/>, called by <see cref="Remove"/>. Not
    /// modeled: the search for valid terrain in front of and behind a bunker that stands in a cliff.
    /// </summary>
    public bool ExitViaDoor(IWorldServices world, Entity rider)
    {
        if (Entity.EffectivelyDead || !Riders.Contains(rider.Id)) return false;
        Remove(world, rider, true);
        world.Emit(SimEventKind.EntityUncontained, rider.Id, Entity.Id, string.Empty, 0);
        if (EvacDisposition is 1 or 2) return true;
        rider.Angle = Entity.Angle;
        world.MarkDirty(rider);
        if (CrateModules.AiOf(rider) is not { } ai) return true;
        var endX = rider.X;
        var endY = rider.Y;
        world.FindPossibleDestination(rider, ref endX, ref endY);
        ai.OrderExitContainer(world, Entity, endX, endY, false);
        return true;
    }

    /// <summary>
    /// <c>GarrisonContain::exitObjectViaDoor</c> (<c>EVAC_TO_LEFT</c>/<c>RIGHT</c>): the occupant appears in a pocket beside the building (a random point of the doorway, a quarter of the length along it,
    /// half to twice the width to the side) and walks to a spread-out point ten widths away, both rotated into the building's frame.
    /// </summary>
    private void LeaveToTheSide(IWorldServices world, Entity candidate)
    {
        var scalar = EvacDisposition == 1 ? Fixed.One : -Fixed.One;
        var length = Entity.Template.GeometryMajorRadius;
        var width = Entity.Template.GeometryMinorRadius;
        var doorX = world.LogicRandom.Range(-length / Fixed.FromInt(4), length / Fixed.FromInt(4));
        var doorY = world.LogicRandom.Range(width / Fixed.FromInt(2), width * Fixed.FromInt(2)) * scalar;
        var walkX = world.LogicRandom.Range(-length, length);
        var walkY = width * Fixed.FromInt(10) * scalar;
        var cos = FixedTrig.Cos(Entity.Angle);
        var sin = FixedTrig.Sin(Entity.Angle);
        candidate.X = Entity.X + doorX * cos - doorY * sin;
        candidate.Y = Entity.Y + doorX * sin + doorY * cos;
        candidate.Angle = Entity.Angle;
        if (CrateModules.AiOf(candidate) is { } ai) ai.OrderMove(world, Entity.X + walkX * cos - walkY * sin, Entity.Y + walkX * sin + walkY * cos, false);
    }

    public void OnBodyDamageStateChange(IWorldServices world, in DamageInfo info, int oldState, int newState)
    {
        _ = info;
        _ = oldState;
        // Edge-triggered (the dispatcher only calls this when the state actually changed): entering REALLYDAMAGED
        // ejects everyone, unless the building is meant to stay garrisonable until destroyed.
        if (newState == BodyDamageStates.ReallyDamaged && !Entity.Template.IsKindOf("GARRISONABLE_UNTIL_DESTROYED") && ContainCount > 0)
            EjectAll(world);
    }

    public override long Update(IWorldServices world)
    {
        var before = Riders.Count;
        PruneDead(world);
        if (Riders.Count != before) RecalcControllingPlayer(world);
        // matchObjectsToGarrisonPoints, once per logic frame of the original, in the first tick of the building's own frame (the rider's acquisition decides there too).
        var ticks = world.Time.TicksPerOriginalFrame;
        if (data.IsEnclosingContainer && Riders.Count > 0 && (ticks == TickOrder.Next || (world.Tick - Entity.SpawnTick) % ticks == 0)) world.MatchGarrisonPoints(Entity, this);
        if (data.HealObjects)
            foreach (var riderId in Riders.ToArray())
            {
                var rider = world.FindEntity(riderId);
                if (rider?.Body is not null) HealOne(world, rider);
            }
        return data.HealObjects || (data.IsEnclosingContainer && Riders.Count > 0) ? SimulationTiming.SleepNone : SimulationTiming.SleepForever;
    }

    /// <summary>
    /// <c>GarrisonContain::healSingleObject</c>: a sliver of <c>MaxHealth / TimeForFullHeal</c> per logic frame of the original, or a hard snap to full once the
    /// occupant has been inside long enough. The sliver is the original's (the duration in frames) and is given once per frame of the original, counted from the
    /// tick the occupant came in: its health is that of 30 Hz at every frame boundary. 30 Hz: every tick.
    /// </summary>
    private void HealOne(IWorldServices world, Entity rider)
    {
        var since = _containedSinceTick.GetValueOrDefault(rider.Id.Value, world.Tick);
        var ticks = world.Time.TicksPerOriginalFrame;
        if (ticks != TickOrder.Next && (world.Tick - since) % ticks != 0) return;
        var full = data.TimeForFullHealTicks > 0 && world.Tick - since >= data.TimeForFullHealTicks;
        var amount = full || data.TimeForFullHealTicks <= 0 ? rider.Body!.MaxHealth : world.Time.SharePerTick(rider.Body!.MaxHealth, Fixed.FromInt(world.Time.ToOriginalFrames(data.TimeForFullHealTicks)));
        world.Damage(rider.Id, new DamageInfo(Entity.Id, amount, DamageTypes.Healing, DeathTypes.None, false));
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", NextWake);
        if (EvacDisposition != 0) writer.Int("evacDisposition", EvacDisposition);
        if (_hasOriginalOwner) writer.Int("originalOwner", _originalOwner.Value);
        writer.BeginArray("contained");
        foreach (var riderId in Riders)
        {
            writer.BeginObject(null);
            writer.UInt("id", riderId.Value);
            writer.Int("since", _containedSinceTick.GetValueOrDefault(riderId.Value));
            writer.EndObject();
        }
        writer.EndArray();
        // Written only while a rider stands at a point or a station: a building without such riders keeps its state.
        if (_pointsInUse > 0)
        {
            writer.BeginArray("points");
            for (var index = 0; index < ContainerFirePoints.MaxGarrisonPoints; index++)
            {
                if (!_pointRider[index].IsValid) continue;
                writer.BeginObject(null);
                writer.Int("index", index);
                writer.UInt("rider", _pointRider[index].Value);
                writer.UInt("target", _pointTarget[index].Value);
                writer.EndObject();
            }
            writer.EndArray();
        }
        if (_stationRider.Any(rider => rider.IsValid))
        {
            writer.BeginArray("stations");
            foreach (var rider in _stationRider)
            {
                writer.BeginObject(null);
                writer.UInt("rider", rider.Value);
                writer.EndObject();
            }
            writer.EndArray();
        }
    }

    public override void Load(IStateReader reader)
    {
        NextWake = reader.Int("nextWake");
        EvacDisposition = reader.Has("evacDisposition") ? checked((int)reader.Int("evacDisposition")) : 0;
        _hasOriginalOwner = reader.Has("originalOwner");
        _originalOwner = _hasOriginalOwner ? new PlayerId(checked((int)reader.Int("originalOwner"))) : default;
        Riders.Clear();
        _containedSinceTick.Clear();
        foreach (var entry in reader.Array("contained"))
        {
            var id = new EntityId(checked((uint)entry.UInt("id")));
            Riders.Add(id);
            _containedSinceTick[id.Value] = entry.Int("since");
        }
        Array.Clear(_pointRider);
        Array.Clear(_pointTarget);
        _pointsInUse = 0;
        if (reader.Has("points"))
            foreach (var entry in reader.Array("points"))
            {
                var index = checked((int)entry.Int("index"));
                _pointRider[index] = new EntityId(checked((uint)entry.UInt("rider")));
                _pointTarget[index] = new EntityId(checked((uint)entry.UInt("target")));
                _pointsInUse++;
            }
        _stationRider = reader.Has("stations") ? [.. reader.Array("stations").Select(entry => new EntityId(checked((uint)entry.UInt("rider"))))] : [];
    }
}

// =================================================================================================== TransportContain

/// <summary>
/// <c>OpenContainModuleData</c> plus <c>TransportContainModuleData</c>'s two mechanically relevant fields: the INI key
/// is literally <c>Slots</c> (not <c>ContainMax</c> — the inherited <c>ContainMax</c> field is parseable but unused,
/// since <c>TransportContain::getContainMax</c> only ever reads <c>m_slotCapacity</c>), and <c>HealthRegen%PerSec</c>
/// (a raw percent-per-second real, not <c>FieldType.Percent</c> — the reference divides by 100 itself in the formula).
/// </summary>
public sealed record TransportContainData(OpenContainData Base, int Slots, Fixed HealthRegenPercentPerSecond) : ModuleData
{
    /// <summary><c>DestroyRidersWhoAreNotFreeToExit</c> (default no): a rider that may not leave the dying transport is destroyed instead of killed.</summary>
    public bool DestroyRidersWhoAreNotFreeToExit { get; init; }

    /// <summary><c>OpenContainModuleData::m_numberOfExitPaths</c> (default 1): 0 lets a rider out on the container's own position, 1 uses the bones <c>ExitStart</c>/<c>ExitEnd</c>, more cycle through <c>ExitStart01</c>..<c>ExitEndNN</c>.</summary>
    public int NumberOfExitPaths { get; init; } = 1;

    /// <summary><c>TransportContainModuleData::m_exitDelay</c> in ticks (default 0): after a rider has left the door is busy that long (<c>isExitBusy</c>).</summary>
    public long ExitDelayTicks { get; init; }

    /// <summary>
    /// <c>ScatterNearbyOnExit</c> (default yes). Typed and never read, as in the original: <c>TransportContain::onRemoving</c> scatters the riders of a transport that is
    /// effectively dead, whatever this key says (only <c>MobNexusContain</c> consults its own key of this name).
    /// </summary>
    public bool ScatterNearbyOnExit { get; init; } = true;

    /// <summary><c>PassengersAllowedToFire</c> (default no, <c>OpenContainModuleData</c>): the riders may fire from inside (F-60; a Humvee, a Battle Bus, a Listening Outpost, the Spectre).</summary>
    public bool PassengersAllowedToFire { get; init; }

    /// <summary><c>WeaponBonusPassedToPassengers</c> (default no, <c>OpenContainModuleData</c>): the transport's own weapon bonus conditions are its riders' too (F-60; the Battle Bus).</summary>
    public bool WeaponBonusPassedToPassengers { get; init; }

    public static TransportContainData Parse(RatedModuleConfiguration configuration) => new(
        OpenContainData.Parse(configuration, ["INFANTRY"]),
        checked((int)ModuleConfigReader.Int(configuration, "Slots", 0)),
        ModuleConfigReader.Real(configuration, "HealthRegen%PerSec", Fixed.Zero))
    {
        NumberOfExitPaths = checked((int)ModuleConfigReader.Int(configuration, "NumberOfExitPaths", 1)),
        ExitDelayTicks = ModuleConfigReader.Ticks(configuration, "ExitDelay", 0),
        ScatterNearbyOnExit = ModuleConfigReader.Bool(configuration, "ScatterNearbyOnExit", true),
        DestroyRidersWhoAreNotFreeToExit = ModuleConfigReader.Bool(configuration, "DestroyRidersWhoAreNotFreeToExit", false),
        PassengersAllowedToFire = ModuleConfigReader.Bool(configuration, "PassengersAllowedToFire", false),
        WeaponBonusPassedToPassengers = ModuleConfigReader.Bool(configuration, "WeaponBonusPassedToPassengers", false),
    };
}

/// <summary>
/// <c>TransportContain.cpp</c>. Capacity is weighted by <see cref="Templates.ObjectTemplate.TransportSlotCount"/>
/// (<c>rider->getTransportSlotCount()</c>), not a simple occupant count: a candidate with 0 slots is not transportable
/// at all, and the sum of contained occupants' slot costs plus the candidate's own must not exceed
/// <see cref="TransportContainData.Slots"/>. <c>HealthRegen%PerSec</c> heals every occupant continuously (unlike
/// <c>GarrisonContain</c>'s ramp-to-full-over-a-fixed-duration): <c>MaxHealth * percent / 100</c> per second, applied
/// as a per-tick sliver. Deliberately not modeled: <c>InitialPayload</c> (same reason as <c>GarrisonContain</c>'s
/// <c>InitialRoster</c>), firing from inside, exit paths/bones/pitch/delay/scatter (all presentation or need real
/// movement/positioning), capture handling (no capture system), <c>ArmedRidersUpgradeMyWeaponSet</c> (no WeaponBonus
/// system at all, project-wide).
/// </summary>
public class TransportContain(TransportContainData data) : ContainModuleBase(data.Base)
{
    /// <summary>Sum of <see cref="Templates.ObjectTemplate.TransportSlotCount"/> of everyone currently aboard, maintained incrementally (<see cref="Add"/>/<see cref="Remove"/>) so capacity checks never need to resolve <see cref="ContainModuleBase.Riders"/> back to entities.</summary>
    private int _usedSlots;

    public override string ModuleName => "TransportContain";
    public override int ContainMax => data.Slots;

    /// <inheritdoc/>
    protected override bool PassengersFireByData => data.PassengersAllowedToFire;

    /// <summary>
    /// <c>TransportContain::isPassengerAllowedToFire</c>: only infantry fires ("no vehicles"); a transport that rides an Overlord-style container (the battle bunker on an Overlord)
    /// lets that container decide with the rider; otherwise the flag (<c>OpenContain::isPassengerAllowedToFire()</c>, without the id).
    /// </summary>
    /// <inheritdoc cref="IContainerModule.PassesWeaponBonus"/>
    public override bool PassesWeaponBonus => data.WeaponBonusPassedToPassengers;

    public override bool IsPassengerAllowedToFire(IWorldServices world, Entity? rider)
    {
        if (rider is not null && !rider.Template.IsKindOf("INFANTRY")) return false;
        if (Entity.ContainedBy.IsValid && world.FindEntity(Entity.ContainedBy)?.Container is { IsSpecialOverlordStyleContainer: true } outer) return outer.IsPassengerAllowedToFire(world, rider);
        return base.IsPassengerAllowedToFire(world, null);
    }

    public override void OnSpawn(IWorldServices world)
        => NextWake = data.HealthRegenPercentPerSecond > Fixed.Zero ? world.Tick + SimulationTiming.SleepNone : SimulationTiming.SleepForever;

    public override bool IsValidContainerFor(IWorldServices world, Entity candidate, bool checkCapacity)
    {
        if (Entity.EffectivelyDead || candidate.Template.TransportSlotCount <= 0 || !PassesKindOfAndRelationship(world, candidate)) return false;
        return !checkCapacity || _usedSlots + candidate.Template.TransportSlotCount <= data.Slots;
    }

    public override void Add(IWorldServices world, Entity candidate)
    {
        base.Add(world, candidate);
        _usedSlots += candidate.Template.TransportSlotCount;
        var phase = (int)(world.Tick - world.Time.OriginalFrames(world.Time.FramesIn(world.Tick)));
        if (phase != 0) EnterPhases[candidate.Id.Value] = phase;
    }

    /// <summary>
    /// <c>TransportContain::killRidersWhoAreNotFreeToExit</c> (F-51): before the riders are put out of the dying transport, every one that may not leave
    /// (<see cref="IsSpecificRiderFreeToExit"/>) is killed - or, with <c>DestroyRidersWhoAreNotFreeToExit</c>, destroyed with what it carries (a payload plane's cargo).
    /// </summary>
    protected override void KillRidersWhoAreNotFreeToExit(IWorldServices world)
    {
        foreach (var riderId in Riders.ToArray())
        {
            if (world.FindEntity(riderId) is not { } rider || IsSpecificRiderFreeToExit(world, rider)) continue;
            if (data.DestroyRidersWhoAreNotFreeToExit)
            {
                Remove(world, rider, false);
                DestroyWithContents(world, rider);
            }
            else world.Kill(rider.Id);
        }
    }

    /// <summary>
    /// <c>TransportContain::isSpecificRiderFreeToExit</c>: the transport's AI must let the rider out (<c>getAiFreeToExit</c>: a Chinook only when landed, a payload plane only with its
    /// doors open and alive); a transport on an airborne locomotor may always put riders out; otherwise the rider needs an AI with a locomotor whose surfaces fit the pathfinding
    /// cell under the transport (<c>Pathfinder::validMovementTerrain</c>: an obstacle or impassable cell counts as fitting, a cliff or water cell needs that surface).
    /// </summary>
    private bool IsSpecificRiderFreeToExit(IWorldServices world, Entity rider)
    {
        if (CrateModules.AiOf(Entity) is { } ai && !ai.AiFreeToExit(world, rider)) return false;
        if (CrateModules.UsesAirborneLocomotor(Entity)) return true;
        if (CrateModules.AiOf(rider) is null || rider.Template.Locomotor is not { } locomotor) return false;
        if (world.Navigation?.Terrain is not { } terrain) return true;
        var (cx, cy) = (Terrain.TerrainMap.CellOf(Entity.X), Terrain.TerrainMap.CellOf(Entity.Y));
        if (!terrain.IsInside(cx, cy)) return false;
        return world.Navigation.IsBlockedByObstacle(cx, cy) || Terrain.NavigationGrid.SurfaceAllows(terrain.CellAt(cx, cy), locomotor.Surfaces);
    }

    private static void DestroyWithContents(IWorldServices world, Entity entity)
    {
        if (entity.Container is { } inside)
            foreach (var id in inside.Contained.ToArray())
                if (world.FindEntity(id) is { } passenger) DestroyWithContents(world, passenger);
        world.RequestDestroy(entity.Id);
    }

    public override void Remove(IWorldServices world, Entity candidate, bool positionAtContainer)
    {
        base.Remove(world, candidate, positionAtContainer);
        _usedSlots -= candidate.Template.TransportSlotCount;
        EnterPhases.Remove(candidate.Id.Value);
        _exitWanted.Remove(candidate.Id);
        // TransportContain::onRemoving: "m_frameExitNotBusy = TheGameLogic->getFrame() + d->m_exitDelay", for every rider that leaves, however it leaves.
        _exitNotBusyTick = world.Tick + data.ExitDelayTicks;
        // The same function: "if (getObject()->isEffectivelyDead()) scatterToNearbyPosition(rider)" - the riders of a destroyed transport, and only they, are scattered.
        if (positionAtContainer && Entity.EffectivelyDead) ScatterToNearbyPosition(world, candidate);
    }

    // The riders that were told to leave and wait for the door (AIExitState of each of them), the tick from which the door is free again (TransportContain::m_frameExitNotBusy),
    // and the exit path the next rider takes (OpenContain::m_whichExitPath, 1-based). Written only where they differ from an idle transport's values.
    private readonly List<EntityId> _exitWanted = [];
    private long _exitNotBusyTick;
    private int _whichExitPath = 1;

    /// <summary>A rider waits for the door, or the door is still busy after the last exit (<c>TransportContain::isExitBusy</c>).</summary>
    public bool IsExitBusy(long tick) => tick < _exitNotBusyTick;

    /// <summary>
    /// <c>aiExit</c> of a rider of a living transport (<c>AIExitState</c>, AIStates.cpp): the rider waits until the door is free (<c>isExitBusy</c>) and then leaves through
    /// <c>OpenContain::exitObjectViaDoor</c>. The riders poll in the order their updates run, so of several waiting riders the one with the lowest id goes first. A dead
    /// transport has no door (false): its riders are removed in one step and scattered (<see cref="Remove"/>).
    /// </summary>
    public virtual bool ExitViaDoor(IWorldServices world, Entity rider)
    {
        if (Entity.EffectivelyDead || !Riders.Contains(rider.Id)) return false;
        if (!_exitWanted.Contains(rider.Id)) _exitWanted.Add(rider.Id);
        world.MarkDirty(Entity);
        ReleaseThroughDoor(world);
        return true;
    }

    private void ReleaseThroughDoor(IWorldServices world)
    {
        while (_exitWanted.Count > 0 && world.Tick >= _exitNotBusyTick)
        {
            var next = _exitWanted[0];
            foreach (var id in _exitWanted)
                if (id.Value < next.Value) next = id;
            _exitWanted.Remove(next);
            if (world.FindEntity(next) is not { EffectivelyDead: false, DestroyRequested: false } rider || !Riders.Contains(next)) continue;
            ExitObjectViaDoor(world, rider);
        }
        if (_exitWanted.Count > 0) world.WakeAt(Entity, this, _exitNotBusyTick);
    }

    private void ExitObjectViaDoor(IWorldServices world, Entity rider) => ExitObjectViaDoor(world, rider, data.NumberOfExitPaths, ref _whichExitPath);

    /// <summary>
    /// <c>OpenContain::scatterToNearbyPosition</c> (OpenContain.cpp): a random angle, then a random distance between one and one and a half bounding-circle radii of the
    /// container (two draws of the logic stream, in this order, for every rider); the rider faces that way, stands on the container's centre and walks to the point, ignoring
    /// the container. A rider without AI is put on the point. A rider that is dead keeps the draws and the position, and walks nowhere.
    /// </summary>
    private void ScatterToNearbyPosition(IWorldServices world, Entity rider)
    {
        var angle = world.LogicRandom.Range(Fixed.Zero, FixedTrig.TwoPi);
        var minRadius = Entity.Template.BoundingCircleRadius;
        var distance = world.LogicRandom.Range(minRadius, minRadius + minRadius / Fixed.FromInt(2));
        var (centreX, centreY, _) = Entity.FrameEnd(world.Tick);
        var x = centreX + distance * FixedTrig.Cos(angle);
        var y = centreY + distance * FixedTrig.Sin(angle);
        rider.Angle = FixedTrig.Normalize(angle);
        world.MarkDirty(rider);
        if (rider.EffectivelyDead) return;
        if (CrateModules.AiOf(rider) is { } ai && rider.Template.Locomotor is not null) ai.OrderExitContainer(world, Entity, x, y, false);
        else
        {
            rider.X = x;
            rider.Y = y;
        }
    }

    public override long Update(IWorldServices world)
    {
        PruneDead(world);
        var sleep = Regenerate(world);
        if (_exitWanted.Count == 0) return sleep;
        ReleaseThroughDoor(world);
        return _exitWanted.Count == 0 ? sleep : Math.Min(sleep, _exitNotBusyTick - world.Tick);
    }

    private long Regenerate(IWorldServices world)
    {
        if (data.HealthRegenPercentPerSecond > Fixed.Zero)
            foreach (var riderId in Riders.ToArray())
            {
                var rider = world.FindEntity(riderId);
                if (rider?.Body is null || rider.Body.Health >= rider.Body.MaxHealth) continue;
                // The original's slice (the regeneration of one second over its 30 logic frames), once per logic frame of the original counted from the tick the rider
                // came in - like GarrisonContain and the tunnel network: slices per tick are smaller numbers that do not add up to the slice of a frame, so the rider
                // would be whole a tick earlier and be healed in one frame less. 30 Hz: every tick, the value it always had.
                var phase = (int)(world.Tick - world.Time.OriginalFrames(world.Time.FramesIn(world.Tick)));
                if (phase != EnterPhases.GetValueOrDefault(riderId.Value)) continue;
                var perSecond = rider.Body.MaxHealth * data.HealthRegenPercentPerSecond / Fixed.FromInt(100);
                var perFrame = SimulationRate.Original.Time.PerSecondToPerTick(perSecond);
                world.Damage(rider.Id, new DamageInfo(Entity.Id, perFrame, DamageTypes.Healing, DeathTypes.None, false));
            }
        return data.HealthRegenPercentPerSecond > Fixed.Zero ? SimulationTiming.SleepNone : SimulationTiming.SleepForever;
    }

    public override void Describe(IStateWriter writer)
    {
        DescribeContained(writer);
        writer.Int("usedSlots", _usedSlots);
        if (_exitNotBusyTick != 0) writer.Int("exitNotBusy", _exitNotBusyTick);
        if (_whichExitPath != 1) writer.Int("whichExitPath", _whichExitPath);
        if (_exitWanted.Count == 0) return;
        writer.BeginArray("exitWanted");
        foreach (var id in _exitWanted)
        {
            writer.BeginObject(null);
            writer.UInt("id", id.Value);
            writer.EndObject();
        }
        writer.EndArray();
    }

    public override void Load(IStateReader reader)
    {
        LoadContained(reader);
        _usedSlots = checked((int)reader.Int("usedSlots"));
        _exitNotBusyTick = reader.Has("exitNotBusy") ? reader.Int("exitNotBusy") : 0;
        _whichExitPath = reader.Has("whichExitPath") ? checked((int)reader.Int("whichExitPath")) : 1;
        _exitWanted.Clear();
        if (!reader.Has("exitWanted")) return;
        foreach (var entry in reader.Array("exitWanted")) _exitWanted.Add(new EntityId(checked((uint)entry.UInt("id"))));
    }
}
