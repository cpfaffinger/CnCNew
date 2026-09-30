using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Data.Definitions;

namespace CnCNew.Presentation.Fx;

/// <summary><c>ParticleSystemInfo::ParticleShaderType</c>; <see cref="None"/> is <c>INVALID_SHADER</c>.</summary>
public enum ParticleShader { None, Additive, Alpha, AlphaTest, Multiply }

/// <summary><c>ParticleSystemInfo::ParticleType</c>; <see cref="None"/> is <c>INVALID_TYPE</c>.</summary>
public enum ParticleKind { None, Particle, Drawable, Streak, VolumeParticle, Smudge }

/// <summary><c>ParticlePriorityType</c> (<c>ParticleSys.h</c>): higher values survive budget culling longer.</summary>
public enum ParticlePriority
{
    Invalid = 0, WeaponExplosion, Scorchmark, DustTrail, Buildup, DebrisTrail, UnitDamageFx, DeathExplosion, SemiConstant,
    Constant, WeaponTrail, AreaEffect, Critical, AlwaysRender,
}

public enum EmissionVelocityType { None, Ortho, Spherical, Hemispherical, Cylindrical, Outward }

public enum EmissionVolumeType { None, Point, Line, Box, Sphere, Cylinder }

/// <summary><c>ParticleSystemInfo::WindMotion</c>; INI spelling <c>NONE</c>, <c>Unused</c>, <c>PingPong</c>, <c>Circular</c>.</summary>
public enum ParticleWindMotion { Invalid, NotUsed, PingPong, Circular }

/// <summary><c>ParticleSystemInfo::RandomKeyframe</c>: an alpha range reached at <see cref="Frame"/> (0 terminates the list).</summary>
public readonly record struct AlphaKeyframe(FxRandomVariable Value, uint Frame);

/// <summary><c>RGBColorKeyframe</c>: a color (components 0..1) reached at <see cref="Frame"/> (0 terminates the list).</summary>
public readonly record struct ColorKeyframe(Vector3 Color, uint Frame);

/// <summary>
/// A typed <c>ParticleSystemTemplate</c> (<c>ParticleSys.cpp ParticleSystemTemplate::m_fieldParseTable</c>). Times are logic frames
/// (the particle editor writes frames, not milliseconds), speeds are distance per frame, angles are radians.
/// <para>
/// The emission velocity and volume parameters are stored the way the reference stores them: in unions. <see cref="VelocitySlots"/>
/// holds <c>m_emissionVelocity</c> (slot 0: ortho X / spherical / hemispherical / cylindrical radial / outward speed; slot 1: ortho Y /
/// cylindrical normal / outward other; slot 2: ortho Z) and <see cref="VolumeSlots"/> holds <c>m_emissionVolume</c> (line start xyz and
/// end xyz; box half size = slots 0..2; sphere radius = slot 0; cylinder radius/length = slots 0/1). The named accessors read them.
/// </para>
/// </summary>
public sealed record ParticleSystemTemplate(
    string Name,
    ParticlePriority Priority,
    bool IsOneShot,
    ParticleShader Shader,
    ParticleKind Kind,
    string ParticleName,
    FxRandomVariable AngleZ,
    FxRandomVariable AngularRateZ,
    FxRandomVariable AngularDamping,
    FxRandomVariable VelocityDamping,
    float Gravity,
    string SlaveSystem,
    Vector3 SlavePositionOffset,
    string PerParticleAttachedSystem,
    FxRandomVariable Lifetime,
    uint SystemLifetime,
    FxRandomVariable StartSize,
    FxRandomVariable StartSizeRate,
    FxRandomVariable SizeRate,
    FxRandomVariable SizeRateDamping,
    ImmutableArray<AlphaKeyframe> AlphaKeys,
    ImmutableArray<ColorKeyframe> ColorKeys,
    FxRandomVariable ColorScale,
    FxRandomVariable BurstDelay,
    FxRandomVariable BurstCount,
    FxRandomVariable InitialDelay,
    Vector3 DriftVelocity,
    EmissionVelocityType VelocityType,
    ImmutableArray<FxRandomVariable> VelocitySlots,
    EmissionVolumeType VolumeType,
    ImmutableArray<float> VolumeSlots,
    bool IsHollow,
    bool IsGroundAligned,
    bool IsEmitAboveGroundOnly,
    bool IsParticleUpTowardsEmitter,
    ParticleWindMotion WindMotion,
    float WindAngleChangeMin,
    float WindAngleChangeMax,
    float WindPingPongStartAngleMin,
    float WindPingPongStartAngleMax,
    float WindPingPongEndAngleMin,
    float WindPingPongEndAngleMax)
{
    public const int MaxKeyframes = 8;

    public FxRandomVariable OrthoX => VelocitySlots[0];
    public FxRandomVariable OrthoY => VelocitySlots[1];
    public FxRandomVariable OrthoZ => VelocitySlots[2];
    public FxRandomVariable SphericalSpeed => VelocitySlots[0];
    public FxRandomVariable CylindricalRadial => VelocitySlots[0];
    public FxRandomVariable CylindricalNormal => VelocitySlots[1];
    public FxRandomVariable OutwardSpeed => VelocitySlots[0];
    public FxRandomVariable OutwardOtherSpeed => VelocitySlots[1];

    public Vector3 LineStart => new(VolumeSlots[0], VolumeSlots[1], VolumeSlots[2]);
    public Vector3 LineEnd => new(VolumeSlots[3], VolumeSlots[4], VolumeSlots[5]);
    public Vector3 BoxHalfSize => new(VolumeSlots[0], VolumeSlots[1], VolumeSlots[2]);
    public float SphereRadius => VolumeSlots[0];
    public float CylinderRadius => VolumeSlots[0];
    public float CylinderLength => VolumeSlots[1];

    /// <summary>The texture of <see cref="ParticleKind.Particle"/>/<see cref="ParticleKind.Streak"/>/<see cref="ParticleKind.VolumeParticle"/> systems.</summary>
    public bool UsesTexture => Kind is ParticleKind.Particle or ParticleKind.Streak or ParticleKind.VolumeParticle or ParticleKind.Smudge;

    /// <summary><c>W3DParticleSystemManager::doParticles</c> routes systems whose texture name starts with <c>SMUD</c> to the heat-haze smudge renderer.</summary>
    public bool IsSmudgeTexture => ParticleName.StartsWith("SMUD", StringComparison.Ordinal);

    internal static readonly string[] PriorityNames =
        ["NONE", "WEAPON_EXPLOSION", "SCORCHMARK", "DUST_TRAIL", "BUILDUP", "DEBRIS_TRAIL", "UNIT_DAMAGE_FX", "DEATH_EXPLOSION",
         "SEMI_CONSTANT", "CONSTANT", "WEAPON_TRAIL", "AREA_EFFECT", "CRITICAL", "ALWAYS_RENDER"];
    internal static readonly string[] ShaderNames = ["NONE", "ADDITIVE", "ALPHA", "ALPHA_TEST", "MULTIPLY"];
    internal static readonly string[] TypeNames = ["NONE", "PARTICLE", "DRAWABLE", "STREAK", "VOLUME_PARTICLE", "SMUDGE"];
    internal static readonly string[] VelocityNames = ["NONE", "ORTHO", "SPHERICAL", "HEMISPHERICAL", "CYLINDRICAL", "OUTWARD"];
    internal static readonly string[] VolumeNames = ["NONE", "POINT", "LINE", "BOX", "SPHERE", "CYLINDER"];
    internal static readonly string[] WindMotionNames = ["NONE", "Unused", "PingPong", "Circular"];

    /// <summary>Parses one compiled <c>ParticleSystem</c> definition; problems go to <paramref name="diagnostics"/>.</summary>
    internal static ParticleSystemTemplate Parse(string name, IEnumerable<DefinitionField> fields, FxDiagnosticBag diagnostics)
    {
        var read = new FxValueReader(diagnostics, $"ParticleSystem {name}");
        var b = new Builder(name);
        var velocityKeyTypes = new SortedSet<EmissionVelocityType>();
        foreach (var field in fields)
        {
            var key = field.Name;
            var text = field.Value.OriginalText;
            switch (key.ToLowerInvariant())
            {
                case "priority": b.Priority = (ParticlePriority)Math.Max(0, read.Index(key, text, PriorityNames, "ParticlePriorityNames")); break;
                case "isoneshot": b.IsOneShot = read.Bool(key, text, false); break;
                case "shader": b.Shader = (ParticleShader)Math.Max(0, read.Index(key, text, ShaderNames, "ParticleShaderTypeNames")); break;
                case "type": b.Kind = (ParticleKind)Math.Max(0, read.Index(key, text, TypeNames, "ParticleTypeNames")); break;
                case "particlename": b.ParticleName = read.Name(key, text) ?? string.Empty; break;
                case "anglez": b.AngleZ = read.RandomVariable(key, text); break;
                case "angularratez": b.AngularRateZ = read.RandomVariable(key, text); break;
                case "angulardamping": b.AngularDamping = read.RandomVariable(key, text); break;
                case "velocitydamping": b.VelocityDamping = read.RandomVariable(key, text); break;
                case "gravity": b.Gravity = read.Real(key, text, 0f); break;
                case "slavesystem": b.SlaveSystem = read.Name(key, text) ?? string.Empty; break;
                case "slaveposoffset": b.SlavePositionOffset = read.Coord3D(key, text); break;
                case "perparticleattachedsystem": b.AttachedSystem = read.Name(key, text) ?? string.Empty; break;
                case "lifetime": b.Lifetime = read.RandomVariable(key, text); break;
                case "systemlifetime": b.SystemLifetime = read.UnsignedInt(key, text, 0); break;
                case "size": b.StartSize = read.RandomVariable(key, text); break;
                case "startsizerate": b.StartSizeRate = read.RandomVariable(key, text); break;
                case "sizerate": b.SizeRate = read.RandomVariable(key, text); break;
                case "sizeratedamping": b.SizeRateDamping = read.RandomVariable(key, text); break;
                case "colorscale": b.ColorScale = read.RandomVariable(key, text); break;
                case "burstdelay": b.BurstDelay = read.RandomVariable(key, text); break;
                case "burstcount": b.BurstCount = read.RandomVariable(key, text); break;
                case "initialdelay": b.InitialDelay = read.RandomVariable(key, text); break;
                case "driftvelocity": b.DriftVelocity = read.Coord3D(key, text); break;
                case "velocitytype": b.VelocityType = (EmissionVelocityType)Math.Max(0, read.Index(key, text, VelocityNames, "EmissionVelocityTypeNames")); break;
                case "velorthox": b.Velocity[0] = read.RandomVariable(key, text); velocityKeyTypes.Add(EmissionVelocityType.Ortho); break;
                case "velorthoy": b.Velocity[1] = read.RandomVariable(key, text); velocityKeyTypes.Add(EmissionVelocityType.Ortho); break;
                case "velorthoz": b.Velocity[2] = read.RandomVariable(key, text); velocityKeyTypes.Add(EmissionVelocityType.Ortho); break;
                case "velspherical": b.Velocity[0] = read.RandomVariable(key, text); velocityKeyTypes.Add(EmissionVelocityType.Spherical); break;
                case "velhemispherical": b.Velocity[0] = read.RandomVariable(key, text); velocityKeyTypes.Add(EmissionVelocityType.Hemispherical); break;
                case "velcylindricalradial": b.Velocity[0] = read.RandomVariable(key, text); velocityKeyTypes.Add(EmissionVelocityType.Cylindrical); break;
                case "velcylindricalnormal": b.Velocity[1] = read.RandomVariable(key, text); velocityKeyTypes.Add(EmissionVelocityType.Cylindrical); break;
                case "veloutward": b.Velocity[0] = read.RandomVariable(key, text); velocityKeyTypes.Add(EmissionVelocityType.Outward); break;
                case "veloutwardother": b.Velocity[1] = read.RandomVariable(key, text); velocityKeyTypes.Add(EmissionVelocityType.Outward); break;
                case "volumetype": b.VolumeType = (EmissionVolumeType)Math.Max(0, read.Index(key, text, VolumeNames, "EmissionVolumeTypeNames")); break;
                case "vollinestart": SetSlots(b.Volume, 0, read.Coord3D(key, text)); break;
                case "vollineend": SetSlots(b.Volume, 3, read.Coord3D(key, text)); break;
                case "volboxhalfsize": SetSlots(b.Volume, 0, read.Coord3D(key, text)); break;
                case "volsphereradius": b.Volume[0] = read.Real(key, text, 0f); break;
                case "volcylinderradius": b.Volume[0] = read.Real(key, text, 0f); break;
                case "volcylinderlength": b.Volume[1] = read.Real(key, text, 0f); break;
                case "ishollow": b.IsHollow = read.Bool(key, text, false); break;
                case "isgroundaligned": b.IsGroundAligned = read.Bool(key, text, false); break;
                case "isemitabovegroundonly": b.IsEmitAboveGroundOnly = read.Bool(key, text, false); break;
                case "isparticleuptowardsemitter": b.IsParticleUpTowardsEmitter = read.Bool(key, text, false); break;
                case "windmotion": b.WindMotion = (ParticleWindMotion)Math.Max(0, read.Index(key, text, WindMotionNames, "WindMotionNames")); break;
                case "windanglechangemin": b.WindAngleChangeMin = read.Real(key, text, b.WindAngleChangeMin); break;
                case "windanglechangemax": b.WindAngleChangeMax = read.Real(key, text, b.WindAngleChangeMax); break;
                case "windpingpongstartanglemin": b.WindStartMin = read.Real(key, text, b.WindStartMin); break;
                case "windpingpongstartanglemax": b.WindStartMax = read.Real(key, text, b.WindStartMax); break;
                case "windpingpongendanglemin": b.WindEndMin = read.Real(key, text, b.WindEndMin); break;
                case "windpingpongendanglemax": b.WindEndMax = read.Real(key, text, b.WindEndMax); break;
                default:
                    if (TryKeyframeIndex(key, "Alpha", out var alphaIndex)) b.Alpha[alphaIndex] = ParseAlphaKey(read, key, text);
                    else if (TryKeyframeIndex(key, "Color", out var colorIndex)) b.Color[colorIndex] = ParseColorKey(read, key, text);
                    else diagnostics.Add(FxDiagnosticCodes.UnknownKey, read.Subject, $"'{key}' is not part of ParticleSystemTemplate::m_fieldParseTable.");
                    break;
            }
        }

        var subject = read.Subject;
        if (b.Shader == ParticleShader.None) diagnostics.Add(FxDiagnosticCodes.ShaderNone, subject, "Shader = NONE.");
        if (b.VelocityType == EmissionVelocityType.None) diagnostics.Add(FxDiagnosticCodes.VelocityNone, subject, "VelocityType = NONE.");
        if (b.VolumeType == EmissionVolumeType.None) diagnostics.Add(FxDiagnosticCodes.VolumeNone, subject, "VolumeType = NONE.");
        if (b.Kind == ParticleKind.Drawable) diagnostics.Add(FxDiagnosticCodes.DrawableParticles, subject, $"Type = DRAWABLE ({b.ParticleName}).");
        foreach (var other in velocityKeyTypes.Where(value => value != b.VelocityType))
            diagnostics.Add(FxDiagnosticCodes.VelocityAlias, subject, $"{other} velocity keys on a {b.VelocityType} system.");
        return b.Build();
    }

    private static void SetSlots(float[] slots, int start, Vector3 value)
    {
        slots[start] = value.X;
        slots[start + 1] = value.Y;
        slots[start + 2] = value.Z;
    }

    private static bool TryKeyframeIndex(string key, string prefix, out int index)
    {
        index = -1;
        if (key.Length != prefix.Length + 1 || !key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var digit = key[^1] - '1';
        if (digit is < 0 or >= MaxKeyframes) return false;
        index = digit;
        return true;
    }

    /// <summary><c>ParticleSystemTemplate::parseRandomKeyframe</c>: <c>low high frame</c> (the range is always UNIFORM).</summary>
    private static AlphaKeyframe ParseAlphaKey(FxValueReader read, string key, string text)
    {
        var tokens = FxValueReader.Tokens(text);
        if (tokens.Length < 3 || !FxValueReader.TryReal(tokens[0], out var low) || !FxValueReader.TryReal(tokens[1], out var high)
            || !uint.TryParse(tokens[2], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var frame))
        {
            read.Invalid(key, text, "'low high frame'");
            return default;
        }
        return new AlphaKeyframe(new FxRandomVariable(low, high, FxDistribution.Uniform), frame);
    }

    /// <summary><c>ParticleSystemTemplate::parseRGBColorKeyframe</c>: <c>R:r G:g B:b frame</c>.</summary>
    private static ColorKeyframe ParseColorKey(FxValueReader read, string key, string text)
    {
        var color = read.RgbColor(key, text, out var rest);
        if (rest.Length < 1 || !uint.TryParse(rest[0], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var frame))
        {
            read.Invalid(key, text, "'R:r G:g B:b frame'");
            return default;
        }
        return new ColorKeyframe(color, frame);
    }

    private sealed class Builder(string name)
    {
        public ParticlePriority Priority = ParticlePriority.WeaponExplosion;
        public bool IsOneShot;
        public ParticleShader Shader;
        public ParticleKind Kind;
        public string ParticleName = string.Empty;
        public FxRandomVariable AngleZ, AngularRateZ, AngularDamping, VelocityDamping, Lifetime, StartSize, StartSizeRate, SizeRate,
            SizeRateDamping, ColorScale, BurstDelay, BurstCount, InitialDelay;
        public float Gravity;
        public string SlaveSystem = string.Empty;
        public Vector3 SlavePositionOffset;
        public string AttachedSystem = string.Empty;
        public uint SystemLifetime;
        public readonly AlphaKeyframe[] Alpha = new AlphaKeyframe[MaxKeyframes];
        public readonly ColorKeyframe[] Color = new ColorKeyframe[MaxKeyframes];
        public Vector3 DriftVelocity;
        public EmissionVelocityType VelocityType;
        public readonly FxRandomVariable[] Velocity = new FxRandomVariable[3];
        public EmissionVolumeType VolumeType;
        public readonly float[] Volume = new float[6];
        public bool IsHollow, IsGroundAligned, IsEmitAboveGroundOnly, IsParticleUpTowardsEmitter;
        // ParticleSystemInfo::ParticleSystemInfo defaults.
        public ParticleWindMotion WindMotion = ParticleWindMotion.NotUsed;
        public float WindAngleChangeMin = 0.15f, WindAngleChangeMax = 0.45f;
        public float WindStartMin, WindStartMax = MathF.PI / 4f, WindEndMin = 2f * MathF.PI - MathF.PI / 4f, WindEndMax = 2f * MathF.PI;

        public ParticleSystemTemplate Build() => new(name, Priority, IsOneShot, Shader, Kind, ParticleName, AngleZ, AngularRateZ, AngularDamping,
            VelocityDamping, Gravity, SlaveSystem, SlavePositionOffset, AttachedSystem, Lifetime, SystemLifetime, StartSize, StartSizeRate,
            SizeRate, SizeRateDamping, [..Alpha], [..Color], ColorScale, BurstDelay, BurstCount, InitialDelay, DriftVelocity, VelocityType,
            [..Velocity], VolumeType, [..Volume], IsHollow, IsGroundAligned, IsEmitAboveGroundOnly, IsParticleUpTowardsEmitter, WindMotion,
            WindAngleChangeMin, WindAngleChangeMax, WindStartMin, WindStartMax, WindEndMin, WindEndMax);
    }
}
