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


        harness.Run("Model geometry: the exit bones of the door containers (OpenContain::exitObjectViaDoor)", () =>
        {
            var exits = data.Templates.Geometry.ExitPaths;
            var paths = exits.Values.Sum(value => value.Length);
            var complete = exits.Values.Sum(value => value.Count(path => path.Start is not null && path.End is not null));
            var none = exits.Where(pair => pair.Value.All(path => path.Start is null && path.End is null)).Select(pair => pair.Key).Order(StringComparer.Ordinal).ToArray();
            Console.WriteLine($"       exit paths: {exits.Count} containers, {paths} paths, {complete} with both bones; {none.Length} containers without any exit bone ({string.Join(", ", none.Take(12))}{(none.Length > 12 ? ", ..." : "")})");
            var humvee = exits["AmericaVehicleHumvee"];
            Console.WriteLine($"       AmericaVehicleHumvee: {string.Join(" | ", humvee.Select(path => $"{path.Start} -> {path.End}"))}");
            TestHarness.Assert(humvee.Length == 3 && humvee.All(path => path.Start is not null && path.End is not null), $"the Humvee has NumberOfExitPaths = 3 and the model AVHUMMER has ExitStart01..03 and ExitEnd01..03 ({humvee.Length})");
            // AVHUMMER.W3D (tools/CnCNew.W3dTool bones): EXITSTART01 (-5.719, -0.006), EXITEND01 (-15.950, 0.001), EXITEND02 (-1.658, 10.989).
            static bool Near(Fixed value, decimal expected) => Math.Abs(value.ToDecimal() - expected) <= 0.07m;
            TestHarness.Assert(Near(humvee[0].Start!.Value.X, -5.719m) && Near(humvee[0].End!.Value.X, -15.950m) && Near(humvee[1].End!.Value.Y, 10.989m), "the Humvee's exit bones are the model's");
            TestHarness.Assert(exits.Count == 100 && paths == 166 && complete == 82 && none.Length == 76, $"measured on the master installation: 100 door containers, 166 paths, 82 with both bones, 76 containers without any ({exits.Count}, {paths}, {complete}, {none.Length})");
        });

        harness.Run("Model geometry: the fire points and stations of the garrisons and transports (loadGarrisonPoints, loadStationGarrisonPoints, putObjAtNextFirePoint)", () =>
        {
            var points = data.Templates.Geometry.FirePoints;
            var garrisons = points.Where(pair => pair.Value.Garrison.Any(set => !set.IsEmpty)).ToArray();
            var transports = points.Where(pair => !pair.Value.Transport.IsEmpty).ToArray();
            var stations = points.Where(pair => !pair.Value.Stations.IsEmpty).ToArray();
            var pristine = garrisons.Sum(pair => pair.Value.Garrison[0].Length);
            Console.WriteLine($"       fire points: {garrisons.Length} garrisons ({pristine} pristine points, at most {garrisons.Max(pair => pair.Value.Garrison.Max(set => set.Length))}), "
                + $"{stations.Length} fire bases ({stations.Sum(pair => pair.Value.Stations.Length)} stations), {transports.Length} transports ({transports.Sum(pair => pair.Value.Transport.Length)} points)");
            Console.WriteLine($"       transports: {string.Join(", ", transports.Select(pair => $"{pair.Key} {pair.Value.Transport.Length}").Take(16))}");
            // ABFIREBASE.W3D (tools/CnCNew.W3dTool bones): STATION01 (17.205, 16.507, -7.176), STATION03 (-17.357, -17.144, -7.176) - the bunker is sunk, its riders stand below its origin.
            static bool Near(Fixed value, decimal expected) => Math.Abs(value.ToDecimal() - expected) <= 0.07m;
            var fireBase = points["AmericaFireBase"].Stations;
            TestHarness.Assert(fireBase.Length == 4 && Near(fireBase[0].X, 17.205m) && Near(fireBase[0].Y, 16.507m) && Near(fireBase[0].Z, -7.176m) && Near(fireBase[2].X, -17.357m) && Near(fireBase[2].Y, -17.144m),
                $"the fire base has the four stations its model makes public (ExtraPublicBone STATION01..04), as the model has them: {string.Join(" | ", fireBase)}");
            TestHarness.Assert(garrisons.Length == 164 && pristine == 3288 && stations.Length == 4 && transports.Length == 55 && transports.Sum(pair => pair.Value.Transport.Length) == 301,
                $"measured on the master installation: 164 garrisons with 3,288 pristine points, 4 fire bases, 55 transports with 301 points ({garrisons.Length}, {pristine}, {stations.Length}, {transports.Length})");
            var turret = transports.Where(pair => pair.Value.TurretPivot is not null).Select(pair => pair.Key).ToArray();
            Console.WriteLine($"       turret fire points (PassengersInTurret): {string.Join(", ", turret)}; ChinaTankOverlord {points["ChinaTankOverlord"].Transport[0]} about {points["ChinaTankOverlord"].TurretPivot}");
            // NVOVRLRD.W3D (tools/CnCNew.W3dTool bones): TURRET01 (0, 0, 10.192), FIREPOINT01 (-15, 0, 15).
            TestHarness.Assert(points["ChinaTankOverlord"].TurretPivot is { } pivot && Near(pivot.Z, 10.192m) && Near(pivot.X, 0m) && Near(points["ChinaTankOverlord"].Transport[0].X, -15m),
                "the Overlord's portable structure stands on its turret's fire point, turned about the turret bone");
            TestHarness.Assert(turret.All(name => name.Contains("Overlord", StringComparison.Ordinal) || name.Contains("Emperor", StringComparison.Ordinal)) && turret.Length == 5,
                $"only the Overlords and the Tank General's Emperor carry their riders in the turret ({turret.Length})");
            TestHarness.Assert(garrisons.All(pair => pair.Value.Garrison.All(set => set.Length <= ContainerFirePoints.MaxGarrisonPoints))
                && transports.All(pair => pair.Value.Transport.Length <= ContainerFirePoints.MaxTransportPoints), "no set is longer than the original's arrays");
        });
        return harness.Failures;
    }
}
