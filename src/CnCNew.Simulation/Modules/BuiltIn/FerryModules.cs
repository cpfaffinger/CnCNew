using CnCNew.Data.Registry;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

// The ferry (<c>CivilianVehicleFerry</c>, the only user of the three modules): a boat that follows pairs of map waypoints (<c>&lt;prefix&gt;Start01</c>/<c>&lt;prefix&gt;End01</c>, ...), whose
// dock is open only while it stands at the end of a path, and whose riders are pulled in and pushed out by the dock instead of driving. The reference sources are
// RailedTransportAIUpdate.cpp, RailedTransportContain.cpp and RailedTransportDockUpdate.cpp (names after the reference's old rail transport; nothing here runs on rails).

// ============================================================================================================ RailedTransportAIUpdate

/// <summary><c>RailedTransportAIUpdateModuleData</c>: the <c>AIUpdate</c> base plus <c>PathPrefixName</c> (default none: the paths are looked up as <c>Start01</c>/<c>End01</c>).</summary>
public sealed record RailedTransportAiData(AIUpdateData Base, string PathPrefix) : ModuleData
{
    public static RailedTransportAiData Parse(ModuleConfiguration configuration) => new(AIUpdateData.Parse(configuration), ModuleConfigReader.Text(configuration, "PathPrefixName"));
}

/// <summary>
/// <c>RailedTransportAIUpdate.cpp</c>: an AI that only ever sails along the map's waypoint paths. On its first update it finds the paths (<c>&lt;prefix&gt;Start%02d</c> and
/// <c>&lt;prefix&gt;End%02d</c> for 01..32, a path needs both), sails to the end waypoint of the one closest to it (<c>pickAndMoveToInitialLocation</c>) and is "in transit" while it
/// does; the dock is closed while in transit and opens when the ferry is within 5 units of its end waypoint or idle. <see cref="ExecuteRailedTransport"/>
/// (<c>privateExecuteRailedTransport</c>) picks the next path round-robin and follows it from its start waypoint to its end, unless the dock is loading or unloading. An evacuation
/// (<see cref="OrderEvacuate"/>) unloads everybody through the dock — not while in transit or while the dock is busy. Orders from the player other than the evacuation are ignored
/// (<c>aiDoCommand</c>: "we ignore all commands from the player except the one to start a transit and to unload"). A waypoint path is followed hop by hop: an internal move to each
/// waypoint, then along its first link to the next, until a waypoint has no link. Not modeled: the ultra-accurate locomotor flag (the boat's steering has no such mode here) and
/// the player command <c>EXECUTE_RAILED_TRANSPORT</c> — the simulation has no command type for it (a new command would change the replay format), so only <see cref="ExecuteRailedTransport"/>
/// starts a sailing; the map data contains no ferry, so no shipped map is affected.
/// </summary>
public sealed class RailedTransportAIUpdate(RailedTransportAiData data) : AIUpdateInterface(data.Base)
{
    private const int MaxWaypointPaths = 32;
    private const int InvalidPath = -1;
    private static readonly Fixed ArrivalDistance = Fixed.FromInt(5);

    private bool _inTransit;
    private int _currentPath = InvalidPath;
    private bool _waypointDataLoaded;
    private int _followWaypoint = -1;
    private (int Start, int End)[]? _paths;

    public override string ModuleName => "RailedTransportAIUpdate";

    /// <summary><c>m_inTransit</c>.</summary>
    public bool InTransit => _inTransit;

    /// <summary><c>m_currentPath</c> (-1 before the first path was picked).</summary>
    public int CurrentPath => _currentPath;

    /// <summary><c>m_numPaths</c>; 0 until the waypoint data is loaded.</summary>
    public int PathCount => _paths?.Length ?? 0;

    /// <summary><c>isDockOpen</c> of the ferry's own dock (false while it is in transit).</summary>
    public bool DockIsOpen => DockModules.DockOf(Entity)?.IsOpen ?? false;

    /// <summary><c>isLoadingOrUnloading</c> of the ferry's own dock: a rider is being pulled in or pushed out.</summary>
    public bool DockIsLoadingOrUnloading => Entity.ModuleArray.OfType<RailedTransportDockUpdate>().FirstOrDefault()?.IsLoadingOrUnloading ?? false;

    public override void OnSpawn(IWorldServices world)
    {
        base.OnSpawn(world);
        NextWake = world.Tick + SimulationTiming.SleepNone;
    }

    /// <summary><c>loadWaypointData</c>: every complete start/end pair of the prefix, in path order. Deterministic from the terrain, so it is simply redone after a restore.</summary>
    private (int Start, int End)[] EnsurePaths(IWorldServices world)
    {
        if (_paths is not null) return _paths;
        var found = new List<(int, int)>();
        if (world.Terrain is { } terrain)
            for (var index = 0; index < MaxWaypointPaths; index++)
            {
                var start = terrain.FindWaypoint($"{data.PathPrefix}Start{index + 1:00}");
                var end = terrain.FindWaypoint($"{data.PathPrefix}End{index + 1:00}");
                if (start is not null && end is not null) found.Add((start.Id, end.Id));
            }
        _paths = [..found];
        _waypointDataLoaded = true;
        return _paths;
    }

    private static TerrainWaypoint? WaypointById(IWorldServices world, int id)
    {
        if (world.Terrain is not { } terrain || id < 0) return null;
        foreach (var waypoint in terrain.Waypoints)
            if (waypoint.Id == id) return waypoint;
        return null;
    }

    public override long Update(IWorldServices world)
    {
        var paths = EnsurePaths(world);
        base.Update(world);
        FollowPath(world);
        // "if we have no current path selected pick one and move to the end waypoint of that path"
        if (_currentPath == InvalidPath && paths.Length > 0) PickAndMoveToInitialLocation(world, paths);
        if (_inTransit)
        {
            var end = _currentPath >= 0 && _currentPath < paths.Length ? WaypointById(world, paths[_currentPath].End) : null;
            var arrived = end is not null && Distance(Entity, end) <= ArrivalDistance;
            if (arrived || IsIdle) SetInTransit(world, false);
        }
        return SimulationTiming.SleepNone;
    }

    private static Fixed Distance(Entity entity, TerrainWaypoint waypoint)
    {
        var dx = waypoint.X - entity.X;
        var dy = waypoint.Y - entity.Y;
        var dz = waypoint.Z - entity.Z;
        return Fixed.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary><c>pickAndMoveToInitialLocation</c>: the path whose end waypoint is closest (the first of equals), sailed to and followed from that end waypoint.</summary>
    private void PickAndMoveToInitialLocation(IWorldServices world, (int Start, int End)[] paths)
    {
        var best = InvalidPath;
        TerrainWaypoint? closest = null;
        var closestDistance = Fixed.Zero;
        for (var index = 0; index < paths.Length; index++)
        {
            if (WaypointById(world, paths[index].End) is not { } waypoint) continue;
            var distance = Distance(Entity, waypoint);
            if (best != InvalidPath && distance >= closestDistance) continue;
            best = index;
            closest = waypoint;
            closestDistance = distance;
        }
        if (closest is null) return;
        BeginFollow(world, closest);
        _currentPath = best;
        SetInTransit(world, true);
    }

    /// <summary><c>aiFollowWaypointPath</c>: sail to the waypoint, then along its links.</summary>
    private void BeginFollow(IWorldServices world, TerrainWaypoint waypoint)
    {
        _followWaypoint = waypoint.Id;
        StartInternalMove(waypoint.X, waypoint.Y);
        world.MarkDirty(Entity);
    }

    /// <summary>When the move to a waypoint has ended, goes on to the waypoint its first link leads to; a waypoint without a link ends the path.</summary>
    private void FollowPath(IWorldServices world)
    {
        if (_followWaypoint < 0 || Entity.HasMoveTarget) return;
        var reached = WaypointById(world, _followWaypoint);
        var next = reached is not null && world.Terrain is { } terrain ? terrain.NextWaypoint(reached) : null;
        if (next is null)
        {
            _followWaypoint = -1;
            world.MarkDirty(Entity);
            return;
        }
        BeginFollow(world, next);
    }

    /// <summary><c>setInTransit</c>: "open up the dock" while not in transit.</summary>
    private void SetInTransit(IWorldServices world, bool inTransit)
    {
        DockModules.DockOf(Entity)?.SetOpen(!inTransit);
        _inTransit = inTransit;
        world.MarkDirty(Entity);
    }

    /// <summary>
    /// <c>privateExecuteRailedTransport</c>: the next path (round-robin) is followed from its start waypoint; nothing happens while the dock is loading or unloading, or when the map has no
    /// path for this ferry.
    /// </summary>
    public void ExecuteRailedTransport(IWorldServices world)
    {
        if (Entity.ModuleArray.OfType<RailedTransportDockUpdate>().FirstOrDefault() is not { IsLoadingOrUnloading: false }) return;
        var paths = EnsurePaths(world);
        if (paths.Length == 0 || WaypointForExecute(world, paths) is not { } start) return;
        BeginFollow(world, start);
        SetInTransit(world, true);
    }

    private TerrainWaypoint? WaypointForExecute(IWorldServices world, (int Start, int End)[] paths)
    {
        if (++_currentPath >= paths.Length) _currentPath = 0;
        return WaypointById(world, paths[_currentPath].Start);
    }

    /// <summary><c>privateEvacuate</c>: unloads every rider through the dock, unless the ferry is in transit or the dock is busy. The position of the order is ignored, as in the reference.</summary>
    public override void OrderEvacuate(IWorldServices world, bool move, Fixed x, Fixed y, bool thenExit = false)
    {
        if (Entity.ModuleArray.OfType<RailedTransportDockUpdate>().FirstOrDefault() is not { } dock || _inTransit || dock.IsLoadingOrUnloading) return;
        dock.UnloadAll(world);
    }

    /// <summary>The player cannot order the ferry about; the AI and scripts can.</summary>
    public override void OrderMove(IWorldServices world, Fixed x, Fixed y, bool fromPlayer, bool attackMove = false)
    {
        if (fromPlayer) return;
        _followWaypoint = -1;
        base.OrderMove(world, x, y, fromPlayer, attackMove);
    }

    public override void OrderEnter(IWorldServices world, Entity container, bool fromPlayer)
    {
        if (fromPlayer) return;
        _followWaypoint = -1;
        base.OrderEnter(world, container, fromPlayer);
    }

    public override void OrderDock(IWorldServices world, Entity dock, bool fromPlayer)
    {
        if (fromPlayer) return;
        _followWaypoint = -1;
        base.OrderDock(world, dock, fromPlayer);
    }

    public override void OrderIdle(IWorldServices world)
    {
        _followWaypoint = -1;
        base.OrderIdle(world);
    }

    public override void Describe(IStateWriter writer)
    {
        base.Describe(writer);
        if (_inTransit) writer.Bool("inTransit", true);
        if (_currentPath != InvalidPath) writer.Int("currentPath", _currentPath);
        if (_waypointDataLoaded) writer.Bool("waypointDataLoaded", true);
        if (_followWaypoint >= 0) writer.Int("followWaypoint", _followWaypoint);
    }

    public override void Load(IStateReader reader)
    {
        base.Load(reader);
        _inTransit = reader.Has("inTransit") && reader.Bool("inTransit");
        _currentPath = reader.Has("currentPath") ? checked((int)reader.Int("currentPath")) : InvalidPath;
        _waypointDataLoaded = reader.Has("waypointDataLoaded") && reader.Bool("waypointDataLoaded");
        _followWaypoint = reader.Has("followWaypoint") ? checked((int)reader.Int("followWaypoint")) : -1;
        _paths = null;
    }
}

// ============================================================================================================ RailedTransportContain

/// <summary>
/// <c>RailedTransportContain.cpp</c>: a <see cref="TransportContain"/> whose riders leave through the ferry's dock. When the last rider has been removed the dock is open for more
/// passengers (<c>onRemoving</c>); a rider may only leave while the dock is open (<c>isSpecificRiderFreeToExit</c>, so nobody steps off mid-sea), and an exit command does not remove the
/// rider on the spot but asks the dock to unload (<c>exitObjectViaDoor</c> -> <c>unloadSingleObject</c>), which unloads the first rider in the list, not necessarily the one that was named
/// (as in the reference). A world-level evacuation (a script, the death of the ferry) still removes the riders in one step. A ferry without a dock module lets its riders out the plain way.
/// </summary>
public sealed class RailedTransportContain(TransportContainData data) : TransportContain(data), IContainerModule
{
    public override string ModuleName => "RailedTransportContain";

    public override void Remove(IWorldServices world, Entity candidate, bool positionAtContainer)
    {
        base.Remove(world, candidate, positionAtContainer);
        if (ContainCount == 0) DockModules.DockOf(Entity)?.SetOpen(true);
    }

    public bool IsRiderFreeToExit(Entity rider) => DockModules.DockOf(Entity) is not { IsOpen: false };

    public bool ExitViaDoor(IWorldServices world, Entity rider)
    {
        if (Entity.ModuleArray.OfType<RailedTransportDockUpdate>().FirstOrDefault() is not { } dock) return false;
        dock.UnloadSingleObject(world);
        return true;
    }
}

// ============================================================================================================ RailedTransportDockUpdate

/// <summary><c>RailedTransportDockUpdateModuleData</c>: the <c>DockUpdate</c> fields plus <c>PullInsideDuration</c> and <c>PushOutsideDuration</c> (ticks, default 0) and <c>ToleranceDistance</c> (default 50).</summary>
public sealed record RailedTransportDockData(int NumberApproachPositions, long PullInsideTicks, long PushOutsideTicks, Fixed ToleranceDistance) : DockData(NumberApproachPositions)
{
    public static RailedTransportDockData Parse(ModuleConfiguration configuration) => new(
        ParseApproachPositions(configuration), ModuleConfigReader.Ticks(configuration, "PullInsideDuration", 0), ModuleConfigReader.Ticks(configuration, "PushOutsideDuration", 0),
        ModuleConfigReader.Real(configuration, "ToleranceDistance", Fixed.FromInt(50)));
}

/// <summary>
/// <c>RailedTransportDockUpdate.cpp</c>: the ferry's dock. A docker that asks for its action while within <c>ToleranceDistance</c> of the ferry is held, made unselectable, turned towards
/// the ferry and then pulled straight towards its centre over <c>PullInsideDuration</c> (the distance covered each tick is the initial distance over the duration) — "typically those
/// objects can only drive on land and have a hard time driving inside the railed transport ... so we fake it!" — until it is within 6 units; then its dock is cancelled, its AI idled and it
/// is put into the ferry (<see cref="IWorldServices.TryEnterContainer"/>; if the ferry refuses it after all, it is released where it stands). Entry is only granted while the ferry
/// has room for the docker. Unloading (<see cref="UnloadAll"/>, <see cref="UnloadSingleObject"/>) takes the first rider out of the ferry, puts it at the ferry facing its way, held, and
/// pushes it to the exit point over <c>PushOutsideDuration</c> until it is within 3 units, then frees it, idles its AI and starts the next one while the unload count lasts. A second
/// single-unload request while a rider is being pushed out is ignored (the reference would abandon that rider, held, on the ferry's deck).
/// <para>Approximations, the reference uses the model's bones: the exit point (<c>DOCKEND</c>) is the ferry's position one major radius ahead of its heading, a rider that arrives there
/// stays where it is instead of walking on to the <c>DOCKWAITING07</c> bone, the step per tick never overshoots the exit point, and a pull-in or push-out duration of 0 is treated as
/// one tick. The "moving" model condition of the pulled rider is presentation.</para>
/// </summary>
internal sealed class RailedTransportDockUpdate(RailedTransportDockData data) : DockUpdateBase(data)
{
    private const int UnloadAllRiders = -1;
    private static readonly Fixed PullArrivalSquared = Fixed.FromInt(36);
    private static readonly Fixed PushArrivalSquared = Fixed.FromInt(9);
    private static readonly int UnselectableBit = ObjectStatusBits.Index("UNSELECTABLE");

    private EntityId _docking;
    private Fixed _pullPerTick;
    private EntityId _unloading;
    private Fixed _pushPerTick;
    private int _unloadCount = UnloadAllRiders;

    public override string ModuleName => "RailedTransportDockUpdate";

    /// <summary><c>isLoadingOrUnloading</c>.</summary>
    public bool IsLoadingOrUnloading => _unloading.IsValid || _docking.IsValid;

    public EntityId DockingObject => _docking;
    public EntityId UnloadingObject => _unloading;

    public override void OnSpawn(IWorldServices world) => NextWake = world.Tick + SimulationTiming.SleepNone;

    /// <summary>
    /// The waiting spot is just outside the ferry's bounding circle on the docker's side (the reference uses the <c>DOCKWAITING</c> bones beside the pier): the ferry is a mobile object that
    /// the local avoidance of a walking docker steers around, so a spot inside its footprint could never be reached.
    /// </summary>
    protected override (Fixed X, Fixed Y) ApproachPosition(Entity docker)
    {
        var dx = docker.X - Entity.X;
        var dy = docker.Y - Entity.Y;
        var distance = Fixed.Sqrt(dx * dx + dy * dy);
        if (distance == Fixed.Zero) return (Entity.X, Entity.Y);
        var reach = Entity.Template.BoundingCircleRadius + docker.Template.BoundingCircleRadius + Fixed.One;
        return (Entity.X + dx / distance * reach, Entity.Y + dy / distance * reach);
    }

    /// <summary><c>isClearToEnter</c>: the base rules, and the ferry must have room for the docker.</summary>
    public override bool IsClearToEnter(IWorldServices world, EntityId docker)
    {
        if (!base.IsClearToEnter(world, docker)) return false;
        return Entity.Container is not { } contain || (world.FindEntity(docker) is { } entity && contain.IsValidContainerFor(world, entity, true));
    }

    /// <summary>The dock action: the first call of a docker within tolerance starts the pull-in; the action always asks to be repeated (the docker's machine ends when the dock cancels it).</summary>
    public override bool Action(IWorldServices world, Entity docker)
    {
        if (_docking == docker.Id) return true;
        var dx = Entity.X - docker.X;
        var dy = Entity.Y - docker.Y;
        var dz = Entity.Z - docker.Z;
        var magnitude = Fixed.Sqrt(dx * dx + dy * dy + dz * dz);
        if (magnitude > data.ToleranceDistance) return true;
        _docking = docker.Id;
        docker.Status |= 1UL << UnselectableBit;
        Hold(docker, true);
        _pullPerTick = magnitude / Fixed.FromInt(Math.Max(data.PullInsideTicks, 1));
        docker.Angle = FixedTrig.Atan2(dy, dx);
        world.MarkDirty(docker);
        world.MarkDirty(Entity);
        return true;
    }

    public override long Update(IWorldServices world)
    {
        base.Update(world);
        DoPullInDocking(world);
        DoPushOutDocking(world);
        return SimulationTiming.SleepNone;
    }

    /// <summary><c>unloadAll</c>: everybody leaves, one after the other; ignored while a rider is being pushed out.</summary>
    public void UnloadAll(IWorldServices world)
    {
        if (_unloading.IsValid) return;
        _unloadCount = UnloadAllRiders;
        UnloadNext(world);
    }

    /// <summary><c>unloadSingleObject</c>: one rider (the first in the list) leaves.</summary>
    public void UnloadSingleObject(IWorldServices world)
    {
        if (_unloading.IsValid) return;
        _unloadCount = 1;
        UnloadNext(world);
    }

    private static void Hold(Entity entity, bool hold)
        => entity.ScriptDisableMask = hold ? (byte)(entity.ScriptDisableMask | Entity.ScriptHeld) : (byte)(entity.ScriptDisableMask & ~Entity.ScriptHeld);

    private void DoPullInDocking(IWorldServices world)
    {
        if (!_docking.IsValid) return;
        var docker = world.FindEntity(_docking);
        if (docker is null || docker.DestroyRequested)
        {
            _docking = EntityId.Invalid;
            world.MarkDirty(Entity);
            return;
        }
        var dx = Entity.X - docker.X;
        var dy = Entity.Y - docker.Y;
        var dz = Entity.Z - docker.Z;
        var length = Fixed.Sqrt(dx * dx + dy * dy + dz * dz);
        if (length > Fixed.Zero)
        {
            docker.X += dx / length * _pullPerTick;
            docker.Y += dy / length * _pullPerTick;
        }
        world.MarkDirty(docker);
        var rx = Entity.X - docker.X;
        var ry = Entity.Y - docker.Y;
        if (rx * rx + ry * ry > PullArrivalSquared) return;
        CancelDock(docker.Id);
        CrateModules.AiOf(docker)?.OrderIdle(world);
        _docking = EntityId.Invalid;
        if (!world.TryEnterContainer(docker, Entity))
        {
            Hold(docker, false);
            docker.Status &= ~(1UL << UnselectableBit);
        }
        world.MarkDirty(Entity);
    }

    private (Fixed X, Fixed Y, Fixed Z) ExitPoint(IWorldServices world)
    {
        var reach = Entity.Template.GeometryMajorRadius;
        var x = Entity.X + FixedTrig.Cos(Entity.Angle) * reach;
        var y = Entity.Y + FixedTrig.Sin(Entity.Angle) * reach;
        return (x, y, world.GroundHeightAt(x, y));
    }

    private void DoPushOutDocking(IWorldServices world)
    {
        if (!_unloading.IsValid) return;
        var unloader = world.FindEntity(_unloading);
        // "if unloader is not found (like they got destroyed) unload the next object inside"
        if (unloader is null || unloader.DestroyRequested)
        {
            UnloadNext(world);
            return;
        }
        var (ex, ey, ez) = ExitPoint(world);
        var dx = ex - unloader.X;
        var dy = ey - unloader.Y;
        var dz = ez - unloader.Z;
        var length = Fixed.Sqrt(dx * dx + dy * dy + dz * dz);
        var step = Fixed.Min(_pushPerTick, length);
        if (length > Fixed.Zero)
        {
            unloader.X += dx / length * step;
            unloader.Y += dy / length * step;
        }
        unloader.Z = ez;
        world.MarkDirty(unloader);
        var rx = ex - unloader.X;
        var ry = ey - unloader.Y;
        if (rx * rx + ry * ry > PushArrivalSquared) return;
        CrateModules.AiOf(unloader)?.OrderIdle(world);
        Hold(unloader, false);
        unloader.Status &= ~(1UL << UnselectableBit);
        UnloadNext(world);
    }

    /// <summary><c>unloadNext</c>: the first rider leaves the ferry and starts its push-out, while the unload count lasts.</summary>
    private void UnloadNext(IWorldServices world)
    {
        _unloading = EntityId.Invalid;
        world.MarkDirty(Entity);
        if (_unloadCount == 0 || Entity.Container is not { } contain) return;
        Entity? unloader = null;
        foreach (var id in contain.Contained)
            if (world.FindEntity(id) is { } found) { unloader = found; break; }
        if (unloader is null) return;
        contain.Remove(world, unloader, false);
        world.Emit(SimEventKind.EntityUncontained, unloader.Id, Entity.Id, string.Empty, 0);
        unloader.X = Entity.X;
        unloader.Y = Entity.Y;
        unloader.Z = Entity.Z;
        unloader.Angle = Entity.Angle;
        unloader.HasMoveTarget = false;
        Hold(unloader, true);
        var (ex, ey, ez) = ExitPoint(world);
        var dx = ex - unloader.X;
        var dy = ey - unloader.Y;
        var dz = ez - unloader.Z;
        _pushPerTick = Fixed.Sqrt(dx * dx + dy * dy + dz * dz) / Fixed.FromInt(Math.Max(data.PushOutsideTicks, 1));
        _unloading = unloader.Id;
        if (_unloadCount != UnloadAllRiders) _unloadCount--;
        world.MarkDirty(unloader);
    }

    protected override void DescribeDock(IStateWriter writer)
    {
        if (_docking.IsValid)
        {
            writer.UInt("docking", _docking.Value);
            writer.Fixed("pullPerTick", _pullPerTick);
        }
        if (_unloading.IsValid)
        {
            writer.UInt("unloading", _unloading.Value);
            writer.Fixed("pushPerTick", _pushPerTick);
        }
        if (_unloadCount != UnloadAllRiders) writer.Int("unloadCount", _unloadCount);
    }

    protected override void LoadDock(IStateReader reader)
    {
        _docking = reader.Has("docking") ? new EntityId(checked((uint)reader.UInt("docking"))) : EntityId.Invalid;
        _pullPerTick = reader.Has("pullPerTick") ? reader.Fixed("pullPerTick") : Fixed.Zero;
        _unloading = reader.Has("unloading") ? new EntityId(checked((uint)reader.UInt("unloading"))) : EntityId.Invalid;
        _pushPerTick = reader.Has("pushPerTick") ? reader.Fixed("pushPerTick") : Fixed.Zero;
        _unloadCount = reader.Has("unloadCount") ? checked((int)reader.Int("unloadCount")) : UnloadAllRiders;
    }
}
