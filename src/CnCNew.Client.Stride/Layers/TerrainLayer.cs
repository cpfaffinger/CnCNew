using System.Diagnostics;
using System.Runtime.InteropServices;
using CnCNew.Client.Stride.Layers.Terrain;
using CnCNew.Client.Stride.Rendering;
using CnCNew.Game.Live;
using CnCNew.Presentation.Minimap;
using CnCNew.Presentation.Terrain;
using CnCNew.W3D;
using Stride.Core.Mathematics;
using Stride.Engine;
using Stride.Graphics;
using Stride.Rendering;
using StrideBuffer = Stride.Graphics.Buffer;

namespace CnCNew.Client.Stride.Layers;

/// <summary>
/// Terrain, water, fog of war and minimap texture of a live match (<c>docs/CLIENT-TERRAIN.md</c>). The chunk meshes of the
/// <see cref="TerrainRenderModel"/> are merged to one opaque pass per chunk (base, blend and 3-way tile evaluated in one material), water
/// follows as transparent batches, and both are darkened by <see cref="FogTexture"/>, which is refreshed from the match's fog grid.
/// </summary>
internal sealed class TerrainLayer : IWorldLayer
{
    private static readonly VertexDeclaration TerrainLayout = new(
        VertexElement.Position<Vector3>(),
        VertexElement.Color<Color>(0),
        VertexElement.Color<Color>(1),
        VertexElement.TextureCoordinate<Vector2>(0),
        VertexElement.TextureCoordinate<Vector2>(1),
        VertexElement.TextureCoordinate<Vector2>(2),
        VertexElement.TextureCoordinate<Vector2>(3));

    private static readonly VertexDeclaration WaterLayout = new(
        VertexElement.Position<Vector3>(),
        VertexElement.Color<Color>(0),
        VertexElement.TextureCoordinate<Vector2>(0),
        VertexElement.TextureCoordinate<Vector2>(3));

    /// <summary>Fog level sampling interval (seconds); the simulation changes fog at most every few ticks.</summary>
    private const double FogInterval = 1.0 / 20.0;

    /// <summary>Minimap recomposition interval (seconds).</summary>
    private const double MinimapInterval = 0.25;

    private readonly List<IDisposable> _resources = [];
    private readonly List<Entity> _entities = [];
    private readonly List<ModelComponent> _chunkComponents = [];
    private readonly List<WaterDraw> _waterDraws = [];
    private RenderServices? _services;
    private LiveGame? _match;
    private bool[] _chunkVisible = [];
    private byte[] _fogLevels = [];
    private byte[] _fogUpload = [];
    private double _nextFogSample;
    private bool _fogUploaded;

    /// <summary>Debug aid (<c>CNCNEW_TERRAIN_REVEAL=1</c>): shows the whole map regardless of the match's fog; the simulation is not touched.</summary>
    private readonly bool _revealAll = Environment.GetEnvironmentVariable("CNCNEW_TERRAIN_REVEAL") == "1";
    private double _nextMinimap;
    private bool _minimapDirty = true;
    private IReadOnlyList<MinimapDot> _dots = [];
    private IReadOnlyList<System.Numerics.Vector2>? _footprintOverride;
    private System.Numerics.Vector2[] _lastFootprint = [];
    private int _dotSize = 2;
    private long _gpuBytes;

    /// <summary>Debug aid (<c>CNCNEW_TERRAIN_PROFILE=1</c>): accumulates <see cref="Update"/> wall time and prints the per-frame average on <see cref="Dispose"/>.</summary>
    private readonly bool _profile = Environment.GetEnvironmentVariable("CNCNEW_TERRAIN_PROFILE") == "1";
    private readonly Stopwatch _profileWatch = new();
    private long _profileFrames;

    public string Name => "terrain";

    /// <summary>
    /// Fog of war as an R8 texture, one texel per fog cell over the playable area (0 = shroud, 127 = fogged, 255 = visible). Sample it with
    /// <c>uv = world.xy * <see cref="FogUvScale"/></c> using linear filtering and clamping; world positions outside the playable area land on
    /// the edge texel. Valid after <see cref="Attach"/>.
    /// </summary>
    public Texture FogTexture { get; private set; } = null!;

    /// <summary>Multiply a world (x, y) position by this to get the fog texture coordinate.</summary>
    public System.Numerics.Vector2 FogUvScale { get; private set; }

    /// <summary>The minimap as an RGBA texture (row 0 = north), composed from terrain, fog, entity dots and the camera footprint.</summary>
    public Texture MinimapTexture { get; private set; } = null!;

    /// <summary>GPU memory held by this layer (meshes, atlas, water, fog and minimap textures).</summary>
    public long GpuBytes => _gpuBytes;

    /// <summary>Wall time of <see cref="Attach"/> in milliseconds.</summary>
    public double AttachMilliseconds { get; private set; }

    public void Attach(RenderServices services, LiveGame match)
    {
        var watch = Stopwatch.StartNew();
        _services = services;
        _match = match;
        var device = services.GraphicsDevice;
        var model = match.TerrainModel;
        var linear = device.ColorSpace == ColorSpace.Linear;
        var fogScale = new System.Numerics.Vector2(1f / (match.Fog.CellsX * TerrainMeshMerger.CellSize), 1f / (match.Fog.CellsY * TerrainMeshMerger.CellSize));
        FogUvScale = fogScale;

        CreateFogTexture(device, match);
        CreateMinimapTexture(device, match);
        var atlas = CreateAtlas(device, model, linear);
        var lightTable = linear ? PackedColor.SrgbDecodeTable() : PackedColor.IdentityTable();
        var material = TerrainMaterials.CreateTerrain(device, atlas, FogTexture, linear, services.DepthWrites);

        var blendCells = 0;
        var extraCells = 0;
        var mismatches = 0;
        _chunkVisible = new bool[model.Chunks.Length];
        for (var i = 0; i < model.Chunks.Length; i++)
        {
            var chunk = model.Chunks[i];
            var merged = TerrainMeshMerger.Merge(model, chunk, fogScale, lightTable);
            TerrainMeshMerger.Validate(merged, chunk);
            // The model triangles are counter-clockwise seen from above; Direct3D (and Stride's default rasterizer) treats clockwise as front.
            for (var t = 0; t < merged.Indices.Length; t += 3) (merged.Indices[t + 1], merged.Indices[t + 2]) = (merged.Indices[t + 2], merged.Indices[t + 1]);
            blendCells += merged.BlendCells;
            extraCells += merged.ExtraCells;
            mismatches += merged.DiagonalMismatches;

            var vertexBuffer = StrideBuffer.Vertex.New(device, merged.Vertices, GraphicsResourceUsage.Immutable);
            var indexBuffer = StrideBuffer.Index.New(device, merged.Indices, GraphicsResourceUsage.Immutable);
            Track(vertexBuffer, merged.Vertices.Length * TerrainVertex.SizeInBytes);
            Track(indexBuffer, merged.Indices.Length * sizeof(ushort));
            var bounds = new BoundingBox(ToStride(chunk.Bounds.Min), ToStride(chunk.Bounds.Max));
            var component = CreateModel($"terrain chunk {chunk.ChunkX},{chunk.ChunkY}", material, vertexBuffer, TerrainLayout, merged.Vertices.Length,
                indexBuffer, false, merged.Indices.Length, bounds);
            _chunkComponents.Add(component);
            _chunkVisible[i] = true;
        }

        CreateWater(services, match, atlas, linear, fogScale);
        var roadWatch = Stopwatch.StartNew();
        var roads = CreateRoads(services, match, linear, fogScale, lightTable);
        roadWatch.Stop();
        watch.Stop();
        AttachMilliseconds = watch.Elapsed.TotalMilliseconds;
        Console.WriteLine(
            $"terrain: {match.Fog.CellsX}x{match.Fog.CellsY} playable cells, {model.Chunks.Length} chunks, {blendCells} blend cells, {extraCells} 3-way cells ({mismatches} with a different diagonal), " +
            $"{_waterDraws.Count} water batches, {roads.Batches} road batches ({roads.Segments} segments, {roads.Triangles} triangles, {roadWatch.Elapsed.TotalMilliseconds:F0} ms), color space {device.ColorSpace}, " +
            $"attach {AttachMilliseconds:F0} ms, gpu {_gpuBytes / (1024.0 * 1024.0):F1} MiB");
    }

    public void Update(in WorldFrame frame)
    {
        if (_profile) _profileWatch.Start();
        UpdateCore(frame);
        if (_profile)
        {
            _profileWatch.Stop();
            _profileFrames++;
        }
    }

    private void UpdateCore(in WorldFrame frame)
    {
        var match = _match;
        if (match is null) return;
        var model = match.TerrainModel;

        // Chunk culling: only flip components whose visibility changed.
        var visible = match.Camera.VisibleChunks(model);
        Span<bool> now = _chunkVisible.Length <= 1024 ? stackalloc bool[_chunkVisible.Length] : new bool[_chunkVisible.Length];
        foreach (var index in visible) now[index] = true;
        for (var i = 0; i < now.Length; i++)
        {
            if (now[i] == _chunkVisible[i]) continue;
            _chunkVisible[i] = now[i];
            _chunkComponents[i].Enabled = now[i];
        }

        UpdateFog(frame.CommandList, frame.TotalSeconds);
        foreach (var draw in _waterDraws)
        {
            if (!draw.Batch.Animated) continue;
            draw.Batch.ApplyScroll(frame.TotalSeconds);
            draw.VertexBuffer.SetData(frame.CommandList, draw.Batch.Vertices);
        }

        if (frame.TotalSeconds >= _nextMinimap)
        {
            var footprint = _footprintOverride?.ToArray() ?? match.Camera.GroundFootprint();
            if (_minimapDirty || !footprint.AsSpan().SequenceEqual(_lastFootprint)) RefreshMinimap(frame.CommandList, frame.TotalSeconds, match);
        }
    }

    /// <summary>
    /// Replaces the entity dots and the camera footprint of the minimap and recomposes it immediately. <paramref name="dots"/> are world
    /// positions with a colour (hide enemies in fog before passing them); <paramref name="cameraFootprint"/> defaults to the match camera's
    /// ground footprint. Without a call, the minimap shows terrain, fog and the camera footprint only and refreshes at 4 Hz on changes.
    /// </summary>
    public void UpdateMinimap(CommandList commandList, IReadOnlyList<MinimapDot> dots, IReadOnlyList<System.Numerics.Vector2>? cameraFootprint = null, int dotSize = 2)
    {
        ArgumentNullException.ThrowIfNull(dots);
        var match = _match ?? throw new InvalidOperationException("The terrain layer is not attached.");
        _dots = dots;
        _footprintOverride = cameraFootprint;
        _dotSize = dotSize;
        RefreshMinimap(commandList, _nextMinimap, match);
    }

    public void Dispose()
    {
        if (_profile && _profileFrames > 0)
            Console.WriteLine($"terrain: profile {_profileFrames} frames, {_profileWatch.Elapsed.TotalMilliseconds / _profileFrames:F3} ms/frame average CPU in Update");
        if (_services is { } services)
            foreach (var entity in _entities) services.Scene.Entities.Remove(entity);
        _entities.Clear();
        _chunkComponents.Clear();
        _waterDraws.Clear();
        foreach (var resource in _resources) resource.Dispose();
        _resources.Clear();
        _services = null;
        _match = null;
    }

    private void UpdateFog(CommandList commandList, double totalSeconds)
    {
        if (totalSeconds < _nextFogSample) return;
        _nextFogSample = totalSeconds + FogInterval;
        _match!.Fog.CopyLevels(_fogUpload);
        if (_revealAll) Array.Fill(_fogUpload, (byte)255);
        // A dynamic texture has no initial data, so the first sample is always uploaded.
        if (_fogUploaded && _fogUpload.AsSpan().SequenceEqual(_fogLevels)) return;
        FogTexture.SetData(commandList, _fogUpload);
        (_fogLevels, _fogUpload) = (_fogUpload, _fogLevels);
        _fogUploaded = true;
        _minimapDirty = true;
    }

    private void RefreshMinimap(CommandList commandList, double totalSeconds, LiveGame match)
    {
        var footprint = _footprintOverride?.ToArray() ?? match.Camera.GroundFootprint();
        var rgba = match.Minimap.Compose(match.Fog, _dots, footprint, _dotSize);
        MinimapTexture.SetData(commandList, rgba);
        if (Environment.GetEnvironmentVariable("CNCNEW_TERRAIN_DUMP") is { Length: > 0 } dump)
        {
            Directory.CreateDirectory(dump);
            PngDump.WriteRgba(Path.Combine(dump, "minimap.png"), match.Minimap.Width, match.Minimap.Height, rgba);
            PngDump.WriteRgba(Path.Combine(dump, "terrain.png"), match.Minimap.Width, match.Minimap.Height, match.Minimap.TerrainRgba.ToArray());
            PngDump.WriteGray(Path.Combine(dump, "fog.png"), match.Fog.CellsX, match.Fog.CellsY, _fogLevels);
        }
        _lastFootprint = footprint;
        _nextMinimap = totalSeconds + MinimapInterval;
        _minimapDirty = false;
    }

    private void CreateFogTexture(GraphicsDevice device, LiveGame match)
    {
        var cells = match.Fog.CellsX * match.Fog.CellsY;
        _fogLevels = new byte[cells];
        _fogUpload = new byte[cells];
        FogTexture = Texture.New2D(device, match.Fog.CellsX, match.Fog.CellsY, PixelFormat.R8_UNorm, TextureFlags.ShaderResource, 1, GraphicsResourceUsage.Dynamic);
        Track(FogTexture, cells);
    }

    private void CreateMinimapTexture(GraphicsDevice device, LiveGame match)
    {
        var minimap = match.Minimap;
        MinimapTexture = Texture.New2D(device, minimap.Width, minimap.Height, PixelFormat.R8G8B8A8_UNorm, TextureFlags.ShaderResource, 1, GraphicsResourceUsage.Dynamic);
        Track(MinimapTexture, minimap.Width * minimap.Height * 4);
    }

    private Texture CreateAtlas(GraphicsDevice device, TerrainRenderModel model, bool linear)
    {
        var levels = model.Atlas.BuildMipChain();
        var handles = new GCHandle[levels.Length];
        try
        {
            var boxes = new DataBox[levels.Length];
            for (var i = 0; i < levels.Length; i++)
            {
                var bytes = System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsArray(levels[i].Rgba)!;
                handles[i] = GCHandle.Alloc(bytes, GCHandleType.Pinned);
                boxes[i] = new DataBox(handles[i].AddrOfPinnedObject(), levels[i].Width * 4, levels[i].Width * levels[i].Height * 4);
            }

            var format = linear ? PixelFormat.R8G8B8A8_UNorm_SRgb : PixelFormat.R8G8B8A8_UNorm;
            var texture = Texture.New2D(device, levels[0].Width, levels[0].Height, levels.Length, format, boxes, TextureFlags.ShaderResource, 1, GraphicsResourceUsage.Immutable);
            Track(texture, model.Atlas.BytesWithMips);
            return texture;
        }
        finally
        {
            foreach (var handle in handles)
                if (handle.IsAllocated) handle.Free();
        }
    }

    private void CreateWater(RenderServices services, LiveGame match, Texture atlas, bool linear, System.Numerics.Vector2 fogScale)
    {
        var model = match.TerrainModel;
        if (model.Water.IsDefaultOrEmpty) return;
        var options = TerrainRenderOptions.FromDefinitions(match.Data.Graph);
        var additive = options.Water?.AdditiveBlending ?? false;
        var batches = WaterMeshBuilder.Build(model.Water, fogScale, linear ? PackedColor.SrgbDecodeTable() : PackedColor.IdentityTable());
        var materials = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        foreach (var batch in batches)
        {
            if (!materials.TryGetValue(batch.Texture, out var material))
            {
                var texture = services.Textures.Resolve(new W3dTextureReference(batch.Texture));
                material = TerrainMaterials.CreateWater(services.GraphicsDevice, texture, FogTexture, linear, additive, services.DepthWrites);
                materials.Add(batch.Texture, material);
            }

            var device = services.GraphicsDevice;
            var usage = batch.Animated ? GraphicsResourceUsage.Dynamic : GraphicsResourceUsage.Immutable;
            var vertexBuffer = StrideBuffer.Vertex.New(device, batch.Vertices, usage);
            var indexBuffer = StrideBuffer.Index.New(device, batch.Indices, GraphicsResourceUsage.Immutable);
            Track(vertexBuffer, batch.Vertices.Length * WaterVertex.SizeInBytes);
            Track(indexBuffer, batch.Indices.Length * sizeof(uint));
            var bounds = new BoundingBox(ToStride(batch.BoundsMin), ToStride(batch.BoundsMax));
            CreateModel($"water {batch.Texture}{(batch.IsRiver ? " river" : string.Empty)}", material, vertexBuffer, WaterLayout, batch.Vertices.Length,
                indexBuffer, true, batch.Indices.Length, bounds);
            _waterDraws.Add(new WaterDraw(batch, vertexBuffer));
            Console.WriteLine($"terrain: water batch '{batch.Texture}' river={batch.IsRiver} scroll={batch.Scroll} {batch.Vertices.Length} vertices, bounds {batch.BoundsMin.X:F0},{batch.BoundsMin.Y:F0}..{batch.BoundsMax.X:F0},{batch.BoundsMax.Y:F0} z {batch.BoundsMin.Z:F1}");
        }
    }

    /// <summary>
    /// The map's roads (<c>docs/CLIENT-ROADS.md</c>): one alpha-blended mesh per road template, drawn in stacking order, lit by the terrain's static lighting and darkened
    /// by the same fog texture as terrain and water. A template whose texture does not resolve fails the attach (no magenta substitute for roads).
    /// </summary>
    private (int Batches, int Segments, int Triangles) CreateRoads(RenderServices services, LiveGame match, bool linear, System.Numerics.Vector2 fogScale, byte[] lightTable)
    {
        if (Environment.GetEnvironmentVariable("CNCNEW_ROADS") == "0") return (0, 0, 0);
        var graph = match.Data.Graph;
        var model = RoadModel.Build(match.Map.Objects, match.Heights, RoadCatalog.FromDefinitions(graph), TerrainRenderOptions.FromDefinitions(graph).Lighting);
        if (model.Meshes.IsDefaultOrEmpty) return (0, 0, 0);
        var batches = RoadMeshBuilder.Build(model, fogScale, lightTable);
        var materials = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        var device = services.GraphicsDevice;
        // Roads of different templates overlap (markings on asphalt) and are blended in stacking order. Stride sorts transparent meshes by the distance of their
        // bounding box, so every road mesh gets the same box: equal keys keep the submission order, which is the stacking order.
        var allMin = batches.Aggregate(new System.Numerics.Vector3(float.MaxValue), (min, batch) => System.Numerics.Vector3.Min(min, batch.BoundsMin));
        var allMax = batches.Aggregate(new System.Numerics.Vector3(float.MinValue), (max, batch) => System.Numerics.Vector3.Max(max, batch.BoundsMax));
        var sharedBounds = new BoundingBox(ToStride(allMin), ToStride(allMax));
        foreach (var batch in batches)
        {
            if (!materials.TryGetValue(batch.Texture, out var material))
            {
                if (!W3dTexturePathResolver.Resolve(batch.Texture, services.Content.Exists).Found)
                    throw new InvalidDataException($"Road '{batch.Template}': texture '{batch.Texture}' is not in the content.");
                var texture = services.Textures.Resolve(new W3dTextureReference(batch.Texture));
                material = TerrainMaterials.CreateRoad(device, texture, FogTexture, linear, services.DepthWrites);
                materials.Add(batch.Texture, material);
            }
            var vertexBuffer = StrideBuffer.Vertex.New(device, batch.Vertices, GraphicsResourceUsage.Immutable);
            var indexBuffer = StrideBuffer.Index.New(device, batch.Indices, GraphicsResourceUsage.Immutable);
            Track(vertexBuffer, batch.Vertices.Length * WaterVertex.SizeInBytes);
            Track(indexBuffer, batch.Indices.Length * sizeof(uint));
            CreateModel($"road {batch.Template}", material, vertexBuffer, WaterLayout, batch.Vertices.Length, indexBuffer, true, batch.Indices.Length, sharedBounds);
        }
        return (batches.Count, model.Stats.Segments, model.Stats.Triangles);
    }

    private ModelComponent CreateModel(string name, Material material, StrideBuffer vertexBuffer, VertexDeclaration layout, int vertexCount,
        StrideBuffer indexBuffer, bool wideIndices, int indexCount, BoundingBox bounds)
    {
        var sphere = BoundingSphere.FromBox(bounds);
        var model = new Model { BoundingBox = bounds, BoundingSphere = sphere };
        model.Materials.Add(material);
        model.Meshes.Add(new Mesh
        {
            Draw = new MeshDraw
            {
                PrimitiveType = PrimitiveType.TriangleList,
                DrawCount = indexCount,
                VertexBuffers = [new VertexBufferBinding(vertexBuffer, layout, vertexCount)],
                IndexBuffer = new IndexBufferBinding(indexBuffer, wideIndices, indexCount),
            },
            MaterialIndex = 0,
            BoundingBox = bounds,
            BoundingSphere = sphere,
        });
        var component = new ModelComponent(model);
        var entity = new Entity(name) { component };
        _services!.Scene.Entities.Add(entity);
        _entities.Add(entity);
        return component;
    }

    private void Track(IDisposable resource, long bytes)
    {
        _resources.Add(resource);
        _gpuBytes += bytes;
    }

    private static Vector3 ToStride(System.Numerics.Vector3 value) => new(value.X, value.Y, value.Z);

    private sealed record WaterDraw(WaterBatch Batch, StrideBuffer VertexBuffer);
}
