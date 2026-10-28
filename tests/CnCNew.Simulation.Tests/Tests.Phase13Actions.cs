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
        Run(session, session.Frames(3));
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
        Run(session, session.Frames(60));
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
        Run(session, session.Frames(600));
        var hiker = NamedIn(session, "Hiker")!;
        Check.That(hiker.X > Fixed.FromInt(100) && session.Engine.MissingActionHits.Count == 0, $"UNIT_MOVE_TOWARDS_NEAREST_OBJECT_TYPE and the approach paths move units (hiker at {hiker.X},{hiker.Y}; missing {string.Join(", ", session.Engine.MissingActionHits.Keys)}).");
        var empty = NamedIn(session, "Empty")!;
        var taker = NamedIn(session, "Taker")!;
        Check.That(empty.Owner == taker.Owner, $"The capturing team takes over the unmanned object (owner {empty.Owner}, taker {taker.Owner}).");
        Check.That(session.Engine.FlagValue("captured") && !session.Engine.FlagValue("never"), "The captured vehicle is counted; obsolete conditions never hold.");
        var again = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(again, again.Frames(600));
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
        Run(session, session.Frames(5));
        foreach (var name in new[] { "state", "notState", "slots", "excess", "percent", "unowned", "tech" })
            Check.That(session.Engine.FlagValue(name), $"Condition '{name}' holds.");
        Check.That(!session.Engine.FlagValue("power") && !session.Engine.FlagValue("upgrade") && !session.Engine.FlagValue("midway"), "Notification conditions are false before the notifications.");
        var player = session.Program.FindPlayer(PlayerA)!.Id;
        session.Engine.Observe([
            new CnCNew.Simulation.Events.SimEvent(new CnCNew.Simulation.Ids.EventId(900), session.World.Tick, CnCNew.Simulation.Events.SimEventKind.SpecialPowerCompleted, new CnCNew.Simulation.Ids.EntityId(1), new CnCNew.Simulation.Ids.EntityId(2), default, "SuperweaponTest", player.Value),
            new CnCNew.Simulation.Events.SimEvent(new CnCNew.Simulation.Ids.EventId(901), session.World.Tick, CnCNew.Simulation.Events.SimEventKind.UpgradeCompleted, new CnCNew.Simulation.Ids.EntityId(3), default, player, "Upgrade_Test", 0)]);
        Run(session, session.Frames(3));
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
        Run(session, session.Frames(3));
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
        for (var tick = 0; tick < session.Frames(700); tick++)
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
        for (var tick = 0; tick < restored.Frames(120); tick++) restored.Step();
        var snapshot = MissionSnapshot.Write(restored);
        var continued = MissionSnapshot.Restore(snapshot, fixture.Templates, fixture.Fingerprint, terrain, map);
        for (var tick = 0; tick < restored.Frames(200); tick++) Check.That(restored.Step().StateHash == continued.Step().StateHash, $"A formation march restored from a snapshot continues identically (tick {tick}).");
    }

    public static void ScriptAiExpandsToASupplyWarehouse()
    {
        // AI_PLAYER_BUILD_SUPPLY_CENTER: a dozer of the player builds a depot next to a warehouse that holds enough supplies; TEAM_GUARD_SUPPLY_CENTER walks a team to it.
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Fund", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("PLAYER_SET_MONEY", Scr.Side(PlayerA), Scr.Int(500))], oneShot: true),
            Scr.Script("Expand", [Scr.And(Scr.If("SUPPLY_SOURCE_SAFE", Scr.Side(PlayerA), Scr.Int(0)), Scr.If("FLAG", Scr.Flag("go"), Scr.Bool(true)))], [Scr.Do("AI_PLAYER_BUILD_SUPPLY_CENTER", Scr.Side(PlayerA), Scr.Type("AlphaDepot"), Scr.Int(0)), Scr.Do("TEAM_GUARD_SUPPLY_CENTER", Scr.Team("Squad"), Scr.Int(0)), Scr.Do("RESUME_SUPPLY_TRUCKING")], oneShot: true),
        ], objects: [Placed("AlphaDozer", 100m, 100m, "teamA", "Dozer"), Placed("ScriptWarehouse", 170m, 100m, "teamPlayerA", "Warehouse"), Placed("AlphaSoldier", 100m, 140m, "Squad", "Guard")]);
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Check.That(session.SkippedObjects.IsEmpty, "The fixture instantiates: " + string.Join(",", session.SkippedObjects));
        Run(session, session.Frames(5));
        session.Engine.SignalUiInteraction("go");
        Run(session, session.Frames(900));
        var depot = session.World.Entities.FirstOrDefault(entity => entity.Template.Name == "AlphaDepot");
        Check.That(depot is not null && depot.Owner == session.Program.FindPlayer(PlayerA)!.Id, $"The dozer builds the depot (diagnostics: {string.Join("; ", session.Engine.Diagnostics.Keys)}; money {session.World.MoneyOf(session.Program.FindPlayer(PlayerA)!.Id)}; objects {string.Join(",", session.World.Entities.Select(entity => entity.Template.Name))}).");
        Check.That(depot is not null && (depot.X - Fixed.FromInt(170)).Abs() < Fixed.FromInt(60) && (depot.Y - Fixed.FromInt(100)).Abs() < Fixed.FromInt(60), $"The depot stands next to the warehouse ({depot?.X}, {depot?.Y}).");
        var guard = NamedIn(session, "Guard")!;
        Check.That(guard.X > Fixed.FromInt(140), $"The guard team walked towards the warehouse ({guard.X}).");
        var restored = MissionSnapshot.Restore(MissionSnapshot.Write(session), fixture.Templates, fixture.Fingerprint, terrain, map);
        Check.That(restored.StateHash() == session.StateHash(), "A restored mission matches.");
    }

    public static void ScriptGuardActions()
    {
        // F-58, step 4: TEAM_GUARD_POSITION guards a waypoint, NAMED_GUARD and TEAM_GUARD the unit's own position, TEAM_GUARD_OBJECT a named object - as guard states of the
        // simulation, no longer a walk or a stop; TEAM_GUARD_SUPPLY_CENTER guards the warehouse's side (ScriptAiExpandsToASupplyWarehouse).
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Guard", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("TEAM_GUARD_POSITION", Scr.Team("Squad"), Scr.Waypoint("Mid")), Scr.Do("NAMED_GUARD", Scr.Unit("Sentry")),
                Scr.Do("TEAM_GUARD_OBJECT", Scr.Team("teamPlayerA"), Scr.Unit("Sentry"))], oneShot: true),
        ], objects: [Placed("AlphaSoldier", 100m, 100m, "Squad", "Walker"), Placed("AlphaSoldier", 100m, 160m, "teamA", "Sentry"), Placed("AlphaSoldier", 140m, 160m, "teamPlayerA", "Escort1")]);
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(session, session.Frames(300));
        var walker = NamedIn(session, "Walker")!;
        var sentry = NamedIn(session, "Sentry")!;
        var escort = NamedIn(session, "Escort1")!;
        Check.That(session.World.IsGuarding(walker) && (walker.X - Fixed.FromInt(250)).Abs() < Fixed.FromInt(30) && (walker.Y - Fixed.FromInt(200)).Abs() < Fixed.FromInt(30), $"TEAM_GUARD_POSITION: the team guards the waypoint and stands there ({walker.X}, {walker.Y}, guarding {session.World.IsGuarding(walker)}; {string.Join("; ", session.Engine.Diagnostics.Keys)}).");
        Check.That(session.World.IsGuarding(sentry) && sentry.X == Fixed.FromInt(100) && sentry.Y == Fixed.FromInt(160), "NAMED_GUARD: the unit guards where it stands.");
        Check.That(session.World.IsGuarding(escort), "TEAM_GUARD_OBJECT: the team guards the named object.");
        var restored = MissionSnapshot.Restore(MissionSnapshot.Write(session), fixture.Templates, fixture.Fingerprint, terrain, map);
        Check.That(restored.StateHash() == session.StateHash() && restored.World.IsGuarding(walker.Id is var id ? restored.World.FindEntity(id)! : null!), "A restored mission keeps the guards.");
    }

    public static void ScriptDisableHoldAndEvacuate()
    {
        // NAMED_SET_HELD freezes an object, PLAYER_DISABLE_FACTORIES stops the production of one type, NAMED_SET_EVAC_LEFT_OR_RIGHT sets a garrison's exit side.
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Hold", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("NAMED_SET_HELD", Scr.Unit("Held"), Scr.Int(1)), Scr.Do("MOVE_NAMED_UNIT_TO", Scr.Unit("Held"), Scr.Waypoint("Far")), Scr.Do("MOVE_NAMED_UNIT_TO", Scr.Unit("Free"), Scr.Waypoint("Far")),
                Scr.Do("PLAYER_DISABLE_FACTORIES", Scr.Side(PlayerA), Scr.Type("AlphaBunker")), Scr.Do("NAMED_SET_EVAC_LEFT_OR_RIGHT", Scr.Unit("Bunker"), Scr.Int(1))], oneShot: true),
            Scr.Script("Release", [Scr.And(Scr.If("FLAG", Scr.Flag("release"), Scr.Bool(true)))], [Scr.Do("NAMED_SET_HELD", Scr.Unit("Held"), Scr.Int(0)), Scr.Do("PLAYER_ENABLE_FACTORIES", Scr.Side(PlayerA), Scr.Type("AlphaBunker"))], oneShot: true),
        ], objects: [Placed("AlphaSoldier", 100m, 100m, "teamA", "Held"), Placed("AlphaSoldier", 100m, 140m, "teamA", "Free"), Placed("AlphaBunker", 100m, 60m, "teamA", "Bunker"), Placed("AlphaSoldier", 100m, 180m, "teamA", "Other")],
            paths: [("Route", [("Far", 300m, 100m)])]);
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(session, session.Frames(60));
        var held = NamedIn(session, "Held")!;
        var free = NamedIn(session, "Free")!;
        var bunker = NamedIn(session, "Bunker")!;
        Check.That((held.ScriptDisableMask & CnCNew.Simulation.World.Entity.ScriptHeld) != 0 && held.X == Fixed.FromInt(100), $"A held unit stays where it is ({held.X}).");
        Check.That(free.X > Fixed.FromInt(100), "An unheld unit walks.");
        Check.That((bunker.ScriptDisableMask & CnCNew.Simulation.World.Entity.ScriptDisabled) != 0 && (NamedIn(session, "Other")!.ScriptDisableMask == 0), "PLAYER_DISABLE_FACTORIES marks the objects of the type only.");
        Check.That(bunker.Modules.OfType<CnCNew.Simulation.Modules.BuiltIn.GarrisonContain>().Single().EvacDisposition == 1, "The garrison leaves to the left.");
        var restored = MissionSnapshot.Restore(MissionSnapshot.Write(session), fixture.Templates, fixture.Fingerprint, terrain, map);
        Check.That(restored.StateHash() == session.StateHash(), "A restored mission matches (script disables and evacuation side are state).");
        session.Engine.SignalUiInteraction("release");
        Run(session, session.Frames(120));
        Check.That(held.ScriptDisableMask == 0 && bunker.ScriptDisableMask == 0, "Releasing clears both disables.");
    }

    public static void ScriptBoobyTrapsAndBridges()
    {
        // NAMED_SET_BOOBYTRAPPED puts a trap object at the victim's perimeter; BRIDGE_BROKEN is true for one update after the bridge object went to rubble.
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Trap", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("NAMED_SET_BOOBYTRAPPED", Scr.Type("ScriptTrap"), Scr.Unit("Victim")), Scr.Do("TEAM_SET_BOOBYTRAPPED", Scr.Type("ScriptTrap"), Scr.Team("Squad"))], oneShot: true),
            Scr.Script("Watch", [Scr.And(Scr.If("BRIDGE_BROKEN", Scr.Unit("Bridge")))], [Scr.Do("SET_FLAG", Scr.Flag("broken"), Scr.Bool(true))], oneShot: true),
            Scr.Script("Fixed", [Scr.And(Scr.If("BRIDGE_REPAIRED", Scr.Unit("Bridge")))], [Scr.Do("SET_FLAG", Scr.Flag("repaired"), Scr.Bool(true))], oneShot: true),
            Scr.Script("Blow", [Scr.And(Scr.If("FLAG", Scr.Flag("blow"), Scr.Bool(true)))], [Scr.Do("NAMED_KILL", Scr.Unit("Bridge"))], oneShot: true),
        ], objects: [Placed("AlphaBunker", 200m, 100m, "teamPlayerA", "Victim"), Placed("AlphaSoldier", 100m, 100m, "Squad", "Guard"), Placed("ScriptBridge", 60m, 60m, "", "Bridge")]);
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Check.That(session.SkippedObjects.IsEmpty, "The fixture instantiates: " + string.Join(",", session.SkippedObjects));
        Run(session, session.Frames(20));
        var traps = session.World.Entities.Where(entity => entity.Template.Name == "ScriptTrap").OrderBy(entity => entity.Id.Value).ToArray();
        Check.That(traps.Length == 2, $"One trap per victim ({traps.Length}).");
        var victim = NamedIn(session, "Victim")!;
        Check.That(victim.HasStatus(CnCNew.Simulation.Modules.ObjectStatusBits.Index("BOOBY_TRAPPED")) && NamedIn(session, "Guard")!.HasStatus(CnCNew.Simulation.Modules.ObjectStatusBits.Index("BOOBY_TRAPPED")), "Both victims are booby-trapped.");
        Check.That(!session.Engine.FlagValue("broken") && !session.Engine.FlagValue("repaired"), "An intact bridge is neither broken nor repaired.");
        session.Engine.SignalUiInteraction("blow");
        Run(session, session.Frames(4));
        Check.That(session.Engine.FlagValue("broken") && !session.Engine.FlagValue("repaired"), "The killed bridge reads as broken.");
        var restored = MissionSnapshot.Restore(MissionSnapshot.Write(session), fixture.Templates, fixture.Fingerprint, terrain, map);
        Check.That(restored.StateHash() == session.StateHash(), "A restored mission matches.");
    }

    public static void ScriptNameFollowsATransfer()
    {
        // ObjectNameTransferred (car bomb, hijack, pilot): the script name of the consumed object moves to the one that took over.
        var session = StartMission([], objects: [Placed("AlphaSoldier", 100m, 100m, "teamA", "Old"), Placed("AlphaSoldier", 140m, 100m, "teamA", "New")]);
        Run(session, session.Frames(2));
        var old = NamedIn(session, "Old")!;
        var successor = NamedIn(session, "New")!;
        session.World.Emit(CnCNew.Simulation.Events.SimEventKind.ObjectNameTransferred, old.Id, successor.Id, string.Empty, 0);
        Run(session, session.Frames(2));
        Check.That(NamedIn(session, "Old")?.Id == successor.Id, "The name Old now belongs to the object that took over.");
    }
}
