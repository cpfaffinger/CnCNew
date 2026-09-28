using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Timers;

namespace CnCNew.Simulation.World;

public sealed partial class SimulationWorld
{
    /// <summary>Writes the complete logic state in canonical order (ids ascending, commands by tick, player, sequence).</summary>
    internal void DescribeFull(IStateWriter writer)
    {
        DescribeGlobal(writer);
        writer.BeginArray("entityList");
        foreach (var entity in _entities.Values.OrderBy(value => value.Id.Value)) entity.Describe(writer);
        writer.EndArray();
        writer.BeginArray("timerList");
        foreach (var timer in _timerById.Values.OrderBy(value => value.Id.Value)) timer.Describe(writer);
        writer.EndArray();
        writer.BeginArray("commandList");
        foreach (var command in _commands.SelectMany(pair => pair.Value.OrderBy(value => value.Player.Value).ThenBy(value => value.Sequence)))
            command.Describe(writer);
        writer.EndArray();
    }

    /// <summary>
    /// Rebuilds the state written by <see cref="DescribeFull"/> into this freshly constructed world. Minimal and internal by
    /// design (phase 9 will own the public save contract): it exists for the continuation run of the replay harness.
    /// </summary>
    internal void LoadFull(IStateReader state)
    {
        if (_entities.Count != 0 || _tick != 0 || _spawnQueue.Count != 0) throw new InvalidOperationException("A snapshot can only be loaded into a fresh world.");
        _tick = state.Int("tick");
        if (_tick < 0) throw new InvalidDataException("Negative tick in snapshot.");
        Ids.Load(state);
        Streams.LoadLogic(state);
        _spawnedTotal = state.Int("spawnedTotal");
        _destroyedTotal = state.Int("destroyedTotal");

        foreach (var player in state.Array("players"))
        {
            var id = checked((int)player.Int("id"));
            if (!_players.TryGetValue(id, out var known) || known.Name != player.Text("name"))
                throw new InvalidDataException($"Snapshot player {id} does not match the world configuration.");
            known.LastSequence = player.UInt("lastSequence");
            // Money and completed upgrades are independent scalar state (not derived from owned objects) and must round-trip
            // explicitly; EnergyProduction is instead rebuilt below as each entity re-registers (Energy::objectEnteringInfluence).
            known.Money = player.Fixed("money");
            known.CompletedUpgrades = UpgradeMask.Load(player, "completedUpgrades");
        }
        var rejections = state.Array("rejections");
        if (rejections.Count != _rejections.Length) throw new InvalidDataException("Rejection table size mismatch.");
        for (var index = 0; index < rejections.Count; index++) _rejections[index] = rejections[index].Int("count");

        foreach (var pending in state.Array("spawnQueue"))
        {
            var templateIndex = checked((int)pending.Int("template"));
            if (templateIndex < 0 || templateIndex >= _templates.Templates.Length) throw new InvalidDataException("Spawn queue references an unknown template.");
            _spawnQueue.Add(new PendingSpawn(new EntityId(checked((uint)pending.UInt("id"))), templateIndex, new PlayerId(checked((int)pending.Int("owner"))),
                pending.Fixed("x"), pending.Fixed("y"), pending.Fixed("z"), pending.Fixed("angle")));
        }
        foreach (var item in state.Array("events")) _events.Enqueue(SimEvent.Load(item));

        foreach (var item in state.Array("entityList")) LoadEntity(item);
        foreach (var item in state.Array("destroyQueue"))
        {
            var entity = FindEntity(new EntityId(checked((uint)item.UInt("id")))) ?? throw new InvalidDataException("Destroy queue references an unknown entity.");
            _destroyQueue.Add(entity);
        }
        foreach (var item in state.Array("timerList"))
        {
            var timer = SimTimer.Load(item);
            if (timer.Owner.IsValid && !_entities.ContainsKey(timer.Owner.Value)) throw new InvalidDataException("Timer owner does not exist.");
            if (_timerById.ContainsKey(timer.Id.Value)) throw new InvalidDataException($"Duplicate timer id {timer.Id.Value}.");
            AddTimer(timer);
        }
        foreach (var item in state.Array("commandList"))
        {
            var command = SimCommand.Load(item);
            if (!_commands.TryGetValue(command.Tick, out var list)) _commands[command.Tick] = list = [];
            list.Add(command);
            _pendingCommandCount++;
        }
        if (state.Int("pendingCommands") != _pendingCommandCount || state.Int("entities") != _entities.Count || state.Int("timers") != _timerById.Count)
            throw new InvalidDataException("Snapshot table sizes do not match their contents.");
        ComputeFullHash();
    }

    private void LoadEntity(IStateReader reader)
    {
        var templateIndex = checked((int)reader.Int("template"));
        if (templateIndex < 0 || templateIndex >= _templates.Templates.Length) throw new InvalidDataException("Entity references an unknown template.");
        var template = _templates[templateIndex];
        if (!template.Name.Equals(reader.Text("templateName"), StringComparison.Ordinal))
            throw new InvalidDataException($"Entity template {templateIndex} is '{template.Name}' but the snapshot says '{reader.Text("templateName")}'.");
        var entity = new Entity(new EntityId(checked((uint)reader.UInt("id"))), template, new PlayerId(checked((int)reader.Int("owner"))), reader.Int("spawnTick"))
        {
            X = reader.Fixed("x"),
            Y = reader.Fixed("y"),
            Z = reader.Fixed("z"),
            Angle = reader.Fixed("angle"),
            Status = reader.UInt("status"),
            VeterancyLevel = checked((int)reader.Int("veterancy")),
            EffectivelyDead = reader.Bool("dead"),
            DestroyRequested = reader.Bool("destroyRequested"),
            CompletedUpgrades = UpgradeMask.Load(reader, "completedUpgrades"),
            Speed = reader.Fixed("speed"),
            HasMoveTarget = reader.Bool("hasMoveTarget"),
            MoveTargetX = reader.Fixed("moveTargetX"),
            MoveTargetY = reader.Fixed("moveTargetY"),
            WeaponClip = checked((int)reader.Int("weaponClip")),
            WeaponReadyTick = reader.Int("weaponReadyTick"),
            LastDamageTick = reader.Int("lastDamageTick"),
            LastFireTick = reader.Int("lastFireTick"),
            HasPlayerUpgradeArmor = reader.Bool("hasPlayerUpgradeArmor"),
            HasPlayerUpgradeWeapon = reader.Bool("hasPlayerUpgradeWeapon"),
        };
        if (entity.VeterancyLevel is < 0 or > 3) throw new InvalidDataException("Veterancy level out of range.");
        AttachModules(entity, template);
        var modules = reader.Array("modules");
        if (modules.Count != entity.ModuleArray.Length) throw new InvalidDataException($"Entity {entity.Id} has {modules.Count} module states but its template has {entity.ModuleArray.Length}.");
        for (var index = 0; index < modules.Count; index++)
        {
            var module = entity.ModuleArray[index];
            if (modules[index].Int("index") != index || modules[index].Text("name") != module.ModuleName || modules[index].Text("tag") != module.Tag)
                throw new InvalidDataException($"Entity {entity.Id}: module {index} does not match its template.");
            module.Load(modules[index].Object("state"));
        }
        if (_entities.ContainsKey(entity.Id.Value)) throw new InvalidDataException($"Duplicate entity id {entity.Id}.");
        Register(entity);
    }
}
