using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;

namespace CnCNew.Simulation.World;

/// <summary>
/// The world services the crate, hijack and sabotage modules need (<c>docs/SIMULATION-CORE.md</c>, crates and collide modules): a veterancy gain by whole
/// levels, a weapon-set refresh and the per-player "power sabotaged" tick. The collision dispatch itself lives in <c>SimulationWorld.Collision.cs</c>.
/// </summary>
public sealed partial class SimulationWorld
{
    private static readonly int CarBombStatusBit = ObjectStatusBits.Index("IS_CARBOMB");
    private static readonly int NoCollisionsBit = ObjectStatusBits.Index("NO_COLLISIONS");
    private static readonly int BoobyTrappedBit = ObjectStatusBits.Index("BOOBY_TRAPPED");

    /// <inheritdoc cref="IWorldServices.GainExperienceForLevels"/>
    public bool GainExperienceForLevels(Entity entity, int levels, bool scaleForBonus)
    {
        var newLevel = Math.Min(entity.VeterancyLevel + levels, 3);
        if (newLevel <= entity.VeterancyLevel) return false;
        if (!entity.Template.IsTrainable) return true; // addExperiencePoints: "if (!isTrainable()) return; //safety"
        var needed = entity.Template.ExperienceRequired[newLevel] - entity.Experience;
        var gain = scaleForBonus ? checked((int)(Fixed.FromInt(needed) * entity.ExperienceScalar).Floor()) : needed;
        entity.Experience = checked(entity.Experience + gain);
        var level = 0;
        while (level + 1 < 4 && entity.Experience >= entity.Template.ExperienceRequired[level + 1]) level++;
        MarkDirty(entity);
        if (level != entity.VeterancyLevel) OnVeterancyLevelChanged(entity, level);
        return true;
    }

    /// <inheritdoc cref="IWorldServices.SetVeterancy"/>
    public void SetVeterancy(Entity entity, int level)
    {
        if (level is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(level));
        SetVeterancyLevel(entity, level);
    }

    /// <inheritdoc cref="IWorldServices.WeaponSetChanged"/>
    public void WeaponSetChanged(Entity entity)
    {
        InitializeWeaponClip(entity);
        MarkDirty(entity);
    }

    /// <inheritdoc cref="IWorldServices.SetPowerSabotagedUntil"/>
    public void SetPowerSabotagedUntil(PlayerId player, long tick)
    {
        if (_players.TryGetValue(player.Value, out var state)) state.PowerSabotagedUntil = tick;
    }

    /// <inheritdoc cref="IWorldServices.PowerSabotagedUntil"/>
    public long PowerSabotagedUntil(PlayerId player) => _players.TryGetValue(player.Value, out var state) ? state.PowerSabotagedUntil : 0;
}
