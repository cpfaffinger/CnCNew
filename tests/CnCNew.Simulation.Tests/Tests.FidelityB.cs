using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>
/// Fidelity package B (Phase 39, docs/SIMULATION-CORE.md §53): the findings F-06 to F-10 and F-43 of the fidelity register (docs/plan/STAGE-1-PORT.md §3), each a test that
/// fails on the code before its fix, at the rate of the run (30, 60 and 120 Hz).
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> FidelityBFiles() => new Dictionary<string, string>(RetailModuleFiles())
    {
        ["Data/INI/Object/FidelityB.ini"] = """
        Object OtherUnit
          KindOf = SELECTABLE INFANTRY
          BuildCost = 10
          BuildTime = 1
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
        End

        Object PairFactory
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = ProductionUpdate Prod01
            MaxQueueEntries = 3
            QuantityModifier = NoSuchUnit 5
            QuantityModifier = QuickUnit 2
          End
          Behavior = QueueProductionExitUpdate Exit01
            UnitCreatePoint = X:0.0 Y:8.0 Z:0.0
            NaturalRallyPoint = X:0.0 Y:20.0 Z:0.0
            ExitDelay = 300
          End
        End
        """,
    };

    /// <summary>
    /// F-08: <c>ProductionUpdate::queueCreateUnit</c> applies the <c>QuantityModifier</c> of the ordered template - one order of a Red Guard gives two, paid once, the second
    /// leaving the exit's delay after the first; a name the data does not define matches nothing; another template gives one; a cancel after the first unit refunds the whole cost.
    /// </summary>
    public static void FidelityQuantityModifier()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: FidelityBFiles());
        Check.That(fixture.Templates.TryGet("PairFactory", out var factory) && factory.IsInstantiable, "The factory with two QuantityModifier lines binds.");

        SimulationWorld Factory(out Feeder feeder)
        {
            var world = NewWorld(fixture);
            feeder = new Feeder(world);
            feeder.Spawn(1, "PairFactory");
            world.Step();
            feeder.GiveMoney(1, 1000);
            world.Step();
            return world;
        }

        var paired = Factory(out var pairedFeeder);
        var ticks = SpawnTicks(paired, pairedFeeder, "QuickUnit", 1, 200);
        for (var i = 0; i < paired.Frames(30) && CountOf(paired, "QuickUnit") < 2; i++)
        {
            paired.Step();
            if (CountOf(paired, "QuickUnit") == 2) ticks.Add(paired.Tick);
        }
        Check.That(CountOf(paired, "QuickUnit") == 2, $"One order of a template with QuantityModifier 2 makes two units ({CountOf(paired, "QuickUnit")}).");
        Check.That(paired.MoneyOf(new PlayerId(1)) == Fixed.FromInt(990), $"The order is paid once ({paired.MoneyOf(new PlayerId(1))}).");
        Check.That(ticks.Count == 2 && paired.InFrames(ticks[1] - ticks[0]) is >= 9 and <= 10,
            $"The second unit leaves the exit delay (300 ms = 9 frames) after the first: {string.Join(", ", ticks)}.");
        for (var i = 0; i < paired.Frames(60); i++) paired.Step();
        Check.That(CountOf(paired, "QuickUnit") == 2, "No third unit follows.");

        var other = Factory(out var otherFeeder);
        SpawnTicks(other, otherFeeder, "OtherUnit", 1, 200);
        for (var i = 0; i < other.Frames(60); i++) other.Step();
        Check.That(CountOf(other, "OtherUnit") == 1, $"A template without a modifier gives one unit ({CountOf(other, "OtherUnit")}).");

        // a save between the two units of one order
        var live = Factory(out var liveFeeder);
        SpawnTicks(live, liveFeeder, "QuickUnit", 1, 200);
        Check.That(CountOf(live, "QuickUnit") == 1, "Setup: the first unit has left, the second waits for the exit.");
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(live), fixture.Templates, fixture.Fingerprint);
        Check.That(restored.StateHash == live.StateHash, "The entry with one of two units made round-trips through a snapshot.");
        for (var i = 0; i < live.Frames(30); i++)
            Check.That(live.Step().StateHash == restored.Step().StateHash, "The rest of the order is hash-identical after a restore at step " + i + ".");
        Check.That(CountOf(restored, "QuickUnit") == 2, "The restored world makes the second unit.");

        // a cancel after the first unit: cancelUnitCreate deposits the whole cost
        var cancelled = Factory(out var cancelledFeeder);
        SpawnTicks(cancelled, cancelledFeeder, "QuickUnit", 1, 200);
        cancelledFeeder.CancelProduction(1, 1, 0);
        cancelled.Step();
        for (var i = 0; i < cancelled.Frames(30); i++) cancelled.Step();
        Check.That(CountOf(cancelled, "QuickUnit") == 1 && cancelled.MoneyOf(new PlayerId(1)) == Fixed.FromInt(1000),
            $"A cancel after the first unit keeps it, makes no second and refunds the whole cost ({CountOf(cancelled, "QuickUnit")}, {cancelled.MoneyOf(new PlayerId(1))}).");
    }
}
