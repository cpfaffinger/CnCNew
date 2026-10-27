using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Templates;

namespace CnCNew.Simulation.World;

/// <summary>
/// Upgrades researched in a producer's queue for their build time (<c>ProductionUpdate::queueUpgrade</c>, <c>AIGroup::queueUpgrade</c>, <c>AIPlayer::buildUpgrade</c>; F-43,
/// docs/SIMULATION-CORE.md §53.6). A <see cref="PurchaseUpgradeCommand"/> names the producer: the object itself for an object upgrade, the building for a player upgrade - or no
/// object, and the world takes the first of the player's buildings, in id order, that can research it now.
/// </summary>
public sealed partial class SimulationWorld
{
    /// <summary><c>Player::hasUpgradeInProduction</c>: the upgrade waits in one of the player's queues.</summary>
    public bool UpgradeInProduction(PlayerId player, int upgradeIndex)
    {
        foreach (var entity in EntitiesOwnedBy(player).AsSpan())
            if (FindProductionModule(entity) is { } production && production.IsUpgradeInQueue(upgradeIndex)) return true;
        return false;
    }

    /// <summary><c>Object::canProduceUpgrade</c>: a button of the producer's command sets researches the upgrade (every set a <c>CommandSetUpgrade</c> can switch to counts).</summary>
    private bool OffersUpgrade(Entity producer, UpgradeInfo info)
    {
        foreach (var set in producer.Template.CommandSets)
            foreach (var name in _templates.Commands.UpgradesOf(set))
                if (name.Equals(info.Name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private bool CanResearchHere(Entity producer, UpgradeInfo info)
        => !producer.DestroyRequested && !producer.EffectivelyDead && CanProduceNow(producer) && FindProductionModule(producer) is { HasRoom: true } && OffersUpgrade(producer, info);

    /// <summary>
    /// The checks of <c>ProductionUpdate::queueUpgrade</c> for one purchase, without a side effect: the rejection, or none with the producer and the upgrade. Shared by the pre-check
    /// and the execution of the command, so the two cannot differ.
    /// </summary>
    private CommandRejection CheckUpgradePurchase(PlayerState buyer, PurchaseUpgradeCommand purchase, out Entity? producer, out UpgradeInfo? upgrade)
    {
        producer = null;
        upgrade = null;
        if (!_templates.Upgrades.TryGet(purchase.Upgrade, out var info)) return CommandRejection.UnknownUpgrade;
        upgrade = info;
        if (purchase.Target is { } targetId)
        {
            producer = FindEntity(targetId);
            if (producer is null || producer.DestroyRequested) return CommandRejection.UnknownEntity;
            if (producer.Owner != buyer.Id) return CommandRejection.NotPermitted;
        }
        else if (info.Scope == UpgradeScope.Object) return CommandRejection.InvalidArguments;
        // AIGroup::queueUpgrade: the money first, then what is owned, then whether the producer can research it.
        if (buyer.Money < info.BuildCost) return CommandRejection.InsufficientFunds;
        if (info.Scope == UpgradeScope.Object ? producer!.CompletedUpgrades.Has(info.Index) : buyer.CompletedUpgrades.Has(info.Index)) return CommandRejection.AlreadyOwned;
        if (info.Scope == UpgradeScope.Player ? UpgradeInProduction(buyer.Id, info.Index) : FindProductionModule(producer!)?.IsUpgradeInQueue(info.Index) == true)
            return CommandRejection.UpgradeInProduction;
        if (producer is null)
        {
            foreach (var candidate in EntitiesOwnedBy(buyer.Id).AsSpan())
                if (CanResearchHere(candidate, info))
                {
                    producer = candidate;
                    break;
                }
            if (producer is null) return CommandRejection.CannotProduce;
        }
        else
        {
            if (FindProductionModule(producer) is not { } production || !CanProduceNow(producer) || !OffersUpgrade(producer, info)) return CommandRejection.CannotProduce;
            if (!production.HasRoom) return CommandRejection.QueueFull;
        }
        return 0;
    }

    /// <summary>The purchase itself: paid when queued (<c>Money::withdraw</c>), researched for its build time.</summary>
    private void QueueUpgrade(PlayerState buyer, Entity producer, UpgradeInfo info)
    {
        buyer.Money -= info.BuildCost;
        var production = FindProductionModule(producer)!;
        production.EnqueueUpgrade(Time, info.Index, info.BuildCost, info.BuildTicks);
        var node = Array.Find(producer.Updates, value => ReferenceEquals(value.Module, production));
        if (node is not null) WakeAt(producer, node.Update, _tick);
        MarkDirty(producer);
    }
}
