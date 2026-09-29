using System.Collections.Immutable;
using CnCNew.Data.World;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Testing;

/// <param name="MaxTicks">Hard stop when nobody has won by then.</param>
/// <param name="GraceTicks">Ticks recorded after the winner is known.</param>
/// <param name="StartMoney">Cash every player starts with, credited by the system at tick 0; the skirmish setup screen (<c>GameInfo</c>) sets it, the faction data holds none.</param>
/// <param name="SciencePointInterval">Every this many ticks the system credits each living player one science point (0: never); stand-in for the rank system, which does not exist.</param>
public sealed record SkirmishMatchOptions(uint WorldSeed, uint AiSeed, long MaxTicks, long GraceTicks, int CheckpointInterval, int StartMoney, int SciencePointInterval)
{
    /// <summary>One hour of game time.</summary>
    public static SkirmishMatchOptions Standard { get; } = new(0xC0FFEE06, 0x5EED0006, 108_000, 300, 1_000, 10_000, 2_700);
}

/// <summary>
/// A headless match of skirmish AIs on a real or synthetic terrain. Each participant starts at a <c>Player_N_Start</c> waypoint with its
/// faction's starting building and units (<c>Player::Player()</c>), the map's supply warehouses are spawned as neutral objects, and every
/// player is then driven by its own <see cref="SkirmishAi"/> through commands only. All commands, rejected ones included, are recorded
/// as a replay that any run must reproduce hash for hash.
/// <para>
/// The system stands in for what the runtime does not model: it credits science points instead of ranks, and it spawns only
/// the map's supply warehouses (no tech buildings, props or pre-placed units of the map).
/// </para>
/// </summary>
public sealed class SkirmishMatchScenario
{
    public const string FixtureId = "skirmish-match";

    private readonly SkirmishMatchOptions _options;
    private readonly List<SimCommand> _commands = [];
    private ulong _systemSequence;

    public SkirmishMatchScenario(SkirmishMatchOptions options) => _options = options;

    public ImmutableArray<PlayerConfiguration> Players { get; private set; } = [];
    public ImmutableArray<SkirmishAi> Ais { get; private set; } = [];
    public PlayerId? Winner { get; private set; }
    public long VictoryTick { get; private set; } = -1;
    public long EndTick { get; private set; }
    public int WarehousesSpawned { get; private set; }
    public ImmutableArray<string> Notes { get; private set; } = [];
    public ImmutableArray<(string Faction, long Money, int Objects, bool Defeated)> Finals { get; private set; } = [];

    /// <summary>Family prefix of a faction's own object variants: the starting building without its trailing <c>CommandCenter</c> (<c>AirF_AmericaCommandCenter</c> gives <c>AirF_America</c>).</summary>
    public static string FamilyPrefix(FactionInfo faction)
        => faction.StartingBuilding.EndsWith("CommandCenter", StringComparison.OrdinalIgnoreCase) ? faction.StartingBuilding[..^"CommandCenter".Length] : string.Empty;

    /// <summary>Plays the match and returns the recorded replay (without checkpoints).</summary>
    public ReplayFile Record(TemplateCatalog catalog, string fingerprint, TerrainMap terrain, ImmutableArray<MapObject> mapObjects, ImmutableArray<MatchParticipant> participants)
    {
        var starts = terrain.StartPositions;
        if (participants.Length < 2) throw new ArgumentException("A match needs at least two participants.", nameof(participants));
        if (participants.Length > starts.Length) throw new ArgumentException($"The map has {starts.Length} start positions for {participants.Length} participants.", nameof(participants));
        Players = [..participants.Select((participant, index) => new PlayerConfiguration(new PlayerId(index + 1), participant.Faction.Name, participant.Faction.Name, IsComputer: true))];
        var provisional = new ReplayHeader(ReplayFormat.CurrentVersion, CommandTypes.Version, SimulationTiming.LogicFramesPerSecond, fingerprint,
            FixtureId, _options.WorldSeed, new WorldId(1), Players, _options.MaxTicks, _options.CheckpointInterval, terrain.Fingerprint);
        var world = ReplayRunner.CreateWorld(provisional, catalog, terrain);
        var notes = ImmutableArray.CreateBuilder<string>();
        SpawnStartingObjects(world, catalog, participants, starts, notes);
        SpawnWarehouses(world, catalog, mapObjects, notes);
        Notes = notes.ToImmutable();

        var ais = ImmutableArray.CreateBuilder<SkirmishAi>();
        for (var index = 0; index < participants.Length; index++)
        {
            var hints = ImmutableArray.CreateBuilder<(Fixed X, Fixed Y)>();
            for (var other = 0; other < participants.Length; other++)
                if (other != index) hints.Add((starts[other].X, starts[other].Y));
            var options = new SkirmishAiOptions(Players[index].Id, unchecked(_options.AiSeed + (uint)index * 7919u), starts[index].X, starts[index].Y,
                participants[index].Side, FamilyPrefix(participants[index].Faction), hints.ToImmutable());
            ais.Add(new SkirmishAi(world, catalog, options, command => Submit(world, command)));
        }
        Ais = ais.ToImmutable();

        var endTick = _options.MaxTicks;
        while (world.Tick < endTick)
        {
            var tick = world.Tick;
            if (VictoryTick < 0)
            {
                GrantSciencePoints(world, tick);
                foreach (var ai in Ais) ai.Update();
            }
            world.Step();
            if (VictoryTick < 0 && tick > 0 && tick % 15 == 0 && world.TryGetWinner() is { } winner)
            {
                Winner = winner;
                VictoryTick = tick;
                endTick = Math.Min(_options.MaxTicks, tick + 1 + _options.GraceTicks);
            }
        }
        EndTick = endTick;
        Finals = [..Players.Select(player => (player.Faction ?? string.Empty, (long)world.MoneyOf(player.Id).Floor(),
            world.Entities.Count(entity => entity.Owner == player.Id && !entity.EffectivelyDead), world.IsPlayerDefeated(player.Id)))];
        var header = provisional with { EndTick = endTick };
        return new ReplayFile(header, [.._commands.OrderBy(value => value.Tick).ThenBy(value => value.Player.Value).ThenBy(value => value.Sequence)], []);
    }

    private SubmitResult Submit(SimulationWorld world, SimCommand command)
    {
        _commands.Add(command);
        return world.Submit(command);
    }

    private void System(SimulationWorld world, Func<ulong, SimCommand> make)
    {
        var command = make(++_systemSequence);
        _commands.Add(command);
        world.Submit(command);
    }

    private void GrantSciencePoints(SimulationWorld world, long tick)
    {
        if (_options.SciencePointInterval <= 0 || tick == 0 || tick % _options.SciencePointInterval != 0) return;
        foreach (var player in Players)
            if (!world.IsPlayerDefeated(player.Id)) System(world, sequence => new GiveSciencePointsCommand(PlayerId.System, sequence, tick, player.Id, 1));
    }

    private void SpawnStartingObjects(SimulationWorld world, TemplateCatalog catalog, ImmutableArray<MatchParticipant> participants, ImmutableArray<TerrainWaypoint> starts, ImmutableArray<string>.Builder notes)
    {
        for (var index = 0; index < participants.Length; index++)
        {
            var player = Players[index].Id;
            var (bx, by) = (starts[index].X, starts[index].Y);
            var participant = participants[index];
            System(world, sequence => new GiveMoneyCommand(PlayerId.System, sequence, 0, player, Fixed.FromInt(_options.StartMoney)));
            if (participant.Substitution is { } substitution) notes.Add($"{participant.Faction.Name}: {substitution}; starts with {participant.BaseBuilding}");
            System(world, sequence => new SpawnCommand(PlayerId.System, sequence, 0, participant.BaseBuilding, player, bx, by, Fixed.Zero, Fixed.Zero));
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
                System(world, sequence => new SpawnCommand(PlayerId.System, sequence, 0, name, player, x, y, Fixed.Zero, angle));
            }
        }
    }

    private void SpawnWarehouses(SimulationWorld world, TemplateCatalog catalog, ImmutableArray<MapObject> mapObjects, ImmutableArray<string>.Builder notes)
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
            System(world, sequence => new SpawnCommand(PlayerId.System, sequence, 0, template.Name, PlayerId.System, x, y, z, angle));
            WarehousesSpawned++;
        }
        foreach (var (name, count) in skipped) notes.Add($"map warehouse {name} x{count} skipped (not instantiable)");
    }
}
