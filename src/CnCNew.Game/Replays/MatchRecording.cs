using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

namespace CnCNew.Game.Replays;

/// <summary>A replay file that cannot be read, listed or played; the message is fit to show.</summary>
public sealed class ReplayRefusedException(string message, ReplayCompatibilityKind kind = ReplayCompatibilityKind.Damaged, Exception? inner = null) : Exception(message, inner)
{
    public ReplayCompatibilityKind Kind { get; } = kind;
}

/// <summary>How a recorded match came to its end.</summary>
public enum ReplayEndingKind
{
    /// <summary>By its own rules: a winner, or the time limit.</summary>
    Completed,
    /// <summary>The server reported that the games were out of sync; the replay is this game's own match up to there.</summary>
    Desync,
    /// <summary>The server ended the match for another reason than its rules (a limit, a member's drop that left nobody, its own end).</summary>
    ServerEnded,
    /// <summary>This game lost its connection to the server.</summary>
    ConnectionLost,
    /// <summary>This player left the match by choice.</summary>
    Left,
    /// <summary>The game was closed while the match ran.</summary>
    Abandoned,
}

/// <param name="Tick">The tick the recording ends at.</param>
/// <param name="Detail">What the server or the connection said, in this project's words; empty for a match that ended by its rules.</param>
public sealed record ReplayEnding(ReplayEndingKind Kind, long Tick, string Detail = "");

/// <summary>One participant of a recorded match as the list shows it.</summary>
/// <param name="Player">The player number in the match (1 to 8).</param>
/// <param name="Name">The name a person gave in a network room, else empty.</param>
/// <param name="Ai">For a computer player the AI as the tools spell it (<c>builtin/rush</c>); empty for a human.</param>
public sealed record ReplayPlayer(int Player, int Slot, string Faction, string Name, int Team, int Color, bool IsComputer, string Ai);

/// <summary>One row of the recorded result: what the score screen showed when the match ended.</summary>
public sealed record ReplayResultRow(int Player, bool IsWinner, bool IsDefeated, int UnitsBuilt, int UnitsLost, int UnitsDestroyed, int BuildingsBuilt, int BuildingsLost, long Resources);

/// <summary>
/// The header of a replay file: everything the list shows and everything the compatibility of the file is judged by, readable without playing the match. The match itself is
/// the simulation's replay (<see cref="CnCNew.Simulation.Replay.ReplayFormat"/>, unchanged) that follows the header in the file.
/// </summary>
/// <param name="Recorded">When the recording was written, as UTC ISO 8601 text; the only wall-clock value of the file, and nothing reads it but the list.</param>
/// <param name="Source"><c>local</c> (the embedded server) or <c>network</c> (a match of a server somewhere else).</param>
/// <param name="Rules">The rule profile the match was played with (<c>original</c>, <c>fixes</c>, ...); also folded into <paramref name="DefinitionFingerprint"/>.</param>
/// <param name="SimulationId">The build identity the match description states; empty when it states none.</param>
/// <param name="LocalPlayer">The player this game played, or 0 for a viewer without a seat.</param>
/// <param name="StartTick">The tick this game's recording began at: 0, or later for a match this game joined running or continued from a savegame (its hashes and tallies begin there).</param>
/// <param name="HashInterval">Every this many ticks the session hash of the recorded match is kept in <paramref name="Hashes"/>.</param>
/// <param name="Hashes">The session hash of the recorded match at ticks that are multiples of <paramref name="HashInterval"/>: what a playback is checked against.</param>
public sealed record MatchRecordingInfo(
    string Recorded, string Source, string MapPath, int TickRate, string Rules, string DefinitionFingerprint, string TerrainFingerprint, string SimulationId,
    int CommandTableVersion, int WorldStateVersion, int ReplayFormatVersion, int DescriptionEncoding, int LocalPlayer, ImmutableArray<ReplayPlayer> Players,
    ReplayEnding Ending, int Winner, long EndTick, long StartTick, long HashInterval, ImmutableArray<(long Tick, ulong Hash)> Hashes, ulong FinalSessionHash, ulong FinalWorldHash,
    ImmutableArray<ReplayResultRow> Result)
{
    /// <summary>The length of the recording in whole seconds of game time.</summary>
    public long DurationSeconds => TickRate > 0 ? EndTick / TickRate : 0;

    /// <summary>The recorded hash at <paramref name="tick"/>, if the recording kept one there.</summary>
    public ulong? HashAt(long tick)
    {
        foreach (var (at, hash) in Hashes)
            if (at == tick) return hash;
        return null;
    }
}

/// <summary>A replay file as read: its header and the bytes of the simulation's replay inside it.</summary>
public sealed record MatchRecording(MatchRecordingInfo Info, byte[] Replay);

/// <summary>
/// The replay file of the game layer (<c>.cncreplay</c>): <c>"CNCREPLY"</c>, u32 version, u32 length and the header as UTF-8 JSON with a fixed property order
/// (<see cref="MatchRecordingInfo"/>), u32 length and the simulation's replay bytes (<see cref="CnCNew.Simulation.Replay.ReplayFormat"/>, version 15, unchanged), and a SHA-256
/// over everything before it. A file with another version, a changed byte or a truncated end is refused with a message fit to show (<see cref="ReplayRefusedException"/>).
/// The header is a summary for the list; what a playback trusts is the simulation's replay inside (its own header, its match description).
/// </summary>
public static class MatchRecordingFormat
{
    public const string Format = "cncnew-replay";
    public const int Version = 1;
    public const string Extension = ".cncreplay";
    private static ReadOnlySpan<byte> Magic => "CNCREPLY"u8;
    private const int HashLength = 32;

    public static byte[] Write(MatchRecordingInfo info, ReadOnlySpan<byte> replay)
    {
        ArgumentNullException.ThrowIfNull(info);
        var header = Header(info);
        var total = Magic.Length + 4 + 4 + header.Length + 4 + replay.Length;
        var bytes = new byte[total + HashLength];
        var position = 0;
        Magic.CopyTo(bytes);
        position += Magic.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(position), Version);
        position += 4;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(position), (uint)header.Length);
        position += 4;
        header.CopyTo(bytes.AsSpan(position));
        position += header.Length;
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(position), (uint)replay.Length);
        position += 4;
        replay.CopyTo(bytes.AsSpan(position));
        SHA256.HashData(bytes.AsSpan(0, total)).CopyTo(bytes.AsSpan(total));
        return bytes;
    }

    /// <summary>Whether the bytes begin like a replay file of this game (of any version).</summary>
    public static bool LooksLikeRecording(ReadOnlySpan<byte> bytes) => bytes.Length >= Magic.Length && bytes[..Magic.Length].SequenceEqual(Magic);

    public static MatchRecording Read(ReadOnlySpan<byte> bytes)
    {
        if (!LooksLikeRecording(bytes)) throw new ReplayRefusedException("This is not a replay of this game.");
        if (bytes.Length < Magic.Length + 12 + HashLength) throw new ReplayRefusedException("The replay is damaged: the file is truncated.");
        var version = BinaryPrimitives.ReadUInt32LittleEndian(bytes[Magic.Length..]);
        if (version != Version) throw new ReplayRefusedException($"The replay file has version {version}; this game reads version {Version}.", ReplayCompatibilityKind.OtherFormat);
        var body = bytes[..^HashLength];
        if (!SHA256.HashData(body).AsSpan().SequenceEqual(bytes[^HashLength..])) throw new ReplayRefusedException("The replay is damaged: its checksum does not match.");
        var position = Magic.Length + 4;
        var headerLength = BinaryPrimitives.ReadUInt32LittleEndian(body[position..]);
        position += 4;
        if (headerLength > body.Length - position - 4) throw new ReplayRefusedException("The replay is damaged: its header is longer than the file.");
        var header = body.Slice(position, (int)headerLength);
        position += (int)headerLength;
        var replayLength = BinaryPrimitives.ReadUInt32LittleEndian(body[position..]);
        position += 4;
        if (replayLength != body.Length - position) throw new ReplayRefusedException("The replay is damaged: the match does not fill the file.");
        MatchRecordingInfo info;
        try
        {
            info = ParseHeader(header);
        }
        catch (Exception exception) when (exception is JsonException or KeyNotFoundException or FormatException or InvalidOperationException or OverflowException)
        {
            throw new ReplayRefusedException($"The replay is damaged: its header cannot be read ({exception.Message}).", ReplayCompatibilityKind.Damaged, exception);
        }
        return new MatchRecording(info, body[position..].ToArray());
    }

    private static byte[] Header(MatchRecordingInfo info)
    {
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            json.WriteStartObject();
            json.WriteString("format", Format);
            json.WriteNumber("version", Version);
            json.WriteString("recorded", info.Recorded);
            json.WriteString("source", info.Source);
            json.WriteString("mapPath", info.MapPath);
            json.WriteNumber("tickRate", info.TickRate);
            json.WriteString("rules", info.Rules);
            json.WriteString("definitionFingerprint", info.DefinitionFingerprint);
            json.WriteString("terrainFingerprint", info.TerrainFingerprint);
            json.WriteString("simulationId", info.SimulationId);
            json.WriteNumber("commandTableVersion", info.CommandTableVersion);
            json.WriteNumber("worldStateVersion", info.WorldStateVersion);
            json.WriteNumber("replayFormatVersion", info.ReplayFormatVersion);
            json.WriteNumber("descriptionEncoding", info.DescriptionEncoding);
            json.WriteNumber("localPlayer", info.LocalPlayer);
            json.WriteStartArray("players");
            foreach (var player in info.Players)
            {
                json.WriteStartObject();
                json.WriteNumber("player", player.Player);
                json.WriteNumber("slot", player.Slot);
                json.WriteString("faction", player.Faction);
                json.WriteString("name", player.Name);
                json.WriteNumber("team", player.Team);
                json.WriteNumber("color", player.Color);
                json.WriteBoolean("computer", player.IsComputer);
                json.WriteString("ai", player.Ai);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartObject("ending");
            json.WriteString("kind", KindName(info.Ending.Kind));
            json.WriteNumber("tick", info.Ending.Tick);
            json.WriteString("detail", info.Ending.Detail);
            json.WriteEndObject();
            json.WriteNumber("winner", info.Winner);
            json.WriteNumber("endTick", info.EndTick);
            json.WriteNumber("startTick", info.StartTick);
            json.WriteNumber("hashInterval", info.HashInterval);
            json.WriteStartArray("hashes");
            foreach (var (tick, hash) in info.Hashes)
            {
                json.WriteStartArray();
                json.WriteNumberValue(tick);
                json.WriteStringValue(Hex(hash));
                json.WriteEndArray();
            }
            json.WriteEndArray();
            json.WriteString("finalSessionHash", Hex(info.FinalSessionHash));
            json.WriteString("finalWorldHash", Hex(info.FinalWorldHash));
            json.WriteStartArray("result");
            foreach (var row in info.Result)
            {
                json.WriteStartObject();
                json.WriteNumber("player", row.Player);
                json.WriteBoolean("winner", row.IsWinner);
                json.WriteBoolean("defeated", row.IsDefeated);
                json.WriteNumber("unitsBuilt", row.UnitsBuilt);
                json.WriteNumber("unitsLost", row.UnitsLost);
                json.WriteNumber("unitsDestroyed", row.UnitsDestroyed);
                json.WriteNumber("buildingsBuilt", row.BuildingsBuilt);
                json.WriteNumber("buildingsLost", row.BuildingsLost);
                json.WriteNumber("resources", row.Resources);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static MatchRecordingInfo ParseHeader(ReadOnlySpan<byte> header)
    {
        using var document = JsonDocument.Parse(header.ToArray());
        var root = document.RootElement;
        if (root.GetProperty("format").GetString() != Format) throw new FormatException("the header is not that of a replay");
        var players = ImmutableArray.CreateBuilder<ReplayPlayer>();
        foreach (var player in root.GetProperty("players").EnumerateArray())
            players.Add(new ReplayPlayer(player.GetProperty("player").GetInt32(), player.GetProperty("slot").GetInt32(), Text(player, "faction"), Text(player, "name"),
                player.GetProperty("team").GetInt32(), player.GetProperty("color").GetInt32(), player.GetProperty("computer").GetBoolean(), Text(player, "ai")));
        var ending = root.GetProperty("ending");
        var hashes = ImmutableArray.CreateBuilder<(long, ulong)>();
        foreach (var entry in root.GetProperty("hashes").EnumerateArray())
            hashes.Add((entry[0].GetInt64(), ParseHex(entry[1].GetString())));
        var result = ImmutableArray.CreateBuilder<ReplayResultRow>();
        foreach (var row in root.GetProperty("result").EnumerateArray())
            result.Add(new ReplayResultRow(row.GetProperty("player").GetInt32(), row.GetProperty("winner").GetBoolean(), row.GetProperty("defeated").GetBoolean(),
                row.GetProperty("unitsBuilt").GetInt32(), row.GetProperty("unitsLost").GetInt32(), row.GetProperty("unitsDestroyed").GetInt32(), row.GetProperty("buildingsBuilt").GetInt32(),
                row.GetProperty("buildingsLost").GetInt32(), row.GetProperty("resources").GetInt64()));
        return new MatchRecordingInfo(Text(root, "recorded"), Text(root, "source"), Text(root, "mapPath"), root.GetProperty("tickRate").GetInt32(), Text(root, "rules"),
            Text(root, "definitionFingerprint"), Text(root, "terrainFingerprint"), Text(root, "simulationId"), root.GetProperty("commandTableVersion").GetInt32(),
            root.GetProperty("worldStateVersion").GetInt32(), root.GetProperty("replayFormatVersion").GetInt32(), root.GetProperty("descriptionEncoding").GetInt32(),
            root.GetProperty("localPlayer").GetInt32(), players.ToImmutable(),
            new ReplayEnding(ParseKind(Text(ending, "kind")), ending.GetProperty("tick").GetInt64(), Text(ending, "detail")),
            root.GetProperty("winner").GetInt32(), root.GetProperty("endTick").GetInt64(), root.GetProperty("startTick").GetInt64(), root.GetProperty("hashInterval").GetInt64(),
            hashes.ToImmutable(), ParseHex(Text(root, "finalSessionHash")), ParseHex(Text(root, "finalWorldHash")), result.ToImmutable());
    }

    private static string Text(JsonElement element, string name) => element.GetProperty(name).GetString() ?? throw new FormatException($"'{name}' is not a text");

    private static string Hex(ulong value) => value.ToString("X16", CultureInfo.InvariantCulture);

    private static ulong ParseHex(string? text) => ulong.Parse(text ?? throw new FormatException("a hash is missing"), NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    /// <summary>The name of an ending as the file spells it.</summary>
    public static string KindName(ReplayEndingKind kind) => kind switch
    {
        ReplayEndingKind.Completed => "completed",
        ReplayEndingKind.Desync => "desync",
        ReplayEndingKind.ServerEnded => "server-ended",
        ReplayEndingKind.ConnectionLost => "connection-lost",
        ReplayEndingKind.Left => "left",
        ReplayEndingKind.Abandoned => "abandoned",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "No name for this ending."),
    };

    private static ReplayEndingKind ParseKind(string name)
    {
        foreach (var kind in Enum.GetValues<ReplayEndingKind>())
            if (KindName(kind) == name) return kind;
        throw new FormatException($"unknown ending '{name}'");
    }
}
