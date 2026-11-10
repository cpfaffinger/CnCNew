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

    /// <summary>
    /// A barracks under construction selected, in both skins and three window sizes, over a striped backdrop: the original's cancel button with its cameo and the percent beside it,
    /// and the modern skin's construction overlay. Written as <c>hudconstruct-SKIN-WxH.png</c>.
    /// </summary>
    public static void CaptureConstruction(GameData data, IUiTextMeasure measure, Action<UiDrawList, int, int, string> capture)
    {
        foreach (var skin in new[] { UiSkin.Original, UiSkin.Modern })
            foreach (var (width, height) in new[] { (800, 600), (1280, 720), (1920, 1080) })
            {
                var scenario = new DemoScenario("hud-construct", "hud-construct", "hud-construct", 6000, [new ScenarioUnit("dozer", "AmericaVehicleDozer", 1, 0, 100, 0f)], [], 100f, 0f, CameraHeight: 330f) { StartMoney = 5000 };
                var live = LiveScenario.Create(data, scenario, new Vector2(width, height));
                var settings = GameSettings.Default with { Display = GameSettings.Default.Display with { Skin = skin.Name } };
                var controller = new MatchController(live, settings, "English", measure) { StarBlinks = false };
                void Frame(int count)
                {
                    for (var frame = 0; frame < count; frame++)
                    {
                        live.Advance(1.0 / CnCNew.Data.OriginalFrame.PerSecond);
                        controller.Update(1.0 / CnCNew.Data.OriginalFrame.PerSecond);
                    }
                }
                Frame(3);
                var dozer = live.World.FindEntity(live.Tags["dozer"])!;
                live.Issue(new CnCNew.Simulation.Ids.PlayerId(1), (sequence, tick) => new CnCNew.Simulation.Commands.ConstructCommand(new CnCNew.Simulation.Ids.PlayerId(1), sequence, tick, dozer.Id, "AmericaBarracks",
                    CnCNew.Simulation.Numerics.Fixed.FromDecimal((decimal)(live.Anchor.X + 80)), CnCNew.Simulation.Numerics.Fixed.FromDecimal((decimal)(live.Anchor.Y + 240)), CnCNew.Simulation.Numerics.Fixed.FromInt(0)));
                CnCNew.Simulation.World.Entity? site = null;
                for (var frame = 0; frame < 400 && site is null; frame++)
                {
                    Frame(1);
                    site = live.World.Entities.FirstOrDefault(entity => entity.Owner.Value == 1 && entity.Template.Name == "AmericaBarracks" && entity.HasStatus(CnCNew.Simulation.Modules.ObjectStatusBits.UnderConstruction));
                }
                Frame(150);
                controller.Input.Selection.Replace([site!.Id]);
                Frame(2);
                var hud = controller.BuildDrawList(width, height);
                var backdrop = ImmutableArray.CreateBuilder<UiDrawCommand>();
                backdrop.Add(new UiFillRect(new CnCNew.Ui.Layout.UiRect(0, 0, width, height), new WndColor(88, 104, 72, 255)));
                capture(hud with { Commands = backdrop.ToImmutable().AddRange(hud.Commands) }, width, height, $"hudconstruct-{skin.Id}-{width}x{height}");
            }
    }

    /// <summary>
    /// The selection markers and health bars of the three styles (<c>display.markerStyle</c>), three heights and the three health bar modes: two barracks (one hurt by an enemy tank) and a tank of
    /// the enemy's, the viewer's Rangers, with one barracks and two Rangers selected. Written as <c>hudmarkers-STYLE-MODE-HEIGHT-WxH.png</c>.
    /// </summary>
    public static void CaptureMarkers(GameData data, IUiTextMeasure measure, Action<UiDrawList, int, int, string> capture)
    {
        var units = new[]
        {
            new ScenarioUnit("b1", "AmericaBarracks", 1, 0, 0, 0f), new ScenarioUnit("b2", "AmericaBarracks", 1, 90, 0, 0f), new ScenarioUnit("tank", "ChinaTankBattleMaster", 2, 90, 110, 0f),
            new ScenarioUnit("r1", "AmericaInfantryRanger", 1, 20, 70, 0f), new ScenarioUnit("r2", "AmericaInfantryRanger", 1, 50, 70, 0f), new ScenarioUnit("r3", "AmericaInfantryRanger", 1, 80, 70, 0f),
            new ScenarioUnit("e1", "ChinaInfantryRedguard", 2, 140, 60, 0f),
        };
        var orders = new[] { new ScenarioOrder(1, "tank", ScenarioOrderKind.Attack, "b2"), new ScenarioOrder(1, "e1", ScenarioOrderKind.Attack, "r3") };
        var cases = new (string Name, SelectionMarkerStyle Style, HealthBarMode Mode, HealthBarHeight Height)[]
        {
            ("extended-selected-normal", SelectionMarkerStyle.Extended, HealthBarMode.SelectedOnly, HealthBarHeight.Normal),
            ("original-selected-narrow", SelectionMarkerStyle.Original, HealthBarMode.SelectedOnly, HealthBarHeight.Narrow),
            ("original-below-normal", SelectionMarkerStyle.Original, HealthBarMode.AlwaysBelowFull, HealthBarHeight.Normal),
            ("modern-selected-normal", SelectionMarkerStyle.Modern, HealthBarMode.SelectedOnly, HealthBarHeight.Normal),
            ("modern-always-wide", SelectionMarkerStyle.Modern, HealthBarMode.AlwaysAuto, HealthBarHeight.Wide),
            ("extended-always-narrow", SelectionMarkerStyle.Extended, HealthBarMode.AlwaysAuto, HealthBarHeight.Narrow),
        };
        foreach (var (name, style, mode, height) in cases)
            foreach (var (width, heightPx) in new[] { (1280, 720), (1920, 1080) })
            {
                var scenario = new DemoScenario("hud-markers", "hud-markers", "hud-markers", 6000, [.. units], [.. orders], 70f, 30f, CameraHeight: 260f) { StartMoney = 1000 };
                var live = LiveScenario.Create(data, scenario, new Vector2(width, heightPx));
                var settings = GameSettings.Default with { Display = GameSettings.Default.Display with { Skin = UiSkinName.Modern, MarkerStyle = style, HealthBars = mode, HealthBarThickness = height } };
                var controller = new MatchController(live, settings, "English", measure) { StarBlinks = false };
                for (var frame = 0; frame < 200; frame++)
                {
                    live.Advance(1.0 / CnCNew.Data.OriginalFrame.PerSecond);
                    controller.Update(1.0 / CnCNew.Data.OriginalFrame.PerSecond);
                }
                controller.Input.Selection.Replace([live.Tags["b1"], live.Tags["r1"], live.Tags["r2"]]);
                for (var frame = 0; frame < 2; frame++)
                {
                    live.Advance(1.0 / CnCNew.Data.OriginalFrame.PerSecond);
                    controller.Update(1.0 / CnCNew.Data.OriginalFrame.PerSecond);
                }
                var hud = controller.BuildDrawList(width, heightPx);
                var backdrop = ImmutableArray.CreateBuilder<UiDrawCommand>();
                backdrop.Add(new UiFillRect(new CnCNew.Ui.Layout.UiRect(0, 0, width, heightPx), new WndColor(88, 104, 72, 255)));
                capture(hud with { Commands = backdrop.ToImmutable().AddRange(hud.Commands) }, width, heightPx, $"hudmarkers-{name}-{width}x{heightPx}");
            }
    }
}
