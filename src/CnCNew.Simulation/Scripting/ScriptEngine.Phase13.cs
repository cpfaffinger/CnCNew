using CnCNew.Simulation.Modules;
using System.Collections.Immutable;
using CnCNew.Data.World;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Scripting;

/// <summary>
/// More actions of the original script engine that the Phase-12 VM lacked (<c>docs/SCRIPT-ENGINE.md</c> §5, Phase 13): topple directions and buildability overrides (tables the world
/// consults through hooks, like the relationship overrides), the skirmish approach paths, the AI's supply-centre expansion, unmanned-vehicle capture, resuming supply trucks, and
/// the single-unit "move towards the nearest object type". Every function cites its reference in <c>ScriptActions.cpp</c>/<c>AIPlayer.cpp</c>; the state of every table is written only when set.
/// </summary>
public sealed partial class ScriptEngine
{
    private const decimal SupplyCenterCloseDistance = 20m * TerrainMap.CellSize;
    private static readonly string[] BuildableNames = ["Yes", "Ignore_Prerequisites", "No", "Only_By_AI"];

    private readonly SortedDictionary<string, (long X, long Y)> _toppleDirections = new(StringComparer.Ordinal);
    private readonly SortedDictionary<string, int> _buildableOverrides = new(StringComparer.Ordinal);
    private readonly SortedDictionary<uint, uint> _captureOrders = [];
    private readonly SortedDictionary<string, (int Owner, long X, long Y)> _pendingStructures = new(StringComparer.Ordinal);

    private bool TryExecutePhase13Action(MapScriptAction action)
    {
        var p = action.Parameters;
        var type = action.TypeName;
        switch (type)
        {
            // doSetToppleDirection -> ScriptEngine::setToppleDirection: a per-object-name table read by the topple modules (adjustToppleDirection).
            case "NAMED_SET_TOPPLE_DIRECTION":
                if (Has(type, p, 2) && Str(p, 0).Length > 0)
                {
                    _toppleDirections[Str(p, 0)] = (Fixed.FromDecimal(p[1].CoordX).Raw, Fixed.FromDecimal(p[1].CoordY).Raw);
                    InstallToppleHook();
                }
                return true;
            // doModifyBuildableStatus -> GameLogic::setBuildableStatusOverride (BuildableStatus: Yes, Ignore_Prerequisites, No, Only_By_AI).
            case "TECHTREE_MODIFY_BUILDABILITY_OBJECT":
                if (Has(type, p, 2))
                {
                    if (!_world.Templates.TryGet(Str(p, 0), out var buildable)) Diagnose($"unknown template '{Str(p, 0)}'");
                    else if (Int(p, 1) is >= 0 and < 4) { _buildableOverrides[buildable.Name] = Int(p, 1); InstallBuildableHook(); }
                    else Diagnose($"buildable status {Int(p, 1)} out of range");
                }
                return true;
            // doAffectPlayerSkillset -> Player::friend_setSkillset: picks the AI's general skill set; the simulation's AI has none (recorded).
            case "PLAYER_SELECT_SKILLSET":
                if (Has(type, p, 2) && ResolvePlayer(Str(p, 0)) is { } skilled) _recordedSettings[type + "|" + skilled.Id.Value] = Int(p, 1) - 1;
                _recordedActions++;
                return true;
            // doResumeSupplyTruckingForIdleUnits(empty): Player::setUnitsShouldIdleOrResume(false) for every human player: idle supply trucks are forced back to work.
            case "RESUME_SUPPLY_TRUCKING":
                foreach (var player in _program.Players.Where(player => player.IsHuman && !player.Id.IsSystem)) ResumeSupplyTrucking(player.Id);
                return true;
            // doLocalDefeat: the local player's defeat window and CampaignManager::SetVictorious(false) — a defeat of the mission.
            case "LOCALDEFEAT":
                End(MissionOutcome.Defeat, EndGameDelayTicks);
                return true;
            // doMoveUnitTowardsNearest: aiMoveToObject towards the closest object of the type inside the area, measured from the unit.
            case "UNIT_MOVE_TOWARDS_NEAREST_OBJECT_TYPE":
                if (Has(type, p, 3) && NamedEntity(Str(p, 0)) is { } mover && mover.Template.Locomotor is not null)
                {
                    if (_program.FindArea(Str(p, 2)) is not { } area) Diagnose($"unknown trigger area '{Str(p, 2)}'");
                    else if (_world.Entities.Where(candidate => CountsAsObject(candidate) && candidate.Id != mover.Id && TypeMatches(Str(p, 1), candidate) && area.Contains(candidate.X, candidate.Y))
                                 .OrderBy(candidate => Distance2(candidate, mover.X, mover.Y)).ThenBy(candidate => candidate.Id.Value).FirstOrDefault() is { } destination)
                        MoveTo(mover, destination.X, destination.Y);
                }
                return true;
            // doTeamCaptureNearestUnownedFactionUnit: the group enters the closest unmanned object (PartitionFilterUnmannedObject(true)); entering one hands it to the entering side.
            case "TEAM_CAPTURE_NEAREST_UNOWNED_FACTION_UNIT":
                if (Has(type, p, 1))
                    foreach (var team in TeamsNamed(Str(p, 0))) CaptureNearestUnmanned(team);
                return true;
            // doTeamMoveToSkirmishApproachPath / doTeamFollowSkirmishApproachPath
            case "SKIRMISH_MOVE_TO_APPROACH_PATH":
                if (Has(type, p, 2)) foreach (var team in TeamsNamed(Str(p, 0))) ApproachPath(team, Str(p, 1), follow: false);
                return true;
            case "SKIRMISH_FOLLOW_APPROACH_PATH":
                if (Has(type, p, 3)) foreach (var team in TeamsNamed(Str(p, 0))) ApproachPath(team, Str(p, 1), follow: true);
                return true;
            // doSetTeamState: Team::setState, the string TEAM_STATE_IS reads.
            case "TEAM_SET_STATE":
                if (Has(type, p, 2)) foreach (var team in TeamsNamed(Str(p, 0))) _teamStates[team.Id] = Str(p, 1);
                return true;
            // doGuardSupplyCenter -> AIPlayer::guardSupplyCenter
            case "TEAM_GUARD_SUPPLY_CENTER":
                if (Has(type, p, 2))
                    foreach (var team in TeamsNamed(Str(p, 0))) GuardSupplyCenter(team, Int(p, 1));
                return true;
            // doBuildSupplyCenter -> AIPlayer::buildBySupplies
            case "AI_PLAYER_BUILD_SUPPLY_CENTER":
                if (Has(type, p, 3) && ResolvePlayer(Str(p, 0)) is { } builder) BuildBySupplies(builder.Id, Str(p, 1), Int(p, 2));
                return true;
            default:
                return false;
        }
    }

    // ------------------------------------------------------------------------------------------------ tables the world consults

    private void InstallToppleHook() => _world.ToppleDirectionOverride = ToppleDirectionOf;

    private void InstallBuildableHook() => _world.BuildableOverride = template => _buildableOverrides.TryGetValue(template.Name, out var status) ? BuildableNames[status] : null;

    private (Fixed X, Fixed Y)? ToppleDirectionOf(Entity entity)
    {
        if (_toppleDirections.Count == 0) return null;
        foreach (var (name, id) in _named)
        {
            if (id != entity.Id.Value || !_toppleDirections.TryGetValue(name, out var direction)) continue;
            var x = Fixed.FromRaw(direction.X);
            var y = Fixed.FromRaw(direction.Y);
            var length = Fixed.Sqrt(x * x + y * y);
            return length == Fixed.Zero ? (Fixed.Zero, Fixed.Zero) : (x / length, y / length);
        }
        return null;
    }

    // ------------------------------------------------------------------------------------------------ supply trucks, unmanned objects

    private void ResumeSupplyTrucking(PlayerId player)
    {
        foreach (var entity in _world.Entities.Where(entity => entity.Owner == player && !entity.EffectivelyDead && !entity.DestroyRequested && !entity.Template.IsKindOf("STRUCTURE")).OrderBy(entity => entity.Id.Value).ToArray())
            if (entity.Modules.OfType<SupplyCarrierAI>().FirstOrDefault() is { } truck && IsIdle(entity)) truck.ForceWanting(_world);
    }

    private void CaptureNearestUnmanned(ScriptTeam team)
    {
        var members = LiveMembers(team).Where(member => member.Template.Locomotor is not null).ToArray();
        if (members.Length == 0) return;
        long sumX = 0, sumY = 0;
        foreach (var member in members) { sumX += member.X.Floor(); sumY += member.Y.Floor(); }
        var centreX = Fixed.FromInt(sumX / members.Length);
        var centreY = Fixed.FromInt(sumY / members.Length);
        var target = _unmanned.Select(id => _world.FindEntity(new EntityId(id))).Where(candidate => candidate is { EffectivelyDead: false, DestroyRequested: false }).Select(candidate => candidate!)
            .OrderBy(candidate => Distance2(candidate, centreX, centreY)).ThenBy(candidate => candidate.Id.Value).FirstOrDefault();
        if (target is null) return;
        foreach (var member in members) { _captureOrders[member.Id.Value] = target.Id.Value; MoveTo(member, target.X, target.Y); }
    }

    /// <summary>Walks the capture orders: a member that reaches its unmanned target takes it over (the object leaves the unmanned set and joins the member's side).</summary>
    private void UpdateCaptureOrders(long tick)
    {
        if (_captureOrders.Count == 0) return;
        var reach = Fixed.FromInt(25);
        foreach (var (memberId, targetId) in _captureOrders.ToArray())
        {
            var member = _world.FindEntity(new EntityId(memberId));
            var target = _world.FindEntity(new EntityId(targetId));
            if (member is null || member.EffectivelyDead || member.DestroyRequested || target is null || target.EffectivelyDead || target.DestroyRequested || !_unmanned.Contains(targetId))
            {
                _captureOrders.Remove(memberId);
                continue;
            }
            var dx = member.X - target.X;
            var dy = member.Y - target.Y;
            if (dx * dx + dy * dy <= reach * reach)
            {
                if (_world.TransferOwnership(target.Id, member.Owner)) _unmanned.Remove(targetId);
                foreach (var key in _captureOrders.Where(pair => pair.Value == targetId).Select(pair => pair.Key).ToArray()) _captureOrders.Remove(key);
            }
            else if (!member.HasMoveTarget && tick % 30 == 0) MoveTo(member, target.X, target.Y);
        }
    }

    // ------------------------------------------------------------------------------------------------ skirmish approach paths

    /// <summary>
    /// <c>ScriptEngine::getSkirmishEnemyPlayer</c>: the AI's current enemy, else the first human player (the simulation's script AI has no "current enemy", so always the human viewer,
    /// the reference's fallback).
    /// </summary>
    private ScriptPlayer? SkirmishEnemy() => _program.Players.FirstOrDefault(player => player.Id == _options.LocalPlayer) ?? _program.Players.FirstOrDefault(player => player.IsHuman);

    /// <summary>
    /// <c>doTeamMoveToSkirmishApproachPath</c> / <c>doTeamFollowSkirmishApproachPath</c>: the path is <c>label + (enemy start index + 1)</c>; the group goes to (or follows from) the waypoint of
    /// that path closest to the team's centre. <c>asTeam</c> (<c>groupFollowWaypointPathAsTeam</c>: the group keeps formation) is not modelled: members follow the path on their own.
    /// </summary>
    private void ApproachPath(ScriptTeam team, string label, bool follow)
    {
        var members = LiveMembers(team).Where(member => member.Template.Locomotor is not null).ToArray();
        if (members.Length == 0) return;
        if (SkirmishEnemy() is not { } enemy) return;
        var path = _program.FindPath(label + (enemy.StartIndex + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (path is not { Waypoints.IsDefaultOrEmpty: false }) { Diagnose($"no approach path '{label}{enemy.StartIndex + 1}'"); return; }
        long sumX = 0, sumY = 0;
        foreach (var member in members) { sumX += member.X.Floor(); sumY += member.Y.Floor(); }
        var centreX = Fixed.FromInt(sumX / members.Length);
        var centreY = Fixed.FromInt(sumY / members.Length);
        var closest = 0;
        var best = Fixed.MaxValue;
        for (var index = 0; index < path.Waypoints.Length; index++)
        {
            var dx = path.Waypoints[index].X - centreX;
            var dy = path.Waypoints[index].Y - centreY;
            var distance = dx * dx + dy * dy;
            if (distance >= best) continue;
            best = distance;
            closest = index;
        }
        foreach (var member in members)
        {
            if (!follow) { _followers.Remove(member.Id.Value); MoveTo(member, path.Waypoints[closest].X, path.Waypoints[closest].Y); continue; }
            _buttonHunts.Remove(member.Id.Value);
            _followers[member.Id.Value] = new Follower { Entity = member.Id.Value, Path = PathMode + path.Label, Index = closest };
            _completedPath.Remove(member.Id.Value);
        }
    }

    // ------------------------------------------------------------------------------------------------ AI supply centres

    private (Fixed X, Fixed Y) BaseCentre(PlayerId player)
    {
        long sumX = 0, sumY = 0, count = 0;
        foreach (var entity in _world.Entities.Where(entity => entity.Owner == player && !entity.EffectivelyDead && entity.Template.IsKindOf("STRUCTURE") && CountsAsObject(entity)))
        {
            sumX += entity.X.Floor();
            sumY += entity.Y.Floor();
            count++;
        }
        return count == 0 ? (Fixed.Zero, Fixed.Zero) : (Fixed.FromInt(sumX / count), Fixed.FromInt(sumY / count));
    }

    /// <summary><c>AIPlayer::getPlayerStructureBounds</c> centre: the middle of the box around the structures of the player (<see langword="null"/> without structures).</summary>
    private (Fixed X, Fixed Y)? StructureBoundsCentre(PlayerId player)
    {
        Fixed minX = Fixed.MaxValue, minY = Fixed.MaxValue, maxX = Fixed.MinValue, maxY = Fixed.MinValue;
        var any = false;
        foreach (var entity in _world.Entities.Where(entity => entity.Owner == player && !entity.EffectivelyDead && entity.Template.IsKindOf("STRUCTURE") && CountsAsObject(entity)))
        {
            any = true;
            minX = Fixed.Min(minX, entity.X); maxX = Fixed.Max(maxX, entity.X);
            minY = Fixed.Min(minY, entity.Y); maxY = Fixed.Max(maxY, entity.Y);
        }
        return any ? ((minX + maxX) / Fixed.FromInt(2), (minY + maxY) / Fixed.FromInt(2)) : null;
    }

    /// <summary>
    /// <c>AIPlayer::findSupplyCenter</c>: the warehouse with at least <paramref name="minimumCash"/> of supplies (halving the demand down to 100) that is not an enemy's, has no own cash generator
    /// next to it, and is not much nearer the enemy's base than ours; the nearest to our base wins.
    /// </summary>
    private Entity? FindSupplyCenter(PlayerId player, int minimumCash)
    {
        var baseCentre = BaseCentre(player);
        var enemy = SkirmishEnemy();
        var enemyCentre = enemy is not null && enemy.Id != player ? StructureBoundsCentre(enemy.Id) : null;
        var valuePerBox = _world.ValuePerSupplyBox;
        var closeDistance = Fixed.FromDecimal(SupplyCenterCloseDistance);
        do
        {
            Entity? best = null;
            var bestDistance = Fixed.MaxValue;
            foreach (var warehouse in _world.Entities.Where(entity => entity.Template.IsKindOf("STRUCTURE") && entity.Template.IsKindOf("SUPPLY_SOURCE") && !entity.EffectivelyDead && !entity.DestroyRequested).OrderBy(entity => entity.Id.Value))
            {
                if (warehouse.Modules.OfType<SupplyWarehouseDockUpdate>().FirstOrDefault() is not { } dock) continue;
                if (dock.Boxes * valuePerBox < minimumCash) continue;
                if (!warehouse.Owner.IsSystem && _world.AreEnemies(player, warehouse.Owner)) continue;
                var radius = closeDistance + warehouse.Template.BoundingCircleRadius;
                if (_world.Entities.Any(entity => entity.Owner == player && !entity.EffectivelyDead && entity.Template.IsKindOf("CASH_GENERATOR") && Distance2(entity, warehouse.X, warehouse.Y) <= radius * radius)) continue;
                var distance = Distance2(warehouse, baseCentre.X, baseCentre.Y);
                if (enemyCentre is { } centre && distance * Fixed.FromDecimal(0.4m) > Distance2(warehouse, centre.X, centre.Y) * Fixed.FromDecimal(0.6m)) continue;
                if (best is null || bestDistance > distance) { best = warehouse; bestDistance = distance; }
            }
            if (best is not null) return best;
            minimumCash /= 2;
        } while (minimumCash > 100);
        return null;
    }

    /// <summary><c>AIPlayer::guardSupplyCenter</c>: the team guards (walks to, then stands at) the warehouse, on the side facing the enemy's structures.</summary>
    private void GuardSupplyCenter(ScriptTeam team, int minSupplies)
    {
        var owner = team.Owner;
        var warehouse = FindSupplyCenter(owner, minSupplies);
        if (warehouse is null) return;
        var location = (X: warehouse.X, Y: warehouse.Y);
        var enemy = SkirmishEnemy();
        if (enemy is not null && enemy.Id != owner && StructureBoundsCentre(enemy.Id) is { } centre)
        {
            var dx = warehouse.X - centre.X;
            var dy = warehouse.Y - centre.Y;
            var length = Fixed.Sqrt(dx * dx + dy * dy);
            if (length > Fixed.Zero)
            {
                var radius = warehouse.Template.BoundingCircleRadius * Fixed.FromDecimal(0.8m);
                location = (warehouse.X - dx / length * radius, warehouse.Y - dy / length * radius);
            }
        }
        foreach (var member in LiveMembers(team).ToArray())
        {
            _followers.Remove(member.Id.Value);
            MoveTo(member, location.X, location.Y);
        }
    }

    /// <summary>
    /// <c>AIPlayer::buildBySupplies</c>: a warehouse is picked (<see cref="FindSupplyCenter"/>), the structure goes next to it — towards the base for a cash generator, towards the enemy for
    /// anything else — and a legal spot is searched in growing rings; a dozer of the player builds it. Without funds the order waits (<see cref="_pendingStructures"/>) like the reference's
    /// build queue.
    /// </summary>
    private void BuildBySupplies(PlayerId player, string templateName, int minimumCash)
    {
        if (!_world.Templates.TryGet(templateName, out var template) || !template.IsInstantiable) { Diagnose($"unknown template '{templateName}'"); return; }
        var warehouse = FindSupplyCenter(player, minimumCash);
        if (warehouse is null) return;
        var cashGenerator = template.IsKindOf("CASH_GENERATOR");
        var baseCentre = BaseCentre(player);
        var dx = warehouse.X - baseCentre.X;
        var dy = warehouse.Y - baseCentre.Y;
        var radius = Fixed.FromDecimal(3m * TerrainMap.CellSize);
        if (!cashGenerator)
        {
            radius = warehouse.Template.BoundingCircleRadius;
            var enemy = SkirmishEnemy();
            if (enemy is not null && enemy.Id != player && StructureBoundsCentre(enemy.Id) is { } centre) { dx = warehouse.X - centre.X; dy = warehouse.Y - centre.Y; }
        }
        var length = Fixed.Sqrt(dx * dx + dy * dy);
        var x = length > Fixed.Zero ? warehouse.X - dx / length * radius : warehouse.X;
        var y = length > Fixed.Zero ? warehouse.Y - dy / length * radius : warehouse.Y;
        var builder = _world.Entities.Where(entity => entity.Owner == player && !entity.EffectivelyDead && !entity.DestroyRequested && entity.Template.Locomotor is not null && !entity.Template.IsKindOf("STRUCTURE")).OrderBy(entity => entity.Id.Value)
            .FirstOrDefault(entity => _world.CheckConstruct(player, new ConstructCommand(player, 0, 0, entity.Id, template.Name, x, y, Fixed.Zero)) is not (CommandRejection.CannotConstruct or CommandRejection.UnknownEntity or CommandRejection.NotPermitted));
        if (builder is null) { Diagnose($"no builder of player {player} for '{templateName}'"); return; }
        var cell = Fixed.FromDecimal(TerrainMap.CellSize);
        var wanted = (X: x, Y: y);
        var rejection = CommandRejection.InvalidPlacement;
        var found = false;
        for (var ring = 0; ring * 2 <= (int)(2m * SupplyCenterCloseDistance / TerrainMap.CellSize) && !found; ring++)
        {
            var offset = Fixed.FromInt(ring) * cell;
            for (var step = -ring; step <= ring && !found; step++)
                foreach (var (candidateX, candidateY) in new[] { (x + Fixed.FromInt(step) * cell, y - offset), (x + Fixed.FromInt(step) * cell, y + offset), (x - offset, y + Fixed.FromInt(step) * cell), (x + offset, y + Fixed.FromInt(step) * cell) })
                {
                    rejection = _world.CheckConstruct(player, new ConstructCommand(player, 0, 0, builder.Id, template.Name, candidateX, candidateY, Fixed.Zero));
                    if (rejection is 0 or CommandRejection.InsufficientFunds) { wanted = (candidateX, candidateY); found = true; break; }
                }
        }
        if (!found) { Diagnose($"no legal place for '{templateName}' near the warehouse"); return; }
        if (rejection == CommandRejection.InsufficientFunds) { _pendingStructures[player.Value + "|" + template.Name] = (player.Value, wanted.X.Raw, wanted.Y.Raw); return; }
        var spot = wanted;
        var id = builder.Id;
        _issue(player, (sequence, tick) => new ConstructCommand(player, sequence, tick, id, template.Name, spot.X, spot.Y, Fixed.Zero));
    }

    /// <summary>Retries the structures that waited for money, at most once a second.</summary>
    private void UpdatePendingStructures(long tick)
    {
        if (_pendingStructures.Count == 0 || tick % SimulationTiming.LogicFramesPerSecond != 0) return;
        foreach (var (key, order) in _pendingStructures.ToArray())
        {
            var player = new PlayerId(order.Owner);
            var templateName = key[(key.IndexOf('|') + 1)..];
            if (!_world.Templates.TryGet(templateName, out var template)) { _pendingStructures.Remove(key); continue; }
            var builder = _world.Entities.Where(entity => entity.Owner == player && !entity.EffectivelyDead && !entity.DestroyRequested && entity.Template.Locomotor is not null && !entity.Template.IsKindOf("STRUCTURE")).OrderBy(entity => entity.Id.Value)
                .FirstOrDefault(entity => _world.CheckConstruct(player, new ConstructCommand(player, 0, 0, entity.Id, template.Name, Fixed.FromRaw(order.X), Fixed.FromRaw(order.Y), Fixed.Zero)) == 0);
            if (builder is null) continue;
            var id = builder.Id;
            var x = Fixed.FromRaw(order.X);
            var y = Fixed.FromRaw(order.Y);
            _issue(player, (sequence, at) => new ConstructCommand(player, sequence, at, id, template.Name, x, y, Fixed.Zero));
            _pendingStructures.Remove(key);
        }
    }

    // ------------------------------------------------------------------------------------------------ per tick and state

    private void UpdatePhase13(long tick)
    {
        UpdateCaptureOrders(tick);
        UpdatePendingStructures(tick);
    }

    private void DescribePhase13State(IStateWriter writer)
    {
        DescribeConditionState(writer);
        if (_toppleDirections.Count > 0)
        {
            writer.BeginArray("toppleDirections");
            foreach (var (name, direction) in _toppleDirections) { writer.BeginObject(null); writer.Text("name", name); writer.Int("x", direction.X); writer.Int("y", direction.Y); writer.EndObject(); }
            writer.EndArray();
        }
        if (_buildableOverrides.Count > 0)
        {
            writer.BeginArray("buildableOverrides");
            foreach (var (name, status) in _buildableOverrides) { writer.BeginObject(null); writer.Text("name", name); writer.Int("status", status); writer.EndObject(); }
            writer.EndArray();
        }
        if (_captureOrders.Count > 0)
        {
            writer.BeginArray("captureOrders");
            foreach (var (member, target) in _captureOrders) { writer.BeginObject(null); writer.UInt("member", member); writer.UInt("target", target); writer.EndObject(); }
            writer.EndArray();
        }
        if (_pendingStructures.Count > 0)
        {
            writer.BeginArray("pendingStructures");
            foreach (var (key, order) in _pendingStructures) { writer.BeginObject(null); writer.Text("key", key); writer.Int("owner", order.Owner); writer.Int("x", order.X); writer.Int("y", order.Y); writer.EndObject(); }
            writer.EndArray();
        }
    }

    private void LoadPhase13State(IStateReader reader)
    {
        LoadConditionState(reader);
        _toppleDirections.Clear();
        if (reader.Has("toppleDirections")) foreach (var item in reader.Array("toppleDirections")) _toppleDirections[item.Text("name")] = (item.Int("x"), item.Int("y"));
        if (_toppleDirections.Count > 0) InstallToppleHook();
        _buildableOverrides.Clear();
        if (reader.Has("buildableOverrides")) foreach (var item in reader.Array("buildableOverrides")) _buildableOverrides[item.Text("name")] = checked((int)item.Int("status"));
        if (_buildableOverrides.Count > 0) InstallBuildableHook();
        _captureOrders.Clear();
        if (reader.Has("captureOrders")) foreach (var item in reader.Array("captureOrders")) _captureOrders[checked((uint)item.UInt("member"))] = checked((uint)item.UInt("target"));
        _pendingStructures.Clear();
        if (reader.Has("pendingStructures")) foreach (var item in reader.Array("pendingStructures")) _pendingStructures[item.Text("key")] = (checked((int)item.Int("owner")), item.Int("x"), item.Int("y"));
    }
}

// ====================================================================================================================================== conditions

public sealed partial class ScriptEngine
{
    private const long SupplyScanFrames = 10 * SimulationTiming.LogicFramesPerSecond;
    private const decimal SupplyCenterSafeRadius = 250m;

    private readonly SortedDictionary<int, string> _teamStates = [];
    private readonly List<(int Player, string Name, uint Source)> _completedPowers = [];
    private readonly List<(int Player, string Name, uint Source)> _completedUpgrades = [];
    private readonly SortedDictionary<uint, (long Frame, int Count)> _transportStatus = [];
    private readonly SortedDictionary<string, bool> _techBuildingAnswers = new(StringComparer.Ordinal);

    /// <summary>Reads the Phase-13 notification events (completed powers and upgrades).</summary>
    private void ObservePhase13(CnCNew.Simulation.Events.SimEvent item)
    {
        switch (item.Kind)
        {
            case CnCNew.Simulation.Events.SimEventKind.SpecialPowerCompleted:
                // SpecialPowerCompletionDie::notifyScriptEngine -> notifyOfCompletedSpecialPower(owner, power, creator)
                _completedPowers.Add((checked((int)item.Value), item.Text, item.Other.Value));
                break;
            case CnCNew.Simulation.Events.SimEventKind.UpgradeCompleted:
                // ProductionUpdate (PRODUCTION_UPGRADE) -> notifyOfCompletedUpgrade(owner, upgrade, producer): the simulation raises the event for the player or the object.
                _completedUpgrades.Add((item.Player.Value, item.Text, item.Entity.Value));
                break;
        }
    }

    private bool TryEvaluatePhase13Condition(MapCondition condition, out bool result)
    {
        var p = condition.Parameters;
        var type = condition.TypeName;
        result = false;
        switch (type)
        {
            // evaluateTeamStateIs / IsNot: the state is the string TEAM_SET_STATE gave the team ("" before); an unknown team is in no state.
            case "TEAM_STATE_IS":
            case "TEAM_STATE_IS_NOT":
                result = Has(type, p, 2) && FindTeam(Str(p, 0)) is { } stated && (_teamStates.GetValueOrDefault(stated.Id, string.Empty) == Str(p, 1)) == (type == "TEAM_STATE_IS");
                return true;
            // evaluateNamedHasFreeContainerSlots: ContainCount < ContainMax.
            case "NAMED_HAS_FREE_CONTAINER_SLOTS":
                result = Has(type, p, 1) && NamedEntity(Str(p, 0)) is { } slotted && slotted.Modules.OfType<IContainerModule>().FirstOrDefault() is { } container && (container.ContainMax < 0 || container.ContainCount < container.ContainMax);
                return true;
            // evaluateUnitHasEmptied: true on the frame after the container went from occupied to empty (the first look only records the count).
            case "UNIT_EMPTIED":
                result = Has(type, p, 1) && NamedEntity(Str(p, 0)) is { } emptier && UnitHasEmptied(emptier);
                return true;
            // evaluatePlayerHasComparisonPercentPower: the supply ratio (production / consumption, the production itself without consumers) against percent / 100.
            case "PLAYER_POWER_COMPARE_PERCENT":
                if (Has(type, p, 3) && ResolvePlayer(Str(p, 0)) is { } powered)
                {
                    var (production, consumption) = EnergyOf(powered.Id);
                    // ratio * 100 against the integer percent, exact in integers; without consumers the ratio is the production itself.
                    result = consumption == 0 ? Compare(Int(p, 1), production * 100L, Int(p, 2)) : Compare(Int(p, 1), production * 100L, (long)Int(p, 2) * consumption);
                }
                return true;
            // evaluatePlayerHasComparisonValueExcessPower: production - consumption against the kilowatts.
            case "PLAYER_EXCESS_POWER_COMPARE_VALUE":
                result = Has(type, p, 3) && ResolvePlayer(Str(p, 0)) is { } excess && Compare(Int(p, 1), _world.EnergyProductionOf(excess.Id), Int(p, 2));
                return true;
            // evaluateSkirmishUnownedFactionUnitComparison: the neutral player's unmanned objects.
            case "SKIRMISH_UNOWNED_FACTION_UNIT_EXISTS":
                result = Has(type, p, 3) && Compare(Int(p, 1), _unmanned.Count(id => _world.FindEntity(new EntityId(id)) is { EffectivelyDead: false, DestroyRequested: false }), Int(p, 2));
                return true;
            // evaluateSkirmishPlayerTechBuildingWithinDistancePerimeter: a tech building of somebody else (not the player, not an ally) within the area's radius + distance of its centre; cached for good.
            case "SKIRMISH_TECH_BUILDING_WITHIN_DISTANCE":
                result = Has(type, p, 3) && TechBuildingNear(Str(p, 0), Int(p, 1), Str(p, 2));
                return true;
            // evaluateSkirmishSupplySourceSafe (AIPlayer::isSupplySourceSafe/isLocationSafe); the reference re-evaluates every two seconds only, this version every time.
            case "SUPPLY_SOURCE_SAFE":
                result = Has(type, p, 2) && ResolvePlayer(Str(p, 0)) is { } safe && SupplySourceSafe(safe.Id, Int(p, 1));
                return true;
            // evaluateSkirmishSupplySourceAttacked -> AIPlayer::isSupplySourceAttacked: a cash generator, dozer or harvester of the player was damaged within the last 10 seconds.
            case "SUPPLY_SOURCE_ATTACKED":
                result = Has(type, p, 1) && ResolvePlayer(Str(p, 0)) is { } attacked && SupplySourceAttacked(attacked.Id);
                return true;
            // evaluateUpgradeFromUnitComplete / evaluatePlayerSpecialPowerFromUnitComplete: consuming lookups (ScriptEngine::isUpgradeComplete / isSpecialPowerComplete).
            case "PLAYER_BUILT_UPGRADE":
            case "PLAYER_BUILT_UPGRADE_FROM_NAMED":
                result = ConsumeNotification(_completedUpgrades, type, p, type.EndsWith("NAMED", StringComparison.Ordinal));
                return true;
            case "PLAYER_COMPLETED_SPECIAL_POWER":
            case "PLAYER_COMPLETED_SPECIAL_POWER_FROM_NAMED":
                result = ConsumeNotification(_completedPowers, type, p, type.EndsWith("NAMED", StringComparison.Ordinal));
                return true;
            // evaluatePlayerSpecialPowerFromUnitMidway: the original has no caller of notifyOfMidwaySpecialPower, so its list stays empty and the condition never holds.
            case "PLAYER_MIDWAY_SPECIAL_POWER":
            case "PLAYER_MIDWAY_SPECIAL_POWER_FROM_NAMED":
                return true;
            default:
                return false;
        }
    }

    private bool ConsumeNotification(List<(int Player, string Name, uint Source)> list, string type, ImmutableArray<MapScriptParameter> p, bool fromNamed)
    {
        if (!Has(type, p, fromNamed ? 3 : 2)) return false;
        var source = EntityId.Invalid;
        if (fromNamed)
        {
            if (NamedEntity(Str(p, 2)) is not { } named) return false;
            source = named.Id;
        }
        if (ResolvePlayer(Str(p, 0)) is not { } player) return false;
        var index = list.FindIndex(entry => entry.Player == player.Id.Value && entry.Name == Str(p, 1) && (!source.IsValid || entry.Source == source.Value));
        if (index < 0) return false;
        list.RemoveAt(index);
        return true;
    }

    private bool UnitHasEmptied(Entity entity)
    {
        var count = entity.Modules.OfType<IContainerModule>().FirstOrDefault()?.ContainCount ?? 0;
        var frame = _world.Tick;
        if (!_transportStatus.TryGetValue(entity.Id.Value, out var status))
        {
            _transportStatus[entity.Id.Value] = (frame, count);
            return false;
        }
        if (status.Frame == frame - 1 && status.Count > 0 && count == 0) return true;
        _transportStatus[entity.Id.Value] = (frame, count);
        return false;
    }

    /// <summary><c>Energy::getProduction</c>/<c>getConsumption</c>: the simulation keeps the net sum, so consumption is rebuilt from the objects' negative <c>EnergyProduction</c>.</summary>
    private (long Production, long Consumption) EnergyOf(PlayerId player)
    {
        long consumption = 0;
        foreach (var entity in _world.Entities)
            if (entity.Owner == player && !entity.EffectivelyDead && !entity.HasStatus(CnCNew.Simulation.Modules.ObjectStatusBits.UnderConstruction) && entity.Template.EnergyProduction < 0)
                consumption -= entity.Template.EnergyProduction;
        return (_world.EnergyProductionOf(player) + consumption, consumption);
    }

    private bool TechBuildingNear(string playerName, int distance, string areaName)
    {
        if (ResolvePlayer(playerName) is not { } player) return false;
        var key = playerName + "|" + distance.ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + areaName;
        if (_techBuildingAnswers.TryGetValue(key, out var cached)) return cached;
        if (AreaOrDiagnose(areaName) is not { Points.Length: > 0 } area) return false;
        long sumX = 0, sumY = 0;
        foreach (var (x, y) in area.Points) { sumX += x; sumY += y; }
        var centreX = Fixed.FromInt(sumX / area.Points.Length);
        var centreY = Fixed.FromInt(sumY / area.Points.Length);
        // PolygonTrigger::getRadius: the largest distance of a corner from the centre.
        var farthest = area.Points.Max(point => (Fixed.FromInt(point.X) - centreX) * (Fixed.FromInt(point.X) - centreX) + (Fixed.FromInt(point.Y) - centreY) * (Fixed.FromInt(point.Y) - centreY));
        var reach = Fixed.Sqrt(farthest) + Fixed.FromInt(distance);
        var found = _world.Entities.Any(entity => entity.Template.IsKindOf("TECH_BUILDING") && CountsAsObject(entity) && entity.Owner != player.Id && !_world.AreAllies(player.Id, entity.Owner) && Distance2(entity, centreX, centreY) <= reach * reach);
        _techBuildingAnswers[key] = found;
        return found;
    }

    /// <summary><c>AIPlayer::isSupplySourceSafe</c>: no warehouse found counts as safe; otherwise no living enemy object (not a harvester or dozer, not stealthed) within the safe radius plus the warehouse's size.</summary>
    private bool SupplySourceSafe(PlayerId player, int minimumSupplies)
    {
        if (FindSupplyCenter(player, minimumSupplies) is not { } warehouse) return true;
        var radius = Fixed.FromDecimal(SupplyCenterSafeRadius) + warehouse.Template.BoundingCircleRadius;
        return !_world.Entities.Any(entity => !entity.Owner.IsSystem && entity.Owner != player && _world.AreEnemies(player, entity.Owner) && CountsAsObject(entity) && !entity.EffectivelyDead
                                              && !entity.Template.IsKindOf("HARVESTER") && !entity.Template.IsKindOf("DOZER") && !IsHiddenByStealth(entity)
                                              && Distance2(entity, warehouse.X, warehouse.Y) <= (radius + entity.Template.BoundingCircleRadius) * (radius + entity.Template.BoundingCircleRadius));
    }

    private bool SupplySourceAttacked(PlayerId player)
    {
        var now = _world.Tick;
        if (now == 0) return false;
        foreach (var entity in _world.Entities.Where(entity => entity.Owner == player && !entity.EffectivelyDead && (entity.Template.IsKindOf("CASH_GENERATOR") || entity.Template.IsKindOf("DOZER") || entity.Template.IsKindOf("HARVESTER"))))
            if (entity.LastDamageTick >= 0 && now - entity.LastDamageTick < SupplyScanFrames) return true;
        return false;
    }

    private void DescribeConditionState(IStateWriter writer)
    {
        if (_teamStates.Count > 0)
        {
            writer.BeginArray("teamStates");
            foreach (var (team, state) in _teamStates) { writer.BeginObject(null); writer.Int("team", team); writer.Text("state", state); writer.EndObject(); }
            writer.EndArray();
        }
        DescribeNotifications(writer, "completedPowers", _completedPowers);
        DescribeNotifications(writer, "completedUpgrades", _completedUpgrades);
        if (_transportStatus.Count > 0)
        {
            writer.BeginArray("transportStatus");
            foreach (var (id, status) in _transportStatus) { writer.BeginObject(null); writer.UInt("id", id); writer.Int("frame", status.Frame); writer.Int("count", status.Count); writer.EndObject(); }
            writer.EndArray();
        }
        if (_techBuildingAnswers.Count > 0)
        {
            writer.BeginArray("techBuildingAnswers");
            foreach (var (key, answer) in _techBuildingAnswers) { writer.BeginObject(null); writer.Text("key", key); writer.Bool("answer", answer); writer.EndObject(); }
            writer.EndArray();
        }

        static void DescribeNotifications(IStateWriter writer, string name, List<(int Player, string Name, uint Source)> list)
        {
            if (list.Count == 0) return;
            writer.BeginArray(name);
            foreach (var (player, entry, source) in list) { writer.BeginObject(null); writer.Int("player", player); writer.Text("name", entry); writer.UInt("source", source); writer.EndObject(); }
            writer.EndArray();
        }
    }

    private void LoadConditionState(IStateReader reader)
    {
        _teamStates.Clear();
        if (reader.Has("teamStates")) foreach (var item in reader.Array("teamStates")) _teamStates[checked((int)item.Int("team"))] = item.Text("state");
        LoadNotifications(reader, "completedPowers", _completedPowers);
        LoadNotifications(reader, "completedUpgrades", _completedUpgrades);
        _transportStatus.Clear();
        if (reader.Has("transportStatus")) foreach (var item in reader.Array("transportStatus")) _transportStatus[checked((uint)item.UInt("id"))] = (item.Int("frame"), checked((int)item.Int("count")));
        _techBuildingAnswers.Clear();
        if (reader.Has("techBuildingAnswers")) foreach (var item in reader.Array("techBuildingAnswers")) _techBuildingAnswers[item.Text("key")] = item.Bool("answer");

        static void LoadNotifications(IStateReader reader, string name, List<(int Player, string Name, uint Source)> list)
        {
            list.Clear();
            if (reader.Has(name)) foreach (var item in reader.Array(name)) list.Add((checked((int)item.Int("player")), item.Text("name"), checked((uint)item.UInt("source"))));
        }
    }
}
