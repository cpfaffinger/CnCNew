using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.World;

/// <summary>
/// The choice of a victim by damage potential (rule fix <see cref="Rules.GameFix.TargetByDamagePotential"/>, the project owner's decision; docs/SIMULATION-CORE.md §87).
/// <c>AIUpdateInterface::getNextMoodTarget</c> and <c>AIGuardMachine::lookForInnerTarget</c> take the closest enemy the unit can attack (<c>AI::findClosestEnemy</c>). With the
/// fix a unit that picks its own victim ranks the enemies it may attack - the same candidates, the same range and filters - by what it can do to them and what they can do to it,
/// and keeps the distance for the last say:
/// <list type="number">
/// <item>the data's attack priority (a script's attack-priority set), as before;</item>
/// <item><b>value</b>: the efficiency of the unit's best weapon against the victim's armour (the share of the weapon's damage that gets through), half the share of the victim's
/// remaining health the weapon takes off in a second, the victim's threat to the unit (the same two numbers the other way round, over ten seconds of the victim's fire), and a small
/// penalty for the distance;</item>
/// <item>the nearer one, then the lower id.</item>
/// </list>
/// Everything is <see cref="Fixed"/> and built from frames of the original, so the pick is the same at every tick rate; the order is a strict one over all candidates, so the order
/// in which they are met does not matter. An ordered attack names its victim and never comes here.
/// </summary>
public sealed partial class SimulationWorld
{
    /// <summary>Weight of the armour efficiency of the unit's best weapon against the victim.</summary>
    private static readonly Fixed ValueEfficiencyWeight = Fixed.One;

    /// <summary>Weight of the share of the victim's remaining health the unit's weapon removes in one second (capped at the whole health).</summary>
    private static readonly Fixed ValueKillRateWeight = Fixed.FromDecimal(0.5m);

    /// <summary>Weight of the victim's threat: how well its best weapon hurts this unit's armour, and how much of the unit's health it removes in <see cref="ThreatHorizonSeconds"/>.</summary>
    private static readonly Fixed ValueThreatWeight = Fixed.FromDecimal(0.5m);

    /// <summary>What a victim without any weapon is worth less than an armed one.</summary>
    private static readonly Fixed ValueUnarmedPenalty = Fixed.FromDecimal(1.5m);

    /// <summary>The distance counts for at most this much, in proportion to the acquisition radius: it orders near-equal values, it never sends a unit across the map.</summary>
    private static readonly Fixed ValueDistanceWeight = Fixed.FromDecimal(0.1m);

    /// <summary>A weapon that does more than its nominal damage against an armour (a flame at a human) is worth no more than this.</summary>
    private static readonly Fixed ValueEfficiencyCap = Fixed.FromDecimal(1.5m);

    /// <summary>A victim that removes the unit's whole health within this many seconds is a full threat.</summary>
    private const int ThreatHorizonSeconds = 10;

    private bool PicksByDamagePotential => _templates.Rules.Has(Rules.GameFix.TargetByDamagePotential);

    private struct VictimCandidate
    {
        public Entity Entity;
        public Fixed Gap;
        public int Effective;
        public int Priority;
    }

    /// <summary>The candidates of the search in progress (the search is not reentrant).</summary>
    private readonly List<VictimCandidate> _victimCandidates = [];

    /// <summary>
    /// The best of the collected candidates: highest effective priority, then priority, then value, then the nearer, then the lower id. One candidate is returned without being valued.
    /// </summary>
    private Entity? PickByDamagePotential(Entity looker, Fixed radius)
    {
        var candidates = _victimCandidates;
        if (candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0].Entity;
        var best = candidates[0];
        var bestValue = ValueOf(looker, best, radius);
        for (var index = 1; index < candidates.Count; index++)
        {
            var candidate = candidates[index];
            if (candidate.Effective < best.Effective || (candidate.Effective == best.Effective && candidate.Priority < best.Priority)) continue;
            var value = ValueOf(looker, candidate, radius);
            var better = candidate.Effective > best.Effective || candidate.Priority > best.Priority || value > bestValue
                         || (value == bestValue && IsNearer(candidate.Entity, candidate.Gap, best.Entity, best.Gap));
            if (!better) continue;
            best = candidate;
            bestValue = value;
        }

        return best.Entity;
    }

    /// <summary>
    /// The parts of the value <paramref name="looker"/> gives <paramref name="victim"/> when it ranks its candidates (rule fix <see cref="Rules.GameFix.TargetByDamagePotential"/>), for
    /// tests and tools: the efficiency of its best weapon, the share of the victim's health that weapon takes off in a second, the victim's threat to the looker, and their sum at no distance.
    /// </summary>
    public (Fixed Efficiency, Fixed KillRate, Fixed Threat, Fixed Value) TargetValueParts(Entity looker, Entity victim)
    {
        var (efficiency, killRate) = DamagePotential(looker, victim, 1);
        var threat = ThreatOf(victim, looker);
        return (efficiency, killRate, threat, ValueWithoutDistance(victim, efficiency, killRate, threat));
    }

    private Fixed ValueOf(Entity looker, in VictimCandidate candidate, Fixed radius)
    {
        var (efficiency, killRate) = DamagePotential(looker, candidate.Entity, 1);
        var value = ValueWithoutDistance(candidate.Entity, efficiency, killRate, ThreatOf(candidate.Entity, looker));
        if (radius > Fixed.Zero) value -= Fixed.Min(Fixed.One, candidate.Gap / radius) * ValueDistanceWeight;
        return value;
    }

    private Fixed ValueWithoutDistance(Entity victim, Fixed efficiency, Fixed killRate, Fixed threat)
    {
        var value = efficiency * ValueEfficiencyWeight + killRate * ValueKillRateWeight + threat * ValueThreatWeight;
        // A worker, a dozer, a depot: whatever shoots comes first, so the unit does not spend itself on a target that is no danger to anything.
        return IsArmed(victim) ? value : value - ValueUnarmedPenalty;
    }

    /// <summary>The enemy has a weapon of its own, or riders that fire from it.</summary>
    private bool IsArmed(Entity enemy)
    {
        if (FightsWithWeapon(enemy)) return true;
        if (enemy.Container is { ContainCount: > 0 } inside)
            foreach (var id in inside.Contained)
                if (FindEntity(id) is { EffectivelyDead: false } rider && ContainerLetsFire(rider) && FightsWithWeapon(rider)) return true;
        return false;
    }

    /// <summary>A weapon that does damage to a living victim - not the builders' mine-clearing weapon, not a weapon without damage.</summary>
    private bool FightsWithWeapon(Entity unit)
    {
        if (ActiveWeaponSet(unit) is not { } set) return false;
        foreach (var index in set.Weapons)
            if (index >= 0 && _templates.Weapons[index] is { } weapon && weapon.PrimaryDamage > Fixed.Zero && weapon.AntiMask != WeaponInfo.AntiMaskMine) return true;
        return false;
    }

    /// <summary>
    /// How dangerous <paramref name="enemy"/> is to <paramref name="unit"/>: the best of its own weapon (and, for a garrison, its riders' weapons) by efficiency against the unit's armour
    /// and by the share of the unit's health it takes off in <see cref="ThreatHorizonSeconds"/>. 0 for an enemy that cannot hurt the unit (a tank at a helicopter, a building without a gun).
    /// </summary>
    private Fixed ThreatOf(Entity enemy, Entity unit)
    {
        var best = ThreatFrom(enemy, unit);
        if (enemy.Container is { ContainCount: > 0 } inside)
            foreach (var id in inside.Contained)
                if (FindEntity(id) is { EffectivelyDead: false } rider && ContainerLetsFire(rider)) best = Fixed.Max(best, ThreatFrom(rider, unit));
        return best;
    }

    private Fixed ThreatFrom(Entity enemy, Entity unit)
    {
        var (efficiency, killRate) = DamagePotential(enemy, unit, ThreatHorizonSeconds);
        return Fixed.Min(Fixed.One, efficiency) * killRate;
    }

    /// <summary>
    /// What <paramref name="source"/>'s best weapon against <paramref name="victim"/> (the slot <see cref="AttackSlotIgnoringRange"/> picks: a pinned one, a preferred one, else the most
    /// damage) does, ignoring range and reload: the <b>efficiency</b>, the share of the weapon's nominal damage that the victim's armour lets through, and the <b>kill rate</b>, the share
    /// of the victim's remaining health that the weapon takes off in <paramref name="seconds"/> of steady fire (a clip and its reload included), at most the whole. Both 0 for no weapon
    /// that can hurt the victim.
    /// </summary>
    private (Fixed Efficiency, Fixed KillRate) DamagePotential(Entity source, Entity victim, int seconds)
    {
        var slot = AttackSlotIgnoringRange(source, victim);
        if (slot < 0 || ActiveWeaponSet(source) is not { } set) return default;
        var weapon = _templates.Weapons[set.Weapons[slot]];
        var adjusted = EstimateDamage(source, weapon, victim);
        if (adjusted <= Fixed.Zero) return default;
        var raw = weapon.DamageWith(_templates.GlobalWeaponBonus, WeaponBonusOf(source), weapon.PrimaryDamage);
        var efficiency = raw > Fixed.Zero ? Fixed.Min(ValueEfficiencyCap, adjusted / raw) : Fixed.One;

        // Steady fire in frames of the original: a clip of n shots takes n - 1 delays and a reload, a weapon without a clip one delay per shot.
        var frame = Time.TicksPerOriginalFrame;
        var delay = Math.Max(frame, weapon.DelayBetweenShotsTicks);
        var shots = Math.Max(1, weapon.ClipSize);
        var cycleTicks = weapon.ClipSize > 0 ? (weapon.ClipSize - 1) * delay + Math.Max(delay, weapon.ClipReloadTicks) : delay;
        var cycleLength = cycleTicks / frame;
        if (cycleLength < 1) cycleLength = 1;
        var health = victim.Body?.Health ?? Fixed.Zero;
        if (health <= Fixed.Zero) return (efficiency, Fixed.One);
        // damage in the window = adjusted * shots * (window frames / cycle frames); the share of the health is that over the health.
        var window = (long)seconds * SimulationRate.OriginalFramesPerSecond;
        var taken = Fixed.MultiplyDivide(adjusted, Fixed.FromInt(shots * window), Fixed.FromInt(cycleLength));
        return (efficiency, Fixed.Min(Fixed.One, taken / health));
    }
}
