using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.World;
using CnCNew.Ui.Settings;

namespace CnCNew.Game.Tests;

/// <summary>
/// The owner's playtest of 8 November 2026, a dozer at its repair: its build buttons were greyed ("builder busy") until the repair was done, because the bar asked whether the dozer had any
/// task at all. The original asks <c>isTaskPending(DOZER_TASK_BUILD)</c> (<c>ControlBar::getCommandAvailability</c>): only a building in hand refuses another. A build order or a move order
/// given to a repairing dozer ends the repair, and a dozer that stands idle goes on repairing hurt structures nearby (<c>DozerPrimaryIdleState</c>).
/// </summary>
internal static class DozerOrdersTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(DozerOrdersTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP DozerOrdersTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("A dozer at a repair keeps its build buttons, takes a build order from the bar (the repair ends), and a dozer at a building refuses a second", () => BuildWhileRepairing(data));
        harness.Run("A dozer at a repair takes a move order from a right click (the repair ends) and, standing idle again, repairs the hurt structure nearby by itself", () => MoveWhileRepairing(data));
        return harness.Failures;
    }

    private static (LiveMatch Match, MatchController Controller) NewMatch(GameData data)
    {
        var mapPath = data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase));
        var setup = new MatchSetup(mapPath, "FactionAmerica", ["FactionChina"], CnCNew.Simulation.Testing.SkirmishMatchOptions.Standard);
        var match = LiveMatch.Create(data, setup, new Vector2(1280, 720));
        var controller = new MatchController(match, SkinPins.Settings) { StarBlinks = false };
        Advance(match, controller, (int)match.World.Time.OriginalFrames(4));
        return (match, controller);
    }

    private static void Advance(LiveMatch match, MatchController controller, int frames)
    {
        for (var i = 0; i < frames; i++)
        {
            match.Advance(CnCNew.Tests.TestTime.FrameSeconds);
            controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
        }
    }

    private static void Click(MatchController controller, ref long time, MouseButton button, Vector2 at)
    {
        controller.Process(new MouseMoveEvent(time += 16, at));
        controller.Process(new MouseButtonEvent(time += 16, button, true, at));
        controller.Process(new MouseButtonEvent(time += 50, button, false, at));
        time += 1000;
    }

    private static Vector2 Screen(LiveMatch match, Vector3 world)
    {
        TestHarness.Assert(match.Camera.TryWorldToScreen(world, out var screen), "the point projects onto the screen");
        return screen;
    }

    /// <summary>The scene: the human's dozer repairing its hurt command center by an order of the player (the explicit repair command, as the bar's repair button sends it).</summary>
    private static (Entity Dozer, Entity Center, AIUpdateInterface Ai) Repairing(LiveMatch match, MatchController controller)
    {
        var world = match.World;
        var human = match.Human;
        var dozer = world.Entities.First(entity => entity.Owner == human && entity.Template.IsKindOf("DOZER"));
        var center = world.Entities.First(entity => entity.Owner == human && entity.Template.IsKindOf("COMMANDCENTER"));
        var ai = dozer.Modules.OfType<AIUpdateInterface>().First(module => module.CanBuild);
        var health = center.Body!.MaxHealth;
        TestHarness.Assert(world.Submit(new DamageCommand(CnCNew.Simulation.Ids.PlayerId.System, 900_001, world.Tick, center.Id, health / Fixed.FromInt(2), DamageTypes.Unresistable, 0)).Accepted, "the command center is hurt");
        Advance(match, controller, 2);
        var hurt = center.Body!.Health;
        TestHarness.Assert(hurt < health, "the command center is hurt");
        TestHarness.Assert(match.Commands.Issue((sequence, tick) => new RepairCommand(human, sequence, tick, dozer.Id, center.Id)).Accepted, "the repair order is accepted");
        var limit = (int)world.Time.OriginalFrames(900);
        for (var frame = 0; frame < limit && !(ai.HasBuilderTask && center.Body!.Health > hurt); frame++) Advance(match, controller, 1);
        TestHarness.Assert(ai.HasBuilderTask && center.Body!.Health > hurt, "the dozer repairs: the task is on and the structure heals");
        return (dozer, center, ai);
    }

    private static void BuildWhileRepairing(GameData data)
    {
        var (match, controller) = NewMatch(data);
        var world = match.World;
        var human = match.Human;
        long time = 10_000;
        var (dozer, _, ai) = Repairing(match, controller);
        TestHarness.Assert(ai.IsBuilding && !ai.IsConstructing, "a repair is a task, not a building in hand");

        controller.Input.Selection.Replace([dozer.Id]);
        Advance(match, controller, 1);
        controller.BuildDrawList(1280, 720);
        // The bar offers the buildings the player can afford and has the prerequisites of; the original greys them only while the dozer builds.
        var slots = controller.CommandBar.Slots.Where(button => button is { Button.Kind: GuiCommandKind.DozerConstruct }).ToArray();
        TestHarness.Assert(slots.Length > 0, "the dozer's bar has construction buttons");
        TestHarness.Assert(slots.All(button => button!.Reason != ButtonReason.BuilderBusy), $"no construction button says the builder is busy ({string.Join(", ", slots.Select(button => button!.Reason))})");
        var open = slots.FirstOrDefault(button => button!.Reason == ButtonReason.None);
        TestHarness.Assert(open is not null, "a construction button of the repairing dozer is available");

        var rect = controller.WindowRect($"ControlBar.wnd:ButtonCommand{open!.Slot:00}") ?? throw new InvalidOperationException("button window missing");
        Click(controller, ref time, MouseButton.Left, new Vector2(rect.CenterX, rect.CenterY));
        Advance(match, controller, 1);
        TestHarness.Assert(controller.Input.Commands.Mode == CommandModeKind.Placement, "a click on the button starts the placement");
        var template = controller.Input.Commands.PendingButton!.Object;
        var ground = SimulationQueries.GroundOf(dozer);
        PlacementGhost? ghost = null;
        foreach (var offset in (ReadOnlySpan<Vector2>)[new(300, 0), new(-300, 0), new(0, 300), new(0, -300), new(220, 220), new(-220, -220), new(220, -220), new(-220, 220)])
        {
            var candidate = ground + offset;
            controller.Process(new MouseMoveEvent(time += 16, Screen(match, new Vector3(candidate.X, candidate.Y, 0f))));
            if (controller.Input.Commands.Ghost is { IsValid: true } valid) { ghost = valid; break; }
        }
        TestHarness.Assert(ghost is not null, "a buildable spot exists near the dozer");
        var moneyBefore = world.MoneyOf(human);
        Click(controller, ref time, MouseButton.Left, Screen(match, ghost!.Position));
        Advance(match, controller, 3);
        var site = world.Entities.FirstOrDefault(entity => entity.Owner == human && entity.Template.Name == template && entity.HasStatus(CnCNew.Simulation.Modules.ObjectStatusBits.UnderConstruction));
        TestHarness.Assert(site is not null && world.MoneyOf(human) < moneyBefore, "the build order was taken: the site stands and is paid");
        TestHarness.Assert(ai.IsConstructing, "the dozer builds instead of repairing");

        // A dozer with a building in hand is the one whose buttons the original greys (isTaskPending(DOZER_TASK_BUILD)).
        Advance(match, controller, 1);
        controller.BuildDrawList(1280, 720);
        var busy = controller.CommandBar.Slots.Where(button => button is { Button.Kind: GuiCommandKind.DozerConstruct }).ToArray();
        TestHarness.Assert(busy.Length > 0 && busy.Any(button => button!.Reason == ButtonReason.BuilderBusy), $"while it builds, the buttons are greyed and name the busy builder ({string.Join(", ", busy.Select(button => button!.Reason))})");
    }

    private static void MoveWhileRepairing(GameData data)
    {
        var (match, controller) = NewMatch(data);
        var world = match.World;
        long time = 10_000;
        var (dozer, center, ai) = Repairing(match, controller);
        controller.Input.Selection.Replace([dozer.Id]);
        Advance(match, controller, 1);
        var here = SimulationQueries.GroundOf(dozer);
        // 40 units to the side, inside the dozer's BoredRange of the command center whichever way: perpendicular to the line to the center.
        var (cx, cy) = ((double)center.X.ToDecimal() - here.X, (double)center.Y.ToDecimal() - here.Y);
        var length = Math.Sqrt(cx * cx + cy * cy);
        Console.WriteLine($"       dozer to center: {length:F1}");
        Click(controller, ref time, MouseButton.Right, Screen(match, new Vector3(here.X + (float)(-cy / length * 40), here.Y + (float)(cx / length * 40), 0f)));
        Advance(match, controller, 2);
        TestHarness.Assert(!ai.HasBuilderTask && dozer.HasMoveTarget, "the move order ends the repair and the dozer walks");
        var limit = (int)world.Time.OriginalFrames(300);
        for (var frame = 0; frame < limit && dozer.HasMoveTarget; frame++) Advance(match, controller, 1);
        TestHarness.Assert(!dozer.HasMoveTarget && !ai.HasBuilderTask, "it arrives and stands idle");
        var health = center.Body!.Health;
        TestHarness.Assert(health < center.Body.MaxHealth, "the command center is still hurt");
        // BoredTime is 5 s; the dozer looks once its time is over and takes the closest hurt structure within BoredRange.
        for (var frame = 0; frame < (int)world.Time.OriginalFrames(5 * 30 + 90) && !ai.HasBuilderTask; frame++) Advance(match, controller, 1);
        TestHarness.Assert(ai.HasBuilderTask, "the idle dozer goes back to repairing by itself");
        for (var frame = 0; frame < (int)world.Time.OriginalFrames(300) && center.Body!.Health <= health; frame++) Advance(match, controller, 1);
        TestHarness.Assert(center.Body!.Health > health, "and the structure heals again");
    }
}
