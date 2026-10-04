using System.Numerics;
using CnCNew.Presentation.Camera;
using CnCNew.Presentation.Terrain;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

internal static class CameraTests
{
    /// <summary>The retail GameData.ini values, written out so the synthetic tests do not need the original data.</summary>
    public static RtsCameraSettings Settings { get; } = new(37.5f, 0f, 232f, 310f, 120f, 0.3f, 50f, false, 1.6f, 2.0f, 2.0f, 0.1f, 10f, 150f);

    private static readonly Vector2 Viewport = new(1024, 768);

    public static int Run(string[] args)
    {
        var t = new TestHarness("Camera");
        t.Run("default placement follows W3DView::buildCameraTransform", DefaultPlacement);
        t.Run("matrices and projection round trip", MatrixRoundTrip);
        t.Run("screen to ground to screen on flat and hilly terrain", PickingRoundTrip);
        t.Run("rays that miss the map", PickingMisses);
        t.Run("frustum culling of chunks", Culling);
        t.Run("map bounds constraints", Constraints);
        t.Run("scrolling directions and speeds", Scrolling);
        t.Run("rotation, pitch and zoom limits", RotationPitchZoom);
        t.Run("zoom follows the terrain height", TerrainFollowing);
        t.Run("camera shake spring", Shake);
        t.Run("ground footprint", Footprint);
        return t.Failures;
    }

    public static TerrainHeightSampler Flat(int cells = 200, byte height = 0)
        => new(SyntheticTerrain.Map(cells + 1, cells + 1, 0, (_, _) => height).HeightField);

    public static TerrainHeightSampler Hills()
        => new(SyntheticTerrain.Map(161, 141, 0, (x, y) => (byte)(60 + 50 * MathF.Sin(x * 0.15f) * MathF.Cos(y * 0.11f))).HeightField);

    private static void DefaultPlacement()
    {
        var camera = new RtsCamera(Settings, Flat(), Viewport);
        Assert(camera.Pivot == new Vector2(1000, 1000) && camera.GroundLevel == 0, "starts in the map centre on the ground");
        Near(310f / 232f, camera.Zoom, 1e-5, "default zoom = (ground + MaxCameraHeight) / CameraHeight");
        var expectedEye = new Vector3(1000, 1000 - 310f / MathF.Tan(37.5f * MathF.PI / 180f), 310);
        Assert(Vector3.Distance(camera.Eye, expectedEye) < 1e-2f, $"eye {camera.Eye} expected {expectedEye}");
        Assert(camera.Target == new Vector3(1000, 1000, 0), "target on the ground level");
        Assert(camera.TryScreenToGround(Viewport / 2, out var center) && Vector2.Distance(new Vector2(center.X, center.Y), camera.Pivot) < 0.05f, "screen centre looks at the pivot");

        var raised = new RtsCamera(Settings, Flat(height: 80), Viewport);
        Assert(raised.GroundLevel == 50 && raised.OffsetHeight == 282, "ground level 80 * 0.625 = 50, offset height 50 + 232");
        Near(310, raised.Eye.Z - 50, 1e-2, "camera stays MaxCameraHeight above the ground");
        var high = new RtsCamera(Settings, Flat(height: 250), Viewport);
        Assert(high.GroundLevel == 120, "ground level is capped at 120");
    }

    private static void MatrixRoundTrip()
    {
        IViewProjection view = new RtsCamera(Settings, Flat(), Viewport);
        var camera = (RtsCamera)view;
        Assert(view.ViewportSize == Viewport, "viewport");
        Assert(view.ViewProjection == camera.View * camera.Projection, "view-projection = view * projection");
        var product = camera.ViewProjection * camera.InverseViewProjection;
        Assert(Vector4.Distance(new Vector4(product.M11, product.M22, product.M33, product.M44), Vector4.One) < 1e-3f, "inverse");
        Assert(view.TryWorldToScreen(camera.Target, out var screen) && Vector2.Distance(screen, Viewport / 2) < 0.01f, "target projects to the centre");
        Assert(!view.TryWorldToScreen(camera.Eye - new Vector3(0, 50, 0), out _), "points behind the camera do not project");
        var fovY = 2 * MathF.Atan(MathF.Tan(25 * MathF.PI / 180) / (1024f / 768f));
        Near(1 / MathF.Tan(fovY / 2), camera.Projection.M22, 1e-4, "vertical fov from 50 degrees horizontal");
        view.GetScreenRay(new Vector2(0, 0), out var origin, out var direction);
        Near(1, direction.Length(), 1e-5, "normalized ray");
        Assert(Vector3.Distance(origin, camera.Eye) is > 9.9f and < 12f, "ray starts on the near plane in front of the eye");
    }

    private static void PickingRoundTrip()
    {
        foreach (var (name, terrain) in new[] { ("flat", Flat()), ("hills", Hills()) })
        {
            var camera = new RtsCamera(Settings, terrain, Viewport);
            camera.CenterOn(new Vector2(700, 600));
            camera.SetAngle(0.7f);
            camera.SettleZoom();
            var hits = 0;
            for (var y = 20; y < 768; y += 97)
                for (var x = 10; x < 1024; x += 131)
                {
                    if (!camera.TryScreenToGround(new Vector2(x, y), out var world)) continue;
                    hits++;
                    Near(terrain.HeightAt(world.X, world.Y), world.Z, 1e-3, $"{name}: hit lies on the terrain");
                    Assert(camera.TryWorldToScreen(world, out var back), $"{name}: hit projects back");
                    Assert(Vector2.Distance(back, new Vector2(x, y)) < 0.5f, $"{name}: ({x},{y}) -> {world} -> {back}");
                }
            Assert(hits >= 40, $"{name}: most samples hit the terrain ({hits})");
        }
    }

    private static void PickingMisses()
    {
        var terrain = Flat(20);
        Assert(!RtsCamera.TryIntersectTerrain(terrain, new Vector3(100, 100, 50), Vector3.UnitZ, 1000, out _), "upwards ray above the terrain misses");
        Assert(!RtsCamera.TryIntersectTerrain(terrain, new Vector3(-500, 100, 50), Vector3.Normalize(new Vector3(-1, 0, -0.1f)), 5000, out _), "hit outside the height field is rejected");
        Assert(RtsCamera.TryIntersectTerrain(terrain, new Vector3(100, 100, 50), -Vector3.UnitZ, 1000, out var down) && down == new Vector3(100, 100, 0), "straight down");
    }

    private static void Culling()
    {
        var map = SyntheticTerrain.Map(401, 401, 0, (x, y) => (byte)((x + y) % 40));
        var model = TerrainRenderModel.Build(map, SyntheticTerrain.Lookup(), SyntheticTerrain.Options());
        var camera = new RtsCamera(Settings, new TerrainHeightSampler(map.HeightField), Viewport);
        camera.CenterOn(new Vector2(2000, 2000));
        var visible = camera.VisibleChunks(model);
        Assert(visible.Count > 0 && visible.Count < model.Chunks.Length / 2, $"only part of 169 chunks visible ({visible.Count})");
        var underPivot = model.ChunkAt(2000 / 320, 2000 / 320);
        Assert(camera.Frustum.Intersects(underPivot.Bounds), "chunk under the pivot is visible");
        var behind = model.ChunkAt(6, 1);
        Assert(!camera.Frustum.Intersects(behind.Bounds), "chunk far behind the camera is culled");
        Assert(camera.Frustum.Contains(camera.Target) && !camera.Frustum.Contains(camera.Eye - new Vector3(0, 0, 50)), "point containment");
        foreach (var index in visible) Assert(camera.Frustum.Intersects(model.Chunks[index].Bounds), "visible list agrees with the frustum");
    }

    private static void Constraints()
    {
        var camera = new RtsCamera(Settings, Flat(), Viewport);
        var (lo, hi) = camera.Constraints();
        Assert(lo.X > 50 && lo.X == lo.Y && hi.X == 2000 - lo.X, $"margin from the centre-to-95% ray distance ({lo}, {hi})");
        camera.CenterOn(new Vector2(-300, 5000));
        Assert(Vector2.Distance(camera.Pivot, new Vector2(lo.X, hi.Y)) < 1e-2f, $"pivot clamped into the map ({camera.Pivot} vs {lo.X}, {hi.Y})");
        var tiny = new RtsCamera(Settings, Flat(8), Viewport);
        Assert(tiny.Pivot == new Vector2(40, 40), "a map smaller than the margin keeps the pivot centred");
    }

    private static void Scrolling()
    {
        var camera = new RtsCamera(Settings, Flat(), Viewport);
        var start = camera.Pivot;
        var delta = camera.KeyboardScrollDelta(up: false, down: false, left: false, right: true, dtSeconds: 1f / 30f);
        Assert(Vector2.Distance(delta, new Vector2(1.6f * 100 * 2, 0)) < 1e-3f, "one frame of right key = HorizontalScrollSpeedFactor * SCROLL_AMT * factor");
        camera.ScrollBy(delta);
        var moved = camera.Pivot - start;
        // 320 * 250 / 1024 * 2 * tan(25 deg) world units along the camera's right axis.
        Near(320 * 250 / 1024f * 2 * MathF.Tan(25 * MathF.PI / 180), moved.X, 1e-2, "right scroll distance");
        Near(0, moved.Y, 1e-3, "right scroll keeps y");
        Assert(camera.LastScrollAmount == delta, "scroll amount remembered");
        start = camera.Pivot;
        camera.ScrollBy(camera.KeyboardScrollDelta(true, false, false, false, 1f / 30f));
        Assert(camera.Pivot.Y > start.Y && MathF.Abs(camera.Pivot.X - start.X) < 1e-3f, "up key scrolls north at yaw 0");
        camera.SetAngle(MathF.PI / 2);
        start = camera.Pivot;
        camera.ScrollBy(camera.KeyboardScrollDelta(false, false, false, true, 1f / 30f));
        Assert(camera.Pivot.Y > start.Y + 10 && MathF.Abs(camera.Pivot.X - start.X) < 1e-2f, "right scroll follows the rotated view");
        // The input layer's amount (CameraIntent.Scroll: y forward, factors applied, per second) moves the camera exactly like the reference formula for the same key and time.
        var byKey = new RtsCamera(Settings, Flat(), Viewport);
        var byIntent = new RtsCamera(Settings, Flat(), Viewport);
        var origin = byIntent.Pivot;
        byKey.ScrollBy(byKey.KeyboardScrollDelta(true, false, false, true, 1f / 30f));
        byIntent.ScrollByIntent(new Vector2(1.6f * 2.0f, 2.0f * 2.0f) / 30f);
        Assert(Vector2.Distance(byKey.Pivot, byIntent.Pivot) < 1e-2f && Vector2.Distance(byIntent.Pivot, origin) > 5f, $"an input-layer scroll of one frame equals one frame of the up and right keys ({byKey.Pivot} vs {byIntent.Pivot})");
        Assert(camera.EdgeScrollDelta(new Vector2(1, 400), 1f / 30f).X < 0 && camera.EdgeScrollDelta(new Vector2(500, 400), 1f / 30f) == Vector2.Zero, "edge scrolling");
        Assert(Vector2.Distance(camera.RightMouseScrollDelta(new Vector2(100, 100), new Vector2(110, 90), 1f / 30f), new Vector2(16, -20)) < 1e-3f, "right mouse drag");
        Assert(Vector2.Distance(camera.KeyboardScrollDelta(false, false, true, false, 0.1f), new Vector2(-960, 0)) < 1e-2f, "speed scales with elapsed frames");
    }

    private static void RotationPitchZoom()
    {
        var camera = new RtsCamera(Settings, Flat(), Viewport);
        camera.RotateByKeyboard(1, 1f);
        Near(3f, camera.Angle, 1e-4, "0.1 rad per frame for one second");
        camera.RotateBy(1f);
        Near(4f - 2 * MathF.PI, camera.Angle, 1e-4, "angles wrap to [-pi, pi]");
        Near(0.5f, camera.MouseRotateAngle(0.2f, 100, 130), 1e-6, "0.01 rad per pixel");
        camera.SetAngle(MathF.PI / 2);
        var offset = camera.Eye - camera.Target;
        Assert(offset.X > 100 && MathF.Abs(offset.Y) < 1e-2f, $"rotated a quarter turn counter-clockwise the camera sits east of the pivot ({offset})");
        camera.SetPitch(2f);
        Near(89.9f * MathF.PI / 180, camera.Pitch, 1e-5, "pitch clamps to 89.9 degrees");
        camera.SetPitchToDefault();
        camera.SetAngle(0);
        var before = camera.Eye.Z;
        camera.SetPitch(60 * MathF.PI / 180);
        Assert(camera.Eye.Z > before, "steeper pitch raises the camera");
        camera.SetPitchToDefault();
        camera.ZoomByWheel(100);
        Assert(camera.HeightAboveGround == 120, "zoom in clamps to MinCameraHeight");
        camera.ZoomByWheel(-3);
        Assert(camera.HeightAboveGround == 150, "three notches out = 30 units");
        camera.ZoomByWheel(-100);
        Assert(camera.HeightAboveGround == 310, "zoom out clamps to MaxCameraHeight");
    }

    private static void TerrainFollowing()
    {
        // A plateau of height 100 * 0.625 = 62.5 in the east half.
        var terrain = new TerrainHeightSampler(SyntheticTerrain.Map(201, 201, 0, (x, _) => (byte)(x > 100 ? 100 : 0)).HeightField);
        var camera = new RtsCamera(Settings, terrain, Viewport);
        Assert(camera.GroundLevel == 0, "west half on the ground");
        camera.CenterOn(new Vector2(1500, 1000));
        var zoom = camera.Zoom;
        camera.Update(1f / 30f, isScrolling: false);
        var desired = (62.5f + 310f) / 232f;
        Near(zoom + (desired - zoom) * 0.3f, camera.Zoom, 1e-4, "one frame moves 30 % towards the desired zoom");
        for (var i = 0; i < 120; i++) camera.Update(1f / 30f, false);
        Near(desired, camera.Zoom, 1e-3, "settles on the plateau height");
        Near(62.5f + 310f, camera.Eye.Z, 0.05, "camera height above the plateau");
        camera.CenterOn(new Vector2(600, 1000));
        camera.ScrollBy(new Vector2(200, 0));
        var fast = camera.Zoom;
        camera.Update(1f / 30f, isScrolling: true);
        Assert(camera.Zoom == fast, "no height change while scrolling fast within the limits");
        Near(camera.HeightAroundPivot(), terrain.HeightAt(camera.Pivot.X, camera.Pivot.Y), 70, "height around pivot samples the neighbourhood");
    }

    private static void Shake()
    {
        var camera = new RtsCamera(Settings, Flat(), Viewport);
        camera.Shake(camera.Pivot + new Vector2(75, 0), 2f, 0f);
        camera.Update(1f / 30f, false);
        Assert(Vector2.Distance(camera.ShakeOffset, new Vector2(1, 0)) < 1e-5f, "intensity 2 at half range = 1 along the angle");
        camera.Update(1f / 30f, false);
        Assert(Vector2.Distance(camera.ShakeOffset, new Vector2(-0.75f, 0)) < 1e-5f, "damped by 0.75 and reversed");
        for (var i = 0; i < 40; i++) camera.Update(1f / 30f, false);
        Assert(camera.ShakeOffset == Vector2.Zero, "shake dies out");
        camera.Shake(camera.Pivot + new Vector2(500, 0), 5f, 0f);
        camera.Update(1f / 30f, false);
        Assert(camera.ShakeOffset == Vector2.Zero, "beyond MaxShakeRange nothing happens");
        camera.Shake(camera.Pivot, 11f, 0f);
        camera.Update(1f / 30f, false);
        Assert(camera.ShakeOffset == new Vector2(3, 0), "above MaxShakeIntensity the reference resets to 3");
        var eye = camera.Eye;
        camera.AddShakeOffset(new Vector2(0, 5));
        Near(eye.Y + 5, camera.Eye.Y, 1e-3, "additive offset moves the view");
    }

    private static void Footprint()
    {
        var camera = new RtsCamera(Settings, Flat(), Viewport);
        var corners = camera.GroundFootprint();
        Assert(corners.Length == 4, "four corners");
        Assert(corners[0].Y > corners[3].Y && corners[1].X > corners[0].X, "top edge is further north, right corner east");
        Assert(corners[0].X - corners[1].X < corners[3].X - corners[2].X, "perspective: the far edge is wider");
        foreach (var (corner, screen) in corners.Zip(new[] { Vector2.Zero, new Vector2(1024, 0), Viewport, new Vector2(0, 768) }))
            Assert(camera.TryWorldToScreen(new Vector3(corner, 0), out var back) && Vector2.Distance(back, screen) < 0.5f, $"corner {screen}");
    }
}
