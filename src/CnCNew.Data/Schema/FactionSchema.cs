using CnCNew.Data.Definitions;

namespace CnCNew.Data.Schema;

/// <summary>
/// Key schema of a <c>PlayerTemplate</c> definition header (<c>PlayerTemplate.cpp TheFieldParseTable</c>). Only the
/// fields a headless reference match needs are typed: side identity, starting cash, starting sciences and starting
/// construction. The remaining ~20 keys (command sets, special-power shortcuts, score/load screen art and audio,
/// tooltips, medallions, watermarks — all UI/presentation) stay deferred, exactly like <see cref="WeaponSchema"/>.
/// </summary>
public static class FactionSchema
{
    private const string Table = "PlayerTemplate.cpp TheFieldParseTable";

    public static SchemaSet Faction { get; } = Build();

    private static SchemaSet Build()
    {
        FieldSchema Unit(int index) => new($"StartingUnit{index}", FieldType.Reference, ReferenceKind: DefinitionKind.Object, Source: Table);
        return new SchemaSet("Faction",
        [
            new("Side", FieldType.Text, Source: Table),
            new("PlayableSide", FieldType.Bool, Source: Table),
            new("IsObserver", FieldType.Bool, Source: Table),
            // PlayerTemplate::parseStartMoney: a plain integer, wrapped into Money::setStartingCash (no upper bound like BuildCost).
            new("StartMoney", FieldType.Money, Min: 0, Source: Table + " PlayerTemplate::parseStartMoney"),
            new("IntrinsicSciences", FieldType.ReferenceList, ReferenceKind: DefinitionKind.Science, Source: Table + " INI::parseScienceVector"),
            new("IntrinsicSciencePurchasePoints", FieldType.Integer, Min: 0, Source: Table),
            new("StartingBuilding", FieldType.Reference, ReferenceKind: DefinitionKind.Object, Source: Table),
            Unit(0), Unit(1), Unit(2), Unit(3), Unit(4), Unit(5), Unit(6), Unit(7), Unit(8), Unit(9),
        ]);
    }
}
