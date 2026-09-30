using CnCNew.Presentation.Tests;

// Each test class exposes `static int Run(string[] args)` returning its failure count.
// Register one class per line.
Func<string[], int>[] suites =
[
];

var failures = 0;
foreach (var suite in suites)
{
    failures += suite(args);
}

Console.WriteLine(failures == 0 ? "All presentation tests passed." : $"{failures} presentation test(s) failed.");
return failures > 0 ? 1 : 0;
