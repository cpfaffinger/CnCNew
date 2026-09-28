using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Timers;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Replay;

public sealed class ReplayFormatException(string message) : FormatException(message);

/// <summary>
/// Replay header. <see cref="DefinitionFingerprint"/> is the SHA-256 (64 hex digits) of the canonical definition snapshot the
/// replay was recorded against; a replay is refused on any other definitions.
/// </summary>
public sealed record ReplayHeader(
    int FormatVersion, int CommandTableVersion, int TickRate, string DefinitionFingerprint, string FixtureId, uint Seed, WorldId WorldId,
    ImmutableArray<PlayerConfiguration> Players, long EndTick, int CheckpointInterval);

/// <summary>State hash after executing <see cref="Tick"/>.</summary>
public readonly record struct ReplayCheckpoint(long Tick, ulong Hash);

public sealed record ReplayFile(ReplayHeader Header, ImmutableArray<SimCommand> Commands, ImmutableArray<ReplayCheckpoint> Checkpoints);

/// <summary>
/// Binary replay container, little endian:
/// <c>"CNCRPLAY"</c>, u32 format version, u32 command table version, u32 tick rate, 32 bytes definition fingerprint, string fixture id,
/// u32 seed, u32 world id, u16 player count (i32 id, string name), i64 end tick, u32 checkpoint interval,
/// u32 command count (i64 tick, u16 type, i32 player, u64 sequence, type-specific payload), u32 checkpoint count (i64 tick, u64 hash),
/// and a trailing SHA-256 over everything before it. Strings are u16-length-prefixed UTF-8.
/// Commands are stored ordered by (tick, player, sequence). Truncated, corrupted (hash mismatch) and version-foreign files are rejected.
/// </summary>
public static class ReplayFormat
{
    public const int CurrentVersion = 1;
    private static readonly byte[] Magic = "CNCRPLAY"u8.ToArray();
    private const int HashLength = 32;

    public static byte[] Write(ReplayFile replay)
    {
        ArgumentNullException.ThrowIfNull(replay);
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            var header = replay.Header;
            writer.Write(Magic);
            writer.Write((uint)header.FormatVersion);
            writer.Write((uint)header.CommandTableVersion);
            writer.Write((uint)header.TickRate);
            writer.Write(FingerprintBytes(header.DefinitionFingerprint));
            WriteString(writer, header.FixtureId);
            writer.Write(header.Seed);
            writer.Write(header.WorldId.Value);
            writer.Write(checked((ushort)header.Players.Length));
            foreach (var player in header.Players)
            {
                writer.Write(player.Id.Value);
                WriteString(writer, player.Name);
            }
            writer.Write(header.EndTick);
            writer.Write((uint)header.CheckpointInterval);
            writer.Write((uint)replay.Commands.Length);
            foreach (var command in replay.Commands.OrderBy(value => value.Tick).ThenBy(value => value.Player.Value).ThenBy(value => value.Sequence))
                WriteCommand(writer, command);
            writer.Write((uint)replay.Checkpoints.Length);
            foreach (var checkpoint in replay.Checkpoints)
            {
                writer.Write(checkpoint.Tick);
                writer.Write(checkpoint.Hash);
            }
        }
        var body = stream.ToArray();
        var result = new byte[body.Length + HashLength];
        body.CopyTo(result, 0);
        SHA256.HashData(body).CopyTo(result, body.Length);
        return result;
    }

    public static ReplayFile Read(ReadOnlySpan<byte> data)
    {
        if (data.Length < Magic.Length + 4 + HashLength) throw new ReplayFormatException("The replay is truncated (shorter than the fixed header).");
        if (!data[..Magic.Length].SequenceEqual(Magic)) throw new ReplayFormatException("Not a CnCNew replay (bad magic).");
        var version = BitConverter.ToUInt32(data[Magic.Length..(Magic.Length + 4)]);
        if (version != CurrentVersion) throw new ReplayFormatException($"Unsupported replay format version {version} (expected {CurrentVersion}).");
        var body = data[..^HashLength];
        if (!SHA256.HashData(body).AsSpan().SequenceEqual(data[^HashLength..]))
            throw new ReplayFormatException("The replay is corrupt or truncated (integrity hash mismatch).");
        try
        {
            using var stream = new MemoryStream(body.ToArray());
            using var reader = new BinaryReader(stream, Encoding.UTF8);
            reader.ReadBytes(Magic.Length);
            var formatVersion = (int)reader.ReadUInt32();
            var commandTableVersion = (int)reader.ReadUInt32();
            var tickRate = (int)reader.ReadUInt32();
            var fingerprint = Convert.ToHexString(reader.ReadBytes(32));
            var fixture = ReadString(reader);
            var seed = reader.ReadUInt32();
            var worldId = new WorldId(reader.ReadUInt32());
            var playerCount = reader.ReadUInt16();
            var players = ImmutableArray.CreateBuilder<PlayerConfiguration>();
            for (var index = 0; index < playerCount; index++) players.Add(new PlayerConfiguration(new PlayerId(reader.ReadInt32()), ReadString(reader)));
            var endTick = reader.ReadInt64();
            var interval = (int)reader.ReadUInt32();
            var commandCount = reader.ReadUInt32();
            if (commandCount > stream.Length - stream.Position) throw new ReplayFormatException("Command count exceeds the file size.");
            var commands = ImmutableArray.CreateBuilder<SimCommand>((int)commandCount);
            for (var index = 0; index < commandCount; index++) commands.Add(ReadCommand(reader));
            var checkpointCount = reader.ReadUInt32();
            if (checkpointCount > (stream.Length - stream.Position) / 16) throw new ReplayFormatException("Checkpoint count exceeds the file size.");
            var checkpoints = ImmutableArray.CreateBuilder<ReplayCheckpoint>((int)checkpointCount);
            for (var index = 0; index < checkpointCount; index++) checkpoints.Add(new ReplayCheckpoint(reader.ReadInt64(), reader.ReadUInt64()));
            if (stream.Position != stream.Length) throw new ReplayFormatException("Unexpected trailing bytes in the replay.");
            var built = commands.MoveToImmutable();
            for (var index = 1; index < built.Length; index++)
            {
                var previous = built[index - 1];
                var current = built[index];
                var ordered = previous.Tick != current.Tick ? previous.Tick < current.Tick
                    : previous.Player.Value != current.Player.Value ? previous.Player.Value < current.Player.Value
                    : previous.Sequence < current.Sequence;
                if (!ordered) throw new ReplayFormatException("Commands are not ordered by (tick, player, sequence).");
            }
            return new ReplayFile(new ReplayHeader(formatVersion, commandTableVersion, tickRate, fingerprint, fixture, seed, worldId, players.ToImmutable(), endTick, interval),
                built, checkpoints.MoveToImmutable());
        }
        catch (EndOfStreamException exception)
        {
            throw new ReplayFormatException($"The replay is truncated: {exception.Message}");
        }
    }

    private static byte[] FingerprintBytes(string hex)
    {
        if (hex.Length != 64) throw new ReplayFormatException("The definition fingerprint must be 64 hex digits (SHA-256).");
        try { return Convert.FromHexString(hex); }
        catch (FormatException) { throw new ReplayFormatException("The definition fingerprint is not valid hexadecimal."); }
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(checked((ushort)bytes.Length));
        writer.Write(bytes);
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.ReadUInt16();
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        return Encoding.UTF8.GetString(bytes);
    }

    private static void WriteCommand(BinaryWriter writer, SimCommand command)
    {
        writer.Write(command.Tick);
        writer.Write(command.TypeCode);
        writer.Write(command.Player.Value);
        writer.Write(command.Sequence);
        switch (command)
        {
            case SpawnCommand spawn:
                WriteString(writer, spawn.Template);
                writer.Write(spawn.Owner.Value);
                writer.Write(spawn.X.Raw);
                writer.Write(spawn.Y.Raw);
                writer.Write(spawn.Z.Raw);
                writer.Write(spawn.Angle.Raw);
                break;
            case DestroyCommand destroy:
                writer.Write(destroy.Target.Value);
                break;
            case DamageCommand damage:
                writer.Write(damage.Target.Value);
                writer.Write(damage.Amount.Raw);
                writer.Write(damage.DamageType);
                writer.Write(damage.DeathType);
                break;
            case KillCommand kill:
                writer.Write(kill.Target.Value);
                break;
            case SetTimerCommand timer:
                writer.Write(timer.Target.Value);
                writer.Write(timer.DelayTicks);
                writer.Write(timer.RepeatTicks);
                writer.Write((byte)timer.Action);
                writer.Write(timer.Amount.Raw);
                writer.Write(timer.DamageType);
                writer.Write(timer.TimerTag);
                break;
            case CancelTimerCommand cancel:
                writer.Write(cancel.Timer.Value);
                break;
            default:
                throw new ReplayFormatException($"Command type {command.GetType().Name} has no replay encoding.");
        }
    }

    private static SimCommand ReadCommand(BinaryReader reader)
    {
        var tick = reader.ReadInt64();
        var type = reader.ReadUInt16();
        var player = new PlayerId(reader.ReadInt32());
        var sequence = reader.ReadUInt64();
        if (!CommandTypes.IsKnown(type)) throw new ReplayFormatException($"Unknown command type {type}.");
        return type switch
        {
            CommandTypes.Spawn => new SpawnCommand(player, sequence, tick, ReadString(reader), new PlayerId(reader.ReadInt32()),
                Fixed.FromRaw(reader.ReadInt64()), Fixed.FromRaw(reader.ReadInt64()), Fixed.FromRaw(reader.ReadInt64()), Fixed.FromRaw(reader.ReadInt64())),
            CommandTypes.Destroy => new DestroyCommand(player, sequence, tick, new EntityId(reader.ReadUInt32())),
            CommandTypes.Damage => new DamageCommand(player, sequence, tick, new EntityId(reader.ReadUInt32()), Fixed.FromRaw(reader.ReadInt64()), reader.ReadInt32(), reader.ReadInt32()),
            CommandTypes.Kill => new KillCommand(player, sequence, tick, new EntityId(reader.ReadUInt32())),
            CommandTypes.SetTimer => ReadSetTimer(reader, player, sequence, tick),
            CommandTypes.CancelTimer => new CancelTimerCommand(player, sequence, tick, new TimerId(reader.ReadUInt64())),
            _ => throw new ReplayFormatException($"Unknown command type {type}."),
        };
    }

    private static SetTimerCommand ReadSetTimer(BinaryReader reader, PlayerId player, ulong sequence, long tick)
    {
        var target = new EntityId(reader.ReadUInt32());
        var delay = reader.ReadInt64();
        var repeat = reader.ReadInt64();
        var action = reader.ReadByte();
        if (!Enum.IsDefined(typeof(TimerActionKind), action)) throw new ReplayFormatException($"Unknown timer action {action}.");
        return new SetTimerCommand(player, sequence, tick, target, delay, repeat, (TimerActionKind)action, Fixed.FromRaw(reader.ReadInt64()), reader.ReadInt32(), reader.ReadInt32());
    }
}
