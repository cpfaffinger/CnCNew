using System.Collections.Immutable;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Random;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.Timers;

namespace CnCNew.Simulation.World;

/// <summary><paramref name="Faction"/> names a <c>PlayerTemplate</c> (<see cref="Templates.FactionCatalog"/>); when set, the world credits its <c>StartMoney</c> and <c>IntrinsicSciences</c> once at construction (<c>Player::Player()</c>). <see langword="null"/>: no faction, starts with nothing (fixtures/tests that set up money and sciences explicitly).</summary>
public sealed record PlayerConfiguration(PlayerId Id, string Name, string? Faction = null);

/// <summary>Everything that identifies a run apart from the definitions and the command stream.</summary>
public sealed record WorldConfiguration(WorldId Id, uint Seed, ImmutableArray<PlayerConfiguration> Players, string FixtureId, string DefinitionFingerprint);

/// <summary>
/// Outcome of one tick. <see cref="Events"/> and <see cref="Rejections"/> are reused buffers: they are valid until the next
/// call to <see cref="SimulationWorld.Step"/>.
/// </summary>
public readonly record struct TickResult(long Tick, ulong StateHash, IReadOnlyList<SimEvent> Events);

/// <summary>Sizes of every table the world owns. A world at rest must return to its baseline (leak checks).</summary>
public readonly record struct WorldMetrics(
    int Entities, int Modules, int ScheduledUpdates, int Timers, int PendingEvents, int PendingCommands, int PendingSpawns,
    int PendingDestroys, long SpawnedTotal, long DestroyedTotal);

/// <summary>
/// The deterministic simulation core.
/// <para>
/// <b>Tick phases</b> (executed by <see cref="Step"/>, always in this order):
/// 1. <i>Commands</i> — commands addressed to this tick, ordered by (player id, sequence);
/// 2. <i>Timers</i> — due timers ordered by (due tick, timer id);
/// 3. <i>Updates</i> — sleepy updates ordered by (wake tick, entity id, module index); destroyed entities are skipped;
/// 4. <i>Events</i> — queued events are delivered in event-id order (events raised meanwhile are delivered in the same phase);
/// 5. <i>Destroy</i> — entities flagged during the tick are removed in ascending id order;
/// 6. <i>Spawn</i> — reserved entities are created in id order; their modules run <c>OnSpawn</c> (which may draw random numbers);
/// 7. <i>Events</i> — the events raised by the spawn phase are delivered;
/// 8. <i>Hash</i> — the tick counter advances and the state hash is computed.
/// Mutations requested while a phase iterates (destroy, spawn) are queued and applied in phases 5 and 6, never in place.
/// </para>
/// </summary>
public sealed partial class SimulationWorld : IWorldServices
{
    private const int MaxUpdatesPerTick = 50_000_000;
    private const int MaxEventsPerTick = 20_000_000;

    private readonly TemplateCatalog _templates;
    private readonly Dictionary<uint, Entity> _entities = [];
    private readonly List<Entity> _dirty = [];
    private readonly IndexedHeap<UpdateNode> _updates;
    private readonly IndexedHeap<SimTimer> _timers;
    private readonly Dictionary<ulong, SimTimer> _timerById = [];
    private readonly SortedDictionary<int, PlayerState> _players = [];
    private readonly PlayerState[] _playerList;
    private readonly SortedDictionary<long, List<SimCommand>> _commands = [];
    private readonly Queue<SimEvent> _events = new();
    private readonly List<SimEvent> _tickEvents = [];
    private readonly List<PendingSpawn> _spawnQueue = [];
    private readonly List<Entity> _destroyQueue = [];
    private readonly long[] _rejections = new long[25];
    private readonly HashStateWriter _hasher = new();
    private readonly HashStateWriter _globalHasher = new();
    private long _tick;
    private int _moduleCount;
    private int _pendingCommandCount;
    private long _spawnedTotal;
    private long _destroyedTotal;
    private ulong _entitySum;
    private ulong _timerSum;
    private ulong _commandSum;
    private UpdateNode? _currentUpdate;

    private sealed class PlayerState(PlayerId id, string name)
    {
        public PlayerId Id { get; } = id;
        public string Name { get; } = name;
        public ulong LastSequence { get; set; }

        // ----------------------------------------------------------------------------------------- phase 5: economy
        public Fixed Money { get; set; }
        /// <summary>Incrementally maintained sum of owned objects' <c>EnergyProduction</c> (<c>Energy::objectEnteringInfluence</c>/<c>Leaving</c>); negative means a net consumer.</summary>
        public int EnergyProduction { get; set; }
        public UpgradeMask CompletedUpgrades { get; set; }

        // ------------------------------------------------------------------------------------- phase 5: sciences
        public ScienceMask CompletedSciences { get; set; }
        /// <summary>"Science Purchase Points" (<c>Player::getSciencePurchasePoints</c>): a currency separate from money, normally earned via rank/experience (not modeled — see <see cref="IWorldServices.AddSciencePoints"/>).</summary>
        public int SciencePurchasePoints { get; set; }
    }

    private readonly record struct PendingSpawn(EntityId Id, int Template, PlayerId Owner, Fixed X, Fixed Y, Fixed Z, Fixed Angle);

    public SimulationWorld(WorldConfiguration configuration, TemplateCatalog templates)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(templates);
        Configuration = configuration;
        _templates = templates;
        Ids = new IdCounters();
        Streams = new RandomStreams(configuration.Seed);
        foreach (var player in configuration.Players)
        {
            if (player.Id.IsSystem) throw new ArgumentException("Player 0 is the built-in system authority.", nameof(configuration));
            var state = new PlayerState(player.Id, player.Name);
            if (player.Faction is { } factionName)
            {
                // Player::Player(): starting cash and intrinsic sciences are credited once, at construction.
                if (!templates.Factions.TryGet(factionName, out var faction))
                    throw new ArgumentException($"Unknown faction '{factionName}' for player {player.Id}.", nameof(configuration));
                state.Money = faction.StartMoney;
                state.CompletedSciences = faction.IntrinsicSciences;
            }
            if (!_players.TryAdd(player.Id.Value, state))
                throw new ArgumentException($"Duplicate player id {player.Id}.", nameof(configuration));
        }
        _players.Add(0, new PlayerState(PlayerId.System, "System"));
        _playerList = [.._players.Values];
        _updates = new IndexedHeap<UpdateNode>(CompareUpdates, node => node.HeapIndex, (node, index) => node.HeapIndex = index);
        _timers = new IndexedHeap<SimTimer>(CompareTimers, timer => timer.HeapIndex, (timer, index) => timer.HeapIndex = index);
        ComputeFullHash();
    }

    public WorldConfiguration Configuration { get; }
    public IdCounters Ids { get; }
    public RandomStreams Streams { get; }
    public TemplateCatalog Templates => _templates;

    /// <summary>Number of executed ticks; also the number of the next tick to execute.</summary>
    public long Tick => _tick;

    /// <summary>
    /// Hash of the complete logic state right now (after the last executed tick, including commands submitted since).
    /// Maintained incrementally: only entities touched since the last call are rehashed.
    /// </summary>
    public ulong StateHash => ComputeIncrementalHash();

    public SageRandom LogicRandom => Streams.Logic;
    public IReadOnlyCollection<Entity> Entities => _entities.Values;
    public long[] RejectionCounts => [.._rejections];

    public WorldMetrics Metrics => new(_entities.Count, _moduleCount, _updates.Count, _timers.Count, _events.Count, _pendingCommandCount,
        _spawnQueue.Count, _destroyQueue.Count, _spawnedTotal, _destroyedTotal);

    public Entity? FindEntity(EntityId id) => _entities.TryGetValue(id.Value, out var entity) ? entity : null;

    // ------------------------------------------------------------------------------ rule-level state mutators

    /// <summary>Sets and clears object status bits (rules such as construction or stealth will use this in phase 5).</summary>
    public void SetStatus(EntityId id, ulong setMask, ulong clearMask = 0)
    {
        var entity = FindEntity(id) ?? throw new ArgumentException($"Unknown entity {id}.", nameof(id));
        entity.Status = (entity.Status | setMask) & ~clearMask;
        MarkDirty(entity);
    }

    /// <summary>Sets the veterancy level (0 regular ... 3 heroic).</summary>
    public void SetVeterancy(EntityId id, int level)
    {
        if (level is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(level));
        var entity = FindEntity(id) ?? throw new ArgumentException($"Unknown entity {id}.", nameof(id));
        entity.VeterancyLevel = level;
        MarkDirty(entity);
    }

    // ------------------------------------------------------------------------------------- phase 5: economy, upgrades

    public UpgradeCatalog Upgrades => _templates.Upgrades;
    public WeaponCatalog Weapons => _templates.Weapons;
    public ScienceCatalog Sciences => _templates.Sciences;
    public Fixed UnitDamagedThreshold => _templates.UnitDamagedThreshold;
    public Fixed UnitReallyDamagedThreshold => _templates.UnitReallyDamagedThreshold;

    public ScienceMask PlayerSciences(PlayerId player) => _players.TryGetValue(player.Value, out var state) ? state.CompletedSciences : ScienceMask.Empty;
    public int SciencePointsOf(PlayerId player) => _players.TryGetValue(player.Value, out var state) ? state.SciencePurchasePoints : 0;

    public void AddSciencePoints(PlayerId player, int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (_players.TryGetValue(player.Value, out var state)) state.SciencePurchasePoints += amount;
    }

    public Fixed MoneyOf(PlayerId player) => _players.TryGetValue(player.Value, out var state) ? state.Money : Fixed.Zero;

    public void AddMoney(PlayerId player, Fixed amount)
    {
        if (amount.IsNegative) throw new ArgumentOutOfRangeException(nameof(amount));
        if (_players.TryGetValue(player.Value, out var state)) state.Money += amount;
    }

    /// <summary><c>Money::withdraw</c>: clamps to the available balance and returns the amount actually withdrawn.</summary>
    public Fixed WithdrawMoney(PlayerId player, Fixed amount)
    {
        if (amount.IsNegative) throw new ArgumentOutOfRangeException(nameof(amount));
        if (!_players.TryGetValue(player.Value, out var state)) return Fixed.Zero;
        var withdrawn = Fixed.Min(amount, state.Money);
        state.Money -= withdrawn;
        return withdrawn;
    }

    public UpgradeMask PlayerUpgrades(PlayerId player) => _players.TryGetValue(player.Value, out var state) ? state.CompletedUpgrades : UpgradeMask.Empty;

    public UpgradeMask UpgradeKeyMaskOf(Entity entity) => entity.CompletedUpgrades | PlayerUpgrades(entity.Owner);

    public void CompleteUpgrade(PlayerId player, EntityId? objectScope, string upgradeName)
    {
        if (!_templates.Upgrades.TryGet(upgradeName, out var info)) throw new ArgumentException($"Unknown upgrade '{upgradeName}'.", nameof(upgradeName));
        if (info.Scope == UpgradeScope.Player)
        {
            var state = _players.TryGetValue(player.Value, out var found) ? found : throw new ArgumentException($"Unknown player {player}.", nameof(player));
            state.CompletedUpgrades = state.CompletedUpgrades.Set(info.Index);
            Emit(SimEventKind.UpgradeCompleted, EntityId.Invalid, EntityId.Invalid, upgradeName, info.Index, player);
            // Player-scoped upgrades (Player::onUpgradeCompleted) re-evaluate every object of that player.
            foreach (var entity in _entities.Values) if (entity.Owner == player) ReevaluateUpgrades(entity);
        }
        else
        {
            var target = objectScope is { } id ? FindEntity(id) : null;
            if (target is null) throw new ArgumentException("An object-scoped upgrade requires a valid target entity.", nameof(objectScope));
            target.CompletedUpgrades = target.CompletedUpgrades.Set(info.Index);
            Emit(SimEventKind.UpgradeCompleted, target.Id, EntityId.Invalid, upgradeName, info.Index, player);
            ReevaluateUpgrades(target);
        }
    }

    public void RemoveUpgradeByName(Entity entity, string upgradeName)
    {
        // An unresolved name is not modeled at all yet (e.g. an object creation list name mistakenly used here): a no-op,
        // matching the reference's behaviour when the removal list names something outside the upgrade table.
        if (!_templates.Upgrades.TryGet(upgradeName, out var info)) return;
        if (info.Scope == UpgradeScope.Player)
        {
            if (_players.TryGetValue(entity.Owner.Value, out var state)) state.CompletedUpgrades = state.CompletedUpgrades.Clear(info.Index);
        }
        else
        {
            entity.CompletedUpgrades = entity.CompletedUpgrades.Clear(info.Index);
        }
        MarkDirty(entity);
        ReevaluateUpgrades(entity);
    }

    private void ReevaluateUpgrades(Entity entity)
    {
        var mask = UpgradeKeyMaskOf(entity);
        foreach (var module in entity.UpgradeModules) module.TryActivate(this, mask);
        MarkDirty(entity);
    }

    public EntityId RequestSpawn(int templateIndex, PlayerId owner, Fixed x, Fixed y, Fixed z, Fixed angle)
    {
        if (templateIndex < 0 || templateIndex >= _templates.Templates.Length) throw new ArgumentOutOfRangeException(nameof(templateIndex));
        var id = Ids.AllocateEntity();
        _spawnQueue.Add(new PendingSpawn(id, templateIndex, owner, x, y, z, angle));
        return id;
    }

    private static int ActiveWeaponIndex(Entity entity)
        => entity.HasPlayerUpgradeWeapon && entity.Template.PlayerUpgradeWeapon >= 0 ? entity.Template.PlayerUpgradeWeapon : entity.Template.DefaultWeapon;

    private static ArmorInfo ActiveArmor(Entity entity)
        => entity.HasPlayerUpgradeArmor && entity.Template.PlayerUpgradeArmor is { } upgraded ? upgraded : entity.Template.DefaultArmor;

    private static Modules.BuiltIn.ProductionUpdate? FindProductionModule(Entity entity)
        => Array.Find(entity.ModuleArray, module => module is Modules.BuiltIn.ProductionUpdate) as Modules.BuiltIn.ProductionUpdate;

    private static bool CanMove(Entity entity) => Array.Exists(entity.ModuleArray, module => module is Modules.BuiltIn.AIUpdateInterface);

    /// <summary><c>VictoryConditions::hasSinglePlayerBeenDefeated</c> (default flags NOBUILDINGS|NOUNITS): zero objects at all, of any kind.</summary>
    public bool IsPlayerDefeated(PlayerId player) => !_entities.Values.Any(entity => entity.Owner == player);

    /// <summary>
    /// "Last player standing" (phase 5 simplification: no teams/alliances). Returns the sole remaining player with at least
    /// one object, or <see langword="null"/> while two or more players (or zero) still have objects.
    /// </summary>
    public PlayerId? TryGetWinner()
    {
        PlayerId? winner = null;
        foreach (var player in _playerList)
        {
            if (player.Id.IsSystem || IsPlayerDefeated(player.Id)) continue;
            if (winner is not null) return null;
            winner = player.Id;
        }
        return winner;
    }

    // ----------------------------------------------------------------------------------------------- commands

    /// <summary>
    /// Validates and queues a command. Rejected commands are counted, raise a <c>CommandRejected</c> event and never change any
    /// other state. Sequences are per player and strictly increasing in submission order.
    /// </summary>
    public SubmitResult Submit(SimCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var reason = Validate(command);
        if (reason != 0)
        {
            RecordRejection(command, reason);
            return SubmitResult.Reject(reason);
        }
        _players[command.Player.Value].LastSequence = command.Sequence;
        if (!_commands.TryGetValue(command.Tick, out var list)) _commands[command.Tick] = list = [];
        list.Add(command);
        _pendingCommandCount++;
        _commandSum = unchecked(_commandSum + HashCommand(command));
        return SubmitResult.Ok;
    }

    private CommandRejection Validate(SimCommand command)
    {
        if (!_players.TryGetValue(command.Player.Value, out var player)) return CommandRejection.UnknownPlayer;
        if (command.Tick < _tick) return CommandRejection.Late;
        if (command.Sequence == 0) return CommandRejection.InvalidArguments;
        if (command.Sequence == player.LastSequence) return CommandRejection.Duplicate;
        if (command.Sequence < player.LastSequence) return CommandRejection.OutOfOrder;
        if (command.RequiresSystem && !command.Player.IsSystem) return CommandRejection.NotPermitted;
        switch (command)
        {
            case SpawnCommand spawn:
                if (!_templates.TryGet(spawn.Template, out var template) || !template.IsInstantiable) return CommandRejection.UnknownTemplate;
                if (spawn.Owner.Value != 0 && !_players.ContainsKey(spawn.Owner.Value)) return CommandRejection.UnknownPlayer;
                if (spawn.Owner.Value != 0 && !command.Player.IsSystem && spawn.Owner != command.Player) return CommandRejection.NotPermitted;
                break;
            case DamageCommand damage:
                if (damage.Amount.IsNegative || !DamageTypes.IsValid(damage.DamageType) || !DeathTypes.IsValid(damage.DeathType)) return CommandRejection.InvalidArguments;
                break;
            case SetTimerCommand timer:
                if (timer.DelayTicks < 0 || timer.RepeatTicks < 0 || !Enum.IsDefined(timer.Action) || !DamageTypes.IsValid(timer.DamageType) || timer.Amount.IsNegative)
                    return CommandRejection.InvalidArguments;
                if (!timer.Target.IsValid && timer.Action != TimerActionKind.Notify) return CommandRejection.InvalidArguments;
                break;

            // ----------------------------------------------------------------------------------------------- phase 5
            case GiveMoneyCommand give:
                if (!_players.ContainsKey(give.Target.Value)) return CommandRejection.UnknownPlayer;
                if (give.Amount.IsNegative) return CommandRejection.InvalidArguments;
                break;
            case PurchaseUpgradeCommand purchase:
            {
                if (!_templates.Upgrades.TryGet(purchase.Upgrade, out var info)) return CommandRejection.UnknownUpgrade;
                if (info.Scope == UpgradeScope.Object)
                {
                    if (purchase.Target is not { } targetId) return CommandRejection.InvalidArguments;
                    if (!_entities.TryGetValue(targetId.Value, out var target) || target.DestroyRequested) return CommandRejection.UnknownEntity;
                    if (target.Owner != command.Player) return CommandRejection.NotPermitted;
                    if (target.CompletedUpgrades.Has(info.Index)) return CommandRejection.AlreadyOwned;
                }
                else if (player.CompletedUpgrades.Has(info.Index)) return CommandRejection.AlreadyOwned;
                if (player.Money < info.BuildCost) return CommandRejection.InsufficientFunds;
                break;
            }
            case QueueProductionCommand queue:
            {
                if (!_templates.TryGet(queue.Template, out var queueTemplate) || !queueTemplate.IsInstantiable) return CommandRejection.UnknownTemplate;
                if (!_entities.TryGetValue(queue.Producer.Value, out var producer) || producer.DestroyRequested) return CommandRejection.UnknownEntity;
                if (producer.Owner != command.Player) return CommandRejection.NotPermitted;
                var production = FindProductionModule(producer);
                if (production is null) return CommandRejection.CannotProduce;
                if (!production.HasRoom) return CommandRejection.QueueFull;
                if (player.Money < queueTemplate.BuildCost) return CommandRejection.InsufficientFunds;
                break;
            }
            case CancelProductionCommand cancel:
            {
                if (!_entities.TryGetValue(cancel.Producer.Value, out var producer) || producer.DestroyRequested) return CommandRejection.UnknownEntity;
                if (producer.Owner != command.Player) return CommandRejection.NotPermitted;
                var production = FindProductionModule(producer);
                if (production is null || !production.HasSlot(cancel.Slot)) return CommandRejection.InvalidArguments;
                break;
            }
            case SetMoveTargetCommand move:
            {
                if (!_entities.TryGetValue(move.Target.Value, out var target) || target.DestroyRequested) return CommandRejection.UnknownEntity;
                if (target.Owner != command.Player) return CommandRejection.NotPermitted;
                if (!CanMove(target)) return CommandRejection.CannotMove;
                break;
            }
            case FireWeaponCommand fire:
            {
                if (!_entities.TryGetValue(fire.Source.Value, out var source) || source.DestroyRequested) return CommandRejection.UnknownEntity;
                if (source.Owner != command.Player) return CommandRejection.NotPermitted;
                if (!_entities.TryGetValue(fire.Target.Value, out var fireTarget) || fireTarget.DestroyRequested || fireTarget.Body is null) return CommandRejection.UnknownEntity;
                var weaponIndex = ActiveWeaponIndex(source);
                if (weaponIndex < 0) return CommandRejection.NoWeapon;
                if (_tick < source.WeaponReadyTick) return CommandRejection.WeaponNotReady;
                var weapon = _templates.Weapons[weaponIndex];
                var dx = fireTarget.X - source.X;
                var dy = fireTarget.Y - source.Y;
                if (dx * dx + dy * dy > weapon.AttackRange * weapon.AttackRange) return CommandRejection.OutOfRange;
                break;
            }
            case GiveSciencePointsCommand giveScience:
                if (!_players.ContainsKey(giveScience.Target.Value)) return CommandRejection.UnknownPlayer;
                if (giveScience.Amount < 0) return CommandRejection.InvalidArguments;
                break;
            case PurchaseScienceCommand purchaseScience:
            {
                if (!_templates.Sciences.TryGet(purchaseScience.Science, out var science)) return CommandRejection.UnknownScience;
                if (!CanPurchaseScience(player, science, out var reason)) return reason;
                break;
            }
            case EnterContainerCommand enter:
            {
                if (!_entities.TryGetValue(enter.Actor.Value, out var actor) || actor.DestroyRequested) return CommandRejection.UnknownEntity;
                if (!command.Player.IsSystem && actor.Owner != command.Player) return CommandRejection.NotPermitted;
                if (actor.ContainedBy.IsValid) return CommandRejection.CannotEnterContainer;
                if (!_entities.TryGetValue(enter.Container.Value, out var container) || container.DestroyRequested || container.Container is null) return CommandRejection.UnknownEntity;
                if (!container.Container.IsValidContainerFor(actor, true)) return CommandRejection.CannotEnterContainer;
                break;
            }
            case ExitContainerCommand exit:
            {
                if (!_entities.TryGetValue(exit.Actor.Value, out var actor) || actor.DestroyRequested) return CommandRejection.UnknownEntity;
                if (!actor.ContainedBy.IsValid) return CommandRejection.NotContained;
                var byContainer = _entities.GetValueOrDefault(actor.ContainedBy.Value);
                if (!command.Player.IsSystem && actor.Owner != command.Player && (byContainer is null || byContainer.Owner != command.Player))
                    return CommandRejection.NotPermitted;
                break;
            }
        }
        return 0;
    }

    /// <summary><c>Player::isCapableOfPurchasingScience</c>.</summary>
    private bool CanPurchaseScience(PlayerState player, Templates.ScienceInfo science, out CommandRejection reason)
    {
        if (player.CompletedSciences.Has(science.Index)) { reason = CommandRejection.AlreadyOwned; return false; }
        if (!_templates.Sciences.PrerequisitesSatisfied(player.CompletedSciences, science)) { reason = CommandRejection.MissingPrerequisite; return false; }
        if (science.PurchasePointCost <= 0) { reason = CommandRejection.ScienceNotPurchasable; return false; }
        if (player.SciencePurchasePoints < science.PurchasePointCost) { reason = CommandRejection.InsufficientSciencePurchasePoints; return false; }
        reason = 0;
        return true;
    }

    private void RecordRejection(SimCommand command, CommandRejection reason)
    {
        _rejections[(int)reason]++;
        Emit(SimEventKind.CommandRejected, EntityId.Invalid, EntityId.Invalid, command.GetType().Name, (long)reason, command.Player);
    }

    private void ExecuteCommands(long tick)
    {
        if (!_commands.Remove(tick, out var list)) return;
        // Player id first, then sequence. Sequences of one player are unique and increasing, so this is a total order.
        list.Sort(static (a, b) =>
        {
            var byPlayer = a.Player.Value.CompareTo(b.Player.Value);
            return byPlayer != 0 ? byPlayer : a.Sequence.CompareTo(b.Sequence);
        });
        foreach (var command in list)
        {
            _pendingCommandCount--;
            _commandSum = unchecked(_commandSum - HashCommand(command));
            Execute(command);
        }
    }

    private void Execute(SimCommand command)
    {
        switch (command)
        {
            case SpawnCommand spawn:
            {
                var template = _templates.TryGet(spawn.Template, out var found) ? found : throw new InvalidOperationException("Validated template disappeared.");
                var owner = spawn.Owner.Value == 0 ? command.Player : spawn.Owner;
                _spawnQueue.Add(new PendingSpawn(Ids.AllocateEntity(), template.Index, owner, spawn.X, spawn.Y, spawn.Z, spawn.Angle));
                break;
            }
            case DestroyCommand destroy:
            {
                var entity = FindEntity(destroy.Target);
                if (entity is null || entity.DestroyRequested) { RecordRejection(command, CommandRejection.UnknownEntity); break; }
                if (!command.Player.IsSystem && entity.Owner != command.Player) { RecordRejection(command, CommandRejection.NotPermitted); break; }
                RequestDestroy(destroy.Target);
                break;
            }
            case DamageCommand damage:
            {
                var entity = FindEntity(damage.Target);
                if (entity is null || entity.DestroyRequested) { RecordRejection(command, CommandRejection.UnknownEntity); break; }
                Damage(damage.Target, new DamageInfo(EntityId.Invalid, damage.Amount, damage.DamageType, damage.DeathType, false));
                break;
            }
            case KillCommand kill:
            {
                var entity = FindEntity(kill.Target);
                if (entity is null || entity.DestroyRequested) { RecordRejection(command, CommandRejection.UnknownEntity); break; }
                Kill(kill.Target);
                break;
            }
            case SetTimerCommand set:
            {
                if (set.Target.IsValid)
                {
                    var owner = FindEntity(set.Target);
                    if (owner is null || owner.DestroyRequested) { RecordRejection(command, CommandRejection.UnknownEntity); break; }
                }
                AddTimer(new SimTimer
                {
                    Id = Ids.AllocateTimer(),
                    DueTick = _tick + set.DelayTicks,
                    Owner = set.Target,
                    RepeatTicks = set.RepeatTicks,
                    Action = set.Action,
                    Amount = set.Amount,
                    DamageType = set.DamageType,
                    Tag = set.TimerTag,
                });
                break;
            }
            case CancelTimerCommand cancel:
            {
                if (!_timerById.TryGetValue(cancel.Timer.Value, out var timer)) { RecordRejection(command, CommandRejection.UnknownTimer); break; }
                RemoveTimer(timer);
                break;
            }

            // --------------------------------------------------------------------------------------------- phase 5
            case GiveMoneyCommand give:
                if (_players.TryGetValue(give.Target.Value, out var recipient)) recipient.Money += give.Amount;
                break;
            case PurchaseUpgradeCommand purchase:
            {
                if (!_templates.Upgrades.TryGet(purchase.Upgrade, out var info)) { RecordRejection(command, CommandRejection.UnknownUpgrade); break; }
                var payer = _players[command.Player.Value];
                Entity? scopeEntity = null;
                if (info.Scope == UpgradeScope.Object)
                {
                    scopeEntity = purchase.Target is { } targetId ? FindEntity(targetId) : null;
                    if (scopeEntity is null || scopeEntity.DestroyRequested) { RecordRejection(command, CommandRejection.UnknownEntity); break; }
                    if (scopeEntity.Owner != command.Player) { RecordRejection(command, CommandRejection.NotPermitted); break; }
                    if (scopeEntity.CompletedUpgrades.Has(info.Index)) { RecordRejection(command, CommandRejection.AlreadyOwned); break; }
                }
                else if (payer.CompletedUpgrades.Has(info.Index)) { RecordRejection(command, CommandRejection.AlreadyOwned); break; }
                if (payer.Money < info.BuildCost) { RecordRejection(command, CommandRejection.InsufficientFunds); break; }
                payer.Money -= info.BuildCost;
                CompleteUpgrade(command.Player, scopeEntity?.Id, purchase.Upgrade);
                break;
            }
            case QueueProductionCommand queue:
            {
                var producer = FindEntity(queue.Producer);
                if (producer is null || producer.DestroyRequested) { RecordRejection(command, CommandRejection.UnknownEntity); break; }
                if (producer.Owner != command.Player) { RecordRejection(command, CommandRejection.NotPermitted); break; }
                if (!_templates.TryGet(queue.Template, out var queueTemplate) || !queueTemplate.IsInstantiable) { RecordRejection(command, CommandRejection.UnknownTemplate); break; }
                var production = FindProductionModule(producer);
                if (production is null) { RecordRejection(command, CommandRejection.CannotProduce); break; }
                if (!production.HasRoom) { RecordRejection(command, CommandRejection.QueueFull); break; }
                var buyer = _players[producer.Owner.Value];
                if (buyer.Money < queueTemplate.BuildCost) { RecordRejection(command, CommandRejection.InsufficientFunds); break; }
                // ProductionUpdate.h: the full cost is withdrawn immediately at queue time, refunded in full on cancel.
                buyer.Money -= queueTemplate.BuildCost;
                production.Enqueue(queueTemplate.Index, queueTemplate.BuildCost, queueTemplate.BuildTicks);
                var producerNode = Array.Find(producer.Updates, node => ReferenceEquals(node.Module, production));
                if (producerNode is not null) WakeAt(producer, producerNode.Update, _tick);
                MarkDirty(producer);
                break;
            }
            case CancelProductionCommand cancelProduction:
            {
                var producer = FindEntity(cancelProduction.Producer);
                if (producer is null || producer.DestroyRequested) { RecordRejection(command, CommandRejection.UnknownEntity); break; }
                if (producer.Owner != command.Player) { RecordRejection(command, CommandRejection.NotPermitted); break; }
                var production = FindProductionModule(producer);
                if (production is null || !production.TryCancel(cancelProduction.Slot, out var refund)) { RecordRejection(command, CommandRejection.InvalidArguments); break; }
                if (_players.TryGetValue(producer.Owner.Value, out var refunded)) refunded.Money += refund;
                MarkDirty(producer);
                break;
            }
            case SetMoveTargetCommand move:
            {
                var target = FindEntity(move.Target);
                if (target is null || target.DestroyRequested) { RecordRejection(command, CommandRejection.UnknownEntity); break; }
                if (target.Owner != command.Player) { RecordRejection(command, CommandRejection.NotPermitted); break; }
                if (!CanMove(target)) { RecordRejection(command, CommandRejection.CannotMove); break; }
                target.HasMoveTarget = true;
                target.MoveTargetX = move.X;
                target.MoveTargetY = move.Y;
                var moveNode = Array.Find(target.Updates, node => node.Module is Modules.BuiltIn.AIUpdateInterface);
                if (moveNode is not null) WakeAt(target, moveNode.Update, _tick);
                MarkDirty(target);
                break;
            }
            case FireWeaponCommand fire:
            {
                var source = FindEntity(fire.Source);
                if (source is null || source.DestroyRequested) { RecordRejection(command, CommandRejection.UnknownEntity); break; }
                if (source.Owner != command.Player) { RecordRejection(command, CommandRejection.NotPermitted); break; }
                var fireTarget = FindEntity(fire.Target);
                if (fireTarget is null || fireTarget.DestroyRequested || fireTarget.Body is null) { RecordRejection(command, CommandRejection.UnknownEntity); break; }
                var weaponIndex = ActiveWeaponIndex(source);
                if (weaponIndex < 0) { RecordRejection(command, CommandRejection.NoWeapon); break; }
                if (_tick < source.WeaponReadyTick) { RecordRejection(command, CommandRejection.WeaponNotReady); break; }
                var weapon = _templates.Weapons[weaponIndex];
                var dx = fireTarget.X - source.X;
                var dy = fireTarget.Y - source.Y;
                if (dx * dx + dy * dy > weapon.AttackRange * weapon.AttackRange) { RecordRejection(command, CommandRejection.OutOfRange); break; }
                // _tick >= WeaponReadyTick was just confirmed above: an empty clip has finished its ClipReloadTime by now.
                if (source.WeaponClip == 0) source.WeaponClip = ClipCapacity(weapon);
                var damage = ActiveArmor(fireTarget).Adjust(weapon.DamageType, weapon.PrimaryDamage);
                source.LastFireTick = _tick;
                if (source.WeaponClip != int.MaxValue) source.WeaponClip--;
                source.WeaponReadyTick = source.WeaponClip == 0 ? _tick + weapon.ClipReloadTicks : _tick + weapon.DelayBetweenShotsTicks;
                MarkDirty(source);
                Damage(fireTarget.Id, new DamageInfo(source.Id, damage, weapon.DamageType, weapon.DeathType, false));
                Emit(SimEventKind.WeaponFired, source.Id, fireTarget.Id, weapon.Name, damage.Raw, command.Player);
                break;
            }
            case GiveSciencePointsCommand giveScience:
                if (_players.TryGetValue(giveScience.Target.Value, out var scienceRecipient)) scienceRecipient.SciencePurchasePoints += giveScience.Amount;
                break;
            case PurchaseScienceCommand purchaseScience:
            {
                if (!_templates.Sciences.TryGet(purchaseScience.Science, out var science)) { RecordRejection(command, CommandRejection.UnknownScience); break; }
                var buyer = _players[command.Player.Value];
                if (!CanPurchaseScience(buyer, science, out var reason)) { RecordRejection(command, reason); break; }
                // Player::attemptToPurchaseScience: withdraw the cost, then grant the science.
                buyer.SciencePurchasePoints -= science.PurchasePointCost;
                buyer.CompletedSciences = buyer.CompletedSciences.Set(science.Index);
                Emit(SimEventKind.ScienceCompleted, EntityId.Invalid, EntityId.Invalid, purchaseScience.Science, science.Index, command.Player);
                break;
            }
            case EnterContainerCommand enter:
            {
                var actor = FindEntity(enter.Actor);
                if (actor is null || actor.DestroyRequested) { RecordRejection(command, CommandRejection.UnknownEntity); break; }
                if (!command.Player.IsSystem && actor.Owner != command.Player) { RecordRejection(command, CommandRejection.NotPermitted); break; }
                if (actor.ContainedBy.IsValid) { RecordRejection(command, CommandRejection.CannotEnterContainer); break; }
                var container = FindEntity(enter.Container);
                if (container is null || container.DestroyRequested || container.Container is null) { RecordRejection(command, CommandRejection.UnknownEntity); break; }
                if (!container.Container.IsValidContainerFor(actor, true)) { RecordRejection(command, CommandRejection.CannotEnterContainer); break; }
                container.Container.Add(this, actor);
                actor.ContainedBy = container.Id; // Add() also does this; kept here too so every entry path is consistent.
                MarkDirty(actor);
                Emit(SimEventKind.EntityContained, actor.Id, container.Id, string.Empty, 0, command.Player);
                break;
            }
            case ExitContainerCommand exit:
            {
                var actor = FindEntity(exit.Actor);
                if (actor is null || actor.DestroyRequested) { RecordRejection(command, CommandRejection.UnknownEntity); break; }
                if (!actor.ContainedBy.IsValid) { RecordRejection(command, CommandRejection.NotContained); break; }
                var byContainer = FindEntity(actor.ContainedBy);
                if (!command.Player.IsSystem && actor.Owner != command.Player && (byContainer is null || byContainer.Owner != command.Player))
                { RecordRejection(command, CommandRejection.NotPermitted); break; }
                var previousContainer = actor.ContainedBy;
                byContainer?.Container?.Remove(this, actor, true);
                actor.ContainedBy = EntityId.Invalid; // Safety net: Remove() also does this; guards a dangling/missing container reference.
                MarkDirty(actor);
                Emit(SimEventKind.EntityUncontained, actor.Id, previousContainer, string.Empty, 0, command.Player);
                break;
            }
            default:
                throw new InvalidOperationException($"Unhandled command type {command.GetType().Name}.");
        }
    }

    // ------------------------------------------------------------------------------------------------- timers

    private static int CompareTimers(SimTimer a, SimTimer b)
    {
        var byDue = a.DueTick.CompareTo(b.DueTick);
        return byDue != 0 ? byDue : a.Id.Value.CompareTo(b.Id.Value);
    }

    private void AddTimer(SimTimer timer)
    {
        _timers.Push(timer);
        _timerById.Add(timer.Id.Value, timer);
        _timerSum = unchecked(_timerSum + HashTimer(timer));
        if (timer.Owner.IsValid && _entities.TryGetValue(timer.Owner.Value, out var owner))
        {
            (owner.Timers ??= []).Add(timer);
            MarkDirty(owner);
        }
    }

    private void RemoveTimer(SimTimer timer)
    {
        if (!_timers.Remove(timer)) return;
        _timerById.Remove(timer.Id.Value);
        _timerSum = unchecked(_timerSum - HashTimer(timer));
        if (timer.Owner.IsValid && _entities.TryGetValue(timer.Owner.Value, out var owner))
        {
            owner.Timers?.Remove(timer);
            MarkDirty(owner);
        }
    }

    private void FireTimers(long tick)
    {
        while (_timers.Count > 0 && _timers.Peek().DueTick <= tick)
        {
            var timer = _timers.Pop();
            _timerSum = unchecked(_timerSum - HashTimer(timer));
            Entity? owner = null;
            if (timer.Owner.IsValid)
            {
                owner = FindEntity(timer.Owner);
                if (owner is null)
                {
                    _timerById.Remove(timer.Id.Value);
                    continue;
                }
                MarkDirty(owner);
            }
            Emit(SimEventKind.TimerFired, timer.Owner, EntityId.Invalid, string.Empty, timer.Tag);
            if (owner is not null && !owner.DestroyRequested)
            {
                switch (timer.Action)
                {
                    case TimerActionKind.Damage:
                        Damage(timer.Owner, new DamageInfo(EntityId.Invalid, timer.Amount, timer.DamageType, DeathTypes.Normal, false));
                        break;
                    case TimerActionKind.Kill:
                        Kill(timer.Owner);
                        break;
                    case TimerActionKind.Destroy:
                        RequestDestroy(timer.Owner);
                        break;
                }
            }
            if (timer.RepeatTicks > 0 && (owner is null || !owner.DestroyRequested))
            {
                timer.DueTick = tick + timer.RepeatTicks;
                _timers.Push(timer);
                _timerSum = unchecked(_timerSum + HashTimer(timer));
            }
            else
            {
                _timerById.Remove(timer.Id.Value);
                owner?.Timers?.Remove(timer);
            }
        }
    }

    // ------------------------------------------------------------------------------------------------ updates

    private static int CompareUpdates(UpdateNode a, UpdateNode b)
    {
        var byWake = a.Update.NextWake.CompareTo(b.Update.NextWake);
        if (byWake != 0) return byWake;
        var byEntity = a.Entity.Id.Value.CompareTo(b.Entity.Id.Value);
        return byEntity != 0 ? byEntity : a.Module.Index.CompareTo(b.Module.Index);
    }

    private void RunUpdates(long tick)
    {
        var processed = 0;
        while (_updates.Count > 0)
        {
            var node = _updates.Peek();
            if (node.Update.NextWake > tick) break;
            _updates.Pop();
            var entity = node.Entity;
            if (entity.DestroyRequested) continue;
            if (++processed > MaxUpdatesPerTick) throw new InvalidOperationException("Update storm: too many module updates in one tick.");
            MarkDirty(entity);
            _currentUpdate = node;
            var sleep = node.Update.Update(this);
            _currentUpdate = null;
            if (sleep < SimulationTiming.SleepNone) sleep = SimulationTiming.SleepNone;
            var wake = Math.Min(tick + sleep, SimulationTiming.SleepForever);
            node.Update.NextWake = wake;
            if (wake < SimulationTiming.SleepForever) _updates.Push(node);
        }
    }

    public void WakeAt(Entity entity, IUpdateModule module, long tick)
    {
        var node = Array.Find(entity.Updates, candidate => ReferenceEquals(candidate.Update, module))
            ?? throw new ArgumentException("The module is not an update module of this entity.", nameof(module));
        // friend_awakenUpdateModule ignores calls from inside the module's own update: its return value decides.
        if (ReferenceEquals(node, _currentUpdate)) return;
        var requested = Math.Min(tick, SimulationTiming.SleepForever);
        if (requested == module.NextWake) return;
        // "Already awake" special case: asking an awake module for the next tick must not postpone this tick's call.
        if (_tick > 0 && module.NextWake == _tick && requested == _tick + SimulationTiming.SleepNone) return;
        module.NextWake = requested;
        if (!entity.Registered) return;
        MarkDirty(entity);
        if (node.HeapIndex >= 0)
        {
            if (requested >= SimulationTiming.SleepForever) _updates.Remove(node); else _updates.Update(node);
        }
        else if (requested < SimulationTiming.SleepForever) _updates.Push(node);
    }

    // -------------------------------------------------------------------------------------- damage and death

    public DamageOutcome Damage(EntityId target, in DamageInfo info)
    {
        var entity = FindEntity(target);
        if (entity is null || entity.DestroyRequested || entity.Body is null) return DamageOutcome.NoEffect;
        MarkDirty(entity);
        return entity.Body.AttemptDamage(this, info);
    }

    public void Kill(EntityId target)
    {
        var entity = FindEntity(target);
        if (entity?.Body is null) return;
        // Object::kill: unresistable damage equal to the maximum health, with the kill flag.
        Damage(target, new DamageInfo(EntityId.Invalid, entity.Body.MaxHealth, DamageTypes.Unresistable, DeathTypes.Normal, true));
    }

    public void OnEntityDied(Entity entity, in DamageInfo info)
    {
        Emit(SimEventKind.EntityDied, entity.Id, info.Source, string.Empty, info.DeathType);
        foreach (var die in entity.Dies)
        {
            MarkDirty(entity);
            die.OnDie(this, info);
        }
    }

    public void RequestDestroy(EntityId id)
    {
        var entity = FindEntity(id);
        if (entity is null || entity.DestroyRequested) return;
        entity.DestroyRequested = true;
        entity.Status |= 1UL << ObjectStatusBits.Destroyed;
        MarkDirty(entity);
        _destroyQueue.Add(entity);
        Emit(SimEventKind.EntityDestroyed, id, EntityId.Invalid, entity.Template.Name, 0);
    }

    // ------------------------------------------------------------------------------------------------ events

    public void Emit(SimEventKind kind, EntityId entity, EntityId other, string text, long value)
        => Emit(kind, entity, other, text, value, PlayerId.System);

    private void Emit(SimEventKind kind, EntityId entity, EntityId other, string text, long value, PlayerId player)
        => _events.Enqueue(new SimEvent(Ids.AllocateEvent(), _tick, kind, entity, other, player, text, value));

    private void DeliverEvents()
    {
        var delivered = 0;
        while (_events.Count > 0)
        {
            var simEvent = _events.Dequeue();
            if (++delivered > MaxEventsPerTick) throw new InvalidOperationException("Event storm: too many events in one tick.");
            _tickEvents.Add(simEvent);
            if (!simEvent.Entity.IsValid || !_entities.TryGetValue(simEvent.Entity.Value, out var entity)) continue;
            foreach (var receiver in entity.Receivers)
            {
                MarkDirty(entity);
                receiver.OnEvent(this, simEvent);
            }
        }
    }

    // -------------------------------------------------------------------------------- destroy and spawn phases

    private static readonly Comparison<Entity> ById = static (a, b) => a.Id.Value.CompareTo(b.Id.Value);

    private void ProcessDestroys()
    {
        if (_destroyQueue.Count == 0) return;
        _destroyQueue.Sort(ById);
        foreach (var entity in _destroyQueue)
        {
            foreach (var module in entity.ModuleArray) module.OnRemove(this);
            foreach (var node in entity.Updates) _updates.Remove(node);
            if (entity.Timers is { Count: > 0 } timers)
                foreach (var timer in timers.ToArray()) RemoveTimer(timer);
            // Energy::objectLeavingInfluence: the mirror image of the Register() hook.
            if (_players.TryGetValue(entity.Owner.Value, out var owner)) owner.EnergyProduction -= entity.Template.EnergyProduction;
            _entitySum = unchecked(_entitySum - entity.CachedHash);
            _entities.Remove(entity.Id.Value);
            entity.Registered = false;
            _moduleCount -= entity.ModuleArray.Length;
            _destroyedTotal++;
        }
        _destroyQueue.Clear();
    }

    private void ProcessSpawns()
    {
        if (_spawnQueue.Count == 0) return;
        foreach (var pending in _spawnQueue)
        {
            var template = _templates[pending.Template];
            var entity = new Entity(pending.Id, template, pending.Owner, _tick)
            {
                X = pending.X,
                Y = pending.Y,
                Z = pending.Z,
                Angle = pending.Angle,
            };
            AttachModules(entity, template);
            InitializeWeaponClip(entity);
            foreach (var module in entity.ModuleArray) module.OnSpawn(this);
            Register(entity);
            Emit(SimEventKind.EntitySpawned, entity.Id, EntityId.Invalid, template.Name, 0, pending.Owner);
            _spawnedTotal++;
        }
        _spawnQueue.Clear();
    }

    private void AttachModules(Entity entity, ObjectTemplate template)
    {
        var modules = new SimModule[template.Modules.Length];
        var dies = new List<IDieModule>();
        var receivers = new List<IEventReceiver>();
        var updates = new List<UpdateNode>();
        var upgrades = new List<IUpgradeModule>();
        var damages = new List<IDamageModule>();
        for (var index = 0; index < modules.Length; index++)
        {
            var spec = template.Modules[index];
            var module = spec.Binding.Create(spec.Data);
            module.Entity = entity;
            module.Index = index;
            module.Tag = spec.Tag;
            modules[index] = module;
            if (module is IBodyModule body && entity.Body is null) entity.Body = body;
            if (module is IContainerModule container && entity.Container is null) entity.Container = container;
            if (module is IDieModule die) dies.Add(die);
            if (module is IEventReceiver receiver) receivers.Add(receiver);
            if (module is IUpdateModule update) updates.Add(new UpdateNode(entity, module, update));
            if (module is IUpgradeModule upgrade) upgrades.Add(upgrade);
            if (module is IDamageModule damage) damages.Add(damage);
        }
        entity.ModuleArray = modules;
        entity.DamageModules = [..damages];
        entity.Dies = [..dies];
        entity.Receivers = [..receivers];
        entity.Updates = [..updates];
        entity.UpgradeModules = [..upgrades];
    }

    /// <summary>Weapon clip starts full for whichever weapon is active (<c>WeaponSetUpgrade</c> re-initializes it if it switches later). Fresh spawns only: a restored entity already carries its saved clip.</summary>
    private void InitializeWeaponClip(Entity entity)
    {
        var weaponIndex = ActiveWeaponIndex(entity);
        entity.WeaponClip = weaponIndex < 0 ? -1 : ClipCapacity(_templates.Weapons[weaponIndex]);
    }

    private static int ClipCapacity(Templates.WeaponInfo weapon) => weapon.ClipSize == 0 ? int.MaxValue : weapon.ClipSize;

    /// <summary>Adds a fully built entity (fresh or restored) to the tables and schedules its update modules.</summary>
    private void Register(Entity entity)
    {
        _entities.Add(entity.Id.Value, entity);
        entity.Registered = true;
        _moduleCount += entity.ModuleArray.Length;
        // Energy::objectEnteringInfluence: incremental, not recomputed per frame.
        if (_players.TryGetValue(entity.Owner.Value, out var owner)) owner.EnergyProduction += entity.Template.EnergyProduction;
        foreach (var node in entity.Updates)
        {
            var update = node.Update;
            if (update.NextWake < 0) update.NextWake = _tick + SimulationTiming.SleepNone;
            if (update.NextWake > SimulationTiming.SleepForever) update.NextWake = SimulationTiming.SleepForever;
            if (update.NextWake < SimulationTiming.SleepForever) _updates.Push(node);
        }
        MarkDirty(entity);
    }

    private void MarkDirty(Entity entity)
    {
        if (entity.Dirty) return;
        entity.Dirty = true;
        _dirty.Add(entity);
    }

    // ------------------------------------------------------------------------------------------------- step

    /// <summary>Executes one tick and returns its state hash. The returned buffers are reused by the next call.</summary>
    public TickResult Step()
    {
        var tick = _tick;
        _tickEvents.Clear();
        ExecuteCommands(tick);
        FireTimers(tick);
        RunUpdates(tick);
        DeliverEvents();
        ProcessDestroys();
        ProcessSpawns();
        DeliverEvents();
        _tick = tick + 1;
        return new TickResult(tick, ComputeIncrementalHash(), _tickEvents);
    }

    // -------------------------------------------------------------------------------------------------- hash

    private ulong HashEntity(Entity entity)
    {
        _hasher.Reset();
        entity.Describe(_hasher);
        return _hasher.Hash;
    }

    private ulong HashTimer(SimTimer timer)
    {
        _hasher.Reset();
        timer.Describe(_hasher);
        return _hasher.Hash;
    }

    private ulong HashCommand(SimCommand command)
    {
        _hasher.Reset();
        command.Describe(_hasher);
        return _hasher.Hash;
    }

    private ulong ComputeIncrementalHash()
    {
        foreach (var entity in _dirty)
        {
            entity.Dirty = false;
            if (!entity.Registered) continue;
            var hash = HashEntity(entity);
            _entitySum = unchecked(_entitySum - entity.CachedHash + hash);
            entity.CachedHash = hash;
        }
        _dirty.Clear();
        return FinishHash();
    }

    /// <summary>Recomputes every hash from scratch (verification, snapshot integrity). Also refreshes the incremental caches.</summary>
    public ulong ComputeFullHash()
    {
        ulong entitySum = 0;
        foreach (var entity in _entities.Values)
        {
            var hash = HashEntity(entity);
            entitySum = unchecked(entitySum + hash);
            entity.CachedHash = hash;
            entity.Dirty = false;
        }
        _dirty.Clear();
        _entitySum = entitySum;
        ulong timerSum = 0;
        foreach (var timer in _timerById.Values) timerSum = unchecked(timerSum + HashTimer(timer));
        _timerSum = timerSum;
        ulong commandSum = 0;
        foreach (var list in _commands.Values)
            foreach (var command in list) commandSum = unchecked(commandSum + HashCommand(command));
        _commandSum = commandSum;
        return FinishHash();
    }

    /// <summary>Throws when the incrementally maintained hash differs from a full recomputation.</summary>
    public void VerifyIncrementalHash()
    {
        var incremental = ComputeIncrementalHash();
        var full = ComputeFullHash();
        if (full != incremental)
            throw new InvalidOperationException($"Incremental state hash {incremental:X16} differs from the full hash {full:X16}.");
    }

    private ulong FinishHash()
    {
        var hash = _globalHasher;
        hash.Reset();
        hash.BeginObject(null);
        DescribeGlobal(hash);
        hash.UInt("entitySum", _entitySum);
        hash.UInt("timerSum", _timerSum);
        hash.UInt("commandSum", _commandSum);
        hash.EndObject();
        return hash.Hash;
    }

    private void DescribeGlobal(IStateWriter writer)
    {
        writer.Int("tick", _tick);
        Ids.Describe(writer);
        Streams.DescribeLogic(writer);
        writer.Int("entities", _entities.Count);
        writer.Int("timers", _timerById.Count);
        writer.Int("pendingCommands", _pendingCommandCount);
        writer.Int("spawnedTotal", _spawnedTotal);
        writer.Int("destroyedTotal", _destroyedTotal);
        writer.BeginArray("players");
        foreach (var player in _playerList)
        {
            writer.BeginObject(null);
            writer.Int("id", player.Id.Value);
            writer.Text("name", player.Name);
            writer.UInt("lastSequence", player.LastSequence);
            writer.Fixed("money", player.Money);
            writer.Int("energyProduction", player.EnergyProduction);
            player.CompletedUpgrades.Describe(writer, "completedUpgrades");
            player.CompletedSciences.Describe(writer, "completedSciences");
            writer.Int("sciencePurchasePoints", player.SciencePurchasePoints);
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("rejections");
        foreach (var count in _rejections)
        {
            writer.BeginObject(null);
            writer.Int("count", count);
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("spawnQueue");
        foreach (var pending in _spawnQueue)
        {
            writer.BeginObject(null);
            writer.UInt("id", pending.Id.Value);
            writer.Int("template", pending.Template);
            writer.Int("owner", pending.Owner.Value);
            writer.Fixed("x", pending.X);
            writer.Fixed("y", pending.Y);
            writer.Fixed("z", pending.Z);
            writer.Fixed("angle", pending.Angle);
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("destroyQueue");
        foreach (var entity in _destroyQueue)
        {
            writer.BeginObject(null);
            writer.UInt("id", entity.Id.Value);
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("events");
        foreach (var simEvent in _events) simEvent.Describe(writer);
        writer.EndArray();
    }
}
