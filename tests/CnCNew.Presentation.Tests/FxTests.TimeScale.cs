using System.Globalization;
using CnCNew.Data;
using CnCNew.Presentation.Fx;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// The time scale of the two lockouts of the effect layer (docs/TICKRATE-MIGRATION.md §14.4): the under-attack notification of <see cref="SoundCueDeriver"/> and the
/// damage-effect throttle of <see cref="FxExecutor"/>. Both compare event ticks of the world with a duration from the data, so the same stream of hits - one in every
/// tick - is fed at 30, 60 and 120 Hz and the time until the next notification or effect is compared in seconds; the tolerance is one original frame (1/30 s).
/// </summary>
public static partial class FxTests
{
    private static readonly SimulationRate[] TimeScaleRates = [SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120];

    private static void SameGameTime(string what, List<(SimulationRate Rate, double Seconds)> values, double expected)
    {
        Console.WriteLine("       " + what + ": " + string.Join(", ", values.Select(value => string.Create(CultureInfo.InvariantCulture, $"{value.Rate}: {value.Seconds:F4} s"))));
        const double oneFrame = 1.0 / OriginalFrame.PerSecond;
        foreach (var (rate, seconds) in values)
        {
            Assert(Math.Abs(seconds - values[0].Seconds) <= oneFrame + 1e-9, $"{what} takes {seconds:F4} s at {rate} and {values[0].Seconds:F4} s at {values[0].Rate}: more than one original frame apart");
            Assert(Math.Abs(seconds - expected) <= oneFrame + 1e-9, $"{what} takes {seconds:F4} s at {rate}, expected {expected} s");
        }
    }

    private static void TimeScaleLockouts()
    {
        var catalog = Catalog();
        var view = new TestView();
        var underAttack = new List<(SimulationRate, double)>();
        var throttle = new List<(SimulationRate, double)>();
        foreach (var rate in TimeScaleRates)
        {
            SimEvent Event(long tick, SimEventKind kind, EntityId entity, EntityId other, string text, long value)
                => new(new EventId(1), tick, kind, entity, other, PlayerId.System, text, value);

            // Radar::tryUnderAttackEvent: the base is hit in every tick; a second notification comes ten seconds after the first.
            var deriver = new SoundCueDeriver(catalog, rate.Time);
            var cues = new List<SoundCue>();
            long first = -1, second = -1;
            for (long tick = 1; tick <= 12 * rate.TicksPerSecond && second < 0; tick++)
            {
                cues.Clear();
                deriver.Derive(Event(tick, SimEventKind.EntityDamaged, TestView.Base, TestView.Tank, string.Empty, Fixed.FromInt(1).Raw), view, cues);
                if (cues.Count == 0) continue;
                Assert(cues.Select(cue => cue.EventName).SequenceEqual(["BaseUnderAttack", "TestRadarStructure"]), $"at {rate} the notification is the base's ({string.Join(",", cues.Select(cue => cue.EventName))})");
                if (first < 0) first = tick;
                else second = tick;
            }

            Assert(first == 1 && second > first, $"at {rate} the first hit is announced and a later one again (first {first}, second {second})");
            underAttack.Add((rate, (double)(second - first) / rate.TicksPerSecond));

            // DamageFX ThrottleTime 1000 ms: the tank is shot in every tick; the next damage effect of the same damage type comes a second after the first.
            var executor = new FxExecutor(catalog, rate.Time, 5, new ParticleBudget());
            long shown = -1, shownAgain = -1;
            for (long tick = 100; tick <= 100 + 3 * rate.TicksPerSecond && shownAgain < 0; tick++)
            {
                var before = executor.Counters.GetValueOrDefault("damagefx-throttled");
                executor.ExecuteEvents(
                [
                    Event(tick, SimEventKind.WeaponFired, TestView.Base, TestView.Tank, "TestGun", Fixed.FromInt(20).Raw),
                    Event(tick, SimEventKind.EntityDamaged, TestView.Tank, TestView.Base, string.Empty, Fixed.FromInt(20).Raw),
                ], view);
                if (executor.Counters.GetValueOrDefault("damagefx-throttled") != before) continue;
                if (shown < 0) shown = tick;
                else shownAgain = tick;
            }

            Assert(shown == 100 && shownAgain > shown, $"at {rate} the first hit shows its effect and a later one again (first {shown}, second {shownAgain})");
            Assert(executor.Counters.GetValueOrDefault("damagefx-without-weapon") == 0, $"at {rate} every hit had its shot");
            throttle.Add((rate, (double)(shownAgain - shown) / rate.TicksPerSecond));
        }

        SameGameTime("under-attack notification: time until the next one (Radar::tryUnderAttackEvent, 10 s)", underAttack, 10.0);
        SameGameTime("damage effect: time until the next one of the same damage type (ThrottleTime 1000 ms)", throttle, 1.0);
    }
}
