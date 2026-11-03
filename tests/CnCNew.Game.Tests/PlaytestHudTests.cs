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
        harness.Run("Over the own bunker the infantry's pointer is enter, over a building the enemy holds it is attack", () => BuildingCursors(data));
        harness.Run("A click on a ready power of the bar presses it, enters its mode and changes the pointer over the ground", () => PowerClick(data));
        return harness.Failures;
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
