using Stride.Engine;
using Stride.Rendering;

namespace CnCNew.Client.Stride.Layers.Objects;

/// <summary>
/// One scene entity with a bare <see cref="ModelComponent"/>, reusable for any asset: its <see cref="Model"/> and material overrides are
/// swapped freely (a Stride <see cref="ModelComponent"/> re-initializes its skeleton and mesh-info list whenever the model reference
/// changes, see <c>ModelComponent.CheckSkeleton</c>), so one small free list serves rigid parts, skin parts and the placeholder box alike.
/// The entity's own transform always stays identity; placement is written directly onto skeleton node world matrices every frame.
/// </summary>
internal sealed class PooledEntity
{
    public required Entity Entity { get; init; }
    public required ModelComponent Component { get; init; }

    public static PooledEntity Create()
    {
        var component = new ModelComponent { IsShadowCaster = false };
        var entity = new Entity("Object") { component };
        return new PooledEntity { Entity = entity, Component = component };
    }
}

/// <summary>A tiny free list of <see cref="PooledEntity"/> not currently attached to a scene; <see cref="ObjectLayer"/> owns scene membership.</summary>
internal sealed class EntityPool
{
    private readonly Stack<PooledEntity> _free = new();

    public int PooledCount => _free.Count;

    public PooledEntity Rent() => _free.Count > 0 ? _free.Pop() : PooledEntity.Create();

    public void Return(PooledEntity entity)
    {
        entity.Component.Model = null!;
        entity.Component.Materials.Clear();
        _free.Push(entity);
    }
}
