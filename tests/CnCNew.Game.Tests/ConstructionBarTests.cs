using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Settings;
using CnCNew.Ui.Skins;

namespace CnCNew.Game.Tests;

/// <summary>
/// The owner's playtest of 10 November 2026 (issues 286 and 287): a building under construction shows the original's cancel button with its cameo and the localised texts, and
/// the modern skin gives that job to a construction overlay of its own.
/// </summary>
internal static class ConstructionBarTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(ConstructionBarTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP ConstructionBarTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("Original skin: a barracks under construction shows the cancel button with its cameo, its percent and its tooltip in the original's words, and a click cancels and refunds",
            () => SiteOnTheBar(data, UiSkin.Original));
        harness.Run("Modern skin: a barracks under construction shows the cancel button with its cameo, its percent and its tooltip in the original's words, and a click cancels and refunds",
            () => SiteOnTheBar(data, UiSkin.Modern));
        return harness.Failures;
    }

    private sealed class Rig
    {
        public Rig(GameData data, UiSkin skin, int width = 1280, int height = 720)
        {
            var scenario = new DemoScenario("hud-construction", "hud-construction", "hud-construction", 6000, [new ScenarioUnit("dozer", "AmericaVehicleDozer", 1, 0, 100, 0f)], [], 100f, 0f, CameraHeight: 330f)
            {
                StartMoney = 5000,
                HumanFaction = "FactionAmerica",
                OpponentFaction = "FactionChina",
            };
            (Width, Height) = (width, height);
            Live = LiveScenario.Create(data, scenario, new Vector2(width, height));
            Controller = new MatchController(Live, GameSettings.Default with { Display = GameSettings.Default.Display with { Skin = skin.Name } }) { StarBlinks = false };
            Frame(3);
        }

        public int Width { get; }
        public int Height { get; }
        public LiveScenario Live { get; }
        public MatchController Controller { get; }
        public long Time { get; private set; } = 100_000;

        public void Frame(int count = 1)
        {
            for (var frame = 0; frame < count; frame++)
            {
                Live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
                Controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
            }
        }

        public CnCNew.Simulation.World.Entity BeginBarracks()
        {
            var dozer = Live.World.FindEntity(Live.Tags["dozer"])!;
            var receipt = Live.Issue(new PlayerId(1), (sequence, tick) => new ConstructCommand(new PlayerId(1), sequence, tick, dozer.Id, "AmericaBarracks",
                Fixed.FromDecimal((decimal)(Live.Anchor.X + 80)), Fixed.FromDecimal((decimal)(Live.Anchor.Y + 240)), Fixed.FromInt(0)));
            TestHarness.Assert(receipt.Accepted, $"the dozer may begin a barracks ({receipt.Reason})");
            for (var frame = 0; frame < 120; frame++)
            {
                Frame();
                if (Live.World.Entities.FirstOrDefault(entity => entity.Owner.Value == 1 && entity.Template.Name == "AmericaBarracks" && entity.HasStatus(ObjectStatusBits.UnderConstruction)) is { } site) return site;
            }
            throw new InvalidOperationException("no site");
        }

        public void Click(Vector2 at)
        {
            Time += 700;
            Controller.Process(new MouseMoveEvent(Time += 10, at));
            Controller.Process(new MouseButtonEvent(Time += 40, MouseButton.Left, true, at));
            Controller.Process(new MouseButtonEvent(Time += 40, MouseButton.Left, false, at));
        }
    }

    /// <summary>
    /// The owner's playtest of 10 November 2026 (issue 286): the button of a building under construction was an empty frame (its cameo was never set), and the description beside it said
    /// the template's name. The original sets the cameo of <c>Command_CancelConstruction</c> (<c>setControlCommand</c>) and formats <c>CONTROLBAR:UnderConstructionDesc</c> with the percent.
    /// </summary>
    private static void SiteOnTheBar(GameData data, UiSkin skin)
    {
        var rig = new Rig(data, skin);
        var player = new PlayerId(1);
        var before = rig.Live.World.MoneyOf(player);
        var site = rig.BeginBarracks();
        var spent = before - rig.Live.World.MoneyOf(player);
        rig.Controller.Input.Selection.Replace([site.Id]);
        rig.Frame(2);
        const string Cancel = "ControlBar.wnd:ButtonCancelConstruction";
        var text = rig.Controller.Assets.Text;
        string Expected() => text.Format("CONTROLBAR:UnderConstructionDesc", (decimal)(rig.Live.World.FindEntity(site.Id)!.ConstructionShare.ToDecimal() * 100m));

        var list = rig.Controller.BuildDrawList(rig.Width, rig.Height);
        TestHarness.Assert(rig.Controller.CommandBar.Context == CommandBarContext.UnderConstruction, $"the bar is the under-construction bar ({rig.Controller.CommandBar.Context})");
        TestHarness.Assert(rig.Controller.IsWindowShown(Cancel), "the cancel button is shown");
        var rect = rig.Controller.WindowRect(Cancel)!.Value;
        TestHarness.Assert(list.Commands.OfType<UiImageDraw>().Any(draw => draw.Image == "SSStop" && draw.Destination == rect), "the cancel button wears the cameo of Command_CancelConstruction (SSStop)");
        TestHarness.Assert(!list.Commands.OfType<UiImageDraw>().Any(draw => draw.Image.StartsWith("Buttons-", StringComparison.Ordinal) && rect.Contains(draw.Destination.CenterX, draw.Destination.CenterY)),
            "and none of the layout's three frame pieces");

        var shown = list.Commands.OfType<UiTextDraw>().Select(draw => draw.Text).ToArray();
        TestHarness.Assert(!shown.Any(line => line.Contains(site.Template.Name, StringComparison.Ordinal)), $"no text names the template ({string.Join(" | ", shown)})");
        TestHarness.Assert(shown.Contains(Expected()), $"the description is the original's text with the percent '{Expected()}' ({string.Join(" | ", shown)})");

        var tooltip = rig.Controller.Tree.TooltipOverride?.Invoke(rig.Controller.Tree.FindByName(Cancel)!);
        var name = text.Get("CONTROLBAR:CancelBuild").Replace("&", string.Empty, StringComparison.Ordinal);
        TestHarness.Assert(tooltip is not null && tooltip.StartsWith(name, StringComparison.Ordinal) && tooltip.Contains(text.Get("CONTROLBAR:ToolTipCancelConstruction"), StringComparison.Ordinal)
            && !tooltip.Contains(site.Template.Name, StringComparison.Ordinal), $"the tooltip is the cancel command's name and description ({tooltip})");

        var percent = Expected();
        rig.Frame(CnCNew.Tests.TestTime.FramesPerSecond * 4);
        rig.Controller.BuildDrawList(rig.Width, rig.Height);
        TestHarness.Assert(rig.Controller.BuildDrawList(rig.Width, rig.Height).Commands.OfType<UiTextDraw>().Any(draw => draw.Text == Expected()) && Expected() != percent,
            $"the percent follows the construction ({percent} then {Expected()})");

        var money = rig.Live.World.MoneyOf(player);
        rig.Click(new Vector2(rect.CenterX, rect.CenterY));
        rig.Frame(6);
        var gone = rig.Live.World.FindEntity(site.Id);
        TestHarness.Assert(gone is null || gone.EffectivelyDead || gone.DestroyRequested || !gone.HasStatus(ObjectStatusBits.UnderConstruction), "a click on the cancel button cancels the construction");
        TestHarness.Assert(rig.Live.World.MoneyOf(player) > money, $"and refunds the money ({money} -> {rig.Live.World.MoneyOf(player)}, spent {spent})");
    }
}
