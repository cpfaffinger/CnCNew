using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using CnCNew.Content;
using CnCNew.Video;

/// <summary>
/// The checks of the Bink decoder against the real movies (docs/CLIENT-VIDEO.md): <c>audit-movies</c> decodes every movie of a content directory to its last frame and prints what it
/// found, the checksums over the decoded planes and samples and the cost per frame; <c>movie-frame</c> writes one picture (and the sound) to files outside the repository.
/// </summary>
internal static class MovieCommands
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static int Audit(string contentDirectory, string[] options)
    {
        var contains = OptionValue(options, "--only");
        var content = ContentFileSystem.Open(contentDirectory);
        var movies = content.EnumerateFiles().Where(path => path.EndsWith(".bik", StringComparison.OrdinalIgnoreCase))
            .Where(path => contains is null || path.Contains(contains, StringComparison.OrdinalIgnoreCase)).Order(StringComparer.Ordinal).ToArray();
        var failures = 0;
        double totalSeconds = 0, worst = 0;
        var worstMovie = string.Empty;
        long frames = 0;
        foreach (var path in movies)
        {
            try
            {
                using var stream = content.OpenRead(path);
                var report = Decode(BinkFile.Open(stream));
                Console.WriteLine(string.Create(Culture, $"{path}: {report.Line}"));
                totalSeconds += report.Seconds;
                frames += report.Frames;
                if (report.WorstFrameMs > worst) (worst, worstMovie) = (report.WorstFrameMs, path);
            }
            catch (Exception exception) when (exception is InvalidDataException or NotSupportedException)
            {
                failures++;
                Console.WriteLine($"{path}: FAILED {exception.GetType().Name}: {exception.Message}");
            }
        }
        Console.WriteLine(string.Create(Culture, $"movies {movies.Length}, failed {failures}, frames {frames}, playing time {totalSeconds / 60:F1} min, slowest frame {worst:F2} ms ({worstMovie})"));
        return failures == 0 && movies.Length > 0 ? 0 : 1;
    }

    public static int WriteFrame(string contentDirectory, string virtualPath, string frameText, string output, string[] options)
    {
        var frame = int.Parse(frameText, Culture);
        var content = ContentFileSystem.Open(contentDirectory);
        using var stream = content.OpenRead(virtualPath);
        var movie = new BinkMovie(BinkFile.Open(stream));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(frame, movie.FrameCount);
        for (var index = 0; index <= frame; index++) movie.DecodeNextFrame();
        var rgba = new byte[movie.Width * movie.Height * 4];
        movie.CopyRgba(rgba);
        WritePng(output, movie.Width, movie.Height, rgba);
        Console.WriteLine($"{output}: frame {frame} of {movie.FrameCount}, {movie.Width}x{movie.Height}");
        if (OptionValue(options, "--wav") is { } wav && movie.DecodeSound() is { } sound)
        {
            WriteWav(wav, sound);
            Console.WriteLine(string.Create(Culture, $"{wav}: {sound.SampleRate} Hz, {sound.Channels} channels, {sound.Seconds:F2} s, peak {sound.Samples.Max(sample => Math.Abs((int)sample))}"));
        }
        return 0;
    }

    private static (string Line, double Seconds, int Frames, double WorstFrameMs) Decode(BinkFile file)
    {
        var movie = new BinkMovie(file);
        var header = file.Header;
        var rgba = new byte[movie.Width * movie.Height * 4];
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var timer = new Stopwatch();
        double worst = 0;
        var total = TimeSpan.Zero;
        while (true)
        {
            timer.Restart();
            if (!movie.DecodeNextFrame()) break;
            movie.CopyRgba(rgba);
            timer.Stop();
            total += timer.Elapsed;
            worst = Math.Max(worst, timer.Elapsed.TotalMilliseconds);
            movie.WritePlanes(hash.AppendData);
        }
        var framesHash = Convert.ToHexString(hash.GetHashAndReset());

        timer.Restart();
        var sound = movie.DecodeSound();
        timer.Stop();
        var audioLine = "no audio";
        if (sound is not null)
        {
            var bytes = new byte[sound.Samples.Length * 2];
            Buffer.BlockCopy(sound.Samples, 0, bytes, 0, bytes.Length);
            var peak = sound.Samples.Length == 0 ? 0 : sound.Samples.Max(sample => Math.Abs((int)sample));
            audioLine = string.Create(Culture, $"{sound.SampleRate} Hz {sound.Channels} ch {sound.Seconds:F2} s peak {peak} decoded in {timer.Elapsed.TotalMilliseconds:F0} ms, samples SHA-256 {Convert.ToHexString(SHA256.HashData(bytes))}");
        }
        var mean = total.TotalMilliseconds / movie.FrameCount;
        var line = string.Create(Culture, $"{header.Signature} {movie.Width}x{movie.Height} {movie.FrameCount} frames @ {header.FramesPerSecond:F3} fps = {header.DurationSeconds:F2} s, alpha {(movie.HasAlpha ? "yes" : "no")}, {audioLine}; frames SHA-256 {framesHash}; decode mean {mean:F2} ms, max {worst:F2} ms per frame");
        return (line, header.DurationSeconds, movie.FrameCount, worst);
    }

    private static string? OptionValue(string[] options, string name)
    {
        var index = Array.IndexOf(options, name);
        return index >= 0 && index + 1 < options.Length ? options[index + 1] : null;
    }

    private static void WritePng(string path, int width, int height, byte[] rgba)
    {
        using var file = File.Create(path);
        file.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10]);
        Span<byte> head = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(head, width);
        BinaryPrimitives.WriteInt32BigEndian(head[4..], height);
        head[8] = 8; // bits per sample
        head[9] = 6; // RGBA
        WriteChunk(file, "IHDR", head);
        using var pixels = new MemoryStream();
        using (var deflate = new ZLibStream(pixels, CompressionLevel.Fastest, leaveOpen: true))
        {
            for (var row = 0; row < height; row++)
            {
                deflate.WriteByte(0);
                deflate.Write(rgba, row * width * 4, width * 4);
            }
        }
        WriteChunk(file, "IDAT", pixels.GetBuffer().AsSpan(0, (int)pixels.Length));
        WriteChunk(file, "IEND", []);
    }

    private static void WriteChunk(Stream file, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        file.Write(number);
        var name = System.Text.Encoding.ASCII.GetBytes(type);
        file.Write(name);
        file.Write(data);
        var crc = Crc(Crc(uint.MaxValue, name), data);
        BinaryPrimitives.WriteUInt32BigEndian(number, ~crc);
        file.Write(number);
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }
        return crc;
    }

    private static void WriteWav(string path, BinkSound sound)
    {
        using var file = new BinaryWriter(File.Create(path));
        var bytes = sound.Samples.Length * 2;
        file.Write("RIFF"u8);
        file.Write(36 + bytes);
        file.Write("WAVEfmt "u8);
        file.Write(16);
        file.Write((short)1);
        file.Write((short)sound.Channels);
        file.Write(sound.SampleRate);
        file.Write(sound.SampleRate * sound.Channels * 2);
        file.Write((short)(sound.Channels * 2));
        file.Write((short)16);
        file.Write("data"u8);
        file.Write(bytes);
        foreach (var sample in sound.Samples) file.Write(sample);
    }
}
