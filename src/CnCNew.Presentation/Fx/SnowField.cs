using System.Numerics;
using CnCNew.Presentation.Camera;
using CnCNew.Presentation.Terrain;

namespace CnCNew.Presentation.Fx;

/// <summary>
/// The snow of <c>W3DSnowManager</c>, and with another texture the rain: nothing is simulated. A grid of emitters fills a box around the camera; the height of a flake is a function of time and
/// of a table of starting heights, and a sine moves it to the side. So a flake is where the formula says, in every frame, and the box goes with the camera.
/// </summary>
public sealed class SnowField
{
    /// <summary><c>SNOW_NOISE_X</c>, <c>SNOW_NOISE_Y</c>: the size of the table of starting heights.</summary>
    public const int NoiseSize = 64;

    /// <summary>Emitters per side of the blocks that are tested against the view; the original's <c>m_leafDim</c> serves the same end with larger ones and draw calls of their own.</summary>
    private const int LeafSize = 16;

    /// <summary>The most flakes a frame can hold (the box of the data's largest maps, one flake per square unit).</summary>
    public const int MaxFlakes = 65536;

    private readonly float[] _startingHeights = new float[NoiseSize * NoiseSize];
    private readonly SnowSettings _settings;
    private readonly float _emitterSpacing;
    private readonly float _period;

    public SnowField(SnowSettings settings, uint seed = 1)
    {
        _settings = settings;
        _emitterSpacing = 1f / settings.BoxDensity;
        _period = settings.BoxDimensions / settings.Velocity;
        // SnowManager::updateIniSettings fills the table with rand(); any even spread does, so a generator of our own keeps two runs alike.
        var state = seed;
        for (var index = 0; index < _startingHeights.Length; index++)
        {
            state = state * 1664525u + 1013904223u;
            _startingHeights[index] = (state >> 8) / (float)(1 << 24) * settings.BoxDimensions;
        }
    }

    public SnowSettings Settings => _settings;

    /// <summary>
    /// Writes the centres of the flakes in view for a camera at <paramref name="eye"/> after <paramref name="seconds"/> of game time and returns their number: <c>W3DSnowManager::render</c>
    /// and <c>renderSubBox</c>. The box stands on whole emitters, and the height of a flake depends on where the camera's height falls within the box, so that a camera that moves does
    /// not move the flakes it passes.
    /// </summary>
    public int Collect(Vector3 eye, double seconds, in Frustum frustum, Span<Vector3> flakes)
    {
        var box = _settings.BoxDimensions;
        var time = (float)(seconds % _period);
        var half = (int)MathF.Floor(box / _emitterSpacing * 0.5f);
        var centerX = (int)MathF.Floor(eye.X / _emitterSpacing);
        var centerY = (int)MathF.Floor(eye.Y / _emitterSpacing);
        var ceiling = eye.Z + box / 2f;
        var traveled = time * _settings.Velocity + eye.Z % box;
        var overscan = _settings.Amplitude + 1f;
        var count = 0;
        for (var leafY = centerY - half; leafY < centerY + half; leafY += LeafSize)
        {
            var endY = Math.Min(leafY + LeafSize, centerY + half);
            for (var leafX = centerX - half; leafX < centerX + half; leafX += LeafSize)
            {
                var endX = Math.Min(leafX + LeafSize, centerX + half);
                var minimum = new Vector3(leafX * _emitterSpacing - overscan, leafY * _emitterSpacing - overscan, ceiling - box);
                var maximum = new Vector3(endX * _emitterSpacing + overscan, endY * _emitterSpacing + overscan, ceiling);
                if (!frustum.Intersects(new Aabb(minimum, maximum))) continue;
                for (var y = leafY; y < endY; y++)
                {
                    for (var x = leafX; x < endX; x++)
                    {
                        if (count == flakes.Length) return count;
                        flakes[count++] = Flake(x, y, ceiling, traveled, box);
                    }
                }
            }
        }

        return count;
    }

    private Vector3 Flake(int x, int y, float ceiling, float traveled, float box)
    {
        // MAXIMUM_CAMERA_DISTANCE makes the coordinates positive before they are masked; the two's complement mask of a negative number gives the same table.
        var noise = (x & (NoiseSize - 1)) + (y & (NoiseSize - 1)) * NoiseSize;
        var height = ceiling - (traveled + _startingHeights[noise]) % box;
        return new Vector3(
            x * _emitterSpacing + _settings.Amplitude * MathF.Sin(height * _settings.FrequencyScaleX + x),
            y * _emitterSpacing + _settings.Amplitude * MathF.Sin(height * _settings.FrequencyScaleY + y),
            height);
    }
}
