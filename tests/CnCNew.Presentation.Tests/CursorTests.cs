using System.Numerics;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Images.Tests;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// Phase 16, items 2 and 3: the Mouse.ini cursor table, the decoded cursor set, the cursor the input layer chooses (context, command mode,
/// modifier keys, scrolling), and the markers of waypoint paths and freshly given orders.
/// </summary>
internal static class CursorTests
{
    private static readonly PlayerId Me = InputTests.Me;
    private static readonly PlayerId Enemy = InputTests.Enemy;

    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(CursorTests));
        harness.Run("Mouse.ini blocks: definitions, files per direction, unknown names, fields and values are issues", CatalogParsing);
        harness.Run("Cursor set: shared files, missing and undecodable files are listed and never replaced; direction frames", CursorSetLoading);
        harness.Run("Cursor animation: frame by time, restart when the cursor changes, cursors without a picture", Animation);
        harness.Run("Cursor by context, command mode and modifier keys (no mouse move needed), over a window, while scrolling", CursorByContext);
        harness.Run("Waypoints with Alt: three stations, three markers; a station goes when reached, the path with a new order", WaypointMarkers);
        harness.Run("Order hints: an order leaves a marker that expires after 40 frames", OrderHints);
        var contentDirectory = args.SkipWhile(value => value != "--content-dir").Skip(1).FirstOrDefault();
        if (contentDirectory is not null) harness.Run("Real data: every cursor the input layer can show is defined in Mouse.ini and decodes", () => RealData(contentDirectory));
        return harness.Failures;
    }

    // ------------------------------------------------------------------------------------------------ fixtures

    private const string MouseIni = """
        MouseCursor Normal
          Texture = TPointer
          Image = TPointer
        End
        MouseCursor Arrow
          Texture = TPointer
          Image = TPointer
        End
        MouseCursor Scroll
          Texture = TScroll
          Image = TScroll
          Directions = 4
        End
        MouseCursor Move
          Image = TMove
          Texture = TMove
          HotSpot = X:3 Y:4
          Frames = 2
          FPS = 10.0
        End
        MouseCursor AttackObj
          Texture = TBroken
          W3DModel = Unused
        End
        MouseCursor Bogus
          Texture = TPointer
        End
        MouseCursor Select
          Texture = TPointer
          Sparkle = Yes
        End
        MouseCursor Target
          Texture = TPointer
          Directions = many
        End
        MouseCursor Dock
          Image = TPointer
        End
        """;

    private static readonly Lazy<SyntheticFixture> MouseFixture = new(() => new SyntheticFixture(CnCNew.Tests.TestRate.Default, files: new Dictionary<string, string>(SyntheticFixture.Files) { ["Data/INI/Mouse.ini"] = MouseIni }));

    private static (byte, byte, byte) Colour(byte value) => (value, value, value);

    private static Dictionary<string, byte[]> CursorFiles()
    {
        byte[] Frame(byte shade, int hotspot = 0) => CursorFixtures.Cur(2, 2, hotspot, hotspot, [Colour(shade), Colour(shade), Colour(shade), Colour(0)], [false, false, false, true]);
        return new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Data/Cursors/tpointer.ani"] = CursorFixtures.Ani([Frame(10)], 10),
            ["Data/Cursors/TScroll0.ani"] = CursorFixtures.Ani([Frame(20)], 10),
            ["Data/Cursors/TScroll1.ani"] = CursorFixtures.Ani([Frame(21)], 10),
            ["Data/Cursors/TScroll2.ani"] = CursorFixtures.Ani([Frame(22)], 10),
            ["Data/Cursors/TMove.ani"] = CursorFixtures.Ani([Frame(30, 1), Frame(31, 1)], 6),
            ["Data/Cursors/TBroken.ani"] = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12],
        };
    }

    private static CursorSet LoadSynthetic()
    {
        var files = CursorFiles();
        return CursorSet.Load(MouseCursorCatalog.FromGraph(MouseFixture.Value.Graph), files.ContainsKey, path => new MemoryStream(files[path]));
    }

    // ------------------------------------------------------------------------------------------------ catalog and files

    private static void CatalogParsing()
    {
        var catalog = MouseCursorCatalog.FromGraph(MouseFixture.Value.Graph);
        TestHarness.Assert(catalog.Definitions.Select(definition => definition.Kind).SequenceEqual([CursorKind.Normal, CursorKind.Arrow, CursorKind.Scroll, CursorKind.Move, CursorKind.AttackObj]),
            $"five usable blocks in cursor order ({string.Join(", ", catalog.Definitions.Select(definition => definition.Name))})");
        TestHarness.Assert(catalog.TryGet(CursorKind.Move, out var move) && move is { Texture: "TMove", Image: "TMove", Directions: 1, Frames: 2, FramesPerSecond: 10f, HotSpot: (3, 4) },
            "the fields of a block");
        TestHarness.Assert(move.Files.SequenceEqual(["Data/Cursors/TMove.ani"]), "one file for a plain cursor");
        TestHarness.Assert(catalog.TryGet(CursorKind.Scroll, out var scroll) && scroll.Files.SequenceEqual(["Data/Cursors/TScroll0.ani", "Data/Cursors/TScroll1.ani", "Data/Cursors/TScroll2.ani", "Data/Cursors/TScroll3.ani"]),
            "one file per direction for a directional cursor");
        TestHarness.Assert(catalog.TryGet(CursorKind.Arrow, out var arrow) && arrow is { Directions: 1, Frames: 1, FramesPerSecond: 20f, HotSpot: null }, "the defaults of CursorInfo");
        var issues = catalog.Issues.Select(issue => (issue.Kind, issue.Cursor)).Order().ToList();
        TestHarness.Assert(issues.SequenceEqual(new[]
        {
            (MouseCursorIssueKind.UnknownCursorName, "Bogus"), (MouseCursorIssueKind.UnknownField, "Select"), (MouseCursorIssueKind.BadValue, "Target"), (MouseCursorIssueKind.MissingTexture, "Dock"),
        }.Order()), $"each bad block is one issue and no definition ({string.Join("; ", catalog.Issues)})");
        TestHarness.Assert(!catalog.TryGet(CursorKind.Select, out _) && !catalog.TryGet(CursorKind.Target, out _) && !catalog.TryGet(CursorKind.Dock, out _), "a block with an issue is not used");
        TestHarness.Assert(catalog.WithoutDefinition.Contains(CursorKind.Build) && catalog.WithoutDefinition.Contains(CursorKind.Select) && !catalog.WithoutDefinition.Contains(CursorKind.Move),
            "cursors without a block are listed");
    }

    private static void CursorSetLoading()
    {
        var set = LoadSynthetic();
        TestHarness.Assert(set.Files.Select(file => file.File).SequenceEqual(["Data/Cursors/TMove.ani", "Data/Cursors/TPointer.ani", "Data/Cursors/TScroll0.ani", "Data/Cursors/TScroll1.ani", "Data/Cursors/TScroll2.ani"]),
            $"Normal and Arrow share one decoded file ({string.Join(", ", set.Files.Select(file => file.File))})");
        TestHarness.Assert(set.FrameCount == 6 && set.InvertedPixels == 0, "frames over all files");
        TestHarness.Assert(set.MissingFiles.SequenceEqual(["Data/Cursors/TScroll3.ani"]), "the missing direction file is listed");
        TestHarness.Assert(set.UndecodableFiles is [var broken] && broken.StartsWith("Data/Cursors/TBroken.ani (", StringComparison.Ordinal), "the undecodable file is listed with its reason");
        TestHarness.Assert(set.Unusable.SequenceEqual([CursorKind.Scroll, CursorKind.AttackObj]), "the kinds that lack a picture");

        TestHarness.Assert(set.Resolve(CursorState.Arrow, 0, out var arrow) && arrow.Image.RgbaPixels[0] == 10 && arrow.Image.RgbaPixels[15] == 0, "the arrow resolves to its picture (last pixel transparent)");
        TestHarness.Assert(set.Resolve(new CursorState(CursorKind.Scroll, new Vector2(1, 0)), 0, out var right) && right.Image.RgbaPixels[0] == 20, "scrolling right is direction file 0");
        TestHarness.Assert(set.Resolve(new CursorState(CursorKind.Scroll, new Vector2(0, 1)), 0, out var down) && down.Image.RgbaPixels[0] == 21, "scrolling down (screen y grows downwards) is the next file clockwise");
        TestHarness.Assert(set.Resolve(new CursorState(CursorKind.Scroll, new Vector2(-1, 0)), 0, out var left) && left.Image.RgbaPixels[0] == 22, "scrolling left");
        TestHarness.Assert(!set.Resolve(new CursorState(CursorKind.Scroll, new Vector2(0, -1)), 0, out _), "the direction whose file is missing has no picture: nothing stands in");
        TestHarness.Assert(!set.Resolve(new CursorState(CursorKind.AttackObj, Vector2.Zero), 0, out _), "an undecodable cursor has no picture");
        TestHarness.Assert(!set.Resolve(new CursorState(CursorKind.Build, Vector2.Zero), 0, out _), "a cursor without a block has no picture");

        // W3DMouse::setCursorDirection with eight directions: 0 right, then clockwise on screen.
        (Vector2 Offset, int Frame)[] table =
        [
            (new(1, 0), 0), (new(1, 1), 1), (new(0, 1), 2), (new(-1, 1), 3), (new(-1, 0), 4), (new(-1, -1), 5), (new(0, -1), 6), (new(1, -1), 7),
            (new(1, -0.1f), 0), (new(1.6f, 2.0f), 1), (Vector2.Zero, 0),
        ];
        foreach (var (offset, frame) in table)
            TestHarness.Assert(CursorSet.DirectionIndex(offset, 8) == frame, $"direction of {offset}: expected {frame}, got {CursorSet.DirectionIndex(offset, 8)}");
        TestHarness.Assert(CursorSet.DirectionIndex(new Vector2(0, 1), 1) == 0, "a cursor without directions has one frame set");
        TestHarness.Assert(set.Report().Contains("missing files [Data/Cursors/TScroll3.ani]", StringComparison.Ordinal) && set.Report().Contains("Bogus: UnknownCursorName", StringComparison.Ordinal),
            "the report names what is missing");
    }

    private static void Animation()
    {
        var animator = new CursorAnimator(LoadSynthetic());
        var move = new CursorState(CursorKind.Move, Vector2.Zero);
        TestHarness.Assert(animator.Update(move, 5000, out var first) && first.FrameIndex == 0 && first.Image is { HotspotX: 1, HotspotY: 1 }, "a new cursor starts at its first frame; the hot spot comes from the file");
        TestHarness.Assert(animator.Update(move, 5099, out var still) && still.FrameIndex == 0, "six jiffies (100 ms) per step");
        TestHarness.Assert(animator.Update(move, 5100, out var second) && second.FrameIndex == 1 && second.Image.RgbaPixels[0] == 31, "then the second frame");
        TestHarness.Assert(animator.Update(move, 5200, out var looped) && looped.FrameIndex == 0, "and round again");
        TestHarness.Assert(animator.Update(CursorState.Arrow, 5250, out var arrow) && arrow.Kind == CursorKind.Arrow, "another cursor");
        TestHarness.Assert(animator.Update(move, 5260, out var restarted) && restarted.FrameIndex == 0, "coming back restarts the animation");
        TestHarness.Assert(animator.Update(move, 5360, out var running) && running.FrameIndex == 1, "which then runs from that moment");

        TestHarness.Assert(!animator.Update(new CursorState(CursorKind.AttackObj, Vector2.Zero), 5400, out _) && animator.Unresolved[CursorKind.AttackObj] == 1, "a defined cursor without a picture is counted");
        TestHarness.Assert(!animator.Update(new CursorState(CursorKind.Build, Vector2.Zero), 5500, out _) && !animator.Unresolved.ContainsKey(CursorKind.Build),
            "a cursor the data does not define shows nothing, as in the original, and is not a fault");
        TestHarness.Assert(animator.State.Kind == CursorKind.Build, "the animator remembers what was asked for");
    }

    // ------------------------------------------------------------------------------------------------ cursor choice

    private static void CursorByContext()
    {
        var arena = new InputTests.Arena();
        arena.Spawn("Ranger", Me, 40, 40);
        arena.Spawn("Dozer", Me, 60, 60);
        arena.Spawn("Barracks", Me, 150, 110);
        arena.Spawn("Ranger", Enemy, 120, 40);
        arena.Step(2);
        var world = arena.World;
        var view = new InputTests.TopDownView();
        var labels = new Dictionary<string, string> { ["CONTROLBAR:AttackMove"] = "&Attack Move", ["CONTROLBAR:ConstructPlant"] = "Construct &Plant", ["CONTROLBAR:SetRallyPoint"] = "&Rally" };
        var input = new GameInputController(Me, arena.Sink, HotkeyMap.Parse(InputTests.CommandMapText).WithEngineDefaults(), CommandButtonCatalog.Build(InputTests.Fixture.Graph),
            label => labels.GetValueOrDefault(label));
        var (ranger, dozer, barracks) = (arena.Find("Ranger", Me, 40, 40), arena.Find("Dozer", Me, 60, 60), arena.Find("Barracks", Me, 150, 110));
        long time = 1000;
        void Send(InputEvent inputEvent) => input.Process(inputEvent, world, view);
        void MoveTo(float x, float y) => Send(new MouseMoveEvent(time += 16, InputTests.Screen(x, y)));
        void Key(KeyCode key, bool down) => Send(new KeyEvent(time += 16, key, down));
        void Tap(KeyCode key)
        {
            Key(key, true);
            Key(key, false);
        }
        void Expect(CursorKind kind, string what)
        {
            var cursor = input.Cursor(pointerOverWindow: false);
            TestHarness.Assert(cursor.Kind == kind, $"{what}: expected {kind}, got {cursor.Kind}");
        }

        TestHarness.Assert(input.Cursor(false) == CursorState.Arrow, "before the first mouse event: the arrow");
        MoveTo(100, 100);
        Expect(CursorKind.Normal, "nothing selected, open ground");
        MoveTo(40, 40);
        Expect(CursorKind.Select, "nothing selected, an own unit");

        input.Selection.Replace([ranger.Id]);
        MoveTo(100, 100);
        Expect(CursorKind.Move, "a unit selected, open ground");
        var commands = arena.Sink.Issued.Count;

        // Modifier keys change the cursor at once: the pointer does not move between these checks.
        Key(KeyCode.LeftAlt, true);
        Expect(CursorKind.Waypoint, "Alt held (BEGIN_WAYPOINTS)");
        Key(KeyCode.LeftAlt, false);
        Expect(CursorKind.Move, "Alt released");
        Key(KeyCode.LeftControl, true);
        Expect(CursorKind.ForceAttackGround, "Ctrl held over ground (BEGIN_FORCEATTACK)");
        Key(KeyCode.LeftControl, false);
        Expect(CursorKind.Move, "Ctrl released");

        MoveTo(120, 40);
        Expect(CursorKind.OutRange, "an enemy beyond weapon range");
        MoveTo(60, 60);
        Expect(CursorKind.Select, "an own unit that is no container");
        Key(KeyCode.RightControl, true);
        Expect(CursorKind.ForceAttackObj, "Ctrl held over an own unit");
        Key(KeyCode.RightControl, false);
        Key(KeyCode.LeftShift, true);
        Expect(CursorKind.Select, "Shift held over an own unit (prefer selection)");
        Key(KeyCode.LeftShift, false);
        Send(new FocusLostEvent(time += 16));
        Expect(CursorKind.Select, "a focus loss releases the modifiers");

        MoveTo(100, 100);
        TestHarness.Assert(input.Cursor(pointerOverWindow: true) == CursorState.Arrow, "over a window: the arrow, whatever is under it in the world");

        // Command modes of the control bar.
        Tap(KeyCode.A);
        TestHarness.Assert(input.Commands.Mode == CommandModeKind.AttackMove, "A enters attack-move");
        Expect(CursorKind.AttackMove, "attack-move mode");
        Tap(KeyCode.Escape);
        Expect(CursorKind.Move, "Escape leaves the mode");
        input.DispatchMeta(MetaAction.Guard, world, view);
        MoveTo(101, 100);
        Expect(CursorKind.Target, "guard mode (a button without a cursor name: the cross)");
        Tap(KeyCode.Escape);

        input.Selection.Replace([barracks.Id]);
        MoveTo(170, 90);
        Expect(CursorKind.SetRallyPoint, "a production building selected, open ground");
        Tap(KeyCode.R);
        TestHarness.Assert(input.Commands.Mode == CommandModeKind.ButtonTarget, "the rally point button waits for its target");
        Expect(CursorKind.SetRallyPoint, "the button's CursorName");
        Tap(KeyCode.Escape);

        input.Selection.Replace([dozer.Id]);
        MoveTo(60, 110);
        Tap(KeyCode.P);
        TestHarness.Assert(input.Commands.Mode == CommandModeKind.Placement, "P enters placement");
        Expect(CursorKind.Build, "a legal building place");
        MoveTo(150, 110);
        Expect(CursorKind.InvalidBuild, "on top of another building");
        Tap(KeyCode.Escape);

        // The world changes under a resting pointer: the hint is re-read without an input event.
        input.Selection.Replace([ranger.Id]);
        MoveTo(100, 100);
        input.Selection.Clear();
        Expect(CursorKind.Move, "the hint is from the last event");
        input.RefreshHint(world, view);
        Expect(CursorKind.Normal, "after a refresh it follows the selection");
        input.Selection.Replace([ranger.Id]);
        input.RefreshHint(world, view);

        // Scrolling: the directional cursor wins over everything else, with the direction of the scroll.
        input.UpdateCamera(view, CnCNew.Tests.TestTime.FrameSecondsF);
        Expect(CursorKind.Move, "the pointer in the middle of the view does not scroll");
        Send(new MouseMoveEvent(time += 16, new Vector2(799, 300)));
        input.UpdateCamera(view, CnCNew.Tests.TestTime.FrameSecondsF);
        var right = input.Cursor(false);
        TestHarness.Assert(right.Kind == CursorKind.Scroll && CursorSet.DirectionIndex(right.Direction, 8) == 0, $"the right edge scrolls right ({right})");
        TestHarness.Assert(input.Cursor(pointerOverWindow: true).Kind == CursorKind.Scroll, "also over a window");
        Send(new MouseMoveEvent(time += 16, new Vector2(400, 599)));
        input.UpdateCamera(view, CnCNew.Tests.TestTime.FrameSecondsF);
        TestHarness.Assert(CursorSet.DirectionIndex(input.Cursor(false).Direction, 8) == 2, "the bottom edge: down");
        Send(new MouseMoveEvent(time += 16, new Vector2(0, 0)));
        input.UpdateCamera(view, CnCNew.Tests.TestTime.FrameSecondsF);
        TestHarness.Assert(CursorSet.DirectionIndex(input.Cursor(false).Direction, 8) == 5, "the top left corner: up and left");
        MoveTo(100, 100);
        Key(KeyCode.Up, true);
        input.UpdateCamera(view, CnCNew.Tests.TestTime.FrameSecondsF);
        var up = input.Cursor(false);
        TestHarness.Assert(up.Kind == CursorKind.Scroll && CursorSet.DirectionIndex(up.Direction, 8) == 6, "the up arrow key scrolls up");
        Key(KeyCode.Up, false);
        input.UpdateCamera(view, CnCNew.Tests.TestTime.FrameSecondsF);
        Expect(CursorKind.Move, "scrolling over: the context cursor is back");
        Send(new MouseButtonEvent(time += 16, MouseButton.Right, true, InputTests.Screen(100, 100)));
        Send(new MouseMoveEvent(time += 16, InputTests.Screen(140, 100)));
        input.UpdateCamera(view, CnCNew.Tests.TestTime.FrameSecondsF);
        TestHarness.Assert(input.Cursor(false).Kind == CursorKind.Scroll && CursorSet.DirectionIndex(input.Cursor(false).Direction, 8) == 0, "a right drag scrolls");
        Send(new MouseButtonEvent(time += 16, MouseButton.Right, false, InputTests.Screen(140, 100)));
        input.UpdateCamera(view, CnCNew.Tests.TestTime.FrameSecondsF);

        TestHarness.Assert(arena.Sink.Issued.Count == commands, "looking at cursors issued no command");
    }

    // ------------------------------------------------------------------------------------------------ markers

    private static void WaypointMarkers()
    {
        var arena = new InputTests.Arena();
        arena.Spawn("Tank", Me, 40, 60);
        arena.Spawn("Ranger", Me, 80, 80);
        arena.Step(2);
        var world = arena.World;
        var view = new InputTests.TopDownView();
        var selection = new SelectionModel(Me);
        var controller = new CommandModeController(Me, arena.Sink, selection);
        var (tank, ranger) = (arena.Find("Tank", Me, 40, 60), arena.Find("Ranger", Me, 80, 80));
        selection.Replace([tank.Id]);

        controller.OnMeta(world, MetaAction.BeginWaypoints);
        var before = arena.Sink.Issued.Count;
        foreach (var station in new[] { InputTests.Screen(30, 30), InputTests.Screen(30, 10), InputTests.Screen(60, 10) })
            TestHarness.Assert(controller.IssueContext(world, view, station), "an Alt-click on the ground adds a waypoint");
        TestHarness.Assert(arena.Sink.Issued.Count == before + 1, "only the first station is ordered at once; the others wait in the standing order");

        var paths = OrderMarkers.WaypointPaths(world, selection, controller.Orders, controller.WaypointMode);
        TestHarness.Assert(paths is [var path] && path.Unit == tank.Id && path.Start == new Vector2(40, 60)
                           && path.Stations.SequenceEqual([new Vector2(30, 30), new Vector2(30, 10), new Vector2(60, 10)]),
            "three waypoints: three markers, the segment from the unit plus two between the stations");
        TestHarness.Assert(controller.DrainFeedback().Count(feedback => feedback.Kind == FeedbackKind.WaypointMarker) == 3, "each Alt-click also leaves a marker hint");

        selection.Replace([ranger.Id]);
        TestHarness.Assert(OrderMarkers.WaypointPaths(world, selection, controller.Orders, true).IsEmpty, "the path of a unit that is not selected is not shown");
        selection.Replace([tank.Id, ranger.Id]);
        TestHarness.Assert(OrderMarkers.WaypointPaths(world, selection, controller.Orders, true).Length == 1, "only units with a waypoint order have a path");
        controller.OnMeta(world, MetaAction.EndWaypoints);
        TestHarness.Assert(OrderMarkers.WaypointPaths(world, selection, controller.Orders, controller.WaypointMode).IsEmpty, "Alt released: no path (the original draws it in waypoint mode only)");

        // The unit arrives at the first station: that marker goes, the path now starts with the second.
        var remaining = 3;
        for (var tick = 0; tick < 400 && remaining == 3; tick++)
        {
            controller.Update(world);
            arena.Step();
            remaining = OrderMarkers.WaypointPaths(world, selection, controller.Orders, true)[0].Stations.Length;
        }
        var afterFirst = OrderMarkers.WaypointPaths(world, selection, controller.Orders, true)[0];
        TestHarness.Assert(afterFirst.Stations.SequenceEqual([new Vector2(30, 10), new Vector2(60, 10)]), "the reached station is gone");
        TestHarness.Assert(Vector2.Distance(afterFirst.Start, new Vector2(30, 30)) < 3f, $"the path starts where the unit stands ({afterFirst.Start})");

        // Another Alt-click appends; a plain order replaces the path.
        controller.OnMeta(world, MetaAction.BeginWaypoints);
        controller.IssueContext(world, view, InputTests.Screen(90, 10));
        TestHarness.Assert(OrderMarkers.WaypointPaths(world, selection, controller.Orders, true)[0].Stations.Length == 3, "a further Alt-click appends a station");
        controller.OnMeta(world, MetaAction.EndWaypoints);
        selection.Replace([tank.Id]);
        TestHarness.Assert(controller.IssueContext(world, view, InputTests.Screen(100, 100)), "a plain move order");
        TestHarness.Assert(OrderMarkers.WaypointPaths(world, selection, controller.Orders, true).IsEmpty, "another order removes the whole path");

        // The last station is reached: the order ends and nothing is left to draw.
        controller.OnMeta(world, MetaAction.BeginWaypoints);
        controller.IssueContext(world, view, InputTests.Screen(100, 90));
        controller.OnMeta(world, MetaAction.EndWaypoints);
        var gone = false;
        for (var tick = 0; tick < 400 && !gone; tick++)
        {
            controller.Update(world);
            arena.Step();
            gone = OrderMarkers.WaypointPaths(world, selection, controller.Orders, true).IsEmpty;
        }
        TestHarness.Assert(gone, "the path disappears when the unit has arrived at its last station");
        TestHarness.Assert(world.RejectionCounts.Sum() == 0, "every command of the path was accepted");
    }

    private static void OrderHints()
    {
        var arena = new InputTests.Arena();
        arena.Spawn("Ranger", Me, 40, 40);
        arena.Spawn("Barracks", Me, 150, 110);
        arena.Spawn("Ranger", Enemy, 60, 40);
        arena.Step(2);
        var world = arena.World;
        var view = new InputTests.TopDownView();
        var selection = new SelectionModel(Me);
        var controller = new CommandModeController(Me, arena.Sink, selection);
        var (ranger, barracks, enemy) = (arena.Find("Ranger", Me, 40, 40), arena.Find("Barracks", Me, 150, 110), arena.Find("Ranger", Enemy, 60, 40));
        var book = new OrderHintBook();
        void Collect(long now)
        {
            foreach (var feedback in controller.DrainFeedback()) book.Observe(feedback, now);
        }

        selection.Replace([ranger.Id]);
        controller.IssueContext(world, view, InputTests.Screen(90, 20));
        Collect(10_000);
        TestHarness.Assert(book.Active(10_000) is [{ Kind: OrderHintKind.Move, LifetimeMilliseconds: 1333 } move] && move.Position == new Vector3(90, 20, 0) && move.Age(10_000) == 0f,
            "a move order leaves a marker at the destination that lives 40 frames at 30 per second");
        TestHarness.Assert(Math.Abs(book.Active(10_666)[0].Age(10_666) - 0.5f) < 0.01f, "it is half faded at half its life");
        controller.IssueContext(world, view, InputTests.Screen(60, 40));
        Collect(11_000);
        TestHarness.Assert(book.Active(11_000) is [{ Kind: OrderHintKind.Move }, { Kind: OrderHintKind.Attack } attack] && attack.Target == enemy.Id, "an attack order marks its target");
        TestHarness.Assert(book.Active(11_332).Length == 2 && book.Active(11_333) is [{ Kind: OrderHintKind.Attack }], "the move marker expires 1333 ms after the order");
        TestHarness.Assert(book.Active(12_333).IsEmpty, "and the attack marker after its own");

        selection.Replace([barracks.Id]);
        controller.IssueContext(world, view, InputTests.Screen(170, 90));
        controller.IssueContext(world, view, InputTests.Screen(150, 110));
        Collect(20_000);
        TestHarness.Assert(book.Active(20_000) is [{ Kind: OrderHintKind.RallyPoint }], "a rally point is marked; a click that orders nothing is not");
        TestHarness.Assert(!book.Observe(new CommandFeedback(FeedbackKind.InvalidOrder, Vector3.Zero, EntityId.Invalid, 0, 40), 20_000)
                           && !book.Observe(new CommandFeedback(FeedbackKind.Rejected, Vector3.Zero, EntityId.Invalid, 0, 40), 20_000)
                           && !book.Observe(new CommandFeedback(FeedbackKind.Unsupported, Vector3.Zero, EntityId.Invalid, 0, 40), 20_000), "refusals are not markers");
        book.Clear();
        for (var index = 0; index < OrderHintBook.Capacity + 5; index++) book.Observe(new CommandFeedback(FeedbackKind.MoveMarker, new Vector3(index, 0, 0), EntityId.Invalid, 0, 40), 30_000);
        var kept = book.Active(30_000);
        TestHarness.Assert(kept.Length == OrderHintBook.Capacity && kept[0].Position.X == 5 && kept[^1].Position.X == OrderHintBook.Capacity + 4, "the book is a ring of 256: the oldest hint makes room");
    }

    // ------------------------------------------------------------------------------------------------ real data

    /// <summary>The cursors <see cref="CommandModeController"/> and <see cref="GameInputController"/> can choose by themselves; command buttons add the names they carry.</summary>
    private static readonly CursorKind[] InputLayerCursors =
    [
        CursorKind.Normal, CursorKind.Arrow, CursorKind.Scroll, CursorKind.Select, CursorKind.Move, CursorKind.Waypoint, CursorKind.AttackMove, CursorKind.Target,
        CursorKind.AttackObj, CursorKind.OutRange, CursorKind.ForceAttackObj, CursorKind.ForceAttackGround, CursorKind.GenericInvalid, CursorKind.ResumeConstruction,
        CursorKind.Dock, CursorKind.DoRepair, CursorKind.GetRepaired, CursorKind.EnterFriendly, CursorKind.SetRallyPoint,
    ];

    private static void RealData(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var catalog = MouseCursorCatalog.FromGraph(graph);
        TestHarness.Assert(catalog.Issues.IsEmpty, $"every MouseCursor block of the retail Mouse.ini is understood: {string.Join("; ", catalog.Issues)}");
        TestHarness.Assert(catalog.WithoutDefinition.SequenceEqual([CursorKind.Build, CursorKind.InvalidBuild]),
            $"only the two placement cursors have no block (the original shows no cursor while placing): {string.Join(", ", catalog.WithoutDefinition)}");

        var set = CursorSet.Load(catalog, content.Exists, content.OpenRead);
        Console.WriteLine($"  cursors: {set.Report()}");
        TestHarness.Assert(set.MissingFiles.IsEmpty && set.UndecodableFiles.IsEmpty && set.Unusable.IsEmpty, "no cursor file is missing or undecodable");
        TestHarness.Assert(set.InvertedPixels == 0, "no retail cursor asks to invert the screen");

        var buttons = CommandButtonCatalog.Build(graph);
        var named = buttons.Buttons.SelectMany(button => new[] { button.CursorName, button.InvalidCursorName }).Where(name => name.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToList();
        // One retail name is not an engine cursor (the daisy cutter buttons): the original shows the cross for it, and so does the input layer (CursorKind.Target).
        var unknownUsers = buttons.Buttons.Where(button => buttons.UnknownCursorNames.Contains(button.CursorName, StringComparer.OrdinalIgnoreCase)
                                                           || buttons.UnknownCursorNames.Contains(button.InvalidCursorName, StringComparer.OrdinalIgnoreCase)).Select(button => button.Name).ToList();
        Console.WriteLine($"  cursors: button cursor names that are no engine cursor [{string.Join(", ", buttons.UnknownCursorNames)}] on {unknownUsers.Count} buttons ({string.Join(", ", unknownUsers)}): shown as the cross (Target), as in the original");
        TestHarness.Assert(buttons.UnknownCursorNames.SequenceEqual(["DaisyCutter"]), $"the only button cursor name that is no engine cursor is DaisyCutter: {string.Join(", ", buttons.UnknownCursorNames)}");
        var fromButtons = named.Where(name => CursorNames.TryParse(name, out _)).Select(name => CursorNames.TryParse(name, out var kind) ? kind : default).Distinct().Order().ToList();

        var needed = InputLayerCursors.Concat(fromButtons).Distinct().Order().ToList();
        var withoutPicture = new List<string>();
        var frames = 0;
        foreach (var kind in needed)
        {
            TestHarness.Assert(catalog.TryGet(kind, out var definition), $"{kind} is defined in Mouse.ini");
            for (var direction = 0; direction < definition.Directions; direction++)
            {
                var angle = MathF.Tau * direction / definition.Directions;
                var state = new CursorState(kind, definition.Directions > 1 ? new Vector2(MathF.Cos(angle), MathF.Sin(angle)) : Vector2.Zero);
                TestHarness.Assert(CursorSet.DirectionIndex(state.Direction, definition.Directions) == direction, $"{kind} direction {direction} maps to its own file");
                if (!set.Resolve(state, 0, out var frame)) withoutPicture.Add($"{kind}/{direction}");
                else
                {
                    frames += frame.Cursor.Animation.Frames.Length;
                    TestHarness.Assert(frame.Image is { Width: > 0, Height: > 0 } image && image.RgbaPixels.Length == image.Width * image.Height * 4 && image.HotspotX < image.Width && image.HotspotY < image.Height,
                        $"{kind} decodes to a picture with its hot spot inside");
                    TestHarness.Assert(frame.Cursor.Animation.Frames.All(picture => picture.RgbaPixels.Where((_, index) => index % 4 == 3).Any(alpha => alpha != 0)), $"{kind}: no frame is fully transparent");
                }
            }
        }
        TestHarness.Assert(withoutPicture.Count == 0, $"every cursor the input layer can show has a picture: {string.Join(", ", withoutPicture)}");
        Console.WriteLine($"  cursors: {needed.Count} kinds the input layer and {named.Count} button cursor names can show ({InputLayerCursors.Length} own, {fromButtons.Count} from buttons), all with a picture; {frames} frames behind them");

        // The table of docs/PRESENTATION-INPUT.md: context -> Mouse.ini block -> cursor file.
        (CursorKind Kind, string Texture)[] expected =
        [
            (CursorKind.Arrow, "SCCPointer"), (CursorKind.Select, "SCCSelect"), (CursorKind.Move, "SCCMove"), (CursorKind.Waypoint, "SCCWaypoint"), (CursorKind.AttackMove, "SCCAttMov"),
            (CursorKind.AttackObj, "SCCAttack"), (CursorKind.OutRange, "SCCAttack"), (CursorKind.ForceAttackObj, "SCCAttack"), (CursorKind.GenericInvalid, "SCCNoAction"),
            (CursorKind.EnterFriendly, "SCCEnter"), (CursorKind.DoRepair, "SCCRepair"), (CursorKind.SetRallyPoint, "SCCRallyPnt"), (CursorKind.CaptureBuilding, "SCCHostile2"),
            (CursorKind.ResumeConstruction, "SCCResumeC"), (CursorKind.Scroll, "SCCScroll"),
        ];
        foreach (var (kind, texture) in expected)
            TestHarness.Assert(catalog.TryGet(kind, out var definition) && definition.Texture.Equals(texture, StringComparison.OrdinalIgnoreCase), $"{kind} uses {texture}");
        TestHarness.Assert(catalog.TryGet(CursorKind.Scroll, out var scroll) && scroll.Directions == 8, "the scroll cursor has eight directions");
    }

    private sealed class RealSource(ContentFileSystem content) : IDefinitionSource
    {
        public IEnumerable<string> EnumerateIniFiles() => content.EnumerateFiles().Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
        public Stream OpenRead(string virtualPath) => content.OpenRead(virtualPath);
        public bool Exists(string virtualPath) => content.Exists(virtualPath);
    }
}
