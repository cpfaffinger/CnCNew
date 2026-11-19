using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

// #463: an attack on a place given to a returning jet, on real data.
static partial class Tests
{
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
