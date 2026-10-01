using System.Collections.Immutable;
using CnCNew.Data.World;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Skirmish;

/// <param name="Participant">Faction, side and base building.</param>
/// <param name="IsHuman">A human slot has no <see cref="SkirmishAi"/>; its commands come from <see cref="SkirmishMatchSession.Issue"/>.</param>
/// <param name="Team">Alliance number (same non-zero team: allies; 0: everybody else is an enemy).</param>
public sealed record SkirmishSlot(MatchParticipant Participant, bool IsHuman = false, int Team = 0);

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
    private const long VictoryCheckInterval = 15;

    private readonly SkirmishMatchOptions _options;
    private readonly List<SimCommand> _commands = [];
    private readonly Dictionary<int, ulong> _humanSequence = [];
    private readonly ReplayHeader _header;
    private ulong _systemSequence;
    private long _endTick;

    private SkirmishMatchSession(SkirmishMatchOptions options, ReplayHeader header, SimulationWorld world)
    {
        _options = options;
        _header = header;
        World = world;
        _endTick = options.MaxTicks;
    }

    public SimulationWorld World { get; }
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

    public static SkirmishMatchSession Start(SkirmishMatchOptions options, TemplateCatalog catalog, string fingerprint, TerrainMap terrain, ImmutableArray<MapObject> mapObjects, ImmutableArray<SkirmishSlot> slots)
    {
        var starts = terrain.StartPositions;
        if (slots.Length < 2) throw new ArgumentException("A match needs at least two participants.", nameof(slots));
        if (slots.Length > starts.Length) throw new ArgumentException($"The map has {starts.Length} start positions for {slots.Length} participants.", nameof(slots));
        var players = ImmutableArray.CreateRange(slots.Select((slot, index) =>
            new PlayerConfiguration(new PlayerId(index + 1), slot.Participant.Faction.Name, slot.Participant.Faction.Name, slot.Team, IsComputer: !slot.IsHuman)));
        var header = new ReplayHeader(ReplayFormat.CurrentVersion, CommandTypes.Version, SimulationTiming.LogicFramesPerSecond, fingerprint,
            FixtureId, options.WorldSeed, new WorldId(1), players, options.MaxTicks, options.CheckpointInterval, terrain.Fingerprint);
        var world = ReplayRunner.CreateWorld(header, catalog, terrain);
        var session = new SkirmishMatchSession(options, header, world) { Players = players };
        var participants = ImmutableArray.CreateRange(slots.Select(slot => slot.Participant));
        var notes = ImmutableArray.CreateBuilder<string>();
        session.SpawnStartingObjects(catalog, participants, starts, notes);
        session.SpawnWarehouses(catalog, mapObjects, notes);
        session.Notes = notes.ToImmutable();

        var ais = ImmutableArray.CreateBuilder<SkirmishAi?>();
        for (var index = 0; index < slots.Length; index++)
        {
            if (slots[index].IsHuman)
            {
                ais.Add(null);
                continue;
            }
            var hints = ImmutableArray.CreateBuilder<(Fixed X, Fixed Y)>();
            for (var other = 0; other < slots.Length; other++)
                if (other != index) hints.Add((starts[other].X, starts[other].Y));
            var aiOptions = new SkirmishAiOptions(players[index].Id, unchecked(options.AiSeed + (uint)index * 7919u), starts[index].X, starts[index].Y,
                participants[index].Side, FamilyPrefix(participants[index].Faction), hints.ToImmutable());
            ais.Add(new SkirmishAi(world, catalog, aiOptions, session.Submit));
        }
        session.Ais = ais.ToImmutable();
        return session;
    }

    /// <summary>Advances one logic tick: science stand-in, AI thinking, the world step and the periodic victory check.</summary>
    public TickResult Step()
    {
        var tick = World.Tick;
        if (VictoryTick < 0)
        {
            GrantSciencePoints(tick);
            foreach (var ai in Ais) ai?.Update();
        }
        var result = World.Step();
        if (VictoryTick < 0 && tick > 0 && tick % VictoryCheckInterval == 0 && World.TryGetWinner() is { } winner)
        {
            Winner = winner;
            VictoryTick = tick;
            _endTick = Math.Min(_options.MaxTicks, tick + 1 + _options.GraceTicks);
        }
        return result;
    }

    public SubmitResult Submit(SimCommand command)
    {
        _commands.Add(command);
        return World.Submit(command);
    }

    /// <summary>Issues a command of a human player at the current tick; the sequence number is assigned here (per player, strictly increasing).</summary>
    public SubmitResult Issue(PlayerId player, Func<ulong, long, SimCommand> make)
    {
        var sequence = _humanSequence.GetValueOrDefault(player.Value) + 1;
        _humanSequence[player.Value] = sequence;
        return Submit(make(sequence, World.Tick));
    }

    /// <summary>The recorded commands (unchecked, no checkpoints) as a replay ending at the current tick, or <see cref="EndTick"/> once the match is over.</summary>
    public ReplayFile ToReplay()
        => new(_header with { EndTick = IsOver ? _endTick : World.Tick },
            [.._commands.OrderBy(value => value.Tick).ThenBy(value => value.Player.Value).ThenBy(value => value.Sequence)], []);

    /// <summary>
    /// Phase 10 decision (`docs/HANDOFF-PHASE10.md` point 6, `docs/SIMULATION-CORE.md` §18): a <em>live, still in-progress</em> skirmish
    /// session cannot be saved and later resumed as a continuing live game, unlike a mission (<c>MissionSnapshot</c>). Two things that sound
    /// similar but are not what this blocks:
    /// <list type="bullet">
    /// <item>A <b>finished or partial recording</b> already saves and resumes perfectly today: <see cref="ToReplay"/> plus a mid-replay
    /// <c>WorldSnapshot</c> checkpoint (<see cref="SkirmishMatchOptions.CheckpointInterval"/>) reproduce the exact same continuation hash for
    /// hash — this is exercised by every <c>ai-match</c>/<c>reference-match</c> run's "snapshot continuation" check and needs no AI state at
    /// all, because a replay only replays the commands the AI already emitted, never the AI itself.</item>
    /// <item>What is missing is pausing a session <em>before</em> it has a full recording and resuming it later as a live session that keeps
    /// making its own new decisions. That needs <see cref="SkirmishAi"/> itself to survive the round trip: per computer player it holds
    /// substantial state outside <see cref="World"/> (enemy and warehouse memory with "seen at" timestamps, order and retry-timer tables,
    /// in-flight production tracking, rally/scout targets, bad-spot avoidance, and its own <c>SageRandom</c> stream — the full field list is
    /// in SkirmishAi.cs), none of which <c>WorldSnapshot</c> carries. Writing only the world would silently resume with every AI's memory
    /// erased: not a fresh-but-valid restart, a quietly wrong one (it would immediately "re-discover" enemies and warehouses it had already
    /// found, and forget mid-flight production and retry back-off, changing behaviour without any error).
    /// </list>
    /// A <c>SkirmishAiSnapshot</c> that closes this gap is real, bounded future work (it is a flat state dump, no new mechanics), just not
    /// done in this phase. Until it exists, this method is the one loud, documented failure point for a live-session save, rather than a
    /// save routine that silently drops the AI and ships a broken resume.
    /// </summary>
    public byte[] CreateSave() => throw new NotSupportedException(
        "A live skirmish session cannot be saved: SkirmishAi holds per-player state (enemy/warehouse memory, retry timers, in-flight " +
        "production tracking, its own random stream) that WorldSnapshot does not carry, so saving only the world would silently erase " +
        "it on load. A finished or partial match already saves and resumes exactly via ToReplay() plus a WorldSnapshot checkpoint, which " +
        "needs no AI state. See docs/SIMULATION-CORE.md §18.");

    private void System(Func<ulong, SimCommand> make)
    {
        var command = make(++_systemSequence);
        _commands.Add(command);
        World.Submit(command);
    }

    private void GrantSciencePoints(long tick)
    {
        if (_options.SciencePointInterval <= 0 || tick == 0 || tick % _options.SciencePointInterval != 0) return;
        foreach (var player in Players)
            if (!World.IsPlayerDefeated(player.Id)) System(sequence => new GiveSciencePointsCommand(PlayerId.System, sequence, tick, player.Id, 1));
    }

    private void SpawnStartingObjects(TemplateCatalog catalog, ImmutableArray<MatchParticipant> participants, ImmutableArray<TerrainWaypoint> starts, ImmutableArray<string>.Builder notes)
    {
        for (var index = 0; index < participants.Length; index++)
        {
            var player = Players[index].Id;
            var (bx, by) = (starts[index].X, starts[index].Y);
            var participant = participants[index];
            System(sequence => new GiveMoneyCommand(PlayerId.System, sequence, 0, player, Fixed.FromInt(_options.StartMoney)));
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
