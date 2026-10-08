using System.Collections.Immutable;
using CnCNew.Data.World;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Scripting;

public sealed partial class ScriptEngine
{
    private static string Str(ImmutableArray<MapScriptParameter> parameters, int index) => index < parameters.Length ? parameters[index].Text : string.Empty;
    private static int Int(ImmutableArray<MapScriptParameter> parameters, int index) => index < parameters.Length ? parameters[index].Integer : 0;
    private static Fixed Real(ImmutableArray<MapScriptParameter> parameters, int index) => index < parameters.Length ? Fixed.FromDecimal(parameters[index].Real) : Fixed.Zero;

    /// <summary>Whether the parameter list is long enough; a short list (an old World Builder file) is counted and the condition/action does nothing instead of guessing.</summary>
    private bool Has(string typeName, ImmutableArray<MapScriptParameter> parameters, int count)
    {
        if (parameters.Length >= count) return true;
        Diagnose($"{typeName}: {parameters.Length} of {count} parameters");
        return false;
    }

    /// <summary><c>ScriptEngine::evaluateCondition</c> and <c>ScriptConditions::evaluateCondition</c>: the engine-level types first, then the world-bound ones.</summary>
    private bool EvaluateCondition(MapCondition condition)
    {
        if (Observer is not { } observer) return EvaluateConditionCore(condition);
        observer.ConditionStarted(condition);
        try { return EvaluateConditionCore(condition); }
        finally { observer.ConditionEnded(condition); }
    }

    private bool EvaluateConditionCore(MapCondition condition)
    {
        var p = condition.Parameters;
        var type = condition.TypeName;
        switch (type)
        {
            case "CONDITION_FALSE": return false;
            case "CONDITION_TRUE": return true;
            case "COUNTER":
                // evaluateCounter: counter <op> value.
                return Has(type, p, 3) && Compare(Int(p, 1), CounterFor(Str(p, 0)).Value, Int(p, 2));
            case "FLAG":
            {
                if (!Has(type, p, 2)) return false;
                if ((Int(p, 1) != 0) == _flags.GetValueOrDefault(Str(p, 0))) return true;
                return _uiInteractions.Contains(Str(p, 0));
            }
            case "TIMER_EXPIRED":
            {
                // evaluateTimer: a timer that was never started is not expired; started ones count down to -1.
                if (!Has(type, p, 1)) return false;
                var counter = CounterFor(Str(p, 0));
                return counter.IsTimer && counter.Value < 1;
            }

            // ---- players
            case "PLAYER_ALL_DESTROYED":
                return !Has(type, p, 1) || ResolvePlayer(Str(p, 0)) is not { } allGone || !PlayerHasAnyObject(allGone.Id);
            case "PLAYER_ALL_BUILDFACILITIES_DESTROYED":
                return !Has(type, p, 1) || ResolvePlayer(Str(p, 0)) is not { } noFacility || !PlayerHasBuildFacility(noFacility.Id);
            case "PLAYER_HAS_CREDITS":
            {
                // evaluatePlayerHasCredits compares the parameter against the balance, parameter on the left.
                if (!Has(type, p, 3) || ResolvePlayer(Str(p, 2)) is not { } rich) return false;
                return Compare(Int(p, 1), Int(p, 0), _world.MoneyOf(rich.Id).Floor());
            }
            case "PLAYER_HAS_N_OR_FEWER_BUILDINGS":
                // The player parameter comes first, the count second (ScriptConditions.cpp: evaluatePlayerHasNOrFewerBuildings(param 1, param 0)).
                return Has(type, p, 2) && ResolvePlayer(Str(p, 0)) is { } builder && Int(p, 1) >= CountBuildings(builder.Id, false);
            case "PLAYER_HAS_N_OR_FEWER_FACTION_BUILDINGS":
                return Has(type, p, 2) && ResolvePlayer(Str(p, 0)) is { } faction && Int(p, 1) >= CountBuildings(faction.Id, true);
            case "PLAYER_HAS_POWER":
                return Has(type, p, 1) && ResolvePlayer(Str(p, 0)) is { } powered && _world.EnergyProductionOf(powered.Id) >= 0;
            case "PLAYER_HAS_NO_POWER":
                return !(Has(type, p, 1) && ResolvePlayer(Str(p, 0)) is { } unpowered && _world.EnergyProductionOf(unpowered.Id) >= 0);
            case "PLAYER_HAS_OBJECT_COMPARISON":
            {
                if (!Has(type, p, 4) || ResolvePlayer(Str(p, 0)) is not { } owner) return false;
                return Compare(Int(p, 1), CountOfTypes(owner.Id, Str(p, 3), null), Int(p, 2));
            }
            case "PLAYER_HAS_COMPARISON_UNIT_TYPE_IN_TRIGGER_AREA":
            {
                if (!Has(type, p, 5) || ResolvePlayer(Str(p, 0)) is not { } inArea || AreaOrDiagnose(Str(p, 4)) is not { } area) return false;
                return Compare(Int(p, 1), CountOfTypes(inArea.Id, Str(p, 3), area), Int(p, 2));
            }
            case "PLAYER_LOST_OBJECT_TYPE":
            {
                // evaluatePlayerLostObjectType: the count of the type dropped below the count seen at the previous evaluation.
                if (!Has(type, p, 2) || ResolvePlayer(Str(p, 0)) is not { } watcher) return false;
                var key = InvariantText.Format($"count:{watcher.Id.Value}:{Str(p, 1)}");
                var now = CountOfTypes(watcher.Id, Str(p, 1), null);
                var before = _counters.TryGetValue(key, out var seen) ? seen.Value : now;
                CounterFor(key).Value = now;
                return now < before;
            }
            case "PLAYER_ACQUIRED_SCIENCE":
            {
                if (!Has(type, p, 2) || ResolvePlayer(Str(p, 0)) is not { } learner) return false;
                return _world.Sciences.TryGet(Str(p, 1), out var science) && _world.PlayerSciences(learner.Id).Has(science.Index);
            }
            case "START_POSITION_IS":
                // Campaign maps have no start positions; the player's slot is its side number, which the skirmish condition compares against.
                return Has(type, p, 2) && ResolvePlayer(Str(p, 0)) is { } starter && starter.SideIndex == Int(p, 1);
            case "MISSION_ATTEMPTS":
                // The attempt count lives in the campaign profile (ScriptEngineOptions.MissionAttempts); a mission run by itself is the first attempt.
                return Has(type, p, 3) && Compare(Int(p, 1), _options.MissionAttempts, Int(p, 2));

            // ---- named objects
            case "NAMED_DESTROYED":
            {
                if (!Has(type, p, 1)) return false;
                var entity = NamedEntity(Str(p, 0));
                return entity is not null ? entity.EffectivelyDead : _everNamed.Contains(Str(p, 0));
            }
            case "NAMED_NOT_DESTROYED": return Has(type, p, 1) && NamedEntity(Str(p, 0)) is { EffectivelyDead: false };
            case "NAMED_DYING": return Has(type, p, 1) && NamedEntity(Str(p, 0)) is { EffectivelyDead: true };
            case "NAMED_TOTALLY_DEAD": return Has(type, p, 1) && NamedEntity(Str(p, 0)) is null && _everNamed.Contains(Str(p, 0));
            case "NAMED_CREATED": return Has(type, p, 1) && NamedEntity(Str(p, 0)) is not null;
            case "NAMED_OWNED_BY_PLAYER":
                return Has(type, p, 2) && ResolvePlayer(Str(p, 1)) is { } namedOwner && NamedEntity(Str(p, 0)) is { } owned && owned.Owner == namedOwner.Id;
            case "UNIT_HEALTH":
            {
                if (!Has(type, p, 3) || NamedEntity(Str(p, 0)) is not { Body: { } body }) return false;
                if (body.MaxHealth <= Fixed.Zero) return false;
                // (health*100 + initial/2) / initial, integer arithmetic of evaluateUnitHealth.
                var percent = (body.Health * 100 + body.MaxHealth / Fixed.FromInt(2)) / body.MaxHealth;
                return Compare(Int(p, 1), percent.Floor(), Int(p, 2));
            }
            case "NAMED_INSIDE_AREA":
                return Has(type, p, 2) && AreaOrDiagnose(Str(p, 1)) is { } insideArea && NamedEntity(Str(p, 0)) is { } inside && insideArea.Contains(inside.X, inside.Y);
            case "NAMED_OUTSIDE_AREA":
                // evaluateNamedOutsideArea is the negation of inside; an unknown object or area leaves it false as there.
                return Has(type, p, 2) && AreaOrDiagnose(Str(p, 1)) is { } outsideArea && NamedEntity(Str(p, 0)) is { } outside && !outsideArea.Contains(outside.X, outside.Y);
            case "NAMED_ENTERED_AREA":
            case "NAMED_EXITED_AREA":
            {
                if (!Has(type, p, 2) || AreaOrDiagnose(Str(p, 1)) is not { } transitionArea || NamedEntity(Str(p, 0)) is not { } mover) return false;
                var now = transitionArea.Contains(mover.X, mover.Y);
                var key = AreaMemoryKey(condition);
                var was = _areaInside.GetValueOrDefault(key);
                _areaInside[key] = now;
                return type == "NAMED_ENTERED_AREA" ? now && !was : !now && was;
            }
            case "NAMED_SELECTED":
                return Has(type, p, 1) && _named.TryGetValue(Str(p, 0), out var selectedId) && _selected.Contains(selectedId);

            // ---- teams
            case "TEAM_HAS_UNITS": return Has(type, p, 1) && AnyTeamNamedHasUnits(Str(p, 0));
            case "TEAM_DESTROYED": return Has(type, p, 1) && FindTeam(Str(p, 0)) is { } destroyed && !TeamHasCountingMember(destroyed);
            case "TEAM_CREATED": return Has(type, p, 1) && FindTeam(Str(p, 0)) is { Created: true };
            case "TEAM_OWNED_BY_PLAYER":
                return Has(type, p, 2) && ResolvePlayer(Str(p, 1)) is { } teamOwner && FindTeam(Str(p, 0)) is { } ownedTeam && ownedTeam.Owner == teamOwner.Id;
            case "TEAM_INSIDE_AREA_PARTIALLY": return Has(type, p, 3) && TeamAreaPartially(Str(p, 0), Str(p, 1), Int(p, 2));
            case "TEAM_INSIDE_AREA_ENTIRELY": return Has(type, p, 3) && TeamAreaEntirely(Str(p, 0), Str(p, 1), Int(p, 2));
            case "TEAM_OUTSIDE_AREA_ENTIRELY":
                return Has(type, p, 3) && !(TeamAreaEntirely(Str(p, 0), Str(p, 1), Int(p, 2)) || TeamAreaPartially(Str(p, 0), Str(p, 1), Int(p, 2)));
            case "TEAM_ENTERED_AREA_ENTIRELY":
            case "TEAM_ENTERED_AREA_PARTIALLY":
            case "TEAM_EXITED_AREA_ENTIRELY":
            case "TEAM_EXITED_AREA_PARTIALLY":
            {
                if (!Has(type, p, 3)) return false;
                var now = type.EndsWith("ENTIRELY", StringComparison.Ordinal) ? TeamAreaEntirely(Str(p, 0), Str(p, 1), Int(p, 2)) : TeamAreaPartially(Str(p, 0), Str(p, 1), Int(p, 2));
                var key = AreaMemoryKey(condition);
                var was = _areaInside.GetValueOrDefault(key);
                _areaInside[key] = now;
                return type.StartsWith("TEAM_ENTERED", StringComparison.Ordinal) ? now && !was : !now && was;
            }

            // ---- damage history (the last attacker, fed by Observe), kinds in areas, built objects, object status, finished paths
            case "SKIRMISH_PLAYER_FACTION":
                // evaluateSkirmishPlayerIsFaction: the player's side (faction side name, e.g. America) equals the parameter.
                return Has(type, p, 2) && ResolvePlayer(Str(p, 0)) is { } factionPlayer && SideOf(factionPlayer) == Str(p, 1);
            case "BUILDING_ENTERED_BY_PLAYER":
                // evaluateBuildingEntered(param 0 = player, param 1 = building): the last object that entered the building belongs to the player.
                return Has(type, p, 2) && ResolvePlayer(Str(p, 0)) is { } enterer && NamedEntity(Str(p, 1)) is { } building && _lastEntered.TryGetValue(building.Id.Value, out var who) && who == enterer.Id.Value;
            case "PLAYER_DESTROYED_N_BUILDINGS_PLAYER":
                // The reference's evaluatePlayerDestroyedNOrMoreBuildings is an unimplemented stub ("@todo CLH implement me!") that returns FALSE; so does this.
                return false;
            case "NAMED_ATTACKED_BY_PLAYER":
                return Has(type, p, 2) && ResolvePlayer(Str(p, 1)) is { } namedAttacker && NamedEntity(Str(p, 0)) is { } attacked && AttackedByPlayer(attacked, namedAttacker.Id);
            case "TEAM_ATTACKED_BY_PLAYER":
                return Has(type, p, 2) && ResolvePlayer(Str(p, 1)) is { } teamAttacker && FindTeam(Str(p, 0)) is { } attackedTeam && TeamAttackedByPlayer(attackedTeam, teamAttacker.Id);
            case "NAMED_ATTACKED_BY_OBJECTTYPE":
                return Has(type, p, 2) && NamedEntity(Str(p, 0)) is { } attackedNamed && AttackedByType(attackedNamed, Str(p, 1));
            case "TEAM_ATTACKED_BY_OBJECTTYPE":
                return Has(type, p, 2) && FindTeam(Str(p, 0)) is { } typedTeam && TeamAttackedByType(typedTeam, Str(p, 1));
            case "PLAYER_HAS_COMPARISON_UNIT_KIND_IN_TRIGGER_AREA":
            {
                // evaluatePlayerHasUnitKindInArea: live, non-inert objects of the player with the kind inside the area.
                if (!Has(type, p, 5) || ResolvePlayer(Str(p, 0)) is not { } kindOwner || AreaOrDiagnose(Str(p, 4)) is not { } kindArea) return false;
                return Compare(Int(p, 1), CountOfKindInArea(kindOwner.Id, Str(p, 3), kindArea), Int(p, 2));
            }
            case "BUILT_BY_PLAYER":
            {
                // evaluateBuiltByPlayer: the player owns at least one object of the type (list). The parameters are told apart by kind, not position.
                var playerParameter = p.FirstOrDefault(value => value.Type == MapScriptParameterType.Side);
                var typeParameter = p.FirstOrDefault(value => value.Type == MapScriptParameterType.ObjectType);
                if (playerParameter is null || typeParameter is null || ResolvePlayer(playerParameter.Text) is not { } builtBy) return false;
                return CountOfTypes(builtBy.Id, typeParameter.Text, null) != 0;
            }
            case "UNIT_HAS_OBJECT_STATUS":
                return Has(type, p, 2) && StatusBit(Str(p, 1)) is { } unitBit && NamedEntity(Str(p, 0)) is { } statusUnit && statusUnit.HasStatus(unitBit);
            case "TEAM_ALL_HAS_OBJECT_STATUS":
            case "TEAM_SOME_HAVE_OBJECT_STATUS":
            {
                // evaluateTeamHasObjectStatus: all members (true for an empty team) or any member.
                if (!Has(type, p, 2) || StatusBit(Str(p, 1)) is not { } teamBit || FindTeam(Str(p, 0)) is not { } statusTeam) return false;
                return TeamHasStatus(statusTeam, teamBit, all: type == "TEAM_ALL_HAS_OBJECT_STATUS");
            }
            case "NAMED_REACHED_WAYPOINTS_END":
                return Has(type, p, 2) && NamedEntity(Str(p, 0)) is { } walker && _completedPath.TryGetValue(walker.Id.Value, out var walked) && walked.Equals(Str(p, 1), StringComparison.OrdinalIgnoreCase);
            case "TEAM_REACHED_WAYPOINTS_END":
                // evaluateTeamReachedWaypointsEnd is true when any member completed the path.
                return Has(type, p, 2) && FindTeam(Str(p, 0)) is { } pathTeam && TeamReachedPathEnd(pathTeam, Str(p, 1));

            // ---- multiplayer victory/defeat (VictoryConditions::isLocalAlliedVictory/Defeat/isLocalDefeat)
            case "MULTIPLAYER_ALLIED_VICTORY": return IsAlliedVictory();
            case "MULTIPLAYER_ALLIED_DEFEAT": return IsAlliedDefeat();
            case "MULTIPLAYER_PLAYER_DEFEAT": return _world.IsPlayerDefeated(_options.LocalPlayer) && !IsAlliedDefeat();

            // ---- presentation state the host reports
            case "HAS_FINISHED_SPEECH":
            case "HAS_FINISHED_AUDIO":
            case "HAS_FINISHED_VIDEO":
                return !Has(type, p, 1) || !_pendingPresentation.ContainsKey(Str(p, 0));
            case "MUSIC_TRACK_HAS_COMPLETED":
                return !Has(type, p, 1) || !_pendingPresentation.ContainsKey(Str(p, 0));
            case "CAMERA_MOVEMENT_FINISHED": return !_pendingPresentation.ContainsKey("<camera>");

            default:
                if (TryEvaluateExtendedCondition(condition, out var extended)) return extended;
                if (TryEvaluatePhase13Condition(condition, out var phase13)) return phase13;
                _missingConditions[type] = _missingConditions.GetValueOrDefault(type) + 1;
                return false;
        }
    }

    /// <summary><c>Player::getSide</c>: the side name of the player's faction (<c>PlayerTemplate::Side</c>); the faction catalog keeps no side, so it is read from the faction's starting building.</summary>
    private string SideOf(ScriptPlayer player)
        => _world.Templates.Factions.TryGet(player.Faction, out var faction) && _world.Templates.TryGet(faction.StartingBuilding, out var start) ? start.Side : string.Empty;

    private bool AttackedByPlayer(Entity victim, PlayerId player)
        => _lastDamage.TryGetValue(victim.Id.Value, out var damage) && damage.Owner == player.Value;

    private bool AttackedByType(Entity victim, string typeParameter)
        => _lastDamage.TryGetValue(victim.Id.Value, out var damage) && damage.Template.Length > 0
           && (_objectLists.TryGetValue(typeParameter, out var list) ? list.Contains(damage.Template) : string.Equals(damage.Template, typeParameter, StringComparison.Ordinal));

    private int? StatusBit(string name)
    {
        try { return ObjectStatusBits.Index(name); }
        catch (ArgumentException)
        {
            Diagnose($"unknown object status '{name}'");
            return null;
        }
    }

    private bool IsUnit(Entity entity) => !entity.Template.IsKindOf("STRUCTURE") && CountsAsObject(entity);

    // ---- team tests written as loops over the member ids: a lambda in EvaluateConditionCore that reads its parameters makes every evaluation of every
    // condition allocate the closure, whether the lambda is reached or not. A live member is one that exists and is not marked for destruction (LiveMembers).

    private Entity? LiveMember(uint id) => _world.FindEntity(new EntityId(id)) is { DestroyRequested: false } entity ? entity : null;

    private bool TeamHasUnit(ScriptTeam team)
    {
        foreach (var id in team.Members)
            if (LiveMember(id) is { } member && IsUnit(member)) return true;
        return false;
    }

    /// <summary>Some team <see cref="TeamsNamed"/> yields has a unit.</summary>
    private bool AnyTeamNamedHasUnits(string name)
    {
        if (name == ThisTeamName) return _callingTeam is { } calling && TeamHasUnit(calling);
        if (_callingTeam is { } instance && instance.Prototype == name) return TeamHasUnit(instance);
        foreach (var team in _teams.Values)
            if (team.Prototype == name && TeamHasUnit(team)) return true;
        return false;
    }

    private bool TeamHasCountingMember(ScriptTeam team)
    {
        foreach (var id in team.Members)
            if (LiveMember(id) is { } member && CountsAsObject(member)) return true;
        return false;
    }

    private bool TeamAttackedByPlayer(ScriptTeam team, PlayerId player)
    {
        foreach (var id in team.Members)
            if (LiveMember(id) is { } member && AttackedByPlayer(member, player)) return true;
        return false;
    }

    private bool TeamAttackedByType(ScriptTeam team, string typeParameter)
    {
        foreach (var id in team.Members)
            if (LiveMember(id) is { } member && AttackedByType(member, typeParameter)) return true;
        return false;
    }

    /// <summary>All live members have the status (true for a team without any) or some live member has it.</summary>
    private bool TeamHasStatus(ScriptTeam team, int bit, bool all)
    {
        foreach (var id in team.Members)
        {
            if (LiveMember(id) is not { } member) continue;
            if (member.HasStatus(bit) != all) return !all;
        }
        return all;
    }

    private bool TeamReachedPathEnd(ScriptTeam team, string path)
    {
        foreach (var id in team.Members)
            if (LiveMember(id) is { } member && _completedPath.TryGetValue(member.Id.Value, out var done) && done.Equals(path, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // The key under which an entered/exited condition remembers its last answer: a function of the condition alone, so it is built once per condition.
    private readonly Dictionary<MapCondition, string> _areaMemoryKeys = new(ReferenceEqualityComparer.Instance);

    private string AreaMemoryKey(MapCondition condition)
    {
        if (_areaMemoryKeys.TryGetValue(condition, out var key)) return key;
        var p = condition.Parameters;
        var type = condition.TypeName;
        key = type.StartsWith("NAMED_", StringComparison.Ordinal)
            ? $"{Str(p, 0)}|{Str(p, 1)}"
            : $"{type[..(type.IndexOf("_AREA", StringComparison.Ordinal))]}|{Str(p, 0)}|{Str(p, 1)}|{type.EndsWith("ENTIRELY", StringComparison.Ordinal)}";
        _areaMemoryKeys.Add(condition, key);
        return key;
    }

    // ThingTemplate::isBuildFacility is derived from other templates' prerequisites in the reference; the simulation has no such back-reference, so a
    // structure that can produce (has a production module) stands in for it (TemplateFacts.BuildFacility). Documented in MAP-SCRIPTS.md §8.

    private ScriptArea? AreaOrDiagnose(string name)
    {
        var area = FindAreaQualified(name);
        if (area is null) Diagnose(UnknownMessage(_unknownAreaMessages, "trigger area", name));
        return area;
    }

    // A script that names something the map does not have is counted every time it runs (Diagnose); the message is the same text each time.
    private readonly Dictionary<string, string> _unknownAreaMessages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _unknownWaypointMessages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _unknownPlayerMessages = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _unknownPowerMessages = new(StringComparer.Ordinal);

    private static string UnknownMessage(Dictionary<string, string> messages, string what, string name)
    {
        if (!messages.TryGetValue(name, out var message)) messages.Add(name, message = $"unknown {what} '{name}'");
        return message;
    }

    private IEnumerable<ScriptTeam> TeamsNamed(string name)
    {
        if (name == ThisTeamName)
        {
            if (_callingTeam is { } calling) yield return calling;
            yield break;
        }
        if (_callingTeam is { } instance && instance.Prototype == name)
        {
            yield return instance;
            yield break;
        }
        foreach (var team in _teams.Values)
            if (team.Prototype == name) yield return team;
    }

    /// <summary><c>ObjectTypes</c> resolution (<c>objectTypesFromParam</c>): a named object-type list if one was built with <c>OBJECTLIST_ADDOBJECTTYPE</c>, otherwise the single template name.</summary>
    private bool TypeMatches(string typeParameter, Entity entity)
        => _objectLists.TryGetValue(typeParameter, out var list)
            ? list.Contains(entity.Template.Name)
            : string.Equals(entity.Template.Name, typeParameter, StringComparison.Ordinal);

    private static bool SurfacesMatch(Entity entity, int mask)
    {
        if (mask == 0) return true;
        var surfaces = entity.Template.Locomotor?.Surfaces ?? LocomotorSurfaces.Ground;
        return ((int)surfaces & mask) != 0;
    }

    /// <summary><c>Team::allInside</c>: a team with any object, every considered (alive, non-inert, surface-matching) member inside, at least one considered.</summary>
    private bool TeamAreaEntirely(string teamName, string areaName, int surfaceMask)
    {
        if (AreaOrDiagnose(areaName) is not { } area) return false;
        var team = teamName == ThisTeamName ? _callingTeam : FindTeam(teamName);
        if (team is null) return false;
        if (!TeamHasCountingMember(team)) return false;
        var considered = false;
        foreach (var id in team.Members)
        {
            if (LiveMember(id) is not { } member) continue;
            if (!SurfacesMatch(member, surfaceMask) || member.EffectivelyDead || member.Template.IsKindOf("INERT")) continue;
            considered = true;
            if (!area.Contains(member.X, member.Y)) return false;
        }
        return considered;
    }

    /// <summary><c>Team::someInsideSomeOutside</c> || <c>allInside</c>: any considered member inside.</summary>
    private bool TeamAreaPartially(string teamName, string areaName, int surfaceMask)
    {
        if (AreaOrDiagnose(areaName) is not { } area) return false;
        var team = teamName == ThisTeamName ? _callingTeam : FindTeam(teamName);
        if (team is null) return false;
        foreach (var id in team.Members)
        {
            if (LiveMember(id) is not { } member) continue;
            if (!SurfacesMatch(member, surfaceMask) || member.EffectivelyDead || member.Template.IsKindOf("INERT")) continue;
            if (area.Contains(member.X, member.Y)) return true;
        }
        return false;
    }

    private bool IsAlliedDefeat()
    {
        // isLocalAlliedDefeat: every member of the local alliance has lost all objects.
        var local = _options.LocalPlayer;
        if (!_world.IsPlayerDefeated(local)) return false;
        foreach (var player in _program.Players)
            if (!player.Id.IsSystem && player.Id != local && _world.AreAllies(local, player.Id) && !_world.IsPlayerDefeated(player.Id)) return false;
        return true;
    }

    private bool IsAlliedVictory()
    {
        // isLocalAlliedVictory: nobody who is not an ally of the local player is left, and the local player (or an ally) still has objects.
        var local = _options.LocalPlayer;
        var anyAllyAlive = !_world.IsPlayerDefeated(local);
        foreach (var player in _program.Players)
        {
            if (player.Id.IsSystem || player.Id == local || _world.IsPlayerDefeated(player.Id)) continue;
            if (_world.AreAllies(local, player.Id)) anyAllyAlive = true;
            else if (_world.AreEnemies(local, player.Id)) return false;
        }
        return anyAllyAlive;
    }
}
