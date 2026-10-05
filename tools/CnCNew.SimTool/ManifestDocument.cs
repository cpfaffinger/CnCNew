using System.Globalization;
using System.Text;
using System.Text.Json;

namespace CnCNew.SimTool.Manifests;

/// <summary>
/// A value of a baseline manifest (<c>baselines/reference-manifest.json</c>, <c>baselines/input-manifest.json</c>): a text, a whole number, a truth value
/// or an object of named values. The model is deliberately a strict subset of JSON (no arrays, no fractions, no null): everything a manifest holds is a
/// name with one exact value, so two manifests can be compared field by field and the file has exactly one spelling.
/// </summary>
public abstract record ManifestNode
{
    public static implicit operator ManifestNode(string value) => new ManifestText(value);
    public static implicit operator ManifestNode(long value) => new ManifestNumber(value);
    public static implicit operator ManifestNode(bool value) => new ManifestFlag(value);

    /// <summary>The value as it is shown in a difference report.</summary>
    public abstract string Display { get; }
}

public sealed record ManifestText(string Value) : ManifestNode
{
    public override string Display => Value;
}

public sealed record ManifestNumber(long Value) : ManifestNode
{
    public override string Display => Value.ToString(CultureInfo.InvariantCulture);
}

public sealed record ManifestFlag(bool Value) : ManifestNode
{
    public override string Display => Value ? "true" : "false";
}

/// <summary>Named values in ordinal key order, whatever the order they were added in.</summary>
public sealed record ManifestObject : ManifestNode
{
    private readonly SortedDictionary<string, ManifestNode> _values = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, ManifestNode> Values => _values;
    public override string Display => $"<object with {_values.Count} entries>";

    public ManifestNode this[string name]
    {
        get => _values[name];
        set => _values[name] = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Adds a value; a name that is already present is an error (a manifest never overwrites silently).</summary>
    public ManifestObject Add(string name, ManifestNode value)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (!_values.TryAdd(name, value ?? throw new ArgumentNullException(nameof(value)))) throw new ArgumentException($"Duplicate manifest entry '{name}'.");
        return this;
    }

    public bool TryGet(string name, out ManifestNode value) => _values.TryGetValue(name, out value!);

    public ManifestObject Object(string name) => _values.TryGetValue(name, out var value) && value is ManifestObject child ? child : throw new InvalidDataException($"The manifest has no object '{name}'.");

    public string Text(string name) => _values.TryGetValue(name, out var value) && value is ManifestText text ? text.Value : throw new InvalidDataException($"The manifest has no text '{name}'.");

    public bool Equals(ManifestObject? other) => other is not null && ManifestDocument.FirstDifference(this, other) is null;

    public override int GetHashCode() => _values.Count;
}

/// <summary>The first place where two manifests disagree: the path of the field and both values (<see cref="Missing"/> when one side has no such field).</summary>
public sealed record ManifestDifference(string Path, string Expected, string Actual)
{
    public const string Missing = "<missing>";

    public override string ToString() => $"{Path}: expected {Expected}, actual {Actual}";
}

/// <summary>
/// The canonical file form of a manifest and the comparison of two manifests. Canonical means: UTF-8 without a byte order mark, LF line ends, two spaces
/// per level, keys in ordinal order, numbers in invariant spelling, and an escaping that is written here and not left to the runtime (only the quote, the
/// backslash and the control characters below U+0020 are escaped). The same tree always gives the same bytes, on every machine.
/// </summary>
public static class ManifestDocument
{
    public static string Write(ManifestObject root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var text = new StringBuilder();
        WriteNode(text, root, 0);
        text.Append('\n');
        return text.ToString();
    }

    public static byte[] WriteBytes(ManifestObject root) => new UTF8Encoding(false).GetBytes(Write(root));

    /// <summary>Reads a manifest; anything outside the model (arrays, fractions, null, a repeated key) is refused.</summary>
    public static ManifestObject Read(ReadOnlySpan<byte> utf8)
    {
        if (utf8.StartsWith("﻿"u8)) throw new InvalidDataException("A manifest has no byte order mark.");
        try
        {
            using var document = JsonDocument.Parse(utf8.ToArray(), new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow });
            return ReadNode(document.RootElement, "$") as ManifestObject ?? throw new InvalidDataException("The root of a manifest is an object.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"The manifest is not valid JSON: {exception.Message}", exception);
        }
    }

    public static ManifestObject Read(string text) => Read(new UTF8Encoding(false).GetBytes(text));

    /// <summary>
    /// The first difference in a depth-first walk in key order, or <see langword="null"/> when both trees are equal. A field that only one side has is
    /// reported with <see cref="ManifestDifference.Missing"/> on the other side.
    /// </summary>
    public static ManifestDifference? FirstDifference(ManifestNode expected, ManifestNode actual, string path = "")
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        if (expected is ManifestObject expectedObject && actual is ManifestObject actualObject)
        {
            var names = new SortedSet<string>(expectedObject.Values.Keys, StringComparer.Ordinal);
            names.UnionWith(actualObject.Values.Keys);
            foreach (var name in names)
            {
                var child = path.Length == 0 ? name : path + "." + name;
                if (!expectedObject.TryGet(name, out var left)) return new ManifestDifference(child, ManifestDifference.Missing, actualObject[name].Display);
                if (!actualObject.TryGet(name, out var right)) return new ManifestDifference(child, left.Display, ManifestDifference.Missing);
                if (FirstDifference(left, right, child) is { } difference) return difference;
            }
            return null;
        }
        if (expected.GetType() != actual.GetType()) return new ManifestDifference(path, Describe(expected), Describe(actual));
        return expected.Display == actual.Display ? null : new ManifestDifference(path, expected.Display, actual.Display);

        static string Describe(ManifestNode node) => node switch
        {
            ManifestText text => $"text \"{text.Value}\"",
            ManifestNumber number => $"number {number.Display}",
            ManifestFlag flag => $"flag {flag.Display}",
            _ => node.Display,
        };
    }

    private static void WriteNode(StringBuilder text, ManifestNode node, int depth)
    {
        switch (node)
        {
            case ManifestText value:
                WriteString(text, value.Value);
                break;
            case ManifestNumber value:
                text.Append(value.Value.ToString(CultureInfo.InvariantCulture));
                break;
            case ManifestFlag value:
                text.Append(value.Value ? "true" : "false");
                break;
            case ManifestObject value when value.Values.Count == 0:
                text.Append("{}");
                break;
            case ManifestObject value:
                text.Append("{\n");
                var index = 0;
                foreach (var pair in value.Values)
                {
                    text.Append(' ', (depth + 1) * 2);
                    WriteString(text, pair.Key);
                    text.Append(": ");
                    WriteNode(text, pair.Value, depth + 1);
                    text.Append(++index < value.Values.Count ? ",\n" : "\n");
                }
                text.Append(' ', depth * 2).Append('}');
                break;
            default:
                throw new InvalidOperationException($"Unknown manifest node {node.GetType().Name}.");
        }
    }

    private static void WriteString(StringBuilder text, string value)
    {
        text.Append('"');
        foreach (var character in value)
        {
            if (character == '"') text.Append("\\\"");
            else if (character == '\\') text.Append("\\\\");
            else if (character < ' ') text.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
            else text.Append(character);
        }
        text.Append('"');
    }

    private static ManifestNode ReadNode(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return new ManifestText(element.GetString()!);
            case JsonValueKind.True:
                return new ManifestFlag(true);
            case JsonValueKind.False:
                return new ManifestFlag(false);
            case JsonValueKind.Number:
                return element.TryGetInt64(out var number) && !element.GetRawText().AsSpan().ContainsAny('.', 'e', 'E')
                    ? new ManifestNumber(number)
                    : throw new InvalidDataException($"{path}: a manifest holds whole numbers only, not {element.GetRawText()}.");
            case JsonValueKind.Object:
                var result = new ManifestObject();
                foreach (var property in element.EnumerateObject())
                {
                    if (result.TryGet(property.Name, out _)) throw new InvalidDataException($"{path}: the key '{property.Name}' occurs twice.");
                    result.Add(property.Name, ReadNode(property.Value, path + "." + property.Name));
                }
                return result;
            default:
                throw new InvalidDataException($"{path}: a manifest holds texts, whole numbers, truth values and objects, not {element.ValueKind}.");
        }
    }
}
