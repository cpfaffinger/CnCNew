using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;

static partial class Tests
{
    /// <summary>
    /// The owner's playtest ("the enemy stops collecting"): a computer player that spends the price of its first supply center on other things never earns again, because its income starts at
    /// that center. America against China on Alpine Assault (seeds 1 and 2) and on Bitter Winter (seed 2) ran out of money before the center was placed: its place beside the supply source is
    /// found only after a builder has walked there to look, the price was kept back only while a builder was free to try, and the army spent it in the thinks in between. Every player must
    /// have delivered supplies within six game minutes. The player's name in a failure is the faction that never earned.
    /// </summary>
    public static void RealComputerPlayersEarnFromTheirFirstCenter(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        (string Map, uint Seed, SimulationRate Rate)[] cases =
        [
            ("Maps/Alpine Assault/Alpine Assault.map", 1, SimulationRate.Hz60), ("Maps/Alpine Assault/Alpine Assault.map", 2, SimulationRate.Hz60),
            ("Maps/Bitter Winter/Bitter Winter.map", 2, SimulationRate.Hz60), ("Maps/Alpine Assault/Alpine Assault.map", 1, SimulationRate.Hz30), ("Maps/Alpine Assault/Alpine Assault.map", 1, SimulationRate.Hz120),
        ];
        foreach (var (mapPath, seed, rate) in cases)
        {
            MapData map;
            using (var stream = content.OpenRead(mapPath)) map = SageMapDecoder.Decode(stream);
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: RuleProfile.Product);
            var everyone = ReferenceMatchScenario.Participants(catalog, out _);
            var session = SkirmishMatchSession.Start(SkirmishMatchOptions.StandardAt(catalog.Rate) with { AiSeed = seed, WorldSeed = seed ^ 0xC0FFEE06 }, catalog, fingerprint, TerrainMap.Build(map, mapPath), map.Objects,
                [new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionAmerica")), new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionChina"))]);
            var delivered = new long[session.Players.Length];
            var end = rate.Time.OriginalFrames(30 * 360);
            while (session.World.Tick < end && !session.IsOver)
            {
                var result = session.Step();
                if (result.Events is not { } events) continue;
                foreach (var ev in events)
                {
                    if (ev.Kind != SimEventKind.SupplyDeposited || ev.Entity == ev.Other || session.World.FindEntity(ev.Entity) is not { } carrier) continue;
                    for (var player = 0; player < delivered.Length; player++)
                        if (session.Players[player].Id == carrier.Owner) delivered[player] += ev.Value;
                }
            }
            for (var player = 0; player < delivered.Length; player++)
                Check.That(delivered[player] > 0, $"{mapPath} seed {seed} at {rate}: {session.Players[player].Faction} delivered no supplies in six game minutes (money {session.World.MoneyOf(session.Players[player].Id).Floor()}).");
        }
    }
}
