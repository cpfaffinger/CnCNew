using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>
/// The hazard-cleaning ambulance (<c>CleanupHazardUpdate</c> + <c>CleanupAreaPower</c>) and the spy-vision family (<c>SpyVisionUpdate</c> + <c>SpyVisionSpecialPower</c>: the CIA
/// intelligence power and the Internet Center's satellite hack). Each class cites its reference file and names what the simulation leaves out.
/// </summary>
internal static class CleanupAndSpyModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<CleanupHazardData>("CleanupHazardUpdate", ModuleOwnerKind.Object, (configuration, _) => CleanupHazardData.Parse(configuration), data => new CleanupHazardUpdate(data)),
        new ModuleBinding<CleanupAreaPowerData>("CleanupAreaPower", ModuleOwnerKind.Object, (configuration, _) => CleanupAreaPowerData.Parse(configuration), data => new CleanupAreaPower(data)),
        new ModuleBinding<SpyVisionData>("SpyVisionUpdate", ModuleOwnerKind.Object, SpyVisionData.Parse, data => new SpyVisionUpdate(data)),
        new ModuleBinding<SpyVisionPowerData>("SpyVisionSpecialPower", ModuleOwnerKind.Object, (configuration, _) => SpyVisionPowerData.Parse(configuration), data => new SpyVisionSpecialPower(data)),
        new ModuleBinding<AssistedTargetingData>("AssistedTargetingUpdate", ModuleOwnerKind.Object, (configuration, _) => AssistedTargetingData.Parse(configuration), data => new AssistedTargetingUpdate(data)),
        new ModuleBinding<BattlePlanUpdateData>("BattlePlanUpdate", ModuleOwnerKind.Object, (configuration, _) => BattlePlanUpdateData.Parse(configuration), data => new BattlePlanUpdate(data)),
    ];
}

// ============================================================================================================ CleanupHazardUpdate

/// <summary>
/// <c>CleanupHazardUpdateModuleData</c>: <c>WeaponSlot</c> (the weapon slot that cleans, default <c>PRIMARY</c>), <c>ScanRate</c> and <c>ScanRange</c>.
/// </summary>
public sealed record CleanupHazardData(long ScanTicks, Fixed ScanRange, int Slot) : ModuleData
{
    public static CleanupHazardData Parse(RatedModuleConfiguration configuration)
    {
        var slot = WeaponSlot.Primary;
        if (ModuleConfigReader.Find(configuration, "WeaponSlot")?.Value.Value is EnumValue named && !WeaponSlot.TryParse(named.Name, out slot))
            throw new UnsupportedModuleConfigurationException("WeaponSlot");
        return new CleanupHazardData(ModuleConfigReader.Ticks(configuration, "ScanRate", 0), ModuleConfigReader.Real(configuration, "ScanRange", Fixed.Zero), slot);
    }
}

/// <summary>
/// <c>CleanupHazardUpdate.cpp</c> (the ambulance, the bio-hazard tech): every <c>ScanRate</c> it looks for the closest <c>CLEANUP_HAZARD</c> object within <c>ScanRange</c> and attacks it
/// with its weapon until it is gone. The reference orders the AI to attack (<c>aiAttackObject</c>), which walks to weapon range and fires; here the module does both — it moves to
/// three quarters of the weapon's range from the target and then fires every tick the weapon is ready (<see cref="IWorldServices.TryFireWeaponSlot"/>, with the slot locked temporarily as the reference does before its attack order). <see cref="SetCleanupAreaParameters"/>
/// (from <see cref="CleanupAreaPower"/>) makes it clean an <em>area</em> instead: it walks to the point, scans around the point with <c>ScanRange + move range</c>, ignores weapon range while
/// it is hunting and goes back to the point when nothing is left, ending the mode within 25 units of it; a new order from the player or a script (not issued by the module itself) abandons it.
/// Not modelled: the reference sets the veteran weapon-set flag on creation (no shipped cleaner has such a set), the "busy" AI state that keeps a cleaning unit from reporting idle to
/// scripts (the simulation has none).
/// </summary>
public sealed class CleanupHazardUpdate(CleanupHazardData data) : SimModule, IUpdateModule
{
    /// <summary><c>BOOBY_TRAP_SCAN_RANGE</c>-style constant of the reference: area mode ends when the unit is back within this distance of its point.</summary>
    private static readonly Fixed AreaDoneDistance = Fixed.FromInt(25);
    /// <summary><c>CleanupHazardUpdate.cpp</c>, <c>fireWhenReady</c>: <c>GameLogicRandomValue( 0, 3 )</c> – the longest wait before the rescan, in logic frames of the original.</summary>
    private const int RescanWaitMaxFrames = 3;

    private long _nextWake = -1;
    private EntityId _bestTarget;
    private bool _inRange;
    private long _nextScan;
    private Fixed _moveRange;
    private Fixed _posX;
    private Fixed _posY;

    public override string ModuleName => "CleanupHazardUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    /// <summary>Whether the unit is cleaning an area (<c>m_moveRange &gt; 0</c>).</summary>
    public bool CleaningArea => _moveRange > Fixed.Zero;

    public override void OnSpawn(IWorldServices world) => _nextWake = world.Tick + SimulationTiming.SleepNone;

    /// <summary><c>CleanupHazardUpdate::setCleanupAreaParameters</c>: clean the area around (x, y), moving up to <paramref name="range"/> from it; the unit walks there at once.</summary>
    public void SetCleanupAreaParameters(IWorldServices world, Fixed x, Fixed y, Fixed range)
    {
        _moveRange = range;
        _posX = x;
        _posY = y;
        CrateModules.AiOf(Entity)?.OrderMove(world, x, y, false);
        world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepNone);
        world.MarkDirty(Entity);
    }

    public long Update(IWorldServices world)
    {
        if (SimulationWorld.WeaponIndexIn(Entity, data.Slot) < 0) return SimulationTiming.SleepForever;
        var ai = CrateModules.AiOf(Entity);
        if (_moveRange > Fixed.Zero && ai is not null && !ai.IsIdle && ai.LastCommandFromPlayer)
        {
            // Either the player or a script gave a NEW order, so abandon the cleanup-area cause.
            _moveRange = Fixed.Zero;
            return SimulationTiming.SleepNone;
        }
        if (_nextScan > 0)
        {
            _nextScan--;
            FireWhenReady(world, ai);
            return SimulationTiming.SleepNone;
        }
        // CleanupHazardUpdate.cpp, update: m_nextScanFrames-- / m_nextScanFrames = data->m_scanFrames – a scan every ScanRate + 1 frames of the original.
        _nextScan = world.Time.CountdownReload(data.ScanTicks);
        if (ScanClosestTarget(world)) FireWhenReady(world, ai);
        else if (_moveRange != Fixed.Zero && ai is not null && ai.IsIdle)
        {
            // Nothing nearby: go back to where the area order was given, then end the mode.
            var dx = _posX - Entity.X;
            var dy = _posY - Entity.Y;
            if (Fixed.Sqrt(dx * dx + dy * dy) < AreaDoneDistance) _moveRange = Fixed.Zero;
            else ai.OrderMove(world, _posX, _posY, false);
        }
        return SimulationTiming.SleepNone;
    }

    private void FireWhenReady(IWorldServices world, AIUpdateInterface? ai)
    {
        var weapon = world.Weapons[SimulationWorld.WeaponIndexIn(Entity, data.Slot)];
        var target = _bestTarget.IsValid ? world.FindEntity(_bestTarget) : null;
        if (target is not null && target.DestroyRequested) target = null;
        if (target is not null && _moveRange == Fixed.Zero)
        {
            // Track the target (this keeps the unit from drifting out of range): in range now, or lost since the last tick and re-evaluated.
            var distance = DistanceTo(target);
            if (distance < weapon.AttackRange) _inRange = true;
            else if (_inRange)
            {
                // CleanupHazardUpdate.cpp, fireWhenReady: m_nextScanFrames = GameLogicRandomValue( 0, 3 ) – drawn in frames of the original; a wait of
                // n > 0 frames ends n + 1 frames later (the countdown acts in the update after it reached zero).
                var waitFrames = world.LogicRandom.Range(0, RescanWaitMaxFrames);
                _nextScan = waitFrames == 0 ? 0 : world.Time.CountdownReload(world.Time.OriginalFrames(waitFrames));
                _bestTarget = EntityId.Invalid;
                if (_nextScan == 0)
                {
                    ScanClosestTarget(world);
                    _nextScan = world.Time.CountdownReload(data.ScanTicks);
                    target = null; // might be out of range
                }
            }
            else _inRange = false;
        }
        if (target is null || ai is null) return;
        // aiAttackObject is only given to a unit that is idle or already doing the module's own approach; any other (player or script) order has priority.
        if (!(ai.IsIdle || (Entity.HasMoveTarget && !ai.LastCommandFromPlayer))) return;
        Attack(world, ai, target, weapon);
    }

    private void Attack(IWorldServices world, AIUpdateInterface ai, Entity target, WeaponInfo weapon)
    {
        var dx = target.X - Entity.X;
        var dy = target.Y - Entity.Y;
        var distance = Fixed.Sqrt(dx * dx + dy * dy);
        if (distance <= weapon.AttackRange)
        {
            if (Entity.HasMoveTarget) ai.StopInternalMove();
            // CleanupHazardUpdate::update: "lock it just till the weapon is empty or the attack is done" before the attack order.
            world.SetWeaponLock(Entity, data.Slot, WeaponLockKind.Temporary);
            world.TryFireWeaponSlot(Entity, data.Slot, target);
            return;
        }
        // Walk to three quarters of the range from the target along the line between them; re-plan only when the target moved away from the old stand-off point.
        var standOff = weapon.AttackRange * Fixed.FromDecimal(0.75m);
        var gx = target.X - dx * standOff / distance;
        var gy = target.Y - dy * standOff / distance;
        var slack = Fixed.FromInt(2 * Terrain.TerrainMap.CellSize);
        if (Entity.HasMoveTarget && (Entity.MoveTargetX - gx).Abs() <= slack && (Entity.MoveTargetY - gy).Abs() <= slack) return;
        ai.OrderMove(world, gx, gy, false);
    }

    private Fixed DistanceTo(Entity target)
    {
        var dx = target.X - Entity.X;
        var dy = target.Y - Entity.Y;
        return Fixed.Sqrt(dx * dx + dy * dy);
    }

    /// <summary><c>CleanupHazardUpdate::scanClosestTarget</c>: the closest <c>CLEANUP_HAZARD</c> object to the unit (or, cleaning an area, to the area's point, within <c>ScanRange + move range</c>).</summary>
    private bool ScanClosestTarget(IWorldServices world)
    {
        _bestTarget = EntityId.Invalid;
        var area = _moveRange > Fixed.Zero;
        var cx = area ? _posX : Entity.X;
        var cy = area ? _posY : Entity.Y;
        var radius = area ? data.ScanRange + _moveRange : data.ScanRange;
        Entity? best = null;
        var limit = SquaredReach.Of(radius);
        var bestDistance = Fixed.Zero;
        foreach (var candidate in world.Entities)
        {
            if (candidate.DestroyRequested || candidate.Id == Entity.Id || !candidate.Template.IsKindOf("CLEANUP_HAZARD")) continue;
            var dx = candidate.X - cx;
            var dy = candidate.Y - cy;
            var distance = dx * dx + dy * dy;
            if (!limit.Contains(distance) || (best is not null && (distance > bestDistance || (distance == bestDistance && candidate.Id.Value > best.Id.Value)))) continue;
            best = candidate;
            bestDistance = distance;
        }
        if (best is not null) _bestTarget = best.Id;
        return best is not null;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.UInt("bestTarget", _bestTarget.Value);
        writer.Bool("inRange", _inRange);
        writer.Int("nextScan", _nextScan);
        writer.Fixed("moveRange", _moveRange);
        writer.Fixed("posX", _posX);
        writer.Fixed("posY", _posY);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _bestTarget = new EntityId(checked((uint)reader.UInt("bestTarget")));
        _inRange = reader.Bool("inRange");
        _nextScan = reader.Int("nextScan");
        _moveRange = reader.Fixed("moveRange");
        _posX = reader.Fixed("posX");
        _posY = reader.Fixed("posY");
    }
}

// ============================================================================================================ CleanupAreaPower

/// <summary><c>CleanupAreaPowerModuleData</c>: the shared special-power fields plus <c>MaxMoveDistanceFromLocation</c> (default 0).</summary>
public sealed record CleanupAreaPowerData(SpecialPowerBaseData Base, Fixed MoveRange) : ModuleData
{
    public static CleanupAreaPowerData Parse(RatedModuleConfiguration configuration)
        => new(SpecialPowerBaseData.Parse(configuration), ModuleConfigReader.Real(configuration, "MaxMoveDistanceFromLocation", Fixed.Zero));
}

/// <summary>
/// <c>CleanupAreaPower::doSpecialPowerAtLocation</c>: tells the object's <see cref="CleanupHazardUpdate"/> to clean up the clicked area (<c>setCleanupAreaParameters(loc, MaxMoveDistanceFromLocation)</c>).
/// A power without a location does nothing but recharge.
/// </summary>
public sealed class CleanupAreaPower(CleanupAreaPowerData data) : SpecialPowerModuleBase(data.Base)
{
    public override string ModuleName => "CleanupAreaPower";
    public override bool AcceptsTarget(SpecialPowerTargetKind kind) => kind == SpecialPowerTargetKind.Location;

    protected override void Apply(IWorldServices world, in SpecialPowerTarget target)
    {
        if (target.Kind != SpecialPowerTargetKind.Location) return;
        foreach (var module in Entity.Modules)
            if (module is CleanupHazardUpdate cleanup) { cleanup.SetCleanupAreaParameters(world, target.X, target.Y, data.MoveRange); return; }
    }
}

// ============================================================================================================ SpyVisionUpdate

/// <summary>
/// <c>SpyVisionUpdateModuleData</c>: the shared upgrade fields, <c>NeedsUpgrade</c>, <c>SelfPowered</c> (turns itself on and off by timers; no timers = always on), the two timers and
/// <c>SpyOnKindof</c> (default all kinds: an empty list here).
/// </summary>
public sealed record SpyVisionData(UpgradeMuxData Mux, bool NeedsUpgrade, bool SelfPowered, long SelfPoweredDurationTicks, long SelfPoweredIntervalTicks, ImmutableArray<string> SpyOnKindOf) : ModuleData
{
    public static SpyVisionData Parse(RatedModuleConfiguration configuration, UpgradeCatalog upgrades) => new(
        UpgradeMuxData.Parse(configuration, upgrades),
        ModuleConfigReader.Bool(configuration, "NeedsUpgrade", false),
        ModuleConfigReader.Bool(configuration, "SelfPowered", false),
        ModuleConfigReader.Ticks(configuration, "SelfPoweredDuration", 0),
        ModuleConfigReader.Ticks(configuration, "SelfPoweredInterval", 0),
        ModuleConfigReader.Flags(configuration, "SpyOnKindof")?.Names ?? []);
}

/// <summary>
/// <c>SpyVisionUpdate.cpp</c>: while active, the owner looks through the eyes of every enemy object of the chosen kinds (<see cref="IWorldServices.SetUnitsVisionSpied"/>: a reference count on
/// each object, so the owner sees what those objects see). It is switched on by <see cref="SpyVisionSpecialPower"/> for a duration (<see cref="ActivateSpyVision"/>) or by its upgrade
/// (<c>NeedsUpgrade</c>); a <c>SelfPowered</c> module then cycles on for <c>SelfPoweredDuration</c> and off for <c>SelfPoweredInterval</c> (both 0: permanently on). Removing the object
/// while it spies turns the spying off. <see cref="SetDisabledUntilFrame"/> is the entry the Internet-Center sabotage uses (the simulation has no general disabled state). Not
/// modelled: the ownership-change hook (<c>onCapture</c> would move the spying to the new owner; the world has no capture hook for modules).
/// </summary>
public sealed class SpyVisionUpdate(SpyVisionData data) : SimModule, IUpdateModule, IUpgradeModule
{
    private readonly UpgradeMuxState _mux = new(data.Mux);
    private long _nextWake = SimulationTiming.SleepForever;
    private long _deactivateTick;
    private bool _active;
    private bool _resetTimersNextUpdate;
    private long _disabledUntil;
    private PlayerId _spyingFor;

    public override string ModuleName => "SpyVisionUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    /// <summary>Whether the owner currently spies (<c>m_currentlyActive</c>).</summary>
    public bool Active => _active;

    public void TryActivate(IWorldServices world, UpgradeMask keyMask)
    {
        // SpyVisionUpdate::upgradeImplementation: an upgrade that "needs" it activates the spying for the self-powered duration (0: permanently).
        _mux.AttemptUpgrade(world, Entity, keyMask, _ =>
        {
            if (data.NeedsUpgrade) ActivateSpyVision(world, data.SelfPoweredDurationTicks);
        });
    }

    public void TryReset(IWorldServices world, UpgradeMask keyMask) => _mux.ResetUpgrade(keyMask);
    public bool WouldUpgrade(UpgradeMask keyMask) => _mux.WouldUpgrade(keyMask);

    /// <summary><c>SpyVisionUpdate::activateSpyVision(duration)</c>: spy now, for <paramref name="durationTicks"/> ticks (0: until switched off by something else).</summary>
    public void ActivateSpyVision(IWorldServices world, long durationTicks)
    {
        _deactivateTick = durationTicks == 0 ? long.MaxValue : world.Tick + durationTicks;
        DoActivationWork(world, Entity.Owner, true);
        world.WakeAt(Entity, this, durationTicks == 0 ? SimulationTiming.SleepForever : world.Tick + Math.Max(durationTicks, SimulationTiming.SleepNone));
        world.MarkDirty(Entity);
    }

    /// <summary><c>SpyVisionUpdate::setDisabledUntilFrame</c>: switches the spying off until <paramref name="tick"/> (a tick not after now is the wake-up message: spying resumes on the next update).</summary>
    public void SetDisabledUntilFrame(IWorldServices world, long tick)
    {
        if (tick > world.Tick)
        {
            if (_active) DoActivationWork(world, Entity.Owner, false);
            _disabledUntil = tick;
            _resetTimersNextUpdate = true;
            world.WakeAt(Entity, this, tick >= SimulationTiming.SleepForever ? SimulationTiming.SleepForever : tick);
        }
        else
        {
            _disabledUntil = world.Tick;
            _resetTimersNextUpdate = true;
            world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepNone);
        }
        world.MarkDirty(Entity);
    }

    public long Update(IWorldServices world)
    {
        if (_resetTimersNextUpdate)
        {
            _resetTimersNextUpdate = false;
            if (data.SelfPowered)
            {
                if (data.SelfPoweredIntervalTicks == 0)
                {
                    // An always-on self-powered special: turn it back on now.
                    DoActivationWork(world, Entity.Owner, true);
                    return SimulationTiming.SleepForever;
                }
                // Reset the interval timer by sleeping, so it has to wait before going on again.
                return world.Time.AtLeastOneFrame(data.SelfPoweredIntervalTicks);
            }
        }
        if (_active && _deactivateTick <= world.Tick)
        {
            DoActivationWork(world, _spyingFor, false);
            _deactivateTick = 0;
        }
        else if (!_active && data.SelfPowered)
        {
            DoActivationWork(world, Entity.Owner, true);
            _deactivateTick = data.SelfPoweredDurationTicks == 0 ? long.MaxValue : world.Tick + data.SelfPoweredDurationTicks;
        }
        if (data.SelfPowered)
        {
            // UPDATE_SLEEP(duration/interval); a permanent spy (no duration) has nothing left to do.
            if (_active) return data.SelfPoweredDurationTicks == 0 ? SimulationTiming.SleepForever : data.SelfPoweredDurationTicks;
            return world.Time.AtLeastOneFrame(data.SelfPoweredIntervalTicks);
        }
        return SimulationTiming.SleepForever;
    }

    /// <summary><c>SpyVisionUpdate::doActivationWork</c>: for every player that is an enemy of <paramref name="spyer"/>, its objects of the chosen kinds are looked through (or released).</summary>
    private void DoActivationWork(IWorldServices world, PlayerId spyer, bool setting)
    {
        foreach (var victim in world.PlayerIds)
            if (world.RelationshipOf(spyer, victim) == Relationship.Enemies) world.SetUnitsVisionSpied(victim, setting, data.SpyOnKindOf, spyer);
        _active = setting;
        _spyingFor = spyer;
    }

    public override void OnRemove(IWorldServices world)
    {
        // onDelete: if I was left on at the time of death, turn me off.
        if (_active) DoActivationWork(world, _spyingFor, false);
    }

    public override void Describe(IStateWriter writer)
    {
        _mux.Describe(writer);
        writer.Int("nextWake", _nextWake);
        writer.Int("deactivateTick", _deactivateTick);
        writer.Bool("active", _active);
        writer.Bool("resetTimersNextUpdate", _resetTimersNextUpdate);
        writer.Int("disabledUntil", _disabledUntil);
        writer.Int("spyingFor", _spyingFor.Value);
    }

    public override void Load(IStateReader reader)
    {
        _mux.Load(reader);
        _nextWake = reader.Int("nextWake");
        _deactivateTick = reader.Int("deactivateTick");
        _active = reader.Bool("active");
        _resetTimersNextUpdate = reader.Bool("resetTimersNextUpdate");
        _disabledUntil = reader.Int("disabledUntil");
        _spyingFor = new PlayerId(checked((int)reader.Int("spyingFor")));
    }
}

// ============================================================================================================ SpyVisionSpecialPower

/// <summary><c>SpyVisionSpecialPowerModuleData</c>: <c>BaseDuration</c>, <c>BonusDurationPerCaptured</c> and <c>MaxDuration</c> (all default 0).</summary>
public sealed record SpyVisionPowerData(SpecialPowerBaseData Base, long BaseDurationTicks, long BonusPerCapturedTicks, long MaxDurationTicks) : ModuleData
{
    public static SpyVisionPowerData Parse(RatedModuleConfiguration configuration) => new(
        SpecialPowerBaseData.Parse(configuration), ModuleConfigReader.Ticks(configuration, "BaseDuration", 0),
        ModuleConfigReader.Ticks(configuration, "BonusDurationPerCaptured", 0), ModuleConfigReader.Ticks(configuration, "MaxDuration", 0));
}

/// <summary>
/// <c>SpyVisionSpecialPower::doSpecialPower</c>: starts the object's <see cref="SpyVisionUpdate"/> for <c>BaseDuration</c> plus <c>BonusDurationPerCaptured</c> for every unit held inside, the
/// sum capped at <c>MaxDuration</c> — the cap is only applied for an object that has a container, exactly as in the reference.
/// </summary>
public sealed class SpyVisionSpecialPower(SpyVisionPowerData data) : SpecialPowerModuleBase(data.Base)
{
    public override string ModuleName => "SpyVisionSpecialPower";
    public override bool AcceptsTarget(SpecialPowerTargetKind kind) => true;

    protected override void Apply(IWorldServices world, in SpecialPowerTarget target)
    {
        var duration = data.BaseDurationTicks;
        if (Entity.Container is { } container)
        {
            duration += container.ContainCount * data.BonusPerCapturedTicks;
            if (duration > data.MaxDurationTicks) duration = data.MaxDurationTicks;
        }
        foreach (var module in Entity.Modules)
            if (module is SpyVisionUpdate spy) { spy.ActivateSpyVision(world, duration); return; }
    }
}

// ============================================================================================================ AssistedTargetingUpdate

/// <summary>
/// <c>AssistedTargetingUpdateModuleData</c>: <c>AssistingClipSize</c> (default 1) and <c>AssistingWeaponSlot</c> (default <c>PRIMARY</c>).
/// The two <c>Laser*</c> template names draw the feedback beams (presentation) and are typed but not read.
/// </summary>
public sealed record AssistedTargetingData(int ClipSize, int Slot) : ModuleData
{
    public static AssistedTargetingData Parse(RatedModuleConfiguration configuration)
    {
        var slot = WeaponSlot.Primary;
        if (ModuleConfigReader.Find(configuration, "AssistingWeaponSlot")?.Value.Value is EnumValue value && !WeaponSlot.TryParse(value.Name, out slot))
            throw new UnsupportedModuleConfigurationException("AssistingWeaponSlot");
        return new AssistedTargetingData(checked((int)ModuleConfigReader.Int(configuration, "AssistingClipSize", 1)), slot);
    }
}

/// <summary>
/// <c>AssistedTargetingUpdate.cpp</c> (the Patriot battery's network): when a neighbour of the same kind fires a weapon with a <c>RequestAssistRange</c> at a victim
/// (<c>SimulationWorld.ProcessRequestAssistance</c>), every free one of these modules attacks the same victim with its assisting weapon slot for <c>AssistingClipSize</c> shots.
/// "Free" is <c>isFreeToAssist</c>: able to attack (alive, complete, not sold) and its current weapon ready, and not already assisting. The reference locks the slot temporarily
/// (<c>setWeaponLock(slot, LOCKED_TEMPORARILY)</c>, ended by the weapon slot model when the clip empties) and gives the AI an attack order (<c>aiAttackObject</c> with a shot limit); this module fires the
/// slot's weapon itself (<see cref="IWorldServices.TryFireWeaponSlot"/>: the slot's own clip, reload and last-shot state), one shot per ready weapon, and gives up when the victim is gone or out of the
/// weapon's range (the shipped users are immobile, so the AI could not approach either). The feedback lasers of <c>makeFeedbackLaser</c> are presentation (a beam object needs end points the simulation cannot carry) and are not created.
/// </summary>
public sealed class AssistedTargetingUpdate(AssistedTargetingData data) : SimModule, IUpdateModule
{
    private long _nextWake = SimulationTiming.SleepForever;
    private EntityId _victim;
    private int _shotsLeft;

    public override string ModuleName => "AssistedTargetingUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    /// <summary>Shots still to fire for the current request (0 when idle).</summary>
    public int ShotsLeft => _shotsLeft;

    /// <summary>
    /// <c>AssistedTargetingUpdate::isFreeToAssist</c> ("the reload times of my two weapons are tied together, so Ready is indicative of either"): <c>isAbleToAttack</c> and the
    /// current weapon <c>READY_TO_FIRE</c>. The module also needs a weapon in its assisting slot and no request of its own under way.
    /// </summary>
    public bool IsFreeToAssist(IWorldServices world)
        => !Entity.EffectivelyDead && !Entity.DestroyRequested && !Entity.HasStatus(ObjectStatusBits.UnderConstruction) && !Entity.HasStatus(ObjectStatusBits.Sold)
           && CrateModules.AiOf(Entity) is not null && SimulationWorld.WeaponIndexIn(Entity, data.Slot) >= 0 && _shotsLeft == 0
           && SimulationWorld.WeaponIndexIn(Entity, Entity.CurrentWeaponSlot) >= 0 && world.Tick >= Entity.ReadyTickOf(Entity.CurrentWeaponSlot);

    /// <summary><c>AssistedTargetingUpdate::assistAttack</c>: attack <paramref name="victim"/> for <c>AssistingClipSize</c> shots on behalf of <paramref name="requester"/>.</summary>
    public void AssistAttack(IWorldServices world, Entity requester, Entity victim)
    {
        _ = requester;
        _victim = victim.Id;
        _shotsLeft = Math.Max(data.ClipSize, 0);
        // assistAttack: "lock it just till the weapon is empty or the attack is done".
        world.SetWeaponLock(Entity, data.Slot, WeaponLockKind.Temporary);
        world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepNone);
        world.MarkDirty(Entity);
    }

    public long Update(IWorldServices world)
    {
        if (_shotsLeft <= 0) return Stop();
        var victim = world.FindEntity(_victim);
        if (victim is null || victim.DestroyRequested || victim.EffectivelyDead || SimulationWorld.WeaponIndexIn(Entity, data.Slot) < 0) return Stop();
        var ready = Entity.ReadyTickOf(data.Slot);
        var reloadWait = world.Time.OriginalFrames(ReloadWaitFrames);
        if (world.Tick < ready) return Math.Min(ready - world.Tick, reloadWait);
        if (!world.TryFireWeaponSlot(Entity, data.Slot, victim)) return Stop();
        _shotsLeft--;
        return _shotsLeft <= 0 ? Stop() : Math.Max(Math.Min(Entity.ReadyTickOf(data.Slot) - world.Tick, reloadWait), SimulationTiming.SleepNone);
    }

    /// <summary>Own value: an upper bound for one sleep while the slot reloads (a weapon that reloads at an airfield never becomes ready by itself), in logic frames of the original.</summary>
    private const long ReloadWaitFrames = 1000;

    private long Stop()
    {
        _shotsLeft = 0;
        _victim = EntityId.Invalid;
        return SimulationTiming.SleepForever;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.UInt("victim", _victim.Value);
        writer.Int("shotsLeft", _shotsLeft);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _victim = new EntityId(checked((uint)reader.UInt("victim")));
        _shotsLeft = checked((int)reader.Int("shotsLeft"));
    }
}
