using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Commands;
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

    /// <summary>The slot whose weapon winds up (<c>Weapon::m_whenPreAttackFinished</c> of the reference lives on the weapon); -1 for none.</summary>
    public int PreSlot = -1;

    /// <summary>The victim the wind-up is for.</summary>
    public EntityId PreVictim;

    /// <summary>The first tick the wound-up weapon may fire.</summary>
    public long PreFinish;

    public bool CountersAtRest => Shots == 0 && !Victim.IsValid && CooldownFrame == 0 && !Slow;

    public bool IsDefault => CountersAtRest && PreSlot < 0;

    public void ClearPreAttack()
    {
        PreSlot = -1;
        PreVictim = EntityId.Invalid;
        PreFinish = 0;
    }

    public void Describe(IStateWriter writer)
    {
        writer.BeginObject("firing");
        writer.Int("shots", Shots);
        writer.UInt("victim", Victim.Value);
        writer.Int("cooldownFrame", CooldownFrame);
        writer.Bool("slow", Slow);
        if (PreSlot >= 0)
        {
            writer.Int("preSlot", PreSlot);
            writer.UInt("preVictim", PreVictim.Value);
            writer.Int("preFinish", PreFinish);
        }
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
        if (reader.Has("preSlot"))
        {
            state.PreSlot = checked((int)reader.Int("preSlot"));
            state.PreVictim = new EntityId(checked((uint)reader.UInt("preVictim")));
            state.PreFinish = reader.Int("preFinish");
            if (state.PreSlot is < 0 or >= WeaponSlot.Count || state.PreFinish < 0) throw new InvalidDataException("Pre-attack state out of range in snapshot.");
        }
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
        state?.ClearPreAttack();
        // Only weapons with stages, or with a wind-up that depends on the shots at the victim, need the counters; for every other weapon an object without counters stays without.
        var counted = weapon.ContinuousFireOneShots != int.MaxValue || weapon.ContinuousFireTwoShots != int.MaxValue
                      || (weapon.PreAttackDelayTicks != 0 && weapon.PreAttackType == WeaponPrefire.PerAttack);
        if (!counted && (state is null || state.CountersAtRest))
        {
            if (state is not null) source.Firing = null;
            return;
        }

        state ??= source.Firing = new FiringState();

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

    // ------------------------------------------------------------------------------------------------------ the wind-up before a shot

    /// <summary>A wind-up nobody followed up on (the attack was dropped without an order the simulation sees) is forgotten after this long past its end.</summary>
    private const long PreAttackStaleFrames = SimulationRate.OriginalFramesPerSecond;

    /// <summary>
    /// <c>Object::preFireCurrentWeapon</c> / <c>Weapon::preFireWeapon</c> and the wait of <c>AIAttackFireWeaponState::update</c> (<c>PRE_ATTACK</c> → continue): a weapon with a
    /// <c>PreAttackDelay</c> that is otherwise ready to fire at <paramref name="victim"/> first winds up for the delay — every shot (<c>PER_SHOT</c>), the first shot at a victim
    /// (<c>PER_ATTACK</c>: no consecutive shot at it yet) or the first shot of a clip (<c>PER_CLIP</c>). False while the weapon winds up: the fire order stays accepted and a later
    /// order of the attack fires. The wind-up is dropped when the unit gets another order (<see cref="EndAttack"/>, the reference's <c>onExit</c>) or aims at another victim.
    /// </summary>
    private bool PreAttackDone(Entity source, WeaponInfo weapon, int slot, Entity victim) => PreAttackDone(source, weapon, slot, victim.Id);

    /// <summary><see cref="PreAttackDone(Entity, WeaponInfo, int, Entity)"/> for a victim by id - invalid for the place of an attack on a place (F-64).</summary>
    private bool PreAttackDone(Entity source, WeaponInfo weapon, int slot, EntityId victimId)
    {
        if (weapon.PreAttackDelayTicks == 0) return true;
        var state = source.Firing;
        // "Until and including" the last tick of the grace is one step more than the grace; that step is a logic frame of the original at every rate (as in StepFiringTrackers).
        if (state is not null && state.PreSlot == slot && state.PreVictim == victimId && _tick < state.PreFinish + Time.OriginalFrames(PreAttackStaleFrames) + _turretFrameTicks) return _tick >= state.PreFinish;

        // Weapon::getPreAttackDelay: "Look for a reason to return zero and have no delay."
        if (weapon.PreAttackType == WeaponPrefire.PerClip && weapon.ClipSize > 0 && source.ClipOf(slot) is var clip && clip != 0 && clip < weapon.ClipSize) return true;
        if (weapon.PreAttackType == WeaponPrefire.PerAttack && state is not null && state.Victim == victimId && state.Shots > 0) return true;
        var delay = weapon.PreAttackDelayWith(Time, _templates.GlobalWeaponBonus, WeaponBonusOf(source));
        if (delay <= 0) return true;

        state ??= source.Firing = new FiringState();
        state.PreSlot = slot;
        state.PreVictim = victimId;
        state.PreFinish = _tick + delay;
        MarkDirty(source);
        return false;
    }

    /// <summary>The slot whose weapon is winding up right now (<c>MODELCONDITION_PREATTACK_A/B/C</c>), or -1.</summary>
    public int PreAttackSlotOf(Entity entity) => entity.Firing is { PreSlot: >= 0 } state && _tick <= state.PreFinish ? state.PreSlot : -1;

    private void EndPreAttack(Entity entity)
    {
        if (entity.Firing is not { PreSlot: >= 0 } state) return;
        state.ClearPreAttack();
        if (state.IsDefault) entity.Firing = null;
        MarkDirty(entity);
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

            // FiringTracker::update acts in the step after the deadline. That step is a logic frame of the original: coast + 1 frames after the next possible shot, not coast + 1 tick.
            if (tick < state.CooldownFrame + _turretFrameTicks) continue;
            state.CooldownFrame = tick + Time.TicksPerSecond;
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
