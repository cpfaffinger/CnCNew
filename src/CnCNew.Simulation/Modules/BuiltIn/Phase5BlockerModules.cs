using System.Collections.Immutable;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>
/// The next wave of phase-5 blocker modules by frequency (<c>docs/HANDOFF-PHASE5.md</c>): <c>FlammableUpdate</c>,
/// each verified against every real occurrence in <c>.local/phase2-import-a</c>.
/// </summary>
internal static class Phase5BlockerModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<FlammableData>("FlammableUpdate", ModuleOwnerKind.Object, (configuration, _) => FlammableData.Parse(configuration), data => new FlammableUpdate(data)),
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
