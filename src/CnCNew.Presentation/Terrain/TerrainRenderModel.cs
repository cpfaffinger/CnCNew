using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Data.Definitions;
using CnCNew.Data.World;

namespace CnCNew.Presentation.Terrain;

/// <summary>
/// One draw pass of a chunk as plain triangle-list data. Every cell owns four vertices (corners (x,y), (x+1,y), (x+1,y+1), (x,y+1)), because
/// neighbouring cells use different texture coordinates. <see cref="Colors"/> carries the static terrain lighting in RGB and the blend
/// weight in A (255 in the base pass). Triangles are counter-clockwise seen from above (+Z); indices are 16-bit because a chunk has at
/// most 32 x 32 x 4 = 4096 vertices.
/// </summary>
public sealed record TerrainMesh(
    ImmutableArray<Vector3> Positions,
    ImmutableArray<Vector3> Normals,
    ImmutableArray<Rgba32> Colors,
    ImmutableArray<Vector2> Uv,
    ImmutableArray<ushort> Indices)
{
    /// <summary>Position, normal, colour and one UV set: 12 + 12 + 4 + 8 bytes.</summary>
    public const int VertexStride = 36;

    public int VertexCount => Positions.Length;
    public int TriangleCount => Indices.Length / 3;
    public long GpuBytes => (long)VertexCount * VertexStride + Indices.Length * sizeof(ushort);
}

/// <summary>
/// A square block of height cells. Draw order per chunk (or per pass over all chunks): <see cref="Base"/> opaque with depth write;
/// <see cref="Blend"/> then <see cref="ExtraBlend"/> alpha-blended (source alpha = vertex alpha, same atlas, depth test less-equal, no depth
/// write). All three passes sample the same <see cref="TerrainRenderModel.Atlas"/> with their own UV set.
/// </summary>
public sealed record TerrainChunk(int ChunkX, int ChunkY, int FirstCellX, int FirstCellY, int CellsX, int CellsY, Aabb Bounds,
    TerrainMesh Base, TerrainMesh? Blend, TerrainMesh? ExtraBlend)
{
    public long GpuBytes => Base.GpuBytes + (Blend?.GpuBytes ?? 0) + (ExtraBlend?.GpuBytes ?? 0);
}

/// <summary>A texture class that could not be placed in the atlas; its cells sample atlas texel (0, 0) like the reference's zero UVs.</summary>
public sealed record TerrainTextureIssue(int ClassIndex, string ClassName, TerrainTextureStatus Status, string Path, string Reason);

public sealed record TerrainRenderStats(
    int Chunks,
    int Cells,
    int BaseVertices,
    int BaseTriangles,
    int BlendCells,
    int BlendVertices,
    int BlendTriangles,
    int ExtraBlendCells,
    int ExtraBlendVertices,
    int ExtraBlendTriangles,
    int CustomEdgeBlendCells,
    int CliffCells,
    int CliffMappedCells,
    int StretchedCells,
    int FlippedCells,
    int MissingTextureCells,
    int InvalidTileReferences,
    int TextureClasses,
    int MirroredTextures,
    int IdFieldTextures,
    int WaterSurfaces,
    int WaterTriangles,
    long MeshBytes,
    long AtlasBytes,
    long WaterBytes)
{
    public long TotalGpuBytes => MeshBytes + AtlasBytes + WaterBytes;
}

public sealed record TerrainRenderOptions(
    TerrainLighting Lighting,
    WaterAppearance? Water,
    bool AdjustCliffTextures = true,
    bool Use3WayBlends = true,
    int ChunkCells = 32,
    int AtlasWidth = TerrainAtlasBuilder.ReferenceTextureWidth)
{
    /// <summary>Lighting, water and the <c>AdjustCliffTextures</c>/<c>Use3WayTerrainBlends</c> switches of <c>GameData.ini</c>.</summary>
    public static TerrainRenderOptions FromDefinitions(CompiledDefinitionGraph graph)
    {
        var lighting = TerrainLighting.FromDefinitions(graph);
        var gameData = WorldDefinitionReader.GameData(graph);
        return new TerrainRenderOptions(
            lighting,
            WaterAppearance.FromDefinitions(graph, lighting),
            WorldDefinitionReader.Boolean(gameData, "AdjustCliffTextures"),
            WorldDefinitionReader.Integer(gameData, "Use3WayTerrainBlends") != 0);
    }
}

/// <summary>
/// Renderer-neutral terrain of one map: chunked meshes for a base, blend and 3-way blend pass over a per-map texture atlas, water
/// meshes, the per-cell radar colours and counted diagnostics. Built by <see cref="Build"/> from the decoded map, following
/// <c>HeightMapRenderObjClass::updateVB</c>, <c>WorldHeightMap::getUVData/getAlphaUVData/getExtraAlphaUVData</c> and
/// <c>TerrainTextureClass::update</c>. World space: X east, Y north, Z up; world (0, 0) is height vertex (border, border).
/// </summary>
public sealed record TerrainRenderModel(
    string Name,
    int VertexWidth,
    int VertexHeight,
    int Border,
    int ChunkCells,
    int ChunksX,
    int ChunksY,
    TerrainAtlas Atlas,
    ImmutableArray<TerrainAtlasRegion> Regions,
    ImmutableArray<TerrainChunk> Chunks,
    ImmutableArray<WaterSurface> Water,
    ImmutableArray<Rgba32> CellColors,
    ImmutableArray<TerrainTextureIssue> TextureIssues,
    TerrainRenderStats Stats)
{
    public int CellsX => VertexWidth - 1;
    public int CellsY => VertexHeight - 1;

    public TerrainChunk ChunkAt(int chunkX, int chunkY) => Chunks[chunkY * ChunksX + chunkX];

    /// <summary>Average base-tile colour of height cell (x, y) (<c>WorldHeightMap::getTerrainColorAt</c>); height cells include the border.</summary>
    public Rgba32 CellColor(int cellX, int cellY) => CellColors[cellY * CellsX + cellX];

    public static TerrainRenderModel Build(MapData map, TerrainTextureLookup textures, TerrainRenderOptions options, string name = "")
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(textures);
        ArgumentNullException.ThrowIfNull(options);
        if (options.ChunkCells is < 1 or > 32) throw new ArgumentOutOfRangeException(nameof(options), "ChunkCells must be 1..32 so chunk indices fit 16 bits.");
        return new Builder(map, textures, options).Run(name);
    }

    private sealed class Builder
    {

        private const float HeightScaleUv = 0.625f / 10f;
        private const float StretchLimit = 1.5f;
        private const float TileLimit = 4f;
        private const float TallStretchLimit = 2f;
        private const float DiamondStretchLimit = 2.4f;

        private readonly MapData _map;
        private readonly MapHeightField _field;
        private readonly MapBlendTiles _blend;
        private readonly TerrainTextureLookup _textures;
        private readonly TerrainRenderOptions _options;
        private readonly int _width;
        private readonly int _height;
        private readonly List<TerrainTextureIssue> _issues = [];
        private TerrainAtlasRegion?[] _classRegions = [];
        private TerrainAtlas _atlas = null!;
        private int _stretched, _cliffMapped, _missingCells, _invalidTiles, _mirrored, _idField;

        public Builder(MapData map, TerrainTextureLookup textures, TerrainRenderOptions options)
        {
            _map = map;
            _field = map.HeightField;
            _blend = map.BlendTiles;
            _textures = textures;
            _options = options;
            _width = _field.Width;
            _height = _field.Height;
            if (_width < 2 || _height < 2) throw new InvalidDataException("A map needs at least 2x2 height vertices.");
            var cells = _width * _height;
            if (_blend.Tiles.Length != cells || _blend.Blends.Length != cells || _blend.ExtraBlends.Length != cells || _blend.CliffInfoIndices.Length != cells)
                throw new InvalidDataException("Blend tile arrays do not match the height field.");
        }

        public TerrainRenderModel Run(string name)
        {
            BuildAtlas();
            var normals = new Vector3[_width * _height];
            var colors = new Rgba32[_width * _height];
            for (var y = 0; y < _height; y++)
                for (var x = 0; x < _width; x++)
                {
                    var normal = VertexNormal(x, y);
                    normals[y * _width + x] = normal;
                    colors[y * _width + x] = Rgba32.FromUnit(_options.Lighting.Shade(normal), 255);
                }

            var cellsX = _width - 1;
            var cellsY = _height - 1;
            var chunkCells = _options.ChunkCells;
            var chunksX = (cellsX + chunkCells - 1) / chunkCells;
            var chunksY = (cellsY + chunkCells - 1) / chunkCells;
            var chunks = ImmutableArray.CreateBuilder<TerrainChunk>(chunksX * chunksY);
            int blendCells = 0, extraCells = 0, customEdge = 0, cliffCells = 0, flipped = 0;
            Span<float> u = stackalloc float[4];
            Span<float> v = stackalloc float[4];
            Span<float> blendU = stackalloc float[4];
            Span<float> blendV = stackalloc float[4];
            Span<byte> alpha = stackalloc byte[4];
            for (var chunkY = 0; chunkY < chunksY; chunkY++)
                for (var chunkX = 0; chunkX < chunksX; chunkX++)
                {
                    var firstX = chunkX * chunkCells;
                    var firstY = chunkY * chunkCells;
                    var countX = Math.Min(chunkCells, cellsX - firstX);
                    var countY = Math.Min(chunkCells, cellsY - firstY);
                    var basePass = new MeshBuilder();
                    var blendPass = new MeshBuilder();
                    var extraPass = new MeshBuilder();
                    for (var y = firstY; y < firstY + countY; y++)
                        for (var x = firstX; x < firstX + countX; x++)
                        {
                            var ndx = y * _width + x;
                            if (_blend.IsCliff(x, y)) cliffCells++;
                            var baseStretched = UvForTile(ndx, _blend.Tiles[ndx], true, u, v);
                            var flip = AlphaUvData(ndx, baseStretched, blendU, blendV, alpha, out var blendKind);
                            if (flip) flipped++;
                            if (blendKind == BlendKind.CustomEdge) customEdge++;
                            basePass.AddCell(this, x, y, u, v, [255, 255, 255, 255], flip, normals, colors);
                            if (blendKind == BlendKind.Alpha)
                            {
                                blendCells++;
                                blendPass.AddCell(this, x, y, blendU, blendV, alpha, flip, normals, colors);
                            }
                            if (_options.Use3WayBlends && ExtraAlphaUvData(ndx, x, y, blendU, blendV, alpha, out var extraFlip))
                            {
                                extraCells++;
                                extraPass.AddCell(this, x, y, blendU, blendV, alpha, extraFlip, normals, colors);
                            }
                        }
                    var baseMesh = basePass.ToMesh();
                    chunks.Add(new TerrainChunk(chunkX, chunkY, firstX, firstY, countX, countY, Aabb.FromPoints(baseMesh.Positions.AsSpan()),
                        baseMesh, blendPass.IsEmpty ? null : blendPass.ToMesh(), extraPass.IsEmpty ? null : extraPass.ToMesh()));
                }

            var water = _options.Water is { } appearance ? WaterSurfaceBuilder.Build(_map, appearance) : [];
            var chunkList = chunks.MoveToImmutable();
            var stats = new TerrainRenderStats(
                chunkList.Length,
                cellsX * cellsY,
                chunkList.Sum(chunk => chunk.Base.VertexCount),
                chunkList.Sum(chunk => chunk.Base.TriangleCount),
                blendCells,
                chunkList.Sum(chunk => chunk.Blend?.VertexCount ?? 0),
                chunkList.Sum(chunk => chunk.Blend?.TriangleCount ?? 0),
                extraCells,
                chunkList.Sum(chunk => chunk.ExtraBlend?.VertexCount ?? 0),
                chunkList.Sum(chunk => chunk.ExtraBlend?.TriangleCount ?? 0),
                customEdge,
                cliffCells,
                _cliffMapped,
                _stretched,
                flipped,
                _missingCells,
                _invalidTiles,
                _blend.TextureClasses.Length,
                _mirrored,
                _idField,
                water.Length,
                water.Sum(surface => surface.Indices.Length / 3),
                chunkList.Sum(chunk => chunk.GpuBytes),
                _atlas.BytesWithMips,
                water.Sum(surface => (long)surface.Positions.Length * (12 + 8 + 4) + surface.Indices.Length * 4L));

            return new TerrainRenderModel(name, _width, _height, _field.Border, chunkCells, chunksX, chunksY, _atlas,
                [.._classRegions.Where(region => region is not null).Select(region => region!)],
                chunkList, water, CellColorsFromAtlas(), [.._issues], stats);
        }

        // ------------------------------------------------------------------ atlas

        private void BuildAtlas()
        {
            var classes = _blend.TextureClasses;
            var blocks = new List<TerrainAtlasBuilder.Block>();
            for (var index = 0; index < classes.Length; index++)
            {
                var textureClass = classes[index];
                var result = _textures.Resolve(textureClass.Name);
                if (result.Status != TerrainTextureStatus.Resolved)
                {
                    _issues.Add(new TerrainTextureIssue(index, textureClass.Name, result.Status, result.VirtualPath, result.Detail));
                    continue;
                }
                var image = result.Image!;
                // readTexClass: the class width is the largest square that fits numTiles; the image must supply at least numTiles tiles.
                var width = 0;
                for (var candidate = 10; candidate >= 1; candidate--)
                    if (textureClass.TileCount >= candidate * candidate) { width = candidate; break; }
                if (width == 0 || image.SquareTiles < textureClass.TileCount)
                {
                    _issues.Add(new TerrainTextureIssue(index, textureClass.Name, TerrainTextureStatus.UndecodableFile, image.VirtualPath,
                        $"{image.Width}x{image.Height} image has {image.SquareTiles} tiles, class needs {textureClass.TileCount}"));
                    continue;
                }
                if (width != textureClass.Width)
                {
                    _issues.Add(new TerrainTextureIssue(index, textureClass.Name, TerrainTextureStatus.UndecodableFile, image.VirtualPath,
                        $"class width {textureClass.Width} does not match the {width}x{width} tiles readTexClass loads"));
                    continue;
                }
                if (image.FlippedVertically || image.FlippedHorizontally) _mirrored++;
                if (image.IdFieldBytes > 0) _idField++;
                blocks.Add(new TerrainAtlasBuilder.Block(index, textureClass.Name, width, ExtractBlock(image, width)));
            }
            var (atlas, regions) = TerrainAtlasBuilder.Build(blocks, _options.AtlasWidth);
            _atlas = atlas;
            _classRegions = new TerrainAtlasRegion?[classes.Length];
            foreach (var region in regions) _classRegions[region.ClassIndex] = region;
        }

        /// <summary>The first <paramref name="width"/>*64 stored rows and columns (<c>readTiles</c>), i.e. the bottom-left square of the image.</summary>
        private static byte[] ExtractBlock(TerrainTextureImage image, int width)
        {
            var size = width * TerrainAtlasBuilder.TilePixels;
            var block = new byte[size * size * 4];
            var firstRow = image.Height - size;
            for (var row = 0; row < size; row++)
                Buffer.BlockCopy(image.Rgba, ((firstRow + row) * image.Width) * 4, block, row * size * 4, size * 4);
            return block;
        }


        private int ClassOfTile(int sourceTile)
        {
            var classes = _blend.TextureClasses;
            for (var index = 0; index < classes.Length; index++)
            {
                var textureClass = classes[index];
                if (textureClass.FirstTile < 0) continue;
                if (sourceTile >= textureClass.FirstTile && sourceTile < textureClass.FirstTile + textureClass.TileCount) return index;
            }
            return -1;
        }

        /// <summary>Pixel rectangle of a 64x64 source tile: class block origin plus (i, width-1-j) tiles, j counting upwards (<c>updateTileTexturePositions</c>).</summary>
        private bool TryTileRect(int sourceTile, out int x, out int y)
        {
            x = y = 0;
            var classIndex = ClassOfTile(sourceTile);
            if (classIndex < 0 || _classRegions[classIndex] is not { } region) return false;
            var local = sourceTile - _blend.TextureClasses[classIndex].FirstTile;
            var i = local % region.TileWidth;
            var j = local / region.TileWidth;
            x = region.X + i * TerrainAtlasBuilder.TilePixels;
            y = region.Y + (region.TileWidth - 1 - j) * TerrainAtlasBuilder.TilePixels;
            return true;
        }

        // ------------------------------------------------------------------ texture coordinates (atlas pixels until the mesh normalizes them)

        /// <summary><c>getUVForNdx</c>: the 32x32 quadrant of the source tile; bit 1 selects the upper half, bit 0 the right half.</summary>
        private bool QuadrantUv(int tileValue, bool counted, Span<float> u, Span<float> v, out float minU, out float minV, out float maxU, out float maxV)
        {
            var sourceTile = tileValue >> 2;
            if (tileValue < 0 || !TryTileRect(sourceTile, out var x, out var y))
            {
                if (counted)
                {
                    if (tileValue < 0 || ClassOfTile(sourceTile) < 0) _invalidTiles++;
                    _missingCells++;
                }
                // getUVForNdx returns U = V = 0 for a tile without texture, so the cell shows the atlas texel (0, 0): the wrapped border pixel
                // of the first placed class, in the reference and here. Its centre is used so bilinear filtering does not wrap.
                SetRect(u, v, minU = 0.5f, minV = 0.5f, maxU = 0.5f, maxV = 0.5f);
                return false;
            }
            minU = x;
            minV = y;
            maxU = x + TerrainAtlasBuilder.TilePixels;
            maxV = y + TerrainAtlasBuilder.TilePixels;
            var midX = x + TerrainAtlasBuilder.TilePixels / 2f;
            var midY = y + TerrainAtlasBuilder.TilePixels / 2f;
            if ((tileValue & 2) != 0) maxV = midY; else minV = midY;
            if ((tileValue & 1) != 0) minU = midX; else maxU = midX;
            SetRect(u, v, minU, minV, maxU, maxV);
            return true;
        }

        private static void SetRect(Span<float> u, Span<float> v, float minU, float minV, float maxU, float maxV)
        {
            u[0] = minU; u[1] = maxU; u[2] = maxU; u[3] = minU;
            v[0] = maxV; v[1] = maxV; v[2] = minV; v[3] = minV;
        }

        /// <summary><c>getUVForTileIndex</c>: quadrant UVs, replaced by the stored cliff mapping or stretched for steep cells. True = "stretched for cliff".</summary>
        private bool UvForTile(int ndx, int tileValue, bool counted, Span<float> u, Span<float> v)
        {
            if (!QuadrantUv(tileValue, counted, u, v, out var nU, out var nV, out var xU, out var xV)) return false;
            if (!_options.AdjustCliffTextures) return false;
            var cliffIndex = _blend.CliffInfoIndices[ndx];
            if (cliffIndex != 0)
            {
                if (cliffIndex < 0 || cliffIndex > _blend.CliffInfos.Length) throw new InvalidDataException($"Cell {ndx} references cliff info {cliffIndex} of {_blend.CliffInfos.Length}.");
                var info = _blend.CliffInfos[cliffIndex - 1];
                var classIndex = ClassOfTile(tileValue >> 2);
                if (classIndex >= 0 && classIndex == ClassOfTile(info.TileIndex >> 2) && _classRegions[classIndex] is { } region)
                {
                    const float scale = TerrainAtlasBuilder.ReferenceTextureWidth;
                    var bottom = region.Y + region.Pixels;
                    for (var corner = 0; corner < 4; corner++)
                    {
                        u[corner] = region.X + (float)info.Uv[corner * 2] * scale;
                        v[corner] = bottom + (float)info.Uv[corner * 2 + 1] * scale;
                    }
                    if (counted) _cliffMapped++;
                    return info.Flip;
                }
            }
            if (ndx >= _width * _height - _width - 1) return true;
            int h0 = _field.Heights[ndx], h1 = _field.Heights[ndx + 1], h2 = _field.Heights[ndx + _width + 1], h3 = _field.Heights[ndx + _width];
            var minH = Math.Min(Math.Min(h0, h1), Math.Min(h2, h3));
            var maxH = Math.Max(Math.Max(h0, h1), Math.Max(h2, h3));
            var deltaH = maxH - minH;
            var belowLimit = minH + (2 * deltaH + 1) / 3;
            var aboveLimit = minH + (deltaH + 1) / 3;
            var below = (h0 < belowLimit ? 1 : 0) + (h1 < belowLimit ? 1 : 0) + (h2 < belowLimit ? 1 : 0) + (h3 < belowLimit ? 1 : 0);
            var above = (h0 > aboveLimit ? 1 : 0) + (h1 > aboveLimit ? 1 : 0) + (h2 > aboveLimit ? 1 : 0) + (h3 > aboveLimit ? 1 : 0);
            var stretch = deltaH * HeightScaleUv;
            if (stretch < StretchLimit) return false;
            var texClass = ClassOfTile(tileValue >> 2);
            if (texClass < 0 || _classRegions[texClass] is not { } block) return false;
            float nUb = block.X, nVb = block.Y, xUb = block.X + block.Pixels, xVb = block.Y + block.Pixels;
            var divisor = Math.Clamp(TileLimit / stretch, 1f, TileLimit);
            var deltaV = xVb - nVb;
            if (above != 1 && below != 1 && (above != 2 || below != 2) && stretch < DiamondStretchLimit) return false;
            if (below == 1 || above > below)
            {
                if (h0 == minH) v[0] = nV + deltaV / divisor;
                else if (h1 == minH) v[1] = nV + deltaV / divisor;
                else if (h2 == minH) v[2] = xV - deltaV / divisor;
                else if (h3 == minH) v[3] = xV - deltaV / divisor;
            }
            else if (above == 1 || below > above)
            {
                if (h0 == maxH) v[0] = nV + deltaV / divisor;
                else if (h1 == maxH) v[1] = nV + deltaV / divisor;
                else if (h2 == maxH) v[2] = xV - deltaV / divisor;
                else if (h3 == maxH) v[3] = xV - deltaV / divisor;
            }
            else
            {
                if (stretch < TallStretchLimit) return false;
                var dx = Stretch((h3 - h2) * HeightScaleUv) * (xU - nU);
                var dy = Stretch((h3 - h0) * HeightScaleUv) * (xV - nV);
                u[0] = nU; u[1] = nU + dx; u[2] = nU + dx; u[3] = nU;
                v[0] = nV + dy; v[1] = nV + dy; v[2] = nV; v[3] = nV;
                dx = Stretch((h1 - h0) * HeightScaleUv) * (xU - nU);
                dy = Stretch((h2 - h1) * HeightScaleUv) * (xV - nV);
                u[1] = u[0] + dx;
                v[1] = v[3] + dy;
            }
            var adjustV = 0f;
            for (var i = 0; i < 4; i++) adjustV = Math.Max(adjustV, nVb - v[i]);
            for (var i = 0; i < 4; i++) v[i] += adjustV;
            var adjustU = 0f;
            adjustV = 0f;
            for (var i = 0; i < 4; i++)
            {
                adjustU = Math.Max(adjustU, u[i] - xUb);
                adjustV = Math.Max(adjustV, v[i] - xVb);
            }
            for (var i = 0; i < 4; i++)
            {
                u[i] -= adjustU;
                v[i] -= adjustV;
            }
            if (counted) _stretched++;
            return true;
        }

        /// <summary>Edge length of a sloped cell side in cells, 1 below the stretch limit, at most the tile limit.</summary>
        private static float Stretch(float slope)
        {
            var length = MathF.Sqrt(1 + slope * slope);
            if (length < StretchLimit) length = 1f;
            return Math.Min(length, TileLimit);
        }

        private enum BlendKind { None, Alpha, CustomEdge }

        /// <summary><c>getAlphaUVData</c>: the blend layer's UVs and corner alphas; returns the triangle flip of the cell.</summary>
        private bool AlphaUvData(int ndx, bool baseStretched, Span<float> u, Span<float> v, Span<byte> alpha, out BlendKind kind)
        {
            alpha.Clear();
            var blendIndex = _blend.Blends[ndx];
            bool stretched;
            var flip = false;
            kind = BlendKind.None;
            if (blendIndex == 0)
            {
                stretched = baseStretched;
            }
            else
            {
                var tile = BlendedTile(blendIndex);
                stretched = UvForTile(ndx, tile.BlendIndex, false, u, v);
                flip = AlphaPattern(tile, alpha, _options.Use3WayBlends);
                kind = tile.CustomEdgeClass >= 0 ? BlendKind.CustomEdge : BlendKind.Alpha;
                if (kind == BlendKind.CustomEdge)
                {
                    alpha.Clear();
                    flip = false;
                }
                else if (alpha[0] == 0 && alpha[1] == 0 && alpha[2] == 0 && alpha[3] == 0)
                {
                    kind = BlendKind.None;
                }
            }
            if (stretched)
            {
                var x = ndx % _width;
                var y = ndx / _width;
                int p0 = RawHeight(x, y), p1 = RawHeight(x + 1, y), p2 = RawHeight(x + 1, y + 1), p3 = RawHeight(x, y + 1);
                flip = Math.Abs(p0 - p2) > Math.Abs(p1 - p3);
            }
            return flip;
        }

        /// <summary><c>getExtraAlphaUVData</c> plus the cliff flip of <c>renderExtraBlendTiles</c>.</summary>
        private bool ExtraAlphaUvData(int ndx, int x, int y, Span<float> u, Span<float> v, Span<byte> alpha, out bool flip)
        {
            flip = false;
            var extraIndex = _blend.ExtraBlends[ndx];
            if (extraIndex == 0) return false;
            alpha.Clear();
            var tile = BlendedTile(extraIndex);
            var cliff = UvForTile(ndx, tile.BlendIndex, false, u, v);
            flip = AlphaPattern(tile, alpha, true);
            if (tile.CustomEdgeClass >= 0)
            {
                alpha.Clear();
                flip = false;
            }
            int p0 = RawHeight(x, y), p1 = RawHeight(x + 1, y), p2 = RawHeight(x + 1, y + 1), p3 = RawHeight(x, y + 1);
            if (cliff && Math.Abs(p0 - p2) > Math.Abs(p1 - p3)) flip = true;
            return alpha[0] != 0 || alpha[1] != 0 || alpha[2] != 0 || alpha[3] != 0;
        }

        private MapBlendedTile BlendedTile(int blendIndex)
        {
            if (blendIndex < 0 || blendIndex > _blend.BlendedTiles.Length)
                throw new InvalidDataException($"Blend index {blendIndex} is outside the {_blend.BlendedTiles.Length} blended tiles.");
            return _blend.BlendedTiles[blendIndex - 1];
        }

        /// <summary>The corner alpha pattern of a blended tile and whether its triangles must be flipped (the inverted byte: bit 0 inverted, bit 1 forced flip).</summary>
        internal static bool AlphaPattern(MapBlendedTile tile, Span<byte> alpha, bool use3WayBlends)
        {
            var inverted = (tile.Inverted & 1) != 0;
            var forcedFlip = use3WayBlends && (tile.Inverted & 2) != 0;
            var flip = false;
            if (tile.Horizontal)
            {
                flip = forcedFlip;
                if (inverted) alpha[0] = alpha[3] = 255; else alpha[1] = alpha[2] = 255;
            }
            if (tile.Vertical)
            {
                flip = forcedFlip;
                if (inverted) alpha[0] = alpha[1] = 255; else alpha[2] = alpha[3] = 255;
            }
            if (tile.RightDiagonal)
            {
                if (inverted)
                {
                    alpha[1] = 255;
                    if (tile.LongDiagonal) alpha[0] = alpha[2] = 255;
                }
                else
                {
                    flip = true;
                    alpha[2] = 255;
                    if (tile.LongDiagonal) alpha[1] = alpha[3] = 255;
                }
            }
            if (tile.LeftDiagonal)
            {
                if (inverted)
                {
                    flip = true;
                    alpha[0] = 255;
                    if (tile.LongDiagonal) alpha[1] = alpha[3] = 255;
                }
                else
                {
                    alpha[3] = 255;
                    if (tile.LongDiagonal) alpha[0] = alpha[2] = 255;
                }
            }
            return flip;
        }

        // ------------------------------------------------------------------ geometry and lighting

        private int RawHeight(int x, int y) => _field.Heights[Math.Min(y, _height - 1) * _width + Math.Min(x, _width - 1)];

        /// <summary><c>updateVB</c>: central differences over two cells (clamped at the field edge) with a fixed 2-cell run.</summary>
        private Vector3 VertexNormal(int x, int y)
        {
            var left = Math.Max(x - 1, 0);
            var right = Math.Min(x + 1, _width - 1);
            var down = Math.Max(y - 1, 0);
            var up = Math.Min(y + 1, _height - 1);
            const float run = 2 * TerrainHeightSampler.CellSize;
            var dx = (_field.HeightAt(right, y) - _field.HeightAt(left, y)) * TerrainHeightSampler.HeightScale;
            var dy = (_field.HeightAt(x, up) - _field.HeightAt(x, down)) * TerrainHeightSampler.HeightScale;
            return Vector3.Normalize(Vector3.Cross(new Vector3(run, 0, dx), new Vector3(0, run, dy)));
        }

        public Vector3 Position(int x, int y)
            => new((x - _field.Border) * TerrainHeightSampler.CellSize, (y - _field.Border) * TerrainHeightSampler.CellSize,
                _field.HeightAt(x, y) * TerrainHeightSampler.HeightScale);

        public Vector2 NormalizeUv(float u, float v) => new(u / _atlas.Width, v / _atlas.Height);

        public int VertexIndex(int x, int y) => y * _width + x;

        private ImmutableArray<Rgba32> CellColorsFromAtlas()
        {
            var cellsX = _width - 1;
            var cellsY = _height - 1;
            var averages = new Dictionary<int, Rgba32>();
            var result = new Rgba32[cellsX * cellsY];
            for (var y = 0; y < cellsY; y++)
                for (var x = 0; x < cellsX; x++)
                {
                    var tileValue = _blend.Tiles[y * _width + x];
                    var sourceTile = tileValue >> 2;
                    if (!averages.TryGetValue(sourceTile, out var color))
                    {
                        color = tileValue >= 0 && TryTileRect(sourceTile, out var tx, out var ty) ? Average(tx, ty) : new Rgba32(0, 0, 0, 255);
                        averages[sourceTile] = color;
                    }
                    result[y * cellsX + x] = color;
                }
            return [..result];
        }

        /// <summary>The 64x64 tile mipped to one pixel (<c>getRGBDataForWidth(1)</c>): the plain average.</summary>
        private Rgba32 Average(int x, int y)
        {
            long r = 0, g = 0, b = 0;
            var pixels = _atlas.Rgba.AsSpan();
            for (var row = 0; row < TerrainAtlasBuilder.TilePixels; row++)
                for (var column = 0; column < TerrainAtlasBuilder.TilePixels; column++)
                {
                    var offset = ((y + row) * _atlas.Width + x + column) * 4;
                    r += pixels[offset];
                    g += pixels[offset + 1];
                    b += pixels[offset + 2];
                }
            const int count = TerrainAtlasBuilder.TilePixels * TerrainAtlasBuilder.TilePixels;
            return new Rgba32((byte)((r + count / 2) / count), (byte)((g + count / 2) / count), (byte)((b + count / 2) / count), 255);
        }
    }

    private sealed class MeshBuilder
    {
        private readonly List<Vector3> _positions = [];
        private readonly List<Vector3> _normals = [];
        private readonly List<Rgba32> _colors = [];
        private readonly List<Vector2> _uv = [];
        private readonly List<ushort> _indices = [];

        public bool IsEmpty => _positions.Count == 0;

        public void AddCell(Builder owner, int x, int y, ReadOnlySpan<float> u, ReadOnlySpan<float> v, ReadOnlySpan<byte> alpha, bool flip,
            Vector3[] normals, Rgba32[] colors)
        {
            var first = _positions.Count;
            for (var corner = 0; corner < 4; corner++)
            {
                var vx = x + (corner is 1 or 2 ? 1 : 0);
                var vy = y + (corner is 2 or 3 ? 1 : 0);
                var index = owner.VertexIndex(vx, vy);
                _positions.Add(owner.Position(vx, vy));
                _normals.Add(normals[index]);
                _colors.Add(colors[index] with { A = alpha[corner] });
                _uv.Add(owner.NormalizeUv(u[corner], v[corner]));
            }
            // Unflipped cells split along corner 0-2 (the simulation's diagonal), flipped cells along corner 1-3.
            ReadOnlySpan<int> order = flip ? [0, 1, 3, 1, 2, 3] : [0, 1, 2, 0, 2, 3];
            foreach (var corner in order) _indices.Add(checked((ushort)(first + corner)));
        }

        public TerrainMesh ToMesh() => new([.._positions], [.._normals], [.._colors], [.._uv], [.._indices]);
    }
}
