using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Definitions;
using CnCNew.Data.Registry;
using CnCNew.Data.Schema;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Random;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.Timers;
using CnCNew.Simulation.World;

var run = new Runner();
run.Test("Fixed-point arithmetic, rounding and limits", Tests.FixedPoint);
run.Test("Tick rate, duration and unit conversions", Tests.Timing);
run.Test("Reference float conversion vs exact ticks (documented deviation)", Tests.FloatDeviation);
run.Test("Golden RNG sequences (independent transcription)", Tests.GoldenRandom);
run.Test("RNG streams: seeds, state round trip, isolation", Tests.RandomStreamsBehave);
run.Test("Ids: monotone, start at 1, 0 invalid", Tests.IdsAreMonotone);
run.Test("Binding table validation", Tests.BindingTable);
run.Test("Templates from the synthetic fixture", Tests.FixtureTemplates);
run.Test("Spawn, tick, damage, death and destroy lifecycle", Tests.Lifecycle);
run.Test("Body and die module semantics", Tests.BodyAndDieModules);
run.Test("Lifetime and deletion updates", Tests.LifetimeAndDeletion);
run.Test("Update order, mutation during iteration, event delivery", Tests.UpdateOrderAndMutation);
run.Test("Timers", Tests.TimerBehaviour);
run.Test("Command validation and order", Tests.CommandValidationAndOrder);
run.Test("State hash: determinism, order independence, incremental == full", Tests.HashDeterminism);
run.Test("Snapshot round trip and continuation", Tests.SnapshotRoundTrip);
run.Test("Corrupt, truncated and foreign snapshots are rejected", Tests.SnapshotRejection);
run.Test("Replay format round trip and rejection", Tests.ReplayFormatTests);
run.Test("Replay equality, divergence detection and continuation", Tests.ReplayRuns);
run.Test("Stress scenario returns to baseline (short run)", Tests.ShortStress);
run.Test("Definition insertion order does not change the simulation", Tests.DefinitionOrderIndependence);
run.Test("Idle ticks do not allocate", Tests.IdleAllocation);
run.Test("Determinism rules hold in the simulation sources", Tests.SourceRules);
run.Test("Phase 5: fear-sound RNG consumption (block 0.3)", Tests.Phase5FearSoundRng);
run.Test("Phase 5: economy and upgrade purchase", Tests.Phase5Economy);
run.Test("Phase 5: weapon fire, armor, clip and reload", Tests.Phase5Combat);
run.Test("Phase 5: armor/weapon/max-health upgrade effects", Tests.Phase5UpgradeEffects);
run.Test("Phase 5: straight-line movement and arrival", Tests.Phase5Movement);
run.Test("Phase 5: production queue, spawn, cancel and refund", Tests.Phase5Production);
run.Test("Phase 5: stealth cloaking and auto-heal", Tests.Phase5StealthAndHeal);
run.Test("Phase 5: FlammableUpdate ignite/burn/damage cycle", Tests.Phase5Flammable);
run.Test("Phase 5: body damage states and TransitionDamageFX", Tests.Phase5DamageStateTransitions);
run.Test("Phase 5: player/faction setup and science purchase", Tests.Phase5Sciences);
run.Test("Phase 5: SlowDeathBehavior weighted sibling lottery", Tests.Phase5SlowDeath);
run.Test("Phase 5: FireWeaponWhenDeadBehavior activation and conflict re-check", Tests.Phase5FireWeaponWhenDead);
run.Test("Phase 5: StealthDetectorUpdate marks and expires DETECTED", Tests.Phase5StealthDetector);
run.Test("Phase 5: PoisonedBehavior periodic damage, re-poison and healing", Tests.Phase5Poisoned);
run.Test("Phase 5: GarrisonContain capacity, entry, healing and ejection", Tests.Phase5Garrison);
run.Test("Phase 5: TransportContain slot-weighted capacity and health regen", Tests.Phase5Transport);
run.Test("Phase 5: CreateCrateDie conditions, draw order and requests", Tests.Phase5CreateCrateDie);
run.Test("Phase 5: ObjectCreationList execution and skip events", Tests.Phase5CreationLists);
run.Test("Phase 5: ObjectCreationList determinism and snapshot", Tests.Phase5CreationListDeterminism);
run.Test("Fixed-point Sin/Cos accuracy", Tests.FixedTrigAccuracy);
run.Test("Phase 5: last player standing wins", Tests.Phase5Victory);
var contentDirectory = args.SkipWhile(value => value != "--content-dir").Skip(1).FirstOrDefault();
if (args.Contains("--long")) run.Test("Long run: 216,000 ticks, fresh runs and snapshot continuation", Tests.LongRun);
if (contentDirectory is not null) run.Test("Real data: instantiate compiled shipped objects", () => Tests.RealData(contentDirectory));
return run.Finish();

sealed class Runner
{
    private int _failed;
    private int _passed;

    public void Test(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine($"  ok   {name}");
        }
        catch (Exception exception)
        {
            _failed++;
            Console.Error.WriteLine($"  FAIL {name}: {exception.Message}");
            Console.Error.WriteLine(exception.StackTrace);
        }
    }

    public int Finish()
    {
        if (_failed > 0)
        {
            Console.Error.WriteLine($"{_failed} simulation test(s) failed, {_passed} passed.");
            return 1;
        }
        Console.WriteLine("All simulation core tests passed.");
        return 0;
    }
}

static class Check
{
    public static void That(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Throws<T>(Action action, string messagePart) where T : Exception
    {
        try { action(); }
        catch (T exception) when (exception.Message.Contains(messagePart, StringComparison.OrdinalIgnoreCase)) { return; }
        catch (Exception exception) { throw new InvalidOperationException($"Expected {typeof(T).Name} containing '{messagePart}', got {exception.GetType().Name}: {exception.Message}"); }
        throw new InvalidOperationException($"Expected {typeof(T).Name} containing '{messagePart}', but nothing was thrown.");
    }
}

/// <summary>A test module that records what the world does to it, and can be told to act. Not part of the production bindings.</summary>
sealed class ProbeModule(ProbeLog log, ProbeData data) : SimModule, IUpdateModule, IEventReceiver
{
    private long _nextWake = -1;
    private int _updates;

    public override string ModuleName => "ProbeModule";
    public long NextWake { get => _nextWake; set => _nextWake = value; }

    public override void OnSpawn(IWorldServices world)
    {
        _nextWake = world.Tick + data.FirstWake;
        log.Add($"spawn:{Entity.Id}:{world.Tick}");
    }

    public long Update(IWorldServices world)
    {
        _updates++;
        log.Add($"update:{Entity.Id}:{world.Tick}");
        if (data.DestroyTarget != 0 && world.Tick == data.DestroyAtTick) world.RequestDestroy(new EntityId(data.DestroyTarget));
        if (data.EmitAtTick == world.Tick) world.Emit(SimEventKind.FxRequested, Entity.Id, EntityId.Invalid, "probe", 1);
        return data.Sleep;
    }

    public void OnEvent(IWorldServices world, in SimEvent simEvent) => log.Add($"event:{Entity.Id}:{simEvent.Kind}:{world.Tick}");
    public override void OnRemove(IWorldServices world) => log.Add($"remove:{Entity.Id}:{world.Tick}");

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Int("updates", _updates);
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _updates = (int)reader.Int("updates");
    }
}

sealed record ProbeData(long FirstWake, long Sleep, uint DestroyTarget, long DestroyAtTick, long EmitAtTick) : ModuleData;

sealed class ProbeLog
{
    public List<string> Entries { get; } = [];
    public void Add(string entry) => Entries.Add(entry);
}
