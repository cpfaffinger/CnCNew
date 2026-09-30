var suites = new List<Func<string[], int>>
{
    ObjectTests.Run,
};

var failures = 0;
foreach (var suite in suites) failures += suite(args);
return failures > 0 ? 1 : 0;
