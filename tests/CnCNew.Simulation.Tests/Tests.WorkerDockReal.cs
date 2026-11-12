using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// #300: an agent's synthetic probe saw a GLA worker that was given a dock order on a supply dock start no gathering loop. On real data, in a skirmish on Alpine Assault with a
    /// human GLA player: its worker builds a supply stash beside the nearest supply dock, and is then given the <see cref="DockCommand"/> a right click on the dock gives
    /// (<c>CommandModeController</c>, <c>ContextAction.Dock</c>). The worker must act as a supply truck (<c>WorkerAIUpdate</c>'s master machine takes the supply brain when it docks)
    /// and go to and fro on its own: within two game minutes at least three deliveries at the stash, the dock's stock lower by what was delivered. The stash's <c>SpawnBehavior</c>
    /// (one <c>GLAInfantryWorker</c>, <c>OneShot</c>) gives a second worker that gathers from the same dock by itself (<c>SupplyCenterProductionExitUpdate</c> forces its want), so the
    /// dock loses more than the ordered worker brings. At 30, 60 and 120 Hz. Measured on 12 November 2026: 11 deliveries of the ordered worker at every rate - not a defect.
    /// </summary>
    public static void RealGlaWorkerGathersAfterADockOrder(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        const string MapPath = "Maps/Alpine Assault/Alpine Assault.map";
        MapData map;
        using (var stream = content.OpenRead(MapPath)) map = SageMapDecoder.Decode(stream);
        foreach (var (rate, _) in WorldRates)
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: RuleProfile.Product);
            var everyone = ReferenceMatchScenario.Participants(catalog, out _);
            var session = SkirmishMatchSession.Start(SkirmishMatchOptions.StandardAt(catalog.Rate), catalog, fingerprint, TerrainMap.Build(map, MapPath), map.Objects,
                [new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionGLA"), IsHuman: true), new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionChina"))]);
            for (var tick = 0; tick < rate.Time.OriginalFrames(30); tick++) session.Step();
            var world = session.World;
            var player = session.Players[0].Id;
            world.SetMoney(player, Fixed.FromInt(10_000));
            world.SetValidatesOwnSites(player);
            var worker = world.Entities.First(entity => entity.Owner == player && entity.Template.Name == "GLAInfantryWorker");
            var brains = worker.Modules.OfType<IWorkerBrains>().Single();
            Check.That(catalog.TryGet("GLASupplyStash", out var stash), "The supply stash is a template.");

            // The supply dock nearest to the worker, and a legal place for the stash beside it.
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
            Check.That(place is not null, $"{rate}: a legal place for the stash beside the supply dock.");
            Check.That(session.Issue(player, (sequence, tick) => new ConstructCommand(player, sequence, tick, worker.Id, "GLASupplyStash", place!.Value.X, place.Value.Y, Fixed.Zero)).Accepted,
                $"{rate}: the order to build the stash is accepted.");
            var deadline = world.Tick + rate.TicksPerSecond * 120;
            Entity? built = null;
            while (world.Tick < deadline && built is null)
            {
                session.Step();
                built = world.Entities.FirstOrDefault(entity => entity.Owner == player && entity.Template == stash && !entity.HasStatus(ObjectStatusBits.UnderConstruction));
            }
            Check.That(built is not null, $"{rate}: the worker built the stash in two game minutes.");
            for (var tick = 0; tick < rate.Time.OriginalFrames(30); tick++) session.Step();
            Check.That(!brains.ActsAsSupplyTruck, $"{rate}: the worker that built the stash acts as a dozer.");

            var boxesBefore = world.WarehouseBoxes(dock.Id)!.Value;
            Check.That(session.Issue(player, (sequence, tick) => new DockCommand(player, sequence, tick, worker.Id, dock.Id)).Accepted, $"{rate}: the dock order is accepted.");
            var deliveries = 0;
            long delivered = 0;
            var truck = false;
            var end = world.Tick + rate.TicksPerSecond * 120;
            while (world.Tick < end)
            {
                foreach (var item in session.Step().Events)
                    if (item.Kind == SimEventKind.SupplyDeposited && item.Entity == worker.Id) { deliveries++; delivered += item.Value; }
                truck |= brains.ActsAsSupplyTruck;
            }
            var taken = (long)(boxesBefore - world.WarehouseBoxes(dock.Id)!.Value) * world.ValuePerSupplyBox;
            Console.WriteLine($"       {rate}: dock {dock.Template.Name} {(built!.X - dock.X).Abs().Floor() + (built.Y - dock.Y).Abs().Floor()} units from the stash, {deliveries} deliveries, {delivered} credits, {taken} taken from the dock");
            Check.That(truck, $"{rate}: the docked worker acts as a supply truck.");
            Check.That(deliveries >= 3, $"{rate}: the worker goes to and fro by itself ({deliveries} deliveries in two game minutes).");
            Check.That(taken > 0 && taken >= delivered, $"{rate}: what was delivered was taken from the dock ({taken} taken, {delivered} delivered).");
            Check.That(world.Entities.Count(entity => entity.Owner == player && entity.Template.Name == "GLAInfantryWorker" && !entity.EffectivelyDead) == 2,
                $"{rate}: the stash gave the player its one free worker.");
        }
    }
}
