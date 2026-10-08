using CnCNew.Data.World;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Scripting;

/// <summary>
/// Sequential scripts (<c>ScriptEngine::evaluateAndProgressAllSequentialScripts</c>, <c>appendSequentialScript</c>, <c>cleanupSequentialScript</c>,
/// <c>setSequentialTimer</c>; actions <c>TEAM/UNIT_EXECUTE_SEQUENTIAL_SCRIPT(_LOOPING)</c>, <c>*_STOP_SEQUENTIAL_SCRIPT</c>, <c>*_GUARD/IDLE/SPIN_FOR_FRAMECOUNT</c>,
/// <c>TEAM_WAIT_FOR_NOT_CONTAINED_*</c>, <c>SKIRMISH_WAIT_FOR_COMMANDBUTTON_AVAILABLE_*</c>): the way campaign scripts drive a team or a unit through a list of
/// orders. A script's actions are not run at once; one instruction runs whenever the team (or unit) is idle and no wait timer is pending, and several
/// instructions that leave it idle (setting a flag, say) run in the same tick.
/// </summary>
public sealed partial class ScriptEngine
{
    private const string ThisObjectName = "<This Object>";
    private const int MaxSequentialSpin = 256;

    /// <summary>One script of a runner's queue (<c>SequentialScript</c>): which script, how often it repeats (-1: forever), where it is and how long it waits.</summary>
    private sealed class SequenceItem
    {
        public int ScriptIndex;
        public int Loops;
        public int Instruction = -1;
        public long FramesToWait = -1;
        public bool DontAdvance;
    }

    /// <summary>The scripts queued for one team or one object; the first runs, the rest wait (<c>m_nextScriptInSequence</c>).</summary>
    private sealed class SequenceRunner
    {
        public int TeamId;
        public uint ObjectId;
        public readonly List<SequenceItem> Queue = [];
    }

    private readonly List<SequenceRunner> _sequences = [];

    /// <summary>Objects that were given a real order this tick; the order reaches the world only at the next step, but the reference's AI is busy at once, so these never count as idle in the tick they were ordered.</summary>
    private readonly HashSet<uint> _busyThisTick = [];

    private Entity? _callingObject;

    private bool TryExecuteSequentialAction(MapScriptAction action)
    {
        var p = action.Parameters;
        var type = action.TypeName;
        switch (type)
        {
            case "TEAM_EXECUTE_SEQUENTIAL_SCRIPT":
            case "TEAM_EXECUTE_SEQUENTIAL_SCRIPT_LOOPING":
                if (!Has(type, p, type.EndsWith("LOOPING", StringComparison.Ordinal) ? 3 : 2)) return true;
                if (FindTeam(Str(p, 0)) is not { } team) return true;
                if (_program.FindScript(Str(p, 1)) is not { } teamScript) { Diagnose($"sequential script '{Str(p, 1)}' does not exist"); return true; }
                // doTeamStartSequentialScript idles the team first so the script starts executing; the loop parameter counts runs, the engine counts repeats (-1 = forever).
                foreach (var member in LiveMembers(team).ToArray()) Stop(member);
                AppendSequence(team.Id, 0, teamScript, type.EndsWith("LOOPING", StringComparison.Ordinal) ? Int(p, 2) - 1 : 0);
                return true;
            case "UNIT_EXECUTE_SEQUENTIAL_SCRIPT":
            case "UNIT_EXECUTE_SEQUENTIAL_SCRIPT_LOOPING":
                if (!Has(type, p, type.EndsWith("LOOPING", StringComparison.Ordinal) ? 3 : 2)) return true;
                if (NamedEntity(Str(p, 0)) is not { } unit) return true;
                if (_program.FindScript(Str(p, 1)) is not { } unitScript) { Diagnose($"sequential script '{Str(p, 1)}' does not exist"); return true; }
                AppendSequence(0, unit.Id.Value, unitScript, type.EndsWith("LOOPING", StringComparison.Ordinal) ? Int(p, 2) - 1 : 0);
                return true;
            case "TEAM_STOP_SEQUENTIAL_SCRIPT":
                if (Has(type, p, 1) && FindTeam(Str(p, 0)) is { } stopped) _sequences.RemoveAll(runner => runner.TeamId == stopped.Id);
                return true;
            case "UNIT_STOP_SEQUENTIAL_SCRIPT":
                if (Has(type, p, 1) && NamedEntity(Str(p, 0)) is { } stoppedUnit) _sequences.RemoveAll(runner => runner.ObjectId == stoppedUnit.Id.Value);
                return true;
            case "TEAM_GUARD_FOR_FRAMECOUNT":
            case "TEAM_IDLE_FOR_FRAMECOUNT":
                // doTeamIdleForFramecount (the reference maps TEAM_GUARD_FOR_FRAMECOUNT to it too): the team stops, then the sequence waits.
                if (Has(type, p, 2) && FindTeam(Str(p, 0)) is { } waiting)
                {
                    foreach (var member in LiveMembers(waiting).ToArray()) Stop(member);
                    SetSequenceTimer(waiting.Id, 0, Int(p, 1));
                }
                return true;
            case "TEAM_SPIN_FOR_FRAMECOUNT":
                if (Has(type, p, 2) && FindTeam(Str(p, 0)) is { } spinning) SetSequenceTimer(spinning.Id, 0, Int(p, 1));
                return true;
            case "UNIT_GUARD_FOR_FRAMECOUNT":
            case "UNIT_IDLE_FOR_FRAMECOUNT":
                if (Has(type, p, 2) && NamedEntity(Str(p, 0)) is { } idler)
                {
                    Stop(idler);
                    SetSequenceTimer(0, idler.Id.Value, Int(p, 1));
                }
                return true;
            case "TEAM_WAIT_FOR_NOT_CONTAINED_ALL":
            case "TEAM_WAIT_FOR_NOT_CONTAINED_PARTIAL":
            case "SKIRMISH_WAIT_FOR_COMMANDBUTTON_AVAILABLE_ALL":
            case "SKIRMISH_WAIT_FOR_COMMANDBUTTON_AVAILABLE_PARTIAL":
                // The reference never executes these outside a sequential script ("should never be used"); the runner handles them itself.
                Diagnose($"{type} outside a sequential script");
                return true;
            default:
                return TryExecutePowerAction(action);
        }
    }

    /// <summary><c>appendSequentialScript</c>: the script joins the queue of the runner of that team/object, or starts a runner.</summary>
    private void AppendSequence(int teamId, uint objectId, ScriptUnit script, int loops)
    {
        var runner = _sequences.FirstOrDefault(candidate => (objectId != 0 && candidate.ObjectId == objectId) || (teamId != 0 && candidate.TeamId == teamId));
        if (runner is null) _sequences.Add(runner = new SequenceRunner { TeamId = teamId, ObjectId = objectId });
        runner.Queue.Add(new SequenceItem { ScriptIndex = script.Index, Loops = loops });
    }

    /// <summary><c>setSequentialTimer</c>: the first runner of the team/object waits that many frames before its next instruction.</summary>
    /// <param name="frames">The wait in logic frames of the original, as the map states it (<c>*_FOR_FRAMECOUNT</c>); the countdown runs once per update of the engine, which is once per frame.</param>
    private void SetSequenceTimer(int teamId, uint objectId, int frames)
    {
        foreach (var runner in _sequences)
            if ((objectId != 0 && runner.ObjectId == objectId) || (teamId != 0 && runner.TeamId == teamId))
            {
                if (runner.Queue.Count > 0) runner.Queue[0].FramesToWait = frames;
                return;
            }
    }

    private bool IsIdle(Entity entity) => !Marching(entity) && !_followers.ContainsKey(entity.Id.Value) && !_busyThisTick.Contains(entity.Id.Value) && (AiOf(entity)?.IsIdle ?? true);

    /// <summary>
    /// <c>evaluateAndProgressAllSequentialScripts</c>. A runner whose team or object is gone is dropped with everything queued behind it; otherwise, while the group is idle
    /// (or a timer ran to exactly 0) one instruction runs, and the same runner is looked at again in the same tick as long as it stays idle.
    /// </summary>
    private void ProcessSequences()
    {
        if (_sequences.Count == 0) return;
        var previousTeam = _callingTeam;
        var previousObject = _callingObject;
        for (var index = 0; index < _sequences.Count;)
        {
            var runner = _sequences[index];
            var spin = 0;
            var removed = false;
            while (runner.Queue.Count > 0 && spin++ < MaxSequentialSpin)
            {
                var team = runner.TeamId != 0 && _teams.TryGetValue(runner.TeamId, out var found) ? found : null;
                var entity = runner.ObjectId != 0 ? _world.FindEntity(new EntityId(runner.ObjectId)) : null;
                if (entity is { DestroyRequested: true }) entity = null;
                if (team is null && entity is null) { Remove(); break; }
                var members = team is not null ? LiveMembers(team).Where(member => !member.EffectivelyDead).ToArray() : [];
                if (team is not null && members.Length == 0) { Remove(); break; }
                var item = runner.Queue[0];
                var idle = team is not null ? members.All(IsIdle) : entity is not null && IsIdle(entity);
                if (!((idle && item.FramesToWait < 1) || item.FramesToWait == 0))
                {
                    if (item.FramesToWait > 0) item.FramesToWait--;
                    break;
                }
                if (item.DontAdvance) item.DontAdvance = false;
                else item.Instruction++;
                var actions = _program.Units[item.ScriptIndex].Script.Actions;
                if (item.Instruction >= actions.Length)
                {
                    // The script ended: repeat it (appended behind the queue, so queued scripts get their turn) or drop it.
                    if (item.Loops != 0)
                    {
                        runner.Queue.Add(new SequenceItem { ScriptIndex = item.ScriptIndex, Loops = item.Loops > 0 ? item.Loops - 1 : -1 });
                    }
                    runner.Queue.RemoveAt(0);
                    continue;
                }
                item.FramesToWait = -1;
                _callingTeam = team;
                _callingObject = entity;
                var action = actions[item.Instruction];
                switch (action.TypeName)
                {
                    case "TEAM_WAIT_FOR_NOT_CONTAINED_ALL" or "TEAM_WAIT_FOR_NOT_CONTAINED_PARTIAL":
                        if (Has(action.TypeName, action.Parameters, 1) && TeamIsContained(Str(action.Parameters, 0), action.TypeName.EndsWith("ALL", StringComparison.Ordinal))) item.DontAdvance = true;
                        _executedActions++;
                        break;
                    case "SKIRMISH_WAIT_FOR_COMMANDBUTTON_AVAILABLE_ALL" or "SKIRMISH_WAIT_FOR_COMMANDBUTTON_AVAILABLE_PARTIAL":
                        if (Has(action.TypeName, action.Parameters, 3) && !CommandButtonReady(Str(action.Parameters, 1), Str(action.Parameters, 2), action.TypeName.EndsWith("ALL", StringComparison.Ordinal))) item.DontAdvance = true;
                        _executedActions++;
                        break;
                    default:
                        ExecuteAction(action);
                        break;
                }
                if (item.DontAdvance) break;
                // After the instruction, a dead group ends the runner (cleanupSequentialScript with danglers); otherwise loop on while still idle.
                if (team is not null && !LiveMembers(team).Any(member => !member.EffectivelyDead)) { Remove(); break; }
                if (entity is not null && (entity.EffectivelyDead || entity.DestroyRequested)) { Remove(); break; }
                void Remove() { _sequences.RemoveAt(index); removed = true; }
                if (runner.Queue.Count == 0) break;
                var stillIdle = team is not null ? LiveMembers(team).All(IsIdle) : entity is not null && IsIdle(entity);
                if (!stillIdle || runner.Queue[0].FramesToWait > 0) break;
            }
            if (spin > MaxSequentialSpin) Diagnose("sequential script appears to loop forever");
            if (!removed)
            {
                if (runner.Queue.Count == 0) _sequences.RemoveAt(index);
                else index++;
            }
        }
        _callingTeam = previousTeam;
        _callingObject = previousObject;
    }

    /// <summary><c>ScriptConditions::evaluateTeamIsContained</c>: every (or some) member sits inside a container; an empty team is not contained.</summary>
    private bool TeamIsContained(string teamName, bool all)
    {
        var team = FindTeam(teamName);
        if (team is null) return false;
        var considered = false;
        foreach (var member in LiveMembers(team))
        {
            var contained = member.ContainedBy.IsValid;
            if (contained) { if (!all) return true; }
            else if (all) return false;
            considered = true;
        }
        return considered && all;
    }

    /// <summary>
    /// <c>ScriptConditions::evaluateSkirmishCommandButtonIsReady</c> with <c>CommandButton::isReady</c>: every (or some) member that is concerned by the button has it ready. A special-power
    /// button concerns the members that carry the power (ready = recharged); an upgrade button concerns every member, and is ready for one that an upgrade module of it would react to
    /// (<c>affectedByUpgrade</c>: the module would execute with the object's, the player's and the button's upgrades together) and that does not have the upgrade yet.
    /// </summary>
    private bool CommandButtonReady(string teamName, string button, bool all)
    {
        var team = FindTeam(teamName);
        if (team is null) return false;
        if (_world.Templates.Commands.TryGetSpecialPowerAbility(button, out var power))
        {
            if (!_world.SpecialPowers.TryGet(power, out var info)) return false;
            foreach (var member in LiveMembers(team))
            {
                if (SpecialPowerRouting.FindModule(member, info.Index) is not { } module) continue;
                if (module.IsReady(_world)) { if (!all) return true; }
                else if (all) return false;
            }
            return all;
        }
        if (!_world.Templates.Commands.TryGetButton(button, out var buttonInfo))
        {
            Diagnose($"unknown command button '{button}'");
            return false;
        }
        if (buttonInfo.Upgrade.Length == 0 || !_world.Upgrades.TryGet(buttonInfo.Upgrade, out var upgrade)) return all;
        var upgradeBit = UpgradeMask.Bit(upgrade.Index);
        foreach (var member in LiveMembers(team))
        {
            var mask = member.CompletedUpgrades | _world.PlayerUpgrades(member.Owner) | upgradeBit;
            var ready = !member.CompletedUpgrades.TestForAll(upgradeBit) && member.ModuleArray.OfType<IUpgradeModule>().Any(module => module.WouldUpgrade(mask));
            if (ready) { if (!all) return true; }
            else if (all) return false;
        }
        return all;
    }

    private void DescribeSequences(IStateWriter writer)
    {
        if (_sequences.Count == 0) return;
        writer.BeginArray("sequences");
        foreach (var runner in _sequences)
        {
            writer.BeginObject(null);
            writer.Int("team", runner.TeamId);
            writer.UInt("object", runner.ObjectId);
            writer.BeginArray("queue");
            foreach (var item in runner.Queue)
            {
                writer.BeginObject(null);
                writer.Int("script", item.ScriptIndex);
                writer.Int("loops", item.Loops);
                writer.Int("instruction", item.Instruction);
                writer.Int("wait", item.FramesToWait);
                writer.Bool("hold", item.DontAdvance);
                writer.EndObject();
            }
            writer.EndArray();
            writer.EndObject();
        }
        writer.EndArray();
    }

    private void LoadSequences(IStateReader reader)
    {
        _sequences.Clear();
        _busyThisTick.Clear();
        if (!reader.Has("sequences")) return;
        foreach (var entry in reader.Array("sequences"))
        {
            var runner = new SequenceRunner { TeamId = checked((int)entry.Int("team")), ObjectId = checked((uint)entry.UInt("object")) };
            foreach (var item in entry.Array("queue"))
            {
                var script = checked((int)item.Int("script"));
                if (script < 0 || script >= _program.Units.Length) throw new InvalidDataException("Sequential script index out of range in script state.");
                runner.Queue.Add(new SequenceItem
                {
                    ScriptIndex = script, Loops = checked((int)item.Int("loops")), Instruction = checked((int)item.Int("instruction")),
                    FramesToWait = item.Int("wait"), DontAdvance = item.Bool("hold"),
                });
            }
            _sequences.Add(runner);
        }
    }
}
