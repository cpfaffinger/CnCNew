using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// Real data (issue 396): the built-in AI leaves the slaves of a spawner to their master. An angry mob (the nexus and the members its <c>SpawnBehavior</c> makes) and a
    /// stinger site (a structure whose <c>SlavedUpdate</c> soldiers it makes) of a GLA computer player: the AI leads the nexus and counts it as army, never a member or a
    /// stinger soldier, which the original's AI orders only through their master either. At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealSkirmishAiLeavesSlavesToTheirMaster(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            var terrain = TerrainMap.Build(SyntheticMaps.Build(120, 120, 1, (_, _) => 0), "ai-slaves");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "real-ai-slaves", fingerprint, terrain.Fingerprint), catalog, terrain);
            var sequence = 0UL;
            var computer = new PlayerId(1);
            void Spawn(string template, int x, int y)
                => Check.That(world.Submit(new SpawnCommand(computer, ++sequence, world.Tick, template, computer, Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, template + " spawns.");
            Spawn("GLAInfantryAngryMobNexus", 300, 300);
            Spawn("GLAStingerSite", 500, 300);
            var aiOptions = new SkirmishAiOptions(computer, 11, Fixed.FromInt(300), Fixed.FromInt(300), "GLA", string.Empty, ImmutableArray<(Fixed X, Fixed Y)>.Empty);
            var ai = new SkirmishAi(world, catalog, aiOptions, world.Submit);
            var end = world.Frames(30 * 20);
            while (world.Tick < end)
            {
                ai.Update();
                world.Step();
            }

            static bool IsSlave(Entity entity)
            {
                foreach (var module in entity.Modules)
                    if (module is ISlavedModule { Slaver.IsValid: true }) return true;
                return false;
            }
            var slaves = world.Entities.Where(entity => entity.Owner == computer && !entity.EffectivelyDead && IsSlave(entity)).ToList();
            var members = slaves.Count(entity => entity.Template.Name.Contains("AngryMob", StringComparison.Ordinal));
            var stingers = slaves.Count(entity => entity.Template.Name.Contains("Stinger", StringComparison.Ordinal));
            Check.That(members > 0 && stingers > 0, $"{rate}: the mob made its members ({members}) and the site its stinger soldiers ({stingers}).");
            var nexus = world.Entities.Single(entity => entity.Template.Name == "GLAInfantryAngryMobNexus");
            Check.That(ai.LeadsUnit(nexus), $"{rate}: the AI leads the mob's nexus.");
            foreach (var slave in slaves)
                Check.That(!ai.LeadsUnit(slave), $"{rate}: the AI does not lead the slave {slave.Template.Name} ({slave.Id.Value}).");
            var state = ai.DescribeState();
            Check.That(state.Contains(", army 1,", StringComparison.Ordinal), $"{rate}: the AI counts the nexus alone as its army ({state}).");
        }
    }
}
