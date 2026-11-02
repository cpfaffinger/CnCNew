using System.Numerics;

namespace CnCNew.Presentation.Camera;

/// <summary>What a camera lock follows: an object's place, and the heading of one with an airborne locomotor (<c>isUsingAirborneLocomotor</c>): the view turns with it while it is above the ground.</summary>
public readonly record struct CameraLockTarget(Vector3 Position, float? AirborneHeading);

/// <summary>
/// The part of <c>W3DView</c> the mission scripts drive: <c>moveCameraTo</c>, <c>moveCameraAlongWaypointPath</c>, <c>zoomCamera</c>, <c>pitchCamera</c>, <c>rotateCamera</c>, the <c>cameraMod*</c>
/// family and the camera lock. Requests arrive in the order the scripts gave them and each acts on what the requests before it left, exactly as the original's calls do: <c>SETUP_CAMERA</c> is
/// <see cref="MoveTo"/>, <see cref="ModLookToward"/>, <see cref="ModFinalPitch"/>, <see cref="ModFinalZoom"/> with no time at all. <see cref="Step"/> is <c>updateCameraMovements</c>; between two
/// requests that lay frames apart the host steps for those frames.
/// <para>
/// A script's zoom is <c>m_zoom</c> itself and its ground level <c>m_groundLevel</c>: the low shots of a cut scene come from a camera whose height falls below the ground level, so that
/// <c>buildCameraTransform</c>'s factor turns negative and a negative pitch looks down again. Clamping either to what a player may do turns those shots into views of the sky.
/// </para>
/// </summary>
public sealed class ScriptedCamera
{
    private const float FramesPerSecond = CnCNew.Data.OriginalFrame.PerSecond;
    private const float FrameMilliseconds = 1000f / FramesPerSecond;

    /// <summary><c>m_zcInfo</c> or <c>m_pcInfo</c>: one value from where it is to where a script wants it, with the original's frame count and ease.</summary>
    private sealed class Ramp(float start, float end, float milliseconds, float easeIn, float easeOut)
    {
        private readonly float _start = start;
        private readonly int _frames = FrameCount(milliseconds);
        private readonly float _easeIn = EaseIn(easeIn, milliseconds);
        private readonly float _easeOut = EaseOut(easeIn, easeOut, milliseconds);
        private float _current;

        public float End { get; } = end;

        public bool IsDone { get; private set; }

        public float Advance(float frames)
        {
            _current += frames;
            if (_frames <= 1 || _current >= _frames)
            {
                IsDone = true;
                return End;
            }
            return _start + (End - _start) * CameraPath.Ease(_current / _frames, _easeIn, _easeOut);
        }
    }

    /// <summary><c>m_rcInfo</c>: a turn between two angles, or towards an object for some frames and on it for the hold.</summary>
    private sealed class Turn
    {
        public float StartAngle, EndAngle, Current, EaseIn, EaseOut;
        public int Frames, HoldFrames;
        public Func<Vector2?>? Tracked;
        public Vector2 TrackedPlace;
        public bool Tracking;
        public float Carry;
    }

    private sealed class LockState(Func<CameraLockTarget?> target, bool tether, float play, bool snap)
    {
        public readonly Func<CameraLockTarget?> Target = target;
        public bool Tether = tether;
        public float Play = play;
        public bool Snap = snap;
        public float FollowFactor = -1f;
        public float Carry;
    }

    private readonly RtsCamera _camera;
    private Ramp? _zoom, _pitch;
    private Turn? _turn;
    private CameraPath? _path;
    private LockState? _lock;
    private bool _movedSinceAsked;

    internal ScriptedCamera(RtsCamera camera) => _camera = camera;

    /// <summary><c>m_doing*</c>: a scripted move runs. <c>isCameraMovementFinished</c> is its opposite; a lock is not a movement that ends.</summary>
    public bool IsMoving => _zoom is not null || _pitch is not null || _turn is not null || _path is not null;

    /// <summary>The camera lock is on.</summary>
    public bool IsLocked => _lock is not null;

    /// <summary><c>didScriptedMovement</c> of <c>W3DView::update</c>: anything scripted moved the camera since the last time this was asked, also what ended in that step.</summary>
    internal bool TakeMoved()
    {
        var moved = _movedSinceAsked || IsMoving || IsLocked;
        _movedSinceAsked = false;
        return moved;
    }

    private static int FrameCount(float milliseconds) => Math.Max(1, (int)(Math.Max(1f, milliseconds) / FrameMilliseconds));

    private static float EaseIn(float easeIn, float milliseconds) => Math.Clamp(easeIn / Math.Max(1f, milliseconds), 0f, 1f);

    private static float EaseOut(float easeIn, float easeOut, float milliseconds) => Math.Max(EaseIn(easeIn, milliseconds), 1f - Math.Clamp(easeOut / Math.Max(1f, milliseconds), 0f, 1f));

    /// <summary><c>W3DView::moveCameraTo</c> (<c>ScriptActions::doMoveCameraTo</c> asks with <paramref name="orient"/>, which turns nothing on the one leg it builds).</summary>
    public void MoveTo(Vector2 destination, float seconds, float easeIn = 0f, float easeOut = 0f, bool orient = true)
    {
        _camera.HoldConstraints();
        _path = CameraPath.To(_camera.Pivot, _camera.Angle, destination, seconds, easeIn, easeOut, _camera.GroundLevel, _camera.GroundHeightAt(destination), orient);
        _turn = null;
    }

    /// <summary><c>W3DView::moveCameraAlongWaypointPath</c>.</summary>
    public void MoveAlong(IReadOnlyList<Vector2> path, float seconds, float stutterSeconds, float easeIn, float easeOut)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Count == 0) return;
        _camera.HoldConstraints();
        var flight = new CameraPath(_camera.Pivot, _camera.Angle, path, seconds, stutterSeconds, easeIn, easeOut, _camera.GroundLevel, _camera.GroundHeightAt(path[^1]));
        _path = flight.IsFinished ? null : flight;
        _turn = null;
    }

    /// <summary><c>W3DView::zoomCamera</c>: to this very <c>m_zoom</c>, not a share of anything.</summary>
    public void Zoom(float zoom, float seconds, float easeIn = 0f, float easeOut = 0f) => ZoomOver(zoom, seconds * 1000f, easeIn * 1000f, easeOut * 1000f);

    private void ZoomOver(float zoom, float milliseconds, float easeIn, float easeOut)
    {
        _camera.HoldConstraints();
        _zoom = new Ramp(_camera.Zoom, zoom, milliseconds, easeIn, easeOut);
    }

    /// <summary><c>W3DView::pitchCamera</c>: <c>m_FXPitch</c>, with no limit.</summary>
    public void Pitch(float pitch, float seconds, float easeIn = 0f, float easeOut = 0f) => PitchOver(pitch, seconds * 1000f, easeIn * 1000f, easeOut * 1000f);

    private void PitchOver(float pitch, float milliseconds, float easeIn, float easeOut)
    {
        _camera.HoldConstraints();
        _pitch = new Ramp(_camera.FxPitch, pitch, milliseconds, easeIn, easeOut);
    }

    private void Begin(Turn turn, float milliseconds, float easeIn, float easeOut)
    {
        _camera.HoldConstraints();
        turn.Frames = FrameCount(milliseconds);
        turn.EaseIn = EaseIn(easeIn * 1000f, milliseconds);
        turn.EaseOut = EaseOut(easeIn * 1000f, easeOut * 1000f, milliseconds);
        _turn = turn;
        _path = null;
    }

    /// <summary><c>W3DView::rotateCamera</c>: this many whole turns about the place looked at.</summary>
    public void Rotate(float rotations, float seconds, float easeIn = 0f, float easeOut = 0f)
        => Begin(new Turn { StartAngle = _camera.Angle, EndAngle = _camera.Angle + 2f * MathF.PI * rotations }, seconds * 1000f, easeIn, easeOut);

    /// <summary><c>W3DView::rotateCameraTowardPosition</c> (<c>ScriptActions::doRotateCameraTowardWaypoint</c>). The original's <c>endAngle</c> is a place on the circle: the turn runs the straight way there, or round the other way with <paramref name="reverse"/>.</summary>
    public void RotateToward(Vector2 place, float seconds, float easeIn = 0f, float easeOut = 0f, bool reverse = false)
    {
        var direction = place - _camera.Pivot;
        if (direction.Length() < 0.1f) return;
        var angle = CameraPath.HeadingOf(direction);
        if (reverse) angle += _camera.Angle < angle ? -2f * MathF.PI : 2f * MathF.PI;
        Begin(new Turn { StartAngle = _camera.Angle, EndAngle = angle }, seconds * 1000f, easeIn, easeOut);
    }

    /// <summary><c>W3DView::rotateCameraTowardObject</c>: follows the object while it turns and for the hold after it; an object that is gone stays where it was last seen.</summary>
    public void RotateTowardObject(Func<Vector2?> place, float seconds, float holdSeconds, float easeIn = 0f, float easeOut = 0f)
    {
        ArgumentNullException.ThrowIfNull(place);
        var hold = holdSeconds * 1000f;
        Begin(new Turn { Tracking = true, Tracked = place, TrackedPlace = place() ?? _camera.Pivot, HoldFrames = hold < 1f ? 0 : (int)(hold / FrameMilliseconds) }, seconds * 1000f, easeIn, easeOut);
    }

    /// <summary><c>W3DView::resetCamera</c>: to the place, with the default angle at its end, the largest zoom there and the default pitch.</summary>
    public void Reset(Vector2 place, float seconds, float easeIn = 0f, float easeOut = 0f)
    {
        MoveTo(place, seconds, easeIn, easeOut, orient: false);
        _path?.SetFinalAngle(0f);
        Zoom(_camera.MaxZoomAt(place), seconds, easeIn, easeOut);
        Pitch(1f, seconds, easeIn, easeOut);
    }

    /// <summary>What is left of the move or turn in progress, in milliseconds, for the two mods that stretch over it.</summary>
    private IEnumerable<(float Milliseconds, Vector2 Place)> Running()
    {
        if (_turn is { } turn) yield return ((turn.Frames + turn.HoldFrames - turn.Current) * FrameMilliseconds, _camera.Pivot);
        if (_path is { } path) yield return (path.RemainingMilliseconds, path.Destination);
    }

    /// <summary><c>W3DView::cameraModFinalZoom</c>: a share of the largest zoom at the end of the move, reached with its end.</summary>
    public void ModFinalZoom(float share, float easeIn = 0f, float easeOut = 0f)
    {
        foreach (var (milliseconds, place) in Running().ToArray()) ZoomOver(share * _camera.MaxZoomAt(place), milliseconds, milliseconds * easeIn, milliseconds * easeOut);
    }

    /// <summary><c>W3DView::cameraModFinalPitch</c>.</summary>
    public void ModFinalPitch(float pitch, float easeIn = 0f, float easeOut = 0f)
    {
        foreach (var (milliseconds, _) in Running().ToArray()) PitchOver(pitch, milliseconds, milliseconds * easeIn, milliseconds * easeOut);
    }

    /// <summary><c>W3DView::cameraModLookToward</c>: does nothing to a rotation about a point.</summary>
    public void ModLookToward(Vector2 place)
    {
        if (_turn is null) _path?.LookToward(place);
    }

    /// <summary><c>W3DView::cameraModFinalLookToward</c>.</summary>
    public void ModFinalLookToward(Vector2 place)
    {
        if (_turn is null) _path?.FinalLookToward(place);
    }

    /// <summary><c>W3DView::cameraModFinalMoveTo</c>.</summary>
    public void ModFinalMoveTo(Vector2 place)
    {
        if (_turn is null) _path?.FinalMoveTo(place);
    }

    /// <summary><c>W3DView::cameraModFreezeAngle</c>.</summary>
    public void ModFreezeAngle()
    {
        if (_turn is { } turn)
        {
            if (turn.Tracking) turn.Tracked = null;
            else turn.StartAngle = turn.EndAngle = _camera.Angle;
        }
        _path?.FreezeAngle();
    }

    /// <summary><c>W3DView::cameraModRollingAverage</c>.</summary>
    public void ModRollingAverage(int frames) => _path?.RollingAverage(frames);

    /// <summary><c>View::setCameraLock</c> with <c>setSnapMode(LOCK_FOLLOW, 0)</c>, or <c>LOCK_TETHER</c> with its play (<c>doCameraFollowNamed</c>, <c>doCameraTetherNamed</c>).</summary>
    public void Lock(Func<CameraLockTarget?> target, bool snap, bool tether = false, float play = 0f)
    {
        ArgumentNullException.ThrowIfNull(target);
        _camera.HoldConstraints();
        _lock = new LockState(target, tether, play, snap);
    }

    /// <summary><c>View::setCameraLock(INVALID_ID)</c>.</summary>
    public void Unlock() => _lock = null;

    /// <summary><c>W3DView::lookAt</c> ends a rotation, a move and the lock.</summary>
    internal void StopMoveAndTurn()
    {
        _turn = null;
        _path = null;
        _lock = null;
    }

    /// <summary><c>W3DView::scrollBy</c> ends a rotation only.</summary>
    internal void StopTurn() => _turn = null;

    /// <summary>A player's angle, pitch or zoom ends everything a script does (<c>W3DView::setAngle</c>, <c>setPitch</c>, <c>setHeightAboveGround</c>, <c>setZoomToDefault</c>).</summary>
    internal void StopAll()
    {
        StopMoveAndTurn();
        _zoom = null;
        _pitch = null;
    }

    /// <summary><c>W3DView::updateCameraMovements</c> and the lock of <c>W3DView::update</c> for the time of a drawn frame; a request of no time is done with the first step, however short.</summary>
    public void Step(double seconds)
    {
        if (seconds <= 0) return;
        var frames = double.IsInfinity(seconds) ? float.MaxValue : (float)seconds * FramesPerSecond;
        if (_lock is { } locked) StepLock(locked, frames);
        if (_lock is not null) _path = null;
        if (!IsMoving) return;
        _movedSinceAsked = true;
        if (_zoom is { } zoom)
        {
            _camera.SetScriptedZoom(zoom.Advance(frames));
            if (zoom.IsDone) _zoom = null;
        }
        if (_pitch is { } pitch)
        {
            _camera.SetFxPitch(pitch.Advance(frames));
            if (pitch.IsDone) _pitch = null;
        }
        if (_turn is { } turn) StepTurn(turn, frames);
        else if (_path is { } path)
        {
            path.Advance(frames >= float.MaxValue ? float.MaxValue : frames / FramesPerSecond);
            _camera.SetScriptedAngle(path.Angle);
            _camera.SetScriptedPlace(path.Position, path.GroundLevel);
            if (path.IsFinished) _path = null;
        }
    }

    private void StepTurn(Turn turn, float frames)
    {
        if (!turn.Tracking)
        {
            turn.Current += frames;
            if (turn.Current >= turn.Frames || turn.Frames <= 1)
            {
                _turn = null;
                _camera.SetScriptedAngle(turn.EndAngle);
                return;
            }
            _camera.SetScriptedAngle(turn.StartAngle + (turn.EndAngle - turn.StartAngle) * CameraPath.Ease(turn.Current / turn.Frames, turn.EaseIn, turn.EaseOut));
            return;
        }

        // Each frame of the original takes a share of what is left of the angle, so a tracked turn goes in the original's whole frames.
        turn.Carry += frames;
        while (turn.Carry >= 1f && _turn is not null)
        {
            turn.Carry -= 1f;
            turn.Current++;
            if (turn.Current <= turn.Frames + turn.HoldFrames)
            {
                if (turn.Tracked?.Invoke() is { } place) turn.TrackedPlace = place;
                var direction = turn.TrackedPlace - _camera.Pivot;
                if (direction.Length() >= 0.1f)
                {
                    var angle = CameraPath.HeadingOf(direction);
                    if (turn.Current <= turn.Frames) angle = _camera.Angle + CameraPath.Normal(angle - _camera.Angle) * CameraPath.Ease(turn.Current / turn.Frames, turn.EaseIn, turn.EaseOut);
                    _camera.SetScriptedAngle(angle);
                }
            }
            if (turn.Current >= turn.Frames + turn.HoldFrames) _turn = null;
        }
    }

    // W3DView::update: the lock moves a share of the way per frame of the original, so it goes in whole frames as well.
    private void StepLock(LockState locked, float frames)
    {
        locked.Carry = Math.Min(locked.Carry + frames, 60f);
        while (locked.Carry >= 1f || locked.Snap)
        {
            locked.Carry = Math.Max(0f, locked.Carry - 1f);
            if (locked.Target() is not { } target)
            {
                _lock = null;
                return;
            }
            _movedSinceAsked = true;
            locked.FollowFactor = locked.FollowFactor < 0f ? 0.05f : Math.Min(1f, locked.FollowFactor + 0.05f);
            var object2D = new Vector2(target.Position.X, target.Position.Y);
            var current = _camera.Pivot;
            var delta = object2D - current;
            if (locked.Snap) current = object2D;
            else if (locked.Tether)
            {
                var threshold = _camera.Settings.PartitionCellSize * _camera.Settings.PartitionCellSize;
                var distanceSquared = delta.LengthSquared();
                if (distanceSquared >= threshold) current += delta * ((1f - threshold / distanceSquared) * 0.5f);
                else current += delta * (0.01f * locked.Play);
            }
            else current += delta * locked.FollowFactor;
            if (!locked.Tether && target.AirborneHeading is { } heading && target.Position.Z > _camera.GroundHeightAt(object2D) + 1f)
            {
                var ideal = CameraPath.Normal(heading - MathF.PI / 2f);
                _camera.SetScriptedAngle(locked.Snap ? ideal : _camera.Angle + CameraPath.Normal(ideal - CameraPath.Normal(_camera.Angle)) * 0.1f);
            }
            locked.Snap = false;
            _camera.SetScriptedPlace(current, target.Position.Z);
        }
    }
}
