var run = new Runner();
run.Test("WAV: PCM 8/16 bit, stereo, extensible, chunk listing", Tests.WavPcm);
run.Test("WAV: tolerated deviations are reported, strict mode rejects them", Tests.WavTolerances);
run.Test("WAV: IMA ADPCM hand vector, stereo layout, round trip, fact trim", Tests.ImaAdpcmDecoding);
run.Test("WAV: MS ADPCM hand vectors and invalid predictor", Tests.MsAdpcmDecoding);
run.Test("PCM conversion, resampling, signature sniffing", Tests.Conversions);
run.Test("MP3: streaming decode of synthetic silent frames and rewind", Tests.Mp3Streaming);
run.Test("Catalog: typed events, templates, redefinition, paths and diagnostics", Tests.CatalogFromGraph);
run.Test("Selection: RANDOM, round-robin, ALL, shifts, time of day, limits, RNG", Tests.Selection);
run.Test("Mixer: distance attenuation and equal-power panning", Tests.AttenuationAndPanning);
run.Test("Mixer: microphone placement and zoom volume", Tests.MicrophonePlacement);
run.Test("Mixer: priority stealing, limits and culling", Tests.VoiceStealingAndLimits);
run.Test("Mixer: loop seams, fade-in and fade-out", Tests.LoopSeamAndFades);
run.Test("Mixer: music streaming and crossfade", Tests.MusicCrossfade);
run.Test("Mixer: event voices with delay, attack, loops and decay", Tests.EventVoices);
run.Test("Mixer: two runs of one script are byte-identical", Tests.MixerDeterminism);
run.Test("Bink: header, audio tracks, frame index and rejection", Tests.BinkHeaders);

var contentDirectory = args.SkipWhile(value => value != "--content-dir").Skip(1).FirstOrDefault();
if (contentDirectory is not null) run.Test("Real data: every sound, speech and music file decodes", () => Tests.RealDecode(contentDirectory));
if (contentDirectory is not null) run.Test("Real data: every Bink movie header parses", () => Tests.RealMovies(contentDirectory));
if (contentDirectory is not null) run.Test("Real data: audio catalog", () => Tests.RealCatalog(contentDirectory));
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
            Console.Error.WriteLine($"{_failed} audio test(s) failed, {_passed} passed.");
            return 1;
        }
        Console.WriteLine("All audio tests passed.");
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
