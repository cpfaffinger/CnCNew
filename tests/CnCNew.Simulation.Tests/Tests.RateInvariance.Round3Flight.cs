using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;

/// <summary>
/// Phase 23, third round, package flight (<c>docs/RATE-INVARIANCE-ROUND3-FLIGHT.md</c>): winged flight, the airfield states of a jet and a helicopter compute the step of a logic
/// frame of the original with the constants and the speed the 30 Hz step computes with (<c>LocomotorFrames</c>, the speed per frame kept by the AI), so the state after every
/// frame is the 30 Hz state bit for bit — not "within a tolerance". The tests name their rates and ignore <c>--tick-rate</c>.
/// </summary>
static partial class Tests
{
    /// <summary>What is compared after every logic frame of the original: raw position, height and heading, the speed per frame, the state of the airfield automaton, the clip.</summary>
    private readonly record struct FlightBits(long X, long Y, long Z, long Angle, long FrameSpeed, JetAIUpdate.JetPhase Phase, bool Airborne, bool Ordered, int Clip)
    {
        public override string ToString()
            => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"x {X} y {Y} z {Z} angle {Angle} speed {FrameSpeed} {Phase}{(Airborne ? "+" : "-")}{(Ordered ? " ordered" : string.Empty)} clip {Clip}");
    }

    private static FlightBits BitsOf(SimulationWorld world, Entity aircraft)
    {
        var ai = JetOf(aircraft);
        return new FlightBits(aircraft.X.Raw, aircraft.Y.Raw, aircraft.Z.Raw, aircraft.Angle.Raw, ai.SpeedPerOriginalFrame(world.Time.TicksPerOriginalFrame).Raw, ai.Phase, ai.AllowAirLoco,
            aircraft.HasMoveTarget, aircraft.WeaponClip);
    }

    /// <summary>
    /// Runs <paramref name="frames"/> logic frames of the original from a frame boundary. <paramref name="script"/> is called where each frame begins (the same game time at every
    /// rate) with the frame's number and the aircraft once it exists; the state of the aircraft is taken after the last tick of each frame.
    /// </summary>
    private static List<FlightBits?> BitTrack(SimulationWorld world, string template, int frames, System.Action<int, Entity?> script)
    {
        Check.That(world.Time.IsOriginalFrameBoundary(world.Tick), "The track starts where a frame begins.");
        var track = new List<FlightBits?>(frames);
        Entity? aircraft = null;
        for (var frame = 0; frame < frames; frame++)
        {
            script(frame, aircraft);
            for (var tick = 0; tick < world.Time.TicksPerOriginalFrame; tick++) world.Step();
            aircraft ??= world.Entities.SingleOrDefault(entity => entity.Template.Name == template);
            track.Add(aircraft is null ? null : BitsOf(world, aircraft));
        }
        return track;
    }

    /// <summary>The tracks of 60 and 120 Hz are the 30 Hz track, bit for bit; prints the phases of the 30 Hz track and the first frame that differs.</summary>
    private static void SameBits(string what, List<FlightBits?>[] tracks)
    {
        var phases = new List<string>();
        JetAIUpdate.JetPhase? last = null;
        var airborne = false;
        for (var frame = 0; frame < tracks[0].Count; frame++)
        {
            if (tracks[0][frame] is not { } bits || (bits.Phase == last && bits.Airborne == airborne)) continue;
            (last, airborne) = (bits.Phase, bits.Airborne);
            phases.Add($"{bits.Phase}{(bits.Airborne ? "+" : "-")}@{frame + 1}");
        }
        var different = new int[tracks.Length];
        for (var index = 1; index < tracks.Length; index++)
        {
            different[index] = -1;
            for (var frame = 0; frame < tracks[0].Count && different[index] < 0; frame++)
                if (tracks[index][frame] != tracks[0][frame]) different[index] = frame;
        }
        Console.WriteLine($"       {what}: {tracks[0].Count} frames, states at 30 Hz (+ airborne, @ frame) {string.Join(" ", phases)}; first frame that differs from 30 Hz: " +
                          $"{(different[1] < 0 ? "none" : (different[1] + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))} / {(different[2] < 0 ? "none" : (different[2] + 1).ToString(System.Globalization.CultureInfo.InvariantCulture))} (60 / 120 Hz)");
        for (var index = 1; index < tracks.Length; index++)
        {
            if (different[index] < 0) continue;
            var frame = different[index];
            Check.That(false, $"{what}: after frame {frame + 1} {FlightRates[index]} is [{tracks[index][frame]}], 30 Hz is [{tracks[0][frame]}].");
        }
    }

    /// <summary>
    /// A whole sortie of a jet, compared after every logic frame of the original: it is produced, rolls out of the hangar to its space, is sent away (taxi, pause, take-off run, climb,
    /// flight), turns back on a second order, is sent at a victim and empties its clip, returns by itself, circles, lands, rolls out, taxis to its space, turns and reloads.
    /// </summary>
    public static void RateFlightJetSortieIsBitIdentical()
    {
        var tracks = new List<FlightBits?>[FlightRates.Length];
        for (var index = 0; index < FlightRates.Length; index++)
        {
            var (world, feeder) = FlightRateWorld(FlightRates[index], WingAirfieldFiles, terrain: false);
            feeder.Spawn(1, "AirTestAirfield", owner: 1, x: 500, y: 500);
            feeder.Spawn(2, "AirDummy", owner: 2, x: 1400, y: 700);
            StepFrames(world, 1);
            feeder.GiveMoney(1, 1000);
            StepFrames(world, 1);
            var airfield = Only(world, "AirTestAirfield");
            var dummy = Only(world, "AirDummy");
            var shots = 0;
            var rate = FlightRates[index];
            tracks[index] = BitTrack(world, "AirTestJet", 1500, (frame, jet) =>
            {
                if (frame == 0) Check.That(feeder.QueueProduction(1, airfield.Id.Value, "AirTestJet").Accepted, "The jet is queued.");
                if (jet is null) return;
                if (frame == 300) Check.That(feeder.SetMoveTarget(1, jet.Id.Value, 1200, 300).Accepted, $"{rate}: the parked jet is sent away.");
                if (frame == 520) Check.That(feeder.SetMoveTarget(1, jet.Id.Value, 200, 900).Accepted, $"{rate}: the flying jet is turned back.");
                if (frame == 700) Check.That(feeder.Pursue(1, jet.Id.Value, dummy.Id.Value, 1400, 700).Accepted, $"{rate}: the jet is sent at its victim.");
                // The stream of fire orders a player's attack is, one where each frame begins.
                if (frame is >= 700 and < 900 && jet.WeaponClip > 0 && feeder.FireWeapon(1, jet.Id.Value, dummy.Id.Value).Accepted) shots++;
            });
        }
        SameBits("jet sortie", tracks);
        for (var index = 0; index < FlightRates.Length; index++)
        {
            var end = tracks[index][^1];
            Check.That(end is { Phase: JetAIUpdate.JetPhase.None, Airborne: false, Clip: 2 }, $"{FlightRates[index]}: the jet is parked and reloaded at the end ({end}).");
            Check.That(tracks[index].Any(bits => bits is { Clip: 0 }) && tracks[index].Any(bits => bits is { Phase: JetAIUpdate.JetPhase.Landing }), $"{FlightRates[index]}: the jet emptied its clip and landed.");
        }
    }

    /// <summary>
    /// A helicopter of a helipad, compared after every logic frame of the original: it is produced on the pad, rises, climbs to its height, flies to a point, turns back to another,
    /// hovers, goes home by itself and settles on the pad.
    /// </summary>
    public static void RateFlightHelicopterIsBitIdentical()
    {
        var tracks = new List<FlightBits?>[FlightRates.Length];
        for (var index = 0; index < FlightRates.Length; index++)
        {
            var (world, feeder) = FlightRateWorld(FlightRates[index], AirFixtureFiles, terrain: false);
            feeder.Spawn(1, "AirTestAirfield", owner: 1, x: 500, y: 500);
            StepFrames(world, 1);
            feeder.GiveMoney(1, 1000);
            StepFrames(world, 1);
            var airfield = Only(world, "AirTestAirfield");
            var rate = FlightRates[index];
            tracks[index] = BitTrack(world, "AirTestHeli", 900, (frame, heli) =>
            {
                if (frame == 0) Check.That(feeder.QueueProduction(1, airfield.Id.Value, "AirTestHeli").Accepted, "The helicopter is queued.");
                if (heli is null) return;
                if (frame == 150) Check.That(feeder.SetMoveTarget(1, heli.Id.Value, 903, 541).Accepted, $"{rate}: the helicopter is sent away.");
                if (frame == 230) Check.That(feeder.SetMoveTarget(1, heli.Id.Value, 377, 811).Accepted, $"{rate}: the helicopter is turned back in flight.");
            });
        }
        SameBits("helicopter", tracks);
        // A healed helicopter leaves its pad again by itself, so the end of the track is not "parked".
        for (var index = 0; index < FlightRates.Length; index++)
            Check.That(tracks[index].Any(bits => bits is { Airborne: true, Ordered: true }) && tracks[index].Any(bits => bits is { Phase: JetAIUpdate.JetPhase.ReloadAmmo, Airborne: false }),
                $"{FlightRates[index]}: the helicopter flew, landed and stood on its pad.");
    }
}
