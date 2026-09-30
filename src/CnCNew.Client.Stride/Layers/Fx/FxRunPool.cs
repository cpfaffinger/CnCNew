using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;

namespace CnCNew.Client.Stride.Layers.Fx;

/// <summary>
/// Pooled one-mesh entities that each draw one run (a range of quads of the shared <see cref="FxGeometry"/>) with one material. The
/// transparent stage orders render objects by the distance of their bounding box center to the camera, so a run is placed on the view
/// axis at the run's depth: that is how the layer controls the back-to-front order without owning the compositor.
/// </summary>
internal sealed class FxRunPool(Scene scene, FxGeometry geometry, FxMaterials materials) : IDisposable
{
    private readonly Dictionary<FxMaterialKey, Bucket> _buckets = [];
    private readonly List<Entity> _entities = [];

    public int SlotCount => _entities.Count;

    public void BeginFrame()
    {
        foreach (var bucket in _buckets.Values)
        {
            bucket.Used = 0;
        }
    }

    /// <summary>Points the next free slot of <paramref name="key"/> at a quad range; <paramref name="center"/> is the sort position.</summary>
    public void Draw(FxMaterialKey key, int firstQuad, int quadCount, Vector3 center)
    {
        if (!_buckets.TryGetValue(key, out var bucket))
        {
            bucket = new Bucket();
            _buckets.Add(key, bucket);
        }

        var index = bucket.Used++;
        if (index == bucket.Slots.Count)
        {
            bucket.Slots.Add(CreateSlot(key, bucket.Slots.Count));
        }

        var slot = bucket.Slots[index];
        slot.Mesh.Draw.StartLocation = firstQuad * 6;
        slot.Mesh.Draw.DrawCount = quadCount * 6;
        var extent = new Vector3(0.5f);
        slot.Mesh.BoundingBox = new BoundingBox(center - extent, center + extent);
        slot.Mesh.BoundingSphere = new BoundingSphere(center, 0.5f);
        slot.Component.Enabled = true;
    }

    public void EndFrame()
    {
        foreach (var bucket in _buckets.Values)
        {
            for (var index = bucket.Used; index < bucket.Slots.Count; index++)
            {
                bucket.Slots[index].Component.Enabled = false;
            }
        }
    }

    public void Dispose()
    {
        foreach (var entity in _entities)
        {
            scene.Entities.Remove(entity);
        }

        _entities.Clear();
        _buckets.Clear();
    }

    private Slot CreateSlot(FxMaterialKey key, int ordinal)
    {
        var mesh = new Mesh
        {
            Draw = new MeshDraw
            {
                PrimitiveType = PrimitiveType.TriangleList,
                StartLocation = 0,
                DrawCount = 0,
                VertexBuffers = [new VertexBufferBinding(geometry.VertexBuffer, FxVertex.Layout, geometry.MaxQuads * 4)],
                IndexBuffer = new IndexBufferBinding(geometry.IndexBuffer, true, geometry.MaxQuads * 6),
            },
            MaterialIndex = 0,
            BoundingBox = new BoundingBox(Vector3.Zero, Vector3.Zero),
            BoundingSphere = new BoundingSphere(Vector3.Zero, 0.5f),
        };
        var model = new Model();
        model.Materials.Add(materials.Get(key));
        model.Meshes.Add(mesh);
        var component = new ModelComponent(model) { IsShadowCaster = false, Enabled = false };
        var entity = new Entity($"fx run {key.Blend} {key.Texture ?? "-"} #{ordinal}") { component };
        scene.Entities.Add(entity);
        _entities.Add(entity);
        return new Slot(mesh, component);
    }

    private sealed record Slot(Mesh Mesh, ModelComponent Component);

    private sealed class Bucket
    {
        public List<Slot> Slots { get; } = [];

        public int Used { get; set; }
    }
}
