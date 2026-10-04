using System.Collections.Immutable;
using CnCNew.Data.Definitions;

namespace CnCNew.Presentation.Terrain;

/// <summary>
/// One <c>Road</c> block of <c>Roads.ini</c> (<c>TerrainRoadType</c>): the texture file, the world width of the road
/// (<c>RoadWidth</c>) and the fraction of the texture's strip that the road itself covers (<c>RoadWidthInTexture</c>). <see cref="Id"/> is the
/// ordinal of the template in name order, starting at 1 (the reference numbers templates in INI order; the id only groups segments of one type).
/// </summary>
public sealed record RoadTemplate(string Name, int Id, string Texture, float Width, float WidthInTexture);

/// <summary>
/// The road templates of the definition graph plus the <c>GameData</c> segment budget. Template names compare exactly like
/// <c>TerrainRoadCollection::findRoad</c> (<c>AsciiString::operator==</c>, case sensitive); a map object that names no template is not drawn
/// (the reference silently draws it with the first road type at width 8 — that guess is not reproduced, the object is counted instead).
/// </summary>
public sealed class RoadCatalog
{
    private readonly ImmutableSortedDictionary<string, RoadTemplate> _templates;
    private readonly ImmutableArray<RoadTemplate> _ordered;

    /// <param name="roads">The templates in INI definition order (the order decides the ids).</param>
    public RoadCatalog(IEnumerable<(string Name, string Texture, float Width, float WidthInTexture)> roads, int maxSegments)
    {
        if (maxSegments < 1) throw new ArgumentOutOfRangeException(nameof(maxSegments), "The road segment budget must be positive.");
        var builder = ImmutableSortedDictionary.CreateBuilder<string, RoadTemplate>(StringComparer.Ordinal);
        var ordered = ImmutableArray.CreateBuilder<RoadTemplate>();
        foreach (var (name, texture, width, widthInTexture) in roads)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(texture))
                throw new ArgumentException("A road template needs a name and a texture file.", nameof(roads));
            if (!(width > 0f) || !(widthInTexture > 0f))
                throw new ArgumentException($"Road '{name}' needs a positive RoadWidth and RoadWidthInTexture.", nameof(roads));
            var template = new RoadTemplate(name, builder.Count + 1, texture, width, widthInTexture);
            if (!builder.TryAdd(name, template)) throw new ArgumentException($"Road '{name}' is defined twice.", nameof(roads));
            ordered.Add(template);
        }
        _templates = builder.ToImmutable();
        _ordered = ordered.ToImmutable();
        MaxSegments = maxSegments;
    }

    /// <summary><c>GameData</c> <c>MaxRoadSegments</c>: the reference stops adding segments (map objects, then inserted curves, tees and joins) at this count.</summary>
    public int MaxSegments { get; }

    public int Count => _templates.Count;

    /// <summary>The templates in id (INI definition) order.</summary>
    public ImmutableArray<RoadTemplate> Templates => _ordered;

    public bool TryGet(string name, out RoadTemplate template)
    {
        if (_templates.TryGetValue(name, out var found))
        {
            template = found;
            return true;
        }
        template = null!;
        return false;
    }

    public RoadTemplate? ById(int id) => id >= 1 && id <= _ordered.Length ? _ordered[id - 1] : null;

    /// <summary>Reads the <c>Road</c> blocks (compatibility definitions) and <c>GameData.MaxRoadSegments</c>; a missing or mistyped key throws.</summary>
    public static RoadCatalog FromDefinitions(CompiledDefinitionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        var roads = new List<(string, string, float, float)>();
        // The graph lists definitions in canonical (name) order; the reference numbers the roads in the order the INI files declare them.
        var declared = graph.Definitions
            .Where(definition => definition.Id.Kind == DefinitionKind.Compatibility && definition.Id.Name.StartsWith("road/", StringComparison.Ordinal))
            .OrderBy(definition => definition.Location.Source, StringComparer.Ordinal)
            .ThenBy(definition => definition.Location.Line);
        foreach (var definition in declared)
            roads.Add((definition.OriginalName, WorldDefinitionReader.Text(definition, "Texture"),
                WorldDefinitionReader.Real(definition, "RoadWidth"), WorldDefinitionReader.Real(definition, "RoadWidthInTexture")));
        if (roads.Count == 0) throw new InvalidDataException("The definition graph has no Road blocks.");
        return new RoadCatalog(roads, WorldDefinitionReader.Integer(WorldDefinitionReader.GameData(graph), "MaxRoadSegments"));
    }
}
