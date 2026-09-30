using System.Buffers.Binary;
using System.Text;
using CnCNew.Audio;
using CnCNew.Audio.Mp3;
using CnCNew.Audio.Wav;

static partial class Tests
{
    public static void WavPcm()
    {
        short[] mono = [0, 16384, -32768, 32767];
        var result = WavReader.Decode(Riff(Fmt(1, 1, 22050, 16), Chunk("LIST", "INFOtest"u8.ToArray()), Chunk("data", Int16(mono))));
        Check.That(result.Pcm.SampleRate == 22050 && result.Pcm.Channels == 1 && result.Pcm.FrameCount == 4, "Mono PCM layout.");
        Check.That(result.Pcm.Samples.SequenceEqual([0f, 0.5f, -1f, 32767 / 32768f]), "16-bit PCM scales by 1/32768.");
        Check.That(result.Diagnostics.IsEmpty && result.Chunks.Select(chunk => chunk.Id).SequenceEqual(["fmt ", "LIST", "data"]), "Chunks are listed, LIST is skipped silently.");

        var stereo = WavReader.Decode(Riff(Fmt(1, 2, 44100, 16), Chunk("data", Int16([100, -100, 200, -200]))));
        Check.That(stereo.Pcm.Channels == 2 && stereo.Pcm.FrameCount == 2 && stereo.Pcm.Samples[1] == -100 / 32768f, "Stereo PCM is interleaved.");

        var eight = WavReader.Decode(Riff(Fmt(1, 1, 11025, 8), Chunk("data", [0, 128, 255])));
        Check.That(eight.Pcm.Samples.SequenceEqual([-1f, 0f, 127 / 128f]), "8-bit PCM is unsigned with 128 as zero.");

        var wide = WavReader.Decode(Riff(Fmt(1, 1, 8000, 24), Chunk("data", [0x00, 0x00, 0x40, 0x00, 0x00, 0x80])));
        Check.That(wide.Pcm.Samples.SequenceEqual([0.5f, -1f]), "24-bit PCM is sign-extended.");
        var floats = WavReader.Decode(Riff(Fmt(3, 1, 8000, 32), Chunk("data", [.. BitConverter.GetBytes(0.25f), .. BitConverter.GetBytes(3f)])));
        Check.That(floats.Pcm.Samples.SequenceEqual([0.25f, 1f]), "IEEE float samples are clamped to [-1, 1].");

        var extensible = WavReader.Decode(Riff(FmtExtensible(1, 1, 22050, 16), Chunk("data", Int16([1000]))));
        Check.That(extensible.Format.FormatTag == WavFormatTags.Extensible && extensible.Format.EncodingTag == WavFormatTags.Pcm
            && extensible.Pcm.Samples[0] == 1000 / 32768f, "WAVE_FORMAT_EXTENSIBLE with the PCM sub-format decodes as PCM.");
    }

    public static void WavTolerances()
    {
        // Odd chunk padded as the RIFF spec demands: no diagnostic.
        var padded = WavReader.Decode(Riff(Fmt(1, 1, 8000, 16), Chunk("note", [1, 2, 3]), Chunk("data", Int16([7]))));
        Check.That(padded.Diagnostics.IsEmpty && padded.Pcm.FrameCount == 1, "A padded odd chunk is read without a diagnostic.");

        // Odd chunk without its pad byte.
        var unpadded = Riff(Fmt(1, 1, 8000, 16), Chunk("note", [1, 2, 3], pad: false), Chunk("data", Int16([7])));
        var tolerated = WavReader.Decode(unpadded);
        Check.That(tolerated.Diagnostics.Single().Code == "unpadded-odd-chunk" && tolerated.Pcm.Samples[0] == 7 / 32768f, "A missing pad byte is tolerated and reported.");
        Check.Throws<InvalidDataException>(() => WavReader.Decode(unpadded, WavReadOptions.StrictMode), "unpadded-odd-chunk");

        // Truncated data chunk: the header promises 8 bytes, only 5 arrive (2 whole frames + 1 byte).
        var truncated = Riff(Fmt(1, 1, 8000, 16), [.. "data"u8, 8, 0, 0, 0, 1, 0, 2, 0, 3]);
        var cut = WavReader.Decode(truncated);
        Check.That(cut.Pcm.FrameCount == 2, "Whole frames of a truncated data chunk are decoded.");
        Check.That(cut.Diagnostics.Select(value => value.Code).Order().SequenceEqual(["partial-frame", "truncated-data"]), "Truncation and the partial frame are reported.");
        Check.Throws<InvalidDataException>(() => WavReader.Decode(truncated, WavReadOptions.StrictMode), "truncated-data");

        var wrongSize = Riff(Fmt(1, 1, 8000, 16), Chunk("data", Int16([1])));
        BinaryPrimitives.WriteUInt32LittleEndian(wrongSize.AsSpan(4), 1000);
        Check.That(WavReader.Decode(wrongSize).Diagnostics.Single().Code == "riff-size-mismatch", "A wrong RIFF size is reported.");

        Check.Throws<InvalidDataException>(() => WavReader.Decode(Riff(Chunk("data", Int16([1])))), "no 'fmt '");
        Check.Throws<InvalidDataException>(() => WavReader.Decode(Riff(Fmt(1, 1, 8000, 16))), "no 'data'");
        Check.Throws<NotSupportedException>(() => WavReader.Decode(Riff(Fmt(0x55, 1, 8000, 16), Chunk("data", [0, 0]))), "0x0055");
        Check.Throws<NotSupportedException>(() => WavReader.Decode(Riff(Fmt(1, 3, 8000, 16), Chunk("data", [0, 0, 0, 0, 0, 0]))), "3 channels");
        Check.Throws<InvalidDataException>(() => WavReader.Decode("RIFX0000WAVE"u8), "Not a RIFF/WAVE");
    }

    public static void ImaAdpcmDecoding()
    {
        // Hand-computed from the IMA step/index tables: predictor 0, index 0, codes 7,0,0,0,0,0,0,0.
        byte[] block = [0, 0, 0, 0, 0x07, 0, 0, 0];
        var decoded = WavReader.Decode(Riff(FmtAdpcm(WavFormatTags.ImaAdpcm, 1, 22050, 8, []), Chunk("data", block)));
        Check.That(decoded.Format.SamplesPerBlock == 9, "IMA samples per block = (align − 4·ch)·2/ch + 1.");
        var expected = new short[] { 0, 11, 13, 14, 15, 16, 17, 18, 19 }.Select(value => value / 32768f);
        Check.That(decoded.Pcm.Samples.SequenceEqual(expected), "IMA ADPCM hand vector.");

        // Stereo: each channel has its own header and 4-byte words, left word first.
        byte[] stereoBlock = [0, 0, 0, 0, 0x10, 0x00, 0, 0, 0x07, 0, 0, 0, 0x0F, 0, 0, 0];
        var stereo = WavReader.Decode(Riff(FmtAdpcm(WavFormatTags.ImaAdpcm, 2, 22050, 16, []), Chunk("data", stereoBlock)));
        Check.That(stereo.Pcm.FrameCount == 9 && stereo.Pcm.Samples[0] == 0f && stereo.Pcm.Samples[1] == 16 / 32768f, "Stereo headers are per channel.");
        Check.That(stereo.Pcm.Samples[2] == 11 / 32768f && stereo.Pcm.Samples[3] == 5 / 32768f, "Stereo words alternate left/right (code F subtracts 11 from 16).");

        // A 440 Hz sine encoded by an independent test encoder decodes back within the ADPCM error.
        var sine = Enumerable.Range(0, 4000).Select(index => (short)(12000 * Math.Sin(2 * Math.PI * 440 * index / 22050))).ToArray();
        var encoded = EncodeIma(sine, 256);
        var roundTrip = WavReader.Decode(Riff(FmtAdpcm(WavFormatTags.ImaAdpcm, 1, 22050, 256, []), Chunk("fact", UInt32((uint)sine.Length)), Chunk("data", encoded)));
        Check.That(roundTrip.Pcm.FrameCount == sine.Length && roundTrip.FactFrames == sine.Length, "The fact chunk trims the padded last block.");
        // The adaptive step starts at its minimum, so the encoder needs a few samples to catch up with the sine.
        var errors = sine.Select((value, index) => Math.Abs(value / 32768.0 - roundTrip.Pcm.Samples[index])).ToArray();
        var settled = errors.Skip(50).Max();
        var rms = Math.Sqrt(errors.Average(value => value * value));
        Check.That(settled < 0.05 && rms < 0.02, $"IMA round trip error is small after the start-up (max {settled:0.0000}, rms {rms:0.0000}).");

        // A final partial block decodes as far as it goes.
        var partial = WavReader.Decode(Riff(FmtAdpcm(WavFormatTags.ImaAdpcm, 1, 22050, 8, []), Chunk("data", [.. block, 0, 0, 0, 0])));
        Check.That(partial.Pcm.FrameCount == 10 && partial.Diagnostics.IsEmpty, "A trailing header-only block yields its header sample.");
    }

    public static void MsAdpcmDecoding()
    {
        // Hand-computed after the MS ADPCM spec: predictor 0 (256, 0), delta 16, sample1 100, sample2 50, codes 1,2,0,0.
        byte[] block = [0, 16, 0, 100, 0, 50, 0, 0x12, 0x00];
        var decoded = WavReader.Decode(Riff(FmtAdpcm(WavFormatTags.MsAdpcm, 1, 22050, 9, MsExtension(6)), Chunk("data", block)));
        Check.That(decoded.Format.SamplesPerBlock == 6, "MS ADPCM samples per block = (align − 7·ch)·2/ch + 2.");
        Check.That(decoded.Pcm.Samples.SequenceEqual(new short[] { 50, 100, 116, 148, 148, 148 }.Select(value => value / 32768f)), "MS ADPCM hand vector (predictor 0).");

        // Predictor 1 (512, −256), delta 20, sample1 1000, sample2 900, codes F (−1), 0.
        byte[] second = [1, 20, 0, 0xE8, 0x03, 0x84, 0x03, 0xF0];
        var two = WavReader.Decode(Riff(FmtAdpcm(WavFormatTags.MsAdpcm, 1, 22050, 8, MsExtension(4)), Chunk("data", second)));
        Check.That(two.Pcm.Samples.SequenceEqual(new short[] { 900, 1000, 1080, 1160 }.Select(value => value / 32768f)), "MS ADPCM hand vector (predictor 1, negative code, delta floor).");

        Check.Throws<InvalidDataException>(() => WavReader.Decode(Riff(FmtAdpcm(WavFormatTags.MsAdpcm, 1, 22050, 8, MsExtension(4)), Chunk("data", [9, 20, 0, 0, 0, 0, 0, 0]))), "predictor index 9");
    }

    public static void Conversions()
    {
        var stereo = PcmBuffer.FromOwnedArray(100, 2, [1f, 0f, 0.5f, -0.5f]);
        Check.That(PcmConvert.ToMono(stereo).Samples.SequenceEqual([0.5f, 0f]), "Stereo to mono averages.");
        Check.That(PcmConvert.ToStereo(PcmConvert.ToMono(stereo)).Samples.SequenceEqual([0.5f, 0.5f, 0f, 0f]), "Mono to stereo duplicates.");
        var ramp = PcmBuffer.FromOwnedArray(100, 1, [0f, 1f, 2f, 3f]);
        var up = PcmConvert.Resample(ramp, 200);
        Check.That(up.SampleRate == 200 && up.Samples.SequenceEqual([0f, 0.5f, 1f, 1.5f, 2f, 2.5f, 3f, 3f]), "Linear upsampling interpolates and holds the last frame.");
        var down = PcmConvert.Resample(ramp, 50);
        Check.That(down.Samples.SequenceEqual([0f, 2f]), "Downsampling picks every other frame.");
        Check.That(AudioFileDecoder.Sniff("RIFF\0\0\0\0WAVE"u8) == AudioContainer.Wav && AudioFileDecoder.Sniff([0xFF, 0xFB, 0x90, 0]) == AudioContainer.Mp3, "Signature sniffing.");
        var mismatch = AudioFileDecoder.Decode(new MemoryStream(Riff(Fmt(1, 1, 8000, 16), Chunk("data", Int16([1])))), "Data/Audio/x.mp3");
        Check.That(mismatch.Diagnostics.Single().Code == "extension-mismatch", "Content that contradicts the extension is reported.");
    }

    public static void Mp3Streaming()
    {
        // Ten MPEG-1 Layer III frames of digital silence (128 kbit/s, 44.1 kHz, joint-free stereo, zero side info).
        var frame = new byte[417];
        frame[0] = 0xFF; frame[1] = 0xFB; frame[2] = 0x90; frame[3] = 0x00;
        var file = Enumerable.Repeat(frame, 10).SelectMany(value => value).ToArray();
        using var stream = new Mp3PcmStream(new MemoryStream(file));
        Check.That(stream.SampleRate == 44100 && stream.Channels == 2, "MPEG header fields.");
        var buffer = new float[1000];
        long total = 0;
        int read;
        var maximum = 0f;
        while ((read = stream.Read(buffer)) > 0)
        {
            Check.That(read % 2 == 0, "Reads return whole frames.");
            total += read;
            maximum = Math.Max(maximum, buffer.AsSpan(0, read).ToArray().Max(Math.Abs));
        }
        Check.That(total >= 2 * 1152 * 9 && total <= 2 * 1152 * 10, $"Ten frames decode to about 10 × 1152 frames ({total / 2}).");
        Check.That(maximum == 0f, "Silent frames decode to silence.");
        stream.Rewind();
        long again = 0;
        while ((read = stream.Read(buffer)) > 0) again += read;
        Check.That(again == total, "Rewind restarts the stream for looping.");
        Check.Throws<InvalidDataException>(() => new Mp3PcmStream(new MemoryStream(new byte[64])).Dispose(), "MP3");
    }

    // ------------------------------------------------------------------------------------------ fixtures

    internal static byte[] Riff(params byte[][] chunks)
    {
        var body = chunks.SelectMany(value => value).ToArray();
        return [.. "RIFF"u8, .. UInt32((uint)(body.Length + 4)), .. "WAVE"u8, .. body];
    }

    internal static byte[] Chunk(string id, byte[] data, bool pad = true)
        => [.. Encoding.ASCII.GetBytes(id), .. UInt32((uint)data.Length), .. data, .. (pad && data.Length % 2 == 1 ? new byte[] { 0 } : [])];

    internal static byte[] Fmt(ushort tag, ushort channels, uint rate, ushort bits)
    {
        var align = (ushort)(channels * bits / 8);
        return Chunk("fmt ", [.. UInt16(tag), .. UInt16(channels), .. UInt32(rate), .. UInt32(rate * align), .. UInt16(align), .. UInt16(bits)]);
    }

    private static byte[] FmtExtensible(ushort subFormat, ushort channels, uint rate, ushort bits)
    {
        var align = (ushort)(channels * bits / 8);
        byte[] guidTail = [0x00, 0x00, 0x00, 0x00, 0x10, 0x00, 0x80, 0x00, 0x00, 0xAA, 0x00, 0x38, 0x9B, 0x71];
        return Chunk("fmt ", [.. UInt16(0xFFFE), .. UInt16(channels), .. UInt32(rate), .. UInt32(rate * align), .. UInt16(align), .. UInt16(bits),
            .. UInt16(22), .. UInt16(bits), .. UInt32(4), .. UInt16(subFormat), .. guidTail]);
    }

    private static byte[] FmtAdpcm(ushort tag, ushort channels, uint rate, ushort align, byte[] extra)
    {
        var samplesPerBlock = tag == WavFormatTags.ImaAdpcm ? (align - 4 * channels) * 2 / channels + 1 : (align - 7 * channels) * 2 / channels + 2;
        byte[] extension = tag == WavFormatTags.ImaAdpcm ? [.. UInt16(2), .. UInt16((ushort)samplesPerBlock)] : [.. UInt16((ushort)extra.Length), .. extra];
        return Chunk("fmt ", [.. UInt16(tag), .. UInt16(channels), .. UInt32(rate), .. UInt32(rate * align / (uint)samplesPerBlock), .. UInt16(align), .. UInt16(4), .. extension]);
    }

    private static byte[] MsExtension(ushort samplesPerBlock)
    {
        (short, short)[] coefficients = [(256, 0), (512, -256), (0, 0), (192, 64), (240, 0), (460, -208), (392, -232)];
        return [.. UInt16(samplesPerBlock), .. UInt16((ushort)coefficients.Length), .. coefficients.SelectMany(pair => Int16([pair.Item1, pair.Item2]))];
    }

    internal static byte[] Int16(short[] values)
    {
        var bytes = new byte[values.Length * 2];
        for (var index = 0; index < values.Length; index++) BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(index * 2), values[index]);
        return bytes;
    }

    private static byte[] UInt16(ushort value) => [(byte)value, (byte)(value >> 8)];
    internal static byte[] UInt32(uint value) => [(byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24)];

    // Independent IMA ADPCM encoder for the round-trip test (tables from the IMA recommendation).
    private static readonly int[] ImaSteps =
    [
        7, 8, 9, 10, 11, 12, 13, 14, 16, 17, 19, 21, 23, 25, 28, 31, 34, 37, 41, 45, 50, 55, 60, 66, 73, 80, 88, 97, 107, 118,
        130, 143, 157, 173, 190, 209, 230, 253, 279, 307, 337, 371, 408, 449, 494, 544, 598, 658, 724, 796, 876, 963, 1060,
        1166, 1282, 1411, 1552, 1707, 1878, 2066, 2272, 2499, 2749, 3024, 3327, 3660, 4026, 4428, 4871, 5358, 5894, 6484,
        7132, 7845, 8630, 9493, 10442, 11487, 12635, 13899, 15289, 16818, 18500, 20350, 22385, 24623, 27086, 29794, 32767,
    ];
    private static readonly int[] ImaIndex = [-1, -1, -1, -1, 2, 4, 6, 8];

    private static byte[] EncodeIma(short[] samples, int blockAlign)
    {
        var perBlock = (blockAlign - 4) * 2 + 1;
        var output = new List<byte>();
        var index = 0;
        for (var start = 0; start < samples.Length; start += perBlock)
        {
            int predictor = samples[start];
            output.AddRange(Int16([(short)predictor]));
            output.Add((byte)index);
            output.Add(0);
            var nibbles = new List<int>();
            for (var offset = 1; offset < perBlock; offset++)
            {
                var target = start + offset < samples.Length ? samples[start + offset] : 0;
                int step = ImaSteps[index], diff = target - predictor, code = 0;
                if (diff < 0) { code = 8; diff = -diff; }
                if (diff >= step) { code |= 4; diff -= step; }
                if (diff >= step >> 1) { code |= 2; diff -= step >> 1; }
                if (diff >= step >> 2) code |= 1;
                var delta = step >> 3;
                if ((code & 4) != 0) delta += step;
                if ((code & 2) != 0) delta += step >> 1;
                if ((code & 1) != 0) delta += step >> 2;
                predictor = Math.Clamp((code & 8) != 0 ? predictor - delta : predictor + delta, short.MinValue, short.MaxValue);
                index = Math.Clamp(index + ImaIndex[code & 7], 0, 88);
                nibbles.Add(code);
            }
            for (var pair = 0; pair < nibbles.Count; pair += 2)
                output.Add((byte)(nibbles[pair] | (nibbles[pair + 1] << 4)));
        }
        return [.. output];
    }
}
