using CnCNew.Simulation.Events;
using CnCNew.Simulation.Ids;

namespace CnCNew.Simulation.World;

public sealed partial class SimulationWorld
{
    /// <summary>
    /// <c>aiEvacuate</c> reaching its end (<c>AIEvacuateState</c>/<c>removeAllContained</c>): every passenger leaves through <paramref name="container"/>, in
    /// contain-list order. A tunnel lets everybody in its owner's network out here; the tunnel module places them around itself.
    /// </summary>
    public void EvacuateContainer(Entity container)
    {
        if (container.Container is not { } contain || container.DestroyRequested) return;
        foreach (var riderId in contain.Contained.ToArray())
        {
            var rider = FindEntity(riderId);
            if (rider is null) continue;
            // A living transport lets its riders out through its door, one after the other (OpenContain::orderAllPassengersToExit -> aiExit of every rider).
            if (contain.ExitViaDoor(this, rider)) continue;
            contain.Remove(this, rider, true);
            rider.ContainedBy = EntityId.Invalid;
            MarkDirty(rider);
            Emit(SimEventKind.EntityUncontained, rider.Id, container.Id, string.Empty, 0);
        }
        MarkDirty(container);
    }
}
