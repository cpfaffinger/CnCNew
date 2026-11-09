using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Presentation.Input;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Settings;
using CnCNew.Ui.Skins;
using CnCNew.Ui.Wnd;

namespace CnCNew.UiRenderTool;

/// <summary>
/// The skirmish HUD with a group of barracks selected and their queues part full, in both skins and four window sizes, over a striped backdrop (so that a translucent bar shows what
/// is under it): the pictures behind the queue-load display, the bar's opacity and the selection markers. Written as <c>hudgroup-SKIN-WxH.png</c>; <c>--hud</c> runs only these.
/// </summary>
internal static class HudSelectionScenes
{
    private static readonly (int Width, int Height)[] Sizes = [(800, 600), (1280, 720), (1920, 1080), (1920, 1200)];

    public static void Capture(GameData data, IUiTextMeasure measure, Action<UiDrawList, int, int, string> capture, Func<GameSettings, GameSettings>? configure = null, string tag = "")
    {
        foreach (var skin in new[] { UiSkin.Original, UiSkin.Modern })
            foreach (var (width, height) in Sizes)
            {
                var units = ImmutableArray.CreateBuilder<ScenarioUnit>();
                for (var index = 0; index < 5; index++) units.Add(new ScenarioUnit($"b{index + 1}", "AmericaBarracks", 1, index * 80, 0f, 0f));
                var scenario = new DemoScenario("hud-group", "hud-group", "hud-group", 6000, units.ToImmutable(), [], 100f, 0f, CameraHeight: 330f) { StartMoney = 30000 };
                var live = LiveScenario.Create(data, scenario, new Vector2(width, height));
                var settings = GameSettings.Default with { Display = GameSettings.Default.Display with { Skin = skin.Name } };
                if (configure is not null) settings = configure(settings);
                var controller = new MatchController(live, settings, "English", measure) { StarBlinks = false };
                long time = 100_000;
                void Frame(int count)
                {
                    for (var frame = 0; frame < count; frame++)
                    {
                        live.Advance(1.0 / CnCNew.Data.OriginalFrame.PerSecond);
                        controller.Update(1.0 / CnCNew.Data.OriginalFrame.PerSecond);
                    }
                }
                Frame(3);
                controller.Input.Selection.Replace([.. Enumerable.Range(1, 5).Select(index => live.Tags[$"b{index}"])]);
                Frame(2);
                controller.BuildDrawList(width, height);
                var ranger = Enumerable.Range(1, CommandBarModel.GridSlots).First(slot => controller.CommandBar[slot] is { } button && button.Button.Kind == GuiCommandKind.UnitBuild && button.Button.Object.Equals("AmericaInfantryRanger", StringComparison.OrdinalIgnoreCase));
                var rect = controller.WindowRect($"ControlBar.wnd:ButtonCommand{ranger:00}")!.Value;
                var at = new Vector2(rect.CenterX, rect.CenterY);
                for (var click = 0; click < 33; click++)
                {
                    time += 700;
                    controller.Process(new MouseMoveEvent(time += 10, at));
                    controller.Process(new MouseButtonEvent(time += 40, MouseButton.Left, true, at));
                    controller.Process(new MouseButtonEvent(time += 40, MouseButton.Left, false, at));
                    if (click % 3 == 2) Frame(1);
                }
                Frame(4);
                controller.Process(new MouseMoveEvent(time += 1000, new Vector2(width / 2f, height / 3f)));
                var hud = controller.BuildDrawList(width, height);
                var backdrop = ImmutableArray.CreateBuilder<UiDrawCommand>();
                var stripe = Math.Max(24, width / 40);
                for (var x = 0; x < width; x += stripe)
                    backdrop.Add(new UiFillRect(new CnCNew.Ui.Layout.UiRect(x, 0, stripe, height), (x / stripe) % 2 == 0 ? new WndColor(72, 98, 70, 255) : new WndColor(150, 130, 70, 255)));
                capture(hud with { Commands = backdrop.ToImmutable().AddRange(hud.Commands) }, width, height, $"hudgroup{tag}-{skin.Id}-{width}x{height}");
            }
    }
}
