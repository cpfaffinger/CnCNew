// Name tables transcribed mechanically from the EA Zero Hour reference sources (GeneralsMD/Code/GameEngine).
// Ordinal positions equal the reference bit/enum indices. Do not reorder.
namespace CnCNew.Data.Definitions;

/// <summary>Phase 5 additions to <see cref="NameTables"/> (kept in a separate file to avoid re-touching the Phase 4 table).</summary>
public static class NameTablesPhase5
{
    /// <summary>StealthUpdate.h TheStealthLevelNames (9 entries).</summary>
    public static NameTable StealthForbiddenCondition { get; } = new("StealthForbiddenCondition", "StealthUpdate.h TheStealthLevelNames",
        "ATTACKING", "MOVING", "USING_ABILITY", "FIRING_PRIMARY", "FIRING_SECONDARY", "FIRING_TERTIARY", "NO_BLACK_MARKET", "TAKING_DAMAGE", "RIDERS_ATTACKING");

    /// <summary>BitFlags.cpp ArmorSetFlags::s_bitNameList (8 entries).</summary>
    public static NameTable ArmorSetCondition { get; } = new("ArmorSetCondition", "BitFlags.cpp ArmorSetFlags::s_bitNameList",
        "VETERAN", "ELITE", "HERO", "PLAYER_UPGRADE", "WEAK_VERSUS_BASEDEFENSES", "SECOND_LIFE", "CRATE_UPGRADE_ONE", "CRATE_UPGRADE_TWO");

    /// <summary>WeaponSet.cpp WeaponSetFlags::s_bitNameList (17 entries).</summary>
    public static NameTable WeaponSetCondition { get; } = new("WeaponSetCondition", "WeaponSet.cpp WeaponSetFlags::s_bitNameList",
        "VETERAN", "ELITE", "HERO", "PLAYER_UPGRADE", "CRATEUPGRADE_ONE", "CRATEUPGRADE_TWO", "VEHICLE_HIJACK", "CARBOMB", "MINE_CLEARING_DETAIL",
        "WEAPON_RIDER1", "WEAPON_RIDER2", "WEAPON_RIDER3", "WEAPON_RIDER4", "WEAPON_RIDER5", "WEAPON_RIDER6", "WEAPON_RIDER7", "WEAPON_RIDER8");

    /// <summary>Upgrade.cpp TheUpgradeTypeNames (2 entries).</summary>
    public static NameTable UpgradeType { get; } = new("UpgradeType", "Upgrade.cpp TheUpgradeTypeNames", "PLAYER", "OBJECT");

    /// <summary>BodyModule.h TheMaxHealthChangeTypeNames (4 entries).</summary>
    public static NameTable MaxHealthChangeType { get; } = new("MaxHealthChangeType", "BodyModule.h TheMaxHealthChangeTypeNames",
        "SAME_CURRENTHEALTH", "PRESERVE_RATIO", "ADD_CURRENT_HEALTH_TOO", "FULLY_HEAL");

    /// <summary>Weapon.h TheWeaponReloadNames (3 entries).</summary>
    public static NameTable WeaponReloadType { get; } = new("WeaponReloadType", "Weapon.h TheWeaponReloadNames", "YES", "NO", "RETURN_TO_BASE");
}
