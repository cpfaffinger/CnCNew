using System.Numerics;
using CnCNew.Presentation.Terrain;

namespace CnCNew.Presentation.Shadows;

/// <summary>
/// The sun of the shadows: <c>W3DShadowManager::setTimeOfDay</c> puts it 10,000 units away against the first light of the objects, and the volumes of <c>W3DVolumetricShadow</c> run from
/// there. At that distance the rays of one object are parallel, so here the sun is a direction. <c>ShadowSizeX</c> of a volume is not a size but the lowest elevation of the light in degrees
/// (<c>W3DVolumetricShadowManager::addShadow</c>), which limits the length of the shadows of tall things.
/// </summary>
public static class ShadowSun
{
    /// <summary><c>SHADOW_EXTRUSION_BUFFER</c>: how far a volume reaches under what it should reach.</summary>
    public const float ExtrusionBuffer = 0.1f;

    /// <summary><c>AIRBORNE_UNIT_GROUND_DELTA</c>: above this a thing flies, and its volume reaches down to the lowest point of the map.</summary>
    public const float AirborneDelta = 2f;

    /// <summary><c>MAX_SHADOW_EXTRUSION_UNDER_OBJECT_BEFORE_CLAMP</c>.</summary>
    public const float MaximumDrop = 5f;

    /// <summary><c>SHADOW_SAMPLING_INTERVAL</c>: two cells of the map.</summary>
    public const float SamplingInterval = 2f * TerrainHeightSampler.CellSize;

    /// <summary><c>OVERHANGING_OBJECT_CLAMP_ANGLE</c>, 80 degrees.</summary>
    public const float OverhangAngle = 80f / 180f * MathF.PI;

    /// <summary><c>m_shadowColor</c> of <c>W3DShadowManager</c> (0x7fa0a0a0): the blend multiplies by its colour, 160/255; the alpha is not used.</summary>
    public const float StencilBrightness = 160f / 255f;

    /// <summary>
    /// The colour the stencil shadows multiply the picture by: the map's own (<c>ParseLightingDataChunk</c> ends in <c>setShadowColor</c>, 0xAARRGGBB, red, green and blue each a
    /// factor of 255, the alpha unused), or <see cref="StencilBrightness"/> grey for a map that names none.
    /// </summary>
    public static Vector3 StencilColor(CnCNew.Data.World.MapData? map)
        => map?.Lighting?.ShadowColor is { } color
            ? new Vector3(((color >> 16) & 0xFF) / 255f, ((color >> 8) & 0xFF) / 255f, (color & 0xFF) / 255f)
            : new Vector3(StencilBrightness);

    /// <summary>The direction the light travels: the first light of the objects, or straight down without one.</summary>
    public static Vector3 Direction(TerrainLighting lighting)
    {
        if (lighting.Lights.IsDefaultOrEmpty) return -Vector3.UnitZ;
        var position = lighting.Lights[0].Position;
        return position == Vector3.Zero ? -Vector3.UnitZ : Vector3.Normalize(position);
    }

    /// <summary>
    /// <c>updateOptimalExtrusionPadding</c>: lifts the light so it stands at least at <paramref name="scale"/> (the tangent of an elevation, 0 = no limit), by raising the position of the
    /// sun above the ground in the same way.
    /// </summary>
    public static Vector3 Limited(Vector3 direction, float scale)
    {
        if (scale <= 0f) return direction;
        var horizontal = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
        var height = horizontal * scale;
        return height > -direction.Z ? Vector3.Normalize(new Vector3(direction.X, direction.Y, -height)) : direction;
    }

    /// <summary>The tangent for a <c>ShadowSizeX</c> in degrees (0 for none).</summary>
    public static float LengthScale(float degrees) => degrees == 0f ? 0f : MathF.Tan(degrees / 180f * MathF.PI);

    /// <summary>
    /// <c>updateOptimalExtrusionPadding</c> for an immobile object: from the four top corners of its box a walk along the shadow, two cells at a time. Terrain lower than the base by at most
    /// <see cref="MaximumDrop"/> takes the floor of the volume down with it; where it drops further the object stands at a cliff, and the light is made steeper so that the shadow ends at
    /// the last good point (the terrain casts no shadow, so a shadow below a cliff would hang in the air).
    /// </summary>
    public static (float Padding, float LengthScale) OptimalExtrusion(TerrainHeightSampler heights, Vector3 position, Vector3 boxMin, Vector3 boxMax, Vector3 direction, float lengthScale)
    {
        var light = Limited(direction, lengthScale);
        var baseHeight = position.Z;
        var lastValid = position;
        var scale = lengthScale;
        Span<Vector3> corners =
        [
            new(boxMax.X, boxMax.Y, boxMax.Z), new(boxMin.X, boxMax.Y, boxMax.Z), new(boxMin.X, boxMin.Y, boxMax.Z), new(boxMax.X, boxMin.Y, boxMax.Z),
        ];
        var rise = light.Z < -1e-4f ? -1f / light.Z : 0f;
        foreach (var corner in corners)
        {
            // The ray meets the terrain about where it reaches the height of the base; the walk below is what looks at the ground.
            var reach = light * (MathF.Max(0f, corner.Z - position.Z) * rise);
            var along = new Vector2(reach.X, reach.Y);
            var length = along.Length();
            var steps = (int)MathF.Ceiling(length / SamplingInterval);
            if (steps == 0) continue;
            var step = 1f / steps;
            for (var index = 0; index < steps; index++)
            {
                var t = step * (index + 1);
                var x = corner.X + along.X * t;
                var y = corner.Y + along.Y * t;
                var height = heights.HeightAt(x, y);
                if (height < position.Z - MaximumDrop)
                {
                    if (index == 0)
                    {
                        baseHeight = height;
                        scale = MathF.Tan(OverhangAngle);
                        break;
                    }

                    var clamp = corner - lastValid;
                    var angle = MathF.Asin(clamp.Z / clamp.Length());
                    if (angle is >= MathF.PI / 2f or <= 0f) angle = OverhangAngle;
                    scale = MathF.Max(scale, MathF.Tan(angle));
                    break;
                }

                if (height < baseHeight)
                {
                    baseHeight = height;
                    lastValid = new Vector3(x, y, height);
                }
            }
        }

        return (position.Z - baseHeight + ExtrusionBuffer, scale);
    }
}
