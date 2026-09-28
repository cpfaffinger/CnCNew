using CnCNew.Data.Definitions;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;

namespace CnCNew.Simulation.World;

/// <summary>Indices of <c>DamageTypeFlags::s_bitNameList</c> (reference Damage.cpp) that phase 4 distinguishes.</summary>
public static class DamageTypes
{
    public const int Explosion = 0;
    public const int Flame = 6;
    public const int Poison = 9;
    public const int Healing = 10;
    public const int Unresistable = 11;
    public const int KillPilot = 16;
    public const int ParticleBeam = 22;
    public const int SubdualMissile = 31;
    public const int SubdualVehicle = 32;
    public const int SubdualBuilding = 33;
    public const int SubdualUnresistable = 34;
    public const int KillGarrisoned = 36;
    public const int Status = 37;

    public static int Count => NameTables.DamageType.Count;
    public static bool IsSubdual(int type) => type is SubdualMissile or SubdualVehicle or SubdualBuilding or SubdualUnresistable;
    public static bool IsValid(int type) => type >= 0 && type < Count;
}

/// <summary>Indices of <c>TheDeathNames</c> (reference Damage.h).</summary>
public static class DeathTypes
{
    public const int Normal = 0;
    public const int None = 1;
    public const int Crushed = 2;
    public const int Burned = 3;
    public const int Poisoned = 5;

    public static int Count => NameTables.DeathType.Count;
    public static bool IsValid(int type) => type >= 0 && type < Count;
}

public readonly record struct DamageInfo(EntityId Source, Fixed Amount, int DamageType, int DeathType, bool Kill);

public enum DamageOutcome : byte
{
    /// <summary>Nothing changed (dead target, subdual, invulnerable body, non-positive amount).</summary>
    NoEffect,
    /// <summary>Health changed and the entity is still alive.</summary>
    Applied,
    /// <summary>The damage killed the entity; die modules ran.</summary>
    Killed,
}
