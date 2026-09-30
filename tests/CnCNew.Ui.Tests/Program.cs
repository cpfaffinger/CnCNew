using CnCNew.Ui.Tests;

// One line per test class; each returns its failure count.
Func<string[], int>[] suites =
[
    TextAndSettingsTests.Run,
    WndTests.Run,
    UiRenderingTests.Run,
];

var failures = 0;
foreach (var suite in suites)
    failures += suite(args);

Console.WriteLine(failures == 0 ? "All UI tests passed." : $"{failures} UI test(s) failed.");
return failures == 0 ? 0 : 1;
