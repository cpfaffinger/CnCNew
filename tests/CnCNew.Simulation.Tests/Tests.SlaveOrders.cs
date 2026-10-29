using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// F-65, the slaves of a spawner take its attacks (<c>AIGroup::groupAttackObject</c> -> <c>SpawnBehavior::orderSlavesToAttackTarget</c>, <c>orderSlavesToAttackPosition</c>): a site like the
/// stinger site (no weapon, immobile) with two armed soldiers that have no free will, and the same site whose soldiers have it.
/// </summary>
static partial class Tests
{
    private const string SlaveOrdersIni = """
        Locomotor SoFeet
          Surfaces = GROUND
          Speed = 20
          Acceleration = 200
          Braking = 200
          TurnRate = 3600
        End
        Weapon SoGun
          PrimaryDamage = 5
          AttackRange = 120.0
          DamageType = SMALL_ARMS
          DelayBetweenShots = 300
        End
        Object SoSite
          KindOf = STRUCTURE IMMOBILE SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DefaultProductionExitUpdate Exit01
            UnitCreatePoint = X:10.0 Y:0.0 Z:0.0
            NaturalRallyPoint = X:10.0 Y:0.0 Z:0.0
          End
          Behavior = SpawnBehavior Spawn01
            SpawnNumber = 2
            SpawnReplaceDelay = 100000
            SpawnTemplateName = SoSoldier
          End
        End
        Object SoFreeSite
          KindOf = STRUCTURE IMMOBILE SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DefaultProductionExitUpdate Exit01
            UnitCreatePoint = X:10.0 Y:0.0 Z:0.0
            NaturalRallyPoint = X:10.0 Y:0.0 Z:0.0
          End
          Behavior = SpawnBehavior Spawn01
            SpawnNumber = 2
            SpawnReplaceDelay = 100000
            SpawnTemplateName = SoSoldier
            SlavesHaveFreeWill = Yes
          End
        End
        Object SoSoldier
          KindOf = INFANTRY SELECTABLE
          VisionRange = 100.0
          Locomotor = SET_NORMAL SoFeet
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY SoGun
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = SlavedUpdate Slaved01
          End
        End
        Object SoTarget
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
        End
        """;

    /// <summary>
    /// The site is ordered to attack an enemy 180 away - beyond the soldiers' rifles of 120 and beyond their own look (vision 100): its soldiers walk at the enemy and shoot it; the site
    /// whose soldiers have free will cannot take the order. The same frame of the first hit at 30, 60 and 120 Hz, and a snapshot during the walk continues identically.
    /// </summary>
    public static void SlavesTakeTheirSpawnersAttack()
    {
        long Run(SimulationRate rate, string site, bool save)
        {
            var fixture = new SyntheticFixture(rate, files: FireFromContainerFiles(SlaveOrdersIni));
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
            var feeder = new Feeder(world);
            var spawner = GdSpawn(world, feeder, site, 1, 0, 0);
            for (var step = 0; step < world.Frames(60); step++) world.Step();
            var target = GdSpawn(world, feeder, "SoTarget", 2, 180, 0);
            var order = feeder.Pursue(1, spawner.Id.Value, target.Id.Value, 0, 0);
            if (site == "SoFreeSite")
            {
                Check.That(!order.Accepted && order.Reason == CommandRejection.CannotMove, $"{rate}: a site whose soldiers have free will cannot take the order ({order.Reason}).");
                return -1;
            }
            Check.That(order.Accepted, $"{rate}: the site takes the attack order ({order.Reason}).");
            var start = world.Tick;
            for (var step = 0; step < world.Frames(300); step++)
            {
                if (save && step == world.Frames(20))
                {
                    var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
                    for (var follow = 0; follow < restored.Frames(20); follow++)
                        Check.That(restored.Step().StateHash == world.Step().StateHash, $"{rate}: the restored world follows the walk (step {follow}).");
                }
                world.Step();
                if (target.Body!.Health < target.Body.MaxHealth) return (world.Tick - start + world.Time.TicksPerOriginalFrame - 1) / world.Time.TicksPerOriginalFrame;
            }
            Check.That(false, $"{rate}: the soldiers hit the enemy.");
            return -1;
        }

        var reference = Run(SimulationRate.Hz30, "SoSite", false);
        foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var other = Run(rate, "SoSite", false);
            Check.That(other == reference, $"{rate}: the first hit falls in the frame of 30 Hz ({other} against {reference}).");
        }
        Run(TestRate.Default, "SoSite", true);
        Run(TestRate.Default, "SoFreeSite", false);
    }
}
