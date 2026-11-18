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
    /// Real data (#454): the built-in AI of every GLA general builds its Palace. The original raises it by its skirmish scripts (<c>GLA Palace - N</c>, <c>- H</c>) once the Arms Dealer
    /// stands and the <c>_ESCALATION</c> counter has reached 30 (five game minutes); the built-in AI had no goal that led to one, so the Battle Bus, the Scud launcher, the bomb truck,
    /// the rocket buggy and the Palace's upgrades stayed locked. In <see cref="BusMap"/> against America the plain GLA player with the standard money owns a finished Palace within
    /// <see cref="PalaceMinutes"/> game minutes at 60 Hz (measured: 7.8, 12.3 while its savings first bought a second Arms Dealer; it saves for it only while its army is as strong as an attack wave), and with <see cref="BusMoney"/> credits
    /// each of the four GLA sides owns the Palace of its own side within that time at 60 Hz, the plain GLA player also at 30 and 120 Hz; none begins one before five game minutes. At
    /// 60 Hz the rich plain GLA player then queues and owns a Battle Bus within <see cref="PalaceBusMinutes"/> - the whole way from an empty base (a bus comes only with the
    /// composition's variety share, so its time depends on the draws; measured at 8.9 game minutes with this seed, 21 before).
    /// </summary>
    public static void RealSkirmishAiBuildsPalace(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        MapData map;
        using (var stream = content.OpenRead(BusMap)) map = SageMapDecoder.Decode(stream);
        var cases = new List<(string Faction, SimulationRate Rate, bool Rich, bool Bus)>();
        cases.Add(("FactionGLA", SimulationRate.Hz60, false, false));
        foreach (var faction in new[] { "FactionGLA", "FactionGLAToxinGeneral", "FactionGLADemolitionGeneral", "FactionGLAStealthGeneral" }) cases.Add((faction, SimulationRate.Hz60, true, faction == "FactionGLA"));
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz120 }) cases.Add(("FactionGLA", rate, true, false));
        foreach (var (faction, rate, rich, withBus) in cases)
        {
            var label = $"{faction} at {rate}{(rich ? $", {BusMoney} credits" : "")}";
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: RuleProfile.Product);
            var everyone = ReferenceMatchScenario.Participants(catalog, out _);
            var terrain = TerrainMap.Build(map, BusMap);
            var standard = SkirmishMatchOptions.StandardAt(catalog.Rate) with { AiSeed = BusSeed, WorldSeed = BusSeed ^ 0xC0FFEE06 };
            var session = SkirmishMatchSession.Start(rich ? standard with { StartMoney = BusMoney } : standard, catalog, fingerprint, terrain, map.Objects,
                [new SkirmishSlot(everyone.Single(value => value.Faction.Name == faction)), new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionAmerica"))]);
            var world = session.World;
            var gla = session.Players[0].Id;
            var side = everyone.Single(value => value.Faction.Name == faction).Side;
            var ai = session.Ais[0] ?? throw new InvalidOperationException($"{label}: the GLA slot has no built-in AI.");
            var palaces = catalog.Templates.Where(template => template.IsKindOf("FS_ADVANCED_TECH") && template.Side.Equals(side, StringComparison.OrdinalIgnoreCase)).ToList();
            Check.That(palaces.Count > 0 && palaces.All(template => template.Name.EndsWith("GLAPalace", StringComparison.Ordinal)), $"{label}: side {side} has a Palace ({string.Join(", ", palaces.Select(template => template.Name))}).");
            var earliest = rate.Time.OriginalFrames(30 * 60 * 5);
            var end = rate.Time.OriginalFrames(30 * 60 * (withBus ? PalaceBusMinutes : PalaceMinutes));
            long begun = 0, finished = 0, bus = 0;
            while (world.Tick < end && !session.IsOver && (withBus ? bus == 0 : finished == 0))
            {
                session.Step();
                foreach (var entity in world.Entities)
                {
                    if (entity.Owner != gla || entity.EffectivelyDead) continue;
                    if (palaces.Contains(entity.Template))
                    {
                        if (begun == 0) begun = world.Tick;
                        if (finished == 0 && !entity.HasStatus(ObjectStatusBits.UnderConstruction)) finished = world.Tick;
                    }
                    else if (bus == 0 && entity.Template.Name.EndsWith("GLAVehicleBattleBus", StringComparison.Ordinal)) bus = world.Tick;
                }
            }
            var seconds = (long tick) => tick == 0 ? "never" : $"{tick / rate.Time.OriginalFrames(30)} s";
            Console.WriteLine($"    {label}: Palace begun {seconds(begun)}, finished {seconds(finished)}, Battle Bus {seconds(bus)}, {world.Tick} ticks.");
            Check.That(begun >= earliest, $"{label}: the Palace was begun at {seconds(begun)}, not before five game minutes.");
            Check.That(finished > 0, $"{label}: the GLA computer player owned a finished Palace within {(withBus ? PalaceBusMinutes : PalaceMinutes)} game minutes (begun {seconds(begun)}).");
            if (withBus)
            {
                var queued = ai.Stats.ArmyProduction.Where(pair => pair.Key.EndsWith("\tqueued:GLAVehicleBattleBus", StringComparison.Ordinal)).Sum(pair => pair.Value);
                Check.That(queued > 0 && bus > finished, $"{label}: after its Palace ({seconds(finished)}) the GLA computer player queued {queued} Battle Buses and owned one at {seconds(bus)}.");
            }
        }
    }

    /// <summary>The game minutes within which each GLA side of <see cref="RealSkirmishAiBuildsPalace"/> must own its Palace.</summary>
    private const int PalaceMinutes = 13;

    /// <summary>The game minutes within which the GLA player of <see cref="RealSkirmishAiBuildsPalace"/> with <see cref="BusMoney"/> credits must own its Palace and then a Battle Bus.</summary>
    private const int PalaceBusMinutes = 26;
}
