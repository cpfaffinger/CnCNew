using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

// #422: the original measures every weapon range between the two bounding circles (Weapon::isWithinAttackRange, ATTACK_RANGE_CALC_TYPE = FROM_BOUNDINGSPHERE_2D,
// distCalcProc_BoundaryAndBoundary_2D), and the shipped build defines RATIONALIZE_ATTACK_RANGE (Weapon.cpp): the range is a quarter cell short, the minimum range too and compared
// strictly, a contact weapon is one whose range less a quarter cell is short of a cell.
static partial class Tests
{
    private sealed class RangeScene
    {
        public required SimulationWorld World;
        private ulong _sequence;

        public static RangeScene Create(TemplateCatalog catalog, string fingerprint, SimulationRate rate)
        {
            var players = System.Collections.Immutable.ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "Attacker"), new PlayerConfiguration(new PlayerId(2), "Enemy", IsComputer: true));
            var terrain = TerrainMap.Build(SyntheticMaps.Build(120, 120, 1, (_, _) => 0), "weapon-range-circles");
            return new RangeScene { World = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 7, players, "weapon-range-circles", fingerprint, terrain.Fingerprint), catalog, terrain) };
        }

        public Entity Spawn(string template, int owner, Fixed x, Fixed y, Fixed angle = default)
        {
            Check.That(World.Submit(new SpawnCommand(new PlayerId(owner), ++_sequence, World.Tick, template, new PlayerId(owner), x, y, Fixed.Zero, angle)).Accepted, template + " spawns.");
            World.Step();
            return World.Entities.Last(value => value.Template.Name == template);
        }

        public void Settle()
        {
            // The first look of the players (above 30 Hz the vision is taken once per logic frame of the original).
            for (var step = 0; step < World.Frames(2); step++) World.Step();
        }

        public ulong Next() => ++_sequence;
    }

    private static (CnCNew.Data.Definitions.CompiledDefinitionGraph Graph, string Fingerprint) RangeGraph(string directory)
    {
        var content = CnCNew.Content.ContentFileSystem.Open(directory);
        var options = CnCNew.Data.Compilation.DefinitionCompilerOptions.Audit with { StrictUnknownModules = true, StrictLoadPlan = true, MapOverlays = CnCNew.Data.Compilation.MapOverlayMode.Exclude };
        var graph = CnCNew.Data.Compilation.DefinitionCompiler.Compile(new RealSource(content), CnCNew.Data.Registry.ModuleRegistry.Default, options);
        var (fingerprint, _) = CnCNew.Data.Snapshots.DefinitionSnapshotWriter.ComputeFingerprint(graph);
        return (graph, fingerprint);
    }

    private static Fixed CentreDistance(Entity a, Entity b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Fixed.Sqrt(dx * dx + dy * dy);
    }

    private static bool Reaches(SimulationWorld world, Entity source, Entity victim) => world.ChooseWeapon(source, victim, FireOrigin.Player).Rejection != CommandRejection.OutOfRange;

    /// <summary>
    /// A Crusader at a Chinese war factory reaches it from its range plus the two circles (18 and 83.5), not from 150 between the centres; it walks up to that distance and fires there; a
    /// human player's Gattling tank, which may not chase, takes a tank beyond 150 between the centres for its victim when the circles are within its range.
    /// </summary>
    public static void RealWeaponRangesAreMeasuredBetweenTheCircles(string directory)
    {
        var (graph, fingerprint) = RangeGraph(directory);
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, CnCNew.Simulation.Modules.ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);

            // Standing still: in reach just inside the range plus the circles, out of it just beyond.
            {
                var scene = RangeScene.Create(catalog, fingerprint, rate);
                var factory = scene.Spawn("ChinaWarFactory", 2, Fixed.FromInt(600), Fixed.FromInt(600));
                var radii = factory.Template.BoundingCircleRadius + catalog.Templates.First(value => value.Name == "AmericaTankCrusader").BoundingCircleRadius;
                var range = scene.World.Weapons[SimulationWorld.WeaponIndexIn(scene.Spawn("AmericaTankCrusader", 1, Fixed.FromInt(100), Fixed.FromInt(100)), WeaponSlot.Primary)].AttackRange;
                Check.That(radii > Fixed.FromInt(100) && range == Fixed.FromInt(150), $"{rate}: setup - the two circles {radii}, the gun's range {range}.");
                var near = scene.Spawn("AmericaTankCrusader", 1, Fixed.FromInt(600) - (range + radii - Fixed.FromInt(5)), Fixed.FromInt(600));
                var far = scene.Spawn("AmericaTankCrusader", 1, Fixed.FromInt(600), Fixed.FromInt(600) + range + radii + Fixed.FromInt(5));
                scene.Settle();
                Check.That(Reaches(scene.World, near, factory), $"{rate}: a Crusader {CentreDistance(near, factory)} from the factory's centre reaches it (its circle is 145 away).");
                Check.That(!Reaches(scene.World, far, factory), $"{rate}: a Crusader {CentreDistance(far, factory)} from the factory's centre does not (its circle is 155 away).");
            }

            // The approach: the attack walks until the circles are in range and fires from there.
            {
                var scene = RangeScene.Create(catalog, fingerprint, rate);
                var factory = scene.Spawn("ChinaWarFactory", 2, Fixed.FromInt(700), Fixed.FromInt(600));
                var tank = scene.Spawn("AmericaTankCrusader", 1, Fixed.FromInt(420), Fixed.FromInt(600));
                scene.Settle();
                var radii = factory.Template.BoundingCircleRadius + tank.Template.BoundingCircleRadius;
                var order = scene.World.Submit(new FireWeaponCommand(new PlayerId(1), scene.Next(), scene.World.Tick, tank.Id, factory.Id, WeaponSlot.Best, FireOrigin.Player, FireWeaponCommand.NoMaxShotsLimit));
                Check.That(order.Accepted, $"{rate}: the attack order is accepted ({order.Reason}).");
                var distance = Fixed.MinValue;
                for (var step = 0; step < scene.World.Frames(900) && distance == Fixed.MinValue; step++)
                    foreach (var value in scene.World.Step().Events)
                        if (value.Entity == tank.Id && value.Kind == SimEventKind.WeaponFired && distance == Fixed.MinValue) distance = CentreDistance(tank, factory);
                Check.That(distance != Fixed.MinValue, $"{rate}: the Crusader fires at the factory within 30 s.");
                Check.That(distance <= Fixed.FromDecimal(147.5m) + radii && distance >= Fixed.FromInt(150) + radii - Fixed.FromInt(20),
                    $"{rate}: it fires from {distance} between the centres - its range 150 between the circles ({radii} together), not 150 between the centres.");
            }

            // The acquisition of a unit that may not chase (a human player's): only what its weapon reaches.
            {
                var scene = RangeScene.Create(catalog, fingerprint, rate);
                var gattling = scene.Spawn("ChinaTankGattling", 1, Fixed.FromInt(400), Fixed.FromInt(600));
                var radii = gattling.Template.BoundingCircleRadius + catalog.Templates.First(value => value.Name == "AmericaTankCrusader").BoundingCircleRadius;
                var victim = scene.Spawn("AmericaTankCrusader", 2, Fixed.FromInt(400) + Fixed.FromInt(150) + radii - Fixed.FromInt(5), Fixed.FromInt(600), FixedTrig.Pi);
                scene.World.SetAttitude(victim, (int)Attitude.Sleep);
                scene.Settle();
                var fired = false;
                for (var step = 0; step < scene.World.Frames(150) && !fired; step++)
                    foreach (var value in scene.World.Step().Events)
                        fired |= value.Entity == gattling.Id && value.Kind == SimEventKind.WeaponFired;
                Check.That(fired && scene.World.AcquiredVictimOf(gattling) == victim.Id,
                    $"{rate}: a human player's Gattling tank takes a tank {CentreDistance(gattling, victim)} away between the centres for its victim and fires: its gun of 150 reaches between the circles.");
            }
        }
    }

    /// <summary>
    /// <c>RATIONALIZE_ATTACK_RANGE</c>: <c>WeaponTemplate::getAttackRange</c> is the range less a quarter of a pathfind cell. A Crusader whose circle is 149 from a war factory's does not
    /// reach it with its gun of 150, one 147 away does; a human player's Gattling tank does not take a tank whose circle is 149 away.
    /// </summary>
    public static void RealAttackRangeIsAQuarterCellShort(string directory)
    {
        var (graph, fingerprint) = RangeGraph(directory);
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var catalog = TemplateCatalog.Build(graph, CnCNew.Simulation.Modules.ModuleBindingTable.Default, rate, rules: CnCNew.Simulation.Rules.RuleProfile.Product);
            var crusader = catalog.Templates.First(value => value.Name == "AmericaTankCrusader").BoundingCircleRadius;
            {
                var scene = RangeScene.Create(catalog, fingerprint, rate);
                var factory = scene.Spawn("ChinaWarFactory", 2, Fixed.FromInt(600), Fixed.FromInt(600));
                var radii = factory.Template.BoundingCircleRadius + crusader;
                var outside = scene.Spawn("AmericaTankCrusader", 1, Fixed.FromInt(600) - (Fixed.FromInt(149) + radii), Fixed.FromInt(600));
                var inside = scene.Spawn("AmericaTankCrusader", 1, Fixed.FromInt(600), Fixed.FromInt(600) + Fixed.FromInt(147) + radii);
                scene.Settle();
                Check.That(!Reaches(scene.World, outside, factory), $"{rate}: a Crusader whose circle is 149 from the factory's does not reach it (150 less a quarter cell).");
                Check.That(Reaches(scene.World, inside, factory), $"{rate}: a Crusader whose circle is 147 from the factory's reaches it.");
            }
            {
                var scene = RangeScene.Create(catalog, fingerprint, rate);
                var gattling = scene.Spawn("ChinaTankGattling", 1, Fixed.FromInt(400), Fixed.FromInt(600));
                var victim = scene.Spawn("AmericaTankCrusader", 2, Fixed.FromInt(400) + Fixed.FromInt(149) + gattling.Template.BoundingCircleRadius + crusader, Fixed.FromInt(600), FixedTrig.Pi);
                scene.World.SetAttitude(victim, (int)Attitude.Sleep);
                scene.Settle();
                var fired = false;
                for (var step = 0; step < scene.World.Frames(150) && !fired; step++)
                    foreach (var value in scene.World.Step().Events)
                        fired |= value.Entity == gattling.Id && value.Kind == SimEventKind.WeaponFired;
                Check.That(!fired && !scene.World.AcquiredVictimOf(gattling).IsValid, $"{rate}: a human player's Gattling tank does not take a tank whose circle is 149 from its own.");
            }
        }
    }
}
