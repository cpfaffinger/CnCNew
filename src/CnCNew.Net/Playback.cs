namespace CnCNew.Net;

/// <summary>The numbers of clock synchronisation and playback control (docs/NETWORK-PROTOCOL.md section 13); every one is configuration with the documented default.</summary>
public sealed record PlaybackOptions
{
    /// <summary>How often the client asks the server for its tick clock; four times as often while it has no estimate yet.</summary>
    public int PingIntervalMs { get; init; } = 200;
    /// <summary>The window of the moving median over the server-clock samples.</summary>
    public int SyncSamples { get; init; } = 9;
    /// <summary>How far back bundle arrivals count for the playback delay.</summary>
    public int JitterWindowMs { get; init; } = 10_000;
    /// <summary>The quantile of the arrival lateness the playback delay covers, in thousandths.</summary>
    public int JitterPerMille { get; init; } = 990;
    /// <summary>Added to the quantile.</summary>
    public int MarginMs { get; init; } = 4;
    /// <summary>The most the playback rate deviates from the game speed, in thousandths ("a few percent at most").</summary>
    public int MaxRateAdjustPerMille { get; init; } = 50;
    /// <summary>A deviation from the target is worked off as if it were to vanish within this time (until the rate limit caps it).</summary>
    public int CorrectionTimeMs { get; init; } = 1_000;
    /// <summary>
    /// A deviation that stays (a local clock that runs fast or slow needs a lasting correction of the rate) is taken up by a slow second term with this time constant, so
    /// that the position settles on the target instead of beside it. Several times the correction time: the control does not swing.
    /// </summary>
    public int IntegralTimeMs { get; init; } = 5_000;
    /// <summary>
    /// The slow term only works on small deviations (below <see cref="IntegralBandMs"/>) and never contributes more than this many thousandths: it is there for a clock
    /// that runs fast, not to remember a large correction that is long over and then overshoot.
    /// </summary>
    public int MaxIntegralPerMille { get; init; } = 15;
    public int IntegralBandMs { get; init; } = 20;
    /// <summary>The position is set for the first time when this many bundle arrivals have been measured: a delay taken from nothing would be wrong for the first second.</summary>
    public int MinArrivals { get; init; } = 8;
    /// <summary>
    /// Bundles taken in after the client itself stood still for longer than this (a long frame, a garbage collection) say nothing about the network: when they arrived
    /// is unknown within that time. They are played, but not measured.
    /// </summary>
    public int HitchMs { get; init; } = 50;
    /// <summary>A deviation beyond this is not steered away but set right at once and counted (the path changed, the process was suspended).</summary>
    public int ResyncMs { get; init; } = 1_000;

    public static PlaybackOptions Default { get; } = new();
}

/// <summary>
/// The client's estimate of the server's tick clock (docs/NETWORK-PROTOCOL.md section 13.1). The server's clock is its <b>game time</b>: the number of ticks it has closed times
/// the tick length, plus what has accumulated toward the next - in microseconds, advancing with the game speed. A ping/pong exchange gives one sample (the local time halfway
/// between sending and receiving, and the game time the server stated); the estimate at a local time is the <b>median</b> of what the last samples say for that time, so one
/// exchange with a lopsided round trip moves nothing. A change of the game speed empties the window: samples taken at another speed say nothing about this one.
/// </summary>
public sealed class ClockSync(int samples = 9)
{
    private readonly List<(long MidMicros, long GameMicros, long RoundTripMicros)> _samples = [];
    private readonly long[] _scratch = new long[Math.Max(1, samples)];

    public int Samples => _samples.Count;
    public bool HasEstimate => _samples.Count >= Math.Min(3, _scratch.Length);

    /// <summary>The game speed in percent the server last stated; 0 while it stands (no match, pause).</summary>
    public int SpeedPercent { get; private set; }
    public long SamplesTaken { get; private set; }

    public void Take(long sentMicros, long receivedMicros, long serverGameMicros, int speedPercent)
    {
        if (speedPercent != SpeedPercent)
        {
            _samples.Clear();
            SpeedPercent = speedPercent;
        }
        if (receivedMicros < sentMicros || speedPercent <= 0) return;
        if (_samples.Count == _scratch.Length) _samples.RemoveAt(0);
        _samples.Add((sentMicros + (receivedMicros - sentMicros) / 2, serverGameMicros, receivedMicros - sentMicros));
        SamplesTaken++;
    }

    public void Reset() => _samples.Clear();

    private long Median(int count)
    {
        Array.Sort(_scratch, 0, count);
        return _scratch[count / 2];
    }

    /// <summary>The server's game time at a local time, by the median of the samples.</summary>
    public long GameMicrosAt(long localMicros)
    {
        for (var index = 0; index < _samples.Count; index++)
            _scratch[index] = _samples[index].GameMicros + (localMicros - _samples[index].MidMicros) * SpeedPercent / 100;
        return _samples.Count == 0 ? 0 : Median(_samples.Count);
    }

    /// <summary>The median round-trip time of the samples in microseconds; -1 without one.</summary>
    public long RoundTripMicros
    {
        get
        {
            for (var index = 0; index < _samples.Count; index++) _scratch[index] = _samples[index].RoundTripMicros;
            return _samples.Count == 0 ? -1 : Median(_samples.Count);
        }
    }
}

/// <summary>
/// When a client plays a tick (docs/NETWORK-PROTOCOL.md section 13.2). The server closes tick N at game time (N + 1) tick lengths; its bundle arrives some time later - the way
/// plus the jitter. The <b>playback position</b> is a clock of the client in game time that follows the server's by the <b>playback delay</b>: tick N may run when the position
/// has reached (N + 1) tick lengths (and its bundle is there, which is the driver's business). The delay is measured, not set: the lateness of every bundle against the
/// estimated server clock is kept over a window, and the delay is the configured quantile of it plus a margin - in microseconds, not in whole ticks. The position is never
/// moved by a jump to follow a changed delay or a drifting clock: it runs faster or slower than the game speed by at most a few percent - by a share of the deviation, and
/// by a slowly accumulated share that takes up what stays (a clock that runs fast). Only a deviation beyond the resync limit, and the first estimate, set it directly.
/// </summary>
public sealed class PlaybackControl(PlaybackOptions options, long tickMicros)
{
    private readonly Queue<(long LocalMicros, long Lateness)> _lateness = new();
    private long[] _sorted = new long[256];
    private long _lastLocal;
    private long _delayComputedAt = long.MinValue;
    private long _fraction;
    private long _accumulated;
    private readonly long[] _gaps = new long[31];
    private readonly long[] _gapScratch = new long[31];
    private int _gapCount;
    private int _gapNext;

    /// <summary>The usual time between two looks of the client for messages (the median of the last ones), in microseconds: its frame.</summary>
    public long TypicalLookMicros
    {
        get
        {
            if (_gapCount == 0) return 0;
            Array.Copy(_gaps, _gapScratch, _gapCount);
            Array.Sort(_gapScratch, 0, _gapCount);
            return _gapScratch[_gapCount / 2];
        }
    }

    public long TickMicros => tickMicros;
    public bool Synced { get; private set; }

    /// <summary>The playback position in microseconds of game time.</summary>
    public long PositionMicros { get; private set; }

    /// <summary>The playback delay in microseconds of game time: how far the position follows the server's clock.</summary>
    public long DelayMicros { get; private set; } = options.MarginMs * 1000L;

    /// <summary>The rate adjustment of the last advance in thousandths (positive: faster than the game speed).</summary>
    public int RateAdjustPerMille { get; private set; }
    public long Resyncs { get; private set; }
    public long LatenessSamples { get; private set; }
    private long _backlogBeforeGameMicros = long.MinValue;

    /// <summary>
    /// A bundle arrived: its lateness against the estimated server clock enters the window - unless the client could not have noticed it earlier
    /// (<paramref name="sinceLastLookMicros"/> beyond <see cref="PlaybackOptions.HitchMs"/>: it stood still itself).
    /// </summary>
    public void Arrived(long tick, long localMicros, ClockSync sync, long sinceLastLookMicros = 0)
    {
        if (sinceLastLookMicros > options.HitchMs * 1000L) return;
        if (sinceLastLookMicros > 0)
        {
            _gaps[_gapNext] = sinceLastLookMicros;
            _gapNext = (_gapNext + 1) % _gaps.Length;
            _gapCount = Math.Min(_gapCount + 1, _gaps.Length);
        }
        if (!sync.HasEstimate) return;
        // The bundle arrived some time since the last look. The client's usual frame is part of what the delay has to cover; what a longer frame adds is the client's
        // own doing and is taken off (within twice the usual frame nothing is).
        var excess = Math.Max(0, sinceLastLookMicros - 2 * TypicalLookMicros);
        var lateness = sync.GameMicrosAt(localMicros) - excess * sync.SpeedPercent / 100 - (tick + 1) * tickMicros;
        // The first bundle this client is sent may come with a backlog - everything the server closed before it began to send to somebody who joined a running match.
        // What was closed before that moment says nothing about the way a bundle takes; counting it would raise the delay for the length of the window.
        if (_backlogBeforeGameMicros == long.MinValue) _backlogBeforeGameMicros = sync.GameMicrosAt(localMicros);
        if ((tick + 1) * tickMicros <= _backlogBeforeGameMicros) return;
        // A bundle that is later than the resync limit is not jitter either.
        if (lateness > options.ResyncMs * 1000L) return;
        _lateness.Enqueue((localMicros, lateness));
        LatenessSamples++;
        while (_lateness.Count > 0 && localMicros - _lateness.Peek().LocalMicros > options.JitterWindowMs * 1000L) _lateness.Dequeue();
    }

    private void ComputeDelay(long localMicros)
    {
        // A sort of the window four times a second is cheap (at most a few thousand values) and keeps the arrival path free of it.
        if (_delayComputedAt != long.MinValue && localMicros - _delayComputedAt < 250_000 && _lateness.Count > 16) return;
        _delayComputedAt = localMicros;
        while (_lateness.Count > 0 && localMicros - _lateness.Peek().LocalMicros > options.JitterWindowMs * 1000L) _lateness.Dequeue();
        if (_lateness.Count == 0) return;
        if (_sorted.Length < _lateness.Count) _sorted = new long[_lateness.Count * 2];
        var count = 0;
        foreach (var (_, lateness) in _lateness) _sorted[count++] = lateness;
        Array.Sort(_sorted, 0, count);
        var index = Math.Min(count - 1, (int)((long)count * options.JitterPerMille / 1000));
        DelayMicros = Math.Max(0, _sorted[index]) + options.MarginMs * 1000L;
    }

    /// <summary>Moves the position to a local time.</summary>
    public void Advance(long localMicros, ClockSync sync)
    {
        var elapsed = Math.Max(0, localMicros - _lastLocal);
        _lastLocal = localMicros;
        if (!sync.HasEstimate || (!Synced && _lateness.Count < options.MinArrivals))
        {
            // No estimate (before the first samples, after a change of speed), or no measured arrivals yet: ticks are played as their bundles arrive until there are.
            Synced = false;
            RateAdjustPerMille = 0;
            _accumulated = 0;
            return;
        }
        ComputeDelay(localMicros);
        var target = sync.GameMicrosAt(localMicros) - DelayMicros;
        var error = target - PositionMicros;
        if (!Synced || Math.Abs(error) > options.ResyncMs * 1000L)
        {
            if (Synced) Resyncs++;
            PositionMicros = target;
            Synced = true;
            RateAdjustPerMille = 0;
            _fraction = 0;
            _accumulated = 0;
            return;
        }
        // The deviation over time, in microseconds squared - only while it is small, and bounded: a large correction (the delay changed) is the first term's work, and
        // must not be remembered here to overshoot when it is over.
        var unit = (long)options.CorrectionTimeMs * options.IntegralTimeMs * 1000;
        if (Math.Abs(error) <= options.IntegralBandMs * 1000L)
            _accumulated = Math.Clamp(_accumulated + error * elapsed, -options.MaxIntegralPerMille * unit, options.MaxIntegralPerMille * unit);
        var adjust = (int)Math.Clamp(error / options.CorrectionTimeMs + _accumulated / unit, -options.MaxRateAdjustPerMille, options.MaxRateAdjustPerMille);
        RateAdjustPerMille = adjust;
        // elapsed * speed/100 * (1000 + adjust)/1000, with the remainder carried so that nothing is lost to rounding over an hour.
        var scaled = elapsed * sync.SpeedPercent * (1000 + adjust) + _fraction;
        PositionMicros += scaled / 100_000;
        _fraction = scaled % 100_000;
    }

    /// <summary>Whether the playback time of a tick is reached. Without an estimate every tick is due when its bundle is there.</summary>
    public bool TimeReached(long tick) => !Synced || PositionMicros >= (tick + 1) * tickMicros;

    /// <summary>How far the position stands between the end of the tick before <paramref name="nextTick"/> and its own, 0 to 1: what a frame interpolates with.</summary>
    public double Fraction(long nextTick) => !Synced ? 1 : Math.Clamp((PositionMicros - nextTick * tickMicros) / (double)tickMicros, 0, 1);

    /// <summary>
    /// How far the position stands beyond the time <paramref name="nextTick"/> was due, in ticks, 0 to 1: the tick is late (its bundle has not arrived, or the game
    /// could not play it yet), and a frame that extrapolates draws that much ahead of the last tick it has.
    /// </summary>
    public double Ahead(long nextTick) => !Synced ? 0 : Math.Clamp((PositionMicros - (nextTick + 1) * tickMicros) / (double)tickMicros, 0, 1);

    /// <summary>The bundles that have arrived ahead of the position, in milliseconds of game time: the buffer level.</summary>
    public double BufferMs(long newestBundleTick) => Synced ? ((newestBundleTick + 1) * tickMicros - PositionMicros) / 1000.0 : 0;
}
