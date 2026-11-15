using System.Collections.Immutable;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// <c>PreorderCreate::onBuildComplete</c> (issue 23): a completed object with the module shows <c>PREORDER</c> - the Officers' Club flag on the command centres of the shipped data -
/// when its controlling player pre-ordered the game, and keeps what it was given when it changes hands. Nobody pre-ordered by default.
/// </summary>
internal static class PreorderConditionTests
{
    private static readonly ImmutableArray<PlayerConfiguration> Players = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta")];

    private static IReadOnlyDictionary<string, string> Files() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/PreorderFixture.ini"] = """
        Object PoCenter
          KindOf = STRUCTURE
          Draw = W3DModelDraw ModuleTag_01
            DefaultConditionState
              Model = PoCenterModel
            End
          End
          Draw = W3DModelDraw ModuleTag_02
            DefaultConditionState
              Model = None
            End
            ConditionState = PREORDER
              Model = PoFlag
            End
          End
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = PreorderCreate ModuleTag_03
          End
        End
        Object PoBarracks
          KindOf = STRUCTURE
          Draw = W3DModelDraw ModuleTag_01
            DefaultConditionState
              Model = PoBarracksModel
            End
          End
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
        End
        """,
    };

    public static int Run(string[] args)
    {
        var t = new TestHarness("PreorderCondition");
        t.Run("a completed command centre of a player who pre-ordered shows PREORDER and keeps it when captured; nobody else's does, nor a site under construction", Flags);
        return t.Failures;
    }

    private static void Flags()
    {
        var rate = CnCNew.Tests.TestRate.Default;
        var fixture = new SyntheticFixture(rate, files: Files());
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), rate, 1, Players, SyntheticFixture.FixtureId, fixture.Fingerprint, ""), fixture.Templates);
        world.Submit(new SpawnCommand(PlayerId.System, 1, world.Tick, "PoCenter", new PlayerId(1), Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero));
        world.Submit(new SpawnCommand(PlayerId.System, 2, world.Tick, "PoCenter", new PlayerId(2), Fixed.FromInt(200), Fixed.Zero, Fixed.Zero, Fixed.Zero));
        world.Submit(new SpawnCommand(PlayerId.System, 3, world.Tick, "PoBarracks", new PlayerId(1), Fixed.FromInt(400), Fixed.Zero, Fixed.Zero, Fixed.Zero));
        world.Submit(new SpawnCommand(PlayerId.System, 4, world.Tick, "PoCenter", new PlayerId(1), Fixed.FromInt(600), Fixed.Zero, Fixed.Zero, Fixed.Zero));
        world.Step();
        var centres = world.Entities.Where(entity => entity.Template.Name == "PoCenter").OrderBy(entity => entity.X).ToArray();
        var (mine, theirs, site) = (centres[0], centres[1], centres[2]);
        var barracks = world.Entities.Single(entity => entity.Template.Name == "PoBarracks");
        world.SetStatus(site.Id, 1UL << ObjectStatusBits.UnderConstruction);
        var catalog = ObjectVisualCatalog.Build(fixture.Graph);

        var nobody = new FrameProjector(catalog);
        nobody.Advance(world);
        Assert(!nobody.Project(1f).Drawables.Any(drawable => drawable.Conditions.Contains(ModelConditions.Preorder)), "Without a player who pre-ordered nothing shows PREORDER.");

        var projector = new FrameProjector(catalog, new FrameProjectorOptions { PreorderPlayers = [new PlayerId(1)] });
        projector.Advance(world);
        bool Flagged(Entity entity) => projector.Project(1f).Drawables.Single(drawable => drawable.Entity == entity.Id).Conditions.Contains(ModelConditions.Preorder);
        Assert(Flagged(mine), "The pre-ordering player's completed command centre shows PREORDER.");
        Assert(!Flagged(theirs), "The other player's does not.");
        Assert(!Flagged(barracks), "A building without PreorderCreate does not.");
        Assert(!Flagged(site), "A site under construction does not yet.");
        var flag = projector.Project(1f).Drawables.Single(drawable => drawable.Entity == mine.Id).Selections[1].Model;
        Assert(string.Equals(flag, "PoFlag", StringComparison.OrdinalIgnoreCase), $"The flag's draw module selects its PREORDER state ({flag}).");

        world.SetStatus(site.Id, 0, 1UL << ObjectStatusBits.UnderConstruction);
        ((IWorldServices)world).TransferOwnership(mine.Id, new PlayerId(2));
        ((IWorldServices)world).TransferOwnership(theirs.Id, new PlayerId(1));
        world.Step();
        projector.Advance(world);
        Assert(Flagged(site), "The site shows it once its construction is complete.");
        Assert(Flagged(mine), "A captured centre keeps the flag it was given at its completion.");
        Assert(!Flagged(theirs), "A centre captured by the pre-ordering player does not gain one.");
    }
}
