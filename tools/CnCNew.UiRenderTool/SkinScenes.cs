using System.Globalization;
using System.Numerics;
using CnCNew.Game.Analysis;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Game.Shell;
using CnCNew.Game.Shell.Screens;
using CnCNew.Presentation.Input;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Settings;
using CnCNew.Ui.Skins;

namespace CnCNew.UiRenderTool;

/// <summary>
/// Both skins (docs/UI-MODERN.md) on the screens a person walks - main menu, single player, skirmish setup and map picker, options and "More options", the quit question,
/// credits, the replay list, the score screen and the analysis after a short match, and the HUD of that match - drawn with the renderer's GDI font in the sizes of the
/// readability check (R3) and written as <c>skin-SKIN-SCREEN-WxH.png</c>. Every screen is also measured (<see cref="UiReadability"/>): the smallest text, and every label
/// wider or taller than its window; the report is <c>skin-audit.txt</c>, and the modern skin's thresholds are checked (the original's numbers are its comparison).
/// </summary>
internal static class SkinScenes
{
    private static readonly (int Width, int Height)[] Sizes = [(800, 600), (1280, 720), (1920, 1080)];

    /// <summary>R3 (b): the smallest text of the modern skin, by window height.</summary>
    public static int MinimumPixels(int height) => height switch { >= 1080 => 24, >= 720 => 16, _ => 13 };

    public static int Capture(GameData data, IUiTextMeasure textMeasure, string outputDirectory, Action<UiDrawList, int, int, string> capture)
    {
        var report = new List<string> { "skin screen size texts smallest-px too-wide too-tall" };
        var failures = 0;
        foreach (var skin in new[] { UiSkin.Original, UiSkin.Modern })
            foreach (var (width, height) in Sizes)
            {
                var time = 0L;
                var settings = Path.Combine(outputDirectory, $"skin-settings-{skin.Id}-{width}x{height}");
                if (Directory.Exists(settings)) Directory.Delete(settings, recursive: true);
                var shell = GameShell.Create(data, new FixedShellEnvironment(settings, width, height), "English", new UiViewport(width, height), textMeasure, skin: skin);
                void Shot(string name, UiDrawList? drawn = null, UiWindowTree? tree = null)
                {
                    var list = drawn ?? shell.BuildDrawList();
                    capture(list, width, height, $"skin-{skin.Id}-{name}-{width}x{height}");
                    File.WriteAllText(Path.Combine(outputDirectory, $"skin-{skin.Id}-{name}-{width}x{height}.txt"), list.Dump());
                    var audit = UiReadability.Audit(tree ?? shell.CurrentScreen.Tree, list);
                    report.Add(string.Create(CultureInfo.InvariantCulture, $"{skin.Id} {name} {width}x{height} {audit.TextLines} {audit.SmallestPixels} {audit.TooWide} {audit.TooTall}"));
                    foreach (var overflow in audit.Overflows) report.Add($"    {(overflow.TooWide ? "too wide" : "too tall")} {overflow.Window} \"{overflow.Text.Replace('\n', ' ')}\" {overflow.Measure}");
                    if (!skin.IsModern) return;
                    if (audit.TooWide > 0) failures++;
                    if (audit.TextLines > 0 && audit.SmallestPixels < MinimumPixels(height))
                    {
                        report.Add($"    smallest text {audit.SmallestPixels} px below {MinimumPixels(height)} px");
                        failures++;
                    }
                }
                void Click(string window)
                {
                    var rect = (shell.CurrentScreen.Tree.FindByName(window) ?? throw new InvalidDataException($"Window '{window}' is missing.")).Rect;
                    var point = new Vector2(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
                    shell.HandleInput(new MouseMoveEvent(++time, point));
                    shell.HandleInput(new MouseButtonEvent(++time, MouseButton.Left, true, point));
                    shell.HandleInput(new MouseButtonEvent(++time, MouseButton.Left, false, point));
                    shell.HandleInput(new MouseMoveEvent(++time, new Vector2(0, 0)));
                }

                Shot("main");
                Click("MainMenu.wnd:ButtonSinglePlayer");
                Shot("single-player");
                Click("MainMenu.wnd:ButtonSkirmish");
                Shot("skirmish-setup");
                Click("SkirmishGameOptionsMenu.wnd:ButtonSelectMap");
                var list = shell.CurrentScreen.Tree.FindByName("SkirmishMapSelectMenu.wnd:ListboxMap")!;
                var row = new Vector2(list.Rect.X + 20, list.Rect.Y + 8);
                shell.HandleInput(new MouseMoveEvent(++time, row));
                shell.HandleInput(new MouseButtonEvent(++time, MouseButton.Left, true, row));
                shell.HandleInput(new MouseButtonEvent(++time, MouseButton.Left, false, row));
                Shot("map-select");
                Click("SkirmishMapSelectMenu.wnd:ButtonOK");
                Shot("skirmish-setup-map");
                Click("SkirmishGameOptionsMenu.wnd:ButtonStart");
                Shot("match-loading");
                shell.Update(1.0 / CnCNew.Data.OriginalFrame.PerSecond);
                var start = shell.DrainOutputs().OfType<StartMatchOutput>().Single();
                var setup = start.Setup with { Options = start.Setup.Options with { MaxTicks = data.Rate.Time.WholeSecondsToTicks(40) } };
                var live = LiveMatch.Create(data, setup, new Vector2(width, height));
                var hud = new MatchController(live, GameSettings.Default with { Display = GameSettings.Default.Display with { Skin = skin.Name } }, "English", textMeasure);
                var analysis = LiveMatchAnalysis.Attach(live) ?? throw new InvalidOperationException("The match cannot be analysed.");
                var hudShot = false;
                while (!live.IsOver)
                {
                    live.Advance(0.25);
                    if (!hudShot && live.Tick >= data.Rate.Time.WholeSecondsToTicks(20))
                    {
                        hud.Update(0.25);
                        Shot("hud", hud.BuildDrawList(width, height), hud.Tree);
                        hudShot = true;
                    }
                }
                var result = CnCNew.Game.Shell.MatchResult.From(live.Session, null) with { Analysis = analysis.Finish("completed") };
                shell.MatchFinished(result);
                Shot("score");
                Click("ScoreScreen.wnd:ButtonSaveReplay");
                Shot("analysis-1");
                for (var page = 1; page < 5; page++) Click("ScoreScreen.wnd:ButtonBuddy");
                Shot("analysis-5");
                Click("ScoreScreen.wnd:ButtonOk");
                Click("ScoreScreen.wnd:ButtonOk");
                Click("MainMenu.wnd:ButtonOptions");
                Shot("options");
                Click(OptionsScreen.MoreOptionsButton);
                Shot("more-options");
                Click("MoreOptions.wnd:ButtonBack");
                Click("OptionsMenu.wnd:ButtonBack");
                Click("MainMenu.wnd:ButtonLoadReplay");
                Click("MainMenu.wnd:ButtonReplay");
                Shot("replays");
                Click("ReplayMenu.wnd:ButtonBack");
                Click("MainMenu.wnd:ButtonCredits");
                shell.Update(2.0);
                Shot("credits");
                shell.HandleInput(new KeyEvent(++time, KeyCode.Escape, true));
                Click("MainMenu.wnd:ButtonExit");
                Shot("quit");
                Directory.Delete(settings, recursive: true);
            }
        report.Add(failures == 0 ? "modern skin: R3 (a) and (b) met" : $"modern skin: {failures} finding(s) against R3");
        File.WriteAllLines(Path.Combine(outputDirectory, "skin-audit.txt"), report);
        foreach (var line in report) Console.WriteLine(line);
        return failures;
    }
}
