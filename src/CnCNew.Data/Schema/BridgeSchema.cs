using CnCNew.Data.Definitions;

namespace CnCNew.Data.Schema;

/// <summary>
/// Key schema of a <c>Bridge</c> block of <c>Roads.ini</c> (<c>TerrainRoads.cpp TerrainRoadType::m_terrainBridgeFieldParseTable</c>). The table is complete: a key it does not name is an
/// error, as it is for the original's parser. <c>TransitionToOCL</c> and <c>TransitionToFX</c> stay text here (<c>Transition:&lt;Damage|Repair&gt; ToState:&lt;state&gt; EffectNum:&lt;n&gt; OCL:&lt;name&gt;</c>,
/// <c>parseTransitionToOCL</c>/<c>parseTransitionToFX</c>); the simulation's bridge catalog takes them apart, and refuses what they do not follow.
/// </summary>
public static class BridgeSchema
{
    private const string Table = "TerrainRoads.cpp TerrainRoadType::m_terrainBridgeFieldParseTable";

    public static SchemaSet Bridge { get; } = new("Bridge",
    [
        new FieldSchema("BridgeScale", FieldType.Real, Source: Table),
        new FieldSchema("ScaffoldObjectName", FieldType.Reference, ReferenceKind: DefinitionKind.Object, Source: Table),
        new FieldSchema("ScaffoldSupportObjectName", FieldType.Reference, ReferenceKind: DefinitionKind.Object, Source: Table),
        new FieldSchema("RadarColor", FieldType.ColorInt, Source: Table),
        new FieldSchema("TransitionEffectsHeight", FieldType.Real, Source: Table),
        new FieldSchema("NumFXPerType", FieldType.Integer, Min: 0, Source: Table),
        new FieldSchema("BridgeModelName", FieldType.Text, Source: Table),
        new FieldSchema("Texture", FieldType.Text, Source: Table),
        new FieldSchema("BridgeModelNameDamaged", FieldType.Text, Source: Table),
        new FieldSchema("TextureDamaged", FieldType.Text, Source: Table),
        new FieldSchema("BridgeModelNameReallyDamaged", FieldType.Text, Source: Table),
        new FieldSchema("TextureReallyDamaged", FieldType.Text, Source: Table),
        new FieldSchema("BridgeModelNameBroken", FieldType.Text, Source: Table),
        new FieldSchema("TextureBroken", FieldType.Text, Source: Table),
        new FieldSchema("TowerObjectNameFromLeft", FieldType.Reference, ReferenceKind: DefinitionKind.Object, Source: Table),
        new FieldSchema("TowerObjectNameFromRight", FieldType.Reference, ReferenceKind: DefinitionKind.Object, Source: Table),
        new FieldSchema("TowerObjectNameToLeft", FieldType.Reference, ReferenceKind: DefinitionKind.Object, Source: Table),
        new FieldSchema("TowerObjectNameToRight", FieldType.Reference, ReferenceKind: DefinitionKind.Object, Source: Table),
        new FieldSchema("DamagedToSound", FieldType.Text, Source: Table),
        new FieldSchema("RepairedToSound", FieldType.Text, Source: Table),
        new FieldSchema("TransitionToOCL", FieldType.Text, Repeat: RepeatPolicy.Accumulate, Source: "TerrainRoads.cpp parseTransitionToOCL"),
        new FieldSchema("TransitionToFX", FieldType.Text, Repeat: RepeatPolicy.Accumulate, Source: "TerrainRoads.cpp parseTransitionToFX"),
    ]);
}
