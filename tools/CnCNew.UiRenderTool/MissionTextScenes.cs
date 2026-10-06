using System.Collections.Immutable;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Shell;
using CnCNew.Simulation.Ids;
using CnCNew.Ui.Layout;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Text;
using CnCNew.Ui.Wnd;

namespace CnCNew.UiRenderTool;

/// <summary>
/// The mission's on-screen texts as the original's <c>InGameUI</c> draws them (<c>docs/SCRIPT-ENGINE.md</c> §7a), one scene per screen size on a flat backdrop: three message lines at the top
/// left, the military caption of <c>MD_USA01</c> halfway through its typing, a countdown and a counter at the named timer position, the superweapon list, the cinematic text near the bottom and a
/// message popup. The state machines are stepped to fixed frames, so two runs write identical files. Written as <c>missiontext-WxH.png</c>.
/// </summary>
internal static class MissionTextScenes
{
    private static readonly UiDrawStatistics NoStatistics = new(0, 0, 0, 0, ImmutableSortedDictionary<string, int>.Empty);
    private static readonly (int Width, int Height)[] Sizes = [(800, 600), (1280, 720), (1920, 1080)];

    public static void Capture(GameData data, IUiTextMeasure measure, Action<UiDrawList, int, int, string> capture)
    {
        var style = InGameUiStyle.FromGraph(data.Graph);
        var options = new UiScaleOptions();
        var text = data.LoadText("English");
        var assets = HudAssets.Load(data, "English");
        var language = style.Language.Settings;

        string Label(string label) => text.TryGet(label, out var value) ? value : throw new InvalidDataException($"The string table has no '{label}'.");

        foreach (var (width, height) in Sizes)
        {
            var commands = ImmutableArray.CreateBuilder<UiDrawCommand>();
            commands.Add(new UiFillRect(new UiRect(0, 0, width, height), new WndColor(72, 98, 70, 255)));
            commands.Add(new UiFillRect(new UiRect(0, height * 4 / 5, width, height - height * 4 / 5), new WndColor(30, 34, 40, 255)));

            // DISPLAY_TEXT: three lines at MessagePosition, the middle one ten seconds younger, so the first has started to fade.
            var log = new UiMessageLog(style.Settings);
            log.Add("Your base is under attack!", 0);
            log.Add("Reinforcements are on their way.", 0);
            log.Add("Build a Power Plant.", 120);
            for (var frame = 1; frame <= 150; frame++) log.Update(frame);
            commands.AddRange(MissionTextOverlay.Messages(log, style, options, measure, width, height));

            // SHOW_MILITARY_CAPTION at frame 95: the title typed, the body half typed.
            var caption = MilitaryCaptionState.Begin(Label("MAP:MD_USA01CINELocation01"), 5000, 0, style.Settings, language.MilitaryCaptionSpeed, language.MilitaryCaptionDelayMilliseconds, out var problem)
                ?? throw new InvalidDataException(problem);
            for (var frame = 1; frame <= 130; frame++) caption.Update(frame);
            commands.AddRange(MissionTextOverlay.MilitaryCaption(caption, style, options, measure, width, height));

            // DISPLAY_COUNTDOWN_TIMER and DISPLAY_COUNTER.
            commands.AddRange(MissionTextOverlay.NamedTimers(
                [(Label("SCRIPT:CHI02xReinforcementTimer"), true, CnCNew.Data.OriginalFrame.PerSecond * (11 * 60 + 55)), (Label("SCRIPT:MD_CHI03InternationalOpinion"), false, 42), (Label("SCRIPT:CHI02xReinforcementTimer"), true, 0)],
                15 * 5, style, options, measure, width, height));

            // The superweapon list: a waiting power, a ready one, another waiting one.
            SuperweaponLine Line(int id, string power, string time, int seconds, bool ready) => new(new PlayerId(id), new EntityId((uint)id), power, Label($"GUI:{power}") + ": ", time, seconds, ready);
            ImmutableArray<SuperweaponLine> lines =
            [
                Line(1, "SuperweaponNeutronMissile", "4:12", 252, false),
                Line(2, "SuperweaponParticleUplinkCannon", "0:00", 0, true),
                Line(3, "SuperweaponScudStorm", "0:47", 47, false),
            ];
            commands.AddRange(SuperweaponTimers.Draw(lines, player => new WndColor((byte)(60 * player.Value), 200, (byte)(255 - 80 * player.Value), 255), 15, width, height, height * 4 / 5, measure, style, options));

            // DISPLAY_CINEMATIC_TEXT.
            commands.AddRange(MissionTextOverlay.Cinematic(new CinematicTextState(Label("MAP:MD_USA01CINELocation01").Replace('\n', ' ') + ", ten kilometres from the border.", new FontDescription("Arial", 14, true), 120),
                style, options, measure, width, height));

            // INGAME_POPUP_MESSAGE.
            var popup = new MissionPopupWindow(assets, measure, options, new UiViewport(width, height));
            popup.Show(new PopupMessage(Label("GUI:ControlBarMoneyDisplay").Replace("%d", "10000") + " - build a Power Plant first, then a Barracks, and train an Infantry squad.", 35, 8, 260, false), new UiViewport(width, height));
            commands.AddRange(popup.BuildCommands(width, height));

            capture(new UiDrawList(commands.ToImmutable(), NoStatistics), width, height, $"missiontext-{width}x{height}");
        }
    }
}
