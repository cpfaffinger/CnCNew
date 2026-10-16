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

    /// <summary>The model-space position of a bone (matched case-insensitively, the first bone of that name), or null when the model or the bone is not there.</summary>
    public Vector3? Find(string model, string bone)
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
        if (skeleton is null) return null;
        var index = skeleton.FindBone(bone);
        return index > 0 ? skeleton.RestWorldTransforms[index].Translation : null;
    }
}
