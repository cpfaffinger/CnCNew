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

internal static class RailroadModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<RailroadData>("RailroadBehavior", ModuleOwnerKind.Object, (configuration, _) => RailroadData.Parse(configuration), data => new RailroadBehavior(data)),
    ];
}

/// <summary>
/// <c>RailroadBehaviorModuleData</c> (<c>RailroadGuideAIUpdate.h</c>): whether this object is the locomotive, the carriages it hauls, its speed limit and its acceleration, braking and friction
/// factors (all per logic frame of the original: the reference applies them once per frame without conversion, and so does <see cref="RailroadBehavior"/> at every rate), the time it waits at a station and the speed thresholds of its collision response. The
/// sounds, the crash template name and the path prefix (which the reference stores and never reads) are typed but have no consumer. Defaults are the constructor's (<c>SpeedMax 4</c>,
/// <c>Acceleration 1.01</c>, <c>Braking 0.99</c>, <c>Friction 0.97</c>, 150 frames at a station).
/// </summary>
public sealed record RailroadData(bool IsLocomotive, ImmutableArray<string> Carriages, Fixed RunningGarrisonSpeedMax, Fixed KillSpeedMin, Fixed SpeedMax, Fixed Acceleration, Fixed Braking, Fixed Friction,
    long WaitAtStationTicks) : ModuleData
{
    public static RailroadData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Bool(configuration, "IsLocomotive", false), ModuleConfigReader.AccumulatedNames(configuration, "CarriageTemplateName"),
        // Speeds per logic frame of the original (INI::parseReal): scaled to per tick. The factors per frame that follow (Acceleration, Braking, Friction) are not:
        // the behavior applies them once per frame of the original and reads the three speeds back per frame.
        configuration.Time.PerOriginalFrameToPerTick(ModuleConfigReader.Real(configuration, "RunningGarrisonSpeedMax", Fixed.One)), configuration.Time.PerOriginalFrameToPerTick(ModuleConfigReader.Real(configuration, "KillSpeedMin", Fixed.One)),
        configuration.Time.PerOriginalFrameToPerTick(ModuleConfigReader.Real(configuration, "SpeedMax", Fixed.FromInt(4))), ModuleConfigReader.Real(configuration, "Acceleration", Fixed.FromDecimal(1.01m)),
        ModuleConfigReader.Real(configuration, "Braking", Fixed.FromDecimal(0.99m)), ModuleConfigReader.Real(configuration, "Friction", Fixed.FromDecimal(0.97m)),
        ModuleConfigReader.Ticks(configuration, "WaitAtStationTime", 150));
}

/// <summary>
/// <c>RailroadBehavior</c> (<c>RailroadGuideAIUpdate.cpp</c>, <c>RailroadBehavior::update</c>, <c>loadTrackData</c>, <c>createCarriages</c>, <c>getPulled</c>,
/// <c>updatePositionTrackDistance</c>, <c>FindPosByPathDistance</c>, <c>onCollide</c>): trains. The locomotive finds the map waypoint nearest to it (<c>loadTrackData</c>; the
/// waypoint links of the map are the track, <c>getLink(0)</c> at every point; a track that leads back to its first waypoint loops), measures it, and hauls its carriages — created
/// from <c>CarriageTemplateName</c> behind it, or taken from the unattached allied carriages it finds on the track behind it (a map artist's carriages) — along it. The conductor
/// (the locomotive) accelerates (<c>(speed + 0.02) · Acceleration</c> up to <c>SpeedMax</c>), brakes at a waypoint whose name ends in <c>Station</c> (or <c>Disembark</c>, which also
/// unloads every carriage, or <c>PingPong</c> on the way past it, which reverses the direction), waits <c>WaitAtStationTime</c> and sets off again; a carriage whose puller is gone for
/// more than two frames becomes a lead carriage of its own and coasts to a halt (<c>Friction</c>). Each carriage sits on the track one of its own major radii behind its puller's hitch and
/// turns to face it; on a track that does not loop, a carriage that has not entered the track yet or has left it at the end is out of the world's way (<c>m_waitingInWings</c>,
/// <c>m_endOfLine</c>: it collides with nothing, and once the lead carriage of a train has left the track it is destroyed). A moving locomotive kills whatever it hits (at <c>KillSpeedMin</c>
/// or more; slower, it crushes for <c>speed · 10</c>), a train hitting a train kills both, every carriage kills base structures and demo traps it touches and pushes other things aside.
/// <para>The geometry that is physics or presentation in the reference is replaced: the position is the exact result of the reference's transform product (a carriage's centre lies one major
/// radius from its hitch on the track, facing its puller), the ground height is the terrain's, a train is hit by whatever its oriented box (major/minor radius) overlaps — checked every tick
/// after the train moved, since the footprint collision of moving units never sees a train that moves itself — and the physics of the push (<c>addVelocityTo</c>, yaw and pitch rates, the
/// bounce) is left out: the victim is shoved out of the footprint (a quarter of the overlap for vehicles; infantry is not displaced) and killed or damaged as the reference decides. A carriage
/// outside the track keeps the position of the first track point instead of the origin. Not modeled: the sounds, hiding the model while it is out of the track, the pathfinding wall
/// made of a train waiting at a station (<c>makeAWallOutOfThisTrain</c>), letting boarding units through (<c>getEnterTarget</c>), and hitching by proximity needs the carriages' allied owner.</para>
/// </summary>
public sealed class RailroadBehavior(RailroadData data) : SimModule, IUpdateModule, IProducerReceiver
{
    /// <summary>
    /// <c>FRAMES_UNPULLED_LONG_ENOUGH_TO_UNHITCH (2)</c> (RailroadGuideAIUpdate.cpp): updates of the original without a pull from the carriage ahead; with more than
    /// that many behind it a carriage leads itself. The counter counts this module's updates (one per tick), so the bound is the last tick of the frame after
    /// those two (<see cref="UnpulledLongEnough"/>): the carriage's first frame as a lead ends where it ends at 30 Hz.
    /// </summary>
    private const long UnpulledLongEnoughToUnhitchFrames = 2;

    private static long UnpulledLongEnough(SimulationTime time) => time.OriginalFrames(UnpulledLongEnoughToUnhitchFrames + 1) - TickOrder.Next;
    private static readonly Fixed PushStart = Fixed.FromDecimal(0.02m);
    private static readonly int UnderConstructionBit = ObjectStatusBits.UnderConstruction;
    private static readonly int CrushDamage = NameTables.DamageType.TryIndexOf("CRUSH", out var crush) ? crush : throw new InvalidOperationException("CRUSH is not a damage type.");

    /// <summary><c>RailroadBehavior::ConductorState</c>.</summary>
    public enum ConductorStates : byte { ApplyBrakes, WaitAtStation, Accelerate, Coast }

    /// <summary><c>TrackPoint</c>: one waypoint of the track with the distances and the name-derived flags the locomotive reacts to.</summary>
    private sealed record TrackPoint(Fixed X, Fixed Y, Fixed Z, Fixed DistanceFromFirst, int Handle, bool IsTunnelOrBridge, bool IsStation, bool IsDisembark, bool IsPingPong);

    /// <summary><c>TrainTrack</c>: the measured waypoint path every carriage of a train shares.</summary>
    private sealed class Track
    {
        public List<TrackPoint> Points { get; } = [];
        public Fixed Length { get; set; }
        public bool IsLooping { get; set; }
    }

    /// <summary><c>RailroadBehavior::PullInfo</c>.</summary>
    private sealed class PullInfo
    {
        public Fixed Direction = Fixed.One;
        public Fixed Speed;
        public Fixed TrackDistance;
        public Fixed HitchX;
        public Fixed HitchY;
        public Fixed HitchZ;
        public int MostRecentSpecialPoint = -1;

        public void CopyFrom(PullInfo other)
        {
            Direction = other.Direction;
            Speed = other.Speed;
            TrackDistance = other.TrackDistance;
            HitchX = other.HitchX;
            HitchY = other.HitchY;
            HitchZ = other.HitchZ;
            MostRecentSpecialPoint = other.MostRecentSpecialPoint;
        }

        public void Describe(IStateWriter writer, string name)
        {
            writer.BeginObject(name);
            writer.Fixed("direction", Direction);
            writer.Fixed("speed", Speed);
            writer.Fixed("trackDistance", TrackDistance);
            writer.Fixed("hitchX", HitchX);
            writer.Fixed("hitchY", HitchY);
            writer.Fixed("hitchZ", HitchZ);
            writer.Int("specialPoint", MostRecentSpecialPoint);
            writer.EndObject();
        }

        public void Load(IStateReader reader)
        {
            Direction = reader.Fixed("direction");
            Speed = reader.Fixed("speed");
            TrackDistance = reader.Fixed("trackDistance");
            HitchX = reader.Fixed("hitchX");
            HitchY = reader.Fixed("hitchY");
            HitchZ = reader.Fixed("hitchZ");
            MostRecentSpecialPoint = checked((int)reader.Int("specialPoint"));
        }
    }

    private long _nextWake = -1;
    private Track? _track;
    private bool _trackDataLoaded;
    private int _anchorId = -1;
    private EntityId _trailer = EntityId.Invalid;
    private EntityId _conductor = EntityId.Invalid;
    private List<EntityId> _carriages = [];
    private readonly PullInfo _conductorPull = new();
    private readonly PullInfo _pull = new();
    private int _currentPointHandle = -1;
    private long _waitAtStationTimer;
    private bool _carriagesCreated;
    private bool _hasEverBeenHitched;
    private bool _waitingInWings = true;
    private bool _endOfLine;
    private bool _isLead = data.IsLocomotive;
    private int _wantsToBeLead;
    private bool _disembark;
    private bool _inTunnel;
    private bool _held;
    private ConductorStates _state = data.IsLocomotive ? ConductorStates.Accelerate : ConductorStates.Coast;
    // The train runs the original's recursion once per logic frame of the original, in its units: every speed of the state (PullInfo.Speed) is a distance per
    // FRAME at every rate, the conductor acts and the friction applies with the first tick of the lead carriage's own frame, the track distance covers the
    // frame's step tick by tick, and the collision response (damage, shove) comes with the frame's last tick. A pulled carriage is told by its puller whether
    // the lead is inside a frame. Both fields are at rest (unwritten) at 30 Hz.
    private FramePhase _framePhase;
    private bool _pulledInsideFrame;
    private int _ticksPerOriginalFrame = (int)TickOrder.Next; // of the world, for Speed; not state

    public override string ModuleName => "RailroadBehavior";
    public RailroadData Data => data;
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public bool IsLocomotive => data.IsLocomotive;
    public ConductorStates Conductor => _state;
    public EntityId Trailer => _trailer;
    public bool IsWaitingInWings => _waitingInWings;
    public bool IsEndOfLine => _endOfLine;
    public bool IsLeadCarriage => _isLead;
    public bool HasEverBeenHitched => _hasEverBeenHitched;
    /// <summary>The speed along the track in distance per tick (the unit of <c>Entity.Speed</c>).</summary>
    public Fixed Speed => _ticksPerOriginalFrame == TickOrder.Next ? _pull.Speed : _pull.Speed / Fixed.FromInt(_ticksPerOriginalFrame);
    public Fixed TrackLength => _track?.Length ?? Fixed.Zero;
    public bool IsOnLoopingTrack => _track?.IsLooping ?? false;

    /// <summary>The locomotive stays at its station while held (<c>setHeld</c>, driven by scripts).</summary>
    public void SetHeld(bool held) => _held = held;

    /// <summary><c>RailroadBehavior::isRailroad</c> (<c>CollideModuleInterface::isRailroad</c>): an object on the track that is the lead or the last carriage; the ones in between decline to collide with other trains.</summary>
    public bool IsRailroad => _track is not null && !_waitingInWings && !_endOfLine && (_isLead || !_trailer.IsValid);

    public void OnProducedBy(IWorldServices world, EntityId producer) => _conductor = producer;

    /// <summary>The locomotive this object belongs to (itself when it is one); invalid for a carriage nobody has hitched.</summary>
    private EntityId TrainId => data.IsLocomotive ? Entity.Id : _conductor;

    // The first update comes one logic frame of the original after the object appeared (the original's first update), so the train's frames end on the same
    // game times at every rate.
    public override void OnSpawn(IWorldServices world) => _nextWake = world.Tick + world.Time.TicksPerOriginalFrame;

    private static RailroadBehavior? RailroadOf(IWorldServices world, EntityId id)
        => id.IsValid && world.FindEntity(id) is { DestroyRequested: false } entity ? entity.ModuleArray.OfType<RailroadBehavior>().FirstOrDefault() : null;

    // ------------------------------------------------------------------------------------------------------------- update

    public long Update(IWorldServices world)
    {
        var time = world.Time;
        _ticksPerOriginalFrame = time.TicksPerOriginalFrame;
        if (!_trackDataLoaded && data.IsLocomotive)
        {
            _track ??= LoadTrack(world);
            _trackDataLoaded = true;
            if (_track is not null)
            {
                CreateCarriages(world);
                // A carriage made now exists from the next tick on; the pull below must not mistake that for a lost trailer.
                if (_carriages.Count > 0 && _carriages.Any(id => world.FindEntity(id) is null)) return SimulationTiming.SleepNone;
            }
        }
        if (_track is null) return SimulationTiming.SleepNone;

        // The tick's number inside the lead carriage's frame of the original (a locomotive always leads); 30 Hz: every tick is a whole frame.
        var subTick = 0;
        if (data.IsLocomotive)
        {
            subTick = _framePhase.Next(time);
            if (subTick == TickOrder.Next) RunConductor(world);
        }

        if (_wantsToBeLead > UnpulledLongEnough(time)) _isLead = true;
        var frameEnds = !_pulledInsideFrame;
        if (_isLead)
        {
            if (!data.IsLocomotive) subTick = _framePhase.Next(time);
            if (subTick == TickOrder.Next && _state == ConductorStates.Coast) _conductorPull.Speed *= data.Friction;
            _conductorPull.TrackDistance += time.FrameShare(_conductorPull.Speed, subTick);
            if (_track.IsLooping)
            {
                while (_conductorPull.TrackDistance > _track.Length) _conductorPull.TrackDistance -= _track.Length;
                while (_conductorPull.TrackDistance < Fixed.Zero) _conductorPull.TrackDistance += _track.Length;
            }
            (_conductorPull.HitchX, _conductorPull.HitchY, _conductorPull.HitchZ) = FindPosition(world, _conductorPull.TrackDistance, setState: false);
            UpdatePosition(world, _conductorPull, _pull);
            frameEnds = _framePhase.AtBoundary;
            _pulledInsideFrame = false;
            var trailer = RailroadOf(world, _trailer);
            if (trailer is not null) trailer.GetPulled(world, _pull, !frameEnds);
            else
            {
                _trailer = EntityId.Invalid;
                // A train leaves the end of the line one carriage per frame of the original, the last one first.
                if (_endOfLine && frameEnds) world.RequestDestroy(Entity.Id);
            }
        }
        else if (_wantsToBeLead <= UnpulledLongEnough(time)) _wantsToBeLead++;

        // onCollide once per frame of the original, with the train where the frame leaves it: the damage and the shove are amounts per frame.
        if (frameEnds) Collide(world);
        return SimulationTiming.SleepNone;
    }

    /// <summary>The conductor's state machine (the first block of <c>update</c>, locomotive only).</summary>
    private void RunConductor(IWorldServices world)
    {
        switch (_state)
        {
            case ConductorStates.ApplyBrakes:
                _conductorPull.Speed *= data.Braking;
                if (_conductorPull.Speed.Abs() < Fixed.FromDecimal(0.1m))
                {
                    _conductorPull.Speed = Fixed.Zero;
                    // Counted down once per frame of the original, so it is kept in frames.
                    _waitAtStationTimer = world.Time.ToOriginalFrames(data.WaitAtStationTicks);
                    _state = ConductorStates.WaitAtStation;
                    if (_disembark)
                    {
                        Disembark(world);
                        _disembark = false;
                    }
                }
                break;
            case ConductorStates.WaitAtStation:
                _waitAtStationTimer--;
                if (_waitAtStationTimer <= 0 && !_held)
                {
                    _state = ConductorStates.Accelerate;
                    _conductorPull.Speed = Fixed.FromDecimal(0.05m) * _conductorPull.Direction;
                }
                break;
            case ConductorStates.Accelerate:
                _conductorPull.Speed += PushStart * _conductorPull.Direction;
                _conductorPull.Speed *= data.Acceleration;
                var speedMax = world.Time.PerTickToPerOriginalFrame(data.SpeedMax);
                if (_conductorPull.Speed > speedMax) _conductorPull.Speed = speedMax;
                else if (_conductorPull.Speed < -speedMax) _conductorPull.Speed = -speedMax;
                break;
        }
    }

    /// <summary><c>RailroadBehavior::disembark</c>: every carriage of the train unloads its passengers (<c>orderAllPassengersToExit</c>).</summary>
    private void Disembark(IWorldServices world)
    {
        if (Entity.Container is not null) world.EvacuateContainer(Entity);
        RailroadOf(world, _trailer)?.Disembark(world);
    }

    /// <summary><c>RailroadBehavior::getPulled</c>: this carriage obeys its puller, positions itself and passes its own pull on.</summary>
    private void GetPulled(IWorldServices world, PullInfo puller, bool insideFrame)
    {
        EnsureHitched(world);
        _wantsToBeLead = 0;
        _pulledInsideFrame = insideFrame;
        if (_track is null) return;
        _conductorPull.CopyFrom(puller);
        UpdatePosition(world, puller, _pull);
        var trailer = RailroadOf(world, _trailer);
        if (trailer is not null) trailer.GetPulled(world, _pull, insideFrame);
        else
        {
            _trailer = EntityId.Invalid;
            if (_endOfLine && !insideFrame) world.RequestDestroy(Entity.Id);
        }
    }

    /// <summary>A carriage the locomotive made takes over the shared track and finds its trailer in the locomotive's list (<c>hitchNewCarriagebyTemplate</c>).</summary>
    private void EnsureHitched(IWorldServices world)
    {
        if (_track is not null || data.IsLocomotive) return;
        var conductor = RailroadOf(world, _conductor);
        if (conductor?._track is null) return;
        _track = conductor._track;
        _hasEverBeenHitched = true;
        var index = conductor._carriages.IndexOf(Entity.Id);
        if (index >= 0 && !_trailer.IsValid) _trailer = index + 1 < conductor._carriages.Count ? conductor._carriages[index + 1] : EntityId.Invalid;
    }

    // ------------------------------------------------------------------------------------------------------------- track

    /// <summary><c>loadTrackData</c>: the nearest waypoint is the anchor; the track follows <c>getLink(0)</c> from it, and ends where there is no link or comes back to the anchor.</summary>
    private Track? LoadTrack(IWorldServices world)
    {
        if (world.Terrain is not { } terrain) return null;
        TerrainWaypoint? anchor = null;
        if (_anchorId < 0)
        {
            var closest = Fixed.FromDecimal(99999.9m);
            foreach (var waypoint in terrain.Waypoints)
            {
                var dx = Entity.X - waypoint.X;
                var dy = Entity.Y - waypoint.Y;
                var dz = Entity.Z - waypoint.Z;
                var distance = Fixed.Sqrt(dx * dx + dy * dy + dz * dz);
                if (closest <= distance) continue;
                closest = distance;
                anchor = waypoint;
                _anchorId = waypoint.Id;
            }
        }
        else anchor = terrain.Waypoints.FirstOrDefault(waypoint => waypoint.Id == _anchorId);
        if (anchor is null || anchor.Id < 0) return null;

        var track = new Track();
        track.Points.Add(new TrackPoint(anchor.X, anchor.Y, anchor.Z, Fixed.Zero, anchor.Id, false, false, false, false));
        var visited = new HashSet<int> { anchor.Id };
        var scanner = anchor;
        while (terrain.NextWaypoint(scanner) is { } another)
        {
            var dx = scanner.X - another.X;
            var dy = scanner.Y - another.Y;
            var dz = scanner.Z - another.Z;
            track.Length += Fixed.Sqrt(dx * dx + dy * dy + dz * dz);
            track.Points.Add(new TrackPoint(another.X, another.Y, another.Z, track.Length, scanner.Id, another.Name.EndsWith("Tunnel", StringComparison.Ordinal),
                another.Name.EndsWith("Station", StringComparison.Ordinal), scanner.Name.EndsWith("Disembark", StringComparison.Ordinal), scanner.Name.EndsWith("PingPong", StringComparison.Ordinal)));
            scanner = another;
            if (scanner.Id == anchor.Id)
            {
                track.IsLooping = true;
                break;
            }
            if (!visited.Add(scanner.Id)) break;
        }
        return track;
    }

    /// <summary>
    /// <c>FindPosByPathDistance</c>: the point at <paramref name="distance"/> along the track (wrapped on a looping track, clamped on an open one, setting the wings/end-of-line flags),
    /// and — with <paramref name="setState"/>, only the carriage's own position uses it — the tunnel flag and the locomotive's reaction to the segment it is on.
    /// </summary>
    private (Fixed X, Fixed Y, Fixed Z) FindPosition(IWorldServices world, Fixed distance, bool setState)
    {
        var track = _track!;
        _waitingInWings = false;
        var actual = distance;
        if (track.IsLooping)
        {
            while (actual < Fixed.Zero) actual += track.Length;
            while (actual > track.Length) actual -= track.Length;
        }
        else
        {
            if (distance < Fixed.Zero) _waitingInWings = true;
            else if (distance >= track.Length) _endOfLine = true;
            actual = Fixed.Max(Fixed.Min(track.Length, distance), Fixed.Zero);
        }

        var first = track.Points[0];
        var position = (X: first.X, Y: first.Y, Z: first.Z);
        for (var index = 0; index < track.Points.Count; index++)
        {
            var point = track.Points[index];
            if (!(point.DistanceFromFirst < actual)) continue;
            position = (point.X, point.Y, point.Z);
            if (index + 1 >= track.Points.Count || !(track.Points[index + 1].DistanceFromFirst > actual)) continue;
            var next = track.Points[index + 1];
            if (setState)
            {
                _inTunnel = point.IsTunnelOrBridge;
                if (data.IsLocomotive && _currentPointHandle != point.Handle)
                {
                    _currentPointHandle = point.Handle;
                    if (point.IsStation) { _state = ConductorStates.ApplyBrakes; _disembark = false; }
                    else if (point.IsDisembark) { _state = ConductorStates.ApplyBrakes; _disembark = true; }
                    else if (point.IsPingPong && _conductorPull.MostRecentSpecialPoint != point.Handle)
                    {
                        _conductorPull.MostRecentSpecialPoint = point.Handle;
                        _state = ConductorStates.ApplyBrakes;
                        _disembark = false;
                        _conductorPull.Direction = -_conductorPull.Direction;
                    }
                }
            }
            var difference = actual - point.DistanceFromFirst;
            var dx = next.X - point.X;
            var dy = next.Y - point.Y;
            var dz = next.Z - point.Z;
            var length = Fixed.Sqrt(dx * dx + dy * dy + dz * dz);
            if (length == Fixed.Zero) return position;
            return (point.X + dx / length * difference, point.Y + dy / length * difference, point.Z + dz / length * difference);
        }
        return position;
    }

    /// <summary>
    /// <c>updatePositionTrackDistance</c>: the carriage's track distance is its puller's minus two of its own major radii; it is put at that point on the track, facing the puller's hitch.
    /// The reference builds a transform that rotates the carriage about its rear hitch and moves the hitch to the track point; the product reduces to: heading towards the puller's hitch from
    /// the old hitch, centre one major radius ahead of the new hitch on that heading, ground height of the old hitch (kept inside a tunnel).
    /// </summary>
    private void UpdatePosition(IWorldServices world, PullInfo puller, PullInfo mine)
    {
        var radius = Entity.Template.GeometryMajorRadius;
        mine.TrackDistance = puller.TrackDistance - radius * 2;
        mine.Speed = puller.Speed;
        mine.Direction = puller.Direction;
        (mine.HitchX, mine.HitchY, mine.HitchZ) = FindPosition(world, mine.TrackDistance, setState: false);
        var car = FindPosition(world, mine.TrackDistance, setState: true);

        var turnX = Entity.X - FixedTrig.Cos(Entity.Angle) * radius;
        var turnY = Entity.Y - FixedTrig.Sin(Entity.Angle) * radius;
        var desired = FixedTrig.Atan2(puller.HitchY - turnY, puller.HitchX - turnX);
        var ground = world.GroundHeightAt(turnX, turnY);
        Entity.Angle = FixedTrig.Normalize(desired);
        Entity.X = car.X + FixedTrig.Cos(Entity.Angle) * radius;
        Entity.Y = car.Y + FixedTrig.Sin(Entity.Angle) * radius;
        if (!_inTunnel) Entity.Z = ground;
        world.MarkDirty(Entity);
    }

    // ------------------------------------------------------------------------------------------------------------- carriages

    /// <summary>
    /// <c>createCarriages</c>: the carriage behind the locomotive is the closest unattached allied carriage within two major radii behind its hitch if the map has one there (and each
    /// carriage looks behind itself the same way); otherwise the carriages of <c>CarriageTemplateName</c> are created, one behind the other. After a restore the chain saved in the
    /// state is walked instead and given the rebuilt track.
    /// </summary>
    private void CreateCarriages(IWorldServices world)
    {
        if (_carriagesCreated && !_trailer.IsValid && _carriages.Count == 0) return;
        var hitched = _trailer.IsValid ? RailroadOf(world, _trailer) : null;
        if (hitched is not null)
        {
            hitched.HitchByProximity(world, Entity.Id, _track!);
            _carriagesCreated = true;
            return;
        }
        var close = FindCarriageBehind(world);
        if (close is not null)
        {
            _trailer = close.Entity.Id;
            close.HitchByProximity(world, Entity.Id, _track!);
            _carriagesCreated = true;
            return;
        }
        if (_carriagesCreated) return;
        _carriages = [];
        foreach (var name in data.Carriages)
        {
            if (!world.Templates.TryGet(name, out var template) || !template.IsInstantiable) continue;
            var id = world.RequestSpawn(template.Index, Entity.Owner, Entity.X, Entity.Y, Entity.Z, Entity.Angle, new SpawnExtras(Fixed.Zero, Producer: Entity.Id));
            _carriages.Add(id);
        }
        if (_carriages.Count > 0) _trailer = _carriages[0];
        _carriagesCreated = true;
    }

    /// <summary><c>hitchNewCarriagebyProximity</c>: this carriage shares the track and looks for the carriage behind it.</summary>
    private void HitchByProximity(IWorldServices world, EntityId conductor, Track track)
    {
        _track = track;
        _conductor = conductor;
        _hasEverBeenHitched = true;
        var behind = _trailer.IsValid ? RailroadOf(world, _trailer) : FindCarriageBehind(world);
        if (behind is null) return;
        _trailer = behind.Entity.Id;
        behind.HitchByProximity(world, conductor, _track);
    }

    private RailroadBehavior? FindCarriageBehind(IWorldServices world)
    {
        var reach = Entity.Template.GeometryMajorRadius * 2;
        var hitchX = Entity.X - FixedTrig.Cos(Entity.Angle) * reach;
        var hitchY = Entity.Y - FixedTrig.Sin(Entity.Angle) * reach;
        RailroadBehavior? best = null;
        var bestDistance = Fixed.MaxValue;
        foreach (var other in world.Entities)
        {
            if (other.Id == Entity.Id || other.DestroyRequested) continue;
            if (other.ModuleArray.OfType<RailroadBehavior>().FirstOrDefault() is not { _hasEverBeenHitched: false } candidate || candidate.IsLocomotive) continue;
            if (world.RelationshipOf(Entity.Owner, other.Owner) != Relationship.Allies && other.Owner != Entity.Owner) continue;
            var dx = other.X - hitchX;
            var dy = other.Y - hitchY;
            var distance = dx * dx + dy * dy;
            if (!SquaredReach.Of(reach).Contains(distance) || distance > bestDistance || (distance == bestDistance && best is not null && other.Id.Value > best.Entity.Id.Value)) continue;
            best = candidate;
            bestDistance = distance;
        }
        return best;
    }

    // ------------------------------------------------------------------------------------------------------------- collision

    /// <summary>
    /// <c>onCollide</c>, for everything this carriage's oriented box overlaps right now (ids ascending): trains destroy each other (a locomotive kills the other, a lead carriage both), base
    /// structures and demo traps die, and what has physics is shoved clear and, for a locomotive moving at or above <c>KillSpeedMin</c>, killed (below it crushed for <c>speed · 10</c>).
    /// </summary>
    private void Collide(IWorldServices world)
    {
        if (_waitingInWings || _endOfLine) return;
        var radiusUs = Entity.Template.GeometryMajorRadius;
        List<Entity>? hits = null;
        foreach (var other in world.Entities)
        {
            if (other.Id == Entity.Id || other.DestroyRequested || other.ContainedBy.IsValid || !Overlaps(other)) continue;
            (hits ??= []).Add(other);
        }
        if (hits is null) return;
        hits.Sort(static (a, b) => a.Id.Value.CompareTo(b.Id.Value));
        foreach (var other in hits)
        {
            if (other.DestroyRequested) continue;
            var otherRail = other.ModuleArray.OfType<RailroadBehavior>().FirstOrDefault();
            // The cars of one train touch each other all the time; they never collide with their own train.
            if (otherRail is not null && TrainId.IsValid && TrainId == otherRail.TrainId) continue;
            if (otherRail is { IsRailroad: true } train)
            {
                // The physics engine calls onCollide on both objects: each train reacts to the other.
                HitTrain(world, train);
                if (!train._waitingInWings && !train._endOfLine) train.HitTrain(world, this);
                continue;
            }
            if (other.Template.IsKindOf("STRUCTURE"))
            {
                if (other.Template.IsKindOf("FS_POWER") || other.Template.IsKindOf("FS_FACTORY") || other.Template.IsKindOf("FS_BASE_DEFENSE") || other.Template.IsKindOf("FS_TECHNOLOGY")
                    || other.Template.IsKindOf("REBUILD_HOLE"))
                {
                    world.Kill(other.Id);
                    continue;
                }
                if (other.ModuleArray.OfType<DemoTrapUpdate>().Any())
                {
                    if (!other.HasStatus(UnderConstructionBit)) world.Kill(Entity.Id);
                    world.Kill(other.Id);
                    continue;
                }
            }
            if (!other.ModuleArray.OfType<PhysicsBehavior>().Any() || other.ContainedBy == Entity.Id) continue;

            // Shove out of the footprint: "the plus 1 makes them go just outside of me"; infantry is left where it stands.
            var victimIsInfantry = other.Template.IsKindOf("INFANTRY");
            var dx = other.X - Entity.X;
            var dy = other.Y - Entity.Y;
            var dz = other.Z - Entity.Z;
            var distance = Fixed.Sqrt(dx * dx + dy * dy + dz * dz);
            if (!victimIsInfantry && distance > Fixed.Zero)
            {
                var overlap = (radiusUs + other.Template.GeometryMajorRadius - distance + Fixed.One) / Fixed.FromInt(4);
                other.X += dx / distance * overlap;
                other.Y += dy / distance * overlap;
                other.Z += dz / distance * overlap;
                world.MarkDirty(other);
            }
            // The speeds of the state are distances per frame of the original; so are the thresholds read back from the catalog and the damage "speed · 10".
            if (_state == ConductorStates.WaitAtStation || (_state == ConductorStates.Coast && _pull.Speed < world.Time.PerTickToPerOriginalFrame(data.RunningGarrisonSpeedMax)) || !data.IsLocomotive) continue;
            if (other.EffectivelyDead) continue;
            if (_pull.Speed >= world.Time.PerTickToPerOriginalFrame(data.KillSpeedMin)) world.Kill(other.Id);
            else world.Damage(other.Id, new DamageInfo(Entity.Id, _pull.Speed * 10, CrushDamage, DeathTypes.Crushed, false));
        }
    }

    /// <summary>What <c>onCollide</c> does when the other object is a train: a locomotive "just rear ended a stranded train" and kills it; a lead carriage "coasted into some other carriage" and both die.</summary>
    private void HitTrain(IWorldServices world, RailroadBehavior other)
    {
        if (data.IsLocomotive) world.Kill(other.Entity.Id);
        else if (_isLead)
        {
            world.Kill(other.Entity.Id);
            world.Kill(Entity.Id);
        }
    }

    /// <summary>The overlap of this carriage's box (major radius along its heading, minor across, grown by the other's bounding radius) with another object's position.</summary>
    private bool Overlaps(Entity other)
    {
        var template = Entity.Template;
        var dx = other.X - Entity.X;
        var dy = other.Y - Entity.Y;
        var otherRadius = other.Template.BoundingCircleRadius;
        if (!template.Geometry.Equals("BOX", StringComparison.OrdinalIgnoreCase))
        {
            var limit = template.BoundingCircleRadius + otherRadius;
            return SquaredReach.Of(limit).Contains(dx * dx + dy * dy);
        }
        var cos = FixedTrig.Cos(Entity.Angle);
        var sin = FixedTrig.Sin(Entity.Angle);
        var along = dx * cos + dy * sin;
        var across = -dx * sin + dy * cos;
        return along.Abs() <= template.GeometryMajorRadius + otherRadius && across.Abs() <= template.GeometryMinorRadius + otherRadius;
    }

    // ------------------------------------------------------------------------------------------------------------- state

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("anchor", _anchorId);
        writer.UInt("trailer", _trailer.Value);
        writer.UInt("conductor", _conductor.Value);
        writer.Int("currentPoint", _currentPointHandle);
        writer.Int("waitTimer", _waitAtStationTimer);
        writer.Bool("carriagesCreated", _carriagesCreated);
        writer.Bool("everHitched", _hasEverBeenHitched);
        writer.Bool("inWings", _waitingInWings);
        writer.Bool("endOfLine", _endOfLine);
        writer.Bool("lead", _isLead);
        writer.Int("wantsLead", _wantsToBeLead);
        writer.Bool("disembark", _disembark);
        writer.Bool("inTunnel", _inTunnel);
        writer.Bool("held", _held);
        writer.Int("conductorState", (int)_state);
        _pull.Describe(writer, "pull");
        _conductorPull.Describe(writer, "conductorPull");
        _framePhase.Describe(writer, "framePhase");
        if (_pulledInsideFrame) writer.Bool("pulledInsideFrame", true);
        if (_carriages.Count == 0) return;
        writer.BeginArray("carriages");
        foreach (var id in _carriages)
        {
            writer.BeginObject(null);
            writer.UInt("id", id.Value);
            writer.EndObject();
        }
        writer.EndArray();
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _anchorId = checked((int)reader.Int("anchor"));
        _trailer = new EntityId(checked((uint)reader.UInt("trailer")));
        _conductor = new EntityId(checked((uint)reader.UInt("conductor")));
        _currentPointHandle = checked((int)reader.Int("currentPoint"));
        _waitAtStationTimer = reader.Int("waitTimer");
        _carriagesCreated = reader.Bool("carriagesCreated");
        _hasEverBeenHitched = reader.Bool("everHitched");
        _waitingInWings = reader.Bool("inWings");
        _endOfLine = reader.Bool("endOfLine");
        _isLead = reader.Bool("lead");
        _wantsToBeLead = checked((int)reader.Int("wantsLead"));
        _disembark = reader.Bool("disembark");
        _inTunnel = reader.Bool("inTunnel");
        _held = reader.Bool("held");
        var state = reader.Int("conductorState");
        if (state < 0 || state > (int)ConductorStates.Coast) throw new InvalidDataException("Conductor state out of range.");
        _state = (ConductorStates)state;
        _pull.Load(reader.Object("pull"));
        _conductorPull.Load(reader.Object("conductorPull"));
        _framePhase.Load(reader, "framePhase");
        _pulledInsideFrame = reader.Has("pulledInsideFrame") && reader.Bool("pulledInsideFrame");
        _carriages = reader.Has("carriages") ? [..reader.Array("carriages").Select(item => new EntityId(checked((uint)item.UInt("id"))))] : [];
        // m_trackDataLoaded and m_track are not saved: the locomotive reloads the track and hands it down the saved chain.
        _track = null;
        _trackDataLoaded = false;
    }
}
