using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Fidelity finding F-62: how a Chinook lets its riders out. <c>ChinookAIUpdate::aiDoCommand</c> sends an unload at a place (<c>AICMD_MOVE_TO_POSITION_AND_EVACUATE</c>) down the
/// Chinook's own states - move, land, <c>ChinookEvacuateState</c> (<c>removeAllContained</c>: everybody out in one frame, where the Chinook stands), take off; a bare evacuation
/// lands and goes the standard way, rider after rider through the door (<c>ExitDelay</c>), and the idle Chinook stays down while riders wait at the door
/// (<c>ChinookAIUpdate::update</c>, <c>hasObjectsWantingToEnterOrExit</c>); a rider at the door of a Chinook in the air waits (<c>AIExitState::update</c>, <c>WAIT_TO_EXIT</c>).
/// </summary>
static partial class Tests
{
    public static void ChinookUnloadAtAPlaceAtOnceAndAtTheDoorOneByOne()
    {
        var files = TransportFiles().ToDictionary(pair => pair.Key, pair => pair.Value.Replace("Slots = 4\n", "Slots = 4\n    ExitDelay = 100\n"));
        Check.That(files.Values.Count(value => value.Contains("ExitDelay = 100")) == 1, "Setup: the Chinook's door has an exit delay of 100 ms (3 frames).");
        foreach (var rate in new[] { SimulationRate.Hz30, SimulationRate.Hz60, SimulationRate.Hz120 })
        {
            var fixture = new SyntheticFixture(rate, files: files);
            var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 11, CombatPlayers, SyntheticFixture.FixtureId, fixture.Fingerprint), fixture.Templates);
            var feeder = new Feeder(world);
            feeder.Spawn(1, "Rotorcraft", x: 150, y: 150);
            for (var tick = 0; tick < world.Frames(25); tick++) world.Step();
            var chinook = Get(world, "Rotorcraft");
            Check.That(chinook.Z == Fixed.FromInt(100), $"{rate}: setup - the Chinook hovers at its height.");
            for (var i = 0; i < 3; i++) feeder.Spawn(1, "Grunt", x: 120, y: 130 + 20 * i);
            world.Step();
            var grunts = world.Entities.Where(value => value.Template.Name == "Grunt").OrderBy(value => value.Id.Value).ToArray();
            bool Ground() => chinook.Z == world.GroundHeightAt(chinook.X, chinook.Y);

            void Board()
            {
                foreach (var grunt in grunts) Check.That(feeder.MoveAndEnter(1, grunt.Id.Value, chinook.Id.Value).Accepted, $"{rate}: a grunt is ordered in.");
                for (var tick = 0; tick < world.Frames(300) && !grunts.All(grunt => grunt.ContainedBy == chinook.Id); tick++) world.Step();
                Check.That(grunts.All(grunt => grunt.ContainedBy == chinook.Id), $"{rate}: all three are aboard.");
                for (var tick = 0; tick < world.Frames(40) && chinook.Z != Fixed.FromInt(100); tick++) world.Step();
                Check.That(chinook.Z == Fixed.FromInt(100), $"{rate}: with nobody wanting in, the Chinook is back at its height.");
                while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
            }

            // An unload at a place: everybody out in the frame the Chinook is down, where it stands.
            Board();
            Check.That(feeder.Evacuate(1, chinook.Id.Value, x: 300, y: 150).Accepted, $"{rate}: an unload at (300, 150) is ordered.");
            var left = new long[grunts.Length];
            long down = 0;
            for (var tick = 0; tick < world.Frames(400) && grunts.Any(grunt => grunt.ContainedBy.IsValid); tick++)
            {
                world.Step();
                if (down == 0 && Ground()) down = world.Tick;
                for (var i = 0; i < grunts.Length; i++)
                    if (left[i] == 0 && !grunts[i].ContainedBy.IsValid)
                    {
                        left[i] = world.Tick;
                        Check.That(down > 0, $"{rate}: nobody leaves before the Chinook is down.");
                    }
            }
            Check.That(left.All(value => value > 0) && left.Distinct().Count() == 1, $"{rate}: all three leave in one step ({string.Join(", ", left)}).");
            Check.That(left[0] - down <= world.Frames(1), $"{rate}: they leave as soon as the Chinook is down (down {down}, out {left[0]}).");
            Check.That(chinook.X == Fixed.FromInt(300) && grunts.All(grunt => grunt.X == chinook.X && grunt.Y == chinook.Y), $"{rate}: they stand where the Chinook landed.");
            for (var tick = 0; tick < world.Frames(25); tick++) world.Step();
            Check.That(chinook.Z == Fixed.FromInt(100), $"{rate}: unloaded, the Chinook takes off again.");

            // A bare evacuation: rider after rider through the door, three frames apart; the Chinook stays down until the last is out.
            Board();
            Check.That(feeder.Evacuate(1, chinook.Id.Value).Accepted, $"{rate}: an evacuation is ordered in the air.");
            Array.Clear(left);
            down = 0;
            for (var tick = 0; tick < world.Frames(400) && grunts.Any(grunt => grunt.ContainedBy.IsValid); tick++)
            {
                world.Step();
                if (down == 0 && Ground()) down = world.Tick;
                if (grunts.Any(grunt => !grunt.ContainedBy.IsValid) && grunts.Any(grunt => grunt.ContainedBy.IsValid)) Check.That(Ground(), $"{rate}: the Chinook stays down while riders wait at the door.");
                for (var i = 0; i < grunts.Length; i++)
                    if (left[i] == 0 && !grunts[i].ContainedBy.IsValid)
                    {
                        left[i] = world.Tick;
                        Check.That(down > 0, $"{rate}: nobody leaves before the Chinook is down.");
                    }
            }
            var order = left.Order().ToArray();
            Check.That(order[0] > 0 && order[1] - order[0] == world.Frames(3) && order[2] - order[1] == world.Frames(3), $"{rate}: they leave three frames apart ({string.Join(", ", order)}).");
            for (var tick = 0; tick < world.Frames(25); tick++) world.Step();
            Check.That(chinook.Z == Fixed.FromInt(100), $"{rate}: with nobody at the door, the Chinook takes off again.");
        }
    }
}
