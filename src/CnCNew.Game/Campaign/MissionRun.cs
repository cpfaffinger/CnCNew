using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Game.Data;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;

namespace CnCNew.Game.Campaign;

/// <summary>What loading a mission needs from the content, without tying it to a file system: the compiled templates and the means to read a decoded map.</summary>
/// <param name="LoadMap">Decodes the map at a virtual path (<c>Maps/USA01/USA01.map</c>); throws <see cref="FileNotFoundException"/> when it does not exist.</param>
public sealed record MissionContext(TemplateCatalog Templates, string Fingerprint, Func<string, MapData> LoadMap)
{
    public static MissionContext From(GameData data) => new(data.Templates, data.Fingerprint, path =>
    {
        using var stream = data.Content.OpenRead(path);
        return SageMapDecoder.Decode(stream);
    });
}

/// <summary>A mission ready to be stepped: the map, its terrain and the running <see cref="MissionSession"/>. <see cref="Mission"/> is <see langword="null"/> for a map played on its own.</summary>
public sealed class MissionRun
{
    private MissionRun(string mapPath, CampaignMission? mission, MapData map, TerrainMap terrain, MissionSession session)
    {
        MapPath = mapPath;
        Mission = mission;
        Map = map;
        Terrain = terrain;
        Session = session;
    }

    public string MapPath { get; }
    public CampaignMission? Mission { get; }
    public MapData Map { get; }
    public TerrainMap Terrain { get; }
    public MissionSession Session { get; }

    public static MissionRun Load(MissionContext context, string mapPath, MissionOptions options, CampaignMission? mission = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var map = context.LoadMap(mapPath);
        var terrain = TerrainMap.Build(map, mapPath);
        return new MissionRun(mapPath, mission, map, terrain, MissionSession.Start(options, context.Templates, context.Fingerprint, terrain, map));
    }

    /// <summary>Rebuilds a run from a <see cref="MissionSnapshot"/> of the same map.</summary>
    public static MissionRun Restore(MissionContext context, string mapPath, ReadOnlySpan<byte> state, CampaignMission? mission = null)
    {
        ArgumentNullException.ThrowIfNull(context);
        var map = context.LoadMap(mapPath);
        var terrain = TerrainMap.Build(map, mapPath);
        return new MissionRun(mapPath, mission, map, terrain, MissionSnapshot.Restore(state, context.Templates, context.Fingerprint, terrain, map));
    }
}
