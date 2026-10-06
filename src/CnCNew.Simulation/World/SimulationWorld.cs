using System.Collections.Immutable;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Random;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.Timers;

namespace CnCNew.Simulation.World;

/// <summary><paramref name="IsComputer"/> is <c>PLAYER_COMPUTER</c> (the AI-driven modules such as <c>AutoFindHealingUpdate</c> only act for computer players); <see langword="false"/> is <c>PLAYER_HUMAN</c>. <paramref name="Team"/> groups players into an alliance (same non-zero team: mutual <see cref="Relationship.Allies"/>; 0: no team, everybody else is an enemy); <see cref="SetRelationshipCommand"/> overrides single directed relations afterwards. <paramref name="Faction"/> names a <c>PlayerTemplate</c> (<see cref="Templates.FactionCatalog"/>); when set, the world credits its <c>StartMoney</c> and <c>IntrinsicSciences</c> once at construction (<c>Player::Player()</c>). <see langword="null"/>: no faction, starts with nothing (fixtures/tests that set up money and sciences explicitly).</summary>
public sealed record PlayerConfiguration(PlayerId Id, string Name, string? Faction = null, int Team = 0, bool IsComputer = false);

/// <summary>Everything that identifies a run apart from the definitions and the command stream.</summary>
public sealed record WorldConfiguration(WorldId Id, uint Seed, ImmutableArray<PlayerConfiguration> Players, string FixtureId, string DefinitionFingerprint, string TerrainFingerprint = "");

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
    // Looked up by id, enumerated in ascending id order: the same in a running world and in one restored from a snapshot (EntityTable, finding D3).
    private readonly EntityTable _entities = new();
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
    private readonly long[] _rejections = new long[(int)CommandRejection.InvalidWeaponTarget + 1];

    /// <summary>Size of the rejection table when the state hash first covered it (reasons up to <see cref="CommandRejection.CannotToggleOvercharge"/>); later reasons are hashed only when they occurred.</summary>
    private const int LegacyRejectionEntries = (int)CommandRejection.CannotToggleOvercharge + 1;
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

    private sealed class PlayerState(PlayerId id, string name, int team, bool isComputer)
    {
        public PlayerId Id { get; } = id;
        public string Name { get; } = name;
        public int Team { get; } = team;
        public bool IsComputer { get; } = isComputer;
        public ulong LastSequence { get; set; }

        // ------------------------------------------------------------------------------------- teams and diplomacy
        /// <summary>Directed relation overrides by other player id (<c>Player::setPlayerRelationship</c>); absent entries fall back to the team default.</summary>
        public SortedList<int, Relationship> Relations { get; } = [];

        // ----------------------------------------------------------------------------------------- phase 5: economy
        public Fixed Money { get; set; }
        /// <summary>Incrementally maintained sum of owned objects' <c>EnergyProduction</c> (<c>Energy::objectEnteringInfluence</c>/<c>Leaving</c>); negative means a net consumer.</summary>
        public int EnergyProduction { get; set; }
        public UpgradeMask CompletedUpgrades { get; set; }

        // ------------------------------------------------------------------------------------- phase 5: sciences
        public ScienceMask CompletedSciences { get; set; }
        /// <summary>"Science Purchase Points" (<c>Player::getSciencePurchasePoints</c>): a currency separate from money, normally earned via rank/experience (not modeled — see <see cref="IWorldServices.AddSciencePoints"/>).</summary>
        public int SciencePurchasePoints { get; set; }

        /// <summary><c>Player::m_canBuildBase</c> / <c>m_canBuildUnits</c> (<c>Player::allowedToBuild</c>): switched off by the <c>PLAYER_DISABLE_*_CONSTRUCTION</c> script actions.</summary>
        public bool CanBuildBase { get; set; } = true;
        public bool CanBuildUnits { get; set; } = true;

        // ------------------------------------------------------------------------------------- special powers
        /// <summary>Power index to shared ready frame (<c>SharedSyncedTimer</c> powers), ordered for deterministic output.</summary>
        public SortedList<int, long> SharedReadyFrames { get; } = [];
        /// <summary><c>Player::m_cashBountyPercent</c> as a ratio.</summary>
        public Fixed CashBounty { get; set; }

        /// <summary><c>Player::getTunnelSystem</c>.</summary>
        public TunnelTracker Tunnels { get; } = new();

        /// <summary><c>Energy::m_powerSabotagedTillFrame</c> (<c>SabotagePowerPlantCrateCollide</c>): the tick until which the player's power counts as sabotaged; 0 = never.</summary>
        public long PowerSabotagedUntil { get; set; }

        /// <summary>The strategy centers' battle plans in force (<c>SimulationWorld.BattlePlan.cs</c>); <see langword="null"/> until a plan was ever started.</summary>
        public BattlePlanState? BattlePlan { get; set; }

        /// <summary><c>Player::m_kindOfPercentProductionChangeList</c> (<c>CostModifierUpgrade</c>); empty for every player no such upgrade ever touched.</summary>
        public List<CostModifier> CostModifiers { get; } = [];
    }

    /// <summary>
    /// Creation-list adjustments applied while an entity is being created (the default value means none).
    /// <see cref="HealthPercent"/> is <c>setInitialHealth</c>'s integer percentage; <see cref="Veterancy"/> only applies to trainable templates;
    /// lifetime ticks override a <c>LifetimeUpdate</c> range when the maximum is positive; <see cref="Container"/> is a
    /// <c>PutInContainer</c> entity spawned earlier in the same queue; <see cref="ContainInSource"/> makes the entity enter the source's
    /// container or be destroyed; <see cref="TransferDamage"/> is unresistable damage dealt right after creation (<c>TransferPreviousHealth</c>);
    /// <see cref="MaxHealth"/> and <see cref="OriginTemplate"/> carry <see cref="SpawnExtras"/>.
    /// </summary>
    private readonly record struct SpawnSetup(bool HasHealth, int HealthPercent, bool HasVeterancy, int Veterancy, long MinLifetimeTicks, long MaxLifetimeTicks,
        EntityId Container, EntityId ContainInSource, Fixed TransferDamage, EntityId TransferSource, Fixed MaxHealth = default, bool HasOrigin = false, int OriginTemplate = 0, bool ForceSupplyWanting = false,
        bool HasRally = false, Fixed RallyX = default, Fixed RallyY = default, ProjectileLaunch? Launch = null, Modules.BuiltIn.DeliveryOrder? Delivery = null, bool SetCreator = false, EntityId Creator = default, EntityId Enslaver = default, EntityId Producer = default);

    private readonly record struct PendingSpawn(EntityId Id, int Template, PlayerId Owner, Fixed X, Fixed Y, Fixed Z, Fixed Angle, SpawnSetup Setup = default);

    public SimulationWorld(WorldConfiguration configuration, TemplateCatalog templates, TerrainMap? terrain = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(templates);
        if (!string.Equals(configuration.TerrainFingerprint, terrain?.Fingerprint ?? string.Empty, StringComparison.Ordinal))
            throw new ArgumentException("The configured terrain fingerprint does not match the supplied terrain.", nameof(terrain));
        Configuration = configuration;
        Terrain = terrain;
        Navigation = terrain is null ? null : new NavigationGrid(terrain);
        _templates = templates;
        Ids = new IdCounters();
        Streams = new RandomStreams(configuration.Seed);
        foreach (var player in configuration.Players)
        {
            if (player.Id.IsSystem) throw new ArgumentException("Player 0 is the built-in system authority.", nameof(configuration));
            if (player.Team < 0) throw new ArgumentException($"Negative team for player {player.Id}.", nameof(configuration));
            var state = new PlayerState(player.Id, player.Name, player.Team, player.IsComputer);
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
        _players.Add(0, new PlayerState(PlayerId.System, "System", 0, false));
        _playerList = [.._players.Values];
        _updates = new IndexedHeap<UpdateNode>(CompareUpdates, node => node.HeapIndex, (node, index) => node.HeapIndex = index);
        _timers = new IndexedHeap<SimTimer>(CompareTimers, timer => timer.HeapIndex, (timer, index) => timer.HeapIndex = index);
        if (terrain is not null) InitializeVision();
        ComputeFullHash();
    }

    public WorldConfiguration Configuration { get; }
    public IdCounters Ids { get; }
    public RandomStreams Streams { get; }
    public TemplateCatalog Templates => _templates;
    public TerrainMap? Terrain { get; }
    public NavigationGrid? Navigation { get; }

    public Fixed GroundHeightAt(Fixed x, Fixed y) => Terrain is { } terrain ? terrain.HeightAt(x, y) : Fixed.Zero;

    /// <summary>The mission script engine's topple-direction table (installed while a script has set one).</summary>
    public Func<Entity, (Fixed X, Fixed Y)?>? ToppleDirectionOverride { get; set; }

    public bool TryGetToppleDirectionOverride(Entity entity, out Fixed x, out Fixed y)
    {
        var direction = ToppleDirectionOverride?.Invoke(entity);
        x = direction?.X ?? Fixed.Zero;
        y = direction?.Y ?? Fixed.Zero;
        return direction is not null;
    }

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

    /// <summary>Sets the veterancy level (0 regular ... 3 heroic); <c>ExperienceTracker::setVeterancyLevel</c>: the experience becomes the level's requirement and the level-change effects run.</summary>
    public void SetVeterancy(EntityId id, int level)
    {
        if (level is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(level));
        var entity = FindEntity(id) ?? throw new ArgumentException($"Unknown entity {id}.", nameof(id));
        SetVeterancyLevel(entity, level);
    }

    /// <summary><c>ExperienceTracker::setMinVeterancyLevel</c>: raises the level to at least <paramref name="level"/> (works on an entity that is not registered yet).</summary>
    public void SetMinVeterancyLevel(Entity entity, int level)
    {
        if (level is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(level));
        if (entity.VeterancyLevel < level) SetVeterancyLevel(entity, level);
    }

    private void SetVeterancyLevel(Entity entity, int level)
    {
        if (entity.VeterancyLevel == level) return;
        entity.Experience = entity.Template.ExperienceRequired[level];
        OnVeterancyLevelChanged(entity, level);
    }

    /// <summary><c>ExperienceTracker::addExperiencePoints</c>: a trainable object gains <c>(int)(amount * scalar)</c> and takes the highest level whose requirement it meets.</summary>
    public void AddExperiencePoints(Entity entity, int amount) => AddExperience(entity, amount);

    private void AddExperience(Entity entity, int amount)
    {
        if (!entity.Template.IsTrainable) return;
        entity.Experience = checked(entity.Experience + (int)(Fixed.FromInt(amount) * entity.ExperienceScalar).Floor());
        var level = 0;
        while (level + 1 < 4 && entity.Experience >= entity.Template.ExperienceRequired[level + 1]) level++;
        MarkDirty(entity);
        if (level != entity.VeterancyLevel) OnVeterancyLevelChanged(entity, level);
    }

    /// <summary>
    /// <c>Object::onVeterancyLevelChanged</c>: the body's max health is scaled by the ratio of the GameData health bonuses (health keeps its
    /// ratio), the VETERAN/ELITE/HERO weapon bonus condition follows the level, and the engine-made <c>Upgrade_Veterancy_*</c> of the new
    /// level is granted (none for level 0). <see cref="ActiveWeaponSet"/>/<see cref="ActiveArmor"/> also switch to the level's
    /// <c>WeaponSet</c>/<c>ArmorSet</c> when the template declares one (see <see cref="Templates.ObjectTemplate.VeteranArmor"/>), as they read
    /// <see cref="World.Entity.VeterancyLevel"/> directly; an object with several weapons builds the new set's weapons fresh (<see cref="UpdateWeaponSet"/>).
    /// </summary>
    private void OnVeterancyLevelChanged(Entity entity, int newLevel)
    {
        var oldLevel = entity.VeterancyLevel;
        entity.VeterancyLevel = newLevel;
        var bonus = _templates.HealthBonus;
        if (entity.Body is IMaxHealthAdjustable adjustable && bonus[oldLevel] > Fixed.Zero && bonus[newLevel] != bonus[oldLevel])
        {
            var maxHealth = entity.Body.MaxHealth;
            adjustable.AdjustMaxHealth(this, maxHealth * bonus[newLevel] / bonus[oldLevel] - maxHealth, "PRESERVE_RATIO");
        }
        var conditions = entity.WeaponBonusConditions & ~WeaponBonusTable.VeterancyConditions;
        if (newLevel > 0) conditions |= 1u << (WeaponBonusTable.VeteranCondition + newLevel - 1);
        entity.WeaponBonusConditions = conditions;
        UpdateWeaponSet(entity);
        MarkDirty(entity);
        if (newLevel > 0 && _templates.Upgrades.VeterancyUpgrade(newLevel) is var upgrade && !entity.CompletedUpgrades.Has(upgrade.Index))
            CompleteObjectUpgrade(entity, upgrade, entity.Owner);
    }

    // ------------------------------------------------------------------------------------- phase 5: economy, upgrades

    public UpgradeCatalog Upgrades => _templates.Upgrades;
    public WeaponCatalog Weapons => _templates.Weapons;
    public ScienceCatalog Sciences => _templates.Sciences;
    public CrateCatalog Crates => _templates.Crates;
    public ObjectCreationListCatalog CreationLists => _templates.CreationLists;
    public Fixed UnitDamagedThreshold => _templates.UnitDamagedThreshold;
    public Fixed UnitReallyDamagedThreshold => _templates.UnitReallyDamagedThreshold;
    public Fixed BaseRegenHealthPercentPerSecond => _templates.BaseRegenHealthPercentPerSecond;
    public long BaseRegenDelayTicks => _templates.BaseRegenDelayTicks;
    public int ValuePerSupplyBox => _templates.ValuePerSupplyBox;

    /// <summary>
    /// <c>Player::getRelationship</c> from <paramref name="from"/>'s point of view: a player is its own ally, the system authority (neutral
    /// objects) is neutral to everybody, an explicit directed override wins, otherwise the team decides. The reference defaults to NEUTRAL
    /// and relies on the skirmish setup to write every relation; this world has no such setup step, so "no team, no relation" means enemies.
    /// </summary>
    public Relationship RelationshipOf(PlayerId from, PlayerId to)
    {
        if (from == to) return Relationship.Allies;
        if (from.IsSystem || to.IsSystem) return Relationship.Neutral;
        if (!_players.TryGetValue(from.Value, out var state) || !_players.ContainsKey(to.Value)) return Relationship.Neutral;
        if (state.Relations.TryGetValue(to.Value, out var explicitRelation)) return explicitRelation;
        return state.Team != 0 && _players[to.Value].Team == state.Team ? Relationship.Allies : Relationship.Enemies;
    }

    public bool TryEnterContainer(Entity actor, Entity container)
    {
        if (actor.DestroyRequested || actor.ContainedBy.IsValid || container.DestroyRequested || container.Container is null) return false;
        if (!container.Container.IsValidContainerFor(this, actor, true)) return false;
        PutIntoContainer(actor, container, actor.Owner);
        return true;
    }

    private void PutIntoContainer(Entity actor, Entity container, PlayerId player)
    {
        container.Container!.Add(this, actor);
        actor.ContainedBy = container.Id; // Add() also does this; kept here too so every entry path is consistent.
        MarkDirty(actor);
        Emit(SimEventKind.EntityContained, actor.Id, container.Id, string.Empty, 0, player);
    }

    public bool IsComputerPlayer(PlayerId player) => _players.TryGetValue(player.Value, out var state) && state.IsComputer;

    public bool AreEnemies(PlayerId a, PlayerId b) => a != b && RelationshipOf(a, b) == Relationship.Enemies;

    /// <summary>A per-object relationship override the mission script engine installs while a script has set team or player overrides (<c>Team::getRelationship</c>); <see langword="null"/> means none.</summary>
    public Func<Entity, Entity, Relationship?>? RelationshipOverride { get; set; }

    /// <summary>The mission script engine's <c>TECHTREE_MODIFY_BUILDABILITY_OBJECT</c> table (<c>GameLogic::setBuildableStatusOverride</c>): the <c>Buildable</c> status a script gave a template, or <see langword="null"/> for the template's own.</summary>
    public Func<ObjectTemplate, string?>? BuildableOverride { get; set; }

    /// <summary>The effective <c>Buildable</c> status of a template (<c>ThingTemplate::getBuildable</c> after a script override).</summary>
    public string BuildableOf(ObjectTemplate template) => BuildableOverride?.Invoke(template) ?? template.Buildable;

    /// <summary>The relationship of one object to another: a script team override if there is one, else the player relationship of the owners.</summary>
    public Relationship RelationshipBetween(Entity from, Entity to) => RelationshipOverride?.Invoke(from, to) ?? RelationshipOf(from.Owner, to.Owner);

    public bool AreEnemies(Entity a, Entity b) => a != b && RelationshipBetween(a, b) == Relationship.Enemies;

    /// <summary><c>VictoryConditions::areAllies</c>: two distinct players that are allies of each other, in both directions.</summary>
    public bool AreAllies(PlayerId a, PlayerId b) => a != b && RelationshipOf(a, b) == Relationship.Allies && RelationshipOf(b, a) == Relationship.Allies;

    public ScienceMask PlayerSciences(PlayerId player) => _players.TryGetValue(player.Value, out var state) ? state.CompletedSciences : ScienceMask.Empty;
    public int SciencePointsOf(PlayerId player) => _players.TryGetValue(player.Value, out var state) ? state.SciencePurchasePoints : 0;

    public void AddSciencePoints(PlayerId player, int amount)
    {
        if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
        if (_players.TryGetValue(player.Value, out var state)) state.SciencePurchasePoints += amount;
    }

    public SpecialPowerCatalog SpecialPowers => _templates.SpecialPowers;

    public bool TryGetSharedReadyFrame(PlayerId player, int powerIndex, out long readyFrame)
    {
        readyFrame = 0;
        return _players.TryGetValue(player.Value, out var state) && state.SharedReadyFrames.TryGetValue(powerIndex, out readyFrame);
    }

    public void SetSharedReadyFrame(PlayerId player, int powerIndex, long readyFrame)
    {
        if (_players.TryGetValue(player.Value, out var state)) state.SharedReadyFrames[powerIndex] = readyFrame;
    }

    public Fixed CashBountyOf(PlayerId player) => _players.TryGetValue(player.Value, out var state) ? state.CashBounty : Fixed.Zero;

    private readonly TunnelTracker _orphanTunnels = new();

    public TunnelTracker TunnelSystemOf(PlayerId player) => _players.TryGetValue(player.Value, out var state) ? state.Tunnels : _orphanTunnels;

    public void RaiseCashBounty(PlayerId player, Fixed ratio)
    {
        if (_players.TryGetValue(player.Value, out var state) && ratio > state.CashBounty) state.CashBounty = ratio;
    }

    public bool GrantScience(PlayerId player, string scienceName)
    {
        if (!_players.TryGetValue(player.Value, out var state)) return false;
        if (!_templates.Sciences.TryGet(scienceName, out var science)) throw new InvalidOperationException($"GrantScience: '{scienceName}' is not a Science definition.");
        // Player::grantScience: a non-grantable science is refused (the reference asserts and returns false).
        return science.IsGrantable && AddScience(state, science);
    }

    /// <summary><c>Player::addScience</c>: records the science, then wakes every special power of the player's objects that requires it (<c>onSpecialPowerCreation</c> + instantly ready).</summary>
    private bool AddScience(PlayerState player, Templates.ScienceInfo science)
    {
        if (player.CompletedSciences.Has(science.Index)) return false;
        player.CompletedSciences = player.CompletedSciences.Set(science.Index);
        foreach (var entity in _entities.Values.OrderBy(value => value.Id.Value))
        {
            if (entity.Owner != player.Id) continue;
            foreach (var module in entity.Modules)
            {
                if (module is not ISpecialPowerModule power || _templates.SpecialPowers[power.PowerIndex].RequiredScience != science.Index) continue;
                MarkDirty(entity);
                power.OnSpecialPowerCreation(this);
                power.SetReadyFrame(_tick);
            }
        }
        return true;
    }

    public int EnergyProductionOf(PlayerId player) => _players.TryGetValue(player.Value, out var state) ? state.EnergyProduction : 0;

    public Fixed MoneyOf(PlayerId player) => _players.TryGetValue(player.Value, out var state) ? state.Money : Fixed.Zero;

    public void AddMoney(PlayerId player, Fixed amount)
    {
        if (amount.IsNegative) throw new ArgumentOutOfRangeException(nameof(amount));
        if (_players.TryGetValue(player.Value, out var state)) state.Money += amount;
    }

    public void AddEnergyProduction(PlayerId player, int amount)
    {
        if (_players.TryGetValue(player.Value, out var state)) state.EnergyProduction += amount;
    }

    /// <summary>
    /// Public dock marker (`docs/PRESENTATION-INPUT.md` §10 point 3): whether <paramref name="id"/> has a dock module at all, replacing the
    /// module-name string match (<c>SupplyCenterDockUpdate</c>, <c>SupplyWarehouseDockUpdate</c>, <c>RepairDockUpdate</c>, ...) presentation used
    /// from outside the assembly, where <c>IDockModule</c> itself is not visible.
    /// </summary>
    public bool IsDock(EntityId id) => FindEntity(id) is { } entity && Modules.BuiltIn.DockModules.DockOf(entity) is not null;

    /// <summary>
    /// What the dock side of a docking shows (<c>DockUpdate::onEnterReached</c> / <c>onDockReached</c> / <c>onExitReached</c> set the same model conditions on the dock and on the docker):
    /// <paramref name="dockerInside"/> while the active docker is between the entry and the exit (<c>DOCKING</c>), <paramref name="dockActive"/> while that docker is at the dock point and the dock works
    /// on it (<c>DOCKING_ACTIVE</c>). False for both when <paramref name="dock"/> is no dock.
    /// </summary>
    public void GetDockActivity(Entity dock, out bool dockerInside, out bool dockActive)
    {
        ArgumentNullException.ThrowIfNull(dock);
        dockerInside = false;
        dockActive = false;
        if (Modules.BuiltIn.DockModules.DockOf(dock) is not Modules.BuiltIn.DockUpdateBase { DockerInside: true } module) return;
        dockerInside = true;
        dockActive = FindEntity(module.ActiveDocker)?.Modules.OfType<Modules.BuiltIn.AIUpdateInterface>().FirstOrDefault() is { IsDockActive: true, DockingAt: var at } && at == dock.Id;
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
            CompleteObjectUpgrade(target, info, player);
        }
    }

    private void CompleteObjectUpgrade(Entity target, UpgradeInfo info, PlayerId player)
    {
        target.CompletedUpgrades = target.CompletedUpgrades.Set(info.Index);
        Emit(SimEventKind.UpgradeCompleted, target.Id, EntityId.Invalid, info.Name, info.Index, player);
        ReevaluateUpgrades(target);
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

    public EntityId RequestSpawn(int templateIndex, PlayerId owner, Fixed x, Fixed y, Fixed z, Fixed angle, in SpawnExtras extras)
    {
        if (templateIndex < 0 || templateIndex >= _templates.Templates.Length) throw new ArgumentOutOfRangeException(nameof(templateIndex));
        var id = Ids.AllocateEntity();
        var setup = new SpawnSetup(false, 0, false, 0, 0, 0, EntityId.Invalid, EntityId.Invalid, Fixed.Zero, EntityId.Invalid,
            extras.MaxHealth, extras.OriginTemplate >= 0, Math.Max(extras.OriginTemplate, 0), extras.ForceSupplyWanting, extras.HasRally, extras.RallyX, extras.RallyY, Enslaver: extras.Enslaver, Producer: extras.Producer);
        _spawnQueue.Add(new PendingSpawn(id, templateIndex, owner, x, y, z, angle, setup));
        return id;
    }

    /// <summary>Armor counterpart of <see cref="ActiveWeaponSet"/>; see <see cref="Templates.ObjectTemplate.VeteranArmor"/>.</summary>
    private static ArmorInfo ActiveArmor(Entity entity)
    {
        // ARMORSET_CRATE_UPGRADE_ONE/TWO (SalvageCrateCollide).
        if (entity.CrateArmorLevel > 0 && entity.Template.CrateUpgradeArmor[entity.CrateArmorLevel] is { } crate) return crate;
        if (entity.VeterancyLevel > 0 && entity.Template.VeteranArmor[entity.VeterancyLevel] is { } veteran) return veteran;
        return entity.HasPlayerUpgradeArmor && entity.Template.PlayerUpgradeArmor is { } upgraded ? upgraded : entity.Template.DefaultArmor;
    }

    private static Modules.BuiltIn.ProductionUpdate? FindProductionModule(Entity entity)
        => Array.Find(entity.ModuleArray, module => module is Modules.BuiltIn.ProductionUpdate) as Modules.BuiltIn.ProductionUpdate;

    private static bool CanMove(Entity entity) => AiOf(entity) is not null;

    private static Modules.BuiltIn.AIUpdateInterface? AiOf(Entity entity)
        => Array.Find(entity.ModuleArray, module => module is Modules.BuiltIn.AIUpdateInterface) as Modules.BuiltIn.AIUpdateInterface;

    /// <summary>
    /// <c>VictoryConditions::hasSinglePlayerBeenDefeated</c> (default flags NOBUILDINGS|NOUNITS) = <c>!Player::hasAnyObjects</c>:
    /// <c>Team::hasAnyObjects</c> skips effectively dead or destroyed objects, projectiles, inert objects and mines.
    /// </summary>
    public bool IsPlayerDefeated(PlayerId player) => !_entities.Values.Any(entity => entity.Owner == player && CountsForSurvival(entity));

    private static bool CountsForSurvival(Entity entity)
        => !entity.EffectivelyDead && !entity.DestroyRequested
           && !entity.Template.IsKindOf("PROJECTILE") && !entity.Template.IsKindOf("INERT") && !entity.Template.IsKindOf("MINE");

    /// <summary>
    /// <c>VictoryConditions::update</c> for a skirmish without a "last alliance" opt-out: the match is decided once every player that still has
    /// objects is an ally of the first one (the reference compares each survivor against the first survivor only, so a non-transitive
    /// relation matrix is judged the same way). Returns those survivors in ascending id order; empty while enemies remain or nobody is left.
    /// </summary>
    public ImmutableArray<PlayerId> TryGetWinningAlliance()
    {
        var survivors = ImmutableArray.CreateBuilder<PlayerId>();
        foreach (var player in _playerList)
        {
            if (player.Id.IsSystem || IsPlayerDefeated(player.Id)) continue;
            if (survivors.Count > 0 && !AreAllies(survivors[0], player.Id)) return [];
            survivors.Add(player.Id);
        }
        return survivors.ToImmutable();
    }

    /// <summary>The lowest-id member of the winning alliance (the sole survivor without teams), or <see langword="null"/> while the match is undecided.</summary>
    public PlayerId? TryGetWinner()
    {
        var alliance = TryGetWinningAlliance();
        return alliance.IsEmpty ? null : alliance[0];
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
                if (info.IsEngine) return CommandRejection.NotPermitted;
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
                if (production is null || !CanProduceNow(producer)) return CommandRejection.CannotProduce;
                if (!production.HasRoom) return CommandRejection.QueueFull;
                if (!CanProduceTemplate(player, producer, queueTemplate) || !player.CanBuildUnits) return CommandRejection.CannotProduceTemplate;
                if (!BuildableOf(queueTemplate).Equals("Ignore_Prerequisites", StringComparison.OrdinalIgnoreCase) && !PrerequisitesSatisfied(player, queueTemplate))
                    return CommandRejection.MissingPrerequisite;
                if (ExceedsMaxSimultaneousOfType(player, queueTemplate)) return CommandRejection.ProductionLimitReached;
                if (player.Money < ProductionCostOf(player, queueTemplate)) return CommandRejection.InsufficientFunds;
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
            case AttackMoveCommand attackMove:
            {
                if (!_entities.TryGetValue(attackMove.Unit.Value, out var marcher) || marcher.DestroyRequested) return CommandRejection.UnknownEntity;
                if (marcher.Owner != command.Player) return CommandRejection.NotPermitted;
                if (!CanMove(marcher)) return CommandRejection.CannotMove;
                break;
            }
            case PursueCommand pursue:
            {
                if (!_entities.TryGetValue(pursue.Unit.Value, out var pursuer) || pursuer.DestroyRequested) return CommandRejection.UnknownEntity;
                if (pursuer.Owner != command.Player) return CommandRejection.NotPermitted;
                if (!_entities.TryGetValue(pursue.Victim.Value, out var pursued) || pursued.DestroyRequested || pursued.Body is null) return CommandRejection.UnknownEntity;
                if (!CanMove(pursuer)) return CommandRejection.CannotMove;
                if (!CanPlayerSee(command.Player, pursued)) return CommandRejection.TargetNotVisible;
                break;
            }
            case FireWeaponCommand fire:
            {
                if (!_entities.TryGetValue(fire.Source.Value, out var source) || source.DestroyRequested) return CommandRejection.UnknownEntity;
                if (source.Owner != command.Player) return CommandRejection.NotPermitted;
                if (!_entities.TryGetValue(fire.Target.Value, out var fireTarget) || fireTarget.DestroyRequested || fireTarget.Body is null) return CommandRejection.UnknownEntity;
                if (fire.Slot is < WeaponSlot.Best or >= WeaponSlot.Count || !Enum.IsDefined(fire.Origin)) return CommandRejection.InvalidArguments;
                var choice = ChooseWeapon(source, fireTarget, fire.Origin, fire.Slot);
                if (choice.Rejection != 0) return choice.Rejection;
                if (!CanPlayerSee(command.Player, fireTarget)) return CommandRejection.TargetNotVisible;
                break;
            }
            case SetWeaponLockCommand lockOrder:
            {
                if (!_entities.TryGetValue(lockOrder.Source.Value, out var lockSource) || lockSource.DestroyRequested) return CommandRejection.UnknownEntity;
                if (lockSource.Owner != command.Player && !command.Player.IsSystem) return CommandRejection.NotPermitted;
                if (!Enum.IsDefined(lockOrder.Lock)) return CommandRejection.InvalidArguments;
                if (lockOrder.Lock == WeaponLockKind.None) break;
                if (lockOrder.Slot is < 0 or >= WeaponSlot.Count) return CommandRejection.InvalidArguments;
                if (ActiveWeaponSet(lockSource) is not { } lockSet || !lockSet.HasWeapon(lockOrder.Slot)) return CommandRejection.NoWeapon;
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
                if (!container.Container.IsValidContainerFor(this, actor, true)) return CommandRejection.CannotEnterContainer;
                break;
            }
            case ExitContainerCommand exit:
            {
                if (!_entities.TryGetValue(exit.Actor.Value, out var actor) || actor.DestroyRequested) return CommandRejection.UnknownEntity;
                if (!actor.ContainedBy.IsValid) return CommandRejection.NotContained;
                var byContainer = _entities.GetValueOrDefault(actor.ContainedBy.Value);
                if (!command.Player.IsSystem && actor.Owner != command.Player && (byContainer is null || byContainer.Owner != command.Player))
                    return CommandRejection.NotPermitted;
                if (byContainer?.Container is { } door && !door.IsRiderFreeToExit(actor)) return CommandRejection.NotPermitted;
                break;
            }
            case SetRelationshipCommand relation:
                if (relation.From.IsSystem || relation.To.IsSystem || relation.From == relation.To) return CommandRejection.InvalidArguments;
                if (!_players.ContainsKey(relation.From.Value) || !_players.ContainsKey(relation.To.Value)) return CommandRejection.UnknownPlayer;
                if (!Enum.IsDefined(relation.Relationship)) return CommandRejection.InvalidArguments;
                break;
            case DockCommand dock:
            {
                if (!_entities.TryGetValue(dock.Actor.Value, out var actor) || actor.DestroyRequested) return CommandRejection.UnknownEntity;
                if (actor.Owner != command.Player) return CommandRejection.NotPermitted;
                if (!CanMove(actor)) return CommandRejection.CannotMove;
                if (!_entities.TryGetValue(dock.Dock.Value, out var target) || target.DestroyRequested) return CommandRejection.UnknownEntity;
                if (Modules.BuiltIn.DockModules.DockOf(target) is null) return CommandRejection.CannotDock;
                break;
            }
            case UseSpecialPowerCommand useSpecialPower:
                return CheckSpecialPower(player, command.Player, useSpecialPower, out _, out _, out _);
            case ConstructCommand construct:
                return CheckConstruct(command.Player, construct, out _, out _);
            case SellCommand sell:
                return CheckSell(command.Player, sell, out _);
            case SetRallyPointCommand rally:
                return CheckRally(command.Player, rally, out _, out _);
            case ResumeConstructCommand resume:
                return CheckResume(command.Player, resume, out _, out _);
            case CancelConstructCommand cancelConstruct:
                return CheckCancelConstruct(command.Player, cancelConstruct, out _);
            case RepairCommand repair:
                return CheckRepair(command.Player, repair, out _, out _);
            case MoveAndEnterCommand moveAndEnter:
                return CheckMoveAndEnter(command.Player, moveAndEnter, out _, out _);
            case EvacuateCommand evacuate:
                return CheckEvacuate(command.Player, evacuate, out _);
            case OverchargeCommand overcharge:
                return CheckOvercharge(command.Player, overcharge, out _);
            case CaptureCommand capture:
                return CheckCapture(command.Player, capture, out _, out _);
        }
        return 0;
    }

    /// <summary><c>ActionManager::canEnterObject</c>, reduced to what exists here: an own mobile object that is not inside anything, a container whose entry rules accept it (capacity is checked on arrival).</summary>
    private CommandRejection CheckMoveAndEnter(PlayerId issuer, MoveAndEnterCommand order, out Entity? actor, out Entity? container)
    {
        container = null;
        actor = FindEntity(order.Actor);
        if (actor is null || actor.DestroyRequested) return CommandRejection.UnknownEntity;
        if (actor.Owner != issuer) return CommandRejection.NotPermitted;
        if (!CanMove(actor) || actor.Template.Locomotor is null) return CommandRejection.CannotMove;
        if (actor.ContainedBy.IsValid) return CommandRejection.CannotEnterContainer;
        container = FindEntity(order.Container);
        if (container is null || container.DestroyRequested || container.Container is null || container == actor) return CommandRejection.UnknownEntity;
        return container.Container.IsValidContainerFor(this, actor, false) ? 0 : CommandRejection.CannotEnterContainer;
    }

    /// <summary>The container's owner evacuates it; a position needs a container that can move there.</summary>
    private CommandRejection CheckEvacuate(PlayerId issuer, EvacuateCommand order, out Entity? container)
    {
        container = FindEntity(order.Container);
        if (container is null || container.DestroyRequested || container.Container is null) return CommandRejection.UnknownEntity;
        if (!issuer.IsSystem && container.Owner != issuer) return CommandRejection.NotPermitted;
        if (order.HasPosition && (AiOf(container) is null || container.Template.Locomotor is null)) return CommandRejection.CannotMove;
        return 0;
    }

    /// <summary><c>ActionManager::canDoSpecialPower</c>: shared by submission and execution, because readiness changes between the two.</summary>
    private CommandRejection CheckSpecialPower(PlayerState player, PlayerId issuer, UseSpecialPowerCommand use, out Entity? source, out ISpecialPowerModule? module, out SpecialPowerInfo? info)
    {
        module = null;
        info = null;
        source = FindEntity(use.Source);
        if (source is null || source.DestroyRequested) return CommandRejection.UnknownEntity;
        if (!issuer.IsSystem && source.Owner != issuer) return CommandRejection.NotPermitted;
        if (!_templates.SpecialPowers.TryGet(use.SpecialPower, out var found)) return CommandRejection.UnknownSpecialPower;
        info = found;
        module = SpecialPowerRouting.FindModule(source, found.Index);
        if (module is null) return CommandRejection.UnknownSpecialPower;
        if (!Enum.IsDefined(use.TargetKind) || !module.AcceptsTarget(use.TargetKind)) return CommandRejection.InvalidSpecialPowerTarget;
        if (use.TargetKind == SpecialPowerTargetKind.Object && FindEntity(use.Target) is not { DestroyRequested: false }) return CommandRejection.InvalidSpecialPowerTarget;
        var owner = _players.GetValueOrDefault(source.Owner.Value) ?? player;
        if (found.RequiredScience >= 0 && !owner.CompletedSciences.Has(found.RequiredScience)) return CommandRejection.MissingScience;
        if (source.HasStatus(ObjectStatusBits.UnderConstruction) || !module.IsReady(this)) return CommandRejection.SpecialPowerNotReady;
        if (module.StartsAttack && SpecialPowerRouting.FindHandler(source, this, found.Index) is null) return CommandRejection.SpecialPowerHandlerMissing;
        if (SpecialPowerRouting.FindHandler(source, this, found.Index) is { } handler && !handler.ValidatesTarget(this, found.Index, use.AsTarget)) return CommandRejection.InvalidSpecialPowerTarget;
        return 0;
    }

    /// <summary><c>Player::isCapableOfPurchasingScience</c>.</summary>
    /// <summary><c>Player::isCapableOfPurchasingScience</c>: not owned yet, prerequisites met, purchasable and enough purchase points.</summary>
    public bool CanPurchaseScienceNow(PlayerId player, string scienceName)
        => _players.TryGetValue(player.Value, out var state) && _templates.Sciences.TryGet(scienceName, out var science) && CanPurchaseScience(state, science, out _);

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
                if (production is null || !CanProduceNow(producer)) { RecordRejection(command, CommandRejection.CannotProduce); break; }
                if (!production.HasRoom) { RecordRejection(command, CommandRejection.QueueFull); break; }
                var buyer = _players[producer.Owner.Value];
                if (!CanProduceTemplate(buyer, producer, queueTemplate)) { RecordRejection(command, CommandRejection.CannotProduceTemplate); break; }
                if (!BuildableOf(queueTemplate).Equals("Ignore_Prerequisites", StringComparison.OrdinalIgnoreCase) && !PrerequisitesSatisfied(buyer, queueTemplate))
                    { RecordRejection(command, CommandRejection.MissingPrerequisite); break; }
                if (ExceedsMaxSimultaneousOfType(buyer, queueTemplate)) { RecordRejection(command, CommandRejection.ProductionLimitReached); break; }
                var queueCost = ProductionCostOf(buyer, queueTemplate);
                if (buyer.Money < queueCost) { RecordRejection(command, CommandRejection.InsufficientFunds); break; }
                // ProductionUpdate.h: the full cost is withdrawn immediately at queue time, refunded in full on cancel.
                buyer.Money -= queueCost;
                production.Enqueue(queueTemplate.Index, queueCost, queueTemplate.BuildTicks);
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
                // GameLogicDispatch doMoveTo: a move order releases any temporary weapon lock.
                ReleaseWeaponLock(target, WeaponLockKind.Temporary);
                AiOf(target)!.OrderMove(this, move.X, move.Y, true);
                break;
            }
            case SetRelationshipCommand relationOrder:
                _players[relationOrder.From.Value].Relations[relationOrder.To.Value] = relationOrder.Relationship;
                break;
            case DockCommand dockOrder:
            {
                var actor = FindEntity(dockOrder.Actor);
                if (actor is null || actor.DestroyRequested) { RecordRejection(command, CommandRejection.UnknownEntity); break; }
                if (actor.Owner != command.Player) { RecordRejection(command, CommandRejection.NotPermitted); break; }
                if (!CanMove(actor)) { RecordRejection(command, CommandRejection.CannotMove); break; }
                var dockEntity = FindEntity(dockOrder.Dock);
                if (dockEntity is null || dockEntity.DestroyRequested) { RecordRejection(command, CommandRejection.UnknownEntity); break; }
                if (Modules.BuiltIn.DockModules.DockOf(dockEntity) is null) { RecordRejection(command, CommandRejection.CannotDock); break; }
                AiOf(actor)!.OrderDock(this, dockEntity, true);
                break;
            }
            case AttackMoveCommand attackMove:
            {
                var marcher = FindEntity(attackMove.Unit);
                if (marcher is null || marcher.DestroyRequested) { RecordRejection(command, CommandRejection.UnknownEntity); break; }
                if (marcher.Owner != command.Player) { RecordRejection(command, CommandRejection.NotPermitted); break; }
                if (!CanMove(marcher)) { RecordRejection(command, CommandRejection.CannotMove); break; }
                ReleaseWeaponLock(marcher, WeaponLockKind.Temporary);
                AiOf(marcher)!.OrderMove(this, attackMove.X, attackMove.Y, true, attackMove: true);
                break;
            }
            case PursueCommand pursue:
            {
                var unit = FindEntity(pursue.Unit);
                if (unit is null || unit.DestroyRequested) { RecordRejection(command, CommandRejection.UnknownEntity); break; }
                if (unit.Owner != command.Player) { RecordRejection(command, CommandRejection.NotPermitted); break; }
                var victim = FindEntity(pursue.Victim);
                if (victim is null || victim.DestroyRequested || victim.Body is null) { RecordRejection(command, CommandRejection.UnknownEntity); break; }
                if (!CanMove(unit)) { RecordRejection(command, CommandRejection.CannotMove); break; }
                ReleaseWeaponLock(unit, WeaponLockKind.Temporary);
                // The order replaces the unit's attack like any new AI command; the pursuit then starts the attack afresh against the victim.
                AiOf(unit)!.OrderMove(this, pursue.X, pursue.Y, true);
                BeginPursuit(unit, victim);
                NoteOrderedAttack(unit, victim);
                break;
            }
            case FireWeaponCommand fire:
            {
                var source = FindEntity(fire.Source);
                if (source is null || source.DestroyRequested) { RecordRejection(command, CommandRejection.UnknownEntity); break; }
                if (source.Owner != command.Player) { RecordRejection(command, CommandRejection.NotPermitted); break; }
                var fireTarget = FindEntity(fire.Target);
                if (fireTarget is null || fireTarget.DestroyRequested || fireTarget.Body is null) { RecordRejection(command, CommandRejection.UnknownEntity); break; }
                UpdateWeaponSet(source);
                var choice = ChooseWeapon(source, fireTarget, fire.Origin, fire.Slot);
                if (choice.Rejection != 0) { RecordRejection(command, choice.Rejection); break; }
                if (!GateAllowsFire(source, fireTarget)) { RecordRejection(command, CommandRejection.WeaponNotReady); break; }
                // An attack order of the unit's command giver: the unit does not pick victims by itself while such orders keep coming. A script's one-off button is no attack.
                if (fire.Origin != FireOrigin.Script) NoteOrderedAttack(source, fireTarget);
                // The turret of the slot turns to the victim first and the weapon waits until it points there (TurretAI). The order stays accepted — the attack goes on and the next
                // order of the stream fires — so this is neither a rejection (no "cannot do" cue) nor a shot. A script's one-off command button fires its single shot regardless.
                if (!AimTurretsAt(source, choice.Slot, fireTarget, fire.Origin != FireOrigin.Script ? TurretAimOrder.Attack : TurretAimOrder.Shot) && fire.Origin != FireOrigin.Script) { _turretWaits++; break; }
                // The weapon winds up first (PreAttackDelay); like the turret wait this is neither a rejection nor a shot. A script's one-off command fires at once.
                if (fire.Origin != FireOrigin.Script && !PreAttackDone(source, _templates.Weapons[ActiveWeaponSet(source)!.Weapons[choice.Slot]], choice.Slot, fireTarget)) break;
                // A command button's weapon slot pins the slot until its clip empties (Object::doCommandButton: setWeaponLock(LOCKED_TEMPORARILY) before the attack).
                if (fire.Slot >= 0) SetWeaponLock(source, fire.Slot, WeaponLockKind.Temporary);
                var weapon = ConsumeShot(source, choice.Slot, fireTarget.Id);
                var damage = FireWeaponAt(source, weapon, fireTarget, fireTarget.X, fireTarget.Y, fireTarget.Z);
                NoteTurretShot(source, choice.Slot);
                Emit(SimEventKind.WeaponFired, source.Id, fireTarget.Id, weapon.Name, damage.Raw, command.Player);
                break;
            }
            case SetWeaponLockCommand lockOrder:
            {
                var lockSource = FindEntity(lockOrder.Source);
                if (lockSource is null || lockSource.DestroyRequested) { RecordRejection(command, CommandRejection.UnknownEntity); break; }
                if (lockSource.Owner != command.Player && !command.Player.IsSystem) { RecordRejection(command, CommandRejection.NotPermitted); break; }
                if (lockOrder.Lock == WeaponLockKind.None) { ReleaseWeaponLock(lockSource, WeaponLockKind.Permanent); break; }
                if (!SetWeaponLock(lockSource, lockOrder.Slot, lockOrder.Lock)) RecordRejection(command, CommandRejection.NoWeapon);
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
                AddScience(buyer, science);
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
                if (!container.Container.IsValidContainerFor(this, actor, true)) { RecordRejection(command, CommandRejection.CannotEnterContainer); break; }
                PutIntoContainer(actor, container, command.Player);
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
                if (byContainer?.Container is { } door)
                {
                    if (!door.IsRiderFreeToExit(actor)) { RecordRejection(command, CommandRejection.NotPermitted); break; }
                    if (door.ExitViaDoor(this, actor)) break; // the container runs its own exit sequence (the ferry's dock)
                }
                byContainer?.Container?.Remove(this, actor, true);
                actor.ContainedBy = EntityId.Invalid; // Safety net: Remove() also does this; guards a dangling/missing container reference.
                MarkDirty(actor);
                Emit(SimEventKind.EntityUncontained, actor.Id, previousContainer, string.Empty, 0, command.Player);
                break;
            }
            case UseSpecialPowerCommand use:
            {
                var reason = CheckSpecialPower(_players[command.Player.Value], command.Player, use, out var source, out var module, out var info);
                if (reason != 0) { RecordRejection(command, reason); break; }
                MarkDirty(source!);
                Emit(SimEventKind.SpecialPowerUsed, source!.Id, use.TargetKind == SpecialPowerTargetKind.Object ? use.Target : EntityId.Invalid, info!.Name, info.Index, command.Player);
                // SpecialAbilityUpdate::initiateIntentToDoSpecialPower ("Clear any old AI before starting this special ability"): a unit ability idles its unit, which ends a move and
                // the unit's attack. The staged capture does so itself when it begins.
                foreach (var candidate in source.ModuleArray)
                    if (candidate is Modules.BuiltIn.SpecialAbilityUpdate ability && ability.PowerIndex == info.Index && !ability.IsCapture(this)) { AiOf(source)?.OrderIdle(this, false); break; }
                module!.Use(this, use.AsTarget);
                break;
            }
            case ConstructCommand construct:
            {
                var reason = CheckConstruct(command.Player, construct, out var builder, out var template);
                if (reason != 0) { RecordRejection(command, reason); break; }
                StartConstruction(command.Player, builder!, template!, construct);
                break;
            }
            case SellCommand sell:
            {
                var reason = CheckSell(command.Player, sell, out var target);
                if (reason != 0) { RecordRejection(command, reason); break; }
                BeginSale(target!);
                break;
            }
            case SetRallyPointCommand rally:
            {
                var reason = CheckRally(command.Player, rally, out var producer, out var production);
                if (reason != 0) { RecordRejection(command, reason); break; }
                production!.SetRally(rally.X, rally.Y);
                MarkDirty(producer!);
                break;
            }
            case ResumeConstructCommand resume:
            {
                var reason = CheckResume(command.Player, resume, out var builder, out var site);
                if (reason != 0) { RecordRejection(command, reason); break; }
                site!.BuilderId = builder!.Id;
                MarkDirty(site);
                BuilderOf(builder)!.OrderConstruct(this, site);
                break;
            }
            case CancelConstructCommand cancelConstruct:
            {
                var reason = CheckCancelConstruct(command.Player, cancelConstruct, out var site);
                if (reason != 0) { RecordRejection(command, reason); break; }
                CancelConstruction(site!);
                break;
            }
            case RepairCommand repair:
            {
                var reason = CheckRepair(command.Player, repair, out var builder, out var target);
                if (reason != 0) { RecordRejection(command, reason); break; }
                AiOf(builder!)!.OrderRepair(this, target!);
                break;
            }
            case MoveAndEnterCommand moveAndEnter:
            {
                var reason = CheckMoveAndEnter(command.Player, moveAndEnter, out var actor, out var container);
                if (reason != 0) { RecordRejection(command, reason); break; }
                AiOf(actor!)!.OrderEnter(this, container!, true);
                break;
            }
            case EvacuateCommand evacuate:
            {
                var reason = CheckEvacuate(command.Player, evacuate, out var container);
                if (reason != 0) { RecordRejection(command, reason); break; }
                if (AiOf(container!) is { } carrier) carrier.OrderEvacuate(this, evacuate.HasPosition, evacuate.X, evacuate.Y);
                else EvacuateContainer(container!);
                break;
            }
            case OverchargeCommand overcharge:
            {
                var reason = CheckOvercharge(command.Player, overcharge, out var module);
                if (reason != 0) { RecordRejection(command, reason); break; }
                module!.Toggle(this);
                break;
            }
            case CaptureCommand capture:
            {
                var reason = CheckCapture(command.Player, capture, out var actor, out var target);
                if (reason != 0) { RecordRejection(command, reason); break; }
                ExecuteCaptureCommand(actor!, target!);
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
        var observer = PhaseObserver;
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
            observer?.UpdateStarted(node.Module);
            var sleep = node.Update.Update(this);
            observer?.UpdateEnded(node.Module);
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
        ScoreTheKill(entity, info);
        // Object::onDie starts with checkAndDetonateBoobyTrap(NULL): an object that dies while booby trapped sets its trap off.
        if (entity.HasStatus(BoobyTrappedBit)) Modules.BuiltIn.StickyBombUpdate.CheckAndDetonateBoobyTrap(this, entity, null);
        foreach (var die in entity.Dies)
        {
            MarkDirty(entity);
            die.OnDie(this, info);
        }
    }

    /// <summary>
    /// <c>Object::scoreTheKill</c>: an enemy kill (not of a structure under construction, not of an <c>IGNORED_IN_GUI</c> object) gives the killer
    /// the victim's <c>ExperienceValue</c> for the victim's level and pays the killer's owner a share of the victim's cost
    /// (<c>Player::doBountyForKill</c>, <c>CashBountyPower</c>); neutral and allied kills score nothing. Skill points and experience sinks are not modeled.
    /// </summary>
    private void ScoreTheKill(Entity victim, in DamageInfo info)
    {
        if (!info.Source.IsValid || victim.Owner.IsSystem) return;
        var killer = FindEntity(info.Source);
        if (killer is null || killer.Owner.IsSystem || killer.Owner == victim.Owner) return;
        if (!AreEnemies(killer.Owner, victim.Owner)) return;
        if (victim.Template.IsKindOf("IGNORED_IN_GUI") || victim.HasStatus(ObjectStatusBits.UnderConstruction)) return;
        AddExperience(killer, victim.Template.ExperienceValue[victim.VeterancyLevel]);
        if (!_players.TryGetValue(killer.Owner.Value, out var payee) || payee.CashBounty.Raw == 0) return;
        var bounty = (victim.Template.BuildCost * payee.CashBounty).Ceiling();
        if (bounty > 0) payee.Money += Fixed.FromInt(bounty);
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
        // OnRemove hooks may request further destroys (spawner slaves); those append to the queue and are processed in the same pass.
        for (var queued = 0; queued < _destroyQueue.Count; queued++)
        {
            var entity = _destroyQueue[queued];
            foreach (var module in entity.ModuleArray) module.OnRemove(this);
            foreach (var node in entity.Updates) _updates.Remove(node);
            if (entity.Timers is { Count: > 0 } timers)
                foreach (var timer in timers.ToArray()) RemoveTimer(timer);
            // Energy::objectLeavingInfluence: the mirror image of the Register() hook.
            if (!entity.HasStatus(ObjectStatusBits.UnderConstruction) && _players.TryGetValue(entity.Owner.Value, out var owner)) owner.EnergyProduction -= entity.Template.EnergyProduction;
            if (entity.SellCompleteTick >= 0) _sales.Remove((entity.SellCompleteTick, entity.Id.Value));
            if (Navigation is { } navigation && NavigationGrid.Blocks(entity.Template))
                navigation.RemoveFootprint(entity.Id.Value, entity.Template, entity.X, entity.Y, entity.Angle);
            _entitySum = unchecked(_entitySum - entity.CachedHash);
            UnregisterStructureHooks(entity);
            _activeTurrets.Remove(entity.Id.Value);
            _acquirers.Remove(entity.Id.Value);
            _coolingTrackers.Remove(entity.Id.Value);
            _entities.Remove(entity.Id.Value);
            UnindexEntity(entity);
            UntrackCollidable(entity);
            entity.Registered = false;
            _moduleCount -= entity.ModuleArray.Length;
            _destroyedTotal++;
        }
        _destroyQueue.Clear();
        // A fixed point of the tick at which no loop over the entities is in flight.
        _entities.Compact();
    }

    private void ProcessSpawns()
    {
        if (_spawnQueue.Count == 0) return;
        // Damage applied while an entity is created (creation-list health transfer) can run die modules that queue further spawns;
        // those stay in the queue for the next tick.
        var batch = _spawnQueue.ToArray();
        _spawnQueue.Clear();
        foreach (var pending in batch) SpawnEntity(pending);
    }

    private Entity SpawnEntity(in PendingSpawn pending, EntityId builder = default)
    {
        var template = _templates[pending.Template];
        var setup = pending.Setup;
        var ground = GroundHeightAt(pending.X, pending.Y);
        var entity = new Entity(pending.Id, template, pending.Owner, _tick)
        {
            X = pending.X,
            Y = pending.Y,
            // Ground placement: anything not significantly above the terrain is put on it; airborne spawns keep their height.
            Z = Terrain is not null && !IsSignificantlyAbove(pending.Z, ground) ? ground : pending.Z,
            Angle = pending.Angle,
        };
        if (builder.IsValid)
        {
            entity.Status |= 1UL << ObjectStatusBits.UnderConstruction;
            entity.BuilderId = builder;
        }
        AttachModules(entity, template);
        if (setup.HasVeterancy && template.IsTrainable) SetVeterancyLevel(entity, setup.Veterancy);
        InitializeWeaponClip(entity);
        if (setup.MaxLifetimeTicks > 0)
            foreach (var module in entity.ModuleArray)
                if (module is Modules.BuiltIn.LifetimeUpdate lifetime) lifetime.OverrideRange(setup.MinLifetimeTicks, setup.MaxLifetimeTicks);
        foreach (var module in entity.ModuleArray) module.OnSpawn(this);
        ApplyBattlePlanToEntity(entity);
        if (setup.Producer.IsValid)
            foreach (var module in entity.ModuleArray)
                if (module is IProducerReceiver receiver) receiver.OnProducedBy(this, setup.Producer);
        if (setup.HasOrigin)
            foreach (var module in entity.ModuleArray)
                if (module is ISpawnOriginReceiver receiver) receiver.OnSpawnedFrom(this, setup.OriginTemplate);
        if (setup.ForceSupplyWanting)
            foreach (var module in entity.ModuleArray)
                if (module is Modules.BuiltIn.ISupplyCarrier carrier) carrier.ForceWanting(this);
        if (setup.MaxHealth > Fixed.Zero && entity.Body is IMaxHealthAdjustable adjustable)
            adjustable.AdjustMaxHealth(this, setup.MaxHealth - entity.Body.MaxHealth, "SAME_CURRENTHEALTH");
        if (setup.HasHealth && entity.Body is IInitialHealthSettable health) health.SetInitialHealth(this, setup.HealthPercent);
        if (builder.IsValid && entity.Body is IConstructionBody site)
            site.ChangeHealthForConstruction(this, Fixed.One - entity.Body.Health);
        Register(entity);
        Emit(SimEventKind.EntitySpawned, entity.Id, EntityId.Invalid, template.Name, 0, pending.Owner);
        _spawnedTotal++;
        // SlavedUpdateInterface::onEnslave, called by the creator (SpawnBehavior::createSpawn, the creation nugget) once the object exists.
        if (setup.Enslaver.IsValid && FindEntity(setup.Enslaver) is { DestroyRequested: false } enslaver)
        {
            foreach (var module in entity.ModuleArray)
                if (module is Modules.BuiltIn.ISlavedModule slaved)
                {
                    slaved.OnEnslave(this, enslaver);
                    break;
                }
            MarkDirty(entity);
        }

        if (setup.Container.IsValid && FindEntity(setup.Container) is { Container: { } group } holder && !holder.DestroyRequested
            && group.IsValidContainerFor(this, entity, true))
        {
            group.Add(this, entity);
            Emit(SimEventKind.EntityContained, entity.Id, holder.Id, string.Empty, 0, pending.Owner);
        }
        if (setup.ContainInSource.IsValid)
        {
            var source = FindEntity(setup.ContainInSource);
            if (source is { DestroyRequested: false, Container: { } sourceGroup } && sourceGroup.IsValidContainerFor(this, entity, true))
            {
                sourceGroup.Add(this, entity);
                Emit(SimEventKind.EntityContained, entity.Id, source.Id, string.Empty, 0, pending.Owner);
            }
            else RequestDestroy(entity.Id);
        }
        if (setup.TransferDamage > Fixed.Zero)
            Damage(entity.Id, new DamageInfo(setup.TransferSource, setup.TransferDamage, DamageTypes.Unresistable, DeathTypes.Normal, false));
        if (setup.HasRally && template.Locomotor is not null && AiOf(entity) is { } rallyAi) rallyAi.OrderMove(this, setup.RallyX, setup.RallyY, false);
        if (setup.SetCreator)
            foreach (var module in entity.ModuleArray)
                if (module is Modules.BuiltIn.SpecialPowerCompletionDie completion) completion.SetCreator(setup.Creator);
        if (setup.Delivery is { } order && AiOf(entity) is Modules.BuiltIn.DeliverPayloadAIUpdate deliverer) deliverer.DeliverPayload(this, order);
        if (setup.Launch is { } launch)
        {
            // fireWeaponTemplate: an object without a ProjectileUpdateInterface is simply put at the destination ("ok, for things like Firestorm").
            if (Array.Find(entity.ModuleArray, module => module is IProjectileModule) is IProjectileModule projectile)
            {
                projectile.Launch(this, launch);
                // Weapon::fireWeaponTemplate: a small missile fired at an object with active countermeasures lets it decide now whether a flare will take the missile.
                if (launch.Victim.IsValid && template.IsKindOf("SMALL_MISSILE") && FindEntity(launch.Victim) is { } victim
                    && Array.Find(victim.ModuleArray, module => module is Modules.BuiltIn.ICountermeasuresModule { IsActive: true }) is Modules.BuiltIn.ICountermeasuresModule countermeasures)
                    countermeasures.ReportMissile(this, entity);
            }
            else
            {
                entity.X = launch.X;
                entity.Y = launch.Y;
                entity.Z = launch.Z;
            }
            MarkDirty(entity);
        }
        return entity;
    }

    private void AttachModules(Entity entity, ObjectTemplate template)
    {
        InitializeTurrets(entity);
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

    /// <summary>
    /// Every weapon of the active set starts with a full clip (<c>WeaponSet::updateWeaponSet</c>: <c>loadAmmoNow</c> for each slot; <c>WeaponSetUpgrade</c> re-initializes
    /// them if the set switches later). Fresh spawns only: a restored entity already carries its saved weapon state. An object with a single weapon keeps it in
    /// <see cref="Entity.WeaponClip"/>; one with several gets <see cref="Entity.WeaponSlots"/>.
    /// </summary>
    private void InitializeWeaponClip(Entity entity)
    {
        var set = ActiveWeaponSet(entity);
        if (!entity.Template.HasMultipleWeaponSlots)
        {
            var weaponIndex = set?.Primary ?? -1;
            entity.WeaponClip = weaponIndex < 0 ? -1 : ClipCapacity(_templates.Weapons[weaponIndex]);
            return;
        }
        var state = new WeaponSlotState { SetIndex = set!.Index };
        entity.WeaponSlots = state;
        FillSlots(entity, set, 0);
    }

    private static int ClipCapacity(Templates.WeaponInfo weapon) => weapon.ClipSize == 0 ? int.MaxValue : weapon.ClipSize;

    /// <summary>Adds a fully built entity (fresh or restored) to the tables and schedules its update modules.</summary>
    private void Register(Entity entity)
    {
        _entities.Add(entity.Id.Value, entity);
        entity.Registered = true;
        IndexEntity(entity);
        TrackCollidable(entity);
        _moduleCount += entity.ModuleArray.Length;
        // Energy::objectEnteringInfluence: incremental, not recomputed per frame.
        if (!entity.HasStatus(ObjectStatusBits.UnderConstruction) && _players.TryGetValue(entity.Owner.Value, out var owner)) owner.EnergyProduction += entity.Template.EnergyProduction;
        if (entity.SellCompleteTick >= 0) _sales.Add((entity.SellCompleteTick, entity.Id.Value));
        if (Navigation is { } navigation && NavigationGrid.Blocks(entity.Template))
            navigation.AddFootprint(entity.Id.Value, entity.Template, entity.X, entity.Y, entity.Angle);
        RegisterStructureHooks(entity);
        RegisterTurrets(entity);
        RegisterFiringTracker(entity);
        RegisterAcquirer(entity);
        foreach (var module in entity.ModuleArray)
            if (module is IWorldAttached attached) attached.Attach(this);
        foreach (var node in entity.Updates)
        {
            var update = node.Update;
            if (update.NextWake < 0) update.NextWake = _tick + SimulationTiming.SleepNone;
            if (update.NextWake > SimulationTiming.SleepForever) update.NextWake = SimulationTiming.SleepForever;
            if (update.NextWake < SimulationTiming.SleepForever) _updates.Push(node);
        }
        MarkDirty(entity);
    }

    public void MarkDirty(Entity entity)
    {
        if (entity.Dirty) return;
        entity.Dirty = true;
        _dirty.Add(entity);
    }

    // ------------------------------------------------------------------------------------------------- step

    /// <summary>Executes one tick and returns its state hash. The returned buffers are reused by the next call.</summary>
    public TickResult Step()
    {
        if (PhaseObserver is { } observer) return StepObserved(observer);
        var tick = _tick;
        _tickEvents.Clear();
        ExecuteCommands(tick);
        FireTimers(tick);
        RunUpdates(tick);
        StepAcquisition(tick);
        StepTurrets(tick);
        StepFiringTrackers(tick);
        DeliverEvents();
        ProcessSales(tick);
        ProcessDestroys();
        ProcessDelayedDamage(tick);
        ProcessSpawns();
        DeliverEvents();
        if (_vision is not null && tick % VisionIntervalTicks == 0) RefreshVision();
        _tick = tick + 1;
        return new TickResult(tick, ComputeIncrementalHash(), _tickEvents);
    }

    /// <summary>
    /// A tool's measuring hook (<see cref="ITickPhaseObserver"/>); <see langword="null"/> unless a tool installs one. Not part of the state: neither hashed nor saved.
    /// </summary>
    public ITickPhaseObserver? PhaseObserver { get; set; }

    /// <summary>Entities touched since the state hash was last computed: what the next hash has to describe again.</summary>
    public int DirtyEntityCount => _dirty.Count;

    /// <summary>The same step as <see cref="Step"/>, statement for statement, with the observer told about every phase boundary (every phase exactly once per tick, also an empty one).</summary>
    private TickResult StepObserved(ITickPhaseObserver observer)
    {
        var tick = _tick;
        _tickEvents.Clear();
        observer.PhaseStarted(TickPhase.Commands);
        ExecuteCommands(tick);
        observer.PhaseEnded(TickPhase.Commands);
        observer.PhaseStarted(TickPhase.Timers);
        FireTimers(tick);
        observer.PhaseEnded(TickPhase.Timers);
        observer.PhaseStarted(TickPhase.Updates);
        RunUpdates(tick);
        observer.PhaseEnded(TickPhase.Updates);
        observer.PhaseStarted(TickPhase.Acquisition);
        StepAcquisition(tick);
        observer.PhaseEnded(TickPhase.Acquisition);
        observer.PhaseStarted(TickPhase.Turrets);
        StepTurrets(tick);
        observer.PhaseEnded(TickPhase.Turrets);
        observer.PhaseStarted(TickPhase.FiringTrackers);
        StepFiringTrackers(tick);
        observer.PhaseEnded(TickPhase.FiringTrackers);
        observer.PhaseStarted(TickPhase.Events);
        DeliverEvents();
        observer.PhaseEnded(TickPhase.Events);
        observer.PhaseStarted(TickPhase.Sales);
        ProcessSales(tick);
        observer.PhaseEnded(TickPhase.Sales);
        observer.PhaseStarted(TickPhase.Destroys);
        ProcessDestroys();
        observer.PhaseEnded(TickPhase.Destroys);
        observer.PhaseStarted(TickPhase.DelayedDamage);
        ProcessDelayedDamage(tick);
        observer.PhaseEnded(TickPhase.DelayedDamage);
        observer.PhaseStarted(TickPhase.Spawns);
        ProcessSpawns();
        observer.PhaseEnded(TickPhase.Spawns);
        observer.PhaseStarted(TickPhase.SpawnEvents);
        DeliverEvents();
        observer.PhaseEnded(TickPhase.SpawnEvents);
        var refresh = _vision is not null && tick % VisionIntervalTicks == 0;
        observer.PhaseStarted(TickPhase.Vision);
        if (refresh) RevealVision();
        observer.PhaseEnded(TickPhase.Vision);
        observer.PhaseStarted(TickPhase.VisionHash);
        if (refresh) HashVision();
        observer.PhaseEnded(TickPhase.VisionHash);
        observer.PhaseStarted(TickPhase.Hash);
        _tick = tick + 1;
        var hash = ComputeIncrementalHash();
        observer.PhaseEnded(TickPhase.Hash);
        return new TickResult(tick, hash, _tickEvents);
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
        // Name the entities whose cached hash is stale before the full recomputation refreshes the caches: they changed without MarkDirty.
        List<string>? stale = null;
        foreach (var entity in _entities.Values)
            if (HashEntity(entity) != entity.CachedHash) (stale ??= []).Add(Scripting.InvariantText.Format($"{entity.Id.Value} {entity.Template.Name}"));
        var full = ComputeFullHash();
        if (full == incremental) return;
        var cause = stale is null ? "no entity is stale: timers, commands or global state"
            : "changed without MarkDirty: " + string.Join(", ", stale.Take(12)) + (stale.Count > 12 ? Scripting.InvariantText.Format($" and {stale.Count - 12} more") : string.Empty);
        throw new InvalidOperationException(Scripting.InvariantText.Format($"Incremental state hash {incremental:X16} differs from the full hash {full:X16} at tick {_tick} ({cause})."));
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
        // Written only once a fire order waited for a turret, so a world without turrets keeps its serialized form (and hash).
        if (_turretWaits != 0) writer.Int("turretWaits", _turretWaits);
        if (_vision is not null) writer.UInt("visionHash", HashVision());
        writer.BeginArray("players");
        foreach (var player in _playerList)
        {
            writer.BeginObject(null);
            writer.Int("id", player.Id.Value);
            writer.Text("name", player.Name);
            writer.Int("team", player.Team);
            writer.Bool("computer", player.IsComputer);
            writer.UInt("lastSequence", player.LastSequence);
            writer.BeginArray("relations");
            for (var index = 0; index < player.Relations.Count; index++)
            {
                writer.BeginObject(null);
                writer.Int("player", player.Relations.Keys[index]);
                writer.Int("relationship", (int)player.Relations.Values[index]);
                writer.EndObject();
            }
            writer.EndArray();
            writer.Fixed("money", player.Money);
            writer.Int("energyProduction", player.EnergyProduction);
            player.CompletedUpgrades.Describe(writer, "completedUpgrades");
            player.CompletedSciences.Describe(writer, "completedSciences");
            writer.Int("sciencePurchasePoints", player.SciencePurchasePoints);
            writer.Fixed("cashBounty", player.CashBounty);
            // Written only when switched off, so a world no script ever restricted keeps the serialized form (and hash) it had before the switches existed.
            if (!player.CanBuildBase) writer.Bool("canBuildBase", false);
            if (!player.CanBuildUnits) writer.Bool("canBuildUnits", false);
            DescribeCostModifiers(writer, player);
            writer.BeginArray("sharedReadyFrames");
            // Indexed: SortedList's foreach boxes its enumerator, and this runs every tick.
            for (var index = 0; index < player.SharedReadyFrames.Count; index++)
            {
                writer.BeginObject(null);
                writer.Int("power", player.SharedReadyFrames.Keys[index]);
                writer.Int("frame", player.SharedReadyFrames.Values[index]);
                writer.EndObject();
            }
            writer.EndArray();
            player.Tunnels.Describe(writer);
            // Written only once a power plant was sabotaged, so every world without a saboteur keeps its serialized form (and hash).
            if (player.PowerSabotagedUntil > 0) writer.Int("powerSabotagedUntil", player.PowerSabotagedUntil);
            player.BattlePlan?.Describe(writer);
            writer.EndObject();
        }
        writer.EndArray();
        DescribeDelayedDamage(writer);
        writer.BeginArray("rejections");
        for (var index = 0; index < LegacyRejectionEntries; index++)
        {
            writer.BeginObject(null);
            writer.Int("count", _rejections[index]);
            writer.EndObject();
        }
        writer.EndArray();
        // Reasons added after this table was frozen are written only once they occurred, so a run without them hashes exactly as it did before.
        for (var index = LegacyRejectionEntries; index < _rejections.Length; index++)
            if (_rejections[index] != 0) writer.Int("rejection" + index, _rejections[index]);
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
            var setup = pending.Setup;
            writer.Bool("hasHealth", setup.HasHealth);
            writer.Int("healthPercent", setup.HealthPercent);
            writer.Bool("hasVeterancy", setup.HasVeterancy);
            writer.Int("veterancy", setup.Veterancy);
            writer.Int("minLifetime", setup.MinLifetimeTicks);
            writer.Int("maxLifetime", setup.MaxLifetimeTicks);
            writer.UInt("container", setup.Container.Value);
            writer.UInt("containInSource", setup.ContainInSource.Value);
            writer.Fixed("transferDamage", setup.TransferDamage);
            writer.UInt("transferSource", setup.TransferSource.Value);
            writer.Fixed("maxHealth", setup.MaxHealth);
            writer.Bool("hasOrigin", setup.HasOrigin);
            writer.Int("originTemplate", setup.OriginTemplate);
            writer.Bool("forceSupplyWanting", setup.ForceSupplyWanting);
            writer.Bool("hasRally", setup.HasRally);
            writer.Fixed("rallyX", setup.RallyX);
            writer.Fixed("rallyY", setup.RallyY);
            if (setup.Producer.IsValid) writer.UInt("producer", setup.Producer.Value);
            if (setup.Launch is { } launch)
            {
                writer.Int("launchWeapon", launch.Weapon);
                writer.UInt("launcher", launch.Launcher.Value);
                writer.UInt("launchVictim", launch.Victim.Value);
                writer.Fixed("launchX", launch.X);
                writer.Fixed("launchY", launch.Y);
                writer.Fixed("launchZ", launch.Z);
                writer.UInt("launchConditions", launch.Conditions);
            }
            if (setup.Delivery is { } delivery)
            {
                writer.Fixed("deliveryMoveX", delivery.MoveToX);
                writer.Fixed("deliveryMoveY", delivery.MoveToY);
                writer.Fixed("deliveryTargetX", delivery.TargetX);
                writer.Fixed("deliveryTargetY", delivery.TargetY);
                writer.Int("deliveryDelay", delivery.DelayTicks);
                delivery.Run.Describe(writer);
            }
            if (setup.SetCreator) writer.UInt("creator", setup.Creator.Value);
            if (setup.Enslaver.IsValid) writer.UInt("enslaver", setup.Enslaver.Value);
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
