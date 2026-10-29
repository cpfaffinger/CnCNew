using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// F-69, mine clearing (<c>DozerAIUpdate.cpp</c>, <c>WorkerAIUpdate.cpp</c>): a builder carries the weapon set flag <c>MINE_CLEARING_DETAIL</c> while it is not at work on a task, so its
/// mine-clearing weapon set is its active one, and a builder that acquires targets when idle disarms an enemy mine in its reach.
/// </summary>
static partial class Tests
{
    private const string MineClearingIni = """
        Weapon McDisarm
          PrimaryDamage = 1.0
          AttackRange = 20.0
          DamageType = DISARM
          DeathType = NORMAL
          DelayBetweenShots = 0
          ClipSize = 1
          ClipReloadTime = 4000
          AntiGround = No
          AntiMine = Yes
        End
        Locomotor McLoco
          Surfaces = GROUND
          TurnRate = 360.0
          Speed = 30.0
          Acceleration = 30.0
          Braking = 30.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End
        Object McDozer
          KindOf = SELECTABLE VEHICLE DOZER
          VisionRange = 100.0
          ShroudClearingRange = 100.0
          Locomotor = SET_NORMAL McLoco
          WeaponSet
            Conditions = None
            Weapon = PRIMARY None
          End
          WeaponSet
            Conditions = MINE_CLEARING_DETAIL
            Weapon = PRIMARY McDisarm
          End
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DozerAIUpdate AI01
            RepairHealthPercentPerSecond = 10%
            BoredTime = 5000
            BoredRange = 150
            AutoAcquireEnemiesWhenIdle = Yes
          End
        End
        Object McMine
          KindOf = MINE
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
        End
        """;

    /// <summary>
    /// An idle dozer beside an enemy mine disarms it: the mine dies in the same frame at 30, 60 and 120 Hz, and the dozer's mine-clearing set is its active one. Before, the dozer had
    /// no weapon and the mine stayed.
    /// </summary>
    public static void IdleDozerDisarmsAMineBesideIt()
    {
        long reference = -1;
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var fixture = new SyntheticFixture(rate, files: new Dictionary<string, string>(SyntheticFixture.Files) { ["Data/INI/Object/MineClearing.ini"] = MineClearingIni });
            var terrain = TerrainMap.Build(SyntheticMaps.Build(60, 60, 1, (_, _) => 0), "mines");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 3, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
            var feeder = new Feeder(world);
            Check.That(feeder.Spawn(1, "McDozer", owner: 1, x: 300, y: 300).Accepted && feeder.Spawn(2, "McMine", owner: 2, x: 315, y: 300).Accepted, $"{rate}: dozer and mine spawn.");
            world.Step();
            var dozer = world.Entities.Single(value => value.Template.Name == "McDozer");
            var mine = world.Entities.Single(value => value.Template.Name == "McMine");
            Check.That(dozer.MineClearingDetail && SimulationWorld.ActiveWeaponIndex(dozer) >= 0, $"{rate}: the idle dozer carries its mine-clearing set.");
            long gone = -1;
            for (var step = 0; step < world.Frames(300) && gone < 0; step++)
            {
                world.Step();
                if (mine.Body!.Health <= Fixed.Zero) gone = (world.Tick + world.Time.TicksPerOriginalFrame - 1) / world.Time.TicksPerOriginalFrame;
            }
            Check.That(gone > 0, $"{rate}: the dozer disarms the mine.");
            if (reference < 0) reference = gone;
            else Check.That(gone == reference, $"{rate}: the mine is disarmed in the frame it is at 30 Hz ({gone} against {reference}).");
        }
    }
}
