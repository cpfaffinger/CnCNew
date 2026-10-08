using System.Text.RegularExpressions;

/// <summary>
/// Phase 18, D6: the lexical determinism rules. Two rule sets: one for the simulation itself, one for the code that produces its inputs (definitions, map
/// decoding, the bone table). A rule is a regular expression over code lines (strings blanked, comments and raw string blocks removed), so it is a tripwire,
/// not a proof: it cannot see types (a culture-formatted number inside an interpolation hole is invisible to it; <c>Determinism D4</c> runs under hostile
/// cultures for that). Every accepted hit is an explicit exception with its reason; an exception that no longer matches anything fails the test.
/// </summary>
static partial class Tests
{
    /// <param name="Violates">Decides for a line the pattern matched whether it breaks the rule (a rule with allowances); null: every match does.</param>
    /// <param name="Scope">Part of the repository-relative path a file must contain for the rule to apply; null: every scanned file.</param>
    private sealed record SourceRule(string Name, Regex Pattern, Func<string, bool>? Violates = null, string? Scope = null)
    {
        public bool Hits(string line) => Pattern.IsMatch(line) && (Violates is null || Violates(line));
    }

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
    private const string RuleFixedTickRate = "hard-coded tick rate";
    private const string RuleRateLiteral = "number used as a tick rate";
    private const string RuleTickNamedNumber = "tick-named value written as a number";
    private const string RuleTickArithmetic = "bare number in tick arithmetic";
    private const string RuleBareSleep = "bare number returned as a sleep";
    private const string RuleOneTickMinimum = "duration floored at one tick";
    private const string RuleLiteralDraw = "logic random drawn over a literal range";

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

    // ------------------------------------------------------------------------------------------------------------------------------------------------
    // Phase 20, the convention of docs/TICKRATE-MIGRATION.md §1 as rules: no bare tick number in the simulation. A duration is a constant in logic frames
    // of the original (a const whose name ends in Frames, converted with the time service where it is used), or whole seconds (…Seconds), or comes from
    // the catalog. A literal that means order is written with TickOrder.Next. The patterns are the strong ones of docs/TICKRATE-AUDIT.md §8 (P02, P03,
    // P06/N08, P07/N05, P08/N07, P09, P13) narrowed by what the convention allows; P04/N16 (every 30, 60, 300 …) are too loud for a rule and stay a
    // reading aid. Lexical tripwires, like every rule here: a number passed as a positional argument (new Leaf(…, 3, 30, …)) is invisible to them.

    private static readonly Regex TickNamedNumber = new(
        @"(?<name>\b_?\w*(?:[Tt]ick|[Ff]rame|[Ii]nterval|[Dd]elay|[Pp]eriod|[Cc]ooldown)\w*)\s*(?:\{[^}]*\}\s*)?(?<![=!<>+\-*/%&|^])(?:=(?![=>])|:(?!:))\s*\(?(?<value>-?\d[\w.]*(?:\s*[*+/-]\s*[\w.]+)*)");

    /// <summary>
    /// A constant, field, local, property or parameter with Tick, Frame, Interval, Delay, Period or Cooldown in its name that is given a number. Allowed by
    /// rule: 0 and -1 (unset, never); an expression of <c>TickOrder.Next</c> (order, not time); a <c>const</c> whose name ends in <c>Frames</c> (logic frames
    /// of the original – that it reaches the time service is checked separately); <c>MaxTicks</c>/<c>maxTicks</c> (a run budget is a number of steps).
    /// </summary>
    private static bool TickNamedNumberViolates(string line)
    {
        foreach (Match match in TickNamedNumber.Matches(line))
        {
            var name = match.Groups["name"].Value;
            var value = match.Groups["value"].Value.Replace("_", string.Empty, StringComparison.Ordinal).TrimEnd('L');
            if (value is "0" or "-1") continue;
            if (value.Contains("TickOrder.Next", StringComparison.Ordinal)) continue;
            if (name.EndsWith("Frames", StringComparison.Ordinal) && Regex.IsMatch(line, @"\bconst\b")) continue;
            if (name is "MaxTicks" or "maxTicks") continue;
            return true;
        }
        return false;
    }

    private static readonly Regex TickArithmetic = new(
        @"\b\w*[Tt]ick\w*\s*[-+%]\s*(?<value>\d\w*(?:\s*\*\s*[\w.]+)?)|\bnow\s*[-+%]\s*(?<value>\d\w*(?:\s*\*\s*[\w.]+)?)" +
        @"|\b\w*[Tt]ick\w*\s*-\s*[\w.]+\s*(?:>=|<=|>|<|==)\s*(?<value>[1-9]\w*(?:\s*\*\s*[\w.]+)?)|(?:\b_life|\b_age|\bframe\w*)\s*%\s*(?<value>\d+)");

    /// <summary>A tick plus, minus or modulo a number, or a tick difference compared with one. Allowed by rule: a multiple of <c>TickOrder.Next</c>.</summary>
    private static bool TickArithmeticViolates(string line)
    {
        foreach (Match match in TickArithmetic.Matches(line))
            if (!match.Groups["value"].Value.Contains("TickOrder.Next", StringComparison.Ordinal)) return true;
        return false;
    }

    private static readonly SourceRule[] TickLiteralRules =
    [
        // A rate written as a number: a field named like a rate that is given one, and 30 or 900 (30²) as a factor, divisor or modulus.
        new(RuleRateLiteral, new Regex(@"\b\w*(PerSecond|FramesPerSec|TicksPerSec|FrameRate|TickRate|Fps|Hz)\w*\s*=\s*\d|1(\.0)?[dfm]?\s*/\s*30(\.0)?[dfm]?\b|30m?\s*/\s*1000|/\s*33\b|1000\s*/\s*30" +
                                     @"|(?<![\w.])(30|900)[mL]?\s*[*/%]|[*/%]\s*(30|900)[mL]?(?![\w.])|[*/%]\s*Fixed\.FromInt\((30|900)\)|Fixed\.FromInt\((30|900)\)\s*[*/%]|FromTicksPerSecond\(\d")),
        new(RuleTickNamedNumber, TickNamedNumber, TickNamedNumberViolates),
        new(RuleTickArithmetic, TickArithmetic, TickArithmeticViolates),
        // IUpdateModule.Update returns its sleep in ticks: a number there is a bare duration (0 is not a sleep, the scheduler raises it to SleepNone).
        new(RuleBareSleep, new Regex(@"\breturn\s+(world\.Tick\s*\+\s*)?[1-9][\d_]*L?\s*;"), Scope: "/Modules/"),
        new(RuleBareSleep, new Regex(@"\breturn\s+(world\.Tick\s*\+\s*)?[1-9][\d_]*L?\s*;"), Scope: "/Scripting/"),
        // "At least one tick" is a lower bound that halves with the rate. A duration uses AtLeastOneFrame; what stays is named below with its reason.
        new(RuleOneTickMinimum, new Regex(@"Math\.Max\([^;]*\bSleepNone\b|Math\.Max\(\s*[^;,]*Ticks\w*\s*,\s*1L?\s*\)|Math\.Max\(\s*1L?\s*,\s*[^;]*Ticks\b")),
        // GameLogicRandomValue(0, 3) in the reference draws frames; such a draw goes through DrawFrames or is drawn in frames and scaled. A literal range
        // that is left draws something else and says so below.
        new(RuleLiteralDraw, new Regex(@"LogicRandom\.Range\((0|1),\s*\d+\)")),
    ];

    /// <summary>
    /// Constants in logic frames of the original that the lexical check for "is converted" does not see reaching the time service, each with the way it
    /// does. Checked by <see cref="FramesConstantsReachTheTimeService"/>; an entry that is not needed fails the test.
    /// </summary>
    private static readonly (string File, string Constant, string Reason)[] FramesConstantExceptions =
    [
        ("Modules/BuiltIn/CleanupAndSpyModules.cs", "RescanWaitMaxFrames", "upper bound of a draw in frames (GameLogicRandomValue(0, 3)); the drawn value is scaled with OriginalFrames on the next line"),
        ("Modules/BuiltIn/ProjectileModules.cs", "RescanMaxFrames", "upper bound of a draw in frames; the drawn value is scaled with OriginalFrames"),
        ("Skirmish/SkirmishAi.cs", "SupplyLookRetryFrames", "passed to Backoff(key, frames), which scales"),
        ("Skirmish/SkirmishAi.cs", "TunnelEvacuationRetryFrames", "passed to Backoff(key, frames), which scales"),
        ("Skirmish/SkirmishAi.cs", "WarehouseScoutRetryFrames", "passed to Backoff(key, frames), which scales"),
    ];

    /// <summary>The simulation: no floating point at all, no access to anything outside itself.</summary>
    private static readonly SourceRule[] SimulationRules =
    [
        ..CommonRules,
        new(RuleFloatingPoint, new Regex(FloatingPointPattern)),
        new(RuleEnvironment, new Regex(@"\b(File|Directory|Process|Environment|AppContext|AppDomain|RuntimeInformation)\.")),
        // The flag alone does not put the entity into the world's dirty list, and it makes every later MarkDirty return early: the cached hash stays stale
        // until something recomputes the full hash (D8, found on MD_GLA05).
        new(RuleDirtyFlag, new Regex(@"\bDirty\s*=\s*true")),
        // Phase 20: the tick rate is a value of the world. The former constant must not come back, and nothing converts with the static class any more:
        // every conversion that depends on the rate goes through the time service (world.Time, configuration.Time).
        new(RuleFixedTickRate, new Regex(@"\bLogicFramesPerSecond\b|\bSimulationTiming\.(DurationMillisecondsTo\w+|SecondsToTicks|PerSecond\w+|DegreesPerSecond\w+)\(")),
        ..TickLiteralRules,
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

    /// <summary>Phase 20: every number the tick rules accept although no allowance of the convention covers it, with the reason it is not a bare tick count.</summary>
    private static readonly SourceException[] TickLiteralExceptions =
    [
        // -- numbers that look like a rate
        new("Time/SimulationRate.cs", RuleRateLiteral, "public const int OriginalFramesPerSecond = 30;", "the one named 30 of the simulation: the logic frame rate of the original"),
        new("Time/SimulationTiming.cs", RuleRateLiteral, "public const long MillisecondsPerSecond = 1000;", "milliseconds in a second, not a rate"),
        new("Modules/BuiltIn/DockMachine.cs", RuleRateLiteral, "private const long ClearanceTimeoutFrames = 30L * SimulationRate.OriginalFramesPerSecond;", "30 seconds times the frames in a second: a constant in frames"),
        // -- tick-named values that are numbers
        new("Time/SimulationRate.cs", RuleTickNamedNumber, "public const int OriginalFramesPerSecond = 30;", "the definition of the original's frame rate"),
        new("Mission/MissionSession.cs", RuleTickNamedNumber, "int CombatIntervalTicks = 3,", "a switch since phase 15 (0: scripted fire off), never a duration; the name is the key of the mission state"),
        new("Modules/BuiltIn/Phase5Modules.cs", RuleTickNamedNumber, "private long _blockerReplanTick = -1000;", "'never planned', far before any tick; also the default the snapshot omits"),
        new("Templates/TurretInfo.cs", RuleTickNamedNumber, "public const long NeverScanTicks = 9999999;", "the reference's sentinel for 'never' (TurretAIData::TurretAIData), compared and never scaled"),
        new("Testing/MissionObjectiveDriver.cs", RuleTickNamedNumber, "long maxTicks = 4500, int spacing = 15, long settleTicks = 60,", "spacing and settleTicks are logic frames of the original under a historic name, scaled in Run; maxTicks is the budget"),
        new("Testing/MissionScenario.cs", RuleTickNamedNumber, "long settleTicks = 150, long waitTicks = 600)", "logic frames of the original under a historic name, scaled in MissionProbe.Run"),
        new("World/SimulationWorld.cs", RuleTickNamedNumber, "private const int MaxUpdatesPerTick = 50_000_000;", "a storm limit per step, no time"),
        new("World/SimulationWorld.cs", RuleTickNamedNumber, "private const int MaxEventsPerTick = 20_000_000;", "a storm limit per step, no time"),
        // -- tick arithmetic with a number
        // -- lower bounds of one tick that are not durations
        new("Modules/BuiltIn/CleanupAndSpyModules.cs", RuleOneTickMinimum, "world.Tick + Math.Max(durationTicks, SimulationTiming.SleepNone)", "a wake-up must lie in the future; the duration is a sum of catalog durations on the grid and zero is handled before"),
        new("Modules/BuiltIn/CleanupAndSpyModules.cs", RuleOneTickMinimum, "Math.Max(Math.Min(Entity.ReadyTickOf(data.Slot) - world.Tick, reloadWait), SimulationTiming.SleepNone)", "a difference of absolute ticks: sleep until then, at least to the next step"),
        new("Modules/BuiltIn/DockModules.cs", RuleOneTickMinimum, "_nextWake = Math.Max(_depositOnTick, world.Tick + SimulationTiming.SleepNone);", "an absolute tick, at least the next step"),
        new("Modules/BuiltIn/MineModules.cs", RuleOneTickMinimum, "return Math.Max(_nextDeathCheck - now, SimulationTiming.SleepNone);", "a difference of absolute ticks"),
        new("Modules/BuiltIn/Phase5BlockerModules.cs", RuleOneTickMinimum, "return Math.Max(soonest - now, SimulationTiming.SleepNone);", "a difference of absolute ticks"),
        new("Modules/BuiltIn/WorldEffectModules.cs", RuleOneTickMinimum, "return Math.Max(soonest - now, SimulationTiming.SleepNone);", "a difference of absolute ticks"),
        new("Modules/BuiltIn/SpecialAbilityStaging.cs", RuleOneTickMinimum, "var denominator = Math.Max(1, data.PreparationTicks);", "guards a division by zero, no duration"),
        new("Testing/StressScenario.cs", RuleOneTickMinimum, "var remaining = (int)Math.Max(1, _options.Ticks - tick - world.Time.OriginalFrames(DrainReserveFrames));", "a number of steps used as a modulus, at least 1"),
        // -- draws over a literal range that are not durations
        new("Modules/BuiltIn/Bodies.cs", RuleLiteralDraw, "world.LogicRandom.Range(0, 99) < 25", "a percentage"),
        new("Modules/BuiltIn/SlaveModules.cs", RuleLiteralDraw, "Fixed.FromInt(world.LogicRandom.Range(0, 6))", "a direction in whole radians"),
        new("Modules/BuiltIn/SlaveModules.cs", RuleLiteralDraw, "_ = world.LogicRandom.Range(0, 10);", "the reference's draw for the locomotor choice, kept for the stream"),
        new("Modules/BuiltIn/WorldEffectModules.cs", RuleLiteralDraw, "_ = world.LogicRandom.Range(1, 100);", "the reference's draw for the splash sound, kept for the stream"),
    ];

    private static readonly SourceException[] SimulationExceptions =
    [
        // -- ids: the dictionary key hash of a value type; no value and no order is derived from it (Dictionary and HashSet enumerate by slot, not by hash).
        new("Numerics/Fixed.cs", RuleProcessHash, "public override int GetHashCode() => _raw.GetHashCode();", "hash of the raw 64-bit value, the same in every process"),
        new("Templates/ScienceCatalog.cs", RuleProcessHash, "HashCode.Combine(_low, _high)", "key hash of a bit set; used for equality lookups only"),
        new("Templates/UpgradeCatalog.cs", RuleProcessHash, "HashCode.Combine(_low, _high)", "key hash of a bit set; used for equality lookups only"),
        new("World/SimulationWorld.cs", RuleDirtyFlag, "entity.Dirty = true;", "MarkDirty itself: the flag and the list entry together"),
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
        ..TickLiteralExceptions,
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
            var applicable = rules.Where(rule => rule.Scope is null || ("/" + relative).Contains(rule.Scope, StringComparison.Ordinal)).ToArray();
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
                foreach (var rule in applicable)
                {
                    if (!rule.Hits(line)) continue;
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
            "private readonly SortedDictionary<string, int> _table = [];", "var d = (decimal)single;", "var t = MathF.Tan(a);", "var q = Math.Sqrt(2);", "var w = Math.Floor(v);", "Entity.Dirty = true;", "text.AppendLine(line);", "var p = Path.Combine(a, b);", "var n = Environment.NewLine;",
        ];
        foreach (var line in caught)
            Check.That(SimulationRules.Concat(FeederRules).Any(rule => rule.Hits(line)), $"No rule catches: {line}");
        string[] allowed =
        [
            "var x = 1.5m;", "var y = Fixed.FromDecimal(0.25m);", "var z = Math.Max(a, b);", "var n = int.Parse(text, CultureInfo.InvariantCulture);", "if (name.StartsWith(prefix, StringComparison.Ordinal)) return;",
            "if (name.StartsWith('*')) return;", "private readonly SortedDictionary<string, int> _table = new(StringComparer.Ordinal);", "var u = name.ToUpperInvariant();", "var h = 0x1F;", "var r = a..b;",
            "var s = value.ToString(CultureInfo.InvariantCulture);", "var o = string.CompareOrdinal(a, b);", "var e = 0x1e5;",
        ];
        foreach (var line in allowed)
        {
            var wrong = SimulationRules.Concat(FeederRules).FirstOrDefault(rule => rule.Hits(line));
            Check.That(wrong is null, $"A rule wrongly catches: {line} ({wrong?.Name})");
        }
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------------
    // Phase 22: the number of fraction bits of Fixed is a property of the simulation and is written in exactly one place (Fixed.FractionBits). A scale
    // written out somewhere else does not crash when the resolution changes: it is silently wrong by a power of two. So the scale never appears as a
    // number, in no project: neither two to the sixteenth (the resolution until phase 22) nor two to the twenty-fourth (the resolution since), neither
    // as a literal nor as a shift. Outside the simulation nothing uses the scale at all except the one conversion to and from floating point
    // (CnCNew.Presentation.FixedConvert); and no raw value is written as a number in product code. Lexical, like every rule here: the hits that are
    // something else (colour channels, file format tags, hash constants) are named with what they are.

    private const string RuleScaleLiteral = "fixed-point scale written as a number or a shift";
    private const string RuleScaleOutside = "fixed-point scale used outside the simulation";
    private const string RuleRawNumber = "raw fixed-point value written as a number";

    private static readonly SourceRule[] ScaleLiteralRules =
    [
        new(RuleScaleLiteral, new Regex(@"(?<![\w.])(65_?536|16_?777_?216)(?![\d_])|\b0[xX](10{4}|10{6})(?![0-9a-fA-F_])|(<<|>>)=?\s*(16|24)\b")),
    ];

    private static readonly SourceRule[] ScaleOutsideRules =
    [
        new(RuleScaleOutside, new Regex(@"\bFixed\.(ScaleRaw|FractionBits)\b|\bFixedMath\.")),
        new(RuleRawNumber, new Regex(@"\bFromRaw\(\s*-?\d[\d_]*L?\s*\)")),
    ];

    private static readonly SourceException[] ScaleLiteralExceptions =
    [
        // -- bytes of a colour or of a 32-bit word (channel n is shifted by 8 n)
        new("Client.Stride/Layers/Fx/FxBuilder.cs", RuleScaleLiteral, "((uint)b << 16) | ((uint)ToByte(alpha) << 24)", "packs colour channels"),
        new("Client.Stride/Layers/Objects/FlashTint.cs", RuleScaleLiteral, "(Steps << 16) | (Steps << 8) | Steps", "packs three tint steps into a key"),
        new("Client.Stride/Layers/Objects/FlashTint.cs", RuleScaleLiteral, "(Quantise(tint.X) << 16)", "packs three tint steps into a key"),
        new("Client.Stride/Layers/Objects/FlashTint.cs", RuleScaleLiteral, "((key >> 16) & 0xFF)", "unpacks the tint key"),
        new("Client.Stride/Layers/Objects/ModelAsset.cs", RuleScaleLiteral, "((uint)color.Red << 16)", "packs colour channels"),
        new("Client.Stride/Layers/Terrain/PngDump.cs", RuleScaleLiteral, "(byte)(value >> 24)", "big-endian bytes of a PNG chunk word"),
        new("Client.Stride/Layers/Terrain/PngDump.cs", RuleScaleLiteral, "(byte)(value >> 16)", "big-endian bytes of a PNG chunk word"),
        new("Client.Stride/Layers/Terrain/TerrainVertex.cs", RuleScaleLiteral, "((uint)b << 16) | ((uint)a << 24)", "packs colour channels"),
        new("Client.Stride/Layers/Terrain/TerrainVertex.cs", RuleScaleLiteral, "(byte)(packed >> 24)", "the alpha channel of a packed colour"),
        new("Client.Stride/Layers/Terrain/TerrainVertex.cs", RuleScaleLiteral, "((uint)alpha << 24)", "the alpha channel of a packed colour"),
        new("Presentation/Objects/FrameProjector.cs", RuleScaleLiteral, "(byte)(argb >> 16)", "the red channel of a house colour"),
        new("Audio.Tests/Tests.Decoding.cs", RuleScaleLiteral, "(byte)(value >> 16), (byte)(value >> 24)", "little-endian bytes of a test word"),
        // -- file formats
        new("Audio/Wav/WavReader.cs", RuleScaleLiteral, "((sbyte)data[at + 2] << 16)", "the top byte of a 24-bit PCM sample"),
        new("Formats/LegacyAssetReaders.cs", RuleScaleLiteral, "packed[2] << 16 | packed[3] << 8 | packed[4]", "a 24-bit length of the RefPack header"),
        new("Ui/Text/CsfFile.cs", RuleScaleLiteral, "public const uint FileId", "a four-character tag of the CSF format"),
        new("Ui/Text/CsfFile.cs", RuleScaleLiteral, "public const uint LabelId", "a four-character tag of the CSF format"),
        new("Ui/Text/CsfFile.cs", RuleScaleLiteral, "public const uint StringId", "a four-character tag of the CSF format"),
        new("Ui/Text/CsfFile.cs", RuleScaleLiteral, "public const uint StringWithSpeechId", "a four-character tag of the CSF format"),
        new("W3D/W3dBinary.cs", RuleScaleLiteral, "(major << 16) | minor", "a W3D version word (major in the upper half)"),
        new("W3D/W3dMesh.cs", RuleScaleLiteral, "(Attributes >> 16) & 0xffu", "a field of the W3D mesh attributes"),
        new("W3D/W3dTextureMapper.cs", RuleScaleLiteral, "(attributes >> 16) & 0xff", "a field of the W3D vertex material attributes"),
        // -- other numbers that happen to be two to the sixteenth or use these shifts
        new("Data/World/SingleBits.cs", RuleScaleLiteral, "((exponent * 19728) >> 16)", "log10(2) as 19728 / 2^16 in the exact decimal expansion of a single"),
        new("Presentation/Input/CommandButtons.cs", RuleScaleLiteral, "NotQueueable = 0x10000,", "a bit of the command button options"),
        new("SimTool/PerfCommand.cs", RuleScaleLiteral, "private ulong[] _words = new ulong[65536];", "the first size of a buffer"),
        new("Data.Tests/Program.cs", RuleScaleLiteral, "9999999.5f, 16777216f, 16777217f,", "singles around the end of the exact integers (2^24), test values of the exact decimal expansion"),
        new("Game.Tests/DeterminismTests.cs", RuleScaleLiteral,"((int)((state >> 16) & 0xFFFF) - 0x8000) / 256f", "bits of a test random number"),
        new("Simulation.Tests/Tests.Indexes.cs", RuleScaleLiteral, "dice.Next(1 << 24) * (long)(1 + dice.Next(1 << 6))", "the range of a test draw"),
        new("Simulation.Tests/Tests.Indexes.cs", RuleScaleLiteral, "((long)dice.Next(1 << 24) << 39)", "the range of a test draw"),
        // -- the implementation with 16 fraction bits as it was before phase 22, kept word for word as the oracle of the 16-bit path
        new("Simulation.Tests/Tests.FixedResolution.cs", RuleScaleLiteral, "private const long One = 65536;", "the 16-bit oracle"),
        new("Simulation.Tests/Tests.FixedResolution.cs", RuleScaleLiteral, "(BigInteger)a << 16", "the 16-bit oracle"),
        new("Simulation.Tests/Tests.FixedResolution.cs", RuleScaleLiteral, "vx <<= 16;", "the 16-bit oracle"),
        new("Simulation.Tests/Tests.FixedResolution.cs", RuleScaleLiteral, "vy <<= 16;", "the 16-bit oracle"),
        new("Simulation.Tests/Tests.FixedResolution.cs", RuleScaleLiteral, "Vector(y * 65536 * 7, x * 65536 * 7);", "vectors in whole units of the 16-bit oracle"),
    ];

    private static readonly SourceException[] ScaleOutsideExceptions =
    [
        new("Presentation/FixedConvert.cs", RuleScaleOutside, "public static double ToDouble(Fixed value) => value.Raw / (double)Fixed.ScaleRaw;", "the one conversion to floating point"),
        new("Presentation/FixedConvert.cs", RuleScaleOutside, "public static Fixed FromDouble(double value)", "the one conversion from floating point"),
        new("Presentation/FixedConvert.cs", RuleScaleOutside, "public static Fixed FromSingleTruncated(float value)", "the same conversion, cut instead of rounded"),
        new("SimTool/PathBench.cs", RuleScaleOutside, "Fixed Coordinate(int cells)", "a random raw coordinate inside the map for the path benchmark: every raw value below cells * cell size"),
    ];

    private static IEnumerable<string> SourceFilesUnder(string root, params string[] directories)
        => directories.SelectMany(directory => Directory.EnumerateFiles(Path.Combine(root, directory), "*.cs", SearchOption.AllDirectories));

    /// <summary>The third rule set: the scale of the fixed-point numbers is written once.</summary>
    public static void FixedScaleSourceRules()
    {
        var root = RepositoryRoot();
        var violations = ScanSources(root, SourceFilesUnder(root, "src", "tools", "tests"), ScaleLiteralRules, ScaleLiteralExceptions);
        Check.That(violations.Count == 0, $"The fixed-point scale as a number ({violations.Count}):\n  " + string.Join("\n  ", violations));

        var simulation = Path.Combine(root, "src", "CnCNew.Simulation") + Path.DirectorySeparatorChar;
        var outside = SourceFilesUnder(root, "src", "tools").Where(file => !file.StartsWith(simulation, StringComparison.Ordinal));
        violations = ScanSources(root, outside, ScaleOutsideRules, ScaleOutsideExceptions);
        Check.That(violations.Count == 0, $"The fixed-point scale outside the simulation ({violations.Count}):\n  " + string.Join("\n  ", violations));
        // Inside the simulation a raw number is forbidden too; the scale itself is at home there.
        violations = ScanSources(root, SourceFilesUnder(root, Path.Combine("src", "CnCNew.Simulation")), [ScaleOutsideRules[1]], []);
        Check.That(violations.Count == 0, $"Raw fixed-point values written as numbers in the simulation ({violations.Count}):\n  " + string.Join("\n  ", violations));

        (string Line, SourceRule[] Rules)[] caught =
        [
            ("var x = entity.X.Raw / 65536f;", ScaleLiteralRules), ("var raw = (long)(value * 65536);", ScaleLiteralRules), ("vx <<= 16;", ScaleLiteralRules), ("var whole = raw >> 16;", ScaleLiteralRules),
            ("const long One = 0x10000;", ScaleLiteralRules), ("var y = raw / 16777216.0;", ScaleLiteralRules), ("var z = units << 24;", ScaleLiteralRules), ("var w = 65_536L * units;", ScaleLiteralRules),
            ("var f = (float)(value.Raw / (double)Fixed.ScaleRaw);", ScaleOutsideRules), ("var s = raw >> Fixed.FractionBits;", ScaleOutsideRules), ("var e = Fixed.FromRaw(1024);", ScaleOutsideRules),
            ("var p = Fixed.FromRaw(-34315L);", ScaleOutsideRules),
        ];
        foreach (var (line, rules) in caught)
            Check.That(rules.Any(rule => rule.Hits(line)), $"No scale rule catches: {line}");
        string[] allowed =
        [
            "var f = FixedConvert.ToSingle(value);", "var e = Fixed.FromFraction(1, 64);", "var mask = 0x100000;", "var a = value << 8;", "var b = Fixed.FromRaw(reader.ReadInt64());", "var c = 165536;",
            "var d = raw >> 32;", "var g = FixedTrig.PiFraction(1, 6);", "var h = Fixed.Epsilon;", "var i = 0x1000;",
        ];
        foreach (var line in allowed)
        {
            var wrong = ScaleLiteralRules.Concat(ScaleOutsideRules).FirstOrDefault(rule => rule.Hits(line));
            Check.That(wrong is null, $"A scale rule wrongly catches: {line} ({wrong?.Name})");
        }
    }

    /// <summary>Strips what the rules do not read from a source line: string contents and the trailing comment.</summary>
    private static string CodeOf(string raw)
    {
        var line = Regex.Replace(raw, "\"(\\\\.|[^\"\\\\])*\"", "\"\"");
        var comment = line.IndexOf("//", StringComparison.Ordinal);
        return comment >= 0 ? line[..comment] : line;
    }

    /// <summary>
    /// The other half of the convention: a constant in logic frames of the original must reach the time service. Lexically that is a line in which the
    /// constant stands inside <c>OriginalFrames(…)</c>, is the fallback of <c>ModuleConfigReader.Ticks(…)</c>, or defines another constant in frames; in the
    /// type of the constant (its file and the other files of a partial class), or anywhere in the simulation for a public one. Returns the constants for which no such line exists and that
    /// <see cref="FramesConstantExceptions"/> does not name, and the exceptions that were not needed.
    /// </summary>
    private static List<string> FramesConstantsReachTheTimeService(string source)
    {
        var files = Directory.EnumerateFiles(source, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .Select(file => (Relative: Path.GetRelativePath(source, file).Replace('\\', '/'), Lines: File.ReadLines(file).Select(CodeOf).ToArray())).ToArray();
        var used = new bool[FramesConstantExceptions.Length];
        var problems = new List<string>();
        var declared = 0;
        foreach (var (relative, lines) in files)
        {
            foreach (var line in lines)
            {
                var declaration = Regex.Match(line, @"\b(?<access>public|internal|private|protected)?\s*const\s+(?:int|long)\s+(?<name>\w+Frames)\s*=");
                if (!declaration.Success) continue;
                declared++;
                var name = declaration.Groups["name"].Value;
                var converted = new Regex($@"OriginalFrames\([^;]*\b{name}\b|\.Ticks\([^;]*,\s*{name}\)|\bconst\s+(?:int|long)\s+\w+Frames\s*=[^;]*\b{name}\b");
                // A private constant is converted in its own type: the file, or the other files of a partial class (SimulationWorld.*.cs, ScriptEngine.*.cs).
                var scope = declaration.Groups["access"].Value == "public" ? files : files.Where(file => PartialKey(file.Relative) == PartialKey(relative));
                var reaches = scope.Any(file => file.Lines.Any(candidate => converted.IsMatch(candidate)));
                var exception = Array.FindIndex(FramesConstantExceptions, value => value.File == relative && value.Constant == name);
                if (exception >= 0)
                {
                    if (reaches) problems.Add($"exception without a need: {relative} {name} is seen reaching the time service");
                    else used[exception] = true;
                }
                else if (!reaches) problems.Add($"{relative}: the constant {name} is in logic frames of the original but no line converts it with the time service");
            }
        }
        for (var index = 0; index < used.Length; index++)
            if (!used[index] && !problems.Any(problem => problem.Contains(FramesConstantExceptions[index].Constant, StringComparison.Ordinal)))
                problems.Add($"exception without a constant: {FramesConstantExceptions[index].File} {FramesConstantExceptions[index].Constant}");
        Check.That(declared >= 60, $"The scan found the constants in frames ({declared}).");
        return problems;
    }

    private static string PartialKey(string relative)
    {
        var slash = relative.LastIndexOf('/');
        var dot = relative.IndexOf('.', slash + 1);
        return relative[..dot];
    }

    /// <summary>The tick rules themselves: what they catch and what the convention lets through.</summary>
    private static void TickLiteralRulesCatchAndAllow()
    {
        (string Line, string Rule)[] caught =
        [
            ("private const long ThinkInterval = 15;", RuleTickNamedNumber), ("private const long RetryTicks = 300;", RuleTickNamedNumber), ("private long _nextTick = 5;", RuleTickNamedNumber),
            ("var scanDelay = cond ? data.ScanDelayTicks : 30;", RuleTickNamedNumber), ("public sealed record Options(long GraceTicks = 300);", RuleTickNamedNumber), ("var framesToWait = 15;", RuleTickNamedNumber),
            ("private int HealRateFrames = 3;", RuleTickNamedNumber), ("_cooldownPeriod = 2 * 30;", RuleTickNamedNumber),
            ("if (world.Tick - _last >= 30) Replan();", RuleTickArithmetic), ("world.WakeAt(Entity, this, tick + 1);", RuleTickArithmetic), ("if (tick % 15 == 0) Think();", RuleTickArithmetic),
            ("_transferTick = now + 2;", RuleTickArithmetic), ("if (_life % 30 == 29) Propagate();", RuleTickArithmetic), ("var remaining = head.RemainingTicks - 1;", RuleTickArithmetic),
            ("var perTick = amount / 30;", RuleRateLiteral), ("var speed = perSecond / Fixed.FromInt(30);", RuleRateLiteral), ("private const int FramesPerSecond = 30;", RuleRateLiteral),
            ("var gravity = value / 900m;", RuleRateLiteral), ("var rate = SimulationRate.FromTicksPerSecond(60);", RuleRateLiteral), ("var ms = ticks * 1000 / 30;", RuleRateLiteral),
            ("_readyTick = world.Tick + Math.Max(data.DelayTicks, SimulationTiming.SleepNone);", RuleOneTickMinimum), ("Add(index, cost, Math.Max(totalTicks, 1));", RuleOneTickMinimum),
            ("_nextScan = world.LogicRandom.Range(0, 3);", RuleLiteralDraw),
        ];
        foreach (var (line, rule) in caught)
            Check.That(TickLiteralRules.Any(value => value.Name == rule && value.Hits(line)), $"The rule '{rule}' does not catch: {line}");
        Check.That(TickLiteralRules.Where(value => value.Name == RuleBareSleep).All(value => value.Hits("        return 15;") && value.Hits("return world.Tick + 30;") && !value.Hits("return 0;") && !value.Hits("return SimulationTiming.SleepNone;")),
            "A bare number as a sleep is caught, 0 and the named sleeps are not.");
        string[] allowed =
        [
            "private const long ThinkIntervalFrames = 15;", "private const long RecentFireFrames = 2 * SimulationRate.OriginalFramesPerSecond;",
            "private long _detectedUntilTick = -1;", "_returnTick = 0;", "public sealed record Options(long MaxTicks = 0, int PresentationLatencyTicks = 0);", "long maxTicks = 4500,",
            "private const long FramesUnpulledLongEnoughToUnhitch = 2 * TickOrder.Next;", "_transferTick = now + 2 * TickOrder.Next;", "world.WakeAt(Entity, this, tick + TickOrder.Next);",
            "if (world.Tick - arrival.Tick > 2 * TickOrder.Next) Drop();", "if (tick % _thinkInterval != 0) return;", "if (world.Tick - _last >= world.Time.OriginalFrames(ReplanFrames)) Replan();",
            "_nextWake = world.Tick + world.Time.AtLeastOneFrame(data.DelayTicks);", "return world.Time.OriginalFrames(HealRateFrames);", "var amount = world.Time.PerSecondToPerTick(body.MaxHealth * ratio);",
            "if (state.OrderedTick == 0) return;", "var wait = world.LogicRandom.Range(0, RescanMaxFrames);", "var ring = radius + Fixed.FromInt(30);", "if (DistanceSquared(a, b) < Fixed.FromInt(900)) return;",
            "z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;", "private const long SparkEverySteps = 2;", "private const long SupplyScanSeconds = 10;",
        ];
        foreach (var line in allowed)
        {
            var wrong = TickLiteralRules.FirstOrDefault(rule => rule.Hits(line));
            Check.That(wrong is null, $"A tick rule wrongly catches: {line} ({wrong?.Name})");
        }
    }
}
