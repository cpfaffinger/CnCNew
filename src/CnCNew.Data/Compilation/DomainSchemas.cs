using CnCNew.Data.Definitions;
using CnCNew.Data.Schema;

namespace CnCNew.Data.Compilation;

/// <summary>
/// Partial, non-strict schemas for definition kinds other than <c>Object</c>. Unlike <see cref="ObjectSchema"/> (which types
/// every key of <c>s_objectFieldParseTable</c> and can therefore reject unknown keys), these schemas cover only the fields the
/// phase-5 simulation reads; every other key of a domain that has a schema here still compiles, but is counted as a deferred
/// field (visible in the compatibility inventory) instead of silently falling back to the heuristic Phase-3 typing.
/// </summary>
internal static class DomainSchemas
{
    public static SchemaSet? For(DefinitionKind kind) => kind switch
    {
        DefinitionKind.Weapon => WeaponSchema.Weapon,
        DefinitionKind.Armor => ArmorSchema.Armor,
        DefinitionKind.Upgrade => UpgradeSchema.Upgrade,
        DefinitionKind.Science => ScienceSchema.Science,
        DefinitionKind.Locomotor => LocomotorSchema.Locomotor,
        DefinitionKind.Faction => FactionSchema.Faction,
        DefinitionKind.Crate => CrateSchema.Crate,
        DefinitionKind.GameData or DefinitionKind.GlobalData => GameDataSchema.GameData,
        _ => null,
    };
}
