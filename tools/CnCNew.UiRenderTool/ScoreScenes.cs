using System.Globalization;
using CnCNew.Game.Data;
using CnCNew.Game.Shell;
using CnCNew.Game.Tests;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Settings;
using CnCNew.Ui.Skins;

namespace CnCNew.UiRenderTool;

/// <summary>
/// The score screen (docs/UI-MODERN.md, the end screen) in both skins at 800 x 600, 1280 x 720, 1920 x 1080 and 1920 x 1200, at the text sizes 100, 125 and 150 %, for a match of two
/// and of eight players, a victory and a defeat: written as <c>score-SKIN-Np-won|lost-WxH-TTT.png</c>, with the readability audit of every picture in <c>score-audit.txt</c>.
/// </summary>
internal static class ScoreScenes
{
    private static readonly (int Width, int Height)[] Sizes = [(800, 600), (1280, 720), (1920, 1080), (1920, 1200)];

    public static int Capture(GameData data, IUiTextMeasure textMeasure, string outputDirectory, Action<UiDrawList, int, int, string> capture)
    {
        var report = new List<string> { "skin players outcome size text% texts smallest-px too-wide too-tall" };
        var failures = 0;
        foreach (var skin in new[] { UiSkin.Original, UiSkin.Modern })
            foreach (var (width, height) in Sizes)
                foreach (var percent in new[] { 100, 125, 150 })
                    foreach (var (players, won) in new[] { (2, true), (3, false), (8, true) })
                    {
                        var settings = Path.Combine(outputDirectory, $"score-settings-{skin.Id}-{width}x{height}-{percent}");
                        if (Directory.Exists(settings)) Directory.Delete(settings, recursive: true);
                        Directory.CreateDirectory(settings);
                        if (percent != 100)
                            new SettingsStore(settings).Save(GameSettings.Default with { Accessibility = GameSettings.Default.Accessibility with { UiTextScalePercent = percent } });
                        var shell = GameShell.Create(data, new FixedShellEnvironment(settings, width, height), "English", new UiViewport(width, height), textMeasure, skin: skin);
                        shell.MatchFinished(ScoreScreenFixture.Result(players, won));
                        for (var step = 0; step < 90; step++) shell.Update(1.0 / 30);
                        var name = $"score-{skin.Id}-{players}p-{(won ? "won" : "lost")}-{width}x{height}-{percent}";
                        var list = shell.BuildDrawList();
                        capture(list, width, height, name);
                        var audit = UiReadability.Audit(shell.CurrentScreen.Tree, list);
                        report.Add(string.Create(CultureInfo.InvariantCulture, $"{skin.Id} {players} {(won ? "won" : "lost")} {width}x{height} {percent} {audit.TextLines} {audit.SmallestPixels} {audit.TooWide} {audit.TooTall}"));
                        foreach (var overflow in audit.Overflows) report.Add($"    {(overflow.TooWide ? "too wide" : "too tall")} {overflow.Window} \"{overflow.Text.Replace('\n', ' ')}\" {overflow.Measure}");
                        Directory.Delete(settings, recursive: true);
                    }
        File.WriteAllLines(Path.Combine(outputDirectory, "score-audit.txt"), report);
        return failures;
    }
}
