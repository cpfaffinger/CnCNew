using System.Collections.Immutable;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Skirmish;

namespace CnCNew.Net;

/// <summary>
/// What a relay recorded - the match description and every bundle - as a replay file (docs/NETWORK-PROTOCOL.md section 18.4): the simulation's own replay format, with the
/// description in its header and the commands of the bundles, each with its player's order number as sequence. The computer players are not in it: they are slots of the
/// description and decide again when the file is played (<c>SkirmishReplay.Play</c>). The relay never simulates; it only writes down what it sent.
/// </summary>
public static class RecordingFile
{
    public const string FixtureId = "relay-recording";

    /// <param name="endTick">The tick the replay ends at: the ticks below it are played.</param>
    public static byte[] ToReplay(byte[] description, IReadOnlyList<TickBundleMessage> bundles, long endTick)
    {
        var decoded = MatchDescription.Decode(description);
        var commands = ImmutableArray.CreateBuilder<SimCommand>();
        foreach (var bundle in bundles)
        {
            if (bundle.Tick >= endTick) break;
            foreach (var entry in bundle.Commands) commands.Add(CommandWire.DecodeIntent(entry)(entry.Order, bundle.Tick));
        }
        // The players of the header are what the description resolves to on whoever plays the file; a relay does not know the factions a "random" stands for.
        var header = new ReplayHeader(ReplayFormat.CurrentVersion, CommandTypes.Version, decoded.TickRate, decoded.DefinitionFingerprint, FixtureId, decoded.Seed, new WorldId(1), [], endTick, 0,
            decoded.TerrainFingerprint) { MatchDescription = Convert.ToBase64String(description) };
        return ReplayFormat.Write(new ReplayFile(header, [..commands.OrderBy(command => command.Tick).ThenBy(command => command.Player.Value).ThenBy(command => command.Sequence)], []));
    }
}
