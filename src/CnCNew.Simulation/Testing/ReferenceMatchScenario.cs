using System.Collections.Immutable;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Random;
using CnCNew.Simulation.Replay;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Testing;

/// <param name="Faction">The faction record (start money, sciences, starting units).</param>
/// <param name="Side">Side of the faction; selects the production roster.</param>
/// <param name="BaseBuilding">The building the player starts with.</param>
/// <param name="Substitution">Why <paramref name="BaseBuilding"/> is not the faction's own starting building; null when it is.</param>
public sealed record MatchParticipant(FactionInfo Faction, string Side, string BaseBuilding, string? Substitution);

/// <param name="MaxTicks">Hard stop when nobody has won by then.</param>
/// <param name="GraceTicks">Ticks recorded after the winner is known.</param>
/// <param name="MinTicks">The replay is padded with idle ticks up to this length (0: none); shows the world stays stable over 216,000 ticks.</param>
/// <param name="ArenaRadius">Distance of every base from the map centre.</param>
/// <param name="Income">Money the system credits every living player every <see cref="IncomeInterval"/> ticks (stand-in for supply gathering).</param>
public sealed record ReferenceMatchOptions(uint WorldSeed, uint DriverSeed, long MaxTicks, long GraceTicks, long MinTicks, int CheckpointInterval, int ArenaRadius, int Income, int IncomeInterval)
{
    public static ReferenceMatchOptions Standard { get; } = new(0xC0FFEE05, 0x5EED0005, 216_000, 300, 0, 1_000, 1_500, 1_500, 300);
}

/// <summary>
/// A headless free-for-all between every playable faction on a synthetic, obstacle-free map that exists only as coordinates. The
/// match is scriptless: a deterministic command generator, driven only by world state and its own <see cref="SageRandom"/>,
/// plays every player and its commands are recorded as a replay that any run must reproduce hash for hash.
/// <para>
/// Setup follows <c>Player::Player()</c> and the faction's <c>PlayerTemplate</c>: start money and intrinsic sciences come from the
/// world, the starting building and starting units are spawned by the player. The generator then keeps every producer busy with
/// units of the faction's side, sends every mobile unit toward the nearest enemy object and fires every armed unit whose target is
/// in range. Documented simplifications (see <c>GAME-RULES.md</c>): no construction, supply, terrain, collision or targeting rules
/// (any weapon may hit any object with a body), the command center may produce the whole roster of its side, no teams, and the
/// system pays a fixed income instead of harvesting.
/// </para>
/// </summary>
public sealed class ReferenceMatchScenario
{
    public const string FixtureId = "reference-match";
    private const int MoveCadence = 15;
    private const int ProductionCadence = 30;
    private const int OrderRefreshTicks = 150;

    private readonly ReferenceMatchOptions _options;
    private readonly SageRandom _random;
    private readonly List<SimCommand> _commands = [];
    private readonly Dictionary<uint, (uint Target, long Tick)> _orders = [];
    private readonly Dictionary<int, UnitClass> _classes = [];
    private readonly Dictionary<string, ImmutableArray<ObjectTemplate>> _rosters = new(StringComparer.OrdinalIgnoreCase);
    private string[] _sides = [];
    private ulong[] _sequence = [];
    private int _alive;

    private readonly record struct UnitClass(bool Armed, bool Mobile, Fixed Range);

    public ReferenceMatchScenario(ReferenceMatchOptions options)
    {
        _options = options;
        _random = new SageRandom(options.DriverSeed);
    }

    public ImmutableArray<PlayerConfiguration> Players { get; private set; } = [];
    public int PeakAlive { get; private set; }
    public PlayerId? Winner { get; private set; }
    public string WinnerFaction => Winner is { } id ? Players.First(value => value.Id == id).Faction ?? string.Empty : string.Empty;
    public long VictoryTick { get; private set; } = -1;
    public int StartObjectsSkipped { get; private set; }
    public ImmutableArray<string> SkippedStartObjects { get; private set; } = [];
    public int ProductionOrders { get; private set; }
    public int Orders { get; private set; }
    public int Shots { get; private set; }

    /// <summary>
    /// Every playable faction that can field a base, in ordinal name order. A faction starts with its own <c>StartingBuilding</c> when the
    /// simulation can instantiate it; otherwise (the retail command centers need special powers, radar and production exits, which the
    /// simulation does not have yet) it starts with a stand-in: the ordinally first instantiable structure of the same side that has a
    /// <c>ProductionUpdate</c>. The substitution is recorded in <see cref="MatchParticipant.Substitution"/>, never silent. Factions
    /// without any usable producer are listed in <paramref name="excluded"/>.
    /// </summary>
    public static ImmutableArray<MatchParticipant> Participants(TemplateCatalog catalog, out ImmutableArray<string> excluded)
    {
        var playable = ImmutableArray.CreateBuilder<MatchParticipant>();
        var rejected = ImmutableArray.CreateBuilder<string>();
        foreach (var faction in catalog.Factions.Factions.Where(value => value.PlayableSide && !value.IsObserver).OrderBy(value => value.Name, StringComparer.Ordinal))
        {
            if (!catalog.TryGet(faction.StartingBuilding, out var building))
            {
                rejected.Add($"{faction.Name}: unknown starting building '{faction.StartingBuilding}'");
                continue;
            }
            if (Roster(catalog, building.Side).IsEmpty)
            {
                rejected.Add($"{faction.Name}: side '{building.Side}' has no instantiable armed unit to build");
                continue;
            }
            if (building.IsInstantiable)
            {
                playable.Add(new MatchParticipant(faction, building.Side, building.Name, null));
                continue;
            }
            var standIn = catalog.Templates.Where(template => template.IsInstantiable && template.IsKindOf("STRUCTURE")
                    && string.Equals(template.Side, building.Side, StringComparison.OrdinalIgnoreCase)
                    && template.Modules.Any(module => module.Name == "ProductionUpdate"))
                .OrderBy(template => template.Name, StringComparer.Ordinal).FirstOrDefault();
            if (standIn is null)
                rejected.Add($"{faction.Name}: '{building.Name}' is not instantiable (missing: {string.Join(", ", building.MissingModules)}) and side '{building.Side}' has no instantiable producer");
            else
                playable.Add(new MatchParticipant(faction, building.Side, standIn.Name, $"'{building.Name}' is not instantiable (missing: {string.Join(", ", building.MissingModules)})"));
        }
        excluded = rejected.ToImmutable();
        return playable.ToImmutable();
    }

    /// <summary>Plays the match with the generator attached and returns the recorded replay (without checkpoints).</summary>
    public ReplayFile Record(TemplateCatalog catalog, string fingerprint)
    {
        var factions = Participants(catalog, out _);
        if (factions.Length < 2) throw new InvalidOperationException("A match needs at least two playable factions.");
        Players = [..factions.Select((participant, index) => new PlayerConfiguration(new PlayerId(index + 1), participant.Faction.Name, participant.Faction.Name))];
        _sequence = new ulong[Players.Length + 1];
        _sides = [..factions.Select(participant => participant.Side)];
        var provisional = new ReplayHeader(ReplayFormat.CurrentVersion, CommandTypes.Version, SimulationTiming.LogicFramesPerSecond, fingerprint,
            FixtureId, _options.WorldSeed, new WorldId(1), Players, _options.MaxTicks, _options.CheckpointInterval);
        var world = ReplayRunner.CreateWorld(provisional, catalog);
        SpawnStartingObjects(world, catalog, factions);

        var endTick = Math.Max(_options.MaxTicks, _options.MinTicks);
        while (world.Tick < endTick)
        {
            var tick = world.Tick;
            if (VictoryTick < 0 && tick < _options.MaxTicks) Generate(world, catalog, tick);
            var result = world.Step();
            Observe(result.Events);
            PeakAlive = Math.Max(PeakAlive, _alive);
            if (VictoryTick < 0 && tick > 0 && tick % MoveCadence == 0 && world.TryGetWinner() is { } winner)
            {
                Winner = winner;
                VictoryTick = tick;
                endTick = Math.Max(tick + 1 + _options.GraceTicks, _options.MinTicks);
            }
        }
        var header = provisional with { EndTick = endTick };
        return new ReplayFile(header, [.._commands.OrderBy(value => value.Tick).ThenBy(value => value.Player.Value).ThenBy(value => value.Sequence)], []);
    }

    private void Observe(IReadOnlyList<SimEvent> events)
    {
        foreach (var simEvent in events)
        {
            if (simEvent.Kind == SimEventKind.EntitySpawned) _alive++;
            else if (simEvent.Kind == SimEventKind.EntityDestroyed)
            {
                _alive--;
                _orders.Remove(simEvent.Entity.Value);
            }
        }
    }

    private void Add(SimulationWorld world, SimCommand command)
    {
        _commands.Add(command);
        world.Submit(command);
    }

    private ulong Next(PlayerId player) => ++_sequence[player.Value];

    private void SpawnStartingObjects(SimulationWorld world, TemplateCatalog catalog, ImmutableArray<MatchParticipant> factions)
    {
        var skipped = ImmutableArray.CreateBuilder<string>();
        for (var index = 0; index < factions.Length; index++)
        {
            var player = Players[index].Id;
            var (bx, by) = BasePosition(index, factions.Length);
            Add(world, new SpawnCommand(player, Next(player), 0, factions[index].BaseBuilding, player, bx, by, Fixed.Zero, Fixed.Zero));
            var units = factions[index].Faction.StartingUnits;
            for (var unit = 0; unit < units.Length; unit++)
            {
                if (!catalog.TryGet(units[unit], out var template) || !template.IsInstantiable)
                {
                    skipped.Add($"{factions[index].Faction.Name}: {units[unit]}");
                    continue;
                }
                var angle = FixedTrig.TwoPi * unit / Fixed.FromInt(Math.Max(1, units.Length));
                var x = bx + FixedTrig.Cos(angle) * Fixed.FromInt(45);
                var y = by + FixedTrig.Sin(angle) * Fixed.FromInt(45);
                Add(world, new SpawnCommand(player, Next(player), 0, units[unit], player, x, y, Fixed.Zero, angle));
            }
        }
        SkippedStartObjects = skipped.ToImmutable();
        StartObjectsSkipped = SkippedStartObjects.Length;
    }

    private (Fixed X, Fixed Y) BasePosition(int index, int count)
    {
        var angle = FixedTrig.TwoPi * index / Fixed.FromInt(count);
        var radius = Fixed.FromInt(_options.ArenaRadius);
        return (FixedTrig.Cos(angle) * radius, FixedTrig.Sin(angle) * radius);
    }

    private UnitClass Classify(SimulationWorld world, Entity entity)
    {
        if (_classes.TryGetValue(entity.Template.Index, out var known)) return known;
        var weapon = entity.Template.DefaultWeapon;
        var classified = new UnitClass(weapon >= 0, entity.Modules.Any(module => module is AIUpdateInterface), weapon >= 0 ? world.Weapons[weapon].AttackRange : Fixed.Zero);
        _classes[entity.Template.Index] = classified;
        return classified;
    }

    private ImmutableArray<ObjectTemplate> RosterOf(TemplateCatalog catalog, string side)
    {
        if (_rosters.TryGetValue(side, out var roster)) return roster;
        roster = Roster(catalog, side);
        _rosters[side] = roster;
        return roster;
    }

    private static bool IsRosterCandidate(ObjectTemplate template)
        => template.BuildCost > Fixed.Zero && template.BuildTicks > 0 && template.DefaultWeapon >= 0
            && (template.IsKindOf("INFANTRY") || template.IsKindOf("VEHICLE")) && !template.IsKindOf("STRUCTURE");

    /// <summary>The instantiable, buildable, armed infantry and vehicles of one side, in ordinal name order.</summary>
    public static ImmutableArray<ObjectTemplate> Roster(TemplateCatalog catalog, string side)
        => [..catalog.Templates.Where(template => template.IsInstantiable && IsRosterCandidate(template) && string.Equals(template.Side, side, StringComparison.OrdinalIgnoreCase))
            .OrderBy(template => template.Name, StringComparer.Ordinal)];

    /// <summary>One line per side: how many of its match units and producers the simulation can instantiate today.</summary>
    public static ImmutableArray<string> DescribeCoverage(TemplateCatalog catalog)
    {
        var lines = ImmutableArray.CreateBuilder<string>();
        foreach (var side in catalog.Templates.Select(template => template.Side).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.Ordinal))
        {
            var units = catalog.Templates.Where(template => IsRosterCandidate(template) && string.Equals(template.Side, side, StringComparison.OrdinalIgnoreCase)).ToArray();
            var producers = catalog.Templates.Where(template => template.IsKindOf("STRUCTURE") && template.Modules.Any(module => module.Name == "ProductionUpdate")
                && string.Equals(template.Side, side, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (units.Length == 0 && producers.Length == 0) continue;
            lines.Add($"side {side}: {units.Count(value => value.IsInstantiable)}/{units.Length} armed units and {producers.Count(value => value.IsInstantiable)}/{producers.Length} producer structures instantiable");
        }
        return lines.ToImmutable();
    }

    /// <summary>Per side: the modules that keep match units and producer structures from being instantiable, with the number of objects each one blocks.</summary>
    public static ImmutableArray<string> DescribeBlockers(TemplateCatalog catalog)
    {
        var lines = ImmutableArray.CreateBuilder<string>();
        foreach (var side in catalog.Factions.Factions.Where(value => value.PlayableSide && !value.IsObserver)
                     .Select(value => catalog.TryGet(value.StartingBuilding, out var building) ? building.Side : null).OfType<string>()
                     .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.Ordinal))
        {
            var units = catalog.Templates.Where(template => !template.IsInstantiable && IsRosterCandidate(template) && string.Equals(template.Side, side, StringComparison.OrdinalIgnoreCase)).ToArray();
            var producers = catalog.Templates.Where(template => !template.IsInstantiable && template.IsKindOf("STRUCTURE") && template.Modules.Any(module => module.Name == "ProductionUpdate")
                && string.Equals(template.Side, side, StringComparison.OrdinalIgnoreCase)).ToArray();
            lines.Add($"side {side} units: {Tally(units)}");
            lines.Add($"side {side} producers: {Tally(producers)}");
        }
        return lines.ToImmutable();

        static string Tally(ObjectTemplate[] templates)
            => templates.Length == 0 ? "-" : string.Join(", ", templates.SelectMany(value => value.MissingModules).GroupBy(value => value, StringComparer.Ordinal)
                .OrderByDescending(group => group.Count()).ThenBy(group => group.Key, StringComparer.Ordinal).Select(group => $"{group.Key} {group.Count()}/{templates.Length}"));
    }

    private void Generate(SimulationWorld world, TemplateCatalog catalog, long tick)
    {
        if (tick == 0) return;
        if (tick % _options.IncomeInterval == 0)
            for (var index = 0; index < Players.Length; index++)
                if (!world.IsPlayerDefeated(Players[index].Id))
                    Add(world, new GiveMoneyCommand(PlayerId.System, Next(PlayerId.System), tick, Players[index].Id, Fixed.FromInt(_options.Income)));
        var productionTick = tick % ProductionCadence == 0;
        var moveTick = tick % MoveCadence == 0;
        if (!productionTick && !moveTick) return;

        var objects = world.Entities.OrderBy(value => value.Id.Value).ToArray();
        if (productionTick) QueueProduction(world, catalog, objects, tick);
        if (moveTick) IssueOrders(world, objects, tick);
    }

    private void QueueProduction(SimulationWorld world, TemplateCatalog catalog, Entity[] objects, long tick)
    {
        var budget = new Fixed[Players.Length + 1];
        for (var index = 0; index < Players.Length; index++) budget[Players[index].Id.Value] = world.MoneyOf(Players[index].Id);
        foreach (var producer in objects)
        {
            if (producer.Owner.IsSystem || producer.DestroyRequested || producer.EffectivelyDead) continue;
            var production = producer.Modules.OfType<ProductionUpdate>().FirstOrDefault();
            if (production is null || !production.HasRoom) continue;
            var roster = RosterOf(catalog, _sides[producer.Owner.Value - 1]);
            if (roster.IsEmpty) continue;
            for (var attempt = 0; attempt < 6; attempt++)
            {
                var pick = roster[_random.Range(0, roster.Length - 1)];
                if (pick.BuildCost > budget[producer.Owner.Value]) continue;
                budget[producer.Owner.Value] -= pick.BuildCost;
                Add(world, new QueueProductionCommand(producer.Owner, Next(producer.Owner), tick, producer.Id, pick.Name));
                ProductionOrders++;
                break;
            }
        }
    }

    private void IssueOrders(SimulationWorld world, Entity[] objects, long tick)
    {
        var targets = objects.Where(value => !value.Owner.IsSystem && value.Body is not null && !value.DestroyRequested && !value.EffectivelyDead && !value.ContainedBy.IsValid).ToArray();
        if (targets.Length == 0) return;
        foreach (var unit in objects)
        {
            if (unit.Owner.IsSystem || unit.DestroyRequested || unit.EffectivelyDead || unit.ContainedBy.IsValid) continue;
            var kind = Classify(world, unit);
            if (!kind.Armed) continue;
            Entity? nearest = null;
            var best = Fixed.MaxValue;
            foreach (var candidate in targets)
            {
                if (candidate.Owner == unit.Owner) continue;
                var dx = candidate.X - unit.X;
                var dy = candidate.Y - unit.Y;
                var distance = dx * dx + dy * dy;
                if (distance < best) { best = distance; nearest = candidate; }
            }
            if (nearest is null) continue;
            if (best <= kind.Range * kind.Range)
            {
                if (tick >= unit.WeaponReadyTick)
                {
                    Add(world, new FireWeaponCommand(unit.Owner, Next(unit.Owner), tick, unit.Id, nearest.Id));
                    Shots++;
                }
            }
            else if (kind.Mobile && (!_orders.TryGetValue(unit.Id.Value, out var order) || order.Target != nearest.Id.Value || tick - order.Tick >= OrderRefreshTicks))
            {
                Add(world, new SetMoveTargetCommand(unit.Owner, Next(unit.Owner), tick, unit.Id, nearest.X, nearest.Y));
                _orders[unit.Id.Value] = (nearest.Id.Value, tick);
                Orders++;
            }
        }
    }
}
