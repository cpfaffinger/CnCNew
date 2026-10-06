using CnCNew.Data.World;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Scripting;

/// <summary>
/// The team queues of the AI player (<c>AIPlayer::checkQueuedTeams</c>, <c>checkReadyTeams</c>, <c>TeamInQueue</c>) as far as scripted <c>BUILD_TEAM</c> and <c>RECRUIT_TEAM</c> use them: a team that is
/// being built or recruited is inactive (<see cref="ScriptTeam.Created"/> is false) and so invisible to scripts until it has been through the queues.
/// <list type="bullet">
/// <item><description><b>Building</b>: a built unit joins the team and walks to the team's home (<c>aiFollowExitProductionPath</c>). When the team's <c>teamInitialIdleFrames</c> are over (0: never) it is disbanded — its units
/// go back to the player's default team, i.e. leave the team — unless every required type is built or in production, in which case it goes on to the ready queue once nothing is in production any more.
/// Once every type is complete it goes to the ready queue at once. While any member is idle, a team with <c>teamExecutesActionsOnCreate</c> runs the true-actions of its production-condition script with
/// itself as <c>&lt;This Team&gt;</c>, every tick.</description></item>
/// <item><description><b>Ready</b> (also where a recruited team starts): the team waits until all members are idle (gathered), 60 seconds after the queue began, or — with a start-action script — as soon as any member is idle;
/// then it becomes active (<c>setActive</c>, which is what runs its create event).</description></item>
/// </list>
/// </summary>
public sealed partial class ScriptEngine
{
    private sealed class TeamQueueEntry
    {
        public int TeamId;
        public long Started;
        public bool Ready;
    }

    private readonly List<TeamQueueEntry> _teamQueue = [];

    /// <summary>The queue entry of a team that was just recruited: it waits at its home (the ready queue).</summary>
    private void EnqueueReadyTeam(ScriptTeam team)
    {
        team.Created = false;
        _teamQueue.RemoveAll(entry => entry.TeamId == team.Id);
        _teamQueue.Add(new TeamQueueEntry { TeamId = team.Id, Started = _tick, Ready = true });
    }

    private void UpdateTeamQueues()
    {
        if (_teamQueue.Count == 0) return;
        foreach (var entry in _teamQueue.ToArray())
        {
            if (!_teams.TryGetValue(entry.TeamId, out var team)) { _teamQueue.Remove(entry); _teamBuilds.RemoveAll(order => order.TeamId == entry.TeamId); continue; }
            var prototype = _program.FindTeam(team.Prototype);
            if (prototype is null) { _teamQueue.Remove(entry); continue; }
            var members = LiveMembers(team).ToArray();
            if (!entry.Ready) UpdateBuildingTeam(entry, team, prototype, members);
            if (entry.Ready) UpdateReadyTeam(entry, team, prototype, members);
        }
    }

    /// <summary><c>AIPlayer::checkQueuedTeams</c> for one team.</summary>
    private void UpdateBuildingTeam(TeamQueueEntry entry, ScriptTeam team, ScriptTeamPrototype prototype, Entity[] members)
    {
        var orders = _teamBuilds.Where(order => order.TeamId == team.Id).ToArray();
        var expired = prototype.InitialIdleFrames >= 1 && _tick > entry.Started + _world.Time.OriginalFrames(prototype.InitialIdleFrames);
        if (expired)
        {
            // isMinimumBuilt: every required type has its minimum finished or in production; areBuildsComplete: nothing is in production any more.
            var minimumBuilt = orders.All(order => order.Min <= 0 || order.Joined + (order.Queued > order.Joined ? 1 : 0) >= order.Min);
            if (minimumBuilt)
            {
                if (orders.All(order => order.Queued <= order.Joined)) entry.Ready = true;
                return;
            }
            // disband: the units return to the player's default team and the instance goes (a singleton stays).
            _teamBuilds.RemoveAll(order => order.TeamId == team.Id);
            _teamQueue.Remove(entry);
            team.Members.Clear();
            if (!(prototype.IsSingleton || prototype.MaxInstances < 2)) _teams.Remove(team.Id);
            Diagnose($"team '{team.Prototype}' disbanded: build time expired");
            return;
        }
        if (orders.All(order => order.Joined >= order.Total)) { entry.Ready = true; return; }
        if (prototype.ExecutesActionsOnCreate && members.Any(IsIdle)) RunStartActions(prototype, team);
    }

    /// <summary><c>AIPlayer::checkReadyTeams</c> for one team.</summary>
    private void UpdateReadyTeam(TeamQueueEntry entry, ScriptTeam team, ScriptTeamPrototype prototype, Entity[] members)
    {
        var timeExpired = entry.Started + _world.Time.WholeSecondsToTicks(60) < _tick;
        var allIdle = members.All(IsIdle);
        var anyIdle = members.Any(IsIdle);
        if (anyIdle && prototype.ExecutesActionsOnCreate && _program.FindScript(prototype.ProductionCondition) is { Script.Actions.IsDefaultOrEmpty: false }) allIdle = true;
        if (!timeExpired && !allIdle) return;
        _teamQueue.Remove(entry);
        team.Created = true;
    }

    /// <summary><c>friend_executeAction(script->getAction(), team)</c>: the production-condition script's true-actions with the team as <c>&lt;This Team&gt;</c>.</summary>
    private void RunStartActions(ScriptTeamPrototype prototype, ScriptTeam team)
    {
        if (_program.FindScript(prototype.ProductionCondition) is not { } script || _depth >= MaxSubroutineDepth) return;
        var previous = _callingTeam;
        _callingTeam = team;
        _depth++;
        try { ExecuteActions(script.Script.Actions); }
        finally { _depth--; _callingTeam = previous; }
    }

    private void DescribeTeamQueue(IStateWriter writer)
    {
        if (_teamQueue.Count == 0) return;
        writer.BeginArray("teamQueue");
        foreach (var entry in _teamQueue)
        {
            writer.BeginObject(null);
            writer.Int("team", entry.TeamId);
            writer.Int("started", entry.Started);
            writer.Bool("ready", entry.Ready);
            writer.EndObject();
        }
        writer.EndArray();
    }

    private void LoadTeamQueue(IStateReader reader)
    {
        _teamQueue.Clear();
        if (!reader.Has("teamQueue")) return;
        foreach (var item in reader.Array("teamQueue"))
            _teamQueue.Add(new TeamQueueEntry { TeamId = checked((int)item.Int("team")), Started = item.Int("started"), Ready = item.Bool("ready") });
    }
}
