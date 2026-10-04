using CnCNew.Simulation.Events;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>The continuous-fire stages of <c>FiringTracker</c> (<c>SimulationWorld.FiringTracker.cs</c>): shots in a row at one victim speed the weapon up through its own weapon bonus lines.</summary>
static partial class Tests
{
    // 600 ms between shots = 18 ticks; the mean stage halves it, the fast stage quarters it. Coast 1000 ms = 30 ticks.
    private static IReadOnlyDictionary<string, string> FiringTrackerFiles() => new Dictionary<string, string>(TurretFiles())
    {
        ["Data/INI/Object/FiringTrackerFixture.ini"] = """
        Weapon SpinGun
          PrimaryDamage = 1
          AttackRange = 150.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 600
          ContinuousFireOne = 3
          ContinuousFireTwo = 6
          ContinuousFireCoast = 1000
          WeaponBonus = CONTINUOUS_FIRE_MEAN RATE_OF_FIRE 200%
          WeaponBonus = CONTINUOUS_FIRE_FAST RATE_OF_FIRE 400%
        End
        Object SpinTank
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY SpinGun
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """,
    };

    public static void FiringTrackerSpinsUpCoastsAndCoolsDown()
    {
        var fixture = new SyntheticFixture(files: FiringTrackerFiles());
        var world = NewWorld(fixture);
        var feeder = new Feeder(world);
        feeder.Spawn(1, "SpinTank", owner: 1, x: 0, y: 0);
        feeder.Spawn(1, "PlainTank", owner: 1, x: 0, y: -50);
        feeder.Spawn(2, "TurretTarget", owner: 2, x: 0, y: 100);
        feeder.Spawn(2, "TurretTarget", owner: 2, x: 100, y: 0);
        world.Step();
        var tank = Slotted(world, "SpinTank");
        var plain = Slotted(world, "PlainTank");
        var north = IdOf(world, "TurretTarget", 0);
        var east = IdOf(world, "TurretTarget", 1);

        var shots = new List<(long Tick, ContinuousFireStage After)>();
        void Fire(SimulationWorld into, Feeder with, uint victim, int ticks, List<(long, ContinuousFireStage)>? record)
        {
            for (var i = 0; i < ticks; i++)
            {
                with.FireWeapon(1, tank.Id.Value, victim);
                var result = into.Step();
                if (record is not null && result.Events.Any(value => value.Kind == SimEventKind.WeaponFired && value.Entity == tank.Id))
                    record.Add((result.Tick, SimulationWorld.ContinuousFireOf(into.FindEntity(tank.Id)!)));
            }
        }

        Fire(world, feeder, north, 120, shots);
        var gaps = shots.Zip(shots.Skip(1), (a, b) => b.Tick - a.Tick).ToArray();
        Check.That(shots.Count >= 12, "The tank keeps firing: " + shots.Count);
        Check.That(shots[0].After == ContinuousFireStage.None && shots[2].After == ContinuousFireStage.None, "Up to ContinuousFireOne shots there is no stage.");
        Check.That(shots[3].After == ContinuousFireStage.Mean && shots[5].After == ContinuousFireStage.Mean, "More than ContinuousFireOne shots in a row: the mean stage.");
        Check.That(shots[6].After == ContinuousFireStage.Fast && shots[^1].After == ContinuousFireStage.Fast, "More than ContinuousFireTwo: the fast stage, and it stays.");
        Check.That(gaps[0] == 18 && gaps[1] == 18 && gaps[2] == 18 && gaps[3] == 18, "Without a stage the weapon fires every 18 ticks: " + string.Join(",", gaps));
        // The shot that reaches a stage was still timed without it (the tracker is told after the shot).
        Check.That(gaps[4] == 9 && gaps[5] == 9 && gaps[6] == 9, "The mean stage (RATE_OF_FIRE 200%) halves the delay: " + string.Join(",", gaps));
        Check.That(gaps.Skip(7).All(gap => gap is 4 or 5), "The fast stage (400%) quarters it: " + string.Join(",", gaps));
        Check.That((tank.WeaponBonusConditions & (1u << WeaponBonusTable.ContinuousFireFastCondition)) != 0 && (tank.WeaponBonusConditions & (1u << WeaponBonusTable.ContinuousFireMeanCondition)) == 0,
            "The stages are weapon bonus conditions and exclusive.");

        // Another victim within the coast time keeps the stage.
        var switched = new List<(long Tick, ContinuousFireStage After)>();
        Fire(world, feeder, east, 20, switched);
        Check.That(switched.Count >= 3 && switched.All(value => value.After == ContinuousFireStage.Fast), "A change of victim within the coast time does not spin the weapon down.");

        // A snapshot taken while spun up continues identically through the cool-down.
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
        var fork = feeder.ForkTo(restored);
        Fire(world, feeder, east, 10, null);
        Fire(restored, fork, east, 10, null);

        // No more shots: the stage holds for the coast time after the next possible shot, drops to slow for a second and then ends.
        var stages = new List<ContinuousFireStage>();
        for (var i = 0; i < 90; i++)
        {
            var hash = world.Step().StateHash;
            Check.That(restored.Step().StateHash == hash, "The restored world cools down identically at step " + i + ".");
            stages.Add(SimulationWorld.ContinuousFireOf(tank));
        }
        var fast = stages.TakeWhile(value => value == ContinuousFireStage.Fast).Count();
        var slow = stages.Skip(fast).TakeWhile(value => value == ContinuousFireStage.Slow).Count();
        Check.That(fast is >= 30 and <= 36, "The fast stage outlasts the last shot by the coast time (30 ticks past the next possible shot): " + fast);
        Check.That(slow == 31, "The slow stage lasts a second: " + slow);
        Check.That(stages.Skip(fast + slow).All(value => value == ContinuousFireStage.None) && tank.WeaponBonusConditions == 0, "Then no stage is left.");

        // After the cool-down the count starts over.
        var again = new List<(long Tick, ContinuousFireStage After)>();
        Fire(world, feeder, north, 60, again);
        Check.That(again[0].After == ContinuousFireStage.None && again[2].After == ContinuousFireStage.None && again[3].After == ContinuousFireStage.Mean, "After the cool-down the weapon spins up from the start.");

        // A weapon without stages never gets a stage.
        for (var i = 0; i < 100; i++)
        {
            feeder.FireWeapon(1, plain.Id.Value, north);
            world.Step();
        }
        Check.That(SimulationWorld.ContinuousFireOf(plain) == ContinuousFireStage.None && plain.WeaponBonusConditions == 0, "A weapon without ContinuousFireOne/Two has no stages.");
    }
}
