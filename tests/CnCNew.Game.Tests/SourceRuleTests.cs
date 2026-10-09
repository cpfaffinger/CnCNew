using System.Text.RegularExpressions;

namespace CnCNew.Game.Tests;

/// <summary>
/// The tick convention outside the simulation as a source test (docs/handoffs/HANDOFF-PHASE21.md, item 10; the simulation's own rules are <c>SourceRules</c> in
/// <c>CnCNew.Simulation.Tests</c>). In <c>CnCNew.Game</c>, <c>CnCNew.Presentation</c>, <c>CnCNew.Ui</c> and <c>CnCNew.Client.Stride</c> nothing names a rate:
/// <list type="bullet">
/// <item><description>the original frame (1/30 s, the unit of the original's client content) is <c>CnCNew.Data.OriginalFrame.PerSecond</c> and nothing else;</description></item>
/// <item><description>the simulation's rate comes from a world or from the catalog (<c>world.Time</c>, <c>GameData.Rate</c>), never from a constant.</description></item>
/// </list>
/// The patterns are the rate patterns of the inventory (docs/TICKRATE-AUDIT.md §8: P01, P02, P03 and the literal forms of 60 and 120). Every exception is named here with
/// its reason, and an exception that no longer matches a line fails the test, so the list cannot go stale.
/// </summary>
internal static class SourceRuleTests
{
    private static readonly string[] Projects = ["CnCNew.Game", "CnCNew.Presentation", "CnCNew.Ui", "CnCNew.Client.Stride"];

    private sealed record Rule(string Name, Regex Pattern, string Instead);

    private static readonly Rule[] Rules =
    [
        new("a simulation rate named as a constant", new Regex(@"SimulationRate\.(OriginalFramesPerSecond|Hz30|Hz60|Hz120|Original)\b"),
            "read the rate from the world or the catalog (world.Time, GameData.Rate); the original frame is CnCNew.Data.OriginalFrame.PerSecond"),
        new("a simulation rate built from a literal", new Regex(@"FromTicksPerSecond\(\s*\d"), "take the rate from the session (GameData.Load(rate:), the client's --tick-rate)"),
        new("a frame or tick rate constant of its own", new Regex(@"\b\w*(FramesPerSec|TicksPerSec|TicksPerFrame|FrameRate|TickRate|Fps|LogicFrames|LogicTicks)\w*\s*=\s*\d"),
            "define it from CnCNew.Data.OriginalFrame.PerSecond (original frame) or read world.Time (simulation rate)"),
        new("the literal 30 used as a rate", new Regex(@"1(\.0)?[dfm]?\s*/\s*30(\.0)?[dfm]?\b|[/*]\s*30(\.0)?[fdm]?\b|\b30(\.0)?[fdmL]?\s*[*/]|1000\s*/\s*30|/\s*33\b|33\.33|0\.0333"),
            "write OriginalFrame.PerSecond for the original frame, or convert with world.Time"),
        new("the literal 60 or 120 used as a rate", new Regex(@"1(\.0)?[dfm]?\s*/\s*(60|120)(\.0)?[dfm]?\b|/\s*(60|120)(\.0)?d\b"), "read the rate from the world (world.Time.TicksPerSecond)"),
    ];

    /// <summary>The named exceptions: file (path suffix), a text the line contains, and why the line is not a rate.</summary>
    private static readonly (string File, string Contains, string Reason)[] Exceptions =
    [
        ("CnCNew.Game/Data/ProductRate.cs", "public const int TicksPerSecond =",
            "the one place outside the simulation that names a simulation rate: the product rate, the default of a session whose host names none (GameData.DefaultSessionRate)"),
        ("CnCNew.Game/Hud/HudOverlayRenderer.cs", "clockMilliseconds / 30 + icon.Entity.Value % CrossFrames",
            "30 milliseconds of wall time per picture of the repair cross: a duration, not a rate"),
        ("CnCNew.Client.Stride/Program.cs", "_renderedFrames / 60d",
            "the model viewer's smoke clock: 1/60 s of animation time per rendered frame; no simulation and no game time is involved"),
    ];

    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(SourceRuleTests));
        harness.Run("No rate constant outside the simulation except the original frame (CnCNew.Data.OriginalFrame.PerSecond)", NoRateConstants);
        harness.Run("The rules find what they forbid and the exceptions are narrow", RulesAreSharp);
        return harness.Failures;
    }

    private static string RepositoryRoot()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "CnCNew.slnx"))) root = Path.GetDirectoryName(root);
        TestHarness.Assert(root is not null, "the repository root (CnCNew.slnx) was not found above the test's directory; run the tests inside the tree");
        return root!;
    }

    /// <summary>The code of a line: without a line comment and without the contents of string literals (a path or a message may contain any number).</summary>
    internal static string CodeOf(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('*') || trimmed.StartsWith("/*", StringComparison.Ordinal)) return string.Empty;
        var code = new System.Text.StringBuilder(line.Length);
        var inString = false;
        for (var index = 0; index < line.Length; index++)
        {
            var c = line[index];
            if (inString)
            {
                if (c == '\\') { index++; continue; }
                // An interpolation hole is code again.
                if (c == '{' && index + 1 < line.Length && line[index + 1] != '{')
                {
                    var end = line.IndexOf('}', index);
                    if (end < 0) end = line.Length - 1;
                    code.Append(line, index, end - index + 1);
                    index = end;
                    continue;
                }
                if (c == '"') { inString = false; code.Append('"'); }
                continue;
            }
            if (c == '"') { inString = true; code.Append('"'); continue; }
            if (c == '/' && index + 1 < line.Length && line[index + 1] == '/') break;
            code.Append(c);
        }
        return code.ToString();
    }

    private static void NoRateConstants()
    {
        var root = RepositoryRoot();
        var findings = new List<string>();
        var used = new bool[Exceptions.Length];
        var files = 0;
        foreach (var project in Projects)
        {
            var directory = Path.Combine(root, "src", project);
            TestHarness.Assert(Directory.Exists(directory), $"the project directory {directory} exists");
            foreach (var file in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories).OrderBy(path => path, StringComparer.Ordinal))
            {
                var relative = Path.GetRelativePath(Path.Combine(root, "src"), file).Replace('\\', '/');
                if (relative.Contains("/bin/", StringComparison.Ordinal) || relative.Contains("/obj/", StringComparison.Ordinal)) continue;
                files++;
                var lines = File.ReadAllLines(file);
                for (var index = 0; index < lines.Length; index++)
                {
                    var code = CodeOf(lines[index]);
                    if (code.Length == 0) continue;
                    foreach (var rule in Rules)
                    {
                        if (!rule.Pattern.IsMatch(code)) continue;
                        var exception = Array.FindIndex(Exceptions, entry => relative.EndsWith(entry.File, StringComparison.Ordinal) && lines[index].Contains(entry.Contains, StringComparison.Ordinal));
                        if (exception >= 0) { used[exception] = true; continue; }
                        findings.Add($"{relative}({index + 1}): {rule.Name} - {rule.Instead}: {lines[index].Trim()}");
                    }
                }
            }
        }

        Console.WriteLine($"       {files} source files of {string.Join(", ", Projects)}; {Rules.Length} rules; {Exceptions.Length} named exceptions");
        TestHarness.Assert(files > 200, $"the four projects were read ({files} files)");
        TestHarness.Assert(findings.Count == 0, $"{findings.Count} rate constant(s) outside the simulation:\n  " + string.Join("\n  ", findings.Take(40)));
        for (var index = 0; index < Exceptions.Length; index++)
            TestHarness.Assert(used[index], $"the exception for '{Exceptions[index].Contains}' in {Exceptions[index].File} no longer matches a line: remove it");
    }

    private static void RulesAreSharp()
    {
        // What the game layer used to contain (docs/TICKRATE-AUDIT.md §6): each of these must be caught.
        string[] forbidden =
        [
            "private const double TickSeconds = 1.0 / SimulationRate.OriginalFramesPerSecond;",
            "public const int HealingIconTicks = 3 * 30;",
            "private const int LogicTicksPerSecond = 30;",
            "public const float FramesPerSecond = 30f;",
            "public const int LogicFramesPerSecond = 30;",
            "private const long UnderAttackFrames = 30 * 10;",
            "live.Advance(1.0 / 30 + 1e-6);",
            "var blend = Math.Pow(x, deltaSeconds * 30.0);",
            "var rate = SimulationRate.Hz60;",
            "var world = Create(SimulationRate.FromTicksPerSecond(60));",
            "var seconds = (ready - tick) / 30;",
            "var step = 1.0 / 60;",
            "public const float RiverVScrollPerSecond = 0.002f * 30f;",
            "var frames = ms * 30 / 1000m;",
        ];
        foreach (var line in forbidden)
            TestHarness.Assert(Rules.Any(rule => rule.Pattern.IsMatch(CodeOf(line))), $"the rules catch: {line}");

        // What must stay possible.
        string[] allowed =
        [
            "private const float FrameRate = OriginalFrame.PerSecond;",
            "public const int HealingIconFrames = 3 * OriginalFrame.PerSecond;",
            "_tickSeconds = 1.0 / world.Time.TicksPerSecond;",
            "var text = $\"{seconds / 60}:{seconds % 60:00}\";",
            "private const float PixelsPerSecond = 40f;",
            "new WndColor(200, 200, 30, 255)",
            "private const int End = 30;",
            "// one tick at 30 Hz is 1.0 / 30 s",
            "Console.WriteLine(\"runs at 1.0 / 30 s\");",
            "var sector = hue / 60f;",
        ];
        foreach (var line in allowed)
            TestHarness.Assert(!Rules.Any(rule => rule.Pattern.IsMatch(CodeOf(line))), $"the rules leave alone: {line}");
        TestHarness.Assert(CodeOf("var a = 1; // 30 * 2") == "var a = 1; " && CodeOf("    /// <c>1.0 / 30</c>").Length == 0, "comments are not code");
        TestHarness.Assert(CodeOf("Print($\"x {value / 30} y\");").Contains("value / 30", StringComparison.Ordinal), "an interpolation hole is code");
    }
}
