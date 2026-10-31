using System.Buffers.Binary;
using System.Security.Cryptography;
using CnCNew.Content;
using CnCNew.Tests;
using CnCNew.Video;

var run = new Runner();
run.Test("Bit reader: least significant bit first, 32-bit alignment, overrun", Tests.BitReader);
run.Test("Header: audio tracks, frame index and rejection", Tests.Header);
run.Test("Container: frames, packets, what is refused", Tests.Container);
run.Test("Video: filled blocks, the plane order and the colours", Tests.FilledFrame);
run.Test("Video: DC-only intra blocks, skipped and moved blocks", Tests.IntraSkipAndMotion);
run.Test("Video: 16 x 16 blocks, doubled", Tests.ScaledBlocks);
run.Test("Video: an alpha plane comes first and reaches the picture", Tests.AlphaPlane);
run.Test("Video: a damaged frame is an error, never a crash", Tests.DamagedFrame);
run.Test("Player: frames come when they are due, the end ends it, a late frame is decoded all the same", Tests.PlayerClock);
run.Test("Audio: the inverse DCT equals its definition", Tests.InverseDct);
run.Test("Audio: a constant block, the overlap between blocks", Tests.AudioBlocks);

var contentDirectory = args.SkipWhile(value => value != "--content-dir").Skip(1).FirstOrDefault();
if (contentDirectory is not null) run.Test("Real data: every movie's header parses", () => Tests.RealHeaders(contentDirectory));
if (contentDirectory is not null) run.Test("Real data: a few movies decode twice to the same checksums", () => Tests.RealDecode(contentDirectory));
return run.Finish();

sealed class Runner
{
    private int _failed;
    private int _passed;

    public void Test(string name, Action test)
    {
        try
        {
            test();
            _passed++;
            Console.WriteLine($"  ok   {name}");
        }
        catch (Exception exception)
        {
            _failed++;
            Console.Error.WriteLine($"  FAIL {name}: {exception.Message}");
            Console.Error.WriteLine(exception.StackTrace);
        }
    }

    public int Finish()
    {
        if (_failed > 0)
        {
            Console.Error.WriteLine($"{_failed} video test(s) failed, {_passed} passed.");
            return 1;
        }
        Console.WriteLine("All video tests passed.");
        return 0;
    }
}

static class Check
{
    public static void That(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    public static void Near(double actual, double expected, double tolerance, string message)
    {
        if (!(Math.Abs(actual - expected) <= tolerance))
            throw new InvalidOperationException($"{message} (expected {expected} ± {tolerance}, got {actual})");
    }

    public static void Throws<T>(Action action, string messagePart) where T : Exception
    {
        try { action(); }
        catch (T exception) when (exception.Message.Contains(messagePart, StringComparison.OrdinalIgnoreCase)) { return; }
        catch (Exception exception) { throw new InvalidOperationException($"Expected {typeof(T).Name} containing '{messagePart}', got {exception.GetType().Name}: {exception.Message}"); }
        throw new InvalidOperationException($"Expected {typeof(T).Name} containing '{messagePart}', but nothing was thrown.");
    }
}

static class Pictures
{
    public static byte[] Rgba(BinkMovie movie)
    {
        var rgba = new byte[movie.Width * movie.Height * 4];
        movie.CopyRgba(rgba);
        return rgba;
    }

    public static int Red(BinkMovie movie, int x, int y) => Rgba(movie)[(y * movie.Width + x) * 4];

    /// <summary>The red of a neutral picture of luminance <paramref name="y"/>: BT.601 in limited range.</summary>
    public static int Grey(int y) => Math.Clamp((298 * (y - 16) + 128) >> 8, 0, 255);
}

static class Tests
{
    public static void BitReader()
    {
        byte[] data = [0b1010_0110, 0xFF, 0x00, 0x80, 0x12, 0x34, 0, 0, 0, 0, 0, 0, 0, 0];
        var bits = new BinkBitReader(data, 0, 6);
        Check.That(bits.Read(1) == 0, "The first bit is the lowest of the first byte.");
        Check.That(bits.Read(3) == 0b011, "A value keeps its order from the low bit.");
        Check.That(bits.ReadBit() == false, "Bit 4.");
        Check.That(bits.Read(3) == 0b101, "Up to the end of the byte.");
        Check.That(bits.Read(12) == 0x0FF, "A value across two bytes.");
        Check.That(bits.Position == 20, "The position counts bits.");
        bits.AlignTo32();
        Check.That(bits.Position == 32, "Alignment is to the next multiple of 32.");
        Check.That(bits.Read(16) == 0x3412 && bits.BitsLeft == 0, "The last word, read to the end.");
        var reader = bits;
        Check.That(reader.Read(1) == 0, "A peek at the very end stays inside the padding.");
        Check.Throws<InvalidDataException>(() => { var over = reader; over.Read(1); }, "ended early");
    }

    public static void Header()
    {
        var file = BinkFixtures.File(640, 480, [new byte[16], new byte[16], new byte[16]], [new byte[4], new byte[4], new byte[4]], audioFlags: 0x3000);
        var header = BinkHeader.Parse(file, file.Length);
        Check.That(header.Signature == "BIKi" && header.FrameCount == 3 && header.Width == 640 && header.Height == 480, "Signature, frames and size.");
        Check.Near(header.FramesPerSecond, 30, 0.001, "Frame rate numerator/denominator.");
        Check.Near(header.DurationSeconds, 0.1, 1e-9, "Duration.");
        Check.That(header.AudioTracks.Single() == new BinkAudioTrack(7, 44100, 2, false, true, 1234), "Audio track rate, stereo and DCT flags.");
        Check.That(header.KeyFrames == 1, "Key frames from bit 0 of the frame index.");
        var read = BinkHeader.Read(new MemoryStream(file));
        Check.That(read with { AudioTracks = header.AudioTracks } == header && read.AudioTracks.SequenceEqual(header.AudioTracks), "Stream reading gives the same header.");
        Check.Throws<NotSupportedException>(() => BinkHeader.Parse([.. "KB2j"u8, .. new byte[60]], 64), "Bink 2");
        Check.Throws<InvalidDataException>(() => BinkHeader.Parse(file, file.Length + 1), "announces");
        var broken = (byte[])file.Clone();
        BinaryPrimitives.WriteUInt32LittleEndian(broken.AsSpan(44 + 12 + 4), 0);
        Check.Throws<InvalidDataException>(() => BinkHeader.Parse(broken, broken.Length), "frame index");
    }

    public static void Container()
    {
        byte[][] video = [[1, 2, 3, 4], [5, 6, 7, 8, 9, 10, 11, 12]];
        byte[][] audio = [new byte[8], new byte[2]];
        var data = BinkFixtures.File(16, 16, video, audio);
        var file = BinkFile.Open(data);
        var first = file.Packets(0);
        var second = file.Packets(1);
        Check.That(first.AudioLength == 8 && first.VideoLength == 4, "Audio and video of the first frame.");
        Check.That(second.AudioLength == 0 && second.VideoLength == 8, "An audio packet under four bytes counts as none.");
        Check.That(file.IsKeyFrame(0) && !file.IsKeyFrame(1), "The key flag of the index.");
        Check.That(BinkFile.Open(new NonSeekingStream(data)).Packets(1) == file.Packets(1), "A stream that cannot seek reads the same.");
        Check.Throws<NotSupportedException>(() => BinkFile.Open(BinkFixtures.File(16, 16, video, revision: 'h')), "revision 'h'");
        Check.Throws<NotSupportedException>(() => BinkFile.Open(BinkFixtures.File(16, 16, video, flags: 0x00020000)), "flags");
        Check.Throws<NotSupportedException>(() => BinkFile.Open(BinkFixtures.File(16, 16, video, audio, audioFlags: 0x6000)), "RDFT");
        var lying = (byte[])data.Clone();
        var firstFrame = (int)(BinaryPrimitives.ReadUInt32LittleEndian(lying.AsSpan(44 + 12)) & ~1u);
        BinaryPrimitives.WriteUInt32LittleEndian(lying.AsSpan(firstFrame), 4096);
        Check.Throws<InvalidDataException>(() => BinkFile.Open(lying).Packets(0), "announces");
    }

    public static void FilledFrame()
    {
        var movie = new BinkMovie(BinkFile.Open(BinkFixtures.File(16, 16, [BinkFixtures.FilledFrame([16, 235, 128, 16], 128, 128), BinkFixtures.FilledFrame([81, 81, 81, 81], 240, 90)])));
        Check.That(movie.DecodeNextFrame(), "The first frame.");
        var rgba = Pictures.Rgba(movie);
        Check.That(rgba[0] == 0 && rgba[1] == 0 && rgba[2] == 0 && rgba[3] == 255, "Y 16 is black, without alpha opaque.");
        Check.That(rgba[(0 * 16 + 8) * 4] == 255, "Y 235 is white, top right block.");
        Check.That(rgba[(8 * 16 + 0) * 4] == 130, "Y 128 is a middle grey, bottom left block.");
        Check.That(movie.DecodeNextFrame(), "The second frame.");
        rgba = Pictures.Rgba(movie);
        // BT.601 limited range; the first chroma plane of the stream is V.
        Check.That(rgba[0] == 255 && rgba[1] == 0 && rgba[2] == 0, $"V 240, U 90, Y 81 is pure red, got {rgba[0]},{rgba[1]},{rgba[2]}.");
        Check.That(!movie.DecodeNextFrame() && movie.IsAtEnd && movie.FramesDecoded == 2, "The end of the movie.");
        Check.That(Pictures.Rgba(movie)[0] == 255, "The last picture stays.");
    }

    public static void IntraSkipAndMotion()
    {
        // Frame 1: two intra blocks with a DC value alone over two filled ones. After the matrix (11 fractional bits) and the transform (8) the DC is the pixel.
        var quant = BinkTables.IntraQuant[0];
        var bits = new BitWriter();
        bits.Put(0, 32);
        var plane = new PlaneWriter(bits, 2);
        plane.Bundles();
        var dc = (200 << 19) / quant;
        var half = (100 << 19) / quant;
        var black = (16 << 19) / quant + 1;
        plane.Row([BinkFixtures.Intra, BinkFixtures.Intra], intraDc: [dc, half]);
        bits.Put(0, 4).Put(0, 4); // no coefficient bits, quantiser 0
        bits.Put(0, 4).Put(0, 4);
        plane.Row([BinkFixtures.Intra, BinkFixtures.Intra], intraDc: [black, black]);
        bits.Put(0, 4).Put(0, 4);
        bits.Put(0, 4).Put(0, 4);
        bits.Align32();
        BinkFixtures.ChromaFill(bits, 128);
        bits.Align32();
        BinkFixtures.ChromaFill(bits, 128);
        bits.Align32();

        // Frame 2: a block moved onto itself and one skipped, then the left block copied from one block up.
        var next = new BitWriter();
        next.Put(0, 32);
        plane = new PlaneWriter(next, 2);
        plane.Bundles();
        plane.Row([BinkFixtures.Motion, BinkFixtures.Skip], xOffsets: [0], yOffsets: [0]);
        plane.Row([BinkFixtures.Motion, BinkFixtures.Skip], xOffsets: [0], yOffsets: [-8]);
        next.Align32();
        BinkFixtures.ChromaFill(next, 128);
        next.Align32();
        BinkFixtures.ChromaFill(next, 128);
        next.Align32();

        var movie = new BinkMovie(BinkFile.Open(BinkFixtures.File(16, 16, [bits.ToArray(), next.ToArray()])));
        movie.DecodeNextFrame();
        var left = Pictures.Red(movie, 3, 3);
        var right = Pictures.Red(movie, 12, 3);
        Check.Near(left, Pictures.Grey(200), 2, "The left block's DC.");
        Check.Near(right, Pictures.Grey(100), 2, "The right block's DC.");
        Check.That(Pictures.Red(movie, 3, 12) == 0, "The bottom row is black.");
        movie.DecodeNextFrame();
        Check.That(Pictures.Red(movie, 3, 3) == left && Pictures.Red(movie, 12, 3) == right, "A skipped block keeps the last picture.");
        Check.That(Pictures.Red(movie, 3, 12) == left, "A motion block copies from the last picture.");
        Check.That(Pictures.Red(movie, 12, 12) == 0, "A skipped block keeps the black.");
    }

    public static void ScaledBlocks()
    {
        // A 16 x 16 frame of one scaled pattern block: the pattern 0xAA doubles to stripes two pixels wide.
        var bits = new BitWriter();
        bits.Put(0, 32);
        var plane = new PlaneWriter(bits, 2);
        plane.Bundles();
        plane.Row([BinkFixtures.Scaled], subTypes: [BinkFixtures.Pattern], colors: [16, 235], patterns: [0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA, 0xAA]);
        plane.Row([BinkFixtures.Scaled]);
        bits.Align32();
        BinkFixtures.ChromaFill(bits, 128);
        bits.Align32();
        BinkFixtures.ChromaFill(bits, 128);
        bits.Align32();
        var movie = new BinkMovie(BinkFile.Open(BinkFixtures.File(16, 16, [bits.ToArray()])));
        movie.DecodeNextFrame();
        Check.That(Pictures.Red(movie, 0, 0) == 0 && Pictures.Red(movie, 1, 15) == 0, "Bit 0 of the pattern is the first colour, doubled.");
        Check.That(Pictures.Red(movie, 2, 0) == 255 && Pictures.Red(movie, 3, 15) == 255, "Bit 1 is the second colour, doubled.");
        Check.That(Pictures.Red(movie, 14, 7) == 255, "The stripes reach the last column.");

        // A chroma plane of one 8 x 8 block is a 16 x 16 area of the picture; the U plane decides the blue channel.
        var red = new BinkMovie(BinkFile.Open(BinkFixtures.File(16, 16, [BinkFixtures.FilledFrame([128, 128, 128, 128], 128, 240)])));
        red.DecodeNextFrame();
        Check.That(Pictures.Rgba(red)[2] == 255, "U 240 raises blue.");
    }

    public static void AlphaPlane()
    {
        var bits = new BitWriter();
        bits.Put(0, 32);
        BinkFixtures.LumaFill(bits, [10, 20, 30, 40]);
        bits.Align32();
        bits.Put(0, 32);
        BinkFixtures.LumaFill(bits, [128, 128, 128, 128]);
        bits.Align32();
        BinkFixtures.ChromaFill(bits, 128);
        bits.Align32();
        BinkFixtures.ChromaFill(bits, 128);
        bits.Align32();
        var movie = new BinkMovie(BinkFile.Open(BinkFixtures.File(16, 16, [bits.ToArray()], flags: BinkFile.AlphaFlag)));
        Check.That(movie.HasAlpha, "The flag.");
        movie.DecodeNextFrame();
        var rgba = Pictures.Rgba(movie);
        Check.That(rgba[3] == 10 && rgba[(8 * 4) + 3] == 20 && rgba[((8 * 16) * 4) + 3] == 30 && rgba[((8 * 16 + 8) * 4) + 3] == 40, "The alpha of each block.");
    }

    public static void DamagedFrame()
    {
        var frame = BinkFixtures.FilledFrame([128, 128, 128, 128], 128, 128);
        var movie = new BinkMovie(BinkFile.Open(BinkFixtures.File(16, 16, [frame[..24]])));
        Check.Throws<InvalidDataException>(() => movie.DecodeNextFrame(), "ended early");

        var bits = new BitWriter();
        bits.Put(0, 32);
        var plane = new PlaneWriter(bits, 2);
        plane.Bundles();
        plane.Row([BinkFixtures.Motion, BinkFixtures.Motion], xOffsets: [-15, -15], yOffsets: [-15, -15]);
        bits.Put(0, 256);
        var moved = new BinkMovie(BinkFile.Open(BinkFixtures.File(16, 16, [bits.ToArray()])));
        Check.Throws<InvalidDataException>(() => moved.DecodeNextFrame(), "leaves its picture");

        var unknown = new BitWriter();
        unknown.Put(0, 32);
        plane = new PlaneWriter(unknown, 2);
        plane.Bundles();
        plane.Row([11, 11]);
        unknown.Put(0, 256);
        var strange = new BinkMovie(BinkFile.Open(BinkFixtures.File(16, 16, [unknown.ToArray()])));
        Check.Throws<InvalidDataException>(() => strange.DecodeNextFrame(), "Unknown Bink block type");
    }

    public static void PlayerClock()
    {
        var player = new MoviePlayer(new BinkMovie(BinkFile.Open(BinkFixtures.FilledMovie(30))));
        Check.That(player.Frame == 1 && player.Version == 1, "The first picture stands from the start.");
        Check.Near(player.DurationSeconds, 1.0, 1e-9, "Thirty frames at 30 a second.");
        player.Advance(0.02);
        Check.That(player.Frame == 1 && player.Version == 1, "Nothing is due before a frame's time is over.");
        player.Advance(0.02);
        Check.That(player.Frame == 2 && player.Version == 2, "The second frame at 1/30 s.");
        var first = player.Rgba[0];
        player.Advance(0.2);
        Check.That(player.Frame == 8 && player.Version == 3 && player.Rgba[0] > first, "A late frame is decoded, one picture shows the newest.");
        player.Advance(0.7);
        Check.That(!player.IsFinished && player.Frame == 29, "Just before the end.");
        player.Advance(0.07);
        Check.That(player.IsFinished && player.Failure is null, "A frame stands for its whole time, then the movie ends.");
        var stopped = new MoviePlayer(new BinkMovie(BinkFile.Open(BinkFixtures.FilledMovie(30))));
        stopped.Stop();
        stopped.Advance(1);
        Check.That(stopped.IsFinished && stopped.Frame == 1, "A stopped movie decodes no more.");
        var frame = BinkFixtures.FilledFrame([128, 128, 128, 128], 128, 128);
        var damaged = new MoviePlayer(new BinkMovie(BinkFile.Open(BinkFixtures.File(16, 16, [frame, frame[..24], frame]))));
        damaged.Advance(0.04);
        Check.That(damaged.IsFinished && damaged.Failure!.Contains("ended early"), "A frame that cannot be decoded ends the movie with its reason.");
    }

    public static void InverseDct()
    {
        foreach (var length in new[] { 8, 512, 2048 })
        {
            var random = new Random(length);
            var spectrum = new float[length];
            for (var index = 0; index < length; index++) spectrum[index] = (float)(random.NextDouble() * 2 - 1);
            var result = new float[length];
            new BinkDct(length).Inverse(spectrum, result);
            for (var sample = 0; sample < length; sample += Math.Max(1, length / 64))
            {
                var sum = spectrum[0] / 2.0;
                for (var k = 1; k < length; k++) sum += spectrum[k] * Math.Cos(Math.PI * k * (sample + 0.5) / length);
                Check.Near(result[sample], 2.0 / length * sum, 2e-4, $"Sample {sample} of a transform of {length}.");
            }
        }
    }

    public static void AudioBlocks()
    {
        const int frameLength = 2048;
        const int overlap = 128;
        // A block of coefficient 0 alone is constant: the file's value times 2 / sqrt(N) in 16-bit samples.
        const double expected = 8000;
        var value = (float)(expected * Math.Sqrt(frameLength) / 2);
        var packet = BinkFixtures.AudioPacket(2, frameLength, [(value, 0f), (0f, 0f)]);
        var file = BinkFixtures.File(16, 16, [BinkFixtures.FilledFrame([16, 16, 16, 16], 128, 128)], [packet]);
        var sound = new BinkMovie(BinkFile.Open(file)).DecodeSound();
        Check.That(sound is { SampleRate: 44100, Channels: 2 }, "The track's rate and channels.");
        var samples = sound!.Samples;
        Check.That(samples.Length == 2 * 2 * (frameLength - overlap), $"Two blocks of 1,920 stereo samples, got {samples.Length}.");
        Check.Near(samples[0], expected, 2, "The first block's level, left.");
        Check.Near(samples[1], expected, 2, "The first block's level, right.");
        Check.Near(samples[3838], expected, 2, "The level holds to the block's end.");
        // The second block's first samples fade from the end of the first to its own silence, the channels a half step apart.
        var seam = 2 * (frameLength - overlap);
        Check.Near(samples[seam], expected, 2, "The fade starts at the old level.");
        Check.Near(samples[seam + 2 * overlap - 2], expected / overlap, 3, "It ends a step above the new level.");
        Check.That(samples[seam + 2 * overlap] == 0, "Behind the overlap the block's own silence.");
        Check.That(samples[seam + 2] < samples[seam] && samples[seam + 3] < samples[seam + 1], "The fade falls on both channels.");
        Check.Throws<InvalidDataException>(() => new BinkMovie(BinkFile.Open(BinkFixtures.File(16, 16, [BinkFixtures.FilledFrame([16, 16, 16, 16], 128, 128)], [packet[..24]]))).DecodeSound(), "ends early");
    }

    public static void RealHeaders(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var movies = content.EnumerateFiles().Where(path => path.EndsWith(".bik", StringComparison.OrdinalIgnoreCase)).ToArray();
        Check.That(movies.Length > 0, "No movie in the content.");
        var totalSeconds = 0.0;
        foreach (var path in movies)
        {
            using var stream = content.OpenRead(path);
            var header = BinkHeader.Read(stream);
            totalSeconds += header.DurationSeconds;
            Check.That(header.Revision == 'i', $"{path}: the game ships revision i.");
        }
        Console.WriteLine($"       {movies.Length} Bink movies, {totalSeconds / 60:0.0} min total");
    }

    public static void RealDecode(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        foreach (var path in new[] { "Data/Movies/VS_small.bik", "Data/Movies/GC_Background.bik", "Data/English/Movies/EA_LOGO640.BIK", "Data/English/Movies/Comp_TankGen_000.bik" })
        {
            var first = Checksums(content, path);
            var second = Checksums(content, path);
            Check.That(first == second, $"{path}: two runs differ.");
            Console.WriteLine($"       {path}: {first}");
        }
    }

    private static string Checksums(ContentFileSystem content, string path)
    {
        using var stream = content.OpenRead(path);
        var movie = new BinkMovie(BinkFile.Open(stream));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        while (movie.DecodeNextFrame()) movie.WritePlanes(hash.AppendData);
        var sound = movie.DecodeSound();
        if (sound is not null)
        {
            var bytes = new byte[sound.Samples.Length * 2];
            Buffer.BlockCopy(sound.Samples, 0, bytes, 0, bytes.Length);
            hash.AppendData(bytes);
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }
}

/// <summary>A stream that says it cannot seek.</summary>
sealed class NonSeekingStream(byte[] data) : MemoryStream(data)
{
    public override bool CanSeek => false;
}
