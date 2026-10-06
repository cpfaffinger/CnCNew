using System.Text.RegularExpressions;

/// <summary>
/// Phase 18, D6: the lexical determinism rules. Two rule sets: one for the simulation itself, one for the code that produces its inputs (definitions, map
/// decoding, the bone table). A rule is a regular expression over code lines (strings blanked, comments and raw string blocks removed), so it is a tripwire,
/// not a proof: it cannot see types (a culture-formatted number inside an interpolation hole is invisible to it; <c>Determinism D4</c> runs under hostile
/// cultures for that). Every accepted hit is an explicit exception with its reason; an exception that no longer matches anything fails the test.
/// </summary>
static partial class Tests
{
    private sealed record SourceRule(string Name, Regex Pattern);

    /// <param name="File">End of the repository-relative path, forward slashes.</param>
    /// <param name="LinePart">Text the offending source line contains.</param>
    private sealed record SourceException(string File, string Rule, string LinePart, string Reason);

    private const string RuleWallTime = "wall time";
    private const string RuleAmbientRandom = "ambient randomness";
    private const string RuleFloatingPoint = "floating point";
    private const string RuleVectors = "vector and intrinsics types";
    private const string RuleThreads = "threads and tasks";
    private const string RuleEnvironment = "file system, process or environment";
    private const string RuleReflection = "reflection";
    private const string RuleCulture = "culture-dependent text operation";
    private const string RuleNumberText = "number formatted or parsed without the invariant culture";
    private const string RuleProcessHash = "process-dependent hash code";
    private const string RuleFloatConversion = "conversion of a floating-point number outside the checked routine";
    private const string RuleDirtyFlag = "dirty flag set without MarkDirty";
    private const string RulePlatformText = "platform line end or path separator";

    private static readonly SourceRule[] CommonRules =
    [
        new(RuleWallTime, new Regex(@"\b(DateTime|DateTimeOffset|Stopwatch|TimeProvider|TimeSpan)\b|Environment\.TickCount")),
        new(RuleAmbientRandom, new Regex(@"Random\.Shared|System\.Random\b|new Random\(|Guid\.NewGuid|RandomNumberGenerator")),
        new(RuleVectors, new Regex(@"System\.Numerics(?!\.(BitOperations|BigInteger)\b)|System\.Runtime\.Intrinsics|\bVector(64|128|256|512)\b|\bVector<|\bMatrix(3x2|4x4)\b|\bQuaternion\b")),
        new(RuleThreads, new Regex(@"\bThread\b|\bTask\b|\bValueTask\b|\bParallel\b|ThreadPool|lock \(|\basync\b|\bawait\b|\bInterlocked\b|ThreadLocal|AsyncLocal|System\.Collections\.Concurrent|\.AsParallel\(|\bvolatile\b")),
        new(RuleReflection, new Regex(@"System\.Reflection|GetType\(\)\.GetMethod|Activator\.|typeof\(.*\)\.GetProperties")),
        // Text comparison and case mapping that follow the culture of the machine: comparers and comparisons named after a culture, the parameterless case
        // mappings, string.Compare without an ordinal comparison, single-argument StartsWith/EndsWith (culture-sensitive for a string argument; a char literal is
        // fine), IndexOf/LastIndexOf of a string literal without a comparison, and sorted collections of strings without a comparer.
        new(RuleCulture, new Regex(@"StringComparer\.(CurrentCulture|InvariantCulture)|StringComparison\.(CurrentCulture|InvariantCulture)|CultureInfo\.(CurrentCulture|CurrentUICulture|DefaultThread)" +
                               @"|\.ToUpper\(\)|\.ToLower\(\)|\bstring\.Compare\((?![^;]*StringComparison\.Ordinal)|\.CompareTo\(""""|Comparer<string>\.Default" +
                               @"|\.(StartsWith|EndsWith)\((?!')[^,()]*\)|\.(IndexOf|LastIndexOf)\(""""\)" +
                               @"|Sorted(Dictionary|Set|List)<\(?string\b[^=;]*=\s*(\[\]|new\(\))|new Sorted(Dictionary|Set|List)<\(?string\b[^(]*\(\)")),
        // Numbers as text: ToString without a format provider, string.Format/AppendFormat, and Parse/TryParse without the invariant culture.
        new(RuleNumberText, new Regex(@"\.ToString\(\)|\.ToString\(""""\)|\bstring\.Format\(|\.AppendFormat\(" +
                                  @"|\b(s?byte|u?short|u?int|u?long|decimal|U?Int(16|32|64|128)|BigInteger)\.(Try)?Parse\((?!.*(CultureInfo\.InvariantCulture|NumberFormatInfo\.InvariantInfo))")),
        // The line end and the path separator of the platform: a text that is hashed (the definition fingerprint, the bone table) must not contain them.
        // The definition snapshot differed between Windows and Linux by exactly this (D9); the serializer's line end is pinned there.
        new(RulePlatformText, new Regex(@"Environment\.NewLine|\.AppendLine\(|\.WriteLine\(|\bPath\.(DirectorySeparatorChar|AltDirectorySeparatorChar|PathSeparator|Combine|Join|GetFullPath|GetRelativePath)")),
        // string.GetHashCode and HashCode.Combine are seeded per process: fine as a dictionary key hash, wrong anywhere a value or an order is derived from them.
        new(RuleProcessHash, new Regex(@"\.GetHashCode\(\)|\bHashCode\.")),
    ];

    private const string FloatingPointPattern =
        // Single/Double as type names, not the LINQ method Single(...).
        @"\b(float|double)\b|(?<!\.)\b(Single|Double)\b(?!\()|MathF\.|\bMath\.(Sin|Cos|Tan|Asin|Acos|Atan|Atan2|Sinh|Cosh|Tanh|Sqrt|Cbrt|Pow|Exp|Log|Log2|Log10|Round|Floor|Ceiling|Truncate|ScaleB|FusedMultiplyAdd|IEEERemainder|PI|E|Tau)\b" +
        // A real literal that is not a decimal (1.5, 1e5, 2f, 2d): `var x = 1.5` is a double without the word.
        @"|(?<![\w.])\d[\d_]*\.\d[\d_]*(?:[eE][+-]?\d+)?(?![\w.])|(?<![\w.])\d[\d_]*(?:[eE][+-]?\d+|[fFdD])(?![\w.])";

    /// <summary>The simulation: no floating point at all, no access to anything outside itself.</summary>
    private static readonly SourceRule[] SimulationRules =
    [
        ..CommonRules,
        new(RuleFloatingPoint, new Regex(FloatingPointPattern)),
        new(RuleEnvironment, new Regex(@"\b(File|Directory|Process|Environment|AppContext|AppDomain|RuntimeInformation)\.")),
        // The flag alone does not put the entity into the world's dirty list, and it makes every later MarkDirty return early: the cached hash stays stale
        // until something recomputes the full hash (D8, found on MD_GLA05).
        // The same holds for the per-module marks: a bit without the entry in the world's list is never looked at, a bit set by hand is a promise nobody checked.
        new(RuleDirtyFlag, new Regex(@"\bDirty\s*=\s*true|\bDirtyModules\s*\|=")),
    ];

    /// <summary>
    /// What feeds the simulation. Floating point exists there only as a file format: the bits of a single are turned into exact numbers by
    /// <c>SingleBits.ToDecimal</c> (maps) or into exact binary fractions (the bone table); no floating-point arithmetic, no cast of the runtime, no reading of
    /// singles as numbers. <c>SingleToInt32Bits</c> (number to bits) is the allowed way in and is therefore not matched.
    /// </summary>
    private static readonly SourceRule[] FeederRules =
    [
        ..CommonRules,
        new(RuleFloatConversion, new Regex(FloatingPointPattern + @"|\(decimal\)\s*[A-Za-z_(]|BitConverter\.(Int32BitsToSingle|Int64BitsToDouble|UInt32BitsToSingle|UInt64BitsToDouble|ToSingle|ToDouble)|\.Read(Single|Double)\(|BinaryPrimitives\.Read(Single|Double)")),
    ];

    private static readonly SourceException[] SimulationExceptions =
    [
        // -- ids: the dictionary key hash of a value type; no value and no order is derived from it (Dictionary and HashSet enumerate by slot, not by hash).
        new("Numerics/Fixed.cs", RuleProcessHash, "public override int GetHashCode() => _raw.GetHashCode();", "hash of the raw 64-bit value, the same in every process"),
        new("Templates/ScienceCatalog.cs", RuleProcessHash, "HashCode.Combine(_low, _high)", "key hash of a bit set; used for equality lookups only"),
        new("Templates/UpgradeCatalog.cs", RuleProcessHash, "HashCode.Combine(_low, _high)", "key hash of a bit set; used for equality lookups only"),
        new("World/SimulationWorld.cs", RuleDirtyFlag, "entity.Dirty = true;", "MarkDirty itself: the flag and the list entry together"),
        new("World/SimulationWorld.cs", RuleDirtyFlag, "entity.DirtyModules |= 1UL << module.Index;", "MarkModuleDirty itself: the bit and the list entry together, for an update that vouched for its own state"),
        // -- ToString() of things that are not numbers
        new("Mission/MissionSession.cs", RuleNumberText, "localPlayer.ToString()", "PlayerId.ToString is invariant (Ids.cs); the text is a load note, not state"),
        new("Skirmish/SkirmishAi.cs", RuleNumberText, "RoleOf(template).ToString()", "an enum name; diagnostics for tools"),
        new("Templates/CrateCatalog.cs", RuleNumberText, "value.Id.ToString()", "DefinitionId is text"),
        new("Templates/FactionCatalog.cs", RuleNumberText, "value.Id.ToString()", "DefinitionId is text"),
        new("Templates/ObjectCreationListCatalog.cs", RuleNumberText, "value.Id.ToString()", "DefinitionId is text"),
        new("Templates/ObjectTemplate.cs", RuleNumberText, "value.Id.ToString()", "DefinitionId is text"),
        new("Templates/ScienceCatalog.cs", RuleNumberText, "value.Id.ToString()", "DefinitionId is text"),
        new("Templates/SpecialPowerCatalog.cs", RuleNumberText, "value.Id.ToString()", "DefinitionId is text"),
        new("Templates/UpgradeCatalog.cs", RuleNumberText, "value.Id.ToString()", "DefinitionId is text"),
        new("Templates/WeaponCatalog.cs", RuleNumberText, "value.Id.ToString()", "DefinitionId is text"),
        new("Templates/ModelGeometry.cs", RuleNumberText, "text.ToString()", "a StringBuilder filled with invariant text"),
        new("World/SimulationWorld.CreationLists.cs", RuleNumberText, "nugget.Kind.ToString(), CreationSkipReasons.NuggetNotSimulated", "an enum name"),
        new("World/SimulationWorld.CreationLists.cs", RuleNumberText, "nugget.Kind.ToString(), CreationSkipReasons.NoObjectNamed", "an enum name"),
        new("Templates/WeaponCatalog.cs", RuleNumberText, "decimal.TryParse(field.Value.OriginalText.Split([' ', '\\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(), System.Globalization.NumberStyles.Float,",
            "the invariant culture is the next argument, on the following line"),
    ];

    private static readonly SourceException[] FeederExceptions =
    [
        // -- ToString() of things that are not numbers (DefinitionId is text, the others are enum names)
        new("Compilation/DefinitionCompiler.cs", RuleNumberText, "owner.ToString()", "an enum name (ModuleOwnerKind)"),
        new("Compilation/DefinitionCompiler.cs", RuleNumberText, "value.Id.ToString()", "DefinitionId is text"),
        new("Compilation/DefinitionCompiler.cs", RuleNumberText, "file.Class.ToString()", "an enum name"),
        new("Compilation/DefinitionCompiler.cs", RuleNumberText, "value.Id.Kind.ToString()", "an enum name"),
        new("Compilation/DefinitionCompiler.cs", RuleNumberText, "value.Source.ToString()", "DefinitionId is text"),
        new("Snapshots/DefinitionSnapshotWriter.cs", RuleNumberText, "value.Id.ToString()", "DefinitionId is text"),
        new("Snapshots/DefinitionSnapshotWriter.cs", RuleNumberText, "Kind.ToString()", "enum names (the references line also prints edge.Source, a DefinitionId, which is text)"),
        new("Formats/LegacyAssetReaders.cs", RuleNumberText, "return result.ToString();", "a StringBuilder"),
        // -- a comparer's key hash: equality lookups only
        new("Schema/ObjectSchema.cs", RuleProcessHash, "HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(value.Item1)", "IEqualityComparer.GetHashCode of a dictionary key; no value or order derives from it"),
        // -- floating point that never becomes a simulation input
        new("Formats/LegacyAssetReaders.cs", RuleFloatConversion, "var value = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(offset)));", "ValidateWak: import-time validation of a walk-mesh file, the number is only tested for finiteness"),
        new("Formats/LegacyAssetReaders.cs", RuleFloatConversion, "if (!float.IsFinite(value)) throw new InvalidDataException($\"WAK contains", "ValidateWak: import-time validation, see above"),
        // -- the bone table (D1): the one place a single of a model file enters the exact domain; only its bits are read, no arithmetic is done on it
        new("Game/Data/ModelGeometryLoader.cs", RuleFloatConversion, "static Dyadic Exact(float component) => Dyadic.FromSingleBits(BitConverter.SingleToInt32Bits(component));", "the parameter type of the bits-only entry point"),
    ];

    /// <summary>Scans the files and returns one line per hit that no exception covers, and one per exception that covered nothing.</summary>
    private static List<string> ScanSources(string root, IEnumerable<string> files, SourceRule[] rules, SourceException[] exceptions)
    {
        var used = new bool[exceptions.Length];
        var violations = new List<string>();
        foreach (var file in files.Order(StringComparer.Ordinal))
        {
            var separator = Path.DirectorySeparatorChar;
            if (file.Contains($"{separator}obj{separator}", StringComparison.Ordinal) || file.Contains($"{separator}bin{separator}", StringComparison.Ordinal)) continue;
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            var lineNumber = 0;
            var insideRawString = false;
            foreach (var raw in File.ReadLines(file))
            {
                lineNumber++;
                // Raw string blocks (""" ... """ over several lines) hold fixture text, not code.
                var delimiters = Regex.Matches(raw, "\"\"\"").Count;
                var line = raw;
                if (insideRawString)
                {
                    if (delimiters % 2 == 1) insideRawString = false;
                    continue;
                }
                if (delimiters % 2 == 1)
                {
                    insideRawString = true;
                    line = raw[..raw.IndexOf("\"\"\"", StringComparison.Ordinal)];
                }
                else if (delimiters > 0)
                {
                    line = Regex.Replace(line, "\"\"\".*?\"\"\"", "\"\"");
                }
                line = Regex.Replace(line, "\"(\\\\.|[^\"\\\\])*\"", "\"\"");
                var comment = line.IndexOf("//", StringComparison.Ordinal);
                if (comment >= 0) line = line[..comment];
                foreach (var rule in rules)
                {
                    if (!rule.Pattern.IsMatch(line)) continue;
                    var exception = Array.FindIndex(exceptions, value => value.Rule == rule.Name && relative.EndsWith(value.File, StringComparison.Ordinal) && raw.Contains(value.LinePart, StringComparison.Ordinal));
                    if (exception >= 0) used[exception] = true;
                    else violations.Add($"{relative}({lineNumber}): {rule.Name}: {raw.Trim()}");
                }
            }
        }
        for (var index = 0; index < exceptions.Length; index++)
            if (!used[index]) violations.Add($"exception without a hit: {exceptions[index].File} [{exceptions[index].Rule}] '{exceptions[index].LinePart}'");
        return violations;
    }

    private static string RepositoryRoot()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "CnCNew.slnx"))) root = Path.GetDirectoryName(root);
        Check.That(root is not null, "Repository root not found.");
        return root!;
    }

    /// <summary>The second rule set: everything that turns files into simulation inputs.</summary>
    public static void FeederSourceRules()
    {
        var root = RepositoryRoot();
        var files = Directory.EnumerateFiles(Path.Combine(root, "src", "CnCNew.Data"), "*.cs", SearchOption.AllDirectories)
            .Append(Path.Combine(root, "src", "CnCNew.Formats", "SageMapDecoder.cs"))
            .Append(Path.Combine(root, "src", "CnCNew.Formats", "LegacyAssetReaders.cs"))
            .Append(Path.Combine(root, "src", "CnCNew.Game", "Data", "ModelGeometryLoader.cs"))
            .Append(Path.Combine(root, "src", "CnCNew.Game", "Data", "ExactBoneMath.cs"));
        var violations = ScanSources(root, files, FeederRules, FeederExceptions);
        Check.That(violations.Count == 0, $"Forbidden constructs in the code that feeds the simulation ({violations.Count}):\n  " + string.Join("\n  ", violations));

        // The rules themselves: each construct the finding names is caught, the allowed spellings are not.
        string[] caught =
        [
            "var x = Math.Atan2(a, b);", "var y = Math.Round(z);", "var half = 1.5;", "var big = 1e5;", "var f = 2f;", "Vector128<int> v;", "using System.Numerics;", "using System.Runtime.Intrinsics;",
            "var c = StringComparer.InvariantCulture;", "names.Sort(Comparer<string>.Default);", "var h = name.GetHashCode();", "var k = HashCode.Combine(a, b);", "var p = Environment.ProcessorCount;",
            "Parallel.For(0, 4, Body);", "await Task.Delay(1);", "var s = value.ToString();", "var n = int.Parse(text);", "if (name.StartsWith(prefix)) return;", "var u = name.ToUpper();",
            "private readonly SortedDictionary<string, int> _table = [];", "var d = (decimal)single;", "var t = MathF.Tan(a);", "var q = Math.Sqrt(2);", "var w = Math.Floor(v);", "Entity.Dirty = true;", "entity.DirtyModules |= 2;", "text.AppendLine(line);", "var p = Path.Combine(a, b);", "var n = Environment.NewLine;",
        ];
        foreach (var line in caught)
            Check.That(SimulationRules.Concat(FeederRules).Any(rule => rule.Pattern.IsMatch(line)), $"No rule catches: {line}");
        string[] allowed =
        [
            "var x = 1.5m;", "var y = Fixed.FromDecimal(0.25m);", "var z = Math.Max(a, b);", "var n = int.Parse(text, CultureInfo.InvariantCulture);", "if (name.StartsWith(prefix, StringComparison.Ordinal)) return;",
            "if (name.StartsWith('*')) return;", "private readonly SortedDictionary<string, int> _table = new(StringComparer.Ordinal);", "var u = name.ToUpperInvariant();", "var h = 0x1F;", "var r = a..b;",
            "var s = value.ToString(CultureInfo.InvariantCulture);", "var o = string.CompareOrdinal(a, b);", "var e = 0x1e5;",
        ];
        foreach (var line in allowed)
        {
            var wrong = SimulationRules.Concat(FeederRules).FirstOrDefault(rule => rule.Pattern.IsMatch(line));
            Check.That(wrong is null, $"A rule wrongly catches: {line} ({wrong?.Name})");
        }
    }
}
