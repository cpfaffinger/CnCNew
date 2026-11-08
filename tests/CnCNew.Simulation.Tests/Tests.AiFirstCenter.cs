using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Views;
using CnCNew.Simulation.World;
using CnCNew.Simulation.Rules;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;

static partial class Tests
{
    /// <summary>
    /// The owner's playtest ("the enemy stops collecting"): a computer player that spends the price of its first supply center on other things never earns again, because its income starts at
    /// that center. America against China on Alpine Assault (seeds 1 and 2) and on Bitter Winter (seed 2) ran out of money before the center was placed: its place beside the supply source is
    /// found only after a builder has walked there to look, the price was kept back only while a builder was free to try, and the army spent it in the thinks in between. Every player must
    /// have delivered supplies within six game minutes. The player's name in a failure is the faction that never earned.
    /// </summary>
    public static void RealComputerPlayersEarnFromTheirFirstCenter(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        (string Map, uint Seed, SimulationRate Rate)[] cases =
        [
            ("Maps/Alpine Assault/Alpine Assault.map", 1, SimulationRate.Hz60), ("Maps/Alpine Assault/Alpine Assault.map", 2, SimulationRate.Hz60),
            ("Maps/Bitter Winter/Bitter Winter.map", 2, SimulationRate.Hz60), ("Maps/Alpine Assault/Alpine Assault.map", 1, SimulationRate.Hz30), ("Maps/Alpine Assault/Alpine Assault.map", 1, SimulationRate.Hz120),
        ];
        foreach (var (mapPath, seed, rate) in cases)
        {
            MapData map;
            using (var stream = content.OpenRead(mapPath)) map = SageMapDecoder.Decode(stream);
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: RuleProfile.Product);
            var everyone = ReferenceMatchScenario.Participants(catalog, out _);
            var session = SkirmishMatchSession.Start(SkirmishMatchOptions.StandardAt(catalog.Rate) with { AiSeed = seed, WorldSeed = seed ^ 0xC0FFEE06 }, catalog, fingerprint, TerrainMap.Build(map, mapPath), map.Objects,
                [new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionAmerica")), new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionChina"))]);
            var delivered = new long[session.Players.Length];
            var end = rate.Time.OriginalFrames(30 * 360);
            while (session.World.Tick < end && !session.IsOver)
            {
                var result = session.Step();
                if (result.Events is not { } events) continue;
                foreach (var ev in events)
                {
                    if (ev.Kind != SimEventKind.SupplyDeposited || ev.Entity == ev.Other || session.World.FindEntity(ev.Entity) is not { } carrier) continue;
                    for (var player = 0; player < delivered.Length; player++)
                        if (session.Players[player].Id == carrier.Owner) delivered[player] += ev.Value;
                }
            }
            for (var player = 0; player < delivered.Length; player++)
                Check.That(delivered[player] > 0, $"{mapPath} seed {seed} at {rate}: {session.Players[player].Faction} delivered no supplies in six game minutes (money {session.World.MoneyOf(session.Players[player].Id).Floor()}).");
        }
    }

    /// <summary>
    /// The owner's playtest ("the enemy stops collecting"), second cause: when every supply depot a computer player has seen is empty, its carriers used to stand idle beside their centers for the
    /// rest of the match although the map still held supplies it had not seen (Alpine Assault, America against China, seed 4: from minute 23 on seven carriers idle, 16,000 credits left on the map).
    /// America against China on Alpine Assault (seed 4) for 28 game minutes: no player with a finished center and a carrier may go three minutes without a delivery while the depots
    /// that no enemy owns, with no enemy object within 450 units, hold more than 3,000 credits between them.
    /// </summary>
    public static void RealIdleCarriersFindSuppliesTheyHaveNotSeen(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        const string MapPath = "Maps/Alpine Assault/Alpine Assault.map";
        MapData map;
        using (var stream = content.OpenRead(MapPath)) map = SageMapDecoder.Decode(stream);
        var rate = SimulationRate.Hz60;
        var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: RuleProfile.Product);
        var everyone = ReferenceMatchScenario.Participants(catalog, out _);
        const uint Seed = 4;
        var session = SkirmishMatchSession.Start(SkirmishMatchOptions.StandardAt(catalog.Rate) with { AiSeed = Seed, WorldSeed = Seed ^ 0xC0FFEE06 }, catalog, fingerprint, TerrainMap.Build(map, MapPath), map.Objects,
            [new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionAmerica")), new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionChina"))]);
        var window = rate.TicksPerSecond * 180;
        var lastDelivery = new long[session.Players.Length];
        var end = rate.TicksPerSecond * 60 * 28;
        while (session.World.Tick < end && !session.IsOver)
        {
            var result = session.Step();
            var world = session.World;
            if (result.Events is { } events)
                foreach (var ev in events)
                {
                    if (ev.Kind != SimEventKind.SupplyDeposited || ev.Entity == ev.Other || world.FindEntity(ev.Entity) is not { } carrier) continue;
                    for (var player = 0; player < lastDelivery.Length; player++)
                        if (session.Players[player].Id == carrier.Owner) lastDelivery[player] = world.Tick;
                }
            if (world.Tick % (rate.TicksPerSecond * 10) != 0) continue;
            for (var player = 0; player < lastDelivery.Length; player++)
            {
                var id = session.Players[player].Id;
                if (world.IsPlayerDefeated(id) || world.Tick - lastDelivery[player] < window) continue;
                var carriers = world.Entities.Where(entity => entity.Owner == id && !entity.EffectivelyDead && entity.Template.IsKindOf("HARVESTER")).ToArray();
                var centre = world.Entities.Any(entity => entity.Owner == id && !entity.EffectivelyDead && entity.Template.IsKindOf("CASH_GENERATOR") && entity.Template.IsKindOf("STRUCTURE")
                    && !entity.HasStatus(ObjectStatusBits.UnderConstruction));
                if (carriers.Length == 0 || !centre) continue;
                var enemies = world.Entities.Where(entity => !entity.EffectivelyDead && !entity.Owner.IsSystem && world.AreEnemies(id, entity.Owner)).ToArray();
                var unguarded = world.Entities.Where(entity => entity.Template.IsKindOf("SUPPLY_SOURCE") && !entity.EffectivelyDead && entity.WarehouseSupplyBoxes() > 0
                    && enemies.All(enemy => (enemy.X - entity.X).Abs() + (enemy.Y - entity.Y).Abs() > Fixed.FromInt(450))).Sum(entity => (long)entity.WarehouseSupplyBoxes() * world.ValuePerSupplyBox);
                Check.That(unguarded <= 3000, $"tick {world.Tick}: {session.Players[player].Faction} has made no delivery for three minutes, has {carriers.Length} carriers and a center, and {unguarded} credits lie unguarded on the map.");
            }
        }
    }

    /// <summary>
    /// The owner's playtest ("the enemy stops collecting"), third cause: a computer player that has lost its carriers had too little money for the next one and no income to get it. The army buys at
    /// prices below a carrier's every think, and the structures and repairs spend what is left, so a player whose carriers a raid killed (America against GLA on Alpine Assault: seeds 1, 3 and 5 of
    /// eight) stood with a supply center, 25 to 700 credits and no carrier until the end of the match. Once a center stands, the price of one carrier stays in the bank, and a missing worker is saved
    /// for. Two assertions over those matches (a player that a raid has left without a carrier and without its price is broke, and nothing can be asked of the AI then):
    /// (1) a player with a finished supply center that could afford a carrier (its price in the bank) is never without one for 90 game seconds;
    /// (2) the army is never bought with money that takes a player with a finished center below the price of a carrier (the purchase of the carrier itself may).
    /// </summary>
    public static void RealComputerPlayersReplaceTheirLostCarriers(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        const string MapPath = "Maps/Alpine Assault/Alpine Assault.map";
        MapData map;
        using (var stream = content.OpenRead(MapPath)) map = SageMapDecoder.Decode(stream);
        var rate = SimulationRate.Hz60;
        var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: RuleProfile.Product);
        var everyone = ReferenceMatchScenario.Participants(catalog, out _);
        foreach (var seed in (uint[])[1, 5])
        {
            var session = SkirmishMatchSession.Start(SkirmishMatchOptions.StandardAt(catalog.Rate) with { AiSeed = seed, WorldSeed = seed ^ 0xC0FFEE06 }, catalog, fingerprint, TerrainMap.Build(map, MapPath), map.Objects,
                [new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionGLA")), new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionAmerica"))]);
            var prices = session.Players.Select(player => catalog.Templates.Where(template => template.IsKindOf("HARVESTER") && !template.IsKindOf("STRUCTURE") && template.BuildCost > Fixed.Zero
                && template.Name.StartsWith(SkirmishMatchScenario.FamilyPrefix(everyone.Single(value => value.Faction.Name == player.Faction).Faction), StringComparison.Ordinal)).Min(template => template.BuildCost)).ToArray();
            var affordableWithout = session.Players.Select(_ => -1L).ToArray();
            var armyBought = session.Players.Select(_ => 0).ToArray();
            var armyChecks = session.Players.Select(_ => -1L).ToArray();
            var end = rate.TicksPerSecond * 60 * 18;
            while (session.World.Tick < end && !session.IsOver)
            {
                session.Step();
                var world = session.World;
                for (var player = 0; player < prices.Length; player++)
                {
                    var id = session.Players[player].Id;
                    var bought = session.Ais[player]!.Stats.ArmyProduction.Where(pair => pair.Key.Contains("	queued:", StringComparison.Ordinal)).Sum(pair => pair.Value);
                    var armyBoughtNow = bought != armyBought[player];
                    armyBought[player] = bought;
                    if (armyBoughtNow) armyChecks[player] = world.Tick + 2;
                    if (world.Tick == armyChecks[player] && !world.IsPlayerDefeated(id) && world.MoneyOf(id) < prices[player]
                        && world.Entities.Any(entity => entity.Owner == id && !entity.EffectivelyDead && entity.Template.IsKindOf("CASH_GENERATOR") && entity.Template.IsKindOf("STRUCTURE") && !entity.HasStatus(ObjectStatusBits.UnderConstruction)))
                        Check.That(false, $"seed {seed}, tick {world.Tick}: {session.Players[player].Faction} bought army units and is left with {world.MoneyOf(id).Floor()} credits, less than a carrier ({prices[player].Floor()}).");
                    if (world.Tick % (rate.TicksPerSecond * 5) != 0) continue;
                    var centre = !world.IsPlayerDefeated(id) && world.Entities.Any(entity => entity.Owner == id && !entity.EffectivelyDead && entity.Template.IsKindOf("CASH_GENERATOR") && entity.Template.IsKindOf("STRUCTURE")
                        && !entity.HasStatus(ObjectStatusBits.UnderConstruction));
                    var money = world.MoneyOf(id);
                    if (!centre || money < prices[player] || world.Entities.Any(entity => entity.Owner == id && !entity.EffectivelyDead && entity.Template.IsKindOf("HARVESTER"))) { affordableWithout[player] = -1; continue; }
                    if (affordableWithout[player] < 0) affordableWithout[player] = world.Tick;
                    Check.That(world.Tick - affordableWithout[player] <= rate.TicksPerSecond * 90, $"seed {seed}, tick {world.Tick}: {session.Players[player].Faction} has had {money.Floor()} credits (a carrier costs {prices[player].Floor()}) and no carrier since tick {affordableWithout[player]}.");
                }
            }
        }
    }

    /// <summary>
    /// Phase 64, item 2 (docs/SKIRMISH-AI.md 7d, "open"): a computer player that has lost every carrier and has less than a carrier's price has no income and none to come, and used to stay
    /// down for the rest of the match because it never sold a building. America against China on Alpine Assault (seed 1): after four game minutes every carrier of player 0 is killed and its money
    /// set to 10 credits. Within two game minutes it must sell a building of its own (a structure takes the Sold status), never its command center or supply center, and have a carrier again.
    /// At 30, 60 and 120 Hz. The threshold of the survey this belongs to: no player with a finished supply center and no carrier for 90 game seconds in 8 seeds of every pairing (SimTool econ-survey).
    /// </summary>
    public static void RealBrokeComputerPlayerSellsABuildingForACarrier(string directory)
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
                [new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionAmerica")), new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionChina"))]);
            var world = session.World;
            var id = session.Players[0].Id;
            bool IsCarrier(Entity entity) => entity.Owner == id && !entity.EffectivelyDead && entity.Template.IsKindOf("HARVESTER") && !entity.Template.IsKindOf("STRUCTURE");
            bool IsCentre(Entity entity) => entity.Owner == id && !entity.EffectivelyDead && entity.Template.IsKindOf("CASH_GENERATOR") && entity.Template.IsKindOf("STRUCTURE")
                && !entity.HasStatus(ObjectStatusBits.UnderConstruction);
            var stage = rate.TicksPerSecond * 240;
            while (world.Tick < stage && !session.IsOver) session.Step();
            Check.That(world.Entities.Any(IsCentre), $"{rate}: player 0 has no finished supply center after four minutes.");
            foreach (var carrier in world.Entities.Where(IsCarrier).ToArray()) world.Kill(carrier.Id);
            world.SetMoney(id, Fixed.FromInt(10));
            var soldBefore = world.Entities.Count(entity => entity.Owner == id && entity.HasStatus(ObjectStatusBits.Sold));
            var sold = false;
            var carrierAgain = false;
            var end = world.Tick + rate.TicksPerSecond * 120;
            while (world.Tick < end && !session.IsOver)
            {
                session.Step();
                foreach (var entity in world.Entities)
                {
                    if (entity.Owner != id) continue;
                    if (entity.HasStatus(ObjectStatusBits.Sold))
                    {
                        sold = true;
                        Check.That((entity.Template.IsKindOf("COMMANDCENTER") || entity.Template.IsKindOf("CASH_GENERATOR")) == false, $"{rate}: player 0 sold {entity.Template.Name}.");
                    }
                    if (IsCarrier(entity) || (entity.Template.IsKindOf("HARVESTER") && !entity.Template.IsKindOf("STRUCTURE") && !entity.EffectivelyDead)) carrierAgain = true;
                }
            }
            Check.That(sold || soldBefore > 0, $"{rate}: the broke player sold no building in two game minutes (money {world.MoneyOf(id).Floor()}).");
            Check.That(carrierAgain, $"{rate}: the broke player has no carrier again after two game minutes (money {world.MoneyOf(id).Floor()}).");
        }
    }
}
