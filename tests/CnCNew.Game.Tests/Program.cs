using CnCNew.Game.Tests;

// Each suite exposes `static int Run(string[] args)` returning its failure count; one registration per line.
Func<string[], int>[] suites =
[
    LiveMatchTests.Run,
    AudioDirectorTests.Run,
    ShellTests.Run,
    HudTests.Run,
];

var failures = 0;
foreach (var suite in suites) failures += suite(args);
Console.WriteLine(failures == 0 ? "All game tests passed." : $"{failures} game test(s) failed.");
return failures == 0 ? 0 : 1;
