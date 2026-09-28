using System.Collections.Immutable;
using CnCNew.Data.Registry;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>
/// The explicit binding list of the phase-4 modules, sorted ordinally by name. Selection by use in the shipped data
/// (16,360 module uses; see docs/SIMULATION-CORE.md for the measured table): the five body types cover every object with
/// health, DestroyDie/FXListDie/CreateObjectDie/InstantDeathBehavior/KeepObjectDie are the most frequent die modules that
/// need no other subsystem, and LifetimeUpdate/DeletionUpdate are the timed-expiry updates.
/// </summary>
public static class BuiltInModules
{
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
    [
        new ModuleBinding<BodyData>("ActiveBody", ModuleOwnerKind.Object, BodyData.Parse, data => new ActiveBody(data)),
        new ModuleBinding<CreateObjectDieData>("CreateObjectDie", ModuleOwnerKind.Object, CreateObjectDieData.Parse, data => new CreateObjectDie(data)),
        new ModuleBinding<LifetimeData>("DeletionUpdate", ModuleOwnerKind.Object, LifetimeData.Parse, data => new DeletionUpdate(data)),
        new ModuleBinding<PlainDieData>("DestroyDie", ModuleOwnerKind.Object, PlainDieData.Parse, data => new DestroyDie(data)),
        new ModuleBinding<FxListDieData>("FXListDie", ModuleOwnerKind.Object, FxListDieData.Parse, data => new FxListDie(data)),
        new ModuleBinding<BodyData>("HighlanderBody", ModuleOwnerKind.Object, BodyData.Parse, data => new HighlanderBody(data)),
        new ModuleBinding<BodyData>("ImmortalBody", ModuleOwnerKind.Object, BodyData.Parse, data => new ImmortalBody(data)),
        new ModuleBinding<InactiveBodyData>("InactiveBody", ModuleOwnerKind.Object, _ => InactiveBodyData.Instance, _ => new InactiveBody()),
        new ModuleBinding<InstantDeathData>("InstantDeathBehavior", ModuleOwnerKind.Object, InstantDeathData.Parse, data => new InstantDeathBehavior(data)),
        new ModuleBinding<PlainDieData>("KeepObjectDie", ModuleOwnerKind.Object, PlainDieData.Parse, data => new KeepObjectDie(data)),
        new ModuleBinding<LifetimeData>("LifetimeUpdate", ModuleOwnerKind.Object, LifetimeData.Parse, data => new LifetimeUpdate(data)),
        new ModuleBinding<BodyData>("StructureBody", ModuleOwnerKind.Object, BodyData.Parse, data => new StructureBody(data)),
    ];
}

/// <summary><c>InactiveBody</c> has no fields.</summary>
public sealed record InactiveBodyData : ModuleData
{
    public static InactiveBodyData Instance { get; } = new();
}
