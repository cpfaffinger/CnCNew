using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// F-19, "collisions only for moving objects": <c>PartitionManager::update</c> looks at what is dirty, and that is more than a mover: a new object (<c>allocCoiArray</c>) and what touched
/// something in the frame before (<c>processContactList</c>, "re-dirtying collision"). Both are not immobile.
/// </summary>
static partial class Tests
{
    private const string ContactFixtureIni = """
        Weapon ContactBurn
          PrimaryDamage = 1
          AttackRange = 500.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 0
        End
        Object ContactPool
          KindOf = IMMOBILE
          Geometry = CYLINDER
          GeometryMajorRadius = 20.0
          GeometryMinorRadius = 20.0
          GeometryHeight = 1.0
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = FireWeaponCollide Collide01
            CollideWeapon = ContactBurn
          End
        End
        Object ContactDrop
          Geometry = CYLINDER
          GeometryMajorRadius = 20.0
          GeometryMinorRadius = 20.0
          GeometryHeight = 1.0
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = FireWeaponCollide Collide01
            CollideWeapon = ContactBurn
          End
        End
        """;

    public static void CollisionsForWhatStandsAndWhatAppears()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var fixture = new SyntheticFixture(rate, files: new Dictionary<string, string>(RoamFiles().Concat(SkirmishFiles()).ToDictionary(pair => pair.Key, pair => pair.Value)) { ["Data/INI/Object/ContactFixture.ini"] = ContactFixtureIni });
            var terrain = TerrainMap.Build(SyntheticMaps.Build(60, 60, 1, (_, _) => 0), "contacts");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 3, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
            var feeder = new Feeder(world);
            Check.That(feeder.Spawn(2, "ContactPool", owner: 2, x: 300, y: 300).Accepted, $"{rate}: the pool spawns.");
            world.Step();
            Check.That(feeder.Spawn(1, "Bystander", owner: 1, x: 310, y: 300).Accepted, $"{rate}: a unit spawns in the pool.");
            for (var step = 0; step < world.Frames(30); step++) world.Step();
            var walker = world.Entities.Single(entity => entity.Template.Name == "Bystander");
            Check.That(!walker.HasMoveTarget, $"{rate}: the unit stands.");
            var before = walker.Body!.Health;
            Check.That(before < walker.Body.MaxHealth, $"{rate}: appearing there, it was burnt.");
            for (var step = 0; step < world.Frames(30); step++) world.Step();
            var burnt = before - walker.Body.Health;
            Check.That(burnt >= Fixed.FromInt(25) && burnt <= Fixed.FromInt(35), $"{rate}: a standing object goes on being touched, once per frame ({burnt} in 30 frames).");
            var restored = RestoredCopy(world, fixture, terrain);
            Check.That(restored.StateHash == world.StateHash, $"{rate}: a restored world matches.");
            for (var step = 0; step < world.Frames(5); step++)
            {
                restored.Step();
                world.Step();
            }
            Check.That(restored.StateHash == world.StateHash, $"{rate}: and goes on alike.");

            var second = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 3, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
            var feeder2 = new Feeder(second);
            Check.That(feeder2.Spawn(1, "Bystander", owner: 1, x: 200, y: 300).Accepted, $"{rate}: a unit stands.");
            for (var step = 0; step < second.Frames(5); step++) second.Step();
            var standing = second.Entities.Single(entity => entity.Template.Name == "Bystander");
            Check.That(feeder2.Spawn(2, "ContactDrop", owner: 2, x: 200, y: 300).Accepted, $"{rate}: something appears on it.");
            for (var step = 0; step < second.Frames(5); step++) second.Step();
            Check.That(standing.Body!.Health < standing.Body.MaxHealth, $"{rate}: what appears on a standing object touches it.");
        }
    }

    private static SimulationWorld RestoredCopy(SimulationWorld world, SyntheticFixture fixture, TerrainMap terrain)
        => CnCNew.Simulation.Snapshots.WorldSnapshot.Restore(CnCNew.Simulation.Snapshots.WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint, terrain);
}
