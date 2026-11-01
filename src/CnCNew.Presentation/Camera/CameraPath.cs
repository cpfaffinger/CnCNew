using System.Numerics;

namespace CnCNew.Presentation.Camera;

/// <summary>
/// A scripted move of the camera (<c>W3DView::moveCameraTo</c>, <c>moveCameraAlongWaypointPath</c>, <c>setupWaypointPath</c>, <c>moveAlongWaypointPath</c>). The position runs through the
/// middles of the legs with each waypoint as the control point of a quadratic curve, the angle turns to the direction of the legs, the distance covered follows <see cref="Ease"/>, the ground
/// level (<c>m_groundLevel</c>, what the camera looks at and how the zoom is scaled) goes from where the camera was to the ground at the end, and a stutter of the script holds the camera
/// still for some frames at a time. The original advances it by the same amount every logic frame; here it advances by the time of each drawn frame, so that a flight is as smooth as the
/// picture, and a frame of the original is exactly 1/30 s (the original's 33 ms make a script's second about 1 % longer).
/// </summary>
public sealed class CameraPath
{
    private const float FrameMilliseconds = 1000f / CnCNew.Data.OriginalFrame.PerSecond;
    private const float MinimumLeg = 10f; // MAP_XY_FACTOR
    private readonly List<Vector2> _points;
    private readonly float[] _angles;
    private readonly float[] _lengths;
    private readonly float[] _grounds;
    private readonly int _waypoints;
    private readonly float _totalDistance;
    private readonly float _easeIn;
    private readonly float _easeOut;
    private readonly int _stutterFrames;
    private float _totalMilliseconds;
    private float _elapsedMilliseconds;
    private float _segmentDistance;
    private int _segment = 1;
    private float _stutter;
    private int _rollingAverageFrames = 1;

    /// <summary>A flight along a chain (<c>moveCameraAlongWaypointPath</c>): a leg shorter than a cell drops its waypoint.</summary>
    /// <param name="position">Where the camera looks now.</param>
    /// <param name="angle">Its angle now.</param>
    /// <param name="path">The positions of the chain's waypoints in order.</param>
    /// <param name="seconds">The length of the flight.</param>
    /// <param name="stutterSeconds">The script's camera stutter: a new position only this often.</param>
    /// <param name="easeIn">Seconds to get up to speed.</param>
    /// <param name="easeOut">Seconds to come to rest.</param>
    /// <param name="groundLevel"><c>m_groundLevel</c> now.</param>
    /// <param name="groundAtEnd">The ground at the last waypoint (<c>TheTerrainLogic->getGroundHeight</c>).</param>
    public CameraPath(Vector2 position, float angle, IReadOnlyList<Vector2> path, float seconds, float stutterSeconds, float easeIn, float easeOut, float groundLevel = 0f, float groundAtEnd = 0f)
        : this(position, angle, Chain(position, path), seconds, (int)(stutterSeconds * 1000f / FrameMilliseconds), easeIn, easeOut, groundLevel, groundAtEnd, orient: true)
    {
    }

    private CameraPath(Vector2 position, float angle, List<Vector2> points, float seconds, int stutterFrames, float easeIn, float easeOut, float groundLevel, float groundAtEnd, bool orient)
    {
        Position = position;
        Angle = angle;
        GroundLevel = groundLevel;
        _points = points;
        _totalMilliseconds = Math.Max(1f, seconds * 1000f);
        _stutterFrames = Math.Max(1, stutterFrames);
        _stutter = _stutterFrames;
        _easeIn = Math.Clamp(easeIn * 1000f / _totalMilliseconds, 0f, 1f);
        _easeOut = Math.Max(_easeIn, 1f - Math.Clamp(easeOut * 1000f / _totalMilliseconds, 0f, 1f));

        var count = points.Count - 1;
        _waypoints = count;
        _angles = new float[count + 2];
        _lengths = new float[count + 2];
        _grounds = new float[count + 2];
        _angles[0] = angle;
        var heading = angle;
        for (var index = 1; index < count; index++)
        {
            var leg = _points[index + 1] - _points[index];
            var length = leg.Length();
            _lengths[index] = length;
            _totalDistance += length;
            if (orient && length >= 0.1f) heading = HeadingOf(leg);
            _angles[index] = heading;
        }
        _angles[1] = angle;
        _angles[count] = _angles[count - 1];
        for (var index = count - 1; index > 1; index--) _angles[index] = (_angles[index] + _angles[index - 1]) / 2f;
        _lengths[count + 1] = count >= 1 ? _lengths[count] : 0f;
        if (_totalDistance < 1f && count > 1)
        {
            _lengths[count - 1] += 1f - _totalDistance;
            _totalDistance = 1f;
        }
        var distance = 0f;
        for (var index = 0; index <= count + 1; index++)
        {
            var share = _totalDistance > 0f ? distance / _totalDistance : 1f;
            _grounds[index] = groundLevel * (1f - share) + groundAtEnd * share;
            distance += _lengths[index];
        }
        _grounds[count + 1] = groundAtEnd;
        _angles[count + 1] = _angles[count];
        var last = count >= 2 ? _points[count] - _points[count - 1] : Vector2.Zero;
        _points.Add(_points[count] + last);
        if (count >= 2) _points[0] -= _points[2] - _points[1];
        IsFinished = count <= 1;
    }

    /// <summary><c>moveCameraTo</c>: the one leg to a place, however short. Without a time it is over with the next step, so that the script's mods in the same frame still reach it.</summary>
    public static CameraPath To(Vector2 position, float angle, Vector2 destination, float seconds, float easeIn, float easeOut, float groundLevel, float groundAtEnd, bool orient)
    {
        var path = new CameraPath(position, angle, [position, position, destination], seconds, 1, easeIn, easeOut, groundLevel, groundAtEnd, orient);
        if (path._totalMilliseconds == 1f) path.Step(1f);
        return path;
    }

    // waypoints[0] pads the start, [1] is the camera, [2..] the chain; a leg shorter than a cell drops its waypoint, the last one replaces the point before it.
    private static List<Vector2> Chain(Vector2 position, IReadOnlyList<Vector2> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var points = new List<Vector2> { position, position };
        var count = 1;
        for (var index = 0; index < path.Count; index++)
        {
            count++;
            if (points.Count <= count) points.Add(path[index]);
            else points[count] = path[index];
            if (Vector2.Distance(points[count], points[count - 1]) >= MinimumLeg) continue;
            if (index + 1 == path.Count) points[count - 1] = points[count];
            count--;
        }
        while (points.Count > count + 1) points.RemoveAt(points.Count - 1);
        return points;
    }

    public Vector2 Position { get; private set; }

    public float Angle { get; private set; }

    /// <summary><c>m_groundLevel</c> on the way: from where the camera started to the ground at the last waypoint, in proportion to the distance flown.</summary>
    public float GroundLevel { get; private set; }

    public bool IsFinished { get; private set; }

    /// <summary><c>m_mcwpInfo.waypoints[numWaypoints]</c>: where the move ends.</summary>
    public Vector2 Destination => _points[_waypoints];

    /// <summary><c>totalTimeMilliseconds - elapsedTimeMilliseconds</c>: what the mods of a script stretch their zoom and pitch over; negative once the move is about to end.</summary>
    public float RemainingMilliseconds => _totalMilliseconds - _elapsedMilliseconds;

    /// <summary>Moves on by the time of a drawn frame.</summary>
    public void Advance(float seconds)
    {
        if (!IsFinished && seconds > 0f) Step(seconds >= float.MaxValue / 1000f ? float.MaxValue : seconds * 1000f);
    }

    /// <summary><c>ParabolicEase</c>: the share of the way at a share of the time. <paramref name="easeOut"/> is the share of the time at which the slowing begins (<c>m_out</c>).</summary>
    public static float Ease(float t, float easeIn, float easeOut)
    {
        t = Math.Clamp(t, 0f, 1f);
        var v0 = 1f + easeOut - easeIn;
        if (t < easeIn) return t * t / (v0 * easeIn);
        if (t <= easeOut) return (easeIn + 2f * (t - easeIn)) / v0;
        return (easeIn + 2f * (easeOut - easeIn) + (2f * (t - easeOut) + easeOut * easeOut - t * t) / (1f - easeOut)) / v0;
    }

    /// <summary>The camera angle that looks along <paramref name="direction"/> (the original's <c>acos</c>, less the quarter turn of the default camera).</summary>
    public static float HeadingOf(Vector2 direction)
    {
        var length = direction.Length();
        return Normal(MathF.Acos(Math.Clamp(direction.X / length, -1f, 1f)) * (direction.Y < 0f ? -1f : 1f) - MathF.PI / 2f);
    }

    internal static float Normal(float angle)
    {
        while (angle > MathF.PI) angle -= 2f * MathF.PI;
        while (angle < -MathF.PI) angle += 2f * MathF.PI;
        return angle;
    }

    /// <summary><c>cameraModLookToward</c>: from every waypoint on the way the camera looks at this place.</summary>
    public void LookToward(Vector2 place)
    {
        for (var index = 2; index <= _waypoints; index++)
        {
            var direction = place - PointOfWaypoint(index);
            if (direction.Length() >= 0.1f) _angles[index] = HeadingOf(direction);
        }
        if (_totalMilliseconds == 1f) Step(1f);
    }

    /// <summary><c>cameraModFinalLookToward</c>: at the last waypoint it looks at this place, at the one before half way there.</summary>
    public void FinalLookToward(Vector2 place)
    {
        for (var index = Math.Max(_waypoints - 1, 2); index <= _waypoints; index++)
        {
            var direction = place - PointOfWaypoint(index);
            if (direction.Length() < 0.1f) continue;
            var angle = HeadingOf(direction);
            if (index != _waypoints) angle = Normal(_angles[index] + Normal(angle - _angles[index]) / 2f);
            _angles[index] = angle;
        }
    }

    /// <summary><c>cameraModFreezeAngle</c>: the camera keeps the angle it started with.</summary>
    public void FreezeAngle()
    {
        for (var index = 0; index < _waypoints; index++) _angles[index + 1] = _angles[0];
    }

    /// <summary><c>cameraModFinalMoveTo</c>: the rest of the way is moved so that it ends at this place.</summary>
    public void FinalMoveTo(Vector2 place)
    {
        var delta = place - _points[_waypoints];
        for (var index = 2; index <= _waypoints; index++) _points[index] += delta;
    }

    /// <summary><c>W3DView::resetCamera</c>: <c>cameraAngle[2]</c>, what the angle comes to at the end.</summary>
    internal void SetFinalAngle(float angle) => _angles[_waypoints] = angle;

    /// <summary><c>cameraModRollingAverage</c>: the angle follows over this many frames.</summary>
    public void RollingAverage(int frames) => _rollingAverageFrames = Math.Max(1, frames);

    // The point of the curve nearest a waypoint: the middle of the half legs on both sides of it.
    private Vector2 PointOfWaypoint(int index)
    {
        var start = (_points[index - 1] + _points[index]) / 2f;
        var middle = _points[index];
        var end = (_points[index] + _points[index + 1]) / 2f;
        return Curve(start, middle, end, 0.5f);
    }

    private static Vector2 Curve(Vector2 start, Vector2 middle, Vector2 end, float factor)
        => start + factor * (end - start) + (1f - factor) * factor * (middle - end + middle - start);

    private void Step(float milliseconds)
    {
        _elapsedMilliseconds += milliseconds;
        if (_elapsedMilliseconds > _totalMilliseconds)
        {
            Angle = _angles[_waypoints];
            GroundLevel = _grounds[_waypoints];
            Position = _points[_waypoints];
            IsFinished = true;
            return;
        }
        var delta = Ease(_elapsedMilliseconds / _totalMilliseconds, _easeIn, _easeOut) - Ease((_elapsedMilliseconds - milliseconds) / _totalMilliseconds, _easeIn, _easeOut);
        _segmentDistance += delta * _totalDistance;
        while (_segmentDistance >= _lengths[_segment])
        {
            _segmentDistance -= _lengths[_segment];
            _segment++;
            if (_segment < _waypoints) continue;
            _totalMilliseconds = 0f; // "Will end following next frame."
            return;
        }
        var frames = milliseconds / FrameMilliseconds;
        if (_stutterFrames > 1)
        {
            _stutter -= frames;
            if (_stutter > 0f) return;
            _stutter += _stutterFrames;
        }
        var along = _segmentDistance / _lengths[_segment];
        var (angle1, angle2) = (_angles[_segment], _angles[_segment + 1]);
        if (angle2 - angle1 > MathF.PI) angle1 += 2f * MathF.PI;
        if (angle2 - angle1 < -MathF.PI) angle1 -= 2f * MathF.PI;

        // The rolling average takes a share of the way per frame of the original, whatever the time of this step; at the last leg it runs out into the angle itself.
        var average = 1f / _rollingAverageFrames;
        if (_segment == _waypoints - 1) average += (1f - average) * along;
        var share = average >= 1f ? 1f : 1f - MathF.Pow(1f - average, frames);
        Angle = Normal(Angle + share * Normal(Normal(angle1 * (1f - along) + angle2 * along) - Angle));
        GroundLevel = _grounds[_segment] * (1f - along) + _grounds[_segment + 1] * along;

        // The curve of the half leg: from the middle of one leg over the waypoint to the middle of the next.
        var near = along < 0.5f ? _segment : _segment + 1;
        var factor = along < 0.5f ? along + 0.5f : along - 0.5f;
        Position = Curve((_points[near - 1] + _points[near]) / 2f, _points[near], (_points[near] + _points[near + 1]) / 2f, factor);
    }
}
