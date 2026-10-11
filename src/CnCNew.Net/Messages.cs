using System.Collections.Immutable;
using CnCNew.Simulation.Skirmish;

namespace CnCNew.Net;

/// <summary>The message types of protocol version <see cref="Protocol.Version"/> (docs/NETWORK-PROTOCOL.md section 3). A number is never reused.</summary>
public enum MessageType : ushort
{
    Hello = 1,
    Welcome = 2,
    Reject = 3,
    RoomConfig = 4,
    SlotRequest = 5,
    Ready = 6,
    RoomState = 7,
    StartRequest = 8,
    MatchStart = 9,
    LoadProgress = 10,
    LoadProgressState = 11,
    Loaded = 12,
    AllLoaded = 13,
    Chat = 14,
    ChatMessage = 15,
    Intent = 16,
    TickRequest = 17,
    TickBundle = 18,
    HashReport = 19,
    PaceRequest = 20,
    PaceState = 21,
    PlayerDropped = 22,
    Desync = 23,
    EndReport = 24,
    MatchEnd = 25,
}

/// <summary>Why the server turned a connection or a request away.</summary>
public enum RejectReason : ushort
{
    ProtocolVersion = 1,
    SimulationVersion = 2,
    FormatVersion = 3,
    Definitions = 4,
    ModifiedBuild = 5,
    RoomFull = 6,
    MatchRunning = 7,
    NotHost = 8,
    InvalidRequest = 9,
    NotReady = 10,
}

public enum RoomPhase : byte
{
    Room = 0,
    Loading = 1,
    Match = 2,
    Ended = 3,
}

/// <summary>Who closes the ticks: the server by its clock, or the one client of a local game by asking.</summary>
public enum Pacing : byte
{
    Server = 0,
    Client = 1,
}

public enum DropReason : byte
{
    Left = 0,
    ConnectionLost = 1,
    ProtocolViolation = 2,
    Desync = 3,
}

public enum EndReason : byte
{
    Finished = 0,
    Desync = 1,
    Abandoned = 2,
}

/// <summary>The limits of the protocol. A message above its limit is discarded and counted, and so is one of an unknown type.</summary>
public static class Protocol
{
    public const ushort Version = 1;
    /// <summary>Upper limit of every message except <see cref="MatchStartMessage"/> and <see cref="StartRequestMessage"/>, which may carry a session state.</summary>
    public const int MaxMessageBytes = 64 * 1024;
    public const int MaxResumeBytes = 64 * 1024 * 1024;
    public const int MaxNameBytes = 64;
    public const int MaxChatBytes = 512;
    public const int MaxPathBytes = 260;
    public const int MaxIdBytes = 128;
    public const int MaxCommandBytes = 8 * 1024;
    public const int MaxDescriptionBytes = 16 * 1024;
    public const int MaxSlots = 8;
    public const int MaxConnections = 16;
    /// <summary>Intents a player may have in one tick's bundle; what arrives beyond it waits for the next tick.</summary>
    public const int MaxIntentsPerTick = 64;
    public const int MaxBundleCommands = MaxIntentsPerTick * MaxSlots;
    public const int MinSpeedPercent = 25;
    public const int MaxSpeedPercent = 800;
}

public abstract record NetMessage
{
    public abstract MessageType Type { get; }
    internal abstract void Write(WireWriter writer);
}

/// <summary>Client to server, first message of a connection: who is this, and can it play with the others at all.</summary>
public sealed record HelloMessage(ushort ProtocolVersion, string SimulationId, int CommandTableVersion, int ReplayFormatVersion, int WorldStateVersion, int DescriptionEncoding,
    string DefinitionFingerprint, string Name) : NetMessage
{
    public override MessageType Type => MessageType.Hello;

    internal override void Write(WireWriter writer)
    {
        writer.U16(ProtocolVersion);
        writer.Text(SimulationId, Protocol.MaxIdBytes);
        writer.I32(CommandTableVersion);
        writer.I32(ReplayFormatVersion);
        writer.I32(WorldStateVersion);
        writer.I32(DescriptionEncoding);
        writer.Text(DefinitionFingerprint, Protocol.MaxIdBytes);
        writer.Text(Name, Protocol.MaxNameBytes);
    }

    internal static HelloMessage Read(ref WireReader reader)
        => new(reader.U16(), reader.Text(Protocol.MaxIdBytes), reader.I32(), reader.I32(), reader.I32(), reader.I32(), reader.Text(Protocol.MaxIdBytes), reader.Text(Protocol.MaxNameBytes));
}

public sealed record WelcomeMessage(uint ConnectionId, bool IsHost) : NetMessage
{
    public override MessageType Type => MessageType.Welcome;

    internal override void Write(WireWriter writer)
    {
        writer.U32(ConnectionId);
        writer.Bool(IsHost);
    }

    internal static WelcomeMessage Read(ref WireReader reader) => new(reader.U32(), reader.Bool());
}

public sealed record RejectMessage(RejectReason Reason, string Text) : NetMessage
{
    public override MessageType Type => MessageType.Reject;

    internal override void Write(WireWriter writer)
    {
        writer.U16((ushort)Reason);
        writer.Text(Text, Protocol.MaxChatBytes);
    }

    internal static RejectMessage Read(ref WireReader reader)
    {
        var reason = reader.U16();
        if (!Enum.IsDefined((RejectReason)reason)) throw new ProtocolException($"Unknown reject reason {reason}.");
        return new RejectMessage((RejectReason)reason, reader.Text(Protocol.MaxChatBytes));
    }
}

/// <summary>
/// What a room is set to: everything of a <see cref="MatchDescription"/> except the seeds, which the server chooses at the start (a host of an embedded server may state them,
/// <see cref="HasSeed"/>), and the fingerprints of definitions and simulation, which come from the handshake.
/// </summary>
public sealed record RoomSettings(int TickRate, string MapPath, string TerrainFingerprint, long MaxTicks, int StartMoney, long GraceFrames, long SciencePointIntervalFrames,
    ImmutableArray<MatchSlotDescription> Slots, bool HasSeed = false, uint Seed = 0, uint AiSeed = 0)
{
    public static RoomSettings Empty { get; } = new(0, string.Empty, string.Empty, 0, 0, 0, 0, []);

    internal void Write(WireWriter writer)
    {
        writer.I32(TickRate);
        writer.Text(MapPath, Protocol.MaxPathBytes);
        writer.Text(TerrainFingerprint, Protocol.MaxIdBytes);
        writer.I64(MaxTicks);
        writer.I32(StartMoney);
        writer.I64(GraceFrames);
        writer.I64(SciencePointIntervalFrames);
        writer.Bool(HasSeed);
        writer.U32(Seed);
        writer.U32(AiSeed);
        if (Slots.Length > Protocol.MaxSlots) throw new ProtocolException($"A room has at most {Protocol.MaxSlots} slots.");
        writer.U8((byte)Slots.Length);
        foreach (var slot in Slots)
        {
            writer.U8((byte)slot.Kind);
            writer.Text(slot.Faction, Protocol.MaxNameBytes);
            writer.I32(slot.Team);
            writer.I32(slot.Color);
            writer.I32(slot.StartPosition);
            writer.I32(slot.AiStrength);
            writer.I32(slot.AiComposition);
        }
    }

    internal static RoomSettings Read(ref WireReader reader)
    {
        var tickRate = reader.I32();
        var map = reader.Text(Protocol.MaxPathBytes);
        var terrain = reader.Text(Protocol.MaxIdBytes);
        var maxTicks = reader.I64();
        var money = reader.I32();
        var grace = reader.I64();
        var science = reader.I64();
        var hasSeed = reader.Bool();
        var seed = reader.U32();
        var aiSeed = reader.U32();
        var count = reader.U8();
        if (count > Protocol.MaxSlots) throw new ProtocolException($"A room has at most {Protocol.MaxSlots} slots, not {count}.");
        var slots = ImmutableArray.CreateBuilder<MatchSlotDescription>(count);
        for (var index = 0; index < count; index++)
        {
            var kind = reader.U8();
            if (kind > (byte)MatchSlotKind.Spectator) throw new ProtocolException($"Unknown slot kind {kind}.");
            slots.Add(new MatchSlotDescription((MatchSlotKind)kind, reader.Text(Protocol.MaxNameBytes), reader.I32(), reader.I32(), reader.I32(), reader.I32(), reader.I32()));
        }
        return new RoomSettings(tickRate, map, terrain, maxTicks, money, grace, science, slots.MoveToImmutable(), hasSeed, seed, aiSeed);
    }

    /// <summary>Content equality (the slots are compared one by one).</summary>
    public bool SameAs(RoomSettings other)
        => TickRate == other.TickRate && MapPath == other.MapPath && TerrainFingerprint == other.TerrainFingerprint && MaxTicks == other.MaxTicks && StartMoney == other.StartMoney
           && GraceFrames == other.GraceFrames && SciencePointIntervalFrames == other.SciencePointIntervalFrames && HasSeed == other.HasSeed && Seed == other.Seed && AiSeed == other.AiSeed
           && Slots.SequenceEqual(other.Slots);
}

/// <summary>Host to server: set the room.</summary>
public sealed record RoomConfigMessage(RoomSettings Settings) : NetMessage
{
    public override MessageType Type => MessageType.RoomConfig;
    internal override void Write(WireWriter writer) => Settings.Write(writer);
    internal static RoomConfigMessage Read(ref WireReader reader) => new(RoomSettings.Read(ref reader));
}

/// <summary>Client to server: sit in a human slot (or leave it: <see cref="Leave"/>), with the faction, team, colour and start position wished for.</summary>
public sealed record SlotRequestMessage(byte Slot, string Faction, int Team, int Color, int StartPosition) : NetMessage
{
    public const byte Leave = 255;
    public override MessageType Type => MessageType.SlotRequest;

    internal override void Write(WireWriter writer)
    {
        writer.U8(Slot);
        writer.Text(Faction, Protocol.MaxNameBytes);
        writer.I32(Team);
        writer.I32(Color);
        writer.I32(StartPosition);
    }

    internal static SlotRequestMessage Read(ref WireReader reader) => new(reader.U8(), reader.Text(Protocol.MaxNameBytes), reader.I32(), reader.I32(), reader.I32());
}

public sealed record ReadyMessage(bool Ready) : NetMessage
{
    public override MessageType Type => MessageType.Ready;
    internal override void Write(WireWriter writer) => writer.Bool(Ready);
    internal static ReadyMessage Read(ref WireReader reader) => new(reader.Bool());
}

/// <param name="Slot">The slot the connection sits in, or -1.</param>
public readonly record struct RoomMember(uint ConnectionId, string Name, int Slot, bool Ready, bool IsHost);

/// <summary>Server to every client: the room as it is now. The server is always authoritative; a client shows this, never what it asked for.</summary>
public sealed record RoomStateMessage(RoomPhase Phase, RoomSettings Settings, ImmutableArray<RoomMember> Members) : NetMessage
{
    public override MessageType Type => MessageType.RoomState;

    internal override void Write(WireWriter writer)
    {
        writer.U8((byte)Phase);
        Settings.Write(writer);
        writer.U8((byte)Members.Length);
        foreach (var member in Members)
        {
            writer.U32(member.ConnectionId);
            writer.Text(member.Name, Protocol.MaxNameBytes);
            writer.I32(member.Slot);
            writer.Bool(member.Ready);
            writer.Bool(member.IsHost);
        }
    }

    internal static RoomStateMessage Read(ref WireReader reader)
    {
        var phase = reader.U8();
        if (phase > (byte)RoomPhase.Ended) throw new ProtocolException($"Unknown room phase {phase}.");
        var settings = RoomSettings.Read(ref reader);
        var count = reader.U8();
        if (count > Protocol.MaxConnections) throw new ProtocolException($"A room has at most {Protocol.MaxConnections} members.");
        var members = ImmutableArray.CreateBuilder<RoomMember>(count);
        for (var index = 0; index < count; index++) members.Add(new RoomMember(reader.U32(), reader.Text(Protocol.MaxNameBytes), reader.I32(), reader.Bool(), reader.Bool()));
        return new RoomStateMessage((RoomPhase)phase, settings, members.MoveToImmutable());
    }
}

/// <summary>
/// A match that does not begin at tick 0: the state every client restores (a session state of the simulation, opaque to the server), the tick it stands at, its session hash
/// and, per human player, how many commands that player has already submitted (the server continues the order numbers from there).
/// </summary>
public sealed record ResumeState(long Tick, ulong SessionHash, ImmutableArray<(int Player, ulong Orders)> OrdersSoFar, byte[] State)
{
    internal void Write(WireWriter writer)
    {
        writer.I64(Tick);
        writer.U64(SessionHash);
        writer.U8((byte)OrdersSoFar.Length);
        foreach (var (player, orders) in OrdersSoFar)
        {
            writer.I32(player);
            writer.U64(orders);
        }
        writer.Bytes(State, Protocol.MaxResumeBytes);
    }

    internal static ResumeState Read(ref WireReader reader)
    {
        var tick = reader.I64();
        var hash = reader.U64();
        var count = reader.U8();
        if (count > Protocol.MaxSlots) throw new ProtocolException("A resume state names more players than a room has slots.");
        var orders = ImmutableArray.CreateBuilder<(int, ulong)>(count);
        for (var index = 0; index < count; index++) orders.Add((reader.I32(), reader.U64()));
        return new ResumeState(tick, hash, orders.MoveToImmutable(), reader.Bytes(Protocol.MaxResumeBytes));
    }
}

/// <summary>Host to server: start the match, from tick 0 or from a saved state.</summary>
public sealed record StartRequestMessage(ResumeState? Resume) : NetMessage
{
    public override MessageType Type => MessageType.StartRequest;

    internal override void Write(WireWriter writer)
    {
        writer.Bool(Resume is not null);
        Resume?.Write(writer);
    }

    internal static StartRequestMessage Read(ref WireReader reader) => new(reader.Bool() ? ResumeState.Read(ref reader) : null);
}

/// <summary>
/// Server to every client: the match. <see cref="Description"/> are the canonical bytes of the <see cref="MatchDescription"/> (seeds chosen by the server);
/// <see cref="Player"/> is the player number of the receiving client (0: it watches).
/// </summary>
public sealed record MatchStartMessage(byte[] Description, int Player, Pacing Pacing, ushort HashInterval, ResumeState? Resume) : NetMessage
{
    public override MessageType Type => MessageType.MatchStart;

    internal override void Write(WireWriter writer)
    {
        writer.Bytes(Description, Protocol.MaxDescriptionBytes);
        writer.I32(Player);
        writer.U8((byte)Pacing);
        writer.U16(HashInterval);
        writer.Bool(Resume is not null);
        Resume?.Write(writer);
    }

    internal static MatchStartMessage Read(ref WireReader reader)
    {
        var description = reader.Bytes(Protocol.MaxDescriptionBytes);
        var player = reader.I32();
        var pacing = reader.U8();
        if (pacing > (byte)Pacing.Client) throw new ProtocolException($"Unknown pacing {pacing}.");
        var interval = reader.U16();
        if (interval == 0) throw new ProtocolException("A hash interval of 0.");
        return new MatchStartMessage(description, player, (Pacing)pacing, interval, reader.Bool() ? ResumeState.Read(ref reader) : null);
    }
}

public sealed record LoadProgressMessage(byte Percent) : NetMessage
{
    public override MessageType Type => MessageType.LoadProgress;
    internal override void Write(WireWriter writer) => writer.U8(Percent);

    internal static LoadProgressMessage Read(ref WireReader reader)
    {
        var percent = reader.U8();
        return percent <= 100 ? new LoadProgressMessage(percent) : throw new ProtocolException($"A loading progress of {percent} percent.");
    }
}

public sealed record LoadProgressStateMessage(uint ConnectionId, byte Percent) : NetMessage
{
    public override MessageType Type => MessageType.LoadProgressState;

    internal override void Write(WireWriter writer)
    {
        writer.U32(ConnectionId);
        writer.U8(Percent);
    }

    internal static LoadProgressStateMessage Read(ref WireReader reader) => new(reader.U32(), reader.U8());
}

/// <summary>Client to server: the match is built; this is its session hash at the start tick.</summary>
public sealed record LoadedMessage(ulong SessionHash) : NetMessage
{
    public override MessageType Type => MessageType.Loaded;
    internal override void Write(WireWriter writer) => writer.U64(SessionHash);
    internal static LoadedMessage Read(ref WireReader reader) => new(reader.U64());
}

public sealed record AllLoadedMessage(long StartTick) : NetMessage
{
    public override MessageType Type => MessageType.AllLoaded;
    internal override void Write(WireWriter writer) => writer.I64(StartTick);
    internal static AllLoadedMessage Read(ref WireReader reader) => new(reader.I64());
}

public sealed record ChatRequestMessage(string Text) : NetMessage
{
    public override MessageType Type => MessageType.Chat;
    internal override void Write(WireWriter writer) => writer.Text(Text, Protocol.MaxChatBytes);
    internal static ChatRequestMessage Read(ref WireReader reader) => new(reader.Text(Protocol.MaxChatBytes));
}

public sealed record ChatMessage(uint ConnectionId, string Text) : NetMessage
{
    public override MessageType Type => MessageType.ChatMessage;

    internal override void Write(WireWriter writer)
    {
        writer.U32(ConnectionId);
        writer.Text(Text, Protocol.MaxChatBytes);
    }

    internal static ChatMessage Read(ref WireReader reader) => new(reader.U32(), reader.Text(Protocol.MaxChatBytes));
}

/// <summary>
/// Client to server: a command intent. <see cref="Command"/> is the command in the simulation's command codec with tick 0 and sequence 0 (<see cref="CommandWire"/>); the server
/// stamps the player from the connection and assigns tick and order number. <see cref="Receipt"/> is the client's own number; it comes back with the bundle.
/// </summary>
public sealed record IntentMessage(ulong Receipt, byte[] Command) : NetMessage
{
    public override MessageType Type => MessageType.Intent;

    internal override void Write(WireWriter writer)
    {
        writer.U64(Receipt);
        writer.Bytes(Command, Protocol.MaxCommandBytes);
    }

    internal static IntentMessage Read(ref WireReader reader) => new(reader.U64(), reader.Bytes(Protocol.MaxCommandBytes));
}

/// <summary>Client to server, client-paced rooms only: close this tick now.</summary>
public sealed record TickRequestMessage(long Tick) : NetMessage
{
    public override MessageType Type => MessageType.TickRequest;
    internal override void Write(WireWriter writer) => writer.I64(Tick);
    internal static TickRequestMessage Read(ref WireReader reader) => new(reader.I64());
}

/// <param name="Order">The player's order number, counted by the server from 1: the order in which the player's commands are submitted.</param>
/// <param name="Origin">The connection the intent came from, and its <paramref name="Receipt"/> there.</param>
public sealed record BundleCommand(int Player, ulong Order, uint Origin, ulong Receipt, byte[] Command);

/// <summary>
/// Server to every client: the commands of one tick, in the order they are to be submitted (by player, then by order number). A tick without commands has an empty bundle;
/// no tick runs without its bundle.
/// </summary>
public sealed record TickBundleMessage(long Tick, ImmutableArray<BundleCommand> Commands) : NetMessage
{
    public override MessageType Type => MessageType.TickBundle;

    internal override void Write(WireWriter writer)
    {
        writer.I64(Tick);
        if (Commands.Length > Protocol.MaxBundleCommands) throw new ProtocolException("A bundle exceeds its command limit.");
        writer.U16((ushort)Commands.Length);
        foreach (var command in Commands)
        {
            writer.I32(command.Player);
            writer.U64(command.Order);
            writer.U32(command.Origin);
            writer.U64(command.Receipt);
            writer.Bytes(command.Command, Protocol.MaxCommandBytes);
        }
    }

    internal static TickBundleMessage Read(ref WireReader reader)
    {
        var tick = reader.I64();
        var count = reader.U16();
        if (count > Protocol.MaxBundleCommands) throw new ProtocolException($"A bundle of {count} commands exceeds the limit of {Protocol.MaxBundleCommands}.");
        var commands = ImmutableArray.CreateBuilder<BundleCommand>(count);
        for (var index = 0; index < count; index++) commands.Add(new BundleCommand(reader.I32(), reader.U64(), reader.U32(), reader.U64(), reader.Bytes(Protocol.MaxCommandBytes)));
        return new TickBundleMessage(tick, commands.MoveToImmutable());
    }
}

/// <summary>Client to server: the session hash after the step of <see cref="Tick"/>.</summary>
public sealed record HashReportMessage(long Tick, ulong SessionHash) : NetMessage
{
    public override MessageType Type => MessageType.HashReport;

    internal override void Write(WireWriter writer)
    {
        writer.I64(Tick);
        writer.U64(SessionHash);
    }

    internal static HashReportMessage Read(ref WireReader reader) => new(reader.I64(), reader.U64());
}

/// <summary>Client to server: please pause or resume, please run at this speed. A request; the answer is <see cref="PaceStateMessage"/>.</summary>
public sealed record PaceRequestMessage(bool Paused, ushort SpeedPercent) : NetMessage
{
    public override MessageType Type => MessageType.PaceRequest;

    internal override void Write(WireWriter writer)
    {
        writer.Bool(Paused);
        writer.U16(SpeedPercent);
    }

    internal static PaceRequestMessage Read(ref WireReader reader) => new(reader.Bool(), reader.U16());
}

public sealed record PaceStateMessage(bool Paused, ushort SpeedPercent, uint RequestedBy) : NetMessage
{
    public override MessageType Type => MessageType.PaceState;

    internal override void Write(WireWriter writer)
    {
        writer.Bool(Paused);
        writer.U16(SpeedPercent);
        writer.U32(RequestedBy);
    }

    internal static PaceStateMessage Read(ref WireReader reader) => new(reader.Bool(), reader.U16(), reader.U32());
}

public sealed record PlayerDroppedMessage(int Player, uint ConnectionId, DropReason Reason) : NetMessage
{
    public override MessageType Type => MessageType.PlayerDropped;

    internal override void Write(WireWriter writer)
    {
        writer.I32(Player);
        writer.U32(ConnectionId);
        writer.U8((byte)Reason);
    }

    internal static PlayerDroppedMessage Read(ref WireReader reader)
    {
        var player = reader.I32();
        var connection = reader.U32();
        var reason = reader.U8();
        if (reason > (byte)DropReason.Desync) throw new ProtocolException($"Unknown drop reason {reason}.");
        return new PlayerDroppedMessage(player, connection, (DropReason)reason);
    }
}

/// <summary>Server to every client: the reports of this tick differ. The hashes are listed per connection so that a client can tell on which side it stands.</summary>
public sealed record DesyncMessage(long Tick, ImmutableArray<(uint ConnectionId, ulong SessionHash)> Reports) : NetMessage
{
    public override MessageType Type => MessageType.Desync;

    internal override void Write(WireWriter writer)
    {
        writer.I64(Tick);
        writer.U8((byte)Reports.Length);
        foreach (var (connection, hash) in Reports)
        {
            writer.U32(connection);
            writer.U64(hash);
        }
    }

    internal static DesyncMessage Read(ref WireReader reader)
    {
        var tick = reader.I64();
        var count = reader.U8();
        if (count > Protocol.MaxConnections) throw new ProtocolException("A desync report lists more connections than a room has.");
        var reports = ImmutableArray.CreateBuilder<(uint, ulong)>(count);
        for (var index = 0; index < count; index++) reports.Add((reader.U32(), reader.U64()));
        return new DesyncMessage(tick, reports.MoveToImmutable());
    }
}

/// <summary>Client to server: my simulation says the match is over at this tick with this winner (0: nobody).</summary>
public sealed record EndReportMessage(long Tick, int Winner) : NetMessage
{
    public override MessageType Type => MessageType.EndReport;

    internal override void Write(WireWriter writer)
    {
        writer.I64(Tick);
        writer.I32(Winner);
    }

    internal static EndReportMessage Read(ref WireReader reader) => new(reader.I64(), reader.I32());
}

public sealed record MatchEndMessage(long Tick, int Winner, EndReason Reason) : NetMessage
{
    public override MessageType Type => MessageType.MatchEnd;

    internal override void Write(WireWriter writer)
    {
        writer.I64(Tick);
        writer.I32(Winner);
        writer.U8((byte)Reason);
    }

    internal static MatchEndMessage Read(ref WireReader reader)
    {
        var tick = reader.I64();
        var winner = reader.I32();
        var reason = reader.U8();
        if (reason > (byte)EndReason.Abandoned) throw new ProtocolException($"Unknown end reason {reason}.");
        return new MatchEndMessage(tick, winner, (EndReason)reason);
    }
}

/// <summary>
/// Messages to bytes and back. A message is <c>u16 type</c> followed by its fields; a transport that carries a byte stream prefixes it with its length (<c>u32</c>,
/// <see cref="Frame"/>). Decoding never throws for what a peer sent: an unknown type, an oversized or malformed message is reported as an error text, and the caller counts
/// and discards it.
/// </summary>
public static class MessageCodec
{
    public static byte[] Encode(NetMessage message)
    {
        var writer = new WireWriter();
        writer.U16((ushort)message.Type);
        message.Write(writer);
        var limit = LimitOf(message.Type);
        if (writer.Length > limit) throw new ProtocolException($"A {message.Type} message of {writer.Length} bytes exceeds its limit of {limit}.");
        return writer.ToArray();
    }

    /// <summary>The upper limit of a message of this type in bytes, type field included.</summary>
    public static int LimitOf(MessageType type) => type is MessageType.MatchStart or MessageType.StartRequest ? Protocol.MaxResumeBytes + Protocol.MaxMessageBytes : Protocol.MaxMessageBytes;

    public static bool TryDecode(ReadOnlySpan<byte> data, out NetMessage? message, out string error)
    {
        message = null;
        error = string.Empty;
        if (data.Length < 2)
        {
            error = "a message shorter than its type field";
            return false;
        }
        var type = (MessageType)(data[0] | (data[1] << 8));
        if (!Enum.IsDefined(type))
        {
            error = $"unknown message type {(ushort)type}";
            return false;
        }
        if (data.Length > LimitOf(type))
        {
            error = $"a {type} message of {data.Length} bytes exceeds its limit of {LimitOf(type)}";
            return false;
        }
        try
        {
            var reader = new WireReader(data[2..]);
            message = type switch
            {
                MessageType.Hello => HelloMessage.Read(ref reader),
                MessageType.Welcome => WelcomeMessage.Read(ref reader),
                MessageType.Reject => RejectMessage.Read(ref reader),
                MessageType.RoomConfig => RoomConfigMessage.Read(ref reader),
                MessageType.SlotRequest => SlotRequestMessage.Read(ref reader),
                MessageType.Ready => ReadyMessage.Read(ref reader),
                MessageType.RoomState => RoomStateMessage.Read(ref reader),
                MessageType.StartRequest => StartRequestMessage.Read(ref reader),
                MessageType.MatchStart => MatchStartMessage.Read(ref reader),
                MessageType.LoadProgress => LoadProgressMessage.Read(ref reader),
                MessageType.LoadProgressState => LoadProgressStateMessage.Read(ref reader),
                MessageType.Loaded => LoadedMessage.Read(ref reader),
                MessageType.AllLoaded => AllLoadedMessage.Read(ref reader),
                MessageType.Chat => ChatRequestMessage.Read(ref reader),
                MessageType.ChatMessage => ChatMessage.Read(ref reader),
                MessageType.Intent => IntentMessage.Read(ref reader),
                MessageType.TickRequest => TickRequestMessage.Read(ref reader),
                MessageType.TickBundle => TickBundleMessage.Read(ref reader),
                MessageType.HashReport => HashReportMessage.Read(ref reader),
                MessageType.PaceRequest => PaceRequestMessage.Read(ref reader),
                MessageType.PaceState => PaceStateMessage.Read(ref reader),
                MessageType.PlayerDropped => PlayerDroppedMessage.Read(ref reader),
                MessageType.Desync => DesyncMessage.Read(ref reader),
                MessageType.EndReport => EndReportMessage.Read(ref reader),
                MessageType.MatchEnd => MatchEndMessage.Read(ref reader),
                _ => throw new ProtocolException($"No decoder for {type}."),
            };
            reader.End();
            return true;
        }
        catch (ProtocolException exception)
        {
            message = null;
            error = $"malformed {type}: {exception.Message}";
            return false;
        }
    }

    /// <summary>The message with its length in front (<c>u32</c>, little endian): the unit of a byte-stream transport.</summary>
    public static byte[] Frame(ReadOnlySpan<byte> message)
    {
        var frame = new byte[4 + message.Length];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(frame, (uint)message.Length);
        message.CopyTo(frame.AsSpan(4));
        return frame;
    }
}

/// <summary>The format versions a build speaks; two builds that differ in one of them cannot play together, whatever their commit.</summary>
public readonly record struct FormatVersions(int CommandTable, int Replay, int WorldState, int DescriptionEncoding)
{
    public static FormatVersions Current { get; } = new(CnCNew.Simulation.Commands.CommandTypes.Version, CnCNew.Simulation.Replay.ReplayFormat.CurrentVersion,
        CnCNew.Simulation.Snapshots.WorldSnapshot.Version, MatchDescription.EncodingVersion);

    public static FormatVersions Of(HelloMessage hello) => new(hello.CommandTableVersion, hello.ReplayFormatVersion, hello.WorldStateVersion, hello.DescriptionEncoding);
}

public static class RoomSettingsExtensions
{
    /// <summary>The match description of a room: its settings, the identity and definitions its members agreed on in their handshakes, and the seeds of the start.</summary>
    public static MatchDescription ToDescription(this RoomSettings settings, string simulationId, string definitionFingerprint, uint seed, uint aiSeed)
        => new(settings.TickRate, settings.MapPath, settings.TerrainFingerprint, definitionFingerprint, seed, aiSeed, settings.MaxTicks, settings.StartMoney, settings.GraceFrames,
            settings.SciencePointIntervalFrames, settings.Slots) { SimulationId = simulationId };

    /// <summary>The room settings a description was made from (a host that already holds a description, such as a saved match).</summary>
    public static RoomSettings ToRoomSettings(this MatchDescription description, bool withSeed)
        => new(description.TickRate, description.MapPath, description.TerrainFingerprint, description.MaxTicks, description.StartMoney, description.GraceFrames,
            description.SciencePointIntervalFrames, description.Slots, withSeed, description.Seed, description.AiSeed);
}
