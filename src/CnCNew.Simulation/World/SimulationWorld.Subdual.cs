using CnCNew.Simulation.Modules.BuiltIn;

namespace CnCNew.Simulation.World;

/// <summary>
/// <c>SubdualDamageHelper</c>: what took subdual damage (the microwave tank, the ECM tank) loses it again, a step of <c>SubdualDamageHealAmount</c> every <c>SubdualDamageHealRate</c>, and
/// every hit puts the next step off by the whole rate. The original gives every object the helper and lets it sleep; this keeps only those that are awake, in the order of their ids.
/// </summary>
public sealed partial class SimulationWorld
{
    private readonly SortedDictionary<uint, Entity> _subdualHealing = [];
    private readonly SortedDictionary<uint, Entity> _statusDamaged = [];

    /// <inheritdoc cref="Modules.IWorldServices.BeginSubdualHealing"/>
    public void BeginSubdualHealing(Entity entity) => _subdualHealing[entity.Id.Value] = entity;

    /// <inheritdoc cref="Modules.IWorldServices.DoStatusDamage"/>
    public void DoStatusDamage(Entity entity, int bit, long frames)
    {
        // "Clear any different status we may have. Re-getting the same status will just reset the timer."
        if (entity.StatusDamageUntil != 0 && entity.StatusDamageBit != bit) entity.Status &= ~(1UL << entity.StatusDamageBit);
        entity.Status |= 1UL << bit;
        entity.StatusDamageBit = bit;
        entity.StatusDamageUntil = _tick + Time.OriginalFrames(frames);
        _statusDamaged[entity.Id.Value] = entity;
        MarkDirty(entity);
    }

    private void StepStatusDamage(long tick)
    {
        if (_statusDamaged.Count == 0) return;
        foreach (var entity in _statusDamaged.Values.ToArray())
        {
            if (entity.Registered && !entity.DestroyRequested && tick < entity.StatusDamageUntil) continue;
            entity.Status &= ~(1UL << entity.StatusDamageBit);
            entity.StatusDamageUntil = 0;
            _statusDamaged.Remove(entity.Id.Value);
            if (entity.Registered) MarkDirty(entity);
        }
    }

    private void StepSubdualHealing(long tick)
    {
        if (_subdualHealing.Count == 0) return;
        foreach (var entity in _subdualHealing.Values.ToArray())
            if (!entity.Registered || entity.DestroyRequested || entity.Body is not ActiveBody body || !body.StepSubdualHealing(this, tick))
                _subdualHealing.Remove(entity.Id.Value);
    }
}
