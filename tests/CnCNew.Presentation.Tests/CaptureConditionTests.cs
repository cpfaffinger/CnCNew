using System.Collections.Immutable;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.Time;
using CnCNew.Simulation.World;
using CnCNew.Tests;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// The model conditions of a unit that captures a building (<c>CaptureConditions</c>, docs/SIM-STAGED-CAPTURE.md): UNPACKING while it unpacks, RAISING_FLAG while the flag goes up
/// (the infantry capture) or FIRING_A (the Black Lotus's hack), PACKING while it packs, nothing before and after.
/// </summary>
internal static class CaptureConditionTests
{
    private static readonly ImmutableArray<PlayerConfiguration> Players = [new(new PlayerId(1), "Alpha"), new(new PlayerId(2), "Beta")];

    private static IReadOnlyDictionary<string, string> Files() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/CaptureConditionFixture.ini"] = """
        Upgrade Upgrade_CaptureCondition
          Type = PLAYER
          BuildCost = 10
          BuildTime = 1
        End
        SpecialPower SPECIAL_InfantryCaptureCondition
          Enum = SPECIAL_INFANTRY_CAPTURE_BUILDING
          ReloadTime = 0
        End
        SpecialPower SPECIAL_LotusCaptureCondition
          Enum = SPECIAL_BLACKLOTUS_CAPTURE_BUILDING
          ReloadTime = 0
        End
        Object CaptureStation
          KindOf = STRUCTURE
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
          Behavior = DestroyDie Destroy01
          End
        End
        Object InfantryCapturer
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = SpecialAbility Cap01
            SpecialPowerTemplate = SPECIAL_InfantryCaptureCondition
            UpdateModuleStartsAttack = Yes
          End
          Behavior = SpecialAbilityUpdate CapUpdate01
            SpecialPowerTemplate = SPECIAL_InfantryCaptureCondition
            StartAbilityRange = 150.0
            UnpackTime = 500
            PreparationTime = 1000
            PackTime = 300
          End
        End
        Object LotusCapturer
          KindOf = SELECTABLE INFANTRY
          Body = ActiveBody Body01
            MaxHealth = 50.0
            InitialHealth = 50.0
          End
          Behavior = DestroyDie Destroy01
          End
          Behavior = AIUpdateInterface AI01
          End
          Behavior = SpecialAbility Cap01
            SpecialPowerTemplate = SPECIAL_LotusCaptureCondition
            UpdateModuleStartsAttack = Yes
          End
          Behavior = SpecialAbilityUpdate CapUpdate01
            SpecialPowerTemplate = SPECIAL_LotusCaptureCondition
            StartAbilityRange = 150.0
            UnpackTime = 500
            PreparationTime = 1000
            PackTime = 300
          End
        End
        """,
    };

    public static int Run(string[] args)
    {
        var t = new TestHarness("CaptureConditions");
        t.Run("the infantry capture shows UNPACKING, RAISING_FLAG and PACKING in order, and nothing before or after", () => Stages("InfantryCapturer", "RAISING_FLAG"));
        t.Run("the Black Lotus's capture shows UNPACKING, FIRING_A and PACKING", () => Stages("LotusCapturer", "FIRING_A"));
        t.Run("the produced names list RAISING_FLAG", () => Assert(ConditionDeriver.ProducedNames.Contains("RAISING_FLAG"), "RAISING_FLAG is produced"));
        return t.Failures;
    }

    private static void Stages(string template, string preparation)
    {
        var fixture = new SyntheticFixture(SimulationRate.Original, files: Files());
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), SimulationRate.Original, 1, Players, SyntheticFixture.FixtureId, fixture.Fingerprint, ""), fixture.Templates);
        ulong sequence = 0;
        world.Submit(new SpawnCommand(PlayerId.System, ++sequence, world.Tick, template, new PlayerId(1), Fixed.FromInt(100), Fixed.FromInt(50), Fixed.Zero, Fixed.Zero));
        world.Submit(new SpawnCommand(PlayerId.System, ++sequence, world.Tick, "CaptureStation", new PlayerId(2), Fixed.FromInt(200), Fixed.FromInt(50), Fixed.Zero, Fixed.Zero));
        world.Submit(new SetRelationshipCommand(PlayerId.System, ++sequence, world.Tick, new PlayerId(1), new PlayerId(2), Relationship.Enemies));
        world.Step();
        var capturer = world.Entities.Single(entity => entity.Template.Name == template);
        var station = world.Entities.Single(entity => entity.Template.Name == "CaptureStation");
        Assert(world.Submit(new CaptureCommand(new PlayerId(1), 1, world.Tick, capturer.Id, station.Id)).Accepted, "the capture is accepted");

        string Names() => string.Join(" ", new[] { "UNPACKING", "RAISING_FLAG", "FIRING_A", "PACKING" }.Where(name => ConditionDeriver.Derive(capturer, world, null, default).Contains(ModelConditions.Parse(name))));
        var runs = new List<(string Names, int Length)>();
        for (var tick = 0; tick < TestTime.Frames(world, 200); tick++)
        {
            world.Step();
            var names = Names();
            if (runs.Count > 0 && runs[^1].Names == names) runs[^1] = (names, runs[^1].Length + 1);
            else runs.Add((names, 1));
        }

        var sequenceOfStages = string.Join(" > ", runs.Select(run => run.Names.Length == 0 ? "-" : run.Names));
        Assert(sequenceOfStages == $"- > UNPACKING > {preparation} > PACKING > -" || sequenceOfStages == $"UNPACKING > {preparation} > PACKING > -",
            $"the conditions go through the stages in order ({sequenceOfStages})");
        Assert(runs.Single(run => run.Names == "UNPACKING").Length == TestTime.Frames(world, 15) && runs.Single(run => run.Names == preparation).Length == TestTime.Frames(world, 30) && runs.Single(run => run.Names == "PACKING").Length == TestTime.Frames(world, 9),
            $"UnpackTime 500 ms, PreparationTime 1000 ms and PackTime 300 ms are 15, 30 and 9 ticks ({string.Join(", ", runs.Select(run => $"{run.Names}={run.Length}"))})");
        Assert(station.Owner == new PlayerId(1), "the building changed hands");
        Assert(!capturer.Modules.OfType<SpecialAbilityUpdate>().Single().IsActive, "the ability is over");
    }
}
