using System.Numerics;
using CnCNew.Presentation.Camera;
using CnCNew.Presentation.Fx;
using CnCNew.Presentation.Terrain;

namespace CnCNew.Client.Stride.Layers.Fx;

/// <summary>Camera data the builder needs: eye, view axes (world space), clip planes and the world size of one pixel at unit depth.</summary>
internal readonly record struct FxView(Vector3 Eye, Vector3 Right, Vector3 Up, Vector3 Forward, float Near, float Far, float WorldPerPixelAtUnitDepth)
{
    public static FxView From(RtsCamera camera)
    {
        var view = camera.View;
        var projection = camera.Projection;
        var viewportHeight = Math.Max(1f, camera.ViewportSize.Y);
        // Row-vector convention: the columns of the view rotation are the camera axes in world space; the camera looks down -Z.
        return new FxView(
            camera.Eye,
            new Vector3(view.M11, view.M21, view.M31),
            new Vector3(view.M12, view.M22, view.M32),
            new Vector3(-view.M13, -view.M23, -view.M33),
            camera.Settings.NearPlane,
            camera.FarPlane,
            2f / (MathF.Abs(projection.M22) * viewportHeight));
    }
}

/// <summary>What one frame of building produced (per-frame numbers; <see cref="FxLayer"/> accumulates them).</summary>
internal readonly record struct FxBuildCounts(
    int ParticlesDrawn, int Streaks, int Tracers, int Decals, int Batches, int Runs, int Quads, int DroppedQuads,
    int SkippedDrawable, int SkippedSmudge, int SkippedUnusable, int SkippedShortStreaks, int DeferredItems);

/// <summary>
/// Turns an <see cref="FxFrame"/> into quads of the shared <see cref="FxGeometry"/> and a short list of draw runs (quad range + material).
/// Order follows the W3D particle pass: ground decals first, then ground-aligned sprites and alpha-tested sprites (unsorted, grouped by
/// batch), then every blended camera-facing sprite, streak and tracer in one global back-to-front order; consecutive items with the same
/// material share a run, so the number of draw calls follows the texture changes along that order and not the particle count.
/// </summary>
internal sealed class FxBuilder(FxGeometry geometry, TerrainHeightSampler heights, bool linearColorSpace)
{
    /// <summary>Upper bound of runs from the sorted list; the rest of that list is grouped per material (still depth ordered inside).</summary>
    public const int MaxSortedRuns = 512;

    private const float GroundLift = 0.3f;
    private const float MinTracerPixels = 1.5f;
    private const float RunDepthStep = 0.01f;

    private static readonly FxMaterialKey TracerKey = new(null, FxBlend.Alpha);

    private enum BatchClass : byte { Skip, Ground, AlphaTest, Billboard, Streak }

    private enum ItemKind : byte { Particle, Streak, Tracer, Stream }

    private struct Item
    {
        public ItemKind Kind;
        public int Material;
        public int A;
        public int B;
    }

    private struct Run
    {
        public int Material;
        public int First;
        public int Count;
        public float DepthSum;
        public int Items;
        public byte Group; // 0 decals and ground, 1 alpha test (opaque), 2 sorted blended
    }

    private readonly byte[] _linearLut = BuildLut();
    private readonly List<FxMaterialKey> _materials = [];
    private Run[] _runs = new Run[256];
    private int _runCount;
    private BatchClass[] _batchClass = new BatchClass[64];
    private int[] _batchMaterial = new int[64];
    private int[] _batchOffsets = new int[65];
    private int[] _groupOrder = new int[1024];
    private Item[] _items = new Item[1024];
    private float[] _sortKeys = new float[1024];
    private int[] _sortOrder = new int[1024];
    private bool[] _deferred = new bool[1024];
    private FxView _view;
    private int _particlesDrawn, _streaks, _tracers, _decals, _skippedDrawable, _skippedSmudge, _skippedUnusable, _skippedShort, _deferredItems;

    public int RunCount => _runCount;

    public FxBuildCounts Counts { get; private set; }

    public FxMaterialKey MaterialOf(int run) => _materials[_runs[run].Material];

    public int FirstQuadOf(int run) => _runs[run].First;

    public int QuadCountOf(int run) => _runs[run].Count;

    public void Build(FxFrame frame, in FxView view, in Frustum frustum)
    {
        geometry.Reset();
        _runCount = 0;
        _materials.Clear();
        _view = view;
        _particlesDrawn = _streaks = _tracers = _decals = _skippedDrawable = _skippedSmudge = _skippedUnusable = _skippedShort = _deferredItems = 0;

        var batches = frame.Particles.Batches;
        PrepareBatches(batches);
        EmitDecals(frame.Decals, frustum, frame.Frame);
        EmitUnsortedParticles(frame.Particles.Particles, batches.Length);
        EmitSorted(frame);
        Counts = new FxBuildCounts(_particlesDrawn, _streaks, _tracers, _decals, batches.Length, _runCount, geometry.QuadCount, geometry.DroppedQuads,
            _skippedDrawable, _skippedSmudge, _skippedUnusable, _skippedShort, _deferredItems);
    }

    /// <summary>The position of a run on the view axis: the transparent stage sorts render objects by the distance of this point.</summary>
    public Vector3 RunCenter(int run, int ordinal, ref float lastDepth)
    {
        ref readonly var value = ref _runs[run];
        var average = value.DepthSum / Math.Max(1, value.Items);
        var minimum = _view.Near * 1.5f;
        var maximum = _view.Far * 0.98f;
        float depth;
        if (value.Group == 1)
        {
            depth = Math.Clamp(average, minimum, maximum);
        }
        else if (value.Group == 0)
        {
            depth = maximum - ordinal * RunDepthStep;
            lastDepth = depth;
        }
        else
        {
            depth = Math.Clamp(Math.Min(average, lastDepth - RunDepthStep), minimum, maximum);
            lastDepth = depth;
        }

        return _view.Eye + _view.Forward * depth;
    }

    // ------------------------------------------------------------------------------------------------ batches

    private void PrepareBatches(System.Collections.Immutable.ImmutableArray<ParticleBatchKey> batches)
    {
        if (_batchClass.Length < batches.Length)
        {
            var size = Math.Max(batches.Length, _batchClass.Length * 2);
            _batchClass = new BatchClass[size];
            _batchMaterial = new int[size];
            _batchOffsets = new int[size + 1];
        }

        for (var index = 0; index < batches.Length; index++)
        {
            var key = batches[index];
            var kind = BatchClass.Skip;
            switch (key.Kind)
            {
                case ParticleKind.Drawable:
                    _skippedDrawable++;
                    break;
                case ParticleKind.Smudge:
                    _skippedSmudge++;
                    break;
                case ParticleKind.None:
                    _skippedUnusable++;
                    break;
                default:
                    if (string.IsNullOrEmpty(key.Texture) || key.Shader == ParticleShader.None)
                    {
                        _skippedUnusable++;
                    }
                    else if (key.Kind == ParticleKind.Streak)
                    {
                        kind = BatchClass.Streak;
                    }
                    else if (key.GroundAligned)
                    {
                        kind = BatchClass.Ground;
                    }
                    else
                    {
                        kind = key.Shader == ParticleShader.AlphaTest ? BatchClass.AlphaTest : BatchClass.Billboard;
                    }

                    break;
            }

            _batchClass[index] = kind;
            _batchMaterial[index] = kind == BatchClass.Skip ? -1 : MaterialId(new FxMaterialKey(key.Texture, ToBlend(key.Shader)));
        }
    }

    private static FxBlend ToBlend(ParticleShader shader) => shader switch
    {
        ParticleShader.Additive => FxBlend.Additive,
        ParticleShader.Alpha => FxBlend.Alpha,
        ParticleShader.AlphaTest => FxBlend.AlphaTest,
        ParticleShader.Multiply => FxBlend.Multiply,
        _ => throw new InvalidOperationException($"Particle shader {shader} has no effect material."),
    };

    private int MaterialId(FxMaterialKey key)
    {
        for (var index = 0; index < _materials.Count; index++)
        {
            if (_materials[index] == key)
            {
                return index;
            }
        }

        _materials.Add(key);
        return _materials.Count - 1;
    }

    // ------------------------------------------------------------------------------------------------ decals

    private void EmitDecals(System.Collections.Immutable.ImmutableArray<DecalInstance> decals, in Frustum frustum, long frameNumber)
    {
        foreach (var decal in decals)
        {
            var reach = decal.Radius * 1.5f;
            var center = decal.Position;
            if (!frustum.Intersects(new Aabb(center - new Vector3(reach, reach, reach + 50f), center + new Vector3(reach, reach, reach + 50f))))
            {
                continue;
            }

            var fade = 1f;
            if (decal.ExpireFrame is { } expire && decal.FadeFrames > 0)
            {
                fade = Math.Clamp((expire - frameNumber) / (float)decal.FadeFrames, 0f, 1f);
            }

            var color = Pack(new Vector3(decal.Color.X, decal.Color.Y, decal.Color.Z), decal.Color.W * fade);
            var material = MaterialId(new FxMaterialKey(decal.Texture, FxBlend.Alpha));
            var first = geometry.QuadCount;
            EmitDraped(decal, color);
            EndItem(material, first, _view.Far, 0);
            _decals++;
        }
    }

    private void EmitDraped(in DecalInstance decal, uint color)
    {
        var cells = Math.Clamp((int)MathF.Ceiling(2f * decal.Radius / TerrainHeightSampler.CellSize), 1, 8);
        var cos = MathF.Cos(decal.Rotation);
        var sin = MathF.Sin(decal.Rotation);
        var uv = decal.UvRect;
        for (var row = 0; row < cells; row++)
        {
            for (var column = 0; column < cells; column++)
            {
                var quad = geometry.NextQuad();
                if (quad.IsEmpty)
                {
                    return;
                }

                SetDrapedCorner(ref quad[0], decal, cos, sin, cells, column, row, uv, color);
                SetDrapedCorner(ref quad[1], decal, cos, sin, cells, column, row + 1, uv, color);
                SetDrapedCorner(ref quad[2], decal, cos, sin, cells, column + 1, row + 1, uv, color);
                SetDrapedCorner(ref quad[3], decal, cos, sin, cells, column + 1, row, uv, color);
            }
        }
    }

    private void SetDrapedCorner(ref FxVertex vertex, in DecalInstance decal, float cos, float sin, int cells, int column, int row, Vector4 uv, uint color)
    {
        var u = column / (float)cells;
        var v = row / (float)cells;
        var x = (u * 2f - 1f) * decal.Radius;
        var y = (1f - v * 2f) * decal.Radius;
        var worldX = decal.Position.X + x * cos - y * sin;
        var worldY = decal.Position.Y + x * sin + y * cos;
        vertex.Position = new Vector3(worldX, worldY, heights.HeightAt(worldX, worldY) + GroundLift);
        vertex.Color = color;
        vertex.Uv = new Vector2(uv.X + (uv.Z - uv.X) * u, uv.Y + (uv.W - uv.Y) * v);
    }

    // ------------------------------------------------------------------------------------------------ unsorted particles

    private void EmitUnsortedParticles(System.Collections.Immutable.ImmutableArray<ParticleInstance> particles, int batchCount)
    {
        // Counting sort by batch: keeps instance order inside a batch and needs no allocations.
        Array.Clear(_batchOffsets, 0, batchCount + 1);
        var selected = 0;
        for (var index = 0; index < particles.Length; index++)
        {
            var kind = _batchClass[particles[index].Batch];
            if (kind is BatchClass.Ground or BatchClass.AlphaTest)
            {
                _batchOffsets[particles[index].Batch + 1]++;
                selected++;
            }
        }

        if (selected == 0)
        {
            return;
        }

        if (_groupOrder.Length < selected)
        {
            _groupOrder = new int[Math.Max(selected, _groupOrder.Length * 2)];
        }

        for (var batch = 0; batch < batchCount; batch++)
        {
            _batchOffsets[batch + 1] += _batchOffsets[batch];
        }

        for (var index = 0; index < particles.Length; index++)
        {
            var batch = particles[index].Batch;
            if (_batchClass[batch] is BatchClass.Ground or BatchClass.AlphaTest)
            {
                _groupOrder[_batchOffsets[batch]++] = index;
            }
        }

        // After the scatter, _batchOffsets[b] is the end of batch b; its start is the end of batch b-1.
        var start = 0;
        for (var batch = 0; batch < batchCount; batch++)
        {
            var end = _batchOffsets[batch];
            if (end > start)
            {
                var ground = _batchClass[batch] == BatchClass.Ground;
                for (var at = start; at < end; at++)
                {
                    var particle = particles[_groupOrder[at]];
                    var first = geometry.QuadCount;
                    if (ground)
                    {
                        EmitGroundSprite(particle);
                    }
                    else
                    {
                        EmitBillboard(particle);
                    }

                    EndItem(_batchMaterial[batch], first, Depth(particle.Position), ground ? (byte)0 : (byte)1);
                }
            }

            start = end;
        }
    }

    // ------------------------------------------------------------------------------------------------ sorted list

    private void EmitSorted(FxFrame frame)
    {
        var particles = frame.Particles.Particles;
        var count = 0;
        for (var index = 0; index < particles.Length; index++)
        {
            var particle = particles[index];
            var kind = _batchClass[particle.Batch];
            if (kind == BatchClass.Billboard)
            {
                Add(ref count, new Item { Kind = ItemKind.Particle, Material = _batchMaterial[particle.Batch], A = index }, Depth(particle.Position));
            }
            else if (kind == BatchClass.Streak)
            {
                var end = index + 1;
                while (end < particles.Length && particles[end].SystemId == particle.SystemId && _batchClass[particles[end].Batch] == BatchClass.Streak)
                {
                    end++;
                }

                if (end - index >= 2)
                {
                    var middle = particles[(index + end) / 2].Position;
                    Add(ref count, new Item { Kind = ItemKind.Streak, Material = _batchMaterial[particle.Batch], A = index, B = end - index }, Depth(middle));
                }
                else
                {
                    _skippedShort++;
                }

                index = end - 1;
            }
        }

        var tracerMaterial = MaterialId(TracerKey);
        for (var index = 0; index < frame.Tracers.Length; index++)
        {
            var tracer = frame.Tracers[index];
            if (tracer.Opacity <= 0f)
            {
                continue;
            }

            var middle = tracer.Head + tracer.Direction * (tracer.Length * 0.5f);
            Add(ref count, new Item { Kind = ItemKind.Tracer, Material = tracerMaterial, A = index }, Depth(middle));
        }

        // Projectile streams (W3DProjectileStreamDraw): one additive band per run of projectiles, sorted by its middle like every other item.
        for (var index = 0; index < frame.ProjectileStreams.Length; index++)
        {
            var stream = frame.ProjectileStreams[index];
            if (stream.Points.Length < 2 || stream.Texture.Length == 0 || stream.Width <= 0f) continue;
            var middle = (stream.Points[0] + stream.Points[^1]) * 0.5f;
            Add(ref count, new Item { Kind = ItemKind.Stream, Material = MaterialId(new FxMaterialKey(stream.Texture, FxBlend.Additive)), A = index }, Depth(middle));
        }

        if (count == 0)
        {
            return;
        }

        // Far to near: sort ascending by negated depth.
        Array.Sort(_sortKeys, _sortOrder, 0, count);
        Array.Clear(_deferred, 0, count);

        var sortedRuns = 0;
        var lastMaterial = -1;
        var capped = false;
        for (var at = 0; at < count; at++)
        {
            ref readonly var item = ref _items[_sortOrder[at]];
            if (item.Material != lastMaterial)
            {
                if (sortedRuns >= MaxSortedRuns)
                {
                    capped = true;
                }
                else
                {
                    sortedRuns++;
                }
            }

            if (capped && item.Material != lastMaterial)
            {
                // From the first material change beyond the cap on, everything left is grouped by material.
                for (var rest = at; rest < count; rest++)
                {
                    _deferred[rest] = true;
                }

                break;
            }

            lastMaterial = item.Material;
            EmitItem(frame, in item, -_sortKeys[at]);
        }

        if (!capped)
        {
            return;
        }

        var firstDeferred = 0;
        while (firstDeferred < count && !_deferred[firstDeferred])
        {
            firstDeferred++;
        }

        var done = new bool[_materials.Count];
        for (var at = firstDeferred; at < count; at++)
        {
            var material = _items[_sortOrder[at]].Material;
            if (done[material])
            {
                continue;
            }

            done[material] = true;
            for (var rest = at; rest < count; rest++)
            {
                ref readonly var item = ref _items[_sortOrder[rest]];
                if (item.Material == material)
                {
                    _deferredItems++;
                    EmitItem(frame, in item, -_sortKeys[rest]);
                }
            }
        }
    }

    private void Add(ref int count, in Item item, float depth)
    {
        if (count == _items.Length)
        {
            Array.Resize(ref _items, count * 2);
            Array.Resize(ref _sortKeys, count * 2);
            Array.Resize(ref _sortOrder, count * 2);
            Array.Resize(ref _deferred, count * 2);
        }

        _items[count] = item;
        _sortKeys[count] = -depth;
        _sortOrder[count] = count;
        count++;
    }

    private void EmitItem(FxFrame frame, in Item item, float depth)
    {
        var first = geometry.QuadCount;
        switch (item.Kind)
        {
            case ItemKind.Particle:
                EmitBillboard(frame.Particles.Particles[item.A]);
                _particlesDrawn++;
                break;
            case ItemKind.Streak:
                EmitStreak(frame.Particles.Particles, item.A, item.B);
                _particlesDrawn += item.B;
                _streaks++;
                break;
            case ItemKind.Stream:
                EmitStream(frame.ProjectileStreams[item.A]);
                _streaks++;
                break;
            default:
                EmitTracer(frame.Tracers[item.A], depth);
                _tracers++;
                break;
        }

        EndItem(item.Material, first, depth, 2);
    }

    // ------------------------------------------------------------------------------------------------ primitives

    private void EmitBillboard(in ParticleInstance particle)
    {
        var quad = geometry.NextQuad();
        if (quad.IsEmpty)
        {
            return;
        }

        var half = particle.Size * 0.5f;
        var cos = MathF.Cos(particle.Rotation);
        var sin = MathF.Sin(particle.Rotation);
        var a = (_view.Right * cos + _view.Up * sin) * half;
        var b = (_view.Up * cos - _view.Right * sin) * half;
        WriteQuad(quad, particle.Position, a, b, PackParticle(particle));
    }

    private void EmitGroundSprite(in ParticleInstance particle)
    {
        var quad = geometry.NextQuad();
        if (quad.IsEmpty)
        {
            return;
        }

        var half = particle.Size * 0.5f;
        var cos = MathF.Cos(particle.Rotation);
        var sin = MathF.Sin(particle.Rotation);
        var a = new Vector3(cos, sin, 0f) * half;
        var b = new Vector3(-sin, cos, 0f) * half;
        WriteQuad(quad, particle.Position, a, b, PackParticle(particle));

        // Keep the flat sprite on a sloped field: shift each corner by the terrain height difference to the center.
        var baseHeight = heights.HeightAt(particle.Position.X, particle.Position.Y);
        for (var index = 0; index < 4; index++)
        {
            ref var vertex = ref quad[index];
            vertex.Position.Z += heights.HeightAt(vertex.Position.X, vertex.Position.Y) - baseHeight + GroundLift;
        }
    }

    private static void WriteQuad(Span<FxVertex> quad, Vector3 center, Vector3 a, Vector3 b, uint color)
    {
        quad[0] = new FxVertex { Position = center - a + b, Color = color, Uv = new Vector2(0f, 0f) };
        quad[1] = new FxVertex { Position = center - a - b, Color = color, Uv = new Vector2(0f, 1f) };
        quad[2] = new FxVertex { Position = center + a - b, Color = color, Uv = new Vector2(1f, 1f) };
        quad[3] = new FxVertex { Position = center + a + b, Color = color, Uv = new Vector2(1f, 0f) };
    }

    /// <summary>
    /// A ribbon through the particles in instance order (<c>W3DParticleSystemManager::doParticles</c> passes them to the streak line in that
    /// order) with the width of each particle; the first point is zeroed like the reference to remove the hard trailing edge.
    /// </summary>
    private void EmitStreak(System.Collections.Immutable.ImmutableArray<ParticleInstance> particles, int start, int count)
    {
        var length = 0f;
        for (var index = 1; index < count; index++)
        {
            length += Vector3.Distance(particles[start + index - 1].Position, particles[start + index].Position);
        }

        var travelled = 0f;
        var previousLeft = Vector3.Zero;
        var previousRight = Vector3.Zero;
        var previousColor = 0u;
        var previousU = 0f;
        for (var index = 0; index < count; index++)
        {
            var particle = particles[start + index];
            if (index > 0)
            {
                travelled += Vector3.Distance(particles[start + index - 1].Position, particle.Position);
            }

            var direction = index + 1 < count
                ? particles[start + index + 1].Position - particle.Position
                : particle.Position - particles[start + index - 1].Position;
            if (index > 0 && index + 1 < count)
            {
                direction = particles[start + index + 1].Position - particles[start + index - 1].Position;
            }

            var side = SideVector(direction, particle.Position, particle.Size * 0.5f);
            var left = particle.Position - side;
            var right = particle.Position + side;
            var color = index == 0 ? Pack(Vector3.Zero, 0f) : PackParticle(particle);
            var u = length > 0f ? travelled / length : 0f;
            if (index > 0)
            {
                var quad = geometry.NextQuad();
                if (quad.IsEmpty)
                {
                    return;
                }

                quad[0] = new FxVertex { Position = previousLeft, Color = previousColor, Uv = new Vector2(previousU, 0f) };
                quad[1] = new FxVertex { Position = previousRight, Color = previousColor, Uv = new Vector2(previousU, 1f) };
                quad[2] = new FxVertex { Position = right, Color = color, Uv = new Vector2(u, 1f) };
                quad[3] = new FxVertex { Position = left, Color = color, Uv = new Vector2(u, 0f) };
            }

            previousLeft = left;
            previousRight = right;
            previousColor = color;
            previousU = u;
        }
    }

    /// <summary>
    /// A projectile stream (<c>W3DProjectileStreamDraw</c>, a <c>SegmentedLineClass</c> with <c>TILED_TEXTURE_MAP</c>): a camera-facing band of the stream's width through its
    /// points, the texture repeated <see cref="StreamInstance.TileFactor"/> times per segment (here one quad per repetition: the effect sampler clamps). The scroll of the
    /// texture (<c>ScrollRate</c>) is not reproduced.
    /// </summary>
    private void EmitStream(StreamInstance stream)
    {
        var color = Pack(Vector3.One, 1f);
        var tiles = Math.Max(1, (int)MathF.Round(stream.TileFactor));
        for (var segment = 0; segment + 1 < stream.Points.Length; segment++)
        {
            var from = stream.Points[segment];
            var to = stream.Points[segment + 1];
            var side = SideVector(to - from, (from + to) * 0.5f, stream.Width * 0.5f);
            for (var tile = 0; tile < tiles; tile++)
            {
                var start = Vector3.Lerp(from, to, tile / (float)tiles);
                var end = Vector3.Lerp(from, to, (tile + 1) / (float)tiles);
                var quad = geometry.NextQuad();
                if (quad.IsEmpty) return;
                quad[0] = new FxVertex { Position = start - side, Color = color, Uv = new Vector2(0f, 0f) };
                quad[1] = new FxVertex { Position = start + side, Color = color, Uv = new Vector2(0f, 1f) };
                quad[2] = new FxVertex { Position = end + side, Color = color, Uv = new Vector2(1f, 1f) };
                quad[3] = new FxVertex { Position = end - side, Color = color, Uv = new Vector2(1f, 0f) };
            }
        }
    }

    /// <summary>A tracer is an untextured camera-facing ribbon from head to head + direction * length, at least 1.5 pixels wide.</summary>
    private void EmitTracer(in TracerInstance tracer, float depth)
    {
        var quad = geometry.NextQuad();
        if (quad.IsEmpty)
        {
            return;
        }

        var tail = tracer.Head;
        var head = tracer.Head + tracer.Direction * tracer.Length;
        var width = Math.Max(tracer.Width, MinTracerPixels * _view.WorldPerPixelAtUnitDepth * Math.Max(depth, _view.Near));
        var middle = (tail + head) * 0.5f;
        var side = SideVector(head - tail, middle, width * 0.5f);
        var color = Pack(tracer.Color, tracer.Opacity);
        quad[0] = new FxVertex { Position = tail - side, Color = color, Uv = new Vector2(0f, 0f) };
        quad[1] = new FxVertex { Position = tail + side, Color = color, Uv = new Vector2(0f, 1f) };
        quad[2] = new FxVertex { Position = head + side, Color = color, Uv = new Vector2(1f, 1f) };
        quad[3] = new FxVertex { Position = head - side, Color = color, Uv = new Vector2(1f, 0f) };
    }

    /// <summary>Half-width vector perpendicular to <paramref name="direction"/> and the line of sight to <paramref name="point"/>.</summary>
    private Vector3 SideVector(Vector3 direction, Vector3 point, float halfWidth)
    {
        var side = Vector3.Cross(direction, point - _view.Eye);
        var length = side.Length();
        return length > 1e-6f ? side * (halfWidth / length) : _view.Right * halfWidth;
    }

    // ------------------------------------------------------------------------------------------------ helpers

    private float Depth(Vector3 position) => Vector3.Dot(position - _view.Eye, _view.Forward);

    private void EndItem(int material, int firstQuad, float depth, byte group)
    {
        var count = geometry.QuadCount - firstQuad;
        if (count <= 0)
        {
            return;
        }

        if (_runCount > 0)
        {
            ref var last = ref _runs[_runCount - 1];
            if (last.Material == material && last.Group == group && last.First + last.Count == firstQuad)
            {
                last.Count += count;
                last.DepthSum += depth;
                last.Items++;
                return;
            }
        }

        if (_runCount == _runs.Length)
        {
            Array.Resize(ref _runs, _runCount * 2);
        }

        _runs[_runCount++] = new Run { Material = material, First = firstQuad, Count = count, DepthSum = depth, Items = 1, Group = group };
    }

    private uint PackParticle(in ParticleInstance particle)
        => Pack(new Vector3(particle.Color.X, particle.Color.Y, particle.Color.Z), particle.Color.W);

    private uint Pack(Vector3 rgb, float alpha)
    {
        var r = ToByte(rgb.X);
        var g = ToByte(rgb.Y);
        var b = ToByte(rgb.Z);
        if (linearColorSpace)
        {
            r = _linearLut[r];
            g = _linearLut[g];
            b = _linearLut[b];
        }

        return r | ((uint)g << 8) | ((uint)b << 16) | ((uint)ToByte(alpha) << 24);
    }

    private static byte ToByte(float value) => (byte)(Math.Clamp(value, 0f, 1f) * 255f + 0.5f);

    /// <summary>W3D modulates sRGB texture and vertex colors without gamma handling; in a linear pipeline the vertex color must be linearized to match.</summary>
    private static byte[] BuildLut()
    {
        var lut = new byte[256];
        for (var index = 0; index < 256; index++)
        {
            var value = index / 255f;
            var linear = value <= 0.04045f ? value / 12.92f : MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
            lut[index] = (byte)(linear * 255f + 0.5f);
        }

        return lut;
    }
}
