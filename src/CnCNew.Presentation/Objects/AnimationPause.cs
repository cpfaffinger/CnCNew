using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Objects;

/// <summary>Why a drawable's model animation stands (<c>Drawable::getShouldAnimate</c>): not at all, because of a disable that stops every animation, or only for a draw module that needs power.</summary>
[Flags]
public enum AnimationPause : byte
{
    None = 0,
    /// <summary>A hack, a paralysis, an EMP, subdual or no driver stops the animations of every draw module (not a helicopter's).</summary>
    Always = 1,
    /// <summary>No power (the script status <c>OBJECT_STATUS_SCRIPT_UNPOWERED</c> or <c>DISABLED_UNDERPOWERED</c>) stops the animations of a draw module with <c>AnimationsRequirePower</c> (its default).</summary>
    WithoutPower = 2,
}

/// <summary>
/// <c>Drawable::setAnimationLoopDuration</c> (<c>W3DModelDraw::setCurAnimDurationInMsec</c>): a module that wants the animation of the object's current state to take a given time says so, and
/// the draw module sets the animation's frame rate multiplier to its natural duration over the wanted one. The Strategy Center is the one such module with a state animation of the
/// shipped objects that the simulation drives (<c>BattlePlanUpdate::setStatus</c>: the doors open and close in the plan's animation time, not in their own 9.0, 5.8 and 3.4 seconds; F-157).
/// </summary>
public static class AnimationDurations
{
    /// <summary>
    /// The seconds the state animation of <paramref name="entity"/> is stretched to, 0 for the animation's own speed: <c>ceilf(numFrames * MSEC_PER_LOGICFRAME_REAL)</c> milliseconds of the
    /// plan's animation time, while a battle plan unpacks or packs.
    /// </summary>
    public static float Of(SimulationWorld world, Entity entity)
    {
        foreach (var module in entity.Modules)
        {
            if (module is not BattlePlanUpdate { DoorAnimationTicks: > 0 and var ticks }) continue;
            var frames = ticks / world.Time.TicksPerOriginalFrame;
            return MathF.Ceiling(frames * 1000f / CnCNew.Data.OriginalFrame.PerSecond) / 1000f;
        }
        return 0f;
    }

    /// <summary>
    /// <c>setCurAnimDurationInMsec</c>: the frame rate multiplier that makes an animation of <paramref name="frameCount"/> frames at <paramref name="frameRate"/> frames per second take
    /// <paramref name="seconds"/> (its natural duration is <c>numFrames / rate</c>, not <c>(numFrames - 1) / rate</c>); 1 when no duration is wanted or the animation has none.
    /// </summary>
    public static double Speed(int frameCount, int frameRate, float seconds)
    {
        var natural = frameRate > 0 ? frameCount / (double)frameRate : 0.0;
        return natural > 0.0 && seconds > 0f ? natural / seconds : 1.0;
    }
}

public static class AnimationPauses
{
    /// <summary><c>Drawable::getShouldAnimate</c>, asked once for each answer of <c>considerPower</c>.</summary>
    public static AnimationPause Of(SimulationWorld world, Entity entity)
    {
        var pause = AnimationPause.None;
        if ((entity.ScriptDisableMask & Entity.ScriptUnpowered) != 0) pause |= AnimationPause.WithoutPower;
        var disabled = world.DisabledFlagsOf(entity);
        if (disabled == DisabledMask.None) return pause;
        // "helicopters just look goofy if they stop animating, so keep animating them, anyway"
        const DisabledMask stopsAll = DisabledMask.Hacked | DisabledMask.Paralyzed | DisabledMask.Emp | DisabledMask.Subdued | DisabledMask.Unmanned;
        if ((disabled & stopsAll) != 0 && !entity.Template.IsKindOf("PRODUCED_AT_HELIPAD")) pause |= AnimationPause.Always;
        if ((disabled & DisabledMask.Underpowered) != 0) pause |= AnimationPause.WithoutPower;
        return pause;
    }

    /// <summary>Whether the animation of a draw module stands: <paramref name="requiresPower"/> is its <c>AnimationsRequirePower</c>.</summary>
    public static bool Holds(AnimationPause pause, bool requiresPower) => (pause & AnimationPause.Always) != 0 || (requiresPower && (pause & AnimationPause.WithoutPower) != 0);
}
