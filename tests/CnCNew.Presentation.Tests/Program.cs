using CnCNew.Presentation.Tests;

var suites = new List<Func<string[], int>>
{
    FxTests.Run,
};

var failed = 0;
foreach (var suite in suites) failed += suite(args);
return failed == 0 ? 0 : 1;
