// Name tables transcribed mechanically from the EA Zero Hour reference sources (GeneralsMD/Code/GameEngine).
// Ordinal positions equal the reference bit/enum indices. Do not reorder.
namespace CnCNew.Data.Definitions;

/// <summary>Phase 5 additions to <see cref="NameTables"/> (kept in a separate file to avoid re-touching the Phase 4 table).</summary>
public static class NameTablesPhase5
{
    /// <summary>LocomotorSet.h TheLocomotorSurfaceTypeNames (5 entries).</summary>
    public static NameTable LocomotorSurface { get; } = new("LocomotorSurface", "LocomotorSet.h TheLocomotorSurfaceTypeNames",
        "GROUND", "WATER", "CLIFF", "AIR", "RUBBLE");

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

    /// <summary>Weapon.h TheWeaponBonusNames (27 entries; the obsolete demoralize slot keeps its index).</summary>
    public static NameTable WeaponBonusCondition { get; } = new("WeaponBonusCondition", "Weapon.h TheWeaponBonusNames",
        "GARRISONED", "HORDE", "CONTINUOUS_FIRE_MEAN", "CONTINUOUS_FIRE_FAST", "NATIONALISM", "PLAYER_UPGRADE", "DRONE_SPOTTING", "DEMORALIZED_OBSOLETE",
        "ENTHUSIASTIC", "VETERAN", "ELITE", "HERO", "BATTLEPLAN_BOMBARDMENT", "BATTLEPLAN_HOLDTHELINE", "BATTLEPLAN_SEARCHANDDESTROY", "SUBLIMINAL",
        "SOLO_HUMAN_EASY", "SOLO_HUMAN_NORMAL", "SOLO_HUMAN_HARD", "SOLO_AI_EASY", "SOLO_AI_NORMAL", "SOLO_AI_HARD", "TARGET_FAERIE_FIRE", "FANATICISM",
        "FRENZY_ONE", "FRENZY_TWO", "FRENZY_THREE");

    /// <summary>Weapon.h TheWeaponBonusFieldNames (5 entries).</summary>
    public static NameTable WeaponBonusField { get; } = new("WeaponBonusField", "Weapon.h TheWeaponBonusFieldNames", "DAMAGE", "RADIUS", "RANGE", "RATE_OF_FIRE", "PRE_ATTACK");

    /// <summary>Upgrade.cpp TheUpgradeTypeNames (2 entries).</summary>
    public static NameTable UpgradeType { get; } = new("UpgradeType", "Upgrade.cpp TheUpgradeTypeNames", "PLAYER", "OBJECT");

    /// <summary>BodyModule.h TheMaxHealthChangeTypeNames (4 entries).</summary>
    public static NameTable MaxHealthChangeType { get; } = new("MaxHealthChangeType", "BodyModule.h TheMaxHealthChangeTypeNames",
        "SAME_CURRENTHEALTH", "PRESERVE_RATIO", "ADD_CURRENT_HEALTH_TOO", "FULLY_HEAL");

    /// <summary>Weapon.h TheWeaponReloadNames (3 entries).</summary>
    public static NameTable WeaponReloadType { get; } = new("WeaponReloadType", "Weapon.h TheWeaponReloadNames", "YES", "NO", "RETURN_TO_BASE");

    /// <summary>Weapon.h TheWeaponAffectsMaskNames (7 entries; the bit order is the <c>WeaponAffectsMaskType</c> enum: SUICIDE is <c>WEAPON_KILLS_SELF</c>).</summary>
    public static NameTable WeaponAffectsMask { get; } = new("WeaponAffectsMask", "Weapon.h TheWeaponAffectsMaskNames", "SELF", "ALLIES", "ENEMIES", "NEUTRALS", "SUICIDE", "NOT_SIMILAR", "NOT_AIRBORNE");

    /// <summary>ObjectCreationList.cpp DebrisDispositionNames (9 entries).</summary>
    public static NameTable DebrisDisposition { get; } = new("DebrisDisposition", "ObjectCreationList.cpp DebrisDispositionNames",
        "LIKE_EXISTING", "ON_GROUND_ALIGNED", "SEND_IT_FLYING", "SEND_IT_UP", "SEND_IT_OUT", "RANDOM_FORCE", "FLOATING", "INHERIT_VELOCITY", "WHIRLING");

    /// <summary>WeaponSet.h TheWeaponSlotTypeNames (3 entries).</summary>
    public static NameTable WeaponSlot { get; } = new("WeaponSlot", "WeaponSet.h TheWeaponSlotTypeNames", "PRIMARY", "SECONDARY", "TERTIARY");

    /// <summary>GameLOD.cpp StaticGameLODNames (4 entries).</summary>
    public static NameTable StaticGameLod { get; } = new("StaticGameLod", "GameLOD.cpp StaticGameLODNames", "Low", "Medium", "High", "Custom");

    /// <summary>SpecialPower.cpp SpecialPowerMaskType::s_bitNameList (67 entries; the retail <c>SPECIAL_DEMORALIZE_OBSOLETE</c> spelling).</summary>
    public static NameTable SpecialPowerType { get; } = new("SpecialPowerType", "SpecialPower.cpp SpecialPowerMaskType::s_bitNameList",
        "SPECIAL_INVALID",
        "SPECIAL_DAISY_CUTTER", "SPECIAL_PARADROP_AMERICA", "SPECIAL_CARPET_BOMB", "SPECIAL_CLUSTER_MINES", "SPECIAL_EMP_PULSE",
        "SPECIAL_NAPALM_STRIKE", "SPECIAL_CASH_HACK", "SPECIAL_NEUTRON_MISSILE", "SPECIAL_SPY_SATELLITE", "SPECIAL_DEFECTOR",
        "SPECIAL_TERROR_CELL", "SPECIAL_AMBUSH", "SPECIAL_BLACK_MARKET_NUKE", "SPECIAL_ANTHRAX_BOMB", "SPECIAL_SCUD_STORM",
        "SPECIAL_DEMORALIZE_OBSOLETE", "SPECIAL_CRATE_DROP", "SPECIAL_A10_THUNDERBOLT_STRIKE", "SPECIAL_DETONATE_DIRTY_NUKE",
        "SPECIAL_ARTILLERY_BARRAGE",
        "SPECIAL_MISSILE_DEFENDER_LASER_GUIDED_MISSILES", "SPECIAL_REMOTE_CHARGES", "SPECIAL_TIMED_CHARGES", "SPECIAL_HELIX_NAPALM_BOMB",
        "SPECIAL_HACKER_DISABLE_BUILDING", "SPECIAL_TANKHUNTER_TNT_ATTACK", "SPECIAL_BLACKLOTUS_CAPTURE_BUILDING",
        "SPECIAL_BLACKLOTUS_DISABLE_VEHICLE_HACK", "SPECIAL_BLACKLOTUS_STEAL_CASH_HACK", "SPECIAL_INFANTRY_CAPTURE_BUILDING",
        "SPECIAL_RADAR_VAN_SCAN", "SPECIAL_SPY_DRONE", "SPECIAL_DISGUISE_AS_VEHICLE", "SPECIAL_BOOBY_TRAP", "SPECIAL_REPAIR_VEHICLES",
        "SPECIAL_PARTICLE_UPLINK_CANNON", "SPECIAL_CASH_BOUNTY", "SPECIAL_CHANGE_BATTLE_PLANS", "SPECIAL_CIA_INTELLIGENCE",
        "SPECIAL_CLEANUP_AREA", "SPECIAL_LAUNCH_BAIKONUR_ROCKET", "SPECIAL_SPECTRE_GUNSHIP", "SPECIAL_GPS_SCRAMBLER", "SPECIAL_FRENZY",
        "SPECIAL_SNEAK_ATTACK",
        "SPECIAL_CHINA_CARPET_BOMB", "EARLY_SPECIAL_CHINA_CARPET_BOMB", "SPECIAL_LEAFLET_DROP", "EARLY_SPECIAL_LEAFLET_DROP",
        "EARLY_SPECIAL_FRENZY", "SPECIAL_COMMUNICATIONS_DOWNLOAD", "EARLY_SPECIAL_REPAIR_VEHICLES", "SPECIAL_TANK_PARADROP",
        "SUPW_SPECIAL_PARTICLE_UPLINK_CANNON", "AIRF_SPECIAL_DAISY_CUTTER", "NUKE_SPECIAL_CLUSTER_MINES", "NUKE_SPECIAL_NEUTRON_MISSILE",
        "AIRF_SPECIAL_A10_THUNDERBOLT_STRIKE", "AIRF_SPECIAL_SPECTRE_GUNSHIP", "INFA_SPECIAL_PARADROP_AMERICA", "SLTH_SPECIAL_GPS_SCRAMBLER",
        "AIRF_SPECIAL_CARPET_BOMB", "SUPR_SPECIAL_CRUISE_MISSILE", "LAZR_SPECIAL_PARTICLE_UPLINK_CANNON", "SUPW_SPECIAL_NEUTRON_MISSILE",
        "SPECIAL_BATTLESHIP_BOMBARDMENT");

    /// <summary>OCLSpecialPower.cpp TheOCLCreateLocTypeNames (6 entries).</summary>
    public static NameTable OclCreateLocation { get; } = new("OclCreateLocation", "OCLSpecialPower.cpp TheOCLCreateLocTypeNames",
        "CREATE_AT_EDGE_NEAR_SOURCE", "CREATE_AT_EDGE_NEAR_TARGET", "CREATE_AT_LOCATION", "USE_OWNER_OBJECT", "CREATE_ABOVE_LOCATION",
        "CREATE_AT_EDGE_FARTHEST_FROM_TARGET");

    /// <summary>SpectreGunshipDeploymentUpdate.cpp TheGunshipCreateLocTypeNames (4 entries).</summary>
    public static NameTable GunshipCreateLocation { get; } = new("GunshipCreateLocation", "SpectreGunshipDeploymentUpdate.cpp TheGunshipCreateLocTypeNames",
        "CREATE_AT_EDGE_NEAR_SOURCE", "CREATE_AT_EDGE_FARTHEST_FROM_SOURCE", "CREATE_AT_EDGE_NEAR_TARGET", "CREATE_AT_EDGE_FARTHEST_FROM_TARGET");
}
