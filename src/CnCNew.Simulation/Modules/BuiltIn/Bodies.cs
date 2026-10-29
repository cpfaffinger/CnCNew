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
    public static BodyData Parse(RatedModuleConfiguration configuration) => new(
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
public class ActiveBody : SimModule, IBodyModule, IMaxHealthAdjustable, IInitialHealthSettable, IConstructionBody
{
    // ActiveBody.cpp: #define YELLOW_DAMAGE_PERCENT (0.25f)
    private static readonly Fixed YellowDamagePercent = Fixed.FromDecimal(0.25m);

    private readonly string _name;
    private readonly Fixed _initialHealth;

    public ActiveBody(BodyData data, string name = "ActiveBody")
    {
        _name = name;
        MaxHealth = data.MaxHealth;
        Health = data.InitialHealth;
        PreviousHealth = data.InitialHealth;
        _initialHealth = data.InitialHealth;
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

    /// <summary>
    /// <c>ActiveBody::attemptDamage</c> for <c>DAMAGE_KILL_GARRISONED</c> (the microwave tank's building clearer): the floor of the amount of riders of a garrisonable container that
    /// is not immune to it die, in the order they are in it, and the damager scores the kills; the building itself takes nothing.
    /// </summary>
    private void KillGarrisoned(IWorldServices world, in DamageInfo info)
    {
        if (Entity.Container is not { CanBeCleared: true } contain) return;
        var kills = info.Amount.Floor();
        foreach (var id in contain.Contained.ToArray())
        {
            if (kills <= 0) break;
            if (world.FindEntity(id) is not { EffectivelyDead: false } rider) continue;
            world.Damage(rider.Id, new DamageInfo(info.Source, Fixed.Zero, DamageTypes.Unresistable, DeathTypes.Normal, true));
            kills--;
        }
    }

    public virtual DamageOutcome AttemptDamage(IWorldServices world, in DamageInfo info)
    {
        if (Entity.EffectivelyDead) return DamageOutcome.NoEffect;
        // ActiveBody::attemptDamage and attemptHealing: the body applies its armour to whatever reaches it - a weapon's damage, burning, crushing, falling, healing -; the caller
        // passes the raw amount, and the damage modules see it (F-48).
        var amount = world.AdjustDamageByArmor(Entity, info.DamageType, info.Amount);
        if (info.DamageType == DamageTypes.Healing && !info.Kill)
        {
            if (amount <= Fixed.Zero) return DamageOutcome.NoEffect;
            var oldHealState = DamageState;
            InternalChangeHealth(world, amount);
            // ActiveBody::attemptHealing: every heal that is applied becomes the last damage record, whether the health rose or not.
            Entity.LastDamageTick = world.Tick;
            Entity.LastDamageSource = info.Source;
            Entity.LastDamageHealing = true;
            world.MarkDirty(Entity);
            if (Health <= PreviousHealth) return DamageOutcome.NoEffect;
            // ActiveBody.cpp attemptHealing: both callbacks only run once health actually rose.
            foreach (var module in Entity.DamageModules) module.OnHealing(world, info);
            if (DamageState != oldHealState)
                foreach (var module in Entity.DamageModules) module.OnBodyDamageStateChange(world, info, oldHealState, DamageState);
            return DamageOutcome.Applied;
        }
        if (info.DamageType == DamageTypes.KillGarrisoned)
        {
            KillGarrisoned(world, info);
            return DamageOutcome.NoEffect;
        }
        if (DamageTypes.IsSubdual(info.DamageType) || info.DamageType is DamageTypes.KillPilot or DamageTypes.Status)
            return DamageOutcome.NoEffect;
        // ActiveBody::attemptDamage: the body's damage scalar (the strategy center's hold-the-line plan) applies to everything but unresistable damage.
        if (info.DamageType != DamageTypes.Unresistable) amount *= world.DamageScalarOf(Entity);
        if (amount <= Fixed.Zero && !info.Kill) return DamageOutcome.NoEffect;

        if (info.Kill) amount = Health;
        var oldDamageState = DamageState;
        InternalChangeHealth(world, -amount);
        RecordDamage(world, info);
        var actualDamageClipped = PreviousHealth - Health;
        if (Health < PreviousHealth)
        {
            world.Emit(SimEventKind.EntityDamaged, Entity.Id, info.Source, string.Empty, actualDamageClipped.Raw);
            // ActiveBody.cpp attemptDamage: "if health went down, run the damage module callback" — before the
            // fear-sound and death checks below, so it runs even on a killing blow. The modules read out.m_actualDamageDealt: the amount after the armour and the
            // damage scalar, before the health clips it (a killing blow deals more than the health it takes).
            foreach (var module in Entity.DamageModules) module.OnDamage(world, info, amount);
        }
        if (DamageState != oldDamageState)
            foreach (var module in Entity.DamageModules) module.OnBodyDamageStateChange(world, info, oldDamageState, DamageState);
        // ActiveBody.cpp attemptDamage (block 0.3): crossing the 25% threshold has a 25% chance of a fear sound. The
        // random draw itself is simulation state — it must consume the same LogicRandom value the reference does,
        // win or lose, for replay/golden-master parity — the sound is presentation, so only a successful roll emits.
        if (MaxHealth > Fixed.Zero && PreviousHealth / MaxHealth > YellowDamagePercent && Health / MaxHealth < YellowDamagePercent && Health > Fixed.Zero)
            if (world.LogicRandom.Range(0, 99) < 25)
                world.Emit(SimEventKind.VoiceFearRequested, Entity.Id, info.Source, string.Empty, 0);
        // The player's retaliation (logical retaliation mode): friends nearby answer the attacker (F-58). The original does this after the death, which changes nothing here.
        world.RetaliateFor(Entity, info.Source);
        if (Health <= Fixed.Zero && PreviousHealth > Fixed.Zero)
        {
            world.OnEntityDied(Entity, info);
            return DamageOutcome.Killed;
        }
        return Health != PreviousHealth ? DamageOutcome.Applied : DamageOutcome.NoEffect;
    }

    /// <summary>
    /// <c>ActiveBody::attemptDamage</c> after the damage is applied: the record is replaced when it is older than the frame before this one; within this frame and the one before,
    /// only by a damage whose source exists - and, when the recorded source exists too, only by one from a vehicle, infantry or faction structure ("we prefer the one that tells
    /// who the attacker is"). Then the victim's player learns it was attacked when the recorded source exists (<c>Player::setAttackedBy</c>). (F-52)
    /// </summary>
    private void RecordDamage(IWorldServices world, in DamageInfo info)
    {
        var replace = true;
        if (Entity.LastDamageTick >= 0 && world.Time.FramesIn(world.Tick) - world.Time.FramesIn(Entity.LastDamageTick) <= 1)
        {
            var recorded = world.FindEntity(Entity.LastDamageSource);
            var current = world.FindEntity(info.Source);
            replace = current is not null && (recorded is null || current.Template.IsKindOf("VEHICLE") || current.Template.IsKindOf("INFANTRY")
                                              || AirWeaponModules.IsFactionStructure(current));
        }
        if (replace)
        {
            Entity.LastDamageTick = world.Tick;
            Entity.LastDamageSource = info.Source;
            Entity.LastDamageHealing = false;
            world.MarkDirty(Entity);
        }
        if (world.FindEntity(Entity.LastDamageSource) is { } attacker) world.NoteAttacked(Entity.Owner, attacker.Owner);
    }

    /// <summary>
    /// <c>ActiveBody::setInitialHealth</c>: sets the current health to <paramref name="percent"/> of the body's configured initial health.
    /// Only meaningful for freshly created bodies (the reference's <c>m_initialHealth</c> follows <c>setMaxHealth</c>; here it is the configured value).
    /// </summary>
    public void SetInitialHealth(IWorldServices world, int percent)
        => InternalChangeHealth(world, _initialHealth * Fixed.FromInt(percent) / Fixed.FromInt(100) - Health);

    public void ChangeHealthForConstruction(IWorldServices world, Fixed delta) => InternalChangeHealth(world, delta);

    /// <summary><c>BodyModuleInterface::internalChangeHealth</c> as <c>MinefieldBehavior::onDamage</c> uses it: lifts the health to <paramref name="minimum"/> when a hit took it below (called from inside a damage callback, before the death check, so the object never dies).</summary>
    public void RaiseHealthTo(IWorldServices world, Fixed minimum)
    {
        if (Health < minimum) InternalChangeHealth(world, minimum - Health);
    }

    /// <summary><c>MaxHealthUpgrade.cpp</c>: raises (or lowers) the cap; <paramref name="changeType"/> is one of <c>NameTablesPhase5.MaxHealthChangeType</c>, compared case-insensitively (unrecognized text falls back to the default, matching the reference's zero-based enum default).</summary>
    public virtual void AdjustMaxHealth(IWorldServices world, Fixed delta, string changeType)
    {
        var oldMax = MaxHealth;
        var newMax = Fixed.Max(Fixed.Zero, MaxHealth + delta);
        MaxHealth = newMax;
        Health = changeType.ToUpperInvariant() switch
        {
            "PRESERVE_RATIO" => oldMax > Fixed.Zero ? Fixed.Min(newMax, Fixed.MultiplyDivide(Health, newMax, oldMax)) : Health,
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
