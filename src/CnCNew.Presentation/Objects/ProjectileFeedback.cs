using System.Collections.Immutable;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Objects;

/// <summary>The clip of one weapon slot as the drawable is told it (<c>Drawable::updateDrawableClipStatus</c>): the rounds left and the clip's size.</summary>
public readonly record struct ProjectileClip(int Slot, int Remaining, int Size);

/// <summary>
/// <c>ProjectileBoneFeedbackEnabledSlots</c> (#385): a model that carries its rounds - the Scud Storm's nine missiles on <c>WeaponA01</c> to <c>WeaponA09</c> - shows one sub-object per round left and hides the
/// fired ones from the front. <c>Object::adjustModelConditionForWeaponStatus</c> tells the drawable every frame how many rounds each slot has left (<c>Weapon::getRemainingAmmo</c>: none while the clip
/// reloads) and <c>W3DModelDraw::doHideShowProjectileObjects</c> names the parts by the current state's launch bone and a two-digit round number, or hides the state's one <c>WeaponHideShowBone</c> part once
/// a round is gone. It acts on the model after the state's own <c>ShowSubObject</c>/<c>HideSubObject</c> lines and before the upgrades' (<c>updateSubObjects</c> runs last).
/// </summary>
public static class ProjectileFeedback
{
    private const int SlotCount = 3;

    /// <summary>The clips of the slots any draw module of <paramref name="visual"/> shows; default when none does.</summary>
    public static ImmutableArray<ProjectileClip> Of(Entity entity, SimulationWorld world, ObjectVisual visual)
    {
        var slots = 0;
        foreach (var draw in visual.Draws) slots |= draw.ProjectileBoneFeedbackSlots;
        if (slots == 0) return default;
        var clips = ImmutableArray.CreateBuilder<ProjectileClip>(SlotCount);
        for (var slot = 0; slot < SlotCount; slot++)
            if ((slots & (1 << slot)) != 0 && world.ClipStatusOf(entity, slot) is { } clip) clips.Add(new ProjectileClip(slot, clip.Remaining, clip.Size));
        return clips.Count == 0 ? default : clips.ToImmutable();
    }

    /// <summary>
    /// What <paramref name="draw"/> shows and hides of its rounds in <paramref name="state"/> (a real state or a transition): for each slot it enables, the first <c>size - remaining</c> rounds hidden and
    /// the rest shown. A state that names no launch bone for the slot names parts the model does not have, and nothing changes. Default when there is nothing to do.
    /// </summary>
    public static ImmutableArray<SubObjectVisibility> Rules(DrawSpec draw, ModelStateSpec state, ImmutableArray<ProjectileClip> clips)
    {
        if (draw.ProjectileBoneFeedbackSlots == 0 || clips.IsDefaultOrEmpty) return default;
        ImmutableArray<SubObjectVisibility>.Builder? rules = null;
        foreach (var clip in clips)
        {
            if ((draw.ProjectileBoneFeedbackSlots & (1 << clip.Slot)) == 0 || clip.Remaining > clip.Size) continue;
            var bones = clip.Slot < state.WeaponBones.Length ? state.WeaponBones[clip.Slot] : WeaponSlotBones.None;
            var hidden = clip.Size - clip.Remaining;
            if (bones.HideShowBone is { } single)
            {
                (rules ??= ImmutableArray.CreateBuilder<SubObjectVisibility>()).Add(new SubObjectVisibility(single, hidden > 0));
                continue;
            }
            if (bones.LaunchBone is not { } launch) continue;
            rules ??= ImmutableArray.CreateBuilder<SubObjectVisibility>();
            for (var round = 1; round <= clip.Size; round++)
                rules.Add(new SubObjectVisibility(launch + round.ToString("00", System.Globalization.CultureInfo.InvariantCulture), round <= hidden));
        }
        return rules is null ? default : rules.ToImmutable();
    }
}
