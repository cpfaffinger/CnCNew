using CnCNew.Data.World;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Scripting;
using CnCNew.Simulation.World;

/// <summary>
/// Wandering, panic and repulsors (F-22): <c>doTeamWander</c>, <c>doTeamWanderInPlace</c>, <c>doTeamPanic</c>, <c>doNamedSetRepulsor</c>, <c>doTeamSetRepulsor</c>, <c>AIWanderState</c>,
/// <c>AIWanderInPlaceState</c>, <c>AIPanicState</c>, <c>AIMoveAwayFromRepulsorsState</c>, <c>AI::findClosestRepulsor</c>, <c>Pathfinder::findSafePath</c>.
/// </summary>
static partial class Tests
{
    private const string RoamFixtureIni = """
        Locomotor RoamWalk
          Surfaces = GROUND
          Speed = 30
          MinSpeed = 0
          Acceleration = 1000
          Braking = 1000
          TurnRate = 3600
        End
        Locomotor RoamStroll
          Surfaces = GROUND
          Speed = 15
          MinSpeed = 0
          Acceleration = 1000
          Braking = 1000
          TurnRate = 3600
          WanderWidthFactor = 1.0
          WanderAboutPointRadius = 30.0
        End
        Locomotor RoamRun
          Surfaces = GROUND
          Speed = 120
          MinSpeed = 0
          Acceleration = 1000
          Braking = 1000
          TurnRate = 3600
        End
        Object Civilian
          KindOf = SELECTABLE INFANTRY CAN_BE_REPULSED
          VisionRange = 60.0
          Locomotor = SET_NORMAL RoamWalk
          Locomotor = SET_WANDER RoamStroll
          Locomotor = SET_PANIC RoamRun
          Body = ActiveBody Body01
            MaxHealth = 5000.0
            InitialHealth = 5000.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        Object Bystander
          KindOf = SELECTABLE INFANTRY
          VisionRange = 60.0
          Locomotor = SET_NORMAL RoamWalk
          Body = ActiveBody Body01
            MaxHealth = 5000.0
            InitialHealth = 5000.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """;

    private static IReadOnlyDictionary<string, string> RoamFiles() => new Dictionary<string, string>(AttitudeFiles()) { ["Data/INI/Object/RoamFixture.ini"] = RoamFixtureIni };

    private static Fixed Distance(Entity entity, decimal x, decimal y)
    {
        var dx = entity.X - Fixed.FromDecimal(x);
        var dy = entity.Y - Fixed.FromDecimal(y);
        return Fixed.Sqrt(dx * dx + dy * dy);
    }

    private static AIUpdateInterface AiIn(Entity entity) => entity.Modules.OfType<AIUpdateInterface>().First();

    public static void TeamWandersAlongAPath()
    {
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Go", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("TEAM_WANDER", Scr.Team("teamB"), Scr.Named(MapScriptParameterType.WaypointPath, "Stroll"))], oneShot: true),
            Scr.Script("End", [Scr.And(Scr.If("TEAM_REACHED_WAYPOINTS_END", Scr.Team("teamB"), Scr.Named(MapScriptParameterType.WaypointPath, "Stroll")))], [Scr.Do("SET_FLAG", Scr.Flag("done"), Scr.Bool(true))], oneShot: true),
        ], objects: [Placed("Civilian", 100m, 100m, "teamB", "Stroller")], paths: [("Stroll", [("A", 120m, 100m), ("B", 200m, 100m)])], extraFiles: RoamFiles());
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(session, session.Frames(90));
        var stroller = NamedIn(session, "Stroller")!;
        Check.That(!AiIn(stroller).IsIdle, "A wandering unit is not idle.");
        Check.That(stroller.X > Fixed.FromInt(105) && stroller.X < Fixed.FromInt(150), $"Three seconds on the wander locomotor (15 a second) cover about 45 units ({stroller.X}).");
        Run(session, session.Frames(600));
        Check.That(session.Engine.FlagValue("done"), "The end of the path is the end of the state: the waypoint-end condition fires.");
        Check.That(AiIn(NamedIn(session, "Stroller")!).IsIdle, "Afterwards the unit is idle.");
        Check.That(Distance(NamedIn(session, "Stroller")!, 200m, 100m) < Fixed.FromInt(16), $"The goal lies at most a cell off the last waypoint ({Distance(NamedIn(session, "Stroller")!, 200m, 100m)}).");
        Check.That(session.Engine.MissingActionHits.Count == 0 && ScriptCoverage.ActionSupport("TEAM_WANDER") == ScriptSupport.Executed, "TEAM_WANDER is executed.");
    }

    public static void TeamWandersInPlace()
    {
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Go", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("TEAM_WANDER_IN_PLACE", Scr.Team("teamB"))], oneShot: true),
            Scr.Script("Stop", [Scr.And(Scr.If("FLAG", Scr.Flag("stop"), Scr.Bool(true)))], [Scr.Do("MOVE_NAMED_UNIT_TO", Scr.Unit("Stroller"), Scr.Waypoint("Mid"))], oneShot: true),
        ], objects: [Placed("Civilian", 100m, 100m, "teamB", "Stroller"), Placed("Bystander", 100m, 300m, "teamB", "Other")], extraFiles: RoamFiles());
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(session, session.Frames(10));
        var stroller = NamedIn(session, "Stroller")!;
        var moved = false;
        var farthest = Fixed.Zero;
        for (var step = 0; step < 40; step++)
        {
            Run(session, session.Frames(15));
            var away = Distance(stroller, 100m, 100m);
            farthest = Fixed.Max(farthest, away);
            moved |= away > Fixed.FromInt(5);
            Check.That(!AiIn(stroller).IsIdle, "A wandering unit is never idle.");
        }
        Check.That(moved && farthest <= Fixed.FromInt(45), $"It walks around its origin, no further than the radius along either axis ({farthest}).");
        var restored = MissionSnapshot.Restore(MissionSnapshot.Write(session), fixture.Templates, fixture.Fingerprint, terrain, map);
        Check.That(restored.StateHash() == session.StateHash(), "A restored mission matches.");
        Run(restored, restored.Frames(20));
        Run(session, session.Frames(20));
        Check.That(restored.StateHash() == session.StateHash(), "And goes on alike.");
        session.Engine.SignalUiInteraction("stop");
        Run(session, session.Frames(600));
        Check.That(Distance(stroller, 250m, 200m) < Fixed.FromInt(3) && AiIn(stroller).IsIdle, $"An order ends the wandering ({Distance(stroller, 250m, 200m)}).");
    }

    public static void CivilianRunsFromARepulsor()
    {
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Mark", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("NAMED_SET_REPULSOR", Scr.Unit("Scare"), Scr.Int(1))], oneShot: true),
        ], objects: [Placed("Civilian", 250m, 200m, "teamB", "Runner"), Placed("Bystander", 280m, 200m, "teamB", "Scare"), Placed("Bystander", 250m, 150m, "teamB", "Calm")], extraFiles: RoamFiles());
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(session, session.Frames(70));
        var runner = NamedIn(session, "Runner")!;
        Check.That(runner.Panicking && !AiIn(runner).IsIdle, $"Within two seconds an idle civilian sees the repulsor in its vision and runs (panicking {runner.Panicking}).");
        Run(session, session.Frames(240));
        Check.That(!runner.Panicking, "It has stopped running.");
        var scare = NamedIn(session, "Scare")!;
        Check.That(Distance(runner, 280m, 200m) >= Fixed.FromInt(205), $"It ran beyond its vision and the repulsed distance (60 + 150; {Distance(runner, 280m, 200m)}).");
        Check.That(NamedIn(session, "Calm")!.X == Fixed.FromInt(250), "What cannot be repulsed stays.");
        Check.That(scare.HasStatus(CnCNew.Simulation.Modules.ObjectStatusBits.Index("REPULSOR")), "A repulsor that is no civilian stays one.");
        var at = (runner.X, runner.Y);
        Run(session, session.Frames(120));
        Check.That(!AiIn(runner).IsIdle && (runner.X, runner.Y) != at, "It wanders where it stopped.");
    }

    public static void DamagedCivilianScaresOthers()
    {
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Hurt", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("NAMED_DAMAGE", Scr.Unit("Hurt"), Scr.Int(10))], oneShot: true),
        ], objects: [Placed("Civilian", 400m, 200m, "teamB", "Hurt"), Placed("Civilian", 430m, 200m, "teamB", "Near")], extraFiles: RoamFiles());
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(session, session.Frames(15));
        var hurt = NamedIn(session, "Hurt")!;
        var repulsor = CnCNew.Simulation.Modules.ObjectStatusBits.Index("REPULSOR");
        Check.That(hurt.HasStatus(repulsor), "A damaged civilian is a repulsor.");
        Run(session, session.Frames(55));
        Check.That(NamedIn(session, "Near")!.Panicking, "The other civilian runs.");
        Run(session, session.Frames(10));
        Check.That(!hurt.HasStatus(repulsor), "After two seconds the civilian is no repulsor any more.");
    }

    public static void ArmedEnemyRepulses()
    {
        var (map, fixture, terrain) = MissionMap([Scr.Script("Idle", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("NO_OP")], oneShot: true)],
            objects: [Placed("Civilian", 150m, 100m, "teamB", "Near"), Placed("MoodGunner", 200m, 100m, "teamA", "Armed"),
                      Placed("Civilian", 150m, 300m, "teamB", "Calm"), Placed("Bystander", 200m, 300m, "teamA", "Unarmed")], extraFiles: RoamFiles());
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(session, session.Frames(70));
        Check.That(NamedIn(session, "Near")!.Panicking, "An enemy that can attack repulses.");
        Check.That(!NamedIn(session, "Calm")!.Panicking && AiIn(NamedIn(session, "Calm")!).IsIdle, "An enemy that cannot does not.");
    }

    public static void PanicRunsOnThePanicLocomotors()
    {
        var (map, fixture, terrain) = MissionMap([
            Scr.Script("Go", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("TEAM_PANIC", Scr.Team("teamB"), Scr.Named(MapScriptParameterType.WaypointPath, "Flight"))], oneShot: true),
            Scr.Script("Scare", [Scr.And(Scr.If("FLAG", Scr.Flag("scare"), Scr.Bool(true)))], [Scr.Do("NAMED_SET_REPULSOR", Scr.Unit("Scare"), Scr.Int(1))], oneShot: true),
        ], objects: [Placed("Civilian", 100m, 100m, "teamB", "Runner"), Placed("Bystander", 550m, 100m, "teamPlayerA", "Scare")],
            paths: [("Flight", [("A", 110m, 100m), ("B", 500m, 100m), ("C", 500m, 300m)])], extraFiles: RoamFiles());
        var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
        Run(session, session.Frames(60));
        var runner = NamedIn(session, "Runner")!;
        Check.That(runner.Panicking, "A panicking unit has the condition PANICKING.");
        Check.That(runner.X > Fixed.FromInt(170), $"Two seconds on the panic locomotor (120 a second) are far beyond the normal one ({runner.X}).");
        session.Engine.SignalUiInteraction("scare");
        Run(session, session.Frames(60));
        Check.That(runner.Panicking, "Near the repulsor it goes on panicking, now away from it.");
        Run(session, session.Frames(240));
        Check.That(Distance(runner, 550m, 100m) >= Fixed.FromInt(205), $"The repulsor turned it aside ({Distance(runner, 550m, 100m)}).");
    }
}
