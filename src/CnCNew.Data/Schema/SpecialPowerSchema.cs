using CnCNew.Data.Definitions;

namespace CnCNew.Data.Schema;

/// <summary>Key schema of a <c>SpecialPower</c> definition (<c>SpecialPower.cpp SpecialPowerTemplate::m_specialPowerFieldParse</c>).</summary>
public static class SpecialPowerSchema
{
    private const string Table = "SpecialPower.cpp SpecialPowerTemplate::m_specialPowerFieldParse";

    public static SchemaSet SpecialPower { get; } = new("SpecialPower",
    [
        new FieldSchema("ReloadTime", FieldType.DurationMs, Source: Table),
        new FieldSchema("RequiredScience", FieldType.Reference, ReferenceKind: DefinitionKind.Science, Source: Table),
        new FieldSchema("InitiateSound", FieldType.Opaque, Source: Table),
        new FieldSchema("InitiateAtLocationSound", FieldType.Opaque, Source: Table),
        new FieldSchema("PublicTimer", FieldType.Bool, Source: Table),
        new FieldSchema("Enum", FieldType.Enum, Table: NameTablesPhase5.SpecialPowerType, Source: Table),
        new FieldSchema("DetectionTime", FieldType.DurationMs, Source: Table),
        new FieldSchema("SharedSyncedTimer", FieldType.Bool, Source: Table),
        new FieldSchema("ViewObjectDuration", FieldType.DurationMs, Source: Table),
        new FieldSchema("ViewObjectRange", FieldType.Real, Source: Table),
        new FieldSchema("RadiusCursorRadius", FieldType.Real, Source: Table),
        new FieldSchema("ShortcutPower", FieldType.Bool, Source: Table),
        new FieldSchema("AcademyClassify", FieldType.Opaque, Source: Table),
    ]);
}
