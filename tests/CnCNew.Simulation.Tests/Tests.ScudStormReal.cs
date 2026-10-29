using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
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
/// Real data, F-64 step 4: the Scud Storm's power is an <c>OCLSpecialPower</c> whose list <c>SUPERWEAPON_ScudStorm</c> holds one <c>Attack</c> nugget (<c>AttackNugget::create</c>: the
/// site locks its primary weapon and attacks the target for nine shots). Before, the nugget was counted as not simulated and the site never fired.
/// </summary>
static partial class Tests
{
    public static void RealScudStormLaunches(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate);
            var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
            var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
            var terrain = TerrainMap.Build(SyntheticMaps.Build(160, 160, 1, (_, _) => 0), "scud");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "scud", fingerprint, terrain.Fingerprint), catalog, terrain);
            var feeder = new Feeder(world);
            Check.That(feeder.Spawn(1, "GLAScudStorm", owner: 1, x: 300, y: 600).Accepted, "The Scud Storm spawns.");
            Check.That(feeder.Spawn(2, "ChinaTankBattleMaster", owner: 2, x: 1300, y: 600).Accepted, "The target spawns.");
            feeder.SetRelationship(1, 2, Relationship.Enemies);
            feeder.SetRelationship(2, 1, Relationship.Enemies);
            world.RevealMapPermanently(new PlayerId(1), true);
            world.Step();
            var site = world.FindEntity(new EntityId(1))!;
            foreach (var module in site.Modules.OfType<ISpecialPowerModule>()) module.SetReadyFrame(world.Tick);
            var order = feeder.UseSpecialPower(1, 1, "SuperweaponScudStorm", SpecialPowerTargetKind.Location, x: 1300, y: 600);
            Check.That(order.Accepted, $"{rate}: the launch is accepted ({order.Reason}).");
            var shots = 0;
            for (var index = 0; index < world.Frames(900); index++)
                shots += world.Step().Events.Count(value => value.Kind == SimEventKind.WeaponFired && value.Entity == site.Id);
            Check.That(shots == 9, $"{rate}: the site launches its nine missiles at the target ({shots}).");
            Check.That(!world.IsAttackingPlace(site), $"{rate}: and is done.");
        }
    }
}
