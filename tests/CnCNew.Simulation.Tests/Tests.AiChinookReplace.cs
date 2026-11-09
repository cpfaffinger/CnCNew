using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// The owner's playtest of 9 November 2026 (finding 3): a computer player of the United States whose Chinooks are gone has Chinooks again, and one whose supply center fell too (the
    /// center makes the first Chinook and the income starts there) builds a new center even when it has less than the center's price in the bank - it sells a building, as it does for a
    /// carrier. Two generals of the United States, the carriers killed after four minutes of play, with and without the supply center razed, and 10, 800, 1,300 and 5,000 credits in the bank
    /// (a center costs 2,000): a carrier stands again within the time the sale, the construction and the Chinook need. Both ways at 60 Hz; the razed center with the least money also at 30 and 120 Hz.
    /// </summary>
    public static void RealUsaComputerPlayersReplaceTheirChinooks(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        const string MapPath = "Maps/Alpine Assault/Alpine Assault.map";
        MapData map;
        using (var stream = content.OpenRead(MapPath)) map = SageMapDecoder.Decode(stream);
        var failures = new List<string>();
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: RuleProfile.Product);
            var everyone = ReferenceMatchScenario.Participants(catalog, out _);
            foreach (var faction in rate == SimulationRate.Hz60 ? new[] { "FactionAmerica", "FactionAmericaAirForceGeneral" } : new[] { "FactionAmerica" })
                foreach (var razed in new[] { false, true })
                    foreach (var money in rate == SimulationRate.Hz60 ? new[] { 10, 800, 1300, 5000 } : new[] { 10 })
                    {
                        const uint Seed = 1;
                        var session = SkirmishMatchSession.Start(SkirmishMatchOptions.StandardAt(catalog.Rate) with { AiSeed = Seed, WorldSeed = Seed ^ 0xC0FFEE06 }, catalog, fingerprint, TerrainMap.Build(map, MapPath), map.Objects,
                            [new SkirmishSlot(everyone.Single(value => value.Faction.Name == faction)), new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionChina"))]);
                        var world = session.World;
                        var id = session.Players[0].Id;
                        bool IsCarrier(Entity entity) => entity.Owner == id && !entity.EffectivelyDead && entity.Template.IsKindOf("HARVESTER") && !entity.Template.IsKindOf("STRUCTURE");
                        bool IsCentre(Entity entity) => entity.Owner == id && !entity.EffectivelyDead && entity.Template.IsKindOf("CASH_GENERATOR") && entity.Template.IsKindOf("STRUCTURE") && !entity.HasStatus(ObjectStatusBits.UnderConstruction);
                        while (world.Tick < rate.TicksPerSecond * 60 * 4 && !session.IsOver) session.Step();
                        Check.That(world.Entities.Any(IsCentre), $"{rate} {faction}: a supply center stands after four minutes.");
                        foreach (var carrier in world.Entities.Where(IsCarrier).ToArray()) world.Kill(carrier.Id);
                        if (razed) foreach (var centre in world.Entities.Where(IsCentre).ToArray()) world.Kill(centre.Id);
                        world.SetMoney(id, Fixed.FromInt(money));
                        var start = world.Tick;
                        var seconds = razed ? 180 : 120;
                        var again = false;
                        while (world.Tick < start + rate.TicksPerSecond * seconds && !session.IsOver && !again)
                        {
                            session.Step();
                            again = world.Entities.Any(IsCarrier);
                        }
                        if (!again) failures.Add($"{rate} {faction} {(razed ? "razed center" : "carriers only")} money {money}");
                    }
        }
        Check.That(failures.Count == 0, "no carrier again after the raid: " + string.Join("; ", failures));
    }
}
