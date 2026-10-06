using System.Globalization;
using CnCNew.Data;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// The time scale of two unit conditions (docs/TICKRATE-MIGRATION.md §14.4): the cheer lasts three seconds, and the free fall of an avalanche rock is limited to the first
/// hundred original frames of its slide, at 30, 60 and 120 Hz. Durations are measured in ticks of each world and compared in seconds; the tolerance is one original frame.
/// </summary>
internal static partial class UnitConditionTests
{
    private static readonly SimulationRate[] TimeScaleRates = [SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120];
    private const double OneFrame = 1.0 / OriginalFrame.PerSecond;

    private static string Seconds(IEnumerable<(SimulationRate Rate, double Seconds)> values)
        => string.Join(", ", values.Select(value => string.Create(CultureInfo.InvariantCulture, $"{value.Rate}: {value.Seconds:F4} s")));

    private static void TimeScaleCheering()
    {
        var shown = new List<(SimulationRate Rate, double Seconds)>();
        foreach (var rate in TimeScaleRates)
        {
            var rig = new Rig(new SyntheticFixture(rate, files: Files()));
            var scout = rig.Spawn("UnitTrooper", 1, 100, 100);
            // Ask on a whole second, so every rate asks at the same game time.
            while (rig.World.Tick % rig.World.Time.TicksPerSecond != 0) rig.Step();
            rig.Projector.Cheer([scout], rig.World);
            long ticks = 0;
            for (var tick = 0; tick < 5 * rig.World.Time.TicksPerSecond; tick++)
            {
                rig.Step();
                if (rig.Bits(scout).Contains(UnitFlags.SpecialCheering)) ticks++;
            }

            Assert(ticks > 0, $"at {rate} the unit cheers");
            shown.Add((rate, (double)ticks / rig.World.Time.TicksPerSecond));
        }

        Console.WriteLine("       SPECIAL_CHEERING after ALL_CHEER: " + Seconds(shown));
        foreach (var (rate, seconds) in shown)
            Assert(Math.Abs(seconds - shown[0].Seconds) <= OneFrame + 1e-9 && Math.Abs(seconds - 3.0) <= OneFrame + 1e-9, $"the cheer lasts {seconds:F4} s at {rate} (three seconds, {shown[0].Seconds:F4} s at {shown[0].Rate})");
    }

    private static void TimeScaleTensileFreefall()
    {
        var first = new List<(SimulationRate Rate, double Seconds)>();
        var last = new List<(SimulationRate Rate, double Seconds)>();
        var shownFor = new List<(SimulationRate Rate, double Seconds)>();
        foreach (var rate in TimeScaleRates)
        {
            var files = new Dictionary<string, string>(Files())
            {
                ["Data/INI/Object/UnitRock.ini"] = """
                Object UnitRock
                  KindOf = SELECTABLE
                  Body = ActiveBody Body01
                    MaxHealth = 100.0
                    InitialHealth = 100.0
                  End
                  Behavior = TensileFormationUpdate Tensile01
                    Enabled = Yes
                  End
                  Behavior = DestroyDie Destroy01
                  End
                End
                """,
            };
            // The slope of TensileFreefall: 4 height bytes (2.5 units) per 10 units toward +x.
            var rig = new Rig(new SyntheticFixture(rate, files: files), Terrain((x, _) => (byte)Math.Min(255, 4 * (21 - x))));
            var rock = rig.Spawn("UnitRock", 0, 100, 100, z: 0);
            var entity = rig.Get(rock);
            var module = entity.Modules.OfType<TensileFormationUpdate>().Single();
            var limit = rig.World.Time.OriginalFrames(100);
            long firstLife = -1, lastLife = -1, ticks = 0;
            for (var tick = 0; tick < 11 * rig.World.Time.TicksPerSecond; tick++)
            {
                rig.Step();
                if (!rig.Bits(rock).Contains(UnitFlags.Freefall)) continue;
                Assert(module.Life > 0 && module.Life < limit, $"at {rate} FREEFALL shows only in the first hundred original frames of the slide (life {module.Life} ticks, limit {limit})");
                if (firstLife < 0) firstLife = module.Life;
                lastLife = module.Life;
                ticks++;
            }

            Assert(ticks > 0 && module.Life >= limit, $"at {rate} the rock showed FREEFALL and slid on past the limit (life {module.Life} ticks)");
            first.Add((rate, (double)firstLife / rig.World.Time.TicksPerSecond));
            last.Add((rate, (double)lastLife / rig.World.Time.TicksPerSecond));
            shownFor.Add((rate, (double)ticks / rig.World.Time.TicksPerSecond));
        }

        Console.WriteLine("       FREEFALL of a sliding rock: first shown at slide time " + Seconds(first) + "; last shown at " + Seconds(last) + "; shown for " + Seconds(shownFor));
        // The window is the presentation's: never at or after 100 original frames (3.3333 s) of the slide, at any rate.
        foreach (var (rate, seconds) in last)
            Assert(seconds < 100.0 / OriginalFrame.PerSecond, $"at {rate} the last FREEFALL lies inside the first hundred original frames ({seconds:F4} s)");
    }
}
