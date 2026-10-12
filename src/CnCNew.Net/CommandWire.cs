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
        if (type == CommandTypes.PlayerLeave)
        {
            // A client leaves by saying so (LeaveMessage) or by vanishing; the command is the relay's to put into a bundle.
            error = "a leave command as an intent";
            return false;
        }
        if (!CommandTypes.IsPlayerOrder(type))
        {
            // Told by the type alone, without decoding: a command only the system may give, or one that exists for fixtures (a spawn would be a free army).
            error = $"command type {type} is not an order of a player";
            return false;
        }
        return true;
    }

    /// <summary>The command the relay puts into a bundle when a player has left: the leaving player's own, stamped like any other.</summary>
    public static byte[] Leave(int player, long tick, bool transferToAlly)
        => ReplayFormat.EncodeCommand(new PlayerLeaveCommand(new PlayerId(player), 0, tick, transferToAlly));

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
    /// <summary>
    /// <see cref="DecodeIntent"/> without the exception: false, and why, for a command of a bundle that is not one. The relay passes command bytes on unread, so a
    /// member can put garbage behind a plausible header; every client sees the same bytes and must do the same with them - leave the command out and count it -
    /// instead of ending its match (docs/NETWORK-PROTOCOL.md section 20.6).
    /// </summary>
    public static bool TryDecodeIntent(BundleCommand entry, out Func<ulong, long, SimCommand> intent, out string error)
    {
        try
        {
            intent = DecodeIntent(entry);
            error = string.Empty;
            return true;
        }
        catch (ProtocolException exception)
        {
            intent = null!;
            error = exception.Message;
            return false;
        }
    }

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
        // The leave comes from the relay itself (origin 0); nothing else that is not a player's order belongs in a bundle.
        if (!CommandTypes.IsPlayerOrder(decoded.TypeCode) && !(decoded is PlayerLeaveCommand && entry.Origin == 0))
            throw new ProtocolException("A bundle carries a command that is not an order of a player.");
        return (sequence, tick) => decoded with { Sequence = sequence, Tick = tick };
    }
}
