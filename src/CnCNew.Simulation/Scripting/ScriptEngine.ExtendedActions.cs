using CnCNew.Data.World;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Scripting;

/// <summary>
/// Actions added in Phase 11: map reveal/shroud, garrison and container orders, build switches, science purchase, warehouse value, team merge/build and the
/// waypoint variants of the command-button abilities. <see cref="TryExecuteExtendedAction"/> is called from the default branch of <see cref="ExecuteAction"/>;
/// every function cites its reference in <c>ScriptActions.cpp</c>.
/// </summary>
public sealed partial class ScriptEngine
{
    /// <summary>The outstanding part of a <c>BUILD_TEAM</c> (<c>AIPlayer::buildSpecificAITeam</c> work orders): how many units of a type the team wants, how many were queued and how many joined.</summary>
    private sealed class TeamBuildOrder
    {
        public int TeamId;
        public string Type = string.Empty;
        public int Min;
        public int Total;
        public int Queued;
        public int Joined;
        public bool FactoryMissing;
    }

    private readonly List<TeamBuildOrder> _teamBuilds = [];

    private bool TryExecuteExtendedAction(MapScriptAction action)
    {
        var p = action.Parameters;
        var type = action.TypeName;
        switch (type)
        {
            // ---- map reveal / shroud (doRevealMapEntire, doRevealMapEntirePermanently, doShroudMapEntire, doRevealMapAtWaypoint, doShroudMapAtWaypoint)
            case "MAP_REVEAL_ALL_PERM":
            case "MAP_REVEAL_ALL_UNDO_PERM":
            case "MAP_REVEAL_ALL":
            case "MAP_SHROUD_ALL":
                if (!Has(type, p, 1)) return true;
                foreach (var player in MapPlayers(Str(p, 0)))
                {
                    if (type == "MAP_REVEAL_ALL_PERM") _world.RevealMapPermanently(player, true);
                    else if (type == "MAP_REVEAL_ALL_UNDO_PERM") _world.RevealMapPermanently(player, false);
                    else if (type == "MAP_REVEAL_ALL") _world.ExploreMap(player);
                    else _world.ShroudMap(player);
                }
                return true;
            case "MAP_REVEAL_AT_WAYPOINT":
            case "MAP_SHROUD_AT_WAYPOINT":
                if (!Has(type, p, 3) || WaypointOrDiagnose(Str(p, 0)) is not { } mapSpot) return true;
                foreach (var player in MapPlayers(Str(p, 2)))
                {
                    if (type == "MAP_REVEAL_AT_WAYPOINT") _world.ExploreArea(player, mapSpot.X, mapSpot.Y, Real(p, 1));
                    else _world.ShroudArea(player, mapSpot.X, mapSpot.Y, Real(p, 1));
                }
                return true;

            // ---- containers (doNamedEnterNamed, doTeamEnterNamed, doNamedExitAll, doTeamExitAll, garrison family)
            case "NAMED_ENTER_NAMED":
            case "NAMED_GARRISON_SPECIFIC_BUILDING":
                if (Has(type, p, 2) && NamedEntity(Str(p, 0)) is { } enterer && NamedEntity(Str(p, 1)) is { } destination)
                {
                    // doUnitGarrisonSpecificBuilding additionally requires a structure nobody else entered yet (or one the unit's own player entered last);
                    // doNamedEnterNamed has no such rule (a transport).
                    if (type == "NAMED_ENTER_NAMED" || MayGarrison(enterer, destination)) Enter(enterer, destination);
                }
                return true;
            case "TEAM_ENTER_NAMED":
                if (Has(type, p, 2) && NamedEntity(Str(p, 1)) is { } transport)
                    foreach (var team in TeamsNamed(Str(p, 0)))
                        foreach (var member in LiveMembers(team).ToArray()) Enter(member, transport);
                return true;
            case "TEAM_GARRISON_SPECIFIC_BUILDING":
                if (Has(type, p, 2) && NamedEntity(Str(p, 1)) is { } teamBuilding)
                    foreach (var team in TeamsNamed(Str(p, 0)))
                        foreach (var member in LiveMembers(team).ToArray())
                            if (MayGarrison(member, teamBuilding)) Enter(member, teamBuilding);
                return true;
            case "NAMED_GARRISON_NEAREST_BUILDING":
                if (Has(type, p, 1) && NamedEntity(Str(p, 0)) is { } nearestUser && NearestGarrisonBuilding(nearestUser, null) is { } nearest) Enter(nearestUser, nearest);
                return true;
            case "TEAM_GARRISON_NEAREST_BUILDING":
                if (Has(type, p, 1)) foreach (var team in TeamsNamed(Str(p, 0))) GarrisonTeamNearest(team);
                return true;
            case "PLAYER_GARRISON_ALL_BUILDINGS":
                if (Has(type, p, 1) && ResolvePlayer(Str(p, 0)) is { } garrisoner) GarrisonPlayerEverywhere(garrisoner.Id);
                return true;
            case "NAMED_EXIT_ALL":
            case "EXIT_SPECIFIC_BUILDING":
                if (Has(type, p, 1) && NamedEntity(Str(p, 0)) is { } evacuee && (type == "NAMED_EXIT_ALL" || evacuee.Template.IsKindOf("STRUCTURE"))) Evacuate(evacuee);
                return true;
            case "TEAM_EXIT_ALL":
                if (Has(type, p, 1))
                    foreach (var team in TeamsNamed(Str(p, 0)))
                        foreach (var member in LiveMembers(team).ToArray()) Evacuate(member);
                return true;
            case "NAMED_EXIT_BUILDING":
                if (Has(type, p, 1) && NamedEntity(Str(p, 0)) is { } leaver) ExitContainer(leaver);
                return true;
            case "TEAM_EXIT_ALL_BUILDINGS":
                if (Has(type, p, 1))
                    foreach (var team in TeamsNamed(Str(p, 0)))
                        foreach (var member in LiveMembers(team).ToArray()) ExitContainer(member);
                return true;
            case "PLAYER_EXIT_ALL_BUILDINGS":
                if (Has(type, p, 1) && ResolvePlayer(Str(p, 0)) is { } exiter)
                    foreach (var entity in PlayerEntities(exiter.Id).ToArray()) ExitContainer(entity);
                return true;

            // ---- build switches, sciences, warehouses
            case "PLAYER_ENABLE_BASE_CONSTRUCTION":
            case "PLAYER_DISABLE_BASE_CONSTRUCTION":
            case "PLAYER_ENABLE_UNIT_CONSTRUCTION":
            case "PLAYER_DISABLE_UNIT_CONSTRUCTION":
                if (Has(type, p, 1) && ResolvePlayer(Str(p, 0)) is { } builder && _world.HasPlayer(builder.Id))
                    _world.SetCanBuild(builder.Id, type.Contains("_BASE_", StringComparison.Ordinal), type.StartsWith("PLAYER_ENABLE", StringComparison.Ordinal));
                return true;
            case "PLAYER_PURCHASE_SCIENCE":
                // doPlayerPurchaseScience -> Player::attemptToPurchaseScience: the ordinary purchase command (checks, points and prerequisites apply).
                if (Has(type, p, 2) && ResolvePlayer(Str(p, 0)) is { } buyer && _world.HasPlayer(buyer.Id))
                {
                    if (!_world.Sciences.TryGet(Str(p, 1), out _)) Diagnose($"unknown science '{Str(p, 1)}'");
                    else
                    {
                        var owner = buyer.Id;
                        var science = Str(p, 1);
                        _issue(owner, (sequence, tick) => new PurchaseScienceCommand(owner, sequence, tick, science));
                    }
                }
                return true;
            case "WAREHOUSE_SET_VALUE":
                // doSetWarehouseValue -> SupplyWarehouseDockUpdate::setCashValue: boxes = ceil(cash / base value per box).
                if (Has(type, p, 2) && NamedEntity(Str(p, 0)) is { } warehouse && warehouse.ModuleArray.OfType<SupplyWarehouseDockUpdate>().FirstOrDefault() is { } dock)
                    dock.SetCashValue(_world, Int(p, 1), _world.ValuePerSupplyBox);
                return true;

            // ---- teams
            case "TEAM_MERGE_INTO_TEAM":
                if (Has(type, p, 2)) MergeTeam(Str(p, 0), Str(p, 1));
                return true;
            case "BUILD_TEAM":
                if (Has(type, p, 1)) BuildTeam(Str(p, 0));
                return true;

            // ---- command-button abilities at a waypoint
            case "NAMED_USE_COMMANDBUTTON_ABILITY_AT_WAYPOINT":
                if (Has(type, p, 3) && NamedEntity(Str(p, 0)) is { } abilityUser && WaypointOrDiagnose(Str(p, 2)) is { } abilitySpot)
                    UseCommandButtonAbility(abilityUser, Str(p, 1), SpecialPowerTargetKind.Location, EntityId.Invalid, abilitySpot.X, abilitySpot.Y);
                return true;
            case "TEAM_USE_COMMANDBUTTON_ABILITY_AT_WAYPOINT":
                if (Has(type, p, 3) && WaypointOrDiagnose(Str(p, 2)) is { } teamAbilitySpot)
                    foreach (var team in TeamsNamed(Str(p, 0)))
                        foreach (var member in LiveMembers(team).ToArray()) UseCommandButtonAbility(member, Str(p, 1), SpecialPowerTargetKind.Location, EntityId.Invalid, teamAbilitySpot.X, teamAbilitySpot.Y);
                return true;
            case "NAMED_USE_COMMANDBUTTON_ABILITY_USING_WAYPOINT_PATH":
            {
                // doNamedUseCommandButtonAbilityUsingWaypointPath starts the ability at the path waypoint closest to the unit; a Particle Uplink Cannon then drives its
                // beam along the rest of the path (ParticleUplinkCannonUpdate::initiateIntentToDoSpecialPower with a waypoint), any other ability is only started there.
                if (!Has(type, p, 3) || NamedEntity(Str(p, 0)) is not { } pathUser) return true;
                if (_program.FindPath(Str(p, 2)) is not { } path || path.Waypoints.IsDefaultOrEmpty) { Diagnose($"unknown waypoint path '{Str(p, 2)}'"); return true; }
                var closestIndex = 0;
                var closestDistance = Fixed.MaxValue;
                for (var index = 0; index < path.Waypoints.Length; index++)
                {
                    var distance = DistanceSquared(pathUser, path.Waypoints[index].X, path.Waypoints[index].Y);
                    if (distance >= closestDistance) continue;
                    closestDistance = distance;
                    closestIndex = index;
                }
                var closest = path.Waypoints[closestIndex];
                if (pathUser.Modules.OfType<ParticleUplinkCannonUpdate>().FirstOrDefault() is { } uplink)
                    uplink.PrepareScriptedPath(_world, [..path.Waypoints.Skip(closestIndex).Select(waypoint => new Point3(waypoint.X, waypoint.Y, _world.GroundHeightAt(waypoint.X, waypoint.Y)))]);
                UseCommandButtonAbility(pathUser, Str(p, 1), SpecialPowerTargetKind.Location, EntityId.Invalid, closest.X, closest.Y);
                return true;
            }
            // ---- doIdleAllPlayerUnits (empty player name: the human players): every mobile non-structure object stops where it is
            case "IDLE_ALL_UNITS":
                foreach (var entity in PlayerEntities(_options.LocalPlayer).Where(value => !value.Template.IsKindOf("STRUCTURE")).ToArray()) Stop(entity);
                return true;
            default:
                return TryExecuteSequentialAction(action);
        }
    }

    /// <summary>The players a map action affects: the named one, or (empty name) the human viewer, as <c>getHumanPlayerMask</c> does in a single-player game.</summary>
    private IEnumerable<PlayerId> MapPlayers(string name)
    {
        if (name.Length == 0) return [_options.LocalPlayer];
        return ResolvePlayer(name) is { } player ? [player.Id] : [];
    }

    // ------------------------------------------------------------------------------------------------ containers

    /// <summary><c>doUnitGarrisonSpecificBuilding</c>: <c>isKindOf(STRUCTURE) &amp;&amp; player == 0 || player == owner</c>, with <c>player</c> the last player who entered the container.</summary>
    private bool MayGarrison(Entity unit, Entity building)
    {
        if (building.Container is null) { Diagnose("garrison of a building without a container"); return false; }
        var hasEntered = _lastEntered.TryGetValue(building.Id.Value, out var who);
        return (building.Template.IsKindOf("STRUCTURE") && !hasEntered) || (hasEntered && who == unit.Owner.Value);
    }

    private void Enter(Entity actor, Entity container)
    {
        if (actor.Owner.IsSystem || actor.Template.Locomotor is null || actor.ContainedBy.IsValid) return;
        _followers.Remove(actor.Id.Value);
        var owner = actor.Owner;
        var actorId = actor.Id;
        var containerId = container.Id;
        if (!_issue(owner, (sequence, tick) => new MoveAndEnterCommand(owner, sequence, tick, actorId, containerId)).Accepted) Diagnose("enter order rejected");
        else _busyThisTick.Add(actorId.Value);
    }

    private void ExitContainer(Entity actor)
    {
        if (!actor.ContainedBy.IsValid) return;
        var owner = actor.Owner;
        if (owner.IsSystem) return;
        var actorId = actor.Id;
        if (!_issue(owner, (sequence, tick) => new ExitContainerCommand(owner, sequence, tick, actorId)).Accepted) Diagnose("exit order rejected");
    }

    /// <summary><c>aiEvacuate</c>/<c>removeAllContained</c>: the container empties itself; a neutral building is emptied on behalf of the first occupant's owner.</summary>
    private void Evacuate(Entity container)
    {
        if (container.Container is not { ContainCount: > 0 } contents) return;
        var issuer = container.Owner;
        if (issuer.IsSystem && _world.FindEntity(contents.Contained[0]) is { } first) issuer = first.Owner;
        if (issuer.IsSystem) return;
        var containerId = container.Id;
        if (!_issue(issuer, (sequence, tick) => new EvacuateCommand(issuer, sequence, tick, containerId, false, Fixed.Zero, Fixed.Zero)).Accepted) Diagnose("evacuate order rejected");
    }

    /// <summary>The garrisonable structures nearest to the point first (<c>iterateObjectsInRange(REALLY_FAR, ITER_SORTED_NEAR_TO_FAR)</c> with <c>KINDOF_STRUCTURE</c>), internet centres only for money hackers.</summary>
    private IEnumerable<Entity> GarrisonCandidates(Entity unit, Fixed x, Fixed y)
    {
        var wantsInternetCentre = unit.Template.IsKindOf("MONEY_HACKER");
        return _world.Entities
            .Where(entity => entity != unit && entity.Container is not null && entity.Template.IsKindOf("STRUCTURE") && !entity.EffectivelyDead && !entity.DestroyRequested
                             && !entity.HasStatus(UnderConstructionBit) && entity.Template.IsKindOf("FS_INTERNET_CENTER") == wantsInternetCentre)
            .OrderBy(entity => DistanceSquared(entity, x, y)).ThenBy(entity => entity.Id.Value);
    }

    /// <summary><c>doUnitGarrisonNearestBuilding</c>: the nearest structure nobody else entered (or the unit's own side entered last) that the unit can enter.</summary>
    private Entity? NearestGarrisonBuilding(Entity unit, Dictionary<uint, int>? reserved)
    {
        foreach (var building in GarrisonCandidates(unit, unit.X, unit.Y))
        {
            if (_lastEntered.TryGetValue(building.Id.Value, out var who) && who != unit.Owner.Value) continue;
            if (!HasFreeSlot(building, reserved)) continue;
            if (!building.Container!.IsValidContainerFor(_world, unit, false)) continue;
            return building;
        }
        return null;
    }

    private static bool HasFreeSlot(Entity building, Dictionary<uint, int>? reserved)
    {
        var container = building.Container!;
        if (container.ContainMax < 0) return true;
        var taken = container.ContainCount + (reserved is not null && reserved.TryGetValue(building.Id.Value, out var count) ? count : 0);
        return taken < container.ContainMax;
    }

    /// <summary>
    /// <c>doTeamGarrisonNearestBuilding</c>: buildings nearest to the team's leader first; each gets as many infantry members (not <c>NO_GARRISON</c>) as it has
    /// open slots, each sent individually.
    /// </summary>
    private void GarrisonTeamNearest(ScriptTeam team)
    {
        var members = LiveMembers(team).ToList();
        if (members.Count == 0) return;
        var leader = members[0];
        var next = 0;
        foreach (var building in GarrisonCandidates(leader, leader.X, leader.Y).ToArray())
        {
            if (building.Container is not { } container) continue;
            var slots = container.ContainMax < 0 ? int.MaxValue : container.ContainMax - container.ContainCount;
            for (var filled = 0; filled < slots;)
            {
                if (next >= members.Count) return;
                var member = members[next++];
                if (!member.Template.IsKindOf("INFANTRY") || member.Template.IsKindOf("NO_GARRISON")) continue;
                Enter(member, building);
                filled++;
            }
        }
    }

    /// <summary><c>Player::garrisonAllUnits</c>: every unit of the player takes the first (map-corner nearest) building it may enter, with open slots counted across the order.</summary>
    private void GarrisonPlayerEverywhere(PlayerId player)
    {
        var reserved = new Dictionary<uint, int>();
        foreach (var unit in PlayerEntities(player).Where(entity => entity.Template.Locomotor is not null && !entity.Template.IsKindOf("STRUCTURE")).ToArray())
        {
            foreach (var building in GarrisonCandidates(unit, Fixed.FromInt(50), Fixed.FromInt(50)))
            {
                if (_lastEntered.TryGetValue(building.Id.Value, out var who) && who != unit.Owner.Value) continue;
                if (!HasFreeSlot(building, reserved) || !building.Container!.IsValidContainerFor(_world, unit, false)) continue;
                reserved[building.Id.Value] = reserved.GetValueOrDefault(building.Id.Value) + 1;
                Enter(unit, building);
                break;
            }
        }
    }

    // ------------------------------------------------------------------------------------------------ teams

    /// <summary>
    /// <c>doMergeTeamIntoTeam</c>: every member of the source team joins the destination (an existing instance of that prototype, or a new one) and takes its owner;
    /// the source instance is emptied. The reference deletes the instance; a singleton prototype keeps an empty one here so <c>TEAM_DESTROYED</c> still answers.
    /// </summary>
    private void MergeTeam(string sourceName, string destinationName)
    {
        var source = FindTeam(sourceName);
        var destination = FindTeam(destinationName) ?? CreateTeam(destinationName);
        if (source is null || destination is null || ReferenceEquals(source, destination)) return;
        foreach (var member in LiveMembers(source).ToArray())
        {
            if (!destination.Members.Contains(member.Id.Value)) destination.Members.Add(member.Id.Value);
            if (member.Owner != destination.Owner) _world.TransferOwnership(member.Id, destination.Owner);
            if (destination.AttackPriority.Length > 0) _prioritySetOf[member.Id.Value] = destination.AttackPriority;
        }
        source.Members.Clear();
        destination.Created = true;
        if (!(_program.FindTeam(source.Prototype) is { } prototype && (prototype.IsSingleton || prototype.MaxInstances < 2))) _teams.Remove(source.Id);
        _teamBuilds.RemoveAll(order => order.TeamId == source.Id);
    }

    /// <summary>
    /// <c>doBuildTeam</c> -> <c>AIPlayer::buildSpecificAITeam</c>: the team is created inactive and its units (at least <c>MinCount</c>, up to <c>MaxCount</c> of
    /// each type) are queued at the owner's factories one command at a time (<see cref="ProcessTeamBuilds"/>); produced units join the team, and it becomes created
    /// once every minimum is met. Simplifications (docs/SCRIPT-ENGINE.md §8): any finished factory that can produce the unit serves (no build list), no rally gathering,
    /// no <c>executeActions</c> start script, no priority queue.
    /// </summary>
    private void BuildTeam(string teamName)
    {
        var prototype = _program.FindTeam(teamName);
        if (prototype is null) { Diagnose($"unknown team '{teamName}'"); return; }
        var team = (prototype.IsSingleton || prototype.MaxInstances < 2 ? FindTeamInstance(teamName) : null) ?? CreateTeam(teamName);
        if (team is null) return;
        if (team.Owner.IsSystem || !_world.HasPlayer(team.Owner)) { Diagnose($"BUILD_TEAM '{teamName}': team has no player"); return; }
        if (!_world.CanBuildUnits(team.Owner)) { Diagnose($"BUILD_TEAM '{teamName}': unit construction disabled"); return; }
        if ((prototype.IsSingleton || prototype.MaxInstances < 2) && LiveMembers(team).Any(CountsAsObject)) { Diagnose($"BUILD_TEAM '{teamName}': singleton team already exists"); return; }
        if (_teamBuilds.Any(order => order.TeamId == team.Id)) { Diagnose($"BUILD_TEAM '{teamName}': build already running"); return; }
        team.Created = false;
        foreach (var unit in prototype.Units)
        {
            if (!_world.Templates.TryGet(unit.Type, out var template)) { Diagnose($"unknown object type '{unit.Type}'"); continue; }
            if (!template.IsInstantiable) { Diagnose($"not instantiable '{unit.Type}'"); continue; }
            if (unit.MaxCount <= 0) continue;
            _teamBuilds.Add(new TeamBuildOrder { TeamId = team.Id, Type = unit.Type, Min = Math.Min(unit.MinCount, unit.MaxCount), Total = unit.MaxCount });
        }
        if (!_teamBuilds.Any(order => order.TeamId == team.Id)) Diagnose($"BUILD_TEAM '{teamName}': contains no buildable units");
        else { _teamQueue.RemoveAll(entry => entry.TeamId == team.Id); _teamQueue.Add(new TeamQueueEntry { TeamId = team.Id, Started = _tick }); }
    }

    /// <summary>Queues one more unit per open order per tick at an idle (else any) factory that can produce it; a shortage of money simply retries next tick.</summary>
    private void ProcessTeamBuilds()
    {
        if (_teamBuilds.Count == 0) return;
        foreach (var order in _teamBuilds)
        {
            if (order.Queued >= order.Total || !_teams.TryGetValue(order.TeamId, out var team)) continue;
            var owner = team.Owner;
            Entity? best = null;
            var bestBusy = true;
            foreach (var entity in PlayerEntities(owner))
            {
                if (entity.HasStatus(UnderConstructionBit) || entity.ModuleArray.OfType<ProductionUpdate>().FirstOrDefault() is not { HasRoom: true } production) continue;
                if (!_world.CanQueueProductionTemplate(owner, entity.Id, order.Type)) continue;
                var busy = production.Queue.Length > 0;
                if (best is not null && (busy || !bestBusy)) continue;
                best = entity;
                bestBusy = busy;
                if (!busy) break;
            }
            if (best is null)
            {
                if (!order.FactoryMissing) { order.FactoryMissing = true; Diagnose($"BUILD_TEAM: no factory for '{order.Type}'"); }
                continue;
            }
            order.FactoryMissing = false;
            var producer = best.Id;
            var template = order.Type;
            if (_issue(owner, (sequence, tick) => new QueueProductionCommand(owner, sequence, tick, producer, template)).Accepted) order.Queued++;
        }
    }

    /// <summary><c>AIPlayer::onUnitProduced</c>: a freshly produced unit of a building team's owner joins the team that still wants its type; the team activates (<c>setActive</c>) once every minimum is met.</summary>
    private void JoinProducedUnit(Entity unit)
    {
        if (_teamBuilds.Count == 0) return;
        var order = _teamBuilds.FirstOrDefault(candidate => candidate.Joined < candidate.Queued && candidate.Type == unit.Template.Name
            && _teams.TryGetValue(candidate.TeamId, out var team) && team.Owner == unit.Owner);
        if (order is null || !_teams.TryGetValue(order.TeamId, out var joined)) return;
        order.Joined++;
        if (!joined.Members.Contains(unit.Id.Value)) joined.Members.Add(unit.Id.Value);
        if (joined.AttackPriority.Length > 0) _prioritySetOf[unit.Id.Value] = joined.AttackPriority;
        if (_program.FindTeam(joined.Prototype) is { Home.Length: > 0 } built && _program.FindWaypoint(built.Home) is { } home && unit.Template.Locomotor is not null) MoveTo(unit, home.X, home.Y);
        if (_teamBuilds.Where(other => other.TeamId == order.TeamId).All(other => other.Joined >= other.Total)) _teamBuilds.RemoveAll(other => other.TeamId == order.TeamId);
    }

    private void DescribeTeamBuilds(IStateWriter writer)
    {
        if (_teamBuilds.Count == 0) return;
        writer.BeginArray("teamBuilds");
        foreach (var order in _teamBuilds)
        {
            writer.BeginObject(null);
            writer.Int("team", order.TeamId);
            writer.Text("type", order.Type);
            writer.Int("min", order.Min);
            writer.Int("total", order.Total);
            writer.Int("queued", order.Queued);
            writer.Int("joined", order.Joined);
            writer.Bool("factoryMissing", order.FactoryMissing);
            writer.EndObject();
        }
        writer.EndArray();
    }

    private void LoadTeamBuilds(IStateReader reader)
    {
        _teamBuilds.Clear();
        if (!reader.Has("teamBuilds")) return;
        foreach (var item in reader.Array("teamBuilds"))
            _teamBuilds.Add(new TeamBuildOrder
            {
                TeamId = checked((int)item.Int("team")), Type = item.Text("type"), Min = checked((int)item.Int("min")), Total = checked((int)item.Int("total")),
                Queued = checked((int)item.Int("queued")), Joined = checked((int)item.Int("joined")), FactoryMissing = item.Bool("factoryMissing"),
            });
    }
}
