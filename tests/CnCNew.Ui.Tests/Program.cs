using CnCNew.Ui.Tests;

// "--tick-rate 30|60|120" (or CNCNEW_TEST_TICK_RATE) is accepted and reported like in the other test programs. No UI test builds a world or a catalog:
// windows, transitions and text run on original frames of wall time, so the option selects nothing here (docs/RATE-SWITCH-OUTER-TESTS.md).
CnCNew.Tests.TestRateOption.Announce("No UI test builds a world: the option selects nothing in this program.");

// One line per test class; each returns its failure count.
Func<string[], int>[] suites =
[
    TextAndSettingsTests.Run,
    WndTests.Run,
    TransitionTests.Run,
    UiRenderingTests.Run,
    SentenceLayoutTests.Run,
];

var failures = 0;
foreach (var suite in suites)
    failures += suite(args);

Console.WriteLine(failures == 0 ? "All UI tests passed." : $"{failures} UI test(s) failed.");
return failures == 0 ? 0 : 1;
