using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>Two modules that act on the objects around their own: <c>PropagandaTowerBehavior</c> (the Propaganda Tower's aura) and <c>TensileFormationUpdate</c> (the avalanche's tied rocks).</summary>
internal static class FieldEffectModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<PropagandaTowerData>("PropagandaTowerBehavior", ModuleOwnerKind.Object, (configuration, _) => PropagandaTowerData.Parse(configuration), data => new PropagandaTowerBehavior(data)),
        new ModuleBinding<TensileFormationData>("TensileFormationUpdate", ModuleOwnerKind.Object, (configuration, _) => TensileFormationData.Parse(configuration), data => new TensileFormationUpdate(data)),
    ];
}

// ================================================================================================================ PropagandaTowerBehavior

/// <summary>
/// <c>PropagandaTowerBehaviorModuleData</c> (<c>PropagandaTowerBehavior.cpp</c>): <c>Radius</c> (default 1), <c>DelayBetweenUpdates</c> (frames, default 100), the two heal rates as ratios
/// of the maximum health per second (defaults 1% and 2%), the upgrade that makes the subliminal messages work and whether the tower affects itself. The pulse effects are presentation.
/// </summary>
public sealed record PropagandaTowerData(Fixed Radius, long ScanDelayTicks, Fixed HealPerSecond, Fixed UpgradedHealPerSecond, string UpgradeRequired, bool AffectsSelf) : ModuleData
{
    public static PropagandaTowerData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Real(configuration, "Radius", Fixed.One),
        ModuleConfigReader.Ticks(configuration, "DelayBetweenUpdates", 100),
        ModuleConfigReader.Real(configuration, "HealPercentEachSecond", Fixed.FromDecimal(0.01m)),
        ModuleConfigReader.Real(configuration, "UpgradedHealPercentEachSecond", Fixed.FromDecimal(0.02m)),
        ModuleConfigReader.Text(configuration, "UpgradeRequired"),
        ModuleConfigReader.Bool(configuration, "AffectsSelf", false));
}

/// <summary>
/// <c>PropagandaTowerBehavior.cpp</c>: every <c>DelayBetweenUpdates</c> frames the tower scans for its allies within <c>Radius</c> (centres on the ground plane; living, not
/// structures, not inside a container; itself only with <c>AffectsSelf</c>); every tick it then gives each of those that count for the score (<c>SCORE</c>, <c>SCORE_CREATE</c>,
/// <c>SCORE_DESTROY</c> or <c>MP_COUNT_FOR_VICTORY</c>) the <c>ENTHUSIASTIC</c> weapon bonus (when it has a weapon that does damage; with the player's <c>UpgradeRequired</c> also
/// <c>SUBLIMINAL</c>) and heals it by the (upgraded) rate of its maximum health per second. Healing does not stack: an object takes healing from one tower only, the one that
/// healed it last, until that tower has not healed it for a scan delay (<c>attemptHealingFromSoleBenefactor</c>; a tower that is gone releases its claims at once instead of letting
/// them run out). Objects that leave the area, and everything when the tower dies, is removed, is sold, is captured by nobody (neutral), or sits in a container that is itself contained
/// (an Overlord inside a Helix), lose both bonuses — also when another tower still covers them, as in the reference. A tower under construction does nothing; a tower that is
/// carried (the Overlord's, the Helix's) does not scan when it is a vehicle itself (the Emperor) or when its carrier is carried. The disabled-by-EMP/underpower check and the pulse FX
/// have no counterpart.
/// </summary>
public sealed class PropagandaTowerBehavior(PropagandaTowerData data) : SimModule, IUpdateModule, IDieModule
{
    private static readonly int EnthusiasticBit = SlaveSupport.WeaponBonusBit("ENTHUSIASTIC");
    private static readonly int SubliminalBit = SlaveSupport.WeaponBonusBit("SUBLIMINAL");

    private long _nextWake = -1;
    private long _lastScan;
    private readonly List<EntityId> _inside = [];
    private readonly List<(EntityId Target, long Expiry)> _claims = [];
    private readonly List<PropagandaTowerBehavior> _others = [];

    public override string ModuleName => "PropagandaTowerBehavior";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    /// <summary>The objects currently inside the area of influence (ascending id).</summary>
    public IReadOnlyList<EntityId> Inside => _inside;

    public override void OnSpawn(IWorldServices world) => _nextWake = world.Tick + SimulationTiming.SleepNone;

    public long Update(IWorldServices world)
    {
        if (Entity.HasStatus(ObjectStatusBits.UnderConstruction)) return SimulationTiming.SleepNone;
        if (Entity.HasStatus(ObjectStatusBits.Sold))
        {
            RemoveAllInfluence(world);
            return SimulationTiming.SleepForever;
        }
        if (Entity.EffectivelyDead) return SimulationTiming.SleepForever;
        // onCapture: "We don't function for the neutral player."
        if (Entity.Owner.IsSystem)
        {
            RemoveAllInfluence(world);
            return SimulationTiming.SleepNone;
        }
        var carrier = Entity.ContainedBy.IsValid ? world.FindEntity(Entity.ContainedBy) : null;
        if (carrier is not null && carrier.ContainedBy.IsValid)
        {
            // "If our container is contained, we turn the heck off."
            RemoveAllInfluence(world);
            return SimulationTiming.SleepNone;
        }

        if (world.Tick - _lastScan >= data.ScanDelayTicks)
        {
            DoScan(world, carrier);
            _lastScan = world.Tick;
        }

        var upgraded = HasUpgrade(world);
        _others.Clear();
        if (_inside.Count > 0)
            foreach (var candidate in world.Entities)
                if (candidate.Id != Entity.Id)
                    foreach (var module in candidate.ModuleArray)
                        if (module is PropagandaTowerBehavior tower && !ReferenceEquals(tower, this)) _others.Add(tower);
        for (var index = 0; index < _inside.Count;)
        {
            var target = world.FindEntity(_inside[index]);
            if (target is not null && !target.DestroyRequested && CountsForScore(target))
            {
                Give(world, target, upgraded);
                index++;
            }
            else _inside.RemoveAt(index);
        }
        _claims.RemoveAll(claim => world.Tick > claim.Expiry);
        return SimulationTiming.SleepNone;
    }

    private bool HasUpgrade(IWorldServices world)
    {
        if (data.UpgradeRequired.Length == 0 || !world.Upgrades.TryGet(data.UpgradeRequired, out var info)) return false;
        return world.PlayerUpgrades(Entity.Owner).TestForAny(world.Upgrades.MaskOf([info.Name]));
    }

    private static bool CountsForScore(Entity entity)
        => entity.Template.IsKindOf("SCORE") || entity.Template.IsKindOf("SCORE_CREATE") || entity.Template.IsKindOf("SCORE_DESTROY") || entity.Template.IsKindOf("MP_COUNT_FOR_VICTORY");

    /// <summary><c>doScan</c>: the new area of influence; objects that were inside and are not any more lose the bonuses.</summary>
    private void DoScan(IWorldServices world, Entity? carrier)
    {
        if (carrier is not null)
        {
            // "proptowers that are riding IN things cannot do their scan" (the Emperor in a Helix), nor those riding ON things that ride in things.
            if (Entity.Template.IsKindOf("VEHICLE") && !Entity.Template.IsKindOf("PORTABLE_STRUCTURE")) return;
            if (carrier.ContainedBy.IsValid) return;
        }
        var found = new List<Entity>();
        var reach = data.Radius * data.Radius;
        foreach (var candidate in world.Entities)
        {
            if (candidate.EffectivelyDead || candidate.DestroyRequested || candidate.ContainedBy.IsValid || candidate.Template.IsKindOf("STRUCTURE")) continue;
            if (candidate.Id == Entity.Id && !data.AffectsSelf) continue;
            if (candidate.Id != Entity.Id && world.RelationshipOf(Entity.Owner, candidate.Owner) != Relationship.Allies) continue;
            if (SlaveSupport.CenterDistanceSquared2D(Entity, candidate) > reach) continue;
            found.Add(candidate);
        }
        found.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));
        foreach (var previous in _inside)
            if (found.TrueForAll(candidate => candidate.Id != previous) && world.FindEntity(previous) is { } gone) Take(world, gone);
        _inside.Clear();
        foreach (var candidate in found) _inside.Add(candidate.Id);
    }

    /// <summary><c>effectLogic(giving)</c>.</summary>
    private void Give(IWorldServices world, Entity target, bool upgraded)
    {
        if (HasDamageWeapon(world, target))
        {
            var bonus = target.WeaponBonusConditions | (1u << EnthusiasticBit);
            if (upgraded) bonus |= 1u << SubliminalBit;
            if (bonus != target.WeaponBonusConditions)
            {
                target.WeaponBonusConditions = bonus;
                world.MarkDirty(target);
            }
        }
        if (target.Body is not { } body) return;
        var amount = (upgraded ? data.UpgradedHealPerSecond : data.HealPerSecond) / world.Time.TicksPerSecondFixed * body.MaxHealth;
        // Healing does not stack: another tower that healed this object recently keeps it.
        foreach (var other in _others)
            if (other.HoldsClaim(target.Id, world.Tick)) return;
        SetClaim(target.Id, world.Tick + data.ScanDelayTicks);
        world.Damage(target.Id, new DamageInfo(Entity.Id, amount, DamageTypes.Healing, DeathTypes.None, false));
    }

    private bool HoldsClaim(EntityId target, long now)
    {
        foreach (var (id, expiry) in _claims)
            if (id == target) return now <= expiry;
        return false;
    }

    private void SetClaim(EntityId target, long expiry)
    {
        for (var index = 0; index < _claims.Count; index++)
            if (_claims[index].Target == target)
            {
                _claims[index] = (target, expiry);
                return;
            }
        var position = _claims.FindIndex(claim => claim.Target.Value > target.Value);
        _claims.Insert(position < 0 ? _claims.Count : position, (target, expiry));
    }

    /// <summary><c>Object::hasAnyDamageWeapon</c>: some slot of the target's active weapon set deals damage.</summary>
    private static bool HasDamageWeapon(IWorldServices world, Entity target) => World.SimulationWorld.HasAnyDamageWeapon(target, world.Weapons);

    /// <summary><c>effectLogic(not giving)</c>: both weapon bonuses go.</summary>
    private static void Take(IWorldServices world, Entity target)
    {
        var bonus = target.WeaponBonusConditions & ~((1u << EnthusiasticBit) | (1u << SubliminalBit));
        if (bonus == target.WeaponBonusConditions) return;
        target.WeaponBonusConditions = bonus;
        world.MarkDirty(target);
    }

    /// <summary><c>removeAllInfluence</c>.</summary>
    private void RemoveAllInfluence(IWorldServices world)
    {
        foreach (var id in _inside)
            if (world.FindEntity(id) is { } target) Take(world, target);
        _inside.Clear();
        _claims.Clear();
    }

    public void OnDie(IWorldServices world, in DamageInfo info) => RemoveAllInfluence(world);

    public override void OnRemove(IWorldServices world) => RemoveAllInfluence(world);

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("lastScan", _lastScan);
        writer.BeginArray("inside");
        foreach (var id in _inside)
        {
            writer.BeginObject(null);
            writer.UInt("id", id.Value);
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("claims");
        foreach (var (target, expiry) in _claims)
        {
            writer.BeginObject(null);
            writer.UInt("id", target.Value);
            writer.Int("expiry", expiry);
            writer.EndObject();
        }
        writer.EndArray();
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _lastScan = reader.Int("lastScan");
        _inside.Clear();
        foreach (var entry in reader.Array("inside")) _inside.Add(new EntityId(checked((uint)entry.UInt("id"))));
        _claims.Clear();
        foreach (var entry in reader.Array("claims")) _claims.Add((new EntityId(checked((uint)entry.UInt("id"))), entry.Int("expiry")));
    }
}

// =============================================================================================================== TensileFormationUpdate

/// <summary><c>TensileFormationUpdateModuleData</c> (<c>TensileFormationUpdate.cpp</c>): whether the formation already moves (<c>Enabled</c>, default no); the crack sound is presentation.</summary>
public sealed record TensileFormationData(bool Enabled) : ModuleData
{
    public static TensileFormationData Parse(RatedModuleConfiguration configuration) => new(ModuleConfigReader.Bool(configuration, "Enabled", false));
}

/// <summary>
/// <c>TensileFormationUpdate.cpp</c> (the avalanche): the rocks of a snow slope are tied together, sit still until one of them is hurt, then all slide down. On its first update a rock
/// finds the nearest other rocks (up to four links: the record-breaking nearest ones in id order, with the offset to each), and ties itself to them. Dormant, it checks every 30
/// frames whether it has been damaged (body state damaged or worse) or dislodged by a neighbour; then it moves for 300 frames: it accelerates down the terrain slope
/// (<c>inertia += slope · (0.3 + steepness)</c>, friction 0.95), rests on the ground, is pulled 7% per link towards where its neighbour would hold it, and — once a second — dislodges the
/// formation members within 100 units and its links. After 300 frames it stays where it is. The terrain normal comes from central differences of the height field. Not simulated: the
/// path-finding wall of the dormant rock, toppling the shrubbery it slides over, the rubble body state at the end (a damage state cannot be forced), the crack sound and the model flags.
/// </summary>
public sealed class TensileFormationUpdate(TensileFormationData data) : SimModule, IUpdateModule
{
    private const int MaxLinks = 4;
    /// <summary><c>TensileFormationUpdate.cpp</c>, <c>TensileFormationUpdate::update</c>: <c>if (++m_life &gt; 300)</c> – the slide lasts 300 logic frames of the original.</summary>
    private const long LifetimeFrames = 300;
    /// <summary>The same function: <c>return UPDATE_SLEEP(30)</c> – a dormant rock checks for damage every 30 logic frames of the original.</summary>
    private const long DormantCheckFrames = 30;
    /// <summary>The same function: <c>if ( m_life%30 == 29 )</c> – period and phase of passing the dislodgement on, in logic frames of the original.</summary>
    private const long PropagatePeriodFrames = 30;
    private const long PropagatePhaseFrames = 29;
    private static readonly Fixed Friction = Fixed.FromDecimal(0.95m);
    private static readonly Fixed NewShare = Fixed.FromDecimal(0.93m);
    private static readonly Fixed LinkShare = Fixed.FromDecimal(0.07m);
    private static readonly Fixed BaseSlope = Fixed.FromDecimal(0.3m);
    private static readonly Fixed GridStep = Fixed.FromInt(10);
    private static readonly Fixed LinkReach = Fixed.FromInt(1000);
    private static readonly Fixed DislodgeReach = Fixed.FromInt(100);

    private readonly List<(EntityId Id, Fixed Dx, Fixed Dy, Fixed Dz)> _links = [];
    private long _nextWake = -1;
    private bool _enabled = data.Enabled;
    private bool _linksInitialised;
    private bool _dislodged;
    private int _life;
    private Fixed _inertiaX;
    private Fixed _inertiaY;
    private Fixed _lowestSlide = Fixed.FromInt(255);

    public override string ModuleName => "TensileFormationUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public bool Enabled => _enabled;

    /// <summary>
    /// <c>m_life</c>: the updates (ticks) the formation has moved so far (read by the presentation: the reference shows <c>FREEFALL</c> while it is below 100 frames and the rock drops –
    /// a reader must compare with <c>Time.OriginalFrames(100)</c>).
    /// </summary>
    public int Life => _life;

    public override void OnSpawn(IWorldServices world) => _nextWake = world.Tick + SimulationTiming.SleepNone;

    /// <summary>A neighbour (or a hit on this rock) tells the formation to start sliding; takes effect at the next dormant check.</summary>
    public void Dislodge() => _dislodged = true;

    public long Update(IWorldServices world)
    {
        if (!_linksInitialised) InitLinks(world);
        if (!_enabled)
        {
            var hurt = _dislodged || (Entity.Body is ActiveBody { DamageState: >= BodyDamageStates.Damaged });
            if (!hurt) return world.Time.OriginalFrames(DormantCheckFrames);
            _enabled = true;
        }
        // _life counts updates, one per tick: lifetime, period and phase are frames of the original and scale together.
        if (++_life > world.Time.OriginalFrames(LifetimeFrames)) return SimulationTiming.SleepForever;
        if (_life % world.Time.OriginalFrames(PropagatePeriodFrames) == world.Time.OriginalFrames(PropagatePhaseFrames)) PropagateDislodgement(world);

        // Apply physics: flow down the slope with friction.
        var (nx, ny, nz) = GroundNormal(world, Entity.X, Entity.Y);
        var scale = BaseSlope + (Fixed.One - nz);
        _inertiaX = (_inertiaX + nx * scale) * Friction;
        _inertiaY = (_inertiaY + ny * scale) * Friction;
        var x = Entity.X + _inertiaX;
        var y = Entity.Y + _inertiaY;
        var z = world.GroundHeightAt(x, y);

        // Apply tensors: pulled towards the place each neighbour holds this rock at.
        foreach (var (id, dx, dy, _) in _links)
        {
            if (world.FindEntity(id) is not { } other) continue;
            x = x * NewShare + (other.X - dx) * LinkShare;
            y = y * NewShare + (other.Y - dy) * LinkShare;
            z = Fixed.Min(_lowestSlide, world.GroundHeightAt(x, y));
        }
        _lowestSlide = z;
        Entity.X = x;
        Entity.Y = y;
        Entity.Z = z;
        return SimulationTiming.SleepNone;
    }

    /// <summary>The links of <c>initLinks</c>: each formation member closer than every earlier candidate within reach is pushed in front of the others (four are kept).</summary>
    private void InitLinks(IWorldServices world)
    {
        _linksInitialised = true;
        var closest = Fixed.FromInt(99999);
        foreach (var other in world.Entities.OrderBy(value => value.Id.Value))
        {
            if (other.Id == Entity.Id || other.DestroyRequested || TensileOf(other) is null) continue;
            var dx = other.X - Entity.X;
            var dy = other.Y - Entity.Y;
            var dz = other.Z - Entity.Z;
            var distance = Fixed.Sqrt(dx * dx + dy * dy + dz * dz);
            if (distance > LinkReach || distance >= closest) continue;
            closest = distance;
            _links.Insert(0, (other.Id, dx, dy, dz));
            if (_links.Count > MaxLinks) _links.RemoveAt(MaxLinks);
        }
        Entity.Angle = world.LogicRandom.Range(-FixedTrig.Pi, FixedTrig.Pi);
    }

    private static TensileFormationUpdate? TensileOf(Entity entity) => entity.ModuleArray.OfType<TensileFormationUpdate>().FirstOrDefault();

    /// <summary><c>propagateDislodgement</c>: the formation members within 100 units and the links are told the formation is on the move.</summary>
    private void PropagateDislodgement(IWorldServices world)
    {
        foreach (var other in world.Entities)
        {
            if (other.Id == Entity.Id || TensileOf(other) is not { } tensile) continue;
            var dx = other.X - Entity.X;
            var dy = other.Y - Entity.Y;
            var dz = other.Z - Entity.Z;
            var reach = DislodgeReach + Entity.Template.BoundingSphereRadius + other.Template.BoundingSphereRadius;
            if (dx * dx + dy * dy + dz * dz <= reach * reach) tensile.Dislodge();
        }
        foreach (var (id, _, _, _) in _links)
            if (world.FindEntity(id) is { } linked && TensileOf(linked) is { } tensile) tensile.Dislodge();
    }

    /// <summary>The unit normal of the height field at (x, y) (<c>getGroundHeight(x, y, &amp;normal)</c>): central differences over one map cell.</summary>
    private static (Fixed X, Fixed Y, Fixed Z) GroundNormal(IWorldServices world, Fixed x, Fixed y)
    {
        var slopeX = (world.GroundHeightAt(x + GridStep, y) - world.GroundHeightAt(x - GridStep, y)) / (GridStep * 2L);
        var slopeY = (world.GroundHeightAt(x, y + GridStep) - world.GroundHeightAt(x, y - GridStep)) / (GridStep * 2L);
        var length = Fixed.Sqrt(slopeX * slopeX + slopeY * slopeY + Fixed.One);
        return (-slopeX / length, -slopeY / length, Fixed.One / length);
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Bool("enabled", _enabled);
        writer.Bool("linksInitialised", _linksInitialised);
        writer.Bool("dislodged", _dislodged);
        writer.Int("life", _life);
        writer.Fixed("inertiaX", _inertiaX);
        writer.Fixed("inertiaY", _inertiaY);
        writer.Fixed("lowestSlide", _lowestSlide);
        writer.BeginArray("links");
        foreach (var (id, dx, dy, dz) in _links)
        {
            writer.BeginObject(null);
            writer.UInt("id", id.Value);
            writer.Fixed("dx", dx);
            writer.Fixed("dy", dy);
            writer.Fixed("dz", dz);
            writer.EndObject();
        }
        writer.EndArray();
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _enabled = reader.Bool("enabled");
        _linksInitialised = reader.Bool("linksInitialised");
        _dislodged = reader.Bool("dislodged");
        _life = checked((int)reader.Int("life"));
        _inertiaX = reader.Fixed("inertiaX");
        _inertiaY = reader.Fixed("inertiaY");
        _lowestSlide = reader.Fixed("lowestSlide");
        _links.Clear();
        foreach (var entry in reader.Array("links")) _links.Add((new EntityId(checked((uint)entry.UInt("id"))), entry.Fixed("dx"), entry.Fixed("dy"), entry.Fixed("dz")));
    }
}
