using System.Buffers.Binary;
using System.Security.Cryptography;
using CnCNew.Audio.Mixing;

namespace CnCNew.Game.Audio;

/// <summary>
/// Renders the mixer without a sound device: every <see cref="Pump"/> mixes exactly the audio of the time that passed, in one block, and either discards it (a
/// headless run that only wants the counters) or appends it to a 16-bit stereo WAV file. The block sizes follow from the time steps alone, so a run with fixed
/// time steps renders the same blocks and — the mixer being deterministic for equal commands and block sizes — the same bytes every time; <see cref="Finish"/>
/// returns the SHA-256 of the samples. A run driven by wall-clock frame times is not reproducible (other block sizes, other command timing).
/// </summary>
public sealed class OfflineAudioRenderer : IDisposable
{
    private const int HeaderBytes = 44;
    private readonly SoftwareMixer _mixer;
    private readonly FileStream? _file;
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    private float[] _block = [];
    private byte[] _bytes = [];
    private double _seconds;
    private long _frames;
    private float _peak;
    private string? _result;

    /// <param name="wavPath">The file to write, or <see langword="null"/> to render and discard.</param>
    public OfflineAudioRenderer(SoftwareMixer mixer, string? wavPath = null)
    {
        _mixer = mixer ?? throw new ArgumentNullException(nameof(mixer));
        if (wavPath is null) return;
        Path = System.IO.Path.GetFullPath(wavPath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        _file = new FileStream(Path, FileMode.Create, FileAccess.Write, FileShare.Read);
        _file.Write(new byte[HeaderBytes]);
    }

    /// <summary>Full path of the WAV file, or <see langword="null"/> when nothing is written.</summary>
    public string? Path { get; }

    public long Frames => _frames;

    /// <summary>Largest absolute sample value rendered so far (1 is full scale); 0 means the run was silent.</summary>
    public float Peak => _peak;

    /// <summary>Renders the audio of the next <paramref name="seconds"/>: the frames up to the new total time that were not rendered yet, as one mixer block.</summary>
    public void Pump(double seconds)
    {
        ObjectDisposedException.ThrowIf(_result is not null, this);
        _seconds += Math.Max(0, seconds);
        var frames = (int)Math.Min(int.MaxValue / 4, (long)Math.Floor(_seconds * _mixer.SampleRate) - _frames);
        if (frames <= 0) return;
        if (_block.Length < frames * 2)
        {
            _block = new float[frames * 2];
            _bytes = new byte[frames * 2 * sizeof(short)];
        }
        var block = _block.AsSpan(0, frames * 2);
        var written = _mixer.Render(block);
        var bytes = _bytes.AsSpan(0, written * 2 * sizeof(short));
        for (var index = 0; index < written * 2; index++)
        {
            var sample = block[index];
            _peak = Math.Max(_peak, Math.Abs(sample));
            BinaryPrimitives.WriteInt16LittleEndian(bytes[(index * 2)..], (short)Math.Clamp(MathF.Round(sample * short.MaxValue), short.MinValue, short.MaxValue));
        }
        _hash.AppendData(bytes);
        _file?.Write(bytes);
        _frames += written;
    }

    /// <summary>Closes the file (writing its header) and returns the SHA-256 of the rendered 16-bit samples as hex; further calls return the same value.</summary>
    public string Finish()
    {
        if (_result is not null) return _result;
        _result = Convert.ToHexString(_hash.GetHashAndReset());
        if (_file is not null)
        {
            var dataBytes = (uint)Math.Min(uint.MaxValue - HeaderBytes, _frames * 2 * sizeof(short));
            Span<byte> header = stackalloc byte[HeaderBytes];
            "RIFF"u8.CopyTo(header);
            BinaryPrimitives.WriteUInt32LittleEndian(header[4..], 36 + dataBytes);
            "WAVEfmt "u8.CopyTo(header[8..]);
            BinaryPrimitives.WriteUInt32LittleEndian(header[16..], 16);
            BinaryPrimitives.WriteUInt16LittleEndian(header[20..], 1);
            BinaryPrimitives.WriteUInt16LittleEndian(header[22..], 2);
            BinaryPrimitives.WriteUInt32LittleEndian(header[24..], (uint)_mixer.SampleRate);
            BinaryPrimitives.WriteUInt32LittleEndian(header[28..], (uint)_mixer.SampleRate * 2 * sizeof(short));
            BinaryPrimitives.WriteUInt16LittleEndian(header[32..], 2 * sizeof(short));
            BinaryPrimitives.WriteUInt16LittleEndian(header[34..], 16);
            "data"u8.CopyTo(header[36..]);
            BinaryPrimitives.WriteUInt32LittleEndian(header[40..], dataBytes);
            _file.Position = 0;
            _file.Write(header);
            _file.Dispose();
        }
        return _result;
    }

    public void Dispose()
    {
        Finish();
        _hash.Dispose();
    }
}
