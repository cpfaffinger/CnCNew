using System.Collections.Immutable;
using CnCNew.Data.Ini;

namespace CnCNew.Audio.Catalog;

/// <summary><c>AudioType</c> of <c>AudioEventInfo.h</c>: the INI block kind decides it (MusicTrack, DialogEvent, AudioEvent).</summary>
public enum AudioType
{
    Music,
    Streaming,
    SoundEffect,
}

/// <summary><c>AudioPriority</c> / <c>theAudioPriorityNames</c>; the order is significant (higher steals lower).</summary>
public enum AudioPriority
{
    Lowest,
    Low,
    Normal,
    High,
    Critical,
}

/// <summary><c>SoundType</c> / <c>theSoundTypeNames</c> (INI key <c>Type</c>).</summary>
[Flags]
public enum SoundTypeFlags
{
    None = 0,
    Ui = 0x0001,
    World = 0x0002,
    Shrouded = 0x0004,
    Global = 0x0008,
    Voice = 0x0010,
    Player = 0x0020,
    Allies = 0x0040,
    Enemies = 0x0080,
    Everyone = 0x0100,
}

/// <summary>
/// <c>AudioControl</c> / <c>theAudioControlNames</c> (INI key <c>Control</c>). There is no ORDERED flag in the reference:
/// an event without <see cref="Random"/> plays its sounds round-robin (<c>AudioEventRTS::generateFilename</c>).
/// </summary>
[Flags]
public enum AudioControlFlags
{
    None = 0,
    Loop = 0x0001,
    Random = 0x0002,
    All = 0x0004,
    PostDelay = 0x0008,
    Interrupt = 0x0010,
}

/// <summary>Time of day for the <c>SoundsMorning/Evening/Night</c> variants (<c>TimeOfDay</c> of the reference without INVALID).</summary>
public enum TimeOfDay
{
    Morning,
    Afternoon,
    Evening,
    Night,
}

/// <summary>A sound name from the INI and the content path it resolves to; <see cref="VirtualPath"/> is null when no file exists.</summary>
public sealed record AudioSoundFile(string Name, string? VirtualPath, bool Localized)
{
    public bool IsResolved => VirtualPath is not null;
}

/// <summary>
/// Typed <c>AudioEventInfo</c> (<c>AudioEventInfo.h</c>, parse table <c>AudioEventInfo::m_audioEventInfo</c> in
/// <c>INIAudioEventInfo.cpp</c>), one record for all three INI kinds like the reference. Values are converted exactly as the
/// reference parsers do: percentages to ratios (<c>parsePercentToReal</c>), <c>PitchShift</c> to playback-rate factors
/// <c>1 + p/100</c> (<c>parsePitchShift</c>), <c>Delay</c> in milliseconds (<c>parseDelay</c>).
/// </summary>
public sealed record AudioEventInfo(
    string Name,
    AudioType Type,
    bool IsTemplate,
    string? Filename,
    AudioSoundFile? File,
    float Volume,
    float VolumeShift,
    float MinVolume,
    float PitchShiftMin,
    float PitchShiftMax,
    int DelayMin,
    int DelayMax,
    int Limit,
    int LoopCount,
    AudioPriority Priority,
    SoundTypeFlags SoundTypes,
    AudioControlFlags Control,
    ImmutableArray<AudioSoundFile> Sounds,
    ImmutableArray<AudioSoundFile> SoundsMorning,
    ImmutableArray<AudioSoundFile> SoundsEvening,
    ImmutableArray<AudioSoundFile> SoundsNight,
    ImmutableArray<AudioSoundFile> Attack,
    ImmutableArray<AudioSoundFile> Decay,
    float LowPassCutoff,
    float MinRange,
    float MaxRange,
    IniSourceLocation Location)
{
    /// <summary><c>AudioEventInfo::isPermanentSound</c>: looping forever until stopped.</summary>
    public bool IsPermanent => (Control & AudioControlFlags.Loop) != 0 && LoopCount == 0;

    /// <summary>Every file reference of the event (music/streaming file, sounds, variants, attack, decay).</summary>
    public IEnumerable<AudioSoundFile> AllFiles
    {
        get
        {
            if (File is not null) yield return File;
            foreach (var list in new[] { Sounds, SoundsMorning, SoundsEvening, SoundsNight, Attack, Decay })
                foreach (var file in list)
                    yield return file;
        }
    }
}
