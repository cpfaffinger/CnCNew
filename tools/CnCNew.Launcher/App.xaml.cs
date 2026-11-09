using System.Globalization;
using System.Windows;
using System.Windows.Threading;

namespace CnCNew.Launcher.Shell;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // The window is English; numbers and dates are written the same everywhere.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

        string? preview = null, screenshot = null, replace = null;
        var delay = 900;
        for (var index = 0; index < e.Args.Length; index++)
        {
            switch (e.Args[index])
            {
                case "--preview" when index + 1 < e.Args.Length: preview = e.Args[++index]; break;
                case "--screenshot" when index + 1 < e.Args.Length: screenshot = e.Args[++index]; break;
                case "--delay" when index + 1 < e.Args.Length && int.TryParse(e.Args[index + 1], out var milliseconds): delay = milliseconds; index++; break;
                case "--data-dir" when index + 1 < e.Args.Length: AppInfo.DataDirectory = Path.GetFullPath(e.Args[++index]); break;
                case SelfUpdate.ReplaceArgument when index + 1 < e.Args.Length: replace = e.Args[++index]; break;
            }
        }
        DispatcherUnhandledException += OnUnhandled;

        // After a self-update the new launcher removes the old file once the old process has ended.
        if (replace is not null) Task.Run(() => LauncherLog.Write(SelfUpdate.RemovePredecessor(replace) ? $"removed the old launcher {replace}" : $"could not remove the old launcher {replace}"));

        new MainWindow(preview, screenshot, delay).Show();
    }

    private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LauncherLog.Write("crash: " + e.Exception);
        if (Environment.GetEnvironmentVariable("CNCNEW_LAUNCHER_NO_DIALOG") == "1") { Current.Shutdown(1); return; }
        MessageBox.Show($"The launcher hit an unexpected problem and will close.\n\n{e.Exception.Message}\n\nThe details are in {LauncherLog.Path}", "CnCNew Launcher", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
        Current.Shutdown(1);
    }
}
