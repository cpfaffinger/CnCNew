using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Time;

/// <summary>
/// Units of one logic frame of the original for the flight modules (phase 23, <c>docs/RATE-INVARIANCE-FLIGHT.md</c>). The 30 Hz step is the definition of an
/// integration: a module decides one step per logic frame of the original with the very expressions it uses at 30 Hz, and for that it needs the catalog's
/// values per tick (speed, turn rate) and per tick² (acceleration, gravity) as values per frame. Every function is the identity at 30 Hz (a factor or a
/// divisor of 1 on the raw value), which keeps 30 Hz bit-identical; at 60 and 120 Hz the factor is a whole 2 or 4 (4 or 16 for the squares), exact on the raw value.
/// </summary>
public static class FlightFrames
{
    /// <summary>A value per tick (speed, turn rate) as a value per logic frame of the original.</summary>
    public static Fixed PerFrame(SimulationTime time, Fixed perTick) => perTick * (long)time.TicksPerOriginalFrame;

    /// <summary>A value per tick² (acceleration, gravity) as a value per logic frame² of the original.</summary>
    public static Fixed PerFrameSquared(SimulationTime time, Fixed perTickSquared) => perTickSquared * ((long)time.TicksPerOriginalFrame * time.TicksPerOriginalFrame);

    /// <summary>
    /// The gravity per logic frame² of the original, negative (downward): <see cref="Templates.TemplateCatalog.GravityPerTickSquared"/> of the original's rate, computed from the definition
    /// value itself and therefore the same number at every rate (at 30 Hz the very expression the catalog evaluates).
    /// </summary>
    public static Fixed GravityPerFrameSquared(Templates.TemplateCatalog templates) => SimulationRate.Original.Time.PerSecondSquaredToPerTickSquared(templates.Gravity);

    /// <summary>The share of one tick of a value per logic frame of the original (a speed per frame as the speed per tick other code reads; cut off toward zero on the raw value).</summary>
    public static Fixed PerTick(SimulationTime time, Fixed perFrame) => Fixed.FromRaw(perFrame.Raw / time.TicksPerOriginalFrame);
}

/// <summary>
/// The rest of a step of one logic frame of the original that a module has decided and that is flown in equal shares, one per tick (phase 23: the 30 Hz
/// step is subdivided, so that at the end of every frame the object stands where the 30 Hz step puts it, and in between on the straight line to there).
/// The module runs its 30 Hz expressions once per frame on the object, hands the change of that frame to <see cref="Begin"/> — which takes all but the
/// first share back — and calls <see cref="Continue"/> on the following ticks instead of deciding again. The shares add up to the frame's change exactly.
/// At 30 Hz a frame is one tick: <see cref="Begin"/> does nothing, <see cref="Continue"/> is never true and nothing is written to a snapshot or hashed.
/// </summary>
public struct FlightFrameStep
{
    private int _left;
    private Fixed _x;
    private Fixed _y;
    private Fixed _z;
    private Fixed _angle;
    // The heading the decision left: the last share sets it, so that the heading after the frame is the 30 Hz heading to the bit (shares added one by one may end on the other
    // representation of a half turn).
    private Fixed _endAngle;

    /// <summary>Ticks of the decided frame that are still to be flown (0: the next tick decides).</summary>
    public readonly int TicksLeft => _left;

    /// <summary>
    /// The frame's step has been applied to <paramref name="entity"/> in full and changed it by (<paramref name="dx"/>, <paramref name="dy"/>, <paramref name="dz"/>) and the
    /// heading by <paramref name="turn"/>: leaves the first tick's share on the entity and keeps the others for <see cref="Continue"/>. A frame without any change still takes its ticks.
    /// </summary>
    public void Begin(SimulationTime time, Entity entity, Fixed dx, Fixed dy, Fixed dz, Fixed turn)
    {
        var ticks = time.TicksPerOriginalFrame;
        if (ticks == 1) return;
        _left = checked((int)(ticks - TickOrder.Next)); // the first of the frame's ticks is the one that decided
        _x = Fixed.FromRaw(dx.Raw / ticks);
        _y = Fixed.FromRaw(dy.Raw / ticks);
        _z = Fixed.FromRaw(dz.Raw / ticks);
        _angle = Fixed.FromRaw(turn.Raw / ticks);
        _endAngle = entity.Angle;
        entity.X -= _x * (long)_left;
        entity.Y -= _y * (long)_left;
        entity.Z -= _z * (long)_left;
        if (_angle != Fixed.Zero) entity.Angle = FixedTrig.Normalize(entity.Angle - _angle * (long)_left);
    }

    /// <summary>Flies the next share of the decided frame; false when there is none (the caller decides the next frame).</summary>
    public bool Continue(Entity entity)
    {
        if (_left == 0) return false;
        // The rest was published on the object (Publish) and is gone: the frame was completed from outside (Entity.CompleteFrame put the object where the frame ends - a unit
        // that came into range, a death by height). Nothing of the travel is left to fly; the frame still takes its ticks and its turn.
        if ((_x.Raw | _y.Raw | _z.Raw) != 0 && !entity.IsInsideFrameStep) _x = _y = _z = Fixed.Zero;
        _left--;
        entity.X += _x;
        entity.Y += _y;
        entity.Z += _z;
        if (_angle != Fixed.Zero) entity.Angle = _left == 0 ? _endAngle : FixedTrig.Normalize(entity.Angle + _angle);
        return true;
    }

    /// <summary>
    /// Publishes what is left of the frame on the object (<see cref="Entity.FrameEnd"/>, docs/RATE-INVARIANCE-CONSOLIDATION.md 1.3 rule 3): whoever looks at the object to decide
    /// something reads it where the frame it has begun ends, as an observer does at 30 Hz. The module that owns the step calls this in every tick it calls <see cref="Begin"/> or
    /// <see cref="Continue"/>, after them; (<paramref name="alsoX"/>, <paramref name="alsoY"/>) is the rest of another step of the same object (its ground or winged step). The
    /// publication is absolute, so a rest somebody else left on the object does not survive it. Nothing is written at 30 Hz.
    /// </summary>
    public readonly void Publish(SimulationTime time, long tick, Entity entity, Fixed alsoX = default, Fixed alsoY = default)
    {
        if (time.TicksPerOriginalFrame == 1) return;
        entity.SetAhead(tick, _x * (long)_left + alsoX, _y * (long)_left + alsoY, _z * (long)_left);
    }

    /// <summary>What is left of the frame, for the owner of a second step of the same object (<see cref="Publish"/>).</summary>
    public readonly (Fixed X, Fixed Y) RestXY => (_x * (long)_left, _y * (long)_left);

    /// <summary>The heading the frame ends with while the object is still turning toward it in shares, for the owner of a second step of the same object; false when no turn is left.</summary>
    public readonly bool TurnsTo(out Fixed endAngle)
    {
        endAngle = _endAngle;
        return _left > 0 && _angle != Fixed.Zero;
    }

    /// <summary>Drops what is left of the frame (the motion it belonged to was ended from outside).</summary>
    public void Cancel() => this = default;

    /// <summary>Written only while a frame is being flown, so a 30 Hz snapshot never carries it.</summary>
    public readonly void Describe(IStateWriter writer, string name)
    {
        if (_left == 0) return;
        writer.BeginObject(name);
        writer.Int("left", _left);
        writer.Fixed("x", _x);
        writer.Fixed("y", _y);
        writer.Fixed("z", _z);
        writer.Fixed("angle", _angle);
        if (_angle != Fixed.Zero) writer.Fixed("endAngle", _endAngle);
        writer.EndObject();
    }

    public void Load(IStateReader reader, string name)
    {
        this = default;
        if (!reader.Has(name)) return;
        var step = reader.Object(name);
        _left = checked((int)step.Int("left"));
        _x = step.Fixed("x");
        _y = step.Fixed("y");
        _z = step.Fixed("z");
        _angle = step.Fixed("angle");
        _endAngle = step.Has("endAngle") ? step.Fixed("endAngle") : Fixed.Zero;
    }
}
