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

        Object DoorFactory
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 500.0
            InitialHealth = 500.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = ProductionUpdate Prod01
            MaxQueueEntries = 3
            NumDoorAnimations = 1
            DoorOpeningTime = 1000
            DoorWaitOpenTime = 1000
            DoorCloseTime = 500
          End
          Behavior = DefaultProductionExitUpdate Exit01
            UnitCreatePoint = X:10.0 Y:0.0 Z:0.0
            NaturalRallyPoint = X:20.0 Y:0.0 Z:0.0
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

    /// <summary>
    /// F-09: <c>ProductionUpdate::update</c> opens the door when the unit is finished and makes the unit only when the door is fully open (more than <c>DoorOpeningTime</c> later,
    /// <c>updateDoors</c>); a unit finished while the door waits open leaves at once; the door closes after <c>DoorWaitOpenTime</c> and <c>DoorCloseTime</c>; a save while the door opens
    /// continues the same; a cancel while it opens makes nothing.
    /// </summary>
    public static void FidelityUnitAtOpenDoor()
    {
        var fixture = new SyntheticFixture(TestRate.Default, files: FidelityBFiles());
        Check.That(fixture.Templates.TryGet("DoorFactory", out var factory) && factory.IsInstantiable, "The factory with a door binds.");

        SimulationWorld Factory(string name, out Feeder feeder)
        {
            var world = NewWorld(fixture);
            feeder = new Feeder(world);
            feeder.Spawn(1, name);
            world.Step();
            feeder.GiveMoney(1, 1000);
            world.Step();
            return world;
        }
        CnCNew.Simulation.Modules.BuiltIn.ProductionUpdate Production(SimulationWorld world)
            => world.FindEntity(new EntityId(1))!.Modules.OfType<CnCNew.Simulation.Modules.BuiltIn.ProductionUpdate>().Single();

        var plain = Factory("DefaultExitFactory", out var plainFeeder);
        var plainTick = SpawnTicks(plain, plainFeeder, "QuickUnit", 1, 200).Single();
        var doored = Factory("DoorFactory", out var dooredFeeder);
        var doorTicks = SpawnTicks(doored, dooredFeeder, "QuickUnit", 1, 200);
        Check.That(doorTicks.Count == 1 && doored.InFrames(doorTicks[0] - plainTick) == 31 && (doorTicks[0] - plainTick) % doored.Frames(1) == 0,
            $"The unit appears DoorOpeningTime and one frame (31 frames) after the same order at a factory without a door ({plainTick} against {string.Join(", ", doorTicks)}).");
        var frame = doored.Frames(1);
        Check.That(Production(doored).DoorAt(0, doored.Tick - 1, frame) == CnCNew.Simulation.Modules.BuiltIn.ProductionUpdate.DoorPhase.WaitingOpen,
            "When the unit appears the door is waiting open.");

        // a second order finished while the door waits open leaves at the end of its build time
        dooredFeeder.QueueProduction(1, 1, "QuickUnit");
        var secondTick = -1L;
        for (var i = 0; i < doored.Frames(60) && secondTick < 0; i++)
        {
            doored.Step();
            if (CountOf(doored, "QuickUnit") == 2) secondTick = doored.Tick;
        }
        // (ordered in the tick the first appeared, which above 30 Hz need not begin a frame: the build takes BuildTime less up to one frame)
        Check.That(secondTick > 0 && secondTick - doorTicks[0] > doored.Frames(29) && secondTick - doorTicks[0] <= doored.Frames(30),
            $"A unit finished behind a door waiting open leaves at once, BuildTime after the order ({doorTicks[0]}, {secondTick}).");
        for (var i = 0; i < doored.Frames(31); i++) doored.Step();
        Check.That(Production(doored).DoorAt(0, doored.Tick - 1, frame) == CnCNew.Simulation.Modules.BuiltIn.ProductionUpdate.DoorPhase.Closing, "More than DoorWaitOpenTime later the door closes.");
        for (var i = 0; i < doored.Frames(16); i++) doored.Step();
        Check.That(Production(doored).DoorAt(0, doored.Tick - 1, frame) == CnCNew.Simulation.Modules.BuiltIn.ProductionUpdate.DoorPhase.Closed, "More than DoorCloseTime later it is shut.");

        // a save while the door opens
        var live = Factory("DoorFactory", out var liveFeeder);
        liveFeeder.QueueProduction(1, 1, "QuickUnit");
        for (var i = 0; i < live.Frames(40); i++) live.Step();
        Check.That(CountOf(live, "QuickUnit") == 0 && Production(live).DoorAt(0, live.Tick - 1, frame) == CnCNew.Simulation.Modules.BuiltIn.ProductionUpdate.DoorPhase.Opening,
            "Setup: the unit is finished and its door is opening.");
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(live), fixture.Templates, fixture.Fingerprint);
        Check.That(restored.StateHash == live.StateHash, "The opening door round-trips through a snapshot.");
        for (var i = 0; i < live.Frames(60); i++)
            Check.That(live.Step().StateHash == restored.Step().StateHash, "The door and the unit are hash-identical after a restore at step " + i + ".");
        Check.That(CountOf(restored, "QuickUnit") == 1, "The restored world makes the unit when the door is open.");

        // a cancel while the door opens
        var cancelled = Factory("DoorFactory", out var cancelledFeeder);
        cancelledFeeder.QueueProduction(1, 1, "QuickUnit");
        for (var i = 0; i < cancelled.Frames(40); i++) cancelled.Step();
        cancelledFeeder.CancelProduction(1, 1, 0);
        for (var i = 0; i < cancelled.Frames(60); i++) cancelled.Step();
        Check.That(CountOf(cancelled, "QuickUnit") == 0 && cancelled.MoneyOf(new PlayerId(1)) == Fixed.FromInt(1000), "A cancel while the door opens makes nothing and refunds the cost.");
    }

    /// <summary>
    /// F-07: a weapon whose clip reloads in no time (<c>ClipReloadTime = 0</c>) fires at most once per logic frame of the original. The original fires from the attack state of its
    /// AI, once per frame (<c>Weapon::getStatus</c> is ready again in the frame of the last shot, but nothing asks twice); the port fires one shot per command, and a second command
    /// in the tick of the shot that emptied the clip fired again at 30 Hz.
    /// </summary>
    public static void FidelityNoReloadClipFiresOncePerFrame()
    {
        foreach (var (rate, _) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, TimingFiles());
            feeder.Spawn(1, "TimingGunner", x: 100, y: 100);
            feeder.Spawn(2, "TimingDummy", x: 150, y: 100);
            world.Step();
            TimingAlign(world);
            var gunner = TimingOnly(world, "TimingGunner");
            var dummy = TimingOnly(world, "TimingDummy");
            int Shots(int commands)
            {
                for (var i = 0; i < commands; i++) feeder.FireWeapon(1, gunner.Id.Value, dummy.Id.Value);
                return world.Step().Events.Count(item => item.Kind == CnCNew.Simulation.Events.SimEventKind.WeaponFired && item.Entity == gunner.Id);
            }
            Check.That(Shots(1) == 1, $"{rate}: the first shot of the clip.");
            for (var i = 0; i < world.Frames(4); i++) world.Step();
            TimingAlign(world);
            var emptying = Shots(2);
            Check.That(emptying == 1, $"{rate}: the shot that empties the clip and a second command in the same tick fire once ({emptying}).");
            TimingAlign(world);
            Check.That(Shots(1) == 1, $"{rate}: the reloaded clip fires in the next frame.");
        }
    }
}

