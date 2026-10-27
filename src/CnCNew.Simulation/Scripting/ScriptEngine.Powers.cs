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

    /// <summary>Recorded-only state: <c>Player::setSkillPointsModifier</c> by player id (raw fixed value) and the last <c>OBJECT_ALLOW_BONUSES</c> choice (-1: never given).</summary>
    private readonly SortedDictionary<int, long> _skillPointsModifier = [];

    private int _objectBonusesEnabled = -1;
    private long _recordedActions;

    /// <summary>Executions of <see cref="ScriptSupport.Recorded"/> actions: stored, but no simulation rule reads them yet.</summary>
    public long RecordedActions => _recordedActions;

    public Fixed SkillPointsModifierOf(PlayerId player) => _skillPointsModifier.TryGetValue(player.Value, out var raw) ? Fixed.FromRaw(raw) : Fixed.One;

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

            // ---- doBuildUpgrade -> AIPlayer::buildUpgrade: a player upgrade is ordered like a player's own purchase (funds, prerequisites and build time apply) at the first of the
            // player's buildings that can research it - in id order, where the original walks its build list
            case "AI_PLAYER_BUILD_UPGRADE":
                if (Has(type, p, 2) && ResolvePlayer(Str(p, 0)) is { } upgrader && _world.HasPlayer(upgrader.Id))
                {
                    if (!_world.Upgrades.TryGet(Str(p, 1), out var upgrade)) { Diagnose($"unknown upgrade '{Str(p, 1)}'"); return true; }
                    if (upgrade.Scope != Templates.UpgradeScope.Player) { Diagnose($"AI_PLAYER_BUILD_UPGRADE: '{upgrade.Name}' is an object upgrade"); return true; }
                    if (_world.PlayerUpgrades(upgrader.Id).Has(upgrade.Index) || _world.UpgradeInProduction(upgrader.Id, upgrade.Index)) return true;
                    var buyer = upgrader.Id;
                    var name = upgrade.Name;
                    if (!_issue(buyer, (sequence, tick) => new PurchaseUpgradeCommand(buyer, sequence, tick, name, null)).Accepted) Diagnose($"AI_PLAYER_BUILD_UPGRADE '{name}' rejected");
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
                if (Has(type, p, 2) && ResolvePlayer(Str(p, 0)) is { } learner) _skillPointsModifier[learner.Id.Value] = Real(p, 1).Raw;
                _recordedActions++;
                return true;
            case "OBJECT_ALLOW_BONUSES":
                if (Has(type, p, 1)) _objectBonusesEnabled = Int(p, 0) != 0 ? 1 : 0;
                _recordedActions++;
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

    /// <summary>
    /// <c>doSkirmishFireSpecialPowerAtMostCost</c> with <c>AIPlayer::computeSuperweaponTarget</c> simplified: the reference scans a (at most 10 x 10) grid over the
    /// enemy's structure bounds in a random direction and picks the cell with the most enemy cash value; here the candidates are the enemies' own object positions and
    /// the winner is the one with the highest summed build cost within the power's radius (at least 50), lowest id on a tie. The first ready holder fires.
    /// </summary>
    private void FireAtMostCost(PlayerId player, string power)
    {
        if (!_world.SpecialPowers.TryGet(power, out var info)) { Diagnose($"unknown special power '{power}'"); return; }
        var radius = info.RadiusCursorRadius > Fixed.FromInt(50) ? info.RadiusCursorRadius : Fixed.FromInt(50);
        var limit = SquaredReach.Of(radius);
        var enemies = _world.Entities.Where(entity => !entity.Owner.IsSystem && CountsAsObject(entity) && _world.AreEnemies(player, entity.Owner)).OrderBy(entity => entity.Id.Value).ToArray();
        if (enemies.Length == 0) return;
        foreach (var holder in PlayerEntities(player))
        {
            if (holder.HasStatus(UnderConstructionBit) || SpecialPowerRouting.FindModule(holder, info.Index) is not { } module || !module.IsReady(_world)) continue;
            Entity? best = null;
            var bestCost = Fixed.FromInt(-1);
            foreach (var center in enemies)
            {
                var cost = Fixed.Zero;
                foreach (var other in enemies)
                    if (limit.Contains(DistanceSquared(other, center.X, center.Y))) cost += other.Template.BuildCost;
                if (cost > bestCost) { best = center; bestCost = cost; }
            }
            if (best is not null) FireSpecialPower(holder, power, SpecialPowerTargetKind.Location, EntityId.Invalid, best.X, best.Y);
            return;
        }
    }

    // ------------------------------------------------------------------------------------------------ recruiting

    /// <summary>
    /// <c>AIPlayer::recruitSpecificAITeam</c> / <c>Team::tryToRecruit</c>: for each unit type of the team (up to <c>MaxCount</c>) the nearest unit of the owner that sits in an
    /// active, recruitable team of lower production priority (or in no team at all, the reference's default team) joins the new team and walks to the team's home waypoint.
    /// A radius under 1 means unlimited. Simplifications (<c>docs/SCRIPT-ENGINE.md</c> §8): the team activates at once instead of after gathering at its home, "build variation"
    /// equivalence is not modelled, held units are not excluded.
    /// </summary>
    private void RecruitTeam(string teamName, Fixed radius)
    {
        var prototype = _program.FindTeam(teamName);
        if (prototype is null) { Diagnose($"unknown team '{teamName}'"); return; }
        var owner = _program.FindPlayer(prototype.Owner);
        if (owner is null || !_world.HasPlayer(owner.Id)) { Diagnose($"RECRUIT_TEAM '{teamName}': team has no player"); return; }
        var singleton = prototype.IsSingleton || prototype.MaxInstances < 2;
        if (singleton && FindTeamInstance(teamName) is { } existing && LiveMembers(existing).Any(CountsAsObject)) { Diagnose($"RECRUIT_TEAM '{teamName}': singleton team already exists"); return; }
        var home = prototype.Home.Length > 0 ? _program.FindWaypoint(prototype.Home) : null;
        if (home is null) { Diagnose($"RECRUIT_TEAM '{teamName}': team has no home position"); return; }
        var team = (singleton ? FindTeamInstance(teamName) : null) ?? CreateTeam(teamName);
        if (team is null) return;
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
                if (team.AttackPriority.Length > 0) _prioritySetOf[recruit.Id.Value] = team.AttackPriority;
                if (recruit.Template.Locomotor is not null) MoveTo(recruit, home.X, home.Y);
                recruited++;
            }
        }
        if (recruited > 0) EnqueueReadyTeam(team);
        else
        {
            Diagnose($"RECRUIT_TEAM '{teamName}': recruited no units");
            if (!singleton) _teams.Remove(team.Id);
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
        if (_skillPointsModifier.Count > 0)
        {
            writer.BeginArray("skillPointsModifier");
            foreach (var (player, raw) in _skillPointsModifier)
            {
                writer.BeginObject(null);
                writer.Int("player", player);
                writer.Int("raw", raw);
                writer.EndObject();
            }
            writer.EndArray();
        }
        if (_objectBonusesEnabled >= 0) writer.Int("objectBonuses", _objectBonusesEnabled);
        if (_recordedActions > 0) writer.Int("recordedActions", _recordedActions);
    }

    private void LoadPowerState(IStateReader reader)
    {
        _namedReveals.Clear();
        if (reader.Has("namedReveals"))
            foreach (var item in reader.Array("namedReveals"))
                _namedReveals[item.Text("name")] = new NamedReveal(checked((int)item.Int("player")), item.Fixed("x"), item.Fixed("y"), item.Fixed("radius"));
        _skillPointsModifier.Clear();
        if (reader.Has("skillPointsModifier"))
            foreach (var item in reader.Array("skillPointsModifier")) _skillPointsModifier[checked((int)item.Int("player"))] = item.Int("raw");
        _objectBonusesEnabled = reader.Has("objectBonuses") ? checked((int)reader.Int("objectBonuses")) : -1;
        _recordedActions = reader.Has("recordedActions") ? reader.Int("recordedActions") : 0;
    }
}
