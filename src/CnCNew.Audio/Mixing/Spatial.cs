using System.Numerics;

namespace CnCNew.Audio.Mixing;

/// <summary>Distance attenuation and stereo panning of positional voices.</summary>
public static class Spatial
{
    /// <summary>
    /// Gain for a source at <paramref name="distance"/> (see <see cref="DistanceModel"/>). A non-positive
    /// <paramref name="maxDistance"/> disables attenuation; a non-positive <paramref name="minDistance"/> means full volume
    /// up to the maximum distance.
    /// </summary>
    public static float Attenuation(DistanceModel model, float distance, float minDistance, float maxDistance)
    {
        if (maxDistance <= 0f) return 1f;
        if (distance >= maxDistance) return 0f;
        if (minDistance <= 0f || distance <= minDistance) return 1f;
        return model == DistanceModel.Inverse
            ? minDistance / distance
            : 1f - (distance - minDistance) / (maxDistance - minDistance);
    }

    /// <summary>
    /// Equal-power pan law: pan −1 (left) … +1 (right) maps to θ = (pan + 1)·π/4 with gains (cos θ, sin θ), so
    /// left² + right² = 1 for every position and the centre sits at −3 dB on each side.
    /// </summary>
    public static (float Left, float Right) EqualPowerPan(float pan)
    {
        var theta = (Math.Clamp(pan, -1f, 1f) + 1f) * (MathF.PI / 4f);
        return (MathF.Cos(theta), MathF.Sin(theta));
    }

    /// <summary>Pan position of <paramref name="position"/>: the cosine between the direction to the source and the listener's right axis (forward × up).</summary>
    public static float Pan(AudioListener listener, Vector3 position)
    {
        var direction = position - listener.Position;
        var right = Vector3.Cross(listener.Forward, listener.Up);
        if (direction.LengthSquared() < 1e-6f || right.LengthSquared() < 1e-12f) return 0f;
        return Math.Clamp(Vector3.Dot(Vector3.Normalize(direction), Vector3.Normalize(right)), -1f, 1f);
    }
}
