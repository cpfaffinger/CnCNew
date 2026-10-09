using CnCNew.Simulation.Ids;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Scripting;

/// <summary>
/// The common victim of a team (<c>Team::m_commonAttackTarget</c>, Common/RTS/Team.cpp): the members of a team whose prototype says <c>teamAttackCommonTarget</c> attack one victim
/// together. Only a computer player's team has one, and not on the easy level (<c>Team::setTeamTargetObject</c>: "we don't do this for easy") - the lever that makes the normal and the
/// hard army focus their fire.
/// <para>
/// Who sets it: the first member that fires at a victim of its own choice while the team has none (<c>AIAttackFireWeaponState::onEnter</c>), and a hunting member on every look
/// (<c>AIHuntState::update</c>). Who reads it: every member that looks for a victim by itself (<c>AIUpdateInterface::getNextMoodTarget</c>, before the member's own look) and a hunting
/// member. The victim is dropped as soon as it is dead, hidden by stealth, inside a container or an aircraft (<c>Team::getTeamTargetObject</c>).
/// </para>
/// <para>
/// Only for the players the original skirmish AI runs: a mission's teams keep attacking as they did (their reference values are fixed). <c>AIAttackSquadState::chooseVictim</c>, the
/// other place the difficulty of a player chooses a victim (easy: a random member of the attacked squad, normal: the nearest, hard: all the same), belongs to the attack-team orders
/// (<c>TEAM_ATTACK_TEAM</c>, <c>NAMED_ATTACK_TEAM</c>), which the skirmish library never gives.
/// </para>
/// </summary>
public sealed partial class ScriptEngine
{
    public Entity? CommonTargetOf(Entity entity)
    {
        if (_ai.Count == 0 || CommonTargetTeamOf(entity) is not { CommonTarget: not 0 } team) return null;
        // "if (teamVictim && getAttitude() >= AI_NORMAL) return teamVictim".
        return AttitudeOf(entity) >= Attitude.Normal ? TeamTarget(team) : null;
    }

    public void NoteFiring(Entity attacker, Entity victim)
    {
        if (_ai.Count == 0 || CommonTargetTeamOf(attacker) is not { } team) return;
        if (TeamTarget(team) is null) SetTeamTarget(team, victim);
    }

    /// <summary>The team of the unit when that team attacks a common victim: one of an AI player, with the flag on its prototype.</summary>
    private ScriptTeam? CommonTargetTeamOf(Entity entity)
        => FirstTeamOf(entity.Id.Value) is { } team && PrototypeOf(team) is { AttackCommonTarget: true } ? team : null;

    /// <summary><c>Team::getTeamTargetObject</c>: the victim while it can still be one; otherwise it is forgotten.</summary>
    private Entity? TeamTarget(ScriptTeam team)
    {
        if (team.CommonTarget == 0) return null;
        var target = _world.FindEntity(new EntityId(team.CommonTarget));
        if (target is not null && (target.DestroyRequested || target.EffectivelyDead || IsHiddenByStealth(target) || target.ContainedBy.IsValid || target.Template.IsKindOf("AIRCRAFT"))) target = null;
        if (target is null) team.CommonTarget = 0;
        return target;
    }

    /// <summary><c>Team::setTeamTargetObject</c>: only a computer player's team, and not on the easy level.</summary>
    private void SetTeamTarget(ScriptTeam team, Entity? victim)
    {
        if (victim is null)
        {
            team.CommonTarget = 0;
            return;
        }
        if (AiOfPlayer(team.Owner) is not { } owner || owner.Setup.Difficulty == ScriptDifficulty.Easy) return;
        team.CommonTarget = victim.Id.Value;
    }

    /// <summary>
    /// <c>AIHuntState::update</c>, the victim of a hunting unit: without a common victim, or for a unit of a team that does not attack together, the nearest enemy by the unit's attack
    /// priorities. A member of a team that attacks together takes the team's victim when it has no priority set; with a set it takes the team's victim unless its own find has a
    /// higher priority, and what it settles on becomes the team's victim.
    /// </summary>
    private Entity? HuntVictim(Entity hunter)
    {
        if (_ai.Count == 0 || CommonTargetTeamOf(hunter) is not { } team) return NearestEnemy(hunter);
        var teamVictim = TeamTarget(team);
        var hasPriorities = _prioritySetOf.TryGetValue(hunter.Id.Value, out var set) && _prioritySets.ContainsKey(set);
        if (teamVictim is not null && !hasPriorities) return teamVictim;
        var victim = NearestEnemy(hunter);
        if (teamVictim is not null)
        {
            victim ??= teamVictim;
            if (AttackPriority(hunter, teamVictim) >= AttackPriority(hunter, victim)) victim = teamVictim;
        }
        SetTeamTarget(team, victim);
        return victim;
    }
}
