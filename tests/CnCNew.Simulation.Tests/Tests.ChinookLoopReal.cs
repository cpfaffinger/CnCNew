using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

static partial class Tests
{
    /// <summary>The world of the endless-gathering tests: a human supply center, a small pile close to it and a second pile beyond a Chinook's scan distance (700 units).</summary>
    private sealed class GatherWorld
    {
        public readonly SimulationWorld World;
        public readonly PlayerId Human = new(1);
        private ulong _sequence;

        private static readonly Dictionary<SimulationRate, (TemplateCatalog Catalog, string Fingerprint)> Catalogs = [];

        public GatherWorld(string directory, SimulationRate rate)
        {
            if (!Catalogs.TryGetValue(rate, out var built))
            {
                var content = ContentFileSystem.Open(directory);
                var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
                var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
                var (print, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
                Catalogs[rate] = built = (TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product), print);
            }
            var (catalog, fingerprint) = built;
            var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"));
            var terrain = TerrainMap.Build(SyntheticMaps.Build(200, 200, 1, (_, _) => 0), "chinook-loop");
            World = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "real-chinook-loop", fingerprint, terrain.Fingerprint), catalog, terrain);
            Submit(new GiveMoneyCommand(PlayerId.System, ++_sequence, World.Tick, Human, Fixed.FromInt(5000)));
        }

        private void Submit(SimCommand command) => Check.That(World.Submit(command).Accepted, command.GetType().Name + " is accepted.");

        public Entity Spawn(string template, PlayerId owner, int x, int y)
        {
            var before = World.Entities.Select(entity => entity.Id.Value).ToHashSet();
            Submit(new SpawnCommand(owner, ++_sequence, World.Tick, template, owner, Fixed.FromInt(x), Fixed.FromInt(y), Fixed.Zero, Fixed.Zero));
            World.Step();
            return World.Entities.First(entity => !before.Contains(entity.Id.Value));
        }

        public void Settle() { for (var step = 0; step < World.Frames(3); step++) World.Step(); }

        public void Dock(Entity carrier, Entity dock) => Submit(new DockCommand(Human, ++_sequence, World.Tick, carrier.Id, dock.Id));

        /// <summary>Steps for <paramref name="seconds"/> of game time and returns the deposits of <paramref name="carrier"/>: the centre each went to and the second it happened at.</summary>
        public List<(uint Center, int Second)> Run(Entity carrier, int seconds)
        {
            var deposits = new List<(uint, int)>();
            var steps = World.Frames(30 * seconds);
            for (var step = 0; step < steps; step++)
            {
                var result = World.Step();
                foreach (var ev in result.Events)
                    if (ev.Kind == SimEventKind.SupplyDeposited && ev.Entity == carrier.Id && ev.Other != ev.Entity) deposits.Add((ev.Other.Value, (int)(World.Tick / World.Time.TicksPerSecond)));
            }
            return deposits;
        }
    }

    /// <summary>
    /// Real data (the owner's playtest of 9 November 2026, finding 1; rule fix <c>chinook-keeps-gathering</c>): a Chinook gathers for good. A Chinook that a center made, and one the player
    /// ordered to a pile, bring supplies again and again; when the pile is empty they look for the next stocked warehouse the player has seen, beyond the scan distance too (a pile nobody
    /// has seen is not a target), and a Chinook whose last order was the player's does not stand for good where it lost its pile.
    /// </summary>
    public static void RealChinookGathersForGood(string directory)
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
            foreach (var ordered in new[] { false, true })
            {
                var label = $"{rate} ({(ordered ? "ordered" : "made")})";
                var scene = new GatherWorld(directory, rate);
                var center = scene.Spawn("AmericaSupplyCenter", scene.Human, 300, 300);
                var small = scene.Spawn("SupplyPileSmall", PlayerId.System, 520, 340);
                var far = scene.Spawn("SupplyPile", PlayerId.System, 1260, 340);
                // The center's own Chinook (SpawnBehavior); the ordered one is a second Chinook that is sent to the small pile by a click.
                scene.Settle();
                var chinook = scene.World.Entities.First(entity => entity.Template.Name == "AmericaVehicleChinook");
                if (ordered)
                {
                    var others = scene.World.Entities.Where(entity => entity.Template.Name == "AmericaVehicleChinook").Select(entity => entity.Id).ToArray();
                    chinook = scene.Spawn("AmericaVehicleChinook", scene.Human, 200, 200);
                    scene.Dock(chinook, small);
                    // The center's own Chinook gathers on its own and would share the small pile; the test follows the ordered one only.
                    Check.That(others.Length == 1, "One Chinook was made by the center.");
                }
                var first = scene.Run(chinook, 200);
                Check.That(first.Count >= 2, $"{label}: the Chinook brought supplies again and again from the pile ({first.Count} deliveries in 200 s).");
                // Let the pile be worked off by everybody; then nothing is left within reach and the far pile has not been seen.
                scene.Run(chinook, 200);
                Check.That(scene.World.FindEntity(small.Id) is null, $"{label}: the small pile is empty and gone.");
                Check.That(!scene.World.IsPositionExploredBy(scene.Human, far.X, far.Y), $"{label}: the far pile is unexplored.");
                var unseen = scene.Run(chinook, 60);
                Check.That(unseen.Count == 0, $"{label}: no unexplored pile is a target ({unseen.Count} deliveries).");
                // The player clicks the center (the home base) of the idle Chinook: its last order is the player's.
                scene.Dock(chinook, center);
                Check.That(scene.Run(chinook, 30).Count == 0, $"{label}: nothing to deliver after the click.");
                // A scout reveals it: the Chinook goes there by itself, and keeps going.
                scene.Spawn("AmericaInfantryRanger", scene.Human, 1240, 340);
                var found = scene.Run(chinook, 300);
                Check.That(found.Count >= 3, $"{label}: after the far pile was seen the Chinook gathers it again and again ({found.Count} deliveries in 300 s).");
                Check.That(found.All(delivery => delivery.Center == center.Id.Value), $"{label}: it delivers to the center.");
            }
    }

    /// <summary>
    /// Real data (finding 1; rule fix <c>chinook-keeps-gathering</c>): the Chinook's home base is the center it delivered to last, and the player sets it with a click on a center. A center built
    /// closer to the pile later does not take the Chinook; a click on another center does.
    /// </summary>
    public static void RealChinookKeepsItsHomeBase(string directory)
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var scene = new GatherWorld(directory, rate);
            var first = scene.Spawn("AmericaSupplyCenter", scene.Human, 300, 300);
            scene.Spawn("SupplyPile", PlayerId.System, 700, 300);
            scene.Settle();
            var chinook = scene.World.Entities.First(entity => entity.Template.Name == "AmericaVehicleChinook");
            var before = scene.Run(chinook, 60);
            Check.That(before.Count >= 2 && before.All(delivery => delivery.Center == first.Id.Value), $"{rate}: the Chinook delivers to the center it started from ({before.Count} deliveries).");
            var closer = scene.Spawn("AmericaSupplyCenter", scene.Human, 560, 300);
            var second = scene.Run(chinook, 90);
            Check.That(second.Count >= 2, $"{rate}: it goes on gathering ({second.Count} deliveries).");
            Check.That(second.All(delivery => delivery.Center == first.Id.Value), $"{rate}: a closer center does not take it away from its home base (last delivered: {first.Id.Value}; deliveries {string.Join(",", second.Select(delivery => delivery.Center))}).");
            scene.Dock(chinook, closer);
            var clicked = scene.Run(chinook, 120);
            Check.That(clicked.Count >= 3, $"{rate}: after the click it gathers ({clicked.Count} deliveries).");
            Check.That(clicked.Skip(1).All(delivery => delivery.Center == closer.Id.Value), $"{rate}: a click on a center makes it the home base ({string.Join(",", clicked.Select(delivery => delivery.Center))}).");
        }
    }
}
