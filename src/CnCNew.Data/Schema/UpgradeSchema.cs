using CnCNew.Data.Definitions;

namespace CnCNew.Data.Schema;

/// <summary>Key schema of an <c>Upgrade</c> definition header (<c>Upgrade.cpp UpgradeTemplate::m_upgradeFieldParseTable</c>).</summary>
public static class UpgradeSchema
{
    private const string Table = "Upgrade.cpp UpgradeTemplate::m_upgradeFieldParseTable";

    public static SchemaSet Upgrade { get; } = new("Upgrade",
    [
        new FieldSchema("DisplayName", FieldType.Opaque, Source: Table),
        new FieldSchema("Type", FieldType.Enum, Table: NameTablesPhase5.UpgradeType, Source: Table),
        new FieldSchema("BuildTime", FieldType.Real, Source: Table),
        new FieldSchema("BuildCost", FieldType.Money, Min: 0, Source: Table),
        new FieldSchema("ButtonImage", FieldType.Opaque, Source: Table),
        new FieldSchema("ResearchSound", FieldType.Opaque, Source: Table),
        new FieldSchema("UnitSpecificSound", FieldType.Opaque, Source: Table),
        new FieldSchema("AcademyClassify", FieldType.Opaque, Source: Table),
    ]);
}

/// <summary>Key schema of a <c>Science</c> definition header (<c>Science.cpp ScienceStore::friend_parseScienceDefinition</c>).</summary>
public static class ScienceSchema
{
    private const string Table = "Science.cpp ScienceStore::friend_parseScienceDefinition";

    public static SchemaSet Science { get; } = new("Science",
    [
        new FieldSchema("PrerequisiteSciences", FieldType.ReferenceList, ReferenceKind: DefinitionKind.Science, Source: Table),
        new FieldSchema("SciencePurchasePointCost", FieldType.Integer, Min: 0, Source: Table),
        new FieldSchema("IsGrantable", FieldType.Bool, Source: Table),
        new FieldSchema("DisplayName", FieldType.Opaque, Source: Table),
        new FieldSchema("Description", FieldType.Opaque, Source: Table),
    ]);
}

/// <summary>
/// Key schema of a <c>Locomotor</c> definition header (<c>Locomotor.cpp LocomotorTemplate::getFieldParse</c>). Only the fields the
/// phase-5 straight-line movement model reads are typed as <see cref="FieldType.Real"/> (raw dist/sec, dist/sec², deg/sec — the
/// simulation layer converts to ticks via <c>SimulationTiming</c>, consistent with how <c>BuildTime</c> stays in seconds here);
/// the remaining ~50 cosmetic/suspension/wander fields are deferred.
/// </summary>
public static class LocomotorSchema
{
    private const string Table = "Locomotor.cpp LocomotorTemplate::getFieldParse";

    public static SchemaSet Locomotor { get; } = Build();

    private static SchemaSet Build()
    {
        FieldSchema Real(string name) => new(name, FieldType.Real, Source: Table);
        return new SchemaSet("Locomotor",
        [
            Real("Speed"),
            Real("SpeedDamaged"),
            Real("MinSpeed"),
            Real("MinTurnSpeed"),
            Real("TurnRate"),
            Real("TurnRateDamaged"),
            Real("Acceleration"),
            Real("AccelerationDamaged"),
            Real("Braking"),
            Real("PreferredHeight"),
            Real("SpeedLimitZ"),
            Real("CloseEnoughDist"),
            new FieldSchema("Surfaces", FieldType.Flags, Table: NameTablesPhase5.LocomotorSurface, Source: Table),
            new FieldSchema("StickToGround", FieldType.Bool, Source: Table),
            new FieldSchema("LocomotorWorksWhenDead", FieldType.Bool, Source: Table),
        ]);
    }
}
