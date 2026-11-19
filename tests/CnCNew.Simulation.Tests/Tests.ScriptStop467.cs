using System.Collections.Generic;
using System.Linq;
using CnCNew.Data.World;
using CnCNew.Simulation.Mission;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

// #467: a script's stop is the player's stop.
static partial class Tests
{
    /// <summary>
    /// #467: <c>ScriptActions::doNamedStop</c> calls <c>aiIdle</c> and <c>doTeamStop</c> <c>AIGroup::groupIdle</c>, the player's stop (<c>MSG_DO_STOP</c>). A soldier of a team and a lone
    /// soldier walk east by script; a script stops the team (<c>TEAM_STOP</c>) and the lone one (<c>NAMED_STOP</c>): each stands where the order finds it, the farthest it ever came,
    /// and stays there idle. A jet in the air that a script stops (<c>NAMED_STOP</c>) returns to land at the airfield, as <c>JetAIUpdate::aiDoCommand</c> answers <c>AICMD_IDLE</c> (#463);
    /// before, the script's stop was a move to the place the unit stood, and the jet circled there. The same places and frames at 30, 60 and 120 Hz.
    /// </summary>
    public static void ScriptStopIsThePlayersStop()
    {
        var results = new List<(SimulationRate Rate, string Line)>();
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var (map, fixture, terrain) = MissionMap([
                Scr.Script("Go", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("MOVE_TEAM_TO", Scr.Team("Squad"), Scr.Waypoint("Far")), Scr.Do("MOVE_TEAM_TO", Scr.Team("teamA"), Scr.Waypoint("Far"))], oneShot: true),
                Scr.Script("Halt", [Scr.And(Scr.If("FLAG", Scr.Flag("halt"), Scr.Bool(true)))], [Scr.Do("TEAM_STOP", Scr.Team("Squad")), Scr.Do("NAMED_STOP", Scr.Unit("Lone"))], oneShot: true),
            ], objects: [Placed("AlphaSoldier", 100m, 150m, "Squad", "S1"), Placed("AlphaSoldier", 100m, 250m, "teamA", "Lone")], rate: rate);
            var session = MissionSession.Start(MissionOptions.Standard, fixture.Templates, fixture.Fingerprint, terrain, map);
            Run(session, session.Frames(60));
            var soldiers = new[] { NamedIn(session, "S1")!, NamedIn(session, "Lone")! };
            Check.That(soldiers.All(soldier => soldier.HasMoveTarget && soldier.X > Fixed.FromInt(110)), $"{rate}: the soldiers walk east ({string.Join(", ", soldiers.Select(soldier => soldier.X))}).");
            session.Engine.SignalUiInteraction("halt");
            var farthest = soldiers.Select(soldier => soldier.X).ToArray();
            for (var tick = 0; tick < session.Frames(90); tick++)
            {
                session.Step();
                for (var index = 0; index < soldiers.Length; index++) farthest[index] = Fixed.Max(farthest[index], soldiers[index].X);
            }
            for (var index = 0; index < soldiers.Length; index++)
            {
                var soldier = soldiers[index];
                Check.That(!soldier.HasMoveTarget && session.World.IsAiIdle(soldier) && soldier.X == farthest[index] && soldier.X < Fixed.FromInt(300),
                    $"{rate}: the stopped soldier stands where the order found it, idle ({soldier.X}, the farthest {farthest[index]}, moving {soldier.HasMoveTarget}).");
            }
            Check.That(session.Engine.MissingActionHits.Count == 0, $"{rate}: no action counted as missing.");

            // A jet in the air, sent east by script and stopped by NAMED_STOP: JetAIUpdate::aiDoCommand(AICMD_IDLE) sends it home to land at the airfield (#463).
            var airFiles = new Dictionary<string, string>
            {
                ["Data/INI/FXList.ini"] = AirFixtureFiles["Data/INI/FXList.ini"],
                ["Data/INI/Upgrade.ini"] = AirFixtureFiles["Data/INI/Upgrade.ini"],
                ["Data/INI/Object/AirFixture.ini"] = AirFixtureFiles["Data/INI/Object/AirFixture.ini"],
            };
            var flying = new MapObject("AirTestJet", 100m, 300m, 60m, 0m, 0, new MapProperties([Scr.Text("originalOwner", "teamPlayerA"), Scr.Text("objectName", "Jet")]));
            var (airMap, airFixture, airTerrain) = MissionMap([
                Scr.Script("Go", [Scr.And(Scr.If("CONDITION_TRUE"))], [Scr.Do("MOVE_NAMED_UNIT_TO", Scr.Unit("Jet"), Scr.Waypoint("Far"))], oneShot: true),
                Scr.Script("Halt", [Scr.And(Scr.If("FLAG", Scr.Flag("halt"), Scr.Bool(true)))], [Scr.Do("NAMED_STOP", Scr.Unit("Jet"))], oneShot: true),
            ], objects: [Placed("AirTestAirfield", 300m, 100m, "teamA"), flying], extraFiles: airFiles, rate: rate);
            var air = MissionSession.Start(MissionOptions.Standard, airFixture.Templates, airFixture.Fingerprint, airTerrain, airMap);
            Run(air, air.Frames(60));
            var jet = NamedIn(air, "Jet")!;
            var jetAi = JetOf(jet);
            Check.That(jetAi.AllowAirLoco && jetAi.Phase == JetAIUpdate.JetPhase.None && air.World.IsAirborneTarget(jet), $"{rate}: the jet flies its script's move ({jetAi.Phase}, z {jet.Z}).");
            air.Engine.SignalUiInteraction("halt");
            long returned = -1;
            for (var tick = 0; tick < air.Frames(30) && returned < 0; tick++)
            {
                air.Step();
                if (jetAi.Phase == JetAIUpdate.JetPhase.ReturningForLanding) returned = tick;
            }
            Check.That(returned >= 0, $"{rate}: the stopped jet returns to land ({jetAi.Phase}).");
            for (var tick = 0; tick < air.Frames(30 * 60) && jetAi.AllowAirLoco; tick++) air.Step();
            Check.That(!jetAi.AllowAirLoco, $"{rate}: it lands at the airfield ({jetAi.Phase}).");
            results.Add((rate, string.Join("; ", soldiers.Select(soldier => $"{soldier.X.Raw},{soldier.Y.Raw}")) + $"; jet returns {returned / air.World.Time.TicksPerOriginalFrame}"));
        }
        foreach (var (rate, text) in results) System.Console.WriteLine($"       #467 {rate}: {text}");
        Check.That(results.Select(value => value.Line).Distinct().Count() == 1, "The soldiers stop on the same places at 30, 60 and 120 Hz.");
    }
}
