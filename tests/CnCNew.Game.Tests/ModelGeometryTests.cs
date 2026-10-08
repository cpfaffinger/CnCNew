using CnCNew.Data.Snapshots;
using CnCNew.Game.Data;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;

namespace CnCNew.Game.Tests;

/// <summary>The airfield bones the simulation gets from the draw models (<see cref="ModelGeometryLoader"/>): complete for all nine retail airfields, reproducible, part of the world fingerprint.</summary>
internal static class ModelGeometryTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(ModelGeometryTests));
        var index = Array.IndexOf(args, "--content-dir");
        if (index < 0 || index + 1 >= args.Length)
        {
            Console.WriteLine("SKIP ModelGeometryTests (pass --content-dir <dir> to run against real data)");
            return 0;
        }

        var data = GameData.Load(args[index + 1], rate: CnCNew.Tests.TestRate.Default);
        harness.Run("Model geometry: all nine retail airfields have their bones, in the catalog and in the world fingerprint", () =>
        {
            var geometry = data.Templates.Geometry;
            var names = geometry.Airfields.Keys.Order(StringComparer.Ordinal).ToArray();
            Console.WriteLine($"       model geometry: {names.Length} airfields ({string.Join(", ", names)}), fingerprint {geometry.Fingerprint[..16]}");
            TestHarness.Assert(names.Length == 9, $"nine airfields with a ParkingPlaceBehavior ({names.Length})");
            foreach (var name in names)
            {
                var layout = geometry.Airfields[name];
                TestHarness.Assert(layout is { Rows: 2, Cols: 2 } && layout.Parking.Length == 4 && layout.Hangar.Length == 4 && layout.Prep.Length == 4 && layout.RunwayStart.Length == 2 && layout.RunwayEnd.Length == 2, $"{name}: 2 x 2 spaces and two runways");
                for (var runway = 0; runway < 2; runway++)
                {
                    var (start, end) = (layout.RunwayStart[runway], layout.RunwayEnd[runway]);
                    var length = Math.Sqrt(Math.Pow((double)(end.X - start.X).ToDecimal(), 2) + Math.Pow((double)(end.Y - start.Y).ToDecimal(), 2));
                    TestHarness.Assert(length > 50 && length < 400, $"{name}: runway {runway + 1} is a runway ({length:F1} long)");
                }

                foreach (var pose in layout.Parking.Concat(layout.Hangar).Concat(layout.Prep).Concat(layout.RunwayStart).Concat(layout.RunwayEnd))
                    TestHarness.Assert(pose.X.Abs() < Fixed.FromInt(300) && pose.Y.Abs() < Fixed.FromInt(300), $"{name}: every bone lies near the building");
            }

            var graphFingerprint = DefinitionSnapshotWriter.ComputeFingerprint(data.Graph).Sha256;
            TestHarness.Assert(data.Fingerprint != graphFingerprint && data.Fingerprint == data.Rate.Combine(geometry.Combine(graphFingerprint)), "the world fingerprint folds the table in (and the tick rate, when it is not the original's)");
        });

        harness.Run("Model geometry: the American airfield's bones are the model's, read twice give the same table", () =>
        {
            var layout = data.Templates.Geometry.Airfields["AmericaAirfield"];
            // ABArFrcCmd.W3D (tools/CnCNew.W3dTool bones): Runway1Parking1 (-21.059, 22.652) heading -90, RunwayStart1 (30.825, -49.047), RunwayEnd1 (110.055, -49.047), Runway1Park1Han (-21.059, 51.855)
            static bool Near(Fixed value, decimal expected, decimal tolerance) => Math.Abs(value.ToDecimal() - expected) <= tolerance;
            TestHarness.Assert(Near(layout.Parking[0].X, -21.059m, 0.07m) && Near(layout.Parking[0].Y, 22.652m, 0.07m) && Near(layout.Parking[0].Heading, -1.5708m, 0.002m), $"Runway1Parking1: {layout.Parking[0]}");
            TestHarness.Assert(Near(layout.Hangar[0].X, -21.059m, 0.07m) && Near(layout.Hangar[0].Y, 51.855m, 0.07m), $"Runway1Park1Han: {layout.Hangar[0]}");
            TestHarness.Assert(Near(layout.RunwayStart[0].X, 30.825m, 0.07m) && Near(layout.RunwayStart[0].Y, -49.047m, 0.07m), $"RunwayStart1: {layout.RunwayStart[0]}");
            TestHarness.Assert(Near(layout.RunwayEnd[0].X, 110.055m, 0.07m) && Near(layout.RunwayEnd[0].Y, -49.047m, 0.07m), $"RunwayEnd1: {layout.RunwayEnd[0]}");
            TestHarness.Assert(layout.HeliPark is { } pad && Near(pad.X, 66.675m, 0.07m) && Near(pad.Y, 41.576m, 0.07m), $"HeliPark01: {layout.HeliPark}");
            var again = ModelGeometryLoader.Load(data.Graph, data.Visuals, data.Content);
            TestHarness.Assert(again.Fingerprint == data.Templates.Geometry.Fingerprint, "a second read gives the byte-identical table");
        });

        return harness.Failures;
    }
}
