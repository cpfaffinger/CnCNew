using System.Collections.Immutable;
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
          ContinueAttackRange = 100
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
                if (mine.Body!.Health <= Fixed.Zero) gone = (world.Tick - dozer.SpawnTick + world.Time.TicksPerOriginalFrame - 1) / world.Time.TicksPerOriginalFrame;
            }
            Check.That(gone > 0, $"{rate}: the dozer disarms the mine.");
            if (reference < 0) reference = gone;
            else Check.That(gone == reference, $"{rate}: the mine is disarmed in the frame it is at 30 Hz ({gone} against {reference}).");
        }
    }

    /// <summary>
    /// F-69 step 2, <c>findMine</c>: a bored dozer with nothing to repair drives to an enemy mine 90 units away - out of its weapon's reach, inside <c>BoredRange</c> - and disarms it,
    /// in the same frame at 30, 60 and 120 Hz; its own mine, nearer, it leaves. Before, an idle dozer only disarmed a mine in its weapon's reach.
    /// </summary>
    public static void BoredDozerGoesToClearAMine()
    {
        long reference = -1;
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var fixture = new SyntheticFixture(rate, files: new Dictionary<string, string>(SyntheticFixture.Files) { ["Data/INI/Object/MineClearing.ini"] = MineClearingIni });
            var terrain = TerrainMap.Build(SyntheticMaps.Build(60, 60, 1, (_, _) => 0), "mines");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 3, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
            var feeder = new Feeder(world);
            Check.That(feeder.Spawn(1, "McDozer", owner: 1, x: 300, y: 300).Accepted && feeder.Spawn(2, "McMine", owner: 2, x: 390, y: 300).Accepted
                && feeder.Spawn(1, "McMine", owner: 1, x: 300, y: 340).Accepted, $"{rate}: dozer and mines spawn.");
            world.Step();
            var enemy = world.Entities.Single(value => value.Template.Name == "McMine" && value.Owner == new PlayerId(2));
            var own = world.Entities.Single(value => value.Template.Name == "McMine" && value.Owner == new PlayerId(1));
            var dozer = world.Entities.Single(value => value.Template.Name == "McDozer");
            long gone = -1;
            for (var step = 0; step < world.Frames(600) && gone < 0; step++)
            {
                world.Step();
                // Frames of the dozer's own grid (from the tick it appeared in), a tick off the world's above 30 Hz.
                if (enemy.Body!.Health <= Fixed.Zero) gone = (world.Tick - dozer.SpawnTick + world.Time.TicksPerOriginalFrame - 1) / world.Time.TicksPerOriginalFrame;
            }
            Check.That(gone > 150, $"{rate}: the bored dozer goes to disarm the enemy's mine ({gone}).");
            Check.That(own.Body!.Health > Fixed.Zero, $"{rate}: its own mine stays.");
            if (reference < 0) reference = gone;
            else Check.That(gone == reference, $"{rate}: the mine is disarmed in the frame it is at 30 Hz ({gone} against {reference}).");
        }
    }

    /// <summary>
    /// F-69 step 3, <c>ContinueAttackRange</c>: after the bored dozer disarmed its first mine it goes on at once to the next mine of the same player near where the first lay, without
    /// being bored again; a mine of another player beside it is not part of that. The same frames at 30, 60 and 120 Hz. Before, the dozer stood and was bored again first.
    /// </summary>
    public static void DozerGoesOnToTheNextMine()
    {
        (long First, long Second)? reference = null;
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var players = ImmutableArray.Create(new PlayerConfiguration(new PlayerId(1), "East"), new PlayerConfiguration(new PlayerId(2), "West"), new PlayerConfiguration(new PlayerId(3), "North"));
            var fixture = new SyntheticFixture(rate, files: new Dictionary<string, string>(SyntheticFixture.Files) { ["Data/INI/Object/MineClearing.ini"] = MineClearingIni });
            var terrain = TerrainMap.Build(SyntheticMaps.Build(60, 60, 1, (_, _) => 0), "mines");
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 3, players, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain.Fingerprint), fixture.Templates, terrain);
            var feeder = new Feeder(world);
            Check.That(feeder.Spawn(1, "McDozer", owner: 1, x: 300, y: 300).Accepted && feeder.Spawn(2, "McMine", owner: 2, x: 390, y: 300).Accepted
                && feeder.Spawn(2, "McMine", owner: 2, x: 400, y: 380).Accepted && feeder.Spawn(3, "McMine", owner: 3, x: 400, y: 330).Accepted, $"{rate}: dozer and mines spawn.");
            world.Step();
            var dozer = world.Entities.Single(value => value.Template.Name == "McDozer");
            var first = world.Entities.Single(value => value.Template.Name == "McMine" && value.Y == Fixed.FromInt(300));
            var second = world.Entities.Single(value => value.Template.Name == "McMine" && value.Y == Fixed.FromInt(380));
            var other = world.Entities.Single(value => value.Template.Name == "McMine" && value.Owner == new PlayerId(3));
            long firstGone = -1, secondGone = -1;
            for (var step = 0; step < world.Frames(900) && secondGone < 0; step++)
            {
                world.Step();
                var frame = (world.Tick - dozer.SpawnTick + world.Time.TicksPerOriginalFrame - 1) / world.Time.TicksPerOriginalFrame;
                if (firstGone < 0 && first.Body!.Health <= Fixed.Zero) firstGone = frame;
                if (secondGone < 0 && second.Body!.Health <= Fixed.Zero)
                {
                    secondGone = frame;
                    Check.That(other.Body!.Health > Fixed.Zero, $"{rate}: the other player's mine is not part of the clearing.");
                }
            }
            Check.That(firstGone > 0 && secondGone > firstGone && secondGone - firstGone < 120, $"{rate}: the dozer goes on to the next mine at once ({firstGone}, {secondGone}).");
            if (reference is null) reference = (firstGone, secondGone);
            else Check.That((firstGone, secondGone) == reference.Value, $"{rate}: the same frames as at 30 Hz (({firstGone}, {secondGone}) against {reference}).");
        }
    }
}
