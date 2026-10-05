using System.Runtime.InteropServices;
using CnCNew.Audio.Mixing;

namespace CnCNew.Client.Stride.Audio;

/// <summary>
/// Feeds <see cref="SoftwareMixer.Render"/> to the Windows sound device through the classic waveOut API (<c>winmm.dll</c>, no extra package). The mixer already produces
/// finished interleaved stereo float, so this class carries no per-voice or spatialization state: it is one continuous 16-bit stream of <see cref="BufferCount"/> buffers
/// of <see cref="BufferFrames"/> frames that a pump thread refills as the device finishes them (see <c>docs/GAME-AUDIO.md</c>).
/// <para>
/// Why not Stride's own audio layer: on the development machine <c>AudioLayer.SourceCreate</c> of Stride 4.3 raises a native access violation when a
/// <c>SoundInstance</c> is built for a <c>DynamicSoundSource</c> (every constructor variant, engine and listener fine). A native crash cannot be caught, so the client
/// died at start-up as soon as a window was opened. Nothing of <c>Stride.Audio</c> is used any more.
/// </para>
/// <para>
/// When no audio device is available — no hardware, the device is in use exclusively, or any other failure at the device boundary — <see cref="Start"/> catches it once,
/// counts it in <see cref="DeviceFailures"/>, keeps the reason in <see cref="FailureReason"/> and every other member becomes a no-op: the client keeps running silently
/// (<see cref="ClientAudio"/> prints the one warning).
/// </para>
/// </summary>
internal sealed class AudioOutput : IDisposable
{
    /// <summary>20 ms at 44100 Hz; four of them (<see cref="BufferCount"/>) give about 80 ms of output latency.</summary>
    public const int BufferFrames = 882;
    public const int BufferCount = 4;
    private const int PumpIntervalMilliseconds = 4;

    private const int WaveMapper = -1;
    private const uint CallbackNull = 0;
    private const ushort WaveFormatPcm = 1;
    private const uint HeaderDone = 0x1;

    private readonly SoftwareMixer _mixer;
    private readonly float[] _floatScratch = new float[BufferFrames * 2];
    private readonly short[] _shortScratch = new short[BufferFrames * 2];
    private readonly IntPtr[] _headers = new IntPtr[BufferCount];
    private readonly IntPtr[] _data = new IntPtr[BufferCount];
    private IntPtr _device;
    private Thread? _pumpThread;
    private volatile bool _running;
    private long _deviceFailures;
    private long _underruns;
    private bool _disposed;

    public AudioOutput(SoftwareMixer mixer) => _mixer = mixer ?? throw new ArgumentNullException(nameof(mixer));

    public SoftwareMixer Mixer => _mixer;

    /// <summary>True once a device was opened and the stream is playing.</summary>
    public bool IsAvailable => _running;

    /// <summary>Failed device opens (no hardware, in use, ...); 0 means the device came up on the first try.</summary>
    public long DeviceFailures => Interlocked.Read(ref _deviceFailures);

    /// <summary>Why the device did not open, or <see langword="null"/>.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>Detected starvations of the stream (every queued buffer had already finished when the pump looked); 0 before <see cref="Start"/> or while unavailable.</summary>
    public long Underruns => Interlocked.Read(ref _underruns);

    /// <summary>Opens the default audio device and starts streaming; safe to call once, a no-op if already started or already failed.</summary>
    public void Start()
    {
        if (_disposed || _running || _device != IntPtr.Zero) return;
        try
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("The waveOut output exists on Windows only.");
            var format = new WaveFormat
            {
                FormatTag = WaveFormatPcm,
                Channels = 2,
                SamplesPerSecond = (uint)_mixer.SampleRate,
                BitsPerSample = 16,
                BlockAlign = 4,
                AverageBytesPerSecond = (uint)_mixer.SampleRate * 4,
                ExtraSize = 0,
            };
            var result = WaveOutOpen(out _device, WaveMapper, ref format, IntPtr.Zero, IntPtr.Zero, CallbackNull);
            if (result != 0) throw new InvalidOperationException($"waveOutOpen failed with MMSYSERR {result}.");
            var headerSize = Marshal.SizeOf<WaveHeader>();
            for (var index = 0; index < BufferCount; index++)
            {
                _data[index] = Marshal.AllocHGlobal(BufferFrames * 4);
                _headers[index] = Marshal.AllocHGlobal(headerSize);
                Marshal.StructureToPtr(new WaveHeader { Data = _data[index], BufferLength = BufferFrames * 4 }, _headers[index], false);
                result = WaveOutPrepareHeader(_device, _headers[index], headerSize);
                if (result != 0) throw new InvalidOperationException($"waveOutPrepareHeader failed with MMSYSERR {result}.");
                Fill(index);
            }

            _running = true;
            _pumpThread = new Thread(Pump) { IsBackground = true, Name = "CnCNew Audio Pump", Priority = ThreadPriority.AboveNormal };
            _pumpThread.Start();
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            FailureReason = $"{exception.GetType().Name}: {exception.Message}";
            Interlocked.Increment(ref _deviceFailures);
            _running = false;
            Release();
        }
    }

    /// <summary>Renders one mixer block into buffer <paramref name="index"/> and queues it on the device.</summary>
    private void Fill(int index)
    {
        var frames = _mixer.Render(_floatScratch);
        var samples = Math.Min(frames * 2, _shortScratch.Length);
        for (var sample = 0; sample < samples; sample++)
            _shortScratch[sample] = (short)Math.Clamp(MathF.Round(_floatScratch[sample] * short.MaxValue), short.MinValue, short.MaxValue);
        Array.Clear(_shortScratch, samples, _shortScratch.Length - samples);
        Marshal.Copy(_shortScratch, 0, _data[index], _shortScratch.Length);
        var result = WaveOutWrite(_device, _headers[index], Marshal.SizeOf<WaveHeader>());
        if (result != 0) throw new InvalidOperationException($"waveOutWrite failed with MMSYSERR {result}.");
    }

    /// <summary>Refills every buffer the device has finished, oldest first.</summary>
    private void Pump()
    {
        var next = 0;
        var flagsOffset = (int)Marshal.OffsetOf<WaveHeader>(nameof(WaveHeader.Flags));
        while (_running && !_disposed)
        {
            try
            {
                var queued = 0;
                for (var step = 0; step < BufferCount; step++)
                {
                    var index = (next + step) % BufferCount;
                    var flags = (uint)Marshal.ReadInt32(_headers[index], flagsOffset);
                    if ((flags & HeaderDone) == 0) { queued++; continue; }
                    Fill(index);
                    next = (index + 1) % BufferCount;
                }

                // Every buffer was finished when we looked: the device played silence in between.
                if (queued == 0) Interlocked.Increment(ref _underruns);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                FailureReason = $"{exception.GetType().Name}: {exception.Message}";
                Interlocked.Increment(ref _deviceFailures);
                _running = false;
                return;
            }

            Thread.Sleep(PumpIntervalMilliseconds);
        }
    }

    private void Release()
    {
        var headerSize = Marshal.SizeOf<WaveHeader>();
        if (_device != IntPtr.Zero)
        {
            WaveOutReset(_device);
            for (var index = 0; index < BufferCount; index++)
                if (_headers[index] != IntPtr.Zero) WaveOutUnprepareHeader(_device, _headers[index], headerSize);
            WaveOutClose(_device);
        }

        for (var index = 0; index < BufferCount; index++)
        {
            if (_headers[index] != IntPtr.Zero) Marshal.FreeHGlobal(_headers[index]);
            if (_data[index] != IntPtr.Zero) Marshal.FreeHGlobal(_data[index]);
            _headers[index] = IntPtr.Zero;
            _data[index] = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _running = false;
        _pumpThread?.Join(TimeSpan.FromSeconds(1));
        Release();
        _device = IntPtr.Zero;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveFormat
    {
        public ushort FormatTag;
        public ushort Channels;
        public uint SamplesPerSecond;
        public uint AverageBytesPerSecond;
        public ushort BlockAlign;
        public ushort BitsPerSample;
        public ushort ExtraSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WaveHeader
    {
        public IntPtr Data;
        public uint BufferLength;
        public uint BytesRecorded;
        public IntPtr User;
        public uint Flags;
        public uint Loops;
        public IntPtr Next;
        public IntPtr Reserved;
    }

    [DllImport("winmm.dll", EntryPoint = "waveOutOpen")]
    private static extern int WaveOutOpen(out IntPtr device, int deviceId, ref WaveFormat format, IntPtr callback, IntPtr instance, uint flags);

    [DllImport("winmm.dll", EntryPoint = "waveOutPrepareHeader")]
    private static extern int WaveOutPrepareHeader(IntPtr device, IntPtr header, int size);

    [DllImport("winmm.dll", EntryPoint = "waveOutUnprepareHeader")]
    private static extern int WaveOutUnprepareHeader(IntPtr device, IntPtr header, int size);

    [DllImport("winmm.dll", EntryPoint = "waveOutWrite")]
    private static extern int WaveOutWrite(IntPtr device, IntPtr header, int size);

    [DllImport("winmm.dll", EntryPoint = "waveOutReset")]
    private static extern int WaveOutReset(IntPtr device);

    [DllImport("winmm.dll", EntryPoint = "waveOutClose")]
    private static extern int WaveOutClose(IntPtr device);
}
