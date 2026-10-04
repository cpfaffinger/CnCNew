using System.Collections.Immutable;
using CnCNew.Data.Definitions;

namespace CnCNew.Data.Schema;

/// <summary>
/// Key schema of a <c>Turret</c>/<c>AltTurret</c> block inside an AI update module (<c>AIUpdateModuleData::parseTurret</c> reading
/// <c>TurretAIData::buildFieldParse</c>, GeneralsMD/Code/GameEngine/Source/GameLogic/AI/TurretAI.cpp). Every key of the table is typed; the block is a
/// nested section of the module (<see cref="Registry.ModuleDescriptor.AllowsNestedSections"/>), so the compiler compiles it with this schema instead of the
/// generic heuristic typing, and an unknown key is an error. Units follow the reference's parsers, kept as written in the INI: <c>TurretTurnRate</c>/<c>TurretPitchRate</c>
/// are degrees per second (<c>parseAngularVelocityReal</c>), the angles degrees (<c>parseAngleReal</c>), the times milliseconds (<c>parseDurationUnsignedInt</c>); the
/// simulation converts them to radians per tick and ticks when it builds the template.
/// <para>
/// <c>InterTurretDelay</c> exists in the table only under <c>INTER_TURRET_DELAY</c>, which the shipped build does not define; it is not a key here and no shipped object sets it.
/// <c>ControlledWeaponSlots</c> is required: <c>TurretAI::TurretAI</c> throws "TurretAI MUST specify controlled weapon slots!" without it.
/// </para>
/// </summary>
public static class TurretSchema
{
    private const string Table = "TurretAI.cpp TurretAIData::buildFieldParse";

    /// <summary>The declarations that open a turret block inside an AI update module (<c>AIUpdateModuleData::buildFieldParse</c>: <c>Turret</c> is turret 0, <c>AltTurret</c> turret 1).</summary>
    public static ImmutableArray<string> Declarations { get; } = ["Turret", "AltTurret"];

    public static SchemaSet Turret { get; } = new("Turret",
    [
        new("TurretTurnRate", FieldType.Real, Source: Table + " parseAngularVelocityReal (degrees per second)"),
        new("TurretPitchRate", FieldType.Real, Source: Table + " parseAngularVelocityReal (degrees per second)"),
        new("NaturalTurretAngle", FieldType.Angle, Source: Table + " parseAngleReal"),
        new("NaturalTurretPitch", FieldType.Angle, Source: Table + " parseAngleReal"),
        new("FirePitch", FieldType.Angle, Source: Table + " parseAngleReal"),
        new("MinPhysicalPitch", FieldType.Angle, Source: Table + " parseAngleReal"),
        new("GroundUnitPitch", FieldType.Angle, Source: Table + " parseAngleReal"),
        // "<SLOT> <angle>" / "<SLOT> <factor>": parseTurretSweep/parseTurretSweepSpeed read a weapon slot name (scanIndexList, no "Default") and a real; the schema's
        // CoefficientByEnum also accepts "Default", which the simulation's binding rejects.
        new("TurretFireAngleSweep", FieldType.CoefficientByEnum, Repeat: RepeatPolicy.IndexedByFirstToken, Table: NameTablesPhase5.WeaponSlot, InnerType: FieldType.Angle,
            Source: Table + " TurretAIData::parseTurretSweep"),
        new("TurretSweepSpeedModifier", FieldType.CoefficientByEnum, Repeat: RepeatPolicy.IndexedByFirstToken, Table: NameTablesPhase5.WeaponSlot, InnerType: FieldType.Real,
            Source: Table + " TurretAIData::parseTurretSweepSpeed"),
        new("ControlledWeaponSlots", FieldType.Flags, Required: true, Table: NameTablesPhase5.WeaponSlot, Source: Table + " parseTWS"),
        new("AllowsPitch", FieldType.Bool, Source: Table),
        new("MinIdleScanAngle", FieldType.Angle, Source: Table + " parseAngleReal"),
        new("MaxIdleScanAngle", FieldType.Angle, Source: Table + " parseAngleReal"),
        new("MinIdleScanInterval", FieldType.DurationMs, Source: Table + " parseDurationUnsignedInt"),
        new("MaxIdleScanInterval", FieldType.DurationMs, Source: Table + " parseDurationUnsignedInt"),
        new("RecenterTime", FieldType.DurationMs, Source: Table + " parseDurationUnsignedInt"),
        new("InitiallyDisabled", FieldType.Bool, Source: Table),
        new("FiresWhileTurning", FieldType.Bool, Source: Table),
    ]);
}
