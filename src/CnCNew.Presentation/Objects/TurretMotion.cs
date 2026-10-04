using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Objects;

/// <summary>
/// The motion data of one turret (<c>TurretAIData</c>, the <c>Turret</c>/<c>AltTurret</c> block of an AI update module): angular speeds in radians per logic tick
/// (<c>TurretTurnRate</c>/<c>TurretPitchRate</c> are degrees per second in the INI), the resting angle, the pitch rules and the time the turret waits before it recentres.
/// Defaults are the reference's (<c>TurretAI.cpp</c>: 0.01 rad per frame for both rates, two seconds of recentre time).
/// </summary>
public sealed record TurretMotion(float TurnRadiansPerTick, float PitchRadiansPerTick, float NaturalAngle, float FirePitch, float MinPitch, bool AllowsPitch, int RecenterTicks)
{
    public const float DefaultRatePerTick = 0.01f;
    public const int LogicTicksPerSecond = 30;

    public static TurretMotion Default { get; } = new(DefaultRatePerTick, DefaultRatePerTick, 0f, 0f, 0f, false, 2 * LogicTicksPerSecond);

    /// <summary>Reads the <c>Turret</c> (index 0) and <c>AltTurret</c> (index 1) blocks of an object's modules; an absent block is null.</summary>
    public static ImmutableArray<TurretMotion?> FromModules(ImmutableArray<ModuleConfiguration> modules)
    {
        TurretMotion? main = null;
        TurretMotion? alt = null;
        foreach (var module in modules)
        {
            if (module.Sections.IsDefaultOrEmpty) continue;
            foreach (var section in module.Sections)
            {
                if (section.Declaration.Equals("Turret", StringComparison.OrdinalIgnoreCase)) main = Parse(section);
                else if (section.Declaration.Equals("AltTurret", StringComparison.OrdinalIgnoreCase)) alt = Parse(section);
            }
        }

        return main is null && alt is null ? [] : [main, alt];
    }

    private static TurretMotion Parse(DefinitionSection section)
    {
        string? Text(string key) => section.Fields.LastOrDefault(field => field.Name.Equals(key, StringComparison.OrdinalIgnoreCase))?.Value.OriginalText.Trim();
        float Number(string key, float fallback) => Text(key) is { } text && float.TryParse(text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;
        const float Degrees = MathF.PI / 180f;
        var turn = Text("TurretTurnRate") is null ? DefaultRatePerTick : Number("TurretTurnRate", 0f) * Degrees / LogicTicksPerSecond;
        var pitch = Text("TurretPitchRate") is null ? DefaultRatePerTick : Number("TurretPitchRate", 0f) * Degrees / LogicTicksPerSecond;
        var recenter = Text("RecenterTime") is null ? Default.RecenterTicks : (int)Math.Ceiling(Number("RecenterTime", 2000f) * LogicTicksPerSecond / 1000f);
        return new TurretMotion(turn, pitch, Number("NaturalTurretAngle", 0f) * Degrees, Number("FirePitch", 0f) * Degrees, Number("MinPhysicalPitch", 0f) * Degrees,
            Text("AllowsPitch") is { } allows && allows.StartsWith("Yes", StringComparison.OrdinalIgnoreCase), recenter);
    }
}

/// <summary>
/// The turret angle and pitch of every object that has a <see cref="TurretMotion"/> (<c>AIUpdateInterface::getTurretRotAndPitch</c>, what
/// <c>W3DModelDraw::handleClientTurretPositioning</c> draws). The simulation has no turret state: a shot is resolved without turning anything. The presentation
/// therefore follows the shots - when an object fires (<see cref="SimEventKind.WeaponFired"/>) its turret turns at its turn rate toward the victim, holds the aim
/// for <see cref="TurretMotion.RecenterTicks"/> after the last shot, then turns back to its natural angle. This is an approximation of <c>TurretAI</c> (no idle
/// scan, no aiming before the first shot) that only draws; it never influences the simulation. The state advances once per simulated tick, so it is deterministic.
/// </summary>
public sealed class TurretTracker
{
    private struct Aim
    {
        public float Angle;
        public float Pitch;
        public long LastShotTick;
        public EntityId Target;
        public float TargetX;
        public float TargetY;
        public float TargetZ;
        public bool HasTarget;
        public bool Rotating;
    }

    private readonly Dictionary<(uint Entity, int Turret), Aim> _aims = [];

    /// <summary>
    /// <c>TurretAI::friend_turnTowardsAngle</c> sets <c>MODELCONDITION_TURRET_ROTATE</c> on the owner while a turret has not reached the angle it turns to and clears it as soon as it snaps there:
    /// true when either turret of the object turned in the last <see cref="Update"/> without arriving.
    /// </summary>
    public bool IsRotating(uint entity)
        => (_aims.TryGetValue((entity, 0), out var main) && main.Rotating) || (_aims.TryGetValue((entity, 1), out var alt) && alt.Rotating);

    /// <summary>Remembers where an object shot (victim position at the time of the shot).</summary>
    public void Observe(SimEvent simEvent, SimulationWorld world)
    {
        if (simEvent.Kind != SimEventKind.WeaponFired) return;
        float x, y, z;
        EntityId target = EntityId.Invalid;
        if (world.FindEntity(simEvent.Other) is { } victim) { x = ToFloat(victim.X); y = ToFloat(victim.Y); z = ToFloat(victim.Z); target = victim.Id; }
        else if (simEvent.HasPosition) { x = ToFloat(simEvent.X); y = ToFloat(simEvent.Y); z = ToFloat(simEvent.Z); }
        else return;
        // The event does not say which turret fired; the main turret controls the primary weapon in nearly every object, so it aims.
        var key = (simEvent.Entity.Value, 0);
        _aims.TryGetValue(key, out var aim);
        aim.LastShotTick = simEvent.Tick;
        aim.Target = target;
        aim.TargetX = x;
        aim.TargetY = y;
        aim.TargetZ = z;
        aim.HasTarget = true;
        _aims[key] = aim;
    }

    /// <summary>Moves the turret one tick; returns its angle relative to the body and its pitch (radians), or null when the object has no such turret.</summary>
    public (float Angle, float Pitch)? Update(Entity entity, ObjectVisual visual, int turretIndex, long tick, SimulationWorld world)
    {
        if (visual.Turrets.IsDefaultOrEmpty || turretIndex >= visual.Turrets.Length || visual.Turrets[turretIndex] is not { } motion) return null;
        var key = (entity.Id.Value, turretIndex);
        if (!_aims.TryGetValue(key, out var aim)) aim = new Aim { Angle = motion.NaturalAngle };
        var bodyYaw = ToFloat(entity.Angle);
        float desiredAngle = motion.NaturalAngle;
        float desiredPitch = 0f;
        if (aim.HasTarget && tick - aim.LastShotTick <= motion.RecenterTicks)
        {
            if (aim.Target.IsValid && world.FindEntity(aim.Target) is { } victim)
            {
                aim.TargetX = ToFloat(victim.X);
                aim.TargetY = ToFloat(victim.Y);
                aim.TargetZ = ToFloat(victim.Z);
            }

            var dx = aim.TargetX - ToFloat(entity.X);
            var dy = aim.TargetY - ToFloat(entity.Y);
            desiredAngle = Normalize(MathF.Atan2(dy, dx) - bodyYaw);
            if (motion.AllowsPitch)
            {
                var horizontal = MathF.Sqrt(dx * dx + dy * dy);
                desiredPitch = motion.FirePitch > 0f ? motion.FirePitch : MathF.Max(motion.MinPitch, MathF.Atan2(aim.TargetZ - ToFloat(entity.Z), MathF.Max(horizontal, 1f)));
            }
        }
        else aim.HasTarget = false;

        // TurretAI.cpp: a turret that is closer than one step snaps to the angle (no rotation), otherwise it turns by one step.
        aim.Rotating = Math.Abs(Normalize(desiredAngle - aim.Angle)) > motion.TurnRadiansPerTick;
        aim.Angle = Toward(aim.Angle, desiredAngle, motion.TurnRadiansPerTick);
        aim.Pitch = Toward(aim.Pitch, desiredPitch, motion.PitchRadiansPerTick);
        _aims[key] = aim;
        return (aim.Angle, aim.Pitch);
    }

    /// <summary>Forgets the objects that no longer exist, so the table does not grow over a long match.</summary>
    public void Prune(Func<uint, bool> exists)
    {
        foreach (var key in _aims.Keys.Where(key => !exists(key.Entity)).ToArray()) _aims.Remove(key);
    }

    private static float Toward(float current, float desired, float step)
    {
        var difference = Normalize(desired - current);
        return Math.Abs(difference) <= step ? Normalize(desired) : Normalize(current + MathF.Sign(difference) * step);
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
