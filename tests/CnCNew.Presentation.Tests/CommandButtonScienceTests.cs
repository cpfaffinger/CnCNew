using CnCNew.Presentation.Input;
using CnCNew.Simulation.Testing;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// #469: a command button's <c>Science</c> is <c>INI::parseScienceVector</c> - every token a science, and a <c>None</c> anywhere clears the list. The button reads the
/// typed list of the compiler, not its own split of the text (which kept the sciences around a <c>None</c>).
/// </summary>
internal static class CommandButtonScienceTests
{
    private static IReadOnlyDictionary<string, string> Files() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Science.ini"] = """
        Science SCIENCE_FixtureOne
        End
        Science SCIENCE_FixtureTwo
        End
        """,
        ["Data/INI/CommandButton.ini"] = """
        CommandButton Command_FixtureBoth
          Command = PURCHASE_SCIENCE
          Science = SCIENCE_FixtureOne SCIENCE_FixtureTwo
        End
        CommandButton Command_FixtureCleared
          Command = PURCHASE_SCIENCE
          Science = SCIENCE_FixtureOne NONE SCIENCE_FixtureTwo
        End
        """,
    };

    public static int Run(string[] args)
    {
        var t = new TestHarness("CommandButtonScience");
        t.Run("a command button's Science is the compiler's list, and a None clears it (parseScienceVector)", ScienceList);
        return t.Failures;
    }

    private static void ScienceList()
    {
        var buttons = CommandButtonCatalog.Build(new SyntheticFixture(CnCNew.Tests.TestRate.Default, files: Files()).Graph);
        Assert(buttons.TryGetButton("Command_FixtureBoth", out var both) && both.Sciences.SequenceEqual(["SCIENCE_FixtureOne", "SCIENCE_FixtureTwo"]), "both sciences, in file order");
        Assert(buttons.TryGetButton("Command_FixtureCleared", out var cleared) && cleared.Sciences.IsEmpty, $"a None clears the list ({string.Join(" ", cleared?.Sciences ?? [])})");
    }
}
