using CnCNew.Data.World;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Scripting;

/// <summary>
/// Actions added in Phase 13 (<c>docs/SCRIPT-ENGINE.md</c> §5): <c>TEAM_FACE_NAMED</c>, <c>PLAYER_SELL_EVERYTHING</c> and the settings; what a mission's computer player has no
/// <c>AIPlayer</c> to read is <see cref="ScriptSupport.Recorded"/> (<c>SET_BASE_CONSTRUCTION_SPEED</c>, <c>PLAYER_SELECT_SKILLSET</c>), kept per subject in
/// <see cref="_recordedSettings"/> (written to the state only when one was set, so existing hashes stay).
/// </summary>
public sealed partial class ScriptEngine
{
    private readonly SortedDictionary<string, long> _recordedSettings = new(StringComparer.Ordinal);

    /// <summary>The value a recorded setting action stored for <paramref name="subject"/> (<c>ACTION|subject</c>), or <see langword="null"/>.</summary>
    public long? RecordedSetting(string action, string subject) => _recordedSettings.TryGetValue(action + "|" + subject, out var value) ? value : null;

    private static readonly string[] FactionStructureKinds = ["FS_FACTORY", "FS_BASE_DEFENSE", "FS_TECHNOLOGY", "FS_SUPPLY_DROPZONE", "FS_SUPERWEAPON", "FS_BLACK_MARKET", "FS_SUPPLY_CENTER", "FS_STRATEGY_CENTER", "FS_FAKE", "FS_INTERNET_CENTER", "FS_ADVANCED_TECH", "FS_BARRACKS", "FS_WARFACTORY", "FS_AIRFIELD"];

    /// <summary><c>sellBuildings</c> (Player.cpp): faction structures (<c>KINDOFMASK_FS</c>), command centres and power plants.</summary>
    private static bool IsSellable(Entity entity) => entity.Template.IsKindOf("STRUCTURE") && (entity.Template.IsKindOf("COMMANDCENTER") || entity.Template.IsKindOf("FS_POWER") || FactionStructureKinds.Any(entity.Template.IsKindOf));

    private bool TryExecuteSettingAction(MapScriptAction action)
    {
        var p = action.Parameters;
        var type = action.TypeName;
        switch (type)
        {
            // ScriptActions::doTeamFaceNamed: every member stops and faces the named object (the reference also clears the waypoint queue and leaves its group).
            case "TEAM_FACE_NAMED":
                if (Has(type, p, 2) && NamedEntity(Str(p, 1)) is { } faced)
                    foreach (var team in TeamsNamed(Str(p, 0)))
                        foreach (var member in LiveMembers(team).ToArray()) Face(member, faced.X, faced.Y);
                return true;
            // ScriptActions::doPlayerSellEverything -> Player::sellEverythingUnderTheSun: iterateObjects(sellBuildings): every faction structure, command centre and power plant is sold (a sell command each).
            case "PLAYER_SELL_EVERYTHING":
                if (Has(type, p, 1) && ResolvePlayer(Str(p, 0)) is { } seller)
                {
                    var owner = seller.Id;
                    foreach (var entity in _world.Entities.Where(entity => entity.Owner == owner && IsSellable(entity) && !entity.EffectivelyDead && !entity.DestroyRequested).OrderBy(entity => entity.Id.Value).ToArray())
                    {
                        var target = entity.Id;
                        _issue(owner, (sequence, tick) => new SellCommand(owner, sequence, tick, target));
                    }
                }
                return true;
            // doNamedSetStoppingDistance: Locomotor::setCloseEnoughDist on the object's current locomotor, from half a unit on.
            case "NAMED_SET_STOPPING_DISTANCE":
                if (Has(type, p, 2) && NamedEntity(Str(p, 0)) is { } stopper) SetStoppingDistance(stopper, Real(p, 1));
                return true;
            // doNamedSetRepulsor: the REPULSOR object status.
            case "NAMED_SET_REPULSOR":
                if (Has(type, p, 2) && NamedEntity(Str(p, 0)) is { } repulsive) _world.SetRepulsor(repulsive, Int(p, 1) != 0);
                return true;
            // updateBaseConstructionSpeed -> AIPlayer::setTeamDelaySeconds. Read by the original AI's team timer; a mission's computer player has no AIPlayer here, the value stays recorded.
            case "SET_BASE_CONSTRUCTION_SPEED":
                if (Has(type, p, 2) && ResolvePlayer(Str(p, 0)) is { } builder)
                {
                    if (AiOfPlayer(builder.Id) is { } teamTimer) teamTimer.TeamSeconds = Int(p, 1);
                    else
                    {
                        _recordedSettings[type + "|" + builder.Id.Value] = Int(p, 1);
                        _recordedActions++;
                    }
                }
                return true;
            // doNamedFireWeaponFollowingWaypointPath: from the waypoint closest to the unit to the end of the path.
            case "NAMED_FIRE_WEAPON_FOLLOWING_WAYPOINT_PATH":
                if (Has(type, p, 2) && NamedEntity(Str(p, 0)) is { } launcher && _program.FindPath(Str(p, 1)) is { Waypoints.IsDefaultOrEmpty: false } flightPath)
                    _world.FireFollowingPath(launcher, [..flightPath.Waypoints.Skip(ClosestWaypointIndex(launcher, flightPath)).Select(waypoint => (waypoint.X, waypoint.Y))]);
                return true;
            case "SKIRMISH_ATTACK_NEAREST_GROUP_WITH_VALUE":
                if (Has(type, p, 3) && FindTeam(Str(p, 0)) is { } valueHunters) AttackNearestGroupWithValue(valueHunters, Int(p, 1), Int(p, 2));
                return true;
            case "SKIRMISH_PERFORM_COMMANDBUTTON_ON_MOST_VALUABLE_OBJECT":
                if (Has(type, p, 3) && FindTeam(Str(p, 0)) is { } valueTakers) CommandButtonOnMostValuable(valueTakers, Str(p, 1), Real(p, 2));
                return true;
            default:
                return TryExecutePhase13Action(action);
        }
    }

    /// <summary>False for an object without an AI or a locomotor, where <c>doSetStoppingDistance</c> gives up on the rest of the team.</summary>
    private bool SetStoppingDistance(Entity entity, Fixed distance)
    {
        if (AiOf(entity) is not { } ai || entity.Template.Locomotor is null) return false;
        if (distance >= Fixed.FromDecimal(0.5m)) ai.SetStoppingDistance(_world, distance);
        return true;
    }

    private void DescribeSettingState(IStateWriter writer)
    {
        DescribePhase13State(writer);
        if (_chooseVictimAlwaysNormal) writer.Bool("chooseVictimAlwaysNormal", true);
        if (_recordedSettings.Count == 0) return;
        writer.BeginArray("recordedSettings");
        foreach (var (key, value) in _recordedSettings) { writer.BeginObject(null); writer.Text("key", key); writer.Int("value", value); writer.EndObject(); }
        writer.EndArray();
    }

    private void LoadSettingState(IStateReader reader)
    {
        LoadPhase13State(reader);
        _recordedSettings.Clear();
        _chooseVictimAlwaysNormal = reader.Has("chooseVictimAlwaysNormal") && reader.Bool("chooseVictimAlwaysNormal");
        if (reader.Has("recordedSettings")) foreach (var item in reader.Array("recordedSettings")) _recordedSettings[item.Text("key")] = item.Int("value");
    }
}
