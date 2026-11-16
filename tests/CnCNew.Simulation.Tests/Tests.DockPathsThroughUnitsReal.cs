using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// #394 on real data, <c>AIDockState::update</c>: "ai->setCanPathThroughUnits(true)" for the whole dock order, and <c>AIUpdateInterface::processCollision</c> returns at once for a unit that
    /// can path through units. A Chinese supply truck sent to a warehouse behind a Red Guard of its own player who walks the same way at half its speed is not held up by him: it reaches the
    /// warehouse in the same frame as without him. The same truck sent there by a plain move is held up behind him (the setup does meet the hold). At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealDockOrderPathsThroughUnits(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "China"), new PlayerConfiguration(new PlayerId(2), "Other"));
        foreach (var (rate, _) in WorldRates)
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            // The frame of the original in which the truck first comes within 150 units of the warehouse.
            long Arrival(bool dock, bool soldier)
            {
                var terrain = TerrainMap.Build(SyntheticMaps.Build(160, 160, 1, (_, _) => 0), "dock-through-units");
                var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "dock-through-units", fingerprint, terrain.Fingerprint), catalog, terrain);
                var sequence = 0UL;
                var china = new PlayerId(1);
                void Spawn(string template, PlayerId owner, int x, int y)
                    => Check.That(world.Submit(new SpawnCommand(owner, ++sequence, world.Tick, template, owner, Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, template + " spawns.");
                Spawn("SupplyWarehouse", PlayerId.System, 1100, 800);
                Spawn("ChinaVehicleSupplyTruck", china, 300, 800);
                if (soldier) Spawn("ChinaInfantryRedguard", china, 360, 800);
                for (var step = 0; step < world.Frames(2); step++) world.Step();
                while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
                var warehouse = world.Entities.Single(entity => entity.Template.Name == "SupplyWarehouse");
                var truck = world.Entities.Single(entity => entity.Template.Name == "ChinaVehicleSupplyTruck");
                if (soldier)
                {
                    var guard = world.Entities.Single(entity => entity.Template.Name == "ChinaInfantryRedguard");
                    Check.That(world.Submit(new SetMoveTargetCommand(china, ++sequence, world.Tick, guard.Id, Fixed.FromInt(1000), Fixed.FromInt(800))).Accepted, $"{rate}: the Red Guard walks east.");
                }
                var order = dock
                    ? world.Submit(new DockCommand(china, ++sequence, world.Tick, truck.Id, warehouse.Id))
                    : world.Submit(new SetMoveTargetCommand(china, ++sequence, world.Tick, truck.Id, Fixed.FromInt(950), Fixed.FromInt(800)));
                Check.That(order.Accepted, $"{rate}: the truck's order is accepted ({order.Reason}).");
                var start = world.Tick;
                for (var step = 0; step < world.Frames(60 * 30); step++)
                {
                    world.Step();
                    if (truck.X >= Fixed.FromInt(950)) return (world.Tick - start) / world.Time.TicksPerOriginalFrame;
                }
                return -1;
            }
            var (docked, dockedAlone) = (Arrival(dock: true, soldier: true), Arrival(dock: true, soldier: false));
            var (moved, movedAlone) = (Arrival(dock: false, soldier: true), Arrival(dock: false, soldier: false));
            Console.WriteLine($"       {rate}: dock order {docked} frames ({dockedAlone} alone), plain move {moved} frames ({movedAlone} alone)");
            Check.That(dockedAlone > 0 && movedAlone > 0, $"{rate}: the truck reaches the warehouse alone ({dockedAlone}, {movedAlone}).");
            Check.That(moved > movedAlone, $"{rate}: on a plain move the truck is held up behind the Red Guard ({moved} against {movedAlone} frames).");
            Check.That(docked == dockedAlone, $"{rate}: on a dock order the truck paths through the Red Guard and arrives as without him ({docked} against {dockedAlone} frames).");
        }
    }
}
