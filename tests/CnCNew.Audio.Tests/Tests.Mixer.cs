using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Numerics;
using System.Security.Cryptography;
using CnCNew.Audio;
using CnCNew.Audio.Catalog;
using CnCNew.Audio.Mixing;
using CnCNew.Audio.Movies;
using CnCNew.Audio.Selection;

static partial class Tests
{
    public static void AttenuationAndPanning()
    {
        Check.That(Spatial.Attenuation(DistanceModel.Inverse, 50, 100, 1000) == 1f, "Inside the minimum distance the volume is full.");
        Check.Near(Spatial.Attenuation(DistanceModel.Inverse, 200, 100, 1000), 0.5, 1e-6, "Inverse: min/d.");
        Check.Near(Spatial.Attenuation(DistanceModel.Inverse, 999, 100, 1000), 100.0 / 999, 1e-6, "Inverse just inside the maximum.");
        Check.That(Spatial.Attenuation(DistanceModel.Inverse, 1000, 100, 1000) == 0f, "At the maximum distance the sound is silent.");
        Check.Near(Spatial.Attenuation(DistanceModel.Linear, 550, 100, 1000), 0.5, 1e-6, "Linear: halfway between min and max.");
        Check.That(Spatial.Attenuation(DistanceModel.Inverse, 1e6f, 100, 0) == 1f, "No maximum distance disables attenuation.");

        foreach (var pan in new[] { -1f, -0.5f, 0f, 0.3f, 1f })
        {
            var (left, right) = Spatial.EqualPowerPan(pan);
            Check.Near(left * left + right * right, 1, 1e-6, $"Equal power at pan {pan}.");
        }
        var centre = Spatial.EqualPowerPan(0);
        Check.Near(centre.Left, Math.Sqrt(0.5), 1e-6, "Centre is −3 dB on the left …");
        Check.Near(centre.Right, Math.Sqrt(0.5), 1e-6, "… and on the right.");
        Check.That(Spatial.EqualPowerPan(-1) is (1f, < 1e-6f), "Hard left.");

        var listener = AudioListener.Default;
        Check.Near(Spatial.Pan(listener, new Vector3(100, 0, 0)), 1, 1e-6, "+X is right for a listener facing +Y with Z up.");
        Check.Near(Spatial.Pan(listener, new Vector3(-100, 0, 0)), -1, 1e-6, "−X is left.");
        Check.Near(Spatial.Pan(listener, new Vector3(0, 100, 0)), 0, 1e-6, "Straight ahead is centred.");
        var turned = listener with { Forward = Vector3.UnitX };
        Check.Near(Spatial.Pan(turned, new Vector3(0, -100, 0)), 1, 1e-6, "Turning the listener to +X puts −Y on the right.");

        // Through the mixer: DC 0.5 at 200 units ahead, min 100, max 1000 → 0.5 · 0.5 · cos(π/4).
        using var mixer = new SoftwareMixer(new MixerOptions(SampleRate: 1000));
        mixer.Play(PlayRequest.ForBuffer(Dc(0.5f, 1000, 1000), AudioBus.Sfx, new Vector3(0, 200, 0)) with { MinDistance = 100, MaxDistance = 1000 });
        mixer.Play(PlayRequest.ForBuffer(Dc(0.5f, 1000, 1000), AudioBus.Sfx, new Vector3(200, 0, 0)) with { MinDistance = 100, MaxDistance = 1000 });
        var block = new float[64];
        mixer.Render(block);
        Check.Near(block[10], 0.25 * Math.Sqrt(0.5), 1e-5, "Left: centred voice only.");
        Check.Near(block[11], 0.25 * Math.Sqrt(0.5) + 0.25, 1e-5, "Right: centred voice plus the hard-right voice.");
        Check.That(mixer.ActiveVoiceCount == 2 && Math.Abs(mixer.PeakRight - block[11]) < 1e-6, "Voice count and peak meter.");
    }

    public static void MicrophonePlacement()
    {
        var settings = FixtureCatalog().Settings;
        var (high, highZoom) = Microphone.Place(Vector3.Zero, 0f, new Vector3(0, -300, 400), settings);
        Check.That(Vector3.Distance(high, new Vector3(0, -30, 40)) < 1e-4f, "A high camera puts the microphone at the desired height.");
        Check.Near(highZoom, 0.9, 1e-6, "Far cameras lose the zoom share of the 3D volume.");
        var (low, lowZoom) = Microphone.Place(Vector3.Zero, 0f, new Vector3(0, -60, 80), settings);
        Check.That(Vector3.Distance(low, new Vector3(0, -15, 20)) < 1e-4f && lowZoom == 1f, "A low camera caps the microphone at the maximum percentage; close cameras get full volume.");
        var (_, midZoom) = Microphone.Place(Vector3.Zero, 0f, new Vector3(0, -150, 200), settings);
        Check.Near(midZoom, 1 - 100.0 / 300 * 0.1, 1e-6, "Between the zoom distances the boost is linear.");
    }

    public static void VoiceStealingAndLimits()
    {
        using var mixer = new SoftwareMixer(new MixerOptions(SampleRate: 1000, PositionalVoices: 2, FlatVoices: 1));
        var buffer = Dc(0.1f, 1000, 5000);
        var at = new Vector3(0, 10, 0);
        var low = mixer.Play(PlayRequest.ForBuffer(buffer, AudioBus.Sfx, at) with { Priority = AudioPriority.Low });
        var normal = mixer.Play(PlayRequest.ForBuffer(buffer, AudioBus.Sfx, at) with { Priority = AudioPriority.Normal });
        var high = mixer.Play(PlayRequest.ForBuffer(buffer, AudioBus.Sfx, at) with { Priority = AudioPriority.High });
        var lowest = mixer.Play(PlayRequest.ForBuffer(buffer, AudioBus.Sfx, at) with { Priority = AudioPriority.Lowest });
        var flat = mixer.Play(PlayRequest.ForBuffer(buffer, AudioBus.Ui));
        var block = new float[32];
        mixer.Render(block);
        Check.That(!mixer.IsActive(low) && mixer.IsActive(normal) && mixer.IsActive(high) && !mixer.IsActive(lowest), "The lowest-priority voice is stolen; nothing lower than LOWEST exists to steal.");
        Check.That(mixer.IsActive(flat) && mixer.ActiveVoiceCount == 3, "Pools are counted separately.");
        var stats = mixer.Statistics;
        Check.That(stats.Stolen == 1 && stats.RejectedNoChannel == 1 && stats.Started == 4, "Stealing and rejection are counted.");

        using var limited = new SoftwareMixer(new MixerOptions(SampleRate: 1000));
        var first = limited.Play(PlayRequest.ForBuffer(buffer, AudioBus.Ui) with { LimitGroup = "Gun", Limit = 1 });
        var second = limited.Play(PlayRequest.ForBuffer(buffer, AudioBus.Ui) with { LimitGroup = "gun", Limit = 1 });
        limited.Render(block);
        Check.That(limited.IsActive(first) && !limited.IsActive(second) && limited.Statistics.RejectedLimit == 1, "A second instance over the limit is dropped.");
        var interrupting = limited.Play(PlayRequest.ForBuffer(buffer, AudioBus.Ui) with { LimitGroup = "Gun", Limit = 1, Interrupt = true });
        limited.Render(block);
        Check.That(!limited.IsActive(first) && limited.IsActive(interrupting) && limited.Statistics.ReplacedByLimit == 1, "An INTERRUPT instance replaces the oldest.");

        using var culling = new SoftwareMixer(new MixerOptions(SampleRate: 1000, MinSampleVolume: 0.05f));
        var far = culling.Play(PlayRequest.ForBuffer(buffer, AudioBus.Sfx, new Vector3(0, 2000, 0)) with { MaxDistance = 1000 });
        var global = culling.Play(PlayRequest.ForBuffer(buffer, AudioBus.Sfx, new Vector3(0, 2000, 0)) with { MaxDistance = 1000, Cullable = false });
        var muted = culling.Play(PlayRequest.ForBuffer(buffer, AudioBus.Ui, volume: 0.01f));
        var fading = culling.Play(PlayRequest.ForBuffer(buffer, AudioBus.Sfx, new Vector3(0, 100, 0)) with { MinDistance = 10, MaxDistance = 1000 });
        culling.Render(block);
        Check.That(!culling.IsActive(far) && culling.IsActive(global) && !culling.IsActive(muted) && culling.IsActive(fading), "Distance and mute culling at admission.");
        culling.SetListener(AudioListener.Default with { Position = new Vector3(0, -150, 0) });
        culling.Render(block);
        Check.That(!culling.IsActive(fading) && culling.Statistics.CulledQuiet == 1, "A 3D voice that drops below MinSampleVolume is culled.");
        Check.That(culling.Statistics is { RejectedDistance: 1, RejectedMuted: 1 }, "Culling is counted.");
    }

    public static void LoopSeamAndFades()
    {
        var ramp = PcmBuffer.FromOwnedArray(1000, 1, [..Enumerable.Range(0, 10).Select(index => (index + 1) / 10f)]);
        using var mixer = new SoftwareMixer(new MixerOptions(SampleRate: 1000));
        mixer.Play(PlayRequest.ForBuffer(ramp, AudioBus.Ui, loopCount: 3));
        var output = new float[2 * 40];
        mixer.Render(output);
        var left = Enumerable.Range(0, 40).Select(frame => output[2 * frame]).ToArray();
        var expected = Enumerable.Range(0, 30).Select(index => (index % 10 + 1) / 10f).ToArray();
        Check.That(left.Take(30).SequenceEqual(expected), "At pitch 1 the loop repeats the buffer sample-exactly, without a gap at the seams.");
        Check.That(left[30] == 0f && left.Skip(31).All(value => value == 0f), "After three loops the voice ends.");
        Check.That(mixer.ActiveVoiceCount == 0 && mixer.Statistics.Finished == 1, "The finished voice is released.");

        using var half = new SoftwareMixer(new MixerOptions(SampleRate: 1000));
        half.Play(PlayRequest.ForBuffer(ramp, AudioBus.Ui, pitch: 0.5f, loopCount: 0));
        var slow = new float[2 * 60];
        half.Render(slow);
        Check.Near(slow[2 * 19], (1.0 + 0.1) / 2, 1e-6, "At half speed the seam interpolates between the last and the first frame.");
        Check.Near(slow[2 * 20], 0.1, 1e-6, "… and continues with the first frame.");
        Check.That(half.ActiveVoiceCount == 1, "LoopCount 0 loops until stopped.");

        using var fades = new SoftwareMixer(new MixerOptions(SampleRate: 1000));
        var handle = fades.Play(PlayRequest.ForBuffer(Dc(1f, 1000, 10000), AudioBus.Ui) with { FadeInSeconds = 0.01f });
        var block = new float[2 * 20];
        fades.Render(block);
        Check.Near(block[0], 0.1, 1e-6, "Fade-in starts at one step.");
        Check.Near(block[2 * 9], 1, 1e-6, "Fade-in reaches full volume after 10 ms.");
        fades.Stop(handle, 0.01f);
        fades.Render(block);
        Check.Near(block[0], 0.9, 1e-5, "Fade-out ramps down …");
        Check.That(block[2 * 10] == 0f && fades.ActiveVoiceCount == 0 && fades.Statistics.Stopped == 1, "… and releases the voice.");

        // A script's per-event volume reaches a voice that is already playing (AudioManager::adjustVolumeOfPlayingAudio).
        using var live = new SoftwareMixer(new MixerOptions(SampleRate: 1000));
        var loud = live.Play(PlayRequest.ForBuffer(Dc(0.4f, 1000, 10000), AudioBus.Ui));
        var other = live.Play(PlayRequest.ForBuffer(Dc(0.4f, 1000, 10000), AudioBus.Ui));
        var samples = new float[2 * 200];
        live.Render(samples);
        Check.Near(samples[^2], 0.8, 1e-6, "Two voices add up.");
        live.SetVoiceVolume(loud, 0.25f);
        live.Render(samples);
        Check.Near(samples[^2], 0.5, 1e-5, "A new volume for one voice changes only that voice.");
        live.SetVoiceVolume(other, 0f);
        live.SetVoiceVolume(new VoiceHandle(999_999), 0.5f);
        live.Render(samples);
        Check.Near(samples[^2], 0.1, 1e-5, "A voice at volume zero is silent; a handle that does not exist is ignored.");
    }

    public static void MusicCrossfade()
    {
        using var mixer = new SoftwareMixer(new MixerOptions(SampleRate: 1000));
        var first = mixer.PlayMusic(new PcmBufferStream(Dc(0.8f, 1000, 200)), loop: true);
        var block = new float[2 * 500];
        mixer.Render(block);
        Check.That(block.All(value => Math.Abs(value - 0.8f) < 1e-6), "A looping stream plays without gaps across rewinds.");
        var second = mixer.PlayMusic(new PcmBufferStream(Dc(-0.4f, 1000, 1000)), crossfadeSeconds: 0.1f, loop: false);
        var crossfade = new float[2 * 200];
        mixer.Render(crossfade);
        Check.Near(crossfade[2 * 49], 0.8 * 0.5 + -0.4 * 0.5, 1e-4, "Linear crossfade at the midpoint.");
        Check.Near(crossfade[2 * 150], -0.4, 1e-6, "After the crossfade only the new track plays.");
        Check.That(!mixer.IsActive(first) && mixer.IsActive(second) && mixer.ActiveMusicCount == 1, "The old track is released.");
        mixer.SetBusVolume(AudioBus.Music, 0.5f);
        mixer.Render(block);
        Check.Near(block[0], -0.4 * (1 - 0.5 / 500), 1e-6, "A bus change ramps across the block …");
        Check.Near(block[^2], -0.2, 1e-5, "… to the new music bus volume.");
        mixer.Render(block);
        mixer.Render(block);
        Check.That(mixer.ActiveMusicCount == 0, "A non-looping track ends.");
    }

    public static void EventVoices()
    {
        var info = Event("Loop", ["a", "b"], AudioControlFlags.Loop) with
        {
            LoopCount = 2,
            DelayMin = 5,
            DelayMax = 5,
            Attack = [new AudioSoundFile("att", "att.wav", false)],
            Decay = [new AudioSoundFile("dec", "dec.wav", false)],
        };
        var buffers = new Dictionary<string, PcmBuffer>(StringComparer.OrdinalIgnoreCase)
        {
            ["Data/Audio/Sounds/a.wav"] = Dc(0.1f, 1000, 10),
            ["Data/Audio/Sounds/b.wav"] = Dc(0.2f, 1000, 10),
            ["att.wav"] = Dc(0.3f, 1000, 10),
            ["dec.wav"] = Dc(0.4f, 1000, 10),
        };
        var settings = FixtureCatalog().Settings;
        var history = new SoundSelectionState();
        var plan = SoundSelector.Pick(info, new SeededRng(3), history)!;
        var request = PlayRequest.ForEvent(plan, history, settings, path => buffers.GetValueOrDefault(path));
        Check.That(request.Bus == AudioBus.Ui && request.Pool == VoicePool.Flat && request.LoopCount == 2 && request.LimitGroup == "Loop", "Event requests map bus, pool, loops and limit group.");
        using var mixer = new SoftwareMixer(new MixerOptions(SampleRate: 1000));
        mixer.Play(request);
        var output = new float[2 * 60];
        mixer.Render(output);
        var levels = Enumerable.Range(0, 60).Select(frame => MathF.Round(output[2 * frame], 3)).ToArray();
        var expected = Enumerable.Repeat(0f, 5).Concat(Enumerable.Repeat(0.3f, 10)).Concat(Enumerable.Repeat(0.1f, 10))
            .Concat(Enumerable.Repeat(0f, 5)).Concat(Enumerable.Repeat(0.2f, 10)).Concat(Enumerable.Repeat(0.4f, 10)).Concat(Enumerable.Repeat(0f, 10)).ToArray();
        Check.That(levels.SequenceEqual(expected), $"Delay, attack, round-robin loop bodies and decay in order ({string.Join(' ', levels)}).");
        Check.That(history.PlayingIndex == 0, "Loops run on a copy of the round-robin state, like the reference's copied event.");

        var global = info with { SoundTypes = SoundTypeFlags.Global, Priority = AudioPriority.Critical };
        var positional = PlayRequest.ForEvent(SoundSelector.Pick(global, new SeededRng(1), new SoundSelectionState())!, new SoundSelectionState(), settings, _ => null, new Vector3(1, 2, 3));
        Check.That(positional.Bus == AudioBus.Sfx && positional.MinDistance == 4000 && positional.MaxDistance == 400000 && !positional.Cullable, "GLOBAL events use the global ranges and are never culled.");
    }

    public static void MixerDeterminism()
    {
        string Run(ulong seed)
        {
            var info = Event("Amb", ["a", "b", "c"], AudioControlFlags.Loop | AudioControlFlags.Random) with { LoopCount = 0, PitchShiftMin = 0.8f, PitchShiftMax = 1.2f, DelayMin = 0, DelayMax = 30, MinRange = 50, MaxRange = 900 };
            var buffers = new Dictionary<string, PcmBuffer>(StringComparer.OrdinalIgnoreCase)
            {
                ["Data/Audio/Sounds/a.wav"] = Sine(220, 22050, 700),
                ["Data/Audio/Sounds/b.wav"] = Sine(330, 44100, 900),
                ["Data/Audio/Sounds/c.wav"] = Sine(440, 11025, 300),
            };
            var settings = FixtureCatalog().Settings;
            var rng = new SeededRng(seed);
            using var mixer = new SoftwareMixer(new MixerOptions(SampleRate: 44100, PositionalVoices: 6, Seed: seed));
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var block = new float[2 * 441];
            var bytes = new byte[block.Length * 4];
            var handles = new List<VoiceHandle>();
            for (var step = 0; step < 200; step++)
            {
                if (step % 7 == 0)
                {
                    var history = new SoundSelectionState();
                    var plan = SoundSelector.Pick(info, rng, history)!;
                    var position = new Vector3(rng.NextFloat(-600, 600), rng.NextFloat(-600, 600), 0);
                    handles.Add(mixer.Play(PlayRequest.ForEvent(plan, history, settings, path => buffers.GetValueOrDefault(path), position)));
                }
                if (step % 13 == 0 && handles.Count > 2) mixer.Stop(handles[^3], 0.05f);
                if (step % 5 == 0) mixer.SetListener(AudioListener.Default with { Position = new Vector3(step, -step, 0), Forward = Vector3.Normalize(new Vector3(MathF.Sin(step * 0.05f), MathF.Cos(step * 0.05f), 0)) });
                if (step == 50) mixer.PlayMusic(new PcmBufferStream(Sine(110, 22050, 5000)), crossfadeSeconds: 0.5f);
                mixer.Render(block);
                for (var index = 0; index < block.Length; index++) BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(index * 4), block[index]);
                hash.AppendData(bytes);
            }
            return $"{Convert.ToHexString(hash.GetHashAndReset())} started {mixer.Statistics.Started} stolen {mixer.Statistics.Stolen}";
        }
        var first = Run(11);
        Check.That(first == Run(11), "Two runs of the same script render byte-identical output.");
        Check.That(first != Run(12), "A different seed changes the output.");
        Console.WriteLine($"       mixer script hash {first}");
    }

    public static void BinkHeaders()
    {
        var file = Bink('i', frames: 3, width: 640, height: 480, fpsNumerator: 30000, fpsDenominator: 1001, tracks: [(44100, 0x3000, 7)]);
        var header = BinkHeader.Parse(file, file.Length);
        Check.That(header.Signature == "BIKi" && header.FrameCount == 3 && header.Width == 640 && header.Height == 480, "Signature, frames and size.");
        Check.Near(header.FramesPerSecond, 29.97, 0.001, "Frame rate numerator/denominator.");
        Check.Near(header.DurationSeconds, 3 * 1001 / 30000.0, 1e-9, "Duration.");
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

    // ------------------------------------------------------------------------------------------ fixtures

    private static PcmBuffer Dc(float value, int rate, int frames) => PcmBuffer.FromOwnedArray(rate, 1, Enumerable.Repeat(value, frames).ToArray());

    private static PcmBuffer Sine(double frequency, int rate, int frames)
        => PcmBuffer.FromOwnedArray(rate, 1, [..Enumerable.Range(0, frames).Select(index => (float)(0.4 * Math.Sin(2 * Math.PI * frequency * index / rate)))]);

    /// <summary>A synthetic Bink 1 file: header, one audio track table, frame index and 16 payload bytes per frame.</summary>
    private static byte[] Bink(char revision, int frames, int width, int height, uint fpsNumerator, uint fpsDenominator, (ushort Rate, ushort Flags, uint Id)[] tracks)
    {
        var headerLength = 44 + tracks.Length * 12 + (frames + 1) * 4;
        var total = headerLength + frames * 16;
        var data = new byte[total];
        var span = data.AsSpan();
        "BIK"u8.CopyTo(span);
        data[3] = (byte)revision;
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], (uint)(total - 8));
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], (uint)frames);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], 16);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], (uint)frames);
        BinaryPrimitives.WriteUInt32LittleEndian(span[20..], (uint)width);
        BinaryPrimitives.WriteUInt32LittleEndian(span[24..], (uint)height);
        BinaryPrimitives.WriteUInt32LittleEndian(span[28..], fpsNumerator);
        BinaryPrimitives.WriteUInt32LittleEndian(span[32..], fpsDenominator);
        BinaryPrimitives.WriteUInt32LittleEndian(span[40..], (uint)tracks.Length);
        var offset = 44;
        foreach (var _ in tracks) { BinaryPrimitives.WriteUInt32LittleEndian(span[offset..], 1234); offset += 4; }
        foreach (var track in tracks)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(span[offset..], track.Rate);
            BinaryPrimitives.WriteUInt16LittleEndian(span[(offset + 2)..], track.Flags);
            offset += 4;
        }
        foreach (var track in tracks) { BinaryPrimitives.WriteUInt32LittleEndian(span[offset..], track.Id); offset += 4; }
        for (var frame = 0; frame <= frames; frame++)
        {
            var position = (uint)(headerLength + frame * 16);
            BinaryPrimitives.WriteUInt32LittleEndian(span[offset..], frame == 0 ? position | 1 : position);
            offset += 4;
        }
        return data;
    }
}
