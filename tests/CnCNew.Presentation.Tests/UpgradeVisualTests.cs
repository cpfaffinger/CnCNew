using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
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
        t.Run("every SubObjectsUpgrade names real sub-objects and overrides them once its upgrade ran", () => SubObjectsUpgrades(rig));
        t.Run("a bomb truck draws the load its upgrades chose", () => BombTruckLoads(rig));
        t.Run("every ModelConditionUpgrade and RadarUpgrade changes a state of the object", () => ConditionUpgrades(rig));
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
            // The renderer can build what the state names: the model has drawn triangles and the extension animation binds to its skeleton.
            var library = new CnCNew.W3D.W3dAssetLibrary(path => rig.Content.Exists(path) ? rig.Content.OpenRead(path) : null);
            var audit = CnCNew.W3D.W3dModelDrawAudit.Analyze(library.GetModel(before.Model ?? "none"));
            Assert(audit.DrawnTriangles > 0 && audit.LostTriangles == 0, $"{plant}: the rods model {before.Model} draws triangles ({audit.DrawnTriangles})");
            Assert(before.Model is not null && before.Animations.IsEmpty, $"{plant}: a reactor without the upgrade shows its retracted rods ({before.Model ?? "no model"})");
            var retracted = before.Model;

            rig.World.CompleteUpgrade(entity.Owner, entity.Id, rig.UpgradeOf(visual, plant));
            var extending = rig.StateOf(entity, visual, draw);
            Assert(extending.Animations.All(animation => library.GetAnimation(animation.Name) is not null), $"{plant}: the extension animation {extending.Animations.FirstOrDefault()?.Name} binds to the rods model");
            Assert(extending.Model == retracted && !extending.Animations.IsEmpty && extending.Mode == AnimationMode.Once && extending.Flags.HasFlag(AnimationFlags.StartFrameFirst),
                $"{plant}: the upgrade plays the extension once from its first frame ({extending.Model}, {extending.Mode}, {extending.Flags})");

            for (var tick = 0; tick < TestTime.Frames(rig.World, 60); tick++) rig.World.Step();
            var extended = rig.StateOf(entity, visual, draw);
            Assert(extended.Model == retracted && !extended.Animations.IsEmpty && extended.Mode == AnimationMode.Manual && extended.Flags.HasFlag(AnimationFlags.StartFrameLast),
                $"{plant}: once the rods are out they stand on the last frame ({extended.Model}, {extended.Mode}, {extended.Flags})");
        }
    }

    private static IEnumerable<string> Words(ModuleConfiguration module, string key)
        => module.Fields.Where(field => field.Name.Equals(key, StringComparison.OrdinalIgnoreCase)).SelectMany(field => ValueGrammar.Tokenize(field.Value.OriginalText));

    private static IEnumerable<(ObjectDefinition Definition, ModuleConfiguration Module)> ModulesOf(Rig rig, params string[] names)
        => rig.Graph.Definitions.OfType<ObjectDefinition>().OrderBy(definition => definition.OriginalName, StringComparer.Ordinal)
            .SelectMany(definition => definition.Modules.Where(module => names.Contains(module.Name, StringComparer.OrdinalIgnoreCase)).Select(module => (definition, module)));

    /// <summary>
    /// Every <c>SubObjectsUpgrade</c> of the shipped data (19 modules: the bomb trucks and the Helix): the sub-objects it names exist in a model of the object, and once its upgrade is granted the drawable
    /// shows and hides them over the state's own lines, which hide them (<c>HideSubObject = Bombload02 ...</c>, <c>HideSubObject = BombWing</c>).
    /// </summary>
    private static void SubObjectsUpgrades(Rig rig)
    {
        var library = new CnCNew.W3D.W3dAssetLibrary(path => rig.Content.Exists(path) ? rig.Content.OpenRead(path) : null);
        var checkedModules = 0;
        foreach (var (definition, module) in ModulesOf(rig, "SubObjectsUpgrade"))
        {
            var show = Words(module, "ShowSubObjects").ToArray();
            var hide = Words(module, "HideSubObjects").ToArray();
            var name = $"{definition.OriginalName}/{module.Tag}";
            var visual = rig.Visuals[definition.OriginalName];
            Assert(visual.SubObjectUpgrades.Any(spec => spec.Tag == module.Tag && spec.Show.SequenceEqual(show) && spec.Hide.SequenceEqual(hide)), $"{name}: the visual keeps the module's names");

            var parts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var model in visual.Draws.SelectMany(draw => draw.States).Select(state => state.Model).Where(model => model is not null).Distinct())
                foreach (var part in library.GetModel(model!).SubObjects)
                {
                    parts.Add(part.Name);
                    if (part.Name.IndexOf('.') is var dot and >= 0) parts.Add(part.Name[(dot + 1)..]);
                }
            var unknown = show.Concat(hide).Where(part => !parts.Contains(part)).ToArray();
            Assert(unknown.Length == 0, $"{name}: no model of the object has the sub-object(s) {string.Join(", ", unknown)}");

            rig.Reset();
            var entity = rig.Spawn(definition.OriginalName);
            Assert(SubObjectOverrides.Of(entity, visual).IsDefaultOrEmpty, $"{name}: nothing is overridden before the upgrade");
            foreach (var trigger in Words(module, "TriggeredBy")) rig.World.CompleteUpgrade(entity.Owner, entity.Id, trigger);
            var overrides = SubObjectOverrides.Of(entity, visual);
            Assert(show.Concat(hide).All(part => overrides.Any(entry => entry.SubObject.Equals(part, StringComparison.OrdinalIgnoreCase))), $"{name}: the upgrade overrides every sub-object it names");
            // The last module that ran decides the sub-objects it names (the drawable keeps one answer per name).
            var last = entity.Modules.OfType<SubObjectsUpgrade>().Last(upgrade => upgrade.Executed);
            var lastSpec = visual.SubObjectUpgrades.Single(spec => spec.Tag == last.Tag);
            Assert(lastSpec.Show.All(part => overrides.Any(entry => entry.SubObject.Equals(part, StringComparison.OrdinalIgnoreCase) && !entry.Hide))
                   && lastSpec.Hide.All(part => overrides.Any(entry => entry.SubObject.Equals(part, StringComparison.OrdinalIgnoreCase) && entry.Hide)), $"{name}: the last module that ran decides its own names");
            checkedModules++;
        }
        Assert(checkedModules == 19, $"19 SubObjectsUpgrade modules are checked ({checkedModules})");
    }

    /// <summary>The bomb truck chooses its load by two upgrades; the part of the model that is drawn follows the module that ran last, over the state's own HideSubObject line.</summary>
    private static void BombTruckLoads(Rig rig)
    {
        string[] loads = ["Bombload01", "Bombload02", "Bombload03", "Bombload04"];
        string[] Shown(Entity truck, ObjectVisual visual)
        {
            var draw = visual.Draws.Single(candidate => candidate.States.Any(state => state.SubObjects.Any(entry => entry.SubObject.Equals("bombload02", StringComparison.OrdinalIgnoreCase))));
            var state = rig.StateOf(truck, visual, draw);
            var overrides = SubObjectOverrides.Of(truck, visual);
            return loads.Where(part => SubObjectOverrides.IsVisible("BombTruck." + part, defaultVisible: true, state.SubObjects, overrides)).ToArray();
        }

        rig.Reset();
        var visual = rig.Visuals["GLAVehicleBombTruck"];
        var truck = rig.Spawn("GLAVehicleBombTruck");
        Assert(Shown(truck, visual).SequenceEqual(["Bombload01"]), $"a truck without upgrades carries the plain load ({string.Join(' ', Shown(truck, visual))})");
        rig.World.CompleteUpgrade(truck.Owner, truck.Id, "Upgrade_GLABombTruckBioBomb");
        Assert(Shown(truck, visual).SequenceEqual(["Bombload02"]), $"the anthrax load ({string.Join(' ', Shown(truck, visual))})");
        rig.World.CompleteUpgrade(truck.Owner, truck.Id, "Upgrade_GLABombTruckHighExplosiveBomb");
        Assert(Shown(truck, visual).SequenceEqual(["Bombload04"]), $"both upgrades: the combined load ({string.Join(' ', Shown(truck, visual))})");

        rig.Reset();
        var other = rig.Spawn("GLAVehicleBombTruck");
        rig.World.CompleteUpgrade(other.Owner, other.Id, "Upgrade_GLABombTruckHighExplosiveBomb");
        Assert(Shown(other, visual).SequenceEqual(["Bombload03"]), $"the high-explosive load alone ({string.Join(' ', Shown(other, visual))})");
    }

    /// <summary>
    /// Every <c>ModelConditionUpgrade</c> (the fortified-structure visuals of the GLA buildings, the Scud Storm's and the Internet Center's USER_n): the upgrade sets its flag, and at least one draw module of the
    /// object selects another state for it. Every <c>RadarUpgrade</c> on an object that has radar states shows the dish extending and then extended.
    /// </summary>
    private static void ConditionUpgrades(Rig rig)
    {
        var modelConditionModules = 0;
        foreach (var (definition, module) in ModulesOf(rig, "ModelConditionUpgrade"))
        {
            var flag = Words(module, "ConditionFlag").Single();
            var name = $"{definition.OriginalName}/{module.Tag}";
            var visual = rig.Visuals[definition.OriginalName];
            rig.Reset();
            var entity = rig.Spawn(definition.OriginalName);
            var before = visual.Draws.Select(draw => rig.StateIndexOf(entity, visual, draw)).ToArray();
            Assert(!ConditionDeriver.Derive(entity, rig.World, visual, default).Flags().Any(candidate => candidate.Name == flag), $"{name}: {flag} is not set before the upgrade");
            foreach (var trigger in Words(module, "TriggeredBy")) rig.World.CompleteUpgrade(entity.Owner, entity.Id, trigger);
            Assert(ConditionDeriver.Derive(entity, rig.World, visual, default).Flags().Any(candidate => candidate.Name == flag), $"{name}: the upgrade sets {flag}");
            var after = visual.Draws.Select(draw => rig.StateIndexOf(entity, visual, draw)).ToArray();
            Assert(!before.SequenceEqual(after), $"{name}: no draw module has a state of its own for {flag}");
            modelConditionModules++;
        }
        Assert(modelConditionModules == 70, $"{70} ModelConditionUpgrade modules are checked ({modelConditionModules})");

        var docks = 0;
        foreach (var (definition, module) in ModulesOf(rig, "RadarUpgrade"))
        {
            var visual = rig.Visuals[definition.OriginalName];
            var radar = visual.Draws.Where(draw => draw.States.Any(state => state.Conditions.Any(set => set.Contains(ModelConditions.RadarExtending)))).ToArray();
            if (radar.Length == 0) continue;
            rig.Reset();
            var entity = rig.Spawn(definition.OriginalName);
            var before = radar.Select(draw => rig.StateIndexOf(entity, visual, draw)).ToArray();
            foreach (var trigger in Words(module, "TriggeredBy")) rig.World.CompleteUpgrade(entity.Owner, entity.Id, trigger);
            var extending = radar.Select(draw => rig.StateIndexOf(entity, visual, draw)).ToArray();
            for (var i = 0; i < TestTime.Frames(rig.World, 600); i++) rig.World.Step();
            var extended = radar.Select(draw => rig.StateIndexOf(entity, visual, draw)).ToArray();
            Assert(!before.SequenceEqual(extending) && !extending.SequenceEqual(extended), $"{definition.OriginalName}: the radar dish extends and then stands ({string.Join(',', before)} > {string.Join(',', extending)} > {string.Join(',', extended)})");
            docks++;
        }
        Assert(docks == 5, $"five China Command Centers show their radar dish ({docks})");
    }

    internal sealed class Rig
    {
        private readonly Func<SimulationWorld> _newWorld;
        private ulong _sequence;
        private int _spawned;

        /// <summary>A world of its own: a player-scoped upgrade granted in one is the player's for good, and the next object would be born with it.</summary>
        public void Reset()
        {
            World = _newWorld();
            _sequence = 0;
            _spawned = 0;
        }

        public Rig(string directory)
        {
            var content = Content = ContentFileSystem.Open(directory);
            Graph = DefinitionCompiler.Compile(new Source(content), ModuleRegistry.Default,
                DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude });
            var templates = TemplateCatalog.Build(Graph, ModuleBindingTable.Default, TestRate.Default, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(Graph);
            ImmutableArray<PlayerConfiguration> players = [new(new PlayerId(1), "East", "FactionAmerica")];
            _newWorld = () => new SimulationWorld(new WorldConfiguration(new WorldId(1), TestRate.Default, 1, players, "upgrade-visual", fingerprint), templates);
            World = _newWorld();
            Visuals = ObjectVisualCatalog.Build(Graph);
        }

        public ContentFileSystem Content { get; }
        public int StateIndexOf(Entity entity, ObjectVisual visual, DrawSpec draw) => BestStateSelector.FindBest(draw, ConditionDeriver.Derive(entity, World, visual, default));

        public CompiledDefinitionGraph Graph { get; }
        public SimulationWorld World { get; private set; }
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
