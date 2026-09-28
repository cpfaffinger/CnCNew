using CnCNew.Data.Registry;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>
/// Body configuration. Defaults (all zero) equal the <c>ActiveBodyModuleData</c> constructor of the reference.
/// The subdual fields are validated and kept, but subdual damage itself is not simulated in phase 4 (it belongs to the
/// disabled-state system of phase 5): bodies report <see cref="DamageOutcome.NoEffect"/> for subdual damage types.
/// </summary>
public sealed record BodyData(Fixed MaxHealth, Fixed InitialHealth, Fixed SubdualDamageCap, long SubdualHealTicks, Fixed SubdualHealAmount) : ModuleData
{
    public static BodyData Parse(ModuleConfiguration configuration) => new(
        ModuleConfigReader.Real(configuration, "MaxHealth", Fixed.Zero),
        ModuleConfigReader.Real(configuration, "InitialHealth", Fixed.Zero),
        ModuleConfigReader.Real(configuration, "SubdualDamageCap", Fixed.Zero),
        ModuleConfigReader.Ticks(configuration, "SubdualDamageHealRate", 0),
        ModuleConfigReader.Real(configuration, "SubdualDamageHealAmount", Fixed.Zero));
}

/// <summary><c>BodyDamageType</c> (<c>BodyModule.h</c>): PRISTINE &lt; DAMAGED &lt; REALLYDAMAGED &lt; RUBBLE, in ascending "worseness" order (values compare directly, matching the reference's <c>IS_CONDITION_WORSE(a, b) = a &gt; b</c>).</summary>
public static class BodyDamageStates
{
    public const int Pristine = 0;
    public const int Damaged = 1;
    public const int ReallyDamaged = 2;
    public const int Rubble = 3;
}

/// <summary>
/// <c>ActiveBody</c>: health with damage, healing and death. A death is the transition from positive health to zero
/// (<c>currentHealth &lt;= 0 &amp;&amp; prevHealth &gt; 0</c>), which triggers the die modules exactly once.
/// Armor coefficients are applied by the caller (<see cref="Templates.ArmorInfo"/>/<c>SimulationWorld</c>'s combat
/// resolution), not here; damage multipliers remain unmodeled (phase 6+).
/// </summary>
public class ActiveBody : SimModule, IBodyModule, IMaxHealthAdjustable
{
    // ActiveBody.cpp: #define YELLOW_DAMAGE_PERCENT (0.25f)
    private static readonly Fixed YellowDamagePercent = Fixed.FromDecimal(0.25m);

    private readonly string _name;

    public ActiveBody(BodyData data, string name = "ActiveBody")
    {
        _name = name;
        MaxHealth = data.MaxHealth;
        Health = data.InitialHealth;
        PreviousHealth = data.InitialHealth;
    }

    public override string ModuleName => _name;
    public Fixed Health { get; protected set; }
    public Fixed MaxHealth { get; protected set; }
    public Fixed PreviousHealth { get; protected set; }

    /// <summary><c>ActiveBody::calcDamageState</c>'s result, kept current by <see cref="InternalChangeHealth"/> exactly like the reference's <c>setCorrectDamageState</c> call inside <c>internalChangeHealth</c>.</summary>
    public int DamageState { get; private set; }

    public override void OnSpawn(IWorldServices world) => DamageState = CalcDamageState(world);

    private int CalcDamageState(IWorldServices world)
    {
        if (MaxHealth <= Fixed.Zero) return BodyDamageStates.Pristine; // calcDamageState: no GlobalData / no max health both fall back to PRISTINE.
        var ratio = Health / MaxHealth;
        if (ratio > world.UnitDamagedThreshold) return BodyDamageStates.Pristine;
        if (ratio > world.UnitReallyDamagedThreshold) return BodyDamageStates.Damaged;
        if (ratio > Fixed.Zero) return BodyDamageStates.ReallyDamaged;
        return BodyDamageStates.Rubble;
    }

    /// <summary><c>ActiveBody::internalChangeHealth</c>: applies the delta, caps at max and floors at zero, recomputes the damage state.</summary>
    protected virtual void InternalChangeHealth(IWorldServices world, Fixed delta)
    {
        PreviousHealth = Health;
        var health = Health + delta;
        if (health > MaxHealth) health = MaxHealth;
        if (health < Fixed.Zero) health = Fixed.Zero;
        Health = health;
        DamageState = CalcDamageState(world);
        Entity.EffectivelyDead = Health <= Fixed.Zero;
    }

    public virtual DamageOutcome AttemptDamage(IWorldServices world, in DamageInfo info)
    {
        if (Entity.EffectivelyDead) return DamageOutcome.NoEffect;
        var amount = info.Amount;
        if (info.DamageType == DamageTypes.Healing && !info.Kill)
        {
            if (amount <= Fixed.Zero) return DamageOutcome.NoEffect;
            InternalChangeHealth(world, amount);
            return Health != PreviousHealth ? DamageOutcome.Applied : DamageOutcome.NoEffect;
        }
        if (DamageTypes.IsSubdual(info.DamageType) || info.DamageType is DamageTypes.KillPilot or DamageTypes.KillGarrisoned or DamageTypes.Status)
            return DamageOutcome.NoEffect;
        if (amount <= Fixed.Zero && !info.Kill) return DamageOutcome.NoEffect;

        if (info.Kill) amount = Health;
        var oldDamageState = DamageState;
        InternalChangeHealth(world, -amount);
        var actualDamageDealt = PreviousHealth - Health;
        if (Health < PreviousHealth)
        {
            world.Emit(SimEventKind.EntityDamaged, Entity.Id, info.Source, string.Empty, actualDamageDealt.Raw);
            // ActiveBody.cpp attemptDamage: "if health went down, run the damage module callback" — before the
            // fear-sound and death checks below, so it runs even on a killing blow.
            foreach (var module in Entity.DamageModules) module.OnDamage(world, info, actualDamageDealt);
        }
        if (DamageState != oldDamageState)
            foreach (var module in Entity.DamageModules) module.OnBodyDamageStateChange(world, info, oldDamageState, DamageState);
        // ActiveBody.cpp attemptDamage (block 0.3): crossing the 25% threshold has a 25% chance of a fear sound. The
        // random draw itself is simulation state — it must consume the same LogicRandom value the reference does,
        // win or lose, for replay/golden-master parity — the sound is presentation, so only a successful roll emits.
        if (MaxHealth > Fixed.Zero && PreviousHealth / MaxHealth > YellowDamagePercent && Health / MaxHealth < YellowDamagePercent && Health > Fixed.Zero)
            if (world.LogicRandom.Range(0, 99) < 25)
                world.Emit(SimEventKind.VoiceFearRequested, Entity.Id, info.Source, string.Empty, 0);
        if (Health <= Fixed.Zero && PreviousHealth > Fixed.Zero)
        {
            world.OnEntityDied(Entity, info);
            return DamageOutcome.Killed;
        }
        return Health != PreviousHealth ? DamageOutcome.Applied : DamageOutcome.NoEffect;
    }

    /// <summary><c>MaxHealthUpgrade.cpp</c>: raises (or lowers) the cap; <paramref name="changeType"/> is one of <c>NameTablesPhase5.MaxHealthChangeType</c>, compared case-insensitively (unrecognized text falls back to the default, matching the reference's zero-based enum default).</summary>
    public virtual void AdjustMaxHealth(IWorldServices world, Fixed delta, string changeType)
    {
        var oldMax = MaxHealth;
        var newMax = Fixed.Max(Fixed.Zero, MaxHealth + delta);
        MaxHealth = newMax;
        Health = changeType.ToUpperInvariant() switch
        {
            "PRESERVE_RATIO" => oldMax > Fixed.Zero ? Fixed.Min(newMax, Health * newMax / oldMax) : Health,
            "ADD_CURRENT_HEALTH_TOO" => Fixed.Min(newMax, Health + delta),
            "FULLY_HEAL" => newMax,
            _ => Fixed.Min(Health, newMax), // SAME_CURRENTHEALTH
        };
        // setMaxHealth never dispatches onBodyDamageStateChange (it does not go through attemptDamage); it only ever
        // keeps the state itself current, exactly as far as the reference goes.
        DamageState = CalcDamageState(world);
        Entity.EffectivelyDead = Health <= Fixed.Zero;
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Fixed("health", Health);
        writer.Fixed("maxHealth", MaxHealth);
        writer.Fixed("previousHealth", PreviousHealth);
        writer.Int("damageState", DamageState);
    }

    public override void Load(IStateReader reader)
    {
        Health = reader.Fixed("health");
        MaxHealth = reader.Fixed("maxHealth");
        PreviousHealth = reader.Fixed("previousHealth");
        DamageState = checked((int)reader.Int("damageState"));
    }
}

/// <summary><c>StructureBody</c> behaves like <see cref="ActiveBody"/> in phase 4 (the constructor-object link is a phase-5 concern).</summary>
public sealed class StructureBody(BodyData data) : ActiveBody(data, "StructureBody");

/// <summary><c>ImmortalBody</c>: health can never drop below one hit point, so the entity is never marked dead.</summary>
public sealed class ImmortalBody(BodyData data) : ActiveBody(data, "ImmortalBody")
{
    protected override void InternalChangeHealth(IWorldServices world, Fixed delta)
    {
        delta = Fixed.Max(delta, -Health + Fixed.One);
        base.InternalChangeHealth(world, delta);
    }
}

/// <summary><c>HighlanderBody</c>: normal damage is limited to leave one hit point; unresistable damage and kills still kill.</summary>
public sealed class HighlanderBody(BodyData data) : ActiveBody(data, "HighlanderBody")
{
    public override DamageOutcome AttemptDamage(IWorldServices world, in DamageInfo info)
    {
        if (info.DamageType == DamageTypes.Unresistable) return base.AttemptDamage(world, info);
        return base.AttemptDamage(world, info with { Amount = Fixed.Min(info.Amount, Health - Fixed.One) });
    }
}

/// <summary>
/// <c>InactiveBody</c>: no health, effectively dead from the start (props). Only unresistable damage (a kill) has an effect:
/// it runs the die modules once.
/// </summary>
public sealed class InactiveBody : SimModule, IBodyModule
{
    private bool _dieCalled;

    public override string ModuleName => "InactiveBody";
    public Fixed Health => Fixed.Zero;
    public Fixed MaxHealth => Fixed.Zero;

    public override void OnSpawn(IWorldServices world) => Entity.EffectivelyDead = true;

    public DamageOutcome AttemptDamage(IWorldServices world, in DamageInfo info)
    {
        if (info.DamageType != DamageTypes.Unresistable || _dieCalled) return DamageOutcome.NoEffect;
        _dieCalled = true;
        world.OnEntityDied(Entity, info);
        return DamageOutcome.Killed;
    }

    public override void Describe(IStateWriter writer) => writer.Bool("dieCalled", _dieCalled);
    public override void Load(IStateReader reader) => _dieCalled = reader.Bool("dieCalled");
}
