using CnCNew.Data.Definitions;

namespace CnCNew.Data.Schema;

/// <summary>
/// Key schema of a <c>Weapon</c> definition header, derived from <c>TheWeaponTemplateFieldParseTable</c>
/// (GeneralsMD/Code/GameEngine/Source/GameLogic/Object/Weapon.cpp). Only the subset the phase-5 combat model reads is typed;
/// the remaining ~60 keys (FX/OCL/exhaust lists, per-veterancy variants, scatter, shockwave, anti-target masks, ...) keep the
/// generic Phase-3/4 typing and are counted as deferred fields, the same policy <see cref="ObjectSchema"/> uses for Opaque keys.
/// </summary>
public static class WeaponSchema
{
    private const string Table = "Weapon.cpp TheWeaponTemplateFieldParseTable";

    public static SchemaSet Weapon { get; } = Build();

    private static SchemaSet Build()
    {
        FieldSchema Real(string name) => new(name, FieldType.Real, Source: Table);
        return new SchemaSet("Weapon",
        [
            Real("PrimaryDamage"),
            new("PrimaryDamageRadius", FieldType.Distance, Source: Table),
            Real("SecondaryDamage"),
            new("SecondaryDamageRadius", FieldType.Distance, Source: Table),
            new("AttackRange", FieldType.Distance, Source: Table),
            new("MinimumAttackRange", FieldType.Distance, Source: Table),
            new("DamageType", FieldType.Enum, Table: NameTables.DamageType, Source: Table + " DamageTypeFlags::parseSingleBitFromINI"),
            new("DeathType", FieldType.Enum, Table: NameTables.DeathType, Source: Table),
            Real("WeaponSpeed"),
            Real("MinWeaponSpeed"),
            new("ClipSize", FieldType.Integer, Min: 0, Max: int.MaxValue, Source: Table),
            new("ClipReloadTime", FieldType.DurationMs, Source: Table),
            // WeaponTemplate::parseShotDelay accepts a plain integer (min == max) or "Min:x Max:y" (4 shipped weapons, e.g.
            // ScudStormMissile, use the labeled form); both convert like INI::scanUnsignedInt (sscanf("%u")).
            new("DelayBetweenShots", FieldType.ShotDelayMs, Source: Table + " WeaponTemplate::parseShotDelay"),
            new("AutoReloadsClip", FieldType.Enum, Table: NameTablesPhase5.WeaponReloadType, Source: Table),
        ]);
    }
}

/// <summary>
/// Key schema of an <c>Armor</c> definition header (<c>Armor.cpp</c>). The repeatable <c>Armor = &lt;Default|DamageType&gt; &lt;percent&gt;</c>
/// line is the only field; <c>RepeatableKeys</c> already indexes it by its first token (Phase 4).
/// </summary>
public static class ArmorSchema
{
    public static SchemaSet Armor { get; } = new("Armor",
    [
        new FieldSchema("Armor", FieldType.CoefficientByEnum, Repeat: RepeatPolicy.IndexedByFirstToken, Table: NameTables.DamageType,
            InnerType: FieldType.Percent, Source: "Armor.cpp ArmorTemplate::parseArmorCoefficients"),
    ]);
}
