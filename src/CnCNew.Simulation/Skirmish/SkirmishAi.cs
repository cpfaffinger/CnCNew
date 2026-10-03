using System.Collections.Immutable;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Random;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Skirmish;

/// <param name="Player">The player the AI plays for.</param>
/// <param name="Seed">Seed of the AI's private random stream (independent of the world's streams).</param>
/// <param name="HomeX">Centre of the base: the start position on the map.</param>
/// <param name="Side">Side of the player's faction; only objects of this side are built or produced.</param>
/// <param name="FamilyPrefix">Name prefix of the faction's own object variants (<c>AirF_America</c>); such variants are preferred over the shared ones of the side.</param>
/// <param name="HintPositions">Start positions of the other players; public map knowledge the AI may attack before it has seen anything.</param>
public sealed record SkirmishAiOptions(PlayerId Player, uint Seed, Fixed HomeX, Fixed HomeY, string Side, string FamilyPrefix, ImmutableArray<(Fixed X, Fixed Y)> HintPositions, int AttackThreshold = 0, int MaxAttackThreshold = 0);

/// <summary>Counters an AI keeps about itself; the match report and the tests read them.</summary>
public sealed class SkirmishAiStats
{
    public int Commands { get; internal set; }
    public int Rejected { get; internal set; }
    public int Constructions { get; internal set; }
    public int Resumes { get; internal set; }
    public int CancelledSites { get; internal set; }
    public int Productions { get; internal set; }
    public int Shots { get; internal set; }
    public int MoveOrders { get; internal set; }
    public int PowerUses { get; internal set; }
    public int SciencePurchases { get; internal set; }
    public int Repairs { get; internal set; }
    public int DockOrders { get; internal set; }
    public int Waves { get; internal set; }
    public int IdleThinks { get; internal set; }
    /// <summary>Units sent into an own tunnel network (to heal or to travel).</summary>
    public int TunnelEntries { get; internal set; }
    /// <summary>Evacuation orders to tunnels and transports.</summary>
    public int Evacuations { get; internal set; }
    /// <summary>Infantry sent into a transport.</summary>
    public int TransportLoads { get; internal set; }
    public long AttackTicks { get; internal set; }
    public SortedDictionary<CommandRejection, int> RejectionsByReason { get; } = [];
}

/// <summary>
/// A computer player that plays through typed commands only. It plans with what the player may know: its own objects, the
/// enemies and warehouses inside the vision the player currently has (<see cref="SimulationWorld.EntitiesVisibleTo"/>), what it
/// remembered of them, the map's start positions and its own exploration state. It never reads the state of an object it cannot see.
/// <para>
/// The structure of a skirmish AI follows <c>AIPlayer</c>/<c>AISkirmishPlayer</c> in outline (build order, base building by dozers, production
/// queues, army teams, attack waves, defence reactions) but none of its script tables: the build plan is derived from the
/// definitions themselves — roles from modules and <c>KindOf</c>, prerequisites from <c>Prerequisites</c> — and the army is one group that
/// masses, attacks in waves and falls back to defend the base. Which structure makes which unit comes from the command sets
/// (<see cref="CommandCatalog"/>); see <c>SIMULATION-CORE.md</c> for the documented limits.
/// </para>
/// </summary>
public sealed class SkirmishAi
{
    private const int FireInterval = 5;
    private const int ThinkInterval = 15;
    private const int RetryTicks = 300;
    private const int MemoryTicks = 900;
    private const int OrderRefreshTicks = 150;
    private const int MinAttackThreshold = 8;
    private const int MaxAttackThreshold = 30;
    private const int PlacementChecksPerThink = 400;
    private static readonly Fixed DefenseRadius = Fixed.FromInt(450);
    private static readonly Fixed EngageRadius = Fixed.FromInt(260);
    private static readonly Fixed RepairBelow = Fixed.FromDecimal(0.7m);
    private static readonly Fixed TunnelHealBelow = Fixed.FromDecimal(0.5m);
    private static readonly Fixed TunnelReach = Fixed.FromInt(600);
    private static readonly Fixed BoardingReach = Fixed.FromInt(250);
    private const int BoardingTicks = 300;

    [Flags]
    private enum Role
    {
        None = 0,
        Builder = 1,
        Carrier = 2,
        SupplyCenter = 4,
        Power = 8,
        Barracks = 16,
        Factory = 32,
        CommandCenter = 64,
        Defense = 128,
        Army = 256,
        Producer = 512,
        Warehouse = 1024,
        Tunnel = 2048,
        Transport = 4096,
    }

    private enum PlanState : byte { Impossible, Waiting, Ready }

    private readonly record struct Remembered(EntityId Id, ObjectTemplate Template, PlayerId Owner, Fixed X, Fixed Y, long Seen, bool IsStructure);
    private readonly record struct Goal(Fixed X, Fixed Y, long Tick);
    private readonly record struct SiteWatch(int Work, int Strikes, long LastResume);

    private readonly SimulationWorld _world;
    private readonly TemplateCatalog _catalog;
    private readonly Func<SimCommand, SubmitResult> _submit;
    private readonly SkirmishAiOptions _options;
    private readonly PlayerId _player;
    private readonly SageRandom _random;
    private readonly string _side;
    private readonly int _phase;
    private readonly Dictionary<int, Role> _roles = [];
    private readonly Dictionary<int, ImmutableArray<ObjectTemplate>> _producible = [];
    private readonly Dictionary<int, ImmutableArray<ObjectTemplate>> _structureCandidates = [];
    private readonly SortedSet<int> _builderTemplates = [];
    private int _builderTemplateCountAtCache = -1;
    private readonly SortedDictionary<uint, Remembered> _enemies = [];
    private readonly SortedDictionary<uint, Remembered> _warehouses = [];
    private readonly Dictionary<uint, long> _retryAt = [];
    private readonly Dictionary<(uint Source, string Power), long> _powerRetry = [];
    private readonly Dictionary<string, long> _scienceRetry = [];
    private readonly Dictionary<uint, Goal> _orders = [];
    private readonly Dictionary<uint, long> _idleSince = [];
    private readonly Dictionary<uint, SiteWatch> _siteWatch = [];
    private readonly HashSet<uint> _rallySet = [];
    private readonly List<(Fixed X, Fixed Y)> _badSpots = [];
    private readonly List<(Role Role, long Until)> _inFlight = [];
    private readonly Dictionary<(Fixed X, Fixed Y), long> _clearedHints = [];
    private ulong _sequence;
    private Fixed _budget;
    private long _tick;
    private List<Entity> _visible = [];
    private List<Entity> _units = [];
    private List<Entity> _structures = [];
    private List<Entity> _sites = [];
    private List<Entity> _enemyTargets = [];
    private bool _attacking;
    private int _threshold = MinAttackThreshold;
    private int _maxThreshold = MaxAttackThreshold;
    private (Fixed X, Fixed Y)? _rally;
    private (Fixed X, Fixed Y)? _scout;
    private long _scoutAt = -1;
    private bool _scoutRevisit;
    private readonly Dictionary<int, long> _seenAt = [];
    private readonly HashSet<int> _unreachable = [];
    private const int RevisitTicks = 3600;
    private readonly Dictionary<uint, long> _boarding = [];
    private (Fixed X, Fixed Y)? _armyGoal;

    public SkirmishAi(SimulationWorld world, TemplateCatalog catalog, SkirmishAiOptions options, Func<SimCommand, SubmitResult> submit)
    {
        _world = world;
        _catalog = catalog;
        _options = options;
        _submit = submit;
        _player = options.Player;
        _random = new SageRandom(options.Seed);
        _phase = options.Player.Value % FireInterval;
        _side = options.Side;
        if (options.AttackThreshold > 0) _threshold = options.AttackThreshold;
        if (options.MaxAttackThreshold > 0) _maxThreshold = Math.Max(options.MaxAttackThreshold, _threshold);
    }

    public SkirmishAiStats Stats { get; } = new();
    public bool Attacking => _attacking;
    public int KnownEnemyStructures => _enemies.Values.Count(value => value.IsStructure);

    /// <summary>Roles of the given template, for diagnostics.</summary>
    public string DescribeRoles(ObjectTemplate template) => RoleOf(template).ToString();

    /// <summary>The AI's turn for the world's current tick: call once per tick, before <see cref="SimulationWorld.Step"/>.</summary>
    public void Update()
    {
        _tick = _world.Tick;
        if (_tick == 0 || (_tick + _phase) % FireInterval != 0) return;
        if (_world.IsPlayerDefeated(_player)) return;
        Observe();
        Fire();
        if ((_tick + _phase) % ThinkInterval != 0) return;
        Think();
    }

    private void Think()
    {
        _budget = _world.MoneyOf(_player);
        _inFlight.RemoveAll(value => value.Until <= _tick);
        foreach (var expired in _boarding.Where(pair => pair.Value <= _tick).Select(pair => pair.Key).ToList()) _boarding.Remove(expired);
        var before = Stats.Commands;
        ProduceWorkers();
        BuildStructures();
        ManageSites();
        RepairStructures();
        BuyScience();
        UsePowers();
        ProduceArmy();
        SetRallyPoints();
        CommandArmy();
        UseTunnels();
        UseTransports();
        OrderCarriers();
        if (Stats.Commands == before) Stats.IdleThinks++;
        if (_attacking) Stats.AttackTicks += ThinkInterval;
    }

    // ------------------------------------------------------------------------------------------------------ command plumbing

    private SubmitResult Send(Func<ulong, SimCommand> make)
    {
        var command = make(++_sequence);
        var result = _submit(command);
        Stats.Commands++;
        if (!result.Accepted)
        {
            Stats.Rejected++;
            Stats.RejectionsByReason[result.Reason] = Stats.RejectionsByReason.GetValueOrDefault(result.Reason) + 1;
        }
        return result;
    }

    private bool Backoff(uint key, int ticks = RetryTicks)
    {
        if (_retryAt.TryGetValue(key, out var until) && until > _tick) return true;
        _retryAt[key] = _tick + ticks;
        return false;
    }

    // ------------------------------------------------------------------------------------------------------------ knowledge

    private void Observe()
    {
        _visible = _world.EntitiesVisibleTo(_player);
        _units.Clear();
        _structures.Clear();
        _sites.Clear();
        _enemyTargets.Clear();
        var seen = new HashSet<uint>();
        foreach (var entity in _visible)
        {
            if (entity.EffectivelyDead || entity.Template.IsKindOf("PROJECTILE")) continue;
            if (entity.Owner == _player)
            {
                if (entity.Template.IsKindOf("STRUCTURE"))
                {
                    if (entity.HasStatus(ObjectStatusBits.UnderConstruction)) _sites.Add(entity);
                    else if (!entity.HasStatus(ObjectStatusBits.Sold)) _structures.Add(entity);
                }
                else if (!entity.ContainedBy.IsValid && !entity.HasStatus(ObjectStatusBits.UnderConstruction)) _units.Add(entity);
                continue;
            }
            if (entity.Owner.IsSystem)
            {
                if ((RoleOf(entity.Template) & Role.Warehouse) != 0)
                {
                    seen.Add(entity.Id.Value);
                    _warehouses[entity.Id.Value] = new Remembered(entity.Id, entity.Template, entity.Owner, entity.X, entity.Y, _tick, false);
                }
                continue;
            }
            if (!_world.AreEnemies(_player, entity.Owner)) continue;
            seen.Add(entity.Id.Value);
            _enemies[entity.Id.Value] = new Remembered(entity.Id, entity.Template, entity.Owner, entity.X, entity.Y, _tick, entity.Template.IsKindOf("STRUCTURE"));
            if (entity.Body is not null) _enemyTargets.Add(entity);
        }
        Forget(_enemies, seen);
        Forget(_warehouses, seen);
    }

    /// <summary>Drops what the player would notice is gone: an entry whose cell is in sight without the object, or a mobile one not seen for a long time.</summary>
    private void Forget(SortedDictionary<uint, Remembered> memory, HashSet<uint> seen)
    {
        List<uint>? drop = null;
        foreach (var (id, entry) in memory)
        {
            if (seen.Contains(id)) continue;
            var stale = !entry.IsStructure && !entry.Owner.IsSystem && _tick - entry.Seen > MemoryTicks;
            if (stale || _world.IsPositionVisibleTo(_player, entry.X, entry.Y)) (drop ??= []).Add(id);
        }
        if (drop is not null)
            foreach (var id in drop) memory.Remove(id);
    }

    private Role RoleOf(ObjectTemplate template)
    {
        if (_roles.TryGetValue(template.Index, out var known)) return known;
        var role = Role.None;
        bool Has(string name) => template.Modules.Any(module => module.Name == name);
        if (Has("DozerAIUpdate") || Has("WorkerAIUpdate")) role |= Role.Builder;
        if (Has("SupplyTruckAIUpdate") || Has("WorkerAIUpdate") || Has("ChinookAIUpdate")) role |= Role.Carrier;
        if (Has("SupplyWarehouseDockUpdate")) role |= Role.Warehouse;
        if (Has("TunnelContain")) role |= Role.Tunnel;
        if (!template.IsKindOf("STRUCTURE") && (Has("TransportContain") || Has("HelixContain"))) role |= Role.Transport;
        if (template.IsKindOf("STRUCTURE"))
        {
            if (Has("SupplyCenterDockUpdate")) role |= Role.SupplyCenter;
            if (template.EnergyProduction > 0) role |= Role.Power;
            if (Has("ProductionUpdate")) role |= Role.Producer;
            if (template.IsKindOf("FS_BARRACKS")) role |= Role.Barracks;
            if (template.IsKindOf("FS_WARFACTORY")) role |= Role.Factory;
            if (template.IsKindOf("COMMANDCENTER")) role |= Role.CommandCenter;
            if (template.IsKindOf("FS_BASE_DEFENSE") && template.DefaultWeapon >= 0) role |= Role.Defense;
        }
        else if ((role & (Role.Builder | Role.Carrier)) == 0 && template.DefaultWeapon >= 0 && template.Locomotor is not null && template.BuildCost > Fixed.Zero
                 && (template.IsKindOf("INFANTRY") || template.IsKindOf("VEHICLE")))
            role |= Role.Army;
        _roles[template.Index] = role;
        return role;
    }

    private bool SameSide(ObjectTemplate template) => template.Side.Length == 0 || _side.Length == 0 || template.Side.Equals(_side, StringComparison.OrdinalIgnoreCase);

    private bool InFamily(ObjectTemplate template) => _options.FamilyPrefix.Length > 0 && template.Name.StartsWith(_options.FamilyPrefix, StringComparison.OrdinalIgnoreCase);

    private bool Available(ObjectTemplate template) => template.IsInstantiable && !_world.BuildableOf(template).Equals("No", StringComparison.OrdinalIgnoreCase) && template.BuildCost >= Fixed.Zero;

    /// <summary>Every unit the producer structure's command sets offer, own variants first.</summary>
    private ImmutableArray<ObjectTemplate> ProducibleBy(ObjectTemplate producer)
    {
        if (_producible.TryGetValue(producer.Index, out var known)) return known;
        var list = new List<ObjectTemplate>();
        if ((RoleOf(producer) & Role.Producer) != 0)
            foreach (var set in producer.CommandSets)
                foreach (var name in _catalog.Commands.BuildsOf(set))
                    if (_catalog.TryGet(name, out var template) && !template.IsKindOf("STRUCTURE") && Available(template) && template.BuildTicks > 0 && template.BuildCost > Fixed.Zero && !list.Contains(template))
                        list.Add(template);
        var result = list.OrderBy(value => InFamily(value) ? 0 : 1).ThenBy(value => value.Name, StringComparer.Ordinal).ToImmutableArray();
        _producible[producer.Index] = result;
        return result;
    }

    private bool PrerequisitesMet(ObjectTemplate template)
    {
        if (_world.BuildableOf(template).Equals("Ignore_Prerequisites", StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var line in template.Prerequisites)
        {
            if (line.IsScience)
            {
                var owned = _world.PlayerSciences(_player);
                foreach (var name in line.Alternatives)
                    if (!_catalog.Sciences.TryGet(name, out var science) || !owned.Has(science.Index)) return false;
                continue;
            }
            if (!line.Alternatives.Any(name => _structures.Any(value => value.Template.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))) return false;
        }
        return true;
    }

    private static Fixed DistanceSquared(Fixed ax, Fixed ay, Fixed bx, Fixed by)
    {
        var dx = ax - bx;
        var dy = ay - by;
        return dx * dx + dy * dy;
    }

    private static AIUpdateInterface? AiOf(Entity entity)
    {
        foreach (var module in entity.Modules)
            if (module is AIUpdateInterface ai) return ai;
        return null;
    }

    // ------------------------------------------------------------------------------------------------------------ economy

    private int CountAlive(Role role, bool includeSites)
    {
        var count = 0;
        foreach (var entity in _structures) if ((RoleOf(entity.Template) & role) != 0) count++;
        if (includeSites) foreach (var entity in _sites) if ((RoleOf(entity.Template) & role) != 0) count++;
        foreach (var entity in _units) if ((RoleOf(entity.Template) & role) != 0) count++;
        return count;
    }

    private int InFlight(Role role) => _inFlight.Count(value => (value.Role & role) != 0);

    /// <summary>Builders first, then supply carriers: the base cannot grow or earn without them.</summary>
    private void ProduceWorkers()
    {
        var builders = CountAlive(Role.Builder, false) + InFlight(Role.Builder);
        var supply = CountAlive(Role.SupplyCenter, false);
        var wantBuilders = supply > 0 || _structures.Count > 1 ? 2 : 1;
        if (builders < wantBuilders) QueueWorker(Role.Builder);
        var carriers = CountAlive(Role.Carrier, false) + InFlight(Role.Carrier);
        var wantCarriers = _warehouses.Count == 0 && CountAlive(Role.Carrier, false) > 0 ? 0 : Math.Min(6, 2 * Math.Max(0, supply));
        if (carriers < wantCarriers) QueueWorker(Role.Carrier);
    }

    private void QueueWorker(Role wanted)
    {
        foreach (var producer in _structures)
        {
            var production = ProductionOf(producer);
            if (production is null || !production.HasRoom) continue;
            foreach (var template in ProducibleBy(producer.Template))
            {
                if ((RoleOf(template) & wanted) == 0 || template.BuildCost > _budget || !PrerequisitesMet(template)) continue;
                if (Backoff(unchecked(producer.Id.Value * 31u + (uint)template.Index + 1_000_000u), 60)) continue;
                var result = Send(sequence => new QueueProductionCommand(_player, sequence, _tick, producer.Id, template.Name));
                if (!result.Accepted) continue;
                _budget -= template.BuildCost;
                _inFlight.Add((RoleOf(template), _tick + template.BuildTicks + 30));
                Stats.Productions++;
                return;
            }
        }
    }

    private static ProductionUpdate? ProductionOf(Entity entity)
    {
        foreach (var module in entity.Modules)
            if (module is ProductionUpdate production) return production;
        return null;
    }

    private Role[] PlanGoals()
    {
        var goals = new List<Role>();
        var army = _units.Count(value => (RoleOf(value.Template) & Role.Army) != 0);
        var producers = CountAlive(Role.Producer, true);
        if (CountAlive(Role.CommandCenter, true) == 0 && CountAlive(Role.Builder, false) > 0) goals.Add(Role.CommandCenter);
        if (CountAlive(Role.SupplyCenter, true) < 1) goals.Add(Role.SupplyCenter);
        if (CountAlive(Role.Power, true) < 1) goals.Add(Role.Power);
        if (CountAlive(Role.Barracks, true) < 1) goals.Add(Role.Barracks);
        if (CountAlive(Role.Factory, true) < 1) goals.Add(Role.Factory);
        if (CountAlive(Role.SupplyCenter, true) < 2 && army >= 10 && _warehouses.Count > 1) goals.Add(Role.SupplyCenter);
        if (CountAlive(Role.Power, true) < 2 && producers >= 3) goals.Add(Role.Power);
        if (CountAlive(Role.Barracks, true) < 2 && _budget > Fixed.FromInt(1200)) goals.Add(Role.Barracks);
        if (CountAlive(Role.Factory, true) < 2 && _budget > Fixed.FromInt(1500)) goals.Add(Role.Factory);
        if (CountAlive(Role.Defense, true) < Math.Min(4, army / 6)) goals.Add(Role.Defense);
        return [..goals];
    }

    private ImmutableArray<ObjectTemplate> StructureCandidates(Role role)
    {
        var builders = _units.Where(value => (RoleOf(value.Template) & Role.Builder) != 0).Select(value => value.Template).Distinct().ToList();
        foreach (var template in builders) _builderTemplates.Add(template.Index);
        if (_builderTemplates.Count != _builderTemplateCountAtCache) { _structureCandidates.Clear(); _builderTemplateCountAtCache = _builderTemplates.Count; }
        if (_structureCandidates.TryGetValue((int)role, out var known)) return known;
        var offering = _builderTemplates.Select(index => _catalog[index]).ToList();
        var result = _catalog.Templates.Where(value => value.IsKindOf("STRUCTURE") && Available(value) && SameSide(value) && (RoleOf(value) & role) != 0 && value.BuildCost > Fixed.Zero
                && offering.Any(builder => _catalog.OffersBuild(builder, value.Name)))
            .OrderBy(value => InFamily(value) ? 0 : 1).ThenBy(value => value.BuildCost.Raw).ThenBy(value => value.Name, StringComparer.Ordinal).ToImmutableArray();
        _structureCandidates[(int)role] = result;
        return result;
    }

    /// <summary>
    /// Resolves the structure to raise next so that <paramref name="target"/> becomes buildable: the target itself, the first missing
    /// prerequisite (recursively), or nothing while a prerequisite is already on its way.
    /// </summary>
    private PlanState Plan(ObjectTemplate target, int depth, out ObjectTemplate? buildNow)
    {
        buildNow = null;
        if (PrerequisitesMet(target))
        {
            buildNow = target;
            return PlanState.Ready;
        }
        if (depth >= 4) return PlanState.Impossible;
        foreach (var line in target.Prerequisites)
        {
            if (line.IsScience)
            {
                var owned = _world.PlayerSciences(_player);
                if (line.Alternatives.All(name => _catalog.Sciences.TryGet(name, out var science) && owned.Has(science.Index))) continue;
                return PlanState.Impossible;
            }
            if (line.Alternatives.Any(name => _structures.Any(value => value.Template.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))) continue;
            if (line.Alternatives.Any(name => _sites.Any(value => value.Template.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))) return PlanState.Waiting;
            var alternatives = line.Alternatives.Select(name => _catalog.TryGet(name, out var found) ? found : null).OfType<ObjectTemplate>()
                .Where(value => value.IsKindOf("STRUCTURE") && Available(value) && SameSide(value)).OrderBy(value => InFamily(value) ? 0 : 1).ThenBy(value => value.Name, StringComparer.Ordinal);
            foreach (var alternative in alternatives)
            {
                var state = Plan(alternative, depth + 1, out buildNow);
                if (state != PlanState.Impossible) return state;
            }
            return PlanState.Impossible;
        }
        return PlanState.Impossible;
    }

    private void BuildStructures()
    {
        var goals = PlanGoals();
        if (goals.Length == 0) return;
        var used = new HashSet<uint>();
        var reserved = Fixed.Zero;
        foreach (var role in goals)
        {
            var builder = PickBuilder(used);
            if (builder is null) break;
            ObjectTemplate? next = null;
            foreach (var candidate in StructureCandidates(role))
            {
                var state = Plan(candidate, 0, out next);
                if (state == PlanState.Impossible) continue;
                if (state == PlanState.Waiting) next = null;
                break;
            }
            if (next is null) continue;
            if (next.BuildCost > _budget)
            {
                reserved = next.BuildCost;
                break;
            }
            if (Backoff(unchecked(0x40000000u + (uint)next.Index), RetryTicks)) continue;
            var spot = FindPlacement(next, builder);
            if (spot is null) continue;
            var (x, y) = spot.Value;
            var result = Send(sequence => new ConstructCommand(_player, sequence, _tick, builder.Id, next.Name, x, y, Fixed.Zero));
            if (!result.Accepted) continue;
            used.Add(builder.Id.Value);
            _budget -= next.BuildCost;
            Stats.Constructions++;
        }
        _reserve = reserved;
    }

    private Fixed _reserve;

    /// <summary>An available builder: not on a construction task, and idle unless it is a worker that also gathers (then the gathering yields).</summary>
    private Entity? PickBuilder(HashSet<uint> used, Entity? near = null)
    {
        Entity? best = null;
        var bestDistance = Fixed.MaxValue;
        foreach (var entity in _units)
        {
            if ((RoleOf(entity.Template) & Role.Builder) == 0 || used.Contains(entity.Id.Value) || AiOf(entity) is not { } ai || ai.IsBuilding) continue;
            if (!ai.IsIdle && (RoleOf(entity.Template) & Role.Carrier) == 0) continue;
            var distance = near is null ? Fixed.Zero : DistanceSquared(entity.X, entity.Y, near.X, near.Y);
            if (best is null || distance < bestDistance) { best = entity; bestDistance = distance; }
        }
        return best;
    }

    /// <summary>The first legal footprint on rings around the base that keeps a gap to every own structure and to earlier failures.</summary>
    private (Fixed X, Fixed Y)? FindPlacement(ObjectTemplate template, Entity builder)
    {
        var radius = template.BoundingCircleRadius;
        var checks = 0;
        for (var ring = 50; ring <= 500; ring += 25)
        {
            var steps = Math.Max(8, ring / 8);
            var offset = _random.Range(0, steps - 1);
            for (var step = 0; step < steps; step++)
            {
                var angle = FixedTrig.TwoPi * ((step + offset) % steps) / Fixed.FromInt(steps);
                var x = _options.HomeX + FixedTrig.Cos(angle) * Fixed.FromInt(ring);
                var y = _options.HomeY + FixedTrig.Sin(angle) * Fixed.FromInt(ring);
                if (++checks > PlacementChecksPerThink) return null;
                if (!SpotFree(template, radius, x, y)) continue;
                if (_world.Navigation is { } navigation && !navigation.IsFootprintLegal(template, x, y, Fixed.Zero)) continue;
                if (!_world.IsPositionExploredBy(_player, x, y)) continue;
                if (_world.Navigation is { } grid && !Reachable(grid, builder, x, y)) continue;
                return (x, y);
            }
        }
        return null;
    }

    private bool Reachable(NavigationGrid grid, Entity builder, Fixed x, Fixed y)
    {
        var surfaces = builder.Template.Locomotor?.Surfaces ?? LocomotorSurfaces.Ground;
        return grid.FindPath(builder.X, builder.Y, x, y, surfaces).Status == PathStatus.Found;
    }

    private bool SpotFree(ObjectTemplate template, Fixed radius, Fixed x, Fixed y)
    {
        var gap = Fixed.FromInt(12);
        foreach (var entity in _structures)
            if (Clash(entity, radius, gap, x, y)) return false;
        foreach (var entity in _sites)
            if (Clash(entity, radius, gap, x, y)) return false;
        foreach (var (spotX, spotY) in _badSpots)
            if (DistanceSquared(spotX, spotY, x, y) < Fixed.FromInt(900)) return false;
        foreach (var warehouse in _warehouses.Values)
            if (DistanceSquared(warehouse.X, warehouse.Y, x, y) < Fixed.FromInt(3600)) return false;
        return true;
    }

    private static bool Clash(Entity entity, Fixed radius, Fixed gap, Fixed x, Fixed y)
    {
        var limit = entity.Template.BoundingCircleRadius + radius + gap;
        return DistanceSquared(entity.X, entity.Y, x, y) < limit * limit;
    }

    /// <summary>Sites whose builder stopped are resumed; a site that does not progress after repeated resumes is cancelled and its spot avoided.</summary>
    private void ManageSites()
    {
        var used = new HashSet<uint>();
        foreach (var site in _sites)
        {
            var builder = _world.FindEntity(site.BuilderId);
            var working = builder is not null && !builder.DestroyRequested && !builder.EffectivelyDead && builder.Owner == _player && AiOf(builder) is { IsBuilding: true };
            if (working) continue;
            var watch = _siteWatch.GetValueOrDefault(site.Id.Value);
            if (watch.LastResume > 0 && _tick - watch.LastResume < 60) continue;
            var strikes = watch.LastResume > 0 && site.ConstructionWork == watch.Work ? watch.Strikes + 1 : 0;
            if (strikes >= 3)
            {
                Send(sequence => new CancelConstructCommand(_player, sequence, _tick, site.Id));
                _badSpots.Add((site.X, site.Y));
                _siteWatch.Remove(site.Id.Value);
                Stats.CancelledSites++;
                continue;
            }
            var candidate = PickBuilder(used, site);
            if (candidate is null) continue;
            var result = Send(sequence => new ResumeConstructCommand(_player, sequence, _tick, candidate.Id, site.Id));
            _siteWatch[site.Id.Value] = new SiteWatch(site.ConstructionWork, strikes, _tick);
            if (!result.Accepted) continue;
            used.Add(candidate.Id.Value);
            Stats.Resumes++;
        }
    }

    private void RepairStructures()
    {
        foreach (var structure in _structures)
        {
            if (structure.Body is not { } body || body.Health >= body.MaxHealth * RepairBelow) continue;
            var builder = PickBuilder([], structure);
            if (builder is null || AiOf(builder) is not { CanRepair: true }) return;
            if (Backoff(unchecked(0x50000000u + structure.Id.Value))) continue;
            if (Send(sequence => new RepairCommand(_player, sequence, _tick, builder.Id, structure.Id)).Accepted) Stats.Repairs++;
            return;
        }
    }

    private void BuyScience()
    {
        var points = _world.SciencePointsOf(_player);
        if (points <= 0) return;
        var owned = _world.PlayerSciences(_player);
        ScienceInfo? pick = null;
        foreach (var science in _catalog.Sciences.Sciences)
        {
            if (science.PurchasePointCost <= 0 || science.PurchasePointCost > points || owned.Has(science.Index) || !_catalog.Sciences.PrerequisitesSatisfied(owned, science)) continue;
            if (_scienceRetry.TryGetValue(science.Name, out var until) && until > _tick) continue;
            if (pick is null || science.PurchasePointCost < pick.PurchasePointCost) pick = science;
        }
        if (pick is null) return;
        _scienceRetry[pick.Name] = _tick + 1800;
        if (Send(sequence => new PurchaseScienceCommand(_player, sequence, _tick, pick.Name)).Accepted) Stats.SciencePurchases++;
    }

    /// <summary>
    /// Every ready special power is used against a visible enemy object or a remembered enemy structure. Powers are not classified as
    /// offensive or supportive (no data says so); a use the rules refuse is counted and retried much later.
    /// </summary>
    private void UsePowers()
    {
        foreach (var entity in _structures.Concat(_units))
        {
            foreach (var module in entity.Modules)
            {
                if (module is not ISpecialPowerModule power || power.IsPaused || !power.IsReady(_world)) continue;
                var info = _world.SpecialPowers[power.PowerIndex];
                if (info.RequiredScience >= 0 && !_world.PlayerSciences(_player).Has(info.RequiredScience)) continue;
                if (_powerRetry.TryGetValue((entity.Id.Value, info.Name), out var until) && until > _tick) continue;
                var command = MakePower(entity, info.Name, power);
                if (command is null) continue;
                var result = Send(command);
                _powerRetry[(entity.Id.Value, info.Name)] = _tick + (result.Accepted ? RetryTicks : result.Reason == CommandRejection.MissingScience ? 3000 : 900);
                if (result.Accepted) Stats.PowerUses++;
            }
        }
    }

    private Func<ulong, SimCommand>? MakePower(Entity source, string power, ISpecialPowerModule module)
    {
        if (module.AcceptsTarget(SpecialPowerTargetKind.None))
            return sequence => new UseSpecialPowerCommand(_player, sequence, _tick, source.Id, power, SpecialPowerTargetKind.None, EntityId.Invalid, Fixed.Zero, Fixed.Zero);
        if (module.AcceptsTarget(SpecialPowerTargetKind.Object))
        {
            var target = _enemyTargets.OrderByDescending(value => value.Template.IsKindOf("STRUCTURE")).ThenBy(value => value.Id.Value).FirstOrDefault();
            if (target is not null)
                return sequence => new UseSpecialPowerCommand(_player, sequence, _tick, source.Id, power, SpecialPowerTargetKind.Object, target.Id, Fixed.Zero, Fixed.Zero);
        }
        if (module.AcceptsTarget(SpecialPowerTargetKind.Location))
        {
            var target = _enemies.Values.Where(value => value.IsStructure).OrderByDescending(value => value.Template.BuildCost.Raw).ThenBy(value => value.Id.Value).Cast<Remembered?>().FirstOrDefault();
            if (target is { } known)
                return sequence => new UseSpecialPowerCommand(_player, sequence, _tick, source.Id, power, SpecialPowerTargetKind.Location, EntityId.Invalid, known.X, known.Y);
        }
        return null;
    }

    // ------------------------------------------------------------------------------------------------------------ army

    private void ProduceArmy()
    {
        var armyCount = _units.Count(value => (RoleOf(value.Template) & Role.Army) != 0);
        var spendable = armyCount < 4 ? _budget : _budget - _reserve;
        foreach (var producer in _structures)
        {
            if ((RoleOf(producer.Template) & (Role.Barracks | Role.Factory)) == 0) continue;
            var production = ProductionOf(producer);
            if (production is null || !production.HasRoom) continue;
            var candidates = ProducibleBy(producer.Template).Where(value => (RoleOf(value) & Role.Army) != 0 && value.BuildCost <= spendable && PrerequisitesMet(value)).ToList();
            if (candidates.Count == 0) continue;
            if (candidates.Any(InFamily)) candidates = candidates.Where(InFamily).ToList();
            var pick = candidates[_random.Range(0, candidates.Count - 1)];
            var result = Send(sequence => new QueueProductionCommand(_player, sequence, _tick, producer.Id, pick.Name));
            if (!result.Accepted) continue;
            spendable -= pick.BuildCost;
            _budget -= pick.BuildCost;
            Stats.Productions++;
        }
    }

    private void SetRallyPoints()
    {
        _rally ??= ChooseRally();
        if (_rally is not { } rally) return;
        foreach (var producer in _structures)
        {
            if ((RoleOf(producer.Template) & (Role.Barracks | Role.Factory)) == 0 || !_rallySet.Add(producer.Id.Value)) continue;
            Send(sequence => new SetRallyPointCommand(_player, sequence, _tick, producer.Id, rally.X, rally.Y));
        }
    }

    /// <summary>A passable point between the base and the nearest opponent's start.</summary>
    private (Fixed X, Fixed Y)? ChooseRally()
    {
        (Fixed X, Fixed Y)? nearest = null;
        var best = Fixed.MaxValue;
        foreach (var hint in _options.HintPositions)
        {
            var distance = DistanceSquared(hint.X, hint.Y, _options.HomeX, _options.HomeY);
            if (distance < best) { best = distance; nearest = hint; }
        }
        if (nearest is not { } toward) return null;
        var dx = toward.X - _options.HomeX;
        var dy = toward.Y - _options.HomeY;
        var length = Fixed.Sqrt(dx * dx + dy * dy);
        if (length <= Fixed.Zero) return null;
        for (var distance = 180; distance >= 60; distance -= 20)
        {
            var x = _options.HomeX + dx / length * Fixed.FromInt(distance);
            var y = _options.HomeY + dy / length * Fixed.FromInt(distance);
            if (_world.Navigation is not { } grid || grid.IsPassable(TerrainMap.CellOf(x), TerrainMap.CellOf(y), LocomotorSurfaces.Ground) && !grid.IsBlockedByObstacle(TerrainMap.CellOf(x), TerrainMap.CellOf(y)))
                return (x, y);
        }
        return null;
    }

    private void Fire()
    {
        if (_enemyTargets.Count == 0) return;
        foreach (var entity in _structures.Concat(_units))
        {
            if (entity.Template.DefaultWeapon < 0 || _tick < entity.WeaponReadyTick) continue;
            if (entity.Template.IsKindOf("STRUCTURE") && (RoleOf(entity.Template) & Role.Defense) == 0) continue;
            var range = _world.Weapons[entity.Template.DefaultWeapon].RangeWith(_catalog.GlobalWeaponBonus, entity.WeaponBonusConditions);
            if (range <= Fixed.Zero) continue;
            var limit = range * range;
            Entity? best = null;
            var bestDistance = Fixed.MaxValue;
            foreach (var target in _enemyTargets)
            {
                var distance = DistanceSquared(entity.X, entity.Y, target.X, target.Y);
                if (distance > limit || distance >= bestDistance) continue;
                best = target;
                bestDistance = distance;
            }
            if (best is null) continue;
            if (Send(sequence => new FireWeaponCommand(_player, sequence, _tick, entity.Id, best.Id)).Accepted) Stats.Shots++;
        }
    }

    private void CommandArmy()
    {
        var army = _units.Where(value => (RoleOf(value.Template) & Role.Army) != 0 && AiOf(value) is not null).ToList();
        if (army.Count == 0)
        {
            _attacking = false;
            return;
        }
        (Fixed X, Fixed Y)? goal;
        var threat = FindThreat();
        if (threat is not null) goal = threat;
        else
        {
            if (!_attacking && army.Count >= _threshold)
            {
                _attacking = true;
                Stats.Waves++;
            }
            else if (_attacking && army.Count < Math.Max(3, _threshold / 3))
            {
                _attacking = false;
                _threshold = Math.Min(_maxThreshold, _threshold + 2);
            }
            goal = _attacking ? AttackTarget(army) : _rally;
        }
        _armyGoal = goal;
        if (goal is not { } destination) return;
        foreach (var unit in army)
        {
            if (_boarding.TryGetValue(unit.Id.Value, out var boardingUntil) && boardingUntil > _tick) continue;
            var (x, y) = MoveGoalFor(unit, destination);
            if (DistanceSquared(unit.X, unit.Y, x, y) < Fixed.FromInt(1600)) continue;
            if (_orders.TryGetValue(unit.Id.Value, out var order))
            {
                var same = DistanceSquared(order.X, order.Y, x, y) < Fixed.FromInt(900);
                if (same && (unit.HasMoveTarget || _tick - order.Tick < OrderRefreshTicks)) continue;
                if (!same && unit.HasMoveTarget && _tick - order.Tick < 60) continue;
            }
            if (Send(sequence => new SetMoveTargetCommand(_player, sequence, _tick, unit.Id, x, y)).Accepted) Stats.MoveOrders++;
            _orders[unit.Id.Value] = new Goal(x, y, _tick);
        }
    }

    /// <summary>
    /// GLA tunnel networks (<c>TunnelContain</c>): a badly hurt army unit near an own tunnel walks in (the network heals it, <c>TimeForFullHeal</c>), and
    /// once everybody inside is whole again the network empties through the tunnel nearest to where the army is heading — so the tunnels double as
    /// a shortcut between the base and a forward defence. Uses only own objects and the own network.
    /// </summary>
    private void UseTunnels()
    {
        Entity? Nearest(List<Entity> candidates, Fixed x, Fixed y, Fixed reach)
        {
            Entity? best = null;
            var bestDistance = reach * reach;
            foreach (var candidate in candidates)
            {
                var distance = DistanceSquared(candidate.X, candidate.Y, x, y);
                if (distance < bestDistance) { best = candidate; bestDistance = distance; }
            }
            return best;
        }

        var tunnels = _structures.Where(value => (RoleOf(value.Template) & Role.Tunnel) != 0 && value.Container is not null).ToList();
        if (tunnels.Count == 0) return;
        var network = _world.TunnelSystemOf(_player);
        var room = _catalog.MaxTunnelCapacity - network.ContainCount - _boarding.Count(pair => pair.Value > _tick);
        foreach (var unit in _units)
        {
            if (room <= 0) break;
            if ((RoleOf(unit.Template) & Role.Army) == 0 || unit.Template.IsKindOf("AIRCRAFT") || unit.Body is not { } body || body.Health >= body.MaxHealth * TunnelHealBelow) continue;
            if (_boarding.TryGetValue(unit.Id.Value, out var until) && until > _tick) continue;
            var tunnel = Nearest(tunnels, unit.X, unit.Y, TunnelReach);
            if (tunnel is null || Backoff(unchecked(0x80000000u + unit.Id.Value), RetryTicks)) continue;
            if (!Send(sequence => new MoveAndEnterCommand(_player, sequence, _tick, unit.Id, tunnel.Id)).Accepted) continue;
            _boarding[unit.Id.Value] = _tick + BoardingTicks;
            _orders.Remove(unit.Id.Value);
            Stats.TunnelEntries++;
            room--;
        }
        if (network.ContainCount == 0 || Backoff(0x81000000u, 90)) return;
        foreach (var riderId in network.Riders)
            if (_world.FindEntity(riderId) is { Body: { } riderBody } && riderBody.Health < riderBody.MaxHealth) return;
        var (goalX, goalY) = _armyGoal ?? _rally ?? (_options.HomeX, _options.HomeY);
        var exit = Nearest(tunnels, goalX, goalY, Fixed.FromInt(100_000));
        if (exit is not null && Send(sequence => new EvacuateCommand(_player, sequence, _tick, exit.Id, false, Fixed.Zero, Fixed.Zero)).Accepted) Stats.Evacuations++;
    }

    /// <summary>
    /// Transports of the army (<c>TransportContain</c>/<c>HelixContain</c> on an armed unit, e.g. Humvee, Listening Outpost): when a wave marches,
    /// nearby infantry boards; a loaded transport that sees enemies or reaches the goal lets its passengers out there.
    /// </summary>
    private void UseTransports()
    {
        foreach (var transport in _units)
        {
            var role = RoleOf(transport.Template);
            if ((role & Role.Transport) == 0 || (role & Role.Army) == 0 || transport.Container is not { ContainCount: > 0 }) continue;
            var arrived = _armyGoal is { } goal && DistanceSquared(transport.X, transport.Y, goal.X, goal.Y) < Fixed.FromInt(90_000);
            var engaged = _enemyTargets.Any(enemy => DistanceSquared(enemy.X, enemy.Y, transport.X, transport.Y) < EngageRadius * EngageRadius);
            if (!arrived && !engaged) continue;
            if (Backoff(unchecked(0x82000000u + transport.Id.Value), 60)) continue;
            if (Send(sequence => new EvacuateCommand(_player, sequence, _tick, transport.Id, false, Fixed.Zero, Fixed.Zero)).Accepted) Stats.Evacuations++;
        }
        if (!_attacking) return;
        foreach (var transport in _units)
        {
            var role = RoleOf(transport.Template);
            if ((role & Role.Transport) == 0 || (role & Role.Army) == 0 || transport.Container is not { } contain) continue;
            if (contain.ContainMax >= 0 && contain.ContainCount >= contain.ContainMax) continue;
            Entity? pick = null;
            var best = BoardingReach * BoardingReach;
            foreach (var unit in _units)
            {
                if (!unit.Template.IsKindOf("INFANTRY") || (RoleOf(unit.Template) & Role.Army) == 0 || unit.Template.TransportSlotCount <= 0) continue;
                if (_boarding.TryGetValue(unit.Id.Value, out var until) && until > _tick) continue;
                var distance = DistanceSquared(unit.X, unit.Y, transport.X, transport.Y);
                if (distance >= best || !contain.IsValidContainerFor(_world, unit, true)) continue;
                best = distance;
                pick = unit;
            }
            if (pick is null || Backoff(unchecked(0x83000000u + pick.Id.Value), RetryTicks)) continue;
            if (!Send(sequence => new MoveAndEnterCommand(_player, sequence, _tick, pick.Id, transport.Id)).Accepted) continue;
            _boarding[pick.Id.Value] = _tick + BoardingTicks;
            _orders.Remove(pick.Id.Value);
            Stats.TransportLoads++;
        }
    }

    /// <summary>The unit walks to the nearest enemy it sees within engagement distance, otherwise to the group goal.</summary>
    private (Fixed X, Fixed Y) MoveGoalFor(Entity unit, (Fixed X, Fixed Y) destination)
    {
        Entity? nearest = null;
        var best = EngageRadius * EngageRadius;
        foreach (var target in _enemyTargets)
        {
            var distance = DistanceSquared(unit.X, unit.Y, target.X, target.Y);
            if (distance < best) { best = distance; nearest = target; }
        }
        if (nearest is null) return destination;
        var weapon = unit.Template.DefaultWeapon;
        var range = weapon >= 0 ? _world.Weapons[weapon].RangeWith(_catalog.GlobalWeaponBonus, unit.WeaponBonusConditions) : Fixed.Zero;
        var distanceToTarget = Fixed.Sqrt(best);
        var stand = range * Fixed.FromDecimal(0.7m);
        if (distanceToTarget <= stand || distanceToTarget <= Fixed.Zero) return (unit.X, unit.Y);
        var ratio = (distanceToTarget - stand) / distanceToTarget;
        return (unit.X + (nearest.X - unit.X) * ratio, unit.Y + (nearest.Y - unit.Y) * ratio);
    }

    /// <summary>The visible enemy closest to the base among those near any of the AI's structures: the army drops its plans and defends.</summary>
    private (Fixed X, Fixed Y)? FindThreat()
    {
        Entity? nearest = null;
        var best = DefenseRadius * DefenseRadius;
        foreach (var enemy in _enemyTargets)
        {
            if (enemy.Template.IsKindOf("STRUCTURE")) continue;
            foreach (var structure in _structures)
            {
                var distance = DistanceSquared(enemy.X, enemy.Y, structure.X, structure.Y);
                if (distance >= best) continue;
                best = distance;
                nearest = enemy;
            }
        }
        return nearest is null ? null : (nearest.X, nearest.Y);
    }

    private (Fixed X, Fixed Y)? AttackTarget(List<Entity> army)
    {
        var cx = Fixed.Zero;
        var cy = Fixed.Zero;
        foreach (var unit in army) { cx += unit.X; cy += unit.Y; }
        cx /= Fixed.FromInt(army.Count);
        cy /= Fixed.FromInt(army.Count);

        Remembered? target = null;
        var best = Fixed.MaxValue;
        foreach (var entry in _enemies.Values)
        {
            if (!entry.IsStructure) continue;
            var distance = DistanceSquared(entry.X, entry.Y, cx, cy);
            if (distance < best) { best = distance; target = entry; }
        }
        if (target is { } structure) return (structure.X, structure.Y);
        foreach (var entry in _enemies.Values)
        {
            var distance = DistanceSquared(entry.X, entry.Y, cx, cy);
            if (distance < best) { best = distance; target = entry; }
        }
        if (target is { } unitEntry) return (unitEntry.X, unitEntry.Y);

        foreach (var hint in _options.HintPositions)
        {
            if (_clearedHints.TryGetValue(hint, out var clearedAt) && _tick - clearedAt < RevisitTicks) continue;
            if (_world.IsPositionVisibleTo(_player, hint.X, hint.Y) && DistanceSquared(hint.X, hint.Y, cx, cy) < Fixed.FromInt(90_000)) { _clearedHints[hint] = _tick; continue; }
            var distance = DistanceSquared(hint.X, hint.Y, cx, cy);
            if (distance < best) { best = distance; target = new Remembered(EntityId.Invalid, army[0].Template, PlayerId.System, hint.X, hint.Y, _tick, false); }
        }
        if (target is { } hintEntry) return (hintEntry.X, hintEntry.Y);
        return Scout(cx, cy);
    }

    /// <summary>
    /// Nothing known to attack: sweep toward the nearest cell of a coarse grid the player has never seen and, once the whole map is
    /// explored, toward the nearest one it has not had in sight for a while (survivors wander and hide). A target that is not
    /// reached within its time is written off as unreachable.
    /// </summary>
    private (Fixed X, Fixed Y)? Scout(Fixed cx, Fixed cy)
    {
        if (_world.Navigation is not { } grid) return null;
        StampVisibleCells(grid);
        if (_scout is { } current)
        {
            var key = CoarseKey(grid, current);
            var reached = _world.IsPositionExploredBy(_player, current.X, current.Y) && (!_scoutRevisit || _seenAt.GetValueOrDefault(key, -1) >= _scoutAt);
            if (!reached && _tick - _scoutAt < 450) return current;
            if (!reached) _unreachable.Add(key);
        }
        _scoutRevisit = false;
        _scout = NearestUnexplored(grid, cx, cy);
        if (_scout is null)
        {
            _scout = NearestStale(grid, cx, cy);
            _scoutRevisit = _scout is not null;
        }
        _scoutAt = _tick;
        return _scout;
    }

    private static int CoarseKey(NavigationGrid grid, (Fixed X, Fixed Y) point) => TerrainMap.CellOf(point.Y) / 8 * grid.Terrain.CellsX + TerrainMap.CellOf(point.X) / 8;

    private void StampVisibleCells(NavigationGrid grid)
    {
        var terrain = grid.Terrain;
        for (var y = 4; y < terrain.CellsY; y += 8)
            for (var x = 4; x < terrain.CellsX; x += 8)
                if (_world.IsPositionVisibleTo(_player, Fixed.FromInt(x * TerrainMap.CellSize + TerrainMap.CellSize / 2), Fixed.FromInt(y * TerrainMap.CellSize + TerrainMap.CellSize / 2)))
                    _seenAt[y / 8 * terrain.CellsX + x / 8] = _tick;
    }

    private (Fixed X, Fixed Y)? NearestStale(NavigationGrid grid, Fixed cx, Fixed cy)
    {
        var terrain = grid.Terrain;
        (Fixed X, Fixed Y)? pick = null;
        var best = Fixed.MaxValue;
        for (var y = 4; y < terrain.CellsY; y += 8)
            for (var x = 4; x < terrain.CellsX; x += 8)
            {
                var key = y / 8 * terrain.CellsX + x / 8;
                if (_unreachable.Contains(key) || _tick - _seenAt.GetValueOrDefault(key, long.MinValue / 2) < RevisitTicks || !grid.IsPassable(x, y, LocomotorSurfaces.Ground)) continue;
                var wx = Fixed.FromInt(x * TerrainMap.CellSize + TerrainMap.CellSize / 2);
                var wy = Fixed.FromInt(y * TerrainMap.CellSize + TerrainMap.CellSize / 2);
                var distance = DistanceSquared(wx, wy, cx, cy);
                if (distance < best) { best = distance; pick = (wx, wy); }
            }
        return pick;
    }

    private (Fixed X, Fixed Y)? NearestUnexplored(NavigationGrid grid, Fixed cx, Fixed cy)
    {
        var terrain = grid.Terrain;
        (Fixed X, Fixed Y)? pick = null;
        var best = Fixed.MaxValue;
        for (var y = 4; y < terrain.CellsY; y += 8)
            for (var x = 4; x < terrain.CellsX; x += 8)
            {
                var wx = Fixed.FromInt(x * TerrainMap.CellSize + TerrainMap.CellSize / 2);
                var wy = Fixed.FromInt(y * TerrainMap.CellSize + TerrainMap.CellSize / 2);
                if (_world.IsPositionExploredBy(_player, wx, wy) || _unreachable.Contains(y / 8 * terrain.CellsX + x / 8) || !grid.IsPassable(x, y, LocomotorSurfaces.Ground)) continue;
                var distance = DistanceSquared(wx, wy, cx, cy);
                if (distance < best) { best = distance; pick = (wx, wy); }
            }
        return pick;
    }

    /// <summary>No warehouse is known yet: an idle carrier walks to the nearest cell the player has never seen and hopes to find one.</summary>
    private void ScoutForWarehouses()
    {
        if (_world.Navigation is not { } grid) return;
        foreach (var unit in _units)
        {
            if ((RoleOf(unit.Template) & Role.Carrier) == 0 || AiOf(unit) is not { } ai || !ai.IsIdle || Backoff(unchecked(0x70000000u + unit.Id.Value), 300)) continue;
            if (NearestUnexplored(grid, unit.X, unit.Y) is not { } spot) continue;
            if (Send(sequence => new SetMoveTargetCommand(_player, sequence, _tick, unit.Id, spot.X, spot.Y)).Accepted) Stats.MoveOrders++;
        }
    }

    /// <summary>A carrier that has stood idle for a while is sent to the nearest warehouse it knows.</summary>

    private void OrderCarriers()
    {
        if (_warehouses.Count == 0)
        {
            ScoutForWarehouses();
            return;
        }
        foreach (var unit in _units)
        {
            if ((RoleOf(unit.Template) & Role.Carrier) == 0 || AiOf(unit) is not { } ai) continue;
            if (!ai.IsIdle)
            {
                _idleSince.Remove(unit.Id.Value);
                continue;
            }
            var since = _idleSince.TryGetValue(unit.Id.Value, out var start) ? start : _idleSince[unit.Id.Value] = _tick;
            if (_tick - since < 90 || Backoff(unchecked(0x60000000u + unit.Id.Value))) continue;
            Remembered? nearest = null;
            var best = Fixed.MaxValue;
            foreach (var warehouse in _warehouses.Values)
            {
                var distance = DistanceSquared(warehouse.X, warehouse.Y, unit.X, unit.Y);
                if (distance < best) { best = distance; nearest = warehouse; }
            }
            if (nearest is not { } target) continue;
            if (Send(sequence => new DockCommand(_player, sequence, _tick, unit.Id, target.Id)).Accepted) Stats.DockOrders++;
        }
    }
}
