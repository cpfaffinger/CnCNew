using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Data.World;
using CnCNew.Formats;
using CnCNew.Game.Data;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Game.Live;

/// <summary>One object a scenario places (<see cref="Tag"/> names it for the orders); positions are metres east/north of the map's first start position.</summary>
public sealed record ScenarioUnit(string Tag, string Template, int Owner, float X, float Y, float AngleDegrees = 0f, float Z = 0f);

public enum ScenarioOrderKind
{
    /// <summary>A standing attack order (chase and fire until the target is gone), the order a player gives with a right click.</summary>
    Attack,
    MoveTo,
    /// <summary>Capture the target structure: the command is retried every original frame until the simulation accepts it (a unit with the staged capture ability walks there itself; any other needs the actor in reach).</summary>
    Capture,
    /// <summary>Queue <see cref="ScenarioOrder.Template"/> at the actor (a producer); the unit that comes out is bound to the tag in <see cref="ScenarioOrder.Target"/>.</summary>
    Produce,
}

/// <summary>
/// An order given to <see cref="Actor"/> from the original frame <see cref="Frame"/> on (1/30 s of game time each; <see cref="Target"/> is another unit's tag; Move uses
/// <see cref="X"/>/<see cref="Y"/>). An order whose actor is not there yet (a unit still being produced) waits until it is.
/// </summary>
public sealed record ScenarioOrder(long Frame, string Actor, ScenarioOrderKind Kind, string? Target = null, float X = 0f, float Y = 0f, string? Template = null);

/// <summary>
/// A staged demonstration for auditing one behaviour on a real map: who stands where, what they are told and when, where the camera looks. The same definition runs in the
/// Stride client (<c>--scenario NAME</c>) and headless (<c>PresentationTool scenario-audit</c>), which prints the facts the demo is meant to show.
/// <see cref="Frames"/> is how long the demo runs, in original frames (game time: the same number of seconds at every tick rate).
/// </summary>
public sealed record DemoScenario(
    string Name, string Title, string Description, long Frames, ImmutableArray<ScenarioUnit> Units, ImmutableArray<ScenarioOrder> Orders,
    float LookX, float LookY, float CameraAngleDegrees = 0f, float CameraPitchDegrees = 35f, float CameraHeight = 220f)
{
    /// <summary>Money the human player starts with (production scenarios).</summary>
    public int StartMoney { get; init; }

    /// <summary>Player upgrades the human player owns from the start (the Ranger's capture ability stays paused until <c>Upgrade_InfantryCaptureBuilding</c>).</summary>
    public ImmutableArray<string> Upgrades { get; init; } = [];

    /// <summary>The special powers of the placed units are recharged at the start (a unit that has stood around for a while), instead of counting down from their creation.</summary>
    public bool PowersReady { get; init; }

    public string Map { get; init; } = "Maps/Alpine Assault/Alpine Assault.map";
    public string HumanFaction { get; init; } = "FactionAmerica";
    public string OpponentFaction { get; init; } = "FactionChina";
}

/// <summary>
/// A running scenario: a world with exactly the placed objects (no bases, no economy), a scripted second player and the viewer as player 1 with the whole map revealed.
/// Both sides' orders go through a <see cref="StandingOrders"/> book, the way a player's orders do, so a demo shows what the simulation does with the commands a player can give.
/// </summary>
public sealed class LiveScenario : LiveGame
{
    private readonly ICommandSink _commands;
    private readonly Dictionary<string, EntityId> _tags = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, StandingOrders> _books = [];
    private readonly Dictionary<int, ulong> _sequence = [];
    private readonly HashSet<int> _done = [];
    private readonly Dictionary<int, string> _pendingProduction = [];
    private readonly ImmutableArray<PlayerConfiguration> _players;

    private LiveScenario(GameData data, DemoScenario scenario, SimulationWorld world, MapData map, TerrainMap terrain, ImmutableArray<PlayerConfiguration> players, Vector2 viewport, Vector2 anchor)
        : base(data, world, map, scenario.Map, terrain, players, new PlayerId(1), 1u, viewport, anchor + new Vector2(scenario.LookX, scenario.LookY))
    {
        Scenario = scenario;
        Anchor = anchor;
        _players = players;
        _commands = new ScenarioCommandSink(this, new PlayerId(1));
    }

    public DemoScenario Scenario { get; }

    /// <summary>World position of the scenario's origin (the map's first start position).</summary>
    public Vector2 Anchor { get; }

    /// <summary>The placed objects by tag.</summary>
    public IReadOnlyDictionary<string, EntityId> Tags => _tags;

    public override string HumanFaction => Scenario.HumanFaction;
    public override bool IsOver => World.Tick >= LengthTicks;

    /// <summary>The length of the scenario in ticks of its world (<see cref="DemoScenario.Frames"/> converted).</summary>
    public long LengthTicks => World.Time.OriginalFrames(Scenario.Frames);
    public override PlayerId? Winner => null;
    public override ImmutableArray<PlayerConfiguration> Players => _players;
    protected override ICommandSink CreateCommandSink() => _commands;

    /// <summary>Submits a command of <paramref name="player"/> with that player's next sequence number.</summary>
    public SubmitResult Issue(PlayerId player, Func<ulong, long, SimCommand> make)
    {
        var next = _sequence.GetValueOrDefault(player.Value) + 1;
        _sequence[player.Value] = next;
        return World.Submit(make(next, World.Tick));
    }

    protected override TickResult StepSimulation()
    {
        // Orders are given, and retried, once per original frame: the commands of a demo follow game time, not the tick rate.
        var frameBegins = World.Time.IsOriginalFrameBoundary(World.Tick);
        for (var index = 0; frameBegins && index < Scenario.Orders.Length; index++)
        {
            var order = Scenario.Orders[index];
            if (World.Time.OriginalFrames(order.Frame) > World.Tick || _done.Contains(index)) continue;
            if (!_tags.TryGetValue(order.Actor, out var actor) || World.FindEntity(actor) is not { } entity) continue; // not produced yet (or gone)
            if (Apply(index, order, actor, entity)) _done.Add(index);
        }

        foreach (var (player, book) in _books) book.Update(World, new PlayerId(player), make => Issue(new PlayerId(player), make));
        var result = World.Step();
        BindProduced();
        return result;
    }

    /// <summary>Returns true when the order is finished (a capture the simulation still refuses, or an attack on a unit still being produced, is tried again in the next original frame).</summary>
    private bool Apply(int index, ScenarioOrder order, EntityId actor, Entity entity)
    {
        var player = entity.Owner;
        switch (order.Kind)
        {
            case ScenarioOrderKind.Attack when order.Target is not null && _tags.TryGetValue(order.Target, out var target) && World.FindEntity(target) is { } victim:
                Book(player).Set(actor, new StandingOrder(StandingOrderKind.Attack, target, new Vector2(victim.X.Raw / 65536f, victim.Y.Raw / 65536f), [], World.Tick));
                return true;
            case ScenarioOrderKind.Attack:
                return false; // the target is still to be produced
            case ScenarioOrderKind.MoveTo:
                Book(player).Clear(actor);
                Issue(player, (sequence, tick) => new SetMoveTargetCommand(player, sequence, tick, actor, Fixed.FromDecimal((decimal)(Anchor.X + order.X)), Fixed.FromDecimal((decimal)(Anchor.Y + order.Y))));
                return true;
            case ScenarioOrderKind.Capture when order.Target is not null && _tags.TryGetValue(order.Target, out var site):
                // The command is refused until the actor stands in reach; walk toward the structure meanwhile and try again every tick.
                if (Issue(player, (sequence, tick) => new CaptureCommand(player, sequence, tick, actor, site)).Accepted) return true;
                if (World.FindEntity(site) is { } building && !entity.HasMoveTarget)
                    Issue(player, (sequence, tick) => new SetMoveTargetCommand(player, sequence, tick, actor, building.X, building.Y));
                return false;
            case ScenarioOrderKind.Produce when order.Template is not null && order.Target is not null:
                if (!Issue(player, (sequence, tick) => new QueueProductionCommand(player, sequence, tick, actor, order.Template)).Accepted)
                    throw new InvalidOperationException($"Scenario '{Scenario.Name}': queueing '{order.Template}' at '{order.Actor}' was refused.");
                _pendingProduction[index] = order.Target;
                return true;
        }

        return true;
    }

    /// <summary>A produced unit gets the tag its order named: the oldest new object of that template and owner.</summary>
    private void BindProduced()
    {
        foreach (var (index, tag) in _pendingProduction.ToArray())
        {
            var order = Scenario.Orders[index];
            if (!_tags.TryGetValue(order.Actor, out var producer) || World.FindEntity(producer) is not { } owner) continue;
            var bound = _tags.Values.ToHashSet();
            var made = World.Entities.Where(entity => entity.Owner == owner.Owner && entity.Template.Name.Equals(order.Template, StringComparison.OrdinalIgnoreCase) && !bound.Contains(entity.Id))
                .OrderBy(entity => entity.Id.Value).FirstOrDefault();
            if (made is null) continue;
            _tags[tag] = made.Id;
            _pendingProduction.Remove(index);
        }
    }

    private StandingOrders Book(PlayerId player)
    {
        if (!_books.TryGetValue(player.Value, out var book)) _books[player.Value] = book = new StandingOrders();
        return book;
    }

    public static LiveScenario Create(GameData data, DemoScenario scenario, Vector2 viewport)
    {
        MapData map;
        using (var stream = data.Content.OpenRead(scenario.Map)) map = SageMapDecoder.Decode(stream);
        var terrain = TerrainMap.Build(map, scenario.Map);
        var anchor = new Vector2(terrain.StartPositions[0].X.Raw / 65536f, terrain.StartPositions[0].Y.Raw / 65536f);
        var players = ImmutableArray.Create(
            new PlayerConfiguration(new PlayerId(1), scenario.HumanFaction, scenario.HumanFaction),
            new PlayerConfiguration(new PlayerId(2), scenario.OpponentFaction, scenario.OpponentFaction, 0, true));
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), data.Templates.Rate, 1u, players, "demo-scenario", data.Fingerprint, terrain.Fingerprint), data.Templates, terrain);
        var live = new LiveScenario(data, scenario, world, map, terrain, players, viewport, anchor);
        live.Populate();
        return live;
    }

    private void Populate()
    {
        World.RevealMapPermanently(new PlayerId(1), true);
        World.RevealMapPermanently(new PlayerId(2), true);
        var before = World.Entities.Select(entity => entity.Id.Value).ToHashSet();
        var systemSequence = 0UL;
        foreach (var unit in Scenario.Units)
        {
            var x = Fixed.FromDecimal((decimal)(Anchor.X + unit.X));
            var y = Fixed.FromDecimal((decimal)(Anchor.Y + unit.Y));
            var angle = Fixed.FromDecimal((decimal)(unit.AngleDegrees * Math.PI / 180.0));
            var result = World.Submit(new SpawnCommand(PlayerId.System, ++systemSequence, World.Tick, unit.Template, new PlayerId(unit.Owner), x, y, Fixed.FromDecimal((decimal)unit.Z), angle));
            if (!result.Accepted) throw new InvalidOperationException($"Scenario '{Scenario.Name}': spawning '{unit.Template}' ({unit.Tag}) was rejected ({result.Reason}).");
        }

        if (Scenario.StartMoney > 0) World.Submit(new GiveMoneyCommand(PlayerId.System, ++systemSequence, World.Tick, new PlayerId(1), Fixed.FromInt(Scenario.StartMoney)));
        World.Step();
        // The spawn command creates the object in the tick after it was submitted; the tags are matched in spawn order.
        var created = World.Entities.Where(entity => !before.Contains(entity.Id.Value)).OrderBy(entity => entity.Id.Value).ToArray();
        if (created.Length != Scenario.Units.Length) throw new InvalidOperationException($"Scenario '{Scenario.Name}' placed {created.Length} objects for {Scenario.Units.Length} units.");
        for (var index = 0; index < created.Length; index++) _tags[Scenario.Units[index].Tag] = created[index].Id;

        foreach (var upgrade in Scenario.Upgrades) World.CompleteUpgrade(new PlayerId(1), null, upgrade);
        if (Scenario.PowersReady)
            foreach (var entity in created)
                foreach (var module in entity.Modules)
                    if (module is ISpecialPowerModule power) power.SetReadyFrame(World.Tick);
    }

    private sealed class ScenarioCommandSink(LiveScenario scenario, PlayerId player) : ICommandSink
    {
        public SubmitResult Issue(Func<ulong, long, SimCommand> make) => scenario.Issue(player, make);
    }
}
