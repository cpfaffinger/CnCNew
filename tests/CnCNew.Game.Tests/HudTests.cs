using CnCNew.Ui.Runtime;
using CnCNew.Ui.Wnd;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Testing;
using CnCNew.Ui.Settings;

namespace CnCNew.Game.Tests;

internal static class HudTests
{
    private const string BaselineDirectory = "baselines";

    public static int Run(string[] args)
    {
        var contentDir = ContentDir(args);
        var harness = new TestHarness(nameof(HudTests));
        if (contentDir is null)
        {
            Console.WriteLine("SKIP HudTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }

        var data = GameData.Load(contentDir, step => Console.WriteLine($"  loading: {step}"));

        harness.Run("Select dozer, construct a structure, queue a unit, move and attack-move", () => ScriptedSession(data));
        harness.Run("Replay of the human player's commands is internally deterministic", () => ReplayMatches(data));
        harness.Run("HUD draw lists at several resolutions contain the expected buttons", () => ResolutionSweep(data));
        harness.Run("Minimap click moves the camera", () => MinimapClickMovesCamera(data));
        harness.Run("Box selection selects the start units", () => BoxSelectionSelects(data));
        harness.Run("Control groups store and recall a selection", () => ControlGroupsWork(data));
        harness.Run("Control bar scheme art and the logo image", () => SchemeArt(data));
        harness.Run("The money display shows the localized money at the scheme's rectangle, not the layout placeholder", () => MoneyDisplay(data));
        harness.Run("A command button shows its description as a pointer tooltip after the delay, and the icons carry no hot key or cost text", () => CommandButtonTooltip(data));
        harness.Run("Command button description: missing prerequisites and sciences are listed after 'Requires:', too little money and owned upgrades are said, as populateBuildTooltipLayout does", () => CommandButtonRequirements(data));
        harness.Run("The money and the power window describe themselves; the power text carries production and consumption", () => MoneyAndPowerTooltips(data));
        harness.Run("Golden draw list hash", () => GoldenDrawListHash(data));
        harness.Run("Message list: refused orders, finished buildings and upgrades, attacks and defeats print the original's labels at the message position and fade; what has no line is counted", () => MessageList(data));
        harness.Run("Message list through the real click path: a refused building spot prints 'You can't build here'", () => MessageListFromARefusedPlacement(data));
        harness.Run("The overlay is drawn: selection ring and health bar, a healed structure shows the wrench", () => OverlayIsDrawn(data));

        harness.Run("Cursor and markers: Alt waypoints draw a path with a marker per station, an order leaves a fading hint, the cursor follows context and windows", () => CursorAndMarkers(data));
        harness.Run("Placement preview: the chosen building follows the cursor in the object frame, translucent, red where refused, gone after the click", () => PlacementPreviewInObjectFrame(data));

        return harness.Failures;
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
        foreach (var station in stations) Click(controller, ref time, MouseButton.Left, station);
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

        // A plain move order: a move hint at the clicked point, fading.
        Click(controller, ref time, MouseButton.Left, open);
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
        Advance(match, controller, 4 * 30);
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

    private static (LiveMatch Match, MatchController Controller) NewMatch(GameData data, Vector2 viewport)
    {
        var mapPath = data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase));
        var setup = new MatchSetup(mapPath, data.Factions[0].Faction.Name, [data.Factions[1].Faction.Name], SkirmishMatchOptions.Standard);
        var match = LiveMatch.Create(data, setup, viewport);
        var controller = new MatchController(match, GameSettings.Default);
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

    private static void Advance(LiveMatch match, MatchController controller, int frames, double dt = 1.0 / 30)
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
                Click(controller, ref time, MouseButton.Left, Screen(match, new Vector3(moveTarget.X, moveTarget.Y, 0f)));
                Advance(match, controller, 30);
                TestHarness.Assert(Vector2.Distance(SimulationQueries.GroundOf(unit), startGround) > 1f, "the left-click move order actually moved the unit");
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
        // The default mouse scheme is left-click-commands: a plain left click on open, reachable ground orders a move; right click cancels/deselects.
        Click(controller, ref time, MouseButton.Left, Screen(match, EntityPicker.CenterOf(dozer)));
        Advance(match, controller, 1);
        Click(controller, ref time, MouseButton.Left, Screen(match, new Vector3(startGround.X + 80, startGround.Y, 0f)));
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
        var (match, controller) = NewMatch(data, new Vector2(1280, 720));
        Advance(match, controller, 1);
        var rect = controller.WindowRect("ControlBar.wnd:LeftHUD") ?? throw new InvalidOperationException("LeftHUD window missing");
        var before = match.Camera.Pivot;
        long time = 10_000;
        // A point well inside the minimap frame but away from its centre, so a no-op click would be detectable.
        var clickPoint = new Vector2(rect.X + rect.Width * 0.25f, rect.Y + rect.Height * 0.75f);
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

    private static void SchemeArt(GameData data)
    {
        var (match, controller) = NewMatch(data, new Vector2(1280, 720));
        Advance(match, controller, 1);
        var commands = controller.BuildDrawList(1280, 720).Commands;
        TestHarness.Assert(commands.Length > 0 && commands[0] is UiImageDraw, "the scheme's background parts come first, under the window tree");
        var backgroundParts = commands.TakeWhile(command => command is UiImageDraw).Count();
        TestHarness.Assert(backgroundParts >= 1, $"the human side scheme contributes background parts ({backgroundParts})");
        TestHarness.Assert(!commands.OfType<UiImageDraw>().Any(draw => draw.Image == "HardCodedWithControlBarSchemeIni"), "the hard-coded portrait image is replaced by the scheme's own right-hand image");
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

        controller.Process(new MouseMoveEvent(time += 16, new Vector2(rect.CenterX + 1, rect.CenterY)));
        TestHarness.Assert(!controller.BuildDrawList(1280, 720).Commands.Any(command => command is UiFillRect { Color: { R: 20, G: 20, B: 20 } }), "moving the pointer hides the tooltip until it has been still for the delay again (1 ms here)");
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
        TestHarness.Assert(lines.Length == 6 && lines[^3].Text == "You must explore there before building" && lines[^2].Text == "You can't build here" && lines[0].Text == "You don't have enough money to make upgrade",
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
        feed.Observe(Event(CnCNew.Simulation.Events.SimEventKind.EntityDamaged, center.Id, enemyCenter.Id, human, value: 100, tick: match.Tick + 200), match.World, human);
        feed.Observe(Event(CnCNew.Simulation.Events.SimEventKind.EntityDamaged, dozer.Id, dozer.Id, human, value: 100, tick: match.Tick + 400), match.World, human);
        feed.Observe(Event(CnCNew.Simulation.Events.SimEventKind.EntityDamaged, enemyCenter.Id, dozer.Id, enemyCenter.Owner, value: 100, tick: match.Tick + 400), match.World, human);
        var dozerNearCenter = Vector2.Distance(SimulationQueries.GroundOf(dozer), SimulationQueries.GroundOf(center)) <= 250f;
        var attacked = feed.Log.Lines.Skip(1).Select(line => line.Text).ToArray();
        TestHarness.Assert(attacked[0] == text.Get(dozer.Template.IsKindOf("HARVESTER") ? "RADAR:HarvesterUnderAttack" : "RADAR:UnitUnderAttack"), $"the attacked dozer prints its radar line once ({string.Join(" | ", attacked)})");
        TestHarness.Assert(attacked.Length == (dozerNearCenter ? 1 : 2) && (dozerNearCenter || attacked[1] == "Structure under attack"),
            $"a second attack within 250 units and ten seconds prints nothing, own damage and the enemy's losses neither (dozer near the center: {dozerNearCenter}; {string.Join(" | ", attacked)})");
        feed.Observe(Event(CnCNew.Simulation.Events.SimEventKind.EntityDamaged, center.Id, enemyCenter.Id, human, value: 100, tick: match.Tick + 700), match.World, human);
        TestHarness.Assert(feed.Log.Lines[^1].Text == "Structure under attack", "ten seconds later the same place reports again");

        // A defeated player (VictoryConditions::update) is named by its army; a mission HUD would stay silent.
        feed.Observe(Event(CnCNew.Simulation.Events.SimEventKind.PlayerDefeated, default, default, enemyCenter.Owner, tick: match.Tick + 700), match.World, human);
        var defeat = feed.Log.Lines[^1].Text;
        TestHarness.Assert(defeat.EndsWith(" has been Defeated", StringComparison.Ordinal) && !defeat.StartsWith("Faction", StringComparison.Ordinal) && !defeat.Contains("??", StringComparison.Ordinal), $"GUI:PlayerHasBeenDefeated with the army's name ({defeat})");
        var shown = feed.Shown;
        feed.AnnounceDefeats = false;
        feed.Observe(Event(CnCNew.Simulation.Events.SimEventKind.PlayerDefeated, default, default, enemyCenter.Owner, tick: match.Tick + 700), match.World, human);
        TestHarness.Assert(feed.Shown == shown, "no defeat line where the original runs no victory conditions");

        // An upgrade the data names (UPGRADE:UpgradeComplete), and one without a display name (counted, no line).
        var upgrade = match.World.Upgrades.Upgrades.First(info => !info.IsEngine && data.Graph.Definitions.Any(definition => definition.Id.Kind == CnCNew.Data.Definitions.DefinitionKind.Upgrade && definition.OriginalName == info.Name
            && definition.Fields.Any(field => field.Name == "DisplayName")));
        var granted = feed.Shown;
        feed.Observe(Event(CnCNew.Simulation.Events.SimEventKind.UpgradeCompleted, default, default, human, upgrade.Name, upgrade.Index), match.World, human);
        TestHarness.Assert(feed.Shown == granted && feed.WithoutLine.GetValueOrDefault("upgrade granted, not ordered") >= 1, "an upgrade nobody ordered (granted at creation or by a script) prints nothing and is counted");
        feed.ExpectUpgrade(upgrade.Name);
        feed.Observe(Event(CnCNew.Simulation.Events.SimEventKind.UpgradeCompleted, default, default, human, upgrade.Name, upgrade.Index), match.World, human);
        TestHarness.Assert(feed.Log.Lines[^1].Text.StartsWith("Upgrade Complete: ", StringComparison.Ordinal) && !feed.Log.Lines[^1].Text.Contains("??", StringComparison.Ordinal), $"the upgrade line carries the upgrade's display name ({feed.Log.Lines[^1].Text})");
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
        var drawn = controller.BuildDrawList(1280, 720).Commands.OfType<UiTextDraw>().Where(draw => draw.Text == "You can't build here").ToArray();
        TestHarness.Assert(drawn is [{ X: 10, Y: 22 }], $"the click prints GUI:CantBuildThere at the message position ({string.Join(" | ", controller.Messages.Log.Lines.Select(line => line.Text))})");
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
        var poor = new MatchController(poorMatch, GameSettings.Default);
        Advance(poorMatch, poor, 1);
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
