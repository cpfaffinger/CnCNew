using CnCNew.Simulation.Ids;
using CnCNew.Simulation.State;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.World;

/// <summary>The continuous-fire stage of an object (<c>MODELCONDITION_CONTINUOUS_FIRE_SLOW/MEAN/FAST</c>; mean and fast are also its weapon bonus conditions).</summary>
public enum ContinuousFireStage : byte { None, Slow, Mean, Fast }

/// <summary>
/// <c>FiringTracker</c>'s counters for one object: how many shots in a row went at the same victim and when the stages start to drop. Exists only for objects that fired a weapon with
/// continuous-fire stages, and only while something is set, so every other object keeps its serialized form.
/// </summary>
internal sealed class FiringState
{
    /// <summary><c>m_consecutiveShots</c>.</summary>
    public int Shots;

    /// <summary><c>m_victimID</c>; invalid for a ground shot or none.</summary>
    public EntityId Victim;

    /// <summary><c>m_frameToStartCooldown</c>: once the tick is past it the stage drops; 0 for no timer.</summary>
    public long CooldownFrame;

    /// <summary><c>MODELCONDITION_CONTINUOUS_FIRE_SLOW</c>: the weapon spins down. A model condition only; mean and fast are weapon bonus conditions of the object.</summary>
    public bool Slow;

    public bool IsDefault => Shots == 0 && !Victim.IsValid && CooldownFrame == 0 && !Slow;

    public void Describe(IStateWriter writer)
    {
        writer.BeginObject("firing");
        writer.Int("shots", Shots);
        writer.UInt("victim", Victim.Value);
        writer.Int("cooldownFrame", CooldownFrame);
        writer.Bool("slow", Slow);
        writer.EndObject();
    }

    public static FiringState Load(IStateReader reader)
    {
        var state = new FiringState
        {
            Shots = checked((int)reader.Int("shots")),
            Victim = new EntityId(checked((uint)reader.UInt("victim"))),
            CooldownFrame = reader.Int("cooldownFrame"),
            Slow = reader.Bool("slow"),
        };
        if (state.Shots < 0 || state.CooldownFrame < 0) throw new InvalidDataException("Negative firing tracker counter in snapshot.");
        return state;
    }
}

/// <summary>
/// <c>FiringTracker.cpp</c>: consecutive shots at one victim spin a weapon up. After more than <c>ContinuousFireOne</c> shots the object gets the weapon bonus condition
/// <c>CONTINUOUS_FIRE_MEAN</c>, after more than <c>ContinuousFireTwo</c> <c>CONTINUOUS_FIRE_FAST</c> (exclusive, not cumulative); the weapon's <c>WeaponBonus</c> lines for those
/// conditions (the Gattling weapons: <c>RATE_OF_FIRE</c> 200 % and 400 %) then shorten the delay between its shots. A change of victim within <c>ContinuousFireCoast</c> of the next
/// possible shot keeps the count; once that time has passed without a shot the stage drops straight to none (showing <c>CONTINUOUS_FIRE_SLOW</c> for a second).
/// The looping fire sound, <c>AutoReloadWhenIdle</c> and the <c>TARGET_FAERIE_FIRE</c> bonus of the reference's tracker are not part of this.
/// </summary>
public sealed partial class SimulationWorld
{
    private const uint ContinuousFireMean = 1u << WeaponBonusTable.ContinuousFireMeanCondition;
    private const uint ContinuousFireFast = 1u << WeaponBonusTable.ContinuousFireFastCondition;

    /// <summary>Ids (ascending) of the objects whose tracker has a cooldown timer running.</summary>
    private readonly SortedSet<uint> _coolingTrackers = [];

    /// <summary>The stage the object's weapons are spun up to.</summary>
    public static ContinuousFireStage ContinuousFireOf(Entity entity)
    {
        if ((entity.WeaponBonusConditions & ContinuousFireFast) != 0) return ContinuousFireStage.Fast;
        if ((entity.WeaponBonusConditions & ContinuousFireMean) != 0) return ContinuousFireStage.Mean;
        return entity.Firing is { Slow: true } ? ContinuousFireStage.Slow : ContinuousFireStage.None;
    }

    /// <summary>
    /// <c>FiringTracker::shotFired</c>, called after the shot's own delay was computed (<c>Object::fireCurrentWeapon</c> notifies the tracker after <c>Weapon::fireWeapon</c>), so a new
    /// stage speeds up the following shots, not this one.
    /// </summary>
    private void NoteShotFired(Entity source, WeaponInfo weapon, int slot, EntityId victim)
    {
        var state = source.Firing;
        if (state is null)
        {
            // A weapon without stages never changes anything on an object that has no tracker state.
            if (weapon.ContinuousFireOneShots == int.MaxValue && weapon.ContinuousFireTwoShots == int.MaxValue) return;
            source.Firing = state = new FiringState();
        }

        if (victim == state.Victim) state.Shots++;
        else if (_tick < state.CooldownFrame)
        {
            // "Switching targets within the coast time is valid, and we will not spin down"
            state.Shots++;
            state.Victim = victim;
        }
        else
        {
            state.Shots = 1;
            state.Victim = victim;
        }

        // Weapon::getPossibleNextShotFrame plus the coast time; a weapon that reloads at its airfield has no next shot to coast from.
        var nextShot = source.ReadyTickOf(slot);
        state.CooldownFrame = weapon.ContinuousFireCoastTicks != 0 ? (nextShot >= SimulationTiming.SleepForever ? _tick : nextShot) + weapon.ContinuousFireCoastTicks : 0;

        if ((source.WeaponBonusConditions & ContinuousFireMean) != 0)
        {
            // Can either go up or down from here.
            if (state.Shots < weapon.ContinuousFireOneShots) CoolDown(source, state);
            else if (state.Shots > weapon.ContinuousFireTwoShots) SpeedUp(source, state);
        }
        else if ((source.WeaponBonusConditions & ContinuousFireFast) != 0)
        {
            // The only way from here is all the way down.
            if (state.Shots < weapon.ContinuousFireTwoShots) CoolDown(source, state);
        }
        else if (state.Shots > weapon.ContinuousFireOneShots) SpeedUp(source, state);

        if (state.CooldownFrame != 0) _coolingTrackers.Add(source.Id.Value);
        MarkDirty(source);
    }

    /// <summary><c>FiringTracker::speedUp</c>: none → mean → fast.</summary>
    private static void SpeedUp(Entity source, FiringState state)
    {
        if ((source.WeaponBonusConditions & ContinuousFireFast) != 0) return;
        source.WeaponBonusConditions = (source.WeaponBonusConditions & ContinuousFireMean) != 0
            ? (source.WeaponBonusConditions & ~ContinuousFireMean) | ContinuousFireFast
            : source.WeaponBonusConditions | ContinuousFireMean;
        state.Slow = false;
    }

    /// <summary><c>FiringTracker::coolDown</c>: "straight to zero from wherever it is" (showing the slow stage), and from there the tracker stops.</summary>
    private static void CoolDown(Entity source, FiringState state)
    {
        if ((source.WeaponBonusConditions & (ContinuousFireMean | ContinuousFireFast)) != 0)
        {
            source.WeaponBonusConditions &= ~(ContinuousFireMean | ContinuousFireFast);
            state.Slow = true;
        }
        else
        {
            state.Slow = false;
            state.CooldownFrame = 0;
        }

        state.Shots = 0;
        state.Victim = EntityId.Invalid;
    }

    /// <summary><c>FiringTracker::update</c>: once the coast time is over the stage drops, a second later the slow stage ends. In id order.</summary>
    private void StepFiringTrackers(long tick)
    {
        if (_coolingTrackers.Count == 0) return;
        List<uint>? finished = null;
        foreach (var id in _coolingTrackers)
        {
            if (!_entities.TryGetValue(id, out var entity) || entity.Firing is not { } state || state.CooldownFrame == 0)
            {
                (finished ??= []).Add(id);
                continue;
            }

            if (tick <= state.CooldownFrame) continue;
            state.CooldownFrame = tick + SimulationTiming.LogicFramesPerSecond;
            CoolDown(entity, state);
            MarkDirty(entity);
            if (state.CooldownFrame != 0) continue;
            (finished ??= []).Add(id);
            if (state.IsDefault) entity.Firing = null;
        }

        if (finished is null) return;
        foreach (var id in finished) _coolingTrackers.Remove(id);
    }

    private void RegisterFiringTracker(Entity entity)
    {
        if (entity.Firing is { CooldownFrame: not 0 }) _coolingTrackers.Add(entity.Id.Value);
    }
}
