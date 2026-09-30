using System.Diagnostics;
using CnCNew.Audio;
using CnCNew.Audio.Catalog;
using CnCNew.Audio.Mixing;
using CnCNew.Audio.Mp3;
using CnCNew.Audio.Selection;
using CnCNew.Content;
using CnCNew.Data.Compilation;
using CnCNew.Data.Registry;

file sealed class ContentSource(ContentFileSystem content) : IDefinitionSource
{
    public IEnumerable<string> EnumerateIniFiles() => content.EnumerateFiles().Where(path => path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase));
    public Stream OpenRead(string virtualPath) => content.OpenRead(virtualPath);
    public bool Exists(string virtualPath) => content.Exists(virtualPath);
}

static partial class Tests
{
    public static void RealDecode(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var files = content.EnumerateFiles("Data/Audio")
            .Where(path => path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var formats = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var diagnostics = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var failures = new List<string>();
        var seconds = new SortedDictionary<string, double>(StringComparer.Ordinal);
        long bytes = 0;
        var watch = Stopwatch.StartNew();
        foreach (var path in files)
        {
            var group = GroupOf(path);
            try
            {
                bytes += content.GetEntry(path).Bytes;
                double duration;
                string format;
                if (path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
                {
                    using var stream = new Mp3PcmStream(content.OpenRead(path));
                    var chunk = new float[16384];
                    long samples = 0;
                    int read;
                    while ((read = stream.Read(chunk)) > 0) samples += read;
                    duration = (double)samples / stream.Channels / stream.SampleRate;
                    format = $"MPEG audio {(stream.Channels == 1 ? "mono" : "stereo")} {stream.SampleRate} Hz";
                    Check.That(duration > 1, $"{path} decodes to at least one second ({duration:0.00}).");
                }
                else
                {
                    using var stream = content.OpenRead(path);
                    var decoded = AudioFileDecoder.Decode(stream, path);
                    duration = decoded.Pcm.DurationSeconds;
                    format = decoded.FormatDescription;
                    foreach (var diagnostic in decoded.Diagnostics)
                        diagnostics[diagnostic.Code] = diagnostics.GetValueOrDefault(diagnostic.Code) + 1;
                    Check.That(decoded.Pcm.FrameCount > 0, $"{path} has audio frames.");
                }
                var key = $"{group}: {format}";
                formats[key] = formats.GetValueOrDefault(key) + 1;
                seconds[group] = seconds.GetValueOrDefault(group) + duration;
            }
            catch (Exception exception) when (exception is InvalidDataException or NotSupportedException or InvalidOperationException)
            {
                failures.Add($"{path}: {exception.Message}");
            }
        }
        watch.Stop();

        Console.WriteLine($"       decoded {files.Length - failures.Count}/{files.Length} audio files ({bytes / 1048576.0:0.0} MiB) in {watch.Elapsed.TotalSeconds:0.0} s");
        foreach (var (group, total) in seconds)
            Console.WriteLine($"         {group}: {total / 60:0.0} min decoded");
        var totalSeconds = seconds.Values.Sum();
        Console.WriteLine($"         throughput {totalSeconds / watch.Elapsed.TotalSeconds:0} × real time");
        foreach (var (format, count) in formats)
            Console.WriteLine($"         {count,5} × {format}");
        foreach (var (code, count) in diagnostics)
            Console.WriteLine($"         tolerance {code}: {count}");
        foreach (var failure in failures.Take(20))
            Console.WriteLine($"         FAILED {failure}");
        Check.That(failures.Count == 0, $"{failures.Count} audio file(s) failed to decode.");
    }

    public static void RealMovies(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var movies = content.EnumerateFiles().Where(path => path.EndsWith(".bik", StringComparison.OrdinalIgnoreCase)).ToArray();
        var totalSeconds = 0.0;
        foreach (var path in movies)
        {
            using var stream = content.OpenRead(path);
            var header = CnCNew.Audio.Movies.BinkHeader.Read(stream);
            totalSeconds += header.DurationSeconds;
            var audio = header.AudioTracks.IsEmpty ? "no audio" : string.Join(", ", header.AudioTracks.Select(track => $"{track.SampleRate} Hz {(track.Channels == 1 ? "mono" : "stereo")}{(track.UsesDct ? " DCT" : " RDFT")}"));
            Console.WriteLine($"         {path}: {header.Signature} {header.Width}x{header.Height} {header.FrameCount} frames @ {header.FramesPerSecond:0.###} fps = {header.DurationSeconds:0.0} s, {header.KeyFrames} key frames, {audio}");
        }
        Console.WriteLine($"       {movies.Length} Bink movies, {totalSeconds / 60:0.0} min total");
    }

    public static void RealCatalog(string directory)
    {
        var content = ContentFileSystem.Open(directory);
        var options = DefinitionCompilerOptions.Audit with { StrictLoadPlan = true, MapOverlays = MapOverlayMode.Exclude };
        var graph = DefinitionCompiler.Compile(new ContentSource(content), ModuleRegistry.Default, options);
        var catalog = AudioCatalog.FromGraph(graph, content.Exists);
        foreach (var line in catalog.Report.ToLines())
            Console.WriteLine("         " + line);
        var again = AudioCatalog.FromGraph(graph, content.Exists);
        Check.That(catalog.Report.ToLines().SequenceEqual(again.Report.ToLines()), "The catalog report is deterministic.");
        var report = catalog.Report;
        Check.That(report.SoundEffects > 1000 && report.MusicTracks > 50 && report.DialogEvents > 2000, "The retail catalog has all three kinds.");
        Check.That(report.DiagnosticCounts.IsEmpty, "The retail audio INI types without unknown keys or invalid values.");
        Check.That(report.UnresolvedFiles.Length * 50 < report.FileReferences, "Fewer than 2 % of the file references are unresolved.");
        Check.That(report.UnresolvedMiscAudioHooks.IsEmpty, "Every MiscAudio hook names an existing event.");

        RealMixerSmoke(content, catalog);
    }

    /// <summary>Plays real events and music through the mixer twice and compares the rendered bytes.</summary>
    private static void RealMixerSmoke(ContentFileSystem content, AudioCatalog catalog)
    {
        var events = catalog.SoundEffects.Where(value => value.Sounds.Any(file => file.IsResolved)).Where((_, index) => index % 25 == 0).Take(40).ToArray();
        var tracks = catalog.MusicTracks.Where(value => value.File is { IsResolved: true }).Take(2).ToArray();
        Check.That(events.Length == 40 && tracks.Length == 2, "Enough resolved events and tracks for the smoke test.");
        (string Hash, MixerStatistics Statistics, double Seconds, int Cached) Run()
        {
            var cache = new SoundCache(content.OpenRead);
            var rng = new SeededRng(2026);
            using var mixer = new SoftwareMixer(MixerOptions.FromSettings(catalog.Settings, seed: 5));
            using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            var block = new float[2 * 1024];
            var bytes = new byte[block.Length * 4];
            var watch = Stopwatch.StartNew();
            var blocks = mixer.SampleRate * 6 / 1024;
            for (var step = 0; step < blocks; step++)
            {
                if (step == 0) mixer.PlayMusic(AudioFileDecoder.OpenStream(content.OpenRead(tracks[0].File!.VirtualPath!)));
                if (step == blocks / 2) mixer.PlayMusic(AudioFileDecoder.OpenStream(content.OpenRead(tracks[1].File!.VirtualPath!)), crossfadeSeconds: 1f);
                if (step % 6 == 0 && step / 6 < events.Length)
                {
                    var info = events[step / 6];
                    var history = new SoundSelectionState();
                    if (SoundSelector.Pick(info, rng, history) is { } plan)
                    {
                        var position = new System.Numerics.Vector3(rng.NextFloat(-300, 300), rng.NextFloat(-300, 300), 0);
                        mixer.Play(PlayRequest.ForEvent(plan, history, catalog.Settings, cache.GetOrLoad, position));
                    }
                }
                mixer.Render(block);
                for (var index = 0; index < block.Length; index++)
                    System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(index * 4), block[index]);
                hash.AppendData(bytes);
            }
            watch.Stop();
            Check.That(cache.Failures.IsEmpty, "Every loaded catalog sound decodes.");
            return (Convert.ToHexString(hash.GetHashAndReset())[..16], mixer.Statistics, watch.Elapsed.TotalSeconds, cache.Count);
        }
        var first = Run();
        var second = Run();
        Check.That(first.Hash == second.Hash, "Two real-data mixer runs render identical bytes.");
        var stats = first.Statistics;
        Console.WriteLine($"         mixer smoke: {stats.RenderedFrames / (double)MixerOptions.FromSettings(catalog.Settings).SampleRate:0.0} s rendered in {first.Seconds:0.00} s (incl. decoding {first.Cached} sounds), "
            + $"{stats.Started} started, {stats.Finished} finished, {stats.Stolen} stolen, {stats.RejectedDistance + stats.RejectedLimit + stats.RejectedNoChannel + stats.RejectedMuted} rejected, {stats.ClippedSamples} clipped samples, hash {first.Hash}");
    }

    private static string GroupOf(string path)
    {
        var parts = path.Split('/');
        return parts.Length > 3 ? $"{parts[2]}{(parts.Length > 4 ? "/" + parts[3] : string.Empty)}" : path;
    }
}
