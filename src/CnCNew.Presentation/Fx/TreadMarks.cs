using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Modules;
using CnCNew.Simulation.World;

namespace CnCNew.Presentation.Fx;

/// <summary>
/// The limits of the track marks (<c>GlobalData</c> <c>MaxTerrainTracks</c>, and the high level of <c>GameLOD.ini</c>: <c>MaxTankTrackEdges</c>, <c>MaxTankTrackOpaqueEdges</c>,
/// <c>MaxTankTrackFadeDelay</c>, which <c>TerrainTracksRenderObjClassSystem::setDetail</c> reads).
/// </summary>
public sealed record TreadSettings(int MaxTracks = 100, int MaxEdges = 100, int OpaqueEdges = 25, double FadeSeconds = 60.0, float EdgeLength = 10f, float DefaultWidth = 14f, float WidthMargin = 4f);

/// <summary>One cross-section of a track: its two ends with their texture coordinates, and its opacity (the age fade and the fade with the distance behind the vehicle already applied).</summary>
public readonly record struct TreadEdge(Vector3 Left, Vector3 Right, Vector2 LeftUv, Vector2 RightUv, float Alpha);

/// <summary>The marks one vehicle has left, oldest edge first (<c>TerrainTracksRenderObjClass</c>); consecutive edges are joined by a quad. At least two edges.</summary>
public sealed record TreadStrip(string Texture, ImmutableArray<TreadEdge> Edges);

/// <summary>
/// One <c>TerrainTracksRenderObjClass</c>: a ring of at most <see cref="TreadSettings.MaxEdges"/> edges that a vehicle lays down while it moves
/// (<c>addEdgeToTrack</c>: an edge for every <see cref="TreadSettings.EdgeLength"/> world units from the last, at the ground and a little above it, across the heading), caps when it
/// is hidden (<c>addCapEdgeToTrack</c>: a transparent end, so the next stretch begins apart), restarts transparent after a flight, and fades with age (<c>update</c>).
/// </summary>
public sealed class TreadTrack(TreadSettings settings, string texture, float width)
{
    private const float RaiseAboveTerrain = 2f; // 0.2 * MAP_XY_FACTOR

    private struct Edge
    {
        public Vector3 Left, Right;
        public Vector2 LeftUv, RightUv;
        public double Added;
        public float Alpha;
    }

    private readonly Edge[] _edges = new Edge[settings.MaxEdges];
    private int _top, _bottom, _active, _totalAdded;
    private bool _haveAnchor, _haveCap = true, _airborne;
    private Vector3 _lastAnchor = new(0f, 1f, 2.25f);

    public string Texture { get; } = texture;
    public float Width { get; } = width;
    public int ActiveEdges => _active;

    /// <summary>The vehicle is still bound to it; an unbound track is dropped when its last edge has faded.</summary>
    public bool Bound { get; set; } = true;

    public void SetAirborne() => _airborne = true;

    /// <summary><c>addEdgeToTrack</c>. <paramref name="height"/> is the ground (or a bridge's deck) under a place.</summary>
    public void AddEdge(float x, float y, Func<float, float, float> height, double seconds)
    {
        if (!_haveAnchor)
        {
            _lastAnchor = new Vector3(x, y, height(x, y));
            _haveAnchor = true;
            _airborne = true;
            _haveCap = true; // a single segment draws nothing
            return;
        }
        _haveCap = false;
        Append(x, y, height, seconds, 1f, restart: true);
    }

    /// <summary><c>addCapEdgeToTrack</c>: the vehicle left (the fog, stealth, hidden): end the stretch with a transparent edge so the track resumes elsewhere.</summary>
    public void AddCap(float x, float y, Func<float, float, float> height, double seconds)
    {
        if (_haveCap) return;
        if (_active == 1)
        {
            _haveCap = true;
            _haveAnchor = false;
            return;
        }
        var position = new Vector3(x, y, height(x, y));
        if ((position - _lastAnchor).LengthSquared() < settings.EdgeLength * settings.EdgeLength)
        {
            // Too close to add an edge: the last one becomes the transparent end.
            var last = (_top - 1 + _edges.Length) % _edges.Length;
            _edges[last].Alpha = 0f;
            _haveCap = true;
            _haveAnchor = false;
            return;
        }
        Append(x, y, height, seconds, 0f, restart: false);
        _haveCap = true;
        _haveAnchor = false;
    }

    private void Append(float x, float y, Func<float, float, float> height, double seconds, float alpha, bool restart)
    {
        var position = new Vector3(x, y, height(x, y));
        var direction = position - _lastAnchor;
        if (direction.LengthSquared() < settings.EdgeLength * settings.EdgeLength) return;
        if (_active >= _edges.Length)
        {
            _bottom = (_bottom + 1) % _edges.Length;
            _active--;
        }
        _top %= _edges.Length;
        direction.Z = 0f;
        direction = direction.LengthSquared() > 0f ? Vector3.Normalize(direction) : Vector3.UnitX;
        var across = Vector3.Cross(direction, Normal(x, y, height));
        var odd = (_totalAdded & 1) != 0;
        ref var edge = ref _edges[_top];
        edge.Left = position - across * (Width * 0.5f) + new Vector3(0f, 0f, RaiseAboveTerrain);
        edge.Right = position + across * (Width * 0.5f) + new Vector3(0f, 0f, RaiseAboveTerrain);
        // Every other edge has the other set of texture coordinates.
        edge.LeftUv = new Vector2(0f, odd ? 0f : 1f);
        edge.RightUv = new Vector2(1f, odd ? 0f : 1f);
        edge.Added = seconds;
        edge.Alpha = alpha;
        if (restart && (_airborne || _active <= 1)) edge.Alpha = 0f; // smooth out the start of a track
        if (restart) _airborne = false;
        _lastAnchor = position;
        _active++;
        _totalAdded++;
        _top++;
    }

    private static Vector3 Normal(float x, float y, Func<float, float, float> height)
    {
        const float step = 5f;
        var dx = height(x + step, y) - height(x - step, y);
        var dy = height(x, y + step) - height(x, y - step);
        return Vector3.Normalize(new Vector3(-dx, -dy, 2f * step));
    }

    /// <summary><c>TerrainTracksRenderObjClassSystem::update</c>: an edge fades linearly over the fade time and is released when it is gone.</summary>
    public void Fade(double seconds)
    {
        for (var i = 0; i < _active; i++)
        {
            ref var edge = ref _edges[(_bottom + i) % _edges.Length];
            var left = (float)Math.Max(0.0, 1.0 - (seconds - edge.Added) / settings.FadeSeconds);
            if (edge.Alpha > 0f) edge.Alpha = left;
        }
        while (_active > 0)
        {
            ref var oldest = ref _edges[_bottom];
            if (seconds - oldest.Added < settings.FadeSeconds) break;
            _bottom = (_bottom + 1) % _edges.Length;
            _active--;
        }
    }

    /// <summary><c>TerrainTracksRenderObjClassSystem::flush</c>'s opacity: the last <see cref="TreadSettings.OpaqueEdges"/> edges as they are, the older ones fading out towards the end of the ring.</summary>
    public TreadStrip? Snapshot()
    {
        if (_active < 2) return null;
        var faded = (float)(settings.MaxEdges - settings.OpaqueEdges);
        var edges = ImmutableArray.CreateBuilder<TreadEdge>(_active);
        for (var i = 0; i < _active; i++)
        {
            var edge = _edges[(_bottom + i) % _edges.Length];
            var fade = 1f;
            if (_active - 1 - i >= settings.OpaqueEdges) fade = 1f - (_active - i - settings.OpaqueEdges) / faded;
            edges.Add(new TreadEdge(edge.Left, edge.Right, edge.LeftUv, edge.RightUv, Math.Clamp(fade * edge.Alpha, 0f, 1f)));
        }
        return new TreadStrip(Texture, edges.MoveToImmutable());
    }
}

/// <summary>
/// The track marks of the vehicles (<c>W3DModelDraw</c> <c>TrackMarks</c>, <c>W3DTerrainTracks</c>): a vehicle whose draw module names a track texture is bound to a track when its model
/// state is set, lays an edge in every tick that moved it far enough, ends the stretch when it is out of the viewer's sight or stealthed, and lets go of the track when it is gone
/// (the marks fade on). At most <see cref="TreadSettings.MaxTracks"/> vehicles at a time have one. The width is the distance between the model's <c>TREADFX01</c> and <c>TREADFX02</c> bones
/// plus the width of one tread, or a default. Drawn only; the simulation knows nothing of it.
/// </summary>
public sealed class TreadMarks(TreadSettings settings, Func<string, string, Vector3?> bones)
{
    private readonly Dictionary<uint, TreadTrack> _bound = [];
    private readonly List<TreadTrack> _tracks = [];
    private readonly HashSet<uint> _seen = [];

    /// <summary>Whose sight decides (<c>m_fullyObscuredByShroud</c>); null: everything is seen.</summary>
    public PlayerId? Viewer { get; set; }

    public int Tracks => _tracks.Count;

    /// <summary>Lay the edges of this tick (call after every tick with the objects the projector shows).</summary>
    public void Update(SimulationWorld world, IEnumerable<ProjectedEntity> projected, IFxWorldView view)
    {
        var seconds = world.Tick / (double)((long)world.Time.TicksPerOriginalFrame * CnCNew.Data.OriginalFrame.PerSecond);
        _seen.Clear();
        foreach (var item in projected)
        {
            if (TrackTexture(item) is not { } texture || world.FindEntity(item.Entity) is not { } entity || entity.EffectivelyDead) continue;
            _seen.Add(item.Entity.Value);
            if (!_bound.TryGetValue(item.Entity.Value, out var track))
            {
                if (_bound.Count >= settings.MaxTracks) continue; // bindTrack: no free track
                track = new TreadTrack(settings, texture.Name, Width(texture.Model));
                _bound[item.Entity.Value] = track;
                _tracks.Add(track);
            }
            if (view.GetPose(item.Entity) is not { } pose) continue;
            var layer = entity.Layer;
            float Height(float x, float y) => layer == SimulationWorld.GroundLayer
                ? view.GroundHeight(x, y)
                : FixedConvert.ToSingle(world.LayerHeightAt(FixedConvert.FromSingle(x), FixedConvert.FromSingle(y), layer)) + 0.25f;
            var obscured = Viewer is { } viewer && !world.IsPositionVisibleTo(viewer, entity.X, entity.Y);
            if (obscured || entity.HasStatus(StealthedStatus)) track.AddCap(pose.Position.X, pose.Position.Y, Height, seconds);
            else
            {
                if (world.IsSignificantlyAboveTerrain(entity)) track.SetAirborne();
                track.AddEdge(pose.Position.X, pose.Position.Y, Height, seconds);
            }
        }
        foreach (var id in _bound.Keys.Where(id => !_seen.Contains(id)).ToArray())
        {
            _bound[id].Bound = false;
            _bound.Remove(id);
        }
        foreach (var track in _tracks) track.Fade(seconds);
        _tracks.RemoveAll(track => !track.Bound && track.ActiveEdges == 0);
    }

    /// <summary>The marks that are on the ground now.</summary>
    public ImmutableArray<TreadStrip> Snapshot()
    {
        var strips = ImmutableArray.CreateBuilder<TreadStrip>();
        foreach (var track in _tracks)
            if (track.Snapshot() is { } strip) strips.Add(strip);
        return strips.ToImmutable();
    }

    private static readonly int StealthedStatus = ObjectStatusBits.Index("STEALTHED");

    private float Width(string? model)
    {
        if (model is not null && bones(model, "treadfx01") is { } left && bones(model, "treadfx02") is { } right)
            return (right - left).Length() + settings.WidthMargin;
        return settings.DefaultWidth;
    }

    /// <summary>The first draw module that names a track texture (a file name; <c>TrackMarks = Yes</c> of one object is no texture) with the model of its current state.</summary>
    private static (string Name, string? Model)? TrackTexture(ProjectedEntity item)
    {
        for (var module = 0; module < item.Visual.Draws.Length; module++)
        {
            if (item.Visual.Draws[module].TrackMarks is not { } name || !name.Contains('.')) continue;
            string? model = null;
            if (module < item.Selections.Length && item.Selections[module].StateIndex is >= 0 and var state) model = item.Visual.Draws[module].States[state].Model;
            return (name, model);
        }
        return null;
    }
}
