using System.Collections.Immutable;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

internal static class BridgeModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<BridgeData>("BridgeBehavior", ModuleOwnerKind.Object, (configuration, _) => BridgeData.Parse(configuration), data => new BridgeBehavior(data)),
        new ModuleBinding<NoFieldsData>("BridgeScaffoldBehavior", ModuleOwnerKind.Object, (_, _) => NoFieldsData.Instance, _ => new BridgeScaffoldBehavior()),
        new ModuleBinding<NoFieldsData>("BridgeTowerBehavior", ModuleOwnerKind.Object, (_, _) => NoFieldsData.Instance, _ => new BridgeTowerBehavior()),
    ];
}

/// <summary>One <c>BridgeDieFX</c>/<c>BridgeDieOCL</c> line: the effect (or creation) list, the tick after the bridge's death it goes off at and the bone it is placed at (empty: somewhere on the deck).</summary>
public sealed record BridgeEffect(string Name, long DelayTicks, string Bone);

/// <summary><c>BridgeBehaviorModuleData</c> (<c>BridgeBehavior.cpp</c>): the scaffold speeds (per tick) and the lists of effects and creation lists that go off during the collapse.</summary>
public sealed record BridgeData(Fixed LateralScaffoldSpeed, Fixed VerticalScaffoldSpeed, ImmutableArray<BridgeEffect> DieFx, ImmutableArray<BridgeEffect> DieOcl) : ModuleData
{
    public static BridgeData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Real(configuration, "LateralScaffoldSpeed", Fixed.One) / configuration.Time.TicksPerSecondFixed,
        ModuleConfigReader.Real(configuration, "VerticalScaffoldSpeed", Fixed.One) / configuration.Time.TicksPerSecondFixed,
        Effects(configuration, "BridgeDieFX", "FX"), Effects(configuration, "BridgeDieOCL", "OCL"));

    /// <summary><c>parseFX</c>/<c>parseOCL</c> + <c>parseTimeAndLocationInfo</c>: <c>&lt;FX|OCL&gt;:&lt;name&gt; Delay:&lt;ms&gt; [Bone:&lt;name&gt;]</c>, colons and blanks both separating tokens.</summary>
    private static ImmutableArray<BridgeEffect> Effects(RatedModuleConfiguration configuration, string key, string label)
    {
        var effects = ImmutableArray.CreateBuilder<BridgeEffect>();
        foreach (var field in ModuleConfigReader.All(configuration, key))
        {
            var tokens = field.Value.OriginalText.Split([' ', '\t', ':'], StringSplitOptions.RemoveEmptyEntries);
            var valid = tokens.Length is 4 or 6 && tokens[0].Equals(label, StringComparison.OrdinalIgnoreCase) && tokens[2].Equals("Delay", StringComparison.OrdinalIgnoreCase)
                        && long.TryParse(tokens[3], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out _)
                        && (tokens.Length == 4 || tokens[4].Equals("Bone", StringComparison.OrdinalIgnoreCase));
            if (!valid) throw new InvalidDataException($"{field.Location.Source}({field.Location.Line}): BridgeBehavior {key} expects '{label}:<name> Delay:<ms> [Bone:<name>]', got '{field.Value.OriginalText.Trim()}'.");
            effects.Add(new BridgeEffect(tokens[1], configuration.Time.DurationMillisecondsToTicks(long.Parse(tokens[3], System.Globalization.CultureInfo.InvariantCulture)), tokens.Length == 6 ? tokens[5] : string.Empty));
        }
        return effects.ToImmutable();
    }
}

/// <summary>
/// <c>BridgeBehavior</c> (<c>BridgeBehavior.cpp</c>): the damage object of a bridge. Damage and healing to the bridge go to its four towers in the same proportion of their health
/// (<c>onDamage</c>/<c>onHealing</c>: the percentage of the bridge's maximum health, unless the hit came from a tower); when the bridge is destroyed (<c>onDie</c>) its towers are killed, whatever
/// stands on its deck and cannot fall is killed, and the <c>BridgeDieFX</c>/<c>BridgeDieOCL</c> lines go off at their delays after the death (each exactly once, in order; placed at the object's
/// position instead of at a bone or a random spot on the deck); a bridge healed out of the rubble state is alive again. The towers are linked by <see cref="SetTower"/>, as
/// <c>TerrainLogic</c> does when it builds a bridge: the simulation's terrain has no bridge layers, no bridge polygons and no repair scaffolding, so landmark bridges
/// (objects with a box geometry that is the deck) are the bridges that exist and nothing links towers to them yet. Not modeled: the damage-state effects of the terrain's bridge template
/// (<c>onBodyDamageStateChange</c>, <c>doAreaEffects</c>), passability of the pathfinding layer, the scaffolding of a repair (<c>createScaffolding</c>), and the fall of physics objects off a
/// destroyed deck (the reference sets <c>allowToFall</c>; only objects without physics are killed here).
/// </summary>
public sealed class BridgeBehavior(BridgeData data) : SimModule, IUpdateModule, IDieModule, IDamageModule
{
    public const int MaxTowers = 4;

    private long _nextWake = SimulationTiming.SleepForever;
    private readonly EntityId[] _towers = [EntityId.Invalid, EntityId.Invalid, EntityId.Invalid, EntityId.Invalid];
    private long _deathTick = -1;
    private long _firedUpTo = -1;

    public override string ModuleName => "BridgeBehavior";
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public BridgeData Data => data;
    public bool IsDestroyed => _deathTick >= 0;
    public EntityId TowerId(int type) => type is >= 0 and < MaxTowers ? _towers[type] : EntityId.Invalid;

    /// <summary><c>BridgeBehavior::setTower</c>.</summary>
    public void SetTower(int type, Entity? tower)
    {
        if (type is < 0 or >= MaxTowers) throw new ArgumentOutOfRangeException(nameof(type));
        _towers[type] = tower?.Id ?? EntityId.Invalid;
    }

    public void OnDamage(IWorldServices world, in DamageInfo info, Fixed actualDamageDealt) => Propagate(world, info, healing: false);

    public void OnHealing(IWorldServices world, in DamageInfo info) => Propagate(world, info, healing: true);

    public void OnBodyDamageStateChange(IWorldServices world, in DamageInfo info, int oldState, int newState)
    {
        // "check for coming back from the dead, if our new state is not the rubble state, we can't possibly be dead"
        if (newState != BodyDamageStates.Rubble) _deathTick = -1;
    }

    private void Propagate(IWorldServices world, in DamageInfo info, bool healing)
    {
        if (Entity.Body is not { } body || body.MaxHealth <= Fixed.Zero) return;
        var source = world.FindEntity(info.Source);
        if (source is not null && source.Template.IsKindOf("BRIDGE_TOWER")) return;
        var percentage = info.Amount / body.MaxHealth;
        foreach (var id in _towers)
        {
            if (world.FindEntity(id) is not { Body: { } towerBody } tower) continue;
            var amount = percentage * towerBody.MaxHealth;
            world.Damage(tower.Id, healing ? new DamageInfo(Entity.Id, amount, DamageTypes.Healing, DeathTypes.None, false) : new DamageInfo(Entity.Id, amount, info.DamageType, info.DeathType, false));
        }
    }

    public void OnDie(IWorldServices world, in DamageInfo info)
    {
        foreach (var id in _towers)
            if (world.FindEntity(id) is { } tower) world.Kill(tower.Id);
        HandleObjectsOnBridge(world);
        _deathTick = world.Tick;
        _firedUpTo = -1;
        world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepNone);
    }

    /// <summary><c>handleObjectsOnBridgeOnDie</c>: what stands on the deck (the object's own box) and cannot fall dies.</summary>
    private void HandleObjectsOnBridge(IWorldServices world)
    {
        var template = Entity.Template;
        var cos = FixedTrig.Cos(Entity.Angle);
        var sin = FixedTrig.Sin(Entity.Angle);
        var victims = new List<Entity>();
        foreach (var other in world.Entities)
        {
            if (other.Id == Entity.Id || other.DestroyRequested || other.EffectivelyDead || other.ContainedBy.IsValid) continue;
            if (other.Template.IsKindOf("BRIDGE") || other.Template.IsKindOf("BRIDGE_TOWER") || world.IsSignificantlyAboveTerrain(other) || other.Z < Entity.Z) continue;
            var dx = other.X - Entity.X;
            var dy = other.Y - Entity.Y;
            var along = (dx * cos + dy * sin).Abs();
            var across = (-dx * sin + dy * cos).Abs();
            if (along > template.GeometryMajorRadius || across > template.GeometryMinorRadius) continue;
            victims.Add(other);
        }
        victims.Sort(static (a, b) => a.Id.Value.CompareTo(b.Id.Value));
        foreach (var other in victims)
            if (!other.ModuleArray.OfType<PhysicsBehavior>().Any()) world.Kill(other.Id);
    }

    public long Update(IWorldServices world)
    {
        if (_deathTick < 0) return SimulationTiming.SleepForever;
        var deathTime = world.Tick - _deathTick;
        var last = 0L;
        foreach (var effect in data.DieFx)
        {
            last = Math.Max(last, effect.DelayTicks);
            if (effect.DelayTicks > _firedUpTo && effect.DelayTicks <= deathTime) world.EmitAt(SimEventKind.FxRequested, Entity.Id, EntityId.Invalid, effect.Name, 0, Entity.X, Entity.Y, Entity.Z);
        }
        foreach (var effect in data.DieOcl)
        {
            last = Math.Max(last, effect.DelayTicks);
            if (effect.DelayTicks > _firedUpTo && effect.DelayTicks <= deathTime) world.CreateFromList(Entity, effect.Name, EntityId.Invalid, false, Entity.X, Entity.Y, Entity.Z);
        }
        _firedUpTo = deathTime;
        return deathTime >= last ? SimulationTiming.SleepForever : SimulationTiming.SleepNone;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("deathTick", _deathTick);
        writer.Int("firedUpTo", _firedUpTo);
        for (var index = 0; index < MaxTowers; index++)
            if (_towers[index].IsValid) writer.UInt(writer.KeepsNames ? "tower" + index : "tower", _towers[index].Value);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _deathTick = reader.Int("deathTick");
        _firedUpTo = reader.Int("firedUpTo");
        for (var index = 0; index < MaxTowers; index++)
            _towers[index] = reader.Has("tower" + index) ? new EntityId(checked((uint)reader.UInt("tower" + index))) : EntityId.Invalid;
    }
}

/// <summary>
/// <c>BridgeTowerBehavior</c> (<c>BridgeTowerBehavior.cpp</c>): one of the four corner posts of a bridge. Damage or healing to a tower goes, in the same share of health, to the other towers and
/// the bridge object, unless it came from the bridge or a tower; the death of a tower kills the bridge (and through it every tower). The bridge is linked with <see cref="SetBridge"/>
/// (<c>TerrainLogic</c> does it when it builds the bridge; see <see cref="BridgeBehavior"/>).
/// </summary>
public sealed class BridgeTowerBehavior : SimModule, IDamageModule, IDieModule
{
    private EntityId _bridge = EntityId.Invalid;
    private int _type;

    public override string ModuleName => "BridgeTowerBehavior";
    public EntityId Bridge => _bridge;
    public int TowerType => _type;

    public void SetBridge(Entity? bridge) => _bridge = bridge?.Id ?? EntityId.Invalid;
    public void SetTowerType(int type) => _type = type;

    public void OnDamage(IWorldServices world, in DamageInfo info, Fixed actualDamageDealt) => Propagate(world, info, healing: false);

    public void OnHealing(IWorldServices world, in DamageInfo info) => Propagate(world, info, healing: true);

    private void Propagate(IWorldServices world, in DamageInfo info, bool healing)
    {
        if (world.FindEntity(_bridge) is not { } bridge || Entity.Body is not { } body || body.MaxHealth <= Fixed.Zero) return;
        var link = bridge.ModuleArray.OfType<BridgeBehavior>().FirstOrDefault();
        if (link is null) return;
        var source = world.FindEntity(info.Source);
        if (source is not null && (source.Template.IsKindOf("BRIDGE") || source.Template.IsKindOf("BRIDGE_TOWER"))) return;
        var percentage = info.Amount / body.MaxHealth;
        for (var index = 0; index < BridgeBehavior.MaxTowers; index++)
        {
            if (world.FindEntity(link.TowerId(index)) is not { Body: { } towerBody } tower || tower.Id == Entity.Id) continue;
            var amount = percentage * towerBody.MaxHealth;
            world.Damage(tower.Id, healing ? new DamageInfo(Entity.Id, amount, DamageTypes.Healing, DeathTypes.None, false) : new DamageInfo(Entity.Id, amount, info.DamageType, info.DeathType, false));
        }
        if (bridge.Body is not { } bridgeBody) return;
        var bridgeAmount = percentage * bridgeBody.MaxHealth;
        world.Damage(bridge.Id, healing ? new DamageInfo(Entity.Id, bridgeAmount, DamageTypes.Healing, DeathTypes.None, false) : new DamageInfo(Entity.Id, bridgeAmount, info.DamageType, info.DeathType, false));
    }

    public void OnDie(IWorldServices world, in DamageInfo info)
    {
        if (world.FindEntity(_bridge) is { } bridge) world.Kill(bridge.Id);
    }

    public override void Describe(IStateWriter writer)
    {
        if (_bridge.IsValid) writer.UInt("bridge", _bridge.Value);
        if (_type != 0) writer.Int("type", _type);
    }

    public override void Load(IStateReader reader)
    {
        _bridge = reader.Has("bridge") ? new EntityId(checked((uint)reader.UInt("bridge"))) : EntityId.Invalid;
        _type = reader.Has("type") ? checked((int)reader.Int("type")) : 0;
    }
}

/// <summary>
/// <c>BridgeScaffoldBehavior</c> (<c>BridgeScaffoldBehavior.cpp</c>): a piece of repair scaffolding that rises from <c>createPos</c> to <c>riseToPos</c>, builds across to <c>buildPos</c>, and
/// the other way round (tear down, then sink and disappear). It moves towards the target of its current motion at the top speed of the motion (vertical or lateral, as set by
/// <see cref="SetSpeeds"/>) slowed to at least 8 % of it over the last quarter of the way, and on arrival goes on to the next motion (<c>RISE → BUILD_ACROSS → still</c>,
/// <c>TEAR_DOWN_ACROSS → SINK → destroyed</c>); <see cref="ReverseMotion"/> turns whatever it is doing round. <c>BridgeBehavior::createScaffolding</c> is what creates and drives
/// scaffolds in the reference and is not modeled (see <see cref="BridgeBehavior"/>), so these objects exist and move only when something sets them up.
/// </summary>
public sealed class BridgeScaffoldBehavior : SimModule, IUpdateModule
{
    /// <summary><c>ScaffoldTargetMotion</c>.</summary>
    public enum Motion : byte { Still, Rise, BuildAcross, TearDownAcross, Sink }

    private static readonly Fixed MinSpeedShare = Fixed.FromDecimal(0.08m);
    /// <summary><c>BridgeScaffoldBehavior.cpp</c>, <c>update</c>: <c>if( speed &lt; 0.001f ) speed = 0.001f</c> – a distance per logic frame of the original.</summary>
    private static readonly Fixed AbsoluteMinSpeedPerFrame = Fixed.FromDecimal(0.001m);
    /// <summary><c>BridgeScaffoldBehavior.cpp</c>, constructor: <c>m_lateralSpeed = 1.0f</c>, <c>m_verticalSpeed = 1.0f</c> – a distance per logic frame of the original.</summary>
    private static readonly Fixed DefaultSpeedPerFrame = Fixed.One;

    private long _nextWake = SimulationTiming.SleepForever;
    private Motion _motion;
    private (Fixed X, Fixed Y, Fixed Z) _create;
    private (Fixed X, Fixed Y, Fixed Z) _riseTo;
    private (Fixed X, Fixed Y, Fixed Z) _build;
    private (Fixed X, Fixed Y, Fixed Z) _target;
    // Distances per tick (state). Until the object is spawned they hold the default per frame of the original; OnSpawn scales it to the rate of the world.
    private Fixed _lateralSpeed = DefaultSpeedPerFrame;
    private Fixed _verticalSpeed = DefaultSpeedPerFrame;
    // The step of the original (a speed that follows the way left, then the move) is taken once per logic frame of the original: the first tick of a frame
    // of the motion decides where the frame ends, every tick moves the scaffold an equal share of what is left, the last tick puts it there and, at the
    // target, goes on to the next motion. At rest (unwritten) between two frames, which is always at 30 Hz.
    private FramePhase _framePhase;
    private (Fixed X, Fixed Y, Fixed Z) _frameEnd;

    public override string ModuleName => "BridgeScaffoldBehavior";
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public Motion CurrentMotion => _motion;

    public override void OnSpawn(IWorldServices world)
        => (_lateralSpeed, _verticalSpeed) = (world.Time.PerOriginalFrameToPerTick(DefaultSpeedPerFrame), world.Time.PerOriginalFrameToPerTick(DefaultSpeedPerFrame));

    public void SetPositions((Fixed X, Fixed Y, Fixed Z) create, (Fixed X, Fixed Y, Fixed Z) riseTo, (Fixed X, Fixed Y, Fixed Z) build) => (_create, _riseTo, _build) = (create, riseTo, build);

    public void SetSpeeds(Fixed lateral, Fixed vertical) => (_lateralSpeed, _verticalSpeed) = (lateral, vertical);

    /// <summary><c>setMotion</c>: picks the target of the motion and wakes the module.</summary>
    public void SetMotion(IWorldServices world, Motion motion)
    {
        _motion = motion;
        // A new motion starts a frame of its own from where the scaffold is.
        _framePhase.Reset();
        _target = motion switch
        {
            Motion.Rise or Motion.TearDownAcross => _riseTo,
            Motion.BuildAcross => _build,
            Motion.Sink => _create,
            _ => _target,
        };
        if (motion != Motion.Still) world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepNone);
    }

    /// <summary><c>reverseMotion</c>.</summary>
    public void ReverseMotion(IWorldServices world) => SetMotion(world, _motion switch
    {
        Motion.Still => Motion.TearDownAcross,
        Motion.Rise => Motion.Sink,
        Motion.BuildAcross => Motion.TearDownAcross,
        Motion.TearDownAcross => Motion.BuildAcross,
        _ => Motion.Rise,
    });

    public long Update(IWorldServices world)
    {
        if (_motion == Motion.Still) return SimulationTiming.SleepForever;
        var ticks = world.Time.TicksPerOriginalFrame;
        var subTick = _framePhase.Next(world.Time);
        if (subTick == TickOrder.Next) _frameEnd = FrameEnd(world);
        if (subTick < ticks)
        {
            // An equal share of what is left for each tick left: a straight line to the place the frame ends at.
            var left = ticks - subTick + TickOrder.Next;
            Entity.X += Fixed.FromRaw((_frameEnd.X - Entity.X).Raw / left);
            Entity.Y += Fixed.FromRaw((_frameEnd.Y - Entity.Y).Raw / left);
            Entity.Z += Fixed.FromRaw((_frameEnd.Z - Entity.Z).Raw / left);
            return SimulationTiming.SleepNone;
        }
        // "Would the step pass the target? Then the target is reached": FrameEnd returns the target itself in that case, and only then.
        var (newX, newY, newZ) = _frameEnd;
        if (_frameEnd == _target)
        {
            switch (_motion)
            {
                case Motion.Rise: SetMotion(world, Motion.BuildAcross); break;
                case Motion.BuildAcross: _motion = Motion.Still; break;
                case Motion.TearDownAcross: SetMotion(world, Motion.Sink); break;
                case Motion.Sink: world.RequestDestroy(Entity.Id); break;
            }
        }
        Entity.X = newX;
        Entity.Y = newY;
        Entity.Z = newZ;
        return _motion == Motion.Still ? SimulationTiming.SleepForever : SimulationTiming.SleepNone;
    }

    /// <summary>One step of <c>BridgeScaffoldBehavior::update</c> from where the scaffold is: where it is one logic frame of the original later (the target, if the step would pass it).</summary>
    private (Fixed X, Fixed Y, Fixed Z) FrameEnd(IWorldServices world)
    {
        var (dirX, dirY, dirZ) = (_target.X - Entity.X, _target.Y - Entity.Y, _target.Z - Entity.Z);
        var dirLength = Fixed.Sqrt(dirX * dirX + dirY * dirY + dirZ * dirZ);
        var (unitX, unitY, unitZ) = dirLength > Fixed.Zero ? (dirX / dirLength, dirY / dirLength, dirZ / dirLength) : (Fixed.Zero, Fixed.Zero, Fixed.Zero);

        var (topPerTick, start, end) = _motion switch
        {
            Motion.Rise => (_verticalSpeed, _create, _riseTo),
            Motion.Sink => (_verticalSpeed, _riseTo, _create),
            Motion.BuildAcross => (_lateralSpeed, _riseTo, _build),
            _ => (_lateralSpeed, _build, _riseTo),
        };
        // The speeds of the state are distances per tick; the step is the original's, a distance per frame.
        var top = world.Time.PerTickAsPerOriginalFrame(topPerTick);
        // "adjust speed so it's slower at the end of motion": a quarter of the whole way is the ramp.
        var (sx, sy, sz) = (end.X - start.X, end.Y - start.Y, end.Z - start.Z);
        var totalDistance = Fixed.Sqrt(sx * sx + sy * sy + sz * sz) * Fixed.FromDecimal(0.25m);
        var (rx, ry, rz) = (end.X - Entity.X, end.Y - Entity.Y, end.Z - Entity.Z);
        var ourDistance = Fixed.Sqrt(rx * rx + ry * ry + rz * rz);
        var speed = totalDistance > Fixed.Zero ? ourDistance / totalDistance * top : top;
        var minSpeed = top * MinSpeedShare;
        if (speed < minSpeed) speed = minSpeed;
        if (speed > top) speed = top;
        if (speed < AbsoluteMinSpeedPerFrame) speed = AbsoluteMinSpeedPerFrame;

        var (newX, newY, newZ) = (Entity.X + unitX * speed, Entity.Y + unitY * speed, Entity.Z + unitZ * speed);
        // Would the step pass the target? Then the target is reached.
        var tooFar = (_target.X - newX) * dirX + (_target.Y - newY) * dirY + (_target.Z - newZ) * dirZ;
        return tooFar <= Fixed.Zero ? _target : (newX, newY, newZ);
    }

    private static void DescribePoint(IStateWriter writer, string name, (Fixed X, Fixed Y, Fixed Z) point)
    {
        writer.BeginObject(name);
        writer.Fixed("x", point.X);
        writer.Fixed("y", point.Y);
        writer.Fixed("z", point.Z);
        writer.EndObject();
    }

    private static (Fixed, Fixed, Fixed) LoadPoint(IStateReader reader, string name)
    {
        var item = reader.Object(name);
        return (item.Fixed("x"), item.Fixed("y"), item.Fixed("z"));
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("motion", (int)_motion);
        DescribePoint(writer, "create", _create);
        DescribePoint(writer, "riseTo", _riseTo);
        DescribePoint(writer, "build", _build);
        DescribePoint(writer, "target", _target);
        writer.Fixed("lateralSpeed", _lateralSpeed);
        writer.Fixed("verticalSpeed", _verticalSpeed);
        if (_framePhase.AtBoundary) return;
        _framePhase.Describe(writer, "framePhase");
        DescribePoint(writer, "frameEnd", _frameEnd);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        var motion = reader.Int("motion");
        if (motion < 0 || motion > (int)Motion.Sink) throw new InvalidDataException("Scaffold motion out of range.");
        _motion = (Motion)motion;
        _create = LoadPoint(reader, "create");
        _riseTo = LoadPoint(reader, "riseTo");
        _build = LoadPoint(reader, "build");
        _target = LoadPoint(reader, "target");
        _lateralSpeed = reader.Fixed("lateralSpeed");
        _verticalSpeed = reader.Fixed("verticalSpeed");
        _framePhase.Load(reader, "framePhase");
        _frameEnd = reader.Has("frameEnd") ? LoadPoint(reader, "frameEnd") : default;
    }
}
