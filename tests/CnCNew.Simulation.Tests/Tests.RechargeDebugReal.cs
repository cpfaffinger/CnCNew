using System.Collections.Immutable;
using System.Linq;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Real data, the debug option "faster recharge of special powers" (#343): a Particle Uplink Cannon (a shared-timer superweapon, 240 s) and the command center's timed powers (a building's own, not a superweapon) of a human
/// and of a computer player recharge in the reduced time at 30, 60 and 120 Hz - for everybody, or for the human only with the computer's at its normal time. The reduced time is whole frames of
/// the original, rounded up.
/// </summary>
static partial class Tests
{
    public static void RealSpecialPowersRechargeFasterWithTheOption(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            foreach (var (percent, humansOnly) in new[] { (100, false), (50, false), (25, true), (10, false), (10, true) })
            {
                var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "Human"), new PlayerConfiguration(new PlayerId(2), "Computer", IsComputer: true));
                var terrain = TerrainMap.Build(SyntheticMaps.Build(160, 160, 1, (_, _) => 0), "sw");
                var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "sw", fingerprint, terrain.Fingerprint), catalog, terrain);
                if (percent != 100) world.ConfigureMatchOptions(100, 0, 0, false, humansOnly, false, percent);
                var feeder = new Feeder(world);
                foreach (var template in new[] { "AmericaParticleCannonUplink", "AmericaCommandCenter" })
                    foreach (var owner in new[] { 1, 2 })
                        Check.That(feeder.Spawn(owner, template, owner: owner, x: 400 + owner * 300, y: template == "AmericaCommandCenter" ? 1200 : 400).Accepted, $"{rate} {template}: spawns for {owner}.");
                world.Step();
                var checkedPowers = 0;
                foreach (var entity in world.Entities.Where(value => value.Template.Name is "AmericaParticleCannonUplink" or "AmericaCommandCenter"))
                {
                    foreach (var module in entity.Modules.OfType<SpecialPowerModuleBase>().Where(value => world.SpecialPowers[value.PowerIndex].ReloadTicks > 0))
                    {
                    var normal = world.InFrames(world.SpecialPowers[module.PowerIndex].ReloadTicks);
                    var scaled = percent == 100 || humansOnly && entity.Owner.Value == 2 ? normal : (normal * percent + 99) / 100;
                    var delay = world.InFrames(module.AvailableOnFrame(world) - entity.SpawnTick);
                    Check.That(Math.Abs(delay - scaled) <= 1, $"{rate} {entity.Template.Name} of player {entity.Owner.Value} at {percent} % {(humansOnly ? "for the human only" : "for everybody")}: recharges in {delay} frames, expected {scaled} (normal {normal}).");
                    checkedPowers++;
                    }
                }
                Check.That(checkedPowers >= 4, $"{rate}: both buildings of both players have timed powers ({checkedPowers}).");
            }
        }
    }
}
