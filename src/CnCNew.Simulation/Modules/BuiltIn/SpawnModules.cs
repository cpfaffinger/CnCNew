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
/// <c>SpawnBehavior</c> field set (<c>SpawnBehavior.h</c>). <c>AggregateHealth</c>, <c>ExitByBudding</c>, <c>CanReclaimOrphans</c> (on a non-one-shot
/// spawner) and <c>PropagateDamageTypesToSlavesWhenExisting</c> need the slaved-AI family (mobs, hives) that does not exist yet: an object
/// that asks for one of them keeps the module in <c>MissingModules</c> instead of being simulated wrongly.
/// <c>SlavesHaveFreeWill</c> only steers the slaves' own AI and is read by the schema but has no effect here.
/// </summary>
public sealed record SpawnBehaviorData(
    int SpawnNumber, long ReplaceDelayTicks, bool OneShot, ImmutableArray<string> SpawnTemplates, bool RequireSpawner, DieMuxData Mux) : ModuleData
{
    public static SpawnBehaviorData Parse(ModuleConfiguration configuration)
    {
        if (ModuleConfigReader.Bool(configuration, "AggregateHealth", false)) throw new UnsupportedModuleConfigurationException("AggregateHealth");
        if (ModuleConfigReader.Bool(configuration, "ExitByBudding", false)) throw new UnsupportedModuleConfigurationException("ExitByBudding");
        var oneShot = ModuleConfigReader.Bool(configuration, "OneShot", false);
        if (!oneShot && ModuleConfigReader.Bool(configuration, "CanReclaimOrphans", false)) throw new UnsupportedModuleConfigurationException("CanReclaimOrphans");
        if (ModuleConfigReader.Flags(configuration, "PropagateDamageTypesToSlavesWhenExisting") is { Names.Length: > 0 })
            throw new UnsupportedModuleConfigurationException("PropagateDamageTypesToSlavesWhenExisting");
        var templates = ModuleConfigReader.AccumulatedNames(configuration, "SpawnTemplateName");
        var number = checked((int)ModuleConfigReader.Int(configuration, "SpawnNumber", 0));
        if (number > 0 && templates.IsEmpty) throw new InvalidDataException("SpawnBehavior has a SpawnNumber but no SpawnTemplateName.");
        return new SpawnBehaviorData(number, ModuleConfigReader.Ticks(configuration, "SpawnReplaceDelay", 0), oneShot, templates,
            ModuleConfigReader.Bool(configuration, "SpawnedRequireSpawner", false), DieMuxData.Parse(configuration));
    }
}

/// <summary>
/// <c>SpawnBehavior</c> (<c>SpawnBehavior.cpp</c>): a spawner that keeps <c>SpawnNumber</c> objects alive, cycling through its template list.
/// It works every <c>SPAWN_UPDATE_RATE</c> (15) ticks, starting with the first update; each spawn leaves through the object's
/// <see cref="IProductionExit"/> (no exit module: nothing is ever created, reported once as <see cref="CreationSkipReasons.NoExitInterface"/>).
/// Differences from the reference, all listed in docs/GAME-RULES.md: a spawn's death is found by polling at the 15-tick rhythm instead of
/// through <c>onSpawnDeath</c> (the replacement is due <c>SpawnReplaceDelay</c> after the poll finds it); the initial burst does not stagger
/// (the reference computes a birth frame and then never uses it); spawns are not linked to a producer id or a <c>SlavedUpdate</c>
/// (that family does not exist yet); a template that cannot be instantiated is reported as <see cref="SimEventKind.CreationSkipped"/>, consumes
/// its slot and is never retried.
/// </summary>
internal sealed class SpawnBehavior(SpawnBehaviorData data) : SimModule, IUpdateModule, IDieModule
{
    private const long UpdateRate = SimulationTiming.LogicFramesPerSecond / 2;
    private const long BeforeAnyTick = -1;

    private long _nextWake = -1;
    private bool _initialised;
    private bool _active = true;
    private bool _exitMissing;
    private int _oneShotCountdown;
    private int _templateCursor;
    private readonly List<long> _replacementTimes = [];
    private readonly List<EntityId> _spawns = [];

    public override string ModuleName => "SpawnBehavior";
    public long NextWake { get => _nextWake; set => _nextWake = value; }
    public int LivingSpawnCount => _spawns.Count;

    public override void OnSpawn(IWorldServices world)
    {
        _oneShotCountdown = data.OneShot ? data.SpawnNumber : -1;
        _nextWake = world.Tick + SimulationTiming.SleepNone;
    }

    public long Update(IWorldServices world)
    {
        if (!_initialised)
        {
            _initialised = true;
            for (var index = 0; index < data.SpawnNumber; index++) _replacementTimes.Add(BeforeAnyTick);
        }
        CollectDead(world);
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
        return _active && !_exitMissing ? UpdateRate : SimulationTiming.SleepForever;
    }

    private void CollectDead(IWorldServices world)
    {
        for (var index = 0; index < _spawns.Count;)
        {
            if (world.FindEntity(_spawns[index]) is { DestroyRequested: false, EffectivelyDead: false })
            {
                index++;
                continue;
            }
            _spawns.RemoveAt(index);
            _replacementTimes.Add(world.Tick + data.ReplaceDelayTicks);
        }
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
        var id = exit.ForcesSupplyWanting
            ? world.RequestSpawn(template.Index, Entity.Owner, x, y, z, angle, new SpawnExtras(Fixed.Zero, ForceSupplyWanting: true))
            : world.RequestSpawn(template.Index, Entity.Owner, x, y, z, angle);
        _spawns.Add(id);
        return true;
    }

    public void OnDie(IWorldServices world, in DamageInfo info)
    {
        if (!data.Mux.IsApplicable(Entity, info) || !data.RequireSpawner) return;
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
        if (data.SpawnTemplates.Length > 0 && (_templateCursor < 0 || _templateCursor >= data.SpawnTemplates.Length)) throw new InvalidDataException("Spawn template cursor out of range.");
        _replacementTimes.Clear();
        foreach (var node in reader.Array("replacementTimes")) _replacementTimes.Add(node.Int("tick"));
        _spawns.Clear();
        foreach (var node in reader.Array("spawns")) _spawns.Add(new EntityId(checked((uint)node.UInt("id"))));
    }
}
