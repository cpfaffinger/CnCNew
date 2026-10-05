using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace CnCNew.SimTool.Manifests;

/// <summary>What <see cref="CanonicalObjectHasher"/> saw: the digest, how many values it described and which kinds of values it could only name.</summary>
/// <param name="Opaque">Per kind (<c>delegate</c>, <c>unordered System.Collections.Generic.HashSet&lt;…&gt;</c>, …) how often a value was named instead of described.</param>
public sealed record CanonicalHash(string Sha256, long Values, IReadOnlyDictionary<string, long> Opaque);

/// <summary>
/// SHA-256 of an object graph by content: a canonical description of what an immutable catalog <em>is</em>, independent of the order of dictionaries and
/// sets, of memory addresses, of the runtime's own hash codes and of the order in which reflection lists members. The built catalogs of the simulation
/// have no serialised form of their own; this is the description the input manifest hashes.
/// <para>
/// Rules. Whole numbers, truth values, characters and enums are written by value (enums as their number), <see cref="decimal"/> in invariant spelling,
/// fractions as their bit pattern, texts with their length. A struct is its fields. A class instance is the SHA-256 of its type name and its fields, so
/// that an object reached twice is described once (a reference back to an object that is still being described is written as the distance up to it).
/// Fields are all instance fields of the type and its base types, in ordinal order of declaring type and name. Arrays and lists keep their order;
/// dictionaries are sorted by the description of the key, sets and every other enumerable by the description of the element. Delegates, reflection
/// objects and pointers are only named: code is not part of the description.
/// </para>
/// <para>Not covered, by construction: the comparer of a dictionary or set (ordinal or case-insensitive), static state, and the code behind delegates.</para>
/// </summary>
public sealed class CanonicalObjectHasher
{
    private readonly Dictionary<object, string> _digests = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, int> _open = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Type, FieldInfo[]> _fields = [];
    private readonly Dictionary<Type, string> _names = [];
    private readonly SortedDictionary<string, long> _opaque = new(StringComparer.Ordinal);
    private long _values;
    private int _depth;

    /// <summary>Describes <paramref name="root"/> and everything it references. Deep graphs need a deep stack: see <see cref="HashOnLargeStack"/>.</summary>
    public static CanonicalHash Hash(object? root)
    {
        var hasher = new CanonicalObjectHasher();
        var text = new StringBuilder("canonical-object-v1\n");
        hasher.Describe(text, root);
        return new CanonicalHash(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))), hasher._values, hasher._opaque);
    }

    /// <summary><see cref="Hash"/> on a thread of its own with a 256 MiB stack (the description recurses once per level of the graph).</summary>
    public static CanonicalHash HashOnLargeStack(object? root)
    {
        CanonicalHash? result = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { result = Hash(root); }
            catch (Exception exception) { failure = exception; }
        }, 256 * 1024 * 1024);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException($"Describing the object graph failed: {failure.Message}", failure);
        return result!;
    }

    /// <returns>The shallowest depth an up-reference inside the described value points to (<see cref="int.MaxValue"/>: none).</returns>
    private int Describe(StringBuilder text, object? value)
    {
        _values++;
        switch (value)
        {
            case null: text.Append("null"); return int.MaxValue;
            case string item: text.Append('s').Append(item.Length.ToString(CultureInfo.InvariantCulture)).Append(':').Append(item); return int.MaxValue;
            case bool item: text.Append(item ? "true" : "false"); return int.MaxValue;
            case char item: text.Append("c:").Append(((int)item).ToString(CultureInfo.InvariantCulture)); return int.MaxValue;
            case sbyte or byte or short or ushort or int or uint or long or ulong or Int128 or UInt128 or nint or nuint:
                text.Append("i:").Append(((IFormattable)value).ToString(null, CultureInfo.InvariantCulture));
                return int.MaxValue;
            case decimal item: text.Append("m:").Append(item.ToString(CultureInfo.InvariantCulture)); return int.MaxValue;
            case double item: text.Append("f8:").Append(BitConverter.DoubleToInt64Bits(item).ToString("X16", CultureInfo.InvariantCulture)); return int.MaxValue;
            case float item: text.Append("f4:").Append(BitConverter.SingleToInt32Bits(item).ToString("X8", CultureInfo.InvariantCulture)); return int.MaxValue;
            case Enum item: text.Append("e:").Append(Convert.ToString(Convert.ChangeType(item, Enum.GetUnderlyingType(item.GetType()), CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)); return int.MaxValue;
            case Delegate: return Opaque(text, "delegate");
            case Type item: text.Append("type:").Append(NameOf(item)); return int.MaxValue;
            case MemberInfo or Assembly or Module or Pointer: return Opaque(text, "reflection " + NameOf(value.GetType()));
        }

        var type = value.GetType();
        if (type.IsValueType)
        {
            // A struct that wraps a collection (ImmutableArray<T>) is its fields like any other: the array inside keeps its order.
            text.Append(NameOf(type));
            return DescribeFields(text, type, value);
        }
        if (_digests.TryGetValue(value, out var known))
        {
            text.Append('#').Append(known);
            return int.MaxValue;
        }
        if (_open.TryGetValue(value, out var openDepth))
        {
            text.Append('^').Append((_depth - openDepth).ToString(CultureInfo.InvariantCulture));
            return openDepth;
        }

        var own = new StringBuilder(NameOf(type));
        _open.Add(value, _depth);
        _depth++;
        if (_depth > 20_000) throw new InvalidOperationException($"The object graph is deeper than 20,000 levels at {NameOf(type)}.");
        var up = value switch
        {
            byte[] bytes => Bytes(own, bytes),
            Array array => Ordered(own, array),
            IDictionary dictionary => Dictionary(own, dictionary),
            IEnumerable enumerable when IsOrdered(type) => Ordered(own, enumerable),
            IEnumerable enumerable => Unordered(own, enumerable, type),
            _ => DescribeFields(own, type, value),
        };
        _depth--;
        _open.Remove(value);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(own.ToString())));
        // A description that points above this object depends on the way it was reached and must not be reused from another way.
        if (up >= _depth) _digests.Add(value, digest);
        text.Append('#').Append(digest);
        return up >= _depth ? int.MaxValue : up;
    }

    private int Opaque(StringBuilder text, string kind)
    {
        _opaque[kind] = _opaque.GetValueOrDefault(kind) + 1;
        text.Append('<').Append(kind).Append('>');
        return int.MaxValue;
    }

    private static int Bytes(StringBuilder text, byte[] bytes)
    {
        text.Append("[bytes ").Append(bytes.Length.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(Convert.ToHexString(SHA256.HashData(bytes))).Append(']');
        return int.MaxValue;
    }

    private int Ordered(StringBuilder text, IEnumerable items)
    {
        var up = int.MaxValue;
        text.Append('[');
        foreach (var item in items)
        {
            up = Math.Min(up, Describe(text, item));
            text.Append(',');
        }
        text.Append(']');
        return up;
    }

    private int Unordered(StringBuilder text, IEnumerable items, Type type)
    {
        if (!IsSet(type)) _opaque["unordered " + NameOf(type)] = _opaque.GetValueOrDefault("unordered " + NameOf(type)) + 1;
        var up = int.MaxValue;
        var parts = new List<string>();
        foreach (var item in items)
        {
            var part = new StringBuilder();
            up = Math.Min(up, Describe(part, item));
            parts.Add(part.ToString());
        }
        parts.Sort(StringComparer.Ordinal);
        text.Append('{');
        foreach (var part in parts) text.Append(part).Append(',');
        text.Append('}');
        return up;
    }

    private int Dictionary(StringBuilder text, IDictionary dictionary)
    {
        var up = int.MaxValue;
        var parts = new List<(string Key, string Value)>();
        foreach (DictionaryEntry entry in dictionary)
        {
            var key = new StringBuilder();
            var item = new StringBuilder();
            up = Math.Min(up, Describe(key, entry.Key));
            up = Math.Min(up, Describe(item, entry.Value));
            parts.Add((key.ToString(), item.ToString()));
        }
        parts.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key) is var order && order != 0 ? order : string.CompareOrdinal(left.Value, right.Value));
        text.Append('{');
        foreach (var (key, item) in parts) text.Append(key).Append("=>").Append(item).Append(',');
        text.Append('}');
        return up;
    }

    private int DescribeFields(StringBuilder text, Type type, object value)
    {
        var up = int.MaxValue;
        text.Append('(');
        foreach (var field in FieldsOf(type))
        {
            text.Append(field.Name).Append('=');
            up = Math.Min(up, Describe(text, field.GetValue(value)));
            text.Append(';');
        }
        text.Append(')');
        return up;
    }

    private FieldInfo[] FieldsOf(Type type)
    {
        if (_fields.TryGetValue(type, out var known)) return known;
        var all = new List<(string Owner, FieldInfo Field)>();
        for (var current = type; current is not null && current != typeof(object) && current != typeof(ValueType); current = current.BaseType)
            foreach (var field in current.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                all.Add((NameOf(current), field));
        all.Sort((left, right) => string.CompareOrdinal(left.Owner, right.Owner) is var order && order != 0 ? order : string.CompareOrdinal(left.Field.Name, right.Field.Name));
        return _fields[type] = [..all.Select(item => item.Field)];
    }

    /// <summary>Lists, queues and stacks have an order of their own; everything else that can be enumerated is treated as a set.</summary>
    private static bool IsOrdered(Type type) => typeof(IList).IsAssignableFrom(type) || HasGeneric(type, typeof(IList<>)) || HasGeneric(type, typeof(IReadOnlyList<>))
        || IsGeneric(type, typeof(Queue<>)) || IsGeneric(type, typeof(Stack<>)) || IsGeneric(type, typeof(LinkedList<>));

    private static bool IsSet(Type type) => HasGeneric(type, typeof(ISet<>)) || HasGeneric(type, typeof(IReadOnlySet<>));

    private static bool IsGeneric(Type type, Type definition) => type.IsGenericType && type.GetGenericTypeDefinition() == definition;

    private static bool HasGeneric(Type type, Type definition) => type.GetInterfaces().Any(item => item.IsGenericType && item.GetGenericTypeDefinition() == definition);

    /// <summary>Namespace and name with generic arguments, without assembly names or versions (those change with the runtime).</summary>
    private string NameOf(Type type)
    {
        if (_names.TryGetValue(type, out var known)) return known;
        string name;
        if (type.IsArray) name = NameOf(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        else
        {
            var prefix = type.IsNested && type.DeclaringType is { } outer ? NameOf(outer) + "+" : type.Namespace is { Length: > 0 } space ? space + "." : string.Empty;
            name = prefix + type.Name;
            if (type.IsConstructedGenericType) name += "<" + string.Join(",", type.GetGenericArguments().Select(NameOf)) + ">";
        }
        return _names[type] = name;
    }
}
