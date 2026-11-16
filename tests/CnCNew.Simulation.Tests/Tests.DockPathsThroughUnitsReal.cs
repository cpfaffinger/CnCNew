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
    /// warehouse in the same frame as without him. The same truck sent there by a plain move is held up behind him (the setup does meet the hold), but only for a moment:
    /// #406, <c>processCollision</c> tells infantry in a vehicle's way that is not already moving away from it to move out of the way (<c>aiMoveAwayFromUnit</c>), and the Red Guard steps off the
    /// truck's way and then goes on to his own goal. At 30, 60 and 120 Hz.
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
            long Arrival(bool dock, bool soldier) => Run(dock, soldier, out _, out _);
            long Run(bool dock, bool soldier, out Fixed aside, out bool soldierArrives)
            {
                aside = Fixed.Zero;
                soldierArrives = false;
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
                var redGuard = soldier ? world.Entities.Single(entity => entity.Template.Name == "ChinaInfantryRedguard") : null;
                var arrival = -1L;
                SimulationWorld? copy = null;
                for (var step = 0; step < world.Frames(60 * 60); step++)
                {
                    world.Step();
                    copy?.Step();
                    if (redGuard is not null) aside = Fixed.Max(aside, (redGuard.Y - Fixed.FromInt(800)).Abs());
                    // While the Red Guard steps aside, a restored world goes on alike.
                    if (copy is null && !dock && aside > Fixed.FromInt(2))
                    {
                        copy = CnCNew.Simulation.Snapshots.WorldSnapshot.Restore(CnCNew.Simulation.Snapshots.WorldSnapshot.Write(world), catalog, fingerprint, terrain);
                        Check.That(copy.StateHash == world.StateHash, $"{rate}: a world restored while the Red Guard steps aside matches.");
                    }
                    if (arrival < 0 && truck.X >= Fixed.FromInt(950)) arrival = (world.Tick - start) / world.Time.TicksPerOriginalFrame;
                    if (arrival >= 0 && (redGuard is null || !redGuard.HasMoveTarget)) break;
                }
                if (copy is not null) Check.That(copy.StateHash == world.StateHash, $"{rate}: and goes on alike.");
                if (redGuard is not null) soldierArrives = !redGuard.HasMoveTarget && (redGuard.X - Fixed.FromInt(1000)).Abs() < Fixed.FromInt(20) && (redGuard.Y - Fixed.FromInt(800)).Abs() < Fixed.FromInt(20);
                return arrival;
            }
            var (docked, dockedAlone) = (Arrival(dock: true, soldier: true), Arrival(dock: true, soldier: false));
            var (moved, movedAlone) = (Run(dock: false, soldier: true, out var aside, out var soldierArrives), Arrival(dock: false, soldier: false));
            Console.WriteLine($"       {rate}: dock order {docked} frames ({dockedAlone} alone), plain move {moved} frames ({movedAlone} alone), the Red Guard {aside} units aside");
            Check.That(dockedAlone > 0 && movedAlone > 0, $"{rate}: the truck reaches the warehouse alone ({dockedAlone}, {movedAlone}).");
            Check.That(moved > movedAlone, $"{rate}: on a plain move the truck is held up behind the Red Guard ({moved} against {movedAlone} frames).");
            Check.That(moved <= movedAlone + 60, $"{rate}: the Red Guard steps out of the truck's way and holds it up for at most two seconds ({moved} against {movedAlone} frames, #406).");
            Check.That(aside > Fixed.FromInt(10), $"{rate}: the Red Guard steps off the truck's way ({aside} units).");
            Check.That(soldierArrives, $"{rate}: the Red Guard goes on to his own goal after stepping aside.");
            Check.That(docked == dockedAlone, $"{rate}: on a dock order the truck paths through the Red Guard and arrives as without him ({docked} against {dockedAlone} frames).");
        }
    }
}
