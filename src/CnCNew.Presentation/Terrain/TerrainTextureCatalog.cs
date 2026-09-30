using System.Collections.Immutable;
using CnCNew.Data.Definitions;

namespace CnCNew.Presentation.Terrain;

/// <summary>
/// The <c>Terrain</c> blocks of <c>Terrain.ini</c> (<c>TerrainTypeCollection</c>): terrain type name to texture file. A map texture class
/// names a terrain type; <c>WorldHeightMap::readTexClass</c> opens <c>Art/Terrain/&lt;Texture&gt;</c>. Names compare case-insensitively
/// like every definition id.
/// </summary>
public sealed class TerrainTextureCatalog
{
    public const string TextureDirectory = "Art/Terrain/";

    private readonly ImmutableSortedDictionary<string, string> _textures;

    public TerrainTextureCatalog(IEnumerable<KeyValuePair<string, string>> terrainTextures)
    {
        var builder = ImmutableSortedDictionary.CreateBuilder<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, texture) in terrainTextures)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(texture))
                throw new ArgumentException("A terrain type needs a name and a texture file.", nameof(terrainTextures));
            if (!builder.TryAdd(name, texture)) throw new ArgumentException($"Terrain type '{name}' is defined twice.", nameof(terrainTextures));
        }
        _textures = builder.ToImmutable();
    }

    public int Count => _textures.Count;

    public IEnumerable<KeyValuePair<string, string>> Entries => _textures;

    public static TerrainTextureCatalog FromDefinitions(CompiledDefinitionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var entries = new List<KeyValuePair<string, string>>();
        foreach (var definition in graph.Definitions)
        {
            if (definition.Id.Kind != DefinitionKind.Compatibility || !definition.Id.Name.StartsWith("terrain/", StringComparison.Ordinal)) continue;
            entries.Add(new(definition.OriginalName, WorldDefinitionReader.Text(definition, "Texture")));
        }
        if (entries.Count == 0) throw new InvalidDataException("The definition graph has no Terrain blocks.");
        return new TerrainTextureCatalog(entries);
    }

    /// <summary>The virtual texture path of a terrain type, or false when <c>TheTerrainTypes->findTerrain</c> would not find it.</summary>
    public bool TryGetTexturePath(string terrainName, out string virtualPath)
    {
        if (_textures.TryGetValue(terrainName, out var file))
        {
            virtualPath = TextureDirectory + file;
            return true;
        }
        virtualPath = string.Empty;
        return false;
    }
}
