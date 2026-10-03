using CnCNew.Simulation.Numerics;
using CnCNew.Data.World;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Testing;

/// <summary>Phase 13: the commands-only mode of the objective driver (no direct world writes; orders and productions only).</summary>
static partial class Tests
{
    public static void ScriptCommandsOnlyDriverUsesOrdersNotWrites()
    {
        // A kill objective: the normal driver kills the target; the commands-only driver may only send the viewer's armed units, and the fixture's units are armed and mobile.
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Win", [Scr.And(Scr.If("NAMED_DESTROYED", Scr.Unit("Target")))], [Scr.Do("VICTORY")], oneShot: true),
        ], objects: [Placed("AlphaHero", 100m, 100m, "teamA", "Hero"), Placed("AlphaSoldier", 120m, 100m, "teamA", "Mate"), Placed("AlphaSoldier", 140m, 100m, "teamA", "Mate2"), Placed("AlphaSoldier", 160m, 100m, "teamA", "Mate3"), Placed("BetaSoldier", 520m, 380m, "teamB", "Target")]);
        var plain = MissionObjectiveDriver.Run(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map, true, maxTicks: 6000);
        Check.That(plain.ReachedByScript && plain.WorldWrites >= 1, $"The normal driver wins by a world write (writes {plain.WorldWrites}, log: {string.Join("; ", plain.Log)}).");
        var orders = MissionObjectiveDriver.Run(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map, true, maxTicks: 6000, commandsOnly: true);
        Check.That(orders.WorldWrites == 0 && orders.Commands > 0, $"The commands-only run wrote nothing and ordered units (writes {orders.WorldWrites}, commands {orders.Commands}).");
        Check.That(orders.ReachedByScript, $"The army walks to the target and the combat loop destroys it ({orders.Outcome}, log: {string.Join("; ", orders.Log)}, unmet: {string.Join("; ", orders.Unmet)}).");
        var again = MissionObjectiveDriver.Run(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map, true, maxTicks: 6000, commandsOnly: true);
        Check.That(again.OutcomeTick == orders.OutcomeTick && again.Commands == orders.Commands && again.Log.SequenceEqual(orders.Log), "A second commands-only run is identical.");
    }

    public static void ScriptCommandsOnlyDriverDeclinesWhatOnlyAWriteCanDo()
    {
        // Money cannot be ordered: the commands-only run declines the write, reports it and does not reach the ending.
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Rich", [Scr.And(Scr.If("PLAYER_HAS_CREDITS", Scr.Int(99999), Scr.Compare(Scr.LessEqual), Scr.Side(PlayerA)))], [Scr.Do("VICTORY")], oneShot: true),
        ], objects: [Placed("AlphaHero", 100m, 100m, "teamA", "Hero")]);
        var result = MissionObjectiveDriver.Run(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map, true, maxTicks: 900, commandsOnly: true);
        Check.That(!result.ReachedByScript && result.WorldWrites == 0 && result.Declined.Any(line => line.Contains("credits", StringComparison.Ordinal)),
            $"The credits write is declined and named (reached {result.ReachedByScript}, writes {result.WorldWrites}, declined: {string.Join("; ", result.Declined)}).");
        Check.That(result.Unmet.Any(line => line.StartsWith("declined world write", StringComparison.Ordinal)), "The refusal shows up in the unmet list.");
    }

    public static void ScriptPhase13SettingActions()
    {
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("All", [Scr.And(Scr.If("CONDITION_TRUE"))], [
                Scr.Do("TEAM_FACE_NAMED", Scr.Team("teamA"), Scr.Unit("Beacon")),
                Scr.Do("PLAYER_SELL_EVERYTHING", Scr.Side(PlayerA)),
                Scr.Do("NAMED_SET_STOPPING_DISTANCE", Scr.Unit("Walker"), Scr.Real(12m)),
                Scr.Do("NAMED_SET_REPULSOR", Scr.Unit("Walker"), Scr.Int(1)),
                Scr.Do("SET_BASE_CONSTRUCTION_SPEED", Scr.Side(PlayerA), Scr.Int(30))], oneShot: true),
        ], objects: [Placed("AlphaSoldier", 100m, 200m, "teamA", "Walker"), Placed("AlphaBunker", 300m, 200m, "teamA", "Bunker"), Placed("BetaSoldier", 100m, 400m, "teamB", "Beacon")]);
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(session, 3);
        var walker = NamedIn(session, "Walker")!;
        Check.That((walker.Angle - Fixed.FromDecimal(1.5707963m)).Abs() < Fixed.FromDecimal(0.05m), $"TEAM_FACE_NAMED turns the team toward the object (heading {walker.Angle}).");
        Check.That(NamedIn(session, "Bunker") is { } bunker && bunker.HasStatus(CnCNew.Simulation.Modules.ObjectStatusBits.Sold), "PLAYER_SELL_EVERYTHING puts the player's structures up for sale.");
        Check.That(session.Engine.RecordedSetting("NAMED_SET_STOPPING_DISTANCE", walker.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)) == Fixed.FromInt(12).Raw
                   && session.Engine.RecordedSetting("NAMED_SET_REPULSOR", walker.Id.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)) == 1
                   && session.Engine.RecordedSetting("SET_BASE_CONSTRUCTION_SPEED", walker.Owner.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)) == 30, "The three consumer-less settings are recorded.");
        Check.That(session.Engine.RecordedActions >= 3 && session.Engine.MissingActionHits.Count == 0, "Recorded actions count apart; none is missing: " + string.Join(", ", session.Engine.MissingActionHits.Keys));
        var restored = MissionSnapshot.Restore(MissionSnapshot.Write(session), fixture.Templates, fixture.Fingerprint, terrain, map);
        Check.That(restored.StateHash() == session.StateHash() && restored.Engine.RecordedSetting("SET_BASE_CONSTRUCTION_SPEED", walker.Owner.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)) == 30, "A restored mission keeps the recorded settings.");
    }
}
