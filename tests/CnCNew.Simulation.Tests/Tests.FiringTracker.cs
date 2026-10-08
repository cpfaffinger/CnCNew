using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>The continuous-fire stages of <c>FiringTracker</c> (<c>SimulationWorld.FiringTracker.cs</c>): shots in a row at one victim speed the weapon up through its own weapon bonus lines.</summary>
static partial class Tests
{
    // 600 ms between shots = 18 ticks; the mean stage halves it, the fast stage quarters it. Coast 1000 ms = 30 ticks.
    private static IReadOnlyDictionary<string, string> FiringTrackerFiles() => new Dictionary<string, string>(TurretAttackFiles())
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
        Weapon WindupGun
          PrimaryDamage = 1
          AttackRange = 150.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 1000
          PreAttackDelay = 500
          PreAttackType = PER_SHOT
        End
        Weapon AttackWindupGun
          PrimaryDamage = 1
          AttackRange = 150.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 1000
          PreAttackDelay = 500
          PreAttackType = PER_ATTACK
        End
        Weapon ClipWindupGun
          PrimaryDamage = 1
          AttackRange = 150.0
          DamageType = EXPLOSION
          ClipSize = 3
          ClipReloadTime = 2000
          DelayBetweenShots = 200
          PreAttackDelay = 500
          PreAttackType = PER_CLIP ; once per clip
        End
        Object WindupTank
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL ChaseLoco
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY WindupGun
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object AttackWindupTank
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY AttackWindupGun
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object ClipWindupTank
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY ClipWindupGun
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """,
    };

    public static void FiringTrackerSpinsUpCoastsAndCoolsDown()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: FiringTrackerFiles());
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
        void Fire(SimulationWorld into, Feeder with, uint victim, int frames, List<(long, ContinuousFireStage)>? record)
        {
            for (var i = 0; i < into.Frames(frames); i++)
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
        Check.That(gaps[0] == world.Frames(18) && gaps[1] == world.Frames(18) && gaps[2] == world.Frames(18) && gaps[3] == world.Frames(18), "Without a stage the weapon fires every 18 ticks: " + string.Join(",", gaps));
        // The shot that reaches a stage was still timed without it (the tracker is told after the shot).
        Check.That(gaps[4] == world.Frames(9) && gaps[5] == world.Frames(9) && gaps[6] == world.Frames(9), "The mean stage (RATE_OF_FIRE 200%) halves the delay: " + string.Join(",", gaps));
        Check.That(gaps.Skip(7).All(gap => world.InFrames(gap) is 4 or 5), "The fast stage (400%) quarters it: " + string.Join(",", gaps));
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
        for (var i = 0; i < world.Frames(90); i++)
        {
            var hash = world.Step().StateHash;
            Check.That(restored.Step().StateHash == hash, "The restored world cools down identically at step " + i + ".");
            stages.Add(SimulationWorld.ContinuousFireOf(tank));
        }
        var fast = stages.TakeWhile(value => value == ContinuousFireStage.Fast).Count();
        var slow = stages.Skip(fast).TakeWhile(value => value == ContinuousFireStage.Slow).Count();
        Check.That(world.InFrames(fast) is >= 30 and <= 36, "The fast stage outlasts the last shot by the coast time (30 ticks past the next possible shot): " + fast);
        Check.That(slow == world.Frames(30 + 1), "The slow stage lasts a second and the logic frame its end is noticed in: " + slow);
        Check.That(stages.Skip(fast + slow).All(value => value == ContinuousFireStage.None) && tank.WeaponBonusConditions == 0, "Then no stage is left.");

        // After the cool-down the count starts over.
        var again = new List<(long Tick, ContinuousFireStage After)>();
        Fire(world, feeder, north, 60, again);
        Check.That(again[0].After == ContinuousFireStage.None && again[2].After == ContinuousFireStage.None && again[3].After == ContinuousFireStage.Mean, "After the cool-down the weapon spins up from the start.");

        // A weapon without stages never gets a stage.
        for (var i = 0; i < world.Frames(100); i++)
        {
            feeder.FireWeapon(1, plain.Id.Value, north);
            world.Step();
        }
        Check.That(SimulationWorld.ContinuousFireOf(plain) == ContinuousFireStage.None && plain.WeaponBonusConditions == 0, "A weapon without ContinuousFireOne/Two has no stages.");
    }

    /// <summary><c>PreAttackDelay</c>: the weapon winds up before the shot (<c>Weapon::preFireWeapon</c>), per shot, per attack or per clip.</summary>
    public static void PreAttackDelayWindsUpBeforeTheShot()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: FiringTrackerFiles());
        Check.That(fixture.Templates.Weapons.TryGet("ClipWindupGun", out var clipGun) && clipGun.PreAttackDelayTicks == TestRate.Frames(15) && clipGun.PreAttackType == WeaponPrefire.PerClip, "PreAttackDelay (500 ms = 15 ticks) and PreAttackType are read.");
        Check.That(fixture.Templates.Weapons.TryGet("SpinGun", out var spinGun) && spinGun.PreAttackDelayTicks == 0 && spinGun.PreAttackType == WeaponPrefire.PerShot, "A weapon without the keys has no delay (default PER_SHOT).");

        List<long> Shots(string template, int frames, out SimulationWorld world, out Feeder feeder, Action<SimulationWorld, Feeder, Entity, int>? each = null, bool twoVictims = false)
        {
            world = NewWorld(fixture);
            feeder = new Feeder(world);
            feeder.Spawn(1, template, owner: 1, x: 0, y: 0);
            feeder.Spawn(2, "TurretTarget", owner: 2, x: 0, y: 100);
            feeder.Spawn(2, "TurretTarget", owner: 2, x: 100, y: 0);
            world.Step();
            var tank = Slotted(world, template);
            var north = IdOf(world, "TurretTarget", 0);
            var east = IdOf(world, "TurretTarget", 1);
            var shots = new List<long>();
            var start = world.Tick;
            for (var i = 0; i < world.Frames(frames); i++)
            {
                each?.Invoke(world, feeder, tank, i);
                feeder.FireWeapon(1, tank.Id.Value, twoVictims && i >= world.Frames(frames) / 2 ? east : north);
                var result = world.Step();
                if (result.Events.Any(value => value.Kind == SimEventKind.WeaponFired && value.Entity == tank.Id)) shots.Add(world.InFrames(result.Tick - start));
            }
            return shots;
        }

        // Per shot: 15 ticks of wind-up before every shot, on top of the 30 ticks between shots.
        var windup = new List<int>();
        var perShot = Shots("WindupTank", 100, out _, out _, (world, _, tank, i) => windup.Add(world.PreAttackSlotOf(tank)));
        Check.That(perShot.SequenceEqual([15L, 60L]), "PER_SHOT: the first shot after the wind-up, the next after reload plus wind-up: " + string.Join(",", perShot));
        Check.That(windup.Skip(1).Take(TestRate.Frames(15)).All(slot => slot == 0) && windup[TestRate.Frames(16)] == -1 && windup[TestRate.Frames(30)] == -1 && windup[TestRate.Frames(46)] == 0, "While it winds up the object reports its slot (PREATTACK_A), not afterwards: " + string.Join("", windup.Take(50).Select(slot => slot < 0 ? "-" : "A")));

        // Per attack: only the first shot at a victim waits; another victim waits again.
        var perAttack = Shots("AttackWindupTank", 200, out _, out _, twoVictims: true);
        Check.That(perAttack.Take(3).SequenceEqual([15L, 45L, 75L]), "PER_ATTACK: only the first shot at the victim winds up: " + string.Join(",", perAttack));
        var afterSwitch = perAttack.Where(tick => tick >= 100).ToArray();
        // The weapon is ready 30 ticks after the last shot at the first victim; from then (or from the switch at tick 100) the new victim costs a wind-up.
        Check.That(afterSwitch.Length >= 2 && afterSwitch[0] - Math.Max(100, perAttack.Last(tick => tick < 100) + 30) == 15, "A new victim winds up again: " + string.Join(",", perAttack));
        Check.That(afterSwitch[1] - afterSwitch[0] == 30, "...once: " + string.Join(",", perAttack));

        // Per clip: the first shot of each clip of three waits.
        var perClip = Shots("ClipWindupTank", 150, out _, out _);
        Check.That(perClip.Take(6).SequenceEqual([15L, 21L, 27L, 102L, 108L, 114L]), "PER_CLIP: wind-up, three shots 6 ticks apart, 60 ticks of reload, wind-up again: " + string.Join(",", perClip));

        // Another order drops the wind-up; the next attack starts it afresh. A snapshot taken mid-wind-up continues identically.
        var moved = Shots("WindupTank", 60, out var world2, out var feeder2, (world, feeder, tank, i) =>
        {
            if (i == world.Frames(8)) feeder.SetMoveTarget(1, tank.Id.Value, 5, 0);
        });
        Check.That(moved.SequenceEqual([8L + 15L]), "A move order during the wind-up drops it; the fire order of the same tick winds up afresh: " + string.Join(",", moved));
        var tank2 = Slotted(world2, "WindupTank");
        var victim2 = IdOf(world2, "TurretTarget", 0);
        while (world2.PreAttackSlotOf(tank2) < 0) { feeder2.FireWeapon(1, tank2.Id.Value, victim2); world2.Step(); }
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world2), fixture.Templates, fixture.Fingerprint);
        var fork = feeder2.ForkTo(restored);
        Check.That(restored.PreAttackSlotOf(restored.FindEntity(tank2.Id)!) == 0, "The wind-up is part of the snapshot.");
        for (var i = 0; i < world2.Frames(60); i++)
        {
            feeder2.FireWeapon(1, tank2.Id.Value, victim2);
            fork.FireWeapon(1, tank2.Id.Value, victim2);
            Check.That(world2.Step().StateHash == restored.Step().StateHash, "The restored world winds up and fires identically at step " + i + ".");
        }

        // A script's one-off command fires at once.
        var scripted = NewWorld(fixture);
        var scriptFeeder = new Feeder(scripted);
        scriptFeeder.Spawn(1, "WindupTank", owner: 1, x: 0, y: 0);
        scriptFeeder.Spawn(2, "TurretTarget", owner: 2, x: 0, y: 100);
        scripted.Step();
        scriptFeeder.FireWeaponSlot(1, IdOf(scripted, "WindupTank"), IdOf(scripted, "TurretTarget"), WeaponSlot.Best, FireOrigin.Script);
        Check.That(scripted.Step().Events.Any(value => value.Kind == SimEventKind.WeaponFired), "A script command button fires without the wind-up.");
    }
}