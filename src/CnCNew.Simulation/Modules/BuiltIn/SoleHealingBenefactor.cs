using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>
/// <c>Object::attemptHealingFromSoleBenefactor</c>, the non-stacking heal of the healing auras: an object takes it from one healer at a time - the one that healed it last, until
/// that one has not renewed its claim for the claim's duration. Another healer's offer in the meantime heals nothing.
/// </summary>
public static class SoleHealingBenefactor
{
    /// <summary>
    /// Heals <paramref name="target"/> by <paramref name="heal"/> when no other healer holds it at <paramref name="now"/> (the healer's frame, as a tick) and holds it for
    /// <paramref name="durationTicks"/> from there; returns whether it healed.
    /// </summary>
    public static bool AttemptHealing(IWorldServices world, Entity target, Entity source, in DamageInfo heal, long now, long durationTicks)
    {
        if (!(now > target.SoleHealingBenefactorUntil || target.SoleHealingBenefactor == source.Id)) return false;
        target.SoleHealingBenefactor = source.Id;
        target.SoleHealingBenefactorUntil = now + durationTicks;
        world.MarkDirty(target);
        world.Damage(target.Id, heal);
        return true;
    }
}
