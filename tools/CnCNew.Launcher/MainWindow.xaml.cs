using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace CnCNew.Launcher.Shell;

public partial class MainWindow : System.Windows.Window
{
    private readonly MainViewModel _model;
    private readonly string? _screenshot;

    public MainWindow(string? preview, string? screenshot, int delayMilliseconds = 900)
    {
        InitializeComponent();
        _model = new MainViewModel();
        DataContext = _model;
        _screenshot = screenshot;
        MinimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        CloseButton.Click += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == Key.Escape && _model.SettingsOpen) _model.SettingsOpen = false; };
        if (preview is not null) _model.LoadPreview(preview);
        Loaded += async (_, _) =>
        {
            if (preview is null) await _model.BeginAsync();
        };
        ContentRendered += (_, _) =>
        {
            if (_screenshot is null) return;
            // A look without a person: render the window to a PNG once it has settled, then end.
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMilliseconds) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                Capture(_screenshot);
                Application.Current.Shutdown();
            };
            timer.Start();
        };
    }

    private void OnScrimClick(object sender, MouseButtonEventArgs e) => _model.SettingsOpen = false;

    private void Capture(string path)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var width = (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX);
        var height = (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY);
        var bitmap = new RenderTargetBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
        bitmap.Render((Visual)Content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
