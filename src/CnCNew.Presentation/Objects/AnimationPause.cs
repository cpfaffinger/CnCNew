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
