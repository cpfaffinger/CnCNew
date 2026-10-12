using System.Collections.Immutable;
using CnCNew.Simulation.State;

namespace CnCNew.Simulation.World;

public sealed partial class SimulationWorld
{
    /// <summary>
    /// The state hash taken apart, for the diagnosis of a desync (docs/NETWORK-DIAGNOSTICS.md): the sums the world keeps separately anyway - objects, timers, pending
    /// commands - and the hash of everything else (tick, identifiers, random streams, players, vision). Two worlds whose <see cref="StateHash"/> differs differ in at least
    /// one of them. Reading it changes nothing; the state hash itself is composed as before.
    /// </summary>
    public ImmutableArray<(string Name, ulong Hash)> StateHashParts()
    {
        _ = ComputeIncrementalHash();
        var global = new HashStateWriter();
        global.BeginObject(null);
        DescribeGlobal(global);
        global.EndObject();
        return [("world.global", global.Hash), ("world.objects", _entitySum), ("world.timers", _timerSum), ("world.commands", _commandSum)];
    }

    /// <summary>The hash every object contributes to the state hash, in id order: what a diagnosis compares to name the objects two worlds differ in.</summary>
    public void CopyObjectHashes(List<(uint Id, ulong Hash)> into)
    {
        _ = ComputeIncrementalHash();
        into.Clear();
        foreach (var entity in _entities.Values) into.Add((entity.Id.Value, entity.CachedHash));
    }
}
