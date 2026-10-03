using CnCNew.Data.World;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.World;

/// <summary>Phase 12: the team command-button family, override relations, facing, unmanning and the recorded-only actions.</summary>
static partial class Tests
{
    public static void ScriptOverrideRelations()
    {
        // teamA looks at teamB as neutral; PlayerB looks at teamA as allies; the player relation itself is untouched; the override survives save/restore and can be removed.
        var session = StartMission([
            Scr.Script("Peace", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("TEAM_SET_OVERRIDE_RELATION_TO_TEAM", Scr.Team("teamA"), Scr.Team("teamB"), Scr.Int(1))], oneShot: true),
            Scr.Script("Truce", [Scr.And(Scr.If("FLAG", Scr.Flag("lift"), Scr.Bool(true)))], [Scr.Do("TEAM_REMOVE_OVERRIDE_RELATION_TO_TEAM", Scr.Team("teamA"), Scr.Team("teamB"))], oneShot: true),
            Scr.Script("Thaw", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("PLAYER_SET_OVERRIDE_RELATION_TO_TEAM", Scr.Side(PlayerB), Scr.Team("teamA"), Scr.Int(2))], oneShot: true),
        ], objects: [Placed("AlphaSoldier", 100m, 200m, "teamA", "Mine"), Placed("BetaSoldier", 400m, 380m, "teamB", "Theirs")]);
        var mine = NamedIn(session, "Mine")!;
        var theirs = NamedIn(session, "Theirs")!;
        Check.That(session.World.AreEnemies(mine, theirs), "Before any script ran the two soldiers are enemies.");
        Run(session, 2);
        Check.That(!session.World.AreEnemies(mine, theirs), "The team override makes the soldier of teamA see teamB as neutral.");
        Check.That(session.World.RelationshipBetween(theirs, mine) == Relationship.Allies, "A player override towards a team applies to all objects of that player.");
        Check.That(session.World.RelationshipOf(mine.Owner, theirs.Owner) == Relationship.Enemies, "The player relation itself is untouched.");
        var bytes = MissionSnapshot.Write(session);
        var restored = MissionSnapshot.Restore(bytes, session.World.Templates, session.World.Configuration.DefinitionFingerprint, session.World.Terrain!, session.Map);
        Check.That(!restored.World.AreEnemies(NamedIn(restored, "Mine")!, NamedIn(restored, "Theirs")!), "A restored mission still has the override installed.");
        session.Engine.SignalUiInteraction("lift");
        Run(session, 2);
        Check.That(session.World.AreEnemies(mine, theirs), "Removing the team override restores the player relation.");
    }

    public static void ScriptTeamCommandButtons()
    {
        // Squad shoots with Cmd_AlphaFire at the nearest of two enemies; of two heroes a 50 % partial use triggers exactly one.
        var session = StartMission([
            Scr.Script("Nearest", [Scr.And(Scr.If("FLAG", Scr.Flag("go"), Scr.Bool(true)))], [Scr.Do("TEAM_ALL_USE_COMMANDBUTTON_ON_NEAREST_ENEMY_UNIT", Scr.Team("Squad"), Scr.Ability("Cmd_AlphaFire"))], oneShot: true),
            Scr.Script("Half", [Scr.And(Scr.If("FLAG", Scr.Flag("go"), Scr.Bool(true)))], [Scr.Do("TEAM_PARTIAL_USE_COMMANDBUTTON", Scr.Real(50m), Scr.Team("teamPlayerA"), Scr.Ability("Cmd_AlphaPower"))], oneShot: true),
            Scr.Script("H1Used", [Scr.And(Scr.If("PLAYER_TRIGGERED_SPECIAL_POWER_FROM_NAMED", Scr.Side(PlayerA), Scr.Named(MapScriptParameterType.SpecialPower, "AlphaInstantPower"), Scr.Unit("H1")))], [Scr.Do("SET_FLAG", Scr.Flag("h1"), Scr.Bool(true))]),
            Scr.Script("H2Used", [Scr.And(Scr.If("PLAYER_TRIGGERED_SPECIAL_POWER_FROM_NAMED", Scr.Side(PlayerA), Scr.Named(MapScriptParameterType.SpecialPower, "AlphaInstantPower"), Scr.Unit("H2")))], [Scr.Do("SET_FLAG", Scr.Flag("h2"), Scr.Bool(true))]),
        ], objects: [Placed("AlphaSoldier", 100m, 100m, "Squad", "S1"), Placed("BetaSoldier", 140m, 100m, "teamB", "Near"), Placed("BetaSoldier", 100m, 175m, "teamB", "Far"),
                     Placed("AlphaHero", 100m, 300m, "teamPlayerA", "H1"), Placed("AlphaHero", 120m, 300m, "teamPlayerA", "H2")], options: MissionOptions.Standard with { CombatIntervalTicks = 0 });
        Run(session, 3);
        session.Engine.SignalUiInteraction("go");
        Run(session, 4);
        var near = NamedIn(session, "Near")!;
        var far = NamedIn(session, "Far")!;
        Check.That(near.Body!.Health < near.Body.MaxHealth && far.Body!.Health == far.Body.MaxHealth, "The team fired at the nearest enemy only.");
        var used = (session.Engine.FlagValue("h1") ? 1 : 0) + (session.Engine.FlagValue("h2") ? 1 : 0);
        Check.That(used == 1, $"A 50 % partial use triggered exactly one of two heroes ({used} used the power; diagnostics: {string.Join("; ", session.Engine.Diagnostics.Select(item => item.Key + "=" + item.Value))}; rejections: {string.Join(",", session.World.RejectionCounts.Select((count, index) => count > 0 ? $"{(CommandRejection)index}={count}" : ""))}).");
        Check.That(session.Engine.MissingActionHits.Count == 0, "No missing actions: " + string.Join(", ", session.Engine.MissingActionHits.Keys));
    }

    public static void ScriptHuntWithCommandButton()
    {
        // The hunter has the hunt module; with a weapon button it goes after an enemy nobody named.
        var session = StartMission([
            Scr.Script("Hunt", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("TEAM_HUNT_WITH_COMMAND_BUTTON", Scr.Team("Squad"), Scr.Ability("Cmd_AlphaFire"))], oneShot: true),
        ], objects: [Placed("AlphaHunter", 100m, 100m, "Squad", "Hunter"), Placed("BetaSoldier", 330m, 100m, "teamB", "Prey")]);
        Run(session, 240);
        var prey = NamedIn(session, "Prey")!;
        Check.That(prey.Body!.Health < prey.Body.MaxHealth || prey.EffectivelyDead, $"The hunting object attacked an enemy nobody ordered it to (hunter at {NamedIn(session, "Hunter")?.X}, diagnostics: {string.Join("; ", session.Engine.Diagnostics.Keys)}, rejections {string.Join(",", session.World.RejectionCounts.Select((count, index) => count > 0 ? $"{(CommandRejection)index}={count}" : ""))}).");
        Check.That(session.Engine.MissingActionHits.Count == 0, "TEAM_HUNT_WITH_COMMAND_BUTTON is implemented.");
    }

    public static void ScriptFacingUnmannedAndRecorded()
    {
        var session = StartMission([
            Scr.Script("All", [Scr.And(Scr.If("CONDITION_TRUE"))], [
                Scr.Do("NAMED_FACE_WAYPOINT", Scr.Unit("Soldier"), Scr.Waypoint("Far")),
                Scr.Do("NAMED_SET_UNMANNED_STATUS", Scr.Unit("Abandoned")),
                Scr.Do("TEAM_SET_ATTITUDE", Scr.Team("teamA"), Scr.Int(2)),
                Scr.Do("PLAYER_SET_RANKLEVEL", Scr.Side(PlayerA), Scr.Int(4)),
                Scr.Do("PLAYER_ADD_RANKLEVEL", Scr.Side(PlayerA), Scr.Int(1)),
                Scr.Do("PLAYER_SET_RANKLEVELLIMIT", Scr.Int(3)),
                Scr.Do("SCRIPTING_OVERRIDE_HULK_LIFETIME", Scr.Real(2m)),
                Scr.Do("NAMED_SET_HELD", Scr.Unit("Soldier"), Scr.Int(1))], oneShot: true),
            Scr.Script("Wipe", [Scr.And(Scr.If("FLAG", Scr.Flag("wipe"), Scr.Bool(true)))], [Scr.Do("DELETE_ALL_UNMANNED")], oneShot: true),
        ], objects: [Placed("AlphaSoldier", 100m, 200m, "teamA", "Soldier"), Placed("AlphaSoldier", 130m, 200m, "teamA", "Abandoned")]);
        Run(session, 3);
        var soldier = NamedIn(session, "Soldier")!;
        var abandoned = NamedIn(session, "Abandoned")!;
        Check.That(soldier.Angle.Abs() < Fixed.FromDecimal(0.01m), $"Facing the waypoint to the east sets heading 0 (was {soldier.Angle}).");
        Check.That(abandoned.Owner.IsSystem, "An unmanned object belongs to the neutral player.");
        Check.That(session.Engine.RankLevelOf(soldier.Owner) == 5 && session.Engine.RankLimit == 3 && session.Engine.HulkLifetimeOverrideTicks == 60, "Rank level, limit and hulk lifetime are recorded.");
        Check.That(session.Engine.RecordedActions >= 5, $"Recorded actions are counted apart from executed ones ({session.Engine.RecordedActions}).");
        Check.That(session.Engine.MissingActionHits.Count == 0, "No missing actions: " + string.Join(", ", session.Engine.MissingActionHits.Keys));
        session.Engine.SignalUiInteraction("wipe");
        Run(session, 3);
        var after = session.World.FindEntity(abandoned.Id);
        Check.That(after is null || after.DestroyRequested || after.EffectivelyDead, "DELETE_ALL_UNMANNED removes the unmanned object.");
    }

    public static void MissionHumanFactionAddsAndAdaptsTheHumanSide()
    {
        // A chosen persona: on a map without the named side a human side is added (faction money, hostile to the others), and a restored mission keeps it.
        var options = MissionOptions.Standard with { HumanSideName = "NewHero", HumanFaction = "FactionBeta" };
        var (map, fixture, terrain) = MissionMap([Scr.Script("Idle", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("NO_OP")], oneShot: true)]);
        var session = MissionSession.Start(options, fixture.Templates, fixture.Fingerprint, terrain, map);
        var added = session.Program.FindPlayer("NewHero");
        Check.That(added is { IsHuman: true, Faction: "FactionBeta" }, "The persona side was added as the human side.");
        Check.That(MissionSession.LocalPlayerOf(session.Program, options.HumanSideName) == added!.Id, "It is the viewer of the mission.");
        Check.That(session.World.MoneyOf(added.Id) == Fixed.FromInt(500), $"It starts with the persona faction's money ({session.World.MoneyOf(added.Id)}).");
        Check.That(session.World.AreEnemies(added.Id, session.Program.FindPlayer(PlayerA)!.Id) && session.World.AreEnemies(session.Program.FindPlayer(PlayerB)!.Id, added.Id), "It is hostile to the map sides, and they to it.");
        Run(session, 5);
        var restored = MissionSnapshot.Restore(MissionSnapshot.Write(session), fixture.Templates, fixture.Fingerprint, terrain, map);
        Check.That(restored.Program.FindPlayer("NewHero") is { IsHuman: true } && restored.StateHash() == session.StateHash(), "A restored mission has the same added side and the same state.");
        // A persona on a map whose human side exists replaces its faction without adding a side.
        var adapted = MissionSession.Start(MissionOptions.Standard with { HumanFaction = "FactionBeta" }, fixture.Templates, fixture.Fingerprint, terrain, map);
        Check.That(adapted.Program.FindPlayer(PlayerA)!.Faction == "FactionBeta" && adapted.Program.Players.Length == session.Program.Players.Length - 1, "The map's own human side takes the persona faction; no side is added.");
    }
}
