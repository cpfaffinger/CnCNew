namespace CnCNew.Data.Schema;

/// <summary>
/// Key schema of the single <c>GameData</c> singleton (<c>GlobalData.cpp TheGlobalDataFieldParseTable</c>, ~250 keys).
/// The two body-damage-state thresholds and <c>Gravity</c> (airborne test of object creation lists) are mechanically relevant so far (<see cref="Templates.ObjectTemplate"/>
/// resolves damage states — <c>Bodies.cs ActiveBody</c> — against them); the remaining keys (game speed limits, camera,
/// scroll, shell-map, weather, multiplayer defaults, …) stay deferred.
/// </summary>
public static class GameDataSchema
{
    private const string Table = "GlobalData.cpp TheGlobalDataFieldParseTable";

    public static SchemaSet GameData { get; } = new("GameData",
    [
        new FieldSchema("UnitDamagedThreshold", FieldType.Real, Source: Table),
        new FieldSchema("UnitReallyDamagedThreshold", FieldType.Real, Source: Table),
        new FieldSchema("Gravity", FieldType.Real, Source: Table),
    ]);
}
