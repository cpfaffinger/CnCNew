using System.Numerics;
using CnCNew.Presentation.Camera;
using CnCNew.Presentation.Terrain;
using static CnCNew.Presentation.Tests.TestHarness;

namespace CnCNew.Presentation.Tests;

/// <summary>The scripted camera against the numbers of the original's formulas (<c>W3DView.cpp</c>), written out here, not read from the code under test.</summary>
internal static class ScriptedCameraTests
{
    private const float Frame = 1f / 30f;
    private const float Low = 15.625f;
    private const float High = 100f;
    private static readonly Vector2 Viewport = new(1280, 720);
    private static readonly Vector2 Start = new(500, 500);
    private static readonly Vector2 Far = new(2500, 500);
    private static readonly RtsCameraSettings Settings = CameraTests.Settings;

    public static int Run(string[] args)
    {
        var t = new TestHarness("ScriptedCamera");
        t.Run("SETUP_CAMERA is moveCameraTo, cameraModLookToward, cameraModFinalPitch and cameraModFinalZoom", SetupCamera);
        t.Run("a low shot below the ground level looks down with a negative pitch", LowShotLooksDown);
        t.Run("the ground level goes with the move, the camera's offset stays", GroundLevelGoesWithTheMove);
        t.Run("the zoom of a script is m_zoom, its final zoom a share of the largest", ZoomOfAScript);
        t.Run("the ease of a move is the script's", EaseOfAMove);
        t.Run("the zoom does not settle against a script and keeps its height after it", ZoomKeepsItsHeight);
        t.Run("what a player does ends what a script does, as W3DView's setters do", PlayerEndsTheScript);
        t.Run("a script may go where a player may not, until a player zooms", ConstraintsWiden);
        t.Run("a rotation, a turn to a place and a turn to an object", Rotations);
        t.Run("RESET_CAMERA comes back to the default view", Reset);
        t.Run("the look of a path, its last look, a frozen angle and a rolling average", PathMods);
        t.Run("a lock follows, tethers, snaps and takes up the object's height", Lock);
        return t.Failures;
    }

    /// <summary>Low ground in the west, a plateau in the east.</summary>
    private static RtsCamera Camera()
    {
        var map = SyntheticTerrain.Map(401, 401, 0, (x, _) => (byte)(x < 150 ? Low / TerrainHeightSampler.HeightScale : High / TerrainHeightSampler.HeightScale));
        var camera = new RtsCamera(Settings, new TerrainHeightSampler(map.HeightField), Viewport);
        camera.InitializeForMap(Start);
        return camera;
    }

    /// <summary><c>W3DView::buildCameraTransform</c> for a view at the default pitch: the offset scaled by the zoom, the factor of the ground level, the angle, then <c>m_FXPitch</c>.</summary>
    private static (Vector3 Eye, Vector3 Target) OriginalPose(Vector2 position, float groundLevel, float cameraOffsetZ, float zoom, float angle, float fxPitch)
    {
        var offsetY = -(cameraOffsetZ / MathF.Tan(37.5f * MathF.PI / 180f));
        var source = new Vector3(0f, offsetY * zoom, cameraOffsetZ * zoom);
        var factor = 1f - groundLevel / source.Z;
        var (sin, cos) = MathF.SinCos(angle);
        source = new Vector3(source.X * cos - source.Y * sin, source.X * sin + source.Y * cos, source.Z) * factor;
        var eye = source + new Vector3(position, groundLevel);
        var target = new Vector3(position, groundLevel);
        if (fxPitch <= 1f) target.Z = eye.Z - (eye.Z - target.Z) * fxPitch;
        else eye = new Vector3(target.X + (eye.X - target.X) / fxPitch, target.Y + (eye.Y - target.Y) / fxPitch, eye.Z);
        return (eye, target);
    }

    private static void NearPose((Vector3 Eye, Vector3 Target) expected, RtsCamera camera, string what)
    {
        Assert(Vector3.Distance(expected.Eye, camera.Eye) < 0.05f, $"{what}: the eye stands at {expected.Eye} as the formula says ({camera.Eye})");
        Assert(Vector3.Distance(expected.Target, camera.Target) < 0.05f, $"{what}: it looks at {expected.Target} as the formula says ({camera.Target})");
    }

    private static void Setup(ScriptedCamera script, Vector2 place, float zoom, float pitch, Vector2 lookAt)
    {
        script.MoveTo(place, 0f);
        script.ModLookToward(lookAt);
        script.ModFinalPitch(pitch);
        script.ModFinalZoom(zoom);
    }

    private static void SetupCamera()
    {
        var camera = Camera();
        Near(Low + 232f, camera.OffsetHeight, 1e-3, "m_cameraOffset.z is the ground level at the start plus CameraHeight");
        var place = new Vector2(600, 600);
        var lookAt = new Vector2(700, 600);
        Setup(camera.Script, place, 1.2f, 2f, lookAt);
        Assert(camera.Pivot == Start, "nothing happens before the view's next frame");
        camera.Script.Step(1f / 60f);
        Assert(!camera.Script.IsMoving, "a cut is over with the first frame, however short");
        Assert(camera.Pivot == place, $"the cut reaches the waypoint ({camera.Pivot})");
        // cameraModLookToward: acos(1) - PI/2, a look to the east, whatever way the camera came.
        Near(-MathF.PI / 2f, camera.Angle, 1e-3, "the angle looks at the second waypoint");
        var zoom = 1.2f * (Low + 310f) / camera.OffsetHeight;
        Near(zoom, camera.Zoom, 1e-3, "the zoom is a share of getMaxZoom at the waypoint");
        Near(2f, camera.FxPitch, 1e-5, "the pitch is m_FXPitch");
        NearPose(OriginalPose(place, Low, camera.OffsetHeight, zoom, -MathF.PI / 2f, 2f), camera, "above 1 the camera comes nearer over the ground");
    }

    private static void LowShotLooksDown()
    {
        var camera = Camera();
        var place = new Vector2(2500, 2000);
        Setup(camera.Script, place, 0.2f, -0.45f, new Vector2(2600, 2000));
        camera.Script.Step(Frame);
        Near(High, camera.GroundLevel, 1e-3, "m_groundLevel is the ground at the waypoint");
        var zoom = 0.2f * (High + 310f) / camera.OffsetHeight;
        Near(zoom, camera.Zoom, 1e-3, "getMaxZoom takes the terrain of the waypoint");
        NearPose(OriginalPose(place, High, camera.OffsetHeight, zoom, -MathF.PI / 2f, -0.45f), camera, "the shot");
        Near(82f, camera.Eye.Z, 0.01, "the height is 0.2 of terrain plus 310, below the ground level");
        Assert(camera.Eye.Z > camera.Target.Z, $"so the factor turns negative and the negative pitch looks down ({camera.Eye.Z} over {camera.Target.Z})");
    }

    private static void GroundLevelGoesWithTheMove()
    {
        var camera = Camera();
        var offset = camera.OffsetHeight;
        camera.Script.MoveTo(Far, 10f);
        for (var frame = 0; frame < 150; frame++) camera.Script.Step(Frame);
        Near(1500f, camera.Pivot.X, 2f, "half the time is half the way");
        Near((Low + High) / 2f, camera.GroundLevel, 0.5, "and half the ground level, in proportion to the distance");
        Near(offset, camera.OffsetHeight, 1e-5, "the offset is not touched (the original's lines are commented out)");
        for (var frame = 0; frame < 160; frame++) camera.Script.Step(Frame);
        Assert(!camera.Script.IsMoving && camera.Pivot == Far, $"the move ends at the waypoint ({camera.Pivot})");
        Near(High, camera.GroundLevel, 1e-3, "on its ground");
    }

    private static void ZoomOfAScript()
    {
        var camera = Camera();
        camera.Script.Zoom(0.6f, 2f);
        for (var frame = 0; frame < 30; frame++) camera.Script.Step(Frame);
        var before = (Low + 310f) / camera.OffsetHeight;
        Near((before + 0.6f) / 2f, camera.Zoom, 1e-3, "half way in half its time");
        for (var frame = 0; frame < 30; frame++) camera.Script.Step(Frame);
        Near(0.6f, camera.Zoom, 1e-5, "ZOOM_CAMERA goes to that very m_zoom");

        camera.Script.MoveTo(Far, 6f);
        camera.Script.ModFinalZoom(0.83f);
        camera.Script.ModFinalPitch(0.925f);
        for (var frame = 0; frame < 90; frame++) camera.Script.Step(Frame);
        var final = 0.83f * (High + 310f) / camera.OffsetHeight;
        Near((0.6f + final) / 2f, camera.Zoom, 0.01, "the mods run with the move");
        Near((1f + 0.925f) / 2f, camera.FxPitch, 0.01, "both of them");
        for (var frame = 0; frame < 95; frame++) camera.Script.Step(Frame);
        Near(final, camera.Zoom, 1e-3, "and end with it, at the zoom the end of the move allows");
        Near(0.925f, camera.FxPitch, 1e-5, "and the pitch");
    }

    private static void EaseOfAMove()
    {
        var camera = Camera();
        camera.Script.Zoom(2f, 10f, 2f, 2f);
        var start = camera.Zoom;
        for (var frame = 0; frame < 30; frame++) camera.Script.Step(Frame);
        // ParabolicEase with in 0.2 and out 0.8: v0 = 1.6, t = 0.1 gives t * t / (v0 * in).
        Near(start + (2f - start) * (0.01f / (1.6f * 0.2f)), camera.Zoom, 1e-3, "the start of a ramp is the parabola of its ease-in");
    }

    private static void ZoomKeepsItsHeight()
    {
        var camera = Camera();
        Setup(camera.Script, new Vector2(600, 600), 0.1f, 1f, new Vector2(700, 600));
        camera.Script.Step(Frame);
        camera.Update(Frame, false);
        var zoom = 0.1f * (Low + 310f) / camera.OffsetHeight;
        Near(zoom, camera.Zoom, 1e-4, "the frame that ends a script does not settle the zoom");
        Near(camera.OffsetHeight * zoom - Low, camera.HeightAboveGround, 1e-3, "m_heightAboveGround takes what the script reached, below every player's limit");
        for (var frame = 0; frame < 60; frame++) camera.Update(Frame, false);
        Near(zoom, camera.Zoom, 1e-4, "the camera stays there afterwards");
    }

    private static void PlayerEndsTheScript()
    {
        var camera = Camera();
        camera.Script.Rotate(1f, 10f);
        camera.Script.Zoom(1f, 10f);
        camera.ScrollBy(new Vector2(0.01f, 0f));
        Assert(camera.Script.IsMoving, "W3DView::scrollBy ends a rotation only");
        var angle = camera.Angle;
        camera.Script.Step(Frame);
        Assert(camera.Angle == angle, "and the rotation is gone");

        camera.Script.MoveTo(Far, 10f);
        camera.CenterOn(Start);
        camera.Script.Step(Frame);
        Assert(camera.Pivot == Start, "lookAt ends a move");
        Assert(camera.Script.IsMoving, "but not the zoom");

        camera.SetAngle(0.5f);
        Assert(!camera.Script.IsMoving, "setAngle ends everything");

        camera.Script.Pitch(0.5f, 1f);
        camera.SetHeightAboveGround(200f);
        Assert(!camera.Script.IsMoving, "and so does a player's zoom");
        camera.SetFxPitch(0.5f);
        camera.SetPitchToDefault();
        Near(1f, camera.FxPitch, 1e-6, "setAngleAndPitchToDefault ends a script's pitch");
    }

    private static void ConstraintsWiden()
    {
        var camera = Camera();
        var edge = new Vector2(5, 500);
        camera.CenterOn(edge);
        var playable = camera.Pivot.X;
        Assert(playable > 100f, $"a player stops short of the edge ({playable})");
        camera.Script.MoveTo(edge, 0f);
        camera.Script.Step(Frame);
        Assert(camera.Pivot == edge, $"a script's move goes all the way ({camera.Pivot})");
        camera.CenterOn(new Vector2(4, 500));
        Assert(camera.Pivot == edge, "and the player may then go as far as the script went (m_cameraConstraint.lo.x = minf(.., pos.x))");
        camera.SetHeightAboveGround(310f);
        Near(playable, camera.Pivot.X, 2f, "until a player's zoom makes the original work the constraints out again");
    }

    private static void Rotations()
    {
        var camera = Camera();
        camera.Script.Rotate(0.25f, 3f);
        for (var frame = 0; frame < 45; frame++) camera.Script.Step(Frame);
        Near(MathF.PI / 4f, camera.Angle, 1e-3, "half a quarter turn in half the time");
        for (var frame = 0; frame < 45; frame++) camera.Script.Step(Frame);
        Near(MathF.PI / 2f, camera.Angle, 1e-3, "and a quarter turn at its end");
        Assert(!camera.Script.IsMoving, "over");

        camera.Script.RotateToward(camera.Pivot + new Vector2(0, 100), 1f);
        for (var frame = 0; frame < 31; frame++) camera.Script.Step(Frame);
        Near(0f, camera.Angle, 1e-3, "north is angle 0, the default camera");
        Assert(!camera.Script.IsMoving, "over again");

        var place = camera.Pivot + new Vector2(100, 0);
        camera.Script.RotateTowardObject(() => place, 1f, 1f);
        for (var frame = 0; frame < 30; frame++) camera.Script.Step(Frame);
        Near(-MathF.PI / 2f, camera.Angle, 0.01, "the turn to an object arrives at it");
        Assert(camera.Script.IsMoving, "and holds");
        place = camera.Pivot + new Vector2(0, 100);
        for (var frame = 0; frame < 30; frame++) camera.Script.Step(Frame);
        Near(0f, camera.Angle, 1e-3, "following the object");
        Assert(!camera.Script.IsMoving, "until the hold is over");
    }

    private static void Reset()
    {
        var camera = Camera();
        camera.SetAngle(1f);
        camera.Script.Pitch(0.5f, 0f);
        camera.Script.Zoom(0.3f, 0f);
        camera.Script.Step(Frame);
        camera.Script.Reset(Far, 2f);
        for (var frame = 0; frame < 62; frame++) camera.Script.Step(Frame);
        Assert(camera.Pivot == Far, "the reset ends at the place");
        Near(0f, camera.Angle, 1e-3, "at the default angle");
        Near(1f, camera.FxPitch, 1e-5, "the default pitch");
        Near((High + 310f) / camera.OffsetHeight, camera.Zoom, 1e-3, "and the largest zoom there");
    }

    private static void PathMods()
    {
        var start = new Vector2(1000, 1000);
        Vector2[] chain = [new(1000, 1500), new(1500, 1500)];
        var flight = new CameraPath(start, 0f, chain, 10f, 0f, 0f, 0f, 10f, 20f);
        var place = new Vector2(1000, 2000);
        flight.LookToward(place);
        while (!flight.IsFinished) flight.Advance(Frame);
        // cameraModLookToward: from the curve's point at the last waypoint, which is the waypoint, because the path is padded straight on.
        Near(CameraPath.HeadingOf(place - chain[^1]), flight.Angle, 1e-3, "the last angle looks at the place");
        Near(20f, flight.GroundLevel, 1e-4, "and the ground level is the last one");

        var frozen = new CameraPath(start, 0.3f, chain, 2f, 0f, 0f, 0f);
        frozen.FreezeAngle();
        while (!frozen.IsFinished)
        {
            frozen.Advance(Frame);
            Near(0.3f, frozen.Angle, 1e-3, "a frozen angle stays");
        }

        var moved = new CameraPath(start, 0f, chain, 2f, 0f, 0f, 0f);
        moved.FinalMoveTo(new Vector2(1500, 1600));
        while (!moved.IsFinished) moved.Advance(Frame);
        Assert(moved.Position == new Vector2(1500, 1600), $"cameraModFinalMoveTo moves the end ({moved.Position})");

        var last = new CameraPath(start, 0f, chain, 2f, 0f, 0f, 0f);
        last.FinalLookToward(new Vector2(1500, 2000));
        while (!last.IsFinished) last.Advance(Frame);
        Near(0f, last.Angle, 1e-3, "the final look: north of the last waypoint");

        var smooth = new CameraPath(start, 1f, chain, 20f, 0f, 0f, 0f);
        var sharp = new CameraPath(start, 1f, chain, 20f, 0f, 0f, 0f);
        smooth.RollingAverage(40);
        for (var frame = 0; frame < 150; frame++)
        {
            smooth.Advance(Frame);
            sharp.Advance(Frame);
        }
        Assert(Math.Abs(smooth.Angle - 1f) > 0.01f && Math.Abs(smooth.Angle - sharp.Angle) > 0.01f, $"a rolling average lags behind the angle it follows ({smooth.Angle} against {sharp.Angle})");
    }

    private static void Lock()
    {
        var camera = Camera();
        var place = new Vector3(600, 500, 33f);
        camera.Script.Lock(() => new CameraLockTarget(place, null), snap: false);
        camera.Script.Step(Frame);
        Near(505f, camera.Pivot.X, 1e-2, "the first frame closes 0.05 of the way");
        Near(33f, camera.GroundLevel, 1e-3, "and m_groundLevel is the object's height");
        camera.Script.Step(Frame);
        Near(505f + 95f * 0.1f, camera.Pivot.X, 1e-2, "the second 0.10 of what is left");
        camera.Script.Unlock();
        camera.Script.Step(Frame);
        Assert(camera.Pivot.X < 515f, "unlocked it stays");

        var snapped = Camera();
        snapped.Script.Lock(() => new CameraLockTarget(place, null), snap: true);
        snapped.Script.Step(1f / 60f);
        Assert(snapped.Pivot == new Vector2(600, 500), "a snap is there at once");

        var flying = Camera();
        flying.Script.Lock(() => new CameraLockTarget(new Vector3(500, 500, 300f), MathF.PI / 2f), snap: true);
        flying.Script.Step(Frame);
        Near(0f, flying.Angle, 1e-4, "the view turns with what flies: its heading less a quarter turn");

        var tether = Camera();
        tether.Script.Lock(() => new CameraLockTarget(new Vector3(680, 500, 20f), null), snap: false, tether: true, play: 50f);
        tether.Script.Step(Frame);
        var distance = 180f;
        var ratio = 1f - 40f * 40f / (distance * distance);
        Near(500f + 180f * ratio * 0.5f, tether.Pivot.X, 1e-2, "outside a partition cell the tether closes half the way past the cell");
        for (var frame = 0; frame < 60; frame++) tether.Script.Step(Frame);
        Near(40f, 680f - tether.Pivot.X, 0.5, "and rests at the edge of the cell");

        var inside = Camera();
        inside.Script.Lock(() => new CameraLockTarget(new Vector3(520, 500, 20f), null), snap: false, tether: true, play: 50f);
        inside.Script.Step(Frame);
        Near(500f + 20f * 0.5f, inside.Pivot.X, 1e-2, "inside it the camera moves by the script's play, 0.01 of it per frame");

        var lost = Camera();
        var alive = true;
        lost.Script.Lock(() => alive ? new CameraLockTarget(place, null) : null, snap: false);
        lost.Script.Step(Frame);
        alive = false;
        lost.Script.Step(Frame);
        Assert(!lost.Script.IsLocked, "an object that is gone ends the lock");
    }
}
