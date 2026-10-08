using System.Collections.Immutable;
using System.Globalization;
using CnCNew.Data.World;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.Skirmish;
using CnCNew.Simulation.Terrain;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.Testing;

public static partial class RateCases
{
    /// <summary>Builders of synthetic map scripts (no content of a shipped map): parameters, conditions, actions, scripts.</summary>
    private static class Scripted
    {
        public static MapScriptParameter Int(int value) => new(MapScriptParameterType.Int, value, 0m, string.Empty, 0m, 0m, 0m);
        public static MapScriptParameter Bool(bool value) => new(MapScriptParameterType.Boolean, value ? 1 : 0, 0m, string.Empty, 0m, 0m, 0m);
        public static MapScriptParameter Real(decimal value) => new(MapScriptParameterType.Real, 0, value, string.Empty, 0m, 0m, 0m);
        public static MapScriptParameter Named(MapScriptParameterType type, string text) => new(type, 0, 0m, text, 0m, 0m, 0m);
        public static MapScriptParameter Counter(string name) => Named(MapScriptParameterType.Counter, name);
        public static MapScriptParameter Flag(string name) => Named(MapScriptParameterType.Flag, name);
        public static MapScriptParameter Team(string name) => Named(MapScriptParameterType.Team, name);
        public static MapScriptParameter Waypoint(string name) => Named(MapScriptParameterType.Waypoint, name);
        public static MapScriptParameter Script(string name) => Named(MapScriptParameterType.Script, name);

        public static MapCondition If(string name, params MapScriptParameter[] parameters)
        {
            var id = MapScriptCatalog.ConditionTypeNames.IndexOf(name);
            return id >= 0 ? new MapCondition(id, name, [..parameters]) : throw new ArgumentException($"Unknown condition {name}.");
        }

        public static MapScriptAction Do(string name, params MapScriptParameter[] parameters)
        {
            var id = MapScriptCatalog.ScriptActionTypeNames.IndexOf(name);
            return id >= 0 ? new MapScriptAction(id, name, [..parameters]) : throw new ArgumentException($"Unknown action {name}.");
        }

        public static MapOrCondition Always => new([If("CONDITION_TRUE")]);
        public static MapOrCondition When(params MapCondition[] conditions) => new([..conditions]);

        public static MapScript Script(string name, MapOrCondition[] conditions, MapScriptAction[] actions, bool oneShot = false, bool active = true, int pauseSeconds = 0)
            => new(name, string.Empty, string.Empty, string.Empty, active, oneShot, true, true, true, false, pauseSeconds, [..conditions], [..actions], []);

        public static MapProperty Text(string key, string value) => new(key, MapPropertyType.Ascii, 0, 0m, value);
        public static MapProperty Flag(string key, bool value) => new(key, MapPropertyType.Bool, value ? 1 : 0, 0m, string.Empty);

        public static MapObject Placed(string template, int x, int y, string team, string name)
            => new(template, x, y, 0m, 0m, 0, new MapProperties([Text("originalOwner", team), Text("objectName", name)]));
    }

    private const string ScriptSide = "PlayerA", ScriptEnemy = "PlayerB", ScriptTeam = "Squad", ScriptSoldier = "AmericaInfantryRanger";

    /// <summary>A mission on the flat map of the scene: a human side with the scripts, a hostile side without, a squad of one Ranger, three waypoints 300 units apart.</summary>
    private static MissionSession StartMission(RateScene scene, MapScript[] scripts)
    {
        scene.Waypoints = [("Spawn", 1000, 2000), ("Mid", 1300, 2000), ("Far", 1600, 2000)];
        var flat = scene.Map.Map;
        MapSide Side(string name, bool human, string enemies)
            => new(new MapProperties([Scripted.Text("playerName", name), Scripted.Flag("playerIsHuman", human), Scripted.Text("playerAllies", string.Empty), Scripted.Text("playerEnemies", enemies)]), []);
        MapProperties Team(string name, string owner) => new([Scripted.Text("teamName", name), Scripted.Text("teamOwner", owner), Scripted.Flag("teamIsSingleton", true)]);
        var map = flat with
        {
            Sides = [new MapSide(new MapProperties([Scripted.Text("playerName", string.Empty)]), []), Side(ScriptSide, true, ScriptEnemy), Side(ScriptEnemy, false, ScriptSide)],
            Teams = [Team("team" + ScriptSide, ScriptSide), Team("team" + ScriptEnemy, ScriptEnemy), Team(ScriptTeam, ScriptSide)],
            Objects = [..flat.Objects, Scripted.Placed(ScriptSoldier, 1000, 2000, ScriptTeam, "Runner")],
            PlayerScripts = [new MapScriptList([], []), new MapScriptList([..scripts], []), new MapScriptList([], [])],
        };
        return MissionSession.Start(MissionOptions.Standard, scene.Catalog, scene.Fingerprint, TerrainMap.Build(map, "rate-equivalence-mission"), map);
    }

    private static RateCaseGroup ScriptCases(RateCatalogs catalogs)
    {
        var builder = new GroupBuilder(catalogs, Scripts);
        builder.Add("timers in seconds and in frames", MeasureScriptTimers, ScriptSoldier);
        builder.Add("counter per evaluation", MeasureScriptCounter, ScriptSoldier);
        builder.Add("evaluation delay", MeasureScriptDelay, ScriptSoldier);
        builder.Add("sequential script waits", MeasureScriptSequence, ScriptSoldier);
        builder.Add("end game delay", MeasureScriptEndGame, ScriptSoldier);
        builder.NotCovered("scripts: the 109 condition and 344 action types one by one, team build queues, reinforcements by transport, the scripts of the shipped campaign maps (step 4 of the phase compares those per map with the objective driver)");
        return builder.Done();
    }

    /// <summary>Steps the mission until the counter has the value, at most <paramref name="seconds"/> of game time; the ticks before the step that set it, or -1.</summary>
    private static long UntilCounter(MissionSession session, string counter, long value, long seconds)
    {
        var budget = session.World.Time.WholeSecondsToTicks(seconds);
        for (long step = 1; step <= budget; step++)
        {
            session.Step();
            if (session.Engine.CounterValue(counter) == value) return step - 1;
        }
        return -1;
    }

    private static ImmutableArray<RateQuantity> MeasureScriptTimers(RateScene scene)
    {
        var quantities = new RateQuantities(scene);
        foreach (var (label, set) in new[]
                 {
                     ("timer of 2.5 s (SET_MILLISECOND_TIMER)", Scripted.Do("SET_MILLISECOND_TIMER", Scripted.Counter("t"), Scripted.Real(2.5m))),
                     ("timer of 75 frames (SET_TIMER)", Scripted.Do("SET_TIMER", Scripted.Counter("t"), Scripted.Int(75))),
                     ("random timer of 40 to 80 frames (SET_RANDOM_TIMER)", Scripted.Do("SET_RANDOM_TIMER", Scripted.Counter("t"), Scripted.Int(40), Scripted.Int(80))),
                     ("random timer of 1 to 3 s (SET_RANDOM_MSEC_TIMER)", Scripted.Do("SET_RANDOM_MSEC_TIMER", Scripted.Counter("t"), Scripted.Real(1m), Scripted.Real(3m))),
                 })
        {
            var session = StartMission(scene.Fresh(), [
                Scripted.Script("Start", [Scripted.Always], [set], oneShot: true),
                Scripted.Script("Expired", [Scripted.When(Scripted.If("TIMER_EXPIRED", Scripted.Counter("t")))], [Scripted.Do("SET_COUNTER", Scripted.Counter("done"), Scripted.Int(1))], oneShot: true),
            ]);
            quantities.Exact(label + ": set to expired", UntilCounter(session, "done", 1, 20));
        }
        return quantities.Done();
    }

    private static ImmutableArray<RateQuantity> MeasureScriptCounter(RateScene scene)
    {
        var quantities = new RateQuantities(scene);
        // A script without a delay raises a counter every time it is evaluated; a second script reads it against a literal, as shipped maps do.
        var session = StartMission(scene, [
            Scripted.Script("Count", [Scripted.Always], [Scripted.Do("INCREMENT_COUNTER", Scripted.Int(1), Scripted.Counter("c"))]),
            Scripted.Script("Reached", [Scripted.When(Scripted.If("COUNTER", Scripted.Counter("c"), new MapScriptParameter(MapScriptParameterType.Comparison, 3, 0m, string.Empty, 0m, 0m, 0m), Scripted.Int(150)))],
                [Scripted.Do("SET_COUNTER", Scripted.Counter("done"), Scripted.Int(1))], oneShot: true),
        ]);
        quantities.Exact("counter raised per evaluation reaches 150", UntilCounter(session, "done", 1, 20));
        var steps = session.World.Time.WholeSecondsToTicks(10) - session.World.Tick;
        for (long step = 0; step < steps; step++) session.Step();
        quantities.Count("counter raised per evaluation after 10 s", session.Engine.CounterValue("c"));
        return quantities.Done();
    }

    private static ImmutableArray<RateQuantity> MeasureScriptDelay(RateScene scene)
    {
        var quantities = new RateQuantities(scene);
        var session = StartMission(scene, [Scripted.Script("Delayed", [Scripted.Always], [Scripted.Do("INCREMENT_COUNTER", Scripted.Int(1), Scripted.Counter("d"))], pauseSeconds: 2)]);
        quantities.Exact("script with a delay of 2 s: first evaluation", UntilCounter(session, "d", 1, 20));
        var first = session.World.Tick;
        var second = UntilCounter(session, "d", 2, 20);
        quantities.Exact("script with a delay of 2 s: first to second evaluation", second < 0 ? -1 : session.World.Tick - first);
        var steps = session.World.Time.WholeSecondsToTicks(30) - session.World.Tick;
        for (long step = 0; step < steps; step++) session.Step();
        quantities.Count("script with a delay of 2 s: evaluations in 30 s", session.Engine.CounterValue("d"));
        return quantities.Done();
    }

    private static ImmutableArray<RateQuantity> MeasureScriptSequence(RateScene scene)
    {
        var quantities = new RateQuantities(scene);
        // One instruction at a time: an idle wait in frames, a walk of 300 units, another wait, a second walk.
        var session = StartMission(scene, [
            Scripted.Script("Start", [Scripted.Always], [Scripted.Do("TEAM_EXECUTE_SEQUENTIAL_SCRIPT", Scripted.Team(ScriptTeam), Scripted.Script("Steps"))], oneShot: true),
            Scripted.Script("Steps", [], [
                Scripted.Do("TEAM_IDLE_FOR_FRAMECOUNT", Scripted.Team(ScriptTeam), Scripted.Int(90)),
                Scripted.Do("SET_COUNTER", Scripted.Counter("waited"), Scripted.Int(1)),
                Scripted.Do("MOVE_TEAM_TO", Scripted.Team(ScriptTeam), Scripted.Waypoint("Mid")),
                Scripted.Do("SET_COUNTER", Scripted.Counter("walked"), Scripted.Int(1)),
                Scripted.Do("TEAM_IDLE_FOR_FRAMECOUNT", Scripted.Team(ScriptTeam), Scripted.Int(45)),
                Scripted.Do("MOVE_TEAM_TO", Scripted.Team(ScriptTeam), Scripted.Waypoint("Far")),
                Scripted.Do("SET_COUNTER", Scripted.Counter("finished"), Scripted.Int(1))], active: false),
        ]);
        var waited = UntilCounter(session, "waited", 1, 30);
        quantities.Exact("idle wait of 90 frames", waited);
        var walkStart = session.World.Tick;
        var walked = UntilCounter(session, "walked", 1, 120);
        quantities.Event("walk of 300 units", walked < 0 ? -1 : session.World.Tick - walkStart);
        var restStart = session.World.Tick;
        var finished = UntilCounter(session, "finished", 1, 120);
        quantities.Event("wait of 45 frames and second walk", finished < 0 ? -1 : session.World.Tick - restStart);
        return quantities.Done();
    }

    private static ImmutableArray<RateQuantity> MeasureScriptEndGame(RateScene scene)
    {
        var quantities = new RateQuantities(scene);
        foreach (var action in new[] { "VICTORY", "DEFEAT", "QUICKVICTORY" })
        {
            var session = StartMission(scene.Fresh(), [Scripted.Script("End", [Scripted.Always], [Scripted.Do(action)], oneShot: true)]);
            long decided = -1, ended = -1;
            var budget = session.World.Time.WholeSecondsToTicks(20);
            for (long step = 1; step <= budget && ended < 0; step++)
            {
                session.Step();
                if (decided < 0 && session.Engine.Outcome != MissionOutcome.None) decided = step;
                if (session.Engine.IsEnded) ended = step;
            }
            quantities.Exact($"{action}: outcome to end of the mission", decided >= 0 && ended >= 0 ? ended - decided : -1);
        }
        return quantities.Done();
    }

    // ------------------------------------------------------------------------------------------------------------------------------------------------ AI

    private const int AiMinutes = 8;

    private static RateCaseGroup AiCases(RateCatalogs catalogs)
    {
        var builder = new GroupBuilder(catalogs, Ai);
        foreach (var (first, second) in new[] { ("FactionAmerica", "FactionChina"), ("FactionGLA", "FactionAmerica"), ("FactionChina", "FactionGLA") })
            builder.Add($"skirmish {first} against {second}", scene => MeasureSkirmishAi(scene, first, second), "SupplyWarehouse");
        builder.Note("two computer players on a flat map of 3,000 by 3,000 units with one supply warehouse each, eight minutes of game time; a whole match is chaotic, so only early decisions are compared case by case (step 4 of the phase compares matches as distributions)");
        builder.NotCovered("ai: the outcome and the length of matches, the armies and the economy later in the match, more than two players, shipped maps, the campaign's script AI");
        return builder.Done();
    }

    /// <summary>
    /// The command stream of one skirmish of the group <c>ai</c> at <paramref name="rate"/>, for a comparison between rates: every command of the two computer players with
    /// its game time in 1/120 s and a text that does not depend on the rate (the command without its tick). A diagnosis, not a measurement: <c>SimTool rate-compare
    /// --ai-diff</c> prints where two rates part.
    /// </summary>
    public static IReadOnlyList<(long Units, string Text)> SkirmishAiCommandStream(RateCatalogs catalogs, SimulationRate rate, string firstFaction, string secondFaction)
    {
        var scene = new RateScene(catalogs, rate);
        var session = StartSkirmishAi(scene, firstFaction, secondFaction);
        while (!session.IsOver) session.Step();
        var stream = new List<(long Units, string Text)>();
        foreach (var command in session.ToReplay().Commands)
        {
            if (command.Player.IsSystem) continue;
            var text = command.ToString() ?? string.Empty;
            var tick = $"Tick = {command.Tick.ToString(CultureInfo.InvariantCulture)}, ";
            stream.Add((scene.Units(command.Tick), text.Replace(tick, string.Empty, StringComparison.Ordinal)));
        }
        return stream;
    }

    /// <summary>
    /// The first logic frame of the original after which the worlds of one skirmish of the group <c>ai</c> differ between two rates, with the objects that differ (position,
    /// health unless <paramref name="positionsOnly"/>, orders): both matches are stepped frame by frame and compared at the end of every frame. A diagnosis for <c>SimTool rate-compare --ai-diff --state</c>;
    /// <paramref name="report"/> receives the lines. Returns the frame, or -1 when the worlds stayed equal for <paramref name="frames"/> frames.
    /// </summary>
    public static long SkirmishAiFirstStateDifference(RateCatalogs catalogs, SimulationRate rateA, SimulationRate rateB, string firstFaction, string secondFaction, long frames, int shown, bool positionsOnly, Action<string> report, long watch = 0, long watchFrom = long.MaxValue)
    {
        var a = StartSkirmishAi(new RateScene(catalogs, rateA), firstFaction, secondFaction);
        var b = StartSkirmishAi(new RateScene(catalogs, rateB), firstFaction, secondFaction);
        for (long frame = 0; frame < frames && !a.IsOver && !b.IsOver; frame++)
        {
            for (var tick = 0; tick < rateA.TicksPerOriginalFrame; tick++) a.Step();
            for (var tick = 0; tick < rateB.TicksPerOriginalFrame; tick++) b.Step();
            if (frame >= watchFrom)
                foreach (var (session, rate) in new[] { (a, rateA), (b, rateB) })
                    if (session.World.FindEntity(new Ids.EntityId(checked((uint)watch))) is { } seen)
                        report($"watch frame {frame.ToString(CultureInfo.InvariantCulture)} {rate.TicksPerSecond} Hz: ({seen.X}, {seen.Y}) angle {seen.Angle} speed {seen.Speed.Raw.ToString(CultureInfo.InvariantCulture)} raw per tick, per frame {(seen.Modules.OfType<Modules.BuiltIn.AIUpdateInterface>().FirstOrDefault()?.SpeedPerOriginalFrame(rate.TicksPerOriginalFrame) ?? Numerics.Fixed.Zero).Raw.ToString(CultureInfo.InvariantCulture)} moving {seen.HasMoveTarget} to ({seen.MoveTargetX}, {seen.MoveTargetY}) ahead ({seen.AheadX}, {seen.AheadY})");
            var different = 0;
            var others = new Dictionary<uint, World.Entity>();
            foreach (var entity in b.World.Entities) others[entity.Id.Value] = entity;
            foreach (var entity in a.World.Entities)
            {
                if (!others.Remove(entity.Id.Value, out var other))
                {
                    if (different++ < shown) report($"frame {frame.ToString(CultureInfo.InvariantCulture)}: {entity.Id.Value} {entity.Template.Name} exists only at {rateA.TicksPerSecond} Hz");
                    continue;
                }
                var healthA = entity.Body?.Health ?? Numerics.Fixed.Zero;
                var healthB = other.Body?.Health ?? Numerics.Fixed.Zero;
                if (entity.X == other.X && entity.Y == other.Y && entity.Z == other.Z && (positionsOnly || healthA == healthB) && entity.HasMoveTarget == other.HasMoveTarget && entity.ContainedBy == other.ContainedBy) continue;
                if (different++ < shown)
                    report($"frame {frame.ToString(CultureInfo.InvariantCulture)}: {entity.Id.Value} {entity.Template.Name} owner {entity.Owner.Value}: "
                        + $"{rateA.TicksPerSecond} Hz ({entity.X}, {entity.Y}, {entity.Z}) health {healthA} moving {entity.HasMoveTarget} to ({entity.MoveTargetX}, {entity.MoveTargetY}) in {entity.ContainedBy.Value} spawned {entity.SpawnTick} angle {entity.Angle} speed/frame {(entity.Modules.OfType<Modules.BuiltIn.AIUpdateInterface>().FirstOrDefault()?.SpeedPerOriginalFrame(rateA.TicksPerOriginalFrame) ?? Numerics.Fixed.Zero)} | "
                        + $"{rateB.TicksPerSecond} Hz ({other.X}, {other.Y}, {other.Z}) health {healthB} moving {other.HasMoveTarget} to ({other.MoveTargetX}, {other.MoveTargetY}) in {other.ContainedBy.Value} spawned {other.SpawnTick} angle {other.Angle} speed/frame {(other.Modules.OfType<Modules.BuiltIn.AIUpdateInterface>().FirstOrDefault()?.SpeedPerOriginalFrame(rateB.TicksPerOriginalFrame) ?? Numerics.Fixed.Zero)} ahead ({other.AheadX}, {other.AheadY})");
            }
            foreach (var entity in others.Values)
                if (different++ < shown) report($"frame {frame.ToString(CultureInfo.InvariantCulture)}: {entity.Id.Value} {entity.Template.Name} exists only at {rateB.TicksPerSecond} Hz");
            if (different > 0)
            {
                report($"frame {frame.ToString(CultureInfo.InvariantCulture)}: {different.ToString(CultureInfo.InvariantCulture)} objects differ");
                return frame;
            }
        }
        return -1;
    }

    private static SkirmishMatchSession StartSkirmishAi(RateScene scene, string firstFaction, string secondFaction)
    {
        scene.MapCells = 300;
        scene.Waypoints = [("Player_1_Start", 600, 1500), ("Player_2_Start", 2400, 1500)];
        var flat = scene.Map.Map;
        var map = flat with
        {
            Objects = [..flat.Objects, new MapObject("SupplyWarehouse", 600m, 1900m, 0m, 0m, 0, MapProperties.Empty), new MapObject("SupplyWarehouse", 2400m, 1100m, 0m, 0m, 0, MapProperties.Empty)],
        };
        var terrain = TerrainMap.Build(map, "rate-equivalence-skirmish");
        var everyone = ReferenceMatchScenario.Participants(scene.Catalog, out _);
        MatchParticipant Of(string faction) => everyone.FirstOrDefault(value => value.Faction.Name == faction) ?? throw new InvalidOperationException($"Unknown or unplayable faction {faction}.");
        var budget = scene.Time.WholeSecondsToTicks(AiMinutes * 60);
        var options = SkirmishMatchOptions.Standard with { MaxTicks = budget };
        return SkirmishMatchSession.Start(options, scene.Catalog, scene.Fingerprint, terrain, map.Objects, [new SkirmishSlot(Of(firstFaction)), new SkirmishSlot(Of(secondFaction))]);
    }

    private static ImmutableArray<RateQuantity> MeasureSkirmishAi(RateScene scene, string firstFaction, string secondFaction)
    {
        var quantities = new RateQuantities(scene);
        var session = StartSkirmishAi(scene, firstFaction, secondFaction);
        var barracks = new long[] { -1, -1 };
        var firstStructure = new long[] { -1, -1 };
        while (!session.IsOver)
        {
            var result = session.Step();
            foreach (var item in result.Events)
            {
                if (item.Kind != SimEventKind.ConstructionCompleted || session.World.FindEntity(item.Entity) is not { } structure) continue;
                var index = structure.Owner.Value - 1;
                if (index is < 0 or > 1) continue;
                if (firstStructure[index] < 0) firstStructure[index] = item.Tick;
                if (barracks[index] < 0 && structure.Template.IsKindOf("FS_BARRACKS")) barracks[index] = item.Tick;
            }
        }
        var commands = session.ToReplay().Commands.Where(command => !command.Player.IsSystem).ToArray();
        var minute = scene.Time.WholeSecondsToTicks(60);
        for (var index = 0; index < 2; index++)
        {
            var player = index + 1;
            var label = index == 0 ? firstFaction : secondFaction;
            var own = commands.Where(command => command.Player.Value == player).ToArray();
            var firstAttack = own.Where(command => command is AttackMoveCommand or PursueCommand).Select(command => command.Tick).DefaultIfEmpty(-1).Min();
            quantities.Exact($"{label}: first command", own.Length > 0 ? own.Min(command => command.Tick) : -1);
            quantities.Exact($"{label}: first construction order", own.OfType<ConstructCommand>().Select(command => command.Tick).DefaultIfEmpty(-1).Min());
            quantities.Event($"{label}: first structure completed", firstStructure[index]);
            quantities.Event($"{label}: first barracks completed", barracks[index]);
            quantities.Event($"{label}: first attack order", firstAttack);
            quantities.Count($"{label}: commands in the first minute", own.Count(command => command.Tick < minute));
            quantities.Count($"{label}: commands in the first three minutes", own.Count(command => command.Tick < 3 * minute));
            quantities.Count($"{label}: commands in {AiMinutes} minutes", own.Length);
            quantities.Count($"{label}: construction orders in {AiMinutes} minutes", own.OfType<ConstructCommand>().Count());
            quantities.Count($"{label}: production orders in {AiMinutes} minutes", own.OfType<QueueProductionCommand>().Count());
            quantities.Amount($"{label}: money after {AiMinutes} minutes", session.World.MoneyOf(new Ids.PlayerId(player)));
        }
        return quantities.Done();
    }
}
