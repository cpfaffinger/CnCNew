using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>
/// The free flight of fixed-wing aircraft (<c>Appearance = WINGS</c>: Raptor, Stealth Fighter, Aurora, MiG and their variants — <c>docs/SIM-JET-FLIGHT.md</c>). A winged locomotor has a
/// positive <c>MinSpeed</c> ("otherwise they hover", <c>Locomotor.cpp</c> after <c>LocomotorTemplate::friend_parse</c>), so an aircraft that holds a position does not stop: <c>Locomotor::locoUpdate_maintainCurrentPosition</c>
/// calls <c>maintainCurrentPositionWings</c>, which steers it toward a point on a circle of <c>CirclingRadius</c> around the position it should hold. Helicopters and every other aircraft AI are unaffected.
/// </summary>
public sealed partial class JetAIUpdate
{
    // The reference aims at the point of the circle that lies (π − π/8) around from the bearing to the center: "the spot on the opposite side of the circle".
    private static readonly Fixed CircleAimOffset = FixedTrig.Pi - FixedTrig.Pi / Fixed.FromInt(8);

    private bool _circling;
    private Fixed _circleX;
    private Fixed _circleY;

    /// <summary>A winged aircraft that is airborne (<see cref="AllowAirLoco"/>) is steered by <see cref="AIUpdateInterface.SteerWinged"/>; the taxi locomotor and helicopter locomotors are not.</summary>
    protected override bool FliesWithWings(LocomotorInfo locomotor) => _allowAir && locomotor.IsWings;

    /// <summary>The order ended at the point it was aimed at; the aircraft holds that point by circling it (the reference's <c>m_maintainPos</c>, which is where the move ended).</summary>
    protected override void OnWingedArrival(Fixed x, Fixed y)
    {
        _circling = true;
        _circleX = x;
        _circleY = y;
    }

    /// <summary>The aircraft is flying with nothing to head for: circle (<see cref="CircleStep"/>) instead of standing still.</summary>
    private bool ShouldCircle(JetPhase phase)
        => _allowAir && !Entity.HasMoveTarget && (phase is JetPhase.None or JetPhase.AwaitLanding or JetPhase.CirclingDeadAirfield) && ActiveLocomotor is { IsWings: true };

    /// <summary>
    /// <c>Locomotor::maintainCurrentPositionWings</c>: the aircraft flies toward the point of a circle of <c>CirclingRadius</c> around its center that lies (π − π/8) around from the bearing from the aircraft to the
    /// center (a negative radius mirrors that, so positive circles clockwise and negative counter-clockwise; 0 is <c>calcMinTurnRadius</c>: the smallest circle at <c>MinSpeed</c>), at the locomotor's minimum
    /// speed. The center is the point the last move order ended at, else the position where the aircraft started to circle. Measured: the track settles on a circle of about 0.93 · radius.
    /// The settled circle depends on the length of the sides of the polygon the aircraft flies, so the aim point is taken once per logic frame of the original (the 30 Hz step) and the
    /// frame's step is flown in equal shares per tick (<see cref="AIUpdateInterface.SteerWinged"/>).
    /// </summary>
    private void CircleStep(IWorldServices world)
    {
        if (ContinueWingedStep())
        {
            world.MarkDirty(Entity);
            return;
        }
        var locomotor = ActiveLocomotor!;
        if (!_circling)
        {
            _circling = true;
            _circleX = Entity.X;
            _circleY = Entity.Y;
        }
        var radius = locomotor.CirclingRadius;
        var aim = CircleAimOffset;
        if (radius.IsNegative)
        {
            radius = -radius;
            aim = -aim;
        }
        if (radius == Fixed.Zero)
        {
            FlightFrameConstants(world.Time, locomotor, out _, out _, out _, out var turnRate, out var minSpeed);
            radius = TurnRadius(turnRate, minSpeed);
        }
        var dx = _circleX - Entity.X;
        var dy = _circleY - Entity.Y;
        var bearing = dx.Abs() + dy.Abs() < Epsilon ? Entity.Angle : FixedTrig.Atan2(dy, dx);
        var angle = bearing + aim;
        SteerWinged(world.Time, locomotor, _circleX + FixedTrig.Cos(angle) * radius, _circleY + FixedTrig.Sin(angle) * radius, Fixed.Zero); // the slowest the wings fly: SteerWinged raises the goal to the minimum speed
        world.MarkDirty(Entity);
    }

    private void DescribeFlight(IStateWriter writer)
    {
        DescribeWingStep(writer);
        if (!_circling) return;
        writer.Bool("circling", true);
        writer.Fixed("circleX", _circleX);
        writer.Fixed("circleY", _circleY);
    }

    private void LoadFlight(IStateReader reader)
    {
        LoadWingStep(reader);
        _circling = reader.Has("circling") && reader.Bool("circling");
        _circleX = _circling ? reader.Fixed("circleX") : Fixed.Zero;
        _circleY = _circling ? reader.Fixed("circleY") : Fixed.Zero;
    }
}
