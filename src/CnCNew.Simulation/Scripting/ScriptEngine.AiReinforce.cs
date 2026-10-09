using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Scripting;

/// <summary>
/// <c>AIPlayer::selectTeamToReinforce</c> (GameLogic/AI/AIPlayer.cpp; <c>AISkirmishPlayer</c> calls it unchanged): before the AI picks a new team to build it looks for a team that is
/// in the field, asks for it (<c>teamAutoReinforce</c>) and misses a unit of one of its types. Such a team gets one unit: taken from a recruitable team near the team's first member
/// (<c>Team::tryToRecruit</c>) or trained, and sent after the team (<c>AIUpdateInterface::joinTeam</c>). Only a prototype of a higher production priority than the best team that could
/// be built now is reinforced, and none that is being built or reinforced already.
/// </summary>
public sealed partial class ScriptEngine
{
    /// <summary><c>invalidPri</c> of <c>AIPlayer::selectTeamToBuild</c>: the priority below every team when no team can be built.</summary>
    private const int NoTeamPriority = -99999;

    private bool SelectTeamToReinforce(AiPlayer state, ScriptPlayer owner, int minPriority)
    {
        var player = state.Setup.Player;
        ScriptTeam? chosen = null;
        ScriptTeamPrototype? chosenPrototype = null;
        string? chosenType = null;
        var priority = minPriority;
        foreach (var prototype in _program.Teams)
        {
            if (prototype.Owner != owner.Name || !prototype.AutoReinforce || prototype.ProductionPriority <= priority) continue;
            var instances = _teams.InstancesOf(prototype.Name);
            if (IsBeingBuiltOrReinforced(instances)) continue;
            foreach (var team in instances)
            {
                if (!HasLiveMember(team)) continue;
                foreach (var unit in prototype.Units)
                {
                    if (unit.MaxCount < 1 || !_world.Templates.TryGet(unit.Type, out var template)) continue;
                    var count = 0;
                    foreach (var id in team.Members)
                        if (_world.FindEntity(new EntityId(id)) is { DestroyRequested: false, EffectivelyDead: false } member && ReferenceEquals(member.Template, template)) count++;
                    if (count >= unit.MaxCount || !HasFactoryFor(player, unit.Type)) continue;
                    chosen = team;
                    chosenPrototype = prototype;
                    chosenType = unit.Type;
                    priority = prototype.ProductionPriority;
                }
            }
        }
        if (chosen is null || chosenPrototype is null || chosenType is null) return false;

        var order = new TeamBuildOrder { TeamId = chosen.Id, Type = chosenType, Min = 0, Total = 1, Reinforcement = true, Started = _tick };
        _teamBuilds.Add(order);
        // The origin of the recruiting is the team's first member; a unit found there joins at once, otherwise one is trained (ProcessTeamBuilds).
        Entity? first = null;
        foreach (var id in chosen.Members)
            if (_world.FindEntity(new EntityId(id)) is { DestroyRequested: false } member) { first = member; break; }
        if (first is not null)
        {
            var origin = new ScriptWaypoint(string.Empty, first.X, first.Y, Fixed.Zero);
            if (FindRecruit(player, chosenType, chosenPrototype, chosen, origin, SquaredReach.Of(_options.Ai!.Data.MaxRecruitRadius)) is { } recruit)
            {
                foreach (var other in _teams.Values) other.Members.Remove(recruit.Id.Value);
                chosen.Members.Add(recruit.Id.Value);
                if (chosen.AttackPriority.Length > 0) _prioritySetOf[recruit.Id.Value] = chosen.AttackPriority;
                order.Queued = 1;
                order.Joined = 1;
                JoinTeam(recruit, chosen);
            }
        }
        state.TeamCheckTick = _tick;
        return true;
    }

    /// <summary>A queue entry of a team that is still being built, or a reinforcement on its way: "currently building one of these".</summary>
    private bool IsBeingBuiltOrReinforced(List<ScriptTeam> instances)
    {
        foreach (var team in instances)
        {
            foreach (var entry in _teamQueue)
                if (entry.TeamId == team.Id && !entry.Ready) return true;
            foreach (var order in _teamBuilds)
                if (order.TeamId == team.Id && order.Reinforcement) return true;
        }
        return false;
    }

    /// <summary><c>AIPlayer::findFactory(thing, busyOK = false)</c> as the reinforcement uses it: some finished building of the player can train the type, busy or not.</summary>
    private bool HasFactoryFor(PlayerId player, string type)
    {
        foreach (var entity in _world.EntitiesOwnedBy(player).AsSpan())
            if (Counts(entity) && !entity.HasStatus(UnderConstructionBit) && ProductionOf(entity) is not null && _world.CanQueueProductionTemplate(player, entity.Id, type)) return true;
        return false;
    }

    /// <summary>
    /// <c>AIUpdateInterface::joinTeam</c>: the unit does what the first other member of its team does - it walks to an idle member, takes over the standing order of a member that has
    /// one (the reference copies goal and state), and otherwise marches to where that member is.
    /// </summary>
    private void JoinTeam(Entity unit, ScriptTeam team)
    {
        if (unit.Template.Locomotor is null) return;
        Entity? other = null;
        foreach (var id in team.Members)
        {
            if (id == unit.Id.Value || _world.FindEntity(new EntityId(id)) is not { DestroyRequested: false, EffectivelyDead: false } member || AiOf(member) is null) continue;
            other = member;
            break;
        }
        if (other is null) return;
        if (!IsIdle(other) && _followers.TryGetValue(other.Id.Value, out var order) && !order.AsTeam)
        {
            StartFollowing(unit, order.Path);
            _followers[unit.Id.Value].Index = order.Index;
            return;
        }
        MoveTo(unit, other.X, other.Y, attack: !IsIdle(other));
    }

    /// <summary>A reinforcement that could not be trained within the build time of its team (<c>teamInitialIdleFrames</c>) is given up, so that the team can be looked at again.</summary>
    private void DropExpiredReinforcements()
    {
        for (var index = _teamBuilds.Count - 1; index >= 0; index--)
        {
            var order = _teamBuilds[index];
            if (!order.Reinforcement || order.Queued > order.Joined) continue;
            if (!_teams.TryGetValue(order.TeamId, out var team) || PrototypeOf(team) is not { } prototype) { _teamBuilds.RemoveAt(index); continue; }
            if (prototype.InitialIdleFrames >= 1 && _tick > order.Started + _world.Time.OriginalFrames(prototype.InitialIdleFrames)) _teamBuilds.RemoveAt(index);
        }
    }
}
