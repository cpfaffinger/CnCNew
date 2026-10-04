using CnCNew.Game.Tests;

// Each suite exposes `static int Run(string[] args)` returning its failure count; one registration per line.
Func<string[], int>[] suites =
[
    LiveMatchTests.Run,
    AudioDirectorTests.Run,
    ShellTests.Run,
    HudTests.Run,
    CampaignTests.Run,
    CampaignShellTests.Run,
    MissionPresentationTests.Run,
    MissionHudTests.Run,
    ScenarioTests.Run,
    ModelGeometryTests.Run,
];

// --suite NAME runs one suite (for example --suite MissionHudTests); the whole run with real content takes many minutes.
var only = Array.IndexOf(args, "--suite") is var at and >= 0 && at + 1 < args.Length ? args[at + 1] : null;
if (only is not null && !suites.Any(suite => suite.Method.DeclaringType!.Name.Equals(only, StringComparison.OrdinalIgnoreCase)))
{
    Console.WriteLine($"Unknown suite '{only}'. Known: {string.Join(", ", suites.Select(suite => suite.Method.DeclaringType!.Name))}.");
    return 2;
}

var failures = 0;
foreach (var suite in suites)
    if (only is null || suite.Method.DeclaringType!.Name.Equals(only, StringComparison.OrdinalIgnoreCase)) failures += suite(args);
Console.WriteLine(failures == 0 ? "All game tests passed." : $"{failures} game test(s) failed.");
return failures == 0 ? 0 : 1;
