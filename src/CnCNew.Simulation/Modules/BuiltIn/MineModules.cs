using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

internal static class MineModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<DemoTrapData>("DemoTrapUpdate", ModuleOwnerKind.Object, (configuration, _) => DemoTrapData.Parse(configuration), data => new DemoTrapUpdate(data)),
        new ModuleBinding<MinefieldData>("MinefieldBehavior", ModuleOwnerKind.Object, (configuration, _) => MinefieldData.Parse(configuration), data => new MinefieldBehavior(data)),
    ];
}

// ================================================================================================================ DemoTrapUpdate

/// <summary>
/// <c>DemoTrapUpdate.cpp</c>'s fields: whether the trap starts in proximity mode (otherwise manual), the three weapon slots that mark its modes, the trigger radius, the kinds it ignores, the scan
/// rate (ticks between scans), whether an ally nearby still lets it go off, the weapon it fires on detonation and whether it goes off when destroyed.
/// </summary>
public sealed record DemoTrapData(bool DefaultProximityMode, int DetonationSlot, int ProximitySlot, int ManualSlot, Fixed TriggerRange, ImmutableArray<string> IgnoreKinds, long ScanTicks,
    bool FriendlyDetonation, string DetonationWeapon, bool DetonateWhenKilled) : ModuleData
{
    public static DemoTrapData Parse(RatedModuleConfiguration configuration)
    {
        int Slot(string name, int fallback) => ModuleConfigReader.Find(configuration, name)?.Value.Value is EnumValue value && NameTablesPhase5.WeaponSlot.TryIndexOf(value.Name, out var index) ? index : fallback;
        return new DemoTrapData(
            ModuleConfigReader.Bool(configuration, "DefaultProximityMode", false), Slot("DetonationWeaponSlot", 0), Slot("ProximityModeWeaponSlot", 0), Slot("ManualModeWeaponSlot", 0),
            ModuleConfigReader.Real(configuration, "TriggerDetonationRange", Fixed.Zero), ModuleConfigReader.Flags(configuration, "IgnoreTargetTypes")?.Names ?? [],
            ModuleConfigReader.Ticks(configuration, "ScanRate", 0), ModuleConfigReader.Bool(configuration, "AutoDetonationWithFriendsInvolved", false),
            ModuleConfigReader.Text(configuration, "DetonationWeapon"), ModuleConfigReader.Bool(configuration, "DetonateWhenKilled", false));
    }
}

/// <summary>
/// <c>DemoTrapUpdate::update</c>/<c>detonate</c>: a booby trap that goes off (fires <c>DetonationWeapon</c> where it stands, if it has one, and kills itself, so its die modules deliver the
/// explosion) when an enemy that is not airborne and not of an ignored kind comes within <c>TriggerDetonationRange</c> of its centre while it is in proximity mode, when it is told to (the
/// detonation mode), or, with <c>DetonateWhenKilled</c>, when it is destroyed; never while it is under construction or being sold. A friend (or the trap itself, which counts as one) in
/// range blocks the proximity trigger unless <c>AutoDetonationWithFriendsInvolved</c> is set. The reference tells the modes apart by which of the object's three weapons is selected (weapon
/// locks switched by command buttons): the simulation has one weapon per object and no slot switching, so the mode is the module's own state — <see cref="DemoTrapMode"/> — set from
/// <c>DefaultProximityMode</c> at creation and changed with <see cref="SetMode"/>; no command is wired to it. Scans happen every <c>ScanRate</c> ticks. Not modeled: a dozer that is
/// disarming it (weapon damage type <c>DISARM</c>) is not excused.
/// </summary>
public sealed class DemoTrapUpdate(DemoTrapData data) : SimModule, IUpdateModule
{
    /// <summary>The three weapon states the reference reads off the current weapon slot.</summary>
    public enum DemoTrapMode : byte { Proximity, Manual, Detonate }

    private long _nextWake = -1;
    private DemoTrapMode _mode;
    private long _scanCountdown;
    private bool _detonated;

    // The objects in range of one scan; kept between scans so that a scan does not allocate. Not state.
    private readonly List<Entity> _inRange = [];

    public override string ModuleName => "DemoTrapUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public DemoTrapMode Mode => _mode;
    public bool Detonated => _detonated;

    public override void OnSpawn(IWorldServices world) => _mode = data.DefaultProximityMode ? DemoTrapMode.Proximity : DemoTrapMode.Manual;

    /// <summary>The weapon-slot switch: proximity, manual (never scans) or detonation (goes off at the next update).</summary>
    public void SetMode(IWorldServices world, DemoTrapMode mode)
    {
        _mode = mode;
        world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepNone);
    }

    public long Update(IWorldServices world)
    {
        if (_detonated) return SimulationTiming.SleepForever;
        if (Entity.HasStatus(ObjectStatusBits.UnderConstruction) || Entity.HasStatus(ObjectStatusBits.Sold)) return SimulationTiming.SleepNone;
        if (Entity.EffectivelyDead)
        {
            if (data.DetonateWhenKilled) Detonate(world);
            return SimulationTiming.SleepNone;
        }
        if (_mode == DemoTrapMode.Detonate)
        {
            Detonate(world);
            return SimulationTiming.SleepNone;
        }
        if (_scanCountdown > 0)
        {
            _scanCountdown--;
            return SimulationTiming.SleepNone;
        }
        if (_mode == DemoTrapMode.Manual) return SimulationTiming.SleepNone;
        _scanCountdown = data.ScanTicks;

        var shallDetonate = false;
        var limit = data.TriggerRange * data.TriggerRange;
        // An object more than one unit beyond the range on one axis is outside the circle whatever the other axis says: its squared distance is at least
        // (range + 1)^2 = range^2 + 2 range + 1, and the rounding of the two products (half a raw unit each) cannot bring that down to the limit. Those
        // objects - nearly all of the world - are passed over before the 128-bit products; the test of the circle itself is unchanged.
        var reach = data.TriggerRange + Fixed.One;
        var boxed = data.TriggerRange >= Fixed.Zero;
        var inRange = _inRange;
        inRange.Clear();
        foreach (var other in world.Entities)
        {
            var dx = other.X - Entity.X;
            var dy = other.Y - Entity.Y;
            if (boxed && (dx > reach || dy > reach || -dx > reach || -dy > reach)) continue;
            if (!other.DestroyRequested && dx * dx + dy * dy <= limit) inRange.Add(other);
        }
        inRange.Sort(static (a, b) => a.Id.Value.CompareTo(b.Id.Value));
        foreach (var other in inRange)
        {
            if (IsIgnoredKind(other.Template) || other.EffectivelyDead) continue;
            // "order matters: we want to know if I consider it to be an enemy, not vice versa"
            if (world.RelationshipOf(Entity.Owner, other.Owner) != Relationship.Enemies)
            {
                if (!data.FriendlyDetonation) return SimulationTiming.SleepNone;
                continue;
            }
            if (world.IsSignificantlyAboveTerrain(other)) continue;
            shallDetonate = true;
            if (data.FriendlyDetonation) break;
        }
        if (shallDetonate) Detonate(world);
        return SimulationTiming.SleepNone;
    }

    private bool IsIgnoredKind(Templates.ObjectTemplate template)
    {
        foreach (var kind in data.IgnoreKinds)
            if (template.IsKindOf(kind)) return true;
        return false;
    }

    private void Detonate(IWorldServices world)
    {
        if (!Entity.HasStatus(ObjectStatusBits.UnderConstruction) && !Entity.HasStatus(ObjectStatusBits.Sold) && data.DetonationWeapon.Length > 0
            && world.Weapons.TryGet(data.DetonationWeapon, out var weapon))
            world.FireWeaponAt(Entity, weapon, null, Entity.X, Entity.Y, Entity.Z);
        world.Kill(Entity.Id);
        _detonated = true;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("mode", (int)_mode);
        writer.Int("scanCountdown", _scanCountdown);
        writer.Bool("detonated", _detonated);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        var mode = reader.Int("mode");
        if (mode < 0 || mode > (int)DemoTrapMode.Detonate) throw new InvalidDataException("Demo trap mode out of range.");
        _mode = (DemoTrapMode)mode;
        _scanCountdown = reader.Int("scanCountdown");
        _detonated = reader.Bool("detonated");
    }
}

// ================================================================================================================ MinefieldBehavior

/// <summary>
/// <c>MinefieldBehaviorModuleData</c> (<c>MinefieldBehavior.cpp</c>): the weapon a detonation fires, which relationships set a mine off (a bit per <c>Relationship</c>; default enemies and
/// neutrals), the regeneration switches, how often it checks whether its creator is gone, the virtual mine count, how far a unit has to move before it sets off the next virtual mine, the
/// degeneration rate after the creator is gone and the creation list run at every detonation. <c>ScootFromStartingPointTime</c> is typed: the slide of a freshly laid mine is not modeled.
/// </summary>
public sealed record MinefieldData(string DetonationWeapon, uint DetonatedByMask, bool StopsRegenAfterCreatorDies, bool Regenerates, bool WorkersDetonate, long CreatorDeathCheckTicks,
    int NumVirtualMines, Fixed RepeatDetonateMoveThreshold, Fixed DegenPerSecond, string CreationList) : ModuleData
{
    public static MinefieldData Parse(RatedModuleConfiguration configuration)
    {
        var flags = ModuleConfigReader.Flags(configuration, "DetonatedBy");
        uint mask = (1u << (int)Relationship.Enemies) | (1u << (int)Relationship.Neutral);
        if (flags is not null)
        {
            mask = 0;
            foreach (var name in flags.Names)
                mask |= 1u << (NameTablesStructures.Relationship.TryIndexOf(name, out var index) ? index : throw new InvalidDataException($"Unknown relationship '{name}'."));
        }
        return new MinefieldData(
            ModuleConfigReader.Text(configuration, "DetonationWeapon"), mask, ModuleConfigReader.Bool(configuration, "StopsRegenAfterCreatorDies", true),
            ModuleConfigReader.Bool(configuration, "Regenerates", false), ModuleConfigReader.Bool(configuration, "WorkersDetonate", false),
            ModuleConfigReader.Ticks(configuration, "CreatorDeathCheckRate", SimulationRate.OriginalFramesPerSecond), checked((int)ModuleConfigReader.Int(configuration, "NumVirtualMines", 1)),
            ModuleConfigReader.Real(configuration, "RepeatDetonateMoveThresh", Fixed.One), ModuleConfigReader.Real(configuration, "DegenPercentPerSecondAfterCreatorDies", Fixed.Zero),
            ModuleConfigReader.Text(configuration, "CreationList"));
    }
}

/// <summary>
/// <c>MinefieldBehavior</c> (<c>MinefieldBehavior.cpp</c>): one object that stands for <c>NumVirtualMines</c> mines. A unit that touches it (<see cref="ICollideModule"/>; a worker only with
/// <c>WorkersDetonate</c>, and only of a relationship <c>DetonatedBy</c> allows) sets off one virtual mine at the point of its footprint nearest the toucher — the weapon fires there, the count
/// drops, the health drops to the matching share (never below 0.1), the creation list runs — and the same unit has to move <c>RepeatDetonateMoveThresh</c> before it sets off another. Damage and
/// healing keep health and count in step (<c>onDamage</c>: a hit detonates as many virtual mines as it took; a heal restores them; <c>AutoHealBehavior</c> regenerates them one by one), a mine
/// with no virtual mines left is masked (<c>MASKED</c> status; the rubble model is presentation) and is destroyed instead if it does not regenerate. A regenerating mine whose creator is
/// gone (checked every <c>CreatorDeathCheckRate</c>; the creator is the object that laid it, <see cref="IProducerReceiver"/>) stops healing and degenerates by
/// <c>DegenPercentPerSecondAfterCreatorDies</c> of its health a second until it is gone. Death destroys it. Not modeled: mine clearers' immunity (<c>isClearingMines</c>; the simulation
/// has no mine-clearing detail), the scoot of a newly laid mine, <c>disarm</c> (no caller).
/// </summary>
public sealed class MinefieldBehavior(MinefieldData data) : SimModule, IUpdateModule, ICollideModule, IDamageModule, IDieModule, IProducerReceiver
{
    // Detonation never puts the health below this, since the mine probably heals itself.
    private static readonly Fixed MinHealth = Fixed.FromDecimal(0.1m);
    private static readonly int MaskedBit = ObjectStatusBits.Index("MASKED");
    private static readonly int NoAttackFromAiBit = ObjectStatusBits.Index("NO_ATTACK_FROM_AI");

    private long _nextWake = -1;
    private long _nextDeathCheck;
    private int _virtualMines = data.NumVirtualMines;
    private bool _regenerates = data.Regenerates;
    private bool _draining;
    private bool _ignoreDamage;
    private EntityId _producer = EntityId.Invalid;
    private readonly List<(EntityId Id, Fixed X, Fixed Y, Fixed Z)> _detonators = [];

    public override string ModuleName => "MinefieldBehavior";
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public int VirtualMinesRemaining => _virtualMines;
    public bool IsDraining => _draining;

    public void OnProducedBy(IWorldServices world, EntityId producer) => _producer = producer;

    public override void OnSpawn(IWorldServices world)
    {
        // "mines aren't auto-acquirable"; the reference starts awake and works out the sleep time from the first update on.
        Entity.Status |= 1UL << NoAttackFromAiBit;
        _nextWake = world.Tick + SimulationTiming.SleepNone;
    }

    private long CalcSleepTime(long now)
    {
        if (_draining) return SimulationTiming.SleepNone;
        if (_regenerates && data.StopsRegenAfterCreatorDies) return Math.Max(_nextDeathCheck - now, SimulationTiming.SleepNone);
        return SimulationTiming.SleepForever;
    }

    public long Update(IWorldServices world)
    {
        var now = world.Tick;
        if (now >= _nextDeathCheck && _regenerates && data.StopsRegenAfterCreatorDies)
        {
            _nextDeathCheck = now + data.CreatorDeathCheckTicks;
            if (_producer.IsValid && (world.FindEntity(_producer) is not { } producer || producer.EffectivelyDead || producer.DestroyRequested))
            {
                _regenerates = false;
                _draining = true;
                foreach (var module in Entity.ModuleArray)
                    if (module is AutoHealBehavior heal) heal.StopHealing(world);
            }
        }
        if (_draining && Entity.Body is { } body)
        {
            var amount = body.MaxHealth * data.DegenPerSecond / world.Time.TicksPerSecondFixed;
            world.Damage(Entity.Id, new DamageInfo(Entity.Id, amount, DamageTypes.Unresistable, DeathTypes.Normal, false));
        }
        return CalcSleepTime(now);
    }

    /// <summary><c>detonateOnce</c>.</summary>
    private void DetonateOnce(IWorldServices world, Fixed x, Fixed y, Fixed z)
    {
        if (data.DetonationWeapon.Length > 0 && world.Weapons.TryGet(data.DetonationWeapon, out var weapon)) world.FireWeaponAt(Entity, weapon, null, x, y, z);
        if (_virtualMines > 0) _virtualMines--;
        if (!_regenerates && _virtualMines == 0) world.RequestDestroy(Entity.Id);
        else if (Entity.Body is { } body)
        {
            var percent = Fixed.FromInt(_virtualMines) / Fixed.FromInt(data.NumVirtualMines);
            var desired = Fixed.Max(percent * body.MaxHealth, MinHealth);
            var amount = body.Health - desired;
            if (amount > Fixed.Zero)
            {
                _ignoreDamage = true;
                world.Damage(Entity.Id, new DamageInfo(Entity.Id, amount, DamageTypes.Unresistable, DeathTypes.None, false));
                _ignoreDamage = false;
            }
        }
        UpdateMasked();
        if (data.CreationList.Length > 0 && !data.CreationList.Equals("None", StringComparison.OrdinalIgnoreCase)) world.CreateFromList(Entity, data.CreationList, Entity.Id, false);
    }

    private void UpdateMasked()
    {
        if (_virtualMines == 0) Entity.Status |= 1UL << MaskedBit;
        else Entity.Status &= ~(1UL << MaskedBit);
    }

    public void OnCollide(IWorldServices world, Entity other)
    {
        if (other.EffectivelyDead || _virtualMines == 0) return;
        // A worker is infantry that is also a dozer: it walks over mines unless the mine says otherwise.
        if (!data.WorkersDetonate && other.Template.IsKindOf("INFANTRY") && other.Template.IsKindOf("DOZER")) return;
        var relationship = world.RelationshipOf(Entity.Owner, other.Owner);
        if ((data.DetonatedByMask & (1u << (int)relationship)) == 0) return;

        var index = _detonators.FindIndex(entry => entry.Id == other.Id);
        if (index >= 0)
        {
            var dx = other.X - _detonators[index].X;
            var dy = other.Y - _detonators[index].Y;
            var dz = other.Z - _detonators[index].Z;
            if (dx * dx + dy * dy + dz * dz <= data.RepeatDetonateMoveThreshold * data.RepeatDetonateMoveThreshold) return;
            _detonators[index] = (other.Id, other.X, other.Y, other.Z);
        }
        else _detonators.Add((other.Id, other.X, other.Y, other.Z));

        var (px, py) = ClipToFootprint(other.X, other.Y);
        DetonateOnce(world, px, py, other.Z);
    }

    /// <summary><c>GeometryInfo::clipPointToFootprint</c>: the point of the mine's footprint nearest to (<paramref name="x"/>, <paramref name="y"/>); a point inside stays where it is.</summary>
    private (Fixed X, Fixed Y) ClipToFootprint(Fixed x, Fixed y)
    {
        var template = Entity.Template;
        var dx = x - Entity.X;
        var dy = y - Entity.Y;
        if (template.Geometry.Equals("BOX", StringComparison.OrdinalIgnoreCase))
        {
            var cos = FixedTrig.Cos(Entity.Angle);
            var sin = FixedTrig.Sin(Entity.Angle);
            var along = Fixed.Max(-template.GeometryMajorRadius, Fixed.Min(template.GeometryMajorRadius, dx * cos + dy * sin));
            var across = Fixed.Max(-template.GeometryMinorRadius, Fixed.Min(template.GeometryMinorRadius, -dx * sin + dy * cos));
            return (Entity.X + along * cos - across * sin, Entity.Y + along * sin + across * cos);
        }
        var distance = Fixed.Sqrt(dx * dx + dy * dy);
        var radius = template.GeometryMajorRadius;
        if (distance <= radius || distance == Fixed.Zero) return (x, y);
        return (Entity.X + dx * radius / distance, Entity.Y + dy * radius / distance);
    }

    public void OnDamage(IWorldServices world, in DamageInfo info, Fixed actualDamageDealt) => Reconcile(world, info);

    public void OnHealing(IWorldServices world, in DamageInfo info) => Reconcile(world, info);

    /// <summary><c>onDamage</c> (<c>onHealing</c> calls it): detonate or restore virtual mines until the count matches the health.</summary>
    private void Reconcile(IWorldServices world, in DamageInfo info)
    {
        if (_ignoreDamage || Entity.Body is not { } body || body.MaxHealth <= Fixed.Zero) return;
        while (true)
        {
            var expectedValue = Fixed.FromInt(data.NumVirtualMines) * body.Health / body.MaxHealth;
            var expected = (int)(info.DamageType == DamageTypes.Healing ? expectedValue.Floor() : expectedValue.Ceiling());
            if (expected > data.NumVirtualMines) expected = data.NumVirtualMines;
            if (_virtualMines < expected) _virtualMines = expected;
            else if (_virtualMines > expected)
            {
                if (_draining && info.Source == Entity.Id && info.DamageType == DamageTypes.Unresistable) _virtualMines--;
                else DetonateOnce(world, Entity.X, Entity.Y, Entity.Z);
            }
            else break;
        }
        if (_virtualMines == 0 && _regenerates && body.Health < MinHealth && body is ActiveBody active) active.RaiseHealthTo(world, MinHealth);
        UpdateMasked();
    }

    public void OnDie(IWorldServices world, in DamageInfo info) => world.RequestDestroy(Entity.Id);

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("nextDeathCheck", _nextDeathCheck);
        writer.Int("virtualMines", _virtualMines);
        writer.Bool("regenerates", _regenerates);
        writer.Bool("draining", _draining);
        if (_producer.IsValid) writer.UInt("producer", _producer.Value);
        if (_detonators.Count == 0) return;
        writer.BeginArray("detonators");
        foreach (var (id, x, y, z) in _detonators)
        {
            writer.BeginObject(null);
            writer.UInt("id", id.Value);
            writer.Fixed("x", x);
            writer.Fixed("y", y);
            writer.Fixed("z", z);
            writer.EndObject();
        }
        writer.EndArray();
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _nextDeathCheck = reader.Int("nextDeathCheck");
        _virtualMines = checked((int)reader.Int("virtualMines"));
        _regenerates = reader.Bool("regenerates");
        _draining = reader.Bool("draining");
        _producer = reader.Has("producer") ? new EntityId(checked((uint)reader.UInt("producer"))) : EntityId.Invalid;
        _detonators.Clear();
        if (!reader.Has("detonators")) return;
        foreach (var item in reader.Array("detonators")) _detonators.Add((new EntityId(checked((uint)item.UInt("id"))), item.Fixed("x"), item.Fixed("y"), item.Fixed("z")));
    }
}
