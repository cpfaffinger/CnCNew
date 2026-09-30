using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.World;

namespace CnCNew.Simulation.Views;

/// <summary>
/// Read-only accessors for the presentation layer (model conditions, supply boxes). They expose state that lives behind
/// assembly-internal module types without changing any behaviour; nothing here mutates an entity or the world.
/// </summary>
public static class EntityPresentationView
{
    /// <summary>The body's <c>BodyDamageType</c> (<see cref="BodyDamageStates"/>); bodies without damage states report PRISTINE.</summary>
    public static int DamageState(this Entity entity) => entity.Body is ActiveBody body ? body.DamageState : BodyDamageStates.Pristine;

    /// <summary>Supply boxes a carrier (supply truck, worker, Chinook) currently holds; 0 for everything else.</summary>
    public static int CarriedSupplyBoxes(this Entity entity)
    {
        foreach (var module in entity.Modules)
            if (module is ISupplyCarrier carrier) return carrier.Boxes;
        return 0;
    }

    /// <summary>Boxes left in a supply warehouse (<c>SupplyWarehouseDockUpdate</c>); -1 when the entity is not a warehouse.</summary>
    public static int WarehouseSupplyBoxes(this Entity entity)
    {
        foreach (var module in entity.Modules)
            if (module is SupplyWarehouseDockUpdate warehouse) return warehouse.Boxes;
        return -1;
    }
}
