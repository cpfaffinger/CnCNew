using System.Globalization;
using CnCNew.Data.Definitions;

namespace CnCNew.Presentation.Terrain;

/// <summary>The <c>Weather</c> block (<c>WeatherSetting</c>): the snow of <c>W3DSnowManager</c>, which with another texture is also the rain of a few maps.</summary>
public sealed record SnowSettings(
    bool Enabled, string Texture, float BoxDimensions, float BoxDensity, float FrequencyScaleX, float FrequencyScaleY, float Amplitude, float Velocity,
    float PointSize, float MinPointSize, float MaxPointSize);

/// <summary>The five textures of the sky box (<c>WaterTransparencySetting</c>), in the order of the shipped model's names.</summary>
public sealed record SkyboxTextures(string North, string East, string South, string West, string Top)
{
    /// <summary>What <c>new_skybox.W3D</c> names, the defaults of <c>WaterTransparencySetting</c>.</summary>
    public static SkyboxTextures Default { get; } = new("TSMorningN.tga", "TSMorningE.tga", "TSMorningS.tga", "TSMorningW.tga", "TSMorningT.tga");

    /// <summary>The name to draw for a name of the model.</summary>
    public string Replace(string modelTexture)
    {
        if (modelTexture.Equals(Default.North, StringComparison.OrdinalIgnoreCase)) return North;
        if (modelTexture.Equals(Default.East, StringComparison.OrdinalIgnoreCase)) return East;
        if (modelTexture.Equals(Default.South, StringComparison.OrdinalIgnoreCase)) return South;
        if (modelTexture.Equals(Default.West, StringComparison.OrdinalIgnoreCase)) return West;
        return modelTexture.Equals(Default.Top, StringComparison.OrdinalIgnoreCase) ? Top : modelTexture;
    }
}

/// <summary>
/// The sky and the weather of one map: the base data with the map's own <c>map.ini</c> over it. The definition graph of a running game leaves map overlays out, and these two are all
/// the picture wants from one, so its two blocks are read here (<c>INI::parseWeatherDefinition</c> and <c>parseWaterTransparencyDefinition</c> with <c>INI_LOAD_CREATE_OVERRIDES</c>).
/// An unknown key of a <c>Weather</c> block is an error, as everywhere in the data.
/// </summary>
public sealed record MapEnvironment(SnowSettings Snow, SkyboxTextures Skybox, float SkyboxScale, float SkyboxZ, int BorderShroudLevel, int BorderClearLevel)
{
    public static MapEnvironment FromDefinitions(CompiledDefinitionGraph graph, string? mapIni)
    {
        var gameData = WorldDefinitionReader.GameData(graph);
        var weather = WorldDefinitionReader.Compatibility(graph, "Weather", "Weather");
        var snow = new SnowSettings(
            WorldDefinitionReader.Boolean(weather, "SnowEnabled"),
            WorldDefinitionReader.Text(weather, "SnowTexture"),
            WorldDefinitionReader.Real(weather, "SnowBoxDimensions"),
            WorldDefinitionReader.Real(weather, "SnowBoxDensity"),
            WorldDefinitionReader.Real(weather, "SnowFrequencyScaleX"),
            WorldDefinitionReader.Real(weather, "SnowFrequencyScaleY"),
            WorldDefinitionReader.Real(weather, "SnowAmplitude"),
            WorldDefinitionReader.Real(weather, "SnowVelocity"),
            WorldDefinitionReader.Real(weather, "SnowPointSize"),
            WorldDefinitionReader.Real(weather, "SnowMinPointSize"),
            WorldDefinitionReader.Real(weather, "SnowMaxPointSize"));
        var (mapSnow, sky) = mapIni is null ? (snow, SkyboxTextures.Default) : ApplyMapIni(mapIni, snow);
        return new MapEnvironment(mapSnow, sky, WorldDefinitionReader.Real(gameData, "SkyBoxScale"), WorldDefinitionReader.Real(gameData, "SkyBoxPositionZ"),
            WorldDefinitionReader.Integer(gameData, "ShroudAlpha"), WorldDefinitionReader.Integer(gameData, "ClearAlpha"));
    }

    /// <summary>The <c>Weather</c> and <c>WaterTransparency</c> blocks of a <c>map.ini</c> over the given snow. Everything else in the file is a matter of the simulation's data.</summary>
    public static (SnowSettings Snow, SkyboxTextures Sky) ApplyMapIni(string text, SnowSettings snow)
    {
        var sky = SkyboxTextures.Default;
        string? block = null;
        foreach (var raw in text.Split('\n'))
        {
            var comment = raw.IndexOf(';');
            var line = (comment >= 0 ? raw[..comment] : raw).Trim();
            if (line.Length == 0) continue;
            if (block is null)
            {
                if (line.Equals("Weather", StringComparison.OrdinalIgnoreCase) || line.Equals("WaterTransparency", StringComparison.OrdinalIgnoreCase)) block = line;
                continue;
            }

            if (line.Equals("End", StringComparison.OrdinalIgnoreCase))
            {
                block = null;
                continue;
            }

            var equals = line.IndexOf('=');
            if (equals <= 0) continue;
            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (block.Equals("Weather", StringComparison.OrdinalIgnoreCase)) snow = ApplyWeatherKey(snow, key, value);
            else sky = ApplySkyKey(sky, key, value);
        }

        return (snow, sky);
    }

    private static SnowSettings ApplyWeatherKey(SnowSettings snow, string key, string value) => key.ToUpperInvariant() switch
    {
        "SNOWENABLED" => snow with { Enabled = value.Equals("Yes", StringComparison.OrdinalIgnoreCase) },
        "SNOWTEXTURE" => snow with { Texture = value },
        "SNOWBOXDIMENSIONS" => snow with { BoxDimensions = Number(value) },
        "SNOWBOXDENSITY" => snow with { BoxDensity = Number(value) },
        "SNOWFREQUENCYSCALEX" => snow with { FrequencyScaleX = Number(value) },
        "SNOWFREQUENCYSCALEY" => snow with { FrequencyScaleY = Number(value) },
        "SNOWAMPLITUDE" => snow with { Amplitude = Number(value) },
        "SNOWVELOCITY" => snow with { Velocity = Number(value) },
        "SNOWPOINTSIZE" => snow with { PointSize = Number(value) },
        "SNOWMINPOINTSIZE" => snow with { MinPointSize = Number(value) },
        "SNOWMAXPOINTSIZE" => snow with { MaxPointSize = Number(value) },
        // Point sprites or their emulation: a Direct3D 8 matter, this renderer draws the same sprites either way.
        "SNOWPOINTSPRITES" or "SNOWQUADSIZE" => snow,
        _ => throw new InvalidDataException($"map.ini: unknown Weather key '{key}'."),
    };

    private static SkyboxTextures ApplySkyKey(SkyboxTextures sky, string key, string value) => key.ToUpperInvariant() switch
    {
        "SKYBOXTEXTUREN" => sky with { North = value },
        "SKYBOXTEXTUREE" => sky with { East = value },
        "SKYBOXTEXTURES" => sky with { South = value },
        "SKYBOXTEXTUREW" => sky with { West = value },
        "SKYBOXTEXTURET" => sky with { Top = value },
        _ => sky,
    };

    private static float Number(string value) => float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture);
}
