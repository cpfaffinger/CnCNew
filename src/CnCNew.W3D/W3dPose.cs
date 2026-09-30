using System.Numerics;

namespace CnCNew.W3D;

/// <summary>
/// Mutable per-instance pose of a skeleton: bone world matrices, bone visibility and procedural bone overrides
/// ("captured" bones). Re-implements HTreeClass::Base_Update/Anim_Update and PivotClass::Capture_Update. One pose per
/// drawable; the skeleton and animations are shared. Not thread-safe.
/// </summary>
public sealed class W3dPose
{
    private readonly Matrix4x4[] _world;
    private readonly bool[] _visible;
    private readonly Matrix4x4?[] _overrides;

    public W3dPose(W3dSkeleton skeleton)
    {
        Skeleton = skeleton ?? throw new ArgumentNullException(nameof(skeleton));
        _world = new Matrix4x4[skeleton.BoneCount];
        _visible = new bool[skeleton.BoneCount];
        _overrides = new Matrix4x4?[skeleton.BoneCount];
        SetRestPose(Matrix4x4.Identity);
    }

    public W3dSkeleton Skeleton { get; }

    /// <summary>Bone world matrices of the last evaluation (row-vector convention; bone 0 is the root transform).</summary>
    public ReadOnlySpan<Matrix4x4> BoneWorldTransforms => _world;

    public Matrix4x4 GetBoneWorldTransform(int bone) => _world[bone];

    /// <summary>Visibility of a bone from the animation's bit channels; bones without a channel are visible.</summary>
    public bool IsBoneVisible(int bone) => _visible[bone];

    /// <summary>
    /// Captures a bone: after the base pose and the animation delta, <paramref name="relative"/> is post-multiplied in the
    /// bone's own space (HTreeClass::Control_Bone without world-space translation) and the bone is forced visible.
    /// </summary>
    public void SetBoneOverride(int bone, Matrix4x4 relative)
    {
        ValidateOverrideBone(bone);
        _overrides[bone] = relative;
    }

    public void ClearBoneOverride(int bone)
    {
        ValidateOverrideBone(bone);
        _overrides[bone] = null;
    }

    public void ClearBoneOverrides() => Array.Clear(_overrides);

    public bool HasBoneOverride(int bone) => _overrides[bone] is not null;

    /// <summary>Turret yaw as W3DModelDraw::handleClientTurretPositioning: Rotate_Z(angle) on the turret bone.</summary>
    public void SetTurretYaw(int bone, float radians) => SetBoneOverride(bone, Matrix4x4.CreateRotationZ(radians));

    /// <summary>Turret pitch as W3DModelDraw::handleClientTurretPositioning: Rotate_Y(-pitch) on the pitch bone.</summary>
    public void SetTurretPitch(int bone, float radians) => SetBoneOverride(bone, Matrix4x4.CreateRotationY(-radians));

    /// <summary>Barrel recoil as W3DModelDraw::handleClientRecoil: Translate_X(-shift) on the recoil bone.</summary>
    public void SetRecoil(int bone, float shift) => SetBoneOverride(bone, Matrix4x4.CreateTranslation(-shift, 0, 0));

    /// <summary>Base pose without animation (HTreeClass::Base_Update).</summary>
    public void SetRestPose(Matrix4x4 root)
    {
        _world[0] = root;
        _visible[0] = true;
        var rest = Skeleton.RestTransforms;
        var parents = Skeleton.Parents;
        for (var bone = 1; bone < _world.Length; bone++)
        {
            var local = rest[bone];
            _visible[bone] = true;
            if (_overrides[bone] is { } capture)
            {
                local = capture * local;
            }

            _world[bone] = local * _world[parents[bone]];
        }
    }

    /// <summary>
    /// Evaluates the animation at a fractional frame (HTreeClass::Anim_Update): per bone
    /// <c>world = override * R(anim) * T(anim) * rest * parentWorld</c>. Bones at or beyond the animation's pivot count and
    /// bones without channels keep their rest pose; the root is <paramref name="root"/>.
    /// </summary>
    public void Evaluate(W3dAnimationBinding animation, float frame, Matrix4x4 root, W3dRawSampling rawSampling = W3dRawSampling.Interpolated)
    {
        ArgumentNullException.ThrowIfNull(animation);
        _world[0] = root;
        _visible[0] = true;
        var rest = Skeleton.RestTransforms;
        var parents = Skeleton.Parents;
        var animatedPivots = Math.Min(animation.PivotCount, _world.Length);
        for (var bone = 1; bone < _world.Length; bone++)
        {
            var local = rest[bone];
            var visible = true;
            if (bone < animatedPivots)
            {
                animation.Sample(bone, frame, rawSampling, out var translation, out var rotation, out visible);
                local = Matrix4x4.CreateFromQuaternion(rotation) * Matrix4x4.CreateTranslation(translation) * local;
            }

            if (_overrides[bone] is { } capture)
            {
                local = capture * local;
                visible = true;
            }

            _world[bone] = local * _world[parents[bone]];
            _visible[bone] = visible;
        }
    }

    private void ValidateOverrideBone(int bone)
    {
        // The original never captures the root: W3DModelDraw treats bone index 0 as "not found".
        if (bone <= 0 || bone >= _world.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(bone), bone, $"Bone must be in 1..{_world.Length - 1}.");
        }
    }
}
