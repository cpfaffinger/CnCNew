using System.Numerics;
using CnCNew.Presentation.Terrain;

namespace CnCNew.Presentation.Shadows;

/// <summary>
/// The sun of the shadows: <c>W3DShadowManager::setTimeOfDay</c> puts it at a point 10,000 units from the origin of the world, against the first light of the objects, and the volumes of
/// <c>W3DVolumetricShadow</c> run from that point. It is a point, not a direction: the light meets every object from the point, so it falls a little differently across the map, and a wall that
/// runs along the light's direction still faces it or turns away from it (an exact direction would leave such a wall parallel to the light and its volume's side in the wall, issue 474).
/// <c>ShadowSizeX</c> of a volume is not a size but the lowest elevation of the light in degrees (<c>W3DVolumetricShadowManager::addShadow</c>), which limits the length of the shadows of tall things.
/// </summary>
public static class ShadowSun
{
    /// <summary><c>SUN_DISTANCE_FROM_GROUND</c>.</summary>
    public const float Distance = 10000f;

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
    {
        if (map?.Lighting?.ShadowColor is not { } color) return new Vector3(StencilBrightness);
        // 0xAARRGGBB: in little-endian order the bytes are blue, green, red, alpha.
        Span<byte> bytes = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes, color);
        return new Vector3(bytes[2] / 255f, bytes[1] / 255f, bytes[0] / 255f);
    }

    /// <summary>
    /// The colour constant of the material that fills the stencil shadows. <c>W3DVolumetricShadowManager::renderStencilShadows</c> draws a rectangle with the shadow colour as its vertex colour
    /// and <c>D3DBLEND_DESTCOLOR</c>/<c>D3DBLEND_ZERO</c>: the frame buffer's bytes times the colour's bytes. The renderer converts a colour constant from sRGB to linear light, and the fill's
    /// material encodes its shaded colour back to display values before the blend (<see cref="CnCNew.Presentation.Fx.FrameBufferBlend.ThroughEncodedOutput"/>), so the constant is the
    /// colour as it is. The client converted it to linear light first and the renderer a second time: shadowed ground showed at 0.35 of its brightness instead of the default 160/255 (issue 362).
    /// </summary>
    public static Vector3 FillConstant(Vector3 stencilColor) => stencilColor;

    /// <summary>The direction the light travels: the first light of the objects, or straight down without one.</summary>
    public static Vector3 Direction(TerrainLighting lighting)
    {
        if (lighting.Lights.IsDefaultOrEmpty) return -Vector3.UnitZ;
        var position = lighting.Lights[0].Position;
        return position == Vector3.Zero ? -Vector3.UnitZ : Vector3.Normalize(position);
    }

    /// <summary><c>W3DShadowManager::setTimeOfDay</c>: the sun, <see cref="Distance"/> from the origin of the world against the direction its light travels.</summary>
    public static Vector3 Position(TerrainLighting lighting) => -Direction(lighting) * Distance;

    /// <summary>
    /// <c>updateMeshVolume</c> and <c>updateOptimalExtrusionPadding</c>: a volume with a limit on the sun's elevation (<paramref name="scale"/>, the tangent; 0 = no limit) raises the sun to at least
    /// that elevation over the origin of the world.
    /// </summary>
    public static Vector3 LimitedPosition(Vector3 sun, float scale)
    {
        if (scale <= 0f) return sun;
        var height = MathF.Sqrt(sun.X * sun.X + sun.Y * sun.Y) * scale;
        return height > sun.Z ? new Vector3(sun.X, sun.Y, height) : sun;
    }

    /// <summary>The direction the light of the sun at <paramref name="sun"/> travels at <paramref name="point"/>.</summary>
    public static Vector3 Toward(Vector3 sun, Vector3 point) => Vector3.Normalize(point - sun);

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
    public static (float Padding, float LengthScale) OptimalExtrusion(TerrainHeightSampler heights, Vector3 position, Vector3 boxMin, Vector3 boxMax, Vector3 sun, float lengthScale)
    {
        var limited = LimitedPosition(sun, lengthScale);
        var baseHeight = position.Z;
        var lastValid = position;
        var scale = lengthScale;
        Span<Vector3> corners =
        [
            new(boxMax.X, boxMax.Y, boxMax.Z), new(boxMin.X, boxMax.Y, boxMax.Z), new(boxMin.X, boxMin.Y, boxMax.Z), new(boxMax.X, boxMin.Y, boxMax.Z),
        ];
        foreach (var corner in corners)
        {
            // The ray from the sun through the corner meets the terrain about where it reaches the height of the base; the walk below is what looks at the ground.
            var light = Toward(limited, corner);
            var rise = light.Z < -1e-4f ? -1f / light.Z : 0f;
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

    /// <summary>
    /// Where the shadow of the top of an object ends under the sun at <paramref name="sun"/>: the point on the floor plane its volume reaches (<c>updateMeshVolume</c> extrudes the top corners of the mesh's box down to <paramref name="floorZ"/>).
    /// A thing in the air reaches down to the lowest point of the map, so its shadow lies far below it, not just its own height: the box that decides whether it is seen has to take that in.
    /// </summary>
    public static Vector3 Tip(Vector3 position, Vector3 sun, float height, float floorZ)
    {
        var direction = Toward(sun, position + new Vector3(0f, 0f, height));
        var slope = direction.Z < -1e-4f ? 1f / -direction.Z : 0f;
        var drop = MathF.Max(0f, position.Z + height - floorZ);
        return new Vector3(position.X + direction.X * drop * slope, position.Y + direction.Y * drop * slope, floorZ);
    }
}
