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
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// #408: a sold building lets out whoever is inside - <c>GarrisonContain::onSelling</c> takes the occupants out at once (<c>removeAllContained</c>), <c>OpenContain::onSelling</c>
    /// tells the riders of any other container to leave (a barracks healing an infantryman). Before, the riders stayed inside a building that was gone: seen by nobody, never
    /// hurt, and their player was never defeated (a computer player sold its fire base with three soldiers in it, and the match on Death Valley never ended).
    /// </summary>
    public static void SoldContainersLetTheirRidersOut()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: RetailModuleFiles());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        // Spawn commands run in order: 1 Bunker, 2 Rebel, 3 Rebel, 4 Barracks, 5 Rebel.
        feeder.Spawn(1, "Bunker", x: 0, y: 0);
        feeder.Spawn(1, "Rebel", x: 5, y: 5);
        feeder.Spawn(1, "Rebel", x: 5, y: 5);
        feeder.Spawn(1, "Barracks", x: 300, y: 0);
        feeder.Spawn(1, "Rebel", x: 303, y: 3);
        world.Step();
        var bunker = world.FindEntity(new EntityId(1))!;
        var first = world.FindEntity(new EntityId(2))!;
        var second = world.FindEntity(new EntityId(3))!;
        var barracks = world.FindEntity(new EntityId(4))!;
        var patient = world.FindEntity(new EntityId(5))!;
        feeder.Damage(5, 30);
        world.Step();
        feeder.EnterContainer(1, 2, 1);
        feeder.EnterContainer(1, 3, 1);
        feeder.EnterContainer(1, 5, 4);
        world.Step();
        Check.That(bunker.Container!.ContainCount == 2 && barracks.Container!.ContainCount == 1 && patient.ContainedBy == barracks.Id, "Two rebels garrison the bunker, a hurt one is healed in the barracks.");

        Check.That(world.Submit(new SellCommand(new PlayerId(1), 100, world.Tick, bunker.Id)).Accepted, "The garrisoned bunker can be sold.");
        Check.That(world.Submit(new SellCommand(new PlayerId(1), 101, world.Tick, barracks.Id)).Accepted, "The barracks can be sold while it heals.");
        world.Step();
        Check.That(bunker.Container.ContainCount == 0 && !first.ContainedBy.IsValid && !second.ContainedBy.IsValid, "The occupants leave the moment the bunker is sold (GarrisonContain::onSelling).");
        Check.That(barracks.Container!.ContainCount == 0 && !patient.ContainedBy.IsValid, "The patient leaves the barracks that is sold (OpenContain::onSelling).");
        for (var tick = 0; tick < world.Frames(300) && (world.FindEntity(bunker.Id) is not null || world.FindEntity(barracks.Id) is not null); tick++) world.Step();
        Check.That(world.FindEntity(bunker.Id) is null && world.FindEntity(barracks.Id) is null, "Both sales complete.");
        foreach (var rider in new[] { first, second, patient })
            Check.That(world.FindEntity(rider.Id) is { EffectivelyDead: false } alive && !alive.ContainedBy.IsValid, $"Rider #{rider.Id.Value} stands in the world after the sale.");
    }

    /// <summary>
    /// <c>OpenContain::onDelete</c>, "last possible moment cleanup": a container that leaves the world without dying or being sold takes whoever is still inside with it
    /// (<c>destroyObject</c> of every rider). Before, the riders kept pointing at a container that no longer existed.
    /// </summary>
    public static void RemovedContainerTakesItsRiders()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: RetailModuleFiles());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "Bunker", x: 0, y: 0);
        feeder.Spawn(1, "Rebel", x: 5, y: 5);
        world.Step();
        var bunker = world.FindEntity(new EntityId(1))!;
        var rebel = world.FindEntity(new EntityId(2))!;
        feeder.EnterContainer(1, 2, 1);
        world.Step();
        Check.That(rebel.ContainedBy == bunker.Id, "The rebel is inside.");
        world.RequestDestroy(bunker.Id);
        for (var tick = 0; tick < world.Frames(3); tick++) world.Step();
        Check.That(world.FindEntity(bunker.Id) is null, "The bunker is removed.");
        Check.That(world.FindEntity(rebel.Id) is null, "The rider went with the container that was removed around it.");
        Check.That(world.Entities.All(entity => !entity.ContainedBy.IsValid || world.FindEntity(entity.ContainedBy) is not null), "Nobody points at a container that is gone.");
    }

    /// <summary>#408 on the real data: the computer's fire base sold with soldiers on its stations (Death Valley, ChinaTank against AirForce, seed 4), at 30, 60 and 120 Hz.</summary>
    public static void RealSoldFireBaseLetsItsSoldiersOut(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
        var outAt = new List<long>();
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            var terrain = TerrainMap.Build(SyntheticMaps.Build(120, 120, 1, (_, _) => 0), "sold-fire-base");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "sold-fire-base", fingerprint, terrain.Fingerprint), catalog, terrain);
            var sequence = 0UL;
            void Spawn(string template, int x, int y)
                => Check.That(world.Submit(new SpawnCommand(new PlayerId(1), ++sequence, world.Tick, template, new PlayerId(1), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, template + " spawns.");
            Spawn("AirF_AmericaFireBase", 600, 600);
            Spawn("AirF_AmericaInfantryRanger", 560, 720);
            Spawn("AirF_AmericaInfantryMissileDefender", 600, 720);
            Spawn("AirF_AmericaInfantryMissileDefender", 640, 720);
            for (var i = 0; i < world.Frames(1); i++) world.Step();
            var fireBase = world.Entities.First(value => value.Template.Name == "AirF_AmericaFireBase");
            var soldiers = world.Entities.Where(value => value.Template.IsKindOf("INFANTRY")).OrderBy(value => value.Id.Value).ToList();
            foreach (var soldier in soldiers)
                Check.That(world.Submit(new MoveAndEnterCommand(new PlayerId(1), ++sequence, world.Tick, soldier.Id, fireBase.Id)).Accepted, "The order to man the fire base is accepted.");
            for (var step = 0; step < world.Frames(900) && soldiers.Any(soldier => !soldier.ContainedBy.IsValid); step++) world.Step();
            Check.That(soldiers.All(soldier => soldier.ContainedBy == fireBase.Id) && fireBase.Container!.ContainCount == 3, $"{rate}: three soldiers man the fire base.");
            Check.That(soldiers.All(soldier => !world.CanPlayerSee(new PlayerId(2), soldier)), $"{rate}: nobody else sees a soldier inside.");
            // Sold in the first tick of a frame of the original, at every rate.
            while (world.Tick % world.Time.TicksPerOriginalFrame != 0) world.Step();
            Check.That(world.Submit(new SellCommand(new PlayerId(1), ++sequence, world.Tick, fireBase.Id)).Accepted, $"{rate}: the manned fire base can be sold.");
            long? outFrame = null;
            for (var step = 0; step < world.Frames(300) && world.FindEntity(fireBase.Id) is not null; step++)
            {
                world.Step();
                if (outFrame is null && soldiers.All(soldier => !soldier.ContainedBy.IsValid)) outFrame = world.Time.ToOriginalFrames(world.Tick);
            }
            Check.That(outFrame is not null, $"{rate}: the soldiers leave the fire base that is sold.");
            outAt.Add(outFrame ?? -1);
            Check.That(world.FindEntity(fireBase.Id) is null, $"{rate}: the sale completes.");
            foreach (var soldier in soldiers)
                Check.That(world.FindEntity(soldier.Id) is { EffectivelyDead: false } alive && !alive.ContainedBy.IsValid, $"{rate}: {soldier.Template.Name}#{soldier.Id.Value} stands in the world after the fire base is sold.");
        }
        Check.That(outAt.Count == 3 && outAt.Max() - outAt.Min() <= 1, $"The soldiers come out in the same frame of the original at 30, 60 and 120 Hz ({string.Join(", ", outAt.Distinct())}).");
    }
}
