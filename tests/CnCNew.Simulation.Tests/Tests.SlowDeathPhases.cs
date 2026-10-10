using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// The phases of a slow death (fidelity finding F-05, <c>docs/SIMULATION-CORE.md</c> §44.3): <c>SlowDeathBehavior::doPhaseStuff</c> plays the effect list, creates the
/// creation list and fires the weapon of the phase at the beginning, at the midpoint and at the end of the slow death.
/// </summary>
internal static partial class Tests
{
    private static IReadOnlyDictionary<string, string> SlowDeathPhaseFiles() => new Dictionary<string, string>(RatedWorldFiles())
    {
        ["Data/INI/FXList.ini"] = "FXList FX_Test\nEnd\nFXList FX_Other\nEnd\nFXList FX_Begin\nEnd\nFXList FX_Middle\nEnd\nFXList FX_EndA\nEnd\nFXList FX_EndB\nEnd\n",
        ["Data/INI/Object/SlowDeathPhaseFixture.ini"] = """
        Weapon PhaseBlast
          PrimaryDamage = 30
          PrimaryDamageRadius = 40
          AttackRange = 999.0
          DamageType = EXPLOSION
          ClipSize = 0
          DelayBetweenShots = 0
        End
        ObjectCreationList OCL_PhaseHull
          CreateObject
            ObjectNames = PhaseHull
          End
        End
        ObjectCreationList OCL_PhaseDebris
          CreateDebris
            ModelNames = Whatever
          End
        End
        Object PhaseHull
          KindOf = IMMOBILE
          Body = ActiveBody Body01
            MaxHealth = 10.0
            InitialHealth = 10.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object PhaseBystander
          KindOf = VEHICLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object PhaseTank
          KindOf = VEHICLE SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = SlowDeathBehavior Death01
            DestructionDelay = 2000
            FX = INITIAL FX_Begin
            FX = MIDPOINT FX_Middle
            FX = FINAL FX_EndA FX_EndB
            OCL = FINAL OCL_PhaseHull
            OCL = MIDPOINT OCL_PhaseDebris
            Weapon = MIDPOINT PhaseBlast
          End
        End
        Locomotor PhaseLoco
          Surfaces = GROUND
          Speed = 30
          Acceleration = 900
          Braking = 900
          TurnRate = 3600
        End
        Object PhaseDriver
          KindOf = VEHICLE SELECTABLE
          Locomotor = SET_NORMAL PhaseLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = SlowDeathBehavior Death01
            DestructionDelay = 2000
            OCL = FINAL OCL_PhaseHull
          End
        End
        Object PlainTank
          KindOf = VEHICLE SELECTABLE
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = SlowDeathBehavior Death01
            DestructionDelay = 2000
          End
        End
        """,
    };

    /// <summary>One death: the frame (after the kill) of every effect request, of the hull, of the skipped debris, of the bystander's damage and of the removal.</summary>
    private static (List<string> Log, SimulationWorld World) SlowDeathRun(SimulationRate rate, string victim, uint seed, int snapshotAfterTicks = -1)
    {
        var (world, feeder, fixture) = RatedWorld(rate, SlowDeathPhaseFiles(), seed: seed);
        feeder.Spawn(1, victim, x: 100, y: 100);
        feeder.Spawn(2, "PhaseBystander", x: 110, y: 100);
        for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
        var tank = world.Entities.Single(value => value.Template.Name == victim);
        feeder.Kill(tank.Id.Value);
        var log = new List<string>();
        var start = world.Tick;
        SimulationWorld? restored = null;
        for (var tick = 0; tick < world.Frames(90); tick++)
        {
            var result = world.Step();
            world.VerifyIncrementalHash();
            if (restored is not null)
            {
                restored.Step();
                Check.That(restored.StateHash == world.StateHash, $"{rate}: live and restored world agree {tick} ticks after the death.");
            }
            if (tick == snapshotAfterTicks)
            {
                var bytes = WorldSnapshot.Write(world);
                restored = WorldSnapshot.Restore(bytes, fixture.Templates, fixture.Fingerprint, null);
                Check.That(restored.StateHash == world.StateHash && WorldSnapshot.Write(restored).AsSpan().SequenceEqual(bytes), $"{rate}: a world restored during the slow death is the live one.");
            }
            var frame = (world.Tick - start - 1) / rate.TicksPerOriginalFrame;
            foreach (var item in result.Events)
            {
                if (item.Kind == SimEventKind.FxRequested) log.Add($"{frame} fx {item.Text}");
                else if (item.Kind == SimEventKind.EntitySpawned && world.FindEntity(item.Entity)?.Template.Name == "PhaseHull") log.Add($"{frame} hull");
                else if (item.Kind == SimEventKind.CreationSkipped) log.Add($"{frame} skipped {item.Text} {item.Value}");
                else if (item.Kind == SimEventKind.EntityDamaged && world.FindEntity(item.Entity)?.Template.Name == "PhaseBystander") log.Add($"{frame} blast");
                else if (item.Kind == SimEventKind.EntityDestroyed && item.Entity == tank.Id) log.Add($"{frame} gone");
            }
        }
        Check.That(snapshotAfterTicks < 0 || restored is not null, $"{rate}: the snapshot was taken.");
        return (log, world);
    }

    /// <summary>INITIAL at the death, MIDPOINT between 35 % and 65 % of the destruction delay, FINAL with the removal; hull created, debris counted, weapon fired.</summary>
    public static void SlowDeathPlaysItsPhases()
    {
        var (log, world) = SlowDeathRun(TestRate.Default, "PhaseTank", 7);
        var text = string.Join("; ", log);
        int Frame(string what) => log.Where(line => line.EndsWith(what, System.StringComparison.Ordinal) || line.Contains(" " + what + " ", System.StringComparison.Ordinal)).Select(line => int.Parse(line.Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture)).DefaultIfEmpty(-1).First();
        // DestructionDelay = 2000 ms is 60 logic frames; the kill executes in the first step (frame 0).
        Check.That(Frame("fx FX_Begin") == 0, $"The INITIAL effect list is requested in the frame of the death ({text}).");
        var middle = Frame("fx FX_Middle");
        Check.That(middle >= 21 && middle <= 39, $"The MIDPOINT effect list is requested between 35 % and 65 % of the delay (frame {middle}; {text}).");
        Check.That(Frame("blast") >= middle && Frame("blast") <= middle + 1, $"The MIDPOINT weapon is fired at the object's position and hurts the bystander ({text}).");
        Check.That(log.Any(line => line == $"{middle} skipped CreateDebris {CreationSkipReasons.NuggetNotSimulated}"), $"A creation the simulation cannot perform is counted with its reason ({text}).");
        var final = log.Where(line => line.Contains("fx FX_End", System.StringComparison.Ordinal)).ToArray();
        Check.That(final.Length == 1 && final[0].StartsWith("60 ", System.StringComparison.Ordinal), $"One of the two FINAL effect lists is drawn and requested at the destruction frame ({text}).");
        Check.That(Frame("hull") is 60 or 61 && Frame("gone") is 60 or 61, $"The FINAL creation list leaves the hull when the object goes ({text}).");
        Check.That(world.Entities.Count(value => value.Template.Name == "PhaseHull") == 1, "Exactly one hull lies there.");

        // Both entries of a list of two are drawn over a few seeds (spread out: neighbouring seeds of the original generator draw alike for the first few values).
        var picked = Enumerable.Range(1, 12).Select(seed => SlowDeathRun(TestRate.Default, "PhaseTank", unchecked((uint)seed * 2654435761u)).Log.Single(line => line.Contains("fx FX_End", System.StringComparison.Ordinal)).Split(' ')[2]).Distinct().Count();
        Check.That(picked == 2, $"Over twelve seeds both FINAL effect lists are drawn ({string.Join(",", Enumerable.Range(1, 12).Select(seed => string.Join("/", SlowDeathRun(TestRate.Default, "PhaseTank", unchecked((uint)seed * 2654435761u)).Log.Where(line => line.Contains("fx FX_", System.StringComparison.Ordinal)))))}).");

        // A slow death without any list plays nothing and ends at its destruction frame.
        var (plain, _) = SlowDeathRun(TestRate.Default, "PlainTank", 7);
        Check.That(plain.SequenceEqual(new[] { "60 gone" }) || plain.SequenceEqual(new[] { "61 gone" }), $"Without lists there is only the removal ({string.Join("; ", plain)}).");

        // The bounds of the midpoint draw are the single-precision products of the reference, cut off.
        Check.That(SlowDeathPhases.MidpointBounds(20) == (7, 13) && SlowDeathPhases.MidpointBounds(30) == (10, 19) && SlowDeathPhases.MidpointBounds(60) == (21, 39) && SlowDeathPhases.MidpointBounds(1) == (0, 0) && SlowDeathPhases.MidpointBounds(0) == (0, 0),
            $"0.35f and 0.65f of 20, 30, 60, 1 and 0 frames: {SlowDeathPhases.MidpointBounds(20)}, {SlowDeathPhases.MidpointBounds(30)}, {SlowDeathPhases.MidpointBounds(60)}, {SlowDeathPhases.MidpointBounds(1)}.");
    }

    /// <summary>The same death at 30, 60 and 120 Hz: every phase in the same logic frame, and a snapshot during the slow death continues identically.</summary>
    public static void SlowDeathPhasesAreTheSameAtEveryRate()
    {
        var reference = SlowDeathRun(SimulationRate.Hz30, "PhaseTank", 11, snapshotAfterTicks: 10).Log;
        foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var run = SlowDeathRun(rate, "PhaseTank", 11, snapshotAfterTicks: 10 * rate.TicksPerOriginalFrame + 1).Log;
            Check.That(run.SequenceEqual(reference), $"{rate}: the phases fall into the frames of 30 Hz ({string.Join("; ", run)} against {string.Join("; ", reference)}).");
        }
    }
}

internal static partial class Tests
{
    /// <summary>
    /// <c>SlowDeathBehavior::onDie</c>, <c>ai->markAsDead()</c>: a vehicle that is killed while it drives stops where the logic frame it has begun ends, takes no order any
    /// more, and its hull lies there two seconds later - at the same place at 30, 60 and 120 Hz.
    /// </summary>
    public static void SlowDeathStopsTheDyingMover()
    {
        static (long X, long Y) Run(SimulationRate rate)
        {
            var (world, feeder, _) = RatedWorld(rate, SlowDeathPhaseFiles(), seed: 5);
            feeder.Spawn(1, "PhaseDriver", x: 100, y: 100);
            for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
            var driver = world.Entities.Single(value => value.Template.Name == "PhaseDriver");
            Check.That(feeder.SetMoveTarget(1, driver.Id.Value, 400, 100).Accepted, $"{rate}: the move is ordered.");
            // Killed in its 41st logic frame: at 30 Hz by a command of the tick whose step is the 41st (the vehicle takes that step, then stands), above 30 Hz one tick
            // into the frame (the vehicle is put on the frame's end). Either way it stands where its 41st frame ends.
            for (var tick = 0; tick < rate.Time.OriginalFrames(40) + (rate.TicksPerOriginalFrame > 1 ? 1 : 0); tick++) world.Step();
            Check.That(driver.HasMoveTarget && driver.X > Fixed.FromInt(110) && driver.X < Fixed.FromInt(390), $"{rate}: the vehicle is on its way ({driver.X}).");
            feeder.Kill(driver.Id.Value);
            world.Step();
            Check.That(driver.EffectivelyDead && !driver.HasMoveTarget, $"{rate}: the dying vehicle has no move any more.");
            var (diedX, diedY) = (driver.X, driver.Y);
            feeder.SetMoveTarget(1, driver.Id.Value, 100, 300);
            for (var tick = 0; tick < rate.Time.OriginalFrames(30); tick++)
            {
                world.Step();
                world.VerifyIncrementalHash();
            }
            Check.That(driver.X == diedX && driver.Y == diedY && !driver.HasMoveTarget, $"{rate}: it stays where it died and takes no order ({driver.X}, {driver.Y}).");
            for (var tick = 0; tick < rate.Time.OriginalFrames(40) && world.Entities.All(value => value.Template.Name != "PhaseHull"); tick++) world.Step();
            var hull = world.Entities.Single(value => value.Template.Name == "PhaseHull");
            Check.That(hull.X == diedX && hull.Y == diedY, $"{rate}: the hull lies where the vehicle died ({hull.X}, {hull.Y} against {diedX}, {diedY}).");
            return (hull.X.Raw, hull.Y.Raw);
        }

        var reference = Run(SimulationRate.Hz30);
        foreach (var rate in new[] { SimulationRate.Hz60, SimulationRate.Hz120 })
            Check.That(Run(rate) == reference, $"{rate}: the hull lies where it lies at 30 Hz.");
    }
}
