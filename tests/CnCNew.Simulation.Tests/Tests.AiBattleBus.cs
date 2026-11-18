using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>
    /// Real data (#434): the built-in AI builds a Battle Bus once it has a Palace. The bus is made empty and fights with its passengers: its default weapon set has no primary weapon,
    /// and armed riders raise its <c>PLAYER_UPGRADE</c> set (<c>TransportContain::letRidersUpgradeWeaponSet</c>), so read by its default set it was no army unit and the AI never built
    /// one. Every Battle Bus is an army unit to the AI (a transport made with riders that arm it, the Listening Outpost, keeps its roles). In <see cref="BusMap"/> with seed
    /// <see cref="BusSeed"/> and <see cref="BusMoney"/> credits each (a bus has no value of its own in the exchange value, so it comes only with the composition's variety share and
    /// a richer player draws more often), a GLA player that is given a Palace beside its start position at tick 0 queues and owns a Battle Bus within <see cref="BusMinutes"/>
    /// game minutes, at 30, 60 and 120 Hz - and at 60 Hz the same match without the Palace never plans one, since its prerequisites (<c>GLAArmsDealer</c>, <c>GLAPalace</c>)
    /// are not met.
    /// </summary>
    public static void RealSkirmishAiBuildsBattleBuses(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        MapData map;
        using (var stream = content.OpenRead(BusMap)) map = SageMapDecoder.Decode(stream);
        foreach (var (rate, palace) in new[] { (SimulationRate.Hz30, true), (SimulationRate.Hz60, true), (SimulationRate.Hz120, true), (SimulationRate.Hz60, false) })
        {
            var label = $"{rate}{(palace ? "" : ", without a Palace")}";
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: RuleProfile.Product);
            var everyone = ReferenceMatchScenario.Participants(catalog, out _);
            var terrain = TerrainMap.Build(map, BusMap);
            var session = SkirmishMatchSession.Start(SkirmishMatchOptions.StandardAt(catalog.Rate) with { AiSeed = BusSeed, WorldSeed = BusSeed ^ 0xC0FFEE06, StartMoney = BusMoney }, catalog, fingerprint, terrain, map.Objects,
                [new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionGLA")), new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionAmerica"))]);
            var ai = session.Ais[0] ?? throw new InvalidOperationException("The GLA slot has no built-in AI.");
            var bus = catalog.Templates.Single(template => template.Name == "GLAVehicleBattleBus");
            foreach (var template in catalog.Templates.Where(template => template.Name.EndsWith("GLAVehicleBattleBus", StringComparison.Ordinal)))
                Check.That(ai.DescribeRoles(template).Contains("Army", StringComparison.Ordinal), $"{label}: {template.Name}, armed by its passengers, is an army unit to the AI ({ai.DescribeRoles(template)}).");
            foreach (var name in new[] { "ChinaVehicleListeningOutpost", "AmericaVehicleHumvee" })
                Check.That(ai.DescribeRoles(catalog.Templates.Single(template => template.Name == name)) == (name == "AmericaVehicleHumvee" ? "Army, Transport" : "Transport"),
                    $"{label}: {name} keeps its roles ({ai.DescribeRoles(catalog.Templates.Single(template => template.Name == name))}).");
            var world = session.World;
            var gla = session.Players[0].Id;
            if (palace) GivePalace(world, catalog, gla, (terrain.StartPositions[0].X, terrain.StartPositions[0].Y), label);
            var owned = 0L;
            var end = rate.Time.OriginalFrames(30 * 60 * BusMinutes);
            while (world.Tick < end && !session.IsOver && owned == 0)
            {
                session.Step();
                foreach (var entity in world.Entities)
                    if (entity.Owner == gla && !entity.EffectivelyDead && entity.Template == bus) { owned = world.Tick; break; }
            }
            var ordered = ai.Stats.ArmyProduction.Where(pair => pair.Key.Contains(":GLAVehicleBattleBus", StringComparison.Ordinal)).Sum(pair => pair.Value);
            var queued = ai.Stats.ArmyProduction.Where(pair => pair.Key.EndsWith("\tqueued:GLAVehicleBattleBus", StringComparison.Ordinal)).Sum(pair => pair.Value);
            if (palace)
                Check.That(queued > 0 && owned > 0, $"{label}: the GLA computer player queued {queued} Battle Buses and owned {(owned > 0 ? "one at tick " + owned : "none")} within {BusMinutes} game minutes.");
            else
                Check.That(ordered == 0 && owned == 0 && world.Tick >= end, $"{label}: the GLA computer player planned {ordered} Battle Buses and owned {(owned > 0 ? "one" : "none")} in {world.Tick} of {end} ticks.");
        }
    }

    /// <summary>Puts a finished Palace of <paramref name="player"/> on the first legal spot on rings around its start position, before the match's first step.</summary>
    private static void GivePalace(SimulationWorld world, TemplateCatalog catalog, CnCNew.Simulation.Ids.PlayerId player, (Fixed X, Fixed Y) start, string label)
    {
        var palace = catalog.Templates.Single(template => template.Name == "GLAPalace");
        for (var ring = 300; ring <= 700; ring += 50)
            for (var step = 0; step < 16; step++)
            {
                var angle = FixedTrig.TwoPi * step / Fixed.FromInt(16);
                var x = start.X + FixedTrig.Cos(angle) * Fixed.FromInt(ring);
                var y = start.Y + FixedTrig.Sin(angle) * Fixed.FromInt(ring);
                if (world.CheckBuildGround(palace, x, y, Fixed.Zero) != LegalBuildCode.Ok) continue;
                world.CreateNow(palace, player, x, y, world.GroundHeightAt(x, y), Fixed.Zero);
                return;
            }
        throw new InvalidOperationException($"{label}: no spot for the Palace beside the start position.");
    }

    private const string BusMap = "Maps/Alpine Assault/Alpine Assault.map";
    private const uint BusSeed = 1;

    /// <summary>The credits each player of <see cref="RealSkirmishAiBuildsBattleBuses"/> starts with.</summary>
    private const int BusMoney = 50_000;

    /// <summary>The game minutes within which the GLA player of <see cref="RealSkirmishAiBuildsBattleBuses"/> must own a Battle Bus.</summary>
    private const int BusMinutes = 6;
}
