using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

// #463: a jet's stop and an attack on a place given to a returning jet, on real data.
static partial class Tests
{
    private static bool InTakeoff(JetAIUpdate ai) => ai.Phase is JetAIUpdate.JetPhase.AwaitTakeoff or JetAIUpdate.JetPhase.TaxiToTakeoff or JetAIUpdate.JetPhase.PauseBeforeTakeoff
        or JetAIUpdate.JetPhase.TakingOff;

    private static void SaveAndRestore(ParkedJetScene scene, string fingerprint)
    {
        var restored = WorldSnapshot.Restore(WorldSnapshot.Write(scene.World), scene.World.Templates, fingerprint, scene.World.Terrain);
        scene.World = restored;
        scene.Jet = restored.FindEntity(scene.Jet.Id)!;
        scene.Ai = scene.Jet.Modules.OfType<JetAIUpdate>().Single();
        scene.Tanks = scene.Tanks.Select(item => restored.FindEntity(item.Id)!).ToArray();
    }

    private static SubmitResult StopJet(ParkedJetScene scene)
        => scene.Send(next => new StopCommand(new PlayerId(1), next, scene.World.Tick, scene.Jet.Id));

    /// <summary>Steps until <paramref name="done"/> holds; the jet's own frame it held in, or -1.</summary>
    private static long JetUntil(ParkedJetScene scene, System.Func<ParkedJetScene, bool> done, int seconds)
    {
        for (var tick = 0; tick < scene.World.Frames(30 * seconds); tick++)
        {
            scene.World.Step();
            if (done(scene)) return scene.Frame();
        }
        return -1;
    }

    /// <summary>
    /// #463: <c>JetAIUpdate::aiDoCommand</c> answers <c>AICMD_IDLE</c> (<c>MSG_DO_STOP</c> -> <c>groupIdle</c> -> <c>aiIdle</c>): while a take-off or landing runs it stores the stop, which
    /// replaces the stored order ("just store the sucker"); in the air a fixed-wing jet (<c>isAirborneTarget</c>, not <c>PRODUCED_AT_HELIPAD</c>) returns to land
    /// (<c>RETURNING_FOR_LANDING</c>); parked, a stop is no reason to take off. A parked Raptor ordered to move and stopped while it taxis to the runway (saved and restored with the stop
    /// stored) flies home as soon as it is airborne instead of to the place of the move, and a stop given while it lands lets it park and stay; a Raptor stopped in flight returns at once;
    /// a parked Raptor stopped stays parked. Before, the stop was a move to the place the jet stood: stored during the take-off, it flew back to the runway's point after it, and in the
    /// air it circled there. At 30, 60 and 120 Hz, in the same frames of the jet's own.
    /// </summary>
    public static void RealJetAnswersTheStopAsItsOwnAiDoes(string directory)
    {
        var source = RealGraph(directory);
        var timelines = new List<(SimulationRate Rate, string Line)>();
        foreach (var rate in TargetRates)
        {
            var line = new List<string>();
            var far = (X: Fixed.FromInt(2000), Y: Fixed.FromInt(4800));

            // Stopped while it taxis to the runway, and again while it lands.
            {
                var scene = BuildParkedJetScene(source, rate, "AmericaJetRaptor");
                scene.ToOwnFrameStart();
                var ordered = scene.Next();
                Check.That(scene.Send(next => new SetMoveTargetCommand(new PlayerId(1), next, scene.World.Tick, scene.Jet.Id, far.X, far.Y)).Accepted, $"{rate}: the Raptor is ordered to move.");
                var stopped = JetUntil(scene, value => value.Ai.Phase == JetAIUpdate.JetPhase.TaxiToTakeoff, 30);
                Check.That(stopped > 0, $"{rate}: the Raptor taxis to the runway.");
                var stop = StopJet(scene);
                Check.That(stop.Accepted, $"{rate}: the taxiing Raptor takes the stop ({stop.Reason}).");
                scene.World.Step();
                Check.That(scene.Ai.HasPendingCommand && InTakeoff(scene.Ai), $"{rate}: the taxiing Raptor stores the stop and goes on with its take-off ({scene.Ai.Phase}).");
                SaveAndRestore(scene, source.Fingerprint);
                var airborne = JetUntil(scene, value => value.Ai.AllowAirLoco && !InTakeoff(value.Ai), 60);
                Check.That(airborne > stopped && scene.Ai.Phase == JetAIUpdate.JetPhase.ReturningForLanding && !scene.Ai.HasPendingCommand,
                    $"{rate}: in the frame it is airborne the Raptor returns to land ({scene.Ai.Phase}, frame {airborne}, pending {scene.Ai.HasPendingCommand}).");
                Check.That((scene.Jet.MoveTargetX - far.X).Abs() + (scene.Jet.MoveTargetY - far.Y).Abs() > Fixed.FromInt(1000),
                    $"{rate}: it does not fly to the place of the move ({scene.Jet.MoveTargetX}, {scene.Jet.MoveTargetY}).");
                var landing = JetUntil(scene, value => value.Ai.Phase == JetAIUpdate.JetPhase.Landing, 120);
                Check.That(landing > airborne, $"{rate}: the Raptor lands ({scene.Ai.Phase}).");
                Check.That(StopJet(scene).Accepted, $"{rate}: the landing Raptor takes a stop.");
                scene.World.Step();
                Check.That(scene.Ai.HasPendingCommand, $"{rate}: the landing Raptor stores the stop.");
                var parked = JetUntil(scene, value => value.Parked && !value.Ai.HasPendingCommand, 120);
                Check.That(parked > landing, $"{rate}: the Raptor parks and forgets the stop ({scene.Ai.Phase}, pending {scene.Ai.HasPendingCommand}).");
                var leaves = JetUntil(scene, value => !value.Parked, 10);
                Check.That(leaves < 0, $"{rate}: the parked Raptor does not take off for the stored stop ({scene.Ai.Phase}).");
                line.Add($"stop while taxiing: airborne {airborne - ordered}, landing {landing - ordered}, parked {parked - ordered}");
            }

            // Stopped in flight, and stopped while parked.
            {
                var scene = BuildParkedJetScene(source, rate, "AmericaJetRaptor");
                scene.ToOwnFrameStart();
                Check.That(StopJet(scene).Accepted, $"{rate}: the parked Raptor takes a stop.");
                var leaves = JetUntil(scene, value => !value.Parked || value.Ai.HasPendingCommand, 10);
                Check.That(leaves < 0, $"{rate}: the parked Raptor stays parked and stores nothing ({scene.Ai.Phase}, pending {scene.Ai.HasPendingCommand}).");
                scene.ToOwnFrameStart();
                var ordered = scene.Next();
                Check.That(scene.Send(next => new SetMoveTargetCommand(new PlayerId(1), next, scene.World.Tick, scene.Jet.Id, far.X, far.Y)).Accepted, $"{rate}: the Raptor is ordered to move.");
                var airborne = UntilJetFlies(scene);
                Check.That(airborne > ordered && scene.Jet.HasMoveTarget, $"{rate}: the Raptor flies toward the place of the move (frame {airborne}).");
                // A few seconds into the flight, at the start of a frame of its own.
                JetUntil(scene, value => false, 3);
                scene.ToOwnFrameStart();
                var stoppedAt = scene.Next();
                Check.That(StopJet(scene).Accepted, $"{rate}: the flying Raptor takes the stop.");
                scene.World.Step();
                Check.That(scene.Ai.Phase == JetAIUpdate.JetPhase.ReturningForLanding && !scene.Ai.HasPendingCommand,
                    $"{rate}: the flying Raptor returns to land at once ({scene.Ai.Phase}, pending {scene.Ai.HasPendingCommand}).");
                var parked = JetUntil(scene, value => value.Parked, 120);
                Check.That(parked > stoppedAt, $"{rate}: the stopped Raptor lands and parks ({scene.Ai.Phase}).");
                line.Add($"stop in flight: airborne {airborne - ordered}, stopped {stoppedAt - ordered}, parked {parked - ordered}");
            }
            timelines.Add((rate, string.Join("; ", line)));
        }
        foreach (var (rate, text) in timelines) System.Console.WriteLine($"       #463 stop {rate}: {text}");
        Check.That(timelines.Select(value => value.Line).Distinct().Count() == 1, "The jets take off, return and park in the same frames of their own at 30, 60 and 120 Hz.");
    }

    /// <summary>
    /// #463: an attack on a place given to a jet that returns to its airfield replaces the return (<c>AIUpdateInterface::aiDoCommand</c> clears the state machine; the return,
    /// <c>JetOrHeliReturnForLandingState</c>, is no take-off or landing in progress, so <c>JetAIUpdate::aiDoCommand</c> stores nothing). A Raptor sent away, idle for its
    /// <c>ReturnToBaseIdleTime</c> and on its way home, ordered to attack a tank's place: it attacks the place and fires at it before it lands. Before, the attack was noted but the return
    /// went on and the jet landed. At 30, 60 and 120 Hz, in the same frames of the jet's own.
    /// </summary>
    public static void RealReturningJetAttacksAPlace(string directory)
    {
        var source = RealGraph(directory);
        var timelines = new List<(SimulationRate Rate, string Line)>();
        foreach (var rate in TargetRates)
        {
            var scene = BuildParkedJetScene(source, rate, "AmericaJetRaptor");
            var tank = scene.Tanks[1];
            scene.ToOwnFrameStart();
            var ordered = scene.Next();
            Check.That(scene.Send(next => new SetMoveTargetCommand(new PlayerId(1), next, scene.World.Tick, scene.Jet.Id, Fixed.FromInt(2000), Fixed.FromInt(4000))).Accepted,
                $"{rate}: the Raptor is ordered to move.");
            var returning = JetUntil(scene, value => value.Ai.Phase == JetAIUpdate.JetPhase.ReturningForLanding, 120);
            Check.That(returning > ordered, $"{rate}: the idle Raptor returns to its airfield ({scene.Ai.Phase}).");
            var (world, jet, ai) = (scene.World, scene.Jet, scene.Ai);
            scene.ToOwnFrameStart();
            Check.That(ai.Phase == JetAIUpdate.JetPhase.ReturningForLanding, $"{rate}: still returning ({ai.Phase}).");
            var attackedAt = scene.Next();
            var fired = jet.LastFireTick;
            var attack = scene.Send(next => new AttackPositionCommand(new PlayerId(1), next, world.Tick, ImmutableArray.Create(jet.Id), tank.X, tank.Y, false, WeaponSlot.Best, 0));
            Check.That(attack.Accepted, $"{rate}: the returning Raptor is ordered to attack the place ({attack.Reason}).");
            world.Step();
            Check.That(ai.Phase == JetAIUpdate.JetPhase.None && world.AttackedPlaceOf(jet) == (tank.X, tank.Y),
                $"{rate}: the attack replaces the return ({ai.Phase}, place {world.AttackedPlaceOf(jet)}).");
            long shot = -1;
            for (var tick = 0; tick < world.Frames(30 * 120) && shot < 0; tick++)
            {
                world.Step();
                if (jet.LastFireTick != fired) shot = scene.Frame();
                Check.That(ai.AllowAirLoco && ai.Phase is not (JetAIUpdate.JetPhase.AwaitLanding or JetAIUpdate.JetPhase.Landing),
                    $"{rate}: the Raptor does not land before it fired at the place ({ai.Phase}).");
            }
            Check.That(shot > attackedAt, $"{rate}: the Raptor fires at the place (frame {shot}, ordered in {attackedAt}).");
            timelines.Add((rate, $"returning {returning - ordered}, attack {attackedAt - ordered}, fired {shot - ordered}"));
        }
        foreach (var (rate, text) in timelines) System.Console.WriteLine($"       #463 place {rate}: {text}");
        Check.That(timelines.Select(value => value.Line).Distinct().Count() == 1, "The Raptor returns, attacks and fires in the same frames of its own at 30, 60 and 120 Hz.");
    }
}
