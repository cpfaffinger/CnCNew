using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Presentation.Input;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Settings;

namespace CnCNew.Game.Tests;

/// <summary>What the owner's playtest of 3 November 2026 saw on the HUD of a skirmish, each pinned on real data.</summary>
internal static class PlaytestHudTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(PlaytestHudTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP PlaytestHudTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("The percent of a building under construction is not cut off: its clip holds the whole line", () => ConstructionPercent(data));
        harness.Run("The general's window keeps its original art in the modern skin; an owned science in colour, one without points grey", () => GeneralsWindow(data));
        harness.Run("A general's power on the bar at the side shows its recharge as a clock over its cameo", () => PowerRecharge(data));
        harness.Run("At a skirmish's start what the map placed stands darkened across the explored map, the enemy's base does not", () => RevealGhosts(data));
        harness.Run("A building the player holds shows who is inside on the bar, lets one out and empties; a neutral one shows its places disabled", () => GarrisonInventory(data));
        harness.Run("The idle worker button is enabled while a worker stands idle and selects the idle workers one after another", () => IdleWorkerButton(data));
        harness.Run("A drone in production or carried greys every drone button of a Humvee, alone and in a selection of two, and its order stands in the queue", () => DroneButtons(data));
        harness.Run("Over the own bunker the infantry's pointer is enter, over a building the enemy holds it is attack", () => BuildingCursors(data));
        harness.Run("A ranger with the capture upgrade shows capture over a neutral tech building and takes it by a click, attacks an enemy's and enters a civilian one", () => CaptureCursor(data));
        harness.Run("A selected unit of a control group shows the group's number at its health bar, one that is not in a group or not selected does not", () => ControlGroupNumber(data));
        harness.Run("A selected supply drop zone shows its countdown on the bar: the share of the delay, the minutes and seconds to the next plane, and a sell button", () => OclTimerWindow(data));
        harness.Run("A click on a ready power of the bar presses it, enters its mode and changes the pointer over the ground", () => PowerClick(data));
        return harness.Failures;
    }

    /// <summary>
    /// The owner's playtest of 4 November 2026 (finding 12): the supply drop zone "shows no countdown". The original's bar switches to <c>CB_CONTEXT_OCL_TIMER</c> for an object with an
    /// <c>OCLUpdate</c>: <c>OCLTimerWindow</c> with a progress bar of <c>getCountdownPercent</c>, the remaining time as "minutes:seconds" and the sell button.
    /// </summary>
    private static void OclTimerWindow(GameData data)
    {
        var scenario = new DemoScenario("hud-ocl-timer", "hud-ocl-timer", "hud-ocl-timer", 30 * 60 * 5,
            [new ScenarioUnit("zone", "AmericaSupplyDropZone", 1, 0, 0, 0f), new ScenarioUnit("tank", "AmericaTankCrusader", 1, 0, 200, 0f)], [], 100f, 40f)
        {
            HumanFaction = "FactionAmerica",
            OpponentFaction = "FactionChina",
        };
        var live = LiveScenario.Create(data, scenario, new Vector2(1280, 720));
        var controller = new MatchController(live, GameSettings.Default) { StarBlinks = false };
        void Seconds(int seconds)
        {
            for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond * seconds; frame++)
            {
                live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
                controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
            }
        }
        string[] Clock() => [..controller.BuildDrawList(1280, 720).Commands.OfType<UiTextDraw>().Select(text => text.Text).Where(text => text.Any(char.IsDigit) && text.Contains(':'))];
        const string Window = "ControlBar.wnd:OCLTimerWindow";
        var (zone, tank) = (live.Tags["zone"], live.Tags["tank"]);
        Seconds(31);
        controller.Input.Selection.Replace([tank]);
        Seconds(1);
        controller.BuildDrawList(1280, 720);
        TestHarness.Assert(!controller.IsWindowShown(Window), "a tank shows no timer");
        controller.Input.Selection.Replace([zone]);
        Seconds(1);
        var text = string.Join("|", Clock());
        TestHarness.Assert(controller.IsWindowShown(Window), $"the selected drop zone shows the timer window ({controller.CommandBar.Context}, {controller.CommandBar.CommandSet}, sel {controller.Input.Selection.Selected.Count()})");
        TestHarness.Assert(controller.IsWindowShown("ControlBar.wnd:OCLTimerSellButton"), "with its sell button");
        var progress = controller.WindowProgress("ControlBar.wnd:OCLTimerProgressBar");
        TestHarness.Assert(progress is >= 24 and <= 28, $"about a quarter of the two minutes have passed ({progress} %)");
        TestHarness.Assert(text.Contains("1:27") || text.Contains("1:28") || text.Contains("1:26"), $"about a minute and a half remains ({text})");
        Seconds(5);
        TestHarness.Assert(string.Join("|", Clock()) != text, "the clock counts down");
        Seconds(100);
        controller.BuildDrawList(1280, 720);
        var later = controller.WindowProgress("ControlBar.wnd:OCLTimerProgressBar");
        TestHarness.Assert(later is >= 0 and < 20, $"after the plane came the delay begins again ({later} %)");
    }

    /// <summary>
    /// The owner's playtest of 7 November 2026: units pulled together with Ctrl and a digit showed nothing at their health bar. <c>Drawable::drawUIText</c> draws the group's number
    /// (<c>NUMBER:n</c>, Arial 10, white with a shadow) a fifth of the bar's width to the left of the bar and ten pixels above it - for a selected, locally controlled object only.
    /// </summary>
    private static void ControlGroupNumber(GameData data)
    {
        var scenario = new DemoScenario("hud-group-number", "hud-group-number", "hud-group-number", 900,
            [
                new ScenarioUnit("tank", "ChinaTankBattleMaster", 1, 0, 0, 0f),
                new ScenarioUnit("other", "ChinaTankBattleMaster", 1, 0, 90, 0f),
                new ScenarioUnit("enemy", "AmericaTankCrusader", 2, 0, 180, 0f),
            ], [], 100f, 40f)
        {
            HumanFaction = "FactionChina",
            OpponentFaction = "FactionAmerica",
        };
        var live = LiveScenario.Create(data, scenario, new Vector2(1280, 720));
        var controller = new MatchController(live, GameSettings.Default) { StarBlinks = false };
        void Frame()
        {
            live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
            controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        }
        long time = 10_000;
        void Press(KeyCode key, bool control)
        {
            if (control) controller.Process(new KeyEvent(time += 16, KeyCode.LeftControl, true));
            controller.Process(new KeyEvent(time += 16, key, true));
            controller.Process(new KeyEvent(time += 16, key, false));
            if (control) controller.Process(new KeyEvent(time += 16, KeyCode.LeftControl, false));
        }
        var (tank, other, enemy) = (live.Tags["tank"], live.Tags["other"], live.Tags["enemy"]);
        for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond / 2; frame++) Frame();
        string[] Numbers() => [..controller.BuildDrawList(1280, 720).Commands.OfType<UiTextDraw>().Where(text => text.Font.Name == "Arial" && text.Font.Size == 10 && text.Text.Length == 1 && char.IsDigit(text.Text[0])).Select(text => text.Text)];

        controller.Input.Selection.Replace([tank, other]);
        Frame();
        TestHarness.Assert(Numbers().Length == 0, "without a control group no number is drawn");

        // Ctrl+3 makes the selection group 3 (the lowest index of Ctrl+0..9 is the digit's).
        Press(KeyCode.D3, control: true);
        TestHarness.Assert(controller.Input.Selection.GroupOf(tank) == 3 && controller.Input.Selection.GroupOf(other) == 3 && controller.Input.Selection.GroupOf(enemy) == -1, "Ctrl+3 put both tanks into group 3");
        Frame();
        var list = controller.BuildDrawList(1280, 720);
        var numbers = list.Commands.OfType<UiTextDraw>().Where(text => text.Font.Name == "Arial" && text.Font.Size == 10 && text.Text == "3").ToArray();
        TestHarness.Assert(numbers.Length == 2, $"both selected tanks show the 3 ({numbers.Length})");
        TestHarness.Assert(numbers.All(text => text is { Alignment: UiTextAlignment.Left, Color.R: 255, Color.G: 255, Color.B: 255, ShadowColor.A: 255 }), "white, left aligned, with a black shadow");

        // Where: the bar of each tank is the one drawn above its ring; the number stands at -20 % of the bar's width and 10 pixels above the bar's top.
        var plates = list.Commands.OfType<UiBorderRect>().Where(border => border.Rect.Height == 7).ToArray();
        TestHarness.Assert(plates.Length == 2, $"two bars ({plates.Length})");
        foreach (var number in numbers)
        {
            var plate = plates.OrderBy(border => MathF.Abs(border.Rect.Y - 10 - number.Y)).First();
            var width = plate.Rect.Width - 2;
            TestHarness.Assert(Math.Abs(number.X - (plate.Rect.X + 1 - width * 0.2)) <= 1.5 && number.Y == plate.Rect.Y + 1 - 10, $"the number at {number.X},{number.Y} stands at the bar's left end less a fifth of its width ({plate.Rect}) and ten pixels above");
        }

        // Only a selected unit of the group shows it; one that is not in a group shows nothing; an enemy never does.
        controller.Input.Selection.Replace([tank]);
        Frame();
        TestHarness.Assert(Numbers().SequenceEqual(["3"]), $"one selected tank of the group shows one number ({string.Join(",", Numbers())})");
        controller.Input.Selection.Replace([enemy]);
        Frame();
        TestHarness.Assert(Numbers().Length == 0, "a selected enemy shows no number");
        controller.Input.Selection.Replace([]);
        Frame();
        TestHarness.Assert(Numbers().Length == 0, "a unit of the group that is not selected shows no number (drawsAnyUIText asks isSelected)");

        // A new group takes its members out of the old one: Ctrl+7 on one tank, and the number follows.
        controller.Input.Selection.Replace([tank]);
        Press(KeyCode.D7, control: true);
        Frame();
        TestHarness.Assert(controller.Input.Selection.GroupOf(tank) == 7 && controller.Input.Selection.GroupOf(other) == 3 && Numbers().SequenceEqual(["7"]), $"the tank is in group 7 now ({string.Join(",", Numbers())})");
    }

    private static void RevealGhosts(GameData data)
    {
        var mapPath = data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase));
        var live = LiveMatch.Create(data, new MatchSetup(mapPath, "FactionAmerica", ["FactionChina"], CnCNew.Simulation.Testing.SkirmishMatchOptions.Standard), new Vector2(1280, 720));
        for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond * 2; frame++) live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
        var drawables = live.ObjectFrame.Drawables;
        var ghosts = drawables.Where(drawable => drawable.IsGhost).ToArray();
        TestHarness.Assert(ghosts.Any(ghost => ghost.Visual.TemplateName.Contains("Supply", StringComparison.OrdinalIgnoreCase)),
            $"the supply docks across the map stand as ghosts ({string.Join(", ", ghosts.Select(ghost => ghost.Visual.TemplateName).Distinct())})");
        TestHarness.Assert(ghosts.All(ghost => !live.World.IsPlayableSide(ghost.Owner)), "no ghost of a player's building: the original places them after the reveal");
        TestHarness.Assert(drawables.Where(drawable => !drawable.IsGhost && drawable.Entity.IsValid).All(drawable => live.World.FindEntity(drawable.Entity) is { } shown && live.World.IsPositionVisibleTo(live.Human, shown.X, shown.Y)),
            "everything else that is drawn is in sight");
    }

    private static void GarrisonInventory(GameData data)
    {
        var scenario = new DemoScenario("hud-inventory", "hud-inventory", "hud-inventory", 900,
            [
                new ScenarioUnit("guard", "ChinaInfantryRedguard", 1, 60, 0, 0f),
                new ScenarioUnit("house", "GermanTownHouse1", 0, 130, 0, 0f),
                new ScenarioUnit("empty", "GermanTownHouse1", 0, 130, 120, 0f),
            ],
            [new ScenarioOrder(5, "guard", ScenarioOrderKind.Enter, "house")], 100f, 40f)
        {
            HumanFaction = "FactionChina",
            OpponentFaction = "FactionAmerica",
        };
        var live = LiveScenario.Create(data, scenario, new Vector2(1280, 720));
        var controller = new MatchController(live, GameSettings.Default) { StarBlinks = false };
        void Frame()
        {
            live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
            controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        }
        var (guard, house) = (live.Tags["guard"], live.Tags["house"]);
        for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond * 20 && !live.World.FindEntity(guard)!.ContainedBy.IsValid; frame++) Frame();
        TestHarness.Assert(live.World.FindEntity(guard)!.ContainedBy == house, "the red guard is inside the house");

        controller.Input.Selection.Replace([live.Tags["empty"]]);
        Frame();
        controller.BuildDrawList(1280, 720);
        TestHarness.Assert(controller.CommandBar is { Context: CommandBarContext.Command } empty && empty[1] is { Availability: ButtonAvailability.Restricted } && empty[12] is { Availability: ButtonAvailability.Restricted },
            $"a neutral building shows its places, disabled ({controller.CommandBar.Context})");

        controller.Input.Selection.Replace([house]);
        Frame();
        controller.BuildDrawList(1280, 720);
        var bar = controller.CommandBar;
        TestHarness.Assert(bar[1] is { Availability: ButtonAvailability.Available } first && first.Rider == guard && first.Button.Name == "Command_StructureExit", $"place 1 is the red guard ({bar[1]?.Button.Name}, {bar[1]?.Rider})");
        TestHarness.Assert(bar[2] is { Availability: ButtonAvailability.Restricted } && bar[11]?.Button.Name == "Command_Stop" && bar[12] is { Availability: ButtonAvailability.Available } evacuate && evacuate.Button.Name == "Command_Evacuate",
            "a free place is disabled, Stop and Evacuate stand at the end");
        TestHarness.Assert(controller.BuildDrawList(1280, 720).Commands.OfType<UiImageDraw>().Any(image => image.Destination == controller.WindowRect("ControlBar.wnd:ButtonCommand01") && live.Data.Visuals.TryGet("ChinaInfantryRedguard", out var cameo) && image.Image == cameo.ButtonImage),
            "the place shows the red guard's cameo");
        var rect = controller.WindowRect("ControlBar.wnd:ButtonCommand01")!.Value;
        var at = new Vector2(rect.CenterX, rect.CenterY);
        controller.Process(new MouseMoveEvent(50_000, at));
        controller.Process(new MouseButtonEvent(50_010, MouseButton.Left, true, at));
        controller.Process(new MouseButtonEvent(50_060, MouseButton.Left, false, at));
        for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond * 5 && live.World.FindEntity(guard)!.ContainedBy.IsValid; frame++) Frame();
        TestHarness.Assert(!live.World.FindEntity(guard)!.ContainedBy.IsValid, "a click on the place lets the red guard out");
    }

    private static void IdleWorkerButton(GameData data)
    {
        var scenario = new DemoScenario("hud-idle-worker", "hud-idle-worker", "hud-idle-worker", 2400,
            [
                new ScenarioUnit("first", "ChinaVehicleDozer", 1, 0, 0, 0f),
                new ScenarioUnit("second", "ChinaVehicleDozer", 1, 0, 120, 0f),
                new ScenarioUnit("tank", "ChinaTankBattleMaster", 1, -120, 0, 0f),
            ],
            [
                new ScenarioOrder(1, "first", ScenarioOrderKind.MoveTo, X: 400, Y: 0),
                new ScenarioOrder(1500, "first", ScenarioOrderKind.MoveTo, X: -400, Y: 0),
                new ScenarioOrder(1500, "second", ScenarioOrderKind.MoveTo, X: -400, Y: 120),
            ], 100f, 40f)
        {
            HumanFaction = "FactionChina",
            OpponentFaction = "FactionAmerica",
        };
        var live = LiveScenario.Create(data, scenario, new Vector2(1280, 720));
        var controller = new MatchController(live, GameSettings.Default) { StarBlinks = false };
        void Frame()
        {
            live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
            controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        }
        const string Button = "ControlBar.wnd:ButtonIdleWorker";
        var (first, second, tank) = (live.Tags["first"], live.Tags["second"], live.Tags["tank"]);
        for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond / 2; frame++) Frame();
        controller.BuildDrawList(1280, 720);
        TestHarness.Assert(controller.Input.Selection.IdleWorkerCount == 1 && controller.IsWindowEnabled(Button), $"one dozer drives, the other stands: one idle worker, the button enabled ({controller.Input.Selection.IdleWorkerCount})");

        controller.Input.Selection.Replace([tank]);
        Frame();
        var rect = controller.WindowRect(Button)!.Value;
        var at = new Vector2(rect.CenterX, rect.CenterY);
        void Click(long time)
        {
            controller.Process(new MouseMoveEvent(time, at));
            controller.Process(new MouseButtonEvent(time + 10, MouseButton.Left, true, at));
            controller.Process(new MouseButtonEvent(time + 60, MouseButton.Left, false, at));
        }
        Click(50_000);
        TestHarness.Assert(controller.Input.Selection.Selected.SequenceEqual([second]), $"a click on the button selects the idle dozer ({string.Join(",", controller.Input.Selection.Selected)})");

        for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond * 30 && controller.Input.Selection.IdleWorkerCount < 2; frame++) Frame();
        TestHarness.Assert(controller.Input.Selection.IdleWorkerCount == 2, "the driving dozer is idle once it has arrived");
        Click(60_000);
        TestHarness.Assert(controller.Input.Selection.Selected.SequenceEqual([first]), $"the next click selects the worker that became idle after the selected one ({string.Join(",", controller.Input.Selection.Selected)})");
        Click(70_000);
        TestHarness.Assert(controller.Input.Selection.Selected.SequenceEqual([second]), "and the one after the last is the first again");

        for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond * 70 && live.World.Tick < live.World.Time.OriginalFrames(1500) + 4; frame++) Frame();
        controller.BuildDrawList(1280, 720);
        TestHarness.Assert(controller.Input.Selection.IdleWorkerCount == 0 && !controller.IsWindowEnabled(Button), "with every worker busy the button is disabled");
    }

    private static void BuildingCursors(GameData data)
    {
        var scenario = new DemoScenario("hud-cursors", "hud-cursors", "hud-cursors", 900,
            [
                new ScenarioUnit("bunker", "ChinaBunker", 1, 0, 0, 0f),
                new ScenarioUnit("guard", "ChinaInfantryRedguard", 1, -60, 0, 0f),
                new ScenarioUnit("house", "GermanTownHouse1", 0, 130, 0, 0f),
                new ScenarioUnit("ranger", "AmericaInfantryRanger", 2, 130, 70, 0f),
            ],
            [new ScenarioOrder(5, "ranger", ScenarioOrderKind.Enter, "house")], 40f, 0f)
        {
            HumanFaction = "FactionChina",
            OpponentFaction = "FactionAmerica",
        };
        var live = LiveScenario.Create(data, scenario, new Vector2(1280, 720));
        var controller = new MatchController(live, GameSettings.Default) { StarBlinks = false };
        var house = live.Tags["house"];
        for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond * 20 && live.World.FindEntity(house)!.Owner.Value != 2; frame++)
        {
            live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
            controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        }
        TestHarness.Assert(live.World.FindEntity(house)!.Owner.Value == 2, "the ranger holds the house, and the house is his player's");
        controller.Input.Selection.Replace([live.Tags["guard"]]);
        CursorKind Over(string tag)
        {
            var entity = live.World.FindEntity(live.Tags[tag])!;
            var point = new System.Numerics.Vector3(CnCNew.Presentation.FixedConvert.ToSingle(entity.X), CnCNew.Presentation.FixedConvert.ToSingle(entity.Y), CnCNew.Presentation.FixedConvert.ToSingle(entity.Z) + 5f);
            TestHarness.Assert(live.Camera.TryWorldToScreen(point, out var screen), $"the {tag} is on the screen");
            controller.Process(new MouseMoveEvent(5_000, screen));
            controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
            return controller.Cursor.Kind;
        }
        var bunker = Over("bunker");
        TestHarness.Assert(bunker == CursorKind.EnterFriendly, $"over the own bunker: enter ({bunker})");
        var held = Over("house");
        TestHarness.Assert(held is CursorKind.AttackObj or CursorKind.OutRange, $"over the house the enemy holds: attack ({held})");
    }

    private static void DroneButtons(GameData data)
    {
        var scenario = new DemoScenario("hud-drones", "hud-drones", "hud-drones", 4500,
            [
                new ScenarioUnit("first", "AmericaVehicleHumvee", 1, 0, 0, 0f),
                new ScenarioUnit("second", "AmericaVehicleHumvee", 1, 0, 60, 0f),
            ],
            [], 0f, 20f)
        {
            HumanFaction = "FactionAmerica",
            OpponentFaction = "FactionChina",
            StartMoney = 10000,
        };
        var live = LiveScenario.Create(data, scenario, new Vector2(1280, 720));
        var controller = new MatchController(live, GameSettings.Default) { StarBlinks = false };
        void Frame()
        {
            live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
            controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        }
        for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond; frame++) Frame();
        var (first, second) = (live.Tags["first"], live.Tags["second"]);
        CommandBarButton? Drone(string name) => controller.CommandBar.Slots.FirstOrDefault(button => button?.Button.Name == name);
        string States() => string.Join(", ", new[] { "Battle", "Scout", "Hellfire" }.Select(kind => $"{kind} {Drone($"Command_ConstructAmericaVehicle{kind}Drone")?.Availability}"));
        controller.Input.Selection.Replace([first, second]);
        Frame();
        TestHarness.Assert(controller.CommandBar.Context == CommandBarContext.MultiSelect && Drone("Command_ConstructAmericaVehicleScoutDrone") is { IsEnabled: true }, $"two Humvees offer the drones ({States()})");

        // The scout drone for the first Humvee alone.
        controller.Input.Selection.Replace([first]);
        Frame();
        controller.BuildDrawList(1280, 720);
        var single = States();
        var rect = controller.WindowRect($"ControlBar.wnd:ButtonCommand{Drone("Command_ConstructAmericaVehicleScoutDrone")!.Slot:00}")!.Value;
        var at = new Vector2(rect.CenterX, rect.CenterY);
        controller.Process(new MouseMoveEvent(9_000, at));
        controller.Process(new MouseButtonEvent(9_010, MouseButton.Left, true, at));
        controller.Process(new MouseButtonEvent(9_060, MouseButton.Left, false, at));
        for (var frame = 0; frame < 4; frame++) Frame();
        var said = string.Join(",", controller.Input.Commands.DrainFeedback().Select(item => item.Kind + ":" + item.Rejection + ":" + item.Detail));
        var humvee = live.World.FindEntity(first)!;
        var upgrades = string.Join(",", live.World.Upgrades.Upgrades.Where(upgrade => humvee.CompletedUpgrades.Has(upgrade.Index)).Select(upgrade => upgrade.Name));
        TestHarness.Assert(controller.CommandBar.Queue.Length == 1, $"the drone's order stands in the Humvee's queue ({controller.CommandBar.Queue.Length}; feedback {said}; completed {upgrades}; drones {live.World.Entities.Count(entity => entity.Template.Name.Contains("Drone"))}; bar {single} at {at}; money {live.World.MoneyOf(live.Human)})");
        TestHarness.Assert(new[] { "Battle", "Scout", "Hellfire" }.All(kind => Drone($"Command_ConstructAmericaVehicle{kind}Drone") is { IsEnabled: false }), $"with a drone in production every drone button is grey ({States()})");

        // Both Humvees selected: the second can still take a drone, so the buttons stay; once the second has one too, none does.
        controller.Input.Selection.Replace([first, second]);
        Frame();
        TestHarness.Assert(Drone("Command_ConstructAmericaVehicleBattleDrone") is { IsEnabled: true }, $"the second Humvee can still take a drone ({States()})");
        controller.Input.Selection.Replace([second]);
        Frame();
        rect = controller.WindowRect($"ControlBar.wnd:ButtonCommand{Drone("Command_ConstructAmericaVehicleBattleDrone")!.Slot:00}")!.Value;
        at = new Vector2(rect.CenterX, rect.CenterY);
        controller.Process(new MouseMoveEvent(10_000, at));
        controller.Process(new MouseButtonEvent(10_010, MouseButton.Left, true, at));
        controller.Process(new MouseButtonEvent(10_060, MouseButton.Left, false, at));
        for (var frame = 0; frame < 4; frame++) Frame();
        controller.Input.Selection.Replace([first, second]);
        Frame();
        TestHarness.Assert(new[] { "Battle", "Scout", "Hellfire" }.All(kind => Drone($"Command_ConstructAmericaVehicle{kind}Drone") is null or { IsEnabled: false }),
            $"with a drone in production at both, no drone button of the selection is enabled ({States()})");

        // Once the drones are there, the other drones conflict (ConflictsWith) and stay grey.
        for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond * 60 && controller.CommandBar.Slots.Any(button => button is not null) && live.World.FindEntity(first)!.CompletedUpgrades.Equals(default(CnCNew.Simulation.Templates.UpgradeMask)); frame++) Frame();
        for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond * 2; frame++) Frame();
        controller.Input.Selection.Replace([first]);
        Frame();
        TestHarness.Assert(controller.CommandBar.Queue.IsEmpty && new[] { "Battle", "Scout", "Hellfire" }.All(kind => Drone($"Command_ConstructAmericaVehicle{kind}Drone") is { IsEnabled: false }),
            $"a Humvee with its scout drone offers no other drone ({States()}, queue {controller.CommandBar.Queue.Length})");
    }

    private static void CaptureCursor(GameData data)
    {
        var scenario = new DemoScenario("hud-capture", "hud-capture", "hud-capture", 4500,
            [
                new ScenarioUnit("ranger", "AmericaInfantryRanger", 1, 0, 0, 0f),
                new ScenarioUnit("derrick", "TechOilDerrick", 0, 110, -40, 0f),
                new ScenarioUnit("barracks", "ChinaBarracks", 2, 110, 90, 0f),
                new ScenarioUnit("house", "GermanTownHouse1", 0, -110, 0, 0f),
            ],
            [], 0f, 20f)
        {
            HumanFaction = "FactionAmerica",
            OpponentFaction = "FactionChina",
            Upgrades = ["Upgrade_InfantryCaptureBuilding"],
            PowersReady = true,
        };
        var live = LiveScenario.Create(data, scenario, new Vector2(1280, 720));
        var controller = new MatchController(live, GameSettings.Default) { StarBlinks = false };
        void Frame()
        {
            live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
            controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        }
        // canCaptureBuilding: "Special not ready" - the ability recharges once the upgrade has unpaused it, and only then is a click a capture.
        bool Ready() => live.World.Check(new CnCNew.Simulation.Commands.CaptureCommand(live.Human, 0, 0, live.Tags["ranger"], live.Tags["derrick"])).Accepted;
        for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond * 90 && !Ready(); frame++) Frame();
        TestHarness.Assert(Ready(), "the ranger's capture is ready");
        controller.Input.Selection.Replace([live.Tags["ranger"]]);
        Vector2 ScreenOf(string tag)
        {
            var entity = live.World.FindEntity(live.Tags[tag])!;
            var point = new System.Numerics.Vector3(CnCNew.Presentation.FixedConvert.ToSingle(entity.X), CnCNew.Presentation.FixedConvert.ToSingle(entity.Y), CnCNew.Presentation.FixedConvert.ToSingle(entity.Z) + 5f);
            TestHarness.Assert(live.Camera.TryWorldToScreen(point, out var screen), $"the {tag} is on the screen");
            return screen;
        }
        CursorKind Over(string tag)
        {
            controller.Process(new MouseMoveEvent(5_000, ScreenOf(tag)));
            controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
            return controller.Cursor.Kind;
        }
        var derrick = Over("derrick");
        var check = live.World.Check(new CnCNew.Simulation.Commands.CaptureCommand(live.Human, 0, 0, live.Tags["ranger"], live.Tags["derrick"]));
        TestHarness.Assert(derrick == CursorKind.CaptureBuilding, $"over the neutral oil derrick: capture ({derrick}; the capture's pre-check: {check.Reason}, power {live.World.HasCapturePower(live.World.FindEntity(live.Tags["ranger"])!)})");
        var barracks = Over("barracks");
        TestHarness.Assert(barracks is CursorKind.AttackObj or CursorKind.OutRange, $"over the enemy's barracks: attack ({barracks})");
        var house = Over("house");
        TestHarness.Assert(house == CursorKind.EnterFriendly, $"over the civilian house: enter ({house})");

        var at = ScreenOf("derrick");
        controller.Process(new MouseMoveEvent(6_000, at));
        controller.Process(new MouseButtonEvent(6_010, MouseButton.Right, true, at));
        controller.Process(new MouseButtonEvent(6_060, MouseButton.Right, false, at));
        var target = live.Tags["derrick"];
        var ranger = live.World.FindEntity(live.Tags["ranger"])!;
        var (fromX, fromY) = (ranger.X, ranger.Y);
        var feedback = string.Join(",", controller.Input.Commands.DrainFeedback().Select(item => item.Kind + ":" + item.Rejection));
        for (var frame = 0; frame < CnCNew.Tests.TestTime.FramesPerSecond * 60 && live.World.FindEntity(target)!.Owner != live.Human; frame++) Frame();
        TestHarness.Assert(live.World.FindEntity(target)!.Owner == live.Human,
            $"a click with the capture cursor makes the ranger walk over and take the derrick (feedback {feedback}; the ranger from ({fromX}, {fromY}) to ({ranger.X}, {ranger.Y}))");
    }

    private static void PowerClick(GameData data)
    {
        var scenario = new DemoScenario("hud-click", "hud-click", "hud-click", 600, [new ScenarioUnit("centre", "AmericaCommandCenter", 1, -260, -80, 0f)], [], 0f, 0f)
        {
            HumanFaction = "FactionAmerica",
            OpponentFaction = "FactionChina",
            PowersReady = true,
            Sciences = ["SCIENCE_Paradrop1"],
        };
        var live = LiveScenario.Create(data, scenario, new Vector2(1280, 720));
        var controller = new MatchController(live, GameSettings.Default) { StarBlinks = false };
        void Frame()
        {
            live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
            controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        }
        for (var frame = 0; frame < 3; frame++) Frame();
        controller.BuildDrawList(1280, 720);
        var number = controller.Powers.IndexOf(controller.Powers.First(button => button.Button.Name.Contains("Paradrop", StringComparison.Ordinal))) + 1;
        var rect = controller.PowerButtonRect(number);
        var on = new Vector2(rect.X + rect.Width / 2f, rect.Y + rect.Height / 2f);
        controller.Process(new MouseMoveEvent(1_000, on));
        controller.Process(new MouseButtonEvent(1_010, MouseButton.Left, true, on));
        Frame();
        var pressed = controller.BuildDrawList(1280, 720).Commands.OfType<UiImageDraw>().Any(image => image.Image == "Cameo_push" && image.Destination == rect);
        TestHarness.Assert(pressed, "the held button wears Cameo_push");
        controller.Process(new MouseButtonEvent(1_090, MouseButton.Left, false, on));
        Frame();
        TestHarness.Assert(controller.Input.Commands.Mode == CnCNew.Presentation.Input.CommandModeKind.ButtonTarget, $"the drop waits for its place after the release ({controller.Input.Commands.Mode})");
        var hilited = controller.BuildDrawList(1280, 720).Commands.OfType<UiImageDraw>().Any(image => image.Image == "Cameo_hilited" && image.Destination == rect);
        TestHarness.Assert(hilited, "released under the pointer it wears Cameo_hilited");
        controller.Process(new MouseMoveEvent(1_200, new Vector2(640, 360)));
        Frame();
        TestHarness.Assert(controller.Input.Commands.Mode == CnCNew.Presentation.Input.CommandModeKind.ButtonTarget && controller.Cursor.Kind != CnCNew.Presentation.Input.CursorKind.Normal && controller.Cursor.Kind != CnCNew.Presentation.Input.CursorKind.Arrow,
            $"over the ground the pointer is the power's ({controller.Cursor.Kind}, {controller.Input.Commands.Cursor})");
    }

    private static void PowerRecharge(GameData data)
    {
        var scenario = new DemoScenario("hud-recharge", "hud-recharge", "hud-recharge", 600, [new ScenarioUnit("centre", "AmericaCommandCenter", 1, -260, -80, 0f)],
            [new ScenarioOrder(10, "centre", ScenarioOrderKind.UsePower, X: 40f, Y: 0f, Template: "SuperweaponParadropAmerica")], 0f, 0f)
        {
            HumanFaction = "FactionAmerica",
            OpponentFaction = "FactionChina",
            PowersReady = true,
            Sciences = ["SCIENCE_Paradrop1"],
        };
        var live = LiveScenario.Create(data, scenario, new Vector2(1280, 720));
        var controller = new MatchController(live, GameSettings.Default) { StarBlinks = false };
        for (var frame = 0; frame < 60; frame++)
        {
            live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
            controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        }
        var list = controller.BuildDrawList(1280, 720).Commands;
        var a10 = controller.Powers.FirstOrDefault(button => button.Button.Name.Contains("Paradrop", StringComparison.Ordinal));
        TestHarness.Assert(a10 is { Availability: CnCNew.Presentation.Input.ButtonAvailability.NotReady, ReadyFraction: < 1f }, $"the drop recharges after it was used ({a10?.Availability}, {a10?.ReadyFraction}, {a10?.Reason})");
        var rect = controller.PowerButtonRect(controller.Powers.IndexOf(a10!) + 1);
        var clock = list.OfType<UiFillRect>().Where(fill => fill.Color.A == 160 && rect.Contains(fill.Rect.X, fill.Rect.Y)).ToArray();
        var covered = clock.Sum(fill => fill.Rect.Width * fill.Rect.Height);
        var share = covered / (double)(rect.Width * rect.Height);
        TestHarness.Assert(share > 0.9 * (1 - a10!.ReadyFraction!.Value) - 0.05 && share < (1 - a10.ReadyFraction.Value) + 0.05, $"the clock covers the share still to come ({share:F2} of the cameo, {a10.ReadyFraction:F2} ready)");
        TestHarness.Assert(list.OfType<UiImageDraw>().Any(image => image.Image == a10!.Button.ButtonImage && !image.Grayscale), "the cameo in colour under the clock");
    }

    private static void GeneralsWindow(GameData data)
    {
        var scenario = new DemoScenario("hud-generals", "hud-generals", "hud-generals", 300, [new ScenarioUnit("centre", "AmericaCommandCenter", 1, 0, 0, 0f)], [], 0f, 0f)
        {
            HumanFaction = "FactionAmerica",
            OpponentFaction = "FactionChina",
            Sciences = ["SCIENCE_PaladinTank"],
        };
        var live = LiveScenario.Create(data, scenario, new Vector2(1280, 720));
        var controller = new MatchController(live, GameSettings.Default) { StarBlinks = false };
        live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
        controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        controller.ToggleGenerals();
        controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        var images = controller.BuildDrawList(1280, 720).Commands.OfType<UiImageDraw>().ToArray();
        TestHarness.Assert(images.Any(image => image.Image.StartsWith("GeneralsPowerMenu", StringComparison.Ordinal)), $"the window draws its original background ({string.Join(", ", images.Select(image => image.Image).Distinct().Take(12))})");
        var view = controller.Generals!;
        var all = view.Rank1.Concat(view.Rank3).Concat(view.Rank8).OfType<CnCNew.Presentation.Input.CommandBarButton>().ToArray();
        var owned = all.Where(button => button.Reason == CnCNew.Presentation.Input.ButtonReason.AlreadyOwned).Select(button => button.Button.ButtonImage).ToHashSet();
        var unaffordable = all.Where(button => !button.IsEnabled && button.Reason != CnCNew.Presentation.Input.ButtonReason.AlreadyOwned).Select(button => button.Button.ButtonImage).ToHashSet();
        TestHarness.Assert(owned.Count > 0 && unaffordable.Count > 0, $"one science is owned, others wait for points ({owned.Count}, {unaffordable.Count})");
        TestHarness.Assert(images.Where(image => owned.Contains(image.Image)).All(image => !image.Grayscale) && images.Where(image => unaffordable.Contains(image.Image)).All(image => image.Grayscale)
            && images.Any(image => unaffordable.Contains(image.Image)), "the owned science in colour, the ones without points grey");
    }

    private static void ConstructionPercent(GameData data)
    {
        var scenario = new DemoScenario("hud-construct", "hud-construct", "hud-construct", 900, [new ScenarioUnit("dozer", "AmericaVehicleDozer", 1, -90, 0, 0f)],
            [new ScenarioOrder(10, "dozer", ScenarioOrderKind.Build, "plant", X: 30, Y: -70, Template: "AmericaPowerPlant")], 20, -15, 20f, 42f, 330f)
        {
            HumanFaction = "FactionAmerica",
            OpponentFaction = "FactionChina",
            StartMoney = 5000,
        };
        var live = LiveScenario.Create(data, scenario, new Vector2(1920, 1080));
        var controller = new MatchController(live, GameSettings.Default);
        UiTextDraw? percent = null;
        for (var frame = 0; frame < 600 && percent is null; frame++)
        {
            live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
            controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
            if (frame % 30 != 0) continue;
            percent = controller.BuildDrawList(1920, 1080).Commands.OfType<UiTextDraw>().FirstOrDefault(text => text.Text.Contains('%'));
        }
        TestHarness.Assert(percent is not null, "a building under construction shows its percent");
        // The tallest line of the label font as the screens scale it (12 points bold, up to twice at 1080 lines) lies below the point.
        TestHarness.Assert(percent!.Clip.Y <= percent.Y && percent.Clip.Y + percent.Clip.Height >= percent.Y + 40, $"the clip holds the whole line ({percent})");
    }
}
