using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;

namespace CnCNew.Simulation.World;

/// <summary>What the presentation reads of one turret: its angle relative to the body and its pitch in radians, and whether it is still turning (<c>MODELCONDITION_TURRET_ROTATE</c>).</summary>
public readonly record struct TurretReading(Fixed Angle, Fixed Pitch, bool Rotating);

/// <summary>
/// The mutable state of one turret of an object (<c>TurretAI::m_angle</c>, <c>m_pitch</c>, the turret state machine's goal and timers, the sweep direction, the idle scan). One exists per
/// <see cref="TurretInfo"/> of the template; the world steps it every tick while it is not at rest (docs/SIM-TURRET-AIM.md).
/// <para>
/// The reference keeps an explicit state machine (idle, idle scan, aim, fire, hold, recenter) driven by the AI's attack state. Here the phases follow from a few fields:
/// </para>
/// <list type="bullet">
/// <item><description><b>Attack intent</b> (<see cref="Persistent"/>, <see cref="Target"/> valid): the unit was ordered to attack, so the turret tracks the victim until the victim is gone,
/// the unit gets another order or (unless it <see cref="Pursuing">pursues</see>) loses interest (<see cref="LoseInterestAt"/>), through reloads and chases alike. Then the turret holds <c>RecenterTime</c>.</description></item>
/// <item><description><b>One-off aim</b> (<see cref="Target"/> valid, not persistent): a shot of a module or script that is not part of an attack order; the turret tracks the victim until <see cref="HoldUntil"/>.</description></item>
/// <item><description><b>Hold</b> (<see cref="Target"/> invalid, tick within <see cref="HoldUntil"/>): the angle stays; afterwards the turret recenters at half speed and returns to <see cref="IsPristine"/>.</description></item>
/// <item><description><b>Idle scan</b> (only turrets whose data set a scan): at rest the turret waits until <see cref="NextScan"/>, then turns (<see cref="Scanning"/>) to <see cref="ScanAngle"/>, holds, recenters and waits again.</description></item>
/// </list>
/// </summary>
internal sealed class TurretState
{
    /// <summary><c>m_angle</c>: radians relative to the body's heading.</summary>
    public Fixed Angle;

    /// <summary><c>m_pitch</c>.</summary>
    public Fixed Pitch;

    /// <summary>The victim the turret tracks (<c>TurretAI::m_target</c> with <c>TARGET_OBJECT</c>); invalid for none.</summary>
    public EntityId Target;

    /// <summary>
    /// A one-off aim tracks until this tick; with the target gone (or after an attack ended) the angle is held until this tick before the turret recenters; 0 while there is nothing to hold.
    /// Ignored while <see cref="Persistent"/>.
    /// </summary>
    public long HoldUntil;

    /// <summary>The target is the victim of an attack order (<c>AIAttackState</c>): it is tracked without a deadline.</summary>
    public bool Persistent;

    /// <summary>
    /// The attack was begun by a pursuit (<c>AIAttackApproachTargetState</c>): the unit chases the victim, so the attack does not end because the victim is out of reach. Without it
    /// (the attack began with a fire order, as the AI and attack-move do) the unit gives up on a victim that stays out of every weapon's range.
    /// </summary>
    public bool Pursuing;

    /// <summary>An attack that is not a pursuit ends at this tick if the victim has been out of every weapon's range since a while (the reference's attack state fails when it may not chase); 0 for none.</summary>
    public long LoseInterestAt;

    /// <summary><c>TurretAIIdleScanState</c> is active: the turret turns to the natural angle plus <see cref="ScanAngle"/>.</summary>
    public bool Scanning;

    /// <summary><c>TurretAIIdleScanState::m_desiredAngle</c>: radians off the natural angle, signed.</summary>
    public Fixed ScanAngle;

    /// <summary><c>TurretAIIdleState::m_nextIdleScan</c>: the tick the idle turret starts its next scan; 0 while not scheduled.</summary>
    public long NextScan;

    /// <summary>How many scan decisions this turret has drawn; with the world seed and the entity it selects the next random values (docs/SIM-TURRET-AIM.md).</summary>
    public int ScanDraws;

    /// <summary>The weapon slot of the latest order: the sweep and the ground pitch depend on the weapon being fired (<c>curWeapon</c> in <c>TurretAIAimTurretState::update</c>).</summary>
    public int Slot;

    /// <summary><c>MODELCONDITION_TURRET_ROTATE</c>: the last step turned the turret without reaching its goal angle.</summary>
    public bool Rotating;

    /// <summary><c>TurretAI::m_enableSweepUntil</c>: the sweep is on for a few ticks after each shot.</summary>
    public long SweepUntil;

    /// <summary><c>TurretAI::m_positiveSweep</c>.</summary>
    public bool PositiveSweep = true;

    public TurretState(TurretInfo info)
    {
        Angle = info.NaturalAngle;
        Pitch = info.NaturalPitch;
    }

    /// <summary>At rest: at the natural angle and pitch, no order, nothing pending. Such a turret needs no stepping and writes nothing to the snapshot.</summary>
    public bool IsPristine(TurretInfo info)
        => Angle == info.NaturalAngle && Pitch == info.NaturalPitch && !Target.IsValid && HoldUntil == 0 && !Rotating && SweepUntil == 0 && PositiveSweep
           && !Persistent && !Pursuing && LoseInterestAt == 0 && !Scanning && NextScan == 0;

    public void ResetToRest(TurretInfo info)
    {
        Angle = info.NaturalAngle;
        Pitch = info.NaturalPitch;
        Target = EntityId.Invalid;
        HoldUntil = 0;
        Rotating = false;
        SweepUntil = 0;
        PositiveSweep = true;
        Slot = 0;
        Persistent = false;
        Pursuing = false;
        LoseInterestAt = 0;
        Scanning = false;
        ScanAngle = Fixed.Zero;
        NextScan = 0;
    }

    public void Describe(IStateWriter writer, int index)
    {
        writer.BeginObject(null);
        writer.Int("index", index);
        writer.Fixed("angle", Angle);
        writer.Fixed("pitch", Pitch);
        writer.UInt("target", Target.Value);
        writer.Int("holdUntil", HoldUntil);
        writer.Int("slot", Slot);
        writer.Bool("rotating", Rotating);
        writer.Int("sweepUntil", SweepUntil);
        writer.Bool("positiveSweep", PositiveSweep);
        // The attack and idle-scan state is written only when it is set, so a turret that never attacked or scanned keeps its serialized form.
        if (Persistent) writer.Bool("persistent", true);
        if (Pursuing) writer.Bool("pursuing", true);
        if (LoseInterestAt != 0) writer.Int("loseInterestAt", LoseInterestAt);
        if (Scanning) { writer.Bool("scanning", true); writer.Fixed("scanAngle", ScanAngle); }
        if (NextScan != 0) writer.Int("nextScan", NextScan);
        if (ScanDraws != 0) writer.Int("scanDraws", ScanDraws);
        writer.EndObject();
    }

    public void Load(IStateReader reader)
    {
        Angle = reader.Fixed("angle");
        Pitch = reader.Fixed("pitch");
        Target = new EntityId(checked((uint)reader.UInt("target")));
        HoldUntil = reader.Int("holdUntil");
        Slot = checked((int)reader.Int("slot"));
        Rotating = reader.Bool("rotating");
        SweepUntil = reader.Int("sweepUntil");
        PositiveSweep = reader.Bool("positiveSweep");
        Persistent = reader.Has("persistent") && reader.Bool("persistent");
        Pursuing = reader.Has("pursuing") && reader.Bool("pursuing");
        LoseInterestAt = reader.Has("loseInterestAt") ? reader.Int("loseInterestAt") : 0;
        Scanning = reader.Has("scanning") && reader.Bool("scanning");
        ScanAngle = reader.Has("scanAngle") ? reader.Fixed("scanAngle") : Fixed.Zero;
        NextScan = reader.Has("nextScan") ? reader.Int("nextScan") : 0;
        ScanDraws = reader.Has("scanDraws") ? checked((int)reader.Int("scanDraws")) : 0;
        if (Slot is < 0 or >= WeaponSlot.Count) throw new InvalidDataException("Turret weapon slot out of range.");
        if (HoldUntil < 0 || SweepUntil < 0 || LoseInterestAt < 0 || NextScan < 0 || ScanDraws < 0) throw new InvalidDataException("Negative turret timer in snapshot.");
        if (Persistent && !Target.IsValid) throw new InvalidDataException("A persistent turret attack needs a victim.");
        if (Pursuing && !Persistent) throw new InvalidDataException("A pursuit is an attack.");
    }
}

/// <summary>Every mutable field of a <see cref="TurretState"/>, compared before and after a step to learn whether the entity's serialized state changed (a struct: stepping must not allocate).</summary>
internal readonly record struct TurretStamp(
    Fixed Angle, Fixed Pitch, bool Rotating, EntityId Target, long SweepUntil, bool PositiveSweep, long HoldUntil, bool Persistent, bool Pursuing, long LoseInterestAt,
    bool Scanning, Fixed ScanAngle, long NextScan, int ScanDraws, int Slot)
{
    public static TurretStamp Of(TurretState state)
        => new(state.Angle, state.Pitch, state.Rotating, state.Target, state.SweepUntil, state.PositiveSweep, state.HoldUntil, state.Persistent, state.Pursuing, state.LoseInterestAt,
            state.Scanning, state.ScanAngle, state.NextScan, state.ScanDraws, state.Slot);
}
