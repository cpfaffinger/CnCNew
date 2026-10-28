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
        var flags = DisabledFlagsAt(entity, tick);
        PauseSpecialPowers(entity, flags & ~DisabledMask.Held);
        var disabled = flags != DisabledMask.None;
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
            var flags = DisabledFlagsAt(entity, _tick);
            entity.DisabledEdge = flags != DisabledMask.None;
            entity.PowerPauseTypes = flags & ~DisabledMask.Held;
            if (entity.DisabledEdge) _disabledObjects.Add(entity);
        }
    }

    /// <summary>
    /// <c>Object::pauseAllSpecialPowers</c>: every disabled type but <c>DISABLED_HELD</c> pauses the object's special powers once when it is set and lets go once when it is
    /// cleared - a superweapon's clock stands while its building is underpowered or EMP'd.
    /// </summary>
    private void PauseSpecialPowers(Entity entity, DisabledMask types)
    {
        if (types == entity.PowerPauseTypes) return;
        var added = System.Numerics.BitOperations.PopCount((uint)(types & ~entity.PowerPauseTypes));
        var removed = System.Numerics.BitOperations.PopCount((uint)(entity.PowerPauseTypes & ~types));
        entity.PowerPauseTypes = types;
        // The disable is set in a frame and cleared at the end of its last one: both count from the start of the frame the edge is found in, so the pause is as long at every rate.
        var frameStart = Time.FloorToFrame(_tick);
        foreach (var module in entity.ModuleArray)
        {
            if (module is not ISpecialPowerModule power) continue;
            for (var count = 0; count < added; count++) power.PauseCountdown(this, true, frameStart);
            for (var count = 0; count < removed; count++) power.PauseCountdown(this, false, frameStart);
        }
        MarkDirty(entity);
    }

    private void OnDisabledEdge(Entity entity, bool becomingDisabled)
    {
        foreach (var module in entity.ModuleArray)
            if (module is SpyVisionUpdate spy) spy.SetDisabledUntilFrame(this, becomingDisabled ? SimulationTiming.SleepForever : 0);
        foreach (var module in entity.ModuleArray)
            if (module is RadarUpgrade { Executed: true } radar)
            {
                if (becomingDisabled) RemoveRadar(entity.Owner, radar.DisableProof);
                else AddRadar(entity.Owner, radar.DisableProof);
            }
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
