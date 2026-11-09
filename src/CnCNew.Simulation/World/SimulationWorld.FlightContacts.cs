using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Numerics;

namespace CnCNew.Simulation.World;

/// <summary>
/// <c>PhysicsBehavior::onCollide</c> for an object in a physics flight (a shock wave, a creation list's throw, a fall): the partition looks at what it touches after its move, once per logic frame
/// of the original. "AI objects move under their own initiative, not by getting bounced" - an object with an AI meets something only when it is dead (its <c>processCollision</c> answers no for
/// anything without an AI and for anything while it is not on the ground), so what is met here is what a dead unit, a hulk or a piece of debris falls on. Only immobile objects are met: another
/// mover with a physics module gets a push the port does not give (a named difference), one without is insubstantial.
/// </summary>
public sealed partial class SimulationWorld
{
    /// <inheritdoc cref="IWorldServices.MeetWhatStands"/>
    /// <remarks>
    /// The test is the original's last one: spheres (bounding sphere radii, the centres raised by the geometry) while the object is above the ground, circles on it; the partition's shape test
    /// before it is not ported. The original meets its contacts in the partition's order; here in the order of the ids.
    /// </remarks>
    public FlightContact MeetWhatStands(Entity flyer, Fixed x, Fixed y, Fixed z, string buildingWeapon, string otherWeapon)
    {
        // PartitionManager::update: "if (collideEm && !dirty->getObject()->isKindOf(KINDOF_IMMOBILE)) addPossibleCollisions" - an immobile object never looks for contacts, however it moves. The Daisy Cutter's
        // gas and the fuel air bomb's are IMMOBILE and fall (setAllowToFall) over the building they were dropped on: met against it, they were destroyed with no death and no blast.
        if (flyer.Template.IsKindOf("IMMOBILE")) return default;
        if (flyer.DestroyRequested || !CanCollide(flyer) || (AiOf(flyer) is not null && !flyer.EffectivelyDead)) return default;
        var above = z > LayerHeightAt(x, y, flyer.Layer);
        var usRadius = above ? flyer.Template.BoundingSphereRadius : CircleRadius(flyer);
        var usCenterZ = z + flyer.Template.CenterZDelta;
        var vehicle = flyer.Template.IsKindOf("VEHICLE");
        FlightContact contact = default;
        foreach (var other in _entities)
        {
            if (other.Id == flyer.Id || !CanCollide(other) || !other.Template.IsKindOf("IMMOBILE") || other.Id == flyer.ContainedBy) continue;
            var themRadius = above ? other.Template.BoundingSphereRadius : CircleRadius(other);
            var limit = usRadius + themRadius;
            if (FurtherThan(other.X, x, limit) || FurtherThan(other.Y, y, limit)) continue;
            var (dx, dy) = (other.X - x, other.Y - y);
            var dz = above ? other.Z + other.Template.CenterZDelta - usCenterZ : Fixed.Zero;
            if (dz.Abs() > limit) continue;
            var squared = dx * dx + dy * dy + dz * dz;
            if (!SquaredReach.Of(limit).Contains(squared)) continue;
            // "if we are moving down, we may want to blow ourselves into smithereens...."
            if (dz < Fixed.Zero && z >= _templates.DefaultStructureRubbleHeight)
            {
                if (other.Template.IsKindOf("STRUCTURE"))
                {
                    // "fall into a building. if a vehicle, blow up. then destroy ourself (not die), regardless."
                    if (vehicle) FireTempWeapon(flyer, buildingWeapon, x, y, z);
                    RequestDestroy(flyer.Id);
                    return new FlightContact(true, true, Fixed.Zero, Fixed.Zero, Fixed.Zero);
                }
                // "fall into a nonbuilding -- whatever. if we're a vehicle, quietly do a little damage."
                if (vehicle) FireTempWeapon(flyer, otherWeapon, x, y, z);
            }
            if (contact.Met) continue;
            // "if objects are coincident, dist is zero, so force would be infinite -- clearly not what we want. so just cap it here for now."
            var distance = Fixed.Max(Fixed.Sqrt(squared), Fixed.One);
            contact = new FlightContact(true, false, -dx / distance, -dy / distance, -dz / distance);
        }
        return contact;
    }
}
