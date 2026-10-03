using System.Collections.Immutable;
using CnCNew.Data.Registry;
using CnCNew.Simulation.Templates;

namespace CnCNew.Simulation.Modules.BuiltIn;

/// <summary>
/// The explicit binding list of the implemented modules, sorted ordinally by name. Phase 4 selected the lifecycle modules by
/// use in the shipped data (16,360 module uses; see docs/SIMULATION-CORE.md); phase 5 adds the upgrade family (needed by
/// <c>AutoHealBehavior</c> and cheap once the shared <see cref="UpgradeMuxState"/> exists), <c>StealthUpdate</c> and
/// <c>AutoHealBehavior</c> themselves (Block 0.2), and the economy/combat/movement modules (see docs/GAME-RULES.md).
/// </summary>
public static class BuiltInModules
{
    private static ModuleBinding<TData> Bind<TData>(string name, ModuleOwnerKind owner, Func<ModuleConfiguration, TData> parse, Func<TData, SimModule> create)
        where TData : ModuleData
        => new(name, owner, (configuration, _) => parse(configuration), create);

    private static ImmutableArray<IModuleBinding> Phase4Bindings { get; } =
    [
        Bind<BodyData>("ActiveBody", ModuleOwnerKind.Object, BodyData.Parse, data => new ActiveBody(data)),
        Bind<CreateCrateDieData>("CreateCrateDie", ModuleOwnerKind.Object, CreateCrateDieData.Parse, data => new CreateCrateDie(data)),
        Bind<CreateObjectDieData>("CreateObjectDie", ModuleOwnerKind.Object, CreateObjectDieData.Parse, data => new CreateObjectDie(data)),
        Bind<LifetimeData>("DeletionUpdate", ModuleOwnerKind.Object, LifetimeData.Parse, data => new DeletionUpdate(data)),
        Bind<PlainDieData>("DestroyDie", ModuleOwnerKind.Object, PlainDieData.Parse, data => new DestroyDie(data)),
        Bind<FxListDieData>("FXListDie", ModuleOwnerKind.Object, FxListDieData.Parse, data => new FxListDie(data)),
        Bind<BodyData>("HighlanderBody", ModuleOwnerKind.Object, BodyData.Parse, data => new HighlanderBody(data)),
        Bind<BodyData>("ImmortalBody", ModuleOwnerKind.Object, BodyData.Parse, data => new ImmortalBody(data)),
        Bind<InactiveBodyData>("InactiveBody", ModuleOwnerKind.Object, _ => InactiveBodyData.Instance, _ => new InactiveBody()),
        Bind<InstantDeathData>("InstantDeathBehavior", ModuleOwnerKind.Object, InstantDeathData.Parse, data => new InstantDeathBehavior(data)),
        Bind<PlainDieData>("KeepObjectDie", ModuleOwnerKind.Object, PlainDieData.Parse, data => new KeepObjectDie(data)),
        Bind<LifetimeData>("LifetimeUpdate", ModuleOwnerKind.Object, LifetimeData.Parse, data => new LifetimeUpdate(data)),
        Bind<BodyData>("StructureBody", ModuleOwnerKind.Object, BodyData.Parse, data => new StructureBody(data)),
    ];

    /// <summary>All implemented modules, ordinally sorted (the order <see cref="ModuleBindingTable"/> requires).</summary>
    public static ImmutableArray<IModuleBinding> Bindings { get; } =
        [..Phase4Bindings.Concat(Phase5Modules.Bindings).Concat(Phase5BlockerModules.Bindings).Concat(ContainModules.Bindings).Concat(MatchModules.Bindings).Concat(RetailMatchModules.Bindings).Concat(SpecialPowerModules.Bindings).Concat(DockModules.Bindings).Concat(RepairModules.Bindings).Concat(CombatModules.Bindings).Concat(SupplyModules.Bindings).Concat(SpawnModules.Bindings).Concat(ProjectileModules.Bindings).Concat(TransportModules.Bindings).Concat(ToppleAndFallModules.Bindings).Concat(Phase12Modules.Bindings).Concat(FlightModules.Bindings).Concat(VehicleModules.Bindings).Concat(AirModules.Bindings).Concat(CrateModules.Bindings).Concat(SpecialsModules.Bindings).Concat(CleanupAndSpyModules.Bindings).Concat(SlaveModules.Bindings).Concat(ContainChainModules.Bindings).Concat(HackerAndAssaultModules.Bindings).Concat(FieldEffectModules.Bindings).OrderBy(value => value.Name, StringComparer.Ordinal)];
}

/// <summary><c>InactiveBody</c> has no fields.</summary>
public sealed record InactiveBodyData : ModuleData
{
    public static InactiveBodyData Instance { get; } = new();
}
