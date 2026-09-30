using System.Collections.Immutable;
using System.Numerics;
using CnCNew.Audio.Catalog;
using CnCNew.Audio.Selection;

namespace CnCNew.Audio.Mixing;

/// <summary>What a voice plays: a raw buffer, or a catalog event with its pre-decoded sounds.</summary>
public abstract record VoiceSource;

public sealed record BufferVoiceSource(PcmBuffer Buffer) : VoiceSource;

/// <summary>
/// An event instance: <paramref name="Plan"/> was chosen on the game thread; later loop iterations are chosen by the
/// mixer with its own RNG from a copy of <paramref name="History"/>, like the reference loops on its copied
/// <c>AudioEventRTS</c>. <paramref name="Buffers"/> maps virtual paths to decoded sounds; a missing entry is skipped.
/// </summary>
public sealed record EventVoiceSource(
    SoundPlan Plan,
    ImmutableDictionary<string, PcmBuffer> Buffers,
    SoundSelectionState History) : VoiceSource;

/// <param name="LoopCount">0 loops until stopped, n plays the body n times (reference <c>LoopCount</c> semantics).</param>
/// <param name="LimitGroup">Instances with the same group count against <paramref name="Limit"/> (the event name in the reference).</param>
/// <param name="Cullable">May be dropped beyond <paramref name="MaxDistance"/> or below the minimum volume (false for GLOBAL and CRITICAL events).</param>
/// <param name="Streaming">Uses the stream pool (dialog events) instead of the flat 2D pool when not positional.</param>
/// <param name="DelayMilliseconds">Silence before a buffer source starts; event sources take every delay from their plan.</param>
public sealed record PlayRequest(
    VoiceSource Source,
    AudioBus Bus,
    Vector3? Position = null,
    float Volume = 1f,
    float Pitch = 1f,
    int LoopCount = 1,
    AudioPriority Priority = AudioPriority.Normal,
    float MinDistance = 0f,
    float MaxDistance = 0f,
    string? LimitGroup = null,
    int Limit = 0,
    bool Interrupt = false,
    bool Cullable = true,
    bool Streaming = false,
    float DelayMilliseconds = 0f,
    float FadeInSeconds = 0f)
{
    public VoicePool Pool => Position is not null ? VoicePool.Positional : Streaming ? VoicePool.Stream : VoicePool.Flat;

    public static PlayRequest ForBuffer(PcmBuffer buffer, AudioBus bus, Vector3? position = null, float volume = 1f, float pitch = 1f, int loopCount = 1)
        => new(new BufferVoiceSource(buffer), bus, position, volume, pitch, loopCount);

    /// <summary>
    /// Builds the request for a picked event like <c>AudioManager::addAudioEvent</c> + <c>MilesAudioManager::playAudioEvent</c>:
    /// volume = event volume × volume shift, pitch = pitch shift, GLOBAL events use the global ranges, sound effects go
    /// to <see cref="AudioBus.Sfx"/> when positional and <see cref="AudioBus.Ui"/> otherwise, music to
    /// <see cref="AudioBus.Music"/>, dialog to <see cref="AudioBus.Voice"/>. <paramref name="load"/> returns the decoded
    /// sound of a virtual path (typically <see cref="SoundCache.GetOrLoad"/>); every sound the event may play is loaded.
    /// </summary>
    public static PlayRequest ForEvent(
        SoundPlan plan,
        SoundSelectionState history,
        AudioSettingsInfo settings,
        Func<string, PcmBuffer?> load,
        Vector3? position = null,
        float? volumeOverride = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(load);
        var info = plan.Event;
        var buffers = ImmutableDictionary.CreateBuilder<string, PcmBuffer>(StringComparer.OrdinalIgnoreCase);
        var candidates = info.Type == AudioType.SoundEffect
            ? SoundSelector.SoundsFor(info, plan.TimeOfDay).Concat(info.Attack).Concat(info.Decay)
            : info.File is null ? [] : [info.File];
        foreach (var file in candidates)
        {
            if (file.VirtualPath is null || buffers.ContainsKey(file.VirtualPath)) continue;
            if (load(file.VirtualPath) is { } buffer) buffers[file.VirtualPath] = buffer;
        }

        var global = (info.SoundTypes & SoundTypeFlags.Global) != 0;
        var bus = info.Type switch
        {
            AudioType.Music => AudioBus.Music,
            AudioType.Streaming => AudioBus.Voice,
            _ => position is null ? AudioBus.Ui : AudioBus.Sfx,
        };
        return new PlayRequest(
            new EventVoiceSource(plan, buffers.ToImmutable(), history.Clone()),
            bus,
            position,
            (volumeOverride ?? info.Volume) * plan.VolumeShift,
            plan.PitchShift,
            plan.LoopCount,
            info.Priority,
            global ? settings.GlobalMinRange : info.MinRange,
            global ? settings.GlobalMaxRange : info.MaxRange,
            info.Name,
            info.Limit,
            (info.Control & AudioControlFlags.Interrupt) != 0,
            Cullable: !global && info.Priority != AudioPriority.Critical,
            Streaming: info.Type == AudioType.Streaming);
    }
}
