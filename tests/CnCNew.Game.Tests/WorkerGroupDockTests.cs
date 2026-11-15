using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.World;

namespace CnCNew.Game.Tests;

/// <summary>
/// #384 (the owner's playtest of 15 November 2026): several GLA workers selected together and sent to a supply dock by a right click on it all gather - each docks in its turn and
/// carries its boxes to the stash (<c>AIGroup::groupDock</c>: one dock order per member).
/// </summary>
internal static class WorkerGroupDockTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(WorkerGroupDockTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP WorkerGroupDockTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("A right click on a supply dock with five GLA workers selected sends all of them, and every one delivers (#384)", () => GroupToDock(data));
        return harness.Failures;
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

    private static void GroupToDock(GameData data)
    {
        var mapPath = data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase));
        var setup = new MatchSetup(mapPath, "FactionGLA", ["FactionChina"], CnCNew.Simulation.Testing.SkirmishMatchOptions.Standard);
        var match = LiveMatch.Create(data, setup, new Vector2(1280, 720));
        var controller = new MatchController(match, SkinPins.Settings) { StarBlinks = false };
        Advance(match, controller, (int)match.World.Time.OriginalFrames(4));
        var world = match.World;
        var human = match.Human;
        var deliveries = new Dictionary<EntityId, int>();
        match.TickStepped += result =>
        {
            foreach (var item in result.Events)
                if (item.Kind == SimEventKind.SupplyDeposited && deliveries.ContainsKey(item.Entity)) deliveries[item.Entity]++;
        };
        world.SetMoney(human, Fixed.FromInt(10_000));
        var worker = world.Entities.First(entity => entity.Owner == human && entity.Template.Name == "GLAInfantryWorker");
        TestHarness.Assert(world.Templates.TryGet("GLASupplyStash", out var stash), "the supply stash is a template");
        var dock = world.Entities.Where(entity => world.WarehouseBoxes(entity.Id) > 0)
            .OrderBy(entity => (entity.X - worker.X).Abs() + (entity.Y - worker.Y).Abs()).ThenBy(entity => entity.Id.Value).First();
        (Fixed X, Fixed Y)? place = null;
        for (var distance = 110; distance <= 300 && place is null; distance += 20)
            for (var step = 0; step < 16 && place is null; step++)
            {
                var angle = Fixed.FromInt(step) * FixedTrig.Pi / Fixed.FromInt(8);
                var (x, y) = (dock.X + Fixed.FromInt(distance) * FixedTrig.Cos(angle), dock.Y + Fixed.FromInt(distance) * FixedTrig.Sin(angle));
                if (world.CheckBuildGround(stash!, x, y, Fixed.Zero) == LegalBuildCode.Ok) place = (x, y);
            }
        TestHarness.Assert(place is not null, "a legal place for the stash beside the dock");
        // The stash is the scene, not the subject: the world takes the site as a computer player's (no look at the shroud).
        world.SetValidatesOwnSites(human);
        var build = match.Commands.Issue((sequence, tick) => new ConstructCommand(human, sequence, tick, worker.Id, "GLASupplyStash", place!.Value.X, place.Value.Y, Fixed.Zero));
        TestHarness.Assert(build.Accepted, $"the order to build the stash is accepted ({build})");
        var center = world.Entities.First(entity => entity.Owner == human && entity.Template.IsKindOf("COMMANDCENTER"));
        for (var i = 0; i < 4; i++)
            TestHarness.Assert(match.Commands.Issue((sequence, tick) => new QueueProductionCommand(human, sequence, tick, center.Id, "GLAInfantryWorker")).Accepted, "a worker is queued");
        var wait = world.Tick + world.Time.TicksPerSecond * 150;
        while (world.Tick < wait) Advance(match, controller, 1);
        TestHarness.Assert(world.Entities.Any(entity => entity.Owner == human && entity.Template == stash && !entity.HasStatus(ObjectStatusBits.UnderConstruction)), "the stash stands");
        var workers = world.Entities.Where(entity => entity.Owner == human && entity.Template.Name == "GLAInfantryWorker" && !entity.EffectivelyDead).OrderBy(entity => entity.Id.Value).ToList();
        TestHarness.Assert(workers.Count >= 5, $"the player has at least five workers ({workers.Count})");
        foreach (var each in workers) deliveries[each.Id] = 0;

        var ground = SimulationQueries.GroundOf(dock);
        match.Camera.CenterOn(ground);
        Advance(match, controller, 2);
        controller.Input.Selection.Replace([.. workers.Select(each => each.Id)]);
        Advance(match, controller, 1);
        var hint = controller.Input.Commands.Evaluate(world, dock, null);
        Console.WriteLine($"       {workers.Count} workers, dock {dock.Template.Name} at ({dock.X.Floor()},{dock.Y.Floor()}), the right click means {hint.Action}");
        TestHarness.Assert(hint.Action == ContextAction.Dock, $"a right click on the dock with the workers selected means docking ({hint.Action})");
        TestHarness.Assert(match.Camera.TryWorldToScreen(new Vector3(ground.X, ground.Y, CnCNew.Presentation.FixedConvert.ToSingle(dock.Z) + 10f), out var screen), "the dock is on the screen");
        long time = 10_000;
        var issued = 0;
        match.CommandIssued += (command, result) => { if (command is DockCommand && result.Accepted) issued++; };
        Click(controller, ref time, MouseButton.Right, screen);
        var end = world.Tick + world.Time.TicksPerSecond * 180;
        while (world.Tick < end) Advance(match, controller, 1);
        Console.WriteLine($"       {issued} dock orders, deliveries {string.Join(", ", deliveries.Select(pair => $"{pair.Key.Value}:{pair.Value}"))}");
        TestHarness.Assert(issued == workers.Count, $"the right click sent every selected worker to the dock ({issued} of {workers.Count})");
        // The computer opponent may reach the dock in that time: a worker it killed is no answer either way.
        var living = workers.Where(each => !each.EffectivelyDead && !each.DestroyRequested).Select(each => each.Id).ToHashSet();
        TestHarness.Assert(living.Count >= 5, $"at least five workers lived through the three minutes ({living.Count})");
        TestHarness.Assert(deliveries.Where(pair => living.Contains(pair.Key)).All(pair => pair.Value >= 3), $"every worker went to and fro within three game minutes ({string.Join(", ", deliveries.Values)})");
    }
}
