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
    // Protocol version 2 (phase 26).
    Ping = 26,
    Pong = 27,
    LagState = 28,
    Leave = 29,
    // Protocol version 3 (phase 27).
    DesyncDetail = 30,
    DesyncReport = 31,
    SnapshotRequest = 32,
    SnapshotOffer = 33,
    SeatState = 34,
    // Protocol version 4 (phase 28).
    PasswordChallenge = 35,
    PasswordResponse = 36,
    Kick = 37,
    // Protocol version 5 (phase 29).
    RoomListRequest = 38,
    RoomList = 39,
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
    /// <summary>The server has no room left for another room (version 2).</summary>
    ServerFull = 11,
    /// <summary>The hello names a seat that is not held for it: the token is unknown or its time is over (version 3).</summary>
    SeatNotHeld = 12,
    /// <summary>The match cannot be joined now: no member could hand over its state, and the match is too long to be played again from its start (version 3).</summary>
    CannotCatchUp = 13,
    /// <summary>The room has a password and the answer to its challenge was wrong (version 4).</summary>
    BadPassword = 14,
    /// <summary>The host of the room removed this client, and it may not come back to this room (version 4).</summary>
    Kicked = 15,
    /// <summary>The client stated a player key and did not prove it, or the server admits only clients that state one (version 4).</summary>
    BadIdentity = 16,
    /// <summary>The player key is on the server's ban list (version 4).</summary>
    Banned = 17,
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
    /// <summary>The client fell behind beyond what the server's rule allows (version 2; docs/NETWORK-PROTOCOL.md section 14).</summary>
    TooSlow = 4,
    /// <summary>The host of the room removed the member (version 4).</summary>
    Kicked = 5,
    /// <summary>The member sent more than a connection may (version 4; docs/NETWORK-PROTOCOL.md section 20.4).</summary>
    Flood = 6,
}

public enum EndReason : byte
{
    Finished = 0,
    Desync = 1,
    Abandoned = 2,
    /// <summary>The match reached a limit of the server - the size of its recording (version 4).</summary>
    ServerLimit = 3,
}

/// <summary>The limits of the protocol. A message above its limit is discarded and counted, and so is one of an unknown type.</summary>
public static class Protocol
{
    /// <summary>
    /// Version 5 (phase 29): the request for the list of a server's rooms and the list - asked on a connection before its hello.
    /// Version 4 (phase 28): the verifier of a room password in the hello, the challenge and the response for it, the kick; the drop reasons "kicked" and "flood", the
    /// end reason "server limit". Version 3 (phase 27): the rejoin token and the watch flag in the hello, the token in the welcome, the detail and the report of a desync, the request and the
    /// offer of a snapshot, the state of a held seat. Version 2 (phase 26): the room name in the hello, ping/pong, lag state, leave.
    /// </summary>
    public const ushort Version = 5;
    /// <summary>Rooms one list names at most; a server with more names the first ones in the order of their names.</summary>
    public const int MaxListedRooms = 64;
    /// <summary>A verifier, a nonce and a response of the room password, as hexadecimal text.</summary>
    public const int VerifierChars = 64;
    public const int NonceChars = 32;
    /// <summary>A public key or a signature of P-256 in hexadecimal: two numbers of 32 bytes.</summary>
    public const int KeyChars = 128;
    public const int MaxPartNameBytes = 48;
    public const int MaxHashParts = 32;
    public const int MaxRoomBytes = 64;
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
    string DefinitionFingerprint, string Name, string Room = "", ulong RejoinToken = 0, bool Watch = false, string RoomVerifier = "", string PlayerKey = "") : NetMessage
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
        writer.Text(Room, Protocol.MaxRoomBytes);
        writer.U64(RejoinToken);
        writer.Bool(Watch);
        writer.Text(RoomVerifier, Protocol.VerifierChars);
        writer.Text(PlayerKey, Protocol.KeyChars);
    }

    /// <summary>
    /// A hello of another protocol version is read as far as every version has it - the version number comes first - so that the server can say which version it speaks
    /// instead of dropping the message as malformed.
    /// </summary>
    internal static HelloMessage Read(ref WireReader reader)
    {
        var version = reader.U16();
        if (version != Protocol.Version)
        {
            reader.Raw(reader.Remaining);
            return new HelloMessage(version, string.Empty, 0, 0, 0, 0, string.Empty, string.Empty);
        }
        return new HelloMessage(version, reader.Text(Protocol.MaxIdBytes), reader.I32(), reader.I32(), reader.I32(), reader.I32(), reader.Text(Protocol.MaxIdBytes), reader.Text(Protocol.MaxNameBytes),
            reader.Text(Protocol.MaxRoomBytes), reader.U64(), reader.Bool(), RoomPassword.CheckedHex(reader.Text(Protocol.VerifierChars), Protocol.VerifierChars, allowEmpty: true),
            RoomPassword.CheckedHex(reader.Text(Protocol.KeyChars), Protocol.KeyChars, allowEmpty: true));
    }
}

/// <summary>
/// The password of a room (docs/NETWORK-PROTOCOL.md section 20.5). The password itself is never sent: whoever opens the room states a <b>verifier</b> - an HMAC of the
/// room name under the password -, the server keeps that and challenges everybody else with a nonce, and the answer is an HMAC of the nonce under the verifier.
/// </summary>
public static class RoomPassword
{
    private static readonly byte[] Label = "CnCNew room password 1\0"u8.ToArray();

    /// <summary>The verifier of a password for a room; empty for no password.</summary>
    public static string Verifier(string password, string room)
    {
        if (password.Length == 0) return string.Empty;
        var name = System.Text.Encoding.UTF8.GetBytes(room);
        byte[] labelled = [..Label, ..name];
        return Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(System.Text.Encoding.UTF8.GetBytes(password), labelled));
    }

    /// <summary>The answer to a challenge. Without a verifier (the client was given no password) the answer is one that cannot be right.</summary>
    public static string Answer(string verifier, string nonce)
        => verifier.Length != Protocol.VerifierChars ? new string('0', Protocol.VerifierChars)
            : Convert.ToHexString(System.Security.Cryptography.HMACSHA256.HashData(Convert.FromHexString(verifier), Convert.FromHexString(nonce)));

    /// <summary>Whether an answer is the one the verifier gives for the nonce; compared in constant time.</summary>
    public static bool Matches(string verifier, string nonce, string answer)
        => verifier.Length == Protocol.VerifierChars && answer.Length == Protocol.VerifierChars
           && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(Convert.FromHexString(Answer(verifier, nonce)), Convert.FromHexString(answer));

    internal static string CheckedHex(string text, int chars, bool allowEmpty = false)
    {
        if (allowEmpty && text.Length == 0) return text;
        if (text.Length != chars || text.AsSpan().ContainsAnyExcept("0123456789ABCDEF")) throw new ProtocolException($"Expected {chars} hexadecimal digits in capitals.");
        return text;
    }
}

/// <summary>
/// The key of a player (docs/NETWORK-PROTOCOL.md section 20.5): a permanent key pair a client may have, so that a server can know "the same player as before" without an
/// account - and keep a player out by it. The hello states the public half; the server challenges with a nonce, and the answer carries a signature over the nonce and
/// the room's name, so a stated key is a proven one and an answer cannot be used a second time.
/// </summary>
public static class PlayerKey
{
    private static readonly byte[] Label = "CnCNew player key 1\0"u8.ToArray();

    private static byte[] Said(string nonce, string room) => [..Label, ..Convert.FromHexString(nonce), ..System.Text.Encoding.UTF8.GetBytes(room)];

    /// <summary>The public half as the hello states it.</summary>
    public static string PublicOf(Udp.UdpIdentity key) => Convert.ToHexString(key.PublicKey);

    public static string Sign(Udp.UdpIdentity key, string nonce, string room)
    {
        Span<byte> signature = stackalloc byte[Udp.UdpIdentity.SignatureBytes];
        key.Sign(Said(nonce, room), signature);
        return Convert.ToHexString(signature);
    }

    public static bool Verify(string publicKey, string nonce, string room, string signature)
        => publicKey.Length == Protocol.KeyChars && signature.Length == Protocol.KeyChars && nonce.Length == Protocol.NonceChars
           && Udp.UdpCrypto.Verify(Convert.FromHexString(publicKey), Said(nonce, room), Convert.FromHexString(signature));

    /// <summary>What a log shows and a ban list names: SHA-256 of the public key, in hexadecimal. Empty for no key.</summary>
    public static string FingerprintOf(string publicKey) => publicKey.Length == Protocol.KeyChars ? Udp.UdpCrypto.FingerprintOf(Convert.FromHexString(publicKey)) : string.Empty;
}

/// <summary>
/// Server to a client whose hello names a room with a password or states a player key: answer this nonce (<see cref="PasswordResponseMessage"/>), then the hello counts.
/// </summary>
public sealed record PasswordChallengeMessage(string Nonce) : NetMessage
{
    public override MessageType Type => MessageType.PasswordChallenge;
    internal override void Write(WireWriter writer) => writer.Text(Nonce, Protocol.NonceChars);
    internal static PasswordChallengeMessage Read(ref WireReader reader) => new(RoomPassword.CheckedHex(reader.Text(Protocol.NonceChars), Protocol.NonceChars));
}

/// <param name="Answer">The answer of the room's password to the nonce.</param>
/// <param name="Signature">The signature of the stated player key over the nonce and the room's name; empty when the hello stated no key.</param>
public sealed record PasswordResponseMessage(string Answer, string Signature = "") : NetMessage
{
    public override MessageType Type => MessageType.PasswordResponse;

    internal override void Write(WireWriter writer)
    {
        writer.Text(Answer, Protocol.VerifierChars);
        writer.Text(Signature, Protocol.KeyChars);
    }

    internal static PasswordResponseMessage Read(ref WireReader reader)
        => new(RoomPassword.CheckedHex(reader.Text(Protocol.VerifierChars), Protocol.VerifierChars), RoomPassword.CheckedHex(reader.Text(Protocol.KeyChars), Protocol.KeyChars, allowEmpty: true));
}

/// <summary>
/// Client to server, on a connection that has not said its hello yet: which rooms are there (version 5, docs/NETWORK-PROTOCOL.md section 22.1)? Carries the protocol
/// version, so that a server of another one can say so instead of answering with something the client cannot read.
/// </summary>
public sealed record RoomListRequestMessage(ushort ProtocolVersion) : NetMessage
{
    public override MessageType Type => MessageType.RoomListRequest;
    internal override void Write(WireWriter writer) => writer.U16(ProtocolVersion);
    internal static RoomListRequestMessage Read(ref WireReader reader) => new(reader.U16());
}

/// <summary>
/// One room as a list shows it: what somebody needs to choose a room, and nothing about who is in it - no names, no addresses, no keys.
/// </summary>
/// <param name="Seats">Seats for players the room's settings have; 0 while its host has set none.</param>
public sealed record ListedRoom(string Name, RoomPhase Phase, int Members, int Players, int Seats, bool HasPassword, string MapPath, int TickRate, string SimulationId);

/// <summary>Server to client: the rooms of the server, in the order of their names (at most <see cref="Protocol.MaxListedRooms"/>), and how many there are in all.</summary>
public sealed record RoomListMessage(int TotalRooms, int MaxRooms, ImmutableArray<ListedRoom> Rooms) : NetMessage
{
    public override MessageType Type => MessageType.RoomList;

    public bool Equals(RoomListMessage? other) => other is not null && TotalRooms == other.TotalRooms && MaxRooms == other.MaxRooms && Rooms.SequenceEqual(other.Rooms);
    public override int GetHashCode() => HashCode.Combine(TotalRooms, MaxRooms, Rooms.Length);

    internal override void Write(WireWriter writer)
    {
        writer.U16(checked((ushort)TotalRooms));
        writer.U16(checked((ushort)MaxRooms));
        writer.U8(checked((byte)Rooms.Length));
        foreach (var room in Rooms)
        {
            writer.Text(room.Name, Protocol.MaxRoomBytes);
            writer.U8((byte)room.Phase);
            writer.U8(checked((byte)room.Members));
            writer.U8(checked((byte)room.Players));
            writer.U8(checked((byte)room.Seats));
            writer.Bool(room.HasPassword);
            writer.Text(room.MapPath, Protocol.MaxPathBytes);
            writer.U8(checked((byte)room.TickRate));
            writer.Text(room.SimulationId, Protocol.MaxIdBytes);
        }
    }

    internal static RoomListMessage Read(ref WireReader reader)
    {
        var total = reader.U16();
        var max = reader.U16();
        var count = reader.U8();
        if (count > Protocol.MaxListedRooms || count > total) throw new ProtocolException($"A room list names at most {Protocol.MaxListedRooms} rooms and no more than there are.");
        var rooms = ImmutableArray.CreateBuilder<ListedRoom>(count);
        for (var index = 0; index < count; index++)
        {
            var name = reader.Text(Protocol.MaxRoomBytes);
            var phase = reader.U8();
            if (phase > (byte)RoomPhase.Ended) throw new ProtocolException("A listed room is in no phase a room has.");
            var members = reader.U8();
            var players = reader.U8();
            var seats = reader.U8();
            if (members > Protocol.MaxConnections || seats > Protocol.MaxSlots || players > seats) throw new ProtocolException("A listed room has more members or players than a room can have.");
            var locked = reader.Bool();
            var map = reader.Text(Protocol.MaxPathBytes);
            var rate = reader.U8();
            if (rate is not (0 or 30 or 60 or 120)) throw new ProtocolException("A listed room states no tick rate a room has.");
            rooms.Add(new ListedRoom(name, (RoomPhase)phase, members, players, seats, locked, map, rate, reader.Text(Protocol.MaxIdBytes)));
        }
        return new RoomListMessage(total, max, rooms.MoveToImmutable());
    }
}

/// <summary>Host to server: remove this member from the room; it may not come back to it.</summary>
public sealed record KickMessage(uint ConnectionId) : NetMessage
{
    public override MessageType Type => MessageType.Kick;
    internal override void Write(WireWriter writer) => writer.U32(ConnectionId);
    internal static KickMessage Read(ref WireReader reader) => new(reader.U32());
}

/// <param name="RejoinToken">What this client presents in a later hello to take its seat again after it lost its connection (0: this server holds no seats).</param>
public sealed record WelcomeMessage(uint ConnectionId, bool IsHost, ulong RejoinToken = 0) : NetMessage
{
    public override MessageType Type => MessageType.Welcome;

    internal override void Write(WireWriter writer)
    {
        writer.U32(ConnectionId);
        writer.Bool(IsHost);
        writer.U64(RejoinToken);
    }

    internal static WelcomeMessage Read(ref WireReader reader) => new(reader.U32(), reader.Bool(), reader.U64());
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

/// <param name="AlliesOnly">In a match: only for the members whose seats are in the sender's team (a watcher's line: only for the other watchers). Before a match a room has no teams yet - the line goes to everybody.</param>
public sealed record ChatRequestMessage(string Text, bool AlliesOnly = false) : NetMessage
{
    public override MessageType Type => MessageType.Chat;

    internal override void Write(WireWriter writer)
    {
        writer.Text(Text, Protocol.MaxChatBytes);
        writer.Bool(AlliesOnly);
    }

    internal static ChatRequestMessage Read(ref WireReader reader) => new(reader.Text(Protocol.MaxChatBytes), reader.Bool());
}

/// <param name="AlliesOnly">The server sent this line only to the sender's allies.</param>
public sealed record ChatMessage(uint ConnectionId, string Text, bool AlliesOnly = false) : NetMessage
{
    public override MessageType Type => MessageType.ChatMessage;

    internal override void Write(WireWriter writer)
    {
        writer.U32(ConnectionId);
        writer.Text(Text, Protocol.MaxChatBytes);
        writer.Bool(AlliesOnly);
    }

    internal static ChatMessage Read(ref WireReader reader) => new(reader.U32(), reader.Text(Protocol.MaxChatBytes), reader.Bool());
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
        if (reason > (byte)DropReason.Flood) throw new ProtocolException($"Unknown drop reason {reason}.");
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
        if (reason > (byte)EndReason.ServerLimit) throw new ProtocolException($"Unknown end reason {reason}.");
        return new MatchEndMessage(tick, winner, (EndReason)reason);
    }
}

/// <summary>One part of a session hash: its name (<c>world.objects</c>, <c>ai.player3</c>, ...) and its value.</summary>
public readonly record struct HashPart(string Name, ulong Hash);

/// <summary>
/// Client to server, after a <see cref="DesyncMessage"/>: the session hash of that tick taken apart, as this client had it (docs/NETWORK-DIAGNOSTICS.md). A client that no
/// longer holds the tick sends no parts.
/// </summary>
public sealed record DesyncDetailMessage(long Tick, ImmutableArray<HashPart> Parts) : NetMessage
{
    public override MessageType Type => MessageType.DesyncDetail;

    internal override void Write(WireWriter writer)
    {
        writer.I64(Tick);
        writer.U8((byte)Parts.Length);
        foreach (var part in Parts)
        {
            writer.Text(part.Name, Protocol.MaxPartNameBytes);
            writer.U64(part.Hash);
        }
    }

    internal static DesyncDetailMessage Read(ref WireReader reader)
    {
        var tick = reader.I64();
        var count = reader.U8();
        if (count > Protocol.MaxHashParts) throw new ProtocolException("A desync detail names more parts than a session hash has.");
        var parts = ImmutableArray.CreateBuilder<HashPart>(count);
        for (var index = 0; index < count; index++) parts.Add(new HashPart(reader.Text(Protocol.MaxPartNameBytes), reader.U64()));
        return new DesyncDetailMessage(tick, parts.MoveToImmutable());
    }
}

/// <summary>One line of a <see cref="DesyncReportMessage"/>: a part in which the clients differ, and what one connection had there.</summary>
public readonly record struct DesyncPart(string Name, uint ConnectionId, ulong Hash);

/// <summary>Server to every client: in which parts of the session hash of that tick the clients differ, with the value of every connection that sent its detail.</summary>
public sealed record DesyncReportMessage(long Tick, ImmutableArray<DesyncPart> Parts) : NetMessage
{
    public override MessageType Type => MessageType.DesyncReport;

    internal override void Write(WireWriter writer)
    {
        writer.I64(Tick);
        writer.U16((ushort)Parts.Length);
        foreach (var part in Parts)
        {
            writer.Text(part.Name, Protocol.MaxPartNameBytes);
            writer.U32(part.ConnectionId);
            writer.U64(part.Hash);
        }
    }

    internal static DesyncReportMessage Read(ref WireReader reader)
    {
        var tick = reader.I64();
        var count = reader.U16();
        if (count > Protocol.MaxHashParts * Protocol.MaxConnections) throw new ProtocolException("A desync report lists more parts than the connections of a room can have.");
        var parts = ImmutableArray.CreateBuilder<DesyncPart>(count);
        for (var index = 0; index < count; index++) parts.Add(new DesyncPart(reader.Text(Protocol.MaxPartNameBytes), reader.U32(), reader.U64()));
        return new DesyncReportMessage(tick, parts.MoveToImmutable());
    }
}

/// <summary>Server to one client: hand over the state of your session after the tick you complete next, for somebody who joins the running match.</summary>
public sealed record SnapshotRequestMessage(uint RequestId) : NetMessage
{
    public override MessageType Type => MessageType.SnapshotRequest;
    internal override void Write(WireWriter writer) => writer.U32(RequestId);
    internal static SnapshotRequestMessage Read(ref WireReader reader) => new(reader.U32());
}

/// <summary>
/// Client to server: the state asked for - the tick the match continues at, the session hash of that state, and the state itself, which the server passes on unread. The
/// server compares the hash with what all clients reported for the tick before; the one who loads it compares again.
/// </summary>
public sealed record SnapshotOfferMessage(uint RequestId, ResumeState State) : NetMessage
{
    public override MessageType Type => MessageType.SnapshotOffer;

    internal override void Write(WireWriter writer)
    {
        writer.U32(RequestId);
        State.Write(writer);
    }

    internal static SnapshotOfferMessage Read(ref WireReader reader) => new(reader.U32(), ResumeState.Read(ref reader));
}

public enum SeatHold : byte
{
    /// <summary>The player lost its connection; its seat is kept free for <see cref="SeatStateMessage.HoldMs"/>.</summary>
    Held = 0,
    /// <summary>The player is back in its seat.</summary>
    Returned = 1,
    /// <summary>The time is over: the player leaves the match with the next bundle.</summary>
    GivenUp = 2,
}

/// <summary>Server to every client: what became of the seat of a player that lost its connection.</summary>
public sealed record SeatStateMessage(int Player, SeatHold State, int HoldMs) : NetMessage
{
    public override MessageType Type => MessageType.SeatState;

    internal override void Write(WireWriter writer)
    {
        writer.I32(Player);
        writer.U8((byte)State);
        writer.I32(HoldMs);
    }

    internal static SeatStateMessage Read(ref WireReader reader)
    {
        var player = reader.I32();
        var state = reader.U8();
        if (state > (byte)SeatHold.GivenUp) throw new ProtocolException($"Unknown seat state {state}.");
        return new SeatStateMessage(player, (SeatHold)state, reader.I32());
    }
}

/// <summary>
/// Client to server, at an interval: "what does your tick clock read?" - with the client's own clock, which the answer returns, and the last tick this client has completed,
/// from which the server knows how far behind it is (docs/NETWORK-PROTOCOL.md sections 13 and 14).
/// </summary>
public sealed record PingMessage(long ClientMicros, long CompletedTick) : NetMessage
{
    public override MessageType Type => MessageType.Ping;

    internal override void Write(WireWriter writer)
    {
        writer.I64(ClientMicros);
        writer.I64(CompletedTick);
    }

    internal static PingMessage Read(ref WireReader reader) => new(reader.I64(), reader.I64());
}

/// <summary>
/// The answer to a ping: the client's clock as it sent it, and the server's game time - ticks closed times the tick length plus what has accumulated toward the next, in
/// microseconds - with the speed it advances at, in percent (0: it stands - no match, or a pause).
/// </summary>
public sealed record PongMessage(long ClientMicros, long GameMicros, ushort SpeedPercent) : NetMessage
{
    public override MessageType Type => MessageType.Pong;

    internal override void Write(WireWriter writer)
    {
        writer.I64(ClientMicros);
        writer.I64(GameMicros);
        writer.U16(SpeedPercent);
    }

    internal static PongMessage Read(ref WireReader reader)
    {
        var message = new PongMessage(reader.I64(), reader.I64(), reader.U16());
        if (message.SpeedPercent > Protocol.MaxSpeedPercent) throw new ProtocolException($"A speed of {message.SpeedPercent} percent.");
        return message;
    }
}

/// <summary>Server to every client: a member is this far behind the server's ticks, in milliseconds of game time (0: it has caught up), and whether the game is slowed for it.</summary>
/// <param name="DropInMs">While the game is slowed for this member: how long the server will still slow it before it drops the member, in milliseconds; -1 otherwise. What a client shows as the deadline.</param>
public sealed record LagStateMessage(uint ConnectionId, int LagMs, bool Throttling, int DropInMs = -1) : NetMessage
{
    public override MessageType Type => MessageType.LagState;

    internal override void Write(WireWriter writer)
    {
        writer.U32(ConnectionId);
        writer.I32(LagMs);
        writer.Bool(Throttling);
        writer.I32(DropInMs);
    }

    internal static LagStateMessage Read(ref WireReader reader)
    {
        var message = new LagStateMessage(reader.U32(), reader.I32(), reader.Bool(), reader.I32());
        if (message.LagMs < 0) throw new ProtocolException("A negative lag.");
        if (message.DropInMs < -1) throw new ProtocolException("A negative deadline.");
        return message;
    }
}

/// <summary>Client to server: this client leaves the match by choice (the original's quit through the menu: a living ally inherits what its player owns).</summary>
public sealed record LeaveMessage : NetMessage
{
    public override MessageType Type => MessageType.Leave;
    internal override void Write(WireWriter writer) { }
    internal static LeaveMessage Read(ref WireReader reader) => new();
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
    public static int LimitOf(MessageType type)
        => type is MessageType.MatchStart or MessageType.StartRequest or MessageType.SnapshotOffer ? Protocol.MaxResumeBytes + Protocol.MaxMessageBytes : Protocol.MaxMessageBytes;

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
                MessageType.Ping => PingMessage.Read(ref reader),
                MessageType.Pong => PongMessage.Read(ref reader),
                MessageType.LagState => LagStateMessage.Read(ref reader),
                MessageType.Leave => LeaveMessage.Read(ref reader),
                MessageType.DesyncDetail => DesyncDetailMessage.Read(ref reader),
                MessageType.DesyncReport => DesyncReportMessage.Read(ref reader),
                MessageType.SnapshotRequest => SnapshotRequestMessage.Read(ref reader),
                MessageType.SnapshotOffer => SnapshotOfferMessage.Read(ref reader),
                MessageType.SeatState => SeatStateMessage.Read(ref reader),
                MessageType.PasswordChallenge => PasswordChallengeMessage.Read(ref reader),
                MessageType.PasswordResponse => PasswordResponseMessage.Read(ref reader),
                MessageType.Kick => KickMessage.Read(ref reader),
                MessageType.RoomListRequest => RoomListRequestMessage.Read(ref reader),
                MessageType.RoomList => RoomListMessage.Read(ref reader),
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
