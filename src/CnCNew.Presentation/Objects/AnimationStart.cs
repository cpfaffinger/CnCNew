namespace CnCNew.Presentation.Objects;

/// <summary>
/// Where the animation of a model state begins (<c>W3DModelDraw::adjustAnimation</c>) and the frame the logic holds an animation at (<c>Drawable::setAnimationFrame</c>).
/// <para>
/// <c>adjustAnimation</c> starts a backwards mode on its last frame and every other on frame 0; <c>START_FRAME_FIRST</c> and <c>START_FRAME_LAST</c> say so outright; failing those, a state with
/// a <c>MAINTAIN_FRAME_ACROSS_STATES</c> flag that follows another state sharing one of those flags starts where that state's animation stood, as a share of its length (the war
/// factory's conveyor belt stops where it was when production ends, and goes on from there). The frame is that share times the frame count less one, truncated, as the original
/// computes it. A <c>MANUAL</c> animation then stands on that frame; every other mode plays on from it. (<c>RANDOMIZE_START_FRAME</c> goes first in the original; the client draws
/// its random start itself.)
/// </para>
/// </summary>
public static class AnimationStart
{
    private const AnimationFlags Maintain = AnimationFlags.MaintainFrameAcrossStates | AnimationFlags.MaintainFrameAcrossStates2
        | AnimationFlags.MaintainFrameAcrossStates3 | AnimationFlags.MaintainFrameAcrossStates4;

    /// <summary><c>isAnyMaintainFrameFlagSet</c>.</summary>
    public static bool Maintains(AnimationFlags flags) => (flags & Maintain) != 0;

    /// <summary>
    /// The first frame of <paramref name="state"/>'s animation of <paramref name="frameCount"/> frames, after <paramref name="previous"/> whose animation stood at the share
    /// <paramref name="previousFraction"/> (<c>getCurrentAnimFraction</c>: frame / (count - 1); negative when the previous state keeps no frame).
    /// </summary>
    public static float Frame(ModelStateSpec state, ModelStateSpec? previous, float previousFraction, int frameCount)
    {
        ArgumentNullException.ThrowIfNull(state);
        var last = Math.Max(frameCount - 1, 0);
        var start = state.Mode is AnimationMode.OnceBackwards or AnimationMode.LoopBackwards ? last : 0;
        if (state.Flags.HasFlag(AnimationFlags.StartFrameFirst)) return 0;
        if (state.Flags.HasFlag(AnimationFlags.StartFrameLast)) return last;
        if (Maintains(state.Flags) && previous is not null && previous.Index != state.Index && (state.Flags & previous.Flags & Maintain) != 0 && previousFraction >= 0f)
            start = (int)(previousFraction * frameCount - 1f);
        return Math.Clamp(start, 0, last);
    }

    /// <summary>
    /// <c>getCurrentAnimFraction</c>: where the animation of a state with a maintain flag stands, as a share of its frames (0 to 1); -1 for a state without one, whose frame the next state
    /// does not take over.
    /// </summary>
    public static float Fraction(ModelStateSpec state, float frame, int frameCount)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (!Maintains(state.Flags)) return -1f;
        if (frame < 0f) return 0f;
        if (frame >= frameCount) return 1f;
        return frameCount > 1 ? frame / (frameCount - 1) : 0f;
    }

    /// <summary>
    /// The time into a state's animation at which the closed form of the animation clock (<c>W3dAnimationClock</c>) is at <paramref name="frame"/>: a forward mode has advanced that many
    /// frames, a backwards one the frames from the last down to it. Zero for a manual animation, which stands on its frame, and for one that does not advance.
    /// </summary>
    public static double ElapsedAt(AnimationMode mode, float frame, int frameCount, int frameRate, double speed)
    {
        var rate = frameRate * speed;
        if (rate <= 0.0 || frameCount <= 1) return 0.0;
        var last = frameCount - 1;
        var advanced = mode switch
        {
            AnimationMode.Loop or AnimationMode.Once or AnimationMode.LoopPingPong => frame,
            AnimationMode.LoopBackwards or AnimationMode.OnceBackwards => last - frame,
            _ => 0f,
        };
        return Math.Max(advanced, 0f) / rate;
    }

    /// <summary>
    /// <c>DeployStyleAIUpdate::update</c> with <c>ManualDeployAnimations</c>: the logic holds the unpacking animation at <c>PackTime - framesLeft</c> (it reads the pack time there,
    /// not the unpack time) and the packing one at <c>framesLeft</c>, in logic frames, as frames of the animation.
    /// </summary>
    /// <param name="unpacking">The unit unpacks (<c>DEPLOY</c>); otherwise it packs (<c>UNDEPLOY</c>).</param>
    /// <param name="ticksLeft">Ticks until the unit is set up or packed (<c>m_frameToWaitForDeploy - now</c>).</param>
    /// <param name="packTicks"><c>PackTime</c> in ticks.</param>
    /// <param name="ticksPerFrame">Ticks per logic frame of the original (1 at 30 Hz).</param>
    public static float DeployFrame(bool unpacking, long ticksLeft, long packTicks, int ticksPerFrame)
    {
        var perFrame = Math.Max(ticksPerFrame, 1);
        var frames = (unpacking ? packTicks - ticksLeft : ticksLeft) / (float)perFrame;
        return Math.Max(frames, 0f);
    }
}
