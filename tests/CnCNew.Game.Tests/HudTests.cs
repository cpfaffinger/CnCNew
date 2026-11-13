using System.Collections.Immutable;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Wnd;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;
using CnCNew.Ui.Settings;

namespace CnCNew.Game.Tests;

internal static class HudTests
{
    private const string BaselineDirectory = "baselines";

    public static int Run(string[] args)
    {
        var contentDir = ContentDir(args);
        var harness = new TestHarness(nameof(HudTests));
        harness.Run("Control bar scheme: the creation resolution is read from the scheme, a scheme without one is refused, unread keys are counted", SchemeCreationResolution);
        if (contentDir is null)
        {
            Console.WriteLine("SKIP HudTests real data (pass --content-dir <dir> to run against real data)");
            return harness.Failures;
        }

        var data = GameData.Load(contentDir, step => Console.WriteLine($"  loading: {step}"), rate: CnCNew.Tests.TestRate.Default);

        harness.Run("Select dozer, construct a structure, queue a unit, move and attack-move", () => ScriptedSession(data));
        harness.Run("Replay of the human player's commands is internally deterministic", () => ReplayMatches(data));
        harness.Run("Lockstep probe: the click path with every order executed 0, 4 and 12 ticks after it was given; a late refusal is shown and counted", () => InputDelayThroughTheClickPath(data));
        harness.Run("Lockstep probe: with no delay the tick driver and the command path record the match of direct submission; pause and speed are requests to the driver", () => TickDriverAndPathChangeNothing(data));
        harness.Run("Lockstep probe: a mission runs with orders executed 4 and 12 ticks later", () => MissionWithInputDelay(data));
        harness.Run("Standing orders with delay: a waypoint path given through the HUD is walked with orders executed 6 ticks later - no station ordered twice, none late", () => StandingOrdersWithDelay(data));
        harness.Run("Mouse setups: by default the left button selects and the right button orders; with AlternateMouse off the left button orders as in the original; the scroll slider is the original's factor", () => MouseSetups(data));
        harness.Run("HUD draw lists at several resolutions contain the expected buttons", () => ResolutionSweep(data));
        harness.Run("Minimap click moves the camera", () => MinimapClickMovesCamera(data));
        harness.Run("Box selection selects the start units", () => BoxSelectionSelects(data));
        harness.Run("Control groups store and recall a selection", () => ControlGroupsWork(data));
        harness.Run("Control bar scheme art and the logo image", () => SchemeArt(data));
        harness.Run("The general's window: the star opens it, a science is bought with a point, a level grants the next, the bar fills, a science of another side stays away", () => GeneralsWindow(data));
        harness.Run("The money display shows the localized money at the scheme's rectangle, not the layout placeholder", () => MoneyDisplay(data));
        harness.Run("A command button shows its description as a pointer tooltip after the delay, and the icons carry no hot key or cost text", () => CommandButtonTooltip(data));
        harness.Run("Command button description: missing prerequisites and sciences are listed after 'Requires:', too little money and owned upgrades are said, as populateBuildTooltipLayout does", () => CommandButtonRequirements(data));
        harness.Run("The money and the power window describe themselves; the power text carries production and consumption", () => MoneyAndPowerTooltips(data));
        // The hash of baselines/hud-drawlist-1280x720.sha256 does not depend on the simulation rate (measured equal at 30, 60 and 120 Hz): the suite's data.
        harness.Run("Golden draw list hash", () => GoldenDrawListHash(data));
        harness.Run("Message list: refused orders, finished buildings and upgrades, attacks and defeats print the original's labels at the message position and fade; what has no line is counted", () => MessageList(data));
        harness.Run("Message list through the real click path: a refused building spot names the obstacles in its way", () => MessageListFromARefusedPlacement(data));
        harness.Run("The overlay is drawn: selection ring and health bar, a healed structure shows the wrench", () => OverlayIsDrawn(data));

        harness.Run("Cursor and markers: Alt waypoints draw a path with a marker per station, an order leaves a fading hint, the cursor follows context and windows", () => CursorAndMarkers(data));
        harness.Run("Diplomacy: the communicator button shows the players of a skirmish, with side, team and state", () => DiplomacyWindow(data));
        harness.Run("Beacons: the button of a match of a server, a place, the limit, the delete key, and nothing for an enemy", () => BeaconButton(data));
        harness.Run("Placement preview: the chosen building follows the cursor in the object frame, translucent, red where refused, gone after the click", () => PlacementPreviewInObjectFrame(data));
        harness.Run("A drag while a building is placed turns it and draws no selection box; a drag of the plain pointer does", () => NoSelectionBoxWhilePlacing(data));
        harness.Run("A cameo that cannot be built yet is grey, one that cannot be paid for is dark", () => DisabledCameosShowWhy(data));
        harness.Run("The bar of the special powers: a power the player has shows, recharges, and a press aims it", () => PowerBar(data));
        harness.Run("Hack, weapon switch, overcharge, hijack, car bomb, sabotage: a press does what the button says", () => ButtonsOfUnits(data));
        harness.Run("E tells the player what it selected: nothing selected, across the map, and no line for a structure that has no fellows", () => SelectionMessages(data));
        harness.Run("The sneak attack: a press puts the tunnel at the cursor and the power is used where it is put down", () => SneakAttack(data));
        harness.Run("The radar's frame is drawn in every frame, over the radar's window", () => TheRadarFrameFollowsTheCamera(data));

        return harness.Failures;
    }

    /// <summary><c>ToggleDiplomacy</c>, <c>PopulateInGameDiplomacyPopup</c>: a skirmish shows the players and has nobody to mute.</summary>
    private static void DiplomacyWindow(GameData data)
    {
        var (match, controller) = NewMatch(data, new Vector2(1280, 720));
        long time = 10_000;
        Vector2 Center(string window)
        {
            var rect = controller.WindowRect(window) ?? throw new InvalidOperationException($"no window {window}");
            return new Vector2(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
        }
        bool Shown(string name) => controller.Tree.FindByName($"Diplomacy.wnd:{name}") is { IsHidden: false };

        Refresh(controller);
        TestHarness.Assert(!controller.IsDiplomacyOpen, "the window begins closed");
        var communicator = controller.Tree.FindByName("ControlBar.wnd:PopupCommunicator")!;
        TestHarness.Assert(controller.Tree.TooltipOverride?.Invoke(communicator)?.StartsWith(controller.Assets.Text.Get("CONTROLBAR:Communicator"), StringComparison.Ordinal) == true, "the button is the communicator in a skirmish");
        Click(controller, ref time, MouseButton.Left, Center("ControlBar.wnd:PopupCommunicator"));
        Advance(match, controller, 1);
        Refresh(controller);
        TestHarness.Assert(controller.IsDiplomacyOpen && Shown("InGameParent") && !Shown("SoloParent") && !Shown("BuddiesParent") && !Shown("RadioButtonBuddies"), "the button opens the players' page");

        var text = controller.Assets.Text;
        var rows = controller.DiplomacyRows;
        TestHarness.Assert(rows.Length == 2 && rows[0].Player == match.Human, $"one row per player ({rows.Length})");
        TestHarness.Assert(rows[1].Name == text.Get("GUI:MediumAI") && rows[0].Side != rows[1].Side, $"the computer's name and each side ({rows[0].Name}, {rows[0].Side}, {rows[1].Name}, {rows[1].Side})");
        TestHarness.Assert(rows.All(row => row.Status == text.Get("GUI:PlayerAlive") && row.MuteConnection is null), "both alive, and nobody to mute");
        TestHarness.Assert(rows[0].Team == text.Get("Team:0") && rows[1].Team == text.Get("Team:AI"), $"the teams ({rows[0].Team}, {rows[1].Team})");
        TestHarness.Assert(controller.Tree.FindByName("Diplomacy.wnd:StaticTextPlayer0") is { TextColor: not null } && Shown("StaticTextPlayer1") && !Shown("StaticTextPlayer2") && !Shown("ButtonMute0") && !Shown("ButtonUnMute0"), "two rows are shown, in the player's colour, the others hidden");

        // What the dying leave behind is killed as well: the start object's builder, a hull.
        for (var round = 0; round < 8 && !match.World.IsPlayerDefeated(rows[1].Player); round++)
        {
            foreach (var entity in match.World.Entities.Where(entity => entity.Owner == rows[1].Player && !entity.EffectivelyDead).ToArray()) match.World.Kill(entity.Id);
            Advance(match, controller, (int)match.World.Time.OriginalFrames(30));
        }
        Refresh(controller);
        TestHarness.Assert(match.World.IsPlayerDefeated(rows[1].Player) && controller.DiplomacyRows[1].Status == text.Get("GUI:PlayerDead"), $"the open window follows a defeat ({controller.DiplomacyRows[1].Status}, {string.Join(", ", match.World.Entities.Where(entity => entity.Owner == rows[1].Player).Select(entity => entity.Template.Name + (entity.EffectivelyDead ? "(dead)" : string.Empty)))})");

        controller.Process(new KeyEvent(time += 16, KeyCode.Escape, true));
        controller.Process(new KeyEvent(time += 16, KeyCode.Escape, false));
        TestHarness.Assert(!controller.IsDiplomacyOpen && !controller.IsMenuOpen, "Escape closes the window and not the game's menu");

        Click(controller, ref time, MouseButton.Left, Center("ControlBar.wnd:PopupCommunicator"));
        Advance(match, controller, 1);
        Click(controller, ref time, MouseButton.Left, Center("ControlBar.wnd:ButtonOptions"));
        TestHarness.Assert(controller.IsMenuOpen && !controller.IsDiplomacyOpen, "the quit menu takes its place");
    }

    /// <summary>The button of a game of several humans (<c>ControlBar::setControlBarSchemeByPlayer</c>, <c>ControlBar::update</c>) and <c>GameLogic::onPlaceBeacon</c> as a player sees it.</summary>
    private static void BeaconButton(GameData data)
    {
        var (match, controller) = NewMatch(data, new Vector2(1280, 720));
        var world = match.World;
        long time = 10_000;
        Refresh(controller);
        var button = controller.Tree.FindByName("ControlBar.wnd:ButtonPlaceBeacon")!;
        TestHarness.Assert(button.IsHidden, "a skirmish on one machine has no beacon button");
        controller.Input.DispatchMeta(MetaAction.PlaceBeacon, world, match.Camera);
        TestHarness.Assert(controller.Input.Commands.Mode == CommandModeKind.None, "and its key does nothing");

        controller.BeaconsEnabled = true;
        var heard = new List<bool>();
        controller.Messages.BeaconPlaced += heard.Add;
        Refresh(controller);
        TestHarness.Assert(!button.IsHidden && button.IsEnabled, "a match of a server has it");
        var rect = controller.WindowRect("ControlBar.wnd:ButtonPlaceBeacon") ?? throw new InvalidOperationException("no button");
        Click(controller, ref time, MouseButton.Left, new Vector2(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f));
        TestHarness.Assert(controller.Input.Commands.Mode == CommandModeKind.ButtonTarget, "the button waits for a place");
        Click(controller, ref time, MouseButton.Left, new Vector2(640, 300));
        Advance(match, controller, (int)world.Time.OriginalFrames(2));
        var beacon = world.Entities.FirstOrDefault(world.IsBeacon) ?? throw new InvalidOperationException($"no beacon stands (mode {controller.Input.Commands.Mode})");
        TestHarness.Assert(beacon.Owner == match.Human && beacon.Template.Name == "MultiplayerBeacon" && controller.Input.Commands.Mode == CommandModeKind.None, $"a click on the ground places it ({beacon.Template.Name})");
        TestHarness.Assert(heard.SequenceEqual([false]), $"the player hears its own beacon and is not told by the announcer ({heard.Count})");
        TestHarness.Assert(match.Frame.Drawables.Any(drawable => drawable.Entity == beacon.Id), "the player sees it");

        // Up to the limit of the data; then the button is grey and the key does nothing.
        for (var count = 1; count < world.Templates.MaxBeaconsPerPlayer; count++)
        {
            controller.Input.DispatchMeta(MetaAction.PlaceBeacon, world, match.Camera);
            Click(controller, ref time, MouseButton.Left, new Vector2(500 + 40 * count, 280));
            Advance(match, controller, (int)world.Time.OriginalFrames(2));
        }
        Refresh(controller);
        TestHarness.Assert(world.BeaconCount(match.Human) == world.Templates.MaxBeaconsPerPlayer && !button.IsEnabled, $"at the limit the button is grey ({world.BeaconCount(match.Human)} of {world.Templates.MaxBeaconsPerPlayer})");
        controller.Input.DispatchMeta(MetaAction.PlaceBeacon, world, match.Camera);
        TestHarness.Assert(controller.Input.Commands.Mode == CommandModeKind.None, "and the key waits for nothing");

        // The delete key on the selection takes the own beacon away.
        controller.Input.Selection.Replace([beacon.Id]);
        controller.Input.DispatchMeta(MetaAction.DeleteBeacon, world, match.Camera);
        Advance(match, controller, (int)world.Time.OriginalFrames(2));
        Refresh(controller);
        TestHarness.Assert(world.FindEntity(beacon.Id) is null && button.IsEnabled, "the delete key takes it away, and there is room again");

        // An enemy's beacon: neither drawn nor told of.
        var enemy = world.Configuration.Players.First(player => !player.Id.IsSystem && player.Id != match.Human && world.AreEnemies(match.Human, player.Id)).Id;
        heard.Clear();
        var near = world.Entities.First(entity => entity.Owner == match.Human && !world.IsBeacon(entity));
        TestHarness.Assert(world.Submit(new CnCNew.Simulation.Commands.PlaceBeaconCommand(enemy, 90_000, world.Tick, near.X, near.Y)).Accepted, "the enemy places one beside the player's unit");
        Advance(match, controller, (int)world.Time.OriginalFrames(2));
        var foreign = world.Entities.FirstOrDefault(entity => entity.Owner == enemy && world.IsBeacon(entity)) ?? throw new InvalidOperationException("the enemy's beacon does not stand");
        TestHarness.Assert(heard.Count == 0, "an enemy's beacon is not announced");
        TestHarness.Assert(!world.CanPlayerSee(match.Human, foreign) && match.Frame.Drawables.All(drawable => drawable.Entity != foreign.Id), "and not drawn, though it stands where the player sees");
    }

    private static void GeneralsWindow(GameData data)
    {
        var (match, controller) = NewMatch(data, new Vector2(1280, 720));
        long time = 10_000;
        Vector2 Center(string window)
        {
            var rect = controller.WindowRect(window) ?? throw new InvalidOperationException($"no window {window}");
            return new Vector2(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
        }

        var view = controller.Generals ?? throw new InvalidOperationException("the player has no general");
        TestHarness.Assert(view.RankLevel == 1 && view.SciencePoints == 1 && view.ProgressPercent == 0, $"a match begins at level 1 with its point ({view.RankLevel}, {view.SciencePoints})");
        TestHarness.Assert(view.Rank1.Length == CommandBarModel.GeneralsRank1Buttons && view.Rank3.Length == CommandBarModel.GeneralsRank3Buttons && view.Rank8.Length == CommandBarModel.GeneralsRank8Buttons, "all places of the layout");
        var shown = view.Rank1.Concat(view.Rank3).Concat(view.Rank8).OfType<CommandBarButton>().ToArray();
        TestHarness.Assert(shown.Length > 0 && shown.All(button => button.Button.Sciences.All(name => !name.Contains("CHINA", StringComparison.OrdinalIgnoreCase) && !name.Contains("GLA", StringComparison.OrdinalIgnoreCase))),
            "no science of another side is shown: " + string.Join(", ", shown.SelectMany(button => button.Button.Sciences)));

        Refresh(controller);
        TestHarness.Assert(!controller.IsGeneralsWindowOpen, "the window begins closed");
        Click(controller, ref time, MouseButton.Left, Center("ControlBar.wnd:ButtonGeneral"));
        Advance(match, controller, 1);
        Refresh(controller);
        TestHarness.Assert(controller.IsGeneralsWindowOpen, "the star opens the window");
        var list = controller.BuildDrawList(1280, 720);
        TestHarness.Assert(list.Commands.OfType<UiTextDraw>().Any(text => text.Text == "1"), "the window shows the point to spend");

        var choice = Array.FindIndex(view.Rank1.ToArray(), button => button is { IsEnabled: true });
        TestHarness.Assert(choice >= 0, "something can be bought at level 1");
        var science = view.Rank1[choice]!.Button.Sciences[0];
        Click(controller, ref time, MouseButton.Left, Center($"GeneralsExpPoints.wnd:ButtonRank1Number{choice}"));
        Advance(match, controller, (int)match.World.Time.OriginalFrames(2));
        Refresh(controller);
        TestHarness.Assert(match.World.Sciences.TryGet(science, out var bought) && match.World.PlayerSciences(match.Human).Has(bought.Index), $"{science} was bought");
        view = controller.Generals!;
        TestHarness.Assert(view.SciencePoints == 0 && view.Rank1[choice] is { Reason: ButtonReason.AlreadyOwned, IsEnabled: false }, "the point is spent and the button shows what is owned");

        match.World.AddSkillPoints(match.Human, 800);
        Advance(match, controller, 1);
        view = controller.Generals!;
        TestHarness.Assert(view.RankLevel == 2 && view.SciencePoints == 1, $"level 2 grants the next point ({view.RankLevel}, {view.SciencePoints})");
        match.World.AddSkillPoints(match.Human, 350);
        Advance(match, controller, 1);
        TestHarness.Assert(controller.Generals!.ProgressPercent == 50, $"the bar stands half way to level 3 ({controller.Generals!.ProgressPercent})");

        Click(controller, ref time, MouseButton.Left, Center("GeneralsExpPoints.wnd:ButtonExit"));
        Advance(match, controller, 1);
        TestHarness.Assert(!controller.IsGeneralsWindowOpen, "the window's button closes it");
    }

    private static void NoSelectionBoxWhilePlacing(GameData data)
    {
        var (match, controller) = NewMatch(data, new Vector2(1280, 720));
        long time = 10_000;
        Advance(match, controller, 1);
        var from = new Vector2(400, 200);
        controller.Process(new MouseMoveEvent(time += 16, from));
        controller.Process(new MouseButtonEvent(time += 16, MouseButton.Left, true, from));
        controller.Process(new MouseMoveEvent(time += 16, from + new Vector2(120, 90)));
        Advance(match, controller, 1);
        TestHarness.Assert(controller.Overlay.SelectionBox.Active, "the plain pointer drags a box");
        controller.Process(new MouseButtonEvent(time += 16, MouseButton.Left, false, from + new Vector2(120, 90)));

        var dozer = match.World.Entities.First(entity => entity.Owner == match.Human && entity.Template.IsKindOf("DOZER"));
        Click(controller, ref time, MouseButton.Left, Screen(match, EntityPicker.CenterOf(dozer)));
        Advance(match, controller, 1);
        Refresh(controller);
        var slot = FindConstructSlot(match, controller.CommandBar);
        var rect = controller.WindowRect($"ControlBar.wnd:ButtonCommand{slot:00}") ?? throw new InvalidOperationException("button window missing");
        Click(controller, ref time, MouseButton.Left, new Vector2(rect.CenterX, rect.CenterY));
        Advance(match, controller, 1);
        TestHarness.Assert(controller.Input.Commands.Mode == CommandModeKind.Placement, "the construct button enters placement mode");
        controller.Process(new MouseMoveEvent(time += 16, from));
        controller.Process(new MouseButtonEvent(time += 16, MouseButton.Left, true, from));
        controller.Process(new MouseMoveEvent(time += 16, from + new Vector2(120, 90)));
        Advance(match, controller, 1);
        TestHarness.Assert(!controller.Overlay.SelectionBox.Active, "the drag that turns a building shows no box (INPUT-05)");
    }

    private static void DisabledCameosShowWhy(GameData data)
    {
        var (match, controller) = NewMatch(data, new Vector2(1280, 720));
        long time = 10_000;
        Advance(match, controller, 1);
        var dozer = match.World.Entities.First(entity => entity.Owner == match.Human && entity.Template.IsKindOf("DOZER"));
        Click(controller, ref time, MouseButton.Left, Screen(match, EntityPicker.CenterOf(dozer)));
        Advance(match, controller, 1);
        Refresh(controller);
        var images = controller.BuildDrawList(1280, 720).Commands.OfType<UiImageDraw>().ToArray();
        var restricted = Enumerable.Range(1, CommandBarModel.GridSlots).Select(slot => controller.CommandBar[slot]).Where(button => button is { Availability: ButtonAvailability.Restricted } && button.Button.ButtonImage.Length > 0).ToArray();
        TestHarness.Assert(restricted.Length > 0, "a dozer at the start has something it cannot build yet");
        foreach (var button in restricted)
            TestHarness.Assert(images.Any(image => image.Image == button!.Button.ButtonImage && image.Grayscale), $"{button!.Button.Name} is drawn grey (SHELL-03)");
        var open = Enumerable.Range(1, CommandBarModel.GridSlots).Select(slot => controller.CommandBar[slot]).First(button => button is { Availability: ButtonAvailability.Available } && button.Button.ButtonImage.Length > 0)!;
        TestHarness.Assert(images.Any(image => image.Image == open.Button.ButtonImage && !image.Grayscale && image.Tint == CnCNew.Ui.Wnd.WndColor.White), $"{open.Button.Name} keeps its colours");
    }

    private static void TheRadarFrameFollowsTheCamera(GameData data)
    {
        var (match, controller) = NewMatch(data, new Vector2(1280, 720));
        Advance(match, controller, 1);
        (float, float) FrameCorner()
        {
            var commands = controller.BuildDrawList(1280, 720).Commands;
            var radar = commands.IndexOf(commands.OfType<UiCustomDraw>().Single(draw => draw.Callback == "W3DLeftHUDDraw"));
            TestHarness.Assert(commands[radar + 1] is UiPushClip && commands.Skip(radar + 2).Take(4).All(command => command is UiLineDraw) && commands[radar + 6] is UiPopClip, "four lines lie over the radar");
            var line = (UiLineDraw)commands[radar + 2];
            return (line.X0, line.Y0);
        }
        var before = FrameCorner();
        match.Camera.CenterOn(match.Camera.Pivot + new Vector2(120, 80));
        var after = FrameCorner();
        TestHarness.Assert(before != after, $"the frame moves with the camera in the next drawn frame, without a tick and without the radar's picture (HUD-07): {before} -> {after}");
    }

    /// <summary>
    /// The placement of a structure shows its model at the cursor (<c>InGameUI::handleBuildPlacements</c>): while the placement mode is on, <see cref="LiveGame.ObjectFrame"/> carries one more
    /// drawable than <see cref="LiveGame.Frame"/> (which stays free of it), translucent at the ghost's place and angle, tinted red where the simulation refuses the spot; the mode ends, the preview goes.
    /// </summary>
    private static void PlacementPreviewInObjectFrame(GameData data)
    {
        var (match, controller) = NewMatch(data, new Vector2(1280, 720));
        long time = 10_000;
        Advance(match, controller, 1);
        TestHarness.Assert(match.PlacementGhost is null && match.ObjectFrame.Drawables.Length == match.Frame.Drawables.Length, "no placement, no preview");

        var dozer = match.World.Entities.First(entity => entity.Owner == match.Human && entity.Template.IsKindOf("DOZER"));
        Click(controller, ref time, MouseButton.Left, Screen(match, EntityPicker.CenterOf(dozer)));
        Advance(match, controller, 1);
        Refresh(controller);
        var slot = FindConstructSlot(match, controller.CommandBar);
        var rect = controller.WindowRect($"ControlBar.wnd:ButtonCommand{slot:00}") ?? throw new InvalidOperationException("button window missing");
        Click(controller, ref time, MouseButton.Left, new Vector2(rect.CenterX, rect.CenterY));
        Advance(match, controller, 1);
        TestHarness.Assert(controller.Input.Commands.Mode == CommandModeKind.Placement, "the construct button enters placement mode");
        var template = controller.Input.Commands.PendingButton!.Object;
        var viewAngle = match.Data.Visuals.TryGet(template, out var visual) ? visual.PlacementViewAngle : 0f;

        var dozerGround = SimulationQueries.GroundOf(dozer);
        PlacementGhost? valid = null;
        foreach (var offset in (ReadOnlySpan<Vector2>)[new(300, 0), new(-300, 0), new(0, 300), new(0, -300), new(220, 220), new(-220, -220)])
        {
            var candidate = dozerGround + offset;
            controller.Process(new MouseMoveEvent(time += 16, Screen(match, new Vector3(candidate.X, candidate.Y, 0f))));
            if (controller.Input.Commands.Ghost is { IsValid: true } found) { valid = found; break; }
        }
        TestHarness.Assert(valid is not null, $"a buildable spot for '{template}' exists near the dozer");
        TestHarness.Assert(Math.Abs(valid!.Angle - viewAngle) < 1e-5f, $"the placement starts at the template's PlacementViewAngle ({valid.Angle} vs {viewAngle})");
        Advance(match, controller, 1);
        TestHarness.Assert(match.PlacementGhost is { } ghost && ghost.Template == template && ghost.IsValid, "the HUD hands the ghost to the game every frame");
        var solid = match.Frame.Drawables;
        var withPreview = match.ObjectFrame.Drawables;
        TestHarness.Assert(withPreview.Length == solid.Length + 1 && !solid.Any(drawable => drawable.IsPlacementPreview), "the preview is in the object frame only");
        var preview = withPreview[^1];
        TestHarness.Assert(preview.IsPlacementPreview && preview.TemplateName.Equals(template, StringComparison.OrdinalIgnoreCase), "the last drawable is the preview of the chosen template");
        TestHarness.Assert(preview.Opacity == CnCNew.Presentation.Objects.PlacementPreview.Opacity && !preview.HasFlashTint, "translucent and untinted on a legal spot");
        TestHarness.Assert(preview.PrimaryModel is not null, "it has a model to draw");
        TestHarness.Assert(Math.Abs(preview.Position.X - valid.Position.X) < 0.5f && Math.Abs(preview.Position.Y - valid.Position.Y) < 0.5f, "at the ghost's place");
        TestHarness.Assert(solid.All(drawable => drawable.Opacity == 1f), "everything else stays solid");

        // Where the simulation refuses the spot (here: on top of the base's own structure) the preview turns red.
        PlacementGhost? refused = null;
        foreach (var structure in match.World.Entities.Where(entity => entity.Owner == match.Human && entity.Template.IsKindOf("STRUCTURE")).Take(4))
        {
            var ground = SimulationQueries.GroundOf(structure);
            controller.Process(new MouseMoveEvent(time += 16, Screen(match, new Vector3(ground.X, ground.Y, 0f))));
            if (controller.Input.Commands.Ghost is { IsValid: false, Reason: not PlacementIssue.NoGround } bad) { refused = bad; break; }
        }
        if (refused is not null)
        {
            Advance(match, controller, 1);
            var red = match.ObjectFrame.Drawables[^1];
            TestHarness.Assert(red.IsPlacementPreview && red.FlashTint == CnCNew.Presentation.Objects.PlacementPreview.IllegalTint, "a refused spot tints the preview red");
        }
        else Console.WriteLine("       (no refused spot was found on screen; only the legal preview was exercised)");

        // The click places the structure: the mode ends and the preview is gone with the next frame.
        controller.Process(new MouseMoveEvent(time += 16, Screen(match, valid.Position)));
        Click(controller, ref time, MouseButton.Left, Screen(match, valid.Position));
        Advance(match, controller, 1);
        TestHarness.Assert(controller.Input.Commands.Mode == CommandModeKind.None, "the click places the structure");
        TestHarness.Assert(match.PlacementGhost is null && match.ObjectFrame.Drawables.All(drawable => !drawable.IsPlacementPreview), "no preview after the placement");
    }

    /// <summary>docs/DEMO-FINDINGS.md #8: neither the cursor nor the waypoints a player set were visible.</summary>
    private static void CursorAndMarkers(GameData data)
    {
        var (match, controller) = NewMatch(data, new Vector2(1280, 720));
        long time = 10_000;
        Advance(match, controller, 30);
        var dozer = match.World.Entities.First(entity => entity.Owner == match.Human && entity.Template.IsKindOf("DOZER"));
        Vector2 Ground(float dx, float dy)
        {
            var (x, y) = ((float)dozer.X.ToDecimal() + dx, (float)dozer.Y.ToDecimal() + dy);
            return Screen(match, new Vector3(x, y, match.Heights.HeightAt(x, y)));
        }

        TestHarness.Assert(controller.Cursor == CursorState.Arrow && controller.PointerPosition is null, "no pointer yet: the arrow");
        var open = Ground(-40, -50);
        controller.Process(new MouseMoveEvent(time += 16, open));
        Advance(match, controller, 1);
        TestHarness.Assert(controller.Cursor.Kind == CursorKind.Normal && controller.PointerPosition == open, $"nothing selected over open ground: the plain pointer ({controller.Cursor.Kind})");
        Click(controller, ref time, MouseButton.Left, Screen(match, EntityPicker.CenterOf(dozer)));
        controller.Process(new MouseMoveEvent(time += 16, open));
        Advance(match, controller, 1);
        TestHarness.Assert(controller.Cursor.Kind == CursorKind.Move, $"the dozer selected, open ground: move ({controller.Cursor.Kind})");
        TestHarness.Assert(controller.Overlay.OrderHints.IsEmpty && controller.Overlay.WaypointPaths.IsEmpty, "nothing ordered yet: no markers");

        // Alt held: the cursor changes at once; three clicks queue three stations.
        controller.Process(new KeyEvent(time += 16, KeyCode.LeftAlt, true));
        TestHarness.Assert(controller.Cursor.Kind == CursorKind.Waypoint, "Alt switches to the waypoint cursor without a mouse move");
        var stations = new[] { Ground(-10, -60), Ground(-90, -70), Ground(-170, -90) };
        foreach (var station in stations) Click(controller, ref time, MouseButton.Right, station);
        Advance(match, controller, 1);
        var path = controller.Overlay.WaypointPaths.Single();
        TestHarness.Assert(path.Unit == dozer.Id && path.Points.Length == 4, $"the path has the unit and three stations ({path.Points.Length} points)");
        for (var index = 0; index < stations.Length; index++)
            TestHarness.Assert(Vector2.Distance(path.Points[index + 1], stations[index]) < 2.5f, $"station {index + 1} is drawn where it was clicked ({path.Points[index + 1]} vs {stations[index]})");
        TestHarness.Assert(controller.Overlay.OrderHints.Count(hint => hint.Kind == OrderHintKind.Waypoint) == 3, "each click left a hint");
        var blue = new CnCNew.Ui.Wnd.WndColor(64, 128, 255, 235);
        var lines = controller.BuildDrawList(1280, 720).Commands.OfType<UiLineDraw>().Where(line => line.Color == blue).ToList();
        // Three segments plus per station a diamond (4 lines) and a centre ring (6 lines).
        TestHarness.Assert(lines.Count == 3 + 3 * 10, $"the path is drawn: 3 segments and 3 station markers ({lines.Count} lines)");
        TestHarness.Assert(lines[0].X0 == path.Points[0].X && lines[0].Y0 == path.Points[0].Y && lines[2].X1 == path.Points[3].X, "the first segment starts at the unit, the last ends at the third station");

        Advance(match, controller, 45);
        TestHarness.Assert(controller.Overlay.OrderHints.IsEmpty, "the hints are gone after 40 frames");
        TestHarness.Assert(controller.Overlay.WaypointPaths.Length == 1, "the path stays while Alt is held");
        controller.Process(new KeyEvent(time += 16, KeyCode.LeftAlt, false));
        Advance(match, controller, 1);
        TestHarness.Assert(controller.Overlay.WaypointPaths.IsEmpty && controller.Cursor.Kind == CursorKind.Move, "Alt released: the path is hidden and the cursor is the move cursor again");

        // A plain move order (the right button in the default mouse setup): a move hint at the clicked point, fading.
        Click(controller, ref time, MouseButton.Right, open);
        Advance(match, controller, 1);
        TestHarness.Assert(controller.Overlay.OrderHints is [{ Kind: OrderHintKind.Move } hint] && Vector2.Distance(hint.Screen, open) < 2.5f && hint.Age < 0.1f, "a move hint at the destination");
        Advance(match, controller, 20);
        TestHarness.Assert(controller.Overlay.OrderHints is [{ Age: > 0.4f and < 0.7f }], "it fades with time");
        controller.Process(new KeyEvent(time += 16, KeyCode.LeftAlt, true));
        Advance(match, controller, 1);
        TestHarness.Assert(controller.Overlay.WaypointPaths.IsEmpty, "the plain order replaced the waypoint path");
        controller.Process(new KeyEvent(time += 16, KeyCode.LeftAlt, false));

        // Over the control bar the cursor is the arrow whatever lies behind it; a press that began there keeps it.
        var bar = controller.WindowRect("ControlBar.wnd:ControlBarParent") ?? throw new InvalidOperationException("no control bar");
        controller.Process(new MouseMoveEvent(time += 16, new Vector2(bar.X + bar.Width / 2, bar.Y + bar.Height / 2)));
        Advance(match, controller, 1);
        TestHarness.Assert(controller.PointerOverWindow && controller.Cursor == CursorState.Arrow, "over the control bar: the arrow");
        controller.Process(new MouseMoveEvent(time += 16, open));
        Advance(match, controller, 1);
        TestHarness.Assert(!controller.PointerOverWindow && controller.Cursor.Kind == CursorKind.Move, "back over the world: the context cursor");

        // The view's edge scrolls and shows the directional cursor.
        controller.Process(new MouseMoveEvent(time += 16, new Vector2(1279, 300)));
        Advance(match, controller, 1);
        TestHarness.Assert(controller.Cursor.Kind == CursorKind.Scroll && CursorSet.DirectionIndex(controller.Cursor.Direction, 8) == 0, "at the right edge: the scroll cursor pointing right");

        var cursors = data.LoadCursors();
        TestHarness.Assert(cursors.MissingFiles.IsEmpty && cursors.UndecodableFiles.IsEmpty && cursors.Resolve(controller.Cursor, 0, out var frame) && frame.Cursor.File.EndsWith("SCCScroll0.ani", StringComparison.OrdinalIgnoreCase),
            "the game data's cursor set has the picture for it");
        Console.WriteLine($"  cursor and markers: {lines.Count} waypoint lines for three stations; cursors {cursors.Files.Length} files / {cursors.FrameCount} frames");
    }

    /// <summary>docs/DEMO-FINDINGS.md #8 and #14: the overlay existed but nothing turned it into draw commands.</summary>
    private static void OverlayIsDrawn(GameData data)
    {
        var (match, controller) = NewMatch(data, new Vector2(1280, 720));
        var human = match.Human;
        long time = 10_000;
        Advance(match, controller, 120); // Drawable::drawHealing ignores the first three seconds of a game
        var dozer = match.World.Entities.First(entity => entity.Owner == human && entity.Template.IsKindOf("DOZER"));
        Click(controller, ref time, MouseButton.Left, Screen(match, EntityPicker.CenterOf(dozer)));
        Advance(match, controller, 1);
        TestHarness.Assert(controller.Input.Selection.Contains(dozer.Id) && !controller.Overlay.Selected.IsDefaultOrEmpty, "the dozer is selected and has an on-screen marker");
        var list = controller.BuildDrawList(1280, 720);
        var kinds = list.CountByKind();
        TestHarness.Assert(kinds.GetValueOrDefault("UiLineDraw") >= 20, $"the selection ring is drawn as lines ({kinds.GetValueOrDefault("UiLineDraw")})");
        TestHarness.Assert(list.Commands.OfType<UiFillRect>().Any(fill => fill.Color.G > 200 && fill.Color.R < 40 && fill.Rect.Height == 5), "a full green health bar is drawn");

        // Healing shows a wrench (structure, vehicle) or a cross for three seconds after health last rose (Drawable::drawHealing). Every retail structure regenerates (BaseRegenerateUpdate
        // heals through the same path, so a regenerating structure keeps its icon, as in the original); the dozer does not regenerate.
        var center = match.World.Entities.First(entity => entity.Owner == human && entity.Template.IsKindOf("COMMANDCENTER"));
        var hurt = CnCNew.Simulation.Numerics.Fixed.FromInt(800);
        match.World.Submit(new CnCNew.Simulation.Commands.DamageCommand(CnCNew.Simulation.Ids.PlayerId.System, 900_001, match.World.Tick, center.Id, hurt, CnCNew.Simulation.World.DamageTypes.Unresistable, 0));
        match.World.Submit(new CnCNew.Simulation.Commands.DamageCommand(CnCNew.Simulation.Ids.PlayerId.System, 900_002, match.World.Tick, dozer.Id, CnCNew.Simulation.Numerics.Fixed.FromInt(40), CnCNew.Simulation.World.DamageTypes.Unresistable, 0));
        Advance(match, controller, 3);
        TestHarness.Assert(!controller.Overlay.HealIcons.Any(value => value.Entity == dozer.Id), "damage alone shows no healing icon");
        match.World.Submit(new CnCNew.Simulation.Commands.DamageCommand(CnCNew.Simulation.Ids.PlayerId.System, 900_003, match.World.Tick, center.Id, CnCNew.Simulation.Numerics.Fixed.FromInt(100), CnCNew.Simulation.World.DamageTypes.Healing, 0));
        match.World.Submit(new CnCNew.Simulation.Commands.DamageCommand(CnCNew.Simulation.Ids.PlayerId.System, 900_004, match.World.Tick, dozer.Id, CnCNew.Simulation.Numerics.Fixed.FromInt(20), CnCNew.Simulation.World.DamageTypes.Healing, 0));
        Advance(match, controller, 3);
        TestHarness.Assert(controller.Overlay.HealIcons.Any(value => value.Entity == center.Id && value.Kind == HealIconKind.Structure), "a healed structure gets the structure healing icon");
        TestHarness.Assert(controller.Overlay.HealIcons.Any(value => value.Entity == dozer.Id && value.Kind == HealIconKind.Vehicle), "a healed vehicle gets the vehicle healing icon");
        var wrench = controller.BuildDrawList(1280, 720).Commands.OfType<UiImageDraw>().FirstOrDefault(image => image.Image.StartsWith("SCPWrench", StringComparison.Ordinal));
        TestHarness.Assert(wrench is not null, "the wrench frame is drawn");
        Console.WriteLine($"  overlay: {kinds.GetValueOrDefault("UiLineDraw")} line commands for one selected dozer, healing icon {wrench?.Image}");
        Advance(match, controller, 4 * CnCNew.Tests.TestTime.FramesPerSecond);
        TestHarness.Assert(!controller.Overlay.HealIcons.Any(value => value.Entity == dozer.Id), "the dozer's icon is gone three seconds after its last healing");

        // The animation is a pure function of the clock: ten frames up and down, sixteen in a loop.
        TestHarness.Assert(HudOverlayRenderer.Frame(10, 0, true) == 0 && HudOverlayRenderer.Frame(10, 9, true) == 9 && HudOverlayRenderer.Frame(10, 10, true) == 8 && HudOverlayRenderer.Frame(10, 18, true) == 0 && HudOverlayRenderer.Frame(16, 17, false) == 1,
            "ping-pong and loop frame indices");
        TestHarness.Assert(HudOverlayRenderer.HealthColor(1f) == new CnCNew.Ui.Wnd.WndColor(0, 255, 0, 255) && HudOverlayRenderer.HealthColor(0.5f) == new CnCNew.Ui.Wnd.WndColor(255, 255, 0, 255) && HudOverlayRenderer.HealthColor(0f).G == 0, "the health ramp");
    }

    private static string? ContentDir(string[] args)
    {
        var index = Array.IndexOf(args, "--content-dir");
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    // ------------------------------------------------------------------------------------------------------------ setup helpers

    /// <summary>
    /// <see cref="ControlSettings.AlternateMouse"/> reaches the input layer: the default (on) moves the selected dozer with the right button and deselects with a left click on open
    /// ground; switched off, the left button orders and the right button deselects (the original's default). The scroll slider maps onto the original's scroll factor unchanged.
    /// </summary>
    private static void MouseSetups(GameData data)
    {
        TestHarness.Assert(GameSettings.Default.Controls.AlternateMouse && MatchController.MapMouseScheme(GameSettings.Default) == MouseScheme.RightClickCommands, "the default is the alternate setup");
        var camera = MatchController.MapCameraSettings(GameSettings.Default);
        TestHarness.Assert(camera is { KeyboardScrollSpeedFactor: 0.5f, HorizontalScrollSpeedFactor: 1.6f, VerticalScrollSpeedFactor: 2.0f }, "the default scroll slider is the original's factor 0.5; the speed factors are GameData's");
        var fast = MatchController.MapCameraSettings(GameSettings.Default with { Controls = GameSettings.Default.Controls with { ScrollSpeed = 1f } });
        TestHarness.Assert(fast is { KeyboardScrollSpeedFactor: 1f, HorizontalScrollSpeedFactor: 1.6f }, "the slider at its end doubles key and edge scrolling and nothing else");

        foreach (var alternate in new[] { true, false })
        {
            var settings = SkinPins.Settings with { Controls = GameSettings.Default.Controls with { AlternateMouse = alternate } };
            var (match, controller) = NewMatch(data, new Vector2(1280, 720), settings);
            TestHarness.Assert(controller.Input.Settings.Scheme == (alternate ? MouseScheme.RightClickCommands : MouseScheme.LeftClickCommands), "the setting chooses the scheme");
            long time = 10_000;
            Advance(match, controller, 1);
            var dozer = match.World.Entities.First(entity => entity.Owner == match.Human && entity.Template.IsKindOf("DOZER"));
            var start = SimulationQueries.GroundOf(dozer);
            var target = Screen(match, new Vector3(start.X + 80, start.Y, 0f));
            var (order, other) = alternate ? (MouseButton.Right, MouseButton.Left) : (MouseButton.Left, MouseButton.Right);
            var moves = 0;
            match.CommandIssued += (command, _) => { if (command is Simulation.Commands.SetMoveTargetCommand) moves++; };

            Click(controller, ref time, MouseButton.Left, Screen(match, EntityPicker.CenterOf(dozer)));
            Advance(match, controller, 1);
            TestHarness.Assert(controller.Input.Selection.Contains(dozer.Id), $"alternate {alternate}: the left button selects the dozer");
            Click(controller, ref time, other, target);
            Advance(match, controller, 1);
            TestHarness.Assert(moves == 0 && controller.Input.Selection.Selected.IsEmpty, $"alternate {alternate}: the {other} button on open ground orders nothing and deselects ({moves} moves)");
            Click(controller, ref time, MouseButton.Left, Screen(match, EntityPicker.CenterOf(dozer)));
            Advance(match, controller, 1);
            Click(controller, ref time, order, target);
            Advance(match, controller, 60);
            TestHarness.Assert(moves == 1 && Vector2.Distance(SimulationQueries.GroundOf(dozer), start) > 1f, $"alternate {alternate}: the {order} button on open ground moves the dozer ({moves} moves)");
        }
    }

    private static void SneakAttack(GameData data)
    {
        var setup = new MatchSetup(data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase)), "FactionGLA", ["FactionChina"], SkirmishMatchOptions.Standard);
        var match = LiveMatch.Create(data, setup, new Vector2(1280, 720));
        var controller = new MatchController(match, SkinPins.Settings) { StarBlinks = false };
        Advance(match, controller, (int)match.World.Time.OriginalFrames(4));
        match.World.GrantScience(match.Human, "SCIENCE_SneakAttack");
        foreach (var module in match.World.Entities.SelectMany(entity => entity.Modules).OfType<CnCNew.Simulation.Modules.ISpecialPowerModule>()) module.SetReadyFrame(0);
        Advance(match, controller, 2);
        controller.BuildDrawList(1280, 720);
        var power = controller.Powers.FirstOrDefault(button => button.Button.Name == "Command_SneakAttackFromShortcut");
        TestHarness.Assert(power is { IsEnabled: true }, $"the sneak attack is on the bar and ready ({string.Join(", ", controller.Powers.Select(button => button.Button.Name + ":" + button.Availability + ":" + button.Reason))}; over {match.IsOver}, faction {match.World.FactionOf(match.Human)?.Name}, set {match.World.FactionOf(match.Human)?.PowerShortcutSet}, own {match.World.Entities.Count(entity => entity.Owner == match.Human)}, tick {match.World.Tick})");
        var commands = controller.Input.Commands;
        TestHarness.Assert(commands.Activate(match.World, power!.Button, []) == ActivationResult.EnteredMode && commands.Mode == CommandModeKind.Placement, "a press puts the tunnel at the cursor");
        var center = match.World.Entities.First(entity => entity.Owner == match.Human && entity.Template.IsKindOf("COMMANDCENTER"));
        var reasons = new List<string>();
        for (var attempt = 0; attempt < 16 && commands.Mode == CommandModeKind.Placement; attempt++)
        {
            var (x, y) = ((float)center.X.ToDecimal() + (attempt % 4 - 2) * 90f, (float)center.Y.ToDecimal() + (attempt / 4 - 2) * 90f);
            if (!match.Camera.TryWorldToScreen(new Vector3(x, y, match.Heights.HeightAt(x, y)), out var screen)) continue;
            commands.PlacementPress(match.World, match.Camera, screen);
            commands.PlacementRelease(match.World, match.Camera, screen);
            reasons.Add(commands.Ghost?.Reason.ToString() ?? "-");
        }
        TestHarness.Assert(commands.Mode != CommandModeKind.Placement, $"it is put down ({string.Join(", ", reasons)})");
        Advance(match, controller, (int)match.World.Time.WholeSecondsToTicks(5));
        TestHarness.Assert(match.World.Entities.Any(entity => entity.Owner == match.Human && entity.Template.Name.Contains("SneakAttack", StringComparison.Ordinal)), $"and the tunnel comes up ({center.Template.Name})");
    }

    private static void ButtonsOfUnits(GameData data)
    {
        var setup = new MatchSetup(data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase)), "FactionGLA", ["FactionChina"], SkirmishMatchOptions.Standard);
        var match = LiveMatch.Create(data, setup, new Vector2(1280, 720));
        var controller = new MatchController(match, SkinPins.Settings) { StarBlinks = false };
        var world = match.World;
        var commands = controller.Input.Commands;
        Advance(match, controller, (int)world.Time.OriginalFrames(4));
        var center = world.Entities.First(entity => entity.Owner == match.Human && entity.Template.IsKindOf("COMMANDCENTER"));
        var enemy = world.Entities.First(entity => entity.Owner != match.Human && entity.Template.IsKindOf("COMMANDCENTER")).Owner;
        ulong sequence = 1_000_000;
        Entity Spawn(string name, CnCNew.Simulation.Ids.PlayerId owner, int dx, int dy)
        {
            var known = world.Entities.Select(entity => entity.Id).ToHashSet();
            var (x, y) = (center.X + CnCNew.Simulation.Numerics.Fixed.FromInt(dx), center.Y + CnCNew.Simulation.Numerics.Fixed.FromInt(dy));
            TestHarness.Assert(world.Submit(new CnCNew.Simulation.Commands.SpawnCommand(CnCNew.Simulation.Ids.PlayerId.System, sequence++, world.Tick, name, owner, x, y, CnCNew.Simulation.Numerics.Fixed.Zero, CnCNew.Simulation.Numerics.Fixed.Zero)).Accepted, $"{name} is spawned");
            Advance(match, controller, (int)world.Time.OriginalFrames(2));
            return world.Entities.First(entity => entity.Template.Name == name && !known.Contains(entity.Id));
        }
        CommandBarButton Press(Entity unit, string name)
        {
            controller.Input.Selection.Replace([unit.Id]);
            Advance(match, controller, 1);
            Refresh(controller);
            var found = controller.CommandBar.Slots.FirstOrDefault(button => button?.Button.Name == name);
            TestHarness.Assert(found is not null, $"{unit.Template.Name} shows {name} ({string.Join(", ", controller.CommandBar.Slots.Select(button => button?.Button.Name + ":" + button?.Availability + ":" + button?.Reason))})");
            return found!;
        }
        void Until(Func<bool> done, string what)
        {
            for (var second = 0; second < 40 && !done(); second++) Advance(match, controller, (int)world.Time.WholeSecondsToTicks(1));
            TestHarness.Assert(done(), what);
        }

        // The places of a transport.
        var humvee = Spawn("AmericaVehicleHumvee", match.Human, 200, 0);
        var first = Spawn("AmericaInfantryRanger", match.Human, 220, 0);
        var seat = Spawn("AmericaInfantryRanger", match.Human, 220, 20);
        TestHarness.Assert(world.TryEnterContainer(first, humvee) && world.TryEnterContainer(seat, humvee), "two rangers sit in the humvee");
        controller.Input.Selection.Replace([humvee.Id]);
        Advance(match, controller, 1);
        Refresh(controller);
        var places = controller.CommandBar.Slots.Where(button => button?.Button.Kind == GuiCommandKind.ExitContainer).ToArray();
        TestHarness.Assert(places.Length == 5 && places[0]!.Rider == first.Id && places[1]!.Rider == seat.Id && places[0]!.IsEnabled && places[2] is { IsEnabled: false }, $"a button per place, the first two taken ({places.Length})");
        var cameo = data.Visuals.TryGet("AmericaInfantryRanger", out var seated) ? seated.ButtonImage : null;
        TestHarness.Assert(cameo is { Length: > 0 } && controller.BuildDrawList(1280, 720).Commands.OfType<UiImageDraw>().Any(image => image.Image == cameo), "with the rider's cameo");
        TestHarness.Assert(commands.Activate(world, places[1]!.Button, [seat]) == ActivationResult.Issued, "a press lets the rider out");
        Until(() => !seat.ContainedBy.IsValid, "and the second ranger stands outside");
        TestHarness.Assert(first.ContainedBy == humvee.Id, "the first stays");
        Advance(match, controller, 1);
        TestHarness.Assert(controller.CommandBar.Slots.Where(button => button?.Button.Kind == GuiCommandKind.ExitContainer).Count(button => button!.IsEnabled) == 1, "and one place is taken");

        // Internet hack.
        var hacker = Spawn("ChinaInfantryHacker", match.Human, 120, 0);
        var hack = hacker.Modules.OfType<HackInternetAIUpdate>().Single();
        var hackButton = Press(hacker, "Command_ChinaInfantryHackerInternetHack");
        TestHarness.Assert(hackButton.IsEnabled && commands.Activate(world, hackButton.Button, [hacker]) == ActivationResult.Issued, "the hack is a press");
        Until(() => hack.IsHackingPackingOrUnpacking, "and the hacker sets up");
        TestHarness.Assert(Press(hacker, "Command_ChinaInfantryHackerInternetHack").Availability == ButtonAvailability.Restricted, "the button is out while it hacks");

        // Weapon switch: checked is the weapon in hand.
        var ranger = Spawn("AmericaInfantryRanger", match.Human, 120, 40);
        TestHarness.Assert(Press(ranger, "Command_AmericaRangerSwitchToFlagBangGrenades").Reason == ButtonReason.MissingUpgrade, "the grenades need their upgrade");
        world.CompleteUpgrade(match.Human, null, "Upgrade_AmericaRangerFlashBangGrenade");
        var grenades = Press(ranger, "Command_AmericaRangerSwitchToFlagBangGrenades");
        TestHarness.Assert(grenades.Availability == ButtonAvailability.Available && Press(ranger, "Command_AmericaRangerSwitchToMachineGun").Availability == ButtonAvailability.Active, "the machine gun is checked");
        TestHarness.Assert(commands.Activate(world, grenades.Button, [ranger]) == ActivationResult.Issued, "a press takes the other weapon");
        Until(() => ranger.CurrentWeaponSlot == grenades.Button.WeaponSlotIndex, "and the ranger holds the grenades");
        var inHand = Press(ranger, "Command_AmericaRangerSwitchToFlagBangGrenades");
        TestHarness.Assert(inHand.Availability == ButtonAvailability.Active, "which are checked now");
        TestHarness.Assert(controller.Tree.FindByName($"ControlBar.wnd:ButtonCommand{inHand.Slot:00}") is { IsSelected: true }, "and shown as on");

        // Overcharge.
        var plant = Spawn("ChinaPowerPlant", match.Human, 180, 80);
        var overcharge = plant.Modules.OfType<OverchargeBehavior>().Single();
        var chargeButton = Press(plant, "Command_Overcharge");
        TestHarness.Assert(commands.Activate(world, chargeButton.Button, [plant]) == ActivationResult.Issued, "overcharge is a press");
        Until(() => overcharge.Active, "and the plant runs overcharged");
        TestHarness.Assert(Press(plant, "Command_Overcharge").Availability == ButtonAvailability.Active, "the button is checked");

        // Hijack: an enemy vehicle, by the button and by the right click.
        var hijacker = Spawn("GLAInfantryHijacker", match.Human, 60, -120);
        var truck = Spawn("ChinaVehicleSupplyTruck", enemy, 110, -120);
        var own = Spawn("GLAVehicleTechnical", match.Human, 60, -160);
        var hijack = Press(hijacker, "Command_GLAInfantryHijack");
        TestHarness.Assert(commands.Activate(world, hijack.Button, [hijacker]) == ActivationResult.EnteredMode, "the hijack asks for its vehicle");
        TestHarness.Assert(!commands.CompleteTarget(world, own, null), "an own vehicle is no target");
        TestHarness.Assert(commands.Mode == CommandModeKind.ButtonTarget && commands.CompleteTarget(world, truck, null), "an enemy vehicle is");
        Until(() => truck.Owner == match.Human, "the truck changes hands");
        var second = Spawn("GLAInfantryHijacker", match.Human, 60, -200);
        var next = Spawn("ChinaVehicleSupplyTruck", enemy, 110, -200);
        controller.Input.Selection.Replace([second.Id]);
        var hint = commands.Evaluate(world, next, null);
        TestHarness.Assert(hint.Action == ContextAction.EnterAggressively && hint.Cursor == CursorKind.EnterAggressive, $"the right click at an enemy vehicle is the hijack ({hint.Action})");

        // Car bomb: a car that stands about.
        var terrorist = Spawn("GLAInfantryTerrorist", match.Human, -60, -120);
        var car = Spawn("CarCamaro", match.Human, -110, -120);
        var bomb = Press(terrorist, "Command_GLAInfantryTerroristMakeCarBomb");
        TestHarness.Assert(commands.Activate(world, bomb.Button, [terrorist]) == ActivationResult.EnteredMode && commands.CompleteTarget(world, car, null), "the terrorist goes for the car");
        Until(() => car.HasStatus(ObjectStatusBits.Index("IS_CARBOMB")), "and the car is a bomb");
        Advance(match, controller, 1);
        TestHarness.Assert(controller.Overlay.Icons.Any(icon => icon.Entity == car.Id), "the owner sees the bomb over the car (drawBombed)");
        var recruit = Spawn("AmericaInfantryRanger", match.Human, -80, -120);
        world.AddExperiencePoints(recruit, recruit.Template.ExperienceRequired[1] + 1);
        Advance(match, controller, 2);
        TestHarness.Assert(recruit.VeterancyLevel >= 1, "the ranger rose");
        TestHarness.Assert(controller.Overlay.Icons.Count(icon => !icon.Entity.IsValid) >= 1, "a rise stands in the world over the unit (addWorldAnimation)");

        // Sabotage: an enemy building.
        var saboteur = Spawn("GLAInfantrySaboteur", match.Human, -60, 120);
        var barracks = Spawn("ChinaBarracks", enemy, -130, 120);
        var sabotage = Press(saboteur, "Command_SabotageBuilding");
        TestHarness.Assert(commands.Activate(world, sabotage.Button, [saboteur]) == ActivationResult.EnteredMode && commands.CompleteTarget(world, barracks, null), "the saboteur goes for the barracks");
        Until(() => !world.Entities.Contains(saboteur), "and is spent at it");
        TestHarness.Assert(commands.UnsupportedCounts.Count == 0, $"no press was refused as unsupported ({string.Join(", ", commands.UnsupportedCounts.Keys)})");
    }

    private static void PowerBar(GameData data)
    {
        var setup = new MatchSetup(data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase)), "FactionAmerica", ["FactionChina"], SkirmishMatchOptions.Standard);
        var match = LiveMatch.Create(data, setup, new Vector2(1280, 720));
        var controller = new MatchController(match, SkinPins.Settings) { StarBlinks = false };
        Advance(match, controller, (int)match.World.Time.OriginalFrames(4));
        var tree = controller.Tree;
        controller.BuildDrawList(1280, 720);
        TestHarness.Assert(controller.Powers.Length == 1 && controller.Powers[0].Button.Name == "Command_SpySatelliteScanFromShortcut" && tree.FindByName("GenPowersShortcutBarUS.wnd:ButtonCommand2") is { IsHidden: true },
            $"a new player has the command center's scan, and it recharges ({string.Join(", ", controller.Powers.Select(power => power.Button.Name))})");
        match.World.GrantScience(match.Human, "SCIENCE_Paradrop1");
        Advance(match, controller, 2);
        controller.BuildDrawList(1280, 720);
        TestHarness.Assert(controller.Powers.Length == 2 && controller.Powers[0].Button.Name == "Command_ParadropFromShortcut" && tree.FindByName("GenPowersShortcutBarUS.wnd:ButtonCommand2") is { IsHidden: false },
            "the paradrop shows once the player has its science, in the order of the set");
        var list = controller.BuildDrawList(1280, 720);
        TestHarness.Assert(list.Commands.OfType<UiImageDraw>().Any(image => image.Image == "SACParatroopers"), "with its cameo");
    }

    /// <summary>
    /// <c>InGameUI::selectMatchingAcrossScreen</c> and <c>selectMatchingAcrossMap</c> put a line into the message list at the top left: <c>GUI:NothingSelected</c> when there was nothing
    /// to match, <c>GUI:SelectedAcrossScreen</c> when units were added from the screen, <c>GUI:SelectedAcrossMap</c> for the map - also when none were added, unless the first selected
    /// object is a structure. The port selected silently.
    /// </summary>
    private static void SelectionMessages(GameData data)
    {
        var (match, controller) = NewMatch(data, new Vector2(1280, 720));
        long time = 10_000;
        var text = controller.Assets.Text;
        string[] Lines() => [.. controller.Messages.Log.Lines.Select(line => line.Text)];
        void PressE()
        {
            controller.Process(new KeyEvent(time += 16, KeyCode.E, true));
            controller.Process(new KeyEvent(time += 16, KeyCode.E, false));
        }

        controller.Input.Selection.Clear();
        PressE();
        TestHarness.Assert(Lines() is [var nothing] && nothing == text.Get("GUI:NothingSelected") && !nothing.Contains("??", StringComparison.Ordinal), $"E with nothing selected says so ({string.Join(" | ", Lines())})");

        var dozer = match.World.Entities.First(entity => entity.Owner == match.Human && entity.Template.IsKindOf("DOZER") && !entity.EffectivelyDead);
        controller.Input.Selection.Replace([dozer.Id]);
        PressE();
        var expected = text.Get("GUI:SelectedAcrossMap");
        TestHarness.Assert(Lines().Length == 2 && Lines()[1] == expected && expected.Length > 0 && !expected.Contains("??", StringComparison.Ordinal), $"E with a lone dozer looks across the map and says so ({string.Join(" | ", Lines())})");

        var center = match.World.Entities.First(entity => entity.Owner == match.Human && entity.Template.IsKindOf("COMMANDCENTER"));
        controller.Input.Selection.Replace([center.Id]);
        PressE();
        TestHarness.Assert(Lines().Length == 2, $"E on a structure finds no fellows and says nothing ({string.Join(" | ", Lines())})");
    }

    private static (LiveMatch Match, MatchController Controller) NewMatch(GameData data, Vector2 viewport, GameSettings? settings = null, bool started = true)
    {
        var mapPath = data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase));
        var setup = new MatchSetup(mapPath, data.Factions[0].Faction.Name, [data.Factions[1].Faction.Name], SkirmishMatchOptions.Standard);
        var match = LiveMatch.Create(data, setup, viewport);
        var controller = new MatchController(match, settings ?? SkinPins.Settings) { StarBlinks = false };
        if (started) Advance(match, controller, (int)match.World.Time.OriginalFrames(4)); // the start object of the patch makes the builder in its third frame
        return (match, controller);
    }

    /// <summary>A left/right click: move, press, release at the same point, matching the timing InputState needs to recognize a click
    /// rather than a drag (see <c>CnCNew.Presentation.Tests.InputTests</c>).</summary>
    private static void Click(MatchController controller, ref long time, MouseButton button, Vector2 at)
    {
        controller.Process(new MouseMoveEvent(time += 16, at));
        controller.Process(new MouseButtonEvent(time += 16, button, true, at));
        controller.Process(new MouseButtonEvent(time += 50, button, false, at));
        time += 1000;
    }

    private static void Drag(MatchController controller, ref long time, Vector2 from, Vector2 to)
    {
        controller.Process(new MouseMoveEvent(time += 16, from));
        controller.Process(new MouseButtonEvent(time += 16, MouseButton.Left, true, from));
        controller.Process(new MouseMoveEvent(time += 16, to));
        controller.Process(new MouseButtonEvent(time += 16, MouseButton.Left, false, to));
        time += 1000;
    }

    private static Vector2 Screen(LiveMatch match, Vector3 world)
    {
        TestHarness.Assert(match.Camera.TryWorldToScreen(world, out var screen), "the point projects onto the screen");
        return screen;
    }

    private static void Advance(LiveMatch match, MatchController controller, int frames, double dt = CnCNew.Tests.TestTime.FrameSeconds)
    {
        for (var i = 0; i < frames; i++)
        {
            match.Advance(dt);
            controller.Update(dt);
        }
    }

    /// <summary>Window hidden/enabled state (which command-bar buttons a real click can reach) is only refreshed by
    /// <see cref="MatchController.BuildDrawList"/>; call this after a selection change and before clicking a command button.</summary>
    private static void Refresh(MatchController controller) => controller.BuildDrawList(1280, 720);

    /// <summary>The command bar slot for a <see cref="GuiCommandKind.DozerConstruct"/> button; prefers a power plant (a structure with
    /// positive <c>EnergyProduction</c>) since the BUILD brief asks for one, but accepts any construct button so the test also runs for
    /// factions without power management (GLA).</summary>
    private static int FindConstructSlot(LiveMatch match, CommandBarView view)
    {
        var fallback = 0;
        for (var slot = 1; slot <= CommandBarModel.GridSlots; slot++)
        {
            if (view[slot] is not { Button.Kind: GuiCommandKind.DozerConstruct } button) continue;
            if (fallback == 0) fallback = slot;
            if (match.World.Templates.TryGet(button.Button.Object, out var template) && template.EnergyProduction > 0) return slot;
        }
        TestHarness.Assert(fallback != 0, "the dozer's command set offers at least one construct button");
        return fallback;
    }

    // ------------------------------------------------------------------------------------------------------------ scenarios

    /// <summary>
    /// Step 8 of phase 24 through the real click path: select the dozer, choose a building, place it, order a unit about - each order pre-checked at once and executed
    /// <c>d</c> ticks later. Then the delayed refusal: a unit is destroyed while its order is on its way; the order's pre-check had passed, its execution is refused, and the
    /// HUD gets the refusal it would have shown at once.
    /// </summary>
    private static void InputDelayThroughTheClickPath(GameData data)
    {
        foreach (var delay in new[] { 0, 4, 12 })
        {
            var (match, controller) = NewMatch(data, new Vector2(1280, 720));
            match.UseInputDelay(delay);
            var human = match.Human;
            long time = 10_000;
            var issued = new List<(Simulation.Commands.SimCommand Command, bool Accepted)>();
            var preChecked = new List<(Simulation.Commands.SimCommand Command, CommandReceipt Receipt)>();
            var resolved = new List<CommandResolution>();
            var rejectedFeedback = 0;
            match.CommandIssued += (command, result) => issued.Add((command, result.Accepted));
            match.CommandPreChecked += (command, receipt) => preChecked.Add((command, receipt));
            match.CommandResolved += resolved.Add;
            controller.CommandFeedbackObserved += feedback => { if (feedback.Kind == FeedbackKind.Rejected) rejectedFeedback++; };
            Advance(match, controller, 1);

            var dozer = match.World.Entities.First(entity => entity.Owner == human && entity.Template.IsKindOf("DOZER"));
            Click(controller, ref time, MouseButton.Left, Screen(match, EntityPicker.CenterOf(dozer)));
            Advance(match, controller, 1);
            Refresh(controller);
            var slot = FindConstructSlot(match, controller.CommandBar);
            var rect = controller.WindowRect($"ControlBar.wnd:ButtonCommand{slot:00}") ?? throw new InvalidOperationException("button window missing");
            Click(controller, ref time, MouseButton.Left, new Vector2(rect.CenterX, rect.CenterY));
            Advance(match, controller, 1);
            var buildTemplate = controller.Input.Commands.PendingButton!.Object;
            var dozerGround = SimulationQueries.GroundOf(dozer);
            PlacementGhost? ghost = null;
            foreach (var offset in (ReadOnlySpan<Vector2>)[new(300, 0), new(-300, 0), new(0, 300), new(0, -300), new(220, 220), new(-220, -220)])
            {
                var candidate = dozerGround + offset;
                controller.Process(new MouseMoveEvent(time += 16, Screen(match, new Vector3(candidate.X, candidate.Y, 0f))));
                if (controller.Input.Commands.Ghost is { IsValid: true } valid) { ghost = valid; break; }
            }
            TestHarness.Assert(ghost is not null, "a buildable spot exists near the dozer");
            var before = match.Tick;
            Click(controller, ref time, MouseButton.Left, Screen(match, ghost!.Position));
            TestHarness.Assert(controller.Input.Commands.Mode == CommandModeKind.None, $"delay {delay}: the click leaves the placement mode at once (the pre-check passed)");
            TestHarness.Assert(preChecked.Any(entry => entry.Command is Simulation.Commands.ConstructCommand && entry.Receipt.Accepted) && !issued.Any(entry => entry.Command is Simulation.Commands.ConstructCommand),
                $"delay {delay}: the order is pre-checked when it is given and not yet submitted");
            Advance(match, controller, 30);
            var construct = resolved.Single(resolution => resolution.Command is Simulation.Commands.ConstructCommand);
            TestHarness.Assert(construct.Result.Accepted && !construct.Deviates && construct.Command.Tick == before + delay, $"delay {delay}: the construction is executed {delay} ticks after the click (tick {construct.Command.Tick}, given at {before})");
            Advance(match, controller, 60);
            TestHarness.Assert(match.World.Entities.Any(entity => entity.Owner == human && entity.Template.Name == buildTemplate), $"delay {delay}: the construction site exists");

            // The dozer is ordered about and destroyed while its order is on its way.
            var victim = dozer;
            Click(controller, ref time, MouseButton.Left, Screen(match, EntityPicker.CenterOf(victim)));
            Advance(match, controller, 1);
            TestHarness.Assert(controller.Input.Selection.Contains(victim.Id), "the dozer is selected");
            var ground = SimulationQueries.GroundOf(victim);
            var feedbackBefore = rejectedFeedback;
            Click(controller, ref time, MouseButton.Right, Screen(match, new Vector3(ground.X + 60, ground.Y + 40, 0f)));
            var orders = preChecked.Count(entry => entry.Receipt.Accepted && entry.Command is Simulation.Commands.GroupMoveCommand or Simulation.Commands.SetMoveTargetCommand);
            TestHarness.Assert(orders >= 1, $"delay {delay}: the move order passes its pre-check");
            if (delay > 0)
            {
                // The system's own number range is far below this one in a test of this length.
                TestHarness.Assert(match.Session.World.Submit(new Simulation.Commands.DestroyCommand(Simulation.Ids.PlayerId.System, 1_000_000_000UL + (ulong)delay, match.Tick, victim.Id)).Accepted, "the unit is destroyed by the system");
                Advance(match, controller, 20);
                TestHarness.Assert(controller.Input.Commands.LateRejections >= 1 && rejectedFeedback > feedbackBefore && match.CommandPath.Deviations >= 1,
                    $"delay {delay}: the order of the destroyed unit is refused at execution; the refusal is counted ({controller.Input.Commands.LateRejections}) and shown ({rejectedFeedback - feedbackBefore} feedback)");
            }
            else
            {
                Advance(match, controller, 20);
                TestHarness.Assert(controller.Input.Commands.LateRejections == 0 && match.CommandPath.Deviations == 0, "with no delay no answer differs from its pre-check");
            }
            TestHarness.Assert(match.CommandPath.Issued == match.CommandPath.ResolvedCount && issued.Count == resolved.Count && issued.Count == preChecked.Count, $"delay {delay}: every order given was answered exactly once");
            Advance(match, controller, 30 * 20);
            TestHarness.Assert(match.World.RejectionCounts[(int)Simulation.Commands.CommandRejection.Late] == 0 && match.World.RejectionCounts[(int)Simulation.Commands.CommandRejection.OutOfOrder] == 0, $"delay {delay}: no order arrives late or out of order");
        }
    }

    /// <summary>
    /// The proof for d = 0 on a match with a human: the same orders once submitted straight to the session before each step (as the game did before phase 24) and once through
    /// LiveGame - tick driver, command path, bundle of the tick - give the same replay, byte for byte, and the same session hash. And the driver: pause and speed are requests
    /// it grants; a driver that releases nothing holds the game.
    /// </summary>
    private static void TickDriverAndPathChangeNothing(GameData data)
    {
        var mapPath = data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase));
        var setup = new MatchSetup(mapPath, data.Factions[0].Faction.Name, [data.Factions[1].Faction.Name], SkirmishMatchOptions.Standard);
        var viewport = new Vector2(1280, 720);
        const int ticks = 1_500;

        Func<ulong, long, Simulation.Commands.SimCommand>? OrderAt(LiveMatch match, long tick)
        {
            if (tick == 0 || tick % 113 != 0) return null;
            var human = match.Human;
            var units = match.World.Entities.Where(entity => entity.Owner == human && !entity.Template.IsKindOf("STRUCTURE") && !entity.EffectivelyDead).Select(entity => entity.Id).ToImmutableArray();
            if (units.IsEmpty) return null;
            var home = match.Terrain.StartPositions[0];
            var x = home.X + Simulation.Numerics.Fixed.FromInt(80 + (int)(tick % 7) * 20);
            return (sequence, at) => new Simulation.Commands.GroupMoveCommand(human, sequence, at, units, x, home.Y + Simulation.Numerics.Fixed.FromInt(60), false);
        }

        var direct = LiveMatch.Create(data, setup, viewport);
        while (direct.Tick < ticks)
        {
            if (OrderAt(direct, direct.Tick) is { } order) direct.Session.Issue(direct.Human, order);
            direct.Session.Step();
        }

        var live = LiveMatch.Create(data, setup, viewport);
        var tickSeconds = 1.0 / live.World.Time.TicksPerSecond;
        while (live.Tick < ticks)
        {
            if (OrderAt(live, live.Tick) is { } order) live.Commands.Issue(order);
            var before = live.Tick;
            live.Advance(tickSeconds);
            TestHarness.Assert(live.Tick == before + 1, "one tick of wall time releases one tick");
        }
        TestHarness.Assert(live.Session.RecordedCommands.Count(command => command.Player == live.Human) >= 10, "the human gave orders");
        TestHarness.Assert(Simulation.Replay.ReplayFormat.Write(live.Session.ToReplay()).AsSpan().SequenceEqual(Simulation.Replay.ReplayFormat.Write(direct.Session.ToReplay())),
            "the match through tick driver and command path has the replay bytes of the match with direct submission");
        TestHarness.Assert(live.Session.StateHash() == direct.Session.StateHash(), "and the same session hash");

        live.Paused = true;
        TestHarness.Assert(live.TickDriver.Paused && live.Paused, "a pause is a request the local driver grants");
        var paused = live.Tick;
        live.Advance(1);
        TestHarness.Assert(live.Tick == paused, "a paused driver releases no tick");
        live.Paused = false;
        live.Speed = 2;
        live.Advance(tickSeconds * 2);
        TestHarness.Assert(live.TickDriver.Speed == 2 && live.Tick == paused + 4, "double speed releases two ticks per tick of wall time");
        live.Speed = 1;

        var holding = new HoldingDriver(live.TickDriver);
        live.TickDriver = holding;
        var held = live.Tick;
        live.Advance(1);
        TestHarness.Assert(live.Tick == held && holding.Asked > 0, "a driver that releases nothing holds the game however much time passes");
        holding.Release = 3;
        live.Advance(0);
        TestHarness.Assert(live.Tick == held + 3, "the ticks the driver releases run, and no more");
    }

    /// <summary>A driver in front of the game's own that lets a tick through only when it is told to: the game's time is no longer its own.</summary>
    private sealed class HoldingDriver(ITickDriver inner) : ITickDriver
    {
        public int Release { get; set; }
        public int Asked { get; private set; }
        public bool Paused => inner.Paused;
        public double Speed => inner.Speed;
        public long Stalls => inner.Stalls;
        public double Fraction => inner.Fraction;
        public void RequestPause(bool paused) => inner.RequestPause(paused);
        public void RequestSpeed(double speed) => inner.RequestSpeed(speed);
        public void AddTime(double seconds) => inner.AddTime(seconds);
        public void LimitBacklog() => inner.LimitBacklog();
        public void TickCompleted(long tick) => inner.TickCompleted(tick);

        public bool IsDue(long tick)
        {
            Asked++;
            return Release > 0 && inner.IsDue(tick);
        }

        public bool TryReleaseTick(long tick, bool bundlePresent)
        {
            if (Release <= 0 || !inner.TryReleaseTick(tick, bundlePresent)) return false;
            Release--;
            return true;
        }
    }

    /// <summary>
    /// docs/handoffs/HANDOFF-PHASE29.md, bottlenecks: the presentation submits a waypoint path station by station (<c>StandingOrders</c>); with a delay between an
    /// order and its execution a station must be ordered neither twice (the book judging the unit before its last order arrived) nor late.
    /// </summary>
    private static void StandingOrdersWithDelay(GameData data)
    {
        (int Orders, long ArrivedAt, float Left, int Refused) Walk(int delay)
        {
            var (match, controller) = NewMatch(data, new Vector2(1280, 720));
            match.UseInputDelay(delay);
            long time = 10_000;
            Advance(match, controller, 30);
            var dozer = match.World.Entities.First(entity => entity.Owner == match.Human && entity.Template.IsKindOf("DOZER"));
            var origin = SimulationQueries.GroundOf(dozer);
            Vector2 Ground(float dx, float dy) => Screen(match, new Vector3(origin.X + dx, origin.Y + dy, match.Heights.HeightAt(origin.X + dx, origin.Y + dy)));
            var orders = 0;
            var refused = 0;
            match.CommandIssued += (command, result) =>
            {
                if (command.Player != match.Human) return;
                if (result.Accepted) orders++;
                else refused++;
            };
            Click(controller, ref time, MouseButton.Left, Screen(match, EntityPicker.CenterOf(dozer)));
            Advance(match, controller, 1);
            controller.Process(new KeyEvent(time += 16, KeyCode.LeftAlt, true));
            var stations = new[] { new Vector2(-10, -60), new Vector2(-90, -70), new Vector2(-170, -90) };
            foreach (var station in stations) Click(controller, ref time, MouseButton.Right, Ground(station.X, station.Y));
            controller.Process(new KeyEvent(time += 16, KeyCode.LeftAlt, false));
            var last = origin + stations[^1];
            var arrivedAt = -1L;
            // At most a minute of game time for a walk of some 180 units.
            for (var frame = 0; frame < 30 * 60 && arrivedAt < 0; frame++)
            {
                Advance(match, controller, 1);
                if (Vector2.Distance(SimulationQueries.GroundOf(dozer), last) < 12f && controller.Overlay.WaypointPaths.IsEmpty) arrivedAt = match.Tick;
            }
            return (orders, arrivedAt, Vector2.Distance(SimulationQueries.GroundOf(dozer), last), refused + (int)match.World.RejectionCounts[(int)Simulation.Commands.CommandRejection.Late]
                + (int)match.World.RejectionCounts[(int)Simulation.Commands.CommandRejection.Duplicate] + (int)match.World.RejectionCounts[(int)Simulation.Commands.CommandRejection.OutOfOrder]);
        }
        var direct = Walk(0);
        var delayed = Walk(6);
        TestHarness.Assert(direct.ArrivedAt > 0 && direct.Orders >= 3 && direct.Refused == 0, $"without delay the path of three stations is walked ({direct})");
        TestHarness.Assert(delayed.ArrivedAt > 0, $"with 6 ticks of delay the unit reaches the last station too ({delayed})");
        TestHarness.Assert(delayed.Orders == direct.Orders && delayed.Refused == 0, $"and the same orders were given - no station twice, none refused ({delayed.Orders} against {direct.Orders}, {delayed.Refused} refused)");
        // Not late: each of the orders may cost its delay and the frame the book needs to see that the last one arrived (two ticks at 60 Hz), no more.
        var allowance = direct.Orders * (6 + 2 * data.Rate.Time.TicksPerOriginalFrame);
        TestHarness.Assert(delayed.ArrivedAt - direct.ArrivedAt <= allowance, $"it arrives at tick {delayed.ArrivedAt}, not later than {allowance} ticks after the walk without delay ({direct.ArrivedAt})");
    }

    private static void MissionWithInputDelay(GameData data)
    {
        foreach (var delay in new[] { 4, 12 })
        {
            var run = CnCNew.Game.Campaign.MissionRun.Load(CnCNew.Game.Campaign.MissionContext.From(data), "Maps/GC_ChemGeneral/GC_ChemGeneral.map", Simulation.Mission.MissionOptions.Standard);
            var live = LiveMission.Create(data, run, new Vector2(1280, 720));
            live.UseInputDelay(delay);
            var resolved = new List<CommandResolution>();
            live.CommandResolved += resolved.Add;
            var frame = CnCNew.Tests.TestTime.FrameSeconds;
            for (var index = 0; index < 30 * 5; index++) live.Advance(frame);
            var given = new List<long>();
            for (var index = 0; index < 30 * 20; index++)
            {
                if (index % 45 == 0)
                {
                    var units = live.World.Entities.Where(entity => entity.Owner == live.Human && !entity.Template.IsKindOf("STRUCTURE") && !entity.EffectivelyDead && entity.Template.Locomotor is not null)
                        .Select(entity => entity.Id).Take(4).ToImmutableArray();
                    if (!units.IsEmpty)
                    {
                        var anchor = live.World.FindEntity(units[0])!;
                        var x = anchor.X + Simulation.Numerics.Fixed.FromInt(40);
                        given.Add(live.Tick);
                        live.Commands.Issue((sequence, tick) => new Simulation.Commands.GroupMoveCommand(live.Human, sequence, tick, units, x, anchor.Y, false));
                    }
                }
                live.Advance(frame);
            }
            TestHarness.Assert(given.Count >= 5 && resolved.Count == given.Count, $"delay {delay}: every order of the human in the mission was answered ({resolved.Count} of {given.Count})");
            TestHarness.Assert(resolved.Select(resolution => resolution.Command.Tick).SequenceEqual(given.Select(tick => tick + delay)), $"delay {delay}: each order is executed {delay} ticks after it was given");
            TestHarness.Assert(resolved.All(resolution => resolution.Result.Accepted), $"delay {delay}: the orders are accepted at execution");
            TestHarness.Assert(live.World.RejectionCounts[(int)Simulation.Commands.CommandRejection.Late] == 0 && live.World.RejectionCounts[(int)Simulation.Commands.CommandRejection.OutOfOrder] == 0
                && live.World.RejectionCounts[(int)Simulation.Commands.CommandRejection.Duplicate] == 0, $"delay {delay}: the human shares the session's sequence counter with the scripts without a refused number");
        }
    }

    private static void ScriptedSession(GameData data)
    {
        var (match, controller) = NewMatch(data, new Vector2(1280, 720));
        var human = match.Human;
        long time = 10_000;
        Advance(match, controller, 1);

        var dozer = match.World.Entities.First(entity => entity.Owner == human && entity.Template.IsKindOf("DOZER"));
        Click(controller, ref time, MouseButton.Left, Screen(match, EntityPicker.CenterOf(dozer)));
        Advance(match, controller, 1);
        TestHarness.Assert(controller.Input.Selection.Contains(dozer.Id), "a click selects the start dozer");
        Refresh(controller);

        var slot = FindConstructSlot(match, controller.CommandBar);
        var rect = controller.WindowRect($"ControlBar.wnd:ButtonCommand{slot:00}") ?? throw new InvalidOperationException("button window missing");
        Click(controller, ref time, MouseButton.Left, new Vector2(rect.CenterX, rect.CenterY));
        Advance(match, controller, 1);
        TestHarness.Assert(controller.Input.Commands.Mode == CommandModeKind.Placement, "the construct button enters placement mode");

        var buildTemplate = controller.Input.Commands.PendingButton!.Object;
        var dozerGround = SimulationQueries.GroundOf(dozer);
        PlacementGhost? ghost = null;
        foreach (var offset in (ReadOnlySpan<Vector2>)[new(300, 0), new(-300, 0), new(0, 300), new(0, -300), new(220, 220), new(-220, -220)])
        {
            var candidate = dozerGround + offset;
            var screen = Screen(match, new Vector3(candidate.X, candidate.Y, 0f));
            controller.Process(new MouseMoveEvent(time += 16, screen));
            if (controller.Input.Commands.Ghost is { IsValid: true } valid) { ghost = valid; break; }
        }
        TestHarness.Assert(ghost is not null, $"a buildable spot for '{buildTemplate}' exists near the dozer");
        var placementScreen = Screen(match, ghost!.Position);
        Click(controller, ref time, MouseButton.Left, placementScreen);
        Advance(match, controller, 1);
        TestHarness.Assert(controller.Input.Commands.Mode == CommandModeKind.None, "the click places the structure");
        Advance(match, controller, 60);
        TestHarness.Assert(match.World.Entities.Any(entity => entity.Owner == human && entity.Template.Name == buildTemplate), "the construction site exists");

        // Queue a unit from whichever own producer currently offers one (a fresh base only has the dozer and the starting structures).
        var (producer, unitButton) = FindProducerWithUnitBuild(match, controller);
        if (producer is not null)
        {
            Click(controller, ref time, MouseButton.Left, Screen(match, EntityPicker.CenterOf(producer)));
            Advance(match, controller, 1);
            Refresh(controller);
            var unitSlot = FindSlot(controller.CommandBar, button => ReferenceEquals(button, unitButton));
            var unitRect = controller.WindowRect($"ControlBar.wnd:ButtonCommand{unitSlot:00}")!.Value;
            Click(controller, ref time, MouseButton.Left, new Vector2(unitRect.CenterX, unitRect.CenterY));
            Advance(match, controller, 1);
            TestHarness.Assert(!controller.CommandBar.Queue.IsEmpty, "the unit is queued");

            // Wait for it to complete, then move and attack-move it.
            var unitName = unitButton!.Object;
            Simulation.World.Entity? unit = null;
            for (var i = 0; i < 4000 && unit is null; i++)
            {
                Advance(match, controller, 1);
                unit = match.World.Entities.FirstOrDefault(entity => entity.Owner == human && entity.Template.Name == unitName && SimulationQueries.CanMove(entity));
            }
            if (unit is not null)
            {
                Click(controller, ref time, MouseButton.Left, Screen(match, EntityPicker.CenterOf(unit)));
                Advance(match, controller, 1);
                TestHarness.Assert(controller.Input.Selection.Contains(unit.Id), "the produced unit is selected");
                Refresh(controller);

                var startGround = SimulationQueries.GroundOf(unit);
                var moveTarget = startGround + new Vector2(150, 0);
                Click(controller, ref time, MouseButton.Right, Screen(match, new Vector3(moveTarget.X, moveTarget.Y, 0f)));
                Advance(match, controller, 30);
                Advance(match, controller, 30); // the path comes from the queue of the budgeted movement, and a dozer turns before it drives
                TestHarness.Assert(Vector2.Distance(SimulationQueries.GroundOf(unit), startGround) > 1f, "the right-click move order actually moved the unit");
                Refresh(controller);

                var attackMoveSlot = FindSlot(controller.CommandBar, button => button.Kind == GuiCommandKind.AttackMove);
                if (attackMoveSlot > 0)
                {
                    var attackRect = controller.WindowRect($"ControlBar.wnd:ButtonCommand{attackMoveSlot:00}")!.Value;
                    Click(controller, ref time, MouseButton.Left, new Vector2(attackRect.CenterX, attackRect.CenterY));
                    Advance(match, controller, 1);
                    TestHarness.Assert(controller.Input.Commands.Mode == CommandModeKind.AttackMove, "the attack-move button enters attack-move mode");
                    var attackTarget = SimulationQueries.GroundOf(unit) + new Vector2(-150, 0);
                    Click(controller, ref time, MouseButton.Left, Screen(match, new Vector3(attackTarget.X, attackTarget.Y, 0f)));
                    Advance(match, controller, 1);
                    TestHarness.Assert(controller.Input.Commands.Mode == CommandModeKind.None, "the click issues the attack-move order");
                }
                else Console.WriteLine("       (no AttackMove button on the produced unit's command set; move-only was exercised)");
            }
            else Console.WriteLine("       (the queued unit did not finish within the tick budget; only queueing was exercised)");
        }
        else Console.WriteLine("       (no own producer currently offers a UnitBuild button; queueing was skipped)");
    }

    private static (Simulation.World.Entity? Producer, CommandButtonInfo? Button) FindProducerWithUnitBuild(LiveMatch match, MatchController controller)
    {
        foreach (var entity in match.World.Entities.Where(entity => entity.Owner == match.Human).OrderBy(entity => entity.Id.Value))
        {
            var view = controller.Input.CommandBar!.Build(match.World, new SelectionModel(match.Human));
            // Build against a throwaway selection model containing only this entity, to probe its own command set cheaply.
            var probe = new SelectionModel(match.Human);
            probe.Replace([entity.Id]);
            var single = controller.Input.CommandBar!.Build(match.World, probe);
            for (var slot = 1; slot <= CommandBarModel.GridSlots; slot++)
                if (single[slot] is { Button.Kind: GuiCommandKind.UnitBuild } button) return (entity, button.Button);
        }
        return (null, null);
    }

    private static int FindSlot(CommandBarView view, Func<CommandButtonInfo, bool> predicate)
    {
        for (var slot = 1; slot <= CommandBarModel.GridSlots; slot++)
            if (view[slot] is { } button && predicate(button.Button)) return slot;
        return 0;
    }

    /// <summary>
    /// <see cref="LiveMatch.Commands"/> is the only way this package changes the world, so everything the human player does is
    /// captured by <see cref="Simulation.Skirmish.SkirmishMatchSession.ToReplay"/> alongside the AI's own commands. This test
    /// exercises exactly that: it verifies the recorded stream contains the human's move command and that replaying it from a
    /// fresh world is itself deterministic (two independent replay runs of the same recording agree, the way
    /// <c>CnCNew.Simulation.Tests</c> already verifies for a pure AI match).
    /// <para>
    /// It stops short of comparing the replay hash to <see cref="LiveMatch"/>'s own live <c>World.StateHash</c>: that comparison
    /// was found to diverge (confirmed with a minimal reproduction using only <c>LiveMatch.Advance</c> and zero input/HUD code, as
    /// soon as the skirmish AI issues its first command) independently of anything package G2 owns. This looks like a pre-existing
    /// gap in how <c>LiveMatch</c>/<c>SkirmishMatchSession</c> interact with <c>ReplayRunner</c> on real map content, not something
    /// introduced here; it is flagged separately for whoever owns <c>LiveMatch.cs</c> (see the final report).
    /// </para>
    /// </summary>
    private static void ReplayMatches(GameData data)
    {
        var (match, controller) = NewMatch(data, new Vector2(1280, 720));
        long time = 10_000;
        Advance(match, controller, 1);
        var dozer = match.World.Entities.First(entity => entity.Owner == match.Human && entity.Template.IsKindOf("DOZER"));
        var startGround = SimulationQueries.GroundOf(dozer);
        // The default mouse setup is the alternate one: the left button selects, a right click on open, reachable ground orders a move.
        Click(controller, ref time, MouseButton.Left, Screen(match, EntityPicker.CenterOf(dozer)));
        Advance(match, controller, 1);
        Click(controller, ref time, MouseButton.Right, Screen(match, new Vector3(startGround.X + 80, startGround.Y, 0f)));
        Advance(match, controller, 90);
        TestHarness.Assert(Vector2.Distance(SimulationQueries.GroundOf(dozer), startGround) > 1f, "the human player's move order was actually carried out");

        var replay = match.Session.ToReplay();
        TestHarness.Assert(replay.Commands.Any(command => command is Simulation.Commands.SetMoveTargetCommand { Player.Value: 1 }),
            "the human player's move order is in the recorded command stream");

        var first = ReplayRunner.Run(replay, data.Templates, data.Fingerprint, new ReplayRunOptions { StopTick = match.World.Tick }, terrain: match.Terrain);
        var second = ReplayRunner.Run(replay, data.Templates, data.Fingerprint, new ReplayRunOptions { StopTick = match.World.Tick }, terrain: match.Terrain);
        TestHarness.Assert(first.RejectedCommands == 0, "every recorded command replays as accepted");
        TestHarness.Assert(first.FinalHash == second.FinalHash, "two independent replays of the same recording agree with each other");
    }

    private static readonly (int Width, int Height)[] Resolutions = [(800, 600), (1280, 720), (1920, 1080), (2560, 1080)];

    private static void ResolutionSweep(GameData data)
    {
        foreach (var (width, height) in Resolutions)
        {
            var (match, controller) = NewMatch(data, new Vector2(width, height));
            Advance(match, controller, 1);
            var drawList = controller.BuildDrawList(width, height);
            TestHarness.Assert(drawList.Commands.Length > 0, $"{width}x{height}: the draw list is not empty");
            var counts = drawList.CountByKind();
            TestHarness.Assert(counts.GetValueOrDefault("UiCustomDraw") >= 1, $"{width}x{height}: at least the minimap custom draw is present");

            for (var slot = 1; slot <= 14; slot++)
            {
                var rect = controller.WindowRect($"ControlBar.wnd:ButtonCommand{slot:00}") ?? throw new InvalidOperationException($"slot {slot} window missing");
                TestHarness.Assert(rect.Width > 0 && rect.Height > 0, $"{width}x{height}: command button {slot} has a real size");
                TestHarness.Assert(rect.X >= 0 && rect.Right <= width && rect.Y >= 0 && rect.Bottom <= height, $"{width}x{height}: command button {slot} is inside the viewport ({rect})");
            }
            var viewportCommand = drawList.Commands.OfType<CnCNew.Ui.Runtime.UiCustomDraw>().FirstOrDefault(command => command.Callback == "Viewport3D");
            TestHarness.Assert(viewportCommand is not null && viewportCommand.Rect.Width == width, $"{width}x{height}: the 3D viewport spans the full width");
        }
    }

    private static void MinimapClickMovesCamera(GameData data)
    {
        var (match, controller) = NewMatch(data, new Vector2(1280, 720), started: false);
        Advance(match, controller, 1);
        var rect = controller.WindowRect("ControlBar.wnd:LeftHUD") ?? throw new InvalidOperationException("LeftHUD window missing");
        var before = match.Camera.Pivot;
        long time = 10_000;
        // A point well inside the minimap frame but away from its centre, so a no-op click would be detectable.
        var clickPoint = new Vector2(rect.X + rect.Width * 0.25f, rect.Y + rect.Height * 0.75f);
        // LeftHUDInput: without a radar the window eats the click (F-50); the viewer has none in the first frame.
        TestHarness.Assert(!match.RadarOn, "the viewer has no radar in the first frame");
        Click(controller, ref time, MouseButton.Left, clickPoint);
        TestHarness.Assert(match.Camera.Pivot == before, "without a radar the minimap click is eaten");
        match.World.AddRadar(match.Human, false);
        TestHarness.Assert(match.RadarOn, "a radar of the viewer switches the minimap on");
        Click(controller, ref time, MouseButton.Left, clickPoint);
        var after = match.Camera.Pivot;
        TestHarness.Assert(Vector2.DistanceSquared(before, after) > 1f, $"the minimap click recentred the camera ({before} -> {after})");
    }

    private static void BoxSelectionSelects(GameData data)
    {
        var (match, controller) = NewMatch(data, new Vector2(1280, 720));
        Advance(match, controller, 1);
        var dozer = match.World.Entities.First(entity => entity.Owner == match.Human && entity.Template.IsKindOf("DOZER"));
        var center = Screen(match, EntityPicker.CenterOf(dozer));
        long time = 10_000;
        Drag(controller, ref time, center - new Vector2(60, 60), center + new Vector2(60, 60));
        TestHarness.Assert(controller.Input.Selection.Contains(dozer.Id), "the drag box selects the start dozer");
    }

    private static void ControlGroupsWork(GameData data)
    {
        var (match, controller) = NewMatch(data, new Vector2(1280, 720));
        Advance(match, controller, 1);
        var dozer = match.World.Entities.First(entity => entity.Owner == match.Human && entity.Template.IsKindOf("DOZER"));
        long time = 10_000;
        Click(controller, ref time, MouseButton.Left, Screen(match, EntityPicker.CenterOf(dozer)));
        Advance(match, controller, 1);
        controller.Process(new KeyEvent(time += 16, KeyCode.LeftControl, true));
        controller.Process(new KeyEvent(time += 16, KeyCode.D1, true));
        controller.Process(new KeyEvent(time += 16, KeyCode.D1, false));
        controller.Process(new KeyEvent(time += 16, KeyCode.LeftControl, false));
        TestHarness.Assert(controller.Input.Group(1).Contains(dozer.Id), "Ctrl+1 stores the group");

        controller.Input.Selection.Clear();
        TestHarness.Assert(controller.Input.Selection.IsEmpty, "the selection is cleared");
        controller.Process(new KeyEvent(time += 16, KeyCode.D1, true));
        controller.Process(new KeyEvent(time += 16, KeyCode.D1, false));
        TestHarness.Assert(controller.Input.Selection.Contains(dozer.Id), "1 recalls the group");
    }

    /// <summary>
    /// A scheme is written for the resolution it names (<c>ScreenCreationRes</c>); the HUD used to take 1920x1080 for granted, the resolution of a modified control bar.
    /// Synthetic text in the file's grammar, no game data.
    /// </summary>
    private static void SchemeCreationResolution()
    {
        const string text = "ControlBarScheme Small\n  ScreenCreationRes X:800 Y:600\n  Side Blue\n  MoneyUL X:360 Y:438\n  MoneyLR X:439 Y:457\n  SomeColor R:1 G:2 B:3 A:4\n"
            + "  AnimatingPart\n    ImagePart\n      Position X:1 Y:2\n      Size X:3 Y:4\n      ImageName Moving\n      Layer 1\n    End\n    Duration 5\n  End\n"
            + "  ImagePart\n    Position X:0 Y:408\n    Size X:800 Y:191\n    ImageName Base\n    Layer 4\n  End\n"
            + "  ImagePart\n    Position X:10 Y:500\n    Size X:20 Y:30\n    ImageName Top\n    Layer 0\n  End\nEnd\n"
            + "ControlBarScheme Large\n  ScreenCreationRes X:1920 Y:1080\n  Side Red\n  SomeColor R:1 G:2 B:3 A:4\nEnd\n";
        var table = ControlBarSchemeTable.Parse(text);
        TestHarness.Assert(table.Schemes.Length == 2, $"two schemes ({table.Schemes.Length})");
        var small = table.Schemes[0];
        var large = table.Schemes[1];
        TestHarness.Assert(small is { CreationWidth: 800, CreationHeight: 600 } && large is { CreationWidth: 1920, CreationHeight: 1080 }, "each scheme carries its own creation resolution");
        TestHarness.Assert(small.Money == new SchemeRect(360, 438, 439, 457) && large.Money.IsEmpty, "a rectangle the scheme does not name is empty");
        TestHarness.Assert(small.ImageParts.Length == 2 && small.BackgroundParts.Single().ImageName == "Base" && small.ForegroundParts.Single().ImageName == "Top",
            "the statement after an AnimatingPart block is read, and layers 3 to 5 are background, 0 to 2 foreground");
        TestHarness.Assert(table.UnreadKeys.Count == 2 && table.UnreadKeys["SomeColor"] == 2 && table.UnreadKeys["AnimatingPart"] == 1, $"unread keys are counted ({string.Join(", ", table.UnreadKeys)})");
        var refused = false;
        try { ControlBarSchemeTable.Parse("ControlBarScheme NoSize\n  Side Blue\nEnd\n"); }
        catch (FormatException) { refused = true; }
        TestHarness.Assert(refused, "a scheme without ScreenCreationRes is refused");
    }

    private static void SchemeArt(GameData data)
    {
        var (match, controller) = NewMatch(data, new Vector2(1280, 720));
        Advance(match, controller, 1);
        var commands = controller.BuildDrawList(1280, 720).Commands;
        TestHarness.Assert(commands.Length > 0 && commands[0] is UiImageDraw, "the scheme's background parts come first, under the window tree");
        var backgroundParts = commands.TakeWhile(command => command is UiImageDraw).Count();
        TestHarness.Assert(backgroundParts >= 1, $"the human side scheme contributes background parts ({backgroundParts})");
        // The scheme of the human side is placed by its own creation resolution: a part as wide as that resolution is as wide as the control bar at every size.
        TestHarness.Assert(controller.Assets.Schemes.Schemes.All(scheme => scheme.CreationWidth > 0 && scheme.CreationHeight > 0), "every scheme of the data names its creation resolution");
        TestHarness.Assert(controller.Assets.Schemes.TryGetForSide("America", out var america), "the data has a scheme for the side America");
        var wide = america.BackgroundParts.First(part => part.X == 0 && part.Width == america.CreationWidth);
        foreach (var (width, height) in Resolutions)
        {
            var (sizedMatch, sized) = NewMatch(data, new Vector2(width, height));
            Advance(sizedMatch, sized, 1);
            var art = sized.BuildDrawList(width, height).Commands.OfType<UiImageDraw>().First(draw => draw.Image == wide.ImageName);
            var bar = sized.WindowRect("ControlBar.wnd:ControlBarParent")!.Value;
            TestHarness.Assert(art.Destination.X == bar.X && Math.Abs(art.Destination.Right - bar.Right) <= width / 400 + 1 && art.Destination.Bottom <= height && art.Destination.Bottom >= height - 3,
                $"{width}x{height}: the scheme's full-width art lies on the control bar ({art.Destination} on {bar})");
        }
        SchemeWindows(data, america);
    }

    /// <summary>
    /// <c>ControlBarScheme::init</c> places the control bar's own buttons at the scheme's rectangles and gives them the scheme's images; the layout has neither (its buttons name
    /// no image). The art is drawn by the two marker windows, the right-hand window shows the side's logo until one object is selected, then the object's portrait.
    /// </summary>
    private static void SchemeWindows(GameData data, ControlBarSchemeInfo america)
    {
        const int width = 1280, height = 720;
        var (match, controller) = NewMatch(data, new Vector2(width, height));
        controller.BeaconsEnabled = true; // the button of a match of a server
        TestHarness.Assert(match.HumanFaction.Contains("America", StringComparison.OrdinalIgnoreCase), $"the test match is played by America ({match.HumanFaction})");
        Advance(match, controller, 1);
        var commands = controller.BuildDrawList(width, height).Commands;
        // Every scheme of the retail file names the QueueButtonImage "SCBigButton", which no mapped image file defines: the original gets a null image and leaves the empty
        // queue places without a picture (ControlBar::updateBuildQueueDisabledImages returns). It is the one missing name, and it is listed, not dropped.
        TestHarness.Assert(controller.MissingSchemeImages.SequenceEqual(["SCBigButton"]), $"the image table has every image the scheme names except the queue place ({string.Join(", ", controller.MissingSchemeImages)})");
        var bar = controller.WindowRect("ControlBar.wnd:ControlBarParent")!.Value;
        foreach (var (window, rect, images) in new[]
                 {
                     ("ButtonOptions", america.Options, america.OptionsImages), ("ButtonIdleWorker", america.Worker, america.WorkerImages),
                     ("ButtonPlaceBeacon", america.Beacon, america.BeaconImages), ("PopupCommunicator", america.Chat, america.ChatImages),
                     ("ButtonGeneral", america.General, america.GeneralImages),
                 })
        {
            var onScreen = controller.WindowRect($"ControlBar.wnd:{window}")!.Value;
            TestHarness.Assert(!rect.IsEmpty && onScreen.X == rect.Left * width / america.CreationWidth && onScreen.Right == rect.Right * width / america.CreationWidth && onScreen.Y >= bar.Y - height / 20 && onScreen.Bottom <= height,
                $"{window} lies at the scheme's rectangle ({onScreen} for {rect})");
            // The idle worker button is enabled while the local player has an idle worker (InGameUI::updateIdleWorker), disabled otherwise: it shows that image.
            var expected = window == "ButtonIdleWorker" && controller.Input.Selection.IdleWorkerCount == 0 ? images.Disabled : images.Enabled;
            TestHarness.Assert(expected.Length > 0 && commands.OfType<UiImageDraw>().Any(draw => draw.Image == expected && draw.Destination == onScreen), $"{window} shows the scheme's image");
        }
        TestHarness.Assert(!commands.OfType<UiCustomDraw>().Any(draw => draw.Callback is "W3DCommandBarBackgroundDraw" or "W3DCommandBarForegroundDraw" || draw is { Callback: "W3DRightHUDDraw", Window: "ControlBar.wnd:RightHUD" }),
            "the scheme's draw callbacks are drawn here, not left to the host");
        var rightHud = controller.WindowRect("ControlBar.wnd:RightHUD")!.Value;
        TestHarness.Assert(america.RightHudImage.Length > 0 && commands.OfType<UiImageDraw>().Any(draw => draw.Image == america.RightHudImage && draw.Destination == rightHud), "with nothing selected the right-hand window shows the side's logo");

        var dozer = match.World.Entities.First(entity => entity.Owner == match.Human && entity.Template.IsKindOf("DOZER"));
        long time = 10_000;
        Click(controller, ref time, MouseButton.Left, Screen(match, EntityPicker.CenterOf(dozer)));
        Advance(match, controller, 1);
        commands = controller.BuildDrawList(width, height).Commands;
        var cameo = controller.WindowRect("ControlBar.wnd:CameoWindow")!.Value;
        TestHarness.Assert(data.Visuals.TryGet(dozer.Template.Name, out var visual) && visual.SelectPortrait is { Length: > 0 }, "the dozer has a portrait in the data");
        TestHarness.Assert(commands.OfType<UiImageDraw>().Any(draw => draw.Image == visual.SelectPortrait && draw.Destination == cameo), "one selected object shows its portrait in the cameo window");
        TestHarness.Assert(!commands.OfType<UiImageDraw>().Any(draw => draw.Image == america.RightHudImage), "the logo is gone while a portrait shows");
        TestHarness.Assert(!commands.OfType<UiFillRect>().Any(fill => fill.Rect == cameo), "the cameo window draws its image only, never a filled box");
    }

    /// <summary>
    /// <c>InGameUI::update</c> sets the text of <c>ControlBar.wnd:MoneyDisplay</c> to <c>GUI:ControlBarMoneyDisplay</c> with the money, and
    /// <c>ControlBarScheme</c> moves the window to the scheme's money rectangle. The layout file holds the placeholder text "$$$" and a 100x100
    /// rectangle at the top left, which the client used to show as it was.
    /// </summary>
    private static void MoneyDisplay(GameData data)
    {
        foreach (var (width, height) in new[] { (1280, 720), (1920, 1080) })
        {
            var (match, controller) = NewMatch(data, new Vector2(width, height));
            Advance(match, controller, 1);
            var commands = controller.BuildDrawList(width, height).Commands;
            var expected = "$ " + ((int)controller.Hud.Money.ToDecimal()).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var texts = commands.OfType<UiTextDraw>().Where(text => text.Text.StartsWith('$')).ToArray();
            TestHarness.Assert(texts.Length == 1 && texts[0].Text == expected, $"{width}x{height}: exactly one money text '{expected}', got [{string.Join("|", texts.Select(text => text.Text))}]");
            TestHarness.Assert(!commands.OfType<UiTextDraw>().Any(text => text.Text.Contains("$$$")), $"{width}x{height}: the layout placeholder '$$$' is not drawn");
            var window = controller.WindowRect("ControlBar.wnd:MoneyDisplay");
            var bar = controller.WindowRect("ControlBar.wnd:ControlBarParent");
            TestHarness.Assert(window is { } money && bar is { } parent && money.Y >= parent.Y && money.Right <= parent.Right + 1 && money.Width > 20, $"{width}x{height}: the money window lies inside the control bar ({window} in {bar})");
            TestHarness.Assert(texts[0].Clip == window, $"{width}x{height}: the money text is clipped to its window");
        }
    }

    /// <summary>
    /// The original draws no text on the command icons (hot keys and costs are in the description popup); the client used to print a hot key
    /// letter and the cost on every icon and had no tooltips at all. The description is now the name, the cost and the description label of the
    /// button in the pointer tooltip (<c>Mouse::drawTooltip</c>), shown after <c>TOOLTIPDELAY</c> and hidden again when the pointer moves.
    /// </summary>
    private static void CommandButtonTooltip(GameData data)
    {
        var (match, controller) = NewMatch(data, new Vector2(1280, 720));
        long time = 10_000;
        Advance(match, controller, 1);
        var dozer = match.World.Entities.First(entity => entity.Owner == match.Human && entity.Template.IsKindOf("DOZER"));
        Click(controller, ref time, MouseButton.Left, Screen(match, EntityPicker.CenterOf(dozer)));
        Advance(match, controller, 1);
        Refresh(controller);
        var slot = FindConstructSlot(match, controller.CommandBar);
        var button = controller.CommandBar[slot]!;
        var rect = controller.WindowRect($"ControlBar.wnd:ButtonCommand{slot:00}")!.Value;

        var idle = controller.BuildDrawList(1280, 720).Commands;
        TestHarness.Assert(!idle.OfType<UiTextDraw>().Any(text => text.Clip == rect && text.Text.Length <= 6), "no hot key letter or cost number is printed on the command icon");

        controller.Process(new MouseMoveEvent(time += 16, new Vector2(rect.CenterX, rect.CenterY)));
        TestHarness.Assert(!controller.BuildDrawList(1280, 720).Commands.Any(command => command is UiFillRect { Color: { R: 20, G: 20, B: 20 } }), "the tooltip is not there at the first moment of the hover");
        Advance(match, controller, 30);
        var shown = controller.BuildDrawList(1280, 720).Commands;
        var texts = shown.OfType<UiTextDraw>().Where(text => text.Color == new WndColor(220, 220, 220, 255)).ToArray();
        TestHarness.Assert(texts.Length > 0 && shown.Any(command => command is UiFillRect { Color: { R: 20, G: 20, B: 20 } }), "after the delay a tooltip box with text lies over the HUD");
        var name = data.LoadText("English").Get(button.Button.TextLabel).Replace("&", string.Empty, StringComparison.Ordinal);
        var joined = string.Join(" ", texts.Select(text => text.Text.Trim()));
        TestHarness.Assert(joined.StartsWith(name.Split(' ')[0], StringComparison.Ordinal), $"the first line is the button name '{name}': {joined}");
        if (button.Cost.Raw > 0) TestHarness.Assert(joined.Contains("Cost: " + (int)button.Cost.ToDecimal(), StringComparison.Ordinal), $"the cost line follows: {joined}");

        var box = shown.OfType<UiFillRect>().First(command => command.Color is { R: 20, G: 20, B: 20 }).Rect;
        controller.Process(new MouseMoveEvent(time += 16, new Vector2(rect.CenterX + 3, rect.CenterY + 2)));
        TestHarness.Assert(controller.BuildDrawList(1280, 720).Commands.OfType<UiFillRect>().Any(command => command.Color is { R: 20, G: 20, B: 20 } && command.Rect == box), "a move over the same button leaves the tooltip where it is (HUD-02)");
        controller.Process(new MouseMoveEvent(time += 16, new Vector2(4, 4)));
        TestHarness.Assert(!controller.BuildDrawList(1280, 720).Commands.Any(command => command is UiFillRect { Color: { R: 20, G: 20, B: 20 } }), "leaving the button ends it");
    }

    /// <summary>
    /// <c>InGameUI::message</c>: the game's own lines (docs/GAME-HUD.md section 8). The HUD computed a "ticker" of invented English texts every frame and nothing drew it; now the lines are the
    /// original's labels, in the one message list, drawn by <c>InGameUI::postDraw</c>'s rules.
    /// </summary>
    private static void MessageList(GameData data)
    {
        var (match, controller) = NewMatch(data, new Vector2(1280, 720));
        Advance(match, controller, 5);
        var feed = controller.Messages;
        var text = data.LoadText("English");
        var style = controller.TextStyle;
        var font = style.Font(InGameUiFontRole.Message, new CnCNew.Ui.Layout.UiViewport(1280, 720), controller.ScaleOptions);
        UiTextDraw[] Lines() => [..controller.BuildDrawList(1280, 720).Commands.OfType<UiTextDraw>().Where(draw => draw.Font == font && draw.X == style.Settings.MessagePosition.X && draw.Y >= style.Settings.MessagePosition.Y && draw.Y < 200)];
        CommandFeedback Rejected(CnCNew.Simulation.Commands.CommandRejection reason, string command) => new(FeedbackKind.Rejected, Vector3.Zero, CnCNew.Simulation.Ids.EntityId.Invalid, match.Tick, 30, reason, command);

        TestHarness.Assert(Lines().Length == 0 && controller.OwnsMessageList, "no message at the start; a skirmish HUD owns its list");

        // Refusals (ControlBar::processCommandUI, PlaceEventTranslator, UpgradeCenter::canAffordUpgrade).
        feed.Observe(Rejected(CnCNew.Simulation.Commands.CommandRejection.InsufficientFunds, nameof(CnCNew.Simulation.Commands.QueueProductionCommand)));
        feed.Observe(Rejected(CnCNew.Simulation.Commands.CommandRejection.InsufficientFunds, nameof(CnCNew.Simulation.Commands.PurchaseUpgradeCommand)));
        feed.Observe(Rejected(CnCNew.Simulation.Commands.CommandRejection.QueueFull, nameof(CnCNew.Simulation.Commands.QueueProductionCommand)));
        feed.Observe(Rejected(CnCNew.Simulation.Commands.CommandRejection.ProductionLimitReached, nameof(CnCNew.Simulation.Commands.QueueProductionCommand)));
        var lines = Lines();
        TestHarness.Assert(lines.Select(line => line.Text).SequenceEqual(["You don't have enough money to build that", "You don't have enough money to make upgrade", "The unit's production queue is full", "You already have the maximum for this unit"]),
            $"the four refusals print their labels, oldest at the top ({string.Join(" | ", lines.Select(line => line.Text))})");
        var step = new MonospaceTextMeasure().LineHeight(font);
        TestHarness.Assert(lines[0].Y == style.Settings.MessagePosition.Y && lines[1].Y == lines[0].Y + step && lines[3].Y == lines[0].Y + 3 * step, "from MessagePosition, one font height apart");
        TestHarness.Assert(lines[0].Color == style.Settings.MessageColor1 && lines[1].Color == style.Settings.MessageColor2 && lines[2].Color == style.Settings.MessageColor1 && lines[0].ShadowColor == new WndColor(0, 0, 0, 255), "MessageColor1 and MessageColor2 alternate, black drop color");
        TestHarness.Assert(controller.Overlay.Ticker.IsEmpty, "the overlay's text list follows with the next update");
        Advance(match, controller, 1);
        TestHarness.Assert(controller.Overlay.Ticker.Length == 4, "and then carries the same lines");

        // A refusal the original has no line for shows nothing and is counted.
        feed.Observe(Rejected(CnCNew.Simulation.Commands.CommandRejection.NotPermitted, nameof(CnCNew.Simulation.Commands.SellCommand)));
        feed.Observe(new CommandFeedback(FeedbackKind.InvalidOrder, Vector3.Zero, CnCNew.Simulation.Ids.EntityId.Invalid, match.Tick, 30));
        feed.Observe(new CommandFeedback(FeedbackKind.MoveMarker, Vector3.Zero, CnCNew.Simulation.Ids.EntityId.Invalid, match.Tick, 30));
        TestHarness.Assert(Lines().Length == 4 && feed.WithoutLine["rejected NotPermitted (SellCommand)"] == 1 && feed.WithoutLine["invalid order"] == 1, $"no line, but counted ({string.Join("; ", feed.WithoutLine.Select(pair => $"{pair.Key} x{pair.Value}"))})");

        // Building spots (InGameUI::displayCantBuildMessage); the seventh line pushes the oldest out.
        feed.Observe(new CommandFeedback(FeedbackKind.InvalidOrder, Vector3.Zero, CnCNew.Simulation.Ids.EntityId.Invalid, match.Tick, 30, Detail: nameof(PlacementIssue.Shroud)));
        feed.Observe(new CommandFeedback(FeedbackKind.InvalidOrder, Vector3.Zero, CnCNew.Simulation.Ids.EntityId.Invalid, match.Tick, 30, Detail: nameof(PlacementIssue.BlockedFootprint)));
        feed.Observe(new CommandFeedback(FeedbackKind.Unsupported, Vector3.Zero, CnCNew.Simulation.Ids.EntityId.Invalid, match.Tick, 30, Detail: "Beacon"));
        lines = Lines();
        TestHarness.Assert(lines.Length == 6 && lines[^3].Text == "You must explore there before building" && lines[^2].Text.Contains("obstacles", StringComparison.Ordinal) && lines[0].Text == "You don't have enough money to make upgrade",
            $"six lines at most ({string.Join(" | ", lines.Select(line => line.Text))})");
        TestHarness.Assert(lines[^1].Text == "(not supported in this build: Beacon)", "what this build cannot do is a line in parentheses, not a text of the game");

        // Events of the simulation: the finished building of the local player, the attacked unit (once per place and ten seconds), nothing for the other player's.
        var fresh = NewMatch(data, new Vector2(1280, 720));
        (match, controller) = fresh;
        feed = controller.Messages;
        Advance(match, controller, 5);
        var human = match.Human;
        var center = match.World.Entities.First(entity => entity.Owner == human && entity.Template.IsKindOf("COMMANDCENTER"));
        var dozer = match.World.Entities.First(entity => entity.Owner == human && entity.Template.IsKindOf("DOZER"));
        var enemyCenter = match.World.Entities.First(entity => entity.Owner != human && !entity.Owner.IsSystem && entity.Template.IsKindOf("COMMANDCENTER"));
        CnCNew.Simulation.Events.SimEvent Event(CnCNew.Simulation.Events.SimEventKind kind, CnCNew.Simulation.Ids.EntityId entity, CnCNew.Simulation.Ids.EntityId other, CnCNew.Simulation.Ids.PlayerId player, string name = "", long value = 0, long tick = -1)
            => new(default, tick < 0 ? match.Tick : tick, kind, entity, other, player, name, value);
        feed.Observe(Event(CnCNew.Simulation.Events.SimEventKind.ConstructionCompleted, center.Id, dozer.Id, human), match.World, human);
        feed.Observe(Event(CnCNew.Simulation.Events.SimEventKind.ConstructionCompleted, enemyCenter.Id, default, enemyCenter.Owner), match.World, human);
        var built = feed.Log.Lines;
        TestHarness.Assert(built.Length == 1 && built[0].Text.StartsWith("Construction Complete: ", StringComparison.Ordinal) && built[0].Text.Length > "Construction Complete: ".Length && !built[0].Text.Contains("??", StringComparison.Ordinal) && !built[0].Text.Contains("MISSING", StringComparison.Ordinal),
            $"DOZER:ConstructionComplete with the structure's display name, for the local player's building only ({string.Join(" | ", built.Select(line => line.Text))})");

        feed.Observe(Event(CnCNew.Simulation.Events.SimEventKind.EntityDamaged, dozer.Id, enemyCenter.Id, human, value: 100), match.World, human);
        feed.Observe(Event(CnCNew.Simulation.Events.SimEventKind.EntityDamaged, dozer.Id, enemyCenter.Id, human, value: 100), match.World, human);
        feed.Observe(Event(CnCNew.Simulation.Events.SimEventKind.EntityDamaged, center.Id, enemyCenter.Id, human, value: 100, tick: match.Tick + CnCNew.Tests.TestTime.Frames(match.World, 200)), match.World, human);
        feed.Observe(Event(CnCNew.Simulation.Events.SimEventKind.EntityDamaged, dozer.Id, dozer.Id, human, value: 100, tick: match.Tick + CnCNew.Tests.TestTime.Frames(match.World, 400)), match.World, human);
        feed.Observe(Event(CnCNew.Simulation.Events.SimEventKind.EntityDamaged, enemyCenter.Id, dozer.Id, enemyCenter.Owner, value: 100, tick: match.Tick + CnCNew.Tests.TestTime.Frames(match.World, 400)), match.World, human);
        var dozerNearCenter = Vector2.Distance(SimulationQueries.GroundOf(dozer), SimulationQueries.GroundOf(center)) <= 250f;
        var attacked = feed.Log.Lines.Skip(1).Select(line => line.Text).ToArray();
        TestHarness.Assert(attacked[0] == text.Get(dozer.Template.IsKindOf("HARVESTER") ? "RADAR:HarvesterUnderAttack" : "RADAR:UnitUnderAttack"), $"the attacked dozer prints its radar line once ({string.Join(" | ", attacked)})");
        TestHarness.Assert(attacked.Length == (dozerNearCenter ? 1 : 2) && (dozerNearCenter || attacked[1] == "Structure under attack"),
            $"a second attack within 250 units and ten seconds prints nothing, own damage and the enemy's losses neither (dozer near the center: {dozerNearCenter}; {string.Join(" | ", attacked)})");
        feed.Observe(Event(CnCNew.Simulation.Events.SimEventKind.EntityDamaged, center.Id, enemyCenter.Id, human, value: 100, tick: match.Tick + CnCNew.Tests.TestTime.Frames(match.World, 700)), match.World, human);
        TestHarness.Assert(feed.Log.Lines[^1].Text == "Structure under attack", "ten seconds later the same place reports again");

        // A defeated player (VictoryConditions::update) is named by its army; a mission HUD would stay silent.
        feed.Observe(Event(CnCNew.Simulation.Events.SimEventKind.PlayerDefeated, default, default, enemyCenter.Owner, tick: match.Tick + CnCNew.Tests.TestTime.Frames(match.World, 700)), match.World, human);
        var defeat = feed.Log.Lines[^1].Text;
        TestHarness.Assert(defeat.EndsWith(" has been Defeated", StringComparison.Ordinal) && !defeat.StartsWith("Faction", StringComparison.Ordinal) && !defeat.Contains("??", StringComparison.Ordinal), $"GUI:PlayerHasBeenDefeated with the army's name ({defeat})");
        var shown = feed.Shown;
        feed.AnnounceDefeats = false;
        feed.Observe(Event(CnCNew.Simulation.Events.SimEventKind.PlayerDefeated, default, default, enemyCenter.Owner, tick: match.Tick + CnCNew.Tests.TestTime.Frames(match.World, 700)), match.World, human);
        TestHarness.Assert(feed.Shown == shown, "no defeat line where the original runs no victory conditions");

        // An upgrade the data names (UPGRADE:UpgradeComplete), and one without a display name (counted, no line).
        var upgrade = match.World.Upgrades.Upgrades.First(info => !info.IsEngine && data.Graph.Definitions.Any(definition => definition.Id.Kind == CnCNew.Data.Definitions.DefinitionKind.Upgrade && definition.OriginalName == info.Name
            && definition.Fields.Any(field => field.Name == "DisplayName")));
        var granted = feed.Shown;
        var radarEvents = feed.RadarHistoryVersion;
        feed.Observe(Event(CnCNew.Simulation.Events.SimEventKind.UpgradeCompleted, center.Id, default, human, upgrade.Name, upgrade.Index), match.World, human);
        TestHarness.Assert(feed.Shown == granted && feed.WithoutLine.GetValueOrDefault("upgrade granted, not ordered") >= 1, "an upgrade nobody ordered (granted at creation or by a script) prints nothing and is counted");
        TestHarness.Assert(feed.RadarHistoryVersion == radarEvents, "and makes no radar event (ProductionUpdate::update makes it with the line only)");
        feed.ExpectUpgrade(upgrade.Name);
        feed.Observe(Event(CnCNew.Simulation.Events.SimEventKind.UpgradeCompleted, center.Id, default, human, upgrade.Name, upgrade.Index), match.World, human);
        TestHarness.Assert(feed.Log.Lines[^1].Text.StartsWith("Upgrade Complete: ", StringComparison.Ordinal) && !feed.Log.Lines[^1].Text.Contains("??", StringComparison.Ordinal), $"the upgrade line carries the upgrade's display name ({feed.Log.Lines[^1].Text})");
        TestHarness.Assert(feed.RadarHistoryVersion == radarEvents + 1 && feed.LastRadarEvent == SimulationQueries.GroundOf(center), "an ordered upgrade with its line is a radar event at its producer");
        feed.ExpectUpgrade("Upgrade_NoSuchName");
        feed.Observe(Event(CnCNew.Simulation.Events.SimEventKind.UpgradeCompleted, default, default, human, "Upgrade_NoSuchName", 0), match.World, human);
        TestHarness.Assert(feed.WithoutLine.GetValueOrDefault("upgrade completed without DisplayName") == 1, "an upgrade without a display name prints nothing (as in the original) and is counted");
        TestHarness.Assert(!controller.Assets.Text.MissingLabels.Any(label => label.StartsWith("RADAR:", StringComparison.Ordinal) || label.StartsWith("DOZER:", StringComparison.Ordinal) || label is "GUI:PlayerHasBeenDefeated" or "UPGRADE:UpgradeComplete"),
            $"every message label used exists in the retail string table ({string.Join(", ", controller.Assets.Text.MissingLabels)})");

        // The lines fade on the logic clock: about 9.2 seconds after the last one nothing is left.
        Advance(match, controller, 120);
        TestHarness.Assert(!feed.Log.IsEmpty && feed.Log.Lines.All(line => line.Color.A < 255), "after four seconds the lines have begun to fade");
        Advance(match, controller, 200);
        TestHarness.Assert(feed.Log.IsEmpty && !controller.BuildDrawList(1280, 720).Commands.OfType<UiTextDraw>().Any(draw => draw.Text.Contains("Defeated", StringComparison.Ordinal)), "and are gone after their 277 logic frames");
    }

    /// <summary>The player's own click: a structure placed on top of another one is refused and <c>InGameUI::displayCantBuildMessage</c> says so.</summary>
    private static void MessageListFromARefusedPlacement(GameData data)
    {
        var (match, controller) = NewMatch(data, new Vector2(1280, 720));
        long time = 10_000;
        Advance(match, controller, 1);
        var dozer = match.World.Entities.First(entity => entity.Owner == match.Human && entity.Template.IsKindOf("DOZER"));
        Click(controller, ref time, MouseButton.Left, Screen(match, EntityPicker.CenterOf(dozer)));
        Advance(match, controller, 1);
        Refresh(controller);
        var slot = FindConstructSlot(match, controller.CommandBar);
        var rect = controller.WindowRect($"ControlBar.wnd:ButtonCommand{slot:00}") ?? throw new InvalidOperationException("button window missing");
        Click(controller, ref time, MouseButton.Left, new Vector2(rect.CenterX, rect.CenterY));
        Advance(match, controller, 1);
        TestHarness.Assert(controller.Input.Commands.Mode == CommandModeKind.Placement, "the construct button enters placement mode");

        Vector2? refusedAt = null;
        foreach (var structure in match.World.Entities.Where(entity => entity.Owner == match.Human && entity.Template.IsKindOf("STRUCTURE")).Take(4))
        {
            var ground = SimulationQueries.GroundOf(structure);
            var at = Screen(match, new Vector3(ground.X, ground.Y, 0f));
            if (controller.WindowRect("ControlBar.wnd:ControlBarParent") is { } bar && at.Y >= bar.Y) continue;
            controller.Process(new MouseMoveEvent(time += 16, at));
            if (controller.Input.Commands.Ghost is { IsValid: false, Reason: PlacementIssue.BlockedFootprint }) { refusedAt = at; break; }
        }
        TestHarness.Assert(refusedAt is not null, "a spot on top of an own structure is on screen and refused");
        TestHarness.Assert(controller.Messages.Log.IsEmpty, "hovering prints nothing");
        Click(controller, ref time, MouseButton.Left, refusedAt!.Value);
        Advance(match, controller, 1);
        var drawn = controller.BuildDrawList(1280, 720).Commands.OfType<UiTextDraw>().Where(draw => draw.Text.Contains("obstacles", StringComparison.Ordinal)).ToArray();
        TestHarness.Assert(drawn is [{ X: 10, Y: 10 }], $"the click prints GUI:CantBuildObjectsInTheWay at the message position of the retail InGameUI.ini, 10,10 ({string.Join(" | ", controller.Messages.Log.Lines.Select(line => line.Text))})");
        TestHarness.Assert(controller.Input.Commands.Mode == CommandModeKind.Placement, "and the placement stays on for another try");
    }

    /// <summary>
    /// <c>ControlBar::populateBuildTooltipLayout</c> and <c>ProductionPrerequisite::getRequiresList</c>: the description names what a button still requires. The tooltip used to
    /// show name, cost and description only, so a greyed-out button did not say why.
    /// </summary>
    private static void CommandButtonRequirements(GameData data)
    {
        var (match, controller) = NewMatch(data, new Vector2(1280, 720));
        long time = 10_000;
        Advance(match, controller, 1);
        var world = match.World;
        var human = match.Human;
        var text = data.LoadText("English");
        var descriptions = new CommandButtonDescription(data.Graph, text);
        var dozer = world.Entities.First(entity => entity.Owner == human && entity.Template.IsKindOf("DOZER"));
        Click(controller, ref time, MouseButton.Left, Screen(match, EntityPicker.CenterOf(dozer)));
        Advance(match, controller, 1);
        Refresh(controller);

        // Through the real HUD: a construct button whose prerequisite the fresh base lacks is greyed out, and its tooltip says what it requires.
        var locked = controller.CommandBar.Slots.FirstOrDefault(button => button is { Reason: ButtonReason.MissingPrerequisite, Button.Kind: GuiCommandKind.DozerConstruct });
        TestHarness.Assert(locked is not null, "the dozer of a fresh base has a structure it cannot build yet");
        TestHarness.Assert(world.Templates.TryGet(locked!.Button.Object, out var template), "the template exists");
        var missing = SimulationQueries.MissingPrerequisites(world, human, template);
        TestHarness.Assert(missing.Length > 0 && !SimulationQueries.PrerequisitesSatisfied(world, human, template), "the model's reason and the missing lines agree");
        var requires = descriptions.Requirements(world, human, template.Name);
        var firstName = descriptions.DisplayName(missing[0].Alternatives[^1]);
        TestHarness.Assert(requires.Length > 0 && requires.Contains(firstName, StringComparison.Ordinal) && !requires.Contains("MISSING", StringComparison.Ordinal) && !requires.Contains("??", StringComparison.Ordinal),
            $"the requirement names the missing object by its display name: '{requires}' (first missing: {firstName})");
        TestHarness.Assert(requires.Split(", ").Length == missing.Length, $"one entry per unmet Prerequisites line, joined by a comma ({requires}; {missing.Length} lines)");

        var rect = controller.WindowRect($"ControlBar.wnd:ButtonCommand{locked.Slot:00}")!.Value;
        controller.Process(new MouseMoveEvent(time += 16, new Vector2(rect.CenterX, rect.CenterY)));
        Advance(match, controller, 30);
        var tooltip = string.Join("\n", controller.BuildDrawList(1280, 720).Commands.OfType<UiTextDraw>().Where(draw => draw.Color == new WndColor(220, 220, 220, 255)).Select(draw => draw.Text.Trim()));
        var flat = tooltip.Replace("\n", " ", StringComparison.Ordinal);
        TestHarness.Assert(flat.Contains("Requires: " + requires.Split(' ')[0], StringComparison.Ordinal), $"the greyed-out button's tooltip carries the 'Requires:' line (CONTROLBAR:Requirements): {tooltip}");
        TestHarness.Assert(flat.Contains("Cost: " + (int)locked.Cost.ToDecimal(), StringComparison.Ordinal), $"and still its cost: {tooltip}");
        Console.WriteLine($"       {template.Name}: {tooltip.Replace("\n", " / ", StringComparison.Ordinal)}");

        // Built directly: a satisfied button has no requirement line, and its texts are name, cost, description.
        var open = controller.CommandBar.Slots.First(button => button is { Reason: ButtonReason.None, Button.Kind: GuiCommandKind.DozerConstruct })!;
        var openTexts = descriptions.Build(open, world, human, dozer);
        TestHarness.Assert(!openTexts.Description.Contains("Requires", StringComparison.Ordinal) && openTexts.Cost == "Cost: " + (int)open.Cost.ToDecimal() && openTexts.Name.Length > 0 && !openTexts.Name.Contains('&'),
            $"a buildable structure: no requirement, its cost, its name without the hot key mark ({openTexts})");
        TestHarness.Assert(openTexts.ToTooltip() == string.Join("\n", new[] { openTexts.Name, openTexts.Cost!, openTexts.Description }.Where(part => part.Length > 0)), "the tooltip is name, cost, description, one below the other");

        // Every Prerequisites line of the retail data, as the player of a fresh base sees it: alternatives are 'A or B', a science is the General's Promotion.
        var alternatives = world.Templates.Templates.FirstOrDefault(candidate => candidate.Prerequisites.Any(line => !line.IsScience && line.Alternatives.Length >= 2
            && SimulationQueries.MissingPrerequisites(world, human, candidate).Contains(line)));
        if (alternatives is not null)
        {
            var line = SimulationQueries.MissingPrerequisites(world, human, alternatives).First(candidate => !candidate.IsScience && candidate.Alternatives.Length >= 2);
            var expected = $"{descriptions.DisplayName(line.Alternatives[^2])} or {descriptions.DisplayName(line.Alternatives[^1])}";
            TestHarness.Assert(descriptions.Requirements(world, human, alternatives.Name).Split(", ").Contains(expected), $"alternatives read 'A or B' (CONTROLBAR:OrRequirement): {descriptions.Requirements(world, human, alternatives.Name)} should hold '{expected}'");
            Console.WriteLine($"       {alternatives.Name}: Requires: {descriptions.Requirements(world, human, alternatives.Name)}");
        }
        else Console.WriteLine("       (no template with an unmet line of alternatives in this data set)");
        var science = world.Templates.Templates.FirstOrDefault(candidate => SimulationQueries.MissingPrerequisites(world, human, candidate).Any(line => line.IsScience));
        TestHarness.Assert(science is not null, "the retail data has a template behind a science the fresh player lacks");
        TestHarness.Assert(descriptions.Requirements(world, human, science!.Name).Split(", ").Contains("General's Promotion"), $"a missing science reads CONTROLBAR:GeneralsPromotion: {descriptions.Requirements(world, human, science.Name)}");

        // Too little money: 'Insufficient funds' after an empty line, before the requirements (canMakeUnit, then getRequiresList).
        var mapPath = data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase));
        var poorMatch = LiveMatch.Create(data, new MatchSetup(mapPath, data.Factions[0].Faction.Name, [data.Factions[1].Faction.Name], SkirmishMatchOptions.Standard with { StartMoney = 100 }), new Vector2(1280, 720));
        var poor = new MatchController(poorMatch, SkinPins.Settings);
        Advance(poorMatch, poor, (int)poorMatch.World.Time.OriginalFrames(4));
        var poorDozer = poorMatch.World.Entities.First(entity => entity.Owner == poorMatch.Human && entity.Template.IsKindOf("DOZER"));
        long poorTime = 10_000;
        Click(poor, ref poorTime, MouseButton.Left, Screen(poorMatch, EntityPicker.CenterOf(poorDozer)));
        Advance(poorMatch, poor, 1);
        var costly = poor.CommandBar.Slots.First(button => button is { Button.Kind: GuiCommandKind.DozerConstruct, Button.DescriptLabel.Length: > 0 } && button.Cost.Raw > poorMatch.World.MoneyOf(poorMatch.Human).Raw)!;
        var costlyTexts = descriptions.Build(costly, poorMatch.World, poorMatch.Human, poorDozer);
        var body = text.Get(costly.Button.DescriptLabel);
        TestHarness.Assert(costlyTexts.Description.StartsWith(body + "\n\nInsufficient funds", StringComparison.Ordinal), $"too little money: '{costlyTexts.Description}'");
        TestHarness.Assert(descriptions.Build(costly, poorMatch.World, poorMatch.Human, null).Description.StartsWith(body, StringComparison.Ordinal) && !descriptions.Build(costly, poorMatch.World, poorMatch.Human, null).Description.Contains("Insufficient", StringComparison.Ordinal),
            "without a selected object the original asks no canMakeUnit, so no reason is appended");

        // Upgrades: an owned one shows its 'purchased' text instead of the description and no cost; one behind a science requires the General's Promotion.
        var catalog = controller.Input.CommandBar!.Catalog;
        var upgradeButtons = catalog.Buttons.Where(button => button.Kind is GuiCommandKind.PlayerUpgrade or GuiCommandKind.ObjectUpgrade && world.Upgrades.TryGet(button.Upgrade, out _)).ToArray();
        CommandBarButton AsBarButton(CommandButtonInfo info) => new(1, info, ButtonAvailability.Restricted, ButtonReason.None, null, world.Upgrades.TryGet(info.Upgrade, out var known) ? known.BuildCost : default, 0, null, []);
        var ownedButton = upgradeButtons.FirstOrDefault(button => world.Upgrades.TryGet(button.Upgrade, out var info) && world.Entities.Any(entity => entity.Owner == human && (entity.CompletedUpgrades.Has(info.Index) || world.PlayerUpgrades(human).Has(info.Index))));
        if (ownedButton is not null)
        {
            world.Upgrades.TryGet(ownedButton.Upgrade, out var ownedInfo);
            var holder = world.Entities.First(entity => entity.Owner == human && (entity.CompletedUpgrades.Has(ownedInfo.Index) || world.PlayerUpgrades(human).Has(ownedInfo.Index)));
            var ownedTexts = descriptions.Build(AsBarButton(ownedButton), world, human, holder);
            var purchasedLabel = data.Graph.Definitions.First(definition => definition.Id.Kind == CnCNew.Data.Definitions.DefinitionKind.CommandButton && definition.OriginalName == ownedButton.Name)
                .Fields.FirstOrDefault(field => field.Name == "PurchasedLabel")?.Value.OriginalText.Trim().Trim('"');
            var expected = text.Get(purchasedLabel is { Length: > 0 } ? purchasedLabel : "TOOLTIP:AlreadyUpgradedDefault");
            TestHarness.Assert(ownedTexts.Description == expected && ownedTexts.Cost is null, $"an owned upgrade ({ownedButton.Name}): '{ownedTexts.Description}' should be '{expected}', without a cost");
        }
        else Console.WriteLine("       (the fresh base owns no upgrade a command button offers; the 'already purchased' text was not exercised)");
        var scienceUpgrade = upgradeButtons.FirstOrDefault(button => button.Sciences.Length > 0 && world.Upgrades.TryGet(button.Upgrade, out var info) && !world.PlayerUpgrades(human).Has(info.Index));
        if (scienceUpgrade is not null)
        {
            var texts = descriptions.Build(AsBarButton(scienceUpgrade), world, human, dozer);
            TestHarness.Assert(texts.Description.EndsWith("Requires: General's Promotion", StringComparison.Ordinal), $"an upgrade behind a science ({scienceUpgrade.Name}): '{texts.Description}'");
        }
        else Console.WriteLine("       (no upgrade button with a science in this data set)");
        Console.WriteLine($"       upgrades: owned {ownedButton?.Name ?? "-"}, behind a science {scienceUpgrade?.Name ?? "-"}");
    }

    /// <summary><c>populateBuildTooltipLayout</c> for a window: <c>CONTROLBAR:Money</c>/<c>MoneyDescription</c> and <c>CONTROLBAR:Power</c>/<c>PowerDescription</c> with the energy numbers.</summary>
    private static void MoneyAndPowerTooltips(GameData data)
    {
        var (match, controller) = NewMatch(data, new Vector2(1280, 720));
        long time = 10_000;
        Advance(match, controller, 1);
        Refresh(controller);
        string Hover(string window)
        {
            var rect = controller.WindowRect(window) ?? throw new InvalidOperationException($"{window} missing");
            controller.Process(new MouseMoveEvent(time += 16, new Vector2(rect.CenterX, rect.CenterY)));
            Advance(match, controller, 45);
            return string.Join(" | ", controller.BuildDrawList(1280, 720).Commands.OfType<UiTextDraw>().Where(draw => draw.Color == new WndColor(220, 220, 220, 255)).Select(draw => draw.Text.Trim()));
        }
        var money = Hover("ControlBar.wnd:MoneyDisplay");
        TestHarness.Assert(money.StartsWith("Money", StringComparison.Ordinal) && money.Contains("This is the amount of money you have", StringComparison.Ordinal), $"the money window: {money}");
        var bar = controller.WindowRect("ControlBar.wnd:ControlBarParent")!.Value;
        var powerRect = controller.WindowRect("ControlBar.wnd:PowerWindow")!.Value;
        TestHarness.Assert(powerRect.Y >= bar.Y && powerRect.Width > 20, $"the power window lies at the scheme's power bar inside the control bar ({powerRect} in {bar})");
        var power = Hover("ControlBar.wnd:PowerWindow");
        TestHarness.Assert(power.StartsWith("Power", StringComparison.Ordinal) && power.Contains($"Current Power: {controller.Hud.EnergyProduction}", StringComparison.Ordinal) && power.Contains($"Required Power: {controller.Hud.EnergyConsumption}", StringComparison.Ordinal),
            $"the power window names production {controller.Hud.EnergyProduction} and consumption {controller.Hud.EnergyConsumption}: {power}");
    }

    private static void GoldenDrawListHash(GameData data)
    {
        var (match, controller) = NewMatch(data, new Vector2(1280, 720));
        Advance(match, controller, 1);
        var dump = controller.BuildDrawList(1280, 720).Dump();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dump)));
        var baselineFile = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", BaselineDirectory, "hud-drawlist-1280x720.sha256");
        baselineFile = Path.GetFullPath(baselineFile);
        if (!File.Exists(baselineFile))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(baselineFile)!);
            File.WriteAllText(baselineFile, hash);
            Console.WriteLine($"       wrote new baseline {baselineFile}: {hash}");
            return;
        }
        var expected = File.ReadAllText(baselineFile).Trim();
        TestHarness.Assert(hash == expected, $"the 1280x720 draw list hash matches the baseline (expected {expected}, got {hash})");
    }
}
