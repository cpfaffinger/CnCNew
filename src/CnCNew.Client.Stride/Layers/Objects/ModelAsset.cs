using System.Numerics;
using CnCNew.Client.Stride.Rendering;
using CnCNew.Data.Definitions;
using CnCNew.W3D;
using Stride.Graphics;
using Stride.Rendering;
using BoundingBox = Stride.Core.Mathematics.BoundingBox;
using BoundingSphere = Stride.Core.Mathematics.BoundingSphere;

namespace CnCNew.Client.Stride.Layers.Objects;

/// <summary>One rigid (non-skinned) renderable sub-object of the model's most detailed LOD; occupies skeleton node <see cref="NodeIndex"/>.</summary>
internal sealed record RigidPart(int NodeIndex, string FullName, int BoneIndex, bool DefaultVisible);

/// <summary>Metadata for one entry of <see cref="ModelAsset.NeutralModel"/>'s <c>Meshes</c> list, aligned 1:1 by index.</summary>
internal sealed record MeshInfo(W3dMesh Mesh, W3dRenderBatch Batch, int MaterialIndex, bool IsHouseColor, bool IsAnimatedMapper, bool UsesHouseColorTexture);

/// <summary>
/// One skinned sub-object: a one-node template model (its Skeleton and Materials are shared by every instance) plus a per-batch vertex
/// template. Every live instance needs its own vertex buffer (bone poses differ per instance), so instances never draw
/// <see cref="Model"/> itself; they call <see cref="CreateRuntimeModel"/> once and then re-skin their own copy every visible frame.
/// </summary>
internal sealed class SkinPart
{
    public required string FullName { get; init; }
    public required int BoneIndex { get; init; }
    public required W3dMesh Mesh { get; init; }
    public required bool DefaultVisible { get; init; }
    public required IReadOnlyList<SkinBatchTemplate> Batches { get; init; }
    public required Model Model { get; init; }

    /// <summary>
    /// A fresh per-instance Model sharing this part's Skeleton and Materials but with its own Dynamic vertex buffers, seeded from the
    /// templates; the returned arrays are the CPU-side copies to mutate and re-upload every visible frame (index-aligned with <see cref="Batches"/>).
    /// </summary>
    public (Model Model, W3dRenderVertex[][] Vertices) CreateRuntimeModel(GraphicsDevice device)
    {
        var runtime = new Model { Skeleton = Model.Skeleton, BoundingBox = Model.BoundingBox, BoundingSphere = Model.BoundingSphere };
        foreach (var material in Model.Materials) runtime.Materials.Add(material);
        var liveVertices = new W3dRenderVertex[Model.Meshes.Count][];
        for (var i = 0; i < Model.Meshes.Count; i++)
        {
            var source = Model.Meshes[i];
            var vertices = (W3dRenderVertex[])Batches[i].Vertices.Clone();
            liveVertices[i] = vertices;
            var vertexBuffer = global::Stride.Graphics.Buffer.Vertex.New(device, vertices, GraphicsResourceUsage.Dynamic);
            runtime.Meshes.Add(new Mesh
            {
                NodeIndex = source.NodeIndex,
                Name = source.Name,
                MaterialIndex = source.MaterialIndex,
                BoundingBox = source.BoundingBox,
                BoundingSphere = source.BoundingSphere,
                Draw = new MeshDraw
                {
                    StartLocation = 0,
                    PrimitiveType = PrimitiveType.TriangleList,
                    DrawCount = vertices.Length,
                    VertexBuffers = [new VertexBufferBinding(vertexBuffer, W3dRenderVertex.Layout, vertices.Length)],
                    IndexBuffer = source.Draw.IndexBuffer,
                },
            });
        }

        return (runtime, liveVertices);
    }
}

internal sealed class SkinBatchTemplate
{
    public required W3dRenderBatch Batch { get; init; }
    public required W3dRenderVertex[] Vertices { get; init; }
    public required int[] SourceIndices { get; init; }
    public required int MaterialIndex { get; init; }
    public required bool IsHouseColor { get; init; }
}

/// <summary>
/// A loaded, GPU-ready W3D model, shared by every instance that draws it: one Stride <see cref="Model"/> for the rigid parts of the most
/// detailed LOD (one skeleton node per rigid part, written directly every frame — see the object layer's remarks on skeleton-node
/// instancing), one tiny one-node <see cref="Model"/> template per skinned part, and the neutral materials. Per-team ZHC-texture
/// materials are cached and applied per instance through <see cref="ModelComponent.Materials"/>, so most assets never clone their
/// <see cref="Model"/>; only assets with HOUSECOLOR vertex-tinted meshes get lazily built, cached whole-model team variants
/// (<see cref="GetRigidModel"/>), because that tint is baked into the vertex buffer.
/// </summary>
internal sealed class ModelAsset
{
    private readonly RenderServices _services;
    private readonly Dictionary<uint, Model> _teamModels = [];
    private readonly Dictionary<(int MaterialIndex, uint TeamHex), Material> _teamMaterials = [];
    private readonly Dictionary<(int MaterialIndex, uint TeamHex), Material> _dimmedMaterials = [];

    /// <summary>The brightness of an object in the fog (a fog level of one half squared for the linear pipeline, as the terrain layer does).</summary>
    public const float FogBrightness = 0.25f;
    private double _animatedBatchesUpdatedAtSeconds = double.NaN;

    private ModelAsset(
        string name, RenderServices services, W3dModel model, Model neutralModel,
        RigidPart[] rigidParts, MeshInfo[] meshInfos, SkinPart[] skinParts,
        float radius, float height, bool hasHouseColorMeshes)
    {
        Name = name;
        _services = services;
        Model = model;
        NeutralModel = neutralModel;
        RigidParts = rigidParts;
        MeshInfos = meshInfos;
        SkinParts = skinParts;
        Radius = radius;
        Height = height;
        HasHouseColorMeshes = hasHouseColorMeshes;
    }

    public string Name { get; }
    public W3dModel Model { get; }
    public Model NeutralModel { get; }
    /// <summary>Concrete array (not <c>IReadOnlyList</c>) so the object layer's per-frame <c>foreach</c> over it does not box its enumerator.</summary>
    public RigidPart[] RigidParts { get; }

    /// <summary>Aligned 1:1 with <see cref="NeutralModel"/>'s <c>Meshes</c> list.</summary>
    public MeshInfo[] MeshInfos { get; }

    /// <summary>Concrete array so <c>foreach</c> over it does not box its enumerator (see <see cref="RigidParts"/>).</summary>
    public SkinPart[] SkinParts { get; }

    /// <summary>Approximate asset radius (rest pose, all parts) used as the frustum-culling bounding sphere; about 25% + 2 units of slack.</summary>
    public float Radius { get; }

    /// <summary>Approximate <c>GeometryInfo::getMaxHeightAbovePosition</c> (max rest-pose Z above the pivot), for the construction-rise offset.</summary>
    public float Height { get; }

    public bool HasHouseColorMeshes { get; }

    /// <summary>The rigid model to assign for a team: <see cref="NeutralModel"/> unless the asset has HOUSECOLOR meshes, built and cached lazily.</summary>
    public Model GetRigidModel(ColorValue team)
    {
        if (!HasHouseColorMeshes) return NeutralModel;
        var hex = Hex(team);
        if (_teamModels.TryGetValue(hex, out var cached)) return cached;
        var built = BuildTeamVariant(team);
        _teamModels[hex] = built;
        return built;
    }

    /// <summary>
    /// The material to use for slot <paramref name="materialIndex"/> for a team, if it samples a ZHC texture or the object is in the fog (<paramref name="dimmed"/>: the same
    /// material darkened by <see cref="FogBrightness"/>, built lazily and cached); else null (use the shared one).
    /// </summary>
    public Material? GetTeamMaterial(int materialIndex, ColorValue team, bool dimmed = false)
    {
        var info = FindByMaterialIndex(materialIndex);
        if (info is null) return null;
        if (dimmed) return GetDimmedMaterial(materialIndex, info, team);
        if (!info.UsesHouseColorTexture) return null;
        var key = (materialIndex, Hex(team));
        if (_teamMaterials.TryGetValue(key, out var cached)) return cached;
        _services.Textures.HouseColor = team;
        Material built;
        try
        {
            built = _services.Materials.Create(info.Mesh, info.Batch);
        }
        finally
        {
            _services.Textures.HouseColor = null;
        }

        _teamMaterials[key] = built;
        return built;
    }

    private Material GetDimmedMaterial(int materialIndex, MeshInfo info, ColorValue team)
    {
        var key = (materialIndex, info.UsesHouseColorTexture ? Hex(team) : 0u);
        if (_dimmedMaterials.TryGetValue(key, out var cached)) return cached;
        if (info.UsesHouseColorTexture) _services.Textures.HouseColor = team;
        _services.Materials.Dim = FogBrightness;
        Material built;
        try
        {
            built = _services.Materials.Create(info.Mesh, info.Batch);
        }
        finally
        {
            _services.Materials.Dim = 1f;
            _services.Textures.HouseColor = null;
        }

        _dimmedMaterials[key] = built;
        return built;
    }

    /// <summary>
    /// Meshes whose index or vertex buffer was disposed or is smaller than the draw they issue (the failure mode of the old shared index buffer, which disposed a buffer
    /// that earlier meshes still bound: triangles silently vanished, docs/DEMO-FINDINGS.md #2). Zero for a healthy asset.
    /// </summary>
    public int CountGpuResourceProblems()
    {
        var problems = 0;
        foreach (var model in _teamModels.Values.Prepend(NeutralModel))
        {
            foreach (var mesh in model.Meshes)
            {
                var draw = mesh.Draw;
                if (draw.IndexBuffer is { } index && (index.Buffer.IsDisposed || index.Count > index.Buffer.ElementCount || draw.DrawCount > index.Count)) problems++;
                foreach (var vertexBuffer in draw.VertexBuffers)
                    if (vertexBuffer.Buffer.IsDisposed || draw.DrawCount > vertexBuffer.Count) problems++;
            }
        }

        return problems;
    }

    /// <summary>Recomputes every rigid batch with an animated texture mapper once per distinct frame time (shared by all instances of this asset).</summary>
    public void RefreshAnimatedBatches(CommandList commandList, double totalSeconds)
    {
        if (totalSeconds == _animatedBatchesUpdatedAtSeconds) return;
        _animatedBatchesUpdatedAtSeconds = totalSeconds;
        for (var i = 0; i < MeshInfos.Length; i++)
        {
            var info = MeshInfos[i];
            if (!info.IsAnimatedMapper) continue;
            var vertices = ObjectRenderVertexBuilder.Build(info.Mesh, info.Batch, totalSeconds).Vertices;
            var buffer = (global::Stride.Graphics.Buffer)NeutralModel.Meshes[i].Draw.VertexBuffers[0].Buffer;
            buffer.SetData(commandList, vertices);
        }
    }

    public static ModelAsset Build(string name, W3dModel model, RenderServices services, SharedIndexBuffer indices)
    {
        var lod = model.LodCount - 1;
        var renderable = model.GetLod(lod).Where(sub => sub.IsRenderable && sub.Mesh is { } mesh && mesh.Triangles.Count > 0).ToArray();
        var rigidSubs = renderable.Where(sub => !sub.IsSkinned).ToArray();
        var skinSubs = renderable.Where(sub => sub.IsSkinned).ToArray();

        var neutralModel = new Model();
        var nodes = new ModelNodeDefinition[rigidSubs.Length + 1];
        nodes[0] = new ModelNodeDefinition { ParentIndex = -1, Name = "Root", Flags = ModelNodeFlags.Default };
        var rigidParts = new List<RigidPart>();
        var meshInfos = new List<MeshInfo>();
        var minimum = new Vector3(float.MaxValue);
        var maximum = new Vector3(float.MinValue);
        var maxHeight = 0f;

        for (var partIndex = 0; partIndex < rigidSubs.Length; partIndex++)
        {
            var sub = rigidSubs[partIndex];
            var mesh = sub.Mesh!;
            var nodeIndex = partIndex + 1;
            nodes[nodeIndex] = new ModelNodeDefinition
            {
                ParentIndex = 0,
                Name = sub.Name,
                Flags = ModelNodeFlags.EnableRender | ModelNodeFlags.OverrideWorldMatrix,
            };
            rigidParts.Add(new RigidPart(nodeIndex, sub.Name, sub.BoneIndex, sub.IsVisibleByDefault));

            var restWorld = model.Skeleton.RestWorldTransforms[sub.BoneIndex];
            var (worldMin, worldMax) = TransformBounds(mesh.BoundsMinimum, mesh.BoundsMaximum, restWorld);
            minimum = System.Numerics.Vector3.Min(minimum, worldMin);
            maximum = System.Numerics.Vector3.Max(maximum, worldMax);
            maxHeight = Math.Max(maxHeight, worldMax.Z);

            foreach (var batch in W3dRenderPlan.Build(mesh))
            {
                var materialIndex = neutralModel.Materials.Count;
                neutralModel.Materials.Add(services.Materials.Create(mesh, batch));

                var built = ObjectRenderVertexBuilder.Build(mesh, batch);
                var animated = ObjectRenderVertexBuilder.HasAnimatedMapper(mesh, batch);
                var isHouseColor = IsHouseColorMesh(mesh);
                var vertexBuffer = global::Stride.Graphics.Buffer.Vertex.New(
                    services.GraphicsDevice, built.Vertices,
                    animated ? GraphicsResourceUsage.Dynamic : GraphicsResourceUsage.Default);
                var box = new BoundingBox(mesh.BoundsMinimum.ToStride(), mesh.BoundsMaximum.ToStride());
                var sphere = new BoundingSphere(mesh.SphereCenter.ToStride(), mesh.SphereRadius);
                meshInfos.Add(new MeshInfo(mesh, batch, materialIndex, isHouseColor, animated, UsesHouseColorTexture(mesh, batch)));

                neutralModel.Meshes.Add(new Mesh
                {
                    NodeIndex = nodeIndex,
                    Name = sub.Name,
                    Draw = new MeshDraw
                    {
                        StartLocation = 0,
                        PrimitiveType = PrimitiveType.TriangleList,
                        DrawCount = built.Vertices.Length,
                        VertexBuffers = [new VertexBufferBinding(vertexBuffer, W3dRenderVertex.Layout, built.Vertices.Length)],
                        IndexBuffer = indices.GetBinding(built.Vertices.Length),
                    },
                    MaterialIndex = materialIndex,
                    BoundingBox = box,
                    BoundingSphere = sphere,
                });
            }
        }

        neutralModel.Skeleton = new Skeleton { Nodes = nodes };
        neutralModel.BoundingBox = minimum.X <= maximum.X ? new BoundingBox(minimum.ToStride(), maximum.ToStride()) : new BoundingBox();
        neutralModel.BoundingSphere = BoundingSphere.FromBox(neutralModel.BoundingBox);

        var skinParts = new List<SkinPart>();
        foreach (var sub in skinSubs)
        {
            var mesh = sub.Mesh!;
            var skinModel = new Model
            {
                Skeleton = new Skeleton
                {
                    Nodes =
                    [
                        new ModelNodeDefinition { ParentIndex = -1, Name = "Root", Flags = ModelNodeFlags.Default },
                        new ModelNodeDefinition { ParentIndex = 0, Name = sub.Name, Flags = ModelNodeFlags.EnableRender | ModelNodeFlags.OverrideWorldMatrix },
                    ],
                },
            };
            var templates = new List<SkinBatchTemplate>();
            var skinMin = new Vector3(float.MaxValue);
            var skinMax = new Vector3(float.MinValue);
            foreach (var batch in W3dRenderPlan.Build(mesh))
            {
                var materialIndex = skinModel.Materials.Count;
                skinModel.Materials.Add(services.Materials.Create(mesh, batch));
                var built = ObjectRenderVertexBuilder.Build(mesh, batch);
                var isHouseColor = IsHouseColorMesh(mesh);
                templates.Add(new SkinBatchTemplate
                {
                    Batch = batch,
                    Vertices = built.Vertices,
                    SourceIndices = built.SourceIndices,
                    MaterialIndex = materialIndex,
                    IsHouseColor = isHouseColor,
                });

                var vertexBuffer = global::Stride.Graphics.Buffer.Vertex.New(services.GraphicsDevice, built.Vertices, GraphicsResourceUsage.Dynamic);
                skinModel.Meshes.Add(new Mesh
                {
                    NodeIndex = 1,
                    Name = sub.Name,
                    Draw = new MeshDraw
                    {
                        StartLocation = 0,
                        PrimitiveType = PrimitiveType.TriangleList,
                        DrawCount = built.Vertices.Length,
                        VertexBuffers = [new VertexBufferBinding(vertexBuffer, W3dRenderVertex.Layout, built.Vertices.Length)],
                        IndexBuffer = indices.GetBinding(built.Vertices.Length),
                    },
                    MaterialIndex = materialIndex,
                    BoundingBox = new BoundingBox(mesh.BoundsMinimum.ToStride(), mesh.BoundsMaximum.ToStride()),
                    BoundingSphere = new BoundingSphere(mesh.SphereCenter.ToStride(), mesh.SphereRadius),
                });
                skinMin = System.Numerics.Vector3.Min(skinMin, mesh.BoundsMinimum);
                skinMax = System.Numerics.Vector3.Max(skinMax, mesh.BoundsMaximum);
            }

            skinModel.BoundingBox = new BoundingBox(skinMin.ToStride(), skinMax.ToStride());
            skinModel.BoundingSphere = BoundingSphere.FromBox(skinModel.BoundingBox);
            minimum = System.Numerics.Vector3.Min(minimum, skinMin);
            maximum = System.Numerics.Vector3.Max(maximum, skinMax);
            maxHeight = Math.Max(maxHeight, skinMax.Z);
            skinParts.Add(new SkinPart { FullName = sub.Name, BoneIndex = sub.BoneIndex, Mesh = mesh, DefaultVisible = sub.IsVisibleByDefault, Batches = templates, Model = skinModel });
        }

        var extent = maximum - minimum;
        var radius = minimum.X <= maximum.X ? MathF.Max(extent.Length() * 0.5f * 1.25f + 2f, 2f) : 2f;
        var hasHouseColor = meshInfos.Any(info => info.IsHouseColor) || skinParts.Any(part => part.Batches.Any(batch => batch.IsHouseColor));

        return new ModelAsset(name, services, model, neutralModel, rigidParts.ToArray(), meshInfos.ToArray(), skinParts.ToArray(),
            radius, maxHeight <= 0 ? radius : maxHeight, hasHouseColor);
    }

    private Model BuildTeamVariant(ColorValue team)
    {
        var variant = new Model
        {
            Skeleton = NeutralModel.Skeleton,
            BoundingBox = NeutralModel.BoundingBox,
            BoundingSphere = NeutralModel.BoundingSphere,
        };
        foreach (var material in NeutralModel.Materials) variant.Materials.Add(material);
        for (var i = 0; i < NeutralModel.Meshes.Count; i++)
        {
            var mesh = NeutralModel.Meshes[i];
            variant.Meshes.Add(MeshInfos[i].IsHouseColor ? BuildHouseColorMesh(mesh, MeshInfos[i], team) : mesh);
        }

        return variant;
    }

    private Mesh BuildHouseColorMesh(Mesh source, MeshInfo info, ColorValue team)
    {
        var vertices = ObjectRenderVertexBuilder.Build(info.Mesh, info.Batch).Vertices;
        Layers.Objects.HouseColor.TintVertexColors(vertices, team, info.Batch.Passes.Count);
        var vertexBuffer = global::Stride.Graphics.Buffer.Vertex.New(_services.GraphicsDevice, vertices, GraphicsResourceUsage.Default);
        return new Mesh
        {
            NodeIndex = source.NodeIndex,
            Name = source.Name,
            MaterialIndex = source.MaterialIndex,
            BoundingBox = source.BoundingBox,
            BoundingSphere = source.BoundingSphere,
            Draw = new MeshDraw
            {
                StartLocation = 0,
                PrimitiveType = PrimitiveType.TriangleList,
                DrawCount = vertices.Length,
                VertexBuffers = [new VertexBufferBinding(vertexBuffer, W3dRenderVertex.Layout, vertices.Length)],
                IndexBuffer = source.Draw.IndexBuffer,
            },
        };
    }

    /// <summary>
    /// <see cref="MeshInfos"/> is built in the same loop that assigns material slots (one batch, one material, one mesh, in order), so
    /// slot <paramref name="materialIndex"/> is always at that same list index; indexing directly avoids a per-call search (and, with a
    /// LINQ predicate, a per-call closure allocation) on this frequently-called, per-instance, per-material-slot path.
    /// </summary>
    private MeshInfo? FindByMaterialIndex(int materialIndex) =>
        (uint)materialIndex < (uint)MeshInfos.Length ? MeshInfos[materialIndex] : null;

    private static bool IsHouseColorMesh(W3dMesh mesh) => mesh.Name.StartsWith("HOUSECOLOR", StringComparison.OrdinalIgnoreCase);

    private static bool UsesHouseColorTexture(W3dMesh mesh, W3dRenderBatch batch)
    {
        foreach (var pass in batch.Passes)
        {
            foreach (var textureId in pass.TextureIds)
            {
                if (textureId is not { } id || id >= mesh.Materials.Textures.Count) continue;
                if (Layers.Objects.HouseColor.IsHouseColorTexture(mesh.Materials.Textures[checked((int)id)].Name)) return true;
            }
        }

        return false;
    }

    private static (Vector3 Min, Vector3 Max) TransformBounds(Vector3 min, Vector3 max, Matrix4x4 transform)
    {
        var corners = new[]
        {
            new Vector3(min.X, min.Y, min.Z), new Vector3(max.X, min.Y, min.Z),
            new Vector3(min.X, max.Y, min.Z), new Vector3(max.X, max.Y, min.Z),
            new Vector3(min.X, min.Y, max.Z), new Vector3(max.X, min.Y, max.Z),
            new Vector3(min.X, max.Y, max.Z), new Vector3(max.X, max.Y, max.Z),
        };
        var outMin = new Vector3(float.MaxValue);
        var outMax = new Vector3(float.MinValue);
        foreach (var corner in corners)
        {
            var transformed = Vector3.Transform(corner, transform);
            outMin = System.Numerics.Vector3.Min(outMin, transformed);
            outMax = System.Numerics.Vector3.Max(outMax, transformed);
        }

        return (outMin, outMax);
    }

    private static uint Hex(ColorValue color) => ((uint)color.Red << 16) | ((uint)color.Green << 8) | color.Blue;
}
