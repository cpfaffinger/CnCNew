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

/// <summary>
/// Units: <paramref name="MaxTicks"/>, <paramref name="MinTicks"/> and <paramref name="CheckpointInterval"/> are run budgets in <b>ticks</b> of the catalog's rate (they go into the
/// replay header as they are); <paramref name="GraceTicks"/> and <paramref name="IncomeInterval"/> are rule values in <b>logic frames of the original</b> (1/30 s) that the scenario
/// scales to the rate. At 30 Hz a frame is a tick and every value means what it always did.
/// </summary>
/// <param name="MaxTicks">Hard stop when nobody has won by then, in ticks (a run budget).</param>
/// <param name="GraceTicks">Game time recorded after the winner is known, in logic frames of the original (the name is kept for the callers).</param>
/// <param name="MinTicks">The replay is padded with idle ticks up to this length, in ticks (0: none; a run budget like <paramref name="MaxTicks"/>); shows the world stays stable over 216,000 ticks.</param>
/// <param name="CheckpointInterval">Distance of the replay's checkpoints, in ticks.</param>
/// <param name="ArenaRadius">Distance of every base from the map centre.</param>
/// <param name="Income">Money the system credits every living player every <see cref="IncomeInterval"/> (stand-in for supply gathering).</param>
/// <param name="IncomeInterval">Distance of two income payments, in logic frames of the original.</param>
public sealed record ReferenceMatchOptions(uint WorldSeed, uint DriverSeed, long MaxTicks, long GraceTicks, long MinTicks, int CheckpointInterval, int ArenaRadius, int Income, int IncomeInterval)
{
    // The driver seed is chosen, not arbitrary: the match is a war of attrition whose end depends on the production draws. Of 36 driver seeds four (at 16
    // fraction bits) or five (at 24) give no winner within 45,000 ticks, among them, at 24 bits, the seed 5EED0005 that was the standard until phase 22.
    // 5EED0007 ends with the same winner at both resolutions (docs/FIXED-POINT-Q39-24.md).
    public static ReferenceMatchOptions Standard { get; } = new(0xC0FFEE05, 0x5EED0007, 216_000, 300, 0, 1_000, 1_500, 1_500, 300);
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
    // The generator's own rhythms, in logic frames of the original; scaled once per run (Record), so it plays the same schedule in game time at every rate.
    private const long MoveCadenceFrames = 15;
    private const long ProductionCadenceFrames = 30;
    private const long OrderRefreshFrames = 150;
    private const long FirstOrderFrames = 1;

    private readonly ReferenceMatchOptions _options;
    private readonly SageRandom _random;
    private readonly List<SimCommand> _commands = [];
    private readonly Dictionary<uint, (uint Target, long Tick)> _orders = [];
    private readonly Dictionary<int, UnitClass> _classes = [];
    private readonly Dictionary<string, ImmutableArray<ObjectTemplate>> _rosters = new(StringComparer.OrdinalIgnoreCase);
    private string[] _sides = [];
    private ulong[] _sequence = [];
    private int _alive;
    private long _moveCadence;
    private long _productionCadence;
    private long _orderRefresh;
    private long _incomeInterval;
    private long _firstOrders;

    private readonly record struct UnitClass(bool Armed, bool Mobile);

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
    /// <summary>Supply carriers the generator ordered to a warehouse; the trucks then run their own state machine.</summary>
    public int DockOrders { get; private set; }
    public ImmutableArray<string> SupplyNotes { get; private set; } = [];
    /// <summary>Deliveries a carrier made at a supply center, and the money they paid (auto-deposits and other income excluded).</summary>
    public int SupplyDeliveries { get; private set; }
    public long SupplyDeliveryMoney { get; private set; }
    public int ProductionOrders { get; private set; }
    public int Orders { get; private set; }
    public int Shots { get; private set; }

    /// <summary>
    /// Every playable faction that can field a base, in ordinal name order. A faction starts with its own <c>StartingBuilding</c> when the
    /// simulation can instantiate it; otherwise (a starting building with a module the simulation lacks) it starts with a stand-in: the ordinally first instantiable structure of the same side that has a
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
        Players = [..factions.Select((participant, index) => new PlayerConfiguration(new PlayerId(index + 1), participant.Faction.Name, participant.Faction.Name, IsComputer: true))];
        _sequence = new ulong[Players.Length + 1];
        _sides = [..factions.Select(participant => participant.Side)];
        var provisional = new ReplayHeader(ReplayFormat.CurrentVersion, CommandTypes.Version, catalog.Rate.TicksPerSecond, fingerprint,
            FixtureId, _options.WorldSeed, new WorldId(1), Players, _options.MaxTicks, _options.CheckpointInterval);
        var world = ReplayRunner.CreateWorld(provisional, catalog);
        var time = world.Time;
        _moveCadence = time.OriginalFrames(MoveCadenceFrames);
        _productionCadence = time.OriginalFrames(ProductionCadenceFrames);
        _orderRefresh = time.OriginalFrames(OrderRefreshFrames);
        _incomeInterval = time.OriginalFrames(_options.IncomeInterval);
        _firstOrders = time.OriginalFrames(FirstOrderFrames);
        var grace = time.OriginalFrames(_options.GraceTicks);
        SpawnStartingObjects(world, catalog, factions);

        var endTick = Math.Max(_options.MaxTicks, _options.MinTicks);
        while (world.Tick < endTick)
        {
            var tick = world.Tick;
            if (VictoryTick < 0 && tick < _options.MaxTicks) Generate(world, catalog, tick);
            var result = world.Step();
            Observe(result.Events);
            PeakAlive = Math.Max(PeakAlive, _alive);
            if (VictoryTick < 0 && tick > 0 && tick % _moveCadence == 0 && world.TryGetWinner() is { } winner)
            {
                Winner = winner;
                VictoryTick = tick;
                endTick = Math.Max(tick + TickOrder.Next + grace, _options.MinTicks);
            }
        }
        var header = provisional with { EndTick = endTick };
        return new ReplayFile(header, [.._commands.OrderBy(value => value.Tick).ThenBy(value => value.Player.Value).ThenBy(value => value.Sequence)], []);
    }

    private void Observe(IReadOnlyList<SimEvent> events)
    {
        foreach (var simEvent in events)
        {
            if (simEvent.Kind == SimEventKind.SupplyDeposited && simEvent.Entity != simEvent.Other)
            {
                SupplyDeliveries++;
                SupplyDeliveryMoney += simEvent.Value;
            }
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
        SpawnSupplyInfrastructure(world, catalog, factions);
    }

    /// <summary>
    /// Stand-in for construction: every base gets one supply center and one supply carrier of its side plus a neutral warehouse, so the dock
    /// machine, the carrier state machine and the deposits run on real definitions. A side without one of the three is noted, never silent.
    /// </summary>
    private void SpawnSupplyInfrastructure(SimulationWorld world, TemplateCatalog catalog, ImmutableArray<MatchParticipant> factions)
    {
        var notes = ImmutableArray.CreateBuilder<string>();
        var warehouse = catalog.Templates.Where(value => value.IsInstantiable && value.Modules.Any(module => module.Name == "SupplyWarehouseDockUpdate"))
            .OrderBy(value => value.Name, StringComparer.Ordinal).FirstOrDefault();
        if (warehouse is null) notes.Add("no instantiable supply warehouse");
        for (var index = 0; index < factions.Length; index++)
        {
            var player = Players[index].Id;
            var side = factions[index].Side;
            var centerTemplate = catalog.Templates.Where(value => value.IsInstantiable && value.IsKindOf("STRUCTURE") && string.Equals(value.Side, side, StringComparison.OrdinalIgnoreCase)
                    && value.Modules.Any(module => module.Name == "SupplyCenterDockUpdate")).OrderBy(value => value.Name, StringComparer.Ordinal).FirstOrDefault();
            var carrier = catalog.Templates.Where(value => value.IsInstantiable && string.Equals(value.Side, side, StringComparison.OrdinalIgnoreCase)
                    && value.Modules.Any(module => module.Name is "SupplyTruckAIUpdate" or "WorkerAIUpdate" or "ChinookAIUpdate")).OrderBy(value => value.Name, StringComparer.Ordinal).FirstOrDefault();
            if (centerTemplate is null || carrier is null || warehouse is null)
            {
                notes.Add($"{factions[index].Faction.Name}: no supply economy (center {centerTemplate?.Name ?? "-"}, carrier {carrier?.Name ?? "-"})");
                continue;
            }
            var (bx, by) = BasePosition(index, factions.Length);
            var angle = FixedTrig.TwoPi * index / Fixed.FromInt(factions.Length);
            var inward = (X: -FixedTrig.Cos(angle), Y: -FixedTrig.Sin(angle));
            Add(world, new SpawnCommand(player, Next(player), 0, centerTemplate.Name, player, bx + inward.X * Fixed.FromInt(90), by + inward.Y * Fixed.FromInt(90), Fixed.Zero, Fixed.Zero));
            Add(world, new SpawnCommand(PlayerId.System, Next(PlayerId.System), 0, warehouse.Name, PlayerId.System, bx + inward.X * Fixed.FromInt(200), by + inward.Y * Fixed.FromInt(200), Fixed.Zero, Fixed.Zero));
            Add(world, new SpawnCommand(player, Next(player), 0, carrier.Name, player, bx + inward.X * Fixed.FromInt(110), by + inward.Y * Fixed.FromInt(110), Fixed.Zero, Fixed.Zero));
        }
        SupplyNotes = notes.ToImmutable();
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
        var armed = entity.Template.WeaponSets.Any(set => set.WeaponCount > 0);
        var classified = new UnitClass(armed, entity.Modules.Any(module => module is AIUpdateInterface));
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

    /// <summary>Per side: the match units and producer structures with the fewest missing modules, i.e. the cheapest ones to unlock.</summary>
    public static ImmutableArray<string> DescribeClosest(TemplateCatalog catalog, int take)
    {
        var lines = ImmutableArray.CreateBuilder<string>();
        foreach (var side in catalog.Factions.Factions.Where(value => value.PlayableSide && !value.IsObserver)
                     .Select(value => catalog.TryGet(value.StartingBuilding, out var building) ? building.Side : null).OfType<string>()
                     .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(value => value, StringComparer.Ordinal))
        {
            var units = catalog.Templates.Where(template => !template.IsInstantiable && IsRosterCandidate(template) && string.Equals(template.Side, side, StringComparison.OrdinalIgnoreCase));
            var producers = catalog.Templates.Where(template => !template.IsInstantiable && template.IsKindOf("STRUCTURE") && template.Modules.Any(module => module.Name == "ProductionUpdate")
                && string.Equals(template.Side, side, StringComparison.OrdinalIgnoreCase));
            foreach (var (label, set) in new[] { ("unit", units), ("producer", producers) })
                foreach (var template in set.OrderBy(value => value.MissingModules.Length).ThenBy(value => value.Name, StringComparer.Ordinal).Take(take))
                    lines.Add($"side {side} {label} {template.Name}: {string.Join(", ", template.MissingModules)}");
        }
        return lines.ToImmutable();
    }

    private void Generate(SimulationWorld world, TemplateCatalog catalog, long tick)
    {
        if (tick == 0) return;
        if (tick % _incomeInterval == 0)
            for (var index = 0; index < Players.Length; index++)
                if (!world.IsPlayerDefeated(Players[index].Id))
                    Add(world, new GiveMoneyCommand(PlayerId.System, Next(PlayerId.System), tick, Players[index].Id, Fixed.FromInt(_options.Income)));
        // Order, not time: the carriers exist one step after their spawn commands of tick 0. One step of the 30 Hz match is one logic frame of the original: above 30 Hz the
        // orders are given where the carriers' first frame begins, not in the middle of the frame they appeared in.
        if (tick == _firstOrders) IssueDockOrders(world, tick);
        var productionTick = tick % _productionCadence == 0;
        var moveTick = tick % _moveCadence == 0;
        if (!productionTick && !moveTick) return;

        var objects = world.Entities.OrderBy(value => value.Id.Value).ToArray();
        if (productionTick) QueueProduction(world, catalog, objects, tick);
        if (moveTick) IssueOrders(world, objects, tick);
    }

    private void IssueDockOrders(SimulationWorld world, long tick)
    {
        var objects = world.Entities.OrderBy(value => value.Id.Value).ToArray();
        var warehouses = objects.Where(value => value.Modules.Any(module => module is SupplyWarehouseDockUpdate)).ToArray();
        foreach (var carrier in objects.Where(value => !value.Owner.IsSystem && value.Modules.Any(module => module is ISupplyCarrier)))
        {
            var nearest = warehouses.OrderBy(value => (value.X - carrier.X) * (value.X - carrier.X) + (value.Y - carrier.Y) * (value.Y - carrier.Y)).ThenBy(value => value.Id.Value).FirstOrDefault();
            if (nearest is null) continue;
            Add(world, new DockCommand(carrier.Owner, Next(carrier.Owner), tick, carrier.Id, nearest.Id));
            DockOrders++;
        }
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
                // Phase 10 enforces prerequisites and MaxSimultaneousOfType on QueueProductionCommand; this scenario never builds the tech
                // structures a tier-2/3 pick might need, so a template not currently queueable is skipped rather than sent as a doomed command.
                if (!world.CanQueueProductionTemplate(producer.Owner, producer.Id, pick.Name)) continue;
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
                if (!world.AreEnemies(unit.Owner, candidate.Owner)) continue;
                var dx = candidate.X - unit.X;
                var dy = candidate.Y - unit.Y;
                var distance = dx * dx + dy * dy;
                if (distance >= best) continue;
                // Only a target one of the unit's weapons can ever damage is worth walking to (an anti-ground unit ignores aircraft, an anti-air unit ground troops).
                if (world.ChooseWeapon(unit, candidate, FireOrigin.Ai).Rejection is CommandRejection.InvalidWeaponTarget or CommandRejection.NoWeapon) continue;
                best = distance;
                nearest = candidate;
            }
            if (nearest is null) continue;
            var range = world.MaxWeaponRange(unit);
            if (SquaredReach.Of(range).Contains(best))
            {
                if (world.ChooseWeapon(unit, nearest, FireOrigin.Ai).CanFire)
                {
                    Add(world, new FireWeaponCommand(unit.Owner, Next(unit.Owner), tick, unit.Id, nearest.Id, WeaponSlot.Best, FireOrigin.Ai));
                    Shots++;
                }
            }
            else if (kind.Mobile && (!_orders.TryGetValue(unit.Id.Value, out var order) || order.Target != nearest.Id.Value || tick - order.Tick >= _orderRefresh))
            {
                Add(world, new PursueCommand(unit.Owner, Next(unit.Owner), tick, unit.Id, nearest.Id, nearest.X, nearest.Y));
                _orders[unit.Id.Value] = (nearest.Id.Value, tick);
                Orders++;
            }
        }
    }
}
