using CnCNew.Data.World;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.State;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Scripting;

/// <summary>
/// Actions added in Phase 13 (<c>docs/SCRIPT-ENGINE.md</c> §5): <c>TEAM_FACE_NAMED</c> and <c>PLAYER_SELL_EVERYTHING</c> are executed; three settings without a consumer in the
/// simulation are <see cref="ScriptSupport.Recorded"/> (<c>NAMED_SET_STOPPING_DISTANCE</c>, <c>NAMED_SET_REPULSOR</c>, <c>SET_BASE_CONSTRUCTION_SPEED</c>), kept per subject in
/// <see cref="_recordedSettings"/> (written to the state only when one was set, so existing hashes stay).
/// </summary>
public sealed partial class ScriptEngine
{
    private readonly SortedDictionary<string, long> _recordedSettings = new(StringComparer.Ordinal);

    /// <summary>The value a recorded setting action stored for <paramref name="subject"/> (<c>ACTION|subject</c>), or <see langword="null"/>.</summary>
    public long? RecordedSetting(string action, string subject) => _recordedSettings.TryGetValue(action + "|" + subject, out var value) ? value : null;

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
            // ScriptActions::doPlayerSellEverything -> Player::sellEverythingUnderTheSun: BuildAssistant::sellObject for every object of the player's teams; here a sell command for each structure.
            case "PLAYER_SELL_EVERYTHING":
                if (Has(type, p, 1) && ResolvePlayer(Str(p, 0)) is { } seller)
                {
                    var owner = seller.Id;
                    foreach (var entity in _world.Entities.Where(entity => entity.Owner == owner && entity.Template.IsKindOf("STRUCTURE") && !entity.EffectivelyDead && !entity.DestroyRequested).OrderBy(entity => entity.Id.Value).ToArray())
                    {
                        var target = entity.Id;
                        _issue(owner, (sequence, tick) => new SellCommand(owner, sequence, tick, target));
                    }
                }
                return true;
            // doNamedSetStoppingDistance (Locomotor::setCloseEnoughDist), doNamedSetRepulsor (the REPULSOR object status), doSetBaseConstructionSpeed (the AI's delay between teams): no consumer.
            case "NAMED_SET_STOPPING_DISTANCE":
                if (Has(type, p, 2) && NamedEntity(Str(p, 0)) is { } stopper && Real(p, 1) >= Fixed.FromDecimal(0.5m)) _recordedSettings[type + "|" + stopper.Id.Value] = Real(p, 1).Raw;
                _recordedActions++;
                return true;
            case "NAMED_SET_REPULSOR":
                if (Has(type, p, 2) && NamedEntity(Str(p, 0)) is { } repulsive) _recordedSettings[type + "|" + repulsive.Id.Value] = Int(p, 1) != 0 ? 1 : 0;
                _recordedActions++;
                return true;
            case "SET_BASE_CONSTRUCTION_SPEED":
                if (Has(type, p, 2) && ResolvePlayer(Str(p, 0)) is { } builder) _recordedSettings[type + "|" + builder.Id.Value] = Int(p, 1);
                _recordedActions++;
                return true;
            default:
                return false;
        }
    }

    private void DescribeSettingState(IStateWriter writer)
    {
        if (_recordedSettings.Count == 0) return;
        writer.BeginArray("recordedSettings");
        foreach (var (key, value) in _recordedSettings) { writer.BeginObject(null); writer.Text("key", key); writer.Int("value", value); writer.EndObject(); }
        writer.EndArray();
    }

    private void LoadSettingState(IStateReader reader)
    {
        _recordedSettings.Clear();
        if (reader.Has("recordedSettings")) foreach (var item in reader.Array("recordedSettings")) _recordedSettings[item.Text("key")] = item.Int("value");
    }
}
