using CnCNew.Ui.Tests;

Func<string[], int>[] suites =
[
    WndTests.Run,
];

var failures = 0;
foreach (var suite in suites) failures += suite(args);
if (failures != 0)
{
    Console.Error.WriteLine($"{failures} UI test(s) failed.");
    return 1;
}
Console.WriteLine("All UI tests passed.");
return 0;
