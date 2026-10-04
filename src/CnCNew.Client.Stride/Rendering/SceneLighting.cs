using CnCNew.Presentation.Terrain;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Rendering.Colors;
using Stride.Rendering.Lights;

namespace CnCNew.Client.Stride.Rendering;

/// <summary>
/// The light W3D models receive in the original (<c>RTS3DScene</c> with <c>GlobalData::m_terrainObjectsLighting</c>): one ambient light plus up to three directional lights. The
/// game data's <c>LightPos</c> is the direction the light travels (the terrain uses <c>dot(-LightPos, normal)</c>); Stride's directional light travels along its entity's
/// <c>-Z</c> axis (<c>LightProcessor.DefaultDirection</c>), so each entity is rotated from <c>-Z</c> onto that direction. World space is Z up like the game data.
/// </summary>
internal static class SceneLighting
{
    public static void Install(Scene scene, TerrainLighting lighting)
    {
        if (lighting.Lights.IsDefaultOrEmpty) return;
        var ambient = lighting.Lights[0].Ambient;
        scene.Entities.Add(new Entity("Ambient light") { new LightComponent { Type = new LightAmbient { Color = new ColorRgbProvider(ToColor(ambient)) } } });
        for (var index = 0; index < lighting.Lights.Length; index++)
        {
            var light = lighting.Lights[index];
            if (light.Diffuse == System.Numerics.Vector3.Zero) continue;
            var sun = new Entity($"Sun {index + 1}") { new LightComponent { Type = new LightDirectional { Color = new ColorRgbProvider(ToColor(light.Diffuse)) } } };
            sun.Transform.Rotation = RotationFromForward(new Vector3(light.Position.X, light.Position.Y, light.Position.Z));
            scene.Entities.Add(sun);
        }
    }

    /// <summary>The rotation that turns Stride's light direction <c>(0, 0, -1)</c> onto <paramref name="travel"/>.</summary>
    internal static Quaternion RotationFromForward(Vector3 travel)
    {
        travel.Normalize();
        var from = new Vector3(0, 0, -1);
        var dot = Vector3.Dot(from, travel);
        if (dot > 0.99999f) return Quaternion.Identity;
        // Opposite directions: any axis perpendicular to -Z turns it around.
        if (dot < -0.99999f) return Quaternion.RotationAxis(Vector3.UnitX, MathF.PI);
        var axis = Vector3.Normalize(Vector3.Cross(from, travel));
        return Quaternion.RotationAxis(axis, MathF.Acos(dot));
    }

    private static Color3 ToColor(System.Numerics.Vector3 unit) => new(unit.X, unit.Y, unit.Z);
}
