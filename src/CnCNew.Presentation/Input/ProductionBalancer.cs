using CnCNew.Simulation.Ids;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Input;

/// <summary>
/// Which of several selected buildings takes a production order (a feature of this client, after StarCraft's several gateways): the one whose queue is the shortest, the first
/// of them in selection order when queues are equally long - so repeated clicks fill the buildings round-robin, one more in each, and the queues stay as even as the orders allow.
/// A choice is a function of the world and of the orders this balancer gave a moment ago, never of chance.
/// <para>
/// An order is executed some ticks after it is given (the next tick at the earliest, the input delay of a network game later), and the queue the world shows does not hold it
/// before. The balancer therefore remembers the length it expects for a building it sent an order to, and uses the larger of that and the length shown, until the world shows it
/// or <c>horizonTicks</c> have passed (an order the simulation refused never shows). The same goes for the object upgrade a building was told to research.
/// </para>
/// </summary>
internal sealed class ProductionBalancer
{
    private readonly Dictionary<uint, (int Expected, long Tick)> _pending = [];
    private readonly Dictionary<(uint Producer, string Upgrade), long> _pendingUpgrades = [];

    /// <summary>What the queue of <paramref name="producer"/> holds, including the orders given but not yet executed.</summary>
    public int QueueLength(SimulationWorld world, Entity producer, long horizonTicks)
    {
        var shown = Shown(producer);
        if (!_pending.TryGetValue(producer.Id.Value, out var pending)) return shown;
        if (shown >= pending.Expected || world.Tick - pending.Tick > horizonTicks)
        {
            _pending.Remove(producer.Id.Value);
            return shown;
        }
        return pending.Expected;
    }

    /// <summary>How many entries the queue of <paramref name="producer"/> takes (<c>MaxQueueEntries</c>).</summary>
    public static int Limit(Entity producer) => SimulationQueries.ProductionOf(producer)?.Data.MaxQueueEntries ?? 0;

    /// <summary>
    /// The producer with the shortest queue among those with room for one more entry (the orders given but not yet executed counted); the first in the order given among equals.
    /// Null for no producer, and for producers that are all full: <paramref name="allFull"/> tells them apart, and no order is sent for a queue that cannot take it.
    /// </summary>
    public Entity? Pick(SimulationWorld world, IEnumerable<Entity> producers, long horizonTicks, out bool allFull)
    {
        Entity? best = null;
        var bestLength = int.MaxValue;
        var any = false;
        foreach (var producer in producers)
        {
            any = true;
            var length = QueueLength(world, producer, horizonTicks);
            if (length >= Limit(producer)) continue;
            if (length < bestLength) (best, bestLength) = (producer, length);
        }
        allFull = any && best is null;
        return best;
    }

    /// <summary>The orders counted as on their way to <paramref name="producer"/> are dropped: the queue the world shows is taken as it is.</summary>
    public void Forget(EntityId producer) => _pending.Remove(producer.Value);

    /// <summary>An order was given to <paramref name="producer"/> whose queue was <paramref name="lengthBefore"/> long.</summary>
    public void Ordered(SimulationWorld world, Entity producer, int lengthBefore) => _pending[producer.Id.Value] = (lengthBefore + 1, world.Tick);

    /// <summary>The buildings among <paramref name="producers"/> that were not just told to research <paramref name="upgrade"/>.</summary>
    public IEnumerable<Entity> WithoutPendingUpgrade(SimulationWorld world, IEnumerable<Entity> producers, string upgrade, long horizonTicks)
    {
        foreach (var producer in producers)
            if (!_pendingUpgrades.TryGetValue((producer.Id.Value, upgrade), out var tick) || world.Tick - tick > horizonTicks) yield return producer;
    }

    public void UpgradeOrdered(SimulationWorld world, Entity producer, string upgrade) => _pendingUpgrades[(producer.Id.Value, upgrade)] = world.Tick;

    private static int Shown(Entity producer)
        => SimulationQueries.ProductionOf(producer) is { } production ? SimulationQueries.Describe(production).Array("queue").Count : 0;
}
