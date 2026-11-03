using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Settings;

namespace CnCNew.Game.Tests;

/// <summary>What the owner's playtest of 3 November 2026 saw on the HUD of a skirmish, each pinned on real data.</summary>
internal static class PlaytestHudTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(PlaytestHudTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP PlaytestHudTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("The percent of a building under construction is not cut off: its clip holds the whole line", () => ConstructionPercent(data));
        harness.Run("The general's window keeps its original art in the modern skin; an owned science in colour, one without points grey", () => GeneralsWindow(data));
        return harness.Failures;
    }

    private static void GeneralsWindow(GameData data)
    {
        var scenario = new DemoScenario("hud-generals", "hud-generals", "hud-generals", 300, [new ScenarioUnit("centre", "AmericaCommandCenter", 1, 0, 0, 0f)], [], 0f, 0f)
        {
            HumanFaction = "FactionAmerica",
            OpponentFaction = "FactionChina",
            Sciences = ["SCIENCE_PaladinTank"],
        };
        var live = LiveScenario.Create(data, scenario, new Vector2(1280, 720));
        var controller = new MatchController(live, GameSettings.Default) { StarBlinks = false };
        live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
        controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        controller.ToggleGenerals();
        controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        var images = controller.BuildDrawList(1280, 720).Commands.OfType<UiImageDraw>().ToArray();
        TestHarness.Assert(images.Any(image => image.Image.StartsWith("GeneralsPowerMenu", StringComparison.Ordinal)), $"the window draws its original background ({string.Join(", ", images.Select(image => image.Image).Distinct().Take(12))})");
        var view = controller.Generals!;
        var all = view.Rank1.Concat(view.Rank3).Concat(view.Rank8).OfType<CnCNew.Presentation.Input.CommandBarButton>().ToArray();
        var owned = all.Where(button => button.Reason == CnCNew.Presentation.Input.ButtonReason.AlreadyOwned).Select(button => button.Button.ButtonImage).ToHashSet();
        var unaffordable = all.Where(button => !button.IsEnabled && button.Reason != CnCNew.Presentation.Input.ButtonReason.AlreadyOwned).Select(button => button.Button.ButtonImage).ToHashSet();
        TestHarness.Assert(owned.Count > 0 && unaffordable.Count > 0, $"one science is owned, others wait for points ({owned.Count}, {unaffordable.Count})");
        TestHarness.Assert(images.Where(image => owned.Contains(image.Image)).All(image => !image.Grayscale) && images.Where(image => unaffordable.Contains(image.Image)).All(image => image.Grayscale)
            && images.Any(image => unaffordable.Contains(image.Image)), "the owned science in colour, the ones without points grey");
    }

    private static void ConstructionPercent(GameData data)
    {
        var scenario = new DemoScenario("hud-construct", "hud-construct", "hud-construct", 900, [new ScenarioUnit("dozer", "AmericaVehicleDozer", 1, -90, 0, 0f)],
            [new ScenarioOrder(10, "dozer", ScenarioOrderKind.Build, "plant", X: 30, Y: -70, Template: "AmericaPowerPlant")], 20, -15, 20f, 42f, 330f)
        {
            HumanFaction = "FactionAmerica",
            OpponentFaction = "FactionChina",
            StartMoney = 5000,
        };
        var live = LiveScenario.Create(data, scenario, new Vector2(1920, 1080));
        var controller = new MatchController(live, GameSettings.Default);
        UiTextDraw? percent = null;
        for (var frame = 0; frame < 600 && percent is null; frame++)
        {
            live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
            controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
            if (frame % 30 != 0) continue;
            percent = controller.BuildDrawList(1920, 1080).Commands.OfType<UiTextDraw>().FirstOrDefault(text => text.Text.Contains('%'));
        }
        TestHarness.Assert(percent is not null, "a building under construction shows its percent");
        // The tallest line of the label font as the screens scale it (12 points bold, up to twice at 1080 lines) lies below the point.
        TestHarness.Assert(percent!.Clip.Y <= percent.Y && percent.Clip.Y + percent.Clip.Height >= percent.Y + 40, $"the clip holds the whole line ({percent})");
    }
}
