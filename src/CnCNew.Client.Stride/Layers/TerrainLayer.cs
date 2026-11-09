using System.Diagnostics;
using System.Runtime.InteropServices;
using CnCNew.Client.Stride.Layers.Terrain;
using CnCNew.Client.Stride.Rendering;
using CnCNew.Game.Data;
using CnCNew.Game.Live;
using CnCNew.Images;
using CnCNew.Presentation;
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
    private readonly List<BridgeDraw> _bridgeDraws = [];
    private RenderServices? _services;
    private LiveGame? _match;
    private bool[] _chunkVisible = [];
    private byte[] _fogCells = [];
    private byte[] _fogLevels = [];
    private byte[] _fogUpload = [];
    private double _nextFogSample;
    private bool _fogUploaded;
    private FogMap? _fogMap;
    private int _borderShroud;
    private int _borderClear = 255;
    private TerrainOverlay? _overlay;
    private byte[] _cloudSource = [];
    private byte[] _cloudPicture = [];
    private float _cloudU;
    private float _cloudV;
    private bool _cloudUploaded;

    /// <summary>Debug aid (<c>CNCNEW_TERRAIN_REVEAL=1</c>): shows the whole map regardless of the match's fog; the simulation is not touched.</summary>
    private readonly bool _revealAll = Environment.GetEnvironmentVariable("CNCNEW_TERRAIN_REVEAL") == "1";
    private double _nextMinimap;
    private bool _minimapDirty = true;
    private IReadOnlyList<MinimapDot> _dots = [];
    private int _dotSize = 2;
    private long _gpuBytes;
    private Material? _terrainMaterial;
    private System.Numerics.Vector2 _fogScale;
    private byte[] _lightTable = [];
    private readonly List<(IDisposable Resource, int Frames)> _retired = [];

    /// <summary>Debug aid (<c>CNCNEW_TERRAIN_PROFILE=1</c>): accumulates <see cref="Update"/> wall time and prints the per-frame average on <see cref="Dispose"/>.</summary>
    private readonly bool _profile = Environment.GetEnvironmentVariable("CNCNEW_TERRAIN_PROFILE") == "1";
    private readonly Stopwatch _profileWatch = new();
    private long _profileFrames;

    public string Name => "terrain";

    /// <summary>
    /// Fog of war as an R8 texture: one texel per fog cell over the playable area (0 = shroud, 127 = fogged, 255 = visible) inside a ring of one texel that holds the level of the map's
    /// border, which world positions outside the playable area land on by clamping. Valid after <see cref="Attach"/>.
    /// </summary>
    public Texture FogTexture { get; private set; } = null!;

    /// <summary>Multiply a world (x, y) position by this to get the fog coordinate; 0 to 1 over the playable area, before the ring of the texture.</summary>
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
        _borderShroud = match.Environment.BorderShroudLevel;
        _borderClear = match.Environment.BorderClearLevel;
        CreateMinimapTexture(device, match);
        var atlas = CreateAtlas(device, model, linear);
        var lightTable = linear ? PackedColor.SrgbDecodeTable() : PackedColor.IdentityTable();
        _overlay = CreateOverlay(services, match, linear);
        var material = _terrainMaterial = TerrainMaterials.CreateTerrain(device, atlas, _fogMap!, linear, services.DepthWrites, _overlay);
        _fogScale = fogScale;
        _lightTable = lightTable;

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

        match.TakeChangedTerrainChunks();
        CreateWater(services, match, linear, fogScale);
        var roadWatch = Stopwatch.StartNew();
        var roads = CreateRoads(services, match, linear, fogScale, lightTable);
        CreateBridges(services, match, linear, fogScale, lightTable);
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

        ReplaceChangedChunks(match);
        UpdateFog(frame.CommandList, frame.TotalSeconds);
        UpdateClouds(frame.CommandList);
        foreach (var draw in _bridgeDraws)
            if (draw.Index < match.World.Bridges.Count) draw.Show(match.World.Bridges[draw.Index].DamageState);
        foreach (var draw in _waterDraws)
        {
            var moved = MoveWater(draw.Batch, match);
            if (draw.Batch.Animated) draw.Batch.ApplyScroll(frame.AnimationSeconds);
            if (moved || draw.Batch.Animated) draw.VertexBuffer.SetData(frame.CommandList, draw.Batch.Vertices);
        }

        if (frame.TotalSeconds >= _nextMinimap && _minimapDirty) RefreshMinimap(frame.CommandList, frame.TotalSeconds, match);
    }

    /// <summary>
    /// Replaces the entity dots of the minimap and recomposes it immediately. <paramref name="dots"/> are world positions with a colour (hide enemies in fog before passing
    /// them). Without a call, the minimap shows terrain and fog only and refreshes at 4 Hz on changes. The camera's frame is not part of the picture: the HUD draws it over the
    /// radar in every frame, because the camera moves in every frame.
    /// </summary>
    public void UpdateMinimap(CommandList commandList, IReadOnlyList<MinimapDot> dots, int dotSize = 2)
    {
        ArgumentNullException.ThrowIfNull(dots);
        var match = _match ?? throw new InvalidOperationException("The terrain layer is not attached.");
        _dots = dots;
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
        foreach (var retired in _retired) retired.Resource.Dispose();
        _retired.Clear();
        _waterDraws.Clear();
        _bridgeDraws.Clear();
        foreach (var resource in _resources) resource.Dispose();
        _resources.Clear();
        _services = null;
        _match = null;
    }

    private void UpdateFog(CommandList commandList, double totalSeconds)
    {
        if (totalSeconds < _nextFogSample) return;
        _nextFogSample = totalSeconds + FogInterval;
        var match = _match!;
        match.Fog.CopyLevels(_fogCells);
        if (_revealAll) Array.Fill(_fogCells, (byte)255);
        // W3DShroud::render: the first row and column of the texture, and what lies beyond the playable area, hold the border's level.
        var ring = (byte)(_revealAll || !match.BorderShroudShown ? _borderClear : _borderShroud);
        var (cellsX, cellsY) = (match.Fog.CellsX, match.Fog.CellsY);
        var stride = cellsX + 2;
        Array.Fill(_fogUpload, ring);
        for (var row = 0; row < cellsY; row++) _fogCells.AsSpan(row * cellsX, cellsX).CopyTo(_fogUpload.AsSpan((row + 1) * stride + 1));
        // A dynamic texture has no initial data, so the first sample is always uploaded.
        if (_fogUploaded && _fogUpload.AsSpan().SequenceEqual(_fogLevels)) return;
        FogTexture.SetData(commandList, _fogUpload);
        (_fogLevels, _fogUpload) = (_fogUpload, _fogLevels);
        _fogUploaded = true;
        _minimapDirty = true;
    }

    /// <summary>
    /// A building that levels its ground makes new chunks of the terrain model (<see cref="LiveGame.TakeChangedTerrainChunks"/>): their merged meshes are made again and the
    /// buffers of the chunk are replaced. The old buffers are let go a few frames later, when no command list in flight can still name them.
    /// </summary>
    private void ReplaceChangedChunks(LiveGame match)
    {
        for (var i = _retired.Count - 1; i >= 0; i--)
        {
            if (_retired[i].Frames > 0) { _retired[i] = _retired[i] with { Frames = _retired[i].Frames - 1 }; continue; }
            _retired[i].Resource.Dispose();
            _retired.RemoveAt(i);
        }
        var changed = match.TakeChangedTerrainChunks();
        if (changed.IsEmpty || _services is not { } services || _terrainMaterial is not { } material) return;
        var model = match.TerrainModel;
        var device = services.GraphicsDevice;
        foreach (var index in changed)
        {
            var chunk = model.Chunks[index];
            var merged = TerrainMeshMerger.Merge(model, chunk, _fogScale, _lightTable);
            TerrainMeshMerger.Validate(merged, chunk);
            for (var t = 0; t < merged.Indices.Length; t += 3) (merged.Indices[t + 1], merged.Indices[t + 2]) = (merged.Indices[t + 2], merged.Indices[t + 1]);
            var vertexBuffer = StrideBuffer.Vertex.New(device, merged.Vertices, GraphicsResourceUsage.Immutable);
            var indexBuffer = StrideBuffer.Index.New(device, merged.Indices, GraphicsResourceUsage.Immutable);
            var component = _chunkComponents[index];
            foreach (var old in component.Model.Meshes[0].Draw.VertexBuffers.Select(binding => (IDisposable)binding.Buffer).Append(component.Model.Meshes[0].Draw.IndexBuffer.Buffer))
            {
                _resources.Remove(old);
                _retired.Add((old, 8));
            }
            _resources.Add(vertexBuffer);
            _resources.Add(indexBuffer);
            var bounds = new BoundingBox(ToStride(chunk.Bounds.Min), ToStride(chunk.Bounds.Max));
            component.Model = BuildModel(material, vertexBuffer, TerrainLayout, merged.Vertices.Length, indexBuffer, false, merged.Indices.Length, bounds);
        }
    }

    /// <summary>The clouds slide with the game's time (<c>updateCloud</c>); a new picture only when they have moved by a part of a texel.</summary>
    private void UpdateClouds(CommandList commandList)
    {
        if (_overlay?.Clouds is not { } texture) return;
        var (u, v) = TerrainClouds.Offset(_match!.Fx.Frame / (double)CnCNew.Data.OriginalFrame.PerSecond);
        if (_cloudUploaded && MathF.Abs(u - _cloudU) + MathF.Abs(v - _cloudV) < 1f / (8f * texture.Width)) return;
        TerrainClouds.Shift(_cloudSource, texture.Width, texture.Height, u, v, _cloudPicture);
        texture.SetData(commandList, _cloudPicture);
        (_cloudU, _cloudV, _cloudUploaded) = (u, v, true);
    }

    /// <summary>What <c>BaseHeightMapRenderObjClass</c> lays over the terrain, with the switches of the game data; a missing texture fails the attach rather than draws a substitute.</summary>
    private TerrainOverlay? CreateOverlay(RenderServices services, LiveGame match, bool linear)
    {
        if (Environment.GetEnvironmentVariable("CNCNEW_CLOUDS") == "0") return null;
        var options = TerrainClouds.ForMap(match.Data.Graph, match.Map);
        if (!options.Clouds && !options.LightMap) return null;
        Texture? clouds = null;
        if (options.Clouds)
        {
            var path = W3dTexturePathResolver.Resolve(TerrainClouds.CloudTexture, services.Content.Exists);
            if (!path.Found) throw new InvalidDataException($"The cloud texture '{TerrainClouds.CloudTexture}' is not in the content.");
            using var stream = services.Content.OpenRead(path.VirtualPath!);
            var image = path.VirtualPath!.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ? DdsReader.Read(stream) : TgaReader.Read(stream);
            _cloudSource = image.RgbaPixels;
            _cloudPicture = new byte[_cloudSource.Length];
            clouds = Texture.New2D(services.GraphicsDevice, image.Width, image.Height, linear ? PixelFormat.R8G8B8A8_UNorm_SRgb : PixelFormat.R8G8B8A8_UNorm, TextureFlags.ShaderResource, 1, GraphicsResourceUsage.Dynamic);
            Track(clouds, _cloudSource.Length);
        }

        var macro = options.LightMap ? services.Textures.Resolve(new W3dTextureReference(TerrainClouds.MacroTexture)) : null;
        var scale = new Vector2(match.Fog.CellsX * TerrainMeshMerger.CellSize * TerrainClouds.Stretch, match.Fog.CellsY * TerrainMeshMerger.CellSize * TerrainClouds.Stretch);
        return new TerrainOverlay(clouds, macro, scale);
    }

    private void RefreshMinimap(CommandList commandList, double totalSeconds, LiveGame match)
    {
        var rgba = match.Minimap.Compose(match.Fog, _dots, null, _dotSize);
        MinimapTexture.SetData(commandList, rgba);
        if (Environment.GetEnvironmentVariable("CNCNEW_TERRAIN_DUMP") is { Length: > 0 } dump)
        {
            Directory.CreateDirectory(dump);
            PngDump.WriteRgba(Path.Combine(dump, "minimap.png"), match.Minimap.Width, match.Minimap.Height, rgba);
            PngDump.WriteRgba(Path.Combine(dump, "terrain.png"), match.Minimap.Width, match.Minimap.Height, match.Minimap.TerrainRgba.ToArray());
            PngDump.WriteGray(Path.Combine(dump, "fog.png"), match.Fog.CellsX + 2, match.Fog.CellsY + 2, _fogLevels);
        }
        _nextMinimap = totalSeconds + MinimapInterval;
        _minimapDirty = false;
    }

    private void CreateFogTexture(GraphicsDevice device, LiveGame match)
    {
        var (width, height) = (match.Fog.CellsX + 2, match.Fog.CellsY + 2);
        var cells = width * height;
        _fogCells = new byte[match.Fog.CellsX * match.Fog.CellsY];
        _fogLevels = new byte[cells];
        _fogUpload = new byte[cells];
        FogTexture = Texture.New2D(device, width, height, PixelFormat.R8_UNorm, TextureFlags.ShaderResource, 1, GraphicsResourceUsage.Dynamic);
        Track(FogTexture, cells);
        _fogMap = new FogMap(FogTexture, new Vector2(match.Fog.CellsX / (float)width, match.Fog.CellsY / (float)height), new Vector2(1f / width, 1f / height));
    }

    private void CreateMinimapTexture(GraphicsDevice device, LiveGame match)
    {
        var minimap = match.Minimap;
        // The radar's picture holds display values (W3DRadar composes it in a gamma-space texture and the screen shows those values as they are). In the linear pipeline
        // a plain UNorm texture is read as linear light and comes out brightened by the display curve - the fog's half level then showed at three quarters and the three
        // states of the shroud hardly differed - so it is read as sRGB, as every other picture the client draws (W3dTextureCache, UiTextures).
        var format = device.ColorSpace == ColorSpace.Linear ? PixelFormat.R8G8B8A8_UNorm_SRgb : PixelFormat.R8G8B8A8_UNorm;
        MinimapTexture = Texture.New2D(device, minimap.Width, minimap.Height, format, TextureFlags.ShaderResource, 1, GraphicsResourceUsage.Dynamic);
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

    private void CreateWater(RenderServices services, LiveGame match, bool linear, System.Numerics.Vector2 fogScale)
    {
        var model = match.TerrainModel;
        if (model.Water.IsDefaultOrEmpty) return;
        var options = TerrainRenderOptions.ForMap(match.Data.Graph, match.Map);
        var additive = options.Water?.AdditiveBlending ?? false;
        var shores = new Dictionary<float, FogMap?>();
        var batches = WaterMeshBuilder.Build(model.Water, fogScale, linear ? PackedColor.SrgbDecodeTable() : PackedColor.IdentityTable());
        var materials = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        foreach (var batch in batches)
        {
            var level = batch.BoundsMax.Z;
            if (!shores.TryGetValue(level, out var shore)) shores.Add(level, shore = CreateShore(services.GraphicsDevice, match, options.Water, level));
            var key = FormattableString.Invariant($"{batch.Texture}|{level}");
            if (!materials.TryGetValue(key, out var material))
            {
                var texture = services.Textures.Resolve(new W3dTextureReference(batch.Texture));
                material = TerrainMaterials.CreateWater(services.GraphicsDevice, texture, _fogMap!, linear, additive, services.DepthWrites, shore);
                materials.Add(key, material);
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

    /// <summary>The standing water of the batch at the levels of the world; a script can move them (<c>docs/CLIENT-TERRAIN.md</c>).</summary>
    private static bool MoveWater(WaterBatch batch, LiveGame match)
    {
        if (batch.IsRiver || match.World.Terrain is not { } terrain) return false;
        var moved = false;
        for (var area = 0; area < terrain.WaterAreas.Length; area++)
            moved |= batch.SetLevel(terrain.WaterAreas[area].Name, (float)match.World.WaterLevelOf(area).ToDecimal());
        return moved;
    }

    /// <summary>The depth of the water at the height <paramref name="level"/> as an opacity over the map, in the fog's coordinates (<c>showSoftWaterEdge</c>); none when the data switch it off.</summary>
    private FogMap? CreateShore(GraphicsDevice device, LiveGame match, WaterAppearance? water, float level)
    {
        if (water is not { TransparentWaterDepth: > 0f }) return null;
        var heights = match.Heights;
        var (width, height) = (heights.PlayableCellsX + 1, heights.PlayableCellsY + 1);
        var opacity = WaterShore.Opacity(heights, level, water.TransparentWaterDepth, water.TransparentWaterMinOpacity);
        var texture = Texture.New2D(device, width, height, PixelFormat.R8_UNorm, opacity);
        Track(texture, opacity.Length);
        // Texel centres stand on the vertices of the height field, while the fog's coordinate is 0 and 1 at the first and last vertex.
        return new FogMap(texture, new Vector2((width - 1f) / width, (height - 1f) / height), new Vector2(0.5f / width, 0.5f / height));
    }

    /// <summary>
    /// The map's roads (<c>docs/CLIENT-ROADS.md</c>): one alpha-blended mesh per road template, drawn in stacking order, lit by the terrain's static lighting and darkened
    /// by the same fog texture as terrain and water. A template whose texture does not resolve fails the attach (no magenta substitute for roads).
    /// </summary>
    private (int Batches, int Segments, int Triangles) CreateRoads(RenderServices services, LiveGame match, bool linear, System.Numerics.Vector2 fogScale, byte[] lightTable)
    {
        if (Environment.GetEnvironmentVariable("CNCNEW_ROADS") == "0") return (0, 0, 0);
        var graph = match.Data.Graph;
        var model = RoadModel.Build(match.Map.Objects, match.Heights, RoadCatalog.FromDefinitions(graph), TerrainRenderOptions.ForMap(graph, match.Map).Lighting);
        if (model.Meshes.IsDefaultOrEmpty) return (0, 0, 0);
        var batches = RoadMeshBuilder.Build(model, fogScale, lightTable);
        services.TransparentOrder.Stacking.Clear();
        var materials = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        var device = services.GraphicsDevice;
        // Roads of different templates overlap (markings on asphalt) and are blended in stacking order. Stride sorts transparent meshes by the distance of their
        // bounding box, so every road mesh gets the same box, and among equal distances the sort mode goes by the place of a road (D-7).
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
                material = TerrainMaterials.CreateRoad(device, texture, _fogMap!, linear, services.DepthWrites, _overlay);
                services.TransparentOrder.Layers[material.Passes[0]] = StackedBackToFrontSortMode.RoadLayer;
                materials.Add(batch.Texture, material);
            }
            var vertexBuffer = StrideBuffer.Vertex.New(device, batch.Vertices, GraphicsResourceUsage.Immutable);
            var indexBuffer = StrideBuffer.Index.New(device, batch.Indices, GraphicsResourceUsage.Immutable);
            Track(vertexBuffer, batch.Vertices.Length * WaterVertex.SizeInBytes);
            Track(indexBuffer, batch.Indices.Length * sizeof(uint));
            var road = CreateModel($"road {batch.Template}", material, vertexBuffer, WaterLayout, batch.Vertices.Length, indexBuffer, true, batch.Indices.Length, sharedBounds);
            services.TransparentOrder.Stacking[road.Model.Meshes[0]] = services.TransparentOrder.Stacking.Count;
        }
        return (batches.Count, model.Stats.Segments, model.Stats.Triangles);
    }

    /// <summary>
    /// The terrain bridges of the map (<c>W3DBridgeBuffer</c>, <c>docs/CLIENT-ROADS.md</c>): a mesh per bridge and damage state, one of them shown; a landmark bridge is an object and is drawn with
    /// the objects. A bridge that has no art in a state keeps showing the state before, as the original puts the old model back.
    /// </summary>
    private void CreateBridges(RenderServices services, LiveGame match, bool linear, System.Numerics.Vector2 fogScale, byte[] lightTable)
    {
        var bridges = match.World.Bridges;
        if (bridges.Count == 0) return;
        var library = new W3dAssetLibrary(path => services.Content.Exists(path) ? services.Content.OpenRead(path) : null);
        var lighting = TerrainRenderOptions.ForMap(match.Data.Graph, match.Map).Lighting;
        var materials = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        var device = services.GraphicsDevice;
        for (var index = 0; index < bridges.Count; index++)
        {
            var bridge = bridges[index];
            if (bridge.RenderIndex < 0 || !match.World.Templates.Bridges.TryGet(bridge.TemplateName, out var template)) continue;
            var (start, end) = (ToVector(bridge.From), ToVector(bridge.To));
            var meshes = new BridgeMesh?[template.Visuals.Length];
            var components = new ModelComponent?[template.Visuals.Length];
            for (var state = 0; state < template.Visuals.Length; state++)
            {
                if (BridgeArtLoader.Load(library, template.Visuals[state], template.Scale) is not { } art) continue;
                // Roads.ini names textures of damaged states the game does not ship.
                if (!W3dTexturePathResolver.Resolve(art.Texture, services.Content.Exists).Found)
                {
                    Console.WriteLine($"terrain: bridge '{bridge.TemplateName}' state {state}: texture '{art.Texture}' is not in the content");
                    continue;
                }

                var mesh = BridgeMeshBuilder.Build(bridge.TemplateName, state, art, start, end, lighting);
                if (!materials.TryGetValue(art.Texture, out var material))
                {
                    material = TerrainMaterials.CreateBridge(device, services.Textures.Resolve(new W3dTextureReference(art.Texture)), _fogMap!, linear, services.DepthWrites, _overlay);
                    materials.Add(art.Texture, material);
                }

                var (vertices, indices) = BridgeVertices.Build(mesh, fogScale, lightTable);
                var vertexBuffer = StrideBuffer.Vertex.New(device, vertices, GraphicsResourceUsage.Immutable);
                var indexBuffer = StrideBuffer.Index.New(device, indices, GraphicsResourceUsage.Immutable);
                Track(vertexBuffer, vertices.Length * WaterVertex.SizeInBytes);
                Track(indexBuffer, indices.Length * sizeof(uint));
                var component = CreateModel($"bridge {index} state {state}", material, vertexBuffer, WaterLayout, vertices.Length, indexBuffer, true, indices.Length, new BoundingBox(ToStride(mesh.Bounds.Min), ToStride(mesh.Bounds.Max)));
                component.Enabled = false;
                meshes[state] = mesh;
                components[state] = component;
            }

            if (meshes[0] is null) continue;
            var draw = new BridgeDraw(index, meshes, components);
            draw.Show(bridge.DamageState);
            _bridgeDraws.Add(draw);
        }
    }

    private static System.Numerics.Vector3 ToVector(CnCNew.Simulation.Terrain.DeckPoint point) => new(FixedConvert.ToSingle(point.X), FixedConvert.ToSingle(point.Y), FixedConvert.ToSingle(point.Z));

    private ModelComponent CreateModel(string name, Material material, StrideBuffer vertexBuffer, VertexDeclaration layout, int vertexCount,
        StrideBuffer indexBuffer, bool wideIndices, int indexCount, BoundingBox bounds)
    {
        var component = new ModelComponent(BuildModel(material, vertexBuffer, layout, vertexCount, indexBuffer, wideIndices, indexCount, bounds));
        var entity = new Entity(name) { component };
        _services!.Scene.Entities.Add(entity);
        _entities.Add(entity);
        return component;
    }

    private static Model BuildModel(Material material, StrideBuffer vertexBuffer, VertexDeclaration layout, int vertexCount,
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
        return model;
    }

    private void Track(IDisposable resource, long bytes)
    {
        _resources.Add(resource);
        _gpuBytes += bytes;
    }

    private static Vector3 ToStride(System.Numerics.Vector3 value) => new(value.X, value.Y, value.Z);

    private sealed record WaterDraw(WaterBatch Batch, StrideBuffer VertexBuffer);

    /// <summary>The meshes of one terrain bridge and the state it shows; <c>W3DBridge::load</c> happens once for all states, when the layer is attached.</summary>
    private sealed class BridgeDraw(int index, BridgeMesh?[] meshes, ModelComponent?[] components)
    {
        private int _shown = -1;

        public int Index { get; } = index;

        public void Show(int damageState)
        {
            var next = BridgeMeshBuilder.ShownState(damageState, _shown < 0 ? 0 : _shown, meshes);
            if (next == _shown) return;
            if (_shown >= 0 && components[_shown] is { } hidden) hidden.Enabled = false;
            if (components[next] is { } shown) shown.Enabled = true;
            _shown = next;
        }
    }
}
