using CnCNew.Audio.Selection;

namespace CnCNew.Audio.Mixing;

/// <summary>A piece of a voice: a decoded buffer or a run of silence at the output rate; both empty means "skip".</summary>
internal readonly record struct Segment(PcmBuffer? Buffer, int SilenceFrames)
{
    public static Segment Silence(float milliseconds, int outputRate)
        => new(null, (int)Math.Round(milliseconds * outputRate / 1000.0, MidpointRounding.AwayFromZero));
}

internal abstract class FrameSource : IDisposable
{
    /// <summary>Reads one frame as a stereo pair (mono is duplicated); false at the end.</summary>
    public abstract bool TryRead(out float left, out float right, out int sampleRate);

    public virtual void Dispose()
    {
    }
}

/// <summary>Plays a lazily generated sequence of segments.</summary>
internal sealed class SegmentFrameSource(IEnumerable<Segment> segments, int silenceRate) : FrameSource
{
    // A looping event whose sounds are all missing would otherwise spin forever on empty segments.
    private const int MaxConsecutiveEmptySegments = 64;
    private readonly IEnumerator<Segment> _segments = segments.GetEnumerator();
    private Segment _current;
    private bool _hasCurrent;
    private int _position;

    public override bool TryRead(out float left, out float right, out int sampleRate)
    {
        var empty = 0;
        while (true)
        {
            if (_hasCurrent)
            {
                if (_current.Buffer is { } buffer)
                {
                    if (_position < buffer.FrameCount)
                    {
                        var samples = buffer.Samples.AsSpan();
                        if (buffer.Channels == 1)
                            left = right = samples[_position];
                        else
                        {
                            left = samples[2 * _position];
                            right = samples[2 * _position + 1];
                        }
                        sampleRate = buffer.SampleRate;
                        _position++;
                        return true;
                    }
                }
                else if (_position < _current.SilenceFrames)
                {
                    left = right = 0f;
                    sampleRate = silenceRate;
                    _position++;
                    return true;
                }
            }

            if (++empty > MaxConsecutiveEmptySegments || !_segments.MoveNext())
            {
                _hasCurrent = false;
                left = right = 0f;
                sampleRate = silenceRate;
                return false;
            }
            _current = _segments.Current;
            _hasCurrent = true;
            _position = 0;
        }
    }

    public override void Dispose() => _segments.Dispose();

    public static IEnumerable<Segment> ForBuffer(PcmBuffer buffer, int loopCount, float delayMilliseconds, int outputRate)
    {
        if (delayMilliseconds > 0) yield return Segment.Silence(delayMilliseconds, outputRate);
        for (var iteration = 0; loopCount == 0 || iteration < loopCount; iteration++)
            yield return new Segment(buffer, 0);
    }

    /// <summary>
    /// Delay → attack → body, then for every further loop a freshly chosen body after its own delay, then the decay
    /// (<c>MilesAudioManager::notifyOfAudioCompletion</c>/<c>startNextLoop</c>, <c>AudioEventRTS::decreaseLoopCount</c>).
    /// </summary>
    public static IEnumerable<Segment> ForEvent(EventVoiceSource source, int loopCount, SeededRng rng, int outputRate)
    {
        var plan = source.Plan;
        var history = source.History;
        var iteration = plan.First;
        var remaining = loopCount;
        yield return Segment.Silence(iteration.DelayMilliseconds, outputRate);
        if (plan.Attack is { } attack) yield return Lookup(source, attack);
        while (true)
        {
            foreach (var file in iteration.Body)
                yield return Lookup(source, file);
            if (remaining == 1) break;
            if (remaining > 1) remaining--;
            var next = SoundSelector.NextIteration(plan.Event, rng, history, plan.TimeOfDay);
            if (next is null) break;
            iteration = next;
            yield return Segment.Silence(iteration.DelayMilliseconds, outputRate);
        }
        if (plan.Decay is { } decay) yield return Lookup(source, decay);
    }

    private static Segment Lookup(EventVoiceSource source, Catalog.AudioSoundFile file)
        => file.VirtualPath is not null && source.Buffers.TryGetValue(file.VirtualPath, out var buffer) ? new Segment(buffer, 0) : default;
}

/// <summary>Pulls frames from a streaming decoder; loops by rewinding.</summary>
internal sealed class StreamFrameSource(IPcmStream stream, bool loop) : FrameSource
{
    private readonly float[] _chunk = new float[4096 * stream.Channels];
    private int _count;
    private int _position;

    public override bool TryRead(out float left, out float right, out int sampleRate)
    {
        sampleRate = stream.SampleRate;
        if (_position >= _count && !Refill())
        {
            left = right = 0f;
            return false;
        }
        if (stream.Channels == 1)
            left = right = _chunk[_position++];
        else
        {
            left = _chunk[_position++];
            right = _chunk[_position++];
        }
        return true;
    }

    private bool Refill()
    {
        _position = 0;
        _count = stream.Read(_chunk);
        if (_count == 0 && loop)
        {
            stream.Rewind();
            _count = stream.Read(_chunk);
        }
        return _count > 0;
    }

    public override void Dispose() => stream.Dispose();
}

/// <summary>
/// Streaming linear-interpolation resampler: output = previous + (current − previous)·fraction, the fraction advancing
/// by sourceRate × pitch / outputRate per output frame. It reads across segment borders, so loop seams and
/// attack→body→decay transitions are interpolated without a gap. After the last frame it ramps to zero over one source
/// frame and ends.
/// </summary>
internal sealed class Resampler(FrameSource source) : IDisposable
{
    private float _previousLeft, _previousRight, _currentLeft, _currentRight;
    private int _rate;
    private double _fraction;
    private bool _started;
    private bool _sourceEnded;
    private bool _finished;

    public bool Next(float pitch, int outputRate, out float left, out float right)
    {
        if (!_started)
        {
            _started = true;
            if (!source.TryRead(out _previousLeft, out _previousRight, out _rate))
                _finished = true;
            else if (!source.TryRead(out _currentLeft, out _currentRight, out _))
                _sourceEnded = true;
        }
        if (_finished)
        {
            left = right = 0f;
            return false;
        }

        var fraction = (float)_fraction;
        left = _previousLeft + (_currentLeft - _previousLeft) * fraction;
        right = _previousRight + (_currentRight - _previousRight) * fraction;
        _fraction += (double)_rate * pitch / outputRate;
        while (_fraction >= 1.0)
        {
            _fraction -= 1.0;
            _previousLeft = _currentLeft;
            _previousRight = _currentRight;
            if (_sourceEnded)
            {
                _finished = true;
                break;
            }
            if (!source.TryRead(out _currentLeft, out _currentRight, out var rate))
            {
                _currentLeft = _currentRight = 0f;
                _sourceEnded = true;
            }
            else
                _rate = rate;
        }
        return true;
    }

    public void Dispose() => source.Dispose();
}
