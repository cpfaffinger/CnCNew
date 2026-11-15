using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.World;

/// <summary>
/// #305: a unit that walks to the target of its staged ability stops when the target is within <c>StartAbilityRange</c> (<c>SpecialAbilityUpdate::isWithinStartAbilityRange</c>).
/// At 30 Hz the ability's update judges the range after the unit's whole step of the frame and stops it there. Above 30 Hz it judged the range in the frame's first tick from the
/// position after the first share, and stopped the unit where it was, a share short (Alpine Assault America against China, a Red Guard at frame 13,707).
/// </summary>
static partial class Tests
{
    /// <summary>
    /// A capturer is ordered to capture a building a few units further away each time; at 30, 60 and 120 Hz it stands at the end of every frame where the 30 Hz capturer stands,
    /// until the building is captured.
    /// </summary>
    public static void AStagedAbilityStopsWhereThe30HzUnitStops()
    {
        var reference = new Dictionary<int, List<(Fixed X, Fixed Y)>>();
        foreach (var (rate, _) in WorldRates)
            for (var stationX = 200; stationX <= 203; stationX++)
            {
                var (world, feeder, _) = RatedWorld(rate, StagedCaptureFiles());
                feeder.Spawn(1, "StagedCapturer", owner: 1, x: 100, y: 50);
                feeder.Spawn(2, "PlainStation", owner: 2, x: stationX, y: 53);
                feeder.SetRelationship(1, 2, Relationship.Enemies);
                world.Step();
                world.CompleteUpgrade(new PlayerId(1), null, "Upgrade_StagedCapture");
                while (world.Tick < world.Time.OriginalFrames(40)) world.Step();
                var capturer = world.Entities.Single(value => value.Template.Name == "StagedCapturer");
                var station = world.Entities.Single(value => value.Template.Name == "PlainStation");
                Check.That(feeder.Capture(1, capturer.Id.Value, station.Id.Value).Accepted, $"{rate}: the capture is accepted.");
                var positions = new List<(Fixed X, Fixed Y)>();
                while (world.Tick < world.Time.OriginalFrames(240))
                {
                    world.Step();
                    if (world.Time.IsOriginalFrameBoundary(world.Tick)) positions.Add((capturer.X, capturer.Y));
                }
                Check.That(station.Owner == new PlayerId(1), $"{rate}, building at {stationX}: the building is captured.");
                if (!reference.TryGetValue(stationX, out var expected))
                {
                    reference[stationX] = positions;
                    continue;
                }
                var first = Enumerable.Range(0, positions.Count).FirstOrDefault(index => positions[index] != expected[index], -1);
                Check.That(first < 0,
                    $"{rate}, building at {stationX}: the capturer stands where the 30 Hz capturer stands at the end of every frame (first difference after frame {(first < 0 ? -1 : 41 + first)}: " +
                    $"{(first < 0 ? "-" : $"{positions[first].X.ToDecimal():0.000} against {expected[first].X.ToDecimal():0.000}")}).");
            }
    }

    /// <summary>
    /// Two capturers are ordered at one building; the second still walks when the first captures it, and its ability ends (the target is its own player's now). At 30, 60 and
    /// 120 Hz the second stands where the 30 Hz one stands at the end of every frame (Alpine Assault America against China, a Red Guard at frame 14,397).
    /// </summary>
    public static void AStagedAbilityThatEndsOnTheWayStopsWhereThe30HzUnitStops()
    {
        var reference = new Dictionary<int, List<(Fixed X, Fixed Y)>>();
        foreach (var (rate, _) in WorldRates)
            for (var behind = 0; behind <= 3; behind++)
            {
                var (world, feeder, _) = RatedWorld(rate, StagedCaptureFiles());
                feeder.Spawn(1, "StagedCapturer", owner: 1, x: 500, y: 50);
                feeder.Spawn(1, "StagedCapturer", owner: 1, x: 100 + behind, y: 51);
                feeder.Spawn(2, "PlainStation", owner: 2, x: 600, y: 53);
                feeder.SetRelationship(1, 2, Relationship.Enemies);
                world.Step();
                world.CompleteUpgrade(new PlayerId(1), null, "Upgrade_StagedCapture");
                while (world.Tick < world.Time.OriginalFrames(40)) world.Step();
                var capturers = world.Entities.Where(value => value.Template.Name == "StagedCapturer").OrderBy(value => value.Id.Value).ToArray();
                var station = world.Entities.Single(value => value.Template.Name == "PlainStation");
                foreach (var capturer in capturers) Check.That(feeder.Capture(1, capturer.Id.Value, station.Id.Value).Accepted, $"{rate}: the capture is accepted.");
                var late = capturers[1];
                var positions = new List<(Fixed X, Fixed Y)>();
                var capturedFrame = -1L;
                while (world.Tick < world.Time.OriginalFrames(240))
                {
                    world.Step();
                    if (capturedFrame < 0 && station.Owner == new PlayerId(1)) capturedFrame = world.Time.ToOriginalFrames(world.Tick - 1);
                    if (world.Time.IsOriginalFrameBoundary(world.Tick)) positions.Add((late.X, late.Y));
                }
                Check.That(capturedFrame > 0 && late.X < Fixed.FromInt(450) && !late.HasMoveTarget,
                    $"{rate}, {behind} behind: the first capturer takes the building (frame {capturedFrame}) while the second walks, and the second stops (at {late.X.ToDecimal():0.000}).");
                if (!reference.TryGetValue(behind, out var expected))
                {
                    reference[behind] = positions;
                    continue;
                }
                var first = Enumerable.Range(0, positions.Count).FirstOrDefault(index => positions[index] != expected[index], -1);
                Check.That(first < 0,
                    $"{rate}, {behind} behind: the second capturer stands where the 30 Hz one stands at the end of every frame (first difference after frame {(first < 0 ? -1 : 41 + first)}: " +
                    $"{(first < 0 ? "-" : $"{positions[first].X.ToDecimal():0.000} against {expected[first].X.ToDecimal():0.000}")}).");
            }
    }
}
