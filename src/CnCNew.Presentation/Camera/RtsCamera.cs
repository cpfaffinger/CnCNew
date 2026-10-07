using System.Numerics;
using CnCNew.Data;
using CnCNew.Presentation.Terrain;

namespace CnCNew.Presentation.Camera;

/// <summary>
/// The RTS camera of the retail game (<c>W3DView</c>, <c>View</c>, <c>LookAtTranslator</c> of the EA Zero Hour sources), renderer-neutral.
/// <para>
/// Model: a pivot on the map (<see cref="Pivot"/>, x/y) at a per-map fixed <see cref="GroundLevel"/>; the camera sits at
/// <c>pivot + factor * Rz(angle) * Rpitch * offset * zoom</c> with <c>offset = (0, -H / tan(CameraPitch), H)</c>,
/// <c>H = GroundLevel + CameraHeight</c> and <c>factor = 1 - GroundLevel / (H * zoom)</c> (<c>buildCameraTransform</c>). The zoom follows the
/// desired height above the highest terrain around the pivot (<c>getHeightAroundPos</c>) with <c>CameraAdjustSpeed</c> per 30 Hz frame.
/// </para>
/// <para>
/// Conventions: world right-handed, Z up. <see cref="View"/> is <see cref="Matrix4x4.CreateLookAt"/>, <see cref="Projection"/> is
/// <see cref="Matrix4x4.CreatePerspectiveFieldOfView"/> (right-handed, clip depth 0..1), row vectors: <c>clip = world * ViewProjection</c>.
/// Screen pixels have their origin top-left, y down.
/// </para>
/// </summary>
public sealed class RtsCamera : IViewProjection
{
    // Camera values of the original are amounts per original frame; the camera itself runs on wall time. Never the simulation's tick rate.
    private const float FrameRate = OriginalFrame.PerSecond;
    private readonly RtsCameraSettings _settings;
    private readonly TerrainHeightSampler _terrain;
    private Vector2 _pivot;
    private Vector2 _shakeOffset;
    private Vector2 _extraShake;
    private float _shakeIntensity;
    private float _shakeCos = 1f;
    private float _shakeSin;
    private float _shakeTime;
    private Vector2 _viewport;
    private bool _dirty = true;
    private Matrix4x4 _view, _projection, _viewProjection, _inverseViewProjection;
    private Vector3 _eye, _target;
    private float _far;
    private int _activeBoundary;

    public RtsCamera(RtsCameraSettings settings, TerrainHeightSampler terrain, Vector2 viewportSize)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
        SetViewportSize(viewportSize);
        Pitch = DegreesToRadians(settings.PitchDegrees);
        HeightAboveGround = settings.MaxHeightAboveGround;
        ScrollFactor = Input.CameraSettings.DefaultScrollOption;
        InitializeForMap(terrain.BoundaryMax(0) * 0.5f);
    }

    public RtsCameraSettings Settings => _settings;

    /// <summary>Look-at point on the ground plane (x, y) before shake and constraints are applied again.</summary>
    public Vector2 Pivot => _pivot;

    /// <summary><c>m_groundLevel</c>: fixed per map by <see cref="InitializeForMap"/>; the look-at target's z.</summary>
    public float GroundLevel { get; private set; }

    /// <summary>Rotation about the up axis in radians, 0 = the default <c>CameraYaw</c> view (positive turns counter-clockwise seen from above).</summary>
    public float Angle { get; private set; }

    /// <summary>Elevation of the view direction in radians (default <c>CameraPitch</c>), clamped like <c>View::setPitch</c>.</summary>
    public float Pitch { get; private set; }

    /// <summary><c>m_heightAboveGround</c>: the height above the terrain the zoom settles towards, clamped to [MinCameraHeight, MaxCameraHeight].</summary>
    public float HeightAboveGround { get; private set; }

    /// <summary><c>m_zoom</c>: scale of the camera offset.</summary>
    public float Zoom { get; private set; }

    /// <summary>The user's scroll speed option (<c>m_keyboardScrollFactor</c>); starts at the option default 0.5, not at <c>KeyboardScrollSpeedFactor</c> of
    /// GameData.ini, which the original overwrites with the option when it loads (<c>GlobalData::parseGameDataDefinition</c>).</summary>
    public float ScrollFactor { get; set; }

    /// <summary>Last scroll delta (<c>m_scrollAmount</c>), used to decide whether the zoom follows the terrain while scrolling.</summary>
    public Vector2 LastScrollAmount { get; private set; }

    public Vector2 ViewportSize => _viewport;

    /// <summary>The map boundary the pivot is constrained to (<c>m_activeBoundary</c>, 0 unless a script changes it).</summary>
    public int ActiveBoundary
    {
        get => _activeBoundary;
        set
        {
            if ((uint)value >= (uint)_terrain.Boundaries.Length) throw new ArgumentOutOfRangeException(nameof(value));
            _activeBoundary = value;
            ClampToConstraints();
        }
    }

    /// <summary><c>getCameraOffsetZ</c> of the retail view: ground level plus <c>CameraHeight</c>.</summary>
    public float OffsetHeight => GroundLevel + _settings.CameraHeight;

    public Vector3 Eye { get { Refresh(); return _eye; } }
    public Vector3 Target { get { Refresh(); return _target; } }
    public float FarPlane { get { Refresh(); return _far; } }
    public Matrix4x4 View { get { Refresh(); return _view; } }
    public Matrix4x4 Projection { get { Refresh(); return _projection; } }
    public Matrix4x4 ViewProjection { get { Refresh(); return _viewProjection; } }
    public Matrix4x4 InverseViewProjection { get { Refresh(); return _inverseViewProjection; } }
    public Frustum Frustum => Frustum.FromViewProjection(ViewProjection);

    /// <summary>Current shake displacement of the pivot (reference shake plus <see cref="AddShakeOffset"/>).</summary>
    public Vector2 ShakeOffset => _shakeOffset + _extraShake;

    public void SetViewportSize(Vector2 size)
    {
        if (!(size.X >= 1 && size.Y >= 1)) throw new ArgumentOutOfRangeException(nameof(size), "The viewport needs at least 1x1 pixels.");
        _viewport = size;
        _dirty = true;
    }

    /// <summary><c>W3DView::initHeightForMap</c> after a <c>lookAt</c>: fixes the ground level at the pivot (at most 120) and resets zoom to default.</summary>
    public void InitializeForMap(Vector2 lookAt)
    {
        _pivot = lookAt;
        GroundLevel = Math.Min(_terrain.HeightAt(lookAt.X, lookAt.Y), _settings.MaxGroundLevel);
        SetZoomToDefault();
    }

    /// <summary><c>View::setZoomToDefault</c>: maximum height above ground, zoom snapped to it.</summary>
    public void SetZoomToDefault()
    {
        HeightAboveGround = _settings.MaxHeightAboveGround;
        Zoom = DesiredZoom();
        _dirty = true;
        ClampToConstraints();
    }

    /// <summary><c>W3DView::lookAt</c>: centre the view on a world point.</summary>
    public void CenterOn(Vector2 world)
    {
        _pivot = world;
        _dirty = true;
        ClampToConstraints();
    }

    public void SetAngle(float radians)
    {
        Angle = MathF.IEEERemainder(radians, 2 * MathF.PI);
        _dirty = true;
        ClampToConstraints();
    }

    public void RotateBy(float radians) => SetAngle(Angle + radians);

    /// <summary>Keyboard rotation: <c>KeyboardCameraRotateSpeed</c> radians per 30 Hz frame.</summary>
    public void RotateByKeyboard(int direction, float dtSeconds) => RotateBy(direction * _settings.KeyboardRotateSpeed * dtSeconds * FrameRate);

    /// <summary>Middle-mouse drag: angle = anchor angle + 0.01 * (x - anchor x).</summary>
    public float MouseRotateAngle(float anchorAngle, float anchorX, float currentX) => anchorAngle + _settings.MouseRotateRadiansPerPixel * (currentX - anchorX);

    public void SetPitch(float radians)
    {
        Pitch = Math.Clamp(radians, DegreesToRadians(_settings.MinPitchDegrees), DegreesToRadians(_settings.MaxPitchDegrees));
        _dirty = true;
        ClampToConstraints();
    }

    public void SetPitchToDefault() => SetPitch(DegreesToRadians(_settings.PitchDegrees));

    public void SetHeightAboveGround(float height)
    {
        HeightAboveGround = Math.Clamp(height, _settings.MinHeightAboveGround, _settings.MaxHeightAboveGround);
        ClampToConstraints();
    }

    /// <summary>Mouse wheel: each notch towards the user (positive) zooms in by <see cref="RtsCameraSettings.WheelZoomStep"/>.</summary>
    public void ZoomByWheel(int notches) => SetHeightAboveGround(HeightAboveGround - notches * _settings.WheelZoomStep);

    /// <summary>
    /// <c>W3DView::scrollBy</c>: a scroll delta in reference units is taken as a pixel offset of <c>delta * 250</c> (y also times the aspect
    /// ratio) on the camera's view plane at distance 1, turned into world space by the camera rotation and applied to the pivot's x/y.
    /// </summary>
    public void ScrollBy(Vector2 delta)
    {
        LastScrollAmount = delta;
        if (delta == Vector2.Zero) return;
        Refresh();
        var aspect = _viewport.X / _viewport.Y;
        var tanX = MathF.Tan(DegreesToRadians(_settings.HorizontalFovDegrees) / 2);
        var tanY = tanX / aspect;
        var viewX = delta.X * _settings.ScrollResolution / _viewport.X * 2 * tanX;
        var viewY = -delta.Y * _settings.ScrollResolution * aspect / _viewport.Y * 2 * tanY;
        Matrix4x4.Invert(_view, out var cameraToWorld);
        var world = Vector3.TransformNormal(new Vector3(viewX, viewY, 0), cameraToWorld);
        _pivot += new Vector2(world.X, world.Y);
        _dirty = true;
        ClampToConstraints();
    }

    /// <summary>
    /// Scrolls by an input-layer amount (<c>CameraIntent.Scroll</c>: x right, y forward, 1 = the base scroll speed for one second, the speed factors already applied):
    /// <c>SCROLL_AMT</c> per 30 Hz frame in <see cref="ScrollBy"/>'s units, whose y points down the screen.
    /// </summary>
    public void ScrollByIntent(Vector2 amount) => ScrollBy(new Vector2(amount.X, -amount.Y) * (_settings.ScrollAmount * FrameRate));

    /// <summary>Key scrolling (<c>SCROLL_KEY</c>): <c>speedFactor * SCROLL_AMT * scrollFactor</c> per 30 Hz frame in each held direction.</summary>
    public Vector2 KeyboardScrollDelta(bool up, bool down, bool left, bool right, float dtSeconds)
    {
        var frames = dtSeconds * FrameRate;
        var x = (right ? 1 : 0) - (left ? 1 : 0);
        var y = (down ? 1 : 0) - (up ? 1 : 0);
        return new Vector2(x * _settings.HorizontalScrollSpeedFactor, y * _settings.VerticalScrollSpeedFactor) * (_settings.ScrollAmount * ScrollFactor * frames);
    }

    /// <summary>Screen-edge scrolling (<c>SCROLL_SCREENEDGE</c>): like keys while the cursor is within 3 pixels of an edge.</summary>
    public Vector2 EdgeScrollDelta(Vector2 cursor, float dtSeconds)
    {
        var edge = _settings.EdgeScrollPixels;
        return KeyboardScrollDelta(cursor.Y < edge, cursor.Y >= _viewport.Y - edge, cursor.X < edge, cursor.X >= _viewport.X - edge, dtSeconds);
    }

    /// <summary>Right-mouse drag scrolling (<c>SCROLL_RMB</c>): the speed factors times the cursor's pixel offset from the anchor, per 30 Hz frame.</summary>
    public Vector2 RightMouseScrollDelta(Vector2 anchor, Vector2 cursor, float dtSeconds)
    {
        var offset = cursor - anchor;
        return new Vector2(offset.X * _settings.HorizontalScrollSpeedFactor, offset.Y * _settings.VerticalScrollSpeedFactor) * (dtSeconds * FrameRate);
    }

    /// <summary>
    /// <c>W3DView::shake</c>: adds intensity falling off linearly to zero at <c>MaxShakeRange</c> from the pivot. The direction angle is the
    /// caller's (the reference draws it from the client random stream). Above <c>MaxShakeIntensity</c> the reference resets the intensity to 3.
    /// </summary>
    public void Shake(Vector2 epicenter, float intensity, float directionRadians)
    {
        _shakeCos = MathF.Cos(directionRadians);
        _shakeSin = MathF.Sin(directionRadians);
        var distance = Vector2.Distance(epicenter, _pivot);
        if (distance > _settings.MaxShakeRange) return;
        _shakeIntensity += intensity * (1f - distance / _settings.MaxShakeRange);
        if (_shakeIntensity > _settings.MaxShakeIntensity) _shakeIntensity = 3f;
    }

    /// <summary>Additive pivot displacement for effects outside the reference shake; replaced on every call, cleared with zero.</summary>
    public void AddShakeOffset(Vector2 offset)
    {
        _extraShake = offset;
        _dirty = true;
    }

    /// <summary>
    /// Per-frame update (<c>W3DView::update</c>): steps the shake spring (damping 0.75 and reversal per 30 Hz frame) and moves the zoom towards
    /// the desired height above the terrain around the pivot. While the user scrolls faster than <c>ScrollAmountCutoff</c> the zoom only
    /// follows when the camera gets below the minimum height (or above the maximum with <c>EnforceMaxCameraHeight</c>).
    /// </summary>
    public void Update(float dtSeconds, bool isScrolling)
    {
        if (dtSeconds < 0) throw new ArgumentOutOfRangeException(nameof(dtSeconds));
        _shakeTime += dtSeconds;
        while (_shakeTime >= 1f / FrameRate)
        {
            _shakeTime -= 1f / FrameRate;
            if (_shakeIntensity > 0.01f)
            {
                _shakeOffset = new Vector2(_shakeIntensity * _shakeCos, _shakeIntensity * _shakeSin);
                _shakeIntensity *= 0.75f;
                _shakeCos = -_shakeCos;
                _shakeSin = -_shakeSin;
            }
            else
            {
                _shakeIntensity = 0f;
                _shakeOffset = Vector2.Zero;
            }
            _dirty = true;
        }

        var terrainUnder = HeightAroundPivot();
        var current = OffsetHeight * Zoom - terrainUnder;
        var adjust = !isScrolling
            || LastScrollAmount.Length() < _settings.ScrollAmountCutoff
            || current < _settings.MinHeightAboveGround
            || (_settings.EnforceMaxCameraHeight && current > _settings.MaxHeightAboveGround);
        if (adjust)
        {
            var desired = DesiredZoom();
            var factor = 1f - MathF.Pow(1f - Math.Clamp(_settings.AdjustSpeed, 0f, 1f), dtSeconds * FrameRate);
            var step = (desired - Zoom) * factor;
            if (MathF.Abs(step) >= 0.0001f)
            {
                Zoom += step;
                _dirty = true;
            }
        }
        ClampToConstraints();
    }

    /// <summary>Snaps the zoom to the desired height at once (useful after <see cref="CenterOn"/> in tests and cut-ins).</summary>
    public void SettleZoom()
    {
        Zoom = DesiredZoom();
        _dirty = true;
        ClampToConstraints();
    }

    /// <summary><c>getHeightAroundPos</c> (retail): the highest ground at the pivot and at the four diagonal points <c>TERRAIN_SAMPLE_SIZE</c> away.</summary>
    public float HeightAroundPivot()
    {
        var s = _settings.TerrainSampleSize;
        var height = _terrain.HeightAt(_pivot.X, _pivot.Y);
        height = Math.Max(height, _terrain.HeightAt(_pivot.X + s, _pivot.Y - s));
        height = Math.Max(height, _terrain.HeightAt(_pivot.X - s, _pivot.Y - s));
        height = Math.Max(height, _terrain.HeightAt(_pivot.X + s, _pivot.Y + s));
        height = Math.Max(height, _terrain.HeightAt(_pivot.X - s, _pivot.Y + s));
        return height;
    }

    private float DesiredZoom() => (HeightAroundPivot() + HeightAboveGround) / OffsetHeight;

    // ------------------------------------------------------------------ constraints

    /// <summary>
    /// <c>W3DView::calcCameraConstraints</c>: the pivot stays inside the active boundary's extent shrunk by the ground distance between the rays through
    /// the screen centre and through 95 % of the screen height (both intersected with the ground level). When that margin exceeds half the
    /// extent, the pivot is held at the centre of that axis.
    /// </summary>
    public (Vector2 Min, Vector2 Max) Constraints()
    {
        BuildMatrices(_pivot, out _, out _, out var view, out var projection, out _);
        var inverse = Inverse(view * projection);
        var center = GroundPoint(inverse, new Vector2(0.5f * _viewport.X, 0.5f * _viewport.Y));
        var bottom = GroundPoint(inverse, new Vector2(0.5f * _viewport.X, 0.95f * _viewport.Y));
        var offset = Vector2.Distance(center, bottom);
        var max = _terrain.BoundaryMax(ActiveBoundary);
        var lo = new Vector2(offset, offset);
        var hi = max - lo;
        if (lo.X > hi.X) lo.X = hi.X = max.X / 2;
        if (lo.Y > hi.Y) lo.Y = hi.Y = max.Y / 2;
        return (lo, hi);
    }

    private void ClampToConstraints()
    {
        var (lo, hi) = Constraints();
        _pivot = Vector2.Clamp(_pivot, lo, hi);
        _dirty = true;
    }

    private Vector2 GroundPoint(Matrix4x4 inverse, Vector2 screen)
    {
        var (origin, direction) = Ray(inverse, screen);
        if (MathF.Abs(direction.Z) < 1e-6f) return new Vector2(origin.X, origin.Y);
        var t = (GroundLevel - origin.Z) / direction.Z;
        var point = origin + direction * t;
        return new Vector2(point.X, point.Y);
    }

    // ------------------------------------------------------------------ matrices

    private void Refresh()
    {
        if (!_dirty) return;
        BuildMatrices(_pivot + ShakeOffset, out _eye, out _target, out _view, out _projection, out _far);
        _viewProjection = _view * _projection;
        _inverseViewProjection = Inverse(_viewProjection);
        _dirty = false;
    }

    private void BuildMatrices(Vector2 pivot, out Vector3 eye, out Vector3 target, out Matrix4x4 view, out Matrix4x4 projection, out float far)
    {
        var defaultPitch = DegreesToRadians(_settings.PitchDegrees);
        var yaw = DegreesToRadians(_settings.YawDegrees);
        var offsetZ = OffsetHeight;
        var offsetY = -(offsetZ / MathF.Tan(defaultPitch));
        var offsetX = -(offsetY * MathF.Tan(yaw));
        var source = new Vector3(offsetX, offsetY, offsetZ) * Zoom;
        var factor = 1f - GroundLevel / source.Z;
        // Positive pitch deltas raise the camera: a rotation about -X (W3DView.cpp buildCameraPosition).
        source = Vector3.Transform(source, Matrix4x4.CreateRotationX(-(Pitch - defaultPitch)));
        source = Vector3.Transform(source, Matrix4x4.CreateRotationZ(Angle));
        source *= factor;
        target = new Vector3(pivot, GroundLevel);
        eye = source + target;
        view = Matrix4x4.CreateLookAt(eye, target, Vector3.UnitZ);
        var aspect = _viewport.X / _viewport.Y;
        var fovY = 2f * MathF.Atan(MathF.Tan(DegreesToRadians(_settings.HorizontalFovDegrees) / 2f) / aspect);
        far = _settings.MinFarPlane;
        var fieldMin = _terrain.FieldMin;
        var fieldMax = _terrain.FieldMax;
        foreach (var corner in new[] { fieldMin, fieldMax, new Vector2(fieldMin.X, fieldMax.Y), new Vector2(fieldMax.X, fieldMin.Y) })
            far = Math.Max(far, Vector3.Distance(eye, new Vector3(corner, _terrain.MinHeight)) + 100f);
        projection = Matrix4x4.CreatePerspectiveFieldOfView(fovY, aspect, _settings.NearPlane, far);
    }

    private static Matrix4x4 Inverse(Matrix4x4 matrix)
        => Matrix4x4.Invert(matrix, out var inverse) ? inverse : throw new InvalidOperationException("The view-projection matrix is singular.");

    // ------------------------------------------------------------------ IViewProjection

    public void GetScreenRay(Vector2 screen, out Vector3 origin, out Vector3 direction)
        => (origin, direction) = Ray(InverseViewProjection, screen);

    private (Vector3 Origin, Vector3 Direction) Ray(Matrix4x4 inverse, Vector2 screen)
    {
        var ndcX = 2f * screen.X / _viewport.X - 1f;
        var ndcY = 1f - 2f * screen.Y / _viewport.Y;
        var near = Unproject(inverse, new Vector3(ndcX, ndcY, 0f));
        var far = Unproject(inverse, new Vector3(ndcX, ndcY, 1f));
        return (near, Vector3.Normalize(far - near));
    }

    private static Vector3 Unproject(Matrix4x4 inverse, Vector3 ndc)
    {
        var point = Vector4.Transform(new Vector4(ndc, 1f), inverse);
        return new Vector3(point.X, point.Y, point.Z) / point.W;
    }

    /// <summary>
    /// Ray-terrain picking: marches the screen ray in quarter-cell steps through the height range of the map and refines the first crossing
    /// below the surface by bisection on <see cref="TerrainHeightSampler.HeightAt"/>. False when the ray leaves the height field without a hit.
    /// </summary>
    public bool TryScreenToGround(Vector2 screen, out Vector3 world)
    {
        GetScreenRay(screen, out var origin, out var direction);
        return TryIntersectTerrain(_terrain, origin, direction, 2f * FarPlane, out world);
    }

    public static bool TryIntersectTerrain(TerrainHeightSampler terrain, Vector3 origin, Vector3 direction, float maxDistance, out Vector3 world)
    {
        world = default;
        const float step = TerrainHeightSampler.CellSize / 4f;
        var start = 0f;
        var end = maxDistance;
        if (direction.Z < 0)
        {
            start = Math.Max(0f, (terrain.MaxHeight - origin.Z) / direction.Z);
            end = Math.Min(end, (terrain.MinHeight - origin.Z) / direction.Z + step);
        }
        else if (origin.Z > terrain.MaxHeight)
        {
            return false;
        }
        var previous = start;
        if (Above(terrain, origin + direction * start) <= 0) return Hit(terrain, origin + direction * start, out world);
        for (var t = start + step; t <= end + step; t += step)
        {
            var point = origin + direction * t;
            if (Above(terrain, point) <= 0)
            {
                var lo = previous;
                var hi = t;
                for (var iteration = 0; iteration < 24; iteration++)
                {
                    var mid = (lo + hi) * 0.5f;
                    if (Above(terrain, origin + direction * mid) > 0) lo = mid; else hi = mid;
                }
                return Hit(terrain, origin + direction * hi, out world);
            }
            previous = t;
        }
        return false;

        static float Above(TerrainHeightSampler terrain, Vector3 point) => point.Z - terrain.HeightAt(point.X, point.Y);

        static bool Hit(TerrainHeightSampler terrain, Vector3 point, out Vector3 world)
        {
            world = new Vector3(point.X, point.Y, terrain.HeightAt(point.X, point.Y));
            return terrain.IsInsideField(point.X, point.Y);
        }
    }

    public bool TryWorldToScreen(Vector3 world, out Vector2 screen)
    {
        var clip = Vector4.Transform(new Vector4(world, 1f), ViewProjection);
        if (clip.W <= 1e-6f)
        {
            screen = default;
            return false;
        }
        screen = new Vector2((clip.X / clip.W + 1f) * 0.5f * _viewport.X, (1f - clip.Y / clip.W) * 0.5f * _viewport.Y);
        return true;
    }

    /// <summary>
    /// The view's footprint on the ground plane at <see cref="GroundLevel"/> (<c>View::getScreenCornerWorldPointsAtZ</c>): top-left, top-right,
    /// bottom-right, bottom-left. Corners whose ray does not reach the plane are placed at the far plane distance.
    /// </summary>
    public Vector2[] GroundFootprint()
    {
        var corners = new[] { Vector2.Zero, new Vector2(_viewport.X, 0), _viewport, new Vector2(0, _viewport.Y) };
        var result = new Vector2[4];
        for (var i = 0; i < 4; i++)
        {
            GetScreenRay(corners[i], out var origin, out var direction);
            var t = direction.Z < -1e-6f ? (GroundLevel - origin.Z) / direction.Z : FarPlane;
            t = Math.Min(t, FarPlane);
            var point = origin + direction * t;
            result[i] = new Vector2(point.X, point.Y);
        }
        return result;
    }

    /// <summary>Indices of the chunks whose bounds intersect the view frustum, in chunk order.</summary>
    public List<int> VisibleChunks(TerrainRenderModel model)
    {
        var frustum = Frustum;
        var visible = new List<int>();
        for (var index = 0; index < model.Chunks.Length; index++)
            if (frustum.Intersects(model.Chunks[index].Bounds)) visible.Add(index);
        return visible;
    }

    private static float DegreesToRadians(float degrees) => degrees * (MathF.PI / 180f);
}
