using System.Collections.Immutable;
using CnCNew.Audio.Mp3;
using CnCNew.Audio.Wav;

namespace CnCNew.Audio;

public enum AudioContainer
{
    Wav,
    Mp3,
}

public sealed record DecodedAudio(
    PcmBuffer Pcm,
    AudioContainer Container,
    string FormatDescription,
    ImmutableArray<WavDiagnostic> Diagnostics);

/// <summary>
/// Decodes a sound file by its content signature (<c>RIFF…WAVE</c> or an MPEG audio frame / ID3 tag), independent of the
/// file extension. A mismatch between extension and content is reported as the <c>extension-mismatch</c> diagnostic.
/// </summary>
public static class AudioFileDecoder
{
    public static AudioContainer Sniff(ReadOnlySpan<byte> head)
    {
        if (head.Length >= 12 && head[..4].SequenceEqual("RIFF"u8) && head.Slice(8, 4).SequenceEqual("WAVE"u8))
            return AudioContainer.Wav;
        if (head.Length >= 3 && head[..3].SequenceEqual("ID3"u8))
            return AudioContainer.Mp3;
        if (head.Length >= 2 && head[0] == 0xFF && (head[1] & 0xE0) == 0xE0)
            return AudioContainer.Mp3;
        throw new InvalidDataException("Unknown audio signature (neither RIFF/WAVE nor MPEG audio).");
    }

    /// <summary>Fully decodes a file; <paramref name="virtualPath"/> is used only for the extension check.</summary>
    public static DecodedAudio Decode(Stream source, string virtualPath, WavReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        using var copy = new MemoryStream();
        source.CopyTo(copy);
        var bytes = copy.GetBuffer().AsSpan(0, (int)copy.Length);
        var container = Sniff(bytes);
        var extension = Path.GetExtension(virtualPath);
        var expected = extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ? AudioContainer.Mp3
            : extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) ? AudioContainer.Wav : (AudioContainer?)null;
        var extra = expected is { } value && value != container
            ? ImmutableArray.Create(new WavDiagnostic("extension-mismatch", $"'{virtualPath}' contains {container} data."))
            : [];

        if (container == AudioContainer.Wav)
        {
            var wav = WavReader.Decode(bytes, options);
            return new DecodedAudio(wav.Pcm, container, wav.Format.Description, extra.AddRange(wav.Diagnostics));
        }

        copy.Position = 0;
        var pcm = Mp3Decoder.DecodeAll(new MemoryStream(copy.GetBuffer(), 0, (int)copy.Length, writable: false));
        return new DecodedAudio(pcm, container, $"MPEG audio {(pcm.Channels == 1 ? "mono" : "stereo")} {pcm.SampleRate} Hz", extra);
    }

    /// <summary>Opens a streaming source: MP3 streams frame by frame, WAV is decoded fully and wrapped.</summary>
    public static IPcmStream OpenStream(Stream source, WavReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        Span<byte> head = stackalloc byte[12];
        var read = source.ReadAtLeast(head, head.Length, throwOnEndOfStream: false);
        source.Position = 0;
        if (Sniff(head[..read]) == AudioContainer.Mp3) return new Mp3PcmStream(source);
        using (source)
            return new PcmBufferStream(WavReader.Decode(source, options).Pcm);
    }
}
