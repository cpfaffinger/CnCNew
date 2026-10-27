using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Game.Shell;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Skins;

namespace CnCNew.Game.Tests;

/// <summary>
/// The campaign's way in both skins (docs/UI-MODERN.md section 9, R2; the side item of Phase 39): the difficulty question of the main menu, the briefing of the first USA
/// mission and that mission's HUD once its opening cinematic is over, each in the six resolutions of the shell tests. The shell screens are the same at every rate; the
/// mission's HUD is a picture of a real mission and is pinned per rate (the same game, not the same bits).
/// </summary>
internal static class CampaignPinTests
{
    private const string BaselinePath = "tests/CnCNew.Game.Tests/baselines/campaign-screens.txt";
    private const int NavWidth = 1280, NavHeight = 720;
    private static readonly (int Width, int Height)[] Resolutions = [(800, 600), (1024, 768), (1280, 720), (1920, 1080), (2560, 1080), (640, 960)];

    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(CampaignPinTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP CampaignPinTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("The campaign's way in six resolutions: the difficulty question, the briefing, the mission's HUD", () => Walk(data, BaselinePath));
        harness.Run("The campaign's way in six resolutions in the modern skin", () => SkinPins.With(UiSkin.Modern, () => Walk(data, SkinPins.Modern(BaselinePath))));
        return harness.Failures;
    }

    private static void Walk(GameData data, string path)
    {
        var updating = Environment.GetEnvironmentVariable("CNCNEW_UPDATE_BASELINES") == "1";
        var baselines = SkinPins.Load(path);
        var hashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var environment = new FixedShellEnvironment(Path.Combine(Path.GetTempPath(), "cncnew-campaign-pins-" + Guid.NewGuid().ToString("N")));
        var shell = GameShell.Create(data, environment, viewport: new UiViewport(NavWidth, NavHeight), skin: SkinPins.Current);

        void Pin(string key, CnCNew.Ui.Runtime.UiDrawList list)
        {
            TestHarness.Assert(list.Statistics.UnresolvedLabels == 0, $"{key}: {list.Statistics.UnresolvedLabels} unresolved label(s)");
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(list.Dump())));
            hashes[key] = hash;
            if (updating) return;
            TestHarness.Assert(baselines.TryGetValue(key, out var expected), $"{key}: no baseline recorded (run with CNCNEW_UPDATE_BASELINES=1 once, then review the diff)");
            TestHarness.Assert(expected == hash, $"{key}: draw list hash changed ({expected} -> {hash})");
        }
        void Screens(string name)
        {
            foreach (var (width, height) in Resolutions)
            {
                shell.Resize(width, height);
                Pin($"{name}@{width}x{height}", shell.BuildDrawList());
            }
            shell.Resize(NavWidth, NavHeight);
        }

        ShellTestInput.Click(shell, "MainMenu.wnd:ButtonSinglePlayer");
        ShellTestInput.Click(shell, "MainMenu.wnd:ButtonUSA");
        Screens("Campaign-Difficulty");
        ShellTestInput.Click(shell, "MainMenu.wnd:ButtonHard");
        TestHarness.Assert(shell.Phase == ShellPhase.MissionBriefing, "Hard starts the first mission with its briefing");
        Screens("Campaign-Briefing");

        for (var i = 0; i < 400 && shell.Phase == ShellPhase.MissionBriefing; i++) shell.Update(0.1);
        var started = shell.DrainOutputs().OfType<StartMissionOutput>().Single();
        var live = LiveMission.Create(data, started.Run, new Vector2(NavWidth, NavHeight));
        var controller = new MissionController(live, SkinPins.Settings);
        for (var frame = 0; frame < 60; frame++) controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        for (var frame = 0; frame < 15000 && controller.Director.Letterbox; frame++) controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        TestHarness.Assert(!controller.Director.Letterbox, $"the opening cinematic of the mission ends (still running at tick {live.Tick}, map {started.Run.MapPath})");
        var rate = data.Rate.TicksPerSecond;
        foreach (var (width, height) in Resolutions) Pin($"Mission-HUD@{width}x{height}@{rate}Hz", controller.BuildDrawList(width, height));

        if (updating) SkinPins.Save(path, hashes, merge: true);
    }
}
