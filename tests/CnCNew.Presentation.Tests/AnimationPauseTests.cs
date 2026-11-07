using System.Collections.Immutable;
using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;
using CnCNew.Tests;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// <c>Drawable::getShouldAnimate</c> (<c>W3DModelDraw::doDrawModule</c> pauses the animation by it): a hack, a paralysis, an EMP, subdual and a missing driver stop every draw module's
/// animation except a helicopter's; no power (the script status or <c>DISABLED_UNDERPOWERED</c>) stops those of a module that has <c>AnimationsRequirePower</c> (its default).
/// </summary>
internal static class AnimationPauseTests
{
    private static readonly ImmutableArray<PlayerConfiguration> Players = [new(new PlayerId(1), "Alpha")];

    private static IReadOnlyDictionary<string, string> Files() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/AnimationPauseFixture.ini"] = """
        Object PauseStation
          KindOf = STRUCTURE SELECTABLE
          Draw = W3DModelDraw ModuleTag_Draw
            ConditionState = NONE
              Model = PauseStation
            End
          End
          Body = StructureBody Body01
            MaxHealth = 100.0
            InitialHealth = 100.0
          End
        End
        Object PauseCopter
          KindOf = VEHICLE SELECTABLE PRODUCED_AT_HELIPAD
          Draw = W3DModelDraw ModuleTag_Draw
            ConditionState = NONE
              Model = PauseCopter
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
        var t = new TestHarness("AnimationPause");
        t.Run("a disable or a missing power stops the animations as getShouldAnimate says", Pauses);
        return t.Failures;
    }

    private static void Pauses()
    {
        var fixture = new SyntheticFixture(CnCNew.Tests.TestRate.Default, files: Files());
        var world = new SimulationWorld(new WorldConfiguration(new WorldId(1), fixture.Templates.Rate, 1, Players, SyntheticFixture.FixtureId, fixture.Fingerprint, ""), fixture.Templates);
        ulong sequence = 0;
        world.Submit(new SpawnCommand(PlayerId.System, ++sequence, world.Tick, "PauseStation", new PlayerId(1), Fixed.FromInt(100), Fixed.FromInt(50), Fixed.Zero, Fixed.Zero));
        world.Submit(new SpawnCommand(PlayerId.System, ++sequence, world.Tick, "PauseCopter", new PlayerId(1), Fixed.FromInt(150), Fixed.FromInt(50), Fixed.Zero, Fixed.Zero));
        world.Step();
        var station = world.Entities.Single(entity => entity.Template.Name == "PauseStation");
        var copter = world.Entities.Single(entity => entity.Template.Name == "PauseCopter");
        var projector = new FrameProjector(ObjectVisualCatalog.Build(fixture.Graph), new FrameProjectorOptions());
        AnimationPause Pause(Entity entity)
        {
            projector.Advance(world);
            return projector.Project(1f).Drawables.Single(drawable => drawable.Entity == entity.Id).Pause;
        }

        Assert(Pause(station) == AnimationPause.None && Pause(copter) == AnimationPause.None, "nothing is disabled: everything animates");
        Assert(!AnimationPauses.Holds(AnimationPause.None, true), "and nothing holds");

        world.SetScriptDisable(station, Entity.ScriptUnpowered, true);
        var unpowered = Pause(station);
        Assert(unpowered == AnimationPause.WithoutPower && AnimationPauses.Holds(unpowered, true) && !AnimationPauses.Holds(unpowered, false),
            $"a script-unpowered object stops the animations of a module that requires power, not of one that does not ({unpowered})");
        world.SetScriptDisable(station, Entity.ScriptUnpowered, false);
        Assert(Pause(station) == AnimationPause.None, "powered again, it animates");

        world.DisableByEmpUntil(station, world.Tick + TestTime.Frames(world, 300));
        var emp = Pause(station);
        Assert(emp == AnimationPause.Always && AnimationPauses.Holds(emp, false), $"an EMP stops every module ({emp})");
        world.DisableByEmpUntil(copter, world.Tick + TestTime.Frames(world, 300));
        Assert(Pause(copter) == AnimationPause.None, "but a helicopter keeps animating");

        // A disable that is none of those pauses nothing: DISABLED_HELD, a script's disable.
        world.SetScriptDisable(copter, Entity.ScriptHeld, true);
        world.SetScriptDisable(station, Entity.ScriptDisabled, true);
        Assert(Pause(copter) == AnimationPause.None, "a hold pauses no animation");
    }
}
