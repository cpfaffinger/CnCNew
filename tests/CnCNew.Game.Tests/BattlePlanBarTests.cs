using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Ui.Runtime;
using CnCNew.Ui.Skins;

namespace CnCNew.Game.Tests;

/// <summary>The owner's playtest of 6 November 2026 (the stances of the Strategy Center do not switch): the three plan buttons, from the click to what the bar shows, on real data.</summary>
internal static class BattlePlanBarTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(BattlePlanBarTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP BattlePlanBarTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("A click on a plan button of the Strategy Center presses it at once and keeps it pressed through unpacking, running and packing; the other two stand out (both skins)", () =>
        {
            SkinPins.With(UiSkin.Original, () => PlanButtons(data));
            SkinPins.With(UiSkin.Modern, () => PlanButtons(data));
        });
        harness.Run("The Strategy Center's own stop button is grey unless the bombardment runs", () => StopButton(data));
        harness.Run("In a skirmish the Strategy Center of every American general switches its plans by a click on the bar", () => SkirmishCenters(data));
        return harness.Failures;
    }

    private const string Prefix = "Command_InitiateBattlePlan";
    private static readonly string[] Plans = ["Bombardment", "HoldTheLine", "SearchAndDestroy"];

    private sealed class Bar(MatchController controller, LiveGame live)
    {
        public void Frame()
        {
            live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
            controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        }

        public void Seconds(int seconds)
        {
            for (var frame = 0; frame < seconds * CnCNew.Tests.TestTime.FramesPerSecond; frame++) Frame();
        }

        public CommandBarButton? Button(string name) => controller.CommandBar.Slots.FirstOrDefault(button => button?.Button.Name == name);

        public string Row() => string.Join(" ", Plans.Select(plan => $"{plan}:{Button(Prefix + plan)?.Availability}"));

        public void Click(string name)
        {
            controller.BuildDrawList(1280, 720);
            var rect = controller.WindowRect($"ControlBar.wnd:ButtonCommand{Button(name)!.Slot:00}")!.Value;
            var at = new Vector2(rect.CenterX, rect.CenterY);
            controller.Process(new MouseMoveEvent(9_000, at));
            controller.Process(new MouseButtonEvent(9_010, MouseButton.Left, true, at));
            controller.Process(new MouseButtonEvent(9_060, MouseButton.Left, false, at));
            for (var frame = 0; frame < 3; frame++) Frame();
            controller.BuildDrawList(1280, 720);
        }

        public bool Pressed(string name) => controller.Tree.FindByName($"ControlBar.wnd:ButtonCommand{Button(name)!.Slot:00}")!.IsSelected;

        public string Look(string name)
        {
            var rect = controller.WindowRect($"ControlBar.wnd:ButtonCommand{Button(name)!.Slot:00}")!.Value;
            return string.Join("|", controller.BuildDrawList(1280, 720).Commands.Where(command => command switch
            {
                UiImageDraw image => rect.Contains(image.Destination.X, image.Destination.Y),
                UiFillRect fill => rect.Contains(fill.Rect.X, fill.Rect.Y),
                _ => false,
            }).Select(command => command.ToString()));
        }
    }

    private static (MatchController Controller, Bar Bar, BattlePlanUpdate Plan) Stage(GameData data)
    {
        var scenario = new DemoScenario("plan-bar", "plan-bar", "plan-bar", 30000,
            [new ScenarioUnit("center", "AmericaStrategyCenter", 1, -260, -80, 0f), new ScenarioUnit("ranger", "AmericaInfantryRanger", 1, -200, -20, 0f)], [], 0f, 0f)
        {
            HumanFaction = "FactionAmerica",
            OpponentFaction = "FactionChina",
            StartMoney = 10000,
        };
        var live = LiveScenario.Create(data, scenario, new Vector2(1280, 720));
        var controller = new MatchController(live, SkinPins.Settings) { StarBlinks = false };
        var bar = new Bar(controller, live);
        bar.Seconds(1);
        var center = live.Tags["center"];
        controller.Input.Selection.Replace([center]);
        bar.Frame();
        controller.BuildDrawList(1280, 720);
        return (controller, bar, live.World.FindEntity(center)!.Modules.OfType<BattlePlanUpdate>().Single());
    }

    private static void PlanButtons(GameData data)
    {
        var (_, bar, plan) = Stage(data);
        TestHarness.Assert(Plans.All(name => bar.Button(Prefix + name) is { Availability: ButtonAvailability.Available }), $"the three plans are on the bar and none is on ({bar.Row()})");
        var idleLook = bar.Look(Prefix + "Bombardment");

        void Expect(string on, string state)
        {
            TestHarness.Assert(Plans.All(name => bar.Button(Prefix + name) is { } button && (button.Availability == ButtonAvailability.Active) == (name == on)), $"{state}: only {on} is on ({bar.Row()}; status {plan.Status}, desired {plan.DesiredPlan})");
            TestHarness.Assert(Plans.All(name => bar.Pressed(Prefix + name) == (name == on)), $"{state}: only the button of {on} is pressed");
        }

        bar.Click(Prefix + "Bombardment");
        TestHarness.Assert(plan.Status == BattlePlanUpdate.TransitionStatus.Unpacking, $"the click starts the unpack ({plan.Status})");
        Expect("Bombardment", "while it unpacks");
        TestHarness.Assert(bar.Look(Prefix + "Bombardment") != idleLook, "a pressed button looks different from a resting one");
        bar.Seconds(8);
        TestHarness.Assert(plan.ActivePlan == BattlePlanKind.Bombardment, $"bombardment runs ({plan.Status})");
        Expect("Bombardment", "while it runs");

        // The next click: the button of the plan asked for is on from the click, through the pack-up of the first plan and the unpack of the second.
        bar.Click(Prefix + "HoldTheLine");
        Expect("HoldTheLine", "at the click");
        bar.Seconds(3);
        TestHarness.Assert(plan.Status == BattlePlanUpdate.TransitionStatus.Packing, $"the first plan packs ({plan.Status})");
        Expect("HoldTheLine", "while the first plan packs");
        bar.Seconds(6);
        TestHarness.Assert(plan.Status == BattlePlanUpdate.TransitionStatus.Unpacking && plan.CurrentPlan == BattlePlanKind.HoldTheLine, $"the second plan unpacks ({plan.Status}, {plan.CurrentPlan})");
        Expect("HoldTheLine", "while the second plan unpacks");
        bar.Seconds(8);
        TestHarness.Assert(plan.ActivePlan == BattlePlanKind.HoldTheLine, $"hold the line runs ({plan.Status})");
        Expect("HoldTheLine", "while it runs");

        // A third plan: the button of the last choice is on from the click.
        bar.Click(Prefix + "SearchAndDestroy");
        Expect("SearchAndDestroy", "at the click");
        bar.Seconds(16);
        TestHarness.Assert(plan.ActivePlan == BattlePlanKind.SearchAndDestroy, $"search and destroy runs ({plan.Status}, {plan.CurrentPlan})");
        Expect("SearchAndDestroy", "once it runs");
    }

    private static void StopButton(GameData data)
    {
        var (_, bar, plan) = Stage(data);
        CommandBarButton? Stop() => bar.Button("Command_StrategyCenter_Stop");
        TestHarness.Assert(Stop() is { Availability: ButtonAvailability.Restricted }, $"the stop button is grey while no plan runs ({Stop()?.Availability})");
        bar.Click(Prefix + "Bombardment");
        TestHarness.Assert(Stop() is { Availability: ButtonAvailability.Restricted }, $"and while the bombardment unpacks ({Stop()?.Availability})");
        bar.Seconds(8);
        TestHarness.Assert(plan.ActivePlan == BattlePlanKind.Bombardment && Stop() is { Availability: ButtonAvailability.Available }, $"it works while the bombardment runs ({Stop()?.Availability})");
        bar.Click(Prefix + "HoldTheLine");
        bar.Seconds(1);
        TestHarness.Assert(Stop() is { Availability: ButtonAvailability.Restricted }, $"it is grey again once the bombardment packs ({Stop()?.Availability}, {plan.Status})");
    }

    private static void SkirmishCenters(GameData data)
    {
        foreach (var (faction, template) in new[]
        {
            ("FactionAmerica", "AmericaStrategyCenter"), ("FactionAmericaAirForceGeneral", "AirF_AmericaStrategyCenter"),
            ("FactionAmericaLaserGeneral", "Lazr_AmericaStrategyCenter"), ("FactionAmericaSuperWeaponGeneral", "SupW_AmericaStrategyCenter"),
        })
        {
            var setup = new MatchSetup(data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase)), faction, ["FactionChina"], CnCNew.Simulation.Testing.SkirmishMatchOptions.Standard);
            var match = LiveMatch.Create(data, setup, new Vector2(1280, 720));
            var controller = new MatchController(match, SkinPins.Settings) { StarBlinks = false };
            var bar = new Bar(controller, match);
            var world = match.World;
            bar.Seconds(1);
            var hq = world.Entities.First(entity => entity.Owner == match.Human && entity.Template.IsKindOf("COMMANDCENTER"));
            var known = world.Entities.Select(entity => entity.Id).ToHashSet();
            TestHarness.Assert(world.Submit(new CnCNew.Simulation.Commands.SpawnCommand(CnCNew.Simulation.Ids.PlayerId.System, 1_000_000, world.Tick, template, match.Human,
                hq.X + CnCNew.Simulation.Numerics.Fixed.FromInt(200), hq.Y, CnCNew.Simulation.Numerics.Fixed.Zero, CnCNew.Simulation.Numerics.Fixed.Zero)).Accepted, $"{template} is placed");
            bar.Seconds(1);
            var center = world.Entities.First(entity => entity.Template.Name == template && !known.Contains(entity.Id));
            var plan = center.Modules.OfType<BattlePlanUpdate>().Single();
            controller.Input.Selection.Replace([center.Id]);
            bar.Frame();
            controller.BuildDrawList(1280, 720);
            TestHarness.Assert(Plans.All(name => bar.Button(Prefix + name) is { IsEnabled: true }), $"{template}: the three plans are on the bar and enabled ({bar.Row()})");
            var expected = new[] { BattlePlanKind.Bombardment, BattlePlanKind.HoldTheLine, BattlePlanKind.SearchAndDestroy, BattlePlanKind.Bombardment };
            var names = new[] { "Bombardment", "HoldTheLine", "SearchAndDestroy", "Bombardment" };
            for (var step = 0; step < names.Length; step++)
            {
                bar.Click(Prefix + names[step]);
                TestHarness.Assert(plan.DesiredPlan == expected[step] && bar.Button(Prefix + names[step]) is { Availability: ButtonAvailability.Active }, $"{template}: the click on {names[step]} chooses it and presses its button ({plan.DesiredPlan}, {bar.Row()})");
                bar.Seconds(step == 0 ? 8 : 16);
                TestHarness.Assert(plan.ActivePlan == expected[step], $"{template}: {names[step]} is in force ({plan.ActivePlan}, {plan.Status})");
            }
        }
    }
}
