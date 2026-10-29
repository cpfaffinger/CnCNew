using System.Collections.Generic;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

/// <summary>
/// F-68, the worker's two brains (<c>WorkerAIUpdate.cpp</c>, <c>WorkerStateMachine</c>): a worker acts as a dozer by default - it gets bored and repairs -, becomes a supply truck when
/// it docks, and a dozer again when its supply brain is busy with something else.
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> WorkerBrainFiles() => new Dictionary<string, string>(SupplyFiles())
    {
        ["Data/INI/Object/WorkerBrain.ini"] = """
        Locomotor LaborerLoco
          Surfaces = GROUND
          TurnRate = 360.0
          Speed = 30.0
          Acceleration = 30.0
          Braking = 30.0
          MinSpeed = 0.0
          CloseEnoughDist = 1.0
        End
        Object Laborer
          KindOf = SELECTABLE INFANTRY DOZER
          Locomotor = SET_NORMAL LaborerLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = WorkerAIUpdate AI01
            RepairHealthPercentPerSecond = 10%
            BoredTime = 5000
            BoredRange = 150
            MaxBoxes = 2
            SupplyCenterActionDelay = 100
            SupplyWarehouseActionDelay = 100
            SupplyWarehouseScanDistance = 400.0
          End
        End
        """,
    };

    /// <summary>
    /// A worker stands idle beside a hurt bench: 150 frames of the original after its first frame it repairs the bench (before, a worker never did). Sent to a warehouse, it acts as a
    /// supply truck and delivers at the depot; sent away while it gathers, it is a dozer again. Every step in the same frame at 30, 60 and 120 Hz, and a snapshot while it gathers
    /// continues identically.
    /// </summary>
    public static void WorkerSwitchesBetweenItsBrains()
    {
        List<long>? reference = null;
        long healing30 = -1;
        long HealingAt30(long value) => healing30 < 0 ? value : healing30;
        foreach (var (rate, factor) in WorldRates)
        {
            var (world, feeder, fixture) = RatedWorld(rate, WorkerBrainFiles());
            feeder.Spawn(1, "Depot", x: -100);
            feeder.Spawn(1, "Warehouse", x: 0, y: 300);
            feeder.Spawn(1, "Laborer", x: 0);
            feeder.Spawn(1, "Bench", x: 100);
            for (var tick = 0; tick < factor; tick++) world.Step();
            var laborer = world.FindEntity(new EntityId(IdOf(world, "Laborer")))!;
            var bench = world.FindEntity(new EntityId(IdOf(world, "Bench")))!;
            var worker = laborer.Modules.OfType<AIUpdateInterface>().Single();
            var brains = (IWorkerBrains)worker;
            Check.That(feeder.Damage(bench.Id.Value, 50).Accepted, $"{rate}: the bench is hurt.");
            var start = world.Tick;
            // Frames of the worker's own grid (from the tick it appeared in), which is a tick off the world's above 30 Hz: the decisions it takes lie on it.
            long Frame() => (world.Tick - laborer.SpawnTick + factor - 1) / factor - (start - laborer.SpawnTick) / factor;
            long bored = -1, healing = -1, repaired = -1, truck = -1, delivered = -1, dozer = -1;
            var docked = false;
            var sentAway = false;
            for (var frame = 0; frame < 2_000 && dozer < 0; frame++)
            {
                for (var tick = 0; tick < factor; tick++)
                    foreach (var item in world.Step().Events)
                        if (item.Kind == SimEventKind.SupplyDeposited && delivered < 0) delivered = Frame();
                if (bored < 0 && worker.HasBuilderTask) bored = Frame();
                if (healing < 0 && bench.Body!.Health > Fixed.FromInt(50)) healing = Frame();
                if (repaired < 0 && bored >= 0 && bench.Body!.Health == bench.Body.MaxHealth) repaired = Frame();
                if (repaired >= 0 && !docked)
                {
                    docked = true;
                    Check.That(!brains.ActsAsSupplyTruck, $"{rate}: the repairing worker is a dozer.");
                    Check.That(feeder.Dock(1, laborer.Id.Value, (uint)IdOf(world, "Warehouse")).Accepted, $"{rate}: the dock order is accepted.");
                }
                if (truck < 0 && brains.ActsAsSupplyTruck) truck = Frame();
                if (delivered >= 0 && !sentAway)
                {
                    sentAway = true;
                    if (factor == 2)
                    {
                        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(world), fixture.Templates, fixture.Fingerprint);
                        for (var follow = 0; follow < 60 * factor; follow++)
                            Check.That(restored.Step().StateHash == world.Step().StateHash, $"{rate}: the restored world gathers as the live one (step {follow}).");
                    }
                    var away = feeder.SetMoveTarget(1, laborer.Id.Value, 0, -200);
                    Check.That(away.Accepted, $"{rate}: the worker is sent away ({away.Reason})."); 
                }
                if (sentAway && dozer < 0 && !brains.ActsAsSupplyTruck) dozer = Frame();
            }
            Check.That(bored > 0 && repaired > bored, $"{rate}: the idle worker repairs the bench by itself ({bored}, {repaired}).");
            Check.That(truck > repaired && delivered > truck, $"{rate}: sent to the warehouse it acts as a supply truck and delivers ({truck}, {delivered}).");
            Check.That(dozer > 0, $"{rate}: sent away it is a dozer again ({dozer}).");
            // The bench is full a frame later above 30 Hz (the heal slice per frame is rounded): the steps of the two brains are compared from where it is full.
            Check.That(healing == HealingAt30(healing), $"{rate}: the repair begins in the frame it begins at 30 Hz ({healing}, F-70).");
            var steps = new List<long> { bored, truck - repaired, delivered - truck, dozer - delivered };
            if (reference is null)
            {
                Check.That(bored == 151, $"The worker is bored 150 frames after its first frame ({bored}).");
                reference = steps;
                healing30 = healing;
                continue;
            }
            // At 60 Hz the snapshot check steps the world 60 frames before the worker is sent away.
            var compared = factor == 2 ? steps.Take(3).Append(dozer - 60 - delivered) : steps;
            Check.That(compared.SequenceEqual(reference), $"{rate}: the same frames as at 30 Hz ({string.Join(",", compared)} against {string.Join(",", reference)}).");
        }
    }
}
