using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;
using CnCNew.Tests;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// What an upgrade does to the picture of a shipped object, on the real data (runs only with <c>--content-dir &lt;dir&gt;</c>): the control rods of the Cold Fusion Reactors are a draw module of their own whose
/// states the INI writes as <c>Model ABPWRPLANT_A1</c> (no "="), which the compiler once dropped, so no reactor ever showed its rods.
/// </summary>
internal static class UpgradeVisualTests
{
    public static int Run(string[] args)
    {
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP Upgrade visual tests (pass --content-dir <dir>)");
            return 0;
        }
        var rig = new Rig(args[index + 1]);
        var t = new TestHarness("UpgradeVisual");
        t.Run("every draw module of the shipped data compiles without an error", () => NoDrawErrors(rig));
        t.Run("the control rods of every Cold Fusion Reactor are drawn, extend and stay extended", () => ControlRods(rig));
        return t.Failures;
    }

    private static void NoDrawErrors(Rig rig)
    {
        var errors = rig.Visuals.Diagnostics.Where(diagnostic => diagnostic.Severity == "error").Select(diagnostic => $"{diagnostic.Location.Source}:{diagnostic.Location.Line} {diagnostic.Key}").ToArray();
        Assert(errors.Length == 0, $"{errors.Length} draw errors, first {string.Join(", ", errors.Take(3))}");
    }

    private static void ControlRods(Rig rig)
    {
        var plants = rig.Graph.Definitions.OfType<ObjectDefinition>()
            .Where(definition => definition.Modules.Any(module => module.Name.Equals("PowerPlantUpgrade", StringComparison.OrdinalIgnoreCase)))
            .Select(definition => definition.OriginalName).Order(StringComparer.Ordinal).ToArray();
        Assert(plants.Length == 5, $"five reactors carry the control rods upgrade ({string.Join(", ", plants)})");
        foreach (var plant in plants)
        {
            var entity = rig.Spawn(plant);
            var visual = rig.Visuals[plant];
            // The module that draws the rods is the one with states for the extension (POWER_PLANT_UPGRADING / POWER_PLANT_UPGRADED).
            var rods = visual.Draws.Where(draw => draw.States.Any(state => state.Conditions.Any(set => set.Contains(ModelConditions.PowerPlantUpgrading)))).ToArray();
            Assert(rods.Length == 1, $"{plant}: one draw module draws the rods ({rods.Length})");
            var draw = rods[0];

            var before = rig.StateOf(entity, visual, draw);
            Assert(before.Model is not null && before.Animations.IsEmpty, $"{plant}: a reactor without the upgrade shows its retracted rods ({before.Model ?? "no model"})");
            var retracted = before.Model;

            rig.World.CompleteUpgrade(entity.Owner, entity.Id, rig.UpgradeOf(visual, plant));
            var extending = rig.StateOf(entity, visual, draw);
            Assert(extending.Model == retracted && !extending.Animations.IsEmpty && extending.Mode == AnimationMode.Once && extending.Flags.HasFlag(AnimationFlags.StartFrameFirst),
                $"{plant}: the upgrade plays the extension once from its first frame ({extending.Model}, {extending.Mode}, {extending.Flags})");

            for (var tick = 0; tick < TestTime.Frames(rig.World, 60); tick++) rig.World.Step();
            var extended = rig.StateOf(entity, visual, draw);
            Assert(extended.Model == retracted && !extended.Animations.IsEmpty && extended.Mode == AnimationMode.Manual && extended.Flags.HasFlag(AnimationFlags.StartFrameLast),
                $"{plant}: once the rods are out they stand on the last frame ({extended.Model}, {extended.Mode}, {extended.Flags})");
        }
    }

    internal sealed class Rig
    {
        private ulong _sequence;
        private int _spawned;

        public Rig(string directory)
        {
            var content = ContentFileSystem.Open(directory);
            Graph = DefinitionCompiler.Compile(new Source(content), ModuleRegistry.Default,
                DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude });
            var templates = TemplateCatalog.Build(Graph, ModuleBindingTable.Default, TestRate.Default, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(Graph);
            ImmutableArray<PlayerConfiguration> players = [new(new PlayerId(1), "East", "FactionAmerica")];
            World = new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, players, "upgrade-visual", fingerprint), templates);
            Visuals = ObjectVisualCatalog.Build(Graph);
        }

        public CompiledDefinitionGraph Graph { get; }
        public SimulationWorld World { get; }
        public ObjectVisualCatalog Visuals { get; }

        public Entity Spawn(string template)
        {
            var x = 400 + 120 * (_spawned % 20);
            var y = 400 + 120 * (_spawned / 20);
            _spawned++;
            Assert(World.Submit(new SpawnCommand(PlayerId.System, ++_sequence, World.Tick, template, new PlayerId(1), Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero)).Accepted, $"{template} spawns");
            World.Step();
            return World.Entities.Where(entity => entity.Template.Name == template).OrderBy(entity => entity.Id.Value).Last();
        }

        /// <summary>The state a draw module shows for the object now.</summary>
        public ModelStateSpec StateOf(Entity entity, ObjectVisual visual, DrawSpec draw)
        {
            var set = ConditionDeriver.Derive(entity, World, visual, default);
            return draw.States[BestStateSelector.FindBest(draw, set)];
        }

        /// <summary>The upgrade that a power plant's <c>PowerPlantUpgrade</c> is triggered by.</summary>
        public string UpgradeOf(ObjectVisual visual, string template)
            => Graph.Definitions.OfType<ObjectDefinition>().Single(definition => definition.OriginalName == template).Modules
                .Single(module => module.Name.Equals("PowerPlantUpgrade", StringComparison.OrdinalIgnoreCase)).Fields
                .Single(field => field.Name.Equals("TriggeredBy", StringComparison.OrdinalIgnoreCase)).Value.OriginalText.Trim();
    }

    private sealed class Source(ContentFileSystem content) : IDefinitionSource
    {
        public IEnumerable<string> EnumerateIniFiles() => content.EnumerateFiles().Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
        public Stream OpenRead(string virtualPath) => content.OpenRead(virtualPath);
        public bool Exists(string virtualPath) => content.Exists(virtualPath);
    }
}
