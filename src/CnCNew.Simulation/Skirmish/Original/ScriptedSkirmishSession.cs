using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using CnCNew.Data.World;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Skirmish.Original;

/// <summary>Which skirmish AI runs a computer slot: the simulation's own (<see cref="SkirmishAi"/>, the default everywhere) or the original one with its script library.</summary>
public enum SkirmishAiKind
{
    Builtin = 0,
    Original = 1,
}

/// <summary>One slot of a skirmish played with the original AI. <paramref name="Difficulty"/> is the slot's army (<c>SLOT_EASY_AI</c>, <c>SLOT_MED_AI</c>, <c>SLOT_BRUTAL_AI</c>); a human slot ignores it.</summary>
/// <param name="Kind"><see cref="SkirmishAiKind.Builtin"/>: the slot is played by the simulation's own <see cref="SkirmishAi"/> inside this session (for comparisons; such a session cannot be saved).</param>
public sealed record ScriptedSkirmishSlot(MatchParticipant Participant, ScriptDifficulty Difficulty = ScriptDifficulty.Normal, bool IsHuman = false, int Team = 0,
    SkirmishAiKind Kind = SkirmishAiKind.Original);

/// <summary>
/// Units: <paramref name="MaxTicks"/> is a run budget in ticks of the session's rate; <paramref name="GraceFrames"/> and <paramref name="SciencePointIntervalFrames"/> are logic frames of the
/// original (1/30 s).
/// </summary>
/// <param name="Seed">
/// The seed of the logic random stream, the one source of every random decision of the AI (which team it trains among equals, its skill set, random script timers). The same seed gives
/// the same match on every machine; a match that should be unpredictable gets a fresh seed from its host, outside the simulation.
/// </param>
/// <param name="AllMapObjects">
/// Whether every pre-placed object of the map is created, as the reference does, or only the ones a match is played with: structures (civilian and tech buildings, bunkers, walls) and
/// supply sources. The default leaves out trees, rocks, lamps, ambient sound objects and trains - several hundred objects per map whose updates cost more than the rest of the match
/// (docs/SKIRMISH-AI-ORIGINAL.md, measurement) and that no script of the library refers to.
/// </param>
/// <param name="LibraryScriptsForMissingSides">
/// Not the reference (<see cref="SkirmishScriptAttachment.Attach"/>): a computer player whose side has no template side on the map takes the library's scripts for its side instead of
/// running without an AI. Off by default: "original" stays what the reference does.
/// </param>
public sealed record ScriptedSkirmishOptions(uint Seed, long MaxTicks, long GraceFrames, int StartMoney, int SciencePointIntervalFrames, bool AllMapObjects = false,
    bool LibraryScriptsForMissingSides = false)
{
    /// <summary>The budget, start money and science stand-in of <see cref="SkirmishMatchOptions.Standard"/>, so that matches of the two AIs compare.</summary>
    public static ScriptedSkirmishOptions Standard { get; } = new(0xC0FFEE06, 108_000, 300, 10_000, 2_700);
}

/// <summary>
/// A skirmish in which the computer players are run by the original AI: the map's own objects, one player per slot with the script list and the teams of
/// <c>SkirmishScripts.scb</c> for its side and start position (<see cref="SkirmishScriptAttachment"/>), executed by the script engine together with the rewritten C++ half
/// (<c>ScriptEngine.AiPlayer.cs</c>). Underneath it is a <see cref="MissionSession"/>: scripts, then the world. Like a mission it is not a replay of commands - scripts change the world
/// directly - and its truth is its state: <see cref="Save"/> and <see cref="Restore"/> continue hash-identically, AI included.
/// <para>
/// What the session itself stands in for, as <see cref="SkirmishMatchSession"/> does: the start money of the setup screen, a science purchase point every
/// <see cref="ScriptedSkirmishOptions.SciencePointIntervalFrames"/> (there is no rank system) and the decision who has won. A computer player has the whole map explored: the reference's AI
/// places structures without asking the shroud.
/// </para>
/// </summary>
public sealed class ScriptedSkirmishSession
{
    public const string FixtureId = "skirmish-original";
    public const string Format = "cncnew-scripted-skirmish-state";
    public const int Version = 1;
    private const long VictoryCheckIntervalFrames = 15;

    private readonly ScriptedSkirmishOptions _options;
    private readonly ImmutableArray<ScriptedSkirmishSlot> _slots;
    private readonly MissionSession _mission;
    private readonly long _victoryCheckInterval;
    private readonly long _sciencePointInterval;
    private readonly List<SkirmishAi> _builtin = [];
    private long _endTick;

    private ScriptedSkirmishSession(ScriptedSkirmishOptions options, ImmutableArray<ScriptedSkirmishSlot> slots, MissionSession mission, ImmutableArray<string> notes)
    {
        _options = options;
        _slots = slots;
        _mission = mission;
        _endTick = options.MaxTicks;
        _victoryCheckInterval = mission.World.Time.OriginalFrames(VictoryCheckIntervalFrames);
        _sciencePointInterval = mission.World.Time.OriginalFrames(options.SciencePointIntervalFrames);
        Notes = notes;
    }

    public SimulationWorld World => _mission.World;
    public ScriptEngine Engine => _mission.Engine;
    public MissionSession Mission => _mission;
    public ImmutableArray<ScriptedSkirmishSlot> Slots => _slots;
    public ImmutableArray<string> Notes { get; }
    public PlayerId? Winner { get; private set; }
    public long VictoryTick { get; private set; } = -1;
    public long EndTick => _endTick;
    public bool IsOver => World.Tick >= _endTick;

    /// <summary>The player of a slot: slot <c>i</c> is player <c>i + 1</c>.</summary>
    public static PlayerId PlayerOf(int slot) => new(slot + 1);

    public static ScriptedSkirmishSession Start(ScriptedSkirmishOptions options, TemplateCatalog catalog, string fingerprint, TerrainMap terrain, MapData map, MapScriptLibrary? library,
        AiData aiData, ImmutableArray<ScriptedSkirmishSlot> slots)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(aiData);
        var starts = terrain.StartPositions;
        if (slots.Length < 2) throw new ArgumentException("A match needs at least two participants.", nameof(slots));
        if (slots.Length > starts.Length) throw new ArgumentException($"The map has {starts.Length} start positions for {slots.Length} participants.", nameof(slots));
        var attached = Attach(catalog, terrain, map, library, aiData, slots, options, out var missionOptions, out var report);
        var mission = MissionSession.Start(missionOptions, catalog, fingerprint, terrain, attached);
        var notes = ImmutableArray.CreateBuilder<string>();
        notes.AddRange(report.Notes);
        notes.AddRange(mission.Notes);
        foreach (var (template, count) in mission.SkippedObjects) notes.Add($"map object {template} x{count} skipped (unknown or not instantiable)");
        var session = new ScriptedSkirmishSession(options, slots, mission, notes.ToImmutable());
        session.PlaceStarts(catalog, starts);
        mission.Engine.StartAiPlayers(terrain.ExtentX, terrain.ExtentY);
        return session;
    }

    private static MapData Attach(TemplateCatalog catalog, TerrainMap terrain, MapData map, MapScriptLibrary? library, AiData aiData, ImmutableArray<ScriptedSkirmishSlot> slots,
        ScriptedSkirmishOptions options, out MissionOptions missionOptions, out SkirmishAttachmentReport report)
    {
        var seed = options.Seed;
        if (!options.AllMapObjects)
        {

            var kept = ImmutableArray.CreateBuilder<MapObject>();
            foreach (var placed in map.Objects)
            {
                if (placed.IsWaypoint || !catalog.TryGet(placed.Name, out var template) || template.IsKindOf("STRUCTURE") || template.IsKindOf("SUPPLY_SOURCE")) kept.Add(placed);

            }
            map = map with { Objects = kept.ToImmutable() };

        }
        var starts = terrain.StartPositions;
        var scriptSlots = ImmutableArray.CreateBuilder<SkirmishScriptSlot>(slots.Length);
        var players = ImmutableArray.CreateBuilder<ScriptAiPlayerSetup>();
        var human = string.Empty;
        for (var index = 0; index < slots.Length; index++)
        {
            var slot = slots[index];
            var scripted = slot.Kind == SkirmishAiKind.Original;
            scriptSlots.Add(new SkirmishScriptSlot(slot.Participant.Faction.Name, slot.Participant.Side, index, slot.IsHuman, slot.Team, scripted));
            if (slot.IsHuman) { if (human.Length == 0) human = SkirmishScriptAttachment.PlayerName(index); }
            else if (scripted) players.Add(new ScriptAiPlayerSetup(PlayerOf(index), slot.Difficulty, slot.Participant.Side, index, starts[index].X, starts[index].Y));
        }
        var attached = SkirmishScriptAttachment.Attach(map, library, scriptSlots.MoveToImmutable(), faction => catalog.Factions.TryGet(faction, out var info) ? info.Side : null, out report,
            options.LibraryScriptsForMissingSides);
        missionOptions = new MissionOptions(seed, ScriptDifficulty.Normal, HumanSideName: human) { Ai = new ScriptAiSetup(aiData, players.ToImmutable()) };
        return attached;
    }

    /// <summary>Start money, the map explored for the computer players, and every player's start units in a ring around its start position (the AI places its own command centre).</summary>
    private void PlaceStarts(TemplateCatalog catalog, ImmutableArray<TerrainWaypoint> starts)
    {
        for (var index = 0; index < _slots.Length; index++)
        {
            var player = PlayerOf(index);
            var participant = _slots[index].Participant;
            World.SetMoney(player, Fixed.FromInt(_options.StartMoney));
            if (_slots[index].IsHuman) continue;
            var (bx, by) = (starts[index].X, starts[index].Y);
            if (_slots[index].Kind == SkirmishAiKind.Builtin)
            {
                // The simulation's own AI starts as in SkirmishMatchSession: with the base building standing, and it sees only what its units see.
                if (catalog.TryGet(participant.BaseBuilding, out var own) && own.IsInstantiable) World.RequestSpawn(own.Index, player, bx, by, Fixed.Zero, Fixed.Zero);
                var hints = ImmutableArray.CreateBuilder<(Fixed X, Fixed Y)>();
                for (var other = 0; other < _slots.Length; other++)
                    if (other != index) hints.Add((starts[other].X, starts[other].Y));
                _builtin.Add(new SkirmishAi(World, catalog, new SkirmishAiOptions(player, unchecked(_options.Seed + (uint)index * 7919u), bx, by, participant.Side,
                    SkirmishMatchSession.FamilyPrefix(participant.Faction), hints.ToImmutable()), World.Submit));
            }
            else World.ExploreMap(player);
            var ring = (catalog.TryGet(participant.BaseBuilding, out var building) ? building.BoundingCircleRadius : Fixed.Zero) + Fixed.FromInt(30);
            var units = participant.Faction.StartingUnits;
            for (var unit = 0; unit < units.Length; unit++)
            {
                if (!catalog.TryGet(units[unit], out var template) || !template.IsInstantiable) continue;
                var angle = FixedTrig.TwoPi * unit / Fixed.FromInt(Math.Max(1, units.Length));
                var id = World.RequestSpawn(template.Index, player, bx + FixedTrig.Cos(angle) * ring, by + FixedTrig.Sin(angle) * ring, Fixed.Zero, angle);
                Engine.AssignToTeam("team" + SkirmishScriptAttachment.PlayerName(index), id);
            }
        }
        World.MaterializePendingSpawns();
    }

    /// <summary>Advances one logic tick: the science stand-in, scripts and AI, the world, and twice a second the question whether the match is decided.</summary>
    public TickResult Step()
    {
        var tick = World.Tick;
        if (VictoryTick < 0 && _sciencePointInterval > 0 && tick > 0 && tick % _sciencePointInterval == 0)
            for (var index = 0; index < _slots.Length; index++)
                if (!World.IsPlayerDefeated(PlayerOf(index))) World.AddSciencePoints(PlayerOf(index), 1);
        if (VictoryTick < 0)
            foreach (var ai in _builtin) ai.Update();
        var result = _mission.Step();
        if (VictoryTick < 0 && tick > 0 && tick % _victoryCheckInterval == 0 && TryGetWinner() is { } winner)
        {
            Winner = winner;
            VictoryTick = tick;
            _endTick = Math.Min(_options.MaxTicks, tick + TickOrder.Next + World.Time.OriginalFrames(_options.GraceFrames));
        }
        return result;
    }

    /// <summary><c>VictoryConditions::update</c> over the slots: decided once every slot player that still has objects is an ally of the first such player (the map's civilians do not count).</summary>
    private PlayerId? TryGetWinner()
    {
        PlayerId? first = null;
        for (var index = 0; index < _slots.Length; index++)
        {
            var player = PlayerOf(index);
            if (World.IsPlayerDefeated(player)) continue;
            if (first is null) first = player;
            else if (!World.AreAllies(first.Value, player)) return null;
        }
        return first;
    }

    public ulong StateHash()
    {
        var hash = State.StateHash.Mix(State.StateHash.Seed, _mission.StateHash());
        hash = State.StateHash.Mix(hash, unchecked((ulong)VictoryTick));
        return State.StateHash.Mix(hash, unchecked((ulong)(Winner?.Value ?? -1)));
    }

    // ---------------------------------------------------------------------------------------------------------- state

    /// <summary>The whole session as one canonical document: the match setup, the session's own counters, the script engine with the AI, and the world.</summary>
    public byte[] Save()
    {
        if (_builtin.Count > 0) throw new NotSupportedException("A session with a slot played by the built-in SkirmishAi cannot be saved: that AI keeps its state outside any snapshot.");
        var world = WorldSnapshot.Write(World);
        using var stream = new MemoryStream();
        using (var json = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            var writer = new JsonStateWriter(json);
            writer.BeginObject(null);
            writer.Text("format", Format);
            writer.Int("version", Version);
            writer.Int("tickRate", World.Rate.TicksPerSecond);
            writer.Text("programFingerprint", _mission.Program.Fingerprint);
            writer.Text("definitionFingerprint", World.Configuration.DefinitionFingerprint);
            writer.Text("terrainFingerprint", World.Configuration.TerrainFingerprint);
            writer.BeginObject("options");
            writer.UInt("seed", _options.Seed);
            writer.Int("maxTicks", _options.MaxTicks);
            writer.Int("graceFrames", _options.GraceFrames);
            writer.Int("startMoney", _options.StartMoney);
            writer.Int("sciencePointIntervalFrames", _options.SciencePointIntervalFrames);
            writer.Bool("allMapObjects", _options.AllMapObjects);
            if (_options.LibraryScriptsForMissingSides) writer.Bool("libraryScriptsForMissingSides", true);
            writer.EndObject();
            writer.BeginArray("slots");
            foreach (var slot in _slots)
            {
                writer.BeginObject(null);
                writer.Text("faction", slot.Participant.Faction.Name);
                writer.Text("side", slot.Participant.Side);
                writer.Text("baseBuilding", slot.Participant.BaseBuilding);
                writer.Int("difficulty", (int)slot.Difficulty);
                writer.Bool("human", slot.IsHuman);
                writer.Int("team", slot.Team);
                writer.EndObject();
            }
            writer.EndArray();
            writer.Int("winner", Winner?.Value ?? -1);
            writer.Int("victoryTick", VictoryTick);
            writer.Int("endTick", _endTick);
            writer.Text("stateHash", StateHash().ToString("X16", System.Globalization.CultureInfo.InvariantCulture));
            writer.BeginObject("session");
            _mission.DescribeSession(writer);
            writer.EndObject();
            writer.BeginObject("script");
            _mission.Engine.Describe(writer);
            writer.EndObject();
            json.WritePropertyName("world");
            json.WriteRawValue(world, skipInputValidation: true);
            writer.EndObject();
        }
        return stream.ToArray();
    }

    /// <summary>Rebuilds a session from <see cref="Save"/>; map, script library and AI data must be the ones the match was started with (the fingerprints are checked).</summary>
    public static ScriptedSkirmishSession Restore(ReadOnlySpan<byte> snapshot, TemplateCatalog catalog, string expectedDefinitionFingerprint, TerrainMap terrain, MapData map,
        MapScriptLibrary? library, AiData aiData)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(terrain);
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(aiData);
        try
        {
            using var document = JsonDocument.Parse(snapshot.ToArray());
            var root = new JsonStateReader(document.RootElement);
            if (root.Text("format") != Format) throw new SnapshotFormatException($"Not a {Format} document.");
            if (root.Int("version") != Version) throw new SnapshotFormatException($"Unsupported scripted skirmish state version {root.Int("version")} (expected {Version}).");
            WorldSnapshot.ReadRate(root, catalog);
            if (root.Text("definitionFingerprint") != expectedDefinitionFingerprint)
                throw new SnapshotFormatException("The skirmish state was written for different definitions (fingerprint mismatch).");
            var optionsReader = root.Object("options");
            var options = new ScriptedSkirmishOptions(checked((uint)optionsReader.UInt("seed")), optionsReader.Int("maxTicks"), optionsReader.Int("graceFrames"),
                checked((int)optionsReader.Int("startMoney")), checked((int)optionsReader.Int("sciencePointIntervalFrames")), optionsReader.Bool("allMapObjects"),
                optionsReader.Has("libraryScriptsForMissingSides") && optionsReader.Bool("libraryScriptsForMissingSides"));
            var slots = ImmutableArray.CreateBuilder<ScriptedSkirmishSlot>();
            foreach (var item in root.Array("slots"))
            {
                if (!catalog.Factions.TryGet(item.Text("faction"), out var faction)) throw new SnapshotFormatException($"The skirmish state names the unknown faction '{item.Text("faction")}'.");
                var difficulty = checked((int)item.Int("difficulty"));
                if (!Enum.IsDefined((ScriptDifficulty)difficulty)) throw new SnapshotFormatException("Unknown difficulty in skirmish state.");
                slots.Add(new ScriptedSkirmishSlot(new MatchParticipant(faction, item.Text("side"), item.Text("baseBuilding"), null), (ScriptDifficulty)difficulty, item.Bool("human"), checked((int)item.Int("team"))));
            }
            var attached = Attach(catalog, terrain, map, library, aiData, slots.ToImmutable(), options, out var missionOptions, out var report);
            var program = ScriptProgram.Build(attached).WithHumanSide(missionOptions.HumanSideName, missionOptions.HumanFaction);
            if (root.Text("programFingerprint") != program.Fingerprint)
                throw new SnapshotFormatException("The skirmish state belongs to a different script program (map, script library or slots differ).");
            var worldBytes = Encoding.UTF8.GetBytes(document.RootElement.GetProperty("world").GetRawText());
            var world = WorldSnapshot.Restore(worldBytes, catalog, expectedDefinitionFingerprint, terrain);
            var mission = new MissionSession(missionOptions, world, program, attached, ImmutableArray<string>.Empty);
            mission.LoadSession(root.Object("session"));
            mission.Engine.Load(root.Object("script"));
            var session = new ScriptedSkirmishSession(options, slots.ToImmutable(), mission, report.Notes)
            {
                Winner = root.Int("winner") is var winner and >= 0 ? new PlayerId(checked((int)winner)) : null,
                VictoryTick = root.Int("victoryTick"),
            };
            session._endTick = root.Int("endTick");
            var recorded = root.Text("stateHash");
            var actual = session.StateHash().ToString("X16", System.Globalization.CultureInfo.InvariantCulture);
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
}
