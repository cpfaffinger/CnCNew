using System.Collections.Immutable;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Real data, #380: the three soldiers of a GLA Stinger Site stay at their site. They are <c>SPAWNS_ARE_THE_WEAPONS</c>, and <c>WeaponSet::getAbleToAttackSpecificObject</c> refuses such an
/// object (like an <c>IMMOBILE</c> one or a rider) every victim no weapon reaches from where it stands (<c>ATTACKRESULT_INVALID_SHOT</c>): its own look takes no victim beyond its range
/// and its attack ends (<c>cannotPossiblyAttackObject</c>) when the victim leaves it - it never walks to one. Before, a computer player's soldiers walked at an enemy they saw.
/// </summary>
static partial class Tests
{
    public static void RealStingerSoldiersStayAtTheirSite(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new RealSource(content), ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);

        // The farthest any soldier came from the site's centre, and the frame of the first hit on the enemy (-1 for none).
        (Fixed Farthest, long FirstHit) Run(SimulationRate rate, bool computer, int enemyDistance)
        {
            var catalog = TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East", IsComputer: computer), new PlayerConfiguration(new PlayerId(2), "West"));
            var terrain = TerrainMap.Build(SyntheticMaps.Build(120, 120, 1, (_, _) => 0), "stinger");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "stinger", fingerprint, terrain.Fingerprint), catalog, terrain);
            var feeder = new Feeder(world);
            Check.That(feeder.Spawn(1, "GLAStingerSite", owner: 1, x: 300, y: 600).Accepted, "The stinger site spawns.");
            for (var step = 0; step < world.Frames(30); step++) world.Step();
            var site = world.Entities.Single(value => value.Template.Name == "GLAStingerSite");
            var soldiers = world.Entities.Where(value => value.Template.Name == "GLAInfantryStingerSoldier").ToArray();
            Check.That(soldiers.Length == 3, $"{rate}: the site has its three soldiers ({soldiers.Length}).");
            // An enemy tank within the soldiers' look (vision 400, times 1.333 for a computer player) but beyond their rockets (225).
            Check.That(feeder.Spawn(2, "AmericaTankCrusader", owner: 2, x: 300 + enemyDistance, y: 600, angle: FixedTrig.Pi).Accepted, "The Crusader spawns.");
            for (var step = 0; step < world.Frames(1); step++) world.Step();
            var tank = world.Entities.Single(value => value.Template.Name == "AmericaTankCrusader");
            var farthest = Fixed.Zero;
            long firstHit = -1;
            var start = world.Tick;
            for (var step = 0; step < world.Frames(600); step++)
            {
                world.Step();
                foreach (var soldier in soldiers)
                {
                    if (soldier.EffectivelyDead) continue;
                    var dx = soldier.X - site.X;
                    var dy = soldier.Y - site.Y;
                    farthest = Fixed.Max(farthest, Fixed.Sqrt(dx * dx + dy * dy));
                }
                if (firstHit < 0 && tank.Body!.Health < tank.Body.MaxHealth) firstHit = (world.Tick - start + world.Time.TicksPerOriginalFrame - 1) / world.Time.TicksPerOriginalFrame;
            }
            return (farthest, firstHit);
        }

        // The soldiers spawn at the site's spawn bones, within some 40 of its centre; "staying" leaves room for them to step aside.
        var stay = Fixed.FromInt(80);
        foreach (var computer in new[] { true, false })
        {
            var who = computer ? "computer" : "human";
            var far = Run(SimulationRate.Hz30, computer, 400);
            Check.That(far.Farthest < stay, $"30 Hz, {who}: the soldiers stay at their site with an enemy out of range (came {far.Farthest} from it).");
            Check.That(far.FirstHit < 0, $"30 Hz, {who}: nobody hits the enemy out of range ({far.FirstHit}).");
            var near = Run(SimulationRate.Hz30, computer, 200);
            Check.That(near.Farthest < stay && near.FirstHit > 0, $"30 Hz, {who}: the soldiers fire at an enemy in range from their site (came {near.Farthest} from it, first hit in frame {near.FirstHit}).");
            foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
            {
                var other = Run(rate, computer, 400);
                Check.That(other.Farthest < stay && other.FirstHit < 0, $"{rate}, {who}: the soldiers stay at their site (came {other.Farthest} from it, first hit {other.FirstHit}).");
                var otherNear = Run(rate, computer, 200);
                Check.That(otherNear.Farthest < stay && otherNear.FirstHit == near.FirstHit,
                    $"{rate}, {who}: the first hit from the site in the frame of 30 Hz ({otherNear.FirstHit} against {near.FirstHit}, came {otherNear.Farthest} from it).");
            }
        }
    }
}
