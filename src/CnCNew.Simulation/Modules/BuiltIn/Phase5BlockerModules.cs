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
    public static FlammableData Parse(ModuleConfiguration configuration) => new(
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

    private void Ignite(IWorldServices world)
    {
        _aflame = true;
        Entity.Status |= 1UL << AflameBit;
        var now = world.Tick;
        _aflameEndTick = now + Math.Max(data.AflameDurationTicks, SimulationTiming.SleepNone);
        _burnedEndTick = data.BurnedDelayTicks > 0 ? now + data.BurnedDelayTicks : 0;
        _damageEndTick = data.AflameDamageDelayTicks > 0 ? now + data.AflameDamageDelayTicks : 0;
        world.WakeAt(Entity, this, now + SimulationTiming.SleepNone);
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
    public static TransitionDamageFXData Parse(ModuleConfiguration configuration)
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
        foreach (var ocl in slots) world.Emit(SimEventKind.CreationListRequested, Entity.Id, info.Source, ocl, 0);
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
    public static SlowDeathData Parse(ModuleConfiguration configuration) => new(
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
public sealed class SlowDeathBehavior(SlowDeathData data) : DieModuleBase(data.Mux), IUpdateModule
{
    private bool _activated;
    private long _destructionTick;
    private long _nextWake = -1;

    public override string ModuleName => "SlowDeathBehavior";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public bool IsApplicable(in DamageInfo info) => Mux.IsApplicable(Entity, info);
    public int ProbabilityWeight => Math.Max(data.ProbabilityModifier, 1);

    public override void OnDie(IWorldServices world, in DamageInfo info)
    {
        if (!IsApplicable(info) || Entity.SlowDeathStarted) return;
        var deathInfo = info; // 'in' parameters cannot be captured by a lambda.
        var siblings = Entity.Modules.OfType<SlowDeathBehavior>().Where(candidate => candidate.IsApplicable(deathInfo)).ToArray();
        var total = siblings.Sum(candidate => candidate.ProbabilityWeight);
        if (total <= 0) return;
        var roll = world.LogicRandom.Range(1, total);
        var cumulative = 0;
        foreach (var candidate in siblings)
        {
            cumulative += candidate.ProbabilityWeight;
            if (roll > cumulative) continue;
            Entity.SlowDeathStarted = true;
            candidate.BeginSlowDeath(world);
            return;
        }
    }

    private void BeginSlowDeath(IWorldServices world)
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

// ===================================================================================== FireWeaponWhenDeadBehavior

/// <summary>
/// <c>FireWeaponWhenDeadBehavior.h</c>'s fields: the shared <c>DieMuxData</c> filter, the shared <c>UpgradeMuxData</c>
/// activation state machine (<c>TriggeredBy</c>/<c>ConflictsWith</c>/<c>RemovesUpgrades</c>/<c>RequiresAllTriggers</c>),
/// <c>StartsActive</c> and the single <c>DeathWeapon</c> reference. <c>FXListUpgrade</c> (shared with every upgrade-mux
/// module) is presentation-only.
/// </summary>
public sealed record FireWeaponWhenDeadData(DieMuxData DieMux, UpgradeMuxData UpgradeMux, bool StartsActive, string DeathWeapon) : ModuleData
{
    public static FireWeaponWhenDeadData Parse(ModuleConfiguration configuration, UpgradeCatalog upgrades) => new(
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
/// Firing itself only requests the weapon (<see cref="SimEventKind.WeaponFireRequested"/>, the same event
/// <see cref="InstantDeathBehavior"/> already uses): actually resolving splash damage to bystanders is a known,
/// project-wide simplification (see <c>WeaponCatalog</c>'s doc comment — phase 5's combat model is direct-target only).
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

    public void OnDie(IWorldServices world, in DamageInfo info)
    {
        if (!_mux.Executed || !data.DieMux.IsApplicable(Entity, info)) return;
        if (Entity.HasStatus(ObjectStatusBits.UnderConstruction)) return;
        var conflicting = data.UpgradeMux.Conflicting;
        if (Entity.CompletedUpgrades.TestForAny(conflicting) || world.PlayerUpgrades(Entity.Owner).TestForAny(conflicting)) return;
        if (data.DeathWeapon.Length != 0) world.Emit(SimEventKind.WeaponFireRequested, Entity.Id, info.Source, data.DeathWeapon, 0);
    }

    public override void Describe(IStateWriter writer) => _mux.Describe(writer);
    public override void Load(IStateReader reader) => _mux.Load(reader);
}
