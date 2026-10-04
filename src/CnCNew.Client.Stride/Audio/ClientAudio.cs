using System.Globalization;
using CnCNew.Game.Audio;
using CnCNew.Game.Data;
using CnCNew.Ui.Settings;

namespace CnCNew.Client.Stride.Audio;

/// <param name="DeviceFree">Never open a sound device (a smoke test): the mixer is rendered and discarded, so the director and its counters still run.</param>
/// <param name="CaptureDirectory">Render the mixer into <c>audio.wav</c> of this directory instead of a device (<c>--audio-capture</c>).</param>
/// <param name="Muted">Start muted (<c>--mute</c>).</param>
internal sealed record ClientAudioOptions(bool DeviceFree, string? CaptureDirectory, bool Muted);

/// <summary>
/// The client's sound for its whole lifetime: one <see cref="GameAudio"/> (director and mixer) and exactly one consumer of the mixer — the OS device
/// (<see cref="AudioOutput"/>), a WAV capture, or a renderer that discards. When the device does not open, one warning goes to the console and the mixer is
/// rendered without a device from then on, so the game keeps running, the command queue of the mixer stays drained and the counters stay meaningful.
/// </summary>
internal sealed class ClientAudio : IDisposable
{
    public const string CaptureFileName = "audio.wav";

    private readonly AudioOutput? _device;
    private readonly OfflineAudioRenderer? _offline;
    private readonly string _output;
    private bool _disposed;

    public ClientAudio(GameData data, AudioSettings settings, ClientAudioOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Game = GameAudio.Create(data, settings);
        Game.Muted = options.Muted;
        if (options.CaptureDirectory is { } directory)
        {
            _offline = new OfflineAudioRenderer(Game.Mixer, Path.Combine(directory, CaptureFileName));
            _output = $"capture '{_offline.Path}' (no device)";
        }
        else if (options.DeviceFree)
        {
            _offline = new OfflineAudioRenderer(Game.Mixer);
            _output = "none (device-free run, rendered and discarded)";
        }
        else
        {
            _device = new AudioOutput(Game.Mixer);
            _device.Start();
            if (_device.IsAvailable) _output = "device";
            else
            {
                Console.WriteLine($"warning: no audio device available, the game runs silently ({_device.FailureReason ?? "unknown reason"}).");
                _output = $"none (device failed: {_device.FailureReason ?? "unknown reason"}; rendered and discarded)";
                _device.Dispose();
                _device = null;
                _offline = new OfflineAudioRenderer(Game.Mixer);
            }
        }
    }

    public GameAudio Game { get; }

    /// <summary>Once per rendered frame, after the game advanced; without a device this also renders the frame's audio.</summary>
    public void Update(double seconds)
    {
        if (_disposed) return;
        Game.Update(seconds);
        _offline?.Pump(seconds);
    }

    /// <summary>Closes a capture and returns the report of the run: the output line (with the capture's checksum) and the lines of <see cref="GameAudio.Report"/>.</summary>
    public IReadOnlyList<string> Finish()
    {
        var culture = CultureInfo.InvariantCulture;
        var lines = new List<string>();
        var muted = Game.Muted ? ", muted" : string.Empty;
        if (_offline is not null)
        {
            var hash = _offline.Finish();
            lines.Add(string.Create(culture, $"audio output: {_output}{muted}; {_offline.Frames} frames, peak {_offline.Peak:F4}, samples SHA-256={hash}"));
        }
        else lines.Add(string.Create(culture, $"audio output: {_output}{muted}; underruns {_device?.Underruns ?? 0}"));
        lines.AddRange(Game.Report());
        return lines;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _device?.Dispose();
        _offline?.Dispose();
        Game.Dispose();
    }
}
