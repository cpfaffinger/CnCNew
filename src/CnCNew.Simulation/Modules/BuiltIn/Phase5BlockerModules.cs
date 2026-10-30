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
/// The next wave of phase-5 blocker modules by frequency (<c>docs/handoffs/HANDOFF-PHASE5.md</c>): <c>FlammableUpdate</c>,
/// each verified against every real occurrence in <c>game/content</c>. <c>FireWeaponWhenDeadBehavior</c> was
/// reprioritized ahead of the originally listed <c>CreateCrateDie</c>/<c>PoisonedBehavior</c> after a fresh
/// <c>audit-coverage</c> run showed both of those unlock zero currently-blocked objects on their own (every occurrence
/// is also blocked by a still-missing module), while <c>GarrisonContain</c> (166 alone) and <c>PhysicsBehavior</c> (113
/// alone) each require a substantial new subsystem (object containment; rigid-body/terrain physics) out of scope for a
/// single blocker fix — see docs/handoffs/HANDOFF-PHASE5.md for the updated ordering and rationale.
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
    /// <summary>
    /// The source of the last flame, in whose name the burning damage is dealt - only with the adopted fix <see cref="Rules.GameFix.FlameKillCredit"/>. The original
    /// remembers the burning object itself (<c>m_flameSource = getObject()->getID()</c>), which credits no one; this port deals it in nobody's name, the same outcome.
    /// Written to the state only when set.
    /// </summary>
    private EntityId _flameSource;

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
        if (world.Templates.Rules.Has(Rules.GameFix.FlameKillCredit)) _flameSource = info.Source;
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
        _aflameEndTick = now + world.Time.AtLeastOneFrame(data.AflameDurationTicks);
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
            _damageEndTick = now + world.Time.AtLeastOneFrame(data.AflameDamageDelayTicks);
            world.Damage(Entity.Id, new DamageInfo(_flameSource, data.AflameDamageAmount, DamageTypes.Flame, DeathTypes.Burned, false));
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
        if (_flameSource.IsValid) writer.UInt("flameSource", _flameSource.Value);
    }

    public override void Load(IStateReader reader)
    {
        _aflame = reader.Bool("aflame");
        _aflameEndTick = reader.Int("aflameEndTick");
        _burnedEndTick = reader.Int("burnedEndTick");
        _damageEndTick = reader.Int("damageEndTick");
        _flameDamageLimit = reader.Fixed("flameDamageLimit");
        _lastFlameDamageTick = reader.Int("lastFlameDamageTick");
        _flameSource = reader.Has("flameSource") ? new EntityId(checked((uint)reader.UInt("flameSource"))) : EntityId.Invalid;
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
/// <c>RequestDestroy</c>, and the per-phase <c>FX</c>/<c>OCL</c>/<c>Weapon</c> lists (<see cref="SlowDeathPhases"/>, <c>doPhaseStuff</c>). Not modeled (typed but Opaque/unapplied — all presentation or physics):
/// <c>SinkRate</c>/<c>DestructionAltitude</c> (the visual sinking-into-ground animation),
/// <c>FlingForce</c>/<c>FlingPitch</c> and their
/// variances (a physics impulse on death — no <c>PhysicsBehavior</c> yet). <c>ModifierBonusPerOverkillPercent</c> needs
/// the actual damage dealt at the killing blow, which <see cref="IWorldServices.OnEntityDied"/> does not carry yet
/// (phase 4's <c>IDieModule.OnDie</c> was never wired for it) — a further, explicitly deferred refinement.
/// </summary>
public sealed record SlowDeathData(DieMuxData Mux, int ProbabilityModifier, long SinkDelayTicks, long SinkDelayVarianceTicks, long DestructionDelayTicks, long DestructionDelayVarianceTicks) : DieData(Mux)
{
    /// <summary>The effect lists, creation lists and weapons of the three phases (<c>m_fx</c>, <c>m_ocls</c>, <c>m_weapons</c>); empty when the module names none.</summary>
    public SlowDeathPhases Phases { get; init; } = SlowDeathPhases.None;

    public static SlowDeathData Parse(RatedModuleConfiguration configuration) => new(
        DieMuxData.Parse(configuration),
        checked((int)ModuleConfigReader.Int(configuration, "ProbabilityModifier", 0)),
        ModuleConfigReader.Ticks(configuration, "SinkDelay", 0),
        ModuleConfigReader.Ticks(configuration, "SinkDelayVariance", 0),
        ModuleConfigReader.Ticks(configuration, "DestructionDelay", 0),
        ModuleConfigReader.Ticks(configuration, "DestructionDelayVariance", 0))
    {
        Phases = SlowDeathPhases.Parse(configuration),
    };
}

/// <summary><c>SlowDeathPhaseType</c> (SlowDeathBehavior.h): the beginning, the midpoint and the end of a slow death.</summary>
public enum SlowDeathPhase : byte { Initial = 0, Midpoint = 1, Final = 2 }

/// <summary>
/// The per-phase lists of <c>SlowDeathBehaviorModuleData</c>: <c>FX</c>, <c>OCL</c> and <c>Weapon</c> lines, each <c>&lt;phase&gt; &lt;name&gt;...</c> and appended to the list
/// of its phase (<c>parseFX</c>, <c>parseOCL</c>, <c>parseWeapon</c>). <c>NONE</c> stays in its list as an empty entry, as the reference pushes a null
/// pointer ("could be null! this is OK!"): it takes part in the draw and does nothing. <see cref="HasAny"/> is <c>m_maskOfLoadedEffects</c>: without a single name
/// <c>doPhaseStuff</c> returns before any draw.
/// </summary>
public sealed record SlowDeathPhases(ImmutableArray<ImmutableArray<string>> Fx, ImmutableArray<ImmutableArray<string>> Ocl, ImmutableArray<ImmutableArray<string>> Weapons, bool HasAny)
{
    private static readonly ImmutableArray<ImmutableArray<string>> Empty = [[], [], []];

    public static SlowDeathPhases None { get; } = new(Empty, Empty, Empty, false);

    public static SlowDeathPhases Parse(RatedModuleConfiguration configuration)
    {
        var any = false;
        ImmutableArray<ImmutableArray<string>> Lists(string key)
        {
            var phases = new[] { ImmutableArray.CreateBuilder<string>(), ImmutableArray.CreateBuilder<string>(), ImmutableArray.CreateBuilder<string>() };
            foreach (var field in ModuleConfigReader.All(configuration, key))
            {
                if (field.Value.Value is not Data.Definitions.TaggedReferenceList tagged) throw new UnsupportedModuleConfigurationException(key);
                var phase = tagged.Tag switch { "INITIAL" => 0, "MIDPOINT" => 1, "FINAL" => 2, _ => throw new UnsupportedModuleConfigurationException(key) };
                foreach (var reference in tagged.References)
                {
                    // "NONE" is the reference's null entry. A name no definition carries is one too in the reference; here it is told apart when it is used (an unknown
                    // creation list or weapon is a counted CreationSkipped, an unknown effect list is counted by the presentation).
                    var defined = !reference.IsNone;
                    phases[phase].Add(defined ? reference.Name : string.Empty);
                    any |= defined;
                }
            }
            return [phases[0].ToImmutable(), phases[1].ToImmutable(), phases[2].ToImmutable()];
        }

        var fx = Lists("FX");
        var ocl = Lists("OCL");
        var weapons = Lists("Weapon");
        return any ? new SlowDeathPhases(fx, ocl, weapons, true) : None;
    }

    /// <summary>
    /// <c>SlowDeathBehavior::doPhaseStuff</c> (SlowDeathBehavior.cpp): of each non-empty list of the phase one entry is drawn (<c>GameLogicRandomValue(0, size - 1)</c>, in the
    /// order effect, creation list, weapon, also for a list of one); the effect list is played at the object, the creation list creates at the object, the weapon is fired once
    /// at the object's own position (<c>createAndFireTempWeapon</c>). What a creation list names and the simulation cannot create is counted by the creation itself
    /// (<c>CreationSkipped</c> with its reason).
    /// </summary>
    public void Do(IWorldServices world, Entity entity, SlowDeathPhase phase)
    {
        if (!HasAny) return;
        var index = (int)phase;
        if (Fx[index].Length > 0 && Fx[index][world.LogicRandom.Range(0, Fx[index].Length - 1)] is { Length: > 0 } fx)
            world.Emit(SimEventKind.FxRequested, entity.Id, EntityId.Invalid, fx, 0);
        if (Ocl[index].Length > 0 && Ocl[index][world.LogicRandom.Range(0, Ocl[index].Length - 1)] is { Length: > 0 } ocl)
            world.CreateFromList(entity, ocl, EntityId.Invalid, false);
        if (Weapons[index].Length > 0 && Weapons[index][world.LogicRandom.Range(0, Weapons[index].Length - 1)] is { Length: > 0 } weapon)
            world.FireTempWeapon(entity, weapon, entity.X, entity.Y, entity.Z);
    }

    /// <summary>
    /// <c>GameLogicRandomValue(BEGIN_MIDPOINT_RATIO * m_destructionFrame, END_MIDPOINT_RATIO * m_destructionFrame)</c> with the ratios 0.35f and 0.65f: the bounds of the
    /// midpoint draw for a destruction delay of <paramref name="frames"/> logic frames. The products are single-precision (the logic runs the FPU with 24 bits of precision,
    /// <c>GameLogic.cpp setFPMode</c>) and are cut off to whole frames: 0.35f is 11744051 / 2^25 and 0.65f is 10905190 / 2^24, computed here with whole numbers.
    /// </summary>
    public static (int Low, int High) MidpointBounds(long frames) => (SingleProductTruncated(11744051, 25, frames), SingleProductTruncated(10905190, 24, frames));

    private static int SingleProductTruncated(long mantissa, int shift, long factor)
    {
        if (factor <= 0) return 0;
        var product = checked(mantissa * factor);
        // Round to 24 significant bits, ties to even: the single-precision result of the multiplication.
        var bits = 64 - System.Numerics.BitOperations.LeadingZeroCount((ulong)product);
        if (bits > 24)
        {
            var drop = bits - 24;
            var kept = product >> drop;
            var rest = product & ((1L << drop) - 1);
            var half = 1L << (drop - 1);
            if (rest > half || (rest == half && (kept & 1) == 1)) kept++;
            product = kept << drop;
        }
        return checked((int)(product >> shift));
    }
}

/// <summary>
/// <c>SlowDeathBehavior</c>: on a matching death, every sibling <c>SlowDeathBehavior</c> module on the same object
/// (real data has up to seven, one per death "flavor") enters a weighted lottery keyed by
/// <see cref="ProbabilityWeight"/> (<c>UndeadBody.cpp</c>/<c>SlowDeathBehavior.cpp</c>: <c>total</c> is the sum of every
/// applicable sibling's weight, the draw is <c>GameLogicRandomValue(1, total)</c>, walked cumulatively); the winner
/// alone schedules its destruction, guarded by <see cref="Entity.SlowDeathStarted"/> so the lottery runs at most once
/// (the reference guards this per-object via "is my AI already in a dead state", which only works when the object has
/// an <c>AIUpdateInterface</c> — the entity-level flag here is deliberately more robust, not a silent behavior gap).
/// Both random delay draws and the midpoint draw happen unconditionally, even at zero variance, to keep logic-RNG consumption aligned with
/// the reference's own unconditional <c>GameLogicRandomValue(0, variance)</c> calls.
/// </summary>
public sealed class SlowDeathBehavior(SlowDeathData data) : DieModuleBase(data.Mux), IUpdateModule, ISlowDeathCandidate
{
    /// <summary><c>getDisabledTypesToProcess</c>.</summary>
    public DisabledMask DisabledTypesToProcess => DisabledMask.All;

    private bool _activated;
    private long _destructionTick;
    private long _midpointTick;
    private bool _midpointDone;
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
        // The variances are drawn in logic frames of the original and then scaled: the same draw at every rate, and the result stays on the 30 Hz grid.
        var sink = data.SinkDelayTicks + world.Time.DrawFrames(world.LogicRandom, 0, data.SinkDelayVarianceTicks);
        var destruction = data.DestructionDelayTicks + world.Time.DrawFrames(world.LogicRandom, 0, data.DestructionDelayVarianceTicks);
        _ = sink; // the sinking-into-ground animation is presentation only; only the destruction timer is modeled.
        // beginSlowDeath: the midpoint is drawn between 35 % and 65 % of the destruction delay, in logic frames of the original like the two variances - the third draw,
        // made for every slow death whether or not a phase has anything to play.
        var (low, high) = SlowDeathPhases.MidpointBounds(world.Time.ToOriginalFrames(destruction));
        var midpoint = world.Time.DrawFrames(world.LogicRandom, world.Time.OriginalFrames(low), world.Time.OriginalFrames(high));
        // The update that first looks at either timer is the one a frame after the death (the reference sleeps for the smallest of them, at least to the next frame).
        _destructionTick = world.Tick + world.Time.AtLeastOneFrame(destruction);
        _midpointTick = world.Tick + world.Time.AtLeastOneFrame(midpoint);
        _midpointDone = false;
        world.WakeAt(Entity, this, Math.Min(_midpointTick, _destructionTick));
        data.Phases.Do(world, Entity, SlowDeathPhase.Initial);
    }

    /// <summary>
    /// <c>SlowDeathBehavior::update</c>: the midpoint phase once its frame has come, then - at the destruction frame - the final phase and the removal of the object; both in
    /// one update when they fall together. The module sleeps between the two (the sinking of the reference's update is presentation), and every answer is the rest of the
    /// next timer (docs/DEFECT-LOST-WAKE.md).
    /// </summary>
    public long Update(IWorldServices world)
    {
        if (!_activated) return SimulationTiming.SleepForever;
        if (!_midpointDone && world.Tick >= _midpointTick)
        {
            _midpointDone = true;
            world.MarkDirty(Entity);
            data.Phases.Do(world, Entity, SlowDeathPhase.Midpoint);
        }
        if (world.Tick < _destructionTick) return (_midpointDone ? _destructionTick : Math.Min(_midpointTick, _destructionTick)) - world.Tick;
        data.Phases.Do(world, Entity, SlowDeathPhase.Final);
        world.RequestDestroy(Entity.Id);
        return SimulationTiming.SleepForever;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Bool("activated", _activated);
        writer.Int("destructionTick", _destructionTick);
        writer.Int("nextWake", _nextWake);
        // Written for a slow death that has begun only: an object that lives keeps the bytes it had.
        if (!_activated) return;
        writer.Int("midpointTick", _midpointTick);
        if (_midpointDone) writer.Bool("midpointDone", true);
    }

    public override void Load(IStateReader reader)
    {
        _activated = reader.Bool("activated");
        _destructionTick = reader.Int("destructionTick");
        _nextWake = reader.Int("nextWake");
        _midpointTick = reader.Has("midpointTick") ? reader.Int("midpointTick") : 0;
        _midpointDone = reader.Has("midpointDone") && reader.Bool("midpointDone");
    }
}

/// <summary>
/// What a subclass of <c>SlowDeathBehavior</c> runs through its base calls (F-37): <c>SlowDeathBehavior::beginSlowDeath</c> - the sink, destruction and midpoint draws in this order
/// (the midpoint between 35 % and 65 % of the destruction delay, in frames of the original) and the initial phase's lists - and the phases of <c>SlowDeathBehavior::update</c>, the
/// midpoint once its frame has come and the final one at the destruction frame. Written with the module only while its slow death runs.
/// </summary>
internal sealed class SlowDeathClock
{
    private long _midpointTick;
    private bool _midpointDone;

    /// <summary>The base's begin; returns the destruction delay in ticks (at least one frame of the original).</summary>
    public long Begin(IWorldServices world, Entity entity, SlowDeathData data)
    {
        _ = data.SinkDelayTicks + world.Time.DrawFrames(world.LogicRandom, 0, data.SinkDelayVarianceTicks);
        var destruction = data.DestructionDelayTicks + world.Time.DrawFrames(world.LogicRandom, 0, data.DestructionDelayVarianceTicks);
        var (low, high) = SlowDeathPhases.MidpointBounds(world.Time.ToOriginalFrames(destruction));
        var midpoint = world.Time.DrawFrames(world.LogicRandom, world.Time.OriginalFrames(low), world.Time.OriginalFrames(high));
        _midpointTick = world.Tick + world.Time.AtLeastOneFrame(midpoint);
        _midpointDone = false;
        data.Phases.Do(world, entity, SlowDeathPhase.Initial);
        return world.Time.AtLeastOneFrame(destruction);
    }

    /// <summary>The base's update before the destruction frame: the midpoint phase once its frame has come.</summary>
    public void Step(IWorldServices world, Entity entity, SlowDeathData data)
    {
        if (_midpointDone || world.Tick < _midpointTick) return;
        _midpointDone = true;
        world.MarkDirty(entity);
        data.Phases.Do(world, entity, SlowDeathPhase.Midpoint);
    }

    /// <summary>The base's update at the destruction frame: the midpoint if it has not come yet, then the final phase.</summary>
    public void Final(IWorldServices world, Entity entity, SlowDeathData data)
    {
        Step(world, entity, data);
        data.Phases.Do(world, entity, SlowDeathPhase.Final);
    }

    /// <summary>The next tick the base needs an update in before <paramref name="destructionTick"/>: the midpoint, else the destruction.</summary>
    public long NextDue(long destructionTick) => _midpointDone ? destructionTick : Math.Min(_midpointTick, destructionTick);

    public void Describe(IStateWriter writer)
    {
        writer.Int("midpointTick", _midpointTick);
        if (_midpointDone) writer.Bool("midpointDone", true);
    }

    public void Load(IStateReader reader)
    {
        _midpointTick = reader.Has("midpointTick") ? reader.Int("midpointTick") : 0;
        _midpointDone = reader.Has("midpointDone") && reader.Bool("midpointDone");
    }
}

/// <summary>
/// <c>PhysicsBehavior::getVelocity</c> of an object the port does not move through its physics module: a unit under its locomotor, a wreck on its way down. Units per logic frame of the
/// original, null while the module does not move the object.
/// </summary>
public interface IFrameVelocity
{
    (Fixed X, Fixed Y, Fixed Z)? FrameVelocity(IWorldServices world);
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
/// <c>PartitionFilterRelationship::ALLOW_ENEMIES|ALLOW_NEUTRAL</c>); <c>CanDetectWhileGarrisoned</c>/<c>CanDetectWhileContained</c>
/// are typed but inert. A rider its container encloses is not scanned; a garrison with a rider of kind <c>STEALTH_GARRISON</c> is, and its
/// riders with a <c>StealthUpdate</c> are marked (F-54, <c>PartitionFilterStealthedOrStealthGarrisoned</c> and the building branch of
/// <c>update</c>). <c>UNDER_CONSTRUCTION</c>/<c>SOLD</c> are ported
/// (matching every other module that checks them) even though nothing sets either status bit yet.
/// The scan has no line of sight: a candidate within <see cref="StealthDetectorData.DetectionRange"/> is found behind higher terrain too, as
/// <c>iterateObjectsInRange</c> finds it (Phase 10 had added a terrain test of its own; removed in Phase 41).
/// </summary>
public sealed class StealthDetectorUpdate(StealthDetectorData data) : SimModule, IUpdateModule
{
    /// <summary><c>getDisabledTypesToProcess</c>.</summary>
    public DisabledMask DisabledTypesToProcess => DisabledMask.Held;

    private static readonly int StealthedBit = ObjectStatusBits.Index("STEALTHED");
    private static readonly int SoldBit = ObjectStatusBits.Index("SOLD");
    private long _nextWake = -1;

    public override string ModuleName => "StealthDetectorUpdate";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public override void OnSpawn(IWorldServices world)
        => _nextWake = data.InitiallyDisabled ? SimulationTiming.SleepForever : world.Tick + world.Time.DrawFrames(world.LogicRandom, world.Time.TicksPerOriginalFrame, data.UpdateRateTicks);

    /// <summary><c>StealthDetectorUpdate::setSDEnabled</c> (the strategy center's search-and-destroy plan): an enabled detector scans again from the next tick, a disabled one sleeps.</summary>
    public void SetEnabled(IWorldServices world, bool enabled) => world.WakeAt(Entity, this, enabled ? world.Tick + SimulationTiming.SleepNone : SimulationTiming.SleepForever);

    public long Update(IWorldServices world)
    {
        if (Entity.EffectivelyDead) return SimulationTiming.SleepForever;
        if (Entity.HasStatus(ObjectStatusBits.UnderConstruction)) return SimulationTiming.SleepNone;
        if (Entity.HasStatus(SoldBit)) return SimulationTiming.SleepForever;

        var range = data.DetectionRange > Fixed.Zero ? data.DetectionRange : Entity.Template.VisionRange;
        foreach (var candidate in world.Entities)
        {
            // PartitionFilterStealthedOrStealthGarrisoned first: nearly no object is stealthed or a stealth garrison, and the tests are independent of each other, so their order only
            // decides what is looked at in vain (the relationship is three table lookups per object). The objects that pass, and the order they are marked in, are the same.
            var garrison = candidate.HasStatus(StealthedBit) ? null : candidate.Container as GarrisonContain;
            if (garrison is null && !candidate.HasStatus(StealthedBit)) continue;
            if (garrison is not null && (garrison.ContainCount == 0 || garrison.StealthUnitsContained(world) == 0)) continue;
            // A rider its container encloses is out of the partition (OpenContain::addOrRemoveObjFromWorld): the detector finds its building, never the rider itself.
            if (IsEnclosed(world, candidate)) continue;
            if (candidate.Id == Entity.Id || candidate.EffectivelyDead || world.RelationshipOf(Entity.Owner, candidate.Owner) == Relationship.Allies) continue;
            if (!data.ExtraRequiredKindOf.IsEmpty && !HasAnyKind(candidate.Template, data.ExtraRequiredKindOf)) continue;
            if (HasAnyKind(candidate.Template, data.ExtraForbiddenKindOf)) continue;
            var dx = candidate.X - Entity.X;
            var dy = candidate.Y - Entity.Y;
            if (!SquaredReach.Of(range).Contains(dx * dx + dy * dy)) continue;
            if (StealthOf(candidate) is { } stealth)
            {
                // markAsDetected(m_updateRate + 1): the extra step is one logic frame of the original, so the mark outlasts the next scan by the same game time at every rate.
                stealth.MarkDetected(world, data.UpdateRateTicks + world.Time.TicksPerOriginalFrame);
                continue;
            }

            // "Perhaps they are garrisoning something stealthy": every rider with a StealthUpdate that is not the detector's player's and not its ally, for two frames more than the scan.
            if (garrison is null) continue;
            foreach (var riderId in garrison.Contained)
                if (world.FindEntity(riderId) is { } rider && StealthOf(rider) is { } riderStealth && rider.Owner != Entity.Owner
                    && world.RelationshipOf(Entity.Owner, rider.Owner) != Relationship.Allies)
                    riderStealth.MarkDetected(world, data.UpdateRateTicks + world.Time.OriginalFrames(2));
        }
        // A DetectionRate of 0 scans once per logic frame of the original; the mark above holds one frame longer, so no gap opens.
        return world.Time.AtLeastOneFrame(data.UpdateRateTicks);
    }

    private static StealthUpdate? StealthOf(World.Entity entity)
    {
        foreach (var module in entity.Modules)
            if (module is StealthUpdate stealth) return stealth;
        return null;
    }

    /// <summary>
    /// <c>OpenContain::isEnclosingContainerFor</c> of the object's own container: every container encloses its riders but a garrison with <c>IsEnclosingContainer = No</c>, a Helix its
    /// portable structure, an Overlord its first rider and a parachute.
    /// </summary>
    private static bool IsEnclosed(IWorldServices world, World.Entity entity)
    {
        if (!entity.ContainedBy.IsValid || world.FindEntity(entity.ContainedBy) is not { } container) return false;
        foreach (var module in container.Modules)
            switch (module)
            {
                case GarrisonContain { IsEnclosing: false }: return false;
                case HelixContain helix when helix.PortableStructure == entity.Id: return false;
                case OverlordContain overlord when overlord.FirstRider == entity.Id: return false;
                case ParachuteContain: return false;
            }
        return container.Container is not null;
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
    /// <summary><c>getDisabledTypesToProcess</c>.</summary>
    public DisabledMask DisabledTypesToProcess => DisabledMask.All;

    private long _nextWake = -1;
    /// <summary>Absolute tick of the next self-damage application; <see cref="long.MaxValue"/> means "not scheduled" (the reference's frame 0 sentinel, avoided here since tick 0 is a real tick).</summary>
    private long _poisonDamageTick = long.MaxValue;
    /// <summary>Absolute tick after which the poison wears off (unless it killed us); <see cref="long.MaxValue"/> means "not poisoned".</summary>
    private long _poisonStopTick = long.MaxValue;
    private Fixed _poisonDamageAmount = Fixed.Zero;
    private int _deathType = DeathTypes.Poisoned;
    /// <summary>
    /// The poisoner, in whose name the periodic damage is dealt - only with the adopted fix <see cref="Rules.GameFix.PoisonKillCredit"/>; the original deals it in
    /// nobody's name (<c>m_poisonSource</c> stays invalid), so a kill by poison counts for nobody. Written to the state only when set.
    /// </summary>
    private EntityId _poisonSource;

    public override string ModuleName => "PoisonedBehavior";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public override void OnSpawn(IWorldServices world) => _nextWake = SimulationTiming.SleepForever;

    public void OnDamage(IWorldServices world, in DamageInfo info, Fixed actualDamageDealt)
    {
        if (info.DamageType != DamageTypes.Poison) return;
        _poisonDamageAmount = actualDamageDealt;
        if (world.Templates.Rules.Has(Rules.GameFix.PoisonKillCredit)) _poisonSource = info.Source;
        _poisonStopTick = world.Tick + world.Time.AtLeastOneFrame(data.PoisonDurationTicks);
        var freshDamageTick = world.Tick + world.Time.AtLeastOneFrame(data.PoisonDamageIntervalTicks);
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
            world.Damage(Entity.Id, new DamageInfo(_poisonSource, _poisonDamageAmount, DamageTypes.Unresistable, _deathType, false));
            _poisonDamageTick = world.Tick + world.Time.AtLeastOneFrame(data.PoisonDamageIntervalTicks);
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
        _poisonSource = EntityId.Invalid;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("poisonDamageTick", _poisonDamageTick);
        writer.Int("poisonStopTick", _poisonStopTick);
        writer.Fixed("poisonDamageAmount", _poisonDamageAmount);
        writer.Int("deathType", _deathType);
        if (_poisonSource.IsValid) writer.UInt("poisonSource", _poisonSource.Value);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _poisonDamageTick = reader.Int("poisonDamageTick");
        _poisonStopTick = reader.Int("poisonStopTick");
        _poisonDamageAmount = reader.Fixed("poisonDamageAmount");
        _deathType = checked((int)reader.Int("deathType"));
        _poisonSource = reader.Has("poisonSource") ? new EntityId(checked((uint)reader.UInt("poisonSource"))) : EntityId.Invalid;
    }
}
