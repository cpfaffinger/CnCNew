using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;

static partial class Tests
{
    /// <summary>
    /// Real data (#434): the built-in AI builds combat bikes. A bike is made with its <c>InitialPayload</c> rider inside, and the rider's line of <c>RiderChangeContain</c> gives it
    /// its weapon set (<c>RiderChangeContain::onContaining</c>); its default set is unarmed, so the AI took it for no army unit and never built one. Every buildable bike whose initial
    /// rider arms it is an army unit to the AI, and in <see cref="BikeMap"/> with seed <see cref="BikeSeed"/> the GLA player has queued a combat bike and owns one with its rider
    /// within <see cref="BikeMinutes"/> game minutes. At 30, 60 and 120 Hz.
    /// <para>
    /// A bike comes with the composition's draw, so when it comes depends on the seed. With seed 1 the first bike was queued at 7.6 game minutes until the Palace (#454) was
    /// planned from minute five; saving for it changes every later draw, and the first bike of seed 1 came at 12.6 minutes. Over 36 seeds (30 Hz, the first ten minutes) the
    /// AI queued 53 bikes of 392 vehicles without the Palace goal and 43 of 375 with it, a bike in 26 and 20 of the 36 matches: the Palace's price is paid mostly by infantry
    /// (2,494 against 2,218), the share of bikes among the vehicles stays within chance. <see cref="BikeSeed"/> 10 queues its first bike at 2.5 minutes at every rate, before
    /// the Palace is planned, so the test asks what it is about - a bike is an army unit and is built - and not the timing of one draw.
    /// </para>
    /// </summary>
    public static void RealSkirmishAiBuildsCombatBikes(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        MapData map;
        using (var stream = content.OpenRead(BikeMap)) map = SageMapDecoder.Decode(stream);
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: RuleProfile.Product);
            var everyone = ReferenceMatchScenario.Participants(catalog, out _);
            var session = SkirmishMatchSession.Start(SkirmishMatchOptions.StandardAt(catalog.Rate) with { AiSeed = BikeSeed, WorldSeed = BikeSeed ^ 0xC0FFEE06 }, catalog, fingerprint, TerrainMap.Build(map, BikeMap), map.Objects,
                [new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionGLA")), new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionAmerica"))]);
            var ai = session.Ais[0] ?? throw new InvalidOperationException("The GLA slot has no built-in AI.");
            var bike = catalog.Templates.Single(template => template.Name == "GLAVehicleCombatBike");
            var bikes = catalog.Templates.Where(template => template.BuildCost > CnCNew.Simulation.Numerics.Fixed.Zero && template.BuildTicks > 0
                && template.Modules.Any(module => module.Data is RiderChangeContainData { InitialPayload.Length: > 0 })).ToList();
            Check.That(bikes.Contains(bike), $"{rate}: GLAVehicleCombatBike is a buildable template with a rider it is made with.");
            foreach (var template in bikes)
                Check.That(ai.DescribeRoles(template).Contains("Army", StringComparison.Ordinal), $"{rate}: {template.Name}, armed by the rider it is made with, is an army unit to the AI ({ai.DescribeRoles(template)}).");
            var world = session.World;
            var gla = session.Players[0].Id;
            var owned = 0L;
            var end = rate.Time.OriginalFrames(30 * 60 * BikeMinutes);
            while (world.Tick < end && !session.IsOver && owned == 0)
            {
                session.Step();
                foreach (var entity in world.Entities)
                    if (entity.Owner == gla && !entity.EffectivelyDead && entity.Template == bike && entity.Container is { ContainCount: > 0 } && entity.RiderWeaponBit >= 0) { owned = world.Tick; break; }
            }
            var queued = ai.Stats.ArmyProduction.Where(pair => pair.Key.EndsWith("\tqueued:GLAVehicleCombatBike", StringComparison.Ordinal)).Sum(pair => pair.Value);
            Check.That(queued > 0 && owned > 0, $"{rate}: the GLA computer player queued {queued} combat bikes and owned {(owned > 0 ? "one with its rider at tick " + owned : "none")} within {BikeMinutes} game minutes.");
        }
    }

    private const string BikeMap = "Maps/Alpine Assault/Alpine Assault.map";
    private const uint BikeSeed = 10;

    /// <summary>The game minutes within which the GLA player of <see cref="RealSkirmishAiBuildsCombatBikes"/> must own a combat bike.</summary>
    private const int BikeMinutes = 10;
}
