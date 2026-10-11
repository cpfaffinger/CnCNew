using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Random;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.Skirmish;

/// <summary>What sits in a slot of a match.</summary>
public enum MatchSlotKind : byte
{
    /// <summary>Nobody; the slot takes no start position and no player number.</summary>
    Closed = 0,
    Human = 1,
    Computer = 2,
    /// <summary>Somebody who watches: no start position, no player number, no objects.</summary>
    Spectator = 3,
}

/// <summary>
/// One slot of a <see cref="MatchDescription"/>.
/// </summary>
/// <param name="Faction">Name of the faction, or empty for "random": then the seed of the description chooses one, the same on every machine.</param>
/// <param name="Team">Alliance number (same non-zero team: allies; 0: everybody else is an enemy).</param>
/// <param name="Color">Index of the player colour, or -1 for "random". Carried for the presentation; the simulation does not read it.</param>
/// <param name="StartPosition">Index of the map's start position, or -1 for "random" (the seed chooses among the free ones).</param>
/// <param name="AiStrength">Strength of a computer slot: 0 easy, 1 medium, 2 hard. The built-in AI knows one strength; a value other than 1 is counted
/// (<see cref="ResolvedMatch.AiStrengthsIgnored"/>), not obeyed.</param>
/// <param name="AiComposition">How a computer slot chooses its army units as a value of <see cref="ArmyComposition"/>, or -1 for the AI's default.</param>
public sealed record MatchSlotDescription(MatchSlotKind Kind, string Faction = "", int Team = 0, int Color = -1, int StartPosition = -1, int AiStrength = 1, int AiComposition = -1)
{
    public const int MediumStrength = 1;
    public bool TakesPart => Kind is MatchSlotKind.Human or MatchSlotKind.Computer;
}

/// <summary>A slot of the description as the session plays it: everything "random" is decided.</summary>
/// <param name="Slot">Index of the slot in the description.</param>
/// <param name="Player">The player number: the slots that take part are numbered from 1 in slot order.</param>
public sealed record ResolvedSlot(int Slot, PlayerId Player, MatchSlotDescription Description, MatchParticipant Participant, int StartPosition, int Color);

/// <param name="AiStrengthsIgnored">Computer slots that asked for a strength the built-in AI does not have.</param>
public sealed record ResolvedMatch(MatchDescription Description, ImmutableArray<ResolvedSlot> Slots, int AiStrengthsIgnored);

/// <summary>
/// Everything a machine needs to build the initial state of a match, and nothing else: an immutable value with one canonical encoding. Two machines that hold the same bytes build
/// the same tick 0 (<see cref="SkirmishMatchSession.Start"/>). Who plays locally, which window shows it and how it is recorded are not part of it.
/// <para>
/// Units: <see cref="GraceFrames"/> and <see cref="SciencePointIntervalFrames"/> are rules in game time, written in logic frames of the original (1/30 s), and mean the same at every
/// tick rate. <see cref="MaxTicks"/> is the length of the match in ticks of <see cref="TickRate"/> - the description names its rate, so the value is unambiguous; it stays a tick count
/// because the budgets of the tools and of the reference manifest are exact tick counts.
/// </para>
/// </summary>
/// <param name="MapPath">Virtual path of the map; informative next to <see cref="TerrainFingerprint"/>, which is what is checked.</param>
/// <param name="Seed">Seed of the world's random streams and of every "random" choice of the description.</param>
/// <param name="AiSeed">Seed the computer players derive their own streams from (slot <c>i</c>: <c>AiSeed + i * 7919</c>).</param>
public sealed record MatchDescription(
    int TickRate, string MapPath, string TerrainFingerprint, string DefinitionFingerprint, uint Seed, uint AiSeed, long MaxTicks, int StartMoney, long GraceFrames,
    long SciencePointIntervalFrames, ImmutableArray<MatchSlotDescription> Slots)
{
    public const int EncodingVersion = 1;
    private static ReadOnlySpan<byte> Magic => "CNCMATCH"u8;

    /// <summary>The version of the simulation the description was made for: the command table and the world state. A machine with another one must refuse the match.</summary>
    public int CommandTableVersion { get; init; } = CommandTypes.Version;
    public int WorldStateVersion { get; init; } = WorldSnapshot.Version;

    public SimulationRate Rate => SimulationRate.FromTicksPerSecond(TickRate);

    /// <summary>The slots are compared by content: two descriptions with equal slots are equal.</summary>
    public bool Equals(MatchDescription? other)
        => other is not null && Encode().AsSpan().SequenceEqual(other.Encode());

    public override int GetHashCode() => Fingerprint.GetHashCode(StringComparison.Ordinal);

    /// <summary>SHA-256 over <see cref="Encode"/>, as 64 upper-case hex digits. Every field is in it.</summary>
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(Encode()));

    /// <summary>
    /// The canonical bytes, little endian: <c>"CNCMATCH"</c>, u32 encoding version, u32 command table version, u32 world state version, u32 tick rate, string map path, string terrain
    /// fingerprint, string definition fingerprint, u32 seed, u32 AI seed, i64 max ticks, i32 start money, i64 grace frames, i64 science point interval frames, u16 slot count
    /// (u8 kind, string faction, i32 team, i32 colour, i32 start position, i32 AI strength, i32 AI composition). Strings are u16-length-prefixed UTF-8.
    /// </summary>
    public byte[] Encode()
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write((uint)EncodingVersion);
            writer.Write((uint)CommandTableVersion);
            writer.Write((uint)WorldStateVersion);
            writer.Write((uint)TickRate);
            WriteString(writer, MapPath);
            WriteString(writer, TerrainFingerprint);
            WriteString(writer, DefinitionFingerprint);
            writer.Write(Seed);
            writer.Write(AiSeed);
            writer.Write(MaxTicks);
            writer.Write(StartMoney);
            writer.Write(GraceFrames);
            writer.Write(SciencePointIntervalFrames);
            writer.Write(checked((ushort)Slots.Length));
            foreach (var slot in Slots)
            {
                writer.Write((byte)slot.Kind);
                WriteString(writer, slot.Faction);
                writer.Write(slot.Team);
                writer.Write(slot.Color);
                writer.Write(slot.StartPosition);
                writer.Write(slot.AiStrength);
                writer.Write(slot.AiComposition);
            }
        }
        return stream.ToArray();
    }

    public static MatchDescription Decode(ReadOnlySpan<byte> data)
    {
        try
        {
            var position = 0;
            if (data.Length < Magic.Length || !data[..Magic.Length].SequenceEqual(Magic)) throw new FormatException("Not a match description.");
            position += Magic.Length;
            var encoding = ReadInt32(data, ref position);
            if (encoding != EncodingVersion) throw new FormatException($"Unsupported match description version {encoding} (expected {EncodingVersion}).");
            var commandTable = ReadInt32(data, ref position);
            var worldState = ReadInt32(data, ref position);
            var tickRate = ReadInt32(data, ref position);
            var mapPath = ReadString(data, ref position);
            var terrain = ReadString(data, ref position);
            var definitions = ReadString(data, ref position);
            var seed = unchecked((uint)ReadInt32(data, ref position));
            var aiSeed = unchecked((uint)ReadInt32(data, ref position));
            var maxTicks = ReadInt64(data, ref position);
            var startMoney = ReadInt32(data, ref position);
            var grace = ReadInt64(data, ref position);
            var science = ReadInt64(data, ref position);
            var count = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(position, 2));
            position += 2;
            var slots = ImmutableArray.CreateBuilder<MatchSlotDescription>(count);
            for (var index = 0; index < count; index++)
            {
                var kind = data[position++];
                if (kind > (byte)MatchSlotKind.Spectator) throw new FormatException($"Unknown slot kind {kind}.");
                var faction = ReadString(data, ref position);
                slots.Add(new MatchSlotDescription((MatchSlotKind)kind, faction, ReadInt32(data, ref position), ReadInt32(data, ref position), ReadInt32(data, ref position),
                    ReadInt32(data, ref position), ReadInt32(data, ref position)));
            }
            if (position != data.Length) throw new FormatException("Trailing bytes after the match description.");
            return new MatchDescription(tickRate, mapPath, terrain, definitions, seed, aiSeed, maxTicks, startMoney, grace, science, slots.MoveToImmutable())
            {
                CommandTableVersion = commandTable, WorldStateVersion = worldState,
            };
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or IndexOutOfRangeException or DecoderFallbackException)
        {
            throw new FormatException("Truncated or corrupt match description.", exception);
        }
    }

    /// <summary>
    /// Decides everything the description leaves open and checks it against the definitions and the map of this machine. "Random" factions and start positions are drawn from a
    /// stream of their own that is seeded by <see cref="Seed"/> - never by the caller -, factions first, then positions, both in slot order.
    /// </summary>
    public ResolvedMatch Resolve(TemplateCatalog catalog, TerrainMap terrain)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(terrain);
        if (CommandTableVersion != CommandTypes.Version || WorldStateVersion != WorldSnapshot.Version)
            throw new ArgumentException($"The match was described for simulation version {CommandTableVersion}/{WorldStateVersion}; this build is {CommandTypes.Version}/{WorldSnapshot.Version}.");
        if (!SimulationRate.IsSupported(TickRate) || catalog.Rate.TicksPerSecond != TickRate)
            throw new ArgumentException($"The match is described at {TickRate} Hz; the catalog was built for {catalog.Rate}.");
        if (!string.Equals(TerrainFingerprint, terrain.Fingerprint, StringComparison.Ordinal))
            throw new ArgumentException("The match is described on a different terrain (fingerprint mismatch).");
        var playable = ReferenceMatchScenario.Participants(catalog, out _);
        var starts = terrain.StartPositions.Length;
        var taking = Slots.Count(slot => slot.TakesPart);
        if (taking < 2) throw new ArgumentException("A match needs at least two participants.");
        if (taking > starts) throw new ArgumentException($"The map has {starts} start positions for {taking} participants.");

        // The stream of the description's own choices: derived from the seed, apart from the world's streams.
        var random = new SageRandom(Seed ^ 0x4D415443u);
        var participants = new MatchParticipant?[Slots.Length];
        for (var index = 0; index < Slots.Length; index++)
        {
            var slot = Slots[index];
            if (!slot.TakesPart) continue;
            if (slot.Faction.Length == 0)
            {
                if (playable.IsEmpty) throw new ArgumentException("No playable faction to choose a random one from.");
                participants[index] = playable[random.Range(0, playable.Length - 1)];
                continue;
            }
            participants[index] = playable.FirstOrDefault(value => value.Faction.Name.Equals(slot.Faction, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"Unknown or unplayable faction '{slot.Faction}'.");
        }

        var positions = new int[Slots.Length];
        var taken = new bool[starts];
        for (var index = 0; index < Slots.Length; index++)
        {
            var slot = Slots[index];
            positions[index] = -1;
            if (!slot.TakesPart || slot.StartPosition < 0) continue;
            if (slot.StartPosition >= starts) throw new ArgumentException($"Slot {index} names start position {slot.StartPosition}; the map has {starts}.");
            if (taken[slot.StartPosition]) throw new ArgumentException($"Start position {slot.StartPosition} is named by two slots.");
            taken[slot.StartPosition] = true;
            positions[index] = slot.StartPosition;
        }
        for (var index = 0; index < Slots.Length; index++)
        {
            if (!Slots[index].TakesPart || positions[index] >= 0) continue;
            var free = new List<int>();
            for (var start = 0; start < starts; start++) if (!taken[start]) free.Add(start);
            var pick = free[random.Range(0, free.Count - 1)];
            taken[pick] = true;
            positions[index] = pick;
        }

        var usedColors = new SortedSet<int>(Slots.Where(slot => slot.TakesPart && slot.Color >= 0).Select(slot => slot.Color));
        var resolved = ImmutableArray.CreateBuilder<ResolvedSlot>();
        var ignored = 0;
        var player = 0;
        var nextColor = 0;
        for (var index = 0; index < Slots.Length; index++)
        {
            var slot = Slots[index];
            if (!slot.TakesPart) continue;
            if (slot.Kind == MatchSlotKind.Computer && slot.AiStrength != MatchSlotDescription.MediumStrength) ignored++;
            if (slot.AiComposition >= 0 && !Enum.IsDefined((ArmyComposition)slot.AiComposition)) throw new ArgumentException($"Slot {index} names the unknown army composition {slot.AiComposition}.");
            var color = slot.Color;
            if (color < 0)
            {
                // "Random" colour: the lowest free index. It needs no draw - nothing depends on which colour it is, only that every machine shows the same one.
                while (usedColors.Contains(nextColor)) nextColor++;
                color = nextColor;
                usedColors.Add(color);
            }
            resolved.Add(new ResolvedSlot(index, new PlayerId(++player), slot, participants[index]!, positions[index], color));
        }
        return new ResolvedMatch(this, resolved.ToImmutable(), ignored);
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(checked((ushort)bytes.Length));
        writer.Write(bytes);
    }

    private static int ReadInt32(ReadOnlySpan<byte> data, ref int position)
    {
        var value = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(position, 4));
        position += 4;
        return value;
    }

    private static long ReadInt64(ReadOnlySpan<byte> data, ref int position)
    {
        var value = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(position, 8));
        position += 8;
        return value;
    }

    private static string ReadString(ReadOnlySpan<byte> data, ref int position)
    {
        var length = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(position, 2));
        position += 2;
        var value = new UTF8Encoding(false, true).GetString(data.Slice(position, length));
        position += length;
        return value;
    }
}
