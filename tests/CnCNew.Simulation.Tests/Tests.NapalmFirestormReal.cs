using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Real data, F-109 and the staged scene <c>acc-firestorm</c>: five MiGs that fly at a power plant and fire their napalm (<c>NapalmMissileWeapon</c>, a clip of two) make ten hits close
/// together, and eight of them within three seconds set off <c>HistoricBonusWeapon</c>, whose <c>FireOCL</c> makes a <c>FirestormSmall</c>. The synthetic test of the same rule
/// (<c>ThreeHitsOfNapalmMakeAFirestorm</c>) cannot see a missile that misses: the scene had MiGs that began in range and facing away, their missiles curved into the ground far from the
/// plant, and since the fire cycle of F-140 moved the shots by a few frames no eight of them fell within a hundred units of each other.
/// </summary>
static partial class Tests
{
    public static void RealNapalmMakesAFirestorm(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        // Three MiGs fire six napalm missiles, five fire ten: the bonus needs eight within three seconds.
        foreach (var (rate, count) in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 }.SelectMany(rate => new[] { 3, 5 }.Select(count => (rate, count))))
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "China"), new PlayerConfiguration(new PlayerId(2), "America"));
            var terrain = TerrainMap.Build(SyntheticMaps.Build(160, 160, 1, (_, _) => 0), "napalm");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "napalm", fingerprint, terrain.Fingerprint), catalog, terrain);
            var feeder = new Feeder(world);
            // The MiGs begin well outside their range of 320 (the scene's lesson): they fly at the plant and fire facing it, so every missile falls on it.
            for (var index = 0; index < count; index++) Check.That(feeder.Spawn(1, "ChinaJetMIG", owner: 1, x: 200, y: 560 + 20 * index).Accepted, "A MiG spawns.");
            Check.That(feeder.Spawn(2, "AmericaPowerPlant", owner: 2, x: 1000, y: 600).Accepted, "The plant spawns.");
            feeder.SetRelationship(1, 2, Relationship.Enemies);
            feeder.SetRelationship(2, 1, Relationship.Enemies);
            world.RevealMapPermanently(new PlayerId(1), true);
            world.Step();
            var plant = world.Entities.Single(value => value.Template.Name == "AmericaPowerPlant");
            var migs = world.Entities.Where(value => value.Template.Name == "ChinaJetMIG").ToArray();
            foreach (var mig in migs) Check.That(feeder.Pursue(1, mig.Id.Value, plant.Id.Value, 1000, 600).Accepted, $"{rate}: the order to attack is accepted.");
            var shots = 0;
            var storm = false;
            for (var index = 0; index < world.Frames(900) ; index++)
            {
                // The attack run of the game layer (StandingOrders.UpdateAttack): the jet flies at its victim and asks for a shot every frame; the pre-check refuses it out of range or while the weapon reloads.
                if (world.Time.IsOriginalFrameBoundary(world.Tick))
                    foreach (var mig in migs) feeder.FireWeapon(1, mig.Id.Value, plant.Id.Value);
                shots += world.Step().Events.Count(value => value.Kind == SimEventKind.WeaponFired && migs.Any(mig => mig.Id == value.Entity));
                storm |= world.Entities.Any(value => value.Template.Name.Contains("Firestorm", StringComparison.OrdinalIgnoreCase));
            }
            Check.That(shots == 2 * count, $"{rate}: {count} MiGs fire their clips ({shots} shots).");
            Check.That(storm == (count == 5), $"{rate}: the napalm of {count} MiGs {(count == 5 ? "makes" : "does not make")} a firestorm ({shots} shots fired).");
        }
    }
}
