using System.Collections.Immutable;
using CnCNew.Data.World;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Testing;

/// <summary>What the objective driver did and whether the mission's own scripts ended it.</summary>
/// <param name="ReachedByScript">The mission ended with the wanted outcome by a script's own <c>VICTORY</c>/<c>DEFEAT</c> action (no generic elimination was applied).</param>
/// <param name="OutcomeScript">The script whose action decided the mission (empty when none did).</param>
/// <param name="Interventions">How many world changes the driver applied; <paramref name="Log"/> names each.</param>
/// <param name="Unmet">When the ending was not reached: per ending script the conditions that were false at the end.</param>
public sealed record ObjectiveDriveResult(bool ReachedByScript, MissionOutcome Outcome, string OutcomeScript, long OutcomeTick, int Interventions, ImmutableArray<string> Log, ImmutableArray<string> Unmet);

/// <summary>
/// A headless "objective driver" (<c>docs/SCRIPT-ENGINE.md</c> §9): instead of eliminating a side as <see cref="MissionProbe"/> does, it reads the world-state conditions the
/// mission's scripts ask about (a named object destroyed, a team destroyed, a unit inside an area, credits, ownership) and satisfies them one at a time, a few ticks apart, the
/// way a player pursuing the objectives would — then lets the scripts run and reports whether <em>their own</em> ending action fired. Test and tool code, not simulation
/// behaviour: it writes to the world directly (like the probe) and through the session's command counter (orders). What it cannot do (spawn objects the map never made, make
/// a script's timer run faster) it does not try; an ending it does not reach is reported with the conditions that stayed false.
/// <para>
/// For a victory it acts on everything that is not the viewer's or an ally's (enemies and neutrals are the targets of "destroyed" conditions, the viewer's own units go where
/// "inside area" conditions send them); for a defeat it acts on the viewer's and allies' objects. The heuristics are deliberately simple and listed here, so a mission that
/// stays unmet shows what a smarter driver would have to add.
/// </para>
/// </summary>
public static class MissionObjectiveDriver
{
    private sealed record Intervention(string Label, Func<bool> TryApply);

    public static ObjectiveDriveResult Run(MissionOptions options, TemplateCatalog catalog, string fingerprint, TerrainMap terrain, MapData map, bool victory, long maxTicks = 4500, int spacing = 15, long settleTicks = 60)
    {
        var session = MissionSession.Start(options, catalog, fingerprint, terrain, map);
        var local = MissionSession.LocalPlayerOf(session.Program, options.HumanSideName);
        var world = session.World;
        var engine = session.Engine;
        var program = session.Program;
        var wanted = victory ? MissionOutcome.Victory : MissionOutcome.Defeat;
        var pending = BuildInterventions(session, local, victory);
        var log = ImmutableArray.CreateBuilder<string>();
        var applied = 0;

        while (world.Tick < settleTicks && !session.IsOver) session.Step();
        long nextAt = world.Tick;
        while (world.Tick < maxTicks && !session.IsOver && engine.Outcome == MissionOutcome.None)
        {
            if (world.Tick >= nextAt && pending.Count > 0)
            {
                // Apply the first intervention whose target exists now; the others wait (their objects may be created by scripts later).
                for (var index = 0; index < pending.Count; index++)
                {
                    if (!pending[index].TryApply()) continue;
                    log.Add($"{world.Tick}: {pending[index].Label}");
                    pending.RemoveAt(index);
                    applied++;
                    break;
                }
                nextAt = world.Tick + spacing;
            }
            session.Step();
        }
        // An outcome decided by a script still needs its end-game delay to finish; the mission is "decided" at the action, which is what counts.
        var actionName = victory ? "VICTORY" : "DEFEAT";
        var unmet = ImmutableArray.CreateBuilder<string>();
        if (engine.Outcome != wanted)
        {
            var ending = program.Units.Where(unit => unit.Script.Actions.Concat(unit.Script.FalseActions).Any(action => action.TypeName == actionName || (victory && action.TypeName == "QUICKVICTORY"))).Take(6);
            foreach (var unit in ending)
            {
                unmet.Add($"{unit.Name}{(engine.IsScriptActive(unit.Name) ? "" : " [inactive]")}: " +
                          string.Join(", ", engine.ExplainScript(unit.Name).Where(item => !item.Result).Select(item => item.Label)));
                ExplainChain(session, unit, 1, new HashSet<string>(StringComparer.Ordinal) { unit.Name }, unmet);
            }
        }
        return new ObjectiveDriveResult(engine.Outcome == wanted, engine.Outcome, engine.OutcomeScript, engine.OutcomeTick, applied, log.ToImmutable(), unmet.ToImmutable());
    }

    /// <summary>
    /// Why an ending script did not fire, one level deeper per call: for each of its false <c>TIMER_EXPIRED</c>/<c>COUNTER</c>/<c>FLAG</c> conditions, the scripts that set that value
    /// and what is still false for them (depth-limited; a diagnostic of the end state, so evaluating conditions here costs nothing).
    /// </summary>
    private static void ExplainChain(MissionSession session, ScriptUnit unit, int depth, HashSet<string> seen, ImmutableArray<string>.Builder lines)
    {
        if (depth > 3) return;
        var engine = session.Engine;
        foreach (var (label, result) in engine.ExplainScript(unit.Name))
        {
            if (result) continue;
            var open = label.IndexOf(' ', StringComparison.Ordinal);
            var type = open < 0 ? label : label[(open + 1)..];
            if (type != "TIMER_EXPIRED" && type != "COUNTER" && type != "FLAG") continue;
            foreach (var or in unit.Script.OrConditions)
                foreach (var condition in or.AndConditions)
                {
                    if (condition.TypeName != type || condition.Parameters.IsDefaultOrEmpty) continue;
                    var name = condition.Parameters[0].Text;
                    foreach (var setter in session.Program.Units.Where(candidate => candidate.Script.Actions.Concat(candidate.Script.FalseActions).Any(action => action.Parameters.Length > 0
                                 && ((action.TypeName is "SET_TIMER" or "SET_MILLISECOND_TIMER" or "SET_RANDOM_TIMER" or "SET_RANDOM_MSEC_TIMER" or "SET_COUNTER" or "SET_FLAG" or "RESTART_TIMER") && action.Parameters[0].Text == name
                                     || (action.TypeName is "INCREMENT_COUNTER" or "DECREMENT_COUNTER") && action.Parameters.Length > 1 && action.Parameters[1].Text == name)))
                                 .Take(4))
                    {
                        if (!seen.Add(setter.Name)) continue;
                        var state = engine.IsScriptActive(setter.Name) ? "" : " [inactive]";
                        lines.Add($"{new string(' ', depth * 2)}needs {type} '{name}' <- {setter.Name}{state}: " +
                                  string.Join(", ", engine.ExplainScript(setter.Name).Where(item => !item.Result).Select(item => item.Label)));
                        ExplainChain(session, setter, depth + 1, seen, lines);
                    }
                }
        }
    }

    // ------------------------------------------------------------------------------------------------ the interventions

    private static List<Intervention> BuildInterventions(MissionSession session, PlayerId local, bool victory)
    {
        var world = session.World;
        var engine = session.Engine;
        var program = session.Program;
        var list = new List<Intervention>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        bool Friendly(PlayerId owner) => owner == local || world.AreAllies(local, owner);
        // The side a destroy-type condition is worth satisfying for: enemies and neutrals for a victory, the viewer's own side for a defeat.
        bool Targeted(PlayerId owner) => victory ? !Friendly(owner) : owner == local || world.AreAllies(local, owner);

        Entity? Named(string name) => engine.NamedObjects.Where(item => item.Key == name).Select(item => world.FindEntity(new EntityId(item.Value))).FirstOrDefault(entity => entity is not null);
        PlayerId? PlayerNamed(string name) => program.FindPlayer(name) is { } player ? player.Id : null;
        IEnumerable<Entity> TeamMembers(string name) => engine.TeamInstances.Where(team => team.Prototype == name).SelectMany(team => team.Members)
            .Select(id => world.FindEntity(new EntityId(id))).Where(entity => entity is not null && !entity.EffectivelyDead)!;

        void Add(string key, string label, Func<bool> apply)
        {
            if (seen.Add(key)) list.Add(new Intervention(label, apply));
        }

        (Fixed X, Fixed Y)? AreaCentre(string name)
        {
            if (program.FindArea(name) is not { Points.Length: > 0 } area) return null;
            long x = 0, y = 0;
            foreach (var (px, py) in area.Points) { x += px; y += py; }
            return (Fixed.FromInt(x / area.Points.Length), Fixed.FromInt(y / area.Points.Length));
        }

        bool Send(Entity entity, (Fixed X, Fixed Y) spot)
        {
            if (entity.Owner.IsSystem || entity.Template.Locomotor is null) return false;
            var owner = entity.Owner;
            var id = entity.Id;
            return session.Issue(owner, (sequence, tick) => new SetMoveTargetCommand(owner, sequence, tick, id, spot.X, spot.Y)).Accepted;
        }

        foreach (var unit in program.Units)
        {
            foreach (var or in unit.Script.OrConditions)
            {
                foreach (var condition in or.AndConditions)
                {
                    var p = condition.Parameters;
                    string Text(int index) => index < p.Length ? p[index].Text : string.Empty;
                    var type = condition.TypeName;
                    switch (type)
                    {
                        case "NAMED_DESTROYED":
                        case "NAMED_DYING":
                        case "NAMED_TOTALLY_DEAD":
                        {
                            var name = Text(0);
                            Add("kill:" + name, $"destroy named '{name}'", () =>
                            {
                                if (Named(name) is not { } entity) return false;
                                if (!Targeted(entity.Owner)) return true;
                                world.Kill(entity.Id);
                                return true;
                            });
                            break;
                        }
                        case "TEAM_DESTROYED":
                        {
                            var name = Text(0);
                            Add("killteam:" + name, $"destroy team '{name}'", () =>
                            {
                                var members = TeamMembers(name).ToArray();
                                if (members.Length == 0) return false;
                                foreach (var member in members)
                                    if (Targeted(member.Owner)) world.Kill(member.Id);
                                return true;
                            });
                            break;
                        }
                        case "PLAYER_ALL_DESTROYED":
                        case "PLAYER_ALL_BUILDFACILITIES_DESTROYED":
                        {
                            var name = Text(0);
                            Add("killplayer:" + name, $"destroy everything of '{name}'", () =>
                            {
                                if (PlayerNamed(name) is not { } player || !Targeted(player)) return true;
                                var objects = world.Entities.Where(entity => entity.Owner == player && !entity.EffectivelyDead).Select(entity => entity.Id).ToArray();
                                if (objects.Length == 0) return false;
                                foreach (var id in objects) world.Kill(id);
                                return true;
                            });
                            break;
                        }
                        case "MULTIPLAYER_ALLIED_VICTORY":
                            if (victory)
                                Add("killenemies", "destroy every enemy object", () =>
                                {
                                    foreach (var id in world.Entities.Where(entity => !entity.Owner.IsSystem && world.AreEnemies(local, entity.Owner)).Select(entity => entity.Id).ToArray()) world.Kill(id);
                                    return true;
                                });
                            break;
                        case "NAMED_INSIDE_AREA":
                        case "NAMED_ENTERED_AREA":
                        {
                            var name = Text(0);
                            var area = Text(1);
                            Add($"go:{name}:{area}", $"send named '{name}' to area '{area}'", () =>
                            {
                                if (Named(name) is not { } entity || AreaCentre(area) is not { } spot) return false;
                                return Friendly(entity.Owner) == victory && Send(entity, spot);
                            });
                            break;
                        }
                        case "TEAM_INSIDE_AREA_ENTIRELY":
                        case "TEAM_INSIDE_AREA_PARTIALLY":
                        case "TEAM_ENTERED_AREA_ENTIRELY":
                        case "TEAM_ENTERED_AREA_PARTIALLY":
                        {
                            var name = Text(0);
                            var area = Text(1);
                            Add($"goteam:{name}:{area}", $"send team '{name}' to area '{area}'", () =>
                            {
                                if (AreaCentre(area) is not { } spot) return false;
                                var members = TeamMembers(name).Where(member => Friendly(member.Owner) == victory).ToArray();
                                if (members.Length == 0) return false;
                                var sent = false;
                                foreach (var member in members) sent |= Send(member, spot);
                                return sent;
                            });
                            break;
                        }
                        case "PLAYER_HAS_CREDITS":
                        {
                            var name = Text(2);
                            Add("money:" + name, $"give '{name}' credits", () =>
                            {
                                if (PlayerNamed(name) is not { } player || !world.HasPlayer(player)) return false;
                                if (victory) world.AddMoney(player, Fixed.FromInt(50000));
                                return true;
                            });
                            break;
                        }
                        case "NAMED_OWNED_BY_PLAYER":
                        {
                            var name = Text(0);
                            var owner = Text(1);
                            Add($"own:{name}:{owner}", $"hand '{name}' to '{owner}'", () =>
                            {
                                if (Named(name) is not { } entity || PlayerNamed(owner) is not { } player) return false;
                                if (entity.Owner != player) world.TransferOwnership(entity.Id, player);
                                return true;
                            });
                            break;
                        }
                    }
                }
            }
        }
        return list;
    }
}
