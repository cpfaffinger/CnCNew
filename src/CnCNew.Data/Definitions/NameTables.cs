// Name tables transcribed mechanically from the EA Zero Hour reference sources (GeneralsMD/Code/GameEngine).
// Ordinal positions equal the reference bit/enum indices. Do not reorder.
using System.Collections.Immutable;

namespace CnCNew.Data.Definitions;

public sealed class NameTable
{
    private readonly ImmutableDictionary<string, int> _index;
    public NameTable(string name, string source, params string[] names)
    {
        Name = name; Source = source; Names = [..names];
        var builder = ImmutableDictionary.CreateBuilder<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < names.Length; i++) builder.Add(names[i], i);
        _index = builder.ToImmutable();
    }
    public string Name { get; }
    public string Source { get; }
    public ImmutableArray<string> Names { get; }
    public int Count => Names.Length;
    public bool TryIndexOf(string token, out int index) => _index.TryGetValue(token, out index);
    public string Canonical(int index) => Names[index];
}

public static class NameTables
{
    /// <summary>KindOf.cpp KindOfMaskType::s_bitNameList (120 entries).</summary>
    public static NameTable KindOf { get; } = new("KindOf", "KindOf.cpp KindOfMaskType::s_bitNameList", "OBSTACLE", "SELECTABLE", "IMMOBILE", "CAN_ATTACK", "STICK_TO_TERRAIN_SLOPE", "CAN_CAST_REFLECTIONS", "SHRUBBERY", "STRUCTURE", "INFANTRY", "VEHICLE", "AIRCRAFT", "HUGE_VEHICLE", "DOZER", "HARVESTER", "COMMANDCENTER", "PRISON", "COLLECTS_PRISON_BOUNTY", "POW_TRUCK", "LINEBUILD", "SALVAGER", "WEAPON_SALVAGER", "TRANSPORT", "BRIDGE", "LANDMARK_BRIDGE", "BRIDGE_TOWER", "PROJECTILE", "PRELOAD", "NO_GARRISON", "WAVEGUIDE", "WAVE_EFFECT", "NO_COLLIDE", "REPAIR_PAD", "HEAL_PAD", "STEALTH_GARRISON", "CASH_GENERATOR", "DRAWABLE_ONLY", "MP_COUNT_FOR_VICTORY", "REBUILD_HOLE", "SCORE", "SCORE_CREATE", "SCORE_DESTROY", "NO_HEAL_ICON", "CAN_RAPPEL", "PARACHUTABLE", "CAN_SURRENDER", "CAN_BE_REPULSED", "MOB_NEXUS", "IGNORED_IN_GUI", "CRATE", "CAPTURABLE", "CLEARED_BY_BUILD", "SMALL_MISSILE", "ALWAYS_VISIBLE", "UNATTACKABLE", "MINE", "CLEANUP_HAZARD", "PORTABLE_STRUCTURE", "ALWAYS_SELECTABLE", "ATTACK_NEEDS_LINE_OF_SIGHT", "WALK_ON_TOP_OF_WALL", "DEFENSIVE_WALL", "FS_POWER", "FS_FACTORY", "FS_BASE_DEFENSE", "FS_TECHNOLOGY", "AIRCRAFT_PATH_AROUND", "LOW_OVERLAPPABLE", "FORCEATTACKABLE", "AUTO_RALLYPOINT", "TECH_BUILDING", "POWERED", "PRODUCED_AT_HELIPAD", "DRONE", "CAN_SEE_THROUGH_STRUCTURE", "BALLISTIC_MISSILE", "CLICK_THROUGH", "SUPPLY_SOURCE_ON_PREVIEW", "PARACHUTE", "GARRISONABLE_UNTIL_DESTROYED", "BOAT", "IMMUNE_TO_CAPTURE", "HULK", "SHOW_PORTRAIT_WHEN_CONTROLLED", "SPAWNS_ARE_THE_WEAPONS", "CANNOT_BUILD_NEAR_SUPPLIES", "SUPPLY_SOURCE", "REVEAL_TO_ALL", "DISGUISER", "INERT", "HERO", "IGNORES_SELECT_ALL", "DONT_AUTO_CRUSH_INFANTRY", "CLIFF_JUMPER", "FS_SUPPLY_DROPZONE", "FS_SUPERWEAPON", "FS_BLACK_MARKET", "FS_SUPPLY_CENTER", "FS_STRATEGY_CENTER", "MONEY_HACKER", "ARMOR_SALVAGER", "REVEALS_ENEMY_PATHS", "BOOBY_TRAP", "FS_FAKE", "FS_INTERNET_CENTER", "BLAST_CRATER", "PROP", "OPTIMIZED_TREE", "FS_ADVANCED_TECH", "FS_BARRACKS", "FS_WARFACTORY", "FS_AIRFIELD", "AIRCRAFT_CARRIER", "NO_SELECT", "REJECT_UNMANNED", "CANNOT_RETALIATE", "TECH_BASE_DEFENSE", "EMP_HARDENED", "DEMOTRAP", "CONSERVATIVE_BUILDING", "IGNORE_DOCKING_BONES");

    /// <summary>ObjectStatusTypes.cpp ObjectStatusMaskType::s_bitNameList (45 entries).</summary>
    public static NameTable ObjectStatus { get; } = new("ObjectStatus", "ObjectStatusTypes.cpp ObjectStatusMaskType::s_bitNameList", "NONE", "DESTROYED", "CAN_ATTACK", "UNDER_CONSTRUCTION", "UNSELECTABLE", "NO_COLLISIONS", "NO_ATTACK", "AIRBORNE_TARGET", "PARACHUTING", "REPULSOR", "HIJACKED", "AFLAME", "BURNED", "WET", "IS_FIRING_WEAPON", "IS_BRAKING", "STEALTHED", "DETECTED", "CAN_STEALTH", "SOLD", "UNDERGOING_REPAIR", "RECONSTRUCTING", "MASKED", "IS_ATTACKING", "USING_ABILITY", "IS_AIMING_WEAPON", "NO_ATTACK_FROM_AI", "IGNORING_STEALTH", "IS_CARBOMB", "DECK_HEIGHT_OFFSET", "STATUS_RIDER1", "STATUS_RIDER2", "STATUS_RIDER3", "STATUS_RIDER4", "STATUS_RIDER5", "STATUS_RIDER6", "STATUS_RIDER7", "STATUS_RIDER8", "FAERIE_FIRE", "KILLING_SELF", "REASSIGN_PARKING", "BOOBY_TRAPPED", "IMMOBILE", "DISGUISED", "DEPLOYED");

    /// <summary>Damage.cpp DamageTypeFlags::s_bitNameList (38 entries).</summary>
    public static NameTable DamageType { get; } = new("DamageType", "Damage.cpp DamageTypeFlags::s_bitNameList", "EXPLOSION", "CRUSH", "ARMOR_PIERCING", "SMALL_ARMS", "GATTLING", "RADIATION", "FLAME", "LASER", "SNIPER", "POISON", "HEALING", "UNRESISTABLE", "WATER", "DEPLOY", "SURRENDER", "HACK", "KILL_PILOT", "PENALTY", "FALLING", "MELEE", "DISARM", "HAZARD_CLEANUP", "PARTICLE_BEAM", "TOPPLING", "INFANTRY_MISSILE", "AURORA_BOMB", "LAND_MINE", "JET_MISSILES", "STEALTHJET_MISSILES", "MOLOTOV_COCKTAIL", "COMANCHE_VULCAN", "SUBDUAL_MISSILE", "SUBDUAL_VEHICLE", "SUBDUAL_BUILDING", "SUBDUAL_UNRESISTABLE", "MICROWAVE", "KILL_GARRISONED", "STATUS");

    /// <summary>Damage.h TheDeathNames (21 entries).</summary>
    public static NameTable DeathType { get; } = new("DeathType", "Damage.h TheDeathNames", "NORMAL", "NONE", "CRUSHED", "BURNED", "EXPLODED", "POISONED", "TOPPLED", "FLOODED", "SUICIDED", "LASERED", "DETONATED", "SPLATTED", "POISONED_BETA", "EXTRA_2", "EXTRA_3", "EXTRA_4", "EXTRA_5", "EXTRA_6", "EXTRA_7", "EXTRA_8", "POISONED_GAMMA");

    /// <summary>GameCommon.cpp TheVeterancyNames (4 entries).</summary>
    public static NameTable Veterancy { get; } = new("Veterancy", "GameCommon.cpp TheVeterancyNames", "REGULAR", "VETERAN", "ELITE", "HEROIC");

    /// <summary>Geometry.h GeometryNames (3 entries).</summary>
    public static NameTable Geometry { get; } = new("Geometry", "Geometry.h GeometryNames", "SPHERE", "CYLINDER", "BOX");

    /// <summary>Radar.h RadarPriorityNames (5 entries).</summary>
    public static NameTable RadarPriority { get; } = new("RadarPriority", "Radar.h RadarPriorityNames", "INVALID", "NOT_ON_RADAR", "STRUCTURE", "UNIT", "LOCAL_UNIT_ONLY");

    /// <summary>ThingTemplate.h BuildableStatusNames (4 entries).</summary>
    public static NameTable BuildableStatus { get; } = new("BuildableStatus", "ThingTemplate.h BuildableStatusNames", "Yes", "Ignore_Prerequisites", "No", "Only_By_AI");

    /// <summary>ThingTemplate.h BuildCompletionNames (3 entries).</summary>
    public static NameTable BuildCompletion { get; } = new("BuildCompletion", "ThingTemplate.h BuildCompletionNames", "INVALID", "APPEARS_AT_RALLY_POINT", "PLACED_BY_PLAYER");

    /// <summary>ThingSort.h EditorSortingNames (14 entries).</summary>
    public static NameTable EditorSorting { get; } = new("EditorSorting", "ThingSort.h EditorSortingNames", "NONE", "STRUCTURE", "INFANTRY", "VEHICLE", "SHRUBBERY", "MISC_MAN_MADE", "MISC_NATURAL", "DEBRIS", "SYSTEM", "AUDIO", "TEST", "FOR_REVIEW", "ROAD", "WAYPOINT");

    /// <summary>Shadow.h TheShadowNames (7 entries).</summary>
    public static NameTable Shadow { get; } = new("Shadow", "Shadow.h TheShadowNames", "SHADOW_DECAL", "SHADOW_VOLUME", "SHADOW_PROJECTION", "SHADOW_DYNAMIC_PROJECTION", "SHADOW_DIRECTIONAL_PROJECTION", "SHADOW_ALPHA_DECAL", "SHADOW_ADDITIVE_DECAL");

    /// <summary>AIUpdate.h TheLocomotorSetNames (8 entries).</summary>
    public static NameTable LocomotorSet { get; } = new("LocomotorSet", "AIUpdate.h TheLocomotorSetNames", "SET_NORMAL", "SET_NORMAL_UPGRADED", "SET_FREEFALL", "SET_WANDER", "SET_PANIC", "SET_TAXIING", "SET_SUPERSONIC", "SET_SLUGGISH");

    public static ImmutableArray<NameTable> All { get; } = [KindOf, ObjectStatus, DamageType, DeathType, Veterancy, Geometry, RadarPriority, BuildableStatus, BuildCompletion, EditorSorting, Shadow, LocomotorSet];
}
