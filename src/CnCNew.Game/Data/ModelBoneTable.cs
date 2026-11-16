using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Content;
using CnCNew.Presentation.Fx;
using CnCNew.W3D;

namespace CnCNew.Game.Data;

/// <summary>
/// The rest-pose bones of the draw models, for the presentation's attached effects and bone FX (<c>Object::getPristineBonePositions</c>: the model's base pose with an identity
/// root), and the W3D particle emitters that some models are. A model is read the first time a bone of it is asked for and kept; a model that cannot be loaded answers no bone
/// (the object layer counts it as missing). Presentation only: nothing here reaches the simulation or a fingerprint.
/// </summary>
public sealed class ModelBoneTable(ContentFileSystem content)
{
    private readonly W3dAssetLibrary _library = new(path => content.Exists(path) ? content.OpenRead(path) : null);
    private readonly Dictionary<string, W3dSkeleton?> _skeletons = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, W3dPose> _poses = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, ModelEmitterSpec?> _emitters = new(StringComparer.OrdinalIgnoreCase);
    private readonly SortedSet<string> _emitterProblems = new(StringComparer.Ordinal);

    /// <summary>Emitter models this table could not run, each with its reason (a line render mode, a shader no particle blend matches): counted, never drawn as something else.</summary>
    public IReadOnlyCollection<string> EmitterProblems => _emitterProblems;

    /// <summary>
    /// The W3D particle emitter a model is (<see cref="W3dModel.Emitter"/>: a file of one emitter alone, the hydrant's water spray <c>EXHydrant</c>), as the presentation runs it; null for a model
    /// of meshes, one that cannot be loaded, and an emitter this runtime cannot draw (named in <see cref="EmitterProblems"/>).
    /// </summary>
    public ModelEmitterSpec? FindEmitter(string model)
    {
        if (_emitters.TryGetValue(model, out var spec)) return spec;
        W3dEmitter? emitter;
        try
        {
            emitter = _library.GetModel(model).Emitter;
        }
        catch (Exception exception) when (exception is W3dMissingAssetException or InvalidDataException or IOException)
        {
            emitter = null;
        }
        spec = emitter is null ? null : ToSpec(model, emitter, _emitterProblems);
        _emitters[model] = spec;
        return spec;
    }

    /// <summary>
    /// The emitter as <see cref="ModelEmitterWorld"/> runs it. The shader becomes the particle blend the original's presets of it are (<c>_PresetAdditiveSpriteShader</c> one and one,
    /// <c>_PresetAlphaSpriteShader</c> source alpha and its inverse, <c>_PresetATestSpriteShader</c> one and zero with the alpha test, <c>_PresetMultiplicativeSpriteShader</c> zero and
    /// source colour); any other blend, a version 1 emitter (whose shader the texture's alpha decides) and the line render modes are not run and named in <paramref name="problems"/>.
    /// </summary>
    public static ModelEmitterSpec? ToSpec(string model, W3dEmitter emitter, ISet<string> problems)
    {
        ArgumentNullException.ThrowIfNull(emitter);
        ArgumentNullException.ThrowIfNull(problems);
        if (emitter.RenderMode is not (W3dEmitterRenderMode.QuadParticles or W3dEmitterRenderMode.TriangleParticles))
        {
            problems.Add($"{model}: render mode {emitter.RenderMode} (lines) is not drawn");
            return null;
        }
        ParticleShader? shader = emitter.Shader switch
        {
            null => null,
            { SourceBlend: W3dSourceBlend.One, DestinationBlend: W3dDestinationBlend.One } => ParticleShader.Additive,
            { SourceBlend: W3dSourceBlend.SourceAlpha, DestinationBlend: W3dDestinationBlend.OneMinusSourceAlpha } => ParticleShader.Alpha,
            { SourceBlend: W3dSourceBlend.One, DestinationBlend: W3dDestinationBlend.Zero, AlphaTestEnabled: true } => ParticleShader.AlphaTest,
            { SourceBlend: W3dSourceBlend.Zero, DestinationBlend: W3dDestinationBlend.SourceColor } => ParticleShader.Multiply,
            _ => null,
        };
        if (shader is not { } blend)
        {
            problems.Add(emitter.Shader is { } unmatched
                ? $"{model}: the shader's blend (source {unmatched.SourceBlend}, destination {unmatched.DestinationBlend}, alpha test {unmatched.AlphaTestEnabled}) matches no particle blend"
                : $"{model}: a version 1 emitter takes its shader from the texture's alpha channel");
            return null;
        }
        var notReproduced = ImmutableArray.CreateBuilder<string>();
        if (emitter.FrameMode != W3dEmitterFrameMode.Grid1x1) notReproduced.Add($"texture frames {emitter.FrameMode} (the whole texture is drawn)");
        if (emitter.Frames.Keys.Length > 0 || emitter.Frames.Start != 0f || emitter.Frames.Random != 0f) notReproduced.Add("frame keyframes");
        if (emitter.RenderMode == W3dEmitterRenderMode.TriangleParticles) notReproduced.Add("triangle particles (drawn as quads)");
        static EmitterVolume Volume(W3dVolumeRandomizer volume) => new((EmitterVolumeKind)(int)volume.Kind, volume.Value1, volume.Value2, volume.Value3);
        static EmitterScalarKeys Scalars(W3dEmitterProperty<float> keys) => new(keys.Start, keys.Random, [.. keys.Keys.Select(key => (key.Time, key.Value))]);
        return new ModelEmitterSpec(
            emitter.Name,
            emitter.TextureName,
            blend,
            emitter.EmissionRate,
            emitter.BurstSize,
            (int)emitter.MaxEmissions,
            emitter.Lifetime,
            Volume(emitter.CreationVolume),
            Volume(emitter.VelocityRandom),
            emitter.Velocity,
            emitter.OutwardVelocity,
            emitter.VelocityInherit,
            emitter.Acceleration,
            new EmitterColorKeys(emitter.Colors.Start, emitter.Colors.Random, [.. emitter.Colors.Keys.Select(key => (key.Time, key.Value))]),
            Scalars(emitter.Opacities),
            Scalars(emitter.Sizes),
            Scalars(emitter.Rotations),
            emitter.InitialOrientationRandom) { NotReproduced = notReproduced.ToImmutable() };
    }

    /// <summary>The model-space position of a bone (matched case-insensitively, the first bone of that name), or null when the model or the bone is not there.</summary>
    public Vector3? Find(string model, string bone)
    {
        var skeleton = Skeleton(model);
        if (skeleton is null) return null;
        var index = skeleton.FindBone(bone);
        return index > 0 ? skeleton.RestWorldTransforms[index].Translation : null;
    }

    /// <summary>
    /// The model-space matrix of a bone with the turrets turned as they are drawn (<c>Drawable::getCurrentWorldspaceClientBonePositions</c>: the bones of the running model, not the
    /// pristine ones): a muzzle on a turret points where the turret points.
    /// </summary>
    public Matrix4x4? FindPosed(string model, string bone, CnCNew.Presentation.Fx.TurretPose turrets)
    {
        var skeleton = Skeleton(model);
        if (skeleton is null) return null;
        var index = skeleton.FindBone(bone);
        if (index <= 0) return null;
        if (!_poses.TryGetValue(model, out var pose)) _poses[model] = pose = new W3dPose(skeleton);
        pose.ClearBoneOverrides();
        void Yaw(string? name, float? angle) { if (name is not null && angle is { } radians && skeleton.FindBone(name) is > 0 and var turret) pose.SetTurretYaw(turret, radians); }
        void Pitch(string? name, float? angle) { if (name is not null && angle is { } radians && skeleton.FindBone(name) is > 0 and var turret) pose.SetTurretPitch(turret, radians); }
        Yaw(turrets.YawBone, turrets.Yaw);
        Pitch(turrets.PitchBone, turrets.Pitch);
        Yaw(turrets.AltYawBone, turrets.AltYaw);
        Pitch(turrets.AltPitchBone, turrets.AltPitch);
        pose.SetRestPose(Matrix4x4.Identity);
        return pose.GetBoneWorldTransform(index);
    }

    /// <summary>
    /// The model-space matrix of a bone under the last frame of an animation (<c>getCurrentClientBonePositions</c> of a drawable that shows its state with <c>START_FRAME_LAST</c>: the particle
    /// cannon's dish deployed). Null when the model, the animation or the bone is not there.
    /// </summary>
    public Matrix4x4? FindAtFinalFrame(string model, string animation, string bone)
    {
        var skeleton = Skeleton(model);
        if (skeleton is null) return null;
        var index = skeleton.FindBone(bone);
        if (index <= 0) return null;
        W3dAnimationBinding? binding;
        try
        {
            binding = animation.Contains('.', StringComparison.Ordinal) ? _library.GetAnimation(animation) : null;
        }
        catch (Exception exception) when (exception is W3dMissingAssetException or InvalidDataException or IOException)
        {
            binding = null;
        }
        if (binding is null || binding.PivotCount > skeleton.BoneCount) return null;
        var pose = new W3dPose(skeleton);
        pose.Evaluate(binding, binding.Animation.FrameCount - 1, Matrix4x4.Identity);
        return pose.GetBoneWorldTransform(index);
    }

    private readonly Dictionary<string, double?> _animationSeconds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The seconds a W3D animation (<c>Hierarchy.Name</c>) takes from its first frame to its last at its own frame rate (<c>isAnimationComplete</c> of a clip played once), or null when it
    /// cannot be loaded.
    /// </summary>
    public double? AnimationSeconds(string animation)
    {
        if (_animationSeconds.TryGetValue(animation, out var known)) return known;
        double? seconds;
        try
        {
            seconds = animation.Contains('.', StringComparison.Ordinal) && _library.GetAnimation(animation) is { } binding && binding.Animation.FrameRate > 0
                ? Math.Max(0, binding.Animation.FrameCount - 1) / (double)binding.Animation.FrameRate
                : null;
        }
        catch (Exception exception) when (exception is W3dMissingAssetException or InvalidDataException or IOException)
        {
            seconds = null;
        }
        _animationSeconds[animation] = seconds;
        return seconds;
    }

    private W3dSkeleton? Skeleton(string model)
    {
        if (!_skeletons.TryGetValue(model, out var skeleton))
        {
            try
            {
                skeleton = _library.GetModel(model).Skeleton;
            }
            catch (Exception exception) when (exception is W3dMissingAssetException or InvalidDataException or IOException)
            {
                skeleton = null;
            }
            _skeletons[model] = skeleton;
        }
        return skeleton;
    }
}
