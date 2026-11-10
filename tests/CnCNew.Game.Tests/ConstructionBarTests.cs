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
        harness.Run("Modern skin: the construction overlay holds the cameo and the name of the building, the bar with the percent and the cancel button, in the grid area and nowhere else, at the bar opacity",
            () => ConstructionOverlay(data));
        harness.Run("Original skin: the layout window shows the cancel button and the text, and no overlay is drawn", () => OriginalKeepsItsWay(data));
        return harness.Failures;
    }

    private sealed class Rig
    {
        public Rig(GameData data, UiSkin skin, int width = 1280, int height = 720, Func<DisplaySettings, DisplaySettings>? display = null)
        {
            var scenario = new DemoScenario("hud-construction", "hud-construction", "hud-construction", 6000, [new ScenarioUnit("dozer", "AmericaVehicleDozer", 1, 0, 100, 0f)], [], 100f, 0f, CameraHeight: 330f)
            {
                StartMoney = 5000,
                HumanFaction = "FactionAmerica",
                OpponentFaction = "FactionChina",
            };
            (Width, Height) = (width, height);
            Live = LiveScenario.Create(data, scenario, new Vector2(width, height));
            var shown = GameSettings.Default.Display with { Skin = skin.Name };
            Controller = new MatchController(Live, GameSettings.Default with { Display = display is null ? shown : display(shown) }) { StarBlinks = false };
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

    /// <summary>The modern skin's own element (issue 287): name, cameo, progress and the cancel button in one overlay that stands where the button grid stands.</summary>
    private static void ConstructionOverlay(GameData data)
    {
        foreach (var (width, height) in new[] { (800, 600), (1280, 720), (1920, 1080) })
        {
            var rig = new Rig(data, UiSkin.Modern, width, height);
            var site = rig.BeginBarracks();
            rig.Controller.Input.Selection.Replace([site.Id]);
            rig.Frame(2);
            var list = rig.Controller.BuildDrawList(width, height);
            var text = rig.Controller.Assets.Text;
            var shown = list.Commands.OfType<UiTextDraw>().ToArray();
            var where = $"at {width}x{height}";
            var barracks = shown.Where(draw => draw.Text == "Barracks").ToArray();
            TestHarness.Assert(barracks.Length == 1, $"the name of the building is written once, in the overlay and not again on the plate {where} ({barracks.Length})");
            var cameo = list.Commands.OfType<UiImageDraw>().FirstOrDefault(draw => draw.Image == "SABarracks");
            TestHarness.Assert(cameo is not null, $"the cameo of the building (SABarracks) is drawn {where}");
            var cancelRect = rig.Controller.WindowRect("ControlBar.wnd:ButtonCancelConstruction")!.Value;
            var icon = list.Commands.OfType<UiImageDraw>().FirstOrDefault(draw => draw.Image == "SSStop");
            TestHarness.Assert(icon is not null && icon.Destination == cancelRect, $"the cancel button wears its icon {where}");
            var percent = shown.FirstOrDefault(draw => draw.Text.Contains('%') && draw.Y > rig.Height * 3 / 4);
            TestHarness.Assert(percent is not null, $"the percent is written {where}");

            // Everything lies in the grid area of the bar: from the first command button to the portrait window, over the buttons' rows.
            var slot1 = rig.Controller.WindowRect("ControlBar.wnd:ButtonCommand01")!.Value;
            var slot12 = rig.Controller.WindowRect("ControlBar.wnd:ButtonCommand12")!.Value;
            var portrait = rig.Controller.WindowRect("ControlBar.wnd:RightHUD")!.Value;
            var grid = CnCNew.Ui.Layout.UiRect.FromEdges(slot1.X, Math.Min(slot1.Y, slot12.Y), portrait.X, Math.Max(slot1.Bottom, slot12.Bottom));
            foreach (var (name, rect) in new[] { ("cameo", cameo!.Destination), ("cancel button", cancelRect) })
                TestHarness.Assert(rect.X >= grid.X - 8 && rect.Right <= grid.Right && rect.Y >= grid.Y - 8 && rect.Bottom <= grid.Bottom + 8, $"the {name} lies in the grid area {where} ({rect} in {grid})");
            TestHarness.Assert(cameo.Destination.Right <= cancelRect.X, $"the cameo is left of the cancel button {where}");
            TestHarness.Assert(percent!.X >= cameo.Destination.Right && percent.X < cancelRect.X, $"the text stands between the cameo and the cancel button {where}");
            TestHarness.Assert(percent.Clip.Right <= cancelRect.X, $"the text is clipped before the cancel button {where}");
        }

        // The plate takes the opacity of the bar like the wells behind the buttons (display.barOpacity); the cameo and the button do not.
        var alphas = new List<int>();
        foreach (var opacity in new[] { 100, 20 })
        {
            var rig = new Rig(data, UiSkin.Modern, display: display => display with { BarOpacityPercent = opacity });
            var site = rig.BeginBarracks();
            rig.Controller.Input.Selection.Replace([site.Id]);
            rig.Frame(2);
            var list = rig.Controller.BuildDrawList(1280, 720);
            var cancel = rig.Controller.WindowRect("ControlBar.wnd:ButtonCancelConstruction")!.Value;
            var plate = list.Commands.OfType<UiFillRect>().First(fill => fill.Rect.Contains(cancel.CenterX, cancel.CenterY) && fill.Rect.Width > cancel.Width * 2);
            alphas.Add(plate.Color.A);
            TestHarness.Assert(list.Commands.OfType<UiImageDraw>().Any(draw => draw.Image == "SSStop" && draw.Tint.A == 255), $"the button stays opaque at {opacity} %");
        }
        TestHarness.Assert(alphas[1] < alphas[0], $"the alpha of the plate follows the setting ({alphas[0]} then {alphas[1]})");

        // Once the building stands the grid is back.
        var done = new Rig(data, UiSkin.Modern);
        var built = done.BeginBarracks();
        done.Controller.Input.Selection.Replace([built.Id]);
        done.Frame(CnCNew.Tests.TestTime.FramesPerSecond * 120);
        TestHarness.Assert(!done.Live.World.FindEntity(built.Id)!.HasStatus(ObjectStatusBits.UnderConstruction), "the barracks is finished");
        done.Frame(2);
        var after = done.Controller.BuildDrawList(1280, 720);
        TestHarness.Assert(!done.Controller.IsWindowShown("ControlBar.wnd:ButtonCancelConstruction") && !after.Commands.OfType<UiImageDraw>().Any(draw => draw.Image == "SSStop"), "a finished building has no cancel button");
        TestHarness.Assert(after.Commands.OfType<UiTextDraw>().Count(draw => draw.Text == "Barracks") == 1, "and its name is on the plate again");
    }

    /// <summary>The original skin keeps the windows of the original: the layout's text beside the button and nothing of the overlay.</summary>
    private static void OriginalKeepsItsWay(GameData data)
    {
        var rig = new Rig(data, UiSkin.Original);
        var site = rig.BeginBarracks();
        rig.Controller.Input.Selection.Replace([site.Id]);
        rig.Frame(2);
        var list = rig.Controller.BuildDrawList(rig.Width, rig.Height);
        TestHarness.Assert(rig.Controller.IsWindowShown("ControlBar.wnd:UnderConstructionDesc"), "the description window of the original is shown");
        TestHarness.Assert(!list.Commands.OfType<UiImageDraw>().Any(draw => draw.Image == "SABarracks"), "no overlay cameo of the building");
    }
}
