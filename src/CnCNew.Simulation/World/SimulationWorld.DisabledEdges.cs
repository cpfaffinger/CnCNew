using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.Modules.BuiltIn;
using CnCNew.Simulation.Time;

namespace CnCNew.Simulation.World;

/// <summary>
/// F-56, <c>Object::onDisabledEdge</c>: when an object becomes disabled (in any way, from none) and again when its last disable ends, the original tells its modules
/// (<c>SpyVisionUpdate</c> switches off and on), cancels a dozer's task when it goes down, and takes a power plant's production - with its upgrade's and an active overcharge's
/// <c>EnergyBonus</c> - from its player and gives it back. The original fires the edge in <c>setDisabledUntil</c> and <c>clearDisabled</c>; the port finds it at the end of the tick,
/// among the objects the tick touched (every setter marks its object) and the objects that are disabled (a timed disable ends by itself), against the next tick's mask.
/// </summary>
public sealed partial class SimulationWorld
{
    /// <summary>The objects whose edge has gone up, in the order it did (derived: rebuilt without effects when a snapshot is restored).</summary>
    private readonly List<Entity> _disabledObjects = [];

    /// <summary>Whether the object's disabled edge has gone up - its production is out of its player's energy, a dozer's task cancelled.</summary>
    internal static bool HasDisabledEdge(Entity entity) => entity.DisabledEdge;

    private void ProcessDisabledEdges(long next)
    {
        for (var index = 0; index < _dirty.Count; index++) RefreshDisabledEdge(_dirty[index], next);
        for (var index = _disabledObjects.Count - 1; index >= 0; index--)
            if (index < _disabledObjects.Count) RefreshDisabledEdge(_disabledObjects[index], next);
    }

    private void RefreshDisabledEdge(Entity entity, long tick)
    {
        if (!entity.Registered || entity.DestroyRequested) return;
        var disabled = DisabledFlagsAt(entity, tick) != DisabledMask.None;
        if (disabled == entity.DisabledEdge) return;
        entity.DisabledEdge = disabled;
        if (disabled) _disabledObjects.Add(entity);
        else _disabledObjects.Remove(entity);
        OnDisabledEdge(entity, disabled);
        MarkDirty(entity);
    }

    /// <summary>After a restore: the edges of the disabled objects are up, without their effects (the energy was saved with them).</summary>
    private void RebuildDisabledEdges()
    {
        _disabledObjects.Clear();
        foreach (var entity in _entities.Values)
        {
            entity.DisabledEdge = DisabledFlagsAt(entity, _tick) != DisabledMask.None;
            if (entity.DisabledEdge) _disabledObjects.Add(entity);
        }
    }

    private void OnDisabledEdge(Entity entity, bool becomingDisabled)
    {
        foreach (var module in entity.ModuleArray)
            if (module is SpyVisionUpdate spy) spy.SetDisabledUntilFrame(this, becomingDisabled ? SimulationTiming.SleepForever : 0);
        // "Have to say goodbye to the thing we might be building or repairing so someone else can do it."
        if (becomingDisabled && Modules.BuiltIn.CrateModules.AiOf(entity) is { HasBuilderTask: true } dozer) dozer.OrderIdle(this);
        // "We can't affect something that consumes, or else we go low power which removes the consumption which makes us not low power..."
        var power = entity.Template.EnergyProduction;
        if (power <= 0) return;
        foreach (var module in entity.ModuleArray)
        {
            if (module is PowerPlantUpgrade { Executed: true }) power += entity.Template.EnergyBonus;
            if (module is OverchargeBehavior { Active: true }) power += entity.Template.EnergyBonus;
        }
        AddEnergyProduction(entity.Owner, becomingDisabled ? -power : power);
    }

    /// <summary><c>Object::friend_adjustPowerForPlayer</c>: a disabled producer neither enters nor leaves its player's energy (its edge took the production out).</summary>
    private void ApplyObjectEnergy(Entity entity, PlayerId player, bool entering)
    {
        if (entity.DisabledEdge && entity.Template.EnergyProduction > 0) return;
        ApplyTemplateEnergy(player, entity.Template.EnergyProduction, entering);
    }
}
