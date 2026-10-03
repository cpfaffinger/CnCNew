using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
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
    ImmutableArray<PlayerConfiguration> Players, long EndTick, int CheckpointInterval, string TerrainFingerprint = "");

/// <summary>State hash after executing <see cref="Tick"/>.</summary>
public readonly record struct ReplayCheckpoint(long Tick, ulong Hash);

public sealed record ReplayFile(ReplayHeader Header, ImmutableArray<SimCommand> Commands, ImmutableArray<ReplayCheckpoint> Checkpoints);

/// <summary>
/// Binary replay container, little endian:
/// <c>"CNCRPLAY"</c>, u32 format version, u32 command table version, u32 tick rate, 32 bytes definition fingerprint, string fixture id, string terrain fingerprint (empty = none),
/// u32 seed, u32 world id, u16 player count (i32 id, string name, string faction; empty = none, i32 team, u8 computer flag), i64 end tick, u32 checkpoint interval,
/// u32 command count (i64 tick, u16 type, i32 player, u64 sequence, type-specific payload), u32 checkpoint count (i64 tick, u64 hash),
/// and a trailing SHA-256 over everything before it. Strings are u16-length-prefixed UTF-8.
/// Commands are stored ordered by (tick, player, sequence). Truncated, corrupted (hash mismatch) and version-foreign files are rejected.
/// </summary>
public static class ReplayFormat
{
    public const int CurrentVersion = 8;
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
            WriteString(writer, header.TerrainFingerprint);
            writer.Write(header.Seed);
            writer.Write(header.WorldId.Value);
            writer.Write(checked((ushort)header.Players.Length));
            foreach (var player in header.Players)
            {
                writer.Write(player.Id.Value);
                WriteString(writer, player.Name);
                WriteString(writer, player.Faction ?? string.Empty);
                writer.Write(player.Team);
                writer.Write(player.IsComputer);
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
            var terrainFingerprint = ReadString(reader);
            var seed = reader.ReadUInt32();
            var worldId = new WorldId(reader.ReadUInt32());
            var playerCount = reader.ReadUInt16();
            var players = ImmutableArray.CreateBuilder<PlayerConfiguration>();
            for (var index = 0; index < playerCount; index++)
            {
                var playerId = new PlayerId(reader.ReadInt32());
                var playerName = ReadString(reader);
                var faction = ReadString(reader);
                var team = reader.ReadInt32();
                var isComputer = reader.ReadBoolean();
                players.Add(new PlayerConfiguration(playerId, playerName, faction.Length == 0 ? null : faction, team, isComputer));
            }
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
            return new ReplayFile(new ReplayHeader(formatVersion, commandTableVersion, tickRate, fingerprint, fixture, seed, worldId, players.ToImmutable(), endTick, interval, terrainFingerprint),
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
            case GiveMoneyCommand giveMoney:
                writer.Write(giveMoney.Target.Value);
                writer.Write(giveMoney.Amount.Raw);
                break;
            case PurchaseUpgradeCommand upgrade:
                WriteString(writer, upgrade.Upgrade);
                writer.Write(upgrade.Target?.Value ?? 0u);
                break;
            case QueueProductionCommand queue:
                writer.Write(queue.Producer.Value);
                WriteString(writer, queue.Template);
                break;
            case CancelProductionCommand cancelProduction:
                writer.Write(cancelProduction.Producer.Value);
                writer.Write(cancelProduction.Slot);
                break;
            case SetMoveTargetCommand move:
                writer.Write(move.Target.Value);
                writer.Write(move.X.Raw);
                writer.Write(move.Y.Raw);
                break;
            case FireWeaponCommand fire:
                writer.Write(fire.Source.Value);
                writer.Write(fire.Target.Value);
                writer.Write((sbyte)fire.Slot);
                writer.Write((byte)fire.Origin);
                break;
            case SetWeaponLockCommand weaponLock:
                writer.Write(weaponLock.Source.Value);
                writer.Write((sbyte)weaponLock.Slot);
                writer.Write((byte)weaponLock.Lock);
                break;
            case GiveSciencePointsCommand givePoints:
                writer.Write(givePoints.Target.Value);
                writer.Write(givePoints.Amount);
                break;
            case PurchaseScienceCommand science:
                WriteString(writer, science.Science);
                break;
            case EnterContainerCommand enter:
                writer.Write(enter.Actor.Value);
                writer.Write(enter.Container.Value);
                break;
            case ExitContainerCommand exit:
                writer.Write(exit.Actor.Value);
                break;
            case UseSpecialPowerCommand power:
                writer.Write(power.Source.Value);
                WriteString(writer, power.SpecialPower);
                writer.Write((byte)power.TargetKind);
                writer.Write(power.Target.Value);
                writer.Write(power.X.Raw);
                writer.Write(power.Y.Raw);
                break;
            case DockCommand dock:
                writer.Write(dock.Actor.Value);
                writer.Write(dock.Dock.Value);
                break;
            case SetRelationshipCommand relation:
                writer.Write(relation.From.Value);
                writer.Write(relation.To.Value);
                writer.Write((int)relation.Relationship);
                break;
            case ConstructCommand construct:
                writer.Write(construct.Builder.Value);
                WriteString(writer, construct.Template);
                writer.Write(construct.X.Raw);
                writer.Write(construct.Y.Raw);
                writer.Write(construct.Angle.Raw);
                break;
            case SellCommand sell:
                writer.Write(sell.Target.Value);
                break;
            case SetRallyPointCommand rally:
                writer.Write(rally.Producer.Value);
                writer.Write(rally.X.Raw);
                writer.Write(rally.Y.Raw);
                break;
            case ResumeConstructCommand resume:
                writer.Write(resume.Builder.Value);
                writer.Write(resume.Site.Value);
                break;
            case CancelConstructCommand cancelConstruct:
                writer.Write(cancelConstruct.Site.Value);
                break;
            case RepairCommand repair:
                writer.Write(repair.Builder.Value);
                writer.Write(repair.Target.Value);
                break;
            case MoveAndEnterCommand moveAndEnter:
                writer.Write(moveAndEnter.Actor.Value);
                writer.Write(moveAndEnter.Container.Value);
                break;
            case EvacuateCommand evacuate:
                writer.Write(evacuate.Container.Value);
                writer.Write(evacuate.HasPosition);
                writer.Write(evacuate.X.Raw);
                writer.Write(evacuate.Y.Raw);
                break;
            case OverchargeCommand overcharge:
                writer.Write(overcharge.Target.Value);
                break;
            case CaptureCommand capture:
                writer.Write(capture.Actor.Value);
                writer.Write(capture.Target.Value);
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
            CommandTypes.GiveMoney => new GiveMoneyCommand(player, sequence, tick, new PlayerId(reader.ReadInt32()), Fixed.FromRaw(reader.ReadInt64())),
            CommandTypes.PurchaseUpgrade => ReadPurchaseUpgrade(reader, player, sequence, tick),
            CommandTypes.QueueProduction => new QueueProductionCommand(player, sequence, tick, new EntityId(reader.ReadUInt32()), ReadString(reader)),
            CommandTypes.CancelProduction => new CancelProductionCommand(player, sequence, tick, new EntityId(reader.ReadUInt32()), reader.ReadInt32()),
            CommandTypes.SetMoveTarget => new SetMoveTargetCommand(player, sequence, tick, new EntityId(reader.ReadUInt32()), Fixed.FromRaw(reader.ReadInt64()), Fixed.FromRaw(reader.ReadInt64())),
            CommandTypes.FireWeapon => ReadFireWeapon(reader, player, sequence, tick),
            CommandTypes.SetWeaponLock => new SetWeaponLockCommand(player, sequence, tick, new EntityId(reader.ReadUInt32()), reader.ReadSByte(), (WeaponLockKind)reader.ReadByte()),
            CommandTypes.GiveSciencePoints => new GiveSciencePointsCommand(player, sequence, tick, new PlayerId(reader.ReadInt32()), reader.ReadInt32()),
            CommandTypes.PurchaseScience => new PurchaseScienceCommand(player, sequence, tick, ReadString(reader)),
            CommandTypes.EnterContainer => new EnterContainerCommand(player, sequence, tick, new EntityId(reader.ReadUInt32()), new EntityId(reader.ReadUInt32())),
            CommandTypes.ExitContainer => new ExitContainerCommand(player, sequence, tick, new EntityId(reader.ReadUInt32())),
            CommandTypes.UseSpecialPower => ReadUseSpecialPower(reader, player, sequence, tick),
            CommandTypes.Dock => new DockCommand(player, sequence, tick, new EntityId(reader.ReadUInt32()), new EntityId(reader.ReadUInt32())),
            CommandTypes.SetRelationship => new SetRelationshipCommand(player, sequence, tick, new PlayerId(reader.ReadInt32()), new PlayerId(reader.ReadInt32()), (Relationship)reader.ReadInt32()),
            CommandTypes.Construct => new ConstructCommand(player, sequence, tick, new EntityId(reader.ReadUInt32()), ReadString(reader),
                Fixed.FromRaw(reader.ReadInt64()), Fixed.FromRaw(reader.ReadInt64()), Fixed.FromRaw(reader.ReadInt64())),
            CommandTypes.Sell => new SellCommand(player, sequence, tick, new EntityId(reader.ReadUInt32())),
            CommandTypes.SetRallyPoint => new SetRallyPointCommand(player, sequence, tick, new EntityId(reader.ReadUInt32()), Fixed.FromRaw(reader.ReadInt64()), Fixed.FromRaw(reader.ReadInt64())),
            CommandTypes.ResumeConstruct => new ResumeConstructCommand(player, sequence, tick, new EntityId(reader.ReadUInt32()), new EntityId(reader.ReadUInt32())),
            CommandTypes.CancelConstruct => new CancelConstructCommand(player, sequence, tick, new EntityId(reader.ReadUInt32())),
            CommandTypes.Repair => new RepairCommand(player, sequence, tick, new EntityId(reader.ReadUInt32()), new EntityId(reader.ReadUInt32())),
            CommandTypes.MoveAndEnter => new MoveAndEnterCommand(player, sequence, tick, new EntityId(reader.ReadUInt32()), new EntityId(reader.ReadUInt32())),
            CommandTypes.Evacuate => new EvacuateCommand(player, sequence, tick, new EntityId(reader.ReadUInt32()), reader.ReadBoolean(), Fixed.FromRaw(reader.ReadInt64()), Fixed.FromRaw(reader.ReadInt64())),
            CommandTypes.Overcharge => new OverchargeCommand(player, sequence, tick, new EntityId(reader.ReadUInt32())),
            CommandTypes.Capture => new CaptureCommand(player, sequence, tick, new EntityId(reader.ReadUInt32()), new EntityId(reader.ReadUInt32())),
            _ => throw new ReplayFormatException($"Unknown command type {type}."),
        };
    }

    private static UseSpecialPowerCommand ReadUseSpecialPower(BinaryReader reader, PlayerId player, ulong sequence, long tick)
    {
        var source = new EntityId(reader.ReadUInt32());
        var power = ReadString(reader);
        var kind = (SpecialPowerTargetKind)reader.ReadByte();
        return new UseSpecialPowerCommand(player, sequence, tick, source, power, kind, new EntityId(reader.ReadUInt32()),
            Fixed.FromRaw(reader.ReadInt64()), Fixed.FromRaw(reader.ReadInt64()));
    }

    private static FireWeaponCommand ReadFireWeapon(BinaryReader reader, PlayerId player, ulong sequence, long tick)
    {
        var source = new EntityId(reader.ReadUInt32());
        var target = new EntityId(reader.ReadUInt32());
        int slot = reader.ReadSByte();
        var origin = reader.ReadByte();
        if (slot is < WeaponSlot.Best or >= WeaponSlot.Count || !Enum.IsDefined((FireOrigin)origin)) throw new ReplayFormatException($"FireWeapon command with slot {slot} and origin {origin} is invalid.");
        return new FireWeaponCommand(player, sequence, tick, source, target, slot, (FireOrigin)origin);
    }

    private static PurchaseUpgradeCommand ReadPurchaseUpgrade(BinaryReader reader, PlayerId player, ulong sequence, long tick)
    {
        var upgrade = ReadString(reader);
        var target = reader.ReadUInt32();
        return new PurchaseUpgradeCommand(player, sequence, tick, upgrade, target == 0 ? null : new EntityId(target));
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
