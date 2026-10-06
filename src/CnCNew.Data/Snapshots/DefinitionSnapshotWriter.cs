using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using CnCNew.Data.Definitions;

namespace CnCNew.Data.Snapshots;

public static class DefinitionSnapshotWriter
{
    public const int SchemaVersion = 2;

    /// <summary>
    /// The canonical snapshot is hashed into the definition fingerprint two machines compare before they play together, so its bytes must not depend on the
    /// machine. Everything the serializer would otherwise take from the platform is stated here: the line end (the default is the platform's, CR LF on Windows
    /// and LF elsewhere - the fingerprints of a Windows and a Linux machine differed) and the indentation. The values are the ones the snapshot always had on
    /// Windows, so existing fingerprints stay valid. String escaping is the serializer's default encoder; it is guarded by golden values in
    /// <c>CnCNew.Data.Tests</c> (docs/DETERMINISM-FINDINGS.md, D9).
    /// </summary>
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        NewLine = "\r\n",
        IndentCharacter = ' ',
        IndentSize = 2,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static byte[] WriteCanonical(CompiledDefinitionGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        return JsonSerializer.SerializeToUtf8Bytes(BuildModel(graph), Options);
    }

    /// <summary>Streams the canonical snapshot; the bytes are identical to <see cref="WriteCanonical"/>.</summary>
    public static void WriteCanonical(CompiledDefinitionGraph graph, Stream destination)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(destination);
        JsonSerializer.Serialize(destination, BuildModel(graph), Options);
    }

    /// <summary>
    /// SHA-256 of the canonical snapshot computed while streaming (no full copy in memory). This is the definition
    /// fingerprint stored in replay headers.
    /// </summary>
    public static (string Sha256, long Length) ComputeFingerprint(CompiledDefinitionGraph graph)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var stream = new HashingStream(hash);
        WriteCanonical(graph, stream);
        return (Convert.ToHexString(hash.GetHashAndReset()), stream.Length);
    }

    private sealed class HashingStream(IncrementalHash hash) : Stream
    {
        private long _length;
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => _length;
        public override long Position { get => _length; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> buffer) { hash.AppendData(buffer); _length += buffer.Length; }
    }

    private static object BuildModel(CompiledDefinitionGraph graph) => new
    {
        schemaVersion = SchemaVersion,
        definitions = graph.Definitions.OrderBy(value => value.Id.ToString(), StringComparer.Ordinal).Select(value => new
        {
            id = value.Id.ToString(), kind = value.Id.Kind.ToString(), name = value.OriginalName,
            source = new { path = value.Location.Source.Replace('\\', '/'), value.Location.Line, value.Location.Column },
            fields = value.Fields.OrderBy(field => field.Name, StringComparer.Ordinal).Select(field => new
            {
                field.Name, kind = field.Value.Kind.ToString(), value = CanonicalValue(field.Value), original = field.Value.OriginalText,
                source = new { path = field.Location.Source.Replace('\\', '/'), field.Location.Line, field.Location.Column }
            }),
            sections = value.Sections.Select(CanonicalSection),
            modules = value.Modules.Select(module => new
            {
                module.Name, module.Tag, owner = module.OwnerKind.ToString(), block = module.BlockKind.ToString(),
                fields = module.Fields.OrderBy(field => field.Name, StringComparer.Ordinal).Select(field => new { field.Name, kind = field.Value.Kind.ToString(), value = CanonicalValue(field.Value), original = field.Value.OriginalText }),
                sections = module.Sections.IsDefault ? [] : module.Sections.Select(CanonicalSection)
            })
        }),
        references = graph.References.Select(edge => new { source = edge.Source.ToString(), edge.Field, targetKind = edge.Target.Kind.ToString(), edge.Target.Name, edge.Resolved, edge.Fallback }),
        compatibility = graph.Audit.Compatibility.Select(value => new { value.Category, value.Name, value.Count, owners = value.OwnerKinds, value.PlannedTarget }),
    };

    private static object CanonicalSection(DefinitionSection section) => new
    {
        section.Declaration,
        fields = section.Fields.OrderBy(field => field.Name, StringComparer.Ordinal).Select(field => new { field.Name, kind = field.Value.Kind.ToString(), value = CanonicalValue(field.Value), original = field.Value.OriginalText }),
        children = section.Children.Select(CanonicalSection),
        source = new { path = section.Location.Source.Replace('\\', '/'), section.Location.Line, section.Location.Column },
    };

    private static string Invariant(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    private static object CanonicalValue(DefinitionValue value) => value.Value switch
    {
        decimal number => Invariant(number),
        GameTime time => new { milliseconds = Invariant(time.Milliseconds) },
        Distance distance => new { value = Invariant(distance.Value) },
        Speed speed => new { value = Invariant(speed.Value) },
        Angle angle => new { degrees = Invariant(angle.Degrees) },
        Percentage percentage => new { ratio = Invariant(percentage.Ratio) },
        Money money => money.Value,
        Vector2Value vector => new { x = Invariant(vector.X), y = Invariant(vector.Y) },
        Vector3Value vector => new { x = Invariant(vector.X), y = Invariant(vector.Y), z = Invariant(vector.Z) },
        ColorValue color => new { color.Red, color.Green, color.Blue, color.Alpha },
        ColorKeyframeValue keyframe => new { keyframe.Color.Red, keyframe.Color.Green, keyframe.Color.Blue, keyframe.Color.Alpha, keyframe.Frame },
        EnumValue enumeration => new { table = enumeration.Table, name = enumeration.Name },
        FlagsValue flags => new { table = flags.Table, names = flags.Names, relative = flags.IsRelative ? true : (bool?)null, removed = flags.IsRelative ? (object)flags.RemovedNames : null },
        DefinitionReference reference => new { kind = reference.Kind.ToString(), reference.Name, reference.IsNone },
        TaggedReferenceList tagged => new { tag = tagged.Tag, references = tagged.References.Select(reference => new { kind = reference.Kind.ToString(), reference.Name, reference.IsNone }) },
        CountedReference counted => new { kind = counted.Reference.Kind.ToString(), counted.Reference.Name, counted.Reference.IsNone, counted.Count },
        CoefficientValue coefficient => new { enumName = coefficient.EnumName, kind = coefficient.Value.Kind.ToString(), value = CanonicalValue(coefficient.Value) },
        WeaponBonusValue bonus => new { condition = bonus.Condition, field = bonus.Field, ratio = Invariant(bonus.Ratio) },
        ShotDelayValue delay => new { min = Invariant(delay.Min.Milliseconds), max = Invariant(delay.Max.Milliseconds) },
        _ => value.Value,
    };
}
