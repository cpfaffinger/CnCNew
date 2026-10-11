using System.Buffers.Binary;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Replay;

namespace CnCNew.Net;

/// <summary>
/// A command on the wire: the simulation's own command codec (<see cref="ReplayFormat.EncodeCommand"/>), nothing invented here. A client sends an intent with tick 0 and
/// sequence 0; the relay, which never decodes a command, checks the header and stamps the player of the connection and the tick of the bundle at their fixed offsets. The
/// sequence number stays 0 on the wire: the session of every client assigns it when the command is submitted, in the order of the bundle.
/// </summary>
public static class CommandWire
{
    /// <summary>The bytes of an intent: the command as built with tick 0 and sequence 0.</summary>
    public static byte[] EncodeIntent(Func<ulong, long, SimCommand> intent)
    {
        var bytes = ReplayFormat.EncodeCommand(intent(0, 0));
        if (bytes.Length > Protocol.MaxCommandBytes) throw new ProtocolException($"A command of {bytes.Length} bytes exceeds the limit of {Protocol.MaxCommandBytes}.");
        return bytes;
    }

    /// <summary>What a relay checks without decoding: the header is there and names a command type this protocol knows, and the command is not the system's.</summary>
    public static bool IsPlausible(ReadOnlySpan<byte> command, out string error)
    {
        error = string.Empty;
        if (command.Length < ReplayFormat.CommandHeaderLength || command.Length > Protocol.MaxCommandBytes)
        {
            error = $"a command of {command.Length} bytes";
            return false;
        }
        var type = BinaryPrimitives.ReadUInt16LittleEndian(command[ReplayFormat.CommandTypeOffset..]);
        if (!CommandTypes.IsKnown(type))
        {
            error = $"unknown command type {type}";
            return false;
        }
        return true;
    }

    /// <summary>The command with the player of its connection and the tick of its bundle written into its header.</summary>
    public static byte[] Stamp(ReadOnlySpan<byte> command, int player, long tick)
    {
        var stamped = command.ToArray();
        BinaryPrimitives.WriteInt64LittleEndian(stamped.AsSpan(ReplayFormat.CommandTickOffset), tick);
        BinaryPrimitives.WriteInt32LittleEndian(stamped.AsSpan(ReplayFormat.CommandPlayerOffset), player);
        BinaryPrimitives.WriteUInt64LittleEndian(stamped.AsSpan(ReplayFormat.CommandSequenceOffset), 0);
        return stamped;
    }

    /// <summary>
    /// A command of a bundle as an intent: the decoded command, given the tick and the sequence number its session assigns at submission. A command whose bytes do not decode,
    /// or that claims another player than the bundle says, is a violation of the protocol.
    /// </summary>
    public static Func<ulong, long, SimCommand> DecodeIntent(BundleCommand entry)
    {
        SimCommand decoded;
        try
        {
            decoded = ReplayFormat.DecodeCommand(entry.Command);
        }
        catch (ReplayFormatException exception)
        {
            throw new ProtocolException($"A command of a bundle does not decode: {exception.Message}");
        }
        if (decoded.Player != new PlayerId(entry.Player)) throw new ProtocolException("A command of a bundle names another player than its entry.");
        if (decoded.RequiresSystem) throw new ProtocolException("A bundle carries a command only the system may give.");
        return (sequence, tick) => decoded with { Sequence = sequence, Tick = tick };
    }
}
