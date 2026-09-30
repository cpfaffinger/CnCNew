using System.Collections.Immutable;
using System.Text;
using CnCNew.Audio.Catalog;
using CnCNew.Audio.Selection;
using CnCNew.Data.Compilation;
using CnCNew.Data.Ini;

file sealed class MemorySource(Dictionary<string, string> files) : IDefinitionSource
{
    public IEnumerable<string> EnumerateIniFiles() => files.Keys.Order(StringComparer.OrdinalIgnoreCase);
    public Stream OpenRead(string virtualPath) => new MemoryStream(Encoding.UTF8.GetBytes(files[virtualPath]));
    public bool Exists(string virtualPath) => files.ContainsKey(virtualPath);
}

static partial class Tests
{
    private static readonly Dictionary<string, string> CatalogFixture = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Data/INI/AudioSettings.ini"] = """
            AudioSettings
              AudioRoot = Data\Audio
              SoundsFolder = Sounds
              MusicFolder = Tracks
              StreamingFolder = Speech
              SoundsExtension = wav
              UseDigital = Yes
              UseMidi = No
              OutputRate = 48000
              OutputBits = 16
              OutputChannels = 2
              SampleCount2D = 6
              SampleCount3D = 20
              StreamCount = 2
              GlobalMinRange = 4000
              GlobalMaxRange = 400000
              TimeBetweenDrawableSounds = 900
              TimeToFadeAudio = 1500
              AudioFootprintInBytes = 1000000
              MinSampleVolume = 3
              Relative2DVolume = -10%
              DefaultSoundVolume = 80%
              Default3DSoundVolume = 60%
              DefaultSpeechVolume = 70%
              DefaultMusicVolume = 50%
              Default2DSpeakerType = "2 Speakers"
              Default3DSpeakerType = "Headphones"
              Preferred3DSW = "Software"
              MicrophoneDesiredHeightAboveTerrain = 40.0
              MicrophoneMaxPercentageBetweenGroundAndCamera = 25%
              ZoomMinDistance = 100.0
              ZoomMaxDistance = 400.0
              ZoomSoundVolumePercentageAmount = 10%
              MysterySetting = 1
            End
            """,
        ["Data/INI/Default/Music.ini"] = "MusicTrack DefaultMusicTrack\n  Filename = NoFilename\n  Volume = 100.0\nEnd\n",
        ["Data/INI/Music.ini"] = "MusicTrack Theme\n  Filename = theme.mp3\nEnd\n",
        ["Data/INI/Default/SoundEffects.ini"] = """
            AudioEvent DefaultSoundEffect
              Volume = 100
              Priority = high
              MinVolume = 40
              Control = random
              Limit = 4
              MinRange = 175
              MaxRange = 800
              PitchShift = -0 0
              Type = ui player
            End
            AudioEvent Redefined
              Limit = 9
              Sounds = aaa
            End
            """,
        ["Data/INI/SoundEffects.ini"] = """
            AudioEvent Gun
              Sounds = gun1 gun2, gun3
              Attack = gunA
              Decay = gunD
              SoundsNight = gunN
              Volume = 70
              VolumeShift = -20
              PitchShift = -10 5
              Delay = 100 250
              Control = loop all
              LoopCount = 3
              Priority = LOW
              Type = +world -ui
              MinRange = 50
              MaxRange = 700
              LowPassCutoff = 50
              Bogus = 1
            End
            AudioEvent BadPriority
              Priority = loud
              Sounds = missing1
            End
            AudioEvent Redefined
              Sounds = bbb
            End
            AudioEvent Quiet
            End
            """,
        ["Data/INI/Default/Speech.ini"] = "DialogEvent DefaultDialog\n  Volume = 100\n  Type = ui everyone\nEnd\n",
        ["Data/INI/Speech.ini"] = "DialogEvent Briefing\n  Filename = brief01.wav\n  Volume = 90\nEnd\n",
        ["Data/INI/MiscAudio.ini"] = "MiscAudio\n  GUIClickSound = Gun\n  NoCanDoSound = NoSound\n  CrateMoney = Missing\n  Unknown = Gun\nEnd\n",
    };

    private static readonly HashSet<string> CatalogFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Data/Audio/Sounds/gun1.wav", "Data/Audio/Sounds/gun2.wav", "Data/Audio/Sounds/English/gun2.wav", "Data/Audio/Sounds/gun3.wav",
        "Data/Audio/Sounds/gunA.wav", "Data/Audio/Sounds/gunD.wav", "Data/Audio/Sounds/gunN.wav", "Data/Audio/Sounds/bbb.wav",
        "Data/Audio/Tracks/theme.mp3", "Data/Audio/Speech/English/brief01.wav",
    };

    private static AudioCatalog FixtureCatalog()
        => AudioCatalog.FromGraph(DefinitionCompiler.Compile(new MemorySource(CatalogFixture)), CatalogFiles.Contains);

    public static void CatalogFromGraph()
    {
        var catalog = FixtureCatalog();
        var settings = catalog.Settings;
        Check.That(settings.AudioRoot == @"Data\Audio" && settings.SoundsExtension == "wav" && settings.OutputRate == 48000, "Settings strings and integers.");
        Check.That(settings.SampleCount2D == 6 && settings.SampleCount3D == 20 && settings.StreamCount == 2, "Channel counts.");
        Check.Near(settings.MinSampleVolume, 0.03, 1e-6, "MinSampleVolume is a percentage.");
        Check.Near(settings.Effective2DSoundVolume, 0.72, 1e-6, "Relative2DVolume −10 % lowers the 2D default.");
        Check.Near(settings.Effective3DSoundVolume, 0.6, 1e-6, "A negative Relative2DVolume leaves 3D alone.");
        Check.That(settings.Default3DSpeakerType == "Headphones" && settings.Preferred3DProviders[4] == "Software" && settings.Preferred3DProviders[0] == "", "Speaker and provider strings.");
        Check.That(settings.TimeBetweenDrawableSoundsMs == 900 && settings.GlobalMaxRange == 400000, "Durations and ranges.");

        var gun = catalog.Find("gun")!;
        Check.That(gun.Type == AudioType.SoundEffect && !gun.IsTemplate, "Gun is a sound effect.");
        Check.Near(gun.Volume, 0.7, 1e-6, "Volume percentage.");
        Check.Near(gun.VolumeShift, -0.2, 1e-6, "VolumeShift percentage.");
        Check.Near(gun.MinVolume, 0.4, 1e-6, "MinVolume inherited from DefaultSoundEffect.");
        Check.Near(gun.PitchShiftMin, 0.9, 1e-6, "PitchShift minimum becomes 1 + p/100.");
        Check.Near(gun.PitchShiftMax, 1.05, 1e-6, "PitchShift maximum becomes 1 + p/100.");
        Check.That(gun.DelayMin == 100 && gun.DelayMax == 250 && gun.LoopCount == 3 && gun.Limit == 4, "Delay, loop count and inherited limit.");
        Check.That(gun.Control == (AudioControlFlags.Loop | AudioControlFlags.All), "Plain control names replace the inherited RANDOM.");
        Check.That(gun.SoundTypes == (SoundTypeFlags.World | SoundTypeFlags.Player), "+world −ui edits the inherited type bits.");
        Check.That(gun.Priority == AudioPriority.Low && gun.MinRange == 50 && gun.MaxRange == 700, "Priority and ranges.");
        Check.Near(gun.LowPassCutoff, 0.5, 1e-6, "LowPassCutoff percentage.");
        Check.That(gun.Sounds.Select(value => value.Name).SequenceEqual(["gun1", "gun2", "gun3"]), "Sounds split on blanks and commas.");
        Check.That(gun.Sounds[0].VirtualPath == "Data/Audio/Sounds/gun1.wav" && !gun.Sounds[0].Localized, "Generic path with the default extension.");
        Check.That(gun.Sounds[1].VirtualPath == "Data/Audio/Sounds/English/gun2.wav" && gun.Sounds[1].Localized, "The localized file wins when it exists.");
        Check.That(gun.Attack.Single().IsResolved && gun.Decay.Single().IsResolved && gun.SoundsNight.Single().Name == "gunN", "Attack, decay and night variants.");

        var bad = catalog.Find("BadPriority")!;
        Check.That(bad.Priority == AudioPriority.High, "An invalid priority keeps the template value …");
        var redefined = catalog.Find("Redefined")!;
        Check.That(redefined.Limit == 4 && redefined.Sounds.Single().Name == "bbb", "A redefinition resets the event to its template.");
        var theme = catalog.Find("Theme")!;
        Check.That(theme.Type == AudioType.Music && theme.File!.VirtualPath == "Data/Audio/Tracks/theme.mp3" && theme.Volume == 1f, "Music tracks play Filename without an added extension.");
        var briefing = catalog.Find("Briefing")!;
        Check.That(briefing.Type == AudioType.Streaming && briefing.File!.VirtualPath == "Data/Audio/Speech/English/brief01.wav", "Dialog files resolve under the localized speech folder.");
        Check.That(briefing.SoundTypes == (SoundTypeFlags.Ui | SoundTypeFlags.Everyone) && Math.Abs(briefing.Volume - 0.9f) < 1e-6, "Dialog inherits DefaultDialog.");
        Check.That(catalog.Find("NoSound") is null && catalog.Find("") is null, "NoSound is explicit silence.");
        Check.That(catalog.MiscAudio.Hooks["GUIClickSound"] == "Gun" && catalog.MiscAudio.Hooks.Count == 3, "Misc audio hooks.");

        var report = catalog.Report;
        Check.That(report.SoundEffects == 4 && report.MusicTracks == 1 && report.DialogEvents == 1 && report.Templates == 3, "Counts per kind.");
        Check.That(report.UnresolvedFiles.Single() == new UnresolvedAudioFile("BadPriority", "Sounds", "missing1", "Data/Audio/Sounds/missing1.wav"), "Unresolved files are named.");
        Check.That(report.SilentEvents == 1, "An event without sounds is silent (Quiet).");
        Check.That(report.UnresolvedMiscAudioHooks.SequenceEqual(["CrateMoney = Missing"]), "Unresolved misc hooks are named.");
        Check.That(report.DiagnosticCounts["unknown-key:Bogus"] == 1 && report.DiagnosticCounts["unknown-key:MysterySetting"] == 1
            && report.DiagnosticCounts["unknown-key:Unknown"] == 1, "Unknown keys are counted per key.");
        Check.That(report.DiagnosticCounts["invalid-value"] == 1 && report.DiagnosticCounts["redefinition-reset"] == 1, "… and the invalid value and the reset are counted.");
        Check.That(report.ToLines().SequenceEqual(FixtureCatalog().Report.ToLines()), "The report is deterministic.");
    }

    public static void Selection()
    {
        var random3 = Event("R3", ["a", "b", "c"], AudioControlFlags.Random);
        var rng = new SeededRng(42);
        var history = new SoundSelectionState();
        var previous = -1;
        for (var pick = 0; pick < 300; pick++)
        {
            var plan = SoundSelector.Pick(random3, rng, history)!;
            var index = random3.Sounds.IndexOf(plan.First.Body.Single());
            Check.That(index != previous, "RANDOM with more than two sounds never repeats the previous sound.");
            previous = index;
        }

        var random2 = Event("R2", ["a", "b"], AudioControlFlags.Random);
        history = new SoundSelectionState();
        var picks = Enumerable.Range(0, 200).Select(_ => SoundSelector.Pick(random2, rng, history)!.First.Body.Single().Name).ToArray();
        Check.That(picks.Zip(picks.Skip(1)).Any(pair => pair.First == pair.Second), "With two sounds repeats are allowed (reference: size > 2).");

        var ordered = Event("O", ["a", "b", "c"], AudioControlFlags.None);
        history = new SoundSelectionState();
        var order = Enumerable.Range(0, 7).Select(_ => SoundSelector.Pick(ordered, rng, history)!.First.Body.Single().Name);
        Check.That(string.Concat(order) == "abcabca", "Without RANDOM the sounds play round-robin from the first.");

        var all = Event("A", ["a", "b", "c"], AudioControlFlags.All);
        history = new SoundSelectionState { PlayingIndex = 0 };
        Check.That(string.Concat(SoundSelector.Pick(all, rng, history)!.First.Body.Select(value => value.Name)) == "bca", "ALL plays the whole list from the chosen index.");

        var shifted = Event("S", ["a"], AudioControlFlags.Loop) with { PitchShiftMin = 0.9f, PitchShiftMax = 1.1f, VolumeShift = -0.25f, DelayMin = 100, DelayMax = 200, LoopCount = 0 };
        SoundPlan[] Run(ulong seed)
        {
            var local = new SeededRng(seed);
            var state = new SoundSelectionState();
            return [..Enumerable.Range(0, 100).Select(_ => SoundSelector.Pick(shifted, local, state)!)];
        }
        var first = Run(7);
        Check.That(first.All(plan => plan.PitchShift is >= 0.9f and <= 1.1f && plan.VolumeShift is >= 0.75f and <= 1f
            && plan.First.DelayMilliseconds is >= 100f and <= 200f && plan.LoopCount == 0), "Shifts and delays stay inside their ranges.");
        Check.That(first.Select(plan => (plan.PitchShift, plan.VolumeShift, plan.First.DelayMilliseconds)).SequenceEqual(Run(7).Select(plan => (plan.PitchShift, plan.VolumeShift, plan.First.DelayMilliseconds))), "Same seed, same choices.");
        Check.That(first.Select(plan => plan.PitchShift).Distinct().Count() > 50, "The pitch shift varies.");
        Check.That(SoundSelector.Pick(Event("Once", ["a"], AudioControlFlags.None) with { LoopCount = 5 }, rng, new SoundSelectionState())!.LoopCount == 1, "Without LOOP an event plays once.");

        var night = Event("N", ["day"], AudioControlFlags.None) with { SoundsNight = [new AudioSoundFile("night", "n.wav", false)] };
        Check.That(SoundSelector.Pick(night, rng, new SoundSelectionState(), TimeOfDay.Night)!.First.Body.Single().Name == "night", "The night variant plays at night.");
        Check.That(SoundSelector.Pick(night, rng, new SoundSelectionState(), TimeOfDay.Morning)!.First.Body.Single().Name == "day", "An empty variant falls back to Sounds.");
        Check.That(SoundSelector.Pick(Event("Empty", [], AudioControlFlags.None), rng, new SoundSelectionState()) is null, "An event without sounds yields no plan.");

        Check.That(SoundSelector.CheckLimit(0, false, 99) == LimitDecision.Play, "Limit 0 is unlimited.");
        Check.That(SoundSelector.CheckLimit(2, false, 1) == LimitDecision.Play && SoundSelector.CheckLimit(2, false, 2) == LimitDecision.Reject, "At the limit a normal event is dropped.");
        Check.That(SoundSelector.CheckLimit(2, true, 2) == LimitDecision.PlayAndStopOldest, "At the limit an INTERRUPT event replaces the oldest.");

        var golden = new SeededRng(1);
        var values = new[] { golden.NextUInt64(), golden.NextUInt64(), golden.NextUInt64() };
        Check.That(values.SequenceEqual(GoldenRng), $"SeededRng golden sequence ({string.Join(", ", values.Select(value => $"0x{value:X16}"))}).");
    }

    private static readonly ulong[] GoldenRng = [0x4B46A55DF3611B9BUL, 0xD7E1F1410E763EF4UL, 0x5F14EC66975F9B06UL];

    internal static AudioEventInfo Event(string name, string[] sounds, AudioControlFlags control)
        => new(name, AudioType.SoundEffect, false, null, null, 1f, 0f, 0f, 1f, 1f, 0, 0, 0, 1, AudioPriority.Normal,
            SoundTypeFlags.World, control, [..sounds.Select(sound => new AudioSoundFile(sound, $"Data/Audio/Sounds/{sound}.wav", false))],
            [], [], [], [], [], 0f, 0f, 0f, new IniSourceLocation("test", 1, 1));
}
