using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using upeko.Models;
using upeko.Services;

namespace upeko.ViewModels;

public partial class BotViewModel : ViewModelBase, IDisposable
{
    private readonly BotModel _bot = null!;
    private bool _isDownloading;
    private Process? _process;

    public BotModel Bot => _bot;
    public BotListViewModel Parent { get; } = null!;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsReady), nameof(IsMissing), nameof(ShowStartStop),
        nameof(CanUpdate), nameof(CanChangePath), nameof(StatusText), nameof(UpdateButtonText),
        nameof(IsUpdateAvailable), nameof(IsBotDownloaded))]
    private MainActivityState _state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanChangePath))]
    private bool _isProgressVisible;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _progressTitle = string.Empty;

    [ObservableProperty]
    private string _progressDetail = string.Empty;

    [ObservableProperty]
    private string _progressPercent = "0%";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateButtonText))]
    private bool _isCheckingForUpdates;

    [ObservableProperty]
    private string? _diskSpaceInfo;

    [ObservableProperty]
    private bool _isEditingName;

    [ObservableProperty]
    private string _editedName = string.Empty;

    [ObservableProperty]
    private string _downloadSpeed = string.Empty;

    [ObservableProperty]
    private string _downloadEta = string.Empty;

    [ObservableProperty]
    private string _downloadBytes = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAvatar))]
    private Bitmap? _avatarImage;

    public string BotPath
    {
        get => Bot.PathUri ?? string.Empty;
        set
        {
            _bot.PathUri = value;
            Parent.UpdateBot(_bot);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ExecutablePath));
        }
    }

    public string Name => Bot.Name;
    public string DisplayInitial => string.IsNullOrEmpty(Name) ? "?" : Name[..1].ToUpper();
    public bool HasAvatar => AvatarImage is not null;
    public string ExecutablePath => Path.Combine(BotPath, PlatformSpecific.GetExecutableName());
    public bool IsRunning => State == MainActivityState.Running;
    public bool IsReady => State == MainActivityState.Runnable || State == MainActivityState.Updatable;
    public bool IsMissing => State == MainActivityState.Downloadable;
    public bool ShowStartStop => IsRunning || IsReady;
    public bool CanUpdate => State == MainActivityState.Updatable;
    public bool CanChangePath => !IsRunning && !IsProgressVisible;
    public bool IsUpdateAvailable => IsBotDownloaded && UpdateChecker.Instance.IsUpdateAvailable(Bot?.Version);
    public bool IsBotDownloaded => !string.IsNullOrWhiteSpace(Bot?.Version);
    public string UpdateButtonText => IsCheckingForUpdates ? Loc["BotView_Checking"] : IsUpdateAvailable ? Loc["BotView_UpdateButton"] : Loc["BotView_CheckUpdates"];

    public string StatusText => State switch
    {
        MainActivityState.Running => Loc["Status_Running"],
        MainActivityState.Runnable => Loc["Status_Ready"],
        MainActivityState.Updatable => Loc["Status_UpdateAvailable"],
        MainActivityState.Downloadable => Loc["Status_NotDownloaded"],
        _ => Loc["Status_Unknown"]
    };

    public string? Version => Bot?.Version;

    public BotViewModel()
    {
        UpdateChecker.Instance.OnNewVersionFound += OnNewVersionFound;
        LocalizationService.Instance.LanguageChanged += OnLanguageChanged;
    }

    public BotViewModel(BotListViewModel parent, BotModel model) : this()
    {
        _bot = model;
        Parent = parent;
        LoadAvatar();
        _ = InitAsync();
    }

    public void Dispose()
    {
        UpdateChecker.Instance.OnNewVersionFound -= OnNewVersionFound;
        LocalizationService.Instance.LanguageChanged -= OnLanguageChanged;
        AvatarImage?.Dispose();
        AvatarImage = null;
    }

    private void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(UpdateButtonText));
        OnPropertyChanged(nameof(Loc));
        _ = UpdateDiskSpaceInfoAsync();
    }

    private void OnNewVersionFound(ReleaseModel release)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateCurrentActivity();
            OnPropertyChanged(nameof(IsUpdateAvailable));
            OnPropertyChanged(nameof(UpdateButtonText));
            _ = UpdateDiskSpaceInfoAsync();
        });
    }

    private async Task InitAsync()
    {
        await ReloadVersionFromPathAsync();
        if (UpdateChecker.Instance.LatestRelease is null)
            await UpdateChecker.Instance.CheckForUpdatesAsync();
        await UpdateDiskSpaceInfoAsync();
    }

    [RelayCommand]
    private void GoBack()
    {
        Parent?.RemoveNavigation();
    }

    [RelayCommand]
    private void StartBot()
    {
        if (!IsReady)
            return;

        if (!File.Exists(ExecutablePath))
        {
            _ = ReloadVersionFromPathAsync();
            return;
        }

        Log.Information("Starting bot {BotName}", Bot.Name);
        Process process;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = ExecutablePath,
                WorkingDirectory = BotPath,
                UseShellExecute = false,
            };
            psi.Environment["PATH"] = PlatformSpecific.GetToolSearchPath();
            process = Process.Start(psi) ?? throw new InvalidOperationException("The process did not start.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start bot {BotName}", Bot.Name);
            Parent.ShowToast(string.Format(Loc["Toast_StartFailed"], Name, ex.Message), ToastKind.Error);
            return;
        }

        _process = process;
        _bot.WasRunning = true;
        Parent.UpdateBot(_bot);
        UpdateCurrentActivity();

        _ = WatchProcessAsync(process);
    }

    private async Task WatchProcessAsync(Process process)
    {
        try
        {
            await process.WaitForExitAsync();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Lost track of bot process {BotName}", Bot.Name);
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Stop and exit clear _process themselves; only an exit nobody asked for gets here.
            if (_process != process)
                return;

            _process = null;
            _bot.WasRunning = false;
            Parent.UpdateBot(_bot);
            UpdateCurrentActivity();

            int? exitCode = null;
            try { exitCode = process.ExitCode; } catch { }
            Log.Warning("Bot {BotName} exited on its own with code {ExitCode}", Bot.Name, exitCode);
            Parent.ShowToast(string.Format(Loc["Toast_BotExited"], Name), ToastKind.Error);
        });

        process.Dispose();
    }

    [RelayCommand]
    private void StopBot()
    {
        if (!IsRunning)
            return;

        Log.Information("Stopping bot {BotName}", Bot.Name);
        _bot.WasRunning = false;
        Parent.UpdateBot(_bot);
        KillProcess();
        UpdateCurrentActivity();
    }

    /// <summary>
    /// Stops the bot because upeko is quitting. WasRunning stays set, so the bot starts again
    /// the next time upeko opens.
    /// </summary>
    public void StopForExit()
    {
        if (_isDownloading)
            UpdateChecker.Instance.CancelDownload();

        if (!IsRunning)
            return;

        Log.Information("Stopping bot {BotName} because upeko is exiting", Bot.Name);
        KillProcess();
        UpdateCurrentActivity();
    }

    /// <summary>
    /// Stops the bot and any download before the instance is removed from upeko.
    /// </summary>
    public void PrepareForRemoval()
    {
        if (_isDownloading)
            UpdateChecker.Instance.CancelDownload();

        if (IsRunning)
            StopBot();

        Dispose();
    }

    private void KillProcess()
    {
        var process = _process;
        _process = null;
        try { process?.Kill(entireProcessTree: true); }
        catch (Exception ex) { Log.Warning(ex, "Failed to kill bot process {BotName}", Bot.Name); }
    }

    [RelayCommand]
    private async Task UpdateBot()
    {
        if (!IsUpdateAvailable && IsBotDownloaded)
            await CheckForUpdatesAsync();
        else
            await DownloadAndInstallBotAsync();
    }

    [RelayCommand]
    private Task DownloadBot() => DownloadAndInstallBotAsync();

    [RelayCommand]
    private void CancelDownload()
    {
        Log.Information("Cancelling download for {BotName}", Bot.Name);
        UpdateChecker.Instance.CancelDownload();
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (string.IsNullOrWhiteSpace(BotPath))
            return;

        var dataFolder = Path.Combine(BotPath, "data");
        TryOpen(Directory.Exists(dataFolder) ? dataFolder : BotPath);
    }

    [RelayCommand]
    private void OpenCreds()
    {
        if (string.IsNullOrWhiteSpace(BotPath))
            return;

        var credsFile = Path.Combine(BotPath, "data", "creds.yml");
        var credsExampleFile = Path.Combine(BotPath, "data", "creds_example.yml");
        try
        {
            if (!File.Exists(credsFile))
            {
                if (!File.Exists(credsExampleFile))
                {
                    Parent.ShowToast(Loc["Toast_CredsMissing"], ToastKind.Error);
                    return;
                }

                File.Copy(credsExampleFile, credsFile);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create creds.yml for {BotName}", Bot.Name);
            Parent.ShowToast(string.Format(Loc["Toast_OpenFailed"], ex.Message), ToastKind.Error);
            return;
        }

        TryOpen(Path.GetFullPath(credsFile), notepadFallback: true);
    }

    private void TryOpen(string path, bool notepadFallback = false)
    {
        try
        {
            PlatformSpecific.Open(path);
        }
        catch (Exception ex) when (notepadFallback && OperatingSystem.IsWindows() && ex is Win32Exception)
        {
            // Windows has no program for .yml files by default.
            Process.Start("notepad.exe", $"\"{path}\"")?.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open {Path}", path);
            Parent.ShowToast(string.Format(Loc["Toast_OpenFailed"], ex.Message), ToastKind.Error);
        }
    }

    [RelayCommand]
    private async Task BrowsePath()
    {
        if (!CanChangePath)
            return;

        var topLevel = TopLevel.GetTopLevel(App.MainWindow);
        if (topLevel == null) return;

        var start = Directory.Exists(BotPath)
            ? await topLevel.StorageProvider.TryGetFolderFromPathAsync(BotPath)
            : null;

        var result = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Loc["BotView_SelectBotPath"],
            AllowMultiple = false,
            SuggestedStartLocation = start,
        });

        if (result.Count == 0)
            return;

        var path = result[0].Path.LocalPath;
        if (path == BotPath)
            return;

        BotPath = path;
        Log.Information("Changed path of {BotName} to {BotPath}", Bot.Name, path);
        await ReloadVersionFromPathAsync();
        await UpdateDiskSpaceInfoAsync();
    }

    [RelayCommand]
    private async Task ChangeAvatar()
    {
        var topLevel = TopLevel.GetTopLevel(App.MainWindow);
        if (topLevel == null) return;

        var result = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Loc["BotView_SelectAvatarImage"],
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType(Loc["BotView_ImageFiles"])
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.gif", "*.webp" }
                }
            }
        });

        if (result.Count > 0)
        {
            _bot.IconUri = new Uri(result[0].Path.LocalPath).ToString();
            Parent?.UpdateBot(Bot);
            LoadAvatar();
        }
    }

    private void LoadAvatar()
    {
        var old = AvatarImage;
        AvatarImage = null;
        old?.Dispose();

        if (string.IsNullOrWhiteSpace(Bot.IconUri))
            return;

        try
        {
            var path = new Uri(Bot.IconUri).LocalPath;
            if (!File.Exists(path))
                return;

            using var stream = File.OpenRead(path);
            AvatarImage = Bitmap.DecodeToWidth(stream, 160);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not load avatar for {BotName}", Bot.Name);
        }
    }

    [RelayCommand]
    private void ShowDeleteModal()
    {
        Parent?.RequestDelete(Bot.Name);
    }

    [RelayCommand]
    private void EditName()
    {
        EditedName = Name;
        IsEditingName = true;
    }

    [RelayCommand]
    private void SaveName()
    {
        var newName = EditedName.Trim();
        if (!string.IsNullOrWhiteSpace(newName) && newName != Name)
        {
            _bot.Name = newName;
            Parent.UpdateBot(_bot);
            OnPropertyChanged(nameof(Name));
            OnPropertyChanged(nameof(DisplayInitial));
        }

        IsEditingName = false;
    }

    [RelayCommand]
    private void CancelEditName()
    {
        EditedName = Name;
        IsEditingName = false;
    }

    public async Task ReloadVersionFromPathAsync()
    {
        if (string.IsNullOrWhiteSpace(BotPath))
            return;

        if (!File.Exists(ExecutablePath))
        {
            Bot.Version = null;
            OnPropertyChanged(nameof(Version));
            UpdateCurrentActivity();
            return;
        }

        var exe = ExecutablePath;
        try
        {
            var version = await Task.Run(async () =>
            {
                using var p = Process.Start(new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "--version",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                });

                if (p is null) return null;

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    var info = await p.StandardOutput.ReadToEndAsync(cts.Token);
                    await p.WaitForExitAsync(cts.Token);
                    return string.IsNullOrWhiteSpace(info) ? null : info.Trim();
                }
                catch (OperationCanceledException)
                {
                    try { p.Kill(entireProcessTree: true); } catch { }
                    throw;
                }
            });

            Bot.Version = version;
            if (version is not null)
                Log.Information("Detected bot version {Version} for {BotName}", version, Bot.Name);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Could not read the version of {BotName}", Bot.Name);
            Bot.Version = null;
        }

        OnPropertyChanged(nameof(Version));
        UpdateCurrentActivity();
    }

    private void UpdateCurrentActivity()
    {
        if (_process != null)
            State = MainActivityState.Running;
        else if (!IsBotDownloaded)
            State = MainActivityState.Downloadable;
        else if (IsUpdateAvailable)
            State = MainActivityState.Updatable;
        else
            State = MainActivityState.Runnable;
    }

    private async Task CheckForUpdatesAsync()
    {
        IsCheckingForUpdates = true;
        try
        {
            var error = await UpdateChecker.Instance.CheckForUpdatesAsync();
            UpdateCurrentActivity();
            OnPropertyChanged(nameof(UpdateButtonText));
            OnPropertyChanged(nameof(IsUpdateAvailable));
            _ = UpdateDiskSpaceInfoAsync();

            if (error is not null)
                Parent.ShowToast(string.Format(Loc["Toast_UpdateCheckFailed"], error), ToastKind.Error);
            else if (!IsUpdateAvailable)
                Parent.ShowToast(Loc["Toast_UpToDate"], ToastKind.Success);
        }
        finally
        {
            IsCheckingForUpdates = false;
        }
    }

    private async Task UpdateDiskSpaceInfoAsync()
    {
        try
        {
            var botPath = BotPath;
            var info = await Task.Run(() =>
            {
                var asset = UpdateChecker.Instance.FindAssetForThisPlatform();
                if (asset is null || asset.Size <= 0)
                    return null;

                var availableBytes = DiskSpace.GetAvailableBytes(string.IsNullOrWhiteSpace(botPath) ? "." : botPath);
                if (availableBytes is null)
                    return null;

                return new { Required = DiskSpace.RequiredForInstall(asset.Size), Available = availableBytes.Value };
            });

            DiskSpaceInfo = info is null
                ? null
                : string.Format(Loc["BotView_DiskSpace"], DiskSpace.Format(info.Required), DiskSpace.Format(info.Available));
        }
        catch
        {
            DiskSpaceInfo = null;
        }
    }

    private async Task DownloadAndInstallBotAsync()
    {
        if (_isDownloading)
            return;

        if (IsRunning)
        {
            Parent.ShowToast(Loc["Toast_StopBeforeUpdate"], ToastKind.Info);
            return;
        }

        if (UpdateChecker.Instance.IsDownloading)
        {
            Parent.ShowToast(Loc["Error_DownloadInProgress"], ToastKind.Info);
            return;
        }

        Log.Information("Starting download for {BotName} at {BotPath}", Bot.Name, BotPath);
        _isDownloading = true;
        ProgressValue = 0;
        ProgressPercent = "0%";
        ProgressTitle = Loc["Progress_Downloading"];
        ProgressDetail = Loc["Progress_PleaseWait"];
        DownloadSpeed = string.Empty;
        DownloadEta = string.Empty;
        DownloadBytes = string.Empty;
        IsProgressVisible = true;

        DownloadResult result;
        try
        {
            result = await UpdateChecker.Instance.DownloadAndInstallBotAsync(Bot.Name, BotPath, OnDownloadProgress);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Download failed for {BotName}", Bot.Name);
            result = DownloadResult.Failed(ex.Message);
        }

        _isDownloading = false;
        IsProgressVisible = false;

        if (result.Success)
        {
            Log.Information("Download completed for {BotName}", Bot.Name);
            await ReloadVersionFromPathAsync();
            Parent.ShowToast(string.Format(Loc["Toast_BotInstalled"], Name, Version), ToastKind.Success);
        }
        else if (result.Cancelled)
        {
            Log.Information("Download cancelled for {BotName}", Bot.Name);
            Parent.ShowToast(Loc["Toast_DownloadCancelled"], ToastKind.Info);
        }
        else
        {
            Log.Error("Download failed for {BotName}: {Error}", Bot.Name, result.Error);
            Parent.ShowToast(string.Format(Loc["Toast_DownloadFailed"], result.Error), ToastKind.Error);
        }

        UpdateCurrentActivity();
        _ = UpdateDiskSpaceInfoAsync();
    }

    private void OnDownloadProgress(DownloadProgressInfo info)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!_isDownloading) return;
            ProgressValue = info.Progress * 100;
            ProgressPercent = $"{(int)(info.Progress * 100)}%";
            ProgressTitle = info.Status;
            DownloadSpeed = info.BytesDownloaded > 0 ? FormatSpeed(info.SpeedBytesPerSec) : string.Empty;
            DownloadBytes = info.BytesDownloaded > 0 ? FormatProgressBytes(info.BytesDownloaded, info.TotalBytes) : string.Empty;
            DownloadEta = info.BytesDownloaded > 0 ? FormatEta(info.EtaSeconds) : string.Empty;
        });
    }

    private string FormatSpeed(double bytesPerSec)
    {
        if (bytesPerSec <= 0) return Loc["Progress_Stalled"];
        return bytesPerSec >= 1_048_576
            ? $"{bytesPerSec / 1_048_576:F1} MB/s"
            : $"{bytesPerSec / 1_024:F0} KB/s";
    }

    private static string FormatProgressBytes(long downloaded, long? total)
    {
        var dl = downloaded / 1_048_576.0;
        if (!total.HasValue) return $"{dl:F1} MB";
        var tot = total.Value / 1_048_576.0;
        return $"{dl:F1} / {tot:F1} MB";
    }

    private string FormatEta(double? etaSeconds)
    {
        if (!etaSeconds.HasValue) return Loc["Progress_Calculating"];
        var s = (int)etaSeconds.Value;
        if (s < 0) return Loc["Progress_Calculating"];
        if (s < 60) return $"{s}s";
        if (s < 3600) return $"{s / 60}m {s % 60}s";
        return $"{s / 3600}h {s % 3600 / 60}m";
    }
}
