using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Snapshots;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// #428: a rider leaves a tunnel by its door. <c>TunnelContain::scatterToNearbyPosition</c> is never called in the original (it is not virtual; only <c>TransportContain</c> and
/// <c>MobNexusContain</c> call their own); an exit order or an evacuation sends the rider through <c>OpenContain::exitObjectViaDoor</c> (<c>AIExitState::update</c>): set on the tunnel's
/// <c>ExitStart</c> bone with its heading, it walks to <c>ExitEnd</c> and on to the tunnel's rally point. A sale of the last tunnel takes everybody out at once
/// (<c>removeAllContained</c>), onto the tunnel and with no order (<c>TunnelContain::onRemoving</c>).
/// </summary>
internal static partial class Tests
{
    // The door of the fixture's tunnel: ExitStart 25 ahead of the centre, ExitEnd 60 ahead, both turned with the tunnel.
    private static ModelGeometry TunnelDoorBones() => new([], [new KeyValuePair<string, ImmutableArray<ExitPath>>("Tunnel",
    [
        new ExitPath(new BonePose(Fixed.FromInt(25), Fixed.Zero, Fixed.Zero, Fixed.Zero), new BonePose(Fixed.FromInt(60), Fixed.Zero, Fixed.Zero, Fixed.Zero)),
    ])]);

    private static (SimulationWorld World, Feeder Feeder) TunnelDoorWorld(SimulationRate rate)
    {
        var geometry = TunnelDoorBones();
        var fixture = new SyntheticFixture(rate, files: TransportFiles());
        var templates = TemplateCatalog.Build(fixture.Graph, fixture.Bindings, rate, geometry);
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 11, CombatPlayers, SyntheticFixture.FixtureId, geometry.Combine(fixture.Fingerprint), string.Empty), templates, null);
        return (world, new Feeder(world));
    }

    /// <summary>Riders evacuated through a turned tunnel appear on its <c>ExitStart</c> bone with its heading, walk to <c>ExitEnd</c> and on to the rally point - the same at 30, 60 and 120 Hz.</summary>
    public static void TunnelRidersLeaveByTheDoor()
    {
        var quarter = FixedTrig.Pi / Fixed.FromInt(2);
        var targets = new List<string>();
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var (world, feeder) = TunnelDoorWorld(rate);
            feeder.Spawn(1, "Tunnel", x: 200, y: 200, angle: quarter);
            feeder.Spawn(1, "Tunnel", x: 600, y: 200);
            feeder.Spawn(1, "Grunt", x: 600, y: 200);
            feeder.Spawn(1, "Grunt", x: 600, y: 200);
            for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
            var b = At(world, "Tunnel", 200, 200);
            var a = At(world, "Tunnel", 600, 200);
            var grunts = Grunts(world);
            foreach (var grunt in grunts) Check.That(feeder.EnterContainer(1, grunt.Id.Value, a.Id.Value).Accepted, $"{rate}: grunt {grunt.Id.Value} enters tunnel A.");
            for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
            Check.That(grunts.All(grunt => grunt.ContainedBy == a.Id), $"{rate}: both are in the network.");

            Check.That(feeder.Evacuate(1, b.Id.Value).Accepted, $"{rate}: tunnel B is evacuated.");
            var result = world.Step();
            Check.That(result.Events.Count(value => value.Kind == SimEventKind.EntityUncontained && value.Other == b.Id) == 2, $"{rate}: both leave through tunnel B at once (its door is never busy).");
            // ExitStart (25, 0) turned by a quarter: (200, 225); ExitEnd: (200, 260).
            var first = grunts[0];
            var near = Fixed.FromDecimal(0.01m);
            Check.That((first.X - Fixed.FromInt(200)).Abs() < near && (first.Y - Fixed.FromInt(225)).Abs() <= Fixed.FromInt(2), $"{rate}: the first rider stands at the door's start, not at a random point ({first.X}, {first.Y}).");
            Check.That(grunts.All(grunt => grunt.Angle == b.Angle), $"{rate}: both face the way the tunnel faces.");
            Check.That(first.HasMoveTarget && (first.MoveTargetX - Fixed.FromInt(200)).Abs() < near && (first.MoveTargetY - Fixed.FromInt(260)).Abs() < near,
                $"{rate}: the first rider walks to the door's end ({first.MoveTargetX}, {first.MoveTargetY}).");
            Check.That(grunts[1].HasMoveTarget && (grunts[1].MoveTargetX != first.MoveTargetX || grunts[1].MoveTargetY != first.MoveTargetY),
                $"{rate}: the second rider gets a place of its own near the door's end ({grunts[1].MoveTargetX}, {grunts[1].MoveTargetY}).");
            targets.Add(string.Join(";", grunts.Select(grunt => $"{grunt.MoveTargetX.Raw},{grunt.MoveTargetY.Raw}")));

            for (var tick = 0; tick < world.Frames(300); tick++) world.Step();
            foreach (var grunt in grunts)
            {
                var dx = grunt.X - Fixed.FromInt(200);
                var dy = grunt.Y - Fixed.FromInt(260);
                Check.That(!grunt.ContainedBy.IsValid && !grunt.HasMoveTarget && Fixed.Sqrt(dx * dx + dy * dy) < Fixed.FromInt(30), $"{rate}: grunt {grunt.Id.Value} stands at the door's end ({grunt.X}, {grunt.Y}).");
            }
            Check.That(Apart(grunts[0], grunts[1]), $"{rate}: the two stand apart.");

            // With a rally point the path goes on to it (m_rallyPointExists), and the point survives a snapshot.
            Check.That(feeder.SetRallyPoint(1, b.Id.Value, 200, 600).Accepted, $"{rate}: tunnel B takes a rally point.");
            foreach (var grunt in grunts) Check.That(feeder.EnterContainer(1, grunt.Id.Value, a.Id.Value).Accepted, $"{rate}: grunt {grunt.Id.Value} enters tunnel A again.");
            for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
            Check.That(feeder.ExitContainer(1, grunts[0].Id.Value).Accepted, $"{rate}: one grunt leaves through tunnel A, which has no rally point.");
            world.Step();
            // ExitStart of A (unturned): (625, 200).
            // It took its first step towards the end in the tick it left.
            Check.That((grunts[0].X - Fixed.FromInt(625)).Abs() <= Fixed.FromInt(2) && (grunts[0].Y - Fixed.FromInt(200)).Abs() < near, $"{rate}: it comes out of tunnel A's door ({grunts[0].X}, {grunts[0].Y}).");
            Check.That(feeder.Evacuate(1, b.Id.Value).Accepted, $"{rate}: the other is evacuated through tunnel B.");
            world.Step();
            for (var tick = 0; tick < world.Frames(30 * 30); tick++) world.Step();
            var walker = grunts[1];
            var rx = walker.X - Fixed.FromInt(200);
            var ry = walker.Y - Fixed.FromInt(600);
            Check.That(!walker.ContainedBy.IsValid && Fixed.Sqrt(rx * rx + ry * ry) < Fixed.FromInt(20), $"{rate}: the grunt that left by tunnel B's door walked on to its rally point ({walker.X}, {walker.Y}).");
            var ax = grunts[0].X - Fixed.FromInt(660);
            var ay = grunts[0].Y - Fixed.FromInt(200);
            Check.That(Fixed.Sqrt(ax * ax + ay * ay) < Fixed.FromInt(30), $"{rate}: the grunt that left by tunnel A's door stays at its end ({grunts[0].X}, {grunts[0].Y}).");
        }
        Check.That(targets.Distinct().Count() == 1, $"The riders walk to the same places at 30, 60 and 120 Hz ({string.Join(" | ", targets)}).");
    }

    /// <summary>Selling the last tunnel takes its riders out at once (<c>removeAllContained</c>, "Can't be order to exit"): they stand on the tunnel with no order, at 30, 60 and 120 Hz.</summary>
    public static void TunnelSaleLeavesRidersOnTheTunnel()
    {
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var (world, feeder) = TunnelDoorWorld(rate);
            feeder.Spawn(1, "Tunnel", x: 200, y: 200);
            feeder.Spawn(1, "Grunt", x: 200, y: 200);
            for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
            var tunnel = Get(world, "Tunnel");
            var grunt = Get(world, "Grunt");
            Check.That(feeder.EnterContainer(1, grunt.Id.Value, tunnel.Id.Value).Accepted, $"{rate}: the grunt enters.");
            for (var tick = 0; tick < rate.TicksPerOriginalFrame; tick++) world.Step();
            var angle = grunt.Angle;
            Check.That(feeder.Sell(1, tunnel.Id.Value).Accepted, $"{rate}: the last tunnel is sold.");
            world.Step();
            Check.That(!grunt.ContainedBy.IsValid && grunt.X == tunnel.X && grunt.Y == tunnel.Y && !grunt.HasMoveTarget && grunt.Angle == angle,
                $"{rate}: the rider stands on the tunnel, not at its door or a random point, and has no order ({grunt.X}, {grunt.Y}, moving {grunt.HasMoveTarget}).");
        }
    }
}
