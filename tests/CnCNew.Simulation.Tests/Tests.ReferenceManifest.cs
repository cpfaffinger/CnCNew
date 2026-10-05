using System.Text;
using CnCNew.SimTool.Manifests;

static partial class Tests
{
    private static ManifestObject SyntheticManifest(string finalHash = "23B4B25DF62765D8", long endTick = 14_791, bool identical = true)
    {
        // Deliberately added out of order: the file form must not depend on it.
        var reference = new ManifestObject()
            .Add("set", "quick")
            .Add("results", new ManifestObject().Add("finalHash", finalHash).Add("endTick", endTick).Add("identicalInRun2", identical).Add("chainHash", "DE5849AF9F8493C4"))
            .Add("parameters", new ManifestObject().Add("worldSeed", "C0FFEE05").Add("maxTicks", 216_000))
            .Add("kind", "reference-match");
        var stress = new ManifestObject()
            .Add("kind", "stress")
            .Add("parameters", new ManifestObject().Add("ticks", 216_000))
            .Add("results", new ManifestObject().Add("endTick", 216_000).Add("finalHash", "56882F763EABEF67"))
            .Add("set", "quick");
        return new ManifestObject().Add("schema", 1).Add("runs", new ManifestObject().Add("stress", stress).Add("reference-match", reference)).Add("kind", "reference-manifest");
    }

    public static void ManifestCanonicalRoundTrip()
    {
        var manifest = SyntheticManifest();
        var text = ManifestDocument.Write(manifest);
        Check.That(!text.Contains('\r'), "A manifest uses LF line ends only.");
        Check.That(text.EndsWith("}\n", StringComparison.Ordinal) && !text.EndsWith("\n\n", StringComparison.Ordinal), "A manifest ends with exactly one line end.");
        Check.That(text.StartsWith("{\n  \"kind\": \"reference-manifest\",\n  \"runs\": {\n    \"reference-match\": {\n      \"kind\": \"reference-match\",\n      \"parameters\": {\n        \"maxTicks\": 216000,\n", StringComparison.Ordinal),
            "Keys are written in ordinal order at every level, two spaces per level: " + text[..Math.Min(text.Length, 160)]);
        Check.That(text.IndexOf("\"reference-match\"", StringComparison.Ordinal) < text.IndexOf("\"stress\"", StringComparison.Ordinal), "Runs are sorted by id.");
        Check.That(text.Contains("\"identicalInRun2\": true", StringComparison.Ordinal) && text.Contains("\"endTick\": 14791", StringComparison.Ordinal), "Truth values and numbers are written as such.");

        var bytes = ManifestDocument.WriteBytes(manifest);
        Check.That(bytes.Length > 3 && bytes[0] == (byte)'{', "A manifest has no byte order mark.");
        var read = ManifestDocument.Read(bytes);
        Check.That(ManifestDocument.FirstDifference(manifest, read) is null, "A manifest read back equals the one written.");
        Check.That(ManifestDocument.WriteBytes(read).AsSpan().SequenceEqual(bytes), "Writing what was read gives the same bytes.");

        // A file with another key order, other spacing and CRLF reads as the same manifest and is rewritten canonically.
        var shuffled = "{\r\n \"schema\":1, \"kind\":\"reference-manifest\",\r\n \"runs\":{ \"stress\":" + ManifestDocument.Write((ManifestObject)manifest.Object("runs")["stress"]).Trim()
                       + ", \"reference-match\":" + ManifestDocument.Write((ManifestObject)manifest.Object("runs")["reference-match"]).Trim() + "}}";
        Check.That(ManifestDocument.Write(ManifestDocument.Read(shuffled)) == text, "The canonical form does not depend on the spelling of the file that was read.");

        // Escaping is written here, not by the runtime: quote, backslash and control characters only; everything else stays as it is.
        var odd = new ManifestObject().Add("path", "Maps/Ünïcode \"quoted\" back\\slash\ttab <&>'+");
        var oddText = ManifestDocument.Write(odd);
        Check.That(oddText == "{\n  \"path\": \"Maps/Ünïcode \\\"quoted\\\" back\\\\slash\\u0009tab <&>'+\"\n}\n", "Unexpected escaping: " + oddText);
        Check.That(ManifestDocument.Read(oddText).Text("path") == odd.Text("path"), "Escaped text reads back unchanged.");
        Check.That(ManifestDocument.Write(new ManifestObject().Add("empty", new ManifestObject())) == "{\n  \"empty\": {}\n}\n", "An empty object is written as {}.");

        Check.Throws<ArgumentException>(() => new ManifestObject().Add("a", 1).Add("a", 2), "Duplicate");
        Check.Throws<InvalidDataException>(() => ManifestDocument.Read("{\"a\": [1]}"), "not Array");
        Check.Throws<InvalidDataException>(() => ManifestDocument.Read("{\"a\": 1.5}"), "whole numbers");
        Check.Throws<InvalidDataException>(() => ManifestDocument.Read("{\"a\": 1e3}"), "whole numbers");
        Check.Throws<InvalidDataException>(() => ManifestDocument.Read("{\"a\": null}"), "not Null");
        Check.Throws<InvalidDataException>(() => ManifestDocument.Read("{\"a\": 1, \"a\": 2}"), "twice");
        Check.Throws<InvalidDataException>(() => ManifestDocument.Read("[]"), "not Array");
        Check.Throws<InvalidDataException>(() => ManifestDocument.Read("\"text\""), "root");
        Check.Throws<InvalidDataException>(() => ManifestDocument.Read("{\"a\": 1,}"), "not valid JSON");
        Check.Throws<InvalidDataException>(() => ManifestDocument.Read([0xEF, 0xBB, 0xBF, (byte)'{', (byte)'}']), "byte order mark");
    }

    public static void ManifestCheckReportsTheFirstDifference()
    {
        var expected = SyntheticManifest();
        Check.That(ManifestDocument.FirstDifference(expected, SyntheticManifest()) is null, "Equal manifests have no difference.");

        // Two fields of the same run differ: the first in key order is reported, with both values.
        var actual = SyntheticManifest(finalHash: "0000000000000000", endTick: 14_790);
        var difference = ManifestDocument.FirstDifference(expected, actual) ?? throw new InvalidOperationException("A difference was expected.");
        Check.That(difference.Path == "runs.reference-match.results.endTick" && difference.Expected == "14791" && difference.Actual == "14790", $"Unexpected first difference: {difference}");
        Check.That(difference.ToString() == "runs.reference-match.results.endTick: expected 14791, actual 14790", $"Unexpected report: {difference}");

        // Compared per run, as `check` does, the path starts at the run.
        var perRun = ManifestDocument.FirstDifference(expected.Object("runs")["reference-match"], SyntheticManifest(finalHash: "0000000000000000").Object("runs")["reference-match"])
                     ?? throw new InvalidOperationException("A difference was expected.");
        Check.That(perRun.Path == "results.finalHash" && perRun.Expected == "23B4B25DF62765D8" && perRun.Actual == "0000000000000000", $"Unexpected per-run difference: {perRun}");
        Check.That(ManifestDocument.FirstDifference(expected.Object("runs")["stress"], actual.Object("runs")["stress"]) is null, "The untouched run has no difference.");

        var flag = ManifestDocument.FirstDifference(expected, SyntheticManifest(identical: false)) ?? throw new InvalidOperationException("A difference was expected.");
        Check.That(flag.Path == "runs.reference-match.results.identicalInRun2" && flag.Expected == "true" && flag.Actual == "false", $"Unexpected flag difference: {flag}");

        // A field only one side has, and a value of another kind.
        var extra = SyntheticManifest();
        ((ManifestObject)extra.Object("runs")["stress"]).Object("results").Add("winner", "nobody");
        var added = ManifestDocument.FirstDifference(expected, extra) ?? throw new InvalidOperationException("A difference was expected.");
        Check.That(added.Path == "runs.stress.results.winner" && added.Expected == ManifestDifference.Missing && added.Actual == "nobody", $"Unexpected difference for an added field: {added}");
        var removed = ManifestDocument.FirstDifference(extra, expected) ?? throw new InvalidOperationException("A difference was expected.");
        Check.That(removed.Path == "runs.stress.results.winner" && removed.Expected == "nobody" && removed.Actual == ManifestDifference.Missing, $"Unexpected difference for a missing field: {removed}");
        var kind = ManifestDocument.FirstDifference(new ManifestObject().Add("endTick", 5), new ManifestObject().Add("endTick", "5")) ?? throw new InvalidOperationException("A difference was expected.");
        Check.That(kind.Path == "endTick" && kind.Expected == "number 5" && kind.Actual == "text \"5\"", $"A number and a text with the same spelling differ: {kind}");

        // A whole run missing on one side is the difference at the run itself.
        var fewer = SyntheticManifest();
        var runs = new ManifestObject().Add("stress", fewer.Object("runs")["stress"]);
        var missing = ManifestDocument.FirstDifference(expected.Object("runs"), runs) ?? throw new InvalidOperationException("A difference was expected.");
        Check.That(missing.Path == "reference-match" && missing.Actual == ManifestDifference.Missing, $"Unexpected difference for a missing run: {missing}");
    }

    private sealed class HashNode(string name)
    {
        public string Name = name;
        public HashNode? Next;
        public List<int> Numbers = [];
        public Dictionary<string, long> Table = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Tags = [];
        public Func<int, int>? Code;
        private readonly decimal _weight = 1.5m;
        public decimal Weight => _weight;
    }

    public static void CanonicalObjectHashIgnoresStorageOrder()
    {
        static HashNode Build(bool reversed, Action<HashNode>? change = null)
        {
            var node = new HashNode("root") { Numbers = [3, 1, 2], Code = value => value + 1 };
            var keys = new[] { "alpha", "beta", "gamma", "delta" };
            foreach (var key in reversed ? keys.Reverse() : keys)
            {
                node.Table[key] = key.Length;
                node.Tags.Add(key.ToUpperInvariant());
            }
            node.Next = new HashNode("child") { Next = node };
            change?.Invoke(node);
            return node;
        }

        var first = CanonicalObjectHasher.Hash(Build(false));
        Check.That(first.Sha256.Length == 64 && first.Values > 10, "A digest and a value count are reported.");
        Check.That(CanonicalObjectHasher.Hash(Build(false)).Sha256 == first.Sha256, "Two equal graphs at different addresses hash equally.");
        Check.That(CanonicalObjectHasher.Hash(Build(true)).Sha256 == first.Sha256, "The insertion order of dictionaries and sets does not change the hash.");
        Check.That(CanonicalObjectHasher.HashOnLargeStack(Build(true)).Sha256 == first.Sha256, "The large-stack variant gives the same hash.");
        Check.That(first.Opaque.TryGetValue("delegate", out var delegates) && delegates == 1 && first.Opaque.Count == 1, "A delegate is named and counted, nothing else is opaque: " + string.Join(", ", first.Opaque.Keys));

        Check.That(CanonicalObjectHasher.Hash(Build(false, node => node.Numbers = [1, 2, 3])).Sha256 != first.Sha256, "The order of a list is part of the hash.");
        Check.That(CanonicalObjectHasher.Hash(Build(false, node => node.Table["beta"] = 5)).Sha256 != first.Sha256, "A changed dictionary value changes the hash.");
        Check.That(CanonicalObjectHasher.Hash(Build(false, node => node.Tags.Add("EPSILON"))).Sha256 != first.Sha256, "A changed set changes the hash.");
        Check.That(CanonicalObjectHasher.Hash(Build(false, node => node.Next!.Name = "other")).Sha256 != first.Sha256, "A changed field of a referenced object changes the hash.");
        Check.That(CanonicalObjectHasher.Hash(Build(false, node => node.Next!.Next = null)).Sha256 != first.Sha256, "A cycle and its absence hash differently.");
        Check.That(CanonicalObjectHasher.Hash(Build(false, node => node.Code = null)).Sha256 != first.Sha256, "A missing delegate differs from a present one (its code is not described).");

        // Shared and repeated references: the same object twice equals two equal objects (content, not identity).
        var shared = new HashNode("leaf");
        var twice = CanonicalObjectHasher.Hash(new[] { shared, shared });
        var copies = CanonicalObjectHasher.Hash(new[] { new HashNode("leaf"), new HashNode("leaf") });
        Check.That(twice.Sha256 == copies.Sha256, "An object reached twice is described by its content.");
        Check.That(twice.Values < copies.Values, "An object reached twice is described once.");

        Check.That(CanonicalObjectHasher.Hash(1L).Sha256 != CanonicalObjectHasher.Hash("1").Sha256, "A number and a text differ.");
        Check.That(CanonicalObjectHasher.Hash(0.1).Sha256 != CanonicalObjectHasher.Hash(0.1f).Sha256, "Fractions are hashed by bit pattern and width.");
        Check.That(CanonicalObjectHasher.Hash(Encoding.UTF8.GetBytes("abc")).Sha256 == CanonicalObjectHasher.Hash(new byte[] { 97, 98, 99 }).Sha256, "Byte arrays hash by content.");
    }
}
