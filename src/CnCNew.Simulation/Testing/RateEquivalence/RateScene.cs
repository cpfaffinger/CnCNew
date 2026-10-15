using System.Collections.Immutable;
using CnCNew.Data.Definitions;
using CnCNew.Data.Snapshots;
using CnCNew.Data.World;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Testing;

/// <summary>
/// The catalogs of one compiled definition graph at 30, 60 and 120 Hz, built on first use, and the flat maps of the scenes. The suite names its rates
/// itself; neither a tool's nor a test's default rate reaches it. The catalogs are built without model geometry (airfield points fall back to the
/// template geometry), so the test and the tool measure the same numbers. A rule profile other than the original (a fix, the movement profile) is folded into every
/// catalog and fingerprint, so the suite can measure the rates under it.
/// </summary>
public sealed class RateCatalogs(CompiledDefinitionGraph graph, Rules.RuleProfile? rules = null)
{
    private readonly Rules.RuleProfile _rules = rules ?? Rules.RuleProfile.Original;

    public Rules.RuleProfile RulesProfile => _rules;

    public static ImmutableArray<SimulationRate> Rates { get; } = [SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120];

    private readonly TemplateCatalog?[] _catalogs = new TemplateCatalog?[Rates.Length];
    private readonly Dictionary<(int Cells, string Waypoints, string Links), (MapData Map, TerrainMap Terrain)> _maps = [];
    private readonly string _fingerprint = DefinitionSnapshotWriter.ComputeFingerprint(graph).Sha256;

    public CompiledDefinitionGraph Graph => graph;

    /// <summary>The catalog cases are generated from.</summary>
    public TemplateCatalog Reference => Of(SimulationRate.Hz30);

    public TemplateCatalog Of(SimulationRate rate)
    {
        var index = Rates.IndexOf(rate);
        if (index < 0) throw new ArgumentException("The suite runs at 30, 60 and 120 Hz.", nameof(rate));
        return _catalogs[index] ??= TemplateCatalog.Build(graph, ModuleBindingTable.Default, rate, null, _rules);
    }

    public string FingerprintOf(SimulationRate rate) => _rules.Combine(rate.Combine(_fingerprint));

    /// <summary>
    /// A flat square map of <paramref name="cells"/> pathfinding cells (ten world units each) with named waypoints; <paramref name="links"/> join waypoints
    /// to paths by their position in <paramref name="waypoints"/>, counted from 1.
    /// </summary>
    public (MapData Map, TerrainMap Terrain) Flat(int cells, ImmutableArray<(string Name, int X, int Y)> waypoints, ImmutableArray<(int From, int To)> links)
    {
        var key = (cells, string.Join(';', waypoints.Select(point => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{point.Name},{point.X},{point.Y}"))),
            string.Join(';', links.Select(link => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{link.From}>{link.To}"))));
        if (_maps.TryGetValue(key, out var existing)) return existing;
        var map = FlatMap.Build(cells, cells, 1, waypoints: waypoints.Select(point => (point.Name, (decimal)point.X, (decimal)point.Y, 0m)));
        if (!links.IsDefaultOrEmpty) map = map with { WaypointLinks = [..links.Select(link => new MapWaypointLink(link.From, link.To))] };
        var built = (map, TerrainMap.Build(map, "rate-equivalence"));
        _maps[key] = built;
        return built;
    }
}

/// <summary>
/// One scene of a <see cref="RateCase"/> at one rate: the catalog of that rate, a flat map, two hostile players (1 and 2) who see the whole map, and a world
/// that is created on first use. Objects are spawned by template name; every spawn takes one frame of the original, so the game time before a measurement
/// is the same at every rate. A measurement starts with <see cref="Align"/> on a frame boundary of the original: the harness adds no sub-frame offset.
/// Units do not pick victims by themselves unless a case asks for it (<see cref="SelfAcquisition"/>).
/// </summary>
public sealed class RateScene
{
    /// <summary>Times are counted in 1/120 s: whole numbers at 30, 60 and 120 Hz.</summary>
    public const long TimeUnitsPerSecond = SimulationRate.OriginalFramesPerSecond * 4;

    public const int DefaultMapCells = 400;

    private readonly RateCatalogs _catalogs;
    private readonly Dictionary<int, ulong> _sequence = [];
    private readonly List<SimEvent> _events = [];
    private SimulationWorld? _world;

    public RateScene(RateCatalogs catalogs, SimulationRate rate)
    {
        _catalogs = catalogs;
        Rate = rate;
        Catalog = catalogs.Of(rate);
        Fingerprint = catalogs.FingerprintOf(rate);
    }

    public RateCatalogs Catalogs => _catalogs;
    public SimulationRate Rate { get; }
    public SimulationTime Time => Rate.Time;
    public TemplateCatalog Catalog { get; }
    public string Fingerprint { get; }

    /// <summary>Side length of the flat map in cells of ten units; set before the world is first used.</summary>
    public int MapCells { get; set; } = DefaultMapCells;

    /// <summary>Named waypoints of the map (start positions of a skirmish); set before the world is first used.</summary>
    public ImmutableArray<(string Name, int X, int Y)> Waypoints { get; set; } = [];

    /// <summary>Links between <see cref="Waypoints"/> (positions counted from 1): the tracks of a train. Set before the world is first used.</summary>
    public ImmutableArray<(int From, int To)> Links { get; set; } = [];

    public uint Seed { get; set; } = 7;

    /// <summary>Both players see the whole map from the start; false leaves the shroud to the objects (the vision cases). Set before the world is first used.</summary>
    public bool RevealMap { get; set; } = true;

    /// <summary>Idle units pick victims by themselves (<c>SimulationWorld.SelfAcquisition</c>); off unless the case is about it. Set before the world is first used.</summary>
    public bool SelfAcquisition { get; set; }

    public PlayerId One => new(1);
    public PlayerId Two => new(2);

    public (MapData Map, TerrainMap Terrain) Map => _catalogs.Flat(MapCells, Waypoints, Links);

    public SimulationWorld World => _world ??= CreateWorld();

    /// <summary>Every event since the world was created, in order.</summary>
    public IReadOnlyList<SimEvent> Events => _events;

    private SimulationWorld CreateWorld()
    {
        var terrain = Map.Terrain;
        var players = ImmutableArray.Create(new PlayerConfiguration(One, "East"), new PlayerConfiguration(Two, "West"));
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), Rate, Seed, players, "rate-equivalence", Fingerprint, terrain.Fingerprint), Catalog, terrain)
        {
            SelfAcquisition = SelfAcquisition,
        };
        if (!RevealMap) return world;
        world.RevealMapPermanently(One, true);
        world.RevealMapPermanently(Two, true);
        return world;
    }

    /// <summary>Whole seconds of game time in frames of the original, the unit of every budget of a scene.</summary>
    public static long Seconds(long seconds) => seconds * SimulationRate.OriginalFramesPerSecond;

    /// <summary>Ticks of this scene in 1/120 s.</summary>
    public long Units(long ticks) => ticks * (TimeUnitsPerSecond / Rate.TicksPerSecond);

    public TickResult Step()
    {
        var result = World.Step();
        foreach (var item in result.Events) _events.Add(item);
        return result;
    }

    /// <summary>Steps <paramref name="originalFrames"/> frames of the original (1/30 s of game time each).</summary>
    public void StepFrames(long originalFrames)
    {
        var ticks = Time.OriginalFrames(originalFrames);
        for (long index = 0; index < ticks; index++) Step();
    }

    /// <summary>Steps to the next tick a frame of the original begins with (none when the world is on one).</summary>
    public void Align()
    {
        while (!Time.IsOriginalFrameBoundary(World.Tick)) Step();
    }

    /// <summary>
    /// Steps until <paramref name="predicate"/> holds after a step, at most <paramref name="budgetFrames"/> frames of the original. Returns the ticks between
    /// the first step (the one that executes the orders given just before) and the step after which it held - 0 when it held after the first step - or -1
    /// when the budget ran out. Every time of the suite is counted this way: the tick something happened in, less the tick its order was executed in.
    /// </summary>
    public long StepUntil(Func<bool> predicate, long budgetFrames)
    {
        var budget = Time.OriginalFrames(budgetFrames);
        for (long step = 1; step <= budget; step++)
        {
            Step();
            if (predicate()) return step - 1;
        }
        return -1;
    }

    /// <summary>
    /// For a state of the world that is the 30 Hz state only after the last tick of a logic frame of the original (the shroud, which the world recomputes after the last tick of
    /// a frame): steps whole frames from a frame boundary until <paramref name="predicate"/> holds at the end of one, and returns the ticks between the first step and the first
    /// step of that frame - the time <see cref="StepUntil(Func{bool}, long)"/> returns at 30 Hz, where a frame is one step. -1 when the budget ran out.
    /// </summary>
    public long StepUntilFrameEnd(Func<bool> predicate, long budgetFrames)
    {
        Align();
        for (long frame = 0; frame < budgetFrames; frame++)
        {
            StepFrames(1);
            if (predicate()) return Time.OriginalFrames(frame);
        }
        return -1;
    }

    /// <summary>The same with the events of each step: <paramref name="predicate"/> sees the result of the step just taken.</summary>
    public long StepUntil(Func<TickResult, bool> predicate, long budgetFrames)
    {
        var budget = Time.OriginalFrames(budgetFrames);
        for (long step = 1; step <= budget; step++)
            if (predicate(Step())) return step - 1;
        return -1;
    }

    public SubmitResult Send(int player, Func<PlayerId, ulong, long, SimCommand> make)
    {
        var next = _sequence.GetValueOrDefault(player) + 1;
        _sequence[player] = next;
        return World.Submit(make(new PlayerId(player), next, World.Tick));
    }

    /// <summary>Sends a command that must be accepted; a rejection is an error of the case, not a measurement.</summary>
    public void Order(int player, Func<PlayerId, ulong, long, SimCommand> make)
    {
        var result = Send(player, make);
        if (!result.Accepted) throw new InvalidOperationException($"A command of player {player.ToString(System.Globalization.CultureInfo.InvariantCulture)} was rejected: {result.Reason}.");
    }

    public bool Has(string template) => Catalog.TryGet(template, out var found) && found.IsInstantiable;

    /// <summary>Spawns <paramref name="template"/> for <paramref name="player"/> and steps one frame of the original; returns the new object.</summary>
    public Entity Spawn(string template, int player, Fixed x, Fixed y, Fixed angle = default)
    {
        var tick = World.Tick;
        Order(player, (owner, sequence, now) => new SpawnCommand(owner, sequence, now, template, owner, x, y, Fixed.Zero, angle));
        StepFrames(1);
        Entity? found = null;
        foreach (var entity in World.Entities)
            if (entity.SpawnTick >= tick && entity.Owner == new PlayerId(player) && entity.Template.Name == template && (found is null || entity.Id.Value < found.Id.Value)) found = entity;
        return found ?? throw new InvalidOperationException($"{template} did not appear after its spawn command.");
    }

    public Entity Spawn(string template, int player, int x, int y) => Spawn(template, player, Fixed.FromInt(x), Fixed.FromInt(y));

    public void Move(Entity unit, Fixed x, Fixed y) => Order(unit.Owner.Value, (owner, sequence, now) => new SetMoveTargetCommand(owner, sequence, now, unit.Id, x, y));

    public void Move(Entity unit, int x, int y) => Move(unit, Fixed.FromInt(x), Fixed.FromInt(y));

    /// <summary>One fire order (the stream a player's attack is); returns whether the world accepted it.</summary>
    public bool Fire(Entity source, Entity target, int slot = WeaponSlot.Best)
        => Send(source.Owner.Value, (owner, sequence, now) => new FireWeaponCommand(owner, sequence, now, source.Id, target.Id, slot)).Accepted;

    public void GiveMoney(int player, int amount)
        => Order(0, (_, sequence, now) => new GiveMoneyCommand(PlayerId.System, sequence, now, new PlayerId(player), Fixed.FromInt(amount)));

    /// <summary>Unresistable damage from nobody (a system command).</summary>
    public void Hurt(Entity entity, Fixed amount)
        => Order(0, (_, sequence, now) => new DamageCommand(PlayerId.System, sequence, now, entity.Id, amount, DamageTypes.Unresistable, 0));

    /// <summary>Damages <paramref name="entity"/> down to <paramref name="numerator"/>/<paramref name="denominator"/> of its maximum health and steps one frame of the original.</summary>
    public void HurtTo(Entity entity, long numerator, long denominator)
    {
        var body = entity.Body ?? throw new InvalidOperationException($"{entity.Template.Name} has no body.");
        var wanted = Fixed.MultiplyDivide(body.MaxHealth, Fixed.FromInt(numerator), Fixed.FromInt(denominator));
        if (body.Health > wanted) Hurt(entity, body.Health - wanted);
        StepFrames(1);
    }

    public void Kill(Entity entity) => Order(0, (_, sequence, now) => new KillCommand(PlayerId.System, sequence, now, entity.Id));

    /// <summary>A scene of the same suite at the same rate with a world of its own, for a second independent measurement of a case.</summary>
    public RateScene Fresh() => new(_catalogs, Rate) { MapCells = MapCells, Seed = Seed };

    /// <summary>The 30 Hz template of the same name: what a case derives its staging from (distances, budgets), so that the staging is the same at every rate.</summary>
    public ObjectTemplate Reference(string template)
        => _catalogs.Reference.TryGet(template, out var found) ? found : throw new InvalidOperationException($"Unknown template {template}.");

    public bool Alive(Entity entity) => !entity.EffectivelyDead && !entity.DestroyRequested && World.FindEntity(entity.Id) == entity;

    /// <summary>The object has left the world (an object without a body counts as dead from the start, but exists until it is removed).</summary>
    public bool Gone(Entity entity) => World.FindEntity(entity.Id) != entity;

    /// <summary>The first object with this template that appeared at or after <paramref name="sinceTick"/> (lowest id), or null.</summary>
    public Entity? Find(string template, long sinceTick = 0)
    {
        Entity? found = null;
        foreach (var entity in World.Entities)
            if (entity.SpawnTick >= sinceTick && entity.Template.Name == template && (found is null || entity.Id.Value < found.Id.Value)) found = entity;
        return found;
    }
}
