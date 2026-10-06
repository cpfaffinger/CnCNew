using System.Collections.Immutable;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

internal static class SpawnModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<SpawnBehaviorData>("SpawnBehavior", ModuleOwnerKind.Object, (configuration, _) => SpawnBehaviorData.Parse(configuration), data => new SpawnBehavior(data)),
    ];
}

/// <summary>
/// <c>SpawnBehavior</c> field set (<c>SpawnBehavior.h</c>). <c>CanReclaimOrphans</c> (on a non-one-shot spawner) and
/// <c>PropagateDamageTypesToSlavesWhenExisting</c> need producer links that do not exist: an object that asks for one of them keeps the module
/// in <c>MissingModules</c> instead of being simulated wrongly. <c>AggregateHealth</c> and <c>ExitByBudding</c> (the angry mob's nexus) are simulated.
/// <c>SlavesHaveFreeWill</c> only steers the slaves' own AI and <c>InitialBurst</c> only paces the nexus' exit through its barracks; both are read by the schema but have no effect here.
/// </summary>
public sealed record SpawnBehaviorData(
    int SpawnNumber, long ReplaceDelayTicks, bool OneShot, ImmutableArray<string> SpawnTemplates, bool RequireSpawner, DieMuxData Mux,
    bool AggregateHealth = false, bool ExitByBudding = false) : ModuleData
{
    public static SpawnBehaviorData Parse(RatedModuleConfiguration configuration)
    {
        var oneShot = ModuleConfigReader.Bool(configuration, "OneShot", false);
        if (!oneShot && ModuleConfigReader.Bool(configuration, "CanReclaimOrphans", false)) throw new UnsupportedModuleConfigurationException("CanReclaimOrphans");
        if (ModuleConfigReader.Flags(configuration, "PropagateDamageTypesToSlavesWhenExisting") is { Names.Length: > 0 })
            throw new UnsupportedModuleConfigurationException("PropagateDamageTypesToSlavesWhenExisting");
        var templates = ModuleConfigReader.AccumulatedNames(configuration, "SpawnTemplateName");
        var number = checked((int)ModuleConfigReader.Int(configuration, "SpawnNumber", 0));
        if (number > 0 && templates.IsEmpty) throw new InvalidDataException("SpawnBehavior has a SpawnNumber but no SpawnTemplateName.");
        return new SpawnBehaviorData(number, ModuleConfigReader.Ticks(configuration, "SpawnReplaceDelay", 0), oneShot, templates,
            ModuleConfigReader.Bool(configuration, "SpawnedRequireSpawner", false), DieMuxData.Parse(configuration),
            ModuleConfigReader.Bool(configuration, "AggregateHealth", false), ModuleConfigReader.Bool(configuration, "ExitByBudding", false));
    }
}

/// <summary>
/// <c>SpawnBehavior</c> (<c>SpawnBehavior.cpp</c>): a spawner that keeps <c>SpawnNumber</c> objects alive, cycling through its template list.
/// It works every <c>SPAWN_UPDATE_RATE</c> (15) ticks, starting with the first update; each spawn leaves through the object's
/// <see cref="IProductionExit"/> (no exit module: nothing is ever created, reported once as <see cref="CreationSkipReasons.NoExitInterface"/>).
/// Differences from the reference, all listed in docs/GAME-RULES.md: a spawn's death is found by polling at the 15-tick rhythm instead of
/// through <c>onSpawnDeath</c> (the replacement is due <c>SpawnReplaceDelay</c> after the poll finds it); the initial burst does not stagger
/// (the reference computes a birth frame and then never uses it); spawns are not linked to a producer id (orphans cannot be reclaimed); a template that cannot be
/// instantiated is reported as <see cref="SimEventKind.CreationSkipped"/>, consumes its slot and is never retried.
/// <para>
/// A spawn whose template carries a <see cref="ISlavedModule"/> (<c>SlavedUpdate</c>, <c>MobMemberSlavedUpdate</c>) is enslaved the moment it exists
/// (<see cref="SpawnExtras.Enslaver"/>), the spawner tells its slaves when it is damaged or dies (<c>onSlaverDamage</c>, <c>onSlaverDie</c>) and hands out its living slaves
/// (<see cref="ISpawnerModule"/>) to a <c>HiveStructureBody</c>.
/// </para>
/// <para>
/// <c>AggregateHealth</c> (the nexus of an angry mob, <c>computeAggregateStates</c>): every tick the spawner's health becomes the share of its slaves' health in the
/// health a full population would have (<c>setInitialHealth(100 * sum / (averageMax * SpawnNumber))</c>, whole percent, 0 without slaves), the veterancy of spawner and slaves is
/// levelled up to the highest of them, and the spawner is <c>MASKED</c> (nobody shoots at a nexus that "doesn't exist"); the death of its last slave destroys it. With aggregate
/// health the module therefore wakes every tick; the creation work still runs every 15th tick (the reference's frame counter). The selection sharing and the health-box
/// offset are presentation. The score of that last kill (<c>scoreTheKill</c>) is not credited.
/// </para>
/// <para>
/// <c>ExitByBudding</c> (<c>exitObjectByBudding</c>): a new spawn does not leave through the exit but appears on the living spawn closest to the spawner (within 1000 units;
/// otherwise on the spawner), then is ordered to move to where it stands so the formation spreads; the exit's delay and burst counters advance as in the reference.
/// The reference first lets the nexus' <c>InitialBurst</c> spawns leave through the barracks that produced it (the producer link is not modelled), so every spawn buds here.
/// </para>
/// </summary>
internal sealed class SpawnBehavior(SpawnBehaviorData data) : SimModule, IUpdateModule, IDieModule, IDamageModule, ISpawnerModule
{
    private const long UpdateRateFrames = SimulationRate.OriginalFramesPerSecond / 2;
    private const long BeforeAnyTick = -1;
    private static readonly Fixed BuddingReach = Fixed.FromDecimal(999999.9m);
    private static readonly int MaskedBit = ObjectStatusBits.Index("MASKED");

    private long _nextWake = -1;
    private bool _initialised;
    private bool _active = true;
    private bool _exitMissing;
    private int _oneShotCountdown;
    private int _templateCursor;
    private long _workTick;
    private readonly List<long> _replacementTimes = [];
    private readonly List<EntityId> _spawns = [];

    public override string ModuleName => "SpawnBehavior";
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public int LivingSpawnCount => _spawns.Count;
    public int SpawnNumber => data.SpawnNumber;
    public IReadOnlyList<EntityId> Slaves => _spawns;

    public override void OnSpawn(IWorldServices world)
    {
        _oneShotCountdown = data.OneShot ? data.SpawnNumber : -1;
        _nextWake = world.Tick + SimulationTiming.SleepNone;
    }

    public long Update(IWorldServices world)
    {
        if (data.AggregateHealth)
        {
            // onSpawnDeath is immediate in the reference, and computeAggregateStates runs on every frame before the sparsely solved part.
            CollectDead(world);
            ComputeAggregateStates(world);
            if (world.Tick < _workTick) return SimulationTiming.SleepNone;
        }
        if (!_initialised)
        {
            _initialised = true;
            for (var index = 0; index < data.SpawnNumber; index++) _replacementTimes.Add(BeforeAnyTick);
        }
        if (!data.AggregateHealth) CollectDead(world);
        if (ShouldTryToSpawn())
        {
            var index = 0;
            while (index < _replacementTimes.Count && !_exitMissing)
            {
                if (world.Tick > _replacementTimes[index] && CreateSpawn(world)) _replacementTimes.RemoveAt(index);
                else index++;
            }
            if (data.OneShot && _oneShotCountdown <= 0) _active = false;
        }
        if (data.AggregateHealth)
        {
            _workTick = world.Tick + world.Time.OriginalFrames(UpdateRateFrames);
            return SimulationTiming.SleepNone;
        }
        return _active && !_exitMissing ? world.Time.OriginalFrames(UpdateRateFrames) : SimulationTiming.SleepForever;
    }

    private void CollectDead(IWorldServices world)
    {
        var removed = false;
        for (var index = 0; index < _spawns.Count;)
        {
            if (world.FindEntity(_spawns[index]) is { DestroyRequested: false, EffectivelyDead: false })
            {
                index++;
                continue;
            }
            _spawns.RemoveAt(index);
            _replacementTimes.Add(world.Tick + data.ReplaceDelayTicks);
            removed = true;
        }
        // onSpawnDeath: "I'm dead without my spawn".
        if (removed && data.AggregateHealth && _spawns.Count == 0 && !Entity.DestroyRequested) world.RequestDestroy(Entity.Id);
    }

    /// <summary><c>SpawnBehavior::computeAggregateStates</c> without the selection and health-box parts (presentation).</summary>
    private void ComputeAggregateStates(IWorldServices world)
    {
        var count = 0;
        var health = Fixed.Zero;
        var maxHealth = Fixed.Zero;
        var level = Entity.VeterancyLevel;
        foreach (var id in _spawns)
            if (world.FindEntity(id) is { } spawn && spawn.Body is { } body)
            {
                if (spawn.VeterancyLevel > level)
                {
                    world.SetMinVeterancyLevel(Entity, spawn.VeterancyLevel);
                    level = spawn.VeterancyLevel;
                }
                else if (spawn.VeterancyLevel < level) world.SetMinVeterancyLevel(spawn, level);
                health += body.Health;
                maxHealth += body.MaxHealth;
                count++;
            }
        if (Entity.Body is IInitialHealthSettable settable)
        {
            var percent = 0;
            if (count > 0)
            {
                var perfect = maxHealth / Fixed.FromInt(count) * Fixed.FromInt(data.SpawnNumber);
                if (perfect > Fixed.Zero) percent = checked((int)(Fixed.FromInt(100) * health / perfect).Floor());
            }
            settable.SetInitialHealth(world, percent);
        }
        Entity.Status |= 1UL << MaskedBit;
    }

    /// <summary><c>SpawnBehavior::onDamage</c>.</summary>
    public void OnDamage(IWorldServices world, in DamageInfo info, Fixed actualDamageDealt)
    {
        foreach (var id in _spawns)
            if (world.FindEntity(id) is { } spawn && SlavedModuleOf(spawn) is { } slaved) slaved.OnSlaverDamage(world, info);
    }

    private static ISlavedModule? SlavedModuleOf(Entity entity) => entity.ModuleArray.OfType<ISlavedModule>().FirstOrDefault();

    private static bool IsSlaveTemplate(Templates.ObjectTemplate template)
        => template.Modules.Any(spec => spec.Binding.Name is "SlavedUpdate" or "MobMemberSlavedUpdate");

    /// <summary><c>exitObjectByBudding</c>'s choice of host: the living spawn closest to the spawner, if within reach.</summary>
    private Entity? BuddingHost(IWorldServices world)
    {
        Entity? host = null;
        var closest = BuddingReach;
        foreach (var id in _spawns)
        {
            if (world.FindEntity(id) is not { } candidate) continue;
            var distance = SlaveSupport.CenterDistanceSquared2D(candidate, Entity);
            if (distance >= closest) continue;
            host = candidate;
            closest = distance;
        }
        return host;
    }

    private bool ShouldTryToSpawn()
    {
        if (!_active) return false;
        if (Entity.HasStatus(ObjectStatusBits.Reconstructing) && data.OneShot)
        {
            _active = false;
            return false;
        }
        if (Entity.HasStatus(ObjectStatusBits.UnderConstruction) || Entity.HasStatus(ObjectStatusBits.Sold)) return false;
        return !Entity.Owner.IsSystem;
    }

    private bool CreateSpawn(IWorldServices world)
    {
        var exit = Entity.Modules.OfType<IProductionExit>().FirstOrDefault();
        if (exit is null)
        {
            world.Emit(SimEventKind.CreationSkipped, Entity.Id, EntityId.Invalid, data.SpawnTemplates[0], CreationSkipReasons.NoExitInterface);
            _exitMissing = true;
            return false;
        }
        if (!exit.IsFreeToExit) return false;

        var name = data.SpawnTemplates[_templateCursor];
        _templateCursor = (_templateCursor + 1) % data.SpawnTemplates.Length;
        if (data.OneShot) _oneShotCountdown--;
        if (!world.Templates.TryGet(name, out var template))
        {
            world.Emit(SimEventKind.CreationSkipped, Entity.Id, EntityId.Invalid, name, CreationSkipReasons.UnknownTemplate);
            return true;
        }
        if (!template.IsInstantiable)
        {
            world.Emit(SimEventKind.CreationSkipped, Entity.Id, EntityId.Invalid, name, CreationSkipReasons.TemplateNotInstantiable);
            return true;
        }
        var (x, y, z, angle) = exit.ClaimExit(world);
        var rally = false;
        if (data.ExitByBudding)
        {
            // exitObjectByBudding: onto the closest spawn (or the spawner), then "I cant stay here, if somebody else is here".
            var host = BuddingHost(world) ?? Entity;
            (x, y, z, angle) = (host.X, host.Y, host.Z, host.Angle);
            rally = true;
        }
        var enslaver = IsSlaveTemplate(template) ? Entity.Id : EntityId.Invalid;
        var id = rally || enslaver.IsValid
            ? world.RequestSpawn(template.Index, Entity.Owner, x, y, z, angle, new SpawnExtras(Fixed.Zero, ForceSupplyWanting: exit.ForcesSupplyWanting, HasRally: rally, RallyX: x, RallyY: y, Enslaver: enslaver))
            : exit.ForcesSupplyWanting
                ? world.RequestSpawn(template.Index, Entity.Owner, x, y, z, angle, new SpawnExtras(Fixed.Zero, ForceSupplyWanting: true))
                : world.RequestSpawn(template.Index, Entity.Owner, x, y, z, angle);
        _spawns.Add(id);
        if (exit is IProductionExitOccupancy occupancy) occupancy.Occupy(id);
        return true;
    }

    public void OnDie(IWorldServices world, in DamageInfo info)
    {
        if (!data.Mux.IsApplicable(Entity, info)) return;
        // Go through all my spawns and see if they have a SlavedUpdate I can tell I was killed to.
        foreach (var id in _spawns)
            if (world.FindEntity(id) is { } spawn && SlavedModuleOf(spawn) is { } slaved) slaved.OnSlaverDie(world);
        if (!data.RequireSpawner) return;
        foreach (var id in _spawns.ToArray())
            if (world.FindEntity(id) is { EffectivelyDead: false, DestroyRequested: false }) world.Kill(id);
    }

    public override void OnRemove(IWorldServices world)
    {
        if (!data.RequireSpawner) return;
        foreach (var id in _spawns)
            if (world.FindEntity(id) is { EffectivelyDead: false, DestroyRequested: false }) world.RequestDestroy(id);
    }

    public override void Describe(IStateWriter writer)
    {
        writer.Int("nextWake", _nextWake);
        writer.Bool("initialised", _initialised);
        writer.Bool("active", _active);
        writer.Bool("exitMissing", _exitMissing);
        writer.Int("oneShotCountdown", _oneShotCountdown);
        writer.Int("templateCursor", _templateCursor);
        if (data.AggregateHealth) writer.Int("workTick", _workTick);
        writer.BeginArray("replacementTimes");
        foreach (var time in _replacementTimes)
        {
            writer.BeginObject(null);
            writer.Int("tick", time);
            writer.EndObject();
        }
        writer.EndArray();
        writer.BeginArray("spawns");
        foreach (var id in _spawns)
        {
            writer.BeginObject(null);
            writer.UInt("id", id.Value);
            writer.EndObject();
        }
        writer.EndArray();
    }

    public override void Load(IStateReader reader)
    {
        _nextWake = reader.Int("nextWake");
        _initialised = reader.Bool("initialised");
        _active = reader.Bool("active");
        _exitMissing = reader.Bool("exitMissing");
        _oneShotCountdown = checked((int)reader.Int("oneShotCountdown"));
        _templateCursor = checked((int)reader.Int("templateCursor"));
        _workTick = data.AggregateHealth ? reader.Int("workTick") : 0;
        if (data.SpawnTemplates.Length > 0 && (_templateCursor < 0 || _templateCursor >= data.SpawnTemplates.Length)) throw new InvalidDataException("Spawn template cursor out of range.");
        _replacementTimes.Clear();
        foreach (var node in reader.Array("replacementTimes")) _replacementTimes.Add(node.Int("tick"));
        _spawns.Clear();
        foreach (var node in reader.Array("spawns")) _spawns.Add(new EntityId(checked((uint)node.UInt("id"))));
    }
}
