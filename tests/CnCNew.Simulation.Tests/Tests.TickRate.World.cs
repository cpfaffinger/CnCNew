using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Phase 20, stage 2, the world (<c>src/CnCNew.Simulation/World</c>, <c>Templates</c>): the durations and rhythms the world used to write as bare tick numbers are logic frames of the
/// original now. At 60 and 120 Hz each of them is exactly 2 and 4 times its 30 Hz tick count, and what the world rounds or draws at run time stays on the original's grid.
/// The 30 Hz numbers themselves are pinned by the older tests (179 ticks for a sale, 45 ticks of interest, 18/9/4 ticks between shots …), which run unchanged.
/// </summary>
static partial class Tests
{
    private static readonly (SimulationRate Rate, int Factor)[] WorldRates = [(SimulationRate.Hz30, 1), (SimulationRate.Hz60, 2), (SimulationRate.Hz120, 4)];

    private static (SimulationWorld World, Feeder Feeder, SyntheticFixture Fixture) RatedWorld(SimulationRate rate, IReadOnlyDictionary<string, string> files, TerrainMap? terrain = null, uint seed = 1)
    {
        var fixture = new SyntheticFixture(rate, files: files);
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, seed, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint, terrain?.Fingerprint ?? ""), fixture.Templates, terrain);
        return (world, new Feeder(world), fixture);
    }

    // 60 units per second: 2 per logic frame of the original, exactly 1 and 0.5 per tick at 60 and 120 Hz. NoDelayGun has no DelayBetweenShots.
    private static IReadOnlyDictionary<string, string> RatedWorldFiles() => new Dictionary<string, string>(FiringTrackerFiles())
    {
        ["Data/INI/Object/TickRateWorldFixture.ini"] = """
        Weapon FlightGun
          PrimaryDamage = 1
          AttackRange = 500.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 1000
          WeaponSpeed = 60
        End
        Weapon NoDelayGun
          PrimaryDamage = 1
          AttackRange = 500.0
          DamageType = EXPLOSION
          ClipSize = 0
        End
        Weapon OddBonusGun
          PrimaryDamage = 1
          AttackRange = 150.0
          DamageType = EXPLOSION
          ClipSize = 4
          ClipReloadTime = 700
          DelayBetweenShots = 500
          PreAttackDelay = 500
          PreAttackType = PER_SHOT
          WeaponBonus = CONTINUOUS_FIRE_MEAN RATE_OF_FIRE 130%
          WeaponBonus = CONTINUOUS_FIRE_FAST RATE_OF_FIRE 370%
          WeaponBonus = CONTINUOUS_FIRE_MEAN PRE_ATTACK 70%
          WeaponBonus = CONTINUOUS_FIRE_FAST PRE_ATTACK 33%
        End
        Object FlightTank
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY FlightGun
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """,
    };

    /// <summary>Class A (<c>BuildAssistant</c>): 180 logic frames from the sell order to the refund, less the one frame by which the refund comes early – 179, 358 and 716 ticks (the same 5.9667 s at every rate).</summary>
    public static void TickRateWorldSaleTakesTheSameGameTime()
    {
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, ConstructionFiles());
            feeder.Spawn(1, "Plant", x: 50, y: 50);
            world.Step();
            var plant = world.FindEntity(new EntityId(1))!;
            Check.That(feeder.Sell(1, 1).Accepted, $"{rate}: the owner sells a finished structure.");
            var start = 0L;
            var sold = -1L;
            for (var tick = 0; tick < 1_000 && sold < 0; tick++)
            {
                var result = world.Step();
                if (tick == 0) start = world.Tick;
                if (result.Events.Any(value => value.Kind == SimEventKind.StructureSold && value.Entity == plant.Id)) sold = world.Tick - start;
            }

            Check.That(sold == 179 * factor, $"{rate}: the refund arrives {179 * factor} ticks after the order (measured {sold}).");
        }
    }

    /// <summary>
    /// Class C: the shroud is recomputed every three logic frames of the original, after the last tick of the frame (phase 0 at 30 Hz): a looker that appears in the first tick of
    /// frame 1 is seen after the last tick of frame 3 – ticks 3, 7 and 15 – which is the same game time as the 30 Hz refresh of tick 3 shows (the state after frame 3).
    /// </summary>
    public static void TickRateWorldVisionKeepsItsRhythm()
    {
        foreach (var (rate, factor) in WorldRates)
        {
            var terrain = NavTerrain();
            var (world, feeder, _) = RatedWorld(rate, VisionFiles(), terrain);
            Check.That(world.VisionInterval == SimulationWorld.VisionIntervalFrames * factor && SimulationWorld.VisionIntervalFrames == 3, $"{rate}: the vision interval is {3 * factor} ticks.");
            for (var step = 0; step < factor; step++) world.Step();
            feeder.Spawn(1, "Scout", x: 100, y: 100);
            var seenAfterTick = -1L;
            for (var step = 0; step < 40 && seenAfterTick < 0; step++)
            {
                var result = world.Step();
                if (world.Vision!.IsVisible(1, 10, 10)) seenAfterTick = result.Tick;
            }

            Check.That(seenAfterTick == 4 * factor - 1, $"{rate}: a looker that appeared in frame 1 is seen with the refresh after the last tick of frame 3, tick {4 * factor - 1} (measured {seenAfterTick}).");
        }
    }

    /// <summary>
    /// W1 (<c>WeaponTemplate::getDelayBetweenShots</c>, <c>getClipReloadTime</c>, <c>getPreAttackDelay</c>): the division by the rate-of-fire bonus and the product with the pre-attack
    /// bonus are rounded in logic frames of the original, so every weapon under every bonus takes exactly 2 and 4 times its 30 Hz ticks; a weapon without a delay fires once per frame.
    /// </summary>
    public static void TickRateWorldWeaponDelaysAreRoundedOnTheOriginalGrid()
    {
        var thirty = new SyntheticFixture(SimulationRate.Hz30, files: RatedWorldFiles());
        var time30 = SimulationRate.Hz30.Time;
        uint[] conditions = [0, 1u << WeaponBonusTable.ContinuousFireMeanCondition, 1u << WeaponBonusTable.ContinuousFireFastCondition];
        foreach (var (rate, factor) in WorldRates)
        {
            var fixture = rate == SimulationRate.Hz30 ? thirty : new SyntheticFixture(rate, files: RatedWorldFiles());
            var time = rate.Time;
            var compared = 0;
            var bonused = 0;
            foreach (var weapon in fixture.Templates.Weapons.Weapons)
            {
                var original = thirty.Templates.Weapons.Weapons[weapon.Index];
                Check.That(weapon.Name == original.Name && weapon.DelayBetweenShotsTicks == factor * original.DelayBetweenShotsTicks && weapon.DelayBetweenShotsTicks >= factor,
                    $"{rate}: {weapon.Name} waits {weapon.DelayBetweenShotsTicks} ticks between shots, expected {factor} x {original.DelayBetweenShotsTicks} and at least one logic frame.");
                foreach (var condition in conditions)
                {
                    var delay = weapon.DelayBetweenShotsWith(time, fixture.Templates.GlobalWeaponBonus, condition);
                    var delay30 = original.DelayBetweenShotsWith(time30, thirty.Templates.GlobalWeaponBonus, condition);
                    var reload = weapon.ClipReloadWith(time, fixture.Templates.GlobalWeaponBonus, condition);
                    var reload30 = original.ClipReloadWith(time30, thirty.Templates.GlobalWeaponBonus, condition);
                    var windUp = weapon.PreAttackDelayWith(time, fixture.Templates.GlobalWeaponBonus, condition);
                    var windUp30 = original.PreAttackDelayWith(time30, thirty.Templates.GlobalWeaponBonus, condition);
                    Check.That(delay == factor * delay30 && reload == factor * reload30 && windUp == factor * windUp30,
                        $"{rate}: {weapon.Name} under bonus {condition}: delay {delay} (30 Hz {delay30}), reload {reload} ({reload30}), wind-up {windUp} ({windUp30}) – expected {factor} x.");
                    // 30 Hz: the former expression (floored in ticks, at least one tick; the overloads without a time service computed it until phase 20).
                    var multiplier = Fixed.One + thirty.Templates.GlobalWeaponBonus.Sum(condition, WeaponBonusTable.RateOfFire) + original.Bonuses.Sum(condition, WeaponBonusTable.RateOfFire);
                    long Former(long ticks) => multiplier <= Fixed.Zero ? SimulationTiming.SleepForever : Math.Max(SimulationTiming.SleepNone, (Fixed.FromInt(ticks) / multiplier).Floor());
                    Check.That(delay30 == Former(original.DelayBetweenShotsTicks) && reload30 == (original.ClipReloadTicks == 0 ? 0 : Former(original.ClipReloadTicks)),
                        $"30 Hz: {weapon.Name} under bonus {condition} differs from the tick-rounded form.");
                    if (delay30 != original.DelayBetweenShotsTicks) bonused++;
                    compared++;
                }
            }

            Check.That(compared >= 30 && bonused >= 4, $"{rate}: {compared} weapon/bonus pairs compared, {bonused} of them with a bonus that changes the delay.");
            fixture.Templates.Weapons.TryGet("NoDelayGun", out var noDelay);
            Check.That(noDelay!.DelayBetweenShotsTicks == factor && noDelay.DelayBetweenShotsWith(time, fixture.Templates.GlobalWeaponBonus, 0) == factor,
                $"{rate}: a weapon without DelayBetweenShots fires once per logic frame of the original ({noDelay.DelayBetweenShotsTicks} ticks).");
            // The case that tells the two roundings apart: 18 frames at 400 % are 4.5 -> 4 frames (8 ticks at 60 Hz); rounded in ticks it would be 9.
            fixture.Templates.Weapons.TryGet("SpinGun", out var spin);
            Check.That(spin!.DelayBetweenShotsWith(time, fixture.Templates.GlobalWeaponBonus, conditions[2]) == 4 * factor, $"{rate}: the fast stage of the spin gun is 4 logic frames.");
        }
    }

    /// <summary>The same through the world (<c>ConsumeShot</c>, the firing tracker): the series of gaps between the shots of a weapon that spins up is the 30 Hz series times 2 and 4.</summary>
    public static void TickRateWorldSpinUpKeepsItsShotTimes()
    {
        long[]? reference = null;
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, RatedWorldFiles());
            feeder.Spawn(1, "SpinTank", owner: 1, x: 0, y: 0);
            feeder.Spawn(2, "TurretTarget", owner: 2, x: 0, y: 100);
            world.Step();
            var tank = Slotted(world, "SpinTank");
            var victim = IdOf(world, "TurretTarget");
            var shots = new List<long>();
            for (var i = 0; i < 120 * factor; i++)
            {
                feeder.FireWeapon(1, tank.Id.Value, victim);
                var result = world.Step();
                if (result.Events.Any(value => value.Kind == SimEventKind.WeaponFired && value.Entity == tank.Id)) shots.Add(result.Tick);
            }

            var gaps = shots.Zip(shots.Skip(1), (a, b) => b - a).ToArray();
            reference ??= gaps;
            Check.That(gaps.Length == reference.Length && gaps.Length >= 11 && gaps.Zip(reference, (gap, original) => gap == factor * original).All(same => same),
                $"{rate}: the gaps between shots are {factor} x the 30 Hz gaps: {string.Join(",", gaps)} against {string.Join(",", reference)}.");
        }
    }

    /// <summary>
    /// W1 (<c>WeaponTemplate::fireWeaponTemplate</c>, <c>delayInFrames</c>): a shot without a projectile object lands after a whole number of logic frames of flight – 50 frames for
    /// 100 units at 2 units per frame – and "at once" means closer than one frame of flight at every rate (1 unit at 2 units per frame; in ticks of 60 Hz it would be a delayed hit).
    /// </summary>
    public static void TickRateWorldFlightTimeIsCountedInFrames()
    {
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, RatedWorldFiles());
            feeder.Spawn(1, "FlightTank", owner: 1, x: 0, y: 0);
            feeder.Spawn(2, "TurretTarget", owner: 2, x: 0, y: 100);
            feeder.Spawn(2, "TurretTarget", owner: 2, x: 1, y: 0);
            world.Step();
            var tank = Slotted(world, "FlightTank");
            var far = IdOf(world, "TurretTarget", 0);
            var near = Slotted(world, "TurretTarget", 1);
            feeder.FireWeapon(1, tank.Id.Value, far);
            var result = world.Step();
            var launched = result.Events.Where(value => value.Kind == SimEventKind.ProjectileLaunched && value.Other == tank.Id).ToArray();
            Check.That(launched.Length == 1 && launched[0].Value - result.Tick == 50 * factor, $"{rate}: 100 units at 2 per logic frame land {50 * factor} ticks later (measured {(launched.Length == 1 ? launched[0].Value - result.Tick : -1)}).");

            for (var i = 0; i < 31 * factor; i++) world.Step();
            var before = near.Body!.Health;
            feeder.FireWeapon(1, tank.Id.Value, near.Id.Value);
            result = world.Step();
            Check.That(result.Events.Any(value => value.Kind == SimEventKind.WeaponFired && value.Entity == tank.Id) && near.Body.Health < before
                       && !result.Events.Any(value => value.Kind == SimEventKind.ProjectileLaunched && value.Other == tank.Id),
                $"{rate}: a victim closer than one logic frame of flight is hit in the tick of the shot.");
        }
    }

    /// <summary>
    /// Class B (the turrets' own deadlines) and W1 (the idle scan's random interval): a standing attacker gives up a victim out of reach after 45 logic frames; an idle turret that
    /// glances around waits a random number of logic frames between 30 and 60 (1000..2000 ms), so the wait in ticks is a multiple of the ticks per frame.
    /// </summary>
    public static void TickRateWorldTurretDeadlinesAndScanStayOnTheGrid()
    {
        foreach (var (rate, factor) in WorldRates)
        {
            // A victim that outruns a tank that stands still: the turret step that first sees it out of reach starts the deadline, 45 logic frames later the attack is over.
            var (world, feeder, _) = RatedWorld(rate, TurretAttackFiles());
            feeder.Spawn(1, "TurretTank", owner: 1, x: 0, y: 0);
            feeder.Spawn(2, "RunnerTarget", owner: 2, x: 0, y: 100);
            world.Step();
            var tank = Slotted(world, "TurretTank");
            var runner = Slotted(world, "RunnerTarget");
            var waited = TicksUntilShot(world, feeder, tank.Id.Value, runner.Id.Value, limit: 80 * factor);
            Check.That(waited > 0 && world.AttackTargetOf(tank) == runner.Id, $"{rate}: the tank attacks (first shot after {waited} ticks).");
            feeder.SetMoveTarget(2, runner.Id.Value, 0, 400);
            long outOfRange = -1;
            long lost = -1;
            for (var i = 0; i < 300 * factor && lost < 0; i++)
            {
                world.Step();
                // The turret decides once per logic frame and reads the runner where the frame it has begun ends (the position at 30 Hz): so does the measurement.
                var (runnerX, runnerY, _) = runner.FrameEnd(world.Tick - 1);
                var (apartX, apartY) = (runnerX - tank.X, runnerY - tank.Y);
                if (outOfRange < 0 && apartX * apartX + apartY * apartY > Fixed.FromInt(150 * 150)) outOfRange = i;
                if (!world.AttackTargetOf(tank).IsValid) lost = i;
            }

            Check.That(outOfRange > 0 && lost - outOfRange == 45 * factor, $"{rate}: the victim left weapon range at step {outOfRange}; the tank gave up at step {lost}, expected {45 * factor} ticks later.");

            // Idle scan: the gaps between coming to rest and the next glance.
            var (scanWorld, scanFeeder, _) = RatedWorld(rate, TurretAttackFiles());
            scanFeeder.Spawn(1, "ScanTank", owner: 1, x: 0, y: 0);
            scanWorld.Step();
            var scanner = Slotted(scanWorld, "ScanTank");
            var angles = new List<Fixed>();
            for (var i = 0; i < 1_500 * factor; i++)
            {
                scanWorld.Step();
                angles.Add(scanner.TurretOf(0)!.Value.Angle);
            }

            var gaps = new List<int>();
            var restSince = -1;
            for (var index = 1; index < angles.Count; index++)
            {
                if (angles[index] == Fixed.Zero && angles[index - 1] != Fixed.Zero) restSince = index;
                else if (angles[index] != Fixed.Zero && angles[index - 1] == Fixed.Zero && restSince >= 0) gaps.Add(index - restSince);
            }

            // Phase 23: the turret walks the step of a logic frame in equal parts, one per tick. It is back at rest in the last tick of its frame and leaves in the first tick
            // of one, so the ticks at rest are a whole number of frames less the ticks the last step still took (none at 30 Hz).
            var walked = factor - 1;
            Check.That(gaps.Count >= 8 && gaps.All(gap => (gap + walked) % factor == 0 && gap + walked >= 30 * factor && gap + walked <= 60 * factor) && gaps.Distinct().Count() > 1,
                $"{rate}: the turret waits a random whole number of logic frames between 30 and 60 before each glance: {string.Join(",", gaps)} ticks.");
        }
    }

    /// <summary>
    /// Class B (<c>ForceIdleMSEC</c>, two logic frames) and W1 (the random shift of the first mood check, drawn in logic frames): an idle unit's first look, and with it its first
    /// shot, falls on a tick that is a whole number of logic frames after it became idle, and the lose-interest deadline of a turret is 45 frames.
    /// </summary>
    public static void TickRateWorldAcquisitionLooksOnTheGrid()
    {
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, AcquisitionFiles());
            feeder.Spawn(1, "AcqSoldier", owner: 1, x: 0, y: 0);
            feeder.Spawn(2, "AcqDummy", owner: 2, x: 60, y: 0);
            world.Step();
            var soldier = Slotted(world, "AcqSoldier");
            var firstShot = -1L;
            for (var step = 0; step < 40 * factor && firstShot < 0; step++)
            {
                var result = world.Step();
                if (ShotsOf(result, soldier) > 0) firstShot = result.Tick;
            }

            // The unit appears in tick 0; its first logic frame begins one frame later, notes the idle state and schedules the look two logic frames after that: three frames
            // after the appearance at every rate (tick 3 at 30 Hz).
            Check.That(firstShot == 3 * factor, $"{rate}: the idle soldier looks two logic frames after it became idle and fires at once (first shot at tick {firstShot}, expected {3 * factor}).");
        }
    }
}
