using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// F-76: a turret whose victim dies holds its angle for <c>RecenterTime</c> and then recentres. Above 30 Hz a turret that stands still decides in every tick, and a victim killed in a
/// frame's first tick after the turrets' phase (by delayed damage) was seen dead in the frame's second tick: the hold counted from there and the recentre began a tick early.
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> TurretHoldFiles() => new Dictionary<string, string>(TurretAttackFiles())
    {
        ["Data/INI/Object/TurretHoldFixture.ini"] = """
        Weapon KillerGun
          PrimaryDamage = 1000
          WeaponSpeed = 600
          AttackRange = 150.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 300
        End
        Object KillerTank
          KindOf = SELECTABLE VEHICLE
          VisionRange = 200.0
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY KillerGun
          End
          Behavior = AIUpdateInterface AI01
            AutoAcquireEnemiesWhenIdle = Yes
            MoodAttackCheckRate = 250
            Turret
              TurretTurnRate = 180
              AllowsPitch = No
              RecenterTime = 500
              ControlledWeaponSlots = PRIMARY
            End
          End
        End
        """,
    };

    /// <summary>
    /// A tank finds a fragile victim by itself, turns its turret to it and kills it with one shot whose damage arrives later (the delayed damage, after the turrets' phase, in a frame's
    /// first tick); then the turret holds and recentres. Its angle at the end of every frame of the original is the same at
    /// 30, 60 and 120 Hz. Before, above 30 Hz the recentre began a tick early and the turret stood a share of a step further at the end of that frame.
    /// </summary>
    public static void TurretHoldsAfterItsKillFromTheNextFrame()
    {
        string? reference = null;
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var fixture = new SyntheticFixture(rate, files: TurretHoldFiles());
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
            var feeder = new Feeder(world);
            feeder.Spawn(1, "KillerTank", owner: 1, x: 0, y: 0);
            feeder.Spawn(2, "FragileRunner", owner: 2, x: 0, y: 100);
            world.Step();
            var tank = Slotted(world, "KillerTank");
            var victim = IdOf(world, "FragileRunner");
            var killed = -1L;
            for (var step = 0; step < world.Frames(80) && killed < 0; step++)
            {
                world.Step();
                if (world.FindEntity(new EntityId(victim)) is not { EffectivelyDead: false }) killed = (world.Tick - 1) / world.Time.TicksPerOriginalFrame;
            }
            Check.That(killed > 0, $"{rate}: the tank killed its victim.");
            // The turret's angle at the end of every frame of the original after the kill, by the number of the frame.
            var frames = new List<string>();
            var recentred = false;
            for (var step = 0; step < world.Frames(60); step++)
            {
                world.Step();
                if (!world.Time.IsOriginalFrameBoundary(world.Tick)) continue;
                var turret = tank.TurretOf(0)!.Value;
                if (world.Tick / world.Time.TicksPerOriginalFrame - 1 - killed is < 1 or > 50) continue;
                frames.Add($"{world.Tick / world.Time.TicksPerOriginalFrame - 1 - killed}:{turret.Angle.Raw}");
                recentred |= turret.Rotating;
            }
            Check.That(recentred, $"{rate}: the turret recentred after its hold.");
            var line = $"kill {killed}; " + string.Join(";", frames);
            if (reference is null) reference = line;
            else
            {
                var a = line.Split(';');
                var b = reference.Split(';');
                var first = Enumerable.Range(0, Math.Min(a.Length, b.Length)).FirstOrDefault(index => a[index] != b[index], -1);
                Check.That(first < 0 && a.Length == b.Length, $"{rate}: the turret stands where 30 Hz has it at the end of every frame (first difference: {(first < 0 ? "-" : a[first] + " against " + b[first])}).");
            }
        }
    }
}
