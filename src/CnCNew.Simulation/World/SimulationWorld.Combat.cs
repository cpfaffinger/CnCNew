using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.Templates;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.World;

public sealed partial class SimulationWorld
{
    // Damage.h HUGE_DAMAGE_AMOUNT.
    private static readonly Fixed HugeDamage = Fixed.FromInt(999999);

    /// <summary>
    /// <c>WeaponTemplate::dealDamageInternal</c> for a weapon without a projectile: damages the <paramref name="primaryVictim"/> (when
    /// there is one, its position replaces the given one) and everything within the larger of the primary and secondary radius, measured
    /// like <c>DAMAGE_RANGE_CALC_TYPE = FROM_BOUNDINGSPHERE_3D</c> (centre distance minus the victim's bounding sphere, clamped at 0).
    /// A victim within the primary radius takes the primary damage, one further out the secondary damage. The primary victim ignores the
    /// <c>RadiusDamageAffects</c> filters; every other victim is filtered (self, similar, airborne, relationship). A weapon without any
    /// radius hurts only the primary victim. Victims are visited in entity-id order (the reference iterates the partition cells).
    /// Returns the armor-adjusted damage dealt to the primary victim (0 without one). Not modelled: <c>RadiusDamageAngle</c> cones,
    /// shockwaves, damage status, historic damage.
    /// </summary>
    public Fixed DealWeaponDamage(Entity source, WeaponInfo weapon, Entity? primaryVictim, Fixed x, Fixed y, Fixed z)
        => DealDamageCore(source.Id, source, EntityId.Invalid, source.Id, WeaponBonusOf(source), weapon, primaryVictim, x, y, z);

    /// <summary>
    /// The shared body of <c>dealDamageInternal</c>. <paramref name="sourceId"/> must be valid (the reference returns without one);
    /// <paramref name="source"/> may be gone by now (a delayed shot whose firer died) and then, exactly like the reference, no
    /// <c>RadiusDamageAffects</c> filter applies at all. <paramref name="producer"/> is the launcher of a projectile: the self filter spares it
    /// (<c>source->getProducerID() == curVictim->getID()</c>) and every damage is credited to <paramref name="damageSource"/>
    /// (<c>projectileGetLauncherID</c>). <paramref name="conditions"/> are the weapon bonus conditions the shot was fired with.
    /// </summary>
    private Fixed DealDamageCore(EntityId sourceId, Entity? source, EntityId producer, EntityId damageSource, uint conditions, WeaponInfo weapon,
        Entity? primaryVictim, Fixed x, Fixed y, Fixed z)
    {
        if (!sourceId.IsValid) return Fixed.Zero;
        var bonuses = _templates.GlobalWeaponBonus;
        // Above 30 Hz everybody is hit where the logic frame of the original it has begun ends (docs/rate-invariance/RATE-INVARIANCE-CONSOLIDATION.md §1.3, rule 3): the 30 Hz blast finds
        // an object that already had its turn in the tick after its whole step, not a share of the way there. Nobody is inside a frame's step at 30 Hz.
        var frameEnd = _firstFrameTicks != TickOrder.Next;
        if (primaryVictim is not null)
        {
            x = primaryVictim.X;
            y = primaryVictim.Y;
            z = primaryVictim.Z;
            if (frameEnd) (x, y, z) = primaryVictim.FrameEnd(_tick);
        }

        var primaryRadius = weapon.RadiusWith(bonuses, conditions, weapon.PrimaryRadius);
        var secondaryRadius = weapon.RadiusWith(bonuses, conditions, weapon.SecondaryRadius);
        var primaryDamage = weapon.DamageWith(bonuses, conditions, weapon.PrimaryDamage);
        var secondaryDamage = weapon.DamageWith(bonuses, conditions, weapon.SecondaryDamage);
        var affects = weapon.AffectsMask;
        var radius = Fixed.Max(primaryRadius, secondaryRadius);

        var victims = new List<(Entity Victim, Fixed DistanceSquared)>();
        if (radius > Fixed.Zero)
        {
            var radiusSquared = SquaredReach.Of(radius);
            foreach (var candidate in _entities.Values)
            {
                if (candidate.DestroyRequested || candidate.Body is null) continue;
                if (candidate.ContainedBy.IsValid && candidate != primaryVictim) continue;
                var dx = candidate.X - x;
                var dy = candidate.Y - y;
                var dz = candidate.Z + candidate.Template.CenterZDelta - z;
                if (frameEnd && candidate.IsInsideFrameStep)
                {
                    var (endX, endY, endZ) = candidate.FrameEnd(_tick);
                    dx = endX - x;
                    dy = endY - y;
                    dz = endZ + candidate.Template.CenterZDelta - z;
                }
                var distanceSquared = dx * dx + dy * dy + dz * dz;
                var sphere = candidate.Template.BoundingSphereRadius;
                var shrunkSquared = Fixed.Zero;
                if (sphere > Fixed.Zero)
                {
                    var reach = radius + sphere;
                    if (!SquaredReach.Of(reach).Contains(distanceSquared)) continue;
                    var shrunk = Fixed.Sqrt(distanceSquared) - sphere;
                    if (shrunk > Fixed.Zero) shrunkSquared = shrunk * shrunk;
                }
                else shrunkSquared = distanceSquared;
                if (radiusSquared.ContainsStrictly(shrunkSquared)) victims.Add((candidate, shrunkSquared));
            }
            victims.Sort((a, b) => a.Victim.Id.Value.CompareTo(b.Victim.Id.Value));
        }
        else
        {
            if ((affects & WeaponInfo.KillsSelf) != 0)
            {
                if (source is not null) Damage(source.Id, new DamageInfo(damageSource, HugeDamage, weapon.DamageType, weapon.DeathType, false));
                return Fixed.Zero;
            }
            if (primaryVictim is not null && !primaryVictim.DestroyRequested) victims.Add((primaryVictim, Fixed.Zero));
        }

        var primaryRadiusSquared = SquaredReach.Of(primaryRadius);
        var dealt = Fixed.Zero;
        foreach (var (victim, distanceSquared) in victims)
        {
            var killSelf = false;
            if (source is not null && victim != primaryVictim)
            {
                if ((affects & WeaponInfo.KillsSelf) != 0 && victim == source) killSelf = true;
                else
                {
                    if ((affects & WeaponInfo.AffectsSelf) == 0 && (victim == source || (producer.IsValid && victim.Id == producer))) continue;
                    if ((affects & WeaponInfo.NotSimilar) != 0 && victim.Template.Index == source.Template.Index
                        && RelationshipOf(source.Owner, victim.Owner) == Relationship.Allies) continue;
                    if ((affects & WeaponInfo.NotAirborne) != 0 && IsSignificantlyAboveTerrainAtFrameEnd(victim)) continue;
                    var required = RelationshipBetween(victim, source) switch
                    {
                        Relationship.Allies => WeaponInfo.AffectsAllies,
                        Relationship.Enemies => WeaponInfo.AffectsEnemies,
                        _ => WeaponInfo.AffectsNeutrals,
                    };
                    if ((affects & required) == 0) continue;
                }
            }

            var amount = killSelf ? HugeDamage : primaryRadiusSquared.Contains(distanceSquared) ? primaryDamage : secondaryDamage;
            // The body applies the armour (ActiveBody::attemptDamage); the value returned is what it would come to.
            if (victim == primaryVictim) dealt = ActiveArmor(victim).Adjust(weapon.DamageType, amount);
            Damage(victim.Id, new DamageInfo(damageSource, amount, weapon.DamageType, weapon.DeathType, false));
        }
        return dealt;
    }
}
