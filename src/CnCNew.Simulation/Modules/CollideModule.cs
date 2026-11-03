using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Modules;

/// <summary>The three orders to enter an object that end in its collide module instead of a container (<c>ActionManager::canHijackVehicle</c> and its two neighbours).</summary>
public enum CollideEntry : byte
{
    None,
    Hijack,
    CarBomb,
    Sabotage,

    /// <summary>The target is an unmanned vehicle, which any infantry may take over (the actor needs no collide module for it).</summary>
    Unmanned,
}

/// <summary>
/// <c>CollideModuleInterface</c>: a collide module is told about the other object it touched (<c>onCollide</c>). The reference gets these calls from the
/// physics partition manager (<c>PartitionManager::checkForCollisions</c> calls <c>obj->onCollide(other)</c> and then <c>other->onCollide(obj)</c>);
/// this simulation has no physics, so <c>SimulationWorld.ResolveFootprintCollisions</c> delivers the same pair of calls whenever a ground mover's
/// footprint overlaps another object's after a step, and an approach order (<c>AIUpdateInterface.OrderApproachGoal</c>) delivers the mover's side once
/// on arrival. <c>Object::onCollide</c> stops asking an object's modules as soon as the object carries <c>NO_COLLISIONS</c>.
/// </summary>
public interface ICollideModule
{
    void OnCollide(IWorldServices world, Entity other);

    /// <summary><c>isHijackedVehicleCrateCollide</c>, <c>isCarBombCrateCollide</c>, <c>isSabotageBuildingCrateCollide</c>: what an order to enter means to this module.</summary>
    CollideEntry Entry => CollideEntry.None;

    /// <summary><c>CollideModuleInterface::wouldLikeToCollideWith</c>: whether touching <paramref name="other"/> would do something (the AI uses it to pick targets); false for modules that never ask for one.</summary>
    bool WouldLikeToCollideWith(IWorldServices world, Entity other) => false;
}
