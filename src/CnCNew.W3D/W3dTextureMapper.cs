using System.Globalization;
using System.Numerics;

namespace CnCNew.W3D;

public enum W3dTextureMapperKind : byte
{
    Uv = 0,
    Environment = 1,
    ClassicEnvironment = 2,
    Screen = 3,
    LinearOffset = 4,
    Silhouette = 5,
    Scale = 6,
    Grid = 7,
    Rotate = 8,
    SineLinearOffset = 9,
    StepLinearOffset = 10,
    ZigZagLinearOffset = 11,
    WorldSpaceClassicEnvironment = 12,
    WorldSpaceEnvironment = 13,
    GridClassicEnvironment = 14,
    GridEnvironment = 15,
    Random = 16,
    Edge = 17,
    BumpEnvironment = 18,
    GridWorldSpaceClassicEnvironment = 19,
    GridWorldSpaceEnvironment = 20,
}

public abstract record W3dTextureMapper(W3dTextureMapperKind Kind)
{
    public static W3dTextureMapper Uv { get; } = new W3dUvTextureMapper();
}

public sealed record W3dUvTextureMapper() : W3dTextureMapper(W3dTextureMapperKind.Uv);

public sealed record W3dEnvironmentTextureMapper(bool Reflection)
    : W3dTextureMapper(Reflection ? W3dTextureMapperKind.Environment : W3dTextureMapperKind.ClassicEnvironment);

public sealed record W3dLinearOffsetTextureMapper(
    Vector2 UnitsPerSecond,
    Vector2 StartingOffset,
    Vector2 Scale,
    bool ClampFix)
    : W3dTextureMapper(W3dTextureMapperKind.LinearOffset);

public sealed record W3dGridTextureMapper(float FramesPerSecond, int Log2Width, int LastFrame, int Offset)
    : W3dTextureMapper(W3dTextureMapperKind.Grid);

public readonly record struct W3dUvTransform(Vector2 Scale, Vector2 Offset)
{
    public static W3dUvTransform Identity => new(Vector2.One, Vector2.Zero);

    public Vector2 Apply(Vector2 coordinate) => coordinate * Scale + Offset;
}

public static class W3dTextureMapperParser
{
    public static W3dTextureMapper Parse(uint attributes, int stage, string? arguments)
    {
        if (stage is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }

        var rawKind = stage == 0 ? (attributes >> 16) & 0xff : (attributes >> 8) & 0xff;
        if (rawKind > (uint)W3dTextureMapperKind.GridWorldSpaceEnvironment)
        {
            throw new InvalidDataException($"Unknown W3D texture mapper value {rawKind} in stage {stage}.");
        }

        var kind = (W3dTextureMapperKind)rawKind;
        var values = ParseArguments(arguments);
        return kind switch
        {
            W3dTextureMapperKind.Uv => RequireNoArguments(kind, values),
            W3dTextureMapperKind.Environment => RequireNoArguments(kind, values, new W3dEnvironmentTextureMapper(true)),
            W3dTextureMapperKind.ClassicEnvironment => RequireNoArguments(kind, values, new W3dEnvironmentTextureMapper(false)),
            W3dTextureMapperKind.LinearOffset => new W3dLinearOffsetTextureMapper(
                new Vector2(GetFloat(values, "UPerSec", 0), GetFloat(values, "VPerSec", 0)),
                new Vector2(GetFloat(values, "UOffset", 0), GetFloat(values, "VOffset", 0)),
                new Vector2(GetFloat(values, "UScale", 1), GetFloat(values, "VScale", 1)),
                GetBool(values, "ClampFix", false, kind)),
            W3dTextureMapperKind.Grid => ParseGrid(values, kind),
            _ => throw new InvalidDataException(
                $"W3D texture mapper '{kind}' is valid but is not supported by the static-material runtime."),
        };
    }

    private static W3dGridTextureMapper ParseGrid(
        Dictionary<string, string> values,
        W3dTextureMapperKind kind)
    {
        var mapper = new W3dGridTextureMapper(
            GetFloat(values, "FPS", 1),
            GetIntAlias(values, "Log2Width", "Log1Width", 1, kind),
            GetInt(values, "Last", 0, kind),
            GetInt(values, "Offset", 0, kind));
        EnsureConsumed(values, kind);
        return mapper;
    }

    private static W3dTextureMapper RequireNoArguments(
        W3dTextureMapperKind kind,
        Dictionary<string, string> values,
        W3dTextureMapper? result = null)
    {
        EnsureConsumed(values, kind);
        return result ?? W3dTextureMapper.Uv;
    }

    private static Dictionary<string, string> ParseArguments(string? arguments)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return values;
        }

        foreach (var rawLine in arguments.Replace("\r", string.Empty).Split('\n'))
        {
            var line = rawLine.Split(';', 2)[0].Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0 || separator == line.Length - 1)
            {
                throw new InvalidDataException($"Invalid W3D mapper argument '{rawLine}'.");
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (!values.TryAdd(key, value))
            {
                throw new InvalidDataException($"Duplicate W3D mapper argument '{key}'.");
            }
        }

        return values;
    }

    private static float GetFloat(Dictionary<string, string> values, string key, float fallback)
    {
        if (!values.Remove(key, out var text))
        {
            return fallback;
        }

        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !float.IsFinite(value))
        {
            throw new InvalidDataException($"W3D mapper argument '{key}' has invalid float value '{text}'.");
        }

        return value;
    }

    private static int GetInt(Dictionary<string, string> values, string key, int fallback, W3dTextureMapperKind kind)
    {
        if (!values.Remove(key, out var text))
        {
            return fallback;
        }

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0)
        {
            throw new InvalidDataException($"W3D mapper '{kind}' argument '{key}' has invalid integer value '{text}'.");
        }

        return value;
    }

    private static int GetIntAlias(
        Dictionary<string, string> values,
        string key,
        string shippedAlias,
        int fallback,
        W3dTextureMapperKind kind)
    {
        var hasKey = values.ContainsKey(key);
        var hasAlias = values.ContainsKey(shippedAlias);
        if (hasKey && hasAlias)
        {
            throw new InvalidDataException($"W3D mapper '{kind}' specifies both '{key}' and '{shippedAlias}'.");
        }

        return GetInt(values, hasAlias ? shippedAlias : key, fallback, kind);
    }

    private static bool GetBool(
        Dictionary<string, string> values,
        string key,
        bool fallback,
        W3dTextureMapperKind kind)
    {
        if (!values.Remove(key, out var text))
        {
            EnsureConsumed(values, kind);
            return fallback;
        }

        if (bool.TryParse(text, out var value))
        {
            EnsureConsumed(values, kind);
            return value;
        }

        if (text is "0" or "1")
        {
            EnsureConsumed(values, kind);
            return text == "1";
        }

        throw new InvalidDataException($"W3D mapper '{kind}' argument '{key}' has invalid Boolean value '{text}'.");
    }

    private static void EnsureConsumed(Dictionary<string, string> values, W3dTextureMapperKind kind)
    {
        if (values.Count > 0)
        {
            throw new InvalidDataException(
                $"W3D mapper '{kind}' has unknown argument(s): {string.Join(", ", values.Keys.Order())}.");
        }
    }
}

public static class W3dTextureMapperEvaluator
{
    public static W3dUvTransform Evaluate(W3dTextureMapper mapper, double totalSeconds)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        if (!double.IsFinite(totalSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(totalSeconds));
        }

        return mapper switch
        {
            W3dUvTextureMapper => W3dUvTransform.Identity,
            W3dEnvironmentTextureMapper => W3dUvTransform.Identity,
            W3dLinearOffsetTextureMapper linear => EvaluateLinear(linear, totalSeconds),
            W3dGridTextureMapper grid => EvaluateGrid(grid, totalSeconds),
            _ => throw new InvalidDataException($"Unsupported W3D mapper '{mapper.Kind}'."),
        };
    }

    public static Vector2 EvaluateEnvironment(
        W3dEnvironmentTextureMapper mapper,
        Vector3 normal,
        Vector3 viewDirection)
    {
        var normalizedNormal = normal.LengthSquared() > 0 ? Vector3.Normalize(normal) : Vector3.UnitZ;
        var generated = mapper.Reflection
            ? Vector3.Reflect(-Vector3.Normalize(viewDirection), normalizedNormal)
            : normalizedNormal;
        return new Vector2(generated.X * 0.5f + 0.5f, generated.Y * 0.5f + 0.5f);
    }

    private static W3dUvTransform EvaluateLinear(W3dLinearOffsetTextureMapper mapper, double totalSeconds)
    {
        var offset = mapper.StartingOffset - mapper.UnitsPerSecond * (float)totalSeconds;
        if (mapper.ClampFix)
        {
            offset = Vector2.Clamp(offset, -mapper.Scale, mapper.Scale);
        }
        else
        {
            offset = new Vector2(Wrap(offset.X), Wrap(offset.Y));
        }

        return new W3dUvTransform(mapper.Scale, offset);
    }

    private static W3dUvTransform EvaluateGrid(W3dGridTextureMapper mapper, double totalSeconds)
    {
        if (mapper.Log2Width is < 0 or > 15)
        {
            throw new InvalidDataException($"Grid mapper Log2Width {mapper.Log2Width} is outside 0..15.");
        }

        var width = 1 << mapper.Log2Width;
        var frameCount = mapper.LastFrame == 0 ? checked(width * width) : mapper.LastFrame;
        if (frameCount <= 0 || mapper.Offset >= frameCount)
        {
            throw new InvalidDataException("Grid mapper frame range is invalid.");
        }

        var step = (long)Math.Floor(Math.Abs(mapper.FramesPerSecond) * totalSeconds);
        var initial = mapper.FramesPerSecond < 0 ? frameCount - 1 - mapper.Offset : mapper.Offset;
        var direction = mapper.FramesPerSecond < 0 ? -1L : mapper.FramesPerSecond > 0 ? 1L : 0L;
        var frame = (int)((initial + direction * step) % frameCount);
        if (frame < 0)
        {
            frame += frameCount;
        }

        return new W3dUvTransform(
            Vector2.One,
            new Vector2((frame % width) / (float)width, (frame / width) / (float)width));
    }

    private static float Wrap(float value) => value - MathF.Floor(value);
}
