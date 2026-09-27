using CnCNew.Content;
using CnCNew.W3D;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;

namespace CnCNew.Client.Stride;

internal sealed class W3dStrideScene : IDisposable
{
    private readonly GraphicsDevice _graphicsDevice;
    private readonly W3dTextureCache _textures;
    private readonly List<global::Stride.Graphics.Buffer> _buffers = [];
    private readonly List<AnimatedBuffer> _animatedBuffers = [];
    private readonly Dictionary<MaterialPass, bool> _depthWrites = [];

    public W3dStrideScene(GraphicsDevice graphicsDevice, ContentFileSystem content)
    {
        _graphicsDevice = graphicsDevice;
        _textures = new W3dTextureCache(graphicsDevice, content);
    }

    public IReadOnlyDictionary<MaterialPass, bool> DepthWrites => _depthWrites;
    public int TextureCount => _textures.LoadedCount;
    public int MissingTextureCount => _textures.MissingCount;
    public int RenderBatchCount { get; private set; }
    public int MaterialPassCount { get; private set; }
    public int AnimatedBufferCount => _animatedBuffers.Count;

    public Entity Build(IReadOnlyList<W3dMesh> meshes)
    {
        var model = new Model();
        var factory = new W3dMaterialFactory(_graphicsDevice, _textures, _depthWrites);
        var allPositions = meshes.SelectMany(mesh => mesh.Vertices).ToArray();
        var minimum = new System.Numerics.Vector3(
            allPositions.Min(position => position.X),
            allPositions.Min(position => position.Y),
            allPositions.Min(position => position.Z));
        var maximum = new System.Numerics.Vector3(
            allPositions.Max(position => position.X),
            allPositions.Max(position => position.Y),
            allPositions.Max(position => position.Z));
        var center = (minimum + maximum) * 0.5f;
        var extent = maximum - minimum;
        var scale = 2.5f / Math.Max(extent.X, Math.Max(extent.Y, extent.Z));

        foreach (var sourceMesh in meshes)
        {
            foreach (var batch in W3dRenderPlan.Build(sourceMesh))
            {
                var material = factory.Create(sourceMesh, batch);
                var materialIndex = model.Materials.Count;
                model.Materials.Add(material);
                var vertices = CreateVertices(sourceMesh, batch, center, scale, 0);
                var animated = HasAnimatedMapper(sourceMesh, batch);
                var vertexBuffer = global::Stride.Graphics.Buffer.Vertex.New(
                    _graphicsDevice,
                    vertices,
                    animated ? GraphicsResourceUsage.Dynamic : GraphicsResourceUsage.Default);
                var indices = Enumerable.Range(0, vertices.Length).ToArray();
                var indexBuffer = global::Stride.Graphics.Buffer.Index.New(_graphicsDevice, indices);
                _buffers.Add(vertexBuffer);
                _buffers.Add(indexBuffer);
                if (animated)
                {
                    _animatedBuffers.Add(new AnimatedBuffer(vertexBuffer, sourceMesh, batch, center, scale));
                }

                model.Meshes.Add(new Mesh
                {
                    Draw = new MeshDraw
                    {
                        StartLocation = 0,
                        PrimitiveType = PrimitiveType.TriangleList,
                        DrawCount = indices.Length,
                        VertexBuffers = [new VertexBufferBinding(vertexBuffer, W3dRenderVertex.Layout, vertices.Length)],
                        IndexBuffer = new IndexBufferBinding(indexBuffer, true, indices.Length),
                    },
                    MaterialIndex = materialIndex,
                    BoundingBox = new BoundingBox(new Vector3(-2), new Vector3(2)),
                    BoundingSphere = new BoundingSphere(Vector3.Zero, 2),
                });
                RenderBatchCount++;
                MaterialPassCount += Math.Max(1, batch.Passes.Count);
            }
        }

        model.BoundingBox = new BoundingBox(new Vector3(-2), new Vector3(2));
        model.BoundingSphere = new BoundingSphere(Vector3.Zero, 2);
        return new Entity("W3D model") { new ModelComponent(model) };
    }

    public void Update(CommandList commandList, double totalSeconds)
    {
        foreach (var animated in _animatedBuffers)
        {
            var vertices = CreateVertices(animated.Mesh, animated.Batch, animated.Center, animated.Scale, totalSeconds);
            animated.Buffer.SetData(commandList, vertices);
        }
    }

    public void Dispose()
    {
        foreach (var buffer in _buffers)
        {
            buffer.Dispose();
        }

        _buffers.Clear();
        _animatedBuffers.Clear();
        _textures.Dispose();
    }

    private static bool HasAnimatedMapper(W3dMesh mesh, W3dRenderBatch batch)
    {
        foreach (var pass in batch.Passes)
        {
            foreach (var triangleIndex in batch.TriangleIndices)
            {
                var triangle = mesh.Triangles[triangleIndex];
                foreach (var vertexIndex in new[] { triangle.Vertex0, triangle.Vertex1, triangle.Vertex2 })
                {
                    for (var stageIndex = 0; stageIndex < pass.MaterialPass.TextureStages.Count; stageIndex++)
                    {
                        if (W3dRenderPlan.GetTextureMapper(mesh, pass.MaterialPass, checked((int)vertexIndex), stageIndex)
                            is W3dLinearOffsetTextureMapper or W3dGridTextureMapper)
                        {
                            return true;
                        }
                    }
                }
            }
        }

        return false;
    }

    private static W3dRenderVertex[] CreateVertices(
        W3dMesh mesh,
        W3dRenderBatch batch,
        System.Numerics.Vector3 center,
        float scale,
        double totalSeconds)
    {
        var output = new List<W3dRenderVertex>(batch.TriangleIndices.Count * 3);
        foreach (var triangleIndex in batch.TriangleIndices)
        {
            var triangle = mesh.Triangles[triangleIndex];
            AddVertex(output, mesh, batch, triangleIndex, 0, triangle.Vertex0, center, scale, totalSeconds);
            AddVertex(output, mesh, batch, triangleIndex, 1, triangle.Vertex1, center, scale, totalSeconds);
            AddVertex(output, mesh, batch, triangleIndex, 2, triangle.Vertex2, center, scale, totalSeconds);
        }

        return output.ToArray();
    }

    private static void AddVertex(
        List<W3dRenderVertex> output,
        W3dMesh mesh,
        W3dRenderBatch batch,
        int triangleIndex,
        int corner,
        uint vertexIndex,
        System.Numerics.Vector3 center,
        float scale,
        double totalSeconds)
    {
        var index = checked((int)vertexIndex);
        var sourcePosition = (mesh.Vertices[index] - center) * scale;
        var sourceNormal = mesh.Normals.Count == mesh.Vertices.Count ? mesh.Normals[index] : System.Numerics.Vector3.UnitZ;
        var displayPosition = new System.Numerics.Vector3(sourcePosition.X, sourcePosition.Z, -sourcePosition.Y);
        var displayNormal = System.Numerics.Vector3.Normalize(
            new System.Numerics.Vector3(sourceNormal.X, sourceNormal.Z, -sourceNormal.Y));
        var viewDirection = System.Numerics.Vector3.Normalize(
            new System.Numerics.Vector3(0, 0.35f, 4) - displayPosition);
        var colors = new Color[4];
        var coordinates = new Vector2[4];
        Array.Fill(colors, Color.White);
        for (var passIndex = 0; passIndex < batch.Passes.Count; passIndex++)
        {
            var pass = batch.Passes[passIndex].MaterialPass;
            var primary = W3dRenderPlan.GetVertexColor(mesh, pass, index);
            var secondary = W3dRenderPlan.GetSecondaryVertexColor(pass, index);
            colors[passIndex * 2] = new Color(primary.Red, primary.Green, primary.Blue, primary.Alpha);
            colors[passIndex * 2 + 1] = new Color(secondary.Red, secondary.Green, secondary.Blue, 255);
            for (var stageIndex = 0; stageIndex < pass.TextureStages.Count; stageIndex++)
            {
                var mapper = W3dRenderPlan.GetTextureMapper(mesh, pass, index, stageIndex);
                var uv = W3dRenderPlan.GetTextureCoordinate(
                    pass.TextureStages[stageIndex],
                    triangleIndex,
                    corner,
                    index,
                    mapper,
                    totalSeconds,
                    displayNormal,
                    viewDirection);
                coordinates[passIndex * 2 + stageIndex] = new Vector2(uv.X, uv.Y);
            }
        }

        output.Add(new W3dRenderVertex(
            new Vector3(displayPosition.X, displayPosition.Y, displayPosition.Z),
            new Vector3(displayNormal.X, displayNormal.Y, displayNormal.Z),
            colors[0], colors[1], colors[2], colors[3],
            coordinates[0], coordinates[1], coordinates[2], coordinates[3]));
    }

    private sealed record AnimatedBuffer(
        global::Stride.Graphics.Buffer Buffer,
        W3dMesh Mesh,
        W3dRenderBatch Batch,
        System.Numerics.Vector3 Center,
        float Scale);
}
