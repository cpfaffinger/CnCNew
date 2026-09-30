using System.Collections.Immutable;
using CnCNew.Data.World;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Scripting;

public sealed partial class ScriptEngine
{
    private const string HuntMode = "hunt";
    private const string PathMode = "path:";
    private const string NamedMode = "named:";
    private const string TeamMode = "team:";
    private const string AreaMode = "area:";
    private const string PositionMode = "position:";
    private const long RetargetInterval = 30;
    private static readonly Fixed ArrivalDistanceSquared = Fixed.FromInt(40 * 40);

    /// <summary><c>ScriptEngine::executeActions</c> / <c>ScriptActions::executeAction</c> for one action; see <see cref="ScriptCoverage"/> for what each type does.</summary>
    private void ExecuteAction(MapScriptAction action)
    {
        var p = action.Parameters;
        var type = action.TypeName;
        if (ScriptCoverage.ForwardedActions.Contains(type))
        {
            Forward(action);
            return;
        }
        switch (type)
        {
            case "NO_OP": break;

            // ---- engine state (ScriptEngine.cpp: setFlag, setCounter, addCounter, subCounter, setTimer, adjustTimer, pauseTimer, restartTimer, enable/disableScript, callSubroutine)
            case "SET_FLAG":
                if (Has(type, p, 2)) _flags[Str(p, 0)] = Int(p, 1) != 0;
                break;
            case "SET_COUNTER":
                if (Has(type, p, 2)) CounterFor(Str(p, 0)).Value = Int(p, 1);
                break;
            case "INCREMENT_COUNTER":
                if (Has(type, p, 2)) CounterFor(Str(p, 1)).Value += Int(p, 0);
                break;
            case "DECREMENT_COUNTER":
                if (Has(type, p, 2)) CounterFor(Str(p, 1)).Value -= Int(p, 0);
                break;
            case "SET_TIMER":
            case "SET_RANDOM_TIMER":
                if (Has(type, p, type == "SET_RANDOM_TIMER" ? 3 : 2))
                {
                    var frames = type == "SET_RANDOM_TIMER" ? _world.LogicRandom.Range(Int(p, 1), Int(p, 2)) : Int(p, 1);
                    var timer = CounterFor(Str(p, 0));
                    timer.Value = frames;
                    timer.IsTimer = true;
                }
                break;
            case "SET_MILLISECOND_TIMER":
            case "SET_RANDOM_MSEC_TIMER":
                if (Has(type, p, type == "SET_RANDOM_MSEC_TIMER" ? 3 : 2))
                {
                    // The reference draws GameLogicRandomValue(value, randomValue) over the real seconds; the fixed-point range draws the same way.
                    var seconds = type == "SET_RANDOM_MSEC_TIMER" ? _world.LogicRandom.Range(Real(p, 1), Real(p, 2)) : Real(p, 1);
                    var timer = CounterFor(Str(p, 0));
                    timer.Value = SecondsToFrames(seconds);
                    timer.IsTimer = true;
                }
                break;
            case "STOP_TIMER":
                if (Has(type, p, 1)) CounterFor(Str(p, 0)).IsTimer = false;
                break;
            case "RESTART_TIMER":
                if (Has(type, p, 1) && CounterFor(Str(p, 0)) is { Value: > 0 } restart) restart.IsTimer = true;
                break;
            case "ADD_TO_MSEC_TIMER":
                if (Has(type, p, 2)) CounterFor(Str(p, 1)).Value += SecondsToFrames(Real(p, 0));
                break;
            case "SUB_FROM_MSEC_TIMER":
                if (Has(type, p, 2)) CounterFor(Str(p, 1)).Value -= SecondsToFrames(Real(p, 0));
                break;
            case "ENABLE_SCRIPT":
            case "DISABLE_SCRIPT":
                if (Has(type, p, 1)) SetScriptActive(Str(p, 0), type == "ENABLE_SCRIPT");
                break;
            case "CALL_SUBROUTINE":
                if (Has(type, p, 1)) CallSubroutine(Str(p, 0));
                break;
            case "OBJECTLIST_ADDOBJECTTYPE":
                if (Has(type, p, 2))
                {
                    if (!_objectLists.TryGetValue(Str(p, 0), out var list)) _objectLists[Str(p, 0)] = list = new SortedSet<string>(StringComparer.Ordinal);
                    list.Add(Str(p, 1));
                }
                break;
            case "OBJECTLIST_REMOVEOBJECTTYPE":
                // doObjectTypeListMaintenance: removing the last type removes the list.
                if (Has(type, p, 2) && _objectLists.TryGetValue(Str(p, 0), out var shrinking))
                {
                    shrinking.Remove(Str(p, 1));
                    if (shrinking.Count == 0) _objectLists.Remove(Str(p, 0));
                }
                break;

            // ---- outcome
            case "VICTORY": End(MissionOutcome.Victory, EndGameDelayTicks); break;
            case "DEFEAT": End(MissionOutcome.Defeat, EndGameDelayTicks); break;
            case "QUICKVICTORY": End(MissionOutcome.Victory, 1); break;

            // ---- players
            case "PLAYER_SET_MONEY":
                if (Has(type, p, 2) && ResolvePlayer(Str(p, 0)) is { } setter && _world.HasPlayer(setter.Id)) _world.SetMoney(setter.Id, Fixed.FromInt(Int(p, 1)));
                break;
            case "PLAYER_GIVE_MONEY":
                if (Has(type, p, 2) && ResolvePlayer(Str(p, 0)) is { } giver && _world.HasPlayer(giver.Id))
                {
                    var amount = Int(p, 1);
                    if (amount < 0) _world.WithdrawMoney(giver.Id, Fixed.FromInt(-amount));
                    else _world.AddMoney(giver.Id, Fixed.FromInt(amount));
                }
                break;
            case "PLAYER_RELATES_PLAYER":
                // updatePlayerRelationTowardPlayer(param 0, relation = param 2, param 1): the first player's view of the second, one direction only.
                if (Has(type, p, 3) && ResolvePlayer(Str(p, 0)) is { } viewer && ResolvePlayer(Str(p, 1)) is { } viewed && Enum.IsDefined((Relationship)Int(p, 2)))
                    _world.SetRelationship(viewer.Id, viewed.Id, (Relationship)Int(p, 2));
                break;
            case "PLAYER_GRANT_SCIENCE":
                if (Has(type, p, 2) && ResolvePlayer(Str(p, 0)) is { } learner && _world.HasPlayer(learner.Id))
                {
                    if (_world.Sciences.TryGet(Str(p, 1), out _)) _world.GrantScience(learner.Id, Str(p, 1));
                    else Diagnose($"unknown science '{Str(p, 1)}'");
                }
                break;
            case "PLAYER_KILL":
                if (Has(type, p, 1) && ResolvePlayer(Str(p, 0)) is { } victim)
                    foreach (var entity in PlayerEntities(victim.Id).ToArray()) _world.Kill(entity.Id);
                break;
            case "PLAYER_TRANSFER_OWNERSHIP_PLAYER":
                if (Has(type, p, 2) && ResolvePlayer(Str(p, 0)) is { } source && ResolvePlayer(Str(p, 1)) is { } target)
                    foreach (var entity in PlayerEntities(source.Id).ToArray()) _world.TransferOwnership(entity.Id, target.Id);
                break;
            case "NAMED_TRANSFER_OWNERSHIP_PLAYER":
                if (Has(type, p, 2) && NamedEntity(Str(p, 0)) is { } transferred && ResolvePlayer(Str(p, 1)) is { } newOwner) _world.TransferOwnership(transferred.Id, newOwner.Id);
                break;
            case "TEAM_TRANSFER_TO_PLAYER":
                if (Has(type, p, 2) && FindTeam(Str(p, 0)) is { } moved && ResolvePlayer(Str(p, 1)) is { } receiver)
                {
                    moved.Owner = receiver.Id;
                    foreach (var member in LiveMembers(moved).ToArray()) _world.TransferOwnership(member.Id, receiver.Id);
                }
                break;

            // ---- objects and teams
            case "NAMED_KILL":
                if (Has(type, p, 1) && NamedEntity(Str(p, 0)) is { } killed) _world.Kill(killed.Id);
                break;
            case "TEAM_KILL":
                if (Has(type, p, 1))
                    foreach (var team in TeamsNamed(Str(p, 0)))
                        foreach (var member in LiveMembers(team).ToArray()) _world.Kill(member.Id);
                break;
            case "NAMED_DELETE":
                if (Has(type, p, 1) && NamedEntity(Str(p, 0)) is { } deleted) _world.RequestDestroy(deleted.Id);
                break;
            case "TEAM_DELETE":
            case "TEAM_DELETE_LIVING":
                if (Has(type, p, 1))
                    foreach (var team in TeamsNamed(Str(p, 0)))
                        foreach (var member in LiveMembers(team).ToArray())
                            if (type == "TEAM_DELETE" || !member.EffectivelyDead) _world.RequestDestroy(member.Id);
                break;
            case "NAMED_DAMAGE":
                if (Has(type, p, 2) && NamedEntity(Str(p, 0)) is { } hurt)
                    _world.Damage(hurt.Id, new DamageInfo(EntityId.Invalid, Fixed.FromInt(Int(p, 1)), DamageTypes.Unresistable, DeathTypes.Normal, false));
                break;
            case "DAMAGE_MEMBERS_OF_TEAM":
                if (Has(type, p, 2))
                    foreach (var team in TeamsNamed(Str(p, 0)))
                        foreach (var member in LiveMembers(team).ToArray())
                            _world.Damage(member.Id, new DamageInfo(EntityId.Invalid, Real(p, 1), DamageTypes.Unresistable, DeathTypes.Normal, false));
                break;
            case "CREATE_OBJECT":
                // doCreateObject(unnamed, param 0 = type, param 1 = team, param 2 = position, param 3 = angle).
                if (Has(type, p, 4))
                    SpawnOnTeam(string.Empty, Str(p, 0), Str(p, 1), Fixed.FromDecimal(p[2].CoordX), Fixed.FromDecimal(p[2].CoordY), Fixed.FromDecimal(p[2].CoordZ), Real(p, 3));
                break;
            case "UNIT_SPAWN_NAMED_LOCATION_ORIENTATION":
                if (Has(type, p, 5))
                    SpawnOnTeam(Str(p, 0), Str(p, 1), Str(p, 2), Fixed.FromDecimal(p[3].CoordX), Fixed.FromDecimal(p[3].CoordY), Fixed.FromDecimal(p[3].CoordZ), Real(p, 4));
                break;
            case "CREATE_NAMED_ON_TEAM_AT_WAYPOINT":
                if (Has(type, p, 4)) SpawnAtWaypoint(Str(p, 0), Str(p, 1), Str(p, 2), Str(p, 3));
                break;
            case "CREATE_UNNAMED_ON_TEAM_AT_WAYPOINT":
                if (Has(type, p, 3)) SpawnAtWaypoint(string.Empty, Str(p, 0), Str(p, 1), Str(p, 2));
                break;

            // ---- attack priorities (AttackPriorityInfo, ScriptEngine::setPriorityThing/Kind/Default, updateNamed/TeamAttackPrioritySet)
            case "SET_ATTACK_PRIORITY_THING":
                if (Has(type, p, 3))
                {
                    var set = PrioritySetFor(Str(p, 0));
                    var types = _objectLists.TryGetValue(Str(p, 1), out var list) ? list.ToArray() : [Str(p, 1)];
                    foreach (var name in types)
                    {
                        if (!_world.Templates.TryGet(name, out _)) { Diagnose($"attack priority on unknown type '{name}'"); continue; }
                        set.Rules.Add((false, name, Int(p, 2)));
                    }
                }
                break;
            case "SET_ATTACK_PRIORITY_KIND_OF":
                if (Has(type, p, 3)) PrioritySetFor(Str(p, 0)).Rules.Add((true, Str(p, 1), Int(p, 2)));
                break;
            case "SET_DEFAULT_ATTACK_PRIORITY":
                if (Has(type, p, 2)) PrioritySetFor(Str(p, 0)).Default = Int(p, 1);
                break;
            case "NAMED_APPLY_ATTACK_PRIORITY_SET":
                if (Has(type, p, 2) && NamedEntity(Str(p, 0)) is { } prioritized)
                {
                    if (_prioritySets.ContainsKey(Str(p, 1))) _prioritySetOf[prioritized.Id.Value] = Str(p, 1);
                    else Diagnose($"unknown attack priority set '{Str(p, 1)}'");
                }
                break;
            case "TEAM_APPLY_ATTACK_PRIORITY_SET":
                if (Has(type, p, 2))
                {
                    if (!_prioritySets.ContainsKey(Str(p, 1))) { Diagnose($"unknown attack priority set '{Str(p, 1)}'"); break; }
                    foreach (var team in TeamsNamed(Str(p, 0)))
                    {
                        team.AttackPriority = Str(p, 1);
                        foreach (var member in LiveMembers(team)) _prioritySetOf[member.Id.Value] = Str(p, 1);
                    }
                }
                break;
            case "CREATE_REINFORCEMENT_TEAM":
                if (Has(type, p, 2)) CreateReinforcements(Str(p, 0), Str(p, 1));
                break;

            // ---- orders (player commands; see OrderMove)
            case "MOVE_NAMED_UNIT_TO":
                if (Has(type, p, 2) && NamedEntity(Str(p, 0)) is { } walker && WaypointOrDiagnose(Str(p, 1)) is { } spot) MoveTo(walker, spot.X, spot.Y);
                break;
            case "MOVE_TEAM_TO":
                if (Has(type, p, 2) && WaypointOrDiagnose(Str(p, 1)) is { } destination)
                    foreach (var team in TeamsNamed(Str(p, 0)))
                        foreach (var member in LiveMembers(team).ToArray()) MoveTo(member, destination.X, destination.Y);
                break;
            case "TEAM_FOLLOW_WAYPOINTS":
            case "TEAM_FOLLOW_WAYPOINTS_EXACT":
                if (Has(type, p, 2))
                    foreach (var team in TeamsNamed(Str(p, 0))) foreach (var member in LiveMembers(team).ToArray()) StartFollowing(member, PathMode + Str(p, 1));
                break;
            case "NAMED_FOLLOW_WAYPOINTS":
            case "NAMED_FOLLOW_WAYPOINTS_EXACT":
                if (Has(type, p, 2) && NamedEntity(Str(p, 0)) is { } follower) StartFollowing(follower, PathMode + Str(p, 1));
                break;
            case "NAMED_STOP":
                if (Has(type, p, 1) && NamedEntity(Str(p, 0)) is { } stopped) Stop(stopped);
                break;
            case "TEAM_STOP":
            case "TEAM_STOP_AND_DISBAND":
            case "TEAM_GUARD":
                if (Has(type, p, 1))
                    foreach (var team in TeamsNamed(Str(p, 0)))
                        foreach (var member in LiveMembers(team).ToArray()) Stop(member);
                break;
            case "NAMED_GUARD":
                if (Has(type, p, 1) && NamedEntity(Str(p, 0)) is { } guard) Stop(guard);
                break;
            case "TEAM_GUARD_POSITION":
                if (Has(type, p, 2))
                    foreach (var team in TeamsNamed(Str(p, 0)))
                        foreach (var member in LiveMembers(team).ToArray()) StartFollowing(member, PositionMode + Str(p, 1));
                break;
            case "TEAM_GUARD_OBJECT":
            case "TEAM_ATTACK_NAMED":
                if (Has(type, p, 2))
                    foreach (var team in TeamsNamed(Str(p, 0)))
                        foreach (var member in LiveMembers(team).ToArray()) StartFollowing(member, NamedMode + Str(p, 1));
                break;
            case "TEAM_GUARD_AREA":
            case "TEAM_ATTACK_AREA":
                if (Has(type, p, 2))
                    foreach (var team in TeamsNamed(Str(p, 0)))
                        foreach (var member in LiveMembers(team).ToArray()) StartFollowing(member, AreaMode + Str(p, 1));
                break;
            case "TEAM_ATTACK_TEAM":
                if (Has(type, p, 2))
                    foreach (var team in TeamsNamed(Str(p, 0)))
                        foreach (var member in LiveMembers(team).ToArray()) StartFollowing(member, TeamMode + Str(p, 1));
                break;
            case "NAMED_ATTACK_NAMED":
                if (Has(type, p, 2) && NamedEntity(Str(p, 0)) is { } attacker) StartFollowing(attacker, NamedMode + Str(p, 1));
                break;
            case "NAMED_ATTACK_TEAM":
                if (Has(type, p, 2) && NamedEntity(Str(p, 0)) is { } teamAttacker) StartFollowing(teamAttacker, TeamMode + Str(p, 1));
                break;
            case "NAMED_ATTACK_AREA":
                if (Has(type, p, 2) && NamedEntity(Str(p, 0)) is { } areaAttacker) StartFollowing(areaAttacker, AreaMode + Str(p, 1));
                break;
            case "NAMED_HUNT":
                if (Has(type, p, 1) && NamedEntity(Str(p, 0)) is { } hunter) StartFollowing(hunter, HuntMode);
                break;
            case "TEAM_HUNT":
                if (Has(type, p, 1))
                    foreach (var team in TeamsNamed(Str(p, 0)))
                        foreach (var member in LiveMembers(team).ToArray()) StartFollowing(member, HuntMode);
                break;
            case "PLAYER_HUNT":
                if (Has(type, p, 1) && ResolvePlayer(Str(p, 0)) is { } hunting)
                    foreach (var entity in PlayerEntities(hunting.Id).Where(value => !value.Template.IsKindOf("STRUCTURE") && value.Template.Locomotor is not null && value.Template.DefaultWeapon >= 0).ToArray())
                        StartFollowing(entity, HuntMode);
                break;

            default:
                _missingActions[type] = _missingActions.GetValueOrDefault(type) + 1;
                return;
        }
        _executedActions++;
    }

    private static long SecondsToFrames(Fixed seconds)
    {
        // REAL_TO_INT_CEIL(ConvertDurationFromMsecsToFrames(value * 1000)): seconds times the logic rate, rounded up.
        var frames = seconds * SimulationTiming.LogicFramesPerSecond;
        return frames.Ceiling();
    }

    private void SetScriptActive(string name, bool active)
    {
        // enableScript/disableScript touch both a group and a script of that name when both exist.
        if (_program.FindGroup(name) is { } group) _groupActive[group.Index] = active;
        if (_program.FindScript(name) is { } script) _scriptActive[script.Index] = active;
    }

    private void End(MissionOutcome outcome, long delay)
    {
        Outcome = outcome;
        if (OutcomeTick < 0)
        {
            OutcomeTick = _tick;
            OutcomeScript = _runningScript;
        }
        _endTimer = delay;
        Output(outcome == MissionOutcome.Victory ? "VICTORY" : "DEFEAT");
    }

    /// <summary>Hands a presentation action to the client as a <see cref="ScriptOutput"/>; speech/sound/movie/camera actions also count as playing for the configured latency.</summary>
    private void Forward(MapScriptAction action)
    {
        _forwardedActions++;
        var arguments = new string[action.Parameters.Length];
        for (var index = 0; index < arguments.Length; index++)
        {
            var parameter = action.Parameters[index];
            arguments[index] = parameter.Type == MapScriptParameterType.Coord3D ? $"{parameter.CoordX},{parameter.CoordY},{parameter.CoordZ}"
                : parameter.Text.Length > 0 ? parameter.Text
                : parameter.Type is MapScriptParameterType.Real or MapScriptParameterType.Angle or MapScriptParameterType.Percent ? parameter.Real.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : parameter.Integer.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        _outputs.Add(new ScriptOutput(_tick, action.TypeName, [..arguments]));
        if (_options.PresentationLatencyTicks <= 0) return;
        var key = action.TypeName switch
        {
            "SPEECH_PLAY" or "SOUND_PLAY_NAMED" or "MOVIE_PLAY_FULLSCREEN" or "MOVIE_PLAY_RADAR" or "MUSIC_SET_TRACK" when arguments.Length > 0 => arguments[0],
            "MOVE_CAMERA_TO" or "MOVE_CAMERA_ALONG_WAYPOINT_PATH" or "ROTATE_CAMERA" or "RESET_CAMERA" or "MOVE_CAMERA_TO_SELECTION" or "CAMERA_MOVE_HOME" => "<camera>",
            _ => null,
        };
        if (key is not null) _pendingPresentation[key] = _tick + _options.PresentationLatencyTicks;
    }

    // ------------------------------------------------------------------------------------------------ spawning

    private ScriptWaypoint? WaypointOrDiagnose(string name)
    {
        var waypoint = _program.FindWaypoint(name);
        if (waypoint is null) Diagnose($"unknown waypoint '{name}'");
        return waypoint;
    }

    private void SpawnAtWaypoint(string unitName, string typeName, string teamName, string waypointName)
    {
        if (WaypointOrDiagnose(waypointName) is not { } waypoint) return;
        SpawnOnTeam(unitName, typeName, teamName, waypoint.X, waypoint.Y, waypoint.Z, Fixed.Zero);
    }

    /// <summary><c>doCreateObject</c>/<c>createUnitOnTeamAt</c>: a new object of the team's owner, joined to the team (created if needed), optionally named; a live object that already has the name blocks the creation.</summary>
    private void SpawnOnTeam(string unitName, string typeName, string teamName, Fixed x, Fixed y, Fixed z, Fixed angle)
    {
        if (unitName.Length > 0 && NamedEntity(unitName) is { EffectivelyDead: false })
        {
            Diagnose($"create of already existing '{unitName}'");
            return;
        }
        var team = FindTeam(teamName) ?? CreateTeam(teamName);
        if (team is null) return;
        if (!_world.Templates.TryGet(typeName, out var template)) { Diagnose($"unknown object type '{typeName}'"); return; }
        if (!template.IsInstantiable) { Diagnose($"not instantiable '{typeName}'"); return; }
        var id = _world.RequestSpawn(template.Index, team.Owner, x, y, z, angle);
        team.Members.Add(id.Value);
        team.Created = true;
        if (team.AttackPriority.Length > 0) _prioritySetOf[id.Value] = team.AttackPriority;
        _world.MaterializePendingSpawns();
        if (unitName.Length > 0) RegisterNamed(unitName, id);
    }

    private PrioritySet PrioritySetFor(string name)
    {
        if (!_prioritySets.TryGetValue(name, out var set)) _prioritySets[name] = set = new PrioritySet();
        return set;
    }

    /// <summary>
    /// <c>ScriptActions::doCreateReinforcements</c>, without the transport: the team's units (<c>teamUnitMaxCount</c> of each type) appear at the team's
    /// reinforcement origin (or at the destination) in a row and, when they came from an origin, walk to the destination. A team with a transport
    /// (<c>teamTransport</c>, <c>teamStartsFull</c>) would arrive inside it; the transport and its loading are not simulated, which is counted as
    /// <c>reinforcement transport not simulated</c> (<c>MAP-SCRIPTS.md</c> §8).
    /// </summary>
    private void CreateReinforcements(string teamName, string destinationName)
    {
        if (WaypointOrDiagnose(destinationName) is not { } destination) return;
        var prototype = _program.FindTeam(teamName);
        if (prototype is null) { Diagnose($"unknown team '{teamName}'"); return; }
        var origin = prototype.ReinforcementOrigin.Length > 0 ? _program.FindWaypoint(prototype.ReinforcementOrigin) : null;
        var start = origin ?? destination;
        var team = CreateTeam(teamName);
        if (team is null) return;
        if (prototype.Transport.Length > 0) Diagnose($"reinforcement transport not simulated '{prototype.Transport}'");
        var row = Fixed.Zero;
        foreach (var unit in prototype.Units)
        {
            if (!_world.Templates.TryGet(unit.Type, out var template)) { Diagnose($"unknown object type '{unit.Type}'"); continue; }
            if (!template.IsInstantiable) { Diagnose($"not instantiable '{unit.Type}'"); continue; }
            for (var index = 0; index < unit.MaxCount; index++)
            {
                // "pos.x = origin.x + 2.25 * j * majorRadius": a row along x, one row per unit type along y.
                var x = start.X + Fixed.FromInt(9) * index / Fixed.FromInt(4) * template.GeometryMajorRadius;
                var id = _world.RequestSpawn(template.Index, team.Owner, x, start.Y + row, start.Z, Fixed.Zero);
                team.Members.Add(id.Value);
                if (team.AttackPriority.Length > 0) _prioritySetOf[id.Value] = team.AttackPriority;
                if (origin is not null && (origin.X != destination.X || origin.Y != destination.Y)) _followers[id.Value] = new Follower { Entity = id.Value, Path = PositionMode + destinationName, Index = 0 };
            }
            row += template.GeometryMajorRadius * 2;
        }
        team.Created = true;
        _world.MaterializePendingSpawns();
    }

    // ------------------------------------------------------------------------------------------------ orders

    private void MoveTo(Entity entity, Fixed x, Fixed y)
    {
        if (entity.Owner.IsSystem) { Diagnose("order for a neutral object"); return; }
        if (entity.Template.Locomotor is null) return;
        var owner = entity.Owner;
        var id = entity.Id;
        if (!_issue(owner, (sequence, tick) => new SetMoveTargetCommand(owner, sequence, tick, id, x, y)).Accepted) Diagnose("order rejected");
    }

    private void Stop(Entity entity)
    {
        _followers.Remove(entity.Id.Value);
        if (entity.Template.Locomotor is null || entity.Owner.IsSystem) return;
        MoveTo(entity, entity.X, entity.Y);
    }

    private void StartFollowing(Entity entity, string mode)
    {
        if (entity.Template.Locomotor is null || entity.Owner.IsSystem) return;
        _followers[entity.Id.Value] = new Follower { Entity = entity.Id.Value, Path = mode, Index = 0 };
        _completedPath.Remove(entity.Id.Value);
    }

    /// <summary>
    /// Advances every standing order (waypoint paths, hunts, attacks, guard positions). A path is followed waypoint by waypoint and looped when the path's last
    /// link returns to its first waypoint; hunts, attacks and area orders move toward the current target every <see cref="RetargetInterval"/> ticks and let
    /// the combat driver of the session open fire in range (<c>MAP-SCRIPTS.md</c> §8: the simulation has no attack-move AI state).
    /// </summary>
    private void UpdateFollowers()
    {
        if (_followers.Count == 0) return;
        foreach (var follower in _followers.Values.ToArray())
        {
            var entity = _world.FindEntity(new EntityId(follower.Entity));
            if (entity is null || entity.DestroyRequested || entity.EffectivelyDead) { _followers.Remove(follower.Entity); continue; }
            var mode = follower.Path;
            if (mode.StartsWith(PathMode, StringComparison.Ordinal))
            {
                if (_program.FindPath(mode[PathMode.Length..]) is not { } path || path.Waypoints.IsDefaultOrEmpty) { Diagnose($"unknown waypoint path '{mode[PathMode.Length..]}'"); _followers.Remove(follower.Entity); continue; }
                var waypoint = path.Waypoints[Math.Min(follower.Index, path.Waypoints.Length - 1)];
                if (DistanceSquared(entity, waypoint.X, waypoint.Y) <= ArrivalDistanceSquared)
                {
                    follower.Index++;
                    if (follower.Index >= path.Waypoints.Length)
                    {
                        if (path.Loops) follower.Index = 0;
                        else
                        {
                            _completedPath[follower.Entity] = path.Label;
                            _followers.Remove(follower.Entity);
                            continue;
                        }
                    }
                    waypoint = path.Waypoints[follower.Index];
                }
                if (!entity.HasMoveTarget) MoveTo(entity, waypoint.X, waypoint.Y);
                continue;
            }
            if (_tick % RetargetInterval != follower.Entity % RetargetInterval) continue;
            if (mode.StartsWith(PositionMode, StringComparison.Ordinal))
            {
                if (WaypointOrDiagnose(mode[PositionMode.Length..]) is { } spot)
                {
                    if (DistanceSquared(entity, spot.X, spot.Y) <= ArrivalDistanceSquared) _followers.Remove(follower.Entity);
                    else if (!entity.HasMoveTarget) MoveTo(entity, spot.X, spot.Y);
                }
                else _followers.Remove(follower.Entity);
                continue;
            }
            (Fixed X, Fixed Y)? target = null;
            if (mode == HuntMode) target = NearestEnemy(entity) is { } enemy ? (enemy.X, enemy.Y) : null;
            else if (mode.StartsWith(NamedMode, StringComparison.Ordinal)) target = NamedEntity(mode[NamedMode.Length..]) is { EffectivelyDead: false } named ? (named.X, named.Y) : null;
            else if (mode.StartsWith(TeamMode, StringComparison.Ordinal)) target = NearestOfTeam(entity, mode[TeamMode.Length..]);
            else if (mode.StartsWith(AreaMode, StringComparison.Ordinal)) target = AreaCentre(mode[AreaMode.Length..]);
            if (target is not { } goal)
            {
                if (mode != HuntMode) _followers.Remove(follower.Entity);
                continue;
            }
            if (DistanceSquared(entity, goal.X, goal.Y) > ArrivalDistanceSquared) MoveTo(entity, goal.X, goal.Y);
        }
    }

    private static Fixed DistanceSquared(Entity entity, Fixed x, Fixed y)
    {
        var dx = entity.X - x;
        var dy = entity.Y - y;
        return dx * dx + dy * dy;
    }

    /// <summary>The enemy a hunting object heads for: highest attack priority first, then the nearest (<c>AI::findClosestEnemy</c> with the priority info).</summary>
    private Entity? NearestEnemy(Entity from)
    {
        Entity? best = null;
        var bestPriority = int.MinValue;
        var bestDistance = Fixed.MaxValue;
        foreach (var candidate in _world.Entities.OrderBy(value => value.Id.Value))
        {
            if (candidate.Owner.IsSystem || !CountsAsObject(candidate) || !_world.AreEnemies(from.Owner, candidate.Owner)) continue;
            var priority = AttackPriority(from, candidate);
            var distance = DistanceSquared(from, candidate.X, candidate.Y);
            if (priority < bestPriority || (priority == bestPriority && distance >= bestDistance)) continue;
            best = candidate;
            bestPriority = priority;
            bestDistance = distance;
        }
        return best;
    }

    private (Fixed X, Fixed Y)? NearestOfTeam(Entity from, string prototype)
    {
        (Fixed X, Fixed Y)? best = null;
        var bestDistance = Fixed.MaxValue;
        foreach (var team in TeamsNamed(prototype))
            foreach (var member in LiveMembers(team))
            {
                if (member.EffectivelyDead) continue;
                var distance = DistanceSquared(from, member.X, member.Y);
                if (distance >= bestDistance) continue;
                best = (member.X, member.Y);
                bestDistance = distance;
            }
        return best;
    }

    private (Fixed X, Fixed Y)? AreaCentre(string name)
    {
        if (_program.FindArea(name) is not { Points.Length: > 0 } area) { Diagnose($"unknown trigger area '{name}'"); return null; }
        long x = 0, y = 0;
        foreach (var (px, py) in area.Points) { x += px; y += py; }
        return (Fixed.FromInt(x / area.Points.Length), Fixed.FromInt(y / area.Points.Length));
    }
}
