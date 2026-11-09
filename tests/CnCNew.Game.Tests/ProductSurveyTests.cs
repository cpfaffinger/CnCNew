using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using CnCNew.Game.Analysis;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Game.Shell;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Testing;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Skins;

namespace CnCNew.Game.Tests;

/// <summary>
/// The survey of the whole product as a player would use it, by machine (docs/handoffs/HANDOFF-PHASE64.md item 5; <c>scripts/check-product-survey.ps1</c> runs it with the
/// parts that need a window). It reports what a program finds broken and fails on a finding that is not in <c>baselines/product-survey-known.txt</c>:
/// <list type="bullet">
/// <item>Menus: every screen both skins show, reached by clicking every button, in four sizes and at two text sizes (<see cref="ProductSurveyShell"/>).</item>
/// <item>Skirmish: a person by script through the AI, advanced and debug options, a match, the in-game menu; the structure groups of a real match.</item>
/// <item>Matches: whole skirmishes of the built-in AI in each tactics profile at the game speeds 1x and 3x - they end without an exception, every player builds and
/// collects, a frame costs what a frame may, and the match plays again from its recording to the same state.</item>
/// </list>
/// The report is written to <c>CNCNEW_SURVEY_REPORT</c> (outside the repository). <c>CNCNEW_SURVEY_MATCH_MINUTES</c> sets the length of a match (default 8 game minutes).
/// </summary>
internal static class ProductSurveyTests
{
    public static int Run(string[] args)
    {
        var index = Array.IndexOf(args, "--content-dir");
        var contentDir = index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        var harness = new TestHarness(nameof(ProductSurveyTests));
        if (contentDir is null)
        {
            Console.WriteLine("SKIP ProductSurveyTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }
        var data = GameData.Load(contentDir, rate: CnCNew.Tests.TestRate.Default);
        var report = ProductSurveyReport.Instance;
        harness.Run("Menus: every screen of both skins, clicked through, in four sizes and two text sizes", () => Menus(data, report));
        harness.Run("More options: the hint above the hotkey list fits its label in both skins at every size and the text sizes 100 and 125 %", () => MoreOptionsHintFits(data));
        harness.Run("Drop-down lists stay inside the screen: the army list of the skirmish setup at 800x600 and 150 % text, in both skins", () => DropDownStaysOnScreen(data));
        harness.Run("Skirmish: a person by script through the AI, advanced and debug options, a match and the in-game menu, in both skins", () => Skirmish(data, report));
        harness.Run("Skirmish: structure groups and subgroups of a real match", () => StructureGroups(data, report));
        harness.Run("Matches: a skirmish of each tactics profile at 1x and 3x ends, builds, collects, costs little and plays again", () => Matches(data, report));
        var path = System.Environment.GetEnvironmentVariable("CNCNEW_SURVEY_REPORT") is { Length: > 0 } given ? given : Path.Combine(Path.GetTempPath(), "cncnew-product-survey.txt");
        var text = report.Write(path);
        Console.WriteLine($"Product survey report: {path}");
        harness.Run("The survey found nothing broken that is not known", () =>
        {
            foreach (var line in report.UnknownLines().Take(40)) Console.WriteLine($"       FOUND {line}");
            TestHarness.Assert(report.Unknown == 0, $"{report.Unknown} finding(s) ({report.Known} known); see {path}");
        });
        _ = text;
        return harness.Failures;
    }

    private static void MoreOptionsHintFits(GameData data)
    {
        var root = Root("hint");
        foreach (var skin in new[] { UiSkin.Original, UiSkin.Modern })
            foreach (var (width, height) in ProductSurveyShell.Sizes)
                foreach (var text in new[] { 100, 125 })
                {
                    var shell = ProductSurveyShell.NewShell(data, new ProductSurveyShell.Config(skin, width, height, text), root);
                    ShellTestInput.Click(shell, "MainMenu.wnd:ButtonOptions");
                    ShellTestInput.Click(shell, CnCNew.Game.Shell.Screens.OptionsScreen.MoreOptionsButton);
                    var tree = shell.CurrentScreen.Tree;
                    var label = tree.FindByName("MoreOptions.wnd:HotkeysLabel")!;
                    var overflow = CnCNew.Ui.Skins.UiReadability.Audit(tree, shell.BuildDrawList()).Overflows.Where(item => item.Window == label.Name && !item.Measure.Contains("(1 lines)", StringComparison.Ordinal)).ToArray();
                    TestHarness.Assert(overflow.Length == 0, $"{skin} {width}x{height} text {text} %: \"{label.Text?.Value}\" overflows its label ({string.Join("; ", overflow.Select(item => item.Measure))})");
                }
        Delete(root);
    }

    private static void DropDownStaysOnScreen(GameData data)
    {
        var root = Root("dropdown");
        foreach (var skin in new[] { UiSkin.Original, UiSkin.Modern })
        {
            var config = new ProductSurveyShell.Config(skin, 800, 600, 150);
            var shell = ProductSurveyShell.NewShell(data, config, root);
            ShellTestInput.Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
            ShellTestInput.Click(shell, "MainMenu.wnd:ButtonSkirmish");
            var tree = shell.CurrentScreen.Tree;
            foreach (var name in new[] { "ComboBoxPlayerTemplate1", "ComboBoxStartingCash", "ComboBoxColor1" })
            {
                var combo = tree.FindByName("SkirmishGameOptionsMenu.wnd:" + name)!;
                var button = CnCNew.Ui.Runtime.UiGadgetGeometry.ComboButton(combo);
                ShellTestInput.ClickAt(shell, button.CenterX, button.CenterY);
                TestHarness.Assert(tree.OpenComboBox == combo, $"{skin} {name} opens");
                var drop = CnCNew.Ui.Runtime.UiGadgetGeometry.ComboDropDown(combo);
                TestHarness.Assert(drop.X >= 0 && drop.Y >= 0 && drop.Right <= 800 && drop.Bottom <= 600, $"{skin} {name}: the opened list lies inside the screen ({drop})");
                var rows = CnCNew.Ui.Runtime.UiGadgetGeometry.ComboVisibleRows(combo);
                TestHarness.Assert(rows >= 1 && rows <= combo.ComboBox!.Entries.Count, $"{skin} {name}: {rows} of {combo.ComboBox!.Entries.Count} rows shown");
                var rowHeight = CnCNew.Ui.Runtime.UiGadgetGeometry.RowHeight(combo);
                ShellTestInput.ClickAt(shell, drop.CenterX, drop.Bottom - rowHeight / 2 - 1);
                TestHarness.Assert(tree.OpenComboBox is null, $"{skin} {name}: a click on the last shown row picks it");
            }
        }
        Delete(root);
    }

    private static string Root(string name)
    {
        var directory = Path.Combine(Path.GetTempPath(), "cncnew-survey-" + Guid.NewGuid().ToString("N"), name);
        Directory.CreateDirectory(directory);
        return directory;
    }

    private static void Delete(string root)
    {
        try { Directory.Delete(Path.GetDirectoryName(root)!, recursive: true); }
        catch (IOException) { }
    }

    // ------------------------------------------------------------------------------------------------------------------------------- menus

    private static void Menus(GameData data, ProductSurveyReport report)
    {
        var root = Root("menus");
        report.Note(ProductSurveyShell.Measure is null ? "- text measure: the headless shell's synthetic widths (no GDI on this system)" : "- text measure: GDI, the font the client draws with");
        var watch = Stopwatch.StartNew();
        foreach (var skin in new[] { UiSkin.Original, UiSkin.Modern })
        {
            var screens = ProductSurveyShell.Explore(data, skin, root, report);
            foreach (var (width, height) in ProductSurveyShell.Sizes)
                foreach (var text in new[] { 100, 150 })
                    ProductSurveyShell.Audit(data, new ProductSurveyShell.Config(skin, width, height, text), screens, root, report);
            report.Note($"- menus [{skin.Id}]: audited in 4 sizes x 2 text sizes; {screens.Count} screens: {string.Join(", ", screens.Select(screen => screen.Key).Distinct())}");
        }
        Console.WriteLine($"       menus survey: {watch.Elapsed.TotalSeconds:F0} s");
        Delete(root);
    }

    // --------------------------------------------------------------------------------------------------------------------------- skirmish

    private static void Skirmish(GameData data, ProductSurveyReport report)
    {
        foreach (var skin in new[] { UiSkin.Original, UiSkin.Modern })
            foreach (var (script, name) in new[] { ("scripts/shell/survey-skirmish.txt", "options and match"), ("scripts/shell/ingame-menu-person.txt", "in-game menu") })
                SkinPins.With(skin, () =>
                {
                    var root = Root("skirmish");
                    var shell = GameShell.Create(data, new FixedShellEnvironment(root), viewport: new UiViewport(1280, 720), skin: skin);
                    shell.SkirmishMatchSeconds = 60;
                    var person = ShellScript.Load(script);
                    var host = new InGameMenuTests.Host(data, shell, person);
                    host.Attach();
                    var watch = Stopwatch.StartNew();
                    try
                    {
                        for (var frame = 0; frame < 60_000 && !person.IsFinished; frame++) host.Frame(0.05);
                    }
                    catch (Exception exception)
                    {
                        report.Add($"script-throws|{skin.Id}|{name}", "Skirmish", $"[{skin.Id}] the person's script \"{name}\" throws {exception.GetType().Name}: {exception.Message}", $"scripts/check-product-survey.ps1 (script {script})");
                    }
                    if (!person.IsFinished || person.Failed)
                    {
                        var failure = person.Report().FirstOrDefault(line => line.Contains("FAILED", StringComparison.Ordinal)) ?? $"stopped after {person.StepsDone}/{person.StepCount} steps";
                        report.Add($"script-failed|{skin.Id}|{name}", "Skirmish", $"[{skin.Id}] the person's script \"{name}\" does not run through: {failure}", $"pwsh scripts/check-product-survey.ps1 (script {script})");
                    }
                    else if (name == "options and match" && host.FirstSetup is { } setup)
                    {
                        // What the options chose must have reached the match.
                        if (setup.Ai.IsDefaultOrEmpty || setup.AiOf(1).Tactics != CnCNew.Simulation.Skirmish.AiTactics.Rush || setup.AiOf(1).Difficulty != CnCNew.Simulation.Scripting.ScriptDifficulty.Hard)
                            report.Add($"ai-options-lost|{skin.Id}", "Skirmish", $"[{skin.Id}] the AI options (rush, hard) did not reach the match (got {(setup.Ai.IsDefaultOrEmpty ? "no list" : setup.AiOf(1).ToString())})", "scripts/shell/survey-skirmish.txt");
                        if (setup.Debug.BuildTimePercent == MatchDebugOptions.NormalBuildTime)
                            report.Add($"debug-options-lost|{skin.Id}", "Skirmish", $"[{skin.Id}] the debug option quick build did not reach the match", "scripts/shell/survey-skirmish.txt");
                    }
                    Console.WriteLine($"       skirmish script \"{name}\" [{skin.Id}]: {person.StepsDone}/{person.StepCount} steps in {watch.Elapsed.TotalSeconds:F0} s");
                    Delete(root);
                });
    }

    private static string Alpine(GameData data) => data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase));

    private static void StructureGroups(GameData data, ProductSurveyReport report)
    {
        foreach (var skin in new[] { UiSkin.Original, UiSkin.Modern })
        {
            var setup = new MatchSetup(Alpine(data), "FactionAmerica", ["FactionChina"], SkirmishMatchOptions.StandardAt(data.Rate)) { Seed = 5, AutoPlay = true };
            var live = LiveMatch.Create(data, setup, new Vector2(1280, 720));
            var hud = new MatchController(live, GameSettings(skin)) { StarBlinks = false };
            long time = 1_000;
            void Key(KeyCode key, params KeyCode[] held)
            {
                foreach (var modifier in held) hud.Process(new KeyEvent(time += 16, modifier, true));
                hud.Process(new KeyEvent(time += 16, key, true));
                hud.Process(new KeyEvent(time += 16, key, false));
                foreach (var modifier in held) hud.Process(new KeyEvent(time += 16, modifier, false));
            }
            var seconds = data.Rate.Time.WholeSecondsToTicks(150);
            while (live.Tick < seconds) { live.Advance(0.05); hud.Update(0.05); }
            var own = live.World.Entities.Where(entity => entity.Owner == live.Human).ToArray();
            var structures = own.Where(entity => entity.Template.IsKindOf("STRUCTURE")).Select(entity => entity.Id).ToArray();
            var units = own.Where(entity => !entity.Template.IsKindOf("STRUCTURE") && !entity.Template.IsKindOf("PROJECTILE") && entity.Template.IsKindOf("SELECTABLE")).Select(entity => entity.Id).ToArray();
            void Look(string what, Func<bool> expect, string detail)
            {
                try
                {
                    hud.Update(0.05);
                    _ = hud.BuildDrawList(1280, 720);
                    if (!expect()) report.Add($"group|{skin.Id}|{what}", "Skirmish", $"[{skin.Id}] {what}: {detail}", "match against the computer for 150 s, select as named (ProductSurveyTests.StructureGroups)");
                }
                catch (Exception exception)
                {
                    report.Add($"group-throws|{skin.Id}|{what}", "Skirmish", $"[{skin.Id}] {what} throws {exception.GetType().Name}: {exception.Message}", "ProductSurveyTests.StructureGroups");
                }
            }
            if (structures.Length < 3) report.Add($"group-setup|{skin.Id}", "Skirmish", $"[{skin.Id}] the player has only {structures.Length} structure(s) after 150 s with the computer playing", "ProductSurveyTests.StructureGroups");
            hud.Input.Selection.Replace(structures);
            Look("all structures selected", () => hud.CommandBar.Context != CommandBarContext.Empty, "the bar has no context");
            for (var press = 0; press < 4; press++) { Key(KeyCode.Tab); Look($"Tab {press + 1} through the structures", () => hud.CommandBar.Context != CommandBarContext.Empty && (hud.CommandBar.Group is null || hud.CommandBar.Slots.Any(slot => slot is not null)), "the bar has no context, or a group without a command"); }
            Key(KeyCode.Tab, KeyCode.LeftShift);
            Look("Shift+Tab through the structures", () => true, string.Empty);
            if (units.Length > 1)
            {
                hud.Input.Selection.Replace(units);
                Look("all units selected: the bar offers commands", () => hud.CommandBar.Slots.Any(slot => slot is not null), "the bar is empty");
                for (var press = 0; press < 5; press++) { Key(KeyCode.Tab); Look($"Tab {press + 1} through the units", () => hud.CommandBar.Context != CommandBarContext.Empty, "the bar has no context"); }
            }
            hud.Input.Selection.Replace([.. structures, .. units]);
            Look("structures and units selected", () => true, string.Empty);
            report.Note($"- groups [{skin.Id}]: {structures.Length} structures, {units.Length} units selected at tick {live.Tick}: {string.Join(", ", own.Where(entity => units.Contains(entity.Id)).GroupBy(entity => entity.Template.Name).Select(group => $"{group.Key} x{group.Count()}"))}");
        }
    }

    private static CnCNew.Ui.Settings.GameSettings GameSettings(UiSkin skin)
        => CnCNew.Ui.Settings.GameSettings.Default with { Display = CnCNew.Ui.Settings.GameSettings.Default.Display with { Skin = skin.Name } };

    // ----------------------------------------------------------------------------------------------------------------------------- matches

    private static void Matches(GameData data, ProductSurveyReport report)
    {
        var minutes = int.TryParse(System.Environment.GetEnvironmentVariable("CNCNEW_SURVEY_MATCH_MINUTES"), CultureInfo.InvariantCulture, out var given) && given > 0 ? given : 8;
        var profiles = new (string Name, AiTactics Tactics, string A, string B)[]
        {
            ("balanced", AiTactics.Balanced, "FactionAmerica", "FactionChina"),
            ("rush", AiTactics.Rush, "FactionChina", "FactionGLA"),
            ("turtle", AiTactics.Turtle, "FactionGLA", "FactionAmerica"),
            ("eco", AiTactics.Eco, "FactionAmerica", "FactionGLA"),
        };
        var seed = 11;
        foreach (var (name, tactics, a, b) in profiles)
            foreach (var speed in new[] { 1.0, 3.0 })
                Match(data, report, name, tactics, a, b, speed, minutes, seed++);
    }

    private static void Match(GameData data, ProductSurveyReport report, string profile, AiTactics tactics, string factionA, string factionB, double speed, int minutes, int seed)
    {
        var label = $"{profile} {factionA.Replace("Faction", string.Empty)} vs {factionB.Replace("Faction", string.Empty)} at {speed:0}x, {minutes} game minutes, seed {seed}";
        var repro = $"ProductSurveyTests.Matches: {label}";
        void Found(string kind, string text) => report.Add($"match|{kind}|{profile}|{speed:0}x", "Matches", $"[{profile} {speed:0}x] {text}", repro);
        var ai = new MatchAi(CnCNew.Simulation.Skirmish.Original.SkirmishAiKind.Builtin, CnCNew.Simulation.Scripting.ScriptDifficulty.Normal, tactics);
        var options = SkirmishMatchOptions.StandardAt(data.Rate);
        options = options with { MaxTicks = data.Rate.Time.WholeSecondsToTicks(minutes * 60) };
        var setup = new MatchSetup(Alpine(data), factionA, [factionB], options) { Seed = seed, AutoPlay = true, Ai = [ai, ai] };
        var wall = Stopwatch.StartNew();
        LiveMatch? live = null;
        MatchAnalysis? analysis = null;
        var frames = new List<double>();
        try
        {
            live = LiveMatch.Create(data, setup, new Vector2(1280, 720));
            live.Speed = speed;
            var hud = new MatchController(live, GameSettings(UiSkin.Modern)) { StarBlinks = false };
            var attached = LiveMatchAnalysis.Attach(live) ?? throw new InvalidOperationException("the match cannot be analysed");
            const double Frame = 1.0 / 60;
            var guard = 0L;
            var watch = new Stopwatch();
            while (!live.IsOver && guard++ < 10_000_000)
            {
                watch.Restart();
                live.Advance(Frame);
                hud.Update(Frame);
                if (guard % 6 == 0) _ = hud.BuildDrawList(1280, 720);
                frames.Add(watch.Elapsed.TotalMilliseconds);
            }
            if (!live.IsOver) Found("endless", $"did not end ({live.Tick} ticks)");
            analysis = attached.Finish("completed");
        }
        catch (Exception exception)
        {
            Found("throws", $"{exception.GetType().Name}: {exception.Message}");
            return;
        }

        frames.Sort();
        var mean = frames.Average();
        var p99 = frames[(int)(frames.Count * 0.99)];
        var budget = speed > 1 ? 14.0 : 6.0;
        if (mean > budget) Found("slow", $"a frame costs {mean:F2} ms on average (budget {budget:F0} ms), 99 % {p99:F1} ms, worst {frames[^1]:F1} ms");
        foreach (var player in analysis.Players)
        {
            var structures = player.StructuresBuilt.Sum(item => item.Count);
            if (structures < 2) Found($"nobase-{player.Player}", $"player {player.Player} ({player.Faction}) began only {structures} structure(s)");
            if (player.Collected <= 0) Found($"nocollect-{player.Player}", $"player {player.Player} ({player.Faction}) collected no supplies");
            if (player.UnitsBuiltCount < 1 && player.Commands < 10) Found($"idle-{player.Player}", $"player {player.Player} ({player.Faction}) built no unit and gave {player.Commands} command(s)");
        }

        // The match plays again from its own recording to the same state, the computer players deciding again.
        try
        {
            var replay = ReplayFormat.Read(ReplayFormat.Write(live.Session.ToReplay()));
            var played = SkirmishReplay.Play(replay, data.Templates, data.Fingerprint, live.Terrain, live.Map.Objects);
            if (played.FirstDeviation is { } deviation) Found("desync", $"the recording plays differently from tick {deviation.Tick}: {deviation}");
            else if (played.FinalSessionHash != live.Session.StateHash() || played.EndTick != live.World.Tick)
                Found("desync", $"the recording ends in another state: tick {played.EndTick} against {live.World.Tick}, hash {played.FinalSessionHash:X16} against {live.Session.StateHash():X16}");
        }
        catch (Exception exception)
        {
            Found("replay-throws", $"playing the recording again throws {exception.GetType().Name}: {exception.Message}");
        }
        var summary = string.Join("; ", analysis.Players.Select(player => $"p{player.Player} {player.Faction.Replace("Faction", string.Empty)} collected {player.Collected}, built {player.StructuresBuilt.Sum(item => item.Count)} structures/{player.UnitsBuiltCount} units, {player.Commands} commands"));
        var line = $"- match {label}: ended at tick {live.Tick}{(analysis.Players.Any(player => player.IsWinner) ? " with a winner" : " without a winner")}; wall {wall.Elapsed.TotalSeconds:F0} s; frame mean {mean:F2} ms, 99% {p99:F1} ms, worst {frames[^1]:F1} ms; {summary}";
        report.Note(line);
        Console.WriteLine("       " + line.TrimStart('-', ' '));
    }
}
