using System.Collections.Immutable;
using CnCNew.Audio.Catalog;

namespace CnCNew.Audio.Selection;

/// <summary>
/// Round-robin/anti-repeat memory of one event instance (<c>AudioEventRTS::m_playingAudioIndex</c>, initially −1).
/// The owner — e.g. a drawable's ambient sound or a unit's voice slot — keeps one state per event it plays.
/// </summary>
public sealed class SoundSelectionState
{
    public int PlayingIndex { get; set; } = -1;

    public SoundSelectionState Clone() => new() { PlayingIndex = PlayingIndex };
}

/// <summary>One iteration of an event: the body sound(s) and the delay before them.</summary>
public sealed record SoundIteration(ImmutableArray<AudioSoundFile> Body, float DelayMilliseconds);

/// <summary>
/// Everything chosen when an event starts (<c>AudioEventRTS::generateFilename</c> + <c>generatePlayInfo</c>).
/// <see cref="LoopCount"/> follows <c>decreaseLoopCount</c>: 0 loops forever, n plays n times; events without
/// <c>LOOP</c> play once.
/// </summary>
public sealed record SoundPlan(
    AudioEventInfo Event,
    AudioSoundFile? Attack,
    SoundIteration First,
    AudioSoundFile? Decay,
    float PitchShift,
    float VolumeShift,
    int LoopCount,
    TimeOfDay TimeOfDay);

public enum LimitDecision
{
    Play,
    PlayAndStopOldest,
    Reject,
}

/// <summary>
/// Deterministic sound choice. The draws happen in the reference order so that a recorded seed reproduces a session:
/// body index (RANDOM only), delay, pitch shift, volume shift, attack index, decay index.
/// </summary>
public static class SoundSelector
{
    /// <summary>Picks the sounds and play parameters for a new instance; null when the event has nothing to play.</summary>
    public static SoundPlan? Pick(AudioEventInfo info, SeededRng rng, SoundSelectionState history, TimeOfDay timeOfDay = TimeOfDay.Afternoon)
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(rng);
        ArgumentNullException.ThrowIfNull(history);
        var first = NextIteration(info, rng, history, timeOfDay);
        if (first is null) return null;

        var pitch = rng.NextFloat(info.PitchShiftMin, info.PitchShiftMax);
        // Volume shifts are negative ratios: the instance plays between (1 + shift) and 1 of its volume.
        var volumeShift = rng.NextFloat(1f + info.VolumeShift, 1f);
        var attack = info.Attack.IsEmpty ? null : info.Attack[rng.NextInt(0, info.Attack.Length - 1)];
        var decay = info.Decay.IsEmpty ? null : info.Decay[rng.NextInt(0, info.Decay.Length - 1)];
        var loopCount = (info.Control & AudioControlFlags.Loop) != 0 ? info.LoopCount : 1;
        return new SoundPlan(info, attack, first, decay, pitch, volumeShift, loopCount, timeOfDay);
    }

    /// <summary>
    /// The body of the next iteration (called again for every loop, like <c>startNextLoop</c> → <c>generateFilename</c>).
    /// RANDOM draws an index and, with more than two sounds, avoids repeating the previous one by taking the next index;
    /// without RANDOM the sounds play round-robin. ALL plays the whole list starting at the chosen index (the reference
    /// declares ALL but its <c>advanceNextPlayPortion</c> never acts on it; see docs/AUDIO.md).
    /// </summary>
    public static SoundIteration? NextIteration(AudioEventInfo info, SeededRng rng, SoundSelectionState history, TimeOfDay timeOfDay)
    {
        ImmutableArray<AudioSoundFile> body;
        if (info.Type != AudioType.SoundEffect)
        {
            if (info.File is null) return null;
            body = [info.File];
        }
        else
        {
            var list = SoundsFor(info, timeOfDay);
            if (list.IsEmpty) return null;
            int which;
            if ((info.Control & AudioControlFlags.Random) != 0)
            {
                which = rng.NextInt(0, list.Length - 1);
                if (which == history.PlayingIndex && list.Length > 2) which = (which + 1) % list.Length;
                history.PlayingIndex = which;
            }
            else
            {
                history.PlayingIndex = history.PlayingIndex == int.MaxValue ? 0 : history.PlayingIndex + 1;
                which = history.PlayingIndex % list.Length;
            }
            body = (info.Control & AudioControlFlags.All) != 0
                ? [..Enumerable.Range(0, list.Length).Select(offset => list[(which + offset) % list.Length])]
                : [list[which]];
        }
        var delay = rng.NextFloat(info.DelayMin, info.DelayMax);
        return new SoundIteration(body, delay);
    }

    /// <summary>The time-of-day variant list, falling back to <c>Sounds</c> when the variant is empty (the reference never selects the variants).</summary>
    public static ImmutableArray<AudioSoundFile> SoundsFor(AudioEventInfo info, TimeOfDay timeOfDay)
    {
        var variant = timeOfDay switch
        {
            TimeOfDay.Morning => info.SoundsMorning,
            TimeOfDay.Evening => info.SoundsEvening,
            TimeOfDay.Night => info.SoundsNight,
            _ => info.Sounds,
        };
        return variant.IsEmpty ? info.Sounds : variant;
    }

    /// <summary>
    /// <c>MilesAudioManager::doesViolateLimit</c>: a limit of 0 is unlimited; at the limit an INTERRUPT event replaces
    /// the oldest instance, any other event is dropped.
    /// </summary>
    public static LimitDecision CheckLimit(int limit, bool interrupt, int playingInstances)
    {
        if (limit <= 0 || playingInstances < limit) return LimitDecision.Play;
        return interrupt ? LimitDecision.PlayAndStopOldest : LimitDecision.Reject;
    }
}
