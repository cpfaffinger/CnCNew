using CnCNew.Presentation.Tests;

// "--tick-rate 30|60|120" (or CNCNEW_TEST_TICK_RATE) builds the default test worlds at that rate: a probe, see docs/rate-invariance/RATE-SWITCH-OUTER-TESTS.md.
CnCNew.Tests.TestRateOption.Announce("The time-scale tests name their rates themselves.");

// Each test class exposes `static int Run(string[] args)` returning its failure count.
// Register one class per line.
Func<string[], int>[] suites =
[
    UpgradeVisualTests.Run,
    ObjectTests.Run,
    FxTests.Run,
    TerrainTests.Run,
    RoadTests.Run,
    BridgeMeshTests.Run,
    ShadowTests.Run,
    CameraTests.Run,
    ScriptedCameraTests.Run,
    VisionTests.Run,
    MinimapTests.Run,
    FogDimmingTests.Run,
    DependencyPlacementTests.Run,
    TreadMarksTests.Run,
    WorldRealDataTests.Run,
    RoadRealDataTests.Run,
    InputTests.Run,
    CommandPathTests.Run,
    CursorTests.Run,
    UnitConditionTests.Run,
    StructureConditionTests.Run,
    CaptureConditionTests.Run,
    StealthGarrisonConditionTests.Run,
    GarrisonGunTests.Run,
    DisguiseDrawTests.Run,
    ShockConditionTests.Run,
    CaptureFlashTests.Run,
    AnimationPauseTests.Run,
    JetOrderTests.Run,
    PlacementPreviewTests.Run,
    SentinelRangeTests.Run,
    SpectatorTests.Run,
    ChinookNetAnimationTests.Run,
];

// "--only Suite[,Suite]" runs the named suites (the name of the class that holds `Run`), for a focused check while working.
var only = args.SkipWhile(argument => argument != "--only").Skip(1).FirstOrDefault()?.Split(',');
var failures = 0;
foreach (var suite in suites)
{
    if (only is not null && !only.Contains(suite.Method.DeclaringType?.Name, StringComparer.OrdinalIgnoreCase)) continue;
    failures += suite(args);
}

Console.WriteLine(failures == 0 ? "All presentation tests passed." : $"{failures} presentation test(s) failed.");
return failures > 0 ? 1 : 0;
