using CnCNew.Data.Definitions;

namespace CnCNew.Data.Schema;

/// <summary>
/// Key schema of a <c>CrateData</c> definition header (<c>CrateSystem.cpp CrateTemplate::m_crateTemplateFieldParseTable</c>).
/// <c>CrateObject</c> stays a raw <c>"ObjectName Weight"</c> pair: the reference parses it with a private
/// <c>parseCrateCreationEntry</c>, so the simulation catalog resolves and validates it.
/// </summary>
public static class CrateSchema
{
    private const string Table = "CrateSystem.cpp CrateTemplate::m_crateTemplateFieldParseTable";

    public static SchemaSet Crate { get; } = new("Crate",
    [
        new FieldSchema("CreationChance", FieldType.Real, Min: 0, Source: Table),
        new FieldSchema("VeterancyLevel", FieldType.Enum, Table: NameTables.Veterancy, Source: Table),
        new FieldSchema("KilledByType", FieldType.Flags, Table: NameTables.KindOf, Source: Table),
        new FieldSchema("CrateObject", FieldType.Opaque, Repeat: RepeatPolicy.Accumulate, Source: Table + " parseCrateCreationEntry"),
        new FieldSchema("KillerScience", FieldType.Reference, ReferenceKind: DefinitionKind.Science, Source: Table),
        new FieldSchema("OwnedByMaker", FieldType.Bool, Source: Table),
    ]);
}
