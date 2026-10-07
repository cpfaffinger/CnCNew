using CnCNew.Presentation.Tests;

// "--tick-rate 30|60|120" (or CNCNEW_TEST_TICK_RATE) builds the default test worlds at that rate: a probe, see docs/RATE-SWITCH-OUTER-TESTS.md.
CnCNew.Tests.TestRateOption.Announce("The time-scale tests name their rates themselves.");

// Each test class exposes `static int Run(string[] args)` returning its failure count.
// Register one class per line.
Func<string[], int>[] suites =
[
    ObjectTests.Run,
    FxTests.Run,
    TerrainTests.Run,
    RoadTests.Run,
    CameraTests.Run,
    VisionTests.Run,
    MinimapTests.Run,
    WorldRealDataTests.Run,
    RoadRealDataTests.Run,
    InputTests.Run,
    CursorTests.Run,
    UnitConditionTests.Run,
    StructureConditionTests.Run,
    CaptureConditionTests.Run,
    CaptureFlashTests.Run,
    JetOrderTests.Run,
    PlacementPreviewTests.Run,
    SentinelRangeTests.Run,
];

var failures = 0;
foreach (var suite in suites)
{
    failures += suite(args);
}

Console.WriteLine(failures == 0 ? "All presentation tests passed." : $"{failures} presentation test(s) failed.");
return failures > 0 ? 1 : 0;
