var tests = new List<Func<string[], int>>
{
    InputTests.Run,
};

var failures = 0;
foreach (var test in tests) failures += test(args);
if (failures > 0)
{
    Console.Error.WriteLine($"{failures} presentation test(s) failed.");
    return 1;
}
Console.WriteLine("All presentation tests passed.");
return 0;
