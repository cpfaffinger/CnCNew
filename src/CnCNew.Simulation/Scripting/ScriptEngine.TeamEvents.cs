using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Scripting;

/// <summary>
/// Team event scripts (<c>Team::updateState</c>, <c>Team::notifyTeamOfObjectDeath</c>, <c>ScriptEngine::runScript</c>): a team prototype can name subroutine scripts that run
/// when the team is created, goes idle, sights or loses sight of an enemy, loses part (<c>teamDestroyedThreshold</c>) or all of itself, or loses a unit. The script runs
/// as a subroutine with the team as <c>&lt;This Team&gt;</c>. Campaign scripts use these for wave counting and for ending missions, so a mission whose end hangs on one is
/// unreachable without them.
/// </summary>
public sealed partial class ScriptEngine
{
    /// <summary>Unit-destroyed events seen by <see cref="ObserveExtended"/>, run at the start of the next update (team id, script name).</summary>
    private readonly List<(int Team, string Script)> _pendingTeamScripts = [];

    /// <summary><c>runScript(name, team)</c>: the subroutine runs with the team as the calling team; a name that is not a subroutine or does not exist is diagnosed.</summary>
    private void RunTeamScript(ScriptTeam team, string script)
    {
        if (script.Length == 0) return;
        var previous = _callingTeam;
        _callingTeam = team;
        CallSubroutine(script);
        _callingTeam = previous;
    }

    private void ProcessTeamEvents()
    {
        if (_pendingTeamScripts.Count > 0)
        {
            var pending = _pendingTeamScripts.ToArray();
            _pendingTeamScripts.Clear();
            foreach (var (teamId, script) in pending)
                if (_teams.TryGetValue(teamId, out var team)) RunTeamScript(team, script);
        }
        foreach (var team in _teams.Values.ToArray())
        {
            if (!team.Created || _program.FindTeam(team.Prototype) is not { HasEventScripts: true } prototype) continue;
            var alive = LiveMembers(team).Where(member => !member.EffectivelyDead).ToArray();

            if (!team.CreateHandled)
            {
                team.CreateHandled = true;
                RunTeamScript(team, prototype.OnCreateScript);
                if (prototype.OnDestroyedScript.Length > 0)
                {
                    // m_destroyThreshold = curUnits - (curUnits * threshold), clamped to 0..curUnits-1.
                    alive = LiveMembers(team).Where(member => !member.EffectivelyDead).ToArray();
                    team.CurUnits = alive.Length;
                    var threshold = (int)(Fixed.FromInt(team.CurUnits) - Fixed.FromInt(team.CurUnits) * prototype.DestroyedThreshold).Floor();
                    team.DestroyThreshold = Math.Clamp(threshold, 0, Math.Max(0, team.CurUnits - 1));
                }
            }

            if (prototype.EnemySightedScript.Length > 0 || prototype.AllClearScript.Length > 0)
            {
                var previous = team.SeeEnemy;
                team.SeeEnemy = false;
                foreach (var member in alive)
                {
                    if (!EnemyInSight(member)) continue;
                    team.SeeEnemy = true;
                    break;
                }
                if (alive.Length > 0 && previous != team.SeeEnemy) RunTeamScript(team, team.SeeEnemy ? prototype.EnemySightedScript : prototype.AllClearScript);
            }

            if (prototype.OnDestroyedScript.Length > 0)
            {
                var before = team.CurUnits;
                team.CurUnits = LiveMembers(team).Count(member => !member.EffectivelyDead);
                if (team.CurUnits != before && team.CurUnits <= team.DestroyThreshold)
                {
                    RunTeamScript(team, prototype.OnDestroyedScript);
                    team.DestroyThreshold = -1;
                }
            }

            if (prototype.OnIdleScript.Length > 0)
            {
                var mobile = LiveMembers(team).Where(member => !member.EffectivelyDead && member.Template.Locomotor is not null).ToArray();
                var idle = mobile.All(IsIdle);
                if (mobile.Length > 0 && idle && team.WasIdle) RunTeamScript(team, prototype.OnIdleScript);
                team.WasIdle = idle;
            }
        }
    }

    /// <summary><c>getClosestObject(visionRange, ALLOW_ENEMIES, alive)</c> for one member: any enemy object inside its vision range.</summary>
    private bool EnemyInSight(Entity member)
    {
        var limit = SquaredReach.Of(member.Template.VisionRange);
        if (_world.RelationshipOverride is not null)
        {
            // A script set a relation between teams: whether two objects are enemies is then a question per pair of objects.
            foreach (var entity in _world.Entities)
            {
                if (entity.Owner.IsSystem || entity.EffectivelyDead || entity.DestroyRequested || !_world.AreEnemies(member, entity)) continue;
                if (limit.Contains(DistanceSquared(member, entity.X, entity.Y))) return true;
            }
            return false;
        }
        // Without such an override "enemy" is the relation of the two owners, so only the objects of the member's enemy players can answer; "any" does not
        // depend on the order they are looked at. The system player and unknown owners are nobody's enemy (SimulationWorld.RelationshipOf).
        foreach (var player in _world.Configuration.Players)
        {
            if (!_world.AreEnemies(member.Owner, player.Id)) continue;
            foreach (var entity in _world.EntitiesOwnedBy(player.Id).AsSpan())
            {
                if (entity.EffectivelyDead || entity.DestroyRequested) continue;
                if (limit.Contains(DistanceSquared(member, entity.X, entity.Y))) return true;
            }
        }
        return false;
    }
}
