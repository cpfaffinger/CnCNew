using CnCNew.Data.Definitions;

namespace CnCNew.Data.Schema;

/// <summary>Key schema of the <c>MultiplayerSettings</c> block of <c>Multiplayer.ini</c> (<c>MultiplayerSettings.cpp MultiplayerSettings::getFieldParse</c>).</summary>
public static class MultiplayerSettingsSchema
{
    private const string Table = "MultiplayerSettings.cpp MultiplayerSettings::getFieldParse";

    public static SchemaSet MultiplayerSettings { get; } = new("MultiplayerSettings",
    [
        new FieldSchema("StartCountdownTimer", FieldType.Integer, Min: 0, Source: Table),
        new FieldSchema("MaxBeaconsPerPlayer", FieldType.Integer, Min: 0, Source: Table),
        new FieldSchema("UseShroud", FieldType.Bool, Source: Table),
        new FieldSchema("ShowRandomPlayerTemplate", FieldType.Bool, Source: Table),
        new FieldSchema("ShowRandomStartPos", FieldType.Bool, Source: Table),
        new FieldSchema("ShowRandomColor", FieldType.Bool, Source: Table),
    ]);
}
