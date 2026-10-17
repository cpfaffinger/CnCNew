using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;

namespace CnCNew.Game.Diagnostics;

public enum StateFieldKind : byte
{
    Integer,
    Fixed,
    Text,
}

/// <summary>One value of a described state: an integer, a raw fixed-point value or a text (booleans and values too large for a long are texts).</summary>
public readonly record struct StateField(StateFieldKind Kind, long Number, string Text)
{
    public static StateField Integer(long value) => new(StateFieldKind.Integer, value, string.Empty);
    public static StateField FixedRaw(long raw) => new(StateFieldKind.Fixed, raw, string.Empty);
    public static StateField Of(string text) => new(StateFieldKind.Text, 0, text);

    public override string ToString() => Kind switch
    {
        StateFieldKind.Fixed => $"{CnCNew.Simulation.Numerics.Fixed.FromRaw(Number)} (raw {Number.ToString(CultureInfo.InvariantCulture)})",
        StateFieldKind.Integer => Number.ToString(CultureInfo.InvariantCulture),
        _ => Text,
    };

    /// <summary>The value shortened for a line of a report.</summary>
    public string Short()
    {
        var text = ToString();
        return text.Length <= 60 ? text : text[..57] + "...";
    }
}

/// <summary>A path at which two states differ; a side without the path is null.</summary>
public sealed record StateFieldDifference(string Path, StateField? Left, StateField? Right)
{
    public override string ToString() => $"{Path}: {Left?.Short() ?? "(missing)"} -> {Right?.Short() ?? "(missing)"}";
}

/// <param name="Ignore">Paths that are not compared.</param>
/// <param name="Equal">When two values of the same path count as equal; null is equality of kind, number and text.</param>
/// <param name="RightOnly">Whether a path that only the right side has is a difference.</param>
/// <param name="LeftOnly">Whether a path that only the left side has is a difference.</param>
public sealed record StateFieldRules(Func<string, bool>? Ignore = null, Func<StateField, StateField, bool>? Equal = null, bool LeftOnly = true, bool RightOnly = true)
{
    public static StateFieldRules Exact { get; } = new();
}

/// <summary>
/// The one field comparison of the diagnosis tools (docs/MATCH-ANALYSIS.md, S3-M4 (c)): a described state - an object, a world, a session - is flattened into paths and
/// values (<see cref="Of"/> from <see cref="IStateWriter"/>, <see cref="OfJson"/> from a saved document), and two such tables are compared path by path in ordinal order
/// (<see cref="Compare"/>). <c>SimTool rate-compare --fields</c> compares one match at two rates with it (with a rule of equality that knows ticks and values per tick),
/// <c>NetTool desync-diff</c> compares a client's saved state with the match played again from its own recording.
/// <para>
/// A path joins the names with '.'; an element of an array is addressed as <c>[id=N]</c> when it is an object whose first field is its <c>id</c> (an object of the world, an
/// event - so that one missing element does not shift every other), else by the first text it contains (<c>[3 Name]</c>, a module by its name) when written through
/// <see cref="IStateWriter"/>, else by its position.
/// </para>
/// </summary>
public static class StateFields
{
    /// <summary>The fields of a state described through <see cref="IStateWriter"/>.</summary>
    public static Dictionary<string, StateField> Of(Action<IStateWriter> describe)
    {
        ArgumentNullException.ThrowIfNull(describe);
        var writer = new FieldWriter();
        describe(writer);
        return writer.Fields;
    }

    /// <summary>The fields of a saved document (numbers are integers: a saved state writes fixed-point values as their raw integers).</summary>
    /// <param name="skipProperty">Properties that are left out wherever they stand (the recording embedded in a session state, for example).</param>
    public static Dictionary<string, StateField> OfJson(JsonElement root, Func<string, bool>? skipProperty = null)
    {
        var fields = new Dictionary<string, StateField>(StringComparer.Ordinal);
        Flatten(root, string.Empty, fields, skipProperty);
        return fields;
    }

    private static void Flatten(JsonElement element, string path, Dictionary<string, StateField> into, Func<string, bool>? skip)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (skip?.Invoke(property.Name) == true) continue;
                    Flatten(property.Value, path.Length == 0 ? property.Name : path + "." + property.Name, into, skip);
                }
                break;
            case JsonValueKind.Array:
            {
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    var key = item.ValueKind == JsonValueKind.Object && item.TryGetProperty("id", out var id) ? "id=" + id.GetRawText() : index.ToString(CultureInfo.InvariantCulture);
                    Flatten(item, $"{path}[{key}]", into, skip);
                    index++;
                }
                if (index == 0) into[path] = StateField.Of("[]");
                break;
            }
            case JsonValueKind.Number:
                into[path] = element.TryGetInt64(out var number) ? StateField.Integer(number) : StateField.Of(element.GetRawText());
                break;
            case JsonValueKind.String:
                into[path] = StateField.Of(element.GetString() ?? string.Empty);
                break;
            default:
                into[path] = StateField.Of(element.GetRawText());
                break;
        }
    }

    /// <summary>The paths at which <paramref name="left"/> and <paramref name="right"/> differ, in ordinal order, at most <paramref name="limit"/> of them.</summary>
    public static ImmutableArray<StateFieldDifference> Compare(IReadOnlyDictionary<string, StateField> left, IReadOnlyDictionary<string, StateField> right, StateFieldRules? rules = null,
        int limit = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        rules ??= StateFieldRules.Exact;
        var result = ImmutableArray.CreateBuilder<StateFieldDifference>();
        foreach (var path in left.Keys.Union(right.Keys).Order(StringComparer.Ordinal))
        {
            if (result.Count >= limit) break;
            if (rules.Ignore?.Invoke(path) == true) continue;
            var hasLeft = left.TryGetValue(path, out var x);
            var hasRight = right.TryGetValue(path, out var y);
            if (hasLeft && hasRight)
            {
                if (rules.Equal is { } equal ? equal(x, y) : x == y) continue;
                result.Add(new StateFieldDifference(path, x, y));
            }
            else if (hasLeft ? rules.LeftOnly : rules.RightOnly) result.Add(new StateFieldDifference(path, hasLeft ? x : null, hasRight ? y : null));
        }
        return result.ToImmutable();
    }

    /// <summary>Collects a description as path → value.</summary>
    private sealed class FieldWriter : IStateWriter
    {
        public Dictionary<string, StateField> Fields { get; } = new(StringComparer.Ordinal);
        private readonly List<string> _path = [];
        private readonly List<int> _counters = [];
        private readonly List<int> _labelAt = [];
        // Whether the element opened last has written a field yet: an element whose first field is its id is addressed by it.
        private readonly List<bool> _fresh = [];

        public void BeginObject(string? name)
        {
            if (name is null && _counters.Count > 0)
            {
                _path.Add($"[{_counters[^1]++}]");
                _labelAt.Add(_path.Count - 1);
            }
            else
            {
                _path.Add(name ?? string.Empty);
                _labelAt.Add(-1);
            }
            _counters.Add(-1);
            _fresh.Add(true);
        }

        public void EndObject()
        {
            _counters.RemoveAt(_counters.Count - 1);
            _path.RemoveAt(_path.Count - 1);
            _labelAt.RemoveAt(_labelAt.Count - 1);
            _fresh.RemoveAt(_fresh.Count - 1);
        }

        public void BeginArray(string name)
        {
            _path.Add(name);
            _labelAt.Add(-1);
            _counters.Add(0);
            _fresh.Add(false);
        }

        public void EndArray() => EndObject();

        public void Bool(string name, bool value) => Add(name, StateField.Of(value ? "true" : "false"));
        public void Int(string name, long value) => Label(name, value.ToString(CultureInfo.InvariantCulture), StateField.Integer(value));
        public void UInt(string name, ulong value) => Label(name, value.ToString(CultureInfo.InvariantCulture), StateField.Of(value.ToString(CultureInfo.InvariantCulture)));
        public void Fixed(string name, Fixed value) => Add(name, StateField.FixedRaw(value.Raw));

        public void Text(string name, string value)
        {
            if (_labelAt.Count > 0 && _labelAt[^1] is >= 0 and var index && _path[index].EndsWith(']') && !_path[index].Contains(' ') && !_path[index].Contains('='))
                _path[index] = _path[index][..^1] + " " + value + "]";
            Add(name, StateField.Of(value));
        }

        /// <summary>The first field of an element of an array, when it is its id, names the element.</summary>
        private void Label(string name, string text, StateField value)
        {
            if (name == "id" && _fresh.Count > 0 && _fresh[^1] && _labelAt[^1] is >= 0 and var index) _path[index] = $"[id={text}]";
            Add(name, value);
        }

        private void Add(string name, StateField value)
        {
            if (_fresh.Count > 0) _fresh[^1] = false;
            if (_counters.Count > 0 && _counters[^1] >= 0) name = $"{name}[{_counters[^1]++}]";
            // An element follows its array without a dot ("objects[id=9].health"), as the paths of a saved document do.
            var path = string.Concat(_path.Where(part => part.Length > 0).Select((part, index) => index > 0 && part[0] != '[' ? "." + part : part)) + "." + name;
            var unique = path;
            for (var copy = 2; !Fields.TryAdd(unique, value); copy++) unique = $"{path}#{copy}";
        }
    }
}
