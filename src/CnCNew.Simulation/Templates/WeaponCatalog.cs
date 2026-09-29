using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Templates;

/// <summary>
/// A typed <c>Weapon</c> definition, resolved to simulation units. Phase 5 scope: direct-target hitscan damage only — no
/// projectile travel time (<c>WeaponSpeed</c> is kept for audit but the shot resolves the instant it is fired) and no area
/// splash to bystanders (<c>PrimaryDamageRadius</c>/<c>SecondaryDamage</c> stay in the compiled definition, unused here).
/// <c>WeaponBonus</c> is modeled through <see cref="Bonuses"/> and the world-wide <see cref="WeaponBonusTable"/> (GameData); the
/// unmodified values are the base fields. These are documented simplifications, not silent gaps: <c>GAME-RULES.md</c> names them.
/// </summary>
public sealed record WeaponInfo(
    int Index, string Name, Fixed PrimaryDamage, int DamageType, int DeathType, Fixed AttackRange,
    int ClipSize, long ClipReloadTicks, long DelayBetweenShotsTicks, WeaponBonusTable Bonuses)
{
    // Weapon.cpp Weapon::computeBonus: the multiplier of a field is 1 plus the sum of the (entry - 1) deltas of every set condition,
    // first the GameData global set, then the weapon's own set.
    private Fixed Multiplier(WeaponBonusTable global, uint conditions, int field) => Fixed.One + global.Sum(conditions, field) + Bonuses.Sum(conditions, field);

    public Fixed DamageWith(WeaponBonusTable global, uint conditions, Fixed baseDamage) => baseDamage * Multiplier(global, conditions, WeaponBonusTable.Damage);

    public Fixed RangeWith(WeaponBonusTable global, uint conditions) => AttackRange * Multiplier(global, conditions, WeaponBonusTable.Range);

    public long DelayBetweenShotsWith(WeaponBonusTable global, uint conditions) => ScaledByRateOfFire(global, conditions, DelayBetweenShotsTicks);

    public long ClipReloadWith(WeaponBonusTable global, uint conditions) => ClipReloadTicks == 0 ? 0 : ScaledByRateOfFire(global, conditions, ClipReloadTicks);

    // Weapon.cpp getDelayBetweenShots/getClipReloadTime: floor(time / rate-of-fire multiplier). A multiplier of zero or less (only
    // reachable by a data-side WeaponBonus of -100% or worse) stops the weapon instead of dividing by zero; a shot never comes
    // faster than one tick.
    private long ScaledByRateOfFire(WeaponBonusTable global, uint conditions, long ticks)
    {
        var multiplier = Multiplier(global, conditions, WeaponBonusTable.RateOfFire);
        if (multiplier <= Fixed.Zero) return SimulationTiming.SleepForever;
        return Math.Max(SimulationTiming.SleepNone, (Fixed.FromInt(ticks) / multiplier).Floor());
    }
}

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
    private readonly ImmutableDictionary<string, WeaponInfo> _byName;

    private WeaponCatalog(ImmutableArray<WeaponInfo> weapons)
    {
        Weapons = weapons;
        _byName = weapons.ToImmutableDictionary(value => value.Name, StringComparer.OrdinalIgnoreCase);
    }

    public ImmutableArray<WeaponInfo> Weapons { get; }
    public WeaponInfo this[int index] => Weapons[index];
    public bool TryGet(string name, out WeaponInfo info) => _byName.TryGetValue(name, out info!);

    public static WeaponCatalog Build(CompiledDefinitionGraph graph)
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
            var reload = Field("ClipReloadTime")?.Value.Value is GameTime reloadTime ? SimulationTiming.DurationMillisecondsToTicks(reloadTime.Milliseconds) : 0;
            var delay = Field("DelayBetweenShots")?.Value.Value is ShotDelayValue shotDelay
                ? SimulationTiming.DurationMillisecondsToTicks(shotDelay.Min.Milliseconds) : SimulationTiming.SleepNone;
            weapons.Add(new WeaponInfo(index++, definition.OriginalName, damage, damageType, deathType, range, clipSize, reload, Math.Max(SimulationTiming.SleepNone, delay), WeaponBonusTable.From(definition)));
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
