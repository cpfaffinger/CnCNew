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
/// Special-power actions that name the power directly (not through a command button), named map reveals, recruiting and the actions the simulation can only record
/// (<see cref="ScriptSupport.Recorded"/>). Called from <see cref="TryExecuteSequentialAction"/>'s fall-through; every function cites its reference in
/// <c>ScriptActions.cpp</c> / <c>AIPlayer.cpp</c>.
/// </summary>
public sealed partial class ScriptEngine
{
    /// <summary><c>ScriptEngine::createNamedMapReveal</c>: a look that <c>MAP_REVEAL_PERMANENTLY_AT_WAYPOINT</c> made and <c>MAP_UNDO_REVEAL_PERMANENTLY_AT_WAYPOINT</c> can take back.</summary>
    private readonly record struct NamedReveal(int Player, Fixed X, Fixed Y, Fixed Radius);

    private readonly SortedDictionary<string, NamedReveal> _namedReveals = new(StringComparer.Ordinal);


    private bool TryExecutePowerAction(MapScriptAction action)
    {
        var p = action.Parameters;
        var type = action.TypeName;
        switch (type)
        {
            case "NAMED_FIRE_SPECIAL_POWER_AT_WAYPOINT":
                // doNamedFireSpecialPowerAtWaypoint -> mod->doSpecialPowerAtLocation: here the ordinary use command (readiness and science checks apply).
                if (Has(type, p, 3) && NamedEntity(Str(p, 0)) is { } waypointUser && WaypointOrDiagnose(Str(p, 2)) is { } spot)
                    FireSpecialPower(waypointUser, Str(p, 1), SpecialPowerTargetKind.Location, EntityId.Invalid, spot.X, spot.Y);
                return true;
            case "NAMED_FIRE_SPECIAL_POWER_AT_NAMED":
                if (Has(type, p, 3) && NamedEntity(Str(p, 0)) is { } objectUser && NamedEntity(Str(p, 2)) is { } target)
                    FireSpecialPower(objectUser, Str(p, 1), SpecialPowerTargetKind.Object, target.Id, Fixed.Zero, Fixed.Zero);
                return true;
            case "SKIRMISH_FIRE_SPECIAL_POWER_AT_MOST_COST":
                if (Has(type, p, 2) && ResolvePlayer(Str(p, 0)) is { } firer) FireAtMostCost(firer.Id, Str(p, 1));
                return true;
            case "NAMED_SET_SPECIAL_POWER_COUNTDOWN":
            case "NAMED_ADD_SPECIAL_POWER_COUNTDOWN":
                if (Has(type, p, 3) && PowerModuleOf(Str(p, 0), Str(p, 1)) is { } timed)
                {
                    var frames = _world.Time.WholeSecondsToTicks(Int(p, 2));
                    timed.SetReadyFrame(type == "NAMED_SET_SPECIAL_POWER_COUNTDOWN" ? _world.Tick + frames : (timed is SpecialPowerModuleBase known ? known.AvailableOnFrame(_world) : _world.Tick) + frames);
                    if (NamedEntity(Str(p, 0)) is { } timedOwner) _world.MarkDirty(timedOwner);
                }
                return true;
            case "NAMED_STOP_SPECIAL_POWER_COUNTDOWN":
            case "NAMED_START_SPECIAL_POWER_COUNTDOWN":
                // pauseCountdown(TRUE) stops, pauseCountdown(FALSE) resumes; the module counts nested pauses.
                if (Has(type, p, 2) && PowerModuleOf(Str(p, 0), Str(p, 1)) is { } paused) paused.PauseCountdown(_world, type == "NAMED_STOP_SPECIAL_POWER_COUNTDOWN");
                return true;

            // ---- named map reveals (createNamedMapReveal / doNamedMapReveal / undoNamedMapReveal)
            case "MAP_REVEAL_PERMANENTLY_AT_WAYPOINT":
                if (Has(type, p, 4) && WaypointOrDiagnose(Str(p, 0)) is { } revealSpot)
                {
                    var players = MapPlayers(Str(p, 2)).ToArray();
                    if (players.Length == 0) return true;
                    _namedReveals[Str(p, 3)] = new NamedReveal(players[0].Value, revealSpot.X, revealSpot.Y, Real(p, 1));
                    _world.RevealAreaPermanently(players[0], revealSpot.X, revealSpot.Y, Real(p, 1), true);
                }
                return true;
            case "MAP_UNDO_REVEAL_PERMANENTLY_AT_WAYPOINT":
                if (Has(type, p, 1) && _namedReveals.Remove(Str(p, 0), out var undone)) _world.RevealAreaPermanently(new PlayerId(undone.Player), undone.X, undone.Y, undone.Radius, false);
                return true;

            // ---- doBuildUpgrade -> Player::buildUpgrade -> AIPlayer::buildUpgrade: at the first building of the build list that can research it; a human player has no AIPlayer, nothing happens
            case "AI_PLAYER_BUILD_UPGRADE":
                if (Has(type, p, 2) && ResolvePlayer(Str(p, 0)) is { } upgrader && _world.HasPlayer(upgrader.Id))
                {
                    if (!_world.Upgrades.TryGet(Str(p, 1), out var upgrade)) { Diagnose($"unknown upgrade '{Str(p, 1)}'"); return true; }
                    if (upgrade.Scope != Templates.UpgradeScope.Player) { Diagnose($"AI_PLAYER_BUILD_UPGRADE: '{upgrade.Name}' is an object upgrade"); return true; }
                    if (_world.PlayerUpgrades(upgrader.Id).Has(upgrade.Index) || _world.UpgradeInProduction(upgrader.Id, upgrade.Index)) return true;
                    if (AiOfPlayer(upgrader.Id) is { } researcher) AiBuildUpgrade(researcher, upgrade);
                }
                return true;

            // ---- recruiting (AIPlayer::recruitSpecificAITeam, Team::setRecruitable)
            case "RECRUIT_TEAM":
                if (Has(type, p, 2)) RecruitTeam(Str(p, 0), Real(p, 1));
                return true;
            case "TEAM_AVAILABLE_FOR_RECRUITMENT":
                if (Has(type, p, 2) && FindTeam(Str(p, 0)) is { } recruitable)
                {
                    recruitable.RecruitableSet = true;
                    recruitable.Recruitable = Int(p, 1) != 0;
                }
                return true;

            // ---- recorded only
            case "PLAYER_AFFECT_RECEIVING_EXPERIENCE":
                if (Has(type, p, 2) && ResolvePlayer(Str(p, 0)) is { } learner) _world.SetSkillPointsModifier(learner.Id, Real(p, 1));
                return true;
            case "OBJECT_ALLOW_BONUSES":
                if (Has(type, p, 1)) _world.SetObjectsReceiveDifficultyBonus(Int(p, 0) != 0);
                return true;
            default:
                return TryExecuteTeamAbilityAction(action);
        }
    }

    private ISpecialPowerModule? PowerModuleOf(string unit, string power)
    {
        if (NamedEntity(unit) is not { } entity) return null;
        if (!_world.SpecialPowers.TryGet(power, out var info)) { Diagnose($"unknown special power '{power}'"); return null; }
        return SpecialPowerRouting.FindModule(entity, info.Index);
    }

    private void FireSpecialPower(Entity user, string power, SpecialPowerTargetKind kind, EntityId target, Fixed x, Fixed y)
    {
        if (user.Owner.IsSystem) { Diagnose("special power use for a neutral object"); return; }
        if (!_world.SpecialPowers.TryGet(power, out var info)) { Diagnose($"unknown special power '{power}'"); return; }
        var owner = user.Owner;
        var source = user.Id;
        if (!_issue(owner, (sequence, tick) => new UseSpecialPowerCommand(owner, sequence, tick, source, info.Name, kind, target, x, y)).Accepted) Diagnose("special power use rejected");
    }

    /// <summary><c>doSkirmishFireSpecialPowerAtMostCost</c>; the place is the AI's (<see cref="AiFireAtMostCost"/>), and a player without an AI has none (<c>Player::computeSuperweaponTarget</c>).</summary>
    private void FireAtMostCost(PlayerId player, string power)
    {
        if (!_world.SpecialPowers.TryGet(power, out var info)) { Diagnose($"unknown special power '{power}'"); return; }
        AiFireAtMostCost(player, info);
    }

    // ------------------------------------------------------------------------------------------------ recruiting

    /// <summary>
    /// <c>Player::recruitSpecificTeam</c> -> <c>AIPlayer::recruitSpecificAITeam</c> / <c>Team::tryToRecruit</c>: for each unit type of the team (up to <c>MaxCount</c>) the nearest unit of the
    /// owner that sits in an active, recruitable team of lower production priority (or in no team at all, the reference's default team) joins the new team and walks to the team's home
    /// waypoint. A radius under 1 means unlimited. A human player has no <c>AIPlayer</c>: nothing happens. A unit a script made unrecruitable (the panel flag "AI Recruitable") is left
    /// out. "Build variation" equivalence is not modelled, held units are not excluded
    /// (<c>docs/SCRIPT-ENGINE.md</c> §8).
    /// </summary>
    private void RecruitTeam(string teamName, Fixed radius)
    {
        var prototype = _program.FindTeam(teamName);
        if (prototype is null) { Diagnose($"unknown team '{teamName}'"); return; }
        var owner = _program.FindPlayer(prototype.Owner);
        if (owner is null || !_world.HasPlayer(owner.Id)) { Diagnose($"RECRUIT_TEAM '{teamName}': team has no player"); return; }
        if (AiOfPlayer(owner.Id) is null) { Diagnose($"RECRUIT_TEAM '{teamName}': a human player has no AI"); return; }
        if (prototype.IsSingleton && FindTeamInstance(teamName) is { } existing && LiveMembers(existing).Any(CountsAsObject)) { Diagnose($"RECRUIT_TEAM '{teamName}': singleton team already exists"); return; }
        // recruitSpecificAITeam reports a team without a home and goes on with m_homeLocation, which is the origin of the map.
        var home = (prototype.Home.Length > 0 ? _program.FindWaypoint(prototype.Home) : null) ?? new ScriptWaypoint(string.Empty, Fixed.Zero, Fixed.Zero, Fixed.Zero);
        if (CreateTeam(teamName, another: true) is not { } team) return;
        var maxDistanceSquared = radius < Fixed.One ? SquaredReach.OfSquare(Fixed.MaxValue) : SquaredReach.Of(radius);
        var recruited = 0;
        foreach (var unit in prototype.Units)
        {
            if (!_world.Templates.TryGet(unit.Type, out var template)) { Diagnose($"unknown object type '{unit.Type}'"); continue; }
            for (var count = unit.MaxCount; count > 0; count--)
            {
                var recruit = FindRecruit(owner.Id, template.Name, prototype, team, home, maxDistanceSquared);
                if (recruit is null) break;
                foreach (var other in _teams.Values) other.Members.Remove(recruit.Id.Value);
                team.Members.Add(recruit.Id.Value);
                _world.ClearOwnAttitude(recruit);
                if (team.AttackPriority.Length > 0) _prioritySetOf[recruit.Id.Value] = team.AttackPriority;
                if (recruit.Template.Locomotor is not null) MoveTo(recruit, home.X, home.Y);
                recruited++;
            }
        }
        if (recruited > 0) EnqueueReadyTeam(team);
        else
        {
            Diagnose($"RECRUIT_TEAM '{teamName}': recruited no units");
            if (!(prototype.IsSingleton || prototype.MaxInstances < 2)) _teams.Remove(team.Id);
        }
    }

    private Entity? FindRecruit(PlayerId owner, string template, ScriptTeamPrototype prototype, ScriptTeam into, ScriptWaypoint home, SquaredReach maxDistanceSquared)
    {
        Entity? recruit = null;
        var bestDistance = maxDistanceSquared;
        foreach (var candidate in _world.EntitiesOwnedBy(owner).AsSpan())
        {
            if (candidate.Template.Name != template || !Counts(candidate)) continue;
            var current = FirstTeamOf(candidate.Id.Value);
            if (current is not null)
            {
                if (ReferenceEquals(current, into) || !current.Created) continue;
                var currentPrototype = _program.FindTeam(current.Prototype);
                if (currentPrototype is not null && ProductionPriorityOf(currentPrototype) >= ProductionPriorityOf(prototype)) continue;
                // The default team of a player is "team" + the player's name (Player::getDefaultTeam); its units are always recruitable.
                var recruitable = (currentPrototype?.AiRecruitable ?? false) || (currentPrototype is not null && currentPrototype.Name == "team" + currentPrototype.Owner);
                if (current.RecruitableSet) recruitable = current.Recruitable;
                if (!recruitable) continue;
            }
            // Team::tryToRecruit: "can't recruit this unit".
            if (!IsRecruitable(candidate)) continue;
            var distance = DistanceSquared(candidate, home.X, home.Y);
            // A unit of the default team is taken even outside the radius while nothing was found yet, then nearer ones win (Team::tryToRecruit).
            var inDefaultTeam = current is null || (_program.FindTeam(current.Prototype) is { } ownTeam && ownTeam.Name == "team" + ownTeam.Owner);
            if (inDefaultTeam && recruit is null) { recruit = candidate; bestDistance = SquaredReach.OfSquare(distance); continue; }
            if (!bestDistance.Contains(distance)) continue;
            bestDistance = SquaredReach.OfSquare(distance);
            recruit = candidate;
        }
        return recruit;
    }

    private void DescribePowerState(IStateWriter writer)
    {
        if (_namedReveals.Count > 0)
        {
            writer.BeginArray("namedReveals");
            foreach (var (name, reveal) in _namedReveals)
            {
                writer.BeginObject(null);
                writer.Text("name", name);
                writer.Int("player", reveal.Player);
                writer.Fixed("x", reveal.X);
                writer.Fixed("y", reveal.Y);
                writer.Fixed("radius", reveal.Radius);
                writer.EndObject();
            }
            writer.EndArray();
        }
    }

    private void LoadPowerState(IStateReader reader)
    {
        _namedReveals.Clear();
        if (reader.Has("namedReveals"))
            foreach (var item in reader.Array("namedReveals"))
                _namedReveals[item.Text("name")] = new NamedReveal(checked((int)item.Int("player")), item.Fixed("x"), item.Fixed("y"), item.Fixed("radius"));
    }
}
