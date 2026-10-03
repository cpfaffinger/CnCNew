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
            Scr.Script("Captured", [Scr.And(Scr.If("SKIRMISH_PLAYER_HAS_COMPARISON_CAPTURED_UNITS", Scr.Side(PlayerA), Scr.Compare(Scr.GreaterEqual), Scr.Int(1)))], [Scr.Do("SET_FLAG", Scr.Flag("captured"), Scr.Bool(true))], oneShot: true),
            Scr.Script("Points", [Scr.And(Scr.If("PLAYER_HAS_SCIENCEPURCHASEPOINTS", Scr.Side(PlayerA), Scr.Int(0)), Scr.If("OBSOLETE_SCRIPT_1"))], [Scr.Do("SET_FLAG", Scr.Flag("never"), Scr.Bool(true))], oneShot: true),
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
        Check.That(session.Engine.FlagValue("captured") && !session.Engine.FlagValue("never"), "The captured vehicle is counted; obsolete conditions never hold.");
        var again = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(again, 600);
        Check.That(again.StateHash() == session.StateHash(), "A second run reaches the same state.");
    }

    public static void ScriptPhase13Conditions()
    {
        MapScript Flag(string name, MapCondition condition) => Scr.Script(name, [Scr.And(condition)], [Scr.Do("SET_FLAG", Scr.Flag(name), Scr.Bool(true))], oneShot: true);
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Setup", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("TEAM_SET_STATE", Scr.Team("Squad"), Scr.Named(MapScriptParameterType.TeamState, "IDLE")), Scr.Do("NAMED_SET_UNMANNED_STATUS", Scr.Unit("Empty"))], oneShot: true),
            Flag("state", Scr.If("TEAM_STATE_IS", Scr.Team("Squad"), Scr.Named(MapScriptParameterType.TeamState, "IDLE"))),
            Flag("notState", Scr.If("TEAM_STATE_IS_NOT", Scr.Team("Squad"), Scr.Named(MapScriptParameterType.TeamState, "BUSY"))),
            Flag("slots", Scr.If("NAMED_HAS_FREE_CONTAINER_SLOTS", Scr.Unit("Bunker"))),
            Flag("excess", Scr.If("PLAYER_EXCESS_POWER_COMPARE_VALUE", Scr.Side(PlayerA), Scr.Compare(Scr.GreaterEqual), Scr.Int(0))),
            Flag("percent", Scr.If("PLAYER_POWER_COMPARE_PERCENT", Scr.Side(PlayerA), Scr.Compare(Scr.Less), Scr.Int(100))),
            Flag("unowned", Scr.If("SKIRMISH_UNOWNED_FACTION_UNIT_EXISTS", Scr.Side(PlayerA), Scr.Compare(Scr.GreaterEqual), Scr.Int(1))),
            Flag("tech", Scr.If("SKIRMISH_TECH_BUILDING_WITHIN_DISTANCE", Scr.Side(PlayerA), Scr.Int(500), Scr.Area("LeftArea"))),
            Flag("safe", Scr.If("SUPPLY_SOURCE_SAFE", Scr.Side(PlayerA), Scr.Int(100))),
            Flag("power", Scr.If("PLAYER_COMPLETED_SPECIAL_POWER", Scr.Side(PlayerA), Scr.Named(MapScriptParameterType.SpecialPower, "SuperweaponTest"))),
            Flag("upgrade", Scr.If("PLAYER_BUILT_UPGRADE", Scr.Side(PlayerA), Scr.Named(MapScriptParameterType.Upgrade, "Upgrade_Test"))),
            Flag("midway", Scr.If("PLAYER_MIDWAY_SPECIAL_POWER", Scr.Side(PlayerA), Scr.Named(MapScriptParameterType.SpecialPower, "SuperweaponTest"))),
        ], objects: [Placed("AlphaSoldier", 100m, 100m, "Squad", "Scout"), Placed("AlphaBunker", 150m, 100m, "teamA", "Bunker"), Placed("BetaSoldier", 140m, 300m, "teamB", "Empty"), Placed("AlphaTech", 120m, 150m, "teamB", "Tech")]);
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(session, 5);
        foreach (var name in new[] { "state", "notState", "slots", "excess", "percent", "unowned", "tech" })
            Check.That(session.Engine.FlagValue(name), $"Condition '{name}' holds.");
        Check.That(!session.Engine.FlagValue("power") && !session.Engine.FlagValue("upgrade") && !session.Engine.FlagValue("midway"), "Notification conditions are false before the notifications.");
        var player = session.Program.FindPlayer(PlayerA)!.Id;
        session.Engine.Observe([
            new CnCNew.Simulation.Events.SimEvent(new CnCNew.Simulation.Ids.EventId(900), session.World.Tick, CnCNew.Simulation.Events.SimEventKind.SpecialPowerCompleted, new CnCNew.Simulation.Ids.EntityId(1), new CnCNew.Simulation.Ids.EntityId(2), default, "SuperweaponTest", player.Value),
            new CnCNew.Simulation.Events.SimEvent(new CnCNew.Simulation.Ids.EventId(901), session.World.Tick, CnCNew.Simulation.Events.SimEventKind.UpgradeCompleted, new CnCNew.Simulation.Ids.EntityId(3), default, player, "Upgrade_Test", 0)]);
        Run(session, 3);
        Check.That(session.Engine.FlagValue("power") && session.Engine.FlagValue("upgrade") && !session.Engine.FlagValue("midway"), "The completed power and the built upgrade are seen once; the midway condition never fires.");
        var restored = MissionSnapshot.Restore(MissionSnapshot.Write(session), fixture.Templates, fixture.Fingerprint, terrain, map);
        Check.That(restored.StateHash() == session.StateHash(), "A restored mission matches (team states, caches and notification lists survive).");
    }

    public static void ScriptPhase13MoreActions()
    {
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("All", [Scr.And(Scr.If("CONDITION_TRUE"))], [
                Scr.Do("NAMED_FACE_NAMED", Scr.Unit("Walker"), Scr.Unit("Beacon")),
                Scr.Do("NAMED_SET_ATTITUDE", Scr.Unit("Walker"), Scr.Int(2)),
                Scr.Do("TEAM_SET_REPULSOR", Scr.Team("teamA"), Scr.Int(1)),
                Scr.Do("SET_STOPPING_DISTANCE", Scr.Team("teamA"), Scr.Real(5m)),
                Scr.Do("CHOOSE_VICTIM_ALWAYS_USES_NORMAL", Scr.Int(1)),
                Scr.Do("PLAYER_ADD_SKILLPOINTS", Scr.Side(PlayerA), Scr.Int(3)),
                Scr.Do("PLAYER_ADD_SKILLPOINTS", Scr.Side(PlayerA), Scr.Int(4)),
                Scr.Do("UNIT_DESTROY_ALL_CONTAINED", Scr.Unit("Bunker")),
                Scr.Do("PLAYER_CREATE_TEAM_FROM_CAPTURED_UNITS", Scr.Side(PlayerA), Scr.Team("teamA")),
                Scr.Do("DEBUG_CRASH_BOX", Scr.Named(MapScriptParameterType.TextString, "x"))], oneShot: true),
        ], objects: [Placed("AlphaSoldier", 100m, 200m, "teamA", "Walker"), Placed("AlphaBunker", 300m, 200m, "teamA", "Bunker"), Placed("BetaSoldier", 100m, 400m, "teamB", "Beacon")]);
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(session, 3);
        var walker = NamedIn(session, "Walker")!;
        Check.That((walker.Angle - Fixed.FromDecimal(1.5707963m)).Abs() < Fixed.FromDecimal(0.05m), $"NAMED_FACE_NAMED turns the unit towards the object ({walker.Angle}).");
        var owner = walker.Owner.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        Check.That(session.Engine.RecordedSetting("NAMED_SET_ATTITUDE", walker.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)) == 2
                   && session.Engine.RecordedSetting("CHOOSE_VICTIM_ALWAYS_USES_NORMAL", string.Empty) == 1 && session.Engine.RecordedSetting("PLAYER_ADD_SKILLPOINTS", owner) == 7, "The consumer-less settings are recorded (skill points add up).");
        Check.That(session.Engine.MissingActionHits.Count == 0, "No missing actions: " + string.Join(", ", session.Engine.MissingActionHits.Keys));
    }

    public static void ScriptTeamFollowsAPathAsAFormation()
    {
        var (map, fixture, terrain) = MissionMap([Scr.Script("Go", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("TEAM_FOLLOW_WAYPOINTS", Scr.Team("teamA"), Scr.Named(MapScriptParameterType.WaypointPath, "Route"), Scr.Bool(true))], oneShot: true)],
            objects: [Placed("AlphaSoldier", 100m, 100m, "teamA", "Lead"), Placed("AlphaSoldier", 100m, 140m, "teamA", "Wing")],
            paths: [("Route", [("A", 200m, 100m), ("B", 300m, 100m), ("C", 400m, 100m)])]);
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        var lead = NamedIn(session, "Lead")!;
        var wing = NamedIn(session, "Wing")!;
        var minGap = Fixed.MaxValue;
        var maxGap = Fixed.Zero;
        for (var tick = 0; tick < 700; tick++)
        {
            session.Step();
            if (lead.X > Fixed.FromInt(150) && lead.X < Fixed.FromInt(380))
            {
                var gap = (wing.Y - lead.Y).Abs();
                minGap = Fixed.Min(minGap, gap);
                maxGap = Fixed.Max(maxGap, gap);
            }
        }
        Check.That(lead.X > Fixed.FromInt(370) && wing.X > Fixed.FromInt(370), $"Both reach the end of the path (lead {lead.X}, wing {wing.X}).");
        Check.That(minGap > Fixed.FromInt(25) && maxGap < Fixed.FromInt(55), $"The wing keeps its place beside the lead while they march (gap {minGap}..{maxGap}).");
        var restored = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        for (var tick = 0; tick < 120; tick++) restored.Step();
        var snapshot = MissionSnapshot.Write(restored);
        var continued = MissionSnapshot.Restore(snapshot, fixture.Templates, fixture.Fingerprint, terrain, map);
        for (var tick = 0; tick < 200; tick++) Check.That(restored.Step().StateHash == continued.Step().StateHash, $"A formation march restored from a snapshot continues identically (tick {tick}).");
    }
}
