using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
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
    /// Real data (#455): the built-in AI lets the riders of an army transport out when an enemy comes within reach only when they cannot fire from inside. On Alpine Assault
    /// (America against China, both built-in AIs), an American Humvee (<c>PassengersAllowedToFire = Yes</c>) with two Rangers inside and three Chinese Red Guards
    /// <see cref="RiderEnemyDistance"/> units away keeps both Rangers inside for <see cref="RiderSeconds"/> game seconds, and they fire from it, at 30, 60 and 120 Hz; a
    /// Chinese Troop Crawler (armed, its Red Guards may not fire from inside) with three American Rangers as close is still unloaded by its AI within that time, at 60 Hz.
    /// </summary>
    public static void RealSkirmishAiKeepsFiringRidersInside(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        MapData map;
        using (var stream = content.OpenRead(BusMap)) map = SageMapDecoder.Decode(stream);
        foreach (var (rate, firing) in new[] { (SimulationRate.Hz30, true), (SimulationRate.Hz60, true), (SimulationRate.Hz120, true), (SimulationRate.Hz60, false) })
        {
            var label = $"{rate}, {(firing ? "Humvee with Rangers" : "Troop Crawler with Red Guards")}";
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: RuleProfile.Product);
            var everyone = ReferenceMatchScenario.Participants(catalog, out _);
            var terrain = TerrainMap.Build(map, BusMap);
            var session = SkirmishMatchSession.Start(SkirmishMatchOptions.StandardAt(catalog.Rate) with { AiSeed = BusSeed, WorldSeed = BusSeed ^ 0xC0FFEE07 }, catalog, fingerprint, terrain, map.Objects,
                [new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionAmerica")), new SkirmishSlot(everyone.Single(value => value.Faction.Name == "FactionChina"))]);
            var world = session.World;
            var america = session.Players[0].Id;
            var china = session.Players[1].Id;
            var (owner, enemy) = firing ? (america, china) : (china, america);
            // The start objects are made by the match's first steps.
            for (var step = 0; step < rate.Time.OriginalFrames(30) && !(HasStructure(world, america) && HasStructure(world, china)); step++) session.Step();
            var (ownX, ownY) = StartOf(world, owner, label);
            var (enemyX, enemyY) = StartOf(world, enemy, label);
            var span = Fixed.Sqrt((enemyX - ownX) * (enemyX - ownX) + (enemyY - ownY) * (enemyY - ownY));
            var (dirX, dirY) = ((enemyX - ownX) / span, (enemyY - ownY) / span);
            var (x, y) = (ownX + dirX * Fixed.FromInt(450), ownY + dirY * Fixed.FromInt(450));
            Entity Spawn(string name, PlayerId player, Fixed atX, Fixed atY)
                => world.CreateNow(catalog.Templates.Single(template => template.Name == name), player, atX, atY, world.GroundHeightAt(atX, atY), Fixed.Zero);
            var transport = Spawn(firing ? "AmericaVehicleHumvee" : "ChinaVehicleTroopCrawler", owner, x, y);
            var foes = new List<Entity>();
            for (var index = -1; index <= 1; index++)
                foes.Add(Spawn(firing ? "ChinaInfantryRedguard" : "AmericaInfantryRanger", enemy, x + dirX * Fixed.FromInt(RiderEnemyDistance) - dirY * Fixed.FromInt(25 * index),
                    y + dirY * Fixed.FromInt(RiderEnemyDistance) + dirX * Fixed.FromInt(25 * index)));
            var riders = new List<EntityId>();
            if (firing)
                for (var index = 0; index < 2; index++)
                {
                    var ranger = Spawn("AmericaInfantryRanger", america, x, y);
                    Check.That(world.TryEnterContainer(ranger, transport), $"{label}: setup - a Ranger enters the Humvee.");
                    riders.Add(ranger.Id);
                }
            var end = world.Tick + rate.Time.OriginalFrames(30 * RiderSeconds);
            var shots = 0;
            var outsideAt = -1L;
            while (world.Tick < end && !session.IsOver)
            {
                var result = session.Step();
                if (!firing && riders.Count == 0 && transport.Container is { ContainCount: > 0 } payload) riders.AddRange(payload.Contained);
                foreach (var value in result.Events)
                    if (value.Kind == SimEventKind.WeaponFired && riders.Contains(value.Entity)) shots++;
                if (outsideAt < 0 && riders.Count > 0 && riders.Any(id => world.FindEntity(id) is { EffectivelyDead: false, ContainedBy: var by } && by != transport.Id)) outsideAt = world.Tick;
            }
            Check.That(riders.Count > 0 && !transport.EffectivelyDead, $"{label}: the transport lived and had {riders.Count} riders.");
            if (firing)
                Check.That(outsideAt < 0 && shots > 0, $"{label}: the Rangers stayed inside ({(outsideAt < 0 ? "all the time" : "one left at tick " + outsideAt)}) and fired {shots} shots from the Humvee.");
            else
                Check.That(outsideAt >= 0, $"{label}: the AI unloaded the Troop Crawler ({(outsideAt >= 0 ? "at tick " + outsideAt : "never")}), whose riders cannot fire from inside.");
        }
    }

    private static bool HasStructure(SimulationWorld world, PlayerId player) => world.Entities.Any(entity => entity.Owner == player && entity.Template.IsKindOf("STRUCTURE"));

    /// <summary>The place of <paramref name="player"/>'s first structure at the start of a match (its command center).</summary>
    private static (Fixed X, Fixed Y) StartOf(SimulationWorld world, PlayerId player, string label)
    {
        foreach (var entity in world.Entities)
            if (entity.Owner == player && entity.Template.IsKindOf("STRUCTURE")) return (entity.X, entity.Y);
        throw new InvalidOperationException($"{label}: the player has no structure at the start.");
    }

    /// <summary>How far from the transport the enemy of <see cref="RealSkirmishAiKeepsFiringRidersInside"/> stands.</summary>
    private const int RiderEnemyDistance = 70;

    /// <summary>The game seconds <see cref="RealSkirmishAiKeepsFiringRidersInside"/> watches the transport.</summary>
    private const int RiderSeconds = 15;
}
