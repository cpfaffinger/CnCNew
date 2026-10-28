using System.Collections.Immutable;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// A hidden stealth garrison as a player sees it (F-54, <c>GarrisonContain::recalcApparentModelConditionState</c>): a building held only by an undetected stealth rider of player 1 shows
/// player 2 no <c>GARRISONED</c>, player 1 does see it; once the rider is detected player 2 sees it too.
/// </summary>
internal static class StealthGarrisonConditionTests
{
    private static readonly ImmutableArray<PlayerConfiguration> Players = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta")];

    private static IReadOnlyDictionary<string, string> Files() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/StealthGarrisonConditionFixture.ini"] = """
        Object SgcBunker
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 1000.0
            InitialHealth = 1000.0
          End
          Behavior = GarrisonContain Garrison01
            ContainMax = 5
          End
        End
        Object SgcSneak
          KindOf = SELECTABLE INFANTRY STEALTH_GARRISON
          TransportSlotCount = 1
          Body = ActiveBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = StealthUpdate Stealth01
            StealthDelay = 0
            InnateStealth = Yes
          End
        End
        """,
    };

    public static int Run(string[] args)
    {
        var t = new TestHarness("StealthGarrisonConditions");
        t.Run("a hidden stealth garrison shows a non-ally no GARRISONED, its owner and an ally the condition, and everybody once its rider is detected", Hidden);
        return t.Failures;
    }

    private static void Hidden()
    {
        var fixture = new SyntheticFixture(CnCNew.Tests.TestRate.Default, files: Files());
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), CnCNew.Tests.TestRate.Default, 1, Players, SyntheticFixture.FixtureId, fixture.Fingerprint, ""), fixture.Templates);
        world.Submit(new SpawnCommand(PlayerId.System, 1, world.Tick, "SgcBunker", PlayerId.System, Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero));
        world.Submit(new SpawnCommand(PlayerId.System, 2, world.Tick, "SgcSneak", new PlayerId(1), Fixed.Zero, Fixed.Zero, Fixed.Zero, Fixed.Zero));
        world.Step();
        var bunker = world.Entities.Single(entity => entity.Template.Name == "SgcBunker");
        var sneak = world.Entities.Single(entity => entity.Template.Name == "SgcSneak");
        Assert(world.TryEnterContainer(sneak, bunker), "Setup: the stealth rider enters the neutral building.");
        bool Garrisoned(int? viewer) => ConditionDeriver.Derive(bunker, world, null, new ConditionContext(Viewer: viewer is { } v ? new PlayerId(v) : null)).Contains(ModelConditions.Garrisoned);
        Assert(!Garrisoned(2), "Player 2 sees no GARRISONED.");
        Assert(Garrisoned(1) && Garrisoned(null), "Player 1 (and a view without a player) sees it.");
        sneak.Modules.OfType<StealthUpdate>().Single().MarkDetected(world, world.Time.OriginalFrames(300));
        Assert(Garrisoned(2), "Detected, player 2 sees it too.");
    }
}
