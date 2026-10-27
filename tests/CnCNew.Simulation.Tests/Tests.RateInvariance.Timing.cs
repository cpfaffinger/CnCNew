using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Phase 23, package TIMING (<c>docs/rate-invariance/RATE-INVARIANCE-TIMING.md</c>): "one tick where the original means one frame". Every test stages one cause on a synthetic
/// fixture at 30, 60 and 120 Hz and compares game time, counted in 1/120 s (a whole number at each rate). The expectation is the 30 Hz value, written in logic
/// frames of the original; a measurement starts on a frame boundary, as the rate-equivalence suite's do.
/// </summary>
static partial class Tests
{
    /// <summary>1/120 s per logic frame of the original.</summary>
    private const long TimingUnitsPerFrame = 4;

    private static void TimingAlign(SimulationWorld world)
    {
        while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
    }

    private static long TimingUnits(SimulationWorld world, long ticks) => ticks < 0 ? -1 : ticks * TimingUnitsPerFrame / world.Time.TicksPerOriginalFrame;

    private static void TimingSame(string what, long expectedFrames, Func<SimulationRate, long> measureUnits)
    {
        foreach (var (rate, _) in WorldRates)
        {
            var units = measureUnits(rate);
            Check.That(units == expectedFrames * TimingUnitsPerFrame,
                $"{rate}: {what} takes {expectedFrames} frames of the original ({expectedFrames * TimingUnitsPerFrame} units of 1/120 s), measured {units}.");
        }
    }

    private static Entity TimingOnly(SimulationWorld world, string template) => world.Entities.Single(value => value.Template.Name == template);

    private static IReadOnlyDictionary<string, string> TimingFiles() => new Dictionary<string, string>(ConstructionFiles())
    {
        ["Data/INI/GameData.ini"] = "GameData\n  SellPercentage = 50%\n  BaseRegenHealthPercentPerSecond = 10%\n  BaseRegenDelay = 0\nEnd\n",
        ["Data/INI/Object/TimingFixture.ini"] = """
        Weapon TimingSelfPulse
          PrimaryDamage = 5
          PrimaryDamageRadius = 50.0
          AttackRange = 0.0
          DamageType = EXPLOSION
          RadiusDamageAffects = SELF ALLIES ENEMIES NEUTRALS
          ClipSize = 0
          DelayBetweenShots = 300
        End
        Weapon TimingNoReloadPulse
          PrimaryDamage = 1
          PrimaryDamageRadius = 50.0
          AttackRange = 0.0
          DamageType = EXPLOSION
          RadiusDamageAffects = SELF ALLIES ENEMIES NEUTRALS
          ClipSize = 2
          ClipReloadTime = 0
          DelayBetweenShots = 100
        End
        Weapon TimingNoReloadRifle
          PrimaryDamage = 1
          AttackRange = 200.0
          DamageType = EXPLOSION
          ClipSize = 2
          ClipReloadTime = 0
          DelayBetweenShots = 100
        End

        Object TimingField
          KindOf = IMMOBILE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          Behavior = FireWeaponUpdate Pulse01
            Weapon = TimingSelfPulse
          End
        End
        Object TimingReloadField
          KindOf = IMMOBILE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          Behavior = FireWeaponUpdate Pulse01
            Weapon = TimingNoReloadPulse
          End
        End
        Object TimingGunner
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          WeaponSet
            Conditions = None
            Weapon = PRIMARY TimingNoReloadRifle
          End
        End
        Object TimingDummy
          KindOf = SELECTABLE VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100000.0
            InitialHealth = 100000.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object TimingSneak
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = StealthUpdate Stealth01
            StealthDelay = 1000
            InnateStealth = Yes
          End
        End
        Object TimingQuickSneak
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = StealthUpdate Stealth01
            StealthDelay = 0
            InnateStealth = Yes
          End
        End
        Object TimingDetector
          KindOf = SELECTABLE VEHICLE
          VisionRange = 300.0
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = StealthDetectorUpdate Detect01
            DetectionRate = 500
            DetectionRange = 300.0
          End
        End
        Object TimingRegenerator
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = BaseRegenerateUpdate Regen01
          End
        End
        Object TimingLeaflet
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = LeafletDropBehavior Leaflet01
            DisabledDuration = 1500
            AffectRadius = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object TimingFirestorm
          KindOf = IMMOBILE NO_COLLIDE
          Geometry = CYLINDER
          GeometryMajorRadius = 1.0
          GeometryHeight = 10.0
          GeometryIsSmall = Yes
          Body = InactiveBody Body01
          End
          Behavior = FirestormDynamicGeometryInfoUpdate Storm01
            InitialDelay = 100
            InitialHeight = 10.0
            InitialMajorRadius = 1.0
            FinalHeight = 10.0
            FinalMajorRadius = 60.0
            TransitionTime = 1000
            ReverseAtTransitionTime = Yes
            DelayBetweenDamageFrames = 500
            DamageAmount = 0.0
          End
        End
        """,
    };

    /// <summary>The ticks from the creation of <paramref name="template"/> (spawned on a frame boundary) to the first step after which <paramref name="reached"/> holds, in 1/120 s.</summary>
    private static long TimingFromCreation(SimulationRate rate, string template, Func<SimulationWorld, Entity, TickResult, bool> reached, long budgetFrames = 120)
    {
        var (world, feeder, _) = RatedWorld(rate, TimingFiles());
        TimingAlign(world);
        var created = world.Tick;
        Check.That(feeder.Spawn(1, template, x: 100, y: 100).Accepted, $"{rate}: {template} is spawned.");
        Entity? entity = null;
        for (var step = 0; step < world.Frames(budgetFrames); step++)
        {
            var result = world.Step();
            entity ??= world.Entities.FirstOrDefault(value => value.Template.Name == template);
            if (entity is not null && reached(world, entity, result)) return TimingUnits(world, result.Tick - created);
        }
        return -1;
    }

    /// <summary>
    /// §5.3 <c>Register</c>/first update: the modules whose first update is an event (a field's first shot, the start of the stealth delay, a leaflet bomb's
    /// effect) get it one logic frame of the original after their object appeared. A leaflet bomb with the default <c>Delay</c> of one frame used to sleep for good
    /// above 30 Hz because its first update came before the delay was over.
    /// </summary>
    public static void RateTimingFirstUpdateComesOneFrameAfterCreation()
    {
        var stealthed = ObjectStatusBits.Index("STEALTHED");
        TimingSame("creation to the first pulse of a field (FireWeaponUpdate)", 1,
            rate => TimingFromCreation(rate, "TimingField", (_, field, result) => result.Events.Any(value => value.Kind == SimEventKind.WeaponFired && value.Entity == field.Id)));
        TimingSame("creation to stealth with a StealthDelay of one second", 31, rate => TimingFromCreation(rate, "TimingSneak", (_, sneak, _) => sneak.HasStatus(stealthed)));
        TimingSame("creation to stealth without a StealthDelay", 2, rate => TimingFromCreation(rate, "TimingQuickSneak", (_, sneak, _) => sneak.HasStatus(stealthed)));

        TimingSame("creation of a leaflet bomb to its effect (default Delay of one frame)", 1, rate =>
        {
            var (world, feeder, _) = RatedWorld(rate, TimingFiles());
            feeder.SetRelationship(1, 2, Relationship.Enemies);
            feeder.SetRelationship(2, 1, Relationship.Enemies);
            feeder.Spawn(2, "Grunt", x: 120, y: 100);
            world.Step();
            TimingAlign(world);
            var grunt = TimingOnly(world, "Grunt");
            var created = world.Tick;
            feeder.Spawn(1, "TimingLeaflet", x: 100, y: 100);
            for (var step = 0; step < world.Frames(30); step++)
            {
                var result = world.Step();
                if (grunt.EmpDisabledUntil > 0) return TimingUnits(world, result.Tick - created);
            }
            return -1;
        });
    }

    /// <summary>§5.3 <c>StealthDetectorUpdate</c>/<c>StealthUpdate.MarkDetected</c>: the mark holds the detection rate plus one logic frame of the original (<c>markAsDetected(m_updateRate + 1)</c>).</summary>
    public static void RateTimingDetectionOutlastsTheScanByOneFrame()
    {
        var detectedBit = ObjectStatusBits.Index("DETECTED");
        // DetectionRate 500 ms is 15 frames: the detector is removed one frame after a scan, the mark of that scan runs out 16 frames after it.
        TimingSame("the last scan of a detector to the end of its mark", 16, rate =>
        {
            var (world, feeder, _) = RatedWorld(rate, TimingFiles());
            feeder.Spawn(1, "TimingQuickSneak", x: 100, y: 100);
            for (var step = 0; step < world.Frames(10); step++) world.Step();
            TimingAlign(world);
            var sneak = TimingOnly(world, "TimingQuickSneak");
            feeder.Spawn(2, "TimingDetector", x: 120, y: 100);
            long scan = -1;
            for (var step = 0; step < world.Frames(60) && scan < 0; step++)
            {
                var result = world.Step();
                if (sneak.HasStatus(detectedBit)) scan = result.Tick;
            }
            Check.That(scan >= 0, $"{rate}: the detector finds the stealthed unit.");
            while (world.Tick < scan + world.Frames(1)) world.Step();
            feeder.Destroy(TimingOnly(world, "TimingDetector").Id.Value);
            for (var step = 0; step < world.Frames(60); step++)
            {
                var result = world.Step();
                if (!sneak.HasStatus(detectedBit)) return TimingUnits(world, result.Tick - scan);
            }
            return -1;
        });
    }

    /// <summary>
    /// New findings of the suite, the shape of the sale (§5.3 <c>_saleDurationTicks</c>): the step that starts a production or a construction stands for a whole logic
    /// frame of the original. The first unit of a queue is done BuildTime less one frame after the order, the second a full BuildTime later; a structure stands
    /// BuildTime less one frame after the first step of work.
    /// </summary>
    public static void RateTimingProductionAndConstructionCountTheFirstFrame()
    {
        foreach (var (rate, _) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, TimingFiles());
            feeder.GiveMoney(1, 1000);
            feeder.Spawn(1, "Barracks", x: 200, y: 200);
            feeder.Spawn(1, "Dozer", x: 100, y: 100);
            world.Step();
            TimingAlign(world);
            var barracks = TimingOnly(world, "Barracks");
            var dozer = TimingOnly(world, "Dozer");

            // Grunt: BuildTime 1 s = 30 frames.
            var ordered = world.Tick;
            Check.That(feeder.QueueProduction(1, barracks.Id.Value, "Grunt").Accepted && feeder.QueueProduction(1, barracks.Id.Value, "Grunt").Accepted, $"{rate}: two units are queued.");
            var done = new List<long>();
            for (var step = 0; step < world.Frames(80); step++)
            {
                var result = world.Step();
                if (result.Events.Any(value => value.Kind == SimEventKind.ProductionCompleted && value.Entity == barracks.Id)) done.Add(TimingUnits(world, result.Tick - ordered));
            }
            Check.That(done.Count == 2 && done[0] == 29 * TimingUnitsPerFrame && done[1] == 59 * TimingUnitsPerFrame,
                $"{rate}: the units are done 29 and 59 frames after the order (116 and 236 units of 1/120 s), measured {string.Join(", ", done)}.");

            // Plant: BuildTime 2 s = 60 frames, counted from the first step of work (the dozer's way is not part of it).
            TimingAlign(world);
            Check.That(feeder.Construct(1, dozer.Id.Value, "Plant", 130, 100).Accepted, $"{rate}: the dozer may build a plant.");
            Entity? site = null;
            long began = -1, complete = -1;
            for (var step = 0; step < world.Frames(400) && complete < 0; step++)
            {
                var result = world.Step();
                site ??= world.Entities.FirstOrDefault(value => value.Template.Name == "Plant");
                if (began < 0 && site is { ConstructionWork: > 0 }) began = result.Tick;
                if (site is not null && result.Events.Any(value => value.Kind == SimEventKind.ConstructionCompleted && value.Entity == site.Id)) complete = result.Tick;
            }
            Check.That(began >= 0 && complete >= 0 && TimingUnits(world, complete - began) == 59 * TimingUnitsPerFrame,
                $"{rate}: the plant stands 59 frames after the first step of work (236 units of 1/120 s), measured {TimingUnits(world, complete - began)}.");
        }
    }

    /// <summary>
    /// §5.3 <c>HackInternetAIUpdate</c>, first counter after an order: a phase started from outside the update lasts its frames (unpack 9), a counter the update
    /// reloads its frames plus one (cash delay 30 + 1). First cash 40 frames after the order, then every 31.
    /// </summary>
    public static void RateTimingHackerPhasesStartedByAnOrder()
    {
        foreach (var (rate, _) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, LastBlockerFiles());
            feeder.Spawn(2, "LbHacker", x: 200, y: 200);
            world.Step();
            TimingAlign(world);
            var hacker = TimingOnly(world, "LbHacker");
            var owner = new PlayerId(2);
            var start = world.Tick;
            hacker.Modules.OfType<HackInternetAIUpdate>().Single().StartHacking(world);
            var money = world.MoneyOf(owner);
            var paid = new List<long>();
            for (var step = 0; step < world.Frames(110); step++)
            {
                var result = world.Step();
                if (world.MoneyOf(owner) == money) continue;
                money = world.MoneyOf(owner);
                paid.Add(TimingUnits(world, result.Tick - start));
            }
            Check.That(paid.Count >= 3 && paid[0] == 40 * TimingUnitsPerFrame && paid[1] - paid[0] == 31 * TimingUnitsPerFrame && paid[2] - paid[1] == 31 * TimingUnitsPerFrame,
                $"{rate}: the hacker is paid 40 frames after the order and then every 31 (160, 124, 124 units of 1/120 s), measured {string.Join(", ", paid)}.");
        }
    }

    /// <summary>New finding (recharge from creation of the superweapons): <c>SpecialPowerCreate</c> restarts the recharge when the object is complete, one logic frame after it appeared.</summary>
    public static void RateTimingBuildCompleteComesOneFrameAfterCreation()
    {
        var files = new Dictionary<string, string>(SpecialPowerFiles())
        {
            ["Data/INI/Object/TimingPower.ini"] = """
            Object TimingCarrier
              KindOf = STRUCTURE
              Body = StructureBody Body01
                MaxHealth = 500.0
                InitialHealth = 500.0
              End
              Behavior = SpecialPowerCreate Create01
              End
              Behavior = SpecialAbility Edge01
                SpecialPowerTemplate = SPECIAL_Edge
              End
            End
            """,
        };
        // SPECIAL_Edge: ReloadTime 3000 ms = 90 frames, restarted one frame after the creation.
        TimingSame("creation to ready of a power restarted by SpecialPowerCreate", 91, rate =>
        {
            var (world, feeder, _) = RatedWorld(rate, files);
            TimingAlign(world);
            feeder.Spawn(1, "TimingCarrier", x: 100, y: 100);
            for (var step = 0; step < world.Frames(4); step++) world.Step();
            var carrier = TimingOnly(world, "TimingCarrier");
            return TimingUnits(world, carrier.Modules.OfType<SpecialPowerModuleBase>().Single().AvailableOnFrame(world) - carrier.SpawnTick);
        });
    }

    /// <summary>
    /// New finding (battleship guns): a <c>ClipReloadTime</c> of 0 reloads at once, and the next shot comes with the next logic frame of the original – through the
    /// weapon slots of an object and through <c>FireWeaponUpdate</c>. Clip of two, 100 ms (3 frames) between shots: the gaps are 3, 1, 3, 1 frames.
    /// </summary>
    public static void RateTimingAnInstantReloadWaitsOneFrame()
    {
        long[] expected = [3 * TimingUnitsPerFrame, TimingUnitsPerFrame, 3 * TimingUnitsPerFrame, TimingUnitsPerFrame, 3 * TimingUnitsPerFrame];
        foreach (var (rate, factor) in WorldRates)
        {
            Check.That(rate.Time.ZeroIsNextFrame(0) == (factor == 1 ? 0 : factor) && rate.Time.ZeroIsNextFrame(7 * factor) == 7 * factor,
                $"{rate}: a wait of zero behind a deadline that is tested once per tick is the next frame (0 at 30 Hz, where the next tick is that frame); another wait is unchanged.");

            var (world, feeder, _) = RatedWorld(rate, TimingFiles());
            feeder.Spawn(1, "TimingGunner", x: 100, y: 100);
            feeder.Spawn(2, "TimingDummy", x: 150, y: 100);
            feeder.Spawn(1, "TimingReloadField", x: 600, y: 600);
            world.Step();
            TimingAlign(world);
            var gunner = TimingOnly(world, "TimingGunner");
            var dummy = TimingOnly(world, "TimingDummy");
            var field = TimingOnly(world, "TimingReloadField");
            var rifle = new List<long>();
            var pulses = new List<long>();
            for (var step = 0; step < world.Frames(40); step++)
            {
                feeder.FireWeapon(1, gunner.Id.Value, dummy.Id.Value);
                var result = world.Step();
                foreach (var item in result.Events)
                {
                    if (item.Kind != SimEventKind.WeaponFired) continue;
                    if (item.Entity == gunner.Id) rifle.Add(result.Tick);
                    if (item.Entity == field.Id) pulses.Add(result.Tick);
                }
            }
            long[] Gaps(List<long> shots) => [..shots.Zip(shots.Skip(1), (first, second) => TimingUnits(world, second - first)).Take(expected.Length)];
            Check.That(Gaps(rifle).SequenceEqual(expected), $"{rate}: the rifle's shots are 3, 1, 3, 1, 3 frames apart (units of 1/120 s: {string.Join(", ", Gaps(rifle))}).");
            Check.That(Gaps(pulses).SequenceEqual(expected), $"{rate}: the field's pulses are 3, 1, 3, 1, 3 frames apart (units of 1/120 s: {string.Join(", ", Gaps(pulses))}).");
        }
    }

    /// <summary>§5.3 <c>BaseRegenerateUpdate.OnDamage</c>: with a <c>BaseRegenDelay</c> of 0 the structure heals one logic frame of the original after the hit, not one tick after it.</summary>
    public static void RateTimingAZeroRegenerationDelayIsOneFrame()
    {
        TimingSame("a hit to the first regeneration with BaseRegenDelay 0", 1, rate =>
        {
            var (world, feeder, _) = RatedWorld(rate, TimingFiles());
            feeder.Spawn(1, "TimingRegenerator", x: 100, y: 100);
            for (var step = 0; step < world.Frames(4); step++) world.Step();
            TimingAlign(world);
            var body = TimingOnly(world, "TimingRegenerator").Body!;
            var hit = world.Tick;
            feeder.Damage(TimingOnly(world, "TimingRegenerator").Id.Value, 300);
            world.Step();
            var health = body.Health;
            for (var step = 0; step < world.Frames(30); step++)
            {
                var result = world.Step();
                if (body.Health > health) return TimingUnits(world, result.Tick - hit);
            }
            return -1;
        });
    }

    /// <summary>
    /// §5.3 <c>BattlePlanUpdate</c>, one state transition per update: unpacking takes its animation time (30 frames); a change of plan packs (30), passes through
    /// idle with a <c>TransitionIdleTime</c> of 0 - left by the next update, one logic frame of the original later - and unpacks (30): 61 frames.
    /// </summary>
    public static void RateTimingBattlePlanPassesThroughIdleInOneFrame()
    {
        foreach (var (rate, _) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, BattlePlanFiles());
            feeder.Spawn(1, "PlanCenter", x: 100, y: 100);
            for (var step = 0; step < world.Frames(4); step++) world.Step();
            var plan = TimingOnly(world, "PlanCenter").Modules.OfType<BattlePlanUpdate>().Single();
            long Until(BattlePlanKind kind)
            {
                TimingAlign(world);
                var start = world.Tick;
                plan.SelectPlan(kind);
                for (var step = 0; step < world.Frames(200); step++)
                {
                    var result = world.Step();
                    if (plan.ActivePlan == kind) return TimingUnits(world, result.Tick - start);
                }
                return -1;
            }
            // Hold the line first: leaving bombardment waits for a turret this center has not (BattlePlanUpdate::isTurretInNaturalPosition is false without one, F-57).
            var first = Until(BattlePlanKind.HoldTheLine);
            var second = Until(BattlePlanKind.Bombardment);
            Check.That(first == 30 * TimingUnitsPerFrame && second == 61 * TimingUnitsPerFrame,
                $"{rate}: the first plan is active after 30 frames, a changed plan after 61 (120 and 244 units of 1/120 s), measured {first} and {second}.");
        }
    }

    /// <summary>
    /// §5.3 <c>FirestormDynamicGeometryInfoUpdate</c>: delay 3 frames, the way out 30 frames (it starts in the update that ends the delay), the way back 30 + 1
    /// (it starts with the next update): finished 64 frames after the creation, and the largest radius is reached 33 frames after it.
    /// </summary>
    public static void RateTimingFirestormTransitionTakesTheSameGameTime()
    {
        TimingSame("creation of a firestorm to its largest radius", 33, rate => TimingFromCreation(rate, "TimingFirestorm",
            (_, storm, _) => storm.Modules.OfType<FirestormDynamicGeometryInfoUpdate>().Single().CurrentMajorRadius == Fixed.FromInt(60)));
        TimingSame("creation of a firestorm to the end of its way back", 64, rate => TimingFromCreation(rate, "TimingFirestorm",
            (_, storm, _) => storm.Modules.OfType<FirestormDynamicGeometryInfoUpdate>().Single().Finished));
    }
}
