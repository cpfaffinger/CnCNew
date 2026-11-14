using System.Collections.Immutable;
using System.Linq;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// The owner's playtest (#346): a China player could build the nuclear missile without a propaganda center. On the real data, for every China variant of the launcher, a dozer is
    /// refused the launcher (<c>MissingPrerequisite</c>) with no center, while the center is only being built, and after it is sold or destroyed, and is allowed it once the center stands
    /// (<c>ProductionPrerequisite::isSatisfied</c> counts the player's own finished objects of the named template).
    /// </summary>
    public static void RealNuclearMissileNeedsAPropagandaCenter(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, TestRate.Default, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
        foreach (var prefix in new[] { "", "Infa_", "Nuke_", "Tank_" })
        {
            var launcher = prefix + "ChinaNuclearMissileLauncher";
            var center = prefix + "ChinaPropagandaCenter";
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 5, players, "real-nuke", fingerprint), catalog);
            ulong sequence = 0;
            SubmitResult Send(Func<ulong, SimCommand> make) => world.Submit(make(++sequence));
            void Spawn(string name, int x, int y) => Check.That(Send(next => new SpawnCommand(new PlayerId(1), next, world.Tick, name, new PlayerId(1), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, $"{name} spawns.");
            Spawn(prefix + "ChinaCommandCenter", 1000, 1000);
            Spawn(prefix + "ChinaPowerPlant", 1200, 1000);
            Spawn(prefix + "ChinaBarracks", 1400, 1000);
            Spawn(prefix + "ChinaWarFactory", 1400, 1300);
            Spawn(prefix + "ChinaVehicleDozer", 1000, 1200);
            world.Step();
            Check.That(Send(next => new GiveMoneyCommand(PlayerId.System, next, world.Tick, new PlayerId(1), Fixed.FromInt(50000))).Accepted, "Money is granted.");
            world.Step();
            var dozer = world.Entities.First(value => value.Template.Name == prefix + "ChinaVehicleDozer");
            SubmitResult Build(string name, int x, int y) => Send(next => new ConstructCommand(new PlayerId(1), next, world.Tick, dozer.Id, name, Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero));
            long Rejections() => world.RejectionCounts[(int)CommandRejection.MissingPrerequisite];
            var before = Rejections();
            Build(launcher, 1600, 1600);
            world.Step();
            Check.That(Rejections() == before + 1 && world.Entities.All(value => value.Template.Name != launcher), $"{launcher}: refused without a propaganda center.");
            Build(center, 1600, 1000);
            var site = world.Entities.FirstOrDefault(value => value.Template.Name == center);
            for (var i = 0; i < world.Frames(900) && site is null; i++) { world.Step(); site = world.Entities.FirstOrDefault(value => value.Template.Name == center); }
            for (var i = 0; i < world.Frames(30); i++) world.Step();
            Check.That(site is not null && site.HasStatus(ObjectStatusBits.UnderConstruction), $"{center} is under construction.");
            before = Rejections();
            Build(launcher, 1800, 1600);
            world.Step();
            Check.That(Rejections() == before + 1 && world.Entities.All(value => value.Template.Name != launcher), $"{launcher}: refused while the propaganda center is only being built.");
            for (var i = 0; i < world.Frames(3000) && site!.HasStatus(ObjectStatusBits.UnderConstruction); i++) world.Step();
            Check.That(!site!.HasStatus(ObjectStatusBits.UnderConstruction), $"{center} is finished.");
            before = Rejections();
            Build(launcher, 1800, 1600);
            world.Step();
            Check.That(Rejections() == before && world.Entities.Any(value => value.Template.Name == launcher), $"{launcher}: allowed with a finished propaganda center.");
            // The center dies: its entity may linger for a few ticks, but the launcher must be refused as soon as it is dead for good.
            var launcherCount = world.Entities.Count(value => value.Template.Name == launcher);
            world.Kill(site.Id);
            for (var i = 0; i < world.Frames(1200); i++) world.Step();
            before = Rejections();
            Build(launcher, 2000, 1600);
            world.Step();
            Check.That(Rejections() == before + 1 && world.Entities.Count(value => value.Template.Name == launcher) == launcherCount, $"{launcher}: refused after the propaganda center is destroyed.");
        }
    }
}
