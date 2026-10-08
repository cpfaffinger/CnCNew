using System.Collections.Immutable;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Random;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Skirmish;

/// <param name="Player">The player the AI plays for.</param>
/// <param name="Seed">Seed of the AI's private random stream (independent of the world's streams).</param>
/// <param name="HomeX">Centre of the base: the start position on the map.</param>
/// <param name="Side">Side of the player's faction; only objects of this side are built or produced.</param>
/// <param name="FamilyPrefix">Name prefix of the faction's own object variants (<c>AirF_America</c>); such variants are preferred over the shared ones of the side.</param>
/// <param name="Composition">How the army's next unit is chosen; null for <see cref="SkirmishAi.DefaultComposition"/>.</param>
/// <param name="HintPositions">Start positions of the other players; public map knowledge the AI may attack before it has seen anything.</param>
public sealed record SkirmishAiOptions(PlayerId Player, uint Seed, Fixed HomeX, Fixed HomeY, string Side, string FamilyPrefix, ImmutableArray<(Fixed X, Fixed Y)> HintPositions, int AttackThreshold = 0, int MaxAttackThreshold = 0, bool EconomyFirst = false, ArmyComposition? Composition = null);

/// <summary>Counters an AI keeps about itself; the match report and the tests read them.</summary>
public sealed class SkirmishAiStats
{
    public int Commands { get; internal set; }
    public int Rejected { get; internal set; }
    public int Constructions { get; internal set; }
    public int Resumes { get; internal set; }
    public int CancelledSites { get; internal set; }
    public int Productions { get; internal set; }
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
    /// <summary>Army draws by <see cref="ArmyComposition.ExchangeValue"/> that had a sighted enemy to weigh against, and those that fell back to the uniform draw.</summary>
    public int InformedDraws { get; internal set; }
    public int UninformedDraws { get; internal set; }
    /// <summary>Objectives the army turned away from for a while because it had marched on them for too long without ending them.</summary>
    public int ObjectivesGivenUp { get; internal set; }
    /// <summary>
    /// What every look of the army production at a barracks or a factory ended with, counted per producer template and outcome (key: template, a tab, the outcome):
    /// <c>queue-full</c>, <c>no-candidate</c> (nothing it offers is an army unit whose prerequisites are met), <c>unaffordable</c> (candidates exist, none is covered by the money
    /// left at that moment), <c>queued:&lt;unit&gt;</c> and <c>rejected:&lt;reason&gt;:&lt;unit&gt;</c>. Diagnostics only; nothing reads it back.
    /// </summary>
    public SortedDictionary<string, int> ArmyProduction { get; } = new(StringComparer.Ordinal);

    internal void CountArmyProduction(ObjectTemplate producer, string outcome)
    {
        var key = producer.Name + "\t" + outcome;
        ArmyProduction[key] = ArmyProduction.GetValueOrDefault(key) + 1;
    }
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
public sealed partial class SkirmishAi
{
    // Own rhythms and deadlines of the AI, all in logic frames of the original (1/30 s) and scaled once per use through the world's time service, so the AI
    // observes, thinks and waits at the same moments of game time at every tick rate. ThinkIntervalFrames must stay a multiple of FireIntervalFrames.
    private const long FireIntervalFrames = 5;
    private const long ThinkIntervalFrames = 15;
    private const long RetryFrames = 300;
    private const long MemoryFrames = 900;
    private const long OrderRefreshFrames = 150;
    /// <summary>Back-off of a worker order per producer and template, of the first supply center's placement, of a transport's unload order and the shortest time between two resumes of a site or two different marching orders (2 s).</summary>
    private const long ShortRetryFrames = 60;
    /// <summary>Margin on a worker's build time before its production counts as lost (1 s).</summary>
    private const long InFlightMarginFrames = 30;
    /// <summary>Back-off of the builder's walk to an unexplored supply source (5 s).</summary>
    private const long SupplyLookRetryFrames = 150;
    /// <summary>Back-off per science after a purchase attempt (60 s).</summary>
    private const long ScienceRetryFrames = 1800;
    /// <summary>Back-off per special power after a refusal for a missing science (100 s) and after any other refusal (30 s).</summary>
    private const long PowerMissingScienceRetryFrames = 3000;
    private const long PowerRefusedRetryFrames = 900;
    /// <summary>Back-off of the tunnel network's evacuation (3 s) and the time a carrier stands idle before it is sent to a warehouse (3 s).</summary>
    private const long TunnelEvacuationRetryFrames = 90;
    private const long CarrierIdleFrames = 90;
    /// <summary>Time a scouting target has to be reached in before its cell is written off as unreachable (15 s).</summary>
    private const long ScoutReachFrames = 450;
    /// <summary>Back-off per idle carrier for the search for a warehouse (10 s).</summary>
    private const long WarehouseScoutRetryFrames = 300;
    private const int MinAttackThreshold = 8;
    private const int MaxAttackThreshold = 30;
    private const int PlacementChecksPerThink = 400;
    private static readonly Fixed DefenseRadius = Fixed.FromInt(450);
    private static readonly Fixed EngageRadius = Fixed.FromInt(260);
    /// <summary>How far from the rally point a unit at rest still counts as gathered: the reach of the world's search for a free place around a taken goal (ten cells) plus one.</summary>
    private static readonly Fixed GatherRadius = Fixed.FromInt(11 * TerrainMap.CellSize);
    private static readonly Fixed RepairBelow = Fixed.FromDecimal(0.7m);
    private static readonly Fixed TunnelHealBelow = Fixed.FromDecimal(0.5m);
    private static readonly Fixed TunnelReach = Fixed.FromInt(600);
    private static readonly Fixed BoardingReach = Fixed.FromInt(250);
    private const long BoardingFrames = 300;

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
    private readonly SimulationTime _time;
    private readonly long _fireInterval;
    private readonly long _thinkInterval;
    private readonly long _phase;
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
    private readonly SortedSet<uint> _wave = [];
    private int _threshold = MinAttackThreshold;
    private int _maxThreshold = MaxAttackThreshold;
    private (Fixed X, Fixed Y)? _rally;
    private (Fixed X, Fixed Y)? _scout;
    private long _scoutAt = -1;
    private bool _scoutRevisit;
    private readonly Dictionary<int, long> _seenAt = [];
    private readonly HashSet<int> _unreachable = [];
    private const long RevisitFrames = 3600;
    private readonly Dictionary<uint, long> _boarding = [];
    private (Fixed X, Fixed Y)? _armyGoal;

    /// <summary>
    /// How long the army keeps marching on one objective that is still there (2 min) before it puts it behind all the others. The army picks the objective nearest to
    /// itself; one it cannot get at (behind a cliff, out of every weapon's reach from where the path ends) would otherwise hold it for ever, because standing in front of it
    /// keeps it the nearest. Only a mission's agent has objectives; a skirmish match is not touched.
    /// </summary>
    private const long ObjectivePatienceFrames = 3600;

    private (Fixed X, Fixed Y)? _objective;
    private long _objectiveSince;
    private readonly HashSet<(Fixed X, Fixed Y)> _givenUp = [];
    // Diagnostics (DescribeState): the enemy the last think took for a threat to the base.
    private string _threat = string.Empty;

    public SkirmishAi(SimulationWorld world, TemplateCatalog catalog, SkirmishAiOptions options, Func<SimCommand, SubmitResult> submit)
    {
        _world = world;
        _catalog = catalog;
        _options = options;
        _submit = submit;
        _player = options.Player;
        _random = new SageRandom(options.Seed);
        _time = world.Time;
        _fireInterval = _time.OriginalFrames(FireIntervalFrames);
        _thinkInterval = _time.OriginalFrames(ThinkIntervalFrames);
        // The stagger between the players is drawn in frames of the original and then scaled like the intervals: at every rate the same players share a frame.
        _phase = _time.OriginalFrames(options.Player.Value % FireIntervalFrames);
        _side = options.Side;
        _composition = options.Composition ?? DefaultComposition;
        if (options.AttackThreshold > 0) _threshold = options.AttackThreshold;
        if (options.MaxAttackThreshold > 0) _maxThreshold = Math.Max(options.MaxAttackThreshold, _threshold);
    }

    public SkirmishAiStats Stats { get; } = new();
    public bool Attacking => _attacking;

    /// <summary>
    /// Places the army is to attack before anything it would pick itself (a mission's objectives, as the briefing and the objective markers tell a player); empty for the AI's own
    /// choice. A skirmish match never sets it.
    /// </summary>
    public ImmutableArray<(Fixed X, Fixed Y)> Objectives { get; set; } = [];

    /// <summary>
    /// The army attacks with whatever it has and does not fall back to mass again: for a side that cannot produce (a mission that hands the player a fixed force) waiting for the
    /// attack threshold would wait forever. A skirmish match never sets it.
    /// </summary>
    public bool CommitEverything { get; set; }

    /// <summary>Money the AI leaves untouched (a purchase the mission's objective needs, e.g. the capture upgrade); zero in a skirmish match.</summary>
    public Fixed ObjectiveReserve { get; set; }

    /// <summary>Whether this AI gives <paramref name="unit"/> its marching orders (it counts it as army).</summary>
    public bool LeadsUnit(Entity unit) => unit.Owner == _player && (RoleOf(unit.Template) & Role.Army) != 0 && AiOf(unit) is not null;

    /// <summary>Where the army gathers while it is not attacking (diagnostics).</summary>
    public (Fixed X, Fixed Y)? RallyPoint => _rally;
    public int KnownEnemyStructures => _enemies.Values.Count(value => value.IsStructure);

    /// <summary>One line about the economy and the army, for diagnostics (observed state of the last think).</summary>
    public string DescribeState()
        => $"carriers {CountAlive(Role.Carrier, false)}, builders {CountAlive(Role.Builder, false)}, supply centers {CountAlive(Role.SupplyCenter, false)}, power {CountAlive(Role.Power, false)}, barracks {CountAlive(Role.Barracks, false)}, factories {CountAlive(Role.Factory, false)}, defenses {CountAlive(Role.Defense, false)}, warehouses known {_warehouses.Count}, army {_units.Count(value => (RoleOf(value.Template) & Role.Army) != 0)}, threshold {_threshold}, attacking {_attacking}, known enemy structures {KnownEnemyStructures}, wave {_wave.Count}, army goal {(_armyGoal is { } goal ? $"({goal.X.Floor()}, {goal.Y.Floor()})" : "none")}, rally {(_rally is { } rally ? $"({rally.X.Floor()}, {rally.Y.Floor()})" : "none")}, threat {(_threat.Length == 0 ? "none" : _threat)}";

    /// <summary>Roles of the given template, for diagnostics.</summary>
    public string DescribeRoles(ObjectTemplate template) => RoleOf(template).ToString();

    /// <summary>The AI's turn for the world's current tick: call once per tick, before <see cref="SimulationWorld.Step"/>.</summary>
    public void Update()
    {
        _tick = _world.Tick;
        if (_tick == 0 || (_tick + _phase) % _fireInterval != 0) return;
        if (_world.IsPlayerDefeated(_player)) return;
        Observe();
        if ((_tick + _phase) % _thinkInterval != 0) return;
        Think();
    }

    private void Think()
    {
        _budget = Fixed.Max(Fixed.Zero, _world.MoneyOf(_player) - ObjectiveReserve);
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
        if (_attacking) Stats.AttackTicks += _thinkInterval;
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

    private bool Backoff(uint key, long frames = RetryFrames)
    {
        if (_retryAt.TryGetValue(key, out var until) && until > _tick) return true;
        _retryAt[key] = _tick + _time.OriginalFrames(frames);
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
            // A warehouse of the system player or of any side that is not an enemy (mission maps give their supply docks to a civilian side) is one to gather from,
            // like FindSupplySource already assumes; it stays remembered as a fixed place.
            if (entity.Owner.IsSystem || !_world.AreEnemies(_player, entity.Owner))
            {
                if ((RoleOf(entity.Template) & Role.Warehouse) != 0)
                {
                    seen.Add(entity.Id.Value);
                    _warehouses[entity.Id.Value] = new Remembered(entity.Id, entity.Template, entity.Owner, entity.X, entity.Y, _tick, !entity.Owner.IsSystem);
                }
                continue;
            }
            seen.Add(entity.Id.Value);
            _enemies[entity.Id.Value] = new Remembered(entity.Id, entity.Template, entity.Owner, entity.X, entity.Y, _tick, entity.Template.IsKindOf("STRUCTURE"));
            if (entity.Body is null) continue;
            _enemyTargets.Add(entity);
            if (_composition == ArmyComposition.ExchangeValue) RememberSighting(entity);
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
            var stale = !entry.IsStructure && !entry.Owner.IsSystem && _tick - entry.Seen > _time.OriginalFrames(MemoryFrames);
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
                if (Backoff(unchecked(producer.Id.Value * 31u + (uint)template.Index + 1_000_000u), ShortRetryFrames)) continue;
                var result = Send(sequence => new QueueProductionCommand(_player, sequence, _tick, producer.Id, template.Name));
                if (!result.Accepted) continue;
                _budget -= template.BuildCost;
                _inFlight.Add((RoleOf(template), _tick + template.BuildTicks + _time.OriginalFrames(InFlightMarginFrames)));
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

    private const int MaxSupplyCenters = 4;

    /// <summary>Credits left beside the own supply centers below which the next center is planned: enough income still to come to pay for it.</summary>
    private const long LowSupplyStock = 3000;

    /// <summary><c>SupplyCenterSafeRadius</c> (AIData.ini, shipped value; the file is not typed yet).</summary>
    private static readonly Fixed SupplyCenterSafeRadius = Fixed.FromInt(300);

    /// <summary>
    /// What the own supply centers can still gather: the credits stored in the warehouses within <see cref="SupplyCenterCloseDistance"/> of one of them that are not an enemy's
    /// (<c>AIPlayer::queueSupplyTruck</c> looks at a center the same way: "make sure we have supplies near it", and stops making gatherers for a center whose source is empty).
    /// </summary>
    private long SupplyStockInReach()
    {
        long total = 0;
        foreach (var entity in _world.Entities)
        {
            if ((RoleOf(entity.Template) & Role.Warehouse) == 0 || entity.EffectivelyDead || entity.DestroyRequested) continue;
            if (!entity.Owner.IsSystem && _world.AreEnemies(_player, entity.Owner)) continue;
            var boxes = Views.EntityPresentationView.WarehouseSupplyBoxes(entity);
            if (boxes <= 0) continue;
            var reach = SupplyCenterCloseDistance + entity.Template.BoundingCircleRadius;
            foreach (var own in _structures.Concat(_sites))
            {
                if ((RoleOf(own.Template) & Role.SupplyCenter) == 0) continue;
                var limit = reach + own.Template.BoundingCircleRadius;
                if (!SquaredReach.Of(limit).Contains(DistanceSquared(own.X, own.Y, entity.X, entity.Y))) continue;
                total += (long)boxes * _catalog.ValuePerSupplyBox;
                break;
            }
        }
        return total;
    }

    /// <summary>
    /// <c>AIPlayer::isSupplySourceSafe</c>/<c>isLocationSafe</c>: no enemy within <see cref="SupplyCenterSafeRadius"/> of the source, harvesters and dozers not counted. Only what this
    /// player sees or remembers: visible enemy units and armed structures, and remembered enemy structures.
    /// </summary>
    private bool SupplySourceSafe(Entity source)
    {
        var radius = SupplyCenterSafeRadius + source.Template.BoundingCircleRadius;
        var limit = SquaredReach.Of(radius);
        foreach (var enemy in _enemyTargets)
        {
            if ((RoleOf(enemy.Template) & (Role.Builder | Role.Carrier)) != 0) continue;
            if (enemy.Template.IsKindOf("STRUCTURE") && enemy.Template.DefaultWeapon < 0) continue;
            if (limit.Contains(DistanceSquared(enemy.X, enemy.Y, source.X, source.Y))) return false;
        }
        foreach (var entry in _enemies.Values)
            if (entry.IsStructure && entry.Template.DefaultWeapon >= 0 && limit.Contains(DistanceSquared(entry.X, entry.Y, source.X, source.Y))) return false;
        return true;
    }

    private Role[] PlanGoals()
    {
        var goals = new List<Role>();
        var army = _units.Count(value => (RoleOf(value.Template) & Role.Army) != 0);
        var producers = CountAlive(Role.Producer, true);
        if (CountAlive(Role.CommandCenter, true) == 0 && CountAlive(Role.Builder, false) > 0) goals.Add(Role.CommandCenter);
        var supplyCenters = CountAlive(Role.SupplyCenter, true);
        if (supplyCenters < 1) goals.Add(Role.SupplyCenter);
        // The stock beside the existing centers is running out: the next center goes to another stocked, safe source before anything else is built, and while it cannot be paid
        // the army production keeps its price back (the reserve of BuildStructures) - once the income has stopped there is nothing left to pay it with.
        else if (supplyCenters < MaxSupplyCenters && SupplyStockInReach() < LowSupplyStock && FindSupplySource() is { } nextSource && SupplySourceSafe(nextSource)) goals.Add(Role.SupplyCenter);
        if (CountAlive(Role.Power, true) < 1) goals.Add(Role.Power);
        if (CountAlive(Role.Barracks, true) < 1) goals.Add(Role.Barracks);
        if (CountAlive(Role.Factory, true) < 1) goals.Add(Role.Factory);
        if (CountAlive(Role.SupplyCenter, true) < 2 && army >= 10 && FindSupplySource() is not null) goals.Add(Role.SupplyCenter);
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
            // EconomyFirst (a mission's player agent): until the first supply center stands nothing else is started - a base without income cannot pay for the rest.
            var essential = _options.EconomyFirst && role == Role.SupplyCenter && CountAlive(Role.SupplyCenter, true) == 0;
            ObjectTemplate? next = null;
            foreach (var candidate in StructureCandidates(role))
            {
                var state = Plan(candidate, 0, out next);
                if (state == PlanState.Impossible) continue;
                if (state == PlanState.Waiting) next = null;
                break;
            }
            if (next is null) { if (essential) break; continue; }
            if (next.BuildCost > _budget)
            {
                reserved = next.BuildCost;
                break;
            }
            if (Backoff(unchecked(0x40000000u + (uint)next.Index), essential ? ShortRetryFrames : RetryFrames)) { if (essential) break; continue; }
            var bySupplies = (RoleOf(next) & Role.SupplyCenter) != 0;
            var spot = bySupplies ? FindSupplyPlacement(next, builder, used) : FindPlacement(next, builder);
            if (bySupplies)
            {
                // No legal spot beside the source for a while (cliffs, the map edge, an occupied yard): the center goes up at the base like every other structure rather than never.
                _supplyPlacementFailures = spot is null ? _supplyPlacementFailures + 1 : 0;
                if (spot is null && _supplyPlacementFailures > SupplyPlacementPatience) spot = FindPlacement(next, builder);
            }
            if (spot is null) { if (essential) break; continue; }
            var (x, y) = spot.Value;
            var result = Send(sequence => new ConstructCommand(_player, sequence, _tick, builder.Id, next.Name, x, y, Fixed.Zero));
            if (!result.Accepted) { if (essential) break; continue; }
            used.Add(builder.Id.Value);
            _budget -= next.BuildCost;
            Stats.Constructions++;
        }
        _reserve = reserved;
    }

    private Fixed _reserve;
    private int _supplyPlacementFailures;

    /// <summary>Attempts (one per retry interval) a supply center may fail to find a spot beside its source before it is placed at the base instead.</summary>
    private const int SupplyPlacementPatience = 3;

    /// <summary>
    /// An available builder: not on a construction task, and idle unless it is a worker that also gathers (then the gathering yields). A builder that is only driving out of the
    /// factory that built it (or stepping aside for the next unit) is available too: nobody ordered that move and any order replaces it. The reference's computer player asks its
    /// dozers whether a task is pending (<c>AIPlayer::findDozer</c>, <c>DozerAIInterface::isAnyTaskPending</c>), not whether they stand still.
    /// </summary>
    private Entity? PickBuilder(HashSet<uint> used, Entity? near = null)
    {
        Entity? best = null;
        var bestDistance = Fixed.MaxValue;
        foreach (var entity in _units)
        {
            if ((RoleOf(entity.Template) & Role.Builder) == 0 || used.Contains(entity.Id.Value) || AiOf(entity) is not { } ai || ai.IsBuilding) continue;
            if (!ai.IsIdle && !ai.IsOnUnorderedMove && (RoleOf(entity.Template) & Role.Carrier) == 0) continue;
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

    /// <summary><c>SUPPLY_CENTER_CLOSE_DIST</c> (AIPlayer.cpp): 20 pathfinding cells.</summary>
    private static readonly Fixed SupplyCenterCloseDistance = Fixed.FromInt(20 * TerrainMap.CellSize);

    /// <summary>The cash a supply source should still hold to be worth a supply center; halved until a source qualifies (<c>findSupplyCenter</c>'s loop down to 100).</summary>
    private const long SupplySourceMinimumCash = 1000;

    /// <summary>
    /// <c>AIPlayer::findSupplyCenter</c>: the stocked warehouse nearest to the base that is not an enemy's, has no own supply center within <see cref="SupplyCenterCloseDistance"/>
    /// and is not much closer to the enemy than to us ("closer than 60/40 to enemy than to us"). Like the reference's computer player this looks at every warehouse on the map,
    /// seen or not; the enemy's base is only what this player has seen of it.
    /// </summary>
    private Entity? FindSupplySource()
    {
        (Fixed X, Fixed Y)? enemyCenter = null;
        var enemyStructures = _enemies.Values.Where(value => value.IsStructure).ToList();
        if (enemyStructures.Count > 0)
        {
            var minX = enemyStructures.Min(value => value.X.Raw);
            var maxX = enemyStructures.Max(value => value.X.Raw);
            var minY = enemyStructures.Min(value => value.Y.Raw);
            var maxY = enemyStructures.Max(value => value.Y.Raw);
            enemyCenter = (Fixed.FromRaw((minX + maxX) / 2), Fixed.FromRaw((minY + maxY) / 2));
        }

        for (var minimumCash = SupplySourceMinimumCash; minimumCash > 100; minimumCash /= 2)
        {
            Entity? best = null;
            var bestDistance = Fixed.MaxValue;
            foreach (var entity in _world.Entities.OrderBy(value => value.Id.Value))
            {
                if ((RoleOf(entity.Template) & Role.Warehouse) == 0 || entity.EffectivelyDead || entity.DestroyRequested) continue;
                if ((long)Views.EntityPresentationView.WarehouseSupplyBoxes(entity) * _catalog.ValuePerSupplyBox < minimumCash) continue;
                if (!entity.Owner.IsSystem && _world.AreEnemies(_player, entity.Owner)) continue;
                var reach = SupplyCenterCloseDistance + entity.Template.BoundingCircleRadius;
                var taken = false;
                foreach (var own in _structures.Concat(_sites))
                {
                    if ((RoleOf(own.Template) & Role.SupplyCenter) == 0) continue;
                    var limit = reach + own.Template.BoundingCircleRadius;
                    if (SquaredReach.Of(limit).Contains(DistanceSquared(own.X, own.Y, entity.X, entity.Y))) { taken = true; break; }
                }
                if (taken) continue;
                var distance = DistanceSquared(entity.X, entity.Y, _options.HomeX, _options.HomeY);
                if (enemyCenter is { } enemy && distance * Fixed.FromInt(4) > DistanceSquared(entity.X, entity.Y, enemy.X, enemy.Y) * Fixed.FromInt(6)) continue;
                if (distance < bestDistance) { best = entity; bestDistance = distance; }
            }
            if (best is not null) return best;
        }
        return null;
    }

    /// <summary>
    /// <c>AIPlayer::buildBySupplies</c>: a supply center goes next to a supply source — three cells from the warehouse toward the base, and if that spot is not legal the first legal
    /// one on growing squares around it (two cells wider each time, up to twice <see cref="SupplyCenterCloseDistance"/>). A spot the player has not explored yet cannot be built on
    /// (<c>ConstructCommand</c>), so the builder is first sent there to look. Without any supply source on the map the center goes up at the base like every other structure.
    /// </summary>
    private (Fixed X, Fixed Y)? FindSupplyPlacement(ObjectTemplate template, Entity builder, HashSet<uint> used)
    {
        if (FindSupplySource() is not { } source) return FindPlacement(template, builder);
        var dx = source.X - _options.HomeX;
        var dy = source.Y - _options.HomeY;
        var length = Fixed.Sqrt(dx * dx + dy * dy);
        var back = Fixed.FromInt(3 * TerrainMap.CellSize);
        var originX = length > Fixed.Zero ? source.X - dx / length * back : source.X;
        var originY = length > Fixed.Zero ? source.Y - dy / length * back : source.Y;
        if (!_world.IsPositionExploredBy(_player, originX, originY))
        {
            // Look first: the builder walks to the source; the placement is tried again once the ground is known.
            if (!Backoff(unchecked(0x48000000u + builder.Id.Value), SupplyLookRetryFrames)
                && Send(sequence => new SetMoveTargetCommand(_player, sequence, _tick, builder.Id, originX, originY)).Accepted)
            {
                used.Add(builder.Id.Value);
                Stats.MoveOrders++;
            }
            return null;
        }

        var radius = template.BoundingCircleRadius;
        var gap = Fixed.FromInt(12);
        var checks = 0;
        var paths = 0;
        (Fixed X, Fixed Y)? Try(Fixed x, Fixed y)
        {
            if (++checks > PlacementChecksPerThink) return null;
            foreach (var entity in _structures) if (Clash(entity, radius, gap, x, y)) return null;
            foreach (var entity in _sites) if (Clash(entity, radius, gap, x, y)) return null;
            foreach (var (spotX, spotY) in _badSpots) if (DistanceSquared(spotX, spotY, x, y) < Fixed.FromInt(900)) return null;
            if (_world.Navigation is { } navigation && !navigation.IsFootprintLegal(template, x, y, Fixed.Zero)) return null;
            if (!_world.IsPositionExploredBy(_player, x, y)) return null;
            if (_world.Navigation is { } grid && (++paths > 12 || !Reachable(grid, builder, x, y))) return null;
            return (x, y);
        }

        if (Try(originX, originY) is { } direct) return direct;
        var cell = Fixed.FromInt(TerrainMap.CellSize);
        for (var size = Fixed.Zero; size < SupplyCenterCloseDistance * Fixed.FromInt(2); size += cell * Fixed.FromInt(2))
        {
            var half = size / Fixed.FromInt(2);
            for (var x = originX - half; x <= originX + half; x += cell)
            {
                if (Try(x, originY - half) is { } low) return low;
                if (Try(x, originY - half + size) is { } high) return high;
            }
            for (var y = originY - half; y <= originY + half; y += cell)
            {
                if (Try(originX - half, y) is { } left) return left;
                if (Try(originX - half + size, y) is { } right) return right;
            }
            if (checks > PlacementChecksPerThink) break;
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
        // The army gathers on the rally point: no structure goes up on it.
        if (_rally is { } rally && SquaredReach.Of(radius + Fixed.FromInt(15)).ContainsStrictly(DistanceSquared(rally.X, rally.Y, x, y))) return false;
        return true;
    }

    private static bool Clash(Entity entity, Fixed radius, Fixed gap, Fixed x, Fixed y)
    {
        var limit = entity.Template.BoundingCircleRadius + radius + gap;
        return SquaredReach.Of(limit).ContainsStrictly(DistanceSquared(entity.X, entity.Y, x, y));
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
            if (watch.LastResume > 0 && _tick - watch.LastResume < _time.OriginalFrames(ShortRetryFrames)) continue;
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
        _scienceRetry[pick.Name] = _tick + _time.OriginalFrames(ScienceRetryFrames);
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
                _powerRetry[(entity.Id.Value, info.Name)] = _tick + _time.OriginalFrames(result.Accepted ? RetryFrames : result.Reason == CommandRejection.MissingScience ? PowerMissingScienceRetryFrames : PowerRefusedRetryFrames);
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

    /// <summary>The army unit the AI has decided on and is saving for: which producer makes it and since when the decision stands.</summary>
    private readonly record struct ArmyOrder(EntityId Producer, ObjectTemplate Unit, long Since);

    private ArmyOrder? _armyOrder;

    /// <summary>
    /// How long an army order may wait for its price (60 s) before it gives way to something that can be paid at once. Own value: long enough for the income of one supply
    /// center to pay a tank, short enough that an economy that has stopped still turns its last money into units.
    /// </summary>
    private const long ArmyOrderPatienceFrames = 1800;

    /// <summary>
    /// The rule "standing army order": the next army unit is drawn among everything the barracks and factories with room in their queue could make (prerequisites met, the
    /// price not looked at), every unit type with the same chance, and the AI then saves for exactly that unit; nothing else is bought for the army until it is paid. Only an
    /// order that has waited <see cref="ArmyOrderPatienceFrames"/> gives way to a draw among what the money covers now.
    /// <para>
    /// Before this rule every producer bought, in the order the structures were built, a random unit among those the money covered at that moment. The barracks stands first
    /// and its infantry is the cheapest army unit, so the money never reached the price of a vehicle: over 80 matches the factories' looks ended 188,605 times with
    /// "unaffordable" and 328 times with an order (docs/AI-OBSERVED-2026-10-08.md).
    /// </para>
    /// </summary>
    private void ProduceArmy()
    {
        var armyCount = _units.Count(value => (RoleOf(value.Template) & Role.Army) != 0);
        var spendable = armyCount < 4 ? _budget : _budget - _reserve;
        var served = new HashSet<uint>();
        while (true)
        {
            if (_armyOrder is not { } order || !ArmyOrderStands(order, served))
            {
                if (DrawArmyOrder(served, Fixed.MaxValue) is not { } drawn) break;
                _armyOrder = order = drawn;
            }
            var producer = _world.FindEntity(order.Producer)!;
            if (order.Unit.BuildCost > spendable)
            {
                if (_tick - order.Since < _time.OriginalFrames(ArmyOrderPatienceFrames) || DrawArmyOrder(served, spendable) is not { } affordable)
                {
                    Stats.CountArmyProduction(producer.Template, "saving:" + order.Unit.Name);
                    break;
                }
                Stats.CountArmyProduction(producer.Template, "gave-way:" + order.Unit.Name);
                _armyOrder = order = affordable;
                producer = _world.FindEntity(order.Producer)!;
            }
            var pick = order.Unit;
            var result = Send(sequence => new QueueProductionCommand(_player, sequence, _tick, producer.Id, pick.Name));
            Stats.CountArmyProduction(producer.Template, result.Accepted ? "queued:" + pick.Name : "rejected:" + result.Reason + ":" + pick.Name);
            _armyOrder = null;
            // One order per producer and think, as before; a refused producer is not asked again in this think.
            served.Add(producer.Id.Value);
            if (!result.Accepted) continue;
            spendable -= pick.BuildCost;
            _budget -= pick.BuildCost;
            Stats.Productions++;
        }
    }

    /// <summary>The order can still be carried out: its producer stands, has room, was not served in this think, and the unit's prerequisites are still met.</summary>
    private bool ArmyOrderStands(ArmyOrder order, HashSet<uint> served)
    {
        if (served.Contains(order.Producer.Value)) return false;
        foreach (var producer in _structures)
            if (producer.Id == order.Producer)
                return ProductionOf(producer) is { HasRoom: true } && PrerequisitesMet(order.Unit);
        return false;
    }

    /// <summary>
    /// Draws the next army order: every unit type a barracks or factory with room offers (prerequisites met, at most <paramref name="limit"/> in price; own variants of the
    /// faction only where a producer has any) with the same chance, made by the first producer that offers it. Null without a candidate; then nothing is drawn from the stream.
    /// </summary>
    private ArmyOrder? DrawArmyOrder(HashSet<uint> served, Fixed limit)
    {
        var units = new List<ObjectTemplate>();
        var producers = new List<Entity>();
        foreach (var producer in _structures)
        {
            if ((RoleOf(producer.Template) & (Role.Barracks | Role.Factory)) == 0 || served.Contains(producer.Id.Value)) continue;
            var production = ProductionOf(producer);
            if (production is null) continue;
            if (!production.HasRoom) { Stats.CountArmyProduction(producer.Template, "queue-full"); continue; }
            var candidates = ProducibleBy(producer.Template).Where(value => (RoleOf(value) & Role.Army) != 0 && value.BuildCost <= limit && PrerequisitesMet(value)).ToList();
            if (candidates.Count == 0)
            {
                if (limit == Fixed.MaxValue) Stats.CountArmyProduction(producer.Template, "no-candidate");
                continue;
            }
            if (candidates.Any(InFamily)) candidates = candidates.Where(InFamily).ToList();
            foreach (var candidate in candidates)
            {
                if (units.Contains(candidate)) continue;
                units.Add(candidate);
                producers.Add(producer);
            }
        }
        if (units.Count == 0) return null;
        var index = _composition == ArmyComposition.ExchangeValue ? DrawByExchangeValue(units) : _random.Range(0, units.Count - 1);
        return new ArmyOrder(producers[index].Id, units[index], _tick);
    }

    private void SetRallyPoints()
    {
        // A structure raised later on the rally point (or beside it) leaves the army idling inside a building footprint: the point is chosen again and told to the producers.
        if (_rally is { } current && !RallyUsable(current, 0))
        {
            _rally = null;
            _rallySet.Clear();
        }

        _rally ??= ChooseRally();
        if (_rally is not { } rally) return;
        foreach (var producer in _structures)
        {
            if ((RoleOf(producer.Template) & (Role.Barracks | Role.Factory)) == 0 || !_rallySet.Add(producer.Id.Value)) continue;
            Send(sequence => new SetRallyPointCommand(_player, sequence, _tick, producer.Id, rally.X, rally.Y));
        }
    }

    /// <summary>The rally point and the cells within <paramref name="margin"/> cells of it are free of structure footprints, so units gathered there do not stand inside a building.</summary>
    private bool RallyUsable((Fixed X, Fixed Y) point, int margin)
    {
        if (_world.Navigation is not { } grid) return true;
        var cx = TerrainMap.CellOf(point.X);
        var cy = TerrainMap.CellOf(point.Y);
        for (var dy = -margin; dy <= margin; dy++)
            for (var dx = -margin; dx <= margin; dx++)
                if (grid.IsBlockedByObstacle(cx + dx, cy + dy)) return false;
        return true;
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
        // Straight toward the opponent first (the original choice); only a crowded base widens the search to the sides and further out (the first free point wins).
        for (var distance = 180; distance >= 60; distance -= 20)
        {
            var x = _options.HomeX + dx / length * Fixed.FromInt(distance);
            var y = _options.HomeY + dy / length * Fixed.FromInt(distance);
            if (_world.Navigation is not { } grid || grid.IsPassable(TerrainMap.CellOf(x), TerrainMap.CellOf(y), LocomotorSurfaces.Ground) && RallyUsable((x, y), 0))
                return (x, y);
        }

        var turns = new[] { 0, 25, -25, 50, -50, 75, -75, 100, -100, 130, -130, 160, -160, 180 };
        var distances = new[] { 180, 160, 140, 120, 100, 80, 60, 220, 260, 300, 360 };
        foreach (var degrees in turns)
        {
            var angle = FixedTrig.TwoPi * Fixed.FromInt(degrees) / Fixed.FromInt(360);
            var cos = FixedTrig.Cos(angle);
            var sin = FixedTrig.Sin(angle);
            var ux = (dx * cos - dy * sin) / length;
            var uy = (dx * sin + dy * cos) / length;
            foreach (var distance in distances)
            {
                var x = _options.HomeX + ux * Fixed.FromInt(distance);
                var y = _options.HomeY + uy * Fixed.FromInt(distance);
                if (_world.Navigation is not { } grid || grid.IsPassable(TerrainMap.CellOf(x), TerrainMap.CellOf(y), LocomotorSurfaces.Ground) && RallyUsable((x, y), 1) && _world.IsPositionExploredBy(_player, x, y))
                    return (x, y);
            }
        }

        return null;
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
        // A wave is the units that marched together (the reference's computer player sends teams, not single units): units produced while it is under way gather at the rally
        // point and follow as a group of their own instead of walking into the enemy one by one.
        _wave.RemoveWhere(id => !army.Any(unit => unit.Id.Value == id));
        var waiting = army.Count - _wave.Count;
        if (threat is not null) goal = threat;
        else
        {
            if (!_attacking && (army.Count >= _threshold || CommitEverything))
            {
                _attacking = true;
                Stats.Waves++;
                foreach (var unit in army) _wave.Add(unit.Id.Value);
            }
            else if (_attacking && waiting >= Math.Max(3, _threshold / 2))
            {
                foreach (var unit in army) _wave.Add(unit.Id.Value);
            }
            else if (_attacking && !CommitEverything && _wave.Count < Math.Max(3, _threshold / 3))
            {
                _attacking = false;
                _wave.Clear();
                _threshold = Math.Min(_maxThreshold, _threshold + 2);
            }
            goal = _attacking ? AttackTarget(army.Where(unit => _wave.Contains(unit.Id.Value)).ToList()) : _rally;
        }
        _armyGoal = goal;
        if (goal is not { } armyGoal) return;
        foreach (var unit in army)
        {
            if (_boarding.TryGetValue(unit.Id.Value, out var boardingUntil) && boardingUntil > _tick) continue;
            // Defence takes everybody; an attack only the wave, the others wait at the rally point.
            // Gathering at the rally point, as opposed to marching on an attack goal or a threat.
            var gathering = threat is null && (!_attacking || !_wave.Contains(unit.Id.Value));
            var destination = threat is null && _attacking && !_wave.Contains(unit.Id.Value) ? _rally ?? armyGoal : armyGoal;
            // An attack-move: the unit engages what it meets on the way by itself (the world's target acquisition) and marches on afterwards.
            var (x, y) = destination;
            var marching = unit.HasMoveTarget || _world.IsAttackMoving(unit);
            if (DistanceSquared(unit.X, unit.Y, x, y) < Fixed.FromInt(1600)) continue;
            // Gathering: a unit that came to rest where the crowd around the rally point left room (SimulationWorld.TryFreeGoalSpot places it up to ten cells away) has
            // arrived; sending it to the point again would only walk it there and back.
            if (gathering && !marching && SquaredReach.Of(GatherRadius).Contains(DistanceSquared(unit.X, unit.Y, x, y))) continue;
            if (_orders.TryGetValue(unit.Id.Value, out var order))
            {
                var same = DistanceSquared(order.X, order.Y, x, y) < Fixed.FromInt(900);
                if (same && (marching || _tick - order.Tick < _time.OriginalFrames(OrderRefreshFrames))) continue;
                if (!same && marching && _tick - order.Tick < _time.OriginalFrames(ShortRetryFrames)) continue;
            }
            if (Send(sequence => new AttackMoveCommand(_player, sequence, _tick, unit.Id, x, y)).Accepted) Stats.MoveOrders++;
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
            var limit = SquaredReach.Of(reach);
            var bestDistance = Fixed.Zero;
            foreach (var candidate in candidates)
            {
                var distance = DistanceSquared(candidate.X, candidate.Y, x, y);
                if (limit.ContainsStrictly(distance) && (best is null || distance < bestDistance)) { best = candidate; bestDistance = distance; }
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
            if (tunnel is null || Backoff(unchecked(0x80000000u + unit.Id.Value), RetryFrames)) continue;
            if (!Send(sequence => new MoveAndEnterCommand(_player, sequence, _tick, unit.Id, tunnel.Id)).Accepted) continue;
            _boarding[unit.Id.Value] = _tick + _time.OriginalFrames(BoardingFrames);
            _orders.Remove(unit.Id.Value);
            Stats.TunnelEntries++;
            room--;
        }
        if (network.ContainCount == 0 || Backoff(0x81000000u, TunnelEvacuationRetryFrames)) return;
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
            var engaged = _enemyTargets.Any(enemy => SquaredReach.Of(EngageRadius).ContainsStrictly(DistanceSquared(enemy.X, enemy.Y, transport.X, transport.Y)));
            if (!arrived && !engaged) continue;
            if (Backoff(unchecked(0x82000000u + transport.Id.Value), ShortRetryFrames)) continue;
            if (Send(sequence => new EvacuateCommand(_player, sequence, _tick, transport.Id, false, Fixed.Zero, Fixed.Zero)).Accepted) Stats.Evacuations++;
        }
        if (!_attacking) return;
        foreach (var transport in _units)
        {
            var role = RoleOf(transport.Template);
            if ((role & Role.Transport) == 0 || (role & Role.Army) == 0 || transport.Container is not { } contain) continue;
            if (contain.ContainMax >= 0 && contain.ContainCount >= contain.ContainMax) continue;
            Entity? pick = null;
            var limit = SquaredReach.Of(BoardingReach);
            var best = Fixed.Zero;
            foreach (var unit in _units)
            {
                if (!unit.Template.IsKindOf("INFANTRY") || (RoleOf(unit.Template) & Role.Army) == 0 || unit.Template.TransportSlotCount <= 0) continue;
                if (_boarding.TryGetValue(unit.Id.Value, out var until) && until > _tick) continue;
                var distance = DistanceSquared(unit.X, unit.Y, transport.X, transport.Y);
                if (!limit.ContainsStrictly(distance) || (pick is not null && distance >= best) || !contain.IsValidContainerFor(_world, unit, true)) continue;
                best = distance;
                pick = unit;
            }
            if (pick is null || Backoff(unchecked(0x83000000u + pick.Id.Value), RetryFrames)) continue;
            if (!Send(sequence => new MoveAndEnterCommand(_player, sequence, _tick, pick.Id, transport.Id)).Accepted) continue;
            _boarding[pick.Id.Value] = _tick + _time.OriginalFrames(BoardingFrames);
            _orders.Remove(pick.Id.Value);
            Stats.TransportLoads++;
        }
    }

    /// <summary>The visible enemy closest to the base among those near any of the AI's structures: the army drops its plans and defends.</summary>
    private (Fixed X, Fixed Y)? FindThreat()
    {
        Entity? nearest = null;
        var limit = SquaredReach.Of(DefenseRadius);
        var best = Fixed.Zero;
        foreach (var enemy in _enemyTargets)
        {
            if (enemy.Template.IsKindOf("STRUCTURE")) continue;
            foreach (var structure in _structures)
            {
                var distance = DistanceSquared(enemy.X, enemy.Y, structure.X, structure.Y);
                if (!limit.ContainsStrictly(distance) || (nearest is not null && distance >= best)) continue;
                best = distance;
                nearest = enemy;
            }
        }
        _threat = nearest is null ? string.Empty : $"{nearest.Template.Name}#{nearest.Id.Value} at ({nearest.X.Floor()}, {nearest.Y.Floor()})";
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
        if (!Objectives.IsDefaultOrEmpty)
        {
            // The nearest objective the army has not given up (ObjectivePatienceFrames); once it has given up every one it starts over with the nearest of all.
            (Fixed X, Fixed Y)? nearest = null;
            foreach (var objective in Objectives)
            {
                if (_givenUp.Contains(objective)) continue;
                var distance = DistanceSquared(objective.X, objective.Y, cx, cy);
                if (distance < best) { best = distance; nearest = objective; }
            }
            if (nearest is null)
            {
                _givenUp.Clear();
                foreach (var objective in Objectives)
                {
                    var distance = DistanceSquared(objective.X, objective.Y, cx, cy);
                    if (distance < best) { best = distance; nearest = objective; }
                }
            }
            if (_objective != nearest)
            {
                _objective = nearest;
                _objectiveSince = _tick;
            }
            else if (nearest is { } stuck && Objectives.Length > 1 && _tick - _objectiveSince > _time.OriginalFrames(ObjectivePatienceFrames))
            {
                _givenUp.Add(stuck);
                Stats.ObjectivesGivenUp++;
            }
            return nearest;
        }
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
            if (_clearedHints.TryGetValue(hint, out var clearedAt) && _tick - clearedAt < _time.OriginalFrames(RevisitFrames)) continue;
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
            if (!reached && _tick - _scoutAt < _time.OriginalFrames(ScoutReachFrames)) return current;
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
                if (_unreachable.Contains(key) || _tick - _seenAt.GetValueOrDefault(key, long.MinValue / 2) < _time.OriginalFrames(RevisitFrames) || !grid.IsPassable(x, y, LocomotorSurfaces.Ground)) continue;
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
            if ((RoleOf(unit.Template) & Role.Carrier) == 0 || AiOf(unit) is not { } ai || !ai.IsIdle || Backoff(unchecked(0x70000000u + unit.Id.Value), WarehouseScoutRetryFrames)) continue;
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
            if (_tick - since < _time.OriginalFrames(CarrierIdleFrames) || Backoff(unchecked(0x60000000u + unit.Id.Value))) continue;
            Remembered? nearest = null;
            var best = Fixed.MaxValue;
            // The nearest warehouse that still holds something (an empty one has nothing to give; the idle carrier stood beside it forever).
            foreach (var warehouse in _warehouses.Values)
            {
                if (_world.FindEntity(warehouse.Id) is not { } store || Views.EntityPresentationView.WarehouseSupplyBoxes(store) <= 0) continue;
                var distance = DistanceSquared(warehouse.X, warehouse.Y, unit.X, unit.Y);
                if (distance < best) { best = distance; nearest = warehouse; }
            }
            if (nearest is not { } target) continue;
            if (Send(sequence => new DockCommand(_player, sequence, _tick, unit.Id, target.Id)).Accepted) Stats.DockOrders++;
        }
    }
}
