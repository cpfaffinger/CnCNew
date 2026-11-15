using System.Collections.Generic;
using System.Linq;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;

/// <summary>
/// #305: <c>PartitionManager::findPositionAround</c> tests a place against what overlaps it. Above 30 Hz a box that turns in a frame stood, for the search in the frame's first tick,
/// where its frame ends but with the heading it had halfway through the turn (Alpine Assault America against China: a supply truck's approach place at frame 17,803, decided past a
/// second truck that turned beside the dock).
/// </summary>
static partial class Tests
{
    private static IReadOnlyDictionary<string, string> PositionAroundTurnFiles() => new Dictionary<string, string>(NavigationFiles())
    {
        ["Data/INI/Object/PositionAroundTurnFixture.ini"] = """
        Object LongBox
          KindOf = SELECTABLE VEHICLE
          Geometry = BOX
          GeometryMajorRadius = 25.0
          GeometryMinorRadius = 4.0
          GeometryHeight = 10.0
          Locomotor = SET_NORMAL GroundLoco
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
        End
        """,
    };

    /// <summary>
    /// A long box turns toward a goal beside it while a place is looked for next to its tip in the first tick of every frame: the place is the one the 30 Hz search finds, at 30, 60
    /// and 120 Hz.
    /// </summary>
    public static void APlaceAroundATurningBoxIsTheSameAtEveryRate()
    {
        List<(Fixed X, Fixed Y)>? reference = null;
        foreach (var (rate, _) in WorldRates)
        {
            var (world, feeder, _) = RatedWorld(rate, PositionAroundTurnFiles(), NavTerrain());
            feeder.Spawn(1, "LongBox", owner: 1, x: 100, y: 100);
            feeder.Spawn(1, "Walker", owner: 1, x: 30, y: 30);
            world.Step();
            var box = world.Entities.Single(value => value.Template.Name == "LongBox");
            var walker = world.Entities.Single(value => value.Template.Name == "Walker");
            while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
            Check.That(feeder.SetMoveTarget(1, box.Id.Value, 100, 170).Accepted, $"{rate}: the box is ordered to a goal beside it.");
            var places = new List<(Fixed X, Fixed Y)>();
            var turned = false;
            for (var frame = 0; frame < 40; frame++)
            {
                // The frame's first tick, then the search, then the rest of the frame.
                world.Step();
                turned |= box.Angle != Fixed.Zero;
                world.FindPositionAround(Fixed.FromInt(122), Fixed.FromInt(103), Fixed.Zero, Fixed.FromInt(40), walker, null, out var x, out var y);
                places.Add((x, y));
                while (!world.Time.IsOriginalFrameBoundary(world.Tick)) world.Step();
            }
            Check.That(turned, $"{rate}: the box turns.");
            if (reference is null)
            {
                reference = places;
                Check.That(places.Distinct().Count() > 1, "30 Hz: the place moves as the box turns.");
                continue;
            }
            var first = Enumerable.Range(0, places.Count).FirstOrDefault(index => places[index] != reference[index], -1);
            Check.That(first < 0, $"{rate}: the place is the 30 Hz one in every frame (first difference in frame {first}: " +
                $"{(first < 0 ? "-" : $"{places[first].X.ToDecimal():0.000},{places[first].Y.ToDecimal():0.000} against {reference[first].X.ToDecimal():0.000},{reference[first].Y.ToDecimal():0.000}")}).");
        }
    }
}
