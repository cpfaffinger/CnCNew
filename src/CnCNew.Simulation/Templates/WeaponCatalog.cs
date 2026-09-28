using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Templates;

/// <summary>
/// A typed <c>Weapon</c> definition, resolved to simulation units. Phase 5 scope: direct-target hitscan damage only — no
/// projectile travel time (<c>WeaponSpeed</c> is kept for audit but the shot resolves the instant it is fired), no area
/// splash to bystanders (<c>PrimaryDamageRadius</c>/<c>SecondaryDamage</c> stay in the compiled definition, unused here), no
/// <c>WeaponBonus</c> condition multipliers (always the baseline 1.0, matching an idle/unconditioned weapon). These are
/// documented simplifications, not silent gaps: <see cref="SIMULATION-CORE"/>/<c>GAME-RULES.md</c> names them.
/// </summary>
public sealed record WeaponInfo(
    int Index, string Name, Fixed PrimaryDamage, int DamageType, int DeathType, Fixed AttackRange,
    int ClipSize, long ClipReloadTicks, long DelayBetweenShotsTicks);

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
            weapons.Add(new WeaponInfo(index++, definition.OriginalName, damage, damageType, deathType, range, clipSize, reload, Math.Max(SimulationTiming.SleepNone, delay)));
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
