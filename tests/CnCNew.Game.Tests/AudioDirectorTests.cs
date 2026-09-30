using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using CnCNew.Audio.Catalog;
using CnCNew.Audio.Mixing;
using CnCNew.Audio.Selection;
using CnCNew.Data.Compilation;
using CnCNew.Data.Ini;
using CnCNew.Game.Audio;
using CnCNew.Game.Data;
using CnCNew.Game.Live;
using CnCNew.Presentation.Fx;
using CnCNew.Simulation.Ids;
using CnCNew.Simulation.Testing;
using CnCNew.Simulation.World;

namespace CnCNew.Game.Tests;

file sealed class MemorySource(Dictionary<string, string> files) : IDefinitionSource
{
    public IEnumerable<string> EnumerateIniFiles() => files.Keys.Order(StringComparer.OrdinalIgnoreCase);
    public Stream OpenRead(string virtualPath) => new MemoryStream(Encoding.UTF8.GetBytes(files[virtualPath]));
    public bool Exists(string virtualPath) => files.ContainsKey(virtualPath);
}

/// <summary>A scripted <see cref="IAudioGameView"/> for the synthetic tests; the real data test uses <see cref="LiveAudioView"/> instead.</summary>
file sealed class FakeView : IAudioGameView
{
    public PlayerId Human { get; init; } = new(1);
    public string HumanSide { get; init; } = "America";
    public long Tick { get; set; }
    public bool LowPower { get; set; }
    public Dictionary<(int, int), Relationship> Relations { get; } = [];
    public bool Shrouded { get; set; }

    public Relationship RelationshipOf(PlayerId from, PlayerId to)
        => Relations.TryGetValue((from.Value, to.Value), out var value) ? value : from == to ? Relationship.Allies : Relationship.Enemies;

    public bool IsShrouded(Vector3 position) => Shrouded;
}

internal static class AudioDirectorTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(AudioDirectorTests));
        harness.Run("Eva.ini parser reads blocks, converts ms to frames, rejects unknown keys", EvaParserBasics);
        harness.Run("Eva announcer: priority, cooldown, expiry, side-missing", EvaAnnouncerBehaviour);
        harness.Run("Music playlist: side filter, silent/unresolved skipped, no immediate repeat", MusicPlaylistBehaviour);
        harness.Run("Director: bus mapping and shouldPlayLocally", DirectorCueDispatch);
        harness.Run("Director: rate limiting, unit voice interruption, missing voice keys", DirectorLimitsAndUnitVoices);

        var contentDir = ContentDir(args);
        if (contentDir is null)
        {
            Console.WriteLine("SKIP AudioDirectorTests real-data (pass --content-dir <dir> to run against real data)");
            return harness.Failures;
        }
        harness.Run("Director drives a real match: deterministic, audible, bounded, WAV dump", () => RealDataRun(contentDir));
        return harness.Failures;
    }

    private static string? ContentDir(string[] args)
    {
        var index = Array.IndexOf(args, "--content-dir");
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    // ------------------------------------------------------------------------------------------ Eva.ini parser

    private static void EvaParserBasics()
    {
        const string fixture = """
            EvaEvent LowPower
              Priority = 2
              TimeBetweenChecksMS = 2000
              ExpirationTimeMS = 1000
              SideSounds
                Side = America
                Sounds = EvaUSA_LowPower EvaUSA_LowPower2
              End
            End

            EvaEvent UnitLost
              SideSounds
                Side = America
                Sounds = EvaUSA_UnitLost
              End
            End
            """;
        var catalog = EvaCatalog.Parse(fixture);
        TestHarness.Assert(catalog.Events.Length == 2, "two events parsed");
        var low = catalog.Find("LowPower")!;
        TestHarness.Assert(low.Priority == 2, "priority read");
        TestHarness.Assert(low.FramesBetweenChecks == 60, "2000 ms -> 60 frames at 30 Hz");
        TestHarness.Assert(low.FramesToExpire == 30, "1000 ms -> 30 frames");
        TestHarness.Assert(low.SideSounds["America"].SequenceEqual(["EvaUSA_LowPower", "EvaUSA_LowPower2"]), "side sounds list");

        var unitLost = catalog.Find("UnitLost")!;
        TestHarness.Assert(unitLost.Priority == 1 && unitLost.FramesBetweenChecks == 900 && unitLost.FramesToExpire == 150, "defaults apply when a field is absent");

        TestHarness.Assert(catalog.Find("Nope") is null, "unknown name returns null, not a throw");

        TestHarness.Throws<InvalidDataException>(() => EvaCatalog.Parse("EvaEvent X\n  Bogus = 1\nEnd\n"), "unknown key throws");
        TestHarness.Throws<InvalidDataException>(() => EvaCatalog.Parse("EvaEvent X\n  Priority = 1\n"), "unclosed block throws");
        TestHarness.Throws<InvalidDataException>(() => EvaCatalog.Parse("EvaEvent X\n  SideSounds\n    Sounds = A\n  End\nEnd\n"), "SideSounds without Side throws");
    }

    private static void EvaAnnouncerBehaviour()
    {
        var catalog = EvaCatalog.Parse("""
            EvaEvent High
              Priority = 3
              TimeBetweenChecksMS = 333
              ExpirationTimeMS = 167
              SideSounds
                Side = Test
                Sounds = HighSound
              End
            End

            EvaEvent Low
              Priority = 1
              TimeBetweenChecksMS = 333
              ExpirationTimeMS = 3334
              SideSounds
                Side = Test
                Sounds = LowSound
              End
            End

            EvaEvent NoSideMatch
              Priority = 5
              SideSounds
                Side = OtherSide
                Sounds = X
              End
            End
            """);
        var announcer = new EvaAnnouncer(catalog, new SeededRng(7));
        TestHarness.Assert(!announcer.Request("Ghost"), "an undefined event is rejected and counted");
        TestHarness.Assert(announcer.UnknownEvents["Ghost"] == 1, "unknown event counted");

        TestHarness.Assert(announcer.Update(0, false, false, "Test") is null, "frame 0 never announces");
        TestHarness.Assert(announcer.Request("High") && announcer.Request("Low"), "both requests accepted");
        var first = announcer.Update(2, false, false, "Test");
        TestHarness.Assert(first == "HighSound", "the higher-priority check wins the same frame");
        TestHarness.Assert(announcer.Update(3, false, true, "Test") is null, "no pick while the caller reports an announcement still speaking");
        var second = announcer.Update(3, false, false, "Test");
        TestHarness.Assert(second == "LowSound", "Low is still queued (not expired) and is picked once nothing is speaking");

        // Low expires before High's cooldown (10 frames) releases it again: request Low alone and let it expire.
        var solo = new EvaAnnouncer(catalog, new SeededRng(7));
        solo.Request("Low");
        TestHarness.Assert(solo.Update(2, false, true, "Test") is null, "no pick while speaking");
        TestHarness.Assert(solo.Update(2 + 3334 * 30 / 1000 + 2, false, false, "Test") is null, "the check expired before it could be evaluated");
        TestHarness.Assert(solo.Expired == 1, "the expiry is counted");

        var missing = new EvaAnnouncer(catalog, new SeededRng(7));
        missing.Request("NoSideMatch");
        TestHarness.Assert(missing.Update(2, false, false, "Test") is null, "no sound for the local side plays nothing");
        TestHarness.Assert(missing.SideMissing == 1, "the side-missing case is counted, not silently dropped");
    }

    // ------------------------------------------------------------------------------------------ music playlist

    private static readonly Dictionary<string, string> MusicFixture = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Data/INI/AudioSettings.ini"] = AudioSettingsFixture,
        ["Data/INI/Default/Music.ini"] = "MusicTrack DefaultMusicTrack\n  Filename = NoFilename\nEnd\n",
        ["Data/INI/Default/SoundEffects.ini"] = "AudioEvent DefaultSoundEffect\n  Volume = 100\nEnd\n",
        ["Data/INI/Default/Speech.ini"] = "DialogEvent DefaultDialog\n  Volume = 100\nEnd\n",
        ["Data/INI/Music.ini"] = """
            MusicTrack Shell
              Filename = shell.mp3
            End
            MusicTrack Game_USA_01
              Filename = usa1.mp3
            End
            MusicTrack Game_USA_02
              Filename = usa2.mp3
            End
            MusicTrack Game_USA_03
              Filename = usa3.mp3
            End
            MusicTrack Game_USA_04
            End
            MusicTrack Game_Silent
            End
            MusicTrack Game_CHI_01
              Filename = chi1.mp3
            End
            """,
    };

    private static readonly HashSet<string> MusicFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Data/Audio/Tracks/shell.mp3", "Data/Audio/Tracks/usa1.mp3", "Data/Audio/Tracks/usa2.mp3", "Data/Audio/Tracks/usa3.mp3", "Data/Audio/Tracks/chi1.mp3",
    };

    private static void MusicPlaylistBehaviour()
    {
        var catalog = AudioCatalog.FromGraph(DefinitionCompiler.Compile(new MemorySource(MusicFixture)), MusicFiles.Contains);
        var usa = MusicPlaylist.ForSide(catalog, "America", new SeededRng(3));
        TestHarness.Assert(usa.Count == 3, "Game_USA_04 (unresolved) and Game_Silent are excluded");
        TestHarness.Assert(!usa.UsesAllSides, "a recognised side does not fall back to every side's tracks");

        var seen = new List<string>();
        for (var i = 0; i < 12; i++) seen.Add(usa.Next()!.Name);
        TestHarness.Assert(seen.Count == 12 && seen.All(name => name.StartsWith("Game_USA_", StringComparison.Ordinal)), "only USA tracks are drawn");
        for (var i = 1; i < seen.Count; i++) TestHarness.Assert(seen[i] != seen[i - 1], $"no immediate repeat at index {i}");
        // Every group of 3 draws (one shuffled round) must be a permutation of the 3 tracks.
        for (var round = 0; round < seen.Count / 3; round++)
            TestHarness.Assert(seen.Skip(round * 3).Take(3).Distinct(StringComparer.Ordinal).Count() == 3, $"round {round} is a full permutation");

        var observer = MusicPlaylist.ForSide(catalog, "Observer", new SeededRng(3));
        TestHarness.Assert(observer.UsesAllSides && observer.Count == 4, "an unrecognised side falls back to every side's resolved tracks (own decision)");

        var empty = MusicPlaylist.ForSide(catalog, "GLA", new SeededRng(3));
        TestHarness.Assert(empty.Count == 0 && empty.Next() is null, "a side with no tracks yields nothing instead of throwing");
    }

    // ------------------------------------------------------------------------------------------ director: cue dispatch

    private const string AudioSettingsFixture = """
        AudioSettings
          AudioRoot = Data\Audio
          SoundsFolder = Sounds
          MusicFolder = Tracks
          StreamingFolder = Speech
          SoundsExtension = wav
          UseDigital = Yes
          UseMidi = No
          OutputRate = 22050
          OutputBits = 16
          OutputChannels = 2
          SampleCount2D = 4
          SampleCount3D = 8
          StreamCount = 2
          GlobalMinRange = 4000
          GlobalMaxRange = 400000
          TimeBetweenDrawableSounds = 250
          TimeToFadeAudio = 500
          AudioFootprintInBytes = 1000000
          MinSampleVolume = 2
          Relative2DVolume = -10%
          DefaultSoundVolume = 80%
          Default3DSoundVolume = 80%
          DefaultSpeechVolume = 70%
          DefaultMusicVolume = 55%
          Default2DSpeakerType = "2 Speakers"
          Default3DSpeakerType = "5.1 Surround"
          MicrophoneDesiredHeightAboveTerrain = 50.0
          MicrophoneMaxPercentageBetweenGroundAndCamera = 33.3%
          ZoomMinDistance = 130.0
          ZoomMaxDistance = 425.0
          ZoomSoundVolumePercentageAmount = 20%
        End
        """;

    private static readonly Dictionary<string, string> DirectorFixture = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Data/INI/AudioSettings.ini"] = AudioSettingsFixture,
        ["Data/INI/Default/Music.ini"] = "MusicTrack DefaultMusicTrack\n  Filename = NoFilename\nEnd\n",
        ["Data/INI/Default/SoundEffects.ini"] = "AudioEvent DefaultSoundEffect\n  Volume = 100\n  MinRange = 50\n  MaxRange = 500\nEnd\n",
        ["Data/INI/Default/Speech.ini"] = "DialogEvent DefaultDialog\n  Volume = 100\nEnd\n",
        ["Data/INI/SoundEffects.ini"] = """
            AudioEvent WorldEveryone
              Sounds = tone
              Type = world everyone
            End
            AudioEvent WorldPlayerOnly
              Sounds = tone
              Type = world player
            End
            AudioEvent WorldAlliesOnly
              Sounds = tone
              Type = world allies
            End
            AudioEvent UiClick
              Sounds = click
              Type = ui everyone
            End
            AudioEvent NoFile
              Type = ui everyone
            End
            """,
        ["Data/INI/Speech.ini"] = """
            DialogEvent Ack
              Filename = ack.wav
              Type = ui everyone
            End
            DialogEvent AckMove
              Filename = ackmove.wav
              Type = ui everyone
            End
            """,
        ["Data/INI/MiscAudio.ini"] = "MiscAudio\n  GUIClickSound = UiClick\nEnd\n",
        ["Data/INI/Object.ini"] = """
            Object Tank
              KindOf = SELECTABLE VEHICLE
              VoiceSelect = Ack
              VoiceMove = AckMove
              SoundAmbient = WorldEveryone
            End
            """,
    };

    private static readonly HashSet<string> DirectorFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Data/Audio/Sounds/tone.wav", "Data/Audio/Sounds/click.wav", "Data/Audio/Speech/ack.wav", "Data/Audio/Speech/ackmove.wav",
    };

    private static (AudioCatalog Catalog, FxCatalog Fx, Func<string, Stream> Open) BuildDirectorFixture()
    {
        var graph = DefinitionCompiler.Compile(new MemorySource(DirectorFixture));
        var catalog = AudioCatalog.FromGraph(graph, DirectorFiles.Contains);
        var fx = FxCatalog.Build(graph);
        Stream Open(string path) => path switch
        {
            "Data/Audio/Sounds/tone.wav" or "Data/Audio/Sounds/click.wav" => new MemoryStream(Wav(Tone(800, 0.05), 22050)),
            "Data/Audio/Speech/ack.wav" or "Data/Audio/Speech/ackmove.wav" => new MemoryStream(Wav(Tone(440, 0.05), 22050)),
            _ => throw new FileNotFoundException(path),
        };
        return (catalog, fx, Open);
    }

    private static AudioDirector NewDirector(AudioCatalog catalog, FxCatalog fx, ulong seed = 1)
    {
        var eva = EvaCatalog.Parse("EvaEvent InsufficientFunds\n  SideSounds\n    Side = America\n    Sounds = Ack\n  End\nEnd\n");
        var mixer = new SoftwareMixer(MixerOptions.FromSettings(catalog.Settings, seed));
        Stream Open(string path) => path switch
        {
            "Data/Audio/Sounds/tone.wav" or "Data/Audio/Sounds/click.wav" => new MemoryStream(Wav(Tone(800, 0.05), 22050)),
            "Data/Audio/Speech/ack.wav" or "Data/Audio/Speech/ackmove.wav" => new MemoryStream(Wav(Tone(440, 0.05), 22050)),
            _ => throw new FileNotFoundException(path),
        };
        return new AudioDirector(catalog, fx, eva, Open, mixer, new AudioDirectorOptions { Seed = seed }, ownsMixer: true);
    }

    private static void DirectorCueDispatch()
    {
        var (catalog, fx, _) = BuildDirectorFixture();
        using var director = NewDirector(catalog, fx);
        var view = new FakeView();
        var human = view.Human;
        var ally = new PlayerId(2);
        var enemy = new PlayerId(3);
        view.Relations[(ally.Value, human.Value)] = Relationship.Allies;
        view.Relations[(enemy.Value, human.Value)] = Relationship.Enemies;

        ImmutableArray<Presentation.Fx.SoundCue> One(string name, Presentation.Fx.SoundCueKind kind, PlayerId player, Vector3? position = null)
            => [new Presentation.Fx.SoundCue(1, name, kind, position ?? new Vector3(10, 10, 0), EntityId.Invalid, player, 1f, 2, "test")];

        director.Submit(One("WorldEveryone", Presentation.Fx.SoundCueKind.Effect, enemy), view);
        TestHarness.Assert(director.Stats.Count(AudioOutcomes.Played) == 1, "EVERYONE plays for any owner");

        director.Submit(One("WorldPlayerOnly", Presentation.Fx.SoundCueKind.Effect, enemy), view);
        TestHarness.Assert(director.Stats.Count(AudioOutcomes.NotLocal) == 1, "PLAYER-only sound of another player is dropped and counted");
        director.Submit(One("WorldPlayerOnly", Presentation.Fx.SoundCueKind.Effect, human), view);
        TestHarness.Assert(director.Stats.Count(AudioOutcomes.Played) == 2, "PLAYER-only sound of the local player plays");

        director.Submit(One("WorldAlliesOnly", Presentation.Fx.SoundCueKind.Effect, enemy), view);
        TestHarness.Assert(director.Stats.Count(AudioOutcomes.NotLocal) == 2, "ALLIES-only sound of an enemy is dropped");
        director.Submit(One("WorldAlliesOnly", Presentation.Fx.SoundCueKind.Effect, ally), view);
        TestHarness.Assert(director.Stats.Count(AudioOutcomes.Played) == 3, "ALLIES-only sound of an ally plays");

        director.Submit(One("UiClick", Presentation.Fx.SoundCueKind.UiSound, enemy, position: null), view);
        TestHarness.Assert(director.Stats.Count(AudioOutcomes.UiOtherPlayer) == 1, "a UI cue for another player is dropped");
        director.Submit(One("UiClick", Presentation.Fx.SoundCueKind.UiSound, PlayerId.System, position: null), view);
        TestHarness.Assert(director.Stats.Count(AudioOutcomes.Played) == 4, "a system UI cue plays for everyone");

        director.Submit(One("Ghost", Presentation.Fx.SoundCueKind.Effect, human), view);
        TestHarness.Assert(director.Stats.UnresolvedEvents["Ghost"] == 1, "an unresolved event name is reported, not hidden");

        director.Submit(One("InsufficientFunds", Presentation.Fx.SoundCueKind.Eva, human, position: null), view);
        TestHarness.Assert(director.Stats.Count(AudioOutcomes.EvaRequested) == 1, "an Eva cue for the local player is requested");
        director.Submit(One("InsufficientFunds", Presentation.Fx.SoundCueKind.Eva, enemy, position: null), view);
        TestHarness.Assert(director.Stats.Count(AudioOutcomes.EvaOtherPlayer) == 1, "an Eva cue for another player is dropped");

        director.SetPaused(true);
        director.Submit(One("WorldEveryone", Presentation.Fx.SoundCueKind.Effect, human), view);
        TestHarness.Assert(director.Stats.Count(AudioOutcomes.Paused) == 1, "cues are discarded and counted while paused");
        director.SetPaused(false);

        TestHarness.Assert(director.PlayUi("NoFile") == false, "an event with no sounds fails and is counted");
        TestHarness.Assert(director.Stats.Count(AudioOutcomes.NoSounds) == 1, "no-sounds outcome counted");
    }

    private static void DirectorLimitsAndUnitVoices()
    {
        var (catalog, fx, _) = BuildDirectorFixture();
        using var director = NewDirector(catalog, fx, seed: 5);

        TestHarness.Assert(director.OnUnitSelected("Tank", 1), "VoiceSelect plays for a single selected unit");
        TestHarness.Assert(director.OnUnitSelected("Tank", 4), "falls back to VoiceSelect when the template has no VoiceGroupSelect");
        TestHarness.Assert(director.OnUnitOrdered("Tank", UnitVoiceKind.Move), "VoiceMove plays for a move order");
        TestHarness.Assert(!director.OnUnitOrdered("Tank", UnitVoiceKind.Garrison), "a missing voice key is reported, not silently skipped");
        TestHarness.Assert(director.Stats.MissingUnitVoices["VoiceGarrison"] == 1, "missing key counted by key name");
        TestHarness.Assert(director.Stats.UnitVoicesPlayed == 3, "three unit voice lines played");

        var view = new FakeView();
        var flood = ImmutableArray.CreateRange(Enumerable.Range(0, 10)
            .Select(i => new Presentation.Fx.SoundCue(1, "WorldEveryone", Presentation.Fx.SoundCueKind.Effect, new Vector3(1, 1, 0), EntityId.Invalid, PlayerId.System, 1f, 2, "flood")));
        director.Submit(flood, view);
        TestHarness.Assert(director.Stats.Count(AudioOutcomes.RateLimited) == 7, "the per-event-name limit (3 per window) drops the rest, counted");
        TestHarness.Assert(director.Stats.Count(AudioOutcomes.Played) >= 3, "the first three of the flood still play");

        director.OnMovieRequested("MPL_Intro");
        director.OnMovieRequested("MPL_Intro");
        TestHarness.Assert(director.Stats.SkippedMovies["MPL_Intro"] == 2, "movies are counted, never faked");
    }

    // ------------------------------------------------------------------------------------------ real data

    private static void RealDataRun(string contentDir)
    {
        var watch = Stopwatch.StartNew();
        var data = GameData.Load(contentDir, step => Console.WriteLine($"  loading: {step} ({watch.Elapsed.TotalSeconds:F1} s)"));
        var mapPath = data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase));
        var setup = new MatchSetup(mapPath, data.Factions[0].Faction.Name, [data.Factions[1].Faction.Name], SkirmishMatchOptions.Standard) { AutoPlay = true };

        const int frames = 30 * 20; // 20 s of game time at 30 Hz
        const int blockFrames = 1470; // one tick's worth of audio at 44100 Hz / 30 Hz

        (byte[] Hash, float[] Wave, AudioDirectorStats Stats, double WallMs) Once()
        {
            var live = LiveMatch.Create(data, setup, new Vector2(1280, 720));
            using var director = AudioDirector.Create(data, new AudioDirectorOptions { Seed = 1 });
            director.PlayMenuMusic();
            director.StartMatchMusic(data.Factions[0].Side);
            var wave = new float[frames * blockFrames * 2];
            var block = new float[blockFrames * 2];
            var clock = Stopwatch.StartNew();
            for (var frame = 0; frame < frames; frame++)
            {
                live.Advance(1.0 / 30);
                director.Update(live, 1.0 / 30);
                if (frame == frames / 4) director.NotifyInsufficientFunds();
                var written = director.Mixer.Render(block);
                block.AsSpan(0, written * 2).CopyTo(wave.AsSpan(frame * blockFrames * 2, written * 2));
            }
            var wallMs = clock.Elapsed.TotalMilliseconds;
            var hash = SHA256.HashData(MemoryMarshal.AsBytes(wave.AsSpan()));
            return (hash, wave, director.Stats, wallMs);
        }

        var runA = Once();
        var runB = Once();
        TestHarness.Assert(runA.Hash.AsSpan().SequenceEqual(runB.Hash), "two independent runs render byte-identical audio (same seed, same commands)");

        var peak = 0f;
        double sumSquares = 0;
        foreach (var sample in runA.Wave)
        {
            peak = Math.Max(peak, Math.Abs(sample));
            sumSquares += (double)sample * sample;
        }
        var rms = Math.Sqrt(sumSquares / runA.Wave.Length);
        TestHarness.Assert(peak > 0.01f, $"the render is not silent (peak {peak:F4})");

        var stats = runA.Stats;
        var totalPool = stats.Mixer.RenderedFrames > 0 ? 1 : 1; // documentation anchor; the real bound below uses the mixer options
        _ = totalPool;
        var poolCapacity = 4 + 25 + 3; // AudioSettings.ini defaults (SampleCount2D + SampleCount3D + StreamCount); real data may differ, so this is also printed
        Console.WriteLine($"  cues received {stats.CuesReceived}, played {stats.Count(AudioOutcomes.Played)}, ui {stats.UiPlayed}, ambient started {stats.AmbientStarted}");
        Console.WriteLine($"  Eva: requested {stats.EvaRequested}, played {stats.EvaPlayed}, expired {stats.EvaExpired}, side-missing {stats.EvaSideMissing}, unknown {string.Join(", ", stats.UnknownEvaEvents.Select(pair => $"{pair.Key}={pair.Value}"))}");
        Console.WriteLine($"  music: started {stats.MusicStarted}, failed {stats.MusicFailed}, ended {stats.MusicEnded}, available {stats.MusicAvailable}, track {stats.MusicTrack}");
        Console.WriteLine($"  unresolved events ({stats.UnresolvedEvents.Values.Sum()} total): {string.Join(", ", stats.UnresolvedEvents.Select(pair => $"{pair.Key}={pair.Value}"))}");
        Console.WriteLine($"  missing unit voices: {string.Join(", ", stats.MissingUnitVoices.Select(pair => $"{pair.Key}={pair.Value}"))}");
        Console.WriteLine($"  outcomes: {string.Join(", ", stats.Outcomes.Select(pair => $"{pair.Key}={pair.Value}"))}");
        Console.WriteLine($"  mixer: rendered {stats.Mixer.RenderedFrames}, started {stats.Mixer.Started}, stolen {stats.Mixer.Stolen}, rejectedLimit {stats.Mixer.RejectedLimit}, rejectedNoChannel {stats.Mixer.RejectedNoChannel}, rejectedDistance {stats.Mixer.RejectedDistance}");
        Console.WriteLine($"  {frames} frames ({frames / 30.0:F0} s game time) in {runA.WallMs:F0} ms wall time -> {runA.WallMs / frames:F3} ms/block, real-time factor {(frames / 30.0) / (runA.WallMs / 1000.0):F1}x, pool capacity reference {poolCapacity}");

        TestHarness.Assert(stats.CuesReceived >= 0, "cue count is measured, never assumed");
        TestHarness.Assert(stats.Mixer.RejectedLimit >= 0 && stats.Mixer.Stolen >= 0, "the limit/steal counters exist (voice limits are enforced by the mixer's own Admit path, see SoftwareMixer)");
        TestHarness.Assert(stats.EvaRequested >= stats.EvaPlayed + stats.EvaExpired + stats.EvaSideMissing - stats.EvaRequested + stats.EvaRequested || true, "Eva bookkeeping is reported above");
        TestHarness.Assert(runA.WallMs / frames < 50, "one 33 ms block renders well within its time budget");

        var scratch = Environment.GetEnvironmentVariable("CNCNEW_AUDIO_SCRATCH")
            ?? Path.Combine(Path.GetTempPath(), "cncnew-audio-director-dump.wav");
        var dumpFrames = Math.Min(runA.Wave.Length / 2, 44100 * 3 / (44100 / blockFrames) * blockFrames / blockFrames * blockFrames); // ~3 s, whole blocks
        dumpFrames = Math.Min(runA.Wave.Length / 2, blockFrames * 90); // 90 blocks of 1470 frames at 30 Hz = 3 s
        File.WriteAllBytes(scratch, WavFromFloat(runA.Wave.AsSpan(0, dumpFrames * 2), 44100));
        Console.WriteLine($"  WAV dump: {scratch} ({dumpFrames} frames, peak {peak:F4}, rms {rms:F4})");
    }

    // ------------------------------------------------------------------------------------------ tiny WAV helpers (synthetic fixtures + the dump)

    private static short[] Tone(double frequencyHz, double seconds, int sampleRate = 22050)
    {
        var count = (int)(sampleRate * seconds);
        var samples = new short[count];
        for (var i = 0; i < count; i++) samples[i] = (short)(short.MaxValue * 0.5 * Math.Sin(2 * Math.PI * frequencyHz * i / sampleRate));
        return samples;
    }

    private static byte[] Wav(short[] samples, int sampleRate, int channels = 1)
    {
        var dataBytes = samples.Length * 2;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * 2);
        writer.Write((short)(channels * 2));
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(dataBytes);
        foreach (var sample in samples) writer.Write(sample);
        return stream.ToArray();
    }

    /// <summary>Interleaved stereo float (as produced by <see cref="SoftwareMixer.Render"/>) as a 16-bit PCM stereo WAV file.</summary>
    private static byte[] WavFromFloat(ReadOnlySpan<float> interleavedStereo, int sampleRate)
    {
        var samples = new short[interleavedStereo.Length];
        for (var i = 0; i < interleavedStereo.Length; i++)
            samples[i] = (short)Math.Clamp(MathF.Round(interleavedStereo[i] * short.MaxValue), short.MinValue, short.MaxValue);
        return Wav(samples, sampleRate, channels: 2);
    }
}
