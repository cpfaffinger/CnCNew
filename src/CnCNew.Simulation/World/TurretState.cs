using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;

namespace CnCNew.Simulation.World;

/// <summary>What the presentation reads of one turret: its angle relative to the body and its pitch in radians, and whether it is still turning (<c>MODELCONDITION_TURRET_ROTATE</c>).</summary>
public readonly record struct TurretReading(Fixed Angle, Fixed Pitch, bool Rotating);

/// <summary>
/// The mutable state of one turret of an object (<c>TurretAI::m_angle</c>, <c>m_pitch</c>, the turret state machine's goal and hold timer, the sweep direction). One exists per
/// <see cref="TurretInfo"/> of the template; the world steps it every tick while it is not at rest (docs/SIM-TURRET-AIM.md).
/// <para>
/// The reference keeps an explicit state machine (idle, aim, fire, hold, recenter) driven by the AI's attack state. The simulation has no persistent attack state — an attack is a
/// stream of <c>FireWeaponCommand</c>s — so the machine is folded into three phases derived from two fields: while <see cref="Target"/> is valid and the clock is within
/// <see cref="HoldUntil"/> the turret tracks the victim (aim/fire), after the victim is gone it keeps its angle until <see cref="HoldUntil"/> (hold), then it turns back to the natural angle
/// at half speed (recenter) and the state returns to <see cref="IsPristine"/>.
/// </para>
/// </summary>
internal sealed class TurretState
{
    /// <summary><c>m_angle</c>: radians relative to the body's heading.</summary>
    public Fixed Angle;

    /// <summary><c>m_pitch</c>.</summary>
    public Fixed Pitch;

    /// <summary>The victim the turret tracks (<c>TurretAI::m_target</c> with <c>TARGET_OBJECT</c>); invalid for none.</summary>
    public EntityId Target;

    /// <summary>The last tick the turret keeps its aim after the latest order that named it; 0 while it never was ordered to aim.</summary>
    public long HoldUntil;

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
        => Angle == info.NaturalAngle && Pitch == info.NaturalPitch && !Target.IsValid && HoldUntil == 0 && !Rotating && SweepUntil == 0 && PositiveSweep;

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
        if (Slot is < 0 or >= WeaponSlot.Count) throw new InvalidDataException("Turret weapon slot out of range.");
        if (HoldUntil < 0 || SweepUntil < 0) throw new InvalidDataException("Negative turret timer in snapshot.");
    }
}
