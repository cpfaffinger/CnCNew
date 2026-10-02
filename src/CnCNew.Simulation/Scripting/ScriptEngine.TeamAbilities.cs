using CnCNew.Data.Definitions;
using CnCNew.Data.World;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Scripting;

/// <summary>
/// Actions added in Phase 12: per-team and per-player relationship overrides, the team command-button family (<c>TEAM_HUNT_WITH_COMMAND_BUTTON</c>,
/// <c>TEAM_ALL_USE_COMMANDBUTTON_ON_NEAREST_*</c>, <c>TEAM_PARTIAL_USE_COMMANDBUTTON</c>), transport loading, tunnel guarding, panic, facing, the stealth switch,
/// unmanning and production priority. Every function cites its reference in <c>ScriptActions.cpp</c>; what the simulation has no consumer for is
/// <see cref="ScriptSupport.Recorded"/> (attitude, wander, held, rank level and limit, hulk lifetime), never counted as executed.
/// </summary>
public sealed partial class ScriptEngine
{
    private readonly SortedDictionary<long, int> _relTeamTeam = [];
    private readonly SortedDictionary<long, int> _relTeamPlayer = [];
    private readonly SortedDictionary<long, int> _relPlayerTeam = [];
    private readonly SortedDictionary<int, int> _teamAttitude = [];
    private readonly SortedDictionary<int, int> _teamWander = [];
    private readonly SortedDictionary<int, int> _rankLevel = [];
    private readonly SortedSet<uint> _held = [];
    private readonly SortedSet<uint> _unmanned = [];
    private readonly SortedDictionary<string, int> _priorityDelta = [];
    private readonly SortedDictionary<uint, string> _buttonHunts = [];
    private readonly SortedDictionary<uint, long> _buttonHuntNext = [];
    private int _rankLimit = -1;
    private long _hulkLifetimeOverrideTicks = -1;
    private Dictionary<uint, ScriptTeam>? _entityTeams;
    private long _entityTeamsTick = -1;
    private int _entityTeamsMembers = -1;

    /// <summary>The rank level script actions set for a player (recorded: the simulation has no rank system to read it).</summary>
    public int RankLevelOf(PlayerId player) => _rankLevel.GetValueOrDefault(player.Value);

    /// <summary>The map-wide rank level limit a script set, or −1.</summary>
    public int RankLimit => _rankLimit;

    /// <summary>The hulk lifetime override in ticks a script set (<c>SCRIPTING_OVERRIDE_HULK_LIFETIME</c>), or −1.</summary>
    public long HulkLifetimeOverrideTicks => _hulkLifetimeOverrideTicks;

    /// <summary>The attitude (<c>AIMood</c> value) a script last gave a team instance, or <see langword="null"/>.</summary>
    public int? AttitudeOf(int teamId) => _teamAttitude.TryGetValue(teamId, out var attitude) ? attitude : null;

    /// <summary>The effective production priority of a team prototype: its map value plus the successes and failures scripts recorded.</summary>
    public int ProductionPriorityOf(ScriptTeamPrototype prototype) => prototype.ProductionPriority + _priorityDelta.GetValueOrDefault(prototype.Name);

    private static long Key(int first, int second) => ((long)first << 32) | (uint)second;

    private bool TryExecuteTeamAbilityAction(MapScriptAction action)
    {
        var p = action.Parameters;
        var type = action.TypeName;
        switch (type)
        {
            // ---- override relationships (doTeamSetOverrideRelationToTeam, Team::setOverrideTeamRelationship, Player::setTeamRelationship, ...)
            case "TEAM_SET_OVERRIDE_RELATION_TO_TEAM":
                if (Has(type, p, 3) && FindTeam(Str(p, 0)) is { } first && FindTeam(Str(p, 1)) is { } second && Enum.IsDefined((Relationship)Int(p, 2)))
                    SetRelationOverride(_relTeamTeam, Key(first.Id, second.Id), Int(p, 2));
                return true;
            case "TEAM_REMOVE_OVERRIDE_RELATION_TO_TEAM":
                if (Has(type, p, 2) && FindTeam(Str(p, 0)) is { } removeFrom && FindTeam(Str(p, 1)) is { } removeTo) _relTeamTeam.Remove(Key(removeFrom.Id, removeTo.Id));
                return true;
            case "TEAM_SET_OVERRIDE_RELATION_TO_PLAYER":
                if (Has(type, p, 3) && FindTeam(Str(p, 0)) is { } viewer && ResolvePlayer(Str(p, 1)) is { } seen && Enum.IsDefined((Relationship)Int(p, 2)))
                    SetRelationOverride(_relTeamPlayer, Key(viewer.Id, seen.Id.Value), Int(p, 2));
                return true;
            case "TEAM_REMOVE_OVERRIDE_RELATION_TO_PLAYER":
                if (Has(type, p, 2) && FindTeam(Str(p, 0)) is { } unviewer && ResolvePlayer(Str(p, 1)) is { } unseen) _relTeamPlayer.Remove(Key(unviewer.Id, unseen.Id.Value));
                return true;
            case "PLAYER_SET_OVERRIDE_RELATION_TO_TEAM":
                if (Has(type, p, 3) && ResolvePlayer(Str(p, 0)) is { } watcher && FindTeam(Str(p, 1)) is { } watched && Enum.IsDefined((Relationship)Int(p, 2)))
                    SetRelationOverride(_relPlayerTeam, Key(watcher.Id.Value, watched.Id), Int(p, 2));
                return true;
            case "PLAYER_REMOVE_OVERRIDE_RELATION_TO_TEAM":
                if (Has(type, p, 2) && ResolvePlayer(Str(p, 0)) is { } unwatcher && FindTeam(Str(p, 1)) is { } unwatched) _relPlayerTeam.Remove(Key(unwatcher.Id.Value, unwatched.Id));
                return true;
            case "TEAM_REMOVE_ALL_OVERRIDE_RELATIONS":
                if (Has(type, p, 1) && FindTeam(Str(p, 0)) is { } cleared)
                {
                    foreach (var key in _relTeamTeam.Keys.Where(key => (int)(key >> 32) == cleared.Id).ToArray()) _relTeamTeam.Remove(key);
                    foreach (var key in _relTeamPlayer.Keys.Where(key => (int)(key >> 32) == cleared.Id).ToArray()) _relTeamPlayer.Remove(key);
                }
                return true;

            // ---- team command buttons (doTeamHuntWithCommandButton, doTeamUseCommandButtonOnNearest*, doTeamPartialUseCommandButton, doTeamUseCommandButtonOnNamed)
            case "TEAM_HUNT_WITH_COMMAND_BUTTON":
                if (Has(type, p, 2)) foreach (var team in TeamsNamed(Str(p, 0))) HuntWithCommandButton(team, Str(p, 1));
                return true;
            case "TEAM_ALL_USE_COMMANDBUTTON_ON_NAMED":
                if (Has(type, p, 3) && NamedEntity(Str(p, 2)) is { } named)
                    foreach (var team in TeamsNamed(Str(p, 0)))
                        foreach (var carrier in Carriers(LiveMembers(team), Str(p, 1)).ToArray()) UseCommandButtonAbility(carrier, Str(p, 1), SpecialPowerTargetKind.Object, named.Id, Fixed.Zero, Fixed.Zero);
                return true;
            case "TEAM_ALL_USE_COMMANDBUTTON_ON_NEAREST_ENEMY_UNIT":
                if (Has(type, p, 2)) UseOnNearest(Str(p, 0), Str(p, 1), false, (_, candidate) => CountsAsObject(candidate));
                return true;
            case "TEAM_ALL_USE_COMMANDBUTTON_ON_NEAREST_ENEMY_BUILDING":
                if (Has(type, p, 2)) UseOnNearest(Str(p, 0), Str(p, 1), false, (_, candidate) => candidate.Template.IsKindOf("STRUCTURE"));
                return true;
            case "TEAM_ALL_USE_COMMANDBUTTON_ON_NEAREST_ENEMY_BUILDING_CLASS":
                if (Has(type, p, 3) && KindOfName(p[2]) is { } buildingClass)
                    UseOnNearest(Str(p, 0), Str(p, 1), false, (_, candidate) => candidate.Template.IsKindOf("STRUCTURE") && candidate.Template.IsKindOf(buildingClass));
                return true;
            case "TEAM_ALL_USE_COMMANDBUTTON_ON_NEAREST_KINDOF":
                if (Has(type, p, 3) && KindOfName(p[2]) is { } kind) UseOnNearest(Str(p, 0), Str(p, 1), false, (_, candidate) => candidate.Template.IsKindOf(kind));
                return true;
            case "TEAM_ALL_USE_COMMANDBUTTON_ON_NEAREST_GARRISONED_BUILDING":
                // Enemy structures that can be garrisoned (PartitionFilterGarrisonable: have a garrison container).
                if (Has(type, p, 2))
                    UseOnNearest(Str(p, 0), Str(p, 1), false, (_, candidate) => candidate.Template.IsKindOf("STRUCTURE") && candidate.Container is not null && candidate.Template.Modules.Any(module => module.Name == "GarrisonContain"));
                return true;
            case "TEAM_ALL_USE_COMMANDBUTTON_ON_NEAREST_OBJECTTYPE":
                // Enemies and neutrals both qualify here (ALLOW_ENEMIES | ALLOW_NEUTRAL); an object-type list is searched like a single type.
                if (Has(type, p, 3)) UseOnNearest(Str(p, 0), Str(p, 1), true, (_, candidate) => TypeMatches(Str(p, 2), candidate));
                return true;
            case "TEAM_PARTIAL_USE_COMMANDBUTTON":
                if (Has(type, p, 3))
                    foreach (var team in TeamsNamed(Str(p, 1)))
                    {
                        var carriers = Carriers(LiveMembers(team), Str(p, 2)).ToArray();
                        var count = (int)(Real(p, 0) * Fixed.FromInt(carriers.Length) / Fixed.FromInt(100)).Floor();
                        foreach (var carrier in carriers.Take(count)) UseCommandButtonAbility(carrier, Str(p, 2), SpecialPowerTargetKind.None, EntityId.Invalid, Fixed.Zero, Fixed.Zero);
                    }
                return true;

            // ---- movement and orders (doLoadAllTransports, doMoveTeamTowardsNearest, doTeamGuardInTunnelNetwork, doTeamPanic, doNamed/TeamFaceWaypoint)
            case "TEAM_LOAD_TRANSPORTS":
                if (Has(type, p, 1)) foreach (var team in TeamsNamed(Str(p, 0))) LoadTransports(team);
                return true;
            case "TEAM_MOVE_TOWARDS_NEAREST_OBJECT_TYPE":
                if (Has(type, p, 3)) foreach (var team in TeamsNamed(Str(p, 0))) MoveTowardsNearest(team, Str(p, 1), Str(p, 2));
                return true;
            case "TEAM_GUARD_IN_TUNNEL_NETWORK":
                if (Has(type, p, 1))
                    foreach (var team in TeamsNamed(Str(p, 0)))
                        foreach (var member in LiveMembers(team).ToArray()) GuardInTunnelNetwork(member);
                return true;
            case "TEAM_PANIC":
                if (Has(type, p, 2) && _program.FindPath(Str(p, 1)) is { Waypoints.IsDefaultOrEmpty: false } panicPath)
                    foreach (var team in TeamsNamed(Str(p, 0)))
                        foreach (var member in LiveMembers(team).ToArray()) Panic(member, panicPath);
                return true;
            case "NAMED_FACE_WAYPOINT":
                if (Has(type, p, 2) && NamedEntity(Str(p, 0)) is { } facing && WaypointOrDiagnose(Str(p, 1)) is { } faced) Face(facing, faced.X, faced.Y);
                return true;
            case "TEAM_FACE_WAYPOINT":
                if (Has(type, p, 2) && WaypointOrDiagnose(Str(p, 1)) is { } teamFaced)
                    foreach (var team in TeamsNamed(Str(p, 0)))
                        foreach (var member in LiveMembers(team).ToArray()) Face(member, teamFaced.X, teamFaced.Y);
                return true;
            case "NAMED_SET_STEALTH_ENABLED":
                if (Has(type, p, 2) && NamedEntity(Str(p, 0)) is { } stealthy) SetStealthEnabled(stealthy, Int(p, 1) != 0);
                return true;
            case "TEAM_SET_STEALTH_ENABLED":
                if (Has(type, p, 2))
                    foreach (var team in TeamsNamed(Str(p, 0)))
                        foreach (var member in LiveMembers(team).ToArray()) SetStealthEnabled(member, Int(p, 1) != 0);
                return true;

            // ---- unmanned objects (doNamedSetUnmanned, doTeamSetUnmanned, doDeleteAllUnmanned)
            case "NAMED_SET_UNMANNED_STATUS":
                if (Has(type, p, 1) && NamedEntity(Str(p, 0)) is { } unmanning) Unman(unmanning);
                return true;
            case "TEAM_SET_UNMANNED_STATUS":
                if (Has(type, p, 1))
                    foreach (var team in TeamsNamed(Str(p, 0)))
                        foreach (var member in LiveMembers(team).ToArray()) Unman(member);
                return true;
            case "DELETE_ALL_UNMANNED":
                foreach (var id in _unmanned.ToArray())
                    if (_world.FindEntity(new EntityId(id)) is { DestroyRequested: false } unmannedObject) _world.RequestDestroy(unmannedObject.Id);
                _unmanned.Clear();
                return true;

            // ---- production priority (doTeamIncreasePriority / doTeamDecreasePriority: TeamPrototype::increaseAIPriorityForSuccess / decreaseAIPriorityForFailure)
            case "TEAM_INCREASE_PRIORITY":
            case "TEAM_DECREASE_PRIORITY":
                if (Has(type, p, 1) && FindTeam(Str(p, 0)) is { } prioritized && _program.FindTeam(prioritized.Prototype) is { } prototype)
                {
                    var delta = type == "TEAM_INCREASE_PRIORITY" ? prototype.PrioritySuccessIncrease : -prototype.PriorityFailureDecrease;
                    _priorityDelta[prototype.Name] = _priorityDelta.GetValueOrDefault(prototype.Name) + delta;
                }
                return true;

            // ---- recorded only: no consumer in the simulation
            case "TEAM_SET_ATTITUDE":
                if (Has(type, p, 2))
                    foreach (var team in TeamsNamed(Str(p, 0))) _teamAttitude[team.Id] = Int(p, 1);
                _recordedActions++;
                return true;
            case "TEAM_WANDER":
            case "TEAM_WANDER_IN_PLACE":
                if (Has(type, p, 1))
                    foreach (var team in TeamsNamed(Str(p, 0))) _teamWander[team.Id] = type == "TEAM_WANDER" ? 1 : 2;
                _recordedActions++;
                return true;
            case "NAMED_SET_HELD":
                if (Has(type, p, 2) && NamedEntity(Str(p, 0)) is { } held)
                {
                    if (Int(p, 1) != 0) _held.Add(held.Id.Value);
                    else _held.Remove(held.Id.Value);
                }
                _recordedActions++;
                return true;
            case "PLAYER_SET_RANKLEVELLIMIT":
                if (Has(type, p, 1)) _rankLimit = Int(p, 0);
                _recordedActions++;
                return true;
            case "PLAYER_SET_RANKLEVEL":
            case "PLAYER_ADD_RANKLEVEL":
                if (Has(type, p, 2) && ResolvePlayer(Str(p, 0)) is { } ranked)
                    _rankLevel[ranked.Id.Value] = type == "PLAYER_SET_RANKLEVEL" ? Int(p, 1) : _rankLevel.GetValueOrDefault(ranked.Id.Value) + Int(p, 1);
                _recordedActions++;
                return true;
            case "SCRIPTING_OVERRIDE_HULK_LIFETIME":
                if (Has(type, p, 1)) _hulkLifetimeOverrideTicks = Real(p, 0).IsNegative ? -1 : SecondsToFrames(Real(p, 0));
                _recordedActions++;
                return true;
            default:
                return false;
        }
    }

    private void SetRelationOverride(SortedDictionary<long, int> table, long key, int relation)
    {
        table[key] = relation;
        _world.RelationshipOverride = OverrideRelationship;
    }

    /// <summary>
    /// <c>Team::getRelationship</c> / <c>Player::getRelationship(const Team*)</c>: the team's override for the other team, then for the other team's player, then the
    /// viewing player's override for the other team; <see langword="null"/> leaves the decision to the player relation as before.
    /// </summary>
    private Relationship? OverrideRelationship(Entity from, Entity to)
    {
        var fromTeam = TeamOfEntity(from.Id.Value);
        var toTeam = TeamOfEntity(to.Id.Value);
        if (fromTeam is not null)
        {
            if (toTeam is not null && _relTeamTeam.TryGetValue(Key(fromTeam.Id, toTeam.Id), out var teamToTeam)) return (Relationship)teamToTeam;
            if (_relTeamPlayer.TryGetValue(Key(fromTeam.Id, to.Owner.Value), out var teamToPlayer)) return (Relationship)teamToPlayer;
        }
        if (toTeam is not null && _relPlayerTeam.TryGetValue(Key(from.Owner.Value, toTeam.Id), out var playerToTeam)) return (Relationship)playerToTeam;
        return null;
    }

    private ScriptTeam? TeamOfEntity(uint entity)
    {
        var members = 0;
        foreach (var team in _teams.Values) members += team.Members.Count;
        if (_entityTeams is null || _entityTeamsTick != _world.Tick || _entityTeamsMembers != members)
        {
            _entityTeams = [];
            foreach (var team in _teams.Values)
                foreach (var id in team.Members) _entityTeams.TryAdd(id, team);
            _entityTeamsTick = _world.Tick;
            _entityTeamsMembers = members;
        }
        return _entityTeams.GetValueOrDefault(entity);
    }

    // ------------------------------------------------------------------------------------------------ command buttons on a team

    /// <summary>The <see cref="NameTables.KindOf"/> name of a KindOf parameter: the text when the map kept one, else the bit index the reference stores (<c>getInt</c>).</summary>
    private static string? KindOfName(MapScriptParameter parameter)
        => parameter.Text.Length > 0 ? parameter.Text : parameter.Integer >= 0 && parameter.Integer < NameTables.KindOf.Count ? NameTables.KindOf.Canonical(parameter.Integer) : null;

    /// <summary>The members that can use a button: those that carry the special power for a special-power button, every member for any other button.</summary>
    private IEnumerable<Entity> Carriers(IEnumerable<Entity> members, string ability)
    {
        if (!_world.Templates.Commands.TryGetSpecialPowerAbility(ability, out var power))
            return _world.Templates.Commands.TryGetButton(ability, out var button) && button.Command.ToUpperInvariant() is "FIRE_WEAPON" or "SWITCH_WEAPON" ? members.Where(member => member.Template.DefaultWeapon >= 0) : members;
        if (!_world.SpecialPowers.TryGet(power, out var info)) return [];
        return members.Where(member => SpecialPowerRouting.FindModule(member, info.Index) is not null);
    }

    /// <summary>
    /// <c>doTeamUseCommandButtonOnNearest*</c>: the group's centre is the mean of its live members; the target is the object nearest to it that the filter accepts
    /// (enemies, or enemies and neutrals) and that is alive, not hidden by stealth and accepted by the world for the first carrier; every carrier then uses the button on it.
    /// </summary>
    private void UseOnNearest(string teamName, string ability, bool includeNeutral, Func<Entity, Entity, bool> filter)
    {
        if (FindTeam(teamName) is not { } team) return;
        var members = LiveMembers(team).ToArray();
        var carriers = Carriers(members, ability).ToArray();
        if (carriers.Length == 0) return;
        long sumX = 0, sumY = 0;
        foreach (var member in members) { sumX += member.X.Floor(); sumY += member.Y.Floor(); }
        var centreX = Fixed.FromInt(sumX / members.Length);
        var centreY = Fixed.FromInt(sumY / members.Length);
        var candidates = _world.Entities
            .Where(candidate => CountsAsObject(candidate) && !IsHiddenByStealth(candidate) && !carriers.Contains(candidate) && filter(carriers[0], candidate)
                                && _world.RelationshipOf(team.Owner, candidate.Owner) is var relation && (relation == Relationship.Enemies || (includeNeutral && relation == Relationship.Neutral)))
            .Select(candidate => (Entity: candidate, Distance: Distance2(candidate, centreX, centreY)))
            .OrderBy(item => item.Distance).ThenBy(item => item.Entity.Id.Value).Take(8).ToArray();
        foreach (var (target, _) in candidates)
        {
            if (!UseCommandButtonAbility(carriers[0], ability, SpecialPowerTargetKind.Object, target.Id, Fixed.Zero, Fixed.Zero)) continue;
            foreach (var other in carriers.Skip(1)) UseCommandButtonAbility(other, ability, SpecialPowerTargetKind.Object, target.Id, Fixed.Zero, Fixed.Zero);
            return;
        }
    }

    private static Fixed Distance2(Entity entity, Fixed x, Fixed y)
    {
        var dx = entity.X - x;
        var dy = entity.Y - y;
        return dx * dx + dy * dy;
    }

    /// <summary>
    /// <c>doTeamHuntWithCommandButton</c> + <c>CommandButtonHuntUpdate</c>: a member that has the module (every unit the data marks "allows command button hunt") starts hunting with the button.
    /// A weapon button hunts like <c>TEAM_HUNT</c> (the session's automatic combat does the shooting); a special-power button scans every <c>ScanRate</c> ticks while the object is
    /// idle for the nearest enemy within <c>ScanRange</c> and uses the power on it (<c>huntSpecialPower</c>, <c>scanClosestTarget</c>). Enter hunts (hijack, car bomb, sabotage) and every other
    /// button type are refused, as the reference does. A later script order for the object ends the hunt (<c>getLastCommandSource != CMD_FROM_AI</c>).
    /// </summary>
    private void HuntWithCommandButton(ScriptTeam team, string ability)
    {
        if (!_world.Templates.Commands.TryGetButton(ability, out var button)) { Diagnose($"unknown command button '{ability}'"); return; }
        var isPower = _world.Templates.Commands.TryGetSpecialPowerAbility(ability, out _);
        var isWeapon = button.Command.Equals("FIRE_WEAPON", StringComparison.OrdinalIgnoreCase) || button.Command.Equals("SWITCH_WEAPON", StringComparison.OrdinalIgnoreCase);
        if (!isPower && !isWeapon) { Diagnose($"team hunt with command button '{ability}': cannot hunt with command {button.Command}"); return; }
        foreach (var member in LiveMembers(team).ToArray())
        {
            if (member.Owner.IsSystem || !member.ModuleArray.OfType<CommandButtonHuntUpdate>().Any()) continue;
            if (isWeapon) { StartFollowing(member, HuntMode); continue; }
            _followers.Remove(member.Id.Value);
            _buttonHunts[member.Id.Value] = ability;
            _buttonHuntNext[member.Id.Value] = _tick;
        }
    }

    /// <summary>The scan of <see cref="HuntWithCommandButton"/>, once per tick for the hunting objects that are due.</summary>
    private void UpdateButtonHunts()
    {
        if (_buttonHunts.Count == 0) return;
        foreach (var (id, ability) in _buttonHunts.ToArray())
        {
            if (_world.FindEntity(new EntityId(id)) is not { DestroyRequested: false, EffectivelyDead: false } hunter) { _buttonHunts.Remove(id); _buttonHuntNext.Remove(id); continue; }
            if (_tick < _buttonHuntNext.GetValueOrDefault(id)) continue;
            var data = hunter.ModuleArray.OfType<CommandButtonHuntUpdate>().First().Data;
            _buttonHuntNext[id] = _tick + Math.Max(data.ScanTicks, SimulationTiming.LogicFramesPerSecond);
            if (hunter.HasMoveTarget || _busyThisTick.Contains(id)) continue;
            var range = data.ScanRange <= Fixed.Zero ? Fixed.FromInt(9999) : data.ScanRange;
            var limit = range * range;
            var candidates = _world.Entities
                .Where(candidate => CountsAsObject(candidate) && !candidate.Owner.IsSystem && !IsHiddenByStealth(candidate) && _world.AreEnemies(hunter, candidate) && Distance2(candidate, hunter.X, hunter.Y) <= limit)
                .OrderBy(candidate => Distance2(candidate, hunter.X, hunter.Y)).ThenBy(candidate => candidate.Id.Value).Take(8);
            foreach (var target in candidates)
                if (UseCommandButtonAbility(hunter, ability, SpecialPowerTargetKind.Object, target.Id, Fixed.Zero, Fixed.Zero)) break;
        }
    }

    // ------------------------------------------------------------------------------------------------ transports, tunnels, panic, facing

    /// <summary>
    /// <c>doLoadAllTransports</c>: the team's transports (<c>KINDOF_TRANSPORT</c>) and passengers are matched by slots (the reference solves a bin-packing problem; here a first-fit by
    /// id, largest passengers first) and each passenger is ordered to enter its transport.
    /// </summary>
    private void LoadTransports(ScriptTeam team)
    {
        var members = LiveMembers(team).ToArray();
        var transports = members.Where(member => member.Template.IsKindOf("TRANSPORT") && member.Container is not null).OrderBy(member => member.Id.Value).ToArray();
        if (transports.Length == 0) return;
        var free = transports.Select(transport => transport.Container!.ContainMax < 0 ? int.MaxValue : transport.Container.ContainMax - transport.Container.ContainCount).ToArray();
        foreach (var passenger in members.Where(member => !member.Template.IsKindOf("TRANSPORT") && member.Template.TransportSlotCount > 0)
                     .OrderByDescending(member => member.Template.TransportSlotCount).ThenBy(member => member.Id.Value))
        {
            for (var index = 0; index < transports.Length; index++)
            {
                if (free[index] < passenger.Template.TransportSlotCount) continue;
                free[index] -= passenger.Template.TransportSlotCount;
                Enter(passenger, transports[index]);
                break;
            }
        }
    }

    /// <summary><c>doMoveTeamTowardsNearest</c>: the nearest object of the type (or of any type of a list) inside the trigger area, measured from the team's centre; every member moves to it.</summary>
    private void MoveTowardsNearest(ScriptTeam team, string typeParameter, string areaName)
    {
        if (_program.FindArea(areaName) is not { } area) { Diagnose($"unknown trigger area '{areaName}'"); return; }
        var members = LiveMembers(team).Where(member => member.Template.Locomotor is not null).ToArray();
        if (members.Length == 0) return;
        long sumX = 0, sumY = 0;
        foreach (var member in members) { sumX += member.X.Floor(); sumY += member.Y.Floor(); }
        var centreX = Fixed.FromInt(sumX / members.Length);
        var centreY = Fixed.FromInt(sumY / members.Length);
        var best = _world.Entities.Where(candidate => CountsAsObject(candidate) && TypeMatches(typeParameter, candidate) && area.Contains(candidate.X, candidate.Y))
            .OrderBy(candidate => Distance2(candidate, centreX, centreY)).ThenBy(candidate => candidate.Id.Value).FirstOrDefault();
        if (best is null) return;
        foreach (var member in members) MoveTo(member, best.X, best.Y);
    }

    /// <summary><c>aiGuardTunnelNetwork</c>: the object goes to the nearest tunnel of its own player and enters it (it then guards from inside the network).</summary>
    private void GuardInTunnelNetwork(Entity member)
    {
        var tunnel = _world.Entities.Where(candidate => candidate.Owner == member.Owner && !candidate.EffectivelyDead && !candidate.DestroyRequested && candidate.Container is not null
                                                        && candidate.Template.Modules.Any(module => module.Name == "TunnelContain"))
            .OrderBy(candidate => Distance2(candidate, member.X, member.Y)).ThenBy(candidate => candidate.Id.Value).FirstOrDefault();
        if (tunnel is null) { Diagnose("guard in tunnel network without a tunnel"); return; }
        Enter(member, tunnel);
    }

    /// <summary><c>doTeamPanic</c>: the member runs the waypoint path from the waypoint closest to it (<c>getClosestWaypointOnPath</c>, then <c>aiPanic</c>); the panic locomotor set is not modelled.</summary>
    private void Panic(Entity member, ScriptWaypointPath path)
    {
        if (member.Template.Locomotor is null || member.Owner.IsSystem) return;
        var closest = 0;
        var bestDistance = Fixed.MaxValue;
        for (var index = 0; index < path.Waypoints.Length; index++)
        {
            var distance = Distance2(member, path.Waypoints[index].X, path.Waypoints[index].Y);
            if (distance >= bestDistance) continue;
            bestDistance = distance;
            closest = index;
        }
        _buttonHunts.Remove(member.Id.Value);
        _followers[member.Id.Value] = new Follower { Entity = member.Id.Value, Path = PathMode + path.Label, Index = closest };
        _completedPath.Remove(member.Id.Value);
    }

    /// <summary>
    /// <c>aiFacePosition</c>: the reference turns the object at its turn rate; the simulation sets the heading at once (the object stops first, as <c>clearWaypointQueue</c> + idle do).
    /// </summary>
    private void Face(Entity entity, Fixed x, Fixed y)
    {
        if (entity.Owner.IsSystem || entity.Template.Locomotor is null) return;
        Stop(entity);
        var dx = x - entity.X;
        var dy = y - entity.Y;
        if (dx == Fixed.Zero && dy == Fixed.Zero) return;
        entity.Angle = FixedTrig.Atan2(dy, dx);
        _world.MarkDirty(entity);
    }

    /// <summary><c>doNamedEnableStealth</c>: sets or clears <c>OBJECT_STATUS_SCRIPT_UNSTEALTHED</c>, which <c>StealthUpdate::allowedToStealth</c> reads; here the module keeps the switch.</summary>
    private void SetStealthEnabled(Entity entity, bool enabled)
    {
        foreach (var stealth in entity.ModuleArray.OfType<StealthUpdate>()) stealth.ScriptUnstealthed = !enabled;
        _world.MarkDirty(entity);
    }

    /// <summary>
    /// <c>doNamedSetUnmanned</c>: the object is disabled <c>DISABLED_UNMANNED</c> and turned over to the neutral player (the reference's visible effect); the simulation has no disabled
    /// state, but a neutral object never fires or takes orders, which is the same for gameplay. <c>DELETE_ALL_UNMANNED</c> later removes every object still unmanned.
    /// </summary>
    private void Unman(Entity entity)
    {
        if (entity.Owner.IsSystem) return;
        _followers.Remove(entity.Id.Value);
        _buttonHunts.Remove(entity.Id.Value);
        _world.TransferOwnership(entity.Id, PlayerId.System);
        _unmanned.Add(entity.Id.Value);
    }

    // ------------------------------------------------------------------------------------------------ state

    private void DescribeTeamAbilityState(IStateWriter writer)
    {
        DescribeRelations(writer, "relTeamTeam", _relTeamTeam);
        DescribeRelations(writer, "relTeamPlayer", _relTeamPlayer);
        DescribeRelations(writer, "relPlayerTeam", _relPlayerTeam);
        DescribePairs(writer, "teamAttitude", _teamAttitude);
        DescribePairs(writer, "teamWander", _teamWander);
        DescribePairs(writer, "rankLevel", _rankLevel);
        if (_rankLimit != -1) writer.Int("rankLimit", _rankLimit);
        if (_hulkLifetimeOverrideTicks != -1) writer.Int("hulkLifetimeOverride", _hulkLifetimeOverrideTicks);
        if (_held.Count > 0) { writer.BeginArray("held"); foreach (var id in _held) { writer.BeginObject(null); writer.UInt("id", id); writer.EndObject(); } writer.EndArray(); }
        if (_unmanned.Count > 0) { writer.BeginArray("unmanned"); foreach (var id in _unmanned) { writer.BeginObject(null); writer.UInt("id", id); writer.EndObject(); } writer.EndArray(); }
        if (_priorityDelta.Count > 0)
        {
            writer.BeginArray("priorityDelta");
            foreach (var (name, delta) in _priorityDelta) { writer.BeginObject(null); writer.Text("name", name); writer.Int("delta", delta); writer.EndObject(); }
            writer.EndArray();
        }
        if (_buttonHunts.Count > 0)
        {
            writer.BeginArray("buttonHunts");
            foreach (var (id, ability) in _buttonHunts) { writer.BeginObject(null); writer.UInt("id", id); writer.Text("ability", ability); writer.Int("next", _buttonHuntNext.GetValueOrDefault(id)); writer.EndObject(); }
            writer.EndArray();
        }

        static void DescribeRelations(IStateWriter writer, string name, SortedDictionary<long, int> table)
        {
            if (table.Count == 0) return;
            writer.BeginArray(name);
            foreach (var (key, relation) in table) { writer.BeginObject(null); writer.Int("first", (int)(key >> 32)); writer.Int("second", (int)(key & 0xFFFFFFFF)); writer.Int("relation", relation); writer.EndObject(); }
            writer.EndArray();
        }

        static void DescribePairs(IStateWriter writer, string name, SortedDictionary<int, int> table)
        {
            if (table.Count == 0) return;
            writer.BeginArray(name);
            foreach (var (key, value) in table) { writer.BeginObject(null); writer.Int("key", key); writer.Int("value", value); writer.EndObject(); }
            writer.EndArray();
        }
    }

    private void LoadTeamAbilityState(IStateReader reader)
    {
        LoadRelations(reader, "relTeamTeam", _relTeamTeam);
        LoadRelations(reader, "relTeamPlayer", _relTeamPlayer);
        LoadRelations(reader, "relPlayerTeam", _relPlayerTeam);
        LoadPairs(reader, "teamAttitude", _teamAttitude);
        LoadPairs(reader, "teamWander", _teamWander);
        LoadPairs(reader, "rankLevel", _rankLevel);
        _rankLimit = reader.Has("rankLimit") ? checked((int)reader.Int("rankLimit")) : -1;
        _hulkLifetimeOverrideTicks = reader.Has("hulkLifetimeOverride") ? reader.Int("hulkLifetimeOverride") : -1;
        _held.Clear();
        if (reader.Has("held")) foreach (var item in reader.Array("held")) _held.Add(checked((uint)item.UInt("id")));
        _unmanned.Clear();
        if (reader.Has("unmanned")) foreach (var item in reader.Array("unmanned")) _unmanned.Add(checked((uint)item.UInt("id")));
        _priorityDelta.Clear();
        if (reader.Has("priorityDelta")) foreach (var item in reader.Array("priorityDelta")) _priorityDelta[item.Text("name")] = checked((int)item.Int("delta"));
        _buttonHunts.Clear();
        _buttonHuntNext.Clear();
        if (reader.Has("buttonHunts"))
            foreach (var item in reader.Array("buttonHunts"))
            {
                var id = checked((uint)item.UInt("id"));
                _buttonHunts[id] = item.Text("ability");
                _buttonHuntNext[id] = item.Int("next");
            }
        _entityTeams = null;
        _world.RelationshipOverride = _relTeamTeam.Count + _relTeamPlayer.Count + _relPlayerTeam.Count > 0 ? OverrideRelationship : null;

        static void LoadRelations(IStateReader reader, string name, SortedDictionary<long, int> table)
        {
            table.Clear();
            if (reader.Has(name))
                foreach (var item in reader.Array(name)) table[Key(checked((int)item.Int("first")), checked((int)item.Int("second")))] = checked((int)item.Int("relation"));
        }

        static void LoadPairs(IStateReader reader, string name, SortedDictionary<int, int> table)
        {
            table.Clear();
            if (reader.Has(name)) foreach (var item in reader.Array(name)) table[checked((int)item.Int("key"))] = checked((int)item.Int("value"));
        }
    }
}
