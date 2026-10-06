using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Phase 20, stage 2, modules N–Z: a sample of the converted tick literals. Each scenario runs at 30, 60 and 120 Hz with the same seed and measures the
/// ticks between two observations; at 60 and 120 Hz the span must be exactly 2× and 4× the 30 Hz span, i.e. the event fires on the same frame boundary
/// of the original. (The 30 Hz values themselves are what the unchanged older tests pin.)
/// </summary>
static partial class Tests
{
    private static readonly SimulationRate[] NzRates = [SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120];

    private static IReadOnlyDictionary<string, string> NzRateFiles() => new Dictionary<string, string>(Phase5Files())
    {
        ["Data/INI/Object/TickRateNzFixture.ini"] = """
        Object TrPoisoned
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = PoisonedBehavior Poison01
            PoisonDuration = 1000
          End
        End

        Object TrPatient
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 300.0
            InitialHealth = 300.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object TrMedic
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AutoHealBehavior Heal01
            StartsActive = Yes
            HealingAmount = 1
            Radius = 100.0
            AffectsWholePlayer = Yes
          End
        End

        Object TrWatcher
          KindOf = STRUCTURE IMMOBILE
          VisionRange = 80.0
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = EnemyNearUpdate Near01
            ScanDelayTime = 100
          End
        End

        Object TrZombie
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = SlowDeathBehavior ModuleTag_Death01
            DestructionDelay = 1000
            DestructionDelayVariance = 500
          End
        End

        Object TrThinker
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """,
    };

    private static SimulationWorld NzWorld(SimulationRate rate, out Feeder feeder)
    {
        var fixture = new SyntheticFixture(rate, files: NzRateFiles());
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, TwoPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
        feeder = new Feeder(world);
        return world;
    }

    private static Entity? NzFind(SimulationWorld world, string template) => world.Entities.FirstOrDefault(value => value.Template.Name == template);

    /// <summary>Steps until <paramref name="reached"/> holds and returns the tick observed then; fails after <paramref name="limitFrames"/> frames of the original.</summary>
    private static long NzStepUntil(SimulationWorld world, System.Func<bool> reached, int limitFrames, string what)
    {
        var limit = world.Time.OriginalFrames(limitFrames);
        for (var step = 0L; step <= limit; step++)
        {
            if (reached()) return world.Tick;
            world.Step();
        }
        throw new System.InvalidOperationException($"{world.Time.Rate}: {what} did not happen within {limitFrames} frames of the original.");
    }

    private static void NzFrames(SimulationWorld world, int frames)
    {
        var ticks = world.Time.OriginalFrames(frames);
        for (var tick = 0L; tick < ticks; tick++) world.Step();
    }

    /// <summary>Runs <paramref name="span"/> at the three rates and checks 2× and 4×; returns the 30 Hz span.</summary>
    private static long NzSpansScale(string what, System.Func<SimulationRate, long> span)
    {
        var thirty = span(SimulationRate.Hz30);
        Check.That(thirty > 0, $"{what}: the 30 Hz span is positive ({thirty}).");
        foreach (var rate in NzRates.Skip(1))
        {
            var ticks = span(rate);
            Check.That(ticks == thirty * rate.TicksPerOriginalFrame, $"{what}: {rate} takes {ticks} ticks, expected {thirty} frames of the original = {thirty * rate.TicksPerOriginalFrame} ticks.");
        }
        return thirty;
    }

    /// <summary><c>StealthDetectorUpdate</c>: the stagger of the first scan is drawn in frames of the original (1..DetectionRate) and scaled.</summary>
    public static void TickRateNzDetectorStaggerIsDrawnInOriginalFrames()
    {
        var detected = ObjectStatusBits.Index("DETECTED");
        var stealthed = ObjectStatusBits.Index("STEALTHED");
        var frames = NzSpansScale("first scan of a detector", rate =>
        {
            var world = NzWorld(rate, out var feeder);
            feeder.Spawn(2, "Cloaked", x: 10, y: 0);
            NzFrames(world, 10);
            Check.That(NzFind(world, "Cloaked")!.HasStatus(stealthed), $"{rate}: the target is cloaked before the detector arrives.");
            feeder.Spawn(1, "Spotter", x: 0, y: 0);
            var appeared = NzStepUntil(world, () => NzFind(world, "Spotter") is not null, 5, "the detector");
            return NzStepUntil(world, () => NzFind(world, "Cloaked")!.HasStatus(detected), 40, "the detection") - appeared;
        });
        Check.That(frames is >= 1 and <= 15, $"The stagger lies within DetectionRate (500 ms = 15 frames): {frames}.");
    }

    /// <summary><c>PoisonedBehavior</c> without <c>PoisonDamageInterval</c>: one application per frame of the original, at every rate.</summary>
    public static void TickRateNzPoisonWithoutIntervalHurtsOncePerOriginalFrame()
    {
        var frames = NzSpansScale("four poison applications", rate =>
        {
            var world = NzWorld(rate, out var feeder);
            feeder.Spawn(1, "TrPoisoned");
            NzFrames(world, 3);
            var victim = NzFind(world, "TrPoisoned")!;
            feeder.Damage(victim.Id.Value, 10, damageType: DamageTypes.Poison);
            var first = NzStepUntil(world, () => victim.Body!.Health <= Fixed.FromInt(270), 20, "the second application");
            return NzStepUntil(world, () => victim.Body!.Health <= Fixed.FromInt(230), 20, "the sixth application") - first;
        });
        Check.That(frames == 4, $"40 points of damage at 10 per frame of the original take 4 frames: {frames}.");
    }

    /// <summary><c>AutoHealBehavior</c> without <c>HealingDelay</c>: one pulse per frame of the original, at every rate.</summary>
    public static void TickRateNzAutoHealWithoutDelayPulsesOncePerOriginalFrame()
    {
        var frames = NzSpansScale("twenty heal pulses", rate =>
        {
            var world = NzWorld(rate, out var feeder);
            feeder.Spawn(1, "TrPatient", x: 10, y: 0);
            feeder.Spawn(1, "TrMedic", x: 0, y: 0);
            NzFrames(world, 3);
            var patient = NzFind(world, "TrPatient")!;
            feeder.Damage(patient.Id.Value, 40);
            NzStepUntil(world, () => patient.Body!.Health < Fixed.FromInt(270), 5, "the damage");
            var first = NzStepUntil(world, () => patient.Body!.Health >= Fixed.FromInt(270), 40, "the heal to 270");
            return NzStepUntil(world, () => patient.Body!.Health >= Fixed.FromInt(290), 40, "the heal to 290") - first;
        });
        Check.That(frames == 20, $"20 points of healing at 1 per frame of the original take 20 frames: {frames}.");
    }

    /// <summary><c>EnemyNearUpdate</c>: the start offset is drawn in frames and the countdown "acts in the update after zero" keeps its frame boundary.</summary>
    public static void TickRateNzEnemyNearScanKeepsItsFrameBoundary()
    {
        var frames = NzSpansScale("first scan of a watcher", rate =>
        {
            var world = NzWorld(rate, out var feeder);
            feeder.Spawn(2, "TrPatient", x: 10, y: 0);
            NzFrames(world, 5);
            feeder.Spawn(1, "TrWatcher", x: 0, y: 0);
            var appeared = NzStepUntil(world, () => NzFind(world, "TrWatcher") is not null, 5, "the watcher");
            var watcher = NzFind(world, "TrWatcher")!;
            return NzStepUntil(world, () => watcher.Modules.OfType<EnemyNearUpdate>().Single().IsEnemyNear, 20, "the alarm") - appeared;
        });
        Check.That(frames is >= 1 and <= 4, $"The first scan comes within ScanDelayTime (100 ms = 3 frames) + 1 frames: {frames}.");
    }

    /// <summary><c>SlowDeathBehavior</c>: the variance of the destruction delay is drawn in frames of the original and scaled.</summary>
    public static void TickRateNzSlowDeathVarianceIsDrawnInOriginalFrames()
    {
        var frames = NzSpansScale("slow death", rate =>
        {
            var world = NzWorld(rate, out var feeder);
            feeder.Spawn(1, "TrZombie");
            NzFrames(world, 3);
            var zombie = NzFind(world, "TrZombie")!;
            feeder.Kill(zombie.Id.Value);
            var died = NzStepUntil(world, () => zombie.EffectivelyDead, 5, "the death");
            return NzStepUntil(world, () => NzFind(world, "TrZombie") is null, 80, "the destruction") - died;
        });
        Check.That(frames is >= 30 and <= 46, $"DestructionDelay 1000 ms plus up to 500 ms: 30 to 45 frames (plus the removal): {frames}.");
    }

    /// <summary>Catalog: the default mood check rate (<c>LOGICFRAMES_PER_SECOND * 2</c>) is 60 frames of the original at every rate.</summary>
    public static void TickRateNzCatalogDefaultsAreOriginalFrames()
    {
        foreach (var rate in NzRates)
        {
            var fixture = new SyntheticFixture(rate, files: NzRateFiles());
            Check.That(fixture.Templates.TryGet("TrThinker", out var thinker), "The fixture has the thinker.");
            var data = thinker.Modules.Select(module => module.Data).OfType<AIUpdateData>().Single();
            Check.That(data.MoodCheckTicks == 60L * rate.TicksPerOriginalFrame, $"{rate}: the default mood check rate is 60 frames of the original ({data.MoodCheckTicks} ticks).");
        }
    }
}
