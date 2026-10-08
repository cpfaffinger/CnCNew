using CnCNew.Data.Definitions;
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
        DescribeVision(writer);
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
        _turretWaits = state.Has("turretWaits") ? state.Int("turretWaits") : 0;

        foreach (var player in state.Array("players"))
        {
            var id = checked((int)player.Int("id"));
            if (!_players.TryGetValue(id, out var known) || known.Name != player.Text("name"))
                throw new InvalidDataException($"Snapshot player {id} does not match the world configuration.");
            if (player.Int("team") != known.Team) throw new InvalidDataException($"Snapshot player {id} belongs to another team than the world configuration.");
            if (player.Bool("computer") != known.IsComputer) throw new InvalidDataException($"Snapshot player {id} has another player type than the world configuration.");
            known.LastSequence = player.UInt("lastSequence");
            known.Relations.Clear();
            foreach (var relation in player.Array("relations"))
            {
                var relationship = checked((int)relation.Int("relationship"));
                if (!Enum.IsDefined((Relationship)relationship)) throw new InvalidDataException("Unknown relationship in snapshot.");
                known.Relations[checked((int)relation.Int("player"))] = (Relationship)relationship;
            }
            // Money and completed upgrades are independent scalar state (not derived from owned objects) and must round-trip
            // explicitly. EnergyProduction is restored after the entities have re-registered: registration only re-adds the template
            // value, while PowerPlantUpgrade and OverchargeBehavior add bonuses that live in module state.
            known.Money = player.Fixed("money");
            known.CompletedUpgrades = UpgradeMask.Load(player, "completedUpgrades");
            known.CompletedSciences = ScienceMask.Load(player, "completedSciences");
            known.SciencePurchasePoints = checked((int)player.Int("sciencePurchasePoints"));
            known.CashBounty = player.Fixed("cashBounty");
            known.CanBuildBase = !player.Has("canBuildBase") || player.Bool("canBuildBase");
            known.CanBuildUnits = !player.Has("canBuildUnits") || player.Bool("canBuildUnits");
            LoadCostModifiers(player, known);
            known.SharedReadyFrames.Clear();
            foreach (var shared in player.Array("sharedReadyFrames")) known.SharedReadyFrames[checked((int)shared.Int("power"))] = shared.Int("frame");
            known.Tunnels.Load(player);
            known.PowerSabotagedUntil = player.Has("powerSabotagedUntil") ? player.Int("powerSabotagedUntil") : 0;
            known.BattlePlan = player.Has("battlePlan") ? BattlePlanState.Load(player.Object("battlePlan")) : null;
        }
        LoadDelayedDamage(state);
        var rejections = state.Array("rejections");
        if (rejections.Count != LegacyRejectionEntries) throw new InvalidDataException("Rejection table size mismatch.");
        for (var index = 0; index < rejections.Count; index++) _rejections[index] = rejections[index].Int("count");
        for (var index = LegacyRejectionEntries; index < _rejections.Length; index++) _rejections[index] = state.Has("rejection" + index) ? state.Int("rejection" + index) : 0;

        foreach (var pending in state.Array("spawnQueue"))
        {
            var templateIndex = checked((int)pending.Int("template"));
            if (templateIndex < 0 || templateIndex >= _templates.Templates.Length) throw new InvalidDataException("Spawn queue references an unknown template.");
            _spawnQueue.Add(new PendingSpawn(new EntityId(checked((uint)pending.UInt("id"))), templateIndex, new PlayerId(checked((int)pending.Int("owner"))),
                pending.Fixed("x"), pending.Fixed("y"), pending.Fixed("z"), pending.Fixed("angle"),
                new SpawnSetup(pending.Bool("hasHealth"), checked((int)pending.Int("healthPercent")), pending.Bool("hasVeterancy"), checked((int)pending.Int("veterancy")),
                    pending.Int("minLifetime"), pending.Int("maxLifetime"), new EntityId(checked((uint)pending.UInt("container"))),
                    new EntityId(checked((uint)pending.UInt("containInSource"))), pending.Fixed("transferDamage"), new EntityId(checked((uint)pending.UInt("transferSource"))),
                    pending.Fixed("maxHealth"), pending.Bool("hasOrigin"), checked((int)pending.Int("originTemplate")), pending.Bool("forceSupplyWanting"),
                    pending.Bool("hasRally"), pending.Fixed("rallyX"), pending.Fixed("rallyY"),
                    pending.Has("launchWeapon")
                        ? new ProjectileLaunch(checked((int)pending.Int("launchWeapon")), new EntityId(checked((uint)pending.UInt("launcher"))), new EntityId(checked((uint)pending.UInt("launchVictim"))),
                            pending.Fixed("launchX"), pending.Fixed("launchY"), pending.Fixed("launchZ"), checked((uint)pending.UInt("launchConditions")))
                        : null,
                    pending.Has("deliveryMoveX")
                        ? new Modules.BuiltIn.DeliveryOrder(pending.Fixed("deliveryMoveX"), pending.Fixed("deliveryMoveY"), pending.Fixed("deliveryTargetX"), pending.Fixed("deliveryTargetY"), CnCNew.Simulation.Templates.DeliveryRun.Load(pending), pending.Int("deliveryDelay"))
                        : null,
                    pending.Has("creator"), pending.Has("creator") ? new EntityId(checked((uint)pending.UInt("creator"))) : EntityId.Invalid,
                    pending.Has("enslaver") ? new EntityId(checked((uint)pending.UInt("enslaver"))) : default,
                    pending.Has("producer") ? new EntityId(checked((uint)pending.UInt("producer"))) : default,
                    pending.Has("exitX"), pending.Has("exitX") ? pending.Fixed("doorX") : default, pending.Has("exitX") ? pending.Fixed("doorY") : default,
                    pending.Has("exitX") ? pending.Fixed("exitX") : default, pending.Has("exitX") ? pending.Fixed("exitY") : default),
                pending.Has("notBefore") ? pending.Int("notBefore") : 0));
        }
        foreach (var item in state.Array("events")) _events.Enqueue(SimEvent.Load(item));
        LoadExitClaims(state);

        foreach (var item in state.Array("entityList")) LoadEntity(item);
        foreach (var player in state.Array("players")) _players[checked((int)player.Int("id"))].EnergyProduction = checked((int)player.Int("energyProduction"));
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
        LoadVision(state);
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
            Experience = checked((int)reader.Int("experience")),
            ExperienceScalar = reader.Fixed("experienceScalar"),
            WeaponBonusConditions = checked((uint)reader.UInt("weaponBonusConditions")),
            EffectivelyDead = reader.Bool("dead"),
            SlowDeathStarted = reader.Bool("slowDeathStarted"),
            DestroyRequested = reader.Bool("destroyRequested"),
            ContainedBy = new EntityId(checked((uint)reader.UInt("containedBy"))),
            CompletedUpgrades = UpgradeMask.Load(reader, "completedUpgrades"),
            Speed = reader.Fixed("speed"),
            HasMoveTarget = reader.Bool("hasMoveTarget"),
            MoveTargetX = reader.Fixed("moveTargetX"),
            MoveTargetY = reader.Fixed("moveTargetY"),
            WeaponClip = checked((int)reader.Int("weaponClip")),
            WeaponReadyTick = reader.Int("weaponReadyTick"),
            LastDamageTick = reader.Int("lastDamageTick"),
            LastFireTick = reader.Int("lastFireTick"),
            Captured = reader.Has("captured"),
            HasPlayerUpgradeArmor = reader.Bool("hasPlayerUpgradeArmor"),
            HasPlayerUpgradeWeapon = reader.Bool("hasPlayerUpgradeWeapon"),
            ConstructionWork = checked((int)reader.Int("constructionWork")),
            BuilderId = new EntityId(checked((uint)reader.UInt("builder"))),
            SellCompleteTick = reader.Int("sellCompleteTick"),
            EmpDisabledUntil = reader.Has("empDisabledUntil") ? reader.Int("empDisabledUntil") : 0,
            ScriptDisableMask = reader.Has("scriptDisableMask") ? checked((byte)reader.Int("scriptDisableMask")) : (byte)0,
        };
        if (reader.Has("shroudScalar")) entity.ShroudScalar = reader.Fixed("shroudScalar");
        if (reader.Has("crateWeaponLevel")) entity.CrateWeaponLevel = checked((int)reader.Int("crateWeaponLevel"));
        if (reader.Has("crateArmorLevel")) entity.CrateArmorLevel = checked((int)reader.Int("crateArmorLevel"));
        if (entity.CrateWeaponLevel is < 0 or > 2 || entity.CrateArmorLevel is < 0 or > 2) throw new InvalidDataException("Crate upgrade level out of range.");
        if (reader.Has("aheadX")) entity.SetAhead(reader.Int("aheadTick"), reader.Fixed("aheadX"), reader.Fixed("aheadY"), reader.Fixed("aheadZ"));
        if (reader.Has("visionSpied"))
        {
            entity.VisionSpiedBy = [];
            foreach (var item in reader.Array("visionSpied")) entity.VisionSpiedBy[checked((int)item.Int("player"))] = checked((int)item.Int("count"));
        }
        if (entity.VeterancyLevel is < 0 or > 3) throw new InvalidDataException("Veterancy level out of range.");
        // Weapon slot state exists exactly for objects whose weapon sets hold more than one weapon; any other shape is a snapshot of different data.
        if (reader.Has("weaponSlots") != template.HasMultipleWeaponSlots)
            throw new InvalidDataException($"Entity {entity.Id} ('{template.Name}') {(template.HasMultipleWeaponSlots ? "lacks" : "carries")} weapon slot state its template {(template.HasMultipleWeaponSlots ? "needs" : "does not have")}.");
        if (template.HasMultipleWeaponSlots) entity.WeaponSlots = WeaponSlotState.Load(reader.Object("weaponSlots"), template);
        if (entity.WeaponBonusConditions >> NameTablesPhase5.WeaponBonusCondition.Count != 0) throw new InvalidDataException("Weapon bonus condition mask out of range.");
        AttachModules(entity, template);
        LoadTurrets(entity, reader);
        if (reader.Has("firing")) entity.Firing = FiringState.Load(reader.Object("firing"));
        if (reader.Has("acquisition")) entity.Acquisition = AcquisitionState.Load(reader.Object("acquisition"));
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
