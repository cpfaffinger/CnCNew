using CnCNew.Data.World;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Scripting;

/// <summary>Phase 13: the script actions added after the Phase-12 VM (topple direction, buildability, approach paths, unmanned capture, supply-centre guard, local defeat).</summary>
static partial class Tests
{
    public static void ScriptPhase13TablesAndOutcome()
    {
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Set", [Scr.And(Scr.If("CONDITION_TRUE"))], [
                Scr.Do("NAMED_SET_TOPPLE_DIRECTION", Scr.Unit("Pole"), Scr.Coord(3m, 4m, 0m)),
                Scr.Do("TECHTREE_MODIFY_BUILDABILITY_OBJECT", Scr.Type("AlphaSoldier"), Scr.Int(2)),
                Scr.Do("PLAYER_SELECT_SKILLSET", Scr.Side(PlayerB), Scr.Int(2)),
                Scr.Do("RESUME_SUPPLY_TRUCKING")], oneShot: true),
            Scr.Script("Quit", [Scr.And(Scr.If("FLAG", Scr.Flag("quit"), Scr.Bool(true)))], [Scr.Do("LOCALDEFEAT")], oneShot: true),
        ], objects: [Placed("AlphaSoldier", 100m, 200m, "teamA", "Pole")]);
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(session, 3);
        var pole = NamedIn(session, "Pole")!;
        Check.That(session.World.TryGetToppleDirectionOverride(pole, out var x, out var y) && (x - Fixed.FromDecimal(0.6m)).Abs() < Fixed.FromDecimal(0.01m) && (y - Fixed.FromDecimal(0.8m)).Abs() < Fixed.FromDecimal(0.01m),
            $"The scripted topple direction is handed to the world normalised ({x}, {y}).");
        Check.That(fixture.Templates.TryGet("AlphaSoldier", out var soldier) && session.World.BuildableOf(soldier) == "No", "The buildability override reaches the world (Buildable = No).");
        Check.That(session.Engine.RecordedSetting("PLAYER_SELECT_SKILLSET", session.Program.FindPlayer(PlayerB)!.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)) == 1, "The skill set choice is recorded (1-based on the map, 0-based stored).");
        Check.That(session.Engine.MissingActionHits.Count == 0, "No missing actions: " + string.Join(", ", session.Engine.MissingActionHits.Keys));
        var restored = MissionSnapshot.Restore(MissionSnapshot.Write(session), fixture.Templates, fixture.Fingerprint, terrain, map);
        Check.That(restored.StateHash() == session.StateHash() && restored.World.TryGetToppleDirectionOverride(NamedIn(restored, "Pole")!, out _, out _) && restored.World.BuildableOf(soldier) == "No",
            "A restored mission keeps the tables and installs the hooks again.");
        session.Engine.SignalUiInteraction("quit");
        Run(session, 60);
        Check.That(session.Engine.Outcome == MissionOutcome.Defeat, $"LOCALDEFEAT ends the mission as a defeat ({session.Engine.Outcome}).");
    }

    public static void ScriptPhase13MovementActions()
    {
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Orders", [Scr.And(Scr.If("CONDITION_TRUE"))], [
                Scr.Do("UNIT_MOVE_TOWARDS_NEAREST_OBJECT_TYPE", Scr.Unit("Hiker"), Scr.Type("AlphaBunker"), Scr.Area("LeftArea")),
                Scr.Do("SKIRMISH_MOVE_TO_APPROACH_PATH", Scr.Team("Squad"), Scr.SkirmishPath("Approach")),
                Scr.Do("SKIRMISH_FOLLOW_APPROACH_PATH", Scr.Team("teamA"), Scr.SkirmishPath("Approach"), Scr.Int(1)),
                Scr.Do("NAMED_SET_UNMANNED_STATUS", Scr.Unit("Empty")),
                Scr.Do("TEAM_CAPTURE_NEAREST_UNOWNED_FACTION_UNIT", Scr.Team("teamPlayerA"))], oneShot: true),
        ], objects: [Placed("AlphaSoldier", 100m, 100m, "teamA", "Hiker"), Placed("AlphaBunker", 150m, 100m, "teamB", "Bunker"), Placed("AlphaSoldier", 100m, 300m, "Squad", "Scout"),
                     Placed("BetaSoldier", 140m, 300m, "teamB", "Empty"), Placed("AlphaSoldier", 110m, 300m, "teamPlayerA", "Taker")],
            paths: [("Approach1", [("A", 130m, 100m), ("B", 220m, 100m), ("C", 300m, 100m)])]);
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(session, 600);
        var hiker = NamedIn(session, "Hiker")!;
        Check.That(hiker.X > Fixed.FromInt(100) && session.Engine.MissingActionHits.Count == 0, $"UNIT_MOVE_TOWARDS_NEAREST_OBJECT_TYPE and the approach paths move units (hiker at {hiker.X},{hiker.Y}; missing {string.Join(", ", session.Engine.MissingActionHits.Keys)}).");
        var empty = NamedIn(session, "Empty")!;
        var taker = NamedIn(session, "Taker")!;
        Check.That(empty.Owner == taker.Owner, $"The capturing team takes over the unmanned object (owner {empty.Owner}, taker {taker.Owner}).");
        var again = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(again, 600);
        Check.That(again.StateHash() == session.StateHash(), "A second run reaches the same state.");
    }
}
