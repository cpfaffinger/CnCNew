using System.Collections.Immutable;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Numerics;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Input;

/// <summary><c>CommandAvailability</c> (<c>ControlBar.h</c>) with <see cref="Active"/> for checked <c>CHECK_LIKE</c> buttons.</summary>
public enum ButtonAvailability
{
    Available,
    Active,
    Restricted,
    NotReady,
    /// <summary>Drawn in the "always colour" look: in the original both "cannot afford" and "already owned" upgrades.</summary>
    CantAfford,
    Hidden,
}

/// <summary>Why a button is not <see cref="ButtonAvailability.Available"/>: one explicit reason per <c>getCommandAvailability</c> branch.</summary>
public enum ButtonReason
{
    None,
    ScriptOnly,
    NotBuildable,
    MissingScience,
    MissingPrerequisite,
    InsufficientFunds,
    QueueFull,
    ProducerBusy,
    Underpowered,
    NotABuilder,
    BuilderBusy,
    NoProducer,
    AlreadyOwned,
    MissingUpgrade,
    NotPurchasable,
    InsufficientSciencePoints,
    Recharging,
    NoWeapon,
    Reloading,
    EmptyContainer,
    NotInstantiable,
    UnknownReference,
    /// <summary>The original supports the button, the simulation has no command for it yet.</summary>
    NotSupportedBySimulation,
}

/// <param name="Slot">1..14 as in the <c>CommandSet</c>.</param>
/// <param name="Hotkey">The lowercase <c>&amp;</c> character of the translated label, when a text lookup was supplied.</param>
/// <param name="ReadyFraction">Recharge (special power) or reload progress 0..1 while <see cref="ButtonAvailability.NotReady"/>.</param>
public sealed record CommandBarButton(int Slot, CommandButtonInfo Button, ButtonAvailability Availability, ButtonReason Reason, char? Hotkey, Fixed Cost, long BuildTicks,
    float? ReadyFraction, ImmutableArray<EntityId> Sources)
{
    public bool IsEnabled => Availability is ButtonAvailability.Available or ButtonAvailability.Active;
}

/// <summary>One production queue entry (<c>populateBuildQueue</c>, at most nine shown); only the head progresses.</summary>
public sealed record QueueEntry(int Index, string Template, string ButtonImage, Fixed Cost, float Progress);

public enum CommandBarContext
{
    Empty,
    /// <summary>A foreign object selected for information: no buttons.</summary>
    Foreign,
    /// <summary>A structure under construction: only the cancel-construction button (<see cref="CommandBarView.CanCancelConstruction"/>).</summary>
    UnderConstruction,
    BeingSold,
    /// <summary>A selected object has no command set (<c>populateCommand</c> hides every slot).</summary>
    NoCommandSet,
    Command,
    MultiSelect,
}

public sealed record CommandBarView(
    CommandBarContext Context,
    string CommandSet,
    ImmutableArray<CommandBarButton?> Slots,
    ImmutableArray<QueueEntry> Queue,
    EntityId Producer,
    ImmutableArray<string> Upgrades,
    bool CanCancelConstruction)
{
    public CommandBarButton? this[int slot] => slot >= 1 && slot <= Slots.Length ? Slots[slot - 1] : null;
}

public sealed record SpecialPowerView(string Power, EntityId Source, bool Ready, float ReadyFraction);

/// <summary>The HUD numbers: money, the power balance (production includes plant bonuses; consumption is the finished consumers' draw), purchase points and the special powers the player can fire.</summary>
public sealed record PlayerHud(Fixed Money, int EnergyProduction, int EnergyConsumption, int SciencePurchasePoints, ImmutableArray<SpecialPowerView> SpecialPowers)
{
    /// <summary><c>Energy::hasSufficientPower</c> fails: <c>POWERED</c> objects are disabled (<c>Player.cpp doPowerDisable</c>).</summary>
    public bool Underpowered => EnergyProduction < EnergyConsumption;
}

public enum HotkeyActivation
{
    None,
    Activated,
    /// <summary>The button is visible but disabled: the original only plays <c>GUIClickDisabled</c>.</summary>
    Disabled,
}

/// <summary>
/// The control bar as data (<c>ControlBar::populateCommand</c>, <c>populateMultiSelect</c>, <c>getCommandAvailability</c>, <c>populateBuildQueue</c>).
/// </summary>
public sealed class CommandBarModel(CommandButtonCatalog catalog, Func<string, string?>? text = null)
{
    /// <summary>The retail control bar has 14 command windows (<c>ButtonCommand01</c>..<c>14</c>).</summary>
    public const int GridSlots = 14;

    /// <summary>What <c>getProductionCount</c> can show at most (<c>MAX_BUILD_QUEUE_BUTTONS</c>).</summary>
    public const int MaxQueueButtons = 9;

    public CommandButtonCatalog Catalog { get; } = catalog;

    /// <summary>What a mission script changed about the control bar (removed or added buttons, sciences hidden or disabled); nothing for a skirmish.</summary>
    public ControlBarScriptState Script { get; } = new();

    /// <summary><c>CommandSet::getCommandButton</c>: the script's override of the slot (<c>GameLogic::findControlBarOverride</c>, which may be "no button") before the set's own button.</summary>
    private CommandButtonInfo? ButtonOf(CommandSetInfo set, int index)
        => Script.TryGetOverride(set.Name, index, out var overridden) ? overridden : set.Slots[index];

    /// <summary>
    /// <c>ScriptActions::doRemoveCommandBarButton</c>: finds the slot of the command set of <paramref name="objectType"/> (its own <c>CommandSet</c> field) that holds
    /// <paramref name="buttonName"/> as the set shows it now and removes it for every object that uses that set. True when the object type and the button are known (a button that is gone
    /// already stays gone); false when either is not, so the caller can report it.
    /// </summary>
    public bool RemoveButton(string buttonName, string objectType)
    {
        if (!Catalog.TryGetObjectSet(objectType, out var setName) || !Catalog.TryGetSet(setName, out var set) || !Catalog.TryGetButton(buttonName, out _)) return false;
        for (var index = 0; index < CommandSetInfo.MaxSlots; index++)
            if (ButtonOf(set, index) is { } present && present.Name.Equals(buttonName, StringComparison.OrdinalIgnoreCase))
            {
                Script.Override(set.Name, index, null);
                break;
            }
        return true;
    }

    /// <summary>
    /// <c>ScriptActions::doAddCommandBarButton</c>: the button takes <paramref name="slot"/> (1-based, as the designers write it) of the object type's command set. False for an unknown object type,
    /// button or a slot outside the set.
    /// </summary>
    public bool AddButton(string buttonName, string objectType, int slot)
    {
        if (!Catalog.TryGetObjectSet(objectType, out var setName) || !Catalog.TryGetSet(setName, out var set) || !Catalog.TryGetButton(buttonName, out var button)) return false;
        if (--slot < 0 || slot >= CommandSetInfo.MaxSlots) return false;
        Script.Override(set.Name, slot, button);
        return true;
    }

    /// <summary>The object's own set, replaced by the set of its last executed <c>CommandSetUpgrade</c> (<c>CommandSetUpgrade::upgradeImplementation</c>: the alternate set when <c>TriggerAlt</c> is owned by the player or the object).</summary>
    public string? ActiveCommandSet(SimulationWorld world, Entity entity)
    {
        string? set = Catalog.TryGetObjectSet(entity.Template.Name, out var own) ? own : null;
        foreach (var module in entity.Modules.Where(module => module.ModuleName == "CommandSetUpgrade").OrderBy(module => module.Index))
        {
            if (!SimulationQueries.Describe(module).Bool("executed")) continue;
            if (entity.Template.Modules.FirstOrDefault(spec => spec.Name == "CommandSetUpgrade" && spec.Tag == module.Tag)?.Data is not CommandSetUpgradeData data) continue;
            var alternate = data.TriggerAlt.Length > 0 && world.Upgrades.TryGet(data.TriggerAlt, out var trigger)
                            && (world.PlayerUpgrades(entity.Owner).Has(trigger.Index) || entity.CompletedUpgrades.Has(trigger.Index));
            set = alternate && data.CommandSetAlt.Length > 0 ? data.CommandSetAlt : data.CommandSet;
        }
        return set is null || set.Equals("NONE", StringComparison.OrdinalIgnoreCase) ? null : set;
    }

    public CommandBarView Build(SimulationWorld world, SelectionModel selection)
    {
        var player = selection.LocalPlayer;
        var selected = selection.Selected.Select(world.FindEntity).OfType<Entity>().ToList();
        if (selected.Count == 0) return Empty(CommandBarContext.Empty);
        if (selected.Any(entity => entity.Owner != player)) return Empty(CommandBarContext.Foreign);
        var units = selected.Where(entity => !entity.Template.IsKindOf("IGNORED_IN_GUI") && !entity.HasStatus(ObjectStatusBits.Sold)).ToList();
        if (units.Count == 0) return Empty(CommandBarContext.BeingSold);
        if (units.Count == 1) return BuildSingle(world, player, units[0]);
        return BuildMulti(world, player, units);
    }

    private static CommandBarView Empty(CommandBarContext context)
        => new(context, string.Empty, [..Enumerable.Repeat<CommandBarButton?>(null, GridSlots)], [], EntityId.Invalid, [], false);

    private CommandBarView BuildSingle(SimulationWorld world, PlayerId player, Entity entity)
    {
        if (entity.HasStatus(ObjectStatusBits.UnderConstruction))
            return Empty(CommandBarContext.UnderConstruction) with { CanCancelConstruction = true, Producer = entity.Id };
        var setName = ActiveCommandSet(world, entity);
        if (setName is null || !Catalog.TryGetSet(setName, out var set)) return Empty(CommandBarContext.NoCommandSet) with { CommandSet = setName ?? string.Empty };
        var slots = new CommandBarButton?[GridSlots];
        var hotkeys = new HashSet<char>();
        for (var index = 0; index < GridSlots; index++)
        {
            if (ButtonOf(set, index) is not { } button) continue;
            var state = Evaluate(world, player, entity, button);
            if (state.Availability == ButtonAvailability.Hidden) continue;
            slots[index] = Make(index + 1, button, state, [entity.Id], hotkeys);
        }
        return new CommandBarView(CommandBarContext.Command, set.Name, [..slots], QueueOf(world, entity, set), entity.Id, UpgradesOf(world, entity), false);
    }

    private CommandBarView BuildMulti(SimulationWorld world, PlayerId player, List<Entity> units)
    {
        var sets = new List<CommandSetInfo>();
        foreach (var unit in units)
        {
            if (ActiveCommandSet(world, unit) is not { } name || !Catalog.TryGetSet(name, out var set)) return Empty(CommandBarContext.NoCommandSet);
            sets.Add(set);
        }
        var common = new CommandButtonInfo?[GridSlots];
        for (var index = 0; index < GridSlots; index++)
            if (ButtonOf(sets[0], index) is { } first && first.Has(CommandOptions.OkForMultiSelect)) common[index] = first;
        for (var unit = 1; unit < sets.Count; unit++)
            for (var index = 0; index < GridSlots; index++)
            {
                var other = ButtonOf(sets[unit], index);
                if (common[index] is { } current && !ReferenceEquals(current, other) && current.Kind != GuiCommandKind.AttackMove) common[index] = null;
                else if (common[index] is null && other is { Kind: GuiCommandKind.AttackMove } attack && attack.Has(CommandOptions.OkForMultiSelect)) common[index] = attack;
            }
        var slots = new CommandBarButton?[GridSlots];
        var hotkeys = new HashSet<char>();
        for (var index = 0; index < GridSlots; index++)
        {
            if (common[index] is not { } button) continue;
            var owners = units.Where((_, unit) => ReferenceEquals(ButtonOf(sets[unit], index), button)).ToList();
            var states = owners.Select(unit => Evaluate(world, player, unit, button)).ToList();
            if (states.All(state => state.Availability == ButtonAvailability.Hidden)) continue;
            // updateContextMultiSelect: enabled when at least one unit can do it; otherwise the first unit's verdict.
            var enabled = states.FindIndex(state => state.Availability is ButtonAvailability.Available or ButtonAvailability.Active);
            var chosen = enabled >= 0 ? states[enabled] : states[0];
            var sources = owners.Where((_, i) => states[i].Availability is ButtonAvailability.Available or ButtonAvailability.Active).Select(unit => unit.Id).ToImmutableArray();
            slots[index] = Make(index + 1, button, chosen, sources.IsEmpty ? [..owners.Select(unit => unit.Id)] : sources, hotkeys);
        }
        return new CommandBarView(CommandBarContext.MultiSelect, string.Empty, [..slots], [], EntityId.Invalid, [], false);
    }

    private CommandBarButton Make(int slot, CommandButtonInfo button, (ButtonAvailability Availability, ButtonReason Reason, float? Ready, Fixed Cost, long Ticks) state,
        ImmutableArray<EntityId> sources, HashSet<char> hotkeys)
    {
        // HotKeyManager::addHotKey: a key already taken by an earlier slot is ignored.
        var hotkey = text is null ? null : CommandButtonCatalog.HotkeyOf(button, text);
        if (hotkey is { } key && !hotkeys.Add(key)) hotkey = null;
        return new CommandBarButton(slot, button, state.Availability, state.Reason, hotkey, state.Cost, state.Ticks, state.Ready, sources);
    }

    /// <summary>
    /// <c>ControlBar::getCommandAvailability</c> for one object, reduced to the state the simulation exposes and ordered like the reference: hidden
    /// buttons, the underpowered disable, <c>NEED_UPGRADE</c>, <c>NOT_QUEUEABLE</c>, then the per-kind checks.
    /// </summary>
    public (ButtonAvailability Availability, ButtonReason Reason, float? Ready, Fixed Cost, long Ticks) Evaluate(SimulationWorld world, PlayerId player, Entity entity, CommandButtonInfo button)
    {
        (ButtonAvailability, ButtonReason, float?, Fixed, long) State(ButtonAvailability availability, ButtonReason reason, float? ready = null, Fixed cost = default, long ticks = 0)
            => (availability, reason, ready, cost, ticks);
        if (button.Has(CommandOptions.ScriptOnly)) return State(ButtonAvailability.Hidden, ButtonReason.ScriptOnly);
        Simulation.Templates.SpecialPowerInfo? power = null;
        if (button.SpecialPower.Length > 0)
        {
            if (!world.SpecialPowers.TryGet(button.SpecialPower, out var found)) return State(ButtonAvailability.Restricted, ButtonReason.UnknownReference);
            power = found;
            if (button.Has(CommandOptions.NeedSpecialPowerScience) && found.RequiredScience >= 0 && !world.PlayerSciences(player).Has(found.RequiredScience))
                return State(ButtonAvailability.Hidden, ButtonReason.MissingScience);
        }
        var exemptFromPower = button.Kind is GuiCommandKind.Sell or GuiCommandKind.Evacuate or GuiCommandKind.ExitContainer or GuiCommandKind.BeaconDelete
            or GuiCommandKind.SetRallyPoint or GuiCommandKind.Stop or GuiCommandKind.SwitchWeapon;
        if (!exemptFromPower && !button.Has(CommandOptions.IgnoresUnderpowered) && entity.Template.IsKindOf("POWERED") && world.EnergyProductionOf(player) < 0)
            return State(ButtonAvailability.Restricted, ButtonReason.Underpowered);
        if (button.Has(CommandOptions.NeedUpgrade) && button.Upgrade.Length > 0)
        {
            if (!world.Upgrades.TryGet(button.Upgrade, out var needed)) return State(ButtonAvailability.Restricted, ButtonReason.UnknownReference);
            if (!world.UpgradeKeyMaskOf(entity).Has(needed.Index)) return State(ButtonAvailability.Restricted, ButtonReason.MissingUpgrade);
        }
        var production = SimulationQueries.ProductionOf(entity);
        if (button.Has(CommandOptions.NotQueueable) && production is not null && SimulationQueries.Describe(production).Array("queue").Count > 0)
            return State(ButtonAvailability.Restricted, ButtonReason.ProducerBusy);

        switch (button.Kind)
        {
            case GuiCommandKind.DozerConstruct or GuiCommandKind.UnitBuild:
            {
                if (!world.Templates.TryGet(button.Object, out var template)) return State(ButtonAvailability.Restricted, ButtonReason.UnknownReference);
                var cost = template.BuildCost;
                var ticks = template.BuildTicks;
                if (template.Buildable.Equals("No", StringComparison.OrdinalIgnoreCase)
                    || template.Buildable.Equals("Only_By_AI", StringComparison.OrdinalIgnoreCase) && !world.IsComputerPlayer(player))
                    return State(ButtonAvailability.Hidden, ButtonReason.NotBuildable, null, cost, ticks);
                if (button.Kind == GuiCommandKind.DozerConstruct)
                {
                    if (!SimulationQueries.IsBuilder(entity)) return State(ButtonAvailability.Restricted, ButtonReason.NotABuilder, null, cost, ticks);
                    if (SimulationQueries.AiOf(entity) is { IsBuilding: true }) return State(ButtonAvailability.Restricted, ButtonReason.BuilderBusy, null, cost, ticks);
                    if (!SimulationQueries.CanBuildTemplate(world, player, entity, template)) return State(ButtonAvailability.Restricted, ButtonReason.NotBuildable, null, cost, ticks);
                }
                else
                {
                    if (production is null || entity.HasStatus(ObjectStatusBits.UnderConstruction)) return State(ButtonAvailability.Restricted, ButtonReason.NoProducer, null, cost, ticks);
                    if (!production.HasRoom) return State(ButtonAvailability.Restricted, ButtonReason.QueueFull, null, cost, ticks);
                }
                if (!template.IsInstantiable) return State(ButtonAvailability.Restricted, ButtonReason.NotInstantiable, null, cost, ticks);
                if (!SimulationQueries.PrerequisitesSatisfied(world, player, template)) return State(ButtonAvailability.Restricted, ButtonReason.MissingPrerequisite, null, cost, ticks);
                if (world.MoneyOf(player) < cost) return State(ButtonAvailability.Restricted, ButtonReason.InsufficientFunds, null, cost, ticks);
                return State(ButtonAvailability.Available, ButtonReason.None, null, cost, ticks);
            }
            case GuiCommandKind.PlayerUpgrade or GuiCommandKind.ObjectUpgrade:
            {
                if (!world.Upgrades.TryGet(button.Upgrade, out var upgrade)) return State(ButtonAvailability.Restricted, ButtonReason.UnknownReference);
                var owned = button.Kind == GuiCommandKind.PlayerUpgrade ? world.PlayerUpgrades(player).Has(upgrade.Index) : entity.CompletedUpgrades.Has(upgrade.Index);
                if (owned) return State(ButtonAvailability.CantAfford, ButtonReason.AlreadyOwned, null, upgrade.BuildCost, upgrade.BuildTicks);
                if (button.Kind == GuiCommandKind.ObjectUpgrade && production is null) return State(ButtonAvailability.Restricted, ButtonReason.NoProducer, null, upgrade.BuildCost, upgrade.BuildTicks);
                if (world.MoneyOf(player) < upgrade.BuildCost) return State(ButtonAvailability.Restricted, ButtonReason.InsufficientFunds, null, upgrade.BuildCost, upgrade.BuildTicks);
                foreach (var name in button.Sciences)
                    if (!world.Sciences.TryGet(name, out var science) || !world.PlayerSciences(player).Has(science.Index))
                        return State(ButtonAvailability.Restricted, ButtonReason.MissingScience, null, upgrade.BuildCost, upgrade.BuildTicks);
                return State(ButtonAvailability.Available, ButtonReason.None, null, upgrade.BuildCost, upgrade.BuildTicks);
            }
            case GuiCommandKind.PurchaseScience:
            {
                // ControlBar::populateGeneralsExpPoints: a science a script hid is not shown at all, one it disabled is shown but cannot be bought (Player::isScienceHidden / isScienceDisabled).
                if (button.Sciences.Length > 0)
                {
                    var scripted = Script.AvailabilityOf(button.Sciences[0]);
                    if (scripted == ScienceAvailability.Hidden) return State(ButtonAvailability.Hidden, ButtonReason.NotPurchasable);
                    if (scripted == ScienceAvailability.Disabled) return State(ButtonAvailability.Restricted, ButtonReason.NotPurchasable);
                }
                var owned = world.PlayerSciences(player);
                Simulation.Templates.ScienceInfo? next = null;
                foreach (var name in button.Sciences)
                {
                    if (!world.Sciences.TryGet(name, out var science)) return State(ButtonAvailability.Restricted, ButtonReason.UnknownReference);
                    if (!owned.Has(science.Index)) { next = science; break; }
                }
                if (next is null) return State(ButtonAvailability.CantAfford, ButtonReason.AlreadyOwned);
                if (!world.Sciences.PrerequisitesSatisfied(owned, next)) return State(ButtonAvailability.Restricted, ButtonReason.MissingPrerequisite);
                if (next.PurchasePointCost <= 0) return State(ButtonAvailability.Restricted, ButtonReason.NotPurchasable);
                if (world.SciencePointsOf(player) < next.PurchasePointCost) return State(ButtonAvailability.Restricted, ButtonReason.InsufficientSciencePoints);
                return State(ButtonAvailability.Available, ButtonReason.None);
            }
            case GuiCommandKind.FireWeapon:
                // Only object targets exist in the simulation (FireWeaponCommand); position and mine-clearing shots have no command.
                if (button.Has(CommandOptions.UsesMineClearingWeaponset) || (!button.Has(CommandOptions.NeedTargetEnemyObject) && !button.Has(CommandOptions.NeedTargetNeutralObject)))
                    return State(ButtonAvailability.Restricted, ButtonReason.NotSupportedBySimulation);
            {
                // The button's own weapon slot (CommandButton::m_weaponSlot, PRIMARY when absent).
                var slot = Simulation.Templates.WeaponSlot.TryParse(button.WeaponSlot, out var parsed) ? parsed : Simulation.Templates.WeaponSlot.Primary;
                if (SimulationWorld.WeaponIndexIn(entity, slot) < 0) return State(ButtonAvailability.Restricted, ButtonReason.NoWeapon);
                if (world.Tick < entity.ReadyTickOf(slot)) return State(ButtonAvailability.NotReady, ButtonReason.Reloading);
                return State(ButtonAvailability.Available, ButtonReason.None);
            }
            case GuiCommandKind.SpecialPower or GuiCommandKind.SpecialPowerFromShortcut:
            {
                var source = button.Kind == GuiCommandKind.SpecialPowerFromShortcut ? MostReadyCarrier(world, player, power!) : entity;
                var module = source?.Modules.OfType<ISpecialPowerModule>().FirstOrDefault(candidate => candidate.PowerIndex == power!.Index);
                if (module is null) return State(ButtonAvailability.Hidden, ButtonReason.UnknownReference);
                if (power!.RequiredScience >= 0 && !world.PlayerSciences(player).Has(power.RequiredScience)) return State(ButtonAvailability.Restricted, ButtonReason.MissingScience);
                if (!module.IsReady(world)) return State(ButtonAvailability.NotReady, ButtonReason.Recharging, ReadyFraction(world, player, (SimModule)module, power));
                return State(ButtonAvailability.Available, ButtonReason.None, 1f);
            }
            case GuiCommandKind.Sell:
                return entity.Template.IsKindOf("STRUCTURE") ? State(ButtonAvailability.Available, ButtonReason.None) : State(ButtonAvailability.Restricted, ButtonReason.NotSupportedBySimulation);
            case GuiCommandKind.Evacuate:
                return entity.Container is { ContainCount: > 0 } ? State(ButtonAvailability.Available, ButtonReason.None) : State(ButtonAvailability.Restricted, ButtonReason.EmptyContainer);
            case GuiCommandKind.SetRallyPoint:
                return production is null ? State(ButtonAvailability.Restricted, ButtonReason.NoProducer) : State(ButtonAvailability.Available, ButtonReason.None);
            case GuiCommandKind.Stop or GuiCommandKind.SelectAllUnitsOfType or GuiCommandKind.DozerConstructCancel or GuiCommandKind.CancelUnitBuild:
                return State(ButtonAvailability.Available, ButtonReason.None);
            case GuiCommandKind.AttackMove or GuiCommandKind.Guard or GuiCommandKind.GuardWithoutPursuit or GuiCommandKind.GuardFlyingUnitsOnly:
                return SimulationQueries.CanMove(entity) ? State(ButtonAvailability.Available, ButtonReason.None) : State(ButtonAvailability.Restricted, ButtonReason.NotSupportedBySimulation);
            default:
                return State(ButtonAvailability.Restricted, ButtonReason.NotSupportedBySimulation);
        }
    }

    private static Entity? MostReadyCarrier(SimulationWorld world, PlayerId player, Simulation.Templates.SpecialPowerInfo power)
    {
        Entity? fallback = null;
        foreach (var entity in world.Entities.Where(entity => entity.Owner == player && SimulationQueries.IsAlive(entity)).OrderBy(entity => entity.Id.Value))
        {
            var module = entity.Modules.OfType<ISpecialPowerModule>().FirstOrDefault(candidate => candidate.PowerIndex == power.Index);
            if (module is null) continue;
            if (module.IsReady(world)) return entity;
            fallback ??= entity;
        }
        return fallback;
    }

    /// <summary><c>SpecialPowerModule::getReadyFrame</c>: the logic frame the power is ready again - the shared frame of the player for <c>SharedSyncedTimer</c> powers, else the module's own saved one.</summary>
    public static long ReadyFrameOf(SimulationWorld world, PlayerId player, SimModule module, Simulation.Templates.SpecialPowerInfo power)
        => power.SharedSynced && world.TryGetSharedReadyFrame(player, power.Index, out var shared) ? shared : SimulationQueries.Describe(module).Int("availableOnFrame");

    /// <summary><c>SpecialPowerModule::getPercentReady</c>: the elapsed share of the reload, from the saved ready frame (the shared frame for <c>SharedSyncedTimer</c> powers).</summary>
    private static float ReadyFraction(SimulationWorld world, PlayerId player, SimModule module, Simulation.Templates.SpecialPowerInfo power)
    {
        var readyFrame = ReadyFrameOf(world, player, module, power);
        if (power.ReloadTicks <= 0) return world.Tick >= readyFrame ? 1f : 0f;
        return Math.Clamp(1f - (readyFrame - world.Tick) / (float)power.ReloadTicks, 0f, 1f);
    }

    private ImmutableArray<QueueEntry> QueueOf(SimulationWorld world, Entity entity, CommandSetInfo set)
    {
        if (SimulationQueries.ProductionOf(entity) is not { } production) return [];
        var entries = ImmutableArray.CreateBuilder<QueueEntry>();
        var queue = SimulationQueries.Describe(production).Array("queue");
        for (var index = 0; index < queue.Count && index < MaxQueueButtons; index++)
        {
            var template = world.Templates[checked((int)queue[index].Int("template"))];
            var cost = queue[index].Values.TryGetValue("cost", out var value) && value is Fixed fixedCost ? fixedCost : template.BuildCost;
            var total = Math.Max(template.BuildTicks, 1);
            var progress = index == 0 ? Math.Clamp((total - queue[index].Int("remaining")) / (float)total, 0f, 1f) : 0f;
            var image = Enumerable.Range(0, set.Slots.Length).Select(slot => ButtonOf(set, slot))
                .FirstOrDefault(button => button is { Kind: GuiCommandKind.UnitBuild } && button.Object.Equals(template.Name, StringComparison.OrdinalIgnoreCase))?.ButtonImage ?? string.Empty;
            entries.Add(new QueueEntry(index, template.Name, image, cost, progress));
        }
        return entries.ToImmutable();
    }

    private static ImmutableArray<string> UpgradesOf(SimulationWorld world, Entity entity)
        => [..world.Upgrades.Upgrades.Where(upgrade => !upgrade.IsEngine && entity.CompletedUpgrades.Has(upgrade.Index)).Select(upgrade => upgrade.Name).Order(StringComparer.Ordinal)];

    /// <summary>A click on a slot (1..14).</summary>
    public ActivationResult Activate(SimulationWorld world, CommandBarView view, int slot, CommandModeController controller)
    {
        if (view[slot] is not { } button || !button.IsEnabled) return ActivationResult.NoSource;
        var sources = button.Sources.Select(world.FindEntity).OfType<Entity>().ToImmutableArray();
        return controller.Activate(world, button.Button, sources);
    }

    /// <summary><c>HotKeyTranslator</c>: a key released without modifiers activates the visible button carrying that hotkey.</summary>
    public HotkeyActivation ActivateHotkey(SimulationWorld world, CommandBarView view, char key, CommandModeController controller, out ActivationResult result)
    {
        result = ActivationResult.NoSource;
        var lower = char.ToLowerInvariant(key);
        foreach (var button in view.Slots)
        {
            if (button?.Hotkey != lower) continue;
            if (!button.IsEnabled) return HotkeyActivation.Disabled;
            result = Activate(world, view, button.Slot, controller);
            return HotkeyActivation.Activated;
        }
        return HotkeyActivation.None;
    }

    public PlayerHud Hud(SimulationWorld world, PlayerId player)
    {
        var consumption = 0;
        foreach (var entity in world.Entities)
            if (entity.Owner == player && SimulationQueries.IsAlive(entity) && !entity.HasStatus(ObjectStatusBits.UnderConstruction) && entity.Template.EnergyProduction < 0)
                consumption -= entity.Template.EnergyProduction;
        var production = world.EnergyProductionOf(player) + consumption;
        var powers = new SortedDictionary<string, SpecialPowerView>(StringComparer.Ordinal);
        var sciences = world.PlayerSciences(player);
        foreach (var entity in world.Entities.Where(entity => entity.Owner == player && SimulationQueries.IsAlive(entity)).OrderBy(entity => entity.Id.Value))
            foreach (var module in entity.Modules.OfType<ISpecialPowerModule>())
            {
                if (module.PowerIndex < 0) continue;
                var info = world.SpecialPowers[module.PowerIndex];
                if (info.RequiredScience >= 0 && !sciences.Has(info.RequiredScience)) continue;
                var ready = module.IsReady(world) && !entity.HasStatus(ObjectStatusBits.UnderConstruction);
                var view = new SpecialPowerView(info.Name, entity.Id, ready, ready ? 1f : ReadyFraction(world, player, (SimModule)module, info));
                if (!powers.TryGetValue(info.Name, out var current) || (!current.Ready && view.Ready)) powers[info.Name] = view;
            }
        return new PlayerHud(world.MoneyOf(player), production, consumption, world.SciencePointsOf(player), [..powers.Values]);
    }
}
