using System.Numerics;
using CnCNew.Game.Data;
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Views;
using CnCNew.Simulation.World;

namespace CnCNew.Game.Tests;

/// <summary>
/// #393: a right click with supply carriers selected docks only where the original's <c>ActionManager::canTransferSuppliesAt</c> lets every selected carrier dock
/// (<c>InGameUI::canSelectedObjectsDoAction(ACTIONTYPE_DOCK_AT, SELECTION_ALL)</c>): a warehouse that holds boxes and is no enemy's, a supply center of the carrier's own player only
/// while the carrier holds boxes, and for a human player no target under the shroud. Elsewhere the pointer shows no dock and the click gives no dock order. On real data: a human
/// China on Alpine Assault with its supply trucks.
/// </summary>
internal static class DockOrderTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(DockOrderTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP DockOrderTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }
        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("A supply truck docks by a right click only at a warehouse with boxes that is no enemy's, at its own supply center only with boxes, and never under the shroud (#393)", () => DockTargets(data));
        return harness.Failures;
    }

    private sealed class Rig
    {
        public readonly LiveMatch Match;
        public readonly MatchController Controller;
        public readonly SimulationWorld World;
        public readonly List<DockCommand> Docks = [];
        private ulong _sequence = 3_000_000;

        public Rig(GameData data)
        {
            var mapPath = data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase));
            Match = LiveMatch.Create(data, new MatchSetup(mapPath, "FactionChina", ["FactionAmerica"], CnCNew.Simulation.Testing.SkirmishMatchOptions.Standard), new Vector2(1280, 720));
            Controller = new MatchController(Match, SkinPins.Settings) { StarBlinks = false };
            World = Match.World;
            Match.CommandIssued += (command, result) => { if (command is DockCommand dock && result.Accepted) Docks.Add(dock); };
            Advance((int)World.Time.OriginalFrames(4));
        }

        public PlayerId Human => Match.Human;
        public PlayerId Enemy => Match.Players.First(player => player.Id != Match.Human && player.IsComputer).Id;

        public void Advance(int frames)
        {
            for (var i = 0; i < frames; i++)
            {
                Match.Advance(CnCNew.Tests.TestTime.FrameSeconds);
                Controller.Update(CnCNew.Tests.TestTime.FrameSeconds);
            }
        }

        public Entity Spawn(string name, PlayerId owner, Entity near, int dx, int dy)
        {
            var known = World.Entities.Select(entity => entity.Id).ToHashSet();
            TestHarness.Assert(World.Submit(new SpawnCommand(PlayerId.System, _sequence++, World.Tick, name, owner, near.X + Fixed.FromInt(dx), near.Y + Fixed.FromInt(dy), Fixed.Zero, Fixed.Zero)).Accepted, $"{name} is spawned");
            Advance((int)World.Time.OriginalFrames(2));
            return World.Entities.First(entity => entity.Template.Name == name && !known.Contains(entity.Id));
        }

        public ContextHint Over(Entity target, params Entity[] selected)
        {
            Controller.Input.Selection.Replace([.. selected.Select(entity => entity.Id)]);
            return Controller.Input.Commands.Evaluate(World, target, null);
        }

        /// <summary>The right click on <paramref name="target"/> with <paramref name="selected"/>: the dock orders that reach the match.</summary>
        public List<DockCommand> Click(Entity target, params Entity[] selected)
        {
            var hint = Over(target, selected);
            var before = Docks.Count;
            Controller.Input.Commands.IssueContext(World, hint, target);
            Advance(2);
            return Docks.Skip(before).ToList();
        }
    }

    private static void DockTargets(GameData data)
    {
        var rig = new Rig(data);
        var world = rig.World;
        var human = rig.Human;
        var center = world.Entities.First(entity => entity.Owner == human && entity.Template.IsKindOf("COMMANDCENTER"));
        var supply = rig.Spawn("ChinaSupplyCenter", human, center, 320, 0);
        var stocked = rig.Spawn("SupplyWarehouse", PlayerId.System, center, 0, 320);
        var emptied = rig.Spawn("SupplyWarehouse", PlayerId.System, center, 0, -320);
        var hostile = rig.Spawn("SupplyWarehouse", rig.Enemy, center, -320, 0);
        world.SetWarehouseValue(emptied.Id, 0);
        var truck = rig.Spawn("ChinaVehicleSupplyTruck", human, center, 160, 160);
        TestHarness.Assert(stocked.WarehouseSupplyBoxes() > 0 && hostile.WarehouseSupplyBoxes() > 0 && emptied.WarehouseSupplyBoxes() == 0,
            $"two warehouses hold boxes, one is empty ({stocked.WarehouseSupplyBoxes()}, {hostile.WarehouseSupplyBoxes()}, {emptied.WarehouseSupplyBoxes()})");
        TestHarness.Assert(truck.CarriedSupplyBoxes() == 0 && world.AreEnemies(human, hostile.Owner), "the new truck is empty, the third warehouse is the enemy's");
        TestHarness.Assert(new[] { supply, stocked, emptied, hostile }.All(entity => world.IsPositionExploredBy(human, entity.X, entity.Y)), "every target lies in explored ground");

        var atStocked = rig.Over(stocked, truck);
        TestHarness.Assert(atStocked is { Action: ContextAction.Dock, Cursor: CursorKind.Dock }, $"an empty truck over a warehouse with boxes: dock ({atStocked.Action}, {atStocked.Cursor})");
        foreach (var (target, what) in new[] { (emptied, "an empty warehouse"), (hostile, "the enemy's warehouse"), (supply, "its own supply center while it carries nothing") })
        {
            var hint = rig.Over(target, truck);
            TestHarness.Assert(hint.Action != ContextAction.Dock && hint.Cursor != CursorKind.Dock, $"an empty truck over {what}: no dock ({hint.Action}, {hint.Cursor})");
            TestHarness.Assert(rig.Click(target, truck) is [], $"a right click on {what} gives no dock order");
        }

        TestHarness.Assert(rig.Click(stocked, truck) is [{ } toStocked] && toStocked.Actor == truck.Id && toStocked.Dock == stocked.Id, "a right click on the warehouse with boxes sends the truck there");
        var limit = world.Tick + world.Time.TicksPerSecond * 120;
        while (truck.CarriedSupplyBoxes() == 0 && world.Tick < limit) rig.Advance(1);
        TestHarness.Assert(truck.CarriedSupplyBoxes() > 0, $"the truck loads boxes at the warehouse ({truck.CarriedSupplyBoxes()})");

        var loaded = rig.Over(supply, truck);
        TestHarness.Assert(loaded is { Action: ContextAction.Dock, Cursor: CursorKind.Dock }, $"a loaded truck over its own supply center: dock ({loaded.Action}, {loaded.Cursor})");
        var empty = rig.Spawn("ChinaVehicleSupplyTruck", human, center, -160, 160);
        TestHarness.Assert(empty.CarriedSupplyBoxes() == 0 && truck.CarriedSupplyBoxes() > 0, "a second truck, empty, beside the loaded one");
        var mixed = rig.Over(supply, truck, empty);
        TestHarness.Assert(mixed.Action != ContextAction.Dock, $"a loaded and an empty truck over the supply center: no dock - every selected carrier must be able to (SELECTION_ALL) ({mixed.Action})");
        TestHarness.Assert(rig.Over(stocked, truck, empty) is { Action: ContextAction.Dock }, "both over the warehouse with boxes: dock");
        TestHarness.Assert(rig.Click(supply, truck) is [{ } toSupply] && toSupply.Actor == truck.Id && toSupply.Dock == supply.Id, "a right click on the supply center sends the loaded truck there");

        // The shroud: a human player's carrier never docks at what lies in ground its player has not explored (the fog is fine).
        world.ShroudMap(human);
        TestHarness.Assert(!world.IsPositionExploredBy(human, stocked.X, stocked.Y), "the warehouse lies under the shroud now");
        var shrouded = rig.Over(stocked, empty);
        TestHarness.Assert(shrouded.Action != ContextAction.Dock, $"an empty truck over a warehouse with boxes under the shroud: no dock ({shrouded.Action})");
    }
}
