using CnCNew.Game.Tests;

// Each suite exposes `static int Run(string[] args)` returning its failure count; one registration per line.
// `--only <Suite>[,<Suite>...]` (or `--suite`) runs just those suites (the whole program takes many minutes on real data).
(string Name, Func<string[], int> Run)[] suites =
[
    (nameof(LiveMatchTests), LiveMatchTests.Run),
    (nameof(AudioDirectorTests), AudioDirectorTests.Run),
    (nameof(ShellTests), ShellTests.Run),
    (nameof(ShellTransitionTests), ShellTransitionTests.Run),
    (nameof(HudTests), HudTests.Run),
    (nameof(CampaignTests), CampaignTests.Run),
    (nameof(CampaignShellTests), CampaignShellTests.Run),
    (nameof(MissionPresentationTests), MissionPresentationTests.Run),
    (nameof(MissionHudTests), MissionHudTests.Run),
    (nameof(MissionEffectsTests), MissionEffectsTests.Run),
    (nameof(ScenarioTests), ScenarioTests.Run),
    (nameof(ModelGeometryTests), ModelGeometryTests.Run),
];

// --suite is accepted as another spelling of --only.
var onlyIndex = Array.IndexOf(args, "--only") is var onlyAt and >= 0 ? onlyAt : Array.IndexOf(args, "--suite");
var only = onlyIndex >= 0 && onlyIndex + 1 < args.Length ? args[onlyIndex + 1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : null;
if (only is not null && only.Except(suites.Select(suite => suite.Name), StringComparer.OrdinalIgnoreCase).ToArray() is { Length: > 0 } unknown)
{
    Console.WriteLine($"Unknown suite(s) {string.Join(", ", unknown)}. Known: {string.Join(", ", suites.Select(suite => suite.Name))}.");
    return 2;
}

var failures = 0;
var skipped = 0;
foreach (var suite in suites)
{
    if (only is not null && !only.Contains(suite.Name, StringComparer.OrdinalIgnoreCase))
    {
        skipped++;
        continue;
    }
    failures += suite.Run(args);
}
if (skipped > 0) Console.WriteLine($"{skipped} suite(s) not run (--only).");
Console.WriteLine(failures == 0 ? (skipped == 0 ? "All game tests passed." : "The selected game tests passed.") : $"{failures} game test(s) failed.");
return failures == 0 ? 0 : 1;
