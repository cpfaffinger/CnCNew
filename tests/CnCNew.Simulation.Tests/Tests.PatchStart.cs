using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;

static partial class Tests
{
    /// <summary>
    /// <c>GameLogic::update</c> asks nobody whether an object is destroyed: an <c>OCLUpdate</c> without a delay still makes its list in the frame the <c>DeletionUpdate</c> of its
    /// object ends it. The object lives two frames, the first update starts the timer, the second finds it run out: one creation, at every rate.
    /// </summary>
    public static void StartingObjectMakesItsListOnce()
    {
        var files = new Dictionary<string, string>
        {
            ["Data/INI/ObjectCreationList.ini"] = """
            ObjectCreationList OCL_Starter
              CreateObject
                ObjectNames = StartedThing
                Disposition = ON_GROUND_ALIGNED
              End
            End
            """,
            ["Data/INI/Object/StartingThing.ini"] = """
            Object StartedThing
              KindOf = SELECTABLE VEHICLE
              Body = ActiveBody Body01
                MaxHealth = 10.0
                InitialHealth = 10.0
              End
              Behavior = DestroyDie Destroy01
              End
            End

            Object StartingThing
              KindOf = INERT
              Body = ActiveBody Body01
                MaxHealth = 1.0
                InitialHealth = 1.0
              End
              Behavior = DeletionUpdate Deletion01
                MinLifetime = 50
                MaxLifetime = 50
              End
              Behavior = OCLUpdate Ocl01
                MinDelay = 0
                MaxDelay = 0
                OCL = OCL_Starter
              End
            End
            """,
        };
        foreach (var (rate, factor) in WorldRates)
        {
            var fixture = new SyntheticFixture(rate, files: files);
            var world = PatchModuleWorld(fixture);
            var feeder = new Feeder(world);
            Check.That(feeder.Spawn(1, "StartingThing", owner: 1, x: 100, y: 100).Accepted, $"{rate}: the starting object appears.");
            for (var tick = 0; tick < 10 * factor; tick++) world.Step();
            Check.That(world.Entities.Count(entity => entity.Template.Name == "StartedThing") == 1 && !world.Entities.Any(entity => entity.Template.Name == "StartingThing"),
                $"{rate}: {world.Entities.Count(entity => entity.Template.Name == "StartedThing")} things made, the object gone.");
        }
    }

    /// <summary>
    /// The patch starts a player with an object of its own (<c>AmericaStartingThings</c>, ...): it lives 50 ms, and its <c>OCLUpdate</c> without a delay makes the builder once
    /// (and a GLA side's bounty dummy) before it goes. One of each at every rate.
    /// </summary>
    public static void RealPlayersStartWithOneBuilder(string directory)
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
                [new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionAmerica")), new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionGLA"))]);
            for (var tick = 0; tick < rate.Time.OriginalFrames(10); tick++) session.Step();
            var world = session.World;
            var builders = world.Entities.Where(entity => entity.Template.Name is "AmericaVehicleDozer" or "GLAInfantryWorker").GroupBy(entity => entity.Template.Name).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group => $"{group.Key} x{group.Count()}");
            var dummies = world.Entities.Count(entity => entity.Template.Name == "CashBountyDummy");
            var things = world.Entities.Count(entity => entity.Template.Name.EndsWith("StartingThings", StringComparison.Ordinal));
            Check.That(string.Join(", ", builders) == "AmericaVehicleDozer x1, GLAInfantryWorker x1" && dummies == 1 && things == 0, $"{rate}: {string.Join(", ", builders)}, {dummies} dummies, {things} starting objects left.");
        }
    }
}
