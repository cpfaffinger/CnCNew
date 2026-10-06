using System.Collections.Immutable;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Random;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.Timers;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Testing;

/// <summary>
/// Options of the stress test. Every number here counts <b>simulation steps</b> (ticks of the fixture's rate), none of them game time: <paramref name="Ticks"/> is the length of the
/// run, <paramref name="DrainTicks"/> the share of it at the end in which the survivors are removed, <paramref name="CheckpointInterval"/> the distance of the replay's checkpoints.
/// </summary>
public sealed record StressOptions(long Ticks, int TargetPopulation, uint WorldSeed, uint DriverSeed, long DrainTicks, int CheckpointInterval)
{
    public static StressOptions Standard { get; } = new(216_000, 10_500, 0xC0FFEE01, 0x5EED0001, 6_000, 1_000);
}

/// <summary>
/// Deterministic command generator for the stress test. It observes only world events (never wall time, threads or ambient
/// randomness) and its own <see cref="SageRandom"/>, so recording it twice yields the same command stream. The recorded stream
/// becomes a replay that any run must reproduce hash for hash.
/// <para>
/// Load: a ramp to the target population of long-lived entities, then per tick one kill plus one replacement spawn of a
/// long-lived entity, a spark every second tick and a puff every third tick, random damage, owner-issued destroys, and timers
/// (repeating notifications and damage) attached to a share of the new entities. The last <see cref="StressOptions.DrainTicks"/>
/// ticks kill every survivor so all tables return to their baseline.
/// </para>
/// <para>
/// <b>The schedule counts steps and does not scale with the tick rate.</b> The stress test loads every step of the world with a fixed churn (commands per step, timers per new
/// entity); a schedule in game time would halve that load at 60 Hz and measure something else. The ramp, the modulo cadences, the timer delays of the commands (ticks by the
/// command's own contract) and the drain are therefore the same numbers at every rate. Relative to the fixture's authored lifetimes (milliseconds, which do scale) the churn is
/// twice as dense in game time at 60 Hz. The one game-time value is <see cref="DrainReserveFrames"/>: what dies has to be gone before the run ends, whatever the rate.
/// </para>
/// </summary>
public sealed class StressScenario
{
    private readonly StressOptions _options;
    private readonly SageRandom _random;
    private readonly List<AliveEntity> _alive = [];
    private readonly Dictionary<uint, int> _aliveIndex = [];
    private readonly ulong[] _sequence = new ulong[3];
    private readonly List<SimCommand> _commands = [];
    private readonly List<uint> _newlySpawned = [];
    private readonly string[] _longLived = ["Barrel", "Tank", "Building"];

    // The schedule in simulation steps (see the class comment: not scaled with the rate).
    private const long RampSteps = 100;
    private const long SparkEverySteps = 2;
    private const long PuffEverySteps = 3;
    private const long DamageEverySteps = 2;
    private const long GhostEverySteps = 40;
    private const long InvalidCommandEverySteps = 97;
    private const long WorldTimerEverySteps = 500;
    private const long WorldTimerStepPhase = 250;

    /// <summary>The drain has killed everything this long before the run ends, so that what dies slowly is gone by then: game time, in logic frames of the original (2 s).</summary>
    private const long DrainReserveFrames = 60;

    private sealed record AliveEntity(uint Id, int Owner, bool LongLived, bool Rubble);

    public StressScenario(StressOptions options)
    {
        _options = options;
        _random = new SageRandom(options.DriverSeed);
    }

    public static ImmutableArray<PlayerConfiguration> Players { get; } =
    [
        new PlayerConfiguration(new PlayerId(1), "Alpha"),
        new PlayerConfiguration(new PlayerId(2), "Beta"),
    ];

    /// <summary>Peak number of simultaneously alive entities seen while recording.</summary>
    public int PeakAlive { get; private set; }

    /// <summary>Records the command stream by running a world with the generator attached. Checkpoints are added by a second run.</summary>
    public ReplayFile Record(SyntheticFixture fixture)
    {
        var header = new ReplayHeader(ReplayFormat.CurrentVersion, CommandTypes.Version, fixture.Rate.TicksPerSecond, fixture.Fingerprint,
            SyntheticFixture.FixtureId, _options.WorldSeed, new WorldId(1), Players, _options.Ticks, _options.CheckpointInterval);
        var world = ReplayRunner.CreateWorld(header, fixture.Templates);
        while (world.Tick < _options.Ticks)
        {
            var tick = world.Tick;
            Generate(world, tick);
            var result = world.Step();
            Observe(result.Events);
            PeakAlive = Math.Max(PeakAlive, _alive.Count);
        }
        return new ReplayFile(header, [.._commands.OrderBy(value => value.Tick).ThenBy(value => value.Player.Value).ThenBy(value => value.Sequence)], []);
    }

    private void Observe(IReadOnlyList<SimEvent> events)
    {
        _newlySpawned.Clear();
        foreach (var simEvent in events)
        {
            switch (simEvent.Kind)
            {
                case SimEventKind.EntitySpawned:
                    var template = simEvent.Text;
                    _aliveIndex[simEvent.Entity.Value] = _alive.Count;
                    _alive.Add(new AliveEntity(simEvent.Entity.Value, simEvent.Player.Value, template is "Barrel" or "Tank" or "Building", template == "Building"));
                    _newlySpawned.Add(simEvent.Entity.Value);
                    break;
                case SimEventKind.EntityDestroyed:
                    RemoveAlive(simEvent.Entity.Value);
                    break;
            }
        }
    }

    private void RemoveAlive(uint id)
    {
        if (!_aliveIndex.Remove(id, out var index)) return;
        var last = _alive.Count - 1;
        if (index != last)
        {
            _alive[index] = _alive[last];
            _aliveIndex[_alive[index].Id] = index;
        }
        _alive.RemoveAt(last);
    }

    private void Add(SimCommand command, SimulationWorld world)
    {
        _commands.Add(command);
        world.Submit(command);
    }

    private ulong Next(int player) => ++_sequence[player];

    private void Generate(SimulationWorld world, long tick)
    {
        var draining = tick >= _options.Ticks - _options.DrainTicks;
        var longLived = 0;
        foreach (var entity in _alive) if (entity.LongLived) longLived++;

        // Timers for entities that appeared last tick.
        foreach (var id in _newlySpawned)
        {
            if (draining) break;
            var roll = _random.Range(0, 99);
            if (roll < 30)
                Add(new SetTimerCommand(PlayerId.System, Next(0), tick, new EntityId(id), 30, 90, TimerActionKind.Notify, Fixed.Zero, DamageTypes.Explosion, roll), world);
            else if (roll < 45)
                Add(new SetTimerCommand(PlayerId.System, Next(0), tick, new EntityId(id), 120, 300, TimerActionKind.Damage, Fixed.FromInt(1), DamageTypes.Explosion, roll), world);
        }
        _newlySpawned.Clear();
        if (tick % WorldTimerEverySteps == WorldTimerStepPhase && !draining)
            Add(new SetTimerCommand(PlayerId.System, Next(0), tick, EntityId.Invalid, 45, 0, TimerActionKind.Notify, Fixed.Zero, DamageTypes.Explosion, 7), world);

        if (draining)
        {
            var remaining = (int)Math.Max(1, _options.Ticks - tick - world.Time.OriginalFrames(DrainReserveFrames));
            var kills = Math.Max(1, (_alive.Count + remaining - 1) / remaining);
            for (var index = 0; index < kills && _alive.Count > 0; index++)
            {
                var victim = _alive[_random.Range(0, _alive.Count - 1)];
                Add(new DestroyCommand(PlayerId.System, Next(0), tick, new EntityId(victim.Id)), world);
                RemoveAlive(victim.Id);
            }
            return;
        }

        var spawnLongLived = tick < RampSteps ? Math.Max(1, _options.TargetPopulation / (int)RampSteps) : (longLived < _options.TargetPopulation ? 2 : 1);
        for (var index = 0; index < spawnLongLived; index++)
            SpawnRandom(world, tick, _longLived[_random.Range(0, 99) switch { < 50 => 0, < 90 => 1, _ => 2 }]);
        if (tick % SparkEverySteps == 0) SpawnRandom(world, tick, "Spark");
        if (tick % PuffEverySteps == 0) SpawnRandom(world, tick, "Puff");
        if (tick % GhostEverySteps == 0) SpawnRandom(world, tick, "Ghost");

        if (tick >= RampSteps && longLived >= _options.TargetPopulation)
        {
            var victim = PickLongLived();
            if (victim is not null)
            {
                // Rubble structures survive their own death (KeepObjectDie), so they are removed explicitly.
                if (victim.Rubble || _random.Range(0, 99) < 30)
                {
                    Add(new DestroyCommand(new PlayerId(victim.Owner), Next(victim.Owner), tick, new EntityId(victim.Id)), world);
                    RemoveAlive(victim.Id);
                }
                else
                    Add(new KillCommand(PlayerId.System, Next(0), tick, new EntityId(victim.Id)), world);
            }
        }
        if (_alive.Count > 0 && tick % DamageEverySteps == 1)
        {
            var target = _alive[_random.Range(0, _alive.Count - 1)];
            Add(new DamageCommand(PlayerId.System, Next(0), tick, new EntityId(target.Id), Fixed.FromInt(_random.Range(1, 20)), _random.Range(0, 30), _random.Range(0, 20)), world);
        }
        if (tick % InvalidCommandEverySteps == 0)
        {
            // Deliberately invalid commands: the harness must reject them explicitly and identically in every run.
            Add(new DestroyCommand(new PlayerId(1), Next(1), tick, new EntityId(uint.MaxValue - 5)), world);
        }
    }

    private AliveEntity? PickLongLived()
    {
        for (var attempt = 0; attempt < 32 && _alive.Count > 0; attempt++)
        {
            var candidate = _alive[_random.Range(0, _alive.Count - 1)];
            if (candidate.LongLived) return candidate;
        }
        return null;
    }

    private void SpawnRandom(SimulationWorld world, long tick, string template)
    {
        var player = _random.Range(1, 2);
        var x = Fixed.FromInt(_random.Range(0, 4000));
        var y = Fixed.FromInt(_random.Range(0, 4000));
        var angle = _random.Range(Fixed.Zero, Fixed.FromInt(6));
        Add(new SpawnCommand(new PlayerId(player), Next(player), tick, template, PlayerId.System, x, y, Fixed.Zero, angle), world);
    }
}
