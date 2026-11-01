using System.Numerics;
using CnCNew.Content;
using CnCNew.W3D;

namespace CnCNew.Game.Data;

/// <summary>
/// The rest-pose bones of the draw models, for the presentation's attached effects and bone FX (<c>Object::getPristineBonePositions</c>: the model's base pose with an identity
/// root). A model is read the first time a bone of it is asked for and kept; a model that cannot be loaded answers no bone (the object layer counts it as missing). Presentation
/// only: nothing here reaches the simulation or a fingerprint.
/// </summary>
public sealed class ModelBoneTable(ContentFileSystem content)
{
    private readonly W3dAssetLibrary _library = new(path => content.Exists(path) ? content.OpenRead(path) : null);
    private readonly Dictionary<string, W3dSkeleton?> _skeletons = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, W3dPose> _poses = new(StringComparer.OrdinalIgnoreCase);

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
