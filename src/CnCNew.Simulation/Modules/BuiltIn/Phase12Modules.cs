using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>
/// The modules the campaign missions needed to make their named objects instantiable (<c>docs/SCRIPT-ENGINE.md</c> §9, Phase 12): the damage reaction weapon of
/// <c>FireWeaponWhenDamagedBehavior</c>, the horde bonus of <c>HordeUpdate</c>, <c>FireWeaponPower</c> and <c>BaikonurLaunchPower</c>, the neutralising
/// <c>TechBuildingBehavior</c> and the purely visual <c>RadiusDecalUpdate</c>. Each cites its reference file; what a module leaves to presentation is stated there.
/// </summary>
internal static class Phase12Modules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<NoFieldsData>("RadiusDecalUpdate", ModuleOwnerKind.Object, (_, _) => NoFieldsData.Instance, _ => new RadiusDecalUpdate()),
        new ModuleBinding<TechBuildingData>("TechBuildingBehavior", ModuleOwnerKind.Object, (configuration, _) => TechBuildingData.Parse(configuration), data => new TechBuildingBehavior(data)),
        new ModuleBinding<FireWeaponWhenDamagedData>("FireWeaponWhenDamagedBehavior", ModuleOwnerKind.Object, FireWeaponWhenDamagedData.Parse, data => new FireWeaponWhenDamagedBehavior(data)),
        new ModuleBinding<FireWeaponPowerData>("FireWeaponPower", ModuleOwnerKind.Object, (configuration, _) => FireWeaponPowerData.Parse(configuration), data => new FireWeaponPower(data)),
        new ModuleBinding<BaikonurLaunchData>("BaikonurLaunchPower", ModuleOwnerKind.Object, (configuration, _) => BaikonurLaunchData.Parse(configuration), data => new BaikonurLaunchPower(data)),
        new ModuleBinding<NoFieldsData>("FlightDeckBehavior", ModuleOwnerKind.Object, (_, _) => NoFieldsData.Instance, _ => new FlightDeckBehavior()),
        new ModuleBinding<HordeData>("HordeUpdate", ModuleOwnerKind.Object, (configuration, _) => HordeData.Parse(configuration), data => new HordeUpdate(data)),
    ];
}

// ============================================================================================================ RadiusDecalUpdate

/// <summary>
/// <c>RadiusDecalUpdate.cpp</c> only keeps the targeting-circle decal of a super weapon (<c>createRadiusDecal</c>/<c>killRadiusDecal</c>, a terrain decal per frame) and has no module
/// data: it is presentation. The simulation module exists so objects that carry it (<c>GLAScudStorm</c>, the other super-weapon sites) can be created; it keeps no state.
/// </summary>
public sealed class RadiusDecalUpdate : SimModule
{
    public override string ModuleName => "RadiusDecalUpdate";
    public override void Describe(IStateWriter writer) { }
    public override void Load(IStateReader reader) { }
}

// ============================================================================================================ TechBuildingBehavior

/// <summary>
/// <c>TechBuildingBehavior.cpp</c>'s fields. <c>PulseFX</c> (an FX list played every <c>PulseFXRate</c> while a playable side owns the building) is presentation and
/// <c>MODELCONDITION_CAPTURED</c> is a model state; only the rate is kept (as ticks, for a presentation layer that wants it).
/// </summary>
public sealed record TechBuildingData(long PulseRateTicks) : ModuleData
{
    public static TechBuildingData Parse(RatedModuleConfiguration configuration) => new(ModuleConfigReader.Ticks(configuration, "PulseFXRate", 0));
}

/// <summary>
/// <c>TechBuildingBehavior::onDie</c>: a tech building (oil derrick, artillery platform, repair bay, hospital) that is destroyed returns to the neutral player, so a
/// captured one does not keep belonging to its capturer as a wreck. Capturing wakes the update in the reference only to set the captured model condition and pulse its FX,
/// both presentation, so nothing happens on capture here.
/// </summary>
public sealed class TechBuildingBehavior(TechBuildingData data) : SimModule, IDieModule
{
    public override string ModuleName => "TechBuildingBehavior";
    public TechBuildingData Data => data;

    public void OnDie(IWorldServices world, in DamageInfo info) => world.TransferOwnership(Entity.Id, PlayerId.System);

    public override void Describe(IStateWriter writer) { }
    public override void Load(IStateReader reader) { }
}

// ============================================================================================================ FireWeaponWhenDamagedBehavior

/// <summary>
/// <c>FireWeaponWhenDamagedBehavior.h</c>'s fields: the shared <c>UpgradeMuxData</c>, <c>StartsActive</c>, four reaction weapons (fired once per damaging hit) and four continuous
/// weapons (fired while the object is in that body-damage state), each by body-damage state (pristine, damaged, really damaged, rubble), the damage-type filter
/// (<c>DamageTypes</c>, default all) and the minimum damage dealt (<c>DamageAmount</c>, default 0). <c>OrientToObject</c>, <c>DeathFX</c> and the like seen next to it in the
/// shipped data belong to the neighbouring modules, not to this one.
/// </summary>
public sealed record FireWeaponWhenDamagedData(
    UpgradeMuxData UpgradeMux, bool StartsActive, ImmutableArray<string> ReactionWeapons, ImmutableArray<string> ContinuousWeapons, ulong DamageTypesMask, Fixed DamageAmount) : ModuleData
{
    private static readonly string[] States = ["Pristine", "Damaged", "ReallyDamaged", "Rubble"];

    public static FireWeaponWhenDamagedData Parse(RatedModuleConfiguration configuration, UpgradeCatalog upgrades)
    {
        var flags = ModuleConfigReader.Flags(configuration, "DamageTypes");
        return new FireWeaponWhenDamagedData(
            UpgradeMuxData.Parse(configuration, upgrades),
            ModuleConfigReader.Bool(configuration, "StartsActive", false),
            [..States.Select(state => ModuleConfigReader.Text(configuration, "ReactionWeapon" + state))],
            [..States.Select(state => ModuleConfigReader.Text(configuration, "ContinuousWeapon" + state))],
            flags is null ? ulong.MaxValue : ObjectStatusBits.Mask(flags, NameTables.DamageType),
            ModuleConfigReader.Real(configuration, "DamageAmount", Fixed.Zero));
    }
}

/// <summary>
/// <c>FireWeaponWhenDamagedBehavior</c>: once active (<c>StartsActive</c> or a matching upgrade), a damaging hit of an accepted type that dealt at least
/// <see cref="FireWeaponWhenDamagedData.DamageAmount"/> fires the reaction weapon of the object's current body-damage state at the object itself
/// (<c>forceFireWeapon(obj, obj->getPosition())</c>), and the continuous weapon of the current state fires every time it is ready. The reference's per-weapon "ready to
/// fire" status (clip, reload) is the weapon's <c>DelayBetweenShots</c> here; a weapon that is still reloading does not fire. The object keeps the weapon instances' reload
/// timers in its state.
/// </summary>
public sealed class FireWeaponWhenDamagedBehavior(FireWeaponWhenDamagedData data) : SimModule, IUpgradeModule, IDamageModule, IUpdateModule
{
    private readonly UpgradeMuxState _mux = new(data.UpgradeMux);
    private readonly long[] _reactionReady = new long[4];
    private readonly long[] _continuousReady = new long[4];
    private long _nextWake = SimulationTiming.SleepForever;

    public override string ModuleName => "FireWeaponWhenDamagedBehavior";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    private bool HasContinuous => data.ContinuousWeapons.Any(name => name.Length > 0);

    public override void OnSpawn(IWorldServices world)
    {
        if (data.StartsActive) _mux.GiveSelfUpgrade(world, _ => { });
        // The continuous weapon first fires one logic frame of the original after the object appeared or the upgrade came (at 30 Hz the next tick).
        _nextWake = _mux.Executed && HasContinuous ? world.Tick + world.Time.TicksPerOriginalFrame : SimulationTiming.SleepForever;
    }

    public void TryActivate(IWorldServices world, UpgradeMask keyMask)
    {
        if (!_mux.AttemptUpgrade(world, Entity, keyMask, _ => { })) return;
        if (HasContinuous) world.WakeAt(Entity, this, world.Tick + world.Time.TicksPerOriginalFrame);
    }

    public void TryReset(IWorldServices world, UpgradeMask keyMask) => _mux.ResetUpgrade(keyMask);
    public bool WouldUpgrade(UpgradeMask keyMask) => _mux.WouldUpgrade(keyMask);

    public void OnDamage(IWorldServices world, in DamageInfo info, Fixed actualDamageDealt)
    {
        if (!_mux.Executed) return;
        if ((data.DamageTypesMask & (1UL << info.DamageType)) == 0 || actualDamageDealt < data.DamageAmount) return;
        Fire(world, data.ReactionWeapons, _reactionReady, StateOf(world));
    }

    public long Update(IWorldServices world)
    {
        if (!_mux.Executed) return SimulationTiming.SleepForever;
        Fire(world, data.ContinuousWeapons, _continuousReady, StateOf(world));
        return SimulationTiming.SleepNone;
    }

    private int StateOf(IWorldServices world) => Entity.Body is ActiveBody body ? body.DamageState : BodyDamageStates.Pristine;

    private void Fire(IWorldServices world, ImmutableArray<string> weapons, long[] ready, int state)
    {
        var name = weapons[state];
        if (name.Length == 0 || world.Tick < ready[state] || !world.Weapons.TryGet(name, out var weapon)) return;
        // A weapon without a delay fires once per logic frame of the original, at every rate.
        ready[state] = world.Tick + world.Time.AtLeastOneFrame(weapon.DelayBetweenShotsTicks);
        world.FireWeaponAt(Entity, weapon, null, Entity.X, Entity.Y, Entity.Z);
    }

    public override void Describe(IStateWriter writer)
    {
        _mux.Describe(writer);
        writer.Int("nextWake", _nextWake);
        for (var state = 0; state < 4; state++)
        {
            // Names are built only for a writer that keeps them; the hash drops them.
            writer.Int(writer.KeepsNames ? "reactionReady" + state : "reactionReady", _reactionReady[state]);
            writer.Int(writer.KeepsNames ? "continuousReady" + state : "continuousReady", _continuousReady[state]);
        }
    }

    public override void Load(IStateReader reader)
    {
        _mux.Load(reader);
        _nextWake = reader.Int("nextWake");
        for (var state = 0; state < 4; state++)
        {
            _reactionReady[state] = reader.Int("reactionReady" + state);
            _continuousReady[state] = reader.Int("continuousReady" + state);
        }
    }
}

// ============================================================================================================ FireWeaponPower

/// <summary><c>FireWeaponPower.cpp</c>: the special-power base fields plus <c>MaxShotsToFire</c> (default 1).</summary>
public sealed record FireWeaponPowerData(SpecialPowerBaseData Base, int MaxShots) : ModuleData
{
    public static FireWeaponPowerData Parse(RatedModuleConfiguration configuration)
        => new(SpecialPowerBaseData.Parse(configuration), checked((int)Math.Max(1, ModuleConfigReader.Int(configuration, "MaxShotsToFire", 1))));
}

/// <summary>
/// <c>FireWeaponPower::doSpecialPower*</c>: reloads every weapon (<c>reloadAllAmmo</c>) and orders the object to attack the target (an object, a position, or its own position) for
/// <see cref="FireWeaponPowerData.MaxShots"/> shots. The reference gives an AI attack order and aims the turrets; the simulation fires the object's current weapon (the locked
/// slot, else the primary one of the active weapon set; <see cref="IWorldServices.FireCurrentWeaponAtPosition"/>, so the slot's clip, reload and last shot are kept) at the target
/// whenever that slot is ready, without the approach or turn time.
/// </summary>
public sealed class FireWeaponPower(FireWeaponPowerData data) : SpecialPowerModuleBase(data.Base), IUpdateModule
{
    private long _nextWake = SimulationTiming.SleepForever;
    private int _shotsLeft;
    private SpecialPowerTarget _target;

    public override string ModuleName => "FireWeaponPower";
    public override bool AcceptsTarget(SpecialPowerTargetKind kind) => true;
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    protected override void Apply(IWorldServices world, in SpecialPowerTarget target)
    {
        if (SimulationWorld.ActiveWeaponIndex(Entity) < 0) return;
        world.ReloadAllAmmo(Entity);
        _shotsLeft = data.MaxShots;
        _target = target;
        world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepNone);
    }

    public long Update(IWorldServices world)
    {
        var slot = Entity.WeaponLock != WeaponLockKind.None ? Entity.CurrentWeaponSlot : WeaponSlot.Primary;
        if (_shotsLeft <= 0 || SimulationWorld.WeaponIndexIn(Entity, slot) < 0) return SimulationTiming.SleepForever;
        var weapon = world.Weapons[SimulationWorld.WeaponIndexIn(Entity, slot)];
        // A slot that is still between shots (or reloading) waits; one that reloads at an airfield never becomes ready by itself, so the power gives up.
        if (world.Tick < Entity.ReadyTickOf(slot)) return Entity.ReadyTickOf(slot) >= SimulationTiming.SleepForever ? StopShooting() : Entity.ReadyTickOf(slot) - world.Tick;
        var victim = _target.Kind == SpecialPowerTargetKind.Object ? world.FindEntity(_target.Entity) : null;
        if (_target.Kind == SpecialPowerTargetKind.Object && (victim is null || victim.EffectivelyDead)) { _shotsLeft = 0; return SimulationTiming.SleepForever; }
        Fixed x = Entity.X, y = Entity.Y, z = Entity.Z;
        if (victim is not null) (x, y, z) = (victim.X, victim.Y, victim.Z);
        else if (_target.Kind == SpecialPowerTargetKind.Location) (x, y) = (_target.X, _target.Y);
        if (!world.FireCurrentWeaponAtPosition(Entity, x, y, z, victim)) return StopShooting();
        _shotsLeft--;
        // At least one logic frame of the original between two shots, at every rate.
        return _shotsLeft <= 0 ? SimulationTiming.SleepForever : world.Time.AtLeastOneFrame(weapon.DelayBetweenShotsTicks);
    }

    private long StopShooting()
    {
        _shotsLeft = 0;
        return SimulationTiming.SleepForever;
    }

    public override void Describe(IStateWriter writer)
    {
        base.Describe(writer);
        writer.Int("nextWake", _nextWake);
        writer.Int("shotsLeft", _shotsLeft);
        writer.Int("targetKind", (long)_target.Kind);
        writer.UInt("targetEntity", _target.Entity.Value);
        writer.Fixed("targetX", _target.X);
        writer.Fixed("targetY", _target.Y);
    }

    public override void Load(IStateReader reader)
    {
        base.Load(reader);
        _nextWake = reader.Int("nextWake");
        _shotsLeft = checked((int)reader.Int("shotsLeft"));
        _target = new SpecialPowerTarget((SpecialPowerTargetKind)checked((byte)reader.Int("targetKind")), new EntityId(checked((uint)reader.UInt("targetEntity"))), reader.Fixed("targetX"), reader.Fixed("targetY"));
    }
}

// ============================================================================================================ BaikonurLaunchPower

/// <summary><c>BaikonurLaunchPower.cpp</c>: the special-power base fields plus <c>DetonationObject</c>, the template created where the launch lands.</summary>
public sealed record BaikonurLaunchData(SpecialPowerBaseData Base, string DetonationObject) : ModuleData
{
    public static BaikonurLaunchData Parse(RatedModuleConfiguration configuration)
        => new(SpecialPowerBaseData.Parse(configuration), ModuleConfigReader.Text(configuration, "DetonationObject"));
}

/// <summary>
/// <c>BaikonurLaunchPower::doSpecialPowerAtLocation</c>: creates the detonation object of the power's owner at the target location (the object's own death and weapon do the
/// rest). Without a location (<c>doSpecialPower</c>) the reference only opens the launch doors (<c>MODELCONDITION_DOOR_1_OPENING</c>, a model state); the simulation does nothing
/// beyond the recharge every power shares. A detonation template that does not exist or cannot be instantiated yet is reported as a skipped creation, never dropped silently.
/// </summary>
public sealed class BaikonurLaunchPower(BaikonurLaunchData data) : SpecialPowerModuleBase(data.Base)
{
    public override string ModuleName => "BaikonurLaunchPower";
    public override bool AcceptsTarget(SpecialPowerTargetKind kind) => true;

    protected override void Apply(IWorldServices world, in SpecialPowerTarget target)
    {
        if (target.Kind != SpecialPowerTargetKind.Location || data.DetonationObject.Length == 0) return;
        if (!world.Templates.TryGet(data.DetonationObject, out var template) || !template.IsInstantiable)
        {
            world.Emit(SimEventKind.CreationSkipped, Entity.Id, EntityId.Invalid, data.DetonationObject, data.DetonationObject.Length > 0 && !world.Templates.TryGet(data.DetonationObject, out _) ? CreationSkipReasons.UnknownTemplate : CreationSkipReasons.TemplateNotInstantiable);
            return;
        }
        world.RequestSpawn(template.Index, Entity.Owner, target.X, target.Y, Fixed.Zero, Fixed.Zero);
    }
}

// ============================================================================================================ HordeUpdate

/// <summary>
/// <c>HordeUpdate.cpp</c>'s fields: <c>UpdateRate</c> (default one second), the <c>KindOf</c> every counted unit must have, <c>Count</c> (units that make a horde, the unit
/// itself included), <c>Radius</c> (<c>m_minDist</c>), <c>RubOffRadius</c> (default 20), <c>AlliesOnly</c> (default yes), <c>ExactMatch</c> (default no) and
/// <c>AllowedNationalism</c> (default yes). <c>Action</c> has the single value <c>HORDE</c>; <c>FlagSubObjectNames</c> belongs to the removed flag drawing.
/// </summary>
public sealed record HordeData(long UpdateRateTicks, ImmutableArray<string> KindOf, int MinCount, Fixed Radius, Fixed RubOffRadius, bool AlliesOnly, bool ExactMatch, bool AllowedNationalism) : ModuleData
{
    public static HordeData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Ticks(configuration, "UpdateRate", SimulationRate.OriginalFramesPerSecond),
        ModuleConfigReader.Flags(configuration, "KindOf")?.Names ?? [],
        checked((int)ModuleConfigReader.Int(configuration, "Count", 0)),
        ModuleConfigReader.Real(configuration, "Radius", Fixed.Zero),
        ModuleConfigReader.Real(configuration, "RubOffRadius", Fixed.FromInt(20)),
        ModuleConfigReader.Bool(configuration, "AlliesOnly", true),
        ModuleConfigReader.Bool(configuration, "ExactMatch", false),
        ModuleConfigReader.Bool(configuration, "AllowedNationalism", true));
}

/// <summary>
/// <c>HordeUpdate::update</c> + <c>AIUpdateInterface::evaluateMoraleBonus</c>: an object counts the other horde members (objects with a <c>HordeUpdate</c>, the required
/// kinds, optionally the exact same type and only allies) within <c>Radius</c> (bounding spheres, measured on the ground plane here); with at least <c>Count − 1</c> of them it is in
/// the thick of the horde, otherwise it is an honorary member if a true member is within <c>RubOffRadius</c>. A member gets the <c>HORDE</c> weapon bonus condition (cleared
/// when it leaves); the player's <c>Upgrade_Nationalism</c> and <c>Upgrade_Fanaticism</c> set <c>NATIONALISM</c>/<c>FANATICISM</c> on every object that carries a horde module, unless
/// <c>AllowedNationalism</c> forbids it for a member in a horde. Infantry re-evaluates at the update rate; vehicles every tick but scan only once per rate, as the reference does.
/// The terrain decals and sub-object flags it drives are presentation.
/// </summary>
public sealed class HordeUpdate(HordeData data) : SimModule, IUpdateModule
{
    private static readonly int HordeBit = NameTablesPhase5.WeaponBonusCondition.TryIndexOf("HORDE", out var horde) ? horde : throw new InvalidOperationException("No HORDE weapon bonus condition.");
    private static readonly int NationalismBit = NameTablesPhase5.WeaponBonusCondition.TryIndexOf("NATIONALISM", out var nationalism) ? nationalism : throw new InvalidOperationException("No NATIONALISM weapon bonus condition.");
    private static readonly int FanaticismBit = NameTablesPhase5.WeaponBonusCondition.TryIndexOf("FANATICISM", out var fanaticism) ? fanaticism : throw new InvalidOperationException("No FANATICISM weapon bonus condition.");

    private long _nextWake = SimulationTiming.SleepForever;
    private long _lastRefresh;
    private bool _inHorde;
    private bool _trueMember;

    public override string ModuleName => "HordeUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public bool IsInHorde => _inHorde;
    public bool IsTrueHordeMember => _trueMember;

    public override void OnSpawn(IWorldServices world)
    {
        _lastRefresh = world.Tick;
        // The stagger is drawn in logic frames of the original (GameLogicRandomValue(1, delay)) and then scaled, so the drawn value stays on the 30 Hz grid.
        _nextWake = world.Tick + world.Time.DrawFrames(world.LogicRandom, world.Time.TicksPerOriginalFrame, world.Time.AtLeastOneFrame(data.UpdateRateTicks));
    }

    public long Update(IWorldServices world)
    {
        if (Entity.EffectivelyDead) return SimulationTiming.SleepForever;
        var infantry = Entity.Template.IsKindOf("INFANTRY");
        // "getFrame() > m_lastHordeRefreshFrame + m_updateRate": the first frame of the original after the rate has passed (a period of rate + 1 frames).
        if (infantry || world.Tick >= _lastRefresh + data.UpdateRateTicks + world.Time.TicksPerOriginalFrame)
        {
            _lastRefresh = world.Tick;
            Evaluate(world);
            ApplyMorale(world);
        }
        return infantry ? data.UpdateRateTicks : SimulationTiming.SleepNone;
    }

    private bool Counts(IWorldServices world, Entity other)
    {
        if (other == Entity || other.EffectivelyDead || other.DestroyRequested) return false;
        if (data.ExactMatch && other.Template.Index != Entity.Template.Index) return false;
        if (!other.ModuleArray.OfType<HordeUpdate>().Any()) return false;
        foreach (var kind in data.KindOf)
            if (!other.Template.IsKindOf(kind)) return false;
        return !data.AlliesOnly || other.Owner == Entity.Owner || world.RelationshipOf(Entity.Owner, other.Owner) == Relationship.Allies;
    }

    private void Evaluate(IWorldServices world)
    {
        var reach = data.Radius;
        var near = new List<Entity>();
        foreach (var other in world.Entities)
        {
            if (!Counts(world, other)) continue;
            var dx = other.X - Entity.X;
            var dy = other.Y - Entity.Y;
            var limit = reach + Entity.Template.BoundingCircleRadius + other.Template.BoundingCircleRadius;
            if (SquaredReach.Of(limit).Contains(dx * dx + dy * dy)) near.Add(other);
        }
        if (near.Count >= data.MinCount - 1)
        {
            _inHorde = true;
            _trueMember = true;
            return;
        }
        _inHorde = false;
        _trueMember = false;
        var rubOff = SquaredReach.Of(data.RubOffRadius);
        foreach (var other in near)
        {
            if (!other.ModuleArray.OfType<HordeUpdate>().First().IsTrueHordeMember) continue;
            var dx = other.X - Entity.X;
            var dy = other.Y - Entity.Y;
            if (!rubOff.Contains(dx * dx + dy * dy)) continue;
            _inHorde = true;
            break;
        }
    }

    private void ApplyMorale(IWorldServices world)
    {
        var upgrades = world.PlayerUpgrades(Entity.Owner);
        bool Has(string name) => world.Upgrades.TryGet(name, out var info) && upgrades.TestForAny(world.Upgrades.MaskOf([info.Name]));
        var nationalism = Has("Upgrade_Nationalism");
        var fanaticism = Has("Upgrade_Fanaticism");
        if (_inHorde && !data.AllowedNationalism) (nationalism, fanaticism) = (false, false);
        var conditions = Entity.WeaponBonusConditions;
        conditions = _inHorde ? conditions | (1u << HordeBit) : conditions & ~(1u << HordeBit);
        conditions = nationalism ? conditions | (1u << NationalismBit) : conditions & ~(1u << NationalismBit);
        conditions = nationalism && fanaticism ? conditions | (1u << FanaticismBit) : conditions & ~(1u << FanaticismBit);
        Entity.WeaponBonusConditions = conditions;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("lastRefresh", _lastRefresh);
        writer.Bool("inHorde", _inHorde);
        writer.Bool("trueMember", _trueMember);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _lastRefresh = reader.Int("lastRefresh");
        _inHorde = reader.Bool("inHorde");
        _trueMember = reader.Bool("trueMember");
    }
}

// ============================================================================================================ FlightDeckBehavior

/// <summary>
/// <c>FlightDeckBehavior.cpp</c> runs an aircraft carrier's deck: parking spaces, catapult take-off, landing, replacing destroyed aircraft (<c>PayloadTemplate</c>), healing aircraft on
/// the deck. It needs jet aircraft (<c>JetAIUpdate</c>, not simulated) and the parking-place machinery. The module is therefore a shell: the carrier can be created, damaged and
/// destroyed (<c>docs/SCRIPT-ENGINE.md</c> §8), but launches, lands, replaces and heals no aircraft. Without it the one carrier of <c>MD_GLA03</c> would not exist and the mission's
/// ending (destroy the carrier) could not be reached.
/// </summary>
public sealed class FlightDeckBehavior : SimModule
{
    public override string ModuleName => "FlightDeckBehavior";
    public override void Describe(IStateWriter writer) { }
    public override void Load(IStateReader reader) { }
}
