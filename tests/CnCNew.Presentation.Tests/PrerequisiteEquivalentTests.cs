using System.Collections.Immutable;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// #468: the command bar asks the world's prerequisite rule. <c>Player::countObjectsByThingTemplate</c> compares by <c>ThingTemplate::isEquivalentTo</c>, so a reskin of the
/// named building meets the line; before, the bar compared names and greyed out a button the world would build.
/// </summary>
internal static class PrerequisiteEquivalentTests
{
    private static readonly ImmutableArray<PlayerConfiguration> Players = [new(new PlayerId(1), "Alpha")];

    private static IReadOnlyDictionary<string, string> Files() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/PrerequisiteEquivalentFixture.ini"] = """
        Object Reactor
          KindOf = STRUCTURE SELECTABLE
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
        End
        ObjectReskin ReactorReskin Reactor
        End
        Object Laboratory
          KindOf = STRUCTURE SELECTABLE
          BuildCost = 50
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Prerequisites
            Object = Reactor
          End
        End
        """,
    };

    public static int Run(string[] args)
    {
        var t = new TestHarness("PrerequisiteEquivalent");
        t.Run("the command bar's prerequisite check is the world's: a reskin of the named building meets the line", ReskinMeetsTheLine);
        return t.Failures;
    }

    private static void ReskinMeetsTheLine()
    {
        var fixture = new SyntheticFixture(CnCNew.Tests.TestRate.Default, files: Files());
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), fixture.Templates.Rate, 1, Players, SyntheticFixture.FixtureId, fixture.Fingerprint, ""), fixture.Templates);
        Assert(world.Templates.TryGet("Laboratory", out var laboratory), "the laboratory exists");
        var me = new PlayerId(1);
        Assert(!SimulationQueries.PrerequisitesSatisfied(world, me, laboratory) && SimulationQueries.MissingPrerequisites(world, me, laboratory).Length == 1, "without a reactor the line is missing");
        world.Submit(new SpawnCommand(PlayerId.System, 1, world.Tick, "ReactorReskin", me, Fixed.FromInt(100), Fixed.FromInt(50), Fixed.Zero, Fixed.Zero));
        world.Step();
        Assert(world.PrerequisitesSatisfied(me, laboratory), "the world counts the reskin (isEquivalentTo)");
        Assert(SimulationQueries.PrerequisitesSatisfied(world, me, laboratory), "so does the command bar");
        Assert(SimulationQueries.MissingPrerequisites(world, me, laboratory).IsEmpty, "and the requirement text names nothing");
    }
}
