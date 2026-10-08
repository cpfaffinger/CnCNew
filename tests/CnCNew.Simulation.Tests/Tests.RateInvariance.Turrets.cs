using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Phase 23, package turrets (docs/RATE-INVARIANCE-TURRETS.md): turrets, the fire gate, the firing tracker and the units' own target acquisition are the same game at 30, 60 and
/// 120 Hz. Each test runs one small scene of the synthetic fixture at the three rates and compares game time and angles; 30 Hz is the reference.
/// </summary>
static partial class Tests
{
    /// <summary>
    /// The turret decides its step once per logic frame of the original exactly as at 30 Hz and walks it in equal parts: at the end of every frame its angle is the 30 Hz angle to the
    /// bit, in between it is on the way, and the shot is released in the same frame.
    /// </summary>
    public static void RateInvarianceTurretStepsAreDecidedPerFrame()
    {
        const int Frames = 40;
        var runs = new List<(SimulationRate Rate, int Factor, int Shot, List<Fixed> Angles)>();
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, TurretAttackFiles());
            feeder.Spawn(1, "TurretTank", owner: 1, x: 0, y: 0);
            feeder.Spawn(2, "TurretTarget", owner: 2, x: 0, y: 100);
            for (var i = 0; i < factor; i++) world.Step();
            var tank = Slotted(world, "TurretTank");
            var target = IdOf(world, "TurretTarget");
            var angles = new List<Fixed>();
            var shot = -1;
            for (var step = 0; step < Frames * factor; step++)
            {
                feeder.FireWeapon(1, tank.Id.Value, target);
                var result = world.Step();
                if (shot < 0 && result.Events.Any(value => value.Kind == SimEventKind.WeaponFired && value.Entity == tank.Id)) shot = step;
                angles.Add(tank.TurretOf(0)!.Value.Angle);
            }

            runs.Add((rate, factor, shot, angles));
            // The counter of held-back orders (part of the hash) counts logic frames of waiting: half a second of orders every tick is 15 at every rate.
            Check.That(world.TurretWaits == 15, $"{rate}: the orders waited 15 logic frames for the turret ({world.TurretWaits} counted).");
        }

        var reference = runs[0];
        Check.That(reference.Shot == 15, $"30 Hz: 90 degrees at 6 degrees per frame release the shot 15 frames after the first order (got {reference.Shot}).");
        foreach (var (rate, factor, shot, angles) in runs.Skip(1))
        {
            Check.That(shot == reference.Shot * factor, $"{rate}: the shot is released in the same frame as at 30 Hz (step {shot}, expected {reference.Shot * factor}).");
            for (var frame = 0; frame < Frames; frame++)
            {
                var atEnd = angles[(frame + 1) * factor - 1];
                Check.That(atEnd == reference.Angles[frame], $"{rate}: at the end of frame {frame} the turret is where the 30 Hz turret is ({atEnd.Raw} raw, 30 Hz {reference.Angles[frame].Raw}).");
                if (frame >= 14) continue;
                // Still turning: every tick inside the frame moves the turret further toward that angle.
                for (var part = 0; part < factor - 1; part++)
                {
                    var inside = angles[frame * factor + part];
                    var before = frame == 0 ? Fixed.Zero : reference.Angles[frame - 1];
                    Check.That(inside > before && inside < atEnd && (part == 0 || inside > angles[frame * factor + part - 1]), $"{rate}: frame {frame}, tick {part}: the turret is on its way ({inside.Raw} raw between {before.Raw} and {atEnd.Raw}).");
                }
            }
        }
    }

    /// <summary>
    /// <c>FiringTracker::update</c> acts in the step after its deadline, and that step is a logic frame at every rate: a spun-up weapon that falls silent drops its stage coast + 1
    /// frames after its next possible shot and shows the slow stage for a second and a frame, at the same game time at 30, 60 and 120 Hz.
    /// </summary>
    public static void RateInvarianceFiringTrackerDeadlinesTakeAFrame()
    {
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, RatedWorldFiles());
            feeder.Spawn(1, "SpinTank", owner: 1, x: 0, y: 0);
            feeder.Spawn(2, "TurretTarget", owner: 2, x: 0, y: 100);
            for (var i = 0; i < factor; i++) world.Step();
            var tank = Slotted(world, "SpinTank");
            var victim = IdOf(world, "TurretTarget");
            var shots = 0;
            long lastShot = -1;
            for (var i = 0; i < 300 * factor && shots < 8; i++)
            {
                feeder.FireWeapon(1, tank.Id.Value, victim);
                var result = world.Step();
                if (!result.Events.Any(value => value.Kind == SimEventKind.WeaponFired && value.Entity == tank.Id)) continue;
                shots++;
                lastShot = result.Tick;
            }

            Check.That(shots == 8 && SimulationWorld.ContinuousFireOf(tank) == ContinuousFireStage.Fast, $"{rate}: eight shots in a row spin the weapon up to the fast stage.");
            var changes = new List<(long After, ContinuousFireStage Stage)>();
            var stage = ContinuousFireStage.Fast;
            for (var i = 0; i < 120 * factor; i++)
            {
                var result = world.Step();
                var now = SimulationWorld.ContinuousFireOf(tank);
                if (now == stage) continue;
                changes.Add((result.Tick - lastShot, now));
                stage = now;
            }

            // 600 ms between shots are 18 frames, 4 at the fast stage; the coast is 30 frames: the stage drops in frame 4 + 30 + 1 after the last shot, the slow stage ends 30 + 1 frames later.
            Check.That(changes.Count == 2 && changes[0] == (35 * factor, ContinuousFireStage.Slow) && changes[1] == (66 * factor, ContinuousFireStage.None),
                $"{rate}: silence drops the stage after 35 logic frames and ends the slow stage after 66: {string.Join(", ", changes.Select(change => $"{change.Stage} after {change.After} ticks"))}.");
        }
    }

    /// <summary>
    /// A wind-up nobody followed up on is forgotten a second after its end - until and including the last tick of that logic frame at every rate: an order in frame 45 after a
    /// 15-frame wind-up began still fires the wound-up weapon at once, an order in frame 46 winds it up again.
    /// </summary>
    public static void RateInvarianceStaleWindUpIsForgottenAfterTheSameFrame()
    {
        foreach (var (rate, factor) in WorldRates)
            foreach (var (afterTicks, fires) in new[] { (45 * factor, true), (45 * factor + factor - 1, true), (46 * factor, false) })
            {
                var (world, feeder, _) = RatedWorld(rate, RatedWorldFiles());
                feeder.Spawn(1, "WindupTank", owner: 1, x: 0, y: 0);
                feeder.Spawn(2, "TurretTarget", owner: 2, x: 100, y: 0);
                for (var i = 0; i < factor; i++) world.Step();
                var tank = Slotted(world, "WindupTank");
                var victim = IdOf(world, "TurretTarget");
                feeder.FireWeapon(1, tank.Id.Value, victim);
                var first = world.Step();
                Check.That(!first.Events.Any(value => value.Kind == SimEventKind.WeaponFired) && world.PreAttackSlotOf(tank) == 0, $"{rate}: the first order starts the wind-up.");
                for (var i = 1; i < afterTicks; i++) world.Step();
                feeder.FireWeapon(1, tank.Id.Value, victim);
                var second = world.Step();
                var fired = second.Events.Any(value => value.Kind == SimEventKind.WeaponFired && value.Entity == tank.Id);
                Check.That(fired == fires, $"{rate}: an order {afterTicks} ticks after the wind-up began {(fires ? "fires the wound-up weapon" : "winds up again")} (fired: {fired}).");
            }
    }

    private static IReadOnlyDictionary<string, string> RateInvarianceFiles() => new Dictionary<string, string>(RatedWorldFiles())
    {
        ["Data/INI/Object/RateInvarianceTurretsFixture.ini"] = """
        Weapon NoReloadClipGun
          PrimaryDamage = 1
          AttackRange = 150.0
          DamageType = EXPLOSION
          ClipSize = 2
          DelayBetweenShots = 300
        End
        Object NoReloadTank
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY NoReloadClipGun
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """,
    };

    /// <summary>
    /// A clip without a reload time is full at once and the weapon fires again in the next logic frame: with an order every tick the gaps between the shots of a two-round clip are
    /// 9 frames (the shot delay) and 1 frame, at every rate. (With a wait of 0 ticks the second gap was one tick: two and four shots per frame at 60 and 120 Hz.)
    /// </summary>
    public static void RateInvarianceClipWithoutReloadTimeFiresOncePerFrame()
    {
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, RateInvarianceFiles());
            feeder.Spawn(1, "NoReloadTank", owner: 1, x: 0, y: 0);
            feeder.Spawn(2, "TurretTarget", owner: 2, x: 100, y: 0);
            for (var i = 0; i < factor; i++) world.Step();
            var tank = Slotted(world, "NoReloadTank");
            var victim = IdOf(world, "TurretTarget");
            var shots = new List<long>();
            for (var i = 0; i < 60 * factor; i++)
            {
                feeder.FireWeapon(1, tank.Id.Value, victim);
                var result = world.Step();
                if (result.Events.Any(value => value.Kind == SimEventKind.WeaponFired && value.Entity == tank.Id)) shots.Add(result.Tick);
            }

            var gaps = shots.Zip(shots.Skip(1), (a, b) => b - a).ToArray();
            Check.That(gaps.Length >= 10 && gaps.Select((gap, index) => gap == (index % 2 == 0 ? 9 : 1) * factor).All(same => same),
                $"{rate}: the gaps between shots are 9 and 1 logic frames in turn: {string.Join(",", gaps)} ticks.");
        }
    }

    /// <summary>A capturer and an enemy station of the staged-capture fixture at <paramref name="rate"/>, the capture power ready (as <c>StagedWorld</c> builds them at the default rate).</summary>
    private static (SimulationWorld World, Feeder Feeder, SpecialAbilityUpdate Ability) RatedCapture(SimulationRate rate, int factor, string capturer, int stationX, int stationY, bool upgrade)
    {
        var (world, feeder, _) = RatedWorld(rate, StagedCaptureFiles());
        feeder.Spawn(1, capturer, owner: 1, x: 100, y: 50);
        feeder.Spawn(2, "PlainStation", owner: 2, x: stationX, y: stationY);
        feeder.SetRelationship(1, 2, Relationship.Enemies);
        for (var i = 0; i < factor; i++) world.Step();
        if (upgrade) world.CompleteUpgrade(new PlayerId(1), null, "Upgrade_StagedCapture");
        for (var i = 0; i < 40 * factor; i++) world.Step();
        return (world, feeder, world.FindEntity(new EntityId(1))!.Modules.OfType<SpecialAbilityUpdate>().First());
    }

    /// <summary>
    /// <c>DoCaptureFX</c>: the flash phase is a recursion per logic frame (its step grows with the fraction of the preparation that is over). At every rate the building flashes
    /// four times while the flag goes up for 60 frames, in the same frames of the preparation. (Stepped per tick the phase ran twice and four times as fast: 4 / 9 / 19 flashes.)
    /// </summary>
    public static void RateInvarianceCaptureFlashesInTheSameFrames()
    {
        long[]? reference = null;
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder, ability) = RatedCapture(rate, factor, "StagedCapturer", 200, 50, upgrade: true);
            Check.That(feeder.Capture(1, 1, 2).Accepted, $"{rate}: the capture is accepted.");
            long start = -1;
            var flashes = new List<long>();
            for (var index = 0; index < 600 * factor; index++)
            {
                var result = world.Step();
                if (start < 0 && ability.Stage == AbilityStage.Preparing) start = result.Tick;
                flashes.AddRange(result.Events.Where(value => value.Kind == SimEventKind.CaptureFlash).Select(_ => result.Tick - start));
            }

            Check.That(start >= 0 && flashes.Count == 4 && flashes.All(tick => tick % factor == 0), $"{rate}: four flashes, each in the first tick of a logic frame of the preparation: {string.Join(",", flashes)} ticks after it began.");
            var frames = flashes.Select(tick => tick / factor).ToArray();
            reference ??= frames;
            Check.That(frames.SequenceEqual(reference), $"{rate}: the flashes fall in the same frames of the preparation as at 30 Hz: {string.Join(",", frames)} against {string.Join(",", reference)}.");
        }
    }

    /// <summary>
    /// <c>aiFaceObject</c> of an ability: "within one step of the target" is decided per logic frame against a frame of turn. The Black Lotus turns a quarter circle to her target
    /// and starts to unpack in the same frame at every rate. (Decided per tick against a tick of turn she was done up to a frame early.)
    /// </summary>
    public static void RateInvarianceAbilityFacingEndsInTheSameFrame()
    {
        long reference = -1;
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder, _) = RatedCapture(rate, factor, "StagedLotus", 100, 190, upgrade: false);
            var lotus = world.FindEntity(new EntityId(1))!;
            var abilities = lotus.Modules.OfType<SpecialAbilityUpdate>().ToArray();
            Check.That(lotus.Angle == Fixed.Zero && feeder.Capture(1, 1, 2).Accepted, $"{rate}: the Lotus faces east and takes the order.");
            long firstTurn = -1, unpacking = -1;
            for (var index = 0; index < 400 * factor && unpacking < 0; index++)
            {
                var result = world.Step();
                if (firstTurn < 0 && lotus.Angle != Fixed.Zero) firstTurn = result.Tick;
                if (abilities.Any(ability => ability.Stage == AbilityStage.Unpacking)) unpacking = result.Tick;
            }

            var turning = unpacking - firstTurn;
            Check.That(firstTurn >= 0 && unpacking > firstTurn && turning % factor == 0, $"{rate}: she turns for a whole number of logic frames before she unpacks ({turning} ticks).");
            if (reference < 0) reference = turning;
            Check.That(turning == reference * factor && Near(lotus.Angle, Degrees(90m)), $"{rate}: she faces her target after the same {reference} frames as at 30 Hz ({turning} ticks).");
        }
    }

    /// <summary>Per tick the turret angle of a lone <c>ScanTank</c> (it glances around at random while idle) over <paramref name="frames"/> logic frames after its first frame.</summary>
    private static List<Fixed> IdleScanAngles(SimulationRate rate, int factor, int frames)
    {
        var (world, feeder, _) = RatedWorld(rate, TurretAttackFiles());
        feeder.Spawn(1, "ScanTank", owner: 1, x: 0, y: 0);
        for (var i = 0; i < factor; i++) world.Step();
        var scanner = Slotted(world, "ScanTank");
        var angles = new List<Fixed>();
        for (var step = 0; step < frames * factor; step++)
        {
            world.Step();
            angles.Add(scanner.TurretOf(0)!.Value.Angle);
        }

        return angles;
    }

    /// <summary>The lengths of the runs of ticks in which the turret stood still away from rest (the holds at the scan angles).</summary>
    private static List<int> HoldRuns(List<Fixed> angles)
    {
        var runs = new List<int>();
        var length = 1;
        for (var index = 1; index < angles.Count; index++)
        {
            if (angles[index] == angles[index - 1]) { length++; continue; }
            if (angles[index - 1] != Fixed.Zero && length > 1) runs.Add(length);
            length = 1;
        }

        return runs;
    }

    /// <summary>
    /// "Hold until and including <c>HoldUntil</c>" is the recentre time plus one step. That step is a logic frame of the original at every rate: a turret that reached its scan angle
    /// stands there RecenterTime + 1 frames (16 at 30 Hz), counted from the end of the frame it arrived in to the start of the frame it leaves in.
    /// </summary>
    public static void RateInvarianceTurretHoldsOneFrameMoreThanTheRecentreTime()
    {
        foreach (var (rate, factor) in WorldRates)
        {
            var runs = HoldRuns(IdleScanAngles(rate, factor, 1_500));
            // The turret is at the angle from the last tick of its arrival frame on: factor - 1 ticks of that frame are not part of the run.
            Check.That(runs.Count >= 8 && runs.All(run => run + factor - 1 == 16 * factor),
                $"{rate}: every hold lasts 15 logic frames and one frame more ({string.Join(",", runs.Distinct())} ticks, expected {16 * factor - (factor - 1)}).");
        }
    }

    /// <summary>
    /// The idle scan is the same film at every rate: the first glance starts in the same logic frame after the object appeared (the turret's first step, which sets the timer, is one
    /// frame after the spawn and takes a frame), and from then on the turret is at the 30 Hz angle at the end of every frame, over 50 seconds of glances, holds and returns.
    /// </summary>
    public static void RateInvarianceTurretIdleScanIsTheSameAtEveryFrame()
    {
        const int Frames = 1_500;
        var reference = IdleScanAngles(SimulationRate.Hz30, 1, Frames);
        var firstGlance = reference.FindIndex(angle => angle != Fixed.Zero);
        Check.That(firstGlance > 30 && reference.Distinct().Count() > 50, $"30 Hz: the turret glances around (first glance in frame {firstGlance}).");
        foreach (var (rate, factor) in WorldRates.Skip(1))
        {
            var angles = IdleScanAngles(rate, factor, Frames);
            var first = angles.FindIndex(angle => angle != Fixed.Zero);
            Check.That(first == firstGlance * factor, $"{rate}: the first glance starts in the first tick of the same logic frame as at 30 Hz (tick {first}, expected {firstGlance * factor}).");
            var different = Enumerable.Range(0, Frames).Where(frame => angles[(frame + 1) * factor - 1] != reference[frame]).ToList();
            Check.That(different.Count == 0, $"{rate}: at the end of every logic frame the turret is where the 30 Hz turret is ({different.Count} of {Frames} frames differ, the first is frame {different.FirstOrDefault(-1)}).");
        }
    }

    /// <summary>
    /// The random shift of a unit's second look for a victim (<c>getNextMoodTarget</c>) is drawn from a hash of the world seed, the unit and the logic frame it is drawn in - not
    /// the tick: a unit that looks at the same game time is shifted by the same number of frames at every rate, so its first shot at an enemy that appears later falls in the
    /// same frame. Different seeds still give different shifts.
    /// </summary>
    public static void RateInvarianceMoodCheckShiftIsTheSameAtEveryRate()
    {
        var frames = new List<long>();
        foreach (var seed in new uint[] { 1, 2, 3, 5, 8, 13, 21, 34 })
        {
            long reference = -1;
            foreach (var (rate, factor) in WorldRates)
            {
                var (world, feeder, _) = RatedWorld(rate, AcquisitionFiles(), seed: seed);
                // Spawned in the last tick of frame 0, the soldier is first stepped in the first tick of frame 1 at every rate; its first look is two frames later.
                for (var i = 0; i < factor - 1; i++) world.Step();
                feeder.Spawn(1, "AcqSoldier", owner: 1, x: 0, y: 0);
                world.Step();
                var soldier = Slotted(world, "AcqSoldier");
                for (var i = 0; i < 4 * factor; i++) world.Step();
                // The first look found nobody. The enemy appears now; the second look is one check rate (8 frames) and the random shift (-4 .. 4 frames) after the first.
                feeder.Spawn(2, "AcqDummy", owner: 2, x: 60, y: 0);
                long firstShot = -1;
                for (var step = 0; step < 40 * factor && firstShot < 0; step++)
                {
                    var result = world.Step();
                    if (ShotsOf(result, soldier) > 0) firstShot = result.Tick;
                }

                Check.That(firstShot > 0 && firstShot % factor == 0, $"seed {seed}, {rate}: the second look falls on the first tick of a logic frame (first shot at tick {firstShot}).");
                var frame = firstShot / factor;
                if (reference < 0) reference = frame;
                Check.That(frame == reference && frame is >= 7 and <= 15, $"seed {seed}, {rate}: the shift is the one of 30 Hz (first shot in frame {frame}, 30 Hz frame {reference}).");
            }

            frames.Add(reference);
        }

        Check.That(frames.Distinct().Count() >= 3, $"The shift is random over the seeds: frames {string.Join(",", frames)}.");
    }

    private static IReadOnlyDictionary<string, string> RateInvarianceSlaveFiles() => new Dictionary<string, string>(SlaveFiles())
    {
        ["Data/INI/Object/RateInvarianceSlaveFixture.ini"] = """
        Locomotor RiSlowLoco
          Surfaces = AIR
          Speed = 1.0
          Acceleration = 600.0
          Braking = 600.0
          TurnRate = 360.0
          MinSpeed = 0.0
          PreferredHeight = 20.0
          CloseEnoughDist = 1.0
        End
        Object RiRepairMaster
          KindOf = SELECTABLE VEHICLE
          Locomotor = SET_NORMAL SvFastLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = DefaultProductionExitUpdate Exit01
            UnitCreatePoint = X:0.0 Y:10.0 Z:0.0
          End
          Behavior = SpawnBehavior Spawn01
            SpawnNumber = 1
            SpawnTemplateName = RiSlowDrone
          End
        End
        Object RiSlowDrone
          KindOf = SELECTABLE VEHICLE AIRCRAFT
          Locomotor = SET_NORMAL RiSlowLoco
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = SlavedUpdate Slaved01
            GuardMaxRange = 35
            GuardWanderRange = 35
            RepairRange = 8
            RepairMinAltitude = 18.0
            RepairMaxAltitude = 24.0
            RepairRatePerSecond = 30.0
            RepairWhenBelowHealth% = 60
            RepairMinReadyTime = 100
            RepairMaxReadyTime = 200
            RepairMinWeldTime = 100
            RepairMaxWeldTime = 200
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object RiStuckWanderer
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = WanderAIUpdate Wander01
          End
        End
        """,
    };

    /// <summary>How many values were drawn from <paramref name="stream"/> since its state was <paramref name="before"/> (found by replaying the stream; -1 beyond <paramref name="limit"/>).</summary>
    private static int DrawsSince(uint[] before, CnCNew.Simulation.Random.SageRandom stream, int limit = 100_000)
    {
        var now = new uint[before.Length];
        stream.CopyState(now);
        var replay = new CnCNew.Simulation.Random.SageRandom(0);
        replay.SetState(before);
        var state = new uint[before.Length];
        for (var draws = 0; draws <= limit; draws++)
        {
            replay.CopyState(state);
            if (state.AsSpan().SequenceEqual(now)) return draws;
            replay.NextUInt32();
        }

        return -1;
    }

    private static uint[] StreamState(CnCNew.Simulation.Random.SageRandom stream)
    {
        var state = new uint[6];
        stream.CopyState(state);
        return state;
    }

    /// <summary>
    /// <c>SlavedUpdate::doRepairLogic</c>: a repair drone that is not yet at its master draws a hover altitude from the logic stream and orders a move once per logic frame. A drone
    /// that cannot keep up with its master does that 100 times in 100 frames at every rate (per tick it was 200 and 400 times, and every later draw of the world shifted).
    /// </summary>
    public static void RateInvarianceRepairDroneDrawsOncePerFrame()
    {
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, RateInvarianceSlaveFiles());
            feeder.Spawn(1, "RiRepairMaster", owner: 1, x: 100, y: 100);
            for (var i = 0; i < 20 * factor; i++) world.Step();
            var master = Slotted(world, "RiRepairMaster");
            var drone = Slotted(world, "RiSlowDrone");
            // The drone starts to repair the hurt master beside it; then the master drives away and the drone (one unit per second) is left behind in its repair state,
            // trying to reach it in every update.
            feeder.Damage(master.Id.Value, 70);
            for (var i = 0; i < 30 * factor; i++) world.Step();
            feeder.SetMoveTarget(1, master.Id.Value, 700, 100);
            for (var i = 0; i < 150 * factor; i++) world.Step();
            Check.That(DistanceBetween(master, drone) > 100m && master.Body!.Health < Fixed.FromInt(60), $"{rate}: the drone is far behind its hurt master ({DistanceBetween(master, drone):F0} units).");
            var before = StreamState(world.LogicRandom);
            for (var i = 0; i < 100 * factor; i++) world.Step();
            var draws = DrawsSince(before, world.LogicRandom);
            Check.That(draws == 100, $"{rate}: 100 logic frames of a drone on its way are 100 draws from the logic stream (got {draws}).");
        }
    }

    /// <summary>
    /// <c>WanderAIUpdate</c>: an idle wanderer draws its next point (two values of the logic stream) once per logic frame. One that cannot move stays idle and draws 200 values
    /// in 100 frames at every rate (per tick it was 400 and 800).
    /// </summary>
    public static void RateInvarianceIdleWandererDrawsOncePerFrame()
    {
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, RateInvarianceSlaveFiles());
            feeder.Spawn(1, "RiStuckWanderer", owner: 1, x: 100, y: 100);
            for (var i = 0; i < 2 * factor; i++) world.Step();
            var before = StreamState(world.LogicRandom);
            for (var i = 0; i < 100 * factor; i++) world.Step();
            var draws = DrawsSince(before, world.LogicRandom);
            Check.That(draws == 200, $"{rate}: 100 logic frames of an idle wanderer are 200 draws from the logic stream (got {draws}).");
        }
    }

    /// <summary>
    /// <c>GrantStealthBehavior</c> (the GPS scrambler's marker): the radius grows and the scan runs once per logic frame of the marker's life. Infantry at 25 and at 45 units is
    /// stealthed in the same frames at every rate and the marker is gone after the same frame.
    /// </summary>
    public static void RateInvarianceGrantStealthScansOncePerFrame()
    {
        (long Close, long Outer, long Gone)? reference = null;
        var stealth = ObjectStatusBits.Index("STEALTHED");
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, CratesAndSpecialsFiles());
            feeder.Spawn(1, "VisibleInfantry", owner: 1, x: 25, y: 0);
            feeder.Spawn(1, "VisibleInfantry", owner: 1, x: 45, y: 0);
            for (var i = 0; i < factor; i++) world.Step();
            var close = Slotted(world, "VisibleInfantry", 0);
            var outer = Slotted(world, "VisibleInfantry", 1);
            feeder.Spawn(1, "FixtureScrambler", owner: 1, x: 0, y: 0);
            long closeAt = -1, outerAt = -1, goneAt = -1;
            for (var step = 0; step < 20 * factor; step++)
            {
                world.Step();
                if (closeAt < 0 && close.HasStatus(stealth)) closeAt = step;
                if (outerAt < 0 && outer.HasStatus(stealth)) outerAt = step;
                if (goneAt < 0 && step > 0 && !world.Entities.Any(value => value.Template.Name == "FixtureScrambler")) goneAt = step;
            }

            Check.That(closeAt > 0 && outerAt > closeAt && goneAt >= outerAt && closeAt % factor == 0 && outerAt % factor == 0 && goneAt % factor == 0,
                $"{rate}: the scans fall on whole logic frames after the marker appeared (stealth at 25 units after {closeAt} ticks, at 45 units after {outerAt}, marker gone after {goneAt}).");
            var frames = (closeAt / factor, outerAt / factor, goneAt / factor);
            reference ??= frames;
            Check.That(frames == reference, $"{rate}: the same frames as at 30 Hz: {frames} against {reference}.");
        }
    }
}
