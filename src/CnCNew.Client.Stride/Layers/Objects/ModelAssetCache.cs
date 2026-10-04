using CnCNew.Client.Stride.Rendering;
using CnCNew.W3D;

namespace CnCNew.Client.Stride.Layers.Objects;

/// <summary>
/// Loads and caches one <see cref="ModelAsset"/> per W3D model name over a <see cref="W3dAssetLibrary"/> bound to
/// <see cref="RenderServices.Content"/>, so every instance of the same model shares its GPU resources. A model that fails to load
/// (missing file, missing hierarchy) is counted and logged exactly once and cached as "missing" so later lookups do not retry or spam.
/// </summary>
internal sealed class ModelAssetCache : IDisposable
{
    private readonly RenderServices _services;
    private readonly W3dAssetLibrary _library;
    private readonly SharedIndexBuffer _indices;
    private readonly Dictionary<string, ModelAsset> _assets = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _missing = new(StringComparer.OrdinalIgnoreCase);

    public ModelAssetCache(RenderServices services)
    {
        _services = services;
        _library = new W3dAssetLibrary(path => services.Content.Exists(path) ? services.Content.OpenRead(path) : null);
        _indices = new SharedIndexBuffer(services.GraphicsDevice);
    }

    public int LoadedCount => _assets.Count;
    public int MissingCount => _missing.Count;

    public W3dAssetLibrary Library => _library;

    /// <summary>The identity index buffer shared by every batch this cache builds; also used by <see cref="PlaceholderBox"/>.</summary>
    public SharedIndexBuffer Indices => _indices;

    /// <summary>Resolves <paramref name="modelName"/> (a bare W3D model name, no directory or extension); null if it or its hierarchy is missing.</summary>
    public ModelAsset? Get(string modelName)
    {
        if (_assets.TryGetValue(modelName, out var cached)) return cached;
        if (_missing.Contains(modelName)) return null;

        W3dModel model;
        try
        {
            model = _library.GetModel(modelName);
        }
        catch (W3dMissingAssetException ex)
        {
            _missing.Add(modelName);
            Console.Error.WriteLine($"warning: object layer could not load model '{modelName}': {ex.Message}");
            return null;
        }
        catch (InvalidDataException ex)
        {
            // A file the W3D layer cannot turn into a model (MD_GLA03's 'cineexplbox' holds one collision box and no mesh): nothing to draw, but no reason to end the game either.
            _missing.Add(modelName);
            Console.Error.WriteLine($"warning: object layer cannot use model '{modelName}': {ex.Message}");
            return null;
        }

        var asset = ModelAsset.Build(modelName, model, _services, _indices);
        _assets[modelName] = asset;
        return asset;
    }

    /// <summary>Sum of <see cref="ModelAsset.CountGpuResourceProblems"/> over every loaded asset.</summary>
    public int CountGpuResourceProblems() => _assets.Values.Sum(asset => asset.CountGpuResourceProblems());

    public void Dispose() => _indices.Dispose();
}
