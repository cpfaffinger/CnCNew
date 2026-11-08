using System.Collections.Immutable;
using System.Linq;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Real data, the owner's playtest of 4 November 2026 (finding 12): the Supply Drop Zone is an <c>OCLUpdate</c> that calls in a cargo plane every two minutes, which drops three crates in chutes
/// onto the building, and the building takes them (<c>MoneyCrateCollide</c> with <c>BuildingPickup</c>). A crate is a thing with an <c>InactiveBody</c>, effectively dead from its first moment: the
/// chute that carried it threw it out as a dead rider and killed itself, so the crates stayed at the plane's start point outside the map; and a crate that left its chute on a building touched
/// nothing, since only movers are collision-checked.
/// </summary>
static partial class Tests
{
    public static void RealSupplyDropZoneDropsItsCrates(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
            var terrain = TerrainMap.Build(SyntheticMaps.Build(160, 160, 1, (_, _) => 0), "sdz");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "sdz", fingerprint, terrain.Fingerprint), catalog, terrain);
            var feeder = new Feeder(world);
            Check.That(feeder.Spawn(1, "AmericaSupplyDropZone", owner: 1, x: 800, y: 800).Accepted, $"{rate}: the zone spawns.");
            world.Step();
            var zone = world.FindEntity(new EntityId(1))!;
            var timer = zone.Modules.OfType<OCLUpdate>().Single();
            var money = world.MoneyOf(new PlayerId(1));
            long? called = null;
            var collected = 0;
            var destroyedChutes = 0;
            var planeGone = false;
            var offMap = false;
            for (var index = 0; index < world.Frames(30 * 60 * 4) && !(planeGone && destroyedChutes == 3); index++)
            {
                var step = world.Step();
                foreach (var e in step.Events)
                {
                    if (e.Kind == SimEventKind.CreationListRequested && called is null) called = world.Tick;
                    if (e.Kind == SimEventKind.CrateCollected) collected++;
                    if (e.Kind == SimEventKind.EntityDestroyed && e.Text == "AmericaCrateParachute") destroyedChutes++;
                    if (e.Kind == SimEventKind.EntityDestroyed && e.Text == "AmericaJetCargoPlane_SupplyDropZone") planeGone = true;
                }
                foreach (var crate in world.Entities.Where(value => value.Template.Name == "SupplyDropZoneCrate" && !value.ContainedBy.IsValid))
                    if (crate.X < Fixed.Zero || crate.X > Fixed.FromInt(1600)) offMap = true;
            }
            Check.That(called is not null, $"{rate}: the zone called its plane.");
            var seconds = (decimal)called!.Value / world.Time.TicksPerSecond;
            Check.That(seconds >= 119 && seconds <= 122, $"{rate}: the plane comes after the two minutes of the data ({seconds:F1} s).");
            Check.That(!offMap, $"{rate}: no crate stays off the map at the plane's start point.");
            Check.That(collected == 3, $"{rate}: the building takes its three crates ({collected}).");
            Check.That(world.MoneyOf(new PlayerId(1)) - money == Fixed.FromInt(1500), $"{rate}: they are worth 1,500 together ({world.MoneyOf(new PlayerId(1)) - money}).");
            Check.That(destroyedChutes == 3 && planeGone, $"{rate}: the chutes are gone and the plane leaves ({destroyedChutes}).");
            Check.That(!world.Entities.Any(value => value.Template.Name == "SupplyDropZoneCrate"), $"{rate}: no crate is left lying.");
            Check.That(timer.NextCreationTick > world.Tick, $"{rate}: the next drop is a whole delay away again.");
        }
    }
}
