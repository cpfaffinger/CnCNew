using System.Reflection;
using System.Runtime.InteropServices;

namespace CnCNew.Client.Stride;

/// <summary>
/// Gives the game window its own icon (title bar, task bar, Alt+Tab). The executable already carries it as its <c>ApplicationIcon</c>, but a run through
/// <c>dotnet CnCNew.Client.Stride.dll</c> is the process of <c>dotnet.exe</c>, and the window of the SDL layer has none of its own: so the same .ico,
/// embedded in this assembly, is handed to the window with WM_SETICON. A failure never stops the game.
/// </summary>
internal static class WindowIcon
{
    private const uint WM_SETICON = 0x0080;
    private const int ICON_SMALL = 0;
    private const int ICON_BIG = 1;
    private const uint IconVersion = 0x00030000;

    public static void Apply(IntPtr window)
    {
        try
        {
            if (window == IntPtr.Zero || !OperatingSystem.IsWindows()) return;
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("cncnew.ico");
            if (stream is null) return;
            var bytes = new byte[stream.Length];
            stream.ReadExactly(bytes);
            var small = Create(bytes, 32);
            var big = Create(bytes, 256);
            if (small != IntPtr.Zero) SendMessage(window, WM_SETICON, (IntPtr)ICON_SMALL, small);
            if (big != IntPtr.Zero) SendMessage(window, WM_SETICON, (IntPtr)ICON_BIG, big);
        }
        catch (Exception problem) when (problem is DllNotFoundException or EntryPointNotFoundException or ArgumentException or InvalidOperationException or IOException)
        {
            Console.WriteLine($"window icon: not set ({problem.GetType().Name})");
        }
    }

    // The images of the .ico are PNG: the entry for a size is handed to the system as it is.
    private static IntPtr Create(byte[] ico, int size)
    {
        var count = BitConverter.ToUInt16(ico, 4);
        for (var i = 0; i < count; i++)
        {
            var entry = 6 + i * 16;
            var width = ico[entry] == 0 ? 256 : ico[entry];
            if (width != size) continue;
            var length = BitConverter.ToInt32(ico, entry + 8);
            var offset = BitConverter.ToInt32(ico, entry + 12);
            var image = new byte[length];
            Array.Copy(ico, offset, image, 0, length);
            return CreateIconFromResourceEx(image, (uint)length, true, IconVersion, size, size, 0);
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", EntryPoint = "CreateIconFromResourceEx")]
    private static extern IntPtr CreateIconFromResourceEx(byte[] bits, uint size, [MarshalAs(UnmanagedType.Bool)] bool icon, uint version, int width, int height, uint flags);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
