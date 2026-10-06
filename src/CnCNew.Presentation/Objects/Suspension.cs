using System.Globalization;
using CnCNew.Data;
using CnCNew.Data.Definitions;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Objects;

public enum SuspensionKind { Treads, Wheels }

/// <summary>
/// The chassis dynamics of a ground locomotor (<c>Locomotor.cpp</c> template fields read by <c>Drawable::calcPhysicsXformTreads/Wheels</c>): spring and damper constants for pitch
/// and roll, how strongly acceleration tilts the body, the wheel steering angle and the suspension travel. Angles are radians, rates per original frame (1/30 s). Defaults are the reference's
/// (<c>LocomotorTemplate::LocomotorTemplate</c>): stiffness 0.1, damping 0.9, everything else zero.
/// </summary>
public sealed record SuspensionMotion(
    SuspensionKind Kind, float PitchStiffness, float RollStiffness, float PitchDamping, float RollDamping, float ForwardAccelCoef, float LateralAccelCoef,
    float UniformAxialDamping, float AccelPitchLimit, float BounceKick, bool HasSuspension, float WheelTurnAngle, float MaxWheelExtension)
{
    private const float Degrees = MathF.PI / 180f;

    /// <summary>Null for every appearance without a chassis simulation (legs, hover, wings, thrust, other): <c>Drawable::calcPhysicsXform</c> handles only <c>FOUR_WHEELS</c> and <c>TREADS</c> on the ground.</summary>
    public static SuspensionMotion? FromLocomotor(LocomotorDefinition locomotor)
    {
        string? Text(string key) => locomotor.Fields.LastOrDefault(field => field.Name.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value.OriginalText.Trim();
        float Number(string key, float fallback) => Text(key) is { } text && float.TryParse(text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
        var appearance = Text("Appearance")?.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        SuspensionKind kind;
        if (string.Equals(appearance, "FOUR_WHEELS", StringComparison.OrdinalIgnoreCase)) kind = SuspensionKind.Wheels;
        else if (string.Equals(appearance, "TREADS", StringComparison.OrdinalIgnoreCase)) kind = SuspensionKind.Treads;
        else return null;
        return new SuspensionMotion(
            kind, Number("PitchStiffness", 0.1f), Number("RollStiffness", 0.1f), Number("PitchDamping", 0.9f), Number("RollDamping", 0.9f),
            Number("ForwardAccelerationPitchFactor", 0f), Number("LateralAccelerationRollFactor", 0f), Number("UniformAxialDamping", 1f),
            Number("AccelerationPitchLimit", 0f) * Degrees, Number("BounceAmount", 0f) * Degrees / OriginalFrame.PerSecond,
            Text("HasSuspension") is { } suspension && suspension.StartsWith("Yes", StringComparison.OrdinalIgnoreCase),
            Number("FrontWheelTurnAngle", 0f) * Degrees, Number("MaximumWheelExtension", 0f));
    }
}

/// <summary>What the object layer draws of the chassis: the body tilt and lift (<c>Drawable::applyPhysicsXform</c>) and the wheel steering angle and spring offsets (<c>TWheelInfo</c>).</summary>
public readonly record struct ChassisPose(float Pitch, float Roll, float LiftZ, float WheelAngle, float FrontLeft, float FrontRight, float RearLeft, float RearRight)
{
    public static ChassisPose Level => default;
    public bool IsLevel => this == default;
}

/// <summary>
/// The chassis pitch and roll, body lift and wheel springs of every object whose locomotor is <c>TREADS</c> or <c>FOUR_WHEELS</c> (what the original computes client-side in
/// <c>Drawable::calcPhysicsXformTreads/Wheels</c>; the simulation keeps none of it). The body settles onto the terrain slope with a damped spring, tilts with acceleration
/// and turns, wheeled vehicles bounce a little while they drive and steer their front wheels toward the turn. This is a port of the algorithm with these deliberate differences:
/// no crushing/overlap lift (the simulation has no overlap state), the bounce direction comes from a hash of (object, original frame) instead of the client random generator, and the
/// terrain normal is taken by central differences of the simulation height field. The state never influences the simulation.
/// <para>
/// The recursion is the original's, one step per original frame (1/30 s): its constants are factors per frame (damping 0.9, a rising nose halved, wheels following by a half,
/// steering by a tenth), not rates of a differential equation, so it cannot be stepped at another width without changing how it swings. It therefore advances on the ticks
/// that begin an original frame (every tick at 30 Hz, every second one at 60 Hz) and holds its pose on the ticks in between; velocity and acceleration are differences over
/// one original frame at every rate.
/// </para>
/// </summary>
public sealed class SuspensionTracker
{
    private const float ForwardEpsilon = 1e-4f;
    private const float TurnEpsilon = 0.002f;
    private const float AirborneHeight = 1f;

    private sealed class State
    {
        public float Pitch, PitchRate, Roll, RollRate;
        public float AccelPitch, AccelPitchRate, AccelRoll, AccelRollRate;
        public float WheelAngle, FrontLeft, FrontRight, RearLeft, RearRight;
        public float X, Y, Yaw, VelX, VelY;
        public bool HasPrevious;
        public ChassisPose Pose;
    }

    private readonly Dictionary<uint, State> _states = [];

    /// <summary>
    /// The pose of one object after the tick just simulated: advanced by one original frame when that tick began one, held otherwise; null when the object has no chassis
    /// simulation. Call once per simulated tick.
    /// </summary>
    public ChassisPose? Update(Entity entity, ObjectVisual visual, SimulationWorld world, float snapDistance)
    {
        if (visual.Suspension is not { } motion) return null;
        // An object first seen between two original frames starts with the next one, so its first velocity is a difference over a whole frame.
        if (!world.Time.IsOriginalFrameBoundary(world.Tick)) return _states.TryGetValue(entity.Id.Value, out var held) ? held.Pose : ChassisPose.Level;
        if (!_states.TryGetValue(entity.Id.Value, out var state)) _states[entity.Id.Value] = state = new State();
        return state.Pose = Step(state, motion, entity, world, snapDistance);
    }

    private static ChassisPose Step(State state, SuspensionMotion motion, Entity entity, SimulationWorld world, float snapDistance)
    {
        var x = ToFloat(entity.X);
        var y = ToFloat(entity.Y);
        var yaw = ToFloat(entity.Angle);
        float velX = 0f, velY = 0f, accelX = 0f, accelY = 0f, yawStep = 0f;
        if (state.HasPrevious)
        {
            velX = x - state.X;
            velY = y - state.Y;
            if (velX * velX + velY * velY > snapDistance * snapDistance) { velX = velY = 0f; }
            accelX = velX - state.VelX;
            accelY = velY - state.VelY;
            yawStep = Normalize(yaw - state.Yaw);
        }

        state.X = x; state.Y = y; state.Yaw = yaw; state.VelX = velX; state.VelY = velY; state.HasPrevious = true;
        var dirX = MathF.Cos(yaw);
        var dirY = MathF.Sin(yaw);
        var perpX = -dirY;
        var perpY = dirX;
        var speed = MathF.Sqrt(velX * velX + velY * velY);
        var forwardSpeed = velX * dirX + velY * dirY;
        var motive = speed > ForwardEpsilon;

        var (normalX, normalY) = TerrainNormal(world, entity.X, entity.Y);
        var groundPitch = (normalX * dirX + normalY * dirY) * (MathF.PI / 2f);
        var groundRoll = (normalX * perpX + normalY * perpY) * (MathF.PI / 2f);
        var ground = world.GroundHeightAt(entity.X, entity.Y);
        var height = ToFloat(entity.Z - ground);
        var airborne = world.IsSignificantlyAboveTerrain(entity);
        var length = ToFloat(entity.Template.GeometryMajorRadius);
        var width = ToFloat(entity.Template.GeometryMinorRadius);

        var wheels = motion.Kind == SuspensionKind.Wheels;
        if (wheels && airborne)
        {
            if (motion.HasSuspension)
            {
                // Wheels extend when airborne (calcPhysicsXformWheels).
                var target = height > -motion.MaxWheelExtension ? motion.MaxWheelExtension : 0f;
                state.RearLeft += (target - state.RearLeft) / 2f;
                state.RearRight += (target - state.RearRight) / 2f;
            }

            var airPitchHeight = length * MathF.Sin(state.Pitch + state.AccelPitch - groundPitch);
            var airRollHeight = width * MathF.Sin(state.Roll + state.AccelRoll - groundRoll);
            return new ChassisPose(0f, 0f, MathF.Abs(airPitchHeight) / 4f + MathF.Abs(airRollHeight) / 4f, state.WheelAngle, state.FrontLeft, state.FrontRight, state.RearLeft, state.RearRight);
        }

        // The locomotor's speed is distance per tick of the world; the chassis measures distance per original frame.
        var topSpeed = ToFloat(entity.Template.Locomotor?.Speed ?? Fixed.Zero) * world.Time.TicksPerOriginalFrame;
        if (wheels && !airborne && speed > topSpeed / 10f)
        {
            // Rough ground: kick the body when it is not already moving (BOUNCE_ANGLE_KICK scaled by speed over top speed).
            var maxSpeed = topSpeed;
            var factor = maxSpeed > 0f ? speed / maxSpeed : 0f;
            if (MathF.Abs(state.PitchRate) < factor * motion.BounceKick / 4f && MathF.Abs(state.RollRate) < factor * motion.BounceKick / 8f)
            {
                var pick = Hash(entity.Id.Value, world.Time.ToOriginalFrames(world.Tick)) & 3;
                state.PitchRate += (pick & 1) == 0 ? -motion.BounceKick * factor : motion.BounceKick * factor;
                state.RollRate += pick < 2 ? -motion.BounceKick * factor / 2f : motion.BounceKick * factor / 2f;
            }
        }

        // The ground pushes back only while the body touches it; a rising nose is damped twice as hard.
        state.PitchRate += -motion.PitchStiffness * (state.Pitch - groundPitch) - motion.PitchDamping * state.PitchRate;
        if (state.PitchRate > 0f) state.PitchRate *= 0.5f;
        state.RollRate += -motion.RollStiffness * (state.Roll - groundRoll) - motion.RollDamping * state.RollRate;
        state.Pitch += state.PitchRate * motion.UniformAxialDamping;
        state.Roll += state.RollRate * motion.UniformAxialDamping;

        // Acceleration sway: damp back to zero.
        state.AccelPitchRate += -motion.PitchStiffness * state.AccelPitch - motion.PitchDamping * state.AccelPitchRate;
        state.AccelPitch += state.AccelPitchRate;
        state.AccelRollRate += -motion.RollStiffness * state.AccelRoll - motion.RollDamping * state.AccelRollRate;
        state.AccelRoll += state.AccelRollRate;
        var totalPitch = state.Pitch + state.AccelPitch;
        var totalRoll = state.Roll + state.AccelRoll;
        if (motive)
        {
            state.AccelPitchRate += -(motion.ForwardAccelCoef * (dirX * accelX + dirY * accelY));
            state.AccelRollRate += -(motion.LateralAccelCoef * (-dirY * accelX + dirX * accelY));
        }

        state.AccelPitch = Math.Clamp(state.AccelPitch, -motion.AccelPitchLimit, motion.AccelPitchLimit);
        state.AccelRoll = Math.Clamp(state.AccelRoll, -motion.AccelPitchLimit, motion.AccelPitchLimit);

        var pitchHeight = length * MathF.Sin(totalPitch - groundPitch);
        var rollHeight = width * MathF.Sin(totalRoll - groundRoll);
        if (wheels && motion.HasSuspension) UpdateWheels(state, motion, yawStep, forwardSpeed, pitchHeight, rollHeight);

        // Raise the body so a tilted hull does not sink into the ground; steeper than 22.5 degrees lifts it more.
        var divisor = 4f;
        var pitch = MathF.Abs(totalPitch - groundPitch);
        if (pitch > MathF.PI / 8f) divisor = (4f * MathF.PI / 8f + (pitch - MathF.PI / 8f)) / pitch;
        var lift = MathF.Abs(pitchHeight) / divisor + MathF.Abs(rollHeight) / divisor;
        return new ChassisPose(totalPitch, totalRoll, wheels ? lift : 0f, state.WheelAngle, state.FrontLeft, state.FrontRight, state.RearLeft, state.RearRight);
    }

    /// <summary>The four wheel offsets from the body tilt and the steering angle toward the turn (<c>calcPhysicsXformWheels</c>, <c>SPRING_FACTOR</c> 0.9, steering smoothed over ten original frames).</summary>
    private static void UpdateWheels(State state, SuspensionMotion motion, float yawStep, float forwardSpeed, float pitchHeight, float rollHeight)
    {
        var wanted = yawStep > TurnEpsilon ? motion.WheelTurnAngle : yawStep < -TurnEpsilon ? -motion.WheelTurnAngle : 0f;
        if (forwardSpeed < 0f) wanted = -wanted;
        state.WheelAngle += (wanted - state.WheelAngle) / 10f;

        const float Spring = 0.9f;
        float frontLeft, frontRight, rearLeft, rearRight;
        if (pitchHeight < 0f)
        {
            frontLeft = frontRight = Spring * (pitchHeight / 3f + pitchHeight / 2f);
            rearLeft = rearRight = -pitchHeight / 2f + pitchHeight / 4f;
        }
        else
        {
            frontLeft = frontRight = -pitchHeight / 4f + pitchHeight / 2f;
            rearLeft = rearRight = Spring * (-pitchHeight / 2f + -pitchHeight / 3f);
        }

        if (rollHeight > 0f)
        {
            frontRight += -Spring * (rollHeight / 3f + rollHeight / 2f);
            rearRight += -Spring * (rollHeight / 3f + rollHeight / 2f);
            rearLeft += rollHeight / 2f - rollHeight / 4f;
            frontLeft += rollHeight / 2f - rollHeight / 4f;
        }
        else
        {
            frontRight += -rollHeight / 2f + rollHeight / 4f;
            rearRight += -rollHeight / 2f + rollHeight / 4f;
            rearLeft += Spring * (rollHeight / 3f + rollHeight / 2f);
            frontLeft += Spring * (rollHeight / 3f + rollHeight / 2f);
        }

        // A wheel that goes down is damped by half, one that goes up follows at once; none extends past the maximum extension.
        static float Follow(float current, float target, float maxExtension)
        {
            var value = target < current ? current + (target - current) / 2f : target;
            return value < maxExtension ? maxExtension : value;
        }

        state.FrontLeft = Follow(state.FrontLeft, frontLeft, motion.MaxWheelExtension);
        state.FrontRight = Follow(state.FrontRight, frontRight, motion.MaxWheelExtension);
        state.RearLeft = Follow(state.RearLeft, rearLeft, motion.MaxWheelExtension);
        state.RearRight = Follow(state.RearRight, rearRight, motion.MaxWheelExtension);
    }

    /// <summary>Forgets the objects that no longer exist.</summary>
    public void Prune(Func<uint, bool> exists)
    {
        foreach (var id in _states.Keys.Where(id => !exists(id)).ToArray()) _states.Remove(id);
    }

    private static (float X, float Y) TerrainNormal(SimulationWorld world, Fixed x, Fixed y)
    {
        var step = Fixed.FromInt(2);
        var dx = ToFloat(world.GroundHeightAt(x + step, y) - world.GroundHeightAt(x - step, y)) / 4f;
        var dy = ToFloat(world.GroundHeightAt(x, y + step) - world.GroundHeightAt(x, y - step)) / 4f;
        var length = MathF.Sqrt(dx * dx + dy * dy + 1f);
        return (-dx / length, -dy / length);
    }

    private static uint Hash(uint id, long frame)
    {
        unchecked
        {
            var value = id * 2654435761u ^ (uint)frame * 40503u;
            value ^= value >> 15;
            value *= 2246822519u;
            return value >> 8;
        }
    }

    private static float Normalize(float angle)
    {
        angle %= MathF.Tau;
        if (angle > MathF.PI) angle -= MathF.Tau;
        else if (angle < -MathF.PI) angle += MathF.Tau;
        return angle;
    }

    private static float ToFloat(Fixed value) => (float)(value.Raw / (double)Fixed.ScaleRaw);
}
