using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using CnCNew.Presentation.Objects;

namespace CnCNew.Game.Lockstep;

/// <summary>
/// What a person at a match of a server would feel, as numbers (docs/MULTIPLAYER-FINDINGS.md section 2; the client's <c>--net-stats</c>): from a click to the first
/// frame that shows something of it (the marker, the acknowledgement - the local pre-check answers, docs/PRESENTATION-INPUT.md section 13), from a click to the tick
/// the order is executed in, and to the first frame after that; and how evenly what is interpolated between two ticks moves from one drawn frame to the next. It
/// only reads: the times of the frames, the HUD's feedback, the resolutions of the command path and the drawn frame. Nothing here reaches the simulation.
/// </summary>
public sealed class NetStats
{
    private readonly List<double> _feedbackFrameMs = [];
    private readonly List<double> _executionMs = [];
    private readonly List<double> _executionFrameMs = [];
    private readonly List<double> _executionTicks = [];
    private readonly List<double> _frameMs = [];
    private readonly Dictionary<long, long> _clickOfReceipt = [];
    private readonly List<long> _awaitFeedbackFrame = [];
    private readonly List<long> _awaitExecutionFrame = [];
    private readonly Dictionary<uint, Track> _tracks = [];
    private long _pendingClick = -1;
    private long _clickOfFrame = -1;
    private long _lastFrame = -1;
    private long _clicks, _withoutFeedback, _withoutOrder, _deviations, _rejected;
    private bool _answered;

    // Smoothness: per quantity the steps between two drawn frames.
    private readonly Series _speed = new(), _turret = new(), _health = new(), _construction = new(), _travel = new();
    private long _movingFrames, _jumps, _holds, _pictures;
    private double _fastest;
    private long _aheadFrames, _fullFrames;

    private sealed class Track
    {
        public System.Numerics.Vector3 Position;
        public float Step = -1, Turret = float.NaN, Health = -1, Construction = -1, Travel = float.NaN;
        public bool Moved;
        public long Frame;
    }

    private sealed class Series
    {
        public readonly List<double> Steps = [];
        public long Changed, Unchanged;
        public void Add(double step)
        {
            if (step > 1e-6) { Steps.Add(step); Changed++; }
            else Unchanged++;
        }
    }

    public long Clicks => _clicks;
    public long Frames => _frameMs.Count;

    /// <summary>A synthetic click was handed to the HUD in this frame's input.</summary>
    public void ClickGiven()
    {
        _clicks++;
        if (_pendingClick >= 0) _withoutOrder++;
        _pendingClick = _clickOfFrame = Stopwatch.GetTimestamp();
        _answered = false;
    }

    /// <summary>The HUD answered the click of this frame with a marker or an acknowledgement (the pre-check accepted it).</summary>
    public void FeedbackSeen()
    {
        if (_clickOfFrame < 0 || _answered) return;
        _answered = true;
        _awaitFeedbackFrame.Add(_clickOfFrame);
    }

    /// <summary>The order the click made got its receipt: the resolution will name it.</summary>
    public void ReceiptOfClick(long receipt)
    {
        if (_pendingClick < 0) return;
        _clickOfReceipt[receipt] = _pendingClick;
        _pendingClick = -1;
    }

    /// <summary>The simulation executed an order (or refused it): the tick it was given at and the tick it ran in are in the resolution.</summary>
    public void Resolved(CnCNew.Presentation.Input.CommandResolution resolution, int ticksPerSecond)
    {
        if (!_clickOfReceipt.Remove(resolution.Receipt, out var click)) return;
        var now = Stopwatch.GetTimestamp();
        _executionMs.Add(Stopwatch.GetElapsedTime(click, now).TotalMilliseconds);
        _executionTicks.Add((resolution.Command.Tick - resolution.IssuedTick) * 1000.0 / ticksPerSecond);
        _awaitExecutionFrame.Add(click);
        if (resolution.Deviates) _deviations++;
        if (!resolution.Result.Accepted) _rejected++;
    }

    /// <summary>A frame was drawn and handed to the display: whatever waited for "the first frame that shows it" is closed, and the frame's picture is measured.</summary>
    /// <param name="frameMilliseconds">The time since the last frame when the caller draws on a clock of its own (a recorded match at a fixed frame rate); null measures it.</param>
    public void FrameDrawn(RenderFrame? frame, double? frameMilliseconds = null)
    {
        var now = Stopwatch.GetTimestamp();
        // The HUD answers a click in the frame it is given in, or not at all.
        if (_clickOfFrame >= 0 && !_answered) _withoutFeedback++;
        _clickOfFrame = -1;
        foreach (var click in _awaitFeedbackFrame) _feedbackFrameMs.Add(Stopwatch.GetElapsedTime(click, now).TotalMilliseconds);
        _awaitFeedbackFrame.Clear();
        foreach (var click in _awaitExecutionFrame) _executionFrameMs.Add(Stopwatch.GetElapsedTime(click, now).TotalMilliseconds);
        _awaitExecutionFrame.Clear();
        var milliseconds = frameMilliseconds ?? (_lastFrame < 0 ? 0 : Stopwatch.GetElapsedTime(_lastFrame, now).TotalMilliseconds);
        if (_lastFrame >= 0) _frameMs.Add(milliseconds);
        _lastFrame = now;
        if (frame is null || milliseconds <= 0) return;
        _pictures++;
        if (frame.Ahead > 0f) _aheadFrames++;
        if (frame.Alpha >= 1f && frame.Ahead <= 0f) _fullFrames++;
        var number = _frameMs.Count;
        foreach (var drawable in frame.Drawables)
        {
            if (!drawable.Entity.IsValid || drawable.IsStatic || drawable.IsGhost) continue;
            if (!_tracks.TryGetValue(drawable.Entity.Value, out var track))
            {
                _tracks[drawable.Entity.Value] = new Track { Position = drawable.Position, Health = drawable.Health01, Construction = drawable.ConstructionProgress01,
                    Turret = drawable.TurretYaw ?? float.NaN, Travel = drawable.WheelTravel, Frame = number };
                continue;
            }
            var consecutive = track.Frame == number - 1;
            track.Frame = number;
            // Speed in world units per second: what the eye follows. A unit that moved in the last frame and in this one should move about as fast in both.
            var step = System.Numerics.Vector3.Distance(track.Position, drawable.Position) / (float)(milliseconds / 1000.0);
            var moving = step > 0.5f;
            _fastest = Math.Max(_fastest, step);
            if (consecutive && track.Moved)
            {
                _movingFrames++;
                if (!moving) _holds++;
                else if (track.Step > 0)
                {
                    var ratio = step / track.Step;
                    _speed.Add(Math.Abs(ratio - 1));
                    if (ratio is > 1.5f or < 1 / 1.5f) _jumps++;
                }
            }
            track.Moved = moving;
            track.Step = moving ? step : -1;
            track.Position = drawable.Position;
            if (consecutive)
            {
                if (drawable.TurretYaw is { } turret && !float.IsNaN(track.Turret)) _turret.Add(Math.Abs(FrameProjector.LerpAngle(track.Turret, turret, 1f) - track.Turret) < 1e-6 ? 0 : AngleStep(track.Turret, turret));
                _health.Add(Math.Abs(drawable.Health01 - track.Health));
                _construction.Add(Math.Abs(drawable.ConstructionProgress01 - track.Construction));
                if (!float.IsNaN(track.Travel)) _travel.Add(Math.Abs(drawable.WheelTravel - track.Travel));
            }
            track.Turret = drawable.TurretYaw ?? float.NaN;
            track.Health = drawable.Health01;
            track.Construction = drawable.ConstructionProgress01;
            track.Travel = drawable.WheelTravel;
        }
    }

    private static double AngleStep(float from, float to)
    {
        var difference = Math.Abs(to - from) % (2 * Math.PI);
        return difference > Math.PI ? 2 * Math.PI - difference : difference;
    }

    private static string Histogram(string name, string unit, List<double> values)
    {
        if (values.Count == 0) return $"net-stats: {name}: n=0";
        var sorted = values.Order().ToArray();
        double At(double share) => sorted[Math.Min(sorted.Length - 1, (int)Math.Ceiling(share * sorted.Length) - 1 < 0 ? 0 : (int)Math.Ceiling(share * sorted.Length) - 1)];
        return string.Create(CultureInfo.InvariantCulture,
            $"net-stats: {name}: n={sorted.Length} min={sorted[0]:F1} p50={At(0.50):F1} p90={At(0.90):F1} p95={At(0.95):F1} p99={At(0.99):F1} max={sorted[^1]:F1} mean={sorted.Average():F1} {unit}");
    }

    private static string Steps(string name, Series series, double scale, string unit)
    {
        if (series.Steps.Count == 0) return $"net-stats: steps {name}: changed=0 unchanged={series.Unchanged}";
        var sorted = series.Steps.Order().ToArray();
        double At(double share) => sorted[Math.Min(sorted.Length - 1, Math.Max(0, (int)Math.Ceiling(share * sorted.Length) - 1))] * scale;
        // "changed share": of the frames in which the quantity was on its way, in how many it moved. Interpolated between ticks it moves in (nearly) every frame.
        return string.Create(CultureInfo.InvariantCulture,
            $"net-stats: steps {name}: changed={series.Changed} unchanged={series.Unchanged} p50={At(0.50):F3} p99={At(0.99):F3} max={sorted[^1] * scale:F3} {unit}");
    }

    /// <summary>The lines for the console at the end of a match; a check reads them (<c>scripts/measure-net-latency.ps1</c>).</summary>
    public ImmutableArray<string> Report()
    {
        var lines = ImmutableArray.CreateBuilder<string>();
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"net-stats: clicks={_clicks} withFeedback={_feedbackFrameMs.Count} executed={_executionMs.Count} withoutFeedback={_withoutFeedback} withoutOrder={_withoutOrder + (_pendingClick >= 0 ? 1 : 0)} unresolved={_clickOfReceipt.Count} deviations={_deviations} rejected={_rejected}"));
        lines.Add(Histogram("click-to-first-changed-frame", "ms", _feedbackFrameMs));
        lines.Add(Histogram("click-to-execution", "ms", _executionMs));
        lines.Add(Histogram("click-to-execution-frame", "ms", _executionFrameMs));
        lines.Add(Histogram("order-delay-in-game-time", "ms", _executionTicks));
        lines.Add(Histogram("frame-time", "ms", _frameMs));
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"net-stats: motion: pictures={_pictures} drawnAhead={_aheadFrames} atTheTick={_fullFrames} objects={_tracks.Count} fastest={_fastest:F1} movingUnitFrames={_movingFrames} jumps={_jumps} jumpShare={(_movingFrames == 0 ? 0 : 100.0 * _jumps / _movingFrames):F3}% holds={_holds} holdShare={(_movingFrames == 0 ? 0 : 100.0 * _holds / _movingFrames):F3}%"));
        lines.Add(Steps("speed-change", _speed, 100, "% of the previous frame's speed"));
        lines.Add(Steps("turret", _turret, 180 / Math.PI, "degrees"));
        lines.Add(Steps("health", _health, 100, "% of full"));
        lines.Add(Steps("construction", _construction, 100, "% of done"));
        lines.Add(Steps("wheel-travel", _travel, 1, "world units"));
        return lines.ToImmutable();
    }
}
