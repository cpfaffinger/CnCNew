using System.Collections.Immutable;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// A unit in disguise (F-83; <c>StealthUpdate::changeVisualDisguise</c>) has the model of what it stands for for everybody, and the house colour of that player for everybody but its allies.
/// </summary>
internal static class DisguiseDrawTests
{
    private static readonly ImmutableArray<PlayerConfiguration> Players = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta")];

    private static IReadOnlyDictionary<string, string> Files() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/DisguiseDrawFixture.ini"] = """
        Object DdTank
          KindOf = SELECTABLE VEHICLE
          Draw = W3DModelDraw ModuleTag_01
            DefaultConditionState
              Model = DdTankModel
            End
          End
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
        End
        Object DdTruck
          KindOf = SELECTABLE VEHICLE DISGUISER
          Draw = W3DModelDraw ModuleTag_01
            DefaultConditionState
              Model = DdTruckModel
            End
          End
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = StealthUpdate Stealth01
            DisguisesAsTeam = Yes
            DisguiseTransitionTime = 1000
            DisguiseRevealTransitionTime = 1000
          End
        End
        """,
    };

    public static int Run(string[] args)
    {
        var t = new TestHarness("DisguiseDraw");
        t.Run("a truck in disguise is drawn as the tank it stands for, in the tank's colour to its enemies and in its own to itself and to an observer", Drawn);
        return t.Failures;
    }

    private static void Drawn()
    {
        var rate = SimulationRate.Hz60;
        var fixture = new SyntheticFixture(rate, files: Files());
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, Players, SyntheticFixture.FixtureId, fixture.Fingerprint, ""), fixture.Templates);
        world.Submit(new SpawnCommand(PlayerId.System, 1, world.Tick, "DdTank", new PlayerId(1), Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero));
        world.Submit(new SpawnCommand(PlayerId.System, 2, world.Tick, "DdTruck", new PlayerId(2), Fixed.FromInt(20), Fixed.Zero, Fixed.Zero, Fixed.Zero));
        world.Step();
        var tank = world.Entities.Single(entity => entity.Template.Name == "DdTank");
        var truck = world.Entities.Single(entity => entity.Template.Name == "DdTruck");
        var catalog = ObjectVisualCatalog.Build(fixture.Graph);

        DrawableInstance Seen(PlayerId? viewer)
        {
            var projector = new FrameProjector(catalog, new FrameProjectorOptions { Viewer = viewer });
            projector.Advance(world);
            return projector.Project(1f).Drawables.Single(drawable => drawable.Entity == truck.Id);
        }

        Assert(Seen(new PlayerId(1)).PrimaryModel == "ddtruckmodel", "Before the disguise the truck is drawn as a truck.");
        truck.Modules.OfType<StealthUpdate>().Single().DisguiseAs(world, tank);
        for (var step = 0; step < world.Time.OriginalFrames(30); step++) world.Step();
        var enemy = Seen(new PlayerId(1));
        Assert(enemy.PrimaryModel == "ddtankmodel" && enemy.TemplateName == "DdTank", $"Its enemy sees the tank's model ({enemy.PrimaryModel}, {enemy.TemplateName}).");
        Assert(enemy.Owner == new PlayerId(1), $"Its enemy sees the tank's house colour ({enemy.Owner}).");
        var own = Seen(new PlayerId(2));
        Assert(own.PrimaryModel == "ddtankmodel" && own.Owner == new PlayerId(2), $"Its own player sees the model in its own colour ({own.PrimaryModel}, {own.Owner}).");
        var observer = Seen(null);
        Assert(observer.PrimaryModel == "ddtankmodel" && observer.Owner == new PlayerId(2), $"An observer sees the true owner ({observer.Owner}).");
    }
}
