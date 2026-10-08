using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Game.Spectator;
using CnCNew.Presentation.Input;
using CnCNew.Presentation.Vision;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Tests;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Settings;

namespace CnCNew.Game.Tests;

/// <summary>
/// The spectator of a skirmish between computer players (docs/SPECTATOR-MODE.md): the observer panels are a deterministic function of the match, nothing the viewer does
/// reaches the simulation, the fog switch, the far camera, and the income per game minute at two tick rates. The camera of a computer player and the zoom limit have their
/// synthetic tests in <c>CnCNew.Presentation.Tests</c>.
/// </summary>
internal static class SpectatorTests
{
    private const int Seed = 7;
    private static readonly Vector2 Viewport = new(1280, 720);

    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(SpectatorTests));
        harness.Run("Income per minute is per game minute at 30, 60 and 120 ticks per second", IncomeWindowRates);
        harness.Run("Every button of the observer HUD has a key and every key an action", KeysAndButtons);

        var index = Array.IndexOf(args, "--content-dir");
        var contentDir = index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
        if (contentDir is null)
        {
            Console.WriteLine("SKIP SpectatorTests with a match (pass --content-dir <dir> to run against real data)");
            return harness.Failures;
        }

        var data = GameData.Load(contentDir, step => Console.WriteLine($"  loading: {step}"));
        harness.Run("Two runs of the same match give the same panels, cameras and report, and the panels have data", () => PanelsAreDeterministic(data));
        harness.Run("Nothing the spectator does reaches the simulation: the match is the headless match of the same seed", () => SpectatorWritesNothing(data));
        harness.Run("The fog switch shows everything, then one player's view, then the other's", () => FogSwitch(data));
        harness.Run("The spectator zooms out to the whole map, a player's limit is lower, an own limit holds", () => FarCamera(data));
        harness.Run("The observer HUD draws its panels, buttons and the radar at several resolutions, and a click on a button acts", () => DrawList(data));
        harness.Run("Income of the last game minute equals the deposits of the last game minute at 30 Hz", () => IncomeMatchesDeposits(data));
        var data60 = GameData.Load(contentDir, step => Console.WriteLine($"  loading (60 Hz): {step}"), modelGeometry: false, rate: SimulationRate.Hz60);
        harness.Run("Income of the last game minute equals the deposits of the last game minute at 60 Hz", () => IncomeMatchesDeposits(data60));
        return harness.Failures;
    }

    private sealed record Watch(LiveMatch Match, MatchController Hud, SpectatorController Spectator);

    private static Watch Start(GameData data, SpectatorOptions? options = null)
    {
        var map = data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase));
        var setup = new MatchSetup(map, "FactionAmerica", ["FactionChina"], SkirmishMatchOptions.Standard) { Seed = Seed, Spectator = true };
        var match = LiveMatch.Create(data, setup, Viewport);
        var hud = new MatchController(match, GameSettings.Default);
        return new Watch(match, hud, new SpectatorController(match, hud, options ?? new SpectatorOptions(Seed: Seed)));
    }

    /// <summary>Advances tick by tick (one tick per step at speed 1) until the world has reached <paramref name="tick"/>.</summary>
    private static void RunTo(Watch watch, long tick, Action<int>? each = null)
    {
        var step = TestTime.TickSeconds(watch.Match.World);
        for (var iteration = 0; watch.Match.Tick < tick; iteration++)
        {
            each?.Invoke(iteration);
            watch.Match.Advance(step);
            watch.Spectator.Update(step);
            TestHarness.Assert(iteration < tick * 4 + 10_000, "the match stopped advancing");
        }
    }

    private static void IncomeWindowRates()
    {
        long[] results = new long[3];
        var rates = new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 };
        for (var index = 0; index < rates.Length; index++)
        {
            var perSecond = rates[index].TicksPerSecond;
            var window = new IncomeWindow(perSecond);
            // Deposits of 300 credits at game seconds 5, 20, 20.5, 59 and 64.
            foreach (var halfSeconds in new[] { 10, 40, 41, 118, 128 }) window.Add((long)halfSeconds * perSecond / 2, 300);
            TestHarness.Assert(window.PerMinute(30L * perSecond) == 900, $"three deposits in the first half minute at {perSecond} ticks per second: {window.PerMinute(30L * perSecond)}");
            TestHarness.Assert(window.PerMinute(64L * perSecond) == 1500, $"at 64 s the deposit of second 5 is still within the minute: {window.PerMinute(64L * perSecond)}");
            TestHarness.Assert(window.PerMinute(65L * perSecond) == 1200, $"at 65 s it has left: {window.PerMinute(65L * perSecond)}");
            TestHarness.Assert(window.PerMinute(125L * perSecond) == 0, "nothing after a minute without deposits");
            results[index] = window.PerMinute(70L * perSecond);
        }
        TestHarness.Assert(results[0] == results[1] && results[1] == results[2] && results[0] == 1200, $"the same deposits at the same game time give the same income at every rate: {string.Join("/", results)}");
    }

    private static void KeysAndButtons()
    {
        foreach (var action in Enum.GetValues<SpectatorAction>())
        {
            var hint = SpectatorController.KeyHint(action);
            TestHarness.Assert(hint.Length > 0, $"{action} has a key");
        }
        TestHarness.Assert(SpectatorController.ActionOf(KeyCode.P) == SpectatorAction.TabProduction && SpectatorController.ActionOf(KeyCode.O) == SpectatorAction.Overview
            && SpectatorController.ActionOf(KeyCode.F1) == SpectatorAction.FollowFirst && SpectatorController.ActionOf(KeyCode.D4) == SpectatorAction.Speed4, "the documented keys");
        TestHarness.Assert(SpectatorController.ActionOf(KeyCode.A) is null && SpectatorController.ActionOf(KeyCode.Delete) is null, "a player's hot keys do nothing");
        TestHarness.Assert(SpectatorController.KeyHint(SpectatorAction.Speed8) == "8" && SpectatorController.KeyHint(SpectatorAction.Quit) == "Esc", "hints read like the key caps");
    }

    private static void PanelsAreDeterministic(GameData data)
    {
        var sawProduction = new HashSet<int>();
        var sawArmy = new HashSet<int>();
        string[] Play(bool observe)
        {
            var watch = Start(data);
            RunTo(watch, TestTime.Ticks(watch.Match.World, 150), iteration =>
            {
                if (!observe || iteration % 90 != 0) return;
                foreach (var player in watch.Spectator.Panels.Players)
                {
                    if (!player.Production.IsEmpty) sawProduction.Add(player.Player.Value);
                    if (player.ArmyUnits > 0) sawArmy.Add(player.Player.Value);
                }
            });
            var spectator = watch.Spectator;
            if (observe)
            {
                var panels = spectator.Panels;
                TestHarness.Assert(panels.Players.Length == 2 && panels.GameSeconds >= 149, $"two players after 150 s: {panels.GameSeconds} s");
                foreach (var player in panels.Players)
                {
                    TestHarness.Assert(player.Structures > 0 && player.Units > 0 && !player.Defeated, $"player {player.Player.Value} has structures and units");
                    TestHarness.Assert(player.PowerProduction > 0, $"player {player.Player.Value} makes power");
                    TestHarness.Assert(spectator.CommandsByPlayer.GetValueOrDefault(player.Player.Value) > 0, $"player {player.Player.Value} gave orders");
                }
                TestHarness.Assert(sawProduction.Count == 2, "both players had something in production");
                TestHarness.Assert(sawArmy.Count >= 1, "somebody had an army");
                TestHarness.Assert(panels.Players.Sum(player => player.Collected) > 0 && panels.Players.Any(player => player.IncomePerMinute > 0 && player.Harvesters > 0), "supplies were collected");
                foreach (var camera in spectator.Cameras)
                    TestHarness.Assert(camera.Switches >= 3 && camera.OrdersSeen > 0, $"the camera of player {camera.Player.Value} found points of interest: {camera.Switches}");
                Console.WriteLine("  " + string.Join("\n  ", spectator.Report().Take(6)));
            }
            return [..spectator.Report()];
        }

        var first = Play(true);
        var second = Play(false);
        TestHarness.Assert(first.SequenceEqual(second), "the report of the second run differs: " + first.Zip(second).FirstOrDefault(pair => pair.First != pair.Second));
    }

    private static void SpectatorWritesNothing(GameData data)
    {
        const long seconds = 60;
        // The headless match of the same seed: a session of computer slots only, as SimTool ai-match starts it.
        var watch = Start(data);
        var spectator = watch.Spectator;
        var slots = watch.Match.Players.Select(player => new SkirmishSlot(data.Factions.First(value => value.Faction.Name == player.Faction))).ToImmutableArray();
        var options = SkirmishMatchOptions.Standard with { AiSeed = (uint)Seed, WorldSeed = (uint)Seed ^ 0xC0FFEE06 };
        var session = SkirmishMatchSession.Start(options, data.Templates, data.Fingerprint, watch.Match.Terrain, watch.Match.Map.Objects, slots);
        var end = TestTime.Ticks(session.World, seconds);
        while (session.World.Tick < end) session.Step();

        var time = 0L;
        RunTo(watch, end, iteration =>
        {
            time += 33;
            void Send(InputEvent input) => spectator.Process(input);
            switch (iteration)
            {
                // What a player would do to give orders: clicks and drags on the ground, hot keys, the radar.
                case 40: Send(new MouseMoveEvent(time, new Vector2(640, 300))); Send(new MouseButtonEvent(time, MouseButton.Left, true, new Vector2(640, 300))); break;
                case 42: Send(new MouseButtonEvent(time, MouseButton.Left, false, new Vector2(700, 340))); break;
                case 50: Send(new MouseButtonEvent(time, MouseButton.Right, true, new Vector2(500, 300))); break;
                case 52: Send(new MouseButtonEvent(time, MouseButton.Right, false, new Vector2(500, 300))); break;
                case 60: foreach (var key in new[] { KeyCode.A, KeyCode.S, KeyCode.Q, KeyCode.W, KeyCode.Delete, KeyCode.D3, KeyCode.Enter }) { Send(new KeyEvent(time, key, true)); Send(new KeyEvent(time, key, false)); } break;
                case 70: Send(new MouseButtonEvent(time, MouseButton.Left, true, new Vector2(100, 620))); Send(new MouseButtonEvent(time, MouseButton.Left, false, new Vector2(100, 620))); break;
                case 80: Send(new MouseWheelEvent(time, -6, new Vector2(640, 300))); break;
                case 90: Send(new KeyEvent(time, KeyCode.Right, true)); break;
                case 120: Send(new KeyEvent(time, KeyCode.Right, false)); break;
                // Every action of the observer HUD except leaving; the speed ends at 1 so the last tick is reached exactly.
                case 200: foreach (var action in Enum.GetValues<SpectatorAction>().Where(value => value is not (SpectatorAction.Quit or SpectatorAction.Pause))) spectator.Do(action); break;
                case 210: spectator.Do(SpectatorAction.Pause); break;
                case 230: spectator.Do(SpectatorAction.Pause); spectator.Do(SpectatorAction.Speed1); break;
                // The command sink of the match itself.
                case 300:
                    var unit = watch.Match.World.EntitiesOwnedBy(new PlayerId(1)).AsSpan()[0].Id;
                    var answer = watch.Match.Commands.Issue((sequence, tick) => new SetMoveTargetCommand(new PlayerId(1), sequence, tick, unit, Fixed.Zero, Fixed.Zero));
                    TestHarness.Assert(!answer.Accepted && answer.Reason == CommandRejection.NotPermitted, "an order through the match's command sink is refused");
                    break;
            }
        });
        TestHarness.Assert(watch.Match.Tick == end, $"the match stands at tick {end}: {watch.Match.Tick}");
        TestHarness.Assert(watch.Match.SpectatorSink is { Refused: >= 1 }, "the refused order was counted");
        TestHarness.Assert(!watch.Hud.Input.Selection.Selected.Any(), "the spectator selects nothing");
        var recorded = watch.Match.Session.RecordedCommands;
        TestHarness.Assert(recorded.Count == session.RecordedCommands.Count, $"the watched match recorded {recorded.Count} commands, the headless one {session.RecordedCommands.Count}");
        TestHarness.Assert(recorded.All(command => command.Player.IsSystem || watch.Match.Players.Any(player => player.IsComputer && player.Id == command.Player)), "every recorded command is the system's or a computer player's");
        TestHarness.Assert(watch.Match.Players.All(player => player.IsComputer), "nobody plays");
        TestHarness.Assert(watch.Match.World.StateHash == session.World.StateHash, $"state hash {watch.Match.World.StateHash:X16}, headless {session.World.StateHash:X16}");
        TestHarness.Assert(spectator.ActionsTaken >= 17 && spectator.HighestReached > spectator.PlayerMaxHeight, "the inputs did act on the view");
    }

    private static void FogSwitch(GameData data)
    {
        var watch = Start(data);
        var match = watch.Match;
        RunTo(watch, TestTime.Ticks(match.World, 30));
        int Owned(int player) => match.Frame.Drawables.Count(drawable => drawable.Entity.IsValid && drawable.Owner.Value == player);
        int Cells(FogCellState state)
        {
            var count = 0;
            for (var y = 0; y < match.Fog.CellsY; y++)
                for (var x = 0; x < match.Fog.CellsX; x++)
                    if (match.Fog.StateAt(x, y) == state) count++;
            return count;
        }

        var all = match.Fog.CellsX * match.Fog.CellsY;
        TestHarness.Assert(watch.Spectator.FogViewer is null && match.FogViewer is null, "the spectator starts without fog");
        TestHarness.Assert(Cells(FogCellState.Visible) == all, "every cell is clear");
        var (one, two) = (Owned(1), Owned(2));
        TestHarness.Assert(one > 0 && two > 0, $"both bases are drawn: {one} and {two} objects");

        watch.Spectator.Do(SpectatorAction.CycleFog);
        TestHarness.Assert(watch.Spectator.FogViewer == new PlayerId(1) && match.FogViewer == new PlayerId(1), "then the view of player 1");
        TestHarness.Assert(Owned(1) == one && Owned(2) == 0, $"player 1 sees its own objects and nothing of player 2 after 30 s: {Owned(1)} and {Owned(2)}");
        TestHarness.Assert(Cells(FogCellState.Unexplored) > all / 2 && Cells(FogCellState.Visible) > 0, "most of the map is unexplored for player 1");
        RunTo(watch, match.Tick + TestTime.Ticks(match.World, 2));
        TestHarness.Assert(Owned(1) > 0 && Owned(2) == 0, "and it stays that way while the match runs");

        watch.Spectator.Do(SpectatorAction.CycleFog);
        TestHarness.Assert(watch.Spectator.FogViewer == new PlayerId(2), "then the view of player 2");
        TestHarness.Assert(Owned(2) > 0 && Owned(1) == 0, "player 2 sees its own objects only");

        watch.Spectator.Do(SpectatorAction.CycleFog);
        TestHarness.Assert(watch.Spectator.FogViewer is null && Cells(FogCellState.Visible) == all && Owned(1) > 0 && Owned(2) > 0, "and everything again");
        RunTo(watch, match.Tick + TestTime.Ticks(match.World, 2));
        TestHarness.Assert(Cells(FogCellState.Visible) == all, "the clear view is not overwritten by a player's vision");
    }

    private static void FarCamera(GameData data)
    {
        var watch = Start(data);
        var (match, spectator) = (watch.Match, watch.Spectator);
        var camera = match.Camera;
        var player = spectator.PlayerMaxHeight;
        TestHarness.Assert(Math.Abs(camera.HeightAboveGround - player) < 0.5f && !spectator.IsFarZoom, "the match starts at a player's zoom");
        TestHarness.Assert(spectator.SpectatorMaxHeight > 3 * player && Math.Abs(spectator.SpectatorMaxHeight - spectator.OverviewHeight) < 0.5f, $"without an option the limit is the overview height: {spectator.SpectatorMaxHeight}");

        var before = camera.Pivot;
        spectator.Do(SpectatorAction.Overview);
        TestHarness.Assert(spectator.Overview && spectator.IsFarZoom && Math.Abs(camera.HeightAboveGround - spectator.OverviewHeight) < 0.5f, "the overview is at the overview height");
        var max = match.Heights.BoundaryMax(0);
        foreach (var corner in new[] { Vector2.Zero, max, new Vector2(0, max.Y), new Vector2(max.X, 0) })
            TestHarness.Assert(camera.TryWorldToScreen(new Vector3(corner, camera.GroundLevel), out var screen) && screen.X >= 0 && screen.Y >= 0 && screen.X <= Viewport.X && screen.Y <= Viewport.Y,
                $"map corner {corner} is on screen");
        // The height field reaches past the playable boundary; what must be in view is every chunk of the playable map.
        var visible = camera.VisibleChunks(match.TerrainModel).ToHashSet();
        var playable = Enumerable.Range(0, match.TerrainModel.Chunks.Length).Where(chunk =>
        {
            var center = (match.TerrainModel.Chunks[chunk].Bounds.Min + match.TerrainModel.Chunks[chunk].Bounds.Max) / 2;
            return center.X >= 0 && center.Y >= 0 && center.X <= max.X && center.Y <= max.Y;
        }).ToArray();
        TestHarness.Assert(playable.Length > 0 && playable.All(visible.Contains), $"every terrain chunk of the playable map is in view: {playable.Count(visible.Contains)} of {playable.Length} ({visible.Count} of all {match.TerrainModel.Chunks.Length})");
        spectator.Do(SpectatorAction.Overview);
        TestHarness.Assert(!spectator.Overview && Math.Abs(camera.HeightAboveGround - player) < 0.5f && Vector2.Distance(camera.Pivot, before) < 1f, "leaving the overview restores the view");

        spectator.Process(new MouseWheelEvent(0, -100, Viewport / 2));
        TestHarness.Assert(Math.Abs(camera.HeightAboveGround - spectator.SpectatorMaxHeight) < 0.5f, "the wheel stops at the spectator's limit");
        spectator.Process(new MouseWheelEvent(0, 400, Viewport / 2));
        TestHarness.Assert(Math.Abs(camera.HeightAboveGround - camera.Settings.MinHeightAboveGround) < 0.5f, "and at the player's lower limit");

        // Following a simulated camera, and the arrow keys taking the view back.
        RunTo(watch, TestTime.Ticks(match.World, 20));
        spectator.Do(SpectatorAction.FollowSecond);
        RunTo(watch, match.Tick + TestTime.Ticks(match.World, 2));
        TestHarness.Assert(spectator.Followed == 1 && Vector2.Distance(camera.Pivot, spectator.Cameras[1].Position) < 250f,
            $"the view follows the camera of player 2: {camera.Pivot} against {spectator.Cameras[1].Position}");
        spectator.Process(new KeyEvent(0, KeyCode.Left, true));
        RunTo(watch, match.Tick + 3);
        spectator.Process(new KeyEvent(0, KeyCode.Left, false));
        TestHarness.Assert(spectator.Followed is null, "scrolling takes the view back");

        var limited = Start(data, new SpectatorOptions(MaxZoomHeight: 500, Seed: Seed));
        limited.Spectator.Process(new MouseWheelEvent(0, -100, Viewport / 2));
        TestHarness.Assert(Math.Abs(limited.Match.Camera.HeightAboveGround - 500) < 0.5f, "an own limit holds for the wheel");
        limited.Spectator.Do(SpectatorAction.Overview);
        TestHarness.Assert(Math.Abs(limited.Match.Camera.HeightAboveGround - 500) < 0.5f, "and for the overview");
    }

    private static void DrawList(GameData data)
    {
        var watch = Start(data);
        RunTo(watch, TestTime.Ticks(watch.Match.World, 60));
        foreach (var (width, height) in new[] { (1280, 720), (1920, 1080), (1024, 768) })
        {
            watch.Match.Camera.SetViewportSize(new Vector2(width, height));
            foreach (var tab in new[] { SpectatorAction.TabProduction, SpectatorAction.TabUnits, SpectatorAction.TabLosses, SpectatorAction.TabEconomy })
            {
                watch.Spectator.Do(tab);
                var list = watch.Spectator.BuildDrawList(width, height);
                var texts = list.Commands.OfType<UiTextDraw>().Select(command => command.Text).ToArray();
                TestHarness.Assert(list.Commands.OfType<UiCustomDraw>().Count(command => command.Callback == "W3DLeftHUDDraw") == 1, "the radar is left to the host");
                TestHarness.Assert(texts.Any(text => text.Contains("America", StringComparison.Ordinal)) && texts.Any(text => text.Contains("China", StringComparison.Ordinal)), $"both players are named at {width}x{height}");
                TestHarness.Assert(texts.Contains("Money") && texts.Contains("Income/min") && texts.Contains("Army value") && texts.Contains("Follow AI 1") && texts.Contains("[F1]"), "top bar and buttons");
                TestHarness.Assert(list.Commands.OfType<UiLineDraw>().Count() >= 8, "the simulated cameras are framed on the radar");
                foreach (var command in list.Commands.OfType<UiFillRect>())
                    TestHarness.Assert(command.Rect.X >= 0 && command.Rect.Right <= width && command.Rect.Bottom <= height, $"{command} lies on the {width}x{height} screen");
            }
            var economy = watch.Spectator.BuildDrawList(width, height);
            TestHarness.Assert(watch.Spectator.Tab == SpectatorTab.Economy && economy.Commands.OfType<UiTextDraw>().Any(command => command.Text == "Supply carriers"), "the economy tab lists the supply carriers");
        }

        watch.Match.Camera.SetViewportSize(Viewport);
        watch.Spectator.Do(SpectatorAction.TabUnits);
        var images = watch.Spectator.BuildDrawList(1280, 720).Commands.OfType<UiImageDraw>().Count();
        TestHarness.Assert(images > 0, "units with a command button are shown with its picture");
        Console.WriteLine($"  units tab at 60 s: {images} cameo pictures");
        var button = watch.Spectator.ButtonRect(SpectatorAction.Speed4, 1280, 720) ?? throw new InvalidOperationException("no 4x button");
        watch.Spectator.Process(new MouseButtonEvent(0, MouseButton.Left, true, new Vector2(button.CenterX, button.CenterY)));
        watch.Spectator.Process(new MouseButtonEvent(0, MouseButton.Left, false, new Vector2(button.CenterX, button.CenterY)));
        TestHarness.Assert(watch.Match.Speed == 4, "a click on the 4x button sets the speed");
        var pause = watch.Spectator.ButtonRect(SpectatorAction.Pause, 1280, 720) ?? throw new InvalidOperationException("no pause button");
        watch.Spectator.Process(new MouseButtonEvent(0, MouseButton.Left, true, new Vector2(pause.CenterX, pause.CenterY)));
        TestHarness.Assert(watch.Match.Paused, "a click on the pause button pauses");
    }

    private static void IncomeMatchesDeposits(GameData data)
    {
        var watch = Start(data);
        var world = watch.Match.World;
        var perSecond = world.Time.TicksPerSecond;
        var deposits = new List<(long Tick, int Player, long Amount)>();
        watch.Match.TickStepped += result =>
        {
            foreach (var ev in result.Events)
                if (ev.Kind == SimEventKind.SupplyDeposited && world.FindEntity(ev.Entity) is { } center) deposits.Add((ev.Tick, center.Owner.Value, ev.Value));
        };
        const long seconds = 170;
        RunTo(watch, seconds * perSecond);
        var panels = watch.Spectator.Report().Count > 0 ? watch.Spectator.Panels : throw new InvalidOperationException("no report");
        TestHarness.Assert(panels.GameSeconds == seconds, $"the panels count game seconds at {perSecond} ticks per second: {panels.GameSeconds}");
        TestHarness.Assert(deposits.Count > 0, "supplies were deposited within 170 s");
        var now = world.Tick / perSecond;
        foreach (var player in panels.Players)
        {
            var total = deposits.Where(item => item.Player == player.Player.Value).Sum(item => item.Amount);
            var lastMinute = deposits.Where(item => item.Player == player.Player.Value && item.Tick / perSecond > now - IncomeWindow.WindowSeconds).Sum(item => item.Amount);
            TestHarness.Assert(player.Collected == total, $"player {player.Player.Value} collected {total}: {player.Collected}");
            TestHarness.Assert(player.IncomePerMinute == lastMinute, $"player {player.Player.Value} at {perSecond} ticks per second: income {player.IncomePerMinute}, deposits of the last game minute {lastMinute} (of {total} in total)");
        }
        Console.WriteLine($"  {perSecond} ticks per second, {seconds} s: " + string.Join(", ", panels.Players.Select(player => $"player {player.Player.Value} +{player.IncomePerMinute}/min of {player.Collected}")));
    }
}
