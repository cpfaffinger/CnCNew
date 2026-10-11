using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using CnCNew.Data.World;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Skirmish;

/// <param name="Participant">Faction, side and base building.</param>
/// <param name="IsHuman">A human slot has no <see cref="SkirmishAi"/>; its commands come from <see cref="SkirmishMatchSession.Issue"/>.</param>
/// <param name="Composition">How a computer slot chooses its army units; null for <see cref="SkirmishAi.DefaultComposition"/>.</param>
/// <param name="Team">Alliance number (same non-zero team: allies; 0: everybody else is an enemy).</param>
public sealed record SkirmishSlot(MatchParticipant Participant, bool IsHuman = false, int Team = 0, ArmyComposition? Composition = null);

/// <summary>
/// One running skirmish: the world, the map's warehouses, the faction start objects and one <see cref="SkirmishAi"/> per computer slot.
/// <see cref="Step"/> advances exactly one logic tick; every command, rejected ones included, is recorded so that
/// <see cref="ToReplay"/> yields a replay that reproduces the match hash for hash. The headless scenario and a live client drive the
/// same session, so a live game is replayable.
/// <para>
/// The system stands in for what the runtime does not model: it credits science points instead of ranks, and it spawns only
/// the map's supply warehouses (no tech buildings, props or pre-placed units of the map).
/// </para>
/// </summary>
public sealed class SkirmishMatchSession
{
    public const string FixtureId = "skirmish-match";
    /// <summary>Own rhythm of the victory check, in logic frames of the original (twice a second).</summary>
    private const long VictoryCheckIntervalFrames = 15;

    /// <summary>Format of <see cref="Save"/>: the match description, the session's counters, one state block per computer player, the recording so far and the world.</summary>
    public const string SaveFormat = "cncnew-skirmish-state";
    public const int SaveVersion = 1;

    private readonly MatchDescription _description;
    private readonly List<SimCommand> _commands = [];
    private readonly Dictionary<int, ulong> _humanSequence = [];
    private readonly ReplayHeader _header;
    // The rule values of the options are logic frames of the original; these are the same values in ticks of this session's world, resolved once.
    private readonly long _victoryCheckInterval;
    private readonly long _graceTicks;
    private readonly long _sciencePointInterval;
    private ulong _systemSequence;
    private long _endTick;

    private SkirmishMatchSession(ResolvedMatch match, ReplayHeader header, SimulationWorld world)
    {
        Match = match;
        _description = match.Description;
        _header = header;
        World = world;
        _endTick = _description.MaxTicks;
        _victoryCheckInterval = world.Time.OriginalFrames(VictoryCheckIntervalFrames);
        _graceTicks = world.Time.OriginalFrames(_description.GraceFrames);
        _sciencePointInterval = world.Time.OriginalFrames(_description.SciencePointIntervalFrames);
    }

    public SimulationWorld World { get; }

    /// <summary>What the match was started from; every machine that holds the same description builds the same session.</summary>
    public MatchDescription Description => _description;

    /// <summary>The description with everything "random" decided: who plays which faction from which start position as which player.</summary>
    public ResolvedMatch Match { get; }
    public ImmutableArray<PlayerConfiguration> Players { get; private set; } = [];

    /// <summary>One entry per slot; <see langword="null"/> for human slots.</summary>
    public ImmutableArray<SkirmishAi?> Ais { get; private set; } = [];

    public PlayerId? Winner { get; private set; }
    public long VictoryTick { get; private set; } = -1;
    public long EndTick => _endTick;
    public int WarehousesSpawned { get; private set; }
    public ImmutableArray<string> Notes { get; private set; } = [];

    /// <summary>The match is over once the winner is known and the grace period has passed, or the tick limit is reached.</summary>
    public bool IsOver => World.Tick >= _endTick;

    /// <summary>Family prefix of a faction's own object variants: the starting building without its trailing <c>CommandCenter</c> (<c>AirF_AmericaCommandCenter</c> gives <c>AirF_America</c>).</summary>
    public static string FamilyPrefix(FactionInfo faction)
        => faction.StartingBuilding.EndsWith("CommandCenter", StringComparison.OrdinalIgnoreCase) ? faction.StartingBuilding[..^"CommandCenter".Length] : string.Empty;

    /// <summary>
    /// The description of a match from the options and slots the tools and tests have always used: slot <c>i</c> is player <c>i + 1</c> at start position <c>i</c>.
    /// </summary>
    public static MatchDescription Describe(SkirmishMatchOptions options, TemplateCatalog catalog, string fingerprint, TerrainMap terrain, ImmutableArray<SkirmishSlot> slots, string mapPath = "")
        => new(catalog.Rate.TicksPerSecond, mapPath, terrain.Fingerprint, fingerprint.ToUpperInvariant(), options.WorldSeed, options.AiSeed, options.MaxTicks, options.StartMoney, options.GraceTicks,
            options.SciencePointInterval,
            [..slots.Select((slot, index) => new MatchSlotDescription(slot.IsHuman ? MatchSlotKind.Human : MatchSlotKind.Computer, slot.Participant.Faction.Name, slot.Team, StartPosition: index,
                AiComposition: slot.Composition is { } composition ? (int)composition : -1))]);

    /// <summary>
    /// Starts the match of options and slots: a shorthand that writes the <see cref="MatchDescription"/> (<see cref="Describe"/>) and starts from it. A slot must name a faction as
    /// the catalog offers it; a participant made by hand (another base building) is refused, because the description could not say it.
    /// </summary>
    public static SkirmishMatchSession Start(SkirmishMatchOptions options, TemplateCatalog catalog, string fingerprint, TerrainMap terrain, ImmutableArray<MapObject> mapObjects, ImmutableArray<SkirmishSlot> slots)
    {
        if (slots.Length < 2) throw new ArgumentException("A match needs at least two participants.", nameof(slots));
        if (slots.Length > terrain.StartPositions.Length) throw new ArgumentException($"The map has {terrain.StartPositions.Length} start positions for {slots.Length} participants.", nameof(slots));
        var session = Start(Describe(options, catalog, fingerprint, terrain, slots), catalog, terrain, mapObjects, options.CheckpointInterval, fingerprint);
        for (var index = 0; index < slots.Length; index++)
            if (session.Match.Slots[index].Participant != slots[index].Participant)
                throw new ArgumentException($"Slot {index} is not the participant the catalog offers for faction '{slots[index].Participant.Faction.Name}'.", nameof(slots));
        return session;
    }

    /// <summary>
    /// Builds tick 0 of the described match. <paramref name="mapObjects"/> are the objects of the described map (they are part of the terrain fingerprint the description names);
    /// <paramref name="checkpointInterval"/> is a property of the recording, not of the match. <paramref name="definitionFingerprint"/> is the fingerprint of the definitions
    /// the catalog was built from: a machine that was handed the description passes its own, and a description for other definitions is refused.
    /// </summary>
    public static SkirmishMatchSession Start(MatchDescription description, TemplateCatalog catalog, TerrainMap terrain, ImmutableArray<MapObject> mapObjects, int checkpointInterval = 0,
        string? definitionFingerprint = null)
    {
        ArgumentNullException.ThrowIfNull(description);
        var match = description.Resolve(catalog, terrain, definitionFingerprint);
        var session = Create(match, catalog, terrain, checkpointInterval, world: null);
        var notes = ImmutableArray.CreateBuilder<string>();
        session.SpawnStartingObjects(catalog, terrain, notes);
        session.SpawnWarehouses(catalog, mapObjects, notes);
        if (match.AiStrengthsIgnored > 0) notes.Add($"{match.AiStrengthsIgnored} computer slot(s) asked for a strength other than medium; the built-in AI has one strength");
        session.Notes = notes.ToImmutable();
        return session;
    }

    private static ReplayHeader HeaderOf(ResolvedMatch match, int checkpointInterval)
    {
        var description = match.Description;
        var players = ImmutableArray.CreateRange(match.Slots.Select(slot =>
            new PlayerConfiguration(slot.Player, slot.Participant.Faction.Name, slot.Participant.Faction.Name, slot.Description.Team, IsComputer: slot.Description.Kind == MatchSlotKind.Computer)));
        return new ReplayHeader(ReplayFormat.CurrentVersion, CommandTypes.Version, description.TickRate, description.DefinitionFingerprint,
            FixtureId, description.Seed, new WorldId(1), players, description.MaxTicks, checkpointInterval, description.TerrainFingerprint)
            { MatchDescription = Convert.ToBase64String(description.Encode()) };
    }

    /// <summary>The session without its objects: the world (a fresh one, or the restored one) and one AI per computer slot.</summary>
    private static SkirmishMatchSession Create(ResolvedMatch match, TemplateCatalog catalog, TerrainMap terrain, int checkpointInterval, SimulationWorld? world)
    {
        var description = match.Description;
        var header = HeaderOf(match, checkpointInterval);
        world ??= ReplayRunner.CreateWorld(header, catalog, terrain);
        var session = new SkirmishMatchSession(match, header, world) { Players = header.Players };
        var starts = terrain.StartPositions;
        var ais = ImmutableArray.CreateBuilder<SkirmishAi?>();
        foreach (var slot in match.Slots)
        {
            if (slot.Description.Kind != MatchSlotKind.Computer)
            {
                ais.Add(null);
                continue;
            }
            var hints = ImmutableArray.CreateBuilder<(Fixed X, Fixed Y)>();
            foreach (var other in match.Slots)
                if (other != slot) hints.Add((starts[other.StartPosition].X, starts[other.StartPosition].Y));
            var index = slot.Player.Value - 1;
            var aiOptions = new SkirmishAiOptions(slot.Player, unchecked(description.AiSeed + (uint)index * 7919u), starts[slot.StartPosition].X, starts[slot.StartPosition].Y,
                slot.Participant.Side, FamilyPrefix(slot.Participant.Faction), hints.ToImmutable(),
                Composition: slot.Description.AiComposition >= 0 ? (ArmyComposition)slot.Description.AiComposition : null);
            ais.Add(new SkirmishAi(world, catalog, aiOptions, session.Submit));
        }
        session.Ais = ais.ToImmutable();
        return session;
    }

    /// <summary>Advances one logic tick: science stand-in, AI thinking, the world step and the periodic victory check.</summary>
    public TickResult Step()
    {
        var tick = World.Tick;
        // A measuring tool's hook (not state): the session reports its own two parts around the world's phases.
        var observer = World.PhaseObserver;
        observer?.PhaseStarted(TickPhase.SkirmishAi);
        if (VictoryTick < 0)
        {
            GrantSciencePoints(tick);
            foreach (var ai in Ais) ai?.Update();
        }
        observer?.PhaseEnded(TickPhase.SkirmishAi);
        var result = World.Step();
        observer?.PhaseStarted(TickPhase.SkirmishVictory);
        if (VictoryTick < 0 && tick > 0 && tick % _victoryCheckInterval == 0 && World.TryGetWinner() is { } winner)
        {
            Winner = winner;
            VictoryTick = tick;
            _endTick = Math.Min(_description.MaxTicks, tick + TickOrder.Next + _graceTicks);
        }
        observer?.PhaseEnded(TickPhase.SkirmishVictory);
        return result;
    }

    /// <summary>
    /// Every command recorded so far in the order it was submitted (system, computer and human players; rejected ones included): the list <see cref="ToReplay"/> sorts.
    /// Read-only and append-only, so an observer (the spectator's camera of a computer player) can follow what the players order without taking part.
    /// </summary>
    public IReadOnlyList<SimCommand> RecordedCommands => _commands;

    public SubmitResult Submit(SimCommand command)
    {
        _commands.Add(command);
        // The counter of a human player follows whatever is submitted for it: an order issued here, or a recorded one fed in by a playback.
        if (IsHumanPlayer(command.Player) && command.Sequence > _humanSequence.GetValueOrDefault(command.Player.Value)) _humanSequence[command.Player.Value] = command.Sequence;
        return World.Submit(command);
    }

    private bool IsHumanPlayer(PlayerId player)
    {
        foreach (var slot in Match.Slots)
            if (slot.Player == player) return slot.Description.Kind == MatchSlotKind.Human;
        return false;
    }

    /// <summary>Issues a command of a human player at the current tick; the sequence number is assigned here (per player, strictly increasing).</summary>
    public SubmitResult Issue(PlayerId player, Func<ulong, long, SimCommand> make)
        => Submit(make(_humanSequence.GetValueOrDefault(player.Value) + 1, World.Tick));

    /// <summary>The recorded commands (unchecked, no checkpoints) as a replay ending at the current tick, or <see cref="EndTick"/> once the match is over.</summary>
    public ReplayFile ToReplay()
        => new(_header with { EndTick = IsOver ? _endTick : World.Tick },
            [.._commands.OrderBy(value => value.Tick).ThenBy(value => value.Player.Value).ThenBy(value => value.Sequence)], []);

    /// <summary>
    /// The hash of the whole session: the world, every computer player's own state (<see cref="SkirmishAi.StateHash"/>) and the session's counters. Two machines that play the
    /// same match agree in it tick by tick; a deviation in an AI shows here at once, not when the worlds have drifted apart.
    /// </summary>
    public ulong StateHash()
    {
        var hash = State.StateHash.Mix(State.StateHash.Seed, World.StateHash);
        foreach (var ai in Ais) hash = State.StateHash.Mix(hash, ai?.StateHash() ?? 0);
        hash = State.StateHash.Mix(hash, unchecked((ulong)VictoryTick));
        hash = State.StateHash.Mix(hash, unchecked((ulong)(Winner?.Value ?? -1)));
        hash = State.StateHash.Mix(hash, unchecked((ulong)_endTick));
        hash = State.StateHash.Mix(hash, _systemSequence);
        foreach (var player in Players) hash = State.StateHash.Mix(hash, _humanSequence.GetValueOrDefault(player.Id.Value));
        return hash;
    }

    /// <summary>
    /// The running match as one document (<see cref="SaveFormat"/>): <see cref="Restore"/> continues it tick for tick like the uninterrupted session, computer players included.
    /// The recording so far is part of it, so the continued match still yields its whole replay.
    /// </summary>
    public byte[] Save()
    {
        var world = WorldSnapshot.Write(World);
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            var writer = new JsonStateWriter(json);
            writer.BeginObject(null);
            writer.Text("format", SaveFormat);
            writer.Int("version", SaveVersion);
            writer.Int("tickRate", World.Rate.TicksPerSecond);
            writer.Text("definitionFingerprint", World.Configuration.DefinitionFingerprint);
            writer.Text("terrainFingerprint", World.Configuration.TerrainFingerprint);
            writer.Text("description", Convert.ToBase64String(_description.Encode()));
            writer.Text("descriptionFingerprint", _description.Fingerprint);
            writer.Int("checkpointInterval", _header.CheckpointInterval);
            writer.BeginObject("session");
            writer.Int("winner", Winner?.Value ?? -1);
            writer.Int("victoryTick", VictoryTick);
            writer.Int("endTick", _endTick);
            writer.UInt("systemSequence", _systemSequence);
            writer.Int("warehousesSpawned", WarehousesSpawned);
            writer.BeginArray("humanSequences");
            foreach (var (player, sequence) in _humanSequence.OrderBy(pair => pair.Key))
            {
                writer.BeginObject(null);
                writer.Int("player", player);
                writer.UInt("sequence", sequence);
                writer.EndObject();
            }
            writer.EndArray();
            writer.BeginArray("notes");
            foreach (var note in Notes)
            {
                writer.BeginObject(null);
                writer.Text("text", note);
                writer.EndObject();
            }
            writer.EndArray();
            writer.EndObject();
            writer.BeginArray("ais");
            foreach (var ai in Ais)
            {
                if (ai is null) continue;
                writer.BeginObject(null);
                ai.Describe(writer);
                writer.EndObject();
            }
            writer.EndArray();
            writer.Text("recording", Convert.ToBase64String(ReplayFormat.Write(ToReplay() with { Header = _header with { EndTick = World.Tick } })));
            writer.Text("stateHash", StateHash().ToString("X16", global::System.Globalization.CultureInfo.InvariantCulture));
            json.WritePropertyName("world");
            json.WriteRawValue(world, skipInputValidation: true);
            writer.EndObject();
        }
        return stream.ToArray();
    }

    /// <summary>The description a save was written for, without restoring it (to load the right map first).</summary>
    public static MatchDescription DescriptionOf(ReadOnlySpan<byte> save)
    {
        try
        {
            using var document = JsonDocument.Parse(save.ToArray());
            var root = new JsonStateReader(document.RootElement);
            if (root.Text("format") != SaveFormat) throw new SnapshotFormatException($"Not a {SaveFormat} document.");
            if (root.Int("version") != SaveVersion) throw new SnapshotFormatException($"Unsupported skirmish state version {root.Int("version")} (expected {SaveVersion}).");
            return MatchDescription.Decode(Convert.FromBase64String(root.Text("description")));
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or FormatException or KeyNotFoundException)
        {
            throw new SnapshotFormatException($"Corrupt or truncated skirmish state: {exception.Message}", exception);
        }
    }

    /// <summary>
    /// Rebuilds the session <see cref="Save"/> wrote. Definitions, tick rate and terrain must be the ones of the match (the fingerprints are checked), and the restored state must
    /// hash to the recorded session hash.
    /// </summary>
    public static SkirmishMatchSession Restore(ReadOnlySpan<byte> save, TemplateCatalog catalog, string expectedDefinitionFingerprint, TerrainMap terrain)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(terrain);
        try
        {
            using var document = JsonDocument.Parse(save.ToArray());
            var root = new JsonStateReader(document.RootElement);
            if (root.Text("format") != SaveFormat) throw new SnapshotFormatException($"Not a {SaveFormat} document.");
            if (root.Int("version") != SaveVersion) throw new SnapshotFormatException($"Unsupported skirmish state version {root.Int("version")} (expected {SaveVersion}).");
            WorldSnapshot.ReadRate(root, catalog);
            if (!string.Equals(root.Text("definitionFingerprint"), expectedDefinitionFingerprint, StringComparison.OrdinalIgnoreCase))
                throw new SnapshotFormatException("The skirmish state was written for different definitions (fingerprint mismatch).");
            var description = MatchDescription.Decode(Convert.FromBase64String(root.Text("description")));
            if (!string.Equals(description.Fingerprint, root.Text("descriptionFingerprint"), StringComparison.Ordinal))
                throw new SnapshotFormatException("The match description of the skirmish state does not match its fingerprint.");
            ResolvedMatch match;
            try
            {
                match = description.Resolve(catalog, terrain, expectedDefinitionFingerprint);
            }
            catch (ArgumentException exception)
            {
                throw new SnapshotFormatException($"The skirmish state cannot be continued here: {exception.Message}", exception);
            }
            var worldBytes = Encoding.UTF8.GetBytes(document.RootElement.GetProperty("world").GetRawText());
            var world = WorldSnapshot.Restore(worldBytes, catalog, expectedDefinitionFingerprint, terrain);
            var session = Create(match, catalog, terrain, checked((int)root.Int("checkpointInterval")), world);
            var state = root.Object("session");
            session.Winner = state.Int("winner") is var winner and >= 0 ? new PlayerId(checked((int)winner)) : null;
            session.VictoryTick = state.Int("victoryTick");
            session._endTick = state.Int("endTick");
            session._systemSequence = state.UInt("systemSequence");
            session.WarehousesSpawned = checked((int)state.Int("warehousesSpawned"));
            foreach (var item in state.Array("humanSequences")) session._humanSequence[checked((int)item.Int("player"))] = item.UInt("sequence");
            session.Notes = [..state.Array("notes").Select(item => item.Text("text"))];
            var blocks = root.Array("ais");
            var next = 0;
            foreach (var ai in session.Ais)
            {
                if (ai is null) continue;
                if (next >= blocks.Count) throw new SnapshotFormatException("The skirmish state holds fewer AI blocks than the match has computer players.");
                ai.Load(blocks[next++]);
            }
            if (next != blocks.Count) throw new SnapshotFormatException("The skirmish state holds more AI blocks than the match has computer players.");
            // The recording comes back in replay order (tick, player, sequence), which is the order the world executes in; the order of submission inside a tick is not kept.
            session._commands.AddRange(ReplayFormat.Read(Convert.FromBase64String(root.Text("recording"))).Commands);
            var recorded = root.Text("stateHash");
            var actual = session.StateHash().ToString("X16", global::System.Globalization.CultureInfo.InvariantCulture);
            if (!string.Equals(recorded, actual, StringComparison.Ordinal))
                throw new SnapshotFormatException($"Skirmish state integrity check failed: recorded hash {recorded}, restored state hashes to {actual}.");
            return session;
        }
        catch (SnapshotFormatException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or OverflowException or KeyNotFoundException or FormatException or InvalidOperationException or ArgumentException)
        {
            throw new SnapshotFormatException($"Corrupt or truncated skirmish state: {exception.Message}", exception);
        }
    }

    private void System(Func<ulong, SimCommand> make)
    {
        var command = make(++_systemSequence);
        _commands.Add(command);
        World.Submit(command);
    }

    private void GrantSciencePoints(long tick)
    {
        if (_sciencePointInterval <= 0 || tick == 0 || tick % _sciencePointInterval != 0) return;
        foreach (var player in Players)
            if (!World.IsPlayerDefeated(player.Id)) System(sequence => new GiveSciencePointsCommand(PlayerId.System, sequence, tick, player.Id, 1));
    }

    private void SpawnStartingObjects(TemplateCatalog catalog, TerrainMap terrain, ImmutableArray<string>.Builder notes)
    {
        var starts = terrain.StartPositions;
        foreach (var slot in Match.Slots)
        {
            var player = slot.Player;
            var (bx, by) = (starts[slot.StartPosition].X, starts[slot.StartPosition].Y);
            var participant = slot.Participant;
            System(sequence => new GiveMoneyCommand(PlayerId.System, sequence, 0, player, Fixed.FromInt(_description.StartMoney)));
            if (participant.Substitution is { } substitution) notes.Add($"{participant.Faction.Name}: {substitution}; starts with {participant.BaseBuilding}");
            System(sequence => new SpawnCommand(PlayerId.System, sequence, 0, participant.BaseBuilding, player, bx, by, Fixed.Zero, Fixed.Zero));
            var units = participant.Faction.StartingUnits;
            // Units start just outside the base building's footprint; inside it they would be walled in by the navigation grid.
            var ring = (catalog.TryGet(participant.BaseBuilding, out var baseTemplate) ? baseTemplate.BoundingCircleRadius : Fixed.Zero) + Fixed.FromInt(30);
            for (var unit = 0; unit < units.Length; unit++)
            {
                if (!catalog.TryGet(units[unit], out var template) || !template.IsInstantiable)
                {
                    notes.Add($"{participant.Faction.Name}: start unit {units[unit]} skipped (unknown or not instantiable)");
                    continue;
                }
                var angle = FixedTrig.TwoPi * unit / Fixed.FromInt(Math.Max(1, units.Length));
                var x = bx + FixedTrig.Cos(angle) * ring;
                var y = by + FixedTrig.Sin(angle) * ring;
                var name = units[unit];
                System(sequence => new SpawnCommand(PlayerId.System, sequence, 0, name, player, x, y, Fixed.Zero, angle));
            }
        }
    }

    private void SpawnWarehouses(TemplateCatalog catalog, ImmutableArray<MapObject> mapObjects, ImmutableArray<string>.Builder notes)
    {
        var skipped = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var mapObject in mapObjects)
        {
            if (mapObject.IsWaypoint || !catalog.TryGet(mapObject.Name, out var template) || !template.Modules.Any(module => module.Name == "SupplyWarehouseDockUpdate")) continue;
            if (!template.IsInstantiable)
            {
                skipped[template.Name] = skipped.GetValueOrDefault(template.Name) + 1;
                continue;
            }
            var (x, y, z, angle) = (Fixed.FromDecimal(mapObject.X), Fixed.FromDecimal(mapObject.Y), Fixed.FromDecimal(mapObject.Z), Fixed.FromDecimal(mapObject.Angle));
            System(sequence => new SpawnCommand(PlayerId.System, sequence, 0, template.Name, PlayerId.System, x, y, z, angle));
            WarehousesSpawned++;
        }
        foreach (var (name, count) in skipped) notes.Add($"map warehouse {name} x{count} skipped (not instantiable)");
    }
}
