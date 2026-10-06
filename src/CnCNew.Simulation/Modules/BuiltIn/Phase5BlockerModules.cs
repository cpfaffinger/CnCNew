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
/// The next wave of phase-5 blocker modules by frequency (<c>docs/HANDOFF-PHASE5.md</c>): <c>FlammableUpdate</c>,
/// each verified against every real occurrence in <c>.local/phase2-import-a</c>. <c>FireWeaponWhenDeadBehavior</c> was
/// reprioritized ahead of the originally listed <c>CreateCrateDie</c>/<c>PoisonedBehavior</c> after a fresh
/// <c>audit-coverage</c> run showed both of those unlock zero currently-blocked objects on their own (every occurrence
/// is also blocked by a still-missing module), while <c>GarrisonContain</c> (166 alone) and <c>PhysicsBehavior</c> (113
/// alone) each require a substantial new subsystem (object containment; rigid-body/terrain physics) out of scope for a
/// single blocker fix — see docs/HANDOFF-PHASE5.md for the updated ordering and rationale.
/// </summary>
internal static class Phase5BlockerModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<FlammableData>("FlammableUpdate", ModuleOwnerKind.Object, (configuration, _) => FlammableData.Parse(configuration), data => new FlammableUpdate(data)),
        new ModuleBinding<TransitionDamageFXData>("TransitionDamageFX", ModuleOwnerKind.Object, (configuration, _) => TransitionDamageFXData.Parse(configuration), data => new TransitionDamageFX(data)),
        new ModuleBinding<SlowDeathData>("SlowDeathBehavior", ModuleOwnerKind.Object, (configuration, _) => SlowDeathData.Parse(configuration), data => new SlowDeathBehavior(data)),
        new ModuleBinding<FireWeaponWhenDeadData>("FireWeaponWhenDeadBehavior", ModuleOwnerKind.Object, FireWeaponWhenDeadData.Parse, data => new FireWeaponWhenDeadBehavior(data)),
        new ModuleBinding<StealthDetectorData>("StealthDetectorUpdate", ModuleOwnerKind.Object, (configuration, _) => StealthDetectorData.Parse(configuration), data => new StealthDetectorUpdate(data)),
        new ModuleBinding<PoisonedData>("PoisonedBehavior", ModuleOwnerKind.Object, (configuration, _) => PoisonedData.Parse(configuration), data => new PoisonedBehavior(data)),
    ];
}

// =============================================================================================== FlammableUpdate

/// <summary>
/// <c>FlammableUpdate.cpp</c> field set. <c>BurningSoundName</c> is presentation (Opaque); the rest drive the ignite/
/// burn/damage timers below.
/// </summary>
public sealed record FlammableData(
    long BurnedDelayTicks, long AflameDurationTicks, long AflameDamageDelayTicks, Fixed AflameDamageAmount,
    Fixed FlameDamageLimit, long FlameDamageExpirationTicks) : ModuleData
{
    public static FlammableData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Ticks(configuration, "BurnedDelay", 0),
        ModuleConfigReader.Ticks(configuration, "AflameDuration", 0),
        ModuleConfigReader.Ticks(configuration, "AflameDamageDelay", 0),
        ModuleConfigReader.Real(configuration, "AflameDamageAmount", Fixed.Zero),
        ModuleConfigReader.Real(configuration, "FlameDamageLimit", Fixed.Zero),
        ModuleConfigReader.Ticks(configuration, "FlameDamageExpiration", 0));
}

/// <summary>
/// <c>FlammableUpdate</c>: accumulates <c>DAMAGE_FLAME</c>/<c>DAMAGE_PARTICLE_BEAM</c> hits against
/// <see cref="FlammableData.FlameDamageLimit"/> (reset once <see cref="FlammableData.FlameDamageExpirationTicks"/> has
/// passed since the last such hit); once the limit is exhausted the object catches fire (<c>AFLAME</c> status),
/// periodically takes <see cref="FlammableData.AflameDamageAmount"/> flame damage, turns <c>BURNED</c> after
/// <see cref="FlammableData.BurnedDelayTicks"/> (if set), and stops burning after
/// <see cref="FlammableData.AflameDurationTicks"/> — permanently if it is already <c>BURNED</c>, otherwise free to
/// ignite again. The periodic self-damage always attributes to <see cref="EntityId.Invalid"/> (the reference
/// remembers the original igniting attacker for experience credit; phase 5 has no experience system, so this is a
/// documented simplification, not a silent one).
/// </summary>
public sealed class FlammableUpdate(FlammableData data) : SimModule, IUpdateModule, IDamageModule
{
    private static readonly int AflameBit = ObjectStatusBits.Index("AFLAME");
    private static readonly int BurnedBit = ObjectStatusBits.Index("BURNED");

    private bool _aflame;
    private long _aflameEndTick;
    private long _burnedEndTick;
    private long _damageEndTick;
    private Fixed _flameDamageLimit;
    private long _lastFlameDamageTick = long.MinValue / 2; // half MinValue: "- FlameDamageExpirationTicks" below can never overflow.
    private long _nextWake = -1;

    public override string ModuleName => "FlammableUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public override void OnSpawn(IWorldServices world)
    {
        _flameDamageLimit = data.FlameDamageLimit;
        _nextWake = SimulationTiming.SleepForever;
    }

    public void OnDamage(IWorldServices world, in DamageInfo info, Fixed actualDamageDealt)
    {
        if (info.DamageType != DamageTypes.Flame && info.DamageType != DamageTypes.ParticleBeam) return;
        if (world.Tick - data.FlameDamageExpirationTicks > _lastFlameDamageTick) _flameDamageLimit = data.FlameDamageLimit;
        _lastFlameDamageTick = world.Tick;
        // Once aflame or already burned, no further ignition bookkeeping (matches the reference's status-bit guard).
        if (_aflame || Entity.HasStatus(BurnedBit)) return;
        _flameDamageLimit -= actualDamageDealt;
        if (_flameDamageLimit <= Fixed.Zero) Ignite(world);
    }

    /// <summary><c>FlammableUpdate::wouldIgnite</c>: the object can still catch fire (<c>FS_NORMAL</c>: not burning, not burned out for good).</summary>
    public bool WouldIgnite => !_aflame && !Entity.HasStatus(BurnedBit);

    /// <summary><c>FlammableUpdate::tryToIgnite</c>: sets a flammable object alight (used by <c>FireSpreadUpdate</c>); false when it burns or burned already.</summary>
    public bool TryToIgnite(IWorldServices world)
    {
        if (!WouldIgnite) return false;
        Ignite(world);
        return true;
    }

    private void Ignite(IWorldServices world)
    {
        _aflame = true;
        Entity.Status |= 1UL << AflameBit;
        var now = world.Tick;
        _aflameEndTick = now + Math.Max(data.AflameDurationTicks, SimulationTiming.SleepNone);
        _burnedEndTick = data.BurnedDelayTicks > 0 ? now + data.BurnedDelayTicks : 0;
        _damageEndTick = data.AflameDamageDelayTicks > 0 ? now + data.AflameDamageDelayTicks : 0;
        world.WakeAt(Entity, this, now + SimulationTiming.SleepNone);
        // tryToIgnite: "bleah. this sucks." — the object's FireSpreadUpdate starts its spreading countdown.
        foreach (var module in Entity.ModuleArray)
            if (module is FireSpreadUpdate spread) spread.StartFireSpreading(world);
    }

    public long Update(IWorldServices world)
    {
        var now = world.Tick;
        if (_damageEndTick != 0 && now >= _damageEndTick)
        {
            _damageEndTick = now + Math.Max(data.AflameDamageDelayTicks, SimulationTiming.SleepNone);
            world.Damage(Entity.Id, new DamageInfo(EntityId.Invalid, data.AflameDamageAmount, DamageTypes.Flame, DeathTypes.Burned, false));
        }
        if (_burnedEndTick != 0 && now >= _burnedEndTick)
        {
            Entity.Status |= 1UL << BurnedBit;
            _burnedEndTick = 0;
        }
        if (_aflameEndTick != 0 && now >= _aflameEndTick)
        {
            _aflame = false;
            Entity.Status &= ~(1UL << AflameBit);
            _aflameEndTick = 0;
            return SimulationTiming.SleepForever;
        }
        return CalcSleep(now);
    }

    private long CalcSleep(long now)
    {
        if (!_aflame || _aflameEndTick == 0) return SimulationTiming.SleepForever;
        var soonest = _aflameEndTick;
        if (_burnedEndTick != 0 && _burnedEndTick < soonest) soonest = _burnedEndTick;
        if (_damageEndTick != 0 && _damageEndTick < soonest) soonest = _damageEndTick;
        return Math.Max(soonest - now, SimulationTiming.SleepNone);
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Bool("aflame", _aflame);
        writer.Int("aflameEndTick", _aflameEndTick);
        writer.Int("burnedEndTick", _burnedEndTick);
        writer.Int("damageEndTick", _damageEndTick);
        writer.Fixed("flameDamageLimit", _flameDamageLimit);
        writer.Int("lastFlameDamageTick", _lastFlameDamageTick);
        writer.Int("nextWake", _nextWake);
    }

    public override void Load(IStateReader reader)
    {
        _aflame = reader.Bool("aflame");
        _aflameEndTick = reader.Int("aflameEndTick");
        _burnedEndTick = reader.Int("burnedEndTick");
        _damageEndTick = reader.Int("damageEndTick");
        _flameDamageLimit = reader.Fixed("flameDamageLimit");
        _lastFlameDamageTick = reader.Int("lastFlameDamageTick");
        _nextWake = reader.Int("nextWake");
    }
}

// ============================================================================================ TransitionDamageFX

/// <summary>
/// <c>TransitionDamageFX.h</c>'s field table is ~111 keys (3 damage-type flag fields, then 12 slots × 3 body-damage-
/// states × 3 effect kinds: FX list, object creation list, particle system). Only <c>DamageOCLTypes</c> and the 36 OCL
/// name slots are mechanically relevant: FX lists and particle systems are pure presentation. The OCL names are kept as
/// text, not resolved references — <c>ObjectCreationList</c> is not yet its own typed definition domain (still
/// <c>Compatibility</c>, see docs/TYPED-DEFINITIONS.md), the same simplification <c>ObjectCreationUpgrade</c> already
/// documents.
/// </summary>
public sealed record TransitionDamageFXData(ulong DamageOclTypesMask, ImmutableArray<string> DamagedOcl, ImmutableArray<string> ReallyDamagedOcl, ImmutableArray<string> RubbleOcl) : ModuleData
{
    public static TransitionDamageFXData Parse(RatedModuleConfiguration configuration)
    {
        ImmutableArray<string> Slots(string prefix)
        {
            var builder = ImmutableArray.CreateBuilder<string>();
            for (var slot = 1; slot <= 12; slot++)
            {
                var name = ModuleConfigReader.Text(configuration, $"{prefix}{slot}");
                if (name.Length > 0) builder.Add(name);
            }
            return builder.ToImmutable();
        }
        // AllNoneDeltaFlags default ALL, matching TransitionDamageFXModuleData's constructor (DAMAGE_TYPE_FLAGS_ALL).
        var flags = ModuleConfigReader.Flags(configuration, "DamageOCLTypes");
        var mask = flags is null ? ulong.MaxValue : ObjectStatusBits.Mask(flags, NameTables.DamageType);
        return new TransitionDamageFXData(mask, Slots("DamagedOCL"), Slots("ReallyDamagedOCL"), Slots("RubbleOCL"));
    }
}

/// <summary>
/// <c>TransitionDamageFX::onBodyDamageStateChange</c>: requests the object creation lists configured for the object's
/// new, worse body-damage state (<c>IS_CONDITION_WORSE(new, old) = new &gt; old</c> — healing back to a better state
/// requests nothing), gated by <see cref="TransitionDamageFXData.DamageOclTypesMask"/> and requiring a resolvable
/// damage source (the reference: <c>if (damageSource && ...)</c> — self-inflicted or sourceless damage never spawns
/// an OCL here either). Execution of the requested list is a phase-6 concern (same event as <c>CreateObjectDie</c>).
/// </summary>
public sealed class TransitionDamageFX(TransitionDamageFXData data) : SimModule, IDamageModule
{
    public override string ModuleName => "TransitionDamageFX";

    public void OnBodyDamageStateChange(IWorldServices world, in DamageInfo info, int oldState, int newState)
    {
        if (newState <= oldState || !info.Source.IsValid) return;
        if ((data.DamageOclTypesMask & (1UL << info.DamageType)) == 0) return;
        var slots = newState switch
        {
            BodyDamageStates.Damaged => data.DamagedOcl,
            BodyDamageStates.ReallyDamaged => data.ReallyDamagedOcl,
            BodyDamageStates.Rubble => data.RubbleOcl,
            _ => ImmutableArray<string>.Empty,
        };
        foreach (var ocl in slots) world.CreateFromList(Entity, ocl, info.Source, false);
    }

    public override void Describe(IStateWriter writer) { }
    public override void Load(IStateReader reader) { }
}

// ================================================================================================ SlowDeathBehavior

/// <summary>
/// <c>SlowDeathBehavior.h</c>'s 13 keys. Mechanically modeled: the shared <c>DieMuxData</c> filter, the weighted
/// sibling lottery (<c>ProbabilityModifier</c>) and the sink/destruction delay timers that end in an actual
/// <c>RequestDestroy</c>. Not modeled (typed but Opaque/unapplied — all presentation or physics):
/// <c>SinkRate</c>/<c>DestructionAltitude</c> (the visual sinking-into-ground animation), <c>FX</c>/<c>OCL</c>/<c>Weapon</c>
/// (three more RNG-drawn, per-phase effect lists — <c>doPhaseStuff</c>), <c>FlingForce</c>/<c>FlingPitch</c> and their
/// variances (a physics impulse on death — no <c>PhysicsBehavior</c> yet). <c>ModifierBonusPerOverkillPercent</c> needs
/// the actual damage dealt at the killing blow, which <see cref="IWorldServices.OnEntityDied"/> does not carry yet
/// (phase 4's <c>IDieModule.OnDie</c> was never wired for it) — a further, explicitly deferred refinement.
/// </summary>
public sealed record SlowDeathData(DieMuxData Mux, int ProbabilityModifier, long SinkDelayTicks, long SinkDelayVarianceTicks, long DestructionDelayTicks, long DestructionDelayVarianceTicks) : DieData(Mux)
{
    public static SlowDeathData Parse(RatedModuleConfiguration configuration) => new(
        DieMuxData.Parse(configuration),
        checked((int)ModuleConfigReader.Int(configuration, "ProbabilityModifier", 0)),
        ModuleConfigReader.Ticks(configuration, "SinkDelay", 0),
        ModuleConfigReader.Ticks(configuration, "SinkDelayVariance", 0),
        ModuleConfigReader.Ticks(configuration, "DestructionDelay", 0),
        ModuleConfigReader.Ticks(configuration, "DestructionDelayVariance", 0));
}

/// <summary>
/// <c>SlowDeathBehavior</c>: on a matching death, every sibling <c>SlowDeathBehavior</c> module on the same object
/// (real data has up to seven, one per death "flavor") enters a weighted lottery keyed by
/// <see cref="ProbabilityWeight"/> (<c>UndeadBody.cpp</c>/<c>SlowDeathBehavior.cpp</c>: <c>total</c> is the sum of every
/// applicable sibling's weight, the draw is <c>GameLogicRandomValue(1, total)</c>, walked cumulatively); the winner
/// alone schedules its destruction, guarded by <see cref="Entity.SlowDeathStarted"/> so the lottery runs at most once
/// (the reference guards this per-object via "is my AI already in a dead state", which only works when the object has
/// an <c>AIUpdateInterface</c> — the entity-level flag here is deliberately more robust, not a silent behavior gap).
/// Both random delay draws happen unconditionally, even at zero variance, to keep logic-RNG consumption aligned with
/// the reference's own unconditional <c>GameLogicRandomValue(0, variance)</c> calls.
/// </summary>
public sealed class SlowDeathBehavior(SlowDeathData data) : DieModuleBase(data.Mux), IUpdateModule, ISlowDeathCandidate
{
    private bool _activated;
    private long _destructionTick;
    private long _nextWake = -1;

    public override string ModuleName => "SlowDeathBehavior";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public bool IsApplicable(in DamageInfo info) => Mux.IsApplicable(Entity, info);
    public int ProbabilityWeight => Math.Max(data.ProbabilityModifier, 1);

    public override void OnDie(IWorldServices world, in DamageInfo info) => SlowDeathLottery.Run(world, Entity, info, IsApplicable(info));

    public void BeginSlowDeath(IWorldServices world)
    {
        _activated = true;
        // Both draws happen every time, matching the reference's unconditional GameLogicRandomValue(0, variance) calls.
        var sink = data.SinkDelayTicks + world.LogicRandom.Range(0, checked((int)data.SinkDelayVarianceTicks));
        var destruction = data.DestructionDelayTicks + world.LogicRandom.Range(0, checked((int)data.DestructionDelayVarianceTicks));
        _ = sink; // the sinking-into-ground animation is presentation only; only the destruction timer is modeled.
        _destructionTick = world.Tick + Math.Max(destruction, SimulationTiming.SleepNone);
        world.WakeAt(Entity, this, _destructionTick);
    }

    public long Update(IWorldServices world)
    {
        if (_activated && world.Tick >= _destructionTick) world.RequestDestroy(Entity.Id);
        return SimulationTiming.SleepForever;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Bool("activated", _activated);
        writer.Int("destructionTick", _destructionTick);
        writer.Int("nextWake", _nextWake);
    }

    public override void Load(IStateReader reader)
    {
        _activated = reader.Bool("activated");
        _destructionTick = reader.Int("destructionTick");
        _nextWake = reader.Int("nextWake");
    }
}

/// <summary>A module that takes part in the weighted slow-death lottery (<c>SlowDeathBehavior</c> and its subclasses such as <c>HelicopterSlowDeathBehavior</c>).</summary>
public interface ISlowDeathCandidate
{
    bool IsApplicable(in DamageInfo info);
    int ProbabilityWeight { get; }
    void BeginSlowDeath(IWorldServices world);
}

/// <summary>
/// The sibling lottery of <c>SlowDeathBehavior::onDie</c>: every applicable slow-death module of the object weighs in with its
/// <c>ProbabilityModifier</c>, one <c>GameLogicRandomValue(1, total)</c> picks the winner, in module order; at most once per death.
/// </summary>
internal static class SlowDeathLottery
{
    public static void Run(IWorldServices world, Entity entity, in DamageInfo info, bool applicable)
    {
        if (!applicable || entity.SlowDeathStarted) return;
        var deathInfo = info; // 'in' parameters cannot be captured by a lambda.
        var siblings = entity.Modules.OfType<ISlowDeathCandidate>().Where(candidate => candidate.IsApplicable(deathInfo)).ToArray();
        var total = siblings.Sum(candidate => candidate.ProbabilityWeight);
        if (total <= 0) return;
        var roll = world.LogicRandom.Range(1, total);
        var cumulative = 0;
        foreach (var candidate in siblings)
        {
            cumulative += candidate.ProbabilityWeight;
            if (roll > cumulative) continue;
            entity.SlowDeathStarted = true;
            candidate.BeginSlowDeath(world);
            return;
        }
    }
}

// ===================================================================================== FireWeaponWhenDeadBehavior

/// <summary>
/// <c>FireWeaponWhenDeadBehavior.h</c>'s fields: the shared <c>DieMuxData</c> filter, the shared <c>UpgradeMuxData</c>
/// activation state machine (<c>TriggeredBy</c>/<c>ConflictsWith</c>/<c>RemovesUpgrades</c>/<c>RequiresAllTriggers</c>),
/// <c>StartsActive</c> and the single <c>DeathWeapon</c> reference. <c>FXListUpgrade</c> (shared with every upgrade-mux
/// module) is presentation-only.
/// </summary>
public sealed record FireWeaponWhenDeadData(DieMuxData DieMux, UpgradeMuxData UpgradeMux, bool StartsActive, string DeathWeapon) : ModuleData
{
    public static FireWeaponWhenDeadData Parse(RatedModuleConfiguration configuration, UpgradeCatalog upgrades) => new(
        DieMuxData.Parse(configuration),
        UpgradeMuxData.Parse(configuration, upgrades),
        ModuleConfigReader.Bool(configuration, "StartsActive", false),
        ModuleConfigReader.Text(configuration, "DeathWeapon"));
}

/// <summary>
/// <c>FireWeaponWhenDeadBehavior::onDie</c>: once active (either <see cref="FireWeaponWhenDeadData.StartsActive"/> from
/// construction — <c>giveSelfUpgrade</c>, bypassing the activation-mask gate — or a later matching <c>TriggeredBy</c>
/// upgrade), a matching death requests <see cref="FireWeaponWhenDeadData.DeathWeapon"/> fire. Three reference guards are
/// ported exactly: never while <c>UNDER_CONSTRUCTION</c> (cancelling construction, or killing a one-hitpoint building
/// under construction, must not set it off); never if a conflicting upgrade has completed since — checked again here,
/// not just at activation time, because the <c>StartsActive</c> path never went through the conflict check at all (this
/// is how mutually exclusive "pick one detonation weapon" upgrade families such as ChemicalGeneral.ini's demo-trap
/// modules work: the default starts active, a conflicting upgrade silences it and its own sibling fires instead).
/// Firing raises <see cref="SimEventKind.WeaponFireRequested"/> (for presentation, the same event
/// <see cref="InstantDeathBehavior"/> raises) and fires the weapon at the object's own position (<c>createAndFireTempWeapon</c>, Phase 13: until then the request was never executed).
/// </summary>
public sealed class FireWeaponWhenDeadBehavior(FireWeaponWhenDeadData data) : SimModule, IUpgradeModule, IDieModule
{
    private readonly UpgradeMuxState _mux = new(data.UpgradeMux);

    public override string ModuleName => "FireWeaponWhenDeadBehavior";

    public override void OnSpawn(IWorldServices world)
    {
        if (data.StartsActive) _mux.GiveSelfUpgrade(world, _ => { });
    }

    public void TryActivate(IWorldServices world, UpgradeMask keyMask) => _mux.AttemptUpgrade(world, Entity, keyMask, _ => { });
    public void TryReset(IWorldServices world, UpgradeMask keyMask) => _mux.ResetUpgrade(keyMask);
    public bool WouldUpgrade(UpgradeMask keyMask) => _mux.WouldUpgrade(keyMask);

    public void OnDie(IWorldServices world, in DamageInfo info)
    {
        if (!_mux.Executed || !data.DieMux.IsApplicable(Entity, info)) return;
        if (Entity.HasStatus(ObjectStatusBits.UnderConstruction)) return;
        var conflicting = data.UpgradeMux.Conflicting;
        if (Entity.CompletedUpgrades.TestForAny(conflicting) || world.PlayerUpgrades(Entity.Owner).TestForAny(conflicting)) return;
        if (data.DeathWeapon.Length == 0) return;
        world.Emit(SimEventKind.WeaponFireRequested, Entity.Id, info.Source, data.DeathWeapon, 0);
        world.FireTempWeapon(Entity, data.DeathWeapon, Entity.X, Entity.Y, Entity.Z);
    }

    public override void Describe(IStateWriter writer) => _mux.Describe(writer);
    public override void Load(IStateReader reader) => _mux.Load(reader);
}

// ============================================================================================ StealthDetectorUpdate

/// <summary>
/// <c>StealthDetectorUpdate.cpp</c>'s fields. <c>DetectionRate</c> defaults to 1 tick (the reference's bare
/// <c>m_updateRate = 1</c>, a frame count, not a duration default that needs conversion). Presentation-only:
/// <c>PingSound</c>/<c>LoudPingSound</c>, the four <c>IR*ParticleSysName</c>/<c>IRParticleSysBone</c> fields (heat-vision
/// FX), and every radar/audio/UI feedback call in the reference's <c>update()</c>.
/// </summary>
public sealed record StealthDetectorData(
    long UpdateRateTicks, Fixed DetectionRange, bool InitiallyDisabled,
    ImmutableArray<string> ExtraRequiredKindOf, ImmutableArray<string> ExtraForbiddenKindOf) : ModuleData
{
    public static StealthDetectorData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Ticks(configuration, "DetectionRate", 1),
        ModuleConfigReader.Real(configuration, "DetectionRange", Fixed.Zero),
        ModuleConfigReader.Bool(configuration, "InitiallyDisabled", false),
        ModuleConfigReader.Flags(configuration, "ExtraRequiredKindOf")?.Names ?? [],
        ModuleConfigReader.Flags(configuration, "ExtraForbiddenKindOf")?.Names ?? []);
}

/// <summary>
/// <c>StealthDetectorUpdate::update</c>: periodically scans every other object (an O(entities) scan, same precedent as
/// <see cref="AutoHealBehavior"/> — a spatial index is a phase-6 performance concern) for a currently <c>STEALTHED</c>
/// object within range and calls <see cref="StealthUpdate.MarkDetected"/> on it. Two reference simplifications, both
/// project-wide and pre-existing, not introduced by this module: no team/diplomacy system exists yet, so "ally" is
/// approximated as same <see cref="World.Entity.Owner"/> (the reference excludes only actual allies via
/// <c>PartitionFilterRelationship::ALLOW_ENEMIES|ALLOW_NEUTRAL</c>); and no Contain system exists yet, so
/// <c>CanDetectWhileGarrisoned</c>/<c>CanDetectWhileContained</c> are typed but currently inert (an object is never
/// "contained"), and garrisoned-riders' own stealth is never scanned. <c>UNDER_CONSTRUCTION</c>/<c>SOLD</c> are ported
/// (matching every other module that checks them) even though nothing sets either status bit yet.
/// A candidate within <see cref="StealthDetectorData.DetectionRange"/> but behind higher terrain is not detected
/// (<c>TerrainMap.HasHeightLineOfSight</c>, phase 10); without a terrain (synthetic fixtures) every in-range candidate is
/// still reachable, as before.
/// </summary>
public sealed class StealthDetectorUpdate(StealthDetectorData data) : SimModule, IUpdateModule
{
    private static readonly int StealthedBit = ObjectStatusBits.Index("STEALTHED");
    private static readonly int SoldBit = ObjectStatusBits.Index("SOLD");
    private long _nextWake = -1;

    public override string ModuleName => "StealthDetectorUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public override void OnSpawn(IWorldServices world)
        => _nextWake = data.InitiallyDisabled ? SimulationTiming.SleepForever : world.Tick + world.LogicRandom.Range(1, checked((int)data.UpdateRateTicks));

    /// <summary><c>StealthDetectorUpdate::setSDEnabled</c> (the strategy center's search-and-destroy plan): an enabled detector scans again from the next tick, a disabled one sleeps.</summary>
    public void SetEnabled(IWorldServices world, bool enabled) => world.WakeAt(Entity, this, enabled ? world.Tick + SimulationTiming.SleepNone : SimulationTiming.SleepForever);

    /// <summary>
    /// The height a line of sight is drawn from/to for an object: ground position plus its full <c>GeometryHeight</c> (the object's own
    /// top, not a separate per-template eye-height field, which the catalog does not carry — a named simplification).
    /// </summary>
    private static Fixed EyeHeight(World.Entity entity) => entity.Z + entity.Template.GeometryHeight;

    public long Update(IWorldServices world)
    {
        if (Entity.EffectivelyDead) return SimulationTiming.SleepForever;
        if (Entity.HasStatus(ObjectStatusBits.UnderConstruction)) return SimulationTiming.SleepNone;
        if (Entity.HasStatus(SoldBit)) return SimulationTiming.SleepForever;

        var range = data.DetectionRange > Fixed.Zero ? data.DetectionRange : Entity.Template.VisionRange;
        var terrain = world.Terrain;
        foreach (var candidate in world.Entities)
        {
            // The status bit first: nearly no object is stealthed, and the tests are independent of each other, so their order only decides what is
            // looked at in vain (the relationship is three table lookups per object). The objects that pass, and the order they are marked in, are the same.
            if (!candidate.HasStatus(StealthedBit)) continue;
            if (candidate.Id == Entity.Id || candidate.EffectivelyDead || world.RelationshipOf(Entity.Owner, candidate.Owner) == Relationship.Allies) continue;
            if (!data.ExtraRequiredKindOf.IsEmpty && !HasAnyKind(candidate.Template, data.ExtraRequiredKindOf)) continue;
            if (HasAnyKind(candidate.Template, data.ExtraForbiddenKindOf)) continue;
            var dx = candidate.X - Entity.X;
            var dy = candidate.Y - Entity.Y;
            if (dx * dx + dy * dy > range * range) continue;
            if (terrain is not null && !terrain.HasHeightLineOfSight(Entity.X, Entity.Y, EyeHeight(Entity), candidate.X, candidate.Y, EyeHeight(candidate))) continue;
            foreach (var module in candidate.Modules)
            {
                if (module is not StealthUpdate stealth) continue;
                stealth.MarkDetected(world, data.UpdateRateTicks + 1);
                break;
            }
        }
        return Math.Max(data.UpdateRateTicks, SimulationTiming.SleepNone);
    }

    private static bool HasAnyKind(ObjectTemplate template, ImmutableArray<string> kinds)
    {
        foreach (var kind in kinds)
            if (template.IsKindOf(kind)) return true;
        return false;
    }

    public override void Describe(IStateWriter writer) => writer.Int("nextWake", _nextWake);
    public override void Load(IStateReader reader) => _nextWake = reader.Int("nextWake");
}

// =================================================================================== PoisonedBehavior

/// <summary><c>PoisonedBehavior.h</c>'s two keys.</summary>
public sealed record PoisonedData(long PoisonDamageIntervalTicks, long PoisonDurationTicks) : ModuleData
{
    public static PoisonedData Parse(RatedModuleConfiguration configuration) => new(
        ModuleConfigReader.Ticks(configuration, "PoisonDamageInterval", 0),
        ModuleConfigReader.Ticks(configuration, "PoisonDuration", 0));
}

/// <summary>
/// <c>PoisonedBehavior.cpp</c>: a <c>DAMAGE_POISON</c> hit starts (or, on a fresh dose while still poisoned, extends) a
/// periodic self-damage cycle that reapplies the <em>actual</em> damage dealt by the original poison hit
/// (<see cref="IDamageModule.OnDamage"/>'s <c>actualDamageDealt</c>) as <c>DAMAGE_UNRESISTABLE</c> — deliberately not
/// <c>DAMAGE_POISON</c> again, so the periodic ticks never re-trigger <see cref="OnDamage"/> and restart the cycle.
/// A fresh dose while already poisoned always extends <see cref="PoisonedData.PoisonDurationTicks"/> from now, but only
/// ever moves the next damage tick <em>earlier</em> (the reference's <c>min()</c>, never resets it later). Healing cures
/// the poison outright (<see cref="IDamageModule.OnHealing"/>), matching the reference exactly. Presentation-only:
/// the drawable tint status.
/// </summary>
public sealed class PoisonedBehavior(PoisonedData data) : SimModule, IUpdateModule, IDamageModule
{
    private long _nextWake = -1;
    /// <summary>Absolute tick of the next self-damage application; <see cref="long.MaxValue"/> means "not scheduled" (the reference's frame 0 sentinel, avoided here since tick 0 is a real tick).</summary>
    private long _poisonDamageTick = long.MaxValue;
    /// <summary>Absolute tick after which the poison wears off (unless it killed us); <see cref="long.MaxValue"/> means "not poisoned".</summary>
    private long _poisonStopTick = long.MaxValue;
    private Fixed _poisonDamageAmount = Fixed.Zero;
    private int _deathType = DeathTypes.Poisoned;

    public override string ModuleName => "PoisonedBehavior";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public override void OnSpawn(IWorldServices world) => _nextWake = SimulationTiming.SleepForever;

    public void OnDamage(IWorldServices world, in DamageInfo info, Fixed actualDamageDealt)
    {
        if (info.DamageType != DamageTypes.Poison) return;
        _poisonDamageAmount = actualDamageDealt;
        _poisonStopTick = world.Tick + Math.Max(data.PoisonDurationTicks, SimulationTiming.SleepNone);
        var freshDamageTick = world.Tick + Math.Max(data.PoisonDamageIntervalTicks, SimulationTiming.SleepNone);
        _poisonDamageTick = _poisonDamageTick == long.MaxValue ? freshDamageTick : Math.Min(_poisonDamageTick, freshDamageTick);
        _deathType = info.DeathType;
        world.WakeAt(Entity, this, Math.Min(_poisonDamageTick, _poisonStopTick));
    }

    public void OnHealing(IWorldServices world, in DamageInfo info)
    {
        _ = info;
        StopPoisonedEffects();
        world.WakeAt(Entity, this, world.Tick + SimulationTiming.SleepForever);
    }

    public long Update(IWorldServices world)
    {
        if (_poisonStopTick == long.MaxValue) return SimulationTiming.SleepForever; // not poisoned (guards the reference's "hmm, this should not happen").

        if (_poisonDamageTick != long.MaxValue && world.Tick >= _poisonDamageTick)
        {
            world.Damage(Entity.Id, new DamageInfo(EntityId.Invalid, _poisonDamageAmount, DamageTypes.Unresistable, _deathType, false));
            _poisonDamageTick = world.Tick + Math.Max(data.PoisonDamageIntervalTicks, SimulationTiming.SleepNone);
        }
        // Unless the poison killed us: then we stay a pulsating toxic pus ball forever (no more ticks to schedule).
        if (world.Tick >= _poisonStopTick && !Entity.EffectivelyDead) StopPoisonedEffects();

        return CalcSleepTime(world);
    }

    private long CalcSleepTime(IWorldServices world)
        => _poisonStopTick == long.MaxValue ? SimulationTiming.SleepForever : Math.Min(_poisonDamageTick, _poisonStopTick) - world.Tick;

    private void StopPoisonedEffects()
    {
        _poisonDamageTick = long.MaxValue;
        _poisonStopTick = long.MaxValue;
        _poisonDamageAmount = Fixed.Zero;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("poisonDamageTick", _poisonDamageTick);
        writer.Int("poisonStopTick", _poisonStopTick);
        writer.Fixed("poisonDamageAmount", _poisonDamageAmount);
        writer.Int("deathType", _deathType);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _poisonDamageTick = reader.Int("poisonDamageTick");
        _poisonStopTick = reader.Int("poisonStopTick");
        _poisonDamageAmount = reader.Fixed("poisonDamageAmount");
        _deathType = checked((int)reader.Int("deathType"));
    }
}
