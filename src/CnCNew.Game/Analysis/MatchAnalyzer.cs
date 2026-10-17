using System.Collections.Immutable;
using CnCNew.Game.Replays;
using CnCNew.Game.Spectator;
using CnCNew.Presentation.Input;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;

namespace CnCNew.Game.Analysis;

/// <summary>
/// The core of the match analysis (docs/MATCH-ANALYSIS.md): it is told every tick of a skirmish - the events the tick raised and the world after it - and keeps what a
/// <see cref="MatchAnalysis"/> holds. The same core serves a running match (<see cref="LiveMatchAnalysis"/>) and a replay played again headless (<see cref="ReplayAnalysis"/>).
/// <para>
/// It reads the world and the events only and writes nothing into either; it uses no wall time, iterates players by number and objects in the world's order, and keeps
/// its tables sorted by name, so the same ticks give the same analysis. The world is sampled every <see cref="MatchAnalysis.SampleSeconds"/> seconds of game time, at the
/// tick that ends an original frame, so that the samples are taken at the same game times at 30, 60 and 120 ticks per second.
/// </para>
/// </summary>
public sealed class MatchAnalyzer
{
    private sealed class Row(int ticksPerSecond)
    {
        public readonly IncomeWindow Income = new(ticksPerSecond);
        public long Collected;
        public long Spent;
        public int Kills;
        public long KillsValue;
        public int StructuresSold;
        public int Upgrades;
        public int Sciences;
        public int SpecialPowers;
        public int Superweapons;
        public readonly SortedDictionary<string, (int Count, long Value)> UnitsBuilt = new(StringComparer.Ordinal);
        public readonly SortedDictionary<string, (int Count, long Value)> UnitsLost = new(StringComparer.Ordinal);
        public readonly SortedDictionary<string, (int Count, long Value)> StructuresBuilt = new(StringComparer.Ordinal);
        public readonly SortedDictionary<string, (int Count, long Value)> StructuresLost = new(StringComparer.Ordinal);
        public int UnitsLostCount;
        public int StructuresLostCount;
        public readonly int[] Movement = new int[MatchAnalysis.HeatGridSize * MatchAnalysis.HeatGridSize];
    }

    /// <summary>An event before its place is known: a special power gets the target of its command when the analysis ends (<see cref="Finish"/>).</summary>
    private sealed record Pending(long Tick, AnalysisEvent Event, int PowerIndex);

    private readonly ImmutableArray<ReplayPlayer> _players;
    private readonly IReadOnlyDictionary<int, int> _colors;
    private readonly SortedDictionary<int, Row> _rows = [];
    private readonly HashSet<uint> _dead = [];
    // Who owned what, noted when it appeared: an object that dies and is removed within one tick is gone from the world when its death is read.
    private readonly Dictionary<uint, (PlayerId Owner, ObjectTemplate Template)> _known = [];
    private readonly List<AnalysisSample> _samples = [];
    private readonly List<Pending> _events = [];
    private readonly int[] _fights = new int[MatchAnalysis.HeatGridSize * MatchAnalysis.HeatGridSize];
    private readonly int _ticksPerSecond;
    private readonly int _ticksPerFrame;
    private readonly long _sampleTicks;
    private readonly Fixed _extentX;
    private readonly Fixed _extentY;
    private long _lastSampled = -1;

    /// <param name="world">The world of the match, at the tick the analysis begins (it is sampled there when that tick lies on the grid).</param>
    /// <param name="players">The players of the match, in slot order, as the replay header names them.</param>
    /// <param name="colors">The colour of every player as 0xRRGGBB.</param>
    public MatchAnalyzer(SimulationWorld world, ImmutableArray<ReplayPlayer> players, IReadOnlyDictionary<int, int> colors)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(colors);
        _players = players;
        _colors = colors;
        _ticksPerSecond = world.Time.TicksPerSecond;
        _ticksPerFrame = world.Rate.TicksPerOriginalFrame;
        _sampleTicks = world.Time.WholeSecondsToTicks(MatchAnalysis.SampleSeconds);
        _extentX = world.Terrain?.ExtentX ?? Fixed.One;
        _extentY = world.Terrain?.ExtentY ?? Fixed.One;
        StartTick = world.Tick;
        foreach (var player in players) RowOf(new PlayerId(player.Player));
        foreach (var entity in world.Entities) _known[entity.Id.Value] = (entity.Owner, entity.Template);
        if (world.Tick % _sampleTicks == 0) Sample(world);
    }

    public long StartTick { get; }

    /// <summary>Events whose object the world no longer held when they were read; they are counted nowhere else.</summary>
    public int Unresolved { get; private set; }

    /// <summary>The ticks told so far.</summary>
    public long TicksObserved { get; private set; }

    /// <summary>The colours of the players as the presentation draws them (<see cref="TeamColors"/>), as 0xRRGGBB.</summary>
    public static IReadOnlyDictionary<int, int> ColorsOf(IEnumerable<PlayerConfiguration> players, CnCNew.Data.Definitions.CompiledDefinitionGraph? graph)
    {
        var list = players.ToList();
        var colors = TeamColors.Resolve(list, graph);
        return list.ToDictionary(player => player.Id.Value, player =>
        {
            var color = colors.Of(player.Id);
            return AnalysisPalette.Rgb(color.Red, color.Green, color.Blue);
        });
    }

    /// <summary>The players of a running session as the replay header names them; <paramref name="nameOf"/> gives a human's name by slot (empty when it has none).</summary>
    public static ImmutableArray<ReplayPlayer> PlayersOf(SkirmishMatchSession session, Func<int, string> nameOf)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(nameOf);
        var players = ImmutableArray.CreateBuilder<ReplayPlayer>();
        foreach (var slot in session.Match.Slots)
        {
            var computer = slot.Description.Kind == MatchSlotKind.Computer;
            players.Add(new ReplayPlayer(slot.Player.Value, slot.Slot, slot.Participant.Faction.Name, computer ? string.Empty : nameOf(slot.Slot), slot.Description.Team, slot.Color,
                computer, computer ? MatchRecorder.AiName(slot.Description) : string.Empty));
        }
        return players.ToImmutable();
    }

    private Row RowOf(PlayerId player) => _rows.TryGetValue(player.Value, out var row) ? row : _rows[player.Value] = new Row(_ticksPerSecond);

    private bool Counted(PlayerId player) => !player.IsSystem && _rows.ContainsKey(player.Value);

    private long Frame(long tick) => tick / _ticksPerFrame;

    private int Cell(Fixed x, Fixed y)
    {
        var size = MatchAnalysis.HeatGridSize;
        var column = (int)Math.Clamp(Fixed.MultiplyDivide(x, Fixed.FromInt(size), _extentX).Floor(), 0, size - 1);
        var row = (int)Math.Clamp(Fixed.MultiplyDivide(y, Fixed.FromInt(size), _extentY).Floor(), 0, size - 1);
        return row * size + column;
    }

    private static void Add(SortedDictionary<string, (int Count, long Value)> counts, string name, long value)
    {
        var (count, sum) = counts.GetValueOrDefault(name);
        counts[name] = (count + 1, sum + value);
    }

    private void Note(long tick, string kind, PlayerId player, PlayerId other, string name, long value, Entity? at, int powerIndex = -1)
        => _events.Add(new Pending(tick, new AnalysisEvent(Frame(tick), kind, player.Value, other.Value, name, value,
            at is null ? -1 : (int)at.X.Floor(), at is null ? -1 : (int)at.Y.Floor()), powerIndex));

    private (PlayerId Owner, ObjectTemplate Template)? Identify(SimulationWorld world, EntityId id)
    {
        if (world.FindEntity(id) is { } entity) return (entity.Owner, entity.Template);
        return _known.TryGetValue(id.Value, out var known) ? known : null;
    }

    /// <summary>Folds in one tick: call after every step with its result and the world that produced it.</summary>
    public void Observe(TickResult result, SimulationWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        TicksObserved++;
        if (result.Events is { } events)
            foreach (var ev in events) Fold(ev, world);
        if (world.Tick % _sampleTicks == 0) Sample(world);
    }

    private void Fold(SimEvent ev, SimulationWorld world)
    {
        switch (ev.Kind)
        {
            case SimEventKind.EntitySpawned:
                if (world.FindEntity(ev.Entity) is { } spawned) _known[ev.Entity.Value] = (spawned.Owner, spawned.Template);
                break;
            case SimEventKind.EntityDestroyed:
                _known.Remove(ev.Entity.Value);
                break;
            case SimEventKind.SupplyDeposited:
            {
                if (Identify(world, ev.Entity) is not { } center) { Unresolved++; break; }
                if (!Counted(center.Owner)) break;
                var row = RowOf(center.Owner);
                row.Collected += ev.Value;
                row.Income.Add(ev.Tick, ev.Value);
                break;
            }
            case SimEventKind.ProductionCompleted:
            {
                if (Identify(world, ev.Entity) is not { } factory) { Unresolved++; break; }
                if (!Counted(factory.Owner) || ev.Value < 0 || ev.Value >= world.Templates.Templates.Length) break;
                var template = world.Templates[(int)ev.Value];
                var cost = template.BuildCost.Floor();
                var row = RowOf(factory.Owner);
                Add(row.UnitsBuilt, template.Name, cost);
                row.Spent += cost;
                break;
            }
            case SimEventKind.ConstructionStarted:
            {
                if (Identify(world, ev.Entity) is not { } site) { Unresolved++; break; }
                if (Counted(site.Owner)) RowOf(site.Owner).Spent += site.Template.BuildCost.Floor();
                break;
            }
            case SimEventKind.ConstructionCancelled:
            {
                if (Identify(world, ev.Entity) is not { } site) { Unresolved++; break; }
                if (Counted(site.Owner)) RowOf(site.Owner).Spent -= site.Template.BuildCost.Floor();
                break;
            }
            case SimEventKind.ConstructionCompleted:
            {
                if (Identify(world, ev.Entity) is not { } building) { Unresolved++; break; }
                var owner = ev.Player.IsSystem ? building.Owner : ev.Player;
                if (!Counted(owner)) break;
                Add(RowOf(owner).StructuresBuilt, building.Template.Name, building.Template.BuildCost.Floor());
                Note(ev.Tick, "structure-built", owner, PlayerId.System, building.Template.Name, building.Template.BuildCost.Floor(), world.FindEntity(ev.Entity));
                break;
            }
            case SimEventKind.StructureSold:
            {
                if (Identify(world, ev.Entity) is not { } sold) { Unresolved++; break; }
                var owner = ev.Player.IsSystem ? sold.Owner : ev.Player;
                if (!Counted(owner)) break;
                RowOf(owner).StructuresSold++;
                Note(ev.Tick, "structure-sold", owner, PlayerId.System, sold.Template.Name, sold.Template.BuildCost.Floor(), world.FindEntity(ev.Entity));
                break;
            }
            case SimEventKind.StructureCaptured:
            {
                // Emitted with the new owner as the player and the old one as the value (SimulationWorld.PowerAndCapture).
                if (world.FindEntity(ev.Entity) is not { } captured) { Unresolved++; break; }
                _known[ev.Entity.Value] = (captured.Owner, captured.Template);
                if (Counted(ev.Player)) Note(ev.Tick, "captured", ev.Player, new PlayerId((int)ev.Value), captured.Template.Name, captured.Template.BuildCost.Floor(), captured);
                break;
            }
            case SimEventKind.UpgradeCompleted when Counted(ev.Player):
            {
                var row = RowOf(ev.Player);
                row.Upgrades++;
                var cost = world.Upgrades.TryGet(ev.Text, out var upgrade) ? upgrade.BuildCost.Floor() : 0;
                row.Spent += cost;
                Note(ev.Tick, "upgrade", ev.Player, PlayerId.System, ev.Text, cost, null);
                break;
            }
            case SimEventKind.ScienceCompleted when Counted(ev.Player):
                RowOf(ev.Player).Sciences++;
                Note(ev.Tick, "science", ev.Player, PlayerId.System, ev.Text, 0, null);
                break;
            case SimEventKind.SpecialPowerUsed when Counted(ev.Player):
            {
                var source = world.FindEntity(ev.Entity);
                var superweapon = source?.Template.IsKindOf("FS_SUPERWEAPON") == true;
                var row = RowOf(ev.Player);
                if (superweapon) row.Superweapons++; else row.SpecialPowers++;
                // A power aimed at an object has its place now (the object's); one aimed at a place gets it from its command when the analysis ends.
                var target = ev.Other.IsValid ? world.FindEntity(ev.Other) : null;
                Note(ev.Tick, superweapon ? "superweapon" : "special-power", ev.Player, PlayerId.System, ev.Text, 0, target, target is null ? (int)ev.Value : -1);
                break;
            }
            case SimEventKind.PlayerDefeated when Counted(ev.Player):
                Note(ev.Tick, "defeated", ev.Player, PlayerId.System, string.Empty, 0, null);
                break;
            case SimEventKind.EntityDied:
            {
                if (!_dead.Add(ev.Entity.Value)) break;
                var found = world.FindEntity(ev.Entity);
                if (Identify(world, ev.Entity) is not { } victim) { Unresolved++; break; }
                var kind = SpectatorStats.Classify(victim.Template);
                if (kind == SpectatorUnitClass.None || !Counted(victim.Owner)) break;
                var value = victim.Template.BuildCost.Floor();
                var row = RowOf(victim.Owner);
                if (kind == SpectatorUnitClass.Structure) { Add(row.StructuresLost, victim.Template.Name, value); row.StructuresLostCount++; }
                else { Add(row.UnitsLost, victim.Template.Name, value); row.UnitsLostCount++; }
                var killer = ev.Other.IsValid && Identify(world, ev.Other) is { } other ? other.Owner : PlayerId.System;
                // Object::scoreTheKill: only a kill of somebody else's object scores.
                if (Counted(killer) && killer != victim.Owner)
                {
                    RowOf(killer).Kills++;
                    RowOf(killer).KillsValue += value;
                }
                else killer = PlayerId.System;
                if (found is not null) _fights[Cell(found.X, found.Y)]++;
                Note(ev.Tick, kind == SpectatorUnitClass.Structure ? "structure-lost" : "unit-lost", victim.Owner, killer, victim.Template.Name, value, found);
                break;
            }
        }
    }

    private void Sample(SimulationWorld world)
    {
        if (world.Tick == _lastSampled) return;
        _lastSampled = world.Tick;
        var regions = MatchAnalysis.RegionsPerSide * MatchAnalysis.RegionsPerSide;
        var presence = new SortedDictionary<int, long[]>();
        var rows = new List<(PlayerId Player, long[] Values)>();
        foreach (var (number, row) in _rows)
        {
            var player = new PlayerId(number);
            long armyValue = 0, infantry = 0, vehicles = 0, aircraft = 0;
            int armyUnits = 0, workers = 0, structures = 0, consumption = 0;
            var map = new long[regions];
            foreach (var entity in world.EntitiesOwnedBy(player).AsSpan())
            {
                if (!SimulationQueries.IsAlive(entity)) continue;
                var template = entity.Template;
                var kind = SpectatorStats.Classify(template);
                if (kind == SpectatorUnitClass.None) continue;
                var cost = template.BuildCost.Floor();
                if (kind == SpectatorUnitClass.Structure)
                {
                    if (entity.HasStatus(ObjectStatusBits.UnderConstruction)) continue;
                    structures++;
                    if (template.EnergyProduction < 0) consumption -= template.EnergyProduction;
                    map[Region(entity.X, entity.Y)] += cost;
                    continue;
                }
                if (kind == SpectatorUnitClass.Worker) { workers++; continue; }
                armyUnits++;
                armyValue += cost;
                if (template.IsKindOf("AIRCRAFT")) aircraft += cost;
                else if (template.IsKindOf("VEHICLE")) vehicles += cost;
                else infantry += cost;
                map[Region(entity.X, entity.Y)] += cost;
                row.Movement[Cell(entity.X, entity.Y)]++;
            }
            presence[number] = map;
            rows.Add((player, [world.MoneyOf(player).Floor(), row.Collected, row.Income.PerMinute(world.Tick), row.Spent, armyValue, armyUnits, infantry, vehicles, aircraft,
                workers, structures, world.EnergyProductionOf(player) + consumption, consumption, row.UnitsLostCount, row.StructuresLostCount, row.Kills]));
        }
        var controlled = new SortedDictionary<int, int>();
        for (var region = 0; region < regions; region++)
        {
            var best = 0;
            long bestValue = 0;
            var tie = false;
            foreach (var (number, map) in presence)
            {
                if (map[region] > bestValue) { best = number; bestValue = map[region]; tie = false; }
                else if (map[region] == bestValue && bestValue > 0) tie = true;
            }
            if (best != 0 && !tie) controlled[best] = controlled.GetValueOrDefault(best) + 1;
        }
        foreach (var (player, v) in rows)
            _samples.Add(new AnalysisSample(Frame(world.Tick), player.Value, v[0], v[1], v[2], v[3], v[4], (int)v[5], v[6], v[7], v[8], (int)v[9], (int)v[10], (int)v[11], (int)v[12],
                controlled.GetValueOrDefault(player.Value), (int)v[13], (int)v[14], (int)v[15]));
    }

    private int Region(Fixed x, Fixed y)
    {
        var size = MatchAnalysis.RegionsPerSide;
        var column = (int)Math.Clamp(Fixed.MultiplyDivide(x, Fixed.FromInt(size), _extentX).Floor(), 0, size - 1);
        var row = (int)Math.Clamp(Fixed.MultiplyDivide(y, Fixed.FromInt(size), _extentY).Floor(), 0, size - 1);
        return row * size + column;
    }

    /// <summary>
    /// The analysis of the ticks told so far. <paramref name="commands"/> are the commands of the match from its start (the session's recording): they give the commands per
    /// minute and the target of every special power. The world is sampled once more at its tick when that is not a sample tick already.
    /// </summary>
    public MatchAnalysis Finish(SimulationWorld world, IReadOnlyList<SimCommand> commands, PlayerId? winner, string mapPath, string rules, string ending, string source)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(commands);
        Sample(world);
        var minute = (long)_ticksPerSecond * 60;
        var minutes = (int)((world.Tick + minute - 1) / minute);
        var perMinute = new SortedDictionary<int, int[]>();
        foreach (var number in _rows.Keys) perMinute[number] = new int[minutes];
        var powers = new Dictionary<(long Tick, int Player, string Power), Queue<UseSpecialPowerCommand>>();
        foreach (var command in commands)
        {
            if (command.Player.IsSystem || command.RequiresSystem || command.Tick >= world.Tick || !perMinute.TryGetValue(command.Player.Value, out var counts)) continue;
            counts[(int)(command.Tick / minute)]++;
            if (command is UseSpecialPowerCommand use)
            {
                var key = (use.Tick, use.Player.Value, use.SpecialPower);
                if (!powers.TryGetValue(key, out var queue)) powers[key] = queue = new Queue<UseSpecialPowerCommand>();
                queue.Enqueue(use);
            }
        }
        var events = ImmutableArray.CreateBuilder<AnalysisEvent>(_events.Count);
        foreach (var pending in _events)
        {
            var item = pending.Event;
            if (pending.PowerIndex >= 0 && powers.TryGetValue((pending.Tick, item.Player, item.Name), out var queue) && queue.Count > 0)
            {
                var use = queue.Dequeue();
                if (use.TargetKind == SpecialPowerTargetKind.Location) item = item with { X = (int)use.X.Floor(), Y = (int)use.Y.Floor() };
            }
            events.Add(item);
        }
        var players = ImmutableArray.CreateBuilder<AnalysisPlayer>();
        foreach (var setup in _players)
        {
            var row = RowOf(new PlayerId(setup.Player));
            var counts = perMinute.GetValueOrDefault(setup.Player) ?? [];
            players.Add(new AnalysisPlayer(setup.Player, setup.Name, setup.Faction, setup.Team, setup.IsComputer, setup.Ai, _colors.GetValueOrDefault(setup.Player),
                winner?.Value == setup.Player, world.IsPlayerDefeated(new PlayerId(setup.Player)), row.Collected, row.Spent, row.Kills, row.KillsValue,
                Sorted(row.UnitsBuilt), Sorted(row.UnitsLost), Sorted(row.StructuresBuilt), Sorted(row.StructuresLost),
                row.StructuresSold, row.Upgrades, row.Sciences, row.SpecialPowers, row.Superweapons, counts.Sum(), [.. counts]));
        }
        var heat = ImmutableArray.CreateBuilder<AnalysisHeat>();
        heat.Add(new AnalysisHeat("fights", 0, [.. _fights]));
        foreach (var setup in _players) heat.Add(new AnalysisHeat("movement", setup.Player, [.. RowOf(new PlayerId(setup.Player)).Movement]));
        return new MatchAnalysis(mapPath, _ticksPerSecond, rules, Frame(StartTick), Frame(world.Tick), winner?.Value ?? 0, ending, source,
            (int)_extentX.Floor(), (int)_extentY.Floor(), players.ToImmutable(), [.. _samples], events.ToImmutable(), heat.ToImmutable(), Unresolved);
    }

    /// <summary>Highest value first, then most, then by name.</summary>
    private static ImmutableArray<AnalysisCount> Sorted(SortedDictionary<string, (int Count, long Value)> counts)
        => [..counts.Select(pair => new AnalysisCount(pair.Key, pair.Value.Count, pair.Value.Value))
            .OrderByDescending(item => item.Value).ThenByDescending(item => item.Count).ThenBy(item => item.Template, StringComparer.Ordinal)];
}
