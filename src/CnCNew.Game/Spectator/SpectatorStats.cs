using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using CnCNew.Data.Definitions;
using CnCNew.Presentation.Input;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;

namespace CnCNew.Game.Spectator;

/// <summary>What an object counts as in the observer panels.</summary>
public enum SpectatorUnitClass
{
    /// <summary>Projectiles, crates, debris and everything else that is neither a unit nor a building.</summary>
    None,
    /// <summary>Infantry, vehicles and aircraft that are not workers.</summary>
    Army,
    /// <summary><c>DOZER</c> or <c>HARVESTER</c>: builders, supply trucks, supply helicopters, workers.</summary>
    Worker,
    Structure,
}

/// <param name="Value">The summed <c>BuildCost</c> of the <paramref name="Count"/> objects.</param>
public sealed record SpectatorCount(string Template, int Count, long Value);

/// <param name="Producer">Template of the building that produces (for a construction: the building itself).</param>
/// <param name="Template">What is being produced or built.</param>
/// <param name="Progress">0..1: the work done on the head of the queue, or on the building.</param>
/// <param name="Queued">Entries in the queue including the head (1 for a construction).</param>
public sealed record SpectatorProductionItem(string Producer, string Template, float Progress, int Queued, bool IsConstruction);

/// <summary>One player's row of the observer panels at one tick. Money values are whole credits; where each value comes from is in docs/SPECTATOR-MODE.md §4.</summary>
public sealed record SpectatorPlayerPanel(
    PlayerId Player, string Faction, ColorValue Color, bool Defeated,
    long Money, long IncomePerMinute, long Collected, int Harvesters, int SupplyCenters, int PowerProduction, int PowerConsumption,
    int Units, int ArmyUnits, long ArmyValue, int Workers, int Structures,
    ImmutableArray<SpectatorCount> Army, ImmutableArray<SpectatorProductionItem> Production,
    ImmutableArray<SpectatorCount> UnitsLost, int UnitsLostCount, long UnitsLostValue,
    ImmutableArray<SpectatorCount> StructuresLost, int StructuresLostCount, long StructuresLostValue,
    int Kills, long KillsValue, int StructuresBuilt,
    ImmutableArray<string> Upgrades = default, int ObjectUpgrades = 0, ImmutableArray<string> Sciences = default, ImmutableArray<SpectatorProductionItem> Researching = default)
{
    /// <summary>The upgrades being researched now, with the building and the progress of each (<c>ProductionUpdate</c>'s queues; F-43).</summary>
    public ImmutableArray<SpectatorProductionItem> Researching { get; init; } = Researching.IsDefault ? [] : Researching;

    /// <summary>The player's upgrades (player-wide, in the catalogue's order; the engine's veterancy upgrades left out).</summary>
    public ImmutableArray<string> Upgrades { get; init; } = Upgrades.IsDefault ? [] : Upgrades;

    /// <summary>The player's sciences, in the catalogue's order.</summary>
    public ImmutableArray<string> Sciences { get; init; } = Sciences.IsDefault ? [] : Sciences;
}

/// <summary>Everything the observer panels show at one tick, built from the world and from <see cref="SpectatorStats"/>.</summary>
public sealed record SpectatorPanels(long Tick, long GameSeconds, ImmutableArray<SpectatorPlayerPanel> Players, PlayerId? Winner, bool IsOver)
{
    /// <summary>One block of lines per player; deterministic (two runs of the same match give the same text), for tests and the report of a headless run.</summary>
    public string Dump()
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"tick {Tick} time {GameSeconds}s winner {(Winner is { } winner ? winner.Value.ToString(CultureInfo.InvariantCulture) : "-")} over {IsOver}\n");
        foreach (var player in Players)
        {
            text.Append(CultureInfo.InvariantCulture, $"player {player.Player.Value} {player.Faction} defeated={player.Defeated} money={player.Money} income/min={player.IncomePerMinute} collected={player.Collected} harvesters={player.Harvesters} supplyCenters={player.SupplyCenters} power={player.PowerProduction}/{player.PowerConsumption}\n");
            text.Append(CultureInfo.InvariantCulture, $"  units={player.Units} army={player.ArmyUnits} armyValue={player.ArmyValue} workers={player.Workers} structures={player.Structures} structuresBuilt={player.StructuresBuilt} kills={player.Kills} killsValue={player.KillsValue}\n");
            text.Append("  army: ").Append(string.Join(", ", player.Army.Select(item => $"{item.Template} x{item.Count}"))).Append('\n');
            text.Append("  production: ").Append(string.Join(", ", player.Production.Select(item =>
                string.Create(CultureInfo.InvariantCulture, $"{(item.IsConstruction ? "build " : item.Producer + " > ")}{item.Template} {item.Progress * 100:F0}% q{item.Queued}")))).Append('\n');
            text.Append(CultureInfo.InvariantCulture, $"  units lost {player.UnitsLostCount} ({player.UnitsLostValue}): ").Append(string.Join(", ", player.UnitsLost.Select(item => $"{item.Template} x{item.Count}"))).Append('\n');
            text.Append(CultureInfo.InvariantCulture, $"  structures lost {player.StructuresLostCount} ({player.StructuresLostValue}): ").Append(string.Join(", ", player.StructuresLost.Select(item => $"{item.Template} x{item.Count}"))).Append('\n');
            text.Append(CultureInfo.InvariantCulture, $"  research: upgrades {string.Join(", ", player.Upgrades)}; object upgrades {player.ObjectUpgrades}; sciences {string.Join(", ", player.Sciences)}");
            if (!player.Researching.IsEmpty)
                text.Append("; in research ").Append(string.Join(", ", player.Researching.Select(item => string.Create(CultureInfo.InvariantCulture, $"{item.Producer} > {item.Template} {item.Progress * 100:F0}%"))));
            text.Append('\n');
        }
        return text.ToString();
    }
}

/// <summary>
/// Credits per game minute over the last minute of game time: one bucket per game second, so the same deposits at the same game time give the same number at 30, 60 and
/// 120 ticks per second. During the first minute it is the sum so far, not an extrapolation.
/// </summary>
public sealed class IncomeWindow
{
    public const int WindowSeconds = 60;

    private readonly long[] _amounts = new long[WindowSeconds];
    private readonly long[] _seconds = new long[WindowSeconds];
    private readonly int _ticksPerSecond;

    /// <param name="ticksPerSecond">The rate of the watched world (<c>world.Time.TicksPerSecond</c>).</param>
    public IncomeWindow(int ticksPerSecond)
    {
        if (ticksPerSecond < 1) throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));
        _ticksPerSecond = ticksPerSecond;
        Array.Fill(_seconds, -1);
    }

    public void Add(long tick, long amount)
    {
        var second = tick / _ticksPerSecond;
        var slot = (int)(second % WindowSeconds);
        if (_seconds[slot] != second)
        {
            _seconds[slot] = second;
            _amounts[slot] = 0;
        }
        _amounts[slot] += amount;
    }

    public long PerMinute(long tick)
    {
        var now = tick / _ticksPerSecond;
        long sum = 0;
        for (var slot = 0; slot < WindowSeconds; slot++)
            if (_seconds[slot] >= 0 && _seconds[slot] > now - WindowSeconds && _seconds[slot] <= now) sum += _amounts[slot];
        return sum;
    }
}

/// <summary>
/// The statistics of the observer panels that the simulation does not keep itself, accumulated from the events of every tick (<see cref="Observe"/>): credits collected and
/// the income of the last game minute (<see cref="SimEventKind.SupplyDeposited"/>), losses by type and value and kills (<see cref="SimEventKind.EntityDied"/>), buildings
/// finished (<see cref="SimEventKind.ConstructionCompleted"/>). <see cref="Build"/> adds what the world shows directly (money, power, the living objects, the queues).
/// It reads the world and the events only, uses no wall time and iterates in a fixed order, so the same match always gives the same panels.
/// </summary>
public sealed class SpectatorStats
{
    private sealed class Row(int ticksPerSecond)
    {
        public readonly IncomeWindow Income = new(ticksPerSecond);
        public long Collected;
        public int StructuresBuilt;
        public int Kills;
        public long KillsValue;
        public readonly SortedDictionary<string, (int Count, long Value)> UnitsLost = new(StringComparer.Ordinal);
        public readonly SortedDictionary<string, (int Count, long Value)> StructuresLost = new(StringComparer.Ordinal);
    }

    private readonly SortedDictionary<int, Row> _rows = [];
    private readonly HashSet<uint> _dead = [];
    // Who owned what, noted when it appeared: an object that dies and is removed within one tick is gone from the world when its death is read.
    private readonly Dictionary<uint, (PlayerId Owner, ObjectTemplate Template)> _known = [];
    private readonly int _ticksPerSecond;

    /// <param name="ticksPerSecond">The rate of the watched world (<c>world.Time.TicksPerSecond</c>).</param>
    public SpectatorStats(int ticksPerSecond) => _ticksPerSecond = ticksPerSecond;

    /// <summary>Events whose object the world no longer held when they were read (they are not counted anywhere).</summary>
    public int Unresolved { get; private set; }

    /// <summary>Events folded in so far.</summary>
    public long EventsSeen { get; private set; }

    private Row RowOf(PlayerId player) => _rows.TryGetValue(player.Value, out var row) ? row : _rows[player.Value] = new Row(_ticksPerSecond);

    public static SpectatorUnitClass Classify(ObjectTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        if (template.IsKindOf("STRUCTURE")) return SpectatorUnitClass.Structure;
        if (template.IsKindOf("PROJECTILE")) return SpectatorUnitClass.None;
        if (template.IsKindOf("DOZER") || template.IsKindOf("HARVESTER")) return SpectatorUnitClass.Worker;
        return template.IsKindOf("INFANTRY") || template.IsKindOf("VEHICLE") || template.IsKindOf("AIRCRAFT") ? SpectatorUnitClass.Army : SpectatorUnitClass.None;
    }

    /// <summary>Folds the events of one tick in; call once per tick with the world that produced them.</summary>
    public void Observe(TickResult result, SimulationWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        if (result.Events is null) return;
        foreach (var ev in result.Events)
        {
            EventsSeen++;
            switch (ev.Kind)
            {
                case SimEventKind.EntitySpawned:
                    if (world.FindEntity(ev.Entity) is { } spawned) _known[ev.Entity.Value] = (spawned.Owner, spawned.Template);
                    break;
                case SimEventKind.EntityDestroyed:
                    _known.Remove(ev.Entity.Value);
                    break;
                case SimEventKind.SupplyDeposited:
                    if (world.FindEntity(ev.Entity) is not { } center) { Unresolved++; break; }
                    if (center.Owner.IsSystem) break;
                    RowOf(center.Owner).Collected += ev.Value;
                    RowOf(center.Owner).Income.Add(ev.Tick, ev.Value);
                    break;
                case SimEventKind.ConstructionCompleted when !ev.Player.IsSystem:
                    RowOf(ev.Player).StructuresBuilt++;
                    break;
                case SimEventKind.EntityDied:
                {
                    if (!_dead.Add(ev.Entity.Value)) break;
                    (PlayerId Owner, ObjectTemplate Template) victim;
                    if (world.FindEntity(ev.Entity) is { } found) victim = (found.Owner, found.Template);
                    else if (!_known.TryGetValue(ev.Entity.Value, out victim)) { Unresolved++; break; }
                    var kind = Classify(victim.Template);
                    if (kind == SpectatorUnitClass.None || victim.Owner.IsSystem) break;
                    var value = victim.Template.BuildCost.Floor();
                    var lost = kind == SpectatorUnitClass.Structure ? RowOf(victim.Owner).StructuresLost : RowOf(victim.Owner).UnitsLost;
                    var (count, sum) = lost.GetValueOrDefault(victim.Template.Name);
                    lost[victim.Template.Name] = (count + 1, sum + value);
                    // Object::scoreTheKill: only a kill of somebody else's object scores.
                    if (ev.Other.IsValid && (world.FindEntity(ev.Other)?.Owner ?? (_known.TryGetValue(ev.Other.Value, out var remembered) ? remembered.Owner : PlayerId.System)) is { IsSystem: false } killerOwner && killerOwner != victim.Owner)
                    {
                        RowOf(killerOwner).Kills++;
                        RowOf(killerOwner).KillsValue += value;
                    }
                    break;
                }
            }
        }
    }

    /// <summary>The panels at the world's current tick.</summary>
    public SpectatorPanels Build(SimulationWorld world, IEnumerable<PlayerConfiguration> players, TeamColors colors, PlayerId? winner, bool isOver)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(players);
        ArgumentNullException.ThrowIfNull(colors);
        var panels = ImmutableArray.CreateBuilder<SpectatorPlayerPanel>();
        foreach (var player in players.OrderBy(value => value.Id.Value))
        {
            var row = RowOf(player.Id);
            var army = new SortedDictionary<string, (int Count, long Value)>(StringComparer.Ordinal);
            var production = ImmutableArray.CreateBuilder<SpectatorProductionItem>();
            var researching = ImmutableArray.CreateBuilder<SpectatorProductionItem>();
            int harvesters = 0, supplyCenters = 0, consumption = 0, units = 0, armyUnits = 0, workers = 0, structures = 0;
            long armyValue = 0;
            foreach (var entity in world.EntitiesOwnedBy(player.Id).AsSpan())
            {
                if (!SimulationQueries.IsAlive(entity)) continue;
                var template = entity.Template;
                var kind = Classify(template);
                if (kind == SpectatorUnitClass.None) continue;
                if (kind == SpectatorUnitClass.Structure)
                {
                    if (entity.HasStatus(ObjectStatusBits.UnderConstruction))
                    {
                        production.Add(new SpectatorProductionItem(template.Name, template.Name, CnCNew.Presentation.FixedConvert.ToSingle(entity.ConstructionShare), 1, true));
                        continue;
                    }
                    structures++;
                    if (template.EnergyProduction < 0) consumption -= template.EnergyProduction;
                    if (SimulationQueries.HasModule(entity, "SupplyCenterDockUpdate")) supplyCenters++;
                    if (SimulationQueries.ProductionOf(entity)?.Queue is { IsDefaultOrEmpty: false } queue)
                    {
                        // Only the head of a queue progresses; an upgrade at the head is research (an upgrade further back is only queued).
                        var (headName, headTicks) = queue[0].IsUpgrade
                            ? (world.Upgrades.Upgrades[queue[0].UpgradeIndex].Name, world.Upgrades.Upgrades[queue[0].UpgradeIndex].BuildTicks)
                            : (world.Templates[queue[0].TemplateIndex].Name, world.Templates[queue[0].TemplateIndex].BuildTicks);
                        var total = Math.Max(queue[0].TotalTicks > 0 ? queue[0].TotalTicks : headTicks, 1);
                        var item = new SpectatorProductionItem(template.Name, headName, Math.Clamp((total - queue[0].RemainingTicks) / (float)total, 0f, 1f), queue.Length, false);
                        production.Add(item);
                        if (queue[0].IsUpgrade) researching.Add(item);
                    }
                    continue;
                }
                units++;
                if (template.IsKindOf("HARVESTER") || SimulationQueries.IsSupplyCarrier(entity)) harvesters++;
                if (kind == SpectatorUnitClass.Worker) { workers++; continue; }
                armyUnits++;
                var cost = template.BuildCost.Floor();
                armyValue += cost;
                var (count, sum) = army.GetValueOrDefault(template.Name);
                army[template.Name] = (count + 1, sum + cost);
            }
            // Research (Phase 37): what the world holds - the player's upgrades, the upgrades of its objects, its sciences - and since Phase 39 (F-43) what is being researched.
            var upgrades = world.PlayerUpgrades(player.Id);
            var objectUpgrades = 0;
            foreach (var entity in world.EntitiesOwnedBy(player.Id).AsSpan())
                if (SimulationQueries.IsAlive(entity) && entity.CompletedUpgrades.Any())
                    foreach (var info in world.Upgrades.Upgrades)
                        if (!info.IsEngine && info.Scope == UpgradeScope.Object && entity.CompletedUpgrades.Has(info.Index)) objectUpgrades++;
            var sciences = world.PlayerSciences(player.Id);
            panels.Add(new SpectatorPlayerPanel(
                player.Id, player.Faction ?? player.Name, colors.Of(player.Id), world.IsPlayerDefeated(player.Id),
                world.MoneyOf(player.Id).Floor(), row.Income.PerMinute(world.Tick), row.Collected, harvesters, supplyCenters,
                // Energy::getProduction as the control bar shows it (CommandBarModel.Hud): the world keeps the balance, the consumers are added back.
                world.EnergyProductionOf(player.Id) + consumption, consumption,
                units, armyUnits, armyValue, workers, structures,
                Sorted(army), production.ToImmutable(),
                Sorted(row.UnitsLost), row.UnitsLost.Values.Sum(value => value.Count), row.UnitsLost.Values.Sum(value => value.Value),
                Sorted(row.StructuresLost), row.StructuresLost.Values.Sum(value => value.Count), row.StructuresLost.Values.Sum(value => value.Value),
                row.Kills, row.KillsValue, row.StructuresBuilt,
                [..world.Upgrades.Upgrades.Where(info => !info.IsEngine && info.Scope == UpgradeScope.Player && upgrades.Has(info.Index)).Select(info => info.Name)], objectUpgrades,
                [..world.Sciences.Sciences.Where(info => sciences.Has(info.Index)).Select(info => info.Name)], researching.ToImmutable()));
        }
        return new SpectatorPanels(world.Tick, world.Tick / _ticksPerSecond, panels.ToImmutable(), winner, isOver);
    }

    /// <summary>Highest value first, then most, then by name: the order of the lists.</summary>
    private static ImmutableArray<SpectatorCount> Sorted(SortedDictionary<string, (int Count, long Value)> counts)
        => [..counts.Select(pair => new SpectatorCount(pair.Key, pair.Value.Count, pair.Value.Value))
            .OrderByDescending(item => item.Value).ThenByDescending(item => item.Count).ThenBy(item => item.Template, StringComparer.Ordinal)];
}
