namespace CnCNew.W3D;

/// <summary>
/// Loads and caches shared W3D assets through a caller-supplied resolver (virtual path to stream, null if missing), so
/// this assembly never touches the content or archive layers. Naming follows WW3DAssetManager: a hierarchy or model
/// <c>X</c> lives in <c>X.W3D</c>; an animation <c>HIER.ANIM</c> lives in <c>ANIM.W3D</c>. Unlike the original's
/// first-loaded-wins registry, a hierarchy name always resolves to the hierarchy of that name in <c>&lt;name&gt;.W3D</c>
/// (models prefer the hierarchy in their own file), which keeps results independent of load order. Thread-safe.
/// </summary>
public sealed class W3dAssetLibrary
{
    private readonly Func<string, Stream?> _resolver;
    private readonly string _directory;
    // Monitor-based on purpose: GetModel and GetAnimation re-enter through GetSkeleton.
    private readonly object _gate = new();
    private readonly Dictionary<string, W3dSkeleton?> _skeletons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, W3dModel> _models = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, W3dAnimationBinding?> _animations = new(StringComparer.OrdinalIgnoreCase);

    public W3dAssetLibrary(Func<string, Stream?> resolver, string directory = "Art/W3D")
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory.TrimEnd('/');
    }

    public string GetVirtualPath(string assetName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetName);
        return $"{_directory}/{assetName}.W3D";
    }

    /// <summary>Decodes <c>&lt;name&gt;.W3D</c> without caching; null if the resolver does not know the file.</summary>
    public W3dAssetFile? ReadFile(string assetName)
    {
        using var stream = _resolver(GetVirtualPath(assetName));
        return stream is null ? null : W3dAssetFile.Read(stream);
    }

    /// <summary>The hierarchy named <paramref name="hierarchyName"/> from <c>&lt;hierarchyName&gt;.W3D</c>, or null if missing.</summary>
    public W3dSkeleton? GetSkeleton(string hierarchyName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hierarchyName);
        lock (_gate)
        {
            if (!_skeletons.TryGetValue(hierarchyName, out var skeleton))
            {
                var hierarchy = ReadFile(hierarchyName)?.Hierarchies.FirstOrDefault(candidate =>
                    candidate.Name.Equals(hierarchyName, StringComparison.OrdinalIgnoreCase));
                skeleton = hierarchy is null ? null : W3dSkeleton.FromHierarchy(hierarchy);
                _skeletons.Add(hierarchyName, skeleton);
            }

            return skeleton;
        }
    }

    /// <summary>The model in <c>&lt;modelName&gt;.W3D</c>; throws <see cref="W3dMissingAssetException"/> if it or its hierarchy is missing.</summary>
    public W3dModel GetModel(string modelName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        lock (_gate)
        {
            if (!_models.TryGetValue(modelName, out var model))
            {
                var file = ReadFile(modelName)
                    ?? throw new W3dMissingAssetException($"Model file '{GetVirtualPath(modelName)}' was not found.", modelName);
                model = W3dModel.Create(modelName, file, GetSkeleton);
                _models.Add(modelName, model);
            }

            return model;
        }
    }

    /// <summary>
    /// The animation <c>HIER.ANIM</c> bound to the pivot count of hierarchy <c>HIER</c>, or null if the file, the
    /// animation or its hierarchy is missing (the original's load fails in all three cases).
    /// </summary>
    public W3dAnimationBinding? GetAnimation(string fullName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fullName);
        var dot = fullName.IndexOf('.');
        if (dot <= 0 || dot == fullName.Length - 1)
        {
            throw new ArgumentException($"Animation name '{fullName}' must have the form HIERARCHY.ANIMATION.", nameof(fullName));
        }

        lock (_gate)
        {
            if (!_animations.TryGetValue(fullName, out var binding))
            {
                var file = ReadFile(fullName[(dot + 1)..]);
                var animation = file?.Animations.FirstOrDefault(candidate =>
                    candidate.FullName.Equals(fullName, StringComparison.OrdinalIgnoreCase));
                var skeleton = animation is null ? null : ResolveAnimationSkeleton(file!, animation);
                binding = skeleton is null ? null : animation!.Bind(skeleton.BoneCount);
                _animations.Add(fullName, binding);
            }

            return binding;
        }
    }

    /// <summary>
    /// The hierarchy an animation binds to: the one of that name in the animation's own file (the original registers all
    /// hierarchies of a file when loading it), otherwise <c>&lt;HierarchyName&gt;.W3D</c>.
    /// </summary>
    public W3dSkeleton? ResolveAnimationSkeleton(W3dAssetFile file, W3dAnimation animation)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(animation);
        var own = file.Hierarchies.FirstOrDefault(hierarchy =>
            hierarchy.Name.Equals(animation.HierarchyName, StringComparison.OrdinalIgnoreCase));
        return own is not null ? W3dSkeleton.FromHierarchy(own) : GetSkeleton(animation.HierarchyName);
    }
}
