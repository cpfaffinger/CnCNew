using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Data.Definitions;

namespace CnCNew.Presentation.Terrain;

/// <summary>One global terrain light (<c>GlobalData::m_terrainLighting[timeOfDay][i]</c>); colours are unit RGB, <see cref="Position"/> the light position vector.</summary>
public sealed record TerrainLight(Vector3 Ambient, Vector3 Diffuse, Vector3 Position);

/// <summary>
/// Static terrain vertex lighting (<c>BaseHeightMapRenderObjClass::doTheLight</c>): the ambient of the first light plus, for every global
/// light, <c>clamp(dot(-LightPos, normal), 0, 1) * Diffuse</c>, clamped to [0, 1]. Source: the <c>TerrainLighting&lt;TimeOfDay&gt;*</c> keys of
/// <c>GameData.ini</c> for the configured <c>TimeOfDay</c>. A map's own <c>GlobalLighting</c> chunk would override them; the map decoder does
/// not decode that chunk yet, so the GameData lighting is used for every map (documented limit).
/// </summary>
public sealed record TerrainLighting(string TimeOfDay, ImmutableArray<TerrainLight> Lights)
{
    /// <summary><c>GlobalData.cpp</c>: <c>m_numGlobalLights = 3</c> unless <c>NumberGlobalLights</c> is given.</summary>
    public const int EngineDefaultGlobalLights = 3;

    public static TerrainLighting FromDefinitions(CompiledDefinitionGraph graph) => Read(graph, "TerrainLighting");

    /// <summary>
    /// The lights of objects (<c>TerrainObjectsLighting&lt;TimeOfDay&gt;*</c> keys, <c>GlobalData::m_terrainObjectsLighting</c>): what <c>RTS3DScene</c> hands to the W3D renderer for
    /// every model, with its own ambient and the same light directions. Only the diffuse of the second and third afternoon lights differs from the terrain.
    /// </summary>
    public static TerrainLighting ObjectsFromDefinitions(CompiledDefinitionGraph graph) => Read(graph, "TerrainObjectsLighting");

    private static TerrainLighting Read(CompiledDefinitionGraph graph, string key)
    {
        var gameData = WorldDefinitionReader.GameData(graph);
        var timeOfDay = WorldDefinitionReader.Text(gameData, "TimeOfDay");
        var suffix = timeOfDay.ToUpperInvariant() switch
        {
            "MORNING" => "Morning",
            "AFTERNOON" => "Afternoon",
            "EVENING" => "Evening",
            "NIGHT" => "Night",
            _ => throw new InvalidDataException($"GameData TimeOfDay '{timeOfDay}' is not MORNING, AFTERNOON, EVENING or NIGHT."),
        };
        var count = WorldDefinitionReader.Has(gameData, "NumberGlobalLights")
            ? WorldDefinitionReader.Integer(gameData, "NumberGlobalLights")
            : EngineDefaultGlobalLights;
        if (count is < 1 or > 3) throw new InvalidDataException($"GameData NumberGlobalLights {count} is outside 1..3.");
        var lights = ImmutableArray.CreateBuilder<TerrainLight>(count);
        for (var index = 0; index < count; index++)
        {
            var keyed = key + suffix;
            var number = index == 0 ? string.Empty : (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);
            lights.Add(new TerrainLight(
                WorldDefinitionReader.UnitColor(gameData, keyed + "Ambient" + number),
                WorldDefinitionReader.UnitColor(gameData, keyed + "Diffuse" + number),
                WorldDefinitionReader.Vector(gameData, keyed + "LightPos" + number)));
        }
        return new TerrainLighting(suffix.ToUpperInvariant(), lights.MoveToImmutable());
    }

    /// <summary>Lit colour for a unit normal: <c>doTheLight</c> without dynamic lights.</summary>
    public Vector3 Shade(Vector3 normal)
    {
        var shade = Lights.IsDefaultOrEmpty ? Vector3.Zero : Lights[0].Ambient;
        foreach (var light in Lights)
        {
            var amount = Math.Clamp(Vector3.Dot(-light.Position, normal), 0f, 1f);
            shade += amount * light.Diffuse;
        }
        return Vector3.Clamp(shade, Vector3.Zero, Vector3.One);
    }
}
