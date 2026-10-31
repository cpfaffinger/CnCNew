using System.Numerics;

namespace CnCNew.Presentation.Camera;

/// <summary>
/// A scripted flight along a chain of waypoints (<c>W3DView::moveCameraAlongWaypointPath</c>, <c>setupWaypointPath</c>, <c>moveAlongWaypointPath</c>). It moves once per logic frame of the
/// original, 30 a second, whatever the frame rate. The position runs through the middles of the legs with each waypoint as the control point of a quadratic curve, the angle turns to
/// the direction of the legs, the distance covered follows <see cref="Ease"/>, and a stutter of the script holds the camera still for some frames at a time.
/// </summary>
public sealed class CameraPath
{
    private const float FrameSeconds = 1f / CnCNew.Data.OriginalFrame.PerSecond;
    private const float MinimumLeg = 10f; // MAP_XY_FACTOR
    private readonly List<Vector2> _points = [];
    private readonly float[] _angles;
    private readonly float[] _lengths;
    private readonly int _waypoints;
    private readonly float _totalSeconds;
    private readonly float _totalDistance;
    private readonly float _easeIn;
    private readonly float _easeOut;
    private readonly int _stutterFrames;
    private float _elapsed;
    private float _segmentDistance;
    private int _segment = 1;
    private int _stutter;
    private float _carry;
    private bool _ending;

    /// <param name="position">Where the camera looks now.</param>
    /// <param name="angle">Its angle now.</param>
    /// <param name="path">The positions of the chain's waypoints in order.</param>
    /// <param name="seconds">The length of the flight.</param>
    /// <param name="stutterSeconds">The script's camera stutter: a new position only this often.</param>
    /// <param name="easeIn">Seconds to get up to speed.</param>
    /// <param name="easeOut">Seconds to come to rest.</param>
    public CameraPath(Vector2 position, float angle, IReadOnlyList<Vector2> path, float seconds, float stutterSeconds, float easeIn, float easeOut)
    {
        ArgumentNullException.ThrowIfNull(path);
        Position = position;
        Angle = angle;
        _totalSeconds = Math.Max(0.001f, seconds);
        _stutterFrames = Math.Max(1, (int)(stutterSeconds / FrameSeconds));
        _stutter = _stutterFrames;
        _easeIn = Math.Clamp(easeIn / _totalSeconds, 0f, 1f);
        _easeOut = Math.Max(_easeIn, 1f - Math.Clamp(easeOut / _totalSeconds, 0f, 1f));

        // waypoints[0] pads the start, [1] is the camera, [2..] the chain; a leg shorter than a cell drops its waypoint, the last one replaces the point before it.
        _points.Add(position);
        _points.Add(position);
        var count = 1;
        for (var index = 0; index < path.Count; index++)
        {
            count++;
            if (_points.Count <= count) _points.Add(path[index]);
            else _points[count] = path[index];
            if (Vector2.Distance(_points[count], _points[count - 1]) >= MinimumLeg) continue;
            if (index + 1 == path.Count) _points[count - 1] = _points[count];
            count--;
        }
        _waypoints = count;
        while (_points.Count > count + 1) _points.RemoveAt(_points.Count - 1);

        _angles = new float[count + 2];
        _lengths = new float[count + 2];
        var heading = angle;
        for (var index = 1; index < count; index++)
        {
            var leg = _points[index + 1] - _points[index];
            var length = leg.Length();
            _lengths[index] = length;
            _totalDistance += length;
            if (length >= 0.1f) heading = Normal(MathF.Acos(leg.X / length) * (leg.Y < 0f ? -1f : 1f) - MathF.PI / 2f);
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
        _angles[count + 1] = _angles[count];
        var last = count >= 2 ? _points[count] - _points[count - 1] : Vector2.Zero;
        _points.Add(_points[count] + last);
        if (count >= 2) _points[0] -= _points[2] - _points[1];
        IsFinished = count <= 1;
    }

    public Vector2 Position { get; private set; }

    public float Angle { get; private set; }

    public bool IsFinished { get; private set; }

    /// <summary>Moves on by real seconds, one step per logic frame of the original.</summary>
    public void Advance(float seconds)
    {
        _carry += seconds;
        while (_carry >= FrameSeconds && !IsFinished)
        {
            _carry -= FrameSeconds;
            Step();
        }
    }

    /// <summary><c>ParabolicEase</c>: the share of the way at a share of the time.</summary>
    public static float Ease(float t, float easeIn, float easeOut)
    {
        t = Math.Clamp(t, 0f, 1f);
        var v0 = 1f + easeOut - easeIn;
        if (t < easeIn) return t * t / (v0 * easeIn);
        if (t <= easeOut) return (easeIn + 2f * (t - easeIn)) / v0;
        return (easeIn + 2f * (easeOut - easeIn) + (2f * (t - easeOut) + easeOut * easeOut - t * t) / (1f - easeOut)) / v0;
    }

    private static float Normal(float angle)
    {
        while (angle > MathF.PI) angle -= 2f * MathF.PI;
        while (angle < -MathF.PI) angle += 2f * MathF.PI;
        return angle;
    }

    private void Step()
    {
        _elapsed += FrameSeconds;
        if (_ending || _elapsed > _totalSeconds)
        {
            Angle = _angles[_waypoints];
            Position = _points[_waypoints];
            IsFinished = true;
            return;
        }
        var delta = Ease(_elapsed / _totalSeconds, _easeIn, _easeOut) - Ease((_elapsed - FrameSeconds) / _totalSeconds, _easeIn, _easeOut);
        _segmentDistance += delta * _totalDistance;
        while (_segmentDistance >= _lengths[_segment])
        {
            _segmentDistance -= _lengths[_segment];
            _segment++;
            if (_segment < _waypoints) continue;
            _ending = true;
            return;
        }
        if (--_stutter > 0) return;
        _stutter = _stutterFrames;
        var along = _segmentDistance / _lengths[_segment];
        var (angle1, angle2) = (_angles[_segment], _angles[_segment + 1]);
        if (angle2 - angle1 > MathF.PI) angle1 += 2f * MathF.PI;
        if (angle2 - angle1 < -MathF.PI) angle1 -= 2f * MathF.PI;
        Angle = Normal(angle1 * (1f - along) + angle2 * along);

        // The curve of the half leg: from the middle of one leg over the waypoint to the middle of the next.
        var near = along < 0.5f ? _segment : _segment + 1;
        var factor = along < 0.5f ? along + 0.5f : along - 0.5f;
        var start = (_points[near - 1] + _points[near]) / 2f;
        var middle = _points[near];
        var end = (_points[near] + _points[near + 1]) / 2f;
        Position = start + factor * (end - start) + (1f - factor) * factor * (middle - end + middle - start);
    }
}
