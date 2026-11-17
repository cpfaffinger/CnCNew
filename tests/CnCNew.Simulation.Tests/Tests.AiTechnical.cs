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
using CnCNew.Simulation.Time;

static partial class Tests
{
    /// <summary>
    /// Real data (#430): the built-in AI buys Technicals. The bare <c>GLAVehicleTechnical</c> the Arms Dealer offers has no weapon, body or locomotor of its own - an order makes one of
    /// its <c>BuildVariations</c> (<c>ThingFactory::newObject</c>) - and its chassis comment their <c>BuildCost</c> out, so the AI took neither for an army unit and never built one.
    /// Every buildable template with build variations is an army unit to the AI, and GLA against America on Alpine Assault (seed 1): the GLA player has queued a Technical and owns
    /// one of its chassis within <see cref="TechnicalMinutes"/> game minutes. At 30, 60 and 120 Hz.
    /// </summary>
    public static void RealSkirmishAiBuysTechnicals(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        const string MapPath = "Maps/Alpine Assault/Alpine Assault.map";
        MapData map;
        using (var stream = content.OpenRead(MapPath)) map = SageMapDecoder.Decode(stream);
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: RuleProfile.Product);
            var everyone = ReferenceMatchScenario.Participants(catalog, out _);
            const uint Seed = 1;
            var session = SkirmishMatchSession.Start(SkirmishMatchOptions.StandardAt(catalog.Rate) with { AiSeed = Seed, WorldSeed = Seed ^ 0xC0FFEE06 }, catalog, fingerprint, TerrainMap.Build(map, MapPath), map.Objects,
                [new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionGLA")), new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionAmerica"))]);
            var ai = session.Ais[0] ?? throw new InvalidOperationException("The GLA slot has no built-in AI.");
            var technical = catalog.Templates.Single(template => template.Name == "GLAVehicleTechnical");
            foreach (var template in catalog.Templates.Where(template => !template.BuildVariations.IsDefaultOrEmpty && template.BuildCost > CnCNew.Simulation.Numerics.Fixed.Zero && template.BuildTicks > 0))
                Check.That(ai.DescribeRoles(template).Contains("Army", StringComparison.Ordinal), $"{rate}: {template.Name}, built as one of its variations, is an army unit to the AI ({ai.DescribeRoles(template)}).");
            var world = session.World;
            var gla = session.Players[0].Id;
            var owned = 0L;
            var end = rate.Time.OriginalFrames(30 * 60 * TechnicalMinutes);
            while (world.Tick < end && !session.IsOver && owned == 0)
            {
                session.Step();
                foreach (var entity in world.Entities)
                    if (entity.Owner == gla && !entity.EffectivelyDead && entity.Template != technical && technical.IsEquivalentTo(entity.Template)) { owned = world.Tick; break; }
            }
            var queued = ai.Stats.ArmyProduction.Where(pair => pair.Key.EndsWith("\tqueued:GLAVehicleTechnical", StringComparison.Ordinal)).Sum(pair => pair.Value);
            Check.That(queued > 0 && owned > 0, $"{rate}: the GLA computer player queued {queued} Technicals and owned {(owned > 0 ? "one at tick " + owned : "none")} within {TechnicalMinutes} game minutes.");
        }
    }

    /// <summary>The game minutes within which the GLA player of <see cref="RealSkirmishAiBuysTechnicals"/> must own a Technical.</summary>
    private const int TechnicalMinutes = 10;
}
