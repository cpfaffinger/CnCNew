using System.Globalization;
using System.Numerics;
using CnCNew.Data;
using CnCNew.Data.Definitions;

namespace CnCNew.Presentation.Fx;

/// <summary>
/// Reads FX field values from the original INI text the compiler keeps in <see cref="DefinitionValue.OriginalText"/>. The FX domains
/// have no typed schema in <c>CnCNew.Data</c> (their fields keep the heuristic Phase-3 typing), so the INI parse functions of the
/// reference are re-applied here: <c>INI::scanReal</c> (numeric prefix), <c>scanBool</c> (Yes/No), <c>parseCoord3D</c>
/// (<c>X: Y: Z:</c>), <c>parseRGBColor</c> (<c>R: G: B:</c>, divided by 255), <c>parseIndexList</c> (case-insensitive),
/// <c>parseDurationUnsignedInt</c>, <c>parseVelocityReal</c>, <c>parseAngleReal</c>, <c>parsePercentToReal</c>. Every value that does
/// not fit is reported, never replaced silently.
/// </summary>
internal sealed class FxValueReader(FxDiagnosticBag diagnostics, string subject)
{
    /// <summary>FX data is authored per original frame (1/30 s); the particle clock steps once per original frame, never per simulation tick.</summary>
    public const float FramesPerSecond = OriginalFrame.PerSecond;
    private const float FramesPerMillisecond = FramesPerSecond / 1000f;
    private const float SecondsPerFrame = 1f / FramesPerSecond;
    private const float RadiansPerDegree = MathF.PI / 180f;

    public string Subject { get; } = subject;

    public static string[] Tokens(string text)
        => text.Split([' ', '\t', '='], StringSplitOptions.RemoveEmptyEntries);

    public void Invalid(string key, string text, string expected)
        => diagnostics.Add(FxDiagnosticCodes.InvalidValue, $"{Subject}/{key}", $"'{text.Trim()}' is not {expected}.");

    private void Extra(string key, string text, int used, string[] tokens)
    {
        if (tokens.Length > used)
            diagnostics.Add(FxDiagnosticCodes.IgnoredTokens, $"{Subject}/{key}", $"'{text.Trim()}': {tokens.Length - used} trailing token(s) ignored.");
    }

    public string? Name(string key, string text)
    {
        var tokens = Tokens(text);
        if (tokens.Length == 0) { Invalid(key, text, "a name"); return null; }
        Extra(key, text, 1, tokens);
        return tokens[0];
    }

    public float Real(string key, string text, float fallback)
    {
        var tokens = Tokens(text);
        if (tokens.Length == 0 || !TryReal(tokens[0], out var value)) { Invalid(key, text, "a real number"); return fallback; }
        Extra(key, text, 1, tokens);
        return value;
    }

    public uint UnsignedInt(string key, string text, uint fallback)
    {
        var tokens = Tokens(text);
        if (tokens.Length == 0 || !uint.TryParse(tokens[0], NumberStyles.None, CultureInfo.InvariantCulture, out var value))
        {
            Invalid(key, text, "an unsigned integer");
            return fallback;
        }
        Extra(key, text, 1, tokens);
        return value;
    }

    public int Int(string key, string text, int fallback)
    {
        var tokens = Tokens(text);
        if (tokens.Length == 0 || !int.TryParse(tokens[0], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value))
        {
            Invalid(key, text, "an integer");
            return fallback;
        }
        Extra(key, text, 1, tokens);
        return value;
    }

    public bool Bool(string key, string text, bool fallback)
    {
        var tokens = Tokens(text);
        if (tokens.Length == 0 || !ValueGrammar.TryBool(tokens[0], out var value)) { Invalid(key, text, "Yes or No"); return fallback; }
        Extra(key, text, 1, tokens);
        return value;
    }

    /// <summary><c>INI::parseDurationUnsignedInt</c>: milliseconds to frames, rounded up, in single precision like the reference.</summary>
    public uint DurationFrames(string key, string text, uint fallback)
    {
        var milliseconds = UnsignedInt(key, text, uint.MaxValue);
        return milliseconds == uint.MaxValue ? fallback : MillisecondsToFrames(milliseconds);
    }

    public static uint MillisecondsToFrames(float milliseconds) => (uint)MathF.Ceiling(milliseconds * FramesPerMillisecond);

    /// <summary><c>INI::parseVelocityReal</c>: distance per second to distance per logic frame.</summary>
    public float VelocityPerFrame(string key, string text, float fallback) => Real(key, text, fallback * FramesPerSecond) * SecondsPerFrame;

    /// <summary><c>INI::parseAngleReal</c>: degrees to radians.</summary>
    public float AngleRadians(string key, string text) => Real(key, text, 0f) * RadiansPerDegree;

    /// <summary><c>INI::parsePercentToReal</c>: <c>150%</c> is 1.5.</summary>
    public float Percent(string key, string text)
    {
        var tokens = Tokens(text);
        if (tokens.Length == 0 || !TryReal(tokens[0].TrimEnd('%'), out var value)) { Invalid(key, text, "a percentage"); return 0f; }
        Extra(key, text, 1, tokens);
        return value / 100f;
    }

    public Vector3 Coord3D(string key, string text)
    {
        if (!ValueGrammar.TryLabeled(text, ["X", "Y", "Z"], [], out var values, out var rest)
            || !TryReal(values[0], out var x) || !TryReal(values[1], out var y) || !TryReal(values[2], out var z))
        {
            Invalid(key, text, "'X:n Y:n Z:n'");
            return Vector3.Zero;
        }
        if (rest.Length > 0) diagnostics.Add(FxDiagnosticCodes.IgnoredTokens, $"{Subject}/{key}", $"'{text.Trim()}': {rest.Length} trailing token(s) ignored.");
        return new Vector3(x, y, z);
    }

    /// <summary><c>INI::parseRGBColor</c> (components 0..255, stored divided by 255) and the trailing tokens for keyframe forms.</summary>
    public Vector3 RgbColor(string key, string text, out string[] rest)
    {
        if (!ValueGrammar.TryLabeled(text, ["R", "G", "B"], [], out var values, out rest)
            || !TryByte(values[0], out var r) || !TryByte(values[1], out var g) || !TryByte(values[2], out var b))
        {
            Invalid(key, text, "'R:n G:n B:n' with components 0..255");
            rest = [];
            return Vector3.Zero;
        }
        return new Vector3(r / 255f, g / 255f, b / 255f);
    }

    public Vector3 RgbColor(string key, string text)
    {
        var color = RgbColor(key, text, out var rest);
        if (rest.Length > 0) diagnostics.Add(FxDiagnosticCodes.IgnoredTokens, $"{Subject}/{key}", $"'{text.Trim()}': {rest.Length} trailing token(s) ignored.");
        return color;
    }

    /// <summary><c>INI::parseGameClientRandomVariable</c>: <c>low high [distribution]</c>, UNIFORM when the distribution is omitted.</summary>
    public FxRandomVariable RandomVariable(string key, string text)
    {
        var tokens = Tokens(text);
        if (tokens.Length < 2 || !TryReal(tokens[0], out var low) || !TryReal(tokens[1], out var high))
        {
            Invalid(key, text, "'low high [distribution]'");
            return FxRandomVariable.Zero;
        }
        var distribution = FxDistribution.Uniform;
        if (tokens.Length >= 3)
        {
            var index = Array.FindIndex(DistributionNames, name => name.Equals(tokens[2], StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                diagnostics.Add(FxDiagnosticCodes.UnknownEnum, $"{Subject}/{key}", $"'{tokens[2]}' is not a GameClientRandomVariable distribution.");
                return FxRandomVariable.Zero;
            }
            distribution = (FxDistribution)index;
            if (distribution is not (FxDistribution.Constant or FxDistribution.Uniform))
                diagnostics.Add(FxDiagnosticCodes.UnsupportedDistribution, $"{Subject}/{key}",
                    $"{tokens[2]} is unimplemented in GameClientRandomVariable::getValue and always yields 0.");
            Extra(key, text, 3, tokens);
        }
        return new FxRandomVariable(low, high, distribution);
    }

    /// <summary><c>INI::parseIndexList</c>/<c>scanLookupList</c>: case-insensitive position in <paramref name="names"/>, or -1.</summary>
    public int Index(string key, string text, IReadOnlyList<string> names, string table)
    {
        var tokens = Tokens(text);
        if (tokens.Length == 0) { Invalid(key, text, $"a {table} name"); return -1; }
        Extra(key, text, 1, tokens);
        for (var index = 0; index < names.Count; index++)
            if (names[index].Equals(tokens[0], StringComparison.OrdinalIgnoreCase)) return index;
        diagnostics.Add(FxDiagnosticCodes.UnknownEnum, $"{Subject}/{key}", $"'{tokens[0]}' is not a member of {table}.");
        return -1;
    }

    private static readonly string[] DistributionNames = ["CONSTANT", "UNIFORM", "GAUSSIAN", "TRIANGULAR", "LOW_BIAS", "HIGH_BIAS"];

    public static bool TryReal(string token, out float value)
    {
        if (ValueGrammar.TryRealPrefix(token, out var number, out _)) { value = (float)number; return true; }
        value = 0f;
        return false;
    }

    private static bool TryByte(string token, out byte value)
        => byte.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out value);
}
