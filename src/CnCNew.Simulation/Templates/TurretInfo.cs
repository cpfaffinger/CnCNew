using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.Templates;

/// <summary>
/// One turret of an object (<c>TurretAIData</c>, the <c>Turret</c>/<c>AltTurret</c> block of an AI update module), converted to simulation units: angular speeds in radians per
/// tick (<c>TurretTurnRate</c>/<c>TurretPitchRate</c> are degrees per second, <c>parseAngularVelocityReal</c>), angles in radians, times in ticks. Absent keys carry the
/// reference's constructor defaults (<c>TurretAIData::TurretAIData</c>: 0.01 rad per frame for both rates, a two second recenter time, everything else zero).
/// <para>
/// Not carried: the idle-scan fields (<c>MinIdleScanAngle</c>…, an idle turret that glances around at random) — they are typed in the data layer but the simulation does not scan.
/// </para>
/// </summary>
public sealed record TurretInfo(
    int Index, Fixed TurnRate, Fixed PitchRate, Fixed NaturalAngle, Fixed NaturalPitch, Fixed FirePitch, Fixed MinPitch, Fixed GroundUnitPitch,
    int WeaponSlotMask, long RecenterTicks, bool AllowsPitch, bool InitiallyDisabled, bool FiresWhileTurning,
    ImmutableArray<Fixed> SweepAngle, ImmutableArray<Fixed> SweepSpeed)
{
    /// <summary><c>DEFAULT_TURN_RATE</c>/<c>DEFAULT_PITCH_RATE</c> (TurretAI.h), radians per frame.</summary>
    public static readonly Fixed DefaultRate = Fixed.FromDecimal(0.01m);

    /// <summary><c>TurretAI::isWeaponSlotOnTurret</c>.</summary>
    public bool Controls(int slot) => (WeaponSlotMask & (1 << slot)) != 0;

    /// <summary>
    /// Whether this turret takes part in aiming and gates its weapons. A turret that cannot turn is one of the reference's "fake turrets" (the Comanche's, so it can attack on the
    /// move): <c>AIAttackAimAtTargetState::update</c> then falls through to turning the body, so its weapons are not held back by the turret. A turret that starts disabled
    /// (<c>InitiallyDisabled</c>, switched on by <c>BattlePlanUpdate</c>/<c>DeployStyleAIUpdate</c>) does not aim either.
    /// </summary>
    public bool Gates => TurnRate > Fixed.Zero && !InitiallyDisabled;

    /// <summary><c>TurretAI::getTurretFireAngleSweepForWeaponSlot</c>: the half-width of the sweep while firing the slot's weapon, 0 for none.</summary>
    public Fixed SweepOf(int slot) => SweepAngle[slot];

    /// <summary><c>TurretAI::getTurretSweepSpeedModifierForWeaponSlot</c>: the turn rate factor while sweeping (1 by default).</summary>
    public Fixed SweepSpeedOf(int slot) => SweepSpeed[slot];
}

/// <summary>The turrets of one object template (<c>AIUpdateInterface::m_turretAI[MAX_TURRETS]</c>): the main <c>Turret</c> and the <c>AltTurret</c>, and whether they are <c>TurretsLinked</c>.</summary>
public sealed class TurretSet
{
    public const int MaxTurrets = 2;

    private TurretSet(ImmutableArray<TurretInfo?> turrets, bool linked)
    {
        Turrets = turrets;
        Linked = linked;
    }

    /// <summary>Indexed by <c>WhichTurretType</c> (0 = <c>Turret</c>, 1 = <c>AltTurret</c>); an absent turret is null.</summary>
    public ImmutableArray<TurretInfo?> Turrets { get; }

    /// <summary><c>AIUpdateModuleData::m_turretsLinked</c>: every attack orders all turrets and any slot may fire from any turret (the battleships).</summary>
    public bool Linked { get; }

    public TurretInfo? this[int index] => Turrets[index];

    /// <summary>
    /// Reads the <c>Turret</c>/<c>AltTurret</c> blocks of an object's modules; null when it has none. The blocks were compiled with <see cref="Data.Schema.TurretSchema"/>, so every field has its
    /// type; a block that appears twice for one turret (two AI modules) or a sweep line without a weapon slot is an error, never a guess.
    /// </summary>
    public static TurretSet? Read(ImmutableArray<ModuleConfiguration> modules)
    {
        var turrets = new TurretInfo?[MaxTurrets];
        var linked = false;
        foreach (var module in modules)
        {
            if (module.Sections.IsDefaultOrEmpty) continue;
            var found = false;
            foreach (var section in module.Sections)
            {
                var index = section.Declaration.Trim().Equals("Turret", StringComparison.OrdinalIgnoreCase) ? 0
                    : section.Declaration.Trim().Equals("AltTurret", StringComparison.OrdinalIgnoreCase) ? 1 : -1;
                if (index < 0) continue;
                if (turrets[index] is not null) throw new InvalidDataException($"{section.Location.Source}({section.Location.Line}): {section.Declaration} is declared twice on one object.");
                turrets[index] = ReadTurret(index, section);
                found = true;
            }

            if (found) linked |= ModuleConfigReader.Bool(module, "TurretsLinked", false);
        }

        return turrets[0] is null && turrets[1] is null ? null : new TurretSet([..turrets], linked);
    }

    private static TurretInfo ReadTurret(int index, DefinitionSection section)
    {
        DefinitionField? Find(string name) => section.Fields.LastOrDefault(field => field.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        InvalidDataException Unexpected(DefinitionField field) => new($"{field.Location.Source}({field.Location.Line}): turret field '{field.Name}' has an unexpected value kind {field.Value.Kind}.");

        decimal Number(string name, decimal fallback)
        {
            var field = Find(name);
            if (field is null) return fallback;
            return field.Value.Value switch
            {
                decimal value => value,
                long value => value,
                CompatibilityValue compatibility => decimal.Parse(compatibility.NormalizedValue, CultureInfo.InvariantCulture),
                _ => throw Unexpected(field),
            };
        }

        decimal Degrees(string name)
        {
            var field = Find(name);
            if (field is null) return 0m;
            return field.Value.Value is Angle angle ? angle.Degrees : throw Unexpected(field);
        }

        bool Bool(string name)
        {
            var field = Find(name);
            if (field is null) return false;
            return field.Value.Value is bool value ? value : throw Unexpected(field);
        }

        var recenter = Find("RecenterTime") is { } recenterField
            ? recenterField.Value.Value is GameTime time ? SimulationTiming.DurationMillisecondsToTicks(time.Milliseconds) : throw Unexpected(recenterField)
            : 2 * SimulationTiming.LogicFramesPerSecond; // TurretAIData: m_recenterTime = 2*LOGICFRAMES_PER_SECOND

        var slotsField = Find("ControlledWeaponSlots") ?? throw new InvalidDataException($"{section.Location.Source}({section.Location.Line}): {section.Declaration} lacks ControlledWeaponSlots.");
        if (slotsField.Value.Value is not FlagsValue slotNames) throw Unexpected(slotsField);
        var mask = 0;
        foreach (var name in slotNames.Names)
            mask |= 1 << (SlotNames.TryIndexOf(name, out var slot) ? slot : throw new InvalidDataException($"{slotsField.Location.Source}({slotsField.Location.Line}): '{name}' is not a weapon slot."));
        if (mask == 0) throw new InvalidDataException($"{slotsField.Location.Source}({slotsField.Location.Line}): {section.Declaration} controls no weapon slot.");

        var sweepAngle = new Fixed[WeaponSlot.Count];
        var sweepSpeed = new Fixed[WeaponSlot.Count];
        Array.Fill(sweepSpeed, Fixed.One);
        foreach (var field in section.Fields)
        {
            var isAngle = field.Name.Equals("TurretFireAngleSweep", StringComparison.OrdinalIgnoreCase);
            var isSpeed = field.Name.Equals("TurretSweepSpeedModifier", StringComparison.OrdinalIgnoreCase);
            if (!isAngle && !isSpeed) continue;
            if (field.Value.Value is not CoefficientValue { EnumName: { } slotName } coefficient || !SlotNames.TryIndexOf(slotName, out var slot))
                throw new InvalidDataException($"{field.Location.Source}({field.Location.Line}): '{field.Name}' needs a weapon slot name.");
            if (isAngle)
                sweepAngle[slot] = coefficient.Value.Value is Angle sweep ? SimulationTiming.DegreesToRadians(sweep.Degrees) : throw Unexpected(field);
            else
                sweepSpeed[slot] = coefficient.Value.Value is decimal speed ? Fixed.FromDecimal(speed) : coefficient.Value.Value is long whole ? Fixed.FromInt(whole) : throw Unexpected(field);
        }

        return new TurretInfo(
            index,
            Find("TurretTurnRate") is null ? TurretInfo.DefaultRate : SimulationTiming.DegreesPerSecondToRadiansPerTick(Number("TurretTurnRate", 0m)),
            Find("TurretPitchRate") is null ? TurretInfo.DefaultRate : SimulationTiming.DegreesPerSecondToRadiansPerTick(Number("TurretPitchRate", 0m)),
            SimulationTiming.DegreesToRadians(Degrees("NaturalTurretAngle")), SimulationTiming.DegreesToRadians(Degrees("NaturalTurretPitch")),
            SimulationTiming.DegreesToRadians(Degrees("FirePitch")), SimulationTiming.DegreesToRadians(Degrees("MinPhysicalPitch")), SimulationTiming.DegreesToRadians(Degrees("GroundUnitPitch")),
            mask, recenter, Bool("AllowsPitch"), Bool("InitiallyDisabled"), Bool("FiresWhileTurning"), [..sweepAngle], [..sweepSpeed]);
    }

    private static NameTable SlotNames => NameTablesPhase5.WeaponSlot;
}
