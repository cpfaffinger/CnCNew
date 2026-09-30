using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Data.Definitions;

namespace CnCNew.Presentation.Fx;

/// <summary>The nugget kinds of <c>FXList.cpp TheFXListFieldParse</c>.</summary>
public enum FxNuggetKind { Sound, RayEffect, Tracer, LightPulse, ViewShake, TerrainScorch, ParticleSystem, FxListAtBonePos }

/// <summary><c>View::CameraShakeType</c> (<c>SHAKE_SUBTLE</c> .. <c>SHAKE_CINE_INSANE</c>).</summary>
public enum ViewShakeType { Subtle, Normal, Strong, Severe, CineExtreme, CineInsane }

/// <summary><c>Scorches</c> (<c>GameType.h</c>); <see cref="Random"/> is the nugget's <c>RANDOM</c> (-1: one of SCORCH_1..SCORCH_4).</summary>
public enum ScorchType { Random = -1, Scorch1 = 0, Scorch2 = 1, Scorch3 = 2, Scorch4 = 3, ShadowScorch = 4 }

/// <summary>One entry of an <see cref="FxList"/>; the records below mirror the <c>*FXNugget</c> classes field by field.</summary>
public abstract record FxNugget(FxNuggetKind Kind);

/// <summary><c>SoundFXNugget</c>: an audio event name.</summary>
public sealed record SoundFxNugget(string EventName) : FxNugget(FxNuggetKind.Sound);

/// <summary><c>TracerFXNugget</c>; <see cref="Speed"/> is distance per frame, 0 means "use the caller's speed" (the weapon speed).</summary>
public sealed record TracerFxNugget(string TracerName, string BoneName, float Speed, float DecayAt, float Length, float Width, Vector3 Color, float Probability)
    : FxNugget(FxNuggetKind.Tracer);

/// <summary><c>RayEffectFXNugget</c>: a beam drawable template from primary+offset to secondary+offset.</summary>
public sealed record RayEffectFxNugget(string TemplateName, Vector3 PrimaryOffset, Vector3 SecondaryOffset) : FxNugget(FxNuggetKind.RayEffect);

/// <summary><c>LightPulseFXNugget</c>; frames are converted with <c>parseDurationUnsignedInt</c>.</summary>
public sealed record LightPulseFxNugget(Vector3 Color, float Radius, float RadiusAsPercentOfObjectSize, uint IncreaseFrames, uint DecreaseFrames)
    : FxNugget(FxNuggetKind.LightPulse);

/// <summary><c>ViewShakeFXNugget</c> (default <c>SHAKE_NORMAL</c>).</summary>
public sealed record ViewShakeFxNugget(ViewShakeType Type) : FxNugget(FxNuggetKind.ViewShake);

/// <summary><c>TerrainScorchFXNugget</c> (default RANDOM, radius 0).</summary>
public sealed record TerrainScorchFxNugget(ScorchType Type, float Radius) : FxNugget(FxNuggetKind.TerrainScorch);

/// <summary><c>ParticleSystemFXNugget</c>; <see cref="InitialDelay"/> is milliseconds and a negative sample keeps the template's delay.</summary>
public sealed record ParticleSystemFxNugget(
    string Name, int Count, Vector3 Offset, FxRandomVariable Radius, FxRandomVariable Height, FxRandomVariable InitialDelay,
    float RotateX, float RotateY, float RotateZ, bool OrientToObject, bool Ricochet, bool AttachToObject, bool CreateAtGroundHeight,
    bool UseCallersRadius) : FxNugget(FxNuggetKind.ParticleSystem);

/// <summary><c>FXListAtBonePosFXNugget</c>: runs <see cref="FxListName"/> at every bone <c>BoneName</c>, <c>BoneName01</c>, ...</summary>
public sealed record FxListAtBonePosFxNugget(string FxListName, string BoneName, bool OrientToBone) : FxNugget(FxNuggetKind.FxListAtBonePos);

/// <summary>A parsed <c>FXList</c> definition: its nuggets in INI order.</summary>
public sealed record FxList(string Name, ImmutableArray<FxNugget> Nuggets)
{
    internal static readonly string[] ShakeNames = ["SUBTLE", "NORMAL", "STRONG", "SEVERE", "CINE_EXTREME", "CINE_INSANE"];
    internal static readonly string[] ScorchNames = ["SCORCH_1", "SCORCH_2", "SCORCH_3", "SCORCH_4", "SHADOW_SCORCH", "RANDOM"];

    internal static FxNugget? ParseNugget(string listName, DefinitionSection section, FxDiagnosticBag diagnostics)
    {
        var type = FxValueReader.Tokens(section.Declaration).FirstOrDefault() ?? string.Empty;
        var read = new FxValueReader(diagnostics, $"FXList {listName}/{type}@{section.Location.Line}");
        if (section.Children.Length > 0)
            diagnostics.Add(FxDiagnosticCodes.UnknownNugget, read.Subject, $"{section.Children.Length} nested block(s) inside a nugget.");
        switch (type.ToLowerInvariant())
        {
            case "sound":
            {
                var name = string.Empty;
                foreach (var field in section.Fields)
                    if (Is(field, "Name")) name = read.Name(field.Name, field.Value.OriginalText) ?? string.Empty;
                    else Unknown(diagnostics, read, field);
                return new SoundFxNugget(name);
            }
            case "tracer":
            {
                // TracerFXNugget::TracerFXNugget defaults.
                string tracer = "GenericTracer", bone = string.Empty;
                float speed = 0f, decayAt = 1f, length = 10f, width = 1f, probability = 1f;
                var color = Vector3.One;
                foreach (var field in section.Fields)
                {
                    var (key, text) = (field.Name, field.Value.OriginalText);
                    if (Is(field, "TracerName")) tracer = read.Name(key, text) ?? tracer;
                    else if (Is(field, "BoneName")) bone = read.Name(key, text) ?? bone;
                    else if (Is(field, "Speed")) speed = read.VelocityPerFrame(key, text, 0f);
                    else if (Is(field, "DecayAt")) decayAt = read.Real(key, text, decayAt);
                    else if (Is(field, "Length")) length = read.Real(key, text, length);
                    else if (Is(field, "Width")) width = read.Real(key, text, width);
                    else if (Is(field, "Color")) color = read.RgbColor(key, text);
                    else if (Is(field, "Probability")) probability = read.Real(key, text, probability);
                    else Unknown(diagnostics, read, field);
                }
                return new TracerFxNugget(tracer, bone, speed, decayAt, length, width, color, probability);
            }
            case "rayeffect":
            {
                var name = string.Empty;
                Vector3 primary = Vector3.Zero, secondary = Vector3.Zero;
                foreach (var field in section.Fields)
                {
                    var (key, text) = (field.Name, field.Value.OriginalText);
                    if (Is(field, "Name")) name = read.Name(key, text) ?? name;
                    else if (Is(field, "PrimaryOffset")) primary = read.Coord3D(key, text);
                    else if (Is(field, "SecondaryOffset")) secondary = read.Coord3D(key, text);
                    else Unknown(diagnostics, read, field);
                }
                return new RayEffectFxNugget(name, primary, secondary);
            }
            case "lightpulse":
            {
                var color = Vector3.Zero;
                float radius = 0f, percent = 0f;
                uint increase = 0, decrease = 0;
                foreach (var field in section.Fields)
                {
                    var (key, text) = (field.Name, field.Value.OriginalText);
                    if (Is(field, "Color")) color = read.RgbColor(key, text);
                    else if (Is(field, "Radius")) radius = read.Real(key, text, 0f);
                    else if (Is(field, "RadiusAsPercentOfObjectSize")) percent = read.Percent(key, text);
                    else if (Is(field, "IncreaseTime")) increase = read.DurationFrames(key, text, 0);
                    else if (Is(field, "DecreaseTime")) decrease = read.DurationFrames(key, text, 0);
                    else Unknown(diagnostics, read, field);
                }
                return new LightPulseFxNugget(color, radius, percent, increase, decrease);
            }
            case "viewshake":
            {
                var shake = ViewShakeType.Normal;
                foreach (var field in section.Fields)
                    if (Is(field, "Type"))
                    {
                        var index = read.Index(field.Name, field.Value.OriginalText, ShakeNames, "View::CameraShakeType");
                        if (index >= 0) shake = (ViewShakeType)index;
                    }
                    else Unknown(diagnostics, read, field);
                return new ViewShakeFxNugget(shake);
            }
            case "terrainscorch":
            {
                var scorch = ScorchType.Random;
                var radius = 0f;
                foreach (var field in section.Fields)
                {
                    var (key, text) = (field.Name, field.Value.OriginalText);
                    if (Is(field, "Type"))
                    {
                        var index = read.Index(key, text, ScorchNames, "Scorches");
                        if (index >= 0) scorch = index == 5 ? ScorchType.Random : (ScorchType)index;
                    }
                    else if (Is(field, "Radius")) radius = read.Real(key, text, 0f);
                    else Unknown(diagnostics, read, field);
                }
                return new TerrainScorchFxNugget(scorch, radius);
            }
            case "particlesystem":
            {
                // ParticleSystemFXNugget::ParticleSystemFXNugget defaults (m_ricochet is left uninitialised there; pooled memory is zeroed).
                var name = string.Empty;
                var count = 1;
                var offset = Vector3.Zero;
                FxRandomVariable radius = FxRandomVariable.Zero, height = FxRandomVariable.Zero, delay = FxRandomVariable.Constant(-1f);
                float rotateX = 0f, rotateY = 0f, rotateZ = 0f;
                bool orient = false, ricochet = false, attach = false, ground = false, callersRadius = false;
                foreach (var field in section.Fields)
                {
                    var (key, text) = (field.Name, field.Value.OriginalText);
                    if (Is(field, "Name")) name = read.Name(key, text) ?? name;
                    else if (Is(field, "Count")) count = read.Int(key, text, count);
                    else if (Is(field, "Offset")) offset = read.Coord3D(key, text);
                    else if (Is(field, "Radius")) radius = read.RandomVariable(key, text);
                    else if (Is(field, "Height")) height = read.RandomVariable(key, text);
                    else if (Is(field, "InitialDelay")) delay = read.RandomVariable(key, text);
                    else if (Is(field, "RotateX")) rotateX = read.AngleRadians(key, text);
                    else if (Is(field, "RotateY")) rotateY = read.AngleRadians(key, text);
                    else if (Is(field, "RotateZ")) rotateZ = read.AngleRadians(key, text);
                    else if (Is(field, "OrientToObject")) orient = read.Bool(key, text, orient);
                    else if (Is(field, "Ricochet")) ricochet = read.Bool(key, text, ricochet);
                    else if (Is(field, "AttachToObject")) attach = read.Bool(key, text, attach);
                    else if (Is(field, "CreateAtGroundHeight")) ground = read.Bool(key, text, ground);
                    else if (Is(field, "UseCallersRadius")) callersRadius = read.Bool(key, text, callersRadius);
                    else Unknown(diagnostics, read, field);
                }
                return new ParticleSystemFxNugget(name, count, offset, radius, height, delay, rotateX, rotateY, rotateZ, orient, ricochet, attach,
                    ground, callersRadius);
            }
            case "fxlistatbonepos":
            {
                string fx = string.Empty, bone = string.Empty;
                var orientToBone = true;
                foreach (var field in section.Fields)
                {
                    var (key, text) = (field.Name, field.Value.OriginalText);
                    if (Is(field, "FX")) fx = read.Name(key, text) ?? fx;
                    else if (Is(field, "BoneName")) bone = read.Name(key, text) ?? bone;
                    else if (Is(field, "OrientToBone")) orientToBone = read.Bool(key, text, orientToBone);
                    else Unknown(diagnostics, read, field);
                }
                return new FxListAtBonePosFxNugget(fx, bone, orientToBone);
            }
            default:
                diagnostics.Add(FxDiagnosticCodes.UnknownNugget, read.Subject, $"'{type}' is not part of FXList.cpp TheFXListFieldParse.");
                return null;
        }
    }

    private static bool Is(DefinitionField field, string key) => field.Name.Equals(key, StringComparison.OrdinalIgnoreCase);

    private static void Unknown(FxDiagnosticBag diagnostics, FxValueReader read, DefinitionField field)
        => diagnostics.Add(FxDiagnosticCodes.UnknownKey, read.Subject, $"'{field.Name}' is not a field of this nugget.");
}
