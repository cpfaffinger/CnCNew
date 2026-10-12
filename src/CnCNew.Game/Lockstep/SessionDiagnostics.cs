using System.Collections.Immutable;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using CnCNew.Net;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Skirmish;

namespace CnCNew.Game.Lockstep;

/// <summary>
/// The state of a session as it travels to a client that joins a running match (docs/NETWORK-PROTOCOL.md section 18.2): the session's own document
/// (<see cref="SkirmishMatchSession.Save"/>), compressed. The relay passes the bytes on unread.
/// </summary>
public static class SessionTransfer
{
    private static readonly byte[] Magic = "CNZ1"u8.ToArray();

    public static byte[] Pack(ReadOnlySpan<byte> state, CompressionLevel level = CompressionLevel.Fastest)
    {
        using var output = new MemoryStream();
        output.Write(Magic);
        using (var brotli = new BrotliStream(output, level, leaveOpen: true)) brotli.Write(state);
        return output.ToArray();
    }

    /// <summary>The state out of its packing; bytes that are not packed are the state itself (the saved match of a local game).</summary>
    public static byte[] Unpack(byte[] packed, int limit = Protocol.MaxResumeBytes * 4)
    {
        if (packed.Length < Magic.Length || !packed.AsSpan(0, Magic.Length).SequenceEqual(Magic)) return packed;
        using var input = new MemoryStream(packed, Magic.Length, packed.Length - Magic.Length);
        using var brotli = new BrotliStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while (true)
        {
            // The decoder reports damaged input as an invalid operation; to whoever reads a state from the wire it is invalid data.
            try
            {
                read = brotli.Read(buffer);
            }
            catch (InvalidOperationException exception)
            {
                throw new InvalidDataException("The packed state is damaged.", exception);
            }
            if (read <= 0) break;
            // A few bytes can unpack to gigabytes: what does not fit a state is not one.
            if (output.Length + read > limit) throw new InvalidDataException("The packed state unpacks to more than a session state can be.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }
}

/// <summary>
/// What a client remembers of its own session for the diagnosis of a desync (docs/NETWORK-DIAGNOSTICS.md): for the last ticks the session hash and its parts, and for the
/// last few of them the hash of every object. When the server reports a desync the client can say which part differed at that tick, and it writes a package from which
/// <c>CnCNew.NetTool desync-diff</c> names the tick, the objects and the fields.
/// </summary>
public sealed class SessionDiagnostics(int hashTicks = 1_024, int objectTicks = 128)
{
    public const string PackageFormat = "cncnew-desync-package";
    public const int PackageVersion = 1;

    private readonly (long Tick, ulong Hash, ImmutableArray<(string Name, ulong Hash)> Parts)[] _hashes = new (long, ulong, ImmutableArray<(string, ulong)>)[Math.Max(1, hashTicks)];
    private readonly (long Tick, List<(uint Id, ulong Hash)> Objects)[] _objects = [..Enumerable.Range(0, Math.Max(1, objectTicks)).Select(_ => (-1L, new List<(uint, ulong)>()))];
    private long _recorded;

    /// <summary>Called after the step of <paramref name="tick"/>.</summary>
    public void Record(long tick, SkirmishMatchSession session, ulong sessionHash)
    {
        _hashes[_recorded % _hashes.Length] = (tick, sessionHash, session.StateHashParts());
        var slot = _recorded % _objects.Length;
        _objects[slot].Tick = tick;
        session.World.CopyObjectHashes(_objects[slot].Objects);
        _recorded++;
    }

    public bool TryGetParts(long tick, out ImmutableArray<HashPart> parts)
    {
        foreach (var entry in _hashes)
            if (entry.Tick == tick && !entry.Parts.IsDefault && _recorded > 0)
            {
                parts = [..entry.Parts.Select(part => new HashPart(part.Name, part.Hash))];
                return true;
            }
        parts = [];
        return false;
    }

    /// <summary>
    /// Writes the package of this client into a new directory: what it was (info), the match so far as a replay, the state it stands at, the hashes and parts of the
    /// last ticks, and the object hashes of the last few. Nothing in it is game data; it is the state of this match.
    /// </summary>
    public void WritePackage(string directory, SkirmishMatchSession session, LockstepClient client, DesyncMessage desync)
    {
        Directory.CreateDirectory(directory);
        var invariant = CultureInfo.InvariantCulture;
        var info = new StringBuilder();
        info.AppendLine("{");
        info.AppendLine(invariant, $"  \"format\": \"{PackageFormat}\",");
        info.AppendLine(invariant, $"  \"version\": {PackageVersion},");
        info.AppendLine(invariant, $"  \"connection\": {client.ConnectionId},");
        info.AppendLine(invariant, $"  \"player\": {client.Player},");
        info.AppendLine(invariant, $"  \"desyncTick\": {desync.Tick},");
        info.AppendLine(invariant, $"  \"stateTick\": {session.World.Tick},");
        info.AppendLine(invariant, $"  \"build\": \"{client.Identity.Id}\",");
        info.AppendLine(invariant, $"  \"reports\": \"{string.Join(' ', desync.Reports.Select(report => string.Create(invariant, $"{report.ConnectionId}:{report.SessionHash:X16}")))}\"");
        info.AppendLine("}");
        File.WriteAllText(Path.Combine(directory, "info.json"), info.ToString());
        File.WriteAllBytes(Path.Combine(directory, "replay.bin"), ReplayFormat.Write(session.ToReplay()));
        File.WriteAllBytes(Path.Combine(directory, "state.bin"), session.Save());
        var hashes = new StringBuilder();
        foreach (var entry in _hashes.Where(entry => !entry.Parts.IsDefault).OrderBy(entry => entry.Tick))
            hashes.AppendLine(invariant, $"{entry.Tick}\t{entry.Hash:X16}\t{string.Join('\t', entry.Parts.Select(part => string.Create(invariant, $"{part.Name}={part.Hash:X16}")))}");
        File.WriteAllText(Path.Combine(directory, "hashes.tsv"), hashes.ToString());
        var objects = new StringBuilder();
        foreach (var (tick, list) in _objects.Where(entry => entry.Tick >= 0).OrderBy(entry => entry.Tick))
            foreach (var (id, hash) in list) objects.AppendLine(invariant, $"{tick}\t{id}\t{hash:X16}");
        File.WriteAllText(Path.Combine(directory, "objects.tsv"), objects.ToString());
    }
}
