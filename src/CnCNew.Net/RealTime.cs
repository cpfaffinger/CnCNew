using System.Diagnostics;
using System.Runtime.InteropServices;

namespace CnCNew.Net;

// This file is the one place of CnCNew.Net and CnCNew.Server that reads wall-clock time and waits (the layer rule of CnCNew.Net.Tests names it as its only exception).
// What is read here paces and measures; it never reaches the simulation, which is driven by tick bundles alone.

/// <summary>The real clock of a host: monotonic, from the moment it was created.</summary>
public sealed class SystemNetClock : INetClock
{
    private readonly long _start = Stopwatch.GetTimestamp();

    public long Milliseconds => (long)Stopwatch.GetElapsedTime(_start).TotalMilliseconds;
    public long Microseconds => Stopwatch.GetElapsedTime(_start).Ticks / (TimeSpan.TicksPerMillisecond / 1000);
}

/// <summary>
/// The pacing of a host loop (docs/NETWORK-PROTOCOL.md section 13.4). A loop waits on its socket with a timeout, so an arriving datagram wakes it at once; how fine the timeout
/// itself is depends on the operating system's timer. Windows wakes a waiting thread on its timer interrupt, by default every 15.6 ms - coarser than a tick at 60 Hz. While a
/// <see cref="FineTimer"/> is held, the process asks Windows for 1 ms (<c>timeBeginPeriod</c>); other systems need nothing.
/// </summary>
public sealed class FineTimer : IDisposable
{
    private bool _held;

    public FineTimer()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            _held = timeBeginPeriod(1) == 0;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            _held = false;
        }
    }

    /// <summary>Whether a finer timer was asked for and granted (always false outside Windows, where it is not needed).</summary>
    public bool Held => _held;

    public void Dispose()
    {
        if (!_held) return;
        _held = false;
        timeEndPeriod(1);
    }

    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint timeBeginPeriod(uint milliseconds);

    [DllImport("winmm.dll", ExactSpelling = true)]
    private static extern uint timeEndPeriod(uint milliseconds);
}
