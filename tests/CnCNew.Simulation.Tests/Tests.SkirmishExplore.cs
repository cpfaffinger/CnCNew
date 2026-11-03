using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;

static partial class Tests
{
    /// <summary>
    /// <c>GameLogic::startNewGame</c>: "remove shroud for the player in MP games" (<c>revealMapForPlayer</c>) unless <c>Multiplayer.ini</c> says <c>UseShroud</c>, and the shipped data say no. A
    /// skirmish starts with every cell of the map explored by every player - the terrain and what stands on it under the fog, nothing of the enemy's units in sight. The owner's playtest of
    /// 3 November 2026: the port left the map black. At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealSkirmishStartsWithTheMapExplored(string directory)
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
            Check.That(!catalog.ShroudInMultiplayer, "Multiplayer.ini says UseShroud = No.");
            var everyone = ReferenceMatchScenario.Participants(catalog, out _);
            var terrain = TerrainMap.Build(map, MapPath);
            var session = SkirmishMatchSession.Start(SkirmishMatchOptions.StandardAt(catalog.Rate), catalog, fingerprint, terrain, map.Objects,
                [new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionAmerica")), new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionChina"))]);
            session.Step();
            var world = session.World;
            var (farX, farY) = (Fixed.FromInt(terrain.CellsX * TerrainMap.CellSize / 2), Fixed.FromInt(terrain.CellsY * TerrainMap.CellSize / 2));
            foreach (var player in new[] { new PlayerId(1), new PlayerId(2) })
            {
                var enemyStart = terrain.StartPositions[player.Value == 1 ? 1 : 0];
                Check.That(world.IsPositionExploredBy(player, farX, farY) && world.IsPositionExploredBy(player, enemyStart.X, enemyStart.Y), $"{rate}: player {player.Value} has explored the map.");
                Check.That(!world.IsPositionVisibleTo(player, enemyStart.X, enemyStart.Y), $"{rate}: player {player.Value} does not see the enemy's base.");
            }
        }

        // Only the system explores a map for a player.
        var fixture = new SyntheticFixture(TestRate.Default);
        var synthetic = NewWorld(fixture);
        Check.That(!synthetic.Submit(new ExploreMapCommand(new PlayerId(1), 1, synthetic.Tick, new PlayerId(1))).Accepted && synthetic.Submit(new ExploreMapCommand(PlayerId.System, 1, synthetic.Tick, new PlayerId(1))).Accepted,
            "A player explores nothing; the system does.");
        Check.That(CommandTypes.IsKnown(CommandTypes.ExploreMap) && !CommandTypes.IsPlayerOrder(CommandTypes.ExploreMap), "No order of a player.");
    }
}
