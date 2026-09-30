using System.Collections.Immutable;

namespace CnCNew.Audio.Mixing;

/// <summary>
/// Fully decoded sound effects and speech by virtual path (music streams instead). Files that fail to decode are
/// remembered with their error and return null every time, so a broken file is reported once and never retried.
/// Not thread-safe: fill it on the game thread and hand the buffers to the mixer through <see cref="PlayRequest"/>.
/// </summary>
public sealed class SoundCache(Func<string, Stream> open)
{
    private readonly Dictionary<string, PcmBuffer?> _buffers = new(StringComparer.OrdinalIgnoreCase);
    private readonly SortedDictionary<string, string> _failures = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _buffers.Count;
    public long DecodedSamples { get; private set; }
    public ImmutableSortedDictionary<string, string> Failures => _failures.ToImmutableSortedDictionary(StringComparer.OrdinalIgnoreCase);

    public PcmBuffer? GetOrLoad(string virtualPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(virtualPath);
        if (_buffers.TryGetValue(virtualPath, out var cached)) return cached;
        PcmBuffer? buffer = null;
        try
        {
            using var stream = open(virtualPath);
            buffer = AudioFileDecoder.Decode(stream, virtualPath).Pcm;
            DecodedSamples += buffer.Samples.Length;
        }
        catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or FileNotFoundException or EndOfStreamException)
        {
            _failures[virtualPath] = exception.Message;
        }
        _buffers[virtualPath] = buffer;
        return buffer;
    }
}
