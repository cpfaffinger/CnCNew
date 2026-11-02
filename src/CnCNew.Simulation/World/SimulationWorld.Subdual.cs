using CnCNew.Simulation.Modules.BuiltIn;

namespace CnCNew.Simulation.World;

/// <summary>
/// <c>SubdualDamageHelper</c>: what took subdual damage (the microwave tank, the ECM tank) loses it again, a step of <c>SubdualDamageHealAmount</c> every <c>SubdualDamageHealRate</c>, and
/// every hit puts the next step off by the whole rate. The original gives every object the helper and lets it sleep; this keeps only those that are awake, in the order of their ids.
/// </summary>
public sealed partial class SimulationWorld
{
    private readonly SortedDictionary<uint, Entity> _subdualHealing = [];

    /// <inheritdoc cref="Modules.IWorldServices.BeginSubdualHealing"/>
    public void BeginSubdualHealing(Entity entity) => _subdualHealing[entity.Id.Value] = entity;

    private void StepSubdualHealing(long tick)
    {
        if (_subdualHealing.Count == 0) return;
        foreach (var entity in _subdualHealing.Values.ToArray())
            if (!entity.Registered || entity.DestroyRequested || entity.Body is not ActiveBody body || !body.StepSubdualHealing(this, tick))
                _subdualHealing.Remove(entity.Id.Value);
    }
}
