using CnCNew.Simulation.Ids;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>
/// <c>ContainModuleInterface::isEnclosingContainerFor</c>: whether a container takes a rider out of the world (<c>OpenContain::addToContain</c> removes it from the partition and hides its
/// drawable only then). Every container encloses (<c>OpenContain</c>'s default) but a parachute (nobody), a garrison with <c>IsEnclosingContainer = No</c> (the fire bases, whose
/// riders stand on its stations), a Helix its portable structure (<c>HelixContain.cpp</c>) and an Overlord its first rider (<c>OverlordContain.cpp</c>). The simulation asks it for the
/// stealth detector and the propaganda tower; the presentation for what it draws and hears.
/// </summary>
public static class Enclosure
{
    /// <summary>Whether <paramref name="container"/> encloses its rider <paramref name="rider"/>; false for an object that holds nothing (it has no contain module).</summary>
    public static bool Encloses(Entity container, EntityId rider)
    {
        foreach (var module in container.Modules)
            switch (module)
            {
                case GarrisonContain { IsEnclosing: false }: return false;
                case HelixContain helix when helix.PortableStructure == rider: return false;
                case OverlordContain overlord when overlord.FirstRider == rider: return false;
                case ParachuteContain: return false;
            }
        return container.Container is not null;
    }

    /// <summary>Whether the object's own container encloses it (false for an object in nothing).</summary>
    public static bool IsEnclosed(IWorldServices world, Entity entity)
        => entity.ContainedBy.IsValid && world.FindEntity(entity.ContainedBy) is { } container && Encloses(container, entity.Id);

    /// <summary>
    /// <c>Object::getEnclosingContainedBy</c>: whether any container up the chain encloses the object below it - a portable structure on an Overlord in the world is out, a soldier
    /// in that structure's bunker is in. The walk stops after eight containers.
    /// </summary>
    public static bool IsEnclosedAnywhere(IWorldServices world, Entity entity)
    {
        var child = entity;
        for (var depth = 0; depth < 8 && child.ContainedBy.IsValid && world.FindEntity(child.ContainedBy) is { } container; depth++)
        {
            if (Encloses(container, child.Id)) return true;
            child = container;
        }
        return false;
    }
}
