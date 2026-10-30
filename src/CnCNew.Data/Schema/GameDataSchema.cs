using CnCNew.Data.Definitions;

namespace CnCNew.Data.Schema;

/// <summary>
/// Key schema of the single <c>GameData</c> singleton (<c>GlobalData.cpp TheGlobalDataFieldParseTable</c>, ~250 keys).
/// The two body-damage-state thresholds, <c>Gravity</c> (airborne test of object creation lists) and the two base-regeneration keys (<c>BaseRegenerateUpdate</c>) <c>ValuePerSupplyBox</c> (supply-center deposits), <c>SellPercentage</c> (sell refund), the three low-energy keys (build time under low power) and <c>SpecialPowerViewObject</c> (the view a power opens at its target) are mechanically relevant so far (<see cref="Templates.ObjectTemplate"/>
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
        new FieldSchema("BaseRegenHealthPercentPerSecond", FieldType.Percent, Source: Table),
        new FieldSchema("BaseRegenDelay", FieldType.DurationMs, Source: Table),
        new FieldSchema("StandardMinefieldDistance", FieldType.Distance, Source: Table),
        new FieldSchema("ValuePerSupplyBox", FieldType.Integer, Source: Table),
        new FieldSchema("SellPercentage", FieldType.Percent, Source: Table),
        new FieldSchema("LowEnergyPenaltyModifier", FieldType.Real, Source: Table),
        new FieldSchema("MinLowEnergyProductionSpeed", FieldType.Real, Source: Table),
        new FieldSchema("MaxLowEnergyProductionSpeed", FieldType.Real, Source: Table),
        new FieldSchema("SpecialPowerViewObject", FieldType.Reference, ReferenceKind: DefinitionKind.Object, Source: Table),
        new FieldSchema("AllowedHeightVariationForBuilding", FieldType.Real, Source: Table),
        new FieldSchema("MinDistFromEdgeOfMapForBuild", FieldType.Real, Source: Table),
        new FieldSchema("HealthBonus_Veteran", FieldType.Percent, Source: Table),
        new FieldSchema("HealthBonus_Elite", FieldType.Percent, Source: Table),
        new FieldSchema("HealthBonus_Heroic", FieldType.Percent, Source: Table),
        new FieldSchema("WeaponBonus", FieldType.WeaponBonusEntry, Repeat: RepeatPolicy.IndexedByFirstTwoTokens, Source: Table + " WeaponBonusSet::parseWeaponBonusSet"),
    ]);
}
