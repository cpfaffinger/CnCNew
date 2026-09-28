using System.Collections.Immutable;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Replay;

/// <summary>The replay cannot be run on these definitions, at this tick rate or with this command table.</summary>
public sealed class ReplayRejectedException(string message) : InvalidOperationException(message);

/// <summary>A checkpoint hash of the replay differs from the hash of the run.</summary>
public sealed class ReplayDivergenceException(long tick, ulong expected, ulong actual)
    : InvalidOperationException($"Replay diverged at tick {tick}: expected state hash {expected:X16}, got {actual:X16}.")
{
    public long Tick { get; } = tick;
}

public sealed class ReplayRunOptions
{
    /// <summary>Keep the hash of every tick (needed to compare two runs tick by tick).</summary>
    public bool RecordTickHashes { get; init; }

    /// <summary>Record a checkpoint every N ticks (0: none). Used when a replay is created from a run.</summary>
    public int RecordCheckpointInterval { get; init; }

    /// <summary>Fully recompute and compare the incremental hash every N ticks (0: never).</summary>
    public int VerifyIncrementalEvery { get; init; }

    /// <summary>Stop before executing this tick (defaults to the replay's end tick).</summary>
    public long? StopTick { get; init; }

    /// <summary>Called after each tick with the world and the tick result (for snapshots, metrics, sampling).</summary>
    public Action<SimulationWorld, TickResult>? AfterTick { get; init; }
}

public sealed record ReplayRunResult(
    long TicksExecuted, long EndTick, ulong FinalHash, ulong[] TickHashes, ImmutableArray<ReplayCheckpoint> Checkpoints,
    int VerifiedCheckpoints, int SubmittedCommands, int RejectedCommands);

/// <summary>Runs replays and creates worlds from replay headers.</summary>
public static class ReplayRunner
{
    public static void CheckCompatibility(ReplayHeader header, string definitionFingerprint)
    {
        if (header.FormatVersion != ReplayFormat.CurrentVersion) throw new ReplayRejectedException($"Replay format version {header.FormatVersion} is not supported.");
        if (header.CommandTableVersion != CommandTypes.Version)
            throw new ReplayRejectedException($"The replay uses command table version {header.CommandTableVersion}; this build implements {CommandTypes.Version}.");
        if (header.TickRate != SimulationTiming.LogicFramesPerSecond)
            throw new ReplayRejectedException($"The replay was recorded at {header.TickRate} Hz; the simulation runs at {SimulationTiming.LogicFramesPerSecond} Hz.");
        if (!string.Equals(header.DefinitionFingerprint, definitionFingerprint, StringComparison.OrdinalIgnoreCase))
            throw new ReplayRejectedException("The replay was recorded against different definitions (fingerprint mismatch); it is refused.");
    }

    public static SimulationWorld CreateWorld(ReplayHeader header, TemplateCatalog templates)
        => new(new WorldConfiguration(header.WorldId, header.Seed, header.Players, header.FixtureId, header.DefinitionFingerprint.ToUpperInvariant()), templates);

    /// <summary>Runs the replay from a fresh world.</summary>
    public static ReplayRunResult Run(ReplayFile replay, TemplateCatalog templates, string definitionFingerprint, ReplayRunOptions? options = null)
    {
        CheckCompatibility(replay.Header, definitionFingerprint);
        return Run(replay, CreateWorld(replay.Header, templates), options);
    }

    /// <summary>Runs (or continues) the replay on an existing world, starting at the world's current tick.</summary>
    public static ReplayRunResult Run(ReplayFile replay, SimulationWorld world, ReplayRunOptions? options = null)
    {
        options ??= new ReplayRunOptions();
        var end = Math.Min(options.StopTick ?? replay.Header.EndTick, replay.Header.EndTick);
        var startTick = world.Tick;
        var hashes = options.RecordTickHashes ? new ulong[Math.Max(0, end - startTick)] : [];
        var checkpoints = ImmutableArray.CreateBuilder<ReplayCheckpoint>();
        var expected = replay.Checkpoints.ToDictionary(value => value.Tick, value => value.Hash);
        var verified = 0;
        var submitted = 0;
        var rejected = 0;

        var commandIndex = 0;
        while (commandIndex < replay.Commands.Length && replay.Commands[commandIndex].Tick < startTick) commandIndex++;
        while (world.Tick < end)
        {
            var tick = world.Tick;
            while (commandIndex < replay.Commands.Length && replay.Commands[commandIndex].Tick == tick)
            {
                submitted++;
                if (!world.Submit(replay.Commands[commandIndex++]).Accepted) rejected++;
            }
            var result = world.Step();
            if (options.RecordTickHashes) hashes[tick - startTick] = result.StateHash;
            if (expected.TryGetValue(tick, out var expectedHash))
            {
                if (expectedHash != result.StateHash) throw new ReplayDivergenceException(tick, expectedHash, result.StateHash);
                verified++;
            }
            if (options.RecordCheckpointInterval > 0 && (tick + 1) % options.RecordCheckpointInterval == 0)
                checkpoints.Add(new ReplayCheckpoint(tick, result.StateHash));
            if (options.VerifyIncrementalEvery > 0 && (tick + 1) % options.VerifyIncrementalEvery == 0) world.VerifyIncrementalHash();
            options.AfterTick?.Invoke(world, result);
        }
        return new ReplayRunResult(world.Tick - startTick, world.Tick, world.StateHash, hashes, checkpoints.ToImmutable(), verified, submitted, rejected);
    }
}
