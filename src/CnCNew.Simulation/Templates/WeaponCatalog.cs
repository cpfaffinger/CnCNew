using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Templates;

/// <summary>
/// A typed <c>Weapon</c> definition, resolved to simulation units. Radius damage follows <c>WeaponTemplate::dealDamageInternal</c>
/// (primary/secondary radius and damage, <c>RadiusDamageAffects</c>); the directional cone (<c>RadiusDamageAngle</c>), shockwaves and
/// status damage stay in the compiled definition, unused here. <c>WeaponBonus</c> is modeled through <see cref="Bonuses"/> and the
/// world-wide <see cref="WeaponBonusTable"/> (GameData); the unmodified values are the base fields.
/// <para>
/// Flight time (phase 8, <c>WeaponTemplate::fireWeaponTemplate</c>): <see cref="WeaponSpeed"/>/<see cref="MinWeaponSpeed"/> are per tick
/// (<c>INI::parseVelocityReal</c>, default 999999 = "effectively instant"); a weapon with a <see cref="ProjectileObject"/> launches that
/// object, one without lands after <c>ceil(distance / WeaponSpeed)</c> logic frames of the original, counted against the way of one such frame (at once below one
/// frame of flight; <c>SimulationWorld.FlightTicks</c>). <see cref="ScatterRadius"/> and
/// <see cref="ScatterRadiusVsInfantry"/> (<c>m_infantryInaccuracyDist</c>) randomise a projectile's aim point, the detonation effect and
/// creation list are <see cref="DetonationFx"/>/<see cref="DetonationOcl"/> (the veterancy-specific variants are not modeled). These are
/// documented simplifications, not silent gaps: <c>GAME-RULES.md</c> names them.
/// </para>
/// </summary>
public sealed record WeaponInfo(
    int Index, string Name, Fixed PrimaryDamage, int DamageType, int DeathType, Fixed AttackRange,
    int ClipSize, long ClipReloadTicks, long DelayBetweenShotsTicks, WeaponBonusTable Bonuses,
    Fixed PrimaryRadius, Fixed SecondaryDamage, Fixed SecondaryRadius, int AffectsMask)
{
    public const int AffectsSelf = 1, AffectsAllies = 2, AffectsEnemies = 4, AffectsNeutrals = 8, KillsSelf = 16, NotSimilar = 32, NotAirborne = 64;
    // WeaponTemplate ctor default (Weapon.cpp): ALLIES | ENEMIES | NEUTRALS.
    public const int DefaultAffects = AffectsAllies | AffectsEnemies | AffectsNeutrals;

    /// <summary><c>WeaponTemplate</c> ctor: <c>m_weaponSpeed = m_minWeaponSpeed = 999999.0f</c> (per frame, "effectively instant").</summary>
    public static readonly Fixed InstantSpeed = Fixed.FromInt(999999);

    public Fixed WeaponSpeed { get; init; } = InstantSpeed;
    public Fixed MinWeaponSpeed { get; init; } = InstantSpeed;
    public bool ScaleWeaponSpeed { get; init; }
    public Fixed MinimumAttackRange { get; init; }

    /// <summary><c>m_projectileName</c>; empty when the weapon fires no projectile object (<c>None</c> or absent).</summary>
    public string ProjectileObject { get; init; } = string.Empty;

    public Fixed ScatterRadius { get; init; }
    public Fixed ScatterRadiusVsInfantry { get; init; }
    public string DetonationFx { get; init; } = string.Empty;
    public string DetonationOcl { get; init; } = string.Empty;
    public bool DamageDealtAtSelfPosition { get; init; }

    /// <summary><c>MissileCallsOnDie</c> (<c>m_dieOnDetonate</c>): a detonating missile kills itself with <c>DEATH_DETONATED</c> so its die modules run.</summary>
    public bool DieOnDetonate { get; init; }

    /// <summary><c>WEAPON_ANTI_GROUND</c> of <c>m_antiMask</c> (<c>AntiGround = Yes</c>); read by <c>PointDefenseLaserUpdate</c> for targets on the ground.</summary>
    public bool AntiGround { get; init; }

    /// <summary><c>AutoReloadsClip = RETURN_TO_BASE</c> (<c>RETURN_TO_BASE_TO_RELOAD</c>): an emptied clip refills only when the aircraft reloads at an airfield (<c>JetAIUpdate</c>), never by time.</summary>
    public bool ReloadsAtBase { get; init; }
    /// <summary><c>RequestAssistRange</c> (<c>WeaponTemplate::m_requestAssistRange</c>): when this weapon fires at an object, equivalent objects of the same player this close may join in (<c>AssistedTargetingUpdate</c>); 0 = never.</summary>
    public Fixed RequestAssistRange { get; init; }

    /// <summary>
    /// <c>ContinuousFireOne</c>/<c>ContinuousFireTwo</c> (<c>WeaponTemplate::m_continuousFireOneShotsNeeded</c>/<c>…TwoShotsNeeded</c>, default <c>INT_MAX</c>): how many consecutive shots at one target start the
    /// first and the second continuous-fire stage (<c>FiringTracker</c>, <c>SimulationWorld.FiringTracker.cs</c>): the stage is a weapon bonus condition, so the weapon's own
    /// <c>WeaponBonus = CONTINUOUS_FIRE_MEAN/FAST RATE_OF_FIRE …</c> lines speed it up.
    /// </summary>
    public int ContinuousFireOneShots { get; init; } = int.MaxValue;

    /// <summary><see cref="ContinuousFireOneShots"/>, second stage.</summary>
    public int ContinuousFireTwoShots { get; init; } = int.MaxValue;

    /// <summary><c>ContinuousFireCoast</c> (<c>m_continuousFireCoastFrames</c>): how long after the next possible shot the stages are kept without firing; 0 = the stages end at once when the weapon stops.</summary>
    public long ContinuousFireCoastTicks { get; init; }

    /// <summary><c>PreAttackDelay</c> (<c>m_preAttackDelay</c>): how long the weapon winds up (<c>PRE_ATTACK</c>, the model's <c>PREATTACK_A/B/C</c>) before a shot; 0 for none.</summary>
    public long PreAttackDelayTicks { get; init; }

    /// <summary><c>PreAttackType</c> (<c>m_prefireType</c>): whether the delay applies to every shot, to the first shot at a victim or to the first shot of a clip.</summary>
    public WeaponPrefire PreAttackType { get; init; } = WeaponPrefire.PerShot;

    /// <summary>
    /// <c>WeaponTemplate::getPreAttackDelay(bonus)</c>: the delay times the <c>PRE_ATTACK</c> bonus multiplier (truncated, as the reference's integer return does). The reference
    /// truncates logic frames, so the product is truncated in logic frames of the original and only then scaled to ticks of <paramref name="time"/>: the wind-up is the same game
    /// time at every tick rate.
    /// </summary>
    public long PreAttackDelayWith(SimulationTime time, WeaponBonusTable global, uint conditions)
    {
        if (PreAttackDelayTicks == 0) return 0;
        var multiplier = Multiplier(global, conditions, WeaponBonusTable.PreAttack);
        return multiplier <= Fixed.Zero ? 0 : time.OriginalFrames((Fixed.FromInt(time.ToOriginalFrames(PreAttackDelayTicks)) * multiplier).Floor());
    }

    /// <summary>
    /// <c>WeaponTemplate::m_antiMask</c> (<c>WeaponAntiMaskType</c>): which kinds of target the weapon may be fired at. The constructor default is
    /// <see cref="AntiMaskGround"/> alone ("but not air or projectile"); each <c>Anti*</c> key sets or clears its bit (<c>INI::parseBitInInt32</c>).
    /// </summary>
    public int AntiMask { get; init; } = AntiMaskGround;

    public const int AntiMaskAirborneVehicle = 0x01, AntiMaskGround = 0x02, AntiMaskProjectile = 0x04, AntiMaskSmallMissile = 0x08, AntiMaskMine = 0x10,
        AntiMaskAirborneInfantry = 0x20, AntiMaskBallisticMissile = 0x40, AntiMaskParachute = 0x80;

    public Fixed RadiusWith(WeaponBonusTable global, uint conditions, Fixed baseRadius) => baseRadius * Multiplier(global, conditions, WeaponBonusTable.Radius);

    // Weapon.cpp Weapon::computeBonus: the multiplier of a field is 1 plus the sum of the (entry - 1) deltas of every set condition,
    // first the GameData global set, then the weapon's own set.
    private Fixed Multiplier(WeaponBonusTable global, uint conditions, int field) => Fixed.One + global.Sum(conditions, field) + Bonuses.Sum(conditions, field);

    public Fixed DamageWith(WeaponBonusTable global, uint conditions, Fixed baseDamage) => baseDamage * Multiplier(global, conditions, WeaponBonusTable.Damage);

    public Fixed RangeWith(WeaponBonusTable global, uint conditions) => AttackRange * Multiplier(global, conditions, WeaponBonusTable.Range);

    /// <summary>The shortest time between two shots of one weapon, in logic frames of the original: the reference fires a weapon at most once per logic frame.</summary>
    public const long MinimumShotDelayFrames = 1;

    /// <summary><c>WeaponTemplate::getDelayBetweenShots(bonus)</c> in ticks of <paramref name="time"/>, rounded on the original's grid (<see cref="ScaledByRateOfFire(SimulationTime, WeaponBonusTable, uint, long)"/>).</summary>
    public long DelayBetweenShotsWith(SimulationTime time, WeaponBonusTable global, uint conditions) => ScaledByRateOfFire(time, global, conditions, DelayBetweenShotsTicks);

    /// <summary><c>WeaponTemplate::getClipReloadTime(bonus)</c> in ticks of <paramref name="time"/>, rounded on the original's grid.</summary>
    public long ClipReloadWith(SimulationTime time, WeaponBonusTable global, uint conditions) => ClipReloadTicks == 0 ? 0 : ScaledByRateOfFire(time, global, conditions, ClipReloadTicks);

    // Weapon.cpp getDelayBetweenShots/getClipReloadTime: REAL_TO_INT_FLOOR(time / rate-of-fire multiplier), in logic frames. A multiplier of zero or less (only
    // reachable by a data-side WeaponBonus of -100% or worse) stops the weapon instead of dividing by zero; a shot never comes faster than one logic frame of the
    // original. The division is floored in logic frames of the original and only then scaled to ticks, so a rate-of-fire bonus gives the same game time between
    // shots at every tick rate (floor(2t / m) is not 2 * floor(t / m)); at 30 Hz frames are ticks and this is the former expression.
    private long ScaledByRateOfFire(SimulationTime time, WeaponBonusTable global, uint conditions, long ticks)
    {
        var multiplier = Multiplier(global, conditions, WeaponBonusTable.RateOfFire);
        if (multiplier <= Fixed.Zero) return SimulationTiming.SleepForever;
        return time.OriginalFrames(Math.Max(MinimumShotDelayFrames, (Fixed.FromInt(time.ToOriginalFrames(ticks)) / multiplier).Floor()));
    }

    /// <summary>
    /// The same without the time service: floored in ticks, at least one tick. Equal to the overload with a time service at 30 Hz only. Kept for the two callers in
    /// <c>Modules/BuiltIn</c> (<c>CombatModules.cs</c>, <c>JetAIUpdate.cs</c>) until they pass <c>world.Time</c>; new code uses the overloads above.
    /// </summary>
    public long DelayBetweenShotsWith(WeaponBonusTable global, uint conditions) => ScaledByRateOfFire(global, conditions, DelayBetweenShotsTicks);

    /// <summary>See <see cref="DelayBetweenShotsWith(WeaponBonusTable, uint)"/>.</summary>
    public long ClipReloadWith(WeaponBonusTable global, uint conditions) => ClipReloadTicks == 0 ? 0 : ScaledByRateOfFire(global, conditions, ClipReloadTicks);

    private long ScaledByRateOfFire(WeaponBonusTable global, uint conditions, long ticks)
    {
        var multiplier = Multiplier(global, conditions, WeaponBonusTable.RateOfFire);
        if (multiplier <= Fixed.Zero) return SimulationTiming.SleepForever;
        return Math.Max(SimulationTiming.SleepNone, (Fixed.FromInt(ticks) / multiplier).Floor());
    }
}

/// <summary><c>WeaponPrefireType</c> (<c>TheWeaponPrefireNames</c>).</summary>
public enum WeaponPrefire : byte { PerShot, PerAttack, PerClip }

/// <summary>
/// The <c>WeaponBonus = &lt;CONDITION&gt; &lt;FIELD&gt; &lt;percent&gt;</c> lines of one definition (a weapon or GameData) as per-condition,
/// per-field deltas (<c>ratio - 1</c>; a line that is absent contributes nothing). The repeated line is keyed by (condition, field),
/// the last duplicate wins (<c>WeaponBonusSet::parseWeaponBonusSet</c> overwrites the slot).
/// </summary>
public sealed class WeaponBonusTable
{
    public const int Damage = 0, Radius = 1, Range = 2, RateOfFire = 3, PreAttack = 4;
    public const int VeteranCondition = 9, EliteCondition = 10, HeroCondition = 11, PlayerUpgradeCondition = 5;
    public const uint VeterancyConditions = (1u << VeteranCondition) | (1u << EliteCondition) | (1u << HeroCondition);
    public const int ContinuousFireMeanCondition = 2, ContinuousFireFastCondition = 3;

    private static readonly int FieldCount = NameTablesPhase5.WeaponBonusField.Count;
    private readonly Fixed[] _delta;

    public static WeaponBonusTable Empty { get; } = new([]);

    private WeaponBonusTable(Fixed[] delta) => _delta = delta;

    public bool IsEmpty => _delta.Length == 0;

    public static WeaponBonusTable From(GameDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        Fixed[]? delta = null;
        foreach (var field in definition.Fields)
        {
            if (field.Value.Value is not WeaponBonusValue bonus) continue;
            if (!NameTablesPhase5.WeaponBonusCondition.TryIndexOf(bonus.Condition, out var condition)
                || !NameTablesPhase5.WeaponBonusField.TryIndexOf(bonus.Field, out var slot))
                throw new InvalidDataException($"{definition.OriginalName}: unknown WeaponBonus {bonus.Condition} {bonus.Field}.");
            delta ??= new Fixed[NameTablesPhase5.WeaponBonusCondition.Count * FieldCount];
            delta[condition * FieldCount + slot] = Fixed.FromDecimal(bonus.Ratio) - Fixed.One;
        }
        return delta is null ? Empty : new WeaponBonusTable(delta);
    }

    public Fixed Sum(uint conditions, int field)
    {
        if (_delta.Length == 0 || conditions == 0) return Fixed.Zero;
        var sum = Fixed.Zero;
        for (var condition = 0; condition < NameTablesPhase5.WeaponBonusCondition.Count; condition++)
            if ((conditions & (1u << condition)) != 0) sum += _delta[condition * FieldCount + field];
        return sum;
    }
}

public sealed class WeaponCatalog
{
    private static readonly (string Key, int Bit)[] AntiKeys =
    [
        ("AntiAirborneVehicle", WeaponInfo.AntiMaskAirborneVehicle), ("AntiGround", WeaponInfo.AntiMaskGround), ("AntiProjectile", WeaponInfo.AntiMaskProjectile),
        ("AntiSmallMissile", WeaponInfo.AntiMaskSmallMissile), ("AntiMine", WeaponInfo.AntiMaskMine), ("AntiParachute", WeaponInfo.AntiMaskParachute),
        ("AntiAirborneInfantry", WeaponInfo.AntiMaskAirborneInfantry), ("AntiBallisticMissile", WeaponInfo.AntiMaskBallisticMissile),
    ];

    private readonly ImmutableDictionary<string, WeaponInfo> _byName;

    private WeaponCatalog(ImmutableArray<WeaponInfo> weapons)
    {
        Weapons = weapons;
        _byName = weapons.ToImmutableDictionary(value => value.Name, StringComparer.OrdinalIgnoreCase);
    }

    public ImmutableArray<WeaponInfo> Weapons { get; }
    public WeaponInfo this[int index] => Weapons[index];
    public bool TryGet(string name, out WeaponInfo info) => _byName.TryGetValue(name, out info!);

    public static WeaponCatalog Build(CompiledDefinitionGraph graph, SimulationTime timing)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var index = 0;
        var weapons = ImmutableArray.CreateBuilder<WeaponInfo>();
        foreach (var definition in graph.Definitions.OfType<WeaponDefinition>().OrderBy(value => value.Id.ToString(), StringComparer.Ordinal))
        {
            DefinitionField? Field(string name) => definition.Fields.LastOrDefault(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

            var damage = Field("PrimaryDamage")?.Value.Value is decimal primaryDamage ? Fixed.FromDecimal(primaryDamage) : Fixed.Zero;
            // WeaponTemplate ctor default: DAMAGE_EXPLOSION.
            var damageType = Field("DamageType")?.Value.Value is EnumValue damageEnum ? NameTables.DamageType.TryIndexOf(damageEnum.Name, out var damageIndex) ? damageIndex : 0 : 0;
            // WeaponTemplate ctor default: DEATH_NORMAL.
            var deathType = Field("DeathType")?.Value.Value is EnumValue deathEnum ? NameTables.DeathType.TryIndexOf(deathEnum.Name, out var deathIndex) ? deathIndex : 0 : 0;
            var range = Field("AttackRange")?.Value.Value is Distance attackRange ? Fixed.FromDecimal(attackRange.Value) : Fixed.Zero;
            var clipSize = Field("ClipSize")?.Value.Value is long clip ? checked((int)clip) : 0;
            var reload = Field("ClipReloadTime")?.Value.Value is GameTime reloadTime ? timing.DurationMillisecondsToTicks(reloadTime.Milliseconds) : 0;
            // A weapon without DelayBetweenShots, or with 0 ms, fires once per logic frame of the original (the lower bound is this simulation's own; the reference's
            // WeaponTemplate::getDelayBetweenShots would return 0 and fire again in the next logic frame).
            var minimumDelay = timing.OriginalFrames(WeaponInfo.MinimumShotDelayFrames);
            var delay = Field("DelayBetweenShots")?.Value.Value is ShotDelayValue shotDelay
                ? timing.DurationMillisecondsToTicks(shotDelay.Min.Milliseconds) : minimumDelay;
            var primaryRadius = Field("PrimaryDamageRadius")?.Value.Value is Distance primaryRadiusValue ? Fixed.FromDecimal(primaryRadiusValue.Value) : Fixed.Zero;
            var secondaryRadius = Field("SecondaryDamageRadius")?.Value.Value is Distance secondaryRadiusValue ? Fixed.FromDecimal(secondaryRadiusValue.Value) : Fixed.Zero;
            var secondaryDamage = Field("SecondaryDamage")?.Value.Value is decimal secondary ? Fixed.FromDecimal(secondary) : Fixed.Zero;
            var affects = WeaponInfo.DefaultAffects;
            if (Field("RadiusDamageAffects")?.Value.Value is FlagsValue affectsFlags)
            {
                affects = 0;
                foreach (var name in affectsFlags.Names)
                    affects |= 1 << (NameTablesPhase5.WeaponAffectsMask.TryIndexOf(name, out var bit) ? bit : throw new InvalidDataException($"{definition.OriginalName}: unknown RadiusDamageAffects {name}."));
            }
            // Keys without a strict weapon schema keep the generic typing (Text/Distance/Boolean, or the NONE sentinel); read them explicitly.
            string Name(string key) => Field(key)?.Value switch
            {
                null => string.Empty,
                { Kind: DefinitionValueKind.Sentinel } => string.Empty,
                { Value: string text } => text.Trim(),
                var other => other.OriginalText.Trim(),
            };
            Fixed Length(string key) => Field(key)?.Value.Value switch
            {
                Distance distance => Fixed.FromDecimal(distance.Value),
                decimal number => Fixed.FromDecimal(number),
                long integer => Fixed.FromInt(integer),
                null => Fixed.Zero,
                var other => throw new InvalidDataException($"{definition.OriginalName}: '{key}' has the unexpected value {other}."),
            };
            bool Flag(string key) => Field(key)?.Value.Value switch
            {
                bool value => value,
                null => false,
                var other => throw new InvalidDataException($"{definition.OriginalName}: '{key}' has the unexpected value {other}."),
            };
            Fixed Speed(string key) => Field(key)?.Value.Value is decimal perSecond ? timing.PerSecondToPerTick(perSecond) : WeaponInfo.InstantSpeed;
            // Weapon.cpp TheWeaponTemplateFieldParseTable: one boolean key per m_antiMask bit, starting from WEAPON_ANTI_GROUND.
            int AntiMaskOf()
            {
                var mask = WeaponInfo.AntiMaskGround;
                foreach (var (key, bit) in AntiKeys)
                    if (Field(key)?.Value.Value is { } value)
                    {
                        if (value is not bool enabled) throw new InvalidDataException($"{definition.OriginalName}: '{key}' has the unexpected value {value}.");
                        mask = enabled ? mask | bit : mask & ~bit;
                    }
                return mask;
            }
            // Deferred keys (docs: compatibility baseline) keep their generic typing; read their source text as an unsigned number.
            decimal? Number(string key) => Field(key) is { } field
                ? decimal.TryParse(field.Value.OriginalText.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed) && parsed >= 0m
                    ? parsed : throw new InvalidDataException($"{definition.OriginalName}: '{key}' has the unexpected value '{field.Value.OriginalText}'.")
                : null;
            var projectile = Name("ProjectileObject");
            weapons.Add(new WeaponInfo(index++, definition.OriginalName, damage, damageType, deathType, range, clipSize, reload, Math.Max(minimumDelay, delay), WeaponBonusTable.From(definition),
                primaryRadius, secondaryDamage, secondaryRadius, affects)
            {
                WeaponSpeed = Speed("WeaponSpeed"),
                MinWeaponSpeed = Speed("MinWeaponSpeed"),
                ScaleWeaponSpeed = Flag("ScaleWeaponSpeed"),
                MinimumAttackRange = Length("MinimumAttackRange"),
                ProjectileObject = projectile,
                ScatterRadius = Length("ScatterRadius"),
                ScatterRadiusVsInfantry = Length("ScatterRadiusVsInfantry"),
                DetonationFx = Name("ProjectileDetonationFX"),
                DetonationOcl = Name("ProjectileDetonationOCL"),
                DamageDealtAtSelfPosition = Flag("DamageDealtAtSelfPosition"),
                DieOnDetonate = Flag("MissileCallsOnDie"),
                AntiGround = Flag("AntiGround"),
                ReloadsAtBase = Field("AutoReloadsClip")?.Value.Value is EnumValue { Name: var reloadName } && reloadName.Equals("RETURN_TO_BASE", StringComparison.OrdinalIgnoreCase),
                RequestAssistRange = Length("RequestAssistRange"),
                AntiMask = AntiMaskOf(),
                ContinuousFireOneShots = Number("ContinuousFireOne") is { } one ? checked((int)one) : int.MaxValue,
                ContinuousFireTwoShots = Number("ContinuousFireTwo") is { } two ? checked((int)two) : int.MaxValue,
                ContinuousFireCoastTicks = Number("ContinuousFireCoast") is { } coast ? timing.DurationMillisecondsToTicks(coast) : 0,
                PreAttackDelayTicks = Number("PreAttackDelay") is { } preAttack ? timing.DurationMillisecondsToTicks(preAttack) : 0,
                // TheWeaponPrefireNames; the constructor default is PREFIRE_PER_SHOT.
                PreAttackType = Name("PreAttackType").Split([' ', '\t', ';'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.ToUpperInvariant() switch
                {
                    null or "PER_SHOT" => WeaponPrefire.PerShot,
                    "PER_ATTACK" => WeaponPrefire.PerAttack,
                    "PER_CLIP" => WeaponPrefire.PerClip,
                    var other => throw new InvalidDataException($"{definition.OriginalName}: unknown PreAttackType {other}."),
                },
            });
        }
        return new WeaponCatalog(weapons.ToImmutable());
    }
}

/// <summary>
/// The resolved per-<see cref="Data.Definitions.DamageType"/> coefficient array of one <c>ArmorSet</c> selection.
/// Phase 5 scope: only <c>Conditions = None</c> (the default) and <c>Conditions = PLAYER_UPGRADE</c> (the one condition
/// <c>ArmorUpgrade</c> actually sets) are modeled; veterancy-, crate- and second-life armor sets are compiled and typed but
/// not selected (a documented simplification pending the full <c>SparseMatchFinder</c> best-fit chooser).
/// </summary>
public sealed record ArmorInfo(ImmutableArray<Fixed> CoefficientByDamageType)
{
    public static readonly ArmorInfo Default = new([..Enumerable.Repeat(Fixed.One, NameTables.DamageType.Count)]);

    public Fixed Adjust(int damageType, Fixed amount)
    {
        // Armor.cpp ArmorTemplate::adjustDamage: DAMAGE_UNRESISTABLE and DAMAGE_SUBDUAL_UNRESISTABLE bypass armor entirely.
        if (damageType == DamageTypes.Unresistable || damageType == DamageTypes.SubdualUnresistable) return amount;
        var adjusted = amount * CoefficientByDamageType[damageType];
        return adjusted.IsNegative ? Fixed.Zero : adjusted;
    }
}

/// <summary>Resolves <c>Armor &lt;name&gt; ... End</c> definitions into a full per-damage-type coefficient array.</summary>
public static class ArmorCatalog
{
    public static ImmutableDictionary<string, ArmorInfo> Build(CompiledDefinitionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var builder = ImmutableDictionary.CreateBuilder<string, ArmorInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var definition in graph.Definitions.OfType<ArmorDefinition>())
        {
            // ArmorTemplate::clear(): every coefficient defaults to 1.0 (100%) before Default/per-type lines are applied.
            var coefficients = Enumerable.Repeat(Fixed.One, NameTables.DamageType.Count).ToArray();
            foreach (var field in definition.Fields.Where(field => field.Name.Equals("Armor", StringComparison.OrdinalIgnoreCase)))
            {
                if (field.Value.Value is not CoefficientValue { Value.Value: Percentage percent } coefficient) continue;
                var value = Fixed.FromDecimal(percent.Ratio);
                if (coefficient.EnumName is null) { for (var i = 0; i < coefficients.Length; i++) coefficients[i] = value; }
                else if (NameTables.DamageType.TryIndexOf(coefficient.EnumName, out var index)) coefficients[index] = value;
            }
            builder[definition.OriginalName] = new ArmorInfo([..coefficients]);
        }
        return builder.ToImmutable();
    }
}
