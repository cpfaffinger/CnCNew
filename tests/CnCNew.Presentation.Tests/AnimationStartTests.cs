using CnCNew.Presentation.Objects;
using CnCNew.Simulation.Testing;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary>
/// Where a state's animation begins (<c>W3DModelDraw::adjustAnimation</c>, issue 23): a <c>MANUAL</c> animation stood on frame 0 whatever its state said. A state with
/// <c>MAINTAIN_FRAME_ACROSS_STATES</c> takes over the share of the state before it (the war factory's conveyor belt stops where it was), <c>START_FRAME_FIRST</c>/<c>LAST</c> win over that,
/// and the logic may hold an animation at a frame of its own (<c>ManualDeployAnimations</c>).
/// </summary>
internal static class AnimationStartTests
{
    private static IReadOnlyDictionary<string, string> Files() => new Dictionary<string, string>(SyntheticFixture.Files)
    {
        ["Data/INI/Object/AnimationStartFixture.ini"] = """
        Object AsFactory
          KindOf = STRUCTURE
          Draw = W3DModelDraw ModuleTag_01
            DefaultConditionState
              Model = AsBelt
              Animation = AsBelt.AsBelt
              AnimationMode = MANUAL
              Flags = MAINTAIN_FRAME_ACROSS_STATES
            End
            ConditionState = ACTIVELY_CONSTRUCTING
              Model = AsBelt
              Animation = AsBelt.AsBelt
              AnimationMode = LOOP
              Flags = MAINTAIN_FRAME_ACROSS_STATES
            End
            ConditionState = DAMAGED
              Model = AsBelt
              Animation = AsBelt.AsBelt
              AnimationMode = MANUAL
              Flags = MAINTAIN_FRAME_ACROSS_STATES2
            End
            ConditionState = REALLYDAMAGED
              Model = AsBelt
              Animation = AsBelt.AsBelt
              AnimationMode = MANUAL
              Flags = MAINTAIN_FRAME_ACROSS_STATES START_FRAME_LAST
            End
            ConditionState = PACKING
              Model = AsBelt
              Animation = AsBelt.AsBelt
              AnimationMode = ONCE_BACKWARDS
            End
            ConditionState = UNPACKING
              Model = AsBelt
              Animation = AsBelt.AsBelt
              AnimationMode = ONCE
              Flags = START_FRAME_LAST
            End
          End
        End
        """,
    };

    public static int Run(string[] args)
    {
        var t = new TestHarness("AnimationStart");
        t.Run("a manual state takes over the frame of a state that shares its maintain flag; the start flags win; the others start by their mode", Starts);
        t.Run("the time into an animation that puts each mode on its start frame", Elapsed);
        t.Run("ManualDeployAnimations: the unpacking animation follows the pack time spent, the packing one the time left", Deploy);
        return t.Failures;
    }

    private static void Starts()
    {
        var fixture = new SyntheticFixture(CnCNew.Tests.TestRate.Default, files: Files());
        var draw = ObjectVisualCatalog.Build(fixture.Graph)["AsFactory"].Draws[0];
        ModelStateSpec State(string conditions) => draw.States[BestStateSelector.FindBest(draw, ModelConditionSet.Parse(conditions))];
        var idle = State("");
        var building = State("ACTIVELY_CONSTRUCTING");
        var other = State("DAMAGED");
        var last = State("REALLYDAMAGED");
        Assert(idle.Mode == AnimationMode.Manual && building.Mode == AnimationMode.Loop, "The fixture's belt is manual when idle and loops while it builds.");

        // The belt stopped half way through a loop of 31 frames: getCurrentAnimFraction 15/30, the manual state stands on (int)(0.5 * 31 - 1) = 14.
        var fraction = AnimationStart.Fraction(building, 15f, 31);
        Assert(fraction == 0.5f, $"The loop stands at half its frames ({fraction}).");
        Assert(AnimationStart.Frame(idle, building, fraction, 31) == 14f, $"The idle belt stands where the loop stopped ({AnimationStart.Frame(idle, building, fraction, 31)}).");
        Assert(AnimationStart.Frame(building, idle, AnimationStart.Fraction(idle, 14f, 31), 31) == (int)(14f / 30f * 31f - 1f), "and the loop goes on from there.");
        Assert(AnimationStart.Frame(idle, null, -1f, 31) == 0f, "Without a state before it the manual belt stands on frame 0.");
        Assert(AnimationStart.Frame(other, building, fraction, 31) == 0f, "A state that shares no maintain flag starts on its own frame.");
        // A condition state starts as a copy of the default state, so every state of the fixture maintains; one without the flag:
        var keepsNone = building with { Flags = AnimationFlags.None };
        Assert(AnimationStart.Fraction(keepsNone, 20f, 31) == -1f && AnimationStart.Frame(idle, keepsNone, 0.5f, 31) == 0f, "A state without a maintain flag hands on no frame.");
        Assert(AnimationStart.Frame(last, building, fraction, 31) == 30f, "START_FRAME_LAST wins over the maintained frame: the manual animation stands on its last frame.");
        Assert(AnimationStart.Frame(State("PACKING"), null, -1f, 31) == 30f, "A backwards mode starts on its last frame.");
        Assert(AnimationStart.Frame(State("UNPACKING"), null, -1f, 31) == 30f, "A forward mode with START_FRAME_LAST starts on its last frame.");
        Assert(AnimationStart.Fraction(building, 40f, 31) == 1f && AnimationStart.Fraction(building, -1f, 31) == 0f && AnimationStart.Fraction(keepsNone, 3f, 31) == -1f,
            "The share is clamped, and -1 for a state that keeps no frame.");
    }

    private static void Elapsed()
    {
        // 31 frames at 15 frames per second, at speed 1.5: 22.5 frames a second.
        Assert(Near(AnimationStart.ElapsedAt(AnimationMode.Loop, 9f, 31, 15, 1.5), 9.0 / 22.5), "A loop has played its start frames.");
        Assert(Near(AnimationStart.ElapsedAt(AnimationMode.Once, 30f, 31, 15, 1.5), 30.0 / 22.5), "A forward animation that starts on its last frame has played them all.");
        Assert(Near(AnimationStart.ElapsedAt(AnimationMode.OnceBackwards, 30f, 31, 15, 1.5), 0.0), "A backwards one that starts on its last frame has played none.");
        Assert(Near(AnimationStart.ElapsedAt(AnimationMode.LoopBackwards, 10f, 31, 15, 1.5), 20.0 / 22.5), "and one that starts on frame 10 has played twenty.");
        Assert(AnimationStart.ElapsedAt(AnimationMode.Manual, 10f, 31, 15, 1.5) == 0.0 && AnimationStart.ElapsedAt(AnimationMode.Loop, 10f, 1, 15, 1.5) == 0.0,
            "A manual animation and a single frame do not advance.");
    }

    private static void Deploy()
    {
        // PackTime 90 frames; the unpack began 30 frames ago and is done in 60: the unpacking animation stands on frame 90 - 60 = 30 (the original reads the pack time there).
        Assert(AnimationStart.DeployFrame(true, 60, 90, 1) == 30f, "Unpacking at 30 Hz.");
        Assert(AnimationStart.DeployFrame(true, 120, 180, 2) == 30f, "Unpacking at 60 Hz, the same frame of the animation.");
        Assert(AnimationStart.DeployFrame(false, 60, 90, 1) == 60f && AnimationStart.DeployFrame(false, 240, 360, 4) == 60f, "Packing stands on the frames left, at 30 and 120 Hz.");
        Assert(AnimationStart.DeployFrame(true, 120, 90, 1) == 0f, "A frame before the first is the first.");
    }

    private static bool Near(double value, double expected) => Math.Abs(value - expected) < 1e-9;
}
