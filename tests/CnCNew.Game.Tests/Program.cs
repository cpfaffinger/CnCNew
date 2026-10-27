using CnCNew.Game.Tests;

// "--tick-rate 30|60|120" (or CNCNEW_TEST_TICK_RATE) builds the default test worlds and game data at that rate: a probe, see docs/rate-invariance/RATE-SWITCH-OUTER-TESTS.md.
CnCNew.Tests.TestRateOption.Announce("Tests about one rate (time scale, savegame rate) name theirs; the golden hashes hold at every rate.");

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
    (nameof(MissionTextTests), MissionTextTests.Run),
    (nameof(ScenarioTests), ScenarioTests.Run),
    (nameof(HotkeyTests), HotkeyTests.Run),
    (nameof(TimeScaleTests), TimeScaleTests.Run),
    (nameof(SourceRuleTests), SourceRuleTests.Run),
    (nameof(ModelGeometryTests), ModelGeometryTests.Run),
    (nameof(DeterminismTests), DeterminismTests.Run),
    (nameof(SpectatorTests), SpectatorTests.Run),
    (nameof(NetLobbyTests), NetLobbyTests.Run),
    (nameof(NetShellTests), NetShellTests.Run),
    (nameof(NetScreenTests), NetScreenTests.Run),
    (nameof(NetHudTests), NetHudTests.Run),
    (nameof(ReplayBrowserTests), ReplayBrowserTests.Run),
    (nameof(MatchAnalysisTests), MatchAnalysisTests.Run),
    (nameof(ObserverTests), ObserverTests.Run),
    (nameof(SkinTests), SkinTests.Run),
    (nameof(AttachedEffectsTests), AttachedEffectsTests.Run),
    (nameof(CampaignPinTests), CampaignPinTests.Run),
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
