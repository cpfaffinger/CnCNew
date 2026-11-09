using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;

namespace CnCNew.Launcher.Shell;

public enum Phase { Starting, Welcome, Installing, Ready, Failed }

/// <summary>
/// Everything the window shows and does. It holds no logic of its own that a test could want: downloading, checking, unpacking and repairing are the engine's; the
/// view model orders them, keeps the lists the window binds to and turns a result into a page.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly System.Windows.Threading.Dispatcher _dispatcher = Application.Current.Dispatcher;
    private readonly HttpClient _http;
    private readonly LauncherEngine _engine;
    private readonly ISystemProbe _probe = new SystemProbe();
    private LauncherSettings _settings;
    private InstallLayout _layout;
    private ReleaseInfo? _release;
    private UpdateOffer _offer = new(UpdateKind.None, null, null, null);
    private bool _launcherOnlyUpdate;
    private CancellationTokenSource? _cancel;
    private StepList _stepList = new(false);
    private Phase _phase = Phase.Starting;
    private string _headline = "Looking at your computer";
    private string _lead = "";
    private double _progress;
    private string _progressText = "";
    private string _statusLine = "";
    private string _failure = "";
    private string _releaseLine = "";
    private bool _canInstall;
    private bool _verifySimulation;
    private bool _checkForUpdates;
    private string _updateText = "";
    private bool _updateVisible;
    private bool _settingsOpen;
    private string _confirmText = "";
    private Action? _confirmed;
    private bool _busy;
    private string _installedText = "";
    private string _simulationNote = "";
    private bool _preview;

    public MainViewModel()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"CnCNew-Launcher/{AppInfo.Version}");
        var api = Environment.GetEnvironmentVariable("CNCNEW_LAUNCHER_API") ?? "https://api.github.com";
        var repository = Environment.GetEnvironmentVariable("CNCNEW_LAUNCHER_REPOSITORY") ?? ReleaseClient.DefaultRepository;
        _engine = new LauncherEngine(_http, new ProcessToolRunner(), AppInfo.DataDirectory, new ReleaseClient(_http, repository, api), log: LauncherLog.Write);
        _engine.Progress += progress => _dispatcher.BeginInvoke(() => OnEngineProgress(progress));
        _settings = LauncherSettings.Load(AppInfo.SettingsPath);
        _layout = new InstallLayout(_settings.EffectiveInstallFolder);
        _verifySimulation = _settings.VerifySimulation;
        _checkForUpdates = _settings.CheckForUpdates;

        InstallCommand = new RelayCommand(() => _ = InstallAsync(), () => CanInstall && !_busy);
        PlayCommand = new RelayCommand(Play, () => IsReady);
        RecheckCommand = new RelayCommand(() => _ = BeginAsync(), () => !_busy);
        ChangeFolderCommand = new RelayCommand(ChangeFolder, () => !_busy);
        UpdateCommand = new RelayCommand(() => _ = UpdateAsync(), () => !_busy && UpdateVisible);
        DismissUpdateCommand = new RelayCommand(() => UpdateVisible = false);
        SkipUpdateCommand = new RelayCommand(SkipUpdate);
        CancelCommand = new RelayCommand(() => _cancel?.Cancel(), () => IsInstalling);
        ToggleSettingsCommand = new RelayCommand(() => SettingsOpen = !SettingsOpen);
        RepairCommand = new RelayCommand(() => { SettingsOpen = false; _ = RepairAsync(); }, () => !_busy && _layout.ReadRelease() is not null);
        UninstallCommand = new RelayCommand(AskUninstall, () => !_busy && _layout.ReadRelease() is not null);
        ConfirmYesCommand = new RelayCommand(() => { var action = _confirmed; ConfirmText = ""; action?.Invoke(); });
        ConfirmNoCommand = new RelayCommand(() => ConfirmText = "");
        OpenFolderCommand = new RelayCommand(() => Open(_layout.Root));
        OpenLogCommand = new RelayCommand(() => Open(AppInfo.LogDirectory));
        RetryCommand = new RelayCommand(() => _ = BeginAsync());
    }

    public ObservableCollection<CheckItem> Checks { get; } = [];
    public ObservableCollection<StepItem> Steps { get; } = [];

    public ICommand InstallCommand { get; }
    public ICommand PlayCommand { get; }
    public ICommand RecheckCommand { get; }
    public ICommand ChangeFolderCommand { get; }
    public ICommand UpdateCommand { get; }
    public ICommand DismissUpdateCommand { get; }
    public ICommand SkipUpdateCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ToggleSettingsCommand { get; }
    public ICommand RepairCommand { get; }
    public ICommand UninstallCommand { get; }
    public ICommand ConfirmYesCommand { get; }
    public ICommand ConfirmNoCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand OpenLogCommand { get; }
    public ICommand RetryCommand { get; }

    public Phase Phase
    {
        get => _phase;
        private set
        {
            if (!Set(ref _phase, value)) return;
            Raise(nameof(IsStarting)); Raise(nameof(IsWelcome)); Raise(nameof(IsInstalling)); Raise(nameof(IsReady)); Raise(nameof(IsFailed));
        }
    }

    public bool IsStarting => _phase == Phase.Starting;
    public bool IsWelcome => _phase == Phase.Welcome;
    public bool IsInstalling => _phase == Phase.Installing;
    public bool IsReady => _phase == Phase.Ready;
    public bool IsFailed => _phase == Phase.Failed;

    public string Headline { get => _headline; private set => Set(ref _headline, value); }
    public string Lead { get => _lead; private set => Set(ref _lead, value); }
    public double Progress { get => _progress; private set => Set(ref _progress, value); }
    public string ProgressText { get => _progressText; private set => Set(ref _progressText, value); }
    public string StatusLine { get => _statusLine; private set => Set(ref _statusLine, value); }
    public string Failure { get => _failure; private set => Set(ref _failure, value); }
    public string ReleaseLine { get => _releaseLine; private set => Set(ref _releaseLine, value); }
    public bool CanInstall { get => _canInstall; private set => Set(ref _canInstall, value); }
    public string InstalledText { get => _installedText; private set => Set(ref _installedText, value); }
    public string InstallFolder => _layout.Root;
    public string LauncherVersion => $"Launcher {AppInfo.Version}";
    public string SimulationNote { get => _simulationNote; private set => Set(ref _simulationNote, value); }

    public bool VerifySimulation
    {
        get => _verifySimulation;
        set { if (Set(ref _verifySimulation, value)) SaveSettings(); }
    }

    public bool CheckForUpdates
    {
        get => _checkForUpdates;
        set { if (Set(ref _checkForUpdates, value)) SaveSettings(); }
    }

    public string UpdateText { get => _updateText; private set => Set(ref _updateText, value); }
    public bool UpdateVisible { get => _updateVisible; set => Set(ref _updateVisible, value); }
    public bool SettingsOpen { get => _settingsOpen; set => Set(ref _settingsOpen, value); }
    public string ConfirmText
    {
        get => _confirmText;
        private set { if (Set(ref _confirmText, value)) Raise(nameof(ConfirmOpen)); }
    }

    public bool ConfirmOpen => _confirmText.Length > 0;
    public string UpdateNotes => _offer.Release?.Notes ?? "";

    // ---- the flow ----------------------------------------------------------------------------------------------------------------------------

    /// <summary>The first thing after the window opens: a finished install goes straight to Play, anything else to the checks.</summary>
    public async Task BeginAsync()
    {
        if (_preview) return;
        _busy = true;
        try
        {
            SettingsOpen = false;
            _settings = LauncherSettings.Load(AppInfo.SettingsPath);
            _layout = new InstallLayout(_settings.EffectiveInstallFolder);
            Raise(nameof(InstallFolder));
            var state = _layout.Inspect();
            LauncherLog.Write($"start: launcher {AppInfo.Version}, folder {_layout.Root}, state {state}");
            if (state == InstallState.Ready)
            {
                EnterReady();
                if (_settings.CheckForUpdates) _ = CheckForUpdateAsync();
                return;
            }
            await ShowChecksAsync();
        }
        finally
        {
            _busy = false;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task ShowChecksAsync()
    {
        Phase = Phase.Starting;
        Headline = "Looking at your computer";
        Lead = "A few quick checks before anything is downloaded.";
        CanInstall = false;
        ReleaseLine = "Looking for the latest version...";
        var folder = _layout.Root;
        var results = await Task.Run(() => Preflight.Run(_probe, folder));
        Checks.Clear();
        foreach (var result in results.Where(result => result.Id != "folder" || result.Status != CheckStatus.Ok)) Checks.Add(new CheckItem(result));

        var check = await _engine.CheckLatestAsync();
        _release = check.Release;
        if (check.Release is { } release)
        {
            if (release.ClientPackage is null || release.ClientHash is null)
            {
                _release = null;
                ReleaseLine = $"Version {release.Version} is published, but its game package for Windows is not complete yet. Look again later.";
            }
            else ReleaseLine = $"Version {release.Version}  -  download about {LauncherEngine.Size(release.ClientPackage.Size)}";
        }
        else ReleaseLine = check.Message;
        CanInstall = Preflight.CanInstall(results) && _release is not null;
        Phase = Phase.Welcome;
        Headline = _layout.Inspect() == InstallState.ClientOnly ? "Finish the installation" : "Install CnCNew";
        Lead = "CnCNew needs your own copy of the game from Steam. The launcher makes a separate, patched copy in the folder below; your Steam copy is only read, never changed.";
        StatusLine = results.All(result => result.Status == CheckStatus.Ok) ? "Everything looks fine." : "";
    }

    private void EnterReady()
    {
        Phase = Phase.Ready;
        var version = _layout.InstalledVersion;
        InstalledText = version is null ? "" : $"Version {version} is installed";
        Headline = "Ready to play";
        Lead = "The game is installed and every file was checked when it was made.";
        StatusLine = "";
        SimulationNote = _layout.HasSimulationCheck ? "" : "The simulation check is not part of this package.";
    }

    private async Task InstallAsync()
    {
        if (_release is null) return;
        await RunEngineAsync(release: _release, folder: _layout.Root, verify: VerifySimulation, launcherToo: false);
    }

    private async Task UpdateAsync()
    {
        if (_offer.Release is not { } release) return;
        UpdateVisible = false;
        await RunEngineAsync(release, _layout.Root, _settings.VerifySimulation, launcherToo: true);
    }

    private async Task RunEngineAsync(ReleaseInfo release, string folder, bool verify, bool launcherToo)
    {
        _busy = true;
        _cancel = new CancellationTokenSource();
        SettingsOpen = false;
        PrepareSteps(verify);
        Phase = Phase.Installing;
        Headline = _launcherOnlyUpdate ? "Updating the launcher" : "Getting started";
        Lead = "";
        Progress = 0;
        ProgressText = "";
        try
        {
            // The first two steps are what the user just did on the page before.
            _stepList.Set(StepId.Checks, StepState.Done, "Everything is in order.");
            _stepList.Set(StepId.Folder, StepState.Done, folder);
            var result = _launcherOnlyUpdate ? EngineResult.Ok() : await _engine.InstallAsync(folder, release, verify, _cancel.Token);
            if (result.Succeeded)
            {
                _settings = _settings with { InstallFolder = folder };
                _settings.Save(AppInfo.SettingsPath);
                _layout = new InstallLayout(folder);
                Raise(nameof(InstallFolder));
                _stepList.MarkAllDone();
                if (launcherToo && await TrySelfUpdateAsync(release)) return;
                EnterReady();
                StatusLine = result.Message;
                _offer = new UpdateOffer(UpdateKind.None, null, null, null);
            }
            else if (result.Cancelled)
            {
                await ShowChecksAsync();
                StatusLine = result.Message;
            }
            else
            {
                if (result.FailedStep is { } step) _stepList.Fail(step, result.Message);
                Fail(result.Message);
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            LauncherLog.Write("unexpected: " + exception);
            Fail("Something unexpected went wrong: " + exception.Message);
        }
        finally
        {
            _busy = false;
            _cancel?.Dispose();
            _cancel = null;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private async Task RepairAsync()
    {
        _busy = true;
        _cancel = new CancellationTokenSource();
        PrepareSteps(false);
        Phase = Phase.Installing;
        Headline = "Checking every file";
        try
        {
            _stepList.Set(StepId.Checks, StepState.Done);
            _stepList.Set(StepId.Folder, StepState.Done, _layout.Root);
            var result = await _engine.RepairAsync(_layout.Root, _cancel.Token);
            if (result.Succeeded) { _stepList.MarkAllDone(); EnterReady(); StatusLine = result.Message; }
            else if (result.Cancelled) EnterReady();
            else { if (result.FailedStep is { } step) _stepList.Fail(step, result.Message); Fail(result.Message); }
        }
        finally
        {
            _busy = false;
            _cancel.Dispose();
            _cancel = null;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    private void AskUninstall()
    {
        SettingsOpen = false;
        _confirmed = () =>
        {
            var result = _engine.Uninstall(_layout.Root);
            LauncherLog.Write("uninstall: " + result.Message);
            if (result.Succeeded) { _ = BeginAsync(); StatusLine = result.Message; }
            else Fail(result.Message);
        };
        ConfirmText = $"Remove the game from {_layout.Root}? Your saved games and settings are kept. Your Steam copy is not touched.";
    }

    private async Task<bool> TrySelfUpdateAsync(ReleaseInfo release)
    {
        if (release.Version <= AppInfo.Version || release.Launcher is null) return false;
        Headline = "Updating the launcher";
        Lead = "A new launcher comes with this version.";
        var current = Environment.ProcessPath;
        if (current is null || !current.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(current).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)) return false;
        var (path, message) = await SelfUpdate.FetchAsync(release, Path.GetDirectoryName(current)!, _http, null, default);
        if (path is null) { LauncherLog.Write("launcher update skipped: " + message); return false; }
        if (!SelfUpdate.StartSuccessor(path, current)) return false;
        Application.Current.Shutdown();
        return true;
    }

    private void PrepareSteps(bool verify)
    {
        _stepList = new StepList(verify);
        Steps.Clear();
        var number = 1;
        foreach (var step in _stepList.Steps) Steps.Add(new StepItem(number++, step));
        _stepList.Changed += status => _dispatcher.BeginInvoke(() => Steps.FirstOrDefault(item => item.Id == status.Id)?.Update(status));
    }

    private void OnEngineProgress(EngineProgress progress)
    {
        if (_stepList.Steps.Count == 0) return;
        _stepList.Apply(progress);
        Headline = progress.Headline;
        Lead = progress.Detail;
        Progress = progress.Fraction ?? Progress;
        ProgressText = progress.Fraction is { } fraction ? string.Create(CultureInfo.InvariantCulture, $"{(int)(fraction * 100)} %") : "";
    }

    private void Fail(string message)
    {
        LauncherLog.Write("failed: " + message);
        Failure = message;
        Headline = "That did not work";
        Phase = Phase.Failed;
    }

    // ---- updates -----------------------------------------------------------------------------------------------------------------------------

    private async Task CheckForUpdateAsync()
    {
        var check = await _engine.CheckLatestAsync();
        if (!check.Succeeded || check.Release is not { } release)
        {
            if (check.Status != ReleaseCheckStatus.NoRelease) StatusLine = check.Message;
            return;
        }
        _offer = UpdateOffer.Evaluate(_layout.InstalledVersion, release, _settings.SkippedVersion);
        _launcherOnlyUpdate = false;
        if (_offer.IsAvailable)
        {
            UpdateText = _offer.Banner;
            Raise(nameof(UpdateNotes));
            UpdateVisible = true;
        }
        else if (release.Version > AppInfo.Version && release.Launcher is not null && _layout.InstalledVersion is { } installed && installed >= release.Version)
        {
            // The client is current but this launcher is older than the release: offer the launcher alone.
            _offer = new UpdateOffer(UpdateKind.Available, AppInfo.Version, release.Version, release);
            _launcherOnlyUpdate = true;
            UpdateText = $"A new launcher is available (version {release.Version}, you have {AppInfo.Version}).";
            UpdateVisible = true;
        }
        LauncherLog.Write($"update check: latest {release.Version}, installed {_layout.InstalledVersion}, offered {UpdateVisible}");
    }

    private void SkipUpdate()
    {
        if (_offer.Latest is { } latest)
        {
            _settings = _settings with { SkippedVersion = latest.ToString() };
            SaveSettings();
        }
        UpdateVisible = false;
    }

    // ---- small things ------------------------------------------------------------------------------------------------------------------------

    private void Play()
    {
        var error = LauncherEngine.Play(_layout);
        if (error is not null) { StatusLine = error; return; }
        LauncherLog.Write("game started");
        Application.Current.Shutdown();
    }

    private void ChangeFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose the folder for the game", InitialDirectory = Directory.Exists(_layout.Root) ? _layout.Root : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) };
        if (dialog.ShowDialog() != true) return;
        var verdict = FolderRules.Judge(dialog.FolderName, _probe.FindSteamCopy().Libraries);
        var folder = verdict.Folder;
        if (!verdict.Acceptable && verdict.Message.Contains("other files", StringComparison.Ordinal))
        {
            // The user picked a folder that holds other things: the game gets a folder of its own inside it.
            var own = FolderRules.Judge(FolderRules.OwnSubfolder(dialog.FolderName), _probe.FindSteamCopy().Libraries);
            if (own.Acceptable) { verdict = own; folder = own.Folder; }
        }
        if (!verdict.Acceptable) { StatusLine = verdict.Message; Fail(verdict.Message); return; }
        _settings = _settings with { InstallFolder = folder };
        SaveSettings();
        LauncherLog.Write("install folder: " + folder);
        _ = BeginAsync();
    }

    private void SaveSettings()
    {
        _settings = _settings with { VerifySimulation = _verifySimulation, CheckForUpdates = _checkForUpdates };
        _settings.Save(AppInfo.SettingsPath);
    }

    private static void Open(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false })?.Dispose();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException) { LauncherLog.Write("open failed: " + exception.Message); }
    }

    // ---- a look without a machine: scripts/preview pages for the screenshot --------------------------------------------------------------

    /// <summary>Fills the window with invented data for a page: <c>welcome</c>, <c>checks-bad</c>, <c>installing</c>, <c>ready</c>, <c>update</c>, <c>failed</c>, <c>settings</c>. No network, no disk.</summary>
    public void LoadPreview(string name)
    {
        _preview = true;
        CheckResult[] good =
        [
            new("windows", "Windows", CheckStatus.Ok, "64-bit Windows, build 26100."),
            new("disk", "Disk space", CheckStatus.Ok, "212 GB free where the game goes."),
            new("graphics", "Graphics", CheckStatus.Ok, "NVIDIA GeForce RTX 3060"),
            new("steam", "Your copy of the game", CheckStatus.Ok, "Found in D:\\SteamLibrary\\steamapps\\common\\Command & Conquer Generals - Zero Hour. Steam's files are only read, never changed."),
            new("folder", "Install folder", CheckStatus.Ok, "C:\\Users\\player\\CnCNew"),
        ];
        var bad = good.ToArray();
        bad[3] = new CheckResult("steam", "Your copy of the game", CheckStatus.Failed, "Your Steam copy is set to german. Only the English version is supported: in Steam, open the game's Properties > Language, choose English, let Steam finish, and press Check again.");
        bad[2] = new CheckResult("graphics", "Graphics", CheckStatus.Warning, "Windows uses its basic display driver. Install the driver of your graphics card, or the game will be very slow.");
        Checks.Clear();
        foreach (var check in (name == "checks-bad" ? bad : good).Where(check => check.Id != "folder")) Checks.Add(new CheckItem(check));
        _layout = new InstallLayout("C:\\Users\\player\\CnCNew");
        Raise(nameof(InstallFolder));
        switch (name)
        {
            case "installing":
                PrepareSteps(true);
                _stepList.Set(StepId.Checks, StepState.Done, "Everything is in order.");
                _stepList.Set(StepId.Folder, StepState.Done, "C:\\Users\\player\\CnCNew");
                _stepList.Set(StepId.Download, StepState.Done, "Version 0.2.0");
                _stepList.Set(StepId.MakeGame, StepState.Running, "42 %");
                Phase = Phase.Installing;
                Headline = "Making the game files";
                Lead = "42 %  -  every file of your Steam copy is read, the community patch is laid over it, and the result is written to the install folder.";
                Progress = 0.42;
                ProgressText = "42 %";
                break;
            case "ready":
            case "update":
            case "settings":
                InstalledText = "Version 0.1.0 is installed";
                Phase = Phase.Ready;
                Headline = "Ready to play";
                Lead = "The game is installed and every file was checked when it was made.";
                SimulationNote = "The simulation check is not part of this package.";
                if (name == "update") { UpdateText = "Version 0.2.0 is available (you have 0.1.0)."; UpdateVisible = true; }
                if (name == "settings") SettingsOpen = true;
                break;
            case "failed":
                PrepareSteps(false);
                _stepList.Set(StepId.Checks, StepState.Done);
                _stepList.Set(StepId.Folder, StepState.Done);
                _stepList.Set(StepId.Download, StepState.Done);
                _stepList.Set(StepId.MakeGame, StepState.Failed, "");
                Failure = "Your Steam copy of the game is not the version this project supports (the English Steam release of Zero Hour, fully updated). In Steam, set the game's language to English and use Properties > Installed Files > Verify integrity, then try again.\n\nchanged: Data/INI/Object.ini";
                Headline = "That did not work";
                Phase = Phase.Failed;
                break;
            default:
                ReleaseLine = "Version 0.2.0  -  download about 214 MB";
                CanInstall = name != "checks-bad";
                Headline = "Install CnCNew";
                Lead = "CnCNew needs your own copy of the game from Steam. The launcher makes a separate, patched copy in the folder below; your Steam copy is only read, never changed.";
                Phase = Phase.Welcome;
                break;
        }
    }
}
