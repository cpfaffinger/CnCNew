using System.Collections.Immutable;
using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;

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

    /// <inheritdoc cref="IWorldServices.SetPowerSabotagedUntil"/>
    public void SetPowerSabotagedUntil(PlayerId player, long tick)
    {
        if (_players.TryGetValue(player.Value, out var state)) state.PowerSabotagedUntil = tick;
    }

    /// <inheritdoc cref="IWorldServices.PowerSabotagedUntil"/>
    public long PowerSabotagedUntil(PlayerId player) => _players.TryGetValue(player.Value, out var state) ? state.PowerSabotagedUntil : 0;

    /// <summary>
    /// <c>Weapon::processRequestAssistance</c>: every other object of the firing player that is equivalent to <paramref name="source"/> (here: the same template), within
    /// <see cref="WeaponInfo.RequestAssistRange"/> of it (centre to centre) and carrying an <see cref="AssistedTargetingUpdate"/> that is free is asked to attack the victim too.
    /// </summary>
    private void ProcessRequestAssistance(Entity source, WeaponInfo weapon, Entity victim)
    {
        var rangeSquared = SquaredReach.Of(weapon.RequestAssistRange);
        foreach (var candidate in _entities.Values)
        {
            if (candidate == source || candidate.Owner != source.Owner || candidate.DestroyRequested || candidate.EffectivelyDead || candidate.Template.Index != source.Template.Index) continue;
            var dx = candidate.X - source.X;
            var dy = candidate.Y - source.Y;
            if (!rangeSquared.Contains(dx * dx + dy * dy)) continue;
            foreach (var module in candidate.ModuleArray)
                if (module is AssistedTargetingUpdate assist && assist.IsFreeToAssist(this))
                {
                    assist.AssistAttack(this, source, victim);
                    break;
                }
        }
    }

    /// <inheritdoc cref="IWorldServices.PlayerIds"/>
    public IReadOnlyList<PlayerId> PlayerIds => [.._playerList.Select(player => player.Id).OrderBy(id => id.Value)];

    /// <inheritdoc cref="IWorldServices.SetUnitsVisionSpied"/>
    public void SetUnitsVisionSpied(PlayerId victim, bool setting, ImmutableArray<string> kinds, PlayerId byWhom)
    {
        foreach (var entity in _entities.Values)
        {
            if (entity.Owner != victim || (kinds.Length > 0 && !kinds.Any(entity.Template.IsKindOf))) continue;
            var spied = entity.VisionSpiedBy ??= [];
            spied.TryGetValue(byWhom.Value, out var count);
            spied[byWhom.Value] = count + (setting ? 1 : -1);
            MarkDirty(entity);
        }
    }
}
