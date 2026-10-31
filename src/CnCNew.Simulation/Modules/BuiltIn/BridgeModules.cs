using System.Collections.Immutable;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
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
/// (<c>onDamage</c>/<c>onHealing</c>: the percentage of the bridge's maximum health, unless the hit came from a tower). A change of damage state plays the transition effects of the <c>Bridge</c>
/// block on the deck and has the terrain look at its bridges (<c>onBodyDamageStateChange</c>, <see cref="SimulationWorld.UpdateBridgeDamageStates"/>: the layer closes or opens, what is on a deck that
/// became rubble dies). When the bridge is destroyed (<c>onDie</c>) its towers are killed, what is still on its layer falls or dies, and the <c>BridgeDieFX</c>/<c>BridgeDieOCL</c> lines go off at their
/// delays after the death (each exactly once, in order; a line without a bone at a random spot of the deck, <c>ParentObject</c> at the object; a named bone at the object as well, because the bones
/// of a landmark's model are not in the world's tables). A bridge healed out of the rubble state is alive again. Repair works with scaffolding: <see cref="CreateScaffolding"/> tiles scaffold objects
/// and their supports in from both ends of the bridge, and while they stand the bridge cannot be used.
/// </summary>
public sealed class BridgeBehavior(BridgeData data) : SimModule, IUpdateModule, IDieModule, IDamageModule
{
    public const int MaxTowers = 4;

    /// <summary><c>BridgeBehavior::setScaffoldData</c>: <c>fudge</c>, how much further than its own height a scaffold starts below where it rises to.</summary>
    private static readonly Fixed SunkenFudge = Fixed.FromInt(8);

    /// <summary><c>createScaffolding</c>: "all scaffold objects *have* to move some distance".</summary>
    private static readonly Fixed LeastMove = Fixed.FromFraction(1, 10);

    private long _nextWake = SimulationTiming.SleepForever;
    private readonly EntityId[] _towers = [EntityId.Invalid, EntityId.Invalid, EntityId.Invalid, EntityId.Invalid];
    private long _deathTick = -1;
    private long _firedUpTo = -1;
    private bool _scaffoldPresent;
    private readonly List<EntityId> _scaffolds = [];

    public override string ModuleName => "BridgeBehavior";
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public BridgeData Data => data;
    public bool IsDestroyed => _deathTick >= 0;
    public EntityId TowerId(int type) => type is >= 0 and < MaxTowers ? _towers[type] : EntityId.Invalid;

    /// <summary><c>isScaffoldPresent</c>.</summary>
    public bool IsScaffoldPresent => _scaffoldPresent;

    /// <summary>The scaffold objects, those of the supports among them.</summary>
    public IReadOnlyList<EntityId> Scaffolds => _scaffolds;

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
        if (world.BridgeOf(Entity.Id) is not { } bridge) return;
        if (world.Templates.Bridges.TryGet(bridge.TemplateName, out var template))
        {
            var transition = (oldState > newState ? template.RepairedTo : template.DamagedTo)[newState];
            for (var effect = 0; effect < BridgeCatalog.MaxBodyEffects; effect++) DoAreaEffects(world, template, bridge, transition.CreationLists[effect], transition.Effects[effect]);
        }
        world.UpdateBridgeDamageStates();
    }

    /// <summary><c>doAreaEffects</c>: <c>NumFXPerType</c> times the FX list and the creation list, each at a spot of its own.</summary>
    private void DoAreaEffects(IWorldServices world, BridgeTemplate template, TerrainBridge bridge, string creationList, string effect)
    {
        if (creationList.Length == 0 && effect.Length == 0) return;
        for (var index = 0; index < template.EffectsPerType; index++)
        {
            if (effect.Length > 0)
            {
                var (x, y, z) = RandomSurfacePosition(world, template, bridge);
                world.EmitAt(SimEventKind.FxRequested, Entity.Id, EntityId.Invalid, effect, 0, x, y, z);
            }
            if (creationList.Length > 0)
            {
                var (x, y, z) = RandomSurfacePosition(world, template, bridge);
                world.CreateFromList(Entity, creationList, EntityId.Invalid, false, x, y, z);
            }
        }
    }

    /// <summary><c>getRandomSurfacePosition</c>: a share along the bridge, a share across it, and a height over the deck up to <c>TransitionEffectsHeight</c>, drawn in that order.</summary>
    private static (Fixed X, Fixed Y, Fixed Z) RandomSurfacePosition(IWorldServices world, BridgeTemplate? template, TerrainBridge bridge)
    {
        var (from, along, across) = (bridge.FromLeft, bridge.ToLeft, bridge.FromRight);
        var first = world.LogicRandom.Range(Fixed.Zero, Fixed.One);
        var second = world.LogicRandom.Range(Fixed.Zero, Fixed.One);
        var x = (along.X - from.X) * first + (across.X - from.X) * second + from.X;
        var y = (along.Y - from.Y) * first + (across.Y - from.Y) * second + from.Y;
        var z = (along.Z - from.Z) * first + (across.Z - from.Z) * second + from.Z;
        return (x, y, z + world.LogicRandom.Range(Fixed.Zero, template?.TransitionEffectsHeight ?? Fixed.Zero));
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

    /// <summary>
    /// <c>handleObjectsOnBridgeOnDie</c>: what stands on the deck's layer inside its outline, not above it and not lower than the deck's lowest corner, leaves the layer; with a physics module it falls,
    /// without one it dies.
    /// </summary>
    private void HandleObjectsOnBridge(IWorldServices world)
    {
        if (world.BridgeOf(Entity.Id) is not { Layer: > 0 } bridge) return;
        var lowest = Fixed.Min(Fixed.Min(bridge.FromLeft.Z, bridge.FromRight.Z), Fixed.Min(bridge.ToLeft.Z, bridge.ToRight.Z));
        var victims = new List<Entity>();
        foreach (var other in world.Entities)
        {
            if (other.Id == Entity.Id || other.Layer != bridge.Layer || other.DestroyRequested || other.ContainedBy.IsValid) continue;
            if (other.Template.IsKindOf("BRIDGE") || other.Template.IsKindOf("BRIDGE_TOWER") || world.IsSignificantlyAboveTerrain(other) || other.Z < lowest) continue;
            if (bridge.IsPointOnBridge(other.X, other.Y)) victims.Add(other);
        }
        victims.Sort(static (a, b) => a.Id.Value.CompareTo(b.Id.Value));
        foreach (var other in victims)
        {
            world.SetLayer(other, SimulationWorld.GroundLayer);
            if (other.ModuleArray.OfType<PhysicsBehavior>().FirstOrDefault() is { } physics) physics.StartFall(world, Fixed.Zero, Fixed.Zero);
            else world.Kill(other.Id);
        }
    }

    /// <summary>
    /// <c>BridgeBehavior::createScaffolding</c>: scaffold objects tiled across the bridge from both of its ends, one object's length apart, each rising out of the ground below its end and then
    /// running to its place, a stack of supports below each down to the ground. While they stand the layer is closed.
    /// </summary>
    public void CreateScaffolding(IWorldServices world)
    {
        if (_scaffoldPresent || world.BridgeOf(Entity.Id) is not { } bridge || !world.Templates.Bridges.TryGet(bridge.TemplateName, out var template)) return;
        if (!world.Templates.TryGet(template.ScaffoldObject, out var scaffold) || !world.Templates.TryGet(template.ScaffoldSupportObject, out var support)) return;
        var center = new Point(Entity.X, Entity.Y, Entity.Z);
        var spacing = scaffold.GeometryMajorRadius * Fixed.FromInt(2);
        var (scaffoldHeight, supportHeight) = (scaffold.GeometryHeight, support.GeometryHeight);
        var two = Fixed.FromInt(2);
        var leftStart = new Point(bridge.FromRight.X + (bridge.FromLeft.X - bridge.FromRight.X) / two, bridge.FromRight.Y + (bridge.FromLeft.Y - bridge.FromRight.Y) / two, bridge.FromRight.Z + (bridge.FromLeft.Z - bridge.FromRight.Z) / two);
        var rightStart = new Point(bridge.ToRight.X + (bridge.ToLeft.X - bridge.ToRight.X) / two, bridge.ToRight.Y + (bridge.ToLeft.Y - bridge.ToRight.Y) / two, bridge.ToRight.Z + (bridge.ToLeft.Z - bridge.ToRight.Z) / two);
        var leftAngle = FixedTrig.Atan2(rightStart.Y - leftStart.Y, rightStart.X - leftStart.X);
        var rightAngle = leftAngle + FixedTrig.TwoPi;
        var (dx, dy, dz) = (rightStart.X - leftStart.X, rightStart.Y - leftStart.Y, rightStart.Z - leftStart.Z);
        var distance = Fixed.Sqrt(dx * dx + dy * dy + dz * dz);
        var numObjects = (spacing > Fixed.Zero ? (distance / spacing).Ceiling() : 0) + 1;
        var numIterations = (numObjects + 1) / 2;
        var (unitX, unitY, unitZ) = distance > Fixed.Zero ? (dx / distance, dy / distance, dz / distance) : (Fixed.Zero, Fixed.Zero, Fixed.Zero);
        var created = 0L;
        for (var index = 0L; index < numIterations; index++)
        {
            Tile(leftStart, unitX, unitY, unitZ, leftAngle, index);
            created++;
            if (created >= numObjects) continue;
            Tile(rightStart, -unitX, -unitY, -unitZ, rightAngle, index);
            created++;
        }

        void Tile(Point riseTo, Fixed x, Fixed y, Fixed z, Fixed angle, long place)
        {
            var share = spacing * place;
            var destination = new Point(x * share + riseTo.X + LeastMove, y * share + riseTo.Y, z * share + riseTo.Z);
            Place(scaffold, angle, scaffoldHeight, riseTo, destination, center);
            // "create support object layers under the scaffold object, this object mirrors the scaffold object except is on a lower layer"
            var (supportRise, supportGoal, supportCenter) = (riseTo, destination, center);
            for (var offset = riseTo.Z; offset >= Fixed.Zero && supportHeight > Fixed.Zero; offset -= supportHeight)
            {
                supportRise = supportRise with { Z = supportRise.Z - supportHeight };
                supportGoal = supportGoal with { Z = supportGoal.Z - supportHeight };
                supportCenter = supportCenter with { Z = supportCenter.Z - supportHeight };
                Place(support, angle, supportHeight, supportRise, supportGoal, supportCenter);
            }
        }

        // setScaffoldData: every object starts sunken below where it rises to, and all of them meet in the middle at the same time.
        void Place(ObjectTemplate what, Fixed angle, Fixed sunkenHeight, Point riseTo, Point build, Point middle)
        {
            var sunken = riseTo with { Z = riseTo.Z - sunkenHeight - SunkenFudge };
            var entity = world.CreateNow(what, Entity.Owner, sunken.X, sunken.Y, sunken.Z, angle);
            var behavior = entity.ModuleArray.OfType<BridgeScaffoldBehavior>().First();
            behavior.SetPositions((sunken.X, sunken.Y, sunken.Z), (riseTo.X, riseTo.Y, riseTo.Z), (build.X, build.Y, build.Z));
            var toBuild = Length(build.X - riseTo.X, build.Y - riseTo.Y, build.Z - riseTo.Z);
            var toMiddle = Length(middle.X - riseTo.X, middle.Y - riseTo.Y, middle.Z - riseTo.Z);
            behavior.SetSpeeds(toMiddle > Fixed.Zero ? data.LateralScaffoldSpeed * toBuild / toMiddle : data.LateralScaffoldSpeed, data.VerticalScaffoldSpeed);
            behavior.SetMotion(world, BridgeScaffoldBehavior.Motion.Rise);
            _scaffolds.Add(entity.Id);
        }

        _scaffoldPresent = true;
        world.SetBridgeUsable(bridge, false);
        world.MarkDirty(Entity);
    }

    private static Fixed Length(Fixed x, Fixed y, Fixed z) => Fixed.Sqrt(x * x + y * y + z * z);

    private readonly record struct Point(Fixed X, Fixed Y, Fixed Z);

    /// <summary><c>BridgeBehavior::removeScaffolding</c>: every scaffold turns round and goes; a bridge that is no rubble can be used again.</summary>
    public void RemoveScaffolding(IWorldServices world)
    {
        if (!_scaffoldPresent) return;
        foreach (var id in _scaffolds)
            if (world.FindEntity(id) is { } scaffold && scaffold.ModuleArray.OfType<BridgeScaffoldBehavior>().FirstOrDefault() is { } behavior) behavior.ReverseMotion(world);
        _scaffolds.Clear();
        _scaffoldPresent = false;
        world.MarkDirty(Entity);
        if (Entity.Body is ActiveBody { DamageState: not BodyDamageStates.Rubble } && world.BridgeOf(Entity.Id) is { } bridge) world.SetBridgeUsable(bridge, true);
    }

    /// <summary><c>BridgeBehavior::isScaffoldInMotion</c>.</summary>
    public bool IsScaffoldInMotion(IWorldServices world)
    {
        foreach (var id in _scaffolds)
            if (world.FindEntity(id) is { } scaffold && scaffold.ModuleArray.OfType<BridgeScaffoldBehavior>().FirstOrDefault() is { CurrentMotion: not BridgeScaffoldBehavior.Motion.Still }) return true;
        return false;
    }

    public long Update(IWorldServices world)
    {
        if (_deathTick < 0) return SimulationTiming.SleepForever;
        var deathTime = world.Tick - _deathTick;
        var bridge = world.BridgeOf(Entity.Id);
        var template = bridge is not null && world.Templates.Bridges.TryGet(bridge.TemplateName, out var found) ? found : null;
        (Fixed X, Fixed Y, Fixed Z) Spot(BridgeEffect effect) => effect.Bone.Length > 0 || bridge is null ? (Entity.X, Entity.Y, Entity.Z) : RandomSurfacePosition(world, template, bridge);
        var last = 0L;
        foreach (var effect in data.DieFx)
        {
            last = Math.Max(last, effect.DelayTicks);
            if (effect.DelayTicks <= _firedUpTo || effect.DelayTicks > deathTime) continue;
            var (x, y, z) = Spot(effect);
            world.EmitAt(SimEventKind.FxRequested, Entity.Id, EntityId.Invalid, effect.Name, 0, x, y, z);
        }
        foreach (var effect in data.DieOcl)
        {
            last = Math.Max(last, effect.DelayTicks);
            if (effect.DelayTicks <= _firedUpTo || effect.DelayTicks > deathTime) continue;
            var (x, y, z) = Spot(effect);
            world.CreateFromList(Entity, effect.Name, EntityId.Invalid, false, x, y, z);
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
        if (!_scaffoldPresent && _scaffolds.Count == 0) return;
        writer.Bool("scaffoldPresent", _scaffoldPresent);
        writer.BeginArray("scaffolds");
        foreach (var id in _scaffolds)
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
        _deathTick = reader.Int("deathTick");
        _firedUpTo = reader.Int("firedUpTo");
        for (var index = 0; index < MaxTowers; index++)
            _towers[index] = reader.Has("tower" + index) ? new EntityId(checked((uint)reader.UInt("tower" + index))) : EntityId.Invalid;
        _scaffoldPresent = reader.Has("scaffoldPresent") && reader.Bool("scaffoldPresent");
        _scaffolds.Clear();
        if (reader.Has("scaffolds"))
            foreach (var item in reader.Array("scaffolds")) _scaffolds.Add(new EntityId(checked((uint)item.UInt("id"))));
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

    /// <summary><c>DozerAIUpdate::createBridgeScaffolding</c> and <c>isScaffoldInMotion</c>: scaffolding is put up over the tower's bridge if it has none; true when it stands still.</summary>
    public bool ScaffoldStands(IWorldServices world)
    {
        if (world.FindEntity(_bridge)?.ModuleArray.OfType<BridgeBehavior>().FirstOrDefault() is not { } bridge) return true;
        bridge.CreateScaffolding(world);
        return !bridge.IsScaffoldInMotion(world);
    }

    /// <summary><c>WorkerAIUpdate::removeBridgeScaffolding</c>: "when we're done repairing bridges, tell the scaffolding to go away".</summary>
    public void RemoveScaffolding(IWorldServices world) => world.FindEntity(_bridge)?.ModuleArray.OfType<BridgeBehavior>().FirstOrDefault()?.RemoveScaffolding(world);

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
/// <c>TEAR_DOWN_ACROSS → SINK → destroyed</c>); <see cref="ReverseMotion"/> turns whatever it is doing round. <see cref="BridgeBehavior.CreateScaffolding"/> creates and drives them.
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
