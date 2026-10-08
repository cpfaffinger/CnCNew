using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Game.Shell;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Testing;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Settings;
using CnCNew.Ui.Wnd;

namespace CnCNew.UiRenderTool;

/// <summary>
/// The real skirmish HUD (<see cref="MatchController"/> over a fresh Alpine Assault match, no 3D world behind it) in the state a player meets when an order fails: the dozer selected,
/// the game's own message lines at the top left (<c>docs/GAME-HUD.md</c> §8: a refused building spot, a finished building, an attack), and the pointer resting on a construct button
/// the base cannot build yet, so its description shows the "Requires:" line (§5a). A second picture per size adds the letterbox bars of a mission cinematic at full fade
/// (<c>W3DDisplay::renderLetterBox</c>: none at 16:9). Written as <c>hudfeedback-WxH.png</c> and <c>hudletterbox-WxH.png</c>; input and clock are fixed, so two runs write identical files.
/// </summary>
internal static class HudFeedbackScenes
{
    private static readonly (int Width, int Height)[] Sizes = [(800, 600), (1280, 720), (1920, 1080)];

    public static void Capture(GameData data, IUiTextMeasure measure, Action<UiDrawList, int, int, string> capture)
    {
        var mapPath = data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase));
        foreach (var (width, height) in Sizes)
        {
            var match = LiveMatch.Create(data, new MatchSetup(mapPath, data.Factions[0].Faction.Name, [data.Factions[1].Faction.Name], SkirmishMatchOptions.StandardAt(data.Rate)), new Vector2(width, height));
            var controller = new MatchController(match, GameSettings.Default, "English", measure);
            long time = 10_000;
            void Advance(int frames)
            {
                for (var frame = 0; frame < frames; frame++)
                {
                    // One original frame (1/30 s) of game and wall time per step, at whatever rate the data's sessions tick.
                    match.Advance(1.0 / CnCNew.Data.OriginalFrame.PerSecond);
                    controller.Update(1.0 / CnCNew.Data.OriginalFrame.PerSecond);
                }
            }
            void Click(Vector2 at)
            {
                controller.Process(new MouseMoveEvent(time += 16, at));
                controller.Process(new MouseButtonEvent(time += 16, MouseButton.Left, true, at));
                controller.Process(new MouseButtonEvent(time += 50, MouseButton.Left, false, at));
                time += 1000;
            }

            Advance(1);
            var dozer = match.World.Entities.First(entity => entity.Owner == match.Human && entity.Template.IsKindOf("DOZER"));
            if (!match.Camera.TryWorldToScreen(EntityPicker.CenterOf(dozer), out var dozerScreen)) throw new InvalidDataException("The dozer of the fresh base is not on screen.");
            Click(dozerScreen);
            Advance(1);
            controller.BuildDrawList(width, height);

            controller.Messages.Message("GUI:CantBuildThere");
            controller.Messages.Message("DOZER:ConstructionComplete", "Cold Fusion Reactor");
            controller.Messages.Message("RADAR:StructureUnderAttack");

            var locked = controller.CommandBar.Slots.FirstOrDefault(button => button is { Reason: ButtonReason.MissingPrerequisite, Button.Kind: GuiCommandKind.DozerConstruct })
                ?? throw new InvalidDataException("The dozer of a fresh base has no construct button with a missing prerequisite.");
            var rect = controller.WindowRect($"ControlBar.wnd:ButtonCommand{locked.Slot:00}") ?? throw new InvalidDataException("The command button window is missing.");
            controller.Process(new MouseMoveEvent(time += 16, new Vector2(rect.CenterX, rect.CenterY)));
            Advance(45);
            var hud = controller.BuildDrawList(width, height);
            var backdrop = ImmutableArray.Create<UiDrawCommand>(new UiFillRect(new CnCNew.Ui.Layout.UiRect(0, 0, width, height), new WndColor(72, 98, 70, 255)));
            capture(hud with { Commands = backdrop.AddRange(hud.Commands) }, width, height, $"hudfeedback-{width}x{height}");

            // A cinematic's letterbox over the same message lines: the control bar is hidden, the bars cover what a 16:9 picture leaves free.
            var cinematic = backdrop.AddRange(MissionTextOverlay.Messages(controller.Messages.Log, controller.TextStyle, controller.ScaleOptions, measure, width, height))
                .AddRange(MissionPresentationDirector.LetterboxBars(width, height, true, 1f));
            capture(hud with { Commands = cinematic }, width, height, $"hudletterbox-{width}x{height}");
        }
    }
}
