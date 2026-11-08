using System.Diagnostics;
using System.Globalization;

namespace CnCNew.Client.Stride;

/// <summary>
/// The time from the start of the process to the milestones of the client's start (the first drawn frame above all), written as <c>startup:</c> lines so a log shows what a person waits for.
/// </summary>
internal static class StartupClock
{
    private static readonly DateTime Started = Process.GetCurrentProcess().StartTime;
    private static bool _firstFrame;

    /// <summary>Milliseconds since the process started.</summary>
    public static double Milliseconds => (DateTime.Now - Started).TotalMilliseconds;

    public static void Note(string what) => Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"startup: {what} at {Milliseconds:F0} ms after process start"));

    /// <summary>Called by every drawn frame; the first one is the line that matters.</summary>
    public static void FrameDrawn(string what)
    {
        if (_firstFrame) return;
        _firstFrame = true;
        Note($"first frame drawn ({what})");
    }
}
