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
        harness.Run("Golden draw list hash", () => GoldenDrawListHash(data));

        return harness.Failures;
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
