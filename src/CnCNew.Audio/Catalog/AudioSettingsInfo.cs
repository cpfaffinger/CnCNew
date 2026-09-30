using System.Collections.Immutable;

namespace CnCNew.Audio.Catalog;

/// <summary>
/// Typed <c>AudioSettings</c> block (<c>AudioSettings.h</c>, parse table <c>audioSettingsFieldParseTable</c> in
/// <c>GameAudio.cpp</c>). Percentages are ratios; durations stay in milliseconds (the reference converts them to logic
/// frames with <c>parseDurationUnsignedInt</c>).
/// </summary>
public sealed record AudioSettingsInfo(
    string AudioRoot,
    string SoundsFolder,
    string MusicFolder,
    string StreamingFolder,
    string SoundsExtension,
    bool UseDigital,
    bool UseMidi,
    int OutputRate,
    int OutputBits,
    int OutputChannels,
    int SampleCount2D,
    int SampleCount3D,
    int StreamCount,
    ImmutableArray<string> Preferred3DProviders,
    string Default2DSpeakerType,
    string Default3DSpeakerType,
    float MinSampleVolume,
    int GlobalMinRange,
    int GlobalMaxRange,
    int TimeBetweenDrawableSoundsMs,
    int TimeToFadeAudioMs,
    long AudioFootprintInBytes,
    float Relative2DVolume,
    float DefaultSoundVolume,
    float Default3DSoundVolume,
    float DefaultSpeechVolume,
    float DefaultMusicVolume,
    float MicrophoneDesiredHeightAboveTerrain,
    float MicrophoneMaxPercentageBetweenGroundAndCamera,
    float ZoomMinDistance,
    float ZoomMaxDistance,
    float ZoomSoundVolumePercentageAmount)
{
    /// <summary>Speaker names of <c>TheSpeakerTypes</c> in <c>GameAudio.cpp</c>.</summary>
    public static ImmutableArray<string> SpeakerTypes { get; } = ["2 Speakers", "Headphones", "Surround Sound", "4 Speaker", "5.1 Surround", "7.1 Surround"];

    /// <summary>
    /// Default 2D sound volume after <c>Relative2DVolume</c> (<c>OptionPreferences::getSoundVolume</c>): a negative relative
    /// value lowers 2D sounds by that fraction.
    /// </summary>
    public float Effective2DSoundVolume => Relative2DVolume < 0 ? DefaultSoundVolume * (1 + Math.Max(-1f, Relative2DVolume)) : DefaultSoundVolume;

    /// <summary>Default 3D sound volume after <c>Relative2DVolume</c> (<c>OptionPreferences::get3DSoundVolume</c>).</summary>
    public float Effective3DSoundVolume => Relative2DVolume > 0 ? Default3DSoundVolume * (1 - Math.Min(1f, Relative2DVolume)) : Default3DSoundVolume;
}

/// <summary>
/// <c>MiscAudio</c> hooks (<c>MiscAudio.h</c>, <c>INIMiscAudio.cpp</c>): engine situations mapped to event names,
/// keyed by the INI key. <c>NoSound</c> is the reference's explicit silence.
/// </summary>
public sealed record MiscAudioInfo(ImmutableSortedDictionary<string, string> Hooks)
{
    public static ImmutableArray<string> KnownHooks { get; } =
    [
        "RadarNotifyUnitUnderAttackSound", "RadarNotifyHarvesterUnderAttackSound", "RadarNotifyStructureUnderAttackSound",
        "RadarNotifyUnderAttackSound", "RadarNotifyInfiltrationSound", "RadarNotifyOnlineSound", "RadarNotifyOfflineSound",
        "DefectorTimerTickSound", "DefectorTimerDingSound", "LockonTickSound", "AllCheerSound", "BattleCrySound",
        "GUIClickSound", "NoCanDoSound", "StealthDiscoveredSound", "StealthNeutralizedSound", "MoneyDepositSound",
        "MoneyWithdrawSound", "BuildingDisabled", "BuildingReenabled", "VehicleDisabled", "VehicleReenabled",
        "SplatterVehiclePilotsBrain", "TerroristInCarMoveVoice", "TerroristInCarAttackVoice", "TerroristInCarSelectVoice",
        "CrateHeal", "CrateShroud", "CrateSalvage", "CrateFreeUnit", "CrateMoney", "UnitPromoted", "RepairSparks",
        "SabotageShutDownBuilding", "SabotageResetTimeBuilding", "AircraftWheelScreech",
    ];
}
