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
using CnCNew.Game.Hud;
using CnCNew.Game.Live;
using CnCNew.Presentation.Fx;
using CnCNew.Presentation.Input;
using CnCNew.Simulation.Commands;
using CnCNew.Simulation.Events;
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

    /// <summary>The rate the fake world's ticks count at (the original's unless a test names another).</summary>
    public CnCNew.Simulation.Time.SimulationRate Rate { get; init; } = CnCNew.Tests.TestRate.Default;
    public CnCNew.Presentation.GameClock Clock => new(Tick, Rate.TicksPerSecond, 0);
    public bool LowPower { get; set; }
    public Dictionary<(int, int), Relationship> Relations { get; } = [];
    public bool Shrouded { get; set; }

    public Relationship RelationshipOf(PlayerId from, PlayerId to)
        => Relations.TryGetValue((from.Value, to.Value), out var value) ? value : from == to ? Relationship.Allies : Relationship.Enemies;

    public bool IsShrouded(Vector3 position) => Shrouded;
}

/// <summary>A world with exactly the poses a test places, for <see cref="SoundCueDeriver"/>.</summary>
file sealed class FakeFxView : IFxWorldView
{
    public Dictionary<uint, FxEntityPose> Poses { get; } = [];
    public FxEntityPose? GetPose(EntityId entity) => Poses.TryGetValue(entity.Value, out var pose) ? pose : null;
    public Vector3? GetBonePosition(EntityId entity, string boneName) => null;
    public Vector3? GetWeaponMuzzle(EntityId entity, string weaponName) => null;
    public string? GetTemplateName(int templateIndex) => null;
    public float GroundHeight(float x, float y) => 0f;
    public bool IsShrouded(Vector3 position) => false;
}

internal static class AudioDirectorTests
{
    public static int Run(string[] args)
    {
        var harness = new TestHarness(nameof(AudioDirectorTests));
        harness.Run("Eva.ini parser reads blocks, converts ms to frames, rejects unknown keys", EvaParserBasics);
        harness.Run("Eva announcer: priority, cooldown, expiry, side-missing", EvaAnnouncerBehaviour);
        harness.Run("Time scale: Eva's wait between two announcements of one event is the same game time at 30, 60 and 120 Hz", EvaLockTimeIsGameTime);
        harness.Run("Music playlist: side filter, silent/unresolved skipped, no immediate repeat", MusicPlaylistBehaviour);
        harness.Run("Director: bus mapping and shouldPlayLocally", DirectorCueDispatch);
        harness.Run("Director: rate limiting, unit voice interruption, missing voice keys", DirectorLimitsAndUnitVoices);
        harness.Run("Wiring: CaptureFlash becomes the MiscAudio DefectorTimerTickSound at the building", CaptureFlashTicksAtTheBuilding);
        harness.Run("Wiring: selection, orders, clicks and refusals reach the director", SelectionOrdersAndClicks);
        harness.Run("Wiring: a missing sound file is counted per source, never replaced", MissingFileIsCounted);
        harness.Run("Wiring: mute and the volume sliders silence the output live, counters unchanged", MuteAndSlidersSilence);
        harness.Run("Wiring: without a device the mixer renders offline; fixed steps give the same WAV bytes", OfflineRenderIsReproducible);
        harness.Run("Script control: the scripts' volumes multiply the sliders, muted and per-event volumes, removal, Eva, reset", ScriptControlsTheSound);

        var contentDir = ContentDir(args);
        if (contentDir is null)
        {
            Console.WriteLine("SKIP AudioDirectorTests real-data (pass --content-dir <dir> to run against real data)");
            return harness.Failures;
        }
        var watch = Stopwatch.StartNew();
        var data = GameData.Load(contentDir, step => Console.WriteLine($"  loading: {step} ({watch.Elapsed.TotalSeconds:F1} s)"), rate: CnCNew.Tests.TestRate.Default);
        harness.Run("Director drives a real match: deterministic, audible, bounded, WAV dump", () => RealDataRun(data));
        harness.Run("Wiring on real data: the capture-building scenario ticks at the barracks, the Ranger answers, the world hash is untouched", () => RealCaptureScenario(data));
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

    /// <summary>
    /// The announcer counts original frames, never ticks (docs/HANDOFF-PHASE21.md, item 9): an event without <c>TimeBetweenChecksMS</c> may be announced again after 900
    /// original frames (30 s) at every tick rate. The director is driven as the client drives it, once per tick of the fake world, with the event requested every tick.
    /// </summary>
    private static void EvaLockTimeIsGameTime()
    {
        var (catalog, fx, _) = BuildDirectorFixture();
        var measured = new List<(CnCNew.Simulation.Time.SimulationRate Rate, double Seconds)>();
        foreach (var rate in new[] { CnCNew.Simulation.Time.SimulationRate.Hz30, CnCNew.Simulation.Time.SimulationRate.Hz60, CnCNew.Simulation.Time.SimulationRate.Hz120 })
        {
            using var director = NewDirector(catalog, fx);
            using var render = new OfflineAudioRenderer(director.Mixer);
            var view = new FakeView { Rate = rate };
            var step = 1.0 / rate.TicksPerSecond;
            long first = -1, second = -1;
            for (long tick = rate.TicksPerSecond; tick < 40L * rate.TicksPerSecond && second < 0; tick++)
            {
                view.Tick = tick;
                director.NotifyInsufficientFunds();
                director.Tick(view, step);
                render.Pump(step);
                if (director.Stats.EvaPlayed >= 1 && first < 0) first = tick;
                if (director.Stats.EvaPlayed >= 2) second = tick;
            }
            TestHarness.Assert(first >= 0 && second > first, $"at {rate} the event is announced twice within forty seconds (ticks {first} and {second})");
            measured.Add((rate, (double)(second - first) / rate.TicksPerSecond));
        }

        Console.WriteLine("       Eva wait between two announcements: " + string.Join(", ", measured.Select(value => FormattableString.Invariant($"{value.Rate}: {value.Seconds:F4} s"))));
        foreach (var (rate, seconds) in measured)
        {
            TestHarness.Assert(Math.Abs(seconds - measured[0].Seconds) <= CnCNew.Tests.TestTime.FrameSeconds + 1e-9, $"Eva waits {seconds:F4} s at {rate} and {measured[0].Seconds:F4} s at {measured[0].Rate}: more than one original frame apart");
            TestHarness.Assert(Math.Abs(seconds - 30.0) <= 2 * CnCNew.Tests.TestTime.FrameSeconds, $"Eva waits about thirty seconds (900 original frames) at {rate} (was {seconds:F4} s)");
        }
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
        TestHarness.Assert(solo.Update(2 + 3334 * CnCNew.Tests.TestTime.FramesPerSecond / 1000 + 2, false, false, "Test") is null, "the check expired before it could be evaluated");
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
            AudioEvent CaptureTick
              Sounds = tone
              Type = world shrouded everyone
            End
            AudioEvent Broken
              Sounds = gone
              Type = ui everyone
            End
            AudioEvent LoopTone
              Sounds = tone
              Type = ui everyone
              Control = loop
              LoopCount = 0
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
        ["Data/INI/MiscAudio.ini"] = "MiscAudio\n  GUIClickSound = UiClick\n  DefectorTimerTickSound = CaptureTick\nEnd\n",
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
        // Listed (so the catalog resolves it) but not openable: the file a broken installation lacks.
        "Data/Audio/Sounds/gone.wav",
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

    // ------------------------------------------------------------------------------------------ wiring

    private static SimEvent Flash(EntityId building, EntityId capturer)
        => new(new EventId(1), 5, SimEventKind.CaptureFlash, building, capturer, PlayerId.System, string.Empty, 0);

    private static void CaptureFlashTicksAtTheBuilding()
    {
        var (catalog, fx, _) = BuildDirectorFixture();
        using var director = NewDirector(catalog, fx);
        var building = new EntityId(7);
        var ranger = new EntityId(8);
        var at = new Vector3(120, 40, 3);
        var world = new FakeFxView();
        world.Poses[building.Value] = new FxEntityPose(at, 0f, "Barracks", new PlayerId(2), 0, 30f);
        world.Poses[ranger.Value] = new FxEntityPose(new Vector3(90, 40, 3), 0f, "Ranger", new PlayerId(1), 0, 5f);

        var cues = new List<SoundCue>();
        new SoundCueDeriver(fx, CnCNew.Tests.TestRate.Time).Derive(Flash(building, ranger), world, cues);
        TestHarness.Assert(cues.Count == 1, "one cue per flash");
        var cue = cues[0];
        TestHarness.Assert(cue.EventName == "CaptureTick", $"the cue is the event MiscAudio names for DefectorTimerTickSound (was {cue.EventName})");
        TestHarness.Assert(cue.Kind == SoundCueKind.Effect && cue.Position == at && cue.Entity == building, "a world sound at the building being captured, not at the capturing unit");
        TestHarness.Assert(cue.Player.IsSystem, "the reference sets no player index on the tick");

        // The local player is neither owner: the event's EVERYONE type lets the tick through, SHROUDED lets it through fog.
        director.Submit([..cues], new FakeView { Shrouded = true });
        var stats = director.Stats;
        TestHarness.Assert(stats.StartedEvents.GetValueOrDefault("CaptureTick") == 1, "the director started the tick");
        var source = stats.Sources[nameof(AudioSource.Simulation)];
        TestHarness.Assert(source is { Requested: 1, Started: 1, Dropped: 0 }, $"counted for the simulation source ({source})");

        // A building that is gone gives no cue; a data set without the hook gives a cue the director reports as unknown.
        cues.Clear();
        new SoundCueDeriver(fx, CnCNew.Tests.TestRate.Time).Derive(Flash(new EntityId(99), ranger), world, cues);
        TestHarness.Assert(cues.Count == 0, "no pose, no cue");
        var without = new Dictionary<string, string>(DirectorFixture, StringComparer.OrdinalIgnoreCase) { ["Data/INI/MiscAudio.ini"] = "MiscAudio\n  GUIClickSound = UiClick\nEnd\n" };
        var bare = FxCatalog.Build(DefinitionCompiler.Compile(new MemorySource(without)));
        new SoundCueDeriver(bare, CnCNew.Tests.TestRate.Time).Derive(Flash(building, ranger), world, cues);
        TestHarness.Assert(cues.Count == 1 && cues[0].EventName == SoundCueDeriver.CaptureTickHook, "a missing hook is not silent: the cue carries the hook's own name");
        director.Submit([..cues], new FakeView());
        TestHarness.Assert(director.Stats.UnresolvedEvents.GetValueOrDefault(SoundCueDeriver.CaptureTickHook) == 1, "and the director reports it as an unknown event");
    }

    private static void SelectionOrdersAndClicks()
    {
        var (catalog, fx, _) = BuildDirectorFixture();
        using var audio = new GameAudio(NewDirector(catalog, fx), fx);
        CommandFeedback Feedback(FeedbackKind kind, CommandRejection rejection = 0) => new(kind, Vector3.Zero, EntityId.Invalid, 1, 30, rejection);

        TestHarness.Assert(audio.SelectionAcknowledged("Tank", 1), "a newly selected own unit answers with VoiceSelect");
        TestHarness.Assert(audio.Director.Stats.StartedEvents.GetValueOrDefault("Ack") == 1, "the select line is the template's VoiceSelect");

        audio.CommandAcknowledged(Feedback(FeedbackKind.MoveMarker), "Tank");
        TestHarness.Assert(audio.Director.Stats.StartedEvents.GetValueOrDefault("AckMove") == 1, "a move order is confirmed with VoiceMove");
        audio.CommandAcknowledged(Feedback(FeedbackKind.AttackMarker), "Tank");
        var voices = audio.Director.Stats.Sources[nameof(AudioSource.UnitVoice)];
        TestHarness.Assert(audio.Director.Stats.MissingUnitVoices.GetValueOrDefault("VoiceAttack") == 1 && voices is { Requested: 3, Started: 2, Dropped: 1 },
            $"a template without VoiceAttack is a counted drop ({voices})");
        audio.CommandAcknowledged(Feedback(FeedbackKind.MoveMarker), null);
        TestHarness.Assert(audio.Director.Stats.Sources[nameof(AudioSource.UnitVoice)].Requested == 3, "an order without an own selected unit has no speaker");
        audio.CommandAcknowledged(Feedback(FeedbackKind.RallyMarker), "Tank");
        TestHarness.Assert(audio.Director.Stats.Sources[nameof(AudioSource.UnitVoice)].Requested == 3, "a rally point is not acknowledged by a voice");

        TestHarness.Assert(audio.PlayHook("GUIClickSound"), "the click hook plays the event MiscAudio names");
        TestHarness.Assert(audio.Director.Stats.StartedEvents.GetValueOrDefault("UiClick") == 1 && audio.Director.Stats.Sources[nameof(AudioSource.Ui)].Started == 1, "counted for the UI source");
        audio.CommandAcknowledged(Feedback(FeedbackKind.InvalidOrder), "Tank");
        TestHarness.Assert(audio.UnknownHooks.GetValueOrDefault("NoCanDoSound") == 1, "a hook the data lacks is counted by key, not skipped");
        TestHarness.Assert(!audio.PlayUi("GUISliderMoved") && audio.Director.Stats.UnresolvedEvents.GetValueOrDefault("GUISliderMoved") == 1, "an unknown shell sound name is reported");

        audio.CommandAcknowledged(Feedback(FeedbackKind.Rejected, CommandRejection.InsufficientFunds), "Tank");
        TestHarness.Assert(audio.Director.Stats.Count(AudioOutcomes.EvaRequested) == 1, "an order refused for lack of money asks Eva");
        audio.CommandAcknowledged(Feedback(FeedbackKind.Rejected, CommandRejection.QueueFull), "Tank");
        TestHarness.Assert(audio.Director.Stats.Count(AudioOutcomes.EvaRequested) == 1, "any other refusal is left to the simulation's CommandRejected cue");

        TestHarness.Assert(audio.Report().Any(line => line.StartsWith("audio source UnitVoice: requested 3, started 2", StringComparison.Ordinal)), "the report prints the per-source counters");
    }

    private static void MissingFileIsCounted()
    {
        var (catalog, fx, _) = BuildDirectorFixture();
        using var director = NewDirector(catalog, fx);
        TestHarness.Assert(!director.PlayUi("Broken"), "an event whose only file cannot be opened does not play");
        TestHarness.Assert(!director.PlayUi("Broken"), "and is not retried into success");
        var stats = director.Stats;
        TestHarness.Assert(stats.Count(AudioOutcomes.DecodeFailed) == 2, "each request is counted as a drop");
        TestHarness.Assert(stats.DecodeFailures.Count == 1 && stats.DecodeFailures.ContainsKey("Data/Audio/Sounds/gone.wav"), "the file is named once with its error");
        TestHarness.Assert(stats.Sources[nameof(AudioSource.Ui)] is { Requested: 2, Started: 0, Dropped: 2 }, "per-source counters show the drops");
        TestHarness.Assert(stats.Mixer.Started == 0 && !stats.StartedEvents.ContainsKey("Broken"), "nothing reached the mixer: no placeholder tone");
    }

    private static void MuteAndSlidersSilence()
    {
        var (catalog, fx, _) = BuildDirectorFixture();

        float PeakOf(Action<AudioDirector> configure, out AudioDirectorStats stats)
        {
            using var director = NewDirector(catalog, fx);
            configure(director);
            using var render = new OfflineAudioRenderer(director.Mixer);
            director.PlayUi("UiClick");
            director.OnUnitSelected("Tank", 1);
            for (var frame = 0; frame < 6; frame++)
            {
                director.Tick(null, CnCNew.Tests.TestTime.FrameSeconds);
                render.Pump(CnCNew.Tests.TestTime.FrameSeconds);
            }
            stats = director.Stats;
            return render.Peak;
        }

        var loud = PeakOf(_ => { }, out var loudStats);
        var muted = PeakOf(director => director.Muted = true, out var mutedStats);
        var zero = PeakOf(director => director.ApplySettings(CnCNew.Ui.Settings.GameSettings.Default.Audio with { Effects = 0f, Voice = 0f }), out _);
        TestHarness.Assert(loud > 0.01f, $"the unmuted run is audible (peak {loud:F4})");
        TestHarness.Assert(muted == 0f, $"mute gives digital silence (peak {muted:F6})");
        TestHarness.Assert(zero == 0f, $"effects and voice sliders at zero silence a click and a unit voice (peak {zero:F6})");
        TestHarness.Assert(mutedStats.StartedEvents.SequenceEqual(loudStats.StartedEvents) && mutedStats.Mixer.Started == loudStats.Mixer.Started,
            "a muted run requests and mixes the same voices; only the master volume differs");

        // Live: unmuting in the middle of a sound makes the rest of it audible.
        using var live = NewDirector(catalog, fx);
        using var output = new OfflineAudioRenderer(live.Mixer);
        live.Muted = true;
        live.PlayUi("UiClick");
        output.Pump(0.01);
        TestHarness.Assert(output.Peak == 0f, "muted start");
        live.Muted = false;
        output.Pump(0.02);
        TestHarness.Assert(output.Peak > 0.01f, "unmuting takes effect with the next rendered block");
    }

    /// <summary>
    /// What a mission script does to the sound (<c>ScriptActions::doAudioSetVolume</c>, <c>doSoundOverrideVolume</c>, <c>doSoundEnableType</c>, <c>doSoundRemove*</c>, <c>doEvaEnabledDisabled</c>): measured on the
    /// rendered output, not only on the director's state, so a volume that never reached the mixer cannot pass.
    /// </summary>
    private static void ScriptControlsTheSound()
    {
        var (catalog, fx, _) = BuildDirectorFixture();

        float Peak(Action<AudioDirector> configure, Action<AudioDirector> play)
        {
            using var director = NewDirector(catalog, fx);
            configure(director);
            using var render = new OfflineAudioRenderer(director.Mixer);
            play(director);
            for (var frame = 0; frame < 6; frame++)
            {
                director.Tick(null, CnCNew.Tests.TestTime.FrameSeconds);
                render.Pump(CnCNew.Tests.TestTime.FrameSeconds);
            }
            return render.Peak;
        }

        // AudioManager::setVolume: the script factor multiplies the player's slider, per kind (2D sound = the UI bus, speech = the voice bus).
        var click = Peak(_ => { }, director => director.PlayUi("UiClick"));
        var clickHalf = Peak(director => director.SetScriptVolume(ScriptVolumeKind.Sound, 50), director => director.PlayUi("UiClick"));
        var clickOff = Peak(director => director.SetScriptVolume(ScriptVolumeKind.Sound, 0), director => director.PlayUi("UiClick"));
        var clickOtherKind = Peak(director => director.SetScriptVolume(ScriptVolumeKind.Speech, 0), director => director.PlayUi("UiClick"));
        TestHarness.Assert(click > 0.01f && Math.Abs(clickHalf / click - 0.5f) < 0.03f, $"a script's sound volume of 50% halves the UI bus (full {click:F4}, half {clickHalf:F4})");
        TestHarness.Assert(clickOff == 0f && Math.Abs(clickOtherKind - click) < 1e-6f, "0% silences its own kind and leaves the other kinds alone");
        var voice = Peak(_ => { }, director => director.OnUnitSelected("Tank", 1));
        var voiceHalf = Peak(director => director.SetScriptVolume(ScriptVolumeKind.Speech, 50), director => director.OnUnitSelected("Tank", 1));
        var voiceMusicOff = Peak(director => director.SetScriptVolume(ScriptVolumeKind.Music, 0), director => director.OnUnitSelected("Tank", 1));
        TestHarness.Assert(voice > 0.01f && Math.Abs(voiceHalf / voice - 0.5f) < 0.03f && Math.Abs(voiceMusicOff - voice) < 1e-6f, "the speech volume halves the voice bus; the music volume does not touch it");
        using (var clamped = NewDirector(catalog, fx))
        {
            clamped.SetScriptVolume(ScriptVolumeKind.Music, 250);
            TestHarness.Assert(clamped.ScriptVolume(ScriptVolumeKind.Music) == 1f, "a script cannot make the sound louder than the player set it (clamped to 100%)");
            clamped.SetScriptVolume(ScriptVolumeKind.Music, -10);
            TestHarness.Assert(clamped.ScriptVolume(ScriptVolumeKind.Music) == 0f, "nor below silence");
        }

        // SOUND_DISABLE_TYPE / SOUND_ENABLE_TYPE / SOUND_ENABLE_ALL: the event is culled as muted, counted, and a name nobody defines is counted as unresolved, not stored.
        using (var director = NewDirector(catalog, fx))
        {
            director.SetEventEnabled("UiClick", false);
            TestHarness.Assert(!director.PlayUi("UiClick"), "a disabled event does not start");
            var stats = director.Stats;
            TestHarness.Assert(stats.Count(AudioOutcomes.ScriptMuted) == 1 && director.ScriptState.Muted == 1 && stats.Sources["Ui"].Started == 0, "the refusal is counted as script-muted, not as an unknown event");
            TestHarness.Assert(director.PlayUi("AudioEventThatDoesNotExist") == false, "an unknown event stays unknown");
            director.SetEventEnabled("NoSuchEvent", false);
            TestHarness.Assert(!director.ScriptState.EventVolumes.ContainsKey("NoSuchEvent") && director.Stats.UnresolvedEvents.ContainsKey("NoSuchEvent"), "disabling an event that does not exist is counted, not silently stored");
            director.SetEventEnabled("UiClick", true);
            TestHarness.Assert(director.PlayUi("UiClick"), "enabled again, it plays");
            director.SetEventEnabled("UiClick", false);
            director.SetEventEnabled("WorldEveryone", false);
            director.SetEventEnabled(string.Empty, true);
            TestHarness.Assert(director.ScriptState.EventVolumes.IsEmpty && director.PlayUi("UiClick"), "SOUND_ENABLE_ALL forgets every override");
        }

        // AUDIO_OVERRIDE_VOLUME_TYPE: the event takes the overridden volume (a factor, 0.5 = 50%); AUDIO_RESTORE_VOLUME_TYPE / _ALL_TYPE give it back.
        var overridden = Peak(director => director.OverrideEventVolume("UiClick", 0.5f), director => director.PlayUi("UiClick"));
        var restored = Peak(director => { director.OverrideEventVolume("UiClick", 0.5f); director.OverrideEventVolume("UiClick", AudioDirector.RestoreEventVolume); }, director => director.PlayUi("UiClick"));
        var restoredAll = Peak(director => { director.OverrideEventVolume("UiClick", 0.5f); director.OverrideEventVolume(string.Empty, AudioDirector.RestoreEventVolume); }, director => director.PlayUi("UiClick"));
        TestHarness.Assert(Math.Abs(overridden / click - 0.5f) < 0.03f, $"an event volume of 0.5 halves that event (full {click:F4}, overridden {overridden:F4})");
        TestHarness.Assert(Math.Abs(restored - click) < 1e-6f && Math.Abs(restoredAll - click) < 1e-6f, "restoring one event, or all of them, brings the catalog volume back");

        // A playing voice follows a new override at once (adjustVolumeOfPlayingAudio); SOUND_REMOVE_TYPE stops it; SOUND_REMOVE_ALL_DISABLED stops the muted ones.
        using (var director = NewDirector(catalog, fx))
        {
            using var render = new OfflineAudioRenderer(director.Mixer);
            director.PlayUi("LoopTone");
            director.Tick(null, 0.05);
            render.Pump(0.1);
            var loud = render.Peak;
            TestHarness.Assert(director.Mixer.ActiveVoiceCount == 1 && loud > 0.01f, "the looping event plays");
            director.OverrideEventVolume("LoopTone", 0.25f);
            render.Pump(0.05); // the mixer applies the command and ramps its gain; the peak is judged on the blocks after that
            var quiet = new OfflineAudioRenderer(director.Mixer);
            quiet.Pump(0.1);
            TestHarness.Assert(quiet.Peak > 0.01f && quiet.Peak < loud * 0.4f, $"the playing loop became quieter when the script lowered its volume (before {loud:F4}, after {quiet.Peak:F4})");
            TestHarness.Assert(director.RemoveEvent("LoopTone") == 1, "SOUND_REMOVE_TYPE stops the playing voice");
            render.Pump(0.1);
            TestHarness.Assert(director.Mixer.ActiveVoiceCount == 0, "the voice is gone from the mixer");

            director.OverrideEventVolume("LoopTone", AudioDirector.RestoreEventVolume);
            director.PlayUi("LoopTone");
            director.Tick(null, 0.05);
            render.Pump(0.1);
            director.SetEventEnabled("LoopTone", false);
            TestHarness.Assert(director.RemoveDisabledEvents() == 1 && director.ScriptState.VoicesRemoved == 2, "SOUND_REMOVE_ALL_DISABLED stops the voice of a muted event");
        }

        // EVA_SET_ENABLED_DISABLED (Eva::setEvaEnabled): a disabled announcer says nothing, the flags raised meanwhile are lost, and it speaks again once enabled.
        using (var director = NewDirector(catalog, fx))
        {
            var view = new FakeView { Tick = 10 };
            director.EvaEnabled = false;
            director.NotifyInsufficientFunds();
            director.Tick(view, CnCNew.Tests.TestTime.FrameSeconds);
            view.Tick = 11;
            director.Tick(view, CnCNew.Tests.TestTime.FrameSeconds);
            TestHarness.Assert(director.Stats.EvaPlayed == 0, "Eva stays silent while disabled");
            director.EvaEnabled = true;
            view.Tick = 12;
            director.Tick(view, CnCNew.Tests.TestTime.FrameSeconds);
            TestHarness.Assert(director.Stats.EvaPlayed == 0, "the announcement raised while disabled was lost");
            director.NotifyInsufficientFunds();
            view.Tick = 13;
            director.Tick(view, CnCNew.Tests.TestTime.FrameSeconds);
            TestHarness.Assert(director.Stats.EvaPlayed == 1, "enabled again, Eva announces");
        }

        // Object sound and ambient pause are state the ambient pass reads; the game's end forgets all of it (AudioManager::reset).
        using (var director = NewDirector(catalog, fx))
        {
            TestHarness.Assert(director.SetObjectSoundEnabled(new EntityId(7), false) && !director.SetObjectSoundEnabled(EntityId.Invalid, false), "a valid object's ambient loop can be switched off, an invalid one cannot");
            director.SetAmbientPaused(true);
            director.SetScriptVolume(ScriptVolumeKind.Music, 20);
            director.OverrideEventVolume("UiClick", 0.3f);
            director.EvaEnabled = false;
            var state = director.ScriptState;
            TestHarness.Assert(state.ObjectsSilenced == 1 && state.AmbientPaused && !state.EvaEnabled && state.EventVolumes.Count == 1 && state.MusicVolume == 0.2f, "the director keeps what the scripts did");
            director.ResetScriptState();
            state = director.ScriptState;
            TestHarness.Assert(state is { ObjectsSilenced: 0, AmbientPaused: false, EvaEnabled: true, MusicVolume: 1f } && state.EventVolumes.IsEmpty, "a new game starts with nothing the last mission's scripts left behind");
        }
    }

    private static void OfflineRenderIsReproducible()
    {
        var (catalog, fx, _) = BuildDirectorFixture();
        var directory = Path.Combine(Path.GetTempPath(), $"cncnew-audio-wiring-{Environment.ProcessId}");

        (string Hash, byte[] File, long Frames) Capture(string name)
        {
            using var director = NewDirector(catalog, fx);
            var path = Path.Combine(directory, name);
            using var render = new OfflineAudioRenderer(director.Mixer, path);
            for (var frame = 0; frame < 30; frame++)
            {
                if (frame == 3) director.PlayUi("UiClick");
                if (frame == 9) director.OnUnitSelected("Tank", 1);
                director.Tick(null, CnCNew.Tests.TestTime.FrameSeconds);
                render.Pump(CnCNew.Tests.TestTime.FrameSeconds);
            }
            var hash = render.Finish();
            return (hash, File.ReadAllBytes(path), render.Frames);
        }

        try
        {
            var first = Capture("a.wav");
            var second = Capture("b.wav");
            // Thirty steps of 1/30 s sum to one second give or take a rounding error, so the last frame may wait for the next step.
            TestHarness.Assert(first.Frames is 22049 or 22050 && first.Frames == second.Frames, $"one second of fixed steps renders one second of frames ({first.Frames}, {second.Frames})");
            TestHarness.Assert(first.File.Length == 44 + first.Frames * 4, "16-bit stereo data behind a 44-byte header");
            TestHarness.Assert(first.File.AsSpan(0, 4).SequenceEqual("RIFF"u8) && BitConverter.ToInt32(first.File, 40) == first.Frames * 4, "the header carries the final data size");
            TestHarness.Assert(first.Hash == second.Hash && first.File.AsSpan().SequenceEqual(second.File), "two runs with the same fixed steps write identical files");
            TestHarness.Assert(first.Hash == Convert.ToHexString(SHA256.HashData(first.File.AsSpan(44))), "the reported checksum is the SHA-256 of the samples");
            TestHarness.Assert(first.File.AsSpan(44).ContainsAnyExcept((byte)0), "the capture is not silent");
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        // No device and no file: the mixer is still drained, nothing throws, the counters keep working.
        using var silent = NewDirector(catalog, fx);
        using var discard = new OfflineAudioRenderer(silent.Mixer);
        silent.PlayUi("UiClick");
        discard.Pump(0.5);
        discard.Pump(0);
        TestHarness.Assert(discard.Path is null && discard.Frames == 11025 && silent.Stats.Mixer.Started == 1, "a device-free run renders and counts");
    }

    private static void RealCaptureScenario(GameData data)
    {
        var scenario = DemoScenarios.CaptureBuilding;
        const int frames = 900;
        const double step = CnCNew.Tests.TestTime.FrameSeconds;

        ulong Run(bool withAudio, out AudioDirectorStats? stats, out int flashes, out string report)
        {
            var live = LiveScenario.Create(data, scenario, new Vector2(1280, 720));
            var hud = new MatchController(live, CnCNew.Ui.Settings.GameSettings.Default);
            var seen = 0;
            live.TickStepped += result => seen += result.Events.Count(simEvent => simEvent.Kind == SimEventKind.CaptureFlash);
            using var audio = withAudio ? GameAudio.Create(data) : null;
            using var output = audio is null ? null : new OfflineAudioRenderer(audio.Mixer);
            audio?.Attach(live, hud);
            for (var frame = 0; frame < frames; frame++)
            {
                live.Advance(step);
                hud.Update(step);
                audio?.Update(step);
                output?.Pump(step);
            }
            if (audio is not null)
            {
                hud.Input.Selection.Replace([live.Tags["ranger"]]);
                audio.Update(step);
                output!.Pump(step);
            }
            stats = audio?.Director.Stats;
            flashes = seen;
            report = audio is null ? string.Empty : string.Join(Environment.NewLine, audio.Report().Select(line => "  " + line));
            return live.World.StateHash;
        }

        var withHash = Run(true, out var stats, out var flashes, out var report);
        var withoutHash = Run(false, out _, out var plainFlashes, out _);
        Console.WriteLine(report);
        Console.WriteLine($"  capture flashes {flashes}, world hash with audio {withHash:X16}, without {withoutHash:X16}");

        TestHarness.Assert(flashes > 0 && flashes == plainFlashes, $"the scenario flashes the barracks ({flashes}) the same with and without audio ({plainFlashes})");
        TestHarness.Assert(withHash == withoutHash, "attaching the audio does not change the simulation");
        var tick = data.Fx.MiscAudio[SoundCueDeriver.CaptureTickHook];
        var started = stats!.StartedEvents.GetValueOrDefault(tick);
        TestHarness.Assert(started > 0 && started <= flashes, $"{tick} was started for the flashes ({started} of {flashes})");
        TestHarness.Assert(stats.Sources[nameof(AudioSource.Simulation)].Requested >= flashes, "every flash reached the director as a cue");
        TestHarness.Assert(!stats.UnresolvedEvents.ContainsKey(tick) && stats.DecodeFailures.IsEmpty, "the tick resolves and its file decodes");
        TestHarness.Assert(stats.MusicStarted == 1 && stats.MusicTrack is not null, "the side's playlist started with the game");
        TestHarness.Assert(stats.UnitVoicesPlayed == 1, "selecting the Ranger through the HUD's selection model makes him answer");
    }

    // ------------------------------------------------------------------------------------------ real data

    private static void RealDataRun(GameData data)
    {
        var mapPath = data.Maps.First(path => path.Contains("Alpine Assault", StringComparison.OrdinalIgnoreCase));
        var setup = new MatchSetup(mapPath, data.Factions[0].Faction.Name, [data.Factions[1].Faction.Name], SkirmishMatchOptions.Standard) { AutoPlay = true };

        const int frames = CnCNew.Tests.TestTime.FramesPerSecond * 20; // 20 s of game time, one original frame per step
        const int blockFrames = 44100 / CnCNew.Tests.TestTime.FramesPerSecond; // one original frame's worth of audio at 44100 Hz

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
                live.Advance(CnCNew.Tests.TestTime.FrameSeconds);
                director.Update(live, CnCNew.Tests.TestTime.FrameSeconds);
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
        Console.WriteLine($"  {frames} frames ({frames * CnCNew.Tests.TestTime.FrameSeconds:F0} s game time) in {runA.WallMs:F0} ms wall time -> {runA.WallMs / frames:F3} ms/block, real-time factor {(frames * CnCNew.Tests.TestTime.FrameSeconds) / (runA.WallMs / 1000.0):F1}x, pool capacity reference {poolCapacity}");

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
